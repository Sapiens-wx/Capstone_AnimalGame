using UnityEngine;

namespace AnimalGame.Animals
{
    public abstract class AnimalBehaviourSet : MonoBehaviour
    {
        private float hidingTimeRemaining;
        private float hidingSafetyCheckCountdown;
        private AnimalAction configuredAction;
        private AnimalDailyBehaviourSettings configuredSettings;
        private float retryAfter;
        public virtual AnimalState PhotoState => configuredAction?.PhotoState
            ?? (Motor != null && Motor.CurrentSpeedMetersPerSecond > .001f ? AnimalState.Moving : AnimalState.Resting);
        public virtual string CurrentActionName => configuredAction?.Name ?? GetType().Name;

        protected AnimalAgent Agent { get; private set; }
        protected AnimalMotor Motor => Agent != null ? Agent.Motor : null;
        protected AnimalSpeciesConfig Config =>
            Agent != null ? Agent.Config : null;

        public virtual bool SupportsAggression => false;
        public bool CanAcceptSocialAction => !HasConfiguredAction || configuredAction is IdleAction
            || configuredAction is LookAroundAction || configuredAction is RestAtTerrainAction;
        public virtual AnimalAction CreateEscapeAction() => Config.EscapeAction != null
            ? Config.EscapeAction.CreateRuntime() : new RunAwayAction();

        protected bool BeginConfiguredAction(AnimalDailyBehaviourSettings settings)
        {
            AnimalAction action = settings.Action != null ? settings.Action.CreateRuntime(settings)
                : settings.Behaviour == AnimalDailyBehaviourKind.Hunt && Config.CanHunt && Agent.Runtime.Time >= Agent.Runtime.HuntCooldownUntil ? new HuntAction()
                : settings.Behaviour == AnimalDailyBehaviourKind.Idle ? new IdleAction(settings.ChooseDuration()) : null;
            if (action == null) return false;
            CancelConfiguredAction();
            configuredSettings = settings;
            configuredAction = action;
            action.Enter(Agent.Context);
            return true;
        }
        protected bool BeginRuntimeAction(AnimalAction action, AnimalDailyBehaviourSettings settings = null)
        {
            if (action == null) return false;
            CancelConfiguredAction();
            configuredSettings = settings;
            configuredAction = action;
            action.Enter(Agent.Context);
            return true;
        }
        protected bool TickConfiguredAction(float dt)
        {
            if (configuredAction == null) return false;
            AnimalActionStatus status = configuredAction.Tick(Agent.Context, dt);
            if (status != AnimalActionStatus.Running)
            {
                AnimalDailyBehaviourSettings settings = configuredSettings;
                CancelConfiguredAction();
                if (status == AnimalActionStatus.Failed) Agent.DailyScheduler.Failed(settings);
                else Agent.DailyScheduler.Complete(settings);
            }
            return true;
        }
        protected bool HasConfiguredAction => configuredAction != null;
        protected AnimalState ConfiguredPhotoState => configuredAction?.PhotoState ?? AnimalState.Daily;
        protected void CancelConfiguredAction()
        {
            AnimalAction old = configuredAction;
            configuredAction = null;
            configuredSettings = null;
            old?.Exit(Agent.Context);
        }
        protected bool IsSharedDaily(AnimalDailyBehaviourSettings settings) => settings.Action != null
            || settings.Behaviour == AnimalDailyBehaviourKind.Idle
            || (settings.Behaviour == AnimalDailyBehaviourKind.Hunt && Config.CanHunt && Agent.Runtime.Time >= Agent.Runtime.HuntCooldownUntil);

        protected bool BeginGroupAction()
        {
            if (Agent.Runtime.Time < retryAfter) return false;
            AnimalGroup group = Agent.Runtime.Group;
            if (group == null || group.Leader == null) return false;
            AnimalAction action = null;
            if (Config.CanHunt)
            {
                AnimalAgent closest = null;
                float nearest = float.PositiveInfinity;
                foreach (AnimalAgent member in group.Members)
                {
                    if (member == null || member == Agent || member.Runtime.Target == null
                        || !Config.Eats(member.Runtime.Target.Config)) continue;
                    float distance = Vector2.Distance(Motor.CurrentMapPosition, member.Motor.CurrentMapPosition);
                    if (distance < nearest) { nearest = distance; closest = member; }
                }
                if (closest != null && (!closest.Runtime.Target.Health.IsDead || Agent.Runtime.Time >= Agent.Runtime.FeedCooldownUntil))
                    action = new HuntAction(closest.Runtime.Target);
            }
            if (action == null && group.Leader != Agent && !group.WaitingForEscape
                && Vector2.Distance(Motor.CurrentMapPosition, group.Leader.Motor.CurrentMapPosition) > Config.GroupFollowRadius)
                action = new FollowLeaderAction();
            if (action == null) return false;
            CancelConfiguredAction();
            configuredAction = action;
            action.Enter(Agent.Context);
            retryAfter = Agent.Runtime.Time + 1f;
            return true;
        }

        public virtual void Initialize(AnimalAgent agent)
        {
            Agent = agent;
        }

        public abstract void EnterDaily();
        public abstract void TickDaily(float deltaTime);
        public abstract void ExitDaily();

        public virtual void EnterCurious()
        {
        }

        public virtual void MoveWhileCurious(float deltaTime) { Motor.Stop(); }

        public virtual void TickCurious(float deltaTime)
        {
        }

        public virtual void ExitCurious()
        {
        }

        public abstract void EnterFleeing();
        public abstract void TickFleeing(float deltaTime);
        public abstract void ExitFleeing();

        public virtual void EnterAggressive()
        {
        }

        public virtual void TickAggressive(float deltaTime)
        {
        }

        public virtual void ExitAggressive()
        {
        }

        public virtual void EnterHiding()
        {
            BeginHidingWait();
        }

        public virtual void TickHiding(float deltaTime)
        {
        }

        public virtual void ExitHiding()
        {
        }

        protected void BeginHidingWait()
        {
            hidingTimeRemaining = Config != null
                ? Config.ChooseFrightenedHideDuration()
                : 0f;
            hidingSafetyCheckCountdown = 0f;
        }

        protected bool ShouldAttemptSafeEmergence(float deltaTime)
        {
            hidingTimeRemaining -= Mathf.Max(0f, deltaTime);
            if (hidingTimeRemaining > 0f)
                return false;

            hidingSafetyCheckCountdown -= Mathf.Max(0f, deltaTime);
            if (hidingSafetyCheckCountdown > 0f)
                return false;

            hidingSafetyCheckCountdown = Config != null
                ? Config.HideSafetyCheckIntervalSeconds
                : 0.75f;
            return true;
        }

        protected bool IsEmergencePositionSafe(Vector2 mapPosition)
        {
            if (Agent == null || Config == null || Agent.Perception == null)
                return false;
            if (!Agent.Perception.TryGetPlayerMapPosition(
                    out Vector2 playerMapPosition))
            {
                return true;
            }

            float safeDistance = Config.ReappearSafeDistanceMeters;
            return (playerMapPosition - mapPosition).sqrMagnitude
                   >= safeDistance * safeDistance;
        }
    }
}
