using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AnimalGame.Editor
{
    internal static class OfficialMapWaterTerrain
    {
        internal const int TileSize = 250;
        private static int sceneHandle;
        private static Terrain[] tiles = Array.Empty<Terrain>();
        private static TerrainCollider[] colliders = Array.Empty<TerrainCollider>();
        private static Dictionary<Vector2Int, Terrain> byOrigin;

        static OfficialMapWaterTerrain() => EditorApplication.hierarchyChanged += () => byOrigin = null;

        internal static IReadOnlyList<Terrain> Tiles
        {
            get { Refresh(); return tiles; }
        }

        internal static Vector2Int OriginOf(Terrain terrain) => new Vector2Int(
            Mathf.RoundToInt(terrain.transform.position.x), Mathf.RoundToInt(terrain.transform.position.z));

        internal static Vector2Int TileOriginAt(int x, int z) => new Vector2Int(
            Mathf.FloorToInt(x / (float)TileSize) * TileSize, Mathf.FloorToInt(z / (float)TileSize) * TileSize);

        internal static bool TryTile(Vector2Int origin, out Terrain terrain)
        {
            Refresh();
            return byOrigin.TryGetValue(origin, out terrain);
        }

        internal static bool TryGround(int x, int z, out float height)
        {
            var origin = TileOriginAt(x, z);
            bool onWest = x == origin.x;
            bool onSouth = z == origin.y;
            if (TryTile(origin, out Terrain terrain)
                || onWest && TryTile(origin + new Vector2Int(-TileSize, 0), out terrain)
                || onSouth && TryTile(origin + new Vector2Int(0, -TileSize), out terrain)
                || onWest && onSouth && TryTile(origin - Vector2Int.one * TileSize, out terrain))
            {
                Vector3 position = terrain.transform.position;
                height = position.y + terrain.terrainData.GetInterpolatedHeight((x - position.x) / TileSize, (z - position.z) / TileSize);
                return true;
            }
            height = 0f;
            return false;
        }

        internal static bool TryHit(Ray ray, out Vector3 point)
        {
            Refresh();
            float distance = float.PositiveInfinity;
            point = default;
            foreach (TerrainCollider collider in colliders)
            {
                if (!collider.enabled || !collider.gameObject.activeInHierarchy
                    || !collider.bounds.IntersectRay(ray, out float entry) || entry > distance) continue;
                if (!collider.Raycast(ray, out RaycastHit hit, distance) || hit.distance >= distance) continue;
                distance = hit.distance;
                point = hit.point;
            }
            return float.IsFinite(distance);
        }

        private static void Refresh()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (byOrigin != null && sceneHandle == scene.handle) return;
            sceneHandle = scene.handle;
            tiles = OfficialMapWaterPreview.IsAuthoringScene(scene)
                ? OfficialPlainsGameplay.GetSourceTerrains(true) : Array.Empty<Terrain>();
            colliders = tiles.Select(terrain => terrain.GetComponent<TerrainCollider>()).ToArray();
            byOrigin = tiles.ToDictionary(OriginOf);
        }
    }
}
