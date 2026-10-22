using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AnimalGame.Tutorial
{
    /// <summary>Opt-in, read-only driving lesson. Only the temporary tutorial scene may run it.</summary>
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class TutorialDrivingLesson : MonoBehaviour
    {
        public const string ScenePath = "Assets/Scenes/GameMaps/TutorialTemporaryMap.unity";
        [SerializeField] private HeightMapPlayerSceneBootstrap bootstrap;
        [Tooltip("教程文案资源。选中资源或点击下方按钮，可在 Inspector 编辑每一步文字。")]
        [SerializeField] private TutorialTextContent tutorialText;
        [SerializeField, Min(.5f)] private float forwardDistanceMeters = 5f;
        [SerializeField, Min(.5f)] private float reverseDistanceMeters = 3f;
        [SerializeField, Range(10f, 180f)] private float turnAngleDegrees = 45f;
        [SerializeField, Min(.2f)] private float stopDurationSeconds = .8f;
        [SerializeField, Range(10f, 90f)] private float movingTurnAngleDegrees = 30f;
        [SerializeField, Min(.5f)] private float movingTurnDistanceMeters = 2f;

        private DrivingTutorialProgress progress;
        private TutorialDialoguePanel panel;
        private float feedbackRemaining;
        private float refreshRemaining;
        private string lastFeedback;
        private bool ownsPresentation = true;
        private readonly DrivingLessonText defaultText = new DrivingLessonText();
        private readonly TutorialPanelText defaultPanelText = new TutorialPanelText();

        public TutorialTextContent TextContent => tutorialText;
        public DrivingTutorialProgress Progress => progress;
        public TutorialDialoguePanel Panel => panel;
        public bool IsComplete => progress != null && progress.IsComplete;

        // The next scene-owned lesson reuses this card after driving is complete.
        public void SetPresentationOwned(bool value) => ownsPresentation = value;

        private void Start()
        {
            if (gameObject.scene.path != ScenePath) { enabled = false; return; }
            progress = new DrivingTutorialProgress(forwardDistanceMeters, reverseDistanceMeters,
                turnAngleDegrees, stopDurationSeconds, movingTurnAngleDegrees, movingTurnDistanceMeters);
            var panelObject = new GameObject("Driving Tutorial Dialogue");
            panelObject.transform.SetParent(transform, false);
            panel = panelObject.AddComponent<TutorialDialoguePanel>();
            if (bootstrap == null)
                foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                {
                    bootstrap = root.GetComponentInChildren<HeightMapPlayerSceneBootstrap>();
                    if (bootstrap != null) break;
                }
            RefreshPanel();
        }

        private void LateUpdate()
        {
            if (panel == null || progress == null || !ownsPresentation) return;
            bool modal = AnimalGame.MainUI.MainUI.BlocksGameplay
                || (bootstrap != null && bootstrap.photoMode != null && bootstrap.photoMode.IsActive);
            panel.SetVisible(!modal);
            if (modal || Time.deltaTime <= 0f || bootstrap == null || bootstrap.robot == null
                || bootstrap.map == null || !bootstrap.map.HasGeneratedMap)
            {
                progress.Suspend();
                return;
            }

#if ENABLE_INPUT_SYSTEM
            // The D-pad is only used here while the inventory is closed; A keeps its world interaction role.
            if (Gamepad.current != null && (Gamepad.current.dpad.left.wasPressedThisFrame
                || Gamepad.current.dpad.right.wasPressedThisFrame))
                panel.ToggleCollapsed();
#endif
            RobotMover robot = bootstrap.robot;
            bool valid = robot.isActiveAndEnabled && !robot.IsMovementLocked
                && !robot.IsArmInputCaptured && !robot.IsPhotoModeInputLocked
                && !robot.IsLevelThreeUnstable && !robot.IsAutoAligningDownhill
                && (bootstrap.tumble == null || bootstrap.tumble.State == RobotTumbleState.Upright);
            bool onMap = bootstrap.map.TrySampleWorldPosition(robot.transform.position,
                out Vector2 mapPosition, out _);
            float worldPerMeter = bootstrap.map.MapMetersToWorldDistance(robot.Forward, 1f);
            float speedMeters = robot.CurrentSpeed / Mathf.Max(.0001f, worldPerMeter);
            progress.Sample(new DrivingTutorialSample(mapPosition,
                bootstrap.map.WorldDirectionToMapDirection(robot.Forward), robot.transform.eulerAngles.z,
                speedMeters, robot.CurrentThrottleIntent, robot.CurrentTurnSpeed, Time.deltaTime,
                valid && onMap));

            if (progress.IsStepSatisfied)
            {
                lastFeedback = GetCompletionFeedback(progress.Step);
                progress.Advance();
                feedbackRemaining = .9f;
                panel.SetCollapsed(false);
                refreshRemaining = 0f;
            }
            feedbackRemaining = Mathf.Max(0f, feedbackRemaining - Time.deltaTime);
            refreshRemaining -= Time.unscaledDeltaTime;
            if (refreshRemaining <= 0f)
            {
                RefreshPanel(valid);
                refreshRemaining = .08f;
            }
        }

        private void OnDisable()
        {
            progress?.Suspend();
            if (panel != null && ownsPresentation) panel.SetVisible(false);
        }

        [ContextMenu("Restart Driving Lesson")]
        public void RestartLesson()
        {
            if (progress == null) return;
            ownsPresentation = true;
            progress.Reset();
            feedbackRemaining = 0f;
            if (panel != null) panel.SetCollapsed(false);
            RefreshPanel();
        }

        private DrivingLessonText Text => tutorialText != null && tutorialText.Driving != null
            ? tutorialText.Driving : defaultText;

        private TutorialTextTokens CreateTextTokens()
        {
            string rt = "RT", lt = "LT", lb = "LB";
#if ENABLE_INPUT_SYSTEM
            if (Gamepad.current != null && Gamepad.current.layout.Contains("Dual"))
            { rt = "R2"; lt = "L2"; lb = "L1"; }
#endif
            return new TutorialTextTokens().Add("rt", rt).Add("lt", lt).Add("lb", lb)
                .Add("value", progress.Value).Add("target", progress.Target)
                .Add("movingDistance", progress.MovingTurnDistanceMeters)
                .Add("movingTarget", progress.MovingTurnTargetMeters);
        }

        private string GetCompletionFeedback(DrivingTutorialStep step) =>
            CreateTextTokens().Format((Text.GetStep(step) ?? defaultText.GetStep(step)).CompleteFeedback);

        private void RefreshPanel(bool canDrive = true)
        {
            if (panel == null || progress == null) return;
            DrivingLessonText text = Text;
            TutorialStepText stepText = text.GetStep(progress.Step) ?? defaultText.GetStep(progress.Step);
            TutorialTextTokens tokens = CreateTextTokens();
            string status = progress.IsComplete ? text.CompleteStatus
                : !canDrive ? text.BlockedStatus
                : feedbackRemaining > 0f ? lastFeedback
                : text.WaitingStatus;
            panel.SetPanelText(tutorialText != null ? tutorialText.Panel : defaultPanelText);
            panel.SetContent(tokens.Format(text.Title), tokens.Format(stepText.Message),
                tokens.Format(stepText.ControlGlyph), tokens.Format(stepText.Action),
                tokens.Format(stepText.Detail), tokens.Format(status), 1, 8);
            panel.SetProgress(progress.NormalizedProgress);
            panel.SetSuccess(progress.IsComplete || feedbackRemaining > 0f);
        }
    }
}
