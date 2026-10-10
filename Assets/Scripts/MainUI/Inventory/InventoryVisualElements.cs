using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.MainUI.Inventory
{
    [UxmlElement]
    public partial class EmptyInventoryElement : VisualElement
    {
        public EmptyInventoryElement()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += context =>
            {
                Painter2D p = context.painter2D; p.lineWidth = 2; p.strokeColor = Color.white;
                Vector2 c = contentRect.center;
                HudDrawing.Line(p, c - Vector2.right * 7, c + Vector2.right * 7);
                HudDrawing.Line(p, c - Vector2.up * 7, c + Vector2.up * 7);
            };
        }
    }

    [UxmlElement]
    public partial class InventorySelectionElement : VisualElement
    {
        public InventorySelectionElement()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += context =>
            {
                Painter2D p = context.painter2D; p.lineWidth = 4; p.strokeColor = HudDrawing.Yellow;
                float w = contentRect.width, h = contentRect.height, a = 3, length = 32;
                foreach (Vector2 corner in new[] { new Vector2(a, a), new Vector2(w - a, a),
                    new Vector2(w - a, h - a), new Vector2(a, h - a) })
                {
                    Vector2 direction = new Vector2(corner.x < w * .5f ? 1 : -1, corner.y < h * .5f ? 1 : -1);
                    p.BeginPath(); p.MoveTo(corner + Vector2.right * direction.x * length);
                    p.LineTo(corner); p.LineTo(corner + Vector2.up * direction.y * length); p.Stroke();
                }
            };
        }
    }

    [UxmlElement]
    public partial class InventoryOutlineElement : VisualElement
    {
        private bool collapsedValue;
        [UxmlAttribute]
        public bool collapsed
        {
            get => collapsedValue;
            set
            {
                if (collapsedValue == value) return;
                collapsedValue = value;
                MarkDirtyRepaint();
            }
        }
        public InventoryOutlineElement()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }
        private void Draw(MeshGenerationContext context)
        {
            float w = contentRect.width, h = contentRect.height;
            Painter2D p = context.painter2D;
            p.strokeColor = Color.white; p.lineWidth = 2; p.fillColor = Color.black;
            Vector2[] points = collapsed ? new[] {
                new Vector2(2, 2), new Vector2(w * .45f, 2), new Vector2(w - 2, h * .72f),
                new Vector2(w - 2, h - 2), new Vector2(w * .42f, h - 2), new Vector2(2, h * .42f) }
                : new[] { new Vector2(3, 3), new Vector2(w * .3f, 3), new Vector2(w * .35f, 25),
                    new Vector2(w - 55, 25), new Vector2(w - 3, 80), new Vector2(w - 3, h * .4f),
                    new Vector2(w - 18, h * .43f), new Vector2(w - 18, h * .62f),
                    new Vector2(w - 3, h * .65f), new Vector2(w - 3, h - 3), new Vector2(3, h - 3) };
            HudDrawing.Polygon(p, points, true); HudDrawing.Polygon(p, points);
        }
    }
}
