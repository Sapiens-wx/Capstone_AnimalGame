using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using AnimalGame.MapTest;
using Clipper2Lib;
using UnityEditor;
using UnityEngine;

namespace AnimalGame.Editor
{
    internal static class TerrainRegionsBaker
    {
        internal const int ParallelTileThreshold = 12;
        internal const int BackgroundThreadCount = 5;
        internal const double UnitsPerMeter = 1_000_000;
        private static readonly TerrainKind[] TerrainKinds = (TerrainKind[])Enum.GetValues(typeof(TerrainKind));

        internal readonly struct Tile
        {
            internal readonly Vector2Int Coordinate;
            internal readonly int Resolution;
            internal readonly byte[] Surface;
            internal readonly Paths64 Water;

            internal Tile(Vector2Int coordinate, int resolution, byte[] surface, Paths64 water = null)
            {
                Coordinate = coordinate;
                Resolution = resolution;
                Surface = surface;
                Water = water;
            }
        }

        internal static TerrainRegionsPrebake Update(TiledLevelAsset level, float cellSize,
            float tolerance, float minimumArea)
        {
            if (!level.Validate(out string error)) throw new InvalidOperationException(error);
            string ownerPath = AssetDatabase.GetAssetPath(level);
            if (!ownerPath.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException("Save the Tiled Level under Assets before updating regions.");
            if (EditorUtility.IsDirty(level))
                throw new InvalidOperationException("Save the Tiled Level asset before updating terrain regions.");

            string outputPath = OutputPath(ownerPath);
            foreach (string guid in AssetDatabase.FindAssets("t:TiledLevelAsset"))
            {
                string other = AssetDatabase.GUIDToAssetPath(guid);
                if (other != ownerPath && string.Equals(OutputPath(other), outputPath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Terrain regions output also belongs to {other}.");
            }

            var tiles = new Tile[level.Tiles.Count];
            for (int i = 0; i < tiles.Length; i++)
            {
                var tile = level.Tiles[i];
                if (EditorUtility.IsDirty(tile.level))
                    throw new InvalidOperationException($"Save the source asset {tile.level.name} before baking terrain regions.");
                tiles[i] = new Tile(tile.coordinate, tile.level.SurfaceMaskResolution, tile.level.CopySurfaceTypeMap(),
                    ReadWaterTile(tile.level, tile.coordinate, level.TileSizeMeters));
            }

            var data = Build(tiles, level.TileSizeMeters, cellSize, tolerance, minimumArea);
            string directory = Path.GetDirectoryName(outputPath);
            Directory.CreateDirectory(directory);
            string temporaryPath = outputPath + ".tmp";
            try
            {
                using (var stream = File.Create(temporaryPath))
                    data.Write(stream);
                if (File.Exists(outputPath))
                    File.Replace(temporaryPath, outputPath, null);
                else
                    File.Move(temporaryPath, outputPath);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            if (!AssetDatabase.IsValidFolder(directory))
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            else
                AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceSynchronousImport);
            string resourcePath = outputPath.Substring(HeightMapAssetPaths.ResourcesDirectory.Length);
            resourcePath = resourcePath.Substring(0, resourcePath.Length - ".bytes".Length);
            if (level.PrebakedTerrainRegionsPath != resourcePath)
            {
                level.SetPrebakedTerrainRegionsPath(resourcePath);
                EditorUtility.SetDirty(level);
                AssetDatabase.SaveAssetIfDirty(level);
            }
            return data;
        }

        private static string OutputPath(string ownerPath) => HeightMapAssetPaths.PrebakeDirectory(ownerPath)
            + "/" + Path.GetFileNameWithoutExtension(ownerPath) + "_TerrainRegions.bytes";

        internal static TerrainRegionsPrebake Build(Tile[] tiles, Vector2 size, float cellSize,
            float tolerance, float minimumArea)
        {
            ValidateParameter(cellSize, nameof(cellSize));
            ValidateParameter(tolerance, nameof(tolerance));
            ValidateParameter(minimumArea, nameof(minimumArea));

            var tilePaths = new Paths64[tiles.Length][];
            int lanes = tiles.Length >= ParallelTileThreshold ? BackgroundThreadCount + 1 : 1;
            var failures = new Exception[lanes];
            void ComputeLane(int lane)
            {
                try
                {
                    for (int i = lane; i < tiles.Length; i += lanes)
                        tilePaths[i] = BuildTile(tiles[i], size, cellSize);
                }
                catch (Exception error)
                {
                    failures[lane] = error;
                }
            }

            var threads = new Thread[lanes - 1];
            int started = 0;
            try
            {
                for (int i = 0; i < threads.Length; i++)
                {
                    int lane = i + 1;
                    threads[i] = new Thread(() => ComputeLane(lane)) { IsBackground = true };
                    threads[i].Start();
                    started++;
                }
                ComputeLane(0);
            }
            finally
            {
                for (int i = 0; i < started; i++) threads[i].Join();
            }
            foreach (Exception failure in failures)
                if (failure != null) throw new InvalidOperationException("Terrain tile polygonization failed.", failure);

            // Each subject already contains only its tile's coverage. The global union
            // dissolves collinear overlaps even when adjacent edges have different lengths.
            var trees = new PolyTree64[TerrainKinds.Length];
            var paths = new Paths64[TerrainKinds.Length];
            int waterKind = (int)TerrainKind.Water;
            for (int kind = 1; kind < trees.Length; kind++)
            {
                if (kind == waterKind) continue;
                var combined = new Paths64();
                foreach (Paths64[] tile in tilePaths)
                    if (tile[kind] != null) combined.AddRange(tile[kind]);
                if (combined.Count == 0) continue;

                PolyTree64 tree = BuildTree(combined);
                trees[kind] = tree;
                if (tolerance > 0)
                {
                    paths[kind] = new Paths64();
                    CollectRings(tree, paths[kind]);
                }
            }

            var water = new Paths64();
            foreach (Tile tile in tiles)
                if (tile.Water != null) water.AddRange(tile.Water);
            if (water.Count > 0)
            {
                trees[waterKind] = BuildTree(water);
                water.Clear();
                CollectRings(trees[waterKind], water);
            }

            if (tolerance > 0)
            {
                var occupied = new HashSet<Vector2Int>();
                int width = 0;
                int height = 0;
                foreach (Tile tile in tiles)
                {
                    occupied.Add(tile.Coordinate);
                    width = Math.Max(width, tile.Coordinate.x + 1);
                    height = Math.Max(height, tile.Coordinate.y + 1);
                }
                HashSet<Vector2Int> mapTiles = occupied.Count == (long)width * height ? null : occupied;
                double toleranceUnits = tolerance * UnitsPerMeter;
                TerrainRegionBoundaries.Simplify(paths, toleranceUnits, mapTiles, size);
                if (water.Count > 0)
                {
                    // Water still overlaps land here, so simplify its rings independently.
                    TerrainRegionBoundaries.Simplify(new[] { water }, toleranceUnits, mapTiles, size);
                    trees[waterKind] = BuildTree(water);
                    water.Clear();
                    CollectRings(trees[waterKind], water);
                }
            }

            if (water.Count > 0)
            {
                // Cut after simplification so both sides use the same shoreline.
                for (int kind = 1; kind < trees.Length; kind++)
                {
                    if (kind == waterKind || trees[kind] == null) continue;
                    Paths64 land = paths[kind];
                    if (land == null)
                    {
                        land = new Paths64();
                        CollectRings(trees[kind], land);
                    }
                    trees[kind] = BuildTree(land, water);
                }
            }

            var regions = new List<TerrainRegionsPrebake.Region>();
            for (int kind = 1; kind < trees.Length; kind++)
            {
                PolyTree64 tree = trees[kind];
                if (tree == null) continue;
                var polygons = new List<TerrainRegionsPrebake.Polygon>();
                double areaThreshold = kind == waterKind ? 0 : minimumArea * UnitsPerMeter * UnitsPerMeter;
                for (int i = 0; i < tree.Count; i++)
                    CollectPolygons(tree[i], areaThreshold, polygons);
                if (polygons.Count > 0)
                    regions.Add(new TerrainRegionsPrebake.Region(TerrainKinds[kind], polygons.ToArray()));
            }
            return new TerrainRegionsPrebake(regions.ToArray());
        }

        private static PolyTree64 BuildTree(Paths64 subject, Paths64 clip = null)
        {
            var tree = new PolyTree64();
            var clipper = new Clipper64 { PreserveCollinear = false };
            clipper.AddSubject(subject);
            if (clip != null) clipper.AddClip(clip);
            if (!clipper.Execute(clip == null ? ClipType.Union : ClipType.Difference, FillRule.NonZero, tree))
                throw new InvalidOperationException(clip == null
                    ? "Could not merge the terrain region polygons."
                    : "Could not subtract water from the terrain region polygons.");
            return tree;
        }

        private static void CollectRings(PolyPath64 parent, Paths64 paths)
        {
            for (int i = 0; i < parent.Count; i++)
            {
                PolyPath64 child = parent[i];
                paths.Add(child.Polygon);
                CollectRings(child, paths);
            }
        }

        private static Paths64[] BuildTile(Tile tile, Vector2 size, float cellSize)
        {
            var paths = new Paths64[TerrainKinds.Length];
            if (tile.Surface.Length == 0) return paths;
            int width = CellCount(size.x, cellSize, tile.Resolution);
            int height = CellCount(size.y, cellSize, tile.Resolution);
            var xEdges = CellEdges(tile.Coordinate.x, size.x, width);
            var yEdges = CellEdges(tile.Coordinate.y, size.y, height);

            for (int y = 0; y < height; y++)
            {
                int row = SampleCenter(y, height, tile.Resolution) * tile.Resolution;
                int start = 0;
                while (start < width)
                {
                    byte kind = tile.Surface[row + SampleCenter(start, width, tile.Resolution)];
                    int end = start + 1;
                    while (end < width && tile.Surface[row + SampleCenter(end, width, tile.Resolution)] == kind)
                        end++;
                    if (kind != (byte)TerrainKind.Unknown && kind != (byte)TerrainKind.Water)
                    {
                        Paths64 rectangles = paths[kind] ??= new Paths64();
                        rectangles.Add(new Path64
                        {
                            new Point64(xEdges[start], yEdges[y]),
                            new Point64(xEdges[end], yEdges[y]),
                            new Point64(xEdges[end], yEdges[y + 1]),
                            new Point64(xEdges[start], yEdges[y + 1])
                        });
                    }
                    start = end;
                }
            }
            for (int kind = 1; kind < paths.Length; kind++)
                if (paths[kind] != null) paths[kind] = Union(paths[kind]);
            return paths;
        }

        private static Paths64 ReadWaterTile(HeightMapLevelAsset level, Vector2Int coordinate, Vector2 size)
        {
            if (level.BakedStaticWaterMask == null) return null;

            // Decode the saved mask at its native dimensions. The imported texture is GPU-only.
            string path = AssetDatabase.GetAssetPath(level.BakedStaticWaterMask);
            var mask = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!ImageConversion.LoadImage(mask, File.ReadAllBytes(path)))
                    throw new InvalidDataException($"Could not decode Water Mask: {path}");

                return BuildWaterTile(coordinate, size, level.BakedStaticWaterBounds,
                    mask.width, mask.height, mask.GetPixels32());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mask);
            }
        }

