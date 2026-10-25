using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AnimalGame.MapTest;
using AnimalGame.RobotArm;
using AnimalGame.RobotMap;
using AnimalGame.Tutorial;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    /// <summary>Real balance input, display, chapter gating and ordinary L3 handoff in a disposable copy.</summary>
    public sealed class TutorialBalancePlaySmoke : ScriptableObject
    {
        private const string SessionKey = "AnimalGame.TutorialBalancePlaySmoke.CurrentRun";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string KnownMissingStaticAnimal = "Prefab instance problem: 'Static_Animal (Missing Prefab with guid: 2645e53e76c8ec846b09ee78b13275be)'. 2 instances are missing the same Prefab Asset with guid 2645e53e76c8ec846b09ee78b13275be.";
        private const string KnownSceneHeader = "Problem detected while opening the Scene file: 'Assets/Scenes/GameMaps/TutorialTemporaryMap.unity'.\nCheck the following logs for more details.";
        private enum Phase
        {
            Starting, TerrainStarting, BalanceStarting, ReadIndicator, HidePanel, HiddenReading, ShowPanel,
            FinishIndicator, Neutral, Adjust, ReadRisk, Paused, Resume, FinishRisk,
            ArmStarting, DeployArms, ArmNeutral, StowArms, Complete, Finishing
        }
        [Serializable] private sealed class Report
        {
            public bool passed, balanceComplete, armStartedBeforeBalance, armsStartedAfterBalance;
            public bool collapsedReadingFrozen, pauseReadingFrozen, actualManualAndPointMoved, actualGraphicMoved;
            public bool ordinaryL3Deploy, ordinaryL3Stow, balanceParametersUnchanged, ownedTeachingAnimalCreated;
            public bool needsNeutral, stopped, tipOccurred, panelCollapsed, panelVisible;
            public bool endpointFixture, sourceSceneUnchanged = true;
            public string project, phase, step, armStep, armState, tumbleState, movementMode, nativeGraphicCanvasName;
            public float readingSeconds, value, target, maximumPositionDriftMeters, maximumManualChange, maximumPointChange;
            public float maximumGraphicChange, pausedReading, hiddenReading, timeScale, rawRightMagnitude;
            public float currentSpeed, terrainSpeed, turnSpeed, terrainTurnSpeed;
            public int screenWidth, screenHeight, knownSceneHeaders, knownMissingPrefabMessages, balanceConfirmations;
            public Vector2 initialMapPosition, playerMapPosition, nativeCounterbalance, nativeBalancePoint, nativeGraphicPoint;
            public Vector2 originalAuthoredSpawn, endpointFixturePosition;
            public Vector2 baselineCounterbalance, baselineBalancePoint, baselineGraphicPoint, actualRawRightStick;
            public List<string> checks = new List<string>(), errors = new List<string>(), knownSceneDiagnostics = new List<string>();
            public List<string> screenshotRequests = new List<string>(), screenshots = new List<string>();
        }

        [SerializeField] private string runId, output, originalBalanceParameters, originalSceneFingerprint;
        [SerializeField] private bool running, originalBackground, cameraFollowSuspended, poseRecorded;
        [SerializeField] private int gamepadId, lastFrame = -1, phaseFrame, lastTapPhase = -1, tapFirstFrame;
        [SerializeField] private double deadline, phaseWallTime;
        [SerializeField] private Phase phase;
        [SerializeField] private Report report;
        [SerializeField] private InputSettings originalInput, testInput;
        [SerializeField] private HeightMapPlayerSceneBootstrap bootstrap;
        [SerializeField] private TutorialDrivingLesson driving;
        [SerializeField] private TutorialTerrainLesson terrain;
        [SerializeField] private TutorialBalanceLesson lesson;
        [SerializeField] private TutorialArmLesson arms;
        [SerializeField] private TutorialDiscoveryLesson discovery;
        [SerializeField] private RobotBalanceGraphic graphic;

        public static void RunBatch()
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            if (!Application.isBatchMode || !project.Contains("/output/validation/"))
                throw new InvalidOperationException("Run balance Play Mode smoke only in a disposable output/validation project.");
            SessionState.EraseString(SessionKey);
            foreach (var old in Resources.FindObjectsOfTypeAll<TutorialBalancePlaySmoke>())
            { old.running = false; old.Unsubscribe(); DestroyImmediate(old); }
            var test = CreateInstance<TutorialBalancePlaySmoke>(); test.hideFlags = HideFlags.HideAndDontSave;
            test.runId = Guid.NewGuid().ToString("N"); SessionState.SetString(SessionKey, test.runId);
            string requested = Environment.GetEnvironmentVariable("TUTORIAL_BALANCE_VALIDATION_OUTPUT");
            test.output = string.IsNullOrWhiteSpace(requested) ? Path.Combine(project, "output/validation/tutorial-balance")
                : Path.GetFullPath(Path.IsPathRooted(requested) ? requested : Path.Combine(project, requested));
            Directory.CreateDirectory(test.output); test.report = new Report { project = project };
            ApplyGameViewSize();
            test.Subscribe();
            var loadedScene = EditorSceneManager.OpenScene(TutorialDrivingLesson.ScenePath, OpenSceneMode.Single);
            if (Environment.GetEnvironmentVariable("TUTORIAL_BALANCE_AT_ENDPOINT") == "1")
                test.ConfigureEndpointFixture(loadedScene);
            test.deadline = EditorApplication.timeSinceStartup + 180;
            test.running = true; test.Begin(Phase.Starting); EditorApplication.isPlaying = true;
        }
        private bool Current => !string.IsNullOrEmpty(runId) && SessionState.GetString(SessionKey, "") == runId;
        private void OnEnable() { if (running && Current) Subscribe(); }
        private void OnDisable() => Unsubscribe();
        private void Subscribe()
        {
            if (!Current) return;
            Unsubscribe(); EditorApplication.update += Tick; InputSystem.onBeforeUpdate += Drive;
            Application.logMessageReceived += OnLog;
        }
        private void Unsubscribe()
        {
            EditorApplication.update -= Tick; InputSystem.onBeforeUpdate -= Drive; Application.logMessageReceived -= OnLog;
            if (bootstrap?.balance != null) bootstrap.balance.TippedOver -= OnTippedOver;
        }

        private void Tick()
        {
            if (!running || !Current) return;
            try
            {
                if (phase == Phase.Finishing)
                { if (!EditorApplication.isPlayingOrWillChangePlaymode) Exit(); return; }
                Require(EditorApplication.timeSinceStartup < deadline, "Balance smoke timed out in " + phase + ".");
                EditorApplication.QueuePlayerLoopUpdate();
                if (!EditorApplication.isPlaying || Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount; ReadNativeState();
                if (poseRecorded) CheckInvariants();
                if (Time.frameCount % 30 == 0) Save();
                switch (phase)
                {
                    case Phase.Starting:
                        bootstrap = Object.FindFirstObjectByType<HeightMapPlayerSceneBootstrap>();
                        driving = Object.FindFirstObjectByType<TutorialDrivingLesson>();
                        lesson = Object.FindFirstObjectByType<TutorialBalanceLesson>();
                        arms = Object.FindFirstObjectByType<TutorialArmLesson>();
                        discovery = Object.FindFirstObjectByType<TutorialDiscoveryLesson>();
                        if (bootstrap?.robot == null || bootstrap.map == null || !bootstrap.map.HasGeneratedMap
                            || bootstrap.balance == null || bootstrap.balanceView == null || bootstrap.armController == null
                            || driving?.Progress == null || driving.Panel?.Root?.panel == null || lesson == null || arms == null) return;
                        // RobotBalanceView owns a separate screen-space canvas; it is not a child
                        // of the player transform. Wait for its normal Awake/LateUpdate setup.
                        graphic = (RobotBalanceGraphic)typeof(RobotBalanceView).GetField("graphic", Flags)
                            .GetValue(bootstrap.balanceView);
                        if (graphic == null || !graphic.enabled || !graphic.gameObject.activeInHierarchy
                            || !bootstrap.balanceView.IsBalanceDisplayVisible) return;
                        Canvas balanceCanvas = graphic.GetComponentInParent<Canvas>();
                        Require(balanceCanvas != null && balanceCanvas.name == RobotBalanceView.BalanceCanvasName,
                            "The observed point must belong to this player's native balance display canvas.");
                        report.nativeGraphicCanvasName = balanceCanvas.name;
                        PrepareGamepad();
                        originalBalanceParameters = EditorJsonUtility.ToJson(bootstrap.balance);
                        bootstrap.balance.TippedOver += OnTippedOver;
                        Require(bootstrap.map.TrySampleWorldPosition(bootstrap.robot.transform.position, out report.initialMapPosition, out _),
                            "The unchanged player must begin on the native map.");
                        if (report.endpointFixture)
                            Require(Vector2.Distance(report.initialMapPosition, report.endpointFixturePosition) <= .1f,
                                "The native bootstrap must spawn directly at the authored terrain endpoint for this opt-in fixture.");
                        poseRecorded = true;
                        typeof(DrivingTutorialProgress).GetProperty("Step", Flags).SetValue(driving.Progress, DrivingTutorialStep.Completed);
                        typeof(TutorialDrivingLesson).GetField("chapterConfirmed", Flags).SetValue(driving, true);
                        terrain = driving.GetComponent<TutorialTerrainLesson>();
                        Require(terrain != null, "The authored terrain lesson must precede balance.");
                        Record("Fixture skips driving/terrain counters only and retains the actual player pose, native balance physics and existing point display.");
                        Begin(Phase.TerrainStarting); break;
                    case Phase.TerrainStarting:
                        if (!terrain.HasStarted || terrain.Progress == null) return;
                        typeof(TerrainTutorialProgress).GetProperty("Step", Flags).SetValue(terrain.Progress, TerrainTutorialStep.Completed);
                        typeof(TutorialTerrainLesson).GetField("chapterConfirmed", Flags).SetValue(terrain, true);
                        Require(!arms.HasStarted, "The arms chapter must not start when only terrain has completed.");
                        Begin(Phase.BalanceStarting); break;
                    case Phase.BalanceStarting:
                        if (!lesson.HasStarted || lesson.Progress == null) return;
                        Require(lesson.Progress.Step == BalanceTutorialStep.ReadIndicator && !arms.HasStarted,
                            "Balance must begin with indicator reading while arms remain gated.");
                        Record("The balance chapter owns the task panel before the arms chapter.", "balance-indicator.png");
                        Begin(Phase.ReadIndicator); break;
                    case Phase.ReadIndicator:
                        if (lesson.Progress.ReadingSeconds < .45f) return;
                        Require(lesson.Progress.Step == BalanceTutorialStep.ReadIndicator,
                            "The authored indicator explanation must remain visible long enough to read.");
                        Begin(Phase.HidePanel); break;
                    case Phase.HidePanel:
                        if (!Settled(4) || !lesson.Panel.IsCollapsed) return;
                        report.hiddenReading = lesson.Progress.ReadingSeconds;
                        Begin(Phase.HiddenReading); break;
                    case Phase.HiddenReading:
                        Require(lesson.Panel.IsCollapsed && lesson.Progress.Step == BalanceTutorialStep.ReadIndicator
                            && Mathf.Abs(lesson.Progress.ReadingSeconds - report.hiddenReading) < .00001f,
                            "The real collapsed panel must freeze indicator reading even while the native right stick moves.");
                        if (Seconds < .7) return;
                        report.collapsedReadingFrozen = true;
                        Record("A real D-pad collapse freezes visible-reading progress without disabling native balance control.");
                        Begin(Phase.ShowPanel); break;
                    case Phase.ShowPanel:
                        if (!Settled(4) || lesson.Panel.IsCollapsed) return;
                        Begin(Phase.FinishIndicator); break;
                    case Phase.FinishIndicator:
                        if (lesson.Progress.Step != BalanceTutorialStep.AdjustWeight) return;
                        Require(!arms.HasStarted, "Reading alone must never unlock the arms chapter.");
                        Begin(Phase.Neutral); break;
                    case Phase.Neutral:
                        if (!Settled(8) || lesson.Progress.NeedsNeutral) return;
                        report.baselineCounterbalance = bootstrap.balance.CurrentState.PlayerCounterbalanceLocal;
                        report.baselineBalancePoint = bootstrap.balance.CurrentState.NormalizedLocalOffset;
                        report.baselineGraphicPoint = GraphicPoint();
                        Begin(Phase.Adjust); break;
                    case Phase.Adjust:
                        if (lesson.Progress.Step != BalanceTutorialStep.ReadRisk) return;
                        Vector2 manual = bootstrap.balance.CurrentState.PlayerCounterbalanceLocal - report.baselineCounterbalance;
                        Vector2 point = bootstrap.balance.CurrentState.NormalizedLocalOffset - report.baselineBalancePoint;
                        float shownShift = Vector2.Distance(GraphicPoint(), report.baselineGraphicPoint);
                        Require(manual.magnitude >= .04f && point.magnitude >= .045f && Vector2.Dot(manual, point) > 0f,
                            "Actual native counterweight and balance point must move together after the real right-stick push.");
                        Require(shownShift > .02f && graphic.enabled && graphic.gameObject.activeInHierarchy,
                            "The native drawn point must also move, rather than only the tutorial progress changing.");
                        report.actualManualAndPointMoved = true; report.actualGraphicMoved = true;
                        Record("One real right-stick direction moves the actual counterweight, physics balance point and existing UI point; no recentering or flip is required.", "balance-adjusted.png");
                        Begin(Phase.ReadRisk); break;
                    case Phase.ReadRisk:
                        if (lesson.Progress.ReadingSeconds < .45f) return;
                        report.pausedReading = lesson.Progress.ReadingSeconds;
                        bootstrap.mainUiObject.GetComponent<AnimalGame.MainUI.MainUI>().OpenInventory();
                        Begin(Phase.Paused); break;
                    case Phase.Paused:
                        if (!Settled(4)) return;
                        Require(AnimalGame.MainUI.MainUI.BlocksGameplay && Time.timeScale == 0f
                            && lesson.Progress.Step == BalanceTutorialStep.ReadRisk
                            && Mathf.Abs(lesson.Progress.ReadingSeconds - report.pausedReading) < .00001f,
                            "The native inventory pause freezes risk reading despite held right-stick input.");
                        Require(lesson.Panel.Root.resolvedStyle.display == DisplayStyle.None,
                            "The balance lesson must hide behind an actual inventory pause.");
                        if (Seconds < .7) return;
                        report.pauseReadingFrozen = true;
                        bootstrap.mainUiObject.GetComponent<AnimalGame.MainUI.MainUI>().CloseInventory();
                        Begin(Phase.Resume); break;
                    case Phase.Resume:
                        if (!Settled(4) || AnimalGame.MainUI.MainUI.BlocksGameplay || Time.deltaTime <= 0f) return;
                        Require(lesson.Progress.Step == BalanceTutorialStep.ReadRisk && !arms.HasStarted,
                            "Returning from inventory restores risk reading without skipping to arms.");
                        Record("A real inventory pause hides the task and freezes risk reading; gameplay resumes normally.");
                        Begin(Phase.FinishRisk); break;
                    case Phase.FinishRisk:
                        if (!lesson.IsComplete) return;
                        Require(!report.tipOccurred && bootstrap.tumble.State == RobotTumbleState.Upright,
                            "Balance introduction completes without tipping or self-righting practice.");
                        Record("The risk explanation completes with normal upright control and unchanged balance tuning.");
                        Begin(Phase.ArmStarting); break;
                    case Phase.ArmStarting:
                        if (!arms.HasStarted || arms.Progress == null) return;
                        report.balanceConfirmations = TutorialContinueSmokeInput.ConfirmationCount(lesson.Panel);
                        Require(report.balanceConfirmations == 4,
                            "The three balance instructions and completion card each require a separate A confirmation after twelve ready frames.");
                        Record("Four separate real A confirmations complete the three balance cards and its chapter before arms can start.");
                        Require(lesson.IsComplete && arms.Progress.Step == ArmTutorialStep.Deploy
                            && bootstrap.armController.State == RobotArmState.Retracted,
                            "The ordinary in-place arms lesson starts only after all balance steps complete.");
                        Require(arms.Panel.Root.Q<Label>("TutorialTitle")?.text == arms.TextContent.Arms.Title,
                            "The completed balance chapter must hand panel ownership to the arm lesson.");
                        report.armsStartedAfterBalance = true;
                        Begin(Phase.DeployArms); break;
                    case Phase.DeployArms:
                        if (bootstrap.armController.State != RobotArmState.OuterOperating || !bootstrap.armController.IsArmModeActive
                            || arms.Progress.Step != ArmTutorialStep.Extend) return;
                        Require(arms.Progress.Step == ArmTutorialStep.Extend, "A real ordinary L3 tap must satisfy the arm deployment step.");
                        report.ordinaryL3Deploy = true; Begin(Phase.ArmNeutral); break;
                    case Phase.ArmNeutral:
                        if (!Settled(4)) return;
                        Begin(Phase.StowArms); break;
                    case Phase.StowArms:
                        if (bootstrap.armController.State != RobotArmState.Retracted || bootstrap.armController.IsArmModeActive) return;
                        report.ordinaryL3Stow = true;
                        Record("Normal L3 deployment and retraction still operate after balance hands over to arms.", "balance-completed-arm-handoff.png");
                        Begin(Phase.Complete); break;
                    case Phase.Complete:
                        if (!Settled(8)) return;
                        Require(report.collapsedReadingFrozen && report.pauseReadingFrozen && report.actualManualAndPointMoved
                            && report.actualGraphicMoved && report.armsStartedAfterBalance && report.ordinaryL3Deploy && report.ordinaryL3Stow,
                            "The actual balance exercise and arm handoff must all finish successfully.");
                        foreach (string path in report.screenshotRequests)
                            if (File.Exists(path) && new FileInfo(path).Length > 1024) report.screenshots.Add(path);
                        Finish(null); break;
                }
            }
            catch (Exception error) { Finish(error); }
        }

        private void PrepareGamepad()
        {
            originalInput = InputSystem.settings; testInput = Instantiate(originalInput); testInput.hideFlags = HideFlags.HideAndDontSave;
            testInput.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            testInput.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus; InputSystem.settings = testInput;
            originalBackground = Application.runInBackground; Application.runInBackground = true;
            foreach (Gamepad other in new List<Gamepad>(Gamepad.all)) InputSystem.RemoveDevice(other);
            gamepadId = InputSystem.AddDevice<Gamepad>("Balance tutorial validation gamepad").deviceId;
        }
        private void ConfigureEndpointFixture(UnityEngine.SceneManagement.Scene scene)
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            Require(Current && Application.isBatchMode && project.Contains("/output/validation/")
                && !EditorApplication.isPlayingOrWillChangePlaymode && scene.path == TutorialDrivingLesson.ScenePath,
                "Only the guarded disposable fixture may adjust its loaded scene's initial spawn before Play Mode.");
            HeightMapPlayerSceneBootstrap sceneBootstrap = null;
            TutorialTerrainLesson sceneTerrain = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (sceneBootstrap == null) sceneBootstrap = root.GetComponentInChildren<HeightMapPlayerSceneBootstrap>(true);
                if (sceneTerrain == null) sceneTerrain = root.GetComponentInChildren<TutorialTerrainLesson>(true);
            }
            Require(sceneBootstrap != null && sceneTerrain != null, "The endpoint fixture needs the authored bootstrap and terrain lesson.");
            originalSceneFingerprint = SceneFingerprint();
            report.originalAuthoredSpawn = sceneBootstrap.PlayerSpawnMapPositionMeters;
            var terrainSettings = new SerializedObject(sceneTerrain);
            SerializedProperty destination = terrainSettings.FindProperty("gentleRouteDestinationMeters");
            Require(destination != null, "The terrain lesson's authored destination must remain available.");
            Vector2 endpoint = destination.vector2Value;
            Require(Vector2.Distance(endpoint, new Vector2(146f, 121f)) < .01f,
                "The requested endpoint scenario expects the authored terrain destination (146,121).");
            var spawnSettings = new SerializedObject(sceneBootstrap);
            SerializedProperty spawn = spawnSettings.FindProperty("playerSpawnMapPositionMeters");
            Require(spawn != null, "The native bootstrap's existing spawn configuration must remain available.");
            spawn.vector2Value = endpoint; spawnSettings.ApplyModifiedPropertiesWithoutUndo();
            Require(sceneBootstrap.PlayerSpawnMapPositionMeters == endpoint,
                "The fixture must set the existing native bootstrap spawn configuration, without teleporting a live player.");
            report.endpointFixture = true; report.endpointFixturePosition = endpoint;
            report.sourceSceneUnchanged = SceneFingerprint() == originalSceneFingerprint;
            Require(report.sourceSceneUnchanged, "The endpoint fixture must not save the authored scene file.");
            Save();
        }
        private static string SceneFingerprint()
        {
            using (var hash = System.Security.Cryptography.SHA256.Create())
                return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(Path.GetFullPath(
                    Path.Combine(Application.dataPath, "..", TutorialDrivingLesson.ScenePath)))));
        }
        private static void ApplyGameViewSize()
        {
            Assembly assembly = typeof(EditorWindow).Assembly;
            Type sizesType = assembly.GetType("UnityEditor.GameViewSizes", true);
            Type sizeType = assembly.GetType("UnityEditor.GameViewSize", true);
            Type kindType = assembly.GetType("UnityEditor.GameViewSizeType", true);
            Type groupType = assembly.GetType("UnityEditor.GameViewSizeGroupType", true);
            Type viewType = assembly.GetType("UnityEditor.GameView", true);
            object sizes = typeof(ScriptableSingleton<>).MakeGenericType(sizesType)
                .GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            object group = sizesType.GetMethod("GetGroup", Flags).Invoke(sizes, new[] { Enum.Parse(groupType, "Standalone") });
            object size = Activator.CreateInstance(sizeType, Flags, null,
                new[] { Enum.Parse(kindType, "FixedResolution"), (object)1920, 1080, "Balance fixture 1920x1080" },
                System.Globalization.CultureInfo.InvariantCulture);
            group.GetType().GetMethod("AddCustomSize", Flags).Invoke(group, new[] { size });
            int builtin = (int)group.GetType().GetMethod("GetBuiltinCount", Flags).Invoke(group, null);
            int custom = (int)group.GetType().GetMethod("GetCustomCount", Flags).Invoke(group, null);
            EditorWindow view = EditorWindow.GetWindow(viewType);
            viewType.GetProperty("selectedSizeIndex", Flags).SetValue(view, builtin + custom - 1); view.Repaint();
        }
        private void Drive()
        {
            if (!running || !Current || phase == Phase.Finishing || InputState.currentUpdateType != InputUpdateType.Dynamic) return;
            if (bootstrap?.cameraFollow != null)
            {
                if (Time.deltaTime <= 0f && bootstrap.cameraFollow.enabled)
                { bootstrap.cameraFollow.enabled = false; cameraFollowSuspended = true; }
                else if (Time.deltaTime > 0f && cameraFollowSuspended)
                { bootstrap.cameraFollow.enabled = true; cameraFollowSuspended = false; }
            }
            var gamepad = InputSystem.GetDeviceById(gamepadId) as Gamepad; if (gamepad == null) return;
            var state = new GamepadState();
            if (phase == Phase.HiddenReading || phase == Phase.Adjust) state.rightStick = Vector2.right * .7f;
            if (phase == Phase.Paused) state.rightStick = Vector2.left * .7f;
            if (phase == Phase.HidePanel || phase == Phase.ShowPanel || phase == Phase.DeployArms || phase == Phase.StowArms)
            {
                if (lastTapPhase != (int)phase) { lastTapPhase = (int)phase; tapFirstFrame = Time.frameCount; }
                if (Time.frameCount - tapFirstFrame < 3)
                    state = state.WithButton(phase == Phase.HidePanel ? GamepadButton.DpadRight
                        : phase == Phase.ShowPanel ? GamepadButton.DpadLeft : GamepadButton.LeftStick);
            }
            if ((phase == Phase.FinishIndicator || phase == Phase.Adjust || phase == Phase.FinishRisk) && lesson?.Progress != null)
                TutorialContinueSmokeInput.Apply(ref state, lesson.Panel, "Balance:" + lesson.Progress.Step);
            if (phase == Phase.DeployArms && arms?.Progress != null)
                TutorialContinueSmokeInput.Apply(ref state, arms.Panel, "Arm:" + arms.Progress.Step);
            gamepad.MakeCurrent(); InputSystem.QueueStateEvent(gamepad, state);
        }
        private Vector2 GraphicPoint() => graphic != null
            ? (Vector2)typeof(RobotBalanceGraphic).GetField("normalizedOffset", Flags).GetValue(graphic) : Vector2.zero;
        private void OnTippedOver(RobotTipOverInfo _) => report.tipOccurred = true;
        private void ReadNativeState()
        {
            report.timeScale = Time.timeScale; report.screenWidth = Screen.width; report.screenHeight = Screen.height;
            if (bootstrap?.robot != null)
            {
                RobotMover robot = bootstrap.robot;
                bootstrap.map.TrySampleWorldPosition(robot.transform.position, out report.playerMapPosition, out _);
                report.currentSpeed = robot.CurrentSpeed; report.terrainSpeed = robot.CurrentTerrainVelocity.magnitude;
                report.turnSpeed = robot.CurrentTurnSpeed; report.terrainTurnSpeed = robot.CurrentTerrainTurnSpeed;
                report.movementMode = robot.MovementMode.ToString();
                report.stopped = Mathf.Abs(robot.CurrentSpeed) < .01f && robot.CurrentTerrainVelocity.magnitude < .01f;
            }
            if (bootstrap?.balance != null)
            {
                report.nativeCounterbalance = bootstrap.balance.CurrentState.PlayerCounterbalanceLocal;
                report.nativeBalancePoint = bootstrap.balance.CurrentState.NormalizedLocalOffset;
                report.nativeGraphicPoint = GraphicPoint();
                if (phase == Phase.Adjust)
                {
                    report.maximumManualChange = Mathf.Max(report.maximumManualChange,
                        Vector2.Distance(report.nativeCounterbalance, report.baselineCounterbalance));
                    report.maximumPointChange = Mathf.Max(report.maximumPointChange,
                        Vector2.Distance(report.nativeBalancePoint, report.baselineBalancePoint));
                    report.maximumGraphicChange = Mathf.Max(report.maximumGraphicChange,
                        Vector2.Distance(report.nativeGraphicPoint, report.baselineGraphicPoint));
                }
            }
            if (bootstrap?.tumble != null) report.tumbleState = bootstrap.tumble.State.ToString();
            if (bootstrap?.armController != null) report.armState = bootstrap.armController.State.ToString();
            if (arms?.Progress != null) report.armStep = arms.Progress.Step.ToString();
            var pad = InputSystem.GetDeviceById(gamepadId) as Gamepad;
            if (pad != null) { report.actualRawRightStick = pad.rightStick.ReadValue(); report.rawRightMagnitude = report.actualRawRightStick.magnitude; }
            if (lesson?.Panel != null) { report.panelCollapsed = lesson.Panel.IsCollapsed; report.panelVisible = lesson.Panel.IsVisible; }
        }
        private void CheckInvariants()
        {
            Require(bootstrap?.balance != null && bootstrap.robot != null, "The native robot and balance components must persist.");
            report.maximumPositionDriftMeters = Mathf.Max(report.maximumPositionDriftMeters,
                Vector2.Distance(report.playerMapPosition, report.initialMapPosition));
            Require(report.maximumPositionDriftMeters <= .1f, "The stationary balance lesson must not relocate or drive the player.");
            Require(!report.tipOccurred && !bootstrap.balance.IsTippedOver && bootstrap.tumble.State == RobotTumbleState.Upright,
                "This introduction must not force a flip, tumble or self-righting operation.");
            report.balanceParametersUnchanged = EditorJsonUtility.ToJson(bootstrap.balance) == originalBalanceParameters;
            Require(report.balanceParametersUnchanged, "The lesson must preserve all authored native balance physics parameters.");
            if (arms != null && arms.HasStarted && (lesson == null || !lesson.IsComplete)) report.armStartedBeforeBalance = true;
            Require(!report.armStartedBeforeBalance, "The arm lesson cannot start while the balance chapter remains incomplete.");
            report.ownedTeachingAnimalCreated |= discovery?.Targets?.Animal != null;
            Require(!report.ownedTeachingAnimalCreated, "Balance and early arm practice must not create or change the later tutorial animal.");
        }
        private double Seconds => EditorApplication.timeSinceStartup - phaseWallTime;
        private bool Settled(int frames) => Time.frameCount - phaseFrame >= frames;
        private void Begin(Phase next) { phase = next; phaseFrame = Time.frameCount; phaseWallTime = EditorApplication.timeSinceStartup; Save(); }
        private void Record(string text, string screenshot = null)
        {
            report.checks.Add(text);
            if (!string.IsNullOrEmpty(screenshot))
            { string path = Path.Combine(output, "game-" + screenshot); ScreenCapture.CaptureScreenshot(path); report.screenshotRequests.Add(path); }
            Save();
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private void OnLog(string text, string stack, LogType type)
        {
            if (!Current || report == null) return;
            string normalized = text.Replace("\r\n", "\n").Trim();
            if (type == LogType.Error && (phase == Phase.Starting || phase == Phase.Finishing)
                && (normalized == KnownMissingStaticAnimal || normalized == KnownSceneHeader))
            {
                if (normalized == KnownMissingStaticAnimal) report.knownMissingPrefabMessages++; else report.knownSceneHeaders++;
                if (!report.knownSceneDiagnostics.Contains(normalized)) report.knownSceneDiagnostics.Add(normalized);
                Save(); return;
            }
            if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && !report.errors.Contains(text))
            { report.errors.Add(text); Save(); }
        }
        private void Save()
        {
            if (report == null || string.IsNullOrEmpty(output)) return;
            report.phase = phase.ToString();
            if (lesson?.Progress != null)
            {
                report.step = lesson.Progress.Step.ToString(); report.readingSeconds = lesson.Progress.ReadingSeconds;
                report.value = lesson.Progress.Value; report.target = lesson.Progress.Target;
                report.needsNeutral = lesson.Progress.NeedsNeutral; report.balanceComplete = lesson.IsComplete;
            }
            File.WriteAllText(Path.Combine(output, "balance-play-smoke.json"), JsonUtility.ToJson(report, true));
        }
        private void Finish(Exception error)
        {
            if (phase == Phase.Finishing) return;
            if (error != null) report.errors.Add(error.ToString());
            InputSystem.onBeforeUpdate -= Drive;
            bootstrap?.mainUiObject?.GetComponent<AnimalGame.MainUI.MainUI>()?.CloseInventory();
            if (cameraFollowSuspended && bootstrap?.cameraFollow != null) bootstrap.cameraFollow.enabled = true;
            var pad = InputSystem.GetDeviceById(gamepadId); if (pad != null) InputSystem.RemoveDevice(pad);
            if (originalInput != null) InputSystem.settings = originalInput;
            if (testInput != null) DestroyImmediate(testInput);
            Application.runInBackground = originalBackground;
            Begin(Phase.Finishing); EditorApplication.isPlaying = false;
        }
        private void Exit()
        {
            if (report.endpointFixture)
            {
                // Restore the loaded editor scene's setting as well. No save API is used, and
                // the actual on-disk scene must retain its original fingerprint throughout.
                HeightMapPlayerSceneBootstrap restored = Object.FindFirstObjectByType<HeightMapPlayerSceneBootstrap>();
                if (restored != null && restored.gameObject.scene.path == TutorialDrivingLesson.ScenePath)
                {
                    var settings = new SerializedObject(restored);
                    settings.FindProperty("playerSpawnMapPositionMeters").vector2Value = report.originalAuthoredSpawn;
                    settings.ApplyModifiedPropertiesWithoutUndo();
                }
                report.sourceSceneUnchanged = SceneFingerprint() == originalSceneFingerprint;
                if (!report.sourceSceneUnchanged) report.errors.Add("The opt-in endpoint fixture changed the authored scene file on disk.");
            }
            if (report.knownSceneHeaders > 0 && report.knownMissingPrefabMessages == 0)
                report.errors.Add("A scene-load failure lacked the exact known two-Static_Animal missing-prefab diagnostic.");
            report.passed = report.errors.Count == 0; Save(); running = false; Unsubscribe(); EditorApplication.Exit(report.passed ? 0 : 1);
        }
    }
}
