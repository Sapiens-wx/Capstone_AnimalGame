using System;
using System.Collections.Generic;
using AnimalGame.World;
using UnityEngine;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AnimalGame.MainUI.Inventory
{
    /// <summary>Host controls presentation/pause; inventory owns input, data and item interactions.</summary>
    public interface IInventoryHost
    {
        bool IsInventoryOpen { get; }
        float InventoryProgress { get; }
        void OpenInventory();
        void CloseInventory();
    }

    public struct InventoryControllerSettings
    {
        public InventoryRewardTable Rewards;
        public VisualTreeAsset ItemTemplate;
        public float BlinkPeriod, StickDeadzone, RepeatDelay, RepeatInterval, OpenHoldSeconds;
    }

    public sealed class InventoryController : IDisposable
    {
        public const int SlotCount = 8;
        private readonly IInventoryHost host;
        private InventoryControllerSettings settings;
        private VisualElement root, inventory, grid;
        private InventorySelectionElement selection;
        private readonly Button[] slots = new Button[SlotCount];
        private int selectedIndex;
        private bool openHoldLatched;
        private float openHoldElapsed;
        public int SelectedIndex => selectedIndex;
        public InventoryItem[] Items => inventoryStore.Items;

        public InventoryController(IInventoryHost host)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            inventoryStore.Changed += InventoryChanged;
        }

        public void Configure(InventoryControllerSettings value) => settings = value;

        public void Bind(VisualElement nextRoot, VisualElement panel, VisualElement nextGrid,
            InventorySelectionElement nextSelection)
        {
            ResetInventoryInteraction();
            root = nextRoot; inventory = panel; grid = nextGrid; selection = nextSelection;
            BuildSlots();
        }

        public void RefreshLayout()
        {
            grid.SetEnabled(CanInteractInventory);
            foreach (Button slot in slots)
                if (slot != null) slot.pickingMode = host.IsInventoryOpen ? PickingMode.Position : PickingMode.Ignore;
            SelectItem(selectedIndex);
            PositionInteractionMenu();
        }

        public void Tick() { HandleInput(); UpdateInventoryStars(); }

        public void Suspend()
        {
            ResetInventoryInteraction();
            openHoldElapsed = 0; openHoldLatched = false;
            stickDirection = Vector2Int.zero;
        }

        public void Dispose()
        {
            Suspend();
            if (inventoryEventRoot != null) inventoryEventRoot.UnregisterCallback<PointerMoveEvent>(InventoryPointerMoved);
            inventoryStore.Changed -= InventoryChanged;
        }

        private readonly InventoryStore inventoryStore = new(SlotCount);
        public InventoryStore Inventory => inventoryStore;
        public InventoryInteractionState InteractionState => host.IsInventoryOpen ? interactionState : InventoryInteractionState.Closed;
        public event Action<InventoryItem> UseRequested;
        public event Action<InventoryItem> InfoRequested;
        // Assign a handler later to show a quantity picker. Without one, discard the whole stack.
        public Action<int> DiscardRequested { get; set; }

        private InventoryInteractionState interactionState = InventoryInteractionState.Browse;
        private InventoryInteractionMenuElement interactionMenu;
        private readonly Label[] itemStars = new Label[SlotCount];
        private Label cargoStar;
        private VisualElement inventoryEventRoot;
        private bool inventoryDataBound;
        private int menuSource = -1, moveSource = -1, moveTarget = -1;
        private InventoryItem movingItem;
        private int movingQuantity, lastInventoryActionFrame = -1;
        private Vector2Int stickDirection;
        private float stickRepeatAt;
        private bool CanInteractInventory => host.IsInventoryOpen && host.InventoryProgress >= .98f;

        public void InitializeInventoryData(IReadOnlyList<InventoryItem> defaultItems)
        {
            if (inventoryDataBound) return;
            inventoryDataBound = true;
            for (int i = 0; defaultItems != null && i < Mathf.Min(SlotCount, defaultItems.Count); i++)
                inventoryStore.Set(i, defaultItems[i]);
        }

        public InventoryStore.AddResult AddItem(InventoryItem item, int quantity = 1)
        {
            return inventoryStore.Add(item, quantity);
        }

        public InventoryStore.AddResult GrantRecycleRewards(RecyclableSize size)
        {
            return settings.Rewards != null ? settings.Rewards.Grant(size, inventoryStore) : default;
        }

        public void SetItem(int index, InventoryItem item)
        {
            inventoryStore.Set(index, item);
        }

        private void InventoryChanged(int index)
        {
            if (interactionState == InventoryInteractionState.Move &&
                (index == moveSource || index == moveTarget)) ResetInventoryInteraction();
            if (interactionState == InventoryInteractionState.Menu && index == menuSource)
                ResetInventoryInteraction();
            RefreshItem(index);
            UpdateInventoryStars();
        }

        private void BuildSlots()
        {
            ResetInventoryInteraction();
            grid.Clear();
            for (int i = 0; i < SlotCount; i++)
            {
                int index = i;
                var button = new Button(() => ClickInventorySlot(index))
                    { name = "InventorySlot" + (i + 1), userData = Items[i], focusable = false };
                button.AddToClassList("inventory-slot");
                button.style.position = Position.Absolute;
                button.style.left = new Length((i % 2) * 50, LengthUnit.Percent);
                button.style.top = new Length((i / 2) * 25, LengthUnit.Percent);
                // InventoryController owns navigation and submit; Toolkit must not also invoke the focused button.
                button.RegisterCallback<NavigationMoveEvent>(evt => evt.StopImmediatePropagation(), TrickleDown.TrickleDown);
                button.RegisterCallback<NavigationSubmitEvent>(evt => evt.StopImmediatePropagation(), TrickleDown.TrickleDown);
                button.RegisterCallback<PointerLeaveEvent>(_ =>
                {
                    if (CanInteractInventory && interactionState == InventoryInteractionState.Move) SetMoveTarget(-1);
                });
                grid.Add(button); slots[i] = button;
                RefreshItem(i);
            }
            if (interactionMenu != null) interactionMenu.RemoveFromHierarchy();
            interactionMenu = new InventoryInteractionMenuElement { name = "InventoryInteractionMenu" };
            interactionMenu.style.display = DisplayStyle.None;
            interactionMenu.Pressed += ExecuteInventoryAction;
            interactionMenu.Hovered += index =>
            {
                if (CanInteractInventory && interactionState == InventoryInteractionState.Menu)
                    interactionMenu.Select(index);
            };
            inventory.Add(interactionMenu);
            cargoStar = inventory.Q<Label>("Label-AStar");
            if (cargoStar != null) cargoStar.pickingMode = PickingMode.Ignore;
            if (inventoryEventRoot != null) inventoryEventRoot.UnregisterCallback<PointerMoveEvent>(InventoryPointerMoved);
            inventoryEventRoot = root;
            root.RegisterCallback<PointerMoveEvent>(InventoryPointerMoved);
            SelectItem(selectedIndex);
            UpdateInventoryStars();
        }

        private void RefreshItem(int index)
        {
            Button button = slots[index];
            if (button == null) return;
            button.Clear(); button.userData = Items[index];
            VisualElement content;
            if (settings.ItemTemplate != null) content = settings.ItemTemplate.CloneTree();
            else
            {
                content = new VisualElement();
                content.Add(new EmptyInventoryElement { name = "EmptyItem", style =
                    { position = Position.Absolute, left = 0, right = 0, top = 0, bottom = 0 } });
                content.Add(new Image { name = "ItemImage", style =
                    { position = Position.Absolute, left = 12, right = 12, top = 12, bottom = 38 } });
                content.Add(new Label { name = "ItemName", style =
                    { position = Position.Absolute, left = 8, right = 8, bottom = 8, height = 24,
                      unityTextAlign = TextAnchor.LowerLeft, color = Color.white, fontSize = 18 } });
                content.Add(new Label("*") { name = "ItemAStar", style =
                    { position = Position.Absolute, right = 8, top = 8, width = 35, height = 29, fontSize = 47,
                      color = HudDrawing.Yellow, unityTextAlign = TextAnchor.MiddleCenter } });
            }
            content.AddToClassList("empty-item");
            content.pickingMode = PickingMode.Ignore;
            foreach (var child in content.Query<VisualElement>().ToList()) child.pickingMode = PickingMode.Ignore;
            button.Add(content);
            InventoryItem item = Items[index];
            bool preview = interactionState == InventoryInteractionState.Move && moveTarget == index && item == null;
            if (preview) item = movingItem;
            content.style.opacity = preview ? .5f : 1;
            var placeholder = content.Q<EmptyInventoryElement>("EmptyItem");
            if (placeholder != null) placeholder.style.display = item != null ? DisplayStyle.None : DisplayStyle.Flex;
            if (item != null && !preview) placeholder?.RemoveFromHierarchy();
            var image = content.Q<Image>("ItemImage");
            if (image != null)
            {
                image.sprite = item != null ? item.Image : null;
                image.scaleMode = ScaleMode.ScaleToFit;
                image.style.display = item != null ? DisplayStyle.Flex : DisplayStyle.None;
            }
            var label = content.Q<Label>("ItemName");
            if (label != null)
            {
                int quantity = preview ? movingQuantity : inventoryStore.Quantity(index);
                label.enableRichText = true;
                label.text = item != null ? item.ItemName + " / <color=#D4B854>" + quantity + "</color>" : string.Empty;
                label.style.display = item != null ? DisplayStyle.Flex : DisplayStyle.None;
            }
            itemStars[index] = content.Q<Label>("ItemAStar");
            if (itemStars[index] != null)
            {
                bool show = preview ? inventoryStore.IsUnread(moveSource) : inventoryStore.IsUnread(index);
                itemStars[index].style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void UpdateInventoryStars()
        {
            float opacity = .75f + .25f * Mathf.Cos(Time.unscaledTime * Mathf.PI * 2 / Mathf.Max(.1f, settings.BlinkPeriod));
            if (cargoStar != null)
            {
                cargoStar.style.display = inventoryStore.HasUnread ? DisplayStyle.Flex : DisplayStyle.None;
                cargoStar.style.opacity = opacity;
            }
            foreach (Label star in itemStars) if (star != null) star.style.opacity = opacity;
        }

        // Public programmatic/layout selection is deliberately passive (does not acknowledge new items).
        public void SelectItem(int index)
        {
            selectedIndex = Mathf.Clamp(index, 0, SlotCount - 1);
            if (selection == null || slots[selectedIndex] == null || !Positive(grid.layout.width)) return;
            float inset = InventorySelectionElement.InnerFrameInset;
            Vector2 offset = grid.layout.position + slots[selectedIndex].layout.position
                - selection.layout.position - Vector2.one * inset;
            selection.style.translate = new Translate(offset.x, offset.y);
            selection.style.width = slots[selectedIndex].layout.width + inset * 2;
            selection.style.height = slots[selectedIndex].layout.height + inset * 2;
        }

        private void InventoryPointerMoved(PointerMoveEvent evt)
        {
            if (!CanInteractInventory || evt.deltaPosition.sqrMagnitude == 0) return;
            VisualElement target = evt.target as VisualElement;
            if (target != null && interactionMenu.Contains(target)) return;
            int index = -1;
            for (VisualElement node = target; node != null; node = node.parent)
                for (int i = 0; i < SlotCount; i++) if (node == slots[i]) index = i;
            if (interactionState == InventoryInteractionState.Move)
            {
                SetMoveTarget(index);
                return;
            }
            if (index < 0) return;
            if (interactionState == InventoryInteractionState.Menu)
            {
                if (index == menuSource || Items[index] == null) return;
                ResetInventoryInteraction();
            }
            SelectItem(index);
            inventoryStore.MarkViewed(index);
        }

        private void ClickInventorySlot(int index)
        {
            if (!CanInteractInventory || CancelInventoryInputPending() || lastInventoryActionFrame == Time.frameCount) return;
            if (interactionState == InventoryInteractionState.Move)
            {
                SetMoveTarget(index); ConfirmInventoryMove(); return;
            }
            if (interactionState == InventoryInteractionState.Menu) return;
            SelectItem(index); inventoryStore.MarkViewed(index);
            OpenInteractionMenu();
        }

        private void OpenInteractionMenu()
        {
            if (Items[selectedIndex] == null) return;
            menuSource = selectedIndex;
            interactionState = InventoryInteractionState.Menu;
            interactionMenu.Select(0);
            interactionMenu.style.display = DisplayStyle.Flex;
            PositionInteractionMenu();
            lastInventoryActionFrame = Time.frameCount;
        }

        private void PositionInteractionMenu()
        {
            if (interactionState != InventoryInteractionState.Menu || menuSource < 0) return;
            Rect slot = slots[menuSource].layout;
            Vector2 anchor = grid.layout.position + slot.center;
            interactionMenu.style.left = anchor.x;
            interactionMenu.style.top = anchor.y - (menuSource >= 6 ? slot.height * 2 : 0);
            interactionMenu.style.width = slot.width;
            interactionMenu.style.height = slot.height * 2;
        }

        private void ExecuteInventoryAction(int action)
        {
            if (!CanInteractInventory || interactionState != InventoryInteractionState.Menu ||
                CancelInventoryInputPending() || lastInventoryActionFrame == Time.frameCount) return;
            int source = menuSource;
            InventoryItem item = Items[source];
            ResetInventoryInteraction();
            lastInventoryActionFrame = Time.frameCount;
            if (item == null) return;
            switch (action)
            {
                case 0: UseRequested?.Invoke(item); break;
                case 1: InfoRequested?.Invoke(item); break;
                case 2:
                    interactionState = InventoryInteractionState.Move;
                    moveSource = source; movingItem = item; movingQuantity = inventoryStore.Quantity(source);
                    break;
                case 3:
                    if (DiscardRequested != null) DiscardRequested(source);
                    else inventoryStore.Set(source, null);
                    break;
            }
        }

        private void SetMoveTarget(int index)
        {
            int old = moveTarget;
            // Only a valid slot changes the frame. Leaving the grid or hitting its edge
            // keeps the current slot's preview; an occupied current slot has no preview.
            if (index >= 0 && index < SlotCount) SelectItem(index);
            moveTarget = Items[selectedIndex] == null ? selectedIndex : -1;
            if (old == moveTarget) return;
            if (old >= 0) RefreshItem(old);
            if (moveTarget >= 0) RefreshItem(moveTarget);
        }

        private void ConfirmInventoryMove()
        {
            if (interactionState != InventoryInteractionState.Move || CancelInventoryInputPending() ||
                moveSource < 0 || Items[moveSource] != movingItem ||
                inventoryStore.Quantity(moveSource) != movingQuantity) return;
            // Confirming the source completes the operation without changing the stack.
            if (selectedIndex == moveSource)
            {
                ResetInventoryInteraction();
                lastInventoryActionFrame = Time.frameCount;
                return;
            }
            if (moveTarget < 0 || Items[moveTarget] != null) return;
            int source = moveSource, target = moveTarget;
            ResetInventoryInteraction();
            if (inventoryStore.Move(source, target)) SelectItem(target);
            lastInventoryActionFrame = Time.frameCount;
        }

        public void ResetInventoryInteraction()
        {
            int preview = moveTarget, source = moveSource;
            interactionState = InventoryInteractionState.Browse;
            moveTarget = moveSource = menuSource = -1; movingItem = null; movingQuantity = 0;
            if (interactionMenu != null) interactionMenu.style.display = DisplayStyle.None;
            if (preview >= 0) RefreshItem(preview);
            if (source >= 0) SelectItem(source);
        }

        public void BackInventory()
        {
            if (!host.IsInventoryOpen) return;
            if (interactionState != InventoryInteractionState.Browse) ResetInventoryInteraction();
            else host.CloseInventory();
            lastInventoryActionFrame = Time.frameCount;
        }

        // Pointer callbacks can run before Update. Give cancellation priority there as well.
        private bool CancelInventoryInputPending()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.nKey.wasPressedThisFrame) return true;
            foreach (Gamepad pad in Gamepad.all)
                if (pad.added && pad.buttonEast.wasPressedThisFrame) return true;
            if (interactionState == InventoryInteractionState.Move && Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
                return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current == null)
#endif
                if (Input.GetKeyDown(KeyCode.N)) return true;
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current == null)
#endif
                if (interactionState == InventoryInteractionState.Move && Input.GetMouseButtonDown(1)) return true;
#endif
            return false;
        }

        private void NavigateInventory(Vector2Int direction)
        {
            if (interactionState == InventoryInteractionState.Menu)
            {
                interactionMenu.Select(interactionMenu.SelectedIndex + direction.y); return;
            }
            int column = selectedIndex % 2 + direction.x, row = selectedIndex / 2 + direction.y;
            if (column < 0 || column > 1 || row < 0 || row > 3)
            {
                if (interactionState == InventoryInteractionState.Move) SetMoveTarget(-1);
                return;
            }
            int index = row * 2 + column;
            if (interactionState == InventoryInteractionState.Move) SetMoveTarget(index);
            else { SelectItem(index); inventoryStore.MarkViewed(index); }
        }

        private void HandleInput()
        {
            bool toggle = false, close = false, heldOpen = false, confirm = false, rightClick = false;
            Vector2Int navigation = Vector2Int.zero;
            Vector2 stick = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                toggle = Keyboard.current.nKey.wasPressedThisFrame;
                if (Keyboard.current.leftArrowKey.wasPressedThisFrame) navigation.x = -1;
                if (Keyboard.current.rightArrowKey.wasPressedThisFrame) navigation.x = 1;
                if (Keyboard.current.upArrowKey.wasPressedThisFrame) navigation.y = -1;
                if (Keyboard.current.downArrowKey.wasPressedThisFrame) navigation.y = 1;
            }
            rightClick = Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
            foreach (Gamepad pad in Gamepad.all)
            {
                if (!pad.added) continue;
                heldOpen |= pad.dpad.right.isPressed;
                close |= pad.buttonEast.wasPressedThisFrame;
                confirm |= pad.buttonSouth.wasPressedThisFrame;
                if (pad.dpad.left.wasPressedThisFrame) navigation.x = -1;
                if (pad.dpad.right.wasPressedThisFrame) navigation.x = 1;
                if (pad.dpad.up.wasPressedThisFrame) navigation.y = -1;
                if (pad.dpad.down.wasPressedThisFrame) navigation.y = 1;
                Vector2 value = pad.leftStick.ReadValue();
                if (value.sqrMagnitude > stick.sqrMagnitude) stick = value;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current == null)
