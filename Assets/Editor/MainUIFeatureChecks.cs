using System;
using System.IO;
using System.Reflection;
using AnimalGame.MainUI;
using AnimalGame.MainUI.Inventory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using Hud = AnimalGame.MainUI.MainUI;

namespace AnimalGame.Editor
{
    /// <summary>Checks the current authored HUD in a disposable preview scene.</summary>
    public static class MainUIFeatureChecks
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [MenuItem("Animal Game/Validation/Run Main UI Feature Checks")]
        public static void Run()
        {
            float previousTimeScale = Time.timeScale;
            var scene = EditorSceneManager.NewPreviewScene();
            PanelSettings settings = null;
            RenderTexture texture = null;
            try
            {
                var obj = new GameObject("Main UI feature checks");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(obj, scene);
                settings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Prefabs/Resources/UI/Main/MainUISetting.asset"));
                texture = new RenderTexture(1920, 1080, 24); texture.Create(); settings.targetTexture = texture;
                var document = obj.AddComponent<UIDocument>(); document.panelSettings = settings;
                document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Prefabs/Resources/UI/Main/MainUIDoc.uxml");
                var hud = obj.AddComponent<Hud>(); hud.Init();
                var root = document.rootVisualElement;
                root.style.width = 1920; root.style.height = 1080;
                Settle(root, hud);
                Assert(root.Q("InventoryEntry") == null, "Separate inventory entry is removed");
                Assert(root.Q("Inventory").resolvedStyle.visibility == Visibility.Visible,
                    "Inventory is visible immediately after initialization");
                CheckInventory(root, hud);
                CheckTime(root, hud);
                CheckSound(root, hud);
                CheckAnimation(root, hud, texture);
                Capture(root, texture, "mainui-features.png");
                CheckRingOcclusion(root, texture);
                Debug.Log("MAIN UI FEATURE CHECKS PASSED");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                if (texture != null) { texture.Release(); Object.DestroyImmediate(texture); }
                if (settings != null) Object.DestroyImmediate(settings);
                Time.timeScale = previousTimeScale;
            }
        }

        private static void CheckInventory(VisualElement root, Hud hud)
        {
            hud.OpenInventory(); Invoke(hud, "AdvanceInventoryAnimation", 1f); Settle(root, hud);
            var grid = root.Q("InventoryGrid");
            Assert(grid.childCount == 8, "Inventory has eight slots");
            for (int i = 0; i < 8; i++)
            {
                var slot = grid[i];
                if (i % 2 == 0) Assert(Mathf.Abs(slot.layout.xMax - grid[i + 1].layout.xMin) < .01f, "Adjacent columns touch");
                if (i < 6) Assert(Mathf.Abs(slot.layout.yMax - grid[i + 2].layout.yMin) < .01f, "Adjacent rows touch");
                hud.SelectItem(i); Layout(root);
                var selection = root.Q<InventorySelectionElement>("InventorySelection");
                Assert(Vector2.Distance(selection.worldBound.position, slot.worldBound.position) < .1f
                    && Vector2.Distance(selection.worldBound.size, slot.worldBound.size) < .1f, "Selection matches slot " + i);
            }
            Assert(root.Q<Label>(className: "inventory-title").text == "CARGO", "Cargo title");
            Assert(root.Q(className: "cargo-mark") != null, "Cargo square");
            hud.SelectItem(2); Layout(root);
        }

        private static void CheckTime(VisualElement root, Hud hud)
        {
            var time = root.Q("Time");
            var orbit = root.Q<TimeOrbitElement>("TimeOrbit");
            var date = root.Q<Label>("Date");
            Assert(Mathf.Abs(time.layout.width - time.layout.height) < .01f, "Time uses square bounds");
            Assert(Vector2.Distance(orbit.worldBound.center, date.worldBound.center) < .1f, "Date is centered in orbit");
            Assert(date.text == "2026_01_01", "Unbound clock retains the default date");
            hud.SetDate(new DateTime(2026, 10, 12));
            Assert(date.text == "2026_10_12", "Date interface formats yyyy_mm_dd");
            hud.SetDate(new DateTime(2026, 1, 1));
        }

