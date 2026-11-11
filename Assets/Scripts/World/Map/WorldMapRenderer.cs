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
        public RenderTexture Texture { get; private set; }

        public WorldMapRenderer(Shader shader)
        {
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            quad = new Mesh { name = "World map tile", hideFlags = HideFlags.HideAndDontSave };
            quad.vertices = new[] { Vector3.zero, Vector3.right, new Vector3(1, 1), Vector3.up };
            quad.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        }

        public void Render(MapTestSceneController map, MapViewState state, int width, int height)
        {
            if (Texture == null || Texture.width != width || Texture.height != height)
            {
                ReleaseTexture();
                Texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
                { name = "World map contours", hideFlags = HideFlags.HideAndDontSave };
                Texture.Create();
            }
            Rect view = new Rect(state.Center - state.ViewSize * .5f, state.ViewSize);
            FindVisibleRange(map, view, out float minimum, out float maximum);
            var presentation = map.Presentation;
            material.SetFloat("_MinimumLineWidth", presentation.MinimumContourWidth);
            material.SetFloat("_MaximumLineWidth", presentation.MaximumContourWidth);
            material.SetFloat("_MinimumOpacity", presentation.MinimumContourOpacity);
            material.SetFloat("_MaximumOpacity", presentation.MaximumContourOpacity);
            material.SetFloat("_MaximumCoverage", presentation.MaximumContourCoverage);
            material.SetFloat("_EdgeSoftness", presentation.ContourEdgeSoftness);
            material.SetVector("_ViewRect", new Vector4(view.x, view.y, view.width, view.height));
            material.SetFloat("_VisibleMinimumHeight", minimum);
            material.SetFloat("_VisibleMaximumHeight", maximum);
            commands.Clear();
            commands.SetRenderTarget(Texture);
            commands.ClearRenderTarget(false, true, Color.black);
            Vector2 tileSize = map.LevelAsset.TileSizeMeters;
            foreach (var tile in map.RuntimeMap.Tiles)
            {
                Vector2 origin = Vector2.Scale(tile.Coordinate, tileSize);
                if (!view.Overlaps(new Rect(origin, tileSize))) continue;
                properties.Clear();
                properties.SetVector("_TileRect", new Vector4(origin.x, origin.y, tileSize.x, tileSize.y));
                properties.SetTexture("_HeightTex", tile.Field.SurfaceTexture);
                properties.SetFloat("_MinimumHeight", tile.Level.MinimumHeightMeters);
                properties.SetFloat("_MaximumHeight", tile.Level.MaximumHeightMeters);
                commands.DrawMesh(quad, Matrix4x4.identity, material, 0, 0, properties);
            }
            Graphics.ExecuteCommandBuffer(commands);
        }

        private static void FindVisibleRange(MapTestSceneController map, Rect view, out float minimum, out float maximum)
        {
            minimum = float.PositiveInfinity;
            maximum = float.NegativeInfinity;
            // Sample each intersecting tile so small visible tiles and irregular layouts count too.
            Vector2 tileSize = map.LevelAsset.TileSizeMeters;
            foreach (var tile in map.RuntimeMap.Tiles)
            {
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

        private void ReleaseTexture()
        {
            if (Texture == null) return;
            Texture.Release();
            CoreUtils.Destroy(Texture);
            Texture = null;
        }

        public void Dispose()
        {
            ReleaseTexture();
            commands.Dispose();
            CoreUtils.Destroy(material);
            CoreUtils.Destroy(quad);
        }
    }
}
