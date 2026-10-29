using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Globalization;
using System.Xml.Linq;
using AnimalGame.MainUI;
using AnimalGame.MainUI.Inventory;
using AnimalGame.Animals;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using Hud = AnimalGame.MainUI.MainUI;

namespace AnimalGame.Editor
{
    /// <summary>Checks the real Toolkit tree in an isolated preview scene without saving or replacing the user's scene.</summary>
    public static class MainUIRegressionChecks
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static int checks;

        [MenuItem("Animal Game/Validation/Run Main UI Checks")]
        public static void Run()
        {
            checks = 0;
            float previousScale = Time.timeScale;
            Scene scene = EditorSceneManager.NewPreviewScene();
            PanelSettings settings = null;
            RenderTexture texture = null;
            try
            {
                var obj = new GameObject("Main UI regression");
                SceneManager.MoveGameObjectToScene(obj, scene);
                var document = obj.AddComponent<UIDocument>();
                settings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(
                    "Assets/Prefabs/Resources/UI/Main/MainUISetting.asset"));
                texture = new RenderTexture(1920, 1080, 24);
                texture.Create();
                settings.targetTexture = texture;
                document.panelSettings = settings;
                document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                    "Assets/Prefabs/Resources/UI/Main/MainUIDoc.uxml");
                var hud = obj.AddComponent<Hud>();
                hud.Init();
                var root = document.rootVisualElement;
                CheckTelemetry(hud, root, scene);
                Assert(root.Q<ViewRingElement>("InnerRing") != null, "Custom UXML elements import");
                var level = root.Q<LevelIndicatorElement>("LevelIndicator");
                Assert(level != null, "Level indicator imports from UXML");
                var authoredLevel = XDocument.Load("Assets/Prefabs/Resources/UI/Main/MainUIDoc.uxml")
                    .Descendants().Single(e => (string)e.Attribute("name") == "LevelIndicator");
                foreach (var attribute in new[] { ("width", "width"), ("length", "length"), ("height", "height"),
                    ("center-margin", "centerMargin"), ("degree", "degree") })
                {
                    float expected = float.Parse((string)authoredLevel.Attribute(attribute.Item1), CultureInfo.InvariantCulture);
                    float actual = (float)typeof(LevelIndicatorElement).GetProperty(attribute.Item2).GetValue(level);
                    Assert(Mathf.Approximately(actual, expected), "Level imports authored " + attribute.Item1);
                }
                level.SetDegree(45);
                Assert(level.degree == 45 && level.Q<Label>("DegreeText").text == "45\u00b0", "Slope degree updates readout");
                level.SetDegree(-10);
                Assert(level.degree == -10 && level.Q<Label>("DegreeText").text == "-10\u00b0",
                    "Runtime degree preserves negative tilt and text");
                level.SetDegree(-120);
                Assert(level.degree == -90, "Runtime degree clamps at -90");
                level.SetDegree(120);
                Assert(level.degree == 90, "Runtime degree clamps at 90");
                level.degree = float.NaN;
                Assert(level.degree == 0, "Invalid degree stays finite");
                level.degree = 45;
                hud.RefreshLatePresentation();
                Assert(level.degree == 0, "Gameplay presentation replaces preview degree when no player is bound");
                var slopeObject = new GameObject("Terrain slope source");
                SceneManager.MoveGameObjectToScene(slopeObject, scene);
                var slopeRobot = slopeObject.AddComponent<RobotMover>();
                slopeRobot.enabled = false;
                typeof(Hud).GetField("slopeSource", Flags).SetValue(hud, slopeRobot);
                foreach (float angle in new[] { 23f, -14f, 0f })
                {
                    var terrain = new SlopeTraversalResult(true, true, false, UphillSlopeLevel.LevelOne,
                        Mathf.Abs(angle), angle, 0, 0, Mathf.Abs(angle), 0, 0, Vector2.down, TraversalBlockReason.None);
                    typeof(RobotMover).GetProperty("CurrentTraversalResult").SetValue(slopeRobot, terrain);
                    hud.RefreshLatePresentation();
                    Assert(level.degree == angle, "HUD reads signed terrain slope " + angle);
                }
                typeof(RobotMover).GetProperty("CurrentTraversalResult").SetValue(slopeRobot, SlopeTraversalResult.NoData);
                hud.RefreshLatePresentation();
                Assert(level.degree == 0, "Unavailable terrain resets slope readout");
                typeof(Hud).GetField("slopeSource", Flags).SetValue(hud, null);
                level.degree = 45;
                Assert(!hud.TryGetRingScreenGeometry(out _, out _), "No scan geometry before first layout");
                var screenSizes = new[] { new Vector2(1920, 1080), new Vector2(1280, 720),
                    new Vector2(1440, 1080), new Vector2(1920, 1200), new Vector2(2560, 1080) };
                foreach (var size in screenSizes)
                {
                    root.style.width = size.x; root.style.height = size.y;
                    ValidateLayout(root);
                    Invoke(hud, "ApplyLayout");
                    ValidateLayout(root);
                    CompareGeometry(hud, root, "Geometry at " + size);
                    Assert(Vector2.Distance(level.worldBound.center,
                        level.Q<Label>("DegreeText").worldBound.center) < .1f,
                        "Level readout stays at the line rotation center at " + size);
                    var inner = root.Q<ViewRingElement>("InnerRing");
                    Assert(Mathf.Abs(inner.worldBound.width - inner.worldBound.height) < .1f, "Circle remains round at " + size);
                    CompareEdgeAnchors(root);
                    hud.OpenInventory();
                    Step(hud, .125f); ValidateLayout(root);
                    CompareStatusMotion(root, hud.InventoryProgress);
                    Step(hud, .2f); ValidateLayout(root);
                    CompareStatusMotion(root, hud.InventoryProgress);
                    hud.CloseInventory(); Step(hud, .1f); ValidateLayout(root);
                    CompareStatusMotion(root, hud.InventoryProgress);
                    hud.OpenInventory(); Step(hud, .1f); ValidateLayout(root);
                    CompareStatusMotion(root, hud.InventoryProgress);
                    hud.CloseInventory(); Step(hud, 1); ValidateLayout(root);
                    CompareEdgeAnchors(root);
                    Assert(root.Q("Inventory").resolvedStyle.visibility == Visibility.Visible,
                        "Reversed inventory animation keeps the inventory strip visible at " + size);
                }
                root.style.width = 1920; root.style.height = 1080;
                level.SetDegree(0);
                ValidateLayout(root); Invoke(hud, "ApplyLayout"); ValidateLayout(root);
                var authoredRing = root.Q("ViewRing");
                var authoredStatus = root.Q("Status");
                var authoredCompass = root.Q("CompassHost");
                Rect compassLayout = authoredCompass.layout;
                Vector2 ringPosition = authoredRing.layout.position;
                Vector2 statusPosition = authoredStatus.layout.position;
                authoredRing.style.left = ringPosition.x + 23;
                authoredRing.style.top = ringPosition.y + 17;
                authoredStatus.style.left = statusPosition.x + 11;
                ValidateLayout(root); hud.Init(); Step(hud, 0); ValidateLayout(root);
                Assert(authoredRing.layout.position == ringPosition + new Vector2(23, 17), "Runtime preserves edited ring layout");
                Assert(authoredStatus.layout.x == statusPosition.x + 11, "Runtime preserves edited status layout");
                Assert(authoredCompass.layout == compassLayout, "Runtime preserves authored compass position and size");
                authoredCompass.style.translate = new Translate(new Length(-40, LengthUnit.Percent), 3);
                Invoke(hud, "ApplyLayout"); ValidateLayout(root);
                Assert(Mathf.Abs(authoredCompass.worldBound.center.x - root.worldBound.center.x - compassLayout.width * .1f) < 1
                    && Mathf.Abs(authoredCompass.worldBound.yMin - root.worldBound.yMin - compassLayout.y - 3) < 1,
                    "Runtime preserves the compass translation edited in UI Builder");
                authoredCompass.style.translate = StyleKeyword.Null;
                Invoke(hud, "ApplyLayout"); ValidateLayout(root);
                CompareGeometry(hud, root, "Edited UXML layout baseline");
                authoredRing.style.left = ringPosition.x; authoredRing.style.top = ringPosition.y;
                authoredStatus.style.left = statusPosition.x;
                ValidateLayout(root); Invoke(hud, "ApplyLayout"); ValidateLayout(root);
                hud.Init(); ValidateLayout(root);
                var scanner = obj.AddComponent<ScanChargeUI>();
                typeof(ScanChargeUI).GetField("mainUI", Flags).SetValue(scanner, hud);
                hud.TryGetRingScreenGeometry(out _, out float scanRadius);
                Assert(Mathf.Abs(scanner.GetUiRingScreenRadiusPixels() - scanRadius) < .01f,
                    "Scan component uses the UIDocument inner ring radius");
                Assert(root.Query<Button>().ToList().Count == 8, "Repeated Init keeps exactly eight buttons");
                Assert(root.Query<EmptyInventoryElement>().ToList().Count == 8, "Eight empty slots use reusable plus template");
                Assert(hud.SelectedIndex == 0, "Initial selection is upper-left");
                var allSlots = root.Query<Button>().ToList();
                var lastSlot = allSlots[7];
                using (var evt = PointerEnterEvent.GetPooled()) { evt.target = lastSlot; lastSlot.SendEvent(evt); }
                Assert(hud.SelectedIndex == 0, "Closed inventory ignores hover");

