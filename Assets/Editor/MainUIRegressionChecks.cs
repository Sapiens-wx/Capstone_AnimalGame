using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AnimalGame.MainUI;
using AnimalGame.MainUI.Inventory;
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
                Assert(root.Q<ViewRingElement>("InnerRing") != null, "Custom UXML elements import");
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
                    Assert(root.Q("InventoryEntry").resolvedStyle.visibility == Visibility.Visible,
                        "Reversed inventory animation restores the entry at " + size);
                }
                root.style.width = 1920; root.style.height = 1080;
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
                hud.SetItem(7, new InventoryItem()); ValidateLayout(root);
                Assert(root.Query<EmptyInventoryElement>().ToList().Count == 7, "Occupied item removes only its empty marker");
                hud.SetItem(7, null);

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
                Assert(root.Q("Inventory").resolvedStyle.visibility == Visibility.Hidden, "Closed panel hidden after return");
                Assert(!allSlots[0].enabledInHierarchy, "Closed inventory buttons cannot interact");
                CompareGeometry(hud, root, "Restored closed pose");
                CompareEdgeAnchors(root);
                hud.SetPhotoPose(1.2f, new Vector2(1920, 1080), new Vector2(60, -30), .5f);
                ValidateLayout(root); CompareGeometry(hud, root, "Photo zoom and translation");
                var photoEntry = root.Q("InventoryEntry").worldBound;
                Vector2 photoEntryCenter = new Vector2(18 + 55, 1080 - 5 - 43);
                photoEntryCenter = new Vector2(960, 540) + (photoEntryCenter - new Vector2(960, 540)) * 1.2f
                    + new Vector2(60, -30) + root.worldBound.position;
                Assert(Vector2.Distance(photoEntry.center, photoEntryCenter) < 1,
                    "Corner HUD follows the Stage photo transform exactly once");
                hud.TryGetRingScreenGeometry(out Vector2 savedCenter, out float savedRadius);
                document.enabled = false;
                Assert(hud.TryGetRingScreenGeometry(out Vector2 hiddenCenter, out float hiddenRadius)
                    && hiddenCenter == savedCenter && hiddenRadius == savedRadius, "Hidden document retains last valid geometry");
                document.enabled = true; hud.Init(); ValidateLayout(document.rootVisualElement);
                Assert(document.rootVisualElement.Query<Button>().ToList().Count == 8, "Rebuilt document rebinds exactly eight slots");
                hud.SetPhotoPose(1, new Vector2(1920, 1080), Vector2.zero, 1);
                Invoke(hud, "ApplyLayout"); ValidateLayout(document.rootVisualElement);
                RenderAndCapture(document.rootVisualElement, texture, "mainui-closed.png");
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
            Rect entry = root.Q("InventoryEntry").worldBound;
            Assert(Mathf.Abs(entry.xMin - screen.xMin - 18 * fit) < 1
                && Mathf.Abs(screen.yMax - entry.yMax - 5 * fit) < 1,
                "Inventory entry anchors to the screen bottom-left at " + screen.size);
            Assert(Mathf.Abs(entry.width - 110 * fit) < 1 && Mathf.Abs(entry.height - 86 * fit) < 1,
                "Inventory entry retains uniform design scaling at " + screen.size);
            Rect time = root.Q("Time").worldBound;
            Assert(Mathf.Abs(screen.xMax - time.xMax - 38 * fit) < 1
                && Mathf.Abs(time.yMin - screen.yMin - 52 * fit) < 1,
                "Time anchors to the screen top-right at " + screen.size);
            Rect coordinates = root.Q("Coordinates").worldBound;
            Assert(Mathf.Abs(screen.xMax - coordinates.xMax - 55 * fit) < 1
                && Mathf.Abs(screen.yMax - coordinates.yMax - 40 * fit) < 1,
                "Coordinates anchor to the screen bottom-right at " + screen.size);
            Rect compass = root.Q("CompassHost").worldBound;
            Assert(Mathf.Abs(compass.center.x - screen.center.x) < 1
                && Mathf.Abs(compass.yMin - screen.yMin - root.Q("CompassHost").layout.y) < 1,
                "Compass anchors to the screen top-center at " + screen.size);
            Assert(Mathf.Abs(compass.width - 392 * fit) < 1,
                "Compass retains uniform design scaling at " + screen.size);
            Rect status = root.Q("Status").worldBound;
            float displacementX = 0;
            if (open)
            {
                float viewportX = (root.layout.width - 1920 * fit) * .5f;
                displacementX = root.layout.width * (2f / 3) - viewportX - 960 * fit + 400 * fit;
            }
            Assert(Mathf.Abs(status.xMin - screen.xMin - 42 * fit - displacementX) < 1
                && Mathf.Abs(status.yMin - screen.yMin - (44 + (open ? 51 : 0)) * fit) < 1,
                "Status uses the screen anchor and panel-space animation at " + screen.size);
        }
        private static void CompareStatusMotion(VisualElement root, float progress)
        {
            float fit = Mathf.Min(root.layout.width / 1920f, root.layout.height / 1080f);
            float horizontal = Mathf.SmoothStep(0, 1, Mathf.Clamp01(progress * 2 - 1));
            float vertical = Mathf.SmoothStep(0, 1, Mathf.Clamp01(progress * 2));
            float viewportX = (root.layout.width - 1920 * fit) * .5f;
            Vector2 expected = root.worldBound.position + new Vector2(
                42 * fit + (root.layout.width * (2f / 3) - viewportX - 960 * fit + 400 * fit) * horizontal,
                (44 + 51 * vertical) * fit);
            Assert(Vector2.Distance(root.Q("Status").worldBound.position, expected) < 1,
                "Status follows the two-phase animation at progress " + progress + " and size " + root.layout.size);
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
                Assert(image.GetPixel(10, target.height - 360).maxColorComponent < .02f,
                    "Scaled margin masks pixels outside the 1280x720 frame");
                Assert(image.GetPixel(16, target.height - 360).maxColorComponent > .02f,
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
