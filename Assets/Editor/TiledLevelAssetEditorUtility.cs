using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AnimalGame.Editor
{
    public static class TiledLevelAssetEditorUtility
    {
        public const string GridFolder = "Assets/Maps/TerrainGridV1";
        public const string GridPath = GridFolder + "/TerrainGridV1_TiledLevel.asset";
        public const string MainFolder = "Assets/Maps/MainHeightMapLevel";

        public static TiledLevelAsset WrapSingle(HeightMapLevelAsset level)
        {
            string source = AssetDatabase.GetAssetPath(level);
            string path = Path.GetDirectoryName(source).Replace('\\', '/') + "/" + level.name + "_TiledLevel.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TiledLevelAsset>(path);
            if (existing != null) return existing;
            return Create(path, level.MapSizeMeters,
                new Vector2(level.PreviewResolution / level.PixelsPerUnit / level.MapSizeMeters.x,
                    level.PreviewResolution / level.PixelsPerUnit / level.MapSizeMeters.y),
                level, new[] { new TiledLevelAsset.Tile(Vector2Int.zero, level) });
        }

        public static TiledLevelAsset CreateGrid()
        {
            var entries = new List<TiledLevelAsset.Tile>();
            for (int z = 0; z < 2; z++) for (int x = 0; x < 2; x++)
            {
                var level = AssetDatabase.LoadAssetAtPath<HeightMapLevelAsset>($"{GridFolder}/Tile_{x}_{z}_MapData.asset");
                if (level == null) throw new InvalidOperationException("Export all four tiles first.");
                entries.Add(new TiledLevelAsset.Tile(new Vector2Int(x, z), level));
            }
            var existing = AssetDatabase.LoadAssetAtPath<TiledLevelAsset>(GridPath);
            // Re-export must not reset the user's presentation source or world scale.
            if (existing != null) return existing;
            return Create(GridPath, Vector2.one * 250, Vector2.one * (2048f / 30f / 250f), entries[0].level, entries);
        }

        private static TiledLevelAsset Create(string path, Vector2 size, Vector2 scale,
            HeightMapLevelAsset presentation, IEnumerable<TiledLevelAsset.Tile> entries)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                throw new InvalidOperationException("Refusing to replace existing asset: " + path);
            var asset = ScriptableObject.CreateInstance<TiledLevelAsset>();
            asset.Configure(size, scale, presentation, entries);
            AssetDatabase.CreateAsset(asset, path);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            return asset;
        }

        [MenuItem("Animal Game/Tiled Levels/Migrate Main and TerrainGridV1")]
        public static void Migrate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play before migration.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save modified scenes before migrating asset references.");
            EnsureFolder(MainFolder + "/data");
            EnsureFolder(GridFolder + "/data");
            Move("Assets/Maps/MainHeightMapLevel.asset", MainFolder + "/MainHeightMapLevel.asset");
            var main = AssetDatabase.LoadAssetAtPath<HeightMapLevelAsset>(MainFolder + "/MainHeightMapLevel.asset");
            if (main == null) throw new InvalidOperationException("Main level is missing.");
            // Dependencies keep their GUIDs, including textures shared by other maps.
            foreach (string path in AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(main)).ToArray())
            {
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith(MainFolder + "/", StringComparison.Ordinal)) continue;
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset is Texture2D || asset is HeightMapPrebakedData)
                    Move(path, MainFolder + "/data/" + Path.GetFileName(path));
            }
            // Include generated sidecars which might no longer be assigned in the inspector.
            foreach (string path in AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/Maps/MainHeightMapLevel_", StringComparison.Ordinal)).ToArray())
                Move(path, MainFolder + "/data/" + Path.GetFileName(path));

            for (int z = 0; z < 2; z++) for (int x = 0; x < 2; x++)
            {
                string stem = $"Tile_{x}_{z}";
                Move($"{GridFolder}/{stem}_TerrainData.asset", $"{GridFolder}/data/{stem}_TerrainData.asset");
            }
            foreach (string path in AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith(GridFolder + "/Exports/", StringComparison.Ordinal)).ToArray())
            {
                if (AssetDatabase.IsValidFolder(path)) continue;
                string folder = path.EndsWith("_MapData.asset", StringComparison.Ordinal) ? GridFolder : GridFolder + "/data";
                Move(path, folder + "/" + Path.GetFileName(path));
            }
            var grid = CreateGrid();
            var mainTiled = WrapSingle(main);
            string mainGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(main));
            string oldCombinedGuid = AssetDatabase.AssetPathToGUID(GridFolder + "/Combined/Combined_500m_MapData.asset");
            var replacement = new Dictionary<string, string>
            { [mainGuid] = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(mainTiled)) };
            if (!string.IsNullOrEmpty(oldCombinedGuid)) replacement[oldCombinedGuid] = AssetDatabase.AssetPathToGUID(GridPath);
            var changedScenes = new HashSet<string>();
            foreach (string path in AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/", StringComparison.Ordinal)
                && (p.EndsWith(".unity", StringComparison.Ordinal) || p.EndsWith(".prefab", StringComparison.Ordinal))).ToArray())
            {
                string source = File.ReadAllText(path);
                string updated = Regex.Replace(source, @"(levelAsset: \{fileID: 11400000, guid: )([0-9a-f]{32})",
                    match => match.Groups[1].Value + (replacement.TryGetValue(match.Groups[2].Value, out string guid) ? guid : match.Groups[2].Value));
                if (source == updated) continue;
                File.WriteAllText(path, updated, new UTF8Encoding(false));
                if (path.EndsWith(".unity", StringComparison.Ordinal)) changedScenes.Add(path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            AssetDatabase.SaveAssets();
            // Delete only the obsolete generated Combined outputs and its exporter.
            if (AssetDatabase.IsValidFolder(GridFolder + "/Combined")) AssetDatabase.DeleteAsset(GridFolder + "/Combined");
            if (AssetDatabase.IsValidFolder(GridFolder + "/Exports")) AssetDatabase.DeleteAsset(GridFolder + "/Exports");
            AssetDatabase.Refresh();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (changedScenes.Contains(scene.path))
                {
                    string path = scene.path;
                    if (SceneManager.sceneCount == 1) EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    else
                    {
                        EditorSceneManager.CloseScene(scene, true);
                        EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    }
                }
            }
            Debug.Log("Migrated MainHeightMapLevel and TerrainGridV1 to tiled levels; existing GUIDs preserved.");
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
        private static void Move(string source, string destination)
        {
            if (source == destination || AssetDatabase.LoadMainAssetAtPath(source) == null) return;
            if (AssetDatabase.LoadMainAssetAtPath(destination) != null)
                throw new InvalidOperationException("Migration destination already exists: " + destination);
            string error = AssetDatabase.MoveAsset(source, destination);
            if (!string.IsNullOrEmpty(error)) throw new IOException(error);
        }
    }
    [CustomEditor(typeof(TiledLevelAsset))]
    public sealed class TiledLevelAssetInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var level = (TiledLevelAsset)target;
            if (!level.Validate(out string error))
                EditorGUILayout.HelpBox(error, MessageType.Error);
            else
                EditorGUILayout.HelpBox($"{level.Tiles.Count} tiles; grid {level.GridSize.x} x {level.GridSize.y}; "
                    + $"map {level.MapSizeMeters.x} x {level.MapSizeMeters.y} meters. All tiles are resident.", MessageType.Info);
        }
    }

}
