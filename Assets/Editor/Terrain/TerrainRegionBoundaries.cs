using System;
using System.Collections.Generic;
using Clipper2Lib;
using UnityEngine;

namespace AnimalGame.Editor
{
    internal static class TerrainRegionBoundaries
    {
        internal static void Simplify(Paths64[] terrainPaths, double tolerance,
            HashSet<Vector2Int> occupiedTiles, Vector2 tileSize)
        {
            var horizontal = new Dictionary<long, SortedSet<long>>();
            var vertical = new Dictionary<long, SortedSet<long>>();
            foreach (Paths64 paths in terrainPaths)
            {
                if (paths == null) continue;
                foreach (Path64 path in paths)
                    foreach (Point64 point in path)
                    {
                        AddBreakpoint(horizontal, point.Y, point.X);
                        AddBreakpoint(vertical, point.X, point.Y);
                    }
            }

            var degree = new Dictionary<Point64, int>();
            var edges = new HashSet<(Point64, Point64)>();
            var boundaryIndex = new BoundaryIndex(occupiedTiles, tileSize);
            foreach (Paths64 paths in terrainPaths)
            {
                if (paths == null) continue;
                for (int i = 0; i < paths.Count; i++)
                {
                    Path64 ring = paths[i];
                    ReplacePoints(ring, SplitEdges(ring, horizontal, vertical));
                    for (int j = 0; j < ring.Count; j++)
                    {
                        Point64 a = ring[j];
                        Point64 b = ring[(j + 1) % ring.Count];
                        if (!edges.Add(Compare(a, b) < 0 ? (a, b) : (b, a))) continue;
                        boundaryIndex.Add(a, b);
                        degree.TryGetValue(a, out int first);
                        degree.TryGetValue(b, out int second);
                        degree[a] = first + 1;
                        degree[b] = second + 1;
                    }
                }
            }

            // The same chain is used in both directions by neighboring rings.
            // Where three or more edges meet, their shared endpoint stays fixed.
            var chains = new Dictionary<(Point64, Point64), Path64>();
            var combined = new Paths64();
            foreach (Paths64 paths in terrainPaths)
            {
                if (paths == null || paths.Count == 0) continue;
                for (int i = 0; i < paths.Count; i++)
                {
                    Path64 source = paths[i];
                    Path64 simplified = SimplifyRing(source, degree, chains, boundaryIndex, tolerance);
                    if (simplified.Count < 3 || Math.Sign(Clipper.Area(source)) != Math.Sign(Clipper.Area(simplified)))
                        throw InvalidTolerance("A polygon ring would collapse.");
                    ReplacePoints(source, simplified);
                }

                Paths64 normalized = Clipper.Union(paths, FillRule.NonZero);
                if (!SameArea(Clipper.Area(paths), Clipper.Area(normalized)))
                    throw InvalidTolerance("A terrain polygon would self-intersect.");
                combined.AddRange(normalized);
            }
            if (!SameArea(Clipper.Area(combined), Clipper.Area(Clipper.Union(combined, FillRule.NonZero))))
                throw InvalidTolerance("Different terrain regions would overlap.");
        }

        private static Path64 SimplifyRing(Path64 ring, Dictionary<Point64, int> degree,
            Dictionary<(Point64, Point64), Path64> chains, BoundaryIndex boundaryIndex, double tolerance)
        {
            int start = 0;
            for (int i = 1; i < ring.Count; i++)
                if ((degree[ring[i]] != 2 && degree[ring[start]] == 2)
                    || ((degree[ring[i]] == 2) == (degree[ring[start]] == 2) && Compare(ring[i], ring[start]) < 0))
                    start = i;

            var result = new Path64();
            var chain = new Path64 { ring[start] };
            for (int offset = 1; offset <= ring.Count; offset++)
            {
                Point64 point = ring[(start + offset) % ring.Count];
                chain.Add(point);
                if (offset < ring.Count && degree[point] == 2) continue;

                var key = (chain[0], chain[1]);
                if (!chains.TryGetValue(key, out Path64 simplified))
                {
                    simplified = SimplifyChain(chain, boundaryIndex, tolerance);
                    chains.Add(key, simplified);
                    // The reverse key comes from the original ring, even if its first edge was simplified away.
                    var reversed = new Path64(simplified);
                    reversed.Reverse();
                    Point64 previous = ring[(start + offset - 1) % ring.Count];
                    chains.Add((point, previous), reversed);
                }
                for (int i = 0; i < simplified.Count - 1; i++) result.Add(simplified[i]);
                chain = new Path64 { point };
            }
            return result;
        }