                Time.timeScale = .5f;
                hud.OpenInventory();
                Assert(hud.IsInventoryOpen && Time.timeScale == 0, "Opening owns pause");
                Step(hud, .125f); ValidateLayout(root);
                CompareGeometry(hud, root, "First motion phase");
                float partial = hud.InventoryProgress;
                hud.CloseInventory(); Step(hud, .03f); ValidateLayout(root);
                Assert(hud.InventoryProgress > 0 && hud.InventoryProgress < partial, "Interrupted opening reverses from current pose");
                hud.OpenInventory(); Step(hud, 1); ValidateLayout(root);
                Assert(hud.InventoryProgress == 1, "Animation advances while time scale is zero");
                CompareGeometry(hud, root, "Open pose");
                float viewportX = root.Q("ViewRing").worldBound.center.x;
                Assert(Mathf.Abs(viewportX - root.worldBound.x - root.layout.width * (2f / 3)) < 1,
                    "Open ring center is at two-thirds");
                Assert(Mathf.Abs(root.Q("Inventory").worldBound.center.x - root.worldBound.x - root.layout.width * .25f) < 1,
                    "Open inventory center is at one-quarter");
                foreach (var size in screenSizes)
                {
                    // Resize while open to cover the two different parent coordinate systems.
                    root.style.width = size.x; root.style.height = size.y;
                    ValidateLayout(root); Step(hud, 0); ValidateLayout(root);
                    CompareGeometry(hud, root, "Resized open pose at " + size);
                    CompareEdgeAnchors(root, true);
                    Assert(Mathf.Abs(root.Q("ViewRing").worldBound.center.x - root.worldBound.x - size.x * (2f / 3)) < 1,
                        "Resized open ring stays at two-thirds at " + size);
                    Vector2 inventoryCenter = root.Q("Inventory").worldBound.center - root.worldBound.position;
                    Assert(Vector2.Distance(inventoryCenter, new Vector2(size.x * .25f, size.y * .5f)) < 1,
                        "Resized open inventory stays at one-quarter and vertical center at " + size);
                }
                root.style.width = 1920; root.style.height = 1080;
                ValidateLayout(root); Step(hud, 0); ValidateLayout(root);
                using (var evt = PointerEnterEvent.GetPooled()) { evt.target = lastSlot; lastSlot.SendEvent(evt); }
                Assert(hud.SelectedIndex == 7, "Hover moves shared selection");
                var inventoryItem = ScriptableObject.CreateInstance<InventoryItem>();
                try
                {
                    hud.SetItem(7, inventoryItem); ValidateLayout(root);
                    Assert(root.Query<EmptyInventoryElement>().ToList().Count == 7, "Occupied item removes only its empty marker");
                    Assert(lastSlot.Q<Image>("ItemImage").style.display.value == DisplayStyle.Flex,
                        "Occupied item displays its image element");
                }
                finally
                {
                    hud.SetItem(7, null);
                    Object.DestroyImmediate(inventoryItem);
                }

