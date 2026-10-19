using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

namespace AnimalGame.Editor
{
    /// <summary>Independent V1 gameplay export of the four-tile prairie scale sample.</summary>
    public static class PlainsSample500Gameplay
    {
        public const string AuthoringScenePath = "Assets/Scenes/GameMaps/Plains_Sample_500m.unity";
        public const string ScenePath = "Assets/Scenes/GameMaps/Plains_Sample_500m_Test.unity";
        public const string Folder = "Assets/Maps/PlainsSample500/Gameplay";
        public const string LevelPath = Folder + "/PlainsSample500_TiledLevel.asset";
        private const string TemplateScenePath = "Assets/Scenes/TerrainHeightMapTestScene.unity";
        private const string RootName = "Plains Sample 500m - V1 Gameplay";
        private const string ReportFolder = "Temp/PlainsSample500";
        private const int Resolution = 2049;
        private const int RuntimeResolution = 2048;
        private const int EditorResolution = 1024;
        private const float TileSize = 250f;
        private const float HeightRange = 350f;
        private const float WorldScale = 2048f / 30f / TileSize;

        public static void Export(Terrain[] terrains)
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play before exporting the sample.");
            Require(SceneManager.GetActiveScene().path == AuthoringScenePath && SceneManager.sceneCount == 1,
                "Open the independent Plains_Sample_500m authoring scene by itself.");
            Require(terrains != null && terrains.Length == 4 && terrains.Distinct().Count() == 4,
                "The prairie sample must contain four independent Terrain tiles.");
            var ordered = new Terrain[4];
            for (int z = 0; z < 2; z++) for (int x = 0; x < 2; x++)
            {
                Terrain terrain = terrains.SingleOrDefault(t => t != null && t.name == $"Sample_{x}_{z}");
                Require(terrain != null && terrain.gameObject.scene.path == AuthoringScenePath,
                    $"Missing sample terrain Sample_{x}_{z}.");
                TerrainData data = terrain.terrainData;
                Require(data != null && data.heightmapResolution == Resolution && data.size == new Vector3(250, 350, 250)
                    && terrain.transform.position == new Vector3(x * TileSize, 0, z * TileSize)
                    && terrain.transform.lossyScale == Vector3.one
                    && Quaternion.Angle(terrain.transform.rotation, Quaternion.identity) < 0.001f,
                    "Sample terrain size, height range, sampling or transform differs from V1.");
                ordered[z * 2 + x] = terrain;
            }
            Require(ordered.Select(t => t.terrainData).Distinct().Count() == 4, "Sample terrains share TerrainData.");
            VerifyTerrainSeams(ordered);
            EnsureFolder(Folder + "/data");
            var levels = new List<TiledLevelAsset.Tile>();
            var report = new StringBuilder("PASS: independent 500 x 500 m prairie height-map export.\n");
            try
            {
                for (int index = 0; index < ordered.Length; index++)
                {
                    int x = index % 2, z = index / 2;
                    string stem = $"Sample_{x}_{z}";
                    EditorUtility.DisplayProgressBar("Export prairie sample", stem, index / 4f);
                    TerrainData data = ordered[index].terrainData;
                    data.SyncHeightmap();
                    float[,] heights = data.GetHeights(0, 0, Resolution, Resolution);
                    byte[] raw = new byte[Resolution * Resolution * 2];
                    ushort minimum = ushort.MaxValue, maximum = 0;
                    for (int row = 0; row < Resolution; row++) for (int column = 0; column < Resolution; column++)
                    {
                        float height = heights[row, column];
                        Require(!float.IsNaN(height) && !float.IsInfinity(height) && height >= 0 && height <= 1,
                            "Sample contains heights outside its fixed 0..350 m encoding.");
                        ushort value = (ushort)Mathf.RoundToInt(height * ushort.MaxValue);
                        int offset = (row * Resolution + column) * 2;
                        raw[offset] = (byte)value;
                        raw[offset + 1] = (byte)(value >> 8);
                        if (value < minimum) minimum = value;
                        if (value > maximum) maximum = value;
                    }
                    string pngPath = Folder + "/data/" + stem + "_height_R16.png";
                    TerrainGridV1Exporter.WriteGray16Png(pngPath, raw);
                    TerrainGridV1Exporter.ValidatePngHeader(pngPath);
                    Texture2D texture = TerrainGridV1Exporter.ImportAndVerify(pngPath, raw);
                    HeightMapLevelAsset level = TerrainGridV1Exporter.ConfigureLevel(stem, texture, Folder, TileSize, RuntimeResolution);
                    var settings = new SerializedObject(level);
                    settings.FindProperty("minimumHeightMeters").floatValue = 0;
                    settings.FindProperty("maximumHeightMeters").floatValue = HeightRange;
                    settings.FindProperty("contourIntervalMeters").floatValue = 7;
                    settings.FindProperty("backgroundColor").colorValue = Color.black;
                    settings.FindProperty("prebakedHeightField").objectReferenceValue = null;
                    settings.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(level);
                    AssetDatabase.SaveAssetIfDirty(level);
                    levels.Add(new TiledLevelAsset.Tile(new Vector2Int(x, z), level));
                    report.AppendLine($"{stem}: all {Resolution} x {Resolution} imported R16 samples match Terrain quantization; "
                        + $"height {minimum / 65535f * HeightRange:F4}..{maximum / 65535f * HeightRange:F4} m; SHA256 {TerrainGridV1Exporter.Sha256(raw)}.");
                }
                var tiled = AssetDatabase.LoadAssetAtPath<TiledLevelAsset>(LevelPath);
                bool create = tiled == null;
                Require(!create || AssetDatabase.LoadMainAssetAtPath(LevelPath) == null,
                    "An unrelated asset occupies the prairie sample tiled level path.");
                if (create) tiled = ScriptableObject.CreateInstance<TiledLevelAsset>();
                tiled.Configure(Vector2.one * TileSize, Vector2.one * WorldScale, levels[0].level, levels);
                if (create) AssetDatabase.CreateAsset(tiled, LevelPath);
                EditorUtility.SetDirty(tiled);
                AssetDatabase.SaveAssetIfDirty(tiled);
                ValidateLevel(tiled);
                Vector2 spawn = ChooseSpawn(tiled);
                report.AppendLine("Four shared tile borders match. PNG is true 16-bit grayscale; PNG top row maps to north Terrain Z.");
                report.AppendLine("Fixed V1 scale; 250 m tiles, 2048 runtime fields, no range normalization, smoothing, RAW files or prebake assets.");
                report.AppendLine($"Gentle starting position near visible relief: {spawn}; logical height {SampleHeight(tiled, spawn):F4} m.");
                Directory.CreateDirectory(ReportFolder);
                File.WriteAllText(ReportFolder + "/ExportValidation.txt", report.ToString(), new UTF8Encoding(false));
                Debug.Log(report.ToString(), tiled);
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        [MenuItem("Animal Game/Plains Sample 500m/Open and Deploy V1 Gameplay Test")]
        public static void Deploy()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play before deploying the sample test.");
            TiledLevelAsset level = LoadLevel();
            RequirePlayerResources();
            Scene target = SceneManager.GetActiveScene();
            if (target.path != ScenePath || SceneManager.sceneCount != 1)
            {
                for (int index = 0; index < SceneManager.sceneCount; index++)
                    Require(!SceneManager.GetSceneAt(index).isDirty, "Save scene changes before opening the independent sample test.");
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
                    target = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                else
                {
                    target = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    // Save the empty scene before opening a template additively: its path and ownership must already exist.
                    Require(EditorSceneManager.SaveScene(target, ScenePath), "Could not create the independent sample test scene.");
                }
            }
            // A single-scene switch unloads unreferenced native assets. A local C# wrapper alone
            // does not retain its native ScriptableObject, so load the level in the target scene.
            level = LoadLevel();
            GameObject root = target.GetRootGameObjects().SingleOrDefault(obj => obj.name == RootName);
            Require(Components<MapTestSceneController>(target).All(component => root != null && component.transform.IsChildOf(root.transform)),
                "An authored map controller exists outside the managed sample test root.");
            Require(Components<HeightMapPlayerSceneBootstrap>(target).All(component => root != null && component.transform.IsChildOf(root.transform)),
                "An authored player bootstrap exists outside the managed sample test root.");
            Require(Components<EventSystem>(target).All(component => root != null && component.transform.IsChildOf(root.transform)),
                "An authored EventSystem exists outside the managed sample test root.");
            Require(Components<Terrain>(target).Length == 0, "Refusing to deploy gameplay over an authored 3D Terrain scene.");
            foreach (Camera camera in Components<Camera>(target).Where(camera => camera.enabled && camera.gameObject.activeInHierarchy))
                Require(camera.name == "Main Camera" && camera.transform.parent == null && camera.transform.childCount == 0
                    && camera.GetComponents<Component>().All(component => component is Transform || component is Camera
                        || component is AudioListener || component is UniversalAdditionalCameraData),
                    "An authored active camera must be disabled before sample deployment.");

            Vector2 spawn = ChooseSpawn(level);
            Scene template = EditorSceneManager.OpenScene(TemplateScenePath, OpenSceneMode.Additive);
            try
            {
                var sourceMap = Components<MapTestSceneController>(template).Single();
                var sourceBootstrap = Components<HeightMapPlayerSceneBootstrap>(template).Single();
                if (SceneManager.GetActiveScene() != target)
                    Require(SceneManager.SetActiveScene(target), "Could not activate the independent sample test.");
                if (root == null)
                {
                    root = new GameObject(RootName);
                    SceneManager.MoveGameObjectToScene(root, target);
                    Undo.RegisterCreatedObjectUndo(root, "Create prairie sample test root");
                }
                Require(root.transform.position == Vector3.zero && root.transform.localScale == Vector3.one
                    && Quaternion.Angle(root.transform.rotation, Quaternion.identity) < 0.001f,
                    "The managed sample gameplay root has an authored transform; restore it to identity before deployment.");
                root.SetActive(false);
                var map = GetOrCreate<MapTestSceneController>(root, "500m Prairie V1 Height Map");
                EditorUtility.CopySerialized(sourceMap, map);
                // Reacquire after opening the template as well; never assign a stale Unity object.
                level = LoadLevel();
                var mapSettings = new SerializedObject(map);
                mapSettings.FindProperty("levelAsset").objectReferenceValue = level;
                mapSettings.FindProperty("editorHeightResolution").intValue = EditorResolution;
                mapSettings.ApplyModifiedPropertiesWithoutUndo();
                Require(map.LevelAsset != null && map.LevelAsset == level,
                    "Could not retain the independent sample level on the target map.");
                map.enabled = true;
                var bootstrap = GetOrCreate<HeightMapPlayerSceneBootstrap>(root, "Prairie Sample Player Bootstrap");
                EditorUtility.CopySerialized(sourceBootstrap, bootstrap);
                var playerSettings = new SerializedObject(bootstrap);
                playerSettings.FindProperty("playerSpawnMapPositionMeters").vector2Value = spawn;
                playerSettings.FindProperty("showRobotTerrainData").boolValue = true;
                playerSettings.ApplyModifiedPropertiesWithoutUndo();
                bootstrap.map = map;
                bootstrap.mapObject = map.gameObject;
                bootstrap.enabled = true;
                EventSystem events = GetOrCreate<EventSystem>(root, "EventSystem");
                if (events.GetComponent<StandaloneInputModule>() == null) Undo.AddComponent<StandaloneInputModule>(events.gameObject);
                events.enabled = true;
                foreach (Camera camera in Components<Camera>(target))
                    if (camera.enabled && camera.gameObject.activeInHierarchy) camera.gameObject.SetActive(false);
                root.SetActive(true);
                map.SendMessage("RebuildGeneratedMap", SendMessageOptions.RequireReceiver);
                WriteInitializationDiagnostic(target, "Configured target while additive template is open");
                EditorUtility.SetDirty(map);
                EditorUtility.SetDirty(bootstrap);
                EditorSceneManager.MarkSceneDirty(target);
                Selection.activeGameObject = bootstrap.gameObject;
            }
            finally
            {
                EditorSceneManager.CloseScene(template, true);
                if (SceneManager.GetActiveScene() != target) SceneManager.SetActiveScene(target);
            }
            // Closing an additive ExecuteAlways template can invalidate editor-owned previews.
            // Reacquire the target component after that lifecycle boundary and initialize its own data.
            var deployedMaps = Components<MapTestSceneController>(target);
            Require(deployedMaps.Length == 1,
                "Expected one target map after closing the gameplay template. " + DescribeMaps(target));
            var deployedMap = deployedMaps[0];
            level = LoadLevel();
            Require(level != null && deployedMap.LevelAsset != null && deployedMap.LevelAsset == level
                && deployedMap.gameObject.activeInHierarchy && deployedMap.enabled,
                "Target map lost its level assignment or active state after closing the template. " + DescribeMaps(target));
            deployedMap.SendMessage("RebuildGeneratedMap", SendMessageOptions.RequireReceiver);
            WriteInitializationDiagnostic(target, "Rebuilt target after additive template closed");
            Validate();
            Require(EditorSceneManager.SaveScene(target), "Could not save the independent prairie gameplay test.");
            SceneView view = SceneView.lastActiveSceneView;
            if (view != null)
            {
                var map = Components<MapTestSceneController>(target).Single();
                view.in2DMode = true;
                view.LookAt(map.WorldBounds.center, Quaternion.identity, map.WorldBounds.size.y * 0.58f, true);
            }
        }

