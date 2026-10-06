using UnityEngine;

namespace AnimalGame.Animals
{
    /// <summary>Immutable processing choices for one capture; safe to retain in the album.</summary>
    public sealed class AnimalPhoto
    {
        public Texture2D Source { get; }
        public Rect Crop { get; }
        public float Saturation { get; }
        public PhotoCaptureSettings CaptureSettings { get; }
        public PhotoRenderSnapshot RenderSnapshot => CaptureSettings.RenderSnapshot;

        public AnimalPhoto(Texture2D source, Rect crop, float saturation)
            : this(source, crop, saturation, PhotoCaptureSettings.Basic()) { }

        public AnimalPhoto(Texture2D source, Rect crop, float saturation, PhotoCaptureSettings captureSettings)
        {
            Source = source;
            Crop = AnimalPhotoProcessing.ClampSubjectRect(crop);
            Saturation = Mathf.Clamp(saturation, 0, 2);
            CaptureSettings = captureSettings ?? PhotoCaptureSettings.Basic();
        }

        public AnimalPhoto WithCaptureSettings(PhotoCaptureSettings settings) =>
            new AnimalPhoto(Source, Crop, Saturation, settings);

        /// <summary>The caller owns the returned processed texture and must release/destroy it.</summary>
        public RenderTexture Render(int maximumSize = 1024)
        {
            return RenderSnapshot.Render(Source, Crop, Saturation, maximumSize);
        }
    }
}
