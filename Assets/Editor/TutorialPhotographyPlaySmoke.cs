using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using AnimalGame.Animals;
using AnimalGame.Discovery;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using AnimalGame.Tutorial;
using AnimalGame.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    /// <summary>Native scan, stationary-woodpecker and actual photo/album smoke in a disposable project.</summary>
    public sealed class TutorialPhotographyPlaySmoke : ScriptableObject
    {
        private const string SessionKey = "AnimalGame.TutorialPhotographyPlaySmoke.CurrentRun";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string KnownMissingStaticAnimal = "Prefab instance problem: 'Static_Animal (Missing Prefab with guid: 2645e53e76c8ec846b09ee78b13275be)'. 2 instances are missing the same Prefab Asset with guid 2645e53e76c8ec846b09ee78b13275be.";
        private const string KnownSceneHeader = "Problem detected while opening the Scene file: 'Assets/Scenes/GameMaps/TutorialTemporaryMap.unity'.\nCheck the following logs for more details.";
        private enum Phase
        {
            Starting, TerrainStarting, ArmStarting, DiscoveryStarting, ScanCharge, ScanRelease,
            ConfirmAnimal, RefreshCharge, RefreshRelease, PhotographyStarting, ObserveMovement,
            EnterPhoto, Compose, Collapse, Capture, Result, SavePhoto, CloseResult, NeutralAfterResult, ExitPhoto, Departure, Complete, Finishing
        }
        [Serializable] private sealed class Report
        {
            public bool passed, actualAnimalScanHit, nativeSeen, nativeMarkerVisible, subjectFramed;
            public bool motionStarted, sawAnimalMove, animalOnDryGround, playerStayedDry = true;
            public bool holdMovementPaused, resultHiddenAllFrames = true, collapsedBeforeCapture;
            public bool exactSubjectResult, exactResultSaved, albumRecordAdded;
            public bool resultClosedBeforePhotoExit, nativePhotoInactive, lessonComplete;
            public bool robotPhotoLocked, photoEntering, photoExiting, forwardPoseDry, reversePoseDry;
            public bool traversalHasData, traversalPassable, traversalHardStop, subjectPhotographable, subjectHasBounds;
            public bool captureFrameReady, subjectProjected, nativeSelectionFound;
            public bool nativePhotoActive, nativeReviewing, nativeFocusing, nativeCapturing;
            public bool nativeResultUiVisible, nativeResultShowing, nativeResultClosing, resultAnimationClosing;
            public bool neutralBetweenBackPresses, resultFullyOpened;
            public bool deadTrunkPresent, nativePecking, departureQueued, birdFlewAfterPhoto, birdDespawned, trunkRetained, noBirdRespawn;
            public bool waitedForA, confirmedWithA, observationConfirmedWithA;
            public bool birdTargetAfterScan, birdTargetSharedAcrossChapters, birdTargetInPhotoMode;
            public bool birdTargetDuringCapture, birdTargetRetainedOnResult, birdTargetClearedAfterPhoto;
            public string project, phase, step, targetFailure, cameraId, screenshotStatus;
            public string movementMode, traversalReason, nativeSelectedSubject, bodyRendererState;
            public string nativePhotoState, nativeCaptureState, species, subjectPhotoState;
            public string fixturePoseSelection;
            public int nativeScanSequence, shutterEvents, resultEvents, saveEvents, albumBefore, albumAfter;
            public int hiddenResultFrames, screenWidth, screenHeight;
            public int fixturePoseCandidates, knownSceneHeaders, knownMissingPrefabMessages;
            public float charge, travelledMeters, movementFromAnchor, animalWaterDepth, playerWaterDepth;
            public float maximumMovementFromAnchor, maximumHoldDrift, value, target, nativeAimDistance;
            public float robotRotation, throttleIntent, currentSpeed, turnSpeed, traversalSlope, cameraSize;
            public float queuedRightTrigger, queuedLeftTrigger, targetDistanceWorld, targetAngleDegrees;
            public float subjectPixelWidth, subjectPixelHeight, subjectPixelArea, subjectLongestSide, targetFrameCoverage;
            public float minimumSubjectAreaPixels, minimumSubjectLongestSidePixels, minimumSubjectCoverage, frameInsetNormalized;
            public float selectedCoverage;
            public float fixtureAimDistanceWorld, fixtureLocalSurfaceSlope;
            public float resultAnimationProgress, timeScale, deltaTime;
            public Vector2 playerMapPosition, animalMapPosition, initialAnchor, fixtureStart, observationPoint, targetSpawnFixtureStart;
            public Vector2 robotForward, queuedLeftStick, subjectScreenCenter, frameCenter;
            public Vector3 subjectBoundsCenter, subjectBoundsSize, nativeAimWorldPosition;
            public Vector2[] captureFrameCorners = new Vector2[4];
            public List<string> checks = new List<string>(), errors = new List<string>();
            public List<string> knownSceneDiagnostics = new List<string>();
            public List<string> screenshots = new List<string>(), screenshotRequests = new List<string>();
        }

        [SerializeField] private string runId, output;
        [SerializeField] private bool running, originalBackground, cameraFollowSuspended;
        [SerializeField] private int gamepadId, phaseFrame, lastFrame = -1, lastTapPhase = -1, tapFirstFrame = -1;
        [SerializeField] private double deadline;
        [SerializeField] private float phaseStartedAt, holdStartedAt;
        [SerializeField] private Phase phase, refreshReturn;
        [SerializeField] private Report report;
        [SerializeField] private InputSettings originalInput, testInput;
        [SerializeField] private HeightMapPlayerSceneBootstrap bootstrap;
        [SerializeField] private TutorialDrivingLesson driving;
        [SerializeField] private TutorialTerrainLesson terrain;
        [SerializeField] private TutorialArmLesson arms;
        [SerializeField] private TutorialDiscoveryLesson discovery;
        [SerializeField] private TutorialPhotographyLesson lesson;
        [SerializeField] private PhotoCameraDefinition originalCamera;
        [SerializeField] private bool nativeAnimalHit, holdPositionRecorded;
        [SerializeField] private bool composeDiagnosticCaptured;
        [SerializeField] private Vector2 holdAnimalPosition;
        private AnimalPhotoSubject acceptedSubject;
        private PhotoResultSnapshot acceptedResult;
        private DiscoverableEntity capturedEntity;
        private string continueStep;
        private int continueReadyFrame = -1, lastContinueFrame = -10;

        public static void RunBatch()
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            if (!Application.isBatchMode || !project.Contains("/output/validation/"))
                throw new InvalidOperationException("Run photography Play Mode smoke only in a disposable output/validation project.");
            SessionState.EraseString(SessionKey);
            foreach (var old in Resources.FindObjectsOfTypeAll<TutorialPhotographyPlaySmoke>())
            { old.running = false; old.Unsubscribe(); DestroyImmediate(old); }
            var test = CreateInstance<TutorialPhotographyPlaySmoke>();
            test.hideFlags = HideFlags.HideAndDontSave;
            test.runId = Guid.NewGuid().ToString("N"); SessionState.SetString(SessionKey, test.runId);
            string requestedOutput = Environment.GetEnvironmentVariable("TUTORIAL_PHOTOGRAPHY_VALIDATION_OUTPUT");
            test.output = string.IsNullOrWhiteSpace(requestedOutput)
                ? Path.Combine(project, "output/validation/tutorial-photography")
                : Path.GetFullPath(Path.IsPathRooted(requestedOutput) ? requestedOutput : Path.Combine(project, requestedOutput));
            Directory.CreateDirectory(test.output);
            test.report = new Report { project = project };
            ApplyGameViewSize();
            test.Subscribe();
            EditorSceneManager.OpenScene(TutorialDrivingLesson.ScenePath, OpenSceneMode.Single);
            test.deadline = EditorApplication.timeSinceStartup + 240;
            test.running = true; test.Begin(Phase.Starting); EditorApplication.isPlaying = true;
        }

        private bool Current => !string.IsNullOrEmpty(runId) && SessionState.GetString(SessionKey, "") == runId;
        private TutorialDiscoveryTargets Targets => lesson != null && lesson.Targets != null ? lesson.Targets : discovery?.Targets;
        private AnimalPhotoSubject Subject => Targets?.Animal != null ? Targets.Animal.GetComponent<AnimalPhotoSubject>() : null;
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
            if (bootstrap?.bioScan != null) bootstrap.bioScan.AnimalDetected -= OnAnimalDetected;
            if (bootstrap?.photoMode != null) bootstrap.photoMode.PhotoCaptured -= OnShutter;
            if (bootstrap?.photoResultUi != null)
            {
                bootstrap.photoResultUi.ResultShown -= OnResultShown;
                bootstrap.photoResultUi.PhotoSaved -= OnPhotoSaved;
            }
        }

        private void Tick()
        {
            if (!running || !Current) return;
            try
            {
                if (phase == Phase.Finishing)
                { if (!EditorApplication.isPlayingOrWillChangePlaymode) Exit(); return; }
                Require(EditorApplication.timeSinceStartup < deadline, "Photography smoke timed out in " + phase + ".");
                EditorApplication.QueuePlayerLoopUpdate();
                if (!EditorApplication.isPlaying || Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount;
                if (lesson != null && lesson.HasStarted && !lesson.IsComplete)
                    Require(!lesson.GetComponent<TutorialRecyclingLesson>().HasStarted,
                        "Recycling cannot start before a real subject photograph or while its native result is visible.");
                ReadNativeState();
                if (Time.frameCount % 30 == 0) Save();
                CheckRuntimeInvariants();
                switch (phase)
                {
                    case Phase.Starting:
                        bootstrap = Object.FindFirstObjectByType<HeightMapPlayerSceneBootstrap>();
                        driving = Object.FindFirstObjectByType<TutorialDrivingLesson>();
                        lesson = Object.FindFirstObjectByType<TutorialPhotographyLesson>();
                        discovery = Object.FindFirstObjectByType<TutorialDiscoveryLesson>();
                        if (bootstrap?.robot == null || bootstrap.map == null || !bootstrap.map.HasGeneratedMap
                            || driving?.Progress == null || driving.Panel?.Root?.panel == null
                            || lesson == null || discovery == null) return;
                        PrepareVirtualGamepad(); ApplyFixtureStartPosition(true);
                        originalCamera = bootstrap.photoMode.CurrentCamera;
                        Require(originalCamera != null && originalCamera.CameraId == "camera-halfly-broken-prime-50"
                            && originalCamera.Handling == PhotoCameraHandling.FixedPrimeHold,
                            "The lesson must preserve the existing halfly-broken prime camera 50 equipment.");
                        report.cameraId = originalCamera.CameraId; report.albumBefore = PhotoAlbumService.Photos.Count;
                        bootstrap.bioScan.AnimalDetected += OnAnimalDetected;
                        bootstrap.photoMode.PhotoCaptured += OnShutter;
                        bootstrap.photoResultUi.ResultShown += OnResultShown;
                        bootstrap.photoResultUi.PhotoSaved += OnPhotoSaved;
                        CompleteFixture(driving, driving.Progress, DrivingTutorialStep.Completed);
                        terrain = driving.GetComponent<TutorialTerrainLesson>(); arms = driving.GetComponent<TutorialArmLesson>();
                        Require(terrain != null && arms != null, "The preceding scene-owned lessons must exist.");
                        Begin(Phase.TerrainStarting); break;
                    case Phase.TerrainStarting:
                        if (!terrain.HasStarted || terrain.Progress == null) return;
                        CompleteFixture(terrain, terrain.Progress, TerrainTutorialStep.Completed);
                        Begin(Phase.ArmStarting); break;
                    case Phase.ArmStarting:
                        var precedingBalance = arms.GetComponent<TutorialBalanceLesson>();
                        if (precedingBalance != null && precedingBalance.enabled && precedingBalance.Progress != null
                            && !precedingBalance.IsComplete)
                        {
                            CompleteFixture(precedingBalance, precedingBalance.Progress, BalanceTutorialStep.Completed);
                            return;
                        }
                        if (!arms.HasStarted || arms.Progress == null) return;
                        CompleteFixture(arms, arms.Progress, ArmTutorialStep.Completed);
                        Begin(Phase.DiscoveryStarting); break;
                    case Phase.DiscoveryStarting:
                        if (!discovery.HasStarted || discovery.Progress == null || Targets == null || !Targets.IsReady) return;
                        report.initialAnchor = Targets.AnimalMapPosition;
                        Require(!Targets.UsesFixedAnimalPosition
                            || Vector2.Distance(report.initialAnchor, Targets.FixedAnimalMapPositionMeters) < .05f,
                            "A fixed teaching woodpecker must start at its authored dry-land anchor.");
                        Require(bootstrap.map.TrySampleStaticWaterMapPosition(report.initialAnchor, out float initialAnimalDepth)
                            && initialAnimalDepth <= .01f, "The teaching woodpecker must begin on actual dry map ground.");
                        Require(Subject != null && Subject.Species == AnimalSpecies.PileatedWoodpecker,
                            "The actual photo subject must be the same owned tutorial woodpecker.");
                        Require(!Targets.Animal.IsPermanentlyDiscovered && !Targets.HasAnimalMotionStarted,
                            "The animal must begin unknown and remain stationary throughout scanning and photography.");
                        Require(Targets.Trunk != null && Targets.Trunk.IsDead && Targets.AnimalActor != null
                            && Targets.AnimalActor.IsPecking && Targets.AnimalActor.Agent.enabled,
                            "The teaching woodpecker must use a real dead trunk and its native peck action.");
                        report.deadTrunkPresent = report.nativePecking = true;
                        capturedEntity = Targets.Animal;
                        // Spawn the existing discovery object at its authored dry approach first; that
                        // object's ground-placement rules are unrelated to photographing from this dry approach.
                        // Only this guarded disposable fixture changes pose, before any actual scan.
                        ApplyFixtureStartPosition(false);
                        Require(Targets.IsAnimalWithinScanReach, "The guarded dry-ground fixture must be within real scan reach.");
                        Record("Fixture skips earlier chapter counters, prepares discovery targets at the terrain lesson endpoint, then starts actual scanning on the dry photography pose with the same unknown woodpecker.");
                        Begin(Phase.ScanCharge); break;
                    case Phase.ScanCharge:
                        if (bootstrap.scanChargeUi.IsFullyCharged) Begin(Phase.ScanRelease);
                        break;
                    case Phase.ScanRelease:
                        if (!nativeAnimalHit) return;
                        if (discovery.BirdTarget == null || !discovery.BirdTarget.HasTarget) return;
                        Require(Targets.Animal.IsPermanentlyDiscovered && bootstrap.bioScan.ScanSequence > 0,
                            "The real released scan must hit and discover the stationary teaching actor.");
                        Require(Vector2.Distance(discovery.BirdTarget.TargetMapMeters, Targets.AnimalMapPosition) < .001f,
                            "The detected woodpecker immediately owns a target at its actual location.");
                        report.birdTargetAfterScan = true;
                        Record("An actual full LB charge and release hits the designated woodpecker.");
                        Begin(Phase.ConfirmAnimal); break;
                    case Phase.ConfirmAnimal:
                        if (!report.nativeSeen || !report.nativeMarkerVisible)
                        {
                            if (report.actualAnimalScanHit && !bootstrap.bioScan.HasActiveScanWave && !bootstrap.scanChargeUi.IsCharging
                                && Seconds > 5f) Refresh(Phase.ConfirmAnimal);
                            return;
                        }
                        Require(!Targets.HasAnimalMotionStarted && Targets.AnimalTravelledMeters <= .001f,
                            "The scan must leave the teaching woodpecker stationary.");
                        Require(PlayerDepth() <= .01f, "Native seen confirmation must be observed from dry ground.");
                        Record("The same native woodpecker marker becomes seen while the scanned actor remains at its original anchor.");
                        CompleteFixture(discovery, discovery.Progress, DiscoveryTutorialStep.Completed);
                        Begin(Phase.PhotographyStarting); break;
                    case Phase.RefreshCharge:
                        if (bootstrap.scanChargeUi.IsFullyCharged) Begin(Phase.RefreshRelease);
                        break;
                    case Phase.RefreshRelease:
                        if (Seconds < 1.8f || bootstrap.scanChargeUi.IsCharging || bootstrap.bioScan.HasActiveScanWave) return;
                        Begin(refreshReturn); break;
                    case Phase.PhotographyStarting:
                        if (!lesson.HasStarted || lesson.Progress == null) return;
                        Require(!discovery.GetComponent<TutorialRecyclingLesson>().HasStarted,
                            "Photography begins directly after discovery while recycling still waits.");
                        Require(ReferenceEquals(lesson.Targets.Animal, discovery.Targets.Animal),
                            "Photography must retain the scanned woodpecker instance rather than clone or replace it.");
                        Require(lesson.Waypoint != null && ReferenceEquals(lesson.Waypoint, discovery.BirdTarget)
                            && lesson.Waypoint.HasTarget,
                            "Discovery passes the same persistent bird target into photography without duplicating or clearing it.");
                        report.birdTargetSharedAcrossChapters = true;
                        Begin(Phase.ObserveMovement); break;
                    case Phase.ObserveMovement:
                        if (lesson.Progress.Step == PhotographyTutorialStep.ObserveMovement)
                        {
                            if (!report.nativeMarkerVisible && Seconds > 2f) Refresh(Phase.ObserveMovement);
                            return;
                        }
                        Require(lesson.Progress.Step == PhotographyTutorialStep.EnterPhoto && report.travelledMeters <= .001f,
                            "Visible observation of the stationary seen subject advances to the native photo-mode entry action.");
                        Require(report.observationConfirmedWithA,
                            "The ready observation card must remain unchanged for five neutral frames and advance only after actual A.");
                        Record("The photo chapter follows discovery and recognizes the same stationary subject.");
                        Begin(Phase.EnterPhoto); break;
                    case Phase.EnterPhoto:
                        if (!bootstrap.photoMode.IsActive || bootstrap.photoMode.IsEntering) return;
                        if (lesson.Progress.Step != PhotographyTutorialStep.FrameSubject) return;
                        Record("A real controller X press enters the unchanged fixed-prime photo mode.");
                        Begin(Phase.Compose); break;
                    case Phase.Compose:
                        if (!composeDiagnosticCaptured && Seconds >= 3f)
                        {
                            composeDiagnosticCaptured = true;
                            ReadCompositionDiagnostics();
                            Record("Actual Compose diagnostics recorded after three seconds without an eligible native subject.",
                                "photography-compose-diagnostic.png");
                        }
                        if (!report.subjectFramed || lesson.Progress.Step != PhotographyTutorialStep.Capture) return;
                        Require(lesson.Waypoint != null && lesson.Waypoint.HasTarget
                            && lesson.Waypoint.CurrentMode != TutorialWaypointIndicator.WaypointMode.Hidden,
                            "A visible framed woodpecker retains its native target while photo mode is active.");
                        report.birdTargetInPhotoMode = true;
                        Require(bootstrap.photoResultUi.IsSubjectInCaptureFrame(Subject),
                            "Composition must satisfy the actual shutter subject-selection rules.");
                        Record("RT/LT and left-stick steering compose the actual woodpecker using native shutter coverage checks.", "photography-framed.png");
                        Begin(Phase.Collapse); break;
                    case Phase.Collapse:
                        if (!Settled(4)) return;
                        Require(lesson.Panel.IsCollapsed, "A real right D-pad tap collapses the photo tutorial.");
                        report.collapsedBeforeCapture = true;
                        Begin(Phase.Capture); break;
                    case Phase.Capture:
                        if (bootstrap.photoMode.IsFocusing || bootstrap.photoMode.IsCapturing)
                        {
                            if (!bootstrap.photoResultUi.IsResultVisible && !bootstrap.photoMode.IsReviewing
                                && !bootstrap.photoResultView.IsShowing)
                            {
                                Require(lesson.Waypoint != null && lesson.Waypoint.HasTarget
                                    && lesson.Waypoint.CurrentMode != TutorialWaypointIndicator.WaypointMode.Hidden,
                                    "The bird target stays visible through exposure even when the tutorial card is collapsed.");
                                report.birdTargetDuringCapture = true;
                            }
                            if (!holdPositionRecorded)
                            { holdAnimalPosition = Targets.AnimalMapPosition; holdStartedAt = Time.time; holdPositionRecorded = true; }
                            float drift = Vector2.Distance(Targets.AnimalMapPosition, holdAnimalPosition);
                            report.maximumHoldDrift = Mathf.Max(report.maximumHoldDrift, drift);
                            if (Time.time - holdStartedAt >= .08f)
                            {
                                Require(drift < .12f, "The teaching woodpecker must hold its position during a real RB exposure hold.");
                                report.holdMovementPaused = true;
                            }
                        }
                        if (!bootstrap.photoResultUi.IsResultVisible) return;
                        Require(report.shutterEvents > 0 && report.exactSubjectResult && acceptedResult != null,
                            "Capture must produce a real result for the exact teaching animal.");
                        Require(ReferenceEquals(acceptedSubject, Subject) && ReferenceEquals(acceptedResult, bootstrap.photoResultUi.DisplayedResult),
                            "Result identity must remain the actual native subject and snapshot.");
                        Begin(Phase.Result); break;
                    case Phase.Result:
                        CheckResultPanelHidden();
                        if (!Settled(8) || !ResultFullyOpen()) return;
                        Require(lesson.Waypoint != null && lesson.Waypoint.HasTarget
                            && lesson.Waypoint.CurrentMode == TutorialWaypointIndicator.WaypointMode.Hidden,
                            "The original bird target is retained but hidden throughout native photo results.");
                        report.birdTargetRetainedOnResult = true;
                        report.resultFullyOpened = true;
                        Require(report.hiddenResultFrames >= 5 && report.resultHiddenAllFrames,
                            "Both expanded card and collapsed tab must stay hidden throughout the real result screen.");
                        Require(lesson.Progress.IsComplete && lesson.Progress.HasCapturedPhoto
                            && !lesson.Progress.CanContinue && !lesson.Progress.HasSavedPhoto && !lesson.IsComplete,
                            "A valid shot finishes photo practice while native results defer chapter handoff and hide all tutorial UI.");
                        Require(Targets.AnimalActor != null && Targets.AnimalActor.IsDeparting,
                            "The accepted shot must immediately queue the teaching bird's departure.");
                        Require(!Targets.AnimalActor.HasStartedDepartureFlight,
                            "The queued native flight must wait until the result screen closes so it remains visible to the player.");
                        report.departureQueued = true;
                        Record("The entire tutorial root, including the collapsed tab, is hidden on the native photo result screen.",
                            "photography-result-no-tutorial.png");
                        Begin(Phase.SavePhoto); break;
                    case Phase.SavePhoto:
                        CheckResultPanelHidden();
                        Require(report.resultFullyOpened, "Save input must follow the fully opened native result animation.");
                        if (!report.exactResultSaved || !bootstrap.photoResultUi.HasSavedDisplayedResult || !Settled(4)) return;
                        report.albumAfter = PhotoAlbumService.Photos.Count;
                        Require(report.albumAfter == report.albumBefore + 1
                            && ReferenceEquals(PhotoAlbumService.Photos[report.albumAfter - 1].Snapshot, acceptedResult),
                            "The real Y save must add this exact result once to the native album.");
                        report.albumRecordAdded = true;
                        Require(lesson.Progress.HasSavedPhoto && lesson.Progress.IsComplete,
                            "The normal optional album save remains available without a tutorial save step.");
                        Record("A real controller Y press saves the same woodpecker snapshot to the album.");
                        Begin(Phase.CloseResult); break;
                    case Phase.CloseResult:
                        if (bootstrap.photoResultUi.IsResultVisible) { CheckResultPanelHidden(); return; }
                        if (!Settled(4) || bootstrap.photoMode.IsReviewing || bootstrap.photoResultView.IsClosing) return;
                        Require(bootstrap.photoMode.IsActive && lesson.IsComplete && !lesson.Panel.CanContinue,
                            "Closing results completes photography without a save, exit or completion confirmation.");
                        Require(lesson.Waypoint != null && !lesson.Waypoint.HasTarget,
                            "Completing photography clears the persistent bird target before later chapters begin.");
                        report.birdTargetClearedAfterPhoto = true;
                        report.resultClosedBeforePhotoExit = true;
                        Record("The first real B closes the native result; photo mode remains active.");
                        Begin(Phase.NeutralAfterResult); break;
                    case Phase.NeutralAfterResult:
                        if (!Settled(4)) return;
                        var neutralPad = InputSystem.GetDeviceById(gamepadId) as Gamepad;
                        Require(neutralPad != null && !neutralPad.buttonEast.isPressed
                            && !bootstrap.photoResultUi.IsResultVisible && !bootstrap.photoMode.IsReviewing,
                            "Several real neutral input frames must separate closing results and the next B press.");
                        report.neutralBetweenBackPresses = true;
                        if (!lesson.IsComplete) return;
                        Begin(Phase.ExitPhoto); break;
                    case Phase.ExitPhoto:
                        if (bootstrap.photoMode.IsActive || !lesson.IsComplete) return;
                        Require(!bootstrap.photoResultUi.IsResultVisible && bootstrap.robot.enabled
                            && !bootstrap.robot.IsPhotoModeInputLocked && Time.timeScale == 1f,
                            "The second B must finish native photo exit and restore normal player control.");
                        Require(report.holdMovementPaused && report.playerStayedDry && report.animalOnDryGround
                            && report.neutralBetweenBackPresses && report.resultFullyOpened
                            && report.waitedForA && report.confirmedWithA,
                            "This exercise must prove a stationary woodpecker, exposure stability and a dry-land player throughout.");
                        Record("A second real B exits photo mode and completes the lesson with normal driving restored.", "photography-completed.png");
                        Begin(Phase.Departure); break;
                    case Phase.Departure:
                        if (!Targets.AnimalActor.HasDeparted) return;
                        Require(report.birdFlewAfterPhoto && Targets.Animal == null
                            && Targets.Trunk != null && Targets.Trunk.IsDead,
                            "After the accepted shot the native bird must visibly fly, despawn, and leave its trunk behind.");
                        report.birdDespawned = report.trunkRetained = true;
                        Targets.EnsureTargets();
                        Require(Targets.Animal == null && Targets.AnimalActor.HasDeparted,
                            "An explicitly departed teaching woodpecker must never be respawned by target recovery.");
                        report.noBirdRespawn = true;
                        Record("The photographed woodpecker flies away after results close, despawns once, and leaves its owned dead trunk.");
                        Begin(Phase.Complete); break;
                    case Phase.Complete:
                        if (!Settled(10)) return;
                        var followingRecycling = lesson.GetComponent<TutorialRecyclingLesson>();
                        if (!followingRecycling.HasStarted || followingRecycling.Progress == null) return;
                        Require(followingRecycling.Progress.Step == RecyclingTutorialStep.ReachWaste
                            && followingRecycling.TargetWaste != null && Targets.Item != null
                            && followingRecycling.TargetWaste.gameObject == Targets.Item.gameObject,
                            "Completed photography hands off to the same discovery waste as chapter seven without extra A prompts.");
                        Require(lesson.Panel.Root.Q<Label>("TutorialChapter").text == "07 / 09" && Targets.HasAnimalDeparted,
                            "Recycling follows photography without respawning its departed teaching bird.");
                        Require(report.birdTargetAfterScan && report.birdTargetSharedAcrossChapters
                            && report.birdTargetInPhotoMode && report.birdTargetDuringCapture
                            && report.birdTargetRetainedOnResult && report.birdTargetClearedAfterPhoto,
                            "The same native target persists from scan to exposure, hides for results and clears at photography completion.");
                        CollectScreenshots();
                        Finish(null); break;
                }
            }
            catch (Exception error) { Finish(error); }
        }

        private void PrepareVirtualGamepad()
        {
            originalInput = InputSystem.settings; testInput = Instantiate(originalInput);
            testInput.hideFlags = HideFlags.HideAndDontSave;
            testInput.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            testInput.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings = testInput;
            originalBackground = Application.runInBackground; Application.runInBackground = true;
            // Removes device wrappers in this disposable Unity process only, without sending OS input.
            foreach (Gamepad other in new List<Gamepad>(Gamepad.all)) InputSystem.RemoveDevice(other);
            gamepadId = InputSystem.AddDevice<Gamepad>("Photography tutorial validation gamepad").deviceId;
        }

        private static void CompleteFixture(MonoBehaviour controller, object progress, object completed)
        {
            progress.GetType().GetProperty("Step", Flags).SetValue(progress, completed);
            controller.GetType().GetField("chapterConfirmed", Flags).SetValue(controller, true);
        }

        private void ApplyFixtureStartPosition(bool prepareDiscoveryTargets)
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            Require(Current && Application.isBatchMode && project.Contains("/output/validation/"),
                "Only this guarded disposable-project fixture may set its test initial pose.");
            Vector2 start = new Vector2(146f, 121f);
            float heading = prepareDiscoveryTargets ? 180f : 90f;
            string requested = Environment.GetEnvironmentVariable("TUTORIAL_PHOTOGRAPHY_TEST_MAP_POSITION");
            if (!prepareDiscoveryTargets && !string.IsNullOrWhiteSpace(requested))
            {
                string[] values = requested.Split(',');
                Require(values.Length == 2 && float.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                    && float.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out _),
                    "TUTORIAL_PHOTOGRAPHY_TEST_MAP_POSITION must contain invariant logical map coordinates.");
                start = new Vector2(float.Parse(values[0], CultureInfo.InvariantCulture), float.Parse(values[1], CultureInfo.InvariantCulture));
            }
            else if (!prepareDiscoveryTargets)
            {
                Require(TryFindFixtureDryPose(out start, out heading),
                    "No unobstructed dry photography pose near the native fixed-prime aim distance was found.");
            }
            Require(float.IsFinite(start.x) && float.IsFinite(start.y) && bootstrap.map.TrySampleMapPosition(start, out _),
                "The fixture start must be a finite position on the actual map.");
            bootstrap.map.TrySampleStaticWaterMapPosition(start, out float depth);
            Require(depth <= .01f, "The photography fixture must start on dry ground.");
            string requestedHeading = Environment.GetEnvironmentVariable("TUTORIAL_PHOTOGRAPHY_TEST_HEADING");
            if (!prepareDiscoveryTargets && !string.IsNullOrWhiteSpace(requestedHeading))
            {
                Require(float.TryParse(requestedHeading, NumberStyles.Float, CultureInfo.InvariantCulture, out heading)
                    && float.IsFinite(heading), "TUTORIAL_PHOTOGRAPHY_TEST_HEADING must be finite.");
            }
            if (!prepareDiscoveryTargets)
            {
                Require(IsFixtureDryPoseSafe(start, out string reason),
                    "The requested photography fixture pose is unsafe or obstructed: " + reason);
                if (!string.IsNullOrWhiteSpace(requested)) report.fixturePoseSelection = "Explicit environment pose, validated: " + reason;
            }
            bootstrap.robot.transform.position = bootstrap.map.MapPositionToWorld(start);
            bootstrap.robot.transform.rotation = Quaternion.Euler(0f, 0f, -heading);
            bootstrap.cameraFollow.SnapToTarget();
            if (prepareDiscoveryTargets) report.targetSpawnFixtureStart = start;
            else report.fixtureStart = start;
        }

        private bool TryFindFixtureDryPose(out Vector2 start, out float heading)
        {
            start = default; heading = 0f;
            if (Targets?.Animal == null || bootstrap?.photoMode == null || bootstrap.traversalEvaluator == null) return false;
            float aimDistance = bootstrap.photoMode.AimLocalPosition.y;
            if (aimDistance < .1f)
            {
                float near = (float)typeof(PhotoModeController).GetField("minimumAimDistance", Flags).GetValue(bootstrap.photoMode);
                float far = (float)typeof(PhotoModeController).GetField("maximumAimDistance", Flags).GetValue(bootstrap.photoMode);
                aimDistance = Mathf.Lerp(near, far, bootstrap.photoMode.CurrentCamera.FixedAimDistance01);
            }
            report.fixtureAimDistanceWorld = aimDistance;
            Vector2 animalWorld = Targets.Animal.transform.position;
            // Prefer the west side, then alternate around it. Distances use the actual world-space
            // fixed-prime aim, preserving authored map scale and all native capture thresholds.
            foreach (float distanceScale in new[] { 1f, .96f, 1.04f, .9f, 1.1f })
            for (int direction = 0; direction < 48; direction++)
            {
                int alternating = (direction + 1) / 2 * (direction % 2 == 0 ? 1 : -1);
                float angle = (180f + alternating * 7.5f) * Mathf.Deg2Rad;
                Vector2 candidateWorld = animalWorld + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (aimDistance * distanceScale);
                report.fixturePoseCandidates++;
                if (!bootstrap.map.TrySampleWorldPosition(candidateWorld, out Vector2 candidate, out _)
                    || !IsFixtureDryPoseSafe(candidate, out string reason)) continue;
                start = candidate;
                heading = -Vector2.SignedAngle(Vector2.up, (animalWorld - candidateWorld).normalized);
                report.fixturePoseSelection = "Native aim-distance dry-ground ring, validated: " + reason;
                return true;
            }
            return false;
        }

        private bool IsFixtureDryPoseSafe(Vector2 candidate, out string reason)
        {
            reason = "Missing native map or teaching target.";
            if (bootstrap?.map == null || bootstrap.traversalEvaluator == null || Targets?.Animal == null) return false;
            MapTestSceneController map = bootstrap.map;
            if (!float.IsFinite(candidate.x) || !float.IsFinite(candidate.y) || !map.TrySampleMapPosition(candidate, out _))
            { reason = "Position lies outside generated terrain."; return false; }
            map.TrySampleStaticWaterMapPosition(candidate, out float waterDepth);
            if (waterDepth > .01f) { reason = "Position lies in water."; return false; }
            Vector2 world = map.MapPositionToWorld(candidate);
            Vector2 forward = ((Vector2)Targets.Animal.transform.position - world).normalized;
            SlopeTraversalResult surface = bootstrap.traversalEvaluator.EvaluateLocalSurface(world);
            if (!surface.HasData || !surface.IsPassable || surface.RequiresHardStop
                || surface.MaximumSurfaceSlopeAngle > bootstrap.traversalEvaluator.LevelOneMaximumUphillAngle)
            { reason = "Local surface is blocked or exceeds level-one slope: " + surface.BlockReason; return false; }
            SlopeTraversalResult ahead = bootstrap.traversalEvaluator.EvaluateImmediateSafety(world, forward, true);
            SlopeTraversalResult behind = bootstrap.traversalEvaluator.EvaluateImmediateSafety(world, -forward, true);
            if (!ahead.HasData || !ahead.IsPassable || ahead.RequiresHardStop
                || !behind.HasData || !behind.IsPassable || behind.RequiresHardStop)
            { reason = "Native forward/reverse movement probe is blocked: " + ahead.BlockReason + "/" + behind.BlockReason; return false; }
            float radius = Mathf.Max(1.25f, bootstrap.traversalEvaluator.RobotObstacleCollisionRadiusMeters + .3f);
            Vector2 mapForward = map.WorldDirectionToMapDirection(forward);
            var kinds = WorldInteractionKind.Collision | WorldInteractionKind.BodyCollision
                | WorldInteractionKind.Grabbable | WorldInteractionKind.Climbable;
            if (WorldInteractionQuery.Query(InteractionShape.Capsule(candidate - mapForward * 2f, candidate + mapForward * 3f, radius),
                kinds, map, bootstrap.gameObject.scene, ignore: bootstrap.robot.transform))
            { reason = "The robot footprint or its short adjustment path overlaps an authored prop."; return false; }
            report.fixtureLocalSurfaceSlope = surface.MaximumSurfaceSlopeAngle;
            reason = "Dry, level-one ground, native forward/reverse probes and footprint clearance pass.";
            return true;
        }

        private static void ApplyGameViewSize()
        {
            Assembly editorAssembly = typeof(EditorWindow).Assembly;
            Type sizesType = editorAssembly.GetType("UnityEditor.GameViewSizes", true);
            Type sizeType = editorAssembly.GetType("UnityEditor.GameViewSize", true);
            Type kindType = editorAssembly.GetType("UnityEditor.GameViewSizeType", true);
            Type groupType = editorAssembly.GetType("UnityEditor.GameViewSizeGroupType", true);
            Type viewType = editorAssembly.GetType("UnityEditor.GameView", true);
            object sizes = typeof(ScriptableSingleton<>).MakeGenericType(sizesType)
                .GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            object group = sizesType.GetMethod("GetGroup", Flags).Invoke(sizes, new[] { Enum.Parse(groupType, "Standalone") });
            object size = Activator.CreateInstance(sizeType, Flags, null,
                new[] { Enum.Parse(kindType, "FixedResolution"), (object)1920, 1080, "Photography fixture 1920x1080" }, CultureInfo.InvariantCulture);
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
            var gamepad = InputSystem.GetDeviceById(gamepadId) as Gamepad;
            if (gamepad == null) return;
            var state = new GamepadState();
            if (phase == Phase.ScanCharge || phase == Phase.RefreshCharge) state = state.WithButton(GamepadButton.LeftShoulder);
            if ((phase == Phase.EnterPhoto || phase == Phase.Collapse || phase == Phase.SavePhoto
                || phase == Phase.CloseResult || phase == Phase.NeutralAfterResult || phase == Phase.ExitPhoto))
            {
                if (lastTapPhase != (int)phase)
                { lastTapPhase = (int)phase; tapFirstFrame = Time.frameCount; }
                if (Time.frameCount - tapFirstFrame < 3)
                {
                    GamepadButton button = phase == Phase.EnterPhoto ? GamepadButton.West
                        : phase == Phase.Collapse ? GamepadButton.DpadRight
                        : phase == Phase.NeutralAfterResult ? GamepadButton.DpadLeft
                        : phase == Phase.SavePhoto ? GamepadButton.North : GamepadButton.East;
                    state = state.WithButton(button);
                }
            }
            if (phase == Phase.Capture) state = state.WithButton(GamepadButton.RightShoulder);
            if ((phase == Phase.ConfirmAnimal || phase == Phase.ObserveMovement || phase == Phase.PhotographyStarting)
                && Targets?.Animal != null && bootstrap?.robot != null)
            {
                Vector2 offset = Targets.Animal.transform.position - bootstrap.robot.transform.position;
                float angle = Vector2.SignedAngle(bootstrap.robot.Forward, offset.normalized);
                state.leftStick = new Vector2(SteeringForAngle(angle), 0f);
                // Seeing is native circle geometry; do not approach into water to fabricate it.
                if (!report.nativeSeen && Mathf.Abs(angle) < 4f && offset.magnitude > 4f && NextPoseIsDry(1f))
                    state.rightTrigger = .2f;
            }
            if (phase == Phase.Compose && Targets?.Animal != null && bootstrap?.photoMode != null && !report.subjectFramed)
            {
                Vector2 offset = Targets.Animal.transform.position - bootstrap.robot.transform.position;
                float angle = Vector2.SignedAngle(bootstrap.robot.Forward, offset.normalized);
                state.leftStick = new Vector2(SteeringForAngle(angle), 0f);
                float desired = bootstrap.photoMode.AimLocalPosition.y;
                if (Mathf.Abs(angle) < 6f)
                {
                    if (offset.magnitude > desired + .2f && NextPoseIsDry(1f)) state.rightTrigger = .18f;
                    if (offset.magnitude < desired - .2f && NextPoseIsDry(-1f)) state.leftTrigger = .18f;
                }
            }
            if ((phase == Phase.ObserveMovement || phase == Phase.EnterPhoto || phase == Phase.Compose
                || phase == Phase.NeutralAfterResult || phase == Phase.ExitPhoto)
                && lesson?.Progress != null && lesson.Panel != null && lesson.Panel.ContinueWasPresented
                && bootstrap.photoResultUi != null && !bootstrap.photoResultUi.IsResultVisible
                && !bootstrap.photoMode.IsCapturing && !bootstrap.photoMode.IsReviewing)
            {
                string step = lesson.Progress.Step.ToString();
                if (continueStep != step) { continueStep = step; continueReadyFrame = Time.frameCount; }
                if (Time.frameCount - continueReadyFrame >= 5 && Time.frameCount - lastContinueFrame >= 5)
                {
                    report.waitedForA = true; report.confirmedWithA = true;
                    if (lesson.Progress.Step == PhotographyTutorialStep.ObserveMovement) report.observationConfirmedWithA = true;
                    state = state.WithButton(GamepadButton.South); lastContinueFrame = Time.frameCount;
                }
            }
            report.queuedLeftStick = state.leftStick;
            report.queuedRightTrigger = state.rightTrigger; report.queuedLeftTrigger = state.leftTrigger;
            gamepad.MakeCurrent(); InputSystem.QueueStateEvent(gamepad, state);
        }

        private bool NextPoseIsDry(float direction)
        {
            Vector2 world = (Vector2)bootstrap.robot.transform.position + bootstrap.robot.Forward * (.35f * direction);
            if (!bootstrap.map.TrySampleWorldPosition(world, out Vector2 candidate, out _)) return false;
            bootstrap.map.TrySampleStaticWaterMapPosition(candidate, out float depth);
            return depth <= .01f;
        }
        private static float SteeringForAngle(float angle)
        {
            float steering = Mathf.Abs(angle) <= 1.5f ? 0f : Mathf.Clamp(-angle / 18f, -1f, 1f);
            return steering != 0f && Mathf.Abs(steering) < .4f ? Mathf.Sign(steering) * .4f : steering;
        }

        private void ReadNativeState()
        {
            report.timeScale = Time.timeScale; report.deltaTime = Time.deltaTime;
            if (bootstrap?.photoMode != null)
            {
                PhotoModeController controller = bootstrap.photoMode;
                report.nativePhotoState = typeof(PhotoModeController).GetField("state", Flags).GetValue(controller).ToString();
                report.nativeCaptureState = typeof(PhotoModeController).GetField("captureState", Flags).GetValue(controller).ToString();
                report.nativePhotoActive = controller.IsActive;
                report.nativePhotoInactive = !controller.IsActive;
                report.nativeReviewing = controller.IsReviewing; report.nativeFocusing = controller.IsFocusing;
                report.nativeCapturing = controller.IsCapturing;
                report.photoEntering = controller.IsEntering; report.photoExiting = controller.IsExiting;
            }
            if (bootstrap?.robot != null) report.robotPhotoLocked = bootstrap.robot.IsPhotoModeInputLocked;
            report.nativeResultUiVisible = bootstrap?.photoResultUi != null && bootstrap.photoResultUi.IsResultVisible;
            if (bootstrap?.photoResultView != null)
            {
                report.nativeResultShowing = bootstrap.photoResultView.IsShowing;
                report.nativeResultClosing = bootstrap.photoResultView.IsClosing;
                var animation = (PhotoResultAnimation)typeof(PhotoResultView).GetField("animationSettings", Flags).GetValue(bootstrap.photoResultView);
                report.resultAnimationProgress = animation.Progress; report.resultAnimationClosing = animation.IsClosing;
            }
            if (bootstrap?.robot == null) return;
            report.playerWaterDepth = PlayerDepth(); report.playerStayedDry &= report.playerWaterDepth <= .01f;
            if (Targets?.AnimalActor != null && Targets.AnimalActor.HasStartedDepartureFlight)
                report.birdFlewAfterPhoto |= Vector2.Distance(Targets.AnimalActor.LastMapPosition, report.initialAnchor) > .5f;
            if (Targets?.Animal == null) return;
            bootstrap.robot.GetComponent<BioScanAnimalMarkerUI>()?.TryGetScanStatus(Targets.Animal,
                out report.nativeSeen, out report.nativeMarkerVisible, out _);
            report.motionStarted = Targets.HasAnimalMotionStarted;
            report.travelledMeters = Targets.AnimalTravelledMeters; report.sawAnimalMove |= report.travelledMeters >= 2f;
            report.animalMapPosition = Targets.AnimalMapPosition;
            if (Subject != null)
            { report.species = Subject.Species.ToString(); report.subjectPhotoState = Subject.PhotoState.ToString(); }
            bootstrap.map.TrySampleStaticWaterMapPosition(report.animalMapPosition, out report.animalWaterDepth);
            if (Targets.AnimalActor == null || !Targets.AnimalActor.HasStartedDepartureFlight)
                report.animalOnDryGround = report.animalWaterDepth <= .01f;
            if ((int)phase >= (int)Phase.ScanCharge)
            {
                report.movementFromAnchor = Vector2.Distance(report.animalMapPosition, report.initialAnchor);
                report.maximumMovementFromAnchor = Mathf.Max(report.maximumMovementFromAnchor, report.movementFromAnchor);
            }
            report.playerWaterDepth = PlayerDepth(); report.playerStayedDry &= report.playerWaterDepth <= .01f;
            report.subjectFramed = bootstrap.photoResultUi != null && Subject != null && bootstrap.photoResultUi.IsSubjectInCaptureFrame(Subject);
            if (bootstrap.photoMode != null) report.nativeAimDistance = bootstrap.photoMode.AimLocalPosition.y;
            if (phase == Phase.Compose || phase == Phase.Capture) ReadCompositionDiagnostics();
            if (bootstrap.photoResultUi != null && bootstrap.photoResultUi.IsResultVisible) CheckResultPanelHidden();
        }

        private bool ResultFullyOpen() => bootstrap?.photoResultView != null
            && bootstrap.photoResultUi != null && bootstrap.photoResultUi.IsResultVisible
            && bootstrap.photoResultView.IsShowing && !bootstrap.photoResultView.IsWarmingUp
            && !report.resultAnimationClosing && report.resultAnimationProgress >= .999f;

        private void ReadCompositionDiagnostics()
        {
            if (bootstrap?.robot == null || bootstrap.photoMode == null || bootstrap.photoResultUi == null || Subject == null) return;
            report.robotForward = bootstrap.robot.Forward;
            report.robotRotation = bootstrap.robot.transform.eulerAngles.z;
            report.throttleIntent = bootstrap.robot.CurrentThrottleIntent;
            report.currentSpeed = bootstrap.robot.CurrentSpeed; report.turnSpeed = bootstrap.robot.CurrentTurnSpeed;
            report.robotPhotoLocked = bootstrap.robot.IsPhotoModeInputLocked;
            report.movementMode = bootstrap.robot.MovementMode.ToString();
            report.photoEntering = bootstrap.photoMode.IsEntering; report.photoExiting = bootstrap.photoMode.IsExiting;
            report.forwardPoseDry = NextPoseIsDry(1f); report.reversePoseDry = NextPoseIsDry(-1f);
            SlopeTraversalResult traversal = bootstrap.robot.CurrentTraversalResult;
            report.traversalHasData = traversal.HasData; report.traversalPassable = traversal.IsPassable;
            report.traversalHardStop = traversal.RequiresHardStop; report.traversalReason = traversal.BlockReason.ToString();
            report.traversalSlope = traversal.MaximumSurfaceSlopeAngle;
            report.nativeAimWorldPosition = bootstrap.photoMode.AimWorldPosition;
            if (bootstrap.mapCamera != null) report.cameraSize = bootstrap.mapCamera.orthographicSize;
            Vector2 offset = Subject.transform.position - bootstrap.robot.transform.position;
            report.targetDistanceWorld = offset.magnitude;
            report.targetAngleDegrees = Vector2.SignedAngle(bootstrap.robot.Forward, offset.normalized);

            PhotoResultUI results = bootstrap.photoResultUi;
            report.minimumSubjectAreaPixels = (float)typeof(PhotoResultUI).GetField("minimumSubjectAreaPixels", Flags).GetValue(results);
            report.minimumSubjectLongestSidePixels = (float)typeof(PhotoResultUI).GetField("minimumSubjectLongestSidePixels", Flags).GetValue(results);
            report.minimumSubjectCoverage = (float)typeof(PhotoResultUI).GetField("minimumSubjectCoverage", Flags).GetValue(results);
            report.frameInsetNormalized = (float)typeof(PhotoResultUI).GetField("frameInsetNormalized", Flags).GetValue(results);
            report.captureFrameReady = bootstrap.photoModeUi != null
                && bootstrap.photoModeUi.TryGetCaptureFrameScreenCorners(report.captureFrameCorners, report.frameInsetNormalized);
            if (report.captureFrameReady)
            {
                report.frameCenter = Vector2.zero;
                foreach (Vector2 corner in report.captureFrameCorners) report.frameCenter += corner;
                report.frameCenter *= .25f;
            }
            report.subjectPhotographable = Subject.IsPhotographable();
            report.subjectHasBounds = Subject.TryGetWorldBounds(out Bounds bounds);
            report.subjectBoundsCenter = bounds.center; report.subjectBoundsSize = bounds.size;
            report.bodyRendererState = string.Empty;
            foreach (SpriteRenderer renderer in Subject.BodyRenderers)
                if (renderer != null) report.bodyRendererState += renderer.name + ":enabled=" + renderer.enabled
                    + ",active=" + renderer.gameObject.activeInHierarchy + ",alpha=" + renderer.color.a.ToString("F3") + "; ";
            report.subjectProjected = false;
            if (report.subjectHasBounds && bootstrap.mapCamera != null)
            {
                object[] projectionArguments = { bounds, default(Rect) };
                report.subjectProjected = (bool)typeof(PhotoResultUI).GetMethod("TryProjectBoundsToScreen", Flags)
                    .Invoke(results, projectionArguments);
                if (report.subjectProjected)
                {
                    Rect rectangle = (Rect)projectionArguments[1];
                    report.subjectScreenCenter = rectangle.center;
                    report.subjectPixelWidth = rectangle.width; report.subjectPixelHeight = rectangle.height;
                    report.subjectPixelArea = rectangle.width * rectangle.height;
                    report.subjectLongestSide = Mathf.Max(rectangle.width, rectangle.height);
                    if (report.captureFrameReady)
                    {
                        float intersection = (float)typeof(PhotoResultUI).GetMethod("CalculateRectFrameIntersectionArea", Flags)
                            .Invoke(results, new object[] { rectangle, report.captureFrameCorners });
                        report.targetFrameCoverage = report.subjectPixelArea > .0001f ? intersection / report.subjectPixelArea : 0f;
                    }
                }
            }
            object[] selectedArguments = { null, 0f };
            report.nativeSelectionFound = (bool)typeof(PhotoResultUI).GetMethod("TrySelectMainSubject", Flags).Invoke(results, selectedArguments);
            AnimalPhotoSubject selected = selectedArguments[0] as AnimalPhotoSubject;
            report.nativeSelectedSubject = selected != null ? selected.name + " / " + selected.Species.ToString() : string.Empty;
            report.selectedCoverage = (float)selectedArguments[1];
        }

        private void CheckRuntimeInvariants()
        {
            if (originalCamera != null && bootstrap?.photoMode != null)
                Require(ReferenceEquals(bootstrap.photoMode.CurrentCamera, originalCamera), "Tutorial must not replace or modify the equipped camera.");
            if (Targets?.Animal == null || (int)phase < (int)Phase.ScanCharge) return;
            Require(report.playerStayedDry, "The actual controller-driven photographer must remain on dry land.");
            if (Targets.AnimalActor != null && Targets.AnimalActor.HasStartedDepartureFlight) return;
            Require(report.animalOnDryGround, "The teaching woodpecker must remain on actual dry ground throughout the lesson.");
            Require(!Targets.HasAnimalMotionStarted && Targets.AnimalTravelledMeters <= .001f,
                "Scanning and photography must never request teaching woodpecker movement.");
            Require(report.maximumMovementFromAnchor <= .05f,
                "The teaching woodpecker must remain at its original anchor throughout scanning, observation, capture and results.");
        }

        private void CheckResultPanelHidden()
        {
            if (lesson?.Panel == null || !lesson.HasStarted || bootstrap?.photoResultUi == null || !bootstrap.photoResultUi.IsResultVisible) return;
            bool hidden = !lesson.Panel.IsVisible && lesson.Panel.Root != null
                && lesson.Panel.Root.resolvedStyle.display == DisplayStyle.None;
            report.resultHiddenAllFrames &= hidden; report.hiddenResultFrames++;
            Require(hidden, "The entire tutorial root, including the collapsed tab, must be hidden while the native result UI is visible.");
        }

        private float PlayerDepth()
        {
            if (bootstrap?.map == null || bootstrap.robot == null) return 0f;
            bootstrap.map.TrySampleWorldPosition(bootstrap.robot.transform.position, out report.playerMapPosition, out _);
            bootstrap.map.TrySampleStaticWaterMapPosition(report.playerMapPosition, out float depth);
            return depth;
        }
        private void OnAnimalDetected(int sequence, DiscoverableEntity entity)
        {
            if (!Current || Targets?.Animal == null || !ReferenceEquals(entity, Targets.Animal)) return;
            nativeAnimalHit = report.actualAnimalScanHit = true;
        }
        private void OnShutter() { if (Current) report.shutterEvents++; }
        private void OnResultShown(AnimalPhotoSubject subject, PhotoResultSnapshot snapshot)
        {
            if (!Current) return;
            report.resultEvents++;
            if (capturedEntity == null || !ReferenceEquals(subject, capturedEntity.GetComponent<AnimalPhotoSubject>())) return;
            acceptedSubject = subject; acceptedResult = snapshot; report.exactSubjectResult = snapshot != null;
        }
        private void OnPhotoSaved(AnimalPhotoSubject subject, PhotoResultSnapshot snapshot)
        {
            if (!Current) return;
            report.saveEvents++;
            report.exactResultSaved = ReferenceEquals(subject, acceptedSubject) && ReferenceEquals(snapshot, acceptedResult);
        }

        private void Refresh(Phase next) { refreshReturn = next; Begin(Phase.RefreshCharge); }
        private float Seconds => Time.time - phaseStartedAt;
        private bool Settled(int frames) => Time.frameCount - phaseFrame >= frames;
        private void Begin(Phase next) { phase = next; phaseFrame = Time.frameCount; phaseStartedAt = Time.time; Save(); }
        private void Record(string text, string screenshot = null)
        {
            report.checks.Add(text);
            if (!string.IsNullOrEmpty(screenshot))
            {
                string path = Path.Combine(output, "game-" + screenshot);
                ScreenCapture.CaptureScreenshot(path); report.screenshotRequests.Add(path);
            }
            Save();
        }
        private void CollectScreenshots()
        {
            foreach (string path in report.screenshotRequests)
                if (File.Exists(path) && new FileInfo(path).Length > 1024 && !report.screenshots.Contains(path)) report.screenshots.Add(path);
            report.screenshotStatus = report.screenshots.Count == report.screenshotRequests.Count
                ? "All requested actual game screenshots were written."
                : "Some game framebuffer screenshots are unavailable in batch mode; functional outcome checks completed.";
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
                report.step = lesson.Progress.Step.ToString(); report.value = lesson.Progress.Value; report.target = lesson.Progress.Target;
                report.lessonComplete = lesson.IsComplete;
            }
            if (Targets != null) { report.targetFailure = Targets.LastFailure; report.observationPoint = Targets.AnimalObservationMapPosition; }
            if (bootstrap?.scanChargeUi != null) report.charge = bootstrap.scanChargeUi.Charge01;
            if (bootstrap?.bioScan != null) report.nativeScanSequence = bootstrap.bioScan.ScanSequence;
            if (bootstrap?.photoMode != null) report.nativePhotoInactive = !bootstrap.photoMode.IsActive;
            report.albumAfter = PhotoAlbumService.Photos.Count; report.screenWidth = Screen.width; report.screenHeight = Screen.height;
            File.WriteAllText(Path.Combine(output, "photography-play-smoke.json"), JsonUtility.ToJson(report, true));
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
