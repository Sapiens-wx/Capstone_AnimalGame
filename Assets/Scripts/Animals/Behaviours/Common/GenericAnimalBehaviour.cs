using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.Animals
{
    // Default behaviour; special species override individual phases instead of duplicating Agent.
    public class GenericAnimalBehaviour : AnimalBehaviourSet
    {
        private readonly List<AnimalDailyBehaviourSettings> available = new();
        private AnimalAction daily;
        private RunAwayAction fleeing;
        [SerializeField] private bool juvenileIndividual;
        [SerializeField, Range(.1f, 1f)] private float juvenileSizeMultiplier = .65f;
        public override bool IsJuvenile => juvenileIndividual;
        public override bool SupportsAggression => !IsJuvenile;
        public override void Initialize(AnimalAgent agent)
        {
            base.Initialize(agent);
            if (juvenileIndividual) agent.PlaceholderView?.ScaleVisuals(juvenileSizeMultiplier);
        }
        public override void TickCurious(float dt)
        {
            if (Agent.Perception.PrimaryThreat != null)
                Motor.FaceMapPosition(AnimalGame.World.InteractionShape.ToQuery(Agent.Perception.PrimaryThreat.position, Agent.Map));
        }
        public override AnimalState PhotoState => HasConfiguredAction ? ConfiguredPhotoState : daily?.PhotoState ?? base.PhotoState;
        public override string CurrentActionName => HasConfiguredAction ? base.CurrentActionName : daily?.Name ?? "";
        public override void EnterDaily() { daily = null; }
        public override bool CanRunDailyForDebug(AnimalDailyBehaviourSettings settings, out string reason)
        {
            if (settings == null || settings.Action != null) return base.CanRunDailyForDebug(settings, out reason);
            reason = null;
            if (settings.Behaviour == AnimalDailyBehaviourKind.RoamAndLook)
                return AnimalDailyTargets.CanRoam(Agent.Context, out reason);
            if (settings.Behaviour == AnimalDailyBehaviourKind.EatAtNearbyPlant
                || settings.Behaviour == AnimalDailyBehaviourKind.TravelToPlantAndEat)
                return AnimalDailyTargets.CanEatPlant(Agent.Context, settings.Behaviour == AnimalDailyBehaviourKind.EatAtNearbyPlant, out reason);
            return base.CanRunDailyForDebug(settings, out reason);
        }
        public override AnimalAction CreateDailyActionForDebug(AnimalDailyBehaviourSettings settings)
        {
            if (settings.Action != null) return base.CreateDailyActionForDebug(settings);
            if (settings.Behaviour == AnimalDailyBehaviourKind.RoamAndLook) return new RoamAction(settings.ChooseDuration());
            if (settings.Behaviour == AnimalDailyBehaviourKind.EatAtNearbyPlant || settings.Behaviour == AnimalDailyBehaviourKind.TravelToPlantAndEat)
                return new EatPlantAction(settings.ChooseDuration(), settings.Behaviour == AnimalDailyBehaviourKind.EatAtNearbyPlant);
            return base.CreateDailyActionForDebug(settings);
        }
        public override void TickDaily(float dt)
        {
            if (TickConfiguredAction(dt)) return;
            if (daily != null)
            {
                if (daily.Tick(Agent.Context, dt) == AnimalActionStatus.Running) return;
                daily.Exit(Agent.Context);
                daily = null;
            }
            if (BeginGroupAction()) return;
            available.Clear();
            foreach (AnimalDailyBehaviourSettings settings in Config.DailyBehaviours)
                if (settings != null && Agent.DailyScheduler.Weight(settings) > 0f
                    && (IsSharedDaily(settings) || settings.Behaviour == AnimalDailyBehaviourKind.RoamAndLook
                        || settings.Behaviour == AnimalDailyBehaviourKind.EatAtNearbyPlant
                        || settings.Behaviour == AnimalDailyBehaviourKind.TravelToPlantAndEat)) available.Add(settings);
            AnimalDailyBehaviourSettings selected = Agent.DailyScheduler.Choose(available);
            if (selected != null && BeginConfiguredAction(selected)) return;
            if (selected != null && (selected.Behaviour == AnimalDailyBehaviourKind.EatAtNearbyPlant || selected.Behaviour == AnimalDailyBehaviourKind.TravelToPlantAndEat))
            { BeginRuntimeAction(new EatPlantAction(selected.ChooseDuration(), selected.Behaviour == AnimalDailyBehaviourKind.EatAtNearbyPlant), selected); return; }
            daily = selected != null ? new RoamAction(selected.ChooseDuration()) : new IdleAction(1f);
            daily.Enter(Agent.Context);
        }
        public override void ExitDaily()
        { CancelConfiguredAction(); daily?.Exit(Agent.Context); daily = null; }
        public override void EnterFleeing() { fleeing = new RunAwayAction(); fleeing.Enter(Agent.Context); }
        public override void TickFleeing(float dt) { fleeing.Tick(Agent.Context, dt); }
        public override void ExitFleeing() { fleeing?.Exit(Agent.Context); fleeing = null; }
    }
}
