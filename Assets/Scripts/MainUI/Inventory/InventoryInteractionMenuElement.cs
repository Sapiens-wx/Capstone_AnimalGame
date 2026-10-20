using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.MainUI.Inventory
{
    [UxmlElement]
    public partial class InventoryInteractionButtonElement : VisualElement
    {
        private readonly Label label;
        private bool selected;
        [UxmlAttribute] public string text { get => label.text; set => label.text = value; }
        public event Action Pressed;
        public event Action Hovered;
        public bool Selected
        {
            get => selected;
            set { selected = value; MarkDirtyRepaint(); }
        }

        public InventoryInteractionButtonElement()
        {
            pickingMode = PickingMode.Position;
            style.flexGrow = 1; style.flexBasis = 0; style.minHeight = 0;
            label = new Label { pickingMode = PickingMode.Ignore };
            label.style.flexGrow = 1;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.fontSize = 24; label.style.color = Color.white;
            Add(label);
            RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (evt.deltaPosition.sqrMagnitude > 0) Hovered?.Invoke();
                evt.StopPropagation();
            });
            RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0) Pressed?.Invoke();
                evt.StopPropagation();
            });
            generateVisualContent += context =>
            {
                Painter2D p = context.painter2D;
                p.strokeColor = Color.white;
                p.lineWidth = 1;
                float inset = .5f;
                p.BeginPath(); p.MoveTo(new Vector2(inset, inset));
                p.LineTo(new Vector2(contentRect.width - inset, inset));
                p.LineTo(new Vector2(contentRect.width - inset, contentRect.height - inset));
                p.LineTo(new Vector2(inset, contentRect.height - inset));
                p.ClosePath(); p.Stroke();
            };
        }
    }

    [UxmlElement]
    public partial class InventoryInteractionMenuElement : VisualElement
    {
        public event Action<int> Pressed;
        public event Action<int> Hovered;
        private readonly InventoryInteractionButtonElement[] buttons = new InventoryInteractionButtonElement[4];
        private readonly VisualElement highlight = new VisualElement { pickingMode = PickingMode.Ignore };
        public int SelectedIndex { get; private set; }

        public InventoryInteractionMenuElement()
        {
            style.position = Position.Absolute;
            style.backgroundColor = Color.black;
            style.paddingLeft = style.paddingRight = style.paddingTop = style.paddingBottom = 5;
            style.borderLeftWidth = style.borderRightWidth = style.borderTopWidth = style.borderBottomWidth = 2;
            style.borderLeftColor = style.borderRightColor = style.borderTopColor = style.borderBottomColor = Color.white;
            string[] titles = { "USE", "INFO", "MOVE", "DISCARD" };
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                var button = new InventoryInteractionButtonElement { name = titles[i], text = titles[i] };
                button.Pressed += () => Pressed?.Invoke(index);
                button.Hovered += () => Hovered?.Invoke(index);
                button.RegisterCallback<GeometryChangedEvent>(_ => highlight.MarkDirtyRepaint());
                buttons[i] = button; Add(button);
            }
            // Paint after the buttons so the yellow band overlaps both white borders.
            highlight.style.position = Position.Absolute;
            highlight.style.left = highlight.style.right = highlight.style.top = highlight.style.bottom = 0;
            highlight.style.overflow = Overflow.Visible;
            highlight.generateVisualContent += DrawHighlight;
            Add(highlight);
            RegisterCallback<GeometryChangedEvent>(_ => highlight.MarkDirtyRepaint());
            Select(0);
        }

        public void Select(int index)
        {
            SelectedIndex = Mathf.Clamp(index, 0, 3);
            for (int i = 0; i < 4; i++) buttons[i].Selected = i == SelectedIndex;
            highlight.MarkDirtyRepaint();
        }

        private void DrawHighlight(MeshGenerationContext context)
        {
            var button = buttons[SelectedIndex];
            if (button.layout.width <= 0 || button.layout.height <= 0) return;
            Rect inner = button.ChangeCoordinatesTo(highlight, button.contentRect);
            // Include the one-pixel button border in the yellow band.
            inner.xMin += 1; inner.yMin += 1; inner.xMax -= 1; inner.yMax -= 1;
            Rect outer = this.ChangeCoordinatesTo(highlight, new Rect(Vector2.zero, layout.size));
            float thickness = inner.xMin - outer.xMin;
            outer.yMin = Mathf.Max(outer.yMin, inner.yMin - thickness);
            outer.yMax = Mathf.Min(outer.yMax, inner.yMax + thickness);
            Painter2D painter = context.painter2D;
            painter.fillColor = HudDrawing.Yellow;
            Fill(new Rect(outer.xMin, outer.yMin, outer.width, inner.yMin - outer.yMin));
            Fill(new Rect(outer.xMin, inner.yMax, outer.width, outer.yMax - inner.yMax));
            Fill(new Rect(outer.xMin, inner.yMin, inner.xMin - outer.xMin, inner.height));
            Fill(new Rect(inner.xMax, inner.yMin, outer.xMax - inner.xMax, inner.height));

            void Fill(Rect rect)
            {
                if (rect.width <= 0 || rect.height <= 0) return;
                painter.BeginPath(); painter.MoveTo(rect.min);
                painter.LineTo(new Vector2(rect.xMax, rect.yMin)); painter.LineTo(rect.max);
                painter.LineTo(new Vector2(rect.xMin, rect.yMax));
                painter.ClosePath(); painter.Fill();
            }
        }
    }
}
