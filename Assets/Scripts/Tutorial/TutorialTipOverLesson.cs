using System.Collections.Generic;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using UnityEngine;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AnimalGame.Tutorial
{
    /// <summary>Temporarily owns this scene's tutorial presentation while native self-righting remains in control.</summary>
    [DefaultExecutionOrder(900)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TutorialDrivingLesson))]
    public sealed class TutorialTipOverLesson : MonoBehaviour
    {
        public const float InertiaSlowMotionFactor = .05f;
        [SerializeField] private HeightMapPlayerSceneBootstrap bootstrap;
        [SerializeField] private TutorialTextContent tutorialText;
        [SerializeField, Min(.1f)] private float requiredStableSeconds = .8f;
        [SerializeField, Range(.1f, .99f)] private float safeBalanceMagnitude = .9f;
        [SerializeField, Min(.2f)] private float automaticRecoveryNoticeSeconds = 1.5f;
        [SerializeField, Min(0f)] private float retryFeedbackSeconds = 1.4f;

        private static readonly Dictionary<int, TutorialTipOverLesson> instances = new();
        private readonly TipOverLessonText defaultText = new();
        private readonly TutorialPanelText defaultPanelText = new();
        private TutorialDrivingLesson driving;
        private RobotTumbleController tumble;
        private RobotSelfRightingController selfRighting;
        private RobotBalanceView balanceView;
        private TipOverTutorialProgress progress;
        private TutorialDialoguePanel.ContentSnapshot savedContent;
        private bool interrupting, resumePending, hasStarted, hasCompleted, reminder;
        private bool savedCollapsed, savedVisible, observedFallen;
        private int resumeFrame;
        private float naturalRecoveryElapsed, failureUntil;
        private Vector2 tumbleDirectionWorld = Vector2.right;
        private RobotSelfRightingFailureReason failureReason;
        private bool ownsInertiaPause, pendingInertiaPause, hasReadInertiaLesson;
        private float savedTimeScale = 1f, appliedInertiaTimeScale;
        private Vector2 inertiaCounterWorld = Vector2.right;

        public TutorialTextContent TextContent => tutorialText;
        public HeightMapPlayerSceneBootstrap Bootstrap => bootstrap;
        public TutorialDialoguePanel Panel => driving != null ? driving.Panel : null;
        public TipOverTutorialProgress Progress => progress;
        public bool HasStarted => hasStarted;
        public bool HasCompleted => hasCompleted;
        public bool IsInterrupting => interrupting;
        public bool IsReminder => reminder;
        public bool OwnsInertiaPause => ownsInertiaPause;
        public bool HasReadInertiaLesson => hasReadInertiaLesson;
        public Vector2 CurrentBraceDirection => selfRighting != null && selfRighting.HasBalancePresentation
            ? selfRighting.RequiredBraceDirectionLocal : WorldToRobot(tumbleDirectionWorld);
        public Vector2 CurrentEffortDirection => selfRighting != null && selfRighting.HasBalancePresentation
            ? selfRighting.RequiredEffortDirectionLocal : -CurrentBraceDirection;
        public Vector2 CurrentCounterDirection => WorldToRobot(inertiaCounterWorld);
        private TipOverLessonText Text => tutorialText != null && tutorialText.TipOver != null ? tutorialText.TipOver : defaultText;
        private TutorialPanelText PanelText => tutorialText != null ? tutorialText.Panel : defaultPanelText;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInstances() => instances.Clear();

        /// <summary>A scene-local gate: no other map, native controls or real result callback is paused.</summary>
        public static bool BlocksMainline(GameObject owner)
        {
            if (owner == null || owner.scene.path != TutorialDrivingLesson.ScenePath) return false;
            return instances.TryGetValue(owner.scene.handle, out var instance) && instance != null
                && instance.isActiveAndEnabled && (instance.interrupting || instance.resumePending);
        }

        /// <summary>Reserve unrelated interactions while the landing card owns this scene's slow motion.</summary>
        public static bool BlocksGameplayForReading(GameObject owner) => owner != null
            && owner.scene.path == TutorialDrivingLesson.ScenePath
            && instances.TryGetValue(owner.scene.handle, out var instance) && instance != null
            && instance.isActiveAndEnabled && instance.ownsInertiaPause;

        private void OnEnable()
        {
            if (gameObject.scene.path != TutorialDrivingLesson.ScenePath) return;
            instances[gameObject.scene.handle] = this;
        }

        private void Start()
        {
            if (gameObject.scene.path != TutorialDrivingLesson.ScenePath) { enabled = false; return; }
            driving = GetComponent<TutorialDrivingLesson>();
            ResolvePlayer();
        }

        private void ResolvePlayer()
        {
            if (bootstrap == null)
                foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                { bootstrap = root.GetComponentInChildren<HeightMapPlayerSceneBootstrap>(); if (bootstrap != null) break; }
            if (bootstrap == null || bootstrap.robot == null) return;
            balanceView = bootstrap.robot.GetComponent<RobotBalanceView>();
            var nextTumble = bootstrap.tumble != null ? bootstrap.tumble : bootstrap.robot.GetComponent<RobotTumbleController>();
            var nextRighting = bootstrap.selfRightingController != null ? bootstrap.selfRightingController
                : bootstrap.robot.GetComponent<RobotSelfRightingController>();
            if (nextTumble != tumble)
            {
                if (tumble != null) tumble.Started -= OnTumbleStarted;
                tumble = nextTumble;
                if (tumble != null) tumble.Started += OnTumbleStarted;
            }
            if (nextRighting != selfRighting)
            {
                if (selfRighting != null)
                { selfRighting.SupportFailed -= OnSupportFailed; selfRighting.RightingLanded -= OnRightingLanded; }
                selfRighting = nextRighting;
                if (selfRighting != null)
                { selfRighting.SupportFailed += OnSupportFailed; selfRighting.RightingLanded += OnRightingLanded; }
            }
        }

        private void OnTumbleStarted(RobotTumbleStartedInfo info)
        {
            tumbleDirectionWorld = info.WorldDirection.sqrMagnitude > .000001f ? info.WorldDirection.normalized : Vector2.right;
            failureUntil = 0f;
            if (ownsInertiaPause)
            {
                ReleaseInertiaPause(); pendingInertiaPause = false;
                progress?.ResumeRecovery();
            }
            if (interrupting) return; // Another fall is part of this same recovery attempt.
            BeginInterrupt();
        }

        private void BeginInterrupt()
        {
            CapturePresentation();
            interrupting = hasStarted = true; resumePending = false;
            reminder = hasCompleted; observedFallen = false; progress = null;
            naturalRecoveryElapsed = 0f;
            TutorialContinueInput.DisarmUntilRelease(gameObject.scene);
            if (Panel != null)
            { Panel.SetContinuePrompt(false); Panel.SetCollapsed(false); }
        }

        private void CapturePresentation()
        {
            if (savedContent != null || Panel == null) return;
            savedContent = Panel.CaptureContent(); savedCollapsed = Panel.IsCollapsed; savedVisible = Panel.IsVisible;
        }

        private void OnSupportFailed(RobotSelfRightingFailureInfo info)
        { if (interrupting) { failureReason = info.Reason; failureUntil = Time.unscaledTime + retryFeedbackSeconds; } }

        private void OnRightingLanded(RobotSelfRightingLandedInfo info)
        {
            if (!interrupting) return;
            inertiaCounterWorld = info.OriginalTumbleDirectionWorld.sqrMagnitude > .000001f
                ? info.OriginalTumbleDirectionWorld.normalized : tumbleDirectionWorld;
            if (reminder || hasReadInertiaLesson || ownsInertiaPause) return;
            pendingInertiaPause = true;
            TryBeginInertiaPause();
        }

        private void TryBeginInertiaPause()
        {
            if (!pendingInertiaPause || Panel == null || Time.timeScale <= 0f || ExternalModalOpen()) return;
            savedTimeScale = Time.timeScale;
            appliedInertiaTimeScale = savedTimeScale * InertiaSlowMotionFactor;
            // Keep native balance, inertia, camera motion and right-stick control running at 5% speed.
            ownsInertiaPause = true; pendingInertiaPause = false;
            Time.timeScale = appliedInertiaTimeScale;
            progress?.Suspend();
            TutorialContinueInput.DisarmUntilRelease(gameObject.scene);
            Panel.SetContinuePrompt(false); Panel.SetPhotoModeVisible(false);
            Panel.SetTutorialPauseVisible(true); Panel.SetCollapsed(false); Panel.SetVisible(true);
            RefreshInertiaPanel();
        }

        private void UpdateInertiaPause()
        {
            if (Time.timeScale <= 0f || ExternalModalOpen())
            { Panel.SetContinuePrompt(false); Panel.SetVisible(false); return; }
            if (!Mathf.Approximately(Time.timeScale, appliedInertiaTimeScale))
            {
                // An explicit foreign speed change ends this lease without replacing that speed.
                ReleaseInertiaPause(); progress?.ResumeRecovery(); RefreshRecoveryPanel(); return;
            }
            Panel.SetTutorialPauseVisible(true); Panel.SetVisible(true);
#if ENABLE_INPUT_SYSTEM
            if (Gamepad.current != null && (Gamepad.current.dpad.left.wasPressedThisFrame
                || Gamepad.current.dpad.right.wasPressedThisFrame)) Panel.ToggleCollapsed();
#endif
            RefreshInertiaPanel();
            if (TutorialContinueInput.TryConsume(Panel, bootstrap, allowDuringInterruption: true, allowDuringTutorialPause: true))
            {
                hasReadInertiaLesson = true;
                ReleaseInertiaPause();
                progress?.Suspend();
                TutorialContinueInput.DisarmUntilRelease(gameObject.scene);
                RefreshRecoveryPanel();
            }
        }

        private void RefreshInertiaPanel()
        {
            ClearRecoveryDirectionGuide();
            var tokens = Tokens();
            Panel.SetPanelText(PanelText); Panel.SetLessonHint(tokens.Format(Text.InertiaStatus));
            Panel.SetContent(tokens.Format(Text.InertiaTitle), tokens.Format(Text.InertiaMessage), "R",
                tokens.Format(Text.InertiaAction), tokens.Format(Text.InertiaDetail), tokens.Format(Text.InertiaStatus));
            Panel.SetContextLabel(Text.ContextLabel); Panel.SetProgress(0f); Panel.SetSuccess(false);
            Panel.SetContinuePrompt(true, tokens.Format(Text.InertiaContinuePrompt));
        }

        private void ReleaseInertiaPause()
        {
            if (!ownsInertiaPause) return;
            // Restore only the multiplier this card applied, leaving a foreign pause or speed intact.
            if (Mathf.Approximately(Time.timeScale, appliedInertiaTimeScale)) Time.timeScale = savedTimeScale;
            ownsInertiaPause = false;
            if (Panel != null) { Panel.SetTutorialPauseVisible(false); Panel.SetContinuePrompt(false); }
        }

        private void LateUpdate()
        {
            ResolvePlayer();
            if (driving == null) driving = GetComponent<TutorialDrivingLesson>();
            if (tumble == null || bootstrap == null || bootstrap.robot == null || Panel == null) return;
            if (resumePending)
            {
                // The final A cannot also confirm the restored card or activate a nearby world object.
                if (Time.frameCount <= resumeFrame || AdaptiveLegacyGamepadInput.IsSouthFaceButtonHeld()) return;
                RestorePresentation(); resumePending = false;
            }
            // Covers a native fall that happened before the scene's generated player was bound.
            if (!interrupting && tumble.State != RobotTumbleState.Upright)
            {
                tumbleDirectionWorld = tumble.DirectionWorld.sqrMagnitude > .000001f ? tumble.DirectionWorld.normalized : Vector2.right;
                BeginInterrupt();
            }
            if (!interrupting) return;
            CapturePresentation();
            if (pendingInertiaPause) TryBeginInertiaPause();
            if (ownsInertiaPause) { UpdateInertiaPause(); return; }
            bool fallen = tumble.State == RobotTumbleState.Fallen;
            if (fallen && !observedFallen)
            {
                observedFallen = true;
                progress = new TipOverTutorialProgress(requiredStableSeconds, reminder);
                progress.ObserveFallen(); // Preserve the real fall even if a modal currently owns the screen.
            }
            if (IsModal())
            {
                ClearRecoveryDirectionGuide();
                progress?.Suspend(); Panel.SetContinuePrompt(false); Panel.SetVisible(false); return;
            }
            Panel.SetPhotoModeVisible(false); Panel.SetVisible(true);
#if ENABLE_INPUT_SYSTEM
            if (Gamepad.current != null && (Gamepad.current.dpad.left.wasPressedThisFrame
                || Gamepad.current.dpad.right.wasPressedThisFrame)) Panel.ToggleCollapsed();
#endif
            bool upright = tumble.State == RobotTumbleState.Upright;
            bool righting = tumble.IsSelfRightingVisualActive || tumble.IsBalanceRecoveryActive
                || (selfRighting != null && selfRighting.State != RobotSelfRightingState.Inactive);
            bool stable = upright && !righting && bootstrap.balance != null && !bootstrap.balance.IsTippedOver
                && bootstrap.balance.CurrentState.Magnitude <= safeBalanceMagnitude
                && !bootstrap.robot.IsLevelThreeUnstable && !bootstrap.robot.IsAutoAligningDownhill;
            if (!observedFallen)
            {
                // A full roll can land upright on its own. Keep the first genuine self-righting lesson available.
                naturalRecoveryElapsed = stable ? naturalRecoveryElapsed + Mathf.Min(Time.deltaTime, .1f) : 0f;
                RefreshRollingPanel(upright);
                if (naturalRecoveryElapsed >= automaticRecoveryNoticeSeconds) FinishInterrupt(false);
                return;
            }
            var arm = bootstrap.armController;
            bool deployed = arm != null && arm.IsArmModeActive && arm.VisibleDeployment01 >= .999f;
            progress.Sample(new TipOverTutorialSample(Time.deltaTime, true, fallen, deployed, upright, righting, stable,
                AdaptiveLegacyGamepadInput.IsSouthFaceButtonHeld()));
            if (progress.IsComplete) { FinishInterrupt(!reminder); return; }
            Panel.SetContinuePrompt(progress.CanContinue, PanelText.ContinuePrompt);
            if (TutorialContinueInput.TryConsume(Panel, bootstrap, allowDuringInterruption: true))
            {
                progress.Continue(); Panel.SetContinuePrompt(false);
            }
            RefreshRecoveryPanel();
        }

        private bool IsModal() => Time.timeScale <= 0f || Time.deltaTime <= 0f || ExternalModalOpen();

        private bool ExternalModalOpen() => AnimalGame.MainUI.MainUI.BlocksGameplay || AnimalGame.AnimalDebug.AnimalDebugUI.BlocksOtherPanels
            || (bootstrap != null && bootstrap.photoMode != null && (bootstrap.photoMode.IsActive || bootstrap.photoMode.IsEntering
                || bootstrap.photoMode.IsExiting || bootstrap.photoMode.IsCapturing || bootstrap.photoMode.IsReviewing))
            || (bootstrap != null && bootstrap.photoResultView != null && bootstrap.photoResultView.IsShowing)
            || (GetComponent<TutorialRefitLesson>() is TutorialRefitLesson refit && refit.Terminal != null && refit.Terminal.IsOpen);

        private TutorialTextTokens Tokens() => new TutorialTextTokens().Add("confirm", "A")
            .Add("brace", "toward the fallen-side support").Add("effort", "toward the robot center")
            .Add("cadence", selfRighting != null ? selfRighting.RequiredEffortPushFrequency : 3f)
            .Add("counter", DirectionArrow(CurrentCounterDirection)).Add("inertia", DirectionArrow(-CurrentCounterDirection));

        private void RefreshRollingPanel(bool recovered)
        {
            ClearRecoveryDirectionGuide();
            var tokens = Tokens();
            Panel.SetPanelText(PanelText); Panel.SetLessonHint(string.Empty);
            Panel.SetContent(tokens.Format(Text.Title), tokens.Format(recovered ? Text.StabilizeMessage : Text.RollingMessage),
                recovered ? "R" : "…", tokens.Format(recovered ? Text.StabilizeStatus : Text.RollingAction),
                recovered ? string.Empty : tokens.Format(Text.RollingDetail), tokens.Format(Text.WaitingStatus));
            Panel.SetContextLabel(Text.ContextLabel); Panel.SetProgress(0f); Panel.SetSuccess(false); Panel.SetContinuePrompt(false);
        }

        private void RefreshRecoveryPanel()
        {
            if (progress == null || progress.IsComplete) { ClearRecoveryDirectionGuide(); Panel.SetContinuePrompt(false); return; }
            var tokens = Tokens();
            TutorialStepText card = Text.GetStep(progress.Step) ?? defaultText.GetStep(progress.Step);
            string message = card.Message, action = card.Action, detail = card.Detail, glyph = card.ControlGlyph;
            string status = Text.WaitingStatus;
            float value = progress.NormalizedProgress;
            if (progress.Step == TipOverTutorialStep.Recover)
            {
                if (tumble.State == RobotTumbleState.Tumbling || tumble.State == RobotTumbleState.FinalRocking)
                { message = Text.RollingMessage; action = Text.RollingAction; detail = Text.RollingDetail; glyph = "…"; }
                else if (tumble.State == RobotTumbleState.Upright)
                { message = Text.StabilizeMessage; action = status = Text.StabilizeStatus; glyph = "R"; detail = string.Empty; }
                else if (selfRighting != null && selfRighting.State == RobotSelfRightingState.RightingChassis)
                { message = Text.RightingMessage; action = status = Text.RightingStatus; glyph = "R"; detail = string.Empty; value = .9f; }
                else if (Time.unscaledTime < failureUntil)
                {
                    message = failureReason == RobotSelfRightingFailureReason.ArmDirectionLost ? Text.ArmLostMessage
                        : failureReason == RobotSelfRightingFailureReason.ForceCadenceLost ? Text.CadenceLostMessage : Text.DirectionLostMessage;
                    status = Text.RetryStatus;
                }
                else if (bootstrap.armController == null || !bootstrap.armController.IsArmModeActive)
                {
                    message = reminder ? Text.ReminderMessage : Text.DeployArms.Message;
                    glyph = "L3"; action = Text.DeployArms.Action; detail = Text.DeployArms.Detail;
                }
                else if (selfRighting == null || !selfRighting.IsArmSupportCorrect)
                { message = Text.SupportMessage; action = status = Text.SupportStatus; glyph = "L"; detail = string.Empty; }
                else
                {
                    message = Text.EffortMessage; action = status = Text.EffortStatus;
                    if (selfRighting != null) value = .1f + selfRighting.PullProgress01 * .75f;
                }
            }
            Panel.SetPanelText(PanelText); Panel.SetLessonHint(progress.IsComplete ? string.Empty : tokens.Format(Text.ToggleHint));
            Panel.SetContent(tokens.Format(Text.Title), tokens.Format(message), tokens.Format(glyph), tokens.Format(action),
                tokens.Format(detail), tokens.Format(status));
            Panel.SetContextLabel(Text.ContextLabel); Panel.SetProgress(value); Panel.SetSuccess(progress.IsComplete);
            Panel.SetContinuePrompt(progress.CanContinue, PanelText.ContinuePrompt);
            UpdateRecoveryDirectionGuide();
        }

        private void UpdateRecoveryDirectionGuide()
        {
            var arm = bootstrap.armController;
            bool show = progress != null && progress.Step == TipOverTutorialStep.Recover
                && tumble.State == RobotTumbleState.Fallen && selfRighting != null
                && selfRighting.State != RobotSelfRightingState.RightingChassis
                && arm != null && arm.IsArmModeActive && !ownsInertiaPause && !IsModal()
                && Panel.Root?.panel != null;
            if (!show) { ClearRecoveryDirectionGuide(); return; }

            bool isBracing = !selfRighting.IsArmSupportCorrect;
            Vector2 input = isBracing
                ? arm.CurrentTargetLocal.normalized * Mathf.Clamp01(arm.CurrentInputMagnitude)
                : selfRighting.CurrentBalancePullInputLocal;
            Vector2 bodyPanel = Vector2.zero, pointPanel = Vector2.zero;
            if (balanceView != null && balanceView.TryGetDisplayedBalanceScreenPositions(out Vector2 body, out Vector2 point))
            {
                bodyPanel = RuntimePanelUtils.ScreenToPanel(Panel.Root.panel, new Vector2(body.x, Screen.height - body.y));
                pointPanel = RuntimePanelUtils.ScreenToPanel(Panel.Root.panel, new Vector2(point.x, Screen.height - point.y));
            }
            Panel.SetRecoveryDirectionGuide(true, isBracing, CurrentBraceDirection, CurrentEffortDirection, input,
                selfRighting.IsArmSupportCorrect, selfRighting.IsEffortPushInputCorrect, bodyPanel, pointPanel);
        }

        private void ClearRecoveryDirectionGuide()
        {
            if (Panel != null) Panel.SetRecoveryDirectionGuide(false, false, Vector2.zero, Vector2.zero,
                Vector2.zero, false, false, Vector2.zero, Vector2.zero);
        }

        private Vector2 WorldToRobot(Vector2 world)
        {
            if (bootstrap == null || bootstrap.robot == null) return Vector2.right;
            Transform robot = bootstrap.robot.transform;
            return new Vector2(Vector2.Dot(world, robot.right), Vector2.Dot(world, robot.up)).normalized;
        }

        private static string DirectionArrow(Vector2 direction)
        {
            string[] arrows = { "→", "↗", "↑", "↖", "←", "↙", "↓", "↘" };
            int index = Mathf.RoundToInt(Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg / 45f);
            return arrows[(index % 8 + 8) % 8];
        }

        private void FinishInterrupt(bool completed)
        {
            ClearRecoveryDirectionGuide();
            ReleaseInertiaPause(); pendingInertiaPause = false;
            if (completed) hasCompleted = true;
            interrupting = false; resumePending = true; resumeFrame = Time.frameCount;
            Panel.SetContinuePrompt(false); Panel.SetVisible(false);
            TutorialContinueInput.DisarmUntilRelease(gameObject.scene);
        }

        private void RestorePresentation()
        {
            if (Panel != null && savedContent != null)
            {
                Panel.RestoreContent(savedContent); Panel.SetCollapsed(savedCollapsed);
                Panel.SetVisible(savedVisible && !IsModal()); Panel.SetContinuePrompt(false);
            }
            savedContent = null;
        }

        [ContextMenu("Reset First Tip-over Tutorial")]
        public void ResetFirstTipOverTutorial()
        {
            ClearRecoveryDirectionGuide();
            ReleaseInertiaPause(); pendingInertiaPause = hasReadInertiaLesson = false;
            interrupting = resumePending = hasStarted = hasCompleted = reminder = observedFallen = false;
            progress = null; RestorePresentation(); TutorialContinueInput.DisarmUntilRelease(gameObject.scene);
        }

        private void OnDisable()
        {
            ClearRecoveryDirectionGuide();
            ReleaseInertiaPause(); pendingInertiaPause = false;
            if (tumble != null) tumble.Started -= OnTumbleStarted;
            if (selfRighting != null)
            { selfRighting.SupportFailed -= OnSupportFailed; selfRighting.RightingLanded -= OnRightingLanded; }
            tumble = null; selfRighting = null;
            if (instances.TryGetValue(gameObject.scene.handle, out var value) && value == this) instances.Remove(gameObject.scene.handle);
            if (interrupting || resumePending) RestorePresentation();
            interrupting = resumePending = false;
        }
    }
}
