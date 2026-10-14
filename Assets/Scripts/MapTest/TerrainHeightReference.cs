using System;
using System.Text;
using UnityEngine;

namespace AnimalGame.MapTest
{
    /// <summary>
    /// Cached terrain measurements for the edit-mode height reference drawn by its editor.
    /// This component does not create renderers, colliders, or runtime update work.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Animal Game/Map Test/Terrain Height Reference")]
    public sealed class TerrainHeightReference : MonoBehaviour
    {
        public const float V1ReferenceHeightMeters = 70f;

        [Header("Reference Terrain")]
        [SerializeField] private Terrain[] terrains = Array.Empty<Terrain>();
        [Tooltip("World-space bottom of the fixed 70 m ruler. V1 uses Y = 0 and unit terrain scale.")]
        [SerializeField] private Vector3 rulerBasePosition;

        [Header("Scene View Display")]
        [SerializeField] private bool showReference = true;
        [SerializeField] private bool showOneMeterTicks = true;
        [SerializeField] private bool showLabels = true;
        [SerializeField] private bool showSizeReferences = true;
        [SerializeField] private bool showMeasuredExtrema = true;
        [SerializeField] private bool showProjectionLines = true;

        [SerializeField, HideInInspector] private bool hasMeasurements;
        [SerializeField, HideInInspector] private float cachedMinHeightMeters;
        [SerializeField, HideInInspector] private float cachedMaxHeightMeters;
        [SerializeField, HideInInspector] private Vector3 cachedMinWorldPosition;
        [SerializeField, HideInInspector] private Vector3 cachedMaxWorldPosition;
        [SerializeField, HideInInspector] private int cachedTerrainCount;
        [SerializeField, HideInInspector] private long cachedSampleCount;
        [SerializeField, HideInInspector] private string measurementStatus = "Not measured. Use Refresh Measurements after sculpting.";
        [SerializeField, HideInInspector] private string measurementTimeUtc;

        public Vector3 RulerBasePosition => rulerBasePosition;
        public bool ShowReference => showReference;
        public bool ShowOneMeterTicks => showOneMeterTicks;
        public bool ShowLabels => showLabels;
        public bool ShowSizeReferences => showSizeReferences;
        public bool ShowMeasuredExtrema => showMeasuredExtrema;
        public bool ShowProjectionLines => showProjectionLines;
        public bool HasMeasurements => hasMeasurements;
        public float CachedMinHeightMeters => cachedMinHeightMeters;
        public float CachedMaxHeightMeters => cachedMaxHeightMeters;
        public float CachedHeightDifferenceMeters => hasMeasurements ? cachedMaxHeightMeters - cachedMinHeightMeters : 0f;
        public Vector3 CachedMinWorldPosition => cachedMinWorldPosition;
        public Vector3 CachedMaxWorldPosition => cachedMaxWorldPosition;
        public int CachedTerrainCount => cachedTerrainCount;
        public long CachedSampleCount => cachedSampleCount;
        public string MeasurementStatus => measurementStatus;
        public string MeasurementTimeUtc => measurementTimeUtc;

        /// <summary>Configures world-space references. Call RefreshMeasurements to read the heightmaps.</summary>
        public void Configure(Terrain[] targetTerrains, Vector3 rulerBaseWorldPosition)
        {
            terrains = targetTerrains == null ? Array.Empty<Terrain>() : (Terrain[])targetTerrains.Clone();
            rulerBasePosition = rulerBaseWorldPosition;
            ClearMeasurements("Configuration changed. Refresh Measurements to read the current terrain.");
        }

