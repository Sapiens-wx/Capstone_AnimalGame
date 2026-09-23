using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.RobotMap
{
    /// <summary>A full-element square grid with an animated reveal.</summary>
    [UxmlElement]
    public partial class PhotoResultGridElement : VisualElement
    {
        private Vector2 gridOffset;
        private float cellLength = 56f;
        private float lineWidth = 1f;
        private Color lineColor = new Color(0.55f, 0.59f, 0.57f, 0.23f);
        private float progress = 1f;

        [UxmlAttribute]
        public Vector2 offset
        {
            get => gridOffset;
            set
            {
                if (gridOffset == value) return;
                gridOffset = value;
                MarkDirtyRepaint();
            }
        }

        [UxmlAttribute]
        public float gridLength
        {
            get => cellLength;
            set
            {
                value = Mathf.Max(0f, value);
                if (Mathf.Approximately(cellLength, value)) return;
                cellLength = value;
                MarkDirtyRepaint();
            }
        }

        [UxmlAttribute]
        public float width
        {
            get => lineWidth;
            set
            {
                value = Mathf.Max(0f, value);
                if (Mathf.Approximately(lineWidth, value)) return;
                lineWidth = value;
                MarkDirtyRepaint();
            }
        }

        [UxmlAttribute]
        public Color color
        {
            get => lineColor;
            set
            {
                if (lineColor == value) return;
                lineColor = value;
                MarkDirtyRepaint();
            }
        }

        public PhotoResultGridElement()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public void Reveal(float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(progress, value)) return;
            progress = value;
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext context)
        {
            Rect bounds = contentRect;
            if (progress <= 0f || cellLength <= 0f || lineWidth <= 0f ||
                bounds.width <= 0f || bounds.height <= 0f) return;

            float firstX = bounds.xMin + Mathf.Repeat(gridOffset.x, cellLength);
            float firstY = bounds.yMin + Mathf.Repeat(gridOffset.y, cellLength);
            float visibleWidth = bounds.width * progress;
            float visibleHeight = bounds.height * progress;

            Painter2D painter = context.painter2D;
            painter.strokeColor = lineColor;
            painter.lineWidth = lineWidth;
            painter.BeginPath();

            for (float x = firstX; x <= bounds.xMax; x += cellLength)
            {
                painter.MoveTo(new Vector2(x, bounds.yMin));
                painter.LineTo(new Vector2(x, bounds.yMin + visibleHeight));
            }

            for (float y = firstY; y <= bounds.yMax; y += cellLength)
            {
                painter.MoveTo(new Vector2(bounds.xMin, y));
                painter.LineTo(new Vector2(bounds.xMin + visibleWidth, y));
            }

            painter.Stroke();
        }
    }
}
