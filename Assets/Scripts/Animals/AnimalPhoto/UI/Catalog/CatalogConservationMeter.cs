using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.Animals.UI
{
    [UxmlElement]
    public partial class CatalogConservationMeter : VisualElement
    {
        private static readonly string[] Statuses =
        {
            "Least Concern", "Near Threatened", "Vulnerable", "Endangered", "Critically Endangered"
        };
        private int selected = -1;

        public CatalogConservationMeter()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public void Bind(string status, bool unlocked)
        {
            selected = unlocked ? Array.FindIndex(Statuses,
                value => string.Equals(value, status?.Trim(), StringComparison.OrdinalIgnoreCase)) : -1;
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext context)
        {
            Rect rect = contentRect;
            if (rect.width <= 6 || rect.height <= 0) return;
            float left = rect.xMin + 3, right = rect.xMax - 3, top = rect.yMin + 1.4f;
            var painter = context.painter2D;
            painter.strokeColor = Color.white;
            painter.lineWidth = 2;
            painter.lineCap = LineCap.Butt;
            painter.BeginPath();
            painter.MoveTo(new Vector2(left, top));
            painter.LineTo(new Vector2(right, top));
            for (int i = 0; i < Statuses.Length; i++)
            {
                float x = Mathf.Lerp(left, right, i / 4f);
                painter.MoveTo(new Vector2(x, top));
                painter.LineTo(new Vector2(x, top + 11.2f));
            }
            painter.Stroke();
            if (selected < 0) return;
            float arrowX = Mathf.Lerp(left, right, selected / 4f);
            painter.fillColor = new Color32(212, 184, 84, 255);
            painter.BeginPath();
            painter.MoveTo(new Vector2(arrowX, top + 16.8f));
            painter.LineTo(new Vector2(arrowX + 6, top + 30.8f));
            painter.LineTo(new Vector2(arrowX - 6, top + 30.8f));
            painter.ClosePath();
            painter.Fill();
        }
    }
}
