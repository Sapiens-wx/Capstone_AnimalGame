namespace Capstone.Audio
{
    internal enum Milestone2ActionCue
    {
        MoveStart, Drive, Coast, Stop, Brake, Turn, GripLoss, Slide, Airborne, Land,
        ArmDeploy, ArmDeployLock, ArmServo, ArmBlocked, HandClose, HandOpen,
        GrabSmall, GrabMedium, GrabLarge, Drop, ArmRetract, ArmRetractLock,
        GroundMove, GroundTurn, LandParts, DriveAcceleration, Count
    }

    internal enum ActionAudioParameter { Speed, Direction, Throttle, Load, Effort, Progress, Impact, Count }

    // One behaviour can share a loop or use a start/loop/end pair. No source WAV
    // is loaded from this catalog: delivery still goes through Studio and Banks.
    public static class Milestone2ActionCatalog
    {
        public const string SourceFolder = "FMOD/SourceAudio/Milestone2/PlayerActions";
        public const string BankName = "RobotActions";
        public const int CueCount = (int)Milestone2ActionCue.Count;

        public static string EventPath(int index)
        {
            switch ((Milestone2ActionCue)index)
            {
                case Milestone2ActionCue.MoveStart: return "event:/Robot/Move/Start";
                case Milestone2ActionCue.Drive: return "event:/Robot/Move/Drive";
                case Milestone2ActionCue.Coast: return "event:/Robot/Move/Coast";
                case Milestone2ActionCue.Stop: return "event:/Robot/Move/Stop";
                case Milestone2ActionCue.Brake: return "event:/Robot/Move/Brake";
                case Milestone2ActionCue.Turn: return "event:/Robot/Move/Turn";
                case Milestone2ActionCue.GripLoss: return "event:/Robot/Move/GripLoss";
                case Milestone2ActionCue.Slide: return "event:/Robot/Move/Slide";
                case Milestone2ActionCue.Airborne: return "event:/Robot/Move/Airborne";
                case Milestone2ActionCue.Land: return "event:/Robot/Move/Land";
                case Milestone2ActionCue.ArmDeploy: return "event:/Robot/Arm/Deploy";
                case Milestone2ActionCue.ArmDeployLock: return "event:/Robot/Arm/DeployLock";
                case Milestone2ActionCue.ArmServo: return "event:/Robot/Arm/Servo";
                case Milestone2ActionCue.ArmBlocked: return "event:/Robot/Arm/Blocked";
                case Milestone2ActionCue.HandClose: return "event:/Robot/Arm/HandClose";
                case Milestone2ActionCue.HandOpen: return "event:/Robot/Arm/HandOpen";
                case Milestone2ActionCue.GrabSmall: return "event:/Robot/Arm/GrabSmall";
                case Milestone2ActionCue.GrabMedium: return "event:/Robot/Arm/GrabMedium";
                case Milestone2ActionCue.GrabLarge: return "event:/Robot/Arm/GrabLarge";
                case Milestone2ActionCue.Drop: return "event:/Robot/Arm/Drop";
                case Milestone2ActionCue.ArmRetract: return "event:/Robot/Arm/Retract";
                case Milestone2ActionCue.ArmRetractLock: return "event:/Robot/Arm/RetractLock";
                case Milestone2ActionCue.GroundMove: return "event:/Robot/Move/Ground";
                case Milestone2ActionCue.GroundTurn: return "event:/Robot/Move/GroundTurn";
                case Milestone2ActionCue.LandParts: return "event:/Robot/Move/LandParts";
                case Milestone2ActionCue.DriveAcceleration: return "event:/Robot/Move/Acceleration";
                default: throw new System.ArgumentOutOfRangeException(nameof(index));
            }
        }

        public static bool IsLoop(int index)
        {
            switch ((Milestone2ActionCue)index)
            {
                case Milestone2ActionCue.Drive:
                case Milestone2ActionCue.Coast:
                case Milestone2ActionCue.Brake:
                case Milestone2ActionCue.Turn:
                case Milestone2ActionCue.Slide:
                case Milestone2ActionCue.ArmDeploy:
                case Milestone2ActionCue.ArmServo:
                case Milestone2ActionCue.ArmRetract: return true;
                case Milestone2ActionCue.GroundMove:
                case Milestone2ActionCue.GroundTurn: return true;
                default: return false;
            }
        }

        // The gameplay API does not expose empty-hand grip changes or the cause
        // of a Drop. Keep these slots without predicting input/forced releases.
        public static bool HasObservableTrigger(int index) =>
            index != (int)Milestone2ActionCue.HandClose
            && index != (int)Milestone2ActionCue.HandOpen
            && index != (int)Milestone2ActionCue.Drop;

        internal static string ParameterName(ActionAudioParameter parameter)
        {
            switch (parameter)
            {
                case ActionAudioParameter.Speed: return "Speed";
                case ActionAudioParameter.Direction: return "Direction";
                case ActionAudioParameter.Throttle: return "Throttle";
                case ActionAudioParameter.Load: return "Load";
                case ActionAudioParameter.Effort: return "Effort";
                case ActionAudioParameter.Progress: return "Progress";
                case ActionAudioParameter.Impact: return "Impact";
                default: throw new System.ArgumentOutOfRangeException(nameof(parameter));
            }
        }
    }
}
