using System;
using System.Collections.Generic;
using AnimalGame.MapTest;
using Clipper2Lib;
using UnityEngine;
using UnityEngine.Rendering;

namespace AnimalGame.World.Map
{
    internal sealed class WorldMapShoreline : IDisposable
    {
        private const double UnitsPerMeter = 1000000;
        private const int CornerSegments = 8;
        private readonly HashSet<HeightMapLevelAsset> tracedTiles = new();
        private readonly Material material;
        private readonly MaterialPropertyBlock properties = new();
        public Mesh Mesh { get; }

        public WorldMapShoreline(TiledLevelAsset map)
        {
            var strips = new Paths64();
            double smallestCell = double.PositiveInfinity;
            foreach (var tile in map.Tiles)
            {
                var level = tile.level;
                var depths = level.StaticWaterDepthBytes;
                if (level.BakedStaticWaterMask == null || depths.Length == 0) continue;
                var bounds = level.StaticWaterPixelBounds;
                if (depths.Length != bounds.width * bounds.height || level.StaticWaterRasterResolution <= 0)
                    continue;

                Vector2 tileSize = map.TileSizeMeters;
                double resolution = level.StaticWaterRasterResolution;
                double cellWidth = tileSize.x / resolution;
                double cellHeight = tileSize.y / resolution;
                smallestCell = System.Math.Min(smallestCell, System.Math.Min(cellWidth, cellHeight));
                bool hasWater = false;
                for (int y = 0; y < bounds.height; y++)
                for (int x = 0; x < bounds.width;)
                {
                    if (depths[y * bounds.width + x] == 0)
                    {
                        x++;
                        continue;
                    }
                    int start = x++;
                    while (x < bounds.width && depths[y * bounds.width + x] != 0) x++;
                    double left = (tile.coordinate.x + (bounds.x + start) / resolution) * tileSize.x * UnitsPerMeter;
                    double right = (tile.coordinate.x + (bounds.x + x) / resolution) * tileSize.x * UnitsPerMeter;
                    double bottom = (tile.coordinate.y + (bounds.y + y) / resolution) * tileSize.y * UnitsPerMeter;
                    double top = (tile.coordinate.y + (bounds.y + y + 1) / resolution) * tileSize.y * UnitsPerMeter;
                    strips.Add(new Path64 { new(left, bottom), new(right, bottom), new(right, top), new(left, top) });
                    hasWater = true;
                }
                if (hasWater) tracedTiles.Add(level);
            }

            if (strips.Count == 0) return;
            // Union removes touching cell edges, including edges shared by adjacent tiles.
            var outlines = Clipper.Union(strips, FillRule.NonZero);
            var vertices = new List<Vector3>();
            var joins = new List<Vector2>();
            var distances = new List<Vector3>();
            var triangles = new List<int>();
            foreach (var outline in outlines)
            {
                // Remove raster stair steps before rounding the remaining bends.
                var simplified = Clipper.SimplifyPath(outline, 1.25 * smallestCell * UnitsPerMeter);
                var rounded = RoundCorners(simplified.Count >= 3 ? simplified : outline, (float)(2 * smallestCell));
                AppendLoop(rounded, vertices, joins, distances, triangles);
            }
            if (vertices.Count == 0) return;
            Mesh = new Mesh { name = "World map shoreline", hideFlags = HideFlags.HideAndDontSave,
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            Mesh.SetVertices(vertices);
            Mesh.SetUVs(0, joins);
            Mesh.SetUVs(1, distances);
            Mesh.SetTriangles(triangles, 0);
            material = new Material(Resources.Load<Shader>("WorldMapShoreline"))
                { hideFlags = HideFlags.HideAndDontSave };
        }

        public bool Contains(HeightMapLevelAsset tile) => Mesh != null && tracedTiles.Contains(tile);

        public void Draw(CommandBuffer commands, Matrix4x4 clipFromMap, Vector2 metresPerPixel,
            Vector2 zoom, Vector2 mapSize, float opacity, Texture fog)
        {
            if (Mesh == null) return;
            properties.SetMatrix("_ShorelineClipFromMap", clipFromMap);
            properties.SetVector("_MapMetresPerPixel", metresPerPixel);
            properties.SetVector("_MapViewZoom", zoom);
            properties.SetVector("_MapSizeMeters", mapSize);
            properties.SetFloat("_ShorelineOpacity", opacity);
            properties.SetTexture("_CheckpointFogTex", fog != null ? fog : Texture2D.blackTexture);
            commands.DrawMesh(Mesh, Matrix4x4.identity, material, 0, 0, properties);
        }

        public void Dispose()
        {
            CoreUtils.Destroy(Mesh);
            CoreUtils.Destroy(material);
        }

        private static Vector2 InMeters(Point64 point) =>
            new Vector2((float)(point.X / UnitsPerMeter), (float)(point.Y / UnitsPerMeter));

        private static Vector2 Normal(Vector2 direction) => new Vector2(-direction.y, direction.x).normalized;

        private static List<Vector2> RoundCorners(Path64 outline, float maxTrim)
        {
            int count = outline.Count;
            var rounded = new List<Vector2>(count);
            for (int i = 0; i < count; i++)
            {
                Vector2 corner = InMeters(outline[i]);
                Vector2 incoming = corner - InMeters(outline[(i + count - 1) % count]);
                Vector2 outgoing = InMeters(outline[(i + 1) % count]) - corner;
                float incomingLength = incoming.magnitude;
                float outgoingLength = outgoing.magnitude;
                // Keep adjacent fillets from meeting or consuming short shore features.
                float trim = Mathf.Min(maxTrim, .45f * Mathf.Min(incomingLength, outgoingLength));
                if (trim == 0 || Vector2.Dot(incoming, outgoing) >= .999f * incomingLength * outgoingLength)
                {
                    rounded.Add(corner);
                    continue;
                }

                Vector2 start = corner - incoming * (trim / incomingLength);
                Vector2 end = corner + outgoing * (trim / outgoingLength);
                for (int step = 0; step <= CornerSegments; step++)
                {
                    float t = (float)step / CornerSegments;
                    rounded.Add(Vector2.Lerp(Vector2.Lerp(start, corner, t), Vector2.Lerp(corner, end, t), t));
                }
            }
            return rounded;
        }

        private static void AppendLoop(List<Vector2> outline, List<Vector3> vertices, List<Vector2> joins,
            List<Vector3> distances, List<int> triangles)
        {
            int count = outline.Count;
            if (count < 3) return;
            float perimeter = 0;
            for (int i = 0; i < count; i++)
                perimeter += Vector2.Distance(outline[i], outline[(i + 1) % count]);
            if (perimeter <= 0) return;

            float distance = 0;
            int first = vertices.Count;
            // Repeat the first join to carry both ends of the loop's distance interval.
            for (int i = 0; i <= count; i++)
            {
                Vector2 point = outline[i % count];
                Vector2 previous = outline[(i + count - 1) % count];
                Vector2 next = outline[(i + 1) % count];
                if (i > 0) distance += Vector2.Distance(previous, point);
                Vector2 incoming = Normal(point - previous);
                Vector2 outgoing = Normal(next - point);
                Vector2 join = (incoming + outgoing).normalized;
                join /= Mathf.Max(.5f, Vector2.Dot(join, outgoing));
                vertices.Add(point);
                vertices.Add(point);
                joins.Add(-join);
                joins.Add(join);
                distances.Add(new Vector3(distance / perimeter, perimeter, -1));
                distances.Add(new Vector3(distance / perimeter, perimeter, 1));
                if (i == 0) continue;
                int end = first + 2 * i;
                triangles.Add(end - 2);
                triangles.Add(end - 1);
                triangles.Add(end);
                triangles.Add(end);
                triangles.Add(end - 1);
                triangles.Add(end + 1);
            }
        }
    }
}
