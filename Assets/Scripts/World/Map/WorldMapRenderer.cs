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
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private RenderTexture faultTexture;
        private RenderTexture revealTexture;
        private Rect view;
        private Rect visibleMap;
        private bool hasVisibleWater;
        private Vector2 openingMetresPerPixel;
        private Texture currentFog;
        private static readonly int BankOpacity = Shader.PropertyToID("_MapBankOpacity");
        internal float WaterTime { get; set; }
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
            view = new Rect(state.Center - state.ViewSize * .5f, state.ViewSize);
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
            currentFog = fog;
            material.SetTexture("_CheckpointFogTex", fog != null ? fog : Texture2D.blackTexture);
            material.SetFloat("_VisibleMinimumHeight", minimum);
            material.SetFloat("_VisibleMaximumHeight", maximum);
            var metresPerPixel = new Vector2(view.width / width, view.height / height);
            if (openingMetresPerPixel == Vector2.zero)
                openingMetresPerPixel = metresPerPixel;
            material.SetVector("_MapMetresPerPixel", metresPerPixel);
            material.SetVector("_MapViewZoom", openingMetresPerPixel / metresPerPixel);
            DrawTiles(map);
        }

        internal void RenderWaterFrame(MapTestSceneController map)
        {
            if (hasVisibleWater) DrawTiles(map);
        }

        private void DrawTiles(MapTestSceneController map)
        {
            var shoreline = map.Shoreline;
            var metresPerPixel = new Vector2(view.width / Texture.width, view.height / Texture.height);
            material.SetFloat("_MapWaterTime", WaterTime);
            hasVisibleWater = false;
            commands.Clear();
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
                var level = tile.Level;
                var waterMask = level.BakedStaticWaterMask;
                bool visibleWater = false;
                if (waterMask != null)
                {
                    Rect bounds = level.BakedStaticWaterBounds;
                    Vector2 padding = Vector2.Max(metresPerPixel, openingMetresPerPixel) * 2f;
                    var waterBounds = new Rect(origin + Vector2.Scale(bounds.position, tileSize) - padding,
                        Vector2.Scale(bounds.size, tileSize) + padding * 2f);
                    if (view.Overlaps(waterBounds))
                    {
                        visibleWater = true;
                        hasVisibleWater = true;
                        properties.SetTexture("_WaterMaskTex", waterMask);
                        properties.SetVector("_WaterMaskTex_TexelSize", new Vector4(
                            1f / waterMask.width, 1f / waterMask.height, waterMask.width, waterMask.height));
                        properties.SetVector("_WaterMaskBounds", new Vector4(bounds.x, bounds.y, bounds.width, bounds.height));
                    }
                }
                properties.SetFloat("_WaterEnabled", visibleWater ? 1 : 0);
                properties.SetFloat(BankOpacity, shoreline.Contains(level) ? 0 : .5f);
                properties.SetVector("_TileRect", new Vector4(origin.x, origin.y, tileSize.x, tileSize.y));
                properties.SetTexture("_HeightTex", tile.Field.SurfaceTexture);
                properties.SetFloat("_MinimumHeight", level.MinimumHeightMeters);
                properties.SetFloat("_MaximumHeight", level.MaximumHeightMeters);
                commands.DrawMesh(quad, Matrix4x4.identity, material, 0, 0, properties);
            }
            if (hasVisibleWater && shoreline.Mesh != null)
            {
                var projection = Matrix4x4.Ortho(view.xMin, view.xMax, view.yMin, view.yMax, -1, 1);
                shoreline.Draw(commands, GL.GetGPUProjectionMatrix(projection, true), metresPerPixel,
                    openingMetresPerPixel / metresPerPixel, map.MapSizeMeters, .5f, currentFog);
            }
            Graphics.ExecuteCommandBuffer(commands);
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
            CoreUtils.Destroy(material);
            CoreUtils.Destroy(quad);
        }
    }
}
