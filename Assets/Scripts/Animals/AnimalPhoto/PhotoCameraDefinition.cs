using System;
using UnityEngine;

namespace AnimalGame.Animals
{
    public enum PhotoCameraHandling
    {
        ZoomFocus = 0,
        FixedPrimeHold = 1
    }

    [CreateAssetMenu(fileName = "PhotoCamera", menuName = "Animal Game/Photos/Camera Model")]
    public sealed class PhotoCameraDefinition : ScriptableObject
    {
        [SerializeField, Tooltip("Stable model identity, independent of the asset name.")]
        private string cameraId;
        [SerializeField] private string displayName = "Camera";
        [SerializeField, Tooltip("Shared rendering preset. Leave empty for the Basic fallback.")]
        private PhotoRenderSet renderSet;
        [Header("Camera Handling")]
        [SerializeField] private PhotoCameraHandling handling;
        [Tooltip("Hold time before an automatic shutter when using fixed-prime handling.")]
        [SerializeField, Min(.05f)] private float automaticHoldSeconds = .3f;
        [Tooltip("Fixed frame distance between the unit's nearest and farthest photo positions.")]
        [SerializeField, Range(0f, 1f)] private float fixedAimDistance01 = .75f;
        [SerializeField, Range(.1f, 1f)] private float fixedFrameWidthOfRange = .9f;
        [SerializeField, Range(1f, 3f)] private float fixedFrameAspect = 2f;

        public string CameraId => cameraId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public PhotoRenderSet RenderSet => renderSet;
        public PhotoCameraHandling Handling => handling;
        public float AutomaticHoldSeconds => Mathf.Max(.05f, automaticHoldSeconds);
        public float FixedAimDistance01 => Mathf.Clamp01(fixedAimDistance01);
        public float FixedFrameWidthOfRange => Mathf.Clamp(fixedFrameWidthOfRange, .1f, 1f);
        public float FixedFrameAspect => Mathf.Clamp(fixedFrameAspect, 1f, 3f);

        public PhotoCaptureSettings Capture(int seed) => new PhotoCaptureSettings(
            CameraId, DisplayName, renderSet != null ? renderSet.Capture(seed) : PhotoRenderSnapshot.Basic(seed));

        /// <summary>Use when making a new model from an existing asset, not when tuning it.</summary>
        public void RegenerateIdentity() => cameraId = Guid.NewGuid().ToString("N");

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(cameraId)) RegenerateIdentity();
        }
    }

    /// <summary>Values frozen at shutter press; no references to editable camera or Set assets.</summary>
    public sealed class PhotoCaptureSettings
    {
        public string CameraId { get; }
        public string CameraName { get; }
        public PhotoRenderSnapshot RenderSnapshot { get; }

        public PhotoCaptureSettings(string cameraId, string cameraName, PhotoRenderSnapshot renderSnapshot)
        {
            CameraId = cameraId ?? string.Empty;
            CameraName = cameraName ?? string.Empty;
            RenderSnapshot = renderSnapshot ?? throw new ArgumentNullException(nameof(renderSnapshot));
        }

        public static PhotoCaptureSettings Basic(int seed = 0) =>
            new PhotoCaptureSettings("basic-fallback", "Basic Camera", PhotoRenderSnapshot.Basic(seed));
    }
}
