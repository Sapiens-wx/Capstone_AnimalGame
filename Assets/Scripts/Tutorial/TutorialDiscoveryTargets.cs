using AnimalGame.Animals;
using AnimalGame.Discovery;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UIElements;

namespace AnimalGame.Tutorial
{
    /// <summary>Owns the teaching bird, its dead trunk and the scan object in the tutorial scene only.</summary>
    [DefaultExecutionOrder(1400)]
    [DisallowMultipleComponent]
    public sealed class TutorialDiscoveryTargets : MonoBehaviour
    {
        [SerializeField] private HeightMapPlayerSceneBootstrap bootstrap;
        [FormerlySerializedAs("muskratPrefab")]
        [SerializeField] private GameObject animalPrefab;
        [SerializeField] private GameObject trunkPrefab;
        [SerializeField] private GameObject smallGarbagePrefab;
        [Header("Tutorial Animal Placement")]
        [SerializeField] private bool useFixedAnimalMapPosition;
        [Tooltip("Optional fixed map coordinates of the teaching animal. Use dry, reachable ground.")]
        [SerializeField] private Vector2 fixedAnimalMapPositionMeters = new Vector2(124f, 79f);
        [Tooltip("Approach point used when a fixed teaching animal is beyond scan range.")]
        [SerializeField] private Vector2 animalObservationMapPositionMeters = new Vector2(140f, 107f);
        [Tooltip("The authored scan release zoom is 11 / 9 of its base size. Keep the animal outside this largest circle.")]
        [SerializeField, Min(1f)] private float releaseZoomMultiplier = 11f / 9f;
        [Tooltip("Maximum target distance in world units, also capped to the actual scanner radius minus a safety margin.")]
        [SerializeField, Min(.5f)] private float maximumSpawnDistanceWorld = 12f;
        [SerializeField, Min(.25f)] private float placementClearanceMeters = 1.25f;
        private GameObject ownedRoot, animalContainer, itemContainer, trunkContainer, actorContainer;
        private AnimalAgent animalAgent;
        private Vector2 animalAnchor;
        private bool departureRequested;
        public TutorialWoodpeckerActor AnimalActor { get; private set; }
        public TreeHabitat Trunk { get; private set; }
        public bool IsAnimalDeparting => AnimalActor != null && AnimalActor.IsDeparting;
        public bool HasAnimalDeparted => departureRequested && (AnimalActor == null || AnimalActor.HasDeparted);
        public float AnimalTravelledMeters => AnimalActor != null && AnimalActor.HasStartedDepartureFlight
            ? Vector2.Distance(AnimalActor.PerchMapPosition, AnimalActor.LastMapPosition) : 0f;
        public bool HasAnimalMotionStarted => AnimalActor != null && AnimalActor.HasStartedDepartureFlight;
        public bool BeginAnimalDeparture()
        {
            if (departureRequested) return true;
            if (AnimalActor == null || !AnimalActor.BeginDeparture()) return false;
            departureRequested = true;
            return true;
        }

        public DiscoverableEntity Animal { get; private set; }
        public ScannableInteractable Item { get; private set; }
        public bool IsAnimalReady => Animal != null && Animal.IsScannable && AnimalActor != null && AnimalActor.IsReady;
        public bool IsReady => (IsAnimalReady || IsAnimalDeparting || HasAnimalDeparted) && Item != null && Item.CanScan;
        public Vector2 AnimalMapPosition => Animal != null ? MapPosition(Animal.transform)
            : AnimalActor != null ? AnimalActor.LastMapPosition : animalAnchor;
        public Vector2 ItemMapPosition => MapPosition(Item != null ? Item.transform : null);
        public string LastFailure { get; private set; }
        public bool UsesFixedAnimalPosition => useFixedAnimalMapPosition;
        public Vector2 FixedAnimalMapPositionMeters => fixedAnimalMapPositionMeters;
        public Vector2 AnimalObservationMapPosition => useFixedAnimalMapPosition
            ? animalObservationMapPositionMeters : AnimalMapPosition;
        public bool IsAnimalWithinScanReach => Animal != null && bootstrap != null && bootstrap.bioScan != null
            && Vector2.Distance(Animal.transform.position, bootstrap.bioScan.FullyDeployedScanOriginWorld)
                <= bootstrap.bioScan.MaximumScanRadiusWorld - .1f;