        [MenuItem("Animal Game/Plains Sample 500m/Validate V1 Gameplay Test")]
        public static void Validate()
        {
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.path == ScenePath && SceneManager.sceneCount == 1, "Open the prairie sample gameplay test by itself.");
            TiledLevelAsset level = LoadLevel();
            var maps = Components<MapTestSceneController>(scene);
            var players = Components<HeightMapPlayerSceneBootstrap>(scene);
            Require(maps.Length == 1 && maps[0].enabled && maps[0].HasGeneratedMap,
                "The prairie sample map did not initialize. " + DescribeMaps(scene));
            Require(players.Length == 1 && players[0].enabled, "Expected one sample player bootstrap.");
            var map = maps[0];
            var player = players[0];
            Require(map.LevelAsset == level && map.MapSizeMeters == Vector2.one * 500, "Wrong sample level or map extent.");
            Require(map.transform.lossyScale == Vector3.one && Quaternion.Angle(map.transform.rotation, Quaternion.identity) < 0.001f,
                "The sample map must retain its unit transform.");
            Require(Mathf.Abs(map.WorldBounds.size.x - 500 * WorldScale) < 0.001f
                && Mathf.Abs(map.WorldBounds.size.y - 500 * WorldScale) < 0.001f
                && Mathf.Abs(map.MapMetersToWorldDistance(Vector2.right, 1) - WorldScale) < 0.00001f,
                "The sample gameplay world does not preserve V1 world units per logical meter.");
            var events = Components<EventSystem>(scene);
            Require(events.Length == 1 && events[0].enabled && events[0].GetComponent<StandaloneInputModule>() != null,
                "The sample requires one complete EventSystem.");
            Vector2 spawn = player.PlayerSpawnMapPositionMeters;
            Require(map.TrySampleMapPosition(spawn, out float spawnHeight), "The starting position has no height data.");
            Require((spawn - ChooseSpawn(level)).sqrMagnitude < 0.01f, "Sample spawn no longer matches its exported gentle position.");
            Camera resourceCamera = RequirePlayerResources();
            var report = new StringBuilder($"PASS: Plains_Sample_500m_Test ({(Application.isPlaying ? "Play" : "Edit")}).\n");
            report.AppendLine($"Independent level: {LevelPath}; extent 500 x 500 m; world size {map.WorldBounds.size.x:F6}; V1 scale {WorldScale:F9}.");
            report.AppendLine($"Spawn {spawn}; height {spawnHeight:F4} m; four 2049 R16 sources; 0..350 m encoding; contours 7 m; black background.");
            if (Application.isPlaying)
            {
                Require(player.robot != null && player.traversalEvaluator != null && player.traversalEvaluator.IsInitialized,
                    "Runtime player movement or traversal did not initialize.");
                Require(player.map == map && player.mapCamera != null && player.cameraFollow != null && player.scanOverlay != null
                    && player.scanChargeUi != null && player.mainUiCanvas != null, "The original camera, scan or UI did not initialize.");
                Require(map.RuntimeMap.TileCount == 4 && map.RuntimeMap.Tiles.All(tile => tile.Field.Width == RuntimeResolution
                    && tile.Field.Height == RuntimeResolution), "Runtime did not generate four full V1 height fields.");
                Require(Components<RobotMover>(scene).Length == 1 && Components<Camera>(scene)
                    .Count(camera => camera.enabled && camera.gameObject.activeInHierarchy) == 1, "Expected one robot and one gameplay camera.");
                Require(player.cameraShake != null && Mathf.Abs(player.cameraShake.BaseOrthographicSize - resourceCamera.orthographicSize) < 0.001f,
                    "Runtime camera size differs from the original player prefab.");
                Require(player.mapCamera.backgroundColor == Color.black, "Runtime camera background is not black.");
                var cameraData = player.mapCamera.GetComponent<UniversalAdditionalCameraData>();
                Require(cameraData != null && cameraData.scriptableRenderer.GetType().Name == "Renderer2D",
                    "Runtime gameplay must retain the original 2D renderer.");
                Require(map.TrySampleWorldPosition(player.robot.transform.position, out Vector2 position, out float height),
                    "Runtime robot is outside the sample map.");
                report.AppendLine($"Player initialized at {position}, height {height:F4} m; camera size {player.cameraShake.BaseOrthographicSize:F3}; scan/UI initialized.");
            }
            else
            {
                Require(new SerializedObject(map).FindProperty("editorHeightResolution").intValue == EditorResolution,
                    "The editor preview resolution changed.");
                Require(Components<Camera>(scene).All(camera => !camera.enabled || !camera.gameObject.activeInHierarchy),
                    "An authored camera would compete with the runtime player camera.");
                var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                Require(pipeline != null && pipeline.scriptableRenderer.GetType().Name == "Renderer2D",
                    "The original 2D pipeline is required for this gameplay test.");
                report.AppendLine($"Editor preview {EditorResolution}; original player camera size {resourceCamera.orthographicSize:F3}.");
            }
            report.AppendLine("Original player, traversal, camera, scan and UI resources are reused unchanged. No prebaked cache assigned.");
            Directory.CreateDirectory(ReportFolder);
            File.WriteAllText(ReportFolder + (Application.isPlaying ? "/PlayValidation.txt" : "/EditValidation.txt"), report.ToString(), new UTF8Encoding(false));
            Debug.Log(report.ToString(), map);
        }

