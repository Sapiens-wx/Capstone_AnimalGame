using AnimalGame.Save;
using UnityEngine;

namespace AnimalGame.World.Checkpoints
{
    [DisallowMultipleComponent, RequireComponent(typeof(WorldInteraction), typeof(SpriteRenderer))]
    public sealed class Checkpoint : MonoBehaviour
    {
        [Tooltip("Unique tower ID that unlocks this map region. Leave blank for a checkpoint reached on contact.")]
        [SerializeField] private string stationId;

        private bool reachedOnContact;

        public string StationId => stationId;
        public bool Visited => IsStationBound ? SaveSystem.IsStationSynced(stationId) : reachedOnContact;
        internal CheckpointManager Owner { get; set; }
        internal bool IsStationBound => !string.IsNullOrWhiteSpace(stationId);

        private void Awake() => GetComponent<WorldInteraction>().SetKind(WorldInteractionKind.Checkpoint);
        internal void ResetVisit() => reachedOnContact = false;

        internal bool TryVisitOnContact()
        {
            if (IsStationBound || reachedOnContact) return false;
            reachedOnContact = true;
            return true;
        }
    }
}
