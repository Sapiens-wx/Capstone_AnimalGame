using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.RobotMap
{
    /// <summary>Independent vector stroke with reveal and explicit exclusion rectangles.</summary>
    internal sealed class PhotoResultVectorElement : VisualElement
    {
        private readonly Vector2[] points;
        private readonly List<Rect> exclusions;
        private readonly Color color;
        private readonly float width;
        private float progress;

        public PhotoResultVectorElement(string id, Vector2[] points, Color color,
            float width = 1.5f, List<Rect> exclusions = null)
        {
            name = id;
            this.points = points;
            this.color = color;
            this.width = width;
            this.exclusions = exclusions;
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = style.top = 0;
            style.width = 1920;
            style.height = 1080;
            generateVisualContent += Draw;
        }

        public void Reveal(float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(progress, value)) return;
            progress = value;
            MarkDirtyRepaint();
        }

        private bool IsExcluded(Vector2 point)
        {
            if (exclusions == null) return false;
            foreach (Rect rect in exclusions)
                if (rect.Contains(point)) return true;
            return false;
        }

        private void Draw(MeshGenerationContext context)
        {
            if (progress <= 0 || points.Length < 2) return;
            Painter2D painter = context.painter2D;
            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.BeginPath();
            float remaining = (points.Length - 1) * progress;
            bool connected = false;
            for (int i = 1; i < points.Length && remaining > 0; i++, remaining--)
            {
                Vector2 start = points[i - 1];
                Vector2 end = Vector2.Lerp(start, points[i], Mathf.Min(1, remaining));
                // Short segments make gaps follow intersecting labels without a black plaque.
                if (IsExcluded(start) || IsExcluded(end)) { connected = false; continue; }
                if (!connected) painter.MoveTo(start);
                painter.LineTo(end);
                connected = true;
            }
            painter.Stroke();
        }

        public static Vector2[] Line(Vector2 start, Vector2 end)
        {
            int count = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(start, end) / 3f));
            var points = new Vector2[count + 1];
            for (int i = 0; i <= count; i++) points[i] = Vector2.Lerp(start, end, (float)i / count);
            return points;
        }

        public static Vector2[] Arc(Vector2 center, float radius, float startDegrees, float sweepDegrees)
        {
            int count = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(sweepDegrees) * radius * Mathf.Deg2Rad / 3f));
            var points = new Vector2[count + 1];
            for (int i = 0; i <= count; i++)
            {
                // UI coordinates point down: increasing angle is clockwise; 180 starts at left.
                float angle = (startDegrees + sweepDegrees * i / count) * Mathf.Deg2Rad;
                points[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }
            return points;
        }
    }
}
