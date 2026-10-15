using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.RobotMap
{
    /// <summary>
    /// Builds a quiet dashed perimeter in fixed normal-view reference pixels.
    /// Each dash is one complete bar rather than overlapping polyline joins.
    /// </summary>
    public static class BioScanStaticOutlinePattern
    {
        private const float OutlineSmoothing = .18f;
        private const float AntialiasPaddingPixels = 1f;
        private const float TolerancePixels = .01f;
        private const int MaximumOutlineDashes = 15000;
        private const int MaximumLayoutAttempts = 12;

        public static bool Build(List<Vector2> footprintOffsetsPixels, float dashLengthPixels,
            float gapLengthPixels, List<StaticScanLineSegment> destination, float lineWidthPixels = .9f,
            uint seed = 0)
        {
            if (destination == null) return false;
            destination.Clear();
            if (!IsFinite(dashLengthPixels) || !IsFinite(gapLengthPixels) || !IsFinite(lineWidthPixels)
                || dashLengthPixels <= 0f || gapLengthPixels <= 0f || lineWidthPixels <= 0f)
                return false;
            if (footprintOffsetsPixels == null || footprintOffsetsPixels.Count < 3) return false;
            foreach (Vector2 point in footprintOffsetsPixels)
            {
                if (!IsFinite(point.x) || !IsFinite(point.y)
                    || Mathf.Abs(point.x) > float.MaxValue * .25f
                    || Mathf.Abs(point.y) > float.MaxValue * .25f)
                    return false;
            }

            var rounded = new List<Vector2>();
            if (!BioScanStaticLinePattern.PrepareFootprint(footprintOffsetsPixels, rounded, OutlineSmoothing))
                return false;
            Canonicalize(rounded);

            // This disk encloses the complete bar's width and its square-cap
            // antialias padding. Its inward offset keeps every emitted rectangle
            // inside the real hull, including dashes that span a rounded corner.
            double outerHalfWidth = lineWidthPixels * .5d + AntialiasPaddingPixels;
            double inset = Math.Sqrt(outerHalfWidth * outerHalfWidth
                + AntialiasPaddingPixels * AntialiasPaddingPixels) + TolerancePixels;
            if (!TryInset(rounded, inset, out List<Vector2> perimeter)) return false;
            Canonicalize(perimeter);
            // Opposite sides of a very narrow footprint can touch even when
            // consecutive dashes have good gaps. Omit an outline that cannot
            // keep its two visible strokes apart after the safe inward offset.
            if (!HasReadableThickness(perimeter, lineWidthPixels + 1d + .05d)) return false;

            var cumulativeLengths = new List<double>(perimeter.Count + 1) { 0d };
            double totalLength = 0d;
            for (int i = 0; i < perimeter.Count; i++)
            {
                totalLength += Distance(perimeter[i], perimeter[(i + 1) % perimeter.Count]);
                cumulativeLengths.Add(totalLength);
            }
            double requestedPeriod = (double)dashLengthPixels + gapLengthPixels;
            double requestedCount = totalLength / requestedPeriod;
            if (!IsFinite(totalLength) || totalLength <= TolerancePixels
                || !IsFinite(requestedCount) || requestedCount > MaximumOutlineDashes)
                return false;

            // Keep a genuine break even when a small target needs shorter bars.
            // Extremely tiny hulls may fit two dashes, or no safe outline at all.
            double minimumDash = Math.Max(.75d, lineWidthPixels);
            double minimumGap = 2d * AntialiasPaddingPixels + lineWidthPixels + .1d;
            double duty = dashLengthPixels / requestedPeriod;
            int capacity = (int)Math.Min(MaximumOutlineDashes,
                Math.Floor(Math.Min(totalLength / (minimumDash + minimumGap),
                    totalLength * duty / minimumDash)));
            if (capacity < 2) return false;
            int count = Math.Min(capacity, Math.Max(4, (int)Math.Floor(requestedCount + .5d)));
            if (seed != 0)
                return BuildRandomized(perimeter, cumulativeLengths, totalLength, count,
                    dashLengthPixels, gapLengthPixels, minimumDash, minimumGap, destination, lineWidthPixels, seed);

            // A whole number of equal periods closes the loop without one short
            // final dash. The seam is centered in a full-sized gap.
            for (int attempt = 0; count >= 2 && attempt < MaximumLayoutAttempts; count--, attempt++)
            {
                destination.Clear();
                double period = totalLength / count;
                double dashLength = Math.Min(dashLengthPixels, Math.Min(period * duty, period - minimumGap));
                double gapLength = period - dashLength;
                for (int dash = 0; dash < count; dash++)
                {
                    double startDistance = dash * period + gapLength * .5d;
                    Vector2 start = PointAtDistance(perimeter, cumulativeLengths, startDistance);
                    Vector2 end = PointAtDistance(perimeter, cumulativeLengths, startDistance + dashLength);
                    if (Distance(start, end) < minimumDash - TolerancePixels)
                        break;
                    destination.Add(new StaticScanLineSegment(start, end));
                }
                if (destination.Count == count && HasVisibleBreaks(destination, lineWidthPixels))
                    return true;
            }
            destination.Clear();
            return false;
        }

        /// <summary>
        /// Places separated diamond centers along the rounded real perimeter.
        /// All distances use the fixed normal-view reference space; callers keep
        /// these centers until the target geometry or scan seed changes.
        /// </summary>
        public static bool BuildDots(List<Vector2> footprintPixels, float spacingPixels,
            List<Vector2> destination, float dotSizePixels = 3.6f, uint seed = 0)
        {
            if (destination == null) return false;
            destination.Clear();
            if (footprintPixels == null || footprintPixels.Count < 3
                || !IsFinite(spacingPixels) || spacingPixels <= 0f
                || !IsFinite(dotSizePixels) || dotSizePixels <= 0f)
                return false;
            foreach (Vector2 point in footprintPixels)
            {
                if (!IsFinite(point.x) || !IsFinite(point.y)
                    || Mathf.Abs(point.x) > float.MaxValue * .25f
                    || Mathf.Abs(point.y) > float.MaxValue * .25f)
                    return false;
            }

            var rounded = new List<Vector2>();
            if (!BioScanStaticLinePattern.PrepareFootprint(footprintPixels, rounded, OutlineSmoothing))
                return false;
            Canonicalize(rounded);
            double side = dotSizePixels / Math.Sqrt(2d);
            if (side < .01d) return false;
            // The same square-bar primitive renders the diamond. Its circumscribed
            // disk protects every orientation, including the complete AA quad.
            double halfLength = side * .5d;
            double halfWidth = Math.Max(.5d, side) * .5d;
            double inset = Math.Sqrt((halfLength + AntialiasPaddingPixels)
                * (halfLength + AntialiasPaddingPixels) + (halfWidth + AntialiasPaddingPixels)
                * (halfWidth + AntialiasPaddingPixels)) + TolerancePixels;
            if (!TryInset(rounded, inset, out List<Vector2> perimeter)) return false;
            Canonicalize(perimeter);

            var cumulativeLengths = new List<double>(perimeter.Count + 1) { 0d };
            double totalLength = 0d;
            for (int i = 0; i < perimeter.Count; i++)
            {
                totalLength += Distance(perimeter[i], perimeter[(i + 1) % perimeter.Count]);
                cumulativeLengths.Add(totalLength);
            }
            double requestedCount = totalLength / spacingPixels;
            if (!IsFinite(totalLength) || totalLength <= TolerancePixels
                || !IsFinite(requestedCount) || requestedCount > MaximumOutlineDashes)
                return false;

            // The shader's visible fringe is half a pixel. A small safety gap
            // remains between the two complete visible glyph disks.
            double minimumSeparation = 2d * Math.Sqrt((halfLength + .5d) * (halfLength + .5d)
                + (halfWidth + .5d) * (halfWidth + .5d)) + .1d;
            int capacity = (int)Math.Min(MaximumOutlineDashes, Math.Floor(totalLength / minimumSeparation));
            if (capacity < 2) return AddSingleCenter(perimeter, destination);
            int count = Math.Min(capacity, Math.Max(4, (int)Math.Floor(requestedCount + .5d)));
            int initialCount = count;
            var intervals = new double[count];
            var occupied = new Dictionary<Vector2Int, List<int>>(count);
            for (int attempt = 0; attempt < MaximumLayoutAttempts; attempt++)
            {
                if (attempt > 0 && attempt % 4 == 0)
                    count = count > 6 ? Math.Max(2, count * 3 / 4) : count - 1;
                if (count < 2) break;
                destination.Clear();
                occupied.Clear();
                double period = totalLength / count;
                double phase;
                if (seed == 0)
                {
                    phase = period * (.5d + (attempt % 4) * .25d);
                    for (int i = 0; i < count; i++) intervals[i] = period;
                }
                else
                {
                    var random = new LocalRandom(unchecked(seed ^ ((uint)attempt + 17u) * 0x9e3779b9u));
                    phase = random.NextUnit() * totalLength;
                    double minimum = Math.Max(minimumSeparation, period * .84d);
                    double maximum = Math.Max(minimum, period * 1.16d);
                    for (int i = 0; i < count; i++)
                        intervals[i] = minimum + (maximum - minimum) * random.NextUnit();
                    FitIntervals(intervals, count, totalLength, minimum, maximum);
                }
                double position = phase;
                for (int i = 0; i < count; i++)
                {
                    Vector2 point = PointAtDistance(perimeter, cumulativeLengths, position);
                    if (!TryAddSeparatedPoint(point, minimumSeparation, occupied, destination)) break;
                    position += intervals[i];
                }
                if (destination.Count == count) return true;
            }
            destination.Clear();
            return initialCount <= 4 && AddSingleCenter(perimeter, destination);
        }

        /// <summary>
        /// Converts projected centers into square-bar glyphs. The supplied
        /// direction is the projected diamond edge direction; render these bars
        /// with width dotSizePixels / sqrt(2) to form solid diamonds.
        /// </summary>
        public static bool BuildDiamondSegments(List<Vector2> projectedPointOffsets,
            Vector2 projectedGlyphDirection, float dotSizePixels, List<StaticScanLineSegment> destination)
        {
            if (destination == null) return false;
            destination.Clear();
            if (projectedPointOffsets == null || projectedPointOffsets.Count == 0
                || !IsFinite(projectedGlyphDirection.x) || !IsFinite(projectedGlyphDirection.y)
                || !IsFinite(dotSizePixels) || dotSizePixels <= 0f)
                return false;
            double directionLength = Math.Sqrt((double)projectedGlyphDirection.x * projectedGlyphDirection.x
                + (double)projectedGlyphDirection.y * projectedGlyphDirection.y);
            double side = dotSizePixels / Math.Sqrt(2d);
            if (directionLength < .000001d || side < .01d) return false;
            Vector2 extent = new Vector2((float)(projectedGlyphDirection.x / directionLength * side * .5d),
                (float)(projectedGlyphDirection.y / directionLength * side * .5d));
            foreach (Vector2 point in projectedPointOffsets)
            {
                if (!IsFinite(point.x) || !IsFinite(point.y))
                {
                    destination.Clear();
                    return false;
                }
                Vector2 start = point - extent, end = point + extent;
                float squaredLength = (end - start).sqrMagnitude;
                if (!IsFinite(start.x) || !IsFinite(start.y) || !IsFinite(end.x) || !IsFinite(end.y)
                    || !IsFinite(squaredLength) || squaredLength < .0001f)
                {
                    destination.Clear();
                    return false;
                }
                destination.Add(new StaticScanLineSegment(start, end));
            }
            return true;
        }

        private static bool AddSingleCenter(List<Vector2> perimeter, List<Vector2> destination)
        {
            destination.Clear();
            Vector2 center = Vector2.zero;
            foreach (Vector2 point in perimeter) center += point / perimeter.Count;
            if (!IsFinite(center.x) || !IsFinite(center.y)) return false;
            destination.Add(center);
            return true;
        }

        private static bool TryAddSeparatedPoint(Vector2 point, double separation,
            Dictionary<Vector2Int, List<int>> occupied, List<Vector2> destination)
        {
            double cellX = Math.Floor(point.x / separation), cellY = Math.Floor(point.y / separation);
            if (cellX < int.MinValue + 1d || cellX > int.MaxValue - 1d
                || cellY < int.MinValue + 1d || cellY > int.MaxValue - 1d)
                return false;
            var cell = new Vector2Int((int)cellX, (int)cellY);
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                if (!occupied.TryGetValue(new Vector2Int(cell.x + x, cell.y + y), out List<int> neighbors)) continue;
                foreach (int index in neighbors)
                    if (Distance(point, destination[index]) < separation - TolerancePixels) return false;
            }
            if (!occupied.TryGetValue(cell, out List<int> bucket))
            {
                bucket = new List<int>(2);
                occupied.Add(cell, bucket);
            }
            bucket.Add(destination.Count);
            destination.Add(point);
            return true;
        }

        private static void FitIntervals(double[] intervals, int count, double perimeter, double minimum, double maximum)
        {
            double low = minimum - maximum, high = maximum - minimum;
            for (int iteration = 0; iteration < 40; iteration++)
            {
                double correction = (low + high) * .5d, total = 0d;
                for (int i = 0; i < count; i++) total += Clamp(intervals[i] + correction, minimum, maximum);
                if (total < perimeter) low = correction;
                else high = correction;
            }
            double finalCorrection = (low + high) * .5d, length = 0d;
            for (int i = 0; i < count; i++)
            {
                intervals[i] = Clamp(intervals[i] + finalCorrection, minimum, maximum);
                length += intervals[i];
            }
            intervals[count - 1] += perimeter - length;
        }

        private static bool BuildRandomized(List<Vector2> perimeter, List<double> cumulativeLengths,
            double totalLength, int initialCount, double requestedDash, double requestedGap,
            double minimumDash, double minimumGap, List<StaticScanLineSegment> destination, float width, uint seed)
        {
            var dashLengths = new double[initialCount];
            var gapLengths = new double[initialCount];
            for (int attempt = 0; attempt < MaximumLayoutAttempts; attempt++)
            {
                // Try several phases before reducing the number of dashes. The
                // fixed attempt limit also bounds pathological large-hull work.
                int count = initialCount - attempt / 4;
                if (count < 2) break;
                double period = totalLength / count;
                double smallScale = Math.Min(1d, Math.Max(0d, (period - minimumGap) / requestedDash));
                double dashMinimum = Math.Max(minimumDash, requestedDash * (2d / 3d) * smallScale);
                double dashMaximum = Math.Max(dashMinimum, requestedDash * (4d / 3d) * smallScale);
                double gapMinimum = Math.Max(minimumGap, requestedGap * .75d);
                double gapMaximum = Math.Max(gapMinimum, requestedGap * 1.375d);
                if (totalLength < count * (dashMinimum + gapMinimum)) continue;
                if (totalLength > count * (dashMaximum + gapMaximum)) break;

                var random = new LocalRandom(unchecked(seed ^ ((uint)attempt + 1u) * 0x9e3779b9u));
                double phase = random.NextUnit() * totalLength;
                for (int i = 0; i < count; i++)
                {
                    dashLengths[i] = dashMinimum + (dashMaximum - dashMinimum) * random.NextUnit();
                    gapLengths[i] = gapMinimum + (gapMaximum - gapMinimum) * random.NextUnit();
                }
                FitClosedLoop(dashLengths, gapLengths, count, totalLength,
                    dashMinimum, dashMaximum, gapMinimum, gapMaximum);

                destination.Clear();
                double position = phase;
                for (int i = 0; i < count; i++)
                {
                    Vector2 start = PointAtDistance(perimeter, cumulativeLengths, position);
                    Vector2 end = PointAtDistance(perimeter, cumulativeLengths, position + dashLengths[i]);
                    if (Distance(start, end) < minimumDash - TolerancePixels) break;
                    destination.Add(new StaticScanLineSegment(start, end));
                    position += dashLengths[i] + gapLengths[i];
                }
                if (destination.Count == count && HasVisibleBreaks(destination, width)) return true;
            }
            destination.Clear();
            return false;
        }

        private static void FitClosedLoop(double[] dashes, double[] gaps, int count, double perimeter,
            double dashMinimum, double dashMaximum, double gapMinimum, double gapMaximum)
        {
            // Correct every interval by the same amount, clamped to its useful
            // range, rather than collecting the residual in a conspicuous seam.
            double low = Math.Min(dashMinimum - dashMaximum, gapMinimum - gapMaximum);
            double high = -low;
            for (int iteration = 0; iteration < 40; iteration++)
            {
                double correction = (low + high) * .5d;
                double length = 0d;
                for (int i = 0; i < count; i++)
                    length += Clamp(dashes[i] + correction, dashMinimum, dashMaximum)
                        + Clamp(gaps[i] + correction, gapMinimum, gapMaximum);
                if (length < perimeter) low = correction;
                else high = correction;
            }
            double finalCorrection = (low + high) * .5d;
            double total = 0d;
            for (int i = 0; i < count; i++)
            {
                dashes[i] = Clamp(dashes[i] + finalCorrection, dashMinimum, dashMaximum);
                gaps[i] = Clamp(gaps[i] + finalCorrection, gapMinimum, gapMaximum);
                total += dashes[i] + gaps[i];
            }
            // Only double-precision rounding remains after the shared correction.
            gaps[count - 1] += perimeter - total;
        }

        private struct LocalRandom
        {
            private uint state;

            public LocalRandom(uint seed)
            {
                state = seed != 0u ? seed : 0x6d2b79f5u;
            }

            public double NextUnit()
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                return (state >> 8) / 16777216d;
            }
        }

        private static bool TryInset(List<Vector2> polygon, double inset, out List<Vector2> result)
        {
            result = new List<Vector2>(polygon);
            var scratch = new List<Vector2>(polygon.Count);
            for (int edgeIndex = 0; edgeIndex < polygon.Count; edgeIndex++)
            {
                Vector2 edgeStart = polygon[edgeIndex];
                Vector2 edgeEnd = polygon[(edgeIndex + 1) % polygon.Count];
                double edgeX = (double)edgeEnd.x - edgeStart.x;
                double edgeY = (double)edgeEnd.y - edgeStart.y;
                double length = Math.Sqrt(edgeX * edgeX + edgeY * edgeY);
                if (length <= TolerancePixels * .001d) continue;
                double normalX = -edgeY / length;
                double normalY = edgeX / length;
                double boundary = normalX * edgeStart.x + normalY * edgeStart.y + inset;
                scratch.Clear();
                Vector2 previous = result[result.Count - 1];
                double previousDistance = normalX * previous.x + normalY * previous.y - boundary;
                foreach (Vector2 point in result)
                {
                    double distance = normalX * point.x + normalY * point.y - boundary;
                    bool previousInside = previousDistance >= 0d;
                    bool inside = distance >= 0d;
                    if (previousInside != inside)
                    {
                        double fraction = previousDistance / (previousDistance - distance);
                        scratch.Add(new Vector2(
                            (float)(previous.x + ((double)point.x - previous.x) * fraction),
                            (float)(previous.y + ((double)point.y - previous.y) * fraction)));
                    }
                    if (inside) scratch.Add(point);
                    previous = point;
                    previousDistance = distance;
                }
                List<Vector2> swap = result;
                result = scratch;
                scratch = swap;
                if (result.Count < 3) return false;
            }
            return true;
        }

        private static bool HasReadableThickness(List<Vector2> polygon, double minimumWidth)
        {
            for (int edgeIndex = 0; edgeIndex < polygon.Count; edgeIndex++)
            {
                Vector2 start = polygon[edgeIndex], end = polygon[(edgeIndex + 1) % polygon.Count];
                double edgeX = (double)end.x - start.x, edgeY = (double)end.y - start.y;
                double length = Math.Sqrt(edgeX * edgeX + edgeY * edgeY);
                if (length <= TolerancePixels * .001d) continue;
                double minimum = double.PositiveInfinity, maximum = double.NegativeInfinity;
                foreach (Vector2 point in polygon)
                {
                    double across = (-(double)point.x * edgeY + (double)point.y * edgeX) / length;
                    minimum = Math.Min(minimum, across);
                    maximum = Math.Max(maximum, across);
                }
                if (maximum - minimum < minimumWidth) return false;
            }
            return true;
        }

        private static Vector2 PointAtDistance(List<Vector2> polygon, List<double> cumulative, double distance)
        {
            double totalLength = cumulative[cumulative.Count - 1];
            distance %= totalLength;
            if (distance < 0d) distance += totalLength;
            int low = 0, high = polygon.Count - 1;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (cumulative[middle + 1] < distance) low = middle + 1;
                else high = middle;
            }
            double edgeLength = cumulative[low + 1] - cumulative[low];
            double fraction = edgeLength > 0d ? (distance - cumulative[low]) / edgeLength : 0d;
            Vector2 start = polygon[low], end = polygon[(low + 1) % polygon.Count];
            return new Vector2((float)(start.x + ((double)end.x - start.x) * fraction),
                (float)(start.y + ((double)end.y - start.y) * fraction));
        }

        private static bool HasVisibleBreaks(List<StaticScanLineSegment> dashes, float width)
        {
            // A half-pixel is the shader's visible antialias fringe. Requiring
            // separation between neighboring centerlines avoids bright joins.
            double minimumDistance = width + 1d + .05d;
            for (int i = 0; i < dashes.Count; i++)
            {
                StaticScanLineSegment first = dashes[i], second = dashes[(i + 1) % dashes.Count];
                double distance = Math.Min(Math.Min(PointSegmentDistance(first.Start, second),
                    PointSegmentDistance(first.End, second)), Math.Min(PointSegmentDistance(second.Start, first),
                    PointSegmentDistance(second.End, first)));
                if (distance < minimumDistance) return false;
            }
            return true;
        }

        private static double PointSegmentDistance(Vector2 point, StaticScanLineSegment segment)
        {
            double deltaX = (double)segment.End.x - segment.Start.x;
            double deltaY = (double)segment.End.y - segment.Start.y;
            double squaredLength = deltaX * deltaX + deltaY * deltaY;
            double fraction = squaredLength > 0d
                ? Math.Max(0d, Math.Min(1d, (((double)point.x - segment.Start.x) * deltaX
                    + ((double)point.y - segment.Start.y) * deltaY) / squaredLength)) : 0d;
            double offsetX = point.x - (segment.Start.x + deltaX * fraction);
            double offsetY = point.y - (segment.Start.y + deltaY * fraction);
            return Math.Sqrt(offsetX * offsetX + offsetY * offsetY);
        }

        private static void Canonicalize(List<Vector2> polygon)
        {
            double area = 0d;
            Vector2 previous = polygon[polygon.Count - 1];
            foreach (Vector2 point in polygon)
            {
                area += (double)previous.x * point.y - (double)previous.y * point.x;
                previous = point;
            }
            if (area < 0d) polygon.Reverse();
            int first = 0;
            for (int i = 1; i < polygon.Count; i++)
            {
                if (polygon[i].x < polygon[first].x - TolerancePixels * .01f
                    || (Mathf.Abs(polygon[i].x - polygon[first].x) <= TolerancePixels * .01f
                        && polygon[i].y < polygon[first].y))
                    first = i;
            }
            if (first == 0) return;
            polygon.Reverse(0, first);
            polygon.Reverse(first, polygon.Count - first);
            polygon.Reverse();
        }

        private static double Distance(Vector2 first, Vector2 second)
        {
            double x = (double)second.x - first.x, y = (double)second.y - first.y;
            return Math.Sqrt(x * x + y * y);
        }

        private static double Clamp(double value, double minimum, double maximum)
            => Math.Max(minimum, Math.Min(maximum, value));

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
