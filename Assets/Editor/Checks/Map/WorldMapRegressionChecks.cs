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
            Debug.Log("PASS: world map cursor bounds, zoom anchoring, pin selection/relocation/deletion/snap escape, prefab references, stable triangle/square/diamond pins, GPU water zoom/flow/shore/fog, bright shallow and deep overview ripples, unknown survey pattern, 14m interval, screen line widths and height emphasis.");
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
                    CheckPinBearings(controller);
                    CheckWater(type, renderer, controller, state);
                }
                CheckOverviewWater(type, renderer, controller, source);
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
            var pattern = new Texture2D(16, 1, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Repeat };
            pattern.SetPixels(new Color[16]); pattern.Apply();
            var mask = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            SetWaterFlow(mask, 0, 1);
            var bakedUv = new Texture2D(2, 2, TextureFormat.RGFloat, false, true)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var fog = new Texture2D(2, 1, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
            fog.SetPixels(new[] { Color.black, Color.white }); fog.Apply();
            try
            {
                state.Resize(Vector2.one * 140);
                Color[] terrainWithFog = Render(type, renderer, map, state, fog);
                foreach (var tile in map.RuntimeMap.Tiles)
                {
                    var settings = new SerializedObject(tile.Level);
                    settings.FindProperty("staticWaterEditorTexture").objectReferenceValue = pattern;
                    settings.FindProperty("staticWaterEditorTileSizeMeters").floatValue = 1;
                    settings.FindProperty("staticWaterLayerOneSpeedMetersPerSecond").vector2Value = Vector2.zero;
                    settings.FindProperty("staticWaterLayerTwoSpeedMetersPerSecond").vector2Value = Vector2.zero;
                    settings.FindProperty("staticWaterWaveSpeed").floatValue = 0;
                    settings.FindProperty("staticWaterWaveDistortion").floatValue = 0;
                    settings.FindProperty("bakedStaticWaterMask").objectReferenceValue = mask;
                    settings.FindProperty("bakedStaticWaterBounds").rectValue = new Rect(.25f, .25f, .5f, .5f);
                    settings.ApplyModifiedPropertiesWithoutUndo();
                }
                Color[] baseWater = Render(type, renderer, map, state);
                Color[] flowingWithoutPattern = ReadWaterFrame(type, renderer, .25f);
                for (int x = 0; x < 128; x++)
                    Require(baseWater[256 * 512 + x] == terrainWithFog[256 * 512 + x], "Cropped water leaked outside its bounds.");
                for (int y = 140; y < 372; y++)
                for (int x = 140; x < 372; x++)
                    Require(baseWater[y * 512 + x].r == 0 && flowingWithoutPattern[y * 512 + x].r == 0,
                        "Transparent water gained a fill, contours or current marks without source ripples.");
                Require(Sum(baseWater, 1, 120) > .1f, "Water hid contours outside its bounds.");

                SetWaterFlow(mask, 0, 0);
                // Four stripes per repeat make the authored texture identifiable at every zoom.
                for (int x = 0; x < 16; x++) pattern.SetPixel(x, 0, x % 4 < 2 ? Color.green : Color.clear);
                pattern.Apply();
                Color[] water = Render(type, renderer, map, state);
                float bands = CountWaterBands(water);
                state.Resize(Vector2.one * 70);
                float zoomBands = CountWaterBands(Render(type, renderer, map, state));
                Require(bands >= 2 && zoomBands >= 2 && Mathf.Abs(bands - zoomBands) <= bands * .3f,
                    "Water ripple spacing changed with map zoom: " + bands + " vs " + zoomBands);
                state.Resize(Vector2.one * 140);
                Color[] partiallyKnown = Render(type, renderer, map, state, fog);
                Color[] still = ReadWaterFrame(type, renderer, 1);
                float darkest = 1f, brightest = 0f;
                for (int y = 0; y < 512; y++)
                for (int x = 0; x < 256; x++)
                {
                    int index = y * 512 + x;
                    Require(still[index] == partiallyKnown[index], "Still water moved without an authored current.");
                    Require(partiallyKnown[index] == water[index], "Known terrain or water changed under a clear mask.");
                    Color hidden = partiallyKnown[index + 256];
                    Require(hidden == terrainWithFog[index + 256], "Water leaked into the unknown survey pattern.");
                    darkest = Mathf.Min(darkest, hidden.r);
                    brightest = Mathf.Max(brightest, hidden.r);
                }
                Require(darkest > .001f && brightest < .4f && brightest - darkest > .03f,
                    "Unknown terrain should be covered by a dark, visible survey pattern.");

                SetWaterFlow(mask, 0, 1);
                Render(type, renderer, map, state, fog);
                Color[] flowing = ReadWaterFrame(type, renderer, .25f);
                for (int y = 0; y < 512; y++)
                for (int x = 0; x < 512; x++)
                    if (x < 128 || x >= 256 || y < 128 || y >= 384)
                        Require(flowing[y * 512 + x] == terrainWithFog[y * 512 + x], "Current escaped its shore or fog mask.");

                // At 2 metres across, one authored metre is 256 px and each stripe repeats every 64 px.
                state.Resize(Vector2.one * 2);
                foreach (int direction in new[] { 1, -1 })
                {
                    SetWaterFlow(mask, direction > 0 ? 0 : 128, 16f / 255f);
                    Color[] before = Render(type, renderer, map, state);
                    float closeBands = CountWaterBands(before);
                    Require(closeBands >= 2.5f && closeBands <= 5, "Close zoom lost the authored metre scale: " + closeBands);
                    Color[] after = ReadWaterFrame(type, renderer, 1f / 12);
                    int shift = CurrentDisplacement(before, after, 1);
                    Require(Mathf.Abs(shift - 13 * direction) <= 2, "Source ripples moved at the wrong speed or direction: " + shift);
                }

                SetWaterFlow(mask, 64, 16f / 255f);
                Color[] north = Render(type, renderer, map, state);
                int northShift = CurrentDisplacement(north, ReadWaterFrame(type, renderer, 1f / 12), 512);
                // The cropped 70m-wide mask covers map coordinates [35,105]. Here +U follows +mapY.
                bakedUv.SetPixelData(new[] { new Vector2(52.5f, 52.5f), new Vector2(52.5f, 87.5f),
                    new Vector2(87.5f, 52.5f), new Vector2(87.5f, 87.5f) }, 0);
                bakedUv.Apply();
                foreach (var tile in map.RuntimeMap.Tiles) tile.Level.SetBakedStaticWaterUv(bakedUv);
                SetWaterFlow(mask, 128, 16f / 255f);
                Color[] alongRiver = Render(type, renderer, map, state);
                Color[] downstream = ReadWaterFrame(type, renderer, 1f / 12);
                int verticalShift = CurrentDisplacement(alongRiver, downstream, 512);
                Require(Mathf.Abs(Mathf.Abs(northShift) - 13) <= 2 && Mathf.Abs(verticalShift - northShift) <= 2,
                    "Baked +U did not move ripples north: " + verticalShift + " vs " + northShift);
            }
            finally { Object.DestroyImmediate(pattern); Object.DestroyImmediate(mask); Object.DestroyImmediate(fog); Object.DestroyImmediate(bakedUv); }
        }

        private static void SetWaterFlow(Texture2D mask, int angleByte, float speed)
        {
            Color data = new Color(1, 1, angleByte / 255f, speed);
            mask.SetPixels(new[] { data, data, data, data }); mask.Apply();
        }

        private static void CheckOverviewWater(Type type, IDisposable renderer, MapTestSceneController map, Texture2D heights)
        {
            var parameters = AssetDatabase.LoadAssetAtPath<MapParams>("Assets/Maps/MapParams/Gameplay Map Params.asset");
            var pattern = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Arts/Textures/Texture_Water.png");
            Require(parameters != null && pattern != null && parameters.StaticWaterTexture == pattern,
                "Overview check needs the gameplay map's water texture.");
            var mask = new Texture2D(2, 1, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
            mask.SetPixels(new[] { new Color(1, .1f, 0, 0), new Color(1, .85f, 0, 1) }); mask.Apply();
            try
            {
                using (new TiledMapTestFixture(map, heights, 1536, 1))
                {
                    foreach (var tile in map.RuntimeMap.Tiles)
                    {
                        var settings = new SerializedObject(tile.Level);
                        settings.FindProperty("mapParams").objectReferenceValue = parameters;
                        settings.ApplyModifiedPropertiesWithoutUndo();
                        tile.Level.SetBakedStaticWaterMask(mask, new Rect(0, 0, 1, 1));
                    }
                    var state = new MapViewState();
                    foreach (float metresPerPixel in new[] { 1.5f, 2.5f })
                    {
                        state.Open(map.MapSizeMeters * .5f, Vector2.one * (512 * metresPerPixel), map.MapSizeMeters);
                        Color[] frame = Render(type, renderer, map, state);
                        CheckBrightStrokes(frame, 16, 240, "shallow", metresPerPixel);
                        CheckBrightStrokes(frame, 272, 496, "deep", metresPerPixel);
                    }
                }
            }
            finally { Object.DestroyImmediate(mask); }
        }

        private static void CheckBrightStrokes(Color[] frame, int left, int right, string depth, float metresPerPixel)
        {
            int nearWhite = 0, black = 0, thickRuns = 0;
            for (int y = 16; y < 496; y++)
            {
                int run = 0;
                for (int x = left; x < right; x++)
                {
                    int index = y * 512 + x;
                    float value = frame[index].r;
                    if (value >= .9f) nearWhite++;
                    if (value < .01f) black++;
                    run = value >= .8f && frame[index + 512].r >= .8f ? run + 1 : 0;
                    if (run == 8) thickRuns++;
                }
            }
            int area = (right - left) * 480;
            Require(nearWhite >= area * .003f && thickRuns >= 4 && black > area * .5f,
                $"{depth} water at {metresPerPixel}m/px: {nearWhite}/{area} near-white pixels, " +
                $"{thickRuns} bright runs at least 8x2px, {black} dark gap pixels.");
        }

        private static float CountWaterBands(Color[] frame)
        {
            int count = 0;
            for (int y = 160; y < 352; y += 8)
            {
                int row = y * 512;
                float low = 1, high = 0;
                for (int x = 144; x < 368; x++)
                {
                    low = Mathf.Min(low, frame[row + x].g);
                    high = Mathf.Max(high, frame[row + x].g);
                }
                Require(high - low > .03f, "Authored water pattern disappeared at map scale.");
                float middle = (high + low) * .5f;
                for (int x = 145; x < 368; x++)
                    if (frame[row + x - 1].g < middle && frame[row + x].g >= middle) count++;
            }
            return count / 24f;
        }

        // A stride of one measures horizontal motion. The scanline stride measures vertical motion.
        private static int CurrentDisplacement(Color[] before, Color[] after, int stride)
        {
            float bestError = float.PositiveInfinity;
            int bestShift = 0;
            for (int shift = -16; shift <= 16; shift++)
            {
                float error = 0;
                for (int y = 144; y < 368; y += 2)
                for (int x = 148; x < 236; x++)
                    error += Mathf.Abs(before[y * 512 + x].g - after[y * 512 + x + shift * stride].g);
                if (error < bestError || (error == bestError && Mathf.Abs(shift) < Mathf.Abs(bestShift)))
                {
                    bestError = error;
                    bestShift = shift;
                }
            }
            return bestShift;
        }

        private static Color[] Render(Type type, IDisposable renderer, MapTestSceneController map, MapViewState state, Texture fog = null)
        {
            type.GetMethod("Render").Invoke(renderer, new object[] { map, state, 512, 512, fog });
            return ReadWaterFrame(type, renderer, 0);
        }

        private static Color[] ReadWaterFrame(Type type, IDisposable renderer, float time)
        {
            type.GetMethod("AnimateWater", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(renderer, new object[] { time });
            var texture = (RenderTexture)type.GetProperty("Texture").GetValue(renderer);
            RenderTexture previous = RenderTexture.active;
            var readback = new Texture2D(512, 512, TextureFormat.RGBA32, false, true);
            try
            {
                RenderTexture.active = texture;
                readback.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
                Color[] pixels = readback.GetPixels();
                foreach (Color pixel in pixels)
                    Require(pixel.r == pixel.g && pixel.g == pixel.b, "Map terrain, ripples or currents rendered outside grayscale.");
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
