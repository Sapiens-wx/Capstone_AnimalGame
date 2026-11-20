using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AnimalGame.MapTest;
using UnityEngine;

namespace AnimalGame.Editor
{
    /// <summary>
    /// A deterministic, metre-based grassland surface. One global cache is shared by every
    /// Terrain tile, so tile boundaries sample the same surface rather than independent noise.
    /// Small hills are placed only where their completed surface encloses a 7 m contour.
    /// </summary>
    public sealed class OfficialPlainsRoughHeightField
    {
        public sealed class Feature
        {
            public string Kind { get; internal set; }
            public Vector2 Center { get; internal set; }
            public float Height { get; internal set; }
            public float RadiusX { get; internal set; }
            public float RadiusZ { get; internal set; }
            public float RotationDegrees { get; internal set; }
            public float SummitHeight { get; internal set; }
            public float BoundaryMaximumHeight { get; internal set; }
            public float VerifiedProminence => SummitHeight - BoundaryMaximumHeight;
            public int EnclosedSevenMeterLevels { get; internal set; }
            internal double Cos;
            internal double Sin;
        }

        public const float BaseHeightMeters = 80f;
        public const float CellSizeMeters = 1f;
        private const double TwoPi = Math.PI * 2;
        private readonly OfficialMapRegion plains;
        private readonly Rect regionBounds;
        private readonly Rect bounds;
        private readonly List<Feature> features = new List<Feature>();
        private float[] heights;
        private int columns;
        private int rows;
        private uint randomState = 0xB1705A31;
        private bool built;
        private readonly int[] slopeBins = new int[6];

        public IReadOnlyList<Feature> Features => features;
        public Rect GridBounds => bounds;
        public string Report { get; private set; } = "Not built.";
        public float MinimumHeightMeters { get; private set; }
        public float MaximumHeightMeters { get; private set; }
        public float MaximumSlopeDegrees { get; private set; }
        public float Slope95Degrees { get; private set; }
        public int VerifiedSmallHillCount { get; private set; }

        public OfficialPlainsRoughHeightField(OfficialMapRegion plainsRegion)
        {
            plains = plainsRegion ?? throw new ArgumentNullException(nameof(plainsRegion));
            if (plains.PointsMeters == null || plains.PointsMeters.Length < 3)
                throw new ArgumentException("The plains outline is missing.", nameof(plainsRegion));
            regionBounds = plains.BoundsMeters;
            bounds = Rect.MinMaxRect(
                (float)(Math.Floor(regionBounds.xMin / 250d) * 250d),
                (float)(Math.Floor(regionBounds.yMin / 250d) * 250d),
                (float)(Math.Ceiling(regionBounds.xMax / 250d) * 250d),
                (float)(Math.Ceiling(regionBounds.yMax / 250d) * 250d));
        }

        public void Build(Action<float, string> progress = null)
        {
            if (built)
                return;
            columns = (int)Math.Round(bounds.width / CellSizeMeters) + 1;
            rows = (int)Math.Round(bounds.height / CellSizeMeters) + 1;
            heights = new float[columns * rows];
            for (int row = 0; row < rows; row++)
            {
                double z = bounds.yMin + row * CellSizeMeters;
                for (int column = 0; column < columns; column++)
                    heights[row * columns + column] = (float)BroadSurface(bounds.xMin + column * CellSizeMeters, z);
                if ((row & 63) == 0)
                    progress?.Invoke(0.30f * row / rows, "Building the continuous broad grassland surface");
            }

            // Long, offset shoulders enrich the northern skyline. Shallow basins keep the
            // central open land readable. They are terrain forms, not carved watercourses.
            AddLandform("Long hill", 0.34, 0.84, 25, 265, 155, 28);
            AddLandform("Long hill", 0.59, 0.76, 27, 285, 165, -23);
            AddLandform("Long hill", 0.77, 0.64, 23, 230, 150, 32);
            AddLandform("Long hill", 0.27, 0.61, 22, 255, 145, -14);
            AddLandform("Long hill", 0.56, 0.48, 20, 245, 165, 38);
            AddLandform("Long hill", 0.75, 0.36, 22, 235, 155, -24);
            AddLandform("Long hill", 0.45, 0.20, 21, 225, 145, 16);
            AddLandform("Long hill", 0.65, 0.09, 19, 205, 140, -27);
            AddLandform("Shallow basin", 0.45, 0.65, -15, 300, 245, 16);
            AddLandform("Shallow basin", 0.50, 0.35, -17, 300, 230, -18);
            AddLandform("Shallow basin", 0.55, 0.12, -12, 230, 195, 23);
            progress?.Invoke(0.40f, "Placing separated, contour-readable small hills");

            PlaceSmallHills();
            progress?.Invoke(0.75f, "Verifying actual hill prominence and 7 m contour closure");
            VerifySmallHills();
            MeasureSurface(progress);
            built = true;
            Report = MakeReport();
            progress?.Invoke(1f, "Continuous grassland height field ready");
        }