        private static Path64 SimplifyChain(Path64 chain, BoundaryIndex index, double tolerance)
        {
            if (chain.Count <= 2) return chain;
            Path64 proposed;
            if (chain[0] == chain[chain.Count - 1])
            {
                // Pin the loop's anchor and two noncollinear vertices so even a
                // high tolerance cannot replace a small island or hole with a line.
                int opposite = chain.Count / 2;
                int corner = 0;
                double maximumDistance = 0;
                for (int i = 1; i < chain.Count - 1; i++)
                {
                    double distance = Clipper.PerpendicDistFromLineSqrd(chain[i], chain[0], chain[opposite]);
                    if (distance <= maximumDistance) continue;
                    maximumDistance = distance;
                    corner = i;
                }
                if (corner == 0) return chain;
                proposed = new Path64 { chain[0] };
                AppendSimplified(proposed, chain, 0, Math.Min(opposite, corner), tolerance);
                AppendSimplified(proposed, chain, Math.Min(opposite, corner), Math.Max(opposite, corner), tolerance);
                AppendSimplified(proposed, chain, Math.Max(opposite, corner), chain.Count - 1, tolerance);
            }
            else
            {
                proposed = Clipper.RamerDouglasPeucker(chain, tolerance);
            }
            if (proposed.Count == chain.Count) return chain;

            var positions = new Dictionary<Point64, int>();
            for (int i = 0; i < chain.Count - 1; i++) positions.Add(chain[i], i);
            var result = new Path64 { chain[0] };
            int start = 0;
            for (int i = 1; i < proposed.Count; i++)
            {
                Point64 next = proposed[i];
                int end = i == proposed.Count - 1 ? chain.Count - 1 : positions[next];
                if (end == start + 1 || index.CanConnect(chain[start], next, positions, start, end))
                {
                    result.Add(next);
                    if (end > start + 1) index.Add(chain[start], next);
                }
                else
                {
                    for (int j = start + 1; j <= end; j++) result.Add(chain[j]);
                }
                start = end;
            }
            return result;
        }

        private static void AppendSimplified(Path64 result, Path64 chain, int start, int end, double tolerance)
        {
            var section = new Path64();
            for (int i = start; i <= end; i++) section.Add(chain[i]);
            Path64 simplified = Clipper.RamerDouglasPeucker(section, tolerance);
            for (int i = 1; i < simplified.Count; i++) result.Add(simplified[i]);
        }

        private static Path64 SplitEdges(Path64 ring, Dictionary<long, SortedSet<long>> horizontal,
            Dictionary<long, SortedSet<long>> vertical)
        {
            var result = new Path64();
            for (int i = 0; i < ring.Count; i++)
            {
                Point64 a = ring[i];
                Point64 b = ring[(i + 1) % ring.Count];
                result.Add(a);
                bool flat = a.Y == b.Y;
                long start = flat ? a.X : a.Y;
                long end = flat ? b.X : b.Y;
                if (Math.Abs(start - end) <= 1) continue;
                SortedSet<long> line = flat ? horizontal[a.Y] : vertical[a.X];
                SortedSet<long> between = line.GetViewBetween(Math.Min(start, end) + 1, Math.Max(start, end) - 1);
                IEnumerable<long> ordered = start < end ? between : between.Reverse();
                foreach (long position in ordered)
                    result.Add(flat ? new Point64(position, a.Y) : new Point64(a.X, position));
            }
            return result;
        }

        private static void AddBreakpoint(Dictionary<long, SortedSet<long>> lines, long line, long position)
        {
            if (!lines.TryGetValue(line, out SortedSet<long> points))
                lines.Add(line, points = new SortedSet<long>());
            points.Add(position);
        }

        private static int Compare(Point64 a, Point64 b) => a.X == b.X ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X);

        private static void ReplacePoints(Path64 ring, Path64 points)
        {
            ring.Clear();
            ring.AddRange(points);
        }

        private static bool SameArea(double a, double b) => Math.Abs(a - b) <= Math.Max(1, Math.Abs(a) * 1e-12);

        private static InvalidOperationException InvalidTolerance(string detail) =>
            new InvalidOperationException(detail + " Use a smaller boundary tolerance.");

        // Keep original edges and previously accepted shortcuts as obstacles.
        // A candidate can only cross edges that it directly replaces.
        private sealed class BoundaryIndex
        {
            private const long BucketSize = 8_000_000; // 8 map metres
            private readonly Dictionary<(long X, long Y), List<(Point64 A, Point64 B)>> buckets = new();
            private readonly HashSet<Vector2Int> occupiedTiles;
            private readonly double tileWidth;
            private readonly double tileHeight;

