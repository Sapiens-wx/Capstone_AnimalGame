using System;
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
            BuildTerminal(material);
            BuildScene(material);
            AssetDatabase.SaveAssetIfDirty(panel);
            AssetDatabase.SaveAssetIfDirty(material);
            Debug.Log("Checkpoint Refit UI-only demo created: " + ScenePath);
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
            Color white = new Color(.8f, .8f, .8f), yellow = new Color(.83f, .72f, .33f);
            CreateLine(parent, "Chassis", material, white, .045f, true,
                new Vector2(-.65f, -.7f), new Vector2(.65f, -.7f), new Vector2(.65f, .65f),
                new Vector2(.4f, .9f), new Vector2(-.65f, .9f));
            CreateLine(parent, "Service Display", material, yellow, .045f, true,
                new Vector2(-.4f, .1f), new Vector2(.4f, .1f), new Vector2(.4f, .55f), new Vector2(-.4f, .55f));
            CreateLine(parent, "Service Port", material, yellow, .05f, false, new Vector2(-.2f, -.3f), new Vector2(.2f, -.3f));
            CreateLine(parent, "Service Port Vertical", material, yellow, .05f, false, new Vector2(0, -.1f), new Vector2(0, -.5f));
            CreateLine(parent, "Antenna", material, white, .035f, false, new Vector2(.3f, .85f), new Vector2(.3f, 1.35f));
            CreateLine(parent, "Foot Left", material, white, .05f, false, new Vector2(-.8f, -.9f), new Vector2(-.35f, -.9f));
            CreateLine(parent, "Foot Right", material, white, .05f, false, new Vector2(.35f, -.9f), new Vector2(.8f, -.9f));
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
