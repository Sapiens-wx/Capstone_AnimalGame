using UnityEngine;

namespace AnimalGame.MapTest
{
    /// <summary>Editor-generated physical terrain data for a fixed level.</summary>
    public sealed class HeightMapPrebakedData : ScriptableObject
    {
        public const int CurrentHeightDataVersion = 1;
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
        [SerializeField] private int heightDataVersion;
        [Tooltip("Row-major little-endian Float32 heights in meters; no header or normalization.")]
        [SerializeField] private TextAsset rawDetailHeightData;
        [Tooltip("Detail heights in meters. References the raw binary when both channels are identical.")]
        [SerializeField] private TextAsset detailHeightData;
        [SerializeField] private TextAsset surfaceHeightData;
        [SerializeField] private Texture2D surfaceTexture;
#if UNITY_EDITOR
        // Retained only for the explicit editor migration of existing texture caches.
        [SerializeField, HideInInspector] private Texture2D rawDetailTexture;
        [SerializeField, HideInInspector] private Texture2D detailTexture;
        public Texture2D LegacyRawDetailTexture => rawDetailTexture;
        public Texture2D LegacyDetailTexture => detailTexture;
#endif

        public int Resolution => resolution;
        public Vector2 MapSizeMeters => mapSizeMeters;
        public float MinimumHeightMeters => minimumHeightMeters;
        public float MaximumHeightMeters => maximumHeightMeters;
        public float SourceMinimum => sourceMinimum;
        public float SourceMaximum => sourceMaximum;
        public TextAsset RawDetailHeightData => rawDetailHeightData;
        public TextAsset DetailHeightData => detailHeightData;
        public TextAsset SurfaceHeightData => surfaceHeightData;
        public Texture2D SurfaceTexture => surfaceTexture;

        public bool HasHeightData => heightDataVersion == CurrentHeightDataVersion
            && System.BitConverter.IsLittleEndian && resolution > 0 && resolution <= 4096
            && HasSampleCount(rawDetailHeightData) && HasSampleCount(DetailHeightData)
            && HasSampleCount(surfaceHeightData) && surfaceTexture != null
            && surfaceTexture.width == resolution && surfaceTexture.height == resolution;

        private bool HasSampleCount(TextAsset data) => data != null
            && data.dataSize == (long)resolution * resolution * sizeof(float);

        public bool Matches(HeightMapLevelAsset level)
        {
            return HasHeightData && MatchesSourceSettings(level);
        }

        public bool MatchesSourceSettings(HeightMapLevelAsset level)
        {
            return level != null && sourceHeightMap == level.HeightMap
                   && resolution == level.BakedHeightResolution
                   && mapSizeMeters == level.MapSizeMeters
                   && Mathf.Approximately(minimumHeightMeters, level.MinimumHeightMeters)
                   && Mathf.Approximately(maximumHeightMeters, level.MaximumHeightMeters)
                   && normalizeSourceRange == level.NormalizeSourceRange
                   && Mathf.Approximately(surfaceSmoothingSigmaMeters, level.SurfaceSmoothingSigmaMeters)
                   && Mathf.Approximately(detailSmoothingSigmaMeters, level.DetailSmoothingSigmaMeters);
        }

        public void SetBakedData(
            HeightMapLevelAsset level, float bakedSourceMinimum,
            float bakedSourceMaximum, TextAsset rawData,
            TextAsset detailData, TextAsset surfaceData, Texture2D bakedSurfaceTexture)
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
            heightDataVersion = CurrentHeightDataVersion;
            rawDetailHeightData = rawData;
            detailHeightData = detailData;
            surfaceHeightData = surfaceData;
            surfaceTexture = bakedSurfaceTexture;
#if UNITY_EDITOR
            rawDetailTexture = null;
            detailTexture = null;
#endif
        }
    }
}
