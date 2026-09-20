using System;
using System.IO;
using System.Text;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    /// <summary>Creates an independent data-driven test of the exported Terrain.</summary>
    internal static class TerrainHeightMapTestSetup
    {
        internal const string ScenePath = "Assets/Scenes/TerrainHeightMapTestScene.unity";
        internal const string LevelPath = "Assets/Maps/TerrainPrototype/TerrainPrototypeHeightMapLevel.asset";
        private const string TexturePath = "Assets/Maps/TerrainPrototype/terrain_height_R16.png";
        private const string DataPath = "Assets/Maps/TerrainPrototype/TestTerrainData.asset";
        private const string ReportFolder = "Temp/TerrainHeightMapTest";

        [MenuItem("Animal Game/Terrain Height Map Test/Create Scene")]
        private static void Create()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play mode before creating the scene.");
            Require(!File.Exists(ScenePath) && !File.Exists(LevelPath),
                "The test scene or level already exists. Open it instead of overwriting it.");
            var source = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            Require(source != null && source.format == TextureFormat.R16 && source.isReadable
                && source.width == 513 && source.height == 513, "The source must be a readable 513 x 513 R16 texture.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var level = ScriptableObject.CreateInstance<HeightMapLevelAsset>();
            level.name = "TerrainPrototypeHeightMapLevel";
            var settings = new SerializedObject(level);
            settings.FindProperty("heightMap").objectReferenceValue = source;
            settings.FindProperty("mapWidthMeters").floatValue = 64f;
            settings.FindProperty("mapHeightMeters").floatValue = 64f;
            settings.FindProperty("minimumHeightMeters").floatValue = 0f;
            settings.FindProperty("maximumHeightMeters").floatValue = 20f;
            settings.FindProperty("bakedHeightResolution").intValue = 513;
            settings.FindProperty("normalizeSourceRange").boolValue = false;
            settings.FindProperty("surfaceSmoothingSigmaMeters").floatValue = 0f;
            settings.FindProperty("detailSmoothingSigmaMeters").floatValue = 0f;
            settings.FindProperty("useHeightMapBorderMask").boolValue = false;
            settings.FindProperty("previewResolution").intValue = 1024;
            settings.FindProperty("pixelsPerUnit").floatValue = 16f;
            settings.FindProperty("contourIntervalMeters").floatValue = 1f;
            settings.FindProperty("dynamicContourShader").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath("66c17e2481f6472cb3cd86fe63c9f57a"));
            settings.FindProperty("lowHeightColor").colorValue = new Color(0.025f, 0.09f, 0.12f, 1f);
            settings.FindProperty("middleHeightColor").colorValue = new Color(0.08f, 0.42f, 0.42f, 1f);
            settings.FindProperty("highHeightColor").colorValue = new Color(0.72f, 0.82f, 0.67f, 1f);
            settings.ApplyModifiedPropertiesWithoutUndo();
            Require(level.DynamicContourShader != null, "The existing contour shader is missing.");
            AssetDatabase.CreateAsset(level, LevelPath);
            Selection.activeObject = level;
            Require(EditorApplication.ExecuteMenuItem("Animal Game/Pre-Bake Height Map"), "Height prebake menu could not run.");
            Require(level.PrebakedHeightField != null && level.PrebakedHeightField.Matches(level), "Height prebake failed.");

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var mapObject = new GameObject("Terrain Prototype Fixed Height Map");
            mapObject.SetActive(false);
            var map = mapObject.AddComponent<MapTestSceneController>();
            var mapSettings = new SerializedObject(map);
            mapSettings.FindProperty("levelAsset").objectReferenceValue = level;
            // Match runtime resolution in the editor so the small ledges also line up there.
            mapSettings.FindProperty("editorHeightResolution").intValue = 513;
            mapSettings.FindProperty("editorPreviewResolution").intValue = 1024;
            mapSettings.ApplyModifiedPropertiesWithoutUndo();
            mapObject.SetActive(true);

            var bootstrap = new GameObject("Terrain Height Map Player Bootstrap")
                .AddComponent<HeightMapPlayerSceneBootstrap>();
            var spawnSettings = new SerializedObject(bootstrap);
            spawnSettings.FindProperty("playerSpawnMapPositionMeters").vector2Value = new Vector2(8f, 9f);
            spawnSettings.FindProperty("showRobotTerrainData").boolValue = true;
            spawnSettings.ApplyModifiedPropertiesWithoutUndo();
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            Require(EditorSceneManager.SaveScene(scene, ScenePath), "Could not save the test scene.");
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = bootstrap.gameObject;
            SceneView view = SceneView.lastActiveSceneView;
            if (view != null)
            {
                view.in2DMode = true;
                view.LookAt(Vector3.zero, Quaternion.identity, 36f, true);
            }
            Validate();
        }

        [MenuItem("Animal Game/Terrain Height Map Test/Validate Active Scene")]
        private static void Validate()
        {
            Require(SceneManager.GetActiveScene().path == ScenePath && SceneManager.sceneCount == 1,
                "Open TerrainHeightMapTestScene by itself before validating.");
            var maps = Object.FindObjectsByType<MapTestSceneController>(FindObjectsSortMode.None);
            Require(maps.Length == 1 && maps[0].HasGeneratedMap, "Expected exactly one generated map.");
            MapTestSceneController map = maps[0];
            HeightMapLevelAsset level = map.LevelAsset;
            Require(level != null && AssetDatabase.GetAssetPath(level) == LevelPath, "Incorrect level reference.");
            Require(AssetDatabase.GetAssetPath(level.HeightMap) == TexturePath
                && level.HeightMap.format == TextureFormat.R16, "Incorrect source texture or texture precision.");
            Require(level.MapSizeMeters == new Vector2(64f, 64f) && level.MinimumHeightMeters == 0f
                && level.MaximumHeightMeters == 20f && !level.NormalizeSourceRange
                && level.SurfaceSmoothingSigmaMeters == 0f && level.DetailSmoothingSigmaMeters == 0f,
                "Height scale or smoothing does not match the exported Terrain.");
            Require(level.PrebakedHeightField != null && level.PrebakedHeightField.Matches(level), "Rebake the test level.");
            Require(level.BakedSurfaceVisual == null && level.BakedStaticWaterMask == null
                && level.PlayableAreaMask == null && !level.UseHeightMapBorderMask,
                "Unexpected surface or mask inherited from another level.");
            Require(map.HeightField.Width == 513 && map.HeightField.Height == 513, "Height field resolution changed.");
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            Require(pipeline != null && pipeline.scriptableRenderer.GetType().Name == "Renderer2D",
                "The test scene should be using the original 2D renderer.");

            var terrain = AssetDatabase.LoadAssetAtPath<TerrainData>(DataPath);
            Require(terrain != null && terrain.heightmapResolution == 513, "Source TerrainData is missing or resized.");
            float[,] heights = terrain.GetHeights(0, 0, 513, 513);
            float maximumError = 0f;
            for (int z = 0; z < 513; z++)
                for (int x = 0; x < 513; x++)
                    maximumError = Mathf.Max(maximumError,
                        Mathf.Abs(map.HeightField.GetSurfaceHeightSample(x, z) - heights[z, x] * terrain.size.y));
            Require(maximumError < 0.002f, "The exported PNG no longer matches TerrainData; re-export and rebake.");

            var report = new StringBuilder();
            report.AppendLine($"Terrain height map test: {(Application.isPlaying ? "Play" : "Edit")} mode");
            report.AppendLine($"Renderer: {pipeline.scriptableRenderer.GetType().Name}; source: R16 513 x 513");
            report.AppendLine($"Compared all 263169 samples with TerrainData; max error: {maximumError:F6} m");
            CheckHeight(map, report, "Entrance", new Vector2(8f, 9f), 4f);
            CheckHeight(map, report, "Valley", new Vector2(52f, 23f), 2f);
            CheckHeight(map, report, "Orientation hill", new Vector2(58f, 58f), 5.4f);
            CheckHeight(map, report, "Low ledge", new Vector2(7f, 26.25f), 4.4f);
            CheckHeight(map, report, "High ledge", new Vector2(13f, 26.25f), 5f);
            float[] angles = { 10f, 25f, 45f };
            for (int i = 0; i < angles.Length; i++)
            {
                float x = 20f + i * 10f;
                float z = 39f - 3f / Mathf.Tan(angles[i] * Mathf.Deg2Rad);
                map.TrySampleMapPosition(new Vector2(x, z - 0.5f), out float below);
                map.TrySampleMapPosition(new Vector2(x, z + 0.5f), out float above);
                float angle = Mathf.Atan(above - below) * Mathf.Rad2Deg;
                report.AppendLine($"Ramp X={x}: {angle:F3} degrees; target {angles[i]}");
                Require(Mathf.Abs(angle - angles[i]) < 0.15f, "Ramp height scale changed.");
            }

            if (Application.isPlaying) ValidateRuntime(map, level, report);
            report.AppendLine("PASS");
            Directory.CreateDirectory(ReportFolder);
            File.WriteAllText(ReportFolder + (Application.isPlaying ? "/PlayValidation.txt" : "/EditValidation.txt"), report.ToString());
            Debug.Log(report.ToString(), map);
        }

        private static void ValidateRuntime(MapTestSceneController map, HeightMapLevelAsset level, StringBuilder report)
        {
            Require(map.HeightField.SurfaceTexture == level.PrebakedHeightField.SurfaceTexture, "Runtime is not using the test prebake.");
            var robots = Object.FindObjectsByType<RobotMover>(FindObjectsSortMode.None);
            var evaluators = Object.FindObjectsByType<HeightMapTraversalEvaluator>(FindObjectsSortMode.None);
            Require(robots.Length == 1 && evaluators.Length == 1 && evaluators[0].IsInitialized, "Player or traversal initialization failed.");
            Require(Object.FindObjectsByType<ScanChargeUI>(FindObjectsSortMode.None).Length == 1
                && Object.FindObjectsByType<TraversalScanOverlayUI>(FindObjectsSortMode.None).Length == 1,
                "Scan UI or overlay is missing.");
            Require(Camera.main != null && Camera.main.GetComponent<RobotCameraFollow>() != null,
                "The original player camera did not initialize.");
            Require(map.TrySampleWorldPosition(robots[0].transform.position, out Vector2 position, out float height),
                "Player is outside the test map.");
            report.AppendLine($"Player: map {position}, height {height:F4} m; scan UI and follow camera initialized.");
            var evaluator = evaluators[0];
            float[] angles = { 10f, 25f, 45f };
            for (int i = 0; i < angles.Length; i++)
            {
                float x = 20f + i * 10f;
                float z = 39f - 3f / Mathf.Tan(angles[i] * Mathf.Deg2Rad);
                SlopeTraversalResult result = evaluator.EvaluateMapPath(new Vector2(x, z - 0.4f), new Vector2(x, z + 0.4f));
                Require(result.HasData, "Traversal has no ramp data.");
                report.AppendLine($"Traversal {angles[i]} deg: {result.UphillLevel}, passable={result.IsPassable}, hardStop={result.RequiresHardStop}, reason={result.BlockReason}");
            }
            foreach (float x in new[] { 7f, 13f })
            {
                SlopeTraversalResult result = evaluator.EvaluateMapPath(new Vector2(x, 24f), new Vector2(x, 27f));
                Require(result.HasData, "Traversal has no ledge data.");
                report.AppendLine($"Traversal ledge X={x}: step={result.MaximumStepHeight:F3}m, passable={result.IsPassable}, hardStop={result.RequiresHardStop}, reason={result.BlockReason}");
            }
            report.AppendLine($"Existing traversal thresholds: level one <= {evaluator.LevelOneMaximumUphillAngle} deg, level three >= {evaluator.LevelThreeUphillAngle} deg, step limit {evaluator.MaximumStepHeightMeters} m.");
        }

        private static void CheckHeight(MapTestSceneController map, StringBuilder report, string name, Vector2 position, float expected)
        {
            Require(map.TrySampleMapPosition(position, out float height) && Mathf.Abs(height - expected) < 0.015f,
                $"{name} height or orientation changed.");
            report.AppendLine($"{name} {position}: {height:F4} m (target {expected:F2})");
        }

        [MenuItem("Animal Game/Terrain Height Map Test/Check Scan Connection (Play)")]
        private static void CheckScanConnection()
        {
            Require(Application.isPlaying && SceneManager.GetActiveScene().path == ScenePath
                && SceneManager.sceneCount == 1, "Run the test scene by itself first.");
            var ui = Object.FindFirstObjectByType<ScanChargeUI>();
            var overlay = Object.FindFirstObjectByType<TraversalScanOverlayUI>();
            Require(ui != null && overlay != null, "Scan components are missing.");
            // Exercise the same scan entry point used after the E double-tap.
            // This checks scene wiring; it does not simulate keyboard timing.
            ui.SendMessage("BeginTerrainScan", SendMessageOptions.RequireReceiver);
            double started = EditorApplication.timeSinceStartup;
            EditorApplication.CallbackFunction poll = null;
            poll = () =>
            {
                if (!Application.isPlaying || overlay == null)
                {
                    EditorApplication.update -= poll;
                    return;
                }
                double elapsed = EditorApplication.timeSinceStartup - started;
                if (elapsed < 2.5) return;
                if (overlay.VisibleMarkerCount == 0 && elapsed < 8.0) return;
                EditorApplication.update -= poll;
                Require(overlay.HasActiveSnapshot && overlay.VisibleMarkerCount > 0,
                    "The scan did not produce a visible snapshot.");
                Directory.CreateDirectory(ReportFolder);
                string result = $"PASS: terrain scan event produced {overlay.VisibleMarkerCount} visible markers.\n";
                File.WriteAllText(ReportFolder + "/ScanValidation.txt", result);
                ScreenCapture.CaptureScreenshot(ReportFolder + "/PlayPreview.png");
                Debug.Log(result, overlay);
            };
            EditorApplication.update += poll;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
