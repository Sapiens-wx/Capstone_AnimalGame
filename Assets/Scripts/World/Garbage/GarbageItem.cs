using AnimalGame.World;
using UnityEngine;

namespace AnimalGame.Garbage
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WorldInteraction))]
    [RequireComponent(typeof(ScannableInteractable))]
    public sealed class GarbageItem : MonoBehaviour
    {
        [SerializeField] private RecyclableSize size = RecyclableSize.Small;
        public RecyclableSize Size => size;
        public WorldInteraction Interaction { get; private set; }

        private void Awake()
        {
            Interaction = GetComponent<WorldInteraction>();
            // Covers existing scene objects and dynamically created garbage as well as prefabs.
            if (!TryGetComponent(out ScannableInteractable scan))
                scan = gameObject.AddComponent<ScannableInteractable>();
            if (size == RecyclableSize.Small) scan.EnableSmallAreaFill();
        }
    }
}
