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
            CheckPinShapes();
            CheckAssets();
            CheckGpuContours();
            Debug.Log("PASS: world map cursor bounds, zoom anchoring, pin selection/relocation/deletion/snap escape, prefab references, stable triangle/square/diamond pins, GPU water hatch angle, spacing, opacity, current-independent sway, dashed shore and fog, unknown survey pattern, 14m interval, screen line widths and height emphasis.");
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
            Require(state.SelectedPinShape == 0, "Opening must select triangle.");
            for (int i = 0; i < 4; i++)
            {
                state.Move(new Vector2(100, 0), screen, map);
                state.Snap(screen, 7 * 1.4f);
                state.Submit();
                Require(state.SelectedPinShape == (i + 1) % 3, "Placement did not advance one slot to the right.");
            }
            Require(state.Pins.Count == 3 && Mathf.Abs(state.Pins[0].x - 140) < .01f
                && Mathf.Abs(state.Pins[2].x - 130) < .01f, "Placing a used shape did not relocate only that pin.");
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

        private static void CheckPinShapes()
        {
            var state = new MapViewState();
            Vector2 size = Vector2.one * 1000;
            state.Open(Vector2.one * 100, size, size);
            for (int i = 0; i < 3; i++)
            {
                state.Move(new Vector2(100, 0), size, size);
                state.Snap(size, 5);
                state.Submit();
            }
            Require(state.GetPinShape(0) == 0 && state.GetPinShape(1) == 1
                && state.GetPinShape(2) == 2, "Initial pins must be triangle, square, diamond.");
            state.Move(new Vector2(-100, 0), size, size);
            state.Snap(size, 5);
            state.Submit();
            Require(state.Pins.Count == 2 && state.GetPinShape(0) == 0
                && state.GetPinShape(1) == 2, "Deleting square changed another pin shape.");
            Require(!state.HasPinShape(1), "Deleted square still marked placed.");
            Require(!state.TryGetPinPosition(1, out _), "Deleted square retained a navigation target.");
            state.CyclePinShape(1);
            state.Move(new Vector2(300, 0), size, size);
            state.Snap(size, 5);
            state.Submit();
            Require(state.GetPinShape(2) == 1, "New pin did not reuse the available shape.");
            state.CyclePinShape(-1);
            state.Move(new Vector2(100, 0), size, size);
            state.Snap(size, 5);
            state.Submit();
            Require(state.Pins.Count == 3 && state.GetPinShape(0) == 0 && state.GetPinShape(1) == 2
                && state.GetPinShape(2) == 1, "Relocating square changed another pin shape.");
            Require(state.Pins[0].x == 200 && state.Pins[1].x == 400 && state.Pins[2].x == 700,
                "Relocating selected square changed another position.");
            Require(state.TryGetPinPosition(1, out Vector2 square) && square.x == 700,
                "Navigation did not use the relocated pin position.");
            state.CyclePinShape(1);
            Require(state.SelectedPinShape == 0, "RB did not wrap diamond to triangle.");
            state.CyclePinShape(-1);
            Require(state.SelectedPinShape == 2, "LB did not wrap triangle to diamond.");
            state.Open(Vector2.one * 100, size, size);
            Require(state.SelectedPinShape == 0 && state.Pins.Count == 3,
                "Reopening did not restore triangle selection while keeping placed pins.");
        }

        private static void CheckAssets()
        {
            const string mapPrefabPath = "Assets/Prefabs/Resources/UI/Map/WorldMapUI.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(mapPrefabPath);
            Require(prefab != null && prefab.GetComponent<WorldMapUI>() != null, "Map prefab is missing.");
            string[] sceneDependencies = AssetDatabase.GetDependencies("Assets/Scenes/GameMaps/Official_Map_Test_22.unity");
            Require(Array.IndexOf(sceneDependencies, mapPrefabPath) >= 0,
                "Official_Map_Test_22 is missing its world-map prefab for the signal tutorial.");
            var doc = prefab.GetComponent<UIDocument>();
            Require(doc.panelSettings != null && doc.visualTreeAsset != null, "UIDocument references are missing.");
            var tree = doc.visualTreeAsset.CloneTree();
            Require(tree.Q("world-map") != null && tree.Q("map-cursor") != null
                && tree.Q("pin-2") != null, "Map UXML is incomplete.");
            Require(tree.Q("map-pin-selector") != null && tree.Q("map-pin-choice-0") != null
                && tree.Q("map-pin-choice-1") != null && tree.Q("map-pin-choice-2") != null
                && tree.Q<Label>("map-pin-previous-key") != null && tree.Q<Label>("map-pin-next-key") != null,
                "Pin selector frame, slots or key hints missing.");
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
            var renderer = (IDisposable)Activator.CreateInstance(type, new object[]
            {
                Shader.Find("AnimalGame/World Map Contours"), Vector2.one * (140f / 512)
            });
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
                    CheckPinBearings(controller);
                    CheckWater(type, renderer, controller, state);
                }
            }
            finally
            {
                renderer.Dispose();
                Object.DestroyImmediate(source);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static void CheckPinBearings(MapTestSceneController map)
        {
            var go = new GameObject("Pin bearing fixture");
            go.SetActive(false);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, map.gameObject.scene);
            try
            {
                var ui = go.AddComponent<WorldMapUI>();
                typeof(WorldMapUI).GetField("map", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ui, map);
                Vector2 center = map.MapSizeMeters * .5f;
                var screen = Vector2.one * 1000;
                var compass = new AnimalGame.MainUI.CompassElement();
                float[] bearings = { 0, 90, 180, 270 };
                foreach (float expected in bearings)
                {
                    var state = ui.View;
                    if (state.Pins.Count > 0) state.Submit();
                    state.Open(center, map.MapSizeMeters, map.MapSizeMeters);
                    float radians = expected * Mathf.Deg2Rad;
                    state.Move(new Vector2(Mathf.Sin(radians), Mathf.Cos(radians)) * 100, screen, map.MapSizeMeters);
                    state.Submit();
                    float actual = ui.PinBearing(0, map.WorldBounds.center);
                    Require(Mathf.Abs(Mathf.DeltaAngle(actual, expected)) < .01f,
                        "Pin compass bearing differs from cardinal direction: " + expected);
                    compass.SetPins(ui, map.WorldBounds.center);
                    var field = typeof(AnimalGame.MainUI.CompassElement).GetField("pinBearings", BindingFlags.Instance | BindingFlags.NonPublic);
                    Require(Mathf.Abs(Mathf.DeltaAngle(((float[])field.GetValue(compass))[0], expected)) < .01f,
                        "Compass did not receive pin bearing.");
                }
                compass.SetPins(null, Vector2.zero);
                Require((int)typeof(AnimalGame.MainUI.CompassElement).GetField("pinCount",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(compass) == 0, "Compass retained absent pins.");
            }
            finally { Object.DestroyImmediate(go); }
        }

        private static void CheckWater(Type type, IDisposable renderer, MapTestSceneController map, MapViewState state)
        {
            var mask = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            var uv = new Texture2D(2, 2, TextureFormat.RGHalf, false, true)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var fog = new Texture2D(2, 1, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
            fog.SetPixels(new[] { Color.black, Color.white }); fog.Apply();
            try
            {
                // This fixture uses a hard-edged mask without baked shoreline geometry.
                // Set the normal-zoom stroke to the reference width so it covers the sampled pixel.
                var settings = new SerializedObject(map.Presentation);
                settings.FindProperty("minimumContourWidth").floatValue = 2.65f;
                settings.ApplyModifiedPropertiesWithoutUndo();
                state.Resize(Vector2.one * 140);
                Color[] dry = Render(type, renderer, map, state);
                Color[] dryWithFog = Render(type, renderer, map, state, fog);
                Color shallow = new Color(1, .1f, 0, 0);
                mask.SetPixels(new[] { shallow, shallow, shallow, shallow }); mask.Apply();
                foreach (var tile in map.RuntimeMap.Tiles)
                    tile.Level.SetBakedStaticWaterMask(mask, new Rect(.25f, .25f, .5f, .5f));

                Color[] water = Render(type, renderer, map, state);
                int bankDashes = 0;
                float bankPeak = 0;
                for (int y = 145; y < 365; y++)
                {
                    int row = y * 512;
                    Require(water[row + 125] == dry[row + 125], "Water escaped the shoreline.");
                    bankPeak = Mathf.Max(bankPeak, water[row + 127].r);
                    if (water[row + 127].r >= .30f && water[row - 512 + 127].r < .30f) bankDashes++;
                }
                Require(bankDashes >= 5 && bankDashes <= 6, "The water edge is missing 40-pixel dash spacing.");
                const float expectedBankPeak = .44f * .84375f; // 44% opacity at the fixture's antialiased shore pixel.
                Require(Mathf.Abs(bankPeak - expectedBankPeak) < .02f
                    || Mathf.Abs(bankPeak - Mathf.LinearToGammaSpace(expectedBankPeak)) < .02f,
                    "The water edge did not render at 44% opacity before antialiasing: " + bankPeak);
                CheckWaterHatch(water);
                state.Resize(Vector2.one * 70);
                CheckHatchZoom(water, Render(type, renderer, map, state));

                state.Resize(Vector2.one * 140);
                Color deepFlowing = new Color(1, .9f, 128f / 255f, 1);
                mask.SetPixels(new[] { deepFlowing, deepFlowing, deepFlowing, deepFlowing }); mask.Apply();
                Color[] deepWater = Render(type, renderer, map, state);
                Color[] withFog = Render(type, renderer, map, state, fog);
                for (int i = 0; i < water.Length; i++)
                {
                    Require(deepWater[i] == water[i], "Depth or initial current changed the map's water symbol.");
                    Require(withFog[i] == (i % 512 < 256 ? water[i] : dryWithFog[i]),
                        "Water changed the unknown survey pattern or was obscured in the known region.");
                }
                var waterTime = type.GetProperty("WaterTime", BindingFlags.Instance | BindingFlags.NonPublic);
                waterTime.SetValue(renderer, 3f);
                type.GetMethod("RenderWaterFrame", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(renderer, new object[] { map });
                // The preceding render used partial fog. Compare its known left half after animation.
                Color[] movingWater = ReadFrame(type, renderer);
                Require(CountWaterDifferences(water, movingWater) > 100,
                    "Map water hatches did not sway.");
                Color eastward = new Color(1, .9f, 0, 1);
                mask.SetPixels(new[] { eastward, eastward, eastward, eastward }); mask.Apply();
                Require(CountWaterDifferences(movingWater, Render(type, renderer, map, state)) == 0,
                    "Authored current changed the map water sway.");

                uv.SetPixels(new[] { new Color(1, 1, 0), new Color(1, 36, 0),
                    new Color(36, 1, 0), new Color(36, 36, 0) });
                uv.Apply();
                foreach (var tile in map.RuntimeMap.Tiles) tile.Level.SetBakedStaticWaterUv(uv);
                Color[] baked = Render(type, renderer, map, state);
                Require(CountWaterDifferences(movingWater, baked) == 0,
                    "Baked UV reshaped the map water hatches.");
                mask.SetPixels(new[] { deepFlowing, deepFlowing, deepFlowing, deepFlowing }); mask.Apply();
                Require(CountWaterDifferences(baked, Render(type, renderer, map, state)) == 0,
                    "Authored current changed map water hatches with baked UV present.");
                foreach (var tile in map.RuntimeMap.Tiles) tile.Level.SetBakedStaticWaterUv(null);

                mask.SetPixels(new[] { shallow, shallow, shallow, shallow }); mask.Apply();
                waterTime.SetValue(renderer, 3f);
                Color[] driftingWater = Render(type, renderer, map, state);
                int driftingPixels = 0;
                for (int y = 165; y < 345; y++)
                for (int x = 165; x < 244; x++)
                    if (Mathf.Abs(water[y * 512 + x].r - driftingWater[y * 512 + x].r) > .012f)
                        driftingPixels++;
                Require(driftingPixels > 100, "Still water hatches did not drift.");
            }
            finally
            {
                Object.DestroyImmediate(mask);
                Object.DestroyImmediate(uv);
                Object.DestroyImmediate(fog);
            }
        }

        private static int CountWaterDifferences(Color[] first, Color[] second)
        {
            int differences = 0;
            for (int y = 165; y < 345; y++)
            for (int x = 165; x < 244; x++)
            {
                int index = y * 512 + x;
                if (Mathf.Abs(first[index].r - second[index].r) > .012f)
                    differences++;
            }
            return differences;
        }

        private static void CheckHatchZoom(Color[] normal, Color[] zoomed)
        {
            int LongestStroke(Color[] frame)
            {
                int longest = 0;
                // Search both readback orientations and sample the centers of nearby hatches.
                for (int direction = -1; direction <= 1; direction += 2)
                for (int offset = -24; offset <= 24; offset++)
                {
                    int length = 0;
                    for (int x = 168; x < 344; x++)
                    {
                        int y = (direction < 0 ? 511 - x : x) + offset;
                        bool visible = frame[y * 512 + x].r > .06f;
                        length = visible ? length + 1 : 0;
                        longest = Mathf.Max(longest, length);
                    }
                }
                return longest;
            }

            int before = LongestStroke(normal);
            int after = LongestStroke(zoomed);
            Require(before >= 10 && Mathf.Abs(after - before * 2) <= 2,
                $"Water hatch lengths did not double at 2× zoom ({before} to {after}).");
        }

        private static void CheckWaterHatch(Color[] frame)
        {
            float peak = 0, axisError = 0, ascendingError = 0, descendingError = 0;
            for (int y = 150; y < 360; y++)
            for (int x = 150; x < 360; x++)
            {
                int i = y * 512 + x;
                float shade = frame[i].r;
                peak = Mathf.Max(peak, shade);
                axisError += Mathf.Abs(shade - frame[i + 1].r);
                ascendingError += Mathf.Abs(shade - frame[i + 513].r);
                descendingError += Mathf.Abs(shade - frame[i - 511].r);
            }
            float gammaPeak = Mathf.LinearToGammaSpace(.12f);
            Require(Mathf.Abs(peak - .12f) < .01f || Mathf.Abs(peak - gammaPeak) < .01f,
                "The center of a water hatch did not render at 12% opacity: " + peak);
            float alongError = Mathf.Min(ascendingError, descendingError);
            Require(axisError > 1 && alongError > axisError * .16f && alongError < axisError * .35f,
                "The water hatches didn't follow broken 45-degree diagonals.");

            int bands = 0;
            for (int y = 224; y < 288; y++)
            for (int x = 151; x < 360; x++)
            {
                int i = y * 512 + x;
                if (frame[i - 1].r < peak * .5f && frame[i].r >= peak * .5f) bands++;
            }
            Require(bands >= 3 * 64 && bands <= 5 * 64, "The water hatches don't match the 24-pixel spacing.");
        }

        private static Color[] Render(Type type, IDisposable renderer, MapTestSceneController map, MapViewState state, Texture fog = null)
        {
            type.GetMethod("Render").Invoke(renderer, new object[] { map, state, 512, 512, fog });
            return ReadFrame(type, renderer);
        }

        private static Color[] ReadFrame(Type type, IDisposable renderer)
        {
            var texture = (RenderTexture)type.GetProperty("Texture").GetValue(renderer);
            RenderTexture previous = RenderTexture.active;
            var readback = new Texture2D(512, 512, TextureFormat.RGBA32, false, true);
            try
            {
                RenderTexture.active = texture;
                readback.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
                Color[] pixels = readback.GetPixels();
                foreach (Color pixel in pixels)
                    Require(pixel.r == pixel.g && pixel.g == pixel.b, "Map terrain or water rendered outside grayscale.");
                return pixels;
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
