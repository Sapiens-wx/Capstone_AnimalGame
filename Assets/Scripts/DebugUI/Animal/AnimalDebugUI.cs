using System.Collections.Generic;
using AnimalGame.Animals;
using AnimalGame.MapTest;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.AnimalDebug
{
    [DisallowMultipleComponent, RequireComponent(typeof(DebugUI))]
    public sealed class AnimalDebugUI : DebugUITab
    {
        public override string Title => "Animal Inspector";
        public override VisualElement Content => panel;
        private UIDocument document;
        private VisualElement root, panel, animalPopup, commandPopup, healthFill;
        private Button animalButton, sampleButton, commandButton, executeButton;
        private Label health, leader, group, state, action, message;
        private ListView animalList, commandList;
        private Arrow arrow, previewArrow;
        private AnimalDebugProjection projection;
        private readonly List<AnimalAgent> animals = new();
        private readonly List<AnimalAgent> refreshed = new();
        private readonly List<Button> controls = new();
        private AnimalAgent selected, highlighted;
        private AnimalDebugCommand command;
        private AnimalDailyBehaviourSettings selectedDaily;
        private int selectedDailyIndex = -1;
        private readonly HashSet<AnimalDebugCommand> expandedCommands = new();
        private readonly List<CommandEntry> commandEntries = new();
        private sealed class CommandEntry
        {
            public AnimalDebugCommand Command;
            public bool IsGroup, IsDailyEntry, IsChild;
            public AnimalDailyBehaviourSettings Daily;
            public int DailyIndex = -1;
            public string Label;
        }
        private AnimalDebugCommandResult result;
        private bool open, sampling;
        private int sampleFrame, lastActionFrame, focusedControl, animalIndex, commandIndex;
        private float refreshAt;
        public AnimalAgent Selected => selected;
        public UIDocument Document => document;

        public override void Bind(VisualElement next)
        {
            document = GetComponent<UIDocument>();
            root = next;
            panel = root.Q("AnimalPanel");
            if (projection == null) projection = gameObject.AddComponent<AnimalDebugProjection>();
            projection.Owner = this;
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
            commandList.itemsSource = commandEntries;
            commandList.fixedItemHeight = 48;
            commandList.selectionType = SelectionType.None;
            ScrollView scroll = commandList.Q<ScrollView>();
            if (scroll != null)
            {
                scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
                scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            }
            commandList.makeItem = () =>
            {
                var row = new Button { focusable = false }; row.AddToClassList("animal-row");
                row.clicked += () => { if (row.userData is CommandEntry entry) ActivateCommand(entry); };
                row.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    if (row.userData is CommandEntry entry)
                    { commandIndex = commandEntries.IndexOf(entry); RefreshCommandHighlight(); }
                });
                return row;
            };
            commandList.bindItem = (element, index) => BindCommandRow((Button)element, index);
            commandList.unbindItem = (element, _) =>
            {
                element.userData = null;
                element.SetEnabled(true);
                element.RemoveFromClassList("command-child");
                element.RemoveFromClassList("command-group");
                element.RemoveFromClassList("highlight");
            };
            RebuildCommands();
        }
        private bool CommandAvailable(CommandEntry entry, out string reason)
        {
            if (entry.IsGroup) { reason = null; return true; }
            return entry.IsDailyEntry ? AnimalDebugCommandService.ValidateDaily(selected, entry.Daily, out reason)
                : AnimalDebugCommandService.Validate(selected, entry.Command, out reason);
        }
        private void BindCommandRow(Button row, int index)
        {
            CommandEntry entry = commandEntries[index];
            row.userData = entry;
            bool available = CommandAvailable(entry, out string reason);
            row.text = entry.Label + (available ? "" : " — unavailable");
            row.tooltip = available ? "" : reason;
            row.SetEnabled(available);
            row.EnableInClassList("command-child", entry.IsChild);
            row.EnableInClassList("command-group", entry.IsGroup);
            row.EnableInClassList("highlight", index == commandIndex);
        }
        private void RebuildCommands()
        {
            commandEntries.Clear();
            foreach (AnimalDebugCommand parent in AnimalDebugCommandService.Commands)
            {
                bool hasChildren = parent == AnimalDebugCommand.Daily && IsValid(selected) && selected.Config.DailyBehaviours.Count > 0;
                if (!hasChildren) expandedCommands.Remove(parent);
                bool expanded = hasChildren && expandedCommands.Contains(parent);
                commandEntries.Add(new CommandEntry { Command = parent, IsGroup = hasChildren,
                    Label = hasChildren ? (expanded ? "▼ " : "▶ ") + "Daily" : AnimalDebugCommandService.Label(parent) });
                if (!expanded) continue;
                commandEntries.Add(new CommandEntry { Command = parent, IsChild = true, Label = AnimalDebugCommandService.Label(parent) });
                if (parent != AnimalDebugCommand.Daily || !IsValid(selected)) continue;
                for (int i = 0; i < selected.Config.DailyBehaviours.Count; i++)
                {
                    AnimalDailyBehaviourSettings settings = selected.Config.DailyBehaviours[i];
                    commandEntries.Add(new CommandEntry { Command = parent, IsChild = true, IsDailyEntry = true, Daily = settings, DailyIndex = i,
                        Label = AnimalDebugCommandService.DailyLabel(settings, i) });
                }
            }
            commandIndex = Mathf.Clamp(commandIndex, 0, Mathf.Max(0, commandEntries.Count - 1));
            commandList.Rebuild();
        }
        private void RefreshCommandHighlight()
        {
            foreach (Button row in commandList.Query<Button>().ToList())
                if (row.userData is CommandEntry entry)
                    row.EnableInClassList("highlight", commandEntries.IndexOf(entry) == commandIndex);
            RefreshDetails();
        }
        private void ActivateCommand(CommandEntry entry)
        {
            if (entry.IsGroup)
            {
                if (!expandedCommands.Add(entry.Command)) expandedCommands.Remove(entry.Command);
                RebuildCommands();
                commandIndex = commandEntries.FindIndex(item => item.IsGroup && item.Command == entry.Command);
                commandList.RefreshItems(); commandList.ScrollToItem(commandIndex);
                return;
            }
            if (!CommandAvailable(entry, out string reason)) { message.text = "Unavailable: " + reason; return; }
            command = entry.Command;
            selectedDaily = entry.IsDailyEntry ? entry.Daily : null;
            selectedDailyIndex = entry.IsDailyEntry ? entry.DailyIndex : -1;
            result = null; message.text = "";
            SetPopup(null); RefreshDetails();
        }
        private void ResetCommandSelection()
        {
            command = AnimalDebugCommand.Daily; selectedDaily = null; selectedDailyIndex = -1;
            expandedCommands.Clear(); commandIndex = 0;
            if (commandList != null) RebuildCommands();
        }
        private string SelectedCommandLabel => selectedDailyIndex >= 0
            ? AnimalDebugCommandService.DailyLabel(selectedDaily, selectedDailyIndex) : AnimalDebugCommandService.Label(command);
        private bool SelectedCommandAvailable(out string reason) => selectedDailyIndex >= 0
            ? AnimalDebugCommandService.ValidateDaily(selected, selectedDaily, out reason)
            : AnimalDebugCommandService.Validate(selected, command, out reason);

        private bool IsValid(AnimalAgent animal) => AnimalDebugSelection.IsSelectable(animal, gameObject.scene);
        public override void Show()
        {
            open = true;
            panel.style.display = DisplayStyle.Flex;
            RefreshAnimals(); RefreshDetails(); FocusControl(0);
            lastActionFrame = Time.frameCount;
        }

        public override void Hide()
        {
            open = sampling = false;
            message.text = "";
            SetPopup(null);
            panel.style.display = DisplayStyle.None;
            RefreshArrow();
        }

        public void Select(AnimalAgent animal)
        {
            if (!IsValid(animal)) return;
            selected = animal; result = null; sampling = false; message.text = "";
            ResetCommandSelection();
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
            if (show)
            {
                expandedCommands.Clear(); RebuildCommands();
                commandIndex = commandEntries.FindIndex(entry => !entry.IsChild && entry.Command == command);
                commandList.RefreshItems(); commandList.ScrollToItem(commandIndex);
            }
        }
        private void ToggleSampling()
        {
            sampling = !sampling; SetPopup(null); sampleFrame = Time.frameCount; result = null;
            message.text = sampling ? "Click an animal. Esc cancels." : "Sampling cancelled.";
        }
        private void Execute()
        {
            if (lastActionFrame == Time.frameCount) return;
            lastActionFrame = Time.frameCount;
            result = selectedDailyIndex >= 0 ? AnimalDebugCommandService.ExecuteDaily(selected, selectedDaily)
                : AnimalDebugCommandService.Execute(selected, command);
            RefreshDetails();
        }
        public override bool Back()
        {
            if (sampling) { sampling = false; message.text = "Sampling cancelled."; }
            else if (Visible(animalPopup) || Visible(commandPopup)) SetPopup(null);
            else return false;
            return true;
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
                if (!ReferenceEquals(selected, null)) ResetCommandSelection();
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
            if (selectedDailyIndex >= 0 && (!valid || selectedDailyIndex >= selected.Config.DailyBehaviours.Count
                || !ReferenceEquals(selected.Config.DailyBehaviours[selectedDailyIndex], selectedDaily))) ResetCommandSelection();
            commandButton.text = SelectedCommandLabel + "  ▼";
            executeButton.SetEnabled(valid && SelectedCommandAvailable(out _));
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
            string actionName = valid ? selected.Brain.CurrentActionName : null;
            action.text = "Action: " + (valid ? string.IsNullOrEmpty(actionName) ? "no action" : actionName : "—");
            if (result != null && result.Agent == selected)
            {
                bool error = result.Status == AnimalDebugCommandStatus.Rejected || result.Status == AnimalDebugCommandStatus.Failed;
                message.text = (error ? "Error: " : "") + result.Message;
            }
            if (Visible(commandPopup) && commandIndex >= 0 && commandIndex < commandEntries.Count)
            {
                CommandEntry entry = commandEntries[commandIndex];
                message.text = CommandAvailable(entry, out string reason)
                    ? entry.IsGroup ? "Select to expand or collapse this group." : "Select this behaviour, then press Execute."
                    : "Unavailable: " + reason;
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

        public override void HandleInput(int move, bool submit, bool click, Vector2 mouse)
        {
            if (lastActionFrame != Time.frameCount)
            {
                if (Visible(animalPopup))
                {
                    if (move != 0 && animals.Count > 0) { animalIndex = (animalIndex + move + animals.Count) % animals.Count; HighlightAnimal(); }
                    if (submit && animals.Count > 0) Select(animals[animalIndex]);
                }
                else if (Visible(commandPopup))
                {
                    if (move != 0 && commandEntries.Count > 0)
                    {
                        commandIndex = (commandIndex + move + commandEntries.Count) % commandEntries.Count;
                        RefreshCommandHighlight(); commandList.ScrollToItem(commandIndex);
                    }
                    if (submit && commandEntries.Count > 0) ActivateCommand(commandEntries[commandIndex]);
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
            {
                refreshAt = Time.unscaledTime + .1f; RefreshAnimals();
                if (Visible(commandPopup))
                {
                    int entries = IsValid(selected) ? selected.Config.DailyBehaviours.Count : 0;
                    bool changed = false;
                    int shown = 0;
                    foreach (CommandEntry entry in commandEntries)
                    {
                        if (!entry.IsChild && entry.Command == AnimalDebugCommand.Daily)
                            changed |= entry.IsGroup != (entries > 0);
                        if (!entry.IsDailyEntry) continue;
                        shown++;
                        changed |= entry.DailyIndex >= entries || !ReferenceEquals(entry.Daily, selected.Config.DailyBehaviours[entry.DailyIndex])
                            || entry.Label != AnimalDebugCommandService.DailyLabel(entry.Daily, entry.DailyIndex);
                    }
                    if (expandedCommands.Contains(AnimalDebugCommand.Daily) && shown != entries) changed = true;
                    if (changed) RebuildCommands(); else commandList.RefreshItems();
                }
                RefreshDetails();
            }
        }

        private bool PointerOverUI(Vector2 screen)
        {
            Vector2 point = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, Screen.height - screen.y));
            if (root.Q("DebugPanel").worldBound.Contains(point)) return true;
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
