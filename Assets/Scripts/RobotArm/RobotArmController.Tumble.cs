using UnityEngine;

namespace AnimalGame.RobotArm
{
    // The ordinary deployment/interaction path stays in RobotArmController.cs.
    // This layout is used only while tumbling, fallen, or returning exposed arms to their ordinary sockets.
    public sealed partial class RobotArmController
    {
        [Header("Tumble Arm Layout")]
        [Tooltip("Time for an exposed arm socket to travel 90 degrees along the body edge.")]
        [SerializeField, Min(.01f)] private float tumbleSocketQuarterTurnDuration = .35f;

        public bool IsTumbleArmLayoutActive => tumbleLayoutActive;
        public Vector2 TumbleArmSupportDirectionLocal => TumbleDirectionLocal;
        public bool IsTumbleArmSupportReady => IsTumbleArmSupportAligned(18f);
        public bool IsTumbleArmSupportAligned(float directionToleranceDegrees)
        {
            if (!tumbleLayoutActive || Upright || !HandsReady || IsBlocked || !TumbleSocketsAtTarget)
                return false;
            Vector2 actual = (HandLocal(left) + HandLocal(right)) * .5f;
            return actual.sqrMagnitude > .000001f
                && Vector2.Angle(actual, TumbleDirectionLocal) <= Mathf.Clamp(directionToleranceDegrees, 0f, 180f);
        }

        private bool tumbleLayoutActive, tumbleWasNonUpright, tumbleReturning, tumbleClearingArms;
        private Vector2 tumbleDirectionWorld;
        private int tumbleQuadrant = -1;
        private float tumbleStartCentre, tumbleStartGap, tumbleTargetCentre, tumbleTargetGap;
        private float tumbleLayoutElapsed, tumbleLayoutDuration;

        private Vector2 TumbleDirectionLocal
        {
            get
            {
                Vector2 direction = new Vector2(Vector2.Dot(tumbleDirectionWorld, transform.right),
                    Vector2.Dot(tumbleDirectionWorld, transform.up));
                return direction.sqrMagnitude > .000001f ? direction.normalized : Vector2.right;
            }
        }
        private float TumbleCurrentCentre => (left.Pose.SocketAngle + right.Pose.SocketAngle) * .5f;
        private bool TumbleSocketsAtTarget => left != null && right != null
            && Mathf.Abs(left.Pose.SocketAngle - (tumbleTargetCentre + tumbleTargetGap * .5f)) <= .05f
            && Mathf.Abs(right.Pose.SocketAngle - (tumbleTargetCentre - tumbleTargetGap * .5f)) <= .05f;

        private static Vector2 SocketAt(Arm arm, Pose pose)
        {
            if (!pose.HasSocket) return arm.Socket;
            float radians = pose.SocketAngle * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * arm.Socket.magnitude;
        }
        private static Vector2 CircleDirection(float angle)
        {
            float radians = angle * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }
        private static Vector2 RightOf(Vector2 forward) => new Vector2(forward.y, -forward.x);

        private void PrepareTumbleArmLayout(bool armHeld)
        {
            bool nonUpright = !Upright;
            bool enteredTumble = nonUpright && !tumbleWasNonUpright;
            tumbleWasNonUpright = nonUpright;
            if (enteredTumble)
            {
                tumbleDirectionWorld = tumble.DirectionWorld.sqrMagnitude > .000001f
                    ? tumble.DirectionWorld.normalized : (Vector2)transform.right;
                tumbleQuadrant = SelectTumbleQuadrant(TumbleDirectionLocal, tumbleQuadrant);
            }

            if (!armHeld && deploymentTime <= .00001f)
            {
                if (tumbleLayoutActive) deploymentTime = 0f;
                ClearTumbleSocketPoses();
                return;
            }
            if (nonUpright && (!tumbleLayoutActive || enteredTumble || tumbleReturning))
            {
                EnableTumbleSocketPoses();
                tumbleLayoutActive = true;
                tumbleReturning = false;
                BeginTumbleSocketTransition(45f + 90f * tumbleQuadrant, 90f);
                if (deploymentTime <= .00001f)
                {
                    SetHiddenTumbleSockets();
                    targetLocal = TumbleDirectionLocal * armLength * .8f;
                }
            }
            else if (!nonUpright && tumbleLayoutActive && !tumbleReturning)
            {
                tumbleReturning = true;
                BeginTumbleSocketTransition(90f, 180f);
            }
        }

