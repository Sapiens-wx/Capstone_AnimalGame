using System;
using System.Collections.Generic;
using System.Linq;
using AnimalGame.Animals.Communication;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.Editor
{
    internal sealed class ContactOptionsWindow : EditorWindow
    {
        private const string ContactsPath = "Assets/Data/Communication/Contacts.asset";
        private const string OptionsFolder = "Assets/Data/Communication/Options";

        private ContactOptionsGraph graph;
        private ContactOptionsHistoryView history;
        private ContactNodeSelection inspectorSelection;
        [SerializeField] private int contactIndex;
        [SerializeField] private bool showHistory;

        [MenuItem("Animal Game/Communication/OptionEditor")]
        private static void Open() => GetWindow<ContactOptionsWindow>("Contact Options");

        public void CreateGUI()
        {
            var contacts = AssetDatabase.LoadAssetAtPath<Contacts>(ContactsPath);
            EnsureOptions(contacts);

            rootVisualElement.Clear();
            rootVisualElement.style.flexDirection = FlexDirection.Row;
            var sidebar = new VisualElement();
            sidebar.style.width = 160;
            sidebar.style.flexShrink = 0;
            rootVisualElement.Add(sidebar);

            var tabs = new VisualElement();
            tabs.style.flexDirection = FlexDirection.Row;
            var contactsTab = new Button { text = "Contacts" };
            var historyTab = new Button { text = "History" };
            contactsTab.style.flexGrow = 1;
            historyTab.style.flexGrow = 1;
            tabs.Add(contactsTab);
            tabs.Add(historyTab);
            sidebar.Add(tabs);
            var contactList = new ScrollView();
            contactList.style.flexGrow = 1;
            sidebar.Add(contactList);
            history = new ContactOptionsHistoryView();
            sidebar.Add(history);
            contactsTab.clicked += () => ShowTab(false);
            historyTab.clicked += () => ShowTab(true);

            graph = new ContactOptionsGraph(this);
            rootVisualElement.Add(graph);
            var buttons = new List<Button>();
            for (int i = 0; i < contacts.contacts.Count; i++)
            {
                int index = i;
                var button = new Button(() => SelectContact(index)) { text = contacts.contacts[i].Name };
                buttons.Add(button);
                contactList.Add(button);
            }

            if (buttons.Count > 0) SelectContact(Mathf.Clamp(contactIndex, 0, buttons.Count - 1));
            ShowTab(showHistory);

            void SelectContact(int index)
            {
                contactIndex = index;
                var contact = contacts.contacts[index];
                graph.Load(contact.options);
                history.Load(contact.Name, contact.options);
                for (int i = 0; i < buttons.Count; i++)
                    buttons[i].SetEnabled(i != index);
            }

            void ShowTab(bool show)
            {
                showHistory = show;
                contactsTab.SetEnabled(show);
                historyTab.SetEnabled(!show);
                contactList.style.display = show ? DisplayStyle.None : DisplayStyle.Flex;
                history.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private static void EnsureOptions(Contacts contacts)
        {
            foreach (var contact in contacts.contacts)
            {
                if (contact.options != null) continue;
                if (!AssetDatabase.IsValidFolder(OptionsFolder))
                    AssetDatabase.CreateFolder("Assets/Data/Communication", "Options");

                string path = $"{OptionsFolder}/{contact.Name}.asset";
                var options = (ContactOptions)AssetDatabase.LoadMainAssetAtPath(path);
                if (options == null)
                {
                    options = CreateInstance<ContactOptions>();
                    AssetDatabase.CreateAsset(options, path);
                }
                contact.options = options;
                EditorUtility.SetDirty(contacts);
                AssetDatabase.SaveAssetIfDirty(contacts);
            }
        }

        private void OnEnable() => Undo.undoRedoPerformed += OnUndoRedo;

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            if (inspectorSelection != null) DestroyImmediate(inspectorSelection);
        }

        private void OnUndoRedo()
        {
            graph?.Rebuild();
            history?.Refresh();
        }

        internal void InspectNode(ContactOptions options, int id)
        {
            if (inspectorSelection == null)
            {
                inspectorSelection = CreateInstance<ContactNodeSelection>();
                inspectorSelection.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
                inspectorSelection.name = "Contact Message";
            }
            inspectorSelection.Window = this;
            inspectorSelection.Select(options, id);
            Selection.activeObject = inspectorSelection;
            EditorUtility.SetDirty(inspectorSelection);
        }

        internal void ClearInspector(int id = 0)
        {
            if (inspectorSelection == null || (id != 0 && inspectorSelection.NodeId != id)) return;
            inspectorSelection.Select(null, 0);
            if (Selection.activeObject == inspectorSelection) Selection.activeObject = null;
        }

        internal void RefreshNode(ContactOptions options, int id) => graph.RefreshNode(options, id);
        internal void RefreshHistory() => history.Refresh();
    }

    internal sealed class ContactOptionsGraph : GraphView
    {
        private static readonly Vector2 DefaultNodeSize = new(148, 77);

        private readonly ContactOptionsWindow window;
        private readonly List<MessageNodeView> views = new();
        private ContactOptions options;
        private MessageEdge pendingEdge;
        private int sourceId;

        public ContactOptionsGraph(ContactOptionsWindow window)
        {
            this.window = window;
            style.flexGrow = 1;
            var background = new GridBackground { pickingMode = PickingMode.Ignore };
            Insert(0, background);
            background.StretchToParentSize();
            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            deleteSelection = (_, _) => DeleteMessages();
            graphViewChanged = SaveMovedNodes;
            RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (pendingEdge != null) pendingEdge.candidatePosition = evt.position;
            });
            RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        }

        public void Load(ContactOptions asset)
        {
            options = asset;
            Rebuild();
            UpdateViewTransform(Vector3.zero, Vector3.one);
            if (views.Count > 0) schedule.Execute(() => FrameAll());
        }

        public void Rebuild()
        {
            CancelConnection();
            ClearSelection();
            window.ClearInspector();
            foreach (var edge in edges.ToList()) RemoveEdge(edge);
            foreach (var view in views) RemoveElement(view);
            views.Clear();
            if (options == null) return;

            for (int id = 1; id <= options.NodeCount; id++) AddNodeView(id);
            foreach (var view in views)
                foreach (int childId in options.GetNode(view.Id).Children)
                    AddEdge(view.Id, childId);
        }

        public void RefreshNode(ContactOptions asset, int id)
        {
            if (asset == options && id <= views.Count) views[id - 1].Refresh(options);
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            if (options == null) return;
            Vector2 pointer = evt.mousePosition;
            var target = evt.target as VisualElement;
            var node = target as MessageNodeView ?? target?.GetFirstAncestorOfType<MessageNodeView>();
            if (node != null)
            {
                evt.menu.AppendAction("Make Connection", _ => StartConnection(node.Id, pointer));
                evt.menu.AppendAction("Set as Entry Node", _ => SetEntry(node.Id),
                    options.EntryNodeId == node.Id ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
                evt.menu.AppendAction("Set as History Entry Node", _ => SetHistoryEntry(node.Id),
                    options.HistoryEntryNodeId == node.Id ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
                return;
            }

            if (target is Edge) return;
            Vector2 position = contentViewContainer.WorldToLocal(pointer);
            evt.menu.AppendAction("New Node/Player", _ => CreateNode(Sender.Player, position));
            evt.menu.AppendAction("New Node/Npc", _ => CreateNode(Sender.Npc, position));
        }

        private void CreateNode(Sender sender, Vector2 position)
        {
            CancelConnection();
            Undo.RecordObject(options, "Create Contact Node");
            int id = options.AddNode(sender, position);
            SaveOptions();
            var view = AddNodeView(id);
            ClearSelection();
            AddToSelection(view);
        }

        private MessageNodeView AddNodeView(int id)
        {
            var node = options.GetNode(id);
            var view = new MessageNodeView(this, id);
            view.Refresh(options);
            view.SetPosition(new Rect(node.Position, Vector2.zero));
            Vector2 size = node.Size == Vector2.zero ? DefaultNodeSize : node.Size;
            view.style.width = size.x;
            view.style.height = size.y;
            views.Add(view);
            AddElement(view);
            view.Q<Resizer>().RegisterCallback<MouseCaptureOutEvent>(_ => SaveResizedNode(view, node));
            return view;
        }

        private void SaveResizedNode(MessageNodeView view, ContactMessageNode node)
        {
            if (options == null || !options.ContainsNode(view.Id)
                || !ReferenceEquals(options.GetNode(view.Id), node)) return;
            Vector2 size = view.CanvasSize;
            if (size == (node.Size == Vector2.zero ? DefaultNodeSize : node.Size)) return;
            Undo.RecordObject(options, "Resize Contact Node");
            options.SetSize(view.Id, size);
            SaveOptions();
        }

        private void SetEntry(int id)
        {
            int oldId = options.EntryNodeId;
            if (id == oldId) return;
            Undo.RecordObject(options, "Set Contact Entry Node");
            options.SetEntryNode(id);
            SaveOptions();
            if (oldId != 0) RefreshNode(options, oldId);
            RefreshNode(options, id);
            window.RefreshHistory();
        }

        private void SetHistoryEntry(int id)
        {
            int oldId = options.HistoryEntryNodeId;
            if (id == oldId && options.EntryNodeId != 0) return;
            Undo.RecordObject(options, "Set Contact History Entry Node");
            options.SetHistoryEntryNode(id);
            SaveOptions();
            if (oldId != 0) RefreshNode(options, oldId);
            RefreshNode(options, id);
            window.RefreshHistory();
        }

        private void StartConnection(int fromId, Vector2 pointer)
        {
            CancelConnection();
            sourceId = fromId;
            pendingEdge = new MessageEdge { output = views[fromId - 1].Output, isGhostEdge = true };
            pendingEdge.pickingMode = PickingMode.Ignore;
            pendingEdge.capabilities = 0;
            pendingEdge.edgeControl.drawToCap = false;
            AddElement(pendingEdge);
            pendingEdge.candidatePosition = pointer;
            Focus();
        }

        private void CancelConnection()
        {
            if (pendingEdge == null) return;
            RemoveEdge(pendingEdge);
            pendingEdge = null;
            sourceId = 0;
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            var target = evt.target as VisualElement;
            var node = target as MessageNodeView ?? target?.GetFirstAncestorOfType<MessageNodeView>();
            if (pendingEdge == null)
            {
                if (node != null && node.selected)
                    window.InspectNode(options, node.Id);
                if (target is MessageEdge edge)
                {
                    if (evt.actionKey && edge.IsSelected(this)) edge.Unselect(this);
                    else edge.Select(this, evt.actionKey);
                    Focus();
                    // Existing edges can be selected here without activating GraphView's endpoint drag.
                    evt.StopImmediatePropagation();
                }
                return;
            }

            if (node == null)
            {
                CancelConnection();
                return;
            }

            evt.StopImmediatePropagation();
            int fromId = sourceId;
            int toId = node.Id;
            if (!options.CanConnect(fromId, toId))
            {
                string reason = fromId == toId ? "A node cannot connect to itself."
                    : "These nodes are already connected in this direction.";
                window.ShowNotification(new GUIContent(reason));
                return;
            }

            CancelConnection();
            Undo.RecordObject(options, "Connect Contact Nodes");
            options.TryConnect(fromId, toId);
            AddEdge(fromId, toId);
            SaveOptions();
            window.RefreshHistory();
            ClearSelection();
            AddToSelection(views[fromId - 1]);
        }

        private void AddEdge(int fromId, int toId)
        {
            var edge = new MessageEdge { output = views[fromId - 1].Output, input = views[toId - 1].Input };
            edge.output.Connect(edge);
            edge.input.Connect(edge);
            AddElement(edge);
        }

        private void RemoveEdge(Edge edge)
        {
            edge.output?.Disconnect(edge);
            edge.input?.Disconnect(edge);
            edge.output = null;
            edge.input = null;
            RemoveElement(edge);
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape && pendingEdge != null)
                CancelConnection();
            else if (evt.keyCode == KeyCode.Delete)
                DeleteMessages();
            else
                return;
            evt.StopImmediatePropagation();
        }

        private void DeleteMessages()
        {
            if (selection.Count == 0) return;
            var nodeIds = selection.OfType<MessageNodeView>().Select(node => node.Id).OrderByDescending(id => id).ToArray();
            var selectedEdges = selection.OfType<MessageEdge>().ToArray();
            Undo.RecordObject(options, "Delete Contact Graph Selection");
            foreach (var edge in selectedEdges)
                options.Disconnect(((MessageNodeView)edge.output.node).Id, ((MessageNodeView)edge.input.node).Id);
            // Descending order keeps the IDs of nodes still scheduled for deletion valid.
            foreach (int id in nodeIds) options.RemoveNode(id);
            SaveOptions();
            Rebuild();
            window.RefreshHistory();
        }

        private GraphViewChange SaveMovedNodes(GraphViewChange change)
        {
            if (change.movedElements == null) return change;
            var moved = change.movedElements.OfType<MessageNodeView>()
                .Where(view => view.CanvasPosition != options.GetNode(view.Id).Position).ToArray();
            if (moved.Length == 0) return change;
            Undo.RecordObject(options, "Move Contact Nodes");
            foreach (var view in moved) options.SetPosition(view.Id, view.CanvasPosition);
            SaveOptions();
            return change;
        }

        private void SaveOptions()
        {
            EditorUtility.SetDirty(options);
            AssetDatabase.SaveAssetIfDirty(options);
        }

        private sealed class MessageNodeView : Node
        {
            public int Id { get; }
            public Port Input { get; }
            public Port Output { get; }
            public Vector2 CanvasPosition => new(style.left.value.value, style.top.value.value);
            public Vector2 CanvasSize => new(style.width.value.value, style.height.value.value);

            private readonly ContactOptionsGraph graph;
            private readonly Label message = new() { pickingMode = PickingMode.Ignore, enableRichText = false };

            public MessageNodeView(ContactOptionsGraph graph, int id)
            {
                this.graph = graph;
                Id = id;
                capabilities &= ~(Capabilities.Collapsible | Capabilities.Snappable);
                capabilities |= Capabilities.Resizable;
                style.minWidth = 64;
                style.minHeight = 32;
                titleContainer.style.display = DisplayStyle.None;
                topContainer.style.display = DisplayStyle.None;
                Input = new MessagePort(Direction.Input, Port.Capacity.Multi);
                Output = new MessagePort(Direction.Output, Port.Capacity.Multi);
                hierarchy.Add(Input);
                hierarchy.Add(Output);
                mainContainer.style.flexGrow = 1;
                message.style.position = Position.Absolute;
                message.style.top = 1;
                message.style.left = 1;
                message.style.right = 1;
                message.style.bottom = 1;
                message.style.overflow = Overflow.Hidden;
                message.style.unityTextAlign = TextAnchor.UpperLeft;
                message.style.whiteSpace = WhiteSpace.Normal;
                message.style.paddingLeft = 8;
                message.style.paddingRight = 8;
                message.style.paddingTop = 6;
                message.style.color = Color.white;
                mainContainer.Add(message);
            }

            public void Refresh(ContactOptions options)
            {
                var node = options.GetNode(Id);
                title = $"#{Id}";
                Color color = Id == options.HistoryEntryNodeId ? new Color32(82, 82, 82, 255)
                    : Id == options.EntryNodeId ? new Color32(166, 77, 31, 255)
                    : node.Sender == Sender.Player ? new Color32(48, 87, 125, 255)
                    : new Color32(70, 100, 61, 255);
                mainContainer.style.backgroundColor = color;
                message.text = string.IsNullOrEmpty(node.Message) ? title : $"{title}  {node.Message}";
                tooltip = message.text;
            }

            // The graph supplies the contact node menu.
            public override void BuildContextualMenu(ContextualMenuPopulateEvent evt) { }

            public override void OnSelected()
            {
                base.OnSelected();
                graph.window.InspectNode(graph.options, Id);
            }

            public override void OnUnselected()
            {
                base.OnUnselected();
                graph.window.ClearInspector(Id);
            }
        }

        private sealed class MessagePort : Port
        {
            public MessagePort(Direction direction, Capacity capacity)
                : base(Orientation.Horizontal, direction, capacity, typeof(ContactMessageNode))
            {
                style.display = DisplayStyle.None;
            }

            public override Vector3 GetGlobalCenter()
            {
                var bounds = node.worldBound;
                bool isOutput = direction == Direction.Output;
                float offset = 6f * GetFirstAncestorOfType<GraphView>().scale;
                return isOutput
                    ? new Vector2(bounds.xMax, bounds.center.y - offset)
                    : new Vector2(bounds.xMin, bounds.center.y + offset);
            }
        }

        private sealed class MessageEdge : Edge
        {
            public MessageEdge() => layer = -1;

            protected override EdgeControl CreateEdgeControl()
            {
                var control = base.CreateEdgeControl();
                control.generateVisualContent += context =>
                {
                    if (input == null) return;
                    Vector2 tip = control.parent.ChangeCoordinatesTo(control, control.to);
                    var painter = context.painter2D;
                    painter.fillColor = control.inputColor;
                    painter.BeginPath();
                    painter.MoveTo(tip);
                    painter.LineTo(tip + new Vector2(-9, -4));
                    painter.LineTo(tip + new Vector2(-9, 4));
                    painter.ClosePath();
                    painter.Fill();
                };
                return control;
            }
        }
    }

    internal sealed class ContactNodeSelection : ScriptableObject
    {
        public ContactOptionsWindow Window { get; set; }
        public ContactOptions Options { get; private set; }
        public int NodeId { get; private set; }
        public event Action Changed;

        public void Select(ContactOptions options, int id)
        {
            if (Options == options && NodeId == id) return;
            Options = options;
            NodeId = id;
            Changed?.Invoke();
        }
    }

    [CustomEditor(typeof(ContactNodeSelection))]
    internal sealed class ContactNodeSelectionEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var selected = (ContactNodeSelection)target;
            var root = new VisualElement();
            var details = new VisualElement();
            root.Add(details);
            var nodeLabel = new Label();
            details.Add(nodeLabel);
            var sender = new EnumField("Sender", Sender.Player);
            details.Add(sender);
            details.Add(new Label("Message"));
            var message = new TextField { multiline = true };
            message.style.minHeight = 72;
            message.style.whiteSpace = WhiteSpace.Normal;
            details.Add(message);
            var children = new VisualElement();
            details.Add(children);

            ContactOptions options = null;
            ContactMessageNode node = null;
            string displayedMessage = "";
            int id = 0;
            UpdateSelection();

            root.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                selected.Changed += UpdateSelection;
                UpdateSelection();
            });
            root.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                selected.Changed -= UpdateSelection;
                CommitMessage();
            });
            message.RegisterCallback<FocusOutEvent>(evt =>
            {
                if (evt.relatedTarget is VisualElement next && message.Contains(next)) return;
                CommitMessage();
            });
            sender.RegisterValueChangedCallback(evt =>
            {
                if (node == null) return;
                Undo.RecordObject(options, "Edit Contact Sender");
                options.SetSender(id, (Sender)evt.newValue);
                SaveAndRefresh();
            });

            return root;

            void UpdateSelection()
            {
                CommitMessage();
                options = selected.Options;
                id = selected.NodeId;
                node = options != null && options.ContainsNode(id) ? options.GetNode(id) : null;
                details.style.display = node == null ? DisplayStyle.None : DisplayStyle.Flex;
                if (node == null) return;

                nodeLabel.text = $"Node #{id}";
                sender.SetValueWithoutNotify(node.Sender);
                displayedMessage = node.Message;
                message.SetValueWithoutNotify(displayedMessage);
                children.Clear();
                children.Add(new Label($"Children ({node.Children.Count})"));
                foreach (int childId in node.Children)
                {
                    var child = options.GetNode(childId);
                    children.Add(new Label($"#{childId}  {child.Sender}  {child.Message}"));
                }
            }

            void CommitMessage()
            {
                if (node == null || message.value == displayedMessage || !options.ContainsNode(id)
                    || !ReferenceEquals(options.GetNode(id), node)) return;
                displayedMessage = message.value;
                if (displayedMessage == node.Message) return;
                Undo.RecordObject(options, "Edit Contact Message");
                options.SetMessage(id, displayedMessage);
                SaveAndRefresh();
            }

            void SaveAndRefresh()
            {
                EditorUtility.SetDirty(options);
                AssetDatabase.SaveAssetIfDirty(options);
                if (selected.Window != null) selected.Window.RefreshNode(options, id);
            }
        }
    }
}
