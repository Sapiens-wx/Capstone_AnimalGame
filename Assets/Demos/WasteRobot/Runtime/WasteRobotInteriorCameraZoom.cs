using AnimalGame.RobotMap;
using UnityEngine;

namespace AnimalGame.Demos.WasteRobot
{
    /// <summary>
    /// An opt-in, single-owner zoom layer on one camera. Update removes only this
    /// layer's previous output; LateUpdate runs after RobotCameraShake (250) and
    /// before scan projection (300+) to multiply the freshly composed camera size.
    /// No shared camera, scan, focus, follow or input settings are changed.
    /// </summary>
    [DefaultExecutionOrder(260)]
    [DisallowMultipleComponent]
    public sealed class WasteRobotInteriorCameraZoom : MonoBehaviour
    {
        private const float MinimumSize = .01f;
        private const float DefaultBlendDuration = .3f;
        private const float MultiplierEpsilon = .00001f;

        private Camera ownedCamera;
        private RobotCameraShake cameraShake;
        private Object currentOwner;
        private bool acquired;
        private bool releaseWhenSettled;
        private float fallbackBaseSize = 1f;
        private float baseSize = 1f;
        private float requestedInteriorSize = 6.5f;
        private float currentMultiplier = 1f;
        private float targetMultiplier = 1f;
        private float blendStartMultiplier = 1f;
        private float blendDuration;
        private float blendElapsed;
        private float lastRequestedDuration = DefaultBlendDuration;

        private Camera writtenCamera;
        private float sizeBeforeWrite;
        private float sizeAfterWrite;
        private bool hasOwnWrite;

        public bool DesiredInterior { get; private set; }
        public bool IsBlending => Mathf.Abs(currentMultiplier - targetMultiplier) > MultiplierEpsilon;
        public float CurrentMultiplier => currentMultiplier;
        public float BaseSize => Mathf.Max(MinimumSize, baseSize);
        public Object Owner => currentOwner;

        /// <summary>Called explicitly by the local prefab effect, never by a global initializer.</summary>
        public static WasteRobotInteriorCameraZoom GetOrCreate(Camera camera)
        {
            if (camera == null) return null;
            WasteRobotInteriorCameraZoom adapter = camera.GetComponent<WasteRobotInteriorCameraZoom>();
            return adapter != null ? adapter : camera.gameObject.AddComponent<WasteRobotInteriorCameraZoom>();
        }

        /// <summary>
        /// Acquires this camera exclusively. Another live owner cannot stack or replace
        /// the layer. Reacquiring with the same owner is idempotent and does not rebase.
        /// </summary>
        public bool Acquire(Object owner, Camera camera)
        {
            if (!IsUsableOwner(owner) || camera == null || camera.gameObject != gameObject
                || !camera.isActiveAndEnabled || !camera.orthographic)
                return false;

            if (acquired && !IsUsableOwner(currentOwner)) ResetOwnership();
            if (acquired)
                return currentOwner == owner && ownedCamera == camera;

            RestoreOwnWrite();
            ownedCamera = camera;
            cameraShake = camera.GetComponent<RobotCameraShake>();
            fallbackBaseSize = SafeSize(camera.orthographicSize);
            baseSize = fallbackBaseSize;
            RefreshBaseSize();
            currentOwner = owner;
            acquired = true;
            releaseWhenSettled = false;
            DesiredInterior = false;
            currentMultiplier = targetMultiplier = blendStartMultiplier = 1f;
            blendElapsed = blendDuration = 0f;
            lastRequestedDuration = DefaultBlendDuration;
            enabled = true;
            return true;
        }

        public void SetInterior(Object owner, bool inside, float interiorSize = 6.5f,
            float duration = DefaultBlendDuration)
        {
            if (!IsCurrentOwner(owner)) return;
            if (!CanContinue())
            {
                ResetOwnership();
                return;
            }

            RefreshBaseSize();
            requestedInteriorSize = SafeSize(interiorSize);
            float nextTarget = inside ? requestedInteriorSize / BaseSize : 1f;
            float safeDuration = SafeDuration(duration);
            lastRequestedDuration = safeDuration;
            releaseWhenSettled = false;
            bool sameGoal = DesiredInterior == inside
                && Mathf.Abs(nextTarget - targetMultiplier) <= MultiplierEpsilon;
            DesiredInterior = inside;
            if (!sameGoal || (safeDuration <= 0f && IsBlending))
                BeginBlend(nextTarget, safeDuration);
        }

        /// <summary>
        /// Immediate release removes this layer now. A non-immediate release retains
        /// ownership until its multiplier returns to one, then stops touching the camera.
        /// </summary>
        public void Release(Object owner, bool immediate = true)
        {
            if (!IsCurrentOwner(owner)) return;
            if (immediate || !CanContinue())
            {
                ResetOwnership();
                return;
            }

            DesiredInterior = false;
            releaseWhenSettled = true;
            BeginBlend(1f, lastRequestedDuration > 0f ? lastRequestedDuration : DefaultBlendDuration);
            if (!IsBlending) ResetOwnership();
        }

