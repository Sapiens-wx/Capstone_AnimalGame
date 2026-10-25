using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AnimalGame.MapTest;
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
    /// <summary>End-to-end virtual-gamepad smoke. Explicit batch entry, disposable project only.</summary>
    public sealed class TutorialDrivingPlaySmoke : ScriptableObject
    {
        private const string RunSessionKey = "AnimalGame.TutorialDrivingPlaySmoke.CurrentRun";
        private const string KnownMissingStaticAnimal = "Prefab instance problem: 'Static_Animal (Missing Prefab with guid: 2645e53e76c8ec846b09ee78b13275be)'. 2 instances are missing the same Prefab Asset with guid 2645e53e76c8ec846b09ee78b13275be.";
        private const string KnownSceneHeader = "Problem detected while opening the Scene file: 'Assets/Scenes/GameMaps/TutorialTemporaryMap.unity'.\nCheck the following logs for more details.";
        private enum Phase { Starting, Ready, Collapsed, Released, Expanded, RightCollapsed, RightReleased,
            RightExpanded, BeforeRightHold, RightHold, Paused, Resumed, Driving, Complete,
            TerrainStarting, TerrainRightCollapsed, TerrainRightReleased, TerrainRightExpanded,
            TerrainTravel, TerrainAtSurvey, TerrainLongPress, TerrainCancelledCharge,
            TerrainScan, TerrainRead, TerrainReadPaused, TerrainReadResumed, TerrainRescan, TerrainRoute,
            TerrainComplete, Capturing, Removed, Cleanup, Finishing }
        [Serializable] private sealed class Report
        {
            public bool passed;
            public string project, scene = TutorialDrivingLesson.ScenePath;
            public List<string> checks = new List<string>();
            public List<string> errors = new List<string>();
            public List<string> knownSceneDiagnostics = new List<string>();
            public int knownSceneHeaders, knownMissingPrefabMessages, drivingConfirmations;
            public string phase;
            public Vector2 robotMapPosition, destination;
            public float speed, throttle, navigationAngle;
            public bool gameplayBlocked;
        }
        [SerializeField] private bool running;
        [SerializeField] private string runId;
        [SerializeField] private Phase phase;
        [SerializeField] private Report report;
        [SerializeField] private int phaseFrame, lastFrame = -1, gamepadId;
        [SerializeField] private double deadline;
        [SerializeField] private string outputDirectory;
        [SerializeField] private HeightMapPlayerSceneBootstrap bootstrap;
        [SerializeField] private TutorialDrivingLesson lesson;
        [SerializeField] private DrivingTutorialStep lastStep;
        [SerializeField] private bool observedMovingTurn;
        [SerializeField] private bool observedTurningWhileDriving;
        [SerializeField] private InputSettings originalInput, testInput;
        [SerializeField] private bool originalBackground;
        [SerializeField] private RenderTexture captureTexture, priorCaptureTarget;
        [SerializeField] private PanelSettings captureSettings;
        [SerializeField] private string captureFilename;
        [SerializeField] private Phase afterCapture;
        [SerializeField] private bool includeTerrain, observedInView, observedBothSigns, checkedTerrainPause;
        [SerializeField] private TutorialTerrainLesson terrain;
        [SerializeField] private float frozenReading;
        [SerializeField] private TerrainTutorialStep lastTerrainStep;
        [SerializeField] private Phase lastShortRightTap;
        [SerializeField] private bool passedApproachBend;
        [SerializeField] private bool cameraFollowSuspended;
        [SerializeField] private float navigationAngle, navigationLastMovedAt;
        [SerializeField] private Vector2 navigationLastPosition;

        public static void RunBatch() => RunBatchInternal(false);
        public static void RunTerrainBatch() => RunBatchInternal(true);

        private static void RunBatchInternal(bool includeTerrain)
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            if (!Application.isBatchMode || !project.Contains("/output/validation/"))
                throw new InvalidOperationException("Run the driving Play Mode smoke in a disposable output/validation project.");
            SessionState.EraseString(RunSessionKey);
            foreach (var previous in Resources.FindObjectsOfTypeAll<TutorialDrivingPlaySmoke>())
            {
                previous.running = false;
                previous.Unsubscribe();
                DestroyImmediate(previous);
            }
            var test = CreateInstance<TutorialDrivingPlaySmoke>();
            test.runId = Guid.NewGuid().ToString("N");
            SessionState.SetString(RunSessionKey, test.runId);
            test.hideFlags = HideFlags.HideAndDontSave;
            test.includeTerrain = includeTerrain;
            test.outputDirectory = Path.Combine(project, includeTerrain ? "output/validation/tutorial-terrain" : "output/validation/tutorial-driving");
            Directory.CreateDirectory(test.outputDirectory);
            test.report = new Report { project = project };
            EditorSceneManager.OpenScene(TutorialDrivingLesson.ScenePath, OpenSceneMode.Single);
            test.running = true;
            test.deadline = EditorApplication.timeSinceStartup + (includeTerrain ? 420 : 210);
            test.Begin(Phase.Starting);
            test.Subscribe();
            EditorApplication.isPlaying = true;
        }

        private bool IsCurrentRun => !string.IsNullOrEmpty(runId) && SessionState.GetString(RunSessionKey, "") == runId;
        private void OnEnable() { if (running && IsCurrentRun) Subscribe(); }
        private void OnDisable() { Unsubscribe(); }
        private void Subscribe()
        {
            if (!IsCurrentRun) return;
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
            InputSystem.onBeforeUpdate -= Drive; InputSystem.onBeforeUpdate += Drive;
        }
        private void Unsubscribe()
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            InputSystem.onBeforeUpdate -= Drive;
        }
        private void Tick()
        {
            if (!running || !IsCurrentRun) return;
            try
            {
                if (phase == Phase.Finishing)
                {
                    if (!EditorApplication.isPlayingOrWillChangePlaymode) Exit();
                    return;
                }
                Require(EditorApplication.timeSinceStartup < deadline, "Timed out in " + phase);
                EditorApplication.QueuePlayerLoopUpdate();
                if (!EditorApplication.isPlaying || Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount;
                if (Time.frameCount % 30 == 0) Save();
                switch (phase)
                {
                    case Phase.Starting:
                        bootstrap = Object.FindFirstObjectByType<HeightMapPlayerSceneBootstrap>();
                        lesson = Object.FindFirstObjectByType<TutorialDrivingLesson>();
                        if (bootstrap == null || bootstrap.robot == null || !bootstrap.map.HasGeneratedMap
                            || lesson == null || lesson.Panel == null || lesson.Panel.Root?.panel == null) return;
                        originalInput = InputSystem.settings;
                        testInput = Instantiate(originalInput); testInput.hideFlags = HideFlags.HideAndDontSave;
                        testInput.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                        testInput.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                        InputSystem.settings = testInput;
                        originalBackground = Application.runInBackground; Application.runInBackground = true;
                        gamepadId = InputSystem.AddDevice<Gamepad>("Driving tutorial validation gamepad").deviceId;
                        Begin(Phase.Ready); break;
                    case Phase.Ready:
                        if (!Settled(8)) return;
                        Require(lesson.Progress.Step == DrivingTutorialStep.TurnLeft && lesson.Progress.Value == 0f,
                            "Idle robot advanced the lesson.");
                        Require(lesson.Panel.Root.Q<Label>("TutorialHeader")?.text == "TUTORIAL",
                            "The saved tutorial scene still displays its former support header.");
                        Require(lesson.Panel.Root.Q<Label>("TutorialToggleHint")?.text == "Tap D-pad ← / →  Hide",
                            "The task panel has no permanent left/right D-pad help.");
                        Record("Saved tutorial scene starts with the existing robot, HUD and a separate readable right-side lesson.");
                        BeginCapture("driving-start.png", Phase.Collapsed); break;
                    case Phase.Collapsed:
                        if (!Settled(4)) return;
                        Require(lesson.Panel.IsCollapsed, "D-pad left did not collapse the panel.");
                        BeginCapture("driving-collapsed.png", Phase.Released); break;
                    case Phase.Released:
                        if (Settled(4)) Begin(Phase.Expanded);
                        break;
                    case Phase.Expanded:
                        if (!Settled(4)) return;
                        Require(!lesson.Panel.IsCollapsed, "D-pad left did not reopen the panel.");
                        Record("D-pad left collapses and reopens the task panel without advancing the task or consuming A.");
                        Begin(Phase.RightCollapsed); break;
                    case Phase.RightCollapsed:
                        if (!Settled(4)) return;
                        Require(lesson.Panel.IsCollapsed && !AnimalGame.MainUI.MainUI.BlocksGameplay,
                            "A short D-pad right press did not hide the panel or incorrectly opened inventory.");
                        Require(lesson.Panel.Root.Q<Label>("TutorialTabToggleArrows")?.text == "← →"
                            && lesson.Panel.Root.Q<Label>("TutorialTabToggleHint")?.text == "Show",
                            "The collapsed panel does not show its left/right reopening hint.");
                        Begin(Phase.RightReleased); break;
                    case Phase.RightReleased:
                        if (Settled(4)) Begin(Phase.RightExpanded);
                        break;
                    case Phase.RightExpanded:
                        if (!Settled(4)) return;
                        Require(!lesson.Panel.IsCollapsed && !AnimalGame.MainUI.MainUI.BlocksGameplay,
                            "A short D-pad right press did not reopen the panel.");
                        Record("D-pad right also hides/reopens the panel, without repeating while held; both directions are permanently explained on the card and tab.");
                        Begin(Phase.BeforeRightHold); break;
                    case Phase.BeforeRightHold:
                        if (Settled(4)) Begin(Phase.RightHold);
                        break;
                    case Phase.RightHold:
                        if (!AnimalGame.MainUI.MainUI.BlocksGameplay) return;
                        Require(lesson.Panel.IsCollapsed, "A held right D-pad input toggled the tutorial more than once.");
                        Record("The existing long right-D-pad hold still opens inventory; the tutorial does not consume the inventory binding.");
                        Begin(Phase.Paused); break;
                    case Phase.Paused:
                        if (!Settled(10)) return;
                        Require(Time.timeScale == 0f && lesson.Progress.Value == 0f,
                            "Paused input advanced the lesson or failed to pause.");
                        Require(lesson.Panel.Root.resolvedStyle.display == DisplayStyle.None, "Lesson covers the inventory.");
                        Require(lesson.Panel.IsCollapsed, "Inventory navigation toggles the hidden tutorial panel.");
                        bootstrap.mainUiObject.GetComponent<AnimalGame.MainUI.MainUI>().CloseInventory();
                        Begin(Phase.Resumed); break;
                    case Phase.Resumed:
                        if (!Settled(10) || AnimalGame.MainUI.MainUI.BlocksGameplay) return;
                        Record("Inventory pause hides the panel and ignores held throttle; resume restores the pending step.");
                        lesson.Panel.SetCollapsed(false);
                        lastStep = lesson.Progress.Step; Begin(Phase.Driving); break;
                    case Phase.Driving:
                        if (lesson.Progress.Step == DrivingTutorialStep.TurnLeft && lesson.Progress.Value > 1f
                            && bootstrap.robot.CurrentThrottleIntent > .12f && bootstrap.robot.CurrentSpeed > .05f
                            && Mathf.Abs(bootstrap.robot.CurrentTurnSpeed) > 1f)
                            observedTurningWhileDriving = true;
                        if (lesson.Progress.Step == DrivingTutorialStep.SteerWhileMoving
                            && lesson.Progress.MovingTurnDistanceMeters > .05f
                            && lesson.Progress.Value > 1f
                            && bootstrap.robot.CurrentThrottleIntent > .12f && bootstrap.robot.CurrentSpeed > .05f
                            && Mathf.Abs(bootstrap.robot.CurrentTurnSpeed) > 1f)
                            observedMovingTurn = true;
                        if (lastStep != lesson.Progress.Step)
                        {
                            if (lastStep == DrivingTutorialStep.TurnLeft)
                                Require(observedTurningWhileDriving, "The first turning lesson did not accept actual forward driving plus steering.");
                            if (lastStep == DrivingTutorialStep.SteerWhileMoving)
                                Require(observedMovingTurn, "Moving-turn lesson advanced without measured simultaneous driving and steering.");
                            Record("Actual virtual-gamepad motion completed " + lastStep + ".");
                            lastStep = lesson.Progress.Step;
                        }
                        Require(!bootstrap.robot.IsMovementLocked, "The lesson reached an unsafe/overturned state.");
                        if (!lesson.IsComplete) return;
                        Begin(Phase.Complete); break;
                    case Phase.Complete:
                        if (!Settled(8)) return;
                        report.drivingConfirmations = TutorialContinueSmokeInput.ConfirmationCount(lesson.Panel);
                        Require(report.drivingConfirmations == 7,
                            "All six driving goals and the completion card require a separate physical A confirmation after at least twelve ready frames.");
                        Record("Seven separate real A confirmations advance the six measured actions and the completed chapter; every ready card waited twelve frames before A.");
                        Require(Time.timeScale == 1f && bootstrap.robot.enabled, "Completion changes gameplay ownership.");
                        Require(observedMovingTurn, "The real controller never demonstrated simultaneous driving and steering.");
                        Require(observedTurningWhileDriving, "Actual moving steering was not counted during the first turn lesson.");
                        Record("Left/right steering comes first, then forward/reverse, moving steering and stop; the first turn accepts actual forward driving plus steering.");
                        BeginCapture("driving-complete.png", includeTerrain ? Phase.TerrainStarting : Phase.Removed); break;
                    case Phase.TerrainStarting:
                        terrain = lesson.GetComponent<TutorialTerrainLesson>();
                        Require(terrain != null, "Saved scene has no terrain lesson.");
                        if (!terrain.HasStarted || terrain.Waypoint.CurrentMode == TutorialWaypointIndicator.WaypointMode.Hidden) return;
                        Require(terrain.Progress.Step == TerrainTutorialStep.ReachSurvey, "Terrain lesson skipped travel.");
                        Require(Vector2.Distance(terrain.Progress.SurveyPosition, new Vector2(163, 140)) < .01f,
                            "The saved survey destination does not match the user's marked point 1.");
                        Require(terrain.Waypoint.CurrentMode == TutorialWaypointIndicator.WaypointMode.Edge,
                            "The distant survey marker was not clamped to the real HUD circle.");
                        Require(terrain.Waypoint.Image.sprite != null && terrain.Waypoint.Image.sprite.name == "Target_0"
                            && !terrain.Waypoint.Image.raycastTarget, "Target_0 sprite binding or input isolation failed.");
                        Record("Driving completion hands the same panel to English chapter 02/09 and the distant target appears on the circular rim.");
                        ScreenCapture.CaptureScreenshot(Path.Combine(outputDirectory, "terrain-waypoint-edge.png"));
                        lastTerrainStep = terrain.Progress.Step; Begin(Phase.TerrainRightCollapsed); break;
                    case Phase.TerrainRightCollapsed:
                        if (!Settled(4)) return;
                        Require(lesson.Panel.IsCollapsed, "D-pad right does not hide the terrain chapter panel.");
                        Begin(Phase.TerrainRightReleased); break;
                    case Phase.TerrainRightReleased:
                        if (Settled(4)) Begin(Phase.TerrainRightExpanded);
                        break;
                    case Phase.TerrainRightExpanded:
                        if (!Settled(4)) return;
                        Require(!lesson.Panel.IsCollapsed && terrain.Progress.Step == TerrainTutorialStep.ReachSurvey,
                            "Right D-pad failed to reopen terrain teaching or advanced the navigation task.");
                        Record("The terrain chapter also supports both D-pad directions without advancing its destination task.");
                        Begin(Phase.TerrainTravel); break;
                    case Phase.TerrainTravel:
                        CheckNavigationProgress();
                        Require(!AnimalGame.MainUI.MainUI.BlocksGameplay, "Survey travel is unexpectedly paused by inventory.");
                        Require(!bootstrap.robot.IsMovementLocked && !bootstrap.robot.IsLevelThreeUnstable,
                            "Survey approach led the real robot onto unsafe ground.");
                        if (!observedInView && terrain.Waypoint.CurrentMode == TutorialWaypointIndicator.WaypointMode.InView)
                        {
                            observedInView = true;
                            Vector3 projected = bootstrap.mapCamera.WorldToScreenPoint(bootstrap.map.MapPositionToWorld(terrain.Progress.CurrentTarget));
                            Require(Mathf.Abs(terrain.Waypoint.CurrentScreenPosition.x - projected.x) < 1.5f
                                && Mathf.Abs(terrain.Waypoint.CurrentRotationDegrees) < .01f,
                                "In-view marker does not point vertically above its world destination.");
                            ScreenCapture.CaptureScreenshot(Path.Combine(outputDirectory, "terrain-waypoint-in-view.png"));
                            Record("Actual controller travel switches the rim marker to an upright destination marker using the final camera projection.");
                        }
                        if (terrain.Progress.Step == TerrainTutorialStep.ReachSurvey) return;
                        Require(observedInView && terrain.Progress.Step == TerrainTutorialStep.ScanTerrain,
                            "Survey arrival skipped scan or never displayed its in-view marker.");
                        Require(!terrain.Waypoint.HasTarget, "Survey marker did not disappear on arrival.");
                        Begin(Phase.TerrainAtSurvey); break;
                    case Phase.TerrainAtSurvey:
                        if (!Settled(12) || Mathf.Abs(bootstrap.robot.CurrentSpeed) > .03f) return;
                        Record("The original robot reached the marked survey area and the triangle disappeared without changing terrain rules.");
                        BeginCapture("terrain-scan.png", Phase.TerrainLongPress); break;
                    case Phase.TerrainLongPress:
                        if (!Settled(24)) return;
                        Require(terrain.Progress.Step == TerrainTutorialStep.ScanTerrain,
                            "A long biological scan press incorrectly completed the terrain scan instruction.");
                        Begin(Phase.TerrainCancelledCharge); break;
                    case Phase.TerrainCancelledCharge:
                        if (!Settled(35) || bootstrap.scanChargeUi.IsCharging) return;
                        Record("Holding LB for a biological charge does not satisfy the double-tap terrain lesson.");
                        Begin(Phase.TerrainScan); break;
                    case Phase.TerrainScan:
                    case Phase.TerrainRescan:
                        if (terrain.Progress.Step != TerrainTutorialStep.ReadSigns) return;
                        Record("Two real short LB taps trigger the production terrain scan event in the survey area.");
                        Begin(Phase.TerrainRead); break;
                    case Phase.TerrainRead:
                        var passable = (System.Collections.ICollection)typeof(TraversalScanOverlayUI).GetField("passableRenderData",
                            BindingFlags.NonPublic | BindingFlags.Instance).GetValue(bootstrap.scanOverlay);
                        var unsafeSigns = (System.Collections.ICollection)typeof(TraversalScanOverlayUI).GetField("unpassableRenderData",
                            BindingFlags.NonPublic | BindingFlags.Instance).GetValue(bootstrap.scanOverlay);
                        observedBothSigns |= passable.Count > 0 && unsafeSigns.Count > 0;
                        if (!checkedTerrainPause && terrain.Progress.ReadingSeconds >= .5f)
                        {
                            frozenReading = terrain.Progress.ReadingSeconds;
                            bootstrap.mainUiObject.GetComponent<AnimalGame.MainUI.MainUI>().OpenInventory();
                            checkedTerrainPause = true; Begin(Phase.TerrainReadPaused); return;
                        }
                        if (terrain.Progress.Step == TerrainTutorialStep.ScanTerrain) { Begin(Phase.TerrainRescan); return; }
                        if (terrain.Progress.Step != TerrainTutorialStep.FollowRoute) return;
                        Require(observedBothSigns, "Survey point did not show both actual passable and unsafe scan signs.");
                        Require(Vector2.Distance(terrain.Progress.CurrentTarget, new Vector2(146, 121)) < .01f,
                            "The uphill destination does not match the user's marked point 2.");
                        Record("Both real + and X signs are visible; only expanded, stopped, unpaused reading advances the lesson.");
                        BeginCapture("terrain-slopes.png", Phase.TerrainRoute); break;
                    case Phase.TerrainReadPaused:
                        if (!Settled(10)) return;
                        Require(Mathf.Abs(terrain.Progress.ReadingSeconds - frozenReading) < .002f
                            && !lesson.Panel.IsVisible && terrain.Waypoint.CurrentMode == TutorialWaypointIndicator.WaypointMode.Hidden,
                            "Terrain reading progresses or its UI remains visible during the inventory pause.");
                        bootstrap.mainUiObject.GetComponent<AnimalGame.MainUI.MainUI>().CloseInventory();
                        Begin(Phase.TerrainReadResumed); break;
                    case Phase.TerrainReadResumed:
                        if (!Settled(10) || AnimalGame.MainUI.MainUI.BlocksGameplay) return;
                        Record("Inventory pause hides terrain UI and freezes reading; reading or re-scanning resumes safely.");
                        Begin(terrain.Progress.Step == TerrainTutorialStep.ScanTerrain ? Phase.TerrainRescan : Phase.TerrainRead); break;
                    case Phase.TerrainRoute:
                        CheckNavigationProgress();
                        Require(!AnimalGame.MainUI.MainUI.BlocksGameplay, "Slope travel is unexpectedly paused by inventory.");
                        Require(!bootstrap.robot.IsMovementLocked && !bootstrap.robot.IsLevelThreeUnstable,
                            "The point 1-to-2 slope route entered an unsafe state.");
                        if (!terrain.IsComplete) return;
                        Require(!terrain.Waypoint.HasTarget && terrain.Waypoint.CurrentMode == TutorialWaypointIndicator.WaypointMode.Hidden,
                            "Final destination marker remains after arrival.");
                        Record("Real gamepad driving climbs from marked point 1 to point 2; arrival completes chapter 02/09 and removes its triangle.");
                        BeginCapture("terrain-complete.png", Phase.TerrainComplete); break;
                    case Phase.TerrainComplete:
                        Require(Time.timeScale == 1f && bootstrap.robot.enabled
                            && lesson.Panel.Root.Q<Label>("TutorialChapter").text == "02 / 09",
                            "Terrain completion altered gameplay ownership or lost the chapter handoff.");
                        Begin(Phase.Removed); break;
                    case Phase.Capturing:
                        if (!Settled(6)) return;
                        EndCapture(); Begin(afterCapture); break;
                    case Phase.Removed:
                        Object.Destroy(lesson.gameObject); Begin(Phase.Cleanup); break;
                    case Phase.Cleanup:
                        if (!Settled(8)) return;
                        Require(Object.FindFirstObjectByType<TutorialDialoguePanel>() == null
                            && Object.FindFirstObjectByType<TutorialWaypointIndicator>() == null
                            && bootstrap.robot.enabled && bootstrap.mainUiObject.activeInHierarchy && Time.timeScale == 1f,
                            "Removing the lesson leaves tutorial UI behind or alters normal gameplay.");
                        Record("Removing the scene-owned lesson removes its UI while leaving the original robot and HUD running.");
                        Finish(null); break;
                }
            }
            catch (Exception error) { Finish(error); }
        }

        private void Drive()
        {
            if (!running || !IsCurrentRun || phase == Phase.Finishing || InputState.currentUpdateType != InputUpdateType.Dynamic) return;
            // Editor-driven zero-delta frames can poison SmoothDamp's velocity on the existing
            // camera. Keep that unrelated smoothing out of the input/progress fixture at dt=0.
            if (bootstrap != null && bootstrap.cameraFollow != null)
            {
                if (Time.deltaTime <= 0f && bootstrap.cameraFollow.enabled)
                {
                    bootstrap.cameraFollow.enabled = false;
                    cameraFollowSuspended = true;
                }
                else if (Time.deltaTime > 0f && cameraFollowSuspended)
                {
                    bootstrap.cameraFollow.enabled = true;
                    cameraFollowSuspended = false;
                }
            }
            var gamepad = InputSystem.GetDeviceById(gamepadId) as Gamepad;
            if (gamepad == null) return;
            var state = new GamepadState();
            if (phase == Phase.Collapsed || phase == Phase.Expanded) state = state.WithButton(GamepadButton.DpadLeft);
            if (phase == Phase.RightHold)
                state = state.WithButton(GamepadButton.DpadRight);
            else if ((phase == Phase.RightCollapsed || phase == Phase.RightExpanded
                || phase == Phase.TerrainRightCollapsed || phase == Phase.TerrainRightExpanded) && lastShortRightTap != phase)
            {
                // Emit a true short tap, independent of variable batch-editor frame durations.
                state = state.WithButton(GamepadButton.DpadRight);
                lastShortRightTap = phase;
            }
            if (phase == Phase.Paused) { state.rightTrigger = 1f; state = state.WithButton(GamepadButton.DpadLeft); }
            if (phase == Phase.Driving && lesson != null && lesson.Progress != null)
                switch (lesson.Progress.Step)
                {
                    case DrivingTutorialStep.Forward: state.rightTrigger = 1f; break;
                    case DrivingTutorialStep.TurnLeft:
                        state.leftStick = new Vector2(-.65f, 0f); state.rightTrigger = .25f; break;
                    case DrivingTutorialStep.TurnRight: state.leftStick = Vector2.right; break;
                    case DrivingTutorialStep.SteerWhileMoving:
                        state.rightTrigger = .45f; state.leftStick = new Vector2(-.65f, 0f); break;
                    case DrivingTutorialStep.Reverse: state.leftTrigger = 1f; break;
                }
            if ((phase == Phase.TerrainTravel || phase == Phase.TerrainRoute) && terrain != null && terrain.Progress.HasTarget)
            {
                Vector2 current = default;
                bootstrap.map.TrySampleWorldPosition(bootstrap.robot.transform.position, out current, out _);
                Vector2 navigationTarget = terrain.Progress.CurrentTarget;
                // A player can steer around existing trees. Keep this automated driver clear too;
                // the visible lesson destination remains the user's point 1 throughout the approach.
                if (phase == Phase.TerrainTravel && !passedApproachBend)
                {
                    Vector2 bend = new Vector2(181f, 151f);
                    if (Vector2.Distance(current, bend) <= 2f) passedApproachBend = true;
                    else navigationTarget = bend;
                }
                Vector2 direction = bootstrap.map.MapDirectionToWorldDirection(navigationTarget - current);
                float angle = Vector2.SignedAngle(bootstrap.robot.Forward, direction);
                navigationAngle = angle;
                float steering = Mathf.Clamp(-angle / 18f, -1f, 1f);
                if (Mathf.Abs(angle) <= 1.5f) steering = 0f;
                else if (Mathf.Abs(steering) < .4f) steering = Mathf.Sign(steering) * .4f;
                // Clear both the Input System stick processor and the robot's steering dead zone.
                state.leftStick = new Vector2(steering, 0f);
                if (Mathf.Abs(angle) < 4f) state.rightTrigger = .45f;
            }
            if (phase == Phase.TerrainLongPress) state = state.WithButton(GamepadButton.LeftShoulder);
            if (phase == Phase.TerrainScan || phase == Phase.TerrainRescan)
            {
                // Short presses on frames 0 and 4 with releases on frames 1 and 5.
                int frame = Time.frameCount - phaseFrame;
                if (frame % 30 == 0 || frame % 30 == 4) state = state.WithButton(GamepadButton.LeftShoulder);
            }
            if (phase == Phase.Driving && lesson?.Progress != null)
                TutorialContinueSmokeInput.Apply(ref state, lesson.Panel, "Driving:" + lesson.Progress.Step);
            if ((phase == Phase.TerrainTravel || phase == Phase.TerrainScan || phase == Phase.TerrainRescan
                || phase == Phase.TerrainRead || phase == Phase.TerrainRoute) && terrain?.Progress != null)
                TutorialContinueSmokeInput.Apply(ref state, lesson.Panel, "Terrain:" + terrain.Progress.Step);
            gamepad.MakeCurrent(); InputSystem.QueueStateEvent(gamepad, state);
        }

        private void BeginCapture(string filename, Phase next)
        {
            captureFilename = filename; afterCapture = next;
            captureSettings = lesson.Panel.Document.panelSettings;
            priorCaptureTarget = captureSettings.targetTexture;
            captureTexture = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            captureTexture.Create();
            captureSettings.targetTexture = captureTexture;
            Begin(Phase.Capturing);
        }

        private void EndCapture()
        {
            RenderTexture oldActive = RenderTexture.active;
            Texture2D image = null;
            try
            {
                RenderTexture.active = captureTexture;
                image = new Texture2D(captureTexture.width, captureTexture.height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0); image.Apply();
                Require(image.GetPixels32().Count(pixel => pixel.a > 100 && (pixel.r > 120 || pixel.g > 120 || pixel.b > 120)) > 300,
                    "The actual running tutorial rendered no visible foreground content.");
                File.WriteAllBytes(Path.Combine(outputDirectory, captureFilename), image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = oldActive;
                if (image != null) Object.DestroyImmediate(image);
                RestoreCapture();
            }
        }

        private void RestoreCapture()
        {
            if (captureSettings != null) captureSettings.targetTexture = priorCaptureTarget;
            if (captureTexture != null) { captureTexture.Release(); Object.DestroyImmediate(captureTexture); }
            captureSettings = null; captureTexture = null; priorCaptureTarget = null;
        }

        private void Begin(Phase next)
        {
            phase = next; phaseFrame = Time.frameCount;
            navigationLastMovedAt = Time.unscaledTime;
            if (bootstrap != null && bootstrap.map != null && bootstrap.robot != null)
                bootstrap.map.TrySampleWorldPosition(bootstrap.robot.transform.position, out navigationLastPosition, out _);
            Save();
        }
        private void CheckNavigationProgress()
        {
            bootstrap.map.TrySampleWorldPosition(bootstrap.robot.transform.position, out Vector2 current, out _);
            if (Vector2.Distance(current, navigationLastPosition) > .1f)
            {
                navigationLastPosition = current;
                navigationLastMovedAt = Time.unscaledTime;
            }
            Require(Time.unscaledTime - navigationLastMovedAt < 10f,
                $"Automated navigation stalled at {current}, heading error {navigationAngle:F1} degrees.");
        }
        private bool Settled(int frames) => Time.frameCount - phaseFrame >= frames;
        private void Record(string text) { report.checks.Add(text); Save(); }
        private static void Require(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); }
        private void OnLog(string text, string stack, LogType type)
        {
            if (!IsCurrentRun || report == null) return;
            string normalized = text.Replace("\r\n", "\n").Trim();
            if (type == LogType.Error && (phase == Phase.Starting || phase == Phase.Finishing)
                && (normalized == KnownMissingStaticAnimal || normalized == KnownSceneHeader))
            {
                if (normalized == KnownMissingStaticAnimal) report.knownMissingPrefabMessages++; else report.knownSceneHeaders++;
                if (!report.knownSceneDiagnostics.Contains(normalized)) report.knownSceneDiagnostics.Add(normalized);
                Save(); return;
            }
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
                if (report != null && !report.errors.Contains(text)) { report.errors.Add(text); Save(); }
        }
        private void Save()
        {
            report.phase = phase.ToString();
            report.navigationAngle = navigationAngle;
            report.gameplayBlocked = AnimalGame.MainUI.MainUI.BlocksGameplay;
            if (bootstrap != null && bootstrap.robot != null)
            {
                report.speed = bootstrap.robot.CurrentSpeed;
                report.throttle = bootstrap.robot.CurrentThrottleIntent;
                if (bootstrap.map != null)
                    bootstrap.map.TrySampleWorldPosition(bootstrap.robot.transform.position, out report.robotMapPosition, out _);
            }
            if (terrain != null && terrain.Progress != null && terrain.Progress.HasTarget)
                report.destination = terrain.Progress.CurrentTarget;
            File.WriteAllText(Path.Combine(outputDirectory, "play-smoke.json"), JsonUtility.ToJson(report, true));
        }
        private void Finish(Exception error)
        {
            if (phase == Phase.Finishing) return;
            if (error != null) report.errors.Add(error.ToString());
            RestoreCapture();
            if (cameraFollowSuspended && Time.deltaTime > 0f && bootstrap != null && bootstrap.cameraFollow != null)
                bootstrap.cameraFollow.enabled = true;
            InputSystem.onBeforeUpdate -= Drive;
            var gamepad = InputSystem.GetDeviceById(gamepadId);
            if (gamepad != null) InputSystem.RemoveDevice(gamepad);
            if (originalInput != null) InputSystem.settings = originalInput;
            if (testInput != null) DestroyImmediate(testInput);
            Application.runInBackground = originalBackground;
            Begin(Phase.Finishing);
            EditorApplication.isPlaying = false;
        }
        private void Exit()
        {
            if (report.knownSceneHeaders > 0 && report.knownMissingPrefabMessages == 0)
                report.errors.Add("A scene-load failure lacked the exact known two-Static_Animal missing-prefab diagnostic.");
            report.passed = report.errors.Count == 0;
            Save(); running = false; Unsubscribe();
            EditorApplication.Exit(report.passed ? 0 : 1);
        }
    }
}
