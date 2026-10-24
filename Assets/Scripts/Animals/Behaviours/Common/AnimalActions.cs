using System.Collections.Generic;
using AnimalGame.World;
using UnityEngine;

namespace AnimalGame.Animals
{
    public class IdleAction : AnimalAction
    {
        private readonly float duration;
        private float elapsed;
        public IdleAction(float duration = 1f) { this.duration = duration; }
        public override AnimalState PhotoState => AnimalState.Resting;
        public override void Enter(AnimalContext context) { elapsed = 0f; context.Motor.Stop(); }
        public override AnimalActionStatus Tick(AnimalContext context, float deltaTime)
        { elapsed += deltaTime; return elapsed >= duration ? AnimalActionStatus.Succeeded : AnimalActionStatus.Running; }
    }

    public class RoamAction : AnimalAction
    {
        private readonly float duration;
        private float elapsed;
        private bool moving;
        public RoamAction(float duration = 5f) { this.duration = duration; }
        public override AnimalState PhotoState => moving ? AnimalState.Moving : AnimalState.Resting;
        public override void Enter(AnimalContext context)
        {
            elapsed = 0f;
            moving = false;
            Vector2 center = context.Agent.ActivityCenter;
            for (int i = 0; i < 20; i++)
            {
                Vector2 target = center + Random.insideUnitCircle * context.Config.ActivityRadiusMeters;
                if (context.Motor.CanOccupyMapPosition(target) && context.Motor.SetTarget(target, context.Config.DailyMoveSpeedMetersPerSecond))
                { moving = true; break; }
            }
        }
        public override AnimalActionStatus Tick(AnimalContext context, float dt)
        {
            elapsed += dt;
            if (!moving) return AnimalActionStatus.Failed;
            if (context.Motor.HasArrived || elapsed >= duration) return AnimalActionStatus.Succeeded;
            return context.Motor.Result == AnimalMovementResult.NoProgress ? AnimalActionStatus.Failed : AnimalActionStatus.Running;
        }
    }

    public class RunAwayAction : AnimalAction
    {
        public override AnimalState PhotoState => AnimalState.Fleeing;
        public override AnimalActionStatus Tick(AnimalContext context, float dt)
        {
            Vector2 direction = context.Agent.Perception.EscapeDirection();
            Vector2 current = context.Motor.CurrentMapPosition;
            Vector2 next = current + direction * Mathf.Max(.2f, context.Config.FleeSpeedMetersPerSecond * dt * 2f);
            if (!context.Motor.SetTarget(next, context.Config.FleeSpeedMetersPerSecond)) context.Motor.Stop();
            return AnimalActionStatus.Running;
        }
    }

    public class EatPlantAction : AnimalAction
    {
        private readonly float duration;
        private readonly bool nearbyOnly;
        private AnimalFoodSource food;
        private Vector2 foodPosition;
        private float eatingSeconds, travelSeconds;
        private bool eating;
        public EatPlantAction(float duration = 5f, bool nearbyOnly = false)
        { this.duration = duration; this.nearbyOnly = nearbyOnly; }
        public override AnimalState PhotoState => eating ? AnimalState.Eating : AnimalState.Moving;
        public override void Enter(AnimalContext c)
        {
            food = null;
            eating = false;
            eatingSeconds = travelSeconds = 0f;
            float weightSum = 0f;
            foreach (AnimalFoodSource source in AnimalFoodSource.ActiveSources)
            {
                if (source == null || source.gameObject.scene != c.Agent.gameObject.scene || !source.isActiveAndEnabled
                    || !source.TryGetMapPosition(c.Agent.Map, out Vector2 position)) continue;
                float distance = Vector2.Distance(c.Motor.CurrentMapPosition, position);
                if (distance > (nearbyOnly ? c.Config.NearbyFoodDistanceMeters : c.Config.ActivityRadiusMeters)) continue;
                float weight = source.SelectionWeight * c.Config.GetFoodSelectionWeight(source.FoodType);
                if (weight <= 0f) continue;
                weightSum += weight;
                if (food == null || Random.value * weightSum <= weight) { food = source; foodPosition = position; }
            }
            if (food == null) return;
            if (nearbyOnly) { eating = true; c.Motor.Stop(); return; }
            float radius = c.Config.BodyRadiusMeters + c.Config.FoodApproachPaddingMeters + food.EatingApproachPaddingMeters;
            WorldInteraction obstacle = food.GetComponentInChildren<WorldInteraction>();
            if (obstacle != null)
            {
                InteractionShape shape = obstacle.GetShape(c.Agent.Map);
                radius += shape.IsBox ? Mathf.Max(Vector2.Distance(shape.Center, shape.A), Vector2.Distance(shape.Center, shape.C)) : shape.Radius;
            }
            Vector2 outward = c.Motor.CurrentMapPosition - foodPosition;
            float baseAngle = Mathf.Atan2(outward.y, outward.x);
            for (int i = 0; i < 12; i++)
            {
                float angle = baseAngle + i * Mathf.PI / 6f;
                Vector2 target = foodPosition + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                if (c.Motor.CanOccupyMapPosition(target) && c.Motor.SetTarget(target, c.Config.DailyMoveSpeedMetersPerSecond)) return;
            }
            food = null;
        }
        public override AnimalActionStatus Tick(AnimalContext c, float dt)
        {
            if (food == null || !food.isActiveAndEnabled) return Fail("No suitable plant or reachable eating position is available.");
            if (!eating)
            {
                travelSeconds += dt;
                if (travelSeconds >= c.Config.MaximumTravelTimeSeconds || c.Motor.Result == AnimalMovementResult.NoProgress) return Fail("Could not reach the food before the travel limit.");
                if (!c.Motor.HasArrived) return AnimalActionStatus.Running;
                eating = true;
                c.Motor.Stop();
            }
            c.Motor.FaceMapPosition(foodPosition);
            eatingSeconds += dt;
            return eatingSeconds >= duration ? AnimalActionStatus.Succeeded : AnimalActionStatus.Running;
        }
    }

