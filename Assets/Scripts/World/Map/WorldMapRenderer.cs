using System;
using AnimalGame.MapTest;
using UnityEngine;
using UnityEngine.Rendering;

namespace AnimalGame.World.Map
{
    internal sealed class WorldMapRenderer : IDisposable
    {
        private readonly Material material;
        private readonly Mesh quad;
        private readonly CommandBuffer commands = new CommandBuffer { name = "World map contours" };
        private readonly CommandBuffer waterCommands = new CommandBuffer { name = "World map currents" };
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private const int WaterFramesPerSecond = 24;
        private int waterFrame;
        private RenderTexture faultTexture;
        private RenderTexture revealTexture;
        private Rect visibleMap;
        public RenderTexture Texture { get; private set; }

        public WorldMapRenderer(Shader shader)
        {
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            quad = new Mesh { name = "World map tile", hideFlags = HideFlags.HideAndDontSave };
            quad.vertices = new[] { Vector3.zero, Vector3.right, new Vector3(1, 1), Vector3.up };
            quad.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        }

        public void Render(MapTestSceneController map, MapViewState state, int width, int height, Texture fog = null)
        {
            if (Texture == null || Texture.width != width || Texture.height != height)
            {
                ReleaseTexture(Texture);
                Texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
                { name = "World map contours", hideFlags = HideFlags.HideAndDontSave };
                Texture.Create();
            }
            Rect view = new Rect(state.Center - state.ViewSize * .5f, state.ViewSize);
            Vector2 mapSize = map.MapSizeMeters;
            Vector2 visibleMin = Vector2.Min(Vector2.Max(view.min, Vector2.zero), mapSize);
            Vector2 visibleMax = Vector2.Min(Vector2.Max(view.max, Vector2.zero), mapSize);
            visibleMap = new Rect(visibleMin, visibleMax - visibleMin);
            FindVisibleRange(map, view, out float minimum, out float maximum);
            var presentation = map.Presentation;
            material.SetFloat("_MinimumLineWidth", presentation.MinimumContourWidth);
            material.SetFloat("_MaximumLineWidth", presentation.MaximumContourWidth);
            material.SetFloat("_MinimumOpacity", presentation.MinimumContourOpacity);
            material.SetFloat("_MaximumOpacity", presentation.MaximumContourOpacity);
            material.SetFloat("_MaximumCoverage", presentation.MaximumContourCoverage);
            material.SetFloat("_EdgeSoftness", presentation.ContourEdgeSoftness);
            material.SetFloat("_DashSubmergedContours", map.DashSubmergedContours ? 1f : 0f);
            material.SetVector("_ViewRect", new Vector4(view.x, view.y, view.width, view.height));
            material.SetVector("_MapSizeMeters", mapSize);
            material.SetTexture("_CheckpointFogTex", fog != null ? fog : Texture2D.blackTexture);
            material.SetFloat("_VisibleMinimumHeight", minimum);
            material.SetFloat("_VisibleMaximumHeight", maximum);
            SetWaterTime(Time.unscaledTime);
            commands.Clear();
            waterCommands.Clear();
            commands.SetRenderTarget(Texture);
            // The black map background shows through absent tiles. The alpha channel lets
            // the reveal pass distinguish those pixels from legitimate black terrain.
            commands.ClearRenderTarget(false, true, Color.clear);
            Vector2 tileSize = map.LevelAsset.TileSizeMeters;
            foreach (var tile in map.RuntimeMap.Tiles)
            {
                if (tile.Field == null) continue;
                Vector2 origin = Vector2.Scale(tile.Coordinate, tileSize);
                if (!view.Overlaps(new Rect(origin, tileSize))) continue;
                properties.Clear();
                TiledMapTileView.ConfigureWater(properties, tile);
                properties.SetVector("_TileRect", new Vector4(origin.x, origin.y, tileSize.x, tileSize.y));
                properties.SetTexture("_HeightTex", tile.Field.SurfaceTexture);
                properties.SetFloat("_MinimumHeight", tile.Level.MinimumHeightMeters);
                properties.SetFloat("_MaximumHeight", tile.Level.MaximumHeightMeters);
                commands.DrawMesh(quad, Matrix4x4.identity, material, 0, 0, properties);

                if (properties.GetFloat("_WaterEnabled") == 0) continue;
                Rect water = tile.Level.BakedStaticWaterBounds;
                Rect waterMapRect = new Rect(origin + Vector2.Scale(water.min, tileSize), Vector2.Scale(water.size, tileSize));
                if (!view.Overlaps(waterMapRect)) continue;
                if (waterCommands.sizeInBytes == 0) waterCommands.SetRenderTarget(Texture);
                var crop = Matrix4x4.TRS(water.min, Quaternion.identity, new Vector3(water.width, water.height, 1));
                waterCommands.DrawMesh(quad, crop, material, 0, 0, properties);
            }
            Graphics.ExecuteCommandBuffer(commands);
        }

