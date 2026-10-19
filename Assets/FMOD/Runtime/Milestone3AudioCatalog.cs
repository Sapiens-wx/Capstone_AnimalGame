using System;

namespace Capstone.Audio
{
    internal enum Milestone3Cue
    {
        TumbleStart, TumbleMove, TumbleImpact, RockImpact, RecoveredUpright,
        SettledFallen, SupportStart, SupportLoad, ForceAccepted, ForceRejected,
        PullBalance, SupportFailed, RightingChassis, RightingLand,
        PushStart, PushLoop, RecycleSmall, RecycleMediumStep, RecycleComplete,
        TumbleImpact02, TumbleImpact03, Count
    }

    internal enum Milestone3Parameter { Speed, Progress, Effort, Impact, Count }

    public static class Milestone3AudioCatalog
    {
        public const string SourceFolder = "FMOD/SourceAudio/Milestone3/PlayerActions";
        public const string BankName = "RobotFoley";
        public const string RecycleHoldParameter = "RecycleHold";
        public const int RecycleClosePositionMs = 800;
        public const int CueCount = (int)Milestone3Cue.Count;

        private static readonly string[] Paths =
        {
            "event:/Robot/Balance/TumbleStart", "event:/Robot/Balance/TumbleMove",
            "event:/Robot/Balance/TumbleImpact", "event:/Robot/Balance/RockImpact",
            "event:/Robot/Balance/RecoveredUpright", "event:/Robot/Balance/SettledFallen",
            "event:/Robot/Balance/SupportStart", "event:/Robot/Balance/SupportLoad",
            "event:/Robot/Balance/ForceAccepted", "event:/Robot/Balance/ForceRejected",
            "event:/Robot/Balance/PullBalance", "event:/Robot/Balance/SupportFailed",
            "event:/Robot/Balance/RightingChassis", "event:/Robot/Balance/RightingLand",
            "event:/Robot/Recycle/PushStart", "event:/Robot/Recycle/PushLoop",
            "event:/Robot/Recycle/Small", "event:/Robot/Recycle/Intake",
            "event:/Robot/Recycle/Complete", "event:/Robot/Balance/TumbleImpact02",
            "event:/Robot/Balance/TumbleImpact03"
        };

        private static readonly string[] Files =
        {
            null, "Balance/B02_Tumble_Move_Loop.wav",
            "Balance/B03_Tumble_Impact_01.wav", "Balance/B04_Tumble_Rock_Impact.wav",
            "Balance/B05_Tumble_Recovered.wav", "Balance/B06_Tumble_Settled.wav",
            "Balance/B07_Support_Start.wav", "Balance/B07_Support_Load_Loop.wav",
            "Balance/B08_Righting_Force.wav", "Balance/B09_Righting_Reject.wav",
            "Balance/B10_Righting_Pull_Loop.wav", "Balance/B11_Righting_Fail.wav",
            "Balance/B12_Righting_Chassis_Loop.wav", "Balance/B13_Righting_Land.wav",
            "Recycling/G01_Push_Start.wav", "Recycling/G01_Push_Loop.wav",
            "Recycling/G03_Recycle.wav", "Recycling/G03_Recycle.wav",
            "Recycling/G04_Recycle_Complete.wav", "Balance/B03_Tumble_Impact_02.wav",
            "Balance/B03_Tumble_Impact_03.wav"
        };

        private static readonly string[,] ParameterNames = CreateParameterNames();

        public static string EventPath(int index) => Paths[index];
        // Shared source files remain in their existing M2 delivery folders.
        // B13 also layers M08_Land_shatter in the Studio authoring definition.
        public static string SourceFile(int index)
        {
            switch ((Milestone3Cue)index)
            {
                case Milestone3Cue.TumbleStart:
                    return Milestone2ActionCatalog.SourceFolder + "/Movement/M06_Move_LoseGrip.wav";
                case Milestone3Cue.ForceAccepted:
                case Milestone3Cue.PullBalance:
                    return Milestone2ActionCatalog.SourceFolder + "/Arm/A02_Arm_Servo_Loop.wav";
                case Milestone3Cue.ForceRejected:
                    return Milestone2ActionCatalog.SourceFolder + "/Arm/A03_Arm_Blocked_01.wav";
                case Milestone3Cue.SupportFailed:
                    return Milestone2ActionCatalog.SourceFolder + "/Arm/A01_Arm_Deploy_Loop.wav";
                case Milestone3Cue.RightingLand:
                    return Milestone2ActionCatalog.SourceFolder + "/Movement/M08_Land_Impact.wav";
                default:
                    return SourceFolder + "/" + Files[index];
            }
        }
        // Keep the removed corner-impact slot stable for serialized settings.
        public static bool HasSeparateEvent(int index) => index != (int)Milestone3Cue.TumbleStart
            && index != (int)Milestone3Cue.TumbleImpact02 && index != (int)Milestone3Cue.RecycleSmall;
        public static bool HasObservableTrigger(int index) => HasSeparateEvent(index) && index != (int)Milestone3Cue.PushStart
            && index != (int)Milestone3Cue.PushLoop && index != (int)Milestone3Cue.RecycleComplete;
        public static bool IsLoop(int index) => index == (int)Milestone3Cue.TumbleMove
            || index == (int)Milestone3Cue.SupportLoad || index == (int)Milestone3Cue.PullBalance
            || index == (int)Milestone3Cue.RightingChassis || index == (int)Milestone3Cue.PushLoop
            || index == (int)Milestone3Cue.RecycleMediumStep;

        internal static string ParameterName(Milestone3Cue cue, Milestone3Parameter parameter) =>
            ParameterNames[(int)cue, (int)parameter];

        private static string[,] CreateParameterNames()
        {
            var names = new string[CueCount, (int)Milestone3Parameter.Count];
            for (int cue = 0; cue < CueCount; cue++)
                for (int parameter = 0; parameter < (int)Milestone3Parameter.Count; parameter++)
                    names[cue, parameter] = "M3_" + Paths[cue].Substring(Paths[cue].LastIndexOf('/') + 1)
                        + "_" + ((Milestone3Parameter)parameter).ToString();
            return names;
        }
    }
}
