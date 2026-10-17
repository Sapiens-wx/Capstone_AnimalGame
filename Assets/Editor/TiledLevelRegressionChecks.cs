using System;
using System.Collections.Generic;
using System.Linq;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    public static class TiledLevelRegressionChecks
    {
        private const string SmokeKey = "AnimalGame.TiledLevel.PlaySmoke";
        private static string SmokeReport => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TiledLevelPlaySmoke.txt");

        [MenuItem("Animal Game/Tiled Levels/Run V1 Play Smoke Check")]
        public static void RunPlaySmoke()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != ContinuousMapV1SceneSetup.ScenePath)
                throw new InvalidOperationException("Open the continuous V1 scene first.");
            SessionState.SetBool(SmokeKey, true);
            System.IO.File.WriteAllText(SmokeReport, "RUNNING");
            ResumeSmoke();
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        private static void ResumeSmoke()
        {
            EditorApplication.update -= SmokeTick;
            if (SessionState.GetBool(SmokeKey, false)) EditorApplication.update += SmokeTick;
        }

        private static void SmokeTick()
        {
            if (!EditorApplication.isPlaying || Time.frameCount < 30) return;
            EditorApplication.update -= SmokeTick;
            try
            {
                ContinuousMapV1SceneSetup.Validate();
                var map = Object.FindFirstObjectByType<MapTestSceneController>();
                var bootstrap = Object.FindFirstObjectByType<HeightMapPlayerSceneBootstrap>();
                Require(map.RuntimeMap.TileCount == 4 && map.RuntimeMap.Tiles.All(t => t.UsesPrebake), "Expected four local caches in Editor Play.");
                var result = bootstrap.traversalEvaluator.EvaluateMapPath(new Vector2(249, 300), new Vector2(251, 300));
                Require(result.HasData && result.IsPassable, $"Movement across the authored tile boundary failed: {result.BlockReason}, slope={result.MaximumSurfaceSlopeAngle}.");
                var scan = new TerrainScanProfile(map.RuntimeMap, new Vector2(249, 300), new Vector2(251, 300), map.ContourIntervalMeters, 16);
                while (!scan.Complete) scan.Step();
                Require(scan.Clear, "Cross-boundary runtime scan failed.");
                var type = typeof(MapTestSceneController).Assembly.GetType("AnimalGame.RobotMap.PhotoContourCapture");
                var capture = (IDisposable)Activator.CreateInstance(type, true);
                try
                {
                    var renderers = map.GetComponentsInChildren<SpriteRenderer>();
                    var originals = renderers.Select(r => r.sharedMaterial).ToArray();
                    bool captured = (bool)type.GetMethod("Capture").Invoke(capture, new object[] { bootstrap.mapCamera, map, 256 });
                    Require(captured, "Multi-tile photo capture failed.");
                    var texture = (RenderTexture)type.GetProperty("Texture").GetValue(capture);
                    Require(texture.width == 256 && texture.height == 256, "Photo output was not bounded.");
                    for (int i = 0; i < renderers.Length; i++) Require(renderers[i].sharedMaterial == originals[i], "Photo did not restore tile materials.");
                }
                finally { capture.Dispose(); }
                string report = "PASS: actual V1 Play; four 2048 fields and caches; one player/camera; cross-tile traversal and scan; multi-tile 256px photo capture. World: " + map.WorldBounds.size;
                System.IO.File.WriteAllText(SmokeReport, report);
                Debug.Log(report);
            }
            catch (Exception exception)
            {
                System.IO.File.WriteAllText(SmokeReport, "FAIL: " + exception);
                Debug.LogException(exception);
            }
            finally
            {
                SessionState.SetBool(SmokeKey, false);
                EditorApplication.ExitPlaymode();
            }
        }

        [MenuItem("Animal Game/Tiled Levels/Run Regression Checks")]
        public static void Run()
        {
            var owned = new List<Object>();
            GameObject root = null;
            try
            {
                var entries = new List<TiledLevelAsset.Tile>();
                for (int y = 0; y < 3; y++) for (int x = 0; x < 2; x++)
                {
                    var texture = new Texture2D(17, 17, TextureFormat.R16, false, true);
                    owned.Add(texture);
                    var colors = new ushort[17 * 17];
                    for (int row = 0; row < 17; row++) for (int column = 0; column < 17; column++)
                    {
                        float h = Height(x * 10 + column * 10f / 16, y * 10 + row * 10f / 16) / 70f;
                        colors[row * 17 + column] = (ushort)Mathf.RoundToInt(h * 65535);
                    }
                    texture.SetPixelData(colors, 0); texture.Apply();
                    var level = ScriptableObject.CreateInstance<HeightMapLevelAsset>();
                    owned.Add(level);
                    var serialized = new SerializedObject(level);
                    serialized.FindProperty("heightMap").objectReferenceValue = texture;
                    serialized.FindProperty("mapWidthMeters").floatValue = 10;
                    serialized.FindProperty("mapHeightMeters").floatValue = 10;
                    serialized.FindProperty("minimumHeightMeters").floatValue = 0;
                    serialized.FindProperty("maximumHeightMeters").floatValue = 70;
                    serialized.FindProperty("normalizeSourceRange").boolValue = false;
                    serialized.FindProperty("surfaceSmoothingSigmaMeters").floatValue = 0;
                    serialized.FindProperty("detailSmoothingSigmaMeters").floatValue = 0;
                    serialized.FindProperty("bakedHeightResolution").intValue = 128;
                    serialized.FindProperty("staticWaterResolution").intValue = 64;
                    serialized.FindProperty("maximumStaticWaterDepthMeters").floatValue = 10;
                    var water = serialized.FindProperty("staticWaterDepthMap");
                    water.arraySize = 64 * 64;
                    for (int i = 0; i < water.arraySize; i++) water.GetArrayElementAtIndex(i).intValue = (y * 2 + x + 1) * 20;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    entries.Add(new TiledLevelAsset.Tile(new Vector2Int(x, y), level));
                }
                var tiled = ScriptableObject.CreateInstance<TiledLevelAsset>();
                owned.Add(tiled);
                entries.Reverse(); // Lookup must not depend on list order.
                tiled.Configure(Vector2.one * 10, Vector2.one * 0.25f, entries[0].level, entries);
                Require(tiled.Validate(out _), "2 x 3 layout was rejected.");
                Require(tiled.MapSizeMeters == new Vector2(20, 30), "Rectangular map size.");
                using (var runtime = new TiledMapRuntime(tiled, false))
                {
                    foreach (Vector2 point in new[] { Vector2.zero, new Vector2(20, 30), new Vector2(10, 10),
                        new Vector2(9.999f, 20), new Vector2(10.001f, 20), new Vector2(14, 26) })
                    {
                        for (int channel = 0; channel < 3; channel++)
                        {
                            Require(runtime.TrySample(point, out float value, channel), "Missing height at " + point);
                            Near(value, Height(point.x, point.y), 0.001f, "Analytic height at " + point);
                        }
                    }
                    Require(!runtime.TrySample(new Vector2(-0.01f, 5), out _)
                        && !runtime.TrySample(new Vector2(20.01f, 5), out _)
                        && !runtime.TrySample(new Vector2(float.NaN, 0), out _), "Invalid coordinates accepted.");
                    runtime.TryLocate(new Vector2(10, 10), out var corner, out var local);
                    Require(corner.Coordinate == new Vector2Int(1, 1) && local == Vector2.zero, "Interior boundary ownership.");
                    Require(runtime.TrySampleWater(new Vector2(9.9f, 5), out float waterA)
                        && runtime.TrySampleWater(new Vector2(10.1f, 5), out float waterB), "Cross-tile water lookup.");
                    runtime.TrySampleWater(new Vector2(10.1f, 5), out float rightWater);
                    Near(waterA, 20f / 255 * 10, 0.0001f, "Left water depth.");
                    Near(rightWater, 40f / 255 * 10, 0.0001f, "Right water depth.");
                    var profile = new TerrainScanProfile(runtime, new Vector2(1, 1), new Vector2(19, 29), 7, 100);
                    while (!profile.Complete) profile.Step();
                    Require(profile.Clear && profile.SamplesRead == 100, "Cross-tile scan profile.");
                    Require(runtime.Tiles.All(t => t.Field.Width == 128 && t.Field.Height == 128), "Whole-world allocation detected.");

                    // Exercise cached and uncached tiles in the same resident map.
                    var source = runtime.Tiles.First();
                    var data = ScriptableObject.CreateInstance<HeightMapPrebakedData>(); owned.Add(data);
                    var raw = source.Field.CreateRawDetailTexture(); owned.Add(raw);
                    var surface = source.Field.CreateSurfaceTextureCopy(); owned.Add(surface);
                    data.SetBakedData(source.Level, source.Field.SourceMinimum, source.Field.SourceMaximum, raw, raw, surface);
                    source.Level.SetPrebakedHeightField(data);
                    using (var cached = new TiledMapRuntime(tiled, true))
                    {
                        Require(cached.Tiles.Count(t => t.UsesPrebake) == 1, "Mixed cache/fallback loading.");
                        cached.TrySample(new Vector2(14, 26), out float h);
                        Near(h, Height(14, 26), 0.001f, "Cached/fallback result.");
                    }
                    Require(surface != null, "Disposal destroyed borrowed cache texture.");
                }
                root = new GameObject("Tiled Regression Map"); root.SetActive(false);
                root.transform.position = new Vector3(8, -3, 0);
                var controller = root.AddComponent<MapTestSceneController>();
                var config = new SerializedObject(controller);
                config.FindProperty("levelAsset").objectReferenceValue = tiled;
                config.ApplyModifiedPropertiesWithoutUndo();
                root.SetActive(true);
                controller.SendMessage("RebuildGeneratedMap");
                Require(controller.HasGeneratedMap && controller.RuntimeMap.TileCount == 6, "Controller did not build six tiles.");
                Near(controller.WorldBounds.size.x, 5, 0.0001f, "Rendered width.");
                Near(controller.WorldBounds.size.y, 7.5f, 0.0001f, "Rendered height.");
                Require(root.GetComponentsInChildren<SpriteRenderer>().Length == 6, "Expected one renderer per tile.");
                Require(controller.TrySampleWorldPosition(controller.MapPositionToWorld(new Vector2(14, 26)), out var recovered, out float worldHeight), "World lookup failed.");
                Near((recovered - new Vector2(14, 26)).magnitude, 0, 0.0001f, "Coordinate round trip.");
                Near(worldHeight, Height(14, 26), 0.001f, "World height.");
                controller.SendMessage("RebuildGeneratedMap");
                Require(root.GetComponentsInChildren<SpriteRenderer>().Length == 6, "Rebuild leaked tile renderers.");
                Object.DestroyImmediate(root); root = null;
                var bad = entries.Take(5).ToList();
                tiled.Configure(Vector2.one * 10, Vector2.one, entries[0].level, bad);
                Require(!tiled.IsValid, "Layout hole accepted.");
                tiled.Configure(Vector2.one * 10, Vector2.one, entries[0].level, new[] { entries[0], entries[0] });
                Require(!tiled.IsValid, "Duplicate coordinate accepted.");
                tiled.Configure(Vector2.one * 10, Vector2.one, entries[0].level,
                    new[] { new TiledLevelAsset.Tile(new Vector2Int(-1, 0), entries[0].level) });
                Require(!tiled.IsValid, "Negative coordinate accepted.");
                tiled.Configure(Vector2.one * 10, Vector2.one * 0.25f, entries[0].level,
                    new[] { new TiledLevelAsset.Tile(Vector2Int.zero, entries[0].level) });
                Require(tiled.IsValid && tiled.MapSizeMeters == Vector2.one * 10, "Single-tile level rejected.");
                Debug.Log("PASS: Tiled levels: 2x3 layout, all height channels, boundaries, water, scan, mixed cache/fallback, render bounds, rebuild disposal and single-tile layout.");
            }
            finally
            {
                if (root != null) Object.DestroyImmediate(root);
                foreach (var asset in owned) if (asset != null) Object.DestroyImmediate(asset);
            }
        }
        private static float Height(float x, float y) => 10 + x * 0.02f + y * 0.01f;
        private static void Near(float actual, float expected, float tolerance, string message)
            => Require(Mathf.Abs(actual - expected) <= tolerance, message + $" Expected {expected}, got {actual}.");
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
