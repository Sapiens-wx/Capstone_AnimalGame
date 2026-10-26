using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AnimalGame.MapTest;
using AnimalGame.RobotArm;
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
    /// <summary>Real arm/tutorial input smoke, restricted to a disposable validation project.</summary>
    public sealed class TutorialArmPlaySmoke : ScriptableObject
    {
        private const string SessionKey = "AnimalGame.TutorialArmPlaySmoke.CurrentRun";
        private const string KnownMissingStaticAnimal = "Prefab instance problem: 'Static_Animal (Missing Prefab with guid: 2645e53e76c8ec846b09ee78b13275be)'. 2 instances are missing the same Prefab Asset with guid 2645e53e76c8ec846b09ee78b13275be.";
        private const string KnownSceneHeader = "Problem detected while opening the Scene file: 'Assets/Scenes/GameMaps/TutorialTemporaryMap.unity'.\nCheck the following logs for more details.";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private enum Phase { Starting, TerrainStarting, ArmStarting, LeftHide, LeftRelease, RightShow,
            Deploying, Paused, Resumed, Actions, Stowing, Complete, Finishing }
        [Serializable] private sealed class Report
        {
            public bool passed;
            public string project, phase, step, armState;
            public float value, target, deployment;
            public Vector2 handCentreLocal, robotMapPosition, fixturePositionMeters;
            public float maximumPositionDriftMeters;
            public int precedingWaypointCount;
            public int knownSceneHeaders, knownMissingPrefabMessages, armConfirmations;
            public List<string> checks = new List<string>(), errors = new List<string>();
            public List<string> knownSceneDiagnostics = new List<string>();
        }
        [SerializeField] private string runId, output;
        [SerializeField] private bool running, originalBackground, cameraFollowSuspended;
        [SerializeField] private int phaseFrame, lastFrame = -1, lastTapPhase = -1, gamepadId;
        [SerializeField] private double deadline;
        [SerializeField] private Phase phase;
        [SerializeField] private Report report;
        [SerializeField] private InputSettings originalInput, testInput;
        [SerializeField] private HeightMapPlayerSceneBootstrap bootstrap;
        [SerializeField] private TutorialDrivingLesson driving;
        [SerializeField] private TutorialTerrainLesson terrain;
        [SerializeField] private TutorialArmLesson lesson;
        [SerializeField] private ArmTutorialStep lastStep;
        [SerializeField] private float pausedValue;
        [SerializeField] private bool fixturePositionRecorded;
        [SerializeField] private Vector2 fixturePositionMeters;
        [SerializeField] private int precedingWaypointCount;

        public static void RunBatch()
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            if (!Application.isBatchMode || !project.Contains("/output/validation/"))
                throw new InvalidOperationException("Run the arm Play Mode smoke only in a disposable output/validation project.");
            SessionState.EraseString(SessionKey);
            foreach (var old in Resources.FindObjectsOfTypeAll<TutorialArmPlaySmoke>())
            { old.running = false; old.Unsubscribe(); DestroyImmediate(old); }
            var test = CreateInstance<TutorialArmPlaySmoke>();
            test.hideFlags = HideFlags.HideAndDontSave;
            test.runId = Guid.NewGuid().ToString("N"); SessionState.SetString(SessionKey, test.runId);
            test.output = Path.Combine(project, "output/validation/tutorial-arm"); Directory.CreateDirectory(test.output);
            test.report = new Report { project = project };
            EditorSceneManager.OpenScene(TutorialDrivingLesson.ScenePath, OpenSceneMode.Single);
            test.deadline = EditorApplication.timeSinceStartup + 180;
            test.running = true; test.Begin(Phase.Starting); test.Subscribe(); EditorApplication.isPlaying = true;
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
        { EditorApplication.update -= Tick; InputSystem.onBeforeUpdate -= Drive; Application.logMessageReceived -= OnLog; }

        private void Tick()
        {
            if (!running || !Current) return;
            try
            {
                if (phase == Phase.Finishing)
                { if (!EditorApplication.isPlayingOrWillChangePlaymode) Exit(); return; }
                Require(EditorApplication.timeSinceStartup < deadline, "Arm smoke timed out in " + phase + ".");
                EditorApplication.QueuePlayerLoopUpdate();
                if (!EditorApplication.isPlaying || Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount;
                if (fixturePositionRecorded) CheckInPlace();
                if (Time.frameCount % 30 == 0) Save();
                switch (phase)
                {
                    case Phase.Starting:
                        bootstrap = Object.FindFirstObjectByType<HeightMapPlayerSceneBootstrap>();
                        driving = Object.FindFirstObjectByType<TutorialDrivingLesson>();
                        lesson = Object.FindFirstObjectByType<TutorialArmLesson>();
                        if (bootstrap == null || bootstrap.map == null || !bootstrap.map.HasGeneratedMap
                            || bootstrap.robot == null || bootstrap.armController == null || driving?.Progress == null
                            || lesson == null || driving.Panel?.Root?.panel == null) return;
                        originalInput = InputSystem.settings; testInput = Instantiate(originalInput);
                        testInput.hideFlags = HideFlags.HideAndDontSave;
                        testInput.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                        testInput.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus; InputSystem.settings = testInput;
                        originalBackground = Application.runInBackground; Application.runInBackground = true;
                        gamepadId = InputSystem.AddDevice<Gamepad>("Arm tutorial validation gamepad").deviceId;
                        typeof(DrivingTutorialProgress).GetProperty("Step", Flags).SetValue(driving.Progress, DrivingTutorialStep.Completed);
                        typeof(TutorialDrivingLesson).GetField("chapterConfirmed", Flags).SetValue(driving, true);
                        terrain = driving.GetComponent<TutorialTerrainLesson>();
                        Require(terrain != null, "The scene must retain its preceding terrain chapter.");
                        Begin(Phase.TerrainStarting); break;
                    case Phase.TerrainStarting:
                        if (!terrain.HasStarted || terrain.Progress == null) return;
                        typeof(TerrainTutorialProgress).GetProperty("Step", Flags).SetValue(terrain.Progress, TerrainTutorialStep.Completed);
                        typeof(TutorialTerrainLesson).GetField("chapterConfirmed", Flags).SetValue(terrain, true);
                        Require(bootstrap.map.TrySampleWorldPosition(bootstrap.robot.transform.position,
                            out fixturePositionMeters, out _), "The untouched initial robot must be on the existing map.");
                        fixturePositionRecorded = true;
                        precedingWaypointCount = lesson.GetComponentsInChildren<TutorialWaypointIndicator>(true).Length;
                        report.fixturePositionMeters = fixturePositionMeters;
                        report.precedingWaypointCount = precedingWaypointCount;
                        Record("Fixture skips only the three preceding chapter counters and records the current robot position without relocating the robot or camera.");
                        Begin(Phase.ArmStarting); break;
                    case Phase.ArmStarting:
                        var precedingBalance = lesson.GetComponent<TutorialBalanceLesson>();
                        if (precedingBalance != null && precedingBalance.enabled && precedingBalance.Progress != null
                            && !precedingBalance.IsComplete)
                        {
                            typeof(BalanceTutorialProgress).GetProperty("Step", Flags).SetValue(precedingBalance.Progress, BalanceTutorialStep.Completed);
                            typeof(TutorialBalanceLesson).GetField("chapterConfirmed", Flags).SetValue(precedingBalance, true);
                            return;
                        }
                        if (!lesson.HasStarted || lesson.Progress == null) return;
                        Require(lesson.Progress.Step == ArmTutorialStep.Deploy, "The arm lesson must start with deployment at the current player position.");
                        Require(bootstrap.armController.State == RobotArmState.Retracted, "The empty-hand fixture must begin with actual stowed arms.");
                        Require(lesson.Panel.Root.Q<Label>("TutorialTitle")?.text == lesson.TextContent.Arms.Title,
                            "A preceding chapter still owns the shared task panel.");
                        Record("Chapter 04 starts directly at the recorded player position in Deploy, within 0.1 m drift, and creates no arm waypoint.");
                        Begin(Phase.LeftHide); break;
                    case Phase.LeftHide:
                        if (!Settled(5)) return;
                        Require(lesson.Panel.IsCollapsed && lesson.Progress.Step == ArmTutorialStep.Deploy,
                            "One left D-pad tap must hide the arms panel exactly once without advancing its step.");
                        Begin(Phase.LeftRelease); break;
                    case Phase.LeftRelease:
                        if (Settled(4)) Begin(Phase.RightShow);
                        break;
                    case Phase.RightShow:
                        if (!Settled(5)) return;
                        Require(!lesson.Panel.IsCollapsed && !AnimalGame.MainUI.MainUI.BlocksGameplay,
                            "One right D-pad tap must reopen the arms panel without terrain toggling it back or opening inventory.");
                        Record("Left/right D-pad taps work once per press while the terrain chapter releases panel ownership.");
                        Begin(Phase.Deploying); break;
                    case Phase.Deploying:
                        if (lesson.Progress.Step == ArmTutorialStep.Deploy) return;
                        Require(lesson.Progress.Step == ArmTutorialStep.Extend && bootstrap.armController.IsArmModeActive
                            && bootstrap.armController.VisibleDeployment01 >= .999f
                            && !(InputSystem.GetDeviceById(gamepadId) as Gamepad).leftStickButton.isPressed,
                            "A single released L3 tap must leave real arms fully deployed before extension begins.");
                        Record("A one-frame L3 tap deploys real arms and remains active after the button is released.");
                        lastStep = lesson.Progress.Step; pausedValue = lesson.Progress.Value;
                        bootstrap.mainUiObject.GetComponent<AnimalGame.MainUI.MainUI>().OpenInventory();
                        Begin(Phase.Paused); break;
                    case Phase.Paused:
                        if (!Settled(10)) return;
                        Require(Time.timeScale == 0f && AnimalGame.MainUI.MainUI.BlocksGameplay,
                            "The actual inventory must own a gameplay pause.");
                        Require(lesson.Progress.Step == ArmTutorialStep.Extend && Mathf.Approximately(lesson.Progress.Value, pausedValue),
                            "Stick and L3 input behind inventory must not advance the arm lesson.");
                        Require(lesson.Panel.Root.resolvedStyle.display == DisplayStyle.None, "The arm tutorial must hide behind the inventory.");
                        bootstrap.mainUiObject.GetComponent<AnimalGame.MainUI.MainUI>().CloseInventory();
                        Begin(Phase.Resumed); break;
                    case Phase.Resumed:
                        if (!Settled(10) || AnimalGame.MainUI.MainUI.BlocksGameplay || Time.deltaTime <= 0f) return;
                        Require(bootstrap.armController.IsArmModeActive && lesson.Progress.Step == ArmTutorialStep.Extend
                            && Mathf.Approximately(lesson.Progress.Value, pausedValue),
                            "Closing inventory must not turn the menu's L3 input into a deployment toggle or movement credit.");
                        Record("Inventory pause hides the task, freezes progress and discards menu-only stick/L3 input.");
                        Begin(Phase.Actions); break;
                    case Phase.Actions:
                        ObserveStep();
                        Require(bootstrap.armController.HeldObject == null && !bootstrap.robot.IsMovementLocked,
                            "Empty-hand arm practice must not grab an unrelated object or tip the robot.");
                        if (lesson.Progress.Step == ArmTutorialStep.Stow) Begin(Phase.Stowing);
                        break;
                    case Phase.Stowing:
                        ObserveStep();
                        if (!lesson.IsComplete) return;
                        Require(bootstrap.armController.State == RobotArmState.Retracted
                            && bootstrap.armController.VisibleDeployment01 <= .001f && !bootstrap.armController.IsArmModeActive,
                            "The last L3 tap must wait for actual full arm retraction.");
                        Begin(Phase.Complete); break;
                    case Phase.Complete:
                        if (!Settled(8)) return;
                        report.armConfirmations = TutorialContinueSmokeInput.ConfirmationCount(lesson.Panel);
                        Require(report.armConfirmations == 6,
                            "Only the six native arm actions require separate A confirmations; no completion card adds another press.");
                        Record("Six separate real A confirmations advance the measured arm actions and directly release the next chapter.");
                        Require(lesson.Progress.IsComplete && Time.timeScale == 1f && bootstrap.robot.enabled,
                            "Completion must preserve ordinary robot and gameplay ownership.");
                        Record("All six real gamepad arm actions finish in place: deploy, extension, inward reach, left swing, right swing and full stow.");
                        Finish(null); break;
                }
            }
            catch (Exception error) { Finish(error); }
        }

        private void ObserveStep()
        {
            if (lesson.Progress.Step == lastStep) return;
            Require((int)lesson.Progress.Step == (int)lastStep + 1, "Actual input must advance one authored arm action at a time.");
            Record("Actual hand motion completed " + lastStep + "."); lastStep = lesson.Progress.Step;
        }

        private void CheckInPlace()
        {
            // The discovery chapter can legitimately create its own destination after this handoff.
            if (lesson.IsComplete) return;
            Require(bootstrap != null && bootstrap.robot != null && bootstrap.map != null,
                "In-place arm practice must retain the existing robot and map.");
            Require(bootstrap.map.TrySampleWorldPosition(bootstrap.robot.transform.position,
                out Vector2 position, out _), "The robot must remain on the map during in-place arm practice.");
            float drift = Vector2.Distance(position, fixturePositionMeters);
            report.maximumPositionDriftMeters = Mathf.Max(report.maximumPositionDriftMeters, drift);
            Require(drift <= .1f, "The robot moved away from its recorded start during arm practice: " + drift + " m.");
            var waypoints = lesson.GetComponentsInChildren<TutorialWaypointIndicator>(true);
            Require(waypoints.Length == precedingWaypointCount,
                "The arm lesson must not create an additional destination waypoint.");
            if (lesson.HasStarted)
                foreach (var waypoint in waypoints)
                    Require(!waypoint.HasTarget, "No destination waypoint may remain active during the in-place arm lesson.");
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
            var gamepad = InputSystem.GetDeviceById(gamepadId) as Gamepad;
            if (gamepad == null) return;
            var state = new GamepadState();
            if ((phase == Phase.LeftHide || phase == Phase.RightShow || phase == Phase.Deploying || phase == Phase.Stowing)
                && lastTapPhase != (int)phase)
            {
                state = state.WithButton(phase == Phase.LeftHide ? GamepadButton.DpadLeft
                    : phase == Phase.RightShow ? GamepadButton.DpadRight : GamepadButton.LeftStick);
                lastTapPhase = (int)phase;
            }
            if (phase == Phase.Paused) { state.leftStick = Vector2.up; state = state.WithButton(GamepadButton.LeftStick); }
            if (phase == Phase.Actions && lesson?.Progress != null)
                state.leftStick = lesson.Progress.Step switch
                {
                    ArmTutorialStep.Extend => Vector2.up * .45f,
                    ArmTutorialStep.RetractReach => Vector2.down * .45f,
                    ArmTutorialStep.SwingLeft => Vector2.left * .45f,
                    ArmTutorialStep.SwingRight => Vector2.right * .45f,
                    _ => Vector2.zero
                };
            if ((phase == Phase.Deploying || phase == Phase.Actions || phase == Phase.Stowing) && lesson?.Progress != null)
                TutorialContinueSmokeInput.Apply(ref state, lesson.Panel, "Arm:" + lesson.Progress.Step);
            gamepad.MakeCurrent(); InputSystem.QueueStateEvent(gamepad, state);
        }

        private void Begin(Phase next) { phase = next; phaseFrame = Time.frameCount; Save(); }
        private bool Settled(int frames) => Time.frameCount - phaseFrame >= frames;
        private void Record(string text) { report.checks.Add(text); Save(); }
        private static void Require(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); }
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
            if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                && !report.errors.Contains(text)) { report.errors.Add(text); Save(); }
        }
        private void Save()
        {
            if (report == null || string.IsNullOrEmpty(output)) return;
            report.phase = phase.ToString();
            if (lesson?.Progress != null)
            { report.step = lesson.Progress.Step.ToString(); report.value = lesson.Progress.Value; report.target = lesson.Progress.Target; }
            if (bootstrap?.armController != null)
            {
                var arm = bootstrap.armController;
                report.armState = arm.State.ToString(); report.deployment = arm.VisibleDeployment01;
                var marker = bootstrap.robot.GetComponent<AnimalGame.RobotMap.RobotMarkerView>();
                if (marker?.MarkerVisualRoot != null)
                    report.handCentreLocal = marker.MarkerVisualRoot.InverseTransformPoint((arm.LeftHandWorld + arm.RightHandWorld) * .5f);
                bootstrap.map.TrySampleWorldPosition(bootstrap.robot.transform.position, out report.robotMapPosition, out _);
            }
            File.WriteAllText(Path.Combine(output, "arm-play-smoke.json"), JsonUtility.ToJson(report, true));
        }
        private void Finish(Exception error)
        {
            if (phase == Phase.Finishing) return;
            if (error != null) report.errors.Add(error.ToString());
            InputSystem.onBeforeUpdate -= Drive;
            if (cameraFollowSuspended && bootstrap?.cameraFollow != null) bootstrap.cameraFollow.enabled = true;
            var pad = InputSystem.GetDeviceById(gamepadId); if (pad != null) InputSystem.RemoveDevice(pad);
            if (originalInput != null) InputSystem.settings = originalInput;
            if (testInput != null) DestroyImmediate(testInput);
            Application.runInBackground = originalBackground;
            bootstrap?.mainUiObject?.GetComponent<AnimalGame.MainUI.MainUI>()?.CloseInventory();
            Begin(Phase.Finishing); EditorApplication.isPlaying = false;
        }
        private void Exit()
        {
            if (report.knownSceneHeaders > 0 && report.knownMissingPrefabMessages == 0)
                report.errors.Add("A scene-load failure lacked the exact known two-Static_Animal missing-prefab diagnostic.");
            report.passed = report.errors.Count == 0; Save(); running = false; Unsubscribe();
            EditorApplication.Exit(report.passed ? 0 : 1);
        }
    }
}
