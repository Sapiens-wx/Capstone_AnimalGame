using UnityEngine;

namespace AnimalGame.RobotArm
{
    public sealed partial class RobotArmController
    {
        [Header("Inventory arm presentation")]
        [SerializeField, Range(.1f, .9f)] private float inventoryHandReachOfArmLength = .25f;
        [SerializeField, Min(.01f)] private float inventoryArmPoseDuration = .25f;

        private AnimalGame.MainUI.MainUI inventoryHud;
        private bool inventoryPresentationActive, inventoryPresentationOpen;
        private float inventoryArmPoseElapsed;
        private Vector2 inventoryArmStartTarget;
        public bool IsInventoryPresentationActive => inventoryPresentationActive;

        public void BindInventory(AnimalGame.MainUI.MainUI hud)
        {
            UnbindInventory();
            inventoryHud = hud;
            hud.InventoryOpenChanged += HandleInventoryOpenChanged;
            if (hud.IsInventoryOpen) HandleInventoryOpenChanged(true);
        }

        private void UnbindInventory()
        {
            if (inventoryHud != null) inventoryHud.InventoryOpenChanged -= HandleInventoryOpenChanged;
            inventoryHud = null;
        }

        private void HandleInventoryOpenChanged(bool open)
        {
            if (!open) { inventoryPresentationOpen = false; return; }
            EnsureVisuals();
            if (!initialized || !isActiveAndEnabled || !CanOperate) return;
            inventoryPresentationActive = inventoryPresentationOpen = true;
            inventoryArmPoseElapsed = 0f;
            inventoryArmStartTarget = deploymentTime > .00001f ? NormalHandCentre
                : Vector2.up * armLength * .8f;
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
            if (inventoryPresentationOpen)
            {
                inventoryArmPoseElapsed = Mathf.Min(inventoryArmPoseDuration, inventoryArmPoseElapsed + stepDelta);
                targetLocal = Vector2.Lerp(inventoryArmStartTarget,
                    Vector2.up * armLength * inventoryHandReachOfArmLength,
                    Mathf.SmoothStep(0f, 1f, inventoryArmPoseElapsed / inventoryArmPoseDuration));
            }
            deploymentTime = inventoryPresentationOpen
                ? Mathf.Min(TotalDeploymentTime, deploymentTime + stepDelta)
                : Mathf.Max(0f, deploymentTime - stepDelta * TotalDeploymentTime / retractDuration);
            left.Pose = DesiredPose(left, deploymentTime);
            right.Pose = DesiredPose(right, deploymentTime);
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
