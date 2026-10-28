using AnimalGame.Discovery;
using AnimalGame.MapTest;
using AnimalGame.RobotArm;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEngine;
using UnityEngine.Serialization;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AnimalGame.Tutorial
{
    /// <summary>Scene-only discovery practice using real scan gestures, target hits and marker presentation.</summary>
    [DefaultExecutionOrder(1500)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TutorialArmLesson), typeof(TutorialDiscoveryTargets))]
    public sealed class TutorialDiscoveryLesson : MonoBehaviour
    {
        [SerializeField] private HeightMapPlayerSceneBootstrap bootstrap;
        [SerializeField] private TutorialTextContent tutorialText;
        [FormerlySerializedAs("muskratPrefab")]
        [SerializeField] private GameObject animalPrefab;
        [SerializeField] private GameObject smallGarbagePrefab;
        [SerializeField] private Sprite targetSprite;
        [SerializeField, Min(0f)] private float armCompletionDelay = 1.5f;
        [SerializeField, Min(.1f)] private float animalReadingSeconds = 1.2f;
        [SerializeField, HideInInspector, Min(.1f)] private float objectReadingSeconds = 1.5f;
        [SerializeField, Min(.5f)] private float recoveryArrivalRadiusMeters = 4f;

        private readonly DiscoveryLessonText defaultText = new DiscoveryLessonText();
        private readonly TutorialPanelText defaultPanelText = new TutorialPanelText();
        private TutorialArmLesson arms;
        private TutorialDiscoveryTargets targets;
        private TutorialWaypointIndicator recoveryWaypoint;
        private DiscoveryTutorialProgress progress;
        private ScanChargeUI scanInput;
        private BioScanController scanner;
        private BioScanAnimalMarkerUI animalMarkers;
        private BioScanStaticAreaUI objectMarkers;
        private bool hasStarted;
        private bool birdScanned;
        private bool chapterConfirmed;
        private bool ownsPresentation = true;
        private bool interrupted;
        private float completionDelay, refreshRemaining, feedbackRemaining;
        private bool cancelledFeedback;
        private int nativeWaveSequence, modelWaveId, pendingAnimalSequence, pendingObjectSequence;
        private DiscoveryTutorialStep displayedStep = (DiscoveryTutorialStep)(-1);

        public TutorialTextContent TextContent => tutorialText;
        public TutorialDiscoveryTargets Targets => targets;
        public DiscoveryTutorialProgress Progress => progress;
        public TutorialDialoguePanel Panel => arms != null ? arms.Panel : null;
        public bool HasStarted => hasStarted;
        public bool IsComplete => hasStarted && progress != null && progress.IsComplete;
        public bool HasScannedBird => birdScanned;
        public TutorialWaypointIndicator BirdTarget => birdScanned ? recoveryWaypoint : null;
        public void SetPresentationOwned(bool value) => ownsPresentation = value;

        public TutorialWaypointIndicator AcquireBirdTarget()
        {
            EnsureRecoveryWaypoint();
            return recoveryWaypoint;
        }
        private DiscoveryLessonText Text => tutorialText != null && tutorialText.Discovery != null
            ? tutorialText.Discovery : defaultText;

        private void Start()
        {
            if (gameObject.scene.path != TutorialDrivingLesson.ScenePath) { enabled = false; return; }
            arms = GetComponent<TutorialArmLesson>();
            targets = GetComponent<TutorialDiscoveryTargets>();
            if (bootstrap == null)
                foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                {
                    bootstrap = root.GetComponentInChildren<HeightMapPlayerSceneBootstrap>();
                    if (bootstrap != null) break;
                }
        }

        private void LateUpdate()
        {
            if (TutorialTipOverLesson.BlocksMainline(gameObject))
            {
                if (!interrupted) progress?.Suspend();
                interrupted = true;
                if (recoveryWaypoint != null) recoveryWaypoint.SetVisible(false);
                return;
            }
            if (interrupted)
            {
                interrupted = false;
                progress?.Suspend();
                refreshRemaining = 0f;
            }
            if (arms == null || Panel == null || bootstrap == null || targets == null) return;
            if (!arms.IsComplete)
            {
                if (hasStarted) ReleasePresentation();
                completionDelay = 0f;
                return;
            }
            if (!ownsPresentation) return;
            if (targets.Animal != null && targets.Animal.IsPermanentlyDiscovered) birdScanned = true;
            if (progress != null && progress.IsComplete)
            { UpdateRecoveryWaypoint(true, false, false); Panel.SetContinuePrompt(false); Panel.SetVisible(false); return; }
            bool modal = IsModal();
            bool justStarted = false;
            if (!hasStarted)
            {
                if (modal || !CanScan()) return;
                BeginLesson(); justStarted = true;
            }
            BindScanner();
            Panel.SetVisible(!modal);
            if (recoveryWaypoint != null) recoveryWaypoint.SetVisible(!TargetModal());
            if (birdScanned) UpdateRecoveryWaypoint(true, false, false);
            if (modal) { progress.Suspend(); return; }
#if ENABLE_INPUT_SYSTEM
            if (!justStarted && Gamepad.current != null && (Gamepad.current.dpad.left.wasPressedThisFrame
                || Gamepad.current.dpad.right.wasPressedThisFrame)) Panel.ToggleCollapsed();
#endif
            bool ready = progress.IsComplete ? targets.IsReady : targets.EnsureTargets();
            // Charging and releasing are real player actions even while the owned targets initialize.
            // Target-dependent progress still requires a hit and the actual visible marker below.
            bool validInput = CanScan();
            bool valid = ready && validInput;
            bool seen = false, animalVisible = false;
            if (animalMarkers != null) animalMarkers.TryGetScanStatus(targets.Animal, out seen, out animalVisible, out _);
            bool objectVisible = objectMarkers != null && objectMarkers.IsTargetVisible(targets.Item);
            progress.Sample(new DiscoveryTutorialSample(Time.deltaTime, validInput, !Panel.IsCollapsed,
                scanInput != null && scanInput.IsFullyCharged, animalVisible, seen, objectVisible));
            TutorialPanelText continueText = tutorialText != null ? tutorialText.Panel : defaultPanelText;
            Panel.SetContinuePrompt(progress.CanContinue && validInput,
                progress.CanContinue && validInput ? continueText.ContinuePrompt : continueText.ContinueWaitingPrompt);
            if (!justStarted && validInput && progress.CanContinue && TutorialContinueInput.TryConsume(Panel, bootstrap))
            {
                progress.Continue();
                Panel.SetContinuePrompt(false);
                refreshRemaining = 0f;
            }
            if (progress.IsComplete)
            { UpdateRecoveryWaypoint(true, animalVisible, objectVisible); Panel.SetContinuePrompt(false); Panel.SetVisible(false); return; }
            if (progress.Step != displayedStep)
            {
                Panel.SetCollapsed(false);
                refreshRemaining = 0f;
                feedbackRemaining = 0f;
            }
            UpdateRecoveryWaypoint(valid, animalVisible, objectVisible);
            feedbackRemaining = Mathf.Max(0f, feedbackRemaining - Time.deltaTime);
            refreshRemaining -= Time.unscaledDeltaTime;
            if (refreshRemaining <= 0f) { RefreshPanel(validInput); refreshRemaining = .08f; }
        }

        private void BeginLesson()
        {
            progress = new DiscoveryTutorialProgress(animalReadingSeconds, objectReadingSeconds);
            targets.Configure(bootstrap, animalPrefab, smallGarbagePrefab);
            arms.SetPresentationOwned(false);
            hasStarted = true;
            BindScanner();
            // The player can begin holding LB during the previous lesson's completion delay.
            // Adopt that still-active native charge rather than waiting for an event that already fired.
            if (scanInput != null && scanInput.IsCharging) progress.RegisterChargeStarted(CanScan());
            nativeWaveSequence = scanner != null ? scanner.ScanSequence : 0;
            targets.CreateTargets();
            Panel.SetCollapsed(false);
            displayedStep = (DiscoveryTutorialStep)(-1);
            refreshRemaining = 0f;
        }

        private bool IsModal() => Time.deltaTime <= 0f || AnimalGame.MainUI.MainUI.BlocksGameplay
            || (bootstrap.photoMode != null && (bootstrap.photoMode.IsActive || bootstrap.photoMode.IsInputLocked))
            || (bootstrap.photoResultView != null && bootstrap.photoResultView.IsShowing);

        private bool TargetModal() => Time.deltaTime <= 0f || AnimalGame.MainUI.MainUI.BlocksGameplay
            || AnimalGame.AnimalDebug.DebugUI.BlocksOtherPanels
            || (bootstrap.photoResultView != null && bootstrap.photoResultView.IsShowing);

        private bool CanScan()
        {
            RobotMover robot = bootstrap != null ? bootstrap.robot : null;
            return robot != null && robot.isActiveAndEnabled && !robot.IsMovementLocked
                && !robot.IsPhotoModeInputLocked && bootstrap.gameObject.scene == gameObject.scene
                && bootstrap.map != null && bootstrap.map.HasGeneratedMap
                && bootstrap.scanChargeUi != null && bootstrap.scanChargeUi.isActiveAndEnabled
                && bootstrap.bioScan != null && bootstrap.bioScan.isActiveAndEnabled
                && (bootstrap.tumble == null || bootstrap.tumble.State == RobotTumbleState.Upright);
        }

        private bool ValidInputEvent() => !TutorialTipOverLesson.BlocksMainline(gameObject)
            && hasStarted && progress != null && !IsModal() && CanScan();
        // An already released wave may finish during recovery. Preserve its exact target hits,
        // while new charge/release gestures remain owned by the interruption.
        private bool ValidEvent() => hasStarted && progress != null && targets != null && targets.IsReady
            && (TutorialTipOverLesson.BlocksMainline(gameObject) || ValidInputEvent());

        private void BindScanner()
        {
            if (scanInput == bootstrap.scanChargeUi && scanner == bootstrap.bioScan) return;
            Unsubscribe();
            scanInput = bootstrap.scanChargeUi; scanner = bootstrap.bioScan;
            animalMarkers = bootstrap.robot != null ? bootstrap.robot.GetComponent<BioScanAnimalMarkerUI>() : null;
            objectMarkers = bootstrap.robot != null ? bootstrap.robot.GetComponent<BioScanStaticAreaUI>() : null;
            if (scanInput != null)
            {
                scanInput.BiologicalScanChargeStarted += OnChargeStarted;
                scanInput.BiologicalScanChargeCancelled += OnChargeCancelled;
                scanInput.FullyChargedBiologicalScanReleased += OnScanReleased;
                scanInput.TerrainScanRequested += OnTerrainScan;
            }
            if (scanner != null)
            {
                scanner.AnimalDetected += OnAnimalDetected;
                scanner.InteractableDetected += OnObjectDetected;
            }
        }

        private void OnChargeStarted()
        {
            if (TutorialTipOverLesson.BlocksMainline(gameObject)) return;
            progress?.RegisterChargeStarted(ValidInputEvent());
            feedbackRemaining = 0f; refreshRemaining = 0f;
        }

        private void OnChargeCancelled()
        {
            if (TutorialTipOverLesson.BlocksMainline(gameObject)) return;
            bool valid = ValidInputEvent();
            progress?.RegisterChargeCancelled(valid);
            if (valid) { cancelledFeedback = true; feedbackRemaining = 2f; refreshRemaining = 0f; }
        }

        private void OnTerrainScan()
        {
            if (TutorialTipOverLesson.BlocksMainline(gameObject)) return;
            if (!ValidInputEvent()) return;
            cancelledFeedback = false; feedbackRemaining = 2f; refreshRemaining = 0f;
        }

        private void OnScanReleased()
        {
            if (TutorialTipOverLesson.BlocksMainline(gameObject)) return;
            int sequence = scanner != null ? scanner.ScanSequence : 0;
            int accepted = progress != null ? progress.RegisterScanReleased(ValidInputEvent()
                && sequence != nativeWaveSequence) : 0;
            if (accepted == 0) return;
            nativeWaveSequence = sequence; modelWaveId = accepted;
            // The scanner's listener runs first. Initial-radius hits may precede this release callback.
            if (pendingAnimalSequence == sequence) progress.RegisterAnimalHit(accepted, ValidEvent());
            if (pendingObjectSequence == sequence) progress.RegisterObjectHit(accepted, ValidEvent());
            pendingAnimalSequence = pendingObjectSequence = 0;
            refreshRemaining = 0f;
        }

        private void OnAnimalDetected(int sequence, DiscoverableEntity entity)
        {
            if (!hasStarted || targets == null || entity != targets.Animal) return;
            // The native hit has already marked this exact clone as discovered. Its target
            // remains valid even if the player opened another native mode while the wave travelled.
            if (entity.IsPermanentlyDiscovered) birdScanned = true;
            if (sequence == nativeWaveSequence) progress.RegisterAnimalHit(modelWaveId, ValidEvent());
            else pendingAnimalSequence = sequence;
            refreshRemaining = 0f;
        }

        private void OnObjectDetected(int sequence, ScannableInteractable item)
        {
            if (!hasStarted || targets == null || item != targets.Item) return;
            if (sequence == nativeWaveSequence) progress.RegisterObjectHit(modelWaveId, ValidEvent());
            else pendingObjectSequence = sequence;
            refreshRemaining = 0f;
        }

        private void RefreshPanel(bool canScan = true)
        {
            if (Panel == null || progress == null) return;
            if (progress.IsComplete) { Panel.SetContinuePrompt(false); Panel.SetVisible(false); return; }
            DiscoveryLessonText text = Text;
            TutorialStepText step = text.GetStep(progress.Step) ?? defaultText.GetStep(progress.Step);
            bool chargingStep = progress.Step == DiscoveryTutorialStep.Charge;
            float value = chargingStep ? (progress.CanContinue ? 100f : scanInput != null ? scanInput.Charge01 * 100f : 0f) : progress.Value;
            float target = chargingStep ? 100f : progress.Target;
            string lb = "LB", rt = "RT", lt = "LT";
#if ENABLE_INPUT_SYSTEM
            if (Gamepad.current != null && Gamepad.current.layout.Contains("Dual")) { lb = "L1"; rt = "R2"; lt = "L2"; }
#endif
            var tokens = new TutorialTextTokens().Add("value", value).Add("target", target)
                .Add("lb", lb).Add("rt", rt).Add("lt", lt);
            string message = step.Message, action = step.Action, control = step.ControlGlyph, detail = step.Detail;
            string status = text.WaitingStatus;
            bool ready = targets == null || targets.IsReady;
            bool waveInFlight = scanner != null && scanner.HasActiveScanWave && modelWaveId > 0
                && scanner.ScanSequence == nativeWaveSequence;
            bool targetMarkAlive = false;
            if (modelWaveId > 0 && scanner != null && scanner.ScanSequence == nativeWaveSequence && targets != null)
            {
                if ((progress.Step == DiscoveryTutorialStep.FindAnimal || progress.Step == DiscoveryTutorialStep.ConfirmAnimal)
                    && animalMarkers != null && animalMarkers.TryGetScanStatus(targets.Animal, out _, out _, out float remaining))
                    targetMarkAlive = remaining > 0f;
            }
            if (progress.IsComplete) status = text.CompleteStatus;
            else if (!ready) status = text.PreparingStatus;
            else if (!canScan) status = text.BlockedStatus;
            else if (progress.Step == DiscoveryTutorialStep.Release && !progress.IsChargeReady && !progress.HasReleasedScan)
            {
                message = text.Charge.Message; action = text.Charge.Action; control = text.Charge.ControlGlyph;
                detail = text.Charge.Detail; status = text.NeedRescanStatus;
                tokens.Add("value", scanInput != null ? scanInput.Charge01 * 100f : 0f);
            }
            else if (targets != null && targets.UsesFixedAnimalPosition && !targets.IsAnimalWithinScanReach
                && (progress.Step == DiscoveryTutorialStep.FindAnimal || progress.Step == DiscoveryTutorialStep.ConfirmAnimal))
            {
                TutorialStepText approach = text.ApproachAnimal ?? defaultText.ApproachAnimal;
                message = approach.Message; control = approach.ControlGlyph;
                action = approach.Action; detail = approach.Detail;
            }
            else if (progress.NeedsFreshScan && !targetMarkAlive && !waveInFlight && (scanInput == null || !scanInput.IsCharging))
            {
                message = text.NeedRescanMessage; control = text.Charge.ControlGlyph;
                action = text.Charge.Action; status = text.NeedRescanStatus;
            }
            if (!progress.IsComplete && canScan && feedbackRemaining > 0f)
                status = cancelledFeedback ? text.CancelledStatus : text.WrongGestureStatus;
            Panel.SetPanelText(tutorialText != null ? tutorialText.Panel : defaultPanelText);
            Panel.SetLessonHint(progress.IsComplete ? string.Empty : tokens.Format(text.ToggleHint));
            Panel.SetContent(tokens.Format(text.Title), tokens.Format(message), tokens.Format(control),
                tokens.Format(action), tokens.Format(detail), tokens.Format(status), 5, 9);
            Panel.SetProgress(chargingStep ? value / 100f : progress.NormalizedProgress);
            Panel.SetSuccess(progress.IsComplete);
            TutorialPanelText panelText = tutorialText != null ? tutorialText.Panel : defaultPanelText;
            Panel.SetContinuePrompt(progress.CanContinue && canScan,
                progress.CanContinue && canScan ? panelText.ContinuePrompt : panelText.ContinueWaitingPrompt);
            displayedStep = progress.Step;
        }

        private void UpdateRecoveryWaypoint(bool valid, bool animalVisible, bool objectVisible)
        {
            // Keep the actual scanned bird highlighted even when it is already visible/nearby,
            // and through the handoff into photography.
            if (birdScanned && targets != null && (targets.Animal != null || targets.AnimalActor != null))
            {
                EnsureRecoveryWaypoint();
                recoveryWaypoint.SetPhotoModeVisible(true);
                recoveryWaypoint.SetVisible(!TargetModal());
                recoveryWaypoint.SetTarget(targets.AnimalMapPosition);
                return;
            }
            bool toAnimal = (progress.Step == DiscoveryTutorialStep.FindAnimal
                || progress.Step == DiscoveryTutorialStep.ConfirmAnimal) && !animalVisible;
            if (!valid || progress.IsComplete || !toAnimal)
            { if (recoveryWaypoint != null) recoveryWaypoint.ClearTarget(); return; }
            EnsureRecoveryWaypoint();
            Vector2 animalTarget = targets.UsesFixedAnimalPosition && !targets.IsAnimalWithinScanReach
                ? targets.AnimalObservationMapPosition : targets.AnimalMapPosition;
            Vector2 target = animalTarget;
            if (bootstrap.map.TrySampleWorldPosition(bootstrap.robot.transform.position, out Vector2 player, out _)
                && Vector2.Distance(player, target) <= recoveryArrivalRadiusMeters)
            { recoveryWaypoint.ClearTarget(); return; }
            recoveryWaypoint.SetTarget(target);
        }

        private void EnsureRecoveryWaypoint()
        {
            if (recoveryWaypoint != null) return;
            var child = new GameObject("Discovery Target Reminder"); child.transform.SetParent(transform, false);
            recoveryWaypoint = child.AddComponent<TutorialWaypointIndicator>();
            recoveryWaypoint.Configure(bootstrap, targetSprite);
        }

        [ContextMenu("Restart Discovery Lesson")]
        public void RestartLesson()
        {
            if (!hasStarted || progress == null) return;
            progress.Reset(); targets.ResetTargets();
            birdScanned = false;
            if (recoveryWaypoint != null) recoveryWaypoint.ClearTarget();
            nativeWaveSequence = scanner != null ? scanner.ScanSequence : 0;
            modelWaveId = pendingAnimalSequence = pendingObjectSequence = 0;
            Panel.SetCollapsed(false); refreshRemaining = 0f;
        }

        private void Unsubscribe()
        {
            if (scanInput != null)
            {
                scanInput.BiologicalScanChargeStarted -= OnChargeStarted;
                scanInput.BiologicalScanChargeCancelled -= OnChargeCancelled;
                scanInput.FullyChargedBiologicalScanReleased -= OnScanReleased;
                scanInput.TerrainScanRequested -= OnTerrainScan;
            }
            if (scanner != null)
            {
                scanner.AnimalDetected -= OnAnimalDetected;
                scanner.InteractableDetected -= OnObjectDetected;
            }
            scanInput = null; scanner = null;
        }

        private void ReleasePresentation()
        {
            Unsubscribe();
            if (recoveryWaypoint != null) recoveryWaypoint.ClearTarget();
            if (targets != null) targets.Cleanup();
            if (Panel != null) Panel.SetLessonHint(string.Empty);
            hasStarted = false; progress = null;
            birdScanned = false;
            ownsPresentation = true;
            if (arms != null) arms.SetPresentationOwned(true);
        }

        private void OnDisable() { if (hasStarted) ReleasePresentation(); }
    }
}
