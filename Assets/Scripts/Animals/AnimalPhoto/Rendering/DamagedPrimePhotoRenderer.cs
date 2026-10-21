using System;
using UnityEngine;

namespace AnimalGame.Animals
{
    /// <summary>Optical blur happens before the damaged sensor's fixed monochrome noise.</summary>
    internal static class DamagedPrimePhotoRenderer
    {
        public static RenderTexture Render(Texture2D source, Rect crop, DamagedPrimeSettings settings, int seed, int maximumSize)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            Shader shader = Resources.Load<Shader>("AnimalPhotoDamagedPrime");
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("Damaged prime photo shader is missing or unsupported.");

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
                name = "Damaged Prime Animal Photo",
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
                // Optical radius and sensor grain use the same photo coordinates at every display size.
                material.SetVector("_PhotoSize", new Vector4(cropWidth / longEdge * 1024, cropHeight / longEdge * 1024, 0, 0));
                material.SetVector("_SourcePhotoHalfTexel", new Vector4(
                    Mathf.Min(0.5f, 0.5f / cropWidth), Mathf.Min(0.5f, 0.5f / cropHeight), 0, 0));
                material.SetInteger("_Seed", seed);
                material.SetFloat("_BlurRadius", settings.blurRadius);
                material.SetVector("_Tone", new Vector4(settings.exposureEV, settings.contrast, settings.blackLift, settings.vignetteEV));
                material.SetVector("_Grain", new Vector4(settings.grainAmount, settings.grainSize, 0, 0));
                if (!output.Create()) throw new InvalidOperationException("Could not allocate damaged prime photo texture.");

                temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                temporary.filterMode = FilterMode.Bilinear;
                temporary.wrapMode = TextureWrapMode.Clamp;
                // Both passes blur in linear light; only grading and noise use the display domain.
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(source, temporary, material, 0);
                Graphics.Blit(temporary, output, material, 1);
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
