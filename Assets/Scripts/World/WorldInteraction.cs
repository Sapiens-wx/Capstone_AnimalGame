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
        Pushable = 1 << 3        // Add to Collision or BodyCollision on the same component.
    }
    public enum RecyclableSize { Small, Medium }

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
        [SerializeField, Range(1, 2)] private int requiredHands = 1;
        [SerializeField] private bool recyclable = true;
        [SerializeField] private RecyclableSize size;
        [Tooltip("Robot target-speed multiplier while pushing this object. 1 means no speed loss; 0 stops driven movement.")]
        [SerializeField, Range(0f, 1f)] private float pushSpeedMultiplier = .6f;
        [SerializeField] private UnityEvent onGrabbed = new();
        [SerializeField] private UnityEvent onReleased = new();
        [SerializeField] private UnityEvent onRecycled = new();
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
            onGrabbed.Invoke();
            return Owner == owner && Available;
        }
        public virtual void Release(Object owner)
        {
            if (Owner != owner) return;
            Owner = null;
            onReleased.Invoke();
        }
        public virtual void Recycle(Object owner)
        {
            if (Owner != owner || !recyclable) return;
            Owner = null;
            onRecycled.Invoke();
            // Default completion for the animation placeholder. Override for inventory/pooling.
            gameObject.SetActive(false);
        }
    }
}
