using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.MainUI.Inventory
{
    internal sealed class InventoryItemInfoView : IDisposable
    {
        private readonly VisualElement root;
        private readonly Image image;
        private readonly Label address, title, quantity, description;
        private readonly ScrollView body;

        public InventoryItemInfoView(VisualElement parent, Action back)
        {
            root = Resources.Load<VisualTreeAsset>("UI/Main/InventoryItemInfo").Instantiate();
            root.StretchToParentSize();
            root.Q("item-info-frame").generateVisualContent += DrawFrame;
            address = root.Q<Label>("item-info-address");
            title = root.Q<Label>("item-info-name");
            quantity = root.Q<Label>("item-info-quantity");
            image = root.Q<Image>("item-info-image");
            description = root.Q<Label>("item-info-description");
            title.enableRichText = description.enableRichText = false;
            body = root.Q<ScrollView>("item-info-body");
            root.Q<Button>("item-info-back").clicked += back;
            Hide();
            parent.Add(root);
        }

        public void Show(InventoryItem item, int count, string bankName, int slot)
        {
            address.text = $"{bankName} / SLOT {slot + 1:00}";
            title.text = item.ItemName;
            image.sprite = item.Image;
            image.style.display = item.Image != null ? DisplayStyle.Flex : DisplayStyle.None;
            quantity.text = $"×{count}";
            description.text = string.IsNullOrWhiteSpace(item.Info) ? "No information recorded." : item.Info;
            root.style.display = DisplayStyle.Flex;
            body.scrollOffset = Vector2.zero;
        }

        public void Hide() => root.style.display = DisplayStyle.None;

        public void Scroll(int direction)
        {
            Scroller scroll = body.verticalScroller;
            scroll.value = Mathf.Clamp(scroll.value + direction * 48f, scroll.lowValue, scroll.highValue);
        }

        public void Dispose() => root.RemoveFromHierarchy();

        private static void DrawFrame(MeshGenerationContext context)
        {
            Vector2 size = context.visualElement.contentRect.size;
            if (size.x < 32f || size.y < 32f) return;
            float right = size.x - 1f, bottom = size.y - 1f;
            var points = new[]
            {
                new Vector2(1f, 17f), new Vector2(17f, 1f), new Vector2(right, 1f),
                new Vector2(right, bottom - 16f), new Vector2(right - 16f, bottom), new Vector2(1f, bottom)
            };
            Painter2D painter = context.painter2D;
            painter.fillColor = new Color32(12, 12, 12, 255);
            HudDrawing.Polygon(painter, points, true);
            painter.strokeColor = new Color32(158, 158, 158, 255);
            painter.lineWidth = 1f;
            HudDrawing.Polygon(painter, points);
            painter.strokeColor = HudDrawing.Yellow;
            painter.lineWidth = 2f;
            painter.BeginPath();
            painter.MoveTo(points[0]);
            painter.LineTo(points[1]);
            painter.LineTo(new Vector2(105f, 1f));
            painter.Stroke();
        }
    }
}
