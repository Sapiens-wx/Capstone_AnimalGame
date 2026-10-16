using System;
using System.Collections.Generic;
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
        [Header("Initial inventory")]
        [Tooltip("Initial slot contents, in order. Only the first eight entries are used; null entries leave empty slots.")]
        [SerializeField] private List<InventoryItem> defaultItems = new List<InventoryItem>();
        [Header("Status (normalized)")]
        [SerializeField, Range(0, 1)] private float power = 1;
        [SerializeField, Range(0, 1)] private float integrity = 1;
        [Header("Level indicator")]
        [Header("Inventory animation")]
        [SerializeField, Min(.05f)] private float animationDuration = .5f;
        [SerializeField] private AnimationCurve easing = AnimationCurve.EaseInOut(0, 0, 1, 1);
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

        private VisualElement root, stage, view, ring, status, time, sound, inventory, grid, compassHost, coordinates;
        private ViewRingElement innerRing, outerRing;
        private CircularProgressElement powerRing, integrityRing;
        private CompassElement compass;
        private LevelIndicatorElement levelIndicator;
        private HudFrameElement frame;
        private TimeOrbitElement timeOrbit;
        private InventorySelectionElement selection;
        private Label powerLabel, integrityLabel, dateLabel, latitudeLabel, longitudeLabel, altitudeLabel;
        private Label latitudeHemisphereLabel, longitudeHemisphereLabel;
        private readonly Button[] slots = new Button[SlotCount];
        private bool started, inventoryOpen, pauseOwned, originalCursor, openHoldLatched, hasGeometry;
        private bool defaultItemsLoaded;
        private CursorLockMode originalCursorLock;
        private float progress, savedTimeScale, openHoldElapsed, fit = 1, photoScale = 1, photoAlpha = 1;
        private float lastPower = -1, lastIntegrity = -1;
        private float headingDegrees;
        private float outerRingHeadingDegrees;
        private int selectedIndex;
        private Vector2 viewportOffset, ringDisplacement, photoOffset, cachedCenter;
        private float cachedRadius;
        private DateTime lastCoordinateRefresh, gameTime;
        private double elapsedGameClockSeconds;
        private bool gameClockInitialized;
        private bool telemetryNeedsRefresh = true;

        private void Awake()
        {
            if (document == null) document = GetComponent<UIDocument>();
        }
        private void OnEnable()
        {
            Active = this;
            telemetryNeedsRefresh = true;
        }
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
            sound = root.Q("Sound");
            inventory = root.Q("Inventory");
            grid = root.Q("InventoryGrid");
            selection = root.Q<InventorySelectionElement>("InventorySelection");
            frame = root.Q<HudFrameElement>("Frame");
            compass = root.Q<CompassElement>("Compass");
            levelIndicator = root.Q<LevelIndicatorElement>("LevelIndicator");
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
            latitudeHemisphereLabel = root.Q<Label>("LatitudeHemisphere");
            longitudeHemisphereLabel = root.Q<Label>("LongitudeHemisphere");
            if (stage == null || view == null || innerRing == null || grid == null) return;

            root.pickingMode = PickingMode.Ignore;
            foreach (VisualElement element in root.Query<VisualElement>().ToList())
                element.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = root.style.top = root.style.right = root.style.bottom = 0;
            root.style.overflow = Overflow.Hidden;
            if (emptyItemTemplate == null)
                emptyItemTemplate = Resources.Load<VisualTreeAsset>("UI/Main/EmptyInventoryItem");
            if (!defaultItemsLoaded)
            {
                for (int i = 0; defaultItems != null && i < Mathf.Min(SlotCount, defaultItems.Count); i++)
                    Items[i] = defaultItems[i];
                defaultItemsLoaded = true;
            }
            BuildSlots();
            ClearCoordinates();
            InitializeGameClock(DateTime.Now);
            RefreshTelemetry(DateTime.Now, true);
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
                button.style.left = new Length((i % 2) * 50, LengthUnit.Percent);
                button.style.top = new Length((i / 2) * 25, LengthUnit.Percent);
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
            if (emptyItemTemplate != null)
            {
                TemplateContainer empty = emptyItemTemplate.CloneTree();
                empty.AddToClassList("empty-item");
                empty.pickingMode = PickingMode.Ignore;
                button.Add(empty);
            }
            else
            {
                button.Add(new EmptyInventoryElement { name = "EmptyItem", style = { flexGrow = 1 } });
                button.Add(new Image { name = "ItemImage", pickingMode = PickingMode.Ignore,
                    style = { position = Position.Absolute, left = 12, right = 12, top = 12, bottom = 38 } });
                button.Add(new Label { name = "ItemName", pickingMode = PickingMode.Ignore,
                    style = { position = Position.Absolute, left = 8, right = 8, bottom = 8, height = 24,
                        unityTextAlign = TextAnchor.MiddleCenter, color = Color.white, fontSize = 18,
                        overflow = Overflow.Hidden } });
            }

            InventoryItem item = Items[index];
            var placeholder = button.Q<EmptyInventoryElement>("EmptyItem");
            if (item != null) placeholder?.RemoveFromHierarchy();
            var image = button.Q<Image>("ItemImage");
            if (image != null)
            {
                image.sprite = item != null ? item.Image : null;
                image.scaleMode = ScaleMode.ScaleToFit;
                image.style.display = item != null ? DisplayStyle.Flex : DisplayStyle.None;
            }
            var label = button.Q<Label>("ItemName");
            if (label != null)
            {
                label.text = item != null ? item.ItemName : string.Empty;
                label.style.display = item != null ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        public void SelectItem(int index)
        {
            selectedIndex = Mathf.Clamp(index, 0, SlotCount - 1);
            if (selection == null) return;
            if (slots[selectedIndex] == null || !Positive(grid.layout.width)) return;
            float inset = InventorySelectionElement.InnerFrameInset;
            Vector2 offset = grid.layout.position + slots[selectedIndex].layout.position
                - selection.layout.position - Vector2.one * inset;
            selection.style.translate = new Translate(offset.x, offset.y);
            selection.style.width = slots[selectedIndex].layout.width + inset * 2;
            selection.style.height = slots[selectedIndex].layout.height + inset * 2;
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
            Cursor.visible = originalCursor;
            Cursor.lockState = originalCursorLock;
        }

        private void Update()
        {
            if (!started || document == null) return;
            if (root != document.rootVisualElement || stage == null) Init();
            if (stage == null) return;
            AdvanceGameClock(Time.deltaTime);
            RefreshTelemetry(DateTime.Now, telemetryNeedsRefresh);
            HandleInput();
            AdvanceInventoryAnimation(Time.unscaledDeltaTime);
        }

        private void LateUpdate() => RefreshLatePresentation();

        private void UpdateLevelIndicator()
        {
            if (levelIndicator == null) return;
            RobotMover robot = HeightMapPlayerSceneBootstrap.inst.robot;
            levelIndicator.SetDegree(robot != null && robot.CurrentTraversalResult.HasData
                ? robot.CurrentTraversalResult.SignedSlopeAngle : 0);
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

            float motion = Ease(progress), horizontal = Ease(progress * 2 - 1);
            float openCircleX = (size.x * (2f / 3) - viewportOffset.x) / fit;
            if (!Positive(ring.layout.width) || !Positive(inventory.layout.width)) return;
            float authoredRingX = view.layout.x + ring.layout.x + innerRing.layout.x + innerRing.contentRect.center.x;
            ringDisplacement = new Vector2((openCircleX - authoredRingX) * horizontal, 0);
            ring.style.translate = new Translate(ringDisplacement.x, ringDisplacement.y);
            // Scale each element around its own screen corner, including its authored edge offsets.
            MoveTopHudOffscreen(status, false, motion);
            MoveTopHudOffscreen(time, true, motion);
            MoveTopHudOffscreen(sound, true, motion);
            FitScreenElement(coordinates, true, true);
            // Keep the authored closed position relative to the screen's left edge, including on ultrawide displays.
            // Only X changes: the UI Builder's top value controls the complete horizontal slide path.
            float inventoryStartX = -viewportOffset.x / fit;
            float inventoryEndX = (size.x * .25f - viewportOffset.x) / fit
                - view.layout.x - inventory.layout.center.x;
            inventory.style.translate = new Translate(Mathf.Lerp(inventoryStartX, inventoryEndX, motion), 0);
            grid.SetEnabled(inventoryOpen && progress >= .98f);
            foreach (Button slot in slots) if (slot != null) slot.pickingMode = inventoryOpen ? PickingMode.Position : PickingMode.Ignore;
            coordinates.style.opacity = 1;
            frame.MarkDirtyRepaint();
            // Position and centering belong to UXML/USS; only size follows the screen fit.
            Vector3 compassScale = new Vector3(fit, fit, 1);
            if (compassHost.style.scale.value.value != compassScale)
            {
                compassHost.style.scale = new Scale(compassScale);
                (compassHost as CompassHostElement)?.RequestMaskRefresh();
            }
            if (levelIndicator != null)
                levelIndicator.style.scale = new Scale(compassScale);
            SelectItem(selectedIndex);
            ApplyPhotoTransform();
        }

        private void MoveTopHudOffscreen(VisualElement element, bool right, float motion)
        {
            if (element == null) return;
            Vector2 offset = FitScreenElement(element, right, false);
            float distance = (Mathf.Max(0, element.layout.y + element.layout.height) + 4) * fit;
            element.style.translate = new Translate(offset.x, offset.y - distance * motion);
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
                SetHeading(Mathf.Atan2(forward.x, forward.y) * Mathf.Rad2Deg);
            }
        }

        public void RefreshLatePresentation()
        {
            UpdateLevelIndicator();
            UpdateOuterRingRotation();
        }

        private void UpdateOuterRingRotation()
        {
            // Keep the tumble offset at full strength on top of the ring's heading.
            if (outerRing != null)
                outerRing.style.rotate = new Rotate(outerRingHeadingDegrees - RobotTumbleUiRotation.ActiveRotationDegrees);
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

        public void SetHeading(float degrees)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees)) return;
            // Scale the shortest heading change so crossing north does not jump by 180 degrees.
            outerRingHeadingDegrees = Mathf.Repeat(outerRingHeadingDegrees
                - Mathf.DeltaAngle(headingDegrees, degrees), 360);
            headingDegrees = Mathf.Repeat(degrees, 360);
            compass?.SetHeading(degrees);
            UpdateOuterRingRotation();
        }
        public void SetDayProgress(float normalized) => timeOrbit?.SetDayProgress(normalized);
        /// <summary>Displays the supplied date using the HUD's date format.</summary>
        public void SetDate(DateTime date)
        {
            if (dateLabel != null) dateLabel.text = date.ToString("yyyy_MM_dd", CultureInfo.InvariantCulture);
        }
        private void InitializeGameClock(DateTime now)
        {
            if (!gameClockInitialized)
            {
                gameTime = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMinute));
                elapsedGameClockSeconds = 0;
                gameClockInitialized = true;
            }
            // Rebind a rebuilt UI to the current clock without restarting time.
            SetDate(gameTime);
            SetDayProgress((float)(gameTime.TimeOfDay.TotalMinutes / 1440d));
        }

        private void AdvanceGameClock(float deltaTime)
        {
            if (!gameClockInitialized || deltaTime <= 0) return;
            elapsedGameClockSeconds += deltaTime;
            double minutesToAdvance = Math.Floor(elapsedGameClockSeconds);
            if (minutesToAdvance < 1) return;

            elapsedGameClockSeconds -= minutesToAdvance;
            DateTime previousDate = gameTime.Date;
            gameTime = gameTime.AddMinutes(minutesToAdvance);
            if (gameTime.Date != previousDate) SetDate(gameTime);
            SetDayProgress((float)(gameTime.TimeOfDay.TotalMinutes / 1440d));
        }

        private void RefreshTelemetry(DateTime now, bool force)
        {
            // Refresh once after a stall; also recover immediately if the computer clock moves backward.
            double elapsed = (now - lastCoordinateRefresh).TotalMilliseconds;
            if (force || elapsed >= 100 || elapsed < 0)
            {
                lastCoordinateRefresh = now;
                UpdateCoordinates();
            }
            telemetryNeedsRefresh = false;
        }

        private void UpdateCoordinates()
        {
            var bootstrap = HeightMapPlayerSceneBootstrap.inst;
            MapTestSceneController map = bootstrap != null ? bootstrap.map : null;
            RobotMover robot = bootstrap != null ? bootstrap.robot : null;
            if (map == null || robot == null
                || !map.TrySampleGeographicWorldPosition(robot.MapPosition,
                    out float latitude, out float longitude, out float height))
            {
                ClearCoordinates();
                return;
            }

            SetCoordinates(latitude, longitude);
            SetAltitude(height);
        }

        private void ClearCoordinates()
        {
            if (latitudeLabel != null) latitudeLabel.text = "--.-----\u00b0";
            if (longitudeLabel != null) longitudeLabel.text = "--.-----\u00b0";
            if (altitudeLabel != null) altitudeLabel.text = "--m";
            if (latitudeHemisphereLabel != null) latitudeHemisphereLabel.text = "--";
            if (longitudeHemisphereLabel != null) longitudeHemisphereLabel.text = "--";
        }

        public void SetCoordinates(float latitude, float longitude)
        {
            if (latitudeLabel != null) latitudeLabel.text = Mathf.Abs(latitude).ToString("F5", CultureInfo.InvariantCulture) + "°";
            if (longitudeLabel != null) longitudeLabel.text = Mathf.Abs(longitude).ToString("F5", CultureInfo.InvariantCulture) + "°";
            if (latitudeHemisphereLabel != null) latitudeHemisphereLabel.text = latitude < 0 ? "S" : "N";
            if (longitudeHemisphereLabel != null) longitudeHemisphereLabel.text = longitude < 0 ? "W" : "E";
        }
        public void SetAltitude(float meters) { if (altitudeLabel != null) altitudeLabel.text = meters.ToString("F0") + "m"; }

        private static bool Positive(float x) => x > 0 && !float.IsNaN(x) && !float.IsInfinity(x);

        private void HandleInput()
        {
            bool toggle = false, close = false, heldOpen = false;
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
                heldOpen |= pad.dpad.right.isPressed;
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
            if (!heldOpen) { openHoldElapsed = 0; openHoldLatched = false; }
            else if (!inventoryOpen && !openHoldLatched)
            {
                openHoldElapsed += Time.unscaledDeltaTime;
                if (openHoldElapsed >= gamepadOpenHoldSeconds) { openHoldLatched = true; OpenInventory(); }
            }
            if (!inventoryOpen || progress < .98f || navigation == Vector2Int.zero) return;
            int column = Mathf.Clamp(selectedIndex % 2 + navigation.x, 0, 1);
            int row = Mathf.Clamp(selectedIndex / 2 + navigation.y, 0, 3);
            SelectItem(row * 2 + column);
        }

        private void OnDisable()
        {
            inventoryOpen = false; progress = 0;
            openHoldElapsed = 0; openHoldLatched = false;
            ReleasePause();
            if (Active == this) Active = null;
        }
    }
}
