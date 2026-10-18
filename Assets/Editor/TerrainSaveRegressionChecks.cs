using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    /// <summary>Exercises named saves using disposable assets, never the user's terrain.</summary>
    public static class TerrainSaveRegressionChecks
    {
        private const string ReportPath = "Temp/TerrainSaves/Regression.txt";

        [MenuItem("Animal Game/Validation/Run Terrain Save Checks")]
        public static void Run()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling
                && !EditorApplication.isUpdating, "Run terrain save checks in idle Edit mode.");
            var report = new StringBuilder();
            var scopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var originalScenes = new Dictionary<int, bool>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                originalScenes.Add(scene.handle, scene.isDirty);
            }
            Scene activeScene = SceneManager.GetActiveScene();
            Object[] selection = Selection.objects;
            Fixture fixture = null;
            Exception failure = null;
            try
            {
                fixture = new Fixture();
                CheckSingleAndRecovery(fixture, scopes, report);
                CheckGroup(fixture, scopes, report);
                CheckCorruptSnapshot(fixture, scopes, report);
                Require(SceneManager.GetActiveScene() == activeScene,
                    "Terrain saves changed the user's active scene.");
                foreach (KeyValuePair<int, bool> pair in originalScenes)
                {
                    Scene scene = default;
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                        if (SceneManager.GetSceneAt(i).handle == pair.Key) scene = SceneManager.GetSceneAt(i);
                    Require(scene.IsValid() && scene.isLoaded && scene.isDirty == pair.Value,
                        "Terrain saves changed a user scene's load or dirty state.");
                }
                report.AppendLine("PASS: the user's loaded scenes, active scene and dirty flags were preserved.");
            }
            catch (Exception error)
            {
                failure = error;
            }
            finally
            {
                Selection.objects = selection;
                try { fixture?.Dispose(); }
                catch (Exception error) { failure = failure ?? error; report.AppendLine("Cleanup: " + error); }
                foreach (string scope in scopes)
                {
                    try { DeleteTestScope(scope); }
                    catch (Exception error) { failure = failure ?? error; report.AppendLine("Cleanup: " + error); }
                }
            }

            report.Insert(0, failure == null ? "PASS: Terrain named save regression checks\n"
                : "FAIL: Terrain named save regression checks\n" + failure + "\n");
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, report.ToString());
            if (failure != null) throw new InvalidOperationException("Terrain save regression failed. See " + ReportPath, failure);
            Debug.Log(report.ToString());
        }

        private static void CheckSingleAndRecovery(Fixture fixture, HashSet<string> scopes, StringBuilder report)
        {
            Terrain[] target = { fixture.First };
            Expected original = Expected.Capture(fixture.First.terrainData);
            string originalGuid = GuidFor(fixture.First.terrainData);
            string metaHash = Hash(fixture.FirstPath + ".meta");
            string otherHash = Hash(fixture.SecondPath);
            Vector3 position = fixture.First.transform.position;
            TerrainSaveRecord first = Track(TerrainSaveStore.Save(target, "同名存档"), scopes);
            Require(!first.automatic && first.entries.Length == 1 && first.entries[0].guid == originalGuid,
                "A single-terrain save did not record exactly its TerrainData GUID.");
            fixture.Apply(fixture.First.terrainData, 1);
            Expected changed = Expected.Capture(fixture.First.terrainData);
            TerrainSaveRecord second = Track(TerrainSaveStore.Save(target, "同名存档"), scopes);
            Require(first.id != second.id && first.scopeKey == second.scopeKey
                && first.label == "同名存档" && second.label == first.label,
                "Two identically named saves must remain independent entries.");
            List<TerrainSaveRecord> listed = TerrainSaveStore.List(target);
            Require(listed.Any(record => record.id == first.id) && listed.Any(record => record.id == second.id),
                "Listing a terrain's saves lost one of its identically named records.");

            TerrainSaveRecord beforeRestore = Track(TerrainSaveStore.Restore(target, first), scopes);
            Require(beforeRestore.automatic && beforeRestore.entries.Length == 1,
                "Restore did not return its automatic pre-restore save.");
            original.AssertMatches(fixture.First.terrainData, "Original named save");
            AssertConnections(fixture, originalGuid);
            Require(GuidFor(fixture.First.terrainData) == originalGuid && Hash(fixture.FirstPath + ".meta") == metaHash,
                "Restore changed the TerrainData GUID or metadata file.");
            Require(fixture.First.transform.position == position && Hash(fixture.SecondPath) == otherHash,
                "Single-terrain restore changed its placement or another terrain asset.");
            TerrainSaveRecord beforeRecovery = Track(TerrainSaveStore.Restore(target, beforeRestore), scopes);
            Require(beforeRecovery.automatic, "Restoring an automatic save did not protect the current state.");
            changed.AssertMatches(fixture.First.terrainData, "Recovering the automatic pre-restore save");
            AssertConnections(fixture, originalGuid);

            string firstHash = Hash(fixture.FirstPath);
            string secondHash = Hash(fixture.SecondPath);
            ExpectRejected(() => TerrainSaveStore.Restore(new[] { fixture.Second }, first),
                "A save belonging to another TerrainData was accepted.");
            Require(Hash(fixture.FirstPath) == firstHash && Hash(fixture.SecondPath) == secondHash,
                "Rejecting a mismatched terrain save changed an asset.");
            report.AppendLine("PASS: two saves with the same name coexist and list independently.");
            report.AppendLine("PASS: height samples, texture weights, holes, grass density, dimensions and resolutions round-trip.");
            report.AppendLine("PASS: GUID, .meta bytes, shared Terrain/Collider links and scene placement survive restore.");
            report.AppendLine("PASS: automatic pre-restore saves can be restored; mismatched targets are rejected without writes.");
        }

        private static void CheckGroup(Fixture fixture, HashSet<string> scopes, StringBuilder report)
        {
            Terrain[] targets = { fixture.First, fixture.Second };
            Expected first = Expected.Capture(fixture.First.terrainData);
            Expected second = Expected.Capture(fixture.Second.terrainData);
            string firstGuid = GuidFor(fixture.First.terrainData);
            string secondGuid = GuidFor(fixture.Second.terrainData);
            string firstMeta = Hash(fixture.FirstPath + ".meta");
            string secondMeta = Hash(fixture.SecondPath + ".meta");
            TerrainSaveRecord group = Track(TerrainSaveStore.Save(targets, "两块地形共同存档"), scopes);
            Require(group.entries.Length == 2 && group.entries.Select(entry => entry.guid).Distinct().Count() == 2,
                "Group save did not capture two distinct TerrainData assets.");
            Require(TerrainSaveStore.List(new[] { fixture.Second, fixture.First }).Any(record => record.id == group.id),
                "Group lookup depends on the order in which terrains were selected.");
            fixture.Apply(fixture.First.terrainData, 2);
            fixture.Apply(fixture.Second.terrainData, 3);
            Expected changedFirst = Expected.Capture(fixture.First.terrainData);
            Expected changedSecond = Expected.Capture(fixture.Second.terrainData);
            TerrainSaveRecord automatic = Track(TerrainSaveStore.Restore(targets, group), scopes);
            Require(automatic.automatic && automatic.entries.Length == 2,
                "A group restore did not create a complete pre-restore save.");
            first.AssertMatches(fixture.First.terrainData, "Group first terrain");
            second.AssertMatches(fixture.Second.terrainData, "Group second terrain");
            AssertConnections(fixture, firstGuid);
            Require(GuidFor(fixture.Second.terrainData) == secondGuid
                && fixture.Second.GetComponent<TerrainCollider>().terrainData == fixture.Second.terrainData
                && Hash(fixture.FirstPath + ".meta") == firstMeta && Hash(fixture.SecondPath + ".meta") == secondMeta,
                "Group restore changed a GUID, metadata or collider connection.");
            Track(TerrainSaveStore.Restore(new[] { fixture.Second, fixture.First }, automatic), scopes);
            changedFirst.AssertMatches(fixture.First.terrainData, "Group automatic recovery first terrain");
            changedSecond.AssertMatches(fixture.Second.terrainData, "Group automatic recovery second terrain");
            report.AppendLine("PASS: two-terrain groups save and restore together, independent of selection order; group recovery is complete.");
        }

        private static void CheckCorruptSnapshot(Fixture fixture, HashSet<string> scopes, StringBuilder report)
        {
            Terrain[] targets = { fixture.First, fixture.Second };
            TerrainSaveRecord record = Track(TerrainSaveStore.Save(targets, "校验损坏拒绝"), scopes);
            string snapshot = Path.Combine(TerrainSaveStore.RootFolder, record.scopeKey, record.id,
                record.entries[record.entries.Length - 1].guid + ".asset");
            byte[] bytes = File.ReadAllBytes(snapshot);
            Require(bytes.Length > 0, "The snapshot data file was empty.");
            bytes[bytes.Length / 2] ^= 0x40;
            File.WriteAllBytes(snapshot, bytes);
            fixture.Apply(fixture.First.terrainData, 4);
            fixture.Apply(fixture.Second.terrainData, 5);
            AssetDatabase.SaveAssetIfDirty(fixture.First.terrainData);
            AssetDatabase.SaveAssetIfDirty(fixture.Second.terrainData);
            string firstHash = Hash(fixture.FirstPath);
            string secondHash = Hash(fixture.SecondPath);
            Expected first = Expected.Capture(fixture.First.terrainData);
            Expected second = Expected.Capture(fixture.Second.terrainData);
            ExpectRejected(() => TerrainSaveStore.Restore(targets, record),
                "A snapshot whose SHA256 did not match was accepted.");
            Require(Hash(fixture.FirstPath) == firstHash && Hash(fixture.SecondPath) == secondHash,
                "A corrupt group snapshot caused a partial asset overwrite.");
            first.AssertMatches(fixture.First.terrainData, "Corruption refusal first terrain");
            second.AssertMatches(fixture.Second.terrainData, "Corruption refusal second terrain");
            report.AppendLine("PASS: a corrupt member of a group is rejected before either current asset is overwritten.");
        }

        private static void AssertConnections(Fixture fixture, string guid)
        {
            TerrainData imported = AssetDatabase.LoadAssetAtPath<TerrainData>(AssetDatabase.GUIDToAssetPath(guid));
            Require(imported != null && fixture.First.terrainData == imported && fixture.Shared.terrainData == imported
                && fixture.First.GetComponent<TerrainCollider>().terrainData == imported
                && fixture.Shared.GetComponent<TerrainCollider>().terrainData == imported,
                "Restore did not refresh every loaded Terrain and TerrainCollider sharing this data.");
        }

        private static TerrainSaveRecord Track(TerrainSaveRecord record, HashSet<string> scopes)
        {
            Require(record != null && !string.IsNullOrWhiteSpace(record.scopeKey), "No completed save record was returned.");
            scopes.Add(record.scopeKey);
            return record;
        }

        private static void ExpectRejected(Action action, string message)
        {
            bool rejected = false;
            try { action(); }
            catch (Exception) { rejected = true; }
            Require(rejected, message);
        }

        private static void DeleteTestScope(string scope)
        {
            string root = Path.GetFullPath(TerrainSaveStore.RootFolder).TrimEnd(Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(root, scope));
            Require(path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetDirectoryName(path), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase),
                "Refusing to delete an unexpected regression snapshot path.");
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }

        private static string GuidFor(TerrainData data) => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(data));

        private static string Hash(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream input = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class Expected
        {
            private Vector3 size;
            private int resolution, alphaResolution, detailResolution;
            private float[,] heights;
            private float[,,] alpha;
            private bool[,] holes;
            private int[,] detail;
            private string[] layers;

            public static Expected Capture(TerrainData data) => new Expected
            {
                size = data.size,
                resolution = data.heightmapResolution,
                alphaResolution = data.alphamapResolution,
                detailResolution = data.detailResolution,
                heights = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution),
                alpha = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight),
                holes = data.GetHoles(0, 0, data.holesResolution, data.holesResolution),
                detail = data.GetDetailLayer(0, 0, data.detailWidth, data.detailHeight, 0),
                layers = data.terrainLayers.Select(layer => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(layer))).ToArray()
            };

            public void AssertMatches(TerrainData data, string description)
            {
                Require(data != null && data.size == size && data.heightmapResolution == resolution
                    && data.alphamapResolution == alphaResolution && data.detailResolution == detailResolution,
                    description + ": terrain size or resolution changed.");
                Require(data.terrainLayers.Select(layer => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(layer)))
                    .SequenceEqual(layers), description + ": terrain layer references changed.");
                Equal(heights, data.GetHeights(0, 0, resolution, resolution), description + ": heights");
                Equal(alpha, data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight), description + ": alpha weights");
                Equal(holes, data.GetHoles(0, 0, data.holesResolution, data.holesResolution), description + ": holes");
                Require(data.detailPrototypes.Length == 1, description + ": grass prototype was lost.");
                Equal(detail, data.GetDetailLayer(0, 0, data.detailWidth, data.detailHeight, 0), description + ": grass density");
            }

            private static void Equal(Array expected, Array actual, string message)
            {
                Require(expected.Rank == actual.Rank && expected.Length == actual.Length, message + " dimensions differ.");
                for (int i = 0; i < expected.Rank; i++)
                    Require(expected.GetLength(i) == actual.GetLength(i), message + " dimensions differ.");
                System.Collections.IEnumerator values = actual.GetEnumerator();
                foreach (object value in expected)
                {
                    Require(values.MoveNext(), message + " ended early.");
                    if (value is float number)
                        Require(Mathf.Abs(number - (float)values.Current) < 0.000001f, message + " sample differs.");
                    else Require(value.Equals(values.Current), message + " sample differs.");
                }
            }
        }

        private sealed class Fixture : IDisposable
        {
            public readonly string Folder = "Assets/__TerrainSaveRegression_" + Guid.NewGuid().ToString("N");
            public string FirstPath => Folder + "/First.asset";
            public string SecondPath => Folder + "/Second.asset";
            public Terrain First, Second, Shared;
            private Scene scene;
            private TerrainLayer[] layers;
            private DetailPrototype grass;

            public Fixture()
            {
                try
                {
                    AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
                    scene = EditorSceneManager.NewPreviewScene();
                    var texture = new Texture2D(2, 2) { name = "Regression grass texture" };
                    texture.SetPixels(new[] { Color.green, Color.green, Color.green, Color.green });
                    texture.Apply();
                    AssetDatabase.CreateAsset(texture, Folder + "/GrassTexture.asset");
                    layers = new[] { new TerrainLayer { name = "Layer A", diffuseTexture = texture },
                        new TerrainLayer { name = "Layer B", diffuseTexture = texture } };
                    AssetDatabase.CreateAsset(layers[0], Folder + "/A.terrainlayer");
                    AssetDatabase.CreateAsset(layers[1], Folder + "/B.terrainlayer");
                    grass = new DetailPrototype { prototypeTexture = texture, renderMode = DetailRenderMode.GrassBillboard,
                        minWidth = 0.5f, maxWidth = 1f, minHeight = 0.5f, maxHeight = 1f,
                        healthyColor = Color.green, dryColor = Color.yellow, noiseSpread = 0.1f };
                    First = Create("Regression First", FirstPath, 0, Vector3.zero);
                    Second = Create("Regression Second", SecondPath, 2, new Vector3(250f, 0f, 0f));
                    Shared = CreateObject("Regression Shared Data", First.terrainData, new Vector3(0f, 0f, 500f));
                }
                catch { Dispose(); throw; }
            }

            private Terrain Create(string name, string path, int variant, Vector3 position)
            {
                var data = new TerrainData { name = name + " Data" };
                Apply(data, variant);
                AssetDatabase.CreateAsset(data, path);
                AssetDatabase.SaveAssetIfDirty(data);
                return CreateObject(name, data, position);
            }

            private Terrain CreateObject(string name, TerrainData data, Vector3 position)
            {
                GameObject obj = Terrain.CreateTerrainGameObject(data);
                obj.name = name;
                SceneManager.MoveGameObjectToScene(obj, scene);
                obj.transform.position = position;
                Terrain terrain = obj.GetComponent<Terrain>();
                terrain.allowAutoConnect = false;
                return terrain;
            }

            public void Apply(TerrainData data, int variant)
            {
                int resolution = variant % 2 == 0 ? 33 : 65;
                data.heightmapResolution = resolution;
                data.size = new Vector3(250f + variant * 7f, 350f + variant * 13f, 250f + variant * 11f);
                data.alphamapResolution = variant % 2 == 0 ? 16 : 32;
                data.baseMapResolution = 32;
                data.terrainLayers = layers;
                var heights = new float[resolution, resolution];
                for (int y = 0; y < resolution; y++)
                    for (int x = 0; x < resolution; x++)
                        heights[y, x] = 0.1f + variant * 0.04f + (x + y * 0.5f) / (resolution - 1) * 0.2f;
                data.SetHeights(0, 0, heights);
                int alphaSize = data.alphamapResolution;
                var alpha = new float[alphaSize, alphaSize, 2];
                for (int y = 0; y < alphaSize; y++)
                    for (int x = 0; x < alphaSize; x++)
                    {
                        float weight = ((x + y + variant * 3) % 9 + 1) / 10f;
                        alpha[y, x, 0] = weight;
                        alpha[y, x, 1] = 1f - weight;
                    }
                data.SetAlphamaps(0, 0, alpha);
                var holes = new bool[data.holesResolution, data.holesResolution];
                for (int y = 0; y < holes.GetLength(0); y++)
                    for (int x = 0; x < holes.GetLength(1); x++)
                        holes[y, x] = (x + variant) % 7 != 0 || (y + variant) % 5 != 0;
                data.SetHoles(0, 0, holes);
                data.SetDetailResolution(16, 8);
                data.detailPrototypes = new[] { grass };
                var density = new int[16, 16];
                for (int y = 0; y < 16; y++)
                    for (int x = 0; x < 16; x++) density[y, x] = (x + y + variant) % 4;
                data.SetDetailLayer(0, 0, 0, density);
                EditorUtility.SetDirty(data);
            }

            public void Dispose()
            {
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
                if (AssetDatabase.IsValidFolder(Folder))
                    Require(AssetDatabase.DeleteAsset(Folder), "Could not remove disposable regression assets: " + Folder);
            }
        }
    }
}
