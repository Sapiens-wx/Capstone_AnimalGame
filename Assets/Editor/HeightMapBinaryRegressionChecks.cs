using System;
using System.IO;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    public static class HeightMapBinaryRegressionChecks
    {
        [MenuItem("Animal Game/Run Binary Height Cache Checks")]
        public static void Run()
        {
            string folder = "Assets/__BinaryHeightChecks_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            try
            {
                var source = new Texture2D(17, 17, TextureFormat.R16, false, true);
                var pixels = new ushort[17 * 17];
                for (int y = 0; y < 17; y++) for (int x = 0; x < 17; x++)
                    pixels[y * 17 + x] = (ushort)(1000 + x * 1300 + y * 1900 + (x == 8 && y == 8 ? 12000 : 0));
                source.SetPixelData(pixels, 0);
                source.Apply();
                AssetDatabase.CreateAsset(source, folder + "/Source.asset");
                var level = ScriptableObject.CreateInstance<HeightMapLevelAsset>();
                AssetDatabase.CreateAsset(level, folder + "/Level.asset");
                var settings = new SerializedObject(level);
                settings.FindProperty("heightMap").objectReferenceValue = source;
                settings.FindProperty("bakedHeightResolution").intValue = 128;
                settings.FindProperty("mapWidthMeters").floatValue = 20;
                settings.FindProperty("mapHeightMeters").floatValue = 15;
                settings.FindProperty("minimumHeightMeters").floatValue = -35;
                settings.FindProperty("maximumHeightMeters").floatValue = 165;
                settings.FindProperty("normalizeSourceRange").boolValue = false;
                settings.FindProperty("surfaceSmoothingSigmaMeters").floatValue = 0.35f;
                settings.FindProperty("detailSmoothingSigmaMeters").floatValue = 0;
                settings.ApplyModifiedPropertiesWithoutUndo();

                HeightMapPrebakeEditor.Bake(level);
                var data = level.PrebakedHeightField;
                Require(data != null && data.Matches(level), "New bake did not assign a valid cache.");
                Require(data.DetailHeightData == data.RawDetailHeightData && !File.Exists(folder + "/Level_Detail.bytes"), "Raw/detail were duplicated.");
                Require(new SerializedObject(data).FindProperty("detailHeightData").objectReferenceValue == data.RawDetailHeightData,
                    "Identical detail did not explicitly reference the raw binary.");
                VerifySamples(level);
                string cacheGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(data));
                string rawGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(data.RawDetailHeightData));
                string surfaceGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(data.SurfaceTexture));

                settings.Update();
                settings.FindProperty("detailSmoothingSigmaMeters").floatValue = 0.2f;
                settings.ApplyModifiedPropertiesWithoutUndo();
                Require(!data.Matches(level), "Changing smoothing did not invalidate the cache.");
                HeightMapPrebakeEditor.Bake(level);
                data = level.PrebakedHeightField;
                Require(data.DetailHeightData != data.RawDetailHeightData,
                    "Distinct detail channel was lost.");
                Require(cacheGuid == AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(data))
                    && rawGuid == AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(data.RawDetailHeightData))
                    && surfaceGuid == AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(data.SurfaceTexture)),
                    "Re-baking changed existing resource GUIDs.");
                VerifySamples(level);

                var invalid = new TextAsset(new byte[7]);
                try
                {
                    var serialized = new SerializedObject(data);
                    serialized.FindProperty("surfaceHeightData").objectReferenceValue = invalid;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    Require(!data.HasHeightData && BakedHeightField.FromPrebakedData(data) == null,
                        "Malformed binary length was accepted.");
                }
                finally { Object.DestroyImmediate(invalid); }
                Debug.Log("PASS: binary meter heights; all channels and interpolation; raw/detail deduplication; "
                    + "distinct smoothing; imported assets; stable GUIDs on rebake; malformed data rejection; GPU texture; no height-array allocation on load.");
            }
            finally { AssetDatabase.DeleteAsset(folder); }
        }

        private static void VerifySamples(HeightMapLevelAsset level)
        {
            var data = level.PrebakedHeightField;
            Require(data.HasHeightData && !data.SurfaceTexture.isReadable, "GPU height texture retained a CPU copy.");
            using var expected = BakedHeightField.Bake(level.HeightMap, level.BakedHeightResolution,
                level.MapSizeMeters, level.MinimumHeightMeters, level.MaximumHeightMeters,
                level.NormalizeSourceRange, level.SurfaceSmoothingSigmaMeters, level.DetailSmoothingSigmaMeters);
            using var actual = BakedHeightField.FromPrebakedData(data);
            Require(actual != null, "Imported binaries did not load.");
            int side = actual.Width;
            for (int y = 0; y < side; y++) for (int x = 0; x < side; x++)
                Compare(expected, actual, new Vector2(x / (float)(side - 1), y / (float)(side - 1)));
            foreach (var uv in new[] { new Vector2(0.1234f, 0.4567f), new Vector2(0.999f, 0.002f), Vector2.one * 0.5f })
                Compare(expected, actual, uv);
            var surface = data.SurfaceHeightData.GetData<float>();
            Require(surface[0] == expected.GetSurfaceHeightSample(0, 0), "Binary values are not exact meter heights.");

            // Warm up the loader before measuring; copied height arrays would vastly exceed this bound.
            using (BakedHeightField.FromPrebakedData(data)) { }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 32; i++) using (BakedHeightField.FromPrebakedData(data)) { }
            Require(GC.GetAllocatedBytesForCurrentThread() - before < 65536, "Loading allocated per-sample height arrays.");
            actual.Dispose();
            Require(data.SurfaceTexture != null && surface[0] == expected.GetSurfaceHeightSample(0, 0),
                "Disposing a field destroyed borrowed assets.");
        }

        private static void Compare(BakedHeightField expected, BakedHeightField actual, Vector2 uv)
        {
            Require(expected.SampleRawDetailHeight(uv) == actual.SampleRawDetailHeight(uv), "Raw height changed at " + uv);
            Require(expected.SampleDetailHeight(uv) == actual.SampleDetailHeight(uv), "Detail height changed at " + uv);
            Require(expected.SampleSurfaceHeight(uv) == actual.SampleSurfaceHeight(uv), "Surface height changed at " + uv);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
