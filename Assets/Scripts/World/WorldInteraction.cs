using System.Collections.Generic;
using AnimalGame.MapTest;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace AnimalGame.World
{
    [System.Flags]
    public enum WorldInteractionKind
    {
        None = 0,
        Collision = 1 << 0,
        Grabbable = 1 << 1,
        BodyCollision = 1 << 2, // Blocks bodies but not the mechanical arm.
        Pushable = 1 << 3,       // Add to Collision or BodyCollision on the same component.
        Climbable = 1 << 4
    }
    public enum RecyclableSize { Small, Medium, Big }

    // Multiple components are intentional: a prop can have separate solid and grab bounds.
    [ExecuteAlways]
    public class WorldInteraction : MonoBehaviour
    {
        private static readonly HashSet<WorldInteraction> active = new();
        public static IEnumerable<WorldInteraction> Active => active;
        private static readonly List<WorldInteraction> hierarchyItems = new();
        // SPATIAL INDEX: kind, box, sprite, localCenter and localSize affect the cache. Use the
        // public setters at runtime; direct/serialized writes require MarkSpatialDirty().
        [SerializeField] private WorldInteractionKind kind = WorldInteractionKind.Collision;
        // Changes to the referenced collider/renderer (including their transforms,
        // sprite assets, draw mode, size, or bounds) require notifying every dependent
        // WorldInteraction, even when the source lives outside this object's hierarchy.
        [SerializeField] private BoxCollider2D box;
        [SerializeField] private SpriteRenderer sprite;
        [SerializeField] private Vector2 localCenter;
        [SerializeField] private Vector2 localSize = Vector2.one;
        [Tooltip("Circular climbable radius in local units. Uses the largest XY scale in map coordinates; remains circular.")]
        [SerializeField, Min(0.001f)] private float climbableRadius = 1f;
        [Tooltip("Virtual slope strength. 1 is the code-defined maximum safe climbing experience.")]
        [SerializeField, Range(0f, 1f)] private float slopeStrength01 = 0.5f;
        [Tooltip("Flat top radius divided by the outer radius. Endpoints are excluded.")]
        [SerializeField, Range(0.001f, 0.999f)] private float topRadiusRatio01 = 0.5f;
        [Tooltip("Retained movement speed. Whole Area applies this throughout contact; otherwise only entry is resisted. 1 disables resistance.")]
        [SerializeField, Range(0.05f, 1f)] private float climbableEntrySpeedMultiplier = 1f;
        [Tooltip("Keep resistance active on the flat top and until completely outside this prop.")]
        [SerializeField] private bool climbableAffectsWholeArea;
        [Tooltip("Use the robot's body circle for contact, rather than only its centre.")]
        [SerializeField] private bool climbableUseBodyOverlap;
        [Tooltip("Maximum seconds to establish the entry speed cap. Short inbound bands finish the blend sooner.")]
        [SerializeField, Min(0.001f)] private float climbableEntryBlendDuration = 0.08f;
        [Tooltip("Scales this prop's virtual landing and automatic climbing camera feedback.")]
        [SerializeField, Range(0f, 1f)] private float climbableCameraMultiplier = 1f;
        [Tooltip("Scales this prop's virtual landing controller pulse.")]
        [SerializeField, Range(0f, 1f)] private float climbableRumbleMultiplier = 1f;
        [Tooltip("Duration relative to the normal virtual landing pulse.")]
        [SerializeField, Range(0.05f, 1f)] private float climbableLandingDurationMultiplier = 1f;
        public float ClimbableRadius { get => Mathf.Max(0.001f, climbableRadius); set { climbableRadius = Mathf.Max(0.001f, value); MarkSpatialDirty(); } }
        public float SlopeStrength01 { get => Mathf.Clamp01(slopeStrength01); set => slopeStrength01 = Mathf.Clamp01(value); }
        public float TopRadiusRatio01 { get => Mathf.Clamp(topRadiusRatio01, 0.001f, 0.999f); set => topRadiusRatio01 = Mathf.Clamp(value, 0.001f, 0.999f); }
        public float ClimbableEntrySpeedMultiplier { get => Mathf.Clamp(climbableEntrySpeedMultiplier, 0.05f, 1f); set => climbableEntrySpeedMultiplier = Mathf.Clamp(value, 0.05f, 1f); }
        public bool ClimbableAffectsWholeArea { get => climbableAffectsWholeArea; set => climbableAffectsWholeArea = value; }
        public bool ClimbableUseBodyOverlap { get => climbableUseBodyOverlap; set => climbableUseBodyOverlap = value; }
        public float ClimbableEntryBlendDuration { get => Mathf.Max(0.001f, climbableEntryBlendDuration); set => climbableEntryBlendDuration = Mathf.Max(0.001f, value); }
        public float ClimbableCameraMultiplier { get => Mathf.Clamp01(climbableCameraMultiplier); set => climbableCameraMultiplier = Mathf.Clamp01(value); }
        public float ClimbableRumbleMultiplier { get => Mathf.Clamp01(climbableRumbleMultiplier); set => climbableRumbleMultiplier = Mathf.Clamp01(value); }
        public float ClimbableLandingDurationMultiplier { get => Mathf.Clamp(climbableLandingDurationMultiplier, 0.05f, 1f); set => climbableLandingDurationMultiplier = Mathf.Clamp(value, 0.05f, 1f); }
        [SerializeField, Range(1, 2)] private int requiredHands = 1;
        [SerializeField] private bool recyclable = true;
        [SerializeField] private RecyclableSize size;
        [Tooltip("Robot target-speed multiplier while pushing this object. 1 means no speed loss; 0 stops driven movement.")]
        [SerializeField, Range(0f, 1f)] private float pushSpeedMultiplier = .6f;
        [Tooltip("Speed loss while this object is held; independent of body pushing.")]
        [SerializeField, Range(0f, 1f)] private float grabResistance;
        private System.Action onGrabbed;
        private System.Action onReleased;
        private System.Action onRecycled;
        public virtual WorldInteractionKind Kind => kind;
        public void SetKind(WorldInteractionKind value) { kind = value; MarkSpatialDirty(); }
        public BoxCollider2D BoxSource { get => box; set { box = value; MarkSpatialDirty(); } }
        public SpriteRenderer SpriteSource { get => sprite; set { sprite = value; MarkSpatialDirty(); } }
        public Vector2 LocalCenter { get => localCenter; set { localCenter = value; MarkSpatialDirty(); } }
        public Vector2 LocalSize { get => localSize; set { localSize = value; MarkSpatialDirty(); } }

        // SPATIAL INDEX: transform position/rotation/scale, ancestors, and scene
        // membership also affect the index. Wrappers notify siblings and descendants.
        public Vector3 WorldPosition
        {
            get => transform.position;
            set { transform.position = value; MarkHierarchySpatialDirty(transform); }
        }
        public Quaternion WorldRotation
        {
            get => transform.rotation;
            set { transform.rotation = value; MarkHierarchySpatialDirty(transform); }
        }
        public Vector3 LocalPosition
        {
            get => transform.localPosition;
            set { transform.localPosition = value; MarkHierarchySpatialDirty(transform); }
        }
        public Quaternion LocalRotation
        {
            get => transform.localRotation;
            set { transform.localRotation = value; MarkHierarchySpatialDirty(transform); }
        }
        public Vector3 LocalScale
        {
            get => transform.localScale;
            set { transform.localScale = value; MarkHierarchySpatialDirty(transform); }
        }
        public void SetWorldPositionAndRotation(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            MarkHierarchySpatialDirty(transform);
        }
        public void SetParent(Transform parent, bool worldPositionStays = true)
        {
            transform.SetParent(parent, worldPositionStays);
            MarkHierarchySpatialDirty(transform);
        }
        public void MoveToScene(Scene scene)
        {
            SceneManager.MoveGameObjectToScene(gameObject, scene);
            MarkHierarchySpatialDirty(transform);
        }
        public void SetLocalBounds(Vector2 center, Vector2 size)
        {
            localCenter = center;
            localSize = size;
            MarkSpatialDirty();
        }

        /// <summary>
        /// Call after external geometry/source edits or changes to overridden GetShape/Kind.
        /// Repeated calls are coalesced; the next query refreshes this component.
        /// This also queues safely from OnValidate, without reading Unity geometry.
        /// </summary>
        public void MarkSpatialDirty() => WorldInteractionQuery.MarkDirty(this);

        /// <summary>
        /// Main-thread notification after directly changing a Transform/ancestor or
        /// moving a hierarchy between scenes. Includes all interaction components on
        /// this object and descendants. External shape-source dependents must also
        /// be notified individually. This does not poll for movement.
        /// </summary>
        public static void MarkHierarchySpatialDirty(Transform root)
        {
            if (root == null) return;
            root.GetComponentsInChildren(true, hierarchyItems);
            foreach (WorldInteraction item in hierarchyItems) item.MarkSpatialDirty();
            hierarchyItems.Clear();
        }
        public int RequiredHands => Mathf.Clamp(requiredHands, 1, 2);
        public bool Recyclable => recyclable;
        public RecyclableSize Size => size;
        public float PushSpeedMultiplier => Mathf.Clamp01(pushSpeedMultiplier);
        public float GrabResistance => Mathf.Clamp01(grabResistance);
        public Object Owner { get; private set; }
        public virtual bool Available => isActiveAndEnabled && gameObject.activeInHierarchy;

        protected virtual void OnEnable()
        {
            if (gameObject.scene.IsValid()) { active.Add(this); MarkSpatialDirty(); }
        }
        protected virtual void OnDisable() { active.Remove(this); WorldInteractionQuery.Remove(this); Owner = null; }
        protected virtual void OnDestroy() { active.Remove(this); WorldInteractionQuery.Remove(this); }
        protected virtual void OnValidate() => MarkSpatialDirty();
        protected virtual void OnTransformParentChanged() => MarkHierarchySpatialDirty(transform);
        protected virtual void OnDidApplyAnimationProperties() => MarkHierarchySpatialDirty(transform);

        public virtual InteractionShape GetShape(MapTestSceneController map)
        {
            if ((Kind & WorldInteractionKind.Climbable) != 0)
            {
                Vector2 center = InteractionShape.ToQuery(transform.TransformPoint(localCenter), map);
                Vector2 right = InteractionShape.ToQuery(transform.TransformPoint(localCenter + Vector2.right), map) - center;
                Vector2 up = InteractionShape.ToQuery(transform.TransformPoint(localCenter + Vector2.up), map) - center;
                return InteractionShape.Capsule(center, center,
                    ClimbableRadius * Mathf.Max(right.magnitude, up.magnitude));
            }
            if (box != null)
                return InteractionShape.Box(box.transform, box.offset, box.size, map);
            if (sprite == null) sprite = GetComponent<SpriteRenderer>();
            if (sprite != null)
            {
                Bounds b = sprite.bounds;
                return InteractionShape.WorldBox(b.center, b.size, map);
            }
            return InteractionShape.Box(transform, localCenter, localSize, map);
        }

        public virtual bool TryGrab(Object owner)
        {
            if (!Available || (Kind & WorldInteractionKind.Grabbable) == 0 || Owner != null) return false;
            Owner = owner;
            onGrabbed?.Invoke();
            return Owner == owner && Available;
        }
        public virtual void Release(Object owner)
        {
            if (Owner != owner) return;
            Owner = null;
            onReleased?.Invoke();
        }
        public virtual void Recycle(Object owner)
        {
            if (Owner != owner || !recyclable) return;
            Owner = null;
            onRecycled?.Invoke();
            // Default completion for the animation placeholder. Override for inventory/pooling.
            gameObject.SetActive(false);
        }
    }
}
