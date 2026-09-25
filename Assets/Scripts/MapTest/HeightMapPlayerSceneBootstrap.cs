using AnimalGame.Animals;
using AnimalGame.RobotMap;
using UnityEngine;

namespace AnimalGame.MapTest
{
    public sealed class HeightMapPlayerSceneBootstrap : Singleton<HeightMapPlayerSceneBootstrap>
    {
        private const string MapResourcePath = "MapTest/MapTestController";
        private const string RobotResourcePath = "Robot/RobotMarker";
        private const string CameraResourcePath = "Camera/RobotCamera";
        private const string TraversalResourcePath = "Traversal/HeightMapTraversalEvaluator";
        private const string OverlayResourcePath = "Traversal/TraversalOverlay";
        private const string ScanOverlayResourcePath = "Traversal/TraversalScanOverlay";
        private const string MainUiResourcePath = "UI/MainUI";

        [Header("Player Spawn")]
        [Tooltip("Initial player position in logical map meters. Values outside the map are clamped to its edges.")]
        [SerializeField] private Vector2 playerSpawnMapPositionMeters = new Vector2(50f, 50f);

        [Header("Terrain Debug Display")]
        [SerializeField] private bool showRobotTerrainData;

        [Header("Animal Photos")]
        [Tooltip("Passed to the PhotoResultUI created at runtime. Leave empty to use its prefab reference.")]
        [SerializeField] private PhotoLibrary photoLibrary;

        [Header("Performance Display")]
        [SerializeField] private bool showFrameRate = true;
        [SerializeField, Min(0.05f)] private float frameRateRefreshInterval = 0.25f;

        [HideInInspector] public MapTestSceneController map;
        [HideInInspector] public RobotMover robot;
        [HideInInspector] public HeightMapTraversalEvaluator traversalEvaluator;
        [HideInInspector] public GameObject mapObject;
        [HideInInspector] public GameObject robotObject;
        [HideInInspector] public GameObject cameraObject;
        [HideInInspector] public GameObject traversalObject;
        [HideInInspector] public GameObject overlayObject;
        [HideInInspector] public GameObject scanOverlayObject;
        [HideInInspector] public GameObject mainUiObject;
        [HideInInspector] public RobotBalanceController balance;
        [HideInInspector] public PhotoModeController photoMode;
        [HideInInspector] public RobotTumbleController tumble;
        [HideInInspector] public RobotHeightMotionDetector heightMotion;
        [HideInInspector] public BioScanController bioScan;
        [HideInInspector] public Camera mapCamera;
        [HideInInspector] public RobotCameraFollow cameraFollow;
        [HideInInspector] public RobotCameraShake cameraShake;
        [HideInInspector] public TraversalOverlayUI traversalOverlay;
        [HideInInspector] public TraversalScanOverlayUI scanOverlay;
        [HideInInspector] public ScanChargeUI scanChargeUi;
        [HideInInspector] public PhotoModeUI photoModeUi;
        [HideInInspector] public PhotoResultUI photoResultUi;
        [HideInInspector] public RobotTumbleUiRotation uiRotation;
        [HideInInspector] public RobotBalanceView balanceView;
        [HideInInspector] public RobotArmController armController;
        [HideInInspector] public RobotSelfRightingController selfRightingController;
        [HideInInspector] public Canvas mainUiCanvas;
        [HideInInspector] public Canvas traversalOverlayCanvas;
        [HideInInspector] public Canvas scanOverlayCanvas;
        [HideInInspector] public GameObject traversalOverlayCanvasObject;
        [HideInInspector] public GameObject scanOverlayCanvasObject;

        private Vector2 playerMapPosition;
        private float playerHeight;
        private bool playerInsideMap;
        private float smoothedUnscaledDeltaTime;
        private float displayedFramesPerSecond;
        private float nextFrameRateRefreshTime;

        public Vector2 PlayerSpawnMapPositionMeters =>
            playerSpawnMapPositionMeters;

