using System;
using UnityEngine;

namespace AnimalGame.MapTest
{
    /// <summary>Owns one tile's temporary rendering objects; borrows the tile height field and source textures.</summary>
    internal sealed class TiledMapTileView : IDisposable
    {
        public SpriteRenderer Renderer { get; private set; }
        public HeightMapLevelAsset Level => tile.Level;
        private readonly TiledMapRuntime.Tile tile;
        private Sprite sprite;
        private GameObject root;
        private Matrix4x4 grassMetersToWorld;
        private Vector4 grassTileOrigin;

        public TiledMapTileView(TiledMapRuntime.Tile tile) { this.tile = tile; }

        public void Build(Transform parent, TiledLevelAsset definition, Material sharedMaterial)
        {
            try
            {
                // The shader renders terrain from physical height and authored surface data.
                var carrier = Texture2D.blackTexture;
                sprite = Sprite.Create(carrier, new Rect(0, 0, carrier.width, carrier.height),
                    Vector2.one * 0.5f, 1, 0, SpriteMeshType.FullRect);
                sprite.hideFlags = HideFlags.DontSave;
                root = new GameObject("Map Tile " + tile.Coordinate) { hideFlags = HideFlags.DontSave };
                root.transform.SetParent(parent, false);
                Vector2 tileSize = definition.TileSizeMeters;
                grassMetersToWorld = parent.localToWorldMatrix
                    * Matrix4x4.Translate(-Vector2.Scale(definition.MapSizeMeters * .5f, definition.WorldUnitsPerMeter))
                    * Matrix4x4.Scale(new Vector3(definition.WorldUnitsPerMeter.x, definition.WorldUnitsPerMeter.y, 1));
                grassTileOrigin = new Vector4(tile.Coordinate.x * tileSize.x, tile.Coordinate.y * tileSize.y, 0, 0);
                Vector2 center = Vector2.Scale((Vector2)tile.Coordinate + Vector2.one * 0.5f, tileSize)
                    - definition.MapSizeMeters * 0.5f;
                root.transform.localPosition = Vector2.Scale(center, definition.WorldUnitsPerMeter);
                Vector2 worldSize = Vector2.Scale(tileSize, definition.WorldUnitsPerMeter);
                root.transform.localScale = new Vector3(worldSize.x / carrier.width, worldSize.y / carrier.height, 1);
                Renderer = root.AddComponent<SpriteRenderer>();
                Renderer.sprite = sprite;
                Renderer.sortingOrder = -1000;
                Renderer.sharedMaterial = sharedMaterial;
            }
            catch { Dispose(); throw; }
        }

