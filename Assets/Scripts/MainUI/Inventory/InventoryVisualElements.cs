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
            if (w <= 0 || h <= 0) return;
            Painter2D p = context.painter2D;
            p.strokeColor = Color.white; p.lineWidth = 2; p.fillColor = Color.black;
            if (collapsed)
            {
                Vector2[] collapsedPoints = {
                new Vector2(2, 2), new Vector2(w * .45f, 2), new Vector2(w - 2, h * .72f),
                new Vector2(w - 2, h - 2), new Vector2(w * .42f, h - 2), new Vector2(2, h * .42f) };
                HudDrawing.Polygon(p, collapsedPoints, true); HudDrawing.Polygon(p, collapsedPoints);
                return;
            }

            // Coordinates share the inventory's 570x780 design space with its 2x4 grid.
            Vector2 P(float x, float y) => new Vector2(x * w / 570, y * h / 780);
            Vector2[] corner = { P(18, 696), P(2, 714), P(2, 766), P(14, 778), P(172, 778), P(188, 763), P(18, 763) };
            HudDrawing.Polygon(p, corner, true); HudDrawing.Polygon(p, corner);
            Vector2[] points = { P(18, 18), P(205, 18), P(223, 36), P(535, 36),
                P(567, 88), P(567, 350), P(542, 392), P(542, 745), P(526, 763), P(18, 763) };
            HudDrawing.Polygon(p, points, true); HudDrawing.Polygon(p, points);

            p.fillColor = Color.white;
            for (int row = 0; row < 4; row++)
            {
                float y = 55 + row * 170;
                HudDrawing.Polygon(p, new[] { P(12, y), P(18, y), P(18, y + 110), P(12, y + 100) }, true);
            }
            foreach (float x in new[] { 190f, 438f })
                HudDrawing.Polygon(p, new[] { P(x, 763), P(x + 54, 763), P(x + 54, 768), P(x + 8, 768) }, true);
        }
    }
}
