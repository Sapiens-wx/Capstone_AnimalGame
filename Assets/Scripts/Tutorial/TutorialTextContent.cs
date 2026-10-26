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
        public RecyclingLessonText Recycling = new RecyclingLessonText();
        public PhotographyLessonText Photography = new PhotographyLessonText();
        public RefitLessonText Refit = new RefitLessonText();
        public TipOverLessonText TipOver = new TipOverLessonText();
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
        public string ContinuePrompt = "Press A to continue";
        public string ContinueWaitingPrompt = "Complete this step to continue";
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
        // Legacy serialized copy is retained; terminal states no longer display a tutorial card.
        [HideInInspector] public TutorialStepText Completed = new TutorialStepText("✓", "Lesson complete",
            "Great work! You can now turn, drive forward, reverse and steer while moving.",
            "Keep practicing freely.");
        public string WaitingStatus = "Waiting";
        public string BlockedStatus = "Return to driving mode";
        [HideInInspector] public string CompleteStatus = "Lesson complete";
        [HideInInspector] public string NeedStowStatus = "Retract arms first";
        [HideInInspector, TextArea(3, 5)] public string NeedStowMessage = "Press L3 to retract\nyour mechanical arms,\nthen resume driving.";

        public TutorialStepText GetStep(DrivingTutorialStep step) => step switch
        {
            DrivingTutorialStep.TurnLeft => TurnLeft,
            DrivingTutorialStep.TurnRight => TurnRight,
            DrivingTutorialStep.Forward => Forward,
            DrivingTutorialStep.Reverse => Reverse,
            DrivingTutorialStep.SteerWhileMoving => SteerWhileMoving,
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
            "Quickly press and release the left bumper twice to scan the terrain around you.",
            "Two short taps\nRelease between taps");
        public TutorialStepText ReadSigns = new TutorialStepText("+/×", "Read signs",
            "+ marks a safe approach.\nX warns of danger.\nSigns change with your\nposition and approach.",
            "+ Passable   × Unsafe\nReading {reading:F1} / {readingTarget:F0} s");
        public TutorialStepText FollowRoute = new TutorialStepText("{rt}+L", "Climb the slope",
            "Follow the marker uphill.\nSteeper slopes slow you\nand can make you slip.\nScan again as you move.",
            "Ground slope {slope:F0}°\nDestination {distance:F1} m");
        [HideInInspector] public TutorialStepText Completed = new TutorialStepText("✓", "Lesson complete",
            "Scan terrain to find a\nsafer approach. Choose\ngentle slopes and scan\nagain as you move.",
            "Destination reached\nKeep exploring freely.");
        public string FollowMarkerStatus = "Follow the gold marker";
        public string WaitingScanStatus = "Waiting for terrain scan";
        public string WaitingMarkersStatus = "Waiting for scan markers";
        [HideInInspector] public string StopToReadStatus = "Stop to read the signs";
        public string ObserveStatus = "Observe the scan signs";
        public string BlockedStatus = "Return to driving mode";
        [HideInInspector] public string CompleteStatus = "Lesson complete";
        [HideInInspector] public string NeedStowStatus = "Retract arms first";
        [HideInInspector, TextArea(3, 5)] public string NeedStowMessage = "Press L3 to retract\nyour mechanical arms,\nthen continue the route.";

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
        [HideInInspector] public TutorialStepText Completed = new TutorialStepText("✓", "Ready",
            "Balance ready.\nUse the right stick to\nadjust your balance.",
            "Keep the point inside.");
        public string WaitingStatus = "Waiting";
        public string ReadingStatus = "Read guide";
        public string BlockedStatus = "Resume lesson";
        [HideInInspector] public string NeedStowStatus = "Retract arms first";
        public string NeutralStatus = "Center right stick";
        [HideInInspector] public string StopStatus = "Stop to continue";
        [HideInInspector] public string CompleteStatus = "Lesson complete";
        public string ToggleHint = "R · Balance";
        [HideInInspector, TextArea(2, 4)] public string NeedStowMessage = "Press L3 to retract\nyour arms and return\nto driving controls.";
        [TextArea(2, 4)] public string NeutralMessage = "Center the right stick.\nThen move it again to\nshift your balance.";
        [HideInInspector, TextArea(2, 4)] public string StopMessage = "Release the triggers.\nStop to continue\nthis balance lesson.";

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
            "Push the left stick up.\nMove the hands\nfarther from the body.",
            "Out {value:F0} / {target:F0} cm");
        public TutorialStepText RetractReach = new TutorialStepText("L ↓", "Shorten reach",
            "Push the left stick down.\nBring the hands\ncloser to the body.",
            "In {value:F0} / {target:F0} cm");
        public TutorialStepText SwingLeft = new TutorialStepText("L ←", "Swing left",
            "Push the left stick left.\nSwing the hands\ntoward the left side.",
            "Left {value:F0}° / {target:F0}°");
        public TutorialStepText SwingRight = new TutorialStepText("L →", "Swing right",
            "Push the left stick right.\nSwing the hands\ntoward the right side.",
            "Right {value:F0}° / {target:F0}°");
        public TutorialStepText Stow = new TutorialStepText("L3", "Retract arms",
            "Click the left stick (L3)\nto retract both arms.\nThis restores driving.",
            "Press once, then release.");
        [HideInInspector] public TutorialStepText Completed = new TutorialStepText("✓", "Arms complete",
            "Arm controls complete.\nYou are ready to scan\nwildlife and objects.",
            "Arms safely retracted.");
        public string WaitingStatus = "Waiting";
        public string BlockedStatus = "Return to arm practice";
        public string ReopenStatus = "Deploy arms to continue";
        [HideInInspector] public string EmptyHandsStatus = "Put down held items";
        [HideInInspector] public string CompleteStatus = "Lesson complete";
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
            ArmTutorialStep.Stow => Stow,
            _ => Completed
        };
    }

    [Serializable]
    public sealed class DiscoveryLessonText
    {
        public string Title = "Discovery Scan";
        public TutorialStepText ApproachAnimal = new TutorialStepText("LS", "Follow marker",
            "Follow the marker.\nLook for the woodpecker\non the tree trunk.",
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
        // Retained for old serialized text assets; this step is no longer part of the lesson.
        [HideInInspector] public TutorialStepText Rescan = new TutorialStepText("{lb}", "Charge scan",
            "Hold the left bumper.\nRelease at full charge\nto locate nearby targets.", "Release {lb} to scan");
        [HideInInspector] public TutorialStepText Completed = new TutorialStepText("✓", "Scan complete",
            "Scan complete.\nYou can now find wildlife\nand useful objects.",
            "Keep exploring freely.");
        public string WaitingStatus = "Waiting";
        public string CancelledStatus = "Scan cancelled";
        public string WrongGestureStatus = "Hold for discovery scan";
        public string NeedRescanStatus = "Locate the target";
        [HideInInspector] public string NeedEmptyHandsStatus = "Retract arms to scan";
        public string BlockedStatus = "Return to scan practice";
        [HideInInspector] public string CompleteStatus = "Lesson complete";
        public string PreparingStatus = "Preparing nearby targets";
        public string ToggleHint = "Hold {lb} · Charge / Release";
        [TextArea(3, 5)] public string NeedRescanMessage = "Hold the left bumper.\nRelease at full charge\nto locate the target.";
        [HideInInspector, TextArea(2, 4)] public string NeedStowMessage = "Press L3 to retract your\narms before scanning.";

        public TutorialStepText GetStep(DiscoveryTutorialStep step) => step switch
        {
            DiscoveryTutorialStep.Charge => Charge,
            DiscoveryTutorialStep.Release => Release,
            DiscoveryTutorialStep.FindAnimal => FindAnimal,
            DiscoveryTutorialStep.ConfirmAnimal => ConfirmAnimal,
            DiscoveryTutorialStep.Rescan => Rescan,
            _ => Completed
        };
    }

    [Serializable]
    public sealed class PhotographyLessonText
    {
        public string Title = "Photo Practice";
        public TutorialStepText ObserveMovement = new TutorialStepText("✦", "Observe bird",
            "Watch the woodpecker\npecking the tree trunk.\nPrepare to take a photo.",
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
        [HideInInspector] public TutorialStepText SavePhoto = new TutorialStepText("{save}", "Save photo",
            "Save the bird photo.\nPress {save} on the result\nscreen to add it to\nyour album.",
            "{save} · Save to album");
        [HideInInspector] public TutorialStepText ExitPhoto = new TutorialStepText("{cancel}", "Resume driving",
            "Photo saved.\nPress {cancel} to leave\nphoto mode and return\nto driving.",
            "{cancel} · Back");
        [HideInInspector] public TutorialStepText Completed = new TutorialStepText("✓", "Photo complete",
            "Photo saved.\nYou can now photograph\nwildlife as you explore.",
            "Next · Waste recycling");
        public string WaitingStatus = "Waiting";
        public string BlockedStatus = "Return to photo practice";
        public string TargetMissingStatus = "Follow the bird marker";
        [HideInInspector] public string NeedStowStatus = "Retract arms first";
        public string NeedRescanStatus = "Locate the bird";
        public string RetryPhotoStatus = "Photograph the bird";
        public string SavedStatus = "Photo saved to album";
        [HideInInspector] public string CompleteStatus = "Lesson complete";
        public string ToggleHint = "{photo} · Photo / {cancel} · Back";
        public string CapturedStatus = "Woodpecker photographed";
        [TextArea(3, 5)] public string TargetMissingMessage = "Follow the gold marker.\nLook for the woodpecker\non the tree trunk.";
        [HideInInspector, TextArea(2, 4)] public string NeedStowMessage = "Press L3 to retract\nyour arms before\nentering photo mode.";
        [TextArea(3, 5)] public string NeedRescanMessage = "Hold the left bumper.\nRelease at full charge\nto locate the bird.";
        [TextArea(3, 5)] public string CapturedMessage = "Woodpecker photographed.\nThe bird is flying away.\nContinue exploring.";
        [HideInInspector, TextArea(3, 5)] public string RecoverySaveMessage = "Your captured photo\nis ready. Press {save}\nto save it to your album.";
        [TextArea(3, 5)] public string RetryPhotoMessage = "The woodpecker was not\nrecorded in that photo.\nBring it into the frame\nand hold {rb} to try again.";

        public TutorialStepText GetStep(PhotographyTutorialStep step) => step switch
        {
            PhotographyTutorialStep.ObserveMovement => ObserveMovement,
            PhotographyTutorialStep.EnterPhoto => EnterPhoto,
            PhotographyTutorialStep.FrameSubject => FrameSubject,
            PhotographyTutorialStep.Capture => Capture,

            _ => Completed
        };
    }

    [Serializable]
    public sealed class RecyclingLessonText
    {
        public string Title = "Waste Recycling";
        public TutorialStepText ReachWaste = new TutorialStepText("L", "Find the waste",
            "The marked waste\ncan be recycled.\nMove closer to\nreach it with your arms.",
            "Waste {distance:F1} m");
        public TutorialStepText DeployArms = new TutorialStepText("L3", "Deploy arms",
            "Press L3 to deploy\nyour mechanical arms.\nUse the left stick\nto move the hands.",
            "Press once, then release.");
        public TutorialStepText RecycleWaste = new TutorialStepText("{confirm}", "Grip and recycle",
            "Move a hand to waste.\nHold {confirm} to grip it.\nKeep holding. Pull\nthe left stick back.",
            "Hold {confirm} · Keep grip");
        public TutorialStepText ReachLargeWaste = new TutorialStepText("L", "Find large waste",
            "Follow the marker.\nL3 can stow the arms\nfor normal steering.\nThis pile needs pulling.",
            "Approach {distance:F1} m");
        public TutorialStepText RecycleLargeWaste = new TutorialStepText("{lt}", "Pull and recycle",
            "Move both hands to\nthe large pile. Hold A,\nthen hold {lt} to pull\nit apart.",
            "A · Grip / {lt} · Reverse");
        [TextArea(3, 5)] public string LargePullMessage = "Keep holding A.\nHold {lt} to reverse.\nKeep pulling until\nthe pile breaks apart.";
        public string LargePullAction = "Pull apart";
        public string LargePullDetail = "Pull {pull:F0}%";
        public string LargePullStatus = "Hold A + {lt}";
        public string LargeToggleHint = "A · Grip / {lt} · Pull";
        public string FragmentAction = "Recycle fragment";
        [TextArea(3, 5)] public string FragmentHoldingMessage = "A piece is in your hands.\nRelease {lt}. Keep A held.\nPull the left stick back\nto the yellow inlet.";
        [TextArea(3, 5)] public string FragmentDroppedMessage = "Hold A to grip the piece.\nPull the left stick back.\nRelease A at the yellow\ninlet to recycle it.";
        [TextArea(3, 5)] public string LargeRecycledMessage = "Large waste cleared.\nMaterials recovered.\nYou can recycle the\nremaining pieces too.";
        [TextArea(3, 5)] public string LargeReopenMessage = "Click L3 to deploy.\nMove both hands to\nthe large pile and hold A.\nHold {lt} to pull it apart.";
        public TutorialStepText StowArms = new TutorialStepText("L3", "Retract arms",
            "Waste recycled.\nPress L3 to retract\nyour mechanical arms\nand resume driving.",
            "Press once, then release.");
        public TutorialStepText OpenInventory = new TutorialStepText("{inventoryRight}", "Open inventory",
            "Recovered materials\nare stored in inventory.\nHold D-pad {inventoryRight}\nto open it.",
            "Hold {inventoryRight} to open.");
        public TutorialStepText ViewMaterials = new TutorialStepText("{inventoryLeft}", "Close inventory",
            "Recovered raw materials\nare stored here.\nProcessing sites turn them\ninto useful materials.",
            "Hold {inventoryLeft} / Tap {cancel} · Close");
        public string OpenInventoryHint = "Hold {inventoryRight} · Open inventory";
        public string InventoryToggleHint = "Hold {inventoryLeft} / Tap {cancel} · Close";
        public string MaterialsDetail = "{materials}";
        public string NoMaterialsDetail = "No materials in storage.";
        [HideInInspector] public TutorialStepText Completed = new TutorialStepText("✓", "Recycling ready",
            "Recycling complete.\nMaterials recovered.\nNext, upgrade your camera\nat the large machine.",
            "Next · Camera upgrade");
        public string WaitingStatus = "Follow the guide";
        public string BlockedStatus = "Resume recycling";
        [HideInInspector] public string StopStatus = "Stop beside the waste";
        public string PreparingStatus = "Preparing waste target";
        public string HoldingStatus = "Keep holding {confirm}";
        public string PullStatus = "Pull the left stick back";
        public string ReadyStatus = "Release {confirm} to recycle";
        public string ProcessingStatus = "Processing waste";
        public string RecycledStatus = "Waste recycled";
        public string DroppedStatus = "Pick up the waste again";
        [HideInInspector] public string CompleteStatus = "Lesson complete";
        public string TargetMissingStatus = "Restoring waste target";
        public string ToggleHint = "L3 · Arms / {confirm} · Grip";
        [TextArea(3, 5)] public string HoldingMessage = "Keep holding {confirm}.\nPull the left stick back.\nAdjust left or right\nto line up the inlet.";
        [TextArea(3, 5)] public string ReadyMessage = "Waste is at the inlet.\nRelease {confirm}\nto begin recycling.";
        [TextArea(3, 5)] public string ProcessingMessage = "The waste is processing.\nWait until recycling\nis complete.";
        [TextArea(3, 5)] public string RecycledMessage = "Waste recycled.\nMaterials recovered.\nRelease {confirm} before\ncontinuing the lesson.";
        [TextArea(3, 5)] public string DroppedMessage = "The waste was dropped.\nMove a hand to it\nand hold {confirm}\nto pick it up again.";
        [TextArea(3, 5)] public string TargetMissingMessage = "The teaching waste\nis being restored.\nFollow its marker\nwhen it returns.";
        [TextArea(3, 5)] public string ReopenMessage = "Press L3 to deploy\nyour arms again.\nMove a hand to the waste\nand hold {confirm} to grip.";
        [TextArea(3, 5)] public string StowRecoveryMessage = "Press L3 to deploy,\nthen press L3 again\nto retract your arms.";
        public TutorialStepText GetStep(RecyclingTutorialStep step) => step switch
        {
            RecyclingTutorialStep.ReachWaste => ReachWaste,
            RecyclingTutorialStep.DeployArms => DeployArms,
            RecyclingTutorialStep.RecycleWaste => RecycleWaste,
            RecyclingTutorialStep.ReachLargeWaste => ReachLargeWaste,
            RecyclingTutorialStep.RecycleLargeWaste => RecycleLargeWaste,
            RecyclingTutorialStep.StowArms => StowArms,
            RecyclingTutorialStep.OpenInventory => OpenInventory,
            RecyclingTutorialStep.ViewMaterials => ViewMaterials,
            _ => Completed
        };
    }

    [Serializable]
    public sealed class RefitLessonText
    {
        public string Title = "Camera Upgrade";
        public TutorialStepText ReachMachine = new TutorialStepText("L", "Find the machine",
            "Your camera is damaged.\nFollow the marker to\nthe large machine.\nApproach its entrance.",
            "Entrance {distance:F1} m");
        public TutorialStepText EnterMachine = new TutorialStepText("{rt}", "Drive inside",
            "Drive through the\nentrance to enter\nthe large machine.",
            "Keep the doorway clear.");
        public TutorialStepText ReachWorkbench = new TutorialStepText("L", "Find workbench",
            "Follow the marker to\nthe assembly station.\nMove beside the station\nto use it.",
            "Station {distance:F1} m");
        public TutorialStepText OpenWorkbench = new TutorialStepText("{confirm}", "Use workbench",
            "Open the station with {confirm}.\nChoose CAMERA, then\nselect Zoom Lens 300.\nPress {confirm} to equip it.",
            "{cancel} · Close after equipping");
        [HideInInspector] public TutorialStepText RefitIntroduction = new TutorialStepText("{confirm}", "Read refit guide",
            "Use this station to\nchange equipment.\nInstall a better camera\nfor clearer photographs.",
            "Choose a new lens.");
        public TutorialStepText EquipLens = new TutorialStepText("{confirm}", "Install better lens",
            "At the assembly station,\nchoose CAMERA and\ninstall Zoom Lens 300.\nClose the station with {cancel}.",
            "{lb}/{rb} · CAMERA\n← / → · Lens · {confirm} equip");
        [HideInInspector] public TutorialStepText CloseRefit = new TutorialStepText("{cancel}", "Return to driving",
            "New lens installed.\nPress {cancel} to close\nthe assembly station\nand resume driving.",
            "{cancel} · Back");
        public TutorialStepText NewLensControls = new TutorialStepText("{rb}", "Learn new shutter",
            "Hold {rb} to focus.\nWhen focused, release it.\nPress {rb} again to shoot.",
            "{photo} · Photo mode\nR · Move frame");
        [HideInInspector] public TutorialStepText Completed = new TutorialStepText("✓", "Camera upgraded",
            "Camera upgraded.\nUse your new lens\nto photograph wildlife\nas you explore.",
            "New lens ready.");
        public string WaitingStatus = "Follow the guide";
        public string PreparingStatus = "Finding assembly station";
        public string BlockedStatus = "Return to refit practice";
        [HideInInspector] public string StopStatus = "Stop beside the target";
        [HideInInspector] public string NeedStowStatus = "Retract arms first";
        public string InstalledStatus = "New lens installed";
        [HideInInspector] public string CompleteStatus = "Lesson complete";
        public string ToggleHint = "{rt}/{lt} · Drive / L · Steer";
        [HideInInspector] public string MenuTitle = "CAMERA UPGRADE";
        [HideInInspector, TextArea(2, 3)] public string MenuIntroductionMessage = "Use this station to change your equipment.\nInstall a better camera for clearer photographs.";
        [HideInInspector, TextArea(2, 3)] public string MenuEquipMessage = "Switch to CAMERA with {lb} / {rb}.\nChoose {lens} with D-pad ← / →.";
        [HideInInspector, TextArea(2, 3)] public string MenuInstalledMessage = "Your new lens is installed.\nRelease {confirm}, then press it again to continue.";
        [HideInInspector, TextArea(2, 3)] public string MenuCloseMessage = "The new lens is ready.\nPress {cancel} to close the assembly station.";
        [HideInInspector] public string MenuSlotHint = "{lb} / {rb} · Select CAMERA";
        [HideInInspector] public string MenuModuleHint = "D-pad ← / → · Select lens";
        [HideInInspector] public string MenuEquipHint = "{confirm} · Equip";
        [HideInInspector] public string MenuBackHint = "{cancel} · Back";
        [HideInInspector] public string MenuInspectHint = "{photo} · Inspect";
        [HideInInspector, TextArea(3, 5)] public string InstalledMessage = "Zoom Lens 300 installed.\nClose the station with {cancel}\nto resume the tutorial.";
        [HideInInspector, TextArea(2, 4)] public string NeedStowMessage = "Press L3 to retract\nyour arms before using\nthe assembly station.";

        public TutorialStepText GetStep(RefitTutorialStep step) => step switch
        {
            RefitTutorialStep.ReachMachine => ReachMachine,
            RefitTutorialStep.EnterMachine => EnterMachine,
            RefitTutorialStep.ReachWorkbench => ReachWorkbench,
            RefitTutorialStep.OpenWorkbench => OpenWorkbench,
            RefitTutorialStep.RefitIntroduction => RefitIntroduction,
            RefitTutorialStep.EquipLens => EquipLens,
            RefitTutorialStep.CloseRefit => CloseRefit,
            RefitTutorialStep.NewLensControls => NewLensControls,
            _ => Completed
        };
    }

    [Serializable]
    public sealed class TipOverLessonText
    {
        public string Title = "Tip-over Recovery";
        public string ContextLabel = "CONTEXT TUTORIAL";
        [TextArea(3, 5)] public string RollingMessage = "Your robot has tipped over.\nWait for it to\nstop rolling.";
        public string RollingAction = "Wait for the robot";
        public string RollingDetail = "Recovery starts when still.";
        public TutorialStepText Explanation = new TutorialStepText("{confirm}", "Learn to recover",
            "Your balance point left\nthe support area.\nUse your arms to help\nthe robot stand upright.",
            "Resume after recovery.");
        public TutorialStepText DeployArms = new TutorialStepText("L3", "Deploy arms",
            "Press L3 to deploy\nyour mechanical arms.\nThey can support\nthe fallen robot.",
            "Press once, then release.");
        public TutorialStepText Recover = new TutorialStepText("L+R", "Brace and recover",
            "~{cadence:F0} pushes per second.\nKeep L held to brace.\nPush R opposite the fall,\ntoward the robot center.\nRecenter R each time.",
            "Move yellow point inward.");
        [HideInInspector] public TutorialStepText Completed = new TutorialStepText("✓", "Robot stabilized",
            "The robot is upright\nand stable again.\nContinue your\nprevious tutorial.",
            "Your progress is retained.");
        [TextArea(3, 5)] public string SupportMessage = "Push L toward the bar\non the fallen side.\nKeep pushing so the arms\nbrace against it.";
        [TextArea(3, 5)] public string EffortMessage = "~{cadence:F0} pushes per second.\nKeep L held to brace.\nPush R opposite the fall,\ntoward the robot center.\nRecenter R each time.";
        [TextArea(3, 5)] public string RightingMessage = "The robot is lifting.\nGet ready to adjust\nyour balance point\nwhen it lands.";
        [TextArea(3, 5)] public string StabilizeMessage = "Use the right stick\nto keep the balance point\ninside the support area\nuntil the robot is stable.";
        [TextArea(3, 5)] public string ArmLostMessage = "Arm support was lost.\nPush L toward the bar\non the fallen side.\nKeep it held to brace.";
        [TextArea(3, 5)] public string CadenceLostMessage = "~{cadence:F0} pushes per second.\nKeep L held to brace.\nPush R opposite the fall,\ntoward the robot center.\nRecenter R each time.";
        [TextArea(3, 5)] public string DirectionLostMessage = "~{cadence:F0} pushes per second.\nKeep L held to brace.\nPush R from yellow point\ntoward robot center.\nRecenter R each time.";
        [TextArea(3, 5)] public string ReminderMessage = "~{cadence:F0} pushes per second.\nL3 deploys your arms.\nHold L toward support.\nPush R opposite the fall.\nRecenter R each time.";
        public string WaitingStatus = "Follow the recovery guide";
        public string SupportStatus = "Brace on fallen side";
        public string EffortStatus = "Push balance inward";
        public string RightingStatus = "Righting the robot";
        public string StabilizeStatus = "Keep the balance point inside";
        [HideInInspector] public string CompleteStatus = "Ready to resume";
        public string RetryStatus = "Try the recovery again";
        public string ToggleHint = "L · Brace / R · Push, recenter";
        [HideInInspector] public string ResumePrompt = "A · Resume tutorial";
        public string InertiaTitle = "Landing Recoil";
        [TextArea(3, 5)] public string InertiaMessage = "Standing up creates\nrecoil toward {inertia}.\nPush the right stick {counter}\nto stop another fall.";
        public string InertiaAction = "Counter the recoil";
        [TextArea(2, 3)] public string InertiaDetail = "R {counter} · Oppose the force\nKeep the dot inside.";
        public string InertiaStatus = "Slow motion · 5% speed";
        public string InertiaContinuePrompt = "A · Continue";

        public TutorialStepText GetStep(TipOverTutorialStep step) => step switch
        {
            TipOverTutorialStep.Explanation => Explanation,
            TipOverTutorialStep.DeployArms => DeployArms,
            TipOverTutorialStep.Recover => Recover,
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
