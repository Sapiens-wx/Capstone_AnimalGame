using System;
using AnimalGame.MapTest;
using UnityEngine;

namespace AnimalGame.World
{
    /// <summary>Terrain queries in logical map meters. World positions use the game's XY plane.</summary>
    public static class WorldTerrainQuery
    {
        /// <summary>
        /// Samples the tile under a map position. Actual static water overrides the painted surface.
        /// A playable, unpainted cell succeeds with Unknown; missing tiles/outside the map fail.
        /// </summary>
        public static bool TrySampleMapPosition(MapTestSceneController map, Vector2 position,
            out TerrainKind terrain)
        {
            terrain = TerrainKind.Unknown;
            if (map == null || !map.TrySampleMapPosition(position, out _)
                || !map.RuntimeMap.TryLocate(position, out var tile, out var local)) return false;
            terrain = TrySampleWater(map, position, out _)
                ? TerrainKind.Water : tile.Level.SampleSurfaceType(local);
            return true;
        }

        /// <summary>Pass an actor's transform.position to sample the terrain under its feet.</summary>
        public static bool TrySampleWorldPosition(MapTestSceneController map, Vector2 worldPosition,
            out TerrainKind terrain)
        {
            terrain = TerrainKind.Unknown;
            return map != null && map.TrySampleWorldPosition(worldPosition, out var position, out _)
                && TrySampleMapPosition(map, position, out terrain);
        }

        /// <summary>Requires actual static water, independently of any painted Water terrain.</summary>
        public static bool TrySampleWater(MapTestSceneController map, Vector2 position,
            out float depthMeters, float minimumDepthMeters = 0f)
        {
            depthMeters = 0f;
            return map != null && IsFinite(minimumDepthMeters)
                && map.TrySampleStaticWaterMapPosition(position, out depthMeters)
                && depthMeters > 0.0001f && depthMeters >= Mathf.Max(0f, minimumDepthMeters);
        }

        /// <summary>
        /// Finds the nearest sampled point of a kind using a full-map grid and local refinement.
        /// This is an approximate geometric search, not pathfinding; small regions may fall between samples.
        /// spacingMeters is clamped to at least 0.5m. acceptPosition can enforce occupancy or other rules.
        /// </summary>
        public static bool TryFindNearestPoint(MapTestSceneController map, Vector2 origin,
            TerrainKind terrain, float spacingMeters, out Vector2 position,
            Predicate<Vector2> acceptPosition = null)
        {
            position = Vector2.zero;
            if (!Enum.IsDefined(typeof(TerrainKind), terrain)) return false;
            return TryFindNearest(map, origin, spacingMeters,
                candidate => TrySampleMapPosition(map, candidate, out var kind) && kind == terrain
                    && (acceptPosition == null || acceptPosition(candidate)), out position);
        }

        /// <summary>Uses the same search, with actual water depth and optional occupancy constraints.</summary>
        public static bool TryFindNearestWaterPoint(MapTestSceneController map, Vector2 origin,
            float minimumDepthMeters, float spacingMeters, out Vector2 position,
            Predicate<Vector2> acceptPosition = null)
        {
            position = Vector2.zero;
            if (!IsFinite(minimumDepthMeters)) return false;
            return TryFindNearest(map, origin, spacingMeters,
                candidate => TrySampleWater(map, candidate, out _, minimumDepthMeters)
                    && (acceptPosition == null || acceptPosition(candidate)), out position);
        }

        private static bool TryFindNearest(MapTestSceneController map, Vector2 origin,
            float spacingMeters, Predicate<Vector2> matches, out Vector2 position)
        {
            position = Vector2.zero;
            if (map == null || map.RuntimeMap == null || !IsFinite(origin.x) || !IsFinite(origin.y)
                || !IsFinite(spacingMeters)) return false;
            if (matches(origin)) { position = origin; return true; }

            Vector2 size = map.MapSizeMeters;
            float spacing = Mathf.Max(0.5f, spacingMeters);
            float bestDistanceSquared = float.PositiveInfinity;
            Vector2 bestPosition = Vector2.zero;
            // Include outer edges even when spacing does not divide the map dimensions.
            int columns = Mathf.CeilToInt(size.x / spacing);
            int rows = Mathf.CeilToInt(size.y / spacing);
            for (int y = 0; y <= rows; y++)
                for (int x = 0; x <= columns; x++)
                    Consider(new Vector2(Mathf.Min(x * spacing, size.x), Mathf.Min(y * spacing, size.y)));

            if (float.IsPositiveInfinity(bestDistanceSquared)) return false;
            // Keep the refinement window fixed while choosing a better point inside it.
            Vector2 refinementCenter = bestPosition;
            float refinementStep = spacing * 0.25f;
            for (int y = -4; y <= 4; y++)
                for (int x = -4; x <= 4; x++)
                    Consider(refinementCenter + new Vector2(x * refinementStep, y * refinementStep));

            position = bestPosition;
            return true;

            void Consider(Vector2 candidate)
            {
                float distanceSquared = (candidate - origin).sqrMagnitude;
                if (distanceSquared >= bestDistanceSquared || !matches(candidate)) return;
                bestDistanceSquared = distanceSquared;
                bestPosition = candidate;
            }
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
