using AnimalGame.RobotArm;
using UnityEngine;

namespace AnimalGame.RobotMap
{
    public sealed partial class RobotMarkerView
    {
        [Header("Three-segment inner ring")]
        [SerializeField] private Sprite innerRingSprite;
        [SerializeField] private Shader innerRingShader;
        [SerializeField, Min(1f)] private float innerRingVisibleDiameterPixels = 346f;
        [SerializeField, Min(0f)] private float innerRingDegreesPerMeter = 180f;
        [SerializeField, Range(0f, .1f)] private float innerRingBreathAmplitude = .06f;
        [SerializeField, Min(.1f)] private float innerRingBreathPeriod = 2.4f;
        [SerializeField, Range(0f, 15f)] private float innerRingJamAmplitude = 12f;
        [SerializeField, Range(.1f, .5f)] private float innerRingJamDuration = .4f;
        [SerializeField, Range(.5f, 1f)] private float innerRingIdleScale = .68f;
        [SerializeField, Range(.5f, 1f)] private float innerRingMovingScale = .82f;
        [SerializeField, Range(10f, 110f)] private float innerRingIdleArcDegrees = 50f;
        [SerializeField, Range(10f, 110f)] private float innerRingMovingArcDegrees = 90f;
        [SerializeField, Range(10f, 110f)] private float innerRingTumbleArcDegrees = 100f;
        [SerializeField, Min(0f)] private float innerRingTumbleSpinSpeed = 900f;
        [SerializeField, Range(0f, .2f)] private float innerRingTumbleScaleAmplitude = .12f;
        [SerializeField, Min(.1f)] private float innerRingTumbleScalePeriod = .28f;

        [Header("Arm-control brackets")]
        [SerializeField, Range(.5f, 1f)] private float innerRingArmScale = .62f;
        [SerializeField, Range(0f, .5f)] private float innerRingArmOffsetOfRadius = .35f;
        [SerializeField, Min(1f)] private float innerRingArmResponse = 14f;
        [SerializeField, Min(.1f)] private float innerRingRecyclePeriod = 1f;
        [SerializeField, Range(0f, .5f)] private float innerRingRecycleShakeOfRadius = .22f;
        [SerializeField, Range(0f, 30f)] private float innerRingRecycleShakeDegrees = 12f;

        [Header("Photo inner ring")]
        [SerializeField, Range(1f, 1.5f)] private float innerRingPhotoChargeScale = 1.2f;
        [SerializeField, Range(.1f, 1f)] private float innerRingPhotoShutterScale = .6f;
        [SerializeField, Min(.01f)] private float innerRingPhotoShrinkDuration = .07f;
        [SerializeField, Min(.01f)] private float innerRingPhotoRecoverDuration = .18f;

        private Transform innerRing;
        private float innerRingBaseScale, innerRingAngle, innerRingIdleTime;
        private float innerRingDisplayedBaseScale;
        private float innerRingJamWait, innerRingJamRemaining;
        private bool innerRingWasFallen;
        private System.Random innerRingRandom;
        private Material innerRingMaterial;
        private float innerRingMotionBlend;
        private float innerRingTumbleTime;
        private Vector2 innerRingArmOffset;
        private float innerRingGrip;
        private float innerRingRecycleTime;
        private float innerRingPhotoShutterElapsed = -1f;
        private float innerRingPhotoShutterStartScale;
        private bool innerRingPhotoShotPlayed;
        private static readonly int InnerRingArcDegreesId = Shader.PropertyToID("_ArcDegrees");
        private static readonly int InnerRingArmBlendId = Shader.PropertyToID("_ArmBlend");
        private static readonly int InnerRingBracketOffsetId = Shader.PropertyToID("_BracketOffset");
        private static readonly int InnerRingBracketGripId = Shader.PropertyToID("_BracketGrip");

        private void BindInnerRing()
        {
            var layer = new GameObject("Body Inner Ring", typeof(SpriteRenderer));
            innerRing = layer.transform;
            innerRing.SetParent(bodyVisualRoot, false);
            innerRing.localPosition = Vector3.forward * ArtworkDepth;
            var renderer = layer.GetComponent<SpriteRenderer>();
            renderer.sprite = innerRingSprite;
            renderer.color = bodyOutlineColor;
            renderer.sortingLayerID = bodyArtwork.sortingLayerID;
            renderer.sortingOrder = 1;
            innerRingMaterial = new Material(innerRingShader)
            {
                name = "Runtime Robot Inner Ring",
                hideFlags = HideFlags.DontSave,
                renderQueue = bodyArtwork.sharedMaterial.renderQueue
            };
            innerRingMaterial.SetFloat(InnerRingArcDegreesId, innerRingIdleArcDegrees);
            renderer.sharedMaterial = innerRingMaterial;
            innerRingBaseScale = CalculateArtworkScale(innerRingSprite,
                innerRingVisibleDiameterPixels,
                VisualBodyDiameter * innerRingVisibleDiameterPixels / bodyArtworkVisibleDiameterPixels);
            innerRing.localScale = Vector3.one * (innerRingBaseScale * innerRingIdleScale);
            innerRingDisplayedBaseScale = innerRing.localScale.x;
            innerRingRandom = new System.Random(GetInstanceID());
            if (photoMode != null) photoMode.PhotoCaptured += HandleInnerRingPhotoCaptured;
        }