            internal BoundaryIndex(HashSet<Vector2Int> occupiedTiles, Vector2 tileSize)
            {
                this.occupiedTiles = occupiedTiles;
                tileWidth = tileSize.x * TerrainRegionsBaker.UnitsPerMeter;
                tileHeight = tileSize.y * TerrainRegionsBaker.UnitsPerMeter;
            }

            internal void Add(Point64 a, Point64 b)
            {
                for (long y = Math.Min(a.Y, b.Y) / BucketSize; y <= Math.Max(a.Y, b.Y) / BucketSize; y++)
                    for (long x = Math.Min(a.X, b.X) / BucketSize; x <= Math.Max(a.X, b.X) / BucketSize; x++)
                    {
                        if (!buckets.TryGetValue((x, y), out var bucket))
                            buckets.Add((x, y), bucket = new List<(Point64, Point64)>());
                        bucket.Add((a, b));
                    }
            }

            internal bool CanConnect(Point64 a, Point64 b, Dictionary<Point64, int> chain, int start, int end)
            {
                if (occupiedTiles != null && CrossesMissingTile(a, b)) return false;
                for (long y = Math.Min(a.Y, b.Y) / BucketSize; y <= Math.Max(a.Y, b.Y) / BucketSize; y++)
                    for (long x = Math.Min(a.X, b.X) / BucketSize; x <= Math.Max(a.X, b.X) / BucketSize; x++)
                    {
                        if (!buckets.TryGetValue((x, y), out var bucket)) continue;
                        foreach (var (c, d) in bucket)
                        {
                            if (!Intersects(a, b, c, d)) continue;
                            bool cOnChain = chain.TryGetValue(c, out int ci);
                            bool dOnChain = chain.TryGetValue(d, out int di);
                            if (c == b) { ci = end; cOnChain = true; }
                            if (d == b) { di = end; dOnChain = true; }
                            if (!cOnChain || !dOnChain || Math.Abs(ci - di) != 1
                                || Math.Min(ci, di) < start || Math.Max(ci, di) > end)
                                return false;
                        }
                    }
                return true;
            }

            private bool CrossesMissingTile(Point64 a, Point64 b)
            {
                for (int y = (int)(Math.Min(a.Y, b.Y) / tileHeight); y <= Math.Max(a.Y, b.Y) / tileHeight; y++)
                    for (int x = (int)(Math.Min(a.X, b.X) / tileWidth); x <= Math.Max(a.X, b.X) / tileWidth; x++)
                    {
                        if (occupiedTiles.Contains(new Vector2Int(x, y))) continue;
                        double enter = 0;
                        double exit = 1;
                        if (CrossesInterval(a.X, b.X, Math.Round(x * tileWidth), Math.Round((x + 1) * tileWidth),
                                ref enter, ref exit)
                            && CrossesInterval(a.Y, b.Y, Math.Round(y * tileHeight), Math.Round((y + 1) * tileHeight),
                                ref enter, ref exit))
                            return true;
                    }
                return false;
            }

            private static bool CrossesInterval(long a, long b, double min, double max,
                ref double enter, ref double exit)
            {
                if (a == b) return min < a && a < max;
                double t0 = (min - a) / (b - a);
                double t1 = (max - a) / (b - a);
                enter = Math.Max(enter, Math.Min(t0, t1));
                exit = Math.Min(exit, Math.Max(t0, t1));
                return enter < exit;
            }

            private static bool Intersects(Point64 a, Point64 b, Point64 c, Point64 d)
            {
                long overlapX = Math.Min(Math.Max(a.X, b.X), Math.Max(c.X, d.X))
                    - Math.Max(Math.Min(a.X, b.X), Math.Min(c.X, d.X));
                long overlapY = Math.Min(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y))
                    - Math.Max(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
                if (overlapX < 0 || overlapY < 0) return false;

                int first = InternalClipper.CrossProductSign(a, b, c);
                int second = InternalClipper.CrossProductSign(a, b, d);
                if (a == c || a == d || b == c || b == d)
                    return first == 0 && second == 0 && (overlapX > 0 || overlapY > 0);
                int third = InternalClipper.CrossProductSign(c, d, a);
                int fourth = InternalClipper.CrossProductSign(c, d, b);
                return first * second <= 0 && third * fourth <= 0;
            }
        }
    }
}
