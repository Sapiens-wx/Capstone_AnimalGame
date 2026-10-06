using System;
using UnityEngine;

namespace AnimalGame.Animals
{
    /// <summary>Artist controls measured in a defined display/perceptual domain, except exposure and vignette in EV.</summary>
    [Serializable]
    public struct InstantFilmSettings
    {
        [Header("Overall")]
        [Tooltip("Zero shows the original photo. One applies the complete look.")]
        [Range(0, 1)] public float strength;

        [Header("Tone")]
        public bool toneEnabled;
        [Tooltip("Exposure compensation in stops, applied in linear light.")]
        [Range(-2, 2)] public float exposureEV;
        [Range(0, 1)] public float toneStrength;
        [Range(0.7f, 1.3f)] public float contrast;
        [Tooltip("Gentle midtone adjustment. The endpoints remain fixed.")]
        [Range(-0.15f, 0.15f)] public float midtoneLift;
        [Tooltip("Small neutral black floor. Keep low to retain readable dark plumage.")]
        [Range(0, 0.12f)] public float blackLift;
        [Tooltip("Softens the bright shoulder without a hard white clipping edge.")]
        [Range(0, 1)] public float highlightCompression;

        [Header("Color")]
        public bool colorEnabled;
        [Range(0, 2)] public float saturation;
        [Range(0, 0.2f)] public float highlightWarmth;
        [Range(0, 0.2f)] public float shadowCoolness;
        [Tooltip("Protects already saturated animal colors from split-tone color casts. It is not a species mask.")]
        [Range(0, 1)] public float colorProtection;

        [Header("Grain")]
        public bool grainEnabled;
        [Tooltip("Standard deviation near the midtones in display RGB. Grain is fixed for each exposure.")]
        [Range(0, 0.06f)] public float grainAmount;
        [Tooltip("Grain size in pixels at a reference photo long edge of 1024, independent of preview resolution.")]
        [Range(0.5f, 4)] public float grainSize;
        [Range(0, 1)] public float grainChroma;

        [Header("Vignette")]
        public bool vignetteEnabled;
        [Tooltip("Broad corner falloff in exposure stops, without a border.")]
        [Range(0, 1)] public float vignetteEV;

        [Header("Optical Softness")]
        [Tooltip("Uses an additional cropped blur pass. Disabled by default to protect feather and fur detail.")]
        public bool softnessEnabled;
        [Range(0, 1)] public float softness;

        public static InstantFilmSettings Default => new InstantFilmSettings
        {
            strength = 1,
            toneEnabled = true,
            exposureEV = 0,
            toneStrength = 0.7f,
            contrast = 1.1f,
            midtoneLift = 0.015f,
            blackLift = 0.012f,
            highlightCompression = 0.22f,
            colorEnabled = true,
            saturation = 0.98f,
            highlightWarmth = 0.04f,
            shadowCoolness = 0.03f,
            colorProtection = 0.85f,
            grainEnabled = true,
            grainAmount = 0.012f,
            grainSize = 1.2f,
            grainChroma = 0.05f,
            vignetteEnabled = true,
            vignetteEV = 0.18f,
            softnessEnabled = false,
            softness = 0
        };

        public InstantFilmSettings Sanitized()
        {
            var value = this;
            value.strength = Clamp(strength, 0, 1);
            value.exposureEV = Clamp(exposureEV, -2, 2);
            value.toneStrength = Clamp(toneStrength, 0, 1);
            value.contrast = Clamp(contrast, 0.7f, 1.3f);
            value.midtoneLift = Clamp(midtoneLift, -0.15f, 0.15f);
            value.blackLift = Clamp(blackLift, 0, 0.12f);
            value.highlightCompression = Clamp(highlightCompression, 0, 1);
            value.saturation = Clamp(saturation, 0, 2);
            value.highlightWarmth = Clamp(highlightWarmth, 0, 0.2f);
            value.shadowCoolness = Clamp(shadowCoolness, 0, 0.2f);
            value.colorProtection = Clamp(colorProtection, 0, 1);
            value.grainAmount = Clamp(grainAmount, 0, 0.06f);
            value.grainSize = Clamp(grainSize, 0.5f, 4);
            value.grainChroma = Clamp(grainChroma, 0, 1);
            value.vignetteEV = Clamp(vignetteEV, 0, 1);
            value.softness = Clamp(softness, 0, 1);
            return value;
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? minimum : Mathf.Clamp(value, minimum, maximum);
        }
    }
}
