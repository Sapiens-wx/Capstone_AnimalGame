using System;
using System.Collections;
using System.Reflection;
using AnimalGame.Demos.WasteRobot;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    /// <summary>Behavior checks for visible lowland sampling and the real camera compositor.</summary>
    public static class CameraElevationZoomRegressionChecks
    {
        private static int passedChecks;

        public static void Run()
        {
            UnityEngine.Random.State randomState = UnityEngine.Random.state;
            try
            {
                passedChecks = 0;
                Check("Flat ground and isolated pits do not behave like broad lowland", CheckAreaSupport);
                Check("Absolute elevation does not affect relative zoom", CheckHeightTranslation);
                Check("Equal drops become weaker with distance", CheckDistanceFalloff);
                Check("Rotated visible rectangles exclude their world AABB corners", CheckRotatedViewport);
                Check("Resized and translated viewports include newly visible ground", CheckViewportChanges);
                Check("Missing samples contribute no invented low ground", CheckInvalidSamples);
                Check("Real camera size, aspect, rotation and offset determine the sampled region", CheckCameraProjection);
                Check("Live viewport feedback stays bounded and returns after leaving lowland", CheckViewportFeedback);
                Check("Smoothing is continuous, bounded and frozen on paused ticks", CheckSmoothing);
                Check("Height composes with scan, photo and impact and cleans up independently", CheckCompositionAndDisable);
                Check("Paused composition applies pending height changes and disable cleanup", CheckPausedComposition);
                Check("Fixed interior size follows a changing environmental base", CheckInteriorComposition);
                Debug.Log("Camera elevation zoom checks PASS: " + passedChecks
                    + " groups; lowland area, relative elevation, distance, viewport geometry, invalid samples,"
                    + " real camera projection, feedback, frame-rate smoothing, pause, composition, disable and interior zoom.");
            }
            finally { UnityEngine.Random.state = randomState; }
        }

        public static void RunBatch()
        {
            try
            {
                Run();
                ScanCameraZoomRegressionChecks.Run();
                DiscoveryScanObservationRegressionChecks.Run();
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void CheckAreaSupport()
        {
            using (var f = new CameraFixture())
            {
                float flat = f.Evaluate(new HeightSource(p => 40f));
                Require(Near(flat, 1f) && Near(f.Zoom.LowlandScore, 0f), "Flat ground changed the view.");
                float pit = f.Evaluate(new HeightSource(p => p.sqrMagnitude < 2f ? -1000f : 40f));
                float broad = f.Evaluate(new HeightSource(p => p.x > 0f ? 0f : 40f));
                Require(pit < 1.02f, "A tiny deep pit dominated the whole viewport.");
                Require(broad > 1.4f && broad > pit + .3f, "A broad drop did not outweigh the isolated pit.");
                Require(Near(f.Evaluate(new HeightSource(p => 42f)), 1f), "Higher surrounding ground zoomed out.");
                Require(Near(f.Evaluate(new HeightSource(p => 38f)), 1f), "A sub-threshold drop zoomed out.");
            }
        }

        private static void CheckHeightTranslation()
        {
            using (var f = new CameraFixture())
            {
                var source = new HeightSource(p => p.x > 0f ? 5f : 40f);
                float original = f.Evaluate(source);
                float raised = f.Evaluate(new HeightSource(p => (p.x > 0f ? 5f : 40f) + 700f), 740f);
                Require(original > 1.1f && Near(original, raised), "Raising the entire landscape changed the result.");
            }
        }

        private static void CheckDistanceFalloff()
        {
            using (var f = new CameraFixture())
            {
                var low = new HeightSource(p => 0f);
                float near = f.Evaluate(low);
                float far = f.Evaluate(low, 40f, new Vector2(100, -10), new Vector2(120, -10), new Vector2(100, 10));
                Require(near > far + .25f && far > 1f, "Distance did not weaken an otherwise equal drop.");
            }
        }

        private static void CheckRotatedViewport()
        {
            using (var f = new CameraFixture())
            {
                float extent = Mathf.Sqrt(200f);
                Vector2 bottom = new Vector2(0, -extent), right = new Vector2(extent, 0), left = new Vector2(-extent, 0);
                var corner = new HeightSource(p => p.x > 8f && p.y > 8f ? 0f : 40f);
                float diamond = f.Evaluate(corner, 40f, bottom, right, left);
                float aabb = f.Evaluate(corner, 40f, Vector2.one * -extent,
                    new Vector2(extent, -extent), new Vector2(-extent, extent));
                Require(Near(diamond, 1f) && aabb > 1.005f, "A rotated viewport included lowland outside its real corners.");
                var visible = new HeightSource(p => p.x > 8f && Mathf.Abs(p.y) < 2f ? 0f : 40f);
                Require(f.Evaluate(visible, 40f, bottom, right, left) > 1.005f,
                    "A visible lowland near a rotated corner was discarded by a circular cutoff.");
            }
        }

        private static void CheckViewportChanges()
        {
            using (var f = new CameraFixture())
            {
                var source = new HeightSource(p => p.x > 15f ? 0f : 40f);
                Require(Near(f.Evaluate(source), 1f), "An off-screen lowland affected the initial view.");
                float expanded = f.Evaluate(source, 40f, new Vector2(-25, -10), new Vector2(25, -10), new Vector2(-25, 10));
                float moved = f.Evaluate(source, 40f, new Vector2(20, -10), new Vector2(40, -10), new Vector2(20, 10));
                Require(expanded > 1.1f && moved > expanded, "The viewport retained an old search radius or center.");
            }
        }

        private static void CheckInvalidSamples()
        {
            using (var f = new CameraFixture())
            {
                f.Evaluate(new HeightSource(p => 0f));
                var edge = new HeightSource(p => 40f, p => p.x <= 0f);
                Require(Near(f.Evaluate(edge), 1f) && f.Zoom.ValidSampleCount == 288,
                    "Missing half of the map was counted as zero-height ground or stale data.");
                Require(Near(f.Evaluate(new HeightSource(p => 0f, p => false)), 1f)
                    && f.Zoom.ValidSampleCount == 0, "An entirely invalid viewport invented low ground.");
            }
        }

        private static void CheckCameraProjection()
        {
            using (var f = new CameraFixture())
            using (var terrain = new TerrainFixture(f))
            {
                f.Zoom.Initialize(terrain.Map, f.Player);
                terrain.PlacePlayer(new Vector2(100, 100));
                f.Camera.orthographicSize = 8f;
                f.Camera.aspect = 1f;
                Require(Near(f.Sample(), 1f), "Screen-external lowland affected a translated, nonuniformly scaled map.");
                f.Camera.aspect = 2f;
                Require(f.Sample() > 1.1f && f.Zoom.ValidSampleCount == 24 * 48,
                    "Increasing the camera aspect did not include the newly visible lowland.");
                f.Camera.transform.rotation = Quaternion.Euler(0, 0, 90);
                Require(Near(f.Sample(), 1f), "Rotating the real camera did not rotate its sampling rectangle.");
                f.Camera.transform.rotation = Quaternion.identity;
                f.Camera.aspect = 1f;
                f.Camera.orthographicSize = 25f;
                Require(f.Sample() > 1.3f, "Increasing current Size failed to enlarge the sampled region.");
                f.Camera.orthographicSize = 5f;
                f.Camera.transform.position += Vector3.right * 15f;
                Require(f.Sample() > 1.3f, "Sampling remained centered on the player instead of the camera.");
            }
        }

        private static void CheckViewportFeedback()
        {
            using (var f = new CameraFixture())
            using (var terrain = new TerrainFixture(f))
            {
                terrain.PlacePlayer(new Vector2(100, 100));
                f.Camera.transform.position += Vector3.right * 15f;
                f.Zoom.Initialize(terrain.Map, f.Player);
                for (int frame = 0; frame < 360; frame++)
                {
                    f.Tick(1f / 60f);
                    f.Apply();
                    Require(f.Zoom.CurrentMultiplier >= 1f && f.Zoom.CurrentMultiplier <= 1.5f,
                        "Viewport-dependent feedback exceeded the authored height limit.");
                }
                Require(f.Zoom.CurrentMultiplier > 1.2f, "Live lowland did not widen the camera.");
                terrain.PlacePlayer(new Vector2(40, 100));
                for (int frame = 0; frame < 480; frame++) { f.Tick(1f / 60f); f.Apply(); }
                Require(Near(f.Zoom.CurrentMultiplier, 1f), "Leaving the lowland retained a stale widened view.");
            }
        }

        private static void CheckSmoothing()
        {
            foreach (float delta in new[] { 1f / 15f, 1f / 60f, 1f / 120f })
            using (var f = new CameraFixture())
            {
                GameObject mapRoot = f.Create("Inactive smoothing map");
                mapRoot.SetActive(false);
                Set(f.Zoom, "map", mapRoot.AddComponent<MapTestSceneController>());
                Set(f.Zoom, "player", f.Player);
                Set(f.Zoom, "sampleTimer", 1000f);
                Set(f.Zoom, "<TargetMultiplier>k__BackingField", 1.5f);
                float previous = f.Zoom.CurrentMultiplier;
                for (int i = 0; i < Mathf.CeilToInt(4f / delta); i++)
                {
                    f.Tick(delta);
                    Require(f.Zoom.CurrentMultiplier >= previous && f.Zoom.CurrentMultiplier <= 1.5f,
                        "Outward smoothing reversed or overshot.");
                    Require(f.Zoom.CurrentMultiplier - previous < .06f, "Outward smoothing jumped in one frame.");
                    previous = f.Zoom.CurrentMultiplier;
                }
                Require(previous > 1.499f, "Outward smoothing did not converge.");
                Set(f.Zoom, "<TargetMultiplier>k__BackingField", 1f);
                f.Tick(0f);
                Require(Near(f.Zoom.CurrentMultiplier, previous), "A paused tick moved the camera.");
                for (int i = 0; i < Mathf.CeilToInt(8f / delta); i++)
                {
                    f.Tick(delta);
                    Require(f.Zoom.CurrentMultiplier <= previous && f.Zoom.CurrentMultiplier >= 1f,
                        "Inward smoothing reversed or overshot.");
                    previous = f.Zoom.CurrentMultiplier;
                }
                Require(Near(previous, 1f), "Inward smoothing did not return to the base view.");
            }
        }

        private static void CheckCompositionAndDisable()
        {
            using (var f = new CameraFixture())
            {
                f.Shake.SetElevationZoomMultiplier(1.4f);
                f.Shake.SetScanZoomMultiplier(1.2f);
                f.Shake.SetPhotoFocusMagnification(2f);
                Set(f.Shake, "springZoom", .02f);
                f.Apply();
                Require(Near(f.Camera.orthographicSize, 11f * 1.4f * 1.2f / 2f * 1.02f),
                    "Height overwrote scan, photo or the impact spring.");
                Require(Near(f.Shake.BaseOrthographicSize, 11f)
                    && Near(f.Shake.ElevationAdjustedOrthographicSize, 15.4f), "Height corrupted the authored base.");
                Call(f.Zoom, "OnDisable");
                f.Apply();
                Require(Near(f.Zoom.CurrentMultiplier, 1f) && Near(f.Zoom.TargetMultiplier, 1f)
                    && Near(f.Camera.orthographicSize, 11f * 1.2f / 2f * 1.02f),
                    "Disabling height retained its output or removed independent effects.");
            }
        }

        private static void CheckInteriorComposition()
        {
            using (var f = new CameraFixture())
            {
                var adapter = WasteRobotInteriorCameraZoom.GetOrCreate(f.Camera);
                GameObject owner = f.Create("Interior owner");
                f.Shake.SetElevationZoomMultiplier(1.4f);
                f.Apply();
                Require(adapter.Acquire(owner, f.Camera), "The interior adapter could not acquire the test camera.");
                adapter.SetInterior(owner, true, 6.5f, 0f);
                Call(adapter, "LateUpdate");
                Require(Near(f.Camera.orthographicSize, 6.5f), "Height changed the fixed interior size.");
                f.Shake.SetElevationZoomMultiplier(1.1f);
                Call(adapter, "Update"); f.Apply(); Call(adapter, "LateUpdate");
                Require(Near(f.Camera.orthographicSize, 6.5f), "A changed height base did not renormalize a settled interior.");
                adapter.SetInterior(owner, false, 6.5f, 0f);
                adapter.SetInterior(owner, true, 6.5f, .3f);
                Set(adapter, "blendElapsed", .31f);
                f.Shake.SetElevationZoomMultiplier(1.3f);
                Call(adapter, "Update"); f.Apply(); Call(adapter, "LateUpdate");
                Require(!adapter.IsBlending && Near(f.Camera.orthographicSize, 6.5f),
                    "A changing environmental base restarted the interior blend duration.");
                f.Shake.SetScanZoomMultiplier(1.2f);
                f.Shake.SetPhotoFocusMagnification(2f);
                Call(adapter, "Update"); f.Apply(); Call(adapter, "LateUpdate");
                Require(Near(f.Camera.orthographicSize, 6.5f * 1.2f / 2f), "Interior compensation removed scan or photo effects.");
                adapter.Release(owner);
            }
        }

        private static void CheckPausedComposition()
        {
            float originalTimeScale = Time.timeScale;
            try
            {
                Time.timeScale = 0f;
                using (var f = new CameraFixture())
                {
                    f.Shake.SetScanZoomMultiplier(1.2f);
                    f.Shake.SetPhotoFocusMagnification(2f);
                    f.Shake.SetElevationZoomMultiplier(1.4f);
                    Call(f.Shake, "LateUpdate");
                    Require(Near(f.Camera.orthographicSize, 11f * 1.4f * 1.2f / 2f),
                        "A paused compositor retained the previous frame's height multiplier.");
                    Call(f.Zoom, "OnDisable");
                    Call(f.Shake, "LateUpdate");
                    Require(Near(f.Camera.orthographicSize, 11f * 1.2f / 2f),
                        "A paused compositor retained the disabled height channel.");
                }
            }
            finally { Time.timeScale = originalTimeScale; }
        }

        private static void Check(string name, Action action)
        {
            try { action(); passedChecks++; }
            catch (Exception error) { throw new InvalidOperationException(name + ": " + error.Message, error); }
        }

        private static bool Near(float a, float b) => Mathf.Abs(a - b) <= .0005f;
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        private static void Set(object target, string name, object value) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static object Call(object target, string name, params object[] args)
        {
            try { return target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args); }
            catch (TargetInvocationException error) when (error.InnerException != null) { throw error.InnerException; }
        }

        private sealed class HeightSource : IMapHeightSource
        {
            private readonly Func<Vector2, float> height;
            private readonly Predicate<Vector2> valid;
            public Vector2 MapSizeMeters => Vector2.one * 1000f;
            public HeightSource(Func<Vector2, float> height, Predicate<Vector2> valid = null)
            { this.height = height; this.valid = valid; }
            public bool TrySample(Vector2 position, out float value, int channel = 0)
            { value = height(position); return valid == null || valid(position); }
        }

        private sealed class CameraFixture : IDisposable
        {
            private readonly Scene scene = EditorSceneManager.NewPreviewScene();
            public readonly Camera Camera;
            public readonly RobotCameraShake Shake;
            public readonly RobotCameraElevationZoom Zoom;
            public readonly Transform Player;
            public CameraFixture()
            {
                Camera = Create("Elevation validation camera").AddComponent<Camera>();
                Camera.orthographic = true;
                Camera.orthographicSize = 11f;
                Camera.aspect = 1f;
                Camera.transform.position = new Vector3(0, 0, -10);
                Shake = Camera.gameObject.AddComponent<RobotCameraShake>();
                Set(Shake, "enableGamepadRumble", false);
                Call(Shake, "Awake");
                Zoom = Camera.gameObject.AddComponent<RobotCameraElevationZoom>();
                Call(Zoom, "Awake");
                Player = Create("Elevation validation player").transform;
            }
            public GameObject Create(string name)
            {
                var root = new GameObject(name);
                SceneManager.MoveGameObjectToScene(root, scene);
                return root;
            }
            public float Evaluate(IMapHeightSource source, float playerHeight = 40f,
                Vector2? bottomLeft = null, Vector2? bottomRight = null, Vector2? topLeft = null)
                => (float)Call(Zoom, "EvaluateViewport", source, Vector2.zero, playerHeight,
                    bottomLeft ?? new Vector2(-10, -10), bottomRight ?? new Vector2(10, -10),
                    topLeft ?? new Vector2(-10, 10), 24, 24);
            public float Sample() => (float)Call(Zoom, "SampleVisibleTerrain");
            public void Tick(float deltaTime) => Call(Zoom, "Tick", deltaTime);
            public void Apply() => Call(Shake, "ApplyShakeToCamera");
            public void Dispose() => EditorSceneManager.ClosePreviewScene(scene);
        }

        private sealed class TerrainFixture : IDisposable
        {
            private readonly CameraFixture fixture;
            private readonly Texture2D texture;
            private readonly HeightMapLevelAsset tile;
            private readonly TiledLevelAsset definition;
            private readonly TiledMapRuntime runtime;
            public readonly MapTestSceneController Map;
            public TerrainFixture(CameraFixture fixture)
            {
                this.fixture = fixture;
                texture = new Texture2D(128, 128, TextureFormat.R16, false, true);
                var heights = new ushort[128 * 128];
                for (int y = 0; y < 128; y++)
                    for (int x = 0; x < 128; x++) heights[y * 128 + x] = x < 77 ? ushort.MaxValue : (ushort)0;
                texture.SetPixelData(heights, 0); texture.Apply();
                tile = ScriptableObject.CreateInstance<HeightMapLevelAsset>();
                Set(tile, "heightMap", texture); Set(tile, "mapWidthMeters", 200f); Set(tile, "mapHeightMeters", 200f);
                Set(tile, "minimumHeightMeters", 0f); Set(tile, "maximumHeightMeters", 40f);
                Set(tile, "normalizeSourceRange", false); Set(tile, "surfaceSmoothingSigmaMeters", 0f);
                Set(tile, "detailSmoothingSigmaMeters", 0f); Set(tile, "bakedHeightResolution", 128);
                definition = ScriptableObject.CreateInstance<TiledLevelAsset>();
                definition.Configure(Vector2.one * 200f, new Vector2(.5f, 1f), tile,
                    new[] { new TiledLevelAsset.Tile(Vector2Int.zero, tile) });
                runtime = new TiledMapRuntime(definition, false, 128);
                GameObject root = fixture.Create("Inactive elevation terrain"); root.SetActive(false);
                root.transform.position = new Vector3(31, 47, 0);
                Map = root.AddComponent<MapTestSceneController>();
                Set(Map, "levelAsset", definition); Set(Map, "runtimeMap", runtime);
                Set(Map, "mapWidthMeters", 200f); Set(Map, "mapHeightMeters", 200f);
                var views = (IList)typeof(MapTestSceneController).GetField("tileViews", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Map);
                Type viewType = typeof(MapTestSceneController).Assembly.GetType("AnimalGame.MapTest.TiledMapTileView");
                foreach (var entry in runtime.Tiles) views.Add(Activator.CreateInstance(viewType, entry));
            }
            public void PlacePlayer(Vector2 position)
            {
                fixture.Player.position = Map.MapPositionToWorld(position);
                fixture.Camera.transform.SetPositionAndRotation(fixture.Player.position - Vector3.forward * 10f, Quaternion.identity);
            }
            public void Dispose()
            {
                Object.DestroyImmediate(Map.gameObject);
                runtime.Dispose();
                Object.DestroyImmediate(definition); Object.DestroyImmediate(tile); Object.DestroyImmediate(texture);
            }
        }
    }
}