                var cameraObject = new GameObject("Projection regression");
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.transform.position = new Vector3(4, 7, -10);
                var projection = obj.AddComponent<MainUIProjection>();
                projection.Initialize(camera);
                Vector3 before = camera.transform.position;
                Invoke(projection, "Awake"); Invoke(projection, "LateUpdate");
                Assert(camera.transform.position == before, "Reframing does not move camera");
                hud.TryGetRingScreenGeometry(out Vector2 center, out _);
                Vector3 renderedCenter = camera.WorldToScreenPoint(new Vector3(before.x, before.y, 0));
                Assert(Vector2.Distance(center, renderedCenter) < 1, "Projection places optical center at live UI center");

                foreach (float value in new[] { 0f, .25f, .5f, .75f, 1f })
                {
                    hud.Power = value; hud.Integrity = 1 - value;
                    Invoke(hud, "UpdateStatus"); ValidateLayout(root);
                    Assert(root.Q<Label>("PowerPercent").text == Mathf.RoundToInt(value * 100) + "%",
                        "Power label and circle share value " + value);
                    Assert(root.Q<CircularProgressElement>("IntegrityRing").progress == 1 - value,
                        "Independent integrity value " + value);
                }
                hud.Power = .92f; hud.Integrity = .71f; Invoke(hud, "UpdateStatus");
                foreach (float heading in new[] { -1f, 0f, 90f, 180f, 270f, 359f, 360f, 721f })
                {
                    hud.SetHeading(heading); ValidateLayout(root);
                    Assert(root.Q<CompassElement>("Compass").Heading >= 0 && root.Q<CompassElement>("Compass").Heading < 360,
                        "Normalized heading " + heading);
                }
                hud.SetHeading(0); hud.SetDate(new DateTime(2026, 1, 1));
                Assert(root.Q<Label>("Date").text == "2026_01_01", "Date format");
                hud.SetCoordinates(-12, -57); hud.SetAltitude(67);
                Assert(root.Q<Label>("LatitudeHemisphere").text == "S" && root.Q<Label>("LongitudeHemisphere").text == "W",
                    "Coordinate display interface");

