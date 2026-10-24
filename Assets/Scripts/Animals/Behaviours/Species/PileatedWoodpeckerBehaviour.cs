using AnimalGame.MapTest;
using UnityEngine;

namespace AnimalGame.Animals
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Animal Game/Animals/Pileated Woodpecker Behaviour")]
    public sealed class PileatedWoodpeckerBehaviour : TreeBirdBehaviour
    {
        [Header("Tree Category Weights (missing categories are renormalized)")]
        [SerializeField, Min(0f)] private float healthyTreeWeight = .15f;
        [SerializeField, Min(0f)] private float deadTreeWeight = .5f;
        [Header("Pecking Motion")]
        [SerializeField] private Transform visualRoot;
        [SerializeField, Min(0f)] private float peckAmplitudeMeters = .08f;
        [SerializeField, Min(.1f)] private float peckFrequencyHz = 5.5f;
        [SerializeField, Min(1)] private int pecksPerSoundWave = 3;
        private Vector3 baseVisualLocalPosition;
        private bool visualPositionCached;
        protected override bool StationaryTreeActivity => true;
        internal override bool FaceTrunkWhilePerched => true;
        protected override float GetTreeSelectionWeight(TreeHealthState health)
            => health == TreeHealthState.Dead ? deadTreeWeight : healthyTreeWeight;
        protected override bool CanRunSpeciesDaily(AnimalDailyBehaviourSettings settings)
            => settings.Behaviour == AnimalDailyBehaviourKind.PeckAtTree && IsUsableTree(CurrentTree) && CurrentTree.IsDead;
        protected override AnimalAction TryCreateSpeciesDailyAction(AnimalDailyBehaviourSettings settings)
            => CanRunSpeciesDaily(settings) ? new WoodpeckerPeckAction(this, settings.ChooseDuration()) : null;
        public override bool CanRunDailyForDebug(AnimalDailyBehaviourSettings settings, out string reason)
        {
            if (settings == null || settings.Behaviour != AnimalDailyBehaviourKind.PeckAtTree)
                return base.CanRunDailyForDebug(settings, out reason);
            bool valid = IsUsableTree(CurrentTree) && CurrentTree.IsDead
                && CurrentTree.ContainsActivityPoint(Agent.Map, Motor.CurrentMapPosition, Config.BodyRadiusMeters);
            reason = valid ? null : "Pecking requires the bird to be perched on a dead tree.";
            return valid;
        }
        internal float PeckFrequency => Mathf.Max(.1f, peckFrequencyHz);
        internal int PecksPerSoundWave => Mathf.Max(1, pecksPerSoundWave);

        public override void Initialize(AnimalAgent agent)
        { CacheVisualPosition(); base.Initialize(agent); }
        internal void ApplyPeckOffset(float elapsed)
        {
            if (visualRoot == null) return;
            CacheVisualPosition();
            Vector2 mapOffset = Motor.FacingMapDirection * peckAmplitudeMeters * Mathf.Sin(elapsed * PeckFrequency * Mathf.PI * 2f);
            Vector3 worldOffset = Agent.Map.MapPositionToWorld(Motor.CurrentMapPosition + mapOffset)
                - Agent.Map.MapPositionToWorld(Motor.CurrentMapPosition);
            Vector3 localOffset = visualRoot.parent != null ? visualRoot.parent.InverseTransformVector(worldOffset) : worldOffset;
            visualRoot.localPosition = baseVisualLocalPosition + localOffset;
        }
        internal Vector3 PeckContactPosition()
        {
            if (CurrentTree == null || !CurrentTree.TryGetMapPosition(Agent.Map, out Vector2 centre)) return transform.position;
            Vector2 side = (Motor.CurrentMapPosition - centre).normalized;
            Vector3 position = Agent.Map.MapPositionToWorld(centre + side * CurrentTree.PerchRadiusMeters);
            position.z = transform.position.z;
            return position;
        }
        internal void ResetVisualOffset()
        { if (visualRoot != null && visualPositionCached) visualRoot.localPosition = baseVisualLocalPosition; }
        private void CacheVisualPosition()
        {
            if (visualRoot == null || visualPositionCached) return;
            baseVisualLocalPosition = visualRoot.localPosition; visualPositionCached = true;
        }
        protected override void OnDisable() { ResetVisualOffset(); base.OnDisable(); }
#if UNITY_EDITOR
        public void ConfigureEditorDefaults(TreeHabitat tree, Transform authoredVisualRoot)
        {
            birthTree = tree; visualRoot = authoredVisualRoot;
            visualPositionCached = false; CacheVisualPosition();
        }
#endif
    }

    public sealed class WoodpeckerPeckAction : PerchAtTreeAction
    {
        private readonly PileatedWoodpeckerBehaviour woodpecker;
        private float peckElapsed;
        private int nextSoundStrike;
        public override AnimalState PhotoState => base.PhotoState == AnimalState.Moving ? AnimalState.Moving : AnimalState.Eating;
        public WoodpeckerPeckAction(PileatedWoodpeckerBehaviour bird, float duration) : base(bird, duration)
        { woodpecker = bird; nextSoundStrike = bird.PecksPerSoundWave; }
        protected override bool IsTreeValid() => base.IsTreeValid() && tree.IsDead;
        protected override void TickPerched(AnimalContext c, float dt)
        {
            peckElapsed += dt;
            woodpecker.ApplyPeckOffset(peckElapsed);
            int strikes = Mathf.Max(0, Mathf.FloorToInt(peckElapsed * woodpecker.PeckFrequency - .25f) + 1);
            if (strikes < nextSoundStrike) return;
            nextSoundStrike = (strikes / woodpecker.PecksPerSoundWave + 1) * woodpecker.PecksPerSoundWave;
            c.Agent.SoundEmitter?.EmitAt(AnimalSoundKind.Pecking, woodpecker.PeckContactPosition());
        }
        public override void Exit(AnimalContext c) { woodpecker.ResetVisualOffset(); base.Exit(c); }
    }
}
