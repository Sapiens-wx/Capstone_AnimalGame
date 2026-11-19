using System;
using System.Collections.Generic;
using System.IO;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    internal static class HeightMapPrebakeEditor
    {
        internal const string BakeMenuPath = "Animal Game/Prebake/Prebake Height Map";
        private const string CleanMenuPath = "Animal Game/Prebake/Clean Prebake";

        [MenuItem(BakeMenuPath)]
        private static void PrebakeSelectedLevel()
        {
            if (Selection.activeObject is HeightMapLevelAsset level)
            {
                var owner = FindTiledLevel(level);
                Bake(level, outputDirectory: owner != null ? GetPrebakeDirectory(owner) : null);
                return;
            }

            Bake(GetSelectedTiledLevel());
        }

        internal static void Bake(TiledLevelAsset tiled)
        {
            if (!tiled.Validate(out string error)) throw new InvalidOperationException(error);
            string directory = GetPrebakeDirectory(tiled);
            foreach (var tile in tiled.Tiles) Bake(tile.level, outputDirectory: directory);
        }

        [MenuItem(BakeMenuPath, true)]
        private static bool ValidatePrebakeSelectedLevel()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode
                && (Selection.activeObject is HeightMapLevelAsset || GetSelectedTiledLevel() != null);
        }

        [MenuItem(CleanMenuPath)]
        private static void CleanSelectedLevel()
        {
            var tiled = GetSelectedTiledLevel();
            string directory = GetPrebakeDirectory(tiled);
            if (!Directory.Exists(directory))
            {
                Debug.Log("No prebake directory to clean: " + directory, tiled);
                return;
            }

            if (EditorUtility.DisplayDialog("Clean Prebake",
                $"Delete all contents of {directory}?", "Clean", "Cancel"))
                Clean(tiled);
        }

        [MenuItem(CleanMenuPath, true)]
        private static bool ValidateCleanSelectedLevel()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode && GetSelectedTiledLevel() != null;
        }

        private static TiledLevelAsset GetSelectedTiledLevel()
        {
            if (Selection.activeObject is TiledLevelAsset tiled) return tiled;
            var controller = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<MapTestSceneController>() : null;
            return controller != null ? controller.LevelAsset : null;
        }

        private static TiledLevelAsset FindTiledLevel(HeightMapLevelAsset level)
        {
            TiledLevelAsset owner = null;
            foreach (string guid in AssetDatabase.FindAssets("t:TiledLevelAsset"))
            {
                var tiled = AssetDatabase.LoadAssetAtPath<TiledLevelAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (tiled == null || !tiled.Contains(level)) continue;
                if (owner != null)
                    throw new InvalidOperationException("This tile belongs to multiple tiled levels. Select the tiled level to bake.");
                owner = tiled;
            }
            return owner;
        }

        private static string GetPrebakeDirectory(TiledLevelAsset tiled)
        {
            string path = AssetDatabase.GetAssetPath(tiled);
            if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException("The tiled level must be saved under Assets.");
            return HeightMapAssetPaths.PrebakeDirectory(path);
        }

        internal static void Clean(TiledLevelAsset tiled)
        {
            string directory = GetPrebakeDirectory(tiled);
            string fullDirectory = Path.GetFullPath(directory);
            string assetsRoot = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            if (!fullDirectory.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Cannot clean a directory outside Assets: " + fullDirectory);
            if (!Directory.Exists(fullDirectory)) return;

            var referencedCaches = new Dictionary<HeightMapLevelAsset, string>();
            foreach (var tile in tiled.Tiles)
            {
                var level = tile?.level;
                if (level == null || string.IsNullOrEmpty(level.PrebakedHeightPath)) continue;
                string cachePath = HeightMapAssetPaths.ResourcesDirectory + level.PrebakedHeightPath + ".asset";
                if (cachePath.StartsWith(directory + "/", StringComparison.Ordinal))
                    referencedCaches[level] = cachePath;
            }

            try
            {
                foreach (string entry in Directory.GetFileSystemEntries(fullDirectory))
                    if (!FileUtil.DeleteFileOrDirectory(entry))
                        throw new IOException("Could not delete prebake data: " + entry);
            }
            finally
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                foreach (var entry in referencedCaches)
                {
                    if (File.Exists(entry.Value)) continue;
                    entry.Key.SetPrebakedHeightPath(null);
                    EditorUtility.SetDirty(entry.Key);
                    AssetDatabase.SaveAssetIfDirty(entry.Key);
                }
            }
            Debug.Log("Cleaned prebake data: " + directory, tiled);
        }

        internal static void Bake(HeightMapLevelAsset level, string outputDirectory = null)
        {
            if (!level.IsValid)
            {
                Debug.LogError("The selected height-map level is not valid.", level);
                return;
            }

            string levelPath = AssetDatabase.GetAssetPath(level);
            string outputPath = HeightMapAssetPaths.PrebakePath(levelPath,
                outputDirectory ?? HeightMapAssetPaths.PrebakeDirectory(levelPath));
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
                    "Writing meter height binaries and GPU surface texture...",
                    0.75f);
                HeightMapPrebakedData data = AssetDatabase.LoadAssetAtPath<HeightMapPrebakedData>(outputPath);
                if (data == null && AssetDatabase.LoadAssetAtPath<Object>(outputPath) != null)
                    throw new System.InvalidOperationException("Refusing to overwrite an unrelated asset at " + outputPath);
                Object[] previousSubAssets = data != null
                    ? AssetDatabase.LoadAllAssetsAtPath(outputPath) : System.Array.Empty<Object>();
                if (data == null)
                {
                    data = ScriptableObject.CreateInstance<HeightMapPrebakedData>();
                    data.name = Path.GetFileNameWithoutExtension(outputPath);
                    AssetDatabase.CreateAsset(data, outputPath);
                }

                string texturePathPrefix = Path.GetDirectoryName(outputPath).Replace('\\', '/')
                    + "/" + Path.GetFileNameWithoutExtension(levelPath);
                TextAsset raw = SaveHeightData(field.CreateRawDetailHeightData(), texturePathPrefix + "_RawDetail.bytes");
                byte[] detailBytes = field.CreateDetailHeightData();
                TextAsset detail = detailBytes != null
                    ? SaveHeightData(detailBytes, texturePathPrefix + "_Detail.bytes") : raw;
                TextAsset surfaceData = SaveHeightData(field.CreateSurfaceHeightData(), texturePathPrefix + "_Surface.bytes");
                Texture2D surface = SaveHeightTexture(
                    field.CreateSurfaceTextureCopy(), texturePathPrefix + "_Surface.asset");
                data.SetBakedData(
                    level,
                    field.SourceMinimum,
                    field.SourceMaximum,
                    raw,
                    detail,
                    surfaceData,
                    surface);
                foreach (Object previous in previousSubAssets)
                    if (previous != data && previous is Texture2D)
                        Object.DestroyImmediate(previous, true);
                level.SetPrebakedHeightPath(HeightMapAssetPaths.ResourcePath(outputPath));
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

        /// <summary>Headerless little-endian IEEE754 Float32 meters, indexed by y * resolution + x.</summary>
        internal static TextAsset SaveHeightData(byte[] bytes, string path)
        {
            if (File.Exists(path) && !(AssetDatabase.LoadMainAssetAtPath(path) is TextAsset))
                throw new System.InvalidOperationException("Refusing to overwrite an unrelated asset at " + path);
            File.WriteAllBytes(path, bytes);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var data = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (data == null || data.dataSize != bytes.Length)
                throw new System.InvalidOperationException("Could not import height binary: " + path);
            return data;
        }

        [MenuItem("Animal Game/Migrate Height Caches To Meter Binaries")]
        public static void MigrateExistingCaches()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new System.InvalidOperationException("Exit Play before migrating height caches.");
            int migrated = 0;
            var visited = new System.Collections.Generic.HashSet<HeightMapPrebakedData>();
            foreach (string guid in AssetDatabase.FindAssets("t:HeightMapLevelAsset"))
            {
                var level = AssetDatabase.LoadAssetAtPath<HeightMapLevelAsset>(AssetDatabase.GUIDToAssetPath(guid));
                var data = level.PrebakedHeightField;
                if (data == null || !visited.Add(data) || data.HasHeightData) continue;
                if (!data.MatchesSourceSettings(level))
                    throw new System.InvalidOperationException("Re-bake stale height cache before migration: " + AssetDatabase.GetAssetPath(level));
                var rawTexture = data.LegacyRawDetailTexture;
                var detailTexture = data.LegacyDetailTexture;
                var surfaceTexture = data.SurfaceTexture;
                byte[] raw = ReadLegacyMeters(rawTexture, data);
                byte[] detail = detailTexture == rawTexture ? null : ReadLegacyMeters(detailTexture, data);
                if (detail != null && System.Linq.Enumerable.SequenceEqual(raw, detail)) detail = null;
                byte[] surface = ReadLegacyMeters(surfaceTexture, data);
                string cachePath = AssetDatabase.GetAssetPath(data);
                string prefix = Path.GetDirectoryName(cachePath).Replace('\\', '/') + "/"
                    + Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(level));
                TextAsset rawData = SaveHeightData(raw, prefix + "_RawDetail.bytes");
                data.SetBakedData(level, data.SourceMinimum, data.SourceMaximum,
                    rawData,
                    detail != null ? SaveHeightData(detail, prefix + "_Detail.bytes") : rawData,
                    SaveHeightData(surface, prefix + "_Surface.bytes"), surfaceTexture);
                // CPU now reads the binary; keep only the GPU copy of the normalized surface texture.
                surfaceTexture.Apply(false, true);
                EditorUtility.SetDirty(surfaceTexture);
                EditorUtility.SetDirty(data);
                foreach (Object previous in AssetDatabase.LoadAllAssetsAtPath(cachePath))
                    if (previous is Texture2D && previous != surfaceTexture) Object.DestroyImmediate(previous, true);
                AssetDatabase.SaveAssets();
                migrated++;
                Debug.Log("Migrated meter height binaries: " + cachePath, level);
            }
            Debug.Log("Migrated " + migrated + " height caches to Float32 meter binaries.");
        }

        private static byte[] ReadLegacyMeters(Texture2D texture, HeightMapPrebakedData data)
        {
            if (texture == null || texture.format != TextureFormat.RFloat
                || texture.width != data.Resolution || texture.height != data.Resolution)
                throw new System.InvalidOperationException("Missing or invalid legacy height texture: " + data.name);
            var values = texture.GetPixelData<float>(0);
            var meters = new float[values.Length];
            for (int i = 0; i < meters.Length; i++)
                meters[i] = Mathf.Lerp(data.MinimumHeightMeters, data.MaximumHeightMeters, values[i]);
            var bytes = new byte[meters.Length * sizeof(float)];
            System.Buffer.BlockCopy(meters, 0, bytes, 0, bytes.Length);
            if (!System.BitConverter.IsLittleEndian)
                for (int i = 0; i < bytes.Length; i += sizeof(float)) System.Array.Reverse(bytes, i, sizeof(float));
            return bytes;
        }

        private static Texture2D SaveHeightTexture(Texture2D generated, string path)
        {
            generated.hideFlags = HideFlags.None;
            generated.name = Path.GetFileNameWithoutExtension(path);
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

    }
}
