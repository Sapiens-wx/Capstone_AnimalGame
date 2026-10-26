using System.Collections.Generic;
using AnimalGame.MapTest;
using UnityEngine;

namespace AnimalGame.Animals
{
    public class PerchAtTreeAction : AnimalAction
    {
        protected readonly TreeBirdBehaviour bird;
        protected TreeHabitat tree;
        private readonly float duration;
        private readonly float approachSpeed;
        private readonly bool faceTrunk, playIdleSound;
        private float elapsed, approachElapsed;
        private bool valid, approaching;
        public override AnimalState PhotoState => approaching ? AnimalState.Moving : AnimalState.Resting;
        public PerchAtTreeAction(TreeBirdBehaviour bird, float duration, float? approachSpeed = null, bool? faceTrunk = null, bool playIdleSound = true)
        {
            this.bird = bird; this.duration = duration;
            this.approachSpeed = Mathf.Max(.05f, approachSpeed ?? bird.Settings.CrawlSpeedMetersPerSecond);
            this.faceTrunk = faceTrunk ?? bird.FaceTrunkWhilePerched; this.playIdleSound = playIdleSound;
        }
        public override void Enter(AnimalContext c)
        {
            tree = bird.CurrentTree;
            c.Motor.Stop();
            c.Motor.ClearMovementOverrides();
            c.Motor.SetPerchedMode(true);
            valid = bird.IsUsableTree(tree) && bird.TryGetPerch(tree, out _);
            if (!valid) return;
            c.Motor.SegmentFilter = (a, b) => tree != null && tree.ContainsActivitySegment(c.Agent.Map, a, b, c.Config.BodyRadiusMeters);
            bird.TryGetPerch(tree, out Vector2 target);
            if (Vector2.Distance(c.Motor.CurrentMapPosition, target) > .01f)
            {
                var path = new List<Vector2>();
                valid = tree.TryBuildActivityPath(c.Agent.Map, c.Motor.CurrentMapPosition, target, c.Config.BodyRadiusMeters, path)
                    && c.Motor.SetAerialPath(path, approachSpeed);
                approaching = valid;
            }
            FaceTree(c);
        }
        protected void FaceTree(AnimalContext c)
        {
            if (faceTrunk && tree != null && tree.TryGetMapPosition(c.Agent.Map, out Vector2 centre))
                c.Motor.LockFacing(centre - c.Motor.CurrentMapPosition);
        }
        protected virtual bool IsTreeValid() => bird.IsUsableTree(tree);
        public override AnimalActionStatus Tick(AnimalContext c, float dt)
        {
            if (!valid || !IsTreeValid()) return Fail("The perch is no longer available.");
            if (approaching)
            {
                approachElapsed += dt;
                if (c.Motor.Result == AnimalMovementResult.NoProgress || approachElapsed > c.Config.MaximumTravelTimeSeconds)
                    return Fail("Could not reach the trunk perch.");
                if (!c.Motor.HasArrived) return AnimalActionStatus.Running;
                c.Motor.Stop(); c.Motor.SetPerchedMode(true); approaching = false;
            }
            FaceTree(c);
            elapsed += dt;
            TickPerched(c, dt);
            return elapsed >= duration ? AnimalActionStatus.Succeeded : AnimalActionStatus.Running;
        }
        protected virtual void TickPerched(AnimalContext c, float dt) { if (playIdleSound) bird.TickTreeActivitySound(dt); }
        public override void Exit(AnimalContext c) { base.Exit(c); c.Motor.ClearMovementOverrides(); }
    }

