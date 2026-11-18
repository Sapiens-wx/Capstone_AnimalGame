using AnimalGame.Animals.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.MainUI
{
    [UxmlElement]
    public partial class DpadHoldHintElement : VisualElement
    {
        private Vector2 direction;
        private readonly ArchiveHoldCircleElement progress;
        private readonly Label caption;

        public DpadHoldHintElement()
        {
            pickingMode = PickingMode.Ignore;
            progress = new ArchiveHoldCircleElement { pickingMode = PickingMode.Ignore };
            progress.style.position = Position.Absolute;
            progress.style.left = progress.style.top = 0;
            progress.style.right = progress.style.bottom = 0;
            progress.style.scale = new Scale(new Vector3(1.2f, 1.2f, 1f));
            progress.style.translate = new Translate(1f, -1f);
            Add(progress);
            caption = new Label { pickingMode = PickingMode.Ignore };
            caption.AddToClassList("dpad-hold-caption");
            Add(caption);
            generateVisualContent += Draw;
        }

        public void SetState(Vector2 held, float holdProgress, string text)
        {
            if (direction != held) { direction = held; MarkDirtyRepaint(); }
            progress.Progress = holdProgress;
            if (text != null) caption.text = text;
        }

        private void Draw(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            painter.lineWidth = 2f;
            Vector2 centre = contentRect.center;
            for (int i = 0; i < 4; i++)
            {
                float angle = i * Mathf.PI * .5f;
                Vector2 x = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) / 3f;
                Vector2 y = new Vector2(-Mathf.Sin(angle), Mathf.Cos(angle)) / 3f;
                bool pressed = i == 0 ? direction.y > .1f : i == 1 ? direction.x > .1f
                    : i == 2 ? direction.y < -.1f : direction.x < -.1f;
                painter.strokeColor = pressed ? (Color)new Color32(212, 184, 84, 255) : new Color(1, 1, 1, .3f);
                painter.BeginPath();
                painter.MoveTo(centre - x * 12 - y * 52);
                painter.LineTo(centre + x * 12 - y * 52);
                painter.LineTo(centre + x * 12 - y * 26);
                painter.LineTo(centre - y * 14);
                painter.LineTo(centre - x * 12 - y * 26);
                painter.ClosePath();
                painter.Stroke();
            }
        }
    }
}
