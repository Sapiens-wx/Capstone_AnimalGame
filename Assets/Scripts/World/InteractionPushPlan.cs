using System.Collections.Generic;
using AnimalGame.Garbage;
using AnimalGame.MapTest;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AnimalGame.World
{
    // A short, read-only motion transaction. The caller owns the robot/arm pose;
    // this plan owns only the translations of directly contacted props.
    public sealed class InteractionPushPlan
    {
        public const WorldInteractionKind Solids = WorldInteractionKind.Collision | WorldInteractionKind.BodyCollision;
        private const float Epsilon = .000001f;
        private struct Motion
        {
            public InteractionShape Before, After;
            public WorldInteractionKind Mask;
            public bool Push;
        }
        private struct Push
        {
            public Transform Root;
            public Vector2 Delta;
        }
        private readonly List<Motion> motions = new();
        private readonly List<Push> pushes = new();
        private readonly List<WorldInteraction> hits = new();
        private readonly List<WorldInteraction> parts = new();
        private readonly List<WorldInteraction> otherParts = new();
        private readonly List<GarbageMotion> garbage = new();
        private readonly Vector2[] points = new Vector2[8];
        private readonly Vector2[] hull = new Vector2[16];
        private MapTestSceneController map;
        private Scene scene;
        private Transform robot, held;
        private bool valid;
        public float SpeedMultiplier { get; private set; } = 1f;

        public void Begin(MapTestSceneController sceneMap, Scene sceneValue, Transform robotRoot, Transform heldRoot)
        {
            map = sceneMap; scene = sceneValue; robot = robotRoot; held = heldRoot;
            motions.Clear(); pushes.Clear(); valid = false; SpeedMultiplier = 1f;
        }

        public void Add(InteractionShape before, InteractionShape after, bool canPush,
            WorldInteractionKind mask = Solids)
        {
            motions.Add(new Motion { Before = before, After = after, Mask = mask, Push = canPush });
        }

        public static float Travel(InteractionShape a, InteractionShape b)
        {
            float result = Mathf.Max(Vector2.Distance(a.A, b.A), Vector2.Distance(a.B, b.B));
            if (a.IsBox) result = Mathf.Max(result, Vector2.Distance(a.C, b.C), Vector2.Distance(a.D, b.D));
            return result + Mathf.Abs(a.Radius - b.Radius);
        }

        public static Vector2 ToWorld(Vector2 point, MapTestSceneController map)
        {
            if (map == null || !map.HasGeneratedMap) return point;
            return (Vector2)map.WorldBounds.min + Vector2.Scale(point, new Vector2(
                map.MapMetersToWorldDistance(Vector2.right, 1f), map.MapMetersToWorldDistance(Vector2.up, 1f)));
        }

        public static Vector2 WorldDelta(Vector2 delta, MapTestSceneController map) =>
            ToWorld(delta, map) - ToWorld(Vector2.zero, map);

        // Transforms a queried shape without moving its source or touching the index.
        public static InteractionShape TransformShape(InteractionShape shape, Matrix4x4 worldDelta,
            MapTestSceneController map)
        {
            shape.A = InteractionShape.ToQuery(worldDelta.MultiplyPoint3x4(ToWorld(shape.A, map)), map);
            shape.B = InteractionShape.ToQuery(worldDelta.MultiplyPoint3x4(ToWorld(shape.B, map)), map);
            if (shape.IsBox)
            {
                shape.C = InteractionShape.ToQuery(worldDelta.MultiplyPoint3x4(ToWorld(shape.C, map)), map);
                shape.D = InteractionShape.ToQuery(worldDelta.MultiplyPoint3x4(ToWorld(shape.D, map)), map);
            }
            return shape;
        }

        private int IndexOf(Transform root)
        {
            for (int i = 0; i < pushes.Count; i++) if (pushes[i].Root == root) return i;
            return -1;
        }
        private Vector2 PlannedDelta(WorldInteraction item)
        {
            int index = IndexOf(item.MotionRoot);
            return index < 0 ? Vector2.zero : pushes[index].Delta;
        }
        private bool Excluded(WorldInteraction item) =>
            (robot != null && item.transform.IsChildOf(robot)) ||
            (held != null && (item.transform.IsChildOf(held) || item.MotionRoot == held));

        private void QuerySweep(InteractionShape before, InteractionShape after, WorldInteractionKind mask)
        {
            Rect a = WorldInteractionQuadTree.BoundsOf(before), b = WorldInteractionQuadTree.BoundsOf(after);
            Vector2 min = Vector2.Min(a.min, b.min) - Vector2.one * Epsilon;
            Vector2 max = Vector2.Max(a.max, b.max) + Vector2.one * Epsilon;
            WorldInteractionQuery.Query(new InteractionShape { IsBox = true,
                A = min, B = new Vector2(max.x, min.y), C = max, D = new Vector2(min.x, max.y) },
                mask, map, scene, hits);
        }

        private bool CanPush(Transform root)
        {
            root.GetComponentsInChildren(false, parts);
            foreach (WorldInteraction item in parts)
            {
                if (!item.Available || item.MotionRoot != root) continue;
                if (item.Owner != null) return false;
                if ((item.Kind & Solids) != 0)
                {
                    if ((item.Kind & WorldInteractionKind.Pushable) == 0) return false;
                    SpeedMultiplier = Mathf.Min(SpeedMultiplier, item.PushSpeedMultiplier);
                }
            }
            return true;
        }

        // Collecting the multiplier is useful even when another contact blocks the plan.
        public bool Evaluate()
        {
            valid = false; pushes.Clear(); SpeedMultiplier = 1f;
            bool blocked = false;
            foreach (Motion motion in motions)
            {
                if (!motion.Push || Travel(motion.Before, motion.After) <= Epsilon) continue;
                if (!InsideMap(motion.After)) blocked = true;
                QuerySweep(motion.Before, motion.After, motion.Mask);
                foreach (WorldInteraction item in hits)
                {
                    if (Excluded(item)) continue;
                    InteractionShape obstacle = item.GetShape(map);
                    if (!Blocks(motion.Before, motion.After, obstacle)) continue;
                    Transform root = item.MotionRoot;
                    if (!CanPush(root)) { blocked = true; continue; }
                    int index = IndexOf(root);
                    Vector2 delta = index < 0 ? Vector2.zero : pushes[index].Delta;
                    // Use the approaching part of the shape, including rotating corners.
                    Vector2 direction = ContactTravel(motion.Before, motion.After, obstacle.Center);
                    if (direction.sqrMagnitude <= Epsilon * Epsilon) { blocked = true; continue; }
                    float length = direction.magnitude;
                    direction /= length;
                    // A prop touched by body and held geometry has one translation.
                    if (Blocks(motion.Before, motion.After.Translated(-delta), obstacle))
                    {
                        float low = 0f, high = Mathf.Max(length, Travel(motion.Before, motion.After)) * 2f + .00002f;
                        if (Blocks(motion.Before, motion.After.Translated(-delta - direction * high), obstacle))
                        { blocked = true; continue; }
                        for (int iteration = 0; iteration < 20; iteration++)
                        {
                            float middle = (low + high) * .5f;
                            if (Blocks(motion.Before, motion.After.Translated(-delta - direction * middle), obstacle)) low = middle;
                            else high = middle;
                        }
                        delta += direction * (high + .00001f);
                    }
                    if (index < 0) pushes.Add(new Push { Root = root, Delta = delta });
                    else pushes[index] = new Push { Root = root, Delta = delta };
                }
            }
            if (blocked) return false;

            // Revalidate all sources against the same target plan, including bare arms.
            foreach (Motion motion in motions)
            {
                QuerySweep(motion.Before, motion.After, motion.Mask);
                foreach (WorldInteraction item in hits)
                    if (!Excluded(item) && Blocks(motion.Before,
                        motion.After.Translated(-PlannedDelta(item)), item.GetShape(map))) return false;
            }
            foreach (Push push in pushes)
            {
                push.Root.GetComponentsInChildren(false, parts);
                foreach (WorldInteraction part in parts)
                {
                    if (!part.Available || part.MotionRoot != push.Root || (part.Kind & Solids) == 0) continue;
                    InteractionShape before = part.GetShape(map), after = before.Translated(push.Delta);
                    if (!InsideMap(after)) return false;
                    QuerySweep(before, after, Solids);
                    foreach (WorldInteraction obstacle in hits)
                    {
                        if (Excluded(obstacle) || obstacle.MotionRoot == push.Root) continue;
                        if (Blocks(before, after.Translated(-PlannedDelta(obstacle)), obstacle.GetShape(map))) return false;
                    }
                    // Targets must not be pushed through the robot or held geometry.
                    foreach (Motion source in motions)
                        if ((part.Kind & source.Mask) != 0 && Blocks(source.Before,
                            source.After.Translated(-push.Delta), before)) return false;
                    // Two targets can meet between their old bounds; querying only
                    // the spatial index would miss that relative motion.
                    foreach (Push other in pushes)
                    {
                        if (other.Root == push.Root) continue;
                        other.Root.GetComponentsInChildren(false, otherParts);
                        foreach (WorldInteraction otherPart in otherParts)
                            if (otherPart.Available && otherPart.MotionRoot == other.Root
                                && (otherPart.Kind & Solids) != 0 && Blocks(before,
                                    after.Translated(-other.Delta), otherPart.GetShape(map))) return false;
                    }
                }
            }
            valid = true;
            return true;
        }

        public bool Commit()
        {
            if (!valid) return false;
            // Recheck ownership before the first mutation, never halfway through a batch.
            foreach (Push push in pushes)
                if (push.Root == null || !CanPush(push.Root)) { valid = false; return false; }
            foreach (Push push in pushes)
            {
                push.Root.GetComponentsInChildren(false, garbage);
                foreach (GarbageMotion motion in garbage) motion.PrepareImmediatePush();
                push.Root.position += (Vector3)WorldDelta(push.Delta, map);
                WorldInteraction.MarkHierarchySpatialDirty(push.Root);
            }
            valid = false;
            return true;
        }

        private bool InsideMap(InteractionShape shape)
        {
            if (map == null || !map.HasGeneratedMap) return true;
            if (shape.IsBox)
            {
                for (int i = 0; i < 4; i++) if (!map.TrySampleMapPosition(shape.Vertex(i), out _)) return false;
            }
            else
            {
                for (int i = 0; i < 2; i++)
                    for (int side = 0; side < 4; side++)
                    {
                        Vector2 offset = side == 0 ? Vector2.right : side == 1 ? Vector2.left : side == 2 ? Vector2.up : Vector2.down;
                        if (!map.TrySampleMapPosition((i == 0 ? shape.A : shape.B) + offset * shape.Radius, out _)) return false;
                    }
            }
            return true;
        }

        private static Vector2 ContactTravel(InteractionShape before, InteractionShape after, Vector2 target)
        {
            Vector2 travel = after.Center - before.Center;
            float best = Vector2.Dot(travel, (target - before.Center).normalized);
            int count = before.IsBox ? 4 : 2;
            for (int i = 0; i < count; i++)
            {
                Vector2 start = before.Vertex(i), delta = after.Vertex(i) - start;
                float score = Vector2.Dot(delta, (target - start).normalized);
                if (score > best + Epsilon) { best = score; travel = delta; }
            }
            return travel;
        }

        // Swept convex hull catches thin obstacles between endpoints. Initial overlaps
        // may escape, but are never deepened. Callers subdivide curved/rotating paths.
        private bool Blocks(InteractionShape before, InteractionShape after, InteractionShape obstacle)
        {
            float previous = WorldInteractionQuery.Penetration(before, obstacle);
            float next = WorldInteractionQuery.Penetration(after, obstacle);
            if (previous >= -Epsilon && next <= previous + Epsilon)
            {
                // A long translation must not use an initial overlap to exit through
                // the far side. Check the escape path, not just its final depth.
                float spacing = Mathf.Max(.000001f, Mathf.Min(.005f, Thickness(before) * .25f));
                int steps = Mathf.CeilToInt(Travel(before, after) / spacing);
                if (steps > 8192) return true;
                for (int i = 1; i < steps; i++)
                {
                    float t = i / (float)steps;
                    InteractionShape sample = before;
                    sample.A = Vector2.Lerp(before.A, after.A, t); sample.B = Vector2.Lerp(before.B, after.B, t);
                    sample.C = Vector2.Lerp(before.C, after.C, t); sample.D = Vector2.Lerp(before.D, after.D, t);
                    sample.Radius = Mathf.Lerp(before.Radius, after.Radius, t);
                    if (WorldInteractionQuery.Penetration(sample, obstacle) > previous + Epsilon) return true;
                }
                return false;
            }
            if (next > Epsilon) return true;
            int count = before.IsBox ? 4 : 2;
            for (int i = 0; i < count; i++) { points[i] = before.Vertex(i); points[i + count] = after.Vertex(i); }
            int n = count * 2;
            // Eight-element insertion sort and monotone hull, no per-query allocation.
            for (int i = 1; i < n; i++)
            {
                Vector2 value = points[i]; int j = i - 1;
                while (j >= 0 && (points[j].x > value.x || (points[j].x == value.x && points[j].y > value.y)))
                { points[j + 1] = points[j]; j--; }
                points[j + 1] = value;
            }
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                while (k >= 2 && Cross(hull[k - 1] - hull[k - 2], points[i] - hull[k - 1]) <= 0f) k--;
                hull[k++] = points[i];
            }
            int lower = k + 1;
            for (int i = n - 2; i >= 0; i--)
            {
                while (k >= lower && Cross(hull[k - 1] - hull[k - 2], points[i] - hull[k - 1]) <= 0f) k--;
                hull[k++] = points[i];
            }
            k = Mathf.Max(1, k - 1);
            float radius = before.IsBox ? 0f : Mathf.Max(before.Radius, after.Radius);
            for (int i = 0; i < k; i++)
            {
                Vector2 edge = hull[(i + 1) % k] - hull[i];
                if (Separated(new Vector2(-edge.y, edge.x), k, radius, obstacle)) return false;
            }
            int vertices = obstacle.IsBox ? 4 : 2;
            for (int i = 0; i < vertices; i++)
            {
                Vector2 edge = obstacle.Vertex((i + 1) % vertices) - obstacle.Vertex(i);
                if (Separated(new Vector2(-edge.y, edge.x), k, radius, obstacle)) return false;
                if (radius > 0f || !obstacle.IsBox)
                    for (int j = 0; j < k; j++)
                        if (Separated(obstacle.Vertex(i) - hull[j], k, radius, obstacle)) return false;
            }
            return true;
        }
        private bool Separated(Vector2 axis, int count, float radius, InteractionShape obstacle)
        {
            if (axis.sqrMagnitude < 1e-14f) return false;
            axis.Normalize();
            float min = Vector2.Dot(hull[0], axis), max = min;
            for (int i = 1; i < count; i++) { float p = Vector2.Dot(hull[i], axis); min = Mathf.Min(min, p); max = Mathf.Max(max, p); }
            float omin = Vector2.Dot(obstacle.A, axis), omax = omin;
            for (int i = 1; i < (obstacle.IsBox ? 4 : 2); i++)
            { float p = Vector2.Dot(obstacle.Vertex(i), axis); omin = Mathf.Min(omin, p); omax = Mathf.Max(omax, p); }
            float margin = radius + (obstacle.IsBox ? 0f : obstacle.Radius);
            return max + margin <= omin + Epsilon || omax + margin <= min + Epsilon;
        }
        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        private static float Thickness(InteractionShape shape) => shape.IsBox
            ? Mathf.Min(Vector2.Distance(shape.A, shape.B), Vector2.Distance(shape.B, shape.C))
            : shape.Radius * 2f;
    }
}
