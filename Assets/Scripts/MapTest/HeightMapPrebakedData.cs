using UnityEngine;

namespace AnimalGame.MapTest
{
    /// <summary>Editor-generated physical terrain data for a fixed level.</summary>
    public sealed class HeightMapPrebakedData : ScriptableObject
    {
        [SerializeField] private Texture2D sourceHeightMap;
        [SerializeField] private int resolution;
        [SerializeField] private Vector2 mapSizeMeters;
        [SerializeField] private float minimumHeightMeters;
        [SerializeField] private float maximumHeightMeters;
        [SerializeField] private float sourceMinimum;
        [SerializeField] private float sourceMaximum;
        [SerializeField] private bool normalizeSourceRange;
        [SerializeField] private float surfaceSmoothingSigmaMeters;
        [SerializeField] private float detailSmoothingSigmaMeters;
        [SerializeField] private Texture2D rawDetailTexture;
        [SerializeField] private Texture2D detailTexture;
        [SerializeField] private Texture2D surfaceTexture;

        public int Resolution => resolution;
        public Vector2 MapSizeMeters => mapSizeMeters;
        public float MinimumHeightMeters => minimumHeightMeters;
        public float MaximumHeightMeters => maximumHeightMeters;
        public float SourceMinimum => sourceMinimum;
        public float SourceMaximum => sourceMaximum;
        public Texture2D RawDetailTexture => rawDetailTexture;
        public Texture2D DetailTexture => detailTexture;
        public Texture2D SurfaceTexture => surfaceTexture;

        public bool Matches(HeightMapLevelAsset level)
        {
            return level != null && sourceHeightMap == level.HeightMap
                   && resolution == level.BakedHeightResolution
                   && mapSizeMeters == level.MapSizeMeters
                   && Mathf.Approximately(minimumHeightMeters, level.MinimumHeightMeters)
                   && Mathf.Approximately(maximumHeightMeters, level.MaximumHeightMeters)
                   && normalizeSourceRange == level.NormalizeSourceRange
                   && Mathf.Approximately(surfaceSmoothingSigmaMeters, level.SurfaceSmoothingSigmaMeters)
                   && Mathf.Approximately(detailSmoothingSigmaMeters, level.DetailSmoothingSigmaMeters)
                   && rawDetailTexture != null && detailTexture != null
                   && surfaceTexture != null;
        }

        public void SetBakedData(
            HeightMapLevelAsset level, float bakedSourceMinimum,
            float bakedSourceMaximum, Texture2D rawTexture,
            Texture2D bakedDetailTexture, Texture2D bakedSurfaceTexture)
        {
            sourceHeightMap = level.HeightMap;
            resolution = level.BakedHeightResolution;
            mapSizeMeters = level.MapSizeMeters;
            minimumHeightMeters = level.MinimumHeightMeters;
            maximumHeightMeters = level.MaximumHeightMeters;
            sourceMinimum = bakedSourceMinimum;
            sourceMaximum = bakedSourceMaximum;
            normalizeSourceRange = level.NormalizeSourceRange;
            surfaceSmoothingSigmaMeters = level.SurfaceSmoothingSigmaMeters;
            detailSmoothingSigmaMeters = level.DetailSmoothingSigmaMeters;
            rawDetailTexture = rawTexture;
            detailTexture = bakedDetailTexture;
            surfaceTexture = bakedSurfaceTexture;
        }
    }
}
