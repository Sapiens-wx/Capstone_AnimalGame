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
    /// Builds one family of continuous, finite, square-ended parallel bars.
    /// The complete bar and its antialias padding fit inside the convex footprint.
    /// </summary>
    public static class BioScanStaticLinePattern
    {
        private const float IntersectionEpsilon = 0.0001f;
        private const float LayoutTolerancePixels = 0.01f;
        private const float AntialiasPaddingPixels = 1f;
        private const int MaximumPatternLines = 15000;
        private const int OutlineSmoothingPasses = 2;
        private const float DefaultOutlineSmoothing = .18f;

        /// <summary>
        /// Fills a small target with a few readable bars, including short edge
        /// bars. This runs in the fixed normal-view reference space, never in
        /// the current camera projection. Very small targets may fit fewer rows.
        /// </summary>
        public static bool BuildSmallArea(List<Vector2> footprintOffsetsPixels, float preferredSpacingPixels,
            float lineAngleDegrees, List<StaticScanLineSegment> destination, float lineWidthPixels = .9f,
            float outlineSmoothing = DefaultOutlineSmoothing)
        {
            if (destination == null) return false;
            destination.Clear();
            if (footprintOffsetsPixels == null || footprintOffsetsPixels.Count < 3
                || !IsFinite(preferredSpacingPixels) || preferredSpacingPixels <= 0f
                || !IsFinite(lineAngleDegrees) || !IsFinite(lineWidthPixels) || lineWidthPixels <= 0f)
                return false;
            if (!TryPrepareFootprint(footprintOffsetsPixels, outlineSmoothing, out List<Vector2> footprint))
                return false;

            float angle = lineAngleDegrees * Mathf.Deg2Rad;
            Vector2 normal = new Vector2(-Mathf.Sin(angle), Mathf.Cos(angle));
            float minimum = float.PositiveInfinity, maximum = float.NegativeInfinity;
            foreach (Vector2 point in footprintOffsetsPixels)
            {
                if (!IsFinite(point.x) || !IsFinite(point.y)) return false;
                float coordinate = Vector2.Dot(point, normal);
                minimum = Mathf.Min(minimum, coordinate);
                maximum = Mathf.Max(maximum, coordinate);
            }

            Vector2 origin = normal * ((minimum + maximum) * .5f);
            return BuildSmallAreaPrepared(footprintOffsetsPixels, footprint, preferredSpacingPixels,
                lineAngleDegrees, destination, lineWidthPixels, out _, origin);
        }

        public static bool Build(List<Vector2> footprintOffsetsPixels, float lineSpacingPixels,
            float lineAngleDegrees, List<StaticScanLineSegment> destination,
            float lineWidthPixels = .9f, Vector2 latticeOrigin = default, float minimumLineLengthPixels = -1f,
            float outlineSmoothing = DefaultOutlineSmoothing)
        {
            if (destination == null)
                return false;
            destination.Clear();
            if (!TryPrepareFootprint(footprintOffsetsPixels, outlineSmoothing, out List<Vector2> footprint))
                return false;
            return BuildPrepared(footprint, lineSpacingPixels, lineAngleDegrees, destination,
                lineWidthPixels, latticeOrigin, minimumLineLengthPixels);
        }

        /// <summary>
        /// Builds the unsmoothed convex boundary of the actual rendered point
        /// centers. Only those centers define this envelope, not the source hull.
        /// </summary>
        public static bool BuildPointBoundaryHull(List<Vector2> actualDotCenters, List<Vector2> hullDestination)
        {
            if (hullDestination == null) return false;
            if (actualDotCenters == null || actualDotCenters.Count < 3)
            {
                hullDestination.Clear();
                return false;
            }
            // Copy before clearing so callers can also build the hull in place.
            var sorted = new List<Vector2>(actualDotCenters);
            hullDestination.Clear();
            foreach (Vector2 point in sorted)
                if (!IsFiniteBoundaryPoint(point)) return false;
            sorted.Sort(CompareBoundaryPoints);
            int uniqueCount = 0;
            for (int i = 0; i < sorted.Count; i++)
            {
                Vector2 point = sorted[i];
                if (uniqueCount > 0)
                {
                    Vector2 previous = sorted[uniqueCount - 1];
                    double x = (double)point.x - previous.x, y = (double)point.y - previous.y;
                    if (x * x + y * y <= IntersectionEpsilon * IntersectionEpsilon) continue;
                }
                sorted[uniqueCount++] = point;
            }
            if (uniqueCount < 3) return false;
            for (int i = 0; i < uniqueCount; i++)
            {
                Vector2 point = sorted[i];
                while (hullDestination.Count >= 2 && BoundaryTurn(
                    hullDestination[hullDestination.Count - 2], hullDestination[hullDestination.Count - 1], point) <= 0d)
                    hullDestination.RemoveAt(hullDestination.Count - 1);
                hullDestination.Add(point);
            }
            int lowerCount = hullDestination.Count;
            for (int i = uniqueCount - 2; i >= 0; i--)
            {
                Vector2 point = sorted[i];
                while (hullDestination.Count > lowerCount && BoundaryTurn(
                    hullDestination[hullDestination.Count - 2], hullDestination[hullDestination.Count - 1], point) <= 0d)
                    hullDestination.RemoveAt(hullDestination.Count - 1);
                hullDestination.Add(point);
            }
            hullDestination.RemoveAt(hullDestination.Count - 1);
            if (TryGetBoundaryWinding(hullDestination, out _, 0d)) return true;
            hullDestination.Clear();
            return false;
        }

        /// <summary>
        /// Shortens existing finite bars to the point boundary. The whole square
        /// cap, stroke and one-pixel antialias rectangle retain the requested
        /// clearance inside every boundary edge. Rows are never regenerated.
        /// </summary>
        public static bool ClipToBoundary(List<StaticScanLineSegment> source, List<Vector2> convexBoundary,
            List<StaticScanLineSegment> destination, float lineWidthPixels = .9f,
            float clearancePixels = 1f, float minimumLengthPixels = .25f)
        {
            if (destination == null) return false;
            List<StaticScanLineSegment> input = ReferenceEquals(source, destination)
                ? new List<StaticScanLineSegment>(source) : source;
            destination.Clear();
            if (input == null || input.Count == 0
                || !IsFinite(lineWidthPixels) || lineWidthPixels <= 0f
                || !IsFinite(clearancePixels) || clearancePixels < 0f
                || !IsFinite(minimumLengthPixels) || minimumLengthPixels < 0f
                || !TryGetBoundaryWinding(convexBoundary, out double winding, LayoutTolerancePixels))
                return false;
            foreach (StaticScanLineSegment segment in input)
                if (!IsFiniteBoundaryPoint(segment.Start) || !IsFiniteBoundaryPoint(segment.End)) return false;

            double outerHalfWidth = Math.Max(.5d, lineWidthPixels) * .5d + AntialiasPaddingPixels;
            foreach (StaticScanLineSegment segment in input)
            {
                double deltaX = (double)segment.End.x - segment.Start.x;
                double deltaY = (double)segment.End.y - segment.Start.y;
                double length = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
                if (length <= IntersectionEpsilon) continue;
                double directionX = deltaX / length, directionY = deltaY / length;
                double normalX = -directionY, normalY = directionX;
                double first = 0d, last = length;
                Vector2 previous = convexBoundary[convexBoundary.Count - 1];
                foreach (Vector2 point in convexBoundary)
                {
                    double edgeX = (double)point.x - previous.x, edgeY = (double)point.y - previous.y;
                    double edgeLength = Math.Sqrt(edgeX * edgeX + edgeY * edgeY);
                    if (edgeLength <= IntersectionEpsilon)
                    {
                        previous = point;
                        continue;
                    }
                    double inwardX = -edgeY / edgeLength * winding;
                    double inwardY = edgeX / edgeLength * winding;
                    double along = inwardX * directionX + inwardY * directionY;
                    double across = inwardX * normalX + inwardY * normalY;
                    double support = Math.Abs(along) * AntialiasPaddingPixels
                        + Math.Abs(across) * outerHalfWidth + clearancePixels + LayoutTolerancePixels;
                    double distance = inwardX * ((double)segment.Start.x - previous.x)
                        + inwardY * ((double)segment.Start.y - previous.y) - support;
                    if (Math.Abs(along) < 1e-12d)
                    {
                        if (distance < 0d)
                        {
                            first = 1d;
                            last = 0d;
                            break;
                        }
                    }
                    else if (along > 0d) first = Math.Max(first, -distance / along);
                    else last = Math.Min(last, -distance / along);
                    if (last < first) break;
                    previous = point;
                }
                if (last - first < Math.Max(IntersectionEpsilon, minimumLengthPixels)) continue;
                Vector2 start = new Vector2((float)(segment.Start.x + directionX * first),
                    (float)(segment.Start.y + directionY * first));
                Vector2 end = new Vector2((float)(segment.Start.x + directionX * last),
                    (float)(segment.Start.y + directionY * last));
                if (!IsFiniteBoundaryPoint(start) || !IsFiniteBoundaryPoint(end))
                {
                    destination.Clear();
                    return false;
                }
                if (DistanceSquared(start, end) < (double)minimumLengthPixels * minimumLengthPixels
                    || DistanceSquared(start, end) <= IntersectionEpsilon * IntersectionEpsilon)
                    continue;
                destination.Add(new StaticScanLineSegment(start, end));
            }
            return destination.Count > 0;
        }

        /// <summary>Builds against a cached, already rounded reference-space hull.</summary>
        internal static bool BuildPrepared(List<Vector2> footprintOffsetsPixels, float lineSpacingPixels,
            float lineAngleDegrees, List<StaticScanLineSegment> destination,
            float lineWidthPixels, Vector2 latticeOrigin, float minimumLineLengthPixels)
        {
            if (destination == null)
                return false;
            destination.Clear();
            if (footprintOffsetsPixels == null || footprintOffsetsPixels.Count < 3
                || !IsFinite(lineSpacingPixels) || !IsFinite(lineAngleDegrees)
                || !IsFinite(lineWidthPixels) || !IsFinite(minimumLineLengthPixels)
                || !IsFinite(latticeOrigin.x) || !IsFinite(latticeOrigin.y)
                || lineSpacingPixels <= 0f || lineWidthPixels <= 0f)
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

            float angle = lineAngleDegrees * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 normal = new Vector2(-direction.y, direction.x);
            float winding = twiceArea > 0d ? 1f : -1f;
            float halfWidthWithPadding = lineWidthPixels * .5f + AntialiasPaddingPixels
                + LayoutTolerancePixels;
            float minimum = float.PositiveInfinity, maximum = float.NegativeInfinity;
            foreach (Vector2 point in footprintOffsetsPixels)
            {
                float coordinate = Vector2.Dot(point, normal);
                minimum = Mathf.Min(minimum, coordinate);
                maximum = Mathf.Max(maximum, coordinate);
            }
            double originNormal = Vector2.Dot(latticeOrigin, normal);
            double firstRow = Math.Ceiling((minimum - originNormal - LayoutTolerancePixels) / lineSpacingPixels);
            double lastRow = Math.Floor((maximum - originNormal + LayoutTolerancePixels) / lineSpacingPixels);
            if (!TryCount(firstRow, lastRow, out int rowCount)) return false;
            float minimumLength = minimumLineLengthPixels < 0f
                ? Mathf.Max(3f, lineWidthPixels * 3f) : Mathf.Max(0f, minimumLineLengthPixels);
            for (int row = 0; row < rowCount; row++)
            {
                float rowOffset = (float)(originNormal + (firstRow + row) * lineSpacingPixels);
                if (!TryGetSafeInterval(footprintOffsetsPixels, direction, normal,
                        rowOffset, winding, halfWidthWithPadding, out float rowStart, out float rowEnd))
                    continue;
                float rowLength = rowEnd - rowStart;
                // Drop marginal slivers instead of drawing torn mask fragments.
                // The remaining rows are each a single complete continuous bar.
                if (rowLength + LayoutTolerancePixels < minimumLength) continue;
                AddSegment(destination, direction, normal, rowOffset, (rowStart + rowEnd) * .5f, rowLength);
            }
            if (destination.Count > 0) return true;

            // Small footprints retain one readable complete interior line.
            float offset = Vector2.Dot(centroid, normal);
            if (!TryGetSafeInterval(footprintOffsetsPixels, direction, normal,
                    offset, winding, halfWidthWithPadding, out float start, out float end))
                return false;
            float length = end - start;
            if (length <= IntersectionEpsilon)
                return false;
            AddSegment(destination, direction, normal, offset, (start + end) * .5f, length);
            return true;
        }

        /// <summary>
        /// Chooses the small-area spacing once against the original hull, then
        /// clips complete bars inside the cached rounded hull. Callers can keep
        /// the returned spacing while changing direction without repacking rows.
        /// </summary>
        internal static bool BuildSmallAreaPrepared(List<Vector2> original, List<Vector2> prepared,
            float preferredSpacingPixels, float lineAngleDegrees, List<StaticScanLineSegment> destination,
            float lineWidthPixels, out float appliedSpacing, Vector2 origin)
        {
            appliedSpacing = 0f;
            if (destination == null) return false;
            destination.Clear();
            if (original == null || original.Count < 3 || prepared == null || prepared.Count < 3
                || !IsFinite(preferredSpacingPixels) || preferredSpacingPixels <= 0f
                || !IsFinite(lineAngleDegrees) || !IsFinite(lineWidthPixels) || lineWidthPixels <= 0f
                || !IsFinite(origin.x) || !IsFinite(origin.y))
                return false;

            float angle = lineAngleDegrees * Mathf.Deg2Rad;
            Vector2 normal = new Vector2(-Mathf.Sin(angle), Mathf.Cos(angle));
            float minimum = float.PositiveInfinity, maximum = float.NegativeInfinity;
            foreach (Vector2 point in original)
            {
                if (!IsFinite(point.x) || !IsFinite(point.y)) return false;
                float coordinate = Vector2.Dot(point, normal);
                minimum = Mathf.Min(minimum, coordinate);
                maximum = Mathf.Max(maximum, coordinate);
            }

            // Preserve the previous spacing selection, including its complete
            // short-bar fallback, while letting the caller choose a stable origin.
            float safeSpacing = lineWidthPixels + 2f * AntialiasPaddingPixels + .1f;
            float usableSpan = Mathf.Max(0f, maximum - minimum - safeSpacing);
            appliedSpacing = Mathf.Max(safeSpacing, Mathf.Max(preferredSpacingPixels, usableSpan / 5f));
            bool built = BuildPrepared(prepared, appliedSpacing, lineAngleDegrees,
                destination, lineWidthPixels, origin, 4f);
            if (destination.Count >= 3 || appliedSpacing <= safeSpacing) return built;

            appliedSpacing = safeSpacing;
            return BuildPrepared(prepared, appliedSpacing, lineAngleDegrees,
                destination, lineWidthPixels, origin, 4f);
        }

        /// <summary>Prepares a reusable hull when its actual geometry changes.</summary>
        internal static bool PrepareFootprint(List<Vector2> source, List<Vector2> destination,
            float smoothing = DefaultOutlineSmoothing)
        {
            if (destination == null) return false;
            bool prepared = TryPrepareFootprint(source, smoothing, out List<Vector2> footprint);
            destination.Clear();
            if (prepared) destination.AddRange(footprint);
            return prepared;
        }

        private static bool TryPrepareFootprint(List<Vector2> source, float smoothing,
            out List<Vector2> footprint)
        {
            footprint = null;
            if (source == null || source.Count < 3 || !IsFinite(smoothing)
                || smoothing < 0f || smoothing >= .5f)
                return false;

            var cleaned = new List<Vector2>(source.Count);
            foreach (Vector2 point in source)
            {
                if (!IsFinite(point.x) || !IsFinite(point.y)) return false;
                if (cleaned.Count == 0
                    || (point - cleaned[cleaned.Count - 1]).sqrMagnitude > IntersectionEpsilon * IntersectionEpsilon)
                    cleaned.Add(point);
            }
            if (cleaned.Count > 1 && (cleaned[0] - cleaned[cleaned.Count - 1]).sqrMagnitude
                <= IntersectionEpsilon * IntersectionEpsilon)
                cleaned.RemoveAt(cleaned.Count - 1);
            if (cleaned.Count < 3) return false;

            // Both windings are supported. Collinear edges and repeated source
            // vertices are harmless, but a concave outline is not a scan hull.
            double turnSign = 0d;
            for (int i = 0; i < cleaned.Count; i++)
            {
                Vector2 incoming = cleaned[(i + 1) % cleaned.Count] - cleaned[i];
                Vector2 outgoing = cleaned[(i + 2) % cleaned.Count] - cleaned[(i + 1) % cleaned.Count];
                double turn = Cross(incoming, outgoing);
                if (Math.Abs(turn) <= IntersectionEpsilon) continue;
                if (turnSign == 0d) turnSign = Math.Sign(turn);
                else if (turn * turnSign < 0d) return false;
            }
            if (turnSign == 0d) return false;

            // Every cut point is a convex combination of an existing edge's
            // endpoints. Two passes soften the envelope without expanding the
            // real object area; the normal-view line lattice remains unchanged.
            footprint = cleaned;
            if (smoothing == 0f) return true;
            for (int pass = 0; pass < OutlineSmoothingPasses; pass++)
            {
                var rounded = new List<Vector2>(footprint.Count * 2);
                Vector2 previous = footprint[footprint.Count - 1];
                foreach (Vector2 point in footprint)
                {
                    rounded.Add(Vector2.LerpUnclamped(previous, point, smoothing));
                    rounded.Add(Vector2.LerpUnclamped(previous, point, 1f - smoothing));
                    previous = point;
                }
                footprint = rounded;
            }
            return true;
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
            if (range > MaximumPatternLines)
                return false;
            if (range > 0d)
                count = (int)range;
            return true;
        }

        private static bool TryGetBoundaryWinding(List<Vector2> polygon, out double winding,
            double collinearDistanceTolerance)
        {
            winding = 0d;
            if (polygon == null || polygon.Count < 3) return false;
            Vector2 origin = polygon[0], previous = polygon[polygon.Count - 1];
            if (!IsFiniteBoundaryPoint(origin) || !IsFiniteBoundaryPoint(previous)) return false;
            double twiceArea = 0d, turnSum = 0d;
            double firstX = 0d, firstY = 0d, previousX = 0d, previousY = 0d;
            int edgeCount = 0, turnSign = 0;
            foreach (Vector2 point in polygon)
            {
                if (!IsFiniteBoundaryPoint(point)) return false;
                twiceArea += ((double)previous.x - origin.x) * ((double)point.y - origin.y)
                    - ((double)previous.y - origin.y) * ((double)point.x - origin.x);
                double edgeX = (double)point.x - previous.x, edgeY = (double)point.y - previous.y;
                previous = point;
                if (edgeX * edgeX + edgeY * edgeY <= IntersectionEpsilon * IntersectionEpsilon) continue;
                if (edgeCount == 0)
                {
                    firstX = edgeX;
                    firstY = edgeY;
                }
                else if (!AccumulateBoundaryTurn(previousX, previousY, edgeX, edgeY,
                    ref turnSign, ref turnSum, collinearDistanceTolerance))
                    return false;
                previousX = edgeX;
                previousY = edgeY;
                edgeCount++;
            }
            if (edgeCount < 3 || Math.Abs(twiceArea) <= IntersectionEpsilon
                || !AccumulateBoundaryTurn(previousX, previousY, firstX, firstY,
                    ref turnSign, ref turnSum, collinearDistanceTolerance)
                || Math.Abs(Math.Abs(turnSum) - 2d * Math.PI) > .001d)
                return false;
            winding = twiceArea > 0d ? 1d : -1d;
            return turnSign == (int)winding;
        }

        private static bool AccumulateBoundaryTurn(double firstX, double firstY, double secondX, double secondY,
            ref int sign, ref double total, double collinearDistanceTolerance)
        {
            double cross = firstX * secondY - firstY * secondX;
            double dot = firstX * secondX + firstY * secondY;
            double relativeTolerance = Math.Sqrt((firstX * firstX + firstY * firstY)
                * (secondX * secondX + secondY * secondY)) * 1e-10d;
            double chordX = firstX + secondX, chordY = firstY + secondY;
            // A cached convex world hull can acquire tiny negative turns when
            // its collinear vertices are projected back to screen floats. Test
            // the middle vertex's distance from the outer chord using the
            // caller's units. World hull construction passes zero; screen bar
            // clipping uses its pixel tolerance and still constrains every edge.
            double tolerance = Math.Max(relativeTolerance,
                collinearDistanceTolerance * Math.Sqrt(chordX * chordX + chordY * chordY));
            if (Math.Abs(cross) <= tolerance)
            {
                if (dot < 0d) return false;
            }
            else
            {
                int current = cross > 0d ? 1 : -1;
                if (sign != 0 && sign != current) return false;
                sign = current;
            }
            total += Math.Atan2(cross, dot);
            return true;
        }

        private static int CompareBoundaryPoints(Vector2 first, Vector2 second)
        {
            int horizontal = first.x.CompareTo(second.x);
            return horizontal != 0 ? horizontal : first.y.CompareTo(second.y);
        }

        private static double BoundaryTurn(Vector2 first, Vector2 second, Vector2 third)
            => ((double)second.x - first.x) * ((double)third.y - second.y)
                - ((double)second.y - first.y) * ((double)third.x - second.x);

        private static double DistanceSquared(Vector2 first, Vector2 second)
        {
            double x = (double)second.x - first.x, y = (double)second.y - first.y;
            return x * x + y * y;
        }

        private static bool IsFiniteBoundaryPoint(Vector2 point)
            => IsFinite(point.x) && IsFinite(point.y)
                && Math.Abs((double)point.x) <= float.MaxValue * .25d
                && Math.Abs((double)point.y) <= float.MaxValue * .25d;

        private static double Cross(Vector2 a, Vector2 b) => (double)a.x * b.y - (double)a.y * b.x;
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
