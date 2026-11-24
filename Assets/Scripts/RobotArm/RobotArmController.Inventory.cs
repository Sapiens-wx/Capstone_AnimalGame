using UnityEngine;

namespace AnimalGame.RobotArm
{
    public sealed partial class RobotArmController
    {
        [Header("Inventory arm presentation")]
        [SerializeField, Range(.1f, .9f)] private float inventoryHandReachOfArmLength = .18f;
        [SerializeField, Min(.01f)] private float inventoryArmPoseDuration = 1f;
        [SerializeField, Range(0f, .5f)] private float inventoryHandHorizontalRangeOfBody = .18f;
        [SerializeField, Range(0f, 90f)] private float inventoryBodyArmAngleDegrees = 15f;

        private AnimalGame.MainUI.MainUI inventoryHud;
        private bool inventoryPresentationActive, inventoryPresentationOpen;
        private float inventoryArmPoseElapsed;
        private Vector2 inventoryArmStartTarget;
        private Vector2 inventoryArmGoal;
        private System.Random inventoryPoseRandom;
        private AnimalGame.MainUI.Inventory.InventoryController inventorySelectionController;
        public bool IsInventoryPresentationActive => inventoryPresentationActive;

        public void BindInventory(AnimalGame.MainUI.MainUI hud)
        {
            UnbindInventory();
            inventoryHud = hud;
            inventorySelectionController = hud.InventoryController;
            inventorySelectionController.SelectionChanged += HandleInventorySelectionChanged;
            hud.InventoryOpenChanged += HandleInventoryOpenChanged;
            if (hud.IsInventoryOpen) HandleInventoryOpenChanged(true);
        }

        private void UnbindInventory()
        {
            if (inventoryHud != null) inventoryHud.InventoryOpenChanged -= HandleInventoryOpenChanged;
            if (inventorySelectionController != null) inventorySelectionController.SelectionChanged -= HandleInventorySelectionChanged;
            inventorySelectionController = null;
            inventoryHud = null;
        }

        private void HandleInventoryOpenChanged(bool open)
        {
            if (!open) { inventoryPresentationOpen = false; return; }
            if (StationPlugActive) return;
            EnsureVisuals();
            if (!initialized || !isActiveAndEnabled || !CanOperate) return;
            inventoryPresentationActive = inventoryPresentationOpen = true;
            inventoryArmPoseElapsed = 0f;
            inventoryArmStartTarget = HandsDeployed ? NormalHandCentre
                : Vector2.up * armLength * .8f;
            inventoryArmGoal = Vector2.up * armLength * inventoryHandReachOfArmLength;
            inventoryPoseRandom ??= new System.Random(GetInstanceID());
            // Match photo-mode takeover: safely restore and release interrupted waste, without rewards.
            if (heldObject != null && heldObject.TryGetComponent<AnimalGame.Garbage.HeavyGarbagePull>(out var pull))
                pull.CancelPullImmediately();
            Drop();
            if (!this || !isActiveAndEnabled || !CanOperate)
            {
                if (this) CancelArmControl();
                return;
            }
            armToggleActive = false;
            armRequiresFreshPress = true;
            ResetNormalArmInput();
            ResetTumbleArmLayout();
            mover.SetArmInputCaptured(false);
        }

        private void HandleInventorySelectionChanged(int index)
        {
            if (!inventoryPresentationActive || !inventoryPresentationOpen) return;
            float range = diameter * inventoryHandHorizontalRangeOfBody;
            float direction = inventoryArmGoal.x > 0f ? -1f : inventoryArmGoal.x < 0f ? 1f
                : inventoryPoseRandom.NextDouble() < .5 ? -1f : 1f;
            inventoryArmStartTarget = targetLocal;
            inventoryArmGoal.x = direction * range * Mathf.Lerp(.35f, 1f, (float)inventoryPoseRandom.NextDouble());
            if (!HandsDeployed) inventoryArmStartTarget = Vector2.up * armLength * .8f;
            inventoryArmPoseElapsed = 0f;
        }

