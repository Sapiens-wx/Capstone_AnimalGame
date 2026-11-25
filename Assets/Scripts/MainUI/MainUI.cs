using System;
using System.Collections.Generic;
using System.Globalization;
using AnimalGame.Animals;
using AnimalGame.MainUI.Inventory;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using AnimalGame.Save;
using AnimalGame.World;
using UnityEngine;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

namespace AnimalGame.MainUI
{
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed partial class MainUI : MonoBehaviour, IInventoryHost
    {
        [SerializeField] private UIDocument document;
        [SerializeField] private GameObject animalDebugPrefab;
        private GameObject animalDebugInstance;
        [SerializeField] private VisualTreeAsset emptyItemTemplate;
        [Header("Initial inventory")]
        [Tooltip("Initial slot contents, in order. Only the first eight entries are used; null entries leave empty slots.")]
        [SerializeField] private List<InventoryItem> defaultItems = new List<InventoryItem>();
        [Header("Status (normalized)")]
        [SerializeField, Range(0, 1)] private float power = 1;
        [SerializeField, Range(0, 1)] private float integrity = 1;
        [Header("Power ring flicker")]
        [Tooltip("Visible duration in seconds. A new duration is sampled each time the ring lights up.")]
        [SerializeField] private FlickerDuration powerRingOnDuration = new FlickerDuration(.15f, .6f);
        [Tooltip("Hidden duration in seconds. A new duration is sampled each time the ring goes dark.")]
        [SerializeField] private FlickerDuration powerRingOffDuration = new FlickerDuration(.04f, .12f);
        [Header("Level indicator")]
        [Header("Inventory animation")]
        [SerializeField, Min(.05f)] private float animationDuration = .5f;
        [SerializeField] private AnimationCurve inventoryEasing = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 3f),
            new Keyframe(1f, 1f, 0f, 0f));
        [Tooltip("Seconds to hold D-pad right to open, or D-pad left to close, the inventory.")]
        [InspectorName("Gamepad Inventory Hold Seconds")]
        [SerializeField, Min(.1f)] private float gamepadOpenHoldSeconds = .5f;
        [SerializeField, Min(.1f)] private float gamepadCloseHoldSeconds = .4f;

        [Header("Inventory interactions")]
        [SerializeField] private InventoryRewardTable inventoryRewards;
        [SerializeField, Min(.1f)] private float newItemBlinkPeriod = 1;
        public float NewItemBlinkPeriod => newItemBlinkPeriod;
        [SerializeField, Range(.1f, .95f)] private float inventoryStickDeadzone = .5f;
        [SerializeField, Min(.05f)] private float inventoryRepeatDelay = .35f;
        [SerializeField, Min(.05f)] private float inventoryRepeatInterval = .12f;

        private InventoryController inventoryController;
        public InventoryController InventoryController
        {
            get
            {
                inventoryController ??= new InventoryController(this);
                inventoryController.Configure(new InventoryControllerSettings
                {
                    Rewards = inventoryRewards, ItemTemplate = emptyItemTemplate,
                    BlinkPeriod = newItemBlinkPeriod, StickDeadzone = inventoryStickDeadzone,
                    RepeatDelay = inventoryRepeatDelay, RepeatInterval = inventoryRepeatInterval,
                    OpenHoldSeconds = gamepadOpenHoldSeconds, CloseHoldSeconds = gamepadCloseHoldSeconds
                });
                inventoryController.InitializeInventoryData(defaultItems);
                return inventoryController;
            }
        }

        public InventoryStore Inventory => InventoryController.Inventory;
        public InventoryStore Warehouse { get; } = new(16);
        public InventoryInteractionState InteractionState => InventoryController.InteractionState;
        public event Action<InventoryItem> UseRequested
        {
            add => InventoryController.UseRequested += value;
            remove => InventoryController.UseRequested -= value;
        }
        public event Action<InventoryItem> InfoRequested
        {
            add => InventoryController.InfoRequested += value;
            remove => InventoryController.InfoRequested -= value;
        }
        public Action<int> DiscardRequested
        {
            get => InventoryController.DiscardRequested;
            set => InventoryController.DiscardRequested = value;
        }
        public InventoryStore.AddResult AddItem(InventoryItem item, int quantity = 1) => InventoryController.AddItem(item, quantity);
        public InventoryStore.AddResult GrantRecycleRewards(RecyclableSize size) => InventoryController.GrantRecycleRewards(size);
        public void SetItem(int index, InventoryItem item) => InventoryController.SetItem(index, item);
        public void SelectItem(int index) => InventoryController.SelectItem(index);

        private const float DesignWidth = 1920, DesignHeight = 1080;
        private const float DriveDamageWarningDuration = 1.2f;
        private const float DriveDamageWarningFadeDuration = .2f;

        public static MainUI Active { get; private set; }
        internal static bool WorldPanelOpen => AnimalGame.World.Map.WorldMapUI.BlocksGameplay
            || AnimalGame.Animals.UI.AnimalArchiveUI.BlocksGameplay
            || AnimalGame.Animals.Communication.EarthlineUI.BlocksGameplay
            || AnimalGame.World.StationLogUI.BlocksGameplay
            || AnimalGame.World.WarehouseUI.BlocksGameplay;
        public static bool BlocksGameplay => OfficialMapOpening.IsPlaying || WorldPanelOpen
            || (Active != null && Active.isActiveAndEnabled && Active.pauseOwned);
        public bool IsInventoryOpen => inventoryOpen;
        public bool IsCargoLocked { get; private set; }
        public event Action<bool> InventoryOpenChanged;
        public float InventoryProgress => progress;
        public int SelectedIndex => InventoryController.SelectedIndex;
        public float Power { get => power; set => power = Mathf.Clamp01(value); }
        public float Integrity { get => integrity; set => integrity = Mathf.Clamp01(value); }
        public InventoryItem[] Items => InventoryController.Items;

        private VisualElement root, stage, view, ring, status, time, sound, inventory, grid, compassHost, coordinates;
        private VisualElement openingDrivingHint;
        private ViewRingElement innerRing, outerRing;
        private CircularProgressElement powerRing, integrityRing;
        private CompassElement compass;
        private LevelIndicatorElement levelIndicator;
        private HudFrameElement frame;
        private TimeOrbitElement timeOrbit;
        private Label powerLabel, integrityLabel, dateLabel, latitudeLabel, longitudeLabel, altitudeLabel;
        private Label latitudeHemisphereLabel, longitudeHemisphereLabel;
        private Label driveDamageWarning;
        private float driveDamageWarningUntil;
        private Label unstuckFailure;
        private Font unstuckFailureFont;
        private float unstuckFailureUntil;
        private bool started, inventoryOpen, pauseOwned, hasGeometry;
        private CursorLockMode originalCursorLock;
        private float progress, savedTimeScale, fit = 1, photoScale = 1, photoAlpha = 1;
        private float discoveryScanDiscScale = 1f;
        private float lastPower = -1, lastIntegrity = -1;
        private float headingDegrees;
        private float outerRingHeadingDegrees;
        private Vector2 viewportOffset, ringDisplacement, photoOffset, cachedCenter;
        private float cachedRadius;
        private DateTime lastCoordinateRefresh;
        public DateTime CurrentGameTime => SaveSystem.CurrentGameTime;
        private bool telemetryNeedsRefresh = true;
        private bool powerRingLit = true;
        private float powerRingFlickerRemaining;

        [Serializable]
        private struct FlickerDuration
        {
            [Min(.01f)] public float min;
            [Min(.01f)] public float max;

            public FlickerDuration(float min, float max) { this.min = min; this.max = max; }

            public float Sample()
            {
                float lower = Mathf.Max(.01f, Mathf.Min(min, max));
                float upper = Mathf.Max(lower, Mathf.Max(min, max));
                return UnityEngine.Random.Range(lower, upper);
            }
        }

        private void Awake()
        {
            if (document == null) document = GetComponent<UIDocument>();
            if (animalDebugPrefab != null && Application.isPlaying)
            {
                // A separate root prevents UIDocument nesting from merging this panel into the HUD.
                animalDebugInstance = Instantiate(animalDebugPrefab);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(animalDebugInstance, gameObject.scene);
            }
        }
        private void OnEnable()
        {
            if (animalDebugInstance != null) animalDebugInstance.SetActive(true);
            Active = this;
            telemetryNeedsRefresh = true;
            ResetPowerRingFlicker();
        }
        private void Start() { started = true; Init(); }

        public void Init()
        {
            if (document == null) document = GetComponent<UIDocument>();
            VisualElement nextRoot = document != null ? document.rootVisualElement : null;
            if (nextRoot == null) return;
            root = nextRoot;
            stage = root.Q("Stage");
            dpadHoldHint = root.Q<DpadHoldHintElement>("DpadHoldHint");
            view = root.Q("HudView");
            ring = root.Q("ViewRing");
            innerRing = root.Q<ViewRingElement>("InnerRing");
            outerRing = root.Q<ViewRingElement>("OuterRing");
            status = root.Q("Status");
            time = root.Q("Time");
            sound = root.Q("Sound");
            inventory = root.Q("Inventory");
            grid = root.Q("InventoryGrid");
            frame = root.Q<HudFrameElement>("Frame");
            compass = root.Q<CompassElement>("Compass");
            levelIndicator = root.Q<LevelIndicatorElement>("LevelIndicator");
            compassHost = root.Q("CompassHost");
            openingDrivingHint = root.Q("OpeningDrivingHint");
            openingSignalPrompt = root.Q("OpeningSignalPrompt");
            openingSignalMapKey = root.Q<Label>("OpeningSignalMapKey");
            openingTraversalHint = root.Q("OpeningTraversalHint");
            timeOrbit = root.Q<TimeOrbitElement>("TimeOrbit");
            coordinates = root.Q("Coordinates");
            powerRing = root.Q<CircularProgressElement>("PowerRing");
            ResetPowerRingFlicker();
            integrityRing = root.Q<CircularProgressElement>("IntegrityRing");
            powerLabel = root.Q<Label>("PowerPercent");
            integrityLabel = root.Q<Label>("IntegrityPercent");
            dateLabel = root.Q<Label>("Date");
            latitudeLabel = root.Q<Label>("Latitude");
            longitudeLabel = root.Q<Label>("Longitude");
            altitudeLabel = root.Q<Label>("Altitude");
            latitudeHemisphereLabel = root.Q<Label>("LatitudeHemisphere");
            longitudeHemisphereLabel = root.Q<Label>("LongitudeHemisphere");
            driveDamageWarning = root.Q<Label>("DriveDamageWarning");
            unstuckFailure = root.Q<Label>("UnstuckFailure");
            if (stage == null || view == null || innerRing == null || grid == null) return;

            root.pickingMode = PickingMode.Ignore;
            foreach (VisualElement element in root.Query<VisualElement>().ToList())
                element.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = root.style.top = root.style.right = root.style.bottom = 0;
            root.style.overflow = Overflow.Hidden;
            if (emptyItemTemplate == null)
                emptyItemTemplate = Resources.Load<VisualTreeAsset>("UI/Main/EmptyInventoryItem");
            InventoryController.Bind(root, inventory, grid, root.Q<InventorySelectionElement>("InventorySelection"));
            if (navigationTelemetryUnavailable) ApplyUnknownCoordinateLayout();
            UpdateCargoVisibility();
            ClearCoordinates();
            RefreshGameClock(CurrentGameTime, true);
            RefreshTelemetry(DateTime.Now, true);
            lastPower = lastIntegrity = -1;
            ApplyLayout();
            BindRobotNotebook();
            UpdateStatus();
        }

        internal void ShowUnstuckFailure()
        {
            if (unstuckFailure == null) return;
            if (unstuckFailureFont == null)
                unstuckFailureFont = Font.CreateDynamicFontFromOSFont(new[]
                {
                    "Microsoft YaHei", "Noto Sans CJK SC", "PingFang SC"
                }, 22);
            if (unstuckFailureFont != null)
                unstuckFailure.style.unityFontDefinition = FontDefinition.FromFont(unstuckFailureFont);
            unstuckFailure.style.display = DisplayStyle.Flex;
            unstuckFailureUntil = Time.unscaledTime + 2f;
        }

        internal void ShowDriveDamageWarning()
        {
            if (driveDamageWarning == null || BlocksGameplay || Time.timeScale <= 0f) return;
            driveDamageWarningUntil = Time.unscaledTime + DriveDamageWarningDuration;
            driveDamageWarning.style.opacity = 1f;
            driveDamageWarning.style.display = DisplayStyle.Flex;
        }

        private void UpdateDriveDamageWarning()
        {
            if (driveDamageWarningUntil <= 0f) return;
            float remaining = driveDamageWarningUntil - Time.unscaledTime;
            if (remaining <= 0f || BlocksGameplay || Time.timeScale <= 0f)
            {
                HideDriveDamageWarning();
                return;
            }
            if (driveDamageWarning == null) return;
            driveDamageWarning.style.display = DisplayStyle.Flex;
            driveDamageWarning.style.opacity = Mathf.Clamp01(remaining / DriveDamageWarningFadeDuration);
        }

        private void HideDriveDamageWarning()
        {
            driveDamageWarningUntil = 0f;
            if (driveDamageWarning != null) driveDamageWarning.style.display = DisplayStyle.None;
        }

        public void ToggleInventory() { if (inventoryOpen) InventoryController.BackInventory(); else OpenInventory(); }
        public void OpenInventory()
        {
            if (IsCargoLocked || OfficialMapOpening.IsPlaying) return;
            if (WorldPanelOpen) return;
            if (AnimalGame.Tutorial.TutorialTipOverLesson.BlocksGameplayForReading(gameObject)) return;
            if (AnimalGame.AnimalDebug.DebugUI.BlocksOtherPanels) return;
            if (!isActiveAndEnabled || stage == null || !TryGetRingScreenGeometry(out _, out _)) return;
            HeightMapPlayerSceneBootstrap bootstrap = HeightMapPlayerSceneBootstrap.inst;
            if (bootstrap != null && bootstrap.photoMode != null && bootstrap.photoMode.IsActive) return;
            if (bootstrap != null && bootstrap.photoResultView != null && bootstrap.photoResultView.IsShowing) return;
            if (inventoryOpen) return;
            inventoryOpen = true;
            dpadHintVisibleUntil = 0f;
            AcquirePause();
            InventoryOpenChanged?.Invoke(true);
        }

        public void UnlockCargo()
        {
            IsCargoLocked = false;
            UpdateCargoVisibility();
        }

        public void CloseInventory()
        {
            inventoryController?.ResetInventoryInteraction();
            if (!inventoryOpen) return;
            inventoryOpen = false;
            dpadHintVisibleUntil = 0f;
            InventoryOpenChanged?.Invoke(false);
        }

        private void AcquirePause()
        {
            if (pauseOwned) return;
            pauseOwned = true;
            savedTimeScale = Time.timeScale;
            Time.timeScale = 0;
            originalCursorLock = Cursor.lockState;
            Cursor.lockState = CursorLockMode.None;
        }

        private void ReleasePause()
        {
            if (!pauseOwned) return;
            pauseOwned = false;
            // Do not overwrite a time scale explicitly changed by another owner while the panel was open.
            if (Time.timeScale == 0) Time.timeScale = savedTimeScale;
            Cursor.lockState = originalCursorLock;
        }

        private void Update()
        {
            if (!started || document == null) return;
            if (root != document.rootVisualElement || stage == null) Init();
            if (stage == null) return;
            if (unstuckFailureUntil > 0f && (BlocksGameplay || Time.unscaledTime >= unstuckFailureUntil))
            {
                if (unstuckFailure != null) unstuckFailure.style.display = DisplayStyle.None;
                unstuckFailureUntil = 0f;
            }
            AdvanceGameClock(Time.deltaTime);
            RefreshTelemetry(DateTime.Now, telemetryNeedsRefresh);
            if (UpdateRobotNotebookInput()) InventoryController.ConsumeOpenHold();
            if (!IsCargoLocked && !OfficialMapOpening.IsPlaying) InventoryController.Tick();
            AdvanceInventoryAnimation(Time.unscaledDeltaTime);
            if (openingPresentation == null) UpdatePowerRingFlicker(Time.unscaledDeltaTime);
        }

        private void ResetPowerRingFlicker()
        {
            powerRingLit = true;
            powerRingFlickerRemaining = powerRingOnDuration.Sample();
            if (powerRing != null) powerRing.visible = true;
        }

        private void UpdatePowerRingFlicker(float deltaTime)
        {
            if (powerRing == null) return;
            powerRingFlickerRemaining -= deltaTime;
            if (powerRingFlickerRemaining > 0f) return;

            powerRingLit = !powerRingLit;
            powerRing.style.opacity = powerRingLit?1:UnityEngine.Random.Range(0.5f, 0.9f);
            powerRingFlickerRemaining = (powerRingLit ? powerRingOnDuration : powerRingOffDuration).Sample();
        }

        private void LateUpdate()
        {
            RefreshLatePresentation();
            RefreshDpadHoldHint();
            AdvanceOpeningDrivingHint(Time.unscaledDeltaTime);
            AdvanceOpeningSignalPrompt(Time.unscaledDeltaTime);
            AdvanceOpeningTraversalHint(Time.unscaledDeltaTime);
            UpdateDriveDamageWarning();
            RefreshRobotNotebook(Time.unscaledDeltaTime);
        }

        private void UpdateLevelIndicator()
        {
            if (levelIndicator == null) return;
            var bootstrap = HeightMapPlayerSceneBootstrap.inst;
            float degree = bootstrap != null && bootstrap.robot != null && bootstrap.mapCamera != null
                ? SampleTerrainLevelDegree(bootstrap.traversalEvaluator, bootstrap.robot.MapPosition,
                    bootstrap.mapCamera.transform.right) : 0f;
            levelIndicator.SetDegree(degree);
        }

        public static float SampleTerrainLevelDegree(HeightMapTraversalEvaluator evaluator,
            Vector2 worldPosition, Vector2 screenRight)
        {
            // UI Toolkit Y grows downward: terrain rising to screen-right needs a negative angle.
            return evaluator != null && evaluator.TryGetSignedSurfaceSlope(worldPosition, screenRight, out float slope)
                ? -slope : 0f;
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

        private void UpdateCargoVisibility()
        {
            if (inventory != null) inventory.style.opacity = IsCargoLocked ? 0f : StyleKeyword.Null;
        }

        private float EaseInventory(float value)
        {
            float normalized = Mathf.Clamp01(value);
            if (inventoryEasing == null)
                return normalized;

            return inventoryOpen
                ? Mathf.Clamp01(inventoryEasing.Evaluate(normalized))
                : Mathf.Clamp01(1f - inventoryEasing.Evaluate(1f - normalized));
        }

        private void ApplyLayout()
        {
            if (root == null || view == null || root.panel == null) return;
            Vector2 size = root.layout.size;
            if (!Positive(size.x) || !Positive(size.y)) return;
            fit = Mathf.Min(size.x / DesignWidth, size.y / DesignHeight);
            viewportOffset = (size - new Vector2(DesignWidth, DesignHeight) * fit) * .5f;
            UpdateOpeningDrivingHint();
            // Keep authored layout coordinates intact; apply screen fitting through transforms only.
            Vector2 viewOffset = viewportOffset + view.layout.position * (fit - 1);
            view.style.translate = new Translate(viewOffset.x, viewOffset.y);
            view.style.scale = new Scale(new Vector3(fit, fit, 1));

            float motion = EaseInventory(progress);
            float openCircleX = (size.x * (2f / 3) - viewportOffset.x) / fit;
            if (!Positive(ring.layout.width) || !Positive(inventory.layout.width)) return;
            float authoredRingX = view.layout.x + ring.layout.x + innerRing.layout.x + innerRing.contentRect.center.x;
            ringDisplacement = new Vector2((openCircleX - authoredRingX) * motion, 0);
            ApplyRingTransform();
            // Scale each element around its own screen corner, including its authored edge offsets.
            MoveTopHudOffscreen(status, false, motion);
            MoveTopHudOffscreen(time, true, motion);
            MoveTopHudOffscreen(sound, true, motion);
            FitScreenElement(coordinates, true, true);
            // Keep the authored closed position offscreen and place the open panel
            // near its original quarter-screen target, nudged slightly left.
            // Only X changes: the UI Builder's top value controls the complete horizontal slide path.
            float inventoryStartX = -viewportOffset.x / fit;
            const float inventoryOpenLeftNudgePixels = 36f;
            float inventoryEndX = (size.x * .25f - inventoryOpenLeftNudgePixels - viewportOffset.x) / fit
                - view.layout.x - inventory.layout.center.x;
            inventory.style.translate = new Translate(
                Mathf.Lerp(inventoryStartX, inventoryEndX, motion), 0);
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
            InventoryController.RefreshLayout();
            ApplyPhotoTransform();
        }

        /// <summary>Compensates discovery observation zoom without scaling the rest of the HUD.</summary>
        public void SetDiscoveryScanDiscScale(float scale)
        {
            discoveryScanDiscScale = Positive(scale) ? Mathf.Clamp(scale, .01f, 1f) : 1f;
            ApplyRingTransform();
        }

        private void ApplyRingTransform()
        {
            if (ring == null || innerRing == null) return;
            // Preserve the optical centre, including authored offsets and inventory movement.
            Vector2 centre = innerRing.layout.position + innerRing.contentRect.center;
            ring.style.transformOrigin = new TransformOrigin(
                new Length(centre.x, LengthUnit.Pixel), new Length(centre.y, LengthUnit.Pixel));
            ring.style.scale = new Scale(new Vector3(discoveryScanDiscScale, discoveryScanDiscScale, 1f));
            ring.style.translate = new Translate(ringDisplacement.x, ringDisplacement.y);
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
            float shownPower = openingPresentation == null ? power : 1f;
            if (lastPower != shownPower)
            {
                powerRing.SetProgress(shownPower); powerLabel.text = Mathf.RoundToInt(powerRing.progress * 100) + "%";
                lastPower = shownPower;
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
                var worldMap = AnimalGame.World.Map.WorldMapUI.Instance;
                compass?.SetPins(worldMap != null && worldMap.gameObject.scene == bootstrap.gameObject.scene ? worldMap : null,
                    bootstrap.robot.transform.position);
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
            if (TryMeasureRingScreenGeometry(innerRing, false, out Vector2 currentCenter, out float currentRadius))
            {
                cachedCenter = currentCenter;
                cachedRadius = currentRadius;
                hasGeometry = true;
            }
            center = cachedCenter; radius = cachedRadius;
            return hasGeometry;
        }

        internal bool TryGetOuterRingScreenGeometry(out Vector2 center, out float radius) =>
            TryMeasureRingScreenGeometry(outerRing, true, out center, out radius);

        private bool TryMeasureRingScreenGeometry(ViewRingElement element, bool outerEdge,
            out Vector2 center, out float radius)
        {
            if (root != null && root.panel != null && document != null && document.isActiveAndEnabled
                && innerRing != null && element != null && root.resolvedStyle.display != DisplayStyle.None
                && stage != null && stage.resolvedStyle.visibility == Visibility.Visible && photoAlpha > 0
                && Positive(element.Radius) && Positive(root.layout.width) && Positive(root.layout.height))
            {
                // Use the current animation pose rather than last frame's deferred transform-style resolution.
                Vector2 ringPivot = innerRing.layout.position + innerRing.contentRect.center;
                Vector2 elementCenter = element.layout.position + element.contentRect.center;
                Vector2 authoredCenter = view.layout.position + ring.layout.position + ringPivot
                    + (elementCenter - ringPivot) * discoveryScanDiscScale;
                Vector2 panelPoint = viewportOffset + (authoredCenter + ringDisplacement) * fit;
                Vector2 panelCenter = stage.layout.size * .5f;
                panelPoint = panelCenter + (panelPoint - panelCenter) * photoScale + photoOffset * fit;
                if (openingScreenRoll != 0f)
                    panelPoint = panelCenter + (Vector2)(Quaternion.Euler(0f, 0f, openingScreenRoll) * (panelPoint - panelCenter));
                panelPoint += OpeningShakeOffset;
                panelPoint += stage.layout.position;
                Vector2 scale = PanelUnitsPerScreenPixel();
                Vector2 origin = RuntimePanelUtils.ScreenToPanel(root.panel, Vector2.zero);
                Vector2 panelRoot = root.LocalToWorld(panelPoint);
                center = new Vector2((panelRoot.x - origin.x) / scale.x,
                    Screen.height - (panelRoot.y - origin.y) / scale.y);
                float authoredRadius = element.Radius + (outerEdge ? element.lineWidth * .5f : 0f);
                radius = authoredRadius * fit * photoScale * discoveryScanDiscScale
                    / Mathf.Max(scale.x, scale.y);
                return Positive(radius);
            }
            center = default;
            radius = 0f;
            return false;
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
            Vector2 offset = photoOffset * fit + OpeningShakeOffset;
            stage.style.translate = new Translate(offset.x, offset.y);
            stage.style.rotate = new Rotate(openingScreenRoll);
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
            if (openingPresentation == null && dateLabel != null)
                dateLabel.text = navigationTelemetryUnavailable
                    ? OpeningHudTimeline.UnknownReading : date.ToString("MM_dd\nyyyy", CultureInfo.InvariantCulture);
        }
        private void RefreshGameClock(DateTime previous, bool force = false)
        {
            DateTime time = CurrentGameTime;
            if (force || time.Date != previous.Date) SetDate(time);
            if (force || time.Ticks / TimeSpan.TicksPerMinute != previous.Ticks / TimeSpan.TicksPerMinute)
                SetDayProgress((float)((time.TimeOfDay.Ticks / TimeSpan.TicksPerMinute) / 1440d));
        }

        private void AdvanceGameClock(float deltaTime)
        {
            if (deltaTime <= 0) return;
            DateTime previous = CurrentGameTime;
            SaveSystem.Current.gameTime = previous.AddTicks((long)(deltaTime * (double)TimeSpan.TicksPerMinute));
            RefreshGameClock(previous);
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
            if (altitudeLabel != null) altitudeLabel.text = "--m";
            if (TryApplyUnknownCoordinates()) return;
            if (latitudeLabel != null) latitudeLabel.text = "--.-----\u00b0";
            if (longitudeLabel != null) longitudeLabel.text = "--.-----\u00b0";
            if (latitudeHemisphereLabel != null) latitudeHemisphereLabel.text = "--";
            if (longitudeHemisphereLabel != null) longitudeHemisphereLabel.text = "--";
        }

        public void SetCoordinates(float latitude, float longitude)
        {
            if (TryApplyUnknownCoordinates()) return;
            if (latitudeLabel != null) latitudeLabel.text = Mathf.Abs(latitude).ToString("F5", CultureInfo.InvariantCulture) + "°";
            if (longitudeLabel != null) longitudeLabel.text = Mathf.Abs(longitude).ToString("F5", CultureInfo.InvariantCulture) + "°";
            if (latitudeHemisphereLabel != null) latitudeHemisphereLabel.text = latitude < 0 ? "S" : "N";
            if (longitudeHemisphereLabel != null) longitudeHemisphereLabel.text = longitude < 0 ? "W" : "E";
        }
        public void SetAltitude(float meters) { if (altitudeLabel != null) altitudeLabel.text = meters.ToString("F0") + "m"; }

        private static bool Positive(float x) => x > 0 && !float.IsNaN(x) && !float.IsInfinity(x);

        private void OnDisable()
        {
            EndOpeningPresentation();
            HideDriveDamageWarning();
            robotNotebookOpen = false;
            robotNotebookView?.Tick(false, false, 0f);
            if (animalDebugInstance != null) animalDebugInstance.SetActive(false);
            inventoryController?.Suspend();
            if (powerRing != null) powerRing.visible = true;
            bool wasOpen = inventoryOpen;
            inventoryOpen = false; progress = 0;
            if (wasOpen) InventoryOpenChanged?.Invoke(false);
            ReleasePause();
            if (Active == this) Active = null;
        }
        private void OnDestroy()
        {
            inventoryController?.Dispose();
            if (animalDebugInstance != null) Destroy(animalDebugInstance);
            if (unstuckFailureFont != null) Destroy(unstuckFailureFont);
        }
    }
}
