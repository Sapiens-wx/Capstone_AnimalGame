using System;
using AnimalGame.Animals;
using AnimalGame.Demos.CheckpointRefit;
using AnimalGame.Demos.WasteRobot;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AnimalGame.Tutorial
{
    /// <summary>Scene-owned guidance over the actual machine doorway, assembly UI and camera equipment.</summary>
    [DefaultExecutionOrder(1700)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TutorialRecyclingLesson))]
    public sealed class TutorialRefitLesson : MonoBehaviour
    {
        [SerializeField] private HeightMapPlayerSceneBootstrap bootstrap;
        [SerializeField] private TutorialTextContent tutorialText;
        [SerializeField] private WasteRobotBase machine;
        [SerializeField] private CheckpointRefitTerminal terminal;
        [SerializeField] private PhotoCameraDefinition targetCamera;
        [SerializeField] private Sprite targetSprite;
        [Tooltip("Machine portal coordinates: positive Y is outside, negative Y is inside.")]
        [SerializeField] private Vector2 entranceApproachLocal = new Vector2(0f, 1.2f);
        [SerializeField] private Vector2 entranceInteriorLocal = new Vector2(0f, -.8f);
        [SerializeField, Min(.1f)] private float arrivalRadiusMeters = 3f;
        [SerializeField, Min(0f)] private float photographyCompletionDelay = .5f;
        [SerializeField, Min(.1f)] private float controlsReadingSeconds = .8f;

        private readonly RefitLessonText defaultText = new RefitLessonText();
        private readonly TutorialPanelText defaultPanelText = new TutorialPanelText();
        private TutorialPhotographyLesson photography;
        private TutorialRecyclingLesson recycling;
        private RefitTutorialProgress progress;
        private TutorialWaypointIndicator waypoint;
        private bool hasStarted, chapterConfirmed, hasWorkbenchPosition;
        private bool interrupted;
        private Vector2 workbenchPosition;
        private float completionDelay;
        private RefitTutorialStep displayedStep = (RefitTutorialStep)(-1);
        private CheckpointRefitTerminal hookedTerminal;
        private Func<bool> previousOpenBlocked, openBlockHandler;

        public TutorialTextContent TextContent => tutorialText;
        public TutorialDialoguePanel Panel => photography != null ? photography.Panel : null;
        public TutorialPhotographyLesson Photography => photography;
        public RefitTutorialProgress Progress => progress;
        public TutorialWaypointIndicator Waypoint => waypoint;
        // Kept as an observation API for existing callers; no in-menu tutorial is created.
        public TutorialRefitGuide Guide => null;
        public WasteRobotBase Machine => machine;
        public CheckpointRefitTerminal Terminal => terminal;
        public PhotoCameraDefinition TargetCamera => targetCamera;
        public float ArrivalRadiusMeters => arrivalRadiusMeters;
        public bool HasStarted => hasStarted;
        public bool IsComplete => hasStarted && progress != null && progress.IsComplete;
        public Vector2 EntranceMapPosition => ToMap(machine != null ? machine.PortalToWorld(entranceApproachLocal) : Vector2.zero);
        public Vector2 InteriorEntranceMapPosition => ToMap(machine != null ? machine.PortalToWorld(entranceInteriorLocal) : Vector2.zero);
        public Vector2 WorkbenchMapPosition => hasWorkbenchPosition ? workbenchPosition : ToMap(terminal != null ? terminal.transform.position : Vector3.zero);
        private RefitLessonText Text => tutorialText != null && tutorialText.Refit != null ? tutorialText.Refit : defaultText;
        private TutorialPanelText PanelText => tutorialText != null ? tutorialText.Panel : defaultPanelText;
        private bool RefitOpen => terminal != null && terminal.IsOpen;
        private bool TargetInstalled => targetCamera != null && bootstrap != null && bootstrap.photoMode != null
            && bootstrap.photoMode.CurrentCamera == targetCamera;


        private void Start()
        {
            if (gameObject.scene.path != TutorialDrivingLesson.ScenePath) { enabled = false; return; }
            photography = GetComponent<TutorialPhotographyLesson>();
            recycling = GetComponent<TutorialRecyclingLesson>();
            ResolveReferences();
        }

        private bool ResolveReferences()
        {
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                if (bootstrap == null) bootstrap = root.GetComponentInChildren<HeightMapPlayerSceneBootstrap>(true);
                if (machine == null)
                    foreach (WasteRobotBase candidate in root.GetComponentsInChildren<WasteRobotBase>(true))
                        if (candidate.InteriorRoot != null && candidate.InteriorRoot.GetComponentInChildren<CheckpointRefitTerminal>(true) != null)
                        { machine = candidate; break; }
            }
            if (terminal == null && machine != null && machine.InteriorRoot != null)
                terminal = machine.InteriorRoot.GetComponentInChildren<CheckpointRefitTerminal>(true);
            return bootstrap != null && bootstrap.gameObject.scene == gameObject.scene && bootstrap.robot != null
                && bootstrap.map != null && bootstrap.map.HasGeneratedMap && machine != null
                && machine.gameObject.scene == gameObject.scene && machine.InteriorRoot != null && terminal != null
                && terminal.gameObject.scene == gameObject.scene && terminal.transform.IsChildOf(machine.InteriorRoot.transform)
                && targetCamera != null;
        }

        private void LateUpdate()
        {
            if (TutorialTipOverLesson.BlocksMainline(gameObject))
            {
                if (!interrupted) progress?.Suspend();
                interrupted = true;
                if (waypoint != null) waypoint.SetVisible(false);
                return;
            }
            if (interrupted) { interrupted = false; progress?.Suspend(); }
            if (photography == null || recycling == null || Panel == null || !ResolveReferences()) return;
            if (!recycling.IsComplete)
            {
                if (hasStarted) ReleasePresentation();
                completionDelay = 0f; return;
            }
            if (!hasStarted)
            {
                if (!RefitOpen && (WorldModal() || !CanNavigate())) return;
                BeginLesson();
            }
            if (IsComplete)
            {
                HideCompletedPresentation();
                return;
            }

            bool open = RefitOpen;
            bool worldModal = !open && WorldModal();
            Panel.SetPhotoModeVisible(false);
            Panel.SetVisible(!open && !worldModal);
            waypoint.SetVisible(!open && !worldModal);
            if (machine.IsInside && !hasWorkbenchPosition) FindWorkbenchPosition();
            bool valid = open || (!worldModal && CanNavigate());
            if (!valid)
            {
                progress.Suspend(); Panel.SetContinuePrompt(false);
                if (!worldModal) RefreshPanel(IsStopped());
                return;
            }

            if (!open)
            {
                HandleCollapse();
            }
            bool stopped = IsStopped();
            Vector2 player = ToMap(bootstrap.robot.transform.position);
            bool atEntrance = Vector2.Distance(player, EntranceMapPosition) <= arrivalRadiusMeters;
            bool atWorkbench = terminal.IsNearby;
            progress.Sample(new RefitTutorialSample(Time.unscaledDeltaTime, valid,
                !open && !Panel.IsCollapsed,
                atEntrance, machine.IsInside, atWorkbench, stopped, open, TargetInstalled));

            if (!open)
            {
                Panel.SetContinuePrompt(progress.CanContinue, PanelText.ContinuePrompt);
                if (TutorialContinueInput.TryConsume(Panel, bootstrap))
                {
                    progress.Continue();
                    Panel.SetContinuePrompt(false);
                }
            }
            else Panel.SetContinuePrompt(false);

            if (IsComplete) { HideCompletedPresentation(); return; }

            if (progress.Step != displayedStep)
            {
                Panel.SetCollapsed(false);
                displayedStep = progress.Step;
            }
            RefreshPanel(stopped);
            UpdateWaypoint();
        }

        private void BeginLesson()
        {
            progress = new RefitTutorialProgress(controlsReadingSeconds);
            chapterConfirmed = false; hasStarted = true;
            recycling.SetPresentationOwned(false);
            Panel.SetContinuePrompt(false); Panel.SetPhotoModeVisible(false); Panel.SetCollapsed(false);
            var targetOwner = new GameObject("Camera Upgrade Destination");
            targetOwner.transform.SetParent(transform, false);
            waypoint = targetOwner.AddComponent<TutorialWaypointIndicator>();
            waypoint.Configure(bootstrap, targetSprite);
            HookTerminal();
        }

        private void HookTerminal()
        {
            if (hookedTerminal == terminal) return;
            UnhookTerminal();
            hookedTerminal = terminal;
            previousOpenBlocked = terminal.TutorialOpenBlocked;
            openBlockHandler = () => hasStarted && Panel != null
                && (Panel.CanContinue || TutorialContinueInput.BlocksWorldSouth(gameObject.scene))
                || (previousOpenBlocked != null && previousOpenBlocked());
            terminal.TutorialOpenBlocked = openBlockHandler;
        }

        private void HandleCollapse()
        {
#if ENABLE_INPUT_SYSTEM
            if (Gamepad.current != null && (Gamepad.current.dpad.left.wasPressedThisFrame
                || Gamepad.current.dpad.right.wasPressedThisFrame)) Panel.ToggleCollapsed();
#endif
        }

        private bool CanNavigate() => bootstrap != null && bootstrap.robot != null && bootstrap.robot.isActiveAndEnabled
            && !bootstrap.robot.IsMovementLocked && !bootstrap.robot.IsPhotoModeInputLocked
            && !bootstrap.robot.IsLevelThreeUnstable && !bootstrap.robot.IsAutoAligningDownhill
            && (bootstrap.photoMode == null || !bootstrap.photoMode.IsInputLocked)
            && (bootstrap.tumble == null || bootstrap.tumble.State == RobotTumbleState.Upright);

        private bool WorldModal() => Time.timeScale <= 0f || Time.deltaTime <= 0f
            || AnimalGame.MainUI.MainUI.BlocksGameplay || AnimalGame.AnimalDebug.AnimalDebugUI.BlocksOtherPanels
            || (bootstrap.photoMode != null && bootstrap.photoMode.IsActive)
            || (bootstrap.photoResultView != null && bootstrap.photoResultView.IsShowing);

        private bool IsStopped()
        {
            if (bootstrap.robot == null) return false;
            float scale = Mathf.Max(.0001f, bootstrap.map.MapMetersToWorldDistance(bootstrap.robot.Forward, 1f));
            return Mathf.Abs(bootstrap.robot.CurrentSpeed) / scale <= .2f
                && bootstrap.robot.CurrentTerrainVelocity.magnitude / scale <= .2f
                && Mathf.Abs(bootstrap.robot.CurrentTurnSpeed) <= 2f;
        }

        private TutorialTextTokens Tokens() => new TutorialTextTokens().Add("confirm", "A").Add("cancel", "B")
            .Add("photo", "X").Add("lb", "LB").Add("rb", "RB").Add("rt", "RT").Add("lt", "LT")
            .Add("lens", targetCamera != null ? targetCamera.DisplayName : "Zoom Lens 300")
            .Add("distance", Vector2.Distance(ToMap(bootstrap.robot.transform.position),
                machine.IsInside ? WorkbenchMapPosition : EntranceMapPosition));

        private void RefreshPanel(bool stopped)
        {
            if (progress == null || progress.IsComplete) { HideCompletedPresentation(); return; }
            TutorialStepText item = Text.GetStep(progress.Step) ?? defaultText.GetStep(progress.Step);
            TutorialTextTokens tokens = Tokens();
            string message = item.Message;
            string glyph = item.ControlGlyph, action = item.Action;
            string status = progress.Step == RefitTutorialStep.EquipLens && TargetInstalled ? Text.InstalledStatus : Text.WaitingStatus;
            Panel.SetPanelText(PanelText); Panel.SetLessonHint(tokens.Format(Text.ToggleHint));
            Panel.SetContent(tokens.Format(Text.Title), tokens.Format(message), tokens.Format(glyph),
                tokens.Format(action), tokens.Format(item.Detail), tokens.Format(status), 8, 9);
            Panel.SetProgress(progress.NormalizedProgress); Panel.SetSuccess(progress.IsComplete);
            Panel.SetContinuePrompt(!RefitOpen && progress.CanContinue, PanelText.ContinuePrompt);
        }

        private void HideCompletedPresentation()
        {
            UnhookTerminal();
            if (Panel != null) { Panel.SetContinuePrompt(false); Panel.SetVisible(false); Panel.SetLessonHint(string.Empty); }
            if (waypoint != null) waypoint.ClearTarget();
        }

        private void UpdateWaypoint()
        {
            if (RefitOpen || IsComplete) { waypoint.ClearTarget(); return; }
            switch (progress.Step)
            {
                case RefitTutorialStep.ReachMachine:
                    if (progress.CanContinue) waypoint.ClearTarget();
                    else waypoint.SetTarget(EntranceMapPosition);
                    break;
                case RefitTutorialStep.EnterMachine:
                    waypoint.SetTarget(InteriorEntranceMapPosition); break;
                case RefitTutorialStep.ReachWorkbench:
                case RefitTutorialStep.OpenWorkbench:
                    if (terminal.IsNearby) waypoint.ClearTarget();
                    else waypoint.SetTarget(WorkbenchMapPosition);
                    break;
                default: waypoint.ClearTarget(); break;
            }
        }

        private void FindWorkbenchPosition()
        {
            Vector2 center = ToMap(terminal.transform.position);
            Vector2 direction = (InteriorEntranceMapPosition - center).normalized;
            if (direction.sqrMagnitude < .001f) direction = Vector2.up;
            float radius = Mathf.Min(.65f, terminal.InteractionRadiusMeters * .65f);
            float body = bootstrap.traversalEvaluator != null ? bootstrap.traversalEvaluator.RobotObstacleCollisionRadiusMeters : .5f;
            for (int i = 0; i < 16; i++)
            {
                float angle = i * 22.5f * Mathf.Deg2Rad;
                Vector2 heading = new Vector2(direction.x * Mathf.Cos(angle) - direction.y * Mathf.Sin(angle),
                    direction.x * Mathf.Sin(angle) + direction.y * Mathf.Cos(angle));
                Vector2 candidate = center + heading * radius;
                if (WorldInteractionQuery.Query(InteractionShape.Circle(candidate, body + .05f),
                    WorldInteractionKind.Collision | WorldInteractionKind.BodyCollision, bootstrap.map,
                    gameObject.scene, ignoreHeld: bootstrap.robot.transform)) continue;
                if (bootstrap.traversalEvaluator != null)
                {
                    var surface = bootstrap.traversalEvaluator.EvaluateLocalSurface(bootstrap.map.MapPositionToWorld(candidate));
                    if (!surface.HasData || !surface.IsPassable || surface.RequiresHardStop) continue;
                }
                workbenchPosition = candidate; hasWorkbenchPosition = true; return;
            }
            // The authored station has no solid body. Its actual native interaction range remains authoritative.
            workbenchPosition = center; hasWorkbenchPosition = true;
        }

        private Vector2 ToMap(Vector2 world) => bootstrap != null && bootstrap.map != null
            && bootstrap.map.TrySampleWorldPosition(world, out Vector2 value, out _) ? value : Vector2.zero;

        private void UnhookTerminal()
        {
            if (hookedTerminal != null)
            {
                if (hookedTerminal.TutorialOpenBlocked == openBlockHandler) hookedTerminal.TutorialOpenBlocked = previousOpenBlocked;
            }
            hookedTerminal = null; previousOpenBlocked = openBlockHandler = null;
        }

        private void ReleasePresentation()
        {
            UnhookTerminal();
            if (waypoint != null) { waypoint.ClearTarget(); Destroy(waypoint.gameObject); }
            waypoint = null;
            if (Panel != null) { Panel.SetContinuePrompt(false); Panel.SetVisible(false); Panel.SetLessonHint(string.Empty); }
            if (recycling != null) recycling.SetPresentationOwned(true);
            hasStarted = chapterConfirmed = hasWorkbenchPosition = false; progress = null;
        }

        [ContextMenu("Restart Camera Upgrade Lesson")]
        public void RestartLesson()
        {
            if (!hasStarted || progress == null) return;
            progress.Reset(); chapterConfirmed = false; displayedStep = (RefitTutorialStep)(-1);
            HookTerminal();
            Panel.SetContinuePrompt(false); Panel.SetCollapsed(false);
        }

        private void OnDisable() { if (hasStarted) ReleasePresentation(); }
    }
}
