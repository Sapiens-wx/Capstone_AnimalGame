using System;
using AnimalGame.MainUI;
using AnimalGame.MainUI.Inventory;
using AnimalGame.RobotMap;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;
using GameMainUI = AnimalGame.MainUI.MainUI;

namespace AnimalGame.World
{
    [DefaultExecutionOrder(-90)]
    [RequireComponent(typeof(UIDocument))]
    public sealed class WarehouseUI : MonoBehaviour
    {
        private static WarehouseUI active;
        private static int closedFrame = -1;
        public static bool BlocksGameplay => active != null || closedFrame == Time.frameCount;

        private GameMainUI hud;
        private InventoryStore cargo, warehouse;
        private WarehouseView view;
        private InventoryInteractionState state;
        private bool warehouseSelected;
        private int slot, action, moveSource, lastActionFrame;
        private Vector2Int heldDirection;
        private float repeatAt, previousTimeScale;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisible;

        private InventoryStore SelectedStore => warehouseSelected ? warehouse : cargo;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { active = null; closedFrame = -1; }

        internal static bool Open(Component source, GameMainUI hud)
        {
            if (BlocksGameplay) return false;
            var ui = source.GetComponentInChildren<WarehouseUI>(true);
            if (ui == null)
            {
                var host = new GameObject("Warehouse UI");
                host.SetActive(false);
                host.transform.SetParent(source.transform, false);
                var document = host.AddComponent<UIDocument>();
                document.panelSettings = Resources.Load<PanelSettings>("UI/AnimalCatalog/AnimalArchivePanel");
                document.sortingOrder = 1;
                ui = host.AddComponent<WarehouseUI>();
            }
            ui.gameObject.SetActive(true);
            ui.Show(hud);
            return true;
        }

        internal static void CloseFor(Component source)
        {
            if (IsOpenFor(source))
                active.gameObject.SetActive(false);
        }

        internal static bool IsOpenFor(Component source) => active != null && active.transform.parent == source.transform;

