using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.RobotMap
{
    /// <summary>A complete finite bar, relative to a target's projected screen anchor.</summary>
    public readonly struct StaticScanLineSegment
    {
        public readonly Vector2 Start;
        public readonly Vector2 End;

        public StaticScanLineSegment(Vector2 start, Vector2 end)
        {
            Start = start;
            End = end;
        }
    }

    /// <summary>
    /// Builds two perpendicular families of finite, square-ended bars. Each bar
    /// occupies the middle of a grid edge, leaving the shared intersections open.
    /// The complete bar and its antialias padding fit inside the convex footprint.
    /// </summary>
    public static class BioScanStaticLinePattern
    {
        private const float IntersectionEpsilon = 0.0001f;
        private const float LayoutTolerancePixels = 0.01f;
        private const float AntialiasPaddingPixels = 1f;
        private const int MaximumPatternLines = 15000;
        private const int MaximumWorkItems = 30000;
        private const int MaximumFallbackLines = 4;

        public static bool Build(List<Vector2> footprintOffsetsPixels, float lineSpacingPixels,
            float lineAngleDegrees, List<StaticScanLineSegment> destination,
            float intersectionGapPixels = 5.6f, float lineWidthPixels = 1.4f,
            Vector2 latticeOrigin = default)
        {
            if (destination == null)
                return false;
            destination.Clear();
            if (footprintOffsetsPixels == null || footprintOffsetsPixels.Count < 3
                || !IsFinite(lineSpacingPixels) || !IsFinite(lineAngleDegrees)
                || !IsFinite(intersectionGapPixels) || !IsFinite(lineWidthPixels)
                || !IsFinite(latticeOrigin.x) || !IsFinite(latticeOrigin.y)
                || lineSpacingPixels <= 0f || lineWidthPixels <= 0f
                || intersectionGapPixels < 0f)
                return false;

            double twiceArea = 0d;
            Vector2 centroid = Vector2.zero;
            Vector2 previous = footprintOffsetsPixels[footprintOffsetsPixels.Count - 1];
            foreach (Vector2 point in footprintOffsetsPixels)
            {
                if (!IsFinite(point.x) || !IsFinite(point.y))
                    return false;
                twiceArea += (double)previous.x * point.y - (double)previous.y * point.x;
                centroid += point / footprintOffsetsPixels.Count;
                previous = point;
            }
            if (double.IsNaN(twiceArea) || double.IsInfinity(twiceArea)
                || Math.Abs(twiceArea) < IntersectionEpsilon
                || !IsFinite(centroid.x) || !IsFinite(centroid.y))
                return false;

            // Even an unusually small authored gap must keep both complete AA
            // rectangles apart, rather than merely separating their centerlines.
            float gap = Mathf.Max(intersectionGapPixels,
                lineWidthPixels + AntialiasPaddingPixels * 4f + LayoutTolerancePixels);
            float barLength = lineSpacingPixels - gap;
            if (barLength <= IntersectionEpsilon)
                return false;

            float angle = lineAngleDegrees * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 normal = new Vector2(-direction.y, direction.x);
            float winding = twiceArea > 0d ? 1f : -1f;
            float halfWidthWithPadding = lineWidthPixels * .5f + AntialiasPaddingPixels
                + LayoutTolerancePixels;
            var fallback = new FallbackSegments();
            int workItems = 0;
            if (!BuildFamily(footprintOffsetsPixels, direction, normal, winding,
                    latticeOrigin, lineSpacingPixels, barLength, lineWidthPixels,
                    halfWidthWithPadding, destination, ref fallback, ref workItems)
                || !BuildFamily(footprintOffsetsPixels, normal, -direction, winding,
                    latticeOrigin, lineSpacingPixels, barLength, lineWidthPixels,
                    halfWidthWithPadding, destination, ref fallback, ref workItems))
            {
                destination.Clear();
                return false;
            }

            if (destination.Count > 0)
                return true;
            if (fallback.Count > 0)
            {
                // Small footprints can keep a few shorter bars at their normal
                // cell centers. These remain subsets of the original grid edges,
                // so they cannot cross the second family or another target's grid.
                for (int index = 0; index < fallback.Count; index++)
                    destination.Add(fallback[index]);
                return true;
            }

            // A footprint smaller than a grid cell may miss every edge center.
            // Use one complete interior bar; never introduce a miniature X.
            float offset = Vector2.Dot(centroid, normal);
            if (!TryGetSafeInterval(footprintOffsetsPixels, direction, normal,
                    offset, winding, halfWidthWithPadding, out float start, out float end))
                return false;
            float length = Mathf.Min(barLength, end - start);
            if (length <= IntersectionEpsilon)
                return false;
            AddSegment(destination, direction, normal, offset, (start + end) * .5f, length);
            return true;
        }

        private static bool BuildFamily(List<Vector2> polygon, Vector2 direction, Vector2 normal,
            float winding, Vector2 latticeOrigin, float spacing, float barLength,
            float lineWidth, float halfWidthWithPadding, List<StaticScanLineSegment> destination,
            ref FallbackSegments fallback, ref int workItems)
        {
            float minimum = float.PositiveInfinity;
            float maximum = float.NegativeInfinity;
            foreach (Vector2 point in polygon)
            {
                float coordinate = Vector2.Dot(point, normal);
                minimum = Mathf.Min(minimum, coordinate);
                maximum = Mathf.Max(maximum, coordinate);
            }
            double originNormal = Vector2.Dot(latticeOrigin, normal);
            double originAlong = Vector2.Dot(latticeOrigin, direction);
            double firstRow = Math.Ceiling((minimum - originNormal - LayoutTolerancePixels) / spacing);
            double lastRow = Math.Floor((maximum - originNormal + LayoutTolerancePixels) / spacing);
            if (!TryCount(firstRow, lastRow, out int rowCount))
                return false;
            float minimumFallbackLength = Mathf.Max(6f, lineWidth * 1.5f);
            for (int row = 0; row < rowCount; row++)
            {
                if (++workItems > MaximumWorkItems)
                    return false;
                float offset = (float)(originNormal + (firstRow + row) * spacing);
                if (!TryGetSafeInterval(polygon, direction, normal, offset, winding,
                        halfWidthWithPadding, out float start, out float end))
                    continue;

                // Enumerate grid edge centers, rather than clipping a continuous
                // hatch to the silhouette. Edges enter or leave only as whole bars.
                double firstCell = Math.Ceiling((start - originAlong - LayoutTolerancePixels) / spacing - .5d);
                double lastCell = Math.Floor((end - originAlong + LayoutTolerancePixels) / spacing - .5d);
                if (!TryCount(firstCell, lastCell, out int cellCount)
                    || cellCount > MaximumWorkItems - workItems)
                    return false;
                for (int cell = 0; cell < cellCount; cell++)
                {
                    workItems++;
                    float midpoint = (float)(originAlong + (firstCell + cell + .5d) * spacing);
                    float availableLength = Mathf.Max(0f, 2f * Mathf.Min(midpoint - start, end - midpoint));
                    if (availableLength + LayoutTolerancePixels >= barLength)
                    {
                        if (destination.Count >= MaximumPatternLines)
                            return false;
                        AddSegment(destination, direction, normal, offset, midpoint, barLength);
                    }
                    else if (availableLength >= minimumFallbackLength)
                    {
                        var candidate = MakeSegment(direction, normal, offset, midpoint, availableLength);
                        KeepFallback(ref fallback, candidate);
                    }
                }
            }
            return true;
        }

        private static void KeepFallback(ref FallbackSegments fallback,
            StaticScanLineSegment candidate)
        {
            if (fallback.Count < MaximumFallbackLines)
            {
                fallback.Add(candidate);
                return;
            }
            int shortest = 0;
            float shortestLength = (fallback[0].End - fallback[0].Start).sqrMagnitude;
            for (int index = 1; index < fallback.Count; index++)
            {
                float length = (fallback[index].End - fallback[index].Start).sqrMagnitude;
                if (length < shortestLength - LayoutTolerancePixels)
                {
                    shortest = index;
                    shortestLength = length;
                }
            }
            if ((candidate.End - candidate.Start).sqrMagnitude > shortestLength + LayoutTolerancePixels)
                fallback[shortest] = candidate;
        }

        private static void AddSegment(List<StaticScanLineSegment> destination,
            Vector2 direction, Vector2 normal, float offset, float midpoint, float length)
        {
            destination.Add(MakeSegment(direction, normal, offset, midpoint, length));
        }

        private static StaticScanLineSegment MakeSegment(Vector2 direction, Vector2 normal,
            float offset, float midpoint, float length)
        {
            Vector2 center = normal * offset + direction * midpoint;
            Vector2 extent = direction * (length * .5f);
            return new StaticScanLineSegment(center - extent, center + extent);
        }

        private static bool TryGetSafeInterval(List<Vector2> polygon, Vector2 direction,
            Vector2 normal, float offset, float winding, float halfWidthWithPadding,
            out float start, out float end)
        {
            double minimum = double.NegativeInfinity;
            double maximum = double.PositiveInfinity;
            Vector2 previous = polygon[polygon.Count - 1];
            foreach (Vector2 point in polygon)
            {
                Vector2 edge = point - previous;
                double alongCoefficient = winding * Cross(edge, direction);
                double normalCoefficient = winding * Cross(edge, normal);
                double boundary = winding * Cross(edge, previous)
                    + Math.Abs(alongCoefficient) * (AntialiasPaddingPixels + LayoutTolerancePixels)
                    + Math.Abs(normalCoefficient) * halfWidthWithPadding;
                double remainder = boundary - normalCoefficient * offset;
                if (Math.Abs(alongCoefficient) < IntersectionEpsilon)
                {
                    if (remainder > IntersectionEpsilon)
                    {
                        start = end = 0f;
                        return false;
                    }
                }
                else if (alongCoefficient > 0d)
                    minimum = Math.Max(minimum, remainder / alongCoefficient);
                else
                    maximum = Math.Min(maximum, remainder / alongCoefficient);
                previous = point;
            }
            start = (float)minimum;
            end = (float)maximum;
            return IsFinite(start) && IsFinite(end) && end - start > IntersectionEpsilon;
        }

        private static bool TryCount(double first, double last, out int count)
        {
            count = 0;
            if (double.IsNaN(first) || double.IsNaN(last)
                || double.IsInfinity(first) || double.IsInfinity(last))
                return false;
            double range = last - first + 1d;
            if (range > MaximumWorkItems)
                return false;
            if (range > 0d)
                count = (int)range;
            return true;
        }

        // This lives on the stack for each Build call. Static buffers would share
        // mutable state between targets, and a tiny List would allocate each frame.
        private struct FallbackSegments
        {
            private StaticScanLineSegment first;
            private StaticScanLineSegment second;
            private StaticScanLineSegment third;
            private StaticScanLineSegment fourth;

            public int Count { get; private set; }

            public StaticScanLineSegment this[int index]
            {
                get
                {
                    switch (index)
                    {
                        case 0: return first;
                        case 1: return second;
                        case 2: return third;
                        default: return fourth;
                    }
                }
                set
                {
                    switch (index)
                    {
                        case 0: first = value; break;
                        case 1: second = value; break;
                        case 2: third = value; break;
                        default: fourth = value; break;
                    }
                }
            }

            public void Add(StaticScanLineSegment segment)
            {
                this[Count] = segment;
                Count++;
            }
        }

        private static double Cross(Vector2 a, Vector2 b) => (double)a.x * b.y - (double)a.y * b.x;
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
