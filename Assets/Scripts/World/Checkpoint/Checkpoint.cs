using UnityEngine;

namespace AnimalGame.World.Checkpoints
{
    [DisallowMultipleComponent, RequireComponent(typeof(WorldInteraction), typeof(SpriteRenderer))]
    public sealed class Checkpoint : MonoBehaviour
    {
        public bool Visited { get; private set; }
        public Sprite Icon => GetComponent<SpriteRenderer>().sprite;
        internal CheckpointManager Owner { get; set; }

        private void Awake() => GetComponent<WorldInteraction>().SetKind(WorldInteractionKind.Checkpoint);
        internal void ResetVisit() => Visited = false;
        internal void Visit() => Visited = true;
    }
}
