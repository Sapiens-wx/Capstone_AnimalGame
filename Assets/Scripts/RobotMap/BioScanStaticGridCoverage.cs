using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.RobotMap
{
    /// <summary>
    /// Reserves bars in fixed normal-view reference pixels across targets and
    /// mesh batches. Camera zoom must not change these overlap decisions.
    /// Shared-grid duplicates and conflicting tiny-target fallback bars must
    /// not turn overlapping scan areas into bright intersections.
    /// </summary>
    internal sealed class BioScanStaticGridCoverage
    {
        private const float CellSize = 32f;
        private readonly struct Bar
        {
            public readonly Vector2 Start, End;
            public readonly float Radius;
            public Bar(Vector2 start, Vector2 end, float radius)
            { Start = start; End = end; Radius = radius; }
        }

        private readonly List<Bar> bars = new List<Bar>();
        private readonly Dictionary<Vector2Int, List<int>> cells = new Dictionary<Vector2Int, List<int>>();
        private readonly List<List<int>> cellLists = new List<List<int>>();
        private int usedLists;

        public void Clear()
        {
            bars.Clear();
            cells.Clear();
            usedLists = 0;
        }

        public bool TryReserve(StaticScanLineSegment segment, Vector2 anchor, float width)
        {
            Vector2 start = anchor + segment.Start, end = anchor + segment.End;
            if (!Finite(start) || !Finite(end) || float.IsNaN(width) || float.IsInfinity(width))
                return false;
            float outerHalfWidth = Mathf.Max(.5f, width) * .5f + 1f;
            // Cover the square cap's AA corner as well as the long edges.
            float radius = Mathf.Sqrt(outerHalfWidth * outerHalfWidth + 1f);
            Vector2 minimum = Vector2.Min(start, end) - Vector2.one * radius;
            Vector2 maximum = Vector2.Max(start, end) + Vector2.one * radius;
            int xMin = Mathf.FloorToInt(minimum.x / CellSize), xMax = Mathf.FloorToInt(maximum.x / CellSize);
            int yMin = Mathf.FloorToInt(minimum.y / CellSize), yMax = Mathf.FloorToInt(maximum.y / CellSize);
            if (xMin == int.MinValue || xMax == int.MaxValue || yMin == int.MinValue || yMax == int.MaxValue)
                return false;
            if ((long)xMax - xMin > 256 || (long)yMax - yMin > 256)
                return false;
            for (int y = yMin; y <= yMax; y++)
            for (int x = xMin; x <= xMax; x++)
            {
                if (!cells.TryGetValue(new Vector2Int(x, y), out List<int> indices)) continue;
                foreach (int index in indices)
                {
                    Bar other = bars[index];
                    float clearance = radius + other.Radius;
                    if (DistanceSquared(start, end, other.Start, other.End) < clearance * clearance)
                        return false;
                }
            }

            int barIndex = bars.Count;
            bars.Add(new Bar(start, end, radius));
            for (int y = yMin; y <= yMax; y++)
            for (int x = xMin; x <= xMax; x++)
            {
                var key = new Vector2Int(x, y);
                if (!cells.TryGetValue(key, out List<int> indices))
                {
                    if (usedLists == cellLists.Count) cellLists.Add(new List<int>(8));
                    indices = cellLists[usedLists++];
                    indices.Clear();
                    cells.Add(key, indices);
                }
                indices.Add(barIndex);
            }
            return true;
        }

        private static float DistanceSquared(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            Vector2 ab = b - a, cd = d - c;
            float denominator = Cross(ab, cd);
            if (Mathf.Abs(denominator) > .000001f)
            {
                float t = Cross(c - a, cd) / denominator;
                float u = Cross(c - a, ab) / denominator;
                if (t >= 0f && t <= 1f && u >= 0f && u <= 1f) return 0f;
            }
            return Mathf.Min(Mathf.Min(PointDistanceSquared(a, c, d), PointDistanceSquared(b, c, d)),
                Mathf.Min(PointDistanceSquared(c, a, b), PointDistanceSquared(d, a, b)));
        }

        private static float PointDistanceSquared(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 edge = b - a;
            float t = edge.sqrMagnitude > .000001f
                ? Mathf.Clamp01(Vector2.Dot(point - a, edge) / edge.sqrMagnitude) : 0f;
            return (point - a - edge * t).sqrMagnitude;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        private static bool Finite(Vector2 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y);
    }
}