        public void Configure(HeightMapPlayerSceneBootstrap sceneBootstrap, GameObject animalPrefab, GameObject itemPrefab)
        {
            if (bootstrap != sceneBootstrap || this.animalPrefab != animalPrefab || smallGarbagePrefab != itemPrefab) Cleanup();
            bootstrap = sceneBootstrap;
            this.animalPrefab = animalPrefab;
            smallGarbagePrefab = itemPrefab;
        }

        /// <summary>Returns ready only after the real animal has completed its normal initialization.</summary>
        public bool CreateTargets() => EnsureTargets();

        /// <summary>Call while the lesson is active to recreate a destroyed or deactivated owned target.</summary>
        public bool EnsureTargets()
        {
            if (!CanCreate()) return false;
            LastFailure = null;
            if (departureRequested) return IsReady;
            if (Animal == null || !Animal.gameObject.activeInHierarchy || (animalAgent != null && animalAgent.IsHidden))
            {
                DestroyOwned(animalContainer);
                DestroyOwned(trunkContainer);
                DestroyOwned(actorContainer);
                animalContainer = trunkContainer = actorContainer = null;
                Animal = null; animalAgent = null; Trunk = null; AnimalActor = null;
                Vector2 position = fixedAnimalMapPositionMeters;
                bool found = useFixedAnimalMapPosition
                    ? float.IsFinite(position.x) && float.IsFinite(position.y) && ValidMapPosition(position, true)
                    : TryFindTargetPosition(true, out position);
                if (!found)
                { LastFailure = useFixedAnimalMapPosition ? "The authored teaching animal position needs safe, reachable dry ground."
                    : "No safe animal position within scan range and outside the release circle is available."; return false; }
                if (!TryFindTrunkPosition(position, out Vector2 trunkPosition))
                { LastFailure = "The teaching bird needs a clear spot for its trunk."; return false; }
                trunkContainer = InstantiateTarget(trunkPrefab, "Tutorial Woodpecker Trunk", trunkPosition);
                Trunk = trunkContainer.GetComponentInChildren<TreeHabitat>(true);
                if (Trunk == null || !Trunk.IsDead)
                { LastFailure = "The tutorial trunk must supply a dead TreeHabitat."; DestroyOwned(trunkContainer); trunkContainer = null; return false; }
                animalContainer = InstantiateTarget(animalPrefab, "Tutorial Woodpecker", position);
                Animal = animalContainer.GetComponentInChildren<DiscoverableEntity>(true);
                animalAgent = animalContainer.GetComponentInChildren<AnimalAgent>(true);
                if (Animal == null || Animal.Kind != DiscoverableKind.Animal || animalAgent == null)
                { LastFailure = "The teaching animal prefab requires its real AnimalAgent and animal DiscoverableEntity."; DestroyOwned(animalContainer); animalContainer = null; Animal = null; return false; }
                animalAnchor = position;
                animalAgent.SetPerceptionSuppressed(true);
                Animal.ResetToUnknown();
                actorContainer = new GameObject("Tutorial Woodpecker Behaviour");
                actorContainer.transform.SetParent(ownedRoot.transform, false);
                AnimalActor = actorContainer.AddComponent<TutorialWoodpeckerActor>();
                if (!AnimalActor.Configure(bootstrap, animalAgent, Trunk))
                { LastFailure = AnimalActor.LastFailure; return false; }
                animalAnchor = AnimalActor.PerchMapPosition;
            }
            if (!IsAnimalReady && AnimalActor != null && !AnimalActor.Configure(bootstrap, animalAgent, Trunk))
            { LastFailure = AnimalActor.LastFailure; return false; }
            if (Item == null || !Item.gameObject.activeInHierarchy || !Item.CanScan)
            {
                DestroyOwned(itemContainer);
                itemContainer = null; Item = null;
                if (!TryFindTargetPosition(false, out Vector2 position))
                { LastFailure = "No safe nearby small-garbage position is currently visible."; return false; }
                itemContainer = InstantiateTarget(smallGarbagePrefab, "Tutorial Small Garbage", position);
                Item = itemContainer.GetComponentInChildren<ScannableInteractable>(true);
                WorldInteraction interaction = Item != null ? Item.GetComponent<WorldInteraction>() : null;
                if (Item == null || interaction == null || (interaction.Kind & WorldInteractionKind.Grabbable) == 0)
                { LastFailure = "The small-garbage prefab requires its real scan and grabbable interaction components."; DestroyOwned(itemContainer); itemContainer = null; Item = null; return false; }
                Item.EnableSmallAreaFill();
            }
            return IsReady;
        }

