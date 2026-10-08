using UnityEngine;

namespace AnimalGame.Animals
{
    /// <summary>Frozen render choices for one exposure. Returned textures belong to the caller.</summary>
    public abstract class PhotoRenderSnapshot
    {
        public string SetId { get; }
        public int Version { get; }
        public int Seed { get; }

        protected PhotoRenderSnapshot(string setId, int version, int seed)
        {
            SetId = setId;
            Version = version;
            Seed = seed;
        }

        public abstract RenderTexture Render(Texture2D source, Rect crop, float legacySaturation, int maximumSize);

        public static PhotoRenderSnapshot Basic(int seed = 0)
        {
            return new BasicPhotoRenderSnapshot("basic-fallback", 1, seed, 1);
        }
    }

    public sealed class BasicPhotoRenderSnapshot : PhotoRenderSnapshot
    {
        public float Saturation { get; }

        public BasicPhotoRenderSnapshot(string setId, int version, int seed, float saturation)
            : base(setId, version, seed)
        {
            Saturation = Mathf.Clamp(saturation, 0, 2);
        }

        public override RenderTexture Render(Texture2D source, Rect crop, float legacySaturation, int maximumSize)
        {
            return AnimalPhotoProcessing.Render(source, crop, legacySaturation * Saturation, maximumSize);
        }
    }

    public sealed class InstantFilmPhotoRenderSnapshot : PhotoRenderSnapshot
    {
        // A struct of primitive values: editing a Set cannot mutate an existing photograph.
        private readonly InstantFilmSettings settings;
        public InstantFilmSettings Settings => settings;

        public InstantFilmPhotoRenderSnapshot(string setId, int version, int seed, InstantFilmSettings settings)
            : base(setId, version, seed)
        {
            this.settings = settings.Sanitized();
        }

        public override RenderTexture Render(Texture2D source, Rect crop, float legacySaturation, int maximumSize)
        {
            return InstantFilmPhotoRenderer.Render(source, crop, settings, Seed, maximumSize);
        }
    }
}
