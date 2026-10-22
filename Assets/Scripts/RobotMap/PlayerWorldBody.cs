using AnimalGame.MapTest;
using AnimalGame.World;
using UnityEngine;

namespace AnimalGame.RobotMap
{
    [DisallowMultipleComponent]
    public sealed class PlayerWorldBody : WorldInteraction
    {
        private RobotMover mover;
        public override WorldInteractionKind Kind => WorldInteractionKind.Collision | WorldInteractionKind.Actor;
        public override InteractionShape GetShape(MapTestSceneController map)
        {
            if (mover == null) mover = GetComponent<RobotMover>();
            float radius = mover != null ? mover.BodyRadiusInQuery(map) : .75f;
            return InteractionShape.Circle(InteractionShape.ToQuery(transform.position, map), radius);
        }
    }
}
