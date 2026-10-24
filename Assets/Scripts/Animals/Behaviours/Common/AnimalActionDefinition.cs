using UnityEngine;

namespace AnimalGame.Animals
{
    public abstract class AnimalActionDefinition : ScriptableObject
    {
        public abstract AnimalAction CreateRuntime();
        public virtual bool CanRunForDebug(AnimalContext context, out string reason) { reason = null; return true; }
        public virtual AnimalAction CreateRuntime(AnimalDailyBehaviourSettings settings) => CreateRuntime();
    }
}
