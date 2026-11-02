using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.Animals.UI
{
    [UxmlElement]
    public partial class CatalogHeadingElement : Label
    {
        private bool flip;
        [UxmlAttribute]
        public bool Flip
        {
            get => flip;
            set { flip = value; MarkDirtyRepaint(); }
        }

        public CatalogHeadingElement()
        {
            AddToClassList("catalog-heading");
            generateVisualContent += DrawUnderline;
        }

        private void DrawUnderline(MeshGenerationContext context)
        {
            if (string.IsNullOrEmpty(text)) return;
            float width = Mathf.Min(contentRect.width,
                MeasureTextSize(text, 0, MeasureMode.Undefined, 0, MeasureMode.Undefined).x);
            float x = flip ? contentRect.xMax - width : contentRect.xMin;
            float y = contentRect.yMax + 3f;
            var painter = context.painter2D;
            painter.strokeColor = resolvedStyle.color;
            painter.lineWidth = 1f;
            painter.BeginPath();
            const float LINE_PADDING = 6;
            if (flip) {
                painter.MoveTo(new Vector2(x, y));
                painter.LineTo(new Vector2(x + width + LINE_PADDING, y));
                painter.LineTo(new Vector2(x + width + LINE_PADDING, y - 7f));
            } else {
                painter.MoveTo(new Vector2(x - LINE_PADDING, y - 7f));
                painter.LineTo(new Vector2(x - LINE_PADDING, y));
                painter.LineTo(new Vector2(x + width, y));
            }
            painter.Stroke();
        }
    }
}
