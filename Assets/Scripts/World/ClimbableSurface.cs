using System.Collections.Generic;
using AnimalGame.MapTest;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AnimalGame.World
{
    /// <summary>A virtual surface in map metres; never changes physical height.</summary>
    public readonly struct ClimbableSurface
    {
        // Normalized balance displacement, deliberately independent of dangerous terrain angles.
        public const float MaximumSafeBalanceOffset = 0.75f;
        public const float MinimumUphillSpeedMultiplier = 0.65f;
        public const float MaximumDownhillSpeedMultiplier = 1.20f;
        public WorldInteraction Source { get; }
        public bool IsActive => Source != null;
        public float Strength { get; }
        public Vector2 DownhillWorldDirection { get; }

        public SlopeTraversalResult Traversal(Vector2 travel)
        {
            // Telemetry only: driving and balance use the safe normalized constants above.
            const float maximumDisplaySlopeDegrees = 20f;
            float angle = maximumDisplaySlopeDegrees * Strength;
            float signed = -angle * Vector2.Dot(travel.normalized, DownhillWorldDirection);
            return new SlopeTraversalResult(true, true, false, UphillSlopeLevel.LevelOne,
                Mathf.Abs(signed), signed, Mathf.Max(signed, 0f), Mathf.Max(-signed, 0f),
                angle, 0f, 0f, DownhillWorldDirection, TraversalBlockReason.None);
        }

        private ClimbableSurface(WorldInteraction source, float strength, Vector2 downhill)
        {
            Source = source;
            Strength = strength;
            DownhillWorldDirection = downhill;
        }

        public float SpeedMultiplier(Vector2 travel)
        {
            float alignment = Vector2.Dot(travel.normalized, DownhillWorldDirection);
            return 1f - (1f - MinimumUphillSpeedMultiplier) * Strength * Mathf.Max(-alignment, 0f)
                + (MaximumDownhillSpeedMultiplier - 1f) * Strength * Mathf.Max(alignment, 0f);
        }

        public static bool IsUsable(WorldInteraction item) => item != null && item.Available
            && item.Owner == null && (item.Kind & WorldInteractionKind.Climbable) != 0;

        public static ClimbableSurface Sample(Vector2 worldPosition, MapTestSceneController map,
            Scene scene, List<WorldInteraction> scratch, WorldInteraction preferred = null)
        {
            Vector2 point = InteractionShape.ToQuery(worldPosition, map);
            WorldInteractionQuery.Query(InteractionShape.Capsule(point, point, 0f),
                WorldInteractionKind.Climbable, map, scene, scratch);
            ClimbableSurface best = default;
            foreach (WorldInteraction item in scratch)
            {
                if (!IsUsable(item)) continue;
                InteractionShape shape = item.GetShape(map);
                if (shape.IsBox || shape.Radius <= 0.000001f) continue;
                Vector2 radial = point - shape.Center;
                float distance = radial.magnitude;
                if (distance >= shape.Radius) continue;
                float top = item.TopRadiusRatio01 * shape.Radius;
                float strength = 0f;
                if (distance > top)
                {
                    float band = (shape.Radius - top) * 0.1f;
                    strength = item.SlopeStrength01
                        * Mathf.SmoothStep(0f, 1f, (distance - top) / band)
                        * Mathf.SmoothStep(0f, 1f, (shape.Radius - distance) / band);
                }
                Vector2 downhill = radial.normalized;
                if (map != null && map.HasGeneratedMap)
                    downhill = new Vector2(downhill.x * map.MapMetersToWorldDistance(Vector2.right, 1f),
                        downhill.y * map.MapMetersToWorldDistance(Vector2.up, 1f)).normalized;
                var candidate = new ClimbableSurface(item, strength, downhill);
                bool tied = Mathf.Abs(strength - best.Strength) <= 0.001f;
                if (!best.IsActive || strength > best.Strength + 0.001f
                    || (tied && (item == preferred || (best.Source != preferred
                        && item.GetInstanceID() < best.Source.GetInstanceID()))))
                    best = candidate;
            }
            return best;
        }
    }

    /// <summary>Consumes only committed driving segments, so teleports and disappearing props cannot land.</summary>
    public sealed class ClimbableContactTracker
    {
        private readonly List<WorldInteraction> candidates = new();
        private readonly HashSet<WorldInteraction> armed = new();
        private readonly HashSet<WorldInteraction> retained = new();
        private readonly Dictionary<WorldInteraction, InteractionShape> previousShapes = new();
        private readonly Dictionary<WorldInteraction, InteractionShape> nextShapes = new();
        private float nextImpactTime = float.NegativeInfinity;

        public void Reset()
        {
            armed.Clear();
            retained.Clear();
            previousShapes.Clear();
            nextShapes.Clear();
            nextImpactTime = float.NegativeInfinity;
        }

        public bool Move(Vector2 startWorld, Vector2 endWorld, MapTestSceneController map,
            Scene scene, float time)
        {
            Vector2 start = InteractionShape.ToQuery(startWorld, map);
            Vector2 end = InteractionShape.ToQuery(endWorld, map);
            Vector2 delta = end - start;
            WorldInteractionQuery.Query(InteractionShape.Capsule(start, end, 0f),
                WorldInteractionKind.Climbable, map, scene, candidates);
            retained.Clear();
            nextShapes.Clear();
            bool exited = false;
            bool endSupported = false;
            foreach (WorldInteraction item in candidates)
            {
                if (!ClimbableSurface.IsUsable(item)) continue;
                InteractionShape shape = item.GetShape(map);
                if (shape.IsBox || shape.Radius <= 0.000001f) continue;
                bool geometryChanged = previousShapes.TryGetValue(item, out InteractionShape previous)
                    && ((previous.Center - shape.Center).sqrMagnitude > 0.00000001f
                        || Mathf.Abs(previous.Radius - shape.Radius) > 0.00001f);
                Vector2 relative = start - shape.Center;
                float t = delta.sqrMagnitude > 1e-12f
                    ? Mathf.Clamp01(-Vector2.Dot(relative, delta) / delta.sqrMagnitude) : 0f;
                float closest = (relative + t * delta).magnitude;
                float endDistance = Vector2.Distance(end, shape.Center);
                bool inside = endDistance < shape.Radius;
                if (inside) nextShapes[item] = shape;
                endSupported |= inside;
                // Two percent inward travel re-arms, preventing repeated edge jitter.
                bool isArmed = (!geometryChanged && armed.Contains(item)) || closest < shape.Radius * 0.98f;
                if (inside && isArmed) retained.Add(item);
                if (!geometryChanged && !inside && isArmed && closest < shape.Radius
                    && Vector2.Dot(end - shape.Center, delta) > 0f && item.SlopeStrength01 > 0f)
                    exited = true;
            }
            armed.Clear();
            foreach (WorldInteraction item in retained) armed.Add(item);
            previousShapes.Clear();
            foreach (var entry in nextShapes) previousShapes.Add(entry.Key, entry.Value);
            if (!exited || endSupported || time < nextImpactTime) return false;
            nextImpactTime = time + 0.15f;
            return true;
        }
    }
}
