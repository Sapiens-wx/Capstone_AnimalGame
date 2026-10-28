using AnimalGame.MapTest;
using UnityEngine;

namespace AnimalGame.Animals
{
    [CreateAssetMenu(menuName = "Animal Game/Animals/Specific Animal Action", fileName = "SpecificAnimalAction")]
    public sealed class SpecificAnimalActionDefinition : AnimalActionDefinition
    {
        public enum ActionKind { SlowWander, RestOnGrass, BushOrGrassEating, ZigzagForaging, ShortFlight,
            HideInBurrow, RestAtBurrow, TreeEating, BarkEating, TreeLooking, ChangeTree, GroundSniff }
        [SerializeField] private ActionKind action;
        [SerializeField, Min(.05f)] private float durationSeconds = 8f;
        [SerializeField, Range(.05f, 1f)] private float speedMultiplier = .45f;
        public override bool CanRunForDebug(AnimalContext c, out string reason)
        {
            bool valid = action switch
            {
                ActionKind.RestOnGrass => AnimalDailyTargets.Terrain(c, TerrainKind.Grass, 10f, out _),
                ActionKind.BushOrGrassEating => AnimalDailyTargets.CanEatPlant(c, false, out _)
                    || AnimalDailyTargets.Terrain(c, TerrainKind.Grass, 90f, out _),
                ActionKind.ZigzagForaging => AnimalDailyTargets.Terrain(c, TerrainKind.Grass, 90f, out _),
                ActionKind.ShortFlight => c.Agent.Behaviour is PrairieGrouseBehaviour && GroundBirdFlightAction.TryLanding(c, out _),
                ActionKind.HideInBurrow or ActionKind.RestAtBurrow => c.Agent.Behaviour is AmericanMarmotBehaviour marmot && marmot.HomeBurrow != null,
                ActionKind.TreeEating or ActionKind.BarkEating or ActionKind.TreeLooking or ActionKind.ChangeTree =>
                    c.Agent.Behaviour is NorthAmericanPorcupineBehaviour porcupine && porcupine.TryFindTree(action == ActionKind.ChangeTree, out _, out _),
                _ => AnimalDailyTargets.CanRoam(c, out _)
            };
            reason = valid ? null : "No suitable target for this species action is available.";
            return valid;
        }
        public override AnimalAction CreateRuntime() => Create(durationSeconds);
        public override AnimalAction CreateRuntime(AnimalDailyBehaviourSettings settings) => Create(settings.ChooseDuration());
        private AnimalAction Create(float duration) => action switch
        {
            ActionKind.SlowWander => new SlowWanderAction(duration, speedMultiplier),
            ActionKind.RestOnGrass => new RestAtTerrainAction(duration, TerrainKind.Grass, 10f),
            ActionKind.BushOrGrassEating => new BushOrGrassEatingAction(duration),
            ActionKind.ZigzagForaging => new ZigzagForagingAction(duration),
            ActionKind.ShortFlight => new GroundBirdFlightAction(false),
            ActionKind.HideInBurrow => new BurrowAction(duration),
            ActionKind.RestAtBurrow => new BurrowAction(duration, false, true),
            ActionKind.TreeEating => new PorcupineTreeAction(duration, AnimalSoundKind.Eating),
            ActionKind.BarkEating => new PorcupineTreeAction(duration, AnimalSoundKind.BarkEating),
            ActionKind.TreeLooking => new PorcupineTreeAction(duration, AnimalSoundKind.Looking),
            ActionKind.ChangeTree => new PorcupineTreeAction(0f, AnimalSoundKind.Idle, true),
            ActionKind.GroundSniff => new PorcupineGroundSniffAction(duration, speedMultiplier),
            _ => throw new System.ArgumentOutOfRangeException()
        };
    }
}