        private void UpdateInnerRing() => StepInnerRing(Time.deltaTime);

        private void StepInnerRing(float dt)
        {
            float photoBlend = photoMode == null ? 0f : Mathf.SmoothStep(0f, 1f, photoMode.Reveal01);
            float armBlend = armController == null ? 0f
                : Mathf.SmoothStep(0f, 1f, armController.VisibleDeployment01) * (1f - photoBlend);
            float response = 1f - Mathf.Exp(-innerRingArmResponse * dt);
            Vector2 control = armController == null ? Vector2.zero : armController.VisualControlInput;
            innerRingArmOffset = Vector2.Lerp(innerRingArmOffset,
                Vector2.ClampMagnitude(control, 1f) * (156f * innerRingArmOffsetOfRadius), response);
            bool recycling = armController != null && armController.State == RobotArmState.Recycling;
            innerRingRecycleTime = recycling
                ? Mathf.Repeat(innerRingRecycleTime + dt, innerRingRecyclePeriod) : 0f;
            float gripTarget = recycling
                ? .5f + .5f * Mathf.Cos(innerRingRecycleTime * Mathf.PI * 2f / innerRingRecyclePeriod)
                : armController != null && armController.IsGripClosed ? 1f : 0f;
            innerRingGrip = Mathf.Lerp(innerRingGrip, gripTarget, response);
            Vector2 recycleShake = Vector2.zero;
            float recycleTwist = 0f;
            if (recycling)
            {
                // Keep jolts rapid independently of the chew period; compression controls their strength.
                float phase = innerRingRecycleTime * Mathf.PI * 2f;
                float strength = .2f + .8f * innerRingGrip;
                recycleShake = new Vector2(Mathf.Sin(phase * 18f), Mathf.Sin(phase * 26f))
                    * (156f * innerRingRecycleShakeOfRadius * strength);
                recycleTwist = Mathf.Sin(phase * 22f) * innerRingRecycleShakeDegrees * strength;
            }
            Vector2 bracketOffset = innerRingArmOffset + recycleShake;
            innerRingMaterial.SetFloat(InnerRingArmBlendId, armBlend);
            innerRingMaterial.SetVector(InnerRingBracketOffsetId,
                new Vector4(bracketOffset.x, bracketOffset.y, 0f, 0f));
            innerRingMaterial.SetFloat(InnerRingBracketGripId, innerRingGrip);
            bool fallen = tumble != null && tumble.State != RobotTumbleState.Upright;
            bool tumbling = tumble != null && tumble.State == RobotTumbleState.Tumbling;
            float speed = mover.IsMovementLocked ? 0f : mover.CurrentSpeed;
            float breath = 1f;
            float jamOffset = 0f;
            if (!tumbling) innerRingTumbleTime = 0f;
            if (tumbling)
            {
                innerRingTumbleTime += dt;
                innerRingAngle -= innerRingTumbleSpinSpeed * dt;
                breath += innerRingTumbleScaleAmplitude
                    * Mathf.Sin(innerRingTumbleTime * Mathf.PI * 2f / innerRingTumbleScalePeriod);
                innerRingIdleTime = 0f;
            }
            else if (fallen)
            {
                if (!innerRingWasFallen)
                {
                    innerRingJamWait = .35f;
                    innerRingJamRemaining = 0f;
                }
                // Consume frame time across both phases so brief bursts remain stable at low FPS.
                float remaining = dt;
                while (remaining > 0f)
                {
                    if (innerRingJamWait > 0f)
                    {
                        float step = Mathf.Min(remaining, innerRingJamWait);
                        innerRingJamWait -= step;
                        remaining -= step;
                        if (innerRingJamWait > 0f) break;
                        innerRingJamRemaining = innerRingJamDuration;
                    }
                    float burstStep = Mathf.Min(remaining, innerRingJamRemaining);
                    innerRingJamRemaining -= burstStep;
                    remaining -= burstStep;
                    if (innerRingJamRemaining <= 0f)
                        innerRingJamWait = .4f + (float)innerRingRandom.NextDouble() * .8f;
                }
                if (innerRingJamRemaining > 0f)
                {
                    float progress = 1f - innerRingJamRemaining / innerRingJamDuration;
                    // Two small left/right oscillations, returning to the original angle.
                    jamOffset = Mathf.Sin(progress * Mathf.PI * 4f) * innerRingJamAmplitude;
                }
                innerRingIdleTime = 0f;
            }
            else if (Mathf.Abs(speed) > .01f)
            {
                // Negative Z rotation is clockwise when viewed by the game's camera.
                innerRingAngle -= speed * innerRingDegreesPerMeter * dt;
                innerRingIdleTime = 0f;
            }
            else
            {
                innerRingIdleTime += dt;
                breath += innerRingBreathAmplitude
                    * Mathf.Sin(innerRingIdleTime * Mathf.PI * 2f / innerRingBreathPeriod);
            }
            innerRingWasFallen = fallen && !tumbling;
            innerRingAngle = Mathf.Repeat(innerRingAngle, 360f);
            innerRing.localRotation = Quaternion.Slerp(
                Quaternion.Euler(0f, 0f, innerRingAngle + jamOffset), Quaternion.identity,
                Mathf.Max(armBlend, photoBlend))
                * Quaternion.Euler(0f, 0f, recycleTwist * armBlend);
            float fullSpeed = speed < 0f ? mover.UnloadedReverseSpeed : mover.DefaultMoveSpeed;
            float motion = tumbling ? 1f : fallen ? 0f
                : Mathf.Clamp01(Mathf.Abs(speed) / Mathf.Max(.01f, fullSpeed));
            innerRingMotionBlend = Mathf.Lerp(innerRingMotionBlend, motion, 1f - Mathf.Exp(-8f * dt));
            float arcDegrees = tumbling ? innerRingTumbleArcDegrees
                : Mathf.Lerp(innerRingIdleArcDegrees, innerRingMovingArcDegrees, innerRingMotionBlend);
            innerRingMaterial.SetFloat(InnerRingArcDegreesId, Mathf.Lerp(arcDegrees, 120f, photoBlend));
            float size = tumbling ? 1f
                : Mathf.Lerp(innerRingIdleScale, innerRingMovingScale, innerRingMotionBlend);
            float displayedSize = Mathf.Lerp(size * breath, innerRingArmScale, armBlend);
            innerRingDisplayedBaseScale = Mathf.Lerp(innerRingDisplayedBaseScale,
                innerRingBaseScale * displayedSize,
                1f - Mathf.Exp(-(tumbling ? 30f : 12f) * dt));
            float photoSize = StepPhotoInnerRing(dt);
            float scanSize = StepScanInnerRing(dt);
            innerRing.localScale = Vector3.one * Mathf.Lerp(innerRingDisplayedBaseScale * scanSize,
                innerRingBaseScale * photoSize, photoBlend);
        }

