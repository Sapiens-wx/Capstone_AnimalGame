using UnityEngine;

namespace AnimalGame.Animals
{
    /// <summary>Immutable processing choices for one capture; safe to retain in the album.</summary>
    public sealed class AnimalPhoto
    {
        public Texture2D Source { get; }
        public Rect Crop { get; }
        public float Saturation { get; }

        public AnimalPhoto(Texture2D source, Rect crop, float saturation)
        {
            Source = source;
            Crop = AnimalPhotoProcessing.ClampSubjectRect(crop);
            Saturation = Mathf.Clamp(saturation, 0, 2);
        }

        /// <summary>The caller owns the returned processed texture and must release/destroy it.</summary>
        public RenderTexture Render(int maximumSize = 1024)
        {
            return AnimalPhotoProcessing.Render(Source, Crop, Saturation, maximumSize);
        }
    }
}
