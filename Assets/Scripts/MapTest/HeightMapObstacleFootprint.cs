using System.Collections.Generic;
using UnityEngine;
using AnimalGame.World;

namespace AnimalGame.MapTest
{
    public enum ObstacleSightBlocking { MatchTraversal, AlwaysBlock, NeverBlock }

    /// <summary>
    /// Circular traversal or sight footprint for a placed map prop. Active scene
    /// instances register automatically without treating the visual canopy as solid.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Animal Game/Level/Height Map Obstacle Footprint")]
    public sealed class HeightMapObstacleFootprint : WorldInteraction
    {
        private static readonly HashSet<HeightMapObstacleFootprint>
            activeFootprints = new HashSet<HeightMapObstacleFootprint>();

        // SPATIAL INDEX: use BlocksTraversal/RadiusMeters setters. Direct writes
        // or future GetShape/Kind-dependent fields must call MarkSpatialDirty().
        [Tooltip("When enabled, normal traversal and tumble sweeps treat this footprint as a hard obstacle.")]
        [SerializeField] private bool blocksTraversal = true;

        [Tooltip("Match Traversal preserves existing animal sight behaviour. Always Block keeps sight occluded even when the player can pass through this footprint.")]
        [SerializeField] private ObstacleSightBlocking sightBlocking = ObstacleSightBlocking.MatchTraversal;

        [Tooltip("Circular solid-core radius in logical map meters, excluding leaves and canopy artwork.")]
        [SerializeField, Min(0f)] private float radiusMeters = 0.3f;
        [SerializeField] private Color gizmoColor =
            new Color(1f, 0.48f, 0.16f, 0.9f);

        public bool BlocksTraversal { get => blocksTraversal; set { blocksTraversal = value; MarkSpatialDirty(); } }
        public ObstacleSightBlocking SightBlocking { get => sightBlocking; set => sightBlocking = value; }
        public bool BlocksSight => sightBlocking switch
        {
            ObstacleSightBlocking.AlwaysBlock => true,
            ObstacleSightBlocking.NeverBlock => false,
            _ => blocksTraversal
        };
        public override WorldInteractionKind Kind => WorldInteractionKind.Collision;
        public override bool Available => base.Available && blocksTraversal && radiusMeters > 0f;
        public override InteractionShape GetShape(MapTestSceneController map)
        {
            return InteractionShape.Capsule(
                InteractionShape.ToQuery(transform.position, map),
                InteractionShape.ToQuery(transform.position, map), radiusMeters);
        }
        public float RadiusMeters
        {
            get => radiusMeters;
            set { radiusMeters = Mathf.Max(0f, value); MarkSpatialDirty(); }
        }
        public static IEnumerable<HeightMapObstacleFootprint>
            ActiveFootprints => activeFootprints;

        protected override void OnEnable()
        {
            base.OnEnable();
            // Prefab assets do not belong to a valid scene and must never act
            // like an obstacle at world origin while they are being imported.
            if (gameObject.scene.IsValid())
                activeFootprints.Add(this);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            activeFootprints.Remove(this);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            activeFootprints.Remove(this);
        }

        protected override void OnValidate()
        {
            radiusMeters = Mathf.Max(0f, radiusMeters);
            base.OnValidate();
        }

        private void OnDrawGizmosSelected()
        {
            if (!blocksTraversal || radiusMeters <= 0f)
                return;

            HeightMapPlacedObject placedObject =
                GetComponentInParent<HeightMapPlacedObject>();
            MapTestSceneController map = placedObject != null
                ? placedObject.Map
                : null;
            if (map == null)
                map = FindObjectOfType<MapTestSceneController>();
            if (map == null || !map.HasGeneratedMap)
                return;

            float worldRadiusX = map.MapMetersToWorldDistance(
                Vector2.right,
                radiusMeters);
            float worldRadiusY = map.MapMetersToWorldDistance(
                Vector2.up,
                radiusMeters);

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;
            Gizmos.matrix = Matrix4x4.TRS(
                transform.position,
                Quaternion.identity,
                new Vector3(worldRadiusX, worldRadiusY, 1f));
            Gizmos.color = gizmoColor;
            Gizmos.DrawWireSphere(Vector3.zero, 1f);
            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
    }
}
