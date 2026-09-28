using System.Collections.Generic;
using AnimalGame.MapTest;
using UnityEngine;
using AnimalGame.RobotMap;
using AnimalGame.World;

namespace AnimalGame.RobotArm
{
    public enum RobotArmState { Retracted, Extending, OuterOperating, Docking, Retracting, Recycling }

    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RobotMover), typeof(RobotMarkerView))]
    public sealed class RobotArmController : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] private KeyCode keyboardArmKey = KeyCode.CapsLock;
        [SerializeField] private KeyCode keyboardGrabKey = KeyCode.J;
        [SerializeField, Range(0f, .17f)] private float leftStickDeadZone = .08f;
        [Header("Artwork")]
        [SerializeField] private Sprite robotArmOneSprite;
        [SerializeField] private Sprite robotArmTwoSprite;
        [SerializeField] private Sprite robotHandSprite;
        [SerializeField] private Color armColor = Color.white;
        [SerializeField, Min(.01f)] private float artworkScale = .7f;
        [SerializeField, Min(.1f)] private float artworkThicknessScale = 2.57f;
        [Header("Fixed geometry (cached when created)")]
        [SerializeField, Range(0f, 1.2f)] private float socketRadiusOfBody = .88f;
        [Tooltip("Arm length reference in marker-local units; upper and lower segments use their respective percentages.")]
        [SerializeField, Min(.05f)] private float armLength = 1f;
        [SerializeField, Min(.05f)] private float upperArmLengthPercent = .45f;
        [SerializeField, Min(.05f)] private float lowerArmLengthPercent = .65f;
        [Tooltip("Total distance between hands, measured along robot-local X in body diameters.")]
        [SerializeField, Min(0f)] private float handSpacingOfBodyDiameter = .28f;
        [SerializeField, Min(.01f)] private float collisionWidthOfBodyDiameter = .08f;
        [Header("Deployment")]
        [SerializeField, Min(.01f)] private float connectorExtendDuration = .24f;
        [SerializeField, Min(.01f)] private float extendDuration = .3f;
        [SerializeField, Min(.01f)] private float handExtendDuration = .12f;
        [SerializeField, Min(.01f)] private float retractDuration = .4f;
        [Header("Control")]
        [SerializeField, Range(0f, 1f)] private float dockEnterMagnitude = .18f;
        [SerializeField, Range(0f, 1f)] private float dockExitMagnitude = .26f;
        [SerializeField, Min(0f)] private float dockEnterDelay = .1f;
        [SerializeField, Range(.3f, 1f)] private float maximumMagnitude = .95f;
        [SerializeField, Range(1f, 179f)] private float followAngle = 70f;
        [SerializeField, Min(1f)] private float bodyFollowSpeed = 100f;
        [SerializeField, Min(1f)] private float maximumAimSpeedDegreesPerSecond = 240f;
        [SerializeField, Min(.01f)] private float aimSmoothingTime = .2f;
        [Header("Docking (robot-local, body diameters)")]
        [SerializeField] private Vector2 smallDockPosition = new Vector2(0f, .65f);
        [SerializeField] private Vector2 mediumDockPosition = new Vector2(0f, .8f);
        [SerializeField, Min(.001f)] private float dockToleranceOfBodyDiameter = .045f;
        [SerializeField, Min(.01f)] private float recycleDuration = .35f;

        public RobotArmState State { get; private set; }
        public bool IsArmModeActive { get; private set; }
        // Kept for self-righting: input direction remains robot-local, using raw magnitude.
        public Vector2 CurrentTargetLocal { get; private set; }
        public float CurrentInputMagnitude { get; private set; }
        public float VisibleDeployment01 => Mathf.Clamp01(deploymentTime / TotalDeploymentTime);
        public bool IsRecycleReady { get; private set; }
        public bool IsBlocked { get; private set; }
        public WorldInteraction HeldObject => heldObject;
        public Vector2 LeftHandWorld => HandWorld(left);
        public Vector2 RightHandWorld => HandWorld(right);

        private RobotMover mover;
        private RobotMarkerView marker;
        private RobotTumbleController tumble;
        private PhotoModeController photoMode;
        private MapTestSceneController map;
        private Arm left, right;
        private float diameter, upperLength, lowerLength, handSpacing, armWidth, deploymentTime;
        private float dockTimer, recycleTime, stepDelta;
        private bool docked, previousGrab, initialized;
        private Vector2 inputLocal, targetLocal;
        private Vector3 framePosition;
        private Quaternion frameRotation;
        private WorldInteraction heldObject;
        private int heldHands;
        private Vector2 heldOffset;
        private Quaternion heldRotation;
        private Vector3 recycleStart;
        private readonly List<WorldInteraction> leftHits = new();
        private readonly List<WorldInteraction> rightHits = new();
        private float TotalDeploymentTime => connectorExtendDuration + extendDuration + handExtendDuration;
        private bool Upright => tumble == null || tumble.State == RobotTumbleState.Upright;
        private bool HandsReady => deploymentTime >= TotalDeploymentTime - .00001f && IsArmModeActive;

        private struct Pose
        {
            public float UpperAngle, LowerAngle, UpperLength, LowerLength, Hand;
            public static Pose Lerp(Pose a, Pose b, float t) => new Pose {
                UpperAngle = Mathf.LerpAngle(a.UpperAngle, b.UpperAngle, t),
                LowerAngle = Mathf.LerpAngle(a.LowerAngle, b.LowerAngle, t),
                UpperLength = Mathf.Lerp(a.UpperLength, b.UpperLength, t),
                LowerLength = Mathf.Lerp(a.LowerLength, b.LowerLength, t), Hand = Mathf.Lerp(a.Hand, b.Hand, t) };
        }
        private sealed class Arm
        {
            public float Side;
            public Vector2 Socket;
            public Pose Pose;
            public Transform Root, Upper, Lower, Hand;
            public SpriteRenderer UpperSprite, LowerSprite, CuffSprite, HandSprite;
            public RobotHandAnimation Animation;
        }

        private void Awake()
        {
            mover = GetComponent<RobotMover>();
            marker = GetComponent<RobotMarkerView>();
            tumble = GetComponent<RobotTumbleController>();
            photoMode = GetComponent<PhotoModeController>();
        }
        private void Start() { EnsureVisuals(); }
        private void EnsureVisuals()
        {
            if (left != null || marker == null || marker.MarkerVisualRoot == null) return;
            foreach (MapTestSceneController candidate in FindObjectsOfType<MapTestSceneController>())
                if (candidate.gameObject.scene == gameObject.scene) { map = candidate; break; }
            diameter = Mathf.Max(.01f, marker.BodyDiameter);
            upperLength = armLength * upperArmLengthPercent;
            lowerLength = armLength * lowerArmLengthPercent;
            handSpacing = diameter * handSpacingOfBodyDiameter;
            armWidth = diameter * collisionWidthOfBodyDiameter;
            left = CreateArm("Left Mechanical Arm", -1f);
            right = CreateArm("Right Mechanical Arm", 1f);
            targetLocal = Vector2.up * armLength * .8f;
            initialized = true;
            framePosition = transform.position; frameRotation = transform.rotation;
        }

        private void Update()
        {
            EnsureVisuals();
            if (!initialized) return;
            bool canOperate = (photoMode == null || !photoMode.IsActive)
                && (mover.MovementMode == RobotMovementMode.Driven || !Upright);
            bool armHeld = canOperate && (Input.GetKey(keyboardArmKey) || AdaptiveLegacyGamepadInput.IsLeftStickButtonHeld());
            bool grab = Input.GetKey(keyboardGrabKey) || AdaptiveLegacyGamepadInput.IsSouthFaceButtonHeld();
            Step(Time.deltaTime, ReadLocalInput(), armHeld, grab);
        }

        // Separate input sampling from simulation so the same transitions can be regression checked.
        private void Step(float deltaTime, Vector2 localInput, bool armHeld, bool grab)
        {
            stepDelta = Mathf.Max(0f, deltaTime);
            IsBlocked = false;
            IsArmModeActive = armHeld;
            mover.SetArmInputCaptured(armHeld);
            inputLocal = armHeld ? Vector2.ClampMagnitude(localInput, 1f) : Vector2.zero;
            CurrentInputMagnitude = inputLocal.magnitude;
            CurrentTargetLocal = inputLocal;
            if (heldObject != null && (!heldObject.Available || heldObject.Owner != this)) ClearHeld();

            // Releasing L3, falling or photo mode always drops first, never recycles.
            if (!armHeld || !Upright) Drop();
            if (!armHeld)
            {
                docked = false; dockTimer = 0f;
                State = deploymentTime > 0f ? RobotArmState.Retracting : RobotArmState.Retracted;
            }
            else if (State != RobotArmState.Recycling)
            {
                if (deploymentTime < TotalDeploymentTime - .00001f) State = RobotArmState.Extending;
                else UpdateDockState();
            }

            if (armHeld && State != RobotArmState.Recycling) TurnBodyForLocalInput();
            UpdateTarget();
            AdvanceArms(armHeld);
            FollowHeldObject();
            UpdateReady();
            if (State == RobotArmState.Recycling) UpdateRecycle();
            else if (armHeld && Upright && HandsReady)
            {
                if (!grab && heldObject != null)
                {
                    if (IsRecycleReady) BeginRecycle(); else Drop();
                }
                else if (grab && heldObject == null) TryGrab();
            }
            // Do not overwrite PlayRecycle with a release clip on the same A-release frame.
            if (State != RobotArmState.Recycling) SetHandGrip(HandsReady && Upright && grab);
            ApplyVisuals(left); ApplyVisuals(right);
            framePosition = transform.position; frameRotation = transform.rotation;
        }

        private void LateUpdate()
        {
            if (!initialized) return;
            // RobotMover runs after this controller. Check its translation AND rotation before rendering.
            if (Upright && deploymentTime > 0f)
                ConstrainBodyPose(framePosition, frameRotation, transform.position, transform.rotation);
            FollowHeldObject();
            UpdateReady();
        }

        private Vector2 ReadLocalInput()
        {
            Vector2 keyboard = new Vector2((Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f),
                (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f));
            keyboard = Vector2.ClampMagnitude(keyboard, 1f);
            Vector2 stick = AdaptiveLegacyGamepadInput.ReadRawLeftStick();
            return keyboard.sqrMagnitude >= stick.sqrMagnitude ? keyboard : stick;
        }
        private void UpdateDockState()
        {
            if (heldObject == null) { docked = false; dockTimer = 0f; }
            else if (docked)
            {
                if (CurrentInputMagnitude > dockExitMagnitude) { docked = false; dockTimer = 0f; }
            }
            else
            {
                dockTimer = CurrentInputMagnitude <= dockEnterMagnitude ? dockTimer + stepDelta : 0f;
                if (dockTimer >= dockEnterDelay) docked = true;
            }
            State = docked ? RobotArmState.Docking : RobotArmState.OuterOperating;
        }
        private void TurnBodyForLocalInput()
        {
            if (!Upright || CurrentInputMagnitude <= leftStickDeadZone) return;
            float angle = Vector2.SignedAngle(Vector2.up, inputLocal);
            if (Mathf.Abs(angle) <= followAngle) return;
            // Input stays body-local: turning does not consume the stick's angle.
            // Continue while held outside the limit, stop as soon as input returns inside it.
            float step = Mathf.Sign(angle) * bodyFollowSpeed * stepDelta;
            Quaternion requested = Quaternion.Euler(0f, 0f, transform.eulerAngles.z + step);
            ConstrainBodyPose(transform.position, transform.rotation, transform.position, requested);
        }
        private Vector2 DockLocal => diameter * (heldObject != null && heldObject.Size == RecyclableSize.Medium
            ? mediumDockPosition : smallDockPosition);
        private void UpdateTarget()
        {
            Vector2 direction = Vector2.up;
            if (CurrentInputMagnitude > leftStickDeadZone)
            {
                Vector2 local = inputLocal.normalized;
                float angle = Mathf.Clamp(Vector2.SignedAngle(Vector2.up, local), -followAngle, followAngle);
                direction = Direction(angle);
            }
            float radius = Mathf.Lerp(armLength * .65f, MaximumCommonReach(),
                Mathf.InverseLerp(dockEnterMagnitude, maximumMagnitude, CurrentInputMagnitude));
            Vector2 desired = direction * radius;
            if (docked && heldObject != null)
            {
                // Dock can still aim around the body. Zero input returns to the actual inlet.
                float angle = Vector2.SignedAngle(Vector2.up, direction);
                desired = Rotate(DockLocal, angle) - heldOffset - AnchorOffset();
            }
            float blend = 1f - Mathf.Exp(-stepDelta / Mathf.Max(.01f, aimSmoothingTime));
            targetLocal = Vector2.Lerp(targetLocal, desired, blend);
        }
        private float MaximumCommonReach()
        {
            float lateral = Mathf.Abs(left.Socket.x + handSpacing * .5f);
            float reach = upperLength + lowerLength - armLength * .005f;
            return Mathf.Sqrt(Mathf.Max(armLength * armLength * .25f, reach * reach - lateral * lateral));
        }

        private void AdvanceArms(bool extending)
        {
            float next = extending ? Mathf.Min(TotalDeploymentTime, deploymentTime + stepDelta)
                : Mathf.Max(0f, deploymentTime - stepDelta * TotalDeploymentTime / retractDuration);
            Pose lp = DesiredPose(left, next), rp = DesiredPose(right, next);
            Pose oldL = left.Pose, oldR = right.Pose;
            int steps = MotionSteps(oldL, lp, oldR, rp);
            float accepted = 0f;
            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                Pose nextL = Pose.Lerp(oldL, lp, t), nextR = Pose.Lerp(oldR, rp, t);
                if (PoseBlocked(left, left.Pose, nextL) || PoseBlocked(right, right.Pose, nextR)) { IsBlocked = true; break; }
                left.Pose = nextL; right.Pose = nextR; accepted = t;
            }
            deploymentTime = Mathf.Lerp(deploymentTime, next, accepted);
            if (extending && HandsReady && State == RobotArmState.Extending) UpdateDockState();
            if (!extending && deploymentTime <= .00001f) State = RobotArmState.Retracted;
        }
        private Pose DesiredPose(Arm arm, float progress)
        {
            Vector2 desired = targetLocal + Vector2.right * arm.Side * handSpacing * .5f;
            SolveIK(desired - arm.Socket, upperLength, lowerLength, arm.Side, out float upper, out float lower);
            float delta = maximumAimSpeedDegreesPerSecond * stepDelta;
            return new Pose {
                UpperAngle = Mathf.MoveTowardsAngle(arm.Pose.UpperAngle, upper, delta),
                LowerAngle = Mathf.MoveTowardsAngle(arm.Pose.LowerAngle, lower, delta),
                UpperLength = upperLength * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / connectorExtendDuration)),
                LowerLength = lowerLength * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((progress - connectorExtendDuration) / extendDuration)),
                Hand = Mathf.Clamp01((progress - connectorExtendDuration - extendDuration) / handExtendDuration) };
        }
        public static void SolveIK(Vector2 target, float upper, float lower, float side, out float upperAngle, out float lowerAngle)
        {
            upper = Mathf.Max(.0001f, upper); lower = Mathf.Max(.0001f, lower);
            float distance = Mathf.Clamp(target.magnitude, Mathf.Abs(upper - lower) + .00001f, upper + lower - .00001f);
            Vector2 direction = target.sqrMagnitude > 1e-10f ? target.normalized : Vector2.up;
            float cosine = Mathf.Clamp((upper * upper + distance * distance - lower * lower) / (2f * upper * distance), -1f, 1f);
            upperAngle = Vector2.SignedAngle(Vector2.up, direction) + Mathf.Acos(cosine) * Mathf.Rad2Deg * -side;
            Vector2 elbow = Direction(upperAngle) * upper;
            lowerAngle = Vector2.SignedAngle(Vector2.up, direction * distance - elbow);
        }
        private int MotionSteps(Pose a, Pose b, Pose c, Pose d)
        {
            float travel = Mathf.Max(PoseTravel(a, b), PoseTravel(c, d));
            return Mathf.Max(1, Mathf.CeilToInt(travel / Mathf.Max(.0001f, armWidth * .2f)));
        }
        private float PoseTravel(Pose a, Pose b) =>
            Mathf.Abs(Mathf.DeltaAngle(a.UpperAngle, b.UpperAngle)) * Mathf.Deg2Rad * upperLength
            + Mathf.Abs(Mathf.DeltaAngle(a.LowerAngle, b.LowerAngle)) * Mathf.Deg2Rad * lowerLength
            + Mathf.Abs(a.UpperLength - b.UpperLength) + Mathf.Abs(a.LowerLength - b.LowerLength);
        private bool PoseBlocked(Arm arm, Pose before, Pose after)
        {
            for (int segment = 0; segment < 2; segment++)
            {
                float length = segment == 0 ? after.UpperLength : after.LowerLength;
                if (length < .00001f) continue;
                InteractionShape next = SegmentShape(arm, after, segment);
                InteractionShape? previous = (segment == 0 ? before.UpperLength : before.LowerLength) > .00001f
                    ? SegmentShape(arm, before, segment) : (InteractionShape?)null;
                if (WorldInteractionQuery.Query(next, WorldInteractionKind.Collision, map, gameObject.scene,
                    ignore: transform, previous: previous, ignoreHeld: heldObject != null ? heldObject.transform : null)) return true;
            }
            return false;
        }
        private InteractionShape SegmentShape(Arm arm, Pose pose, int segment)
        {
            Vector2 elbow = arm.Socket + Direction(pose.UpperAngle) * pose.UpperLength;
            Vector2 a = segment == 0 ? arm.Socket : elbow;
            Vector2 b = segment == 0 ? elbow : elbow + Direction(pose.LowerAngle) * pose.LowerLength;
            Vector2 normal = new Vector2(-(b - a).y, (b - a).x).normalized * armWidth * .5f;
            Transform frame = marker.MarkerVisualRoot;
            return new InteractionShape { IsBox = true,
                A = InteractionShape.ToQuery(frame.TransformPoint(a - normal), map),
                B = InteractionShape.ToQuery(frame.TransformPoint(b - normal), map),
                C = InteractionShape.ToQuery(frame.TransformPoint(b + normal), map),
                D = InteractionShape.ToQuery(frame.TransformPoint(a + normal), map) };
        }
        private void ConstrainBodyPose(Vector3 start, Quaternion rotation, Vector3 end, Quaternion endRotation)
        {
            if (!initialized || deploymentTime <= 0f) { transform.SetPositionAndRotation(end, endRotation); return; }
            float scale = Mathf.Max(.0001f, marker.MarkerVisualRoot.lossyScale.x);
            float distance = Vector3.Distance(start, end) / scale
                + Quaternion.Angle(rotation, endRotation) * Mathf.Deg2Rad * (diameter + upperLength + lowerLength);
            int steps = Mathf.Max(1, Mathf.CeilToInt(distance / Mathf.Max(.0001f, armWidth * .2f)));
            transform.SetPositionAndRotation(start, rotation);
            for (int i = 1; i <= steps; i++)
            {
                InteractionShape l0 = SegmentShape(left, left.Pose, 0), l1 = SegmentShape(left, left.Pose, 1);
                InteractionShape r0 = SegmentShape(right, right.Pose, 0), r1 = SegmentShape(right, right.Pose, 1);
                Vector3 safePosition = transform.position; Quaternion safeRotation = transform.rotation;
                float t = i / (float)steps;
                transform.SetPositionAndRotation(Vector3.Lerp(start, end, t), Quaternion.Slerp(rotation, endRotation, t));
                if (BodySegmentBlocked(left, 0, l0) || BodySegmentBlocked(left, 1, l1)
                    || BodySegmentBlocked(right, 0, r0) || BodySegmentBlocked(right, 1, r1))
                {
                    transform.SetPositionAndRotation(safePosition, safeRotation); IsBlocked = true; break;
                }
            }
        }
        private bool BodySegmentBlocked(Arm arm, int segment, InteractionShape previous)
        {
            if ((segment == 0 ? arm.Pose.UpperLength : arm.Pose.LowerLength) < .00001f) return false;
            return WorldInteractionQuery.Query(SegmentShape(arm, arm.Pose, segment), WorldInteractionKind.Collision,
                map, gameObject.scene, ignore: transform, previous: previous,
                ignoreHeld: heldObject != null ? heldObject.transform : null);
        }

        private void TryGrab()
        {
            QueryHand(left, leftHits); QueryHand(right, rightHits);
            WorldInteraction best = null; int mask = 0; float bestDistance = float.PositiveInfinity;
            for (int pass = 0; pass < 2; pass++)
            {
                List<WorldInteraction> hits = pass == 0 ? leftHits : rightHits;
                foreach (WorldInteraction item in hits)
                {
                    if (item.Owner != null) continue;
                    int hands = (leftHits.Contains(item) ? 1 : 0) | (rightHits.Contains(item) ? 2 : 0);
                    if (item.RequiredHands == 2 && hands != 3) continue;
                    if (item.RequiredHands == 1) hands = (hands & 1) != 0 ? 1 : 2;
                    Vector2 anchor = hands == 3 ? (HandWorld(left) + HandWorld(right)) * .5f
                        : HandWorld(hands == 1 ? left : right);
                    float distance = ((Vector2)item.transform.position - anchor).sqrMagnitude;
                    if (distance < bestDistance || (Mathf.Approximately(distance, bestDistance)
                        && best != null && item.GetInstanceID() < best.GetInstanceID()))
                    { best = item; mask = hands; bestDistance = distance; }
                }
            }
            if (best == null || !best.TryGrab(this)) return;
            heldObject = best; heldHands = mask;
            heldOffset = marker.MarkerVisualRoot.InverseTransformVector(best.transform.position - (Vector3)HeldAnchor());
            heldRotation = Quaternion.Inverse(transform.rotation) * best.transform.rotation;
            SetHandGrip(true);
        }
        private void QueryHand(Arm arm, List<WorldInteraction> results)
        {
            Vector2 point = InteractionShape.ToQuery(HandWorld(arm), map);
            WorldInteractionQuery.Query(InteractionShape.Capsule(point, point, 0f), WorldInteractionKind.Grabbable,
                map, gameObject.scene, results, transform);
        }
        private Vector2 AnchorOffset() => heldHands == 3 ? Vector2.zero
            : Vector2.right * (heldHands == 1 ? -1f : 1f) * handSpacing * .5f;
        private Vector2 HeldAnchor() => heldHands == 3 ? (HandWorld(left) + HandWorld(right)) * .5f
            : HandWorld(heldHands == 1 ? left : right);
        private void FollowHeldObject()
        {
            if (heldObject == null || State == RobotArmState.Recycling) return;
            Vector3 position = (Vector3)HeldAnchor() + marker.MarkerVisualRoot.TransformVector(heldOffset);
            position.z = heldObject.transform.position.z;
            heldObject.transform.SetPositionAndRotation(position, transform.rotation * heldRotation);
        }
        private void UpdateReady()
        {
            IsRecycleReady = State == RobotArmState.Docking && heldObject != null && heldObject.Recyclable
                && ((Vector2)marker.MarkerVisualRoot.InverseTransformPoint(heldObject.transform.position) - DockLocal).magnitude
                <= diameter * dockToleranceOfBodyDiameter;
        }
        private void BeginRecycle()
        {
            State = RobotArmState.Recycling; recycleTime = 0f;
            recycleStart = marker.MarkerVisualRoot.InverseTransformPoint(heldObject.transform.position);
            SetHandGrip(false);
            left.Animation.PlayRecycle(); right.Animation.PlayRecycle();
            IsRecycleReady = false;
        }
        private void UpdateRecycle()
        {
            if (heldObject == null) { State = RobotArmState.OuterOperating; return; }
            recycleTime += stepDelta;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(recycleTime / recycleDuration));
            Vector3 inlet = new Vector3(0f, diameter * .35f, recycleStart.z);
            heldObject.transform.position = marker.MarkerVisualRoot.TransformPoint(Vector3.Lerp(recycleStart, inlet, t));
            if (recycleTime < recycleDuration) return;
            WorldInteraction item = heldObject;
            ClearHeld();
            State = RobotArmState.OuterOperating;
            item.Recycle(this);
        }
        private void Drop()
        {
            if (heldObject != null)
            {
                WorldInteraction item = heldObject;
                ClearHeld(); item.Release(this);
            }
            SetHandGrip(false);
            if (State == RobotArmState.Recycling) State = RobotArmState.OuterOperating;
        }
        private void SetHandGrip(bool closed)
        {
            if (closed == previousGrab || left == null || right == null) return;
            previousGrab = closed;
            if (closed) { left.Animation.PlayGrab(); right.Animation.PlayGrab(); }
            else { left.Animation.PlayRelease(); right.Animation.PlayRelease(); }
        }
        private void ClearHeld() { heldObject = null; heldHands = 0; docked = false; dockTimer = 0f; IsRecycleReady = false; }
        private Vector2 HandLocal(Arm arm) => arm.Socket + Direction(arm.Pose.UpperAngle) * arm.Pose.UpperLength
            + Direction(arm.Pose.LowerAngle) * arm.Pose.LowerLength;
        private Vector2 HandWorld(Arm arm) => arm == null ? (Vector2)transform.position
            : (Vector2)marker.MarkerVisualRoot.TransformPoint(HandLocal(arm));

        private Arm CreateArm(string name, float side)
        {
            var arm = new Arm { Side = side, Socket = Vector2.right * side * diameter * .5f * socketRadiusOfBody };
            arm.Root = new GameObject(name).transform;
            arm.Root.SetParent(marker.MarkerVisualRoot, false);
            arm.UpperSprite = CreateSprite(arm.Root, "Upper Arm", robotArmTwoSprite != null ? robotArmTwoSprite : robotArmOneSprite, 996);
            arm.LowerSprite = CreateSprite(arm.Root, "Lower Arm", robotArmTwoSprite != null ? robotArmTwoSprite : robotArmOneSprite, 997);
            arm.CuffSprite = CreateSprite(arm.Root, "Wrist Cuff", robotArmOneSprite, 998);
            arm.HandSprite = CreateSprite(arm.Root, "Mechanical Hand", robotHandSprite, 999);
            arm.Upper = arm.UpperSprite.transform; arm.Lower = arm.LowerSprite.transform; arm.Hand = arm.HandSprite.transform;
            arm.Animation = arm.Hand.gameObject.AddComponent<RobotHandAnimation>();
            arm.Root.gameObject.SetActive(false);
            return arm;
        }
        private SpriteRenderer CreateSprite(Transform parent, string name, Sprite sprite, int order)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var renderer = go.AddComponent<SpriteRenderer>(); renderer.sprite = sprite;
            renderer.color = armColor; renderer.sortingOrder = order;
            if (marker.ForegroundSpriteMaterial != null) renderer.sharedMaterial = marker.ForegroundSpriteMaterial;
            return renderer;
        }
        private void ApplyVisuals(Arm arm)
        {
            arm.Root.gameObject.SetActive(deploymentTime > 0f);
            Vector2 elbow = arm.Socket + Direction(arm.Pose.UpperAngle) * arm.Pose.UpperLength;
            SetSegmentVisual(arm.UpperSprite, arm.Socket, arm.Pose.UpperAngle, arm.Pose.UpperLength);
            SetSegmentVisual(arm.LowerSprite, elbow, arm.Pose.LowerAngle, arm.Pose.LowerLength);
            arm.HandSprite.enabled = arm.Pose.Hand > 0f;
            arm.Hand.localRotation = Quaternion.Euler(0f, 0f, arm.Pose.LowerAngle);
            arm.Hand.localScale = Vector3.one * artworkScale * arm.Pose.Hand;
            // Source hand art is centred at image Y=35 in the 128-pixel canvas.
            Vector2 handArtOffset = new Vector2(0f, (63.5f - 35f) / PixelsPerUnit(arm.HandSprite));
            arm.Hand.localPosition = HandLocal(arm) - Rotate(handArtOffset * artworkScale * arm.Pose.Hand, arm.Pose.LowerAngle);
            arm.CuffSprite.enabled = arm.Pose.LowerLength > .00001f;
            float cuffReveal = Mathf.Clamp01(arm.Pose.LowerLength / lowerLength);
            arm.CuffSprite.transform.localRotation = arm.Hand.localRotation;
            arm.CuffSprite.transform.localScale = new Vector3(artworkScale * artworkThicknessScale, artworkScale * cuffReveal, 1f);
            arm.CuffSprite.transform.localPosition = HandLocal(arm)
                - Direction(arm.Pose.LowerAngle) * (28.5f / PixelsPerUnit(arm.CuffSprite) * artworkScale * cuffReveal);
        }
        private void SetSegmentVisual(SpriteRenderer renderer, Vector2 start, float angle, float length)
        {
            renderer.enabled = length > .00001f;
            float ppu = PixelsPerUnit(renderer);
            // Preserve the existing Robot_Arm_2 attachment coordinates (image Y 96 to 60).
            float sourceLength = 36f / ppu;
            float scaleY = length / sourceLength;
            renderer.transform.localScale = new Vector3(artworkScale * artworkThicknessScale, scaleY, 1f);
            renderer.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            renderer.transform.localPosition = start + Direction(angle) * (32.5f / ppu * scaleY);
        }
        private static float PixelsPerUnit(SpriteRenderer renderer) => renderer.sprite != null ? renderer.sprite.pixelsPerUnit : 100f;
        private static Vector2 Direction(float angle) => Rotate(Vector2.up, angle);
        private static Vector2 Rotate(Vector2 vector, float angle)
        {
            float radians = angle * Mathf.Deg2Rad, c = Mathf.Cos(radians), s = Mathf.Sin(radians);
            return new Vector2(c * vector.x - s * vector.y, s * vector.x + c * vector.y);
        }
        private void OnDisable()
        {
            Drop(); IsArmModeActive = false; CurrentInputMagnitude = 0f; CurrentTargetLocal = Vector2.zero;
            mover?.SetArmInputCaptured(false);
            deploymentTime = 0f; State = RobotArmState.Retracted; previousGrab = false;
            if (left != null) { left.Pose = default; left.Root.gameObject.SetActive(false); }
            if (right != null) { right.Pose = default; right.Root.gameObject.SetActive(false); }
        }
        private void OnDestroy()
        {
            if (left != null && left.Root != null) Destroy(left.Root.gameObject);
            if (right != null && right.Root != null) Destroy(right.Root.gameObject);
        }
        private void OnDrawGizmosSelected()
        {
            if (!initialized || marker == null || marker.MarkerVisualRoot == null) return;
            Matrix4x4 previous = Gizmos.matrix;
            Color previousColor = Gizmos.color;
            Gizmos.matrix = marker.MarkerVisualRoot.localToWorldMatrix;
            Gizmos.color = IsBlocked ? Color.red : Color.cyan;
            DrawArmBoxes(left); DrawArmBoxes(right);
            Gizmos.color = IsRecycleReady ? Color.green : Color.yellow;
            Gizmos.DrawWireSphere(DockLocal, diameter * dockToleranceOfBodyDiameter);
            Gizmos.matrix = previous; Gizmos.color = previousColor;
        }
        private void DrawArmBoxes(Arm arm)
        {
            Vector2 elbow = arm.Socket + Direction(arm.Pose.UpperAngle) * arm.Pose.UpperLength;
            DrawBox(arm.Socket, elbow);
            DrawBox(elbow, HandLocal(arm));
        }
        private void DrawBox(Vector2 start, Vector2 end)
        {
            Vector2 normal = new Vector2(-(end - start).y, (end - start).x).normalized * armWidth * .5f;
            Gizmos.DrawLine(start - normal, end - normal); Gizmos.DrawLine(end - normal, end + normal);
            Gizmos.DrawLine(end + normal, start + normal); Gizmos.DrawLine(start + normal, start - normal);
        }
        private void OnValidate()
        {
            connectorExtendDuration = Mathf.Max(.01f, connectorExtendDuration); extendDuration = Mathf.Max(.01f, extendDuration);
            handExtendDuration = Mathf.Max(.01f, handExtendDuration); retractDuration = Mathf.Max(.01f, retractDuration);
            recycleDuration = Mathf.Max(.01f, recycleDuration);
            dockEnterMagnitude = Mathf.Clamp(dockEnterMagnitude, 0f, .9f);
            dockExitMagnitude = Mathf.Clamp(dockExitMagnitude, dockEnterMagnitude + .001f, .99f);
            maximumMagnitude = Mathf.Clamp(maximumMagnitude, dockExitMagnitude + .001f, 1f);
            leftStickDeadZone = Mathf.Clamp(leftStickDeadZone, 0f, dockEnterMagnitude);
        }
    }
}
