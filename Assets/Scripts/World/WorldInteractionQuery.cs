using System.Collections.Generic;
using AnimalGame.MapTest;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AnimalGame.World
{
    // Shapes are expressed in logical map meters, or world XY when no map is present.
    // A capsule with coincident ends is also the point/circle query.
    public struct InteractionShape
    {
        public Vector2 A, B, C, D;
        public float Radius;
        public bool IsBox;
        public Vector2 Center => IsBox ? (A + B + C + D) * .25f : (A + B) * .5f;
        public Vector2 Vertex(int i) => i == 0 ? A : i == 1 ? B : i == 2 ? C : D;
        public static Vector2 ToQuery(Vector2 world, MapTestSceneController map)
        {
            if (map == null || !map.HasGeneratedMap) return world;
            Vector2 origin = map.WorldBounds.min;
            return new Vector2((world.x - origin.x) / Mathf.Max(.000001f, map.MapMetersToWorldDistance(Vector2.right, 1f)),
                (world.y - origin.y) / Mathf.Max(.000001f, map.MapMetersToWorldDistance(Vector2.up, 1f)));
        }
        public static InteractionShape Capsule(Vector2 a, Vector2 b, float radius) =>
            new InteractionShape { A = a, B = b, Radius = Mathf.Max(0f, radius) };
        public static InteractionShape Box(Transform frame, Vector2 center, Vector2 size, MapTestSceneController map)
        {
            Vector2 h = size * .5f;
            return new InteractionShape { IsBox = true,
                A = ToQuery(frame.TransformPoint(center + new Vector2(-h.x, -h.y)), map),
                B = ToQuery(frame.TransformPoint(center + new Vector2(h.x, -h.y)), map),
                C = ToQuery(frame.TransformPoint(center + new Vector2(h.x, h.y)), map),
                D = ToQuery(frame.TransformPoint(center + new Vector2(-h.x, h.y)), map) };
        }
        public static InteractionShape WorldBox(Vector2 center, Vector2 size, MapTestSceneController map)
        {
            Vector2 h = size * .5f;
            return new InteractionShape { IsBox = true,
                A = ToQuery(center + new Vector2(-h.x, -h.y), map),
                B = ToQuery(center + new Vector2(h.x, -h.y), map),
                C = ToQuery(center + new Vector2(h.x, h.y), map),
                D = ToQuery(center + new Vector2(-h.x, h.y), map) };
        }
    }

    public static class WorldInteractionQuery
    {
        // All body, arm and grab queries share this registry and type filter.
        public static bool Query(InteractionShape shape, WorldInteractionKind kind,
            MapTestSceneController map, Scene scene, List<WorldInteraction> results = null,
            Transform ignore = null, InteractionShape? previous = null, Transform ignoreHeld = null)
        {
            bool found = false;
            results?.Clear();
            foreach (WorldInteraction item in WorldInteraction.Active)
            {
                if (item == null || !item.Available || item.Kind != kind || item.gameObject.scene != scene
                    || (ignore != null && (item.transform == ignore || item.transform.IsChildOf(ignore)))
                    || (ignoreHeld != null && (item.transform == ignoreHeld || item.transform.IsChildOf(ignoreHeld)))) continue;
                InteractionShape obstacle = item.GetShape(map);
                float depth = Penetration(shape, obstacle);
                if (depth < 0f) continue;
                // Permit escape/sliding from an existing overlap, but never deeper penetration.
                if (previous.HasValue && Penetration(previous.Value, obstacle) >= depth - .000001f) continue;
                found = true;
                if (results == null) return true;
                results.Add(item);
            }
            return found;
        }

        public static float Penetration(InteractionShape a, InteractionShape b)
        {
            if (!a.IsBox && !b.IsBox)
                return a.Radius + b.Radius - Mathf.Sqrt(SegmentDistanceSquared(a.A, a.B, b.A, b.B));
            if (!a.IsBox) return CapsuleBox(a, b);
            if (!b.IsBox) return CapsuleBox(b, a);
            float depth = float.PositiveInfinity;
            for (int shape = 0; shape < 2; shape++)
            {
                InteractionShape p = shape == 0 ? a : b;
                for (int i = 0; i < 2; i++)
                {
                    Vector2 edge = p.Vertex(i + 1) - p.Vertex(i);
                    if (edge.sqrMagnitude < 1e-12f) continue;
                    Vector2 axis = new Vector2(-edge.y, edge.x).normalized;
                    Project(a, axis, out float amin, out float amax);
                    Project(b, axis, out float bmin, out float bmax);
                    float overlap = Mathf.Min(amax - bmin, bmax - amin);
                    if (overlap < 0f) return overlap;
                    depth = Mathf.Min(depth, overlap);
                }
            }
            return depth;
        }
        private static void Project(InteractionShape p, Vector2 axis, out float min, out float max)
        {
            min = max = Vector2.Dot(p.A, axis);
            for (int i = 1; i < 4; i++)
            {
                float v = Vector2.Dot(p.Vertex(i), axis);
                min = Mathf.Min(min, v); max = Mathf.Max(max, v);
            }
        }
        private static float CapsuleBox(InteractionShape capsule, InteractionShape box)
        {
            float distance = float.PositiveInfinity;
            for (int i = 0; i < 4; i++)
                distance = Mathf.Min(distance, SegmentDistanceSquared(capsule.A, capsule.B, box.Vertex(i), box.Vertex((i + 1) % 4)));
            float inside = Mathf.Max(InsideDepth(capsule.A, box), InsideDepth(capsule.B, box));
            // A sweep beginning overlapped must not escape through the opposite side.
            // Maximise the minimum signed edge distance along the centre segment.
            // Its maximum occurs at an endpoint or at two edge-distance intersections.
            for (int i = 0; i < 4; i++)
            {
                EdgeDistanceLine(box, i, capsule.A, capsule.B, out float ai, out float bi);
                for (int j = i + 1; j < 4; j++)
                {
                    EdgeDistanceLine(box, j, capsule.A, capsule.B, out float aj, out float bj);
                    if (Mathf.Abs(bi - bj) < 1e-9f) continue;
                    float t = (aj - ai) / (bi - bj);
                    if (t >= 0f && t <= 1f)
                        inside = Mathf.Max(inside, InsideDepth(Vector2.Lerp(capsule.A, capsule.B, t), box));
                }
            }
            return inside >= 0f ? capsule.Radius + inside : capsule.Radius - Mathf.Sqrt(distance);
        }
        private static void EdgeDistanceLine(InteractionShape box, int i, Vector2 start, Vector2 end,
            out float intercept, out float slope)
        {
            Vector2 a = box.Vertex(i), edge = box.Vertex((i + 1) % 4) - a;
            Vector2 normal = new Vector2(-edge.y, edge.x).normalized;
            if (Vector2.Dot(box.Center - a, normal) < 0f) normal = -normal;
            intercept = Vector2.Dot(start - a, normal);
            slope = Vector2.Dot(end - start, normal);
        }
        private static float InsideDepth(Vector2 point, InteractionShape box)
        {
            float sign = 0f, nearest = float.PositiveInfinity;
            for (int i = 0; i < 4; i++)
            {
                Vector2 a = box.Vertex(i), b = box.Vertex((i + 1) % 4);
                float cross = Cross(b - a, point - a);
                if (Mathf.Abs(cross) > 1e-7f)
                {
                    if (sign != 0f && Mathf.Sign(cross) != sign) return -1f;
                    sign = Mathf.Sign(cross);
                }
                nearest = Mathf.Min(nearest, PointSegmentSquared(point, a, b));
            }
            return Mathf.Sqrt(nearest);
        }
        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        private static float PointSegmentSquared(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            float t = d.sqrMagnitude < 1e-12f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, d) / d.sqrMagnitude);
            return (p - a - d * t).sqrMagnitude;
        }
        private static float SegmentDistanceSquared(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            Vector2 u = b - a, v = d - c;
            float cross = Cross(u, v);
            if (Mathf.Abs(cross) > 1e-9f)
            {
                float t = Cross(c - a, v) / cross, s = Cross(c - a, u) / cross;
                if (t >= 0f && t <= 1f && s >= 0f && s <= 1f) return 0f;
            }
            return Mathf.Min(Mathf.Min(PointSegmentSquared(a, c, d), PointSegmentSquared(b, c, d)),
                Mathf.Min(PointSegmentSquared(c, a, b), PointSegmentSquared(d, a, b)));
        }
    }
}