    public sealed class TreeActivityAction : AnimalAction
    {
        private readonly TreeBirdBehaviour bird;
        private readonly float duration;
        private TreeHabitat tree;
        private float elapsed, pause, travel;
        private readonly List<Vector2> path = new();
        private PerchAtTreeAction stationary;
        public override AnimalState PhotoState => stationary?.PhotoState
            ?? (moving ? AnimalState.Moving : AnimalState.Resting);
        private bool moving;
        public TreeActivityAction(TreeBirdBehaviour bird, float duration) { this.bird = bird; this.duration = duration; }
        public override void Enter(AnimalContext c)
        {
            if (bird.StationaryActivity)
            {
                stationary = new PerchAtTreeAction(bird, duration);
                stationary.Enter(c); return;
            }
            tree = bird.CurrentTree;
            c.Motor.Stop(); c.Motor.ClearMovementOverrides(); c.Motor.SetPerchedMode(true);
            c.Motor.SegmentFilter = (a, b) => tree != null && tree.ContainsActivitySegment(c.Agent.Map, a, b, c.Config.BodyRadiusMeters);
        }
        public override AnimalActionStatus Tick(AnimalContext c, float dt)
        {
            if (stationary != null) return stationary.Tick(c, dt);
            if (!bird.IsUsableTree(tree) || !tree.ContainsActivityPoint(c.Agent.Map, c.Motor.CurrentMapPosition, c.Config.BodyRadiusMeters))
                return Fail("The activity tree was removed or its region changed.");
            elapsed += dt;
            bird.TickTreeActivitySound(dt);
            if (elapsed >= duration) return AnimalActionStatus.Succeeded;
            if (moving)
            {
                travel += dt;
                if (c.Motor.HasArrived)
                {
                    c.Motor.Stop(); c.Motor.SetPerchedMode(true); moving = false;
                    pause = Random.Range(Mathf.Max(.05f, bird.Settings.CrawlPauseSeconds.x), Mathf.Max(.05f, bird.Settings.CrawlPauseSeconds.y));
                }
                else if (c.Motor.Result == AnimalMovementResult.NoProgress || travel > c.Config.MaximumTravelTimeSeconds)
                    return Fail("The activity path is blocked or changed.");
                return AnimalActionStatus.Running;
            }
            pause -= dt;
            if (pause > 0f) return AnimalActionStatus.Running;
            // Try several samples, but never spin on a crowded or disappearing habitat.
            for (int i = 0; i < 8; i++)
                if (tree.TrySampleActivityPoint(c.Agent.Map, c.Config.BodyRadiusMeters, out Vector2 target)
                    && tree.TryBuildActivityPath(c.Agent.Map, c.Motor.CurrentMapPosition, target, c.Config.BodyRadiusMeters, path)
                    && c.Motor.SetAerialPath(path, Mathf.Max(.05f, bird.Settings.CrawlSpeedMetersPerSecond)))
                { moving = true; travel = 0f; return AnimalActionStatus.Running; }
            return Fail("No valid same-tree crawl path.");
        }
        public override void Exit(AnimalContext c)
        {
            stationary?.Exit(c);
            base.Exit(c); c.Motor.ClearMovementOverrides();
        }
    }

