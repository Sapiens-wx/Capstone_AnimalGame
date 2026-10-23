using System.Collections.Generic;
using AnimalGame.Animals;
using AnimalGame.MapTest;
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
    public sealed class AnimalDebugUI : MonoBehaviour
    {
        private static AnimalDebugUI active;
        private static int closedFrame = -1;
        public static bool IsOpen => active != null && active.open;
        public static bool BlocksOtherPanels => IsOpen || closedFrame == Time.frameCount;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { active = null; closedFrame = -1; }

        private UIDocument document;
        private PanelSettings settings, originalSettings;
        private VisualElement root, panel, animalPopup, commandPopup, healthFill;
        private Button animalButton, sampleButton, commandButton, executeButton;
        private Label health, leader, group, state, action, message;
        private ListView animalList, commandList;
        private Arrow arrow, previewArrow;
        private readonly List<AnimalAgent> animals = new();
        private readonly List<AnimalAgent> refreshed = new();
        private readonly List<Button> controls = new();
        private AnimalAgent selected, highlighted;
        private AnimalDebugCommand command;
        private AnimalDebugCommandResult result;
        private bool open, sampling, savedCursorVisible;
        private CursorLockMode savedCursorLock;
        private int sampleFrame, lastActionFrame, focusedControl, animalIndex, commandIndex;
        private float refreshAt;
        public AnimalAgent Selected => selected;
        public UIDocument Document => document;

        private void Awake()
        {
            document = GetComponent<UIDocument>();
            originalSettings = document.panelSettings;
            settings = originalSettings != null ? Instantiate(originalSettings) : ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = "Animal AI Debug (Runtime)";
            settings.hideFlags = HideFlags.DontSave;
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = .5f;
            settings.targetTexture = null;
            settings.sortingOrder = 80;
            document.panelSettings = settings;
            gameObject.AddComponent<AnimalDebugProjection>().Owner = this;
        }

        private void OnEnable() { Bind(); }
        private void Bind()
        {
            VisualElement next = document != null ? document.rootVisualElement : null;
            if (root == next && panel != null) return;
            root = next;
            if (root == null) return;
            panel = root.Q("AnimalPanel");
            if (panel == null) return;
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = root.style.top = root.style.right = root.style.bottom = 0;
            animalButton = root.Q<Button>("AnimalName"); sampleButton = root.Q<Button>("Sample");
            commandButton = root.Q<Button>("Command"); executeButton = root.Q<Button>("Execute");
            animalPopup = root.Q("AnimalPopup"); commandPopup = root.Q("CommandPopup");
            animalList = root.Q<ListView>("AnimalList"); commandList = root.Q<ListView>("CommandList");
            healthFill = root.Q("HealthFill"); health = root.Q<Label>("HealthValue");
            leader = root.Q<Label>("Leader"); group = root.Q<Label>("Group"); state = root.Q<Label>("State");
            action = root.Q<Label>("Action"); message = root.Q<Label>("Message");
            // A rebuilt document has new buttons; binding once per root avoids duplicate callbacks.
            animalButton.clicked += ToggleAnimals; sampleButton.clicked += ToggleSampling;
            commandButton.clicked += ToggleCommands; executeButton.clicked += Execute;
            controls.Clear(); controls.Add(animalButton); controls.Add(sampleButton); controls.Add(commandButton); controls.Add(executeButton);
            SetupAnimals(); SetupCommands();
            root.Q("NameRow").RegisterCallback<GeometryChangedEvent>(PositionPopups);
            root.Q("CommandRow").RegisterCallback<GeometryChangedEvent>(PositionPopups);
            panel.RegisterCallback<GeometryChangedEvent>(PositionPopups);
            arrow = new Arrow(); root.Add(arrow);
            previewArrow = new Arrow { name = "AnimalPreviewArrow" }; root.Add(previewArrow);
            // Toolkit's input module can also map gameplay WASD/stick to focus navigation.
            // Only the explicit D-pad, arrow-key and Tab handling below may move our focus.
            root.RegisterCallback<NavigationMoveEvent>(evt =>
            {
                root.focusController?.IgnoreEvent(evt);
                evt.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationSubmitEvent>(evt => evt.StopImmediatePropagation(), TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationCancelEvent>(evt => evt.StopImmediatePropagation(), TrickleDown.TrickleDown);
            SetPopup(null); panel.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            RefreshDetails();
        }

        private void PositionPopups(GeometryChangedEvent _)
        {
            float afterName = root.Q("NameRow").layout.yMax + 4;
            float beforeCommand = root.Q("CommandRow").layout.yMin - 4;
            float height = panel.layout.height;
            if (height <= 0f) return;
            animalPopup.style.top = afterName;
            animalPopup.style.height = Mathf.Max(40, Mathf.Min(height * .53f, height - afterName - 12));
            float commandHeight = Mathf.Max(48, Mathf.Min(height * .56f, beforeCommand - afterName));
            commandPopup.style.top = Mathf.Max(afterName, beforeCommand - commandHeight);
            commandPopup.style.bottom = StyleKeyword.Auto;
            commandPopup.style.height = commandHeight;
        }

        private void SetupAnimals()
        {
            animalList.itemsSource = animals;
            animalList.fixedItemHeight = 38;
            animalList.selectionType = SelectionType.None;
            animalList.makeItem = () =>
            {
                var row = new Button { focusable = false }; row.AddToClassList("animal-row");
                row.clicked += () => { if (row.userData is AnimalAgent animal && IsValid(animal)) Select(animal); };
                row.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    if (row.userData is AnimalAgent animal && IsValid(animal))
                    { highlighted = animal; animalIndex = animals.IndexOf(animal); RefreshRows(); }
                });
                row.RegisterCallback<PointerLeaveEvent>(_ => { if (highlighted == row.userData as AnimalAgent) { highlighted = null; RefreshRows(); } });
                return row;
            };
            animalList.bindItem = (element, index) =>
            {
                var row = (Button)element;
                row.userData = animals[index]; row.text = AnimalDebugSelection.DisplayName(animals[index]);
                row.EnableInClassList("highlight", animals[index] == highlighted);
            };
            animalList.unbindItem = (element, _) => { element.userData = null; element.RemoveFromClassList("highlight"); };
        }

        private void SetupCommands()
        {
            commandList.itemsSource = AnimalDebugCommandService.Commands;
            commandList.fixedItemHeight = 48;
            commandList.selectionType = SelectionType.None;
            commandList.makeItem = () =>
            {
                var row = new Button { focusable = false }; row.AddToClassList("animal-row");
                row.clicked += () => { if (row.userData is AnimalDebugCommand next) ChooseCommand(next); };
                row.RegisterCallback<PointerEnterEvent>(_ =>
                { if (row.userData is AnimalDebugCommand next) { commandIndex = (int)next; commandList.RefreshItems(); } });
                return row;
            };
            commandList.bindItem = (element, index) =>
            {
                var row = (Button)element; var next = AnimalDebugCommandService.Commands[index]; row.userData = next;
                bool available = AnimalDebugCommandService.Validate(selected, next, out string reason);
                row.text = AnimalDebugCommandService.Label(next) + (available ? "" : " — unavailable");
                row.tooltip = available ? "" : reason;
                row.EnableInClassList("highlight", index == commandIndex);
            };
            commandList.unbindItem = (element, _) => element.userData = null;
        }

        private bool IsValid(AnimalAgent animal) => AnimalDebugSelection.IsSelectable(animal, gameObject.scene);
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
            savedCursorVisible = Cursor.visible; savedCursorLock = Cursor.lockState;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            panel.style.display = DisplayStyle.Flex;
            RefreshAnimals(); RefreshDetails(); FocusControl(0);
            lastActionFrame = Time.frameCount;
            return true;
        }

        public void Close()
        {
            if (!open) return;
            open = sampling = false; SetPopup(null);
            panel.style.display = DisplayStyle.None;
            Cursor.visible = savedCursorVisible; Cursor.lockState = savedCursorLock;
            closedFrame = Time.frameCount;
            if (active == this) active = null;
        }

        public void Select(AnimalAgent animal)
        {
            if (!IsValid(animal)) return;
            selected = animal; result = null; sampling = false; message.text = "";
            SetPopup(null); RefreshDetails();
        }

        private void SetPopup(VisualElement popup)
        {
            if (animalPopup == null) return;
            animalPopup.style.display = popup == animalPopup ? DisplayStyle.Flex : DisplayStyle.None;
            commandPopup.style.display = popup == commandPopup ? DisplayStyle.Flex : DisplayStyle.None;
            highlighted = null;
            RefreshArrow();
        }
        private bool Visible(VisualElement element) => element.style.display.value != DisplayStyle.None;
        private void ToggleAnimals()
        {
            sampling = false;
            bool show = !Visible(animalPopup); SetPopup(show ? animalPopup : null);
            if (show) { RefreshAnimals(); animalIndex = Mathf.Max(0, animals.IndexOf(selected)); HighlightAnimal(); }
        }
        private void ToggleCommands()
        {
            sampling = false;
            bool show = !Visible(commandPopup); SetPopup(show ? commandPopup : null);
            if (show) { commandIndex = (int)command; commandList.RefreshItems(); }
        }
        private void ToggleSampling()
        {
            sampling = !sampling; SetPopup(null); sampleFrame = Time.frameCount; result = null;
            message.text = sampling ? "Click an animal. B / Esc cancels." : "Sampling cancelled.";
        }
        private void ChooseCommand(AnimalDebugCommand next)
        { command = next; SetPopup(null); commandButton.text = AnimalDebugCommandService.Label(command) + "  ▼"; }
        private void Execute()
        {
            if (lastActionFrame == Time.frameCount) return;
            lastActionFrame = Time.frameCount;
            result = AnimalDebugCommandService.Execute(selected, command); RefreshDetails();
        }
        private void Back()
        {
            if (sampling) { sampling = false; message.text = "Sampling cancelled."; }
            else if (Visible(animalPopup) || Visible(commandPopup)) SetPopup(null);
            else Close();
        }

        private void RefreshAnimals()
        {
            refreshed.Clear();
            foreach (AnimalAgent animal in AnimalAgent.ActiveAgents) if (IsValid(animal)) refreshed.Add(animal);
            refreshed.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));
            bool changed = refreshed.Count != animals.Count;
            for (int i = 0; !changed && i < refreshed.Count; i++) changed = refreshed[i] != animals[i];
            if (changed) { animals.Clear(); animals.AddRange(refreshed); animalList.RefreshItems(); }
            if (!IsValid(selected))
            {
                if (!ReferenceEquals(selected, null) && !sampling) message.text = "";
                selected = null; result = null;
            }
            if (!IsValid(highlighted)) highlighted = null;
            animalIndex = Mathf.Clamp(animalIndex, 0, Mathf.Max(0, animals.Count - 1));
        }

        private void RefreshDetails()
        {
            if (panel == null) return;
            bool valid = IsValid(selected);
            animalButton.text = (valid ? AnimalDebugSelection.DisplayName(selected) : "Select an animal") + "  ▼";
            sampleButton.text = sampling ? "Cancel pick" : "Pick";
            commandButton.text = AnimalDebugCommandService.Label(command) + "  ▼";
            executeButton.SetEnabled(valid);
            float ratio = valid && selected.Health.MaximumHealth > 0 ? Mathf.Clamp01(selected.Health.CurrentHealth / selected.Health.MaximumHealth) : 0;
            healthFill.style.width = Length.Percent(ratio * 100);
            health.text = valid ? $"{selected.Health.CurrentHealth:0.#} / {selected.Health.MaximumHealth:0.#}  ({ratio:P0})" : "—";
            AnimalGroup team = valid ? selected.Runtime.Group : null;
            int count = valid ? 1 : 0;
            if (team != null)
            {
                count = 0;
                foreach (AnimalAgent member in team.Members)
                    if (member != null && member.IsInitialized && !member.Runtime.Removed && !member.Health.IsDead && member.isActiveAndEnabled) count++;
            }
            leader.text = "Leader: " + (!valid ? "—" : team == null ? "No (ungrouped)" : team.Leader == selected ? "Yes" : "No");
            group.text = "Group size: " + (valid ? count.ToString() : "—");
            state.text = "State: " + (valid ? selected.CurrentState.ToString() : "—");
            action.text = "Action: " + (valid ? selected.Brain.CurrentActionName : "—");
            if (result != null && result.Agent == selected)
            {
                bool error = result.Status == AnimalDebugCommandStatus.Rejected || result.Status == AnimalDebugCommandStatus.Failed;
                message.text = (error ? "Error: " : "") + result.Message;
            }
        }

        private void RefreshRows()
        {
            foreach (var row in animalList.Query<Button>().ToList())
                if (row.userData is AnimalAgent animal) row.EnableInClassList("highlight", animal == highlighted);
        }
        private void HighlightAnimal()
        {
            highlighted = animals.Count > 0 ? animals[animalIndex] : null;
            RefreshRows();
            if (animals.Count > 0) animalList.ScrollToItem(animalIndex);
        }
        private void FocusControl(int index)
        {
            focusedControl = (index + controls.Count) % controls.Count;
            if (!controls[focusedControl].enabledSelf) focusedControl = 0;
            controls[focusedControl].Focus();
        }

        private void Update()
        {
            if (document != null && (root != document.rootVisualElement || panel == null)) Bind();
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
            foreach (Gamepad pad in Gamepad.all)
            {
                if (!pad.added) continue;
                if (!open && pad.dpad.up.wasPressedThisFrame) toggle = true;
                back |= pad.buttonEast.wasPressedThisFrame; submit |= pad.buttonSouth.wasPressedThisFrame;
                if (pad.dpad.up.wasPressedThisFrame) move = -1;
                if (pad.dpad.down.wasPressedThisFrame) move = 1;
            }
            if (Mouse.current != null) { click = Mouse.current.leftButton.wasPressedThisFrame; mouse = Mouse.current.position.ReadValue(); }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current == null)
