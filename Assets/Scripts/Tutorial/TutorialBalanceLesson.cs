using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AnimalGame.Tutorial
{
    /// <summary>Scene-only balance introduction. Observes the existing controls without changing their physics.</summary>
    [DefaultExecutionOrder(1100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TutorialTerrainLesson))]
    public sealed class TutorialBalanceLesson : MonoBehaviour
    {
        [SerializeField] private HeightMapPlayerSceneBootstrap bootstrap;
        [SerializeField] private TutorialTextContent tutorialText;
        [SerializeField, Min(0f)] private float terrainCompletionDelay = 1.5f;
        [SerializeField, Min(1f)] private float indicatorReadingSeconds = 3f;
        [SerializeField, Min(1f)] private float riskReadingSeconds = 3f;
        [SerializeField, Range(.02f, .2f)] private float requiredPointMovement = .05f;

        private readonly BalanceLessonText defaultText = new BalanceLessonText();
        private readonly TutorialPanelText defaultPanelText = new TutorialPanelText();
        private TutorialTerrainLesson terrain;
        private TutorialDrivingLesson driving;
        private BalanceTutorialProgress progress;
        private bool hasStarted, ownsPresentation = true;
        private bool chapterConfirmed;
        private bool interrupted;
        private float completionDelay, refreshRemaining;
        private BalanceTutorialStep displayedStep = (BalanceTutorialStep)(-1);

        public TutorialTextContent TextContent => tutorialText;
        public BalanceTutorialProgress Progress => progress;
        public TutorialDialoguePanel Panel => driving != null ? driving.Panel : null;
        public bool HasStarted => hasStarted;
        public bool IsComplete => hasStarted && progress != null && progress.IsComplete;
        private BalanceLessonText Text => tutorialText != null && tutorialText.Balance != null
            ? tutorialText.Balance : defaultText;

        private void Start()
        {
            if (gameObject.scene.path != TutorialDrivingLesson.ScenePath) { enabled = false; return; }
            terrain = GetComponent<TutorialTerrainLesson>();
            driving = GetComponent<TutorialDrivingLesson>();
            if (bootstrap == null)
                foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                { bootstrap = root.GetComponentInChildren<HeightMapPlayerSceneBootstrap>(); if (bootstrap != null) break; }
        }

        public void SetPresentationOwned(bool value) => ownsPresentation = value;

        private void LateUpdate()
        {
            if (TutorialTipOverLesson.BlocksMainline(gameObject))
            {
                if (!interrupted) progress?.Suspend();
                interrupted = true;
                return;
            }
            if (interrupted)
            {
                interrupted = false;
                progress?.Suspend();
                refreshRemaining = 0f;
            }
            if (terrain == null || Panel == null || bootstrap == null) return;
            if (!terrain.IsComplete)
            {
                if (hasStarted) ReleasePresentation();
                completionDelay = 0f; return;
            }
            if (!ownsPresentation) return;
            if (progress != null && progress.IsComplete) { Panel.SetContinuePrompt(false); Panel.SetVisible(false); return; }
            bool modal = IsModal();
            bool justStarted = false;
            if (!hasStarted)
            {
                if (modal || !ReadyToPresent()) return;
                BeginLesson(); justStarted = true;
            }
            Panel.SetVisible(!modal);
            if (modal) { progress.Suspend(); Panel.SetContinuePrompt(false); return; }
#if ENABLE_INPUT_SYSTEM
            if (!justStarted && Gamepad.current != null && (Gamepad.current.dpad.left.wasPressedThisFrame
                || Gamepad.current.dpad.right.wasPressedThisFrame)) Panel.ToggleCollapsed();
#endif
            bool valid = CanPractice();
            RobotBalanceState state = bootstrap.balance != null ? bootstrap.balance.CurrentState : default;
            progress.Sample(new BalanceTutorialSample(Time.deltaTime, valid, !Panel.IsCollapsed,
                AdaptiveLegacyGamepadInput.ReadRightStick(), state.PlayerCounterbalanceLocal, state.NormalizedLocalOffset));
            Panel.SetContinuePrompt(valid && progress.CanContinue,
                (tutorialText != null ? tutorialText.Panel : defaultPanelText).ContinuePrompt);
            if (TutorialContinueInput.TryConsume(Panel, bootstrap))
            {
                progress.Continue();
                Panel.SetContinuePrompt(false); refreshRemaining = 0f;
            }
            if (progress.IsComplete) { Panel.SetContinuePrompt(false); Panel.SetVisible(false); return; }
            if (progress.Step != displayedStep) { Panel.SetCollapsed(false); refreshRemaining = 0f; }
            refreshRemaining -= Time.unscaledDeltaTime;
            if (refreshRemaining <= 0f) { RefreshPanel(valid); refreshRemaining = .08f; }
        }

        private void BeginLesson()
        {
            progress = new BalanceTutorialProgress(indicatorReadingSeconds, riskReadingSeconds, requiredPointMovement);
            hasStarted = true; terrain.SetPresentationOwned(false);
            Panel.SetContinuePrompt(false);
            Panel.SetCollapsed(false); Panel.SetLessonHint(string.Empty);
            displayedStep = (BalanceTutorialStep)(-1); refreshRemaining = 0f;
        }

        private bool IsModal() => Time.deltaTime <= 0f || AnimalGame.MainUI.MainUI.BlocksGameplay
            || AnimalGame.AnimalDebug.DebugUI.BlocksOtherPanels
            || (bootstrap.photoMode != null && (bootstrap.photoMode.IsActive || bootstrap.photoMode.IsInputLocked))
            || (bootstrap.photoResultView != null && bootstrap.photoResultView.IsShowing);

        private bool ReadyToPresent() => bootstrap.robot != null && bootstrap.robot.isActiveAndEnabled
            && bootstrap.balance != null && bootstrap.balance.isActiveAndEnabled && !bootstrap.balance.IsTippedOver
            && bootstrap.balanceView != null && bootstrap.balanceView.isActiveAndEnabled && bootstrap.balanceView.IsBalanceDisplayVisible
            && !bootstrap.robot.IsMovementLocked && !bootstrap.robot.IsPhotoModeInputLocked
            && bootstrap.gameObject.scene == gameObject.scene && bootstrap.map != null && bootstrap.map.HasGeneratedMap
            && (bootstrap.tumble == null || bootstrap.tumble.State == RobotTumbleState.Upright);

        private bool CanPractice() => ReadyToPresent();

        private void RefreshPanel(bool valid = true)
        {
            if (Panel == null || progress == null) return;
            if (progress.IsComplete) { Panel.SetContinuePrompt(false); Panel.SetVisible(false); return; }
            BalanceLessonText text = Text;
            TutorialStepText step = text.GetStep(progress.Step) ?? defaultText.GetStep(progress.Step);
            var tokens = new TutorialTextTokens().Add("value", progress.Value).Add("target", progress.Target);
            string message = step.Message, glyph = step.ControlGlyph, action = step.Action;
            string status = progress.Step == BalanceTutorialStep.AdjustWeight ? text.WaitingStatus : text.ReadingStatus;
            if (progress.IsComplete) status = text.CompleteStatus;
            else if (!valid) status = text.BlockedStatus;
            else if (progress.Step == BalanceTutorialStep.AdjustWeight && progress.NeedsNeutral)
            { message = text.NeutralMessage; status = text.NeutralStatus; }
            Panel.SetPanelText(tutorialText != null ? tutorialText.Panel : defaultPanelText);
            Panel.SetLessonHint(progress.IsComplete ? string.Empty : text.ToggleHint);
            Panel.SetContent(tokens.Format(text.Title), tokens.Format(message), tokens.Format(glyph), tokens.Format(action),
                tokens.Format(step.Detail), tokens.Format(status), 3, 9);
            Panel.SetProgress(progress.NormalizedProgress); Panel.SetSuccess(progress.CanContinue);
            displayedStep = progress.Step;
        }

        [ContextMenu("Restart Balance Lesson")]
        public void RestartLesson()
        {
            if (!hasStarted || progress == null) return;
            progress.Reset(); Panel.SetContinuePrompt(false);
            Panel.SetCollapsed(false); refreshRemaining = 0f;
        }

        private void ReleasePresentation()
        {
            if (Panel != null) Panel.SetLessonHint(string.Empty);
            Panel?.SetContinuePrompt(false);
            hasStarted = false; progress = null; ownsPresentation = true;
            if (terrain != null) terrain.SetPresentationOwned(true);
        }

        private void OnDisable() { if (hasStarted) ReleasePresentation(); }
    }
}
