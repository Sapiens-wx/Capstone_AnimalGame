using System;
using UnityEngine;

namespace AnimalGame.MapTest
{
    /// <summary>Owns one tile's temporary rendering objects; borrows the tile height field and source textures.</summary>
    internal sealed class TiledMapTileView : IDisposable
    {
        public SpriteRenderer Renderer { get; private set; }
        private readonly TiledMapRuntime.Tile tile;
        private Sprite sprite;
        private Material material;
        private GameObject root;

        public TiledMapTileView(TiledMapRuntime.Tile tile) { this.tile = tile; }

        public void Build(Transform parent, TiledLevelAsset definition, Material sharedSettings)
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
                Vector2 center = Vector2.Scale((Vector2)tile.Coordinate + Vector2.one * 0.5f, tileSize)
                    - definition.MapSizeMeters * 0.5f;
                root.transform.localPosition = Vector2.Scale(center, definition.WorldUnitsPerMeter);
                Vector2 worldSize = Vector2.Scale(tileSize, definition.WorldUnitsPerMeter);
                root.transform.localScale = new Vector3(worldSize.x / carrier.width, worldSize.y / carrier.height, 1);
                Renderer = root.AddComponent<SpriteRenderer>();
                Renderer.sprite = sprite;
                Renderer.sortingOrder = -1000;
                material = new Material(sharedSettings) { hideFlags = HideFlags.DontSave, name = "Tile Contours " + tile.Coordinate };
                Renderer.sharedMaterial = material;
                Refresh(sharedSettings);
            }
            catch { Dispose(); throw; }
        }

        public void Refresh(Material sharedSettings)
        {
            material.CopyPropertiesFromMaterial(sharedSettings);
            var level = tile.Level;
            material.SetTexture("_HeightTex", tile.Field.SurfaceTexture);
            material.SetFloat("_MinimumHeight", level.MinimumHeightMeters);
            material.SetFloat("_MaximumHeight", level.MaximumHeightMeters);
            material.SetTexture("_PlayableMaskTex", tile.Field.PlayableMaskTexture ?? Texture2D.whiteTexture);
            material.SetFloat("_PlayableMaskEnabled", tile.Field.PlayableMaskTexture != null ? 1 : 0);
            material.SetVector("_MapSizeMeters", new Vector4(level.MapSizeMeters.x, level.MapSizeMeters.y, 0, 0));
            material.SetTexture("_SurfaceTex", level.BakedSurfaceVisual ?? Texture2D.blackTexture);
            material.SetFloat("_SurfaceEnabled", level.BakedSurfaceVisual != null ? 1 : 0);
            bool water = level.BakedStaticWaterMask != null && level.StaticWaterTexture != null;
            material.SetTexture("_WaterMaskTex", water ? level.BakedStaticWaterMask : Texture2D.blackTexture);
            material.SetTexture("_WaterPatternTex", water ? level.StaticWaterTexture : Texture2D.blackTexture);
            material.SetFloat("_WaterEnabled", water ? 1 : 0);
            if (!water) return;
            material.SetFloat("_WaterTileSizeMeters", level.StaticWaterTileSizeMeters);
            Vector2 one = level.StaticWaterLayerOneSpeedMetersPerSecond;
            Vector2 two = level.StaticWaterLayerTwoSpeedMetersPerSecond;
            material.SetVector("_WaterLayerOneSpeed", new Vector4(one.x, one.y, 0, 0));
            material.SetVector("_WaterLayerTwoSpeed", new Vector4(two.x, two.y, 0, 0));
            material.SetFloat("_WaterLayerTwoScale", level.StaticWaterLayerTwoScale);
            material.SetFloat("_WaterWaveDistortion", level.StaticWaterWaveDistortion);
            material.SetFloat("_WaterWaveSpeed", level.StaticWaterWaveSpeed);
            material.SetFloat("_WaterWaveLengthMeters", level.StaticWaterWaveLengthMeters);
            material.SetFloat("_WaterDeepSpeedMultiplier", level.StaticWaterDeepSpeedMultiplier);
        }
        public void Dispose()
        {
            Destroy(root); Destroy(material); Destroy(sprite);
            root = null; material = null; sprite = null; Renderer = null;
        }
        private static void Destroy(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
