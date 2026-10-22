using AnimalGame.World;
using UnityEngine;

namespace AnimalGame.Animals
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Animal Game/Animals/Wolf Behaviour")]
    public sealed class WolfBehaviour : GenericAnimalBehaviour
    {
        [SerializeField, Range(.01f, 1f)] private float retreatSpeedMultiplier = .4f;
        public override bool SupportsAggression => false;
        public override void MoveWhileCurious(float dt)
        {
            if (Agent.Perception.PrimaryThreat == null) { Motor.Stop(); return; }
            Vector2 direction = Agent.Perception.EscapeDirection();
            Motor.SetTarget(Motor.CurrentMapPosition + direction * Mathf.Max(.6f, Config.ArrivalDistanceMeters * 3f),
                Config.DailyMoveSpeedMetersPerSecond * retreatSpeedMultiplier);
        }
        public override void TickCurious(float dt)
        {
            if (Agent.Perception.PrimaryThreat != null)
                Motor.LockFacing(InteractionShape.ToQuery(Agent.Perception.PrimaryThreat.position, Agent.Map) - Motor.CurrentMapPosition);
        }
        public override void ExitCurious() { Motor.ClearMovementOverrides(); }
    }
}
