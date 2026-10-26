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
    /// <summary>Initial imbalance is seeded once per attempt; all rollover, support, effort and landing use production systems.</summary>
    public sealed class TutorialTipOverPlaySmoke : ScriptableObject
    {
        private const string SessionKey = "AnimalGame.TutorialTipOverPlaySmoke.CurrentRun";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string KnownSceneHeader = "Problem detected while opening the Scene file: 'Assets/Scenes/GameMaps/TutorialTemporaryMap.unity'.\nCheck the following logs for more details.";
        private const string KnownMissingStaticAnimal = "Prefab instance problem: 'Static_Animal (Missing Prefab with guid: 2645e53e76c8ec846b09ee78b13275be)'. 2 instances are missing the same Prefab Asset with guid 2645e53e76c8ec846b09ee78b13275be.";
        private enum Phase
        {
            Starting, Terrain, HoldBeforeFall, Rolling, HoldExplanation, CaptureExplanation, InventoryPaused, InventoryResumed, ReleaseExplanation,
            ConfirmExplanation, Deploy, WaitDeploymentA, ConfirmDeployment, Brace, Effort,
            LoseSupport, WaitFailure, Retry, HoldInertiaA, CaptureInertia, InertiaInputBlocked,
            ReleaseInertiaA, ConfirmInertia,
            StabilizeAfterInertia, WaitSuccessA, CaptureSuccess, ConfirmReturn, ReleaseReturn,
            Restored, Retract, SecondFall, SecondRecovery, CleanupFall, CleanupRecovery, DisablePausedRecovery, Finishing
        }
        [Serializable] private sealed class Report
        {
            public bool passed, realTumbleStarted, originalProgressRetained, originalTargetRetained;
            public bool targetHiddenDuringInterruption, originalCountersFrozen, oldAHeldDidNotContinue;
            public bool explanationWaitedForA, realL3Deployment, deploymentWaitedForA, actualSupport;
            public bool actualEffort, actualSupportFailure, failureRetainedRecoveryCard;
            public bool actualSelfRighting, stabilityWaitedForRealLanding, successWaitedForA;
            public bool returnAConsumedOnce, originalCollapseRestored, originalTargetRestored;
            public bool modalHidesRecovery, modalPreservesProgress;
            public bool secondTumbleReminderOnly, secondRecoveryAutomatic, physicsSettingsPreserved;
            public bool inertiaSlowMotionOnRealLanding, inertiaCardVisible, inertiaStateAdvancesSlowly, inertiaOldAHeldIgnored;
            public bool inertiaSeparateAResumed, inertiaReadOnce, disableReleasesInertiaSlowMotion;
            public bool inertiaInventoryBlocked, inertiaArmPhotoInputBlocked;
            public bool continuousEffortGuide, mainBodyCadenceVisible, rotatedGuideVerified, guidesClearedForInertia;
            public string project, phase, recoveryStep, tumbleState, selfRightingState, mainlineStep;
            public float phaseSeconds, pullProgress, effortFrequency, balanceMagnitude, deployment;
            public float timeScale, pausedInertiaMagnitude, pausedInertiaElapsed, observedSlowMotionRate;
            public Vector2 playerMapPosition, queuedLeftStick, queuedRightStick, braceDirection, effortDirection;
            public int tumbleEvents, acceptedPulses, supportFailures, landings, knownSceneHeaders, knownMissingPrefabMessages;
            public List<string> checks = new(), errors = new(), knownSceneDiagnostics = new(), screenshots = new();
        }
        [SerializeField] private string runId, output;
        [SerializeField] private bool running, originalBackground, cameraFollowSuspended;
        [SerializeField] private int gamepadId, phaseFrame, lastFrame = -1;
        [SerializeField] private double deadline, phaseStarted;
        [SerializeField] private float latestLandingTime = float.NegativeInfinity;
        [SerializeField] private Vector2 landingBrace;
        [SerializeField] private Phase phase;
        [SerializeField] private Report report;
        [SerializeField] private InputSettings originalInput, testInput;
        [SerializeField] private HeightMapPlayerSceneBootstrap bootstrap;
        [SerializeField] private TutorialDrivingLesson driving;
        [SerializeField] private TutorialTerrainLesson terrain;
        [SerializeField] private TutorialTipOverLesson recovery;
        [SerializeField] private Vector2 originalTarget;
        [SerializeField] private TerrainTutorialStep originalStep;
        [SerializeField] private bool originalCollapsed;
        [SerializeField] private float interruptedDistance, interruptedReadingSeconds;
        [SerializeField] private int interruptedApproachIndex;
        [SerializeField] private bool pauseSnapshotCaptured;
        [SerializeField] private bool pausedArmsActive, originalBalanceEnabled, originalCameraEnabled;
        [SerializeField] private Vector2 pausedPosition, pausedBalance;
        [SerializeField] private float pausedGameTime, pausedUnscaledTime, pausedInertiaMagnitude, pausedInertiaElapsed;
        private TerrainTutorialProgress originalProgress;
        private string nativeSettings;
        private RobotTumbleController Tumble => bootstrap?.tumble;
        private RobotSelfRightingController Self => bootstrap?.selfRightingController;
        private RobotArmController Arms => bootstrap?.armController;
        private bool Current => !string.IsNullOrEmpty(runId) && SessionState.GetString(SessionKey, "") == runId;

        public static void RunBatch()
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            if (!Application.isBatchMode || !project.Contains("/output/validation/"))
                throw new InvalidOperationException("Run tip-over Play Mode checks only in a disposable output/validation project.");
            SessionState.EraseString(SessionKey);
            foreach (var old in Resources.FindObjectsOfTypeAll<TutorialTipOverPlaySmoke>())
            { old.running = false; old.Unsubscribe(); DestroyImmediate(old); }
            var test = CreateInstance<TutorialTipOverPlaySmoke>(); test.hideFlags = HideFlags.HideAndDontSave;
            test.runId = Guid.NewGuid().ToString("N"); SessionState.SetString(SessionKey, test.runId);
            string configured = Environment.GetEnvironmentVariable("TUTORIAL_TIPOVER_VALIDATION_OUTPUT");
            test.output = string.IsNullOrWhiteSpace(configured) ? Path.Combine(project, "output/validation/tutorial-tipover") : Path.GetFullPath(configured);
            Directory.CreateDirectory(test.output); test.report = new Report { project = project };
            typeof(TutorialPhotographyPlaySmoke).GetMethod("ApplyGameViewSize", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
            test.Subscribe(); EditorSceneManager.OpenScene(TutorialDrivingLesson.ScenePath, OpenSceneMode.Single);
            test.deadline = EditorApplication.timeSinceStartup + 210;
            test.running = true; test.Begin(Phase.Starting); EditorApplication.isPlaying = true;
        }

        private void OnEnable() { if (running && Current) Subscribe(); }
        private void OnDisable() => Unsubscribe();
        private void Subscribe()
        { if (!Current) return; Unsubscribe(); EditorApplication.update += Tick; InputSystem.onBeforeUpdate += Drive; Application.logMessageReceived += OnLog; }
        private void Unsubscribe()
        {
            EditorApplication.update -= Tick; InputSystem.onBeforeUpdate -= Drive; Application.logMessageReceived -= OnLog;
            if (Tumble != null) Tumble.Started -= OnStarted;
            if (Self != null) { Self.ForcePulse -= OnPulse; Self.SupportFailed -= OnFailure; Self.RightingLanded -= OnLanding; }
        }
        private void OnStarted(RobotTumbleStartedInfo info) { report.tumbleEvents++; report.realTumbleStarted = true; }
        private void OnPulse(RobotSelfRightingForcePulseInfo info) { if (info.Accepted) report.acceptedPulses++; }
        private void OnFailure(RobotSelfRightingFailureInfo info) { report.supportFailures++; report.actualSupportFailure = true; }
        private void OnLanding(RobotSelfRightingLandedInfo info)
        { report.landings++; report.actualSelfRighting = true; latestLandingTime = Time.time; landingBrace = recovery.CurrentBraceDirection; }

        private void Tick()
        {
            if (!running || !Current) return;
            try
            {
                if (phase == Phase.Finishing) { if (!EditorApplication.isPlayingOrWillChangePlaymode) Exit(); return; }
                Require(EditorApplication.timeSinceStartup < deadline, "Tip-over validation timed out in " + phase);
                Require(phase == Phase.Starting || Seconds < 35, "Tip-over phase stalled: " + phase + "; " + report.tumbleState + "; " + report.selfRightingState);
                EditorApplication.QueuePlayerLoopUpdate();
                if (!EditorApplication.isPlaying || Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount;
                if (Time.frameCount % 20 == 0) Save();
                if (originalProgress != null && recovery != null && recovery.IsInterrupting) CheckOriginal();
                switch (phase)
                {
                    case Phase.Starting:
                        bootstrap = Object.FindFirstObjectByType<HeightMapPlayerSceneBootstrap>();
                        driving = Object.FindFirstObjectByType<TutorialDrivingLesson>();
                        terrain = Object.FindFirstObjectByType<TutorialTerrainLesson>();
                        recovery = Object.FindFirstObjectByType<TutorialTipOverLesson>();
                        if (bootstrap?.map == null || !bootstrap.map.HasGeneratedMap || bootstrap.robot == null
                            || bootstrap.balance == null || Tumble == null || Self == null || Arms == null
                            || driving?.Progress == null || terrain == null || recovery == null || driving.Panel?.Root?.panel == null) return;
                        PrepareFixture(); Begin(Phase.Terrain); break;
                    case Phase.Terrain:
                        if (!terrain.HasStarted || terrain.Progress == null || terrain.Waypoint == null || !terrain.Waypoint.HasTarget) return;
                        originalProgress = terrain.Progress; originalStep = originalProgress.Step;
                        originalTarget = terrain.Waypoint.TargetMapMeters;
                        originalCollapsed = true; driving.Panel.SetCollapsed(originalCollapsed);
                        Record("The fixture skips driving counters only, then preserves the actual terrain chapter's model, current step and authored destination. The robot is initialized once on the known dry terrain endpoint.");
                        Begin(Phase.HoldBeforeFall); break;
                    case Phase.HoldBeforeFall:
                        if (!Settled(12)) return;
                        SeedImbalance(); Begin(Phase.Rolling); break;
                    case Phase.Rolling:
                        if (Tumble.State != RobotTumbleState.Fallen || recovery.Progress == null) return;
                        Require(report.tumbleEvents == 1 && recovery.IsInterrupting && TutorialTipOverLesson.BlocksMainline(terrain.gameObject),
                            "The production Started event must suspend the mainline before the real fallen state.");
                        Require(terrain.Waypoint.CurrentMode == TutorialWaypointIndicator.WaypointMode.Hidden,
                            "Interruption hides the original destination without clearing it.");
                        report.targetHiddenDuringInterruption = true;
                        Begin(Phase.HoldExplanation); break;
                    case Phase.HoldExplanation:
                        if (!Settled(12)) return;
                        Require(recovery.Progress.Step == TipOverTutorialStep.Explanation && !recovery.HasCompleted,
                            "A held before the fall cannot dismiss the first explanation.");
                        report.oldAHeldDidNotContinue = report.explanationWaitedForA = true;
                        Record("Real native rollover temporarily expands the same right panel. A held before entry cannot confirm the recovery introduction.", "tipover-explanation.png");
                        Begin(Phase.CaptureExplanation); break;
                    case Phase.CaptureExplanation:
                        if (!Settled(4)) return;
                        bootstrap.mainUiObject.GetComponent<AnimalGame.MainUI.MainUI>().OpenInventory();
                        Begin(Phase.InventoryPaused); break;
                    case Phase.InventoryPaused:
                        if (!Settled(12) || Time.timeScale > 0f) return;
                        Require(AnimalGame.MainUI.MainUI.BlocksGameplay && !recovery.Panel.IsVisible
                            && recovery.Progress.Step == TipOverTutorialStep.Explanation && Self.PullProgress01 == 0f,
                            "The existing inventory pause must hide recovery and prevent both tutorial and self-righting input.");
                        report.modalHidesRecovery = report.modalPreservesProgress = true;
                        bootstrap.mainUiObject.GetComponent<AnimalGame.MainUI.MainUI>().CloseInventory();
                        Begin(Phase.InventoryResumed); break;
                    case Phase.InventoryResumed:
                        if (!Settled(12) || Time.timeScale <= 0f || !recovery.Panel.IsVisible) return;
                        Require(recovery.Progress.Step == TipOverTutorialStep.Explanation && Tumble.State == RobotTumbleState.Fallen,
                            "Closing inventory must restore the pending recovery card without clearing the original fall.");
                        Record("Opening the real inventory hides the recovery card and freezes its input; closing it restores the same fallen attempt and explanation.");
                        Begin(Phase.ReleaseExplanation); break;
                    case Phase.ReleaseExplanation:
                        if (!Settled(8)) return; Begin(Phase.ConfirmExplanation); break;
                    case Phase.ConfirmExplanation:
                        if (recovery.Progress.Step != TipOverTutorialStep.DeployArms) return;
                        Begin(Phase.Deploy); break;
                    case Phase.Deploy:
                        if (!Arms.IsArmModeActive || Arms.VisibleDeployment01 < .999f) return;
                        report.realL3Deployment = true; Begin(Phase.WaitDeploymentA); break;
                    case Phase.WaitDeploymentA:
                        if (!Settled(10)) return;
                        Require(recovery.Progress.Step == TipOverTutorialStep.DeployArms, "Physical L3 deployment must still wait for its separate A confirmation.");
                        report.deploymentWaitedForA = true; Begin(Phase.ConfirmDeployment); break;
                    case Phase.ConfirmDeployment:
                        if (recovery.Progress.Step != TipOverTutorialStep.Recover) return;
                        Begin(Phase.Brace); break;
                    case Phase.Brace:
                        if (!Self.IsArmSupportCorrect || !Settled(8)) return;
                        report.actualSupport = true; Begin(Phase.Effort); break;
                    case Phase.Effort:
                        if (Self.PullProgress01 <= .1f || report.acceptedPulses < 3) return;
                        CheckContinuousGuide(); report.continuousEffortGuide = true;
                        Record("A non-cardinal continuous stick target matches native recovery input. The main body starts with approximately three pushes per second, and the inward balance-point arrow is visible.", "tipover-continuous-effort.png");
                        report.actualEffort = true; Begin(Phase.LoseSupport); break;
                    case Phase.LoseSupport:
                        if (!report.actualSupportFailure) return;
                        Begin(Phase.WaitFailure); break;
                    case Phase.WaitFailure:
                        if (Self.State == RobotSelfRightingState.ReturningAfterFailure || Seconds < .9f) return;
                        Require(Tumble.State == RobotTumbleState.Fallen && recovery.Progress.Step == TipOverTutorialStep.Recover,
                            "A real support failure must remain on the continuous recovery card without restarting the explanations.");
                        report.failureRetainedRecoveryCard = true;
                        Record("A real left-stick support release causes the native SupportFailed event. The same recovery card and original mainline progress survive the failed attempt.");
                        Begin(Phase.Retry); break;
                    case Phase.Retry:
                        if (recovery.OwnsInertiaPause)
                        {
                            Require(report.landings > 0 && Tumble.State == RobotTumbleState.Upright && Mathf.Approximately(Time.timeScale, .05f),
                                "The inertia reading slowdown must start at five percent speed on the genuine native upright landing.");
                            report.inertiaSlowMotionOnRealLanding = true; pauseSnapshotCaptured = false;
                            Begin(Phase.HoldInertiaA); return;
                        }
                        Require(recovery.Progress.Step != TipOverTutorialStep.Completed,
                            "The first genuine landing must show its inertia explanation before tutorial success.");
                        return;
                    case Phase.HoldInertiaA:
                        if (!Settled(2)) return;
                        CheckInertiaPause();
                        Require(!recovery.HasReadInertiaLesson, "A held before landing cannot dismiss the inertia explanation.");
                        if (!Settled(14) || !report.inertiaStateAdvancesSlowly) return;
                        report.inertiaOldAHeldIgnored = true;
                        Record("The actual native landing slows the world to five percent speed. The reading card remains visible while the clock, controllable balance point and native inertia continue advancing at the reduced rate; A held before landing is ignored.", "tipover-inertia-slow-motion.png");
                        Begin(Phase.CaptureInertia); break;
                    case Phase.CaptureInertia:
                        CheckInertiaPause();
                        if (!Settled(4)) return;
                        bootstrap.mainUiObject.GetComponent<AnimalGame.MainUI.MainUI>().OpenInventory();
                        Begin(Phase.InertiaInputBlocked); break;
                    case Phase.InertiaInputBlocked:
                        if (!Settled(12)) return;
                        Require(!AnimalGame.MainUI.MainUI.BlocksGameplay && recovery.OwnsInertiaPause
                            && Mathf.Approximately(Time.timeScale, .05f) && recovery.Panel.Root.resolvedStyle.display == DisplayStyle.Flex
                            && !recovery.HasReadInertiaLesson,
                            "A new inventory cannot steal ownership of the short inertia reading slowdown.");
                        Require(Arms.IsArmModeActive == pausedArmsActive && !bootstrap.photoMode.IsActive,
                            "Actual L3 and photo-mode presses cannot change arm or camera mode during the reading slowdown.");
                        CheckInertiaPause(); report.inertiaInventoryBlocked = report.inertiaArmPhotoInputBlocked = true;
                        Record("Attempts to open inventory, toggle L3 arms or enter photo mode are ignored during the owned reading slowdown; its advancing native force and visible confirmation card remain intact.");
                        Begin(Phase.ReleaseInertiaA); break;
                    case Phase.ReleaseInertiaA:
                        CheckInertiaPause();
                        if (!Settled(8)) return;
                        Begin(Phase.ConfirmInertia); break;
                    case Phase.ConfirmInertia:
                        if (recovery.OwnsInertiaPause || !recovery.HasReadInertiaLesson || !Settled(12)) return;
                        Require(Time.timeScale == 1f && recovery.IsInterrupting && !recovery.HasCompleted,
                            "A separate reading confirmation restores the original world speed without finishing or resuming the interrupted mainline.");
                        Require(!recovery.Panel.ShowDuringTutorialPause,
                            "The private pause visibility exception ends as soon as reading is confirmed.");
                        report.inertiaSeparateAResumed = report.inertiaReadOnce = true;
                        Record("A new A after release resumes the same native landing inertia. The player still has to counter it with the real right stick and stabilize before returning to the original tutorial.");
                        Begin(Phase.StabilizeAfterInertia); break;
                    case Phase.StabilizeAfterInertia:
                        if (report.landings == 0 || recovery.Progress.Step != TipOverTutorialStep.Completed) return;
                        report.stabilityWaitedForRealLanding = true; Begin(Phase.WaitSuccessA); break;
                    case Phase.WaitSuccessA:
                        if (!Settled(12)) return;
                        Require(recovery.HasCompleted && !recovery.IsInterrupting && !recovery.Panel.CanContinue,
                            "Real upright stability resumes the previous lesson without a success card or final A.");
                        report.successWaitedForA = false;
                        CheckOriginal(); report.returnAConsumedOnce = true;
                        Begin(Phase.ReleaseReturn); break;
                    case Phase.CaptureSuccess:
                    case Phase.ConfirmReturn:
                        Begin(Phase.ReleaseReturn); break;
                    case Phase.ReleaseReturn:
                        if (TutorialTipOverLesson.BlocksMainline(terrain.gameObject) || !Settled(10)) return;
                        Begin(Phase.Restored); break;
                    case Phase.Restored:
                        if (!Settled(12)) return;
                        CheckOriginal();
                        Require(driving.Panel.IsCollapsed == originalCollapsed, "Returning restores the original task panel collapse preference.");
                        Require(terrain.Waypoint.HasTarget && terrain.Waypoint.TargetMapMeters == originalTarget,
                            "Returning restores the original terrain destination.");
                        report.originalCollapseRestored = report.originalTargetRestored = true;
                        Record("Stable recovery directly restores the original model, step, destination and collapsed preference without a completion card.");
                        Begin(Phase.Retract); break;
                    case Phase.Retract:
                        if (Arms.IsArmModeActive || Arms.VisibleDeployment01 > .001f || !Settled(10)) return;
                        SeedImbalance(); Begin(Phase.SecondFall); break;
                    case Phase.SecondFall:
                        if (Tumble.State != RobotTumbleState.Fallen || !recovery.IsInterrupting) return;
                        Require(recovery.IsReminder && recovery.Progress.Step != TipOverTutorialStep.Explanation
                            && recovery.Progress.Step != TipOverTutorialStep.DeployArms,
                            "A subsequent fall uses the short reminder, not the first-time explanation/deployment cards.");
                        report.secondTumbleReminderOnly = true; Begin(Phase.SecondRecovery); break;
                    case Phase.SecondRecovery:
                        if (!Arms.IsArmModeActive && Settled(5)) return;
                        if (!report.rotatedGuideVerified && Tumble.State == RobotTumbleState.Fallen && Self.IsArmSupportCorrect
                            && Self.PullProgress01 > .1f)
                        {
                            CheckContinuousGuide(); report.rotatedGuideVerified = true;
                            Record("After rotating the robot and camera, a second non-cardinal fall still displays the exact native stick angle and projects the balance point inward arrow correctly.", "tipover-continuous-rotated.png");
                        }
                        if (recovery.IsInterrupting || TutorialTipOverLesson.BlocksMainline(terrain.gameObject)) return;
                        Require(report.landings >= 2 && Tumble.State == RobotTumbleState.Upright,
                            "A reminder can finish automatically only after the second real upright recovery.");
                        Require(recovery.HasReadInertiaLesson && !recovery.OwnsInertiaPause && Time.timeScale == 1f,
                            "A later reminder does not repeat the already-read landing slowdown.");
                        report.secondRecoveryAutomatic = true;
                        Require(report.continuousEffortGuide && report.mainBodyCadenceVisible && report.rotatedGuideVerified
                            && report.guidesClearedForInertia, "Continuous guide angles, top-body cadence and lifecycle cleanup must all be verified.");
                        report.physicsSettingsPreserved = nativeSettings == NativeSettings();
                        Require(report.physicsSettingsPreserved, "The tutorial and fixture preserve all native tumble/self-righting settings.");
                        Record("The second actual fall presents only an operational reminder and returns automatically after a second native recovery. Native physics settings remain unchanged.");
                        recovery.ResetFirstTipOverTutorial(); SeedImbalance(); Begin(Phase.CleanupFall); break;
                    case Phase.CleanupFall:
                        if (Tumble.State != RobotTumbleState.Fallen || !recovery.IsInterrupting) return;
                        Begin(Phase.CleanupRecovery); break;
                    case Phase.CleanupRecovery:
                        if (!recovery.OwnsInertiaPause) return;
                        Require(report.landings >= 3 && Mathf.Approximately(Time.timeScale, .05f),
                            "Cleanup is checked only after another real native landing owns a new tutorial slowdown.");
                        recovery.enabled = false; Begin(Phase.DisablePausedRecovery); break;
                    case Phase.DisablePausedRecovery:
                        if (!Settled(3)) return;
                        Require(Time.timeScale == 1f && !TutorialTipOverLesson.BlocksMainline(terrain.gameObject)
                            && !driving.Panel.ShowDuringTutorialPause && bootstrap.balance.enabled == originalBalanceEnabled
                            && (bootstrap.cameraFollow == null || bootstrap.cameraFollow.enabled == originalCameraEnabled),
                            "Disabling the scene-owned recovery releases its own world slowdown and task-panel exception.");
                        report.disableReleasesInertiaSlowMotion = true;
                        Record("A third actual self-righting landing is used only to verify cleanup: disabling recovery releases its own slowdown, preserves normal component states, restores task-panel visibility rules and removes the mainline gate.");
                        Finish(null); break;
                }
            }
            catch (Exception error) { Finish(error); }
        }

        private void PrepareFixture()
        {
            originalInput = InputSystem.settings; testInput = Instantiate(originalInput); testInput.hideFlags = HideFlags.HideAndDontSave;
            testInput.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            testInput.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus; InputSystem.settings = testInput;
            originalBackground = Application.runInBackground; Application.runInBackground = true;
            foreach (Gamepad other in new List<Gamepad>(Gamepad.all)) InputSystem.RemoveDevice(other);
            gamepadId = InputSystem.AddDevice<Gamepad>("Tip-over tutorial validation gamepad").deviceId;
            bootstrap.robot.transform.position = bootstrap.map.MapPositionToWorld(new Vector2(146f, 121f));
            bootstrap.robot.transform.rotation = Quaternion.identity; bootstrap.cameraFollow?.SnapToTarget();
            SetStep(driving.Progress, DrivingTutorialStep.Completed); Set(driving, "chapterConfirmed", true);
            Tumble.Started += OnStarted; Self.ForcePulse += OnPulse; Self.SupportFailed += OnFailure; Self.RightingLanded += OnLanding;
            nativeSettings = NativeSettings();
            originalBalanceEnabled = bootstrap.balance.enabled;
            originalCameraEnabled = bootstrap.cameraFollow != null && bootstrap.cameraFollow.enabled;
        }
        private string NativeSettings()
        {
            // Serialize configured values only; runtime fields are intentionally excluded.
            return EditorJsonUtility.ToJson(Tumble) + EditorJsonUtility.ToJson(Self);
        }
        private void SeedImbalance()
        {
            // This is an initial condition only. The next production balance LateUpdate performs
            // TryTipOver and raises its normal event; no tumble/recovery/tutorial state is assigned.
            interruptedDistance = terrain.Progress.DistanceMeters;
            interruptedReadingSeconds = terrain.Progress.ReadingSeconds;
            interruptedApproachIndex = terrain.Progress.ApproachIndex;
            float degrees = report.tumbleEvents == 0 ? 17f : report.tumbleEvents == 1 ? 73f : 37f;
            if (report.tumbleEvents == 1)
            {
                bootstrap.robot.transform.rotation = Quaternion.Euler(0f, 0f, 31f);
                bootstrap.cameraFollow?.SnapToTarget();
            }
            float radians = degrees * Mathf.Deg2Rad;
            Set(bootstrap.balance, "currentBalanceLocal", new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * 1.19f);
            Set(bootstrap.balance, "balanceVelocityLocal", Vector2.zero);
            Record("Fixture seeded one initial balance offset at 1.19 support radii; the production balance solver alone triggers and simulates the resulting fall.");
        }
        private void CheckOriginal()
        {
            Require(ReferenceEquals(terrain.Progress, originalProgress) && terrain.Progress.Step == originalStep,
                "Interruption must preserve the exact existing mainline progress object and its step.");
            Require(terrain.Waypoint != null && terrain.Waypoint.HasTarget && terrain.Waypoint.TargetMapMeters == originalTarget,
                "Interruption may hide but must retain the original waypoint destination.");
            if (recovery.IsInterrupting)
                Require(terrain.Progress.DistanceMeters == interruptedDistance
                    && terrain.Progress.ReadingSeconds == interruptedReadingSeconds && terrain.Progress.ApproachIndex == interruptedApproachIndex,
                    "Original navigation and reading observations must remain frozen while the recovery tutorial owns the panel.");
            report.originalProgressRetained = report.originalCountersFrozen = report.originalTargetRetained = true;
        }
        private void CheckInertiaPause()
        {
            Require(!recovery.Panel.IsRecoveryDirectionGuideVisible,
                "The standing landing-inertia lesson must not retain the fallen recovery direction guide.");
            report.guidesClearedForInertia = true;
            Require(recovery.OwnsInertiaPause && Mathf.Approximately(Time.timeScale, .05f) && recovery.Panel.IsVisible
                && !recovery.Panel.IsCollapsed && recovery.Panel.ShowDuringTutorialPause
                && recovery.Panel.Root.resolvedStyle.display == DisplayStyle.Flex,
                "The tutorial's own reading slowdown keeps the actual expanded card visible.");
            Require(recovery.Panel.Root.Q<Label>("TutorialTitle").text == recovery.TextContent.TipOver.InertiaTitle,
                "The slow-motion card uses the editable landing-inertia title.");
            Require(Tumble.State == RobotTumbleState.Upright && recovery.Progress.Step != TipOverTutorialStep.Completed,
                "Reading alone cannot satisfy the later stability practice.");
            report.inertiaCardVisible = true;
            CheckSlowMotionInertia();
        }

        private void CheckContinuousGuide()
        {
            var panel = recovery.Panel;
            Require(panel.IsRecoveryDirectionGuideVisible && !panel.RecoveryGuideIsBracing,
                "Actual right-stick recovery displays the continuous effort guide.");
            Require(Vector2.Angle(panel.RecoveryStickTargetDirection, Self.RequiredEffortDirectionLocal) < .05f
                && Vector2.Angle(recovery.CurrentEffortDirection, Self.RequiredEffortDirectionLocal) < .05f,
                "The shown angle and tutorial target use exactly the same local direction as native force acceptance.");
            Require(Vector2.Dot(Self.RequiredBraceDirectionLocal, Self.RequiredEffortDirectionLocal) < -.9999f,
                "The arms brace toward the fallen side while recovery effort acts in its opposite direction.");
            float angle = Mathf.Atan2(Self.RequiredEffortDirectionLocal.y, Self.RequiredEffortDirectionLocal.x) * Mathf.Rad2Deg;
            Require(Mathf.Abs(Mathf.DeltaAngle(angle, Mathf.Round(angle / 45f) * 45f)) > 1f,
                "This native fixture actually exercises an angle between the old eight-way directions.");
            string firstLine = panel.Root.Q<Label>("TutorialMessage").text.Split('\n')[0];
            Require(firstLine.Contains(Self.RequiredEffortPushFrequency.ToString("F0")) && firstLine.Contains("second"),
                "The required cadence is in the first line of the main body, not just its detail footer.");
            report.mainBodyCadenceVisible = true;
            foreach (string name in new[] { "TutorialRecoveryStickGuide", "TutorialRecoveryInwardGuide" })
            {
                var element = panel.Root.Q(name);
                Require(element != null && element.worldBound.width > 1f && element.worldBound.height > 1f,
                    name + " has actual runtime geometry.");
                for (var parent = element; parent != null; parent = parent.parent)
                    Require(parent.resolvedStyle.display != DisplayStyle.None && parent.resolvedStyle.visibility == Visibility.Visible,
                        name + " and every ancestor are actually visible.");
            }
        }
        private void CheckSlowMotionInertia()
        {
            float elapsed = NativeFloat(bootstrap.balance, "selfRightingInertiaElapsed");
            float magnitude = NativeFloat(bootstrap.balance, "selfRightingInertiaMagnitude");
            if (!pauseSnapshotCaptured)
            {
                pauseSnapshotCaptured = true; pausedPosition = bootstrap.robot.transform.position;
                pausedBalance = bootstrap.balance.CurrentState.NormalizedLocalOffset;
                pausedArmsActive = Arms.IsArmModeActive;
                pausedGameTime = Time.time; pausedUnscaledTime = Time.unscaledTime;
                pausedInertiaElapsed = elapsed; pausedInertiaMagnitude = magnitude;
                Require(magnitude > 0f, "The reading slowdown begins with real unspent native landing inertia.");
            }
            Require(bootstrap.balance.enabled == originalBalanceEnabled
                && (bootstrap.cameraFollow == null || bootstrap.cameraFollow.enabled == originalCameraEnabled),
                "The slowdown keeps the production balance simulation and camera follow enabled.");
            float unscaledElapsed = Time.unscaledTime - pausedUnscaledTime;
            float scaledElapsed = Time.time - pausedGameTime;
            if (unscaledElapsed >= .1f)
            {
                float observedRate = scaledElapsed / unscaledElapsed;
                Require(scaledElapsed > .001f && Mathf.Abs(observedRate - .05f) < .008f,
                    "The scaled game clock advances at five percent of elapsed real time rather than freezing.");
                Require(elapsed > pausedInertiaElapsed && Mathf.Abs((elapsed - pausedInertiaElapsed) - scaledElapsed) < .006f,
                    "The real native inertia timer continues advancing with the slowed game clock.");
                Require(magnitude > 0f && magnitude <= pausedInertiaMagnitude + .00001f
                    && Vector2.Distance(bootstrap.balance.CurrentState.NormalizedLocalOffset, pausedBalance) > .00001f,
                    "The native recoil clock keeps running through its hold/decay curve, and the actual balance point remains simulated and controllable in slow motion.");
                report.observedSlowMotionRate = observedRate;
                report.inertiaStateAdvancesSlowly = true;
            }
            report.pausedInertiaElapsed = elapsed; report.pausedInertiaMagnitude = magnitude;
        }
        private static float NativeFloat(object target, string name) => (float)target.GetType().GetField(name, Flags).GetValue(target);
        private void Drive()
        {
            if (!running || !Current || phase == Phase.Finishing || InputState.currentUpdateType != InputUpdateType.Dynamic) return;
            if (bootstrap?.cameraFollow != null)
            {
                if (Time.deltaTime <= 0f && bootstrap.cameraFollow.enabled) { bootstrap.cameraFollow.enabled = false; cameraFollowSuspended = true; }
                else if (Time.deltaTime > 0f && cameraFollowSuspended) { bootstrap.cameraFollow.enabled = true; cameraFollowSuspended = false; }
            }
            var pad = InputSystem.GetDeviceById(gamepadId) as Gamepad; if (pad == null) return;
            var state = new GamepadState();
            if (phase == Phase.HoldBeforeFall || phase == Phase.Rolling || phase == Phase.HoldExplanation
                || phase == Phase.CaptureExplanation || phase == Phase.ConfirmReturn || phase == Phase.Retry
                || phase == Phase.HoldInertiaA || phase == Phase.CaptureInertia || phase == Phase.InertiaInputBlocked
                || phase == Phase.ConfirmInertia)
                state = state.WithButton(GamepadButton.South);
            if (phase == Phase.ConfirmExplanation || phase == Phase.ConfirmDeployment) Tap(ref state, GamepadButton.South);
            if (phase == Phase.InventoryPaused)
            { state = state.WithButton(GamepadButton.South); state.leftStick = recovery.CurrentBraceDirection; state.rightStick = recovery.CurrentEffortDirection; }
            if (phase == Phase.Deploy || phase == Phase.Retract) Tap(ref state, GamepadButton.LeftStick);
            if (phase == Phase.InertiaInputBlocked)
            { Tap(ref state, GamepadButton.LeftStick); Tap(ref state, GamepadButton.West); }
            if (phase == Phase.HoldInertiaA || phase == Phase.CaptureInertia || phase == Phase.InertiaInputBlocked
                || phase == Phase.ReleaseInertiaA)
                state.rightStick = recovery.CurrentCounterDirection;
            if (phase == Phase.Brace || phase == Phase.Effort || phase == Phase.Retry || phase == Phase.SecondRecovery
                || phase == Phase.StabilizeAfterInertia || phase == Phase.CleanupRecovery)
            {
                if (phase == Phase.SecondRecovery && Arms != null && !Arms.IsArmModeActive) Tap(ref state, GamepadButton.LeftStick);
                if (Tumble != null && Tumble.State == RobotTumbleState.Fallen && recovery != null)
                {
                    state.leftStick = recovery.CurrentBraceDirection;
                    if (phase != Phase.Brace && Self.State != RobotSelfRightingState.ReturningAfterFailure)
                    {
                        float cycle = Mathf.Repeat(Time.time, .23f);
                        if (cycle < .085f) state.rightStick = recovery.CurrentEffortDirection;
                    }
                }
                else Stabilize(ref state);
            }
            if (phase == Phase.WaitSuccessA || phase == Phase.CaptureSuccess || phase == Phase.ConfirmReturn
                || phase == Phase.ConfirmInertia) Stabilize(ref state);
            report.queuedLeftStick = state.leftStick; report.queuedRightStick = state.rightStick;
            pad.MakeCurrent(); InputSystem.QueueStateEvent(pad, state);
        }
        private void Stabilize(ref GamepadState state)
        {
            if (Tumble == null || Tumble.State != RobotTumbleState.Upright || bootstrap?.balance == null) return;
            float since = Time.time - latestLandingTime;
            // Let the opposite inertia begin moving the real point before countering it. No
            // force, time scale, chassis orientation or balance value is changed during recovery.
            if (since > .03f && since < 1.15f
                && Vector2.Dot(bootstrap.balance.CurrentState.NormalizedLocalOffset, landingBrace) < .2f)
                state.rightStick = landingBrace;
        }
        private void Tap(ref GamepadState state, GamepadButton button)
        { if (Time.frameCount - phaseFrame < 3) state = state.WithButton(button); }
        private bool Settled(int frames) => Time.frameCount - phaseFrame >= frames;
        private double Seconds => EditorApplication.timeSinceStartup - phaseStarted;
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Flags).SetValue(target, value);
        private static void SetStep(object progress, object step) => progress.GetType().GetProperty("Step", Flags).SetValue(progress, step);
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private void Begin(Phase next) { phase = next; phaseFrame = Time.frameCount; phaseStarted = EditorApplication.timeSinceStartup; Save(); }
        private void Record(string message, string filename = null)
        {
            report.checks.Add(message);
            if (filename != null) { string path = Path.Combine(output, filename); ScreenCapture.CaptureScreenshot(path); report.screenshots.Add(path); }
            Save();
        }
        private void OnLog(string message, string stack, LogType type)
        {
            if (!Current || report == null) return;
            string normalized = message.Replace("\r\n", "\n").Trim();
            if (type == LogType.Error && (phase == Phase.Starting || phase == Phase.Finishing)
                && (normalized == KnownSceneHeader || normalized == KnownMissingStaticAnimal))
            {
                if (normalized == KnownSceneHeader) report.knownSceneHeaders++; else report.knownMissingPrefabMessages++;
                if (!report.knownSceneDiagnostics.Contains(normalized)) report.knownSceneDiagnostics.Add(normalized); return;
            }
            if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && !report.errors.Contains(message))
            { report.errors.Add(message); Save(); }
        }
        private void Save()
        {
            if (report == null || string.IsNullOrEmpty(output)) return;
            report.phase = phase.ToString(); report.phaseSeconds = (float)Seconds;
            report.timeScale = Time.timeScale;
            report.recoveryStep = recovery?.Progress?.Step.ToString(); report.mainlineStep = terrain?.Progress?.Step.ToString();
            report.tumbleState = Tumble?.State.ToString(); report.selfRightingState = Self?.State.ToString();
            if (Self != null) { report.pullProgress = Self.PullProgress01; report.effortFrequency = Self.CurrentEffortPushFrequency; }
            if (Arms != null) report.deployment = Arms.VisibleDeployment01;
            if (bootstrap?.balance != null) report.balanceMagnitude = bootstrap.balance.CurrentState.Magnitude;
            if (bootstrap?.map != null && bootstrap.robot != null) bootstrap.map.TrySampleWorldPosition(bootstrap.robot.transform.position, out report.playerMapPosition, out _);
            if (recovery != null) { report.braceDirection = recovery.CurrentBraceDirection; report.effortDirection = recovery.CurrentEffortDirection; }
            File.WriteAllText(Path.Combine(output, "tipover-play-smoke.json"), JsonUtility.ToJson(report, true));
        }
        private void Finish(Exception error)
        {
            if (phase == Phase.Finishing) return;
            if (error != null) report.errors.Add(error.ToString());
            InputSystem.onBeforeUpdate -= Drive;
            if (cameraFollowSuspended && bootstrap?.cameraFollow != null) bootstrap.cameraFollow.enabled = true;
            var pad = InputSystem.GetDeviceById(gamepadId); if (pad != null) InputSystem.RemoveDevice(pad);
            if (originalInput != null) InputSystem.settings = originalInput;
            if (testInput != null) DestroyImmediate(testInput); Application.runInBackground = originalBackground;
            Begin(Phase.Finishing); EditorApplication.isPlaying = false;
        }
        private void Exit()
        {
            if (report.knownSceneHeaders > 0 && report.knownMissingPrefabMessages == 0)
                report.errors.Add("Scene-load error lacked the exact known missing-two-Static_Animal diagnostic.");
            report.passed = report.errors.Count == 0; Save(); running = false; Unsubscribe(); EditorApplication.Exit(report.passed ? 0 : 1);
        }
    }
}
