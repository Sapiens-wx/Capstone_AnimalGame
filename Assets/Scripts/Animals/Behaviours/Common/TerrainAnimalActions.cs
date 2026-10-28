using AnimalGame.MapTest;
using AnimalGame.World;
using UnityEngine;

namespace AnimalGame.Animals
{
    internal static class AnimalDailyTargets
    {
        public static bool CanRoam(AnimalContext c, out string reason)
        {
            reason = null;
            for (int i = 0; i < 40; i++)
                if (c.Motor.CanOccupyMapPosition(c.Agent.ActivityCenter + Random.insideUnitCircle * c.Config.ActivityRadiusMeters)) return true;
            reason = "No playable roaming position is available."; return false;
        }
        public static bool CanEatPlant(AnimalContext c, bool nearby, out string reason)
        {
            foreach (AnimalFoodSource source in AnimalFoodSource.ActiveSources)
                if (source != null && source.isActiveAndEnabled && source.gameObject.scene == c.Agent.gameObject.scene
                    && source.SelectionWeight * c.Config.GetFoodSelectionWeight(source.FoodType) > 0f
                    && source.TryGetMapPosition(c.Agent.Map, out Vector2 point)
                    && Vector2.Distance(point, c.Motor.CurrentMapPosition) <= (nearby ? c.Config.NearbyFoodDistanceMeters : c.Config.ActivityRadiusMeters))
                { reason = null; return true; }
            reason = nearby ? "No suitable nearby plant is available." : "No suitable plant is within the activity radius.";
            return false;
        }
        public static bool Matches(TerrainKind actual, TerrainKind desired) => actual == desired
            || (desired == TerrainKind.Grass && (actual == TerrainKind.Grassland || actual == TerrainKind.GrassTest));
        public static bool Terrain(AnimalContext c, TerrainKind kind, float maxSlope, out Vector2 target)
        {
            Vector2 origin = c.Motor.CurrentMapPosition;
            Vector2 searchCenter = c.Agent.Behaviour is AmericanMarmotBehaviour ? c.Agent.ActivityCenter : origin;
            target = origin;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < 48; i++)
            {
                Vector2 p = i == 0 ? searchCenter : searchCenter + Random.insideUnitCircle * c.Config.ActivityRadiusMeters;
                if (!AnimalTerrainQuery.TrySample(c.Agent.Map, p, out var sample) || !Matches(sample.Terrain, kind)
                    || sample.SlopeDegrees >= maxSlope || !c.Motor.CanOccupyMapPosition(p)) continue;
                float distance = Vector2.SqrMagnitude(p - origin);
                if (distance >= nearest) continue;
                nearest = distance; target = p;
            }
            return !float.IsPositiveInfinity(nearest);
        }
        public static bool Plant(AnimalContext c, out Transform plant, out Vector2 target, out Vector2 center)
        {
            Transform selected = null;
            Vector2 chosen = default, plantCenter = default;
            float best = float.PositiveInfinity;
            foreach (TreeHabitat tree in TreeHabitat.ActiveTrees)
                if (tree != null && tree.isActiveAndEnabled && tree.gameObject.scene == c.Agent.gameObject.scene
                    && tree.TryGetMapPosition(c.Agent.Map, out Vector2 p)) Consider(tree.transform, p, tree.PerchRadiusMeters);
            foreach (AnimalFoodSource bush in AnimalFoodSource.ActiveSources)
                if (bush != null && bush.isActiveAndEnabled && bush.FoodType == AnimalFoodType.Bush
                    && bush.gameObject.scene == c.Agent.gameObject.scene && bush.TryGetMapPosition(c.Agent.Map, out Vector2 p))
                    Consider(bush.transform, p, .3f);
            plant = selected; target = chosen; center = plantCenter;
            return selected != null;

            void Consider(Transform source, Vector2 p, float radius)
            {
                float distance = Vector2.Distance(c.Motor.CurrentMapPosition, p);
                if (distance > c.Config.ActivityRadiusMeters || distance >= best) return;
                WorldInteraction obstacle = source.GetComponentInChildren<WorldInteraction>();
                if (obstacle != null)
                {
                    InteractionShape shape = obstacle.GetShape(c.Agent.Map);
                    radius = Mathf.Max(radius, shape.IsBox
                        ? Mathf.Max(Vector2.Distance(shape.Center, shape.A), Vector2.Distance(shape.Center, shape.C)) : shape.Radius);
                }
                radius += c.Config.BodyRadiusMeters + c.Config.FoodApproachPaddingMeters;
                Vector2 outward = (c.Motor.CurrentMapPosition - p).normalized;
                if (outward.sqrMagnitude < .001f) outward = Vector2.up;
                for (int i = 0; i < 16; i++)
                {
                    Vector2 candidate = p + SlopeEscapeAction.Rotate(outward, i * 22.5f) * radius;
                    if (!c.Motor.CanOccupyMapPosition(candidate)) continue;
                    best = distance; selected = source; chosen = candidate; plantCenter = p;
                    break;
                }
            }
        }
    }

    public sealed class LookAroundAction : AnimalAction
    {
        private readonly float duration;
        private readonly float turnSpeed;
        private float elapsed, lookCountdown;
        private Vector2 initialFacing;
        public LookAroundAction(float duration, float turnSpeed = 35f) { this.duration = duration; this.turnSpeed = turnSpeed; }
        public override AnimalState PhotoState => AnimalState.Resting;
        public override void Enter(AnimalContext c) { c.Motor.Stop(); initialFacing = c.Motor.FacingMapDirection; c.Motor.FacingTurnSpeedDegreesPerSecond = turnSpeed; }
        public override AnimalActionStatus Tick(AnimalContext c, float dt)
        {
            elapsed += dt; lookCountdown -= dt;
            if (lookCountdown <= 0f)
            {
                lookCountdown = c.Config.ChooseLookInterval();
                c.Motor.FaceMapDirection(SlopeEscapeAction.Rotate(initialFacing, Random.Range(-c.Config.LookAngleDegrees, c.Config.LookAngleDegrees)));
                c.Agent.SoundEmitter?.Emit(AnimalSoundKind.Looking);
            }
            return elapsed >= duration ? AnimalActionStatus.Succeeded : AnimalActionStatus.Running;
        }
        public override void Exit(AnimalContext c) { c.Motor.ClearMovementOverrides(); base.Exit(c); }
    }

    public sealed class RestAtTerrainAction : AnimalAction
    {
        private readonly float duration, maxSlope;
        private readonly TerrainKind terrain;
        private readonly AnimalState restingState;
        private readonly bool howl;
        private Vector2 target;
        private bool valid, resting;
        private float elapsed, travel, soundCountdown;
        public RestAtTerrainAction(float duration, TerrainKind terrain, float maxSlope = 90f,
            AnimalState restingState = AnimalState.Resting, bool howl = false)
        { this.duration = duration; this.terrain = terrain; this.maxSlope = maxSlope; this.restingState = restingState; this.howl = howl; }
        public override string Name => howl ? "HowlOnRock" : restingState == AnimalState.Eating ? "GrazeOnTerrain" : "RestAtTerrain";
        public override AnimalState PhotoState => resting ? restingState : AnimalState.Moving;
        public override void Enter(AnimalContext c)
        {
            valid = AnimalDailyTargets.Terrain(c, terrain, maxSlope, out target)
                && c.Motor.SetTarget(target, c.Config.DailyMoveSpeedMetersPerSecond);
        }
        public override AnimalActionStatus Tick(AnimalContext c, float dt)
        {
            if (!valid) return AnimalActionStatus.Failed;
            if (!resting)
            {
                travel += dt;
                if (travel > c.Config.MaximumTravelTimeSeconds || c.Motor.Result == AnimalMovementResult.NoProgress) return AnimalActionStatus.Failed;
                if (!c.Motor.HasArrived) return AnimalActionStatus.Running;
                resting = true; c.Motor.Stop();
            }
            if (!AnimalTerrainQuery.TrySample(c.Agent.Map, c.Motor.CurrentMapPosition, out var ground)
                || !AnimalDailyTargets.Matches(ground.Terrain, terrain) || ground.SlopeDegrees >= maxSlope) return AnimalActionStatus.Failed;
            if (howl && c.Agent.SoundEmitter != null) c.Agent.SoundEmitter.TickRepeated(AnimalSoundKind.Howling, ref soundCountdown, dt);
            elapsed += dt;
            return elapsed >= duration ? AnimalActionStatus.Succeeded : AnimalActionStatus.Running;
        }
    }

    public sealed class RestNearPlantAction : AnimalAction
    {
        private readonly float duration;
        private readonly bool marking;
        private Transform plant;
        private Vector2 target, center;
        private bool arrived;
        private float travel, elapsed;
        public RestNearPlantAction(float duration, bool marking = false) { this.duration = duration; this.marking = marking; }
        public override string Name => marking ? "MarkNearPlant" : "RestNearPlant";
        public override AnimalState PhotoState => arrived ? AnimalState.Resting : AnimalState.Moving;
        public override void Enter(AnimalContext c)
        {
            if (AnimalDailyTargets.Plant(c, out plant, out target, out center))
                c.Motor.SetTarget(target, c.Config.DailyMoveSpeedMetersPerSecond);
        }
        public override AnimalActionStatus Tick(AnimalContext c, float dt)
        {
            if (plant == null || !plant.gameObject.activeInHierarchy) return AnimalActionStatus.Failed;
            if (!arrived)
            {
                travel += dt;
                if (travel > c.Config.MaximumTravelTimeSeconds || c.Motor.Result == AnimalMovementResult.NoProgress) return AnimalActionStatus.Failed;
                if (!c.Motor.HasArrived) return AnimalActionStatus.Running;
                arrived = true; c.Motor.Stop();
            }
            if (c.Motor.IsDisplaced || Vector2.Distance(c.Motor.CurrentMapPosition, target) > c.Config.ArrivalDistanceMeters * 2f) return AnimalActionStatus.Failed;
            if (marking)
            {
                Vector2 radial = center - c.Motor.CurrentMapPosition;
                c.Motor.FaceMapDirection(new Vector2(-radial.y, radial.x));
            }
            elapsed += dt;
            return elapsed >= duration ? AnimalActionStatus.Succeeded : AnimalActionStatus.Running;
        }
    }

    public sealed class TerrainWanderAction : AnimalAction
    {
        private readonly float duration, speedMultiplier;
        private float elapsed;
        private bool valid, approaching;
        private float travel;
        public TerrainWanderAction(float duration, float speedMultiplier = .45f) { this.duration = duration; this.speedMultiplier = speedMultiplier; }
        public override AnimalState PhotoState => AnimalState.Moving;
        public override void Enter(AnimalContext c)
        {
            valid = OnRock(c, c.Motor.CurrentMapPosition);
            if (!valid && AnimalDailyTargets.Terrain(c, TerrainKind.Rock, 90f, out Vector2 target))
            { valid = c.Motor.SetTarget(target, c.Config.DailyMoveSpeedMetersPerSecond); approaching = valid; }
            if (valid && !approaching) c.Motor.SegmentFilter = (a, b) => OnRock(c, a) && OnRock(c, b);
        }
        private static bool OnRock(AnimalContext c, Vector2 p) => WorldTerrainQuery.TrySampleMapPosition(c.Agent.Map, p, out var kind) && kind == TerrainKind.Rock;
        public override AnimalActionStatus Tick(AnimalContext c, float dt)
        {
            if (!valid) return AnimalActionStatus.Failed;
            if (approaching)
            {
                travel += dt;
                if (travel > c.Config.MaximumTravelTimeSeconds || c.Motor.Result == AnimalMovementResult.NoProgress) return AnimalActionStatus.Failed;
                if (!c.Motor.HasArrived) return AnimalActionStatus.Running;
                if (!OnRock(c, c.Motor.CurrentMapPosition)) return AnimalActionStatus.Failed;
                approaching = false;
                c.Motor.SegmentFilter = (a, b) => OnRock(c, a) && OnRock(c, b);
            }
            elapsed += dt;
            if (elapsed >= duration) return AnimalActionStatus.Succeeded;
            if (c.Motor.HasTarget && c.Motor.Result != AnimalMovementResult.NoProgress) return AnimalActionStatus.Running;
            for (int i = 0; i < 24; i++)
            {
                Vector2 target = c.Motor.CurrentMapPosition + Random.insideUnitCircle.normalized * Mathf.Max(.6f, c.Config.ArrivalDistanceMeters * 3f);
                if (c.Motor.CanTraverseSegment(c.Motor.CurrentMapPosition, target)
                    && c.Motor.SetTarget(target, c.Config.DailyMoveSpeedMetersPerSecond * speedMultiplier)) return AnimalActionStatus.Running;
            }
            return AnimalActionStatus.Failed;
        }
        public override void Exit(AnimalContext c) { c.Motor.ClearMovementOverrides(); base.Exit(c); }
    }

    public sealed class SniffWanderAction : AnimalAction
    {
        private readonly float duration, speedMultiplier;
        private float elapsed, segmentTime, lookTime;
        private Vector2 heading;
        private HuntAction feeding;
        public SniffWanderAction(float duration, float speedMultiplier = .4f) { this.duration = duration; this.speedMultiplier = speedMultiplier; }
        public override string Name => feeding != null ? "Sniff/" + feeding.Name : "SniffWander";
        public override AnimalState PhotoState => feeding?.PhotoState ?? AnimalState.Moving;
        public override void Enter(AnimalContext c) { c.Motor.Stop(); }
        public override AnimalActionStatus Tick(AnimalContext c, float dt)
        {
            if (feeding != null) return feeding.Tick(c, dt);
            elapsed += dt; segmentTime -= dt; lookTime -= dt;
            if (segmentTime <= 0f || c.Motor.HasArrived || c.Motor.Result == AnimalMovementResult.NoProgress)
            {
                // Search after a sniffing segment, never before the first one.
                AnimalAgent corpse = elapsed > dt ? AnimalHuntTargets.Select(c, null, true) : null;
                if (corpse != null)
                {
                    c.Motor.ClearMovementOverrides();
                    feeding = new HuntAction(corpse, true); feeding.Enter(c);
                    return AnimalActionStatus.Running;
                }
                if (elapsed >= duration) return AnimalActionStatus.Succeeded;
                segmentTime = Random.Range(2f, 4f);
                bool found = false;
                for (int i = 0; i < 24; i++)
                {
                    heading = Random.insideUnitCircle.normalized;
                    Vector2 p = c.Motor.CurrentMapPosition + heading * Mathf.Max(.6f, c.Config.DailyMoveSpeedMetersPerSecond * speedMultiplier * segmentTime);
                    if (!c.Motor.CanTraverseSegment(c.Motor.CurrentMapPosition, p)) continue;
                    c.Motor.SetTarget(p, c.Config.DailyMoveSpeedMetersPerSecond * speedMultiplier); found = true; break;
                }
                if (!found) return AnimalActionStatus.Failed;
            }
            if (elapsed >= duration) return AnimalActionStatus.Succeeded;
            if (lookTime <= 0f)
            {
                lookTime = Random.Range(.3f, .65f);
                c.Motor.LockFacing(SlopeEscapeAction.Rotate(heading, Random.Range(-20f, 20f)));
            }
            return AnimalActionStatus.Running;
        }
        public override void Exit(AnimalContext c) { feeding?.Exit(c); feeding = null; c.Motor.ClearMovementOverrides(); base.Exit(c); }
    }
}
