using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.Animals.UI
{
    [UxmlElement]
    public partial class CatalogRecognitionElement : VisualElement
    {
        private readonly Label percent, caption;
        private int value = -1;
        private float displayed, animationBefore, animationAfter, animationElapsed;
        private bool animating;

        [UxmlAttribute]
        public int Value
        {
            get => value;
            set
            {
                animating = false;
                SetDisplayed(value);
            }
        }

        public void Animate(int before, int after)
        {
            animationBefore = Mathf.Clamp(before, 0, 100);
            animationAfter = Mathf.Clamp(after, 0, 100);
            animationElapsed = 0;
            animating = animationBefore != animationAfter;
            SetDisplayed(animationBefore);
        }

        public void Tick(float unscaledDeltaTime)
        {
            if (!animating) return;
            animationElapsed = Mathf.Min(1f, animationElapsed + Mathf.Max(0, unscaledDeltaTime));
            float next = Mathf.Lerp(animationBefore, animationAfter, Mathf.SmoothStep(0, 1, animationElapsed));
            // Show every integer percentage, including when a slow frame crosses multiple ticks.
            next = animationAfter >= animationBefore ? Mathf.Min(next, value + 1f) : Mathf.Max(next, value - 1f);
            SetDisplayed(next);
            if (animationElapsed >= 1f && displayed == animationAfter) animating = false;
        }

        public void FinishAnimation()
        {
            if (!animating) return;
            animating = false;
            SetDisplayed(animationAfter);
        }

        private void SetDisplayed(float amount)
        {
            displayed = Mathf.Clamp(amount, 0, 100);
            int next = Mathf.FloorToInt(displayed);
            if (value != next)
            {
                value = next;
                percent.text = next + "%";
                caption.style.display = next < 100 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            LayoutCaptions();
            MarkDirtyRepaint();
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
            float end = rect.xMin + rect.width * displayed / 100f;
            float left = Mathf.Max(rect.xMin, end - textWidth - 4f);
            percent.style.left = left;
            caption.style.left = Mathf.Max(end + 8f, left + textWidth + 8f);
        }

        private void DrawTicks(MeshGenerationContext context)
        {
            Rect rect = contentRect;
            if (rect.width <= 0f || rect.height <= 0f) return;
            var painter = context.painter2D;
            painter.fillColor = new Color32(212, 184, 84, 255);
            const float halfWidth = 3f;
            for (int tick = 0; tick <= displayed; tick++)
            {
                float x = rect.xMin + rect.width * tick / 100f;
                float left = Mathf.Max(rect.xMin, x - halfWidth);
                float right = Mathf.Min(rect.xMax, x + halfWidth);
                painter.BeginPath();
                painter.MoveTo(new Vector2(left, rect.yMin));
                painter.LineTo(new Vector2(right, rect.yMin));
                painter.LineTo(new Vector2(right, rect.yMax));
                painter.LineTo(new Vector2(left, rect.yMax));
                painter.ClosePath();
                painter.Fill();
            }
        }
    }
}
