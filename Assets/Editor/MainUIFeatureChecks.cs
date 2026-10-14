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
                CheckInventory(root, hud);
                CheckTime(root, hud);
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
