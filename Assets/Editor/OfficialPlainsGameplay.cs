using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace AnimalGame.Editor
{
    /// <summary>Exports the currently authored official prairie without modifying its height samples.</summary>
    public static class OfficialPlainsGameplay
    {
        public const string AuthoringScenePath = "Assets/Scenes/GameMaps/Official_Map_01.unity";
        public const string ScenePath = "Assets/Scenes/GameMaps/Official_Map_Test_01.unity";
        public const string Folder = "Assets/Maps/Official_Map_01/Gameplay";
        public const string LevelPath = Folder + "/OfficialPlains_TiledLevel.asset";
        public const string ReportFolder = "Temp/OfficialPlainsGameplay";
        public const float WorldScale = 2048f / 30f / 250f;
        private const string TemplateScenePath = "Assets/Scenes/TerrainHeightMapTestScene.unity";
        private const string RootName = "Official Plains - V1 Gameplay";
        private const int Resolution = 2049, RuntimeResolution = 2048, EditorResolution = 512;
        private const float TileSize = 250f, HeightRange = 350f;

        /// <summary>An independent numbered export; index 1 retains the original paths.</summary>
        public sealed class ExportDestination
        {
            public int Index { get; }
            public string ScenePath { get; }
            public string Folder { get; }
            public string LevelPath => Folder + "/OfficialPlains_TiledLevel.asset";
            public string ReportFolder { get; }

            public ExportDestination(int index)
            {
                if (index < 1) throw new ArgumentOutOfRangeException(nameof(index));
                Index = index;
                string suffix = index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
                ScenePath = "Assets/Scenes/GameMaps/Official_Map_Test_" + suffix + ".unity";
                Folder = index == 1 ? OfficialPlainsGameplay.Folder : OfficialPlainsGameplay.Folder + "_" + suffix;
                ReportFolder = index == 1 ? OfficialPlainsGameplay.ReportFolder : OfficialPlainsGameplay.ReportFolder + "_" + suffix;
            }
        }

        public static ExportDestination CreateNextDestination()
        {
            for (int index = 1; index < int.MaxValue; index++)
            {
                var destination = new ExportDestination(index);
                if (!File.Exists(destination.ScenePath) && !File.Exists(destination.ScenePath + ".meta")
                    && !Directory.Exists(destination.Folder) && !File.Exists(destination.Folder + ".meta")) return destination;
            }
            throw new InvalidOperationException("No unused official prairie export number remains.");
        }

        [MenuItem("Animal Game/Official Map 01/Export Current Plains as New Gameplay")]
        public static void ExportNew() => ExportCurrent(CreateNextDestination());

        [MenuItem("Animal Game/Official Map 01/Export Current Plains and Open Gameplay")]
        public static void ExportCurrent()
        {
            ExportCurrent(new ExportDestination(1));
        }

        public static void ExportCurrent(ExportDestination destination)
        {
            Export(GetSourceTerrains(), destination);
            Deploy(destination);
        }

        public static Terrain[] GetSourceTerrains()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play before exporting the official prairie.");
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.path == AuthoringScenePath, "Open Official_Map_01 before exporting its current prairie.");
            var roots = scene.GetRootGameObjects().Where(root => root.name == OfficialPlainsTerrainBuilder.RootName).ToArray();
            Require(roots.Length == 1, "Expected one official editable prairie root.");
            Terrain[] terrains = roots[0].GetComponentsInChildren<Terrain>(true);
            Require(terrains.Length == 40 && terrains.Select(t => t.terrainData).Distinct().Count() == terrains.Length,
                "The current official prairie must have 40 distinct TerrainData tiles.");
            return terrains.OrderBy(t => t.transform.position.z).ThenBy(t => t.transform.position.x).ToArray();
        }

        public static void Export(Terrain[] terrains)
        {
            Export(terrains, new ExportDestination(1));
        }

        public static void Export(Terrain[] terrains, ExportDestination destination)
        {
            Require(destination != null, "An official prairie export destination is required.");
            Terrain[] source = GetSourceTerrains();
            Require(terrains != null && terrains.Length == source.Length && terrains.Distinct().Count() == source.Length
                && terrains.All(source.Contains), "Export only the current official prairie Terrain objects.");
            foreach (Terrain terrain in source)
            {
                TerrainData data = terrain.terrainData;
                Require(data != null && data.heightmapResolution == Resolution && data.size == new Vector3(TileSize, HeightRange, TileSize)
                    && terrain.transform.lossyScale == Vector3.one
                    && Quaternion.Angle(terrain.transform.rotation, Quaternion.identity) < 0.001f,
                    terrain.name + ": expected upright unit-scale 250 x 350 x 250 m Terrain with 2049 samples.");
                data.SyncHeightmap();
                AssetDatabase.SaveAssetIfDirty(data);
            }
            SaveLoadedScenes();
            float west = source.Min(t => t.transform.position.x), south = source.Min(t => t.transform.position.z);
            var byCell = new Dictionary<Vector2Int, Terrain>();
            foreach (Terrain terrain in source)
            {
                Vector2 cell = new Vector2((terrain.transform.position.x - west) / TileSize,
                    (terrain.transform.position.z - south) / TileSize);
                Vector2Int key = new Vector2Int(Mathf.RoundToInt(cell.x), Mathf.RoundToInt(cell.y));
                Require((cell - key).sqrMagnitude < 0.0000001f && !byCell.ContainsKey(key),
                    "Official prairie tiles overlap or do not lie on their 250 m grid.");
                byCell.Add(key, terrain);
            }
            EnsureFolder(destination.Folder + "/data");
            Directory.CreateDirectory(destination.ReportFolder);
            var report = new StringBuilder("Official prairie: current native Terrain exported without resculpting.\n");
            var manifest = new ExportManifest { sourceOrigin = new Vector3(west, 0, south), tileCount = source.Length };
            CheckSeams(byCell, report, manifest);
            var entries = new List<TiledLevelAsset.Tile>();
            try
            {
                int index = 0;
                foreach (var pair in byCell.OrderBy(pair => pair.Key.y).ThenBy(pair => pair.Key.x))
                {
                    Terrain terrain = pair.Value;
                    string stem = $"Plains_{pair.Key.x}_{pair.Key.y}";
                    EditorUtility.DisplayProgressBar("Export current official prairie", $"{++index}/{source.Length}: {terrain.name}", index / (float)source.Length);
                    float[,] heights = terrain.terrainData.GetHeights(0, 0, Resolution, Resolution);
                    float sourceY = terrain.transform.position.y;
                    string invalidHeightMessage = terrain.name + ": absolute source height is outside the shared 0..350 m encoding.";
                    var raw = new byte[Resolution * Resolution * 2];
                    ushort minimum = ushort.MaxValue, maximum = 0;
                    for (int z = 0; z < Resolution; z++) for (int x = 0; x < Resolution; x++)
                    {
                        // Preserve actual absolute metres even if the source root has a Y translation.
                        float meters = sourceY + heights[z, x] * HeightRange;
                        Require(!float.IsNaN(meters) && !float.IsInfinity(meters) && meters >= 0 && meters <= HeightRange,
                            invalidHeightMessage);
                        ushort value = (ushort)Mathf.RoundToInt(meters / HeightRange * 65535);
                        int offset = (z * Resolution + x) * 2;
                        raw[offset] = (byte)value; raw[offset + 1] = (byte)(value >> 8);
                        minimum = (ushort)Math.Min(minimum, value); maximum = (ushort)Math.Max(maximum, value);
                    }
                    string pngPath = destination.Folder + "/data/" + stem + "_height_R16.png";
                    TerrainGridV1Exporter.WriteGray16Png(pngPath, raw);
                    TerrainGridV1Exporter.ValidatePngHeader(pngPath);
                    Texture2D texture = TerrainGridV1Exporter.ImportAndVerify(pngPath, raw);
                    HeightMapLevelAsset data = TerrainGridV1Exporter.ConfigureLevel(stem, texture, destination.Folder, TileSize, RuntimeResolution);
                    var settings = new SerializedObject(data);
                    settings.FindProperty("minimumHeightMeters").floatValue = 0;
                    settings.FindProperty("maximumHeightMeters").floatValue = HeightRange;
                    settings.FindProperty("contourIntervalMeters").floatValue = 7;
                    settings.FindProperty("backgroundColor").colorValue = Color.black;
                    settings.FindProperty("prebakedHeightField").objectReferenceValue = null;
                    settings.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(data); AssetDatabase.SaveAssetIfDirty(data);
                    entries.Add(new TiledLevelAsset.Tile(pair.Key, data));
                    var tile = new TileManifest
                    {
                        name = terrain.name, coordinate = pair.Key, nativeOrigin = terrain.transform.position,
                        tileOriginMeters = (Vector2)pair.Key * TileSize,
                        sourceTerrainData = AssetDatabase.GetAssetPath(terrain.terrainData),
                        heightMapAsset = pngPath, mapDataAsset = AssetDatabase.GetAssetPath(data),
                        minimumHeightMeters = minimum / 65535f * HeightRange,
                        maximumHeightMeters = maximum / 65535f * HeightRange,
                        quantizedSamplesSha256 = TerrainGridV1Exporter.Sha256(raw)
                    };
                    manifest.tiles.Add(tile);
                    report.AppendLine($"{terrain.name} -> {pair.Key}: native origin {terrain.transform.position}; height {tile.minimumHeightMeters:F4}..{tile.maximumHeightMeters:F4} m; every imported R16 pixel verified.");
                }
                TiledLevelAsset level = AssetDatabase.LoadAssetAtPath<TiledLevelAsset>(destination.LevelPath);
                bool create = level == null;
                Require(!create || AssetDatabase.LoadMainAssetAtPath(destination.LevelPath) == null, "An unrelated asset occupies " + destination.LevelPath);
                if (create) level = ScriptableObject.CreateInstance<TiledLevelAsset>();
                level.Configure(Vector2.one * TileSize, Vector2.one * WorldScale, entries[0].level, entries, true);
                if (create) AssetDatabase.CreateAsset(level, destination.LevelPath);
                EditorUtility.SetDirty(level); AssetDatabase.SaveAssetIfDirty(level);
                ValidateLevel(level, destination);
                manifest.mapSizeMeters = level.MapSizeMeters;
                manifest.spawnMapPositionMeters = ChooseSpawn(level);
                manifest.minimumHeightMeters = manifest.tiles.Min(tile => tile.minimumHeightMeters);
                manifest.maximumHeightMeters = manifest.tiles.Max(tile => tile.maximumHeightMeters);
                File.WriteAllText(destination.Folder + "/world-manifest.json", JsonUtility.ToJson(manifest, true), new UTF8Encoding(false));
                AssetDatabase.ImportAsset(destination.Folder + "/world-manifest.json");
                report.AppendLine($"PASS: {entries.Count} sparse tiles; extent {level.MapSizeMeters}; occupied area {entries.Count * TileSize * TileSize:F0} m2.");
                report.AppendLine($"Fixed V1 scale {WorldScale:F9}, 0..350 m encoding, 7 m contours, black background; start {manifest.spawnMapPositionMeters}.");
                report.AppendLine("No normalization, smoothing, RAW files, prebake assets or changes to authored Terrain heights. Missing tiles remain outside the playable map.");
                File.WriteAllText(destination.ReportFolder + "/ExportValidation.txt", report.ToString(), new UTF8Encoding(false));
                Debug.Log(report.ToString(), level);
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        [MenuItem("Animal Game/Official Map 01/Open Exported Plains Gameplay")]
        public static void Deploy()
        {
            Deploy(new ExportDestination(1));
        }

        public static void Deploy(ExportDestination destination)
        {
            Require(destination != null, "An official prairie export destination is required.");
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play before deploying the official prairie.");
            LoadLevel(destination); RequirePlayerResources(); SaveLoadedScenes();
            Scene target = SceneManager.GetActiveScene();
            if (target.path != destination.ScenePath || SceneManager.sceneCount != 1)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(destination.ScenePath) != null)
                    target = EditorSceneManager.OpenScene(destination.ScenePath, OpenSceneMode.Single);
                else
                {
                    target = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    Require(EditorSceneManager.SaveScene(target, destination.ScenePath), "Could not create the official gameplay test scene.");
                }
            }
            TiledLevelAsset level = LoadLevel(destination);
            GameObject root = target.GetRootGameObjects().SingleOrDefault(obj => obj.name == RootName);
            Require(Components<MapTestSceneController>(target).All(c => root != null && c.transform.IsChildOf(root.transform))
                && Components<HeightMapPlayerSceneBootstrap>(target).All(c => root != null && c.transform.IsChildOf(root.transform))
                && Components<EventSystem>(target).All(c => root != null && c.transform.IsChildOf(root.transform)),
                "The target scene contains authored gameplay objects outside its managed official prairie root.");
            Require(Components<Terrain>(target).Length == 0, "Refusing to replace an authored 3D Terrain scene.");
            foreach (Camera camera in Components<Camera>(target).Where(c => c.enabled && c.gameObject.activeInHierarchy))
                Require(camera.name == "Main Camera" && camera.transform.parent == null && camera.transform.childCount == 0
                    && camera.GetComponents<Component>().All(c => c is Transform || c is Camera || c is AudioListener || c is UniversalAdditionalCameraData),
                    "An authored active camera must be disabled before deploying this gameplay scene.");
            Vector2 spawn = ChooseSpawn(level);
            Scene template = EditorSceneManager.OpenScene(TemplateScenePath, OpenSceneMode.Additive);
            try
            {
                var sourceMap = Components<MapTestSceneController>(template).Single();
                var sourcePlayer = Components<HeightMapPlayerSceneBootstrap>(template).Single();
                if (SceneManager.GetActiveScene() != target)
                    Require(SceneManager.SetActiveScene(target), "Could not activate the official gameplay test.");
                if (root == null)
                {
                    root = new GameObject(RootName); SceneManager.MoveGameObjectToScene(root, target);
                    Undo.RegisterCreatedObjectUndo(root, "Create official prairie gameplay");
                }
                Require(root.transform.position == Vector3.zero && root.transform.localScale == Vector3.one
                    && Quaternion.Angle(root.transform.rotation, Quaternion.identity) < 0.001f,
                    "The official gameplay root must have its original unit transform.");
                root.SetActive(false);
                var map = GetOrCreate<MapTestSceneController>(root, "Official Plains V1 Height Map");
                EditorUtility.CopySerialized(sourceMap, map);
                level = LoadLevel(destination);
                var settings = new SerializedObject(map);
                settings.FindProperty("levelAsset").objectReferenceValue = level;
                settings.FindProperty("editorHeightResolution").intValue = EditorResolution;
                settings.ApplyModifiedPropertiesWithoutUndo(); map.enabled = true;
                Require(map.LevelAsset == level, "The target map did not retain its official prairie level.");
                var player = GetOrCreate<HeightMapPlayerSceneBootstrap>(root, "Official Plains Player Bootstrap");
                EditorUtility.CopySerialized(sourcePlayer, player);
                settings = new SerializedObject(player);
                settings.FindProperty("playerSpawnMapPositionMeters").vector2Value = spawn;
                settings.FindProperty("showRobotTerrainData").boolValue = true;
                settings.ApplyModifiedPropertiesWithoutUndo();
                player.map = map; player.mapObject = map.gameObject; player.enabled = true;
                var events = GetOrCreate<EventSystem>(root, "EventSystem");
                if (events.GetComponent<StandaloneInputModule>() == null) Undo.AddComponent<StandaloneInputModule>(events.gameObject);
                events.enabled = true;
                foreach (Camera camera in Components<Camera>(target))
                    if (camera.enabled && camera.gameObject.activeInHierarchy) camera.gameObject.SetActive(false);
                root.SetActive(true);
                EditorUtility.SetDirty(map); EditorUtility.SetDirty(player);
                EditorSceneManager.MarkSceneDirty(target); Selection.activeGameObject = player.gameObject;
            }
            finally
            {
                EditorSceneManager.CloseScene(template, true);
                if (SceneManager.GetActiveScene() != target) SceneManager.SetActiveScene(target);
            }
            // Closing an ExecuteAlways template can dispose previews. Reacquire and rebuild in the final scene.
            var deployed = Components<MapTestSceneController>(target).Single();
            Require(deployed.LevelAsset == LoadLevel(destination) && deployed.enabled && deployed.gameObject.activeInHierarchy,
                "The official map assignment was lost while closing the template.");
            deployed.SendMessage("RebuildGeneratedMap", SendMessageOptions.RequireReceiver);
            Validate();
            Require(EditorSceneManager.SaveScene(target), "Could not save the official prairie gameplay test.");
            SceneView view = SceneView.lastActiveSceneView;
            if (view != null)
            {
                view.in2DMode = true;
                view.LookAt(deployed.WorldBounds.center, Quaternion.identity, Mathf.Max(deployed.WorldBounds.size.x, deployed.WorldBounds.size.y) * 0.58f, true);
            }
        }

        [MenuItem("Animal Game/Official Map 01/Validate Plains Gameplay")]
        public static void Validate()
        {
            Scene scene = SceneManager.GetActiveScene();
            ExportDestination destination = ResolveDestination(scene.path);
            Require(SceneManager.sceneCount == 1, "Open the official prairie gameplay test by itself.");
            TiledLevelAsset level = LoadLevel(destination);
            var maps = Components<MapTestSceneController>(scene);
            var players = Components<HeightMapPlayerSceneBootstrap>(scene);
            Require(maps.Length == 1 && maps[0].enabled && maps[0].HasGeneratedMap, "The official prairie map did not initialize.");
            Require(players.Length == 1 && players[0].enabled, "Expected one official prairie player bootstrap.");
            var map = maps[0]; var player = players[0];
            Require(map.LevelAsset == level && map.MapSizeMeters == level.MapSizeMeters && map.RuntimeMap.TileCount == level.Tiles.Count,
                "Gameplay does not use all exported prairie tiles.");
            Require(map.transform.lossyScale == Vector3.one && Quaternion.Angle(map.transform.rotation, Quaternion.identity) < 0.001f
                && Mathf.Abs(map.WorldBounds.size.x - level.MapSizeMeters.x * WorldScale) < 0.001f
                && Mathf.Abs(map.WorldBounds.size.y - level.MapSizeMeters.y * WorldScale) < 0.001f
                && Mathf.Abs(map.MapMetersToWorldDistance(Vector2.right, 1) - WorldScale) < 0.00001f,
                "The gameplay map no longer preserves the V1 metre scale.");
            var events = Components<EventSystem>(scene);
            Require(events.Length == 1 && events[0].enabled && events[0].GetComponent<StandaloneInputModule>() != null,
                "Expected one complete EventSystem.");
            Vector2 spawn = player.PlayerSpawnMapPositionMeters;
            Require((spawn - ChooseSpawn(level)).sqrMagnitude < 0.01f && map.TrySampleMapPosition(spawn, out _),
                "The player start no longer matches the exported gentle terrain position.");
            Camera resourceCamera = RequirePlayerResources();
            var sampler = new ExportSampler(level);
            float maximumProbeError = 0;
            foreach (var tile in level.Tiles)
            {
                foreach (Vector2 uv in new[] { new Vector2(.2f, .2f), new Vector2(.5f, .5f), new Vector2(.8f, .8f) })
                {
                    Vector2 point = (Vector2)tile.coordinate * TileSize + uv * TileSize;
                    Require(map.TrySampleMapPosition(point, out float actual) && sampler.TrySample(point, out _), "A resident tile has no height data.");
                    sampler.TrySample(point, out float expected);
                    float difference = Mathf.Abs(actual - expected);
                    maximumProbeError = Mathf.Max(maximumProbeError, difference);
                    Require(difference < (Application.isPlaying ? 0.15f : 2f), "Gameplay height differs from the exported source at " + point);
                }
            }
            var report = new StringBuilder($"PASS: {Path.GetFileNameWithoutExtension(destination.ScenePath)} ({(Application.isPlaying ? "Play" : "Edit")}).\n");
            report.AppendLine($"{level.Tiles.Count} resident tiles; extent {level.MapSizeMeters}; occupied area {level.Tiles.Count * TileSize * TileSize:F0} m2; V1 scale {WorldScale:F9}.");
            report.AppendLine($"Absolute 0..350 m R16 source; 7 m contours; black background; spawn {spawn}; maximum height probe error {maximumProbeError:F5} m.");
            if (Application.isPlaying)
            {
                Require(player.robot != null && player.traversalEvaluator != null && player.traversalEvaluator.IsInitialized
                    && player.map == map && player.mapCamera != null && player.cameraFollow != null && player.scanOverlay != null
                    && player.scanChargeUi != null && player.mainUiCanvas != null, "The original player, traversal, camera, scan or UI failed to initialize.");
                Require(map.RuntimeMap.Tiles.All(tile => tile.Field.Width == RuntimeResolution && tile.Field.Height == RuntimeResolution),
                    "Every runtime tile must retain its full 2048 x 2048 V1 height field.");
                Require(Components<RobotMover>(scene).Length == 1 && Components<Camera>(scene).Count(c => c.enabled && c.gameObject.activeInHierarchy) == 1,
                    "Expected one player and one active gameplay camera.");
                Require(player.cameraShake != null && Mathf.Abs(player.cameraShake.BaseOrthographicSize - resourceCamera.orthographicSize) < 0.001f
                    && player.mapCamera.backgroundColor == Color.black, "The player camera lost its original scale or black background.");
                var cameraData = player.mapCamera.GetComponent<UniversalAdditionalCameraData>();
                Require(cameraData != null && cameraData.scriptableRenderer.GetType().Name == "Renderer2D", "The gameplay camera must retain the original 2D renderer.");
                Require(map.TrySampleWorldPosition(player.robot.transform.position, out _, out _), "The player spawned outside the occupied prairie tiles.");
                report.AppendLine($"Full runtime sampling; original camera size {player.cameraShake.BaseOrthographicSize:F3}; movement/scan/UI initialized.");
            }
            else
            {
                Require(new SerializedObject(map).FindProperty("editorHeightResolution").intValue == EditorResolution
                    && map.RuntimeMap.Tiles.All(tile => tile.Field.Width == EditorResolution && tile.Field.Height == EditorResolution), "Editor preview must use 512 samples per tile.");
                Require(Components<Camera>(scene).All(c => !c.enabled || !c.gameObject.activeInHierarchy), "An authored camera would compete with the player camera.");
                var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                Require(pipeline != null && pipeline.scriptableRenderer.GetType().Name == "Renderer2D", "The original gameplay 2D pipeline is required.");
                report.AppendLine("Editor preview 512 samples per tile; Play uses full 2048 samples. No prebake cache assigned.");
            }
            Directory.CreateDirectory(destination.ReportFolder);
            File.WriteAllText(destination.ReportFolder + (Application.isPlaying ? "/PlayValidation.txt" : "/EditValidation.txt"), report.ToString(), new UTF8Encoding(false));
            Debug.Log(report.ToString(), map);
        }

        private static ExportDestination ResolveDestination(string scenePath)
        {
            const string prefix = "Assets/Scenes/GameMaps/Official_Map_Test_";
            const string extension = ".unity";
            Require(scenePath.StartsWith(prefix, StringComparison.Ordinal) && scenePath.EndsWith(extension, StringComparison.Ordinal),
                "Open a numbered official prairie gameplay test by itself.");
            string number = scenePath.Substring(prefix.Length, scenePath.Length - prefix.Length - extension.Length);
            Require(int.TryParse(number, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int index)
                && index >= 1, "The official prairie gameplay scene has an invalid export number.");
            var destination = new ExportDestination(index);
            Require(destination.ScenePath == scenePath, "The official prairie gameplay scene does not use its numbered export path.");
            return destination;
        }

        private static TiledLevelAsset LoadLevel(ExportDestination destination)
        {
            var level = AssetDatabase.LoadAssetAtPath<TiledLevelAsset>(destination.LevelPath); ValidateLevel(level, destination); return level;
        }

        private static void ValidateLevel(TiledLevelAsset level, ExportDestination destination)
        {
            Require(level != null && level.IsValid && level.AllowMissingTiles && level.Tiles.Count == 40
                && level.TileSizeMeters == Vector2.one * TileSize && (level.WorldUnitsPerMeter - Vector2.one * WorldScale).sqrMagnitude < 0.00000001f,
                "Export the current 40 official prairie tiles before deploying gameplay.");
            foreach (var tile in level.Tiles)
            {
                HeightMapLevelAsset data = tile.level;
                Require(AssetDatabase.GetAssetPath(data).StartsWith(destination.Folder + "/", StringComparison.Ordinal)
                    && data.HeightMap != null && AssetDatabase.GetAssetPath(data.HeightMap).StartsWith(destination.Folder + "/data/", StringComparison.Ordinal),
                    "The official gameplay level must use its own exported map assets.");
                Require(data.HeightMap.width == Resolution && data.HeightMap.height == Resolution && data.HeightMap.format == TextureFormat.R16
                    && data.BakedHeightResolution == RuntimeResolution && data.MinimumHeightMeters == 0 && data.MaximumHeightMeters == HeightRange
                    && data.ContourIntervalMeters == 7 && data.BackgroundColor == Color.black && !data.NormalizeSourceRange
                    && data.SurfaceSmoothingSigmaMeters == 0 && data.DetailSmoothingSigmaMeters == 0 && data.PrebakedHeightField == null,
                    "An exported prairie tile lost its fixed metre scale, R16 precision, 7 m contours or unsmoothed data.");
            }
        }

        private static Vector2 ChooseSpawn(TiledLevelAsset level)
        {
            var sampler = new ExportSampler(level);
            Vector2 best = Vector2.zero;
            float bestScore = float.NegativeInfinity;
            foreach (var tile in level.Tiles)
            for (int z = 30; z <= 220; z += 10) for (int x = 30; x <= 220; x += 10)
            {
                Vector2 point = (Vector2)tile.coordinate * TileSize + new Vector2(x, z);
                if (!sampler.TrySample(point, out float height) || !sampler.TrySample(point + Vector2.right, out float right)
                    || !sampler.TrySample(point - Vector2.right, out float left) || !sampler.TrySample(point + Vector2.up, out float up)
                    || !sampler.TrySample(point - Vector2.up, out float down)) continue;
                float slope = Mathf.Atan(new Vector2((right - left) * .5f, (up - down) * .5f).magnitude) * Mathf.Rad2Deg;
                if (slope > 10) continue;
                float low = height, high = height;
                int nearbySamples = 0;
                for (int direction = 0; direction < 16; direction++)
                {
                    float angle = direction * Mathf.PI / 8;
                    Vector2 delta = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 40;
                    if (!sampler.TrySample(point + delta, out float nearby)) continue;
                    low = Mathf.Min(low, nearby); high = Mathf.Max(high, nearby); nearbySamples++;
                }
                if (nearbySamples < 12) continue;
                // Prefer a gentle point below nearby authored relief, with a flat-map fallback.
                float score = Mathf.Min(high - low, 35) * 2 + Mathf.Min(high - height, 20) - slope * 1.5f
                    - (point - level.MapSizeMeters * .5f).magnitude * .0001f;
                if (score > bestScore) { bestScore = score; best = point; }
            }
            Require(!float.IsNegativeInfinity(bestScore), "The official prairie has no gentle playable starting point.");
            return best;
        }

        private sealed class ExportSampler
        {
            private readonly Dictionary<Vector2Int, NativeArray<ushort>> pixels = new Dictionary<Vector2Int, NativeArray<ushort>>();
            private readonly Vector2Int grid;
            public ExportSampler(TiledLevelAsset level)
            {
                grid = level.GridSize;
                foreach (var tile in level.Tiles) pixels.Add(tile.coordinate, tile.level.HeightMap.GetPixelData<ushort>(0));
            }
            public bool TrySample(Vector2 point, out float height)
            {
                height = 0;
                if (point.x < 0 || point.y < 0 || point.x > grid.x * TileSize || point.y > grid.y * TileSize) return false;
                var coordinate = new Vector2Int(Mathf.Min(Mathf.FloorToInt(point.x / TileSize), grid.x - 1),
                    Mathf.Min(Mathf.FloorToInt(point.y / TileSize), grid.y - 1));
                if (!pixels.TryGetValue(coordinate, out var data)) return false;
                Vector2 uv = (point / TileSize - (Vector2)coordinate) * (Resolution - 1);
                int x = Mathf.FloorToInt(uv.x), z = Mathf.FloorToInt(uv.y);
                int right = Mathf.Min(x + 1, Resolution - 1), top = Mathf.Min(z + 1, Resolution - 1);
                height = Mathf.Lerp(Mathf.Lerp(data[z * Resolution + x], data[z * Resolution + right], uv.x - x),
                    Mathf.Lerp(data[top * Resolution + x], data[top * Resolution + right], uv.x - x), uv.y - z) / 65535f * HeightRange;
                return true;
            }
        }

        private static void CheckSeams(Dictionary<Vector2Int, Terrain> tiles, StringBuilder report, ExportManifest manifest)
        {
            foreach (var pair in tiles)
            foreach (Vector2Int direction in new[] { Vector2Int.right, Vector2Int.up })
            {
                if (!tiles.TryGetValue(pair.Key + direction, out Terrain neighbor)) continue;
                bool horizontal = direction == Vector2Int.right;
                float[,] a = pair.Value.terrainData.GetHeights(horizontal ? Resolution - 1 : 0, horizontal ? 0 : Resolution - 1,
                    horizontal ? 1 : Resolution, horizontal ? Resolution : 1);
                float[,] b = neighbor.terrainData.GetHeights(0, 0, horizontal ? 1 : Resolution, horizontal ? Resolution : 1);
                float maximum = 0;
                for (int i = 0; i < Resolution; i++)
                {
                    float difference = (horizontal ? a[i, 0] - b[i, 0] : a[0, i] - b[0, i]) * HeightRange
                        + pair.Value.transform.position.y - neighbor.transform.position.y;
                    maximum = Mathf.Max(maximum, Mathf.Abs(difference));
                }
                manifest.seams.Add(new SeamManifest { first = pair.Key, second = pair.Key + direction, maximumHeightDifferenceMeters = maximum });
                report.AppendLine($"Seam {pair.Key}->{pair.Key + direction}: maximum difference {maximum:F6} m (source preserved).");
                Require(maximum <= 1f, "Source Terrain border differs by more than 1 m: " + pair.Value.name + " / " + neighbor.name + ". No source heights were repaired.");
                if (maximum > .02f) Debug.LogWarning($"Official prairie source border {pair.Value.name}/{neighbor.name} differs by {maximum:F4} m; exported without changing it.");
            }
        }

        private static void SaveLoadedScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty) Require(!string.IsNullOrEmpty(scene.path), "Save the unnamed edited scene before switching to the gameplay test.");
            }
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty) Require(EditorSceneManager.SaveScene(scene), "Could not save scene changes: " + scene.path);
            }
        }

        private static Camera RequirePlayerResources()
        {
            foreach (string path in new[] { "Robot/RobotMarker", "Camera/RobotCamera", "Traversal/HeightMapTraversalEvaluator",
                "Traversal/TraversalOverlay", "Traversal/TraversalScanOverlay", "UI/MainUI" })
                Require(Resources.Load<GameObject>(path) != null, "Missing original gameplay resource: " + path);
            GameObject prefab = Resources.Load<GameObject>("Camera/RobotCamera");
            Camera camera = prefab.GetComponent<Camera>();
            Require(camera != null && camera.orthographic && prefab.GetComponent<RobotCameraFollow>() != null, "The original player camera prefab is incomplete.");
            return camera;
        }
        private static T GetOrCreate<T>(GameObject root, string name) where T : Component
        {
            T[] existing = root.GetComponentsInChildren<T>(true);
            Require(existing.Length <= 1, "Duplicate official gameplay component: " + typeof(T).Name);
            if (existing.Length == 1) { existing[0].gameObject.SetActive(true); return existing[0]; }
            var obj = new GameObject(name); obj.transform.SetParent(root.transform, false);
            Undo.RegisterCreatedObjectUndo(obj, "Create " + name); return Undo.AddComponent<T>(obj);
        }
        private static T[] Components<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/'); EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

        [Serializable] private sealed class ExportManifest
        {
            public string format = "Official prairie V1 sparse terrain tiles";
            public int version = 1, tileCount, sourceResolution = Resolution, runtimeResolution = RuntimeResolution;
            public Vector3 sourceOrigin;
            public Vector2 mapSizeMeters, spawnMapPositionMeters;
            public float tileSizeMeters = TileSize, worldUnitsPerMeter = WorldScale, minimumHeightMeters, maximumHeightMeters;
            public float encodingMinimumHeightMeters = 0, encodingMaximumHeightMeters = HeightRange, contourIntervalMeters = 7;
            public string pngFormat = "16-bit grayscale; top row is north (+Terrain Z); Unity pixels retain Terrain row order";
            public List<TileManifest> tiles = new List<TileManifest>();
            public List<SeamManifest> seams = new List<SeamManifest>();
        }
        [Serializable] private sealed class TileManifest
        {
            public string name, sourceTerrainData, heightMapAsset, mapDataAsset, quantizedSamplesSha256;
            public Vector2Int coordinate;
            public Vector3 nativeOrigin;
            public Vector2 tileOriginMeters;
            public float minimumHeightMeters, maximumHeightMeters;
        }
        [Serializable] private sealed class SeamManifest
        {
            public Vector2Int first, second;
            public float maximumHeightDifferenceMeters;
        }
    }
}
