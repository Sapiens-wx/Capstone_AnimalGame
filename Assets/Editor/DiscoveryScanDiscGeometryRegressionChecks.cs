using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Hud = AnimalGame.MainUI.MainUI;
using AnimalGame.MainUI;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace AnimalGame.Editor
{
    /// <summary>Checks the actual Toolkit disc against released scan zoom and the shared world-renderer boundary.</summary>
    public static class DiscoveryScanDiscGeometryRegressionChecks
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string UiPath = "Assets/Prefabs/Resources/UI/MainUI.prefab";
        private const string RobotPath = "Assets/Prefabs/Resources/Robot/RobotMarker.prefab";
        private static int checks;

        [MenuItem("Animal Game/Validation/Run Discovery Scan Disc Geometry Checks")]
        public static void Run()
        {
            float timeScale = Time.timeScale;
            Random.State random = Random.state;
            float clipEnabled = Shader.GetGlobalFloat("_PlayerUiClipEnabled");
            float clipRadius = Shader.GetGlobalFloat("_PlayerUiClipRadiusPixels");
            float clipSoftness = Shader.GetGlobalFloat("_PlayerUiClipSoftnessPixels");
            Vector4 clipCenter = Shader.GetGlobalVector("_PlayerUiClipCenterPixels");
            try
            {
                Time.timeScale = 1f;
                checks = 0;
                Check("Toolkit disc changes only its radius across aspect ratios and poses", CheckToolkitGeometry);
                Check("Rebuilt documents retain the current scan disc pose", CheckRebuiltDocument);
                Check("Released scan preserves normal world coverage throughout expansion and return", CheckReleasedLifecycle);
                Check("Organic and terrain clipping share the current disc", CheckClipping);
                Check("Shake and photo focus do not pump disc compensation", CheckCameraComposition);
                Check("Disabled or missing camera effects restore the normal disc", CheckCleanup);
                Check("Larger normal cameras and legacy release keep their normal disc", CheckLargerAndLegacy);
                Debug.Log("DISCOVERY SCAN DISC GEOMETRY CHECKS PASSED: " + checks + " groups; real Toolkit geometry,"
                    + " released particles, world coverage, clipping, camera composition, rebuilding and cleanup.");
            }
            finally
            {
                Time.timeScale = timeScale;
                Random.state = random;
                Shader.SetGlobalFloat("_PlayerUiClipEnabled", clipEnabled);
                Shader.SetGlobalFloat("_PlayerUiClipRadiusPixels", clipRadius);
                Shader.SetGlobalFloat("_PlayerUiClipSoftnessPixels", clipSoftness);
                Shader.SetGlobalVector("_PlayerUiClipCenterPixels", clipCenter);
            }
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void CheckToolkitGeometry()
        {
            using (var f = new Fixture())
            {
                foreach (Vector2 size in new[] { new Vector2(1920, 1080), new Vector2(1280, 720),
                    new Vector2(1440, 1080), new Vector2(1920, 1200), new Vector2(2560, 1080) })
                foreach (float inventoryProgress in new[] { 0f, .4f, 1f })
                {
                    f.Root.style.width = size.x;
                    f.Root.style.height = size.y;
                    Set(f.Hud, "progress", inventoryProgress);
                    f.Hud.SetPhotoPose(inventoryProgress == .4f ? 1.15f : 1f,
                        new Vector2(1920, 1080), inventoryProgress == .4f ? new Vector2(40, -20) : Vector2.zero, 1f);
                    f.Hud.SetDiscoveryScanDiscScale(1f);
                    f.Layout();
                    f.Hud.TryGetRingScreenGeometry(out Vector2 originalCenter, out float originalRadius);
                    float outerRadius = RenderedRadius(f.Root.Q<ViewRingElement>("OuterRing"));
                    Rect[] edgeBounds = f.EdgeBounds();
                    const float scale = 11f / 15.5f;
                    f.Hud.SetDiscoveryScanDiscScale(scale);
                    // Geometry must be authoritative immediately, even before deferred Toolkit layout resolves.
                    Require(f.Hud.TryGetRingScreenGeometry(out Vector2 immediateCenter, out float immediateRadius)
                        && Vector2.Distance(originalCenter, immediateCenter) < .01f
                        && Near(immediateRadius, originalRadius * scale),
                        "A new disc scale depends on the previous rendered frame or moves the optical center.");
                    f.Layout();
                    f.CompareRenderedGeometry();
                    f.Hud.TryGetRingScreenGeometry(out Vector2 center, out float radius);
                    Require(Vector2.Distance(originalCenter, center) < .01f && Near(radius, originalRadius * scale),
                        "The displayed disc did not shrink around its inner circle center at " + size + ".");
                    Require(Near(RenderedRadius(f.Root.Q<ViewRingElement>("OuterRing")), outerRadius * scale),
                        "The outer circle and its ticks did not follow the inner circle.");
                    f.RequireEdgeBounds(edgeBounds);
                }
                foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 3f })
                {
                    f.Hud.SetDiscoveryScanDiscScale(invalid);
                    Require(Near((float)Get(f.Hud, "discoveryScanDiscScale"), 1f),
                        "An invalid or enlarged scale destabilized the normal disc.");
                }
                f.Hud.SetDiscoveryScanDiscScale(-1f);
                Require((float)Get(f.Hud, "discoveryScanDiscScale") > 0f,
                    "Negative input inverted or removed the disc.");
            }
        }

        private static void CheckRebuiltDocument()
        {
            using (var f = new Fixture())
            {
                f.Hud.SetDiscoveryScanDiscScale(.72f);
                f.Layout();
                f.Hud.TryGetRingScreenGeometry(out Vector2 center, out float radius);
                VisualElement previous = f.Root.Q("ViewRing");
                VisualTreeAsset source = f.Document.visualTreeAsset;
                f.Document.visualTreeAsset = null;
                f.Document.visualTreeAsset = source;
                f.Hud.Init();
                f.Layout();
                Require(previous != f.Root.Q("ViewRing"), "The fixture did not rebuild the actual Toolkit document.");
                Require(f.Hud.TryGetRingScreenGeometry(out Vector2 rebuiltCenter, out float rebuiltRadius)
                    && Vector2.Distance(center, rebuiltCenter) < .01f && Near(radius, rebuiltRadius),
                    "Document recreation lost the live scan disc pose.");
                f.CompareRenderedGeometry();
            }
        }

        private static void CheckReleasedLifecycle()
        {
            using (var f = new Fixture())
            {
                float normalRadius = f.Radius;
                f.Capture("Normal");
                f.Charge();
                Require(f.Size < 11f && Near(f.Radius, normalRadius),
                    "The existing charge-in camera effect unexpectedly shrank the disc.");
                f.Release();
                float previousRadius = f.Radius;
                float arrival = f.ArrivalDuration;
                int expansionFrames = Mathf.CeilToInt((arrival + .001f) / .01f);
                float expansionStep = (arrival + .001f) / expansionFrames;
                for (int frame = 0; frame < expansionFrames; frame++)
                {
                    f.Advance(expansionStep);
                    f.RequireWorldCoverage(normalRadius);
                    Require(f.Radius <= previousRadius + .001f,
                        "The disc radius reversed direction while the released camera was expanding.");
                    previousRadius = f.Radius;
                }
                Require(Near(f.Size, 15.5f) && Near(f.Radius / normalRadius, 11f / 15.5f)
                    && f.Source.HasActiveReleasedScanWaves,
                    "The released disc does not occupy the expected 71 percent diameter during observation.");
                f.Advance(.1f);
                Require(Near(f.Size, 15.5f) && f.Source.HasActiveReleasedScanWaves,
                    "The disc recovered before the real released particles finished.");
                for (int frame = 0; frame < 120; frame++)
                {
                    f.Advance(.01f);
                    f.RequireWorldCoverage(normalRadius);
                }
                Require(!f.Source.HasActiveReleasedScanWaves && Near(f.Size, 11f) && Near(f.Radius, normalRadius),
                    "Wave completion did not smoothly restore the normal view and disc.");
            }
            using (var f = new Fixture(11f, false))
            {
                float normalRadius = f.Radius;
                f.Charge(); f.Release(); f.Advance(f.ArrivalDuration + .001f);
                f.RequireWorldCoverage(normalRadius);
                Require(Near(f.Size, 15.5f), "The camera without RobotCameraShake did not enter observation.");
                // This is a new panel's first render. Repeated manual panel renders in one editor frame
                // can reuse native UI batches and omit unchanged HUD artwork after a target clear.
                f.Capture("Observation");
            }
        }

        private static void CheckClipping()
        {
            using (var f = new Fixture())
            {
                f.Charge(); f.Release(); f.Advance(.7f);
                f.Layout();
                Call(f.View, "UpdateOrganicVisibilityClip");
                Vector2 center = f.View.GetUiCenterScreenPoint();
                Require(Near(Shader.GetGlobalFloat("_PlayerUiClipRadiusPixels"), f.Radius)
                    && Vector2.Distance((Vector2)Shader.GetGlobalVector("_PlayerUiClipCenterPixels"), center) < .01f,
                    "Organic sprites use a different boundary from the live white circle.");
                GameObject go = f.Create("Inactive terrain clip fixture");
                go.SetActive(false);
                var map = go.AddComponent<MapTestSceneController>();
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/DynamicHeightContours.shader");
                Require(shader != null, "The real surface shader is missing.");
                var material = new Material(shader);
                try
                {
                    Set(map, "surfaceRevealUi", f.View);
                    Set(map, "contourMaterial", material);
                    Set(map, "mapCamera", f.Camera);
                    Call(map, "RefreshSurfaceMaterialSettings");
                    Require(Near(material.GetFloat("_SurfaceRevealRadiusPixels"), f.Radius)
                        && Vector2.Distance((Vector2)material.GetVector("_SurfaceRevealCenterPixels"), center) < .01f,
                        "Terrain surface reveal uses a different boundary from organic sprites and the white circle.");
                    float previousRadius = f.Radius;
                    f.Advance(.1f);
                    Require(!Near(previousRadius, f.Radius), "The fixture did not advance the live scan disc.");
                    Set(map, "lastViewportUpdateFrame", -1);
                    Call(map, "HandleCameraPreCull", f.Camera);
                    Require(Near(material.GetFloat("_SurfaceRevealRadiusPixels"), f.Radius),
                        "The map camera's before-render callback left the surface mask at an earlier Update radius.");
                }
                finally
                {
                    Set(map, "contourMaterial", null);
                    Object.DestroyImmediate(material);
                }
            }
        }

        private static void CheckCameraComposition()
        {
            using (var f = new Fixture())
            {
                f.Charge(); f.Release(); f.Advance(f.ArrivalDuration + .001f);
                float radius = f.Radius;
                f.Shake.SetPhotoFocusMagnification(2f);
                Set(f.Shake, "springZoom", .02f);
                Set(f.Shake, "globalIntensity", 1f);
                f.TickCamera(0f);
                Require(Near(f.Shake.CurrentZoomOffsetFraction, .02f)
                    && f.Shake.CurrentZoomOffsetFraction > 0f && Near(f.Size, 15.5f / 2f * 1.02f),
                    "The fixture did not compose photo focus with a legal nonzero impact zoom.");
                Require(Near(f.Radius, radius),
                    "Photo focus or impact shake contaminated the scan-only disc compensation.");
                Set(f.Shake, "springZoom", -.02f);
                f.TickCamera(0f);
                Require(f.Shake.CurrentZoomOffsetFraction < 0f && Near(f.Size, 15.5f / 2f * .98f),
                    "The fixture did not apply the opposite legal impact zoom.");
                Require(Near(f.Radius, radius), "Changing instantaneous camera shake pumps the white circle.");
            }
        }

        private static void CheckCleanup()
        {
            using (var f = new Fixture())
            {
                float radius = f.Radius;
                f.Charge(); f.Release(); f.Advance(.8f);
                Require(f.Radius < radius, "The zoom cleanup fixture never entered the reduced-disc pose.");
                Set(f.View, "enableScanCameraZoom", false);
                f.TickCamera(0f);
                Require(Near(f.Radius, radius) && Near(f.Size, 11f), "Disabling zoom retained a reduced disc.");
                Set(f.View, "enableScanCameraZoom", true);
                f.TickCamera(.5f);
                Require(f.Radius < radius, "Re-enabling zoom did not resume the existing released wave.");
                Call(f.View, "OnDisable");
                Require(Near(f.Radius, radius), "Disabling the scan view retained a reduced disc.");
                f.TickCamera(.5f);
                f.Source.enabled = false;
                Call(f.Source, "OnDisable");
                f.TickCamera(.7f); f.TickCamera(.7f);
                Require(Near(f.Radius, radius), "Losing the scan source retained a reduced disc.");
            }
            using (var f = new Fixture())
            {
                float radius = f.Radius;
                f.Charge(); f.Release(); f.Advance(.8f);
                Require(f.Radius < radius, "The camera-loss fixture never entered the reduced-disc pose.");
                f.Camera.enabled = false;
                f.TickCamera(0f);
                Require(Near(f.Radius, radius), "Losing the tracking camera retained a reduced disc.");
            }
        }

        private static void CheckLargerAndLegacy()
        {
            using (var f = new Fixture(18f))
            {
                float radius = f.Radius;
                f.Charge(); f.Release(); f.Advance(f.ArrivalDuration + .001f);
                Require(Near(f.Size, 18f) && Near(f.Radius, radius),
                    "A normal camera already wider than observation was narrowed or shrank its disc.");
            }
            using (var f = new Fixture())
            {
                float radius = f.Radius;
                f.View.BindDiscoveryScanController(null);
                f.View.FullyChargedBiologicalScanReleased -= f.Launch;
                f.Charge(); f.Release(); f.TickCamera(.3f);
                Require(Near(f.Radius, radius), "A legacy non-discovery release changed the disc.");
            }
        }

        private static void Check(string name, Action action)
        {
            try { action(); checks++; }
            catch (Exception error) { throw new InvalidOperationException(name + ": " + error.Message, error); }
        }
        private static bool Near(float a, float b) => Mathf.Abs(a - b) <= .01f;
        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }
        private static object Get(object obj, string field) => obj.GetType().GetField(field, Flags).GetValue(obj);
        private static void Set(object obj, string field, object value) => obj.GetType().GetField(field, Flags).SetValue(obj, value);
        private static object Call(object obj, string method, params object[] args)
        {
            try { return obj.GetType().GetMethod(method, Flags).Invoke(obj, args); }
            catch (TargetInvocationException error) when (error.InnerException != null) { throw error.InnerException; }
        }
        private static float RenderedRadius(ViewRingElement ring) => Vector2.Distance(
            ring.LocalToWorld(ring.contentRect.center), ring.LocalToWorld(ring.contentRect.center + Vector2.right * ring.Radius));

        private sealed class Fixture : IDisposable
        {
            private readonly Scene scene;
            private readonly PanelSettings settings;
            private readonly RenderTexture texture;
            public readonly Hud Hud;
            public readonly UIDocument Document;
            public readonly Camera Camera;
            public readonly RobotCameraShake Shake;
            public readonly ScanChargeUI View;
            public readonly BioScanController Source;
            public VisualElement Root => Document.rootVisualElement;
            public float Size => Camera.orthographicSize;
            public float Radius { get { Hud.TryGetRingScreenGeometry(out _, out float radius); return radius; } }
            public float ArrivalDuration => Mathf.Max(.05f, Source.LastReleasedWaveTravelDuration
                - (float)Get(View, "discoveryCameraArrivalLeadTime"));
            private ParticleSystem Emitted => (ParticleSystem)Get(Source, "emittedPointSystem");
            private static readonly string[] EdgeNames = { "Status", "Time", "Sound", "Coordinates", "CompassHost", "LevelIndicator", "Inventory" };

            public Fixture(float baseSize = 11f, bool withShake = true)
            {
                scene = EditorSceneManager.NewPreviewScene();
                GameObject go = Create("Discovery disc actual Toolkit fixture");
                Document = go.AddComponent<UIDocument>();
                settings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(
                    "Assets/Prefabs/Resources/UI/Main/MainUISetting.asset"));
                texture = new RenderTexture(1920, 1080, 24);
                texture.Create(); settings.targetTexture = texture;
                Document.panelSettings = settings;
                Document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                    "Assets/Prefabs/Resources/UI/Main/MainUIDoc.uxml");
                Hud = go.AddComponent<Hud>();
                Hud.Init(); Layout();
                Camera = Create("Discovery disc camera").AddComponent<Camera>();
                Camera.orthographic = true; Camera.orthographicSize = baseSize;
                Camera.transform.position = new Vector3(10000f, 10000f, -10f);
                if (withShake)
                {
                    Shake = Camera.gameObject.AddComponent<RobotCameraShake>();
                    Set(Shake, "enableGamepadRumble", false); Call(Shake, "Awake");
                }
                GameObject viewObject = new GameObject("Discovery disc scan view", typeof(RectTransform));
                SceneManager.MoveGameObjectToScene(viewObject, scene);
                View = viewObject.AddComponent<ScanChargeUI>();
                var profile = AssetDatabase.LoadAssetAtPath<GameObject>(UiPath).GetComponentInChildren<ScanChargeUI>(true);
                foreach (string field in new[] { "scanZoomReferenceOrthographicSize", "holdTargetOrthographicSize",
                    "releasePeakOrthographicSize", "maximumChargeDuration", "releaseZoomOutDuration", "releaseZoomReturnDuration",
                    "cancelledChargeZoomReturnDuration", "discoveryObservationOrthographicSize", "discoveryCameraArrivalLeadTime" })
                    Set(View, field, Get(profile, field));
                Set(View, "showScanRing", false); Set(View, "mainUI", Hud); Set(View, "mapCamera", Camera);
                Set(View, "cameraZoomInitialized", false); Call(View, "ResolveTrackingReferences");
                Source = Create("Discovery disc real scan wave").AddComponent<BioScanController>();
                Source.transform.position = new Vector3(10000f, 10000f, 0f);
                var sourceProfile = AssetDatabase.LoadAssetAtPath<GameObject>(RobotPath).GetComponentInChildren<BioScanController>(true);
                foreach (string field in new[] { "biologicalSignalClipShader", "signalPointCount", "maximumPointCount",
                    "automaticAngularCoverage", "pointCoverageOverlap", "formedRingRadius", "signalSpeed", "maximumSignalDistance",
                    "signalFadeOutDuration", "signalPointDiameter", "scanWaveHalfThickness" })
                    Set(Source, field, Get(sourceProfile, field));
                Call(Source, "CreateSignalResources");
                Transform particleRoot = (Transform)Get(Source, "signalParticleRoot");
                SceneManager.MoveGameObjectToScene(particleRoot.gameObject, scene);
                View.BindDiscoveryScanController(Source);
                View.FullyChargedBiologicalScanReleased += Launch;
                TickCamera(0f);
            }

            public GameObject Create(string name)
            {
                var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, scene); return go;
            }
            public void Layout()
            {
                Require(Root.panel != null, "The actual UIDocument is not attached to a runtime panel.");
                Call(Root.panel, "ValidateLayout"); Call(Hud, "ApplyLayout"); Call(Root.panel, "ValidateLayout");
            }
            public void CompareRenderedGeometry()
            {
                Hud.TryGetRingScreenGeometry(out Vector2 center, out float radius);
                var inner = Root.Q<ViewRingElement>("InnerRing");
                Vector2 origin = RuntimePanelUtils.ScreenToPanel(Root.panel, Vector2.zero);
                Vector2 end = RuntimePanelUtils.ScreenToPanel(Root.panel, new Vector2(Screen.width, Screen.height));
                Vector2 units = new Vector2((end.x - origin.x) / Screen.width, (end.y - origin.y) / Screen.height);
                Vector2 actual = inner.LocalToWorld(inner.contentRect.center);
                Vector2 actualScreen = new Vector2((actual.x - origin.x) / units.x, Screen.height - (actual.y - origin.y) / units.y);
                Require(Vector2.Distance(center, actualScreen) < 1f && Mathf.Abs(radius - RenderedRadius(inner) / units.x) < 1f,
                    "Published scan geometry differs from the actual transformed Toolkit circle.");
            }
            public void Capture(string phase)
            {
                Layout();
                Require(Resources.Load<Font>("UI/Font/Orbitron-Regular") != null
                    && Resources.Load<Font>("UI/Font/Orbitron-Medium") != null,
                    "The actual HUD fonts are missing from the graphics validation project.");
                string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string output = Path.GetFileName(project).StartsWith("DiscoveryScanDiscValidation-", StringComparison.Ordinal)
                    ? Path.GetDirectoryName(project) : Path.Combine(project, "Temp");
                Directory.CreateDirectory(output);
                string path = Path.Combine(output, "DiscoveryScanDiscGeometry-" + phase + ".png");
                RenderTexture previous = RenderTexture.active;
                Texture2D image = null;
                try
                {
                    foreach (VisualElement element in Root.Query<VisualElement>().ToList())
                        element.MarkDirtyRepaint();
                    Root.MarkDirtyRepaint();
                    RenderTexture.active = texture; GL.Clear(true, true, Color.black);
                    MethodInfo repaint = Root.panel.GetType().GetMethods(Flags)
                        .FirstOrDefault(m => m.Name == "Repaint" && m.GetParameters().Length == 1);
                    Require(repaint != null, "The native Toolkit panel has no Repaint method.");
                    repaint.Invoke(Root.panel, new object[] { new Event { type = EventType.Repaint } });
                    MethodInfo render = Root.panel.GetType().GetMethod("Render", Flags, null, Type.EmptyTypes, null);
                    Require(render != null, "The native Toolkit panel has no Render method.");
                    render.Invoke(Root.panel, null);
                    RenderTexture.active = texture;
                    image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
                    image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); image.Apply();
                    int whitePixels = image.GetPixels32().Count(p => p.r > 180 && p.g > 180 && p.b > 180);
                    Require(whitePixels > 1000, "Native HUD capture is empty or missing its white circle strokes.");
                    foreach (string element in new[] { "Status", "CompassHost", "Time" })
                        Require(CountBrightPixels(image, Root.Q(element).worldBound) > 100,
                            "Native HUD capture omitted unchanged " + element + " artwork.");
                    Require(CountBrightPixels(image, new Rect(1450, 18, 200, 6)) > 100,
                        "Native HUD capture omitted the unchanged top-right frame border.");
                    File.WriteAllBytes(path, image.EncodeToPNG());
                    Debug.Log("Discovery scan disc native HUD capture: " + path);
                }
                finally
                {
                    RenderTexture.active = previous;
                    if (image != null) Object.DestroyImmediate(image);
                }
            }
            private static int CountBrightPixels(Texture2D image, Rect panelBounds)
            {
                int left = Mathf.Clamp(Mathf.FloorToInt(panelBounds.xMin), 0, image.width);
                int right = Mathf.Clamp(Mathf.CeilToInt(panelBounds.xMax), 0, image.width);
                int top = Mathf.Clamp(Mathf.FloorToInt(panelBounds.yMin), 0, image.height);
                int bottom = Mathf.Clamp(Mathf.CeilToInt(panelBounds.yMax), 0, image.height);
                int count = 0;
                for (int y = top; y < bottom; y++)
                for (int x = left; x < right; x++)
                {
                    Color pixel = image.GetPixel(x, image.height - 1 - y);
                    if (pixel.r > .5f && pixel.g > .5f) count++;
                }
                return count;
            }
            public Rect[] EdgeBounds()
            {
                var bounds = new Rect[EdgeNames.Length];
                for (int i = 0; i < bounds.Length; i++) bounds[i] = Root.Q(EdgeNames[i]).worldBound;
                return bounds;
            }
            public void RequireEdgeBounds(Rect[] expected)
            {
                for (int i = 0; i < expected.Length; i++)
                {
                    Rect current = Root.Q(EdgeNames[i]).worldBound;
                    Require(Vector2.Distance(expected[i].position, current.position) < .01f
                        && Vector2.Distance(expected[i].size, current.size) < .01f,
                        EdgeNames[i] + " was scaled or displaced with the discovery disc.");
                }
            }
            public void Charge()
            {
                Call(View, "BeginCharge"); Set(View, "chargeElapsed", Get(View, "maximumChargeDuration")); TickCamera(0f);
            }
            public void Release() => Call(View, "BeginRelease");
            public void Launch() => Call(Source, "LaunchFormedSignals");
            public void TickCamera(float seconds)
            {
                Call(View, "UpdateScanCameraZoom", seconds); Call(View, "ApplyScanCameraZoom");
                if (Shake != null) Call(Shake, "ApplyShakeToCamera");
            }
            public void Advance(float seconds)
            {
                int frames = Mathf.Max(1, Mathf.CeilToInt(seconds / .01f));
                float step = seconds / frames;
                for (int frame = 0; frame < frames; frame++)
                {
                    Call(Source, "TickScanWave", step); Emitted.Simulate(step, true, false, false); TickCamera(step);
                }
            }
            public void RequireWorldCoverage(float normalRadius)
            {
                float scanSize = (float)Get(View, "currentScanOrthographicSize");
                float normalSize = View.BaseCameraOrthographicSize;
                float expected = scanSize > normalSize ? normalRadius * normalSize / scanSize : normalRadius;
                Require(Near(Radius, expected), "Scan release/return changed the normal world area visible inside the disc.");
            }
            public void Dispose()
            {
                View.FullyChargedBiologicalScanReleased -= Launch;
                Call(Source, "OnDisable");
                Transform particleRoot = (Transform)Get(Source, "signalParticleRoot");
                Material material = (Material)Get(Source, "signalPointMaterial");
                Set(Source, "signalParticleRoot", null); Set(Source, "signalPointMaterial", null);
                if (particleRoot != null) Object.DestroyImmediate(particleRoot.gameObject);
                if (material != null) Object.DestroyImmediate(material);
                EditorSceneManager.ClosePreviewScene(scene);
                texture.Release(); Object.DestroyImmediate(texture); Object.DestroyImmediate(settings);
            }
        }
    }
}
