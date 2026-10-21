using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.Demos.CheckpointRefit
{
    /// <summary>Resolution-independent assembly views and a text-free unavailable bar.</summary>
    [UxmlElement]
    public partial class RefitModulePreviewElement : VisualElement
    {
        private static readonly Color Outline = new Color32(228, 228, 220, 255);
        private static readonly Color Detail = new Color32(126, 126, 119, 255);
        private string kind = "unavailable";
        private float scale;
        private Vector2 offset;

        [UxmlAttribute]
        public string ModuleKind
        {
            get => kind;
            set { kind = value ?? "unavailable"; MarkDirtyRepaint(); }
        }

        public RefitModulePreviewElement()
        {
            pickingMode = PickingMode.Ignore;
            focusable = false;
            generateVisualContent += Draw;
        }

        private void Draw(MeshGenerationContext context)
        {
            if (contentRect.width < 2 || contentRect.height < 2) return;
            Painter2D painter = context.painter2D;
            painter.lineCap = LineCap.Butt;
            painter.lineJoin = LineJoin.Miter;
            if (kind == "unavailable")
            {
                painter.strokeColor = resolvedStyle.color;
                painter.lineWidth = 2.5f;
                painter.BeginPath();
                painter.MoveTo(new Vector2(contentRect.xMin + contentRect.width * .1f,
                    contentRect.yMax - contentRect.height * .15f));
                painter.LineTo(new Vector2(contentRect.xMax - contentRect.width * .1f,
                    contentRect.yMin + contentRect.height * .15f));
                painter.Stroke();
                return;
            }
            scale = Mathf.Min(contentRect.width / 600f, contentRect.height / 400f);
            offset = contentRect.center - new Vector2(300f, 200f) * scale;
            if (kind == "tracks") DrawTracks(painter);
            else if (kind == "camera") DrawCamera(painter);
        }

        private void DrawTracks(Painter2D painter)
        {
            Stroke(painter, Outline, 2.6f);
            Capsule(painter, 300, 205, 192, 69);
            Stroke(painter, Detail, 1.4f);
            Capsule(painter, 300, 205, 179, 55);
            for (int x = 154; x <= 446; x += 36)
            {
                Line(painter, x, 137, x + 10, 150);
                Line(painter, x, 260, x + 10, 273);
            }
            Stroke(painter, Outline, 2);
            for (int x = 178; x <= 422; x += 61)
            {
                Ellipse(painter, x, 205, 35, 35);
                Stroke(painter, Detail, 1.4f);
                Ellipse(painter, x, 205, 8, 8);
                Stroke(painter, Outline, 2);
            }
            Stroke(painter, Detail, 1.5f);
            Line(painter, 182, 205, 422, 205);
            Line(painter, 235, 134, 235, 113);
            Line(painter, 365, 134, 365, 113);
            Line(painter, 235, 113, 365, 113);
        }

        private void DrawCamera(Painter2D painter)
        {
            Stroke(painter, Detail, 1.5f);
            Ellipse(painter, 229, 205, 27, 58);
            Line(painter, 205, 151, 179, 151);
            Line(painter, 179, 151, 179, 259);
            Line(painter, 179, 259, 205, 259);
            Stroke(painter, Outline, 2.4f);
            Line(painter, 229, 147, 376, 128);
            Line(painter, 229, 263, 376, 282);
            Ellipse(painter, 376, 205, 64, 77);
            Ellipse(painter, 376, 205, 53, 65);
            Stroke(painter, Detail, 1.5f);
            Ellipse(painter, 376, 205, 38, 49);
            Ellipse(painter, 280, 205, 37, 65);
            Ellipse(painter, 309, 205, 43, 69);
            Line(painter, 350, 177, 379, 151);
            Line(painter, 367, 238, 396, 212);
        }

        private void Capsule(Painter2D painter, float cx, float cy, float rx, float ry)
        {
            painter.BeginPath();
            const int segments = 48;
            for (int i = 0; i <= segments * 2; i++)
            {
                float angle = i * Mathf.PI / segments - Mathf.PI * .5f;
                float side = i <= segments ? rx - ry : -(rx - ry);
                Vector2 point = Point(cx + side + Mathf.Cos(angle) * ry, cy + Mathf.Sin(angle) * ry);
                if (i == 0) painter.MoveTo(point); else painter.LineTo(point);
            }
            painter.ClosePath();
            painter.Stroke();
        }

        private void Ellipse(Painter2D painter, float cx, float cy, float rx, float ry)
        {
            painter.BeginPath();
            const int segments = 128;
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector2 point = Point(cx + Mathf.Cos(angle) * rx, cy + Mathf.Sin(angle) * ry);
                if (i == 0) painter.MoveTo(point); else painter.LineTo(point);
            }
            painter.ClosePath();
            painter.Stroke();
        }

        private Vector2 Point(float x, float y) => offset + new Vector2(x, y) * scale;
        private void Stroke(Painter2D painter, Color color, float width)
        {
            painter.strokeColor = color;
            painter.lineWidth = width * scale;
        }
        private void Line(Painter2D painter, float x1, float y1, float x2, float y2)
        {
            painter.BeginPath(); painter.MoveTo(Point(x1, y1)); painter.LineTo(Point(x2, y2)); painter.Stroke();
        }
    }
}