        private Pose InventoryPose(Arm arm)
        {
            Pose pose = DesiredPose(arm, deploymentTime);
            pose.HasSocket = false;
            // Lower is the body-side thick segment; Upper is the hand-side segment.
            bool turningBodyArm = inventoryPresentationOpen
                && Mathf.Abs(Mathf.DeltaAngle(arm.Pose.LowerAngle, inventoryBodyArmAngleDegrees)) > .01f;
            pose.LowerAngle = Mathf.MoveTowardsAngle(arm.Pose.LowerAngle,
                inventoryBodyArmAngleDegrees,
                180f * stepDelta / inventoryArmPoseDuration);
            if (turningBodyArm)
            {
                // Carry the forearm with the elbow, retaining its bend until the body arm stops.
                pose.UpperAngle = arm.Pose.UpperAngle
                    + Mathf.DeltaAngle(arm.Pose.LowerAngle, pose.LowerAngle);
                return pose;
            }
            Vector2 elbow = arm.Socket + Direction(pose.LowerAngle) * arm.LowerLength;
            // Aim directly at the final hand position, rather than chasing the interpolated
            // target through the elbow and reversing the forearm partway through its turn.
            Vector2 desired = inventoryPresentationOpen ? inventoryArmGoal : targetLocal;
            float handArmAngle = Vector2.SignedAngle(Vector2.up, desired - elbow);
            pose.UpperAngle = inventoryPresentationOpen
                ? Mathf.MoveTowardsAngle(arm.Pose.UpperAngle, handArmAngle,
                    180f * stepDelta / inventoryArmPoseDuration)
                : handArmAngle;
            return pose;
        }

        // Inventory pauses gameplay. Only the empty arms' presentation advances on unscaled time;
        // do not run world-contact plans, grabbing, recycling or body motion here.
        private bool StepInventoryPresentation(float dt)
        {
            if (!inventoryPresentationActive) return false;
            if (!CanOperate) { CancelArmControl(); return false; }
            stepDelta = Mathf.Max(0f, dt);
            ResetNormalArmInput();
            inputLocal = Vector2.zero;
            CurrentTargetLocal = Vector2.zero;
            CurrentInputMagnitude = 0f;
            IsRecycleReady = IsBlocked = false;
            IsArmModeActive = inventoryPresentationOpen;
            mover.SetArmInputCaptured(false);
            // Finish the ordinary mirrored deployment before moving the right hand to the chest.
            bool readyForInventoryPose = HandsDeployed;
            bool bodyArmAligned = Mathf.Abs(Mathf.DeltaAngle(right.Pose.LowerAngle,
                inventoryBodyArmAngleDegrees)) <= .01f;
            if (inventoryPresentationOpen && readyForInventoryPose && bodyArmAligned)
            {
                inventoryArmPoseElapsed = Mathf.Min(inventoryArmPoseDuration, inventoryArmPoseElapsed + stepDelta);
                targetLocal = Vector2.Lerp(inventoryArmStartTarget,
                    inventoryArmGoal,
                    Mathf.SmoothStep(0f, 1f, inventoryArmPoseElapsed / inventoryArmPoseDuration));
            }
            else if (inventoryPresentationOpen)
                targetLocal = Vector2.up * armLength * .8f;
            deploymentTime = inventoryPresentationOpen
                ? Mathf.Min(TotalDeploymentTime, deploymentTime + stepDelta * 2f)
                : Mathf.Max(0f, deploymentTime - stepDelta * TotalDeploymentTime / retractDuration);
            left.Pose = DesiredPose(left, deploymentTime, Vector2.up * armLength * .8f);
            right.Pose = inventoryPresentationOpen && !readyForInventoryPose
                ? DesiredPose(right, deploymentTime, Vector2.up * armLength * .8f)
                : InventoryPose(right);
            State = inventoryPresentationOpen ? HandsDeployed ? RobotArmState.OuterOperating : RobotArmState.Extending
                : deploymentTime > 0f ? RobotArmState.Retracting : RobotArmState.Retracted;
            SetHandGrip(false);
            ApplyVisuals(left); ApplyVisuals(right);
            framePosition = transform.position; frameRotation = transform.rotation;
            if (!inventoryPresentationOpen && deploymentTime == 0f && marker.AreBodyHatchesClosed)
                inventoryPresentationActive = false;
            return true;
        }

        private void OnDestroy() => UnbindInventory();
    }
}
