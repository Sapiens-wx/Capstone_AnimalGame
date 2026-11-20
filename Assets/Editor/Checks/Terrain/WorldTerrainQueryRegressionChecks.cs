using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using AnimalGame.MapTest;
using AnimalGame.World;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class WorldTerrainQueryRegressionChecks
{
    [MenuItem("Animal Game/Validation/Run World Terrain Query Checks")]
    public static void Run()
    {
        var owned = new List<Object>();
        var root = new GameObject("Terrain Query Checks");
        root.SetActive(false);
        TiledMapRuntime runtime = null;
        try
        {
            var height = new Texture2D(2, 2, TextureFormat.R16, false, true);
            height.SetPixelData(new ushort[] { 32768, 32768, 32768, 32768 }, 0); height.Apply();
            owned.Add(height);
            var entries = new List<TiledLevelAsset.Tile>();
            for (int i = 0; i < 2; i++)
            {
                var level = ScriptableObject.CreateInstance<HeightMapLevelAsset>(); owned.Add(level);
                Set(level, "heightMap", height);
                Set(level, "mapWidthMeters", 10f); Set(level, "mapHeightMeters", 10f);
                Set(level, "bakedHeightResolution", 128);
                Set(level, "surfaceSmoothingSigmaMeters", 0f); Set(level, "detailSmoothingSigmaMeters", 0f);
                Set(level, "surfaceMaskResolution", 64); Set(level, "surfaceDataVersion", 1);
                Set(level, "surfaceMask", Filled(64 * 64, (byte)(i == 0 ? TerrainKind.Grass : TerrainKind.Rock)));
                Set(level, "staticWaterDepthMap", Filled(64 * 64, 0));
                Set(level, "maximumStaticWaterDepthMeters", 10f);
                entries.Add(new TiledLevelAsset.Tile(new Vector2Int(i, 0), level));
            }
            var definition = ScriptableObject.CreateInstance<TiledLevelAsset>(); owned.Add(definition);
            definition.Configure(Vector2.one * 10, new Vector2(2, 3), entries[0].level, entries);
            runtime = new TiledMapRuntime(definition, false);
            var map = root.AddComponent<MapTestSceneController>();
            Set(map, "levelAsset", definition); Set(map, "runtimeMap", runtime);
            Set(map, "mapWidthMeters", 20f); Set(map, "mapHeightMeters", 10f);
            // Register render-free tile views so world-coordinate conversion has the same bounds as a built map.
            var views = (IList)Field(map, "tileViews").GetValue(map);
            var viewType = typeof(MapTestSceneController).Assembly.GetType("AnimalGame.MapTest.TiledMapTileView");
            foreach (var tile in runtime.Tiles) views.Add(Activator.CreateInstance(viewType, tile));
            root.transform.position = new Vector3(31, 47, 0);

            Sample(map, new Vector2(9.9f, 5), TerrainKind.Grass);
            Sample(map, new Vector2(10, 5), TerrainKind.Rock);
            Sample(map, new Vector2(20, 10), TerrainKind.Rock);
            Require(!WorldTerrainQuery.TrySampleMapPosition(map, new Vector2(-1, 0), out _), "Outside map accepted.");
            Require(WorldTerrainQuery.TrySampleWorldPosition(map, map.MapPositionToWorld(new Vector2(12, 5)), out var kind)
                && kind == TerrainKind.Rock, "World XY sampling ignored map translation or scale.");
            Require(WorldTerrainQuery.TryFindNearestPoint(map, new Vector2(3, 5), TerrainKind.Grass, 2, out var point)
                && point == new Vector2(3, 5), "Matching origin must have zero distance.");
            Require(WorldTerrainQuery.TryFindNearestPoint(map, new Vector2(9, 5), TerrainKind.Rock, 2, out point)
                && point == new Vector2(10, 5), "Wrong nearest point across tile seam.");
            Require(WorldTerrainQuery.TryFindNearestPoint(map, new Vector2(9, 5), TerrainKind.Rock, 2, out point,
                p => p.x >= 14) && point.x >= 14, "Occupancy predicate ignored.");
            Require(!WorldTerrainQuery.TryFindNearestPoint(map, Vector2.zero, TerrainKind.Web, 2, out _), "Absent terrain found.");
            Require(!WorldTerrainQuery.TryFindNearestPoint(map, Vector2.zero, TerrainKind.Rock, float.NaN, out _), "NaN spacing accepted.");
            Require(!WorldTerrainQuery.TryFindNearestWaterPoint(map, Vector2.zero, 0, 2, out _), "Dry ground counted as water.");

            Set(entries[1].level, "staticWaterDepthMap", Filled(64 * 64, 51)); // Exactly 2m.
            Sample(map, new Vector2(12, 5), TerrainKind.Water);
            Require(WorldTerrainQuery.TryFindNearestWaterPoint(map, new Vector2(9, 5), 1, 2, out point)
                && point == new Vector2(10, 5), "Depth-constrained water search failed.");
            Require(!WorldTerrainQuery.TryFindNearestWaterPoint(map, Vector2.zero, 3, 2, out _), "Shallow water passed minimum depth.");
            Set(entries[0].level, "surfaceMask", Filled(64 * 64, (byte)TerrainKind.Water));
            Sample(map, new Vector2(3, 5), TerrainKind.Water);
            Require(!WorldTerrainQuery.TrySampleWater(map, new Vector2(3, 5), out _), "Painted Water invented physical depth.");
            Set(entries[0].level, "surfaceMask", Filled(64 * 64, 0));
            Sample(map, new Vector2(3, 5), TerrainKind.Unknown);
            Require(!WorldTerrainQuery.TrySampleMapPosition(null, Vector2.zero, out _), "Null map accepted.");
            Debug.Log("World terrain query checks PASS: tiled/world sampling, nearest search, occupancy, Unknown, water depth and invalid inputs.");
        }
        finally
        {
            Object.DestroyImmediate(root);
            runtime?.Dispose();
            foreach (var item in owned) Object.DestroyImmediate(item);
        }
    }

    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static byte[] Filled(int count, byte value)
    { var bytes = new byte[count]; for (int i = 0; i < count; i++) bytes[i] = value; return bytes; }
    private static void Sample(MapTestSceneController map, Vector2 point, TerrainKind expected) =>
        Require(WorldTerrainQuery.TrySampleMapPosition(map, point, out var kind) && kind == expected, "Wrong terrain at " + point);
    private static FieldInfo Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
    private static void Set(object target, string name, object value) => Field(target, name).SetValue(target, value);
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
}
