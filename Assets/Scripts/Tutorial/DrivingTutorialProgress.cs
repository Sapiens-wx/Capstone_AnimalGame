using UnityEngine;

namespace AnimalGame.Tutorial
{
    public enum DrivingTutorialStep { TurnLeft, TurnRight, Forward, Reverse, SteerWhileMoving, Stop, Completed }

    /// <summary>A measured driving frame, in logical map metres (not Unity world units).</summary>
    public readonly struct DrivingTutorialSample
    {
        public readonly Vector2 PositionMeters;
        public readonly Vector2 Forward;
        public readonly float HeadingDegrees;
        public readonly float SpeedMetersPerSecond;
        public readonly float ThrottleIntent;
        public readonly float CommandedTurnSpeed;
        public readonly float DeltaTime;
        public readonly bool CanDrive;

        public DrivingTutorialSample(Vector2 positionMeters, Vector2 forward, float headingDegrees,
            float speedMetersPerSecond, float throttleIntent, float commandedTurnSpeed,
            float deltaTime, bool canDrive)
        {
            PositionMeters = positionMeters; Forward = forward.normalized; HeadingDegrees = headingDegrees;
            SpeedMetersPerSecond = speedMetersPerSecond; ThrottleIntent = throttleIntent;
            CommandedTurnSpeed = commandedTurnSpeed; DeltaTime = deltaTime; CanDrive = canDrive;
        }
    }

    /// <summary>Scene-independent measurement; never changes the robot, controls, time or saved progress.</summary>
    public sealed class DrivingTutorialProgress
    {
        private readonly float forwardMeters, reverseMeters, turnDegrees, stopSeconds;
        private readonly float movingTurnDegrees, movingTurnMeters;
        private DrivingTutorialSample previous;
        private bool hasPrevious;

        public DrivingTutorialStep Step { get; private set; }
        public float Value { get; private set; }
        public float MovingTurnDistanceMeters { get; private set; }
        public float MovingTurnTargetMeters => movingTurnMeters;
        public bool IsComplete => Step == DrivingTutorialStep.Completed;
        public bool IsStepSatisfied => !IsComplete && Value >= Target
            && (Step != DrivingTutorialStep.SteerWhileMoving || MovingTurnDistanceMeters >= movingTurnMeters);
        public bool CanContinue => IsComplete || IsStepSatisfied;
        public float NormalizedProgress => IsComplete ? 1f : Step == DrivingTutorialStep.SteerWhileMoving
            ? Mathf.Min(Mathf.Clamp01(Value / Target), Mathf.Clamp01(MovingTurnDistanceMeters / movingTurnMeters))
            : Mathf.Clamp01(Value / Target);
        public float Target => Step switch
        {
            DrivingTutorialStep.Forward => forwardMeters,
            DrivingTutorialStep.Reverse => reverseMeters,
            DrivingTutorialStep.Stop => stopSeconds,
            DrivingTutorialStep.SteerWhileMoving => movingTurnDegrees,
            DrivingTutorialStep.Completed => 1f,
            _ => turnDegrees
        };

        public DrivingTutorialProgress(float forwardMeters = 5f, float reverseMeters = 3f,
            float turnDegrees = 45f, float stopSeconds = .8f,
            float movingTurnDegrees = 30f, float movingTurnMeters = 2f)
        {
            this.forwardMeters = Mathf.Max(.5f, forwardMeters);
            this.reverseMeters = Mathf.Max(.5f, reverseMeters);
            this.turnDegrees = Mathf.Clamp(turnDegrees, 10f, 180f);
            this.stopSeconds = Mathf.Max(.2f, stopSeconds);
            this.movingTurnDegrees = Mathf.Clamp(movingTurnDegrees, 10f, 90f);
            this.movingTurnMeters = Mathf.Max(.5f, movingTurnMeters);
        }

        public void Reset()
        {
            Step = DrivingTutorialStep.TurnLeft; Value = 0f; MovingTurnDistanceMeters = 0f; hasPrevious = false;
        }

        public void Suspend()
        {
            hasPrevious = false;
            if (Step == DrivingTutorialStep.Stop && !IsStepSatisfied) Value = 0f;
        }

