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
        public BalanceLessonText Balance = new BalanceLessonText();
        public ArmsLessonText Arms = new ArmsLessonText();
        public DiscoveryLessonText Discovery = new DiscoveryLessonText();
        public PhotographyLessonText Photography = new PhotographyLessonText();
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

    [Serializable]
    public sealed class BalanceLessonText
    {
        public string Title = "Balance";
        public TutorialStepText ReadIndicator = new TutorialStepText("·", "Read the dot",
            "The dot shows your\nbalance point.\nThe ring marks your\nsupport area.",
            "Read {value:F1} / {target:F0} s");
        public TutorialStepText AdjustWeight = new TutorialStepText("R", "Shift weight",
            "Move the right stick.\nWatch the dot move\nas you shift weight.",
            "Move the balance point.");
        public TutorialStepText ReadRisk = new TutorialStepText("·", "Support ring",
            "Keep the point inside\nthe support ring.\nIf it moves outside,\nthe robot will tip.",
            "Read {value:F1} / {target:F0} s");
        public TutorialStepText Completed = new TutorialStepText("✓", "Ready",
            "Balance ready.\nUse the right stick to\nadjust your balance.",
            "Keep the point inside.");
        public string WaitingStatus = "Waiting";
        public string ReadingStatus = "Read guide";
        public string BlockedStatus = "Resume lesson";
        public string NeedStowStatus = "Retract arms first";
        public string NeutralStatus = "Center right stick";
        public string StopStatus = "Stop to continue";
        public string CompleteStatus = "Lesson complete";
        public string ToggleHint = "R · Balance";
        [TextArea(2, 4)] public string NeedStowMessage = "Press L3 to retract\nyour arms and return\nto driving controls.";
        [TextArea(2, 4)] public string NeutralMessage = "Center the right stick.\nThen move it again to\nshift your balance.";
        [TextArea(2, 4)] public string StopMessage = "Release the triggers.\nStop to continue\nthis balance lesson.";

        public TutorialStepText GetStep(BalanceTutorialStep step) => step switch
        {
            BalanceTutorialStep.ReadIndicator => ReadIndicator,
            BalanceTutorialStep.AdjustWeight => AdjustWeight,
            BalanceTutorialStep.ReadRisk => ReadRisk,
            _ => Completed
        };
    }

    [Serializable]
    public sealed class ArmsLessonText
    {
        public string Title = "Mechanical Arms";
        public TutorialStepText Deploy = new TutorialStepText("L3", "Deploy arms",
            "Click the left stick (L3)\nto deploy both arms.\nL3 also retracts them.",
            "Press once, then release.");
        public TutorialStepText Extend = new TutorialStepText("L ↑", "Extend reach",
            "Push the left stick up.\nMove the empty hands\nfarther from the body.",
            "Out {value:F0} / {target:F0} cm");
        public TutorialStepText RetractReach = new TutorialStepText("L ↓", "Shorten reach",
            "Push the left stick down.\nBring the empty hands\ncloser to the body.",
            "In {value:F0} / {target:F0} cm");
        public TutorialStepText SwingLeft = new TutorialStepText("L ←", "Swing left",
            "Push the left stick left.\nSwing the empty hands\ntoward the left side.",
            "Left {value:F0}° / {target:F0}°");
        public TutorialStepText SwingRight = new TutorialStepText("L →", "Swing right",
            "Push the left stick right.\nSwing the empty hands\ntoward the right side.",
            "Right {value:F0}° / {target:F0}°");
        public TutorialStepText HoldStill = new TutorialStepText("L", "Hold position",
            "Center the left stick.\nThe hands stay where\nyou leave them.\nKeep them still.",
            "Still {value:F1} / {target:F1} s");
        public TutorialStepText Stow = new TutorialStepText("L3", "Retract arms",
            "Click the left stick (L3)\nto retract both arms.\nThis restores driving.",
            "Press once, then release.");
        public TutorialStepText Completed = new TutorialStepText("✓", "Arms complete",
            "Arm controls complete.\nYou are ready to scan\nwildlife and objects.",
            "Arms safely retracted.");
        public string WaitingStatus = "Waiting";
        public string BlockedStatus = "Return to arm practice";
        public string ReopenStatus = "Deploy arms to continue";
        public string EmptyHandsStatus = "Put down held items";
        public string CompleteStatus = "Lesson complete";
        public string ToggleHint = "L3 · Deploy / Retract";
        [TextArea(2, 4)] public string ReopenMessage = "Press L3 to deploy your\narms and continue.";
        [TextArea(3, 5)] public string InitialStowMessage = "Press L3 to retract your\narms, then press again\nto deploy.";
        public string InitialStowAction = "Retract first";
        [TextArea(3, 5)] public string StowRecoveryMessage = "Press L3 to deploy your\narms, then press again\nto retract.";

        public TutorialStepText GetStep(ArmTutorialStep step) => step switch
        {
            ArmTutorialStep.Deploy => Deploy,
            ArmTutorialStep.Extend => Extend,
            ArmTutorialStep.RetractReach => RetractReach,
            ArmTutorialStep.SwingLeft => SwingLeft,
            ArmTutorialStep.SwingRight => SwingRight,
            ArmTutorialStep.HoldStill => HoldStill,
            ArmTutorialStep.Stow => Stow,
            _ => Completed
        };
    }

    [Serializable]
    public sealed class DiscoveryLessonText
    {
        public string Title = "Discovery Scan";
        public TutorialStepText ApproachAnimal = new TutorialStepText("LS", "Follow marker",
            "Follow the marker.\nScan again when nearby.\nLook for the woodpecker\non dry ground.",
            "Stay on dry ground.");
        public TutorialStepText Charge = new TutorialStepText("{lb}", "Charge scan",
            "Hold the left bumper.\nKeep holding to charge\na discovery scan.",
            "Charge {value:F0}%");
        public TutorialStepText Release = new TutorialStepText("{lb}", "Release to scan",
            "The scan is ready.\nRelease the left bumper\nto send the pulse.",
            "Release {lb} to scan");
        public TutorialStepText FindAnimal = new TutorialStepText("✧", "Find bird",
            "A hollow star marks\nwildlife not yet seen.\nFind the woodpecker.",
            "Hollow star · Unseen");
        public TutorialStepText ConfirmAnimal = new TutorialStepText("✦", "See bird",
            "Bring the woodpecker\ninside your view.\nIts star becomes solid\nwhen you see it.",
            "Solid star · Seen");
        public TutorialStepText ReadObject = new TutorialStepText("///", "Inspect object",
            "Stripes and diamonds\nmark objects you can\ninteract with. Some need\nmore than recycling.",
            "Striped mark · Interact");
        public TutorialStepText Rescan = new TutorialStepText("{lb}", "Scan again",
            "Marks fade over time.\nHold the left bumper.\nRelease at full charge\nto refresh nearby marks.",
            "Scans {value:F0} / {target:F0}");
        public TutorialStepText Completed = new TutorialStepText("✓", "Scan complete",
            "Scan complete.\nYou can now find wildlife\nand useful objects.",
            "Keep exploring freely.");
        public string WaitingStatus = "Waiting";
        public string CancelledStatus = "Scan cancelled";
        public string WrongGestureStatus = "Hold for discovery scan";
        public string NeedRescanStatus = "Scan again to continue";
        public string NeedEmptyHandsStatus = "Retract arms to scan";
        public string BlockedStatus = "Return to scan practice";
        public string CompleteStatus = "Lesson complete";
        public string PreparingStatus = "Preparing nearby targets";
        public string ToggleHint = "Hold {lb} · Charge / Release";
        [TextArea(3, 5)] public string NeedRescanMessage = "The scan marks faded.\nHold the left bumper,\nthen release when full\nto scan again.";
        [TextArea(2, 4)] public string NeedStowMessage = "Press L3 to retract your\narms before scanning.";

        public TutorialStepText GetStep(DiscoveryTutorialStep step) => step switch
        {
            DiscoveryTutorialStep.Charge => Charge,
            DiscoveryTutorialStep.Release => Release,
            DiscoveryTutorialStep.FindAnimal => FindAnimal,
            DiscoveryTutorialStep.ConfirmAnimal => ConfirmAnimal,
            DiscoveryTutorialStep.ReadObject => ReadObject,
            DiscoveryTutorialStep.Rescan => Rescan,
            _ => Completed
        };
    }

    [Serializable]
    public sealed class PhotographyLessonText
    {
        public string Title = "Photo Practice";
        public TutorialStepText ObserveMovement = new TutorialStepText("✦", "Observe bird",
            "Keep the woodpecker\ninside your view.\nPrepare to take a photo.",
            "Keep the bird in view.");
        public TutorialStepText EnterPhoto = new TutorialStepText("{photo}", "Enter photo",
            "Press {photo} to enter\nphoto mode and open\nthe camera view.",
            "Press once, then release.");
        public TutorialStepText FrameSubject = new TutorialStepText("L", "Frame bird",
            "Use the triggers to move.\nSteer with the left stick.\nBring the bird into\nthe camera frame.",
            "{rt} / {lt} move · L steer");
        public TutorialStepText Capture = new TutorialStepText("{rb}", "Hold to capture",
            "Hold the right bumper.\nKeep the bird framed\nuntil the photo is taken.",
            "Hold {rb} until captured.");
        public TutorialStepText SavePhoto = new TutorialStepText("{save}", "Save photo",
            "Save the bird photo.\nPress {save} on the result\nscreen to add it to\nyour album.",
            "{save} · Save to album");
        public TutorialStepText ExitPhoto = new TutorialStepText("{cancel}", "Resume driving",
            "Photo saved.\nPress {cancel} to leave\nphoto mode and return\nto driving.",
            "{cancel} · Back");
        public TutorialStepText Completed = new TutorialStepText("✓", "Photo complete",
            "Photo saved.\nYou can now photograph\nwildlife as you explore.",
            "Keep exploring freely.");
        public string WaitingStatus = "Waiting";
        public string BlockedStatus = "Return to photo practice";
        public string TargetMissingStatus = "Follow the bird marker";
        public string NeedStowStatus = "Retract arms first";
        public string NeedRescanStatus = "Scan again to find it";
        public string RetryPhotoStatus = "Photograph the bird";
        public string SavedStatus = "Photo saved to album";
        public string CompleteStatus = "Lesson complete";
        public string ToggleHint = "{photo} · Photo / {cancel} · Back";
        [TextArea(3, 5)] public string TargetMissingMessage = "Follow the gold marker.\nLook for the woodpecker\nnearby on dry ground.";
        [TextArea(2, 4)] public string NeedStowMessage = "Press L3 to retract\nyour arms before\nentering photo mode.";
        [TextArea(3, 5)] public string NeedRescanMessage = "The star marker faded.\nHold the left bumper,\nthen release at full\ncharge to find it again.";
        [TextArea(3, 5)] public string RetryPhotoMessage = "The woodpecker was not\nrecorded in that photo.\nBring it into the frame\nand hold {rb} to try again.";

        public TutorialStepText GetStep(PhotographyTutorialStep step) => step switch
        {
            PhotographyTutorialStep.ObserveMovement => ObserveMovement,
            PhotographyTutorialStep.EnterPhoto => EnterPhoto,
            PhotographyTutorialStep.FrameSubject => FrameSubject,
            PhotographyTutorialStep.Capture => Capture,
            PhotographyTutorialStep.SavePhoto => SavePhoto,
            PhotographyTutorialStep.ExitPhoto => ExitPhoto,
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
