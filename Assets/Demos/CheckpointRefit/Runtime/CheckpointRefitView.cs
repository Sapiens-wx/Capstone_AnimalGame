using System;
using System.Collections.Generic;
using AnimalGame.Animals;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.Demos.CheckpointRefit
{
    /// <summary>
    /// Maintenance presentation that emits equipment requests for the terminal to handle.
    /// Gameplay input and changes to the player's equipment remain outside the view.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class CheckpointRefitView : MonoBehaviour
    {
        private static readonly string[] SlotNames = { "LEGS", "CAMERA", "BACK", "EXPANSION" };
        private readonly Button[] slotButtons = new Button[4];
        private readonly Button[] moduleButtons = new Button[3];
        private readonly PhotoCameraDefinition[] cameraModules = new PhotoCameraDefinition[3];
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
        private Label detailTitle;
        private Label descriptionText;
        private Label equipHint;
        private Label modalTitle;
        private Label modalCaption;
        private Label photoExpand;
        private Label noData;
        private Label modalNoData;
        private RefitModulePreviewElement inspectionPreview;
        private RefitModulePreviewElement modalPreview;
        private Image playerPreview;
        private Label previewCaption;
        private Button inspectButton;
        private Button inspectFooterButton;
        private Button equipButton;
        private Button equipFooterButton;
        private Button inspectionCloseButton;
        private RefitCameraEquipment cameraEquipment;
        private PhotoCameraDefinition zoomLens300;
        private PhotoCameraDefinition damagedPrime50;
        private PhotoCameraDefinition displayedInstalledCamera;

        public event Action CloseRequested;
        public event Action EquipRequested;
        public bool IsVisible { get; private set; }
        public bool IsInspectionOpen { get; private set; }
        public int SelectedSlotIndex { get; private set; }
        public int SelectedModuleIndex { get; private set; }
        public int SlotCount => SlotNames.Length;
        public int ModuleCount => moduleButtons.Length;
        public bool IsSelectedModuleAvailable => IsModuleAvailable(SelectedSlotIndex, SelectedModuleIndex);
        public bool IsSelectedModuleInstalled => IsModuleInstalled(SelectedSlotIndex, SelectedModuleIndex);
        public PhotoCameraDefinition SelectedCamera => SelectedSlotIndex == 1 ? GetCameraModule(SelectedModuleIndex) : null;
        public UIDocument Document => document != null ? document : (document = GetComponent<UIDocument>());

        public void BindCameraEquipment(RefitCameraEquipment equipment, PhotoCameraDefinition zoomCamera, PhotoCameraDefinition damagedCamera)
        {
            cameraEquipment = equipment;
            zoomLens300 = zoomCamera;
            damagedPrime50 = damagedCamera;
            RefreshSelection();
        }

        public PhotoCameraDefinition GetCameraModule(int index)
            => index >= 0 && index < cameraModules.Length ? cameraModules[index] : null;

        public bool IsModuleAvailable(int slotIndex, int moduleIndex)
        {
            if (slotIndex == 0) return moduleIndex == 0;
            if (slotIndex != 1) return false;
            var camera = GetCameraModule(moduleIndex);
            return camera != null && (camera == zoomLens300 || (cameraEquipment != null && cameraEquipment.IsUnlocked(camera)));
        }

        public bool IsModuleInstalled(int slotIndex, int moduleIndex)
        {
            if (slotIndex == 0) return moduleIndex == 0;
            return slotIndex == 1 && cameraEquipment != null && cameraEquipment.IsInstalled(GetCameraModule(moduleIndex));
        }

        public void RequestEquipSelected()
        {
            if (!IsVisible || IsInspectionOpen || !CanEquipSelection) return;
            EquipRequested?.Invoke();
        }

        public void RefreshEquipment() => RefreshSelection();

        private bool CanEquipSelection => SelectedSlotIndex == 1 && IsSelectedModuleAvailable
            && !IsSelectedModuleInstalled && cameraEquipment != null && cameraEquipment.IsAvailable;

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

        private void LateUpdate()
        {
            if (!IsVisible) return;
            var installed = cameraEquipment != null ? cameraEquipment.CurrentCamera : null;
            if (installed != displayedInstalledCamera) RefreshEquipment();
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
            if (!IsVisible || IsInspectionOpen || inspectionModal == null || !IsSelectedModuleAvailable) return;
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
            detailTitle = root.Q<Label>("refit-detail-title");
            descriptionText = root.Q<Label>("refit-description-text");
            equipHint = root.Q<Label>("refit-equip-hint");
            modalTitle = root.Q<Label>("refit-modal-title");
            modalCaption = root.Q<Label>("refit-modal-caption");
            photoExpand = root.Q<Label>("refit-photo-expand");
            noData = root.Q<Label>("refit-no-data");
            modalNoData = root.Q<Label>("refit-modal-no-data");
            inspectionPreview = root.Q<RefitModulePreviewElement>("refit-inspection-preview");
            modalPreview = root.Q<RefitModulePreviewElement>("refit-modal-preview");
            playerPreview = root.Q<Image>("refit-player-preview");
            previewCaption = root.Q<Label>("refit-preview-caption");
            inspectButton = root.Q<Button>("refit-inspect");
            inspectFooterButton = root.Q<Button>("refit-inspect-footer");
            equipButton = root.Q<Button>("refit-equip");
            equipFooterButton = root.Q<Button>("refit-equip-footer");
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
            BindClick(inspectFooterButton, OpenInspection);
            BindClick(inspectionCloseButton, () => CloseInspection());
            BindClick(root.Q<Button>("refit-close"), () => CloseRequested?.Invoke());
            BindClick(equipButton, RequestEquipSelected);
            BindClick(equipFooterButton, RequestEquipSelected);
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
            RefreshCameraModuleOrder();
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
            bool selectionMoved = RefreshCameraModuleOrder();
            for (int i = 0; i < slotButtons.Length; i++) slotButtons[i]?.EnableInClassList("refit-selected", i == SelectedSlotIndex);
            for (int i = 0; i < moduleButtons.Length; i++)
            {
                var button = moduleButtons[i];
                if (button == null) continue;
                bool available = IsModuleAvailable(SelectedSlotIndex, i);
                bool installed = IsModuleInstalled(SelectedSlotIndex, i);
                string name = SelectedSlotIndex == 0 ? "TRACKS" : GetCameraModule(i)?.DisplayName;
                bool longName = available && name != null && name.Length > 22;
                button.EnableInClassList("refit-selected", i == SelectedModuleIndex);
                button.EnableInClassList("refit-module-unavailable", !available);
                button.EnableInClassList("refit-module-long-name", longName);
                button.tooltip = available ? name + (installed ? " / Installed" : " / Available") : "Module unavailable";
                SetCardLabel(button, "refit-module-code", (i + 1).ToString("00") + (SelectedSlotIndex == 0 ? " / INITIAL" : " / CAMERA"), available);
                SetCardLabel(button, "refit-module-name", name, available);
                SetCardLabel(button, "refit-module-state", installed ? "INSTALLED" : "AVAILABLE", available);
                var slash = button.Q<VisualElement>(className: "refit-module-slash");
                if (slash != null) slash.style.display = available ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (libraryLabel != null) libraryLabel.text = SlotNames[SelectedSlotIndex] + " / MODULES";
            bool selectedAvailable = IsSelectedModuleAvailable;
            bool selectedInstalled = IsSelectedModuleInstalled;
            bool selectedDamagedCamera = SelectedSlotIndex == 1 && SelectedCamera != null && SelectedCamera == damagedPrime50;
            string kind = selectedAvailable ? (SelectedSlotIndex == 0 ? "tracks" : "camera") : "unavailable";
            string title = selectedAvailable ? (SelectedSlotIndex == 0 ? "TRACKS" : SelectedCamera.DisplayName) : "NO MODULE DATA";
            string description = selectedAvailable
                ? (SelectedSlotIndex == 0
                    ? "Standard tracked undercarriage. Installed as the unit's initial mobility assembly."
                    : (selectedDamagedCamera
                        ? "Damaged 50 mm prime camera. Black-and-white photographs with severe blur and heavy grain."
                        : "Variable-zoom lens in good condition. Captures the unit's original photographic finish."))
                : "No module data available.";
            string code = SlotNames[SelectedSlotIndex] + (selectedAvailable ? (selectedInstalled ? " / INSTALLED" : " / AVAILABLE") : " / \u2014");
            if (moduleId != null) moduleId.text = code;
            if (modalCode != null) modalCode.text = code;
            if (detailTitle != null)
            {
                detailTitle.text = title;
                detailTitle.EnableInClassList("refit-detail-title-long", title.Length > 22);
            }
            if (modalTitle != null)
            {
                modalTitle.text = title;
                modalTitle.EnableInClassList("refit-modal-title-long", title.Length > 22);
            }
            if (descriptionText != null) descriptionText.text = description;
            if (modalCaption != null) modalCaption.text = description;
            if (equipHint != null) equipHint.text = selectedAvailable
                ? (SelectedSlotIndex == 0 ? "INITIAL CONFIGURATION" : (selectedDamagedCamera ? "CONDITION / DAMAGED" : "CONDITION / GOOD"))
                : "\u2014";
            if (equipButton != null)
            {
                equipButton.text = selectedAvailable ? (selectedInstalled ? "INSTALLED" : "A / ENTER   EQUIP") : "/";
                equipButton.tooltip = selectedAvailable ? (selectedInstalled ? "Currently installed" : "Equip selected camera") : "Module unavailable";
                equipButton.EnableInClassList("refit-equip-unavailable", !selectedAvailable);
                equipButton.SetEnabled(CanEquipSelection);
            }
            if (equipFooterButton != null)
            {
                equipFooterButton.SetEnabled(CanEquipSelection);
                equipFooterButton.style.display = CanEquipSelection ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (inspectionPreview != null) inspectionPreview.ModuleKind = kind;
            if (modalPreview != null) modalPreview.ModuleKind = kind;
            inspectButton?.SetEnabled(selectedAvailable);
            inspectFooterButton?.SetEnabled(selectedAvailable);
            if (photoExpand != null) photoExpand.style.display = selectedAvailable ? DisplayStyle.Flex : DisplayStyle.None;
            if (noData != null) noData.style.display = selectedAvailable ? DisplayStyle.None : DisplayStyle.Flex;
            if (modalNoData != null) modalNoData.style.display = selectedAvailable ? DisplayStyle.None : DisplayStyle.Flex;
            if (selectionMoved && IsVisible && !IsInspectionOpen) moduleButtons[SelectedModuleIndex]?.Focus();
        }

        private bool RefreshCameraModuleOrder()
        {
            var selected = SelectedCamera;
            int previousIndex = SelectedModuleIndex;
            cameraModules[0] = zoomLens300;
            cameraModules[1] = damagedPrime50;
            cameraModules[2] = null;
            displayedInstalledCamera = cameraEquipment != null ? cameraEquipment.CurrentCamera : null;
            // Move the installed definition to the front without changing the
            // relative order of the other modules or the identity of the selection.
            for (int i = 1; i < cameraModules.Length; i++)
            {
                if (cameraModules[i] == null || cameraModules[i] != displayedInstalledCamera) continue;
                var installed = cameraModules[i];
                for (int j = i; j > 0; j--) cameraModules[j] = cameraModules[j - 1];
                cameraModules[0] = installed;
                break;
            }
            if (selected != null)
            {
                int nextIndex = Array.IndexOf(cameraModules, selected);
                SelectedModuleIndex = nextIndex >= 0 ? nextIndex : 0;
            }
            return SelectedModuleIndex != previousIndex;
        }

        private static void SetCardLabel(Button button, string className, string text, bool visible)
        {
            var label = button.Q<Label>(className: className);
            if (label == null) return;
            label.text = visible ? text : string.Empty;
            label.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
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