        private static TiledLevelAsset LoadLevel()
        {
            var level = AssetDatabase.LoadAssetAtPath<TiledLevelAsset>(LevelPath);
            ValidateLevel(level);
            return level;
        }

        private static void ValidateLevel(TiledLevelAsset level)
        {
            Require(level != null && level.IsValid && level.Tiles.Count == 4 && level.MapSizeMeters == Vector2.one * 500
                && level.TileSizeMeters == Vector2.one * TileSize && (level.WorldUnitsPerMeter - Vector2.one * WorldScale).sqrMagnitude < 0.00000001f,
                "Export four valid V1 sample tiles before deploying the gameplay scene.");
            foreach (var tile in level.Tiles)
            {
                HeightMapLevelAsset data = tile.level;
                Require(AssetDatabase.GetAssetPath(data).StartsWith(Folder + "/", StringComparison.Ordinal)
                    && data.HeightMap != null && AssetDatabase.GetAssetPath(data.HeightMap).StartsWith(Folder + "/data/", StringComparison.Ordinal),
                    "Sample gameplay must use independent local map assets.");
                Require(data.HeightMap.width == Resolution && data.HeightMap.height == Resolution && data.HeightMap.format == TextureFormat.R16
                    && data.BakedHeightResolution == RuntimeResolution && data.MinimumHeightMeters == 0 && data.MaximumHeightMeters == HeightRange
                    && data.ContourIntervalMeters == 7 && data.BackgroundColor == Color.black && !data.NormalizeSourceRange
                    && data.SurfaceSmoothingSigmaMeters == 0 && data.DetailSmoothingSigmaMeters == 0 && data.PrebakedHeightField == null,
                    "Sample export lost its fixed height encoding, R16 precision, V1 resolution, contour spacing or independent data settings.");
            }
        }