#endif
            { toggle |= Input.GetKeyDown(KeyCode.M); back |= Input.GetKeyDown(KeyCode.Escape); submit |= Input.GetKeyDown(KeyCode.Return);
                if (Input.GetKeyDown(KeyCode.UpArrow)) move = -1; if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.Tab)) move = 1; }
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current == null)
#endif
            { click = Input.GetMouseButtonDown(0); mouse = Input.mousePosition; }
#endif
            if (toggle) { if (open) Close(); else Open(); return; }
            if (!open) return;
            if (back) { Back(); return; }
            if (lastActionFrame != Time.frameCount)
            {
                if (Visible(animalPopup))
                {
                    if (move != 0 && animals.Count > 0) { animalIndex = (animalIndex + move + animals.Count) % animals.Count; HighlightAnimal(); }
                    if (submit && animals.Count > 0) Select(animals[animalIndex]);
                }
                else if (Visible(commandPopup))
                {
                    if (move != 0) { commandIndex = (commandIndex + move + AnimalDebugCommandService.Commands.Length) % AnimalDebugCommandService.Commands.Length; commandList.RefreshItems(); commandList.ScrollToItem(commandIndex); }
                    if (submit) ChooseCommand(AnimalDebugCommandService.Commands[commandIndex]);
                }
                else
                {
                    if (move != 0) FocusControl(focusedControl + move);
                    if (submit) { if (focusedControl == 0) ToggleAnimals(); else if (focusedControl == 1) ToggleSampling(); else if (focusedControl == 2) ToggleCommands(); else Execute(); }
                }
            }
            if (sampling && click && Time.frameCount > sampleFrame && !PointerOverUI(mouse))
            {
                RefreshAnimals();
                AnimalAgent picked = AnimalDebugSelection.Pick(GameCamera(), mouse, animals);
                if (picked != null && IsValid(picked)) Select(picked); else message.text = "No animal at this point. Try again.";
            }
            if (Time.unscaledTime >= refreshAt)
            { refreshAt = Time.unscaledTime + .1f; RefreshAnimals(); RefreshDetails(); }
        }

        private bool PointerOverUI(Vector2 screen)
        {
            Vector2 point = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, Screen.height - screen.y));
            if (panel.worldBound.Contains(point)) return true;
            foreach (UIDocument ui in FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
            {
                if (ui == document || ui.rootVisualElement?.panel == null) continue;
                Vector2 local = RuntimePanelUtils.ScreenToPanel(ui.rootVisualElement.panel, new Vector2(screen.x, Screen.height - screen.y));
                VisualElement hit = ui.rootVisualElement.panel.Pick(local);
                if (hit != null && hit != ui.rootVisualElement && hit.pickingMode == PickingMode.Position) return true;
            }
            return UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
        }
        private Camera GameCamera()
        {
            var bootstrap = HeightMapPlayerSceneBootstrap.inst;
            if (bootstrap != null && bootstrap.gameObject.scene == gameObject.scene) return bootstrap.mapCamera;
            return selected != null ? selected.Simulation.World.Camera : Camera.main;
        }

        internal void RefreshArrow()
        {
            AnimalAgent preview = open && Visible(animalPopup) && IsValid(highlighted) ? highlighted : null;
            AnimalAgent persistent = IsValid(selected) ? selected : null;
            RenderArrow(arrow, persistent != null ? persistent : preview);
            RenderArrow(previewArrow, persistent != null && preview != persistent ? preview : null);
        }

        private void RenderArrow(Arrow marker, AnimalAgent animal)
        {
            if (marker == null) return;
            if (!open || root.panel == null || !IsValid(animal)
                || !AnimalDebugSelection.TryBounds(animal, out Bounds bounds))
            { marker.style.display = DisplayStyle.None; return; }
            Camera camera = GameCamera();
            if (camera == null) { marker.style.display = DisplayStyle.None; return; }
            Vector3 projection = camera.WorldToScreenPoint(bounds.center);
            if (projection.z <= 0f) { marker.style.display = DisplayStyle.None; return; }
            Vector2 target = projection, location, direction;
            Rect viewport = camera.pixelRect;
            if (viewport.Contains(target))
            {
                Vector3 side = camera.WorldToScreenPoint(bounds.center + Vector3.right * bounds.extents.x);
                float offset = Mathf.Abs(side.x - target.x) + 28;
                location = target + Vector2.left * offset;
                if (location.x < viewport.xMin + 20) location = target + Vector2.right * offset;
                location.x = Mathf.Clamp(location.x, viewport.xMin + 20, viewport.xMax - 20);
                location.y = Mathf.Clamp(location.y, viewport.yMin + 20, viewport.yMax - 20);
                direction = target - location;
            }
            else { location = AnimalDebugSelection.EdgePoint(viewport, target, 22); direction = target - viewport.center; }
            Vector2 ui = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(location.x, Screen.height - location.y));
            marker.style.display = DisplayStyle.Flex;
            marker.style.left = ui.x - 14; marker.style.top = ui.y - 14;
            Vector2 next = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(location.x + direction.x, Screen.height - location.y - direction.y));
            marker.Direction = (next - ui).normalized; marker.MarkDirtyRepaint();
        }

        private void OnDisable() { Close(); }
        private void OnDestroy()
        {
            Close();
            if (document != null) document.panelSettings = originalSettings;
            if (settings != null) { if (Application.isPlaying) Destroy(settings); else DestroyImmediate(settings); }
        }

        private sealed class Arrow : VisualElement
        {
            public Vector2 Direction = Vector2.right;
            public Arrow()
            {
                name = "AnimalArrow"; pickingMode = PickingMode.Ignore;
                AddToClassList("animal-arrow");
                style.position = Position.Absolute; style.width = style.height = 28; style.display = DisplayStyle.None;
                generateVisualContent += context =>
                {
                    Vector2 center = contentRect.center, direction = Direction.sqrMagnitude > .001f ? Direction : Vector2.right;
                    Vector2 perpendicular = new Vector2(-direction.y, direction.x);
                    var painter = context.painter2D; painter.fillColor = resolvedStyle.color;
                    painter.BeginPath(); painter.MoveTo(center + direction * 13);
                    painter.LineTo(center - direction * 10 + perpendicular * 9);
                    painter.LineTo(center - direction * 10 - perpendicular * 9); painter.ClosePath(); painter.Fill();
                };
            }
        }
    }
}
