using AnimalGame.MapTest;
using AnimalGame.World;
using UnityEngine;

namespace AnimalGame.Animals
{
    [DisallowMultipleComponent]
    public sealed class NorthAmericanPorcupineBehaviour : GenericAnimalBehaviour
    {
        [SerializeField, Min(0f)] private float quillDamage = 3f;
        [Tooltip("Optional artwork enabled while defending; do not assign the animal root.")]
        [SerializeField] private GameObject quillVisual;
        public TreeHabitat CurrentTree { get; private set; }
        public bool QuillsRaised { get; private set; }
        public override bool SupportsAggression => CurrentTree == null;
        public override bool IgnoresThreats => CurrentTree != null;
        public override bool CanBeHuntedBy(AnimalAgent hunter) => CurrentTree == null || hunter.Config.CanClimbTrees;
        public override void EnterDaily() { base.EnterDaily(); Motor.SetPerchedMode(CurrentTree != null); }
        public void LeaveTree() { CurrentTree = null; Motor.SetPerchedMode(false); }
        public void EnterTree(TreeHabitat tree) { CurrentTree = tree; Motor.Stop(); Motor.SetPerchedMode(true); }
        public bool TryFindTree(bool different, out TreeHabitat selected, out Vector2 target, bool escaping = false)
        {
            selected = null; target = default;
            float nearest = float.PositiveInfinity;
            foreach (TreeHabitat tree in TreeHabitat.ActiveTrees)
            {
                if (tree == null || !tree.isActiveAndEnabled || tree.gameObject.scene != gameObject.scene
                    || (different && tree == CurrentTree) || !tree.TryGetMapPosition(Agent.Map, out Vector2 center)) continue;
                float distance = Vector2.SqrMagnitude(center - Motor.CurrentMapPosition);
                if ((!escaping && distance > Config.ActivityRadiusMeters * Config.ActivityRadiusMeters) || distance >= nearest) continue;
                if (tree == CurrentTree) { selected = tree; target = Motor.CurrentMapPosition; nearest = distance; continue; }
                float radius = tree.PerchRadiusMeters;
                WorldInteraction obstacle = tree.GetComponentInChildren<WorldInteraction>();
                if (obstacle != null)
                {
                    var shape = obstacle.GetShape(Agent.Map);
                    radius = Mathf.Max(radius, shape.IsBox ? Mathf.Max(Vector2.Distance(shape.Center, shape.A), Vector2.Distance(shape.Center, shape.C)) : shape.Radius);
                }
                radius += Config.BodyRadiusMeters + .15f;
                Vector2 outward = (Motor.CurrentMapPosition - center).normalized;
                if (outward.sqrMagnitude < .001f) outward = Vector2.up;
                for (int i = 0; i < 16; i++)
                {
                    Vector2 point = center + SlopeEscapeAction.Rotate(outward, i * 22.5f) * radius;
                    if (!Motor.CanOccupyMapPosition(point)) continue;
                    nearest = distance; selected = tree; target = point; break;
                }
            }
            return selected != null;
        }
        public override AnimalAction CreateEscapeAction() => new PorcupineTreeAction(0f, AnimalSoundKind.Idle, false, true);
        public override AnimalAction CreateAggressiveAction() => new QuillDefenseAction();
        public void SetQuills(bool raised)
        {
            QuillsRaised = raised;
            if (quillVisual != null) quillVisual.SetActive(raised);
        }
        public override void OnDamaged(Transform attacker, AnimalState stateWhenHit)
        {
            if (stateWhenHit != AnimalState.Aggressive || attacker == null || !attacker.TryGetComponent(out AnimalAgent predator)
                || !predator.IsPresent || !predator.Config.Eats(Config)) return;
            // Direct health damage deliberately does not produce another attack/retaliation event.
            predator.Health.Damage(quillDamage);
        }
    }
}
