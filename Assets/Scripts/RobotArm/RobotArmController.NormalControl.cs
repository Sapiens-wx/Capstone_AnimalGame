using UnityEngine;

namespace AnimalGame.RobotArm
{
    // Ordinary deployed arms use stick velocity in their current polar frame.
    // Tumble targets, anchored heavy grips, deployment and recycling stay on their own paths.
    public sealed partial class RobotArmController
    {
        [Header("Normal Arm Rate Control")]
        [SerializeField, Range(0f, .5f)] private float normalControlDeadZone = .14f;
        [Tooltip("Independent axis dead zone prevents small sideways noise during extension/retraction.")]
        [SerializeField, Range(0f, .5f)] private float normalAxisDeadZone = .10f;
        [Tooltip("Seconds to traverse the forward reach range at full input. Sets both extension and lateral hand speed, before grab resistance.")]
        [SerializeField, Min(.01f)] private float normalFullTravelDuration = 1f;
        [SerializeField, Range(1f, 3f)] private float normalSpeedExponent = 1.5f;
        [Tooltip("Raw horizontal input needed to start turning the body after the hands reach the angle limit.")]
        [SerializeField, Range(0f, 1f)] private float normalFollowEnterMagnitude = .55f;
        [SerializeField, Range(0f, 1f)] private float normalFollowExitMagnitude = .45f;

        private bool normalManualActive, normalHoldPose, normalBodyFollowActive;
        private Vector2 normalRateInput;
        private float normalBoundaryTurnDelta;
        private Vector2 NormalHandCentre => (HandLocal(left) + HandLocal(right)) * .5f;

        private void ResetNormalArmInput()
        {
            normalManualActive = normalHoldPose = normalBodyFollowActive = false;
            normalRateInput = Vector2.zero;
            normalBoundaryTurnDelta = 0f;
        }

        private void PrepareNormalArmInput(bool armHeld)
        {
            if (!armHeld || !Upright || IsTumbleArmLayoutActive || !HandsDeployed
                || State == RobotArmState.Recycling
                || (heldObject != null && heldObject.TryGetComponent<AnimalGame.Garbage.HeavyGarbagePull>(out _)))
            {
                ResetNormalArmInput();
                return;
            }
            normalManualActive = true;
            normalRateInput = CurrentInputMagnitude <= normalControlDeadZone ? Vector2.zero
                : new Vector2(NormalAxisRate(inputLocal.x), NormalAxisRate(inputLocal.y));
            normalHoldPose = normalRateInput == Vector2.zero;
            normalBoundaryTurnDelta = 0f;
        }

        private float NormalAxisRate(float axis)
        {
            float amount = Mathf.InverseLerp(normalAxisDeadZone, 1f, Mathf.Abs(axis));
            return Mathf.Sign(axis) * Mathf.Pow(amount, normalSpeedExponent);
        }

