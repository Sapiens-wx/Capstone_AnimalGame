using System;
using AnimalGame.Animals;
using UnityEngine;

namespace AnimalGame.AnimalDebug
{
    public enum AnimalDebugCommand { Daily, Curious, Fleeing, Aggressive, Hunt, Eat }
    public enum AnimalDebugCommandStatus { Rejected, Queued, Started, Succeeded, Failed, Interrupted }

    public sealed class AnimalDebugCommandResult
    {
        public int Id { get; internal set; }
        public AnimalAgent Agent { get; internal set; }
        public AnimalDebugCommandStatus Status { get; internal set; }
        public string Message { get; internal set; }
    }

    /// <summary>One-shot commands. Reuses normal actions, species escapes and state entry/group effects.</summary>
    public static class AnimalDebugCommandService
    {
        private static int nextId;
        public static readonly AnimalDebugCommand[] Commands = (AnimalDebugCommand[])Enum.GetValues(typeof(AnimalDebugCommand));
        public static string Label(AnimalDebugCommand command) => command == AnimalDebugCommand.Daily ? "Daily (autonomous)"
            : command == AnimalDebugCommand.Curious ? "Curious / alert"
            : command == AnimalDebugCommand.Hunt ? "Hunt live prey"
            : command == AnimalDebugCommand.Eat ? "Eat" : command.ToString();

        public static bool Validate(AnimalAgent agent, AnimalDebugCommand command, out string reason)
            => Prepare(agent, command, out _, out _, out reason);

        public static string DailyLabel(AnimalDailyBehaviourSettings settings, int index)
            => (settings == null ? "Missing entry" : settings.Action != null ? settings.Action.name : settings.Behaviour.ToString())
                + " (Element " + index + ")";

        public static bool ValidateDaily(AnimalAgent agent, AnimalDailyBehaviourSettings settings, out string reason)
        {
            if (!Validate(agent, AnimalDebugCommand.Daily, out reason)) return false;
            bool found = false;
            foreach (AnimalDailyBehaviourSettings entry in agent.Config.DailyBehaviours)
                if (settings != null && ReferenceEquals(settings, entry)) { found = true; break; }
            if (!found) { reason = "This daily entry no longer belongs to the selected animal."; return false; }
            // Availability refreshes must not consume the simulation's random sequence
            // or instantiate/enter actions just to draw a menu.
            UnityEngine.Random.State random = UnityEngine.Random.state;
            try { return agent.Behaviour.CanRunDailyForDebug(settings, out reason); }
            finally { UnityEngine.Random.state = random; }
        }

        public static AnimalDebugCommandResult ExecuteDaily(AnimalAgent agent, AnimalDailyBehaviourSettings settings)
        {
            var result = new AnimalDebugCommandResult { Id = ++nextId, Agent = agent };
            if (!ValidateDaily(agent, settings, out string reason))
            { Set(result, AnimalDebugCommandStatus.Rejected, reason); return result; }
            Set(result, AnimalDebugCommandStatus.Queued, "Command queued.");
            agent.Brain.ScheduleDebugCommand(() =>
            {
                if (!ValidateDaily(agent, settings, out string failure))
                { Set(result, AnimalDebugCommandStatus.Rejected, failure); return; }
                AnimalAction action = agent.Behaviour.CreateDailyActionForDebug(settings);
                if (action == null)
                { Set(result, AnimalDebugCommandStatus.Rejected, "The selected daily behaviour could not create an action."); return; }
                bool accepted = agent.Brain.ReplaceForDebug(AnimalState.Daily,
                    new TrackedAction(action, result, AnimalDebugCommand.Daily, settings), () =>
                    {
                        agent.Perception.ResetDetection();
                        agent.Runtime.HuntCooldownUntil = agent.Runtime.FeedCooldownUntil = agent.Runtime.SparringCooldownUntil = agent.Runtime.Time;
                    });
                if (!accepted) Set(result, AnimalDebugCommandStatus.Rejected, "The animal can no longer accept this command.");
                else if (result.Status == AnimalDebugCommandStatus.Queued)
                    Set(result, AnimalDebugCommandStatus.Started, "Started: " + action.Name + " (once).");
            }, () => Set(result, AnimalDebugCommandStatus.Interrupted, "The queued command was cancelled or replaced."));
            return result;
        }

