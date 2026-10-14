using System;
using System.IO;
using System.Linq;
using System.Text;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TerrainTools;

namespace AnimalGame.Editor
{
    /// <summary>Persistent authoring terrain. Creation never overwrites sculpted TerrainData.</summary>
    public static class TerrainGridV1Builder
    {
        public const string ScenePath = "Assets/Scenes/3DMapTestScene.unity";
        public const string Folder = "Assets/Maps/TerrainGridV1";
        public const string RootName = "Terrain Grid V1 - 500m";
        public const int Resolution = 2049;
        public const float TileSize = 250f;
        public const float HeightRange = 70f;
        public static readonly Vector3 GridOrigin = new Vector3(300f, 0f, 0f);
        private const string ReportPath = "Temp/TerrainGridV1/GridValidation.txt";

        [MenuItem("Animal Game/Terrain Grid V1/Create Grid and Height References")]
        public static void Create()
        {
            Scene scene = RequireScene();
            Require(FindRoot(scene) == null, "The V1 grid already exists. Sculpt it; creation does not overwrite it.");
            EnsureFolder(Folder);
            for (int i = 0; i < 4; i++)
                Require(!File.Exists(DataPath(i % 2, i / 2)), "A V1 TerrainData already exists. Restore its grid instead of overwriting it.");

            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Maps/TerrainPrototype/PrototypeTerrain.mat");
            TerrainLayer grass = AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/Maps/TerrainPrototype/Grass.terrainlayer");
            TerrainLayer rock = AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/Maps/TerrainPrototype/Rock.terrainlayer");
            Require(material != null && grass != null && rock != null, "The existing terrain material/layers are missing.");

            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.position = GridOrigin;
            Undo.RegisterCreatedObjectUndo(root, "Create four-tile V1 terrain grid");

            try
            {
                for (int z = 0; z < 2; z++)
                {
                    for (int x = 0; x < 2; x++)
                    {
                        EditorUtility.DisplayProgressBar("Create V1 Terrain Grid", $"Creating Tile_{x}_{z}...", (z * 2 + x) / 4f);
                        var data = new TerrainData
                        {
                            name = $"Tile_{x}_{z}_TerrainData",
                            heightmapResolution = Resolution,
                            size = new Vector3(TileSize, HeightRange, TileSize),
                            alphamapResolution = 256,
                            baseMapResolution = 512,
                            terrainLayers = new[] { grass, rock }
                        };
                        var heights = new float[Resolution, Resolution];
                        for (int row = 0; row < Resolution; row++)
                            for (int column = 0; column < Resolution; column++)
                                heights[row, column] = HeightAt(x * TileSize + column * TileSize / (Resolution - 1),
                                    z * TileSize + row * TileSize / (Resolution - 1)) / HeightRange;
                        data.SetHeights(0, 0, heights);
                        PaintHeightLayers(data);
                        AssetDatabase.CreateAsset(data, DataPath(x, z));
                        var obj = Terrain.CreateTerrainGameObject(data);
                        obj.name = $"Tile_{x}_{z}";
                        obj.transform.SetParent(root.transform, false);
                        obj.transform.localPosition = new Vector3(x * TileSize, 0f, z * TileSize);
                        Terrain terrain = obj.GetComponent<Terrain>();
                        terrain.materialTemplate = material;
                        terrain.groupingID = 100;
                        terrain.allowAutoConnect = true;
                        terrain.heightmapPixelError = 1f;
                        terrain.basemapDistance = 600f;
                        terrain.Flush();
                        EditorUtility.SetDirty(data);
                        EditorUtility.SetDirty(terrain);
                    }
                }
                Reconnect(false);
                AddHeightReferences(scene, root);
                string validation = ValidateGrid();
                EditorSceneManager.MarkSceneDirty(scene);
                AssetDatabase.SaveAssets();
                Require(EditorSceneManager.SaveScene(scene), "Could not save 3DMapTestScene.");
                Debug.Log(validation);
                FocusGrid();
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        // Evaluate one continuous world-space surface for all tiles, including shared endpoints.
        private static float HeightAt(float x, float z)
        {
            float hill = 54f * Mathf.Exp(-((x - 250f) * (x - 250f) + (z - 260f) * (z - 260f)) / (2f * 85f * 85f));
            float shoulder = 10f * Mathf.Exp(-((x - 110f) * (x - 110f) + (z - 380f) * (z - 380f)) / (2f * 42f * 42f));
            float valley = 7f * Mathf.Exp(-(z - 105f) * (z - 105f) / (2f * 18f * 18f))
                * Mathf.Exp(-(x - 250f) * (x - 250f) / (2f * 220f * 220f));
            return Mathf.Clamp(10f + hill + shoulder - valley, 0f, HeightRange);
        }

        private static void PaintHeightLayers(TerrainData data)
        {
            int n = data.alphamapResolution;
            var alpha = new float[n, n, 2];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    float stone = Mathf.SmoothStep(0f, 1f, (data.GetInterpolatedHeight(x / (n - 1f), z / (n - 1f)) - 28f) / 32f);
                    alpha[z, x, 0] = 1f - stone;
                    alpha[z, x, 1] = stone;
                }
            data.SetAlphamaps(0, 0, alpha);
        }

        private static void AddHeightReferences(Scene scene, GameObject grid)
        {
            var gridReference = new GameObject("Height Reference - Four Tile Grid");
            gridReference.transform.SetParent(grid.transform, false);
            var measurement = gridReference.AddComponent<TerrainHeightReference>();
            measurement.Configure(GetGridTerrains(), new Vector3(GridOrigin.x - 18f, 0f, 250f));
            Require(measurement.RefreshMeasurements(), "The grid reference could not measure the new terrain.");
            EditorUtility.SetDirty(measurement);

            Terrain original = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Terrain>(true))
                .FirstOrDefault(t => t.name == "Main Map Replica Terrain");
            if (original == null) return;
            var oldReference = new GameObject("Height Reference - Original V1 Terrain");
            SceneManager.MoveGameObjectToScene(oldReference, scene);
            var originalMeasurement = oldReference.AddComponent<TerrainHeightReference>();
            originalMeasurement.Configure(new[] { original }, new Vector3(-18f, 0f, 125f));
            Require(originalMeasurement.RefreshMeasurements(), "The original V1 reference could not measure its terrain.");
            Undo.RegisterCreatedObjectUndo(oldReference, "Create original V1 height reference");
            EditorUtility.SetDirty(originalMeasurement);
        }

