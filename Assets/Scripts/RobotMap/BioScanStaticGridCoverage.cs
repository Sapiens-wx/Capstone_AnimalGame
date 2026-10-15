using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.RobotMap
{
    /// <summary>
    /// Reserves bars in fixed normal-view reference pixels across targets and
    /// mesh batches. Camera zoom must not change these overlap decisions.
    /// Subtracts already covered spans instead of dropping a whole lower-priority
    /// continuous line when only part of it overlaps another revealed object.
    /// </summary>
    internal sealed class BioScanStaticGridCoverage
    {
        private const float CellSize = 32f;
        private const float MinimumCutGap = 2.04f;
        private readonly struct Bar
        {
            public readonly Vector2 Start, End;
            public readonly float OuterHalfWidth;
            public Bar(Vector2 start, Vector2 end, float outerHalfWidth)
            { Start = start; End = end; OuterHalfWidth = outerHalfWidth; }
        }

        private readonly struct Span
        {
            public readonly float Start, End;
            public Span(float start, float end) { Start = start; End = end; }
        }

        private readonly List<Bar> bars = new List<Bar>();
        private readonly Dictionary<Vector2Int, List<int>> cells = new Dictionary<Vector2Int, List<int>>();
        private readonly List<List<int>> cellLists = new List<List<int>>();
        private readonly List<int> blockers = new List<int>();
        private readonly HashSet<int> visitedBars = new HashSet<int>();
        private readonly List<Span> remaining = new List<Span>();
        private readonly List<Span> scratch = new List<Span>();
        private int usedLists;

        public void Clear()
        {
            bars.Clear();
            cells.Clear();
            usedLists = 0;
        }

        public void AppendUncoveredSegments(StaticScanLineSegment segment, Vector2 anchor,
            float width, List<StaticScanLineSegment> destination)
        {
            Vector2 start = anchor + segment.Start, end = anchor + segment.End;
            if (!Finite(start) || !Finite(end) || float.IsNaN(width) || float.IsInfinity(width))
                return;
            float outerHalfWidth = Mathf.Max(.5f, width) * .5f + 1f;
            if (!CellBounds(start, end, outerHalfWidth, out int xMin, out int xMax,
                    out int yMin, out int yMax)) return;
            Vector2 delta = end - start;
            float length = delta.magnitude;
            if (length <= .0001f) return;
            Vector2 direction = delta / length;
            Vector2 normal = new Vector2(-direction.y, direction.x);
            blockers.Clear();
            visitedBars.Clear();
            for (int y = yMin; y <= yMax; y++)
            for (int x = xMin; x <= xMax; x++)
            {
                if (!cells.TryGetValue(new Vector2Int(x, y), out List<int> indices)) continue;
                foreach (int index in indices)
                    if (visitedBars.Add(index)) blockers.Add(index);
            }
            remaining.Clear();
            remaining.Add(new Span(0f, length));
            foreach (int index in blockers)
            {
                if (!BlockedSpan(start, direction, normal, outerHalfWidth, bars[index],
                        out float blockedStart, out float blockedEnd)) continue;
                scratch.Clear();
                foreach (Span span in remaining)
                {
                    if (blockedEnd <= span.Start || blockedStart >= span.End)
                        scratch.Add(span);
                    else
                    {
                        if (blockedStart > span.Start) scratch.Add(new Span(span.Start, blockedStart));
                        if (blockedEnd < span.End) scratch.Add(new Span(blockedEnd, span.End));
                    }
                }
                remaining.Clear();
                remaining.AddRange(scratch);
                if (remaining.Count == 0) return;
            }
            foreach (Span span in remaining)
            {
                if (span.End - span.Start <= .5f) continue;
                Vector2 pieceStart = start + direction * span.Start;
                Vector2 pieceEnd = start + direction * span.End;
                StoreBar(pieceStart, pieceEnd, outerHalfWidth);
                destination.Add(new StaticScanLineSegment(pieceStart - anchor, pieceEnd - anchor));
            }
        }

        private void StoreBar(Vector2 start, Vector2 end, float outerHalfWidth)
        {
            if (!CellBounds(start, end, outerHalfWidth, out int xMin, out int xMax,
                    out int yMin, out int yMax)) return;
            int barIndex = bars.Count;
            bars.Add(new Bar(start, end, outerHalfWidth));
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
        }

        private static bool CellBounds(Vector2 start, Vector2 end, float outerHalfWidth,
            out int xMin, out int xMax, out int yMin, out int yMax)
        {
            float radius = Mathf.Sqrt(outerHalfWidth * outerHalfWidth + 1f);
            Vector2 minimum = Vector2.Min(start, end) - Vector2.one * radius;
            Vector2 maximum = Vector2.Max(start, end) + Vector2.one * radius;
            xMin = Mathf.FloorToInt(minimum.x / CellSize); xMax = Mathf.FloorToInt(maximum.x / CellSize);
            yMin = Mathf.FloorToInt(minimum.y / CellSize); yMax = Mathf.FloorToInt(maximum.y / CellSize);
            return xMin != int.MinValue && xMax != int.MaxValue
                && yMin != int.MinValue && yMax != int.MaxValue
                && (long)xMax - xMin <= 256 && (long)yMax - yMin <= 256;
        }

        private static bool BlockedSpan(Vector2 start, Vector2 direction, Vector2 normal,
            float halfWidth, Bar other, out float minimum, out float maximum)
        {
            Vector2 edge = other.End - other.Start;
            Vector2 otherDirection = edge.normalized;
            Vector2 otherNormal = new Vector2(-otherDirection.y, otherDirection.x);
            Vector2 offset = start - (other.Start + other.End) * .5f;
            // Minkowski bounds include both rectangles' 1px flat-cap AA.
            // Parallel spans leave a fixed 2.02px reference-space safety seam.
            float along = edge.magnitude * .5f + 1.02f
                + Mathf.Abs(Vector2.Dot(normal, otherDirection)) * halfWidth
                + Mathf.Abs(Vector2.Dot(direction, otherDirection));
            float across = other.OuterHalfWidth + .02f
                + Mathf.Abs(Vector2.Dot(normal, otherNormal)) * halfWidth
                + Mathf.Abs(Vector2.Dot(direction, otherNormal));
            minimum = float.NegativeInfinity;
            maximum = float.PositiveInfinity;
            if (!ClipSlab(Vector2.Dot(offset, otherDirection), Vector2.Dot(direction, otherDirection),
                    along, ref minimum, ref maximum)
                || !ClipSlab(Vector2.Dot(offset, otherNormal), Vector2.Dot(direction, otherNormal),
                    across, ref minimum, ref maximum))
                return false;

            // An oblique line can graze a corner of these conservative Minkowski
            // bounds for less than two pixels. Splitting at that tiny interval
            // creates two new flat caps whose one-pixel AA rectangles overlap
            // each other, even though both avoid the original blocking bar.
            // Reserve the complete two-cap gap without widening ordinary cuts.
            if (maximum - minimum < MinimumCutGap)
            {
                float midpoint = (minimum + maximum) * .5f;
                minimum = midpoint - MinimumCutGap * .5f;
                maximum = midpoint + MinimumCutGap * .5f;
            }
            return true;
        }

        private static bool ClipSlab(float origin, float velocity, float extent,
            ref float minimum, ref float maximum)
        {
            if (Mathf.Abs(velocity) < .000001f) return Mathf.Abs(origin) <= extent;
            float first = (-extent - origin) / velocity, last = (extent - origin) / velocity;
            minimum = Mathf.Max(minimum, Mathf.Min(first, last));
            maximum = Mathf.Min(maximum, Mathf.Max(first, last));
            return minimum <= maximum;
        }

        private static bool Finite(Vector2 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y);
    }
}
