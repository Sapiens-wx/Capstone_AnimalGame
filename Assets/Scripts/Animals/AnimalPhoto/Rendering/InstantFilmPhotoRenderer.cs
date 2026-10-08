using System;
using UnityEngine;

namespace AnimalGame.Animals
{
    internal static class InstantFilmPhotoRenderer
    {
        public static RenderTexture Render(Texture2D source, Rect crop, InstantFilmSettings settings, int seed, int maximumSize)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            Shader shader = Resources.Load<Shader>("AnimalPhotoInstantFilm");
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("Instant film photo shader is missing or unsupported.");

            crop = AnimalPhotoProcessing.ClampSubjectRect(crop);
            settings = settings.Sanitized();
            float cropWidth = source.width * crop.width;
            float cropHeight = source.height * crop.height;
            float longEdge = Mathf.Max(cropWidth, cropHeight);
            float scale = Mathf.Min(1, Mathf.Clamp(maximumSize, 16, 4096) / longEdge);
            int width = Mathf.Max(1, Mathf.RoundToInt(cropWidth * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(cropHeight * scale));
            var output = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "Instant Film Animal Photo",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            RenderTexture temporary = null;
            RenderTexture previous = RenderTexture.active;
            bool previousSRGBWrite = GL.sRGBWrite;
            try
            {
                material.SetTexture("_OriginalTex", source);
                material.SetVector("_Crop", new Vector4(crop.x, crop.y, crop.width, crop.height));
                material.SetVector("_PhotoSize", new Vector4(cropWidth / longEdge * 1024, cropHeight / longEdge * 1024, 0, 0));
                material.SetInteger("_Seed", seed);
                material.SetVector("_Modules", new Vector4(settings.toneEnabled ? 1 : 0, settings.colorEnabled ? 1 : 0,
                    settings.grainEnabled ? 1 : 0, settings.vignetteEnabled ? 1 : 0));
                material.SetVector("_Tone", new Vector4(settings.exposureEV, settings.toneStrength, settings.contrast, settings.midtoneLift));
                material.SetVector("_ToneLimits", new Vector4(settings.blackLift, settings.highlightCompression, 0, 0));
                material.SetVector("_Color", new Vector4(settings.saturation, settings.highlightWarmth, settings.shadowCoolness, settings.colorProtection));
                material.SetVector("_Grain", new Vector4(settings.grainAmount, settings.grainSize, settings.grainChroma, 0));
                material.SetFloat("_VignetteEV", settings.vignetteEV);
                material.SetFloat("_Strength", settings.strength);
                material.SetFloat("_Softness", settings.softness);
                if (!output.Create()) throw new InvalidOperationException("Could not allocate instant film photo texture.");

                // Both targets store sRGB in a Linear project. Sampling decodes to linear once;
                // the shader performs explicit perceptual transforms only within its grading stage.
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                if (settings.softnessEnabled && settings.softness > 0 && settings.strength > 0)
                {
                    temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    temporary.filterMode = FilterMode.Bilinear;
                    temporary.wrapMode = TextureWrapMode.Clamp;
                    Graphics.Blit(source, temporary, material, 0);
                    material.SetFloat("_Softened", 1);
                    Graphics.Blit(temporary, output, material, 1);
                }
                else
                {
                    material.SetFloat("_Softened", 0);
                    Graphics.Blit(source, output, material, 1);
                }
                return output;
            }
            catch
            {
                AnimalPhotoProcessing.Release(output);
                throw;
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = previousSRGBWrite;
                if (temporary != null) RenderTexture.ReleaseTemporary(temporary);
                if (Application.isPlaying) UnityEngine.Object.Destroy(material);
                else UnityEngine.Object.DestroyImmediate(material);
            }
        }
    }
}