        private static Vector2 ChooseSpawn(TiledLevelAsset level)
        {
            Vector2 best = Vector2.zero;
            float bestScore = float.NegativeInfinity;
            for (int z = 60; z <= 440; z += 10) for (int x = 60; x <= 440; x += 10)
            {
                Vector2 point = new Vector2(x, z);
                float dx = (SampleHeight(level, point + Vector2.right) - SampleHeight(level, point - Vector2.right)) * 0.5f;
                float dz = (SampleHeight(level, point + Vector2.up) - SampleHeight(level, point - Vector2.up)) * 0.5f;
                float slope = Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg;
                if (slope > 10) continue;
                float height = SampleHeight(level, point), low = height, high = height;
                for (int direction = 0; direction < 16; direction++)
                {
                    float angle = direction * Mathf.PI / 8;
                    Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 40;
                    float nearby = SampleHeight(level, point + offset);
                    low = Mathf.Min(low, nearby); high = Mathf.Max(high, nearby);
                }
                float relief = high - low;
                if (relief < 8 || high - height < 6) continue;
                float score = -Mathf.Abs(relief - 13) - slope * 0.5f - (point - Vector2.one * 250).magnitude * 0.005f;
                if (score > bestScore) { bestScore = score; best = point; }
            }
            Require(!float.IsNegativeInfinity(bestScore), "No gentle starting position with visible 7 m relief was found in the sample.");
            return best;
        }