        private void HandleInnerRingPhotoCaptured()
        {
            if (!isActiveAndEnabled || !photoMode.IsActive || photoMode.IsExiting) return;
            innerRingPhotoShutterStartScale = innerRing.localScale.x / innerRingBaseScale;
            innerRingPhotoShutterElapsed = 0f;
            innerRingPhotoShotPlayed = true;
        }

        private void ResetPhotoInnerRing()
        {
            innerRingPhotoShutterElapsed = -1f;
            innerRingPhotoShotPlayed = false;
        }

        private float StepPhotoInnerRing(float dt)
        {
            float normalSize = innerRingMovingScale;
            if (photoMode == null || !photoMode.IsActive || photoMode.IsExiting)
            {
                ResetPhotoInnerRing();
                return normalSize;
            }
            if (photoMode.IsFocusing) ResetPhotoInnerRing();
            if (innerRingPhotoShutterElapsed >= 0f)
            {
                innerRingPhotoShutterElapsed += dt;
                float smallSize = normalSize * innerRingPhotoShutterScale;
                if (innerRingPhotoShutterElapsed < innerRingPhotoShrinkDuration)
                    return Mathf.Lerp(innerRingPhotoShutterStartScale, smallSize,
                        Mathf.SmoothStep(0f, 1f, innerRingPhotoShutterElapsed / innerRingPhotoShrinkDuration));
                float recovery = (innerRingPhotoShutterElapsed - innerRingPhotoShrinkDuration)
                    / innerRingPhotoRecoverDuration;
                if (recovery >= 1f) innerRingPhotoShutterElapsed = -1f;
                return Mathf.Lerp(smallSize, normalSize, Mathf.SmoothStep(0f, 1f, recovery));
            }
            bool charging = !innerRingPhotoShotPlayed
                && (photoMode.IsFocusing || photoMode.IsFocused || photoMode.IsCapturing);
            return normalSize * (charging
                ? Mathf.Lerp(1f, innerRingPhotoChargeScale, Mathf.SmoothStep(0f, 1f, photoMode.FocusProgress01))
                : 1f);
        }
    }
}
