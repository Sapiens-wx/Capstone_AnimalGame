using UnityEngine;

namespace AnimalGame.Animals
{
    public abstract class TreeBirdActionDefinition : AnimalActionDefinition
    {
        public override AnimalAction CreateRuntime()
            => throw new System.InvalidOperationException("Tree bird actions require an animal context.");
        internal virtual bool CanRun(AnimalContext context) => CanRunForDebug(context, out _);
        protected static bool IsPerched(AnimalContext context, TreeBirdBehaviour bird)
            => bird != null && bird.IsUsableTree(bird.CurrentTree)
                && bird.CurrentTree.ContainsActivityPoint(context.Agent.Map, context.Motor.CurrentMapPosition, context.Config.BodyRadiusMeters);
        public override AnimalAction CreateRuntime(AnimalDailyBehaviourSettings settings, AnimalContext context)
            => CanRunForDebug(context, out _) ? CreateForBird(context.Agent.GetComponent<TreeBirdBehaviour>()) : null;
        protected abstract AnimalAction CreateForBird(TreeBirdBehaviour bird);
    }
}
