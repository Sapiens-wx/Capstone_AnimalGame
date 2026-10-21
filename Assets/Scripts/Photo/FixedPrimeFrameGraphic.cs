using UnityEngine;
using UnityEngine.UI;

namespace AnimalGame.RobotMap
{
    /// <summary>A fixed prime lens frame drawn in geometry at stable pixel weight.</summary>
    [DisallowMultipleComponent]
    public sealed class FixedPrimeFrameGraphic : MaskableGraphic
    {
        private const float LineWidthPixels = 2.5f;
        private const float CrossArmPixels = 8f;
        private float pixelsPerUnit = 1f;

        public void SetPresentation(Color tint, float reveal, float screenScale)
        {
            tint.a *= Mathf.Clamp01(reveal);
            color = tint;
            float nextScale = Mathf.Max(0.0001f, screenScale);
            if (Mathf.Approximately(pixelsPerUnit, nextScale))
                return;

            pixelsPerUnit = nextScale;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            Rect rect = rectTransform.rect;
            if (rect.width <= 0f || rect.height <= 0f || color.a <= 0f)
                return;

            float thickness = Mathf.Min(
                LineWidthPixels / pixelsPerUnit,
                Mathf.Min(rect.width, rect.height) * 0.5f);
            AddRect(vertexHelper, rect.xMin, rect.yMin, rect.width, thickness);
            AddRect(vertexHelper, rect.xMin, rect.yMax - thickness, rect.width, thickness);
            AddRect(vertexHelper, rect.xMin, rect.yMin + thickness,
                thickness, rect.height - thickness * 2f);
            AddRect(vertexHelper, rect.xMax - thickness, rect.yMin + thickness,
                thickness, rect.height - thickness * 2f);

            float arm = CrossArmPixels / pixelsPerUnit;
            Vector2 center = rect.center;
            AddRect(vertexHelper, center.x - arm - thickness * 0.5f,
                center.y - thickness * 0.5f, arm * 2f + thickness, thickness);
            AddRect(vertexHelper, center.x - thickness * 0.5f,
                center.y + thickness * 0.5f, thickness, arm);
            AddRect(vertexHelper, center.x - thickness * 0.5f,
                center.y - thickness * 0.5f - arm, thickness, arm);
        }

        private void AddRect(VertexHelper vertexHelper, float x, float y,
            float width, float height)
        {
            if (width <= 0f || height <= 0f)
                return;

            int start = vertexHelper.currentVertCount;
            Color32 tint = color;
            vertexHelper.AddVert(new Vector3(x, y, 0f), tint, Vector2.zero);
            vertexHelper.AddVert(new Vector3(x, y + height, 0f), tint, Vector2.zero);
            vertexHelper.AddVert(new Vector3(x + width, y + height, 0f), tint, Vector2.zero);
            vertexHelper.AddVert(new Vector3(x + width, y, 0f), tint, Vector2.zero);
            vertexHelper.AddTriangle(start, start + 1, start + 2);
            vertexHelper.AddTriangle(start, start + 2, start + 3);
        }
    }
}