        protected override void Awake()
        {
            base.Awake();
            map = FindObjectOfType<MapTestSceneController>();
            mapObject = map != null
                ? map.gameObject
                : InstantiateResource(MapResourcePath, "Map Test Controller");
            robotObject = InstantiateResource(RobotResourcePath, "Robot Marker");
            cameraObject = InstantiateResource(CameraResourcePath, "Robot Camera");
            traversalObject = InstantiateResource(
                TraversalResourcePath,
                "Height Map Traversal Evaluator");
            overlayObject = InstantiateResource(
                OverlayResourcePath,
                "Debug Traversal Overlay");
            scanOverlayObject = InstantiateResource(
                ScanOverlayResourcePath,
                "Scanned Traversal Overlay");
            mainUiObject = InstantiateResource(
                MainUiResourcePath,
                "Main UI");
            if (mapObject == null || robotObject == null || cameraObject == null
                || traversalObject == null || overlayObject == null
                || scanOverlayObject == null || mainUiObject == null)
            {
                enabled = false;
                return;
            }

            if (map == null)
                map = mapObject.GetComponent<MapTestSceneController>();
            robot = robotObject.GetComponent<RobotMover>();
            balance =
                robotObject.GetComponent<RobotBalanceController>();
            if (balance == null)
                balance = robotObject.AddComponent<RobotBalanceController>();
            photoMode =
                robotObject.GetComponent<PhotoModeController>();
            if (photoMode == null)
                photoMode = robotObject.AddComponent<PhotoModeController>();
            tumble =
                robotObject.GetComponent<RobotTumbleController>();
            if (tumble == null)
                tumble = robotObject.AddComponent<RobotTumbleController>();
            heightMotion =
                robotObject.GetComponent<RobotHeightMotionDetector>();
            if (heightMotion == null)
                heightMotion = robotObject.AddComponent<RobotHeightMotionDetector>();
            balanceView = robotObject.GetComponent<RobotBalanceView>();
            if (balanceView == null)
                balanceView = robotObject.AddComponent<RobotBalanceView>();
            armController = robotObject.GetComponent<RobotArmController>();
            if (armController == null)
                armController = robotObject.AddComponent<RobotArmController>();
            bioScan =
                robotObject.GetComponent<BioScanController>();
            if (bioScan == null)
                bioScan = robotObject.AddComponent<BioScanController>();
            selfRightingController = robotObject.GetComponent<RobotSelfRightingController>();
            if (selfRightingController == null)
                selfRightingController = robotObject.AddComponent<RobotSelfRightingController>();
            mapCamera = cameraObject.GetComponent<Camera>();
            cameraFollow = cameraObject.GetComponent<RobotCameraFollow>();
            cameraShake =
                cameraObject.GetComponent<RobotCameraShake>();
            if (cameraShake == null)
                cameraShake = cameraObject.AddComponent<RobotCameraShake>();
            traversalEvaluator = traversalObject.GetComponent<HeightMapTraversalEvaluator>();
            traversalOverlay = overlayObject.GetComponent<TraversalOverlayUI>();
            scanOverlay =
                scanOverlayObject.GetComponent<TraversalScanOverlayUI>();
            scanChargeUi =
                mainUiObject.GetComponentInChildren<ScanChargeUI>(true);
            photoModeUi =
                mainUiObject.GetComponent<PhotoModeUI>();
            photoResultUi =
                mainUiObject.GetComponent<PhotoResultUI>();
            if (photoResultUi == null)
                photoResultUi = mainUiObject.AddComponent<PhotoResultUI>();

            if (map == null || robot == null || mapCamera == null || cameraFollow == null
                || traversalEvaluator == null || traversalOverlay == null
                || scanOverlay == null || scanChargeUi == null
                || photoMode == null || photoModeUi == null
                || !map.HasGeneratedMap)
            {
                Debug.LogError(
                    "HeightMapPlayerScene is missing a required prefab component or generated map.",
                    this);
                enabled = false;
                return;
            }

            robot.transform.position = map.MapPositionToWorld(playerSpawnMapPositionMeters);
            map.UseCamera(mapCamera);
            map.UseSurfaceRevealUi(scanChargeUi);
            map.UseElevationFilterTarget(robot.transform);
            cameraFollow.FollowBalanceTarget(balance);
            cameraFollow.SnapToTarget();
            traversalEvaluator.Initialize(map);
            robot.SetTraversalEvaluator(traversalEvaluator);
            tumble.Initialize(traversalEvaluator);
            heightMotion.Initialize(map);
            cameraShake.Initialize(robot, balance, heightMotion);
            photoMode.InitializeCamera(cameraFollow, cameraShake);
            scanChargeUi.SetPhotoModeController(photoMode);
            bioScan.Initialize(scanChargeUi);
            photoModeUi.Initialize(photoMode, mapCamera);
            if (photoLibrary != null) photoResultUi.Library = photoLibrary;
            photoResultUi.Initialize(photoMode, photoModeUi, mapCamera, map);
            uiRotation =
                mainUiObject.GetComponent<RobotTumbleUiRotation>();
            if (uiRotation == null)
                uiRotation = mainUiObject.AddComponent<RobotTumbleUiRotation>();
            uiRotation.Initialize(tumble, mapCamera);
            traversalOverlay.Initialize(map, traversalEvaluator, mapCamera, robot);
            scanOverlay.Initialize(
                map,
                traversalEvaluator,
                mapCamera,
                robot,
                scanChargeUi);
            mainUiCanvas = mainUiObject.GetComponent<Canvas>();
            traversalOverlayCanvas = traversalOverlay.OverlayCanvas;
            scanOverlayCanvas = scanOverlay.OverlayCanvas;
            traversalOverlayCanvasObject = traversalOverlayCanvas != null ? traversalOverlayCanvas.gameObject : null;
            scanOverlayCanvasObject = scanOverlayCanvas != null ? scanOverlayCanvas.gameObject : null;
            if (showRobotTerrainData)
                UpdatePlayerHeight();
        }

