using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace AnimalGame.Editor
{
    /// <summary>Creates a saved flat editing surface once, without replacing authored heights.</summary>
    public static class OfficialPlainsTerrainBuilder
    {
        public const string RootName = "Official Plains - Editable Terrain";
        public const string Folder = "Assets/Maps/Official_Map_01/Plains";
        public const float TileSizeMeters = 250f;
        public const float HeightRangeMeters = 350f;
        public const float InitialHeightMeters = 80f;
        public const int TileResolution = 2049;
        public const int SingleResolution = 4097;
        public const int GroupingId = 2001;
        private const string DataFolder = Folder + "/data";
        private const string MaterialPath = Folder + "/PlainsTerrain.mat";
        private const string LayerPath = Folder + "/Grass.terrainlayer";
        private const string ReportPath = "Temp/OfficialMap01/Terrain/Validation.txt";

        [MenuItem("Animal Game/Official Map 01/Create Flat Plains Terrain")]
        private static void CreateMenu() => Create();

        public static void Create(bool singleTerrain = false)
        {
            Scene scene = RequireScene();
            OfficialMapPlan plan = OfficialMap01PlanBuilder.FindPlan();
            Require(plan.HasMetricTransform(out string transformIssue), transformIssue);
            OfficialMapRegion plains = RequirePlains(plan);
            GameObject existing = FindRoot(scene);
            if (existing != null)
            {
                Reconnect(existing, plan);
                Validate();
                Frame();
                return;
            }

            List<Vector2Int> cells = IntersectingCells(plains);
            Require(cells.Count > 0, "The plains outline intersects no 250 m cells.");
            Require(!AssetDatabase.IsValidFolder(DataFolder)
                || AssetDatabase.FindAssets("t:TerrainData", new[] { DataFolder }).Length == 0,
                "Saved plains TerrainData already exists without its scene root. Restore the existing objects instead of overwriting their data.");
            Require(!File.Exists(MaterialPath) && !File.Exists(LayerPath),
                "Plains material or layer assets already exist. No existing assets are overwritten.");
            string sourceMaterial = "Assets/Maps/TerrainPrototype/PrototypeTerrain.mat";
            string sourceLayer = "Assets/Maps/TerrainPrototype/Grass.terrainlayer";
            Require(AssetDatabase.LoadAssetAtPath<Material>(sourceMaterial) != null
                && AssetDatabase.LoadAssetAtPath<TerrainLayer>(sourceLayer) != null,
                "The existing prototype terrain material or grass layer is missing.");
            int resolution = singleTerrain ? SingleResolution : TileResolution;
            Rect bounds = plains.BoundsMeters;
            int count = singleTerrain ? 1 : cells.Count;
            for (int index = 0; index < count; index++)
                Require(!File.Exists(DataPath(singleTerrain, cells[index])), "A plains TerrainData path is already occupied.");

            EnsureFolder(DataFolder);
            Require(AssetDatabase.CopyAsset(sourceMaterial, MaterialPath), "Could not copy the plains terrain material.");
            Require(AssetDatabase.CopyAsset(sourceLayer, LayerPath), "Could not copy the plains grass layer.");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            TerrainLayer grass = AssetDatabase.LoadAssetAtPath<TerrainLayer>(LayerPath);
            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.position = plan.transform.position;
            Undo.RegisterCreatedObjectUndo(root, "Create flat official plains terrain");

            var heights = new float[resolution, resolution];
            float normalizedHeight = InitialHeightMeters / HeightRangeMeters;
            for (int row = 0; row < resolution; row++)
                for (int column = 0; column < resolution; column++)
                    heights[row, column] = normalizedHeight;
            const int alphaResolution = 256;
            var alpha = new float[alphaResolution, alphaResolution, 1];
            for (int row = 0; row < alphaResolution; row++)
                for (int column = 0; column < alphaResolution; column++)
                    alpha[row, column, 0] = 1f;

            try
            {
                for (int index = 0; index < count; index++)
                {
                    Vector2Int cell = cells[index];
                    string name = singleTerrain ? "Plains Terrain" : $"Plains_{cell.x}_{cell.y}";
                    EditorUtility.DisplayProgressBar("Create Official Plains Terrain",
                        $"{index + 1}/{count}: {name}", index / (float)count);
                    var data = new TerrainData
                    {
                        name = name + "_TerrainData",
                        heightmapResolution = resolution,
                        size = new Vector3(singleTerrain ? bounds.width : TileSizeMeters,
                            HeightRangeMeters, singleTerrain ? bounds.height : TileSizeMeters),
                        alphamapResolution = alphaResolution,
                        baseMapResolution = 512,
                        terrainLayers = new[] { grass }
                    };
                    data.SetHeights(0, 0, heights);
                    data.SetAlphamaps(0, 0, alpha);
                    AssetDatabase.CreateAsset(data, DataPath(singleTerrain, cell));
                    GameObject obj = Terrain.CreateTerrainGameObject(data);
                    obj.name = name;
                    obj.transform.SetParent(root.transform, false);
                    obj.transform.localPosition = new Vector3(singleTerrain ? bounds.xMin : cell.x * TileSizeMeters,
                        0f, singleTerrain ? bounds.yMin : cell.y * TileSizeMeters);
                    Terrain terrain = obj.GetComponent<Terrain>();
                    terrain.materialTemplate = material;
                    terrain.groupingID = GroupingId;
                    terrain.allowAutoConnect = true;
                    terrain.heightmapPixelError = 2f;
                    terrain.basemapDistance = 2000f;
                    terrain.drawInstanced = true;
                    terrain.drawHeightmap = true;
                    terrain.Flush();
                    Require(Mathf.Abs(data.GetInterpolatedHeight(0.5f, 0.5f) - InitialHeightMeters) < 0.02f,
                        name + ": initial flat height was not stored correctly.");
                    EditorUtility.SetDirty(data);
                    AssetDatabase.SaveAssetIfDirty(data);
                }

                Reconnect(root, plan);
                AddEditingLight(root);
                AssetDatabase.SaveAssetIfDirty(material);
                AssetDatabase.SaveAssetIfDirty(grass);
                EditorSceneManager.MarkSceneDirty(scene);
                Validate();
                Require(EditorSceneManager.SaveScene(scene), "Could not save Official_Map_01.");
                Frame();
                Debug.Log($"Official plains ready: {count} flat editable Terrain object(s), initial height {InitialHeightMeters} m. "
                    + "Existing region outlines and height/area references remain in place.", root);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem("Animal Game/Official Map 01/Validate Plains Terrain")]
        public static void Validate()
        {
            Scene scene = RequireScene();
            OfficialMapPlan plan = OfficialMap01PlanBuilder.FindPlan();
            Require(plan.HasMetricTransform(out string transformIssue), transformIssue);
            OfficialMapRegion plains = RequirePlains(plan);
            GameObject root = FindRoot(scene);
            Require(root != null, "Create the plains terrain first.");
            Require(root.transform.parent == null && root.transform.position == plan.transform.position
                && root.transform.rotation == Quaternion.identity && root.transform.localScale == Vector3.one,
                "The plains terrain root must share the plan's metric origin with upright unit scale.");
            Terrain[] terrains = root.GetComponentsInChildren<Terrain>(true);
            bool single = terrains.Length == 1 && terrains[0].name == "Plains Terrain";
            List<Vector2Int> cells = IntersectingCells(plains);
            Require(terrains.Length == (single ? 1 : cells.Count), "The terrain count does not match the plains outline.");
            var byCell = TileDictionary(root, plan, single);
            var assets = new HashSet<TerrainData>();
            Rect bounds = plains.BoundsMeters;
            double terrainArea = 0d;
            var report = new StringBuilder("PASS: Official_Map_01 editable plains terrain\n");
            report.AppendLine("terrainCount: " + terrains.Length);
            report.AppendLine("mode: " + (single ? "single rectangular terrain" : "connected 250 m tiles intersecting the plains polygon"));
            report.AppendLine($"sourceResolution: {(single ? SingleResolution : TileResolution)}");
            report.AppendLine($"heightRangeMeters: 0..{HeightRangeMeters:F0}; initialFlatHeightMeters: {InitialHeightMeters:F0} (creation only; authored heights are retained)");
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "plainsBoundsMeters: X={0:F4}..{1:F4}, Z={2:F4}..{3:F4}", bounds.xMin, bounds.xMax, bounds.yMin, bounds.yMax));
            foreach (Terrain terrain in terrains)
            {
                TerrainData data = terrain.terrainData;
                Require(terrain.enabled && terrain.gameObject.activeInHierarchy && terrain.drawHeightmap,
                    terrain.name + ": terrain is not enabled and visible.");
                Require(data != null && assets.Add(data), "Each terrain must have a separate saved TerrainData asset.");
                Require(data.heightmapResolution == (single ? SingleResolution : TileResolution)
                    && Mathf.Abs(data.size.x - (single ? bounds.width : TileSizeMeters)) < 0.01f
                    && Mathf.Abs(data.size.z - (single ? bounds.height : TileSizeMeters)) < 0.01f
                    && data.size.y == HeightRangeMeters, terrain.name + ": dimensions or heightmap resolution changed.");
                Require(terrain.transform.parent == root.transform && terrain.transform.localRotation == Quaternion.identity
                    && terrain.transform.localScale == Vector3.one && terrain.transform.localPosition.y == 0f,
                    terrain.name + ": preserve upright unit scale and the Y=0 absolute height origin.");
                TerrainCollider collider = terrain.GetComponent<TerrainCollider>();
                Require(collider != null && collider.enabled && collider.terrainData == data,
                    terrain.name + ": enabled TerrainCollider must reference the same TerrainData.");
                Require(terrain.materialTemplate != null && AssetDatabase.GetAssetPath(terrain.materialTemplate) == MaterialPath
                    && data.terrainLayers.Length > 0 && AssetDatabase.GetAssetPath(data.terrainLayers[0]) == LayerPath,
                    terrain.name + ": independent plains material and grass layer are missing.");
                Require(terrain.groupingID == GroupingId && terrain.allowAutoConnect,
                    terrain.name + ": neighbor grouping or auto-connect is disabled.");
                Vector2Int key = CellOf(terrain, plan);
                Require(AssetDatabase.GetAssetPath(data) == DataPath(single, key), terrain.name + ": unexpected TerrainData path.");
                if (single)
                {
                    Require((terrain.transform.localPosition - new Vector3(bounds.xMin, 0f, bounds.yMin)).sqrMagnitude < 0.0001f,
                        "The single terrain does not cover the plains bounds.");
                    Require(terrain.leftNeighbor == null && terrain.rightNeighbor == null
                        && terrain.topNeighbor == null && terrain.bottomNeighbor == null, "The single terrain has unexpected neighbors.");
                }
                else
                {
                    Require(cells.Contains(key) && (terrain.transform.localPosition
                        - new Vector3(key.x * TileSizeMeters, 0f, key.y * TileSizeMeters)).sqrMagnitude < 0.0001f,
                        terrain.name + ": tile is outside its planned 250 m cell.");
                    Require(terrain.leftNeighbor == At(byCell, key + Vector2Int.left)
                        && terrain.rightNeighbor == At(byCell, key + Vector2Int.right)
                        && terrain.topNeighbor == At(byCell, key + Vector2Int.up)
                        && terrain.bottomNeighbor == At(byCell, key + Vector2Int.down), terrain.name + ": neighbor links are incomplete.");
                }
                terrainArea += (double)data.size.x * data.size.z;
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0}: size={1:F3}x{2:F3}m; sampleHeightsMeters={3:F4},{4:F4},{5:F4}",
                    terrain.name, data.size.x, data.size.z, data.GetInterpolatedHeight(0f, 0f),
                    data.GetInterpolatedHeight(0.5f, 0.5f), data.GetInterpolatedHeight(1f, 1f)));
            }
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "plainsPolygonAreaM2: {0:F6}; terrainCoverageAreaM2: {1:F6}; boundaryBufferAreaM2: {2:F6}",
                plains.AreaSquareMeters, terrainArea, terrainArea - plains.AreaSquareMeters));
            report.AppendLine("The 2.25 km2 target measures the region polygon, equivalent to 36 V1 tile areas; full rectangular edge tiles also cover boundary buffers.");
            report.AppendLine("Each Unity unit is one metre. Validation reads the current authored heights; it does not generate terrain, other biomes, heightmap exports, or prebakes.");
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, report.ToString());
            Debug.Log(report.ToString(), root);
        }

        [MenuItem("Animal Game/Official Map 01/Frame Plains Terrain")]
        public static void Frame()
        {
            Scene scene = RequireScene();
            OfficialMapPlan plan = OfficialMap01PlanBuilder.FindPlan();
            OfficialMapRegion plains = RequirePlains(plan);
            GameObject root = FindRoot(scene);
            Require(root != null, "Create the plains terrain first.");
            Vector3 label = plan.CoordinateToWorld(plains.LabelPositionMeters, InitialHeightMeters);
            Terrain selected = root.GetComponentsInChildren<Terrain>(true)
                .OrderBy(terrain => (terrain.transform.position + new Vector3(terrain.terrainData.size.x * 0.5f,
                    InitialHeightMeters, terrain.terrainData.size.z * 0.5f) - label).sqrMagnitude).FirstOrDefault();
            Require(selected != null, "The plains root has no Terrain objects.");
            Selection.activeGameObject = selected.gameObject;
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null) return;
            Rect bounds = plains.BoundsMeters;
            view.in2DMode = false;
            view.LookAt(plan.CoordinateToWorld(bounds.center, InitialHeightMeters), Quaternion.Euler(55f, -20f, 0f),
                Mathf.Max(bounds.width, bounds.height) * 0.75f, false);
            view.Repaint();
        }

        private static void AddEditingLight(GameObject root)
        {
            var sunObject = new GameObject("Plains Editing Sun", typeof(Light), typeof(UniversalAdditionalLightData));
            sunObject.transform.SetParent(root.transform, false);
            sunObject.transform.localRotation = Quaternion.Euler(48f, -35f, 0f);
            Light sun = sunObject.GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.97f, 0.9f);
            sun.intensity = 1.1f;
            sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.52f, 0.59f, 0.64f);
            RenderSettings.ambientEquatorColor = new Color(0.36f, 0.4f, 0.33f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.24f, 0.19f);
        }

        private static void Reconnect(GameObject root, OfficialMapPlan plan)
        {
            Terrain[] terrains = root.GetComponentsInChildren<Terrain>(true);
            bool single = terrains.Length == 1 && terrains[0].name == "Plains Terrain";
            Dictionary<Vector2Int, Terrain> byCell = TileDictionary(root, plan, single);
            foreach (Terrain terrain in terrains)
            {
                Vector2Int cell = CellOf(terrain, plan);
                terrain.SetNeighbors(single ? null : At(byCell, cell + Vector2Int.left),
                    single ? null : At(byCell, cell + Vector2Int.up),
                    single ? null : At(byCell, cell + Vector2Int.right),
                    single ? null : At(byCell, cell + Vector2Int.down));
                terrain.Flush();
            }
        }

        private static Dictionary<Vector2Int, Terrain> TileDictionary(GameObject root, OfficialMapPlan plan, bool single)
        {
            var result = new Dictionary<Vector2Int, Terrain>();
            foreach (Terrain terrain in root.GetComponentsInChildren<Terrain>(true))
            {
                Vector2Int cell = single ? Vector2Int.zero : CellOf(terrain, plan);
                Require(!result.ContainsKey(cell), "Multiple plains Terrain objects occupy one cell.");
                result.Add(cell, terrain);
            }
            return result;
        }

        private static Vector2Int CellOf(Terrain terrain, OfficialMapPlan plan)
        {
            Vector3 position = terrain.transform.position - plan.transform.position;
            return new Vector2Int(Mathf.RoundToInt(position.x / TileSizeMeters), Mathf.RoundToInt(position.z / TileSizeMeters));
        }

        private static Terrain At(Dictionary<Vector2Int, Terrain> terrains, Vector2Int cell)
            => terrains.TryGetValue(cell, out Terrain terrain) ? terrain : null;

        private static List<Vector2Int> IntersectingCells(OfficialMapRegion plains)
        {
            Rect bounds = plains.BoundsMeters;
            var result = new List<Vector2Int>();
            for (int z = Mathf.FloorToInt(bounds.yMin / TileSizeMeters); z < Mathf.CeilToInt(bounds.yMax / TileSizeMeters); z++)
                for (int x = Mathf.FloorToInt(bounds.xMin / TileSizeMeters); x < Mathf.CeilToInt(bounds.xMax / TileSizeMeters); x++)
                {
                    var clipped = new List<Vector2>(plains.PointsMeters);
                    clipped = Clip(clipped, true, x * TileSizeMeters, true);
                    clipped = Clip(clipped, true, (x + 1) * TileSizeMeters, false);
                    clipped = Clip(clipped, false, z * TileSizeMeters, true);
                    clipped = Clip(clipped, false, (z + 1) * TileSizeMeters, false);
                    if (OfficialMapPlan.PolygonArea(clipped.ToArray()) > 0.01d) result.Add(new Vector2Int(x, z));
                }
            return result;
        }

        private static List<Vector2> Clip(List<Vector2> points, bool xAxis, float limit, bool greater)
        {
            var result = new List<Vector2>();
            if (points.Count == 0) return result;
            Vector2 previous = points[points.Count - 1];
            float previousValue = xAxis ? previous.x : previous.y;
            bool previousInside = greater ? previousValue >= limit : previousValue <= limit;
            foreach (Vector2 current in points)
            {
                float currentValue = xAxis ? current.x : current.y;
                bool currentInside = greater ? currentValue >= limit : currentValue <= limit;
                if (currentInside != previousInside)
                {
                    float amount = (limit - previousValue) / (currentValue - previousValue);
                    Vector2 intersection = previous + (current - previous) * amount;
                    if (xAxis) intersection.x = limit; else intersection.y = limit;
                    result.Add(intersection);
                }
                if (currentInside) result.Add(current);
                previous = current;
                previousValue = currentValue;
                previousInside = currentInside;
            }
            return result;
        }

        private static OfficialMapRegion RequirePlains(OfficialMapPlan plan)
        {
            OfficialMapRegion plains = plan.FindRegion("plains");
            Require(plains != null && plains.PointsMeters != null && plains.PointsMeters.Length >= 3
                && plains.AreaSquareMeters > 1d, "The official plains region outline is missing.");
            return plains;
        }

        private static Scene RequireScene()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode
                && SceneManager.GetActiveScene().path == OfficialMap01PlanBuilder.ScenePath,
                "Open Assets/Scenes/GameMaps/Official_Map_01.unity in Edit Mode first.");
            return SceneManager.GetActiveScene();
        }

        private static GameObject FindRoot(Scene scene)
        {
            GameObject[] matches = scene.GetRootGameObjects().Where(root => root.name == RootName).ToArray();
            Require(matches.Length <= 1, "Multiple official plains terrain roots exist.");
            return matches.SingleOrDefault();
        }

        private static string DataPath(bool single, Vector2Int cell)
            => single ? DataFolder + "/Plains_TerrainData.asset" : $"{DataFolder}/Plains_{cell.x}_{cell.y}_TerrainData.asset";

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
