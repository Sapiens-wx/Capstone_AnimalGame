using System;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEngine;

namespace AnimalGame.Editor
{
    /// <summary>Builds a real single-tile map for gameplay regression fixtures.</summary>
    internal sealed class TiledMapTestFixture : IDisposable
    {
        private readonly MapTestSceneController controller;
        private readonly HeightMapLevelAsset tile;
        private readonly TiledLevelAsset level;
        public TiledMapTestFixture(MapTestSceneController controller, Texture2D texture,
            float sizeMeters, float worldUnitsPerMeter, float maximumHeight = 1)
        {
            this.controller = controller;
            tile = ScriptableObject.CreateInstance<HeightMapLevelAsset>();
            var settings = new SerializedObject(tile);
            settings.FindProperty("heightMap").objectReferenceValue = texture;
            settings.FindProperty("mapWidthMeters").floatValue = sizeMeters;
            settings.FindProperty("mapHeightMeters").floatValue = sizeMeters;
            settings.FindProperty("minimumHeightMeters").floatValue = 0;
            settings.FindProperty("maximumHeightMeters").floatValue = maximumHeight;
            settings.FindProperty("normalizeSourceRange").boolValue = false;
            settings.FindProperty("surfaceSmoothingSigmaMeters").floatValue = 0;
            settings.FindProperty("bakedHeightResolution").intValue = 128;
            settings.ApplyModifiedPropertiesWithoutUndo();
            level = ScriptableObject.CreateInstance<TiledLevelAsset>();
            level.Configure(Vector2.one * sizeMeters, Vector2.one * worldUnitsPerMeter, tile,
                new[] { new TiledLevelAsset.Tile(Vector2Int.zero, tile) });
            var map = new SerializedObject(controller);
            map.FindProperty("levelAsset").objectReferenceValue = level;
            map.ApplyModifiedPropertiesWithoutUndo();
            controller.SendMessage("RebuildGeneratedMap");
        }
        public void Dispose()
        {
            if (controller != null) controller.SendMessage("ReleaseGeneratedMap");
            UnityEngine.Object.DestroyImmediate(level);
            UnityEngine.Object.DestroyImmediate(tile);
        }
    }
}
