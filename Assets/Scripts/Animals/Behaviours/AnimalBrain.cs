using System;

namespace AnimalGame.Animals
{
    public abstract class AnimalStateBase
    {
        public abstract void Enter(AnimalContext context);
        public abstract void Tick(AnimalContext context, float deltaTime);
        public abstract void Exit(AnimalContext context);
    }

    public sealed class AnimalBrain
    {
        private readonly AnimalContext context;
        private readonly AnimalBehaviourSet behaviour;
        private AnimalStateBase current;
        private AnimalState? requested;
        private bool updating;
        private AnimalAction overrideAction;
        private AnimalAction pendingAction;
        public string CurrentActionName => overrideAction?.Name ?? behaviour?.CurrentActionName ?? "";
        public AnimalState PhotoState => overrideAction?.PhotoState ?? behaviour.PhotoState;
        public AnimalState State { get; private set; } = AnimalState.Despawned;

        public AnimalBrain(AnimalContext context, AnimalBehaviourSet behaviour)
        {
            this.context = context;
            this.behaviour = behaviour;
        }

        public void Request(AnimalState state)
        {
            if (context.Runtime.IsDead && state != AnimalState.Dead && state != AnimalState.Despawned) return;
            if (!requested.HasValue || Priority(state) >= Priority(requested.Value)) requested = state;
            if (!updating) Flush();
        }

        private static int Priority(AnimalState state) => state == AnimalState.Despawned ? 100
            : state == AnimalState.Dead ? 90 : state == AnimalState.Fleeing || state == AnimalState.Aggressive ? 50
            : state == AnimalState.Hiding ? 40 : state == AnimalState.Curious ? 30 : 0;

        public void Tick(float deltaTime)
        {
            updating = true;
            try
            {
                if (overrideAction != null)
                {
                    if (overrideAction.Tick(context, deltaTime) != AnimalActionStatus.Running)
                    {
                        overrideAction.Exit(context);
                        overrideAction = null;
                        requested ??= AnimalState.Daily;
                    }
                }
                else current?.Tick(context, deltaTime);
            }
            finally { updating = false; }
            Flush();
            if (pendingAction != null && !requested.HasValue)
            {
                AnimalAction next = pendingAction;
                pendingAction = null;
                StartAction(next);
            }
        }

        public bool StartAction(AnimalAction action)
        {
            if (State != AnimalState.Daily || action == null || overrideAction != null) return false;
            if (updating) { pendingAction = action; return true; }
            current?.Exit(context);
            current = null;
            overrideAction = action;
            updating = true;
            try { action.Enter(context); }
            finally { updating = false; }
            Flush();
            return true;
        }

        private void Flush()
        {
            updating = true;
            try
            {
                // Enter may request a follow-up state (e.g. no escape tree -> hide).
                for (int count = 0; requested.HasValue && count < 16; count++)
                {
                    AnimalState next = requested.Value;
                    requested = null;
                    current?.Exit(context);
                    overrideAction?.Exit(context);
                    overrideAction = null;
                    context.Motor.Stop();
                    State = next;
                    pendingAction = null;
                    context.Agent.OnStateEntered(next);
                    current = CreateState(next);
                    current?.Enter(context);
                }
            }
            finally { updating = false; }
        }

        private AnimalStateBase CreateState(AnimalState state)
        {
            switch (state)
            {
                case AnimalState.Daily: return new DelegateState(behaviour.EnterDaily, behaviour.TickDaily, behaviour.ExitDaily);
                case AnimalState.Curious: return new DelegateState(behaviour.EnterCurious, context.Agent.TickCurious, behaviour.ExitCurious);
                case AnimalState.Fleeing: return new ActionState(behaviour.CreateEscapeAction());
                case AnimalState.Aggressive: return new DelegateState(context.Agent.EnterAggressiveAction, context.Agent.TickAggressive, context.Agent.ExitAggressiveAction);
                case AnimalState.Hiding: return new DelegateState(behaviour.EnterHiding, behaviour.TickHiding, behaviour.ExitHiding);
                default: return null;
            }
        }

        public void Stop()
        {
            updating = true;
            try
            {
                requested = null;
                pendingAction = null;
                current?.Exit(context);
                overrideAction?.Exit(context);
                current = null;
                overrideAction = null;
                context.Motor.Stop();
            }
            finally { updating = false; }
        }

        private sealed class DelegateState : AnimalStateBase
        {
            private readonly Action enter, exit;
            private readonly Action<float> tick;
            public DelegateState(Action enter, Action<float> tick, Action exit)
            { this.enter = enter; this.tick = tick; this.exit = exit; }
            public override void Enter(AnimalContext _) { enter(); }
            public override void Tick(AnimalContext _, float dt) { tick(dt); }
            public override void Exit(AnimalContext _) { exit(); }
        }
        private sealed class ActionState : AnimalStateBase
        {
            private readonly AnimalAction action;
            public ActionState(AnimalAction action) { this.action = action; }
            public override void Enter(AnimalContext context) { action.Enter(context); }
            public override void Tick(AnimalContext context, float dt)
            { if (action.Tick(context, dt) != AnimalActionStatus.Running) context.Agent.ReturnToDaily(); }
            public override void Exit(AnimalContext context) { action.Exit(context); }
        }
    }
}
