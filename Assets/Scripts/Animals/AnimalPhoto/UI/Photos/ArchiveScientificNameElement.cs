using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.Animals.UI
{
    [UxmlElement]
    public partial class ArchiveScientificNameElement : Label
    {
        private float bottomLineRatio = .4f;

        [UxmlAttribute]
        public float BottomLineRatio
        {
            get => bottomLineRatio;
            set { bottomLineRatio = Mathf.Clamp01(value); MarkDirtyRepaint(); }
        }

        public ArchiveScientificNameElement()
        {
            AddToClassList("photo-scientific-name");
            generateVisualContent += DrawBottomBorder;
        }

        private void DrawBottomBorder(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            painter.strokeColor = resolvedStyle.borderTopColor;
            painter.lineWidth = resolvedStyle.borderTopWidth;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0, layout.height - .5f));
            painter.LineTo(new Vector2(layout.width * bottomLineRatio, layout.height - .5f));
            painter.Stroke();
        }
    }
}
