using System.Collections.Generic;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MapParams))]
internal sealed class MapParamsEditor : Editor
{
    private static readonly string[] HeightAndContourBakePaths =
    {
        "contourIntervalMeters", "normalizeSourceRange", "surfaceSmoothingSigmaMeters"
    };
    private const string MaskResolutionPath = "surfaceMaskResolution";

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "m_Script");
        if (!serializedObject.hasModifiedProperties)
            return;

        var settings = (MapParams)target;
        using var previous = new SerializedObject(settings);
        bool rebake = HasChanges(previous, HeightMapSurfaceSettings.BakedPaths)
                      || HasChanges(previous, HeightAndContourBakePaths);
        bool resize = !SerializedProperty.DataEquals(previous.FindProperty(MaskResolutionPath),
            serializedObject.FindProperty(MaskResolutionPath));
        serializedObject.ApplyModifiedProperties();

        if (rebake)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:HeightMapLevelAsset"))
            {
                var tile = AssetDatabase.LoadAssetAtPath<HeightMapLevelAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (tile == null || tile.MapParams != settings)
                    continue;

                Undo.RegisterCompleteObjectUndo(tile, "Change Shared Terrain Settings");
                if (resize)
                    tile.EnsureSurfaceAuthoringData();
                tile.InvalidateSurfaceBake();
                EditorUtility.SetDirty(tile);
            }
        }

        SceneView.RepaintAll();
    }

    private bool HasChanges(SerializedObject previous, IReadOnlyList<string> paths)
    {
        foreach (string path in paths)
        {
            if (!SerializedProperty.DataEquals(previous.FindProperty(path), serializedObject.FindProperty(path)))
                return true;
        }
        return false;
    }

    internal static void Assign(HeightMapLevelAsset tile, MapParams settings)
    {
        if (tile.MapParams == settings)
            return;

        Undo.RegisterCompleteObjectUndo(tile, "Assign Map Params");
        tile.AssignMapParams(settings);
    }
}
