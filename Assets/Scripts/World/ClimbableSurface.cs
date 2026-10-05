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

        public static InteractionShape GetContactShape(WorldInteraction item, MapTestSceneController map,
            float bodyRadius = 0f)
        {
            InteractionShape shape = item.GetShape(map);
            return !shape.IsBox && item.ClimbableUseBodyOverlap
                ? InteractionShape.Capsule(shape.Center, shape.Center, shape.Radius + Mathf.Max(0f, bodyRadius))
                : shape;
        }

        public static float AreaSpeedMultiplier(Vector2 worldPosition, MapTestSceneController map,
            Scene scene, List<WorldInteraction> scratch, float bodyRadius = 0f)
        {
            Vector2 point = InteractionShape.ToQuery(worldPosition, map);
            WorldInteractionQuery.Query(InteractionShape.Capsule(point, point, Mathf.Max(0f, bodyRadius)),
                WorldInteractionKind.Climbable, map, scene, scratch);
            float multiplier = 1f;
            foreach (WorldInteraction item in scratch)
            {
                if (!IsUsable(item) || !item.ClimbableAffectsWholeArea) continue;
                InteractionShape shape = GetContactShape(item, map, bodyRadius);
                if (!shape.IsBox && Vector2.Distance(point, shape.Center) < shape.Radius)
                    multiplier = Mathf.Min(multiplier, item.ClimbableEntrySpeedMultiplier);
            }
            return multiplier;
        }

        public static ClimbableSurface Sample(Vector2 worldPosition, MapTestSceneController map,
            Scene scene, List<WorldInteraction> scratch, WorldInteraction preferred = null, float bodyRadius = 0f)
        {
            Vector2 point = InteractionShape.ToQuery(worldPosition, map);
            WorldInteractionQuery.Query(InteractionShape.Capsule(point, point, Mathf.Max(0f, bodyRadius)),
                WorldInteractionKind.Climbable, map, scene, scratch);
            ClimbableSurface best = default;
            foreach (WorldInteraction item in scratch)
            {
                if (!IsUsable(item)) continue;
                InteractionShape shape = GetContactShape(item, map, bodyRadius);
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

    /// <summary>
    /// Plans entry-only or whole-area resistance along a swept body path. Only Commit consumes the
    /// planned state, so a wall or a rejected push cannot consume a bush entry.
    /// </summary>
    public sealed class ClimbableEntryResistance
    {
        private struct Episode
        {
            public bool Connected, Active, Ready, WholeArea;
            public float EntrySpeed, Elapsed, Multiplier, BlendDuration;
            public WorldInteraction Source;
            public Vector2 Center;
            public float Radius;
        }
        private struct Span
        {
            public WorldInteraction Source;
            public float Enter, Exit, TopEnter, TopExit;
            public InteractionShape Shape;
        }
        private readonly List<WorldInteraction> candidates = new();
        private readonly List<Span> spans = new();
        private Episode episode = new() { Ready = true };
        private Episode pending;
        private bool hasPlan;
        private float planSpeedLimit;
        public float VelocityScale { get; private set; } = 1f;
        public Vector2 VelocityLoss { get; private set; }

        public void Reset()
        {
            episode = new Episode { Ready = true };
            hasPlan = false;
            VelocityScale = 1f;
            VelocityLoss = Vector2.zero;
        }

        public void Commit()
        {
            if (hasPlan) episode = pending;
            hasPlan = false;
        }

        public Vector2 Plan(Vector2 startWorld, Vector2 endWorld, MapTestSceneController map,
            Scene scene, float deltaTime, float bodyRadius = 0f, float normalSpeedLimit = float.PositiveInfinity)
        {
            VelocityScale = 1f;
            VelocityLoss = Vector2.zero;
            Episode work = episode;
            pending = work;
            hasPlan = true;
            Vector2 displacement = endWorld - startWorld;
            float length = displacement.magnitude;
            if (deltaTime <= 0f || length <= 0.000001f) return endWorld;
            Vector2 start = InteractionShape.ToQuery(startWorld, map);
            Vector2 queryDirection = (InteractionShape.ToQuery(endWorld, map) - start) / length;
            WorldInteractionQuery.Query(InteractionShape.Capsule(start,
                start + queryDirection * length, Mathf.Max(0f, bodyRadius)), WorldInteractionKind.Climbable, map, scene, candidates);
            spans.Clear();
            foreach (WorldInteraction item in candidates)
            {
                if (!ClimbableSurface.IsUsable(item) || item.ClimbableEntrySpeedMultiplier >= 1f) continue;
                InteractionShape shape = ClimbableSurface.GetContactShape(item, map, bodyRadius);
                if (shape.IsBox || !Interval(start - shape.Center, queryDirection, shape.Radius,
                        out float enter, out float exit) || exit <= 0f || enter >= length) continue;
                bool hasTop = Interval(start - shape.Center, queryDirection,
                    shape.Radius * item.TopRadiusRatio01, out float topEnter, out float topExit);
                spans.Add(new Span { Source = item, Enter = enter, Exit = exit, Shape = shape,
                    TopEnter = hasTop ? topEnter : float.PositiveInfinity,
                    TopExit = hasTop ? topExit : float.PositiveInfinity });
            }
            if (work.Source != null)
            {
                InteractionShape current = ClimbableSurface.GetContactShape(work.Source, map, bodyRadius);
                if (!ClimbableSurface.IsUsable(work.Source) || work.Source.gameObject.scene != scene
                    || (current.Center - work.Center).sqrMagnitude > 0.00000001f
                    || Mathf.Abs(current.Radius - work.Radius) > 0.00001f)
                    work = new Episode { Ready = true };
            }

            // Circle boundaries are events. Small integration slices only cover
            // the 80 ms blend, including the portion after entry in the same frame.
            float remaining = deltaTime, travelled = 0f;
            float originalSpeed = length / deltaTime, speed = originalSpeed;
            planSpeedLimit = float.IsPositiveInfinity(normalSpeedLimit) ? originalSpeed : Mathf.Max(0f, normalSpeedLimit);
            const float epsilon = 0.000001f;
            while (remaining > 0.0000001f)
            {
                bool supported = false, onTop = false, wholeArea = false;
                Span source = default;
                float multiplier = 1f, blend = 0.001f;
                float boundary = float.PositiveInfinity;
                foreach (Span span in spans)
                {
                    if (span.Enter > travelled + epsilon) boundary = Mathf.Min(boundary, span.Enter);
                    if (span.Exit > travelled + epsilon) boundary = Mathf.Min(boundary, span.Exit);
                    if (span.TopEnter > travelled + epsilon) boundary = Mathf.Min(boundary, span.TopEnter);
                    if (span.TopExit > travelled + epsilon) boundary = Mathf.Min(boundary, span.TopExit);
                    if (travelled + epsilon < span.Enter || travelled + epsilon >= span.Exit) continue;
                    supported = true;
                    wholeArea |= span.Source.ClimbableAffectsWholeArea;
                    onTop |= travelled + epsilon >= span.TopEnter && travelled + epsilon < span.TopExit;
                    if (span.Source.ClimbableEntrySpeedMultiplier < multiplier)
                    {
                        source = span;
                        multiplier = span.Source.ClimbableEntrySpeedMultiplier;
                    }
                    blend = Mathf.Max(blend, span.Source.ClimbableEntryBlendDuration);
                }
                if (!supported)
                {
                    work.Connected = work.Active = false;
                    Vector2 point = start + queryDirection * travelled;
                    if (work.Source == null || Vector2.Distance(point, work.Center) >= work.Radius * 1.02f)
                        work.Ready = true; // Ignore repeated tiny edge crossings.
                    float duration = float.IsPositiveInfinity(boundary)
                        ? remaining : Mathf.Min(remaining, (boundary - travelled) / speed);
                    travelled += speed * duration;
                    remaining -= duration;
                    continue;
                }
                if (!work.Connected)
                {
                    work.Connected = true;
                    work.Active = work.Ready || wholeArea;
                    bool freshEntry = work.Ready;
                    work.Ready = false;
                    work.EntrySpeed = speed;
                    if (freshEntry) work.Elapsed = 0f;
                    work.Multiplier = multiplier;
                    // A tiny bush can be crossed before 80 ms at normal speed.
                    // Finish the entry blend within its inbound band rather than
                    // releasing the cap on the top before resistance is felt.
                    float inboundDistance = source.TopEnter - travelled;
                    work.BlendDuration = Mathf.Max(0.001f, Mathf.Min(blend,
                        inboundDistance > 0f ? inboundDistance / speed : blend));
                }
                work.Source = source.Source;
                work.Center = source.Shape.Center;
                work.Radius = source.Shape.Radius;
                work.WholeArea = wholeArea;
                work.Multiplier = wholeArea ? multiplier : Mathf.Min(work.Multiplier, multiplier);
                if (wholeArea) work.Active = true;
                else if (onTop) work.Active = false; // Legacy entry-only props release on the top.
                float step = work.Active ? Mathf.Min(remaining, 1f / 240f) : remaining;
                float distance = DistanceOver(speed, work, step);
                if (travelled + distance > boundary)
                {
                    // Solve the event time, rather than slowing the entire frame
                    // before its entry point or crossing the flat top unnoticed.
                    float low = 0f, high = step;
                    for (int i = 0; i < 18; i++)
                    {
                        float mid = (low + high) * 0.5f;
                        if (DistanceOver(speed, work, mid) < boundary - travelled) low = mid;
                        else high = mid;
                    }
                    step = high;
                    distance = boundary - travelled;
                }
                if (work.Active)
                {
                    work.Elapsed += step;
                    speed = SpeedAt(speed, work, work.Elapsed);
                }
                travelled += distance;
                remaining -= step;
            }
            // Keep the reduced momentum after leaving; releasing the cap does not
            // restore lost velocity or create an exit kick. It is never compounded.
            VelocityScale = Mathf.Clamp01(speed / originalSpeed);
            VelocityLoss = displacement / deltaTime * (1f - VelocityScale);
            pending = work;
            return startWorld + displacement / length * travelled;
        }

        private float SpeedAt(float freeSpeed, Episode state, float elapsed)
        {
            if (!state.Active) return freeSpeed;
            float blend = Mathf.SmoothStep(0f, 1f, elapsed / state.BlendDuration);
            float basis = state.WholeArea ? planSpeedLimit : state.EntrySpeed;
            return Mathf.Min(freeSpeed, basis * Mathf.Lerp(1f, state.Multiplier, blend));
        }
        private float DistanceOver(float speed, Episode state, float duration)
        {
            // Simpson integration is exact for the cubic blend away from a cap.
            return duration / 6f * (SpeedAt(speed, state, state.Elapsed)
                + 4f * SpeedAt(speed, state, state.Elapsed + duration * 0.5f)
                + SpeedAt(speed, state, state.Elapsed + duration));
        }
        private static bool Interval(Vector2 relative, Vector2 direction, float radius,
            out float enter, out float exit)
        {
            float a = direction.sqrMagnitude;
            float b = Vector2.Dot(relative, direction);
            enter = exit = 0f;
            if (a <= 0.000000000001f) return false;
            Vector2 perpendicular = relative - direction * (b / a);
            float penetrationSquared = radius * radius - perpendicular.sqrMagnitude;
            if (penetrationSquared <= radius * radius * 0.000001f) return false;
            float root = Mathf.Sqrt(penetrationSquared / a);
            enter = -b / a - root;
            exit = -b / a + root;
            return true;
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
        private bool hasFeedback;
        private float cameraMultiplier, rumbleMultiplier, durationMultiplier;
        public float LandingCameraMultiplier { get; private set; } = 1f;
        public float LandingRumbleMultiplier { get; private set; } = 1f;
        public float LandingDurationMultiplier { get; private set; } = 1f;

        public void Reset()
        {
            armed.Clear();
            retained.Clear();
            previousShapes.Clear();
            nextShapes.Clear();
            nextImpactTime = float.NegativeInfinity;
            hasFeedback = false;
            LandingCameraMultiplier = LandingRumbleMultiplier = LandingDurationMultiplier = 1f;
        }

        public bool Move(Vector2 startWorld, Vector2 endWorld, MapTestSceneController map,
            Scene scene, float time, float bodyRadius = 0f)
        {
            Vector2 start = InteractionShape.ToQuery(startWorld, map);
            Vector2 end = InteractionShape.ToQuery(endWorld, map);
            Vector2 delta = end - start;
            WorldInteractionQuery.Query(InteractionShape.Capsule(start, end, Mathf.Max(0f, bodyRadius)),
                WorldInteractionKind.Climbable, map, scene, candidates);
            retained.Clear();
            nextShapes.Clear();
            bool exited = false;
            bool endSupported = false;
            foreach (WorldInteraction item in candidates)
            {
                if (!ClimbableSurface.IsUsable(item)) continue;
                InteractionShape shape = ClimbableSurface.GetContactShape(item, map, bodyRadius);
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
                if (!geometryChanged && isArmed && closest < shape.Radius && item.SlopeStrength01 > 0f)
                {
                    // Keep the entire crossing's profile after Source is cleared at exit.
                    // Mixed garbage/bush crossings retain the stronger garbage feedback.
                    cameraMultiplier = hasFeedback ? Mathf.Max(cameraMultiplier, item.ClimbableCameraMultiplier) : item.ClimbableCameraMultiplier;
                    rumbleMultiplier = hasFeedback ? Mathf.Max(rumbleMultiplier, item.ClimbableRumbleMultiplier) : item.ClimbableRumbleMultiplier;
                    durationMultiplier = hasFeedback ? Mathf.Max(durationMultiplier, item.ClimbableLandingDurationMultiplier) : item.ClimbableLandingDurationMultiplier;
                    hasFeedback = true;
                }
                if (inside && isArmed) retained.Add(item);
                if (!geometryChanged && !inside && isArmed && closest < shape.Radius
                    && Vector2.Dot(end - shape.Center, delta) > 0f && item.SlopeStrength01 > 0f)
                    exited = true;
            }
            armed.Clear();
            foreach (WorldInteraction item in retained) armed.Add(item);
            previousShapes.Clear();
            foreach (var entry in nextShapes) previousShapes.Add(entry.Key, entry.Value);
            if (endSupported) return false;
            LandingCameraMultiplier = hasFeedback ? cameraMultiplier : 1f;
            LandingRumbleMultiplier = hasFeedback ? rumbleMultiplier : 1f;
            LandingDurationMultiplier = hasFeedback ? durationMultiplier : 1f;
            hasFeedback = false;
            if (!exited || time < nextImpactTime) return false;
            nextImpactTime = time + 0.15f;
            return true;
        }
    }
}
