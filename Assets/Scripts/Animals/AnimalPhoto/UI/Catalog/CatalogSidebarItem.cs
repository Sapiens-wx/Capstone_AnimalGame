using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.Animals.UI
{
    [UxmlElement]
    public partial class CatalogSidebarItem : Button
    {
        private bool selected;
        [UxmlAttribute]
        public bool Selected
        {
            get => selected;
            set { selected = value; MarkDirtyRepaint(); }
        }

        public CatalogSidebarItem()
        {
            AddToClassList("catalog-sidebar-item");
            generateVisualContent += DrawSelection;
        }

        private void DrawSelection(MeshGenerationContext context)
        {
            if (!selected) return;
            const float inset = 4f;
            var painter = context.painter2D;
            painter.strokeColor = new Color32(212, 184, 84, 255);
            painter.lineWidth = 6f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(inset, inset));
            painter.LineTo(new Vector2(layout.width - inset, inset));
            painter.LineTo(new Vector2(layout.width - inset, layout.height - inset));
            painter.LineTo(new Vector2(inset, layout.height - inset));
            painter.ClosePath();
            painter.Stroke();
        }
    }
}
