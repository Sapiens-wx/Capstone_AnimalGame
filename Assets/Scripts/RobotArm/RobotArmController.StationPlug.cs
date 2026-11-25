using UnityEngine;

namespace AnimalGame.RobotArm
{
    public sealed partial class RobotArmController
    {
        private const float ClawSwapTime = .18f;
        private const float PlugExtendTime = .32f;
        private const float PlugAlignTime = .10f;
        private const float PlugAlignedPause = .12f;
        private const float PlugInsertTime = .30f;
        private const float PlugPressStart = PlugExtendTime + PlugAlignTime + PlugAlignedPause;
        private const float PlugStrokeEnd = PlugPressStart + PlugInsertTime;
        private const float PlugSeatedTime = PlugStrokeEnd + .10f;
        private const float PlugDepthPixels = 16f;
        // Pixel centres in the authored 48 x 77 plug sprite, measured from its lower left.
        private static readonly Vector2 PlugTipPixel = new Vector2(24f, 75.5f);
        private const float PlugWristPixelY = 13.5f;

        private Arm stationPlugArm;
        private Vector2 stationSocketWorld, stationInsertionWorld;
        private float stationPlugTime, stationElbowSide;
        private bool stationPlugFromDeployedPose, pullingStationPlug;
        private bool deferredStationGrab;

        public bool StationPlugActive => stationPlugArm != null;
        public bool StationPlugSeated => StationPlugActive && !pullingStationPlug && stationPlugTime >= PlugSeatedTime;
        internal bool CanStartStationPlug => isActiveAndEnabled && initialized && !StationPlugActive
            && CanOperate && Upright && !openingArmPresentation && !inventoryPresentationActive
            && !IsTumbleArmLayoutActive && State != RobotArmState.Recycling
            && (deploymentTime <= 0f || HandsReady);
        private Arm StationStandbyArm => stationPlugArm == left ? right : left;
        private float StationPlugRestTime => stationPlugFromDeployedPose ? -ClawSwapTime : 0f;

        public bool CanReachStationPlug(Vector2 socket, Vector2 direction) =>
            FindStationPlugArm(socket, direction) != null;

        public bool TryBeginStationPlug(Vector2 socket, Vector2 direction)
        {
            if (StationPlugActive) return false;
            Arm arm = FindStationPlugArm(socket, direction);
            if (arm == null) return false;
            stationPlugArm = arm;
            stationSocketWorld = socket;
            stationInsertionWorld = direction.normalized;
            stationPlugFromDeployedPose = HandsReady;
            stationPlugTime = StationPlugRestTime;
            pullingStationPlug = false;
            if (stationPlugFromDeployedPose)
            {
                if (heldObject != null && heldObject.TryGetComponent<AnimalGame.Garbage.HeavyGarbagePull>(out var pull))
                    pull.CancelPullImmediately();
                if (heldObject != null) Drop(keepArmsOpen: true);
                if (!this || !isActiveAndEnabled || !StationPlugActive) return false;
                SetHandGrip(false);
                Vector2 elbow = Direction(arm.Pose.LowerAngle) * arm.Pose.LowerLength;
                // Keep the existing elbow bend as the rigid arm swings from its driving pose.
                stationElbowSide = Mathf.Sign(Vector2.Dot(Vector2.Perpendicular(elbow), HandLocal(arm) - arm.Socket));
                return true;
            }

            EndGrabArmSession();
            Transform root = marker.MarkerVisualRoot;
            Vector2 axis = ((Vector2)root.InverseTransformVector(stationInsertionWorld)).normalized;
            Vector2 midStroke = StationWristDelta(arm, root.InverseTransformPoint(stationSocketWorld), axis, .5f);
            // Choose the outward-bending IK branch for front AND rear approaches.
            // Keep that branch throughout the stroke, even when the wrist passes the shoulder axis.
            stationElbowSide = midStroke.y >= 0f ? arm.Side : -arm.Side;
            SetHandGrip(false);
            ShowStationPlugArt(arm);
            return true;
        }

        public void RetractStationPlug()
        {
            if (!StationPlugActive) return;
            stationPlugTime = Mathf.Min(stationPlugTime, PlugStrokeEnd);
            pullingStationPlug = true;
        }