        /// <summary>Restarts discovery with fresh owned targets near the current player; never relocates the player.</summary>
        public void ResetTargets()
        {
            Cleanup();
            EnsureTargets();
        }

        public void Cleanup()
        {
            Animal = null; Item = null; animalAgent = null; Trunk = null; AnimalActor = null;
            DestroyOwned(ownedRoot);
            ownedRoot = animalContainer = itemContainer = trunkContainer = actorContainer = null;
            LastFailure = null;
            departureRequested = false;
        }

        private bool CanCreate()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || gameObject.scene.path != TutorialDrivingLesson.ScenePath
                || bootstrap == null || bootstrap.gameObject.scene != gameObject.scene || bootstrap.map == null
                || !bootstrap.map.HasGeneratedMap || bootstrap.robot == null || bootstrap.mapCamera == null
                || bootstrap.traversalEvaluator == null || !bootstrap.traversalEvaluator.IsInitialized
                || bootstrap.scanChargeUi == null || animalPrefab == null || trunkPrefab == null || smallGarbagePrefab == null)
            { LastFailure = "Tutorial targets are waiting for this scene's map, camera, scanner and prefab references."; return false; }
            return true;
        }

        private GameObject InstantiateTarget(GameObject prefab, string name, Vector2 mapPosition)
        {
            if (ownedRoot == null)
            {
                ownedRoot = new GameObject("Discovery Tutorial Targets (Runtime)");
                SceneManager.MoveGameObjectToScene(ownedRoot, gameObject.scene);
                ownedRoot.transform.SetParent(transform, true);
            }
            // An inactive staging parent prevents OnEnable/Start from seeing the prefab's neutral stored position.
            var container = new GameObject(name);
            container.SetActive(false);
            container.transform.SetParent(ownedRoot.transform, false);
            GameObject clone = Instantiate(prefab, bootstrap.map.MapPositionToWorld(mapPosition), Quaternion.identity, container.transform);
            clone.name = name + " (Owned Clone)";
            foreach (HeightMapPlacedObject placed in clone.GetComponentsInChildren<HeightMapPlacedObject>(true))
                placed.CaptureCurrentTransform();
            clone.SetActive(true);
            container.SetActive(true);
            return container;
        }

        /// <summary>Samples actual camera/HUD geometry and checks existing ground, path and interaction APIs.</summary>
        public bool TryFindTargetPosition(bool outsideCircle, out Vector2 mapPosition)
        {
            mapPosition = default;
            if (!CanCreate()) return false;
            var hud = bootstrap.mainUiObject != null ? bootstrap.mainUiObject.GetComponent<AnimalGame.MainUI.MainUI>() : null;
            if (hud == null || !hud.TryGetRingScreenGeometry(out Vector2 center, out float ringRadius)) return false;
            float fit = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
            float baseSize = bootstrap.scanChargeUi.BaseCameraOrthographicSize;
            float zoom = Mathf.Max(baseSize * releaseZoomMultiplier, bootstrap.mapCamera.orthographicSize)
                / Mathf.Max(.01f, bootstrap.mapCamera.orthographicSize);
            // At the default camera size, the release circle reaches past the former 10.8-unit cap.
            // Search the actual scannable band densely; permit an offscreen fallback with waypoint guidance.
            for (int presentation = 0; presentation < (outsideCircle ? 2 : 1); presentation++)
            for (int distance = 0; distance < 6; distance++)
            {
                float radius = outsideCircle ? ringRadius * zoom + (39f + 8f * distance) * fit
                    : ringRadius * (.2f + .07f * distance);
                for (int direction = 0; direction < 48; direction++)
                {
                    // Prefer the left side, clear of the right-hand lesson card. Alternate above/below.
                    int offset = (direction + 1) / 2 * (direction % 2 == 0 ? 1 : -1);
                    float angle = (180f + offset * 7.5f) * Mathf.Deg2Rad;
                    Vector2 screen = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    Ray ray = bootstrap.mapCamera.ScreenPointToRay(screen);
                    var plane = new Plane(Vector3.forward, new Vector3(0, 0, bootstrap.map.WorldBounds.center.z));
                    if (!plane.Raycast(ray, out float enter)) continue;
                    Vector3 world = ray.GetPoint(enter);
                    if (!bootstrap.map.TrySampleWorldPosition(world, out Vector2 candidate, out _)
                        || !FitsPresentation(candidate, outsideCircle, presentation == 0)
                        || !ValidMapPosition(candidate, outsideCircle)
                        || (outsideCircle && !TryFindTrunkPosition(candidate, out _))) continue;
                    mapPosition = candidate;
                    return true;
                }
            }
            return false;
        }

        private bool TryFindTrunkPosition(Vector2 birdPosition, out Vector2 position)
        {
            position = default;
            if (bootstrap == null || bootstrap.map == null || trunkPrefab == null || animalPrefab == null) return false;
            AnimalAgent prefabAgent = animalPrefab.GetComponentInChildren<AnimalAgent>(true);
            TreeHabitat prefabTrunk = trunkPrefab.GetComponentInChildren<TreeHabitat>(true);
            float bodyRadius = prefabAgent != null && prefabAgent.Config != null ? prefabAgent.Config.BodyRadiusMeters : .12f;
            float trunkRadius = prefabTrunk != null ? prefabTrunk.PerchRadiusMeters : .6f;
            float offset = trunkRadius + bodyRadius + .04f;
            bootstrap.map.TrySampleWorldPosition(bootstrap.robot.transform.position, out Vector2 player, out _);
            Vector2 away = (birdPosition - player).normalized;
            if (away.sqrMagnitude < .1f) away = Vector2.up;
            var kinds = WorldInteractionKind.Collision | WorldInteractionKind.BodyCollision
                | WorldInteractionKind.Grabbable | WorldInteractionKind.Climbable;
            for (int i = 0; i < 12; i++)
            {
                float angle = i * 30f * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(away.x * Mathf.Cos(angle) - away.y * Mathf.Sin(angle),
                    away.x * Mathf.Sin(angle) + away.y * Mathf.Cos(angle));
                Vector2 candidate = birdPosition + direction * offset;
                if (!bootstrap.map.TrySampleMapPosition(candidate, out _)
                    || (bootstrap.map.TrySampleStaticWaterMapPosition(candidate, out float depth) && depth > .01f)) continue;
                var surface = bootstrap.traversalEvaluator.EvaluateLocalSurface(bootstrap.map.MapPositionToWorld(candidate));
                if (!surface.HasData || !surface.IsPassable || surface.RequiresHardStop) continue;
                if (WorldInteractionQuery.Query(InteractionShape.Circle(candidate, trunkRadius + .15f), kinds,
                    bootstrap.map, gameObject.scene, ignore: ownedRoot != null ? ownedRoot.transform : null,
                    ignoreHeld: bootstrap.robot.transform)) continue;
                position = candidate; return true;
            }
            return false;
        }

        private bool FitsPresentation(Vector2 mapPosition, bool outsideCircle, bool requireViewport = true)
        {
            var hud = bootstrap.mainUiObject != null ? bootstrap.mainUiObject.GetComponent<AnimalGame.MainUI.MainUI>() : null;
            if (hud == null || !hud.TryGetRingScreenGeometry(out Vector2 center, out float radius)) return false;
            Camera camera = bootstrap.mapCamera;
            Vector3 world = bootstrap.map.MapPositionToWorld(mapPosition);
            Vector3 screen = camera.WorldToScreenPoint(world);
            float fit = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
            Rect viewport = camera.pixelRect;
            float margin = 34f * fit;
            float scanRange = bootstrap.bioScan != null ? bootstrap.bioScan.MaximumScanRadiusWorld - .2f
                : maximumSpawnDistanceWorld;
            if (screen.z < camera.nearClipPlane
                || Vector2.Distance(world, bootstrap.robot.transform.position) > maximumSpawnDistanceWorld
                || (bootstrap.bioScan != null && Vector2.Distance(world,
                    bootstrap.bioScan.FullyDeployedScanOriginWorld) > scanRange)) return false;
            if (requireViewport && (screen.x < viewport.xMin + margin || screen.x > viewport.xMax - margin
                || screen.y < viewport.yMin + margin || screen.y > viewport.yMax - margin)) return false;
            // A scan marker inside the camera rectangle can still be covered by the lesson card.
            VisualElement card = GetComponent<TutorialDrivingLesson>()?.Panel?.Root?.Q("TutorialPanel");
            if (card != null && card.worldBound.width > 1f)
            {
                Rect covered = card.worldBound;
                covered.xMin -= margin; covered.xMax += margin;
                covered.yMin -= margin; covered.yMax += margin;
                if (covered.Contains(new Vector2(screen.x, Screen.height - screen.y))) return false;
            }
            float expanded = Mathf.Max(bootstrap.scanChargeUi.BaseCameraOrthographicSize * releaseZoomMultiplier,
                camera.orthographicSize) / Mathf.Max(.01f, camera.orthographicSize);
            float distance = Vector2.Distance(screen, center);
            return outsideCircle ? distance >= radius * expanded + margin : distance <= radius - margin;
        }

        private bool ValidMapPosition(Vector2 position, bool animal)
        {
            MapTestSceneController map = bootstrap.map;
            if (!map.TrySampleMapPosition(position, out _) || !map.TrySampleWorldPosition(
                bootstrap.robot.transform.position, out Vector2 player, out _) || Vector2.Distance(player, position) < 3f) return false;
            if (map.TrySampleStaticWaterMapPosition(position, out float depth) && depth > .01f) return false;
            if (Animal != null && !animal && Vector2.Distance(position, AnimalMapPosition) < 3f) return false;
            if (Item != null && animal && Vector2.Distance(position, ItemMapPosition) < 3f) return false;
            var evaluator = bootstrap.traversalEvaluator;
            SlopeTraversalResult surface = evaluator.EvaluateLocalSurface(map.MapPositionToWorld(position));
            if (!surface.HasData || !surface.IsPassable || surface.RequiresHardStop
                || surface.MaximumSurfaceSlopeAngle > evaluator.LevelOneMaximumUphillAngle) return false;
            SlopeTraversalResult path = evaluator.EvaluateMapPath(player, position);
            if (!path.HasData || !path.IsPassable || path.RequiresHardStop) return false;
            var kinds = WorldInteractionKind.Collision | WorldInteractionKind.BodyCollision
                | WorldInteractionKind.Grabbable | WorldInteractionKind.Climbable;
            Transform ignore = ownedRoot != null ? ownedRoot.transform : null;
            if (WorldInteractionQuery.Query(InteractionShape.Circle(position, placementClearanceMeters), kinds,
                map, gameObject.scene, ignore: ignore)) return false;
            return !WorldInteractionQuery.Query(InteractionShape.Capsule(player, position, .8f),
                WorldInteractionKind.Collision | WorldInteractionKind.BodyCollision, map, gameObject.scene,
                ignore: ignore, ignoreHeld: bootstrap.robot.transform);
        }

        private Vector2 MapPosition(Transform target) => target != null && bootstrap != null && bootstrap.map != null
            && bootstrap.map.TrySampleWorldPosition(target.position, out Vector2 position, out _) ? position : Vector2.zero;

        private static void DestroyOwned(GameObject target)
        {
            if (target == null) return;
            target.SetActive(false);
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        private void OnDestroy() => Cleanup();

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!useFixedAnimalMapPosition || gameObject.scene.path != TutorialDrivingLesson.ScenePath) return;
            MapTestSceneController map = bootstrap != null ? bootstrap.map : null;
            if (map == null)
                foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                {
                    map = root.GetComponentInChildren<MapTestSceneController>();
                    if (map != null) break;
                }
            if (map == null || !map.HasGeneratedMap) return;
            Vector3 animal = map.MapPositionToWorld(fixedAnimalMapPositionMeters);
            Vector3 observation = map.MapPositionToWorld(animalObservationMapPositionMeters);
            float radius = Vector3.Distance(animal, map.MapPositionToWorld(fixedAnimalMapPositionMeters + Vector2.right));
            Gizmos.color = new Color(212f / 255f, 184f / 255f, 84f / 255f, 1f);
            Gizmos.DrawWireSphere(animal, radius);
            Gizmos.DrawWireSphere(observation, radius);
            Gizmos.DrawLine(animal, observation);
            UnityEditor.Handles.Label(animal, "Tutorial Woodpecker");
            UnityEditor.Handles.Label(observation, "Scan Approach");
        }
#endif
    }
}
