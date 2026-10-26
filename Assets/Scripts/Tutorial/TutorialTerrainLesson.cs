using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AnimalGame.Tutorial
{
    /// <summary>Opt-in chapter two; observes existing terrain and scan systems in the tutorial scene.</summary>
    [DefaultExecutionOrder(1100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TutorialDrivingLesson))]
    public sealed class TutorialTerrainLesson : MonoBehaviour
    {
        [SerializeField] private HeightMapPlayerSceneBootstrap bootstrap;
        [Tooltip("教程文案资源。选中资源或点击下方按钮，可在 Inspector 编辑每一步文字。")]
        [SerializeField] private TutorialTextContent tutorialText;
        [SerializeField] private Sprite targetSprite;
        [Tooltip("Logical map metres. The last approach point is the terrain survey area.")]
        [SerializeField] private Vector2[] approachPointsMeters = { new Vector2(163f, 140f) };
        [SerializeField] private Vector2 gentleRouteDestinationMeters = new Vector2(146f, 121f);
        [SerializeField, Min(.5f)] private float arrivalRadiusMeters = 4f;
        [SerializeField, Min(1f)] private float surveyRadiusMeters = 8f;
        [SerializeField, Min(1f)] private float signsReadingSeconds = 4f;
        [SerializeField, Min(0f)] private float drivingCompletionDelay = 2f;

        private TutorialDrivingLesson driving;
        private TerrainTutorialProgress progress;
        private TutorialWaypointIndicator waypoint;
        private ScanChargeUI subscribedScan;
        private float completionDelay, refreshRemaining;
        private bool hasStarted;
        private TerrainTutorialStep displayedStep;
        private int displayedApproachIndex = -1;
        private Vector2 displayedTarget;
        private bool showingTarget;
        private bool ownsPresentation = true;
        private bool chapterConfirmed;
        private bool interrupted;
        private readonly TerrainLessonText defaultText = new TerrainLessonText();
        private readonly TutorialPanelText defaultPanelText = new TutorialPanelText();

        public TutorialTextContent TextContent => tutorialText;
        public TerrainTutorialProgress Progress => progress;
        public TutorialWaypointIndicator Waypoint => waypoint;
        public bool HasStarted => hasStarted;
        public bool IsComplete => hasStarted && progress != null && progress.IsComplete;

        public void SetPresentationOwned(bool value)
        {
            ownsPresentation = value;
            if (!value && progress != null && progress.IsComplete) UpdateTarget();
        }

        private void Start()
        {
            if (gameObject.scene.path != TutorialDrivingLesson.ScenePath) { enabled = false; return; }
            driving = GetComponent<TutorialDrivingLesson>();
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
                interrupted = true;
                if (waypoint != null) waypoint.SetVisible(false);
                return;
            }
            if (interrupted) { interrupted = false; refreshRemaining = 0f; }
            if (driving == null || driving.Panel == null || bootstrap == null) return;
            if (!driving.IsComplete)
            {
                if (hasStarted) ReleasePresentation();
                completionDelay = 0f;
                return;
            }
            if (!ownsPresentation) return;
            if (progress != null && progress.IsComplete)
            { UpdateTarget(); driving.Panel.SetContinuePrompt(false); driving.Panel.SetVisible(false); return; }
            bool modal = IsModal();
            if (!hasStarted)
            {
                if (modal || !CanDrive()) return;
                BeginLesson();
            }
            EnsureScanSubscription();
            TutorialDialoguePanel panel = driving.Panel;
            panel.SetVisible(!modal);
            waypoint.SetVisible(!modal);
            if (modal) { panel.SetContinuePrompt(false); return; }
#if ENABLE_INPUT_SYSTEM
            if (!AnimalGame.AnimalDebug.DebugUI.BlocksOtherPanels && Gamepad.current != null && (Gamepad.current.dpad.left.wasPressedThisFrame
                || Gamepad.current.dpad.right.wasPressedThisFrame))
                panel.ToggleCollapsed();
#endif
            bool onMap = TryGetPosition(out Vector2 position);
            bool valid = CanDrive() && onMap;
            bool scanReady = bootstrap.scanOverlay != null && bootstrap.scanOverlay.HasActiveSnapshot
                && bootstrap.scanOverlay.VisibleMarkerCount > 0;
            progress.Sample(position, Time.deltaTime, valid, !panel.IsCollapsed, scanReady, true);
            panel.SetContinuePrompt(valid && progress.CanContinue,
                (tutorialText != null ? tutorialText.Panel : defaultPanelText).ContinuePrompt);
            if (TutorialContinueInput.TryConsume(panel, bootstrap))
            {
                progress.Continue();
                panel.SetContinuePrompt(false);
                refreshRemaining = 0f;
            }
            if (progress.IsComplete) { UpdateTarget(); panel.SetContinuePrompt(false); panel.SetVisible(false); return; }
            if (progress.Step != displayedStep || progress.ApproachIndex != displayedApproachIndex)
            {
                panel.SetCollapsed(false);
                refreshRemaining = 0f;
            }
            UpdateTarget();
            refreshRemaining -= Time.unscaledDeltaTime;
            if (refreshRemaining <= 0f) { RefreshPanel(valid, scanReady); refreshRemaining = .08f; }
        }

        private void BeginLesson()
        {
            progress = new TerrainTutorialProgress(approachPointsMeters, gentleRouteDestinationMeters,
                arrivalRadiusMeters, surveyRadiusMeters, signsReadingSeconds);
            driving.SetPresentationOwned(false);
            if (waypoint == null)
            {
                var child = new GameObject("Terrain Tutorial Destination");
                child.transform.SetParent(transform, false);
                waypoint = child.AddComponent<TutorialWaypointIndicator>();
            }
            waypoint.Configure(bootstrap, targetSprite);
            driving.Panel.SetCollapsed(false);
            driving.Panel.SetContinuePrompt(false);
            hasStarted = true;
            if (TryGetPosition(out Vector2 position))
                progress.Sample(position, Time.deltaTime, CanDrive(), true, false, false);
            EnsureScanSubscription();
            UpdateTarget();
            RefreshPanel();
        }

        private void EnsureScanSubscription()
        {
            if (subscribedScan == bootstrap.scanChargeUi) return;
            Unsubscribe();
            subscribedScan = bootstrap.scanChargeUi;
            if (subscribedScan != null) subscribedScan.TerrainScanRequested += OnTerrainScan;
        }

        private void OnTerrainScan()
        {
            if (TutorialTipOverLesson.BlocksMainline(gameObject)) return;
            if (!hasStarted || !isActiveAndEnabled || IsModal() || !CanDrive() || !TryGetPosition(out Vector2 position)) return;
            if (progress.RegisterTerrainScan(position, true))
            {
                refreshRemaining = 0f;
            }
        }

        private void UpdateTarget()
        {
            if (waypoint == null) { showingTarget = false; return; }
            if (!progress.HasTarget)
            {
                if (showingTarget) waypoint.ClearTarget();
                showingTarget = false;
            }
            else if (!showingTarget || displayedTarget != progress.CurrentTarget)
            {
                waypoint.SetTarget(progress.CurrentTarget);
                displayedTarget = progress.CurrentTarget;
                showingTarget = true;
            }
        }

        private bool IsModal() => Time.deltaTime <= 0f || AnimalGame.MainUI.MainUI.BlocksGameplay
            || (bootstrap.photoMode != null && (bootstrap.photoMode.IsActive || bootstrap.photoMode.IsInputLocked))
            || (bootstrap.photoResultView != null && bootstrap.photoResultView.IsShowing);

        private bool CanDrive()
        {
            RobotMover robot = bootstrap != null ? bootstrap.robot : null;
            return robot != null && robot.isActiveAndEnabled && !robot.IsMovementLocked
                && !robot.IsPhotoModeInputLocked
                && bootstrap.map != null && bootstrap.map.HasGeneratedMap
                && (bootstrap.tumble == null || bootstrap.tumble.State == RobotTumbleState.Upright);
        }

        private bool TryGetPosition(out Vector2 position)
        {
            position = default;
            return bootstrap != null && bootstrap.map != null && bootstrap.robot != null
                && bootstrap.map.TrySampleWorldPosition(bootstrap.robot.transform.position, out position, out _);
        }

        private void RefreshPanel(bool canDrive = true, bool scanReady = false, bool stopped = true)
        {
            if (progress == null || driving == null || driving.Panel == null) return;
            if (progress.IsComplete)
            { UpdateTarget(); driving.Panel.SetContinuePrompt(false); driving.Panel.SetVisible(false); return; }
            TerrainLessonText text = tutorialText != null && tutorialText.Terrain != null ? tutorialText.Terrain : defaultText;
            TutorialStepText stepText = text.GetStep(progress.Step) ?? defaultText.GetStep(progress.Step);
            string lb = "LB", rt = "RT", lt = "LT";
#if ENABLE_INPUT_SYSTEM
            if (Gamepad.current != null && Gamepad.current.layout.Contains("Dual"))
            { lb = "L1"; rt = "R2"; lt = "L2"; }
#endif
            string status = text.FollowMarkerStatus;
            float slope = 0f;
            switch (progress.Step)
            {
                case TerrainTutorialStep.ScanTerrain:
                    status = text.WaitingScanStatus;
                    break;
                case TerrainTutorialStep.ReadSigns:
                    status = !scanReady && !progress.HasAcknowledgedScan ? text.WaitingMarkersStatus : text.ObserveStatus;
                    break;
                case TerrainTutorialStep.FollowRoute:
                    slope = bootstrap != null && bootstrap.traversalEvaluator != null && bootstrap.robot != null
                        ? bootstrap.traversalEvaluator.EvaluateLocalSurface(bootstrap.robot.transform.position).MaximumSurfaceSlopeAngle : 0f;
                    break;
                case TerrainTutorialStep.Completed:
                    status = text.CompleteStatus;
                    break;
            }
            if (!canDrive && !progress.IsComplete) status = text.BlockedStatus;
            string message = stepText.Message, glyph = stepText.ControlGlyph, action = stepText.Action, detail = stepText.Detail;
            var tokens = new TutorialTextTokens().Add("rt", rt).Add("lt", lt).Add("lb", lb)
                .Add("distance", progress.DistanceMeters).Add("slope", slope)
                .Add("reading", progress.ReadingSeconds).Add("readingTarget", progress.ReadingDuration);
            driving.Panel.SetPanelText(tutorialText != null ? tutorialText.Panel : defaultPanelText);
            driving.Panel.SetLessonHint(string.Empty);
            driving.Panel.SetContent(tokens.Format(text.Title), tokens.Format(message),
                tokens.Format(glyph), tokens.Format(action),
                tokens.Format(detail), tokens.Format(status), 2, 9);
            driving.Panel.SetProgress(progress.NormalizedProgress);
            driving.Panel.SetSuccess(progress.CanContinue);
            displayedStep = progress.Step;
            displayedApproachIndex = progress.ApproachIndex;
        }

        private void Unsubscribe()
        {
            if (subscribedScan != null) subscribedScan.TerrainScanRequested -= OnTerrainScan;
            subscribedScan = null;
        }

        private void ReleasePresentation()
        {
            Unsubscribe();
            if (waypoint != null) waypoint.ClearTarget();
            showingTarget = false;
            driving?.Panel?.SetContinuePrompt(false);
            hasStarted = false;
            ownsPresentation = true;
            progress = null;
            if (driving != null) driving.SetPresentationOwned(true);
        }

        private void OnDisable()
        {
            if (!hasStarted) return;
            ReleasePresentation();
        }

        private void OnDestroy() => Unsubscribe();
    }
}
