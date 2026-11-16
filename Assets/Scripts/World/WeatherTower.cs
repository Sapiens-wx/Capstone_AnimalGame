using AnimalGame.MapTest;
using AnimalGame.RobotArm;
using AnimalGame.RobotMap;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AnimalGame.World
{
    public enum WeatherTowerPhase { Ground, Ascending, Outdoor, Indoor, Descending }

    [DefaultExecutionOrder(110)]
    [DisallowMultipleComponent]
    public sealed class WeatherTower : MonoBehaviour
    {
        [Header("Registered Floor Views")]
        [SerializeField] private GameObject groundView;
        [SerializeField] private GameObject roofView;
        [SerializeField] private GameObject indoorView;
        [SerializeField] private Transform lift;
        [SerializeField] private Vector2 liftSize;
        [SerializeField] private Transform door;
        [SerializeField] private float doorWidth;
        [SerializeField] private GameObject groundGate;
        [SerializeField] private GameObject roofGate;
        [SerializeField] private GameObject liftPrompt;

        [Header("Lift and Camera")]
        [SerializeField] private RobotMover playerOverride;
        [SerializeField, Min(.1f)] private float towerHeightMeters = 12f;
        [SerializeField, Min(.1f)] private float travelSeconds = 2.4f;
        [SerializeField, Min(1f)] private float outdoorZoomMultiplier = 2.25f;
        [SerializeField, Min(.1f)] private float indoorCameraSize = 7f;
        [SerializeField, Min(0f)] private float indoorBlendSeconds = .65f;

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
        private LineRenderer groundGateArt;
        private float groundHeight;
        private float liftProgress;
        private bool interactWasHeld;
        private Vector2 previousDoorPosition;

        public WeatherTowerPhase Phase { get; private set; }
        public RobotMover Player => player;
        public Transform Lift => lift;
        public Transform Door => door;
        public float HeightAboveGroundMeters => Mathf.SmoothStep(0f, towerHeightMeters, liftProgress);
        private bool IsRiding => Phase == WeatherTowerPhase.Ascending || Phase == WeatherTowerPhase.Descending;
        private bool IsInside => Phase == WeatherTowerPhase.Indoor;
        private bool OnUpperView => IsInside || Phase == WeatherTowerPhase.Outdoor || liftProgress >= .5f;

        public void Configure(GameObject ground, GameObject roof, GameObject inside, Transform liftAnchor,
            Vector2 platformSize, Transform indoorDoor, float openingWidth,
            GameObject lowerGate, GameObject upperGate, GameObject prompt)
        {
            groundView = ground;
            roofView = roof;
            indoorView = inside;
            lift = liftAnchor;
            liftSize = platformSize;
            door = indoorDoor;
            doorWidth = openingWidth;
            groundGate = lowerGate;
            roofGate = upperGate;
            liftPrompt = prompt;
            RefreshViews();
        }

        private void Awake()
        {
            groundArt = groundView.GetComponentInChildren<SpriteRenderer>(true);
            groundGateArt = groundGate.GetComponent<LineRenderer>();
            ScopeCollisions(roofView);
            ScopeCollisions(indoorView);
            RefreshViews();
        }

        private void Update()
        {
            bool interactHeld = IsInteractHeld();
            bool interactPressed = interactHeld && !interactWasHeld;
            interactWasHeld = interactHeld;
            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f) return;

            if (!IsScenePlayer(player))
            {
                if (Phase != WeatherTowerPhase.Ground) ResetTower();
                ResolvePlayer();
                if (player == null) return;
            }
            if (player.IgnoreCollisionAndTerrain)
            {
                if (Phase != WeatherTowerPhase.Ground) ResetTower();
                SetActive(liftPrompt, false);
                return;
            }

            if (IsRiding)
                AdvanceLift(deltaTime);
            else if (Phase == WeatherTowerPhase.Outdoor || IsInside)
                CheckDoorCrossing();

            bool canRide = CanUseLift();
            SetActive(liftPrompt, canRide);
            if (canRide && interactPressed) TryStartLift();

            if (Phase != WeatherTowerPhase.Ground)
                towerCamera.Apply(HeightAboveGroundMeters / towerHeightMeters, IsInside,
                    outdoorZoomMultiplier, indoorCameraSize, indoorBlendSeconds, deltaTime);
        }

        public bool TryStartLift()
        {
            if (Time.deltaTime <= 0f || !CanUseLift() || !towerCamera.Acquire(this, player))
                return false;

            bool goingUp = Phase == WeatherTowerPhase.Ground;
            if (goingUp)
                groundHeight = map != null && map.TrySampleWorldPosition(lift.position, out _, out float height)
                    ? height : 0f;

            if (!player.SetTowerSupport(this, ActiveCollisionRoot(), groundHeight + HeightAboveGroundMeters, true))
            {
                towerCamera.Release();
                return false;
            }

            Phase = goingUp ? WeatherTowerPhase.Ascending : WeatherTowerPhase.Descending;
            SetActive(liftPrompt, false);
            RefreshViews();
            return true;
        }

        private void AdvanceLift(float deltaTime)
        {
            bool goingUp = Phase == WeatherTowerPhase.Ascending;
            liftProgress = Mathf.MoveTowards(liftProgress, goingUp ? 1f : 0f, deltaTime / travelSeconds);
            if (!goingUp && liftProgress <= 0f)
            {
                ResetTower();
                return;
            }

            if (goingUp && liftProgress >= 1f)
            {
                Phase = WeatherTowerPhase.Outdoor;
                previousDoorPosition = door.InverseTransformPoint(player.transform.position);
            }

            RefreshViews();
            player.SetTowerSupport(this, ActiveCollisionRoot(), groundHeight + HeightAboveGroundMeters, IsRiding);
        }

        private void CheckDoorCrossing()
        {
            Vector2 position = door.InverseTransformPoint(player.transform.position);
            Vector2 previous = previousDoorPosition;
            previousDoorPosition = position;
            float threshold = IsInside ? -DoorHysteresis : DoorHysteresis;
            bool crossed = IsInside ? previous.y > threshold && position.y <= threshold
                : previous.y < threshold && position.y >= threshold;
            if (!crossed) return;

            float crossingX = Mathf.Lerp(previous.x, position.x,
                (threshold - previous.y) / (position.y - previous.y));
            if (Mathf.Abs(crossingX) + Clearance(door, Vector2.right) >= doorWidth * .5f)
                return;

            Phase = IsInside ? WeatherTowerPhase.Outdoor : WeatherTowerPhase.Indoor;
            RefreshViews();
            player.SetTowerSupport(this, ActiveCollisionRoot(), groundHeight + HeightAboveGroundMeters, false);
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
            (IsInside ? indoorView : OnUpperView ? roofView : groundView).transform;

        private void RefreshViews()
        {
            // Ground walls still belong to the ground when the player is upstairs.
            if (groundArt != null) groundArt.enabled = !OnUpperView;
            if (groundGateArt != null) groundGateArt.enabled = !OnUpperView;
            SetActive(roofView, OnUpperView && !IsInside);
            SetActive(indoorView, IsInside);
            SetActive(groundGate, Phase != WeatherTowerPhase.Ground);
            SetActive(roofGate, IsRiding && OnUpperView);
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
            Phase = WeatherTowerPhase.Ground;
            liftProgress = 0f;
            SetActive(liftPrompt, false);
            RefreshViews();
        }

        private void OnDisable() => ResetTower();

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
