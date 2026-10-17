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
            TiledLevelAsset tiled = GetSelectedTiledLevel();
            if (tiled != null)
            {
                if (!tiled.Validate(out string error)) throw new System.InvalidOperationException(error);
                foreach (var tile in tiled.Tiles) Bake(tile.level, preserveAssetGuid: true);
                return;
            }
            HeightMapLevelAsset level = GetSelectedLevel();
            if (level == null)
            {
                EditorUtility.DisplayDialog(
                    "Pre-Bake Height Map",
                    "Select a tile MapData, TiledLevelAsset, or Map Test Controller.",
                    "OK");
                return;
            }

            Bake(level);
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidatePrebakeSelectedLevel()
        {
            return GetSelectedLevel() != null || GetSelectedTiledLevel() != null;
        }

        private static HeightMapLevelAsset GetSelectedLevel()
        {
            if (Selection.activeObject is HeightMapLevelAsset level)
                return level;

            return null;
        }

        private static TiledLevelAsset GetSelectedTiledLevel()
        {
            if (Selection.activeObject is TiledLevelAsset tiled) return tiled;
            var controller = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<MapTestSceneController>() : null;
            return controller != null ? controller.LevelAsset : null;
        }

        internal static void Bake(HeightMapLevelAsset level, bool preserveAssetGuid = false)
        {
            preserveAssetGuid |= level.UsesLocalPrebakeCache;
            if (!level.IsValid)
            {
                Debug.LogError("The selected height-map level is not valid.", level);
                return;
            }

            string levelPath = AssetDatabase.GetAssetPath(level);
            string outputPath = HeightMapAssetPaths.PrebakePath(levelPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            AssetDatabase.Refresh();

            try
            {
                EditorUtility.DisplayProgressBar(
                    "Pre-Bake Height Map",
                    "Resampling and smoothing physical height data...",
                    0.15f);
                using BakedHeightField field = BakedHeightField.Bake(
                    level.HeightMap,
                    level.BakedHeightResolution,
                    level.MapSizeMeters,
                    level.MinimumHeightMeters,
                    level.MaximumHeightMeters,
                    level.NormalizeSourceRange,
                    level.SurfaceSmoothingSigmaMeters,
                    level.DetailSmoothingSigmaMeters);

                EditorUtility.DisplayProgressBar(
                    "Pre-Bake Height Map",
                    "Writing baked height textures...",
                    0.75f);
                HeightMapPrebakedData data = preserveAssetGuid
                    ? AssetDatabase.LoadAssetAtPath<HeightMapPrebakedData>(outputPath) : null;
                if (preserveAssetGuid && data == null && AssetDatabase.LoadAssetAtPath<Object>(outputPath) != null)
                    throw new System.InvalidOperationException("Refusing to overwrite an unrelated asset at " + outputPath);
                Object[] previousSubAssets = data != null
                    ? AssetDatabase.LoadAllAssetsAtPath(outputPath) : System.Array.Empty<Object>();
                if (data == null)
                {
                    if (AssetDatabase.LoadAssetAtPath<Object>(outputPath) != null)
                        AssetDatabase.DeleteAsset(outputPath);
                    data = ScriptableObject.CreateInstance<HeightMapPrebakedData>();
                    data.name = Path.GetFileNameWithoutExtension(outputPath);
                    AssetDatabase.CreateAsset(data, outputPath);
                }

                string texturePathPrefix = Path.GetDirectoryName(outputPath).Replace('\\', '/')
                    + "/" + Path.GetFileNameWithoutExtension(levelPath);
                Texture2D raw = SaveHeightTexture(
                    field.CreateRawDetailTexture(), texturePathPrefix + "_RawDetail.asset");
                Texture2D generatedDetail = field.CreateDetailTexture();
                Texture2D detail = generatedDetail != null
                    ? SaveHeightTexture(generatedDetail, texturePathPrefix + "_Detail.asset")
                    : raw;
                Texture2D surface = SaveHeightTexture(
                    field.CreateSurfaceTextureCopy(), texturePathPrefix + "_Surface.asset");
                data.SetBakedData(
                    level,
                    field.SourceMinimum,
                    field.SourceMaximum,
                    raw,
                    detail,
                    surface);
                foreach (Object previous in previousSubAssets)
                    if (previous != data && previous is Texture2D)
                        Object.DestroyImmediate(previous, true);
                level.SetPrebakedHeightField(data);
                EditorUtility.SetDirty(data);
                EditorUtility.SetDirty(level);
                AssetDatabase.SaveAssets();
                Debug.Log($"Pre-baked physical height field: {outputPath}", level);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static Texture2D SaveHeightTexture(Texture2D generated, string path)
        {
            generated.hideFlags = HideFlags.None;
            Object existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, path);
                return generated;
            }

            if (!(existing is Texture2D texture) || texture.format != TextureFormat.RFloat)
            {
                Object.DestroyImmediate(generated);
                throw new System.InvalidOperationException("Refusing to overwrite an unrelated asset at " + path);
            }

            // Update in place so repeated bakes retain the texture's GUID and references.
            EditorUtility.CopySerialized(generated, texture);
            Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(texture);
            return texture;
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