        /// <summary>
        /// Reads each assigned TerrainData once. Measurements use actual height samples and absolute
        /// world Y, rather than assuming that TerrainData.size.y is the terrain's actual height difference.
        /// Invalid terrain transforms are skipped because V1 requires upright, unit-scale terrain.
        /// </summary>
        public bool RefreshMeasurements()
        {
            ClearMeasurements(string.Empty);
            var issues = new StringBuilder();
            float minimum = float.PositiveInfinity;
            float maximum = float.NegativeInfinity;

            if (terrains == null || terrains.Length == 0)
            {
                measurementStatus = "No Terrain assigned. Assign Terrain components and refresh.";
                return false;
            }

            for (int terrainIndex = 0; terrainIndex < terrains.Length; terrainIndex++)
            {
                Terrain terrain = terrains[terrainIndex];
                if (terrain == null)
                {
                    AppendIssue(issues, $"Terrain slot {terrainIndex} is empty.");
                    continue;
                }

                TerrainData data = terrain.terrainData;
                if (data == null)
                {
                    AppendIssue(issues, $"{terrain.name}: TerrainData is missing.");
                    continue;
                }

                if (!IsUnitScale(terrain.transform.lossyScale)
                    || Quaternion.Angle(terrain.transform.rotation, Quaternion.identity) > 0.01f)
                {
                    AppendIssue(issues, $"{terrain.name}: use world rotation (0, 0, 0) and unit scale (1, 1, 1), including parent transforms.");
                    continue;
                }

                int resolution = data.heightmapResolution;
                if (resolution < 2 || data.size.y <= 0f)
                {
                    AppendIssue(issues, $"{terrain.name}: heightmap resolution or terrain height is invalid.");
                    continue;
                }

                try
                {
                    float[,] heights = data.GetHeights(0, 0, resolution, resolution);
                    Vector3 origin = terrain.transform.position;
                    Vector3 size = data.size;
                    float xStep = size.x / (resolution - 1);
                    float zStep = size.z / (resolution - 1);

                    for (int z = 0; z < resolution; z++)
                    {
                        for (int x = 0; x < resolution; x++)
                        {
                            float sample = heights[z, x];
                            if (float.IsNaN(sample) || float.IsInfinity(sample))
                                continue;

                            float absoluteHeight = origin.y + sample * size.y;
                            if (absoluteHeight < minimum)
                            {
                                minimum = absoluteHeight;
                                cachedMinWorldPosition = origin + new Vector3(x * xStep, sample * size.y, z * zStep);
                            }

                            if (absoluteHeight > maximum)
                            {
                                maximum = absoluteHeight;
                                cachedMaxWorldPosition = origin + new Vector3(x * xStep, sample * size.y, z * zStep);
                            }
                        }
                    }

                    cachedTerrainCount++;
                    cachedSampleCount += (long)resolution * resolution;
                }
                catch (Exception exception)
                {
                    AppendIssue(issues, $"{terrain.name}: could not read heights ({exception.Message}).");
                }
            }

            hasMeasurements = cachedTerrainCount > 0 && !float.IsInfinity(minimum) && !float.IsInfinity(maximum);
            if (hasMeasurements)
            {
                cachedMinHeightMeters = minimum;
                cachedMaxHeightMeters = maximum;
                measurementTimeUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
                measurementStatus = $"Measured {cachedTerrainCount} Terrain(s). Values are cached; refresh after sculpting."
                    + (issues.Length > 0 ? "\nSkipped or invalid entries:\n" + issues : string.Empty);
            }
            else
            {
                measurementStatus = "No valid height samples were measured.\n" + issues;
            }

            return hasMeasurements;
        }

        private void ClearMeasurements(string status)
        {
            hasMeasurements = false;
            cachedMinHeightMeters = 0f;
            cachedMaxHeightMeters = 0f;
            cachedMinWorldPosition = Vector3.zero;
            cachedMaxWorldPosition = Vector3.zero;
            cachedTerrainCount = 0;
            cachedSampleCount = 0;
            measurementTimeUtc = string.Empty;
            measurementStatus = status;
        }

        private static bool IsUnitScale(Vector3 scale)
        {
            return Mathf.Abs(scale.x - 1f) < 0.0001f
                && Mathf.Abs(scale.y - 1f) < 0.0001f
                && Mathf.Abs(scale.z - 1f) < 0.0001f;
        }

        private static void AppendIssue(StringBuilder issues, string issue)
        {
            if (issues.Length > 0)
                issues.AppendLine();
            issues.Append(issue);
        }
    }
}