        private static Transform Threat(AnimalAgent agent)
        {
            Transform current = agent.Perception.PrimaryThreat;
            if (current != null && current.gameObject.activeInHierarchy) return current;
            var player = agent.Simulation.World.Player;
            return player != null && player.gameObject.activeInHierarchy ? player.transform : null;
        }

        private static bool Prepare(AnimalAgent agent, AnimalDebugCommand command,
            out AnimalAction action, out Transform threat, out string reason)
        {
            action = null; threat = null; reason = null;
            if (agent == null || !agent.isActiveAndEnabled || !agent.IsInitialized || agent.Runtime.Removed || !agent.IsPresent)
            { reason = "Select a living, visible animal."; return false; }
            switch (command)
            {
                case AnimalDebugCommand.Daily: return true;
                case AnimalDebugCommand.Aggressive:
                    if (!agent.SupportsAggression)
                    { reason = "This species does not support aggression."; return false; }
                    goto case AnimalDebugCommand.Curious;
                case AnimalDebugCommand.Curious:
                case AnimalDebugCommand.Fleeing:
                    threat = Threat(agent);
                    if (threat == null) { reason = "There is no valid threat or player."; return false; }
                    return true;
                case AnimalDebugCommand.Hunt:
                    if (!agent.Config.CanHunt) { reason = "This species has no prey in its diet."; return false; }
                    AnimalAgent prey = AnimalHuntTargets.Select(agent.Context, aliveOnly: true, ignoreCooldown: true);
                    if (prey == null) { reason = "No eligible live prey is within hunting range (diet, group or target retry rules)."; return false; }
                    action = new HuntAction(prey, startWithLivePrey: true);
                    return true;
                case AnimalDebugCommand.Eat:
                    // Carnivores prefer a corpse; herbivores and omnivores can use their configured plants.
                    if (agent.Config.CanHunt)
                    {
                        AnimalAgent corpse = AnimalHuntTargets.Select(agent.Context, corpseOnly: true, ignoreCooldown: true);
                        if (corpse != null) { action = new HuntAction(corpse, corpseOnly: true); return true; }
                    }
                    foreach (AnimalFoodSource source in AnimalFoodSource.ActiveSources)
                    {
                        if (source == null || !source.isActiveAndEnabled || source.gameObject.scene != agent.gameObject.scene
                            || source.SelectionWeight * agent.Config.GetFoodSelectionWeight(source.FoodType) <= 0f
                            || !source.TryGetMapPosition(agent.Map, out Vector2 position)
                            || Vector2.Distance(position, agent.Motor.CurrentMapPosition) > agent.Config.ActivityRadiusMeters) continue;
                        action = new EatPlantAction();
                        return true;
                    }
                    reason = agent.Config.CanHunt ? CorpseFailure(agent) : "No suitable plant is within the activity radius.";
                    return false;
                default: reason = "Unsupported command."; return false;
            }
        }

        private static string CorpseFailure(AnimalAgent agent)
        {
            foreach (AnimalAgent corpse in AnimalAgent.ActiveAgents)
            {
                if (corpse == null || !corpse.IsInitialized || corpse.Runtime.Removed || !corpse.Health.IsDead
                    || corpse.gameObject.scene != agent.gameObject.scene || !agent.Config.Eats(corpse.Config)
                    || (agent.Runtime.Group != null && corpse.Runtime.Group == agent.Runtime.Group)
                    || Vector2.Distance(corpse.Motor.CurrentMapPosition, agent.Motor.CurrentMapPosition) > agent.Config.HuntingRadiusMeters) continue;
                if (corpse.Health.CorpseRemaining <= 0f) return "Nearby corpses have no food remaining.";
                if (!corpse.Health.CanFeed(agent)) return "All feeding positions on the nearby corpse are occupied.";
                return "The nearby corpse is temporarily excluded by target retry rules.";
            }
            return "No suitable corpse or plant is within range.";
        }

