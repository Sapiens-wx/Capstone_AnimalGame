using AnimalGame.World;
using UnityEngine;

namespace AnimalGame.MapTest
{
    // The root owns the single circle shared by collision, climbing and sight.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HeightMapPlacedObject))]
    [AddComponentMenu("Animal Game/Level/Vegetation")]
    public sealed class Vegetation : WorldInteraction
    {
        [Tooltip("Interpret Collider Radius in logical map metres instead of scaled local units.")]
        [SerializeField] private bool radiusInMapMeters = true;
        public bool RadiusInMapMeters
        {
            get => radiusInMapMeters;
            set { radiusInMapMeters = value; MarkSpatialDirty(); }
        }

        public override WorldInteractionKind Kind => base.Kind | WorldInteractionKind.Vegetation;

        private void Reset()
        {
            SetKind(WorldInteractionKind.Vegetation);
            ColliderType = ColliderType.Custom;
            ColliderShape = WorldInteractionColliderShape.Circle;
        }

        public override InteractionShape GetShape(MapTestSceneController map)
        {
            Vector2 center = InteractionShape.ToQuery(transform.TransformPoint(ColliderCenter), map);
            float radius = ColliderRadius;
            if (!radiusInMapMeters)
            {
                Vector2 right = InteractionShape.ToQuery(transform.TransformPoint(ColliderCenter + Vector2.right), map) - center;
                Vector2 up = InteractionShape.ToQuery(transform.TransformPoint(ColliderCenter + Vector2.up), map) - center;
                radius *= Mathf.Max(right.magnitude, up.magnitude);
            }
            return InteractionShape.Circle(center, radius);
        }

        public override InteractionShape GetShapeAfterMotion(Matrix4x4 worldDelta, MapTestSceneController map) =>
            InteractionPushPlan.TransformShape(GetShape(map), worldDelta, map);
    }
}
