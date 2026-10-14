using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalGame.RobotMap
{
    /// <summary>
    /// Batches complete, finite screen-pixel bars projected from the scene.
    /// Pixel width is independent of the camera scale and UI hierarchy.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    [AddComponentMenu("")]
    public sealed class BioScanStaticAreaGraphic : MaskableGraphic
    {
        private const int MaximumMeshVertices = 60000;
        private const float AntialiasPaddingPixels = 1f;

        private sealed class AreaLines
        {
            public readonly List<StaticScanLineSegment> Segments = new List<StaticScanLineSegment>(32);
            public Vector2 ScreenAnchor;
            public Color32 Tint;
            public float Width;
            public Rect Viewport;
        }

        private readonly List<AreaLines> areas = new List<AreaLines>();
        private int areaCount;
        private int meshVertexCount;

        public int AreaCount => areaCount;
        public int MeshVertexCount => meshVertexCount;

        public void ClearAreas()
        {
            if (areaCount == 0)
                return;
            areaCount = 0;
            meshVertexCount = 0;
            SetVerticesDirty();
        }

        public bool TryAddArea(List<StaticScanLineSegment> segments, Vector2 screenAnchor,
            Color tint, float lineWidthPixels, Rect viewport)
        {
            if (segments == null || segments.Count == 0
                || segments.Count > (MaximumMeshVertices - meshVertexCount) / 4
                || !IsFinite(screenAnchor) || !IsFinite(lineWidthPixels)
                || !IsFinite(viewport.min) || !IsFinite(viewport.max)
                || viewport.width <= 0f || viewport.height <= 0f)
                return false;

            foreach (StaticScanLineSegment segment in segments)
            {
                if (!IsFinite(segment.Start) || !IsFinite(segment.End)
                    || (segment.End - segment.Start).sqrMagnitude < 0.0001f)
                    return false;
            }

            AreaLines area;
            if (areaCount < areas.Count)
                area = areas[areaCount];
            else
            {
                area = new AreaLines();
                areas.Add(area);
            }
            area.Segments.Clear();
            area.Segments.AddRange(segments);
            area.ScreenAnchor = screenAnchor;
            area.Tint = tint;
            area.Width = Mathf.Max(0.5f, lineWidthPixels);
            area.Viewport = viewport;
            areaCount++;
            meshVertexCount += segments.Count * 4;
            SetVerticesDirty();
            return true;
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera : null;
            for (int index = 0; index < areaCount; index++)
            {
                AreaLines area = areas[index];
                foreach (StaticScanLineSegment segment in area.Segments)
                {
                    Vector2 delta = segment.End - segment.Start;
                    float halfLength = delta.magnitude * 0.5f;
                    Vector2 direction = delta / (halfLength * 2f);
                    Vector2 normal = new Vector2(-direction.y, direction.x);
                    Vector2 center = area.ScreenAnchor + (segment.Start + segment.End) * 0.5f;
                    float halfWidth = area.Width * 0.5f;
                    float outerLength = halfLength + AntialiasPaddingPixels;
                    float outerWidth = halfWidth + AntialiasPaddingPixels;
                    int firstVertex = mesh.currentVertCount;

                    AddBarVertex(mesh, area, center, direction, normal,
                        new Vector2(-outerLength, -outerWidth), halfLength, halfWidth, uiCamera);
                    AddBarVertex(mesh, area, center, direction, normal,
                        new Vector2(-outerLength, outerWidth), halfLength, halfWidth, uiCamera);
                    AddBarVertex(mesh, area, center, direction, normal,
                        new Vector2(outerLength, outerWidth), halfLength, halfWidth, uiCamera);
                    AddBarVertex(mesh, area, center, direction, normal,
                        new Vector2(outerLength, -outerWidth), halfLength, halfWidth, uiCamera);
                    mesh.AddTriangle(firstVertex, firstVertex + 1, firstVertex + 2);
                    mesh.AddTriangle(firstVertex, firstVertex + 2, firstVertex + 3);
                }
            }
        }

        private void AddBarVertex(VertexHelper mesh, AreaLines area, Vector2 center,
            Vector2 direction, Vector2 normal, Vector2 barPoint, float halfLength,
            float halfWidth, Camera uiCamera)
        {
            Vector2 screenPoint = center + direction * barPoint.x + normal * barPoint.y;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform, screenPoint, uiCamera, out Vector2 localPoint);
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = localPoint;
            vertex.color = area.Tint;
            // uv0: distance along/across the bar, in actual screen pixels.
            // uv1: finite rectangle half-size, followed by absolute screen xy.
            // uv2: camera viewport xmin, ymin, xmax, ymax in screen pixels.
            vertex.uv0 = barPoint;
            vertex.uv1 = new Vector4(halfLength, halfWidth, screenPoint.x, screenPoint.y);
            vertex.uv2 = new Vector4(area.Viewport.xMin, area.Viewport.yMin,
                area.Viewport.xMax, area.Viewport.yMax);
            mesh.AddVert(vertex);
        }

        private static bool IsFinite(Vector2 value) => IsFinite(value.x) && IsFinite(value.y);
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
