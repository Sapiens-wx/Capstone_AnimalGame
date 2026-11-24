using UnityEngine;

namespace AnimalGame.RobotArm
{
    public sealed partial class RobotArmController
    {
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
        private bool pullingStationPlug;

        public bool StationPlugActive => stationPlugArm != null;
        public bool StationPlugSeated => StationPlugActive && !pullingStationPlug && stationPlugTime >= PlugSeatedTime;
        private Arm StationStandbyArm => stationPlugArm == left ? right : left;

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
            Transform root = marker.MarkerVisualRoot;
            Vector2 axis = ((Vector2)root.InverseTransformVector(direction)).normalized;
            Vector2 midStroke = StationWristDelta(arm, root.InverseTransformPoint(socket), axis, .5f);
            // Choose the outward-bending IK branch for front AND rear approaches.
            // Keep that branch throughout the stroke, even when the wrist passes the shoulder axis.
            stationElbowSide = midStroke.y >= 0f ? arm.Side : -arm.Side;
            stationPlugTime = 0f;
            pullingStationPlug = false;
            SetHandGrip(false);
            arm.HandSprite.sprite = stationPlugSprite;
            BeginStationPlugVisual(arm, socket, stationInsertionWorld);
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
            EndStationPlugVisual(stationPlugArm);
            stationPlugArm.HandSprite.sprite = stationPlugArm.OpenHandSprite;
            ApplyVisuals(stationPlugArm, default, false);
            ApplyVisuals(StationStandbyArm, default, false);
            stationPlugArm = null;
        }

        private Arm FindStationPlugArm(Vector2 socket, Vector2 direction)
        {
            if (!isActiveAndEnabled || direction.sqrMagnitude < .000001f) return null;
            EnsureVisuals();
            if (!initialized || stationPlugSprite == null) return null;
            if (!StationPlugActive && (!CanOperate || !Upright || openingArmPresentation ||
                inventoryPresentationActive || State != RobotArmState.Retracted || IsArmModeActive))
                return null;
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
        private float StationInsertionStroke => diameter * .46f;

        private bool StationWristReachable(Arm arm, Vector2 localSocket, Vector2 axis)
        {
            Vector2 seated = localSocket - arm.Socket - axis * StationPlugLength(arm);
            // Preserve the existing interaction range. The longer wind-up uses whatever
            // travel the two rigid links have available along the insertion axis.
            Vector2 beforeInsertion = seated - axis * (diameter * .14f);
            float reach = arm.UpperLength + arm.LowerLength - .0001f;
            return Mathf.Max(seated.sqrMagnitude, beforeInsertion.sqrMagnitude) <= reach * reach;
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
            return wristAtMouth + axis * Mathf.Lerp(-gap, depth, insertion);
        }

        private void StepStationPlug(float dt)
        {
            stationPlugTime = Mathf.MoveTowards(stationPlugTime, pullingStationPlug ? 0f : PlugSeatedTime,
                pullingStationPlug ? dt * PlugStrokeEnd / retractDuration : dt);
            if (pullingStationPlug && stationPlugTime <= 0f)
            {
                CancelStationPlug();
                return;
            }

            Arm arm = stationPlugArm;
            Transform root = marker.MarkerVisualRoot;
            Vector2 localSocket = root.InverseTransformPoint(stationSocketWorld);
            Vector2 axis = ((Vector2)root.InverseTransformVector(stationInsertionWorld)).normalized;
            float deployment = Mathf.Clamp01(stationPlugTime / PlugExtendTime);
            Arm standby = StationStandbyArm;
            ApplyVisuals(standby, DeploymentPose(standby, deployment * TotalDeploymentTime, StandbyHandCentre), deployment > 0f);
            float extension = Mathf.SmoothStep(0f, 1f, deployment);
            float alignment = Mathf.SmoothStep(0f, 1f, (stationPlugTime - PlugExtendTime) / PlugAlignTime);
            float insertion = Mathf.SmoothStep(0f, 1f, (stationPlugTime - PlugPressStart) / PlugInsertTime);
            Vector2 delta = StationWristDelta(arm, localSocket, axis, insertion) * extension;
            SetStationPlugLip(alignment >= 1f);

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
            ApplyVisuals(arm, pose, extension > 0f);
            float rotation = Mathf.LerpAngle(pose.UpperAngle, Vector2.SignedAngle(Vector2.up, axis), alignment);
            arm.Hand.localRotation = Quaternion.Euler(0f, 0f, rotation);
            float pixelSize = StationPlugPixelSize(arm) * extension;
            arm.Hand.localScale = Vector3.one * (stationPlugSprite.pixelsPerUnit * pixelSize);
            Vector2 wristPixel = new Vector2(PlugTipPixel.x, PlugWristPixelY);
            arm.Hand.localPosition -= (Vector3)Rotate((wristPixel - stationPlugSprite.pivot) * pixelSize, rotation);
        }
    }
}
