using System;
using System.Globalization;
using AnimalGame.Animals;
using AnimalGame.MainUI.Inventory;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using UnityEngine;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AnimalGame.MainUI
{
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class MainUI : MonoBehaviour
    {
        [SerializeField] private UIDocument document;
        [SerializeField] private VisualTreeAsset emptyItemTemplate;
        [Header("Status (normalized)")]
        [SerializeField, Range(0, 1)] private float power = 1;
        [SerializeField, Range(0, 1)] private float integrity = 1;
        [Header("Inventory animation")]
        [SerializeField, Min(.05f)] private float animationDuration = .5f;
        [SerializeField] private AnimationCurve easing = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [Tooltip("Additional status movement when inventory opens, relative to its authored position and the ring movement.")]
        [SerializeField] private Vector2 statusInventoryDisplacement = new Vector2(400, 51);
        [SerializeField, Min(.1f)] private float gamepadOpenHoldSeconds = .4f;

        private const float DesignWidth = 1920, DesignHeight = 1080;
        private const int SlotCount = 8;
        public static MainUI Active { get; private set; }
        public static bool BlocksGameplay => Active != null && Active.isActiveAndEnabled && Active.pauseOwned;
        public bool IsInventoryOpen => inventoryOpen;
        public float InventoryProgress => progress;
        public int SelectedIndex => selectedIndex;
        public float Power { get => power; set => power = Mathf.Clamp01(value); }
        public float Integrity { get => integrity; set => integrity = Mathf.Clamp01(value); }
        public InventoryItem[] Items { get; } = new InventoryItem[SlotCount];

        private VisualElement root, stage, view, ring, status, time, inventory, grid, entry, compassHost, coordinates;
        private ViewRingElement innerRing, outerRing;
        private CircularProgressElement powerRing, integrityRing;
        private CompassElement compass;
        private HudFrameElement frame;
        private TimeOrbitElement timeOrbit;
        private InventorySelectionElement selection;
        private Label powerLabel, integrityLabel, dateLabel, latitudeLabel, longitudeLabel, altitudeLabel;
        private readonly Button[] slots = new Button[SlotCount];
        private bool started, inventoryOpen, pauseOwned, originalCursor, downLatched, hasGeometry;
        private CursorLockMode originalCursorLock;
        private float progress, savedTimeScale, downElapsed, fit = 1, photoScale = 1, photoAlpha = 1;
        private float lastPower = -1, lastIntegrity = -1;
        private int selectedIndex;
        private IDisposable animalPause;
        private Vector2 viewportOffset, ringDisplacement, photoOffset, cachedCenter;
        private float cachedRadius;
        private DateTime displayedDate = new DateTime(2026, 1, 1);

        private void Awake()
        {
            if (document == null) document = GetComponent<UIDocument>();
        }
        private void OnEnable() => Active = this;
        private void Start() { started = true; Init(); }

        public void Init()
        {
            if (document == null) document = GetComponent<UIDocument>();
            VisualElement nextRoot = document != null ? document.rootVisualElement : null;
            if (nextRoot == null) return;
            root = nextRoot;
            stage = root.Q("Stage");
            view = root.Q("HudView");
            ring = root.Q("ViewRing");
            innerRing = root.Q<ViewRingElement>("InnerRing");
            outerRing = root.Q<ViewRingElement>("OuterRing");
            status = root.Q("Status");
            time = root.Q("Time");
            inventory = root.Q("Inventory");
            grid = root.Q("InventoryGrid");
            entry = root.Q("InventoryEntry");
            selection = root.Q<InventorySelectionElement>("InventorySelection");
            frame = root.Q<HudFrameElement>("Frame");
            compass = root.Q<CompassElement>("Compass");
            compassHost = root.Q("CompassHost");
            timeOrbit = root.Q<TimeOrbitElement>("TimeOrbit");
            coordinates = root.Q("Coordinates");
            powerRing = root.Q<CircularProgressElement>("PowerRing");
            integrityRing = root.Q<CircularProgressElement>("IntegrityRing");
            powerLabel = root.Q<Label>("PowerPercent");
            integrityLabel = root.Q<Label>("IntegrityPercent");
            dateLabel = root.Q<Label>("Date");
            latitudeLabel = root.Q<Label>("Latitude");
            longitudeLabel = root.Q<Label>("Longitude");
            altitudeLabel = root.Q<Label>("Altitude");
            if (stage == null || view == null || innerRing == null || grid == null) return;

            root.pickingMode = PickingMode.Ignore;
            foreach (VisualElement element in root.Query<VisualElement>().ToList())
                element.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = root.style.top = root.style.right = root.style.bottom = 0;
            root.style.overflow = Overflow.Hidden;
            if (emptyItemTemplate == null)
                emptyItemTemplate = Resources.Load<VisualTreeAsset>("UI/Main/EmptyInventoryItem");
            BuildSlots();
            SetDate(displayedDate);
            lastPower = lastIntegrity = -1;
            ApplyLayout();
            UpdateStatus();
        }

        private void BuildSlots()
        {
            grid.Clear();
            for (int i = 0; i < SlotCount; i++)
            {
                int index = i;
                var button = new Button(() => SelectItem(index)) { name = "InventorySlot" + (i + 1), userData = Items[i] };
                button.AddToClassList("inventory-slot");
                button.style.position = Position.Absolute;
                button.style.left = (i % 2) * 240;
                button.style.top = (i / 2) * 170;
                button.RegisterCallback<PointerEnterEvent>(_ => { if (inventoryOpen) SelectItem(index); });
                button.RegisterCallback<FocusInEvent>(_ => { if (inventoryOpen) SelectItem(index); });
                // Navigation is owned by MainUI so each input moves the shared selection exactly once.
                button.RegisterCallback<NavigationMoveEvent>(evt => evt.StopImmediatePropagation());
                grid.Add(button);
                slots[i] = button;
                RefreshItem(i);
            }
            SelectItem(selectedIndex);
        }

        public void SetItem(int index, InventoryItem item)
        {
            if (index < 0 || index >= SlotCount) throw new ArgumentOutOfRangeException(nameof(index));
            Items[index] = item;
            RefreshItem(index);
        }

        private void RefreshItem(int index)
        {
            Button button = slots[index];
            if (button == null) return;
            button.Clear();
            button.userData = Items[index];
            if (Items[index] != null) return; // Future item presentation belongs here.
            if (emptyItemTemplate != null)
            {
                TemplateContainer empty = emptyItemTemplate.CloneTree();
                empty.AddToClassList("empty-item");
                empty.pickingMode = PickingMode.Ignore;
                button.Add(empty);
            }
            else button.Add(new EmptyInventoryElement { style = { flexGrow = 1 } });
        }

        public void SelectItem(int index)
        {
            selectedIndex = Mathf.Clamp(index, 0, SlotCount - 1);
            if (selection == null) return;
            if (slots[selectedIndex] == null || !Positive(grid.layout.width)) return;
            Vector2 offset = grid.layout.position + slots[selectedIndex].layout.position - selection.layout.position;
            selection.style.translate = new Translate(offset.x, offset.y);
        }

        public void ToggleInventory() { if (inventoryOpen) CloseInventory(); else OpenInventory(); }
        public void OpenInventory()
        {
            if (!isActiveAndEnabled || stage == null || !TryGetRingScreenGeometry(out _, out _)) return;
            HeightMapPlayerSceneBootstrap bootstrap = HeightMapPlayerSceneBootstrap.inst;
            if (bootstrap != null && bootstrap.photoMode != null && bootstrap.photoMode.IsActive) return;
            if (bootstrap != null && bootstrap.photoResultView != null && bootstrap.photoResultView.IsShowing) return;
            if (inventoryOpen) return;
            inventoryOpen = true;
            AcquirePause();
        }
        public void CloseInventory() => inventoryOpen = false;

        private void AcquirePause()
        {
            if (pauseOwned) return;
            pauseOwned = true;
            savedTimeScale = Time.timeScale;
            Time.timeScale = 0;
            animalPause = AnimalSimulation.AcquirePause();
            originalCursor = Cursor.visible;
            originalCursorLock = Cursor.lockState;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void ReleasePause()
        {
            if (!pauseOwned) return;
            pauseOwned = false;
            // Do not overwrite a time scale explicitly changed by another owner while the panel was open.
            if (Time.timeScale == 0) Time.timeScale = savedTimeScale;
            animalPause?.Dispose(); animalPause = null;
            Cursor.visible = originalCursor;
            Cursor.lockState = originalCursorLock;
        }

        private void Update()
        {
            if (!started || document == null) return;
            if (root != document.rootVisualElement || stage == null) Init();
            if (stage == null) return;
            HandleInput();
            AdvanceInventoryAnimation(Time.unscaledDeltaTime);
        }

        private void AdvanceInventoryAnimation(float deltaTime)
        {
            progress = Mathf.MoveTowards(progress, inventoryOpen ? 1 : 0,
                Mathf.Max(0, deltaTime) / Mathf.Max(.05f, animationDuration));
            ApplyLayout();
            UpdateStatus();
            TryGetRingScreenGeometry(out _, out _);
            if (!inventoryOpen && progress == 0) ReleasePause();
        }

        private float Ease(float value) => Mathf.Clamp01(easing == null ? value : easing.Evaluate(Mathf.Clamp01(value)));

        private void ApplyLayout()
        {
            if (root == null || view == null || root.panel == null) return;
            Vector2 size = root.layout.size;
            if (!Positive(size.x) || !Positive(size.y)) return;
            fit = Mathf.Min(size.x / DesignWidth, size.y / DesignHeight);
            viewportOffset = (size - new Vector2(DesignWidth, DesignHeight) * fit) * .5f;
            // Keep authored layout coordinates intact; apply screen fitting through transforms only.
            Vector2 viewOffset = viewportOffset + view.layout.position * (fit - 1);
            view.style.translate = new Translate(viewOffset.x, viewOffset.y);
            view.style.scale = new Scale(new Vector3(fit, fit, 1));

            float vertical = Ease(progress * 2), horizontal = Ease(progress * 2 - 1);
            float openCircleX = (size.x * (2f / 3) - viewportOffset.x) / fit;
            if (!Positive(ring.layout.width) || !Positive(inventory.layout.width)) return;
            float authoredRingX = view.layout.x + ring.layout.x + innerRing.layout.x + innerRing.contentRect.center.x;
            ringDisplacement = new Vector2((openCircleX - authoredRingX) * horizontal, 0);
            ring.style.translate = new Translate(ringDisplacement.x, ringDisplacement.y);
            // Scale each element around its own screen corner, including its authored edge offsets.
            Vector2 statusOffset = FitScreenElement(status, false, false);
            FitScreenElement(time, true, false);
            FitScreenElement(entry, false, true);
            FitScreenElement(coordinates, true, true);
            status.style.translate = new Translate(statusOffset.x
                + (ringDisplacement.x + statusInventoryDisplacement.x * horizontal) * fit,
                statusOffset.y + statusInventoryDisplacement.y * vertical * fit);
            Vector2 targetCenter = new Vector2((size.x * .25f - viewportOffset.x) / fit, DesignHeight * .5f);
            Vector2 inventoryDelta = targetCenter - view.layout.position - inventory.layout.center;
            inventory.style.translate = new Translate(inventoryDelta.x * horizontal, inventoryDelta.y * vertical);
            bool visible = inventoryOpen || progress > 0;
            inventory.style.visibility = visible ? Visibility.Visible : Visibility.Hidden;
            grid.SetEnabled(inventoryOpen && progress >= .98f);
            foreach (Button slot in slots) if (slot != null) slot.pickingMode = inventoryOpen ? PickingMode.Position : PickingMode.Ignore;
            entry.style.visibility = visible ? Visibility.Hidden : Visibility.Visible;
            coordinates.style.opacity = 1;
            frame.MarkDirtyRepaint();
            // Position and centering belong to UXML/USS; only size follows the screen fit.
            compassHost.style.scale = new Scale(new Vector3(fit, fit, 1));
            SelectItem(selectedIndex);
            ApplyPhotoTransform();
        }

        private Vector2 FitScreenElement(VisualElement element, bool right, bool bottom)
        {
            Rect layout = element.layout;
            Vector2 parentSize = element.parent.layout.size;
            // Layout is unaffected by transforms, so this also preserves edits made in UI Builder.
            Vector2 edgeOffset = new Vector2(right ? layout.xMax - parentSize.x : layout.x,
                bottom ? layout.yMax - parentSize.y : layout.y);
            Vector2 correction = edgeOffset * (fit - 1);
            element.style.scale = new Scale(new Vector3(fit, fit, 1));
            element.style.translate = new Translate(correction.x, correction.y);
            return correction;
        }

        private void UpdateStatus()
        {
            if (powerRing == null) return;
            if (lastPower != power)
            {
                powerRing.SetProgress(power); powerLabel.text = Mathf.RoundToInt(powerRing.progress * 100) + "%";
                lastPower = power;
            }
            if (lastIntegrity != integrity)
            {
                integrityRing.SetProgress(integrity); integrityLabel.text = Mathf.RoundToInt(integrityRing.progress * 100) + "%";
                lastIntegrity = integrity;
            }
            HeightMapPlayerSceneBootstrap bootstrap = HeightMapPlayerSceneBootstrap.inst;
            if (bootstrap != null && bootstrap.robot != null)
            {
                Vector2 forward = bootstrap.robot.Forward;
                compass.SetHeading(Mathf.Atan2(forward.x, forward.y) * Mathf.Rad2Deg);
            }
        }

        public void RefreshLatePresentation()
        {
            if (outerRing == null) return;
            // Tumble rotates only the outer ring, leaving the inner boundary and the rest of the HUD upright.
            outerRing.style.rotate = new Rotate(-RobotTumbleUiRotation.ActiveRotationDegrees);
        }

        public bool TryGetRingScreenGeometry(out Vector2 center, out float radius)
        {
            if (root != null && root.panel != null && document != null && document.isActiveAndEnabled
                && innerRing != null && root.resolvedStyle.display != DisplayStyle.None
                && stage != null && stage.resolvedStyle.visibility == Visibility.Visible && photoAlpha > 0
                && Positive(innerRing.Radius) && Positive(root.layout.width) && Positive(root.layout.height))
            {
                // Use the current animation pose rather than last frame's deferred transform-style resolution.
                Vector2 authoredCenter = view.layout.position + ring.layout.position + innerRing.layout.position + innerRing.contentRect.center;
                Vector2 panelPoint = viewportOffset + (authoredCenter + ringDisplacement) * fit;
                Vector2 panelCenter = stage.layout.size * .5f;
                panelPoint = panelCenter + (panelPoint - panelCenter) * photoScale + photoOffset * fit;
                panelPoint += stage.layout.position;
                Vector2 scale = PanelUnitsPerScreenPixel();
                Vector2 origin = RuntimePanelUtils.ScreenToPanel(root.panel, Vector2.zero);
                Vector2 panelRoot = root.LocalToWorld(panelPoint);
                cachedCenter = new Vector2((panelRoot.x - origin.x) / scale.x,
                    Screen.height - (panelRoot.y - origin.y) / scale.y);
                cachedRadius = innerRing.Radius * fit * photoScale / Mathf.Max(scale.x, scale.y);
                hasGeometry = Positive(cachedRadius);
            }
            center = cachedCenter; radius = cachedRadius;
            return hasGeometry;
        }

        private Vector2 PanelUnitsPerScreenPixel()
        {
            Vector2 a = RuntimePanelUtils.ScreenToPanel(root.panel, Vector2.zero);
            Vector2 b = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(Screen.width, Screen.height));
            return new Vector2(Mathf.Max(.0001f, (b.x - a.x) / Mathf.Max(1, Screen.width)),
                Mathf.Max(.0001f, (b.y - a.y) / Mathf.Max(1, Screen.height)));
        }

        public void SetPhotoPose(float scale, Vector2 design, Vector2 displacement, float alpha)
        {
            photoScale = Mathf.Max(.01f, scale);
            photoOffset = new Vector2(displacement.x * DesignWidth / Mathf.Max(1, design.x),
                displacement.y * DesignHeight / Mathf.Max(1, design.y));
            photoAlpha = Mathf.Clamp01(alpha);
            ApplyPhotoTransform();
        }

        private void ApplyPhotoTransform()
        {
            if (stage == null) return;
            stage.style.scale = new Scale(new Vector3(photoScale, photoScale, 1));
            stage.style.translate = new Translate(photoOffset.x * fit, photoOffset.y * fit);
            stage.style.opacity = photoAlpha;
        }

        public void SetHeading(float degrees) => compass?.SetHeading(degrees);
        public void SetDayProgress(float normalized) => timeOrbit?.SetDayProgress(normalized);
        public void SetDate(DateTime date)
        {
            displayedDate = date;
            if (dateLabel != null) dateLabel.text = date.ToString("yyyy_MM_dd", CultureInfo.InvariantCulture);
        }
        // TODO: Bind these display interfaces to the future map coordinate conversion and altitude data source.
        public void SetCoordinates(float latitude, float longitude)
        {
            if (latitudeLabel != null) latitudeLabel.text = Mathf.Abs(latitude).ToString("F5", CultureInfo.InvariantCulture) + "°";
            if (longitudeLabel != null) longitudeLabel.text = Mathf.Abs(longitude).ToString("F5", CultureInfo.InvariantCulture) + "°";
            if (root != null)
            {
                root.Q<Label>("LatitudeHemisphere").text = latitude < 0 ? "S" : "N";
                root.Q<Label>("LongitudeHemisphere").text = longitude < 0 ? "W" : "E";
            }
        }
        public void SetAltitude(float meters) { if (altitudeLabel != null) altitudeLabel.text = meters.ToString("F0") + "m"; }

        private static bool Positive(float x) => x > 0 && !float.IsNaN(x) && !float.IsInfinity(x);

        private void HandleInput()
        {
            bool toggle = false, close = false, heldDown = false;
            Vector2Int navigation = Vector2Int.zero;
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                toggle = Keyboard.current.nKey.wasPressedThisFrame;
                if (Keyboard.current.leftArrowKey.wasPressedThisFrame) navigation.x = -1;
                if (Keyboard.current.rightArrowKey.wasPressedThisFrame) navigation.x = 1;
                if (Keyboard.current.upArrowKey.wasPressedThisFrame) navigation.y = -1;
                if (Keyboard.current.downArrowKey.wasPressedThisFrame) navigation.y = 1;
            }
            foreach (Gamepad pad in Gamepad.all)
            {
                if (!pad.added) continue;
                heldDown |= pad.dpad.down.isPressed;
                close |= pad.buttonEast.wasPressedThisFrame;
                if (pad.dpad.left.wasPressedThisFrame) navigation.x = -1;
                if (pad.dpad.right.wasPressedThisFrame) navigation.x = 1;
                if (pad.dpad.up.wasPressedThisFrame) navigation.y = -1;
                if (pad.dpad.down.wasPressedThisFrame) navigation.y = 1;
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
#endif
            if (toggle) ToggleInventory();
            if (close && inventoryOpen) CloseInventory();
            if (!heldDown) { downElapsed = 0; downLatched = false; }
            else if (!inventoryOpen && !downLatched)
            {
                downElapsed += Time.unscaledDeltaTime;
                if (downElapsed >= gamepadOpenHoldSeconds) { downLatched = true; OpenInventory(); }
            }
            if (!inventoryOpen || progress < .98f || navigation == Vector2Int.zero) return;
            int column = Mathf.Clamp(selectedIndex % 2 + navigation.x, 0, 1);
            int row = Mathf.Clamp(selectedIndex / 2 + navigation.y, 0, 3);
            SelectItem(row * 2 + column);
        }

        private void OnDisable()
        {
            inventoryOpen = false; progress = 0;
            ReleasePause();
            if (Active == this) Active = null;
        }
    }
}
