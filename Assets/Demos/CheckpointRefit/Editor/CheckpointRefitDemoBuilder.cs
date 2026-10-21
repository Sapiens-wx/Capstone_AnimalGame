using System;
using AnimalGame.Interaction;
using AnimalGame.Interaction.Editor;
using AnimalGame.RobotArm;
using AnimalGame.RobotMap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AnimalGame.Demos.CheckpointRefit.Editor
{
    /// <summary>Explicit opt-in generation, confined to this demo's own directory.</summary>
    public static class CheckpointRefitDemoBuilder
    {
        public const string RootPath = "Assets/Demos/CheckpointRefit";
        public const string TerminalPath = RootPath + "/Prefabs/CheckpointRefitTerminal.prefab";
        public const string UIPath = RootPath + "/Prefabs/CheckpointRefitUI.prefab";
        public const string ScenePath = RootPath + "/Scenes/CheckpointRefitDemo.unity";
        public const string ArtworkPath = RootPath + "/Art/AssemblyStation.png";
        // v12's 888px silhouette keeps the previous ~4.85-unit unscaled sprite width.
        public const float ArtworkPixelsPerUnit = 183.1f;
        public const int ArtworkSortingOrder = 20;
        private const string PanelPath = RootPath + "/UI/CheckpointRefitPanel.asset";
        private const string MaterialPath = RootPath + "/Materials/TerminalLine.mat";

        [MenuItem("Animal Game/Demos/Checkpoint Refit/Create Or Open Demo")]
        public static void CreateOrOpen()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) Build();
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Animal Game/Demos/Checkpoint Refit/Rebuild Demo Assets")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before building checkpoint refit assets.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).path == ScenePath)
                    throw new InvalidOperationException("Close the refit Demo scene before rebuilding it.");
            EnsureFolder(RootPath + "/Prefabs");
            EnsureFolder(RootPath + "/Scenes");
            EnsureFolder(RootPath + "/Materials");
            AssetDatabase.ImportAsset(RootPath + "/UI/CheckpointRefit.uxml", ImportAssetOptions.ForceSynchronousImport);
            VisualTreeAsset tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(RootPath + "/UI/CheckpointRefit.uxml");
            if (tree == null) throw new InvalidOperationException("Checkpoint refit UXML is missing or invalid.");

            PanelSettings panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (panel == null)
            {
                PanelSettings template = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Prefabs/Resources/UI/Main/MainUISetting.asset");
                if (template == null) throw new InvalidOperationException("The original UI PanelSettings template is missing.");
                panel = Object.Instantiate(template);
                panel.name = "Checkpoint Refit Panel";
                panel.sortingOrder = 200;
                panel.scaleMode = PanelScaleMode.ConstantPixelSize;
                panel.scale = 1f;
                panel.targetTexture = null;
                AssetDatabase.CreateAsset(panel, PanelPath);
            }
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/PlayerUiOrganicSprite.shader");
                if (shader == null) throw new InvalidOperationException("Player-view clipped sprite shader is missing.");
                material = new Material(shader) { name = "Checkpoint Terminal Lines" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            BuildUi(tree, panel);
            ImportStationArtwork();
            BuildTerminal(material);
            BuildScene(material);
            AssetDatabase.SaveAssetIfDirty(panel);
            AssetDatabase.SaveAssetIfDirty(material);
            Debug.Log("Checkpoint Refit UI-only demo created: " + ScenePath);
        }

        [MenuItem("Animal Game/Demos/Checkpoint Refit/Update Station Artwork")]
        public static void UpdateStationArtwork()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before updating station artwork.");
            ImportStationArtwork();
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null) throw new InvalidOperationException("The station's player-view clipped material is missing.");
            GameObject root = PrefabUtility.LoadPrefabContents(TerminalPath);
            try
            {
                // Replace only the demo-owned visuals; keep the root and interaction references intact.
                string[] ownedNames = { "Chassis", "Service Display", "Service Port", "Service Port Vertical",
                    "Antenna", "Foot Left", "Foot Right", "Station Artwork" };
                foreach (string childName in ownedNames)
                {
                    Transform child = root.transform.Find(childName);
                    if (child != null) Object.DestroyImmediate(child.gameObject);
                }
                DrawTerminal(root.transform, material);
                EnsureInteractionMarker(root);
                PrefabUtility.SaveAsPrefabAsset(root, TerminalPath, out bool saved);
                if (!saved) throw new InvalidOperationException("Unable to save the station artwork replacement.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Debug.Log("Checkpoint Refit station artwork updated: " + TerminalPath);
        }

        public static void UpdateStationArtworkBatch()
        {
            try
            {
                UpdateStationArtwork();
                CheckpointRefitValidation.Run();
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1); else throw;
            }
        }

        [MenuItem("Animal Game/Demos/Checkpoint Refit/Add Shared Interaction Marker")]
        public static void UpdateInteractionMarker()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before updating the station marker.");
            InteractionMarkerBuilder.EnsureAssets();
            GameObject root = PrefabUtility.LoadPrefabContents(TerminalPath);
            try
            {
                Vector3 scale = root.transform.localScale;
                float radius = root.GetComponent<CheckpointRefitTerminal>().InteractionRadiusMeters;
                EnsureInteractionMarker(root);
                if (root.GetComponentsInChildren<InteractionMarker>(true).Length != 1
                    || root.transform.localScale != scale
                    || root.GetComponent<CheckpointRefitTerminal>().InteractionRadiusMeters != radius)
                    throw new InvalidOperationException("The marker update must preserve station scale/range and produce exactly one marker.");
                PrefabUtility.SaveAsPrefabAsset(root, TerminalPath, out bool saved);
                if (!saved) throw new InvalidOperationException("Unable to save the station interaction marker.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        public static void UpdateInteractionMarkerBatch()
        {
            try
            {
                UpdateInteractionMarker();
                // A second pass verifies that an existing nested marker is reused.
                UpdateInteractionMarker();
                CheckpointRefitValidation.Run();
                Debug.Log("Reusable interaction marker installed without rebuilding the demo scene.");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1); else throw;
            }
        }

        private static void EnsureInteractionMarker(GameObject root)
        {
            InteractionMarker marker = root.GetComponentInChildren<InteractionMarker>(true);
            if (marker == null)
            {
                GameObject markerPrefab = InteractionMarkerBuilder.EnsureAssets();
                var child = (GameObject)PrefabUtility.InstantiatePrefab(markerPrefab, root.scene);
                child.transform.SetParent(root.transform, false);
                marker = child.GetComponent<InteractionMarker>();
            }
            root.GetComponent<CheckpointRefitTerminal>().ConfigureMarker(marker);
        }

        private static void ImportStationArtwork()
        {
            AssetDatabase.ImportAsset(ArtworkPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(ArtworkPath) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("The approved assembly station PNG is missing.");
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.textureType = TextureImporterType.Sprite;
            settings.spriteMode = (int)SpriteImportMode.Single;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spritePivot = new Vector2(.5f, .5f);
            settings.spritePixelsPerUnit = ArtworkPixelsPerUnit;
            settings.mipmapEnabled = false;
            settings.alphaIsTransparency = true;
            importer.SetTextureSettings(settings);
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }

        public static void BuildBatch()
        {
            try
            {
                if (Application.isBatchMode && SceneManager.sceneCount == 1
                    && string.IsNullOrEmpty(SceneManager.GetActiveScene().path)
                    && !SceneManager.GetActiveScene().isDirty)
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Build();
                CheckpointRefitValidation.Run();
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1); else throw;
            }
        }

        private static void BuildUi(VisualTreeAsset tree, PanelSettings panel)
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            try
            {
                root = new GameObject("CheckpointRefitUI");
                SceneManager.MoveGameObjectToScene(root, preview);
                UIDocument document = root.AddComponent<UIDocument>();
                document.panelSettings = panel;
                document.visualTreeAsset = tree;
                document.sortingOrder = 200;
                root.AddComponent<CheckpointRefitView>();
                PrefabUtility.SaveAsPrefabAsset(root, UIPath, out bool saved);
                if (!saved) throw new InvalidOperationException("Unable to save checkpoint refit UI prefab.");
            }
            finally
            {
                if (root != null) Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private static void BuildTerminal(Material material)
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            try
            {
                root = new GameObject("CheckpointRefitTerminal");
                SceneManager.MoveGameObjectToScene(root, preview);
                var terminal = root.AddComponent<CheckpointRefitTerminal>();
                terminal.Configure(AssetDatabase.LoadAssetAtPath<GameObject>(UIPath));
                DrawTerminal(root.transform, material);
                EnsureInteractionMarker(root);
                PrefabUtility.SaveAsPrefabAsset(root, TerminalPath, out bool saved);
                if (!saved) throw new InvalidOperationException("Unable to save checkpoint terminal prefab.");
            }
            finally
            {
                if (root != null) Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private static void DrawTerminal(Transform parent, Material material)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ArtworkPath);
            if (sprite == null) throw new InvalidOperationException("Import the approved station sprite before building its prefab.");
            var child = new GameObject("Station Artwork");
            child.transform.SetParent(parent, false);
            var renderer = child.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = material;
            renderer.color = Color.white;
            renderer.sortingOrder = ArtworkSortingOrder;
        }

        private static void CreateLine(Transform parent, string name, Material material, Color color,
            float width, bool loop, params Vector2[] points)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            var line = child.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = loop;
            line.sharedMaterial = material;
            line.startColor = line.endColor = color;
            line.startWidth = line.endWidth = width;
            line.sortingOrder = 20;
            line.positionCount = points.Length;
            for (int i = 0; i < points.Length; i++) line.SetPosition(i, points[i]);
        }

        private static void BuildScene(Material material)
        {
            Scene previous = SceneManager.GetActiveScene();
            bool replaceEmpty = SceneManager.sceneCount == 1 && string.IsNullOrEmpty(previous.path)
                && !previous.isDirty && previous.rootCount == 0;
            if (!replaceEmpty)
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                        throw new InvalidOperationException("Save the untitled scene before generating the refit Demo scene.");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                replaceEmpty ? NewSceneMode.Single : NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var station = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(TerminalPath), scene);
                station.transform.position = new Vector3(0f, 5f, 0f);
                GameObject playerAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Resources/Robot/RobotMarker.prefab");
                GameObject cameraAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Resources/Camera/RobotCamera.prefab");
                if (playerAsset == null || cameraAsset == null) throw new InvalidOperationException("Original player or camera prefab is missing.");
                var playerObject = (GameObject)PrefabUtility.InstantiatePrefab(playerAsset, scene);
                var cameraObject = (GameObject)PrefabUtility.InstantiatePrefab(cameraAsset, scene);
                playerObject.name = "Demo Player (original prefab instance)";
                playerObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                if (playerObject.GetComponent<RobotBalanceController>() == null) playerObject.AddComponent<RobotBalanceController>();
                if (playerObject.GetComponent<RobotArmController>() == null) playerObject.AddComponent<RobotArmController>();
                if (playerObject.GetComponent<RobotTumbleController>() == null) playerObject.AddComponent<RobotTumbleController>();
                if (playerObject.GetComponent<RobotSelfRightingController>() == null) playerObject.AddComponent<RobotSelfRightingController>();
                if (playerObject.GetComponent<RobotBalanceView>() == null) playerObject.AddComponent<RobotBalanceView>();
                cameraObject.transform.SetPositionAndRotation(new Vector3(0f, 0f, -10f), Quaternion.identity);
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.orthographic = true;
                camera.backgroundColor = new Color(.015f, .015f, .015f);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.orthographicSize = 9f;
                var setup = new GameObject("Checkpoint Refit Demo Bindings").AddComponent<CheckpointRefitDemoScene>();
                SceneManager.MoveGameObjectToScene(setup.gameObject, scene);
                setup.Configure(station.GetComponent<CheckpointRefitTerminal>(), playerObject.GetComponent<RobotMover>(), camera);
                var ground = new GameObject("Demo Ground Guide");
                SceneManager.MoveGameObjectToScene(ground, scene);
                CreateLine(ground.transform, "Ground Boundary", material, new Color(.16f, .16f, .16f), .025f, true,
                    new Vector2(-10f, -6f), new Vector2(10f, -6f), new Vector2(10f, 12f), new Vector2(-10f, 12f));
                PrefabUtility.RecordPrefabInstancePropertyModifications(station.transform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(playerObject.transform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(playerObject);
                PrefabUtility.RecordPrefabInstancePropertyModifications(cameraObject.transform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(camera);
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Unable to save the independent refit Demo scene.");
            }
            finally
            {
                if (replaceEmpty) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                else EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
