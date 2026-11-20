using System;
using System.IO;
using System.Linq;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace AnimalGame.Editor
{
    /// <summary>Creates the independent four-tile prairie scale study once; preserves subsequent sculpting.</summary>
    [InitializeOnLoad]
    public static class PlainsSample500Builder
    {
        public const string ScenePath = "Assets/Scenes/GameMaps/Plains_Sample_500m.unity";
        public const string Folder = "Assets/Maps/PlainsSample500";
        public const string RootName = "Plains Sample 500m";
        public const int Resolution = 2049;
        public const float TileSize = 250f, HeightRange = 350f;
        public const string Reports = "Temp/PlainsSample500";
        static PlainsSample500Builder() { SceneView.duringSceneGui += DrawTools; }

        [MenuItem("Animal Game/Plains Sample 500m/Create or Open Authoring Sample")]
        public static void Create()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play before creating the sample.");
            if (File.Exists(ScenePath))
            {
                SaveLoadedScenes();
                EditorSceneManager.OpenScene(ScenePath);
                Frame();
                return;
            }
            Require(!AssetDatabase.IsValidFolder(Folder), "Sample assets already exist without their scene; restore that scene before continuing.");
            string analyticReport = PlainsSample500HeightField.Validate();
            SaveLoadedScenes();
            EnsureFolder(Folder + "/data");
            Require(AssetDatabase.CopyAsset("Assets/Maps/Official_Map_01/Plains/PlainsTerrain.mat", Folder + "/SampleTerrain.mat"), "Could not copy the grassland material.");
            Require(AssetDatabase.CopyAsset("Assets/Maps/Official_Map_01/Plains/Grass.terrainlayer", Folder + "/Grass.terrainlayer"), "Could not copy the grassland layer.");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/SampleTerrain.mat");
            TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(Folder + "/Grass.terrainlayer");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject(RootName);
            var terrains = new Terrain[4];
            try
            {
                var alpha = new float[128, 128, 1];
                for (int z = 0; z < 128; z++) for (int x = 0; x < 128; x++) alpha[z, x, 0] = 1;
                for (int tz = 0; tz < 2; tz++) for (int tx = 0; tx < 2; tx++)
                {
                    string name = $"Sample_{tx}_{tz}";
                    FileProgress("Creating " + name);
                    EditorUtility.DisplayProgressBar("Create prairie sample", name, (tz * 2 + tx) / 4f);
                    var heights = new float[Resolution, Resolution];
                    for (int z = 0; z < Resolution; z++) for (int x = 0; x < Resolution; x++)
                        heights[z, x] = PlainsSample500HeightField.Evaluate(tx * TileSize + x * TileSize / (Resolution - 1),
                            tz * TileSize + z * TileSize / (Resolution - 1)) / HeightRange;
                    var data = new TerrainData { name = name + "_TerrainData", heightmapResolution = Resolution,
                        size = new Vector3(TileSize, HeightRange, TileSize), alphamapResolution = 128,
                        baseMapResolution = 512, terrainLayers = new[] { layer } };
                    data.SetHeights(0, 0, heights);
                    data.SetAlphamaps(0, 0, alpha);
                    AssetDatabase.CreateAsset(data, Folder + "/data/" + name + "_TerrainData.asset");
                    var obj = Terrain.CreateTerrainGameObject(data);
                    obj.name = name;
                    obj.transform.SetParent(root.transform, false);
                    obj.transform.localPosition = new Vector3(tx * TileSize, 0, tz * TileSize);
                    var terrain = obj.GetComponent<Terrain>();
                    terrain.materialTemplate = material;
                    terrain.groupingID = 25001;
                    terrain.allowAutoConnect = true;
                    terrain.heightmapPixelError = 1f;
                    terrain.basemapDistance = 2000;
                    // Set the per-object serialized flag; the public setter can affect other loaded terrains.
                    var serialized = new SerializedObject(terrain);
                    serialized.FindProperty("m_DrawInstanced").boolValue = true;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    terrain.Flush();
                    terrains[tz * 2 + tx] = terrain;
                    EditorUtility.SetDirty(data);
                    AssetDatabase.SaveAssetIfDirty(data);
                }
                for (int z = 0; z < 2; z++) for (int x = 0; x < 2; x++)
                    terrains[z * 2 + x].SetNeighbors(x > 0 ? terrains[z * 2 + x - 1] : null,
                        z < 1 ? terrains[(z + 1) * 2 + x] : null, x < 1 ? terrains[z * 2 + x + 1] : null,
                        z > 0 ? terrains[(z - 1) * 2 + x] : null);
                AddLightingAndCamera();
                AddReferences(root, terrains);
                Require(EditorSceneManager.SaveScene(scene, ScenePath), "Could not save the authoring sample.");
                Directory.CreateDirectory(Reports);
                File.WriteAllText(Reports + "/AnalyticValidation.txt", analyticReport);
                Validate();
                Frame();
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        [MenuItem("Animal Game/Plains Sample 500m/Export Current Sample and Open Gameplay")]
        public static void ExportCurrentSample()
        {
            Terrain[] terrains = GetTerrains();
            foreach (Terrain terrain in terrains)
            {
                terrain.terrainData.SyncHeightmap();
                AssetDatabase.SaveAssetIfDirty(terrain.terrainData);
            }
            SaveLoadedScenes();
            PlainsSample500Gameplay.Export(terrains);
            PlainsSample500Gameplay.Deploy();
        }

        [MenuItem("Animal Game/Plains Sample 500m/Validate Current Sample Terrain")]
        public static void Validate()
        {
            Terrain[] terrains = GetTerrains();
            Require(terrains.Length == 4, "Expected four independent sample terrains.");
            foreach (Terrain terrain in terrains)
            {
                Require(terrain.terrainData.heightmapResolution == Resolution
                    && terrain.terrainData.size == new Vector3(TileSize, HeightRange, TileSize)
                    && terrain.transform.lossyScale == Vector3.one && terrain.transform.rotation == Quaternion.identity,
                    "The sample lost its V1 metric dimensions.");
                Require(terrain.GetComponent<TerrainCollider>().terrainData == terrain.terrainData, "Collider link changed.");
            }
            float maximumSeam = 0;
            for (int i = 0; i < 4; i++)
            {
                if (i % 2 == 0) maximumSeam = Mathf.Max(maximumSeam, EdgeError(terrains[i], terrains[i + 1], true));
                if (i < 2) maximumSeam = Mathf.Max(maximumSeam, EdgeError(terrains[i], terrains[i + 2], false));
            }
            Require(maximumSeam < .001f, "The sample contains a discontinuity between native terrain tiles.");
            string report = PlainsSample500HeightField.Validate((x, z) => Sample(terrains, x, z));
            Directory.CreateDirectory(Reports);
            File.WriteAllText(Reports + "/NativeValidation.txt", "PASS: four 250 m / 2049 sample TerrainData; exact shared boundaries.\n"
                + "Maximum native seam difference: " + maximumSeam + " m.\n" + report);
            Debug.Log("PASS: prairie sample native heights, hill dimensions, contours and boundaries.\n" + report);
        }

        public static Terrain[] GetTerrains()
        {
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.path == ScenePath, "Open Plains_Sample_500m authoring scene first.");
            var root = scene.GetRootGameObjects().Single(o => o.name == RootName);
            return root.GetComponentsInChildren<Terrain>(true).OrderBy(t => t.transform.position.z)
                .ThenBy(t => t.transform.position.x).ToArray();
        }

        [MenuItem("Animal Game/Plains Sample 500m/Frame Whole Sample")]
        public static void Frame()
        {
            Selection.activeGameObject = GetTerrains()[0].transform.parent.gameObject;
            var view = SceneView.lastActiveSceneView;
            if (view == null) return;
            view.in2DMode = false;
            view.LookAt(new Vector3(250, 85, 250), Quaternion.Euler(40, -35, 0), 340, false);
            view.Repaint();
        }
        private static float Sample(Terrain[] tiles, float x, float z)
        {
            int tx = Mathf.Clamp(Mathf.FloorToInt(x / TileSize), 0, 1), tz = Mathf.Clamp(Mathf.FloorToInt(z / TileSize), 0, 1);
            return tiles[tz * 2 + tx].terrainData.GetInterpolatedHeight((x - tx * TileSize) / TileSize, (z - tz * TileSize) / TileSize);
        }
        private static float EdgeError(Terrain a, Terrain b, bool right)
        {
            float[,] aa = a.terrainData.GetHeights(right ? Resolution - 1 : 0, right ? 0 : Resolution - 1,
                right ? 1 : Resolution, right ? Resolution : 1);
            float[,] bb = b.terrainData.GetHeights(0, 0, right ? 1 : Resolution, right ? Resolution : 1);
            float error = 0;
            for (int i = 0; i < Resolution; i++)
                error = Mathf.Max(error, Mathf.Abs((right ? aa[i, 0] : aa[0, i]) - (right ? bb[i, 0] : bb[0, i])) * HeightRange);
            return error;
        }
        private static void AddLightingAndCamera()
        {
            var lightObject = new GameObject("Sample Sun", typeof(Light), typeof(UniversalAdditionalLightData));
            lightObject.transform.rotation = Quaternion.Euler(38, -40, 0);
            var light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.3f; light.shadows = LightShadows.Soft;
            light.color = new Color(1, .96f, .86f);
            RenderSettings.sun = light;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.53f, .6f, .65f);
            RenderSettings.ambientEquatorColor = new Color(.34f, .39f, .31f);
            RenderSettings.ambientGroundColor = new Color(.18f, .22f, .17f);
            var cameraObject = new GameObject("Sample Overview Camera", typeof(Camera), typeof(AudioListener), typeof(UniversalAdditionalCameraData));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(250, 350, -300);
            cameraObject.transform.LookAt(new Vector3(250, 85, 250));
            var camera = cameraObject.GetComponent<Camera>();
            camera.farClipPlane = 1800; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.32f, .43f, .49f);
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            Require(pipeline != null, "The sample needs the existing Universal pipeline.");
            var renderers = new SerializedObject(pipeline).FindProperty("m_RendererDataList");
            for (int i = 0; i < renderers.arraySize; i++)
                if (AssetDatabase.GetAssetPath(renderers.GetArrayElementAtIndex(i).objectReferenceValue)
                    == "Assets/Settings/TerrainPrototypeRenderer.asset")
                    cameraObject.GetComponent<UniversalAdditionalCameraData>().SetRenderer(i);
        }
        private static void AddReferences(GameObject root, Terrain[] terrains)
        {
            var referenceRoot = new GameObject("Scale References - 1 unit is 1 metre");
            var reference = referenceRoot.AddComponent<TerrainHeightReference>();
            reference.Configure(terrains, new Vector3(-12, 80, 40));
            Require(reference.RefreshMeasurements(), "Could not measure the sample.");
            var settings = new SerializedObject(reference);
            settings.FindProperty("showOneMeterTicks").boolValue = false;
            settings.FindProperty("showProjectionLines").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Scale Reference Yellow" };
            material.color = new Color(1, .78f, .14f);
            AssetDatabase.CreateAsset(material, Folder + "/ScaleReference.mat");
            for (int i = 0; i < 3; i++)
            {
                float x = 25 + i * 5, z = 25;
                var figure = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                figure.name = "2m Person Reference " + (i + 1);
                figure.transform.SetParent(referenceRoot.transform, false);
                figure.transform.position = new Vector3(x, Sample(terrains, x, z) + 1, z);
                figure.transform.localScale = new Vector3(.6f, 1, .6f);
                figure.GetComponent<Renderer>().sharedMaterial = material;
                UnityEngine.Object.DestroyImmediate(figure.GetComponent<Collider>());
            }
            AddPost(referenceRoot, "7m Height Reference", 7, 43, terrains, material);
            AddPost(referenceRoot, "14m Height Reference", 14, 49, terrains, material);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "1m Cube Reference"; cube.transform.SetParent(referenceRoot.transform, false);
            cube.transform.position = new Vector3(18, Sample(terrains, 18, 25) + .5f, 25);
            cube.GetComponent<Renderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
        }
        private static void AddPost(GameObject root, string name, float height, float x, Terrain[] terrains, Material material)
        {
            var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            post.name = name; post.transform.SetParent(root.transform, false);
            post.transform.position = new Vector3(x, Sample(terrains, x, 25) + height * .5f, 25);
            post.transform.localScale = new Vector3(.25f, height, .25f);
            post.GetComponent<Renderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(post.GetComponent<Collider>());
        }
        private static void DrawTools(SceneView view)
        {
            if (SceneManager.GetActiveScene().path != ScenePath || Application.isPlaying) return;
            Handles.BeginGUI();
            GUILayout.BeginArea(new Rect(12, view.position.height - 188, 300, 150), GUI.skin.window);
            GUILayout.Label("500 x 500 m 草原样区 / 四块连续 Terrain");
            GUILayout.Label("丘陵宽度 × 0.75；高差 8–21m；等高线 7m");
            GUILayout.Label("黄色人物高 2m；黄色杆高 7m / 14m");
            if (GUILayout.Button("查看整个样区")) Frame();
            if (GUILayout.Button("近看 2m 人物与高度参照"))
            {
                view.in2DMode = false;
                view.LookAt(new Vector3(34, 85, 25), Quaternion.Euler(25, -20, 0), 22, false);
            }
            if (GUILayout.Button("打开游戏测试场景"))
            {
                SaveLoadedScenes();
                if (File.Exists(PlainsSample500Gameplay.ScenePath)) EditorSceneManager.OpenScene(PlainsSample500Gameplay.ScenePath);
            }
            GUILayout.EndArea();
            Handles.EndGUI();
        }
        private static void SaveLoadedScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isDirty) continue;
                Require(!string.IsNullOrEmpty(scene.path), "Save the untitled scene before creating the sample.");
                Require(EditorSceneManager.SaveScene(scene), "Could not preserve " + scene.path);
            }
        }
        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
        private static void FileProgress(string value) { Directory.CreateDirectory(Reports); File.WriteAllText(Reports + "/Progress.txt", value); }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