        private static float SampleHeight(TiledLevelAsset level, Vector2 point)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt(point.x / TileSize), 0, 1);
            int z = Mathf.Clamp(Mathf.FloorToInt(point.y / TileSize), 0, 1);
            HeightMapLevelAsset tile = level.Tiles.First(entry => entry.coordinate == new Vector2Int(x, z)).level;
            float u = Mathf.Clamp01((point.x - x * TileSize) / TileSize) * (Resolution - 1);
            float v = Mathf.Clamp01((point.y - z * TileSize) / TileSize) * (Resolution - 1);
            int left = Mathf.FloorToInt(u), bottom = Mathf.FloorToInt(v);
            int right = Mathf.Min(left + 1, Resolution - 1), top = Mathf.Min(bottom + 1, Resolution - 1);
            var pixels = tile.HeightMap.GetPixelData<ushort>(0);
            return Mathf.Lerp(Mathf.Lerp(pixels[bottom * Resolution + left], pixels[bottom * Resolution + right], u - left),
                Mathf.Lerp(pixels[top * Resolution + left], pixels[top * Resolution + right], u - left), v - bottom) / 65535f * HeightRange;
        }

        private static void VerifyTerrainSeams(Terrain[] terrains)
        {
            VerifySeam(terrains[0], terrains[1], true); VerifySeam(terrains[2], terrains[3], true);
            VerifySeam(terrains[0], terrains[2], false); VerifySeam(terrains[1], terrains[3], false);
        }

        private static void VerifySeam(Terrain first, Terrain second, bool horizontal)
        {
            first.terrainData.SyncHeightmap(); second.terrainData.SyncHeightmap();
            float[,] a = first.terrainData.GetHeights(horizontal ? Resolution - 1 : 0, horizontal ? 0 : Resolution - 1,
                horizontal ? 1 : Resolution, horizontal ? Resolution : 1);
            float[,] b = second.terrainData.GetHeights(0, 0, horizontal ? 1 : Resolution, horizontal ? Resolution : 1);
            for (int index = 0; index < Resolution; index++)
                Require(Mathf.Abs(horizontal ? a[index, 0] - b[index, 0] : a[0, index] - b[0, index]) <= 0.0000001f,
                    "The prairie sample contains a mismatched shared Terrain border.");
        }

        private static Camera RequirePlayerResources()
        {
            foreach (string path in new[] { "Robot/RobotMarker", "Camera/RobotCamera", "Traversal/HeightMapTraversalEvaluator",
                "Traversal/TraversalOverlay", "Traversal/TraversalScanOverlay", "UI/MainUI" })
                Require(Resources.Load<GameObject>(path) != null, "Missing original gameplay resource: " + path);
            GameObject prefab = Resources.Load<GameObject>("Camera/RobotCamera");
            Camera camera = prefab.GetComponent<Camera>();
            Require(camera != null && camera.orthographic && prefab.GetComponent<RobotCameraFollow>() != null,
                "The original player camera prefab is incomplete.");
            return camera;
        }

        private static T GetOrCreate<T>(GameObject root, string name) where T : Component
        {
            T[] existing = root.GetComponentsInChildren<T>(true);
            Require(existing.Length <= 1, "Duplicate sample component: " + typeof(T).Name);
            if (existing.Length == 1) { existing[0].gameObject.SetActive(true); return existing[0]; }
            var obj = new GameObject(name);
            obj.transform.SetParent(root.transform, false);
            Undo.RegisterCreatedObjectUndo(obj, "Create " + name);
            return Undo.AddComponent<T>(obj);
        }

        private static T[] Components<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

        private static void WriteInitializationDiagnostic(Scene scene, string stage)
        {
            Directory.CreateDirectory(ReportFolder);
            File.AppendAllText(ReportFolder + "/InitializationDiagnostic.txt",
                stage + "\n" + DescribeMaps(scene) + "\n", new UTF8Encoding(false));
        }

        private static string DescribeMaps(Scene scene)
        {
            var descriptions = Components<MapTestSceneController>(scene).Select(map =>
                $"Map '{map.name}' id={map.GetInstanceID()} scene='{map.gameObject.scene.path}' enabled={map.enabled} "
                + $"activeSelf={map.gameObject.activeSelf} activeHierarchy={map.gameObject.activeInHierarchy} "
                + $"level='{AssetDatabase.GetAssetPath(map.LevelAsset)}' hasGeneratedMap={map.HasGeneratedMap} "
                + $"runtimeTiles={(map.RuntimeMap != null ? map.RuntimeMap.TileCount : -1)} "
                + $"spriteChildren={map.GetComponentsInChildren<SpriteRenderer>(true).Length}");
            return $"Scene '{scene.path}', active='{SceneManager.GetActiveScene().path}', loadedScenes={SceneManager.sceneCount}; "
                + "roots=[" + string.Join(", ", scene.GetRootGameObjects().Select(root => root.name)) + "]; "
                + string.Join("; ", descriptions);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