        public bool Advance()
        {
            if (!IsStepSatisfied) return false;
            Step++; Value = 0f; MovingTurnDistanceMeters = 0f; hasPrevious = false;
            return true;
        }

        public bool Continue() => Advance();

        public void Sample(in DrivingTutorialSample sample)
        {
            DrivingTutorialSample last = previous;
            bool hadPrevious = hasPrevious;
            previous = sample;
            hasPrevious = sample.CanDrive && sample.DeltaTime > 0f;
            if (IsComplete || IsStepSatisfied) return;
            if (!hadPrevious || !hasPrevious)
            {
                if (Step == DrivingTutorialStep.Stop) Value = 0f;
                return;
            }

            Vector2 displacement = sample.PositionMeters - last.PositionMeters;
            float angle = Mathf.DeltaAngle(last.HeadingDegrees, sample.HeadingDegrees);
            // A teleport, reset or externally rotated chassis is not a lesson achievement.
            float plausibleDistance = Mathf.Max(.025f,
                Mathf.Abs(sample.SpeedMetersPerSecond) * sample.DeltaTime * 2f + .015f);
            float plausibleAngle = Mathf.Abs(sample.CommandedTurnSpeed) * sample.DeltaTime * 2f + .3f;
            if (displacement.magnitude > plausibleDistance || Mathf.Abs(angle) > plausibleAngle)
            {
                if (Step == DrivingTutorialStep.Stop) Value = 0f;
                return;
            }

            Vector2 forward = (last.Forward + sample.Forward).normalized;
            float drivenDistance = Vector2.Dot(displacement, forward);
            switch (Step)
            {
                case DrivingTutorialStep.Forward:
                    if (sample.ThrottleIntent > .12f && sample.SpeedMetersPerSecond > .05f)
                        Value += Mathf.Max(0f, drivenDistance);
                    break;
                case DrivingTutorialStep.Reverse:
                    if (sample.ThrottleIntent < -.12f && sample.SpeedMetersPerSecond < -.05f)
                        Value += Mathf.Max(0f, -drivenDistance);
                    break;
                case DrivingTutorialStep.SteerWhileMoving:
                    // Both measurements must come from the same genuinely driven turning frames.
                    // Turning on the spot followed by driving straight cannot satisfy this lesson.
                    if (sample.ThrottleIntent > .12f && sample.SpeedMetersPerSecond > .15f
                        && drivenDistance > .000001f && Mathf.Abs(angle) > .001f
                        && Mathf.Abs(sample.CommandedTurnSpeed) > 1f
                        && angle * sample.CommandedTurnSpeed < 0f)
                    {
                        Value += Mathf.Abs(angle);
                        MovingTurnDistanceMeters = Mathf.Min(movingTurnMeters,
                            MovingTurnDistanceMeters + drivenDistance);
                    }
                    break;
                case DrivingTutorialStep.TurnLeft:
                case DrivingTutorialStep.TurnRight:
                    float motionSign = sample.SpeedMetersPerSecond < 0f ? -1f : 1f;
                    if (Mathf.Abs(sample.CommandedTurnSpeed) > 1f
                        && angle * sample.CommandedTurnSpeed * motionSign < 0f)
                    {
                        // Count actual steering both on the spot and while driving either way.
                        float expectedSign = Step == DrivingTutorialStep.TurnLeft ? 1f : -1f;
                        // Going back in the opposite direction removes the same amount of progress.
                        Value = Mathf.Max(0f, Value + angle * expectedSign);
                    }
                    break;
                case DrivingTutorialStep.Stop:
                    bool stopped = Mathf.Abs(sample.ThrottleIntent) <= .08f
                        && Mathf.Abs(sample.CommandedTurnSpeed) <= 1f
                        && Mathf.Abs(sample.SpeedMetersPerSecond) <= .15f
                        && displacement.magnitude / sample.DeltaTime <= .15f
                        && Mathf.Abs(angle) / sample.DeltaTime <= 3f;
                    Value = stopped ? Value + sample.DeltaTime : 0f;
                    break;
            }
            Value = Mathf.Min(Value, Target);
        }
    }
}
