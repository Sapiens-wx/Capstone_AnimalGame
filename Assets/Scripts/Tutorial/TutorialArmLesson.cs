using AnimalGame.MapTest;
using AnimalGame.RobotArm;
using AnimalGame.RobotMap;
using UnityEngine;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AnimalGame.Tutorial
{
    /// <summary>In-place empty-hand practice that observes normal arm controls in this scene only.</summary>
    [DefaultExecutionOrder(1200)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TutorialTerrainLesson))]
    public sealed class TutorialArmLesson : MonoBehaviour
    {
        [SerializeField] private HeightMapPlayerSceneBootstrap bootstrap;
        [SerializeField] private TutorialTextContent tutorialText;
        [SerializeField, Range(.05f, .5f)] private float reachDistanceOfBodyDiameter = .2f;
        [SerializeField, Range(5f, 40f)] private float swingAngleDegrees = 15f;
        [SerializeField, Min(.1f)] private float holdStillSeconds = .6f;
        [SerializeField, Min(0f)] private float terrainCompletionDelay = 1.5f;

        private readonly ArmsLessonText defaultText = new ArmsLessonText();
        private readonly TutorialPanelText defaultPanelText = new TutorialPanelText();
        private TutorialTerrainLesson terrain;
        private TutorialDrivingLesson driving;
        private RobotArmController arm;
        private RobotMarkerView marker;
        private ArmTutorialProgress progress;
        private Label directionCue;
        private float completionDelay, refreshRemaining;
        private bool previousToggleHeld, hasStarted;
        private bool ownsPresentation = true;

        public TutorialTextContent TextContent => tutorialText;
        public ArmTutorialProgress Progress => progress;
        public TutorialDialoguePanel Panel => driving != null ? driving.Panel : null;
        public bool HasStarted => hasStarted;
        public bool IsComplete => hasStarted && progress != null && progress.IsComplete;
        public void SetPresentationOwned(bool value) => ownsPresentation = value;
        private ArmsLessonText Text => tutorialText != null && tutorialText.Arms != null ? tutorialText.Arms : defaultText;

        private void Start()
        {
            if (gameObject.scene.path != TutorialDrivingLesson.ScenePath) { enabled = false; return; }
            terrain = GetComponent<TutorialTerrainLesson>();
            driving = GetComponent<TutorialDrivingLesson>();
            if (bootstrap == null)
                foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                {
                    bootstrap = root.GetComponentInChildren<HeightMapPlayerSceneBootstrap>();
                    if (bootstrap != null) break;
                }
            previousToggleHeld = AdaptiveLegacyGamepadInput.IsLeftStickButtonHeld();
        }

        private void LateUpdate()
        {
            bool toggleHeld = AdaptiveLegacyGamepadInput.IsLeftStickButtonHeld();
            bool togglePressed = toggleHeld && !previousToggleHeld;
            previousToggleHeld = toggleHeld;
            if (terrain == null || Panel == null || bootstrap == null) return;
            if (!terrain.IsComplete)
            {
                if (hasStarted) ReleasePresentation();
                completionDelay = 0f;
                return;
            }
            if (!ownsPresentation) return;
            arm = bootstrap.armController;
            if (arm == null || bootstrap.robot == null) return;
            if (marker == null) marker = bootstrap.robot.GetComponent<RobotMarkerView>();
            bool modal = IsModal();
            bool justStarted = false;
            if (!hasStarted)
            {
                if (modal || !CanPractice()) return;
                completionDelay += Time.deltaTime;
                if (completionDelay < terrainCompletionDelay) return;
                BeginLesson();
                justStarted = true;
            }
            Panel.SetVisible(!modal);
            if (modal)
            {
                progress.Suspend();
                SetDirectionCueVisible(false);
                return;
            }
#if ENABLE_INPUT_SYSTEM
            if (!justStarted && Gamepad.current != null && (Gamepad.current.dpad.left.wasPressedThisFrame
                || Gamepad.current.dpad.right.wasPressedThisFrame)) Panel.ToggleCollapsed();
#endif
            bool valid = CanPractice();
            Vector2 handCentre = HandCentreLocal();
            ArmTutorialStep previousStep = progress.Step;
            progress.Sample(new ArmTutorialSample(handCentre,
                AdaptiveLegacyGamepadInput.ReadRawLeftStick(), arm.VisibleDeployment01,
                arm.IsArmModeActive, arm.State == RobotArmState.OuterOperating && !arm.IsTumbleArmLayoutActive,
                arm.State == RobotArmState.Retracted, arm.HeldObject == null && arm.State != RobotArmState.Recycling,
                bootstrap.tumble == null || bootstrap.tumble.State == RobotTumbleState.Upright,
                valid, Time.deltaTime, !justStarted && ManualActionPressed(progress.Step,
                    togglePressed, arm.ManualRetractRequestedThisFrame), arm.IsBlocked));
            if (progress.IsStepSatisfied) progress.Advance();
            if (progress.Step != previousStep)
            {
                Panel.SetCollapsed(false);
                refreshRemaining = 0f;
            }
            refreshRemaining -= Time.unscaledDeltaTime;
            if (refreshRemaining <= 0f)
            {
                RefreshPanel(valid);
                refreshRemaining = .08f;
            }
            UpdateDirectionCue(valid);
        }

        private void BeginLesson()
        {
            progress = new ArmTutorialProgress(
                Mathf.Max(.01f, marker != null ? marker.BodyDiameter : .72f) * reachDistanceOfBodyDiameter,
                swingAngleDegrees, holdStillSeconds);
            terrain.SetPresentationOwned(false);
            hasStarted = true;
            Panel.SetCollapsed(false);
            refreshRemaining = 0f;
        }

        private bool IsModal() => Time.deltaTime <= 0f || AnimalGame.MainUI.MainUI.BlocksGameplay
            || (bootstrap.photoMode != null && (bootstrap.photoMode.IsActive || bootstrap.photoMode.IsInputLocked))
            || (bootstrap.photoResultView != null && bootstrap.photoResultView.IsShowing);

        // Observe an accepted close command so menu input and B on hidden arms cannot satisfy Stow.
        private static bool ManualActionPressed(ArmTutorialStep step, bool togglePressed, bool closeAccepted) =>
            togglePressed || (step == ArmTutorialStep.Stow && closeAccepted);

        private bool CanPractice()
        {
            RobotMover robot = bootstrap != null ? bootstrap.robot : null;
            return robot != null && robot.isActiveAndEnabled && arm != null && arm.isActiveAndEnabled
                && marker != null && marker.MarkerVisualRoot != null && !robot.IsMovementLocked
                && !robot.IsPhotoModeInputLocked && !robot.IsLevelThreeUnstable && !robot.IsAutoAligningDownhill
                && bootstrap.gameObject.scene == gameObject.scene && bootstrap.map != null && bootstrap.map.HasGeneratedMap
                && (bootstrap.tumble == null || bootstrap.tumble.State == RobotTumbleState.Upright);
        }

        private Vector2 HandCentreLocal() => marker != null && marker.MarkerVisualRoot != null
            ? (Vector2)marker.MarkerVisualRoot.InverseTransformPoint((arm.LeftHandWorld + arm.RightHandWorld) * .5f)
            : Vector2.zero;

        private void RefreshPanel(bool canPractice = true)
        {
            if (Panel == null || progress == null) return;
            ArmsLessonText text = Text;
            TutorialStepText step = text.GetStep(progress.Step) ?? defaultText.GetStep(progress.Step);
            float value = progress.Value, target = progress.Target;
            if (progress.Step == ArmTutorialStep.Extend || progress.Step == ArmTutorialStep.RetractReach)
            {
                float worldPerLocal = marker != null && marker.MarkerVisualRoot != null
                    ? marker.MarkerVisualRoot.TransformVector(Vector3.up).magnitude : 1f;
                float worldPerMeter = bootstrap != null && bootstrap.map != null && bootstrap.robot != null
                    ? bootstrap.map.MapMetersToWorldDistance(bootstrap.robot.Forward, 1f) : 1f;
                float centimetersPerLocal = worldPerLocal / Mathf.Max(.0001f, worldPerMeter) * 100f;
                value *= centimetersPerLocal; target *= centimetersPerLocal;
            }
            string rt = "RT", lt = "LT", lb = "LB";
#if ENABLE_INPUT_SYSTEM
            if (Gamepad.current != null && Gamepad.current.layout.Contains("Dual"))
            { rt = "R2"; lt = "L2"; lb = "L1"; }
#endif
            var tokens = new TutorialTextTokens().Add("value", value).Add("target", target)
                .Add("rt", rt).Add("lt", lt).Add("lb", lb);
            string message = step.Message, action = step.Action, control = step.ControlGlyph;
            string status = text.WaitingStatus;
            bool empty = arm == null || (arm.HeldObject == null && arm.State != RobotArmState.Recycling);
            bool needsReopen = arm != null && progress.Step > ArmTutorialStep.Deploy
                && progress.Step < ArmTutorialStep.Stow && (!arm.IsArmModeActive || arm.VisibleDeployment01 < .999f);
            if (progress.IsComplete) status = text.CompleteStatus;
            else if (!empty) status = text.EmptyHandsStatus;
            else if (!canPractice || (arm != null && arm.IsBlocked)) status = text.BlockedStatus;
            else if (progress.Step == ArmTutorialStep.Deploy && progress.NeedsInitialStow)
            { message = text.InitialStowMessage; action = text.InitialStowAction; }
            else if (needsReopen)
            { message = text.ReopenMessage; action = text.Deploy.Action; control = text.Deploy.ControlGlyph; status = text.ReopenStatus; }
            else if (progress.Step == ArmTutorialStep.Stow && arm != null && !arm.IsArmModeActive
                && arm.State == RobotArmState.Retracted && !progress.IsStepSatisfied)
            { message = text.StowRecoveryMessage; status = text.ReopenStatus; }
            Panel.SetPanelText(tutorialText != null ? tutorialText.Panel : defaultPanelText);
            Panel.SetLessonHint(progress.IsComplete ? string.Empty : text.ToggleHint);
            Panel.SetContent(tokens.Format(text.Title), tokens.Format(message), tokens.Format(control),
                tokens.Format(action), tokens.Format(step.Detail), tokens.Format(status), 3, 8);
            Panel.SetProgress(progress.NormalizedProgress);
            Panel.SetSuccess(progress.IsComplete);
        }

        private void UpdateDirectionCue(bool valid)
        {
            bool show = valid && arm.HeldObject == null && arm.IsArmModeActive && arm.VisibleDeployment01 >= .999f
                && !arm.IsBlocked && !Panel.IsCollapsed && progress.Step >= ArmTutorialStep.Extend
                && progress.Step <= ArmTutorialStep.SwingRight && Panel.Root?.panel != null && bootstrap.mapCamera != null;
            if (!show) { SetDirectionCueVisible(false); return; }
            if (directionCue == null || directionCue.parent != Panel.Root)
            {
                directionCue?.RemoveFromHierarchy();
                directionCue = new Label("→") { name = "TutorialArmDirection", pickingMode = PickingMode.Ignore };
                directionCue.style.position = Position.Absolute;
                directionCue.style.width = 46; directionCue.style.height = 46;
                directionCue.style.fontSize = 34;
                directionCue.style.color = new Color(212f / 255f, 184f / 255f, 84f / 255f);
                directionCue.style.unityTextAlign = TextAnchor.MiddleCenter;
                Panel.Root.Add(directionCue);
            }
            Vector2 local = HandCentreLocal();
            Vector2 radial = local.sqrMagnitude > .000001f ? local.normalized : Vector2.up;
            Vector2 direction = progress.Step switch
            {
                ArmTutorialStep.Extend => radial,
                ArmTutorialStep.RetractReach => -radial,
                ArmTutorialStep.SwingLeft => new Vector2(-radial.y, radial.x),
                _ => new Vector2(radial.y, -radial.x)
            };
            Vector3 world = (arm.LeftHandWorld + arm.RightHandWorld) * .5f;
            Vector3 screen = bootstrap.mapCamera.WorldToScreenPoint(world);
            Vector3 tip = bootstrap.mapCamera.WorldToScreenPoint(world + marker.MarkerVisualRoot.TransformVector(direction));
            Vector2 point = RuntimePanelUtils.ScreenToPanel(Panel.Root.panel, new Vector2(screen.x, Screen.height - screen.y));
            Vector2 end = RuntimePanelUtils.ScreenToPanel(Panel.Root.panel, new Vector2(tip.x, Screen.height - tip.y));
            Vector2 screenDirection = (end - point).normalized;
            if (!float.IsFinite(point.x) || !float.IsFinite(point.y) || !float.IsFinite(screenDirection.x)
                || !float.IsFinite(screenDirection.y) || screen.z < bootstrap.mapCamera.nearClipPlane)
            { SetDirectionCueVisible(false); return; }
            float fit = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
            Vector2 center = point + screenDirection * (40f + 4f * Mathf.Sin(Time.time * 4f)) * fit;
            directionCue.style.left = center.x - 23f; directionCue.style.top = center.y - 23f;
            directionCue.style.scale = new Scale(new Vector3(fit, fit, 1f));
            directionCue.style.rotate = new Rotate(new Angle(Mathf.Atan2(screenDirection.y, screenDirection.x) * Mathf.Rad2Deg));
            SetDirectionCueVisible(true);
        }

        private void SetDirectionCueVisible(bool value)
        {
            if (directionCue != null) directionCue.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
        }

        [ContextMenu("Restart Arm Lesson")]
        public void RestartLesson()
        {
            if (!hasStarted || progress == null) return;
            progress.Reset();
            previousToggleHeld = AdaptiveLegacyGamepadInput.IsLeftStickButtonHeld();
            Panel.SetCollapsed(false);
            refreshRemaining = 0f;
        }

        private void ReleasePresentation()
        {
            directionCue?.RemoveFromHierarchy(); directionCue = null;
            if (Panel != null) Panel.SetLessonHint(string.Empty);
            hasStarted = false; progress = null;
            ownsPresentation = true;
            if (terrain != null) terrain.SetPresentationOwned(true);
        }

        private void OnDisable()
        {
            if (hasStarted) ReleasePresentation();
        }
    }
}
