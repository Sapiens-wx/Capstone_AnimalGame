using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.MapTest
{
    /// <summary>Resident tile registry and coordinate queries. No full-world height allocation.</summary>
    public sealed class TiledMapRuntime : IDisposable, IMapHeightSource
    {
        public sealed class Tile
        {
            public readonly Vector2Int Coordinate;
            public readonly HeightMapLevelAsset Level;
            public readonly BakedHeightField Field;
            public readonly bool UsesPrebake;
            public Tile(Vector2Int coordinate, HeightMapLevelAsset level, BakedHeightField field, bool cached)
            { Coordinate = coordinate; Level = level; Field = field; UsesPrebake = cached; }
        }
        private readonly Dictionary<Vector2Int, Tile> resident = new Dictionary<Vector2Int, Tile>();
        private readonly Vector2 mapSize, tileSize;
        private readonly Vector2Int gridSize;
        public IEnumerable<Tile> Tiles => resident.Values;
        public Vector2 MapSizeMeters => mapSize;
        public int TileCount => resident.Count;
        public TiledLevelAsset Definition { get; }
        public TiledMapRuntime(TiledLevelAsset definition, bool usePrebakes, int resolutionLimit = int.MaxValue)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (!definition.Validate(out string error)) throw new ArgumentException(error);
            Definition = definition;
            mapSize = definition.MapSizeMeters;
            tileSize = definition.TileSizeMeters;
            gridSize = definition.GridSize;
            try
            {
                foreach (var entry in definition.Tiles)
                {
                    var level = entry.level;
                    var cache = usePrebakes ? level.PrebakedHeightField : null;
                    BakedHeightField field = cache != null && cache.Matches(level)
                        ? BakedHeightField.FromPrebakedData(cache) : null;
                    bool cached = field != null;
                    field ??= BakedHeightField.Bake(level.HeightMap, Mathf.Min(level.BakedHeightResolution, resolutionLimit),
                        level.MapSizeMeters, level.MinimumHeightMeters, level.MaximumHeightMeters,
                        level.NormalizeSourceRange, level.SurfaceSmoothingSigmaMeters, level.DetailSmoothingSigmaMeters,
                        level.UseHeightMapBorderMask, level.HeightMapBorderMaskThreshold, level.HeightMapBorderInsetMeters,
                        level.PlayableAreaMask);
                    resident.Add(entry.coordinate, new Tile(entry.coordinate, level, field, cached));
                }
            }
            catch { Dispose(); throw; }
        }
        public bool TryLocate(Vector2 position, out Tile tile, out Vector2 local)
        {
            tile = null;
            local = default;
            Vector2 size = mapSize;
            if (float.IsNaN(position.x) || float.IsNaN(position.y) || position.x < 0 || position.y < 0
                || position.x > size.x || position.y > size.y) return false;
            Vector2 step = tileSize;
            Vector2Int grid = gridSize;
            // Interior boundaries belong to the positive tile; the last outer edge stays in the last tile.
            var coordinate = new Vector2Int(Mathf.Min(Mathf.FloorToInt(position.x / step.x), grid.x - 1),
                Mathf.Min(Mathf.FloorToInt(position.y / step.y), grid.y - 1));
            if (!resident.TryGetValue(coordinate, out tile)) return false;
            local = position - Vector2.Scale(coordinate, step);
            return true;
        }
        public bool TrySample(Vector2 position, out float height, int channel = 0)
        {
            height = 0;
            if (!TryLocate(position, out var tile, out var local)) return false;
            Vector2 size = tileSize;
            var uv = new Vector2(local.x / size.x, local.y / size.y);
            if (!tile.Field.IsPlayable(uv)) return false;
            height = channel == 1 ? tile.Field.SampleDetailHeight(uv)
                : channel == 2 ? tile.Field.SampleRawDetailHeight(uv) : tile.Field.SampleSurfaceHeight(uv);
            return true;
        }
        public bool TrySampleWater(Vector2 position, out float depth)
        {
            depth = 0;
            if (!TrySample(position, out _) || !TryLocate(position, out var tile, out var local)) return false;
            depth = tile.Level.SampleStaticWaterDepth(local);
            return true;
        }
        public Vector2 SampleSpacing(Vector2 position)
        {
            return TryLocate(position, out var tile, out _) ? new Vector2(tile.Field.TexelSizeXMeters,
                tile.Field.TexelSizeYMeters) : Vector2.zero;
        }
        public void Dispose()
        {
            foreach (var tile in resident.Values) tile.Field.Dispose();
            resident.Clear();
        }
    }
}
