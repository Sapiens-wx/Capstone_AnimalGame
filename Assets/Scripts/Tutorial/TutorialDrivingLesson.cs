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
            if (!AnimalGame.AnimalDebug.AnimalDebugUI.BlocksOtherPanels && Gamepad.current != null && (Gamepad.current.dpad.left.wasPressedThisFrame
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
                lastFeedback = StepName(progress.Step) + " complete";
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

        private static string StepName(DrivingTutorialStep step) => step switch
        {
            DrivingTutorialStep.Forward => "Forward",
            DrivingTutorialStep.TurnLeft => "Left turn",
            DrivingTutorialStep.TurnRight => "Right turn",
            DrivingTutorialStep.SteerWhileMoving => "Moving turn",
            DrivingTutorialStep.Reverse => "Reverse",
            DrivingTutorialStep.Stop => "Stop",
            _ => "Driving"
        };

        private void RefreshPanel(bool canDrive = true)
        {
            if (panel == null || progress == null) return;
            string glyph, action, message, detail;
            string rt = "RT", lt = "LT";
#if ENABLE_INPUT_SYSTEM
            if (Gamepad.current != null && Gamepad.current.layout.Contains("Dual")) { rt = "R2"; lt = "L2"; }
#endif
            switch (progress.Step)
            {
                case DrivingTutorialStep.Forward:
                    glyph = rt; action = "Drive forward";
                    message = "Hold the right trigger to drive forward. Release it when you reach the target distance.";
                    detail = $"Forward {progress.Value:F1} / {progress.Target:F1} m";
                    break;
                case DrivingTutorialStep.TurnLeft:
                    glyph = "L"; action = "Turn left";
                    message = "Release both triggers and let the robot stop. Push the left stick left to turn in place.";
                    detail = $"Left {progress.Value:F0}° / {progress.Target:F0}°";
                    break;
                case DrivingTutorialStep.TurnRight:
                    glyph = "L"; action = "Turn right";
                    message = "Keep both triggers released. Push the left stick right to turn the other way.";
                    detail = $"Right {progress.Value:F0}° / {progress.Target:F0}°";
                    break;
                case DrivingTutorialStep.Reverse:
                    glyph = lt; action = "Reverse";
                    message = "Center the left stick. Hold the left trigger to reverse, then release it at the target distance.";
                    detail = $"Reverse {progress.Value:F1} / {progress.Target:F1} m";
                    break;
                case DrivingTutorialStep.SteerWhileMoving:
                    glyph = rt + "+L"; action = "Drive and steer";
                    message = "Hold the right trigger and push the left stick left or right. Keep driving as you turn.";
                    detail = $"Turn {progress.Value:F0}° / {progress.Target:F0}°\n"
                        + $"Drive {progress.MovingTurnDistanceMeters:F1} / {progress.MovingTurnTargetMeters:F1} m";
                    break;
                case DrivingTutorialStep.Stop:
                    glyph = "·"; action = "Release inputs";
                    message = "Release both triggers and center the left stick. Let the robot come to a complete stop.";
                    detail = $"Stopped {progress.Value:F1} / {progress.Target:F1} s";
                    break;
                default:
                    glyph = "✓"; action = "Lesson complete";
                    message = "Great work! You can now drive, turn in place, steer while moving, reverse and stop.";
                    detail = "Keep practicing freely.";
                    break;
            }
            string status = progress.IsComplete ? "Lesson complete"
                : !canDrive ? "Return to driving mode"
                : feedbackRemaining > 0f ? lastFeedback
                : "Waiting";
            panel.SetContent("Driving Practice", message, glyph, action, detail, status, 1, 8);
            panel.SetProgress(progress.NormalizedProgress);
            panel.SetSuccess(progress.IsComplete || feedbackRemaining > 0f);
        }
    }
}