        private static void CheckSound(VisualElement root, Hud hud)
        {
            var sound = root.Q<SoundElement>("Sound");
            Assert(sound != null && sound.chamfer == 36, "Sound imports its chamfer attribute");
            var time = root.Q("Time");
            foreach (Vector2 size in new[] { new Vector2(1280, 720), new Vector2(1440, 1080), new Vector2(2560, 1080), new Vector2(1920, 1080) })
            {
                root.style.width = size.x; root.style.height = size.y; Settle(root, hud);
                float fit = Mathf.Min(size.x / 1920, size.y / 1080);
                Assert(Vector2.Distance(sound.worldBound.size, time.worldBound.size) < .1f, "Sound and Time share a size at " + size);
                Assert(Mathf.Abs(sound.worldBound.width - 230 * fit) < .1f, "Sound scales at " + size);
                Assert(Mathf.Abs(time.worldBound.xMin - sound.worldBound.xMax - 24 * fit) < .1f
                    && Mathf.Abs(sound.worldBound.yMin - time.worldBound.yMin) < .1f, "Sound stays left of Time at " + size);
            }
        }

        private static void CheckAnimation(VisualElement root, Hud hud, RenderTexture texture)
        {
            var inventory = root.Q("Inventory");
            var status = root.Q("Status");
            foreach (Vector2 size in new[] { new Vector2(1280, 720), new Vector2(1440, 1080), new Vector2(2560, 1080), new Vector2(1920, 1080) })
            {
                hud.CloseInventory(); Invoke(hud, "AdvanceInventoryAnimation", 1f);
                root.style.width = size.x; root.style.height = size.y; Settle(root, hud);
                Rect closed = inventory.worldBound;
                float fit = Mathf.Min(size.x / 1920, size.y / 1080);
                Assert(Mathf.Abs(closed.xMax - root.worldBound.xMin - 80 * fit) < .1f
                    && inventory.resolvedStyle.visibility == Visibility.Visible, "Closed inventory exposes its right strip at " + size);
                Rect statusClosed = status.worldBound;
                float ringClosedX = root.Q("ViewRing").worldBound.center.x;
                hud.OpenInventory(); Invoke(hud, "AdvanceInventoryAnimation", .125f); Settle(root, hud);
                Rect quarter = inventory.worldBound;
                Assert(quarter.xMin > closed.xMin && Mathf.Abs(quarter.yMin - closed.yMin) < .1f, "Inventory moves right without moving vertically");
                Assert(status.worldBound.yMax < statusClosed.yMax && Mathf.Abs(status.worldBound.xMin - statusClosed.xMin) < .1f, "Status moves straight up");
                Assert(Mathf.Abs(root.Q("ViewRing").worldBound.center.x - ringClosedX) < .1f, "Ring retains its delayed horizontal motion");
                Invoke(hud, "AdvanceInventoryAnimation", .125f); Settle(root, hud);
                hud.CloseInventory(); Invoke(hud, "AdvanceInventoryAnimation", .125f); Settle(root, hud);
                Assert(Vector2.Distance(inventory.worldBound.position, quarter.position) < .1f, "Reversing retraces the same inventory path");
                hud.OpenInventory(); Invoke(hud, "AdvanceInventoryAnimation", 1f); Settle(root, hud);
                Assert(Mathf.Abs(inventory.worldBound.center.x - root.worldBound.xMin - size.x * .25f) < .1f, "Open inventory reaches the screen quarter");
                Assert(Mathf.Abs(inventory.worldBound.yMin - closed.yMin) < .1f, "Open inventory retains authored vertical position");
                foreach (string name in new[] { "Status", "Time", "Sound" })
                    Assert(root.Q(name).worldBound.yMax < root.worldBound.yMin, name + " exits above the screen");
                hud.CloseInventory(); Invoke(hud, "AdvanceInventoryAnimation", 1f); Settle(root, hud);
                Assert(Vector2.Distance(inventory.worldBound.position, closed.position) < .1f
                    && Vector2.Distance(status.worldBound.position, statusClosed.position) < .1f, "Closing restores the original pose");
            }
            // An artist can change the initial pose in UI Builder without runtime overwriting it.
            float left = inventory.layout.x, top = inventory.layout.y;
            inventory.style.left = left - 70; inventory.style.top = top + 23; Settle(root, hud);
            Rect edited = inventory.worldBound;
            hud.OpenInventory(); Invoke(hud, "AdvanceInventoryAnimation", 1f); Settle(root, hud);
            Assert(Mathf.Abs(inventory.worldBound.yMin - edited.yMin) < .1f, "Edited top controls the slide path");
            hud.CloseInventory(); Invoke(hud, "AdvanceInventoryAnimation", 1f); Settle(root, hud);
            Assert(Vector2.Distance(inventory.worldBound.position, edited.position) < .1f, "Edited left remains the closed endpoint");
            inventory.style.left = left; inventory.style.top = top; Settle(root, hud);
            Capture(root, texture, "mainui-features-closed.png");
            hud.OpenInventory(); Invoke(hud, "AdvanceInventoryAnimation", .25f); Settle(root, hud);
            Capture(root, texture, "mainui-features-half.png");
            Invoke(hud, "AdvanceInventoryAnimation", 1f); Settle(root, hud);
        }