        private void Show(GameMainUI owner)
        {
            hud = owner;
            cargo = hud.Inventory;
            warehouse = hud.Warehouse;
            view = new WarehouseView(GetComponent<UIDocument>().rootVisualElement, cargo, warehouse);
            view.SlotHovered += HoverSlot;
            view.SlotPressed += PressSlot;
            view.ActionHovered += SelectAction;
            view.ActionPressed += ExecuteAction;
            view.ConfirmPressed += Confirm;
            view.BackPressed += Back;
            cargo.Changed += CargoChanged;
            warehouse.Changed += WarehouseChanged;
            state = InventoryInteractionState.Browse;
            warehouseSelected = false;
            slot = Array.FindIndex(cargo.Items, item => item != null);
            if (slot < 0)
            {
                slot = Array.FindIndex(warehouse.Items, item => item != null);
                warehouseSelected = slot >= 0;
                slot = Mathf.Max(0, slot);
            }
            view.Select(warehouseSelected, slot);
            lastActionFrame = Time.frameCount;
            heldDirection = Vector2Int.zero;
            previousTimeScale = Time.timeScale;
            previousCursorVisible = Cursor.visible;
            previousCursorLock = Cursor.lockState;
            active = this;
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void CargoChanged(int index) => SlotChanged(false, index);
        private void WarehouseChanged(int index) => SlotChanged(true, index);

        private void SlotChanged(bool inWarehouse, int index)
        {
            if (inWarehouse == warehouseSelected &&
                ((state == InventoryInteractionState.Menu && index == slot) ||
                 (state == InventoryInteractionState.Move && index == moveSource)))
                Browse();
            view.Refresh(inWarehouse, index);
        }

        private void HoverSlot(bool inWarehouse, int index)
        {
            if (state == InventoryInteractionState.Menu)
            {
                if ((warehouseSelected == inWarehouse && slot == index) ||
                    (inWarehouse ? warehouse : cargo).Items[index] == null) return;
                Browse();
            }
            SelectSlot(inWarehouse, index);
        }

        private void SelectSlot(bool inWarehouse, int index)
        {
            if (state == InventoryInteractionState.Move && warehouseSelected != inWarehouse) return;
            warehouseSelected = inWarehouse;
            slot = index;
            if (state == InventoryInteractionState.Browse) SelectedStore.MarkViewed(index);
            view.Select(inWarehouse, index);
        }

        private void PressSlot(bool inWarehouse, int index)
        {
            if (lastActionFrame == Time.frameCount || BackPressedThisFrame() || state == InventoryInteractionState.Menu ||
                (state == InventoryInteractionState.Move && inWarehouse != warehouseSelected)) return;
            SelectSlot(inWarehouse, index);
            Confirm();
        }

        private void Confirm()
        {
            if (lastActionFrame == Time.frameCount || BackPressedThisFrame()) return;
            if (state == InventoryInteractionState.Menu)
            {
                ExecuteAction(action);
                return;
            }
            lastActionFrame = Time.frameCount;
            if (state == InventoryInteractionState.Move)
            {
                if (slot != moveSource && SelectedStore.Items[slot] != null)
                {
                    view.SetStatus("Choose an empty slot.");
                    return;
                }
                int source = moveSource;
                Browse();
                if (slot != source) SelectedStore.Move(source, slot);
                return;
            }
            if (SelectedStore.Items[slot] == null) return;
            SelectedStore.MarkViewed(slot);
            state = InventoryInteractionState.Menu;
            view.SetStatus(string.Empty);
            view.ShowMenu(warehouseSelected, slot);
            SelectAction(0);
        }

        private void SelectAction(int index)
        {
            if (state != InventoryInteractionState.Menu) return;
            action = Mathf.Clamp(index, 0, 1);
            view.SelectAction(action);
        }

        private void ExecuteAction(int index)
        {
            if (lastActionFrame == Time.frameCount || BackPressedThisFrame() || state != InventoryInteractionState.Menu) return;
            lastActionFrame = Time.frameCount;
            Browse();
            if (index == 1)
            {
                moveSource = slot;
                state = InventoryInteractionState.Move;
                view.SetMoveSource(warehouseSelected, moveSource);
                return;
            }
            string itemName = SelectedStore.Items[slot].ItemName;
            var result = SelectedStore.TransferTo(slot, warehouseSelected ? cargo : warehouse);
            string target = warehouseSelected ? "Cargo" : "Storage";
            if (result.Added == 0) view.SetStatus($"{target} is full.");
            else
            {
                string verb = warehouseSelected ? "Withdrew" : "Stored";
                string remainder = result.Remaining > 0 ? $"  /  {result.Remaining} left in place." : string.Empty;
                view.SetStatus($"{verb} {result.Added} × {itemName}.{remainder}");
            }
        }

        private void Browse()
        {
            if (state == InventoryInteractionState.Menu) view.HideMenu();
            if (state == InventoryInteractionState.Move) view.SetMoveSource(warehouseSelected, -1);
            state = InventoryInteractionState.Browse;
            view.SetStatus(string.Empty);
        }

        private void Back()
        {
            if (lastActionFrame == Time.frameCount) return;
            lastActionFrame = Time.frameCount;
            if (state == InventoryInteractionState.Browse) { gameObject.SetActive(false); return; }
            if (state == InventoryInteractionState.Move)
            {
                slot = moveSource;
                view.Select(warehouseSelected, slot);
            }
            Browse();
        }

        private bool BackPressedThisFrame()
        {
            var keyboard = Keyboard.current;
            return (keyboard != null && (keyboard.escapeKey.wasPressedThisFrame || keyboard.nKey.wasPressedThisFrame))
                || AdaptiveLegacyGamepadInput.WasEastFaceButtonPressedThisFrame()
                || (state == InventoryInteractionState.Move && Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame);
        }

        private void Navigate(Vector2Int direction)
        {
            if (state == InventoryInteractionState.Menu) { SelectAction(action + direction.y); return; }
            // Treat both banks as adjacent columns, so crossing the aisle preserves the row.
            const int left = WarehouseView.CargoColumns, right = WarehouseView.WarehouseColumns;
            int columns = warehouseSelected ? right : left;
            int x = (warehouseSelected ? left : 0) + slot % columns + direction.x;
            int y = slot / columns + direction.y;
            if (x < 0 || x >= left + right || y < 0) return;
            bool inWarehouse = x >= left;
            int index = inWarehouse ? y * right + x - left : y * left + x;
            if (index < (inWarehouse ? warehouse : cargo).Capacity) SelectSlot(inWarehouse, index);
        }

        private void Update()
        {
            if (active != this || lastActionFrame == Time.frameCount) return;
            if (hud == null || !hud.isActiveAndEnabled) { gameObject.SetActive(false); return; }
            if (BackPressedThisFrame())
            {
                Back();
                return;
            }
            Vector2 axis = AdaptiveLegacyGamepadInput.ReadLeftStick();
            var pad = Gamepad.current;
            Vector2 dpad = pad != null ? pad.dpad.ReadValue() : Vector2.zero;
            if (dpad != Vector2.zero) axis = dpad;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.leftArrowKey.isPressed) axis = Vector2.left;
                if (keyboard.rightArrowKey.isPressed) axis = Vector2.right;
                if (keyboard.upArrowKey.isPressed) axis = Vector2.up;
                if (keyboard.downArrowKey.isPressed) axis = Vector2.down;
            }
            var direction = Vector2Int.zero;
            var settings = hud.InventoryController.Settings;
            if (Mathf.Max(Mathf.Abs(axis.x), Mathf.Abs(axis.y)) >= settings.StickDeadzone)
                direction = Mathf.Abs(axis.x) > Mathf.Abs(axis.y)
                    ? new Vector2Int(axis.x > 0 ? 1 : -1, 0) : new Vector2Int(0, axis.y > 0 ? -1 : 1);
            if (direction != Vector2Int.zero && (direction != heldDirection || Time.unscaledTime >= repeatAt))
            {
                Navigate(direction);
                repeatAt = Time.unscaledTime + (direction == heldDirection ? settings.RepeatInterval : settings.RepeatDelay);
            }
            heldDirection = direction;
            if ((keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)) ||
                AdaptiveLegacyGamepadInput.WasSouthFaceButtonPressedThisFrame()) Confirm();
        }

        private void OnDisable()
        {
            if (active != this) return;
            active = null;
            closedFrame = Time.frameCount;
            cargo.Changed -= CargoChanged;
            warehouse.Changed -= WarehouseChanged;
            view.Dispose();
            view = null;
            if (Time.timeScale == 0f) Time.timeScale = previousTimeScale;
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
        }
    }
}
