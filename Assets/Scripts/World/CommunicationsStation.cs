using AnimalGame.Animals.Communication;
using AnimalGame.Interaction;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using UnityEngine;

namespace AnimalGame.World
{
    [DefaultExecutionOrder(-1100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class CommunicationsStation : MonoBehaviour
    {
        [SerializeField] private GameObject interactionPrompt;
        [SerializeField] private bool useTutorialInteractionHint;

        [Header("Signal Broadcast")]
        [SerializeField] private Transform signalOrigin;
        [SerializeField] private Material signalMaterial;
        [SerializeField, Min(.1f)] private float signalInterval = 15f;
        [SerializeField, Min(.1f)] private float signalDuration = 6.6f;
        [SerializeField, Min(1f)] private float signalRadius = 40f;
        [SerializeField, Range(0f, 1f)] private float signalOpacity = .64f;

        private BoxCollider2D interactionTrigger;
        private RobotMover player;
        private EarthlineUI earthline;
        private bool interactWasHeld;
        private CommunicationsSignalWave signal;
        private bool towerDrivesSignal;

        internal static event System.Action<CommunicationsStation, RobotMover> SignalReceived;

        public BoxCollider2D InteractionTrigger => interactionTrigger;
        internal Transform SignalOrigin => signalOrigin;
        internal Material SignalMaterial => signalMaterial;
        internal float SignalInterval => signalInterval;
        internal float SignalDuration => signalDuration;
        internal float SignalRadius => signalRadius;
        internal float SignalOpacity => signalOpacity;
        internal static bool HasSignalReceiver => SignalReceived != null;

        internal void ReportSignalReceived(RobotMover receiver) => SignalReceived?.Invoke(this, receiver);

        public bool IsNearby
        {
            get
            {
                if (!isActiveAndEnabled || player == null || !player.isActiveAndEnabled
                    || player.gameObject.scene != gameObject.scene || !interactionTrigger.enabled)
                    return false;

                Transform floor = player.TowerCollisionRoot;
                WeatherTower tower = GetComponentInParent<WeatherTower>();
                if (floor != null && !transform.IsChildOf(floor)) return false;
                if (tower != null && (floor == null || (tower.Phase != WeatherTowerPhase.Ground
                    && tower.Phase != WeatherTowerPhase.Outdoor))) return false;

                return WorldStationInteraction.IsInRange(interactionTrigger, player);
            }
        }

        public bool CanInteract => IsNearby && earthline != null && earthline.isActiveAndEnabled
            && WorldStationInteraction.CanUseControls(player);

        private void Awake()
        {
            interactionTrigger = GetComponent<BoxCollider2D>();
            if (useTutorialInteractionHint) WorldInteractionHint.ReplaceLegacyPrompt(interactionPrompt);
            InitializeSignal(GetComponentInParent<WeatherTower>() != null);
        }

        internal CommunicationsSignalWave InitializeSignal(bool drivenByTower)
        {
            towerDrivesSignal = drivenByTower;
            return signal ??= new CommunicationsSignalWave(this);
        }

        private void OnEnable()
        {
            interactWasHeld = WorldStationInteraction.IsInteractHeld();
            SetPrompt(false);
        }

        private void Start()
        {
            if (player != null) return;
            HeightMapPlayerSceneBootstrap bootstrap = HeightMapPlayerSceneBootstrap.inst;
            if (bootstrap == null || bootstrap.gameObject.scene != gameObject.scene)
            {
                Debug.LogError("CommunicationsStation requires the scene's HeightMapPlayerSceneBootstrap.", this);
                enabled = false;
                return;
            }
            Bind(bootstrap.robot, bootstrap.GetComponentInChildren<EarthlineUI>());
        }

        public void Bind(RobotMover target, EarthlineUI view)
        {
            player = target;
            earthline = view;
        }

        private void Update()
        {
            if (!towerDrivesSignal) signal.Tick(Time.deltaTime, player);
            bool held = WorldStationInteraction.IsInteractHeld();
            bool pressed = held && !interactWasHeld;
            interactWasHeld = held;
            bool available = CanInteract;
            SetPrompt(available);
            if (available && pressed) TryOpen();
        }

        public bool TryOpen()
        {
            if (!CanInteract) return false;
            earthline.Open();
            SetPrompt(false);
            return EarthlineUI.BlocksGameplay;
        }

        private void OnDisable()
        {
            SetPrompt(false);
            if (!towerDrivesSignal) signal?.Hide();
        }

        private void OnDestroy() => signal?.Dispose();

        private void SetPrompt(bool visible)
        {
            if (interactionPrompt != null && interactionPrompt.activeSelf != visible)
                interactionPrompt.SetActive(visible);
        }
    }
}
