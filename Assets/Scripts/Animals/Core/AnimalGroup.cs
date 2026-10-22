using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.Animals
{
    public sealed class AnimalGroup
    {
        private readonly List<AnimalAgent> members = new();
        private readonly Dictionary<AnimalAgent, int> reactionVersions = new();
        private float reactionDue = -1f;
        private Transform reactionThreat;
        public AnimalAgent Leader { get; private set; }
        public IReadOnlyList<AnimalAgent> Members => members;
        public bool WaitingForEscape
        {
            get
            {
                foreach (AnimalAgent member in members)
                    if (member != null && member.CurrentState == AnimalState.Fleeing) return true;
                return false;
            }
        }
        public void Add(AnimalAgent animal)
        {
            if (!members.Contains(animal)) members.Add(animal);
            animal.Runtime.Group = this;
            if (Leader == null) Leader = animal;
        }
        public void Remove(AnimalAgent animal)
        {
            members.Remove(animal);
            reactionVersions.Remove(animal);
            if (animal.Runtime != null && animal.Runtime.Group == this) animal.Runtime.Group = null;
            if (Leader == animal) Leader = members.Count == 0 ? null : members[Random.Range(0, members.Count)];
        }
        public void Alert(AnimalAgent source, Transform threat)
        {
            foreach (AnimalAgent member in members)
                if (member != null && member != source) member.NotifyThreat(threat, false);
        }
        public bool CanCalm()
        {
            foreach (AnimalAgent member in members)
                if (member != null && (member.Perception.HasThreatInRange || !member.IsCalmReady)) return false;
            return true;
        }
        public void Reaction(AnimalAgent source, Transform threat)
        {
            if (reactionDue >= 0f) return;
            reactionDue = source.Simulation.Time + .5f;
            reactionThreat = threat;
            reactionVersions.Clear();
            foreach (AnimalAgent member in members)
                if (member != null && member != source) reactionVersions[member] = member.ReactionVersion;
        }
        public void Tick(float time)
        {
            if (reactionDue < 0f || time < reactionDue) return;
            // Keep propagation independent of the original trigger's lifetime.
            foreach (var pair in reactionVersions)
                if (pair.Key != null && members.Contains(pair.Key) && pair.Key.ReactionVersion == pair.Value)
                    pair.Key.ForceGroupReaction(reactionThreat);
            reactionVersions.Clear();
            reactionDue = -1f;
        }
    }
}
