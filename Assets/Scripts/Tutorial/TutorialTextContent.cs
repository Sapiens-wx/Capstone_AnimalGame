using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace AnimalGame.Tutorial
{
    /// <summary>Editable copy only. Changing this asset never changes tutorial rules or player controls.</summary>
    [CreateAssetMenu(fileName = "TutorialText", menuName = "AnimalGame/Tutorial/Tutorial Text Content")]
    public sealed class TutorialTextContent : ScriptableObject
    {
        public TutorialPanelText Panel = new TutorialPanelText();
        public DrivingLessonText Driving = new DrivingLessonText();
        public TerrainLessonText Terrain = new TerrainLessonText();
    }

    [Serializable]
    public sealed class TutorialPanelText
    {
        public string Header = "TUTORIAL";
        public string CurrentLesson = "CURRENT LESSON";
        public string CurrentAction = "CURRENT ACTION";
        public string CollapseHint = "Tap D-pad ← / →  Hide";
        public string ExpandHint = "Show";
        public string ExpandArrows = "← →";
        [TextArea(2, 6)] public string GuideLabel = "G\nU\nI\nD\nE";
        public string CollapseTooltip = "Tap D-pad Left / Right to hide";
        public string ExpandTooltip = "Tap D-pad Left / Right to show";
    }

    [Serializable]
    public sealed class TutorialStepText
    {
        public string ControlGlyph;
        public string Action;
        [TextArea(3, 8)] public string Message;
        [TextArea(2, 5)] public string Detail;
        public string CompleteFeedback;

        public TutorialStepText(string controlGlyph, string action, string message, string detail,
            string completeFeedback = "")
        {
            ControlGlyph = controlGlyph;
            Action = action;
            Message = message;
            Detail = detail;
            CompleteFeedback = completeFeedback;
        }
    }

    [Serializable]
    public sealed class DrivingLessonText
    {
        public string Title = "Driving Practice";
        public TutorialStepText TurnLeft = new TutorialStepText("L", "Turn left",
            "Push the left stick left.\nTurn on the spot or steer\nwhile driving. Both count.",
            "Left {value:F0}° / {target:F0}°", "Left turn complete");
        public TutorialStepText TurnRight = new TutorialStepText("L", "Turn right",
            "Push the left stick right.\nTurn on the spot or steer\nwhile driving. Both count.",
            "Right {value:F0}° / {target:F0}°", "Right turn complete");
        public TutorialStepText Forward = new TutorialStepText("{rt}", "Drive forward",
            "Hold the right trigger to drive forward. Release it when you reach the target distance.",
            "Forward {value:F1} / {target:F1} m", "Forward complete");
        public TutorialStepText Reverse = new TutorialStepText("{lt}", "Reverse",
            "Center the left stick. Hold the left trigger to reverse, then release it at the target distance.",
            "Reverse {value:F1} / {target:F1} m", "Reverse complete");
        public TutorialStepText SteerWhileMoving = new TutorialStepText("{rt}+L", "Drive and steer",
            "Hold the right trigger and push the left stick left or right. Keep driving as you turn.",
            "Turn {value:F0}° / {target:F0}°\nDrive {movingDistance:F1} / {movingTarget:F1} m", "Moving turn complete");
        public TutorialStepText Stop = new TutorialStepText("·", "Release inputs",
            "Release both triggers and center the left stick. Let the robot come to a complete stop.",
            "Stopped {value:F1} / {target:F1} s", "Stop complete");
        public TutorialStepText Completed = new TutorialStepText("✓", "Lesson complete",
            "Great work! You can now turn, drive forward, reverse, steer while moving and stop.",
            "Keep practicing freely.");
        public string WaitingStatus = "Waiting";
        public string BlockedStatus = "Return to driving mode";
        public string CompleteStatus = "Lesson complete";

        public TutorialStepText GetStep(DrivingTutorialStep step) => step switch
        {
            DrivingTutorialStep.TurnLeft => TurnLeft,
            DrivingTutorialStep.TurnRight => TurnRight,
            DrivingTutorialStep.Forward => Forward,
            DrivingTutorialStep.Reverse => Reverse,
            DrivingTutorialStep.SteerWhileMoving => SteerWhileMoving,
            DrivingTutorialStep.Stop => Stop,
            _ => Completed
        };
    }

    [Serializable]
    public sealed class TerrainLessonText
    {
        public string Title = "Terrain & Slopes";
        public TutorialStepText ReachSurvey = new TutorialStepText("{rt}+L", "Follow target",
            "Follow the gold triangle to the practice area. Distant goals are marked along the circular rim.",
            "Destination {distance:F1} m\n{rt} drive · L steer");
        public TutorialStepText ScanTerrain = new TutorialStepText("{lb}×2", "Scan terrain",
            "Stop here. Quickly press and release the left bumper twice to scan the terrain around you.",
            "Two short taps\nRelease between taps");
        public TutorialStepText ReadSigns = new TutorialStepText("+/×", "Read signs",
            "+ marks a safe approach.\nX warns of danger.\nSigns change with your\nposition and approach.",
            "+ Passable   × Unsafe\nReading {reading:F1} / {readingTarget:F0} s");
        public TutorialStepText FollowRoute = new TutorialStepText("{rt}+L", "Climb the slope",
            "Follow the marker uphill.\nSteeper slopes slow you\nand can make you slip.\nScan again as you move.",
            "Ground slope {slope:F0}°\nDestination {distance:F1} m");
        public TutorialStepText Completed = new TutorialStepText("✓", "Lesson complete",
            "Scan terrain to find a\nsafer approach. Choose\ngentle slopes and scan\nagain as you move.",
            "Destination reached\nKeep exploring freely.");
        public string FollowMarkerStatus = "Follow the gold marker";
        public string WaitingScanStatus = "Waiting for terrain scan";
        public string WaitingMarkersStatus = "Waiting for scan markers";
        public string StopToReadStatus = "Stop to read the signs";
        public string ObserveStatus = "Observe the scan signs";
        public string BlockedStatus = "Return to driving mode";
        public string CompleteStatus = "Lesson complete";

        public TutorialStepText GetStep(TerrainTutorialStep step) => step switch
        {
            TerrainTutorialStep.ReachSurvey => ReachSurvey,
            TerrainTutorialStep.ScanTerrain => ScanTerrain,
            TerrainTutorialStep.ReadSigns => ReadSigns,
            TerrainTutorialStep.FollowRoute => FollowRoute,
            _ => Completed
        };
    }

    /// <summary>Named placeholders stay readable, including invalid ones, so typos cannot break a lesson.</summary>
    public sealed class TutorialTextTokens
    {
        private static readonly Regex Token = new Regex(@"\{(?<name>[A-Za-z][A-Za-z0-9]*)(?::(?<format>[^{}]+))?\}");
        private readonly Dictionary<string, object> values = new Dictionary<string, object>(StringComparer.Ordinal);

        public TutorialTextTokens Add(string name, object value)
        {
            values[name] = value;
            return this;
        }

        public string Format(string template) => string.IsNullOrEmpty(template) ? string.Empty : Token.Replace(template, match =>
        {
            if (!values.TryGetValue(match.Groups["name"].Value, out object value)) return match.Value;
            string format = match.Groups["format"].Success ? match.Groups["format"].Value : null;
            try
            {
                // A format on a button name is a typo as well; leave the placeholder visible for the author.
                if (format != null && !(value is IFormattable)) return match.Value;
                return value is IFormattable number
                    ? number.ToString(format, CultureInfo.InvariantCulture) : value?.ToString() ?? string.Empty;
            }
            catch (FormatException) { return match.Value; }
        });
    }
}