#endif
            {
                toggle = Input.GetKeyDown(KeyCode.N);
                if (Input.GetKeyDown(KeyCode.LeftArrow)) navigation.x = -1;
                if (Input.GetKeyDown(KeyCode.RightArrow)) navigation.x = 1;
                if (Input.GetKeyDown(KeyCode.UpArrow)) navigation.y = -1;
                if (Input.GetKeyDown(KeyCode.DownArrow)) navigation.y = 1;
            }
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current == null)
#endif
                rightClick = Input.GetMouseButtonDown(1);
#endif
            if (toggle || close)
            {
                if (host.IsInventoryOpen) BackInventory();
                else if (toggle) { host.OpenInventory(); lastInventoryActionFrame = Time.frameCount; }
                // A right hold used for opening must be released after cancelling/closing.
                openHoldLatched = heldOpen;
                return;
            }
            if (rightClick && interactionState == InventoryInteractionState.Move)
            {
                ResetInventoryInteraction(); lastInventoryActionFrame = Time.frameCount; return;
            }
            if (!heldOpen) { openHoldElapsed = 0; openHoldLatched = false; }
            else if (!host.IsInventoryOpen && !openHoldLatched)
            {
                openHoldElapsed += Time.unscaledDeltaTime;
                if (openHoldElapsed >= settings.OpenHoldSeconds)
                {
                    openHoldLatched = true; host.OpenInventory(); lastInventoryActionFrame = Time.frameCount;
                }
            }
            Vector2Int direction = Vector2Int.zero;
            if (Mathf.Max(Mathf.Abs(stick.x), Mathf.Abs(stick.y)) >= settings.StickDeadzone)
                direction = Mathf.Abs(stick.x) > Mathf.Abs(stick.y)
                    ? new Vector2Int(stick.x > 0 ? 1 : -1, 0) : new Vector2Int(0, stick.y > 0 ? -1 : 1);
            if (direction != stickDirection)
            {
                stickDirection = direction; stickRepeatAt = Time.unscaledTime + settings.RepeatDelay;
                if (navigation == Vector2Int.zero) navigation = direction;
            }
            else if (direction != Vector2Int.zero && Time.unscaledTime >= stickRepeatAt)
            {
                stickRepeatAt = Time.unscaledTime + settings.RepeatInterval;
                if (navigation == Vector2Int.zero) navigation = direction;
            }
            if (!CanInteractInventory || lastInventoryActionFrame == Time.frameCount) return;
            if (navigation != Vector2Int.zero) NavigateInventory(navigation);
            if (!confirm) return;
            if (interactionState == InventoryInteractionState.Move) ConfirmInventoryMove();
            else if (interactionState == InventoryInteractionState.Menu) ExecuteInventoryAction(interactionMenu.SelectedIndex);
            else { inventoryStore.MarkViewed(selectedIndex); OpenInteractionMenu(); }
        }
        private static bool Positive(float x) => x > 0 && !float.IsNaN(x) && !float.IsInfinity(x);
    }
}
