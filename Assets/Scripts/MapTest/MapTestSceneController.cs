using System.Collections.Generic;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace AnimalGame.MapTest
{
    [ExecuteAlways]
    [DefaultExecutionOrder(-1000)]
    public sealed class MapTestSceneController : MonoBehaviour
    {
        private const float DefaultSurfaceRevealRadiusPixels = 430f;
        private const float VisibleRangeSamplesPerSecond = 2f;

        [Header("Fixed Level Asset")]
        [Tooltip("Required tiled definition. Use a single-tile definition for standalone maps.")]
        [SerializeField] private TiledLevelAsset levelAsset;

        private float mapWidthMeters, mapHeightMeters, minimumHeightMeters, maximumHeightMeters;

        [Header("Visualization")]
        [Tooltip("Vertical elevation difference in meters between neighboring contour lines.")]
        [SerializeField, Min(1f)] private float contourIntervalMeters = 10f;

        [Header("Editor Preview")]
        [Tooltip("Lower physical bake resolution used only while editing. Runtime still uses the fixed level asset's full bake resolution.")]
        [SerializeField, Range(128, 1024)] private int editorHeightResolution = 512;

        [Tooltip("Direct reference to the contour shader used by standalone builds. Keep this assigned so Unity includes the shader instead of stripping a Shader.Find-only dependency.")]
        [SerializeField] private Shader dynamicContourShader;
        [SerializeField] private bool dashSubmergedContours;
        [SerializeField] private bool smoothContourEdges;

        [Tooltip("Opacity of the lowest contour currently visible in the camera.")]
        [SerializeField, Range(0f, 1f)] private float minimumContourOpacity = 0.15f;
        [Tooltip("Opacity of the highest contour currently visible in the camera.")]
        [SerializeField, Range(0f, 1f)] private float maximumContourOpacity = 1f;

        [Tooltip("Darkening outside the view ring: zero leaves the map unchanged; one makes it black.")]
        [SerializeField, Range(0f, 1f)] private float outsideRevealDarkening = 0.3f;

        [Header("Viewport Contours")]
        [Tooltip("Width of the lowest contour currently visible in the camera.")]
        [SerializeField, Range(0.1f, 10f)] private float minimumContourWidth = 0.75f;

        [Tooltip("Width of the highest contour currently visible in the camera.")]
        [SerializeField, Range(0.1f, 10f)] private float maximumContourWidth = 3f;

        [Tooltip("Maximum fraction of the gap between neighboring contours that one line may occupy.")]
        [SerializeField, Range(0.1f, 0.7f)] private float maximumContourCoverage = 0.45f;

        [Tooltip("Softness of contour edges in screen pixels. Lower values produce crisper lines.")]
        [SerializeField, Range(0.1f, 1.5f)] private float contourEdgeSoftness = 0.4f;

        [Tooltip("Total number of samples distributed around the viewport perimeter when finding the visible height range.")]
        [SerializeField, Range(4, 128)] private int viewportEdgeSamples = 32;

        [Tooltip("Number of interior grid samples on the viewport's vertical axis. The horizontal count scales with camera aspect ratio.")]
        [SerializeField, Range(2, 128)] private int viewportGridHeightSamples = 16;

        [Tooltip("Rate at which the displayed contour range approaches the latest sampled terrain range.")]
        [SerializeField, Min(0.01f)] private float visibleContourRangeSmoothingSpeed = 8f;

        [Header("Color Palette")]
        [Tooltip("Color outside the generated map.")]
        [SerializeField] private Color backgroundColor = new Color(0.008f, 0.012f, 0.017f);

        [Tooltip("Base color of all dynamic contour lines before height-dependent opacity is applied.")]
        [SerializeField] private Color contourColor = new Color(0.92f, 0.97f, 1f);

        private Camera mapCamera;
        private TiledMapRuntime runtimeMap;
        private readonly List<TiledMapTileView> tileViews = new List<TiledMapTileView>();
        private readonly List<SpriteRenderer> tileRenderers = new List<SpriteRenderer>();
        private Material contourMaterial;
        private float surfaceRevealEdgePixels = 4f;
        private ScanChargeUI surfaceRevealUi;
        private int lastViewportUpdateFrame = -1;
        private float nextVisibleRangeSampleTime;
        private float targetVisibleMinimumHeight;
        private float targetVisibleMaximumHeight;
        private bool hasVisibleRangeTarget;
        private int editorConfigurationHash = int.MinValue;
        private bool rebuildingMap;
        private bool generatedForPlayMode;

        public float VisibleMinimumContourHeight { get; private set; }
        public float VisibleMaximumContourHeight { get; private set; }
        public Vector2 MapSizeMeters => new Vector2(mapWidthMeters, mapHeightMeters);
        public TiledLevelAsset LevelAsset => levelAsset;
        public HeightMapLevelAsset Presentation => levelAsset != null ? levelAsset.Presentation : null;
        public TiledMapRuntime RuntimeMap => runtimeMap;
        public float MinimumHeightMeters => minimumHeightMeters;
        public Vector2 SampleSpacing(Vector2 position) => runtimeMap?.SampleSpacing(position) ?? Vector2.zero;
        public float ContourIntervalMeters => contourIntervalMeters;
        public Color BackgroundColor => backgroundColor;
        public bool HasGeneratedMap => runtimeMap != null && tileViews.Count == runtimeMap.TileCount && tileViews.Count > 0;
        public bool HasPendingHeightLoads => runtimeMap != null && runtimeMap.HasPendingLoads;
#if UNITY_EDITOR
        public IReadOnlyList<SpriteRenderer> EditorTileRenderers => tileRenderers;
        public static event System.Action EditorTilesChanged;
#endif

        internal IReadOnlyList<SpriteRenderer> PhotoContourRenderers => tileRenderers;

        internal void GetPhotoSnapshotCircle(out Vector2 centerPixels, out float radiusPixels)
        {
            centerPixels = surfaceRevealUi != null
                ? surfaceRevealUi.GetUiCenterScreenPoint()
                : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            radiusPixels = surfaceRevealUi != null
                ? surfaceRevealUi.GetUiRingScreenRadiusPixels()
                : DefaultSurfaceRevealRadiusPixels;
        }

        public Bounds WorldBounds
        {
            get
            {
                if (!HasGeneratedMap) return new Bounds(transform.position, Vector3.zero);
                Vector2 size = Vector2.Scale(MapSizeMeters, levelAsset.WorldUnitsPerMeter);
                Vector3 scale = transform.lossyScale;
                return new Bounds(transform.position, new Vector3(size.x * scale.x, size.y * scale.y, 0));
            }
        }

        private void Awake()
        {
#if !UNITY_EDITOR
            if (Application.isPlaying)
                Cursor.visible = false;
#endif

            RebuildGeneratedMap();
        }

        private void OnEnable()
        {
            if (Application.isPlaying)
            {
                if (!HasGeneratedMap || !generatedForPlayMode)
                    RebuildGeneratedMap();
                Camera.onPreCull += HandleCameraPreCull;
                RenderPipelineManager.beginCameraRendering +=
                    HandleBeginCameraRendering;
            }
            else if (!HasGeneratedMap || generatedForPlayMode)
            {
                RebuildGeneratedMap();
            }
        }

        private void OnDisable()
        {
            Camera.onPreCull -= HandleCameraPreCull;
            RenderPipelineManager.beginCameraRendering -=
                HandleBeginCameraRendering;
        }

        private void Update()
        {
            if (Application.isPlaying)
            {
                if (runtimeMap != null && runtimeMap.CompletePendingLoads())
                {
                    var properties = new MaterialPropertyBlock();
                    foreach (var view in tileViews) view.RefreshNewlyLoaded(properties);
                    nextVisibleRangeSampleTime = 0;
                }
                return;
            }
            if (rebuildingMap)
                return;

            RefreshContourMaterialSettings();
            RefreshEditorSurfaceIfChanged();
            int configurationHash = CalculateEditorConfigurationHash();
            if (generatedForPlayMode
                || configurationHash != editorConfigurationHash)
            {
                RebuildGeneratedMap();
            }
        }

        public float SampleHeight(Vector2 uv) => SampleHeightChannel(uv, 0);
        public float SampleDetailHeight(Vector2 uv) => SampleHeightChannel(uv, 1);
        public float SampleRawDetailHeight(Vector2 uv) => SampleHeightChannel(uv, 2);

        private float SampleHeightChannel(Vector2 uv, int channel)
        {
            Vector2 position = Vector2.Scale(uv, MapSizeMeters);
            if (TrySample(position, out float height, channel)) return height;
            if (HasPendingHeightLoads && runtimeMap.TryLocate(position, out var tile, out _) && tile.Field == null)
                return 0f;
            return minimumHeightMeters;
        }
        public bool TrySampleWorldPosition(Vector2 worldPosition, out Vector2 mapPositionMeters, out float heightMeters)
        {
            mapPositionMeters = Vector2.zero;
            heightMeters = 0;
            if (!HasGeneratedMap) return false;
            Bounds bounds = WorldBounds;
            if (worldPosition.x < bounds.min.x || worldPosition.y < bounds.min.y
                || worldPosition.x > bounds.max.x || worldPosition.y > bounds.max.y) return false;
            mapPositionMeters = new Vector2((worldPosition.x - bounds.min.x) / bounds.size.x * mapWidthMeters,
                (worldPosition.y - bounds.min.y) / bounds.size.y * mapHeightMeters);
            return TrySampleMapPosition(mapPositionMeters, out heightMeters);
        }
        /// <summary>Samples geographic coordinates and ground elevation at a world XY position.</summary>
        public bool TrySampleGeographicWorldPosition(Vector2 worldPosition,
            out float latitude, out float longitude, out float heightMeters)
        {
            latitude = longitude = 0;
            if (!TrySampleWorldPosition(worldPosition, out Vector2 position, out heightMeters))
                return false;
            latitude = Mathf.Lerp(levelAsset.LatitudeRange.x, levelAsset.LatitudeRange.y,
                position.y / mapHeightMeters);
            longitude = Mathf.Lerp(levelAsset.LongitudeRange.x, levelAsset.LongitudeRange.y,
                position.x / mapWidthMeters);
            return true;
        }

        public bool TryGeographicToWorldPosition(Vector2 longitudeLatitude, out Vector3 worldPosition)
        {
            worldPosition = default;
            if (!HasGeneratedMap) return false;
            Vector2 longitude = levelAsset.LongitudeRange, latitude = levelAsset.LatitudeRange;
            if (longitudeLatitude.x < longitude.x || longitudeLatitude.x > longitude.y
                || longitudeLatitude.y < latitude.x || longitudeLatitude.y > latitude.y) return false;
            worldPosition = MapPositionToWorld(new Vector2(
                Mathf.InverseLerp(longitude.x, longitude.y, longitudeLatitude.x) * mapWidthMeters,
                Mathf.InverseLerp(latitude.x, latitude.y, longitudeLatitude.y) * mapHeightMeters));
            return true;
        }

        public bool TrySampleMapPosition(Vector2 position, out float height) => TrySample(position, out height, 0);
        public bool TrySampleDetailMapPosition(Vector2 position, out float height) => TrySample(position, out height, 1);
        public bool TrySampleRawDetailMapPosition(Vector2 position, out float height) => TrySample(position, out height, 2);
        private bool TrySample(Vector2 position, out float height, int channel)
        {
            height = 0;
            return runtimeMap != null && runtimeMap.TrySample(position, out height, channel);
        }
        public bool TrySampleStaticWaterWorldPosition(Vector2 position, out float depth)
        {
            depth = 0;
            return TrySampleWorldPosition(position, out var mapPosition, out _)
                && TrySampleStaticWaterMapPosition(mapPosition, out depth);
        }
        public bool TrySampleStaticWaterMapPosition(Vector2 position, out float depth)
        {
            depth = 0;
            return runtimeMap != null && runtimeMap.TrySampleWater(position, out depth);
        }
        public bool TrySampleStaticWaterFlowWorldPosition(Vector2 position, out Vector2 worldFlow)
        {
            worldFlow = Vector2.zero;
            if (!TrySampleWorldPosition(position, out var mapPosition, out _)
                || !runtimeMap.TrySampleWaterFlow(mapPosition, out var mapFlow))
                return false;
            worldFlow = MapDirectionToWorldDirection(mapFlow) * mapFlow.magnitude;
            return true;
        }
        public bool ContainsLevel(HeightMapLevelAsset level) => levelAsset != null && levelAsset.Contains(level);
        public bool TryGetTileOrigin(HeightMapLevelAsset level, out Vector2 origin)
        {
            origin = default;
            if (levelAsset == null) return false;
            foreach (var entry in levelAsset.Tiles)
                if (entry.level == level) { origin = Vector2.Scale(entry.coordinate, levelAsset.TileSizeMeters); return true; }
            return false;
        }

        public Vector3 MapPositionToWorld(Vector2 mapPositionMeters)
        {
            if (!HasGeneratedMap)
                return transform.position;

            Vector2 clampedMapPosition = new Vector2(
                Mathf.Clamp(mapPositionMeters.x, 0f, mapWidthMeters),
                Mathf.Clamp(mapPositionMeters.y, 0f, mapHeightMeters));
            Vector2 uv = new Vector2(
                clampedMapPosition.x / Mathf.Max(0.0001f, mapWidthMeters),
                clampedMapPosition.y / Mathf.Max(0.0001f, mapHeightMeters));
            Bounds bounds = WorldBounds;
            return new Vector3(
                Mathf.Lerp(bounds.min.x, bounds.max.x, uv.x),
                Mathf.Lerp(bounds.min.y, bounds.max.y, uv.y),
                bounds.center.z);
        }

        public float MapMetersToWorldDistance(Vector2 worldDirection, float distanceMeters)
        {
            if (!HasGeneratedMap || worldDirection.sqrMagnitude < 0.000001f)
                return 0f;

            Bounds bounds = WorldBounds;
            Vector2 direction = worldDirection.normalized;
            float mapMetersPerWorldUnit = Mathf.Sqrt(
                Mathf.Pow(direction.x * mapWidthMeters / Mathf.Max(0.0001f, bounds.size.x), 2f)
                + Mathf.Pow(direction.y * mapHeightMeters / Mathf.Max(0.0001f, bounds.size.y), 2f));
            return mapMetersPerWorldUnit > 0.0001f
                ? distanceMeters / mapMetersPerWorldUnit
                : 0f;
        }

        public Vector2 WorldDirectionToMapDirection(Vector2 worldDirection)
        {
            if (!HasGeneratedMap || worldDirection.sqrMagnitude < 0.000001f)
                return Vector2.zero;

            Bounds bounds = WorldBounds;
            Vector2 mapDirection = new Vector2(
                worldDirection.x * mapWidthMeters / Mathf.Max(0.0001f, bounds.size.x),
                worldDirection.y * mapHeightMeters / Mathf.Max(0.0001f, bounds.size.y));
            return mapDirection.normalized;
        }

        public Vector2 MapDirectionToWorldDirection(Vector2 mapDirection)
        {
            if (!HasGeneratedMap || mapDirection.sqrMagnitude < 0.000001f)
                return Vector2.zero;

            Bounds bounds = WorldBounds;
            Vector2 worldDirection = new Vector2(
                mapDirection.x * bounds.size.x / Mathf.Max(0.0001f, mapWidthMeters),
                mapDirection.y * bounds.size.y / Mathf.Max(0.0001f, mapHeightMeters));
            return worldDirection.normalized;
        }

        public void UseCamera(Camera cameraToUse)
        {
            if (cameraToUse == null)
                return;

            mapCamera = cameraToUse;
            mapCamera.backgroundColor = backgroundColor;
            UpdateVisibleContourRange(mapCamera);
            RefreshSurfaceMaterialSettings();
        }

        public void UseSurfaceRevealUi(ScanChargeUI scanUi)
        {
            surfaceRevealUi = scanUi;
            RefreshSurfaceMaterialSettings();
        }

        private void RebuildGeneratedMap()
        {
            if (rebuildingMap) return;
            rebuildingMap = true;
            try
            {
                for (int i = transform.childCount - 1; i >= 0; i--)
                    DestroyGeneratedObject(transform.GetChild(i).gameObject);
                ReleaseGeneratedMap();
                hasVisibleRangeTarget = false;
                nextVisibleRangeSampleTime = 0;
                if (levelAsset == null) return;
                if (!levelAsset.Validate(out string error)) throw new System.InvalidOperationException(error);
                if (Quaternion.Angle(transform.rotation, Quaternion.identity) > 0.001f
                    || transform.lossyScale.x <= 0 || transform.lossyScale.y <= 0)
                    throw new System.InvalidOperationException("Tiled maps require positive scale and zero rotation.");
                ApplyFixedLevelAsset();
                runtimeMap = new TiledMapRuntime(levelAsset, Application.isPlaying,
                    Application.isPlaying ? int.MaxValue : editorHeightResolution,
                    Application.isPlaying ? FindPlayerSpawn() : null);
                CreateDynamicContourMaterial();
                foreach (var tile in runtimeMap.Tiles)
                {
                    var view = new TiledMapTileView(tile);
                    view.Build(transform, levelAsset, contourMaterial);
                    tileViews.Add(view);
                    tileRenderers.Add(view.Renderer);
                }
                if (Application.isPlaying)
                {
                    UpdateVisibleContourRange(mapCamera);
                }
                else ShowCompleteContourRange();
                RefreshSurfaceMaterialSettings();
                RefreshTileProperties();
                generatedForPlayMode = Application.isPlaying;
                editorConfigurationHash = CalculateEditorConfigurationHash();
            }
            catch (System.Exception exception)
            {
                ReleaseGeneratedMap();
                editorConfigurationHash = CalculateEditorConfigurationHash();
                Debug.LogException(exception, this);
            }
            finally
            {
                rebuildingMap = false;
                WorldInteractionQuery.InvalidateSpatialIndex();
#if UNITY_EDITOR
                EditorTilesChanged?.Invoke();
#endif
            }
        }

        private Vector2 FindPlayerSpawn()
        {
            foreach (var root in gameObject.scene.GetRootGameObjects())
            {
                var bootstrap = root.GetComponentInChildren<HeightMapPlayerSceneBootstrap>();
                if (bootstrap != null) return bootstrap.PlayerSpawnMapPositionMeters;
            }
            return Vector2.Scale(levelAsset.Tiles[0].coordinate, levelAsset.TileSizeMeters);
        }

        private void ApplyFixedLevelAsset()
        {
            if (Presentation == null || !Presentation.IsValid)
                return;

            mapWidthMeters = levelAsset.MapSizeMeters.x;
            mapHeightMeters = levelAsset.MapSizeMeters.y;
            minimumHeightMeters = Presentation.MinimumHeightMeters;
            maximumHeightMeters = Presentation.MaximumHeightMeters;
            contourIntervalMeters = Presentation.ContourIntervalMeters;
            dynamicContourShader = Presentation.DynamicContourShader;
            minimumContourWidth = Presentation.MinimumContourWidth;
            maximumContourWidth = Presentation.MaximumContourWidth;
            maximumContourCoverage = Presentation.MaximumContourCoverage;
            contourEdgeSoftness = Presentation.ContourEdgeSoftness;
            viewportEdgeSamples = Presentation.ViewportEdgeSamples;
            viewportGridHeightSamples = Presentation.ViewportGridHeightSamples;
            surfaceRevealEdgePixels = Presentation.SurfaceRevealEdgePixels;
            backgroundColor = Presentation.BackgroundColor;
            contourColor = Presentation.ContourColor;
            ApplyLevelPresentationSettings();
        }

        private void ApplyLevelPresentationSettings()
        {
            if (Presentation == null || !Presentation.IsValid)
                return;

            contourIntervalMeters = Presentation.ContourIntervalMeters;
            minimumContourWidth = Presentation.MinimumContourWidth;
            maximumContourWidth = Presentation.MaximumContourWidth;
            maximumContourCoverage = Presentation.MaximumContourCoverage;
            contourEdgeSoftness = Presentation.ContourEdgeSoftness;
            contourColor = Presentation.ContourColor;
            surfaceRevealEdgePixels = Presentation.SurfaceRevealEdgePixels;
            minimumContourOpacity = Presentation.MinimumContourOpacity;
            maximumContourOpacity = Presentation.MaximumContourOpacity;
            outsideRevealDarkening = Presentation.OutsideRevealDarkening;
        }

        private int CalculateEditorConfigurationHash()
        {
            unchecked
            {
                int hash = levelAsset != null
                    ? levelAsset.ConfigurationHash
                    : 0;
                hash = hash * 397 ^ editorHeightResolution;
                hash = hash * 397 ^ transform.position.GetHashCode();
                hash = hash * 397 ^ transform.rotation.GetHashCode();
                hash = hash * 397 ^ transform.lossyScale.GetHashCode();
                return hash;
            }
        }

        private void RefreshEditorSurfaceIfChanged()
        {
            RefreshSurfaceMaterialSettings();
            RefreshTileProperties();
        }








        private void CreateDynamicContourMaterial()
        {
            Shader shader = dynamicContourShader != null ? dynamicContourShader : Shader.Find("AnimalGame/Dynamic Height Contours");
            if (shader == null) throw new System.InvalidOperationException("Missing dynamic contour shader.");
            contourMaterial = new Material(shader) { name = "Shared Map Presentation", hideFlags = HideFlags.DontSave };
            RefreshContourMaterialSettings();
        }
        private void RefreshTileProperties()
        {
            if (contourMaterial == null) return;
            var properties = new MaterialPropertyBlock();
            foreach (var view in tileViews) view.RefreshProperties(properties);
        }


        private void ShowCompleteContourRange()
        {
            if (contourMaterial == null)
                return;

            VisibleMinimumContourHeight = minimumHeightMeters;
            VisibleMaximumContourHeight = maximumHeightMeters;
            contourMaterial.SetFloat(
                "_VisibleMinimumHeight",
                VisibleMinimumContourHeight);
            contourMaterial.SetFloat(
                "_VisibleMaximumHeight",
                VisibleMaximumContourHeight);
        }

        private void HandleCameraPreCull(Camera cameraToRender)
        {
            UpdateVisibleRangeOncePerFrame(cameraToRender);
        }

        private void HandleBeginCameraRendering(
            ScriptableRenderContext context,
            Camera cameraToRender)
        {
            UpdateVisibleRangeOncePerFrame(cameraToRender);
        }

        private void UpdateVisibleRangeOncePerFrame(Camera cameraToRender)
        {
            if (cameraToRender != mapCamera || lastViewportUpdateFrame == Time.frameCount)
                return;

            lastViewportUpdateFrame = Time.frameCount;
            UpdateVisibleContourRange(cameraToRender);
            RefreshSurfaceMaterialSettings();
        }

        private void UpdateVisibleContourRange(Camera cameraToSample)
        {
            if (cameraToSample == null || contourMaterial == null || !HasGeneratedMap)
                return;

            float now = Time.unscaledTime;
            if (!hasVisibleRangeTarget || now >= nextVisibleRangeSampleTime)
            {
                nextVisibleRangeSampleTime = now + 1f / VisibleRangeSamplesPerSecond;
                if (TryFindVisibleTerrainRange(
                        cameraToSample,
                        out float minimumTerrain,
                        out float maximumTerrain))
                {
                    targetVisibleMinimumHeight = minimumTerrain;
                    targetVisibleMaximumHeight = maximumTerrain;
                    if (!hasVisibleRangeTarget)
                    {
                        VisibleMinimumContourHeight = minimumTerrain;
                        VisibleMaximumContourHeight = maximumTerrain;
                        hasVisibleRangeTarget = true;
                    }
                }
            }

            if (!hasVisibleRangeTarget)
                return;

            float blend = 1f - Mathf.Exp(
                -Mathf.Max(0.01f, visibleContourRangeSmoothingSpeed)
                * Time.unscaledDeltaTime);
            VisibleMinimumContourHeight = Mathf.Lerp(
                VisibleMinimumContourHeight,
                targetVisibleMinimumHeight,
                blend);
            VisibleMaximumContourHeight = Mathf.Lerp(
                VisibleMaximumContourHeight,
                targetVisibleMaximumHeight,
                blend);

            contourMaterial.SetFloat("_VisibleMinimumHeight", VisibleMinimumContourHeight);
            contourMaterial.SetFloat("_VisibleMaximumHeight", VisibleMaximumContourHeight);
        }

        private bool TryFindVisibleTerrainRange(
            Camera cameraToSample,
            out float minimumTerrain,
            out float maximumTerrain)
        {
            minimumTerrain = float.PositiveInfinity;
            maximumTerrain = float.NegativeInfinity;
            Bounds bounds = WorldBounds;
            int edgeSamples = Mathf.Max(4, viewportEdgeSamples);
            int verticalSamples = Mathf.Max(2, viewportGridHeightSamples);
            int horizontalSamples = Mathf.Max(
                2,
                Mathf.CeilToInt(verticalSamples * Mathf.Max(0.1f, cameraToSample.aspect)));
            int validSamples = 0;

            for (int i = 0; i < edgeSamples; i++)
            {
                float perimeterPosition = i * 4f / edgeSamples;
                int edge = Mathf.FloorToInt(perimeterPosition);
                float edgeProgress = perimeterPosition - edge;
                Vector2 viewportPoint;
                switch (edge)
                {
                    case 0:
                        viewportPoint = new Vector2(edgeProgress, 0f);
                        break;
                    case 1:
                        viewportPoint = new Vector2(1f, edgeProgress);
                        break;
                    case 2:
                        viewportPoint = new Vector2(1f - edgeProgress, 1f);
                        break;
                    default:
                        viewportPoint = new Vector2(0f, 1f - edgeProgress);
                        break;
                }

                TryAccumulateVisibleHeight(
                    cameraToSample,
                    viewportPoint,
                    bounds,
                    ref minimumTerrain,
                    ref maximumTerrain,
                    ref validSamples);
            }

            for (int y = 0; y < verticalSamples; y++)
            {
                float viewportY = (y + 0.5f) / verticalSamples;
                for (int x = 0; x < horizontalSamples; x++)
                {
                    float viewportX = (x + 0.5f) / horizontalSamples;
                    TryAccumulateVisibleHeight(
                        cameraToSample,
                        new Vector2(viewportX, viewportY),
                        bounds,
                        ref minimumTerrain,
                        ref maximumTerrain,
                        ref validSamples);
                }
            }

            return validSamples > 0;
        }

        private void TryAccumulateVisibleHeight(
            Camera cameraToSample,
            Vector2 viewportPoint,
            Bounds bounds,
            ref float minimumTerrain,
            ref float maximumTerrain,
            ref int validSamples)
        {
            if (!TryProjectViewportPointToMapPlane(
                    cameraToSample,
                    viewportPoint,
                    WorldBounds.center.z,
                    out Vector3 world)
                || world.x < bounds.min.x
                || world.x > bounds.max.x
                || world.y < bounds.min.y
                || world.y > bounds.max.y)
            {
                return;
            }

            if (!TrySampleWorldPosition(world, out _, out float height)) return;
            minimumTerrain = Mathf.Min(minimumTerrain, height);
            maximumTerrain = Mathf.Max(maximumTerrain, height);
            validSamples++;
        }

        private static bool TryProjectViewportPointToMapPlane(
            Camera cameraToSample,
            Vector2 viewportPoint,
            float mapPlaneZ,
            out Vector3 worldPoint)
        {
            Ray ray = cameraToSample.ViewportPointToRay(
                new Vector3(viewportPoint.x, viewportPoint.y, 0f));
            return TryProjectRayToMapPlane(ray, mapPlaneZ, out worldPoint);
        }

        private static bool TryProjectRayToMapPlane(
            Ray ray,
            float mapPlaneZ,
            out Vector3 worldPoint)
        {
            float directionAlongPlaneNormal = ray.direction.z;
            if (Mathf.Abs(directionAlongPlaneNormal) < 0.000001f)
            {
                worldPoint = default;
                return false;
            }

            float distance = (mapPlaneZ - ray.origin.z)
                             / directionAlongPlaneNormal;
            if (distance < 0f)
            {
                worldPoint = default;
                return false;
            }

            worldPoint = ray.GetPoint(distance);
            return true;
        }

        private void RefreshContourMaterialSettings()
        {
            if (contourMaterial == null)
                return;

            ApplyLevelPresentationSettings();
            contourMaterial.SetFloat("_ContourInterval", contourIntervalMeters);
            contourMaterial.SetColor("_ContourColor", contourColor);
            contourMaterial.SetFloat("_DashSubmergedContours", dashSubmergedContours ? 1f : 0f);
            contourMaterial.SetFloat("_SmoothContourEdges", smoothContourEdges ? 1f : 0f);
            contourMaterial.SetFloat("_OutsideRevealDarkening", Mathf.Clamp01(outsideRevealDarkening));
            contourMaterial.SetFloat("_MinimumLineWidth", minimumContourWidth);
            contourMaterial.SetFloat("_MaximumLineWidth", maximumContourWidth);
            contourMaterial.SetFloat("_MaximumCoverage", maximumContourCoverage);
            contourMaterial.SetFloat("_EdgeSoftness", contourEdgeSoftness);
            contourMaterial.SetFloat("_MinimumOpacity", minimumContourOpacity);
            contourMaterial.SetFloat("_MaximumOpacity", maximumContourOpacity);
            contourMaterial.SetFloat("_SurfaceRevealEnabled", Application.isPlaying ? 1 : 0);
            contourMaterial.SetFloat("_SurfaceRevealEdgePixels", surfaceRevealEdgePixels);
        }

        private void RefreshSurfaceMaterialSettings()
        {
            if (contourMaterial == null) return;
            Vector2 center = surfaceRevealUi != null ? surfaceRevealUi.GetUiCenterScreenPoint()
                : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            float radius = surfaceRevealUi != null ? surfaceRevealUi.GetUiRingScreenRadiusPixels() : DefaultSurfaceRevealRadiusPixels;
            contourMaterial.SetVector("_SurfaceRevealCenterPixels", new Vector4(center.x, center.y, 0, 0));
            contourMaterial.SetFloat("_SurfaceRevealRadiusPixels", Mathf.Max(1, radius));
        }


        private void OnDestroy()
        {
            ReleaseGeneratedMap();
        }

        private void ReleaseGeneratedMap()
        {
            WorldInteractionQuery.InvalidateSpatialIndex();
            foreach (var view in tileViews) view.Dispose();
            tileViews.Clear();
            tileRenderers.Clear();
            runtimeMap?.Dispose();
            runtimeMap = null;
            if (contourMaterial != null) DestroyGeneratedObject(contourMaterial);
            contourMaterial = null;
        }


        private static void DestroyGeneratedObject(Object generatedObject)
        {
            if (generatedObject == null)
                return;

            if (Application.isPlaying)
                Destroy(generatedObject);
            else
                DestroyImmediate(generatedObject);
        }

        private void OnValidate()
        {
            if (Application.isPlaying)
                return;

            ApplyFixedLevelAsset();
            mapWidthMeters = Mathf.Max(1f, mapWidthMeters);
            mapHeightMeters = Mathf.Max(1f, mapHeightMeters);
            maximumHeightMeters = Mathf.Max(minimumHeightMeters + 0.01f, maximumHeightMeters);
            contourIntervalMeters = Mathf.Max(1f, contourIntervalMeters);
            minimumContourOpacity = Mathf.Clamp01(minimumContourOpacity);
            maximumContourOpacity = Mathf.Clamp01(maximumContourOpacity);
            outsideRevealDarkening = Mathf.Clamp01(outsideRevealDarkening);
            editorHeightResolution = Mathf.Clamp(
                editorHeightResolution,
                128,
                1024);
            editorConfigurationHash = int.MinValue;
        }
    }
}