        private void Update()
        {
            UpdateFrameRate();
            if (showRobotTerrainData)
                UpdatePlayerHeight();
        }

        private void LateUpdate()
        {
            if (map == null || robot == null)
                return;

            Bounds bounds = map.WorldBounds;
            Vector3 position = robot.transform.position;
            position.x = Mathf.Clamp(position.x, bounds.min.x, bounds.max.x);
            position.y = Mathf.Clamp(position.y, bounds.min.y, bounds.max.y);
            robot.transform.position = position;
        }

        private void UpdatePlayerHeight()
        {
            if (map == null || robot == null)
                return;

            playerInsideMap = map.TrySampleWorldPosition(
                robot.transform.position,
                out playerMapPosition,
                out playerHeight);
        }

        private void UpdateFrameRate()
        {
            float deltaTime = Time.unscaledDeltaTime;
            if (deltaTime <= 0.000001f)
                return;

            if (smoothedUnscaledDeltaTime <= 0f)
            {
                smoothedUnscaledDeltaTime = deltaTime;
            }
            else
            {
                // Smooth quickly enough to show real performance changes without making
                // the number unreadable by changing to a completely new value every frame.
                float smoothing = 1f - Mathf.Exp(-8f * deltaTime);
                smoothedUnscaledDeltaTime = Mathf.Lerp(
                    smoothedUnscaledDeltaTime,
                    deltaTime,
                    smoothing);
            }

            if (Time.unscaledTime < nextFrameRateRefreshTime)
                return;

            displayedFramesPerSecond = 1f / Mathf.Max(0.000001f, smoothedUnscaledDeltaTime);
            nextFrameRateRefreshTime = Time.unscaledTime
                                       + Mathf.Max(0.05f, frameRateRefreshInterval);
        }

        private static GameObject InstantiateResource(string path, string instanceName)
        {
            GameObject prefab = Resources.Load<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogError($"Missing Resources prefab: {path}");
                return null;
            }

            GameObject instance = Object.Instantiate(prefab);
            instance.name = instanceName;
            return instance;
        }

        private void OnGUI()
        {
            Matrix4x4 previousGuiMatrix =
                RobotTumbleUiRotation.BeginImmediateModeGuiRotation();
            try
            {
                DrawFrameRate();
            }
            finally
            {
                GUI.matrix = previousGuiMatrix;
            }

            if (showRobotTerrainData)
                DrawRobotTerrainData();
        }

