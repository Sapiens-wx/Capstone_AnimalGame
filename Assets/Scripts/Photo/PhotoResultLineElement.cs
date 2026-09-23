using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.RobotMap
{
    /// <summary>A UXML-editable line whose points are relative to the element's position.</summary>
    [UxmlElement]
    public partial class PhotoResultLineElement : VisualElement
    {
        private Vector2 lineStartPoint;
        private Vector2 lineEndPoint = new Vector2(100f, 0f);
        private Color lineColor = Color.white;
        private float lineWidth = 1.5f;
        private float progress = 1f;
        private List<string> excludedElementNames = new List<string>();
        private Vector2[] points = Array.Empty<Vector2>();

        [UxmlAttribute]
        public Vector2 startPoint
        {
            get => lineStartPoint;
            set
            {
                if (lineStartPoint == value) return;
                lineStartPoint = value;
                RebuildGeometry();
            }
        }

        [UxmlAttribute]
        public Vector2 endPoint
        {
            get => lineEndPoint;
            set
            {
                if (lineEndPoint == value) return;
                lineEndPoint = value;
                RebuildGeometry();
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
        public List<string> exclusions
        {
            get => excludedElementNames;
            set
            {
                excludedElementNames = value ?? new List<string>();
                MarkDirtyRepaint();
            }
        }

        public PhotoResultLineElement()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(RebuildGeometry);
            RebuildGeometry();
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
            if (progress <= 0f || lineWidth <= 0f || points.Length < 2) return;

            int lastPointIndex = Mathf.Min(
                Mathf.CeilToInt((points.Length - 1) * progress),
                points.Length - 1);
            List<Rect> excludedRects = GetExcludedWorldRects();
            Painter2D painter = context.painter2D;
            painter.strokeColor = lineColor;
            painter.lineWidth = lineWidth;
            painter.BeginPath();

            bool connected = false;
            for (int i = 0; i <= lastPointIndex; i++)
            {
                Vector2 point = points[i];
                if (IsExcluded(point, excludedRects))
                {
                    connected = false;
                    continue;
                }

                if (!connected) painter.MoveTo(point);
                else painter.LineTo(point);
                connected = true;
            }

            painter.Stroke();
        }

        private void RebuildGeometry(GeometryChangedEvent evt)
        {
            RebuildGeometry();
        }

        private void RebuildGeometry()
        {
            float lineLength = Vector2.Distance(lineStartPoint, lineEndPoint);
            int segmentCount = Mathf.Max(1, Mathf.CeilToInt(lineLength / 30f));
            points = new Vector2[segmentCount + 1];

            for (int i = 0; i <= segmentCount; i++)
                points[i] = Vector2.Lerp(lineStartPoint, lineEndPoint, (float)i / segmentCount);

            MarkDirtyRepaint();
        }

        private List<Rect> GetExcludedWorldRects()
        {
            var rects = new List<Rect>(excludedElementNames.Count);
            VisualElement root = this;
            while (root.parent != null) root = root.parent;

            foreach (string elementName in excludedElementNames)
            {
                if (string.IsNullOrEmpty(elementName)) continue;
                PhotoResultExcludeElement element = root.Q<PhotoResultExcludeElement>(elementName);
                if (element != null) rects.Add(element.worldBound);
            }

            return rects;
        }

        private bool IsExcluded(Vector2 localPoint, List<Rect> excludedRects)
        {
            Vector2 worldPoint = this.LocalToWorld(localPoint);
            foreach (Rect rect in excludedRects)
                if (rect.Contains(worldPoint)) return true;
            return false;
        }
    }
}
