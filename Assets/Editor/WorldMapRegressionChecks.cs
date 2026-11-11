using System;
using System.Reflection;
using AnimalGame.MapTest;
using AnimalGame.World.Map;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    public static class WorldMapRegressionChecks
    {
        [MenuItem("Animal Game/Checks/World Map")]
        public static void Run()
        {
            CheckCursorAndPins();
            CheckAssets();
            CheckGpuContours();
            Debug.Log("PASS: world map cursor bounds, zoom anchoring, pin FIFO/deletion/snap escape, prefab references, GPU 14m interval, screen line widths and height emphasis.");
        }

        private static void CheckCursorAndPins()
        {
            var state = new MapViewState();
            var screen = new Vector2(1000, 1000);
            var map = Vector2.one * 1000;
            state.Open(Vector2.one * 500, Vector2.one * 100, map);
            state.Move(new Vector2(400, 400), screen, map);
            Require((state.Center - Vector2.one * 515).sqrMagnitude < .001f, "Cursor did not pan at the central half-screen rectangle.");
            var before = state.ToScreen(state.Cursor, screen);
            state.Resize(Vector2.one * 200);
            Require((state.ToScreen(state.Cursor, screen) - before).sqrMagnitude < .001f, "Zoom moved the cursor screen anchor.");
            state.Open(Vector2.one * 100, Vector2.one * 100, map);
            for (int i = 0; i < 4; i++)
            {
                state.Move(new Vector2(100, 0), screen, map);
                state.Snap(screen, 7 * 1.4f);
                state.Submit();
            }
            Require(state.Pins.Count == 3 && Mathf.Abs(state.Pins[0].x - 120) < .01f
                && Mathf.Abs(state.Pins[2].x - 140) < .01f, "Pins did not replace the oldest in FIFO order.");
            state.Submit();
            Require(state.Pins.Count == 2, "Submitting on selected pin did not delete it.");
            state.Move(new Vector2(-95, 0), screen, map);
            state.Snap(screen, 9.8f);
            Require(state.SelectedPin == 1, "Cursor did not snap within 1.4 radii.");
            state.Move(new Vector2(30, 0), screen, map);
            state.Snap(screen, 9.8f);
            Require(state.SelectedPin == -1, "Cursor could not escape the pin snap.");
            state.Move(Vector2.one * 100000, screen, map);
            Require(state.Cursor == map, "Cursor escaped the map bounds.");
            state.Open(Vector2.one * 50, Vector2.one * 100, map);
            Require(state.Pins.Count == 2, "Reopening cleared session pins.");
            state.Resize(map);
            state.FitMap(map);
            Require(state.Center == map * .5f, "Whole-map zoom did not center the map.");
            Vector2 screenCursor = state.ToScreen(state.Cursor, screen);
            Require(screenCursor.x >= 250 && screenCursor.x <= 750 && screenCursor.y >= 250 && screenCursor.y <= 750,
                "Whole-map zoom moved the cursor outside the central half-screen rectangle.");
        }

        private static void CheckAssets()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Resources/UI/Map/WorldMapUI.prefab");
            Require(prefab != null && prefab.GetComponent<WorldMapUI>() != null, "Map prefab is missing.");
            var doc = prefab.GetComponent<UIDocument>();
            Require(doc.panelSettings != null && doc.visualTreeAsset != null, "UIDocument references are missing.");
            var tree = doc.visualTreeAsset.CloneTree();
            Require(tree.Q("world-map") != null && tree.Q("map-cursor") != null
                && tree.Q("pin-2") != null, "Map UXML is incomplete.");
            foreach (string path in new[] { "Assets/Shaders/WorldMapContours.shader", "Assets/Shaders/DynamicHeightContours.shader" })
                Require(!ShaderUtil.ShaderHasError(AssetDatabase.LoadAssetAtPath<Shader>(path)), path + " has shader errors.");
        }

        private static void CheckGpuContours()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var go = new GameObject("World map regression fixture");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            var source = new Texture2D(128, 128, TextureFormat.RGBA32, false, true);
            var pixels = new Color[128 * 128];
            for (int y = 0; y < 128; y++)
            for (int x = 0; x < 128; x++)
            {
                float value = x / 127f;
                pixels[y * 128 + x] = new Color(value, value, value, 1);
            }
            source.SetPixels(pixels); source.Apply();
            var controller = go.AddComponent<MapTestSceneController>();
            var type = typeof(WorldMapUI).Assembly.GetType("AnimalGame.World.Map.WorldMapRenderer", true);
            var renderer = (IDisposable)Activator.CreateInstance(type, new object[] { Shader.Find("AnimalGame/World Map Contours") });
            try
            {
                using (var fixture = new TiledMapTestFixture(controller, source, 140, 1, 140))
                {
                    var state = new MapViewState();
                    state.Open(Vector2.one * 70, Vector2.one * 140, Vector2.one * 140);
                    Color[] full = Render(type, renderer, controller, state);
                    int count = 0;
                    for (int x = 1; x < 511; x++)
                        if (full[256 * 512 + x].r > .025f && full[256 * 512 + x - 1].r <= .025f) count++;
                    Require(count >= 9 && count <= 11, "GPU output did not show 14m contours over 140m: " + count);
                    float low = Sum(full, 45, 58), high = Sum(full, 454, 467);
                    Require(high > low * 1.5f, "Higher contours were not thicker/brighter.");
                    // The 70m contour has the same relative height in both views.
                    float widthFull = Width(full, 250, 264);
                    state.Resize(Vector2.one * 70);
                    Color[] zoom = Render(type, renderer, controller, state);
                    float widthZoom = Width(zoom, 250, 264);
                    Require(Mathf.Abs(widthFull - widthZoom) <= 1, "Zoom scaled screen contour width: " + widthFull + " vs " + widthZoom);
                }
            }
            finally
            {
                renderer.Dispose();
                Object.DestroyImmediate(source);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static Color[] Render(Type type, IDisposable renderer, MapTestSceneController map, MapViewState state)
        {
            type.GetMethod("Render").Invoke(renderer, new object[] { map, state, 512, 512 });
            var texture = (RenderTexture)type.GetProperty("Texture").GetValue(renderer);
            RenderTexture previous = RenderTexture.active;
            var readback = new Texture2D(512, 512, TextureFormat.RGBA32, false, true);
            try
            {
                RenderTexture.active = texture;
                readback.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
                return readback.GetPixels();
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(readback); }
        }

        private static float Sum(Color[] pixels, int from, int to)
        {
            float sum = 0;
            for (int x = from; x < to; x++) sum += pixels[256 * 512 + x].r;
            return sum;
        }
        private static int Width(Color[] pixels, int from, int to)
        {
            float peak = 0;
            for (int x = from; x < to; x++) peak = Mathf.Max(peak, pixels[256 * 512 + x].r);
            int width = 0;
            for (int x = from; x < to; x++) if (pixels[256 * 512 + x].r >= peak * .5f) width++;
            return width;
        }
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
