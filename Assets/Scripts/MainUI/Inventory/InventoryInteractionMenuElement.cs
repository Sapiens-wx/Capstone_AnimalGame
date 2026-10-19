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
                p.strokeColor = selected ? HudDrawing.Yellow : Color.white;
                p.lineWidth = selected ? 5 : 1;
                float inset = selected ? 2.5f : .5f;
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
                buttons[i] = button; Add(button);
            }
            Select(0);
        }

        public void Select(int index)
        {
            SelectedIndex = Mathf.Clamp(index, 0, 3);
            for (int i = 0; i < 4; i++) buttons[i].Selected = i == SelectedIndex;
        }
    }
}
