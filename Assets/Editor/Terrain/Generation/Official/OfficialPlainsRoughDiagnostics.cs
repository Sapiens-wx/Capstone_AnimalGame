using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AnimalGame.MapTest;
using UnityEngine;

namespace AnimalGame.Editor
{
    /// <summary>Reads authored native TerrainData. The PNGs are diagnostic views, never height-map exports.</summary>
    public static class OfficialPlainsRoughDiagnostics
    {
        private const float BoundaryTolerance = 0.01f;

        public static string Validate(Terrain[] terrains, OfficialMapPlan plan, string outputFolder)
        {
            Require(plan != null && plan.HasMetricTransform(out _), "A metric Official Map Plan is required.");
            OfficialMapRegion plains = plan.FindRegion("plains");
            Require(plains != null && plains.PointsMeters.Length >= 3, "The plains outline is missing.");
            var sampler = new NativeSampler(terrains, plan);
            Directory.CreateDirectory(outputFolder);
            var report = new StringBuilder();
            report.AppendLine("Official plains native TerrainData diagnostics");
            report.AppendLine("Heights are metres above the plan origin. Contours are 0 + k * 7 m, independent of the player.");
            report.AppendLine("PNGs are shaded diagnostic views; they are not physical height maps or runtime prebakes.");
            report.AppendLine("Terrain count: " + sampler.Tiles.Count);
            report.AppendFormat(CultureInfo.InvariantCulture, "Native coverage bounds: X {0:F3}..{1:F3}, Z {2:F3}..{3:F3} m\n",
                sampler.Bounds.xMin, sampler.Bounds.xMax, sampler.Bounds.yMin, sampler.Bounds.yMax);

            int seams = 0, seamSamples = 0;
            float maxLocalSeam = 0f, maxWorldSeam = 0f;
            foreach (Tile tile in sampler.Tiles)
            {
                foreach (Tile neighbour in sampler.Tiles)
                {
                    bool right = Near(tile.Bounds.xMax, neighbour.Bounds.xMin)
                        && Near(tile.Bounds.yMin, neighbour.Bounds.yMin) && Near(tile.Bounds.yMax, neighbour.Bounds.yMax);
                    bool top = Near(tile.Bounds.yMax, neighbour.Bounds.yMin)
                        && Near(tile.Bounds.xMin, neighbour.Bounds.xMin) && Near(tile.Bounds.xMax, neighbour.Bounds.xMax);
                    if (!right && !top) continue;
                    int resolution = tile.Data.heightmapResolution;
                    Require(resolution == neighbour.Data.heightmapResolution, "Adjacent height-map resolutions differ.");
                    float[,] a = right ? tile.Data.GetHeights(resolution - 1, 0, 1, resolution)
                        : tile.Data.GetHeights(0, resolution - 1, resolution, 1);
                    float[,] b = right ? neighbour.Data.GetHeights(0, 0, 1, resolution)
                        : neighbour.Data.GetHeights(0, 0, resolution, 1);
                    for (int index = 0; index < resolution; index++)
                    {
                        float ha = (right ? a[index, 0] : a[0, index]) * tile.Size.y;
                        float hb = (right ? b[index, 0] : b[0, index]) * neighbour.Size.y;
                        Require(Finite(ha) && Finite(hb), "A shared edge contains a non-finite height.");
                        maxLocalSeam = Mathf.Max(maxLocalSeam, Mathf.Abs(ha - hb));
                        maxWorldSeam = Mathf.Max(maxWorldSeam, Mathf.Abs(ha + tile.BaseHeight - hb - neighbour.BaseHeight));
                    }
                    seams++;
                    seamSamples += resolution;
                }
            }
            report.AppendFormat(CultureInfo.InvariantCulture,
                "Shared edges: {0}; samples checked: {1}; maximum local/world error: {2:F6}/{3:F6} m\n",
                seams, seamSamples, maxLocalSeam, maxWorldSeam);
            Require(maxLocalSeam <= BoundaryTolerance && maxWorldSeam <= BoundaryTolerance,
                "Native terrain seam error exceeds 0.01 m.");

            Raster whole = sampler.Read(sampler.Bounds, 2f, true);
            float minimum = float.PositiveInfinity, maximum = float.NegativeInfinity;
            float coverageMinimum = float.PositiveInfinity, coverageMaximum = float.NegativeInfinity, maxSlope = 0f;
            int covered = 0, grass = 0;
            var bins = new int[5];
            for (int z = 0; z < whole.Height; z++)
            {
                for (int x = 0; x < whole.Width; x++)
                {
                    int index = z * whole.Width + x;
                    float height = whole.Heights[index];
                    if (float.IsNaN(height)) continue;
                    Require(Finite(height) && height >= -0.02f && height <= 350.02f,
                        "Sampled terrain height is non-finite or outside 0..350 m.");
                    covered++;
                    coverageMinimum = Mathf.Min(coverageMinimum, height);
                    coverageMaximum = Mathf.Max(coverageMaximum, height);
                    if (!Inside(plains.PointsMeters, whole.X(x), whole.Z(z))) continue;
                    float slope = whole.Slopes[index];
                    Require(Finite(slope) && slope >= 0f && slope <= 90.01f, "Invalid native GetSteepness sample.");
                    grass++;
                    minimum = Mathf.Min(minimum, height);
                    maximum = Mathf.Max(maximum, height);
                    maxSlope = Mathf.Max(maxSlope, slope);
                    bins[slope <= 3f ? 0 : slope <= 12f ? 1 : slope <= 22f ? 2 : slope <= 28f ? 3 : 4]++;
                }
            }
            Require(grass > 0, "No native grassland height samples were found.");
            report.AppendFormat(CultureInfo.InvariantCulture,
                "2 m native grid: {0} coverage samples, {1} inside grassland polygon.\nCoverage height: {2:F3}..{3:F3} m.\nGrassland height: {4:F3}..{5:F3} m; relief {6:F3} m.\n",
                covered, grass, coverageMinimum, coverageMaximum, minimum, maximum, maximum - minimum);
            report.AppendLine("Slope bins use native TerrainData.GetSteepness at the same 2 m grid positions inside the polygon:");
            string[] names = { "0..3 degrees", ">3..12 degrees", ">12..22 degrees", ">22..28 degrees", ">28 degrees" };
            for (int index = 0; index < bins.Length; index++)
                report.AppendFormat(CultureInfo.InvariantCulture, "  {0}: {1} ({2:F2}%)\n", names[index], bins[index], bins[index] * 100d / grass);
            report.AppendFormat(CultureInfo.InvariantCulture, "Maximum sampled native slope: {0:F3} degrees.\n", maxSlope);
            if (maxSlope > 35f) report.AppendLine("REVIEW: a sampled slope exceeds 35 degrees; inspect its location before accepting traversal.");

            Draw(whole, plains.PointsMeters, minimum, maximum, Path.Combine(outputFolder, "Topo7m.png"));
            Vector2 centre = plains.LabelPositionMeters;
            Rect detailBounds = new Rect(centre.x - 250f, centre.y - 250f, 500f, 500f);
            Raster detail = sampler.Read(detailBounds, 1f, false);
            Draw(detail, plains.PointsMeters, minimum, maximum, Path.Combine(outputFolder, "Topo7m_Detail.png"));
            report.AppendFormat(CultureInfo.InvariantCulture,
                "Topo7m.png: {0} x {1} samples, 2 m per pixel; darker pixels are editing buffer outside the region.\n", whole.Width, whole.Height);
            report.AppendFormat(CultureInfo.InvariantCulture,
                "Topo7m_Detail.png: X {0:F3}..{1:F3}, Z {2:F3}..{3:F3} m, 1 m per pixel.\n", detailBounds.xMin, detailBounds.xMax, detailBounds.yMin, detailBounds.yMax);
            report.AppendLine("Black lines mark sampled 7 m contour crossings; faint white lines show the 250 m grid. PNG top is +Z.");
            bool enoughRelief = maximum - minimum > 35f;
            report.AppendLine(enoughRelief ? "PASS: native range, seams and sampled relief." : "FAIL: sampled grassland relief must exceed 35 m.");
            string result = report.ToString();
            File.WriteAllText(Path.Combine(outputFolder, "NativeValidation.txt"), result, new UTF8Encoding(false));
            Require(enoughRelief, "Sampled grassland total relief must exceed 35 m; see NativeValidation.txt.");
            return result;
        }