    // Migration adapters retain the old search/transition algorithms while sharing action lifecycle.
    public class DiveEscapeAction : RunAwayAction
    {
        private readonly MuskratBehaviour behaviour;
        public DiveEscapeAction(MuskratBehaviour behaviour) { this.behaviour = behaviour; }
        public override void Enter(AnimalContext context) { behaviour.EnterFleeing(); }
        public override AnimalActionStatus Tick(AnimalContext context, float dt)
        { behaviour.TickFleeing(dt); return AnimalActionStatus.Running; }
        public override void Exit(AnimalContext context) { behaviour.ExitFleeing(); base.Exit(context); }
    }
    public sealed class FollowLeaderAction : AnimalAction
    {
        private float elapsed;
        public override AnimalState PhotoState => AnimalState.Moving;
        public override AnimalActionStatus Tick(AnimalContext context, float dt)
        {
            AnimalAgent leader = context.Runtime.Group?.Leader;
            if (leader == null || leader == context.Agent) return AnimalActionStatus.Succeeded;
            elapsed += dt;
            if (Vector2.Distance(context.Motor.CurrentMapPosition, leader.Motor.CurrentMapPosition) <= context.Config.GroupFollowRadius)
                return AnimalActionStatus.Succeeded;
            if (elapsed > context.Config.MaximumTravelTimeSeconds) return AnimalActionStatus.Failed;
            if (!context.Motor.HasTarget || context.Motor.HasArrived)
                context.Motor.SetTarget(leader.Motor.CurrentMapPosition + Random.insideUnitCircle * context.Config.GroupFollowRadius * .5f,
                    context.Config.DailyMoveSpeedMetersPerSecond);
            return AnimalActionStatus.Running;
        }
    }

    public sealed class AggressiveAction : AnimalAction
    {
        private float recharge;
        private Transform target;
        public override void Enter(AnimalContext c) { target = c.Agent.Perception.PrimaryThreat; recharge = 0f; }
        public override AnimalActionStatus Tick(AnimalContext c, float dt)
        {
            if (target == null || !target.gameObject.activeInHierarchy) return AnimalActionStatus.Succeeded;
            AnimalAgent animal = target.GetComponent<AnimalAgent>();
            if (animal != null && animal.Health.IsDead) return AnimalActionStatus.Succeeded;
            Vector2 destination = InteractionShape.ToQuery(target.position, c.Agent.Map);
            float distance = Vector2.Distance(destination, c.Motor.CurrentMapPosition);
            if (distance > c.Config.AggressiveRadius) return AnimalActionStatus.Succeeded;
            recharge -= dt;
            if (recharge > 0f) { c.Motor.Stop(); return AnimalActionStatus.Running; }
            float targetRadius = animal != null ? animal.Config.BodyRadiusMeters : target.GetComponent<AnimalGame.RobotMap.PlayerWorldBody>()?.GetShape(c.Agent.Map).Radius ?? .75f;
            if (distance <= c.Config.BodyRadiusMeters + targetRadius + c.Config.AttackPadding)
            {
                c.Motor.Stop();
                AnimalImpact.Apply(c, destination - c.Motor.CurrentMapPosition);
                recharge = 1f;
                return Random.value < .6f ? AnimalActionStatus.Succeeded : AnimalActionStatus.Running;
            }
            c.Motor.SetTarget(destination, c.Config.AggressiveSpeed);
            return AnimalActionStatus.Running;
        }
    }

