using UnityEngine;

namespace AnimalGame.Animals
{
    [CreateAssetMenu(menuName = "Animal Game/Animals/Fly To Tree", fileName = "FlyToTree")]
    public sealed class FlyToTreeActionDefinition : TreeBirdActionDefinition
    {
        [Min(.1f)] public float SpeedMetersPerSecond = 9f;
        [Min(0f)] public float MinimumTreeStaySeconds = 15f;
        [Min(0f)] public float FlightCooldownSeconds = 20f;
        [Range(0f, .5f)] public float FlightSpeedVariation = .1f;
        public Vector2 FlightSpeedPeriodSeconds = new Vector2(1f, 2f);
        [Range(0f, 1f)] public float ArcProbability = .35f;
        [Range(0f, .3f)] public float ArcOffsetRatio = .08f;
        [Min(0f)] public float MaximumArcOffsetMeters = 2f;
        [Min(0f)] public float HealthyTreeWeight = .15f;
        [Min(0f)] public float DeadTreeWeight = .5f;
        public override bool CanRunForDebug(AnimalContext context, out string reason)
        {
            var bird = context.Agent.GetComponent<TreeBirdBehaviour>();
            bool valid = bird != null && bird.HasOtherTree(HealthyTreeWeight, DeadTreeWeight);
            reason = valid ? null : "No other valid tree is available.";
            return valid;
        }
        internal override bool CanRun(AnimalContext context)
            => base.CanRun(context) && context.Agent.GetComponent<TreeBirdBehaviour>().CanStartDailyFlight;
        protected override AnimalAction CreateForBird(TreeBirdBehaviour bird)
            => bird.TryChooseTree(out var tree, HealthyTreeWeight, DeadTreeWeight) ? new FlyToTreeAction(bird, tree, this) : null;
    }
}