        public static AnimalDebugCommandResult Execute(AnimalAgent agent, AnimalDebugCommand command)
        {
            var result = new AnimalDebugCommandResult { Id = ++nextId, Agent = agent };
            if (!Validate(agent, command, out string reason))
            { Set(result, AnimalDebugCommandStatus.Rejected, reason); return result; }
            Set(result, AnimalDebugCommandStatus.Queued, "Command queued.");
            agent.Brain.ScheduleDebugCommand(() =>
            {
                if (!Prepare(agent, command, out AnimalAction action, out Transform threat, out string failure))
                { Set(result, AnimalDebugCommandStatus.Rejected, failure); return; }
                AnimalState state = command == AnimalDebugCommand.Curious ? agent.ThreatResponse
                    : command == AnimalDebugCommand.Fleeing ? AnimalState.Fleeing
                    : command == AnimalDebugCommand.Aggressive ? AnimalState.Aggressive : AnimalState.Daily;
                AnimalAction tracked = action == null ? null : new TrackedAction(action, result, command);
                bool accepted = agent.Brain.ReplaceForDebug(state, tracked, () =>
                {
                    if (command == AnimalDebugCommand.Daily) agent.Perception.ResetDetection();
                    else if (threat != null) agent.Perception.Remember(threat);
                    // Reset after exiting the old action, whose Exit may itself establish a cooldown.
                    if (command == AnimalDebugCommand.Hunt || command == AnimalDebugCommand.Eat)
                    {
                        agent.Runtime.HuntCooldownUntil = agent.Runtime.Time;
                        agent.Runtime.FeedCooldownUntil = agent.Runtime.Time;
                    }
                });
                if (!accepted) Set(result, AnimalDebugCommandStatus.Rejected, "The animal can no longer accept this command.");
                else if (result.Status == AnimalDebugCommandStatus.Queued)
                {
                    Set(result, AnimalDebugCommandStatus.Started,
                        command == AnimalDebugCommand.Curious && state == AnimalState.Fleeing
                            ? "Started: Fleeing (this species reacts immediately)." : "Started: " + Label(command) + ".");
                    if (command == AnimalDebugCommand.Curious) agent.Runtime.Group?.Alert(agent, threat);
                }
            }, () => Set(result, AnimalDebugCommandStatus.Interrupted, "The queued command was cancelled or replaced."));
            return result;
        }

        private static void Set(AnimalDebugCommandResult result, AnimalDebugCommandStatus status, string message)
        { result.Status = status; result.Message = message; }

        private sealed class TrackedAction : AnimalAction
        {
            private readonly AnimalAction inner;
            private readonly AnimalDebugCommandResult result;
            private readonly AnimalDebugCommand command;
            private readonly AnimalDailyBehaviourSettings dailySettings;
            private bool finished;
            public TrackedAction(AnimalAction inner, AnimalDebugCommandResult result, AnimalDebugCommand command,
                AnimalDailyBehaviourSettings dailySettings = null)
            { this.inner = inner; this.result = result; this.command = command; this.dailySettings = dailySettings; }
            public override string Name => inner.Name;
            public override AnimalState PhotoState => inner.PhotoState;
            public override void Enter(AnimalContext c) { inner.Enter(c); }
            public override AnimalActionStatus Tick(AnimalContext c, float dt)
            {
                AnimalActionStatus status = inner.Tick(c, dt);
                if (status != AnimalActionStatus.Running)
                {
                    finished = true;
                    Set(result, status == AnimalActionStatus.Succeeded ? AnimalDebugCommandStatus.Succeeded : AnimalDebugCommandStatus.Failed,
                        status == AnimalActionStatus.Succeeded ? "Completed: " + (dailySettings != null ? inner.Name : Label(command)) + "."
                        : inner.FailureReason ?? "The action could not continue.");
                }
                return status;
            }
            public override void Exit(AnimalContext c)
            {
                inner.Exit(c);
                if (!finished) Set(result, AnimalDebugCommandStatus.Interrupted, "The animal changed state before the action completed.");
                if (dailySettings != null)
                {
                    if (finished && result.Status == AnimalDebugCommandStatus.Failed) c.Agent.DailyScheduler.Failed(dailySettings);
                    else if (finished) c.Agent.DailyScheduler.Complete(dailySettings);
                    return;
                }
                // Establish the same per-entry scheduling cooldown as a naturally completed daily action.
                foreach (AnimalDailyBehaviourSettings settings in c.Config.DailyBehaviours)
                    if (finished && settings != null && (command == AnimalDebugCommand.Hunt && settings.Behaviour == AnimalDailyBehaviourKind.Hunt
                        || command == AnimalDebugCommand.Eat && (settings.Behaviour == AnimalDailyBehaviourKind.EatAtNearbyPlant
                            || settings.Behaviour == AnimalDailyBehaviourKind.TravelToPlantAndEat)))
                    {
                        if (result.Status == AnimalDebugCommandStatus.Failed) c.Agent.DailyScheduler.Failed(settings);
                        else c.Agent.DailyScheduler.Complete(settings);
                    }
            }
        }
    }
}