        // Counter-clockwise quadrants: upper-right, upper-left, lower-left, lower-right.
        // At an axis, retain a compatible previous pair; otherwise take the clockwise pair.
        private static int SelectTumbleQuadrant(Vector2 direction, int previous)
        {
            const float epsilon = .0001f;
            if (Mathf.Abs(direction.x) <= epsilon)
            {
                if (direction.y >= 0f) return previous == 0 || previous == 1 ? previous : 0;
                return previous == 2 || previous == 3 ? previous : 2;
            }
            if (Mathf.Abs(direction.y) <= epsilon)
            {
                if (direction.x >= 0f) return previous == 0 || previous == 3 ? previous : 3;
                return previous == 1 || previous == 2 ? previous : 1;
            }
            return direction.y > 0f ? (direction.x > 0f ? 0 : 1) : (direction.x < 0f ? 2 : 3);
        }

        private void EnableTumbleSocketPoses()
        {
            if (left.Pose.HasSocket) return;
            left.Pose.HasSocket = right.Pose.HasSocket = true;
            left.Pose.SocketAngle = 180f;
            right.Pose.SocketAngle = 0f;
        }
        private void BeginTumbleSocketTransition(float centre, float gap)
        {
            tumbleStartCentre = TumbleCurrentCentre;
            tumbleStartGap = left.Pose.SocketAngle - right.Pose.SocketAngle;
            tumbleTargetCentre = tumbleStartCentre + Mathf.DeltaAngle(tumbleStartCentre, centre);
            tumbleTargetGap = gap;
            float leftTravel = Mathf.Abs(tumbleTargetCentre + gap * .5f - left.Pose.SocketAngle);
            float rightTravel = Mathf.Abs(tumbleTargetCentre - gap * .5f - right.Pose.SocketAngle);
            tumbleLayoutDuration = Mathf.Max(leftTravel, rightTravel) / 90f * tumbleSocketQuarterTurnDuration;
            tumbleLayoutElapsed = 0f;
        }
        private void SetHiddenTumbleSockets()
        {
            left.Pose.SocketAngle = tumbleTargetCentre + tumbleTargetGap * .5f;
            right.Pose.SocketAngle = tumbleTargetCentre - tumbleTargetGap * .5f;
            tumbleLayoutElapsed = tumbleLayoutDuration;
        }
        private void TumbleSocketAngles(float elapsed, out float leftAngle, out float rightAngle)
        {
            float t = tumbleLayoutDuration <= .00001f ? 1f
                : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / tumbleLayoutDuration));
            float centre = Mathf.Lerp(tumbleStartCentre, tumbleTargetCentre, t);
            float gap = Mathf.Lerp(tumbleStartGap, tumbleTargetGap, t);
            leftAngle = centre + gap * .5f;
            rightAngle = centre - gap * .5f;
        }

        private void UpdateTumbleArmTarget()
        {
            Vector2 basis = !TumbleSocketsAtTarget ? CircleDirection(TumbleCurrentCentre)
                : Upright ? Vector2.up : TumbleDirectionLocal;
            Vector2 direction = basis;
            // During migration the aim frame follows the moving pair, keeping the arms outside the body.
            if (TumbleSocketsAtTarget && CurrentInputMagnitude > leftStickDeadZone)
            {
                float angle = Mathf.Clamp(Vector2.SignedAngle(basis, inputLocal), -followAngle, followAngle);
                direction = Rotate(basis, angle);
            }
            Vector2 tangent = RightOf(basis);
            float reach = Mathf.Min(TumbleForwardReach(left, direction, tangent),
                TumbleForwardReach(right, direction, tangent));
            float radius = Mathf.Lerp(armLength * staticArmLengthPercent, reach,
                Mathf.InverseLerp(dockEnterMagnitude, maximumMagnitude, CurrentInputMagnitude));
            float blend = 1f - Mathf.Exp(-stepDelta / Mathf.Max(.01f, aimSmoothingTime));
            targetLocal = Vector2.Lerp(targetLocal, direction * radius, blend);
        }
        private float TumbleForwardReach(Arm arm, Vector2 forward, Vector2 tangent)
        {
            Vector2 socket = SocketAt(arm, arm.Pose) - tangent * arm.Side * handSpacing * .5f;
            float longitudinal = Vector2.Dot(socket, forward);
            float lateral = Vector2.Dot(socket, RightOf(forward));
            float reach = arm.UpperLength + arm.LowerLength - armLength * .005f;
            return Mathf.Max(0f, longitudinal + Mathf.Sqrt(Mathf.Max(0f, reach * reach - lateral * lateral)));
        }

        private Pose DesiredTumblePose(Arm arm, float progress, float socketAngle, Vector2 target,
            Vector2 tangent, bool extending)
        {
            Pose result = arm.Pose;
            result.HasSocket = true;
            result.SocketAngle = socketAngle;
            result.LowerLength = arm.LowerLength * Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01(progress / connectorExtendDuration));
            result.UpperLength = arm.UpperLength * Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01((progress - connectorExtendDuration) / extendDuration));
            result.Hand = Mathf.Clamp01((progress - connectorExtendDuration - extendDuration) / handExtendDuration);
            if (!extending) return result;
            Vector2 desired = target + tangent * arm.Side * handSpacing * .5f;
            SolveIK(desired - SocketAt(arm, result), arm.LowerLength, arm.UpperLength, arm.Side,
                out float lower, out float upper);
            float rotation = socketAngle - arm.Pose.SocketAngle;
            float delta = deploymentTime <= .00001f ? 360f : maximumAimSpeedDegreesPerSecond * stepDelta;
            result.LowerAngle = Mathf.MoveTowardsAngle(arm.Pose.LowerAngle + rotation, lower, delta);
            result.UpperAngle = Mathf.MoveTowardsAngle(arm.Pose.UpperAngle + rotation, upper, delta);
            return result;
        }

        private void AdvanceTumbleArms(bool extending)
        {
            if (extending && tumbleClearingArms)
            {
                ClearTumbleArmJoints(stepDelta);
                return;
            }
            float nextDeployment = extending ? Mathf.Min(TotalDeploymentTime, deploymentTime + stepDelta)
                : Mathf.Max(0f, deploymentTime - stepDelta * TotalDeploymentTime / retractDuration);
            float nextElapsed = extending ? Mathf.Min(tumbleLayoutDuration, tumbleLayoutElapsed + stepDelta)
                : tumbleLayoutElapsed;
            TumbleSocketAngles(nextElapsed, out float la, out float ra);
            if (!extending) { la = left.Pose.SocketAngle; ra = right.Pose.SocketAngle; }
            float centreRotation = (la + ra) * .5f - TumbleCurrentCentre;
            Vector2 nextTarget = Rotate(targetLocal, centreRotation);
            Vector2 basis = !TumbleSocketsAtTarget ? CircleDirection((la + ra) * .5f)
                : Upright ? Vector2.up : TumbleDirectionLocal;
            Vector2 tangent = RightOf(basis);
            Pose oldL = left.Pose, oldR = right.Pose;
            Pose lp = DesiredTumblePose(left, nextDeployment, la, nextTarget, tangent, extending);
            Pose rp = DesiredTumblePose(right, nextDeployment, ra, nextTarget, tangent, extending);
            int steps = Mathf.Clamp(Mathf.Max(MotionSteps(oldL, lp, oldR, rp),
                Mathf.CeilToInt((PoseTravel(oldL, lp) + PoseTravel(oldR, rp)) / .005f)), 1, 4096);
            float accepted = 0f;
            for (int i = 1; i <= steps; i++)
            {
                float t = accepted + 1f / steps;
                Pose nextL = Pose.Lerp(oldL, lp, t), nextR = Pose.Lerp(oldR, rp, t);
                BuildArmPlan(nextL, nextR, heldOffset);
                bool legal = contactPlan.Evaluate();
                float multiplier = contactPlan.SpeedMultiplier;
                if (multiplier < 1f)
                {
                    t = accepted + multiplier / steps;
                    nextL = Pose.Lerp(oldL, lp, t); nextR = Pose.Lerp(oldR, rp, t);
                    BuildArmPlan(nextL, nextR, heldOffset);
                    legal = contactPlan.Evaluate();
                }
                if (!legal || multiplier <= 0f) { IsBlocked = true; break; }
                if (!TumbleArmMotionClear(nextL, nextR))
                {
                    // Legacy upright poses can cross the body. Free their joints while the
                    // sockets stay still before attempting the paired circular path again.
                    tumbleClearingArms = extending && !TumbleSocketsAtTarget;
                    IsBlocked = true;
                    break;
                }
                if (!contactPlan.Commit()) { IsBlocked = true; break; }
                left.Pose = nextL; right.Pose = nextR; accepted = t;
            }
            deploymentTime = Mathf.Lerp(deploymentTime, nextDeployment, accepted);
            tumbleLayoutElapsed = Mathf.Lerp(tumbleLayoutElapsed, nextElapsed, accepted);
            targetLocal = Rotate(targetLocal, centreRotation * accepted);
            if (tumbleClearingArms && extending) ClearTumbleArmJoints(stepDelta * (1f - accepted));
            if (!extending && deploymentTime <= .00001f)
            {
                deploymentTime = 0f;
                State = RobotArmState.Retracted;
                ClearTumbleSocketPoses();
            }
            else if (Upright && tumbleReturning && TumbleSocketsAtTarget) ClearTumbleSocketPoses();
            else if (extending && HandsReady && State == RobotArmState.Extending) UpdateDockState();
        }

        private void ClearTumbleArmJoints(float deltaTime)
        {
            IsBlocked = true;
            Pose lp = OutwardTumblePose(left, deltaTime), rp = OutwardTumblePose(right, deltaTime);
            Pose oldL = left.Pose, oldR = right.Pose;
            int steps = Mathf.Clamp(MotionSteps(oldL, lp, oldR, rp), 1, 4096);
            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                Pose nextL = Pose.Lerp(oldL, lp, t), nextR = Pose.Lerp(oldR, rp, t);
                if (TryClearTumbleJoints(nextL, nextR)) continue;
                // If the coupled sweep meets the other arm, let one arm make room first.
                if (TryClearTumbleJoints(nextL, right.Pose)) continue;
                if (!TryClearTumbleJoints(left.Pose, nextR)) break;
            }
            if (TumbleJointsOutward(left) && TumbleJointsOutward(right)) tumbleClearingArms = false;
        }
        private Pose OutwardTumblePose(Arm arm, float deltaTime)
        {
            Pose pose = arm.Pose;
            float angle = pose.SocketAngle - 90f;
            float delta = maximumAimSpeedDegreesPerSecond * Mathf.Max(0f, deltaTime);
            pose.LowerAngle = Mathf.MoveTowardsAngle(pose.LowerAngle, angle, delta);
            pose.UpperAngle = Mathf.MoveTowardsAngle(pose.UpperAngle, angle, delta);
            return pose;
        }
        private static bool TumbleJointsOutward(Arm arm) =>
            Mathf.Abs(Mathf.DeltaAngle(arm.Pose.LowerAngle, arm.Pose.SocketAngle - 90f)) <= .05f
            && Mathf.Abs(Mathf.DeltaAngle(arm.Pose.UpperAngle, arm.Pose.SocketAngle - 90f)) <= .05f;
        private bool TryClearTumbleJoints(Pose lp, Pose rp)
        {
            if (Mathf.Abs(Mathf.DeltaAngle(left.Pose.LowerAngle, lp.LowerAngle))
                + Mathf.Abs(Mathf.DeltaAngle(left.Pose.UpperAngle, lp.UpperAngle))
                + Mathf.Abs(Mathf.DeltaAngle(right.Pose.LowerAngle, rp.LowerAngle))
                + Mathf.Abs(Mathf.DeltaAngle(right.Pose.UpperAngle, rp.UpperAngle)) < .000001f) return false;
            if (!TumbleArmMotionClear(lp, rp)) return false;
            BuildArmPlan(lp, rp, heldOffset);
            if (!contactPlan.Evaluate() || contactPlan.SpeedMultiplier <= 0f) return false;
            if (contactPlan.SpeedMultiplier < 1f)
            {
                lp = Pose.Lerp(left.Pose, lp, contactPlan.SpeedMultiplier);
                rp = Pose.Lerp(right.Pose, rp, contactPlan.SpeedMultiplier);
                if (!TumbleArmMotionClear(lp, rp)) return false;
                BuildArmPlan(lp, rp, heldOffset);
                if (!contactPlan.Evaluate()) return false;
            }
            if (!contactPlan.Commit()) return false;
            left.Pose = lp; right.Pose = rp;
            return true;
        }

        private bool TumbleArmMotionClear(Pose nextL, Pose nextR)
        {
            // World interaction queries ignore the player's hierarchy. Keep the attachment collar,
            // but do not let either exposed chain cut farther through the body or the other arm.
            if (TumbleBodyPenetration(left, nextL) > Mathf.Max(.000001f, TumbleBodyPenetration(left, left.Pose))
                || TumbleBodyPenetration(right, nextR) > Mathf.Max(.000001f, TumbleBodyPenetration(right, right.Pose)))
                return false;
            return !TumbleArmsCross(nextL, nextR) || TumbleArmsCross(left.Pose, right.Pose);
        }
        private static float SegmentDistanceToOrigin(Vector2 a, Vector2 b)
        {
            Vector2 delta = b - a;
            float t = delta.sqrMagnitude <= .00000001f ? 0f
                : Mathf.Clamp01(-Vector2.Dot(a, delta) / delta.sqrMagnitude);
            return (a + delta * t).magnitude;
        }
        private static float TumbleBodyPenetration(Arm arm, Pose pose)
        {
            Vector2 socket = SocketAt(arm, pose);
            Vector2 elbow = socket + Direction(pose.LowerAngle) * pose.LowerLength;
            Vector2 hand = HandPoint(arm, pose);
            float radius = arm.Socket.magnitude * .9f;
            float penetration = pose.LowerLength > .00001f
                ? Mathf.Max(0f, radius - SegmentDistanceToOrigin(socket, elbow)) : 0f;
            if (pose.UpperLength > .00001f)
                penetration = Mathf.Max(penetration, radius - SegmentDistanceToOrigin(elbow, hand));
            return penetration;
        }
        private bool TumbleArmsCross(Pose lp, Pose rp)
        {
            Vector2 ls = SocketAt(left, lp), rs = SocketAt(right, rp);
            Vector2 le = ls + Direction(lp.LowerAngle) * lp.LowerLength;
            Vector2 re = rs + Direction(rp.LowerAngle) * rp.LowerLength;
            Vector2 lh = HandPoint(left, lp), rh = HandPoint(right, rp);
            return (lp.LowerLength > .00001f && rp.LowerLength > .00001f && SegmentsCross(ls, le, rs, re))
                || (lp.LowerLength > .00001f && rp.UpperLength > .00001f && SegmentsCross(ls, le, re, rh))
                || (lp.UpperLength > .00001f && rp.LowerLength > .00001f && SegmentsCross(le, lh, rs, re))
                || (lp.UpperLength > .00001f && rp.UpperLength > .00001f && SegmentsCross(le, lh, re, rh));
        }
        private static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            Vector2 ab = b - a, cd = d - c;
            float cSide = ab.x * (c - a).y - ab.y * (c - a).x;
            float dSide = ab.x * (d - a).y - ab.y * (d - a).x;
            float aSide = cd.x * (a - c).y - cd.y * (a - c).x;
            float bSide = cd.x * (b - c).y - cd.y * (b - c).x;
            return cSide * dSide < -.000000001f && aSide * bSide < -.000000001f;
        }

        private void ClearTumbleSocketPoses()
        {
            tumbleLayoutActive = tumbleReturning = tumbleClearingArms = false;
            if (left != null) { left.Pose.HasSocket = false; left.Pose.SocketAngle = 0f; }
            if (right != null) { right.Pose.HasSocket = false; right.Pose.SocketAngle = 0f; }
        }
        private void ResetTumbleArmLayout()
        {
            ClearTumbleSocketPoses();
            tumbleWasNonUpright = false;
            tumbleDirectionWorld = Vector2.zero;
            tumbleQuadrant = -1;
            tumbleLayoutElapsed = tumbleLayoutDuration = 0f;
        }
    }
}
