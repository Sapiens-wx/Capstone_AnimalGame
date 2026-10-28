using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.Animals
{
    public sealed class SparringSession
    {
        private readonly AnimalAgent a, b;
        private readonly float duration, created;
        private readonly bool[] entered = new bool[2], reached = new bool[2], struck = new bool[2];
        private readonly Vector2[] goals = new Vector2[2];
        private float phaseStarted, activityStarted = -1f;
        private int phase;
        public bool Ended { get; private set; }
        public int Phase => phase;
        public SparringSession(AnimalAgent a, AnimalAgent b, float duration)
        { this.a = a; this.b = b; this.duration = duration; created = a.Simulation.Time; }
        public void Enter(AnimalAgent member)
        {
            entered[member == a ? 0 : 1] = true;
            if (entered[0] && entered[1]) BeginPhase(0);
        }
        public void Cancel()
        {
            if (Ended) return;
            Ended = true;
            Release(a); Release(b);
        }
        private void Release(AnimalAgent member)
        {
            if (member == null || member.Runtime == null || member.Runtime.Sparring != this) return;
            member.Runtime.Sparring = null;
            member.Runtime.SparringCooldownUntil = member.Simulation.Time + 15f;
            if (member.CurrentState == AnimalState.Daily) member.Motor.Stop();
        }
        private bool Valid(AnimalAgent member) => member != null && member.isActiveAndEnabled && member.IsPresent
            && member.CurrentState == AnimalState.Daily && member.Runtime.Sparring == this;
        public AnimalActionStatus Tick(AnimalContext c)
        {
            if (Ended) return AnimalActionStatus.Succeeded;
            if (!Valid(a) || !Valid(b) || a.Runtime.Group == null || a.Runtime.Group != b.Runtime.Group)
            { Cancel(); return AnimalActionStatus.Failed; }
            float now = c.Agent.Simulation.Time;
            if (!entered[0] || !entered[1])
            {
                if (now - created > 1f) { Cancel(); return AnimalActionStatus.Failed; }
                return AnimalActionStatus.Running;
            }
            if (activityStarted >= 0f && now - activityStarted >= duration) { Cancel(); return AnimalActionStatus.Succeeded; }
            if (now - phaseStarted > c.Config.MaximumTravelTimeSeconds) { Cancel(); return AnimalActionStatus.Failed; }
            int index = c.Agent == a ? 0 : 1;
            AnimalAgent partner = index == 0 ? b : a;
            if (c.Motor.IsDisplaced) return AnimalActionStatus.Running;
            if (c.Motor.Result == AnimalMovementResult.NoProgress) { Cancel(); return AnimalActionStatus.Failed; }
            if (phase == 1 && !struck[index])
            {
                float contact = c.Config.BodyRadiusMeters + partner.Config.BodyRadiusMeters + c.Config.AttackPadding + .15f;
                if (Vector2.Distance(c.Motor.CurrentMapPosition, partner.Motor.CurrentMapPosition) <= contact)
                {
                    struck[0] = struck[1] = true;
                    // Social contact never causes combat impacts.
                }
            }
            if (Vector2.Distance(c.Motor.CurrentMapPosition, goals[index]) <= c.Config.ArrivalDistanceMeters)
            { reached[index] = true; c.Motor.Stop(); }
            else c.Motor.SetTarget(goals[index], phase == 1 ? c.Config.AggressiveSpeed : c.Config.DailyMoveSpeedMetersPerSecond);
            // Both actors advance from one shared clock and phase, irrespective of update ordering.
            if ((reached[0] && reached[1]) || (phase == 1 && (struck[0] || struck[1]) && now - phaseStarted >= 1f))
            {
                if (phase == 0 && activityStarted < 0f) activityStarted = now;
                BeginPhase((phase + 1) % 3);
            }
            return AnimalActionStatus.Running;
        }
        private void BeginPhase(int next)
        {
            phase = next; phaseStarted = a.Simulation.Time;
            reached[0] = reached[1] = struck[0] = struck[1] = false;
            Vector2 pa = a.Motor.CurrentMapPosition, pb = b.Motor.CurrentMapPosition;
            Vector2 direction = (pb - pa).normalized;
            if (direction.sqrMagnitude < .001f) direction = Vector2.right;
            Vector2 side = new Vector2(-direction.y, direction.x);
            float separation = a.Config.BodyRadiusMeters + b.Config.BodyRadiusMeters + .35f;
            if (phase == 0)
            {
                Vector2 midpoint = (pa + pb) * .5f;
                goals[0] = midpoint - direction * separation;
                goals[1] = midpoint + direction * separation;
            }
            else if (phase == 1)
            {
                // Tangential offset reaches the other animal's side without targeting its occupied center.
                goals[0] = pb + side * separation * .6f;
                goals[1] = pa - side * separation * .6f;
            }
            else
            { goals[0] = pa - direction * 1.5f; goals[1] = pb + direction * 1.5f; }
            if (!a.Motor.CanOccupyMapPosition(goals[0]) || !b.Motor.CanOccupyMapPosition(goals[1])) Cancel();
        }
    }

    public sealed class SparringAction : AnimalAction
    {
        private readonly float duration;
        private SparringSession session;
        public SparringAction(float duration) { this.duration = duration; }
        internal SparringAction(SparringSession session) { this.session = session; }
        public override string Name => "Sparring/" + (session == null ? "Unavailable" : session.Phase.ToString());
        public override AnimalState PhotoState => AnimalState.Moving;
        public static bool CanStart(AnimalContext c)
        {
            if (c.Agent.Behaviour.IsJuvenile || c.Runtime.Group == null || c.Runtime.Sparring != null
                || c.Runtime.Time < c.Runtime.SparringCooldownUntil) return false;
            foreach (AnimalAgent member in c.Runtime.Group.Members)
                if (member != null && member != c.Agent && member.Behaviour.GetType() == c.Agent.Behaviour.GetType() && member.IsPresent
                    && !member.Behaviour.IsJuvenile && member.CanAcceptSocialAction && member.Runtime.Sparring == null
                    && member.Runtime.Time >= member.Runtime.SparringCooldownUntil
                    && Vector2.Distance(c.Motor.CurrentMapPosition, member.Motor.CurrentMapPosition) <= c.Config.ActivityRadiusMeters)
                    return true;
            return false;
        }
        public override void Enter(AnimalContext c)
        {
            if (session != null) { if (!session.Ended) session.Enter(c.Agent); return; }
            if (c.Agent.Behaviour.IsJuvenile || c.Runtime.Group == null || c.Runtime.Sparring != null
                || c.Runtime.Time < c.Runtime.SparringCooldownUntil) return;
            var candidates = new List<AnimalAgent>();
            foreach (AnimalAgent member in c.Runtime.Group.Members)
            {
                if (member == null || member == c.Agent || !member.IsPresent || !member.CanAcceptSocialAction
                    || member.Runtime.Sparring != null || member.Runtime.Time < member.Runtime.SparringCooldownUntil) continue;
                if (member.Behaviour.GetType() == c.Agent.Behaviour.GetType() && !member.Behaviour.IsJuvenile
                    && Vector2.Distance(c.Motor.CurrentMapPosition, member.Motor.CurrentMapPosition) <= c.Config.ActivityRadiusMeters)
                    candidates.Add(member);
            }
            if (candidates.Count == 0) return;
            AnimalAgent partner = candidates[Random.Range(0, candidates.Count)];
            session = new SparringSession(c.Agent, partner, duration);
            c.Runtime.Sparring = partner.Runtime.Sparring = session;
            session.Enter(c.Agent);
            if (!partner.Brain.StartAction(new SparringAction(session))) session.Cancel();
        }
        public override AnimalActionStatus Tick(AnimalContext c, float dt) => session != null ? session.Tick(c) : AnimalActionStatus.Failed;
        public override void Exit(AnimalContext c) { session?.Cancel(); base.Exit(c); }
    }
}
