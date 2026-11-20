using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace AnimalGame.Editor
{
    /// <summary>
    /// A metre-based 500 m sample, shared by all four Terrain tiles. Width always means the
    /// complete foot-to-foot extent, never radius. Every hill has a compact, smooth footprint.
    /// </summary>
    public static class PlainsSample500HeightField
    {
        public const float SizeMeters = 500f;
        public const float BaseHeightMeters = 80f;
        public const float ContourIntervalMeters = 7f;
        public const float NorthSouthCorridorX = 220f;
        public const float EastWestCorridorZ = 215f;

        public sealed class Feature
        {
            public string Id { get; }
            public string Kind { get; }
            public string Group { get; }
            public Vector2 CenterMeters { get; }
            public float HeightMeters { get; }
            public Vector2 WidthMeters { get; }
            public float RotationDegrees { get; }
            public string Profile { get; }
            public Vector2 ActualWidthMeters { get; }

            internal readonly float Cos;
            internal readonly float Sin;
            private readonly float skew;
            private readonly float normalization;
            private readonly float inverseRadiusX;
            private readonly float inverseRadiusZ;
            private readonly float maximumRadiusSquared;

            internal Feature(string id, string kind, string group, float x, float z, float height,
                float width, float depth, float rotation, float flankSkew, string profile = "Oval")
            {
                Id = id;
                Kind = kind;
                Group = group;
                CenterMeters = new Vector2(x, z);
                HeightMeters = height;
                WidthMeters = new Vector2(width, depth);
                RotationDegrees = rotation;
                Profile = profile;
                Vector2 extent = profile == "Twin" ? new Vector2(.99f, .80f)
                    : profile == "Bent" ? new Vector2(.96f, .86f)
                    : profile == "Teardrop" ? new Vector2(.925f, .85f)
                    : profile == "Kidney" ? new Vector2(.935f, .82f) : Vector2.one;
                ActualWidthMeters = Vector2.Scale(WidthMeters, extent);
                Cos = Mathf.Cos(rotation * Mathf.Deg2Rad);
                Sin = Mathf.Sin(rotation * Mathf.Deg2Rad);
                skew = flankSkew;
                inverseRadiusX = 2f / width;
                inverseRadiusZ = 2f / depth;
                float maximumRadius = Mathf.Max(width, depth) * 0.5f;
                maximumRadiusSquared = maximumRadius * maximumRadius;

                // A mild flank bias moves the crest without stretching either footprint axis.
                // Normalize the resulting polynomial so HeightMeters is the actual added peak.
                double crestU = Math.Abs(skew) < 0.00001
                    ? 0d : (-4d + Math.Sqrt(16d + 20d * skew * skew)) / (10d * skew);
                normalization = (float)(Math.Pow(1d - crestU * crestU, 2d) * (1d + skew * crestU));
                if (Profile != "Oval")
                {
                    normalization = 0f;
                    for (int iz = -200; iz <= 200; iz++)
                        for (int ix = -200; ix <= 200; ix++)
                            normalization = Mathf.Max(normalization, RawProfile(ix / 200f, iz / 200f));
                }
            }

            internal float AddHeight(float x, float z)
            {
                float dx = x - CenterMeters.x;
                float dz = z - CenterMeters.y;
                if (dx * dx + dz * dz >= maximumRadiusSquared)
                    return 0f;
                float u = (Cos * dx + Sin * dz) * inverseRadiusX;
                float v = (-Sin * dx + Cos * dz) * inverseRadiusZ;
                float radiusSquared = u * u + v * v;
                if (radiusSquared >= 1f)
                    return 0f;
                return HeightMeters * RawProfile(u, v) / normalization;
            }

            private float RawProfile(float u, float v)
            {
                if (Profile == "Twin")
                    return Blend(Cap(u, v, -.37f, 0f, .62f, .80f), .92f * Cap(u, v, .37f, .03f, .62f, .77f));
                if (Profile == "Bent")
                    return Blend(Blend(.94f * Cap(u, v, -.28f, .10f, .68f, .68f),
                        Cap(u, v, 0f, -.20f, .60f, .74f)), .88f * Cap(u, v, .28f, .10f, .68f, .68f));
                if (Profile == "Teardrop")
                    return Blend(Cap(u, v, -.15f, 0f, .80f, .85f), .6f * Cap(u, v, .45f, .12f, .45f, .50f));
                if (Profile == "Kidney")
                    return Blend(Cap(u, v, -.25f, -.12f, .70f, .68f), .88f * Cap(u, v, .26f, .18f, .66f, .66f))
                        * (1f - .22f * Cap(u, v, 0f, -.50f, .50f, .45f));
                float shoulder = Mathf.Max(0f, 1f - u * u - v * v);
                return shoulder * shoulder * (1f + skew * u);
            }

            // An L4 smooth union retains real lobes and saddles without the extra steepness of adding their slopes.
            private static float Blend(float a, float b)
            {
                a *= a;
                b *= b;
                return Mathf.Sqrt(Mathf.Sqrt(a * a + b * b));
            }

            private static float Cap(float u, float v, float x, float z, float width, float depth)
            {
                float dx = (u - x) / width;
                float dz = (v - z) / depth;
                float q = dx * dx + dz * dz;
                if (q >= 1f) return 0f;
                float shoulder = (1.004987562f - Mathf.Sqrt(q + .01f)) / .904987562f;
                return shoulder * Mathf.Sqrt(shoulder);
            }
        }

        private static readonly Feature[] Items =
        {
            new Feature("S01", "Small", "Southwest", 72, 76, 8.5f, 48, 46, -20, 0.16f, "Teardrop"),
            new Feature("R01", "Regular", "Southwest", 148, 85, 11.5f, 70, 63, 25, -0.17f),
            new Feature("S02", "Small", "Southwest", 109, 145, 8, 52, 48, 38, 0.14f, "Kidney"),
            new Feature("R02", "Regular", "Southwest", 178, 172, 10.5f, 75, 75, -25, -0.18f, "Bent"),

            new Feature("M01", "Main", "Northwest", 115, 340, 17.5f, 105, 105, -24, 0.14f, "Twin"),
            new Feature("S03", "Small", "Northwest", 78, 403, 8.5f, 50, 46, 22, -0.15f),
            new Feature("R03", "Regular", "Northwest", 165, 410, 11.5f, 72, 65, -18, 0.18f, "Teardrop"),
            new Feature("S04", "Small", "Northwest", 195, 333, 8.5f, 55, 51, 32, -0.14f, "Kidney"),

            // S05 crosses x = 250; R05 crosses z = 250. There is no per-tile noise or falloff.
            new Feature("S05", "Small", "Southeast", 250, 110, 9, 54, 49, -12, 0.16f),
            new Feature("R04", "Regular", "Southeast", 325, 100, 12, 72, 65, 30, -0.17f),
            new Feature("M02", "Main", "Southeast", 380, 157, 18, 105, 105, 16, 0.14f, "Bent"),
            new Feature("S06", "Small", "Southeast", 300, 180, 8.5f, 51, 47, -33, -0.15f),

            new Feature("R05", "Regular", "Northeast", 345, 250, 11, 75, 70, -24, 0.17f, "Kidney"),
            new Feature("S07", "Small", "Northeast", 285, 310, 9, 53, 49, 24, -0.18f, "Teardrop"),
            new Feature("R06", "Regular", "Northeast", 387, 377, 12, 74, 75, 30, 0.16f, "Twin"),
            new Feature("S08", "Small", "Northeast", 325, 414, 8.5f, 55, 50, -18, -0.16f, "Kidney")
        };

        public static IReadOnlyList<Feature> Features { get; } = Array.AsReadOnly(Items);

        /// <summary>Local sample X/Z in metres; returns absolute elevation in metres.</summary>
        public static float Evaluate(float x, float z)
        {
            // Flat outer edges also permit the sample to join untouched 80 m terrain later.
            if (x <= 0f || z <= 0f || x >= SizeMeters || z >= SizeMeters)
                return BaseHeightMeters;
            float edgeDistance = Mathf.Min(Mathf.Min(x, SizeMeters - x), Mathf.Min(z, SizeMeters - z));
            float edge = Mathf.Clamp01(edgeDistance / 30f);
            edge = edge * edge * (3f - 2f * edge);
            float height = BaseHeightMeters + edge *
                (1.8f * Mathf.Sin(x / 73f) * Mathf.Sin(z / 91f) + 0.8f * Mathf.Sin((x + z) / 47f));
            for (int index = 0; index < Items.Length; index++)
                height += Items[index].AddHeight(x, z);
            return height;
        }

        public static string Validate() => Validate(Evaluate);

        /// <summary>
        /// Measures either the analytic surface or the written native Terrain. The whole patch
        /// is checked at 1 m spacing; summits and footprint rims are checked more closely.
        /// </summary>
        public static string Validate(Func<float, float, float> sample)
        {
            if (sample == null)
                throw new ArgumentNullException(nameof(sample));
            const int side = 501;
            var heights = new float[side * side];
            var slopes = new float[heights.Length];
            var visited = new int[heights.Length];
            var queue = new int[heights.Length];
            float minimum = float.MaxValue;
            float maximum = float.MinValue;
            for (int z = 0; z < side; z++)
            {
                for (int x = 0; x < side; x++)
                {
                    float height = sample(x, z);
                    Require(!float.IsNaN(height) && !float.IsInfinity(height) && height >= 0f && height <= 350f,
                        "A sample exceeds the fixed 0..350 m height encoding.");
                    heights[z * side + x] = height;
                    minimum = Mathf.Min(minimum, height);
                    maximum = Mathf.Max(maximum, height);
                }
            }

            int[] below = new int[4];
            float[] limits = { 12f, 22f, 30f, 38f };
            for (int z = 0; z < side; z++)
            {
                for (int x = 0; x < side; x++)
                {
                    int left = Mathf.Max(x - 1, 0);
                    int right = Mathf.Min(x + 1, side - 1);
                    int bottom = Mathf.Max(z - 1, 0);
                    int top = Mathf.Min(z + 1, side - 1);
                    float dx = (heights[z * side + right] - heights[z * side + left]) / (right - left);
                    float dz = (heights[top * side + x] - heights[bottom * side + x]) / (top - bottom);
                    float slope = Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg;
                    slopes[z * side + x] = slope;
                    for (int limit = 0; limit < limits.Length; limit++)
                        if (slope <= limits[limit]) below[limit]++;
                }
            }
            Array.Sort(slopes);

            var report = new StringBuilder();
            report.AppendLine("500 x 500 m prairie sample; 4 continuous 250 m tiles; shared global function.");
            report.AppendLine("16 compact asymmetric hills: 8 small, 6 regular, 2 main; 4 loose groups and open crossing corridors.");
            report.AppendLine("Profiles: 2 bent short crests, 2 paired tops with real connecting saddles, 3 teardrops, 4 kidney forms, 5 asymmetric oval contrast hills.");
            report.AppendLine("Widths are full principal-axis foot-to-foot extents, with semiaxes = width / 2 and zero height beyond each ellipse.");
            report.AppendLine("Allowed widths after the requested 25% reduction: small 37.5..56.25 m; regular 52.5..75 m; main 75..105 m.");
            report.AppendLine(Format("Whole surface at 1 m spacing: min {0:F3} m; max {1:F3} m; range {2:F3} m.", minimum, maximum, maximum - minimum));
            report.AppendLine(Format("Slope: median {0:F2}, p90 {1:F2}, p95 {2:F2}, p99 {3:F2}, max {4:F2} degrees.",
                Percentile(slopes, 0.5f), Percentile(slopes, 0.9f), Percentile(slopes, 0.95f), Percentile(slopes, 0.99f), slopes[slopes.Length - 1]));
            for (int index = 0; index < limits.Length; index++)
                report.AppendLine(Format("Area at or below {0:F0} degrees: {1:F2}%.", limits[index], 100f * below[index] / heights.Length));
            if (slopes[slopes.Length - 1] > 38f)
                report.AppendLine("REVIEW: a sampled local slope exceeds the 38 degree design target.");

            float northSouth = CorridorMaximumSlope(sample, true, NorthSouthCorridorX);
            float eastWest = CorridorMaximumSlope(sample, false, EastWestCorridorZ);
            Require(northSouth <= 12f && eastWest <= 12f, "An open crossing corridor exceeds the 12 degree design limit.");
            report.AppendLine(Format("North-south corridor x={0:F0} m: max {1:F2} degrees; east-west corridor z={2:F0} m: max {3:F2} degrees.",
                NorthSouthCorridorX, northSouth, EastWestCorridorZ, eastWest));

            int generation = 0;
            int contourCount = 0;
            foreach (Feature feature in Items)
            {
                ValidateDimensions(feature);
                Vector2 summitPosition;
                float summit = FindSummit(sample, feature, out summitPosition);
                float rimMaximum = RimMaximum(sample, feature);
                float prominence = summit - rimMaximum;
                Require(prominence >= 7f, feature.Id + " lacks 7 m of conservative local prominence.");
                var featureContours = new StringBuilder();
                int localCount = 0;
                int firstLevel = Mathf.CeilToInt((rimMaximum + 0.3f) / ContourIntervalMeters);
                int lastLevel = Mathf.FloorToInt((summit - 0.3f) / ContourIntervalMeters);
                for (int level = firstLevel; level <= lastLevel; level++)
                {
                    float elevation = level * ContourIntervalMeters;
                    float width;
                    float depth;
                    int area = ClosedComponent(heights, side, summitPosition, elevation, visited, ++generation,
                        queue, out width, out depth);
                    Require(area >= 25 && Mathf.Min(width, depth) >= 6f,
                        feature.Id + " has an insufficiently readable closed 7 m contour.");
                    if (localCount > 0) featureContours.Append("; ");
                    featureContours.Append(Format("{0:F0} m: closed, {1:F0} m2, extent {2:F0} x {3:F0} m, peak clearance {4:F2} m",
                        elevation, area, width, depth, summit - elevation));
                    localCount++;
                }
                Require(localCount > 0, feature.Id + " does not enclose a clear absolute 7 m contour level.");
                contourCount += localCount;
                report.AppendLine(Format("{0} {1} ({2}): center ({3:F0},{4:F0}), height +{5:F1} m, full footprint {6:F1} x {7:F1} m, summit {8:F3} m, highest rim {9:F3} m, conservative prominence {10:F3} m; {11}.",
                    feature.Id + " " + feature.Profile, feature.Kind, feature.Group, feature.CenterMeters.x, feature.CenterMeters.y,
                    feature.HeightMeters, feature.ActualWidthMeters.x, feature.ActualWidthMeters.y, summit, rimMaximum, prominence, featureContours));
            }
            report.AppendLine(Format("PASS: all {0} hills have at least 7 m prominence and a readable closed absolute 7 m contour; {1} qualifying closed contours total.", Items.Length, contourCount));
            report.AppendLine("S05 crosses the x=250 m tile seam; R05 crosses z=250 m. All four outer borders are flat at the 80 m baseline.");
            report.AppendLine("The 1 m diagnostics complement, rather than replace, full native heightmap and identical shared-edge verification.");
            return report.ToString();
        }

        private static float FindSummit(Func<float, float, float> sample, Feature feature, out Vector2 position)
        {
            float maximum = float.MinValue;
            position = feature.CenterMeters;
            float searchRadius = Mathf.Max(feature.WidthMeters.x, feature.WidthMeters.y) * 0.48f;
            for (float dz = -searchRadius; dz <= searchRadius + 0.001f; dz += 0.5f)
            {
                for (float dx = -searchRadius; dx <= searchRadius + 0.001f; dx += 0.5f)
                {
                    Vector2 candidate = feature.CenterMeters + new Vector2(dx, dz);
                    float height = sample(candidate.x, candidate.y);
                    if (height <= maximum) continue;
                    maximum = height;
                    position = candidate;
                }
            }
            return maximum;
        }

        private static float RimMaximum(Func<float, float, float> sample, Feature feature)
        {
            float maximum = float.MinValue;
            for (int index = 0; index < 512; index++)
            {
                float angle = index * (Mathf.PI * 2f / 512f);
                float u = Mathf.Cos(angle) * feature.WidthMeters.x * 0.5f;
                float v = Mathf.Sin(angle) * feature.WidthMeters.y * 0.5f;
                float x = feature.CenterMeters.x + feature.Cos * u - feature.Sin * v;
                float z = feature.CenterMeters.y + feature.Sin * u + feature.Cos * v;
                maximum = Mathf.Max(maximum, sample(x, z));
            }
            return maximum;
        }

        private static int ClosedComponent(float[] heights, int side, Vector2 summit, float level,
            int[] visited, int generation, int[] queue, out float width, out float depth)
        {
            int startX = Mathf.RoundToInt(summit.x);
            int startZ = Mathf.RoundToInt(summit.y);
            int start = startZ * side + startX;
            Require(heights[start] > level, "A contour summit falls below its level on the 1 m verification grid.");
            int head = 0;
            int tail = 1;
            int minX = startX;
            int maxX = startX;
            int minZ = startZ;
            int maxZ = startZ;
            queue[0] = start;
            visited[start] = generation;
            while (head < tail)
            {
                int current = queue[head++];
                int x = current % side;
                int z = current / side;
                Require(x > 0 && x < side - 1 && z > 0 && z < side - 1,
                    "A supposedly closed contour reaches the sample boundary.");
                minX = Mathf.Min(minX, x);
                maxX = Mathf.Max(maxX, x);
                minZ = Mathf.Min(minZ, z);
                maxZ = Mathf.Max(maxZ, z);
                EnqueueAbove(heights, visited, generation, queue, ref tail, current - 1, level);
                EnqueueAbove(heights, visited, generation, queue, ref tail, current + 1, level);
                EnqueueAbove(heights, visited, generation, queue, ref tail, current - side, level);
                EnqueueAbove(heights, visited, generation, queue, ref tail, current + side, level);
            }
            width = maxX - minX + 1;
            depth = maxZ - minZ + 1;
            return tail;
        }

        private static void EnqueueAbove(float[] heights, int[] visited, int generation,
            int[] queue, ref int tail, int index, float level)
        {
            if (visited[index] == generation || heights[index] <= level) return;
            visited[index] = generation;
            queue[tail++] = index;
        }

        private static float CorridorMaximumSlope(Func<float, float, float> sample, bool northSouth, float crossCoordinate)
        {
            float maximum = 0f;
            for (float coordinate = 0.5f; coordinate <= SizeMeters - 0.5f; coordinate += 0.5f)
            {
                float x = northSouth ? crossCoordinate : coordinate;
                float z = northSouth ? coordinate : crossCoordinate;
                float dx = (sample(x + 0.25f, z) - sample(x - 0.25f, z)) * 2f;
                float dz = (sample(x, z + 0.25f) - sample(x, z - 0.25f)) * 2f;
                maximum = Mathf.Max(maximum, Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg);
            }
            return maximum;
        }

        private static void ValidateDimensions(Feature feature)
        {
            float minimumWidth = feature.Kind == "Small" ? 37.5f : feature.Kind == "Regular" ? 52.5f : 75f;
            float maximumWidth = feature.Kind == "Small" ? 56.25f : feature.Kind == "Regular" ? 75f : 105f;
            float minimumHeight = feature.Kind == "Small" ? 8f : feature.Kind == "Regular" ? 10f : 14f;
            float maximumHeight = feature.Kind == "Small" ? 11f : feature.Kind == "Regular" ? 14f : 21f;
            Require(feature.WidthMeters.x >= minimumWidth && feature.WidthMeters.x <= maximumWidth
                && feature.WidthMeters.y >= minimumWidth && feature.WidthMeters.y <= maximumWidth
                && feature.ActualWidthMeters.x >= minimumWidth && feature.ActualWidthMeters.x <= maximumWidth
                && feature.ActualWidthMeters.y >= minimumWidth && feature.ActualWidthMeters.y <= maximumWidth
                && feature.HeightMeters >= minimumHeight && feature.HeightMeters <= maximumHeight,
                feature.Id + " exceeds the approved hill dimensions.");
            float radius = Mathf.Max(feature.WidthMeters.x, feature.WidthMeters.y) * 0.5f;
            Require(feature.CenterMeters.x - radius > 0f && feature.CenterMeters.y - radius > 0f
                && feature.CenterMeters.x + radius < SizeMeters && feature.CenterMeters.y + radius < SizeMeters,
                feature.Id + " extends outside the sample.");
        }

        private static float Percentile(float[] sorted, float fraction) => sorted[Mathf.RoundToInt((sorted.Length - 1) * fraction)];
        private static string Format(string format, params object[] values) => string.Format(CultureInfo.InvariantCulture, format, values);
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
