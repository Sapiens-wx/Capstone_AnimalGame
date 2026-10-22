using AnimalGame.Animals;
using AnimalGame.Discovery;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace AnimalGame.Tutorial
{
    /// <summary>Owns only the two runtime tutorial clones. Creation is explicitly requested by the lesson.</summary>
    [DefaultExecutionOrder(1400)]
    [DisallowMultipleComponent]
    public sealed class TutorialDiscoveryTargets : MonoBehaviour
    {
        [SerializeField] private HeightMapPlayerSceneBootstrap bootstrap;
        [SerializeField] private GameObject muskratPrefab;
        [SerializeField] private GameObject smallGarbagePrefab;
        [Header("Fixed Tutorial Muskrat")]
        [SerializeField] private bool useFixedAnimalMapPosition;
        [Tooltip("Logical map coordinates of the teaching muskrat. This point may be in water.")]
        [SerializeField] private Vector2 fixedAnimalMapPositionMeters = new Vector2(140f, 62.5f);
        [Tooltip("Dry approach point used when the muskrat is beyond scan range. The player never needs to enter the water.")]
        [SerializeField] private Vector2 animalObservationMapPositionMeters = new Vector2(140f, 107f);
        [Tooltip("The authored scan release zoom is 11 / 9 of its base size. Keep the animal outside this largest circle.")]
        [SerializeField, Min(1f)] private float releaseZoomMultiplier = 11f / 9f;
        [Tooltip("Maximum target distance in world units, also capped to the actual scanner radius minus a safety margin.")]
        [SerializeField, Min(.5f)] private float maximumSpawnDistanceWorld = 12f;
        [SerializeField, Min(.25f)] private float placementClearanceMeters = 1.25f;
        [SerializeField, Min(0f)] private float animalAmbientRadiusMeters;
        [SerializeField, Min(.1f)] private float animalAmbientPeriodSeconds = 8f;
        [Header("Scanned Muskrat Movement")]
        [SerializeField, Min(0f)] private float movementDelaySeconds = 2f;
        [SerializeField, Min(.05f)] private float movementSpeedMetersPerSecond = .5f;
        [SerializeField, Min(0f)] private float movementEndpointPauseSeconds = 2.5f;
        [SerializeField] private Vector2 movementOffsetMeters = new Vector2(3f, 1.5f);

        private GameObject ownedRoot, animalContainer, itemContainer;
        private AnimalAgent animalAgent;
        private Vector2 animalAnchor;
        private bool requested, animalFrozen;
        private float ambientStartedAt;
        private bool motionRequested, motionPaused, motionRouteReady, movingToEnd = true;
        private float motionWait;
        private Vector2 motionEnd;
        public float AnimalTravelledMeters { get; private set; }
        public bool HasAnimalMotionStarted => motionRequested;
        public string MotionFailure { get; private set; }

        public void StartAnimalMotion()
        {
            if (motionRequested || Animal == null) return;
            motionRequested = true; motionWait = movementDelaySeconds;
        }
        public void SetAnimalMotionPaused(bool value) => motionPaused = value;

        public DiscoverableEntity Animal { get; private set; }
        public ScannableInteractable Item { get; private set; }
        public bool IsAnimalReady => Animal != null && Animal.IsScannable && animalFrozen;
        public bool IsReady => IsAnimalReady && Item != null && Item.CanScan;
        public Vector2 AnimalMapPosition => MapPosition(Animal != null ? Animal.transform : null);
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
            if (bootstrap != sceneBootstrap || muskratPrefab != animalPrefab || smallGarbagePrefab != itemPrefab) Cleanup();
            bootstrap = sceneBootstrap;
            muskratPrefab = animalPrefab;
            smallGarbagePrefab = itemPrefab;
        }

        /// <summary>Returns ready only after the real animal has completed its normal initialization.</summary>
        public bool CreateTargets() => EnsureTargets();

        /// <summary>Call while the lesson is active to recreate a destroyed or deactivated owned target.</summary>
        public bool EnsureTargets()
        {
            if (!CanCreate()) return false;
            requested = true;
            LastFailure = null;
            if (Animal == null || !Animal.gameObject.activeInHierarchy || (animalAgent != null && animalAgent.IsHidden))
            {
                DestroyOwned(animalContainer);
                animalContainer = null; Animal = null; animalAgent = null; animalFrozen = false;
                Vector2 position = fixedAnimalMapPositionMeters;
                bool found = useFixedAnimalMapPosition
                    ? float.IsFinite(position.x) && float.IsFinite(position.y) && bootstrap.map.TrySampleMapPosition(position, out _)
                    : TryFindTargetPosition(true, out position);
                if (!found)
                { LastFailure = useFixedAnimalMapPosition ? "The authored muskrat position is outside the map."
                    : "No safe animal position within scan range and outside the release circle is available."; return false; }
                animalContainer = InstantiateTarget(muskratPrefab, "Tutorial Muskrat", position);
                Animal = animalContainer.GetComponentInChildren<DiscoverableEntity>(true);
                animalAgent = animalContainer.GetComponentInChildren<AnimalAgent>(true);
                if (Animal == null || Animal.Kind != DiscoverableKind.Animal || animalAgent == null)
                { LastFailure = "The muskrat prefab requires its real AnimalAgent and animal DiscoverableEntity."; DestroyOwned(animalContainer); animalContainer = null; Animal = null; return false; }
                animalAnchor = position;
                motionRequested = motionRouteReady = false; movingToEnd = true;
                AnimalTravelledMeters = 0f; MotionFailure = null;
                animalAgent.SetPerceptionSuppressed(true);
                Animal.ResetToUnknown();
                ambientStartedAt = Time.time;
            }
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
            FreezeInitializedAnimal();
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
            requested = false;
            Animal = null; Item = null; animalAgent = null; animalFrozen = false;
            DestroyOwned(ownedRoot);
            ownedRoot = animalContainer = itemContainer = null;
            LastFailure = null;
            motionRequested = motionPaused = motionRouteReady = false;
            AnimalTravelledMeters = 0f; MotionFailure = null; movingToEnd = true;
        }

        private bool CanCreate()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || gameObject.scene.path != TutorialDrivingLesson.ScenePath
                || bootstrap == null || bootstrap.gameObject.scene != gameObject.scene || bootstrap.map == null
                || !bootstrap.map.HasGeneratedMap || bootstrap.robot == null || bootstrap.mapCamera == null
                || bootstrap.traversalEvaluator == null || !bootstrap.traversalEvaluator.IsInitialized
                || bootstrap.scanChargeUi == null || muskratPrefab == null || smallGarbagePrefab == null)
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

        private void LateUpdate()
        {
            if (!requested || !Application.isPlaying) return;
            FreezeInitializedAnimal();
            if (motionRequested) { TickScannedAnimalMotion(); return; }
            if (!animalFrozen || useFixedAnimalMapPosition || animalAmbientRadiusMeters <= 0f || Animal == null || Time.deltaTime <= 0f
                || !CanCreate() || AnimalGame.MainUI.MainUI.BlocksGameplay) return;
            float phase = (Time.time - ambientStartedAt) / Mathf.Max(.1f, animalAmbientPeriodSeconds) * Mathf.PI * 2f;
            Vector2 candidate = animalAnchor + new Vector2(Mathf.Sin(phase), Mathf.Sin(phase * 2f) * .35f)
                * Mathf.Min(1f, animalAmbientRadiusMeters);
            if (ValidMapPosition(candidate, true) && FitsPresentation(candidate, true))
            {
                Animal.transform.position = bootstrap.map.MapPositionToWorld(candidate);
                Animal.GetComponent<HeightMapPlacedObject>()?.CaptureCurrentTransform();
            }
        }

        private void TickScannedAnimalMotion()
        {
            if (!animalFrozen || Animal == null || motionPaused || Time.deltaTime <= 0f
                || AnimalGame.MainUI.MainUI.BlocksGameplay) return;
            PhotoModeController camera = bootstrap != null ? bootstrap.photoMode : null;
            if ((camera != null && (camera.IsFocusing || camera.IsCapturing || camera.IsReviewing))
                || (bootstrap.photoResultView != null && bootstrap.photoResultView.IsShowing)) return;
            if (!motionRouteReady)
            {
                Vector2 offset = movementOffsetMeters;
                if (offset.magnitude < 2.5f) offset = new Vector2(3f, 1.5f);
                for (int attempt = 0; attempt < 13; attempt++)
                {
                    Vector2 delta = attempt == 0 ? offset
                        : new Vector2(Mathf.Cos((attempt - 1) * Mathf.PI / 6f), Mathf.Sin((attempt - 1) * Mathf.PI / 6f)) * 3f;
                    if (!IsWaterRoute(animalAnchor, animalAnchor + delta)) continue;
                    motionEnd = animalAnchor + delta; motionRouteReady = true; MotionFailure = null; break;
                }
                if (!motionRouteReady) { MotionFailure = "No short water route is available around the teaching muskrat."; return; }
            }
            if (motionWait > 0f) { motionWait = Mathf.Max(0f, motionWait - Time.deltaTime); return; }
            Vector2 current = AnimalMapPosition;
            Vector2 target = movingToEnd ? motionEnd : animalAnchor;
            Vector2 next = Vector2.MoveTowards(current, target, movementSpeedMetersPerSecond * Time.deltaTime);
            if (!IsWaterRoute(current, next)) { movingToEnd = !movingToEnd; motionWait = movementEndpointPauseSeconds; return; }
            AnimalTravelledMeters += Vector2.Distance(current, next);
            Vector2 direction = next - current;
            Animal.transform.position = bootstrap.map.MapPositionToWorld(next);
            if (direction.sqrMagnitude > .0000001f)
                Animal.transform.rotation = Quaternion.FromToRotation(Vector3.up, new Vector3(direction.x, direction.y, 0f));
            Animal.GetComponent<HeightMapPlacedObject>()?.CaptureCurrentTransform();
            if (Vector2.Distance(next, target) <= .001f)
            { movingToEnd = !movingToEnd; motionWait = movementEndpointPauseSeconds; }
        }

        private bool IsWaterRoute(Vector2 from, Vector2 to)
        {
            if (bootstrap == null || bootstrap.map == null) return false;
            int samples = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, to) / .25f));
            for (int index = 0; index <= samples; index++)
            {
                Vector2 point = Vector2.Lerp(from, to, index / (float)samples);
                if (!bootstrap.map.TrySampleMapPosition(point, out _)
                    || !bootstrap.map.TrySampleStaticWaterMapPosition(point, out float depth) || depth <= .01f) return false;
            }
            return true;
        }

        private void FreezeInitializedAnimal()
        {
            if (animalFrozen || animalAgent == null || !animalAgent.IsPresent) return;
            // Disabling the behaviour alone is insufficient: AnimalAgent directly calls its Tick methods.
            // IsPresent stays true after disabling this initialized clone, so production discovery/photo checks remain valid.
            animalAgent.Motor?.Stop();
            if (useFixedAnimalMapPosition && Animal != null)
            {
                Animal.transform.position = bootstrap.map.MapPositionToWorld(animalAnchor);
                Animal.GetComponent<HeightMapPlacedObject>()?.CaptureCurrentTransform();
            }
            animalAgent.SetPerceptionSuppressed(true);
            animalAgent.PlaceholderView?.RestoreVisibleAppearance();
            animalAgent.enabled = false;
            animalFrozen = true;
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
                        || !ValidMapPosition(candidate, outsideCircle)) continue;
                    mapPosition = candidate;
                    return true;
                }
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
                WorldInteractionKind.Collision | WorldInteractionKind.BodyCollision, map, gameObject.scene, ignore: ignore);
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
            Gizmos.color = new Color(.85f, .73f, .3f, 1f);
            Gizmos.DrawWireSphere(animal, radius);
            Gizmos.DrawWireSphere(observation, radius);
            Gizmos.DrawLine(animal, observation);
            UnityEditor.Handles.Label(animal, "Tutorial Muskrat (Water)");
            UnityEditor.Handles.Label(observation, "Dry Scan Approach");
        }
#endif
    }
}