    public sealed class FlyToTreeAction : AnimalAction
    {
        private readonly TreeBirdBehaviour bird;
        private readonly TreeHabitat target;
        private readonly FlyToTreeActionDefinition definition;
        private bool ownsDefinition;
        private float BaseSpeed => Mathf.Max(.1f, definition.SpeedMetersPerSecond);
        private float SpeedVariation => Mathf.Clamp(definition.FlightSpeedVariation, 0f, .5f);
        private float elapsed, period, phase, travelLimit;
        private bool valid, started, arrived;
        public bool IsCurved { get; private set; }
        public override AnimalState PhotoState => AnimalState.Moving;
        public FlyToTreeAction(TreeBirdBehaviour bird, TreeHabitat target, FlyToTreeActionDefinition definition = null)
        {
            this.bird = bird; this.target = target;
            ownsDefinition = definition == null;
            this.definition = ownsDefinition ? ScriptableObject.CreateInstance<FlyToTreeActionDefinition>() : definition;
            if (ownsDefinition) this.definition.hideFlags = HideFlags.HideAndDontSave;
        }
        public override void Enter(AnimalContext c)
        {
            c.Motor.Stop(); c.Motor.ClearMovementOverrides();
            bird.TargetTree = target;
            if (!bird.TryGetPerch(target, out Vector2 end)) return;
            if (Vector2.Distance(c.Motor.CurrentMapPosition, end) < .01f)
            { valid = true; arrived = true; bird.ArrivedAt(target); return; }
            Vector2 start = c.Motor.CurrentMapPosition;
            var path = new List<Vector2>();
            bool curved = Random.value < definition.ArcProbability;
            float offset = curved ? Mathf.Min(Vector2.Distance(start, end) * definition.ArcOffsetRatio,
                Mathf.Max(0f, definition.MaximumArcOffsetMeters)) * (Random.value < .5f ? -1f : 1f) : 0f;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                BuildFlightPath(start, end, offset, path);
                if (c.Motor.SetAerialPath(path, BaseSpeed))
                { valid = true; IsCurved = Mathf.Abs(offset) > .0001f; break; }
                offset = attempt == 0 ? offset * .5f : 0f;
            }
            if (!valid) return;
            float length = 0f;
            Vector2 previous = start;
            foreach (Vector2 point in path) { length += Vector2.Distance(previous, point); previous = point; }
            travelLimit = Mathf.Max(c.Config.MaximumTravelTimeSeconds,
                length / Mathf.Max(.1f, Mathf.Min(c.Config.FleeSpeedMetersPerSecond,
                    BaseSpeed * (1f - SpeedVariation))) * 1.5f);
            bird.CurrentTree = null;
            started = true;
            Vector2 periods = definition.FlightSpeedPeriodSeconds;
            period = Random.Range(Mathf.Max(.1f, periods.x), Mathf.Max(.1f, Mathf.Max(periods.x, periods.y)));
            phase = Random.Range(0f, Mathf.PI * 2f);
            c.Agent.SoundEmitter?.Emit(AnimalSoundKind.Takeoff);
            c.Agent.SoundEmitter?.BeginMovementSound(AnimalSoundKind.Flying);
            UpdateSpeed(c);
        }
        // One circular arc joins the two perches. Offset is the signed height
        // above the chord at its midpoint, rather than a Bezier control point.
        private static void BuildFlightPath(Vector2 start, Vector2 end, float offset, List<Vector2> path)
        {
            path.Clear();
            Vector2 delta = end - start;
            float distance = delta.magnitude;
            if (Mathf.Abs(offset) <= .0001f || distance <= .0001f)
            { path.Add(end); return; }

            float height = Mathf.Min(Mathf.Abs(offset), distance * .3f);
            float angle = 4f * Mathf.Atan(2f * height / distance);
            float radius = distance / (2f * Mathf.Sin(angle * .5f));
            Vector2 forward = delta / distance;
            Vector2 normal = new Vector2(-forward.y, forward.x) * Mathf.Sign(offset);
            Vector2 midpoint = (start + end) * .5f;
            int count = Mathf.Clamp(Mathf.CeilToInt(radius * angle / .25f), 8, 2048);
            for (int i = 1; i < count; i++)
            {
                float a = (i / (float)count - .5f) * angle;
                float halfSine = Mathf.Sin(a * .5f);
                path.Add(midpoint + forward * (radius * Mathf.Sin(a))
                    + normal * (height - 2f * radius * halfSine * halfSine));
            }
            path.Add(end);
        }
        private void UpdateSpeed(AnimalContext c)
        {
            float speed = BaseSpeed
                * (1f + SpeedVariation * Mathf.Sin(phase + elapsed * 2f * Mathf.PI / period));
            c.Motor.SetMovementSpeed(Mathf.Min(c.Config.FleeSpeedMetersPerSecond, speed));
        }
        public override AnimalActionStatus Tick(AnimalContext c, float dt)
        {
            if (!valid || !bird.IsUsableTree(target)) return Fail("The destination tree or flight path is unavailable.");
            if (arrived) return AnimalActionStatus.Succeeded;
            elapsed += dt;
            if (c.Motor.HasArrived)
            {
                arrived = true;
                c.Motor.Stop(); bird.ArrivedAt(target);
                if (started) c.Agent.SoundEmitter?.Emit(AnimalSoundKind.Landing);
                return AnimalActionStatus.Succeeded;
            }
            if (elapsed > travelLimit || c.Motor.Result == AnimalMovementResult.NoProgress)
                return Fail("Flight timed out or was blocked.");
            UpdateSpeed(c);
            return AnimalActionStatus.Running;
        }
        public override void Exit(AnimalContext c)
        {
            base.Exit(c); c.Motor.ClearMovementOverrides();
            if (!arrived) bird.FlightFailed();
            else c.Motor.SetPerchedMode(true);
            if (ownsDefinition)
            {
                ownsDefinition = false;
                if (Application.isPlaying) Object.Destroy(definition);
                else Object.DestroyImmediate(definition);
            }
        }
    }

    public sealed class BirdFleeAction : AnimalAction
    {
        private readonly TreeBirdBehaviour bird;
        private Vector2 direction, threatPosition;
        private bool hasThreatPosition;
        private float offscreenTime, retryTime;
        public Vector2 Direction => direction;
        public override AnimalState PhotoState => AnimalState.Fleeing;
        public BirdFleeAction(TreeBirdBehaviour bird) { this.bird = bird; }
        public override void Enter(AnimalContext c)
        {
            c.Motor.Stop(); c.Motor.ClearMovementOverrides();
            bird.CurrentTree = null; bird.TargetTree = null;
            Transform threat = c.Agent.Perception.PrimaryThreat;
            hasThreatPosition = threat != null && c.Agent.Map.TrySampleWorldPosition(threat.position, out threatPosition, out _);
            if (!hasThreatPosition) hasThreatPosition = c.Agent.Perception.TryGetPlayerMapPosition(out threatPosition);
            Vector2 away = hasThreatPosition ? c.Motor.CurrentMapPosition - threatPosition : c.Motor.FacingMapDirection;
            if (away.sqrMagnitude < .000001f) away = c.Motor.FacingMapDirection;
            direction = Rotate(away.normalized, Random.Range(-60f, 60f));
            c.Agent.SoundEmitter?.Emit(AnimalSoundKind.Fleeing);
            c.Agent.SoundEmitter?.BeginMovementSound(AnimalSoundKind.Fleeing);
            SetNextTarget(c);
        }
        private static Vector2 Rotate(Vector2 value, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(value.x * Mathf.Cos(radians) - value.y * Mathf.Sin(radians),
                value.x * Mathf.Sin(radians) + value.y * Mathf.Cos(radians));
        }
        private bool SetNextTarget(AnimalContext c)
        {
            Vector2 current = c.Motor.CurrentMapPosition;
            float stride = Mathf.Max(.3f, c.Config.FleeSpeedMetersPerSecond * .35f);
            // Keep the sampled heading until blocked. At map edges choose the longest
            // playable continuation, preferring headings away from the remembered threat.
            if (c.Motor.CanFlySegment(current, current + direction * stride))
                return c.Motor.SetAerialTarget(current + direction * stride, c.Config.FleeSpeedMetersPerSecond);
            Vector2 best = default;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < 24; i++)
            {
                Vector2 candidate = Rotate(direction, i * 15f);
                for (float length = stride; length >= .15f; length *= .5f)
                {
                    Vector2 end = current + candidate * length;
                    if (!c.Motor.CanFlySegment(current, end)) continue;
                    float score = length + Vector2.Dot(candidate, direction) * .1f;
                    if (hasThreatPosition) score += (Vector2.Distance(end, threatPosition) - Vector2.Distance(current, threatPosition)) * .4f;
                    if (score > bestScore) { bestScore = score; best = candidate * length; }
                    break;
                }
            }
            if (best.sqrMagnitude < .00001f) { c.Motor.Stop(); return false; }
            direction = best.normalized;
            return c.Motor.SetAerialTarget(current + best, c.Config.FleeSpeedMetersPerSecond);
        }
        public override AnimalActionStatus Tick(AnimalContext c, float dt)
        {
            if (c.Agent.Perception.TryGetPlayerMapPosition(out Vector2 player))
            { threatPosition = player; hasThreatPosition = true; }
            Camera camera = c.Agent.Simulation.World.Camera;
            bool outside = camera == null;
            if (camera != null)
            {
                Vector3 view = camera.WorldToViewportPoint(c.Agent.transform.position);
                outside = view.z < 0f || view.x < -.05f || view.x > 1.05f || view.y < -.05f || view.y > 1.05f;
            }
            bool distant = hasThreatPosition && Vector2.Distance(c.Motor.CurrentMapPosition, threatPosition)
                > c.Config.AlertRadiusMeters * Mathf.Max(1f, bird.Settings.FleeSafeDistanceMultiplier);
            offscreenTime = outside && distant ? offscreenTime + dt : 0f;
            if (offscreenTime >= Mathf.Max(.01f, bird.Settings.FleeOffscreenDelaySeconds))
            { c.Agent.Despawn(); return AnimalActionStatus.Running; }
            retryTime -= dt;
            if ((!c.Motor.HasTarget || c.Motor.Result == AnimalMovementResult.Blocked
                || c.Motor.Result == AnimalMovementResult.NoProgress) && retryTime <= 0f)
            { SetNextTarget(c); retryTime = .15f; }
            return AnimalActionStatus.Running;
        }
        public override void Exit(AnimalContext c) { base.Exit(c); c.Motor.ClearMovementOverrides(); }
    }
}
