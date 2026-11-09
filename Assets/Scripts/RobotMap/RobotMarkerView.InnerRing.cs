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

        private Transform innerRing;
        private float innerRingBaseScale, innerRingAngle, innerRingIdleTime;
        private float innerRingJamWait, innerRingJamRemaining;
        private bool innerRingWasFallen;
        private System.Random innerRingRandom;
        private Material innerRingMaterial;
        private float innerRingMotionBlend;
        private float innerRingTumbleTime;
        private static readonly int InnerRingArcDegreesId = Shader.PropertyToID("_ArcDegrees");

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
            innerRingRandom = new System.Random(GetInstanceID());
        }

        private void UpdateInnerRing()
        {
            float dt = Time.deltaTime;
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
            innerRing.localRotation = Quaternion.Euler(0f, 0f, innerRingAngle + jamOffset);
            float fullSpeed = speed < 0f ? mover.UnloadedReverseSpeed : mover.DefaultMoveSpeed;
            float motion = tumbling ? 1f : fallen ? 0f
                : Mathf.Clamp01(Mathf.Abs(speed) / Mathf.Max(.01f, fullSpeed));
            innerRingMotionBlend = Mathf.Lerp(innerRingMotionBlend, motion, 1f - Mathf.Exp(-8f * dt));
            innerRingMaterial.SetFloat(InnerRingArcDegreesId,
                tumbling ? innerRingTumbleArcDegrees
                    : Mathf.Lerp(innerRingIdleArcDegrees, innerRingMovingArcDegrees, innerRingMotionBlend));
            float size = tumbling ? 1f
                : Mathf.Lerp(innerRingIdleScale, innerRingMovingScale, innerRingMotionBlend);
            innerRing.localScale = Vector3.Lerp(innerRing.localScale,
                Vector3.one * (innerRingBaseScale * size * breath),
                1f - Mathf.Exp(-(tumbling ? 30f : 12f) * dt));
        }
    }
}
