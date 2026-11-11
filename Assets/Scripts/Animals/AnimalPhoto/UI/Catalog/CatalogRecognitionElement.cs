using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.Animals.UI
{
    [UxmlElement]
    public partial class CatalogRecognitionElement : VisualElement
    {
        private readonly Label percent, caption;
        private int value = -1;

        [UxmlAttribute]
        public int Value
        {
            get => value;
            set
            {
                int next = Mathf.Clamp(value, 0, 100);
                if (this.value == next) return;
                this.value = next;
                percent.text = next + "%";
                caption.style.display = next < 100 ? DisplayStyle.Flex : DisplayStyle.None;
                LayoutCaptions();
                MarkDirtyRepaint();
            }
        }

        public CatalogRecognitionElement()
        {
            pickingMode = PickingMode.Ignore;
            percent = new Label { name = "recognition-percent", pickingMode = PickingMode.Ignore };
            caption = new Label("Take more photos for recognition")
                { name = "recognition-caption", pickingMode = PickingMode.Ignore };
            Add(percent);
            Add(caption);
            percent.RegisterCallback<GeometryChangedEvent>(_ => LayoutCaptions());
            RegisterCallback<GeometryChangedEvent>(_ =>
            {
                LayoutCaptions();
                MarkDirtyRepaint();
            });
            generateVisualContent += DrawTicks;
            Value = 0;
        }

        private void LayoutCaptions()
        {
            Rect rect = contentRect;
            float textWidth = percent.layout.width;
            if (rect.width <= 0f || float.IsNaN(textWidth)) return;
            float end = rect.xMin + rect.width * value / 100f;
            float left = Mathf.Max(rect.xMin, end - textWidth - 4f);
            percent.style.left = left;
            caption.style.left = Mathf.Max(end + 8f, left + textWidth + 8f);
        }

        private void DrawTicks(MeshGenerationContext context)
        {
            Rect rect = contentRect;
            if (rect.width <= 0f || rect.height <= 0f) return;
            var painter = context.painter2D;
            painter.strokeColor = new Color32(212, 184, 84, 255);
            painter.lineWidth = 3f;
            painter.lineCap = LineCap.Butt;
            painter.BeginPath();
            for (int tick = 1; tick <= value; tick++)
            {
                float x = rect.xMin + rect.width * tick / 100f;
                painter.MoveTo(new Vector2(x, rect.yMin));
                painter.LineTo(new Vector2(x, rect.yMax));
            }
            painter.Stroke();
        }
    }
}