    public sealed class HuntAction : AnimalAction
    {
        private AnimalAgent target;
        private readonly bool corpseOnly;
        private readonly bool startWithLivePrey;
        private float started, feedingSeconds, lastSelection;
        private bool feeding, reserved;
        public override AnimalState PhotoState => feeding ? AnimalState.Eating : AnimalState.Moving;
        public HuntAction(AnimalAgent initialTarget = null, bool corpseOnly = false, bool startWithLivePrey = false)
        { target = initialTarget; this.corpseOnly = corpseOnly; this.startWithLivePrey = startWithLivePrey; }
        public override void Enter(AnimalContext c)
        {
            started = c.Runtime.Time;
            FailureReason = null;
            feedingSeconds = 0f;
            feeding = reserved = false;
            c.Runtime.Target = target;
            lastSelection = -1f;
        }
        public override AnimalActionStatus Tick(AnimalContext c, float dt)
        {
            if (c.Runtime.Time - started >= c.Config.HuntTimeoutSeconds) return Fail("The hunt timed out.");
            if (!AnimalHuntTargets.Eligible(c, target, corpseOnly)
                || Vector2.Distance(c.Motor.CurrentMapPosition, target.Motor.CurrentMapPosition) > c.Config.ChaseRadiusMeters)
            { Release(c); target = null; }
            if (!feeding && c.Runtime.Time - lastSelection >= .5f)
            {
                lastSelection = c.Runtime.Time;
                // Once the hunted target dies, retain it and continue the normal feeding lifecycle.
                AnimalAgent selected = AnimalHuntTargets.Select(c, target, corpseOnly,
                    startWithLivePrey && (target == null || !target.Health.IsDead));
                if (selected != null && selected != target) { Release(c); target = selected; }
            }
            c.Runtime.Target = target;
            if (target == null) return Fail(FailureReason ?? (corpseOnly ? "No eligible corpse remains (diet, range, capacity or target availability)." : "No eligible prey remains in range."));
            Vector2 destination = target.Motor.CurrentMapPosition;
            float contact = c.Config.BodyRadiusMeters + target.Config.BodyRadiusMeters + c.Config.AttackPadding;
            float distance = Vector2.Distance(c.Motor.CurrentMapPosition, destination);
            if (target.Health.IsDead)
            {
                if (c.Motor.IsDisplaced) return Fail("Feeding was interrupted by displacement.");
                if (c.Runtime.Time < c.Runtime.FeedCooldownUntil) return Fail("Feeding is on cooldown.");
                if (target.Health.CorpseRemaining <= 0f) return AnimalActionStatus.Succeeded;
                if (distance > contact)
                {
                    if (feeding) return Fail("The corpse moved out of feeding range.");
                    c.Motor.SetTarget(destination, c.Config.DailyMoveSpeedMetersPerSecond);
                    return AnimalActionStatus.Running;
                }
                if (!reserved) reserved = target.Health.Reserve(c.Agent);
                if (!reserved)
                {
                    FailureReason = "All feeding positions on the corpse are occupied.";
                    target = null;
                    return AnimalActionStatus.Running;
                }
                FailureReason = null;
                feeding = true;
                c.Motor.Stop();
                c.Motor.FaceMapPosition(destination);
                float portionSeconds = Mathf.Min(dt, 20f - feedingSeconds);
                float rate = c.Config.AttackDamage / 20f;
                float consumed = target.Health.Consume(rate * portionSeconds);
                feedingSeconds += rate > 0f ? consumed / rate : portionSeconds;
                if (feedingSeconds >= 20f - .0001f || target.Health.CorpseRemaining <= .00001f) return AnimalActionStatus.Succeeded;
                return AnimalActionStatus.Running;
            }
            if (target.CurrentState == AnimalState.Aggressive && target.Perception.PrimaryThreat == c.Agent.transform)
            { c.Agent.Perception.Remember(target.transform); c.Agent.Brain.Request(AnimalState.Fleeing); return AnimalActionStatus.Running; }
            if (distance <= contact)
            { c.Motor.Stop(); c.Agent.Simulation.Hit(c.Agent, target.transform, false); }
            else
            {
                float speed = distance > target.Config.AlertRadiusMeters ? c.Config.DailyMoveSpeedMetersPerSecond
                    : target.CurrentState == AnimalState.Fleeing || distance < contact * 3f ? c.Config.FleeSpeedMetersPerSecond
                    : c.Config.DailyMoveSpeedMetersPerSecond * .5f;
                c.Motor.SetTarget(destination, speed);
            }
            if (c.Motor.Result == AnimalMovementResult.NoProgress)
            { c.Runtime.FailedTargets[target] = c.Runtime.Time + 5f; return Fail("The animal cannot make progress towards its target."); }
            return AnimalActionStatus.Running;
        }
        private void Release(AnimalContext c)
        {
            if (reserved && target != null) target.Health.Release(c.Agent);
            reserved = false;
        }
        public override void Exit(AnimalContext c)
        {
            Release(c);
            if (feedingSeconds > 0f)
            {
                float until = c.Runtime.Time + 300f * Mathf.Clamp01(feedingSeconds / 20f);
                c.Runtime.HuntCooldownUntil = Mathf.Max(c.Runtime.HuntCooldownUntil, until);
                c.Runtime.FeedCooldownUntil = Mathf.Max(c.Runtime.FeedCooldownUntil, until);
            }
            c.Runtime.Target = null;
            base.Exit(c);
        }
    }
}
