using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AnimalGame.Animals;
using AnimalGame.Demos.CheckpointRefit;
using AnimalGame.Demos.WasteRobot;
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
    /// <summary>Real portal, maintenance, equipment and tutorial-input checks in a disposable project.</summary>
    public sealed class TutorialRefitPlaySmoke : ScriptableObject
    {
        private const string SessionKey = "AnimalGame.TutorialRefitPlaySmoke.CurrentRun";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string KnownSceneHeader = "Problem detected while opening the Scene file: 'Assets/Scenes/GameMaps/TutorialTemporaryMap.unity'.\nCheck the following logs for more details.";
        private const string KnownMissingStaticAnimal = "Prefab instance problem: 'Static_Animal (Missing Prefab with guid: 2645e53e76c8ec846b09ee78b13275be)'. 2 instances are missing the same Prefab Asset with guid 2645e53e76c8ec846b09ee78b13275be.";
        private enum Phase
        {
            Starting, Foundations, WaitRecyclingConfirmation, ConfirmRecycling, ReachEntrance, WaitEntranceConfirmation,
            ConfirmEntrance, EnterMachine, ReachWorkbench, OpenWorkbench, WaitIntroduction,
            SelectCameraSlot, SelectLens, EquipLens, HoldAfterEquipment, NeutralAfterEquipment,
            CloseWorkbench, ReadNewControls, ConfirmControls, ConfirmCompletion, FinalRelease, FinalHide, FinalShow,
            ReopenNormally, SelectOldNormally, EquipOldNormally, SelectTargetNormally, EquipTargetNormally,
            CloseNormally, Cleanup, Finishing
        }
        [Serializable] private sealed class Report
        {
            public bool passed, waitedForRecyclingA, entryReachedWithNativeInput, enteredWithNativeInput;
            public bool targetSwitchedWithoutA, reachedWorkbenchWithoutA, openedWithNativeA;
            public bool noMenuGuide, noMenuSubmitInterception, nativeCameraSlot, nativeLensSelection;
            public bool installedWithNativeA, heldEquipDidNotContinue, separateEquipmentConfirmation;
            public bool closedWithNativeB, retainedInterior, retainedCamera, completedWithA, cleanupReleasedTutorial;
            public bool finalADidNotReopen, finalPanelHidden, finalPanelShown, normalReopen, ordinaryEquipmentRestored;
            public bool terminalIsNearby, terminalIsOpen, terminalInputArmed, terminalReleasing;
            public bool terminalHasOpenBlockHandler, tutorialPromptReady, heldContinueBlock, worldSouthHeld, canAcquireMaintenance;
            public string project, phase, step, targetCamera, actualCamera, movementMode, failureReason, foundationStage;
            public string firstGamepadName;
            public float interactionRadius, speed, throttle, turnSpeed, targetAngle, targetDistanceMeters, timeScale;
            public Vector2 playerMapPosition, playerPortalPosition, waypointMapPosition, queuedLeftStick;
            public float queuedRightTrigger, traversalSlope;
            public float nativeTriggerThrottle, firstGamepadRightTrigger, virtualGamepadRightTrigger;
            public int connectedGamepads;
            public Vector3 machinePosition, terminalLocalPosition;
            public int nativeTransitions, equipRequests, knownSceneHeaders, knownMissingPrefabMessages;
            public List<string> checks = new(), errors = new(), knownSceneDiagnostics = new(), screenshotRequests = new();
        }
        [SerializeField] private string runId, output;
        [SerializeField] private bool running, originalBackground, fixturePrepared, cameraFollowSuspended;
        [SerializeField] private int phaseFrame, lastFrame = -1, gamepadId, tapPhase = -1, tapFrame;
        [SerializeField] private double deadline, phaseStartedAt;
        [SerializeField] private Phase phase;
        [SerializeField] private Report report;
        [SerializeField] private InputSettings originalInput, testInput;
        [SerializeField] private HeightMapPlayerSceneBootstrap bootstrap;
        [SerializeField] private TutorialDrivingLesson driving;
        [SerializeField] private TutorialPhotographyLesson photography;
        [SerializeField] private TutorialRecyclingLesson recycling;
        [SerializeField] private TutorialRefitLesson lesson;
        [SerializeField] private WasteRobotBase machine;
        [SerializeField] private CheckpointRefitTerminal terminal;
        [SerializeField] private PhotoCameraDefinition damagedCamera;
        [SerializeField] private Vector3 machinePosition, machineScale, terminalPosition, terminalScale;
        [SerializeField] private Quaternion machineRotation, terminalRotation;
        [SerializeField] private Transform terminalParent;
        private Vector2 driveTarget;
        private bool targetRecorded;

        public static void RunBatch()
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            if (!Application.isBatchMode || !project.Contains("/output/validation/"))
                throw new InvalidOperationException("Run refit tutorial checks only in a disposable output/validation project.");
            SessionState.EraseString(SessionKey);
            foreach (var old in Resources.FindObjectsOfTypeAll<TutorialRefitPlaySmoke>())
            { old.running = false; old.Unsubscribe(); DestroyImmediate(old); }
            var test = CreateInstance<TutorialRefitPlaySmoke>();
            test.hideFlags = HideFlags.HideAndDontSave;
            test.runId = Guid.NewGuid().ToString("N"); SessionState.SetString(SessionKey, test.runId);
            string configured = Environment.GetEnvironmentVariable("TUTORIAL_REFIT_VALIDATION_OUTPUT");
            test.output = string.IsNullOrWhiteSpace(configured) ? Path.Combine(project, "output/validation/tutorial-refit")
                : Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(project, configured));
            Directory.CreateDirectory(test.output); test.report = new Report { project = project };
            typeof(TutorialPhotographyPlaySmoke).GetMethod("ApplyGameViewSize", BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, null);
            test.Subscribe();
            EditorSceneManager.OpenScene(TutorialDrivingLesson.ScenePath, OpenSceneMode.Single);
            test.deadline = EditorApplication.timeSinceStartup + 300;
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
            EditorApplication.update -= Tick; InputSystem.onBeforeUpdate -= Drive;
            Application.logMessageReceived -= OnLog;
            if (terminal?.View != null) terminal.View.EquipRequested -= OnEquipRequested;
        }
        private void OnEquipRequested() => report.equipRequests++;

        private void Tick()
        {
            if (!running || !Current) return;
            try
            {
                if (phase == Phase.Finishing)
                { if (!EditorApplication.isPlayingOrWillChangePlaymode) Exit(); return; }
                Require(EditorApplication.timeSinceStartup < deadline, "Refit tutorial timed out in " + phase + ".");
                EditorApplication.QueuePlayerLoopUpdate();
                if (!EditorApplication.isPlaying || Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount;
                if (fixturePrepared) CheckAuthoredObjects();
                if (Time.frameCount % 30 == 0) Save();
                switch (phase)
                {
                    case Phase.Starting:
                        bootstrap = Object.FindFirstObjectByType<HeightMapPlayerSceneBootstrap>();
                        driving = Object.FindFirstObjectByType<TutorialDrivingLesson>();
                        photography = Object.FindFirstObjectByType<TutorialPhotographyLesson>();
                        recycling = Object.FindFirstObjectByType<TutorialRecyclingLesson>();
                        lesson = Object.FindFirstObjectByType<TutorialRefitLesson>();
                        if (bootstrap?.map == null || !bootstrap.map.HasGeneratedMap || bootstrap.robot == null
                            || driving?.Progress == null || lesson == null || photography == null || recycling == null || driving.Panel?.Root?.panel == null) return;
                        originalInput = InputSystem.settings; testInput = Instantiate(originalInput);
                        testInput.hideFlags = HideFlags.HideAndDontSave;
                        testInput.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                        testInput.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus; InputSystem.settings = testInput;
                        originalBackground = Application.runInBackground; Application.runInBackground = true;
                        // Adaptive legacy input reads the first live wrapper. Keep this disposable
                        // validation process deterministic without sending any OS controller input.
                        foreach (Gamepad other in new List<Gamepad>(Gamepad.all)) InputSystem.RemoveDevice(other);
                        gamepadId = InputSystem.AddDevice<Gamepad>("Refit tutorial validation gamepad").deviceId;
                        CompleteCounter(driving, driving.Progress, DrivingTutorialStep.Completed);
                        Begin(Phase.Foundations); break;
                    case Phase.Foundations:
                        var terrain = driving.GetComponent<TutorialTerrainLesson>();
                        if (terrain?.Progress != null) CompleteCounter(terrain, terrain.Progress, TerrainTutorialStep.Completed);
                        var balance = driving.GetComponent<TutorialBalanceLesson>();
                        if (balance?.Progress != null) CompleteCounter(balance, balance.Progress, BalanceTutorialStep.Completed);
                        var arms = driving.GetComponent<TutorialArmLesson>();
                        if (arms?.Progress != null) CompleteCounter(arms, arms.Progress, ArmTutorialStep.Completed);
                        var discovery = driving.GetComponent<TutorialDiscoveryLesson>();
                        report.foundationStage = "Terrain=" + terrain?.HasStarted + "/" + terrain?.IsComplete
                            + ", Balance=" + balance?.HasStarted + "/" + balance?.IsComplete
                            + ", Arms=" + arms?.HasStarted + "/" + arms?.IsComplete
                            + ", Discovery=" + discovery?.HasStarted + "/" + discovery?.IsComplete;
                        Require(EditorApplication.timeSinceStartup - phaseStartedAt < 25,
                            "Prior chapter counter fixture did not become ready: " + report.foundationStage);
                        if (terrain == null || !terrain.IsComplete || balance == null || !balance.IsComplete
                            || arms == null || !arms.IsComplete || discovery == null) return;
                        // This smoke starts after photography and recycling. Do not ask the scan lesson
                        // to spawn/initialize a fresh unknown animal at the unrelated original start.
                        var completedDiscovery = new DiscoveryTutorialProgress();
                        SetStep(completedDiscovery, DiscoveryTutorialStep.Completed);
                        typeof(TutorialDiscoveryLesson).GetField("progress", Flags).SetValue(discovery, completedDiscovery);
                        typeof(TutorialDiscoveryLesson).GetField("hasStarted", Flags).SetValue(discovery, true);
                        typeof(TutorialDiscoveryLesson).GetField("chapterConfirmed", Flags).SetValue(discovery, true);
                        arms.SetPresentationOwned(false); discovery.SetPresentationOwned(false);
                        var completedRecycling = new RecyclingTutorialProgress();
                        SetStep(completedRecycling, RecyclingTutorialStep.Completed);
                        typeof(TutorialRecyclingLesson).GetField("progress", Flags).SetValue(recycling, completedRecycling);
                        typeof(TutorialRecyclingLesson).GetField("hasStarted", Flags).SetValue(recycling, true);
                        typeof(TutorialRecyclingLesson).GetField("chapterConfirmed", Flags).SetValue(recycling, false);
                        typeof(TutorialRecyclingLesson).GetField("acceptedRecycle", Flags).SetValue(recycling, true);
                        recycling.SetPresentationOwned(true);
                        var completedPhoto = new PhotographyTutorialProgress();
                        SetStep(completedPhoto, PhotographyTutorialStep.Completed);
                        
                        typeof(TutorialPhotographyLesson).GetField("progress", Flags).SetValue(photography, completedPhoto);
                        typeof(TutorialPhotographyLesson).GetField("hasStarted", Flags).SetValue(photography, true);
                        typeof(TutorialPhotographyLesson).GetField("chapterConfirmed", Flags).SetValue(photography, true);
                        photography.SetPresentationOwned(false);
                        recycling.Panel.SetPhotoModeVisible(false); recycling.Panel.SetCollapsed(false);
                        PrepareFixture();
                        Record("The fixture skips preceding practice, photo and recycling counters only. Refit starts directly after recycling; only the player is placed outside the saved entrance, preserving the authored machine, station and collisions.");
                        Begin(Phase.WaitRecyclingConfirmation); break;
                    case Phase.WaitRecyclingConfirmation:
                        if (!Settled(12)) return;
                        Require(recycling.IsComplete, "Recycling is complete without another final confirmation card.");
                        report.waitedForRecyclingA = false; Begin(Phase.ConfirmRecycling); break;
                    case Phase.ConfirmRecycling:
                        if (!recycling.IsComplete || !lesson.HasStarted) return;
                        Require(lesson.ArrivalRadiusMeters >= 3f && terminal.InteractionRadiusMeters >= 2f,
                            "The tutorial scene uses the larger machine entrance and native station interaction areas.");
                        Require(lesson.Progress.Step == RefitTutorialStep.ReachMachine && lesson.Waypoint != null && lesson.Waypoint.HasTarget,
                            "The completed recycling chapter must hand its shared panel to the machine entrance waypoint.");
                        Record("A real A confirmation transfers the existing panel from recycling to Camera Upgrade.");
                        Begin(Phase.ReachEntrance); break;
                    case Phase.ReachEntrance:
                        Require(!machine.IsInside, "The entrance approach must stop outside before its A confirmation.");
                        if (!lesson.Progress.CanContinue || !Settled(5)) return;
                        report.entryReachedWithNativeInput = true; Begin(Phase.WaitEntranceConfirmation); break;
                    case Phase.WaitEntranceConfirmation:
                        if (!Settled(12)) return;
                        Require(lesson.Progress.Step == RefitTutorialStep.ReachMachine && !machine.IsInside,
                            "Arriving outside the machine must leave the entrance card pending A.");
                        Begin(Phase.ConfirmEntrance); break;
                    case Phase.ConfirmEntrance:
                        if (lesson.Progress.Step != RefitTutorialStep.EnterMachine) return;
                        Begin(Phase.EnterMachine); break;
                    case Phase.EnterMachine:
                        if (!machine.IsInside) return;
                        Require(lesson.Progress.Step == RefitTutorialStep.ReachWorkbench || lesson.Progress.Step == RefitTutorialStep.OpenWorkbench,
                            "Actual entry must immediately switch to the workbench task without another A.");
                        Require(lesson.Waypoint.HasTarget, "The workbench destination must immediately replace the entry destination.");
                        report.enteredWithNativeInput = report.targetSwitchedWithoutA = true;
                        Record("Real RT driving crosses the saved portal. IsInside immediately points the same target indicator toward the saved workbench without a confirmation press.", "refit-entered-machine.png");
                        Begin(Phase.ReachWorkbench); break;
                    case Phase.ReachWorkbench:
                        Require(machine.IsInside, "Workbench navigation must remain inside the authored machine.");
                        if (lesson.Progress.Step != RefitTutorialStep.OpenWorkbench) return;
                        Require(terminal.IsNearby && !terminal.IsOpen && !lesson.Panel.CanContinue,
                            "Workbench arrival must offer native A interaction directly, without an intermediate tutorial confirmation.");
                        report.reachedWorkbenchWithoutA = true;
                        Record("The original robot reaches the real station's enlarged tutorial interaction radius. No A confirmation is inserted before opening the station.", "refit-workbench-arrival.png");
                        Begin(Phase.OpenWorkbench); break;
                    case Phase.OpenWorkbench:
                        Require(EditorApplication.timeSinceStartup - phaseStartedAt < 25,
                            "The native station A did not open: nearby=" + report.terminalIsNearby
                            + ", inputArmed=" + report.terminalInputArmed + ", releasing=" + report.terminalReleasing
                            + ", continueBlock=" + report.heldContinueBlock + ", canAcquire=" + report.canAcquireMaintenance
                            + ", reason=" + report.failureReason);
                        if (!terminal.IsOpen || !Settled(6)) return;
                        CheckPausedNativeMenu();
                        Require(lesson.Progress.Step == RefitTutorialStep.EquipLens && bootstrap.photoMode.CurrentCamera == damagedCamera,
                            "Opening the station starts passive equipment observation without an in-menu tutorial.");
                        terminal.View.EquipRequested -= OnEquipRequested; terminal.View.EquipRequested += OnEquipRequested;
                        report.openedWithNativeA = true; Begin(Phase.WaitIntroduction); break;
                    case Phase.WaitIntroduction:
                        CheckPausedNativeMenu();
                        if (!Settled(12)) return;
                        Require(lesson.Progress.Step == RefitTutorialStep.EquipLens && !lesson.Progress.CanContinue
                            && report.equipRequests == 0,
                            "The native menu has no introduction card or tutorial confirmation before selection.");
                        Record("The outside task card gives the Zoom Lens 300 requirement; opening maintenance hides all tutorial UI and leaves native equipment controls untouched.", "refit-native-menu-no-tutorial.png");
                        Begin(Phase.SelectCameraSlot); break;
                    case Phase.SelectCameraSlot:
                        CheckPausedNativeMenu();
                        if (!Settled(5) || terminal.View.SelectedSlotIndex != 1) return;
                        report.nativeCameraSlot = true; Begin(Phase.SelectLens); break;
                    case Phase.SelectLens:
                        CheckPausedNativeMenu();
                        if (!Settled(5) || terminal.View.SelectedCamera != lesson.TargetCamera) return;
                        Require(!terminal.View.IsSelectedModuleInstalled && bootstrap.photoMode.CurrentCamera == damagedCamera,
                            "Selecting an available lens must not install it.");
                        report.nativeLensSelection = true;
                        Record("Native RB changes to CAMERA and the horizontal D-pad selects the actual target camera definition, independently of card order.", "refit-lens-selection.png");
                        Begin(Phase.EquipLens); break;
                    case Phase.EquipLens:
                        CheckPausedNativeMenu();
                        if (bootstrap.photoMode.CurrentCamera != lesson.TargetCamera) return;
                        Require(report.equipRequests == 1 && terminal.CameraEquipment.IsInstalled(lesson.TargetCamera),
                            "The real native A installation must apply the target camera exactly once.");
                        report.installedWithNativeA = true; Begin(Phase.HoldAfterEquipment); break;
                    case Phase.HoldAfterEquipment:
                        CheckPausedNativeMenu();
                        if (!Settled(8)) return;
                        Require(lesson.Progress.Step == RefitTutorialStep.EquipLens && report.equipRequests == 1,
                            "Continuing to hold the installation A cannot continue the successful tutorial card or equip twice.");
                        report.heldEquipDidNotContinue = true; Begin(Phase.NeutralAfterEquipment); break;
                    case Phase.NeutralAfterEquipment:
                        CheckPausedNativeMenu();
                        if (!Settled(8)) return;
                        Require(lesson.Progress.Step == RefitTutorialStep.EquipLens && !lesson.Progress.CanContinue,
                            "The native installed lens needs no tutorial success confirmation while the table remains open.");
                        Record("A installs Zoom Lens 300 directly. Native menu operation continues without any tutorial A interception.", "refit-lens-installed.png");
                        Begin(Phase.CloseWorkbench); break;
                    case Phase.CloseWorkbench:
                        if (terminal.IsOpen || Time.timeScale <= 0f || !bootstrap.robot.isActiveAndEnabled || !Settled(8)) return;
                        Require(machine.IsInside && bootstrap.photoMode.CurrentCamera == lesson.TargetCamera,
                            "Native B closing must retain interior state and the installed camera.");
                        Require(lesson.Progress.Step == RefitTutorialStep.NewLensControls,
                            "Closing the interface must restore the new-lens operation card.");
                        report.closedWithNativeB = report.retainedInterior = report.retainedCamera = true;
                        Begin(Phase.ReadNewControls); break;
                    case Phase.ReadNewControls:
                        if (!lesson.Panel.CanContinue || !Settled(20)) return;
                        Require(lesson.Progress.Step == RefitTutorialStep.NewLensControls && lesson.Panel.IsVisible,
                            "The new focus/release/shutter instructions must stay available until A.");
                        Require(lesson.Panel.Root.Q<Label>("TutorialMessage").text.Contains("release", StringComparison.OrdinalIgnoreCase),
                            "The new lens must explain releasing focus before another shutter press.");
                        Record("B returns to the same interior. The new camera stays installed and its focus/release/shutter explanation waits for A.", "refit-new-controls.png");
                        Begin(Phase.ConfirmControls); break;
                    case Phase.ConfirmControls:
                        if (lesson.Progress.Step != RefitTutorialStep.Completed) return;
                        Require(lesson.IsComplete && !lesson.Panel.CanContinue && !lesson.Panel.IsVisible, "The last controls confirmation completes the chapter without a final card.");
                        Begin(Phase.ConfirmCompletion); break;
                    case Phase.ConfirmCompletion:
                        if (!lesson.IsComplete) return;
                        Require(!lesson.Panel.CanContinue, "A fully confirmed chapter must release gameplay A.");
                        report.completedWithA = true;
                        Record("A confirms the new-lens explanation and directly releases the finished tutorial.", "refit-completed.png");
                        Begin(Phase.FinalRelease); break;
                    case Phase.FinalRelease:
                        if (!Settled(12)) return;
                        Require(!terminal.IsOpen && lesson.IsComplete && !lesson.Panel.CanContinue,
                            "The final confirmation A and its release cannot reopen the nearby terminal.");
                        report.finalADidNotReopen = true; Begin(Phase.ReopenNormally); break;
                    case Phase.FinalHide:
                        if (!Settled(6)) return;
                        Require(lesson.IsComplete && lesson.Panel.IsCollapsed && !terminal.IsOpen,
                            "The confirmed completion card must still hide with a real left D-pad tap.");
                        report.finalPanelHidden = true; Begin(Phase.FinalShow); break;
                    case Phase.FinalShow:
                        if (!Settled(6)) return;
                        Require(lesson.IsComplete && !lesson.Panel.IsCollapsed && !lesson.Panel.CanContinue && !terminal.IsOpen,
                            "The confirmed completion card must reopen with a real right D-pad tap without reclaiming A.");
                        report.finalPanelShown = true;
                        Record("After chapter confirmation, real left/right D-pad taps still hide and reopen the completion card without reclaiming ordinary A.");
                        Begin(Phase.ReopenNormally); break;
                    case Phase.ReopenNormally:
                        if (!terminal.IsOpen || !Settled(6)) return;
                        Require(lesson.Guide == null || !lesson.Guide.IsVisible,
                            "A normal terminal open after completion cannot restore the tutorial guide.");
                        Require(lesson.IsComplete && bootstrap.photoMode.CurrentCamera == lesson.TargetCamera,
                            "Ordinary reopening must preserve the installed lens and confirmed chapter.");
                        report.normalReopen = true; Begin(Phase.SelectOldNormally); break;
                    case Phase.SelectOldNormally:
                        if (!Settled(5) || terminal.View.SelectedCamera != damagedCamera) return;
                        Require(!terminal.View.IsSelectedModuleInstalled, "The old lens must remain a selectable, available module.");
                        Begin(Phase.EquipOldNormally); break;
                    case Phase.EquipOldNormally:
                        if (bootstrap.photoMode.CurrentCamera != damagedCamera || !Settled(5)) return;
                        Require(lesson.IsComplete, "Changing equipment normally must not restart the completed tutorial.");
                        Begin(Phase.SelectTargetNormally); break;
                    case Phase.SelectTargetNormally:
                        if (!Settled(5) || terminal.View.SelectedCamera != lesson.TargetCamera) return;
                        Begin(Phase.EquipTargetNormally); break;
                    case Phase.EquipTargetNormally:
                        if (bootstrap.photoMode.CurrentCamera != lesson.TargetCamera || !Settled(5)) return;
                        report.ordinaryEquipmentRestored = true;
                        Record("After final A release, a new native A reopens normal maintenance; ordinary A can equip the old and new lens again with no tutorial interception.");
                        Begin(Phase.CloseNormally); break;
                    case Phase.CloseNormally:
                        if (terminal.IsOpen || Time.timeScale <= 0f || !Settled(8)) return;
                        lesson.enabled = false; Begin(Phase.Cleanup); break;
                    case Phase.Cleanup:
                        if (!Settled(6)) return;
                        Require(bootstrap.robot.isActiveAndEnabled && Time.timeScale == 1f && machine.IsInside
                            && bootstrap.photoMode.CurrentCamera == lesson.TargetCamera && !terminal.IsOpen,
                            "Disabling the tutorial must retain normal gameplay, the interior and the selected lens.");
                        report.cleanupReleasedTutorial = true;
                        Record("Releasing the tutorial leaves the real robot, nested station, portal and camera equipment intact.");
                        Finish(null); break;
                }
            }
            catch (Exception error) { Finish(error); }
        }

        private void PrepareFixture()
        {
            foreach (var candidate in Object.FindObjectsByType<WasteRobotBase>(FindObjectsSortMode.None))
            {
                if (candidate.gameObject.scene != bootstrap.gameObject.scene || candidate.InteriorRoot == null) continue;
                var nested = candidate.InteriorRoot.GetComponentInChildren<CheckpointRefitTerminal>(true);
                if (nested == null) continue;
                Require(machine == null, "The tutorial fixture requires one unambiguous authored machine and station.");
                machine = candidate; terminal = nested;
            }
            Require(machine != null && terminal != null, "The saved machine must contain its own assembly station.");
            machinePosition = machine.transform.position; machineRotation = machine.transform.rotation; machineScale = machine.transform.localScale;
            terminalParent = terminal.transform.parent; terminalPosition = terminal.transform.localPosition;
            terminalRotation = terminal.transform.localRotation; terminalScale = terminal.transform.localScale;
            machine.SetInside(false);
            Vector2 outside = machine.PortalToWorld(new Vector2(0f, 1.7f));
            Require(bootstrap.map.TrySampleWorldPosition(outside, out _, out _), "The saved entrance must lie within the generated map.");
            bootstrap.robot.transform.position = new Vector3(outside.x, outside.y, bootstrap.robot.transform.position.z);
            Vector2 inward = -machine.transform.up;
            bootstrap.robot.transform.rotation = Quaternion.Euler(0f, 0f, Vector2.SignedAngle(Vector2.up, inward));
            bootstrap.cameraFollow?.SnapToTarget();
            damagedCamera = AssetDatabase.LoadAssetAtPath<PhotoCameraDefinition>("Assets/Data/AnimalPhoto/Rendering/HalflyBrokenPrimeCamera50.asset");
            Require(damagedCamera != null && bootstrap.photoMode.CurrentCamera == damagedCamera,
                "The tutorial's existing damaged prime camera must remain equipped before assembly.");
            fixturePrepared = true;
        }

        private void CheckAuthoredObjects()
        {
            Require(machine != null && terminal != null && machine.transform.position == machinePosition
                && machine.transform.rotation == machineRotation && machine.transform.localScale == machineScale
                && terminal.transform.parent == terminalParent && terminal.transform.localPosition == terminalPosition
                && terminal.transform.localRotation == terminalRotation && terminal.transform.localScale == terminalScale,
                "The refit tutorial must retain the authored machine, station hierarchy and every local placement.");
        }
        private void CheckPausedNativeMenu()
        {
            Require(terminal.IsOpen && terminal.View != null && terminal.View.IsVisible && machine.IsInside,
                "The real nested maintenance UI must remain open in the interior.");
            Require(Time.timeScale == 0f && !bootstrap.robot.enabled && !lesson.Panel.IsVisible,
                "The native refit UI owns its world pause and hides the original right task panel.");
            Require(lesson.Guide == null && terminal.View.Document.rootVisualElement.Q("TutorialRefitGuide") == null,
                "The paused native assembly interface has no tutorial component or overlay.");
            Require(terminal.TutorialSubmitHandler == null,
                "The native equipment A is not intercepted by the tutorial.");
            report.noMenuGuide = report.noMenuSubmitInterception = true;
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
            var pad = InputSystem.GetDeviceById(gamepadId) as Gamepad;
            if (pad == null) return;
            var state = new GamepadState();
            if (phase == Phase.ReachEntrance || phase == Phase.EnterMachine || phase == Phase.ReachWorkbench)
                DriveToWaypoint(ref state);
            if (phase == Phase.ConfirmEntrance || phase == Phase.OpenWorkbench
                || phase == Phase.ConfirmControls
                || phase == Phase.ReopenNormally || phase == Phase.EquipOldNormally || phase == Phase.EquipTargetNormally)
                Tap(ref state, GamepadButton.South);
            if (phase == Phase.EquipLens || phase == Phase.HoldAfterEquipment) state = state.WithButton(GamepadButton.South);
            if (phase == Phase.SelectCameraSlot) Tap(ref state, GamepadButton.RightShoulder);
            if (phase == Phase.SelectLens || phase == Phase.SelectOldNormally || phase == Phase.SelectTargetNormally)
            {
                PhotoCameraDefinition cameraToSelect = phase == Phase.SelectOldNormally ? damagedCamera : lesson.TargetCamera;
                int target = -1;
                for (int i = 0; i < terminal.View.ModuleCount; i++)
                    if (terminal.View.GetCameraModule(i) == cameraToSelect) { target = i; break; }
                if (target >= 0 && target != terminal.View.SelectedModuleIndex)
                    Tap(ref state, target > terminal.View.SelectedModuleIndex ? GamepadButton.DpadRight : GamepadButton.DpadLeft);
            }
            if (phase == Phase.CloseWorkbench || phase == Phase.CloseNormally) Tap(ref state, GamepadButton.East);
            if (phase == Phase.FinalHide) Tap(ref state, GamepadButton.DpadLeft);
            if (phase == Phase.FinalShow) Tap(ref state, GamepadButton.DpadRight);
            report.queuedLeftStick = state.leftStick; report.queuedRightTrigger = state.rightTrigger;
            pad.MakeCurrent(); InputSystem.QueueStateEvent(pad, state);
        }
        private void Tap(ref GamepadState state, GamepadButton button)
        {
            if (tapPhase != (int)phase) { tapPhase = (int)phase; tapFrame = Time.frameCount; }
            if (Time.frameCount - tapFrame < 3) state = state.WithButton(button);
        }
        private void DriveToWaypoint(ref GamepadState state)
        {
            if (lesson?.Waypoint != null && lesson.Waypoint.HasTarget)
            { driveTarget = lesson.Waypoint.TargetMapMeters; targetRecorded = true; }
            if (!targetRecorded || bootstrap?.robot == null || Time.deltaTime <= 0f) return;
            if (!bootstrap.map.TrySampleWorldPosition(bootstrap.robot.transform.position, out Vector2 current, out _)) return;
            Vector2 targetWorld = bootstrap.map.MapPositionToWorld(driveTarget);
            Vector2 delta = targetWorld - (Vector2)bootstrap.robot.transform.position;
            float distance = Vector2.Distance(current, driveTarget);
            report.targetDistanceMeters = distance;
            if (phase == Phase.ReachEntrance && distance <= lesson.ArrivalRadiusMeters * .8f) return;
            if (phase == Phase.ReachWorkbench && terminal != null && terminal.IsNearby) return;
            if (distance <= .22f || (phase == Phase.ReachWorkbench && lesson.Progress.Step == RefitTutorialStep.OpenWorkbench)) return;
            float angle = Vector2.SignedAngle(bootstrap.robot.Forward, delta.normalized);
            report.targetAngle = angle;
            state.leftStick = Vector2.right * Mathf.Clamp(-angle / 35f, -.5f, .5f);
            if (Mathf.Abs(angle) < 36f) state.rightTrigger = distance < 1.2f ? .2f : .38f;
        }

        private static void CompleteCounter(object controller, object progress, object step)
        { SetStep(progress, step); controller.GetType().GetField("chapterConfirmed", Flags).SetValue(controller, true); }
        private static void SetStep(object progress, object step) => progress.GetType().GetProperty("Step", Flags).SetValue(progress, step);
        private bool Settled(int frames) => Time.frameCount - phaseFrame >= frames;
        private void Begin(Phase next)
        { phase = next; phaseFrame = Time.frameCount; phaseStartedAt = EditorApplication.timeSinceStartup; targetRecorded = false; Save(); }
        private void Record(string text, string screenshot = null)
        {
            report.checks.Add(text);
            if (!string.IsNullOrEmpty(screenshot))
            { string path = Path.Combine(output, screenshot); ScreenCapture.CaptureScreenshot(path); report.screenshotRequests.Add(path); }
            Save();
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private void Save()
        {
            if (report == null || string.IsNullOrEmpty(output)) return;
            report.phase = phase.ToString(); report.timeScale = Time.timeScale;
            report.connectedGamepads = Gamepad.all.Count;
            report.nativeTriggerThrottle = AdaptiveLegacyGamepadInput.ReadTriggerThrottle();
            report.firstGamepadName = Gamepad.all.Count > 0 ? Gamepad.all[0].name : string.Empty;
            report.firstGamepadRightTrigger = Gamepad.all.Count > 0 ? Gamepad.all[0].rightTrigger.ReadValue() : 0f;
            var virtualGamepad = InputSystem.GetDeviceById(gamepadId) as Gamepad;
            report.virtualGamepadRightTrigger = virtualGamepad != null ? virtualGamepad.rightTrigger.ReadValue() : 0f;
            if (lesson?.Progress != null) report.step = lesson.Progress.Step.ToString();
            if (lesson?.TargetCamera != null) report.targetCamera = lesson.TargetCamera.CameraId;
            if (bootstrap?.photoMode?.CurrentCamera != null) report.actualCamera = bootstrap.photoMode.CurrentCamera.CameraId;
            if (bootstrap?.robot != null)
            {
                var robot = bootstrap.robot; report.speed = robot.CurrentSpeed; report.throttle = robot.CurrentThrottleIntent;
                report.turnSpeed = robot.CurrentTurnSpeed; report.movementMode = robot.MovementMode.ToString();
                bootstrap.map.TrySampleWorldPosition(robot.transform.position, out report.playerMapPosition, out _);
                if (machine != null) report.playerPortalPosition = machine.WorldToPortal(robot.transform.position);
                var surface = bootstrap.traversalEvaluator?.EvaluateLocalSurface(robot.transform.position);
                if (surface.HasValue) report.traversalSlope = surface.Value.MaximumSurfaceSlopeAngle;
            }
            if (machine != null) { report.machinePosition = machine.transform.position; report.nativeTransitions = machine.TransitionCount; }
            if (terminal != null)
            {
                report.terminalLocalPosition = terminal.transform.localPosition; report.interactionRadius = terminal.InteractionRadiusMeters;
                report.terminalIsNearby = terminal.IsNearby; report.terminalIsOpen = terminal.IsOpen;
                report.terminalInputArmed = (bool)typeof(CheckpointRefitTerminal).GetField("inputArmed", Flags).GetValue(terminal);
                report.terminalReleasing = (bool)typeof(CheckpointRefitTerminal).GetField("releasing", Flags).GetValue(terminal);
                report.terminalHasOpenBlockHandler = terminal.TutorialOpenBlocked != null;
                report.tutorialPromptReady = lesson?.Panel != null && lesson.Panel.CanContinue;
                // Read the latch directly: invoking the open-block callback during diagnostics would
                // itself observe a neutral release and could conceal an input-release regression.
                report.heldContinueBlock = (bool)typeof(TutorialContinueInput).GetField("blockUntilRelease", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                report.worldSouthHeld = AdaptiveLegacyGamepadInput.IsSouthFaceButtonHeld();
                report.canAcquireMaintenance = RefitGameplayLease.CanAcquire(bootstrap.robot, out string reason);
                report.failureReason = reason;
            }
            if (lesson?.Waypoint != null) report.waypointMapPosition = lesson.Waypoint.TargetMapMeters;
            File.WriteAllText(Path.Combine(output, "refit-play-smoke.json"), JsonUtility.ToJson(report, true));
        }
        private void OnLog(string text, string stack, LogType type)
        {
            if (!Current || report == null) return;
            string normalized = text.Replace("\r\n", "\n").Trim();
            if (type == LogType.Error && (phase == Phase.Starting || phase == Phase.Finishing)
                && (normalized == KnownSceneHeader || normalized == KnownMissingStaticAnimal))
            {
                if (normalized == KnownMissingStaticAnimal) report.knownMissingPrefabMessages++; else report.knownSceneHeaders++;
                if (!report.knownSceneDiagnostics.Contains(normalized)) report.knownSceneDiagnostics.Add(normalized);
                Save(); return;
            }
            if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && !report.errors.Contains(text))
            { report.errors.Add(text); Save(); }
        }
        private void Finish(Exception error)
        {
            if (phase == Phase.Finishing) return;
            if (error != null) report.errors.Add(error.ToString());
            InputSystem.onBeforeUpdate -= Drive;
            if (terminal?.View != null) terminal.View.EquipRequested -= OnEquipRequested;
            if (terminal != null && terminal.IsOpen) terminal.Close();
            if (cameraFollowSuspended && bootstrap?.cameraFollow != null) bootstrap.cameraFollow.enabled = true;
            var pad = InputSystem.GetDeviceById(gamepadId); if (pad != null) InputSystem.RemoveDevice(pad);
            if (originalInput != null) InputSystem.settings = originalInput;
            if (testInput != null) DestroyImmediate(testInput);
            Application.runInBackground = originalBackground;
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
