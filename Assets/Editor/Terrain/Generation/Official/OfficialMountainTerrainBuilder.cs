using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AnimalGame.Editor
{
    public static class OfficialMountainTerrainBuilder
    {
        public const string RootName = "Official Mountains - Editable Terrain";
        private const string Folder = "Assets/Maps/Official_Map_01/Mountains";
        private const string DataFolder = Folder + "/data";
        private const string MaterialPath = Folder + "/MountainsTerrain.mat";
        private const string LayerPath = Folder + "/Ground.terrainlayer";
        private const float TileSize = OfficialPlainsTerrainBuilder.TileSizeMeters;
        private const float HeightRange = OfficialPlainsTerrainBuilder.HeightRangeMeters;
        private const float InitialHeight = OfficialPlainsTerrainBuilder.InitialHeightMeters;
        private const int Resolution = OfficialPlainsTerrainBuilder.TileResolution;

        [MenuItem("Animal Game/Official Map 01/Create Flat Mountains Terrain")]
        public static void Create()
        {
            OfficialMapPlan plan = GetPlan();
            Scene scene = plan.gameObject.scene;
            GameObject existing = FindRoot(scene);
            if (existing != null)
            {
                OfficialPlainsTerrainBuilder.Reconnect(PlainsRoot(scene), plan);
                Validate();
                Frame();
                return;
            }

            GameObject plainsRoot = PlainsRoot(scene);
            var occupied = OfficialPlainsTerrainBuilder.TileDictionary(plainsRoot, plan, false);
            List<Vector2Int> cells = MountainCells(plan).Where(cell => !occupied.ContainsKey(cell)).ToList();
            Require(cells.Count > 0, "The mountain outline has no unoccupied terrain cells.");
            Require(!AssetDatabase.IsValidFolder(Folder) && !File.Exists(Folder + ".meta"),
                "Mountain terrain assets already exist. Inspect the saved assets before creating a new grid.");
            string sourceFolder = OfficialPlainsTerrainBuilder.Folder;
            Require(AssetDatabase.LoadAssetAtPath<Material>(sourceFolder + "/PlainsTerrain.mat") != null
                && AssetDatabase.LoadAssetAtPath<TerrainLayer>(sourceFolder + "/Grass.terrainlayer") != null,
                "The existing plains editing material and layer are required.");

            AssetDatabase.CreateFolder(Path.GetDirectoryName(Folder).Replace('\\', '/'), "Mountains");
            Require(AssetDatabase.IsValidFolder(Folder), "Could not create the mountain terrain folder.");
            AssetDatabase.CreateFolder(Folder, "data");
            Require(AssetDatabase.CopyAsset(sourceFolder + "/PlainsTerrain.mat", MaterialPath)
                && AssetDatabase.CopyAsset(sourceFolder + "/Grass.terrainlayer", LayerPath),
                "Could not create independent mountain editing material and layer.");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(LayerPath);
            var heights = new float[Resolution, Resolution];
            for (int z = 0; z < Resolution; z++)
                for (int x = 0; x < Resolution; x++)
                    heights[z, x] = InitialHeight / HeightRange;
            const int alphaResolution = 256;
            var alpha = new float[alphaResolution, alphaResolution, 1];
            for (int z = 0; z < alphaResolution; z++)
                for (int x = 0; x < alphaResolution; x++)
                    alpha[z, x, 0] = 1f;

            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.position = plan.transform.position;
            Undo.RegisterCreatedObjectUndo(root, "Create flat official mountains terrain");
            try
            {
                for (int index = 0; index < cells.Count; index++)
                {
                    Vector2Int cell = cells[index];
                    string name = $"Mountains_{cell.x}_{cell.y}";
                    EditorUtility.DisplayProgressBar("Create mountain terrain grid", name, index / (float)cells.Count);
                    var data = new TerrainData
                    {
                        name = name + "_TerrainData", heightmapResolution = Resolution,
                        size = new Vector3(TileSize, HeightRange, TileSize),
                        alphamapResolution = alphaResolution, baseMapResolution = 512,
                        terrainLayers = new[] { layer }
                    };
                    data.SetHeights(0, 0, heights);
                    data.SetAlphamaps(0, 0, alpha);
                    AssetDatabase.CreateAsset(data, DataPath(cell));
                    GameObject obj = Terrain.CreateTerrainGameObject(data);
                    obj.name = name;
                    obj.transform.SetParent(root.transform, false);
                    obj.transform.localPosition = new Vector3(cell.x * TileSize, 0f, cell.y * TileSize);
                    Terrain terrain = obj.GetComponent<Terrain>();
                    terrain.materialTemplate = material;
                    terrain.groupingID = OfficialPlainsTerrainBuilder.GroupingId;
                    terrain.allowAutoConnect = true;
                    terrain.heightmapPixelError = 2f;
                    terrain.basemapDistance = 2000f;
                    terrain.drawInstanced = true;
                    terrain.drawHeightmap = true;
                    terrain.Flush();
                    EditorUtility.SetDirty(data);
                    AssetDatabase.SaveAssetIfDirty(data);
                }

                OfficialPlainsTerrainBuilder.Reconnect(plainsRoot, plan);
                Validate();
                OfficialPlainsTerrainBuilder.Validate();
                EditorSceneManager.MarkSceneDirty(scene);
                Require(EditorSceneManager.SaveScene(scene), "Could not save the mountain terrain grid.");
                Frame();
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        [MenuItem("Animal Game/Official Map 01/Validate Mountains Terrain")]
        public static void Validate()
        {
            OfficialMapPlan plan = GetPlan();
            GameObject root = FindRoot(plan.gameObject.scene);
            Require(root != null, "Create the mountain terrain grid first.");
            Require(root.transform.parent == null && root.transform.position == plan.transform.position
                && root.transform.rotation == Quaternion.identity && root.transform.localScale == Vector3.one,
                "The mountain root must share the official map's metric origin.");
            GameObject plains = PlainsRoot(plan.gameObject.scene);
            var grid = OfficialPlainsTerrainBuilder.TileDictionary(plains, plan, false);
            var plainsCells = new HashSet<Vector2Int>(plains.GetComponentsInChildren<Terrain>(true)
                .Select(terrain => CellOf(terrain, plan)));
            var expected = new HashSet<Vector2Int>(MountainCells(plan).Where(cell => !plainsCells.Contains(cell)));
            Terrain[] mountains = root.GetComponentsInChildren<Terrain>(true);
            Require(mountains.Length == expected.Count, "Mountain terrain count does not match its unoccupied planning cells.");
            var assets = new HashSet<TerrainData>();
            foreach (Terrain terrain in mountains)
            {
                Vector2Int cell = CellOf(terrain, plan);
                TerrainData data = terrain.terrainData;
                Require(expected.Remove(cell) && grid[cell] == terrain, "A mountain terrain occupies an unexpected or duplicate cell.");
                Require(terrain.enabled && terrain.gameObject.activeInHierarchy && terrain.drawHeightmap
                    && terrain.transform.parent == root.transform && terrain.transform.localRotation == Quaternion.identity
                    && terrain.transform.localScale == Vector3.one
                    && terrain.transform.localPosition == new Vector3(cell.x * TileSize, 0f, cell.y * TileSize),
                    terrain.name + ": terrain transform or visibility is invalid.");
                Require(data != null && assets.Add(data) && data.heightmapResolution == Resolution
                    && data.size == new Vector3(TileSize, HeightRange, TileSize)
                    && AssetDatabase.GetAssetPath(data) == DataPath(cell),
                    terrain.name + ": expected independent 250 x 350 x 250 m terrain data with 2049 samples.");
                TerrainCollider collider = terrain.GetComponent<TerrainCollider>();
                Require(collider != null && collider.enabled && collider.terrainData == data,
                    terrain.name + ": collider must reference its terrain data.");
                Require(AssetDatabase.GetAssetPath(terrain.materialTemplate) == MaterialPath
                    && data.terrainLayers.Length > 0 && AssetDatabase.GetAssetPath(data.terrainLayers[0]) == LayerPath
                    && terrain.groupingID == OfficialPlainsTerrainBuilder.GroupingId && terrain.allowAutoConnect,
                    terrain.name + ": editing material, layer or neighbor group is invalid.");
            }
            foreach (var pair in grid)
            {
                Terrain terrain = pair.Value;
                Vector2Int cell = pair.Key;
                Require(terrain.leftNeighbor == At(grid, cell + Vector2Int.left)
                    && terrain.topNeighbor == At(grid, cell + Vector2Int.up)
                    && terrain.rightNeighbor == At(grid, cell + Vector2Int.right)
                    && terrain.bottomNeighbor == At(grid, cell + Vector2Int.down),
                    terrain.name + ": terrain neighbors are incomplete.");
            }
            Directory.CreateDirectory("Temp/OfficialMap01/Mountains");
            File.WriteAllText("Temp/OfficialMap01/Mountains/Validation.txt",
                $"PASS: {mountains.Length} mountain Terrain tiles, {plainsCells.Count} plains tiles, {grid.Count} occupied cells.\n"
                + $"250 x 250 m cells, 0..350 m height range, 2049 source samples, independent data and colliders.\n"
                + "Metric transforms, planning coverage and all cross-region terrain neighbors verified.\n");
            Debug.Log($"PASS: {mountains.Length} editable mountain terrain tiles.", root);
        }

        [MenuItem("Animal Game/Official Map 01/Frame Mountains Terrain")]
        public static void Frame()
        {
            OfficialMapPlan plan = GetPlan();
            GameObject root = FindRoot(plan.gameObject.scene);
            Require(root != null, "Create the mountain terrain grid first.");
            Selection.activeGameObject = root;
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null) return;
            Rect bounds = plan.FindRegion("mountains").BoundsMeters;
            view.in2DMode = false;
            view.LookAt(plan.CoordinateToWorld(bounds.center, InitialHeight), Quaternion.Euler(55f, 0f, 0f),
                Mathf.Max(bounds.width, bounds.height) * .75f, false);
            view.Repaint();
        }

        private static OfficialMapPlan GetPlan()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play before editing the mountain grid.");
            OfficialMapPlan plan = OfficialMap01PlanBuilder.FindPlan();
            Require(plan.HasMetricTransform(out string issue), issue);
            return plan;
        }

        private static List<Vector2Int> MountainCells(OfficialMapPlan plan) =>
            OfficialPlainsTerrainBuilder.IntersectingCells(plan.FindRegion("mountains"));

        private static GameObject PlainsRoot(Scene scene) =>
            scene.GetRootGameObjects().Single(root => root.name == OfficialPlainsTerrainBuilder.RootName);

        private static GameObject FindRoot(Scene scene) => scene.GetRootGameObjects().SingleOrDefault(root => root.name == RootName);

        private static Vector2Int CellOf(Terrain terrain, OfficialMapPlan plan)
        {
            Vector3 position = terrain.transform.position - plan.transform.position;
            return new Vector2Int(Mathf.RoundToInt(position.x / TileSize), Mathf.RoundToInt(position.z / TileSize));
        }

        private static Terrain At(Dictionary<Vector2Int, Terrain> grid, Vector2Int cell) =>
            grid.TryGetValue(cell, out Terrain terrain) ? terrain : null;

        private static string DataPath(Vector2Int cell) => $"{DataFolder}/Mountains_{cell.x}_{cell.y}_TerrainData.asset";

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
