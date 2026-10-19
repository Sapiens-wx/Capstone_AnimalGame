using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.MapTest
{
    /// <summary>A region outline on the plan's X/Z plane, measured in metres.</summary>
    [Serializable]
    public sealed class OfficialMapRegion
    {
        public string Id;
        public string DisplayName;
        public Color Color = new Color(0.5f, 0.8f, 0.4f, 0.2f);
        public Vector2[] PointsMeters = Array.Empty<Vector2>();
        public Vector2 LabelPositionMeters;
        public Vector2 ElevationRangeMeters;

        /// <summary>Horizontal footprint, not the area of a future sculpted surface.</summary>
        public double AreaSquareMeters => OfficialMapPlan.PolygonArea(PointsMeters);
        public double AreaSquareKilometers => AreaSquareMeters / 1000000d;

        public double TileEquivalent(float tileSizeMeters)
        {
            return tileSizeMeters > 0f ? AreaSquareMeters / ((double)tileSizeMeters * tileSizeMeters) : 0d;
        }

        public Rect BoundsMeters => OfficialMapPlan.PolygonBounds(PointsMeters);
    }

    /// <summary>
    /// Planning data only. The editor draws the outlines and dimensional references;
    /// this component creates no Terrain, renderer, collider, or runtime update work.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Animal Game/Map Test/Official Map Plan")]
    public sealed class OfficialMapPlan : MonoBehaviour
    {
        public const double TargetPlainsAreaSquareMeters = 1562500d;
        public const float PlainsAreaReferenceSizeMeters = 1250f;
        public const float V1ReferenceHeightMeters = 70f;
        public const float V1TileSizeMeters = 250f;

        [Header("Metric Planning Bounds")]
        [SerializeField] private Vector2 worldSizeMeters = new Vector2(4250f, 2500f);
        [SerializeField, Min(1f)] private float tileSizeMeters = V1TileSizeMeters;
        [Tooltip("Planning ruler range only; no Terrain height samples are created or measured.")]
        [SerializeField, Min(1f)] private float maximumReferenceHeightMeters = 350f;
        [Tooltip("X/Z position of the reference group, in metres from the plan origin.")]
        [SerializeField] private Vector2 referenceOriginMeters = new Vector2(-400f, 250f);
        [Tooltip("Height of the ruler's lowest point above the plan origin, in metres (1 Unity unit = 1 metre). Align to Plains samples the current grassland surface once; it does not move the terrain.")]
        [SerializeField] private float referenceBaseHeightMeters = 0f;
        [SerializeField] private OfficialMapRegion[] regions = Array.Empty<OfficialMapRegion>();

        [Header("Scene View Display")]
        [SerializeField] private bool showPlan = true;
        [SerializeField] private bool showGrid = true;
        [SerializeField] private bool showLabels = true;
        [SerializeField] private bool showHeightReferences = true;
        [SerializeField] private bool showAreaReferences = true;

        public Vector2 WorldSizeMeters => worldSizeMeters;
        public float TileSizeMeters => tileSizeMeters;
        public float MaximumReferenceHeightMeters => maximumReferenceHeightMeters;
        public Vector2 ReferenceOriginMeters => referenceOriginMeters;
        public float ReferenceBaseHeightMeters => referenceBaseHeightMeters;
        public IReadOnlyList<OfficialMapRegion> Regions => regions ?? Array.Empty<OfficialMapRegion>();
        public bool ShowPlan => showPlan;
        public bool ShowGrid => showGrid;
        public bool ShowLabels => showLabels;
        public bool ShowHeightReferences => showHeightReferences;
        public bool ShowAreaReferences => showAreaReferences;
        public Rect BoundsMeters => new Rect(Vector2.zero, worldSizeMeters);

        private void OnValidate()
        {
            if (float.IsNaN(referenceBaseHeightMeters) || float.IsInfinity(referenceBaseHeightMeters))
                referenceBaseHeightMeters = 0f;
        }

        /// <summary>Changes planning data without changing any Terrain or display switches.</summary>
        public void Configure(Vector2 worldSize, float tileSize, float maximumReferenceHeight,
            Vector2 referenceOrigin, OfficialMapRegion[] planRegions)
        {
            if (!IsFinitePositive(worldSize.x) || !IsFinitePositive(worldSize.y)
                || !IsFinitePositive(tileSize) || !IsFinitePositive(maximumReferenceHeight))
            {
                throw new ArgumentOutOfRangeException(nameof(worldSize), "Plan dimensions and ruler range must be finite and positive.");
            }

            worldSizeMeters = worldSize;
            tileSizeMeters = tileSize;
            maximumReferenceHeightMeters = maximumReferenceHeight;
            referenceOriginMeters = referenceOrigin;
            regions = planRegions == null ? Array.Empty<OfficialMapRegion>() : (OfficialMapRegion[])planRegions.Clone();
        }

        /// <summary>
        /// Converts X/Z metres and height above the plan origin to Unity world units.
        /// Each unit remains one metre. Rotated or scaled hierarchies must be corrected first.
        /// </summary>
        public Vector3 CoordinateToWorld(Vector2 coordinateMeters, float heightMeters = 0f)
        {
            if (!HasMetricTransform(out string issue))
                throw new InvalidOperationException(issue);

            return transform.position + new Vector3(coordinateMeters.x, heightMeters, coordinateMeters.y);
        }

        /// <summary>Checks every ancestor, including transformations that could cancel each other.</summary>
        public bool HasMetricTransform(out string issue)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                Vector3 scale = current.localScale;
                if (Mathf.Abs(scale.x - 1f) > 0.0001f || Mathf.Abs(scale.y - 1f) > 0.0001f
                    || Mathf.Abs(scale.z - 1f) > 0.0001f
                    || Quaternion.Angle(current.localRotation, Quaternion.identity) > 0.01f)
                {
                    issue = $"{current.name}: the plan and all parents require rotation (0, 0, 0) and scale (1, 1, 1) so that one Unity unit equals one metre.";
                    return false;
                }
            }

            issue = string.Empty;
            return true;
        }

        public OfficialMapRegion FindRegion(string id)
        {
            if (regions == null)
                return null;

            foreach (OfficialMapRegion region in regions)
            {
                if (region != null && string.Equals(region.Id, id, StringComparison.Ordinal))
                    return region;
            }

            return null;
        }

        /// <summary>Unsigned shoelace area; clockwise and counterclockwise outlines have equal area.</summary>
        public static double PolygonArea(Vector2[] points)
        {
            if (points == null || points.Length < 3)
                return 0d;

            double twiceArea = 0d;
            Vector2 previous = points[points.Length - 1];
            for (int index = 0; index < points.Length; index++)
            {
                Vector2 current = points[index];
                twiceArea += (double)previous.x * current.y - (double)current.x * previous.y;
                previous = current;
            }

            return Math.Abs(twiceArea) * 0.5d;
        }

        public static Rect PolygonBounds(Vector2[] points)
        {
            if (points == null || points.Length == 0)
                return new Rect();

            Vector2 minimum = points[0];
            Vector2 maximum = points[0];
            for (int index = 1; index < points.Length; index++)
            {
                minimum = Vector2.Min(minimum, points[index]);
                maximum = Vector2.Max(maximum, points[index]);
            }

            return Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
        }

        private static bool IsFinitePositive(float value)
        {
            return value > 0f && !float.IsInfinity(value) && !float.IsNaN(value);
        }
    }
}
