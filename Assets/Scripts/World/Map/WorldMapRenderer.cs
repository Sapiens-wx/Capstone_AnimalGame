using System;
using AnimalGame.MapTest;
using UnityEngine;
using UnityEngine.Rendering;

namespace AnimalGame.World.Map
{
    internal readonly struct WorldMapAperture
    {
        internal readonly Vector2 Center;
        internal readonly float Radius, Feather;

        internal WorldMapAperture(Vector2 displaySize, Vector2 center, float progress)
        {
            Center = center;
            float farthestCorner = Vector2.Max(center, displaySize - center).magnitude;
            Radius = farthestCorner * progress;
            Feather = Mathf.Max(.01f, Mathf.Min(Radius, displaySize.y * .055f) * (1f - progress));
        }

        internal float OpacityAt(Vector2 point, float extent)
        {
            float distance = Vector2.Distance(point, Center) - Radius + extent;
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-Feather, 0f, distance));
        }
    }

    internal sealed class WorldMapRenderer : IDisposable
    {
        private const float ShorelineZoomOutBoostPixels = .3f;
        private const float ShorelineOpacity = .44f;
        private readonly Material material;
        private readonly Mesh quad;
        private readonly Vector2 patternMetresPerPixel;
        private readonly CommandBuffer commands = new CommandBuffer { name = "World map contours" };
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private RenderTexture faultTexture;
        private RenderTexture revealTexture;
        private RenderTexture openingTexture;
        private Rect viewBounds;
        private Rect visibleMap;
        private Matrix4x4 clipFromMap;
        private Vector2 metresPerPixel;
        private bool hasVisibleWater;
        private Texture currentFog;
        private static readonly int BankOpacity = Shader.PropertyToID("_MapBankOpacity");
        internal float WaterTime { get; set; }
        public RenderTexture Texture { get; private set; }

        public WorldMapRenderer(Shader shader, Vector2 patternMetresPerPixel)
        {
            this.patternMetresPerPixel = patternMetresPerPixel;
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetVector("_MapPatternMetresPerPixel", patternMetresPerPixel);
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
            Vector2 viewSize = state.ProjectedViewSize;
            Vector2 half = viewSize * .5f;
            Matrix4x4 mapFromView = state.MapFromView;
            var projection = Matrix4x4.Ortho(-half.x, half.x, -half.y, half.y, -1, 1);
            clipFromMap = GL.GetGPUProjectionMatrix(projection, true) * mapFromView.inverse;
            var extent = new Vector3(viewSize.x, viewSize.y, 1f);
            Matrix4x4 mapFromViewport = mapFromView * Matrix4x4.TRS(-half, Quaternion.identity, extent);
            Vector2 bottomLeft = mapFromViewport.MultiplyPoint3x4(Vector2.zero);
            Vector2 bottomRight = mapFromViewport.MultiplyPoint3x4(Vector2.right);
            Vector2 topLeft = mapFromViewport.MultiplyPoint3x4(Vector2.up);
            Vector2 topRight = mapFromViewport.MultiplyPoint3x4(Vector2.one);
            Vector2 viewMin = Vector2.Min(Vector2.Min(bottomLeft, bottomRight), Vector2.Min(topLeft, topRight));
            Vector2 viewMax = Vector2.Max(Vector2.Max(bottomLeft, bottomRight), Vector2.Max(topLeft, topRight));
            viewBounds = new Rect(viewMin, viewMax - viewMin);
            Vector2 mapSize = map.MapSizeMeters;
            Vector2 visibleMin = Vector2.Min(Vector2.Max(viewMin, Vector2.zero), mapSize);
            Vector2 visibleMax = Vector2.Min(Vector2.Max(viewMax, Vector2.zero), mapSize);
            visibleMap = new Rect(visibleMin, visibleMax - visibleMin);
            FindVisibleRange(map, viewBounds, out float minimum, out float maximum);
            var presentation = map.Presentation;
            material.SetFloat("_MinimumLineWidth", presentation.MinimumContourWidth);
            material.SetFloat("_MaximumLineWidth", presentation.MaximumContourWidth);
            material.SetFloat("_MinimumOpacity", presentation.MinimumContourOpacity);
            material.SetFloat("_MaximumOpacity", presentation.MaximumContourOpacity);
            material.SetFloat("_MaximumCoverage", presentation.MaximumContourCoverage);
            material.SetFloat("_EdgeSoftness", presentation.ContourEdgeSoftness);
            material.SetFloat("_DashSubmergedContours", map.DashSubmergedContours ? 1f : 0f);
            material.SetMatrix("_MapToClip", clipFromMap);
            material.SetMatrix("_MapFromViewport", mapFromViewport);
            material.SetVector("_MapSizeMeters", mapSize);
            currentFog = fog;
            material.SetTexture("_CheckpointFogTex", fog != null ? fog : Texture2D.blackTexture);
            material.SetFloat("_VisibleMinimumHeight", minimum);
            material.SetFloat("_VisibleMaximumHeight", maximum);
            metresPerPixel = new Vector2(viewSize.x / width, viewSize.y / height);
            material.SetVector("_MapMetresPerPixel", metresPerPixel);
            DrawTiles(map);
        }

        internal void RenderWaterFrame(MapTestSceneController map)
        {
            if (hasVisibleWater) DrawTiles(map);
        }

        private void DrawTiles(MapTestSceneController map)
        {
            var shoreline = map.Shoreline;
            float zoom = Mathf.Sqrt((patternMetresPerPixel.x * patternMetresPerPixel.y) /
                (metresPerPixel.x * metresPerPixel.y));
            // Widen the stroke in screen space as the camera pulls back.
            float shorelineWidth = map.Presentation.MinimumContourWidth + ShorelineZoomOutBoostPixels / (1f + zoom);
            Vector2 padding = Vector2.Max(metresPerPixel * Mathf.Max(2f, shorelineWidth * .5f + .5f),
                patternMetresPerPixel * 2f);
            material.SetFloat("_MapBankWidthScale", shorelineWidth / (WorldMapShoreline.ReferenceWidthPixels * zoom));
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
                if (!viewBounds.Overlaps(new Rect(origin, tileSize))) continue;
                properties.Clear();
                var level = tile.Level;
                var waterMask = level.BakedStaticWaterMask;
                bool visibleWater = false;
                if (waterMask != null)
                {
                    Rect bounds = level.BakedStaticWaterBounds;
                    var waterBounds = new Rect(origin + Vector2.Scale(bounds.position, tileSize) - padding,
                        Vector2.Scale(bounds.size, tileSize) + padding * 2f);
                    if (viewBounds.Overlaps(waterBounds))
                    {
                        visibleWater = true;
                        hasVisibleWater = true;
                        properties.SetTexture("_WaterMaskTex", waterMask);
                        properties.SetTexture("_WaterUvTex", level.BakedStaticWaterUv != null
                            ? level.BakedStaticWaterUv : Texture2D.blackTexture);
                        properties.SetVector("_WaterMaskTex_TexelSize", new Vector4(
                            1f / waterMask.width, 1f / waterMask.height, waterMask.width, waterMask.height));
                        properties.SetVector("_WaterMaskBounds", new Vector4(bounds.x, bounds.y, bounds.width, bounds.height));
                    }
                }
                properties.SetFloat("_WaterEnabled", visibleWater ? 1 : 0);
                properties.SetFloat(BankOpacity, shoreline.Contains(level) ? 0 : ShorelineOpacity);
                properties.SetVector("_TileRect", new Vector4(origin.x, origin.y, tileSize.x, tileSize.y));
                properties.SetTexture("_HeightTex", tile.Field.SurfaceTexture);
                properties.SetFloat("_MinimumHeight", level.MinimumHeightMeters);
                properties.SetFloat("_MaximumHeight", level.MaximumHeightMeters);
                commands.DrawMesh(quad, Matrix4x4.identity, material, 0, 0, properties);
            }
            if (hasVisibleWater && shoreline.Mesh != null)
            {
                shoreline.Draw(commands, clipFromMap, metresPerPixel,
                    patternMetresPerPixel, map.MapSizeMeters, shorelineWidth, ShorelineOpacity, currentFog);
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

        internal RenderTexture RenderOpening(Texture source, Vector2 displaySize, Rect viewport, WorldMapAperture aperture,
            float bearing, Vector4 grid, Color gridColor)
        {
            int width = (int)displaySize.x, height = (int)displaySize.y;
            if (openingTexture == null || openingTexture.width != width || openingTexture.height != height)
            {
                ReleaseTexture(openingTexture);
                openingTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
                { name = "World map opening", hideFlags = HideFlags.HideAndDontSave };
                openingTexture.Create();
            }
            float angle = bearing * Mathf.Deg2Rad;
            material.SetVector("_OpeningDisplaySize", displaySize);
            material.SetVector("_OpeningViewport", new Vector4(viewport.x, viewport.y, viewport.width, viewport.height));
            material.SetVector("_OpeningCenter", aperture.Center);
            material.SetVector("_OpeningEdge", new Vector4(aperture.Radius, aperture.Feather, Mathf.Cos(angle), Mathf.Sin(angle)));
            material.SetVector("_OpeningGrid", grid);
            material.SetColor("_OpeningGridColor", gridColor);
            commands.Clear();
            commands.Blit(source, openingTexture, material, 3);
            Graphics.ExecuteCommandBuffer(commands);
            return openingTexture;
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

        internal void EndOpening()
        {
            ReleaseTexture(openingTexture);
            openingTexture = null;
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
            EndOpening();
            ReleaseTexture(Texture);
            Texture = null;
            commands.Dispose();
            CoreUtils.Destroy(material);
            CoreUtils.Destroy(quad);
        }
    }
}