        private void DrawRobotTerrainData()
        {
            GUIStyle title = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            title.normal.textColor = new Color(0.9f, 0.97f, 1f);
            GUIStyle data = new GUIStyle(GUI.skin.label) { fontSize = 15 };
            data.normal.textColor = new Color(0.75f, 0.9f, 0.93f);

            float left = Screen.width - 330f;
            GUI.Box(new Rect(left, 18f, 312f, 250f), GUIContent.none);
            GUI.Label(new Rect(left + 16f, 28f, 280f, 26f), "ROBOT TERRAIN DATA", title);
            if (!playerInsideMap)
            {
                GUI.Label(new Rect(left + 16f, 64f, 280f, 24f), "OUTSIDE MAP", data);
                return;
            }

            GUI.Label(new Rect(left + 16f, 61f, 280f, 24f),
                $"POSITION   X {playerMapPosition.x:F1}m   Y {playerMapPosition.y:F1}m", data);
            GUI.Label(new Rect(left + 16f, 88f, 280f, 26f), $"CURRENT HEIGHT   {playerHeight:F1}m", data);
            string slopeText = robot != null && robot.CurrentTraversalResult.HasData
                ? $"FORWARD SLOPE   {robot.CurrentTraversalResult.SignedSlopeAngle:+0.0;-0.0;0.0} deg"
                : "FORWARD SLOPE   NO DATA";
            GUI.Label(new Rect(left + 16f, 115f, 280f, 26f), slopeText, data);
            string surfaceText = robot != null && robot.CurrentTraversalResult.HasData
                ? $"SURFACE MAX     {robot.CurrentTraversalResult.MaximumSurfaceSlopeAngle:F1} deg"
                : "SURFACE MAX     NO DATA";
            GUI.Label(new Rect(left + 16f, 142f, 280f, 26f), surfaceText, data);
            string stepText = robot != null && robot.CurrentTraversalResult.HasData
                ? $"STEP RESIDUAL   {robot.CurrentTraversalResult.MaximumStepHeight:F2}m"
                : "STEP RESIDUAL   NO DATA";
            GUI.Label(new Rect(left + 16f, 169f, 280f, 26f), stepText, data);
            GUIStyle state = new GUIStyle(data);
            string traversalText = "TRAVERSAL   NO DATA";
            state.normal.textColor = new Color(0.75f, 0.9f, 0.93f);
            if (robot != null && robot.CurrentTraversalResult.HasData)
            {
                if (robot.IsSlopeBlocked)
                {
                    traversalText =
                        $"TRAVERSAL   BLOCKED ({robot.CurrentTraversalResult.BlockReason})";
                    state.normal.textColor = new Color(1f, 0.35f, 0.28f);
                }
                else if (robot.CurrentLevelThreeClimbPhase
                         != LevelThreeClimbFailurePhase.None)
                {
                    traversalText = robot.CurrentLevelThreeClimbPhase switch
                    {
                        LevelThreeClimbFailurePhase.Grip =>
                            "TRAVERSAL   LEVEL III / GRIP",
                        LevelThreeClimbFailurePhase.Strain =>
                            "TRAVERSAL   LEVEL III / STRAIN",
                        LevelThreeClimbFailurePhase.Slip =>
                            "TRAVERSAL   LEVEL III / SLIP",
                        _ => "TRAVERSAL   SLOPE LEVEL III"
                    };
                    state.normal.textColor = new Color(1f, 0.55f, 0.2f);
                }
                else if (robot.IsLevelThreeUnstable)
                {
                    traversalText = "TRAVERSAL   LEVEL III / UNSTABLE";
                    state.normal.textColor = new Color(1f, 0.55f, 0.2f);
                }
                else if (robot.IsDownhillBoosted)
                {
                    traversalText = "TRAVERSAL   DOWNHILL BOOST";
                    state.normal.textColor = new Color(0.3f, 0.8f, 1f);
                }
                else
                {
                    UphillSlopeLevel surfaceLevel = traversalEvaluator != null
                        ? traversalEvaluator.ClassifyUphillSlope(
                            robot.CurrentTraversalResult.MaximumSurfaceSlopeAngle)
                        : UphillSlopeLevel.LevelOne;
                    traversalText = surfaceLevel switch
                    {
                        UphillSlopeLevel.LevelTwo => "TRAVERSAL   SLOPE LEVEL II",
                        UphillSlopeLevel.LevelThree => "TRAVERSAL   SLOPE LEVEL III",
                        _ => "TRAVERSAL   SLOPE LEVEL I"
                    };
                    state.normal.textColor = surfaceLevel == UphillSlopeLevel.LevelOne
                        ? new Color(0.35f, 1f, 0.66f)
                        : new Color(1f, 0.83f, 0.27f);
                }
            }
            GUI.Label(
                new Rect(left + 16f, 196f, 280f, 26f),
                traversalText,
                state);
        }

        private void DrawFrameRate()
        {
            if (!showFrameRate)
                return;

            float width = 144f;
            float left = Screen.width - width - 18f;
            var panelRect = new Rect(left, 278f, width, 46f);
            GUI.Box(panelRect, GUIContent.none);

            GUIStyle frameRateStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            frameRateStyle.normal.textColor = displayedFramesPerSecond >= 55f
                ? new Color(0.35f, 1f, 0.66f)
                : displayedFramesPerSecond >= 30f
                    ? new Color(1f, 0.83f, 0.27f)
                    : new Color(1f, 0.35f, 0.28f);

            GUI.Label(
                new Rect(left + 6f, 283f, width - 12f, 34f),
                $"FPS  {displayedFramesPerSecond:F1}",
                frameRateStyle);
        }

        private void OnValidate()
        {
            frameRateRefreshInterval = Mathf.Max(0.05f, frameRateRefreshInterval);
        }
    }
}
