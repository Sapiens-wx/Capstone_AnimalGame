using UnityEngine;

namespace AnimalGame.RobotMap
{
    public sealed partial class RobotMarkerView
    {
        [Header("Scan inner ring")]
        [SerializeField, Range(.1f, 1f)] private float innerRingScanHoldScale = .55f;
        [SerializeField, Range(1f, 1.6f)] private float innerRingScanReleaseScale = 1.3f;
        [SerializeField, Min(.01f)] private float innerRingScanExpandDuration = .08f;
        [SerializeField, Min(.01f)] private float innerRingScanRecoverDuration = .2f;

        private ScanChargeUI innerRingScanInput;
        private float innerRingScanScale = 1f;
        private float innerRingScanPulseElapsed = -1f;
        private float innerRingScanPulseStartScale = 1f;

        public void BindScanInnerRing(ScanChargeUI input)
        {
            UnbindScanInnerRing();
            innerRingScanInput = input;
            if (input != null)
            {
                input.ScanHoldReleased += PulseScanInnerRing;
                input.TerrainScanRequested += PulseScanInnerRing;
            }
            ResetScanInnerRing();
        }

        private void UnbindScanInnerRing()
        {
            if (innerRingScanInput == null) return;
            innerRingScanInput.ScanHoldReleased -= PulseScanInnerRing;
            innerRingScanInput.TerrainScanRequested -= PulseScanInnerRing;
            innerRingScanInput = null;
        }

        private bool CanShowScanInnerRing => isActiveAndEnabled
            && innerRingScanInput != null && innerRingScanInput.isActiveAndEnabled
            && !AnimalGame.MainUI.MainUI.BlocksGameplay
            && mover.MovementMode == RobotMovementMode.Driven
            && (tumble == null || tumble.State == RobotTumbleState.Upright)
            && (photoMode == null || !photoMode.IsActive)
            && (armController == null || (!armController.IsArmModeActive
                && armController.VisibleDeployment01 <= .001f));

        private void ResetScanInnerRing()
        {
            innerRingScanScale = 1f;
            innerRingScanPulseElapsed = -1f;
        }

        private void PulseScanInnerRing()
        {
            if (!CanShowScanInnerRing) { ResetScanInnerRing(); return; }
            innerRingScanPulseStartScale = innerRingScanScale;
            innerRingScanPulseElapsed = 0f;
        }

        private float StepScanInnerRing(float dt)
        {
            if (!CanShowScanInnerRing) { ResetScanInnerRing(); return 1f; }
            if (innerRingScanInput.IsGestureHeld)
            {
                innerRingScanPulseElapsed = -1f;
                float target = Mathf.Lerp(.8f, innerRingScanHoldScale,
                    Mathf.SmoothStep(0f, 1f, innerRingScanInput.GestureHold01));
                innerRingScanScale = Mathf.Lerp(innerRingScanScale, target, 1f - Mathf.Exp(-18f * dt));
            }
            else if (innerRingScanPulseElapsed >= 0f)
            {
                innerRingScanPulseElapsed += dt;
                if (innerRingScanPulseElapsed < innerRingScanExpandDuration)
                    innerRingScanScale = Mathf.Lerp(innerRingScanPulseStartScale, innerRingScanReleaseScale,
                        Mathf.SmoothStep(0f, 1f, innerRingScanPulseElapsed / innerRingScanExpandDuration));
                else
                {
                    float recovery = (innerRingScanPulseElapsed - innerRingScanExpandDuration)
                        / innerRingScanRecoverDuration;
                    innerRingScanScale = Mathf.Lerp(innerRingScanReleaseScale, 1f,
                        Mathf.SmoothStep(0f, 1f, recovery));
                    if (recovery >= 1f) innerRingScanPulseElapsed = -1f;
                }
            }
            else
            {
                float target = innerRingScanInput.IsTapPending ? Mathf.Min(innerRingScanScale, .8f) : 1f;
                innerRingScanScale = Mathf.Lerp(innerRingScanScale, target, 1f - Mathf.Exp(-18f * dt));
            }
            return innerRingScanScale;
        }
    }
}