        internal static void Settle(VisualElement root, Hud hud)
        {
            for (int i = 0; i < 3; i++) { Layout(root); Invoke(hud, "ApplyLayout"); }
            Layout(root);
            var scheduler = root.panel.GetType().GetProperty("scheduler", Flags).GetValue(root.panel);
            scheduler.GetType().GetMethod("UpdateScheduledEvents", Flags).Invoke(scheduler, null);
            Layout(root);
        }
        internal static void Layout(VisualElement root) => root.panel.GetType().GetMethod("ValidateLayout", Flags).Invoke(root.panel, null);
        internal static void Invoke(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Flags).Invoke(obj, args);
        internal static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void CheckRingOcclusion(VisualElement root, RenderTexture target)
        {
            var inner = root.Q<ViewRingElement>("InnerRing");
            var outer = root.Q<ViewRingElement>("OuterRing");
            Assert(outer.Radius > inner.Radius && outer.tickLength > outer.Radius - inner.Radius,
                "Triangles reach across the inner ring");
            // At the top axis the triangle's black interior must erase the previously drawn inner circle.
            Vector2 point = inner.LocalToWorld(inner.contentRect.center + Vector2.up * -inner.Radius);
            var previous = RenderTexture.active; RenderTexture.active = target;
            var pixel = new Texture2D(1, 1, TextureFormat.RGB24, false);
            pixel.ReadPixels(new Rect(Mathf.Round(point.x), target.height - Mathf.Round(point.y), 1, 1), 0, 0); pixel.Apply();
            Assert(pixel.GetPixel(0, 0).maxColorComponent < .1f, "Triangle black fill occludes inner ring");
            Object.DestroyImmediate(pixel); RenderTexture.active = previous;
        }

        internal static void Capture(VisualElement root, RenderTexture target, string filename)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = target; GL.Clear(true, true, new Color(.035f, .065f, .07f));
            root.panel.GetType().GetMethod("Repaint", Flags, null, new[] { typeof(Event) }, null).Invoke(root.panel, new object[] { new Event { type = EventType.Repaint } });
            root.panel.GetType().GetMethod("Render", Flags, null, Type.EmptyTypes, null).Invoke(root.panel, null);
            RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
            var folder = Path.Combine(Path.GetTempPath(), "MainUIFeatureValidation"); Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, filename), image.EncodeToPNG());
            Object.DestroyImmediate(image); RenderTexture.active = previous;
        }
        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}