        public void CancelStationPlug()
        {
            if (!StationPlugActive) return;
            RestoreStationClawArt(stationPlugArm);
            if (stationPlugFromDeployedPose) ApplyVisuals(stationPlugArm);
            else
            {
                ApplyVisuals(stationPlugArm, default, false);
                ApplyVisuals(StationStandbyArm, default, false);
            }
            stationPlugArm = null;
        }

        private Arm FindStationPlugArm(Vector2 socket, Vector2 direction)
        {
            if (!isActiveAndEnabled || direction.sqrMagnitude < .000001f) return null;
            EnsureVisuals();
            if (!initialized || stationPlugSprite == null) return null;
            if (!StationPlugActive && !CanStartStationPlug) return null;
            Vector2 localSocket = marker.MarkerVisualRoot.InverseTransformPoint(socket);
            Vector2 axis = ((Vector2)marker.MarkerVisualRoot.InverseTransformVector(direction)).normalized;
            Arm nearest = localSocket.x < 0f ? left : right;
            if (StationPlugActive)
                return StationWristReachable(stationPlugArm, localSocket, axis) ? stationPlugArm : null;
            if (StationWristReachable(nearest, localSocket, axis)) return nearest;
            Arm other = nearest == left ? right : left;
            return StationWristReachable(other, localSocket, axis) ? other : null;
        }

        private float StationPlugPixelSize(Arm arm) => handScale * stationPlugScale / arm.OpenHandSprite.pixelsPerUnit;
        private float StationPlugLength(Arm arm) => (PlugTipPixel.y - PlugWristPixelY) * StationPlugPixelSize(arm);
        private static float StationWristMinimumReach(Arm arm) => Mathf.Abs(arm.UpperLength - arm.LowerLength) + .00002f;
        private float StationInsertionStroke => diameter * .46f;

        private bool StationWristReachable(Arm arm, Vector2 localSocket, Vector2 axis)
        {
            Vector2 seated = localSocket - arm.Socket - axis * StationPlugLength(arm);
            // Preserve the existing interaction range. The longer wind-up uses whatever
            // travel the two rigid links have available along the insertion axis.
            Vector2 beforeInsertion = seated - axis * (diameter * .14f);
            float reach = arm.UpperLength + arm.LowerLength - .0001f;
            if (Mathf.Max(seated.sqrMagnitude, beforeInsertion.sqrMagnitude) > reach * reach) return false;
            if (!HandsReady) return true;
            float minimum = StationWristMinimumReach(arm);
            return seated.sqrMagnitude > minimum * minimum;
        }

        private Vector2 StationWristDelta(Arm arm, Vector2 socket, Vector2 axis, float insertion)
        {
            Vector2 wristAtMouth = socket - arm.Socket - axis * StationPlugLength(arm);
            // Intersect the insertion line with the arm's reach circle. Only the available
            // travel changes at the edge of reach; the wrist still moves on the same line.
            float along = Vector2.Dot(wristAtMouth, axis);
            float lateral = Vector2.Dot(wristAtMouth, Vector2.Perpendicular(axis));
            float reach = arm.UpperLength + arm.LowerLength - .0001f;
            float axialLimit = Mathf.Sqrt(Mathf.Max(0f, reach * reach - lateral * lateral));
            float gap = Mathf.Clamp(axialLimit + along, 0f, StationInsertionStroke);
            float depth = Mathf.Clamp(axialLimit - along, 0f, PlugDepthPixels * StationPlugPixelSize(arm));
            if (stationPlugFromDeployedPose)
            {
                // Rigid links cannot cross the unreachable circle at the shoulder during insertion.
                float minimum = StationWristMinimumReach(arm);
                float innerLimit = Mathf.Sqrt(Mathf.Max(0f, minimum * minimum - lateral * lateral));
                if (innerLimit > 0f)
                {
                    if (along >= 0f) gap = Mathf.Min(gap, Mathf.Max(0f, along - innerLimit));
                    else depth = Mathf.Min(depth, Mathf.Max(0f, -along - innerLimit));
                }
            }
            return wristAtMouth + axis * Mathf.Lerp(-gap, depth, insertion);
        }

