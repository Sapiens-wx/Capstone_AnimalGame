using System;
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
    /// <summary>Deploys the tiled V1 terrain into its independent player test scene.</summary>
    public static class ContinuousMapV1SceneSetup
    {
        public const string ScenePath = "Assets/Scenes/ContinousMapTestScene.unity";
        public const string LevelPath = "Assets/Maps/TerrainGridV1/TerrainGridV1_TiledLevel.asset";
        private const string TemplateScenePath = "Assets/Scenes/TerrainHeightMapTestScene.unity";
        private const string RootName = "Continuous V1 Height Map Test";
        private const string ReportFolder = "Temp/ContinuousMapV1";
        private static readonly Vector2 InitialSpawn = new Vector2(250f, 100f);

        [MenuItem("Animal Game/Terrain Grid V1/Deploy Tiled Map to ContinousMapTestScene")]
        public static void Deploy()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play mode before deploying the test scene.");
            Scene target = RequireScene();
            TiledLevelAsset level = LoadLevel();
            RequirePlayerResources();
            GameObject root = target.GetRootGameObjects().SingleOrDefault(obj => obj.name == RootName);
            Require(Components<MapTestSceneController>(target).All(map => root != null && map.transform.IsChildOf(root.transform)),
                "This scene contains another map controller; remove or relocate it before deploying this independent test.");
            Require(Components<HeightMapPlayerSceneBootstrap>(target).All(bootstrap => root != null && bootstrap.transform.IsChildOf(root.transform)),
                "This scene contains another player bootstrap.");
            Require(Components<EventSystem>(target).All(events => root != null && events.transform.IsChildOf(root.transform)),
                "This scene already has an EventSystem outside the managed test root.");

            // Preserve the new scene's default camera, but let the existing player bootstrap
            // create the same RobotCamera prefab used by TerrainHeightMapTestScene at runtime.
            foreach (Camera camera in Components<Camera>(target))
            {
                if (!camera.enabled || !camera.gameObject.activeInHierarchy)
                    continue;
                Require(camera.gameObject.name == "Main Camera" && camera.transform.parent == null
                    && camera.transform.childCount == 0
                    && camera.GetComponents<Component>().All(component => component is Transform
                        || component is Camera || component is AudioListener || component is UniversalAdditionalCameraData),
                    "The scene has an authored active camera. Disable it before deploying the independent player camera.");
            }

            Scene template = EditorSceneManager.OpenScene(TemplateScenePath, OpenSceneMode.Additive);
            try
            {
                var sourceMap = Components<MapTestSceneController>(template).Single();
                var sourceBootstrap = Components<HeightMapPlayerSceneBootstrap>(template).Single();
                if (SceneManager.GetActiveScene() != target)
                    Require(SceneManager.SetActiveScene(target), "Could not make the target scene active.");
                int undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Deploy continuous V1 map test");
                if (root == null)
                {
                    root = new GameObject(RootName);
                    SceneManager.MoveGameObjectToScene(root, target);
                    Undo.RegisterCreatedObjectUndo(root, "Create continuous V1 test root");
                }
                Undo.RecordObject(root, "Configure continuous V1 test root");
                root.SetActive(false);
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                root.transform.localScale = Vector3.one;

                var map = GetOrCreate<MapTestSceneController>(root, "Tiled 500m V1 Height Map");
                Undo.RecordObject(map, "Configure tiled V1 map");
                EditorUtility.CopySerialized(sourceMap, map);
                var mapSettings = new SerializedObject(map);
                mapSettings.FindProperty("levelAsset").objectReferenceValue = level;
                mapSettings.FindProperty("editorHeightResolution").intValue = 1024;
                mapSettings.ApplyModifiedPropertiesWithoutUndo();
                map.enabled = true;

                var bootstrap = GetOrCreate<HeightMapPlayerSceneBootstrap>(root, "Continuous Height Map Player Bootstrap");
                Undo.RecordObject(bootstrap, "Configure continuous V1 player");
                EditorUtility.CopySerialized(sourceBootstrap, bootstrap);
                var playerSettings = new SerializedObject(bootstrap);
                playerSettings.FindProperty("playerSpawnMapPositionMeters").vector2Value = InitialSpawn;
                playerSettings.FindProperty("showRobotTerrainData").boolValue = true;
                playerSettings.ApplyModifiedPropertiesWithoutUndo();
                bootstrap.enabled = true;

                EventSystem events = GetOrCreate<EventSystem>(root, "EventSystem");
                if (events.GetComponent<StandaloneInputModule>() == null)
                    Undo.AddComponent<StandaloneInputModule>(events.gameObject);
                events.enabled = true;
                foreach (Camera camera in Components<Camera>(target))
                {
                    if (!camera.enabled || !camera.gameObject.activeInHierarchy)
                        continue;
                    Undo.RecordObject(camera.gameObject, "Preserve inactive default scene camera");
                    camera.gameObject.SetActive(false);
                }

                root.SetActive(true);
                // A reused ExecuteAlways controller may still own its old generated preview.
                // Explicitly rebuild before validation rather than waiting for an editor Update.
                map.SendMessage("RebuildGeneratedMap", SendMessageOptions.RequireReceiver);
                EditorUtility.SetDirty(map);
                EditorUtility.SetDirty(bootstrap);
                EditorSceneManager.MarkSceneDirty(target);
                Undo.CollapseUndoOperations(undoGroup);
                Selection.activeGameObject = bootstrap.gameObject;
            }
            finally
            {
                EditorSceneManager.CloseScene(template, true);
                SceneManager.SetActiveScene(target);
            }

            Validate();
            Require(EditorSceneManager.SaveScene(target), "Could not save ContinousMapTestScene.");
            SceneView view = SceneView.lastActiveSceneView;
            if (view != null)
            {
                var map = Components<MapTestSceneController>(target).Single();
                view.in2DMode = true;
                view.LookAt(map.WorldBounds.center, Quaternion.identity, map.WorldBounds.size.y * 0.58f, true);
            }
        }

        [MenuItem("Animal Game/Terrain Grid V1/Validate Continuous Map Test Scene")]
        public static void Validate()
        {
            Scene scene = RequireScene();
            TiledLevelAsset level = LoadLevel();
            MapTestSceneController[] maps = Components<MapTestSceneController>(scene);
            HeightMapPlayerSceneBootstrap[] bootstraps = Components<HeightMapPlayerSceneBootstrap>(scene);
            EventSystem[] eventSystems = Components<EventSystem>(scene);
            Require(maps.Length == 1 && maps[0].enabled && maps[0].HasGeneratedMap, "Expected one initialized continuous map.");
            Require(bootstraps.Length == 1 && bootstraps[0].enabled, "Expected one player bootstrap.");
            Require(eventSystems.Length == 1 && eventSystems[0].enabled
                && eventSystems[0].GetComponent<StandaloneInputModule>() != null, "Expected one complete EventSystem.");
            var map = maps[0];
            var bootstrap = bootstraps[0];
            Require(map.LevelAsset == level && map.MapSizeMeters == new Vector2(500f, 500f), "Incorrect tiled map assignment.");
            Require(map.transform.lossyScale == Vector3.one && Quaternion.Angle(map.transform.rotation, Quaternion.identity) < 0.001f,
                "The V1 world scale requires an unrotated, unit-scale map.");
            float expectedWorldSize = level.MapSizeMeters.x * level.WorldUnitsPerMeter.x;
            Require(Mathf.Abs(map.WorldBounds.size.x - expectedWorldSize) < 0.001f
                && Mathf.Abs(map.WorldBounds.size.y - expectedWorldSize) < 0.001f, "Rendered map scale changed.");
            Require(Mathf.Abs(map.MapMetersToWorldDistance(Vector2.right, 1f) - 2048f / 30f / 250f) < 0.00001f,
                "The tiled world does not preserve V1 world units per logical meter.");
            bool spawnPlayable = map.TrySampleMapPosition(InitialSpawn, out float spawnHeight);
            Require(bootstrap.PlayerSpawnMapPositionMeters == InitialSpawn && spawnPlayable,
                "The initial spawn has no playable height data.");

            var report = new StringBuilder();
            report.AppendLine($"PASS: ContinousMapTestScene ({(Application.isPlaying ? "Play" : "Edit")})");
            report.AppendLine($"Independent level: {LevelPath}");
            report.AppendLine("Logical size: 500 x 500 m; four sources: 2049 R16; per-tile runtime bake / preview: 2048; PPU: 30.");
            report.AppendLine($"World size: {map.WorldBounds.size.x:F6} x {map.WorldBounds.size.y:F6}; V1 scale: {expectedWorldSize / 500f:F9} world units per meter.");
            report.AppendLine($"Spawn: {InitialSpawn}; editor/runtime sampled height: {spawnHeight:F6} m.");
            report.AppendLine("The original Resources robot, traversal, camera and scan prefabs are used without modifying their settings.");
            HeightMapPrebakedData localCache = level.Presentation.PrebakedHeightField;
            bool hasMatchingCache = localCache != null && localCache.Matches(level.Presentation);
            report.AppendLine(hasMatchingCache
                ? "Height data: matching local prebake cache available."
                : "Height data: no matching local cache; Play rebuilds from the source PNG.");

            Camera resourceCamera = RequirePlayerResources();
            if (Application.isPlaying)
            {
                Require(bootstrap.robot != null && bootstrap.traversalEvaluator != null && bootstrap.traversalEvaluator.IsInitialized,
                    "Player movement or traversal did not initialize.");
                Require(bootstrap.map == map && bootstrap.mapCamera != null && bootstrap.cameraFollow != null
                    && bootstrap.scanOverlay != null && bootstrap.scanChargeUi != null && bootstrap.mainUiCanvas != null,
                    "The original follow camera, gameplay UI or terrain scan wiring did not initialize.");
                Require(map.RuntimeMap.TileCount == 4 && map.RuntimeMap.Tiles.All(tile => tile.Field.Width == 2048 && tile.Field.Height == 2048),
                    "Runtime did not create four independent 2048 height fields.");
                if (hasMatchingCache)
                    Require(map.RuntimeMap.Tiles.First(tile => tile.Level == level.Presentation).Field.SurfaceTexture == localCache.SurfaceTexture,
                        "Runtime did not load the matching local prebake.");
                Require(Components<RobotMover>(scene).Length == 1, "Expected one runtime robot.");
                Require(Components<Camera>(scene).Count(camera => camera.enabled && camera.gameObject.activeInHierarchy) == 1,
                    "More than one gameplay camera is active.");
                Require(bootstrap.cameraShake != null
                    && Mathf.Abs(bootstrap.cameraShake.BaseOrthographicSize - resourceCamera.orthographicSize) < 0.001f,
                    "The camera base size differs from the original V1 camera prefab.");
                Require(bootstrap.mapCamera.backgroundColor == level.Presentation.BackgroundColor, "Gameplay background differs from the level.");
                var cameraData = bootstrap.mapCamera.GetComponent<UniversalAdditionalCameraData>();
                Require(cameraData != null && cameraData.scriptableRenderer.GetType().Name == "Renderer2D",
                    "The gameplay camera is not using the original 2D renderer.");
                Require(map.TrySampleWorldPosition(bootstrap.robot.transform.position, out Vector2 playerMapPosition, out float playerHeight),
                    "The runtime robot is outside the continuous world.");
                report.AppendLine($"Runtime player: {playerMapPosition}; height: {playerHeight:F6} m; base camera size: {bootstrap.cameraShake.BaseOrthographicSize:F3}.");
                report.AppendLine($"Terrain scan wired; current visible scan markers: {bootstrap.scanOverlay.VisibleMarkerCount}.");
            }
            else
            {
                var settings = new SerializedObject(map);
                Require(settings.FindProperty("editorHeightResolution").intValue == 1024,
                    "The lightweight editor height settings changed.");
                Require(Components<Camera>(scene).All(camera => !camera.enabled || !camera.gameObject.activeInHierarchy),
                    "The default editor camera would compete with the runtime player camera.");
                var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                Require(pipeline != null && pipeline.scriptableRenderer.GetType().Name == "Renderer2D",
                    "The original 2D render pipeline must be active in the gameplay test scene.");
                report.AppendLine($"Player bootstrap configured; original runtime camera base size: {resourceCamera.orthographicSize:F3}.");
            }
            Directory.CreateDirectory(ReportFolder);
            File.WriteAllText(ReportFolder + (Application.isPlaying ? "/PlayValidation.txt" : "/EditValidation.txt"), report.ToString());
            Debug.Log(report.ToString(), map);
        }

        private static TiledLevelAsset LoadLevel()
        {
            var level = AssetDatabase.LoadAssetAtPath<TiledLevelAsset>(LevelPath);
            Require(level != null && level.IsValid, "Export the tiled V1 level before deploying this scene.");
            Require(level.MapSizeMeters == new Vector2(500, 500) && level.Tiles.Count == 4, "Expected four V1 tiles.");
            return level;
        }

        private static Camera RequirePlayerResources()
        {
            string[] paths = { "Robot/RobotMarker", "Camera/RobotCamera", "Traversal/HeightMapTraversalEvaluator",
                "Traversal/TraversalOverlay", "Traversal/TraversalScanOverlay", "UI/MainUI" };
            foreach (string path in paths)
                Require(Resources.Load<GameObject>(path) != null, "Missing original player resource: " + path);
            GameObject prefab = Resources.Load<GameObject>("Camera/RobotCamera");
            Camera camera = prefab.GetComponent<Camera>();
            Require(camera != null && camera.orthographic && prefab.GetComponent<RobotCameraFollow>() != null,
                "The original player camera prefab is incomplete.");
            return camera;
        }

        private static T GetOrCreate<T>(GameObject root, string objectName) where T : Component
        {
            T[] existing = root.GetComponentsInChildren<T>(true);
            Require(existing.Length <= 1, "The managed root contains duplicated " + typeof(T).Name + " components.");
            if (existing.Length == 1)
            {
                Undo.RecordObject(existing[0].gameObject, "Name tiled map object");
                existing[0].gameObject.name = objectName;
                existing[0].gameObject.SetActive(true);
                return existing[0];
            }
            var obj = new GameObject(objectName);
            obj.transform.SetParent(root.transform, false);
            Undo.RegisterCreatedObjectUndo(obj, "Create " + objectName);
            return Undo.AddComponent<T>(obj);
        }

        private static T[] Components<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

        private static Scene RequireScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.path == ScenePath && SceneManager.sceneCount == 1,
                "Open ContinousMapTestScene by itself before deploying or validating it.");
            return scene;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