        [MenuItem("Animal Game/Terrain Grid V1/Reconnect Neighbors")]
        public static void Reconnect() => Reconnect(true);

        internal static void Reconnect(bool save)
        {
            Terrain[] tiles = GetGridTerrains();
            for (int z = 0; z < 2; z++)
                for (int x = 0; x < 2; x++)
                {
                    Terrain tile = tiles[z * 2 + x];
                    Undo.RecordObject(tile, "Reconnect V1 terrain neighbors");
                    tile.groupingID = 100;
                    tile.allowAutoConnect = true;
                    tile.SetNeighbors(x > 0 ? tiles[z * 2 + x - 1] : null,
                        z < 1 ? tiles[(z + 1) * 2 + x] : null,
                        x < 1 ? tiles[z * 2 + x + 1] : null,
                        z > 0 ? tiles[(z - 1) * 2 + x] : null);
                    tile.Flush();
                    EditorUtility.SetDirty(tile);
                }
            if (save)
            {
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
                Debug.Log(ValidateGrid());
            }
        }

        public static Terrain[] GetGridTerrains()
        {
            Scene scene = RequireScene();
            GameObject root = FindRoot(scene);
            Require(root != null, "The V1 terrain grid is missing. Create it first.");
            var tiles = new Terrain[4];
            for (int z = 0; z < 2; z++)
                for (int x = 0; x < 2; x++)
                {
                    Transform child = root.transform.Find($"Tile_{x}_{z}");
                    tiles[z * 2 + x] = child != null ? child.GetComponent<Terrain>() : null;
                    Require(tiles[z * 2 + x] != null, $"Tile_{x}_{z} is missing.");
                }
            return tiles;
        }

        [MenuItem("Animal Game/Terrain Grid V1/Validate Grid and Seams")]
        public static void Validate() => Debug.Log(ValidateGrid());

