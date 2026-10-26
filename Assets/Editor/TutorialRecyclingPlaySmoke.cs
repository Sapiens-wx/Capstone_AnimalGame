using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AnimalGame.Discovery;
using AnimalGame.Garbage;
using AnimalGame.MapTest;
using AnimalGame.RobotArm;
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
    /// <summary>Uses real virtual-controller driving, hand contacts, drop/regrab, recycling and inventory rewards.</summary>
    public sealed class TutorialRecyclingPlaySmoke : ScriptableObject
    {
        private const string SessionKey = "AnimalGame.TutorialRecyclingPlaySmoke.CurrentRun";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string KnownSceneHeader = "Problem detected while opening the Scene file: 'Assets/Scenes/GameMaps/TutorialTemporaryMap.unity'.\nCheck the following logs for more details.";
        private const string KnownMissingStaticAnimal = "Prefab instance problem: 'Static_Animal (Missing Prefab with guid: 2645e53e76c8ec846b09ee78b13275be)'. 2 instances are missing the same Prefab Asset with guid 2645e53e76c8ec846b09ee78b13275be.";
        private enum Phase
        {
            Starting, Targets, WaitPhotoA, ConfirmPhoto, Approach, WaitArrivalA, ConfirmArrival,
            Deploy, WaitDeploymentA, ConfirmDeployment, Align, Grab, EnsureOutside, Drop, Regrab,
            HoldGrip, PullInside, WaitInlet, ReleaseInlet, WaitRecycleA, ConfirmRecycle,
            LargePrepare, LargeStow, LargeApproach, WaitLargeArrivalA, ConfirmLargeArrival,
            LargeDeploy, LargeAlign, LargeGrab, LargePull, LargeCancelPull, LargeResumePull,
            FragmentSettle, FragmentPullInside, FragmentAtInlet, FragmentReleaseInlet,
            WaitFragmentRecycleA, ConfirmFragmentRecycle, Stow,
            WaitStowA, ConfirmStow, OpenInventory, ViewMaterials, CloseInventory, WaitInventoryClosed, Refit, Finishing
        }
        [Serializable] private sealed class Report
        {
            public bool passed, sameScanWaste, photoWaitedForA, approachWaitedForA, deploymentWaitedForA;
            public bool realGrab, holdingADidNotAdvance, dropDidNotRecycle, actualRegrab, actualInletReady;
            public bool sawProcessing, actualRecycled, materialsGranted, recycledWaitedForA, actualManualStow;
            public bool finalWaitedForA, refitHandoff, noWasteRespawn, birdStayedDeparted;
            public bool inventoryOpened, materialTutorialVisible, inventoryClosed;
            public string inventoryCloseControl;
            public bool rootCanContinue;
            public bool lateralInletAlignmentUsed;
            public bool inletCueVisibleWhileHolding, inletCueHiddenAfterRecycle;
            public bool readyCardPreservedGrip, freshAAfterNativeRelease;
            public int readyCardGripFrames;
            public bool processingDetailCorrect, recycledDetailCorrect;
            public bool productionLargeTarget, largeArrivalWaitedForA, actualLargeGrip, actualLargePull;
            public bool cancelledPullDidNotComplete, actualLargeBreak, exactHeldFragment, fragmentProcessing;
            public bool fragmentRecycled, fragmentMaterialsGranted, noRefitBeforeFragment;
            public int fragmentRecycleEvents, inventoryBeforeFragment, inventoryAfterFragment;
            public float largePullProgress, maximumLargePullProgress;
            public string project, phase, step, armState, targetFailure;
            public float deployment, targetDistanceWorld, targetDistanceMeters, handDistanceWorld, handAngle;
            public float speed, turnSpeed, throttle, phaseSeconds;
            public float approachAngle, approachRadiusMeters;
            public int recycleEvents, inventoryBefore, inventoryAfter, knownSceneHeaders, knownMissingPrefabMessages;
            public Vector2 playerMapPosition, handCentreLocal, wasteLocal, queuedStick;
            public Vector2 inletCenterLocal, inletHalfExtents;
            public float queuedThrottle;
            public List<string> checks = new(), errors = new(), knownSceneDiagnostics = new(), screenshots = new();
        }
        [SerializeField] private string runId, output;
        [SerializeField] private bool running, originalBackground, cameraFollowSuspended, closeInventoryWithLeft;
        [SerializeField] private int gamepadId, phaseFrame, lastFrame = -1;
        [SerializeField] private double deadline, phaseStarted;
        [SerializeField] private Phase phase;
        [SerializeField] private Report report;
        [SerializeField] private InputSettings originalInput, testInput;
        [SerializeField] private HeightMapPlayerSceneBootstrap bootstrap;
        [SerializeField] private TutorialRecyclingLesson lesson;
        [SerializeField] private TutorialDiscoveryLesson discovery;
        [SerializeField] private TutorialDiscoveryTargets targets;
        [SerializeField] private TutorialPhotographyLesson photography;
        [SerializeField] private TutorialRefitLesson refit;
        [SerializeField] private WorldInteraction waste;
        [SerializeField] private WorldInteraction largeWaste, fragment;
        [SerializeField] private HeavyGarbagePull largePull;
        [SerializeField] private Vector3 largeSourcePosition;
        [SerializeField] private DiscoverableEntity bird;
        private RobotArmController Arms => bootstrap?.armController;
        private RobotMarkerView Marker => bootstrap?.robot != null ? bootstrap.robot.GetComponent<RobotMarkerView>() : null;

        public static void RunBatch()
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            if (!Application.isBatchMode || !project.Contains("/output/validation/"))
                throw new InvalidOperationException("Run recycling Play Mode checks only in a disposable output/validation project.");
            SessionState.EraseString(SessionKey);
            foreach (var old in Resources.FindObjectsOfTypeAll<TutorialRecyclingPlaySmoke>())
            { old.running = false; old.Unsubscribe(); DestroyImmediate(old); }
            var test = CreateInstance<TutorialRecyclingPlaySmoke>(); test.hideFlags = HideFlags.HideAndDontSave;
            test.runId = Guid.NewGuid().ToString("N"); SessionState.SetString(SessionKey, test.runId);
            string configured = Environment.GetEnvironmentVariable("TUTORIAL_RECYCLING_VALIDATION_OUTPUT");
            test.output = string.IsNullOrWhiteSpace(configured) ? Path.Combine(project, "output/validation/tutorial-recycling") : Path.GetFullPath(configured);
            Directory.CreateDirectory(test.output); test.report = new Report { project = project };
            test.closeInventoryWithLeft = Environment.GetEnvironmentVariable("TUTORIAL_RECYCLING_CLOSE_WITH_LEFT") == "1";
            test.report.inventoryCloseControl = test.closeInventoryWithLeft ? "Hold D-pad left" : "Tap B";
            typeof(TutorialPhotographyPlaySmoke).GetMethod("ApplyGameViewSize", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
            test.Subscribe(); EditorSceneManager.OpenScene(TutorialDrivingLesson.ScenePath, OpenSceneMode.Single);
            test.deadline = EditorApplication.timeSinceStartup + 360;
            test.running = true; test.Begin(Phase.Starting); EditorApplication.isPlaying = true;
        }
        private bool Current => !string.IsNullOrEmpty(runId) && SessionState.GetString(SessionKey, "") == runId;
        private void OnEnable() { if (running && Current) Subscribe(); }
        private void OnDisable() => Unsubscribe();
        private void Subscribe()
        { if (!Current) return; Unsubscribe(); EditorApplication.update += Tick; InputSystem.onBeforeUpdate += Drive; Application.logMessageReceived += OnLog; }
        private void Unsubscribe()
        { EditorApplication.update -= Tick; InputSystem.onBeforeUpdate -= Drive; Application.logMessageReceived -= OnLog; if (waste != null) waste.Recycled -= OnRecycled; if (fragment != null) fragment.Recycled -= OnFragmentRecycled; }
        private void OnRecycled() { report.recycleEvents++; report.actualRecycled = true; }
        private void OnFragmentRecycled() { report.fragmentRecycleEvents++; report.fragmentRecycled = true; }

        private void Tick()
        {
            if (!running || !Current) return;
            try
            {
                if (phase == Phase.Finishing) { if (!EditorApplication.isPlayingOrWillChangePlaymode) Exit(); return; }
                Require(EditorApplication.timeSinceStartup < deadline, "Recycling smoke timed out in " + phase + ".");
                Require(phase == Phase.Starting || EditorApplication.timeSinceStartup - phaseStarted < 35,
                    "Recycling phase made no progress: " + phase + "; " + report.armState + "; target " + report.targetDistanceWorld + "; hand " + report.handDistanceWorld);
                EditorApplication.QueuePlayerLoopUpdate();
                if (!EditorApplication.isPlaying || Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount;
                if (Time.frameCount % 20 == 0) Save();
                switch (phase)
                {
                    case Phase.Starting:
                        bootstrap = Object.FindFirstObjectByType<HeightMapPlayerSceneBootstrap>();
                        lesson = Object.FindFirstObjectByType<TutorialRecyclingLesson>();
                        discovery = Object.FindFirstObjectByType<TutorialDiscoveryLesson>();
                        targets = Object.FindFirstObjectByType<TutorialDiscoveryTargets>();
                        photography = Object.FindFirstObjectByType<TutorialPhotographyLesson>();
                        refit = Object.FindFirstObjectByType<TutorialRefitLesson>();
                        if (bootstrap?.map == null || !bootstrap.map.HasGeneratedMap || Arms == null
                            || bootstrap.robot == null || lesson == null || discovery == null || targets == null || refit == null
                            || discovery.Panel?.Root?.panel == null || !Arms.isActiveAndEnabled) return;
                        PrepareFixture(); Begin(Phase.Targets); break;
                    case Phase.Targets:
                        if (!targets.EnsureTargets()) return;
                        Require(targets.Item != null && targets.Item.GetComponent<WorldInteraction>() != null,
                            "The discovery owner must supply its original real small waste.");
                        waste = targets.Item.GetComponent<WorldInteraction>(); bird = targets.Animal;
                        Require(waste.Size == RecyclableSize.Small && waste.Recyclable && waste.RequiredHands == 1,
                            "The first recycling exercise must use one directly recyclable one-hand small waste.");
                        waste.Recycled += OnRecycled;
                        report.inventoryBefore = InventoryQuantity();
                        report.sameScanWaste = true;
                        var completedPhoto = new PhotographyTutorialProgress();
                        SetStep(completedPhoto, PhotographyTutorialStep.Completed);
                        
                        Set(photography,"progress",completedPhoto); Set(photography,"hasStarted",true);
                        Set(photography,"chapterConfirmed",false); photography.SetPresentationOwned(true);
                        Require(targets.BeginAnimalDeparture(), "The saved photograph fixture starts the teaching bird's real departure.");
                        Record("Fixture skips preceding practice/photo counters only; the original discovery waste remains and the teaching bird makes its real departure. Recycling starts without a final photography confirmation card.");
                        Begin(Phase.WaitPhotoA); break;
                    case Phase.WaitPhotoA:
                        if (!Settled(12) || !targets.HasAnimalDeparted || !lesson.HasStarted) return;
                        Require(photography.IsComplete && !refit.HasStarted,
                            "Recycling follows the completed photograph without a separate final A.");
                        report.photoWaitedForA = false; Begin(Phase.ConfirmPhoto); break;
                    case Phase.ConfirmPhoto:
                        if (!lesson.HasStarted || lesson.Progress == null) return;
                        Require(photography.IsComplete && !refit.HasStarted && lesson.TargetWaste == waste,
                            "After photography, recycling must take over the original scanned waste before refit starts.");
                        Require(lesson.Panel.Root.Q<Label>("TutorialTitle").text == lesson.TextContent.Recycling.Title,
                            "Recycling owns the shared tutorial panel.");
                        Require(lesson.Panel.Root.Q<Label>("TutorialChapter").text == "07 / 09", "Recycling is the seventh chapter.");
                        Record("Completed photography transfers the same panel and original waste directly to recycling chapter 07/09.");
                        Begin(Phase.Approach); break;
                    case Phase.Approach:
                        if (!PoseReady() || !lesson.Progress.CanContinue || !Settled(5)) return;
                        Begin(Phase.WaitArrivalA); break;
                    case Phase.WaitArrivalA:
                        if (!Settled(12)) return;
                        Require(lesson.Progress.Step == RecyclingTutorialStep.ReachWaste && !refit.HasStarted,
                            "Arriving at the larger target radius cannot advance without A.");
                        report.approachWaitedForA = true; Begin(Phase.ConfirmArrival); break;
                    case Phase.ConfirmArrival:
                        if (lesson.Progress.Step != RecyclingTutorialStep.DeployArms) return;
                        Begin(Phase.Deploy); break;
                    case Phase.Deploy:
                        if (Arms.VisibleDeployment01 < .999f || !lesson.Progress.CanContinue) return;
                        Begin(Phase.WaitDeploymentA); break;
                    case Phase.WaitDeploymentA:
                        if (!Settled(12)) return;
                        Require(lesson.Progress.Step == RecyclingTutorialStep.DeployArms && Arms.IsArmModeActive,
                            "A real L3 deployment stays on its card until another A confirms it.");
                        report.deploymentWaitedForA = true; Begin(Phase.ConfirmDeployment); break;
                    case Phase.ConfirmDeployment:
                        if (lesson.Progress.Step != RecyclingTutorialStep.RecycleWaste || !Settled(5)) return;
                        Begin(Phase.Align); break;
                    case Phase.Align:
                        Require(!lesson.Panel.CanContinue && !lesson.Progress.CanContinue,
                            "The continuous exercise cannot claim the grabbing A before native recycling succeeds.");
                        if (!HandTouchesWaste()) return;
                        Begin(Phase.Grab); break;
                    case Phase.Grab:
                        if (Arms.HeldObject != waste || !Settled(8)) return;
                        report.realGrab = true; Begin(Phase.EnsureOutside); break;
                    case Phase.EnsureOutside:
                        Require(Arms.HeldObject == waste && !lesson.Panel.CanContinue && !lesson.Progress.CanContinue,
                            "Holding the actual grip A cannot advance the exercise or lose the object.");
                        if (Arms.IsRecycleReady || !Settled(12)) return;
                        report.holdingADidNotAdvance = true; Begin(Phase.Drop); break;
                    case Phase.Drop:
                        if (!Settled(10) || Arms.HeldObject != null) return;
                        Require(waste.Available && report.recycleEvents == 0 && !lesson.Progress.HasRecycledWaste
                            && lesson.Progress.Step == RecyclingTutorialStep.RecycleWaste && !lesson.Panel.CanContinue,
                            "Releasing A outside the inlet drops the actual waste without satisfying recycling.");
                        report.dropDidNotRecycle = true;
                        Record("Native A grips the exact waste. Keeping A held leaves the continuous card active; an actual premature release drops it and earns no recycling credit.");
                        Begin(Phase.Regrab); break;
                    case Phase.Regrab:
                        if (Arms.HeldObject != waste || !Settled(8)) return;
                        report.actualRegrab = true; Begin(Phase.HoldGrip); break;
                    case Phase.HoldGrip:
                        if (!Settled(12)) return;
                        Require(Arms.HeldObject == waste && !lesson.Progress.CanContinue && !lesson.Panel.CanContinue,
                            "A remains exclusively the real grip throughout the continuous practice.");
                        Require(report.readyCardGripFrames >= 8,
                            "The native grip survived repeated ready-card A requests over several real gameplay frames.");
                        report.readyCardPreservedGrip = true;
                        Require(lesson.IsInletCueVisible, "The expanded tutorial shows the real chest receiving outline while the designated waste is held.");
                        report.inletCueVisibleWhileHolding = true;
                        Record("The held waste exposes the tutorial's yellow receiving outline before pulling toward the inlet.", "recycling-held-inlet-outline.png");
                        Begin(Phase.PullInside); break;
                    case Phase.PullInside:
                        Require(Arms.HeldObject == waste, "The real waste must remain gripped while LS pulls toward the chest.");
                        if (!Arms.IsRecycleReady) return;
                        report.actualInletReady = true; Begin(Phase.WaitInlet); break;
                    case Phase.WaitInlet:
                        if (!Settled(8)) return;
                        Require(Arms.HeldObject == waste && Arms.IsRecycleReady && !lesson.Progress.CanContinue,
                            "Entering the chest region is not completion; the player still holds A.");
                        Require(lesson.IsInletCueVisible, "The receiving outline remains visible until the player releases A at the inlet.");
                        Record("Real LS-down arm movement carries the same small waste into the production chest receiving region while A remains held.", "recycling-ready-to-release.png");
                        Begin(Phase.ReleaseInlet); break;
                    case Phase.ReleaseInlet:
                        report.sawProcessing |= Arms.State == RobotArmState.Recycling;
                        if (Arms.State == RobotArmState.Recycling && lesson.Progress.IsProcessing && Settled(3))
                        {
                            RequireRecycleLabels(lesson.TextContent.Recycling.ProcessingStatus, "Native processing");
                            report.processingDetailCorrect = true;
                        }
                        if (report.recycleEvents == 0 || Arms.State == RobotArmState.Recycling) return;
                        Require(report.processingDetailCorrect, "Native processing must show its processing instruction instead of the grip instruction.");
                        Require(report.recycleEvents == 1 && report.sawProcessing && !waste.Available
                            && lesson.Progress.HasRecycledWaste, "Only the real full native recycling completion may satisfy the exercise.");
                        report.inventoryAfter = InventoryQuantity();
                        Require(report.inventoryAfter > report.inventoryBefore, "Successful native recycling must grant material through the existing inventory reward path.");
                        report.materialsGranted = true;
                        Begin(Phase.WaitRecycleA); break;
                    case Phase.WaitRecycleA:
                        if (!Settled(12) || !lesson.Panel.CanContinue) return;
                        Require(lesson.Progress.Step == RecyclingTutorialStep.RecycleWaste,
                            "Actual disposal and its material reward stay on the continuous card until a new A.");
                        Require(!lesson.IsInletCueVisible, "Successful recycling removes the temporary receiving outline.");
                        report.inletCueHiddenAfterRecycle = true;
                        RequireRecycleLabels(lesson.TextContent.Recycling.RecycledStatus, "Successful recycling");
                        report.recycledDetailCorrect = true;
                        report.recycledWaitedForA = true;
                        targets.EnsureTargets();
                        Require(targets.Item == null || ReferenceEquals(targets.Item.GetComponent<WorldInteraction>(), waste),
                            "The discovery owner cannot respawn its explicitly recycled teaching waste.");
                        report.noWasteRespawn = true;
                        Record("Releasing A at the inlet performs native processing, emits exactly one recycling event and grants materials; success then waits for a new A.", "recycling-success.png");
                        Begin(Phase.ConfirmRecycle); break;
                    case Phase.ConfirmRecycle:
                        if (lesson.Progress.Step != RecyclingTutorialStep.ReachLargeWaste) return;
                        report.freshAAfterNativeRelease = true;
                        Begin(Phase.LargePrepare); break;
                    case Phase.LargePrepare:
                        if (lesson.TargetLargeWaste == null || lesson.LargeTargets == null) return;
                        Require(!refit.HasStarted && !lesson.IsComplete,
                            "Small-waste recycling must now continue into large-waste practice before camera upgrade.");
                        if (waste != null) waste.Recycled -= OnRecycled;
                        largeWaste = lesson.TargetLargeWaste; waste = largeWaste;
                        largePull = largeWaste.GetComponent<HeavyGarbagePull>();
                        Require(largePull != null && largeWaste.Size == RecyclableSize.Big
                            && !largeWaste.Recyclable && largeWaste.RequiredHands == 2
                            && largeWaste.GetComponent<GarbageFragmentSpawner>().HasPullFragmentPrefabs,
                            "The owned tutorial pile retains production two-hand heavy-pull and medium-fragment settings.");
                        report.productionLargeTarget = true;
                        report.inventoryBeforeFragment = InventoryQuantity();
                        Record("The same recycling chapter now guides the player to an actual large pile; the existing camera-upgrade chapter remains pending.");
                        Begin(Phase.LargeStow); break;
                    case Phase.LargeStow:
                        if (Arms.State != RobotArmState.Retracted || Arms.VisibleDeployment01 > .001f) return;
                        Begin(Phase.LargeApproach); break;
                    case Phase.LargeApproach:
                        if (!LargePoseReady() || !lesson.Progress.CanContinue || !Settled(5)) return;
                        Begin(Phase.WaitLargeArrivalA); break;
                    case Phase.WaitLargeArrivalA:
                        if (!Settled(12)) return;
                        Require(lesson.Progress.Step == RecyclingTutorialStep.ReachLargeWaste && !refit.HasStarted,
                            "The large pile uses a reachable outside approach and waits for A on its arrival card.");
                        report.largeArrivalWaitedForA = true;
                        Begin(Phase.ConfirmLargeArrival); break;
                    case Phase.ConfirmLargeArrival:
                        if (lesson.Progress.Step != RecyclingTutorialStep.RecycleLargeWaste) return;
                        Begin(Phase.LargeDeploy); break;
                    case Phase.LargeDeploy:
                        if (Arms.VisibleDeployment01 < .999f || !Arms.IsArmModeActive) return;
                        Begin(Phase.LargeAlign); break;
                    case Phase.LargeAlign:
                        if (!HandTouchesWaste(2)) return;
                        Begin(Phase.LargeGrab); break;
                    case Phase.LargeGrab:
                        if (Arms.HeldObject != largeWaste || !Settled(8)) return;
                        Require(largeWaste.Owner == Arms && !lesson.Progress.CanContinue,
                            "Both real hands grip the anchored large pile; gripping it alone cannot complete the lesson.");
                        report.actualLargeGrip = true; largeSourcePosition = largeWaste.WorldPosition;
                        Begin(Phase.LargePull); break;
                    case Phase.LargePull:
                        Require(Arms.HeldObject == largeWaste && Vector3.Distance(largeWaste.WorldPosition, largeSourcePosition) < .0001f,
                            "The large source stays fixed while its real heavy-pull controller accumulates tension.");
                        if (largePull.Progress < .2f) return;
                        report.actualLargePull = true;
                        Record("Holding actual A and LT, rather than moving the arm stick backward, builds the production heavy-pull tension.", "recycling-large-pull.png");
                        Begin(Phase.LargeCancelPull); break;
                    case Phase.LargeCancelPull:
                        if (!Settled(12)) return;
                        Require(largePull.Progress <= .001f && Arms.HeldObject == largeWaste
                            && largeWaste.Available && !lesson.Progress.HasBrokenLargeWaste && !lesson.Progress.HasRecycledFragment,
                            "Releasing LT resets an unfinished pull while A keeps the real pile gripped, without false completion.");
                        report.cancelledPullDidNotComplete = true;
                        Begin(Phase.LargeResumePull); break;
                    case Phase.LargeResumePull:
                        if (!lesson.Progress.HasBrokenLargeWaste || lesson.TargetFragment == null) return;
                        fragment = lesson.TargetFragment;
                        Require(fragment == Arms.HeldObject && fragment.Size == RecyclableSize.Medium
                            && fragment.Recyclable && fragment.Owner == Arms
                            && (largeWaste == null || !largeWaste.gameObject.activeInHierarchy),
                            "Only the real source break and automatic exact medium-fragment handoff satisfy large disassembly.");
                        Require(InventoryQuantity() == report.inventoryBeforeFragment && !refit.HasStarted
                            && !lesson.Progress.CanContinue && !lesson.Progress.HasRecycledFragment,
                            "Splitting the large pile grants no material and cannot skip recycling its held fragment.");
                        fragment.Recycled += OnFragmentRecycled;
                        waste = fragment; report.actualLargeBreak = report.exactHeldFragment = report.noRefitBeforeFragment = true;
                        Record("The actual large pile breaks into native medium fragments, handing the designated piece directly into the player's still-held A grip.", "recycling-large-broken.png");
                        Begin(Phase.FragmentSettle); break;
                    case Phase.FragmentSettle:
                        if (!Settled(24)) return;
                        Require(Arms.HeldObject == fragment && Mathf.Abs(bootstrap.robot.CurrentThrottleIntent) < .01f,
                            "The reverse trigger is released after the break while A keeps the medium fragment held.");
                        Begin(Phase.FragmentPullInside); break;
                    case Phase.FragmentPullInside:
                        Require(Arms.HeldObject == fragment, "The exact held medium fragment remains in the native grip while moving to the inlet.");
                        if (!Arms.IsRecycleReady) return;
                        Begin(Phase.FragmentAtInlet); break;
                    case Phase.FragmentAtInlet:
                        if (!Settled(8)) return;
                        Require(Arms.IsRecycleReady && Arms.HeldObject == fragment && !lesson.Progress.CanContinue
                            && lesson.IsInletCueVisible, "The medium fragment reaches the same yellow native receiving cue before actual release.");
                        Begin(Phase.FragmentReleaseInlet); break;
                    case Phase.FragmentReleaseInlet:
                        report.fragmentProcessing |= Arms.State == RobotArmState.Recycling;
                        if (report.fragmentRecycleEvents == 0 || Arms.State == RobotArmState.Recycling) return;
                        Require(report.fragmentRecycleEvents == 1 && report.fragmentProcessing && lesson.Progress.HasRecycledFragment
                            && !fragment.Available && !refit.HasStarted,
                            "The exact native medium-fragment Recycled callback, after its automatic feed animation, completes the large-waste action.");
                        report.inventoryAfterFragment = InventoryQuantity();
                        Require(report.inventoryAfterFragment > report.inventoryBeforeFragment,
                            "The production medium fragment grants its own material reward only after native recycling succeeds.");
                        report.fragmentMaterialsGranted = true;
                        Begin(Phase.WaitFragmentRecycleA); break;
                    case Phase.WaitFragmentRecycleA:
                        if (!Settled(12) || !lesson.Panel.CanContinue) return;
                        Require(lesson.Progress.Step == RecyclingTutorialStep.RecycleLargeWaste && !refit.HasStarted,
                            "The completed large-waste operation waits for a fresh A on its current action, without a separate completion card.");
                        Record("The exact held medium fragment is pulled to the yellow inlet, released, processed and converted into materials before camera upgrade becomes eligible.", "recycling-large-fragment-recycled.png");
                        Begin(Phase.ConfirmFragmentRecycle); break;
                    case Phase.ConfirmFragmentRecycle:
                        if (lesson.Progress.Step != RecyclingTutorialStep.StowArms) return;
                        Begin(Phase.Stow); break;
                    case Phase.Stow:
                        if (Arms.State != RobotArmState.Retracted || Arms.VisibleDeployment01 > .001f || !lesson.Progress.CanContinue) return;
                        report.actualManualStow = true; Begin(Phase.WaitStowA); break;
                    case Phase.WaitStowA:
                        if (!Settled(12)) return;
                        Require(lesson.Progress.Step == RecyclingTutorialStep.StowArms && !refit.HasStarted,
                            "Stowing the arms is a real action and still waits for A before the next card.");
                        Begin(Phase.ConfirmStow); break;
                    case Phase.ConfirmStow:
                        if (lesson.Progress.Step != RecyclingTutorialStep.OpenInventory) return;
                        Require(!lesson.IsComplete && !refit.HasStarted && !lesson.Progress.CanContinue,
                            "Stowing introduces inventory controls before the camera-upgrade chapter.");
                        Begin(Phase.OpenInventory); break;
                    case Phase.OpenInventory:
                        if (lesson.Progress.Step != RecyclingTutorialStep.ViewMaterials) return;
                        var openedInventory = bootstrap.mainUiObject.GetComponent<AnimalGame.MainUI.MainUI>();
                        Require(openedInventory.IsInventoryOpen && openedInventory.InventoryProgress >= .98f,
                            "The tutorial observes the real inventory opened by holding D-pad right.");
                        Require(Time.timeScale == 0f && lesson.Panel.IsVisible && !lesson.Panel.IsCollapsed
                            && !lesson.Panel.CanContinue && !refit.HasStarted,
                            "Material teaching remains visible and owns no A while the actual inventory pauses gameplay.");
                        report.inventoryOpened = true;
                        Begin(Phase.ViewMaterials); break;
                    case Phase.ViewMaterials:
                        if (!Settled(12)) return;
                        Require(lesson.Progress.Step == RecyclingTutorialStep.ViewMaterials && lesson.Panel.IsVisible
                            && ActuallyVisible(lesson.Panel.Root)
                            && !string.IsNullOrWhiteSpace(lesson.Panel.Root.Q<Label>("TutorialMessage")?.text),
                            "The paused inventory keeps its material explanation visible until the player closes it.");
                        var materialUi = bootstrap.mainUiObject.GetComponent<AnimalGame.MainUI.MainUI>();
                        var materialRoot = (VisualElement)Get(materialUi, "root");
                        var materialHighlights = materialRoot.Query<VisualElement>("InventoryTutorialHighlight").ToList();
                        Require(materialHighlights.Count > 0, "Actually recovered material slots carry the tutorial's gold outline.");
                        foreach (VisualElement highlight in materialHighlights)
                            Require(ActuallyVisible(highlight) && highlight.worldBound.width > 0 && highlight.worldBound.height > 0,
                                "The material outline has real visible layout in the same frame as the paused tutorial.");
                        report.materialTutorialVisible = true;
                        Record("Hold D-pad right opens the actual inventory, showing recovered materials and processing-site guidance during pause.",
                            "recycling-inventory-materials.png");
                        Begin(Phase.CloseInventory); break;
                    case Phase.CloseInventory:
                        if (bootstrap.mainUiObject.GetComponent<AnimalGame.MainUI.MainUI>().IsInventoryOpen) return;
                        Begin(Phase.WaitInventoryClosed); break;
                    case Phase.WaitInventoryClosed:
                        if (!lesson.IsComplete) return;
                        var closedInventory = bootstrap.mainUiObject.GetComponent<AnimalGame.MainUI.MainUI>();
                        Require(!closedInventory.IsInventoryOpen && closedInventory.InventoryProgress <= .001f
                            && !lesson.Progress.CanContinue,
                            "Native closure finishes the inventory teaching after its slide animation, without another A.");
                        Require(((VisualElement)Get(closedInventory, "root")).Query<VisualElement>("InventoryTutorialHighlight").ToList().Count == 0,
                            "Completing inventory teaching removes its temporary material outlines.");
                        report.inventoryClosed = true;
                        report.finalWaitedForA = false; Begin(Phase.Refit); break;
                    case Phase.Refit:
                        if (!refit.HasStarted || !Settled(5)) return;
                        Require(report.productionLargeTarget && report.actualLargeGrip && report.actualLargePull
                            && report.cancelledPullDidNotComplete && report.actualLargeBreak && report.exactHeldFragment
                            && report.fragmentRecycled && report.fragmentMaterialsGranted && report.noRefitBeforeFragment
                            && report.inventoryOpened && report.materialTutorialVisible && report.inventoryClosed,
                            "Real recycling, material viewing and native inventory closure must succeed before refit.");
                        targets.EnsureTargets();
                        Require(targets.HasAnimalDeparted && targets.Animal == null && targets.Trunk != null,
                            "The earlier photograph's departed woodpecker is never respawned during recycling or refit.");
                        Require(refit.Panel.Root.Q<Label>("TutorialChapter").text == "08 / 09",
                            "Camera upgrade follows recycling as chapter eight.");
                        report.birdStayedDeparted = report.refitHandoff = true;
                        Record("Closing the viewed material inventory passes the panel to camera upgrade with the photo bird still departed and no replacement waste.", "recycling-refit-handoff.png");
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
            gamepadId = InputSystem.AddDevice<Gamepad>("Recycling tutorial validation gamepad").deviceId;
            bootstrap.robot.transform.position = bootstrap.map.MapPositionToWorld(new Vector2(146f,121f));
            bootstrap.robot.transform.rotation = Quaternion.Euler(0f,0f,180f); bootstrap.cameraFollow?.SnapToTarget();
            Complete(discovery.GetComponent<TutorialDrivingLesson>(), new DrivingTutorialProgress(), DrivingTutorialStep.Completed);
            Complete(discovery.GetComponent<TutorialTerrainLesson>(), new TerrainTutorialProgress(new[] {new Vector2(163,140)},new Vector2(146,121)), TerrainTutorialStep.Completed);
            Complete(discovery.GetComponent<TutorialBalanceLesson>(), new BalanceTutorialProgress(), BalanceTutorialStep.Completed);
            Complete(discovery.GetComponent<TutorialArmLesson>(), new ArmTutorialProgress(), ArmTutorialStep.Completed);
            var completed = new DiscoveryTutorialProgress(); SetStep(completed, DiscoveryTutorialStep.Completed);
            Set(discovery,"progress",completed); Set(discovery,"hasStarted",true); Set(discovery,"chapterConfirmed",true);
            discovery.SetPresentationOwned(false);
            var animalPrefab = (GameObject)Get(discovery,"animalPrefab"); var itemPrefab = (GameObject)Get(discovery,"smallGarbagePrefab");
            targets.Configure(bootstrap,animalPrefab,itemPrefab);
        }
        private static void Complete(MonoBehaviour controller, object progress, object step)
        {
            SetStep(progress,step); Set(controller,"progress",progress);
            if (controller.GetType().GetField("hasStarted",Flags) != null) Set(controller,"hasStarted",true);
            Set(controller,"chapterConfirmed",true);
            controller.GetType().GetMethod("SetPresentationOwned",Flags)?.Invoke(controller,new object[]{false});
        }
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
            if (phase == Phase.Approach) DriveApproach(ref state);
            if (phase == Phase.Deploy || phase == Phase.Stow || phase == Phase.LargeStow || phase == Phase.LargeDeploy)
                Tap(ref state,GamepadButton.LeftStick);
            if (phase == Phase.ConfirmArrival || phase == Phase.ConfirmDeployment
                || phase == Phase.ConfirmRecycle || phase == Phase.ConfirmStow
                || phase == Phase.ConfirmLargeArrival || phase == Phase.ConfirmFragmentRecycle)
                Tap(ref state,GamepadButton.South);
            if (phase == Phase.OpenInventory) state = state.WithButton(GamepadButton.DpadRight);
            if (phase == Phase.CloseInventory)
            {
                if (closeInventoryWithLeft) state = state.WithButton(GamepadButton.DpadLeft);
                else Tap(ref state, GamepadButton.East);
            }
            if (phase == Phase.LargeApproach) DriveLargeApproach(ref state);
            if (phase == Phase.LargeAlign) AlignLargeHands(ref state);
            if (phase == Phase.Align || phase == Phase.Regrab) AlignHands(ref state);
            if (phase == Phase.Grab || phase == Phase.EnsureOutside || phase == Phase.Regrab
                || phase == Phase.HoldGrip || phase == Phase.PullInside || phase == Phase.WaitInlet)
                state = state.WithButton(GamepadButton.South);
            if (phase == Phase.EnsureOutside && Arms != null && Arms.IsRecycleReady) state.leftStick = Vector2.up * .6f;
            if (phase == Phase.HoldGrip && Arms != null && Arms.HeldObject == waste)
            {
                // Simulate another tutorial action becoming ready while the player freely grips
                // an item. The real arm Update must continue seeing the held native A.
                lesson.Panel.SetContinuePrompt(true);
                Require(lesson.Panel.CanContinue, "The conflicting ready card is visible for the native input regression.");
                Require(!TutorialContinueInput.BlocksWorldSouth(lesson.gameObject.scene),
                    "A ready tutorial card must defer South input to the actual held native item.");
                report.readyCardGripFrames++;
            }
            if (phase == Phase.PullInside) PullTowardInlet(ref state);
            if (phase == Phase.LargeGrab || phase == Phase.LargePull || phase == Phase.LargeCancelPull
                || phase == Phase.LargeResumePull || phase == Phase.FragmentSettle
                || phase == Phase.FragmentPullInside || phase == Phase.FragmentAtInlet)
                state = state.WithButton(GamepadButton.South);
            if (phase == Phase.LargePull || phase == Phase.LargeResumePull) state.leftTrigger = 1f;
            if (phase == Phase.FragmentPullInside) PullTowardInlet(ref state);
            report.queuedStick = state.leftStick; report.queuedThrottle = state.rightTrigger - state.leftTrigger;
            pad.MakeCurrent(); InputSystem.QueueStateEvent(pad,state);
        }
        private void Tap(ref GamepadState state, GamepadButton button)
        { if (Time.frameCount-phaseFrame < 3) state = state.WithButton(button); }
        private float HandSpacing => Arms != null ? (float)Get(Arms,"handSpacing") : .5f;
        private float DesiredReach => Arms != null ? (float)Arms.GetType().GetMethod("MaximumCommonReach",Flags).Invoke(Arms,null) * .75f : 1f;
        private float LargeRadiusWorld => largeWaste != null ? largeWaste.GetShape(null).Radius : 0f;
        private float DesiredLargeDistance => LargeRadiusWorld + DesiredReach - .1f;
        private float LargeApproachAngle() => largeWaste != null
            ? Vector2.SignedAngle(bootstrap.robot.Forward, (Vector2)largeWaste.WorldPosition - (Vector2)bootstrap.robot.transform.position) : 0f;
        private bool LargePoseReady() => largeWaste != null
            && Vector2.Distance(largeWaste.WorldPosition, bootstrap.robot.transform.position) <= DesiredLargeDistance + .04f
            && Mathf.Abs(LargeApproachAngle()) <= 10f;
        private void DriveLargeApproach(ref GamepadState state)
        {
            if (largeWaste == null || bootstrap?.robot == null || Time.deltaTime <= 0f || LargePoseReady()) return;
            float angle = LargeApproachAngle();
            float distance = Vector2.Distance(largeWaste.WorldPosition, bootstrap.robot.transform.position);
            if (Mathf.Abs(angle) > 8f) state.leftStick = Vector2.right * -Mathf.Sign(angle) * Mathf.Clamp(Mathf.Abs(angle) / 30f, .35f, .5f);
            if (distance > DesiredLargeDistance + .025f && Mathf.Abs(angle) < 16f)
                state.rightTrigger = distance < DesiredLargeDistance + .45f ? .18f : .33f;
        }
        private void AlignLargeHands(ref GamepadState state)
        {
            if (largeWaste == null || Marker?.MarkerVisualRoot == null || Arms == null || HandTouchesWaste(2)) return;
            Vector2 away = ((Vector2)largeWaste.WorldPosition - (Vector2)bootstrap.robot.transform.position).normalized;
            Vector2 targetWorld = (Vector2)largeWaste.WorldPosition - away * Mathf.Max(0f, LargeRadiusWorld - .1f);
            Vector2 desired = Marker.MarkerVisualRoot.InverseTransformPoint(targetWorld);
            Vector2 centre = Marker.MarkerVisualRoot.InverseTransformPoint((Arms.LeftHandWorld + Arms.RightHandWorld) * .5f);
            float angle = Vector2.SignedAngle(centre, desired);
            state.leftStick = new Vector2(ArmRate(-angle, 25f), ArmRate(desired.magnitude - centre.magnitude, .25f));
        }
        private bool PoseReady() => waste != null && Vector2.Distance(waste.transform.position,bootstrap.robot.transform.position)
            <= Mathf.Sqrt(DesiredReach*DesiredReach+HandSpacing*HandSpacing*.25f)+.04f && Mathf.Abs(ApproachAngle()) <= 12f;
        private float ApproachAngle()
        {
            Vector2 delta = (Vector2)waste.transform.position-(Vector2)bootstrap.robot.transform.position;
            float correction = Mathf.Atan2(HandSpacing*.5f,DesiredReach);
            Vector2 facing = new Vector2(delta.x*Mathf.Cos(-correction)-delta.y*Mathf.Sin(-correction),delta.x*Mathf.Sin(-correction)+delta.y*Mathf.Cos(-correction)).normalized;
            return Vector2.SignedAngle(bootstrap.robot.Forward,facing);
        }
        private void DriveApproach(ref GamepadState state)
        {
            if (waste == null || bootstrap?.robot == null || Time.deltaTime <= 0f) return;
            Vector2 delta = (Vector2)waste.transform.position-(Vector2)bootstrap.robot.transform.position;
            float desired = Mathf.Sqrt(DesiredReach*DesiredReach+HandSpacing*HandSpacing*.25f);
            float angle = ApproachAngle();
            // Navigation needs a comfortable stopping pose, not a precision docking angle.
            // The following real arm exercise aligns a hand with the waste independently.
            if (PoseReady()) return;
            if (Mathf.Abs(angle)>10f)
                state.leftStick = Vector2.right * -Mathf.Sign(angle) * Mathf.Clamp(Mathf.Abs(angle)/30f,.4f,.5f);
            if (delta.magnitude <= desired+.025f) return;
            if (Mathf.Abs(angle)<18f) state.rightTrigger = delta.magnitude < desired+.5f ? .18f : .33f;
        }
        private void AlignHands(ref GamepadState state)
        {
            if (waste == null || Marker?.MarkerVisualRoot == null || Arms == null || Arms.HeldObject != null) return;
            Vector2 target = Marker.MarkerVisualRoot.InverseTransformPoint(waste.transform.position);
            Vector2 centre = Marker.MarkerVisualRoot.InverseTransformPoint((Arms.LeftHandWorld+Arms.RightHandWorld)*.5f);
            Vector2 desired = target + Vector2.right*(HandSpacing*.5f);
            float angle = Vector2.SignedAngle(centre,desired);
            if (!HandTouchesWaste()) state.leftStick = new Vector2(ArmRate(-angle,25f),ArmRate(desired.magnitude-centre.magnitude,.25f));
        }
        private static float ArmRate(float error,float scale) => Mathf.Abs(error)<.001f ? 0f
            : Mathf.Sign(error)*Mathf.Clamp(Mathf.Abs(error)/scale,.2f,.45f);
        private void PullTowardInlet(ref GamepadState state)
        {
            if (Arms == null || waste == null || Marker?.MarkerVisualRoot == null || Arms.IsRecycleReady) return;
            if (!Arms.TryGetRecyclingInlet(out Vector2 center,out Vector2 halfExtents)) return;
            report.inletCenterLocal = center; report.inletHalfExtents = halfExtents;
            Vector2 position = Marker.MarkerVisualRoot.InverseTransformPoint(waste.transform.position);
            // A genuine off-centre hand contact preserves its actual grip offset. Bring the
            // waste's centre into the native side channel instead of assuming straight pull suffices.
            float innerWidth = halfExtents.x * .7f;
            float lateralError = Mathf.Clamp(position.x,center.x-innerWidth,center.x+innerWidth)-position.x;
            float lateral = Mathf.Abs(lateralError) <= .002f ? 0f
                : Mathf.Sign(lateralError)*Mathf.Clamp(Mathf.Abs(lateralError)/Mathf.Max(.01f,halfExtents.x),.4f,.5f);
            if (lateral != 0f) report.lateralInletAlignmentUsed = true;
            state.leftStick = new Vector2(lateral,lateral != 0f ? -.35f : -.55f);
        }
        private bool HandTouchesWaste(int requiredContacts = 1)
        {
            if (waste == null || Arms == null || bootstrap?.map == null) return false;
            var hits = new List<WorldInteraction>();
            int contacts = 0;
            foreach (Vector2 hand in new[] { Arms.LeftHandWorld, Arms.RightHandWorld })
            {
                hits.Clear();
                WorldInteractionQuery.Query(InteractionShape.Circle(InteractionShape.ToQuery(hand,bootstrap.map),0f),
                    WorldInteractionKind.Grabbable,bootstrap.map,bootstrap.gameObject.scene,hits,bootstrap.robot.transform);
                if (hits.Contains(waste)) contacts++;
            }
            return contacts >= requiredContacts;
        }
        private int InventoryQuantity()
        { var inventory = bootstrap.mainUiObject.GetComponent<AnimalGame.MainUI.MainUI>().Inventory; int total=0; for(int i=0;i<inventory.Capacity;i++) total+=inventory.Quantity(i); return total; }
        private static bool ActuallyVisible(VisualElement element)
        {
            if (element == null || element.panel == null) return false;
            for (var current = element; current != null; current = current.parent)
                if (current.resolvedStyle.display == DisplayStyle.None || current.resolvedStyle.visibility == Visibility.Hidden
                    || current.resolvedStyle.opacity <= 0f) return false;
            return true;
        }
        private static object Get(object target,string field) => target.GetType().GetField(field,Flags).GetValue(target);
        private static void Set(object target,string field,object value) => target.GetType().GetField(field,Flags).SetValue(target,value);
        private static void SetStep(object progress,object step) => progress.GetType().GetProperty("Step",Flags).SetValue(progress,step);
        private bool Settled(int frames) => Time.frameCount-phaseFrame>=frames;
        private void RequireRecycleLabels(string expected, string context)
        {
            foreach (string name in new[] { "TutorialAction", "TutorialDetail", "TutorialStatus" })
            {
                string actual = lesson.Panel.Root.Q<Label>(name)?.text;
                Require(actual == expected, context + " must replace the grip instruction in " + name
                    + "; expected '" + expected + "', got '" + actual + "'.");
            }
        }
        private void Begin(Phase next) { phase=next; phaseFrame=Time.frameCount; phaseStarted=EditorApplication.timeSinceStartup; Save(); }
        private void Record(string message,string filename=null)
        { report.checks.Add(message); if(filename!=null){string path=Path.Combine(output,filename);ScreenCapture.CaptureScreenshot(path);report.screenshots.Add(path);} Save(); }
        private static void Require(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
        private void OnLog(string message,string stack,LogType type)
        {
            if(!Current || report==null)return;
            string normalized=message.Replace("\r\n","\n").Trim();
            if(type==LogType.Error && (phase==Phase.Starting || phase==Phase.Finishing) && (normalized==KnownSceneHeader || normalized==KnownMissingStaticAnimal))
            {if(normalized==KnownSceneHeader)report.knownSceneHeaders++;else report.knownMissingPrefabMessages++; if(!report.knownSceneDiagnostics.Contains(normalized))report.knownSceneDiagnostics.Add(normalized);return;}
            if((type==LogType.Error || type==LogType.Exception || type==LogType.Assert)&&!report.errors.Contains(message)){report.errors.Add(message);Save();}
        }
        private void Save()
        {
            if(report==null||string.IsNullOrEmpty(output))return;
            report.phase=phase.ToString(); report.phaseSeconds=(float)(EditorApplication.timeSinceStartup-phaseStarted);
            report.step=lesson?.Progress?.Step.ToString(); report.targetFailure=targets?.LastFailure;
            if (largePull != null)
            { report.largePullProgress = largePull.Progress; report.maximumLargePullProgress = Mathf.Max(report.maximumLargePullProgress, largePull.Progress); }
            if(lesson!=null) report.approachRadiusMeters=lesson.ApproachRadiusMeters;
            if(lesson?.Progress!=null) report.rootCanContinue=lesson.Progress.CanContinue;
            if(waste!=null&&bootstrap?.robot!=null) report.approachAngle=ApproachAngle();
            if(bootstrap?.robot!=null)
            {bootstrap.map.TrySampleWorldPosition(bootstrap.robot.transform.position,out report.playerMapPosition,out _);report.speed=bootstrap.robot.CurrentSpeed;report.turnSpeed=bootstrap.robot.CurrentTurnSpeed;report.throttle=bootstrap.robot.CurrentThrottleIntent;}
            if(Arms!=null)
            {
                report.armState=Arms.State.ToString();report.deployment=Arms.VisibleDeployment01;
                if(Arms.TryGetRecyclingInlet(out Vector2 inlet,out Vector2 extents))
                {report.inletCenterLocal=inlet;report.inletHalfExtents=extents;}
                if(Marker?.MarkerVisualRoot!=null)
                {report.handCentreLocal=Marker.MarkerVisualRoot.InverseTransformPoint((Arms.LeftHandWorld+Arms.RightHandWorld)*.5f);if(waste!=null)report.wasteLocal=Marker.MarkerVisualRoot.InverseTransformPoint(waste.transform.position);}
                if(waste!=null){report.targetDistanceWorld=Vector2.Distance(waste.transform.position,bootstrap.robot.transform.position);report.handDistanceWorld=Mathf.Min(Vector2.Distance(waste.transform.position,Arms.LeftHandWorld),Vector2.Distance(waste.transform.position,Arms.RightHandWorld));bootstrap.map.TrySampleWorldPosition(waste.transform.position,out Vector2 p,out _);report.targetDistanceMeters=Vector2.Distance(p,report.playerMapPosition);}
            }
            File.WriteAllText(Path.Combine(output,"recycling-play-smoke.json"),JsonUtility.ToJson(report,true));
        }
        private void Finish(Exception error)
        {
            if(phase==Phase.Finishing)return;if(error!=null)report.errors.Add(error.ToString());
            InputSystem.onBeforeUpdate-=Drive;if(waste!=null)waste.Recycled-=OnRecycled;if(fragment!=null)fragment.Recycled-=OnFragmentRecycled;
            if(cameraFollowSuspended&&bootstrap?.cameraFollow!=null)bootstrap.cameraFollow.enabled=true;
            var pad=InputSystem.GetDeviceById(gamepadId);if(pad!=null)InputSystem.RemoveDevice(pad);
            if(originalInput!=null)InputSystem.settings=originalInput;if(testInput!=null)DestroyImmediate(testInput);
            Application.runInBackground=originalBackground;Begin(Phase.Finishing);EditorApplication.isPlaying=false;
        }
        private void Exit()
        {
            if(report.knownSceneHeaders>0&&report.knownMissingPrefabMessages==0)report.errors.Add("Scene-load error lacked the exact known missing-two-Static_Animal diagnostic.");
            report.passed=report.errors.Count==0;Save();running=false;Unsubscribe();EditorApplication.Exit(report.passed?0:1);
        }
    }
}
