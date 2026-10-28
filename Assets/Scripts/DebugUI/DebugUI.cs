using System.Collections.Generic;
using AnimalGame.Tutorial;
using UnityEngine;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AnimalGame.AnimalDebug
{
    [DefaultExecutionOrder(-1200)]
    [DisallowMultipleComponent, RequireComponent(typeof(UIDocument))]
    public sealed class DebugUI : MonoBehaviour
    {
        private static DebugUI active;
        private static int closedFrame = -1;
        public static bool IsOpen => active != null && active.open;
        public static bool BlocksOtherPanels => IsOpen || closedFrame == Time.frameCount;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { active = null; closedFrame = -1; }

        private UIDocument document;
        private PanelSettings settings, originalSettings;
        private VisualElement root, panel;
        private DebugUITab[] tabs;
        private readonly List<Button> tabButtons = new();
        private int selectedTab;
        private bool open;
        private CursorLockMode savedCursorLock;
        public UIDocument Document => document;
        public DebugUITab SelectedTab => tabs[selectedTab];

        private void Awake()
        {
            document = GetComponent<UIDocument>();
            tabs = GetComponents<DebugUITab>();
            originalSettings = document.panelSettings;
            settings = originalSettings != null ? Instantiate(originalSettings) : ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = "Debug UI (Runtime)";
            settings.hideFlags = HideFlags.DontSave;
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = .5f;
            settings.targetTexture = null;
            settings.sortingOrder = 80;
            document.panelSettings = settings;
        }

        private void OnEnable() => Bind();
        private void Bind()
        {
            // UIDocument can enable after this controller; Update binds once its tree exists.
            if (document.rootVisualElement == null) return;
            if (root == document.rootVisualElement && panel != null) return;
            root = document.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = root.style.top = root.style.right = root.style.bottom = 0;
            panel = new VisualElement { name = "DebugPanel" };
            panel.AddToClassList("debug-panel");
            var header = new VisualElement { name = "DebugTabs" };
            header.AddToClassList("row");
            tabButtons.Clear();
            for (int i = 0; i < tabs.Length; i++)
            {
                int index = i;
                var button = new Button(() => SelectTab(index)) { text = tabs[i].Title, focusable = false };
                button.AddToClassList("grow");
                header.Add(button);
                tabButtons.Add(button);
                tabs[i].Bind(root);
                tabs[i].Content.RemoveFromHierarchy();
            }
            var close = new Button(Close) { name = "DebugClose", text = "Close", focusable = false };
            header.Add(close);
            panel.Add(header);
            var hint = new Label("M: close | Esc: back | Arrow keys / Tab: navigate | Enter: select");
            hint.AddToClassList("hint");
            panel.Add(hint);
            foreach (DebugUITab tab in tabs) { panel.Add(tab.Content); tab.Hide(); }
            root.Add(panel);
            // Consume Toolkit navigation from gameplay input; keyboard navigation is handled below.
            root.RegisterCallback<NavigationMoveEvent>(evt =>
            {
                root.focusController?.IgnoreEvent(evt);
                evt.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationSubmitEvent>(evt => evt.StopImmediatePropagation(), TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationCancelEvent>(evt => evt.StopImmediatePropagation(), TrickleDown.TrickleDown);
            panel.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            SelectTab(selectedTab);
        }

        public void SelectTab(int index)
        {
            if (index < 0 || index >= tabs.Length) throw new System.ArgumentOutOfRangeException(nameof(index));
            SelectedTab.Hide();
            selectedTab = index;
            for (int i = 0; i < tabs.Length; i++)
                tabButtons[i].EnableInClassList("highlight", i == index);
            if (open) SelectedTab.Show();
        }

        private bool OtherPanelOpen()
        {
            var hud = AnimalGame.MainUI.MainUI.Active;
            if (hud != null && (hud.IsInventoryOpen || hud.InventoryProgress > .001f)) return true;
            foreach (GameObject sceneRoot in gameObject.scene.GetRootGameObjects())
            {
                foreach (var tutorial in sceneRoot.GetComponentsInChildren<TutorialDialoguePanel>())
                    if (tutorial.isActiveAndEnabled && tutorial.IsVisible && !tutorial.IsCollapsed) return true;
                foreach (var terminal in sceneRoot.GetComponentsInChildren<AnimalGame.Demos.CheckpointRefit.CheckpointRefitTerminal>())
                    if (terminal.isActiveAndEnabled && terminal.IsOpen) return true;
            }
            return false;
        }

        public bool Open()
        {
            if (open) return true;
            if (panel == null || OtherPanelOpen() || IsOpen) return false;
            active = this; open = true;
            savedCursorLock = Cursor.lockState;
            Cursor.lockState = CursorLockMode.None;
            panel.style.display = DisplayStyle.Flex;
            SelectedTab.Show();
            return true;
        }

        public void Close()
        {
            if (!open) return;
            open = false;
            SelectedTab.Hide();
            panel.style.display = DisplayStyle.None;
            Cursor.lockState = savedCursorLock;
            closedFrame = Time.frameCount;
            if (active == this) active = null;
        }

        private void Update()
        {
            Bind();
            bool toggle = false, back = false, submit = false, click = false;
            int move = 0; Vector2 mouse = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                toggle = keyboard.mKey.wasPressedThisFrame; back = keyboard.escapeKey.wasPressedThisFrame;
                submit = keyboard.enterKey.wasPressedThisFrame;
                if (keyboard.upArrowKey.wasPressedThisFrame) move = -1;
                if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.tabKey.wasPressedThisFrame) move = 1;
            }
            if (Mouse.current != null) { click = Mouse.current.leftButton.wasPressedThisFrame; mouse = Mouse.current.position.ReadValue(); }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current == null)
#endif
            {
                toggle = Input.GetKeyDown(KeyCode.M); back = Input.GetKeyDown(KeyCode.Escape); submit = Input.GetKeyDown(KeyCode.Return);
                if (Input.GetKeyDown(KeyCode.UpArrow)) move = -1;
                if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.Tab)) move = 1;
            }
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current == null)
#endif
            { click = Input.GetMouseButtonDown(0); mouse = Input.mousePosition; }
#endif
            if (toggle) { if (open) Close(); else Open(); return; }
            if (!open) return;
            if (back) { if (!SelectedTab.Back()) Close(); return; }
            SelectedTab.HandleInput(move, submit, click, mouse);
        }

        private void OnDisable() => Close();
        private void OnDestroy()
        {
            Close();
            if (document != null) document.panelSettings = originalSettings;
            if (settings != null) { if (Application.isPlaying) Destroy(settings); else DestroyImmediate(settings); }
        }
    }
}
