using AnimalGame.MapTest;
using AnimalGame.World;
using UnityEngine;

namespace AnimalGame.Animals
{
    public sealed class SlowWanderAction : AnimalAction
    {
        private readonly float duration, speed;
        private float elapsed;
        public SlowWanderAction(float duration, float speed) { this.duration = duration; this.speed = speed; }
        public override AnimalState PhotoState => AnimalState.Moving;
        public override void Enter(AnimalContext c) { c.Motor.Stop(); }
        public override AnimalActionStatus Tick(AnimalContext c, float dt)
        {
            elapsed += dt;
            if (elapsed >= duration) return AnimalActionStatus.Succeeded;
            if (c.Motor.HasTarget && c.Motor.Result != AnimalMovementResult.NoProgress) return AnimalActionStatus.Running;
            for (int i = 0; i < 32; i++)
            {
                Vector2 target = c.Agent.ActivityCenter + Random.insideUnitCircle * c.Config.ActivityRadiusMeters;
                if (c.Motor.CanOccupyMapPosition(target) && c.Motor.SetTarget(target, c.Config.DailyMoveSpeedMetersPerSecond * speed))
                    return AnimalActionStatus.Running;
            }
            return Fail("No walkable roaming target.");
        }
    }

    public sealed class BushOrGrassEatingAction : AnimalAction
    {
        private readonly float duration;
        private AnimalAction eating;
        public BushOrGrassEatingAction(float duration) { this.duration = duration; }
        public override AnimalState PhotoState => eating?.PhotoState ?? AnimalState.Moving;
        public override void Enter(AnimalContext c)
        {
            // The pronghorn config permits bushes only, never trees or aquatic plants.
            eating = AnimalDailyTargets.CanEatPlant(c, false, out _) && Random.value < .5f
                ? new EatPlantAction(duration) : AnimalDailyTargets.Terrain(c, TerrainKind.Grass, 90f, out _)
                    ? new RestAtTerrainAction(duration, TerrainKind.Grass, 90f, AnimalState.Eating) : new EatPlantAction(duration);
            eating.Enter(c);
        }
        public override AnimalActionStatus Tick(AnimalContext c, float dt) => eating.Tick(c, dt);
        public override void Exit(AnimalContext c) { eating.Exit(c); }
    }

    public sealed class ZigzagForagingAction : AnimalAction
    {
        private readonly float duration;
        private float elapsed, travel, peckRemaining;
        private bool approaching, valid;
        private int side = 1;
        public ZigzagForagingAction(float duration) { this.duration = duration; }
        public override AnimalState PhotoState => peckRemaining > 0f ? AnimalState.Eating : AnimalState.Moving;
        private static bool Grass(AnimalContext c, Vector2 p) => WorldTerrainQuery.TrySampleMapPosition(c.Agent.Map, p, out var kind)
            && AnimalDailyTargets.Matches(kind, TerrainKind.Grass);
        public override void Enter(AnimalContext c)
        {
            valid = AnimalDailyTargets.Terrain(c, TerrainKind.Grass, 90f, out Vector2 point)
                && c.Motor.SetTarget(point, c.Config.DailyMoveSpeedMetersPerSecond);
            approaching = true;
        }
        public override AnimalActionStatus Tick(AnimalContext c, float dt)
        {
            if (!valid) return Fail("No reachable grass for foraging.");
            if (approaching)
            {
                travel += dt;
                if (travel > c.Config.MaximumTravelTimeSeconds || c.Motor.Result == AnimalMovementResult.NoProgress) return Fail("Could not reach grass.");
                if (!c.Motor.HasArrived) return AnimalActionStatus.Running;
                approaching = false;
                c.Motor.Stop();
                peckRemaining = .5f;
                c.Motor.SegmentFilter = (a, b) => Grass(c, a) && Grass(c, b);
                c.Agent.SoundEmitter?.Emit(AnimalSoundKind.Pecking);
            }
            elapsed += dt;
            if (elapsed >= duration) return AnimalActionStatus.Succeeded;
            if (peckRemaining > 0f)
            { peckRemaining -= dt; if (peckRemaining > 0f) return AnimalActionStatus.Running; }
            else if (c.Motor.HasArrived)
            {
                c.Motor.Stop(); peckRemaining = .5f;
                c.Agent.SoundEmitter?.Emit(AnimalSoundKind.Pecking);
                return AnimalActionStatus.Running;
            }
            if (c.Motor.HasTarget && c.Motor.Result != AnimalMovementResult.NoProgress) return AnimalActionStatus.Running;
            for (int i = 0; i < 24; i++)
            {
                Vector2 heading = SlopeEscapeAction.Rotate(c.Motor.FacingMapDirection, side * Random.Range(45f, 120f));
                Vector2 next = c.Motor.CurrentMapPosition + heading * Random.Range(.4f, 1.2f);
                if (!c.Motor.CanTraverseSegment(c.Motor.CurrentMapPosition, next)) continue;
                side = -side;
                c.Motor.SetTarget(next, c.Config.DailyMoveSpeedMetersPerSecond);
                return AnimalActionStatus.Running;
            }
            return Fail("No grass segment available for the next foraging leg.");
        }
        public override void Exit(AnimalContext c) { c.Motor.ClearMovementOverrides(); base.Exit(c); }
    }

