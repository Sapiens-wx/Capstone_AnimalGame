using UnityEngine;

namespace AnimalGame.Animals
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Animal Game/Animals/Mountain Goat Behaviour")]
    public sealed class MountainGoatBehaviour : GenericAnimalBehaviour
    {
        [SerializeField] private bool juvenile;
        [Tooltip("Assign only the artwork child, never the animal root.")]
        [SerializeField] private Transform visualRoot;
        [SerializeField] private SpriteRenderer bodyRenderer;
        [SerializeField] private Sprite juvenileSprite;
        [SerializeField, Range(.1f, 1f)] private float juvenileVisualScale = .65f;
        [Header("Slope Escape")]
        [SerializeField] private Vector2 replanIntervalSeconds = new Vector2(.7f, .9f);
        [SerializeField, Min(.3f)] private float escapeProbeMeters = 1.2f;
        [SerializeField, Min(.001f)] private float minimumRiseMeters = .025f;
        [SerializeField, Min(.1f)] private float minimumDescentTravelMeters = 1.5f;
        private bool visualApplied;
        public override bool IsJuvenile => juvenile;
        public override bool SupportsAggression => !juvenile;
        public float EscapeProbeMeters => Mathf.Max(Config.ArrivalDistanceMeters * 3f, escapeProbeMeters);
        public float MinimumRiseMeters => Mathf.Max(.001f, minimumRiseMeters);
        public float MinimumDescentTravelMeters => Mathf.Max(.1f, minimumDescentTravelMeters);
        public float ChooseReplanInterval() => Random.Range(Mathf.Max(.05f, replanIntervalSeconds.x), Mathf.Max(replanIntervalSeconds.x, replanIntervalSeconds.y));
        public override void Initialize(AnimalAgent agent)
        {
            base.Initialize(agent);
            if (visualApplied || !juvenile) return;
            if (agent.PlaceholderView != null) agent.PlaceholderView.ScaleVisuals(juvenileVisualScale);
            else if (visualRoot != null && visualRoot != transform) visualRoot.localScale *= juvenileVisualScale;
            if (bodyRenderer != null && juvenileSprite != null) bodyRenderer.sprite = juvenileSprite;
            visualApplied = true;
        }
        public override AnimalAction CreateEscapeAction() => Config.EscapeAction != null
            ? base.CreateEscapeAction() : new SlopeEscapeAction(this);
        public override void TickCurious(float dt)
        {
            if (Agent.Perception.PrimaryThreat != null)
                Motor.FaceMapPosition(AnimalGame.World.InteractionShape.ToQuery(Agent.Perception.PrimaryThreat.position, Agent.Map));
        }
    }
}
