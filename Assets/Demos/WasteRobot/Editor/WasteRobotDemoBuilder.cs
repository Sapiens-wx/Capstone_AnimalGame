using System;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AnimalGame.Demos.WasteRobot.Editor
{
    // Explicit commands only. No InitializeOnLoad, play callbacks, Resources entries
    // or writes to production assets/settings.
    public static class WasteRobotDemoBuilder
    {
        public const string RootPath = "Assets/Demos/WasteRobot";
        public const string PrefabPath = RootPath + "/Prefabs/WasteRobotBase.prefab";
        public const string ScenePath = RootPath + "/Scenes/WasteRobotTraversalDemo.unity";
        public const string ExteriorArtPath = RootPath + "/Art/Exterior.png";
        public const string InteriorArtPath = RootPath + "/Art/Interior.png";
        private const string PlayerPath = "Assets/Prefabs/Resources/Robot/RobotMarker.prefab";
        private const string CameraPath = "Assets/Prefabs/Resources/Camera/RobotCamera.prefab";
        private const float PortalWidth = 2.4f;
        private const float SpritePixelsPerUnit = 100f;

        [MenuItem("Animal Game/Demos/Waste Robot/Create Or Open Demo")]
        public static void CreateOrOpen()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) Build();
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Animal Game/Demos/Waste Robot/Rebuild Demo Assets")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before rebuilding this Demo.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).path == ScenePath)
                    throw new InvalidOperationException("Close the Waste Robot Demo scene before rebuilding its generated assets.");
            EnsureFolder(RootPath + "/Art");
            EnsureFolder(RootPath + "/Prefabs");
            EnsureFolder(RootPath + "/Scenes");
            Sprite exterior = ImportSprite(ExteriorArtPath);
            Sprite interior = ImportSprite(InteriorArtPath);
            ValidateSpriteRegistration(exterior, interior);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(RootPath + "/Art/DemoUnlit.mat");
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                    ?? Shader.Find("Sprites/Default");
                if (shader == null) throw new InvalidOperationException("No unlit Sprite shader available.");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, RootPath + "/Art/DemoUnlit.mat");
            }
            BuildPrefab(exterior, interior, material);
            BuildScene();
            // Never flush unrelated dirty production assets from the editor.
            AssetDatabase.SaveAssetIfDirty(material);
            Debug.Log("Waste Robot Demo created: " + ScenePath);
        }

        public static void BuildBatch()
        {
            try
            {
                // A fresh batch editor starts in an untitled default scene. No
                // user scene is discarded; this branch is batch-only and clean.
                if (Application.isBatchMode && SceneManager.sceneCount == 1
                    && string.IsNullOrEmpty(SceneManager.GetActiveScene().path)
                    && !SceneManager.GetActiveScene().isDirty)
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Build();
                WasteRobotDemoValidation.Run();
                WasteRobotPlayerBindingValidation.Run();
                WasteRobotTransitionValidation.Run();
                WasteRobotScanValidation.Run();
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        private static Sprite ImportSprite(string path)
        {
            if (!path.StartsWith(RootPath + "/", StringComparison.Ordinal))
                throw new InvalidOperationException("Import target must stay in the Demo folder.");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Missing Demo PNG: " + path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = SpritePixelsPerUnit;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spritePivot = new Vector2(.5f, .5f);
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static void ValidateSpriteRegistration(Sprite exterior, Sprite interior)
        {
            foreach (Sprite sprite in new[] { exterior, interior })
            {
                if (sprite == null || sprite.rect.size != WasteRobotCollisionLayout.SharedImageSize)
                    throw new InvalidOperationException("Both Demo sprites must retain the shared 1779 x 884 canvas; do not trim or independently fit either view.");
                if (sprite.pivot != sprite.rect.size * .5f
                    || !Mathf.Approximately(sprite.pixelsPerUnit, SpritePixelsPerUnit))
                    throw new InvalidOperationException("Both Demo sprites must use the same centered pivot and pixels per unit.");
            }
        }

        private static void BuildPrefab(Sprite exterior, Sprite interior, Material material)
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            try
            {
                root = new GameObject("WasteRobotBase");
                SceneManager.MoveGameObjectToScene(root, preview);
                GameObject outside = CreateView(root.transform, "Exterior", exterior, material, WasteRobotWallView.Exterior);
                GameObject inside = CreateView(root.transform, "Interior", interior, material, WasteRobotWallView.Interior);
                WasteRobotBase machine = root.AddComponent<WasteRobotBase>();
                machine.Configure(outside, inside, PortalWidth);
                machine.AutoBindPlayer = true;
                machine.Bind(null);
                root.AddComponent<WasteRobotVisibility>();
                root.AddComponent<WasteRobotTransitionEffects>();
                ConfigureScanning(root, outside.GetComponentInChildren<SpriteRenderer>(true));
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
                if (!saved) throw new InvalidOperationException("Unable to save Demo prefab.");
            }
            finally
            {
                if (root != null) Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private static void ConfigureScanning(GameObject root, SpriteRenderer exterior)
        {
            ScannableInteractable scan = root.AddComponent<ScannableInteractable>();
            var settings = new SerializedObject(scan);
            settings.FindProperty("spriteSource").objectReferenceValue = exterior;
            Vector2[] hull = WasteRobotScanLayout.CreateNormalizedHull();
            SerializedProperty outline = settings.FindProperty("customNormalizedOutline");
            outline.arraySize = hull.Length;
            for (int i = 0; i < hull.Length; i++) outline.GetArrayElementAtIndex(i).vector2Value = hull[i];
            settings.ApplyModifiedPropertiesWithoutUndo();
            root.AddComponent<WasteRobotScan>().Configure(scan);
        }

        private static GameObject CreateView(Transform parent, string name, Sprite sprite,
            Material material, WasteRobotWallView view)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);
            // Both textures are two states of the same registered canvas. Never
            // fit the interior independently: that used to enlarge it by 22.5%.
            Vector2 imageSize = WasteRobotCollisionLayout.SharedImageSize;
            Vector2 entrance = WasteRobotCollisionLayout.SharedEntranceCenter;
            float scale = PortalWidth / WasteRobotCollisionLayout.SharedEntranceWidth;
            var art = new GameObject("Visual");
            art.transform.SetParent(root.transform, false);
            art.transform.localPosition = new Vector3((imageSize.x * .5f - entrance.x) * scale,
                (entrance.y - imageSize.y * .5f) * scale, 0f);
            art.transform.localScale = Vector3.one * (SpritePixelsPerUnit * scale);
            SpriteRenderer renderer = art.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = material;
            renderer.sortingOrder = 10;
            var walls = new GameObject("Collision");
            walls.transform.SetParent(root.transform, false);
            foreach (WasteRobotWallShape data in WasteRobotCollisionLayout.Create(view))
            {
                var segment = new GameObject(data.Name);
                segment.transform.SetParent(walls.transform, false);
                Vector2 a = PixelPoint(data.A, entrance, scale);
                Vector2 b = PixelPoint(data.B, entrance, scale);
                segment.transform.localPosition = (a + b) * .5f;
                WorldInteraction collision = segment.AddComponent<WorldInteraction>();
                collision.SetKind(WorldInteractionKind.Collision);
                collision.ColliderType = ColliderType.Custom;
                collision.ColliderCenter = Vector2.zero;
                if (data.IsCircle)
                {
                    collision.ColliderShape = WorldInteractionColliderShape.Circle;
                    collision.ColliderRadius = data.Radius * scale;
                }
                else
                {
                    Vector2 direction = b - a;
                    segment.transform.localRotation = Quaternion.Euler(0f, 0f,
                        Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
                    collision.ColliderShape = WorldInteractionColliderShape.Box;
                    collision.SetLocalBounds(Vector2.zero, new Vector2(direction.magnitude, data.Thickness * scale));
                }
                collision.MarkSpatialDirty();
            }
            return root;
        }

        private static Vector2 PixelPoint(Vector2 pixel, Vector2 entrance, float scale) =>
            new Vector2(pixel.x - entrance.x, entrance.y - pixel.y) * scale;

        private static void BuildScene()
        {
            GameObject playerAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
            GameObject cameraAsset = AssetDatabase.LoadAssetAtPath<GameObject>(CameraPath);
            if (playerAsset == null || cameraAsset == null)
                throw new InvalidOperationException("The original player and camera prefabs must exist.");
            Scene previous = SceneManager.GetActiveScene();
            bool replaceEmpty = SceneManager.sceneCount == 1 && string.IsNullOrEmpty(previous.path)
                && !previous.isDirty && previous.rootCount == 0;
            if (!replaceEmpty)
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                        throw new InvalidOperationException("Save the untitled scene before generating the independent Demo scene.");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                replaceEmpty ? NewSceneMode.Single : NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var machineObject = (GameObject)PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), scene);
                var playerObject = (GameObject)PrefabUtility.InstantiatePrefab(playerAsset, scene);
                var cameraObject = (GameObject)PrefabUtility.InstantiatePrefab(cameraAsset, scene);
                WasteRobotBase machine = machineObject.GetComponent<WasteRobotBase>();
                RobotMover player = playerObject.GetComponent<RobotMover>();
                Camera camera = cameraObject.GetComponent<Camera>();
                playerObject.name = "Demo Player (original prefab instance)";
                playerObject.transform.SetPositionAndRotation(machine.PortalToWorld(new Vector2(0f, 2f)),
                    Quaternion.Euler(0f, 0f, 180f));
                cameraObject.name = "Demo Camera (original prefab instance)";
                cameraObject.tag = "MainCamera";
                camera.orthographic = true;
                camera.orthographicSize = 8f;
                camera.backgroundColor = new Color(.008f, .011f, .014f);
                camera.clearFlags = CameraClearFlags.SolidColor;
                cameraObject.transform.SetPositionAndRotation(new Vector3(0f, 0f, -10f), Quaternion.identity);
                var followSettings = new SerializedObject(cameraObject.GetComponent<RobotCameraFollow>());
                followSettings.FindProperty("followTargetRotation").boolValue = false;
                followSettings.ApplyModifiedPropertiesWithoutUndo();
                foreach (Behaviour behaviour in playerObject.GetComponents<Behaviour>())
                    if (behaviour.GetType().Name == "PhotoModeController" || behaviour.GetType().Name == "BioScanController")
                    {
                        behaviour.enabled = false;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(behaviour);
                    }
                var bootstrapObject = new GameObject("Waste Robot Demo Scene");
                SceneManager.MoveGameObjectToScene(bootstrapObject, scene);
                bootstrapObject.AddComponent<WasteRobotDemoScene>().Configure(machine, player, camera);
                machine.Bind(player.transform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(machine);
                PrefabUtility.RecordPrefabInstancePropertyModifications(playerObject.transform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(cameraObject.transform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(camera);
                PrefabUtility.RecordPrefabInstancePropertyModifications(cameraObject);
                PrefabUtility.RecordPrefabInstancePropertyModifications(cameraObject.GetComponent<RobotCameraFollow>());
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new InvalidOperationException("Unable to save the independent Demo scene.");
            }
            finally
            {
                if (replaceEmpty) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                else
                {
                    if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            if (!path.StartsWith("Assets/Demos", StringComparison.Ordinal))
                throw new InvalidOperationException("Folder creation must stay in the Demo tree.");
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}

