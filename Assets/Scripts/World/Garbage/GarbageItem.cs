using AnimalGame.World;
using UnityEngine;

namespace AnimalGame.Garbage
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WorldInteraction))]
    public sealed class GarbageItem : MonoBehaviour
    {
        [SerializeField] private RecyclableSize size = RecyclableSize.Small;
        public RecyclableSize Size => size;
        public WorldInteraction Interaction { get; private set; }

        private void Awake() => Interaction = GetComponent<WorldInteraction>();
    }
}
