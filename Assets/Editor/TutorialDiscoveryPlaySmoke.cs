using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using AnimalGame.Animals;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using AnimalGame.Tutorial;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    /// <summary>Actual scan, target presentation and first-sight smoke in a disposable project.</summary>
    public sealed class TutorialDiscoveryPlaySmoke : ScriptableObject
    {
        private const string SessionKey = "AnimalGame.TutorialDiscoveryPlaySmoke.CurrentRun";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string KnownMissingStaticAnimal = "Prefab instance problem: 'Static_Animal (Missing Prefab with guid: 2645e53e76c8ec846b09ee78b13275be)'. 2 instances are missing the same Prefab Asset with guid 2645e53e76c8ec846b09ee78b13275be.";
        private const string KnownSceneHeader = "Problem detected while opening the Scene file: 'Assets/Scenes/GameMaps/TutorialTemporaryMap.unity'.\nCheck the following logs for more details.";
        private enum Phase { Starting, TerrainStarting, ArmStarting, DiscoveryStarting, Hide, Show,
            ShortTaps, EarlyCharge, EarlyRelease, FirstCharge, FirstRelease, TravelToObservation, ObserveAnimal,
            ApproachAnimal, ReadObject, RefreshCharge, RefreshRelease, RescanCharge, RescanRelease, Complete, Finishing }
        [Serializable] private sealed class Report
        {
            public bool passed, animalSeen, animalVisible, observedHollow;
            public bool targetsReady, animalExists, animalScannable, objectExists, objectScannable;
            public bool animalPermanentlyDiscovered, armIsActive, nativeWaveActive;
            public bool earlyChargeFixture, chargedBeforeTargetsReady, fullChargeBeforeTargetsReady;
            public bool prechargeFixture, fullyChargedBeforeChapter;
            public bool usesFixedAnimalPosition, animalWithinScanReach, observationReached, observationPathHasData, observationPathPassable, observationPathHardStop;
            public bool animalMotionRequested, animalMovedAfterScan, checkedAnimalFrozenBeforeScan;
            public string project, phase, step, species, targetFailure, screenshotStatus, movementMode;
            public float value, charge, animalRemaining, objectRemaining, targetDistanceWorld, cameraSize, baseCameraSize, heading;
            public float targetDistanceFromNativeOrigin, currentTurnSpeed, currentThrottleIntent, steeringAuthority;
            public float animalWaterDepth, playerWaterDepth, observationWaterDepth, observationPathMaximumSlope;
            public float animalTravelledMeters, maximumAnimalAnchorDistance;
            public int nativeSequence, terrainRequests, screenWidth, screenHeight, requestedScreenWidth, requestedScreenHeight;
            public int knownSceneHeaders, knownMissingPrefabMessages;
            public Vector2 robotMapPosition, initialMapPosition, animalMapPosition, objectMapPosition;
            public Vector2 nativeWaveOrigin, actualLeftStick, queuedLeftStick;
            public Vector2 fixedAnimalMapPosition, observationMapPosition, initialAnimalMapPosition;
            public List<string> checks = new List<string>(), errors = new List<string>(), screenshots = new List<string>();
            public List<string> knownSceneDiagnostics = new List<string>();
            public List<string> screenshotRequests = new List<string>(), screenshotWarnings = new List<string>();
        }
        [SerializeField] private string runId, output;
        [SerializeField] private bool running, originalBackground, cameraFollowSuspended;
        [SerializeField] private int gamepadId, phaseFrame, lastFrame = -1, lastTapFrame = -1, lastTapPhase = -1;
        [SerializeField] private int terrainRequests, rescanBeforeSequence, refreshBeforeSequence, shortTapCount, firstShortTapFrame;
        [SerializeField] private double deadline;
        [SerializeField] private Phase phase, refreshReturn;
        [SerializeField] private Report report;
        [SerializeField] private InputSettings originalInput, testInput;
        [SerializeField] private HeightMapPlayerSceneBootstrap bootstrap;
        [SerializeField] private TutorialDrivingLesson driving;
        [SerializeField] private TutorialTerrainLesson terrain;
        [SerializeField] private TutorialArmLesson arms;
        [SerializeField] private TutorialDiscoveryLesson lesson;
        [SerializeField] private bool earlyChargeFixture, earlyTargetsRestored, prechargeFixture;
        [SerializeField] private GameObject fixtureAnimalPrefab, fixtureItemPrefab;
        [SerializeField] private Vector2 lastQueuedLeftStick;
        [SerializeField] private bool animalAnchorCaptured, observationReached;
        [SerializeField] private Vector2 initialAnimalMapPosition;

        public static void RunBatch()
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            if (!Application.isBatchMode || !project.Contains("/output/validation/"))
                throw new InvalidOperationException("Run discovery Play Mode smoke only in a disposable output/validation project.");
            SessionState.EraseString(SessionKey);
            foreach (var old in Resources.FindObjectsOfTypeAll<TutorialDiscoveryPlaySmoke>())
            { old.running = false; old.Unsubscribe(); DestroyImmediate(old); }
            var test = CreateInstance<TutorialDiscoveryPlaySmoke>(); test.hideFlags = HideFlags.HideAndDontSave;
            test.runId = Guid.NewGuid().ToString("N"); SessionState.SetString(SessionKey, test.runId);
            string requestedOutput = Environment.GetEnvironmentVariable("TUTORIAL_DISCOVERY_VALIDATION_OUTPUT");
            test.output = string.IsNullOrWhiteSpace(requestedOutput) ? Path.Combine(project, "output/validation/tutorial-discovery")
                : Path.GetFullPath(Path.IsPathRooted(requestedOutput) ? requestedOutput : Path.Combine(project, requestedOutput));
            Directory.CreateDirectory(test.output);
            test.report = new Report { project = project };
            test.ApplyFixtureGameViewSize();
            test.earlyChargeFixture = Environment.GetEnvironmentVariable("TUTORIAL_DISCOVERY_TEST_EARLY_CHARGE") == "1";
            test.report.earlyChargeFixture = test.earlyChargeFixture;
            test.prechargeFixture = Environment.GetEnvironmentVariable("TUTORIAL_DISCOVERY_TEST_PRECHARGE") == "1";
            test.report.prechargeFixture = test.prechargeFixture;
            Require(!test.earlyChargeFixture || !test.prechargeFixture,
                "Run the delayed-target and prechapter-charge scenarios separately so each charge boundary is verified independently.");
            test.Subscribe();
            EditorSceneManager.OpenScene(TutorialDrivingLesson.ScenePath, OpenSceneMode.Single);
            test.deadline = EditorApplication.timeSinceStartup + 210;
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
            if (bootstrap?.scanChargeUi != null) bootstrap.scanChargeUi.TerrainScanRequested -= CountTerrain;
        }
        private void CountTerrain() => terrainRequests++;

        private void Tick()
        {
            if (!running || !Current) return;
            try
            {
                if (phase == Phase.Finishing)
                { if (!EditorApplication.isPlayingOrWillChangePlaymode) Exit(); return; }
                Require(EditorApplication.timeSinceStartup < deadline, "Discovery smoke timed out in " + phase + ".");
                EditorApplication.QueuePlayerLoopUpdate();
                if (!EditorApplication.isPlaying || Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount;
                ReadMarker();
                CheckAnimalMovement();
                if (Time.frameCount % 30 == 0) Save();
                switch (phase)
                {
                    case Phase.Starting:
                        bootstrap = Object.FindFirstObjectByType<HeightMapPlayerSceneBootstrap>();
                        driving = Object.FindFirstObjectByType<TutorialDrivingLesson>();
                        lesson = Object.FindFirstObjectByType<TutorialDiscoveryLesson>();
                        if (bootstrap?.robot == null || bootstrap.map == null || !bootstrap.map.HasGeneratedMap
                            || driving?.Progress == null || driving.Panel?.Root?.panel == null || lesson == null) return;
                        originalInput = InputSystem.settings; testInput = Instantiate(originalInput); testInput.hideFlags = HideFlags.HideAndDontSave;
                        testInput.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                        testInput.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus; InputSystem.settings = testInput;
                        originalBackground = Application.runInBackground; Application.runInBackground = true;
                        // This guarded disposable Unity process exits after the fixture. Make its virtual pad
                        // the first pad so production's legacy-compatible selector cannot read an idle hardware pad.
                        foreach (Gamepad otherPad in new List<Gamepad>(Gamepad.all)) InputSystem.RemoveDevice(otherPad);
                        gamepadId = InputSystem.AddDevice<Gamepad>("Discovery tutorial validation gamepad").deviceId;
                        bootstrap.scanChargeUi.TerrainScanRequested += CountTerrain;
                        ApplyFixtureStartPosition();
                        bootstrap.map.TrySampleWorldPosition(bootstrap.robot.transform.position, out report.initialMapPosition, out _);
                        typeof(DrivingTutorialProgress).GetProperty("Step", Flags).SetValue(driving.Progress, DrivingTutorialStep.Completed);
                        terrain = driving.GetComponent<TutorialTerrainLesson>(); arms = driving.GetComponent<TutorialArmLesson>();
                        Require(terrain != null && arms != null, "All preceding scene-owned lesson components must exist.");
                        Begin(Phase.TerrainStarting); break;
                    case Phase.TerrainStarting:
                        if (!terrain.HasStarted || terrain.Progress == null) return;
                        typeof(TerrainTutorialProgress).GetProperty("Step", Flags).SetValue(terrain.Progress, TerrainTutorialStep.Completed);
                        Begin(Phase.ArmStarting); break;
                    case Phase.ArmStarting:
                        var precedingBalance = arms.GetComponent<TutorialBalanceLesson>();
                        if (precedingBalance != null && precedingBalance.enabled && precedingBalance.Progress != null
                            && !precedingBalance.Progress.IsComplete)
                        {
                            typeof(BalanceTutorialProgress).GetProperty("Step", Flags).SetValue(precedingBalance.Progress, BalanceTutorialStep.Completed);
                            return;
                        }
                        if (!arms.HasStarted || arms.Progress == null) return;
                        if (prechargeFixture)
                        {
                            report.fullyChargedBeforeChapter |= !lesson.HasStarted && bootstrap.scanChargeUi.IsFullyCharged;
                            if (!report.fullyChargedBeforeChapter) return;
                            Record("A real held LB charge reaches full charge in the preceding arm chapter before discovery begins.");
                        }
                        typeof(ArmTutorialProgress).GetProperty("Step", Flags).SetValue(arms.Progress, ArmTutorialStep.Completed);
                        if (earlyChargeFixture)
                        {
                            fixtureAnimalPrefab = (GameObject)typeof(TutorialDiscoveryLesson).GetField("animalPrefab", Flags).GetValue(lesson);
                            fixtureItemPrefab = (GameObject)typeof(TutorialDiscoveryLesson).GetField("smallGarbagePrefab", Flags).GetValue(lesson);
                            Require(fixtureAnimalPrefab != null && fixtureItemPrefab != null,
                                "The early-charge fixture needs the authored target prefabs to restore after its readiness delay.");
                            // Fixture only: keep targets unavailable while a real held LB gesture reaches full charge.
                            typeof(TutorialDiscoveryLesson).GetField("animalPrefab", Flags).SetValue(lesson, null);
                        }
                        Begin(Phase.DiscoveryStarting); break;
                    case Phase.DiscoveryStarting:
                        if (earlyChargeFixture && !earlyTargetsRestored)
                        {
                            bool ready = lesson.Targets != null && lesson.Targets.IsReady;
                            report.chargedBeforeTargetsReady |= !ready && bootstrap.scanChargeUi.IsCharging;
                            report.fullChargeBeforeTargetsReady |= !ready && bootstrap.scanChargeUi.IsFullyCharged;
                            if (!lesson.HasStarted || lesson.Progress == null || !report.fullChargeBeforeTargetsReady) return;
                            Require(!ready && report.chargedBeforeTargetsReady,
                                "The early-charge fixture must observe a real ongoing charge while targets are unavailable.");
                            typeof(TutorialDiscoveryLesson).GetField("animalPrefab", Flags).SetValue(lesson, fixtureAnimalPrefab);
                            lesson.Targets.Configure(bootstrap, fixtureAnimalPrefab, fixtureItemPrefab);
                            earlyTargetsRestored = true;
                            Record("Fixture delayed target readiness until a real held LB scan reached full charge, then restored its original prefabs.");
                            return;
                        }
                        if (!lesson.HasStarted || lesson.Progress == null || lesson.Targets == null || !lesson.Targets.IsReady) return;
                        if (earlyChargeFixture || prechargeFixture)
                        {
                            // Do not release/repress LB: an ongoing real charge must survive the targets becoming ready.
                            if (lesson.Progress.Step != DiscoveryTutorialStep.Release) return;
                            Require(bootstrap.scanChargeUi.IsFullyCharged && lesson.Progress.LatestWaveId == 0,
                                "A full scan begun before readiness must reach Release without emitting a synthetic wave.");
                        }
                        else Require(lesson.Progress.Step == DiscoveryTutorialStep.Charge, "Discovery must start by requesting a fresh charge.");
                        var subject = lesson.Targets.Animal.GetComponent<AnimalPhotoSubject>();
                        Require(subject != null && subject.Species == AnimalSpecies.PileatedWoodpecker
                            && subject.SpeciesId == "pileated_woodpecker",
                            "The designated scan subject must be the requested woodpecker.");
                        report.species = subject.SpeciesId;
                        Require(lesson.Targets.GetComponentsInChildren<AnimalGame.Discovery.DiscoverableEntity>(true).Length == 1
                            && lesson.Targets.GetComponentsInChildren<AnimalGame.World.ScannableInteractable>(true).Length == 1,
                            "The tutorial owner must contain exactly one animal and one object, without duplicate runtime clones.");
                        Require(!lesson.Targets.Animal.IsPermanentlyDiscovered, "The tutorial animal must begin unknown before real scanning.");
                        Require(lesson.Targets.Item.RevealRemaining <= 0f, "The tutorial object must begin without a synthetic reveal.");
                        bootstrap.map.TrySampleWorldPosition(bootstrap.robot.transform.position, out Vector2 current, out _);
                        Require(Vector2.Distance(current, report.initialMapPosition) < .2f,
                            "Skipping earlier chapters must not move the player to a scan practice destination.");
                        initialAnimalMapPosition = lesson.Targets.AnimalMapPosition; animalAnchorCaptured = true;
                        Require(bootstrap.map.TrySampleStaticWaterMapPosition(initialAnimalMapPosition, out float animalDepth)
                            && animalDepth <= .01f, "The woodpecker teaching location must be on actual dry map ground.");
                        Require(!lesson.Targets.HasAnimalMotionStarted && lesson.Targets.AnimalTravelledMeters <= .001f,
                            "The teaching woodpecker must remain frozen at its initial anchor before a real scan hit.");
                        report.checkedAnimalFrozenBeforeScan = true;
                        if (lesson.Targets.UsesFixedAnimalPosition)
                        {
                            Require(Vector2.Distance(initialAnimalMapPosition, lesson.Targets.FixedAnimalMapPositionMeters) < .05f,
                                "A fixed teaching woodpecker must start at its initial dry-land anchor.");
                            Vector2 observation = lesson.Targets.AnimalObservationMapPosition;
                            bootstrap.map.TrySampleStaticWaterMapPosition(observation, out float observationDepth);
                            Require(observationDepth <= .01f, "The configured observation point must be on dry land.");
                            SlopeTraversalResult observationPath = bootstrap.traversalEvaluator.EvaluateMapPath(report.initialMapPosition, observation);
                            report.observationPathHasData = observationPath.HasData;
                            report.observationPathPassable = observationPath.IsPassable;
                            report.observationPathHardStop = observationPath.RequiresHardStop;
                            report.observationPathMaximumSlope = observationPath.MaximumSurfaceSlopeAngle;
                            Record("The requested woodpecker remains fixed on dry ground while the fixture drives to the observation point and scans it.");
                        }
                        Record("Fixture skips earlier chapter progress only; discovery creates an unknown woodpecker and object around the unchanged player.");
                        if (earlyChargeFixture || prechargeFixture)
                        {
                            Record(prechargeFixture
                                ? "The same continuously held LB charge advances to Release across the arm-to-discovery chapter boundary."
                                : "The same continuously held LB charge advances to Release after native tutorial targets become ready.");
                            Begin(Phase.FirstRelease);
                        }
                        else Begin(Phase.Hide);
                        break;
                    case Phase.Hide:
                        if (!Settled(4)) return;
                        Require(lesson.Panel.IsCollapsed, "One right D-pad tap must hide the new chapter exactly once.");
                        Begin(Phase.Show); break;
                    case Phase.Show:
                        if (!Settled(4)) return;
                        Require(!lesson.Panel.IsCollapsed && !AnimalGame.MainUI.MainUI.BlocksGameplay,
                            "One left D-pad tap must show discovery without a previous chapter stealing ownership.");
                        Record("The discovery chapter owns the shared panel and responds once to each D-pad tap.");
                        Begin(Phase.ShortTaps); break;
                    case Phase.ShortTaps:
                        if (!Settled(10)) return;
                        Require(terrainRequests > 0 && lesson.Progress.Step == DiscoveryTutorialStep.Charge
                            && lesson.Progress.LatestWaveId == 0, "A real terrain double-tap must not satisfy biological charge/release.");
                        Record("Two short LB taps still request terrain scanning without advancing this chapter.");
                        Begin(Phase.EarlyCharge); break;
                    case Phase.EarlyCharge:
                        if (!bootstrap.scanChargeUi.IsCharging) return;
                        Require(!bootstrap.scanChargeUi.IsFullyCharged, "The cancellation fixture must release before actual full charge.");
                        Begin(Phase.EarlyRelease); break;
                    case Phase.EarlyRelease:
                        if (!Settled(5)) return;
                        Require(!bootstrap.scanChargeUi.IsCharging && lesson.Progress.Step == DiscoveryTutorialStep.Charge
                            && lesson.Progress.LatestWaveId == 0, "Early release must cancel without counting an emitted scan.");
                        Record("An unfinished real biological charge cancels without awarding charge or target progress.");
                        Begin(Phase.FirstCharge); break;
                    case Phase.FirstCharge:
                        if (!bootstrap.scanChargeUi.IsFullyCharged || lesson.Progress.Step != DiscoveryTutorialStep.Release) return;
                        Require(lesson.Progress.LatestWaveId == 0, "Holding full charge must wait for the release action.");
                        Record("Actual full charge reaches Release while the LB button is still held.");
                        Begin(Phase.FirstRelease); break;
                    case Phase.FirstRelease:
                        if (lesson.Targets.UsesFixedAnimalPosition && !observationReached
                            && lesson.Progress.Step == DiscoveryTutorialStep.FindAnimal)
                        { Begin(Phase.TravelToObservation); break; }
                        if (lesson.Progress.Step == DiscoveryTutorialStep.FindAnimal && report.animalRemaining <= .1f
                            && !bootstrap.scanChargeUi.IsCharging && !bootstrap.bioScan.HasActiveScanWave)
                        { Refresh(Phase.FirstRelease); break; }
                        if (lesson.Progress.Step != DiscoveryTutorialStep.FindAnimal || !report.animalVisible
                            || report.animalRemaining <= 0f || lesson.Targets.Item.RevealRemaining <= 0f) return;
                        Require(lesson.Progress.LatestWaveId > 0 && bootstrap.bioScan.ScanSequence > 0
                            && lesson.Targets.Animal.IsPermanentlyDiscovered, "Real outgoing scan must hit both designated targets.");
                        Require(report.observedHollow, "The initial detected animal must present a real hollow marker outside first-sight confirmation.");
                        Record("The released native wave discovers the woodpecker and highlights the designated object; a hollow animal marker is visible.",
                            "discovery-hollow.png");
                        Begin(Phase.ObserveAnimal); break;
                    case Phase.TravelToObservation:
                        bootstrap.map.TrySampleWorldPosition(bootstrap.robot.transform.position, out Vector2 observationPlayer, out _);
                        if (Vector2.Distance(observationPlayer, lesson.Targets.AnimalObservationMapPosition) > 1f) return;
                        observationReached = true;
                        Require(lesson.Targets.IsAnimalWithinScanReach,
                            "The configured dry observation point must put the fixed woodpecker inside real scan reach.");
                        Require(PlayerWaterDepth() <= .01f, "Approaching the woodpecker for scanning must stay on dry ground.");
                        Record("Real RT and left-stick driving reaches the configured dry observation point; a fresh actual wave will scan the fixed woodpecker.");
                        Refresh(Phase.FirstRelease); break;
                    case Phase.ObserveAnimal:
                        if (lesson.Progress.Step == DiscoveryTutorialStep.FindAnimal)
                        {
                            if (report.animalRemaining <= .1f && !bootstrap.bioScan.HasActiveScanWave)
                                Refresh(Phase.ObserveAnimal);
                            return;
                        }
                        Require(lesson.Progress.Step == DiscoveryTutorialStep.ConfirmAnimal, "Expanded legend reading must advance to first sight, not skip it.");
                        Record("The live animal marker and expanded legend finish the 1.2-second observation.");
                        Begin(Phase.ApproachAnimal); break;
                    case Phase.ApproachAnimal:
                        if (lesson.Progress.Step == DiscoveryTutorialStep.ReadObject)
                        {
                            Require(report.animalSeen, "Approach must produce the real solid/seen marker state.");
                            if (lesson.Targets.UsesFixedAnimalPosition)
                                Require(PlayerWaterDepth() <= .01f, "The real solid woodpecker marker must be confirmed while the player is still on dry ground.");
                            Record("Real RT and left-stick driving brings the animal to the view circle and confirms its solid marker before collision.",
                                "discovery-solid.png");
                            Begin(Phase.ReadObject); break;
                        }
                        if (report.animalRemaining <= .1f) Refresh(Phase.ApproachAnimal);
                        break;
                    case Phase.ReadObject:
                        if (lesson.Progress.Step == DiscoveryTutorialStep.Rescan)
                        { rescanBeforeSequence = bootstrap.bioScan.ScanSequence; Record("The actual object highlight and expanded panel complete the 1.5-second reading."); Begin(Phase.RescanCharge); break; }
                        if (lesson.Targets.Item.RevealRemaining <= .1f) Refresh(Phase.ReadObject);
                        break;
                    case Phase.RefreshCharge:
                        if (bootstrap.scanChargeUi.IsFullyCharged) Begin(Phase.RefreshRelease);
                        break;
                    case Phase.RefreshRelease:
                        if (!Settled(3) || bootstrap.scanChargeUi.IsCharging || bootstrap.bioScan.HasActiveScanWave) return;
                        Require(bootstrap.bioScan.ScanSequence > refreshBeforeSequence,
                            "A refresh must emit a new real native wave before returning to observation.");
                        // Only the currently taught target must be refreshed. The other target may be outside view or range.
                        if (refreshReturn == Phase.ReadObject ? lesson.Targets.Item.RevealRemaining <= 0f
                            : report.animalRemaining <= 0f)
                        { Refresh(refreshReturn); break; }
                        Begin(refreshReturn); break;
                    case Phase.RescanCharge:
                        if (bootstrap.scanChargeUi.IsFullyCharged) Begin(Phase.RescanRelease);
                        break;
                    case Phase.RescanRelease:
                        if (!lesson.IsComplete) return;
                        Require(bootstrap.bioScan.ScanSequence > rescanBeforeSequence,
                            "Rescan completion must belong to a new native scan emitted after this step began.");
                        Record("A new real full charge, release and designated-target hit complete the rescan action.", "discovery-completed.png");
                        Begin(Phase.Complete); break;
                    case Phase.Complete:
                        if (!Settled(5)) return;
                        bool screenshotsReady = ScreenshotsWritten();
                        if (!screenshotsReady && !Settled(60)) return;
                        if (!screenshotsReady)
                        {
                            report.screenshotStatus = "Unavailable in batch mode; functional checks completed successfully.";
                            foreach (string path in report.screenshotRequests)
                                if (!report.screenshots.Contains(path))
                                    report.screenshotWarnings.Add("The game framebuffer screenshot was requested but was not generated: " + path);
                        }
                        Require(lesson.Progress.IsComplete && bootstrap.robot.enabled && Time.timeScale == 1f,
                            "Completing discovery must preserve normal player ownership.");
                        if (animalAnchorCaptured)
                        {
                            Require(report.checkedAnimalFrozenBeforeScan && !lesson.Targets.HasAnimalMotionStarted
                                && lesson.Targets.AnimalTravelledMeters <= .001f,
                                "The teaching woodpecker must remain anchored before and after an actual discovery scan.");
                            Require(report.maximumAnimalAnchorDistance <= .05f,
                                "The scanned teaching woodpecker must remain at its original dry-land anchor.");
                            Require(PlayerWaterDepth() <= .01f, "The completed woodpecker tutorial must leave the player on dry land.");
                        }
                        Finish(null); break;
                }
            }
            catch (Exception error) { Finish(error); }
        }

        private void ApplyFixtureStartPosition()
        {
            string requested = Environment.GetEnvironmentVariable("TUTORIAL_DISCOVERY_TEST_MAP_POSITION");
            string requestedHeading = Environment.GetEnvironmentVariable("TUTORIAL_DISCOVERY_TEST_HEADING");
            if (string.IsNullOrWhiteSpace(requested) && string.IsNullOrWhiteSpace(requestedHeading)) return;
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            Require(Current && Application.isBatchMode && project.Contains("/output/validation/"),
                "Only the current disposable-project fixture may override its test starting position.");
            if (!string.IsNullOrWhiteSpace(requested))
            {
                string[] values = requested.Split(',');
                Require(values.Length == 2
                    && float.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                    && float.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out _),
                    "TUTORIAL_DISCOVERY_TEST_MAP_POSITION must contain invariant logical coordinates such as 146,121.");
                var point = new Vector2(float.Parse(values[0], CultureInfo.InvariantCulture), float.Parse(values[1], CultureInfo.InvariantCulture));
                Require(float.IsFinite(point.x) && float.IsFinite(point.y) && bootstrap.map.TrySampleMapPosition(point, out _),
                    "The fixture's requested starting position must be finite and on the actual generated map.");
                bootstrap.robot.transform.position = bootstrap.map.MapPositionToWorld(point);
            }
            if (!string.IsNullOrWhiteSpace(requestedHeading))
            {
                Require(float.TryParse(requestedHeading, NumberStyles.Float, CultureInfo.InvariantCulture, out float heading)
                    && float.IsFinite(heading), "TUTORIAL_DISCOVERY_TEST_HEADING must be a finite invariant compass heading.");
                bootstrap.robot.transform.rotation = Quaternion.Euler(0f, 0f, -heading);
            }
            bootstrap.cameraFollow.SnapToTarget();
        }

        private void ApplyFixtureGameViewSize()
        {
            string requested = Environment.GetEnvironmentVariable("TUTORIAL_DISCOVERY_TEST_SCREEN");
            if (string.IsNullOrWhiteSpace(requested)) return;
            string[] values = requested.Split(',');
            Require(values.Length == 2 && int.TryParse(values[0], out _) && int.TryParse(values[1], out _),
                "TUTORIAL_DISCOVERY_TEST_SCREEN must contain integer pixels such as 1920,1080.");
            int width = int.Parse(values[0]), height = int.Parse(values[1]);
            Require(width >= 320 && height >= 200 && width <= 7680 && height <= 4320,
                "The disposable fixture's requested Game View resolution is outside its supported range.");
            Assembly editorAssembly = typeof(EditorWindow).Assembly;
            Type sizesType = editorAssembly.GetType("UnityEditor.GameViewSizes", true);
            Type sizeType = editorAssembly.GetType("UnityEditor.GameViewSize", true);
            Type kindType = editorAssembly.GetType("UnityEditor.GameViewSizeType", true);
            Type groupType = editorAssembly.GetType("UnityEditor.GameViewSizeGroupType", true);
            Type viewType = editorAssembly.GetType("UnityEditor.GameView", true);
            object sizes = typeof(ScriptableSingleton<>).MakeGenericType(sizesType)
                .GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            object group = sizesType.GetMethod("GetGroup", Flags).Invoke(sizes,
                new[] { Enum.Parse(groupType, "Standalone") });
            object fixedResolution = Enum.Parse(kindType, "FixedResolution");
            object size = Activator.CreateInstance(sizeType, Flags, null,
                new[] { fixedResolution, (object)width, height, "Discovery fixture " + width + "x" + height }, CultureInfo.InvariantCulture);
            group.GetType().GetMethod("AddCustomSize", Flags).Invoke(group, new[] { size });
            int builtin = (int)group.GetType().GetMethod("GetBuiltinCount", Flags).Invoke(group, null);
            int custom = (int)group.GetType().GetMethod("GetCustomCount", Flags).Invoke(group, null);
            EditorWindow view = EditorWindow.GetWindow(viewType);
            viewType.GetProperty("selectedSizeIndex", Flags).SetValue(view, builtin + custom - 1);
            view.Repaint();
            report.requestedScreenWidth = width; report.requestedScreenHeight = height;
        }

        private void Refresh(Phase resume)
        {
            refreshReturn = resume; refreshBeforeSequence = bootstrap.bioScan.ScanSequence;
            Begin(Phase.RefreshCharge);
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
            if ((phase == Phase.Hide || phase == Phase.Show) && lastTapPhase != (int)phase)
            { state = state.WithButton(phase == Phase.Hide ? GamepadButton.DpadRight : GamepadButton.DpadLeft); lastTapPhase = (int)phase; }
            if (phase == Phase.ShortTaps && (shortTapCount == 0
                || (shortTapCount == 1 && Time.frameCount - firstShortTapFrame >= 3)) && lastTapFrame != Time.frameCount)
            {
                state = state.WithButton(GamepadButton.LeftShoulder); lastTapFrame = Time.frameCount;
                if (shortTapCount == 0) firstShortTapFrame = Time.frameCount;
                shortTapCount++;
            }
            if (phase == Phase.EarlyCharge || phase == Phase.FirstCharge || phase == Phase.RefreshCharge || phase == Phase.RescanCharge)
                state = state.WithButton(GamepadButton.LeftShoulder);
            if (phase == Phase.DiscoveryStarting && earlyChargeFixture && lesson != null && lesson.HasStarted)
                state = state.WithButton(GamepadButton.LeftShoulder);
            if (prechargeFixture && (phase == Phase.ArmStarting || phase == Phase.DiscoveryStarting))
                state = state.WithButton(GamepadButton.LeftShoulder);
            if (phase == Phase.TravelToObservation && lesson?.Targets != null)
            {
                Vector2 destination = bootstrap.map.MapPositionToWorld(lesson.Targets.AnimalObservationMapPosition);
                Vector2 offset = destination - (Vector2)bootstrap.robot.transform.position;
                float angle = Vector2.SignedAngle(bootstrap.robot.Forward, offset.normalized);
                state.leftStick = new Vector2(SteeringForAngle(angle), 0f);
                if (Mathf.Abs(angle) < 4f && offset.magnitude > .15f) state.rightTrigger = .3f;
            }
            bool refreshingAnimal = (phase == Phase.RefreshCharge || phase == Phase.RefreshRelease)
                && refreshReturn != Phase.ReadObject;
            bool rotateToFind = ((phase == Phase.FirstRelease || phase == Phase.ObserveAnimal) && !report.animalVisible)
                || refreshingAnimal;
            if ((phase == Phase.ApproachAnimal || rotateToFind) && lesson?.Targets?.Animal != null)
            {
                Vector2 offset = lesson.Targets.Animal.transform.position - bootstrap.robot.transform.position;
                // Outside-circle markers fit in the landscape viewport's side wedges. Face across
                // the target first so its hollow marker appears at the left; approach only after reading.
                Vector2 look = phase == Phase.ApproachAnimal ? offset : new Vector2(offset.y, -offset.x);
                float angle = Vector2.SignedAngle(bootstrap.robot.Forward, look.normalized);
                float steering = SteeringForAngle(angle);
                state.leftStick = new Vector2(steering, 0f);
                if (phase == Phase.ApproachAnimal && !report.animalSeen
                    && lesson.Progress.Step == DiscoveryTutorialStep.ConfirmAnimal
                    && Mathf.Abs(angle) < 4f && offset.magnitude > .9f)
                    state.rightTrigger = .3f;
            }
            lastQueuedLeftStick = state.leftStick;
            gamepad.MakeCurrent(); InputSystem.QueueStateEvent(gamepad, state);
        }

        private static float SteeringForAngle(float angle)
        {
            float steering = Mathf.Abs(angle) <= 1.5f ? 0f : Mathf.Clamp(-angle / 18f, -1f, 1f);
            return steering != 0f && Mathf.Abs(steering) < .4f ? Mathf.Sign(steering) * .4f : steering;
        }

        private float PlayerWaterDepth()
        {
            if (bootstrap?.robot == null || bootstrap.map == null) return 0f;
            bootstrap.map.TrySampleWorldPosition(bootstrap.robot.transform.position, out Vector2 position, out _);
            bootstrap.map.TrySampleStaticWaterMapPosition(position, out float depth);
            return depth;
        }

        private void CheckAnimalMovement()
        {
            if (!animalAnchorCaptured || lesson?.Targets?.Animal == null || bootstrap?.map == null) return;
            TutorialDiscoveryTargets targets = lesson.Targets;
            float distance = Vector2.Distance(targets.AnimalMapPosition, initialAnimalMapPosition);
            report.maximumAnimalAnchorDistance = Mathf.Max(report.maximumAnimalAnchorDistance, distance);
            Require(!targets.HasAnimalMotionStarted && targets.AnimalTravelledMeters <= .001f && distance <= .05f,
                "Discovery must never request movement or displace the teaching woodpecker from its dry-land anchor.");
            if (!targets.Animal.IsPermanentlyDiscovered)
            {
                Require(!targets.HasAnimalMotionStarted && targets.AnimalTravelledMeters <= .001f && distance < .05f,
                    "Before the real animal scan hit, the teaching woodpecker must stay frozen at its initial dry-land anchor.");
                report.checkedAnimalFrozenBeforeScan = true;
                return;
            }
            Require(bootstrap.bioScan.ScanSequence > 0, "A discovered teaching woodpecker requires a native emitted scan.");
            Require(bootstrap.map.TrySampleStaticWaterMapPosition(targets.AnimalMapPosition, out float depth)
                && depth <= .01f, "The scanned stationary teaching woodpecker must stay on actual dry map ground.");
            report.animalMovedAfterScan |= targets.AnimalTravelledMeters > .05f;
        }

        private void ReadMarker()
        {
            if (lesson?.Targets?.Animal == null || bootstrap?.robot == null) return;
            var markers = bootstrap.robot.GetComponent<BioScanAnimalMarkerUI>();
            if (markers != null) markers.TryGetScanStatus(lesson.Targets.Animal,
                out report.animalSeen, out report.animalVisible, out report.animalRemaining);
            report.observedHollow |= report.animalVisible && !report.animalSeen && report.animalRemaining > 0f;
        }
        private void Begin(Phase next) { phase = next; phaseFrame = Time.frameCount; Save(); }
        private bool Settled(int frames) => Time.frameCount - phaseFrame >= frames;
        private void Record(string text, string screenshot = null)
        {
            report.checks.Add(text);
            if (!string.IsNullOrEmpty(screenshot))
            {
                string path = Path.Combine(output, "game-" + screenshot);
                // Capture the real game framebuffer, including world, all existing HUD canvases and the tutorial.
                ScreenCapture.CaptureScreenshot(path);
                report.screenshotRequests.Add(path);
            }
            Save();
        }
        private bool ScreenshotsWritten()
        {
            foreach (string path in report.screenshotRequests)
                if (File.Exists(path) && new FileInfo(path).Length > 1024 && !report.screenshots.Contains(path))
                    report.screenshots.Add(path);
            bool ready = report.screenshotRequests.Count >= 3 && report.screenshots.Count == report.screenshotRequests.Count;
            report.screenshotStatus = ready ? "All requested game screenshots were written." : "Waiting for optional game screenshots.";
            return ready;
        }
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
            if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && !report.errors.Contains(text))
            { report.errors.Add(text); Save(); }
        }
        private void Save()
        {
            if (report == null || string.IsNullOrEmpty(output)) return;
            report.phase = phase.ToString(); report.terrainRequests = terrainRequests;
            if (lesson?.Progress != null) { report.step = lesson.Progress.Step.ToString(); report.value = lesson.Progress.Value; }
            if (bootstrap?.robot != null)
            {
                bootstrap.map.TrySampleWorldPosition(bootstrap.robot.transform.position, out report.robotMapPosition, out _);
                report.charge = bootstrap.scanChargeUi.Charge01; report.nativeSequence = bootstrap.bioScan.ScanSequence;
                report.heading = Mathf.Repeat(-bootstrap.robot.transform.eulerAngles.z, 360f);
                report.currentTurnSpeed = bootstrap.robot.CurrentTurnSpeed;
                report.currentThrottleIntent = bootstrap.robot.CurrentThrottleIntent;
                report.movementMode = bootstrap.robot.MovementMode.ToString();
            }
            report.queuedLeftStick = lastQueuedLeftStick;
            var gamepad = InputSystem.GetDeviceById(gamepadId) as Gamepad;
            if (gamepad != null) report.actualLeftStick = gamepad.leftStick.ReadValue();
            if (bootstrap?.balance != null) report.steeringAuthority = bootstrap.balance.SteeringAuthority;
            if (bootstrap?.armController != null) report.armIsActive = bootstrap.armController.IsArmModeActive;
            if (bootstrap?.bioScan != null)
            {
                report.nativeWaveOrigin = (Vector2)typeof(BioScanController).GetField("scanWaveOrigin", Flags).GetValue(bootstrap.bioScan);
                report.nativeWaveActive = bootstrap.bioScan.HasActiveScanWave;
            }
            report.screenWidth = Screen.width; report.screenHeight = Screen.height;
            if (bootstrap?.mapCamera != null) report.cameraSize = bootstrap.mapCamera.orthographicSize;
            if (bootstrap?.scanChargeUi != null) report.baseCameraSize = bootstrap.scanChargeUi.BaseCameraOrthographicSize;
            if (lesson?.Targets?.Item != null) report.objectRemaining = lesson.Targets.Item.RevealRemaining;
            if (lesson?.Targets != null)
            {
                report.targetFailure = lesson.Targets.LastFailure;
                report.targetsReady = lesson.Targets.IsReady;
                report.animalExists = lesson.Targets.Animal != null;
                report.animalScannable = report.animalExists && lesson.Targets.Animal.IsScannable;
                report.animalPermanentlyDiscovered = report.animalExists && lesson.Targets.Animal.IsPermanentlyDiscovered;
                report.objectExists = lesson.Targets.Item != null;
                report.objectScannable = report.objectExists && lesson.Targets.Item.CanScan;
                report.animalMapPosition = lesson.Targets.AnimalMapPosition;
                report.objectMapPosition = lesson.Targets.ItemMapPosition;
                report.usesFixedAnimalPosition = lesson.Targets.UsesFixedAnimalPosition;
                report.animalMotionRequested = lesson.Targets.HasAnimalMotionStarted;
                report.animalTravelledMeters = lesson.Targets.AnimalTravelledMeters;
                report.fixedAnimalMapPosition = lesson.Targets.FixedAnimalMapPositionMeters;
                report.observationMapPosition = lesson.Targets.AnimalObservationMapPosition;
                report.animalWithinScanReach = lesson.Targets.IsAnimalWithinScanReach;
                report.observationReached = observationReached;
                report.initialAnimalMapPosition = initialAnimalMapPosition;
                if (report.animalExists)
                    bootstrap.map.TrySampleStaticWaterMapPosition(report.animalMapPosition, out report.animalWaterDepth);
                bootstrap.map.TrySampleStaticWaterMapPosition(report.observationMapPosition, out report.observationWaterDepth);
                report.playerWaterDepth = PlayerWaterDepth();
            }
            if (lesson?.Targets?.Animal != null) report.targetDistanceWorld = Vector2.Distance(bootstrap.robot.transform.position, lesson.Targets.Animal.transform.position);
            if (lesson?.Targets?.Animal != null && bootstrap?.bioScan != null)
                report.targetDistanceFromNativeOrigin = Vector2.Distance(report.nativeWaveOrigin, lesson.Targets.Animal.transform.position);
            File.WriteAllText(Path.Combine(output, "discovery-play-smoke.json"), JsonUtility.ToJson(report, true));
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
            report.passed = report.errors.Count == 0; Save(); running = false; Unsubscribe(); EditorApplication.Exit(report.passed ? 0 : 1);
        }
    }
}
