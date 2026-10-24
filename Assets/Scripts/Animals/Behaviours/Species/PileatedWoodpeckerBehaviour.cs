using AnimalGame.MapTest;
using UnityEngine;

namespace AnimalGame.Animals
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Animal Game/Animals/Pileated Woodpecker Behaviour")]
    public sealed class PileatedWoodpeckerBehaviour : TreeBirdBehaviour
    {
        [Header("Pecking Motion")]
        [SerializeField] private Transform visualRoot;
        private Vector3 baseVisualLocalPosition;
        private bool visualPositionCached;
        protected override bool StationaryTreeActivity => true;
        internal override bool FaceTrunkWhilePerched => true;
        protected override float GetTreeSelectionWeight(TreeHealthState health)
            => health == TreeHealthState.Dead ? DailyFlightDefinition?.DeadTreeWeight ?? .5f : DailyFlightDefinition?.HealthyTreeWeight ?? .15f;

        public override void Initialize(AnimalAgent agent)
        { CacheVisualPosition(); base.Initialize(agent); }
        internal void ApplyPeckOffset(float elapsed, float amplitude, float frequency)
        {
            if (visualRoot == null) return;
            CacheVisualPosition();
            Vector2 mapOffset = Motor.FacingMapDirection * amplitude * Mathf.Sin(elapsed * frequency * Mathf.PI * 2f);
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
        private readonly float amplitude, frequency;
        private readonly int pecksPerSound;
        private float peckElapsed;
        private int nextSoundStrike;
        public override AnimalState PhotoState => base.PhotoState == AnimalState.Moving ? AnimalState.Moving : AnimalState.Eating;
        public WoodpeckerPeckAction(PileatedWoodpeckerBehaviour bird, float duration,
            float amplitude = .08f, float frequency = 5.5f, int pecksPerSound = 3, float approachSpeed = .35f)
            : base(bird, duration, approachSpeed, true, false)
        {
            woodpecker = bird; this.amplitude = Mathf.Max(0f, amplitude); this.frequency = Mathf.Max(.1f, frequency);
            this.pecksPerSound = Mathf.Max(1, pecksPerSound); nextSoundStrike = this.pecksPerSound;
        }
        protected override bool IsTreeValid() => base.IsTreeValid() && tree.IsDead;
        protected override void TickPerched(AnimalContext c, float dt)
        {
            peckElapsed += dt;
            woodpecker.ApplyPeckOffset(peckElapsed, amplitude, frequency);
            int strikes = Mathf.Max(0, Mathf.FloorToInt(peckElapsed * frequency - .25f) + 1);
            if (strikes < nextSoundStrike) return;
            nextSoundStrike = (strikes / pecksPerSound + 1) * pecksPerSound;
            c.Agent.SoundEmitter?.EmitAt(AnimalSoundKind.Pecking, woodpecker.PeckContactPosition());
        }
        public override void Exit(AnimalContext c) { woodpecker.ResetVisualOffset(); base.Exit(c); }
    }
}
