using UnityEngine;

namespace AnimalGame.Animals
{
    [CreateAssetMenu(menuName = "Animal Game/Animals/Basic Action", fileName = "AnimalAction")]
    public sealed class BasicAnimalActionDefinition : AnimalActionDefinition
    {
        public enum ActionKind { Idle, Roam, Hunt, EatPlant }
        [SerializeField] private ActionKind action;
        [SerializeField, Min(.05f)] private float durationSeconds = 5f;
        public override bool CanRunForDebug(AnimalContext context, out string reason)
        {
            reason = null;
            if (action == ActionKind.EatPlant) return AnimalDailyTargets.CanEatPlant(context, false, out reason);
            if (action == ActionKind.Roam) return AnimalDailyTargets.CanRoam(context, out reason);
            if (action != ActionKind.Hunt) return true;
            if (context.Config.CanHunt && AnimalHuntTargets.Select(context, ignoreCooldown: true) != null) return true;
            reason = "No eligible prey or corpse is available."; return false;
        }
        public override AnimalAction CreateRuntime() => action == ActionKind.Hunt ? new HuntAction()
            : action == ActionKind.Roam ? new RoamAction(durationSeconds)
            : action == ActionKind.EatPlant ? new EatPlantAction(durationSeconds) : new IdleAction(durationSeconds);
    }
}
