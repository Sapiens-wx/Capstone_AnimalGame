using AnimalGame.MapTest;
using AnimalGame.World;
using UnityEngine;

namespace AnimalGame.RobotMap
{
    [DisallowMultipleComponent]
    public sealed class PlayerWorldBody : WorldInteraction
    {
        private RobotMover mover;
        public override WorldInteractionKind Kind => TryGetComponent(out RobotMover player) && player.IgnoreCollisionAndTerrain
            ? WorldInteractionKind.None : WorldInteractionKind.Collision | WorldInteractionKind.Actor;
        public override InteractionShape GetShape(MapTestSceneController map)
        {
            if (mover == null) mover = GetComponent<RobotMover>();
            return mover.GetCollisionShape(map);
        }
        public override InteractionShape GetShapeAfterMotion(Matrix4x4 worldDelta, MapTestSceneController map) =>
            InteractionPushPlan.TransformShape(GetShape(map), worldDelta, map);
    }
}
