using AnimalGame.Animals;
using AnimalGame.Animals.Communication;
using AnimalGame.MapTest;
using AnimalGame.RobotArm;
using AnimalGame.RobotMap;
using UnityEngine;
using GameMainUI = AnimalGame.MainUI.MainUI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AnimalGame.World
{
    [DefaultExecutionOrder(-1100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class CommunicationsStation : MonoBehaviour
    {
        [SerializeField] private GameObject interactionPrompt;

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

                // RobotMover moves through the custom world query, without a Rigidbody2D.
                // Read the authored trigger directly so rotated/scaled stations work without physics callbacks.
                Vector2 local = (Vector2)interactionTrigger.transform.InverseTransformPoint(player.transform.position)
                    - interactionTrigger.offset;
                Vector2 halfSize = interactionTrigger.size * .5f;
                return Mathf.Abs(local.x) <= halfSize.x && Mathf.Abs(local.y) <= halfSize.y;
            }
        }

        public bool CanInteract
        {
            get
            {
                if (!IsNearby || earthline == null || !earthline.isActiveAndEnabled || Time.timeScale <= 0f
                    || GameMainUI.Active == null || GameMainUI.BlocksGameplay
                    || GameMainUI.Active.InventoryProgress > .001f || GameMainUI.Active.IsInventoryOpen
                    || AnimalGame.AnimalDebug.DebugUI.BlocksOtherPanels
                    || AnimalGame.Tutorial.TutorialTipOverLesson.BlocksGameplayForReading(player.gameObject)
                    || player.IgnoreCollisionAndTerrain || player.IsMovementLocked
                    || player.IsPhotoModeInputLocked || player.IsArmInputCaptured)
                    return false;

                RobotArmController arm = player.GetComponent<RobotArmController>();
                PhotoModeController photo = player.GetComponent<PhotoModeController>();
                RobotHeightMotionDetector height = player.GetComponent<RobotHeightMotionDetector>();
                PlayerCombatReceiver combat = player.GetComponent<PlayerCombatReceiver>();
                return (arm == null || arm.State == RobotArmState.Retracted)
                    && (photo == null || (!photo.IsActive && !photo.IsInputLocked))
                    && (height == null || !height.IsAirborne)
                    && (combat == null || !combat.IsDisplaced);
            }
        }

        private void Awake()
        {
            interactionTrigger = GetComponent<BoxCollider2D>();
            InitializeSignal(GetComponentInParent<WeatherTower>() != null);
        }

        internal CommunicationsSignalWave InitializeSignal(bool drivenByTower)
        {
            towerDrivesSignal = drivenByTower;
            return signal ??= new CommunicationsSignalWave(this);
        }

        private void OnEnable()
        {
            interactWasHeld = IsInteractHeld();
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
            bool held = IsInteractHeld();
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

        private static bool IsInteractHeld()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.enterKey.isPressed || keyboard.numpadEnterKey.isPressed)) return true;
#else
            if (Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter)) return true;
#endif
            return AdaptiveLegacyGamepadInput.IsSouthFaceButtonHeld();
        }
    }
}
