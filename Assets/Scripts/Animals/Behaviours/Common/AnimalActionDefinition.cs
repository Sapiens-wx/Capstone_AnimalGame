using UnityEngine;

namespace AnimalGame.Animals
{
    public abstract class AnimalActionDefinition : ScriptableObject
    {
        public abstract AnimalAction CreateRuntime();
        public virtual AnimalAction CreateRuntime(AnimalDailyBehaviourSettings settings) => CreateRuntime();
    }
}
