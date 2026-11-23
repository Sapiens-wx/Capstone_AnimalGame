using AnimalGame.MapTest;
using AnimalGame.RobotArm;
using AnimalGame.RobotMap;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AnimalGame.World
{
    public enum WeatherTowerPhase { Exterior, Ground, Ascending, Outdoor, Descending }

    [DefaultExecutionOrder(110)]
    [DisallowMultipleComponent]
    public sealed class WeatherTower : MonoBehaviour
    {
        [Header("Registered Floor Views")]
        [SerializeField] private GameObject exteriorView;
        [SerializeField] private GameObject groundView;
        [SerializeField] private GameObject roofView;
        [SerializeField] private Transform lift;
        [SerializeField] private Vector2 liftSize;
        [SerializeField] private Transform groundDoor;
        [SerializeField] private float groundDoorWidth;
        [SerializeField] private GameObject groundGate;
        [SerializeField] private GameObject roofGate;
        [SerializeField] private GameObject liftPrompt;

        [Header("Lift and Camera")]
        [SerializeField] private RobotMover playerOverride;
        [SerializeField, Min(.1f)] private float towerHeightMeters = 12f;
        [SerializeField, Min(.1f)] private float travelSeconds = 2.4f;
        [SerializeField, Min(1f)] private float outdoorZoomMultiplier = 2.25f;

        private const float DoorHysteresis = .08f;
        private const float MaxBoardingSpeed = .2f;
        private readonly WeatherTowerCamera towerCamera = new();
        private RobotMover player;
        private RobotArmController arm;
        private PhotoModeController photo;
        private RobotHeightMotionDetector heightMotion;
        private PlayerCombatReceiver combat;
        private MapTestSceneController map;
        private SpriteRenderer groundArt;
        private SpriteRenderer roofArt;
        private LineRenderer groundGateArt;
        private LineRenderer roofGateArt;
        private float groundHeight;
        private float liftProgress;
        private bool interactWasHeld;
        private Vector2 previousPlayerPosition;
        private CommunicationsSignalWave[] communicationsSignals;

        public WeatherTowerPhase Phase { get; private set; }
        internal event System.Action PlayerEntered;
        public RobotMover Player => player;
        public Transform Lift => lift;
        public Transform GroundDoor => groundDoor;
        public float HeightAboveGroundMeters => Mathf.SmoothStep(0f, towerHeightMeters, liftProgress);
        private bool IsRiding => Phase == WeatherTowerPhase.Ascending || Phase == WeatherTowerPhase.Descending;
        private bool OnUpperView => liftProgress >= .5f;

        public void Configure(GameObject exterior, GameObject ground, GameObject upper, Transform liftAnchor,
            Vector2 platformSize, Transform southDoor, float southOpeningWidth,
            GameObject lowerGate, GameObject upperGate, GameObject prompt)
        {
            exteriorView = exterior;
            groundView = ground;
            roofView = upper;
            lift = liftAnchor;
            liftSize = platformSize;
            groundDoor = southDoor;
            groundDoorWidth = southOpeningWidth;
            groundGate = lowerGate;
            roofGate = upperGate;
            liftPrompt = prompt;
            InitializeViews();
        }

        private void Awake()
        {
            // Initialize inactive roof devices before RefreshViews can hide their floor.
            CommunicationsStation[] stations = GetComponentsInChildren<CommunicationsStation>(true);
            communicationsSignals = new CommunicationsSignalWave[stations.Length];
            for (int index = 0; index < stations.Length; index++)
                communicationsSignals[index] = stations[index].InitializeSignal(true);
            ScopeCollisions(roofView);
            InitializeViews();
        }

        private void InitializeViews()
        {
            groundArt = groundView.GetComponentInChildren<SpriteRenderer>(true);
            roofArt = roofView.GetComponentInChildren<SpriteRenderer>(true);
            groundGateArt = groundGate.GetComponent<LineRenderer>();
            roofGateArt = roofGate.GetComponent<LineRenderer>();
            RefreshViews();
        }

        private void Update()
        {
            foreach (CommunicationsSignalWave signal in communicationsSignals)
                signal.Tick(Time.deltaTime, player);
            bool interactHeld = IsInteractHeld();
            bool interactPressed = interactHeld && !interactWasHeld;
            interactWasHeld = interactHeld;
            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f) return;

            if (!IsScenePlayer(player))
            {
                if (Phase != WeatherTowerPhase.Exterior) ResetTower();
                ResolvePlayer();
                if (player == null) return;
            }

            Vector2 previous = previousPlayerPosition;
            Vector2 position = player.transform.position;
            previousPlayerPosition = position;
            if (player.IgnoreCollisionAndTerrain)
            {
                if (Phase != WeatherTowerPhase.Exterior) ResetTower();
                SetActive(liftPrompt, false);
                return;
            }

            if (IsRiding)
                AdvanceLift(deltaTime);
            else if (!OnUpperView)
                CheckGroundDoor(previous, position);

            bool canRide = CanUseLift();
            SetActive(liftPrompt, canRide);
            if (canRide && interactPressed) TryStartLift();

            if (IsRiding || OnUpperView)
                towerCamera.Apply(HeightAboveGroundMeters / towerHeightMeters, outdoorZoomMultiplier);
        }

        public bool TryStartLift()
        {
            if (Time.deltaTime <= 0f || !CanUseLift() || !towerCamera.Acquire(this, player))
                return false;

            bool goingUp = Phase == WeatherTowerPhase.Ground;
            if (!player.SetTowerSupport(this, ActiveCollisionRoot(), groundHeight + HeightAboveGroundMeters, true))
            {
                towerCamera.Release();
                return false;
            }

            Phase = goingUp ? WeatherTowerPhase.Ascending : WeatherTowerPhase.Descending;
            towerCamera.ApplyLiftFeedback(0f, travelSeconds, goingUp);
            SetActive(liftPrompt, false);
            RefreshViews();
            return true;
        }

        private void AdvanceLift(float deltaTime)
        {
            bool goingUp = Phase == WeatherTowerPhase.Ascending;
            liftProgress = Mathf.MoveTowards(liftProgress, goingUp ? 1f : 0f, deltaTime / travelSeconds);
            towerCamera.ApplyLiftFeedback(goingUp ? liftProgress : 1f - liftProgress, travelSeconds, goingUp);
            if (!goingUp && liftProgress <= 0f)
            {
                Phase = WeatherTowerPhase.Ground;
                towerCamera.Release();
            }
            if (goingUp && liftProgress >= 1f)
                Phase = WeatherTowerPhase.Outdoor;

            RefreshViews();
            player.SetTowerSupport(this, ActiveCollisionRoot(), groundHeight + HeightAboveGroundMeters, IsRiding);
        }

        private void CheckGroundDoor(Vector2 previous, Vector2 position)
        {
            bool exiting = Phase == WeatherTowerPhase.Ground;
            if (!CrossedDoor(groundDoor, groundDoorWidth, previous, position, exiting)) return;

            if (exiting)
            {
                ResetTower();
                return;
            }

            float height = map != null && map.TrySampleWorldPosition(lift.position, out _, out float sampledHeight)
                ? sampledHeight : 0f;
            if (!player.SetTowerSupport(this, groundView.transform, height, false)) return;
            groundHeight = height;
            Phase = WeatherTowerPhase.Ground;
            RefreshViews();
            PlayerEntered?.Invoke();
        }

        private bool CrossedDoor(Transform portal, float openingWidth, Vector2 previous, Vector2 position,
            bool exiting)
        {
            previous = portal.InverseTransformPoint(previous);
            position = portal.InverseTransformPoint(position);
            float threshold = exiting ? -DoorHysteresis : DoorHysteresis;
            bool crossed = exiting ? previous.y > threshold && position.y <= threshold
                : previous.y < threshold && position.y >= threshold;
            if (!crossed) return false;

            float crossingX = Mathf.Lerp(previous.x, position.x,
                (threshold - previous.y) / (position.y - previous.y));
            return Mathf.Abs(crossingX) + Clearance(portal, Vector2.right) < openingWidth * .5f;
        }

        private bool CanUseLift()
        {
            if ((Phase != WeatherTowerPhase.Ground && Phase != WeatherTowerPhase.Outdoor)
                || !IsScenePlayer(player) || player.IgnoreCollisionAndTerrain
                || player.IsMovementLocked || player.IsPhotoModeInputLocked
                || Mathf.Abs(player.CurrentSpeed) > MaxBoardingSpeed
                || (arm != null && arm.State != RobotArmState.Retracted)
                || (photo != null && photo.IsActive)
                || (heightMotion != null && heightMotion.IsAirborne)
                || (combat != null && combat.IsDisplaced))
                return false;

            Vector2 local = lift.InverseTransformPoint(player.transform.position);
            return Mathf.Abs(local.x) + Clearance(lift, Vector2.right) < liftSize.x * .5f
                && Mathf.Abs(local.y) + Clearance(lift, Vector2.up) < liftSize.y * .5f;
        }

        private float Clearance(Transform frame, Vector2 localAxis)
        {
            Vector2 worldAxis = frame.TransformVector(localAxis);
            float scale = worldAxis.magnitude;
            float radius = player.BodyRadiusInQuery(map) + player.CollisionSkin;
            if (map != null && map.HasGeneratedMap)
            {
                Vector2 axis = worldAxis / scale;
                Vector2 mapAxes = new Vector2(map.MapMetersToWorldDistance(Vector2.right, 1f),
                    map.MapMetersToWorldDistance(Vector2.up, 1f));
                radius *= Vector2.Scale(axis, mapAxes).magnitude;
            }
            return radius / scale;
        }

        private void ResolvePlayer()
        {
            HeightMapPlayerSceneBootstrap bootstrap = HeightMapPlayerSceneBootstrap.inst;
            if (bootstrap != null && bootstrap.gameObject.scene != gameObject.scene) bootstrap = null;
            player = playerOverride != null ? playerOverride : bootstrap != null ? bootstrap.robot : FindScenePlayer();
            if (!IsScenePlayer(player))
            {
                player = null;
                return;
            }

            previousPlayerPosition = player.transform.position;
            map = bootstrap != null && player == bootstrap.robot ? bootstrap.map : null;
            arm = player.GetComponent<RobotArmController>();
            photo = player.GetComponent<PhotoModeController>();
            heightMotion = player.GetComponent<RobotHeightMotionDetector>();
            combat = player.GetComponent<PlayerCombatReceiver>();
        }

        private RobotMover FindScenePlayer()
        {
            RobotMover result = null;
            foreach (RobotMover candidate in FindObjectsByType<RobotMover>(FindObjectsSortMode.None))
            {
                if (!IsScenePlayer(candidate)) continue;
                if (result != null) return null;
                result = candidate;
            }
            return result;
        }

        private bool IsScenePlayer(RobotMover candidate) => candidate != null && candidate.isActiveAndEnabled
            && candidate.gameObject.scene == gameObject.scene;

        private Transform ActiveCollisionRoot() =>
            (OnUpperView ? roofView : groundView).transform;

        private void RefreshViews()
        {
            bool outside = Phase == WeatherTowerPhase.Exterior;
            float roofOpacity = Mathf.SmoothStep(0f, 1f, liftProgress);
            float groundOpacity = outside ? 0f : 1f - roofOpacity;
            SetFloorOpacity(groundArt, groundGateArt, groundOpacity);
            SetFloorOpacity(roofArt, roofGateArt, roofOpacity);

            // The scopes still select just one floor for collisions while both views are visible.
            // Once the roof fully covers it, stop ground-floor props and their owned interaction UI.
            SetActive(groundView, outside || groundOpacity > 0f);
            SetActive(exteriorView, outside);
            SetActive(roofView, roofOpacity > 0f);
            SetActive(groundGate, Phase != WeatherTowerPhase.Ground);
            SetActive(roofGate, IsRiding);
        }

        private static void SetFloorOpacity(SpriteRenderer art, LineRenderer gate, float opacity)
        {
            Color color = art.color;
            color.a = opacity;
            art.color = color;
            art.enabled = opacity > 0f;

            Color start = gate.startColor;
            Color end = gate.endColor;
            start.a = end.a = opacity;
            gate.startColor = start;
            gate.endColor = end;
            gate.enabled = opacity > 0f;
        }

        private static void ScopeCollisions(GameObject view)
        {
            foreach (WorldInteraction interaction in view.GetComponentsInChildren<WorldInteraction>(true))
                interaction.RequiresQueryScope = true;
        }

        private void ResetTower()
        {
            if (player != null) player.ClearTowerSupport(this);
            towerCamera.Release();
            Phase = WeatherTowerPhase.Exterior;
            liftProgress = 0f;
            SetActive(liftPrompt, false);
            RefreshViews();
        }

        private void OnDisable()
        {
            ResetTower();
            if (communicationsSignals == null) return;
            foreach (CommunicationsSignalWave signal in communicationsSignals) signal.Hide();
        }

        private void OnDestroy()
        {
            if (communicationsSignals == null) return;
            foreach (CommunicationsSignalWave signal in communicationsSignals) signal.Dispose();
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active) target.SetActive(active);
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
