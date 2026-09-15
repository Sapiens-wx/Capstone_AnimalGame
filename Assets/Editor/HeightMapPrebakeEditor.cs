using System.IO;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEngine;

namespace AnimalGame.Editor
{
    internal static class HeightMapPrebakeEditor
    {
        private const string MenuPath = "Animal Game/Pre-Bake Height Map";

        [MenuItem(MenuPath)]
        private static void PrebakeSelectedLevel()
        {
            HeightMapLevelAsset level = GetSelectedLevel();
            if (level == null)
            {
                EditorUtility.DisplayDialog(
                    "Pre-Bake Height Map",
                    "Select a HeightMapLevelAsset or a Map Test Controller with an assigned level asset.",
                    "OK");
                return;
            }

            Bake(level);
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidatePrebakeSelectedLevel()
        {
            return GetSelectedLevel() != null;
        }

        private static HeightMapLevelAsset GetSelectedLevel()
        {
            if (Selection.activeObject is HeightMapLevelAsset level)
                return level;

            if (Selection.activeGameObject == null)
                return null;

            MapTestSceneController controller = Selection.activeGameObject
                .GetComponent<MapTestSceneController>();
            return controller != null ? controller.LevelAsset : null;
        }

        private static void Bake(HeightMapLevelAsset level)
        {
            if (!level.IsValid)
            {
                Debug.LogError("The selected height-map level is not valid.", level);
                return;
            }

            string levelPath = AssetDatabase.GetAssetPath(level);
            string outputPath = Path.Combine(
                    Path.GetDirectoryName(levelPath) ?? "Assets",
                    Path.GetFileNameWithoutExtension(levelPath)
                    + "_HeightPrebake.asset")
                .Replace('\\', '/');

            try
            {
                EditorUtility.DisplayProgressBar(
                    "Pre-Bake Height Map",
                    "Resampling and smoothing physical height data...",
                    0.15f);
                BakedHeightField field = BakedHeightField.Bake(
                    level.HeightMap,
                    level.BakedHeightResolution,
                    level.MapSizeMeters,
                    level.MinimumHeightMeters,
                    level.MaximumHeightMeters,
                    level.NormalizeSourceRange,
                    level.SurfaceSmoothingSigmaMeters,
                    level.DetailSmoothingSigmaMeters,
                    level.UseHeightMapBorderMask,
                    level.HeightMapBorderMaskThreshold,
                    level.HeightMapBorderInsetMeters,
                    level.PlayableAreaMask);

                EditorUtility.DisplayProgressBar(
                    "Pre-Bake Height Map",
                    "Writing baked height textures...",
                    0.75f);
                if (AssetDatabase.LoadAssetAtPath<Object>(outputPath) != null)
                    AssetDatabase.DeleteAsset(outputPath);

                var data = ScriptableObject.CreateInstance<HeightMapPrebakedData>();
                data.name = Path.GetFileNameWithoutExtension(outputPath);
                AssetDatabase.CreateAsset(data, outputPath);

                Texture2D raw = field.CreateRawDetailTexture();
                Texture2D detail = field.CreateDetailTexture() ?? raw;
                Texture2D surface = field.CreateSurfaceTextureCopy();
                Texture2D mask = field.CreatePlayableMaskTextureCopy();
                Texture2D preview = field.CreateVisualizationTexture(
                    level.PreviewResolution,
                    level.BackgroundColor,
                    level.LowHeightColor,
                    level.MiddleHeightColor,
                    level.HighHeightColor);
                AddSubAsset(raw, data);
                if (detail != raw)
                    AddSubAsset(detail, data);
                AddSubAsset(surface, data);
                AddSubAsset(mask, data);
                AddSubAsset(preview, data);
                data.SetBakedData(
                    level,
                    field.SourceMinimum,
                    field.SourceMaximum,
                    raw,
                    detail,
                    surface,
                    mask,
                    preview);
                level.SetPrebakedHeightField(data);
                EditorUtility.SetDirty(data);
                EditorUtility.SetDirty(level);
                AssetDatabase.SaveAssets();
                field.Dispose();
                Debug.Log($"Pre-baked physical height field: {outputPath}", level);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static void AddSubAsset(Object asset, Object parent)
        {
            if (asset != null)
            {
                asset.hideFlags = HideFlags.None;
                AssetDatabase.AddObjectToAsset(asset, parent);
            }
        }
    }
}
