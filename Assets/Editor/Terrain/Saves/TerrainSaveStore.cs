using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AnimalGame.Editor
{
    [Serializable]
    public sealed class TerrainSaveEntry
    {
        public string guid, assetPath, terrainName, sha256;
        public int resolution;
        public Vector3 size;
        public long bytes;
    }

    [Serializable]
    public sealed class TerrainSaveRecord
    {
        public int version = 1;
        public string id, label, createdUtc, scopeKey, sourceScene, unityVersion;
        public bool automatic;
        public TerrainSaveEntry[] entries;
    }

    /// <summary>Named, immutable native TerrainData snapshots outside Assets and Git.</summary>
    public static class TerrainSaveStore
    {
        public static string RootFolder => Path.GetFullPath(Path.Combine(Application.dataPath, "../LocalBackups/TerrainSaves"));
        private const string Manifest = "save.json";

        public static TerrainSaveRecord Save(Terrain[] targets, string label, bool automatic = false)
        {
            RequireWritable();
            TerrainSaveEntry[] entries = Describe(targets);
            label = (label ?? string.Empty).Trim();
            Require(label.Length > 0 && label.Length <= 120, "存档名称需要 1–120 个字符。");
            var record = new TerrainSaveRecord
            {
                id = Guid.NewGuid().ToString("N"), label = label,
                createdUtc = DateTime.UtcNow.ToString("O"), scopeKey = Scope(entries), automatic = automatic,
                sourceScene = targets[0].gameObject.scene.path, unityVersion = Application.unityVersion, entries = entries
            };
            string folder = DirectoryFor(record);
            Directory.CreateDirectory(folder);
            // Write the manifest last: interrupted copies never appear as valid saves.
            foreach (TerrainSaveEntry entry in entries)
            {
                TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(entry.assetPath);
                data.SyncHeightmap();
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssetIfDirty(data);
                string snapshot = Path.Combine(folder, entry.guid + ".asset");
                File.Copy(entry.assetPath, snapshot, false);
                File.Copy(entry.assetPath + ".meta", snapshot + ".meta", false);
                entry.sha256 = Hash(snapshot);
                entry.bytes = new FileInfo(snapshot).Length;
            }
            File.WriteAllText(Path.Combine(folder, Manifest), JsonUtility.ToJson(record, true), Encoding.UTF8);
            return record;
        }

        public static List<TerrainSaveRecord> List(Terrain[] targets)
        {
            TerrainSaveEntry[] current = Describe(targets);
            string scope = Scope(current);
            string folder = Path.Combine(RootFolder, scope);
            var records = new List<TerrainSaveRecord>();
            if (!Directory.Exists(folder)) return records;
            foreach (string directory in Directory.GetDirectories(folder))
            {
                string manifest = Path.Combine(directory, Manifest);
                if (!File.Exists(manifest)) continue;
                try
                {
                    TerrainSaveRecord record = JsonUtility.FromJson<TerrainSaveRecord>(File.ReadAllText(manifest));
                    ValidateIdentity(record, current);
                    Require(Path.GetFullPath(directory) == DirectoryFor(record), "存档目录与记录不匹配。");
                    records.Add(record);
                }
                catch (Exception error)
                { Debug.LogWarning("跳过无法读取的 Terrain 存档: " + manifest + "\n" + error.Message); }
            }
            return records.OrderByDescending(record => record.createdUtc, StringComparer.Ordinal).ToList();
        }

        /// <summary>Restores the selected snapshot directly, without creating a pre-restore save.</summary>
        public static void Restore(Terrain[] targets, TerrainSaveRecord selected)
        {
            RequireWritable();
            TerrainSaveEntry[] current = Describe(targets);
            ValidateIdentity(selected, current);
            string manifest = Path.Combine(DirectoryFor(selected), Manifest);
            Require(File.Exists(manifest), "此存档已经不存在，请刷新列表。");
            // Re-read disk metadata instead of trusting a stale window selection.
            TerrainSaveRecord record = JsonUtility.FromJson<TerrainSaveRecord>(File.ReadAllText(manifest));
            ValidateIdentity(record, current);
            Require(record.id == selected.id, "存档编号不匹配。");
            VerifyFiles(record);
            var paths = current.ToDictionary(entry => entry.guid, entry => entry.assetPath, StringComparer.Ordinal);
            List<Binding> bindings = CaptureBindings(paths.Keys);
            // Flush native edits in place before replacing the asset. Otherwise a snapshot that
            // matches the last disk version can reuse Unity's stale, edited in-memory object.
            // These asset files are overwritten by Apply; no backup or save record is created.
            foreach (string path in paths.Values)
            {
                TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
                data.SyncHeightmap();
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssetIfDirty(data);
            }
            Apply(record, paths, bindings);
        }

        public static Terrain[] GetTargets(GameObject root)
        {
            if (root == null) return Array.Empty<Terrain>();
            return root.GetComponentsInChildren<Terrain>(true);
        }

        private static TerrainSaveEntry[] Describe(Terrain[] targets)
        {
            Require(targets != null && targets.Length > 0, "请先选择一个 Terrain，或包含 Terrain 的根物体。");
            var entries = new Dictionary<string, TerrainSaveEntry>(StringComparer.Ordinal);
            foreach (Terrain terrain in targets)
            {
                Require(terrain != null && terrain.gameObject.scene.IsValid() && terrain.gameObject.scene.isLoaded,
                    "目标必须是已加载场景中的 Terrain。");
                TerrainData data = terrain.terrainData;
                Require(data != null, terrain.name + " 没有 TerrainData。");
                string path = AssetDatabase.GetAssetPath(data);
                Require(path.StartsWith("Assets/", StringComparison.Ordinal) && path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
                    && AssetDatabase.IsMainAsset(data), terrain.name + " 的 TerrainData 必须是 Assets 中独立保存的 .asset 资源。");
                string guid = AssetDatabase.AssetPathToGUID(path);
                Require(IsIdentifier(guid) && File.Exists(path) && File.Exists(path + ".meta"), "TerrainData 文件或资源标识缺失。");
                Require(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly) == false, "TerrainData 文件只读：" + path);
                if (!entries.ContainsKey(guid)) entries.Add(guid, new TerrainSaveEntry
                {
                    guid = guid, assetPath = path, terrainName = terrain.name,
                    resolution = data.heightmapResolution, size = data.size
                });
            }
            return entries.Values.OrderBy(entry => entry.guid, StringComparer.Ordinal).ToArray();
        }

        private static void Apply(TerrainSaveRecord record, Dictionary<string, string> paths, List<Binding> bindings)
        {
            VerifyFiles(record);
            string folder = DirectoryFor(record);
            foreach (TerrainSaveEntry entry in record.entries)
            {
                string path = paths[entry.guid];
                Require(AssetDatabase.AssetPathToGUID(path) == entry.guid, "目标资源标识发生变化，已停止恢复。");
                // Preserve the live .meta and native subasset IDs; never import duplicate GUIDs.
                File.Copy(Path.Combine(folder, entry.guid + ".asset"), path, true);
            }
            foreach (TerrainSaveEntry entry in record.entries)
            {
                string path = paths[entry.guid];
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                TerrainData restored = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
                Require(restored != null && AssetDatabase.AssetPathToGUID(path) == entry.guid
                    && restored.heightmapResolution == entry.resolution && restored.size == entry.size,
                    "重新导入后的 TerrainData 与存档不匹配：" + path);
                Require(Hash(path) == entry.sha256, "恢复后的文件校验失败：" + path);
            }
            foreach (Binding binding in bindings)
            {
                TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(paths[binding.guid]);
                if (binding.terrain != null)
                {
                    if (binding.terrain.terrainData != data) binding.terrain.terrainData = data;
                    binding.terrain.Flush();
                }
                if (binding.collider != null && binding.collider.terrainData != data) binding.collider.terrainData = data;
            }
            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
        }

        private static void VerifyFiles(TerrainSaveRecord record)
        {
            string folder = DirectoryFor(record);
            foreach (TerrainSaveEntry entry in record.entries)
            {
                string path = Path.Combine(folder, entry.guid + ".asset");
                Require(File.Exists(path) && new FileInfo(path).Length == entry.bytes
                    && string.Equals(Hash(path), entry.sha256, StringComparison.Ordinal),
                    "存档文件缺失或校验失败，尚未覆盖地形：" + entry.terrainName);
            }
        }

        private static void ValidateIdentity(TerrainSaveRecord record, TerrainSaveEntry[] current)
        {
            Require(record != null && record.version == 1 && IsIdentifier(record.id) && IsIdentifier(record.scopeKey)
                && !string.IsNullOrWhiteSpace(record.label) && record.entries != null && record.entries.Length > 0,
                "存档记录无效或版本不支持。");
            Require(record.entries.All(entry => entry != null && IsIdentifier(entry.guid)
                && entry.resolution >= 33 && entry.resolution <= 4097 && entry.bytes > 0
                && entry.sha256 != null && entry.sha256.Length == 64 && entry.sha256.All(Uri.IsHexDigit)
                && IsPositive(entry.size.x) && IsPositive(entry.size.y) && IsPositive(entry.size.z)), "存档地形数据无效。");
            string[] expected = current.Select(entry => entry.guid).OrderBy(guid => guid, StringComparer.Ordinal).ToArray();
            string[] actual = record.entries.Select(entry => entry.guid).OrderBy(guid => guid, StringComparer.Ordinal).ToArray();
            Require(expected.SequenceEqual(actual) && record.scopeKey == Scope(current),
                "此存档属于其他 Terrain 或不同的区块组合，请选择原来的目标。");
        }

        private static List<Binding> CaptureBindings(IEnumerable<string> guids)
        {
            var included = new HashSet<string>(guids, StringComparer.Ordinal);
            var bindings = new List<Binding>();
            foreach (Terrain terrain in Resources.FindObjectsOfTypeAll<Terrain>())
            {
                if (!terrain.gameObject.scene.IsValid() || !terrain.gameObject.scene.isLoaded || terrain.terrainData == null) continue;
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(terrain.terrainData));
                if (included.Contains(guid)) bindings.Add(new Binding { guid = guid, terrain = terrain });
            }
            foreach (TerrainCollider collider in Resources.FindObjectsOfTypeAll<TerrainCollider>())
            {
                if (!collider.gameObject.scene.IsValid() || !collider.gameObject.scene.isLoaded || collider.terrainData == null) continue;
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(collider.terrainData));
                if (included.Contains(guid)) bindings.Add(new Binding { guid = guid, collider = collider });
            }
            return bindings;
        }

        private static string Scope(TerrainSaveEntry[] entries)
        {
            if (entries.Length == 1) return entries[0].guid;
            using (SHA256 hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", entries.Select(entry => entry.guid)
                    .OrderBy(guid => guid, StringComparer.Ordinal))))).Replace("-", "").Substring(0, 32).ToLowerInvariant();
        }

        private static string DirectoryFor(TerrainSaveRecord record)
        {
            Require(record != null && IsIdentifier(record.scopeKey) && IsIdentifier(record.id), "无效的存档路径。");
            return Path.GetFullPath(Path.Combine(RootFolder, record.scopeKey, record.id));
        }

        private static string Hash(string path)
        {
            using (SHA256 hash = SHA256.Create()) using (FileStream stream = File.OpenRead(path))
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
        }
        private static bool IsIdentifier(string value) => value != null && value.Length == 32 && value.All(Uri.IsHexDigit);
        private static bool IsPositive(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value > 0;
        private static void RequireWritable() => Require(!EditorApplication.isPlayingOrWillChangePlaymode
            && !EditorApplication.isCompiling && !EditorApplication.isUpdating, "请在退出 Play、编译和资源导入完成后使用存档。");
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private sealed class Binding { public string guid; public Terrain terrain; public TerrainCollider collider; }
    }
}
