using UnityEngine;

namespace AnimalGame.Tutorial
{
    public enum ArmTutorialStep
    {
        Deploy, Extend, RetractReach, SwingLeft, SwingRight, HoldStill, Stow, Completed
    }

    /// <summary>Measured hand pose in robot-local units; chassis translation and rotation are excluded.</summary>
    public readonly struct ArmTutorialSample
    {
        public readonly Vector2 HandCentreLocal, Stick;
        public readonly float Deployment01, DeltaTime;
        public readonly bool ArmModeActive, HandsOperating, Retracted, Empty, Upright, Valid, TogglePressed, Blocked;

        public ArmTutorialSample(Vector2 handCentreLocal, Vector2 stick,
            float deployment01, bool armModeActive, bool handsOperating, bool retracted,
            bool empty, bool upright, bool valid, float deltaTime, bool togglePressed = false, bool blocked = false)
        {
            HandCentreLocal = handCentreLocal; Stick = stick;
            Deployment01 = deployment01; ArmModeActive = armModeActive; HandsOperating = handsOperating;
            Retracted = retracted; Empty = empty; Upright = upright; Valid = valid; DeltaTime = deltaTime;
            TogglePressed = togglePressed; Blocked = blocked;
        }
    }

    /// <summary>Scene-owned lesson measurement. Never deploys arms, moves the robot or changes arm settings.</summary>
    public sealed class ArmTutorialProgress
    {
        private readonly float radialDistance, swingDegrees, holdSeconds;
        private Vector2 previousCentre;
        private bool hasPrevious, deployRequested, stowRequested, hasDeployObservation;

        public ArmTutorialStep Step { get; private set; }
        public float Value { get; private set; }
        public bool NeedsInitialStow { get; private set; }
        public bool IsComplete => Step == ArmTutorialStep.Completed;
        public bool IsStepSatisfied => !IsComplete && Value >= Target;
        public float Target => Step switch
        {
            ArmTutorialStep.Extend or ArmTutorialStep.RetractReach => radialDistance,
            ArmTutorialStep.SwingLeft or ArmTutorialStep.SwingRight => swingDegrees,
            ArmTutorialStep.HoldStill => holdSeconds,
            _ => 1f
        };
        public float NormalizedProgress => IsComplete ? 1f : Mathf.Clamp01(Value / Target);

        public ArmTutorialProgress(float radialDistance = .2f, float swingDegrees = 15f, float holdSeconds = .6f)
        {
            this.radialDistance = float.IsFinite(radialDistance) ? Mathf.Max(.001f, radialDistance) : .2f;
            this.swingDegrees = float.IsFinite(swingDegrees) ? Mathf.Clamp(swingDegrees, 1f, 90f) : 15f;
            this.holdSeconds = float.IsFinite(holdSeconds) ? Mathf.Max(.1f, holdSeconds) : .6f;
        }

        public void Reset()
        {
            Step = ArmTutorialStep.Deploy; Value = 0f;
            hasPrevious = deployRequested = stowRequested = hasDeployObservation = false;
            NeedsInitialStow = false;
        }

        public void Suspend()
        {
            hasPrevious = false;
            if (Step == ArmTutorialStep.HoldStill) Value = 0f;
            if (Step == ArmTutorialStep.Deploy)
            {
                deployRequested = hasDeployObservation = false;
                Value = 0f;
            }
            // An observed manual close remains the player's action if a menu pauses its animation.
            // Without that request, another system's automatic retraction still cannot satisfy Stow.
            if (Step == ArmTutorialStep.Stow) Value = 0f;
        }

        public bool Advance()
        {
            if (!IsStepSatisfied) return false;
            Step++; Value = 0f;
            hasPrevious = deployRequested = stowRequested = hasDeployObservation = false;
            NeedsInitialStow = false;
            return true;
        }

        public void Sample(in ArmTutorialSample sample)
        {
            if (IsComplete) return;
            bool valid = sample.Valid && sample.Empty && sample.Upright
                && sample.DeltaTime > 0f && sample.DeltaTime <= .25f && float.IsFinite(sample.DeltaTime)
                && float.IsFinite(sample.Deployment01)
                && Finite(sample.HandCentreLocal) && Finite(sample.Stick);
            if (!valid) { Suspend(); return; }
            if (Step == ArmTutorialStep.Deploy)
            {
                // A camera/menu's old open state cannot satisfy the requested new L3 action.
                if (!hasDeployObservation)
                {
                    NeedsInitialStow = sample.ArmModeActive && !sample.TogglePressed;
                    hasDeployObservation = true;
                }
                if (sample.TogglePressed) deployRequested = sample.ArmModeActive;
                if (!sample.ArmModeActive) { deployRequested = false; NeedsInitialStow = false; }
                Value = deployRequested && FullyOperating(sample) ? 1f : 0f;
                return;
            }
            if (Step == ArmTutorialStep.Stow)
            {
                if (sample.TogglePressed && !sample.ArmModeActive) stowRequested = true;
                if (sample.ArmModeActive) stowRequested = false;
                Value = stowRequested && !sample.ArmModeActive && sample.Retracted
                    && sample.Deployment01 <= .001f ? 1f : 0f;
                return;
            }
            if (!FullyOperating(sample) || sample.Blocked)
            {
                Suspend();
                return;
            }
            if (IsStepSatisfied) return;
            Vector2 last = previousCentre;
            bool hadPrevious = hasPrevious;
            previousCentre = sample.HandCentreLocal; hasPrevious = true;
            if (!hadPrevious) return;

            Vector2 movement = sample.HandCentreLocal - last;
            float angle = Mathf.DeltaAngle(Heading(last), Heading(sample.HandCentreLocal));
            // Loose physical limits reject teleports/rebuilt visuals, while allowing the authored fast arm setting.
            if (movement.magnitude > Mathf.Max(radialDistance * .1f, radialDistance * 30f * sample.DeltaTime) + .00001f
                || Mathf.Abs(angle) > Mathf.Max(1f, 720f * sample.DeltaTime) + .00001f)
            {
                Suspend();
                return;
            }
            float radial = sample.HandCentreLocal.magnitude - last.magnitude;
            if (Step == ArmTutorialStep.HoldStill)
            {
                bool still = sample.Stick.magnitude <= .14f
                    && movement.magnitude <= radialDistance * .1f * sample.DeltaTime
                    && Mathf.Abs(angle) <= 2f * sample.DeltaTime;
                Value = still ? Mathf.Min(holdSeconds, Value + sample.DeltaTime) : 0f;
                return;
            }
            if (Step == ArmTutorialStep.Extend || Step == ArmTutorialStep.RetractReach)
            {
                if (Mathf.Abs(sample.Stick.y) > .15f && radial * sample.Stick.y > 0f)
                    Value = Mathf.Clamp(Value + radial * (Step == ArmTutorialStep.Extend ? 1f : -1f), 0f, Target);
            }
            else if (Mathf.Abs(sample.Stick.x) > .15f && angle * sample.Stick.x < 0f)
                Value = Mathf.Clamp(Value + angle * (Step == ArmTutorialStep.SwingLeft ? 1f : -1f), 0f, Target);
            if (Target - Value < Target * .00001f) Value = Target;
        }

        private static bool FullyOperating(in ArmTutorialSample sample) => sample.ArmModeActive
            && sample.HandsOperating && !sample.Retracted && sample.Deployment01 >= .999f;
        private static float Heading(Vector2 centre) => Vector2.SignedAngle(Vector2.up, centre);
        private static bool Finite(Vector2 value) => float.IsFinite(value.x) && float.IsFinite(value.y);
    }
}
