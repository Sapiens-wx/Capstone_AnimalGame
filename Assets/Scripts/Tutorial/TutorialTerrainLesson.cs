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
        [SerializeField, Min(.5f)] private float arrivalRadiusMeters = 2f;
        [SerializeField, Min(1f)] private float surveyRadiusMeters = 6f;
        [SerializeField, Min(1f)] private float signsReadingSeconds = 4f;
        [SerializeField, Min(0f)] private float drivingCompletionDelay = 2f;

        private TutorialDrivingLesson driving;
        private TerrainTutorialProgress progress;
        private TutorialWaypointIndicator waypoint;
        private ScanChargeUI subscribedScan;
        private float completionDelay, refreshRemaining, lastAcceptedScanAt;
        private bool hasStarted;
        private TerrainTutorialStep displayedStep;
        private int displayedApproachIndex = -1;
        private Vector2 displayedTarget;
        private bool showingTarget;
        private bool ownsPresentation = true;
        private readonly TerrainLessonText defaultText = new TerrainLessonText();
        private readonly TutorialPanelText defaultPanelText = new TutorialPanelText();

        public TutorialTextContent TextContent => tutorialText;
        public TerrainTutorialProgress Progress => progress;
        public TutorialWaypointIndicator Waypoint => waypoint;
        public bool HasStarted => hasStarted;
        public bool IsComplete => hasStarted && progress != null && progress.IsComplete;

        public void SetPresentationOwned(bool value) => ownsPresentation = value;

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
            if (driving == null || driving.Panel == null || bootstrap == null) return;
            if (!driving.IsComplete)
            {
                if (hasStarted) ReleasePresentation();
                completionDelay = 0f;
                return;
            }
            if (!ownsPresentation) return;
            bool modal = IsModal();
            if (!hasStarted)
            {
                if (modal || !CanDrive()) return;
                completionDelay += Time.deltaTime;
                if (completionDelay < drivingCompletionDelay) return;
                BeginLesson();
            }
            EnsureScanSubscription();
            TutorialDialoguePanel panel = driving.Panel;
            panel.SetVisible(!modal);
            waypoint.SetVisible(!modal);
            if (modal) return;
#if ENABLE_INPUT_SYSTEM
            if (!AnimalGame.AnimalDebug.AnimalDebugUI.BlocksOtherPanels && Gamepad.current != null && (Gamepad.current.dpad.left.wasPressedThisFrame
                || Gamepad.current.dpad.right.wasPressedThisFrame))
                panel.ToggleCollapsed();
#endif
            bool onMap = TryGetPosition(out Vector2 position);
            bool valid = CanDrive() && onMap;
            bool scanReady = bootstrap.scanOverlay != null && bootstrap.scanOverlay.HasActiveSnapshot
                && bootstrap.scanOverlay.VisibleMarkerCount > 0;
            // Requests arrive before the asynchronous scan has produced its first markers.
            if (progress.Step == TerrainTutorialStep.ReadSigns && !scanReady
                && Time.unscaledTime - lastAcceptedScanAt > 2f) progress.RequireFreshScan();
            float metersPerWorld = bootstrap.map != null && bootstrap.robot != null
                ? 1f / Mathf.Max(.0001f, bootstrap.map.MapMetersToWorldDistance(bootstrap.robot.Forward, 1f)) : 1f;
            bool stopped = bootstrap.robot != null && Mathf.Abs(bootstrap.robot.CurrentSpeed) * metersPerWorld <= .2f
                && bootstrap.robot.CurrentTerrainVelocity.magnitude * metersPerWorld <= .2f
                && Mathf.Abs(bootstrap.robot.CurrentTurnSpeed) <= 2f
                && Mathf.Abs(bootstrap.robot.CurrentTerrainTurnSpeed) <= 2f;
            progress.Sample(position, Time.deltaTime, valid, !panel.IsCollapsed, scanReady, stopped);
            if (progress.Step != displayedStep || progress.ApproachIndex != displayedApproachIndex)
            {
                panel.SetCollapsed(false);
                refreshRemaining = 0f;
            }
            UpdateTarget();
            refreshRemaining -= Time.unscaledDeltaTime;
            if (refreshRemaining <= 0f) { RefreshPanel(valid, scanReady, stopped); refreshRemaining = .08f; }
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
            if (!hasStarted || !isActiveAndEnabled || IsModal() || !CanDrive() || !TryGetPosition(out Vector2 position)) return;
            if (progress.RegisterTerrainScan(position, true))
            {
                lastAcceptedScanAt = Time.unscaledTime;
                refreshRemaining = 0f;
            }
        }

        private void UpdateTarget()
        {
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
            return robot != null && robot.isActiveAndEnabled && !robot.IsMovementLocked && !robot.IsArmInputCaptured
                && !robot.IsPhotoModeInputLocked && !robot.IsLevelThreeUnstable && !robot.IsAutoAligningDownhill
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
                    status = !scanReady ? text.WaitingMarkersStatus : !stopped ? text.StopToReadStatus : text.ObserveStatus;
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
            var tokens = new TutorialTextTokens().Add("rt", rt).Add("lt", lt).Add("lb", lb)
                .Add("distance", progress.DistanceMeters).Add("slope", slope)
                .Add("reading", progress.ReadingSeconds).Add("readingTarget", progress.ReadingDuration);
            driving.Panel.SetPanelText(tutorialText != null ? tutorialText.Panel : defaultPanelText);
            driving.Panel.SetLessonHint(string.Empty);
            driving.Panel.SetContent(tokens.Format(text.Title), tokens.Format(stepText.Message),
                tokens.Format(stepText.ControlGlyph), tokens.Format(stepText.Action),
                tokens.Format(stepText.Detail), tokens.Format(status), 2, 8);
            driving.Panel.SetProgress(progress.NormalizedProgress);
            driving.Panel.SetSuccess(progress.IsComplete);
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
