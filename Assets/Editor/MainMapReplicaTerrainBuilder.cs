using System;
using System.IO;
using System.Linq;
using System.Text;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AnimalGame.Editor
{
    /// <summary>One-time conversion of the main map's physical surface into a sculptable Terrain.</summary>
    internal static class MainMapReplicaTerrainBuilder
    {
        internal const string Folder = "Assets/Maps/MainMapReplica";
        internal const string DataPath = Folder + "/MainMapReplicaTerrainData.asset";
        private const string LevelPath = "Assets/Maps/MainHeightMapLevel.asset";
        private const string TerrainName = "Main Map Replica Terrain";
        private const string ReportPath = "Temp/MainMapReplica/Validation.txt";
        private const int Resolution = 2049;

        [MenuItem("Animal Game/Main Map Replica/Create Terrain in 3DMapTestScene")]
        internal static void Create()
        {
            Scene scene = RequireScene();
            Require(!File.Exists(DataPath), "Replica TerrainData already exists. Sculpt it; creation never overwrites it.");
            Terrain[] existing = SceneTerrains(scene);
            Require(!existing.Any(t => t.name == TerrainName), "Replica Terrain already exists in the scene.");
            var level = AssetDatabase.LoadAssetAtPath<HeightMapLevelAsset>(LevelPath);
            Require(level != null && level.IsValid && level.HeightMap.isReadable, "Main map source is missing or unreadable.");
            Require(level.MapSizeMeters == new Vector2(250, 250) && level.MinimumHeightMeters == 0
                && level.MaximumHeightMeters == 70 && level.BakedHeightResolution == 2048,
                "The main map dimensions changed; review the planned Terrain parameters before generating.");

            try
            {
                EditorUtility.DisplayProgressBar("Main map Terrain", "Reconstructing the current map surface...", 0.15f);
                using (BakedHeightField field = BakeSource(level))
                {
                    var heights = new float[Resolution, Resolution];
                    for (int z = 0; z < Resolution; z++)
                        for (int x = 0; x < Resolution; x++)
                            heights[z, x] = (field.SampleSurfaceHeight(new Vector2(x / 2048f, z / 2048f))
                                - level.MinimumHeightMeters) / (level.MaximumHeightMeters - level.MinimumHeightMeters);

                    EditorUtility.DisplayProgressBar("Main map Terrain", "Creating independent TerrainData...", 0.65f);
                    if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Maps", "MainMapReplica");
                    var data = new TerrainData
                    {
                        name = "MainMapReplicaTerrainData",
                        heightmapResolution = Resolution,
                        size = new Vector3(level.MapSizeMeters.x,
                            level.MaximumHeightMeters - level.MinimumHeightMeters, level.MapSizeMeters.y)
                    };
                    data.SetHeights(0, 0, heights);
                    AssetDatabase.CreateAsset(data, DataPath);
                    GameObject created = Terrain.CreateTerrainGameObject(data);
                    created.name = TerrainName;
                    if (created.scene != scene) SceneManager.MoveGameObjectToScene(created, scene);
                    created.transform.position = new Vector3(0, level.MinimumHeightMeters, 0);
                    Undo.RegisterCreatedObjectUndo(created, "Create main map Terrain replica");
                    Terrain terrain = created.GetComponent<Terrain>();

                    // Preserve the user's earlier Terrain and all of its data. No
                    // camera, lighting, material or Scene View setup belongs here.
                    foreach (Terrain previous in existing)
                    {
                        if (!previous.gameObject.activeSelf) continue;
                        Undo.RecordObject(previous.gameObject, "Disable previous test Terrain");
                        previous.gameObject.SetActive(false);
                    }
                    EditorUtility.SetDirty(data);
                    EditorSceneManager.MarkSceneDirty(scene);
                    AssetDatabase.SaveAssets();
                    Require(EditorSceneManager.SaveScene(scene), "Could not save the Terrain scene.");
                    ValidateAgainstSource(terrain, level, field);
                    Selection.activeGameObject = created;
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        [MenuItem("Animal Game/Main Map Replica/Validate Terrain Against Main Map")]
        internal static void Validate()
        {
            Scene scene = RequireScene();
            Terrain terrain = SceneTerrains(scene).SingleOrDefault(t => t.name == TerrainName);
            Require(terrain != null, "The main map Terrain replica is missing.");
            var level = AssetDatabase.LoadAssetAtPath<HeightMapLevelAsset>(LevelPath);
            using (BakedHeightField field = BakeSource(level)) ValidateAgainstSource(terrain, level, field);
        }

        private static BakedHeightField BakeSource(HeightMapLevelAsset level) => BakedHeightField.Bake(
            level.HeightMap, level.BakedHeightResolution, level.MapSizeMeters,
            level.MinimumHeightMeters, level.MaximumHeightMeters, level.NormalizeSourceRange,
            level.SurfaceSmoothingSigmaMeters, level.DetailSmoothingSigmaMeters,
            level.UseHeightMapBorderMask, level.HeightMapBorderMaskThreshold,
            level.HeightMapBorderInsetMeters, level.PlayableAreaMask);

        private static void ValidateAgainstSource(Terrain terrain, HeightMapLevelAsset level, BakedHeightField field)
        {
            TerrainData data = terrain.terrainData;
            Require(terrain.enabled && terrain.gameObject.activeInHierarchy && terrain.drawHeightmap, "Replica is not enabled.");
            Require(AssetDatabase.GetAssetPath(data) == DataPath && data.heightmapResolution == Resolution
                && data.size == new Vector3(250, 70, 250), "Terrain data or dimensions are incorrect.");
            var collider = terrain.GetComponent<TerrainCollider>();
            Require(collider != null && collider.enabled && collider.terrainData == data, "Collider data differs from Terrain data.");
            Require(terrain.transform.position == Vector3.zero && terrain.transform.lossyScale == Vector3.one
                && terrain.transform.rotation == Quaternion.identity, "Terrain coordinates changed.");
            float[,] actual = data.GetHeights(0, 0, Resolution, Resolution);
            float maxError = 0, minimum = float.MaxValue, maximum = float.MinValue;
            Vector2Int lowPoint = default, highPoint = default;
            double sumSquared = 0;
            for (int z = 0; z < Resolution; z++)
                for (int x = 0; x < Resolution; x++)
                {
                    float h = actual[z, x] * data.size.y;
                    float error = Mathf.Abs(h - field.SampleSurfaceHeight(new Vector2(x / 2048f, z / 2048f)));
                    maxError = Mathf.Max(maxError, error);
                    sumSquared += error * error;
                    if (h < minimum) { minimum = h; lowPoint = new Vector2Int(x, z); }
                    if (h > maximum) { maximum = h; highPoint = new Vector2Int(x, z); }
                }
            Require(maxError < 0.005f, "Replica differs from the current source surface; inspect edits or regenerate separately.");

            float maxSlopeError = 0;
            double sourceSlopeSum = 0, terrainSlopeSum = 0;
            const int slopeSamplesPerAxis = 64;
            // Compare grades over one meter in both directions, rather than the
            // editor's differently filtered GetSteepness calculation.
            float d = 0.5f / 250f;
            for (int z = 1; z <= slopeSamplesPerAxis; z++)
                for (int x = 1; x <= slopeSamplesPerAxis; x++)
                {
                    Vector2 uv = new Vector2(x / 65f, z / 65f);
                    float sx = field.SampleSurfaceHeight(uv + new Vector2(d, 0)) - field.SampleSurfaceHeight(uv - new Vector2(d, 0));
                    float sz = field.SampleSurfaceHeight(uv + new Vector2(0, d)) - field.SampleSurfaceHeight(uv - new Vector2(0, d));
                    float tx = data.GetInterpolatedHeight(uv.x + d, uv.y) - data.GetInterpolatedHeight(uv.x - d, uv.y);
                    float tz = data.GetInterpolatedHeight(uv.x, uv.y + d) - data.GetInterpolatedHeight(uv.x, uv.y - d);
                    float sourceSlope = Mathf.Atan(new Vector2(sx, sz).magnitude) * Mathf.Rad2Deg;
                    float terrainSlope = Mathf.Atan(new Vector2(tx, tz).magnitude) * Mathf.Rad2Deg;
                    maxSlopeError = Mathf.Max(maxSlopeError, Mathf.Abs(sourceSlope - terrainSlope));
                    sourceSlopeSum += sourceSlope;
                    terrainSlopeSum += terrainSlope;
                }
            Require(maxSlopeError < 0.5f, "Slope comparison exceeded tolerance.");
            var report = new StringBuilder();
            report.AppendLine("PASS: Main Map Replica Terrain");
            report.AppendLine($"Source: {AssetDatabase.GetAssetPath(level.HeightMap)}; current MainHeightMapLevel settings");
            report.AppendLine($"Normalize={level.NormalizeSourceRange}; surface sigma={level.SurfaceSmoothingSigmaMeters}m; source gray range={field.SourceMinimum:R}..{field.SourceMaximum:R}");
            report.AppendLine("Terrain: 250 x 250 m, height range 0..70 m, 2049 x 2049, spacing 0.1220703125 m; collider shares data.");
            report.AppendLine($"Checked all {Resolution * Resolution} height samples, including orientation. Maximum error={maxError:F6}m; RMS={Math.Sqrt(sumSquared / (Resolution * Resolution)):F6}m.");
            report.AppendLine($"Actual elevation range after smoothing: {minimum:F4}..{maximum:F4}m.");
            report.AppendLine($"Lowest point (X,Z)=({lowPoint.x * 250f / 2048:F3},{lowPoint.y * 250f / 2048:F3}); highest point=({highPoint.x * 250f / 2048:F3},{highPoint.y * 250f / 2048:F3}).");
            report.AppendLine($"4096 one-meter slope probes: mean source={sourceSlopeSum / 4096:F4}deg; mean Terrain={terrainSlopeSum / 4096:F4}deg; maximum difference={maxSlopeError:F4}deg.");
            if (level.PrebakedHeightField != null && level.PrebakedHeightField.Matches(level))
                using (BakedHeightField cached = BakedHeightField.FromPrebakedData(level.PrebakedHeightField))
                {
                    float cacheError = 0;
                    for (int z = 0; z < field.Height; z++)
                        for (int x = 0; x < field.Width; x++)
                            cacheError = Mathf.Max(cacheError, Mathf.Abs(field.GetSurfaceHeightSample(x, z) - cached.GetSurfaceHeightSample(x, z)));
                    report.AppendLine($"Fresh source versus existing main-map runtime prebake: maximum difference={cacheError:F6}m.");
                }
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, report.ToString());
            Debug.Log(report.ToString(), terrain);
        }

        private static Scene RequireScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && scene.path == TerrainPrototypeBuilder.ScenePath
                && SceneManager.sceneCount == 1, "Open 3DMapTestScene by itself in Edit Mode.");
            return scene;
        }

        private static Terrain[] SceneTerrains(Scene scene) => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Terrain>(true)).ToArray();

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
