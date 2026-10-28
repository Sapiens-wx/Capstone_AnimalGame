using AnimalGame.MapTest;
using UnityEngine;

namespace AnimalGame.Animals
{
    [CreateAssetMenu(menuName = "Animal Game/Animals/Terrain Animal Action", fileName = "TerrainAnimalAction")]
    public sealed class TerrainAnimalActionDefinition : AnimalActionDefinition
    {
        public enum ActionKind { LookAround, Graze, RestOnRock, WanderOnRock, RestNearPlant, Sniff, Howl, Mark, Spar }
        [SerializeField] private ActionKind action;
        [SerializeField, Min(.05f)] private float durationSeconds = 8f;
        [SerializeField, Range(.05f, 1f)] private float speedMultiplier = .45f;
        [SerializeField, Min(1f)] private float lookTurnSpeedDegreesPerSecond = 35f;
        public override bool CanRunForDebug(AnimalContext c, out string reason)
        {
            reason = null;
            bool valid;
            switch (action)
            {
                case ActionKind.Graze:
                    valid = AnimalDailyTargets.Terrain(c, TerrainKind.Grass, 90f, out _); break;
                case ActionKind.RestOnRock:
                    valid = AnimalDailyTargets.Terrain(c, TerrainKind.Rock, 10f, out _); break;
                case ActionKind.WanderOnRock:
                case ActionKind.Howl:
                    valid = AnimalDailyTargets.Terrain(c, TerrainKind.Rock, 90f, out _); break;
                case ActionKind.RestNearPlant:
                case ActionKind.Mark:
                    valid = AnimalDailyTargets.Plant(c, out _, out _, out _); break;
                case ActionKind.Spar:
                    valid = SparringAction.CanStart(c);
                    reason = valid ? null : "No available adult of this species in this group can spar.";
                    return valid;
                case ActionKind.Sniff: return AnimalDailyTargets.CanRoam(c, out reason);
                default: return true;
            }
            reason = valid ? null : "No suitable terrain or plant position is available for this action.";
            return valid;
        }
        public override AnimalAction CreateRuntime() => Create(durationSeconds);
        public override AnimalAction CreateRuntime(AnimalDailyBehaviourSettings settings) => Create(settings.ChooseDuration());
        private AnimalAction Create(float duration) => action switch
        {
            ActionKind.LookAround => new LookAroundAction(duration, lookTurnSpeedDegreesPerSecond),
            ActionKind.Graze => new RestAtTerrainAction(duration, TerrainKind.Grass, 90f, AnimalState.Eating),
            ActionKind.RestOnRock => new RestAtTerrainAction(duration, TerrainKind.Rock, 10f),
            ActionKind.WanderOnRock => new TerrainWanderAction(duration, speedMultiplier),
            ActionKind.RestNearPlant => new RestNearPlantAction(duration),
            ActionKind.Sniff => new SniffWanderAction(duration, speedMultiplier),
            ActionKind.Howl => new RestAtTerrainAction(duration, TerrainKind.Rock, 90f, AnimalState.Resting, true),
            ActionKind.Mark => new RestNearPlantAction(duration, true),
            ActionKind.Spar => new SparringAction(duration),
            _ => new IdleAction(duration)
        };
    }
}
