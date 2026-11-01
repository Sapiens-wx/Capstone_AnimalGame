using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.Animals.UI
{
    [UxmlElement]
    public partial class ArchiveHeadingElement : Label
    {
        private float endLength = 8f;
        private float bottomOffset = 10f;

        [UxmlAttribute]
        public float EndLength
        {
            get => endLength;
            set { endLength = Mathf.Max(0, value); UpdateSpacing(); }
        }

        [UxmlAttribute]
        public float BottomOffset
        {
            get => bottomOffset;
            set { bottomOffset = value; MarkDirtyRepaint(); }
        }

        public ArchiveHeadingElement()
        {
            AddToClassList("archive-heading");
            UpdateSpacing();
            generateVisualContent += DrawUnderline;
        }

        private void UpdateSpacing()
        {
            style.paddingBottom = endLength + 5f;
            MarkDirtyRepaint();
        }

        private void DrawUnderline(MeshGenerationContext context)
        {
            if (string.IsNullOrEmpty(text)) return;
            float width = Mathf.Min(contentRect.width,
                MeasureTextSize(text, 0, MeasureMode.Undefined, 0, MeasureMode.Undefined).x);
            if (width <= 0) return;
            float x = contentRect.xMin, y = localBound.height - bottomOffset;
            var painter = context.painter2D;
            painter.strokeColor = resolvedStyle.color;
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(x, y - endLength));
            painter.LineTo(new Vector2(x, y));
            painter.LineTo(new Vector2(x + width, y));
            painter.LineTo(new Vector2(x + width, y + endLength));
            painter.Stroke();
        }
    }
}
