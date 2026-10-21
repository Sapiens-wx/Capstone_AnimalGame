using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.Demos.CheckpointRefit
{
    /// <summary>Deliberately unidentified inspection placeholder for undiscovered modules.</summary>
    [UxmlElement]
    public partial class RefitRobotPreviewElement : VisualElement
    {
        private static readonly Color Quiet = new Color32(82, 82, 82, 255);
        private bool showInspection;
        private float drawScale;
        private Vector2 drawOffset;

        [UxmlAttribute]
        public bool inspectionMode
        {
            get => showInspection;
            set { showInspection = value; MarkDirtyRepaint(); }
        }

        public RefitRobotPreviewElement()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        private void Draw(MeshGenerationContext context)
        {
            if (contentRect.width <= 0 || contentRect.height <= 0) return;
            Painter2D painter = context.painter2D;
            painter.lineCap = LineCap.Butt;
            painter.lineJoin = LineJoin.Miter;
            DrawInspection(painter);
        }

        private void DrawInspection(Painter2D painter)
        {
            ConfigureSpace(600, 400);
            SetStroke(painter, Quiet, 1.1f);
            // An abstract sealed block carries no identifiable equipment silhouette or upgrade promise.
            Polyline(painter, true, 239, 110, 336, 110, 370, 144, 370, 235, 337, 265, 239, 265, 208, 235, 208, 144);
            Polyline(painter, true, 253, 130, 325, 130, 348, 153, 348, 222, 325, 244, 253, 244, 231, 222, 231, 153);
            Line(painter, 269, 107, 269, 85);
            Line(painter, 309, 107, 309, 85);
            Line(painter, 252, 280, 325, 280);
            Line(painter, 262, 290, 314, 290);
            SetStroke(painter, new Color32(42, 42, 42, 255), 1);
            for (int i = 0; i < 8; i++) Line(painter, 235 + i * 15, 157, 219 + i * 15, 213);
            SetStroke(painter, new Color32(132, 132, 132, 255), 1.6f);
            Line(painter, 283, 181, 295, 181);
            Line(painter, 289, 175, 289, 187);
            // Discontinuous midline reads as unavailable scan data.
            SetStroke(painter, Quiet, 1);
            Line(painter, 86, 183, 157, 183);
            Line(painter, 421, 183, 492, 183);
        }

        private void ConfigureSpace(float width, float height)
        {
            drawScale = Mathf.Min(contentRect.width / width, contentRect.height / height);
            drawOffset = contentRect.center - new Vector2(width, height) * drawScale * .5f;
        }

        private Vector2 Point(float x, float y) => drawOffset + new Vector2(x, y) * drawScale;

        private void SetStroke(Painter2D painter, Color color, float width)
        {
            painter.strokeColor = color;
            painter.lineWidth = width * drawScale;
        }

        private void Line(Painter2D painter, float x1, float y1, float x2, float y2)
        {
            painter.BeginPath();
            painter.MoveTo(Point(x1, y1));
            painter.LineTo(Point(x2, y2));
            painter.Stroke();
        }

        private void Polyline(Painter2D painter, bool closed, params float[] points)
        {
            if (points.Length < 4) return;
            painter.BeginPath();
            painter.MoveTo(Point(points[0], points[1]));
            for (int i = 2; i + 1 < points.Length; i += 2) painter.LineTo(Point(points[i], points[i + 1]));
            if (closed) painter.ClosePath();
            painter.Stroke();
        }

    }
}
