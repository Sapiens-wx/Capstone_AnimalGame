using UnityEngine;

namespace AnimalGame.Animals
{
    public sealed class BurrowAction : AnimalAction
    {
        private readonly float duration;
        private readonly bool escaping, atEntrance;
        private AnimalBurrow target;
        private AmericanMarmotBehaviour marmot;
        private Vector2 destination;
        private bool arrived;
        private float elapsed, travel;
        public BurrowAction(float duration, bool escaping = false, bool atEntrance = false)
        { this.duration = duration; this.escaping = escaping; this.atEntrance = atEntrance; }
        public override string Name => escaping ? "EscapeToBurrow" : atEntrance ? "RestAtBurrow" : "HideInHomeBurrow";
        public override AnimalState PhotoState => arrived ? AnimalState.Resting : AnimalState.Moving;
        public override void Enter(AnimalContext c)
        {
            marmot = (AmericanMarmotBehaviour)c.Agent.Behaviour;
            if (!escaping) target = marmot.HomeBurrow;
            else
            {
                float nearest = float.PositiveInfinity;
                foreach (AnimalBurrow burrow in AnimalBurrow.ActiveBurrows)
                {
                    if (burrow.gameObject.scene != c.Agent.gameObject.scene || !burrow.TryGetPosition(c.Agent.Map, out Vector2 p)
                        || !c.Motor.CanOccupyMapPosition(p) || !c.Motor.CanTraverseSegment(c.Motor.CurrentMapPosition, p)) continue;
                    float distance = Vector2.SqrMagnitude(p - c.Motor.CurrentMapPosition);
                    if (distance < nearest) { nearest = distance; target = burrow; }
                }
            }
            if (target != null && target.TryGetPosition(c.Agent.Map, out destination))
                c.Motor.SetTarget(destination, escaping ? c.Config.FleeSpeedMetersPerSecond : c.Config.DailyMoveSpeedMetersPerSecond);
        }
        public override AnimalActionStatus Tick(AnimalContext c, float dt)
        {
            if (target == null) return Fail("No reachable burrow entrance.");
            if (!arrived)
            {
                travel += dt;
                if (travel > c.Config.MaximumTravelTimeSeconds || c.Motor.Result == AnimalMovementResult.NoProgress) return Fail("Could not reach the burrow.");
                if (!c.Motor.HasArrived) return AnimalActionStatus.Running;
                c.Motor.Stop(); arrived = true;
                marmot.HidingBurrow = target;
                if (escaping) { c.Agent.BeginHiding(); return AnimalActionStatus.Running; }
                if (!atEntrance) c.Agent.SetConcealed(true);
            }
            elapsed += dt;
            if (elapsed < duration) return AnimalActionStatus.Running;
            if (!atEntrance && !marmot.EntranceIsSafe())
            { c.Agent.BeginHiding(); return AnimalActionStatus.Running; }
            return AnimalActionStatus.Succeeded;
        }
        public override void Exit(AnimalContext c)
        {
            c.Agent.SetConcealed(false);
            base.Exit(c);
        }
    }
}
