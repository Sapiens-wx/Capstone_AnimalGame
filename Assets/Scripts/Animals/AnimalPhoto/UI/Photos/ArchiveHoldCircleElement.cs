using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.Animals.UI
{
    [UxmlElement]
    public partial class ArchiveHoldCircleElement : Label
    {
        private float progress;
        [UxmlAttribute]
        public float Progress
        {
            get => progress;
            set { progress = Mathf.Clamp01(value); MarkDirtyRepaint(); }
        }

        public ArchiveHoldCircleElement()
        {
            generateVisualContent += DrawProgress;
        }

        private void DrawProgress(MeshGenerationContext context)
        {
            if (progress <= 0) return;
            const float thickness = 3f;
            var center = new Vector2(layout.width, layout.height) * .5f;
            float radius = Mathf.Min(center.x, center.y) - resolvedStyle.borderLeftWidth - thickness * .5f;
            var painter = context.painter2D;
            painter.strokeColor = new Color32(212, 184, 84, 255);
            painter.lineWidth = thickness;
            painter.BeginPath();
            painter.MoveTo(center + Vector2.up * -radius);
            int segments = Mathf.CeilToInt(96 * progress);
            for (int i = 1; i <= segments; i++)
            {
                float angle = progress * Mathf.PI * 2 * i / segments - Mathf.PI * .5f;
                painter.LineTo(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }
            painter.Stroke();
        }
    }
}
