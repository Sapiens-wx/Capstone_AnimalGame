using System;
using UnityEngine;

namespace AnimalGame.Animals
{
    public static class AnimalPhotoProcessing
    {
        public static Rect ClampSubjectRect(Rect rect)
        {
            if (float.IsNaN(rect.x) || float.IsNaN(rect.y) || float.IsNaN(rect.width) ||
                float.IsNaN(rect.height) || float.IsInfinity(rect.x) || float.IsInfinity(rect.y) ||
                float.IsInfinity(rect.width) || float.IsInfinity(rect.height))
                return new Rect(0, 0, 1, 1);
            float xMin = Mathf.Clamp(Mathf.Min(rect.x, rect.x + rect.width), 0, 0.9999f);
            float yMin = Mathf.Clamp(Mathf.Min(rect.y, rect.y + rect.height), 0, 0.9999f);
            return Rect.MinMaxRect(xMin, yMin,
                Mathf.Clamp(Mathf.Max(rect.x, rect.x + rect.width), xMin + 0.0001f, 1),
                Mathf.Clamp(Mathf.Max(rect.y, rect.y + rect.height), yMin + 0.0001f, 1));
        }

        public static bool CanSquareCrop(Rect subjectRect, int imageWidth, int imageHeight)
        {
            Rect subject = ClampSubjectRect(subjectRect);
            return imageWidth > 0 && imageHeight > 0 &&
                Mathf.Max(subject.width * imageWidth, subject.height * imageHeight) <= Mathf.Min(imageWidth, imageHeight);
        }

        public static bool TryRandomSquareCrop(Rect subjectRect, int imageWidth, int imageHeight, out Rect crop)
        {
            crop = default;
            if (!CanSquareCrop(subjectRect, imageWidth, imageHeight)) return false;
            Rect subject = ClampSubjectRect(subjectRect);
            // Square in source pixels, not normalized UV coordinates of a non-square image.
            float side = UnityEngine.Random.Range(
                Mathf.Max(subject.width * imageWidth, subject.height * imageHeight),
                Mathf.Min(imageWidth, imageHeight));
            float width = side / imageWidth;
            float height = side / imageHeight;
            float x = UnityEngine.Random.Range(Mathf.Max(0, subject.xMax - width), Mathf.Min(subject.xMin, 1 - width));
            float y = UnityEngine.Random.Range(Mathf.Max(0, subject.yMax - height), Mathf.Min(subject.yMin, 1 - height));
            crop = new Rect(x, y, width, height);
            return true;
        }

        public static RenderTexture Render(Texture2D source, Rect crop, float saturation, int maximumSize)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            Shader shader = Resources.Load<Shader>("AnimalPhotoProcess");
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("Animal photo processing shader is missing or unsupported.");
            crop = ClampSubjectRect(crop);
            float width = source.width * crop.width;
            float height = source.height * crop.height;
            float scale = Mathf.Min(1, Mathf.Clamp(maximumSize, 16, 4096) / Mathf.Max(width, height));
            var output = new RenderTexture(Mathf.Max(1, Mathf.RoundToInt(width * scale)),
                Mathf.Max(1, Mathf.RoundToInt(height * scale)), 0, RenderTextureFormat.ARGB32)
            { name = "Processed Animal Photo", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            RenderTexture previous = RenderTexture.active;
            try
            {
                material.SetVector("_Crop", new Vector4(crop.x, crop.y, crop.width, crop.height));
                material.SetFloat("_Saturation", Mathf.Clamp(saturation, 0, 2));
                if (!output.Create()) throw new InvalidOperationException("Could not allocate animal photo texture.");
                Graphics.Blit(source, output, material);
                return output;
            }
            catch
            {
                Release(output);
                throw;
            }
            finally
            {
                RenderTexture.active = previous;
                if (Application.isPlaying) UnityEngine.Object.Destroy(material);
                else UnityEngine.Object.DestroyImmediate(material);
            }
        }

        public static void Release(RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
            else UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
