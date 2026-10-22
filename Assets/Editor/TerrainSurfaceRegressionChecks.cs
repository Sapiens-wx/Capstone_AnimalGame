using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class TerrainSurfaceRegressionChecks
{
    [MenuItem("Animal Game/Validation/Run Terrain Surface Checks")]
    public static void Run()
    {
        CheckPaintingAndValidation();
        CheckLegacySerialization();
        Debug.Log("Terrain surface checks PASS: shared enum, painting/erasing, duplicate validation, legacy palette and mask preservation.");
    }

    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static void CheckPaintingAndValidation()
    {
        var level = ScriptableObject.CreateInstance<HeightMapLevelAsset>();
        try
        {
            var palette = new List<TerrainSurfaceDefinition>();
            foreach (TerrainKind terrain in Enum.GetValues(typeof(TerrainKind)))
            {
                if (terrain != TerrainKind.Unknown)
                    palette.Add(new TerrainSurfaceDefinition(terrain, terrain.ToString(), null, Color.white, 1f, 8f));
            }
            Set(level, "surfacePalette", palette);
            Set(level, "surfaceMaskResolution", 64);
            Set(level, "surfaceDataVersion", 1);
            level.EnsureSurfaceAuthoringData();
            Require(level.TryValidateSurfacePalette(out _), "A unique shared palette must be valid.");
            var serialized = new SerializedObject(level);
            Require(serialized.FindProperty("surfacePalette").GetArrayElementAtIndex(0)
                .FindPropertyRelative("terrain").propertyType == SerializedPropertyType.Enum,
                "The palette must expose an enum selector.");

            foreach (TerrainKind terrain in Enum.GetValues(typeof(TerrainKind)))
            {
                level.FillSurfaceType(terrain);
                Require(level.GetOrCreateSurfaceTypeMap().All(value => value == (byte)terrain),
                    $"Fill failed for {terrain}.");
            }
            level.FillSurfaceType(TerrainKind.Unknown);
            Require(level.PaintSurfaceType(Vector2.zero, 10f, TerrainKind.Rock), "Rock brush did not paint.");
            Require(level.GetOrCreateSurfaceTypeMap()[0] == (byte)TerrainKind.Rock, "Rock brush stored the wrong terrain.");
            Require(level.PaintSurfaceType(Vector2.zero, 10f, TerrainKind.Unknown), "Erase brush did not erase.");
            Require(level.GetOrCreateSurfaceTypeMap()[0] == 0, "Erased terrain must be Unknown.");
            Require(!level.FillSurfaceType((TerrainKind)255), "Undefined terrain was accepted.");

            palette.Add(new TerrainSurfaceDefinition(TerrainKind.Rock, "Duplicate", null, Color.white, 1f, 8f));
            byte[] before = (byte[])level.GetOrCreateSurfaceTypeMap().Clone();
            level.EnsureSurfaceAuthoringData();
            Require(palette[palette.Count - 1].Terrain == TerrainKind.Rock, "Validation silently reassigned a terrain kind.");
            Require(!level.TryValidateSurfacePalette(out _), "Duplicate terrain was accepted.");
            Require(!level.FillSurfaceType(TerrainKind.Grass), "An invalid palette was allowed to paint.");
            Require(before.SequenceEqual(level.GetOrCreateSurfaceTypeMap()), "Invalid painting changed the terrain mask.");
        }
        finally { Object.DestroyImmediate(level); }
    }

    private static void CheckLegacySerialization()
    {
        var source = ScriptableObject.CreateInstance<HeightMapLevelAsset>();
        string path = AssetDatabase.GenerateUniqueAssetPath("Assets/TerrainSurfaceLegacyCheck.asset");
        try
        {
            Require(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(MonoScript.FromScriptableObject(source),
                out string guid, out long scriptFileId), "Could not resolve the map script for the legacy asset.");
            // Use the old field name and byte encoding, as saved by existing maps.
            string mask = string.Concat(Enumerable.Repeat("06030700", 1024));
            File.WriteAllText(path,
                "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\nMonoBehaviour:\n" +
                "  m_ObjectHideFlags: 0\n  m_Script: {fileID: " + scriptFileId + ", guid: " + guid + ", type: 3}\n" +
                "  m_Name: Legacy Terrain Check\n  surfacePalette:\n" +
                "  - terrainId: 6\n    displayName: Legacy Rock\n" +
                "  - terrainId: 3\n    displayName: Legacy Grass\n" +
                "  - terrainId: 7\n    displayName: Legacy Water\n" +
                "  surfaceMaskResolution: 64\n  surfaceDataVersion: 1\n  surfaceMask: " + mask + "\n");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imported = AssetDatabase.LoadAssetAtPath<HeightMapLevelAsset>(path);
            Require(imported != null, "Legacy map failed to import.");
            Require(imported.SurfacePalette.Select(entry => entry.Terrain).SequenceEqual(
                new[] { TerrainKind.Rock, TerrainKind.Grass, TerrainKind.Water }), "Legacy palette migration changed terrain meanings.");
            byte[] before = (byte[])imported.GetOrCreateSurfaceTypeMap().Clone();
            imported.EnsureSurfaceAuthoringData();
            Require(before.SequenceEqual(imported.GetOrCreateSurfaceTypeMap()), "Legacy painted mask changed during migration.");
            Require(before.Length == 4096 && before[0] == (byte)TerrainKind.Rock
                && before[1] == (byte)TerrainKind.Grass && before[2] == (byte)TerrainKind.Water && before[3] == 0,
                "Legacy mask no longer matches the shared terrain enum.");
        }
        finally
        {
            AssetDatabase.DeleteAsset(path);
            Object.DestroyImmediate(source);
        }
    }

    private static void Set(object target, string field, object value) => target.GetType()
        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
