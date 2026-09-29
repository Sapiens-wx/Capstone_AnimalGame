using System.Collections.Generic;
using AnimalGame.MapTest;
using UnityEngine;
using UnityEngine.Events;

namespace AnimalGame.World
{
    public enum WorldInteractionKind { Collision, Grabbable }
    public enum RecyclableSize { Small, Medium }

    // Multiple components are intentional: a prop can have separate solid and grab bounds.
    [ExecuteAlways]
    public class WorldInteraction : MonoBehaviour
    {
        private static readonly HashSet<WorldInteraction> active = new();
        public static IEnumerable<WorldInteraction> Active => active;
        [SerializeField] private WorldInteractionKind kind;
        [SerializeField] private BoxCollider2D box;
        [SerializeField] private SpriteRenderer sprite;
        [SerializeField] private Vector2 localCenter;
        [SerializeField] private Vector2 localSize = Vector2.one;
        [SerializeField, Range(1, 2)] private int requiredHands = 1;
        [SerializeField] private bool recyclable = true;
        [SerializeField] private RecyclableSize size;
        [SerializeField] private UnityEvent onGrabbed = new();
        [SerializeField] private UnityEvent onReleased = new();
        [SerializeField] private UnityEvent onRecycled = new();
        public virtual WorldInteractionKind Kind => kind;
        public int RequiredHands => Mathf.Clamp(requiredHands, 1, 2);
        public bool Recyclable => recyclable;
        public RecyclableSize Size => size;
        public Object Owner { get; private set; }
        public virtual bool Available => isActiveAndEnabled && gameObject.activeInHierarchy;

        protected virtual void OnEnable()
        {
            if (gameObject.scene.IsValid()) active.Add(this);
        }
        protected virtual void OnDisable() { active.Remove(this); Owner = null; }
        protected virtual void OnDestroy() { active.Remove(this); }

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
            if (!Available || Kind != WorldInteractionKind.Grabbable || Owner != null) return false;
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
