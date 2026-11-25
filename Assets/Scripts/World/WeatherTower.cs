using AnimalGame.MapTest;
using AnimalGame.Rendering;
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
    public sealed partial class WeatherTower : MonoBehaviour
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
        [Tooltip("Optional second lift between the east exterior dock and the upper floor.")]
        [SerializeField] private Transform exitLift;
        [SerializeField] private GameObject exitLiftPrompt;

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
        private SpriteRenderer exteriorArt;
        private SpriteRenderer groundArt;
        private SpriteRenderer roofArt;
        private LineRenderer groundGateArt;
        private LineRenderer roofGateArt;
        private float groundHeight;
        private float liftBaseHeight;
        private float liftProgress;
        private bool ridingExitLift;
        private bool interactWasHeld;
        private Vector2 previousPlayerPosition;
        private CommunicationsSignalWave[] communicationsSignals;

        public WeatherTowerPhase Phase { get; private set; }
        internal event System.Action PlayerEntered;
        public RobotMover Player => player;
        public Transform Lift => lift;
        public Transform ExitLift => exitLift;
        public Transform GroundDoor => groundDoor;
        public float HeightAboveGroundMeters => Mathf.SmoothStep(0f, towerHeightMeters, liftProgress);
        private bool IsRiding => Phase == WeatherTowerPhase.Ascending || Phase == WeatherTowerPhase.Descending;
        private bool OnUpperView => liftProgress >= .5f;
        private float UpperFloorOpacity => Mathf.SmoothStep(0f, 1f, liftProgress);

        public void Configure(GameObject exterior, GameObject ground, GameObject upper, Transform liftAnchor,
            Vector2 platformSize, Transform southDoor, float southOpeningWidth,
            GameObject lowerGate, GameObject upperGate, GameObject prompt,
            Transform eastLift = null, GameObject eastPrompt = null)
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
            exitLift = eastLift;
            exitLiftPrompt = eastPrompt;
            InitializeViews();
        }

        private void Awake()
        {
            // Initialize inactive roof devices before RefreshViews can hide their floor.
            CommunicationsStation[] stations = GetComponentsInChildren<CommunicationsStation>(true);
            communicationsSignals = new CommunicationsSignalWave[stations.Length];
            for (int index = 0; index < stations.Length; index++)
                communicationsSignals[index] = stations[index].InitializeSignal(true);
            if (HasOpenGround) InitializeSite();
            ScopeCollisions(roofView);
            if (exitLift != null) ScopeCollisions(groundView);
            InitializeViews();
            if (HasOpenGround) InitializePowerVisuals();
        }

        private void InitializeViews()
        {
            exteriorArt = HasOpenGround ? groundRoofArt : exteriorView.GetComponentInChildren<SpriteRenderer>(true);
            groundArt = groundView.GetComponentInChildren<SpriteRenderer>(true);
            roofArt = roofView.GetComponentInChildren<SpriteRenderer>(true);
            groundGateArt = groundGate != null ? groundGate.GetComponent<LineRenderer>() : null;
            roofGateArt = roofGate != null ? roofGate.GetComponent<LineRenderer>() : null;
            RefreshViews();
        }

        private void Update()
        {
            if (Time.timeScale > 0f) TickPowerVisuals(Time.deltaTime);
            foreach (CommunicationsSignalWave signal in communicationsSignals)
                signal.Tick(Time.deltaTime, player);
            bool interactHeld = IsInteractHeld();
            bool interactPressed = interactHeld && !interactWasHeld;
            interactWasHeld = interactHeld;
            TickStationPlug();
            float deltaTime = Time.deltaTime;
            // The refit UI can pause and disable the mover earlier in this same frame,
            // while deltaTime still reflects the frame before the pause.
            if (Time.timeScale <= 0f || deltaTime <= 0f) return;
            EnclosedRoomTerrain.Advance(this, deltaTime);
            if (HasOpenGround) TickLiftSite(deltaTime);

            if (!IsScenePlayer(player))
            {
                if (Phase != WeatherTowerPhase.Exterior) ResetTower();
                ResolvePlayer();
                if (player == null)
                {
                    HideInteractionPrompts();
                    return;
                }
            }

            Vector2 previous = previousPlayerPosition;
            Vector2 position = player.transform.position;
            previousPlayerPosition = position;
            if (player.IgnoreCollisionAndTerrain)
            {
                if (Phase != WeatherTowerPhase.Exterior) ResetTower();
                HideInteractionPrompts();
                return;
            }

            if (IsRiding)
                AdvanceLift(deltaTime);
            else if (!OnUpperView)
                CheckGroundDoor(previous, position);

            if (EquipmentInteractionReady)
                UpdateLiftInteraction(interactPressed);
            else if (!IsPowered)
                UpdatePowerInteraction(interactPressed);
            else
                HideInteractionPrompts();

            if (IsRiding || OnUpperView)
                towerCamera.Apply(HeightAboveGroundMeters / towerHeightMeters, outdoorZoomMultiplier);
        }

        private void UpdateLiftInteraction(bool pressed)
        {
            if (plugPhase != PlugPhase.Idle) { SetLiftPrompt(null); return; }
            Transform platform = AvailableLift();
            Landing? call = platform == null ? AvailableLiftCall() : null;
            if (HasOpenGround && (platform != null || call.HasValue))
            {
                Landing landing = call ?? (platform == lift ? MainLanding : EastLanding);
                if (!TryFindLiftSocket(landing, out WeatherTowerSocket socket))
                {
                    SetLiftPrompt(null);
                    return;
                }
                if (pressed)
                {
                    BeginLiftPlug(socket, landing, call.HasValue);
                    SetLiftPrompt(null);
                    return;
                }
            }
            SetLiftPrompt(platform, call);
            if (!HasOpenGround && pressed && platform != null) TryStartLift(platform);
        }

        public bool TryStartLift()
        {
            Transform availableLift = AvailableLift();
            return availableLift != null && TryStartLift(availableLift);
        }

        private bool TryStartLift(Transform availableLift)
        {
            if (Time.deltaTime <= 0f || !towerCamera.Acquire(this, player))
                return false;

            bool goingUp = Phase != WeatherTowerPhase.Outdoor;
            bool useExitLift = availableLift == exitLift;
            float datum = Phase == WeatherTowerPhase.Exterior ? SampleGround(lift) : groundHeight;
            float baseHeight = useExitLift ? SampleGround(exitLift) : datum;
            Transform boardingFloor = goingUp
                ? (useExitLift ? exteriorView : groundView).transform
                : roofView.transform;
            float boardingHeight = goingUp ? baseHeight : datum + towerHeightMeters;
            if (!player.SetTowerSupport(this, boardingFloor, boardingHeight, true))
            {
                towerCamera.Release();
                return false;
            }

            ridingExitLift = useExitLift;
            groundHeight = datum;
            liftBaseHeight = baseHeight;
            Phase = goingUp ? WeatherTowerPhase.Ascending : WeatherTowerPhase.Descending;
            if (!HasOpenGround) towerCamera.ApplyLiftFeedback(0f, travelSeconds, goingUp);
            SetLiftPrompt(null);
            RefreshViews();
            return true;
        }

        private void AdvanceLift(float deltaTime)
        {
            if (HasOpenGround && !LiftDoorsClosed(ridingExitLift)) return;
            bool goingUp = Phase == WeatherTowerPhase.Ascending;
            liftProgress = Mathf.MoveTowards(liftProgress, goingUp ? 1f : 0f, deltaTime / travelSeconds);
            towerCamera.ApplyLiftFeedback(goingUp ? liftProgress : 1f - liftProgress, travelSeconds, goingUp);
            if (!goingUp && liftProgress <= 0f)
            {
                if (HasOpenGround) DockRiddenLift(false);
                if (ridingExitLift)
                {
                    ResetTower();
                    return;
                }
                Phase = WeatherTowerPhase.Ground;
                towerCamera.Release();
            }
            if (goingUp && liftProgress >= 1f)
            {
                if (HasOpenGround) DockRiddenLift(true);
                Phase = WeatherTowerPhase.Outdoor;
            }

            RefreshViews();
            float supportHeight = Mathf.Lerp(liftBaseHeight, groundHeight + towerHeightMeters,
                HeightAboveGroundMeters / towerHeightMeters);
            player.SetTowerSupport(this, ActiveCollisionRoot(), supportHeight, IsRiding);
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

            float height = SampleGround(lift);
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

        private float SampleGround(Transform anchor) =>
            map != null && map.TrySampleWorldPosition(anchor.position, out _, out float sampledHeight) ? sampledHeight : 0f;

        private Transform AvailableLift()
        {
            if (!CanUseLiftControls()) return null;
            if (Phase != WeatherTowerPhase.Exterior && CanBoard(lift, MainLanding)) return lift;
            return Phase != WeatherTowerPhase.Ground && exitLift != null && CanBoard(exitLift, EastLanding)
                ? exitLift : null;
        }

        private bool CanUseLiftControls() =>
            EquipmentInteractionReady && !IsRiding && IsScenePlayer(player) && !player.IgnoreCollisionAndTerrain
            && !player.IsMovementLocked && !player.IsPhotoModeInputLocked
            && Mathf.Abs(player.CurrentSpeed) <= MaxBoardingSpeed
            && (arm == null || arm.State == RobotArmState.Retracted
                || (UsesStationPlugs && (arm.CanStartStationPlug || arm.StationPlugSeated)))
            && (photo == null || !photo.IsActive)
            && (heightMotion == null || !heightMotion.IsAirborne)
            && (combat == null || !combat.IsDisplaced);

        private bool CanBoard(Transform anchor, Landing landing) =>
            (!HasOpenGround || (!landingBarriers[(int)landing].activeSelf && ClearOfLiftDoors(anchor))) && OnLift(anchor);

        private bool OnLift(Transform anchor)
        {
            Vector2 local = anchor.InverseTransformPoint(player.transform.position);
            return Mathf.Abs(local.x) + Clearance(anchor, Vector2.right) < liftSize.x * .5f
                && Mathf.Abs(local.y) + Clearance(anchor, Vector2.up) < liftSize.y * .5f;
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
            (OnUpperView ? roofView : ridingExitLift ? exteriorView : groundView).transform;

        private void RefreshViews()
        {
            bool outside = Phase == WeatherTowerPhase.Exterior;
            bool inGroundRoom = Phase == WeatherTowerPhase.Ground || (IsRiding && !ridingExitLift && !OnUpperView);
            EnclosedRoomTerrain.SetOccupied(this, inGroundRoom);
            float roofOpacity = UpperFloorOpacity;
            bool blendExterior = outside || (IsRiding && ridingExitLift);
            float exteriorOpacity = blendExterior ? 1f - roofOpacity : 0f;
            float groundOpacity = blendExterior ? 0f : 1f - roofOpacity;
            // The first-floor roof covers its interior whenever that room is not on display.
            if (exitLift != null) SetArtOpacity(exteriorArt, HasOpenGround ? 1f - groundOpacity : exteriorOpacity);
            SetFloorOpacity(groundArt, groundGateArt, groundOpacity);
            SetFloorOpacity(roofArt, roofGateArt, roofOpacity);

            // The scopes still select just one floor for collisions while both views are visible.
            // Once the roof fully covers it, stop ground-floor props and their owned interaction UI.
            SetActive(groundView, (outside && exitLift == null) || groundOpacity > 0f);
            SetActive(exteriorView, HasOpenGround ? roofOpacity < 1f : exteriorOpacity > 0f);
            SetActive(roofView, roofOpacity > 0f);
            SetActive(groundGate, Phase != WeatherTowerPhase.Ground);
            SetActive(roofGate, IsRiding);
            if (doorLines != null) UpdateLandingDoors(0f);
        }

        private static void SetFloorOpacity(SpriteRenderer art, LineRenderer gate, float opacity)
        {
            SetArtOpacity(art, opacity);
            if (gate == null) return;
            Color start = gate.startColor;
            Color end = gate.endColor;
            start.a = end.a = opacity;
            gate.startColor = start;
            gate.endColor = end;
            gate.enabled = opacity > 0f;
        }

        private static void SetArtOpacity(SpriteRenderer art, float opacity)
        {
            Color color = art.color;
            color.a = opacity;
            art.color = color;
            art.enabled = opacity > 0f;
        }

        private static void ScopeCollisions(GameObject view)
        {
            foreach (WorldInteraction interaction in view.GetComponentsInChildren<WorldInteraction>(true))
                interaction.RequiresQueryScope = true;
        }

        private void ResetTower()
        {
            CancelPlugInteraction();
            if (player != null) player.ClearTowerSupport(this);
            towerCamera.Release();
            Phase = WeatherTowerPhase.Exterior;
            liftProgress = 0f;
            ridingExitLift = false;
            HideInteractionPrompts();
            RefreshViews();
        }

        private void OnDisable()
        {
            ResetTower();
            EnclosedRoomTerrain.Release(this);
            doorLines?.Hide();
            deckLines?.Hide();
            powerLines?.Hide();
            if (communicationsSignals == null) return;
            foreach (CommunicationsSignalWave signal in communicationsSignals) signal.Hide();
        }

        private void OnDestroy()
        {
            doorLines?.Dispose();
            deckLines?.Dispose();
            powerLines?.Dispose();
            if (communicationsSignals == null) return;
            foreach (CommunicationsSignalWave signal in communicationsSignals) signal.Dispose();
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active) target.SetActive(active);
        }

        private void HideInteractionPrompts()
        {
            if (powerSocket != null) powerSocket.enabled = false;
            SetLiftPrompt(null);
        }

        private void SetLiftPrompt(Transform availableLift, Landing? call = null)
        {
            bool callEast = call.HasValue && IsEast(call.Value);
            bool showMain = availableLift == lift || (call.HasValue && !callEast);
            bool showEast = (exitLift != null && availableLift == exitLift) || callEast;
            if (HasOpenGround)
            {
                if (showMain) PositionLiftPrompt(liftPrompt, lift, call);
                if (showEast) PositionLiftPrompt(exitLiftPrompt, exitLift, call);
            }
            SetActive(liftPrompt, showMain);
            SetActive(exitLiftPrompt, showEast);
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