        public void RefreshProperties(MaterialPropertyBlock properties)
        {
            Renderer.enabled = tile.Field != null;
            if (!Renderer.enabled) return;
            properties.Clear();
            var level = tile.Level;
            properties.SetTexture("_HeightTex", tile.Field.SurfaceTexture);
            properties.SetFloat("_MinimumHeight", level.MinimumHeightMeters);
            properties.SetFloat("_MaximumHeight", level.MaximumHeightMeters);
            properties.SetVector("_MapSizeMeters", new Vector4(level.MapSizeMeters.x, level.MapSizeMeters.y, 0, 0));
            bool hasSurface = level.TerrainCoverage != null;
            if (hasSurface)
            {
                properties.SetTexture("_TerrainCoverage", level.TerrainCoverage);
                properties.SetVectorArray("_TerrainSettings", level.TerrainPatternSettings);
                properties.SetVectorArray("_TerrainTints", level.TerrainPatternTints);
                properties.SetVector("_TerrainOrigin", grassTileOrigin);
                properties.SetFloat("_TerrainSeed", level.TerrainPatternSeed);
                var patterns = level.TerrainPatterns;
                var samplerPattern = patterns[(int)TerrainKind.GridTest];
                for (int id = 2; id < patterns.Length; id++)
                {
                    if (id == (int)TerrainKind.Grassland) continue;
                    var pattern = patterns[id];
                    if (samplerPattern == null) samplerPattern = pattern;
                    properties.SetTexture("_TerrainPattern" + id, pattern != null ? pattern : Texture2D.blackTexture);
                }
                // Every static pattern uses slot 1's sampler, even on maps without Grid Test.
                properties.SetTexture("_TerrainPattern1", samplerPattern != null ? samplerPattern : Texture2D.blackTexture);
            }
            properties.SetFloat("_SurfaceEnabled", hasSurface ? 1 : 0);
            bool grass = level.BakedGrasslandMask != null && level.GrasslandPattern != null;
            properties.SetFloat("_GrassEnabled", grass ? 1 : 0);
            if (grass)
            {
                properties.SetTexture("_GrassMaskTex", level.BakedGrasslandMask);
                properties.SetTexture("_GrassPatternTex", level.GrasslandPattern);
                Texture2D grass1 = level.Grass1;
                Texture2D grass2 = level.Grass2;
                Texture2D grass3 = level.Grass3;
                if (grass1 != null) properties.SetTexture("_GrassOverlay1", grass1);
                if (grass2 != null) properties.SetTexture("_GrassOverlay2", grass2);
                if (grass3 != null) properties.SetTexture("_GrassOverlay3", grass3);
                properties.SetVector("_GrassOverlaySettings", level.GrassOverlaySettings);
                properties.SetVector("_GrassContourOpacity", new Vector4(level.SurfaceClosedContourEdgeAlphaMultiplier,
                    level.SurfaceClosedContourCenterAlphaMultiplier,
                    level.SurfaceClosedContourEdgeAlphaMultiplier, level.SurfaceClosedContourCenterAlphaMultiplier));
                properties.SetVectorArray("_GrassRects", level.GrasslandBladeRects);
                properties.SetFloat("_GrassTileSize", level.GrasslandTileSizeMeters);
                properties.SetVector("_GrassLayout", level.GrasslandLayout);
                properties.SetVector("_GrassShape", level.GrasslandShape);
                Vector2 normalization = level.GrasslandAlphaNormalization;
                properties.SetVector("_GrassAlphaNormalization", new Vector4(normalization.x, normalization.y, 0, 0));
                properties.SetMatrix("_GrassMetersToWorld", grassMetersToWorld);
                properties.SetVector("_GrassTileOrigin", grassTileOrigin);
            }
            ConfigureWater(properties, tile);
            Renderer.SetPropertyBlock(properties);
        }

        public void RefreshNewlyLoaded(MaterialPropertyBlock properties)
        {
            if (!Renderer.enabled && tile.Field != null) RefreshProperties(properties);
        }

        internal static void ConfigureWater(MaterialPropertyBlock properties, TiledMapRuntime.Tile tile)
        {
            var level = tile.Level;
            bool water = level.BakedStaticWaterMask != null && level.StaticWaterTexture != null;
            properties.SetTexture("_WaterMaskTex", water ? level.BakedStaticWaterMask : Texture2D.blackTexture);
            properties.SetTexture("_WaterUvTex", level.BakedStaticWaterUv != null
                ? level.BakedStaticWaterUv : Texture2D.blackTexture);
            Rect waterBounds = level.BakedStaticWaterBounds;
            properties.SetVector("_WaterMaskBounds", new Vector4(
                waterBounds.x, waterBounds.y, waterBounds.width, waterBounds.height));
            properties.SetTexture("_WaterPatternTex", water ? level.StaticWaterTexture : Texture2D.blackTexture);
            properties.SetFloat("_WaterEnabled", water ? 1 : 0);
            if (!water) return;
            Vector2 waterOrigin = Vector2.Scale((Vector2)tile.Coordinate, level.MapSizeMeters);
            properties.SetVector("_WaterOriginMeters", new Vector4(waterOrigin.x, waterOrigin.y, 0, 0));
            properties.SetFloat("_WaterTileSizeMeters", level.StaticWaterTileSizeMeters);
            Vector2 one = level.StaticWaterLayerOneSpeedMetersPerSecond;
            Vector2 two = level.StaticWaterLayerTwoSpeedMetersPerSecond;
            properties.SetVector("_WaterLayerOneSpeed", new Vector4(one.x, one.y, 0, 0));
            properties.SetVector("_WaterLayerTwoSpeed", new Vector4(two.x, two.y, 0, 0));
            properties.SetFloat("_WaterLayerTwoScale", level.StaticWaterLayerTwoScale);
            properties.SetFloat("_WaterWaveDistortion", level.StaticWaterWaveDistortion);
            properties.SetFloat("_WaterWaveSpeed", level.StaticWaterWaveSpeed);
            properties.SetFloat("_WaterWaveLengthMeters", level.StaticWaterWaveLengthMeters);
        }

        public void Dispose()
        {
            Destroy(root); Destroy(sprite);
            root = null; sprite = null; Renderer = null;
        }
        private static void Destroy(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
