using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.MapTest
{
    /// <summary>Tile registry, height loading and coordinate queries.</summary>
    public sealed class TiledMapRuntime : IDisposable, IMapHeightSource
    {
        public sealed class Tile
        {
            public readonly Vector2Int Coordinate;
            public readonly HeightMapLevelAsset Level;
            public BakedHeightField Field { get; private set; }
            public bool UsesPrebake { get; private set; }
            internal Tile(Vector2Int coordinate, HeightMapLevelAsset level)
            { Coordinate = coordinate; Level = level; }

            internal void SetHeightField(BakedHeightField field, bool cached)
            { Field = field; UsesPrebake = cached; }
        }
        private readonly Dictionary<Vector2Int, Tile> resident = new Dictionary<Vector2Int, Tile>();
        private readonly List<(Tile Tile, ResourceRequest Request)> pending = new List<(Tile, ResourceRequest)>();
        private readonly Vector2 mapSize, tileSize;
        private readonly Vector2Int gridSize;
        private readonly int resolutionLimit;
        private readonly bool usePrebakes;
        public IEnumerable<Tile> Tiles => resident.Values;
        public Vector2 MapSizeMeters => mapSize;
        public int TileCount => resident.Count;
        public bool HasPendingLoads => pending.Count > 0;
        public TiledLevelAsset Definition { get; }
        public TiledMapRuntime(TiledLevelAsset definition, bool usePrebakes, int resolutionLimit = int.MaxValue,
            Vector2? spawnPosition = null)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (!definition.Validate(out string error)) throw new ArgumentException(error);
            Definition = definition;
            mapSize = definition.MapSizeMeters;
            tileSize = definition.TileSizeMeters;
            gridSize = definition.GridSize;
            this.resolutionLimit = resolutionLimit;
            this.usePrebakes = usePrebakes;
            try
            {
                foreach (var entry in definition.Tiles)
                    resident.Add(entry.coordinate, new Tile(entry.coordinate, entry.level));

                Tile spawnTile = null;
                if (usePrebakes && spawnPosition.HasValue)
                {
                    Vector2 spawn = Vector2.Min(Vector2.Max(spawnPosition.Value, Vector2.zero), mapSize);
                    if (!TryLocate(spawn, out spawnTile, out _))
                        spawnTile = resident[definition.Tiles[0].coordinate];
                    BuildField(spawnTile, spawnTile.Level.LoadPrebakedHeightField());
                }

                foreach (var tile in resident.Values)
                {
                    if (tile == spawnTile) continue;
                    var request = spawnTile != null ? tile.Level.RequestPrebakedHeightField() : null;
                    if (request != null) pending.Add((tile, request));
                    else BuildField(tile, usePrebakes ? tile.Level.LoadPrebakedHeightField() : null);
                }
            }
            catch { Dispose(); throw; }
        }

        public bool CompletePendingLoads()
        {
            bool changed = false;
            for (int index = pending.Count - 1; index >= 0; index--)
            {
                var (tile, request) = pending[index];
                if (!request.isDone) continue;
                var cache = request.asset as HeightMapPrebakedData;
                BuildField(tile, cache);
                tile.Level.AssignLoadedPrebake(tile.UsesPrebake ? cache : null);
                pending.RemoveAt(index);
                changed = true;
            }
            return changed;
        }

        private void BuildField(Tile tile, HeightMapPrebakedData cache)
        {
            var level = tile.Level;
            var field = cache != null && cache.Matches(level) ? BakedHeightField.FromPrebakedData(cache) : null;
            bool cached = field != null;
            field ??= BakedHeightField.Bake(level.HeightMap, Mathf.Min(level.BakedHeightResolution, resolutionLimit),
                level.MapSizeMeters, level.MinimumHeightMeters, level.MaximumHeightMeters,
                level.NormalizeSourceRange, level.SurfaceSmoothingSigmaMeters, level.DetailSmoothingSigmaMeters);
            tile.SetHeightField(field, cached);
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
            if (!TryLocate(position, out var tile, out var local) || tile.Field == null) return false;
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
        public bool TrySampleWaterFlow(Vector2 position, out Vector2 flow)
        {
            flow = Vector2.zero;
            return TrySample(position, out _) && TryLocate(position, out var tile, out var local)
                && tile.Level.TrySampleStaticWaterFlow(local, out flow);
        }
        public Vector2 SampleSpacing(Vector2 position)
        {
            return TryLocate(position, out var tile, out _) && tile.Field != null ? new Vector2(tile.Field.TexelSizeXMeters,
                tile.Field.TexelSizeYMeters) : Vector2.zero;
        }
        public void Dispose()
        {
            foreach (var tile in resident.Values)
            {
                tile.Field?.Dispose();
                if (usePrebakes) tile.Level.AssignLoadedPrebake(null);
            }
            pending.Clear();
            resident.Clear();
        }
    }
}
