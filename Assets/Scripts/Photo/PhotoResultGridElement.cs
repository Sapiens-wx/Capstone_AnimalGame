using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.RobotMap
{
    /// <summary>A full-element square grid.</summary>
    [UxmlElement]
    public partial class PhotoResultGridElement : VisualElement
    {
        private Vector2 gridOffset;
        private float cellLength = 56f;
        private float lineWidth = 1f;
        private Color lineColor = new Color(0.55f, 0.59f, 0.57f, 0.23f);
        private Vector2[] points = Array.Empty<Vector2>();

        [UxmlAttribute]
        public Vector2 offset
        {
            get => gridOffset;
            set
            {
                if (gridOffset == value) return;
                gridOffset = value;
                RebuildGeometry();
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
                RebuildGeometry();
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
            RegisterCallback<GeometryChangedEvent>(RebuildGeometry);
            RebuildGeometry();
        }

        private void Draw(MeshGenerationContext context)
        {
            if (lineWidth <= 0f || points.Length == 0) return;

            Painter2D painter = context.painter2D;
            painter.strokeColor = lineColor;
            painter.lineWidth = lineWidth;
            painter.BeginPath();

            for (int i = 0; i < points.Length; i += 2)
            {
                painter.MoveTo(points[i]);
                painter.LineTo(points[i + 1]);
            }

            painter.Stroke();
        }

        private void RebuildGeometry(GeometryChangedEvent evt)
        {
            RebuildGeometry();
        }

        private void RebuildGeometry()
        {
            Rect bounds = contentRect;
            if (cellLength <= 0f || bounds.width <= 0f || bounds.height <= 0f)
            {
                points = Array.Empty<Vector2>();
                MarkDirtyRepaint();
                return;
            }

            float firstX = bounds.xMin + Mathf.Repeat(gridOffset.x, cellLength);
            float firstY = bounds.yMin + Mathf.Repeat(gridOffset.y, cellLength);
            int verticalLineCount = firstX <= bounds.xMax
                ? Mathf.FloorToInt((bounds.xMax - firstX) / cellLength) + 1
                : 0;
            int horizontalLineCount = firstY <= bounds.yMax
                ? Mathf.FloorToInt((bounds.yMax - firstY) / cellLength) + 1
                : 0;
            points = new Vector2[(verticalLineCount + horizontalLineCount) * 2];

            int pointIndex = 0;
            for (int line = 0; line < verticalLineCount; line++)
            {
                float x = firstX + line * cellLength;
                points[pointIndex++] = new Vector2(x, bounds.yMin);
                points[pointIndex++] = new Vector2(x, bounds.yMax);
            }

            for (int line = 0; line < horizontalLineCount; line++)
            {
                float y = firstY + line * cellLength;
                points[pointIndex++] = new Vector2(bounds.xMin, y);
                points[pointIndex++] = new Vector2(bounds.xMax, y);
            }

            MarkDirtyRepaint();
        }
    }
}
