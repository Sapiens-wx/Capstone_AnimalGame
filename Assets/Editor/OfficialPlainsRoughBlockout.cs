using System;
using System.Globalization;
using System.IO;
using System.Linq;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AnimalGame.Editor
{
    /// <summary>Applies the first grassland blockout once and preserves later hand sculpting.</summary>
    public static class OfficialPlainsRoughBlockout
    {
        public const string ManifestPath = OfficialPlainsTerrainBuilder.Folder + "/RoughBlockout.json";
        public const string ReportFolder = "Temp/OfficialMap01/RoughBlockout";
        public const string BackupFolder = "LocalBackups/OfficialMap01/RoughBlockout";

        [MenuItem("Animal Game/Official Map 01/Create or Show Plains Rough Blockout")]
        public static void CreateOrShow()
        {
            OfficialMapPlan plan = OfficialMap01PlanBuilder.FindPlan();
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play Mode before sculpting the official map.");
            Terrain[] terrains = GetTerrains(plan);
            if (File.Exists(ManifestPath))
            {
                // Opening the tool never replaces any subsequent hand sculpting.
                Frame();
                return;
            }
            Require(!File.Exists(BackupFolder + "/InProgress.json"),
                "An interrupted blockout has a protected backup in " + BackupFolder + ". Inspect that backup before applying another blockout.");
            OfficialPlainsTerrainBuilder.Validate();
            OfficialMapRegion plains = plan.FindRegion("plains");
            var field = new OfficialPlainsRoughHeightField(plains);
            Directory.CreateDirectory(ReportFolder);
            try
            {
                field.Build((progress, message) => Progress("Build grassland landforms", message, progress * 0.25f));
                File.WriteAllText(ReportFolder + "/HeightField.txt", field.Report);
                string backup = Backup(plan, terrains);
                File.WriteAllText(BackupFolder + "/InProgress.json", JsonUtility.ToJson(new Record
                    { version = 1, backupFolder = backup, generatedUtc = DateTime.UtcNow.ToString("O") }, true));
                var heights = new float[OfficialPlainsTerrainBuilder.TileResolution, OfficialPlainsTerrainBuilder.TileResolution];
                for (int index = 0; index < terrains.Length; index++)
                {
                    Terrain terrain = terrains[index];
                    TerrainData data = terrain.terrainData;
                    int resolution = data.heightmapResolution;
                    Vector3 tileOrigin = terrain.transform.position - plan.transform.position;
                    float stepX = data.size.x / (resolution - 1);
                    float stepZ = data.size.z / (resolution - 1);
                    Progress("Sculpt continuous plains", $"{index + 1}/{terrains.Length}: {terrain.name}",
                        0.25f + 0.62f * index / terrains.Length);
                    for (int row = 0; row < resolution; row++)
                    {
                        float z = tileOrigin.z + row * stepZ;
                        for (int column = 0; column < resolution; column++)
                        {
                            float height = field.Sample(tileOrigin.x + column * stepX, z) - tileOrigin.y;
                            Require(height >= 0f && height <= data.size.y, "A generated height exceeds the fixed TerrainData encoding range.");
                            heights[row, column] = height / data.size.y;
                        }
                    }
                    data.SetHeights(0, 0, heights);
                    terrain.Flush();
                    EditorUtility.SetDirty(data);
                    AssetDatabase.SaveAssetIfDirty(data);
                }
                Progress("Validate sculpted plains", "Checking native heights, seams and 7 m contour silhouettes", 0.9f);
                string contours = field.ValidateContourFeatures((x, z) => OfficialPlainsRoughDiagnostics.SampleHeight(terrains, plan, x, z));
                File.WriteAllText(ReportFolder + "/NativeSmallHillContours.txt", contours);
                string nativeReport = OfficialPlainsRoughDiagnostics.Validate(terrains, plan, ReportFolder);
                OfficialMapPlanHeightAlignment.AlignToPlains(plan);
                OfficialMap01PlanBuilder.Validate(plan);
                OfficialPlainsTerrainBuilder.Validate();
                Require(EditorSceneManager.SaveScene(plan.gameObject.scene), "Could not save the official map scene.");
                var record = new Record
                {
                    version = 1,
                    generatedUtc = DateTime.UtcNow.ToString("O"),
                    backupFolder = backup,
                    tileCount = terrains.Length,
                    contourIntervalMeters = 7f,
                    minimumEncodingHeightMeters = 0f,
                    maximumEncodingHeightMeters = OfficialPlainsTerrainBuilder.HeightRangeMeters,
                    sourceHeightmapResolution = OfficialPlainsTerrainBuilder.TileResolution,
                    heightFieldReport = field.Report,
                    contourVerification = contours,
                    nativeVerification = nativeReport
                };
                File.WriteAllText(ManifestPath, JsonUtility.ToJson(record, true));
                AssetDatabase.ImportAsset(ManifestPath);
                File.Delete(BackupFolder + "/InProgress.json");
                Frame();
                Debug.Log("Official plains rough blockout saved. " + nativeReport
                    + "\nOriginal sources backed up at " + backup, plan);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem("Animal Game/Official Map 01/Frame Plains Rough Blockout")]
        public static void Frame()
        {
            OfficialMapPlan plan = OfficialMap01PlanBuilder.FindPlan();
            Terrain[] terrains = GetTerrains(plan);
            Rect bounds = plan.FindRegion("plains").BoundsMeters;
            Vector2 center = bounds.center;
            float height = OfficialPlainsRoughDiagnostics.SampleHeight(terrains, plan, center.x, center.y);
            Terrain selected = terrains.OrderBy(terrain =>
                (terrain.transform.position + new Vector3(125f, 0f, 125f) - plan.CoordinateToWorld(center)).sqrMagnitude).First();
            Selection.activeGameObject = selected.gameObject;
            SceneView view = SceneView.lastActiveSceneView ?? EditorWindow.GetWindow<SceneView>();
            view.in2DMode = false;
            view.LookAt(plan.CoordinateToWorld(center, height), Quaternion.Euler(42f, -25f, 0f),
                Mathf.Max(bounds.width, bounds.height) * 0.60f, false);
            view.Focus();
            view.Repaint();
        }

        private static Terrain[] GetTerrains(OfficialMapPlan plan)
        {
            GameObject[] roots = plan.gameObject.scene.GetRootGameObjects()
                .Where(root => root.name == OfficialPlainsTerrainBuilder.RootName).ToArray();
            Require(roots.Length == 1, "Expected the existing editable official plains root.");
            Terrain[] terrains = roots[0].GetComponentsInChildren<Terrain>(true)
                .OrderBy(terrain => terrain.transform.position.z).ThenBy(terrain => terrain.transform.position.x).ToArray();
            Require(terrains.Length == 50, "Expected the existing fifty grassland Terrain tiles. No new terrain is created.");
            foreach (Terrain terrain in terrains)
                Require(terrain.gameObject.scene == plan.gameObject.scene
                    && terrain.terrainData != null
                    && terrain.terrainData.heightmapResolution == OfficialPlainsTerrainBuilder.TileResolution
                    && terrain.terrainData.size == new Vector3(250f, 350f, 250f)
                    && AssetDatabase.GetAssetPath(terrain.terrainData).StartsWith(OfficialPlainsTerrainBuilder.Folder + "/data/", StringComparison.Ordinal),
                    "A selected Terrain does not belong to the existing 250 m / 2049 / 350 m official plains grid.");
            return terrains;
        }

        private static string Backup(OfficialMapPlan plan, Terrain[] terrains)
        {
            string folder = BackupFolder + "/" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            Directory.CreateDirectory(folder);
            Require(EditorSceneManager.SaveScene(plan.gameObject.scene), "Could not save the current official scene before backup.");
            File.Copy(plan.gameObject.scene.path, folder + "/Official_Map_01.unity", false);
            foreach (Terrain terrain in terrains)
            {
                TerrainData data = terrain.terrainData;
                AssetDatabase.SaveAssetIfDirty(data);
                string path = AssetDatabase.GetAssetPath(data);
                File.Copy(path, folder + "/" + Path.GetFileName(path), false);
                File.Copy(path + ".meta", folder + "/" + Path.GetFileName(path) + ".meta", false);
            }
            File.WriteAllText(folder + "/README.txt",
                "Pre-blockout source backup. These files are outside Assets and ignored by Git.\n"
                + "TerrainData filenames correspond to Assets/Maps/Official_Map_01/Plains/data/. Original GUIDs are retained.\n"
                + "Restore with Unity closed or by an explicit editor restore operation; never import duplicate GUIDs alongside current Assets.\n"
                + "The saved official scene includes all edits present before this rough blockout.\n");
            return folder;
        }

        private static void Progress(string title, string message, float progress)
        {
            EditorUtility.DisplayProgressBar(title, message, progress);
            File.WriteAllText(ReportFolder + "/Progress.txt", DateTime.Now.ToString("O") + " " + message);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        [Serializable]
        private sealed class Record
        {
            public int version;
            public string generatedUtc;
            public string backupFolder;
            public int tileCount;
            public float contourIntervalMeters;
            public float minimumEncodingHeightMeters;
            public float maximumEncodingHeightMeters;
            public int sourceHeightmapResolution;
            public string heightFieldReport;
            public string contourVerification;
            public string nativeVerification;
        }
    }
}
