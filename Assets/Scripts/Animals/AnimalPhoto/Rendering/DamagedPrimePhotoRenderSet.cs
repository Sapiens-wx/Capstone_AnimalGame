using UnityEngine;

namespace AnimalGame.Animals
{
    /// <summary>Optical damage and monochrome sensor noise, independent of the other camera looks.</summary>
    [CreateAssetMenu(fileName = "DamagedPrimePhotoRenderSet", menuName = "Animal Game/Photos/Render Sets/Damaged Prime")]
    public sealed class DamagedPrimePhotoRenderSet : PhotoRenderSet
    {
        [SerializeField] private DamagedPrimeSettings settings = DamagedPrimeSettings.Default;
        public DamagedPrimeSettings Settings => settings.Sanitized();

        public override PhotoRenderSnapshot Capture(int seed) =>
            new DamagedPrimePhotoRenderSnapshot(SetId, Version, seed, Settings);
    }

    [System.Serializable]
    public struct DamagedPrimeSettings
    {
        [Tooltip("Defocus radius in pixels at a 1024-pixel photo long edge. Grain is applied after defocus.")]
        [Range(0f, 32f)] public float blurRadius;
        [Tooltip("Monochrome sensor noise amplitude. The shutter seed fixes the pattern for this photograph.")]
        [Range(0f, .35f)] public float grainAmount;
        [Tooltip("Grain cell size in the same 1024-pixel photo coordinates.")]
        [Range(.5f, 4f)] public float grainSize;
        [Range(-3f, 2f)] public float exposureEV;
        [Range(.25f, 1.5f)] public float contrast;
        [Range(0f, .15f)] public float blackLift;
        [Range(0f, 2f)] public float vignetteEV;

        public static DamagedPrimeSettings Default => new DamagedPrimeSettings
        {
            blurRadius = 14f,
            grainAmount = .13f,
            grainSize = .95f,
            exposureEV = -.25f,
            contrast = .88f,
            blackLift = .025f,
            vignetteEV = .25f
        };

        public DamagedPrimeSettings Sanitized()
        {
            DamagedPrimeSettings result = this;
            result.blurRadius = ClampFinite(blurRadius, 0f, 32f, 14f);
            result.grainAmount = ClampFinite(grainAmount, 0f, .35f, .13f);
            result.grainSize = ClampFinite(grainSize, .5f, 4f, .95f);
            result.exposureEV = ClampFinite(exposureEV, -3f, 2f, -.25f);
            result.contrast = ClampFinite(contrast, .25f, 1.5f, .88f);
            result.blackLift = ClampFinite(blackLift, 0f, .15f, .025f);
            result.vignetteEV = ClampFinite(vignetteEV, 0f, 2f, .25f);
            return result;
        }

        private static float ClampFinite(float value, float minimum, float maximum, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, minimum, maximum);
    }

    public sealed class DamagedPrimePhotoRenderSnapshot : PhotoRenderSnapshot
    {
        private readonly DamagedPrimeSettings settings;
        public DamagedPrimeSettings Settings => settings;

        public DamagedPrimePhotoRenderSnapshot(string setId, int version, int seed, DamagedPrimeSettings settings)
            : base(setId, version, seed)
        {
            this.settings = settings.Sanitized();
        }

        public override RenderTexture Render(Texture2D source, Rect crop, float legacySaturation, int maximumSize) =>
            DamagedPrimePhotoRenderer.Render(source, crop, settings, Seed, maximumSize);
    }
}