        private void StepStationPlug(float dt)
        {
            float restTime = StationPlugRestTime;
            if (pullingStationPlug)
            {
                // Withdraw the plug at its usual rate, then give the returning claw the full swap time.
                float speed = PlugStrokeEnd / retractDuration;
                float remaining = stationPlugTime > 0f ? stationPlugTime / speed : stationPlugTime;
                remaining = Mathf.MoveTowards(remaining, restTime, dt);
                if (remaining <= restTime)
                {
                    CancelStationPlug();
                    return;
                }
                stationPlugTime = remaining > 0f ? remaining * speed : remaining;
            }
            else stationPlugTime = Mathf.MoveTowards(stationPlugTime, PlugSeatedTime, dt);

            Arm arm = stationPlugArm;
            if (stationPlugTime < 0f)
            {
                RestoreStationClawArt(arm);
                Pose claw = arm.Pose;
                claw.Hand *= Mathf.SmoothStep(0f, 1f, -stationPlugTime / ClawSwapTime);
                ApplyVisuals(arm, claw, true);
                return;
            }

            ShowStationPlugArt(arm);
            Transform root = marker.MarkerVisualRoot;
            Vector2 localSocket = root.InverseTransformPoint(stationSocketWorld);
            Vector2 axis = ((Vector2)root.InverseTransformVector(stationInsertionWorld)).normalized;
            float deployment = Mathf.Clamp01(stationPlugTime / PlugExtendTime);
            float extension = Mathf.SmoothStep(0f, 1f, deployment);
            float alignment = Mathf.SmoothStep(0f, 1f, (stationPlugTime - PlugExtendTime) / PlugAlignTime);
            float insertion = Mathf.SmoothStep(0f, 1f, (stationPlugTime - PlugPressStart) / PlugInsertTime);
            Vector2 delta = StationWristDelta(arm, localSocket, axis, insertion);
            SetStationPlugLip(alignment >= 1f);
            Pose pose = stationPlugFromDeployedPose ? ReachWithDeployedArm(arm, delta, extension)
                : DeployStationArm(arm, delta * extension, extension, deployment);
            ApplyVisuals(arm, pose, stationPlugFromDeployedPose || extension > 0f);
            DrawStationPlugAtWrist(arm, pose.UpperAngle, axis, alignment, extension);
        }

        private Pose ReachWithDeployedArm(Arm arm, Vector2 wrist, float reach)
        {
            Pose pose = arm.Pose;
            SolveIK(wrist, pose.LowerLength, pose.UpperLength, stationElbowSide, out float lower, out float upper);
            pose.LowerAngle = Mathf.LerpAngle(pose.LowerAngle, lower, reach);
            pose.UpperAngle = Mathf.LerpAngle(pose.UpperAngle, upper, reach);
            pose.Hand = reach;
            return pose;
        }

        private Pose DeployStationArm(Arm arm, Vector2 delta, float extension, float deployment)
        {
            Arm standby = StationStandbyArm;
            ApplyVisuals(standby, DeploymentPose(standby, deployment * TotalDeploymentTime, StandbyHandCentre), deployment > 0f);

            // Near the shoulder, collapse the telescoping segment enough for the two-link IK to fold.
            float upper = arm.UpperLength * extension;
            float lower = arm.LowerLength * extension;
            float separation = delta.magnitude;
            var pose = new Pose
            {
                LowerLength = Mathf.Min(lower, upper + separation),
                UpperLength = Mathf.Min(upper, lower + separation),
                Hand = extension
            };
            SolveIK(delta, pose.LowerLength, pose.UpperLength, stationElbowSide, out pose.LowerAngle, out pose.UpperAngle);
            return pose;
        }

        private void DrawStationPlugAtWrist(Arm arm, float wristAngle, Vector2 axis, float alignment, float extension)
        {
            float rotation = Mathf.LerpAngle(wristAngle, Vector2.SignedAngle(Vector2.up, axis), alignment);
            arm.Hand.localRotation = Quaternion.Euler(0f, 0f, rotation);
            float pixelSize = StationPlugPixelSize(arm) * extension;
            arm.Hand.localScale = Vector3.one * (stationPlugSprite.pixelsPerUnit * pixelSize);
            Vector2 wristPixel = new Vector2(PlugTipPixel.x, PlugWristPixelY);
            arm.Hand.localPosition -= (Vector3)Rotate((wristPixel - stationPlugSprite.pivot) * pixelSize, rotation);
        }
    }
}
