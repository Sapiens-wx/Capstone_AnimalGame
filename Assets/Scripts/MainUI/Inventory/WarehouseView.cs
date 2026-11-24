using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.MainUI.Inventory
{
    internal sealed class WarehouseView : IDisposable
    {
        public const int CargoColumns = 2;
        public const int WarehouseColumns = 4;

        public event Action<bool, int> SlotPressed;
        public event Action<bool, int> SlotHovered;
        public event Action<int> ActionPressed;
        public event Action<int> ActionHovered;
        public event Action ConfirmPressed;
        public event Action BackPressed;

        private sealed class Bank
        {
            public readonly VisualElement Panel, Grid, Fill;
            public readonly Label Count;
            public readonly InventoryStore Store;
            public readonly Button[] Slots;
            public readonly int Columns;
            public int Rows => (Store.Capacity + Columns - 1) / Columns;

            public Bank(VisualElement root, string id, InventoryStore store, int columns)
            {
                Store = store;
                Columns = columns;
                Slots = new Button[store.Capacity];
                Panel = root.Q(id + "-bank");
                Grid = Panel.Q(id + "-grid");
                Count = Panel.Q<Label>(id + "-count");
                Fill = Panel.Q(id + "-fill");
                Panel.Q(className: "bank-outline").generateVisualContent += DrawBankOutline;
            }

            public void UpdateCapacity()
            {
                int used = 0;
                foreach (InventoryItem item in Store.Items)
                    if (item != null) used++;
                Count.text = $"{used:00} / {Store.Capacity:00}";
                Fill.style.width = Length.Percent(100f * used / Store.Capacity);
            }
        }

        private readonly VisualElement root;
        private readonly Bank cargoBank, storageBank;
        private readonly InventorySelectionElement selection;
        private readonly Label address, detail, status, confirmCopy, backCopy;
        private readonly Button confirm;
        private Bank selectedBank, moveBank;
        private int selectedSlot, moveSlot = -1;
        private InventoryInteractionMenuElement menu;
        private string statusMessage;

        public WarehouseView(VisualElement parent, InventoryStore cargo, InventoryStore warehouse)
        {
            root = Resources.Load<VisualTreeAsset>("UI/Warehouse/Warehouse").Instantiate();
            root.StretchToParentSize();
            parent.Add(root);
            root.Q("hold-icon").generateVisualContent += DrawHoldIcon;
            cargoBank = new Bank(root, "cargo", cargo, CargoColumns);
            storageBank = new Bank(root, "storage", warehouse, WarehouseColumns);
            address = root.Q<Label>("slot-address");
            detail = root.Q<Label>("slot-detail");
            status = root.Q<Label>("status");
            confirm = root.Q<Button>("confirm");
            confirmCopy = confirm.Q<Label>("confirm-copy");
            backCopy = root.Q<Label>("back-copy");
            confirm.clicked += () => ConfirmPressed?.Invoke();
            root.Q<Button>("back").clicked += () => BackPressed?.Invoke();
            selection = new InventorySelectionElement { name = "warehouse-selection" };
            selection.style.position = Position.Absolute;

            var template = Resources.Load<VisualTreeAsset>("UI/Main/EmptyInventoryItem");
            BuildGrid(cargoBank, template, false);
            BuildGrid(storageBank, template, true);
            root.Query<Button>().ForEach(button => button.focusable = false);
            root.RegisterCallback<NavigationMoveEvent>(evt => evt.StopImmediatePropagation(), TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationSubmitEvent>(evt => evt.StopImmediatePropagation(), TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationCancelEvent>(evt => evt.StopImmediatePropagation(), TrickleDown.TrickleDown);
            Select(false, 0);
        }

        private void BuildGrid(Bank bank, VisualTreeAsset template, bool warehouse)
        {
            for (int i = 0; i < bank.Slots.Length; i++)
            {
                int index = i;
                var button = new Button(() => SlotPressed?.Invoke(warehouse, index))
                {
                    name = $"{(warehouse ? "Storage" : "Cargo")}Slot{index + 1}"
                };
                button.AddToClassList("warehouse-slot");
                PositionSlot(button, bank, index);
                button.RegisterCallback<PointerMoveEvent>(evt =>
                {
                    if (evt.deltaPosition.sqrMagnitude > 0) SlotHovered?.Invoke(warehouse, index);
                });
                var content = template.Instantiate();
                content.AddToClassList("warehouse-slot-content");
                content.pickingMode = PickingMode.Ignore;
                button.Add(content);
                bank.Grid.Add(button);
                bank.Slots[index] = button;
                RenderSlot(bank, index);
            }
            bank.UpdateCapacity();
        }

        private static void PositionSlot(VisualElement element, Bank bank, int slot)
        {
            element.style.left = Length.Percent(100f * (slot % bank.Columns) / bank.Columns);
            element.style.top = Length.Percent(100f * (slot / bank.Columns) / bank.Rows);
            element.style.width = Length.Percent(100f / bank.Columns);
            element.style.height = Length.Percent(100f / bank.Rows);
        }

        private void RenderSlot(Bank bank, int index)
        {
            InventoryItem item = bank.Store.Items[index];
            bool preview = moveBank == bank && selectedBank == bank && selectedSlot == index && item == null;
            int itemIndex = preview ? moveSlot : index;
            item = bank.Store.Items[itemIndex];
            var button = bank.Slots[index];
            button.EnableInClassList("slot-source", bank == moveBank && index == moveSlot);
            button.EnableInClassList("slot-selected", bank == selectedBank && index == selectedSlot);
            VisualElement content = button.Q(className: "warehouse-slot-content");
            content.style.opacity = preview || (bank == moveBank && index == moveSlot) ? .5f : 1f;
            content.Q("EmptyItem").style.display = item == null ? DisplayStyle.Flex : DisplayStyle.None;
            var image = content.Q<Image>("ItemImage");
            image.sprite = item != null ? item.Image : null;
            image.scaleMode = ScaleMode.ScaleToFit;
            image.style.display = item != null ? DisplayStyle.Flex : DisplayStyle.None;
            var nameLabel = content.Q<Label>("ItemName");
            nameLabel.enableRichText = true;
            nameLabel.style.display = item != null ? DisplayStyle.Flex : DisplayStyle.None;
            nameLabel.text = item != null ? $"{item.ItemName} / <color=#D4B854>{bank.Store.Quantity(itemIndex)}</color>" : "";
            content.Q("ItemAStar").style.display = bank.Store.IsUnread(itemIndex) ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void Refresh(bool warehouse, int slot)
        {
            Bank bank = warehouse ? storageBank : cargoBank;
            RenderSlot(bank, slot);
            bank.UpdateCapacity();
            if (bank == moveBank && slot == moveSlot && selectedBank == bank && slot != selectedSlot)
                RenderSlot(bank, selectedSlot);
            UpdateDetails();
        }

        public void Select(bool warehouse, int slot)
        {
            Bank next = warehouse ? storageBank : cargoBank;
            if (selectedBank == next && selectedSlot == slot) return;
            statusMessage = string.Empty;
            Bank previous = selectedBank;
            int previousSlot = selectedSlot;
            selectedBank = next;
            selectedSlot = slot;
            if (previous != null) RenderSlot(previous, previousSlot);
            RenderSlot(selectedBank, slot);
            cargoBank.Panel.EnableInClassList("bank-active", !warehouse);
            storageBank.Panel.EnableInClassList("bank-active", warehouse);
            if (selection.parent != selectedBank.Grid) selectedBank.Grid.Add(selection);
            PositionSlot(selection, selectedBank, slot);
            UpdateDetails();
        }

        public void ShowMenu(bool warehouse, int slot)
        {
            HideMenu();
            Bank bank = warehouse ? storageBank : cargoBank;
            menu = new InventoryInteractionMenuElement(warehouse ? "WITHDRAW" : "DEPOSIT", "MOVE");
            menu.Pressed += index => ActionPressed?.Invoke(index);
            menu.Hovered += index => ActionHovered?.Invoke(index);
            int col = slot % bank.Columns, row = slot / bank.Columns;
            float across = col + (col + 1 == bank.Columns ? 0f : .53f);
            menu.style.left = Length.Percent(100f * across / bank.Columns);
            menu.style.width = Length.Percent(100f / bank.Columns);
            menu.style.height = 114;
            if (row == bank.Rows - 1)
                menu.style.bottom = Length.Percent(50f / bank.Rows);
            else
                menu.style.top = Length.Percent(100f * (row + .47f) / bank.Rows);
            bank.Grid.Add(menu);
            UpdateDetails();
        }

        public void SelectAction(int index) => menu.Select(index);

        public void HideMenu()
        {
            menu?.RemoveFromHierarchy();
            menu = null;
            UpdateDetails();
        }

        public void SetMoveSource(bool warehouse, int slot)
        {
            Bank previous = moveBank;
            int previousSlot = moveSlot;
            moveBank = slot < 0 ? null : (warehouse ? storageBank : cargoBank);
            moveSlot = slot;
            if (previous != null) RenderSlot(previous, previousSlot);
            if (moveBank != null) RenderSlot(moveBank, moveSlot);
            RenderSlot(selectedBank, selectedSlot);
            UpdateDetails();
        }

        public void SetStatus(string text)
        {
            statusMessage = text;
            UpdateDetails();
        }

        private void UpdateDetails()
        {
            InventoryItem item = selectedBank.Store.Items[selectedSlot];
            address.text = $"{(selectedBank == cargoBank ? "CARGO" : "STORAGE")}   /   SLOT {selectedSlot + 1:00}";
            detail.text = item == null ? "Empty slot" : $"{item.ItemName}   ×{selectedBank.Store.Quantity(selectedSlot)}";
            bool moving = moveBank != null;
            bool atSource = moving && moveBank == selectedBank && moveSlot == selectedSlot;
            confirmCopy.text = atSource ? "Keep here" : moving ? "Place" : menu != null ? "Confirm" : "Actions";
            backCopy.text = moving || menu != null ? "Back" : "Close";
            confirm.SetEnabled(moving ? selectedBank == moveBank && (item == null || atSource) : item != null);
            status.text = !string.IsNullOrEmpty(statusMessage) ? statusMessage
                : moving ? $"Select an empty slot in {(moveBank == cargoBank ? "CARGO" : "STORAGE")}."
                : menu != null ? "Choose an action."
                : "Arrow keys or D-pad to browse";
        }

        public void Dispose() => root.RemoveFromHierarchy();

        private static void DrawBankOutline(MeshGenerationContext context)
        {
            Rect bounds = context.visualElement.contentRect;
            float w = bounds.width - 1, h = bounds.height - 1;
            float tab = Mathf.Min(w * .48f, 245);
            var points = new[]
            {
                new Vector2(1, 1), new Vector2(tab, 1), new Vector2(tab + 12, 13),
                new Vector2(w - 19, 13), new Vector2(w, 32), new Vector2(w, h - 17),
                new Vector2(w - 17, h), new Vector2(1, h)
            };
            Painter2D painter = context.painter2D;
            painter.fillColor = Color.black;
            HudDrawing.Polygon(painter, points, true);
            painter.strokeColor = new Color32(169, 171, 159, 255);
            painter.lineWidth = 1.5f;
            HudDrawing.Polygon(painter, points);
        }

        private static void DrawHoldIcon(MeshGenerationContext context)
        {
            // Isometric cargo crate. The lid, side seams and central strap meet at shared vertices.
            Painter2D p = context.painter2D;
            Vector2 top = new(34, 4), left = new(4, 18), right = new(64, 18), middle = new(34, 33);
            Vector2 baseLeft = new(4, 47), baseRight = new(64, 47), bottom = new(34, 63);
            p.strokeColor = HudDrawing.Yellow;
            p.lineWidth = 2.5f;
            HudDrawing.Polygon(p, new[] { top, right, baseRight, bottom, baseLeft, left });
            HudDrawing.Line(p, left, middle);
            HudDrawing.Line(p, middle, right);
            HudDrawing.Line(p, middle, bottom);
            Vector2 strapStart = Vector2.Lerp(top, right, .55f);
            Vector2 strapEnd = Vector2.Lerp(left, middle, .55f);
            HudDrawing.Line(p, strapStart, strapEnd);
            HudDrawing.Line(p, strapEnd, Vector2.Lerp(baseLeft, bottom, .55f));
        }
    }
}
