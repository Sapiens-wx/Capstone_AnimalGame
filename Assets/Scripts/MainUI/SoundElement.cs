using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.MainUI
{
    /// <summary>Chamfered sound panel with a static center line; dimensions follow its layout.</summary>
    [UxmlElement]
    public partial class SoundElement : VisualElement
    {
        private float chamferValue = 36;
        [UxmlAttribute]
        public float chamfer
        {
            get => chamferValue;
            set
            {
                value = float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Max(0, value);
                if (chamferValue == value) return;
                chamferValue = value;
                MarkDirtyRepaint();
            }
        }

        public SoundElement()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        private void Draw(MeshGenerationContext context)
        {
            Rect rect = contentRect;
            if (rect.width <= 2 || rect.height <= 2) return;
            float left = rect.xMin + 1, right = rect.xMax - 1;
            float top = rect.yMin + 1, bottom = rect.yMax - 1;
            float cut = Mathf.Min(chamfer, Mathf.Min(right - left, bottom - top) * .5f);
            Painter2D p = context.painter2D;
            p.lineWidth = 2;
            p.strokeColor = HudDrawing.Yellow;
            HudDrawing.Line(p, new Vector2(left, rect.center.y), new Vector2(right, rect.center.y));
            p.strokeColor = Color.white;
            HudDrawing.Polygon(p, new[] {
                new Vector2(left + cut, top), new Vector2(right - cut, top),
                new Vector2(right, top + cut), new Vector2(right, bottom - cut),
                new Vector2(right - cut, bottom), new Vector2(left + cut, bottom),
                new Vector2(left, bottom - cut), new Vector2(left, top + cut) });
        }
    }
}
