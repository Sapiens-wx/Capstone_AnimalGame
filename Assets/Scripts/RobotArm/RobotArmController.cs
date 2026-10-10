using System.Collections.Generic;
using AnimalGame.MapTest;
using UnityEngine;
using UnityEngine.Serialization;
using AnimalGame.RobotMap;
using AnimalGame.World;
using AnimalGame.Garbage;
using System.Runtime.CompilerServices;

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
        [SerializeField] private KeyCode keyboardGrabKey = KeyCode.Space;
        [SerializeField, Range(0f, .17f)] private float leftStickDeadZone = .08f;
        [Header("Artwork")]
        [FormerlySerializedAs("robotHandSprite")]
        [SerializeField] private Sprite robotHandOpenSprite;
        [SerializeField] private Sprite robotHandClosedSprite;
        [SerializeField] private Color armColor = Color.white;
        [Tooltip("Uniform multiplier of the authored Upper Arm and Lower Arm scales. Also determines their IK lengths.")]
        [SerializeField, Min(.01f)] private float armScale = 1f;
        [FormerlySerializedAs("artworkScale")]
        [SerializeField, Min(.01f)] private float handScale = .7f;
        [Header("Fixed geometry (cached when created)")]
        [SerializeField, Range(0f, 1.2f)] private float socketRadiusOfBody = .88f;
        [Tooltip("Deployed Y scale multiplier of Lower Arm/Loopable. The body-side end stays at its authored attachment while the other end extends forward.")]
        [SerializeField, Min(0f)] private float lowerArmScale = 1f;
        [Tooltip("Forearm pivot offset from the body-side arm's tip, in its unscaled artwork-local coordinates. X is mirrored between arms; the offset follows Arm Scale and is cached when created.")]
        [SerializeField] private Vector2 forearmAnchorOffset = Vector2.zero;
        [SerializeField, Range(.05f, 1f)] private float staticArmLengthPercent = .4f;
        [Tooltip("Total distance between hands, measured along robot-local X in body diameters.")]
        [SerializeField, Min(0f)] private float handSpacingOfBodyDiameter = .28f;
        [SerializeField, Min(.01f)] private float collisionWidthOfBodyDiameter = .08f;
        [Header("Deployment")]
        [SerializeField, Min(.01f)] private float connectorExtendDuration = .24f;
        [SerializeField, Min(.01f)] private float extendDuration = .3f;
        [SerializeField, Min(.01f)] private float handExtendDuration = .12f;
        [SerializeField, Min(.01f)] private float retractDuration = .4f;
        [Header("Control")]
        [SerializeField, Range(0f, 1f)] private float dockEnterMagnitude = .4f;
        [SerializeField, Range(0f, 1f)] private float dockExitMagnitude = .5f;
        [SerializeField, Range(.3f, 1f)] private float maximumMagnitude = .95f;
        [SerializeField, Range(1f, 179f)] private float followAngle = 70f;
        [SerializeField, Min(1f)] private float maximumAimSpeedDegreesPerSecond = 240f;
        [SerializeField, Min(.01f)] private float aimSmoothingTime = .2f;
        [Header("Docking (robot-local, arm length fractions)")]
        [SerializeField] private Vector2 smallDockPosition = new Vector2(0f, .65f);
        [SerializeField] private Vector2 mediumDockPosition = new Vector2(0f, .8f);
        [Tooltip("Half-width of the chest recycling area, measured in body diameters. Releasing A inside starts recycling immediately.")]
        [SerializeField, Min(.01f)] private float recycleZoneHalfWidthOfBodyDiameter = .35f;
        [Tooltip("Half-depth of the chest recycling area, measured in body diameters.")]
        [SerializeField, Min(.01f)] private float recycleZoneHalfDepthOfBodyDiameter = .3f;
        [SerializeField, Min(.01f)] private float recycleDuration = .35f;
        [Header("Recycling")]
        [Tooltip("Minimum forward hand position during recycling, as a fraction of arm length.")]
        [SerializeField, Min(0f)] private float recycleHandMinimumForward = .4f;
        [Header("Small Recycling Processing")]
        [Tooltip("Small garbage post-feed processing time, including the simultaneous hand release.")]
        [SerializeField, Min(.01f)] private float smallRecycleProcessingDuration = .45f;
        [Tooltip("Small garbage processing screen and gamepad strength, including the inlet confirmation, relative to medium garbage.")]
        [SerializeField, Range(0f, 1f)] private float smallRecycleProcessingFeedbackMultiplier = .6f;
        [Header("Medium Recycling")]
        [SerializeField] private Shader mediumRecycleInletShader;
        [SerializeField, Min(.01f)] private float mediumRecycleClampDuration = .2f;
        [Tooltip("Fraction of the full feed distance reached while aligning, before the first loaded press.")]
        [SerializeField, Range(0f, .25f)] private float mediumRecycleInitialFeedFraction = .12f;
        [SerializeField] private Vector3 mediumRecycleStrokeDurations = new Vector3(.75f, .85f, .95f);
        [SerializeField, Min(.01f)] private float mediumRecycleFinishDuration = .25f;
        [Tooltip("Medium garbage post-feed processing time, including the simultaneous hand release.")]
        [SerializeField, Min(.01f)] private float mediumRecycleProcessingDuration = .95f;
        [SerializeField, Min(0f)] private float mediumRecycleProcessingFadeDuration = .10f;
        [SerializeField, Range(0f, .05f)] private float mediumRecycleProcessingBodyShakeOfBodyDiameter = .01f;
        [SerializeField, Range(0f, .05f)] private float mediumRecycleLockRecoilFraction = .012f;
        [SerializeField, Range(0f, .05f)] private float mediumRecycleHandShakeOfBodyDiameter = .012f;
        [SerializeField, Min(1f)] private float mediumRecycleHandShakeFrequency = 24f;
        [SerializeField, Range(0f, 8f)] private float mediumRecycleHandShakeDegrees = 2.5f;
        [SerializeField, Range(0f, .05f)] private float mediumRecycleGarbageShakeOfBodyDiameter = .03f;
        [SerializeField, Min(1f)] private float mediumRecycleGarbageShakeFrequency = 19f;
        [SerializeField, Range(0f, .08f)] private float mediumRecycleBodyRecoilOfBodyDiameter = .03f;
        [SerializeField, Range(0f, 3f)] private float mediumRecycleBodyRollDegrees = .6f;

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
        public MediumRecycleFrame CurrentMediumRecycleFrame { get; private set; }
        public Vector2 MediumRecycleHandVisualOffset => handShakeOffset;
        public float MediumRecycleHandVisualRollDegrees => handShakeRoll;
        public Vector2 MediumRecycleGarbageVisualOffset => garbageShakeOffset;

        private RobotMover mover;
        private RobotMarkerView marker;
        private RobotTumbleController tumble;
        private PhotoModeController photoMode;
        private RobotCameraShake grabFeedback;
        private MapTestSceneController map;
        private Arm left, right;
        private float diameter, upperLength, lowerLength, handSpacing, armWidth, deploymentTime;
        private float armLength;
        private float recycleTime, stepDelta;
        private bool docked, previousGrab, initialized;
        private Vector2 inputLocal, targetLocal;
        private Vector3 framePosition;
        private Quaternion frameRotation;
        private WorldInteraction heldObject;
        private int heldHands;
        private Vector2 heldOffset;
        private Quaternion heldRotation;
        private Vector3 recycleStart;
        private Vector3 recycleSafePosition;
        private Quaternion recycleSafeRotation;
        private Vector3 recycleStartScale, recyclePosition;
        private Vector2 recycleHandStart;
        private MediumRecycleInletClip mediumRecycleClip;
        private Vector3 mediumRecycleEnd;
        private float mediumRecycleInletY, mediumRecycleClipY;
        private Vector2 handShakeOffset;
        private float handShakeRoll;
        private Vector2 garbageShakeOffset;
        private int mediumRecycleStopsPlayed;
        private bool mediumRecycleActive, mediumRecycleCompletionPlayed, recyclePresentationActive, recycleCompleting;
        private readonly List<WorldInteraction> leftHits = new();
        private readonly List<WorldInteraction> rightHits = new();
        private readonly List<WorldInteraction> heldSolids = new();
        private readonly InteractionPushPlan contactPlan = new();
        private Vector2? desiredGripOffset;
        public Transform HeldMotionRoot => heldObject != null ? heldObject.MotionRoot : null;
        private bool FollowsHands => heldObject != null && heldObject.Available && heldObject.Owner == this
            && State != RobotArmState.Recycling && mover.GrabMovementMultiplier > 0f
            && !heldObject.TryGetComponent<HeavyGarbagePull>(out _);
        private float TotalDeploymentTime => connectorExtendDuration + extendDuration + handExtendDuration;
        private bool Upright => tumble == null || tumble.State == RobotTumbleState.Upright;
        private bool CanOperate => (photoMode == null || !photoMode.IsActive)
            && (mover.MovementMode == RobotMovementMode.Driven || !Upright);
        private bool HandsDeployed => deploymentTime >= TotalDeploymentTime - .00001f;
        private bool HandsReady => HandsDeployed && IsArmModeActive;
        private bool IsProcessingRecycle => State == RobotArmState.Recycling
            && (CurrentMediumRecycleFrame.Phase == MediumRecyclePhase.Processing
                || CurrentMediumRecycleFrame.Phase == MediumRecyclePhase.Complete);

        private struct Pose
        {
            public float UpperAngle, LowerAngle, UpperLength, LowerLength, Hand;
            public static Pose Lerp(Pose a, Pose b, float t) => new Pose {
                UpperAngle = Mathf.LerpAngle(a.UpperAngle, b.UpperAngle, t),
                LowerAngle = Mathf.LerpAngle(a.LowerAngle, b.LowerAngle, t),
                UpperLength = Mathf.Lerp(a.UpperLength, b.UpperLength, t),
                LowerLength = Mathf.Lerp(a.LowerLength, b.LowerLength, t),
                Hand = Mathf.Lerp(a.Hand, b.Hand, t) };
        }
        private sealed class Arm
        {
            public float Side;
            public Vector2 Socket;
            public Pose Pose;
            public Transform Root, Upper, Lower, Hand;
            public SpriteRenderer UpperSprite, LowerSprite, HandSprite;
            public SpriteRenderer LoopSprite;
            public Vector3 UpperScale, LowerScale, LoopScale;
            public Vector3 LoopPosition, LoopAnchor;
            public Quaternion LoopRotation;
            public Sprite OpenHandSprite;
            public float HandBottom;
            public float UpperLength, LowerLength, LowerBaseLength, UpperBottom, LowerBottom, DeployedLoopScale;
            public float LowerAnchorAngle;
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
            handSpacing = diameter * handSpacingOfBodyDiameter;
            armWidth = diameter * collisionWidthOfBodyDiameter;
            left = BindArm("Left Mechanical Arm", -1f);
            right = BindArm("Right Mechanical Arm", 1f);
            upperLength = Mathf.Max(left.UpperLength, right.UpperLength);
            lowerLength = Mathf.Max(left.LowerLength, right.LowerLength);
            armLength = Mathf.Max(left.UpperLength + left.LowerLength, right.UpperLength + right.LowerLength);
            targetLocal = Vector2.up * armLength * .8f;
            initialized = true;
            framePosition = transform.position; frameRotation = transform.rotation;
        }

        private void Update()
        {
            if (AnimalGame.MainUI.MainUI.BlocksGameplay) return;
            EnsureVisuals();
            if (!initialized) return;
            bool armHeld = CanOperate && (Input.GetKey(keyboardArmKey) || AdaptiveLegacyGamepadInput.IsLeftStickButtonHeld());
            bool grab = Input.GetKey(keyboardGrabKey) || AdaptiveLegacyGamepadInput.IsSouthFaceButtonHeld();
            Step(Time.deltaTime, ReadLocalInput(), armHeld, grab);
        }

        // Separate input sampling from simulation so the same transitions can be regression checked.
        private void Step(float deltaTime, Vector2 localInput, bool armHeld, bool grab)
        {
            stepDelta = Mathf.Max(0f, deltaTime);
            IsBlocked = false;
            if (!this || !isActiveAndEnabled) return;
            bool canOperate = CanOperate;
            armHeld &= canOperate;
            if ((heldObject != null && (!heldObject.Available || heldObject.Owner != this))
                || (State == RobotArmState.Recycling && heldObject == null))
            {
                bool wasRecycling = State == RobotArmState.Recycling;
                ClearHeld();
                if (wasRecycling) FinishArmAction(armHeld);
            }
            mover.SetGrabResistance(heldObject != null && !IsProcessingRecycle ? heldObject.GrabResistance : 0f);

            if (!canOperate || !Upright) Drop();
            else if (State != RobotArmState.Recycling)
            {
                // Accept A release at the visible chest position before L3 release, aim or body motion.
                // Deployed hands, rather than the current button state, permit releasing A and L3 together.
                if (!grab && HandsDeployed && IsHeldInRecycleZone()) BeginRecycle();
                else if (!armHeld) Drop();
            }
            if (!this || !isActiveAndEnabled) return;

            bool recycling = State == RobotArmState.Recycling;
            IsArmModeActive = armHeld || recycling;
            mover.SetArmInputCaptured(IsArmModeActive && !IsProcessingRecycle);
            inputLocal = armHeld && !recycling ? Vector2.ClampMagnitude(localInput, 1f) : Vector2.zero;
            CurrentInputMagnitude = inputLocal.magnitude;
            CurrentTargetLocal = inputLocal;
            if (!recycling)
            {
                if (!armHeld)
                {
                    docked = false;
                    State = deploymentTime > 0f ? RobotArmState.Retracting : RobotArmState.Retracted;
                }
                else if (!HandsDeployed) State = RobotArmState.Extending;
                else UpdateDockState();
            }

            if (armHeld && State != RobotArmState.Recycling) TurnBodyForLocalInput();
            if (State != RobotArmState.Recycling)
            {
                desiredGripOffset = null;
                UpdateTarget();
                AdvanceArms(armHeld);
            }
            FollowHeldObject();
            UpdateReady();
            if (State == RobotArmState.Recycling) UpdateRecycle(armHeld);
            else if (armHeld && Upright && HandsReady)
            {
                if (!grab && heldObject != null)
                {
                    if (IsRecycleReady) BeginRecycle(); else Drop();
                }
                else if (grab && heldObject == null) TryGrab();
            }
            // A recycle callback may disable this component; do not restore its controls or visuals afterwards.
            if (!this || !isActiveAndEnabled) return;
            // Do not overwrite PlayRecycle with a release clip on the same A-release frame.
            if (State != RobotArmState.Recycling) SetHandGrip(HandsReady && Upright && grab);
            ApplyVisuals(left); ApplyVisuals(right);
            framePosition = transform.position; frameRotation = transform.rotation;
        }

        private void LateUpdate()
        {
            if (!initialized) return;
            // RobotMover runs after this controller. Check its translation AND rotation before rendering.
            if (Upright && deploymentTime > 0f && !IsProcessingRecycle &&
                (transform.position != framePosition || Quaternion.Angle(transform.rotation, frameRotation) > .00001f))
                ConstrainBodyPose(framePosition, frameRotation, transform.position, transform.rotation);
            if (State == RobotArmState.Recycling && heldObject != null)
            {
                if (recyclePresentationActive)
                    SetHeldRootPose(marker.MarkerVisualRoot.TransformPoint(recyclePosition),
                        transform.rotation * heldRotation);
                else SetHeldRootPosition(marker.MarkerVisualRoot.TransformPoint(recyclePosition));
                if (recyclePresentationActive)
                {
                    mediumRecycleClip?.SetVisualOffset(marker.MarkerVisualRoot, garbageShakeOffset);
                    mediumRecycleClip?.Update(marker.MarkerVisualRoot, mediumRecycleClipY);
                }
            }
            FollowHeldObject();
            UpdateReady();
        }

        private Vector2 ReadLocalInput()
        {
            Vector2 keyboard = new Vector2((Input.GetKey(KeyCode.L) ? 1f : 0f) - (Input.GetKey(KeyCode.J) ? 1f : 0f),
                (Input.GetKey(KeyCode.I) ? 1f : 0f) - (Input.GetKey(KeyCode.K) ? 1f : 0f));
            keyboard = Vector2.ClampMagnitude(keyboard, 1f);
            Vector2 stick = AdaptiveLegacyGamepadInput.ReadRawLeftStick();
            return keyboard.sqrMagnitude >= stick.sqrMagnitude ? keyboard : stick;
        }
        private void UpdateDockState()
        {
            if (heldObject != null && heldObject.TryGetComponent<HeavyGarbagePull>(out _))
            {
                docked = false;
                State = RobotArmState.OuterOperating;
                return;
            }
            if (heldObject == null) docked = false;
            else if (docked)
            {
                if (CurrentInputMagnitude > dockExitMagnitude) docked = false;
            }
            else
            {
                docked = CurrentInputMagnitude <= dockEnterMagnitude;
            }
            State = docked ? RobotArmState.Docking : RobotArmState.OuterOperating;
        }
        private void TurnBodyForLocalInput()
        {
            if (!Upright || State == RobotArmState.Docking || CurrentInputMagnitude <= leftStickDeadZone) return;
            float angle = Vector2.SignedAngle(Vector2.up, inputLocal);
            if (Mathf.Abs(angle) <= followAngle) return;
            // Input stays body-local: turning does not consume the stick's angle.
            // Continue while held outside the limit, stop as soon as input returns inside it.
            float step = Mathf.Sign(angle) * mover.BaseTurnSpeed * stepDelta * mover.GrabMovementMultiplier;
            Quaternion requested = Quaternion.Euler(0f, 0f, transform.eulerAngles.z + step);
            ConstrainBodyPose(transform.position, transform.rotation, transform.position, requested);
        }
        private Vector2 DockLocal => armLength * (heldObject != null && heldObject.Size == RecyclableSize.Medium
            ? mediumDockPosition : smallDockPosition);
        private void UpdateTarget()
        {
            if (heldObject != null && heldObject.TryGetComponent(out HeavyGarbagePull pull))
            {
                if (pull.HasGripAnchor || pull.IsReturningGrip)
                    targetLocal = marker.MarkerVisualRoot.InverseTransformPoint(pull.GripAnchorWorld);
                return;
            }
            Vector2 direction = Vector2.up;
            if (CurrentInputMagnitude > leftStickDeadZone)
            {
                Vector2 local = inputLocal.normalized;
                float angle = Mathf.Clamp(Vector2.SignedAngle(Vector2.up, local), -followAngle, followAngle);
                direction = Direction(angle);
            }
            float radius = Mathf.Lerp(armLength * staticArmLengthPercent, MaximumCommonReach(),
                Mathf.InverseLerp(dockEnterMagnitude, maximumMagnitude, CurrentInputMagnitude));
            Vector2 desired = direction * radius;
            if (docked && heldObject != null)
            {
                // The whole inner circle means "bring it to the inlet", regardless of stick drift/direction.
                AlignDockGrip();
                desired = DockLocal - (desiredGripOffset ?? heldOffset) - AnchorOffset();
            }
            float blend = 1f - Mathf.Exp(-stepDelta * mover.GrabMovementMultiplier / Mathf.Max(.01f, aimSmoothingTime));
            targetLocal = Vector2.Lerp(targetLocal, desired, blend);
        }
        private void AlignDockGrip()
        {
            if (!heldObject.Recyclable) return;
            Vector2 centred = DockLocal - AnchorOffset();
            Vector2 desired = centred - heldOffset;
            if (CommonTargetReachable(desired) || !CommonTargetReachable(centred)) return;
            // An edge grip can require longer arms than exist. Slide the grip only as far as
            // necessary while docking; outer operation still preserves the original grab offset.
            float low = 0f, high = 1f;
            for (int i = 0; i < 16; i++)
            {
                float t = (low + high) * .5f;
                if (CommonTargetReachable(centred - heldOffset * t)) low = t; else high = t;
            }
            float blend = 1f - Mathf.Exp(-stepDelta * mover.GrabMovementMultiplier / Mathf.Max(.01f, aimSmoothingTime));
            desiredGripOffset = Vector2.Lerp(heldOffset, heldOffset * low, blend);
        }
        private bool CommonTargetReachable(Vector2 target)
        {
            Vector2 leftDelta = target - Vector2.right * handSpacing * .5f - left.Socket;
            Vector2 rightDelta = target + Vector2.right * handSpacing * .5f - right.Socket;
            return TargetReachable(left, leftDelta) && TargetReachable(right, rightDelta);
        }
        private bool TargetReachable(Arm arm, Vector2 delta)
        {
            float minimum = Mathf.Abs(arm.UpperLength - arm.LowerLength) + .00002f;
            float maximum = arm.UpperLength + arm.LowerLength - armLength * .005f;
            return delta.sqrMagnitude >= minimum * minimum && delta.sqrMagnitude <= maximum * maximum;
        }
        private float MaximumCommonReach()
        {
            return Mathf.Min(ForwardReach(left), ForwardReach(right));
        }
        private float ForwardReach(Arm arm)
        {
            float lateral = arm.Socket.x - arm.Side * handSpacing * .5f;
            float reach = arm.UpperLength + arm.LowerLength - armLength * .005f;
            return Mathf.Sqrt(Mathf.Max(0f, reach * reach - lateral * lateral));
        }

        private void AdvanceArms(bool extending)
        {
            float next = extending ? Mathf.Min(TotalDeploymentTime, deploymentTime + stepDelta)
                : Mathf.Max(0f, deploymentTime - stepDelta * TotalDeploymentTime / retractDuration);
            Pose lp = DesiredPose(left, next), rp = DesiredPose(right, next);
            Pose oldL = left.Pose, oldR = right.Pose;
            Vector2 oldOffset = heldOffset, nextOffset = desiredGripOffset ?? heldOffset;
            int steps = Mathf.Max(MotionSteps(oldL, lp, oldR, rp),
                Mathf.CeilToInt((PoseTravel(oldL, lp) + PoseTravel(oldR, rp) + (nextOffset - oldOffset).magnitude) / .005f));
            steps = Mathf.Clamp(steps, 1, 4096);
            float accepted = 0f;
            for (int i = 1; i <= steps; i++)
            {
                float t = accepted + 1f / steps;
                Pose nextL = Pose.Lerp(oldL, lp, t), nextR = Pose.Lerp(oldR, rp, t);
                Vector2 offset = Vector2.Lerp(oldOffset, nextOffset, t);
                BuildArmPlan(nextL, nextR, offset);
                bool legal = contactPlan.Evaluate();
                float multiplier = contactPlan.SpeedMultiplier;
                if (multiplier < 1f)
                {
                    t = accepted + multiplier / steps;
                    nextL = Pose.Lerp(oldL, lp, t); nextR = Pose.Lerp(oldR, rp, t);
                    offset = Vector2.Lerp(oldOffset, nextOffset, t);
                    BuildArmPlan(nextL, nextR, offset);
                    legal = contactPlan.Evaluate();
                }
                if (!legal || multiplier <= 0f || !contactPlan.Commit()) { IsBlocked = true; break; }
                left.Pose = nextL; right.Pose = nextR; accepted = t;
                heldOffset = offset;
                FollowHeldObject();
            }
            deploymentTime = Mathf.Lerp(deploymentTime, next, accepted);
            if (extending && HandsReady && State == RobotArmState.Extending) UpdateDockState();
            if (!extending && deploymentTime <= .00001f) State = RobotArmState.Retracted;
        }
        private Pose DesiredPose(Arm arm, float progress)
        {
            Vector2 desired = targetLocal + Vector2.right * arm.Side * handSpacing * .5f;
            float lowerGrowth = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / connectorExtendDuration));
            float upperGrowth = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((progress - connectorExtendDuration) / extendDuration));
            // Lower Arm is the body-side segment; Upper Arm carries the hand.
            // Aim the full-size chain, then grow each segment from its attachment point.
            SolveIK(desired - arm.Socket, arm.LowerLength, arm.UpperLength, arm.Side, out float lower, out float upper);
            // Recycling supplies an animated target already; extra aim smoothing would lag behind it.
            bool heavyGrip = heldObject != null && heldObject.TryGetComponent<HeavyGarbagePull>(out _);
            float delta = State == RobotArmState.Recycling ? 360f
                : heavyGrip ? maximumAimSpeedDegreesPerSecond * stepDelta
                : maximumAimSpeedDegreesPerSecond * stepDelta * mover.GrabMovementMultiplier;
            return new Pose {
                UpperAngle = Mathf.MoveTowardsAngle(arm.Pose.UpperAngle, upper, delta),
                LowerAngle = Mathf.MoveTowardsAngle(arm.Pose.LowerAngle, lower, delta),
                UpperLength = arm.UpperLength * upperGrowth,
                LowerLength = arm.LowerLength * lowerGrowth,
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
                float length = segment == 0 ? after.LowerLength : after.UpperLength;
                if (length < .00001f) continue;
                InteractionShape next = SegmentShape(arm, after, segment);
                InteractionShape? previous = (segment == 0 ? before.LowerLength : before.UpperLength) > .00001f
                    ? SegmentShape(arm, before, segment) : (InteractionShape?)null;
                if (WorldInteractionQuery.Query(next, WorldInteractionKind.Collision, map, gameObject.scene,
                    ignore: transform, previous: previous, ignoreHeld: heldObject != null ? heldObject.transform : null)) return true;
            }
            return false;
        }
        private InteractionShape SegmentShape(Arm arm, Pose pose, int segment)
        {
            Vector2 elbow = arm.Socket + Direction(pose.LowerAngle) * pose.LowerLength;
            Vector2 a = segment == 0 ? arm.Socket : elbow;
            Vector2 b = segment == 0 ? elbow : elbow + Direction(pose.UpperAngle) * pose.UpperLength;
            float growth = segment == 0 ? pose.LowerLength / arm.LowerLength : pose.UpperLength / arm.UpperLength;
            Vector2 normal = new Vector2(-(b - a).y, (b - a).x).normalized * armWidth * growth * .5f;
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
            // A held prop can extend far beyond the hands. Its radius participates in
            // the angular subdivision, independently of the arm's visual width.
            float heldRadius = 0f;
            if (FollowsHands)
            {
                HeldMotionRoot.GetComponentsInChildren(false, heldSolids);
                foreach (WorldInteraction part in heldSolids)
                {
                    if (!part.Available || (part.Kind & InteractionPushPlan.Solids) == 0) continue;
                    InteractionShape shape = part.GetShape(map);
                    Vector2 pivot = InteractionShape.ToQuery(start, map);
                    for (int j = 0; j < (shape.IsBox ? 4 : 2); j++)
                        heldRadius = Mathf.Max(heldRadius, Vector2.Distance(shape.Vertex(j), pivot) + shape.Radius);
                }
            }
            int steps = Mathf.Clamp(Mathf.CeilToInt((distance + Quaternion.Angle(rotation, endRotation)
                * Mathf.Deg2Rad * heldRadius) / .005f), 1, 8192);
            transform.SetPositionAndRotation(start, rotation);
            float accepted = 0f;
            for (int i = 1; i <= steps; i++)
            {
                float t = accepted + 1f / steps;
                Vector3 position = Vector3.Lerp(start, end, t);
                Quaternion heading = Quaternion.Slerp(rotation, endRotation, t);
                BuildBodyPlan(position, heading);
                bool legal = contactPlan.Evaluate();
                float multiplier = contactPlan.SpeedMultiplier;
                if (multiplier < 1f)
                {
                    t = accepted + multiplier / steps;
                    position = Vector3.Lerp(start, end, t); heading = Quaternion.Slerp(rotation, endRotation, t);
                    BuildBodyPlan(position, heading); legal = contactPlan.Evaluate();
                }
                if (!legal || multiplier <= 0f || !contactPlan.Commit()) { IsBlocked = true; break; }
                transform.SetPositionAndRotation(position, heading);
                FollowHeldObject(); accepted = t;
            }
            ConfirmBodyMotion();
        }

        public void ConstrainBodyRotation(Quaternion rotation) =>
            ConstrainBodyPose(transform.position, transform.rotation, transform.position, rotation);

        public void ConfirmBodyMotion()
        {
            FollowHeldObject();
            framePosition = transform.position; frameRotation = transform.rotation;
        }

        public void ReportBodyBlocked() => IsBlocked = true;

        private Matrix4x4 FrameAt(Vector3 position, Quaternion rotation) =>
            Matrix4x4.TRS(position, rotation, Vector3.one)
            * Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one).inverse
            * marker.MarkerVisualRoot.localToWorldMatrix;

        private InteractionShape SegmentAt(Arm arm, Pose pose, int segment, Matrix4x4 frame)
        {
            Vector2 elbow = arm.Socket + Direction(pose.LowerAngle) * pose.LowerLength;
            Vector2 a = segment == 0 ? arm.Socket : elbow;
            Vector2 b = segment == 0 ? elbow : elbow + Direction(pose.UpperAngle) * pose.UpperLength;
            float growth = segment == 0 ? pose.LowerLength / arm.LowerLength : pose.UpperLength / arm.UpperLength;
            Vector2 normal = new Vector2(-(b - a).y, (b - a).x).normalized * armWidth * growth * .5f;
            return new InteractionShape { IsBox = true,
                A = InteractionShape.ToQuery(frame.MultiplyPoint3x4(a - normal), map),
                B = InteractionShape.ToQuery(frame.MultiplyPoint3x4(b - normal), map),
                C = InteractionShape.ToQuery(frame.MultiplyPoint3x4(b + normal), map),
                D = InteractionShape.ToQuery(frame.MultiplyPoint3x4(a + normal), map) };
        }

        private void AddArmMotion(InteractionPushPlan plan, Arm arm, Pose after, Matrix4x4 frame)
        {
            for (int segment = 0; segment < 2; segment++)
            {
                if ((segment == 0 ? after.LowerLength : after.UpperLength) < .00001f) continue;
                plan.Add(SegmentShape(arm, arm.Pose, segment), SegmentAt(arm, after, segment, frame),
                    false, WorldInteractionKind.Collision);
            }
        }

        private Vector3 HeldPosition(Pose lp, Pose rp, Matrix4x4 frame, Vector2 offset)
        {
            Vector2 l = HandPoint(left, lp), r = HandPoint(right, rp);
            Vector2 anchor = heldHands == 3 ? (l + r) * .5f : heldHands == 1 ? l : r;
            Vector3 position = frame.MultiplyPoint3x4(anchor + offset);
            position.z = HeldMotionRoot.position.z;
            return position;
        }
        private static Vector2 HandPoint(Arm arm, Pose pose) => arm.Socket
            + Direction(pose.UpperAngle) * pose.UpperLength + Direction(pose.LowerAngle) * pose.LowerLength;

        private void AddHeldMotion(InteractionPushPlan plan, Pose lp, Pose rp, Matrix4x4 frame,
            Quaternion heading, Vector2 offset)
        {
            if (!FollowsHands) return;
            Transform root = HeldMotionRoot;
            Matrix4x4 delta = Matrix4x4.TRS(HeldPosition(lp, rp, frame, offset), heading * heldRotation, Vector3.one)
                * Matrix4x4.TRS(root.position, root.rotation, Vector3.one).inverse;
            root.GetComponentsInChildren(false, heldSolids);
            foreach (WorldInteraction part in heldSolids)
                if (part.Available && (part.Kind & InteractionPushPlan.Solids) != 0)
                    plan.Add(part.GetShape(map), part.GetShapeAfterMotion(delta, map), true);
        }

        private void BuildArmPlan(Pose lp, Pose rp, Vector2 offset)
        {
            contactPlan.Begin(map, gameObject.scene, transform, HeldMotionRoot);
            mover.AppendBodyShape(contactPlan, transform.position, transform.position, map);
            Matrix4x4 frame = marker.MarkerVisualRoot.localToWorldMatrix;
            AddArmMotion(contactPlan, left, lp, frame); AddArmMotion(contactPlan, right, rp, frame);
            AddHeldMotion(contactPlan, lp, rp, frame, transform.rotation, offset);
        }

        private void BuildBodyPlan(Vector3 position, Quaternion rotation)
        {
            contactPlan.Begin(map, gameObject.scene, transform, HeldMotionRoot);
            mover.AppendBodyShape(contactPlan, transform.position, position, map);
            AppendBodyMotion(contactPlan, position, rotation);
        }

        // Called by RobotMover BEFORE its terrain/contact bookkeeping is committed.
        public void AppendBodyMotion(InteractionPushPlan plan, Vector3 position, Quaternion rotation)
        {
            // Fully ingested waste leaves only the physical body constraining driving.
            if (!initialized || deploymentTime <= 0f || !Upright || IsProcessingRecycle) return;
            Matrix4x4 frame = FrameAt(position, rotation);
            AddArmMotion(plan, left, left.Pose, frame); AddArmMotion(plan, right, right.Pose, frame);
            AddHeldMotion(plan, left.Pose, right.Pose, frame, rotation, heldOffset);
        }
        private bool BodySegmentBlocked(Arm arm, int segment, InteractionShape previous)
        {
            if ((segment == 0 ? arm.Pose.LowerLength : arm.Pose.UpperLength) < .00001f) return false;
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
            if (!this || !isActiveAndEnabled || best == null || !best.Available || best.Owner != this)
            {
                if (best != null) best.Release(this);
                return;
            }
            heldObject = best; heldHands = mask;
            mover.SetGrabResistance(best.GrabResistance);
            heldOffset = marker.MarkerVisualRoot.InverseTransformVector(HeldMotionRoot.position - (Vector3)HeldAnchor());
            heldRotation = Quaternion.Inverse(transform.rotation) * HeldMotionRoot.rotation;
            SetHandGrip(true);
            PlayGarbageGrabFeedback(best);
        }
        private void PlayGarbageGrabFeedback(WorldInteraction item)
        {
            // The garbage marker also covers the fixed, non-recyclable large obstacle.
            // Automatic fragment handoffs use TryReplaceHeldObject and do not play another grip.
            if (!item.TryGetComponent<GarbageItem>(out _)) return;
            if (grabFeedback == null || !grabFeedback.isActiveAndEnabled || !grabFeedback.FollowsRobot(mover))
            {
                grabFeedback = null;
                Camera main = Camera.main;
                RobotCameraShake candidate = main != null ? main.GetComponent<RobotCameraShake>() : null;
                if (candidate != null && candidate.isActiveAndEnabled && candidate.FollowsRobot(mover))
                    grabFeedback = candidate;
                else
                    foreach (RobotCameraShake shake in FindObjectsByType<RobotCameraShake>(FindObjectsSortMode.None))
                        if (shake.isActiveAndEnabled && shake.FollowsRobot(mover))
                        { grabFeedback = shake; break; }
            }
            if (grabFeedback != null)
                grabFeedback.PlayGarbageGrabFeedback(item.Size, (Vector2)(item.transform.position - transform.position));
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
            if (!FollowsHands) return;
            Vector3 position = (Vector3)HeldAnchor() + marker.MarkerVisualRoot.TransformVector(heldOffset);
            position.z = HeldMotionRoot.position.z;
            HeldMotionRoot.SetPositionAndRotation(position, transform.rotation * heldRotation);
            WorldInteraction.MarkHierarchySpatialDirty(HeldMotionRoot);
        }

        public bool TryReplaceHeldObject(WorldInteraction oldItem, WorldInteraction replacement)
        {
            if (State == RobotArmState.Recycling || oldItem == null || replacement == null || heldObject != oldItem
                || oldItem.Owner != this || (replacement.RequiredHands == 2 && heldHands != 3)
                || !replacement.TryGrab(this)) return false;
            if (replacement.RequiredHands == 1 && heldHands == 3)
            {
                float leftDistance = ((Vector2)replacement.transform.position - HandWorld(left)).sqrMagnitude;
                float rightDistance = ((Vector2)replacement.transform.position - HandWorld(right)).sqrMagnitude;
                heldHands = leftDistance <= rightDistance ? 1 : 2;
            }
            heldObject = replacement;
            heldOffset = marker.MarkerVisualRoot.InverseTransformVector(
                HeldMotionRoot.position - (Vector3)HeldAnchor());
            heldRotation = Quaternion.Inverse(transform.rotation) * HeldMotionRoot.rotation;
            docked = false;
            IsRecycleReady = false;
            mover.SetGrabResistance(replacement.GrabResistance);
            oldItem.Release(this);
            return true;
        }

        public bool ReleaseHeldObject(WorldInteraction expected)
        {
            if (expected == null || heldObject != expected) return false;
            Drop();
            return true;
        }
        private bool RecyclableHeld => heldObject != null && heldObject.Available && heldObject.Owner == this
            && heldObject.Recyclable && heldObject.Size != RecyclableSize.Big
            && !heldObject.TryGetComponent<HeavyGarbagePull>(out _);
        private Vector2 RecycleZoneHalfExtents => diameter * new Vector2(
            Mathf.Max(.01f, recycleZoneHalfWidthOfBodyDiameter), Mathf.Max(.01f, recycleZoneHalfDepthOfBodyDiameter));
        private void UpdateReady()
        {
            IsRecycleReady = State != RobotArmState.Recycling && CanOperate && Upright && HandsReady
                && IsHeldInRecycleZone();
        }
        private bool IsHeldInRecycleZone()
        {
            if (!RecyclableHeld) return false;
            Vector2 position = marker.MarkerVisualRoot.InverseTransformPoint(heldObject.transform.position);
            if (position.y <= 0f) return false;
            Vector2 offset = position - DockLocal;
            Vector2 halfExtents = RecycleZoneHalfExtents;
            Vector2 normalized = new Vector2(offset.x / halfExtents.x, offset.y / halfExtents.y);
            return normalized.sqrMagnitude <= 1.00001f;
        }
        private void BeginRecycle()
        {
            State = RobotArmState.Recycling; recycleTime = 0f;
            recycleSafePosition = HeldMotionRoot.position; recycleSafeRotation = HeldMotionRoot.rotation;
            recycleStart = marker.MarkerVisualRoot.InverseTransformPoint(HeldMotionRoot.position);
            recyclePosition = recycleStart;
            recycleStartScale = HeldMotionRoot.localScale;
            recycleHandStart = (HandLocal(left) + HandLocal(right)) * .5f;
            bool medium = heldObject.Size == RecyclableSize.Medium;
            SetHandGrip(medium);
            recyclePresentationActive = true;
            mediumRecycleActive = medium;
            mediumRecycleStopsPlayed = 0;
            mediumRecycleCompletionPlayed = false;
            garbageShakeOffset = Vector2.zero;
            mediumRecycleInletY = marker.VisualBodyDiameter * .5f;
            mediumRecycleClipY = mediumRecycleInletY;
            mediumRecycleClip = new MediumRecycleInletClip(heldObject, mediumRecycleInletShader);
            if (!mediumRecycleClip.Begin(marker.MarkerVisualRoot, mediumRecycleInletY))
            {
                Drop();
                return;
            }
            float trailingExtent = Mathf.Max(diameter * .01f,
                mediumRecycleClip.MaximumLocalY(marker.MarkerVisualRoot) - recycleStart.y);
            // Leave the complete sprite/icon inside throughout the shared body vibration.
            float insideY = mediumRecycleInletY - trailingExtent - diameter * .06f;
            mediumRecycleEnd = new Vector3(0f, medium ? insideY : Mathf.Min(0f, insideY), recycleStart.z);
            CurrentMediumRecycleFrame = default;
            if (medium)
            {
                CurrentMediumRecycleFrame = MediumRecycleMotion.Sample(0f, mediumRecycleClampDuration,
                    mediumRecycleStrokeDurations, mediumRecycleFinishDuration, mediumRecycleLockRecoilFraction,
                    mediumRecycleProcessingDuration, mediumRecycleProcessingFadeDuration);
            }
            ResolveRecycleFeedback();
            left.Animation?.PlayRecycle(); right.Animation?.PlayRecycle();
            IsRecycleReady = false;
            IsArmModeActive = true;
            mover.SetArmInputCaptured(true);
            inputLocal = Vector2.zero; CurrentInputMagnitude = 0f; CurrentTargetLocal = Vector2.zero;
        }
        private void UpdateRecycle(bool armHeld)
        {
            if (heldObject == null) { ClearHeld(); FinishArmAction(armHeld); return; }
            recycleTime += stepDelta;
            bool medium = mediumRecycleActive;
            float duration = medium ? MediumRecycleMotion.Duration(mediumRecycleClampDuration,
                mediumRecycleStrokeDurations, mediumRecycleFinishDuration, mediumRecycleProcessingDuration)
                : recycleDuration + MediumRecycleMotion.ProcessingDuration(mediumRecycleFinishDuration, smallRecycleProcessingDuration);
            float t;
            if (medium)
            {
                CurrentMediumRecycleFrame = MediumRecycleMotion.Sample(recycleTime, mediumRecycleClampDuration,
                    mediumRecycleStrokeDurations, mediumRecycleFinishDuration, mediumRecycleLockRecoilFraction,
                    mediumRecycleProcessingDuration, mediumRecycleProcessingFadeDuration);
                float initialFeed = Mathf.Clamp(mediumRecycleInitialFeedFraction, 0f, .25f);
                float alignment = Mathf.SmoothStep(0f, 1f,
                    Mathf.Clamp01(recycleTime / Mathf.Max(.01f, mediumRecycleClampDuration)));
                float alignedFeed = initialFeed * alignment;
                t = Mathf.Lerp(alignedFeed, 1f, CurrentMediumRecycleFrame.Progress);
                HeldMotionRoot.localScale = recycleStartScale;
            }
            else t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(recycleTime / recycleDuration));
            Vector3 inlet = mediumRecycleEnd;
            recyclePosition = Vector3.Lerp(recycleStart, inlet, t);
            if (medium)
                recyclePosition.x = Mathf.Lerp(recycleStart.x, 0f,
                    Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(recycleTime / Mathf.Max(.01f, mediumRecycleClampDuration))));
            SetHeldRootPose(marker.MarkerVisualRoot.TransformPoint(recyclePosition),
                transform.rotation * heldRotation);

            // Preserve the original grip offset, then stop both hands at the inward limit.
            if (medium)
            {
                Vector2 handTravel = (Vector2)(recyclePosition - recycleStart);
                float minimumHandY = Mathf.Min(recycleHandStart.y, armLength * recycleHandMinimumForward);
                targetLocal = recycleHandStart + handTravel;
                targetLocal.y = Mathf.Max(minimumHandY, targetLocal.y);
                targetLocal.y += diameter * .12f * CurrentMediumRecycleFrame.HandRelease01;
                UpdateMediumRecyclePresentation();
                SetHandGrip(CurrentMediumRecycleFrame.Phase != MediumRecyclePhase.Finish
                    && CurrentMediumRecycleFrame.Phase != MediumRecyclePhase.Processing
                    && CurrentMediumRecycleFrame.Phase != MediumRecyclePhase.Complete);
            }
            else
            {
                Vector2 travel = (Vector2)(inlet - recycleStart);
                float followLimit = travel.y < 0f
                    ? Mathf.Clamp01((recycleHandStart.y - armLength * recycleHandMinimumForward) / -travel.y) : 1f;
                targetLocal = recycleHandStart + travel * Mathf.Min(t, followLimit);
                if (recycleTime >= recycleDuration)
                {
                    float processingTime = recycleTime - recycleDuration;
                    CurrentMediumRecycleFrame = MediumRecycleMotion.SampleProcessing(processingTime,
                        mediumRecycleFinishDuration, smallRecycleProcessingDuration, mediumRecycleProcessingFadeDuration);
                    targetLocal.y += diameter * .12f * CurrentMediumRecycleFrame.HandRelease01;
                    UpdateRecycleProcessingPresentation(CurrentMediumRecycleFrame, processingTime);
                    PlayRecycleCompletionFeedback();
                }
                else mediumRecycleClip?.Update(marker.MarkerVisualRoot, mediumRecycleClipY);
            }
            AdvanceArms(true);
            if (recycleTime < duration) return;
            WorldInteraction item = heldObject;
            recycleCompleting = true;
            ClearHeld();
            recycleCompleting = false;
            FinishArmAction(armHeld);
            item.Recycle(this);
        }

        private void ResolveRecycleFeedback()
        {
            if (grabFeedback != null && grabFeedback.FollowsRobot(mover)) return;
            grabFeedback = null;
            foreach (RobotCameraShake candidate in FindObjectsByType<RobotCameraShake>(FindObjectsSortMode.None))
                if (candidate.gameObject.scene == gameObject.scene && candidate.FollowsRobot(mover))
                { grabFeedback = candidate; break; }
        }

        private void UpdateMediumRecyclePresentation()
        {
            MediumRecycleFrame frame = CurrentMediumRecycleFrame;
            bool processing = frame.Phase == MediumRecyclePhase.Processing || frame.Phase == MediumRecyclePhase.Complete;
            if (processing)
                UpdateRecycleProcessingPresentation(frame,
                    recycleTime - MediumRecycleMotion.ProcessingStartTime(mediumRecycleClampDuration, mediumRecycleStrokeDurations));
            else
            {
                float shakePhase = recycleTime * mediumRecycleHandShakeFrequency * Mathf.PI * 2f;
                handShakeOffset = new Vector2(Mathf.Sin(shakePhase), Mathf.Sin(shakePhase * 1.17f + 1.1f))
                    * diameter * mediumRecycleHandShakeOfBodyDiameter * frame.Effort01;
                handShakeOffset.y += diameter * mediumRecycleHandShakeOfBodyDiameter * frame.Recoil01;
                handShakeRoll = Mathf.Sin(shakePhase * .93f + .4f) * mediumRecycleHandShakeDegrees * frame.Effort01;
                float reaction = .35f * frame.Effort01 + frame.Recoil01;
                Vector2 bodyOffset = new Vector2(.15f * Mathf.Sin(shakePhase * .5f), -1f)
                    * diameter * mediumRecycleBodyRecoilOfBodyDiameter * reaction;
                float bodyRoll = Mathf.Sin(shakePhase * .45f) * mediumRecycleBodyRollDegrees * reaction;
                marker.SetMediumRecycleVisualRecoil(bodyOffset, bodyRoll);
                float crushStrength = 0f;
                if (frame.Phase == MediumRecyclePhase.Push)
                {
                    float pushStart = MediumRecycleMotion.PushTime(mediumRecycleClampDuration, mediumRecycleStrokeDurations, frame.Stage);
                    float pushEnd = MediumRecycleMotion.StopTime(mediumRecycleClampDuration, mediumRecycleStrokeDurations, frame.Stage);
                    float push01 = Mathf.Clamp01((recycleTime - pushStart) / Mathf.Max(.001f, pushEnd - pushStart));
                    crushStrength = Mathf.Max(0f, Mathf.Sin(push01 * Mathf.PI));
                }
                else if (frame.Phase == MediumRecyclePhase.Load) crushStrength = .15f * frame.Effort01;
                else if (frame.Phase == MediumRecyclePhase.Lock) crushStrength = .65f * frame.Recoil01;
                float garbagePhase = recycleTime * mediumRecycleGarbageShakeFrequency * Mathf.PI * 2f;
                float stageStrength = .75f + .125f * Mathf.Clamp(frame.Stage, 0, 2);
                // Sideways chatter and inward kicks suggest the leading edge being crushed.
                // Keeping the longitudinal offset inward avoids shaking ingested vertices back outside.
                garbageShakeOffset = new Vector2(.75f * Mathf.Sin(garbagePhase) + .25f * Mathf.Sin(garbagePhase * 2.31f),
                    -.5f * Mathf.Abs(Mathf.Sin(garbagePhase * 1.37f + .6f)))
                    * diameter * mediumRecycleGarbageShakeOfBodyDiameter * stageStrength * crushStrength;
                mediumRecycleClip?.SetVisualOffset(marker.MarkerVisualRoot, garbageShakeOffset);
                mediumRecycleClipY = mediumRecycleInletY + bodyOffset.y;
                mediumRecycleClip?.Update(marker.MarkerVisualRoot, mediumRecycleClipY);
            }
            if (grabFeedback == null) return;
            if (!processing)
            {
                grabFeedback.SetMediumRecycleEffort(this, frame.Effort01);
                grabFeedback.SetMediumRecycleContinuous(this, frame.Stage, frame.Effort01, recycleTime, -transform.up);
            }
            for (int stage = 0; stage < 3; stage++)
            {
                int bit = 1 << stage;
                if ((mediumRecycleStopsPlayed & bit) != 0 || recycleTime + .000001f
                    < MediumRecycleMotion.StopTime(mediumRecycleClampDuration, mediumRecycleStrokeDurations, stage)) continue;
                mediumRecycleStopsPlayed |= bit;
                grabFeedback.PlayMediumRecycleImpact(this, stage, -transform.up);
            }
            if (processing || frame.Phase == MediumRecyclePhase.Finish) PlayRecycleCompletionFeedback();
        }

        private void UpdateRecycleProcessingPresentation(MediumRecycleFrame frame, float processingTime)
        {
            // The automatic claw release continues while the normal drive controls return.
            mover.SetArmInputCaptured(false);
            mover.SetGrabResistance(0f);
            handShakeOffset = Vector2.zero; handShakeRoll = 0f;
            garbageShakeOffset = Vector2.zero;
            float phase = processingTime * 10.5f * Mathf.PI * 2f;
            Vector2 bodyOffset = new Vector2(.3f * Mathf.Sin(phase * 1.19f), Mathf.Sin(phase))
                * diameter * mediumRecycleProcessingBodyShakeOfBodyDiameter * frame.ProcessingEnvelope01;
            float bodyRoll = Mathf.Sin(phase * .83f) * mediumRecycleBodyRollDegrees * .25f * frame.ProcessingEnvelope01;
            marker.SetMediumRecycleVisualRecoil(bodyOffset, bodyRoll);
            mediumRecycleClipY = mediumRecycleInletY + bodyOffset.y;
            mediumRecycleClip?.SetVisualOffset(marker.MarkerVisualRoot, Vector2.zero);
            mediumRecycleClip?.Update(marker.MarkerVisualRoot, mediumRecycleClipY);
            float feedbackMultiplier = mediumRecycleActive ? 1f : Mathf.Clamp01(smallRecycleProcessingFeedbackMultiplier);
            grabFeedback?.SetMediumRecycleProcessing(this, frame.ProcessingEnvelope01 * feedbackMultiplier,
                processingTime, -transform.up);
        }

        private void PlayRecycleCompletionFeedback()
        {
            if (!mediumRecycleCompletionPlayed && grabFeedback != null)
            {
                mediumRecycleCompletionPlayed = true;
                grabFeedback.PlayMediumRecycleImpact(this, 3, -transform.up);
            }
        }

        private void EndMediumRecyclePresentation()
        {
            mediumRecycleClip?.Dispose(); mediumRecycleClip = null;
            if (!recyclePresentationActive) return;
            marker?.ClearMediumRecycleVisualRecoil();
            handShakeOffset = Vector2.zero; handShakeRoll = 0f;
            garbageShakeOffset = Vector2.zero;
            CurrentMediumRecycleFrame = default;
            if (grabFeedback != null)
            {
                if (recycleCompleting) grabFeedback.CompleteMediumRecycleFeedback(this);
                else grabFeedback.CancelMediumRecycleFeedback(this);
            }
            mediumRecycleActive = false;
            recyclePresentationActive = false;
        }
        private void FinishArmAction(bool armHeld)
        {
            State = armHeld ? RobotArmState.OuterOperating
                : deploymentTime > 0f ? RobotArmState.Retracting : RobotArmState.Retracted;
            IsArmModeActive = armHeld;
            mover.SetArmInputCaptured(armHeld);
        }
        private void Drop()
        {
            WorldInteraction item = heldObject;
            // Cancellation restores the physical pose before removing the inlet clip.
            if (item != null && State == RobotArmState.Recycling && item.Owner == this)
                SetHeldRootPose(recycleSafePosition, recycleSafeRotation);
            // Unity's destroyed-object null must still release the presentation session.
            ClearHeld();
            if (item != null) item.Release(this);
            SetHandGrip(false);
            if (State == RobotArmState.Recycling) State = RobotArmState.OuterOperating;
        }
        private void SetHandGrip(bool closed)
        {
            if (closed == previousGrab || left == null || right == null) return;
            previousGrab = closed;
            left.HandSprite.sprite = closed && robotHandClosedSprite != null ? robotHandClosedSprite : left.OpenHandSprite;
            right.HandSprite.sprite = closed && robotHandClosedSprite != null ? robotHandClosedSprite : right.OpenHandSprite;
        }
        private void ClearHeld()
        {
            EndMediumRecyclePresentation();
            // Restore for interrupted recycling and for objects reused by a pool after completion.
            if (State == RobotArmState.Recycling && heldObject != null)
            {
                HeldMotionRoot.localScale = recycleStartScale;
                WorldInteraction.MarkHierarchySpatialDirty(HeldMotionRoot);
            }
            heldObject = null; heldHands = 0; docked = false;
            IsRecycleReady = false; mover?.SetGrabResistance(0f);
            desiredGripOffset = null;
        }
        private void SetHeldRootPose(Vector3 position, Quaternion rotation)
        {
            HeldMotionRoot.SetPositionAndRotation(position, rotation);
            WorldInteraction.MarkHierarchySpatialDirty(HeldMotionRoot);
        }
        private void SetHeldRootPosition(Vector3 position)
        {
            HeldMotionRoot.position = position;
            WorldInteraction.MarkHierarchySpatialDirty(HeldMotionRoot);
        }
        private Vector2 HandLocal(Arm arm) => arm.Socket + Direction(arm.Pose.UpperAngle) * arm.Pose.UpperLength
            + Direction(arm.Pose.LowerAngle) * arm.Pose.LowerLength;
        private Vector2 HandWorld(Arm arm) => arm == null ? (Vector2)transform.position
            : (Vector2)marker.MarkerVisualRoot.TransformPoint(HandLocal(arm));

        private Arm BindArm(string name, float side)
        {
            var arm = new Arm { Side = side, Socket = Vector2.right * side * diameter * .5f * socketRadiusOfBody };
            arm.Root = marker.MarkerVisualRoot.Find(name);
            arm.UpperSprite = BindSprite(arm.Root, "Upper Arm", null);
            arm.LowerSprite = BindSprite(arm.Root, "Lower Arm", null);
            arm.LoopSprite = BindSprite(arm.LowerSprite.transform, "Loopable", null);
            arm.LoopScale = arm.LoopSprite.transform.localScale;
            arm.LoopPosition = arm.LoopSprite.transform.localPosition;
            arm.LoopRotation = arm.LoopSprite.transform.localRotation;
            // Older prefabs may still contain the retired cuff.
            Transform legacyCuff = arm.Root.Find("Wrist Cuff");
            if (legacyCuff != null) legacyCuff.gameObject.SetActive(false);
            arm.HandSprite = BindSprite(arm.Root, "Mechanical Hand", robotHandOpenSprite);
            arm.OpenHandSprite = arm.HandSprite.sprite;
            arm.HandBottom = arm.OpenHandSprite != null ? arm.OpenHandSprite.bounds.min.y : 0f;
            arm.Upper = arm.UpperSprite.transform; arm.Lower = arm.LowerSprite.transform; arm.Hand = arm.HandSprite.transform;
            arm.UpperScale = arm.Upper.localScale * armScale;
            arm.LowerScale = arm.Lower.localScale * armScale;
            // Mirror once at the arm segments; Loopable inherits Lower Arm's reflection.
            arm.UpperScale.x = -side * Mathf.Abs(arm.UpperScale.x);
            arm.LowerScale.x = -side * Mathf.Abs(arm.LowerScale.x);
            float loopDirection = (arm.LoopRotation * Vector3.up).y * arm.LoopScale.y * arm.LowerScale.y;
            arm.LoopAnchor = new Vector3(0f, loopDirection >= 0f
                ? arm.LoopSprite.sprite.bounds.min.y : arm.LoopSprite.sprite.bounds.max.y, 0f);
            arm.Upper.localScale = arm.UpperScale;
            arm.Lower.localScale = arm.LowerScale;
            arm.UpperBottom = SpriteBottom(arm.UpperSprite);
            arm.LowerBottom = SpriteBottom(arm.LowerSprite);
            arm.UpperLength = Mathf.Max(.0001f, SpriteTop(arm.UpperSprite) - arm.UpperBottom);
            arm.LowerBaseLength = Mathf.Max(.0001f, SpriteTop(arm.LowerSprite) - arm.LowerBottom);
            arm.DeployedLoopScale = lowerArmScale;
            // IK uses the socket-to-pivot vector; the artwork retains its own longitudinal axis.
            Vector2 lowerAnchor = new Vector2(forearmAnchorOffset.x * arm.LowerScale.x,
                LowerLengthAtScale(arm, arm.DeployedLoopScale) + forearmAnchorOffset.y * arm.LowerScale.y);
            arm.LowerLength = Mathf.Max(.0001f, lowerAnchor.magnitude);
            arm.LowerAnchorAngle = Vector2.SignedAngle(Vector2.up, lowerAnchor);
            arm.Pose = default;
            arm.Animation = arm.Hand.GetComponent<RobotHandAnimation>();
            arm.Root.gameObject.SetActive(false);
            return arm;
        }
        private SpriteRenderer BindSprite(Transform parent, string name, Sprite sprite)
        {
            var renderer = parent.Find(name).GetComponent<SpriteRenderer>();
            if (sprite != null) renderer.sprite = sprite;
            renderer.color = armColor;
            if (marker.ForegroundSpriteMaterial != null) renderer.sharedMaterial = marker.ForegroundSpriteMaterial;
            return renderer;
        }
        private void ApplyVisuals(Arm arm)
        {
            arm.Root.gameObject.SetActive(deploymentTime > 0f);
            Pose visualPose = arm.Pose;
            if (mediumRecycleActive && visualPose.Hand > 0f)
            {
                Vector2 exertedHand = HandLocal(arm) + new Vector2(arm.Side * handShakeOffset.x, handShakeOffset.y);
                SolveIK(exertedHand - arm.Socket, visualPose.LowerLength, visualPose.UpperLength, arm.Side,
                    out visualPose.LowerAngle, out visualPose.UpperAngle);
            }
            Vector2 elbow = arm.Socket + Direction(visualPose.LowerAngle) * visualPose.LowerLength;
            SetSegmentVisual(arm.LowerSprite, arm.Socket, visualPose.LowerAngle - arm.LowerAnchorAngle, arm.LowerBottom,
                arm.LowerScale, visualPose.LowerLength / arm.LowerLength);
            SetSegmentVisual(arm.UpperSprite, elbow, visualPose.UpperAngle, arm.UpperBottom,
                arm.UpperScale, visualPose.UpperLength / arm.UpperLength);
            Vector3 scale = arm.LoopScale;
            scale.y *= arm.DeployedLoopScale;
            arm.LoopSprite.transform.localScale = scale;
            arm.LoopSprite.transform.localPosition = LoopPositionAtScale(arm, scale);
            arm.LoopSprite.enabled = arm.DeployedLoopScale > 0f && arm.Pose.LowerLength > 0f;
            arm.HandSprite.enabled = arm.Pose.Hand > 0f;
            arm.Hand.localRotation = Quaternion.Euler(0f, 0f, visualPose.UpperAngle
                + (mediumRecycleActive ? arm.Side * handShakeRoll : 0f));
            arm.Hand.localScale = new Vector3(-arm.Side, 1f, 1f) * handScale * arm.Pose.Hand;
            // Keep the open hand's attachment offset when swapping sprites of different sizes/pivots.
            arm.Hand.localPosition = elbow + Direction(visualPose.UpperAngle) * visualPose.UpperLength;
        }
        private static float SpriteBottom(SpriteRenderer renderer) => Mathf.Min(
            renderer.sprite.bounds.min.y * renderer.transform.localScale.y,
            renderer.sprite.bounds.max.y * renderer.transform.localScale.y);
        private static float SpriteTop(SpriteRenderer renderer) => Mathf.Max(
            renderer.sprite.bounds.min.y * renderer.transform.localScale.y,
            renderer.sprite.bounds.max.y * renderer.transform.localScale.y);
        private static Vector3 LoopPositionAtScale(Arm arm, Vector3 scale) =>
            arm.LoopPosition + arm.LoopRotation * Vector3.Scale(arm.LoopScale - scale, arm.LoopAnchor);
        private static float LoopTip(Arm arm, float multiplier)
        {
            Bounds bounds = arm.LoopSprite.sprite.bounds;
            Vector3 scale = arm.LoopScale;
            scale.y *= multiplier;
            Matrix4x4 matrix = Matrix4x4.TRS(LoopPositionAtScale(arm, scale), arm.LoopRotation, scale);
            float tip = float.NegativeInfinity;
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                    tip = Mathf.Max(tip, matrix.MultiplyPoint3x4(new Vector3(
                        x == 0 ? bounds.min.x : bounds.max.x, y == 0 ? bounds.min.y : bounds.max.y, 0f)).y
                        * arm.LowerScale.y);
            return tip;
        }
        private static float LowerLengthAtScale(Arm arm, float multiplier) =>
            multiplier <= 0f ? arm.LowerBaseLength
                : Mathf.Max(arm.LowerBaseLength, LoopTip(arm, multiplier) - arm.LowerBottom);
        private void SetSegmentVisual(SpriteRenderer renderer, Vector2 start, float angle, float bottom,
            Vector3 targetScale, float growth)
        {
            renderer.enabled = growth > 0f;
            targetScale.y *= growth;
            renderer.transform.localScale = targetScale;
            renderer.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            renderer.transform.localPosition = start - Direction(angle) * (bottom * growth);
        }
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
        private void OnDrawGizmosSelected()
        {
            if (!initialized || marker == null || marker.MarkerVisualRoot == null) return;
            Matrix4x4 previous = Gizmos.matrix;
            Color previousColor = Gizmos.color;
            Gizmos.matrix = marker.MarkerVisualRoot.localToWorldMatrix;
            Gizmos.color = IsBlocked ? Color.red : Color.cyan;
            DrawArmBoxes(left); DrawArmBoxes(right);
            Gizmos.matrix = previous; Gizmos.color = previousColor;
        }
        private void DrawArmBoxes(Arm arm)
        {
            Vector2 elbow = arm.Socket + Direction(arm.Pose.LowerAngle) * arm.Pose.LowerLength;
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
            armScale = Mathf.Max(.01f, armScale);
            handScale = Mathf.Max(.01f, handScale);
            lowerArmScale = Mathf.Max(0f, lowerArmScale);
            connectorExtendDuration = Mathf.Max(.01f, connectorExtendDuration); extendDuration = Mathf.Max(.01f, extendDuration);
            handExtendDuration = Mathf.Max(.01f, handExtendDuration); retractDuration = Mathf.Max(.01f, retractDuration);
            recycleDuration = Mathf.Max(.01f, recycleDuration);
            recycleHandMinimumForward = Mathf.Max(0f, recycleHandMinimumForward);
            mediumRecycleClampDuration = Mathf.Max(.01f, mediumRecycleClampDuration);
            mediumRecycleStrokeDurations = new Vector3(Mathf.Max(.01f, mediumRecycleStrokeDurations.x),
                Mathf.Max(.01f, mediumRecycleStrokeDurations.y), Mathf.Max(.01f, mediumRecycleStrokeDurations.z));
            mediumRecycleFinishDuration = Mathf.Max(.01f, mediumRecycleFinishDuration);
            smallRecycleProcessingDuration = Mathf.Max(mediumRecycleFinishDuration, smallRecycleProcessingDuration);
            smallRecycleProcessingFeedbackMultiplier = Mathf.Clamp01(smallRecycleProcessingFeedbackMultiplier);
            mediumRecycleProcessingDuration = Mathf.Max(mediumRecycleFinishDuration, mediumRecycleProcessingDuration);
            mediumRecycleProcessingFadeDuration = Mathf.Clamp(mediumRecycleProcessingFadeDuration, 0f, mediumRecycleProcessingDuration);
            mediumRecycleHandShakeFrequency = Mathf.Max(1f, mediumRecycleHandShakeFrequency);
            mediumRecycleGarbageShakeFrequency = Mathf.Max(1f, mediumRecycleGarbageShakeFrequency);
            recycleZoneHalfWidthOfBodyDiameter = Mathf.Max(.01f, recycleZoneHalfWidthOfBodyDiameter);
            recycleZoneHalfDepthOfBodyDiameter = Mathf.Max(.01f, recycleZoneHalfDepthOfBodyDiameter);
            dockEnterMagnitude = Mathf.Clamp(dockEnterMagnitude, 0f, .9f);
            dockExitMagnitude = Mathf.Clamp(dockExitMagnitude, dockEnterMagnitude + .001f, .99f);
            maximumMagnitude = Mathf.Clamp(maximumMagnitude, dockExitMagnitude + .001f, 1f);
            leftStickDeadZone = Mathf.Clamp(leftStickDeadZone, 0f, dockEnterMagnitude);
        }
    }
}