        /// <summary>World-plan X/Z metres. The cache covers the complete aligned tile envelope.</summary>
        public float Sample(float x, float z)
        {
            if (!built)
                throw new InvalidOperationException("Build the plains height field before sampling it.");
            if (x < bounds.xMin - 0.01f || x > bounds.xMax + 0.01f
                || z < bounds.yMin - 0.01f || z > bounds.yMax + 0.01f)
                throw new ArgumentOutOfRangeException(nameof(x), "The sample is outside the shared plains cache.");
            return SampleCache(x, z);
        }

        /// <summary>Rechecks 7k contour closure using the final written Terrain surface.</summary>
        public string ValidateContourFeatures(Func<float, float, float> sample, float interval = 7f)
        {
            if (!built)
                throw new InvalidOperationException("Build the field before checking final Terrain contours.");
            if (sample == null || interval <= 0 || float.IsNaN(interval) || float.IsInfinity(interval))
                throw new ArgumentException("A sampler and positive contour interval are required.");
            var report = new StringBuilder();
            int count = 0;
            foreach (Feature feature in features)
            {
                if (feature.Kind != "Small hill")
                    continue;
                double summit = double.MinValue;
                for (double dz = -feature.RadiusZ * 0.6; dz <= feature.RadiusZ * 0.6; dz += 2)
                    for (double dx = -feature.RadiusX * 0.6; dx <= feature.RadiusX * 0.6; dx += 2)
                        summit = Math.Max(summit, sample(feature.Center.x + (float)dx, feature.Center.y + (float)dz));
                double boundary = double.MinValue;
                for (int index = 0; index < 128; index++)
                {
                    double angle = index * TwoPi / 128;
                    double localX = Math.Cos(angle) * feature.RadiusX;
                    double localZ = Math.Sin(angle) * feature.RadiusZ;
                    float x = (float)(feature.Center.x + feature.Cos * localX - feature.Sin * localZ);
                    float z = (float)(feature.Center.y + feature.Sin * localX + feature.Cos * localZ);
                    boundary = Math.Max(boundary, sample(x, z));
                }
                int firstLevel = (int)Math.Ceiling((boundary + 0.3) / interval);
                int lastLevel = (int)Math.Floor((summit - 0.3) / interval);
                if (summit - boundary < 8 || firstLevel > lastLevel)
                    throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                        "Written small hill at ({0:F1},{1:F1}) lost prominence or closed contour: summit={2:F3}, ring={3:F3}.",
                        feature.Center.x, feature.Center.y, summit, boundary));
                count++;
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Small hill ({0:F2}, {1:F2}): summit {2:F3} m, boundary max {3:F3} m, prominence {4:F3} m, closed levels {5:F1}..{6:F1} m.",
                    feature.Center.x, feature.Center.y, summit, boundary, summit - boundary, firstLevel * interval, lastLevel * interval));
            }
            report.Insert(0, "PASS: " + count + " written small hills retain at least one closed absolute-height contour with 0.3 m clearance.\n");
            return report.ToString();
        }

        private double BroadSurface(double x, double z)
        {
            double u = x - regionBounds.center.x;
            double v = z - regionBounds.center.y;
            double warpedU = u + 62 * Math.Sin(v / 490) + 28 * Math.Sin((u + v) / 670);
            double warpedV = v + 70 * Math.Sin(u / 610);
            return BaseHeightMeters
                + 11 * Math.Sin(TwoPi * (0.83 * warpedU + 0.55 * warpedV) / 1100 + 0.8)
                + 7 * Math.Sin(TwoPi * (-0.35 * warpedU + 0.94 * warpedV) / 790 + 1.2)
                + 6 * Math.Sin(TwoPi * (0.94 * warpedU - 0.34 * warpedV) / 1550 - 1.5);
        }

        private void AddLandform(string kind, double normalizedX, double normalizedZ,
            double amplitude, double radiusX, double radiusZ, double degrees)
        {
            Rect region = regionBounds;
            Feature feature = MakeFeature(kind,
                region.xMin + normalizedX * region.width,
                region.yMin + normalizedZ * region.height,
                amplitude, radiusX, radiusZ, degrees);
            features.Add(feature);
            Deposit(feature);
        }

        private void PlaceSmallHills()
        {
            Rect region = regionBounds;
            var smallHills = new List<Feature>();
            // Random rejection, rather than a jittered rectangular lattice, avoids an egg-box
            // layout. The southern half has denser hills; central sightlines stay more open.
            for (int attempt = 0; attempt < 8000 && smallHills.Count < 48; attempt++)
            {
                double nx = Next01();
                double nz = Next01();
                double density = nz < 0.37 ? 1 : (nz < 0.65 ? 0.56 : 0.78);
                if (Next01() > density)
                    continue;
                double x = region.xMin + nx * region.width;
                double z = region.yMin + nz * region.height;
                double radiusX = 90 + 22 * Next01();
                double radiusZ = 80 + 20 * Next01();
                double degrees = -45 + 100 * Next01();
                var candidate = MakeFeature("Small hill", x, z, 0, radiusX, radiusZ, degrees);
                if (!Inside(x, z) || !RingInside(candidate))
                    continue;
                bool overlaps = false;
                foreach (Feature previous in smallHills)
                {
                    double dx = previous.Center.x - x;
                    double dz = previous.Center.y - z;
                    double minimumDistance = Math.Max(previous.RadiusX, previous.RadiusZ)
                        + Math.Max(radiusX, radiusZ) + 12;
                    if (dx * dx + dz * dz < minimumDistance * minimumDistance)
                    {
                        overlaps = true;
                        break;
                    }
                }
                if (overlaps)
                    continue;
                double centerHeight = SampleCache(x, z);
                double ringMaximum = RingMaximum(candidate);
                // A > 7 m summit-to-entire-boundary gap guarantees at least one closed
                // 7 m level, independent of the absolute elevation's phase relative to 7.
                double amplitude = Math.Max(11 + 2 * Next01(), ringMaximum - centerHeight + 9.2);
                if (amplitude > 17.5)
                    continue;
                candidate.Height = (float)amplitude;
                if (CandidateSlopeTooSteep(candidate))
                    continue;
                features.Add(candidate);
                smallHills.Add(candidate);
                Deposit(candidate);
            }
            if (smallHills.Count < 24)
                throw new InvalidOperationException("Too few contour-readable grassland small hills were placed.");
        }

        private void VerifySmallHills()
        {
            VerifiedSmallHillCount = 0;
            foreach (Feature feature in features)
            {
                if (feature.Kind != "Small hill")
                    continue;
                double summit = double.MinValue;
                for (double dz = -feature.RadiusZ * 0.60; dz <= feature.RadiusZ * 0.60; dz += 2)
                    for (double dx = -feature.RadiusX * 0.60; dx <= feature.RadiusX * 0.60; dx += 2)
                        summit = Math.Max(summit, SampleCache(feature.Center.x + dx, feature.Center.y + dz));
                double boundary = RingMaximum(feature);
                feature.SummitHeight = (float)summit;
                feature.BoundaryMaximumHeight = (float)boundary;
                feature.EnclosedSevenMeterLevels = Math.Max(0,
                    (int)Math.Floor((summit - 0.05) / 7) - (int)Math.Ceiling((boundary + 0.05) / 7) + 1);
                if (feature.VerifiedProminence < 8.5 || feature.EnclosedSevenMeterLevels < 1)
                    throw new InvalidOperationException("A small hill did not retain its required enclosed 7 m contour.");
                VerifiedSmallHillCount++;
            }
        }

        private double RingMaximum(Feature feature)
        {
            double maximum = double.MinValue;
            for (int index = 0; index < 128; index++)
            {
                double angle = index * TwoPi / 128;
                double localX = Math.Cos(angle) * feature.RadiusX;
                double localZ = Math.Sin(angle) * feature.RadiusZ;
                double x = feature.Center.x + feature.Cos * localX - feature.Sin * localZ;
                double z = feature.Center.y + feature.Sin * localX + feature.Cos * localZ;
                maximum = Math.Max(maximum, SampleCache(x, z));
            }
            return maximum;
        }

        private bool RingInside(Feature feature)
        {
            for (int index = 0; index < 16; index++)
            {
                double angle = index * TwoPi / 16;
                double localX = Math.Cos(angle) * feature.RadiusX;
                double localZ = Math.Sin(angle) * feature.RadiusZ;
                double x = feature.Center.x + feature.Cos * localX - feature.Sin * localZ;
                double z = feature.Center.y + feature.Sin * localX + feature.Cos * localZ;
                if (!Inside(x, z))
                    return false;
            }
            return true;
        }

        private bool CandidateSlopeTooSteep(Feature feature)
        {
            double maximumGradient = Math.Tan(27 * Math.PI / 180);
            double radius = Math.Max(feature.RadiusX, feature.RadiusZ);
            for (double dz = -radius; dz <= radius; dz += 6)
            {
                for (double dx = -radius; dx <= radius; dx += 6)
                {
                    double localX = (feature.Cos * dx + feature.Sin * dz) / feature.RadiusX;
                    double localZ = (-feature.Sin * dx + feature.Cos * dz) / feature.RadiusZ;
                    double squaredRadius = localX * localX + localZ * localZ;
                    if (squaredRadius >= 1)
                        continue;
                    double x = feature.Center.x + dx;
                    double z = feature.Center.y + dz;
                    double shoulder = 1 - squaredRadius;
                    double derivative = -6 * feature.Height * shoulder * shoulder;
                    double gradientX = (SampleCache(x + 1, z) - SampleCache(x - 1, z)) / 2
                        + derivative * (localX * feature.Cos / feature.RadiusX - localZ * feature.Sin / feature.RadiusZ);
                    double gradientZ = (SampleCache(x, z + 1) - SampleCache(x, z - 1)) / 2
                        + derivative * (localX * feature.Sin / feature.RadiusX + localZ * feature.Cos / feature.RadiusZ);
                    if (gradientX * gradientX + gradientZ * gradientZ > maximumGradient * maximumGradient)
                        return true;
                }
            }
            return false;
        }

        private static Feature MakeFeature(string kind, double x, double z, double height,
            double radiusX, double radiusZ, double degrees)
        {
            double angle = degrees * Math.PI / 180;
            return new Feature
            {
                Kind = kind,
                Center = new Vector2((float)x, (float)z),
                Height = (float)height,
                RadiusX = (float)radiusX,
                RadiusZ = (float)radiusZ,
                RotationDegrees = (float)degrees,
                Cos = Math.Cos(angle),
                Sin = Math.Sin(angle)
            };
        }

        private void Deposit(Feature feature)
        {
            double radius = Math.Max(feature.RadiusX, feature.RadiusZ);
            int minColumn = Math.Max(0, (int)Math.Floor((feature.Center.x - radius - bounds.xMin) / CellSizeMeters));
            int maxColumn = Math.Min(columns - 1, (int)Math.Ceiling((feature.Center.x + radius - bounds.xMin) / CellSizeMeters));
            int minRow = Math.Max(0, (int)Math.Floor((feature.Center.y - radius - bounds.yMin) / CellSizeMeters));
            int maxRow = Math.Min(rows - 1, (int)Math.Ceiling((feature.Center.y + radius - bounds.yMin) / CellSizeMeters));
            for (int row = minRow; row <= maxRow; row++)
            {
                double dz = bounds.yMin + row * CellSizeMeters - feature.Center.y;
                for (int column = minColumn; column <= maxColumn; column++)
                {
                    double dx = bounds.xMin + column * CellSizeMeters - feature.Center.x;
                    double localX = (feature.Cos * dx + feature.Sin * dz) / feature.RadiusX;
                    double localZ = (-feature.Sin * dx + feature.Cos * dz) / feature.RadiusZ;
                    double squaredRadius = localX * localX + localZ * localZ;
                    if (squaredRadius >= 1)
                        continue;
                    double shoulder = 1 - squaredRadius;
                    // Compact C2 support: height, slope and curvature join the surrounding
                    // surface continuously, with a rounded summit and no crater-like rim.
                    heights[row * columns + column] += (float)(feature.Height * shoulder * shoulder * shoulder);
                }
            }
        }

        private float SampleCache(double x, double z)
        {
            double gx = Math.Max(0, Math.Min(columns - 1, (x - bounds.xMin) / CellSizeMeters));
            double gz = Math.Max(0, Math.Min(rows - 1, (z - bounds.yMin) / CellSizeMeters));
            int ix = Math.Min(columns - 2, (int)gx);
            int iz = Math.Min(rows - 2, (int)gz);
            float tx = (float)(gx - ix);
            float tz = (float)(gz - iz);
            int offset = iz * columns + ix;
            float a = heights[offset] + (heights[offset + 1] - heights[offset]) * tx;
            float b = heights[offset + columns] + (heights[offset + columns + 1] - heights[offset + columns]) * tx;
            return a + (b - a) * tz;
        }

        private void MeasureSurface(Action<float, string> progress)
        {
            double minimum = double.MaxValue;
            double maximum = double.MinValue;
            var slopes = new List<float>();
            float maxSlope = 0;
            for (int row = 1; row < rows - 1; row += 2)
            {
                double z = bounds.yMin + row;
                for (int column = 1; column < columns - 1; column += 2)
                {
                    double x = bounds.xMin + column;
                    if (!Inside(x, z))
                        continue;
                    int offset = row * columns + column;
                    double h = heights[offset];
                    minimum = Math.Min(minimum, h);
                    maximum = Math.Max(maximum, h);
                    double dx = (heights[offset + 1] - heights[offset - 1]) / 2d;
                    double dz = (heights[offset + columns] - heights[offset - columns]) / 2d;
                    float slope = (float)(Math.Atan(Math.Sqrt(dx * dx + dz * dz)) * 180 / Math.PI);
                    slopes.Add(slope);
                    maxSlope = Math.Max(maxSlope, slope);
                    int bin = slope < 3 ? 0 : (slope < 12 ? 1 : (slope < 22 ? 2 : (slope < 28 ? 3 : (slope < 29 ? 4 : 5))));
                    slopeBins[bin]++;
                }
                if ((row & 63) == 1)
                    progress?.Invoke(0.75f + 0.24f * row / rows, "Measuring completed grassland heights and slopes");
            }
            slopes.Sort();
            MinimumHeightMeters = (float)minimum;
            MaximumHeightMeters = (float)maximum;
            MaximumSlopeDegrees = maxSlope;
            Slope95Degrees = slopes[(int)(slopes.Count * 0.95)];
            if (minimum < 35 || maximum > 165 || maxSlope > 29)
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                    "Grassland height/slope limits exceeded: {0:F2}..{1:F2} m, maximum {2:F2} degrees.", minimum, maximum, maxSlope));
        }

        private bool Inside(double x, double z)
        {
            bool inside = false;
            Vector2 previous = plains.PointsMeters[plains.PointsMeters.Length - 1];
            foreach (Vector2 current in plains.PointsMeters)
            {
                if ((current.y > z) != (previous.y > z)
                    && x < (previous.x - current.x) * (z - current.y) / (previous.y - current.y) + current.x)
                    inside = !inside;
                previous = current;
            }
            return inside;
        }

        private double Next01()
        {
            randomState ^= randomState << 13;
            randomState ^= randomState >> 17;
            randomState ^= randomState << 5;
            return randomState / ((double)uint.MaxValue + 1);
        }

        private string MakeReport()
        {
            var report = new StringBuilder();
            report.AppendLine("Official Plains rough height field V1");
            report.AppendLine("World metre coordinates; baseline Y=80 m; shared 1 m cache; no clipping or height normalization.");
            report.AppendLine("Broad wavelengths 790 / 1100 / 1550 m, amplitudes 7 / 11 / 6 m; smooth directional warping.");
            report.AppendLine("Eight offset elongated hills, three broad shallow basins; no rivers, foothills or fine noise.");
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Cache extent X={0:F1}..{1:F1}, Z={2:F1}..{3:F1}; {4} x {5} samples at 1 m spacing.",
                bounds.xMin, bounds.xMax, bounds.yMin, bounds.yMax, columns, rows));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Grassland samples: height {0:F3}..{1:F3} m; slope P95={2:F3} degrees, max={3:F3} degrees.",
                MinimumHeightMeters, MaximumHeightMeters, Slope95Degrees, MaximumSlopeDegrees));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "2 m-spaced in-polygon slope sample counts: [0,3)={0}, [3,12)={1}, [12,22)={2}, [22,28)={3}, [28,29)={4}, >=29={5}.",
                slopeBins[0], slopeBins[1], slopeBins[2], slopeBins[3], slopeBins[4], slopeBins[5]));
            report.AppendLine("Verified small hills: " + VerifiedSmallHillCount + "; each summit exceeds its entire enclosing support ring by at least 8.5 m.");
            report.AppendLine("Small hill verification uses completed combined heights, 128 boundary samples and 2 m summit search.");
            report.AppendLine("kind, centerX, centerZ, amplitudeM, radiusX, radiusZ, angleDegrees, summitM, maximumRingM, prominenceM, enclosed7mLevels");
            foreach (Feature feature in features)
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0}, {1:F3}, {2:F3}, {3:F3}, {4:F3}, {5:F3}, {6:F3}, {7:F3}, {8:F3}, {9:F3}, {10}",
                    feature.Kind, feature.Center.x, feature.Center.y, feature.Height, feature.RadiusX,
                    feature.RadiusZ, feature.RotationDegrees, feature.SummitHeight,
                    feature.BoundaryMaximumHeight, feature.VerifiedProminence, feature.EnclosedSevenMeterLevels));
            return report.ToString();
        }
    }
}
