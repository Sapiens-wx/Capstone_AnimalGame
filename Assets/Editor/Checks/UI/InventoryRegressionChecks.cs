using System;
using System.Reflection;
using AnimalGame.MainUI.Inventory;
using AnimalGame.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using MouseButton = UnityEngine.InputSystem.LowLevel.MouseButton;
#endif
using Hud = AnimalGame.MainUI.MainUI;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    public static class InventoryRegressionChecks
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static int checks;
        [MenuItem("Animal Game/Validation/Run Inventory Checks")]
        public static void Run()
        {
            checks = 0;
            var metal = AssetDatabase.LoadAssetAtPath<InventoryItem>("Assets/Data/Inventory/Items/MetalScrap.asset");
            var synthetic = AssetDatabase.LoadAssetAtPath<InventoryItem>("Assets/Data/Inventory/Items/SyntheticScrap.asset");
            var rewards = AssetDatabase.LoadAssetAtPath<InventoryRewardTable>("Assets/Data/Inventory/InventoryRewards.asset");
            Check(metal != null && synthetic != null && rewards != null, "Configured resources load");
            Check(metal.Image != null && synthetic.Image != null, "Scrap sprites resolve");
            var store = new InventoryStore();
            store.Add(metal, 9); store.MarkViewed(0);
            var result = store.Add(metal, 2);
            Check(result.Added == 2 && store.Quantity(0) == 10 && store.Quantity(1) == 1, "9+2 stacks before occupying the next slot");
            Check(!store.IsUnread(0) && !store.IsUnread(1), "Known type never gains another star");
            store = new InventoryStore(); store.Add(metal, 11); store.Add(synthetic, 1);
            Check(store.IsUnread(0) && store.IsUnread(1) && store.IsUnread(2), "New types mark each initial stack");
            store.MarkViewed(0);
            Check(store.HasUnread && store.IsUnread(1) && store.IsUnread(2), "Viewing one stack preserves all others");
            store.MarkViewed(1); store.MarkViewed(2);
            Check(!store.HasUnread, "All viewed clears aggregate");
            store.Set(0, null); store.Add(metal, 1);
            Check(!store.IsUnread(0), "Discarding does not forget a discovered type");
            store = new InventoryStore(1); store.Add(metal, 9); result = store.Add(metal, 4);
            Check(result.Added == 1 && result.Remaining == 3 && store.Quantity(0) == 10, "Partial capacity reports ignored overflow");
            result = store.Add(synthetic, 1);
            Check(result.Added == 0 && result.Remaining == 1 && store.Items[0] == metal, "Full inventory never overwrites");
            store.Set(0, null); store.Add(synthetic, 1);
            Check(store.IsUnread(0), "Previously rejected type is still new when first accepted");
            store = new InventoryStore();
            rewards.Grant(RecyclableSize.Small, store, () => 0);
            Check(store.Items[0] == metal && store.Quantity(0) == 1, "Small reward metal branch");
            store = new InventoryStore(); rewards.Grant(RecyclableSize.Small, store, () => .999f);
            Check(store.Items[0] == synthetic && store.Quantity(0) == 1, "Small reward synthetic branch");
            store = new InventoryStore(); rewards.Grant(RecyclableSize.Medium, store, () => 0);
            Check(store.Items[0] == metal && store.Quantity(0) == 2, "Medium reward same type merges");
            store = new InventoryStore(); rewards.Grant(RecyclableSize.Medium, store, () => .999f);
            Check(store.Items[0] == metal && store.Items[1] == synthetic && store.HasUnread, "Medium reward produces two new types");
            Check(rewards.Grant(RecyclableSize.Big, store).Added == 0, "No invented big garbage reward");
            Check(!store.Move(0, 1), "Occupied destination cannot merge or swap");
            Check(store.Move(0, 7) && store.Quantity(7) == 1 && store.Items[0] == null && store.IsUnread(7), "Move preserves quantity and unread state");
            CheckUI(metal, synthetic);
            Debug.Log("INVENTORY CHECKS PASSED: " + checks);
        }

        private static void CheckUI(InventoryItem metal, InventoryItem synthetic)
        {
            GameObject obj = null;
            PanelSettings settings = null;
            RenderTexture texture = null;
            float previousTimeScale = Time.timeScale;
#if ENABLE_INPUT_SYSTEM
            Gamepad pad = null;
            Keyboard keyboard = null;
            Mouse mouse = null;
            var previousBackground = InputSystem.settings.backgroundBehavior;
            var previousEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
#endif
            try
            {
                settings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Prefabs/Resources/UI/Main/MainUISetting.asset"));
                texture = new RenderTexture(1920, 1080, 24); texture.Create(); settings.targetTexture = texture;
                obj = new GameObject("Inventory validation");
                var document = obj.AddComponent<UIDocument>();
                document.panelSettings = settings;
                document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Prefabs/Resources/UI/Main/MainUIDoc.uxml");
                var hud = obj.AddComponent<Hud>();
                ConfigureRewards(hud);
                hud.Init();
                var root = document.rootVisualElement;
                Layout(root); Call(hud, "ApplyLayout"); Layout(root);
                hud.AddItem(metal, 2); hud.AddItem(synthetic, 1);
                hud.OpenInventory(); Call(hud, "AdvanceInventoryAnimation", 1f); Layout(root);
                Check(hud.IsInventoryOpen && Time.timeScale == 0, "Open acquires pause");
                Check(hud.Inventory.IsUnread(0) && hud.Inventory.IsUnread(1), "Default selection does not consume unread marks");
                hud.Init(); Layout(root);
                Check(hud.Inventory.IsUnread(0) && hud.Inventory.IsUnread(1), "UI rebuild preserves data and unread marks");
                Check(root.Q<Label>("Label-AStar").style.display.value == DisplayStyle.Flex, "Cargo star visible with unread items");
                Check(root.Q("InventorySlot1").Q<Label>("ItemName").text.Contains("#D4B854>2"), "Quantity uses yellow rich text");
                Check(root.Q("InventorySlot3").Q<Label>("ItemAStar").style.display.value == DisplayStyle.None, "Empty star stays hidden");
                Call(hud, "ClickInventorySlot", 0);
                var menu = root.Q<InventoryInteractionMenuElement>(); Layout(root);
                Check(hud.InteractionState == AnimalGame.MainUI.InventoryInteractionState.Menu && menu.SelectedIndex == 0, "Click opens menu defaulting to USE");
                Check(!hud.Inventory.IsUnread(0) && hud.Inventory.IsUnread(1), "Click acknowledges only its stack");
                Check(menu.Query<InventoryInteractionButtonElement>().ToList().Count == 4, "Four independent custom menu buttons");
                Call(hud, "ExecuteInventoryAction", 2);
                Check(hud.InteractionState == AnimalGame.MainUI.InventoryInteractionState.Menu, "Opening click cannot immediately execute an action");
                PointerMove(menu.Q<InventoryInteractionButtonElement>("INFO"), Vector2.zero);
                Check(menu.SelectedIndex == 0, "Stationary pointer does not steal menu selection");
                PointerMove(menu.Q<InventoryInteractionButtonElement>("INFO"), Vector2.one);
                Check(menu.SelectedIndex == 1 && hud.InteractionState == AnimalGame.MainUI.InventoryInteractionState.Menu, "Mouse movement navigates menu without executing or closing");
                Fresh(hud); Call(hud, "ExecuteInventoryAction", 2);
                Check(hud.InteractionState == AnimalGame.MainUI.InventoryInteractionState.Move, "MOVE closes menu and enters move mode");
                Call(hud, "SetMoveTarget", 2);
                Check(hud.Items[0] == metal && hud.Items[2] == null, "Preview does not mutate inventory");
                Check(root.Q("InventorySlot3").Q<Image>("ItemImage").sprite == metal.Image &&
                    root.Q("InventorySlot3").Q<EmptyInventoryElement>().style.display.value == DisplayStyle.None, "Preview shows sprite and hides cross");
                using (var leave = PointerLeaveEvent.GetPooled())
                {
                    var slot = root.Q("InventorySlot3");
                    leave.target = slot; slot.SendEvent(leave);
                }
                Check(hud.SelectedIndex == 2 && (int)Field(hud, "moveTarget") == 2 &&
                    root.Q("InventorySlot3").Q<EmptyInventoryElement>().style.display.value == DisplayStyle.None,
                    "Leaving the slot retains the frame and preview");
                PointerMove(root, Vector2.one);
                Check(hud.SelectedIndex == 2 && (int)Field(hud, "moveTarget") == 2,
                    "Moving the mouse outside the grid retains the current target");
                Call(hud, "SetMoveTarget", 7);
                Call(hud, "NavigateInventory", Vector2Int.right);
                Check(hud.SelectedIndex == 7 && (int)Field(hud, "moveTarget") == 7 &&
                    root.Q("InventorySlot8").Q<Image>("ItemImage").sprite == metal.Image &&
                    root.Q("InventorySlot8").Q<EmptyInventoryElement>().style.display.value == DisplayStyle.None,
                    "Right boundary keeps the current empty slot preview");
                Call(hud, "NavigateInventory", new Vector2Int(0, 1));
                Check(hud.SelectedIndex == 7 && (int)Field(hud, "moveTarget") == 7,
                    "Bottom boundary also retains the current target");
                Call(hud, "SetMoveTarget", 1);
                Check(hud.SelectedIndex == 1 && (int)Field(hud, "moveTarget") == -1, "Frame can pass through an occupied slot");
                PointerMove(root, Vector2.one);
                Check(hud.SelectedIndex == 1 && (int)Field(hud, "moveTarget") == -1 &&
                    root.Q("InventorySlot8").Q<EmptyInventoryElement>().style.display.value == DisplayStyle.Flex,
                    "Leaving an occupied slot does not restore the previous empty slot preview");
                Call(hud, "ConfirmInventoryMove");
                Check(hud.Items[0] == metal && hud.Items[1] == synthetic, "Confirm on occupied slot does nothing");
                Check(root.Q("InventorySlot3").Q<EmptyInventoryElement>().style.display.value == DisplayStyle.Flex, "Previous preview restores cross");
                Call(hud, "SetMoveTarget", 4); Call(hud, "BackInventory");
                Check(hud.IsInventoryOpen && hud.SelectedIndex == 0 && hud.Items[0] == metal && hud.Items[4] == null, "Cancel restores source selection without closing");
                Fresh(hud); Call(hud, "ClickInventorySlot", 0); Fresh(hud); Call(hud, "ExecuteInventoryAction", 2);
                Fresh(hud); Call(hud, "ConfirmInventoryMove");
                Check(hud.IsInventoryOpen && hud.InteractionState == AnimalGame.MainUI.InventoryInteractionState.Browse &&
                    hud.SelectedIndex == 0 && hud.Items[0] == metal && hud.Inventory.Quantity(0) == 2,
                    "Confirming at the source ends MOVE without changing the stack");
                Fresh(hud); Call(hud, "ClickInventorySlot", 0); Fresh(hud); Call(hud, "ExecuteInventoryAction", 2);
                Call(hud, "SetMoveTarget", 4); Fresh(hud); Call(hud, "ClickInventorySlot", 0);
                Check(hud.InteractionState == AnimalGame.MainUI.InventoryInteractionState.Browse && hud.SelectedIndex == 0 &&
                    hud.Items[0] == metal && hud.Inventory.Quantity(0) == 2 && hud.Items[4] == null &&
                    root.Q("InventorySlot5").Q<EmptyInventoryElement>().style.display.value == DisplayStyle.Flex,
                    "Clicking the source completes MOVE and clears the previous preview");
                Call(hud, "ClickInventorySlot", 0);
                Check(hud.InteractionState == AnimalGame.MainUI.InventoryInteractionState.Browse,
                    "Source confirmation click cannot also reopen the menu");
                Fresh(hud); Call(hud, "ClickInventorySlot", 0); Fresh(hud); Call(hud, "ExecuteInventoryAction", 2);
                Call(hud, "SetMoveTarget", 7); Call(hud, "NavigateInventory", Vector2Int.right);
                Fresh(hud); Call(hud, "ConfirmInventoryMove");
                Check(hud.Items[0] == null && hud.Items[7] == metal && hud.Inventory.Quantity(7) == 2, "Confirm transfers exactly the whole stack");
                Check(hud.SelectedIndex == 7 && hud.InteractionState == AnimalGame.MainUI.InventoryInteractionState.Browse, "Commit selects destination in browse mode");
                Call(hud, "NavigateInventory", new Vector2Int(0, -1));
                Check(hud.Inventory.IsUnread(1), "Navigating elsewhere leaves the other new type unread");
                Fresh(hud); Call(hud, "ClickInventorySlot", 1);
                Check(!hud.Inventory.HasUnread && root.Q<Label>("Label-AStar").style.display.value == DisplayStyle.None, "Last explicit selection hides aggregate star");
                Call(hud, "BackInventory");
                int uses = 0; hud.UseRequested += _ => uses++;
                Fresh(hud); Call(hud, "ClickInventorySlot", 7); Fresh(hud); Call(hud, "ExecuteInventoryAction", 0);
                Check(uses == 1 && hud.Inventory.Quantity(7) == 2, "USE placeholder fires once and consumes nothing");
                foreach (int slot in new[] { 0, 1, 2, 3, 4, 5, 6, 7 })
                {
                    hud.SetItem(slot, metal); hud.SelectItem(slot); Fresh(hud); Call(hud, "OpenInteractionMenu"); Layout(root);
                    var grid = root.Q("InventoryGrid"); var cell = root.Q("InventorySlot" + (slot + 1));
                    Check(Mathf.Abs(menu.style.width.value.value - cell.layout.width) < .1f &&
                        Mathf.Abs(menu.style.height.value.value - cell.layout.height * 2) < .1f, "Menu dimensions slot " + slot);
                    float expectedY = grid.layout.y + cell.layout.center.y - (slot >= 6 ? cell.layout.height * 2 : 0);
                    Check(Mathf.Abs(menu.style.top.value.value - expectedY) < .1f, "Menu anchor slot " + slot);
                    // The topmost pick within the overlay must be its button, never the covered inventory slot.
                    var hit = root.panel.Pick(menu.LocalToWorld(new Vector2(menu.layout.width * .5f, menu.layout.height * .2f)));
                    Check(hit != null && menu.Contains(hit), "Menu owns hit testing slot " + slot);
                    if (slot == 2) Capture(root, texture);
                    Call(hud, "BackInventory");
                }
                Fresh(hud); Call(hud, "ClickInventorySlot", 0);
                PointerMove(root.Q("InventorySlot8"), Vector2.one);
                Check(hud.SelectedIndex == 7 && hud.InteractionState == AnimalGame.MainUI.InventoryInteractionState.Browse, "Mouse entering another item closes current menu without opening another");
                Fresh(hud); Call(hud, "ClickInventorySlot", 7); Fresh(hud); Call(hud, "ExecuteInventoryAction", 3);
                Check(hud.Items[7] == null, "DISCARD deletes whole stack");
                Fresh(hud); Call(hud, "ClickInventorySlot", 0); Fresh(hud); Call(hud, "ExecuteInventoryAction", 2);
                Call(hud, "SetMoveTarget", 7); hud.SetItem(7, synthetic);
                Check(hud.InteractionState == AnimalGame.MainUI.InventoryInteractionState.Browse && hud.Items[7] == synthetic, "External destination write cancels move safely");
                // Actual input-system state tests exercise N/B/A and right click dispatch priority.
#if ENABLE_INPUT_SYSTEM
                if (Application.isPlaying)
                {
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                pad = InputSystem.AddDevice<Gamepad>(); keyboard = InputSystem.AddDevice<Keyboard>(); mouse = InputSystem.AddDevice<Mouse>();
                void Input(GamepadState gamepad, KeyboardState keys = default, MouseState pointer = default)
                {
                    InputSystem.QueueStateEvent(pad, gamepad); InputSystem.QueueStateEvent(keyboard, keys);
                    InputSystem.QueueStateEvent(mouse, pointer);
                    InputSystem.Update();
                    Fresh(hud); Call(hud, "HandleInput");
                }
                hud.SelectItem(0); Input(new GamepadState().WithButton(GamepadButton.South));
                Check(hud.InteractionState == AnimalGame.MainUI.InventoryInteractionState.Menu,
                    "Gamepad A opens menu (value=" + pad.buttonSouth.ReadValue() + ", pressed=" + pad.buttonSouth.wasPressedThisFrame + ", state=" + hud.InteractionState + ")");
                Input(default); Input(new GamepadState().WithButton(GamepadButton.DpadDown));
                Check(menu.SelectedIndex == 1 && hud.SelectedIndex == 0, "Dpad navigates menu without moving grid selection");
                Input(default); Input(new GamepadState { leftStick = Vector2.down });
                Check(menu.SelectedIndex == 2, "Left stick navigates menu");
                Input(new GamepadState { leftStick = Vector2.down });
                Check(menu.SelectedIndex == 2, "Held stick respects initial repeat delay");
                Input(default); Input(new GamepadState().WithButton(GamepadButton.East));
                Check(hud.IsInventoryOpen && hud.InteractionState == AnimalGame.MainUI.InventoryInteractionState.Browse, "B first closes only menu");
                Input(default); Input(default, new KeyboardState(Key.N));
                Check(!hud.IsInventoryOpen, "N closes from browse");
                Call(hud, "AdvanceInventoryAnimation", 1f); Layout(root);
                Input(default); Input(default, new KeyboardState(Key.N)); Call(hud, "AdvanceInventoryAnimation", 1f); Layout(root);
                Check(hud.IsInventoryOpen, "N opens closed inventory");
                Input(default); hud.SelectItem(0); Input(new GamepadState().WithButton(GamepadButton.South));
                Input(default); Input(default, default, new MouseState().WithButton(MouseButton.Right));
                Check(hud.InteractionState == AnimalGame.MainUI.InventoryInteractionState.Menu, "Right click has no menu effect");
                Input(default); Fresh(hud); Call(hud, "ExecuteInventoryAction", 2); Call(hud, "SetMoveTarget", 7);
                Input(default, default, new MouseState().WithButton(MouseButton.Right));
                Check(hud.IsInventoryOpen && hud.InteractionState == AnimalGame.MainUI.InventoryInteractionState.Browse, "Right click cancels MOVE only");
                Input(default); Fresh(hud); Call(hud, "ClickInventorySlot", 0); Fresh(hud); Call(hud, "ExecuteInventoryAction", 2);
                Input(default, new KeyboardState(Key.N));
                Check(hud.IsInventoryOpen && hud.InteractionState == AnimalGame.MainUI.InventoryInteractionState.Browse, "N first cancels MOVE");
                Input(default);
                Fresh(hud); Call(hud, "ClickInventorySlot", 0); Fresh(hud); Call(hud, "ExecuteInventoryAction", 2);
                Input(new GamepadState().WithButton(GamepadButton.South));
                Check(hud.IsInventoryOpen && hud.InteractionState == AnimalGame.MainUI.InventoryInteractionState.Browse &&
                    hud.SelectedIndex == 0 && hud.Items[0] == metal && hud.Inventory.Quantity(0) == 1,
                    "Gamepad A at the source completes MOVE in place");
                Input(default);
                }
#endif
                hud.CloseInventory(); Call(hud, "AdvanceInventoryAnimation", 1f);
                Check(Time.timeScale == previousTimeScale, "Closing restores prior time scale");
            }
            finally
            {
#if ENABLE_INPUT_SYSTEM
                if (pad != null) InputSystem.RemoveDevice(pad);
                if (keyboard != null) InputSystem.RemoveDevice(keyboard);
                if (mouse != null) InputSystem.RemoveDevice(mouse);
                InputSystem.settings.backgroundBehavior = previousBackground;
                InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInput;
#endif
                if (obj != null) Object.DestroyImmediate(obj);
                if (settings != null) Object.DestroyImmediate(settings);
                if (texture != null) { texture.Release(); Object.DestroyImmediate(texture); }
                Time.timeScale = previousTimeScale;
            }
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }

        public static void RunRecycleChecks()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Recycle integration checks require Play Mode.");
            var root = new GameObject("Inventory recycle validation");
            try
            {
                var hud = root.AddComponent<Hud>();
                ConfigureRewards(hud);
                var wrongOwner = new GameObject("Wrong owner"); wrongOwner.transform.parent = root.transform;
                AnimalGame.Garbage.GarbageItem Garbage(RecyclableSize size)
                {
                    var obj = new GameObject("Test " + size); obj.transform.parent = root.transform; obj.SetActive(false);
                    var garbage = obj.AddComponent<AnimalGame.Garbage.GarbageItem>();
                    var data = new SerializedObject(garbage); data.FindProperty("size").enumValueIndex = (int)size;
                    data.ApplyModifiedPropertiesWithoutUndo(); obj.SetActive(true);
                    garbage.Interaction.SetKind(WorldInteractionKind.Grabbable);
                    return garbage;
                }
                int Total()
                {
                    int total = 0; for (int i = 0; i < hud.Inventory.Capacity; i++) total += hud.Inventory.Quantity(i);
                    return total;
                }
                var small = Garbage(RecyclableSize.Small);
                Check(small.Interaction.TryGrab(hud), "Small garbage can be grabbed");
                Check(Total() == 0, "Grabbing grants nothing");
                small.Interaction.Release(hud);
                Check(Total() == 0, "Cancelling grants nothing");
                small.Interaction.TryGrab(hud); small.Interaction.Recycle(wrongOwner);
                Check(Total() == 0 && small.gameObject.activeSelf, "Wrong owner cannot complete recycling");
                small.Interaction.Recycle(hud);
                Check(Total() == 1 && !small.gameObject.activeSelf, "Successful completion grants small reward exactly once");
                small.Interaction.Recycle(hud); GarbageInventoryRewards.Grant(small);
                Check(Total() == 1, "Repeated completion cannot duplicate reward");
                var medium = Garbage(RecyclableSize.Medium); medium.Interaction.TryGrab(hud); medium.Interaction.Recycle(hud);
                Check(Total() == 3, "Successful medium completion adds two items");
                var big = Garbage(RecyclableSize.Big); big.Interaction.TryGrab(hud); big.Interaction.Recycle(hud);
                Check(Total() == 3, "Big completion grants no unspecified reward");
                var destroyed = Garbage(RecyclableSize.Small); Object.DestroyImmediate(destroyed.gameObject);
                Check(Total() == 3, "Destroying garbage does not grant a reward");
                Debug.Log("INVENTORY RECYCLE PLAY CHECKS PASSED");
            }
            finally { Object.DestroyImmediate(root); }
        }
        private static void ConfigureRewards(Hud hud)
        {
            var data = new SerializedObject(hud);
            data.FindProperty("inventoryRewards").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<InventoryRewardTable>("Assets/Data/Inventory/InventoryRewards.asset");
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        private static object Field(Hud hud, string name) => typeof(InventoryController).GetField(name, Flags).GetValue(hud.InventoryController);
        private static void Fresh(Hud hud) => typeof(InventoryController).GetField("lastInventoryActionFrame", Flags).SetValue(hud.InventoryController, -1);
        private static object Call(Hud hud, string method, params object[] args)
        {
            var inventoryMethod = typeof(InventoryController).GetMethod(method, Flags);
            return inventoryMethod != null ? inventoryMethod.Invoke(hud.InventoryController, args)
                : typeof(Hud).GetMethod(method, Flags).Invoke(hud, args);
        }
        private static void Layout(VisualElement root) => root.panel.GetType().GetMethod("ValidateLayout", Flags).Invoke(root.panel, null);
        private static void PointerMove(VisualElement target, Vector2 delta)
        {
            using var evt = PointerMoveEvent.GetPooled(new Event
                { type = EventType.MouseMove, mousePosition = target.worldBound.center, delta = delta });
            evt.target = target; target.SendEvent(evt);
        }
        private static void Capture(VisualElement root, RenderTexture target)
        {
            var repaint = root.panel.GetType().GetMethod("Repaint", Flags, null, new[] { typeof(Event) }, null);
            repaint?.Invoke(root.panel, new object[] { new Event { type = EventType.Repaint } });
            root.panel.GetType().GetMethod("Render", Flags, null, Type.EmptyTypes, null)?.Invoke(root.panel, null);
            var previous = RenderTexture.active;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "inventory-menu-validation.png"), pixels.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(pixels); }
        }
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Inventory: " + message);
            checks++;
        }
    }
}
