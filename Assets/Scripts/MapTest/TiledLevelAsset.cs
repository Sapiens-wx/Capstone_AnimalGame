using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.MapTest
{
    [CreateAssetMenu(menuName = "Animal Game/Tiled Level")]
    public sealed class TiledLevelAsset : ScriptableObject
    {
        [Serializable]
        public sealed class Tile
        {
            public Vector2Int coordinate;
            public HeightMapLevelAsset level;
            public Tile(Vector2Int coordinate, HeightMapLevelAsset level)
            { this.coordinate = coordinate; this.level = level; }
        }

        [SerializeField] private Vector2 tileSizeMeters = new Vector2(250, 250);
        [Tooltip("Explicit source of shared contour, color and traversal settings. Tile textures/water remain local.")]
        [SerializeField] private HeightMapLevelAsset presentation;
        [SerializeField] private Vector2 worldUnitsPerMeter = Vector2.one * (2048f / 30f / 250f);
        [SerializeField] private List<Tile> tiles = new List<Tile>();
        public IReadOnlyList<Tile> Tiles => tiles;
        public HeightMapLevelAsset Presentation => presentation;
        public Vector2 TileSizeMeters => tileSizeMeters;
        public Vector2 WorldUnitsPerMeter => worldUnitsPerMeter;
        public Vector2Int GridSize
        {
            get
            {
                var size = Vector2Int.zero;
                foreach (var tile in tiles)
                    if (tile != null) size = Vector2Int.Max(size, tile.coordinate + Vector2Int.one);
                return size;
            }
        }
        public Vector2 MapSizeMeters => Vector2.Scale(GridSize, tileSizeMeters);
        public bool IsValid => Validate(out _);
        public bool Validate(out string error)
        {
            error = null;
            if (presentation == null || !presentation.IsValid || tiles == null || tiles.Count == 0)
                error = "Assign a presentation source and at least one valid tile.";
            else if (!FinitePositive(tileSizeMeters.x) || !FinitePositive(tileSizeMeters.y)
                     || !FinitePositive(worldUnitsPerMeter.x) || !FinitePositive(worldUnitsPerMeter.y))
                error = "Tile dimensions and world units per meter must be finite and positive.";
            else
            {
                var coordinates = new HashSet<Vector2Int>();
                foreach (var tile in tiles)
                {
                    if (tile == null || tile.level == null || !tile.level.IsValid)
                    { error = "Every tile must reference a valid height-map level."; break; }
                    if (tile.coordinate.x < 0 || tile.coordinate.y < 0 || !coordinates.Add(tile.coordinate))
                    { error = "Tile coordinates must be unique and nonnegative."; break; }
                    if ((tile.level.MapSizeMeters - tileSizeMeters).sqrMagnitude > 0.000001f)
                    { error = "All tiles must match the declared tile dimensions."; break; }
                }
                Vector2Int size = GridSize;
                if (error == null && (long)size.x * size.y != tiles.Count)
                    error = "Tiles must fill a rectangle starting at (0,0), without holes.";
            }
            return error == null;
        }
        private static bool FinitePositive(float value) => value > 0 && !float.IsInfinity(value) && !float.IsNaN(value);
        public int ConfigurationHash
        {
            get
            {
                unchecked
                {
                    int hash = tileSizeMeters.GetHashCode() ^ worldUnitsPerMeter.GetHashCode();
                    hash = hash * 397 ^ (presentation != null ? presentation.ConfigurationHash : 0);
                    foreach (var tile in tiles)
                    {
                        hash = hash * 397 ^ (tile != null ? tile.coordinate.GetHashCode() : 0);
                        hash = hash * 397 ^ (tile?.level != null ? tile.level.ConfigurationHash : 0);
                    }
                    return hash;
                }
            }
        }
        public bool Contains(HeightMapLevelAsset level)
        {
            foreach (var tile in tiles) if (tile.level == level) return true;
            return false;
        }
#if UNITY_EDITOR
        public void Configure(Vector2 size, Vector2 scale, HeightMapLevelAsset settings, IEnumerable<Tile> entries)
        {
            tileSizeMeters = size;
            worldUnitsPerMeter = scale;
            presentation = settings;
            tiles = new List<Tile>(entries);
        }
#endif
    }
}
