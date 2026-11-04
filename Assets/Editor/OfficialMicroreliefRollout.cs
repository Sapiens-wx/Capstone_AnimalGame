using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AnimalGame.Editor
{
    [InitializeOnLoad]
    internal static class OfficialMicroreliefRollout
    {
        private const string Work = "Temp/MicroreliefRollout";
        private const float TileSize = 250f, ContourInterval = 7f, SeamBuffer = 8f;
        private const int CellsPerTile = 4;
        private static readonly Rect OriginalSample = new Rect(680, 365, 50, 100);
        private static readonly Vector2 Spawn = new Vector2(700, 372);

        static OfficialMicroreliefRollout() => EditorApplication.update += Tick;

        private static void Tick()
        {
            string commandPath = Work + "/Command.txt";
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(commandPath)) return;
            string command = File.ReadAllText(commandPath).Trim();
            File.Delete(commandPath);
            try
            {
                if (command == "refresh")
                {
                    AssetDatabase.Refresh();
                    return;
                }
                if (command == "build")
                {
                    Build();
                    return;
                }
                if (command == "probe")
                {
                    var manifest = JsonUtility.FromJson<Layout>(File.ReadAllText(Work + "/Layout.json"));
                    RolloutProbe.Begin(manifest.scenePath, manifest.probes.Select(b => b.center).ToArray(),
                        manifest.probes.Select(b => b.forward).ToArray(), Work + "/PlayReport.json");
                    return;
                }
                if (command.StartsWith("probe ", StringComparison.Ordinal))
                {
                    RolloutProbe.BeginFromFile(command.Substring("probe ".Length));
                    return;
                }
                throw new InvalidOperationException("Unknown rollout command: " + command);
            }
            catch (Exception exception)
            {
                File.WriteAllText(Work + "/Error.txt", exception.ToString());
                Debug.LogException(exception);
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        private static void Build()
        {
            if (File.Exists(Work + "/Layout.json"))
                throw new InvalidOperationException("A layout already exists. Avoid applying the same bumps twice.");
            Scene current = SceneManager.GetActiveScene();
            if (current.path != OfficialPlainsGameplay.AuthoringScenePath || SceneManager.sceneCount != 1)
            {
                if (Enumerable.Range(0, SceneManager.sceneCount).Any(index => SceneManager.GetSceneAt(index).isDirty))
                    throw new InvalidOperationException("Save the open scene before opening the official prairie.");
                EditorSceneManager.OpenScene(OfficialPlainsGameplay.AuthoringScenePath, OpenSceneMode.Single);
            }

            Terrain[] terrains = OfficialPlainsGameplay.GetSourceTerrains();
            Vector2 origin = new Vector2(terrains.Min(t => t.transform.position.x), terrains.Min(t => t.transform.position.z));
            var destination = OfficialPlainsGameplay.CreateNextDestination();
            var layout = new Layout { scenePath = destination.ScenePath, mapFolder = destination.Folder };
            var plans = new List<TilePlan>(terrains.Length);
            for (int index = 0; index < terrains.Length; index++)
            {
                Terrain terrain = terrains[index];
                EditorUtility.DisplayProgressBar("Prairie microrelief", "Checking " + terrain.name, .45f * index / terrains.Length);
                var plan = new TilePlan(terrain, origin);
                plan.Generate();
                if (!plan.Record.bumps.Any(b => b.landing))
                    throw new InvalidOperationException("No safe landing mound fits " + terrain.name);
                layout.tiles.Add(plan.Record);
                plans.Add(plan);
            }

            Vector2[] probeAreas = { new Vector2(475, 1150), new Vector2(1300, 1550), new Vector2(300, 2090) };
            Bump[] all = layout.tiles.SelectMany(tile => tile.bumps).ToArray();
            foreach (Vector2 area in probeAreas)
            {
                Bump probe = all.Where(b => b.landing && b.approachRelief < .10f && !layout.probes.Contains(b))
                    .Where(b => all.All(other => !other.landing || other == b || !InProbeCorridor(b, other)))
                    .OrderBy(b => (b.center - area).sqrMagnitude).First();
                layout.probes.Add(probe);
            }
            File.WriteAllText(Work + "/Layout.json", JsonUtility.ToJson(layout, true));
            for (int index = 0; index < plans.Count; index++)
            {
                EditorUtility.DisplayProgressBar("Prairie microrelief", "Applying " + plans[index].Record.terrainData,
                    .45f + .55f * index / plans.Count);
                plans[index].Apply();
            }

            OfficialPlainsGameplay.ExportCurrent(destination, Spawn);
            File.WriteAllText(Work + "/BuildResult.txt",
                $"PASS {destination.ScenePath}\n{all.Length} new bumps, {all.Count(b => b.landing)} landing mounds, {layout.tiles.Count} tiles.");
        }

        private static bool InProbeCorridor(Bump route, Bump other)
        {
            Vector2 displacement = other.center - route.center;
            float along = Vector2.Dot(displacement, route.forward);
            float across = (displacement - along * route.forward).magnitude;
            return along > -19 && along < 17 && across < 5;
        }

        private sealed class TilePlan
        {
            internal readonly TileRecord Record;
            private readonly Terrain terrain;
            private readonly TerrainData data;
            private float[,] before;
            private readonly Vector2 tileOrigin;
            private readonly System.Random random;
            private readonly float step, yOrigin, range;
            private readonly int resolution;

            internal TilePlan(Terrain terrain, Vector2 origin)
            {
                this.terrain = terrain;
                data = terrain.terrainData;
                resolution = data.heightmapResolution;
                range = data.size.y;
                yOrigin = terrain.transform.position.y;
                if (resolution != 2049 || data.size != new Vector3(TileSize, 350f, TileSize))
                    throw new InvalidOperationException("Expected native V1 terrain on " + terrain.name);
                step = TileSize / (resolution - 1);
                tileOrigin = new Vector2(terrain.transform.position.x, terrain.transform.position.z) - origin;
                var cell = new Vector2Int(Mathf.RoundToInt(tileOrigin.x / TileSize), Mathf.RoundToInt(tileOrigin.y / TileSize));
                random = new System.Random(522517 + 1009 * cell.x + 3137 * cell.y);
                data.SyncHeightmap();
                before = data.GetHeights(0, 0, resolution, resolution);
                Record = new TileRecord { cell = cell, terrainData = AssetDatabase.GetAssetPath(data) };
            }

            internal void Generate()
            {
                const float cellSize = TileSize / CellsPerTile;
                for (int z = 0; z < CellsPerTile; z++)
                    for (int x = 0; x < CellsPerTile; x++)
                    {
                        if (random.NextDouble() < .11) continue;
                        Vector2 center = cellSize * new Vector2(x + .5f, z + .5f);
                        for (int attempt = 0; attempt < 36; attempt++)
                        {
                            Vector2 local = center + new Vector2(Range(-25, 25), Range(-25, 25));
                            float angle = Range(0, 2 * Mathf.PI);
                            Vector2 forward = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
                            var landing = new Bump(tileOrigin + local, forward, true,
                                Range(.22f, .265f), Range(1.8f, 2.2f), Range(1.52f, 1.8f), Range(1.02f, 1.20f));
                            if (!GentleApproach(landing) || !Accept(landing)) continue;
                            foreach (int sign in new[] { -1, 1 })
                            {
                                Vector2 side = new Vector2(forward.y, -forward.x);
                                Vector2 offset = sign * forward * Range(8.5f, 13.5f) + side * Range(-2.0f, 2.0f);
                                Accept(new Bump(landing.center + offset, forward, false,
                                    Range(.075f, .10f), Range(1.95f, 2.4f), Range(2.3f, 2.7f), Range(1.5f, 1.8f)));
                            }
                            break;
                        }
                    }
                before = null;
            }

            private bool GentleApproach(Bump bump)
            {
                Vector2 local = bump.center - tileOrigin;
                if (local.x < 18 || local.y < 18 || local.x > TileSize - 18 || local.y > TileSize - 18)
                    return false;
                float min = float.PositiveInfinity, max = float.NegativeInfinity;
                float previous = 0;
                for (int i = -16; i <= 14; i++)
                {
                    Vector2 p = local + bump.forward * i;
                    float meters = Sample(Mathf.RoundToInt(p.x / step), Mathf.RoundToInt(p.y / step));
                    if (i > -16 && Mathf.Abs(meters - previous) > .16f) return false;
                    min = Mathf.Min(min, meters);
                    max = Mathf.Max(max, meters);
                    previous = meters;
                }
                bump.approachRelief = max - min;
                return true;
            }

            private bool Accept(Bump bump)
            {
                Rect bounds = bump.Bounds;
                Vector2 localMin = bounds.min - tileOrigin;
                Vector2 localMax = bounds.max - tileOrigin;
                if (localMin.x < SeamBuffer || localMin.y < SeamBuffer || localMax.x > TileSize - SeamBuffer ||
                    localMax.y > TileSize - SeamBuffer || SampleOverlap(bounds) ||
                    Record.bumps.Any(other => other.Bounds.Overlaps(bounds) ||
                        (other.center - bump.center).sqrMagnitude < (other.landing && bump.landing ? 26f * 26f : 7f * 7f)))
                    return false;
                RectInt samples = ToSamples(bounds);
                float low = float.PositiveInfinity, high = float.NegativeInfinity, maxSlope = 0;
                int count = 0;
                for (int z = samples.yMin; z < samples.yMax; z++)
                    for (int x = samples.xMin; x < samples.xMax; x++)
                    {
                        Vector2 p = MapPoint(x, z);
                        float delta = bump.Elevation(p);
                        if (delta <= 0) continue;
                        float height = Sample(x, z);
                        low = Mathf.Min(low, height);
                        high = Mathf.Max(high, height + delta);
                        float dx = (Sample(x + 1, z) - Sample(x - 1, z)) / (2 * step);
                        float dz = (Sample(x, z + 1) - Sample(x, z - 1)) / (2 * step);
                        if (dx * dx + dz * dz > .16f * .16f) return false;
                        dx += (bump.Elevation(p + Vector2.right * step) - bump.Elevation(p - Vector2.right * step)) / (2 * step);
                        dz += (bump.Elevation(p + Vector2.up * step) - bump.Elevation(p - Vector2.up * step)) / (2 * step);
                        maxSlope = Mathf.Max(maxSlope, dx * dx + dz * dz);
                        count++;
                    }
                float bandStart = Mathf.Floor(low / ContourInterval) * ContourInterval;
                if (count == 0 || low <= bandStart + .045f || high >= bandStart + ContourInterval - .045f ||
                    maxSlope > .47f * .47f)
                    return false;
                // Keep interpolation cells around the edited footprint off the contour, too.
                for (int z = samples.yMin - 2; z < samples.yMax + 2; z++)
                    for (int x = samples.xMin - 2; x < samples.xMax + 2; x++)
                    {
                        float height = Sample(x, z);
                        if (height <= bandStart + .045f || height >= bandStart + ContourInterval - .045f)
                            return false;
                    }
                bump.baseMin = low;
                bump.raisedMax = high;
                bump.maximumSlopeDegrees = Mathf.Atan(Mathf.Sqrt(maxSlope)) * Mathf.Rad2Deg;
                Record.bumps.Add(bump);
                return true;
            }

            internal void Apply()
            {
                foreach (Bump bump in Record.bumps)
                {
                    RectInt samples = ToSamples(bump.Bounds);
                    float[,] heights = data.GetHeights(samples.x, samples.y, samples.width, samples.height);
                    for (int dz = 0; dz < samples.height; dz++)
                        for (int dx = 0; dx < samples.width; dx++)
                        {
                            int x = samples.x + dx, z = samples.y + dz;
                            heights[dz, dx] += bump.Elevation(MapPoint(x, z)) / range;
                        }
                    data.SetHeightsDelayLOD(samples.x, samples.y, heights);
                }
                data.SyncHeightmap();
                terrain.Flush();
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssetIfDirty(data);
            }

            private float Range(float lo, float hi) => Mathf.Lerp(lo, hi, (float)random.NextDouble());
            private float Sample(int x, int z) => yOrigin + before[z, x] * range;
            private Vector2 MapPoint(int x, int z) => tileOrigin + new Vector2(x * step, z * step);
            private static bool SampleOverlap(Rect bounds) => OriginalSample.Overlaps(bounds);
            private RectInt ToSamples(Rect bounds)
            {
                Vector2 min = (bounds.min - tileOrigin) / step, max = (bounds.max - tileOrigin) / step;
                int x = Mathf.CeilToInt(min.x), z = Mathf.CeilToInt(min.y);
                return new RectInt(x, z, Mathf.FloorToInt(max.x) - x + 1, Mathf.FloorToInt(max.y) - z + 1);
            }
        }

        [Serializable] private sealed class Layout
        {
            public string scenePath, mapFolder;
            public List<TileRecord> tiles = new List<TileRecord>();
            public List<Bump> probes = new List<Bump>();
        }

        [Serializable] internal sealed class TileRecord
        {
            public Vector2Int cell;
            public string terrainData;
            public List<Bump> bumps = new List<Bump>();
        }

        [Serializable] internal sealed class Bump
        {
            public Vector2 center, forward;
            public bool landing;
            public float height, side, rise, fall, baseMin, raisedMax, maximumSlopeDegrees, approachRelief;

            internal Bump(Vector2 center, Vector2 forward, bool landing, float height, float side, float rise, float fall)
            {
                this.center = center; this.forward = forward; this.landing = landing;
                this.height = height; this.side = side; this.rise = rise; this.fall = fall;
            }

            internal Rect Bounds
            {
                get
                {
                    Vector2 along = new Vector2(Mathf.Abs(forward.x), Mathf.Abs(forward.y));
                    Vector2 across = new Vector2(along.y, along.x);
                    Vector2 extents = along * (rise + fall) * .5f + across * side;
                    Vector2 middle = center + forward * (fall - rise) * .5f;
                    return Rect.MinMaxRect(middle.x - extents.x, middle.y - extents.y, middle.x + extents.x, middle.y + extents.y);
                }
            }

            internal float Elevation(Vector2 point)
            {
                Vector2 d = point - center;
                float along = Vector2.Dot(d, forward);
                float u = Mathf.Abs(along) / (along < 0 ? rise : fall);
                float v = Mathf.Abs(d.x * forward.y - d.y * forward.x) / side;
                return u >= 1 || v >= 1 ? 0 : height * Cap(u) * Cap(v);
            }

            private static float Cap(float t) => .5f * (1 + Mathf.Cos(Mathf.PI * t));
        }
    }
}