        private void UpdateNormalArmTarget()
        {
            if (!normalManualActive)
            {
                // A fresh deployment always starts at its authored forward standby position.
                // Input during deployment never accumulates an invisible manual displacement.
                if (IsArmModeActive && deploymentTime <= .00001f)
                    targetLocal = Vector2.up * armLength * .8f;
                return;
            }

            Vector2 actual = NormalHandCentre;
            targetLocal = actual;
            if (normalHoldPose || stepDelta <= 0f || mover.GrabMovementMultiplier <= 0f)
            {
                normalHoldPose = true;
                return;
            }

            float radius = actual.magnitude;
            float angle = radius > .000001f ? Vector2.SignedAngle(Vector2.up, actual) : 0f;
            float minimum = armLength * staticArmLengthPercent;
            float linearSpeed = Mathf.Max(0f, MaximumCommonReach() - minimum)
                / Mathf.Max(.01f, normalFullTravelDuration);
            // Both stick axes control hand movement in the current radial/tangential frame.
            // Only the body motion after reaching an angle limit uses the player's turn speed.
            float angularDelta = -normalRateInput.x * linearSpeed
                / Mathf.Max(radius, armLength * .001f) * Mathf.Rad2Deg
                * mover.GrabMovementMultiplier * stepDelta;
            float nextAngle = angularDelta > 0f
                ? Mathf.Min(angle + angularDelta, Mathf.Max(followAngle, angle))
                : Mathf.Max(angle + angularDelta, Mathf.Min(-followAngle, angle));
            float boundaryTimeFraction = Mathf.Abs(angularDelta) > .000001f
                ? Mathf.Clamp01((angularDelta - (nextAngle - angle)) / angularDelta) : 0f;
            normalBoundaryTurnDelta = -normalRateInput.x * mover.BaseTurnSpeed
                * mover.GrabMovementMultiplier * stepDelta * boundaryTimeFraction;

            float radialDelta = normalRateInput.y * linearSpeed * mover.GrabMovementMultiplier * stepDelta;
            float nextRadius = radialDelta > 0f ? radius + radialDelta
                : Mathf.Max(radius + radialDelta, Mathf.Min(radius, minimum));
            Vector2 direction = Direction(nextAngle);
            // The two sockets have different reach limits when aiming off-centre.
            // Clip to their common workspace instead of leaving an unreachable target ahead.
            float maximum = Mathf.Min(NormalReachAt(left, direction), NormalReachAt(right, direction));
            nextRadius = Mathf.Min(nextRadius, maximum);
            Vector2 desired = direction * nextRadius;
            if (!CommonTargetReachable(desired))
            {
                float low = 0f, high = 1f;
                for (int i = 0; i < 18; i++)
                {
                    float t = (low + high) * .5f;
                    if (CommonTargetReachable(Vector2.Lerp(actual, desired, t))) low = t; else high = t;
                }
                desired = Vector2.Lerp(actual, desired, low);
            }
            targetLocal = desired;
            normalHoldPose = (desired - actual).sqrMagnitude < 1e-12f;
        }

        private float NormalReachAt(Arm arm, Vector2 direction)
        {
            Vector2 offset = arm.Socket - Vector2.right * arm.Side * handSpacing * .5f;
            float along = Vector2.Dot(offset, direction);
            float acrossSquared = Mathf.Max(0f, offset.sqrMagnitude - along * along);
            float length = arm.UpperLength + arm.LowerLength - armLength * .005f;
            return Mathf.Max(0f, along + Mathf.Sqrt(Mathf.Max(0f, length * length - acrossSquared)));
        }

        private void CompleteNormalArmMotion()
        {
            // Discard blocked displacement and joint lag every frame. Recentring must never
            // keep chasing an old command, and reversing must take effect at the actual hands.
            if (normalManualActive) targetLocal = NormalHandCentre;
        }

        private void TurnBodyForLocalInput()
        {
            if (!normalManualActive || Mathf.Abs(normalBoundaryTurnDelta) < .000001f
                || Mathf.Abs(inputLocal.x) <= normalFollowExitMagnitude)
            {
                normalBodyFollowActive = false;
                return;
            }
            float actualAngle = Vector2.SignedAngle(Vector2.up, NormalHandCentre);
            bool atBoundary = normalBoundaryTurnDelta > 0f ? actualAngle >= followAngle - .05f
                : actualAngle <= -followAngle + .05f;
            if (!atBoundary)
            {
                normalBodyFollowActive = false;
                return;
            }
            if (!normalBodyFollowActive && Mathf.Abs(inputLocal.x) < normalFollowEnterMagnitude) return;
            normalBodyFollowActive = true;
            // Only the frame time left after reaching the arm limit turns the body.
            // Fast lateral hand input must not increase the player's body turn speed.
            Quaternion requested = Quaternion.Euler(0f, 0f, transform.eulerAngles.z + normalBoundaryTurnDelta);
            ConstrainBodyPose(transform.position, transform.rotation, transform.position, requested);
        }

        private void ValidateNormalArmInput()
        {
            normalControlDeadZone = Mathf.Clamp(normalControlDeadZone, 0f, .5f);
            normalAxisDeadZone = Mathf.Clamp(normalAxisDeadZone, 0f, .5f);
            normalFullTravelDuration = Mathf.Max(.01f, normalFullTravelDuration);
            normalSpeedExponent = Mathf.Clamp(normalSpeedExponent, 1f, 3f);
            normalFollowExitMagnitude = Mathf.Clamp(normalFollowExitMagnitude, 0f, .999f);
            normalFollowEnterMagnitude = Mathf.Clamp(normalFollowEnterMagnitude, normalFollowExitMagnitude + .001f, 1f);
        }
    }
}
