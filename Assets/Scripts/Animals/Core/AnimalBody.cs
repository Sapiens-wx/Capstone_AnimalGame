using AnimalGame.Garbage;
using AnimalGame.MapTest;
using AnimalGame.World;
using UnityEngine;

namespace AnimalGame.Animals
{
    [DisallowMultipleComponent]
    public sealed class AnimalBody : WorldInteraction
    {
        private AnimalAgent agent;
        public void Initialize(AnimalAgent value) { agent = value; MarkSpatialDirty(); }
        public override WorldInteractionKind Kind => WorldInteractionKind.Collision | WorldInteractionKind.Actor
            | (agent != null && agent.Health != null && agent.Health.IsDead ? WorldInteractionKind.Pushable : WorldInteractionKind.None);
        public override bool Available => base.Available && agent != null && agent.IsInitialized
            && !agent.Runtime.Removed && (!agent.IsHidden || agent.Health.IsDead);
        public override InteractionShape GetShape(MapTestSceneController map)
        {
            float radius = agent != null && agent.Config != null ? agent.Config.BodyRadiusMeters : .16f;
            return InteractionShape.Circle(InteractionShape.ToQuery(transform.position, map), radius);
        }

        public void BecomeCorpse()
        {
            MarkSpatialDirty();
            GarbageMotion motion = GetComponent<GarbageMotion>();
            if (motion == null) motion = gameObject.AddComponent<GarbageMotion>();
            motion.Initialize(agent.Map, agent.Simulation.World.Player);
        }
        public override InteractionShape GetShapeAfterMotion(Matrix4x4 delta, MapTestSceneController map) =>
            InteractionPushPlan.TransformShape(GetShape(map), delta, map);
    }
}