    public sealed class GroundBirdFlightAction : AnimalAction
    {
        private readonly bool fleeing;
        private bool valid;
        private float elapsed;
        public GroundBirdFlightAction(bool fleeing) { this.fleeing = fleeing; }
        public override string Name => fleeing ? "FlyingEscape" : "ShortFlight";
        public override AnimalState PhotoState => fleeing ? AnimalState.Fleeing : AnimalState.Moving;
        public static bool TryLanding(AnimalContext c, out Vector2 target)
        {
            for (int i = 0; i < 48; i++)
            {
                target = c.Motor.CurrentMapPosition + Random.insideUnitCircle.normalized * Random.Range(3f, 6f);
                if (c.Motor.CanOccupyMapPosition(target) && c.Motor.CanFlySegment(c.Motor.CurrentMapPosition, target)) return true;
            }
            target = default; return false;
        }
        public override void Enter(AnimalContext c)
        {
            c.Motor.Stop();
            valid = fleeing || (TryLanding(c, out Vector2 target) && c.Motor.SetAerialTarget(target, c.Config.FleeSpeedMetersPerSecond));
            if (valid) c.Agent.SoundEmitter?.Emit(AnimalSoundKind.Takeoff);
        }
        public override AnimalActionStatus Tick(AnimalContext c, float dt)
        {
            if (!valid) return Fail("No valid landing point 3–6 metres away.");
            elapsed += dt;
            if (!fleeing)
            {
                if (c.Motor.HasArrived)
                {
                    // A moving actor may have occupied the landing point during flight.
                    if (!c.Motor.CanOccupyMapPosition(c.Motor.CurrentMapPosition))
                    {
                        if (TryLanding(c, out Vector2 next)) c.Motor.SetAerialTarget(next, c.Config.FleeSpeedMetersPerSecond);
                        return AnimalActionStatus.Running;
                    }
                    c.Agent.SoundEmitter?.Emit(AnimalSoundKind.Landing);
                    return AnimalActionStatus.Succeeded;
                }
                return elapsed > c.Config.MaximumTravelTimeSeconds ? Fail("Short flight could not complete.") : AnimalActionStatus.Running;
            }
            Vector2 direction = c.Agent.Perception.EscapeDirection();
            for (int i = 0; i < 9; i++)
            {
                float angle = i == 0 ? 0f : ((i + 1) / 2) * 30f * (i % 2 == 0 ? -1 : 1);
                Vector2 target = c.Motor.CurrentMapPosition + SlopeEscapeAction.Rotate(direction, angle) * Mathf.Max(1f, c.Config.FleeSpeedMetersPerSecond * dt * 2f);
                if (c.Motor.CanFlySegment(c.Motor.CurrentMapPosition, target))
                { c.Motor.SetAerialTarget(target, c.Config.FleeSpeedMetersPerSecond); break; }
            }
            return AnimalActionStatus.Running;
        }
    }
}
