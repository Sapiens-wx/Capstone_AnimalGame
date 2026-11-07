using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.Animals.UI
{
    [UxmlElement]
    public partial class ArchiveScientificNameElement : Label
    {
        private float topLineRatio = 1f;
        private float bottomLineRatio = .4f;

        [UxmlAttribute]
        public float TopLineRatio
        {
            get => topLineRatio;
            set { topLineRatio = Mathf.Clamp01(value); MarkDirtyRepaint(); }
        }

        [UxmlAttribute]
        public float BottomLineRatio
        {
            get => bottomLineRatio;
            set { bottomLineRatio = Mathf.Clamp01(value); MarkDirtyRepaint(); }
        }

        public ArchiveScientificNameElement()
        {
            AddToClassList("photo-scientific-name");
            generateVisualContent += DrawBorders;
        }

        private void DrawBorders(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            painter.strokeColor = resolvedStyle.borderLeftColor;
            painter.lineWidth = resolvedStyle.borderLeftWidth;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0, .5f));
            painter.LineTo(new Vector2(layout.width * topLineRatio, .5f));
            painter.Stroke();
            painter.BeginPath();
            painter.MoveTo(new Vector2(0, layout.height - .5f));
            painter.LineTo(new Vector2(layout.width * bottomLineRatio, layout.height - .5f));
            painter.Stroke();
        }
    }
}