        private static Paths64 BuildWaterTile(Vector2Int coordinate, Vector2 size, Rect bounds,
            int width, int height, Color32[] pixels)
        {
            long[] xEdges = CellEdges(coordinate.x, size.x, width, bounds.x, bounds.width);
            long[] yEdges = CellEdges(coordinate.y, size.y, height, bounds.y, bounds.height);
            var rectangles = new Paths64();
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width;)
                {
                    // Red is the binary wet range. The remaining channels encode depth and flow.
                    if (pixels[row + x].r == 0)
                    {
                        x++;
                        continue;
                    }

                    int start = x++;
                    while (x < width && pixels[row + x].r != 0) x++;
                    rectangles.Add(new Path64
                    {
                        new Point64(xEdges[start], yEdges[y]),
                        new Point64(xEdges[x], yEdges[y]),
                        new Point64(xEdges[x], yEdges[y + 1]),
                        new Point64(xEdges[start], yEdges[y + 1])
                    });
                }
            }
            return Union(rectangles);
        }

        private static Paths64 Union(Paths64 input)
        {
            if (input.Count == 0) return input;
            var clipper = new Clipper64 { PreserveCollinear = false };
            clipper.AddSubject(input);
            var result = new Paths64();
            if (!clipper.Execute(ClipType.Union, FillRule.NonZero, result))
                throw new InvalidOperationException("Could not merge terrain polygons.");
            return result;
        }

        private static int CellCount(float size, float cellSize, int native) => cellSize == 0
            ? native : (int)Math.Min(native, Math.Ceiling((double)size / cellSize));

        private static int SampleCenter(int index, int count, int native) => (2 * index + 1) * native / (2 * count);

        private static long[] CellEdges(int tile, float size, int count, double origin = 0, double range = 1)
        {
            var edges = new long[count + 1];
            for (int i = 0; i <= count; i++)
                edges[i] = checked((long)Math.Round((tile + origin + range * i / count) * size * UnitsPerMeter));
            return edges;
        }

        private static void CollectPolygons(PolyPath64 outer, double minimumArea,
            List<TerrainRegionsPrebake.Polygon> polygons)
        {
            double area = Math.Abs(Clipper.Area(outer.Polygon));
            for (int h = 0; h < outer.Count; h++)
                area -= Math.Abs(Clipper.Area(outer[h].Polygon));
            if (area >= minimumArea)
            {
                var holes = new Vector2[outer.Count][];
                for (int h = 0; h < holes.Length; h++) holes[h] = InMeters(outer[h].Polygon);
                polygons.Add(new TerrainRegionsPrebake.Polygon(InMeters(outer.Polygon), holes));
            }
            for (int h = 0; h < outer.Count; h++)
            {
                PolyPath64 hole = outer[h];
                for (int i = 0; i < hole.Count; i++)
                    CollectPolygons(hole[i], minimumArea, polygons);
            }
        }

        internal static Vector2[] InMeters(Path64 path)
        {
            var points = new Vector2[path.Count];
            for (int i = 0; i < points.Length; i++)
                points[i] = new Vector2((float)(path[i].X / UnitsPerMeter), (float)(path[i].Y / UnitsPerMeter));
            return points;
        }

        private static void ValidateParameter(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0)
                throw new ArgumentOutOfRangeException(name);
        }
    }
}
