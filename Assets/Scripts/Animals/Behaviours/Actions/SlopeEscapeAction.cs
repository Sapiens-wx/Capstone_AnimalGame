using AnimalGame.World;
using UnityEngine;

namespace AnimalGame.Animals
{
    public sealed class SlopeEscapeAction : AnimalAction
    {
        public enum EscapePhase { Uphill, DownhillTraverse }
        private readonly MountainGoatBehaviour behaviour;
        private AnimalContext context;
        private Vector2 lastPosition, lastClimb, heading;
        private float lastHeight, countdown, descentTravel;
        private int missingUphill, risingSamples;
        private bool descended, fallback, wasDisplaced;
        public EscapePhase Phase { get; private set; }
        public override string Name => "SlopeEscape/" + Phase + (fallback ? "/Avoid" : "");
        public override AnimalState PhotoState => AnimalState.Fleeing;
        public SlopeEscapeAction(MountainGoatBehaviour behaviour) { this.behaviour = behaviour; }
        public override void Enter(AnimalContext c)
        {
            context = c;
            lastPosition = c.Motor.CurrentMapPosition;
            c.Agent.Map.TrySampleMapPosition(lastPosition, out lastHeight);
            heading = c.Agent.Perception.EscapeDirection();
            Phase = EscapePhase.Uphill;
            c.Motor.SegmentFilter = AcceptStep;
        }
        private bool Safe(Vector2 start, Vector2 end)
        {
            foreach (Transform threat in context.Runtime.Threats)
            {
                if (threat == null) continue;
                Vector2 p = InteractionShape.ToQuery(threat.position, context.Agent.Map);
                float separation = context.Config.BodyRadiusMeters + .75f;
                var animal = threat.GetComponent<AnimalAgent>();
                if (animal != null) separation = context.Config.BodyRadiusMeters + animal.Config.BodyRadiusMeters;
                float danger = separation + 1f;
                float before = Vector2.Distance(start, p), after = Vector2.Distance(end, p);
                // Near threats must never become nearer, even if the next slope is uphill.
                if (after < danger && after < before - .001f) return false;
            }
            return true;
        }
        private bool AcceptStep(Vector2 start, Vector2 end)
        {
            if (!Safe(start, end)) return false;
            if (fallback) return true;
            if (!context.Agent.Map.TrySampleMapPosition(start, out float a)
                || !context.Agent.Map.TrySampleMapPosition(end, out float b)) return false;
            if (Phase == EscapePhase.DownhillTraverse)
                return Vector2.Dot((end - start).normalized, heading) >= .1f;
            return b >= a - .001f;
        }
        public override AnimalActionStatus Tick(AnimalContext c, float dt)
        {
            Vector2 current = c.Motor.CurrentMapPosition;
            if (!c.Agent.Map.TrySampleMapPosition(current, out float height)) return AnimalActionStatus.Running;
            if (c.Motor.IsDisplaced)
            { lastPosition = current; lastHeight = height; wasDisplaced = true; return AnimalActionStatus.Running; }
            if (wasDisplaced) { lastPosition = current; lastHeight = height; countdown = 0f; wasDisplaced = false; }
            Vector2 travel = current - lastPosition;
            if (travel.magnitude > .01f)
            {
                if (Phase == EscapePhase.Uphill && height > lastHeight + .001f) lastClimb = travel.normalized;
                if (Phase == EscapePhase.DownhillTraverse)
                {
                    descentTravel += travel.magnitude;
                    if (height < lastHeight - .001f) descended = true;
                    if (descended && descentTravel >= behaviour.MinimumDescentTravelMeters && height > lastHeight + .001f)
                        risingSamples++;
                    else if (height < lastHeight - .001f) risingSamples = 0;
                    if (risingSamples >= 3) { Phase = EscapePhase.Uphill; missingUphill = 0; countdown = 0f; }
                }
            }
            lastPosition = current; lastHeight = height;
            countdown -= dt;
            if (countdown > 0f && !c.Motor.HasArrived && c.Motor.Result != AnimalMovementResult.NoProgress) return AnimalActionStatus.Running;
            countdown = behaviour.ChooseReplanInterval();
            fallback = false;
            bool found = ChooseTarget(c, current, height, out Vector2 target);
            if (!found && Phase == EscapePhase.Uphill && missingUphill < 2)
            {
                missingUphill++;
                c.Motor.Stop();
                return AnimalActionStatus.Running;
            }
            if (!found && Phase == EscapePhase.Uphill && ++missingUphill >= 3)
            {
                Phase = EscapePhase.DownhillTraverse;
                heading = lastClimb.sqrMagnitude > .001f ? -lastClimb
                    : AnimalTerrainQuery.TrySample(c.Agent.Map, current, out var ground) && ground.Gradient.sqrMagnitude > .001f
                        ? -ground.Gradient.normalized : c.Agent.Perception.EscapeDirection();
                descentTravel = 0f; descended = false; risingSamples = 0;
                found = ChooseTarget(c, current, height, out target);
            }
            if (!found)
            {
                fallback = true;
                Vector2 away = c.Agent.Perception.EscapeDirection();
                for (int i = 0; i < 16; i++)
                {
                    float angle = (i % 2 == 0 ? 1 : -1) * ((i + 1) / 2) * 22.5f;
                    Vector2 direction = Rotate(away, angle);
                    target = current + direction * behaviour.EscapeProbeMeters;
                    if (c.Motor.CanTraverseSegment(current, target)) { found = true; break; }
                }
            }
            if (found) c.Motor.SetTarget(target, c.Config.FleeSpeedMetersPerSecond);
            else c.Motor.Stop();
            return AnimalActionStatus.Running;
        }
        private bool ChooseTarget(AnimalContext c, Vector2 current, float height, out Vector2 target)
        {
            target = current;
            float best = float.NegativeInfinity;
            Vector2 away = c.Agent.Perception.EscapeDirection();
            for (int i = 0; i < 16; i++)
            {
                Vector2 direction = Rotate(Phase == EscapePhase.DownhillTraverse ? heading : away, i * 22.5f);
                if (Phase == EscapePhase.DownhillTraverse && Vector2.Dot(direction, heading) < .25f) continue;
                Vector2 p = current + direction * behaviour.EscapeProbeMeters;
                if (!c.Agent.Map.TrySampleMapPosition(p, out float h)) continue;
                float rise = h - height;
                if (Phase == EscapePhase.Uphill && rise < behaviour.MinimumRiseMeters) continue;
                if (!c.Motor.CanTraverseSegment(current, p)) continue;
                float score = Phase == EscapePhase.Uphill
                    ? rise + .2f * Vector2.Dot(direction, away) + .1f * Vector2.Dot(direction, lastClimb)
                    : Vector2.Dot(direction, heading) - .1f * rise + .1f * Vector2.Dot(direction, away);
                if (score <= best) continue;
                best = score; target = p;
            }
            if (float.IsNegativeInfinity(best)) return false;
            if (Phase == EscapePhase.Uphill) missingUphill = 0;
            return true;
        }
        internal static Vector2 Rotate(Vector2 direction, float angle)
        {
            float radians = angle * Mathf.Deg2Rad;
            return new Vector2(direction.x * Mathf.Cos(radians) - direction.y * Mathf.Sin(radians),
                direction.x * Mathf.Sin(radians) + direction.y * Mathf.Cos(radians));
        }
        public override void Exit(AnimalContext c) { c.Motor.ClearMovementOverrides(); base.Exit(c); }
    }
}
