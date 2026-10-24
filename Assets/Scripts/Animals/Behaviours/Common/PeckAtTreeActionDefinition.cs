using UnityEngine;

namespace AnimalGame.Animals
{
    [CreateAssetMenu(menuName = "Animal Game/Animals/Peck At Tree", fileName = "PeckAtTree")]
    public sealed class PeckAtTreeActionDefinition : TreeBirdActionDefinition
    {
        [SerializeField] private Vector2 durationSeconds = new Vector2(3f, 6f);
        [SerializeField, Min(.05f)] private float approachSpeedMetersPerSecond = .35f;
        [SerializeField, Min(0f)] private float amplitudeMeters = .08f;
        [SerializeField, Min(.1f)] private float frequencyHz = 5.5f;
        [SerializeField, Min(1)] private int pecksPerSoundWave = 3;
        public override bool CanRunForDebug(AnimalContext context, out string reason)
        {
            var bird = context.Agent.GetComponent<PileatedWoodpeckerBehaviour>();
            bool valid = IsPerched(context, bird) && bird.CurrentTree.IsDead;
            reason = valid ? null : "Pecking requires the bird to be perched on a dead tree.";
            return valid;
        }
        protected override AnimalAction CreateForBird(TreeBirdBehaviour bird)
            => new WoodpeckerPeckAction((PileatedWoodpeckerBehaviour)bird,
                Random.Range(Mathf.Max(.05f, durationSeconds.x), Mathf.Max(.05f, Mathf.Max(durationSeconds.x, durationSeconds.y))),
                amplitudeMeters, frequencyHz, pecksPerSoundWave, approachSpeedMetersPerSecond);
    }
}
