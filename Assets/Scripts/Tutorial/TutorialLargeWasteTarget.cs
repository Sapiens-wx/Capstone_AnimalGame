using System;
using AnimalGame.Garbage;
using AnimalGame.MapTest;
using AnimalGame.RobotArm;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AnimalGame.Tutorial
{
    /// <summary>Owns only the tutorial's native large pile and its exact carried fragment.</summary>
    [DisallowMultipleComponent]
    public sealed class TutorialLargeWasteTarget : MonoBehaviour
    {
        [SerializeField] private HeightMapPlayerSceneBootstrap bootstrap;
        [SerializeField] private GameObject largeGarbagePrefab;
        [SerializeField] private GameObject mediumFragmentPrefab;
        [SerializeField, Min(1f)] private float minimumApproachDistanceMeters = 6f;
        [SerializeField, Min(1f)] private float maximumApproachDistanceMeters = 65f;
        [SerializeField, Min(.1f)] private float placementMarginMeters = .75f;

        private GameObject ownedRoot, sourceContainer, recoveryContainer;
        private TutorialLargeWasteBreakObserver sourceObserver;
        private bool resetting;
        private float nextPlacementAttemptAt;
        private int placementSearchAttempt;

        public WorldInteraction Target { get; private set; }
        public HeavyGarbagePull Pull { get; private set; }
        public WorldInteraction Fragment { get; private set; }
        public bool HasBroken { get; private set; }
        public Vector2 ApproachMapPosition { get; private set; }
        public string LastFailure { get; private set; }
        public event Action<WorldInteraction> Broken;

        public float DistanceMeters => bootstrap != null && bootstrap.map != null && bootstrap.robot != null
            && bootstrap.map.TrySampleWorldPosition(bootstrap.robot.transform.position, out Vector2 player, out _)
                ? Vector2.Distance(player, ApproachMapPosition) : float.PositiveInfinity;

        public void Configure(HeightMapPlayerSceneBootstrap sceneBootstrap)
        {
            if (bootstrap != null && bootstrap != sceneBootstrap) ResetTarget();
            bootstrap = sceneBootstrap;
        }

        /// <summary>Called by the active lesson. A genuinely split pile is never recreated.</summary>
        public bool EnsureTarget()
        {
            if (!CanCreate()) return false;
            if (HasBroken)
            {
                if (Fragment != null && Fragment.Available) { LastFailure = null; return true; }
                // The lesson stops asking after the exact fragment's native recycled callback.
                // This path repairs an accidental lost/deactivated fragment without undoing the break.
                if (Time.unscaledTime < nextPlacementAttemptAt) return false;
                nextPlacementAttemptAt = Time.unscaledTime + .75f;
                if (!TryFindPlacement(mediumFragmentPrefab, out Vector2 mediumPosition, out Vector2 mediumApproach))
                { LastFailure = "No clear dry spot is available for the recovered fragment."; return false; }
                DestroyOwned(recoveryContainer);
                recoveryContainer = InstantiateOwned(mediumFragmentPrefab, "Recovered Tutorial Fragment", mediumPosition);
                Fragment = recoveryContainer.GetComponentInChildren<WorldInteraction>();
                ApproachMapPosition = mediumApproach;
                LastFailure = null;
                Broken?.Invoke(Fragment);
                return Fragment != null;
            }

            if (Target != null && Target.Available) { LastFailure = null; return true; }
            if (Time.unscaledTime < nextPlacementAttemptAt) return false;
            nextPlacementAttemptAt = Time.unscaledTime + .75f;
            DisarmObserver();
            DestroyOwned(sourceContainer); sourceContainer = null; Target = null; Pull = null;
            if (!TryFindPlacement(largeGarbagePrefab, out Vector2 position, out Vector2 approach))
            { LastFailure = "No clear dry approach and retreat area is available for the large pile."; return false; }
            sourceContainer = InstantiateOwned(largeGarbagePrefab, "Tutorial Large Waste", position);
            Target = sourceContainer.GetComponentInChildren<WorldInteraction>();
            Pull = Target != null ? Target.GetComponent<HeavyGarbagePull>() : null;
            if (Target == null || Pull == null)
            {
                LastFailure = "The large waste prefab needs its native interaction and pull components.";
                DestroyOwned(sourceContainer); sourceContainer = null; Target = null; Pull = null;
                return false;
            }
            ApproachMapPosition = approach;
            sourceObserver = Target.gameObject.AddComponent<TutorialLargeWasteBreakObserver>();
            sourceObserver.Configure(this, Target, bootstrap.armController);
            LastFailure = null;
            return true;
        }

        internal void AcceptNativeBreak(TutorialLargeWasteBreakObserver observer, WorldInteraction source,
            WorldInteraction carried)
        {
            RobotArmController arm = bootstrap != null ? bootstrap.armController : null;
            if (resetting || HasBroken || observer != sourceObserver || source != Target
                || source == null || source.Kind != WorldInteractionKind.None || arm == null
                || carried == null || carried == source || carried != arm.HeldObject || carried.Owner != arm
                || !carried.Available || !carried.Recyclable || carried.Size != RecyclableSize.Medium
                || carried.gameObject.scene != gameObject.scene) return;
            HasBroken = true;
            Fragment = carried;
            LastFailure = null;
            // Native scattered pieces remain normal world garbage. Only this carried piece is
            // used for tutorial receipt and recovery; no nearby objects are inferred or claimed.
            Broken?.Invoke(carried);
        }

        public void ResetTarget()
        {
            resetting = true;
            DisarmObserver();
            if (Fragment != null && Fragment.gameObject.scene == gameObject.scene)
                DestroyOwned(Fragment.gameObject);
            DestroyOwned(ownedRoot);
            ownedRoot = sourceContainer = recoveryContainer = null;
            Target = Fragment = null; Pull = null; HasBroken = false;
            ApproachMapPosition = default; LastFailure = null;
            nextPlacementAttemptAt = 0f;
            placementSearchAttempt = 0;
            resetting = false;
        }

        private void DisarmObserver()
        { if (sourceObserver != null) sourceObserver.Disarm(); sourceObserver = null; }

        private bool CanCreate()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || gameObject.scene.path != TutorialDrivingLesson.ScenePath
                || bootstrap == null || bootstrap.gameObject.scene != gameObject.scene || bootstrap.map == null
                || !bootstrap.map.HasGeneratedMap || bootstrap.robot == null || bootstrap.armController == null
                || bootstrap.traversalEvaluator == null || !bootstrap.traversalEvaluator.IsInitialized
                || largeGarbagePrefab == null || mediumFragmentPrefab == null)
            { LastFailure = "Large waste is waiting for this tutorial scene's map, player and prefab references."; return false; }
            return true;
        }

        private GameObject InstantiateOwned(GameObject prefab, string label, Vector2 mapPosition)
        {
            if (ownedRoot == null)
            {
                ownedRoot = new GameObject("Large Waste Tutorial Targets (Runtime)");
                SceneManager.MoveGameObjectToScene(ownedRoot, gameObject.scene);
                ownedRoot.transform.SetParent(transform, true);
            }
            var container = new GameObject(label);
            container.SetActive(false);
            container.transform.SetParent(ownedRoot.transform, false);
            GameObject clone = Instantiate(prefab, bootstrap.map.MapPositionToWorld(mapPosition),
                prefab.transform.rotation, container.transform);
            clone.name = label + " (Owned Clone)";
            foreach (HeightMapPlacedObject placed in clone.GetComponentsInChildren<HeightMapPlacedObject>(true))
                placed.CaptureCurrentTransform();
            clone.SetActive(true);
            container.SetActive(true);
            return container;
        }

        private bool TryFindPlacement(GameObject prefab, out Vector2 position, out Vector2 approach)
        {
            position = approach = default;
            if (prefab == null || !bootstrap.map.TrySampleWorldPosition(bootstrap.robot.transform.position,
                out Vector2 player, out _)) return false;
            WorldInteraction template = prefab.GetComponentInChildren<WorldInteraction>(true);
            if (template == null) return false;
            InteractionShape shape = template.GetShape(bootstrap.map);
            float radius = ShapeRadius(shape);
            RobotMarkerView marker = bootstrap.robot.GetComponent<RobotMarkerView>();
            float bodyWorld = marker != null ? marker.BodyDiameter : .72f;
            if (marker != null && marker.MarkerVisualRoot != null)
                bodyWorld *= Mathf.Max(Mathf.Abs(marker.MarkerVisualRoot.lossyScale.x),
                    Mathf.Abs(marker.MarkerVisualRoot.lossyScale.y));
            float worldPerMeter = Mathf.Min(bootstrap.map.MapMetersToWorldDistance(Vector2.right, 1f),
                bootstrap.map.MapMetersToWorldDistance(Vector2.up, 1f));
            float bodyRadius = bodyWorld * .5f / Mathf.Max(.0001f, worldPerMeter);
            float standOff = bodyRadius * 1.12f;
            float clearRadius = radius + Mathf.Max(placementMarginMeters, bodyRadius * .4f);
            Vector2 preferred = bootstrap.map.WorldDirectionToMapDirection(bootstrap.robot.Forward);
            if (preferred.sqrMagnitude < .001f) preferred = Vector2.up;
            preferred.Normalize();
            // Keep looking beyond a crowded shoreline without relaxing the dry footprint,
            // approach or pulling-room checks. Interleaved bearings cover gaps between props;
            // retries also sample different bearings instead of repeating a failed search.
            float bearingOffset = placementSearchAttempt++ % 3 * 5f;
            float maximumDistance = Mathf.Max(minimumApproachDistanceMeters, maximumApproachDistanceMeters);
            int ringCount = Mathf.CeilToInt((maximumDistance - minimumApproachDistanceMeters) / 2.5f) + 1;
            for (int ring = 0; ring < ringCount; ring++)
            {
                float distance = Mathf.Min(maximumDistance, minimumApproachDistanceMeters + ring * 2.5f);
                for (int side = 0; side < 24; side++)
                {
                    int offset = (side + 1) / 2 * (side % 2 == 0 ? 1 : -1);
                    float angle = (offset * 15f + (ring % 2) * 7.5f + bearingOffset) * Mathf.Deg2Rad;
                    Vector2 direction = new Vector2(preferred.x * Mathf.Cos(angle) - preferred.y * Mathf.Sin(angle),
                        preferred.x * Mathf.Sin(angle) + preferred.y * Mathf.Cos(angle));
                    Vector2 candidate = player + direction * (distance + radius + standOff);
                    Vector2 stand = candidate - direction * (radius + standOff);
                    if (!ClearFootprint(candidate, clearRadius) || !ClearApproach(player, stand, direction, bodyRadius)) continue;
                    position = candidate; approach = stand;
                    return true;
                }
            }
            return false;
        }

        private bool ClearFootprint(Vector2 center, float radius)
        {
            if (!DryAndPassable(center)) return false;
            for (int ring = 1; ring <= 2; ring++)
                for (int point = 0; point < 16; point++)
                {
                    float angle = point * Mathf.PI / 8f;
                    if (!DryAndPassable(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius * ring * .5f))
                        return false;
                }
            return !WorldInteractionQuery.Query(InteractionShape.Circle(center, radius),
                WorldInteractionKind.Collision | WorldInteractionKind.BodyCollision
                    | WorldInteractionKind.Grabbable | WorldInteractionKind.Climbable,
                bootstrap.map, gameObject.scene, ignore: ownedRoot != null ? ownedRoot.transform : null,
                ignoreHeld: bootstrap.robot.transform);
        }

        private bool ClearApproach(Vector2 player, Vector2 stand, Vector2 direction, float bodyRadius)
        {
            if (!DryAndPassable(stand)) return false;
            Vector2 retreat = stand - direction * Mathf.Max(3f, bodyRadius * 3f);
            if (!DryAndPassable(retreat)) return false;
            var path = bootstrap.traversalEvaluator.EvaluateMapPath(player, stand);
            var back = bootstrap.traversalEvaluator.EvaluateMapPath(stand, retreat);
            if (!path.HasData || !path.IsPassable || path.RequiresHardStop
                || !back.HasData || !back.IsPassable || back.RequiresHardStop) return false;
            Transform ignore = ownedRoot != null ? ownedRoot.transform : null;
            var kinds = WorldInteractionKind.Collision | WorldInteractionKind.BodyCollision;
            // Match native movement's escape rule: a prop already touching the starting
            // clearance circle must not invalidate every route away from that same prop.
            // The destination and retreat remain fully checked for collisions below.
            return !WorldInteractionQuery.Query(InteractionShape.Capsule(player, stand, bodyRadius + .15f), kinds,
                    bootstrap.map, gameObject.scene, ignore: ignore,
                    previous: InteractionShape.Circle(player, bodyRadius + .15f), ignoreHeld: bootstrap.robot.transform)
                && !WorldInteractionQuery.Query(InteractionShape.Capsule(stand, retreat, bodyRadius + .15f), kinds,
                    bootstrap.map, gameObject.scene, ignore: ignore, ignoreHeld: bootstrap.robot.transform);
        }

        private bool DryAndPassable(Vector2 point)
        {
            if (!bootstrap.map.TrySampleMapPosition(point, out _)
                || (bootstrap.map.TrySampleStaticWaterMapPosition(point, out float depth) && depth > .01f)) return false;
            var ground = bootstrap.traversalEvaluator.EvaluateLocalSurface(bootstrap.map.MapPositionToWorld(point));
            return ground.HasData && ground.IsPassable && !ground.RequiresHardStop
                && ground.MaximumSurfaceSlopeAngle <= bootstrap.traversalEvaluator.LevelOneMaximumUphillAngle;
        }

        private static float ShapeRadius(InteractionShape shape)
        {
            if (!shape.IsBox) return shape.Radius + Vector2.Distance(shape.A, shape.B) * .5f;
            float radius = 0f;
            for (int i = 0; i < 4; i++) radius = Mathf.Max(radius, Vector2.Distance(shape.Center, shape.Vertex(i)));
            return radius;
        }

        private static void DestroyOwned(GameObject value)
        {
            if (value == null) return;
            value.SetActive(false);
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }

        private void OnDestroy() => ResetTarget();
    }
}