        private void Update()
        {
            // This also runs while timeScale is zero. Removing the old layer before
            // reading/writing again prevents repeated multiplication on paused frames.
            RestoreOwnWrite();
            if (!acquired) return;
            if (!CanContinue())
            {
                ResetOwnership();
                return;
            }

            RefreshBaseSize();
            float nextTarget = DesiredInterior ? requestedInteriorSize / BaseSize : 1f;
            if (Mathf.Abs(nextTarget - targetMultiplier) > MultiplierEpsilon)
                BeginBlend(nextTarget, lastRequestedDuration);

            if (IsBlending)
            {
                blendElapsed += Mathf.Max(0f, Time.unscaledDeltaTime);
                float progress = blendDuration <= 0f ? 1f : Mathf.Clamp01(blendElapsed / blendDuration);
                currentMultiplier = Mathf.Lerp(blendStartMultiplier, targetMultiplier,
                    Mathf.SmoothStep(0f, 1f, progress));
                if (progress >= 1f) currentMultiplier = targetMultiplier;
            }
            if (releaseWhenSettled && !IsBlending) ResetOwnership();
        }

        private void LateUpdate()
        {
            if (!acquired) return;
            if (!CanContinue())
            {
                ResetOwnership();
                return;
            }

            // Outside at multiplier one, do not even assign an unchanged size.
            if (!IsBlending && Mathf.Abs(currentMultiplier - 1f) <= MultiplierEpsilon)
                return;

            // Defensive against a second manual tick without an intervening Update.
            RestoreOwnWrite();
            float unmodifiedSize = ownedCamera.orthographicSize;
            if (!IsFinite(unmodifiedSize) || unmodifiedSize <= 0f) return;
            float modifiedSize = Mathf.Max(MinimumSize, unmodifiedSize * currentMultiplier);
            if (!IsFinite(modifiedSize)) return;

            writtenCamera = ownedCamera;
            sizeBeforeWrite = unmodifiedSize;
            sizeAfterWrite = modifiedSize;
            ownedCamera.orthographicSize = modifiedSize;
            hasOwnWrite = true;
        }

        private void BeginBlend(float nextTarget, float duration)
        {
            targetMultiplier = Mathf.Max(MultiplierEpsilon, nextTarget);
            blendStartMultiplier = currentMultiplier;
            blendDuration = SafeDuration(duration);
            blendElapsed = 0f;
            if (blendDuration <= 0f || !IsBlending) currentMultiplier = targetMultiplier;
        }

        private void RefreshBaseSize()
        {
            if (ownedCamera == null) return;
            if (cameraShake == null) cameraShake = ownedCamera.GetComponent<RobotCameraShake>();
            // The shake component owns the normal base size even when its effect
            // is temporarily disabled; its scan and focus channels remain untouched.
            float shakeBase = cameraShake != null ? cameraShake.BaseOrthographicSize : 0f;
            baseSize = IsFinite(shakeBase) && shakeBase > MinimumSize
                ? shakeBase : fallbackBaseSize;
        }

        private void RestoreOwnWrite()
        {
            if (!hasOwnWrite) return;
            // A camera system may have replaced our output since the last tick.
            // Never overwrite that newer value with an old captured size.
            if (writtenCamera != null && Mathf.Approximately(writtenCamera.orthographicSize, sizeAfterWrite))
                writtenCamera.orthographicSize = sizeBeforeWrite;
            hasOwnWrite = false;
            writtenCamera = null;
        }

        private bool IsCurrentOwner(Object owner) => acquired && owner != null && currentOwner == owner;

        private bool CanContinue() => IsUsableOwner(currentOwner) && ownedCamera != null
            && ownedCamera.isActiveAndEnabled && ownedCamera.orthographic;

        private static bool IsUsableOwner(Object owner)
        {
            if (owner == null) return false;
            if (owner is Behaviour behaviour) return behaviour.isActiveAndEnabled;
            if (owner is Component component) return component.gameObject.activeInHierarchy;
            if (owner is GameObject ownerObject) return ownerObject.activeInHierarchy;
            return true;
        }

        private void ResetOwnership()
        {
            RestoreOwnWrite();
            currentOwner = null;
            acquired = false;
            releaseWhenSettled = false;
            DesiredInterior = false;
            currentMultiplier = targetMultiplier = blendStartMultiplier = 1f;
            blendElapsed = blendDuration = 0f;
            ownedCamera = null;
            cameraShake = null;
        }

        private static float SafeSize(float size) => IsFinite(size) ? Mathf.Max(MinimumSize, size) : 6.5f;
        private static float SafeDuration(float duration) => IsFinite(duration) ? Mathf.Max(0f, duration) : 0f;
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private void OnDisable() => ResetOwnership();
        private void OnDestroy() => ResetOwnership();
    }
}