        private void SetWaterTime(float time)
        {
            waterFrame = Mathf.FloorToInt(time * WaterFramesPerSecond);
            material.SetFloat("_MapWaterTime", waterFrame / (float)WaterFramesPerSecond);
        }

        internal void AnimateWater(float time)
        {
            if (waterCommands.sizeInBytes == 0 || Mathf.FloorToInt(time * WaterFramesPerSecond) == waterFrame) return;
            SetWaterTime(time);
            Graphics.ExecuteCommandBuffer(waterCommands);
        }

        internal RenderTexture RenderFault(float progress)
        {
            material.SetFloat("_FaultProgress", progress);
            return faultTexture = ApplyEffect(faultTexture, "World map data fault", 1);
        }

        internal RenderTexture RenderReveal(Texture previousFog, Vector2 origin, float progress)
        {
            if (progress >= 1f) return Texture;
            float farthest = Vector2.Max(origin - visibleMap.min, visibleMap.max - origin).magnitude;
            material.SetTexture("_PreviousCheckpointFogTex", previousFog);
            material.SetVector("_RevealWave", new Vector4(origin.x, origin.y, farthest * progress, progress));
            return revealTexture = ApplyEffect(revealTexture, "World map region reveal", 2);
        }

        private RenderTexture ApplyEffect(RenderTexture target, string name, int pass)
        {
            if (target == null || target.width != Texture.width || target.height != Texture.height)
            {
                ReleaseTexture(target);
                target = new RenderTexture(Texture.descriptor)
                { name = name, hideFlags = HideFlags.HideAndDontSave };
                target.Create();
            }
            commands.Clear();
            commands.Blit(Texture, target, material, pass);
            Graphics.ExecuteCommandBuffer(commands);
            return target;
        }

        internal void EndFault()
        {
            ReleaseTexture(faultTexture);
            faultTexture = null;
        }

        internal void EndReveal()
        {
            ReleaseTexture(revealTexture);
            revealTexture = null;
        }

        private static void FindVisibleRange(MapTestSceneController map, Rect view, out float minimum, out float maximum)
        {
            minimum = float.PositiveInfinity;
            maximum = float.NegativeInfinity;
            // Sample each intersecting tile so small visible tiles and irregular layouts count too.
            Vector2 tileSize = map.LevelAsset.TileSizeMeters;
            foreach (var tile in map.RuntimeMap.Tiles)
            {
                if (tile.Field == null) continue;
                Vector2 origin = Vector2.Scale(tile.Coordinate, tileSize);
                Vector2 lo = Vector2.Max(view.min, origin);
                Vector2 hi = Vector2.Min(view.max, origin + tileSize);
                if (hi.x <= lo.x || hi.y <= lo.y) continue;
                int nx = Mathf.Clamp(Mathf.CeilToInt((hi.x - lo.x) / view.width * 80), 2, 80);
                int ny = Mathf.Clamp(Mathf.CeilToInt((hi.y - lo.y) / view.height * 48), 2, 48);
                for (int y = 0; y <= ny; y++)
                for (int x = 0; x <= nx; x++)
                {
                    Vector2 position = new Vector2(Mathf.Lerp(lo.x, hi.x, x / (float)nx),
                        Mathf.Lerp(lo.y, hi.y, y / (float)ny));
                    Vector2 uv = position - origin;
                    uv = new Vector2(uv.x / tileSize.x, uv.y / tileSize.y);
                    if (!tile.Field.IsPlayable(uv)) continue;
                    float sample = tile.Field.SampleSurfaceHeight(uv);
                    minimum = Mathf.Min(minimum, sample);
                    maximum = Mathf.Max(maximum, sample);
                }
            }
            if (float.IsPositiveInfinity(minimum)) minimum = maximum = map.MinimumHeightMeters;
        }

        private static void ReleaseTexture(RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            CoreUtils.Destroy(texture);
        }

        public void Dispose()
        {
            EndFault();
            EndReveal();
            ReleaseTexture(Texture);
            Texture = null;
            commands.Dispose();
            waterCommands.Dispose();
            CoreUtils.Destroy(material);
            CoreUtils.Destroy(quad);
        }
    }
}