        public static string ValidateGrid()
        {
            Terrain[] tiles = GetGridTerrains();
            var assets = new System.Collections.Generic.HashSet<TerrainData>();
            for (int z = 0; z < 2; z++)
                for (int x = 0; x < 2; x++)
                {
                    Terrain tile = tiles[z * 2 + x];
                    TerrainData data = tile.terrainData;
                    Require(tile.enabled && tile.gameObject.activeInHierarchy && tile.drawHeightmap, $"{tile.name} is not enabled.");
                    Require(data != null && assets.Add(data), "Each tile must use its own TerrainData.");
                    Require(data.heightmapResolution == Resolution && data.size == new Vector3(TileSize, HeightRange, TileSize),
                        $"{tile.name}: use 250 x 70 x 250 m and 2049 height resolution.");
                    Require(tile.transform.position == GridOrigin + new Vector3(x * TileSize, 0f, z * TileSize)
                        && tile.transform.lossyScale == Vector3.one && tile.transform.rotation == Quaternion.identity,
                        $"{tile.name}: terrain position, rotation, or scale changed.");
                    var collider = tile.GetComponent<TerrainCollider>();
                    Require(collider != null && collider.enabled && collider.terrainData == data, $"{tile.name}: collider TerrainData differs.");
                    Require(tile.groupingID == 100 && tile.allowAutoConnect, $"{tile.name}: neighbor auto connection is disabled.");
                    Require(tile.leftNeighbor == (x > 0 ? tiles[z * 2 + x - 1] : null)
                        && tile.rightNeighbor == (x < 1 ? tiles[z * 2 + x + 1] : null)
                        && tile.topNeighbor == (z < 1 ? tiles[(z + 1) * 2 + x] : null)
                        && tile.bottomNeighbor == (z > 0 ? tiles[(z - 1) * 2 + x] : null), $"{tile.name}: neighbor links differ from the 2 x 2 layout.");
                }
            float maximumError = 0f;
            int comparisons = 0;
            for (int z = 0; z < 2; z++)
                CompareEdges(tiles[z * 2].terrainData.GetHeights(Resolution - 1, 0, 1, Resolution),
                    tiles[z * 2 + 1].terrainData.GetHeights(0, 0, 1, Resolution), ref maximumError, ref comparisons);
            for (int x = 0; x < 2; x++)
                CompareEdges(tiles[x].terrainData.GetHeights(0, Resolution - 1, Resolution, 1),
                    tiles[x + 2].terrainData.GetHeights(0, 0, Resolution, 1), ref maximumError, ref comparisons);
            Require(maximumError <= 0.00001f, $"Shared height edges differ by {maximumError:F6} m. Fix the source terrain before exporting.");
            var report = new StringBuilder("PASS: four-tile V1 authoring grid\n");
            report.AppendLine("4 independent TerrainData assets; each 250 x 250 m, height range 0..70 m, 2049 samples, upright unit scale.");
            report.AppendLine($"Shared-edge samples: {comparisons}; maximum difference: {maximumError:F8} m. Four-way corner included.");
            report.AppendLine("Grouping ID 100; four-way neighbors and matching TerrainColliders verified.");
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, report.ToString());
            return report.ToString();
        }

        private static void CompareEdges(float[,] a, float[,] b, ref float maximumError, ref int comparisons)
        {
            Require(a.GetLength(0) == b.GetLength(0) && a.GetLength(1) == b.GetLength(1), "Edge dimensions differ.");
            for (int z = 0; z < a.GetLength(0); z++)
                for (int x = 0; x < a.GetLength(1); x++)
                {
                    maximumError = Mathf.Max(maximumError, Mathf.Abs(a[z, x] - b[z, x]) * HeightRange);
                    comparisons++;
                }
        }

        [MenuItem("Animal Game/Terrain Grid V1/Refresh Height References")]
        public static void RefreshReferences()
        {
            Scene scene = RequireScene();
            foreach (TerrainHeightReference reference in scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<TerrainHeightReference>(true)))
            {
                Undo.RecordObject(reference, "Refresh terrain reference measurements");
                Require(reference.RefreshMeasurements(), reference.MeasurementStatus);
                EditorUtility.SetDirty(reference);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            SceneView.RepaintAll();
        }

        [MenuItem("Animal Game/Terrain Grid V1/Frame Grid")]
        public static void FocusGrid()
        {
            GameObject root = FindRoot(RequireScene());
            Require(root != null, "The V1 grid is missing.");
            Selection.activeGameObject = root;
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null) return;
            view.in2DMode = false;
            view.drawGizmos = true;
            view.LookAt(GridOrigin + new Vector3(245f, 25f, 250f), Quaternion.Euler(35f, -35f, 0f), 420f, false);
            view.Repaint();
        }

        // A brush spanning the central junction must gather all four connected terrains.
        public static string ValidateBrushCoverage()
        {
            Terrain[] tiles = GetGridTerrains();
            PaintContext context = TerrainPaintUtility.BeginPaintHeightmap(tiles[0], new Rect(230f, 230f, 40f, 40f), 1);
            try
            {
                Require(context.terrainCount == 4, "A brush over the central junction did not gather all four Terrain tiles.");
                return "PASS: a 40 m height brush at the central junction gathers all four connected Terrain tiles.";
            }
            finally { TerrainPaintUtility.ReleaseContextResources(context); }
        }

        public static void SetupAndExport()
        {
            OpenAuthoringScene();
            if (FindRoot(SceneManager.GetActiveScene()) == null) Create();
            else { ValidateGrid(); RefreshReferences(); }
            TerrainGridV1Exporter.Export();
            string brush = ValidateBrushCoverage();
            File.WriteAllText("Temp/TerrainGridV1/BrushCoverage.txt", brush);
            FocusGrid();
            Debug.Log("Terrain Grid V1 setup and export completed. " + brush);
        }

        internal static void OpenAuthoringScene()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play Mode before editing Terrain.");
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
        }

        private static Scene RequireScene()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && SceneManager.GetActiveScene().path == ScenePath,
                "Open 3DMapTestScene in Edit Mode first.");
            return SceneManager.GetActiveScene();
        }

        private static GameObject FindRoot(Scene scene) => scene.GetRootGameObjects().FirstOrDefault(r => r.name == RootName);
        private static string DataPath(int x, int z) => $"{Folder}/Tile_{x}_{z}_TerrainData.asset";

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
