using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.Demos.CheckpointRefit
{
    /// <summary>
    /// Presentation-only maintenance screen. Every module is deliberately unidentified and locked.
    /// It neither reads gameplay input nor changes the player, equipment, or persistent data.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class CheckpointRefitView : MonoBehaviour
    {
        private static readonly string[] SlotNames = { "LEGS", "CAMERA", "ARMS", "BACK", "SENSORS" };
        private readonly Button[] slotButtons = new Button[5];
        private readonly Button[] moduleButtons = new Button[3];
        private readonly List<KeyValuePair<Button, Action>> clickHandlers = new List<KeyValuePair<Button, Action>>();
        private UIDocument document;
        private VisualElement boundRoot;
        private VisualElement screen;
        private VisualElement stage;
        private VisualElement content;
        private VisualElement inspectionModal;
        private Label prompt;
        private Label checkpoint;
        private Label libraryLabel;
        private Label moduleId;
        private Label modalCode;
        private Image playerPreview;
        private Label previewCaption;
        private Button inspectButton;
        private Button inspectionCloseButton;

        public event Action CloseRequested;
        public bool IsVisible { get; private set; }
        public bool IsInspectionOpen { get; private set; }
        public int SelectedSlotIndex { get; private set; } = 2;
        public int SelectedModuleIndex { get; private set; }
        public UIDocument Document => document != null ? document : (document = GetComponent<UIDocument>());

        private void OnEnable()
        {
            if (EnsureBound() && !IsVisible) Hide();
        }

        private void OnDisable()
        {
            Hide();
        }

        private void OnDestroy()
        {
            Unbind();
        }

        public void Show(string checkpointName, string checkpointId)
        {
            if (!EnsureBound()) return;
            checkpoint.text = (string.IsNullOrWhiteSpace(checkpointName) ? "MAINTENANCE CHECKPOINT" : checkpointName.ToUpperInvariant())
                + " / " + (string.IsNullOrWhiteSpace(checkpointId) ? "DEMO-01" : checkpointId.ToUpperInvariant());
            IsVisible = true;
            IsInspectionOpen = false;
            screen.style.display = DisplayStyle.Flex;
            inspectionModal.style.display = DisplayStyle.None;
            content.SetEnabled(true);
            SetPrompt(string.Empty, false);
            RefreshSelection();
            FitStage();
            moduleButtons[SelectedModuleIndex]?.Focus();
        }

        public void Hide()
        {
            IsVisible = false;
            IsInspectionOpen = false;
            if (screen != null) screen.style.display = DisplayStyle.None;
            if (inspectionModal != null) inspectionModal.style.display = DisplayStyle.None;
            if (content != null) content.SetEnabled(true);
            if (prompt != null) prompt.style.display = DisplayStyle.None;
        }

        public void SetPrompt(string text, bool visible)
        {
            if (!EnsureBound() || prompt == null) return;
            prompt.text = text ?? string.Empty;
            prompt.style.display = visible && !IsVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void SetPlayerPreview(Texture texture)
        {
            if (!EnsureBound()) return;
            if (playerPreview != null)
            {
                playerPreview.image = texture;
                playerPreview.scaleMode = ScaleMode.ScaleToFit;
                playerPreview.tintColor = Color.white;
            }
            if (previewCaption != null)
                previewCaption.text = texture != null ? "CURRENT CONFIGURATION" : "PREVIEW UNAVAILABLE";
        }

        public void NavigateSlot(int delta)
        {
            if (!IsVisible || IsInspectionOpen || delta == 0) return;
            SelectSlot(Wrap(SelectedSlotIndex + delta, SlotNames.Length));
            slotButtons[SelectedSlotIndex]?.Focus();
        }

        public void NavigateModule(int delta)
        {
            if (!IsVisible || IsInspectionOpen || delta == 0) return;
            SelectModule(Wrap(SelectedModuleIndex + delta, moduleButtons.Length));
            moduleButtons[SelectedModuleIndex]?.Focus();
        }

        public void OpenInspection()
        {
            if (!IsVisible || IsInspectionOpen || inspectionModal == null) return;
            IsInspectionOpen = true;
            content.SetEnabled(false);
            inspectionModal.style.display = DisplayStyle.Flex;
            inspectionCloseButton?.Focus();
        }

        public bool CloseInspection()
        {
            if (!IsInspectionOpen) return false;
            IsInspectionOpen = false;
            if (inspectionModal != null) inspectionModal.style.display = DisplayStyle.None;
            if (content != null) content.SetEnabled(true);
            inspectButton?.Focus();
            return true;
        }

        private bool EnsureBound()
        {
            var root = Document != null ? Document.rootVisualElement : null;
            if (root == null) return false;
            if (root == boundRoot && screen != null && screen.parent != null) return true;
            Unbind();
            screen = root.Q<VisualElement>("refit-screen");
            if (screen == null) return false;
            boundRoot = root;
            boundRoot.pickingMode = PickingMode.Ignore;
            stage = root.Q<VisualElement>("refit-stage");
            content = root.Q<VisualElement>("refit-content");
            prompt = root.Q<Label>("refit-interaction-prompt");
            inspectionModal = root.Q<VisualElement>("refit-inspection-modal");
            checkpoint = root.Q<Label>("refit-checkpoint");
            libraryLabel = root.Q<Label>("refit-library-label");
            moduleId = root.Q<Label>("refit-module-id");
            modalCode = root.Q<Label>("refit-modal-code");
            playerPreview = root.Q<Image>("refit-player-preview");
            previewCaption = root.Q<Label>("refit-preview-caption");
            inspectButton = root.Q<Button>("refit-inspect");
            inspectionCloseButton = root.Q<Button>("refit-inspection-close");
            for (int i = 0; i < slotButtons.Length; i++)
            {
                int index = i;
                slotButtons[i] = root.Q<Button>("refit-slot-" + i);
                BindClick(slotButtons[i], () => SelectSlot(index));
            }
            for (int i = 0; i < moduleButtons.Length; i++)
            {
                int index = i;
                moduleButtons[i] = root.Q<Button>("refit-module-" + i);
                BindClick(moduleButtons[i], () => SelectModule(index));
            }
            BindClick(inspectButton, OpenInspection);
            BindClick(root.Q<Button>("refit-inspect-footer"), OpenInspection);
            BindClick(inspectionCloseButton, () => CloseInspection());
            BindClick(root.Q<Button>("refit-close"), () => CloseRequested?.Invoke());
            root.Q<Button>("refit-equip")?.SetEnabled(false);
            // The terminal owns directional navigation. Capture at the document root, including
            // events targeting that root after a focus change. Pointer clicks and Tab remain native.
            root.RegisterCallback<NavigationMoveEvent>(SuppressMove, TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationSubmitEvent>(SuppressSubmit, TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationCancelEvent>(SuppressCancel, TrickleDown.TrickleDown);
            root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            screen.style.display = IsVisible ? DisplayStyle.Flex : DisplayStyle.None;
            inspectionModal.style.display = DisplayStyle.None;
            RefreshSelection();
            FitStage();
            return true;
        }

        private void BindClick(Button button, Action action)
        {
            if (button == null) return;
            button.clicked += action;
            clickHandlers.Add(new KeyValuePair<Button, Action>(button, action));
        }

        private void Unbind()
        {
            foreach (var pair in clickHandlers) pair.Key.clicked -= pair.Value;
            clickHandlers.Clear();
            if (boundRoot != null)
            {
                boundRoot.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
                boundRoot.UnregisterCallback<NavigationMoveEvent>(SuppressMove, TrickleDown.TrickleDown);
                boundRoot.UnregisterCallback<NavigationSubmitEvent>(SuppressSubmit, TrickleDown.TrickleDown);
                boundRoot.UnregisterCallback<NavigationCancelEvent>(SuppressCancel, TrickleDown.TrickleDown);
            }
            boundRoot = null;
            screen = null;
        }

        private void SelectSlot(int index)
        {
            if (!IsVisible || IsInspectionOpen) return;
            SelectedSlotIndex = Wrap(index, SlotNames.Length);
            SelectedModuleIndex = 0;
            RefreshSelection();
        }

        private void SelectModule(int index)
        {
            if (!IsVisible || IsInspectionOpen) return;
            SelectedModuleIndex = Wrap(index, moduleButtons.Length);
            RefreshSelection();
        }

        private void RefreshSelection()
        {
            for (int i = 0; i < slotButtons.Length; i++) slotButtons[i]?.EnableInClassList("refit-selected", i == SelectedSlotIndex);
            for (int i = 0; i < moduleButtons.Length; i++) moduleButtons[i]?.EnableInClassList("refit-selected", i == SelectedModuleIndex);
            if (libraryLabel != null) libraryLabel.text = SlotNames[SelectedSlotIndex] + " / UNKNOWN MODULES";
            string code = "UNIDENTIFIED / " + (SelectedModuleIndex + 1).ToString("00");
            if (moduleId != null) moduleId.text = code;
            if (modalCode != null) modalCode.text = code;
        }

        private void OnGeometryChanged(GeometryChangedEvent evt) => FitStage();

        private void FitStage()
        {
            if (boundRoot == null || stage == null) return;
            var size = boundRoot.contentRect.size;
            if (size.x <= 0 || size.y <= 0) return;
            float scale = Mathf.Min(size.x / 1920f, size.y / 1080f);
            stage.style.scale = new Scale(new Vector3(scale, scale, 1));
            stage.style.left = (size.x - 1920f * scale) * .5f;
            stage.style.top = (size.y - 1080f * scale) * .5f;
        }

        private static int Wrap(int value, int length) => (value % length + length) % length;
        private void SuppressMove(NavigationMoveEvent evt)
        {
            if (!IsVisible || evt.direction == NavigationMoveEvent.Direction.Next
                || evt.direction == NavigationMoveEvent.Direction.Previous) return;
            // Stopping callbacks alone still lets the panel focus controller move once more,
            // after NavigateSlot/NavigateModule have already focused the selected button.
            boundRoot?.focusController?.IgnoreEvent(evt);
            evt.StopImmediatePropagation();
        }

        private void SuppressSubmit(NavigationSubmitEvent evt)
        {
            if (IsVisible) evt.StopImmediatePropagation();
        }

        private void SuppressCancel(NavigationCancelEvent evt)
        {
            if (IsVisible) evt.StopImmediatePropagation();
        }
    }
}
