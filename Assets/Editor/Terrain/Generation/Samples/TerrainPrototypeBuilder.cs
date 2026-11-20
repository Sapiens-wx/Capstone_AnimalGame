using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace AnimalGame.Editor
{
    /// <summary>Creates a persistent, brush-editable terrain once; never regenerates on load.</summary>
    public static class TerrainPrototypeBuilder
    {
        public const string ScenePath = "Assets/Scenes/3DMapTestScene.unity";
        public const string Folder = "Assets/Maps/TerrainPrototype";
        private const string TerrainPath = Folder + "/TestTerrainData.asset";
        private const string RendererPath = "Assets/Settings/TerrainPrototypeRenderer.asset";
        private const string RootName = "Terrain Prototype - 64m";
        private const int Resolution = 513;
        private const float Size = 64f;
        private const float HeightRange = 20f;
        private static readonly float[] LaneX = { 20f, 30f, 40f };
        private static readonly float[] LaneAngles = { 10f, 25f, 45f };

        [MenuItem("Animal Game/Terrain Prototype/Create in 3DMapTestScene")]
        public static void Create()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != ScenePath)
                throw new InvalidOperationException("Open 3DMapTestScene in Edit Mode first.");
            if (AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainPath) != null
                || GameObject.Find(RootName) != null)
                throw new InvalidOperationException("The prototype already exists. Sculpt the saved Terrain with Unity's brushes.");

            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/Maps", "TerrainPrototype");
            int rendererIndex = EnsureRenderer();
            TerrainLayer grass = CreateLayer("Grass", new Color(0.32f, 0.43f, 0.23f), 13f);
            TerrainLayer dirt = CreateLayer("Trail", new Color(0.57f, 0.44f, 0.28f), 29f);
            TerrainLayer rock = CreateLayer("Rock", new Color(0.46f, 0.49f, 0.47f), 47f);

            var data = new TerrainData
            {
                name = "TestTerrainData",
                heightmapResolution = Resolution,
                size = new Vector3(Size, HeightRange, Size),
                alphamapResolution = 512,
                baseMapResolution = 512,
                terrainLayers = new[] { grass, dirt, rock }
            };
            float[,] heights = new float[Resolution, Resolution];
            for (int z = 0; z < Resolution; z++)
                for (int x = 0; x < Resolution; x++)
                    heights[z, x] = HeightAt(x * Size / (Resolution - 1), z * Size / (Resolution - 1)) / HeightRange;
            AssetDatabase.CreateAsset(data, TerrainPath);
            data.SetHeights(0, 0, heights);
            PaintSurface(data);

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Create terrain prototype");
            GameObject terrainObject = Terrain.CreateTerrainGameObject(data);
            terrainObject.name = "Test Terrain (select to sculpt)";
            terrainObject.transform.SetParent(root.transform, false);
            Terrain terrain = terrainObject.GetComponent<Terrain>();
            var terrainMaterial = new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit"))
            {
                name = "Prototype Terrain"
            };
            AssetDatabase.CreateAsset(terrainMaterial, Folder + "/PrototypeTerrain.mat");
            terrain.materialTemplate = terrainMaterial;
            terrain.heightmapPixelError = 1f;
            terrain.basemapDistance = 200f;
            terrain.drawInstanced = true;
            terrain.Flush();

            var sunObject = new GameObject("Terrain Sun", typeof(Light));
            sunObject.transform.SetParent(root.transform, false);
            sunObject.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
            Light sun = sunObject.GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.87f);
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            sun.shadowBias = 0.03f;
            sun.shadowNormalBias = 0.3f;
            sunObject.AddComponent<UniversalAdditionalLightData>();
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.53f, 0.62f, 0.70f);
            RenderSettings.ambientEquatorColor = new Color(0.40f, 0.43f, 0.38f);
            RenderSettings.ambientGroundColor = new Color(0.24f, 0.26f, 0.20f);
            RenderSettings.fog = false;

            Camera camera = null;
            foreach (GameObject obj in scene.GetRootGameObjects())
            {
                camera = obj.GetComponentInChildren<Camera>();
                if (camera != null) break;
            }
            if (camera == null)
            {
                var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                cameraObject.tag = "MainCamera";
                camera = cameraObject.GetComponent<Camera>();
            }
            Undo.RecordObject(camera, "Set terrain overview camera");
            Undo.RecordObject(camera.transform, "Position terrain overview camera");
            camera.orthographic = false;
            camera.fieldOfView = 48f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 300f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.17f, 0.20f);
            camera.transform.position = new Vector3(76f, 61f, -38f);
            camera.transform.LookAt(new Vector3(32f, 4f, 33f));
            UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.SetRenderer(rendererIndex);
            cameraData.renderShadows = true;
            cameraData.renderPostProcessing = false;
            cameraData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            EditorUtility.SetDirty(cameraData);

            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene))
                throw new IOException("Unity could not save the terrain scene.");
            Selection.activeGameObject = terrainObject;
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.in2DMode = false;
                SceneView.lastActiveSceneView.LookAt(new Vector3(32f, 5f, 32f),
                    Quaternion.Euler(45f, -32f, 0f), 48f, false, true);
            }
            SceneView.RepaintAll();
            Debug.Log("Terrain prototype saved. Use Unity's Terrain brushes to edit it. Animal Game/Terrain Prototype/Validate and Capture checks the reference heights.", terrain);
        }

        private static int EnsureRenderer()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline == null) throw new InvalidOperationException("This scene requires the project's URP asset.");
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                renderer.name = "TerrainPrototypeRenderer";
                AssetDatabase.CreateAsset(renderer, RendererPath);
            }
            var serialized = new SerializedObject(pipeline);
            SerializedProperty list = serialized.FindProperty("m_RendererDataList");
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == renderer) return i;
            Undo.RecordObject(pipeline, "Add terrain camera renderer");
            int index = list.arraySize;
            list.InsertArrayElementAtIndex(index);
            list.GetArrayElementAtIndex(index).objectReferenceValue = renderer;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(pipeline);
            return index;
        }

        // Compact domes have zero height and zero derivative at their perimeter.
        private static float Dome(float x, float z, float cx, float cz, float rx, float rz)
        {
            float dx = (x - cx) / rx;
            float dz = (z - cz) / rz;
            float v = Mathf.Max(0f, 1f - dx * dx - dz * dz);
            return v * v;
        }

        private static float Fade(float a, float b, float value)
        {
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, value));
        }

        private static float RoundedPositive(float value, float radius)
        {
            if (value <= -radius) return 0f;
            if (value >= radius) return value;
            return (value + radius) * (value + radius) / (4f * radius);
        }

        private static float HeightAt(float x, float z)
        {
            float height = 4f + 8f * Dome(x, z, 31f, 45f, 19f, 13f)
                + 1.5f * Dome(x, z, 46f, 47f, 10f, 9f)
                - 2f * Dome(x, z, 52f, 23f, 8f, 12f);
            for (int i = 0; i < LaneX.Length; i++)
            {
                float grade = Mathf.Tan(LaneAngles[i] * Mathf.Deg2Rad);
                float start = 39f - 6f / grade;
                float rawRise = (z - start) * grade;
                float rise = RoundedPositive(rawRise, 0.2f) - RoundedPositive(rawRise - 6f, 0.2f);
                float weight = (1f - Fade(3f, 5.5f, Mathf.Abs(x - LaneX[i])))
                    * Fade(start - 3f, start - 1f, z) * (1f - Fade(43f, 49f, z));
                height = Mathf.Lerp(height, 4f + rise, weight);
            }
            // Two isolated shelves: their very short front transitions are the ledge probes.
            for (int i = 0; i < 2; i++)
            {
                float centerX = i == 0 ? 7f : 13f;
                float stepHeight = i == 0 ? 0.4f : 1f;
                float weight = (1f - Fade(1.7f, 3f, Mathf.Abs(x - centerX)))
                    * Fade(22f, 24f, z) * (1f - Fade(34f, 38f, z));
                float shelf = stepHeight * Fade(25.9375f, 26.0625f, z)
                    * (1f - Fade(32f, 36f, z));
                height = Mathf.Lerp(height, 4f + shelf, weight);
            }
            return height + 1.4f * Dome(x, z, 58f, 58f, 2.8f, 2.8f);
        }

        private static TerrainLayer CreateLayer(string name, Color color, float seed)
        {
            var texture = new Texture2D(64, 64, TextureFormat.RGB24, false);
            texture.name = name + " Albedo";
            var pixels = new Color[64 * 64];
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    // Periodic waves make a subtle, seamless material swatch.
                    float u = x * Mathf.PI * 2f / 64f;
                    float v = y * Mathf.PI * 2f / 64f;
                    float variation = 1f + 0.035f * Mathf.Sin(u * 3f + seed) * Mathf.Sin(v * 5f)
                        + 0.025f * Mathf.Sin(u * 11f + v * 7f + seed);
                    pixels[y * 64 + x] = color * variation;
                }
            texture.SetPixels(pixels);
            texture.Apply();
            string path = Folder + "/" + name + "Albedo.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            var layer = new TerrainLayer
            {
                name = name,
                diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path),
                tileSize = new Vector2(4f, 4f),
                metallic = 0f,
                smoothness = 0.08f
            };
            AssetDatabase.CreateAsset(layer, Folder + "/" + name + ".terrainlayer");
            return layer;
        }

        private static void PaintSurface(TerrainData data)
        {
            int n = data.alphamapResolution;
            var weights = new float[n, n, 3];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)(n - 1);
                    float v = z / (float)(n - 1);
                    float px = u * Size;
                    float pz = v * Size;
                    float rock = Fade(23f, 47f, data.GetSteepness(u, v)) * 0.85f;
                    float trail = Dome(px, pz, 8f, 9f, 7f, 7f) * 0.85f;
                    for (int i = 0; i < LaneX.Length; i++)
                    {
                        float start = 39f - 6f / Mathf.Tan(LaneAngles[i] * Mathf.Deg2Rad);
                        float lane = (1f - Fade(1.1f, 2.3f, Mathf.Abs(px - LaneX[i])))
                            * Fade(start - 3f, start, pz) * (1f - Fade(41f, 44f, pz));
                        trail = Mathf.Max(trail, lane * 0.78f);
                    }
                    trail = Mathf.Max(trail, 0.7f * Dome(px, pz, 52f, 23f, 7f, 10f));
                    trail = Mathf.Max(trail, 0.8f * (1f - Fade(4.5f, 6.5f, Mathf.Abs(px - 10f)))
                        * Fade(23f, 25f, pz) * (1f - Fade(32f, 35f, pz)));
                    weights[z, x, 0] = (1f - rock) * (1f - trail);
                    weights[z, x, 1] = (1f - rock) * trail;
                    weights[z, x, 2] = rock;
                }
            data.SetAlphamaps(0, 0, weights);
            data.SetBaseMapDirty();
        }

        [MenuItem("Animal Game/Terrain Prototype/Validate and Capture")]
        public static void ValidateAndCapture()
        {
            Terrain terrain = FindTerrain();
            if (terrain == null) throw new InvalidOperationException("Open the generated terrain scene first.");
            TerrainData data = terrain.terrainData;
            var report = new StringBuilder();
            report.AppendLine("Terrain prototype validation (meters, terrain-local X/Z)");
            report.AppendLine($"Resolution: {data.heightmapResolution}; Size: {data.size}; Asset: {AssetDatabase.GetAssetPath(data)}");
            if (data.heightmapResolution != Resolution || data.size != new Vector3(Size, HeightRange, Size))
                throw new InvalidOperationException("Unexpected terrain dimensions.");
            if (terrain.GetComponent<TerrainCollider>().terrainData != data)
                throw new InvalidOperationException("Terrain collider does not reference the saved height field.");
            float[,,] trailWeights = data.GetAlphamaps(160, 175, 1, 1);
            report.AppendLine($"Trail surface weights: grass={trailWeights[0,0,0]:F3}, trail={trailWeights[0,0,1]:F3}, rock={trailWeights[0,0,2]:F3}");
            if (data.terrainLayers.Length != 3 || trailWeights[0,0,1] < 0.65f)
                throw new InvalidOperationException("The saved trail surface weights are missing.");
            CheckHeight(report, data, "Entrance", 8f, 9f, 4f);
            CheckHeight(report, data, "Valley", 52f, 23f, 2f);
            CheckHeight(report, data, "Orientation hill", 58f, 58f, 5.4f);
            for (int i = 0; i < LaneX.Length; i++)
            {
                float z = 39f - 3f / Mathf.Tan(LaneAngles[i] * Mathf.Deg2Rad);
                float rise = data.GetInterpolatedHeight(LaneX[i] / Size, (z + 0.5f) / Size)
                    - data.GetInterpolatedHeight(LaneX[i] / Size, (z - 0.5f) / Size);
                float angle = Mathf.Atan(rise) * Mathf.Rad2Deg;
                report.AppendLine($"Lane X={LaneX[i]}: {angle:F3} degrees (target {LaneAngles[i]})");
                if (Mathf.Abs(angle - LaneAngles[i]) > 0.15f)
                    throw new InvalidOperationException("A reference ramp angle changed: " + report);
            }
            CheckHeight(report, data, "Low ledge foot", 7f, 25.75f, 4f);
            CheckHeight(report, data, "Low ledge top", 7f, 26.25f, 4.4f);
            CheckHeight(report, data, "High ledge foot", 13f, 25.75f, 4f);
            CheckHeight(report, data, "High ledge top", 13f, 26.25f, 5f);
            report.AppendLine("PASS: saved TerrainData, collider, heights and slope reference checks.");
            Directory.CreateDirectory("Temp/TerrainPrototype");
            File.WriteAllText("Temp/TerrainPrototype/Validation.txt", report.ToString());
            CapturePreview();
            Debug.Log(report.ToString(), terrain);
        }

        private static void CheckHeight(StringBuilder report, TerrainData data, string label, float x, float z, float expected)
        {
            float actual = data.GetInterpolatedHeight(x / Size, z / Size);
            report.AppendLine($"{label} ({x:F2}, {z:F2}): {actual:F4} m; target {expected:F4} m");
            if (Mathf.Abs(actual - expected) > 0.015f)
                throw new InvalidOperationException("A reference height changed: " + report);
        }

        private static void CapturePreview()
        {
            Camera camera = Camera.main;
            if (camera == null || camera.gameObject.scene.path != ScenePath)
                throw new InvalidOperationException("No terrain overview camera.");
            var target = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGB32);
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            RenderTexture previous = RenderTexture.active;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply();
                File.WriteAllBytes("Temp/TerrainPrototype/Overview.png", image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(image);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static Terrain FindTerrain()
        {
            foreach (Terrain terrain in Terrain.activeTerrains)
                if (terrain.gameObject.scene.path == ScenePath && AssetDatabase.GetAssetPath(terrain.terrainData) == TerrainPath)
                    return terrain;
            return null;
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
        private static void DrawLandmarks(Terrain terrain, GizmoType type)
        {
            if (terrain.gameObject.scene.path != ScenePath || AssetDatabase.GetAssetPath(terrain.terrainData) != TerrainPath) return;
            Label(terrain, 8f, 9f, "ENTRY / 4 m");
            Label(terrain, 20f, 22f, "10 deg");
            Label(terrain, 30f, 32f, "25 deg");
            Label(terrain, 40f, 36f, "45 deg");
            Label(terrain, 31f, 46f, "SUMMIT");
            Label(terrain, 52f, 23f, "VALLEY / 2 m");
            Label(terrain, 7f, 29f, "STEP +0.4 m");
            Label(terrain, 13f, 29f, "STEP +1.0 m");
            Label(terrain, 58f, 58f, "+Z / +X marker");
        }

        private static void Label(Terrain terrain, float x, float z, string text)
        {
            float height = terrain.terrainData.GetInterpolatedHeight(x / Size, z / Size);
            Handles.Label(terrain.transform.position + new Vector3(x, height + 0.6f, z), text, EditorStyles.whiteBoldLabel);
        }
    }
}