        /// <summary>Samples the actual native surface at plan-local X/Z, including its transform's Y offset.</summary>
        public static float SampleHeight(Terrain[] terrains, OfficialMapPlan plan, float x, float z)
        {
            Require(plan != null && terrains != null, "Terrain array and plan are required.");
            Vector3 origin = plan.transform.position;
            foreach (Terrain terrain in terrains)
            {
                if (terrain == null || terrain.terrainData == null) continue;
                Vector3 position = terrain.transform.position - origin;
                Vector3 size = terrain.terrainData.size;
                float u = (x - position.x) / size.x, v = (z - position.z) / size.z;
                if (u < -BoundaryTolerance / size.x || u > 1f + BoundaryTolerance / size.x
                    || v < -BoundaryTolerance / size.z || v > 1f + BoundaryTolerance / size.z) continue;
                return position.y + terrain.terrainData.GetInterpolatedHeight(Mathf.Clamp01(u), Mathf.Clamp01(v));
            }
            throw new InvalidOperationException($"No native terrain covers plan coordinate ({x:F3}, {z:F3}).");
        }

        private static void Draw(Raster raster, Vector2[] polygon, float minimum, float maximum, string path)
        {
            var pixels = new Color32[raster.Heights.Length];
            Color low = new Color(0.46f, 0.53f, 0.28f), high = new Color(0.78f, 0.76f, 0.45f);
            for (int z = 0; z < raster.Height; z++)
            {
                for (int x = 0; x < raster.Width; x++)
                {
                    int index = z * raster.Width + x;
                    float height = raster.Heights[index];
                    if (!Finite(height)) { pixels[index] = new Color32(105, 105, 105, 0); continue; }
                    float left = raster.ValidHeight(x - 1, z, height), right = raster.ValidHeight(x + 1, z, height);
                    float down = raster.ValidHeight(x, z - 1, height), up = raster.ValidHeight(x, z + 1, height);
                    Vector3 normal = new Vector3((left - right) / (2f * raster.Step), 1f,
                        (down - up) / (2f * raster.Step)).normalized;
                    float shade = 0.62f + 0.43f * Mathf.Max(0f, Vector3.Dot(normal, new Vector3(-0.5f, 0.8f, 0.4f).normalized));
                    Color color = Color.Lerp(low, high, Mathf.InverseLerp(minimum, maximum, height)) * shade;
                    if (!Inside(polygon, raster.X(x), raster.Z(z))) color *= 0.53f;
                    bool contour = x > 0 && Finite(left) && Mathf.FloorToInt(left / 7f) != Mathf.FloorToInt(height / 7f)
                        || z > 0 && Finite(down) && Mathf.FloorToInt(down / 7f) != Mathf.FloorToInt(height / 7f);
                    if (contour) color = new Color(0.07f, 0.085f, 0.045f, 1f);
                    else if (GridDistance(raster.X(x)) < raster.Step * 0.45f || GridDistance(raster.Z(z)) < raster.Step * 0.45f)
                        color = Color.Lerp(color, Color.white, 0.16f);
                    color.a = 1f;
                    pixels[index] = color;
                }
            }
            Texture2D texture = null;
            try
            {
                texture = new Texture2D(raster.Width, raster.Height, TextureFormat.RGBA32, false, false);
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static float GridDistance(float value) => Mathf.Abs(value / 250f - Mathf.Round(value / 250f)) * 250f;
        private static bool Near(float a, float b) => Mathf.Abs(a - b) <= BoundaryTolerance;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }

        private static bool Inside(Vector2[] polygon, float x, float z)
        {
            bool result = false;
            Vector2 previous = polygon[polygon.Length - 1];
            foreach (Vector2 current in polygon)
            {
                if ((current.y > z) != (previous.y > z)
                    && x < (previous.x - current.x) * (z - current.y) / (previous.y - current.y) + current.x)
                    result = !result;
                previous = current;
            }
            return result;
        }

        private sealed class Tile
        {
            public readonly TerrainData Data;
            public readonly Vector3 Size;
            public readonly float BaseHeight;
            public readonly Rect Bounds;
            public Tile(Terrain terrain, Vector3 origin)
            {
                Data = terrain.terrainData;
                Size = Data.size;
                Vector3 offset = terrain.transform.position - origin;
                BaseHeight = offset.y;
                Bounds = new Rect(offset.x, offset.z, Size.x, Size.z);
            }
        }

        private sealed class NativeSampler
        {
            public readonly List<Tile> Tiles = new List<Tile>();
            public readonly Rect Bounds;
            public NativeSampler(Terrain[] terrains, OfficialMapPlan plan)
            {
                Require(terrains != null && terrains.Length > 0, "No authoring terrains were supplied.");
                Vector2 minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
                Vector2 maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
                foreach (Terrain terrain in terrains)
                {
                    Require(terrain != null && terrain.terrainData != null, "An authoring terrain is missing its TerrainData.");
                    Require(Quaternion.Angle(terrain.transform.rotation, Quaternion.identity) < 0.01f
                        && (terrain.transform.lossyScale - Vector3.one).sqrMagnitude < 0.000001f,
                        "Native diagnostics require unrotated terrains with unit scale.");
                    var tile = new Tile(terrain, plan.transform.position);
                    Require(tile.Data.heightmapResolution == 2049, "Expected the V1 2049-sample native terrain grid.");
                    Require(Mathf.Abs(tile.Size.x - 250f) < 0.01f && Mathf.Abs(tile.Size.z - 250f) < 0.01f
                        && Mathf.Abs(tile.Size.y - 350f) < 0.01f, "Expected 250 x 350 x 250 m V1 authoring terrain dimensions.");
                    Tiles.Add(tile);
                    minimum = Vector2.Min(minimum, tile.Bounds.min);
                    maximum = Vector2.Max(maximum, tile.Bounds.max);
                }
                Bounds = Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
            }

            public Raster Read(Rect bounds, float step, bool withSlopes)
            {
                var raster = new Raster(bounds, step, withSlopes);
                foreach (Tile tile in Tiles)
                {
                    int x0 = Mathf.Max(0, Mathf.CeilToInt((tile.Bounds.xMin - bounds.xMin - 0.001f) / step));
                    int z0 = Mathf.Max(0, Mathf.CeilToInt((tile.Bounds.yMin - bounds.yMin - 0.001f) / step));
                    int x1 = Mathf.Min(raster.Width - 1, Mathf.FloorToInt((tile.Bounds.xMax - bounds.xMin + 0.001f) / step));
                    int z1 = Mathf.Min(raster.Height - 1, Mathf.FloorToInt((tile.Bounds.yMax - bounds.yMin + 0.001f) / step));
                    if (x1 < x0 || z1 < z0) continue;
                    float u0 = Mathf.Clamp01((raster.X(x0) - tile.Bounds.xMin) / tile.Size.x);
                    float v0 = Mathf.Clamp01((raster.Z(z0) - tile.Bounds.yMin) / tile.Size.z);
                    float[,] heights = tile.Data.GetInterpolatedHeights(u0, v0, x1 - x0 + 1, z1 - z0 + 1,
                        step / tile.Size.x, step / tile.Size.z);
                    for (int z = z0; z <= z1; z++)
                    {
                        for (int x = x0; x <= x1; x++)
                        {
                            int index = z * raster.Width + x;
                            float height = tile.BaseHeight + heights[z - z0, x - x0];
                            Require(Finite(height), "The native terrain contains a non-finite sampled height.");
                            raster.Heights[index] = height;
                            if (withSlopes)
                                raster.Slopes[index] = tile.Data.GetSteepness(Mathf.Clamp01((raster.X(x) - tile.Bounds.xMin) / tile.Size.x),
                                    Mathf.Clamp01((raster.Z(z) - tile.Bounds.yMin) / tile.Size.z));
                        }
                    }
                }
                return raster;
            }
        }

        private sealed class Raster
        {
            public readonly int Width, Height;
            public readonly float Step;
            public readonly Rect Bounds;
            public readonly float[] Heights, Slopes;
            public Raster(Rect bounds, float step, bool withSlopes)
            {
                Bounds = bounds;
                Step = step;
                Width = Mathf.CeilToInt(bounds.width / step) + 1;
                Height = Mathf.CeilToInt(bounds.height / step) + 1;
                Heights = new float[Width * Height];
                for (int index = 0; index < Heights.Length; index++) Heights[index] = float.NaN;
                if (withSlopes) Slopes = new float[Width * Height];
            }
            public float X(int index) => Bounds.xMin + index * Step;
            public float Z(int index) => Bounds.yMin + index * Step;
            public float ValidHeight(int x, int z, float fallback)
            {
                if (x < 0 || z < 0 || x >= Width || z >= Height) return fallback;
                float value = Heights[z * Width + x];
                return Finite(value) ? value : fallback;
            }
        }
    }
}
