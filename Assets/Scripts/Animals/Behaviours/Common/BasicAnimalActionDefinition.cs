using UnityEngine;

namespace AnimalGame.Animals
{
    [CreateAssetMenu(menuName = "Animal Game/Animals/Basic Action", fileName = "AnimalAction")]
    public sealed class BasicAnimalActionDefinition : AnimalActionDefinition
    {
        public enum ActionKind { Idle, Roam, Hunt, EatPlant }
        [SerializeField] private ActionKind action;
        [SerializeField, Min(.05f)] private float durationSeconds = 5f;
        public override AnimalAction CreateRuntime() => action == ActionKind.Hunt ? new HuntAction()
            : action == ActionKind.Roam ? new RoamAction(durationSeconds)
            : action == ActionKind.EatPlant ? new EatPlantAction(durationSeconds) : new IdleAction(durationSeconds);
    }
}
