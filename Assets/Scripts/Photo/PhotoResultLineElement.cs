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
        private readonly PhotoResultExclusionCache exclusionCache;

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
                RebuildGeometry();
            }
        }

        public PhotoResultLineElement()
        {
            pickingMode = PickingMode.Ignore;
            exclusionCache = new PhotoResultExclusionCache(this, false, MarkDirtyRepaint);
            generateVisualContent += Draw;
            RebuildGeometry();
        }

        public void Reveal(float value)
        {
            value = Mathf.Clamp01(value);
            if (progress == value) return;
            progress = value;
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext context)
        {
            if (progress <= 0f || lineWidth <= 0f || points.Length < 2 || !exclusionCache.IsReady) return;

            int lastPointIndex = Mathf.Min(
                Mathf.FloorToInt((points.Length - 1) * progress),
                points.Length - 1);
            bool[] excluded = exclusionCache.Excluded;
            Painter2D painter = context.painter2D;
            painter.strokeColor = lineColor;
            painter.lineWidth = lineWidth;
            painter.BeginPath();

            bool connected = false;
            for (int i = 0; i <= lastPointIndex; i++)
            {
                Vector2 point = points[i];
                if (excluded.Length != 0 && excluded[i])
                {
                    connected = false;
                    continue;
                }

                if (!connected) painter.MoveTo(point);
                else painter.LineTo(point);
                connected = true;
            }

            // Unlike an arc, the tip changes even within one sampled segment.
            if (connected && progress < 1f)
            {
                Vector2 tip = Vector2.Lerp(lineStartPoint, lineEndPoint, progress);
                if (!exclusionCache.Contains(tip)) painter.LineTo(tip);
            }
            painter.Stroke();
        }

        private void RebuildGeometry()
        {
            if (excludedElementNames.Count == 0)
            {
                points = new[] { lineStartPoint, lineEndPoint };
                exclusionCache.Configure(excludedElementNames, points);
                MarkDirtyRepaint();
                return;
            }
            float lineLength = Vector2.Distance(lineStartPoint, lineEndPoint);
            int segmentCount = Mathf.Max(1, Mathf.CeilToInt(lineLength / 30f));
            points = new Vector2[segmentCount + 1];

            for (int i = 0; i <= segmentCount; i++)
                points[i] = Vector2.Lerp(lineStartPoint, lineEndPoint, (float)i / segmentCount);

            exclusionCache.Configure(excludedElementNames, points);
            MarkDirtyRepaint();
        }
    }
}
