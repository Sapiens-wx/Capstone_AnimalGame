using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.World
{
    [UxmlElement]
    public partial class StationSyncOutline : VisualElement
    {
        private static readonly CustomStyleProperty<Color> Stroke = new("--station-outline");
        private static readonly CustomStyleProperty<Color> Fill = new("--station-fill");
        private const float Slope = .21256f; // tan(12°), shared with the header divider angle.

        private Color stroke;
        private Color fill;

        public StationSyncOutline()
        {
            generateVisualContent += DrawOutline;
            RegisterCallback<CustomStyleResolvedEvent>(ReadColors);
        }

        private void ReadColors(CustomStyleResolvedEvent evt)
        {
            evt.customStyle.TryGetValue(Stroke, out stroke);
            evt.customStyle.TryGetValue(Fill, out fill);
            MarkDirtyRepaint();
        }

        private void DrawOutline(MeshGenerationContext context)
        {
            Rect bounds = contentRect;
            float slopeOffset = bounds.height * Slope;
            float left = bounds.xMin + .5f;
            float right = bounds.xMax - .5f;
            float top = bounds.yMin + .5f;
            float bottom = bounds.yMax - .5f;
            var painter = context.painter2D;
            painter.BeginPath();
            painter.MoveTo(new Vector2(left + slopeOffset, top));
            painter.LineTo(new Vector2(right, top));
            painter.LineTo(new Vector2(right - slopeOffset, bottom));
            painter.LineTo(new Vector2(left, bottom));
            painter.ClosePath();
            if (fill.a > 0f)
            {
                painter.fillColor = fill;
                painter.Fill();
            }

            painter.strokeColor = stroke;
            painter.lineWidth = 1f;
            painter.Stroke();
        }
    }
}
