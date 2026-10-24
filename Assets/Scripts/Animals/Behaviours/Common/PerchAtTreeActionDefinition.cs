using UnityEngine;

namespace AnimalGame.Animals
{
    [CreateAssetMenu(menuName = "Animal Game/Animals/Perch At Tree", fileName = "PerchAtTree")]
    public sealed class PerchAtTreeActionDefinition : TreeBirdActionDefinition
    {
        [SerializeField] private Vector2 durationSeconds = new Vector2(5f, 10f);
        [SerializeField, Min(.05f)] private float approachSpeedMetersPerSecond = .35f;
        [SerializeField] private bool faceTrunk = true;
        [SerializeField] private bool playIdleSound = true;
        public override bool CanRunForDebug(AnimalContext context, out string reason)
        {
            bool valid = IsPerched(context, context.Agent.GetComponent<TreeBirdBehaviour>());
            reason = valid ? null : "The bird must be perched on a valid tree.";
            return valid;
        }
        protected override AnimalAction CreateForBird(TreeBirdBehaviour bird)
            => new PerchAtTreeAction(bird, Random.Range(Mathf.Max(.05f, durationSeconds.x), Mathf.Max(.05f, Mathf.Max(durationSeconds.x, durationSeconds.y))),
                approachSpeedMetersPerSecond, faceTrunk, playIdleSound);
    }
}
