using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.RobotMap
{
    /// <summary>A UXML-editable arc whose center is relative to the element's position.</summary>
    [UxmlElement]
    public partial class PhotoResultArcElement : VisualElement
    {
        private Vector2 arcCenter;
        private float arcRadius = 50f;
        private float arcStartDegrees;
        private float arcSweepDegrees = 90f;
        private Color arcColor = Color.white;
        private float arcWidth = 1.5f;
        private float progress = 1f;
        private List<string> excludedElementNames = new List<string>();

        [UxmlAttribute]
        public Vector2 center
        {
            get => arcCenter;
            set
            {
                if (arcCenter == value) return;
                arcCenter = value;
                MarkDirtyRepaint();
            }
        }

        [UxmlAttribute]
        public float radius
        {
            get => arcRadius;
            set
            {
                value = Mathf.Max(0f, value);
                if (Mathf.Approximately(arcRadius, value)) return;
                arcRadius = value;
                MarkDirtyRepaint();
            }
        }

        [UxmlAttribute]
        public float startDegrees
        {
            get => arcStartDegrees;
            set
            {
                if (Mathf.Approximately(arcStartDegrees, value)) return;
                arcStartDegrees = value;
                MarkDirtyRepaint();
            }
        }

        [UxmlAttribute]
        public float sweepDegrees
        {
            get => arcSweepDegrees;
            set
            {
                if (Mathf.Approximately(arcSweepDegrees, value)) return;
                arcSweepDegrees = value;
                MarkDirtyRepaint();
            }
        }

        [UxmlAttribute]
        public Color color
        {
            get => arcColor;
            set
            {
                if (arcColor == value) return;
                arcColor = value;
                MarkDirtyRepaint();
            }
        }

        [UxmlAttribute]
        public float width
        {
            get => arcWidth;
            set
            {
                value = Mathf.Max(0f, value);
                if (Mathf.Approximately(arcWidth, value)) return;
                arcWidth = value;
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

        public PhotoResultArcElement()
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
            float visibleSweep = arcSweepDegrees * progress;
            if (progress <= 0f || arcRadius <= 0f || arcWidth <= 0f ||
                Mathf.Approximately(visibleSweep, 0f)) return;

            float arcLength = Mathf.Abs(visibleSweep) * arcRadius * Mathf.Deg2Rad;
            int segmentCount = Mathf.Max(2, Mathf.CeilToInt(arcLength / 3f));
            List<Rect> excludedRects = GetExcludedWorldRects();

            Painter2D painter = context.painter2D;
            painter.strokeColor = arcColor;
            painter.lineWidth = arcWidth;
            painter.BeginPath();

            bool connected = false;
            for (int i = 0; i <= segmentCount; i++)
            {
                // UI coordinates point down, so increasing angles turn clockwise.
                float angle = (arcStartDegrees + visibleSweep * i / segmentCount) * Mathf.Deg2Rad;
                Vector2 point = arcCenter + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * arcRadius;
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
