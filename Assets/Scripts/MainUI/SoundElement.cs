using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.MainUI
{
    /// <summary>Sound display with two white end lines and a static center line; dimensions follow its layout.</summary>
    [UxmlElement]
    public partial class SoundElement : VisualElement
    {
        private float lineWidthValue = 2;
        /// <summary>Stroke width of the white end lines only.</summary>
        [UxmlAttribute]
        public float lineWidth
        {
            get => lineWidthValue;
            set
            {
                value = float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Max(0, value);
                if (lineWidthValue == value) return;
                lineWidthValue = value;
                MarkDirtyRepaint();
            }
        }

        private float lineLengthValue = 72;
        [UxmlAttribute]
        public float lineLength
        {
            get => lineLengthValue;
            set
            {
                value = float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Max(0, value);
                if (lineLengthValue == value) return;
                lineLengthValue = value;
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
            float halfLength = Mathf.Min(lineLength, bottom - top) * .5f;
            Painter2D p = context.painter2D;
            p.lineWidth = 2;
            p.strokeColor = HudDrawing.Yellow;
            HudDrawing.Line(p, new Vector2(left, rect.center.y), new Vector2(right, rect.center.y));
            if (lineWidth <= 0) return;
            p.lineWidth = lineWidth;
            p.strokeColor = Color.white;
            HudDrawing.Line(p, new Vector2(left, rect.center.y - halfLength), new Vector2(left, rect.center.y + halfLength));
            HudDrawing.Line(p, new Vector2(right, rect.center.y - halfLength), new Vector2(right, rect.center.y + halfLength));
        }
    }
}
