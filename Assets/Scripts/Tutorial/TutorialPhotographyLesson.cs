using AnimalGame.Animals;
using AnimalGame.MapTest;
using AnimalGame.RobotArm;
using AnimalGame.RobotMap;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AnimalGame.Tutorial
{
    /// <summary>Observes the real camera, exact teaching subject, result and album save in this scene only.</summary>
    [DefaultExecutionOrder(1600)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TutorialDiscoveryLesson))]
    public sealed class TutorialPhotographyLesson : MonoBehaviour
    {
        [SerializeField] private HeightMapPlayerSceneBootstrap bootstrap;
        [SerializeField] private TutorialTextContent tutorialText;
        [SerializeField] private Sprite targetSprite;
        [SerializeField, Min(0f)] private float discoveryCompletionDelay = 1.5f;
        [SerializeField, Min(.1f)] private float observationSeconds = .8f;
        [SerializeField, Min(.1f)] private float framingSeconds = .5f;
        [SerializeField, Min(.1f)] private float requiredMovementMeters = 2f;

        private readonly PhotographyLessonText defaultText = new PhotographyLessonText();
        private readonly TutorialPanelText defaultPanelText = new TutorialPanelText();
        private TutorialDiscoveryLesson discovery;
        private TutorialDiscoveryTargets targets;
        private PhotographyTutorialProgress progress;
        private TutorialWaypointIndicator waypoint;
        private PhotoResultUI results;
        private PhotoModeController camera;
        private BioScanAnimalMarkerUI markers;
        private AnimalPhotoSubject subject;
        private PhotoResultSnapshot acceptedSnapshot;
        private long resultSequence, acceptedResultId;
        private bool acceptedSaved, hasStarted, wasResultVisible, retryFeedback, shutterPending;
        private float completionDelay, refreshRemaining;
        private PhotographyTutorialStep displayedStep = (PhotographyTutorialStep)(-1);

        public TutorialTextContent TextContent => tutorialText;
        public TutorialDiscoveryTargets Targets => targets;
        public PhotographyTutorialProgress Progress => progress;
        public TutorialDialoguePanel Panel => discovery != null ? discovery.Panel : null;
        public bool HasStarted => hasStarted;
        public bool IsComplete => hasStarted && progress != null && progress.IsComplete;
        private PhotographyLessonText Text => tutorialText != null && tutorialText.Photography != null
            ? tutorialText.Photography : defaultText;

        private void Start()
        {
            if (gameObject.scene.path != TutorialDrivingLesson.ScenePath) { enabled = false; return; }
            discovery = GetComponent<TutorialDiscoveryLesson>(); targets = GetComponent<TutorialDiscoveryTargets>();
            if (bootstrap == null)
                foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                { bootstrap = root.GetComponentInChildren<HeightMapPlayerSceneBootstrap>(); if (bootstrap != null) break; }
        }

        private void LateUpdate()
        {
            if (discovery == null || targets == null || Panel == null || bootstrap == null) return;
            BindCamera();
            if (!discovery.IsComplete)
            {
                if (hasStarted) ReleasePresentation();
                completionDelay = 0f; return;
            }
            bool modal = IsModal();
            bool justStarted = false;
            if (!hasStarted)
            {
                if (modal || !CanPractice()) return;
                completionDelay += Time.deltaTime;
                if (completionDelay < discoveryCompletionDelay) return;
                BeginLesson(); justStarted = true;
            }
            bool resultVisible = ResultVisible();
            UpdatePanelVisibility(modal, resultVisible);
            if (modal) { progress.Suspend(); return; }
            if (!resultVisible && !progress.HasSavedPhoto && !progress.IsComplete && !targets.IsAnimalReady)
            { targets.EnsureTargets(); BindCamera(); }
            bool valid = CanPractice();
            if (acceptedSnapshot != null && (acceptedSaved || (results != null && results.DisplayedResult == acceptedSnapshot)))
            {
                progress.RegisterValidPhoto(acceptedResultId, valid);
                if (acceptedSaved) progress.RegisterPhotoSaved(acceptedResultId, valid);
            }
            bool seen = false;
            if (markers != null) markers.TryGetScanStatus(targets.Animal, out seen, out _, out _);
            bool bodyVisible = markers != null && markers.IsAnimalBodyVisible(targets.Animal);
            bool photoActive = camera != null && camera.IsActive && !camera.IsEntering && !camera.IsExiting;
            bool framed = photoActive && results != null && results.IsSubjectInCaptureFrame(subject);
            if (wasResultVisible && !resultVisible && progress.ActiveResultId > 0)
            {
                progress.RegisterResultClosed(progress.ActiveResultId, valid);
                if (!acceptedSaved) { retryFeedback = true; acceptedSnapshot = null; acceptedResultId = 0; }
            }
            wasResultVisible = resultVisible;
            if (shutterPending && camera != null && !camera.IsCapturing && !camera.IsReviewing && !resultVisible)
            { shutterPending = false; retryFeedback = true; }
            bool cameraTransition = camera != null && (camera.IsEntering || camera.IsExiting);
            progress.Sample(new PhotographyTutorialSample(Time.deltaTime, valid && !cameraTransition, !Panel.IsCollapsed,
                camera != null && camera.IsActive, resultVisible, bodyVisible, seen,
                framed, targets.AnimalTravelledMeters));
#if ENABLE_INPUT_SYSTEM
            if (!justStarted && !resultVisible && Gamepad.current != null
                && (Gamepad.current.dpad.left.wasPressedThisFrame || Gamepad.current.dpad.right.wasPressedThisFrame))
                Panel.ToggleCollapsed();
#endif
            if (progress.Step != displayedStep)
            { Panel.SetCollapsed(false); refreshRemaining = 0f; }
            if (!resultVisible && !progress.IsComplete && !bodyVisible && !photoActive) UpdateWaypoint();
            else if (waypoint != null) waypoint.ClearTarget();
            refreshRemaining -= Time.unscaledDeltaTime;
            if (refreshRemaining <= 0f) { RefreshPanel(valid); refreshRemaining = .08f; }
        }

        private void BeginLesson()
        {
            progress = new PhotographyTutorialProgress(observationSeconds, framingSeconds, requiredMovementMeters);
            hasStarted = true; discovery.SetPresentationOwned(false);
            Panel.SetPhotoModeVisible(true); Panel.SetCollapsed(false);
            if (acceptedSnapshot != null && (acceptedSaved || (results != null && results.DisplayedResult == acceptedSnapshot)))
            {
                progress.RegisterValidPhoto(acceptedResultId, true);
                if (acceptedSaved) progress.RegisterPhotoSaved(acceptedResultId, true);
            }
            wasResultVisible = ResultVisible(); refreshRemaining = 0f;
        }

        private bool IsModal() => Time.deltaTime <= 0f || AnimalGame.MainUI.MainUI.BlocksGameplay;
        private bool CanPractice()
        {
            if (bootstrap == null || bootstrap.robot == null || camera == null || !camera.isActiveAndEnabled
                || !bootstrap.robot.isActiveAndEnabled || bootstrap.robot.IsMovementLocked
                || bootstrap.robot.IsLevelThreeUnstable || bootstrap.robot.IsAutoAligningDownhill
                || (bootstrap.tumble != null && bootstrap.tumble.State != RobotTumbleState.Upright)) return false;
            RobotArmController arm = bootstrap.armController;
            // Entering photo mode cancels the arms itself, including an active grip.
            return arm != null && arm.isActiveAndEnabled
                && ((progress != null && progress.HasSavedPhoto) || (targets != null && targets.IsAnimalReady));
        }

        private bool ResultVisible() => (bootstrap != null && bootstrap.photoResultView != null && bootstrap.photoResultView.IsShowing)
            || (results != null && results.IsResultVisible)
            || (camera != null && (camera.IsReviewing || camera.IsCapturing));

        private void UpdatePanelVisibility(bool modal, bool resultVisible)
        {
            if (Panel == null) return;
            Panel.SetPhotoModeVisible(true);
            Panel.SetVisible(!modal && !resultVisible);
            if (waypoint != null) waypoint.SetVisible(!modal && !resultVisible);
        }

        private void BindCamera()
        {
            PhotoResultUI nextResults = bootstrap.photoResultUi;
            PhotoModeController nextCamera = bootstrap.photoMode;
            if (results != nextResults || camera != nextCamera)
            {
                Unsubscribe(); results = nextResults; camera = nextCamera;
                if (results != null) { results.ResultShown += OnResultShown; results.PhotoSaved += OnPhotoSaved; }
                if (camera != null) camera.PhotoCaptured += OnShutter;
            }
            markers = bootstrap.robot != null ? bootstrap.robot.GetComponent<BioScanAnimalMarkerUI>() : null;
            AnimalPhotoSubject next = targets.Animal != null ? targets.Animal.GetComponent<AnimalPhotoSubject>() : null;
            if (!ReferenceEquals(subject, null) && !ReferenceEquals(subject, next) && !acceptedSaved)
            {
                acceptedSnapshot = null; acceptedSaved = false; acceptedResultId = 0;
                if (progress != null && !progress.IsComplete) progress.Reset(resultSequence);
            }
            subject = next;
        }

        private void OnShutter()
        {
            if (!hasStarted) return;
            shutterPending = true; retryFeedback = false;
            if (Panel != null) Panel.SetVisible(false);
        }

        private void OnResultShown(AnimalPhotoSubject captured, PhotoResultSnapshot snapshot)
        {
            if (hasStarted && Panel != null) Panel.SetVisible(false);
            shutterPending = false;
            if (captured == null || captured != subject || snapshot == null)
            { retryFeedback = true; return; }
            acceptedSnapshot = snapshot; acceptedResultId = ++resultSequence;
            acceptedSaved = false; retryFeedback = false;
            if (hasStarted && !IsModal()) progress.RegisterValidPhoto(acceptedResultId, true);
            refreshRemaining = 0f;
        }

        private void OnPhotoSaved(AnimalPhotoSubject captured, PhotoResultSnapshot snapshot)
        {
            if (captured != subject || snapshot == null || snapshot != acceptedSnapshot) return;
            acceptedSaved = true;
            if (hasStarted && !IsModal()) progress.RegisterPhotoSaved(acceptedResultId, true);
            refreshRemaining = 0f;
        }

        private void RefreshPanel(bool valid = true)
        {
            if (Panel == null || progress == null) return;
            PhotographyLessonText text = Text;
            TutorialStepText item = text.GetStep(progress.Step) ?? defaultText.GetStep(progress.Step);
            string photo = "X", rb = "RB", save = "Y", cancel = "B", rt = "RT", lt = "LT", lb = "LB";
#if ENABLE_INPUT_SYSTEM
            if (Gamepad.current != null && Gamepad.current.layout.Contains("Dual"))
            { photo = "□"; rb = "R1"; save = "△"; cancel = "○"; rt = "R2"; lt = "L2"; lb = "L1"; }
#endif
            var tokens = new TutorialTextTokens().Add("photo", photo).Add("rb", rb).Add("save", save)
                .Add("cancel", cancel).Add("rt", rt).Add("lt", lt).Add("lb", lb)
                .Add("value", progress.Value).Add("target", progress.Target);
            string message = item.Message, action = item.Action, glyph = item.ControlGlyph, detail = item.Detail;
            string status = progress.HasSavedPhoto ? text.SavedStatus : text.WaitingStatus;
            if (progress.IsComplete) status = text.CompleteStatus;
            else if (!valid) status = text.BlockedStatus;
            else if (targets != null && targets.Animal == null) { message = text.TargetMissingMessage; status = text.TargetMissingStatus; }
            else if (targets != null && targets.Animal != null && !targets.Animal.IsPermanentlyDiscovered)
            { message = text.NeedRescanMessage; glyph = "{lb}"; status = text.NeedRescanStatus; }
            else if (retryFeedback && !progress.HasSavedPhoto) { message = text.RetryPhotoMessage; status = text.RetryPhotoStatus; }
            Panel.SetPanelText(tutorialText != null ? tutorialText.Panel : defaultPanelText);
            Panel.SetLessonHint(progress.IsComplete ? string.Empty : tokens.Format(text.ToggleHint));
            Panel.SetContent(tokens.Format(text.Title), tokens.Format(message), tokens.Format(glyph),
                tokens.Format(action), tokens.Format(detail), tokens.Format(status), 5, 8);
            Panel.SetProgress(progress.NormalizedProgress); Panel.SetSuccess(progress.IsComplete);
            displayedStep = progress.Step;
        }

        private void UpdateWaypoint()
        {
            if (targets.Animal == null) return;
            if (waypoint == null)
            {
                var child = new GameObject("Photography Target Reminder"); child.transform.SetParent(transform, false);
                waypoint = child.AddComponent<TutorialWaypointIndicator>(); waypoint.Configure(bootstrap, targetSprite);
            }
            waypoint.SetTarget(targets.IsAnimalWithinScanReach ? targets.AnimalMapPosition : targets.AnimalObservationMapPosition);
        }

        [ContextMenu("Restart Photography Lesson")]
        public void RestartLesson()
        {
            if (progress == null) return;
            progress.Reset(resultSequence); acceptedSnapshot = null; acceptedSaved = false; acceptedResultId = 0;
            retryFeedback = shutterPending = false; Panel.SetCollapsed(false); refreshRemaining = 0f;
        }

        private void Unsubscribe()
        {
            if (results != null) { results.ResultShown -= OnResultShown; results.PhotoSaved -= OnPhotoSaved; }
            if (camera != null) camera.PhotoCaptured -= OnShutter;
            results = null; camera = null;
        }

        private void ReleasePresentation()
        {
            if (waypoint != null) waypoint.ClearTarget();
            if (Panel != null) { Panel.SetPhotoModeVisible(false); Panel.SetLessonHint(string.Empty); }
            if (discovery != null) discovery.SetPresentationOwned(true);
            hasStarted = false; progress = null;
        }
        private void OnDisable() { if (hasStarted) ReleasePresentation(); Unsubscribe(); }
    }
}
