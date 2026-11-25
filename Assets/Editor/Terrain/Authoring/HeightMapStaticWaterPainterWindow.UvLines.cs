using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class HeightMapStaticWaterPainterWindow
{
    private const float UvBranchThreshold = .3f;

    private sealed class UvSection
    {
        public readonly List<Vector2> points;
        public readonly float[] distances;
        public UvGridVertex[] row;
        public UvSection downstream;
        public float Length => distances[distances.Length - 1];

        public UvSection(List<Vector2> points)
        {
            this.points = points;
            distances = new float[points.Count];
            for (int i = 1; i < points.Count; i++)
                distances[i] = distances[i - 1] + Vector2.Distance(points[i - 1], points[i]);
        }

        public Vector2 At(float arc)
        {
            int end = Array.BinarySearch(distances, arc);
            if (end >= 0) return points[end];
            end = ~end;
            if (end == points.Count) return points[end - 1];
            float t = Mathf.InverseLerp(distances[end - 1], distances[end], arc);
            return Vector2.LerpUnclamped(points[end - 1], points[end], t);
        }

        public float Project(Vector2 point, out Vector2 nearest)
        {
            float bestDistance = float.PositiveInfinity, arc = 0;
            nearest = points[0];
            for (int i = 1; i < points.Count; i++)
            {
                Vector2 start = points[i - 1], edge = points[i] - start;
                float t = Mathf.Clamp01(Vector2.Dot(point - start, edge) / edge.sqrMagnitude);
                Vector2 projected = start + t * edge;
                float distance = (point - projected).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                nearest = projected;
                arc = Mathf.LerpUnclamped(distances[i - 1], distances[i], t);
            }
            return arc;
        }

        public bool TryIntersect(Vector2 start, Vector2 end, out float arc, out Vector2 hit)
        {
            float first = float.PositiveInfinity;
            arc = 0;
            Vector2 step = end - start;
            for (int i = 1; i < points.Count; i++)
            {
                Vector2 edge = points[i] - points[i - 1];
                float determinant = CrossUv(step, edge);
                if (Mathf.Abs(determinant) < 1e-6f) continue;
                Vector2 offset = points[i - 1] - start;
                float alongStep = CrossUv(offset, edge) / determinant;
                float alongEdge = CrossUv(offset, step) / determinant;
                if (alongStep < 0 || alongStep > 1 || alongStep >= first || alongEdge < 0 || alongEdge > 1)
                    continue;
                first = alongStep;
                arc = Mathf.LerpUnclamped(distances[i - 1], distances[i], alongEdge);
            }
            hit = first <= 1 ? start + step * first : end;
            return first <= 1;
        }

        public UvSection Slice(float from, float to)
        {
            var slice = new List<Vector2> { At(from) };
            for (int i = 1; i < points.Count - 1; i++)
                if (distances[i] > from + 1e-4f && distances[i] < to - 1e-4f)
                    slice.Add(points[i]);
            slice.Add(At(to));

            var vertices = new List<UvGridVertex> { Boundary(from) };
            foreach (UvGridVertex vertex in row)
                if (vertex.arc > from + 1e-4f && vertex.arc < to - 1e-4f)
                    vertices.Add(new UvGridVertex(vertex.position, vertex.uv, vertex.line, vertex.arc - from));
            vertices.Add(Boundary(to));
            return new UvSection(slice) { row = vertices.ToArray() };

            UvGridVertex Boundary(float arc)
            {
                int right = 1;
                while (right < row.Length - 1 && row[right].arc < arc) right++;
                float t = Mathf.InverseLerp(row[right - 1].arc, row[right].arc, arc);
                return new UvGridVertex(Vector2.LerpUnclamped(row[right - 1].position, row[right].position, t),
                    Vector2.LerpUnclamped(row[right - 1].uv, row[right].uv, t), arc: arc - from);
            }
        }
    }

    private readonly struct UvBranch
    {
        public readonly UvSection parent;
        public readonly float from, to;
        public Vector2 Centre => parent.At((from + to) * .5f);

        public UvBranch(UvSection parent, float from, float to)
        {
            this.parent = parent;
            this.from = from;
            this.to = to;
        }
    }

    private void UpdateUvLines()
    {
        uvVLines.Clear();
        uvULines.Clear();
        if (!SelectUvIsland()) return;

        EnsureWorkingWaterUv();
        var covered = new bool[uvIsland.Length];
        var gradients = new Vector4[uvIsland.Length];
        var branches = new Stack<UvBranch>();
        var queued = new HashSet<int>();
        int nextLine = 0;
        float limit = uvBounds.width + uvBounds.height;
        Vector2 metresPerPixel = levelAsset.MapSizeMeters / uvResolution;
        Vector2 start = (uvSectionA + uvSectionB) * .5f;
        List<UvSection> main = TraceUvRiver(start, UvSectionNormal(), null, limit, metresPerPixel, covered,
            branches, queued, ref nextLine, out bool wetStarted, out bool oppositeEntry);
        BakeUvRiver(main, covered, gradients);

        int branchCount = 0;
        while (branches.Count > 0)
        {
            UvBranch branch = branches.Pop();
            Vector2 centre = branch.Centre;
            Vector2 direction = SampleUvDirection(centre, false, 1f);
            if (!CanTraceUvBranch(centre, direction, covered)) continue;

            UvSection root = branch.parent.Slice(branch.from, branch.to);
            List<UvSection> river = TraceUvRiver(centre, direction, root, limit, metresPerPixel, covered,
                branches, queued, ref nextLine, out _, out _);
            if (river.Count < 2) continue;
            BakeUvRiver(river, covered, gradients);
            branchCount++;
        }

        int pixelCount = ExtendBakedWaterUv(covered, gradients);
        waterUvChanged |= pixelCount > 0;
        uvStatus = $"Generated {uvVLines.Count} V lines, {uvULines.Count} U cross-sections and {branchCount} branches."
            + (pixelCount > 0 ? $" Baked {pixelCount} UV pixels; click Save to apply." : " No UV pixels were baked.")
            + (!wetStarted ? " The center trace did not reach the selected water." : "")
            + (oppositeEntry ? " The entry flow opposes the green arrow. Draw A to B in reverse to point downstream." : "");
        UnityEditor.SceneView.RepaintAll();
        Repaint();
    }

    private List<UvSection> TraceUvRiver(Vector2 centre, Vector2 normal, UvSection root, float limit,
        Vector2 metresPerPixel, bool[] covered, Stack<UvBranch> branches, HashSet<int> queued, ref int nextLine,
        out bool wetStarted, out bool oppositeEntry)
    {
        var sections = new List<UvSection>();
        if (root != null) sections.Add(root);
        var visitedCentres = new HashSet<int>();
        float travelled = 0;
        float u = root != null ? root.row[0].uv.x : .5f * (metresPerPixel.x + metresPerPixel.y);
        wetStarted = false;
        oppositeEntry = false;
        bool reachedNewWater = false;
        while (travelled + uvUSpacing <= 3f * limit)
        {
            List<Vector2> segment = TraceUvLine(centre, normal, false, 1f, uvUSpacing, true);
            if (sections.Count > 0 && UvLineLength(segment) + .001f < uvUSpacing)
            {
                // At a split, the old midpoint can run straight into the dry bank between the arms.
                UvSection previousSection = sections[sections.Count - 1];
                float nearest = float.PositiveInfinity;
                foreach (UvGridVertex seed in previousSection.row)
                {
                    if (seed.line == 0) continue;
                    float offset = Mathf.Abs(seed.arc - previousSection.Length * .5f);
                    if (offset >= nearest || covered[UvPixelIndex(seed.position)]) continue;
                    Vector2 direction = SampleUvDirection(seed.position, false, 1f);
                    List<Vector2> alternative = TraceUvLine(seed.position, direction, false, 1f, uvUSpacing);
                    if (UvLineLength(alternative) + .001f < uvUSpacing) continue;
                    segment = alternative;
                    nearest = offset;
                }
            }
            bool reachedExistingWater = false;
            for (int i = 0; i < segment.Count; i++)
            {
                Vector2 point = segment[i];
                bool wet = UvContainsWater(point);
                if (!wetStarted && wet)
                {
                    wetStarted = true;
                    oppositeEntry = Vector2.Dot(SampleUvDirection(point, false, 1f), normal) < 0f;
                }
                if (wet && covered[UvPixelIndex(point)] && reachedNewWater)
                {
                    reachedExistingWater = true;
                    break;
                }
                if (wet && !covered[UvPixelIndex(point)]) reachedNewWater = true;
                if (i == 0) continue;

                Vector2 delta = point - segment[i - 1];
                float speed = SampleUvFlow(point - .5f * delta).magnitude;
                // Faster water advances less in U per metre. Static UV retain their original scale.
                u += Vector2.Scale(delta, metresPerPixel).magnitude / (1f + speed);
                travelled += delta.magnitude;
            }
            if (reachedExistingWater || segment.Count < 2 || UvLineLength(segment) + .001f < uvUSpacing)
                break;

            centre = segment[segment.Count - 1];
            if (!UvContainsWater(centre) || covered[UvPixelIndex(centre)]) continue;
            List<Vector2> cross = TraceUvCrossLine(centre, limit, covered);
            if (cross.Count < 2) break;
            var section = new UvSection(cross);
            UvSection previous = sections.Count > 0 ? sections[sections.Count - 1] : null;
            centre = UvPointAtDistance(cross, section.Length * .5f, out Vector2 tangent);
            if (!UvContainsWater(centre) || covered[UvPixelIndex(centre)] || !visitedCentres.Add(UvPixelIndex(centre)))
                break;

            if (previous != null && previous != root)
                FindUvBranches(previous, section, covered, branches, queued);
            BuildUvRow(section, previous?.downstream ?? previous, u, metresPerPixel, ref nextLine);
            sections.Add(section);

            Vector2 forward = segment[segment.Count - 1] - segment[segment.Count - 2];
            normal = UvNormal(tangent);
            if (Vector2.Dot(normal, forward) < 0f) normal = -normal;
        }
        return sections;
    }

    private int UvPixelIndex(Vector2 point) => Mathf.FloorToInt(point.y) * uvResolution + Mathf.FloorToInt(point.x);

    private void FindUvBranches(UvSection previous, UvSection current, bool[] covered,
        Stack<UvBranch> branches, HashSet<int> queued)
    {
        float minimum = current.Length * UvBranchThreshold;
        if (previous.Length <= current.Length + minimum) return;

        float first = previous.Length, last = 0;
        foreach (Vector2 point in current.points)
        {
            float where = previous.Project(point, out _);
            first = Mathf.Min(first, where);
            last = Mathf.Max(last, where);
        }

        bool right = Enqueue(last, previous.Length);
        bool left = Enqueue(0, first);
        // The continuing strip shares its upstream edge with the branch slices.
        if ((left || right) && last - first > 1e-4f)
            previous.downstream = previous.Slice(left ? first : 0, right ? last : previous.Length);

        bool Enqueue(float from, float to)
        {
            if (to - from <= minimum) return false;
            var branch = new UvBranch(previous, from, to);
            Vector2 centre = branch.Centre;
            Vector2 direction = SampleUvDirection(centre, false, 1f);
            if (!CanTraceUvBranch(centre, direction, covered) || !queued.Add(UvPixelIndex(centre)))
                return false;
            branches.Push(branch);
            return true;
        }
    }

    private bool CanTraceUvBranch(Vector2 centre, Vector2 direction, bool[] covered)
    {
        if (direction == Vector2.zero || !UvContainsWater(centre)) return false;
        bool reachedNewWater = !covered[UvPixelIndex(centre)];
        float newDistance = 0;
        List<Vector2> probe = TraceUvLine(centre, direction, false, 1f, 2f * uvUSpacing);
        for (int i = 1; i < probe.Count; i++)
        {
            if (!UvSegmentIsWet(probe[i - 1], probe[i])) return false;
            if (covered[UvPixelIndex(probe[i])])
            {
                if (reachedNewWater) return false;
                continue;
            }
            reachedNewWater = true;
            newDistance += Vector2.Distance(probe[i - 1], probe[i]);
        }
        return newDistance + .001f >= uvUSpacing;
    }

    private void BuildUvRow(UvSection section, UvSection previous, float u, Vector2 metresPerPixel, ref int nextLine)
    {
        var row = new List<UvGridVertex> { new UvGridVertex(section.points[0], new Vector2(u, 0)) };
        for (float arc = uvVSpacing; arc < section.Length - .001f; arc += uvVSpacing)
            row.Add(new UvGridVertex(section.At(arc), new Vector2(u, 0), arc: arc));
        row.Add(new UvGridVertex(section.points[section.points.Count - 1], new Vector2(u, 0), arc: section.Length));
        section.row = row.ToArray();

        if (previous != null)
        {
            var predicted = new float[previous.row.Length - 2];
            for (int i = 0; i < predicted.Length; i++)
                predicted[i] = PredictUvArrival(previous.row[i + 1].position, section);
            MatchUvSeeds(previous.row, section.row, predicted);
        }

        // Shore endpoints stay available for the triangle strips. Interior seeds can start new V lines.
        int left = -1, right = NextUvAnchor(section.row, 0);
        float scale = .5f * (metresPerPixel.x + metresPerPixel.y);
        for (int i = 0; i < section.row.Length; i++)
        {
            ref UvGridVertex vertex = ref section.row[i];
            if (i == right)
            {
                left = i;
                right = NextUvAnchor(section.row, i + 1);
                continue;
            }

            if (left >= 0 && right < section.row.Length)
            {
                UvGridVertex a = section.row[left], b = section.row[right];
                vertex.uv.y = Mathf.LerpUnclamped(a.uv.y, b.uv.y, Mathf.InverseLerp(a.arc, b.arc, vertex.arc));
            }
            else if (left >= 0 || right < section.row.Length)
            {
                UvGridVertex anchor = section.row[left >= 0 ? left : right];
                vertex.uv.y = anchor.uv.y + (vertex.arc - anchor.arc) * scale;
            }
            else
                vertex.uv.y = (vertex.arc - section.Length * .5f) * scale;

            if (i > 0 && i < section.row.Length - 1)
                vertex.line = ++nextLine;
        }
    }

    private static int NextUvAnchor(UvGridVertex[] row, int start)
    {
        while (start < row.Length && row[start].line == 0) start++;
        return start;
    }

    private float PredictUvArrival(Vector2 point, UvSection destination)
    {
        for (float travelled = 0; travelled < uvUSpacing;)
        {
            Vector2 direction = SampleUvDirection(point, false, 1f);
            if (direction == Vector2.zero) break;
            float step = Mathf.Min(uvStep, uvUSpacing - travelled);
            Vector2 next = point + direction * step;
            if (destination.TryIntersect(point, next, out float arc, out Vector2 hit))
                return UvSegmentIsWet(point, hit) ? arc : float.PositiveInfinity;
            if (!UvSegmentIsWet(point, next)) return float.PositiveInfinity;
            point = next;
            travelled += step;
        }

        float projected = destination.Project(point, out Vector2 nearest);
        float handoff = Mathf.Min(uvUSpacing, uvVSpacing);
        return (point - nearest).sqrMagnitude <= handoff * handoff && UvSegmentIsWet(point, nearest)
            ? projected : float.PositiveInfinity;
    }

    private bool UvSegmentIsWet(Vector2 from, Vector2 to)
    {
        Vector2Int pixel = Vector2Int.FloorToInt(from), end = Vector2Int.FloorToInt(to);
        Vector2 delta = to - from;
        int sx = delta.x >= 0 ? 1 : -1, sy = delta.y >= 0 ? 1 : -1;
        float dx = delta.x == 0 ? float.PositiveInfinity : Mathf.Abs(1 / delta.x);
        float dy = delta.y == 0 ? float.PositiveInfinity : Mathf.Abs(1 / delta.y);
        float tx = dx * Mathf.Abs(pixel.x + (sx > 0 ? 1 : 0) - from.x);
        float ty = dy * Mathf.Abs(pixel.y + (sy > 0 ? 1 : 0) - from.y);
        while (true)
        {
            if (!uvBounds.Contains(pixel) || !uvIsland[pixel.y * uvResolution + pixel.x]) return false;
            if (pixel == end) return true;
            float x = pixel.x == end.x ? float.PositiveInfinity : tx;
            float y = pixel.y == end.y ? float.PositiveInfinity : ty;
            if (x <= y) { pixel.x += sx; tx += dx; }
            if (y <= x) { pixel.y += sy; ty += dy; }
        }
    }

    private void MatchUvSeeds(UvGridVertex[] previous, UvGridVertex[] current, float[] predicted)
    {
        int m = predicted.Length, n = current.Length - 2;
        if (m == 0 || n == 0) return;
        var choices = new byte[m, n];
        var below = new double[n + 1];
        var scores = new double[n + 1];
        // One extra match outweighs every possible distance error, then minimize total error.
        double matchBonus = (Math.Min(m, n) + 1) * (double)uvVSpacing;
        for (int i = m - 1; i >= 0; i--)
        {
            for (int j = n - 1; j >= 0; j--)
            {
                double best = below[j];
                byte choice = 0;
                if (scores[j + 1] > best)
                {
                    best = scores[j + 1];
                    choice = 1;
                }
                float error = Mathf.Abs(predicted[i] - current[j + 1].arc);
                double matched = below[j + 1] + matchBonus - error;
                if (error <= uvVSpacing && matched >= best)
                {
                    best = matched;
                    choice = 2;
                }
                scores[j] = best;
                choices[i, j] = choice;
            }
            (below, scores) = (scores, below);
        }

        for (int i = 0, j = 0; i < m && j < n;)
        {
            byte choice = choices[i, j];
            if (choice == 2)
            {
                current[j + 1].line = previous[i + 1].line;
                current[j + 1].uv.y = previous[i + 1].uv.y;
            }
            if (choice != 1) i++;
            if (choice != 0) j++;
        }
    }

    private void BakeUvRiver(List<UvSection> sections, bool[] covered, Vector4[] gradients)
    {
        var rows = new List<UvGridVertex[]>(sections.Count);
        foreach (UvSection section in sections)
        {
            rows.Add(section.row);
            uvULines.Add(section.points.ToArray());
        }
        var columns = new List<Dictionary<int, UvGridVertex>>(rows.Count);
        foreach (UvGridVertex[] row in rows)
        {
            var lookup = new Dictionary<int, UvGridVertex>();
            foreach (UvGridVertex vertex in row)
                if (vertex.line > 0) lookup.Add(vertex.line, vertex);
            columns.Add(lookup);
        }

        var lines = new Dictionary<int, List<Vector2>>();
        for (int row = 0; row + 1 < rows.Count; row++)
        {
            UvGridVertex[] a = sections[row].downstream?.row ?? rows[row];
            UvGridVertex[] b = rows[row + 1];
            var curves = new List<UvSplineSegment>();
            int count = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(Vector2.Distance(a[0].position, b[0].position),
                Vector2.Distance(a[a.Length - 1].position, b[b.Length - 1].position)) / uvCurveSpacing));
            foreach (UvGridVertex start in a)
            {
                int line = start.line;
                if (line == 0 || !columns[row + 1].TryGetValue(line, out UvGridVertex end)) continue;
                Vector2 before = row > 0 && columns[row - 1].TryGetValue(line, out UvGridVertex earlier)
                    ? earlier.position : 2f * start.position - end.position;
                Vector2 after = row + 2 < rows.Count && columns[row + 2].TryGetValue(line, out UvGridVertex later)
                    ? later.position : 2f * end.position - start.position;
                UvSplineSegment curve = MakeUvSplineSegment(start, end, before, after);
                curves.Add(curve);
                count = Mathf.Max(count, Mathf.CeilToInt(curve.Length / uvCurveSpacing));
                if (!lines.ContainsKey(line)) lines.Add(line, new List<Vector2> { start.position });
            }

            // Use the same samples for the cyan preview and the triangle strips.
            UvGridVertex[] previous = a;
            for (int i = 1; i <= count; i++)
            {
                float t = (float)i / count;
                var next = i == count ? b : new UvGridVertex[curves.Count + 2];
                if (i < count)
                {
                    next[0] = InterpolateUvEdge(a[0], b[0], t);
                    next[next.Length - 1] = InterpolateUvEdge(a[a.Length - 1], b[b.Length - 1], t);
                }
                for (int j = 0; j < curves.Count; j++)
                {
                    UvSplineSegment curve = curves[j];
                    Vector2 position = i == count ? curve.end.position : curve.PointAtDistance(curve.Length * t);
                    lines[curve.start.line].Add(position);
                    if (i < count)
                        next[j + 1] = new UvGridVertex(position, Vector2.Lerp(curve.start.uv, curve.end.uv, t));
                }
                RasterizeUvStrip(previous, next, covered, gradients);
                previous = next;
            }
        }
        foreach (List<Vector2> line in lines.Values) uvVLines.Add(line.ToArray());
    }

    private static UvGridVertex InterpolateUvEdge(UvGridVertex a, UvGridVertex b, float t)
        => new UvGridVertex(Vector2.Lerp(a.position, b.position, t), Vector2.Lerp(a.uv, b.uv, t));

    private void RasterizeUvStrip(UvGridVertex[] a, UvGridVertex[] b, bool[] covered, Vector4[] gradients)
    {
        int i = 0, j = 0;
        while (i + 1 < a.Length || j + 1 < b.Length)
        {
            if (j + 1 == b.Length || (i + 1 < a.Length && a[i + 1].uv.y <= b[j + 1].uv.y))
            {
                RasterizeUvTriangle(a[i], b[j], a[i + 1], covered, gradients);
                i++;
            }
            else
            {
                RasterizeUvTriangle(a[i], b[j], b[j + 1], covered, gradients);
                j++;
            }
        }
    }

    private static float CrossUv(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
}