                RenderAndCapture(root, texture, "mainui-open.png");
                hud.CloseInventory(); Step(hud, 1); ValidateLayout(root);
                Invoke(projection, "LateUpdate");
                Assert(Time.timeScale == .5f, "Closing restores previous time scale");
                Assert(root.Q("Inventory").resolvedStyle.visibility == Visibility.Visible, "Closed inventory strip remains visible");
                Assert(!allSlots[0].enabledInHierarchy, "Closed inventory buttons cannot interact");
                CompareGeometry(hud, root, "Restored closed pose");
                CompareEdgeAnchors(root);
                Vector2 inventoryCenterBeforePhoto = root.Q("Inventory").worldBound.center;
                hud.SetPhotoPose(1.2f, new Vector2(1920, 1080), new Vector2(60, -30), .5f);
                ValidateLayout(root); CompareGeometry(hud, root, "Photo zoom and translation");
                Vector2 photoCenter = root.worldBound.position + new Vector2(960, 540);
                Vector2 expectedInventoryCenter = photoCenter + (inventoryCenterBeforePhoto - photoCenter) * 1.2f
                    + new Vector2(60, -30);
                Assert(Vector2.Distance(root.Q("Inventory").worldBound.center, expectedInventoryCenter) < 1,
                    "Inventory follows the Stage photo transform exactly once");
                hud.TryGetRingScreenGeometry(out Vector2 savedCenter, out float savedRadius);
                document.enabled = false;
                Assert(hud.TryGetRingScreenGeometry(out Vector2 hiddenCenter, out float hiddenRadius)
                    && hiddenCenter == savedCenter && hiddenRadius == savedRadius, "Hidden document retains last valid geometry");
                document.enabled = true; hud.Init(); ValidateLayout(document.rootVisualElement);
                Assert(document.rootVisualElement.Query<Button>().ToList().Count == 8, "Rebuilt document rebinds exactly eight slots");
                hud.SetPhotoPose(1, new Vector2(1920, 1080), Vector2.zero, 1);
                Invoke(hud, "ApplyLayout"); ValidateLayout(document.rootVisualElement);
                RenderAndCapture(document.rootVisualElement, texture, "mainui-closed.png");
                var renderedLevel = document.rootVisualElement.Q<LevelIndicatorElement>("LevelIndicator");
                renderedLevel.SetDegree(45);
                RenderAndCapture(document.rootVisualElement, texture, "mainui-level-45.png");
                renderedLevel.SetDegree(90);
                RenderAndCapture(document.rootVisualElement, texture, "mainui-level-90.png");
                renderedLevel.SetDegree(-45);
                RenderAndCapture(document.rootVisualElement, texture, "mainui-level-negative-45.png");
                renderedLevel.SetDegree(-90);
                RenderAndCapture(document.rootVisualElement, texture, "mainui-level-negative-90.png");
                renderedLevel.SetDegree(0);
                document.rootVisualElement.style.width = 1280;
                document.rootVisualElement.style.height = 720;
                ValidateLayout(document.rootVisualElement);
                Invoke(hud, "ApplyLayout"); ValidateLayout(document.rootVisualElement);
                RenderAndCapture(document.rootVisualElement, texture, "mainui-1280.png");
                Debug.Log("Main UI regression PASS: " + checks + " checks.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                if (texture != null) { texture.Release(); Object.DestroyImmediate(texture); }
                if (settings != null) Object.DestroyImmediate(settings);
                Time.timeScale = previousScale;
            }
        }

        [MenuItem("Animal Game/Validation/Run Main UI Clock Checks")]
        public static void RunClockChecks()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            PanelSettings settings = null;
            try
            {
                var obj = new GameObject("Main UI clock checks");
                SceneManager.MoveGameObjectToScene(obj, scene);
                settings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(
                    "Assets/Prefabs/Resources/UI/Main/MainUISetting.asset"));
                var document = obj.AddComponent<UIDocument>();
                document.panelSettings = settings;
                document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                    "Assets/Prefabs/Resources/UI/Main/MainUIDoc.uxml");
                var hud = obj.AddComponent<Hud>();
                hud.Init();
                CheckGameClock(hud, document.rootVisualElement);
                Debug.Log("MAIN UI CLOCK CHECKS PASSED");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                if (settings != null) Object.DestroyImmediate(settings);
            }
        }

        private static void CheckGameClock(Hud hud, VisualElement root)
        {
            var clock = typeof(Hud).GetField("gameTime", Flags);
            var elapsed = typeof(Hud).GetField("elapsedGameClockSeconds", Flags);
            var initialized = typeof(Hud).GetField("gameClockInitialized", Flags);
            DateTime savedTime = (DateTime)clock.GetValue(hud);
            double savedElapsed = (double)elapsed.GetValue(hud);
            float previousScale = Time.timeScale;
            try
            {
                initialized.SetValue(hud, false);
                Invoke(hud, "InitializeGameClock", new DateTime(2026, 12, 31, 23, 59, 38));
                var start = new DateTime(2026, 12, 31, 23, 59, 0);
                Assert((DateTime)clock.GetValue(hud) == start, "Clock initializes at minute precision");
                Invoke(hud, "AdvanceGameClock", .75f);
                Assert((DateTime)clock.GetValue(hud) == start, "Fractional seconds wait for a complete second");
                Invoke(hud, "AdvanceGameClock", .5f);
                Assert((DateTime)clock.GetValue(hud) == start.AddMinutes(1)
                    && root.Q<Label>("Date").text == "2027_01_01", "Game clock rolls date across year end");
                var orbit = root.Q<TimeOrbitElement>("TimeOrbit");
                Assert((float)typeof(TimeOrbitElement).GetField("dayProgress", Flags).GetValue(orbit) == 0,
                    "Midnight starts day progress");
                Time.timeScale = 0;
                Invoke(hud, "AdvanceGameClock", .75f * Time.timeScale);
                Assert((DateTime)clock.GetValue(hud) == start.AddMinutes(1), "Scaled clock stays still while paused");
                Time.timeScale = 1;
                Invoke(hud, "AdvanceGameClock", .75f);
                Assert((DateTime)clock.GetValue(hud) == start.AddMinutes(2), "Resume preserves fractional time");
                Invoke(hud, "AdvanceGameClock", 3.5f);
                Assert((DateTime)clock.GetValue(hud) == start.AddMinutes(5), "Long frames advance every complete second");
                hud.Init();
                Invoke(hud, "AdvanceGameClock", .5f);
                Assert((DateTime)clock.GetValue(hud) == start.AddMinutes(6), "UI reinitialization preserves clock and remainder");
                Invoke(hud, "RefreshTelemetry", start.AddYears(-1), true);
                Assert((DateTime)clock.GetValue(hud) == start.AddMinutes(6)
                    && root.Q<Label>("Date").text == "2027_01_01", "System clock changes do not overwrite game time");
                // A minute tick must not replace the existing date string on the same day.
                string dateText = root.Q<Label>("Date").text;
                Invoke(hud, "AdvanceGameClock", 1f);
                Assert(ReferenceEquals(dateText, root.Q<Label>("Date").text), "Same-day ticks retain the date text");
                initialized.SetValue(hud, false);
                Invoke(hud, "InitializeGameClock", new DateTime(2028, 2, 28, 23, 59, 0));
                Invoke(hud, "AdvanceGameClock", 1f);
                Assert(root.Q<Label>("Date").text == "2028_02_29", "DateTime handles leap-day rollover");
            }
            finally
            {
                Time.timeScale = previousScale;
                clock.SetValue(hud, savedTime);
                elapsed.SetValue(hud, savedElapsed);
                initialized.SetValue(hud, true);
                Invoke(hud, "InitializeGameClock", DateTime.Now);
            }
        }

        private static void CheckTelemetry(Hud hud, VisualElement root, Scene scene)
        {
            CheckGameClock(hud, root);
            var midnight = new DateTime(2026, 10, 16);
            Assert(root.Q<Label>("Altitude").text == "--m", "Missing map displays placeholders");

            var mapObject = new GameObject("HUD coordinate map");
            SceneManager.MoveGameObjectToScene(mapObject, scene);
            var map = mapObject.AddComponent<MapTestSceneController>();
            var robotObject = new GameObject("HUD coordinate robot");
            SceneManager.MoveGameObjectToScene(robotObject, scene);
            var robot = robotObject.AddComponent<RobotMover>();
            robot.enabled = false;
            var heights = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            heights.SetPixels(Enumerable.Repeat(Color.white * .5f, 4).ToArray());
            heights.Apply();
            var previousBootstrap = HeightMapPlayerSceneBootstrap.inst;
            var bootstrapObject = new GameObject("Telemetry bootstrap");
            bootstrapObject.SetActive(false);
            SceneManager.MoveGameObjectToScene(bootstrapObject, scene);
            var bootstrap = bootstrapObject.AddComponent<HeightMapPlayerSceneBootstrap>();
            bootstrap.map = map;
            bootstrap.robot = robot;
            HeightMapPlayerSceneBootstrap.inst = bootstrap;
            try
            {
                using (var fixture = new TiledMapTestFixture(map, heights, 100, 2, 70))
                {
                    typeof(TiledLevelAsset).GetField("longitudeRange", Flags).SetValue(map.LevelAsset, new Vector2(-110, -100));
                    typeof(TiledLevelAsset).GetField("latitudeRange", Flags).SetValue(map.LevelAsset, new Vector2(30, 40));
                    map.transform.position = new Vector3(50, -20, 0);
                    robot.transform.position = map.WorldBounds.min;
                    Invoke(hud, "RefreshTelemetry", midnight, true);
                    Assert(root.Q<Label>("Longitude").text == "110.00000°"
                        && root.Q<Label>("Latitude").text == "30.00000°", "Southwest uses minimum coordinates");
                    Assert(root.Q<Label>("LongitudeHemisphere").text == "W", "Negative longitude uses west");
                    Assert(map.TrySampleWorldPosition(robot.MapPosition, out _, out float height)
                        && root.Q<Label>("Altitude").text == height.ToString("F0") + "m", "Altitude uses map height in meters");
                    robot.transform.position = map.WorldBounds.max;
                    Invoke(hud, "RefreshTelemetry", midnight.AddMilliseconds(99), false);
                    Assert(root.Q<Label>("Longitude").text == "110.00000°", "Coordinates wait for 100 milliseconds");
                    Invoke(hud, "RefreshTelemetry", midnight.AddMilliseconds(100), false);
                    Assert(root.Q<Label>("Longitude").text == "100.00000°"
                        && root.Q<Label>("Latitude").text == "40.00000°", "Northeast uses maximum coordinates");
                    robot.transform.position = map.WorldBounds.center;
                    Invoke(hud, "RefreshTelemetry", midnight.AddMilliseconds(200), false);
                    Assert(root.Q<Label>("Longitude").text == "105.00000°"
                        && root.Q<Label>("Latitude").text == "35.00000°", "Map center interpolates both axes");
                    robot.transform.position = map.WorldBounds.max + Vector3.one;
                    Invoke(hud, "RefreshTelemetry", midnight.AddMilliseconds(300), false);
                    Assert(root.Q<Label>("Altitude").text == "--m"
                        && root.Q<Label>("Longitude").text == "--.-----°"
                        && root.Q<Label>("Latitude").text == "--.-----°", "Outside map clears all coordinates");
                    robot.transform.position = map.WorldBounds.center;
                    Invoke(hud, "RefreshTelemetry", midnight.AddMilliseconds(150), false);
                    Assert(root.Q<Label>("Altitude").text != "--m", "Clock moving backward recovers refresh immediately");
                    CheckPhotoLocation(map, robot, scene);
                }
            }
            finally
            {
                HeightMapPlayerSceneBootstrap.inst = previousBootstrap;
                Object.DestroyImmediate(bootstrapObject);
                Object.DestroyImmediate(robotObject);
                Object.DestroyImmediate(mapObject);
                Object.DestroyImmediate(heights);
                Invoke(hud, "RefreshTelemetry", DateTime.Now, true);
            }
        }

        private static void CheckPhotoLocation(MapTestSceneController map, RobotMover robot, Scene scene)
        {
            var subjectObject = new GameObject("Location snapshot subject");
            subjectObject.SetActive(false);
            SceneManager.MoveGameObjectToScene(subjectObject, scene);
            var resultObject = new GameObject("Location snapshot UI");
            resultObject.SetActive(false);
            SceneManager.MoveGameObjectToScene(resultObject, scene);
            try
            {
                var subject = subjectObject.AddComponent<AnimalPhotoSubject>();
                var serializedSubject = new SerializedObject(subject);
                serializedSubject.FindProperty("species").enumValueIndex = (int)AnimalSpecies.PileatedWoodpecker;
                serializedSubject.ApplyModifiedPropertiesWithoutUndo();
                subject.transform.position = map.WorldBounds.max;
                robot.transform.position = map.WorldBounds.min;
                var controller = robot.gameObject.AddComponent<PhotoModeController>();
                controller.enabled = false;
                var resultUI = resultObject.AddComponent<PhotoResultUI>();
                resultUI.Library = AssetDatabase.LoadAssetAtPath<PhotoLibrary>("Assets/Data/AnimalPhoto/AnimalPhotoLibrary.asset");
                typeof(PhotoResultUI).GetField("map", Flags).SetValue(resultUI, map);
                typeof(PhotoResultUI).GetField("controller", Flags).SetValue(resultUI, controller);
                var create = typeof(PhotoResultUI).GetMethod("CreateSnapshot", Flags);
                var snapshot = (PhotoResultSnapshot)create.Invoke(resultUI, new object[] { subject, null, .5f });
                AnimalPhotoLibrary identity = resultUI.Library.GetAnimal(subject.Species);
                Assert(snapshot.DisplayName == identity.DisplayName && snapshot.ScientificName == identity.ScientificName
                    && snapshot.RegionName == identity.RegionName && snapshot.Species == subject.Species,
                    "Photo identity comes from the species library");
                Assert(snapshot.HasGeographicCoordinates && snapshot.Latitude == 30 && snapshot.Longitude == -110,
                    "Photo samples player coordinates rather than animal coordinates");
                Assert(snapshot.MapPositionMeters == map.MapSizeMeters,
                    "Photo retains separate animal map coordinates");
                Assert(map.TrySampleWorldPosition(robot.MapPosition, out _, out float height)
                    && snapshot.HasHeight && snapshot.HeightMeters == height, "Photo samples player ground elevation");
                robot.transform.position = map.WorldBounds.max + Vector3.one;
                Assert(snapshot.Latitude == 30 && snapshot.Longitude == -110 && snapshot.HeightMeters == height,
                    "Captured location remains frozen after player moves");

                var view = resultObject.AddComponent<PhotoResultView>();
                var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                    "Assets/Prefabs/Resources/UI/Photo/PhotoResult.uxml").CloneTree();
                typeof(PhotoResultView).GetField("root", Flags).SetValue(view, tree);
                Invoke(view, "UpdateLocation", snapshot);
                Assert(tree.Q<Label>("longitude").text == "Longitude 110.00000\u00b0 W"
                    && tree.Q<Label>("latitude").text == "Latitude 30.00000\u00b0 N",
                    "Photo labels display captured geographic coordinates and hemispheres");
                Assert(tree.Q<Label>("altitude").text == $"{height:0}m", "Photo label displays captured altitude");
                var missing = (PhotoResultSnapshot)create.Invoke(resultUI, new object[] { subject, null, .5f });
                Assert(!missing.HasHeight && !missing.HasGeographicCoordinates,
                    "Photo marks unavailable player location as missing");
                Invoke(view, "UpdateLocation", missing);
                Assert(tree.Q<Label>("longitude").text == "Longitude --"
                    && tree.Q<Label>("latitude").text == "Latitude --"
                    && tree.Q<Label>("altitude").text == "- m", "Photo clears stale location values");
            }
            finally
            {
                Object.DestroyImmediate(resultObject);
                Object.DestroyImmediate(subjectObject);
            }
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
        private static void Step(Hud hud, float seconds) => Invoke(hud, "AdvanceInventoryAnimation", seconds);
        private static void Invoke(object obj, string method, params object[] args)
            => obj.GetType().GetMethod(method, Flags).Invoke(obj, args);
        private static void ValidateLayout(VisualElement root)
        {
            var panel = root.panel;
            if (panel == null) throw new Exception("Missing runtime panel.");
            panel.GetType().GetMethod("ValidateLayout", Flags).Invoke(panel, null);
        }
        private static void CompareGeometry(Hud hud, VisualElement root, string label)
        {
            Assert(hud.TryGetRingScreenGeometry(out Vector2 actualCenter, out float actualRadius), label + " is ready");
            var ring = root.Q<ViewRingElement>("InnerRing");
            Vector2 center = ring.LocalToWorld(ring.contentRect.center);
            Vector2 edge = ring.LocalToWorld(ring.contentRect.center + Vector2.right * ring.Radius);
            Vector2 origin = RuntimePanelUtils.ScreenToPanel(root.panel, Vector2.zero);
            Vector2 end = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(Screen.width, Screen.height));
            Vector2 units = new Vector2((end.x - origin.x) / Screen.width, (end.y - origin.y) / Screen.height);
            Vector2 expectedCenter = new Vector2((center.x - origin.x) / units.x, Screen.height - (center.y - origin.y) / units.y);
            float expectedRadius = Vector2.Distance(center, edge) / units.x;
            Assert(Vector2.Distance(actualCenter, expectedCenter) < 1 && Mathf.Abs(actualRadius - expectedRadius) < 1,
                label + " matches rendered element");
        }
        private static void CompareEdgeAnchors(VisualElement root, bool open = false)
        {
            Rect screen = root.worldBound;
            float fit = Mathf.Min(root.layout.width / 1920f, root.layout.height / 1080f);
            Assert(root.Q("InventoryEntry") == null, "Separate inventory entry is removed");
            var inventory = root.Q("Inventory");
            if (!open)
                Assert(Mathf.Abs(inventory.worldBound.xMax - screen.xMin
                    - (root.Q("HudView").layout.x + inventory.layout.xMax) * fit) < 1
                    && inventory.resolvedStyle.visibility == Visibility.Visible,
                    "Closed inventory strip follows the authored left position at " + screen.size);
            var timeElement = root.Q("Time");
            Rect time = timeElement.worldBound;
            Assert(Mathf.Abs(screen.xMax - time.xMax - 38 * fit) < 1
                && Mathf.Abs(time.yMin - screen.yMin - (open ? -timeElement.layout.height - 4 : timeElement.layout.y) * fit) < 1,
                "Time anchors to the top-right and exits upward at " + screen.size);
            Rect coordinates = root.Q("Coordinates").worldBound;
            Assert(Mathf.Abs(screen.xMax - coordinates.xMax - 55 * fit) < 1
                && Mathf.Abs(screen.yMax - coordinates.yMax - 40 * fit) < 1,
                "Coordinates anchor to the screen bottom-right at " + screen.size);
            Rect compass = root.Q("CompassHost").worldBound;
            Assert(Mathf.Abs(compass.center.x - screen.center.x) < 1
                && Mathf.Abs(compass.yMin - screen.yMin - root.Q("CompassHost").layout.y) < 1,
                "Compass anchors to the screen top-center at " + screen.size);
            Assert(Mathf.Abs(compass.width - root.Q("CompassHost").layout.width * fit) < 1,
                "Compass retains uniform design scaling at " + screen.size);
            var statusElement = root.Q("Status");
            Rect status = statusElement.worldBound;
            Assert(Mathf.Abs(status.xMin - screen.xMin - statusElement.layout.x * fit) < 1
                && Mathf.Abs(status.yMin - screen.yMin - (open ? -statusElement.layout.height - 4 : statusElement.layout.y) * fit) < 1,
                "Status uses the screen anchor and panel-space animation at " + screen.size);
        }
        private static void CompareStatusMotion(VisualElement root, float progress)
        {
            float fit = Mathf.Min(root.layout.width / 1920f, root.layout.height / 1080f);
            float motion = Mathf.SmoothStep(0, 1, Mathf.Clamp01(progress));
            var status = root.Q("Status");
            Vector2 expected = root.worldBound.position + new Vector2(status.layout.x,
                Mathf.Lerp(status.layout.y, -status.layout.height - 4, motion)) * fit;
            Assert(Vector2.Distance(status.worldBound.position, expected) < 1,
                "Status follows the reversible upward animation at progress " + progress + " and size " + root.layout.size);
        }
        private static void RenderAndCapture(VisualElement root, RenderTexture target, string filename)
        {
            // A bright backing makes accidental black coverage inside the frame visible in the preview.
            var previous = RenderTexture.active;
            RenderTexture.active = target; GL.Clear(true, true, new Color(.035f, .065f, .07f));
            var repaint = root.panel.GetType().GetMethods(Flags).FirstOrDefault(m => m.Name == "Repaint" && m.GetParameters().Length == 1);
            repaint?.Invoke(root.panel, new object[] { new Event { type = EventType.Repaint } });
            var render = root.panel.GetType().GetMethod("Render", Flags, null, Type.EmptyTypes, null);
            if (render == null) throw new Exception("Missing runtime panel Render method.");
            render.Invoke(root.panel, null);
            RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
            Assert(image.GetPixel(5, target.height - 5).maxColorComponent < .02f, "Frame masks the scene outside its border");
            Assert(image.GetPixel((int)(root.layout.width / 2), target.height - (int)(root.layout.height / 2)).maxColorComponent > .02f,
                "Frame keeps the scene visible inside its border");
            if (Mathf.Abs(root.layout.width - 1280) < 1 && Mathf.Abs(root.layout.height - 720) < 1)
            {
                // Sample the right margin; the left interior now intentionally contains the black inventory strip.
                Assert(image.GetPixel(1280 - 10, target.height - 360).maxColorComponent < .02f,
                    "Scaled margin masks pixels outside the 1280x720 frame");
                Assert(image.GetPixel(1280 - 16, target.height - 360).maxColorComponent > .02f,
                    "Design margin 20 scales to 13.33 at 1280x720 rather than staying at 20 pixels");
            }
            var folder = Path.Combine(Path.GetTempPath(), "AnimalGameMainUIValidation");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, filename), image.EncodeToPNG());
            RenderTexture.active = previous;
            Object.DestroyImmediate(image);
        }
        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception("Main UI check failed: " + message);
            checks++;
        }
    }
}
