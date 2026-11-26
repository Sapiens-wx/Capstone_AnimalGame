using AnimalGame.MainUI;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using AnimalGame.Save;
using AnimalGame.World.Checkpoints;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace AnimalGame.World.Map
{
    // UIDocument (-100) must create its visual tree before OnEnable binds it.
    [DefaultExecutionOrder(-90)]
    [DisallowMultipleComponent, RequireComponent(typeof(UIDocument))]
    public sealed partial class WorldMapUI : MonoBehaviour
    {
        private const float PinLineWidth = 1.5f;
        private const float UnknownSignalRadius = 94f;
        private const float UnknownSignalPeriod = 2.4f;
        private const float MapDataFaultDuration = .65f;
        private const float MapDataNoticeHold = 3f;
        private const float MapDataNoticeFade = .25f;
        private const float SurveySweepDuration = 1.2f;
        private const float OpenTransitionDuration = 1.5f;
        private const float CloseTransitionDuration = .96f;
        [SerializeField] private Shader contourShader;
        [Header("Zoom (visible range / player view at opening)")]
        [SerializeField, Min(.01f)] private float minimumZoom = 1;
        [Tooltip("0 automatically uses the scale that fits the entire map. Positive values cap that scale.")]
        [SerializeField, Min(0)] private float maximumZoom;
        [SerializeField, Min(.01f)] private float defaultZoom = 2;
        [SerializeField, Min(.01f)] private float zoomSpeed = 1.4f;
        [SerializeField, Min(.01f)] private float wheelZoomSpeed = .18f;
        [Header("Cursor (screen pixels)")]
        [SerializeField, Min(1)] private float cursorSpeed = 500;
        [SerializeField, Min(1)] private float circleRadius = 7;
        [SerializeField, Range(0, .9f)] private float stickDeadZone = .16f;

        public static WorldMapUI Instance { get; private set; }
        private static WorldMapUI active;
        private static int closedFrame = -1;
        public static bool BlocksGameplay => active != null || closedFrame == Time.frameCount;
        internal static string OpenControlLabel => Gamepad.current?.selectButton.displayName.ToUpperInvariant() ?? "C";
        public bool IsOpen { get; private set; }
        public float MinimumZoom { get; private set; }
        public float MaximumZoom { get; private set; }
        public float CurrentZoom { get; private set; }
        public MapViewState View { get; } = new MapViewState();

        private VisualElement panel, root, cursor, playerIcon, mapRulers, northIndicator;
        private VisualElement mapDataNoticeLayer;
        private VisualElement unknownSignal;
        private string unknownSignalStationId;
        private Vector2? unknownSignalWorldPosition;
        private bool frameUnknownSignalOnOpen;
        private Label mapDataNotice;
        private Label northLabel;
        private float mapDataNoticeStartsAt = -1f;
        private readonly VisualElement[] pinElements = new VisualElement[MapViewState.PinLimit];
        private readonly VisualElement[] edgePinElements = new VisualElement[MapViewState.PinLimit];
        private readonly System.Action<MeshGenerationContext>[] pinDrawHandlers = new System.Action<MeshGenerationContext>[MapViewState.PinLimit];
        private readonly VisualElement[] pinChoices = new VisualElement[MapViewState.PinLimit];
        private readonly System.Action<MeshGenerationContext>[] pinChoiceDrawHandlers = new System.Action<MeshGenerationContext>[MapViewState.PinLimit];
        private Label previousPinKey, nextPinKey;
        private VisualElement pinSelector;
        private Image mapImage, mapOpeningImage;
        private PhotoResultGridElement mapGrid;
        private float cursorSelectionSpread;
        private bool centeringMapView;
        private float centeringElapsed;
        private Vector2 centeringStart, centeringCursorStart, centeringTarget;
        private VisualElement controls;
        private Label moveKey, zoomKey, pinKey, centerKey, closeKey;
        private Label coordinateText;
        private VisualElement coordinateMask;
        private Vector2 hoverMapPosition;
        private float hoverIdleSince, coordinateVisibleCount, coordinateRotationProgress;
        private string fullCoordinateText, plainCoordinateText;
        private int coordinateCharacters;
        private bool coordinatesVisible, coordinatesHiding;
        private WorldMapRenderer mapRenderer;
        private MapTestSceneController map;
        private Vector2 baseViewSize, lastSize;
        private float savedTimeScale;
        private float openingPlayerBearing;
        private Vector2 openingPlayerMapPosition, openingPlayerScreenFraction;
        private Vector2 gameplayFootprintPerScreenHeight;
        private float transitionProgress;
        private bool closing;
        private CursorLockMode savedCursorLock;
        private bool savedCursorVisible, dirty;
        private Vector2 lastRenderedCenter, lastRenderedSize;
        private float lastRenderedBearing;
        private CheckpointManager checkpointManager;
        private StationLogUI surveyTerminal;
        private RenderTexture previousSurveyFog;
        private Vector2 surveyOrigin;
        private float surveyStartedAt;
        private float mapOpenedAt;
        private int lastFogRevision = -1;
        private bool MissingMapData => checkpointManager != null && !checkpointManager.HasSurveyData;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { active = null; Instance = null; closedFrame = -1; }

        private void OnEnable()
        {
            Instance = this;
            panel = GetComponent<UIDocument>().rootVisualElement.Q("world-map");
            root = panel.Q("map-viewport");
            mapImage = root.Q<Image>("map-image");
            mapOpeningImage = panel.Q<Image>("map-opening-image");
            mapOpeningImage.scaleMode = ScaleMode.StretchToFill;
            mapGrid = root.Q<PhotoResultGridElement>("map-grid");
            unknownSignal = root.Q("map-unknown-signal");
            unknownSignal.generateVisualContent += DrawUnknownSignal;
            mapDataNotice = panel.Q<Label>("map-data-notice");
            mapDataNoticeLayer = panel.Q("map-data-notice-layer");
            northIndicator = root.Q("map-north");
            northLabel = northIndicator.Q<Label>("map-north-label");
            northIndicator.generateVisualContent += DrawNorthIndicator;
            coordinateMask = root.Q("map-coordinate-mask");
            coordinateText = root.Q<Label>("map-coordinate-text");
            coordinateText.enableRichText = true;
            locationName = root.Q<Label>("map-location-name");
            locationName.enableRichText = false;
            mapRulers = new VisualElement { name = "map-rulers", pickingMode = PickingMode.Ignore };
            mapRulers.style.position = Position.Absolute;
            mapRulers.style.left = mapRulers.style.top = 0;
            mapRulers.style.right = mapRulers.style.bottom = 0;
            mapRulers.generateVisualContent += DrawMapRulers;
            root.Add(mapRulers);
            mapImage.scaleMode = ScaleMode.StretchToFill;
            playerIcon = new VisualElement { name = "map-player", pickingMode = PickingMode.Ignore };
            playerIcon.style.position = Position.Absolute;
            playerIcon.style.scale = new Scale(new Vector3(1.5f, 1.5f, 1f));
            playerIcon.generateVisualContent += context =>
            {
                var painter = context.painter2D;
                painter.strokeColor = new Color32(0xD4, 0xB8, 0x54, 255);
                painter.lineWidth = 1.95f;
                float width = playerIcon.contentRect.width;
                float height = playerIcon.contentRect.height;
                float radius = Mathf.Max(0, Mathf.Min((width - 4f) * .5f, (height - 4f) / 3f));
                Vector2 center = playerIcon.contentRect.center;
                Vector2 tip = center - Vector2.up * (radius * 2f);
                painter.BeginPath();
                painter.MoveTo(tip);
                // Equal X/Y radius gives a circular body; the tip joins it tangentially.
                for (int i = 0; i <= 64; i++)
                {
                    float angle = Mathf.Lerp(-30f, 210f, i / 64f) * Mathf.Deg2Rad;
                    painter.LineTo(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
                }
                painter.ClosePath();
                painter.Stroke();
            };
            root.Insert(root.IndexOf(root.Q("map-cursor")), playerIcon);
            cursor = root.Q("map-cursor");
            cursor.style.scale = new Scale(new Vector3(3.125f, 3.125f, 1f));
            cursor.generateVisualContent += DrawMapCursor;
            controls = panel.Q("map-controls");
            moveKey = controls.Q<Label>("map-move-key");
            zoomKey = controls.Q<Label>("map-zoom-key");
            pinKey = controls.Q<Label>("map-pin-key");
            centerKey = controls.Q<Label>("map-center-key");
            closeKey = controls.Q<Label>("map-close-key");
            previousPinKey = panel.Q<Label>("map-pin-previous-key");
            nextPinKey = panel.Q<Label>("map-pin-next-key");
            pinSelector = panel.Q("map-pin-selector");
            UpdateControlHints();
            for (int i = 0; i < pinElements.Length; i++)
            {
                int index = i;
                var element = root.Q("pin-" + i);
                element.style.scale = new Scale(new Vector3(2f, 2f, 1f));
                pinElements[i] = element;
                pinDrawHandlers[i] = context =>
                {
                    if (index >= View.Pins.Count) return;
                    DrawPinShape(context.painter2D, element.contentRect.center,
                        Mathf.Max(0, element.contentRect.width * .5f - 1), View.GetPinShape(index));
                };
                element.generateVisualContent += pinDrawHandlers[i];
                var edge = new VisualElement { name = "edge-pin-" + i, pickingMode = PickingMode.Ignore };
                edge.style.position = Position.Absolute;
                edge.style.scale = new Scale(new Vector3(2f, 2f, 1f));
                edge.style.opacity = .25f;
                edge.style.display = DisplayStyle.None;
                edge.generateVisualContent += pinDrawHandlers[i];
                edgePinElements[i] = edge;
                root.Insert(root.IndexOf(cursor), edge);
                var choice = panel.Q("map-pin-choice-" + i);
                pinChoices[i] = choice;
                pinChoiceDrawHandlers[i] = context =>
                {
                    bool selected = View.SelectedPinShape == index;
                    bool placed = View.HasPinShape(index);
                    Color color = placed ? Color.white : (Color)new Color32(0xD4, 0xB8, 0x54, 255);
                    color.a = selected ? (placed ? .75f : 1f) : .3f;
                    float radius = index == 1 ? 17f : index == 0 ? 14f : 12f;
                    radius *= selected ? 1.35f : 1f;
                    Vector2 center = choice.contentRect.center;
                    if (index == 0) center.y += radius * .25f;
                    // Compensate for the selector's smaller transform so its stroke
                    // matches the placed map pins in screen pixels.
                    float stroke = PinLineWidth * element.resolvedStyle.scale.value.x
                        / pinSelector.resolvedStyle.scale.value.x;
                    DrawPinShape(context.painter2D, center, radius, index, color, stroke);
                };
                choice.generateVisualContent += pinChoiceDrawHandlers[i];
            }
            coordinateText.BringToFront();
            locationName.BringToFront();
            panel.style.display = DisplayStyle.None;
        }

        private void DrawNorthIndicator(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            painter.strokeColor = Color.white;
            painter.lineWidth = 1f;
            painter.lineCap = LineCap.Butt;
            painter.BeginPath();
            painter.MoveTo(new Vector2(60, 146.4f));
            painter.LineTo(new Vector2(60, 69.6f));
            painter.MoveTo(new Vector2(24, 108));
            painter.LineTo(new Vector2(96, 108));
            painter.MoveTo(new Vector2(55.2f, 74.4f));
            painter.LineTo(new Vector2(60, 69.6f));
            painter.LineTo(new Vector2(64.8f, 74.4f));
            painter.Stroke();
        }

        private void DrawUnknownSignal(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            Vector2 center = unknownSignal.contentRect.center;
            float cycle = Time.unscaledTime / UnknownSignalPeriod;
            for (int i = 0; i < 3; i++)
            {
                float progress = Mathf.Repeat(cycle + i / 3f, 1f);
                float radius = Mathf.Lerp(17f, UnknownSignalRadius - 2f, progress);
                Color color = HudDrawing.Yellow;
                color.a = .68f * (1f - progress) * (1f - progress);
                painter.strokeColor = color;
                painter.lineWidth = Mathf.Lerp(2f, 1f, progress);
                painter.BeginPath();
                painter.Arc(center, radius, Angle.Degrees(0f), Angle.Degrees(360f));
                painter.ClosePath();
                painter.Stroke();
            }
        }

        private void DrawMapRulers(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            painter.strokeColor = Color.white;
            painter.lineWidth = 2f;
            painter.lineCap = LineCap.Butt;
            float step = mapGrid.gridLength * .5f;
            Vector2 size = mapRulers.contentRect.size;
            Vector2 offset = mapGrid.offset;
            float firstX = Mathf.Repeat(offset.x, step);
            float firstY = Mathf.Repeat(offset.y, step);
            int xIndex = Mathf.RoundToInt((firstX - offset.x) / step);
            int yIndex = Mathf.RoundToInt((firstY - offset.y) / step);
            painter.BeginPath();
            for (float x = firstX; x < size.x; x += step, xIndex++)
            {
                float length = xIndex % 5 == 0 ? 18.2f : 10.4f;
                painter.MoveTo(new Vector2(x, 0));
                painter.LineTo(new Vector2(x, length));
            }
            for (float y = firstY; y < size.y; y += step, yIndex++)
            {
                float length = yIndex % 5 == 0 ? 18.2f : 10.4f;
                // Extend into the clipped edge so the ticks meet the outer frame.
                painter.MoveTo(new Vector2(size.x + 1.5f, y));
                painter.LineTo(new Vector2(size.x - length + 1.5f, y));
            }
            painter.Stroke();
        }

        public static void DrawPinShape(Painter2D painter, Vector2 center, float radius, int shape)
            => DrawPinShape(painter, center, radius, shape, new Color32(0xD4, 0xB8, 0x54, 255));

        private static void DrawPinShape(Painter2D painter, Vector2 center, float radius, int shape, Color color,
            float lineWidth = PinLineWidth)
        {
            painter.strokeColor = color;
            painter.lineWidth = lineWidth;
            int sides = shape == 0 ? 3 : 4;
            float startAngle = shape == 1 ? -135f : -90f;
            painter.BeginPath();
            for (int i = 0; i < sides; i++)
            {
                float angle = (startAngle + i * 360f / sides) * Mathf.Deg2Rad;
                Vector2 point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                if (i == 0) painter.MoveTo(point); else painter.LineTo(point);
            }
            painter.ClosePath();
            painter.Stroke();
        }

        private void DrawMapCursor(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            painter.strokeColor = new Color32(0xD4, 0xB8, 0x54, 255);
            // Half the previous icon size, with a 30% thinner screen-space stroke.
            painter.lineWidth = .7f;
            painter.lineCap = LineCap.Round;
            Vector2 center = cursor.contentRect.center;
            float radius = Mathf.Max(0, Mathf.Min(cursor.contentRect.width, cursor.contentRect.height) * .5f - 2);
            float breathing = .035f * Mathf.Sin(Time.unscaledTime * (2f * Mathf.PI / 2.4f))
                * (1f - cursorSelectionSpread);
            for (int i = 0; i < 3; i++)
            {
                float angle = (90f + i * 120f) * Mathf.Deg2Rad;
                Vector2 radial = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                Vector2 tangent = new Vector2(-Mathf.Sin(angle), Mathf.Cos(angle)) * radius;
                Vector2 pieceCenter = center + radial * (.36f * cursorSelectionSpread + breathing);
                // atan(sqrt(3)) = 60 degrees on each side: a 120-degree corner.
                // Separate paths and the inset apex leave equal gaps between all three pieces.
                Vector2 corner = pieceCenter + radial * .25f;
                float halfSpan = .4f * Mathf.Sqrt(3f);
                Vector2 start = pieceCenter + radial * .65f - tangent * halfSpan;
                Vector2 end = pieceCenter + radial * .65f + tangent * halfSpan;
                Vector2 roundStart = Vector2.Lerp(corner, start, .12f);
                Vector2 roundEnd = Vector2.Lerp(corner, end, .12f);
                painter.BeginPath();
                painter.MoveTo(start);
                painter.LineTo(roundStart);
                painter.BezierCurveTo(Vector2.Lerp(roundStart, corner, 2f / 3f),
                    Vector2.Lerp(roundEnd, corner, 2f / 3f), roundEnd);
                painter.LineTo(end);
                painter.Stroke();
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            Gamepad pad = Gamepad.current;
            if (!IsOpen)
            {
                if ((keyboard != null && keyboard.cKey.wasPressedThisFrame)
                    || (pad != null && pad.selectButton.wasPressedThisFrame)) Open();
                return;
            }
            if (!closing && ((keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                || (pad != null && pad.buttonEast.wasPressedThisFrame)))
            {
                Close();
                return;
            }
            Rect viewport = MapScreenRect();
            Vector2 size = viewport.size;
            if (size != lastSize)
            {
                baseViewSize.x = baseViewSize.y * size.x / size.y;
                RecalculateZoomLimits();
                CurrentZoom = Mathf.Clamp(CurrentZoom, MinimumZoom, MaximumZoom);
                ResizeView();
                dirty = true;
                lastSize = size;
            }
            if (closing || transitionProgress < 1f)
            {
                AdvanceTransition(Time.unscaledDeltaTime, size);
                if (IsOpen) RefreshVisuals(viewport);
                return;
            }
            if (previousSurveyFog != null)
            {
                RefreshVisuals(viewport);
                return;
            }
            if ((pad != null && pad.leftShoulder.wasPressedThisFrame)
                || (keyboard != null && keyboard.qKey.wasPressedThisFrame)) CyclePinSelection(-1);
            if ((pad != null && pad.rightShoulder.wasPressedThisFrame)
                || (keyboard != null && keyboard.eKey.wasPressedThisFrame)) CyclePinSelection(1);
            Vector2 movement = pad != null ? pad.leftStick.ReadValue() : Vector2.zero;
            if (movement.magnitude < stickDeadZone) movement = Vector2.zero;
            if (pad != null) movement += pad.dpad.ReadValue();
            if (keyboard != null)
                movement += new Vector2((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                    (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
            if (movement.sqrMagnitude > .0001f) centeringMapView = false;
            if (!centeringMapView)
            {
                Vector2 beforePan = View.Center;
                View.Move(Vector2.ClampMagnitude(movement, 1) * (cursorSpeed * Time.unscaledDeltaTime), size, map.MapSizeMeters);
                // Accumulate only manual pan translation, before any zoom/centering
                // changes the view origin. Zoom never writes the grid offset.
                if (movement.sqrMagnitude > .0001f)
                {
                    Vector2 shift = View.Center - beforePan;
                    Vector2 uiSize = root.contentRect.size;
                    Vector2 offset = mapGrid.offset + new Vector2(
                        -shift.x * uiSize.x / View.ViewSize.x,
                        shift.y * uiSize.y / View.ViewSize.y);
                    // Retain the five-cell phase for the long ruler ticks.
                    float rulerPeriod = mapGrid.gridLength * 5f;
                    mapGrid.offset = new Vector2(Mathf.Repeat(offset.x, rulerPeriod),
                        Mathf.Repeat(offset.y, rulerPeriod));
                    mapRulers.MarkDirtyRepaint();
                }
            }
            float zoomAxis = pad != null ? pad.rightStick.ReadValue().y : 0;
            if (Mathf.Abs(zoomAxis) < stickDeadZone) zoomAxis = 0;
            float wheel = Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0;
            float nextZoom = Mathf.Clamp(CurrentZoom * Mathf.Exp(-zoomAxis * zoomSpeed * Time.unscaledDeltaTime
                - wheel * wheelZoomSpeed), MinimumZoom, MaximumZoom);
            if (!Mathf.Approximately(nextZoom, CurrentZoom))
            {
                centeringMapView = false;
                CurrentZoom = nextZoom;
                ResizeView();
            }
            if ((pad != null && pad.buttonNorth.wasPressedThisFrame)
                || (keyboard != null && keyboard.yKey.wasPressedThisFrame))
            {
                BeginCentering(WorldToMap(HeightMapPlayerSceneBootstrap.inst.robotObject.transform.position));
            }
            bool cursorOnSignal = false;
            if (centeringMapView)
            {
                centeringElapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(centeringElapsed / .45f);
                float eased = Mathf.SmoothStep(0f, 1f, progress);
                View.SetFocus(Vector2.Lerp(centeringStart, centeringTarget, eased),
                    Vector2.Lerp(centeringCursorStart, centeringTarget, eased), map.MapSizeMeters);
                if (progress >= 1f) centeringMapView = false;
            }
            else
            {
                cursorOnSignal = unknownSignalWorldPosition is Vector2 source && View.Cursor == WorldToMap(source);
                View.Snap(size, cursorOnSignal ? 0f : circleRadius * 1.4f);
                if ((keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
                    || (pad != null && pad.buttonSouth.wasPressedThisFrame)) View.Submit();
            }
            cursorSelectionSpread = Mathf.MoveTowards(cursorSelectionSpread, cursorOnSignal || View.SelectedPin >= 0 ? 1f : 0f,
                Time.unscaledDeltaTime / .12f);
            cursor.MarkDirtyRepaint();
            UpdateControlHints();
            RefreshVisuals(viewport);
            UpdateHoverCoordinates(movement.sqrMagnitude > .0001f || centeringMapView);
        }

        private void BeginCentering(Vector2 position)
        {
            centeringStart = View.Center;
            centeringCursorStart = View.DisplayCursor;
            centeringTarget = position;
            centeringElapsed = 0f;
            centeringMapView = true;
        }

        private void CyclePinSelection(int direction)
        {
            View.CyclePinShape(direction);
            centeringMapView = false;
            if (View.TryGetPinPosition(View.SelectedPinShape, out Vector2 position)) BeginCentering(position);
        }

        private void HideHoverCoordinates(bool resetRotation = false)
        {
            coordinatesVisible = false;
            coordinatesHiding = false;
            coordinateVisibleCount = 0;
            if (resetRotation)
            {
                coordinateRotationProgress = 0f;
                cursor.style.rotate = new Rotate(0f);
            }
            coordinateText.style.display = coordinateMask.style.display = DisplayStyle.None;
        }

        private void AdvanceCoordinateReveal(bool opening, float dt)
        {
            coordinateVisibleCount = Mathf.MoveTowards(coordinateVisibleCount,
                opening ? plainCoordinateText.Length : 0, dt / .012f);
            int characters = Mathf.FloorToInt(coordinateVisibleCount);
            if (characters != coordinateCharacters)
            {
                coordinateCharacters = characters;
                coordinateText.text = characters == plainCoordinateText.Length ? fullCoordinateText
                    : PhotoResultView.HideUnrevealedTail(characters, fullCoordinateText);
            }
            if (!opening && coordinateVisibleCount <= 0) HideHoverCoordinates();
        }

        private void UpdateHoverCoordinates(bool moving)
        {
            if (locationName.style.display.value == DisplayStyle.Flex)
            {
                if (coordinatesVisible || coordinateRotationProgress > 0f) HideHoverCoordinates(true);
                hoverMapPosition = View.DisplayCursor;
                hoverIdleSince = Time.unscaledTime;
                return;
            }
            if (MissingMapData) return;

            Vector2 position = View.DisplayCursor;
            float now = Time.unscaledTime;
            if (moving || (position - hoverMapPosition).sqrMagnitude > .000001f)
            {
                hoverMapPosition = position;
                hoverIdleSince = now;
                if (coordinatesVisible) coordinatesHiding = true;
            }
            // Start rotating 0.15 seconds before the one-second text reveal delay.
            const float coordinateRevealDelay = .5f;
            bool rotateOpen = !coordinatesHiding && now - hoverIdleSince >= coordinateRevealDelay - .15f;
            coordinateRotationProgress = Mathf.MoveTowards(coordinateRotationProgress,
                rotateOpen ? 1f : 0f, Time.unscaledDeltaTime / .3f);
            cursor.style.rotate = new Rotate(120f * Mathf.SmoothStep(0f, 1f, coordinateRotationProgress));
            if (coordinatesHiding)
            {
                AdvanceCoordinateReveal(false, Time.unscaledDeltaTime);
                if (coordinatesVisible) UpdateCoordinateMask();
                return;
            }
            if (now - hoverIdleSince < coordinateRevealDelay) return;
            if (!coordinatesVisible)
            {
                var level = map.LevelAsset;
                float latitude = Mathf.Lerp(level.LatitudeRange.x, level.LatitudeRange.y, position.y / map.MapSizeMeters.y);
                float longitude = Mathf.Lerp(level.LongitudeRange.x, level.LongitudeRange.y, position.x / map.MapSizeMeters.x);
                string north = latitude < 0 ? "S" : "N";
                string east = longitude < 0 ? "W" : "E";
                string latitudeValue = Mathf.Abs(latitude).ToString("F5", CultureInfo.InvariantCulture) + "°";
                string longitudeValue = Mathf.Abs(longitude).ToString("F5", CultureInfo.InvariantCulture) + "°";
                plainCoordinateText = north + " " + latitudeValue + " " + east + " " + longitudeValue;
                fullCoordinateText = "<color=#D4B854>" + north + "</color> " + latitudeValue
                    + " <color=#D4B854>" + east + "</color> " + longitudeValue;
                coordinatesVisible = true;
                coordinateVisibleCount = 0;
                coordinateCharacters = -1;
                coordinateText.style.display = coordinateMask.style.display = DisplayStyle.Flex;
            }
            AdvanceCoordinateReveal(true, Time.unscaledDeltaTime);
            Vector2 textSize = coordinateText.MeasureTextSize(plainCoordinateText, 0, VisualElement.MeasureMode.Undefined,
                0, VisualElement.MeasureMode.Undefined) + new Vector2(16, 8);
            Vector2 size = root.contentRect.size;
            textSize.x = Mathf.Min(textSize.x, size.x - 8);
            Vector2 screen = View.ToScreen(position, size);
            float x = screen.x + 18;
            if (x + textSize.x > size.x - 4) x = screen.x - 18 - textSize.x;
            x = Mathf.Clamp(x, 4, Mathf.Max(4, size.x - textSize.x - 4));
            float y = Mathf.Clamp(screen.y - textSize.y * .5f - 1f, 4, Mathf.Max(4, size.y - textSize.y - 4));
            coordinateMask.style.left = coordinateText.style.left = x;
            coordinateMask.style.top = coordinateText.style.top = y;
            coordinateText.style.width = textSize.x;
            coordinateMask.style.height = coordinateText.style.height = textSize.y;
            UpdateCoordinateMask();
        }

        private void UpdateCoordinateMask()
        {
            int characters = Mathf.Clamp(coordinateCharacters, 0, plainCoordinateText.Length);
            float currentWidth = characters == 0 ? 0 : coordinateText.MeasureTextSize(
                plainCoordinateText.Substring(0, characters), 0, VisualElement.MeasureMode.Undefined,
                0, VisualElement.MeasureMode.Undefined).x;
            float nextWidth = characters == plainCoordinateText.Length ? currentWidth : coordinateText.MeasureTextSize(
                plainCoordinateText.Substring(0, characters + 1), 0, VisualElement.MeasureMode.Undefined,
                0, VisualElement.MeasureMode.Undefined).x;
            float width = Mathf.Lerp(currentWidth, nextWidth, coordinateVisibleCount - characters)
                + 16f * Mathf.Clamp01(coordinateVisibleCount);
            coordinateMask.style.width = Mathf.Min(coordinateText.style.width.value.value, width);
        }

        private void UpdateControlHints()
        {
            bool gamepad = Gamepad.current != null;
            controls.EnableInClassList("map-keyboard", !gamepad);
            moveKey.text = gamepad ? "LS" : "WASD";
            zoomKey.text = gamepad ? "RS" : "Wheel";
            pinKey.text = gamepad ? "A" : "Space";
            centerKey.text = "Y";
            closeKey.text = gamepad ? "B" : "Esc";
            previousPinKey.text = gamepad ? "LB" : "Q";
            nextPinKey.text = gamepad ? "RB" : "E";
        }

        internal bool RevealUnknownSignal(string stationId, Vector2 worldPosition)
        {
            if (SaveSystem.IsStationSynced(stationId)) return false;
            unknownSignalStationId = stationId;
            unknownSignalWorldPosition = worldPosition;
            frameUnknownSignalOnOpen = true;
            return true;
        }

        private void ClearSyncedUnknownSignal()
        {
            if (!unknownSignalWorldPosition.HasValue || !SaveSystem.IsStationSynced(unknownSignalStationId)) return;
            unknownSignalWorldPosition = null;
            unknownSignalStationId = null;
            frameUnknownSignalOnOpen = false;
            unknownSignal.style.display = DisplayStyle.None;
        }

        public void Open()
        {
            if (AnimalGame.MainUI.MainUI.BlocksGameplay || HeightMapPlayerSceneBootstrap.StationPlugOwnsInput || Time.timeScale <= 0) return;
            OpenMap(null);
        }

        // The terminal owns the gameplay pause for the entire round trip.
        internal bool OpenMap(StationLogUI terminal)
        {
            if (BlocksGameplay || AnimalGame.AnimalDebug.DebugUI.BlocksOtherPanels) return false;
            var bootstrap = HeightMapPlayerSceneBootstrap.inst;
            if (bootstrap == null || bootstrap.gameObject.scene != gameObject.scene || bootstrap.map == null
                || !bootstrap.map.HasGeneratedMap || bootstrap.mapCamera == null || bootstrap.robotObject == null) return false;
            if ((bootstrap.photoMode != null && bootstrap.photoMode.IsActive)
                || (bootstrap.photoResultView != null && bootstrap.photoResultView.IsShowing)
                || AnimalGame.Tutorial.TutorialTipOverLesson.BlocksGameplayForReading(bootstrap.robotObject)) return false;
            map = bootstrap.map;
            checkpointManager = CheckpointManager.inst;
            if (checkpointManager != null && checkpointManager.gameObject.scene != gameObject.scene) checkpointManager = null;
            lastFogRevision = -1;
            Camera camera = bootstrap.mapCamera;
            // Measure the camera footprint on the map plane without inflating it for camera roll.
            Plane plane = new Plane(Vector3.forward, map.transform.position);
            var corners = new Vector2[3];
            for (int i = 0; i < corners.Length; i++)
            {
                Ray ray = camera.ViewportPointToRay(new Vector3(i % 2, i / 2));
                if (!plane.Raycast(ray, out float distance)) return false;
                corners[i] = ray.GetPoint(distance);
            }
            Vector3 boundsSize = map.WorldBounds.size;
            Vector2 metresPerUnit = new Vector2(map.MapSizeMeters.x / boundsSize.x, map.MapSizeMeters.y / boundsSize.y);
            Vector2 footprint = new Vector2(Vector2.Scale(corners[1] - corners[0], metresPerUnit).magnitude,
                Vector2.Scale(corners[2] - corners[0], metresPerUnit).magnitude);
            Rect cameraPixels = camera.pixelRect;
            gameplayFootprintPerScreenHeight = new Vector2(footprint.x / cameraPixels.width,
                footprint.y / cameraPixels.height) * Screen.height;
            Rect viewport = MapScreenRect();
            Vector2 viewportSize = viewport.size;
            float aspect = viewportSize.x / viewportSize.y;
            baseViewSize.y = Mathf.Max(footprint.y, footprint.x / aspect);
            baseViewSize.x = baseViewSize.y * aspect;
            RecalculateZoomLimits();
            CurrentZoom = Mathf.Clamp(defaultZoom, MinimumZoom, MaximumZoom);
            // Water uses the normal map scale even when a station or signal opens a wider view.
            Vector2 defaultViewSize = baseViewSize * CurrentZoom;
            var waterPatternMetresPerPixel = new Vector2(
                defaultViewSize.x / (int)viewportSize.x, defaultViewSize.y / (int)viewportSize.y);
            Vector3 playerWorld = bootstrap.robotObject.transform.position;
            Vector2 player = WorldToMap(playerWorld);
            ClearSyncedUnknownSignal();
            Vector2? openingSignal = terminal == null && frameUnknownSignalOnOpen ? WorldToMap(unknownSignalWorldPosition.Value) : null;
            Rect stationRegion = default;
            if (terminal != null)
            {
                if (terminal.gameObject.scene != gameObject.scene || checkpointManager == null
                    || !checkpointManager.TryGetStationRegion(map, terminal.Station.StationId, out surveyOrigin, out stationRegion))
                    return false;
                var before = checkpointManager.CaptureFog(map);
                try
                {
                    if (!SaveSystem.SyncStation(terminal.Station.StationId)) return false;
                    previousSurveyFog = before;
                }
                finally
                {
                    if (previousSurveyFog == null) RenderTexture.ReleaseTemporary(before);
                }
                surveyTerminal = terminal;
                float fit = Mathf.Max(stationRegion.width / baseViewSize.x, stationRegion.height / baseViewSize.y);
                CurrentZoom = Mathf.Clamp(fit * 1.08f, MinimumZoom, MaximumZoom);
            }
            if (openingSignal is Vector2 signal)
            {
                Vector2 separation = signal - player;
                // Leave both locations within the central half of the viewport so cursor bounds
                // don't pan the signal toward the edge on the first input update.
                float fitSignalZoom = Mathf.Max(Mathf.Abs(separation.x) / baseViewSize.x,
                    Mathf.Abs(separation.y) / baseViewSize.y) / .46f;
                CurrentZoom = Mathf.Clamp(fitSignalZoom, CurrentZoom, MaximumZoom);
                frameUnknownSignalOnOpen = false;
            }
            Vector2 forward = bootstrap.robot.Forward;
            openingPlayerBearing = Mathf.Atan2(forward.x, forward.y) * Mathf.Rad2Deg;
            openingPlayerMapPosition = player;
            Vector3 playerScreen = camera.WorldToScreenPoint(playerWorld);
            openingPlayerScreenFraction = new Vector2(playerScreen.x / Screen.width, playerScreen.y / Screen.height);
            View.Open(player, baseViewSize * CurrentZoom, map.MapSizeMeters);
            if (terminal != null) View.SetFocus(stationRegion.center, stationRegion.center, map.MapSizeMeters);
            FitWholeMap();
            if (openingSignal is Vector2 source) View.SetFocus((player + source) * .5f, source, map.MapSizeMeters);
            hoverMapPosition = View.DisplayCursor;
            hoverIdleSince = Time.unscaledTime;
            HideHoverCoordinates(true);
            mapGrid.offset = new Vector2(0, 12);
            mapRulers.MarkDirtyRepaint();
            cursorSelectionSpread = openingSignal.HasValue ? 1f : 0f;
            cursor.MarkDirtyRepaint();
            mapRenderer = new WorldMapRenderer(contourShader, waterPatternMetresPerPixel);
            mapOpenedAt = Time.unscaledTime;
            savedTimeScale = Time.timeScale;
            savedCursorLock = UnityEngine.Cursor.lockState;
            savedCursorVisible = UnityEngine.Cursor.visible;
            Time.timeScale = 0;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = false;
            active = this;
            IsOpen = true;
            closing = false;
            transitionProgress = 0f;
            ApplyTransitionPose(viewportSize);
            mapDataNotice.EnableInClassList("survey-notice", terminal != null);
            RebuildSyncedLocations(terminal == null ? null : terminal.Station.StationId);
            if (terminal != null)
            {
                surveyStartedAt = Time.unscaledTime;
                mapDataNotice.text = terminal.Station.StationId + "  /  RECEIVING MAP DATA";
                mapDataNotice.style.opacity = 1f;
                mapDataNotice.style.display = DisplayStyle.Flex;
            }
            else if (MissingMapData)
            {
                mapDataNotice.text = "MISSING MAP DATA";
                mapDataNoticeStartsAt = Time.unscaledTime + (openingSignal.HasValue ? MapDataFaultDuration : 0f);
            }
            lastSize = viewportSize;
            panel.style.display = DisplayStyle.Flex;
            dirty = true;
            UpdateControlHints();
            RefreshVisuals(viewport);
            return true;
        }

        private Rect MapScreenRect()
        {
            Rect content = root.contentRect;
            float panelHeight = panel.contentRect.height;
            if (content.width <= 0 || content.height <= 0 || panelHeight <= 0)
            {
                // The hidden panel has no layout yet. Use the map frame's USS margins.
                return new Rect(Screen.width * .02f, Screen.height * .07f, Screen.width * .96f, Screen.height * .9f);
            }

            float pixelsPerUnit = Screen.height / panelHeight;
            Vector2 center = panel.WorldToLocal(root.LocalToWorld(content.center)) * pixelsPerUnit;
            center.y = Screen.height - center.y;
            Vector2 size = content.size * pixelsPerUnit;
            return new Rect(center - size * .5f, size);
        }

        private void RecalculateZoomLimits()
        {
            float fit = Mathf.Max(map.MapSizeMeters.x / baseViewSize.x, map.MapSizeMeters.y / baseViewSize.y);
            MaximumZoom = maximumZoom > 0 ? Mathf.Min(maximumZoom, fit) : fit;
            MinimumZoom = Mathf.Min(minimumZoom, MaximumZoom);
        }

        private void ResizeView()
        {
            View.Resize(baseViewSize * CurrentZoom);
            FitWholeMap();
        }

        private void FitWholeMap()
        {
            if (ShowsWholeMap) View.FitMap(map.MapSizeMeters);
        }

        private bool ShowsWholeMap => View.ViewSize.x >= map.MapSizeMeters.x - .001f
            && View.ViewSize.y >= map.MapSizeMeters.y - .001f;

        private float TransitionTurn
        {
            get
            {
                float t = Mathf.InverseLerp(.15f, 1f, transitionProgress);
                return t * t * t * (4f - 3f * t);
            }
        }

        private void AdvanceTransition(float deltaTime, Vector2 viewportSize)
        {
            float duration = closing ? CloseTransitionDuration : OpenTransitionDuration;
            transitionProgress = Mathf.MoveTowards(transitionProgress, closing ? 0f : 1f, deltaTime / duration);
            ApplyTransitionPose(viewportSize);
            if (closing && transitionProgress == 0f) FinishClose(returnToGameplay: true);
            else if (transitionProgress == 1f) hoverIdleSince = Time.unscaledTime;
        }

        private void ApplyTransitionPose(Vector2 viewportSize)
        {
            cursor.style.visibility = !closing && transitionProgress == 1f ? Visibility.Visible : Visibility.Hidden;
            float turn = TransitionTurn;
            View.Bearing = Mathf.Lerp(openingPlayerBearing, 0f, turn);
            // Crop the gameplay footprint to the map frame at the same metres per screen pixel.
            Vector2 gameplayViewSize = Vector2.Scale(gameplayFootprintPerScreenHeight, viewportSize / Screen.height);
            View.TransitionViewSize = turn < 1f ? Vector2.Lerp(gameplayViewSize, View.ViewSize, turn) : null;
            mapGrid.SetRotation(-View.Bearing);
            northIndicator.style.rotate = new Rotate(-View.Bearing);
            northLabel.style.rotate = new Rotate(View.Bearing);
            mapRulers.style.opacity = 1f - Mathf.InverseLerp(0f, 8f, Mathf.Abs(View.Bearing));
        }

        private void UpdateOpeningImage(Rect viewport, Vector2 displaySize, WorldMapAperture? aperture)
        {
            panel.EnableInClassList("revealing", aperture.HasValue);
            if (aperture is WorldMapAperture opening)
            {
                float appliedHeight = root.contentRect.height;
                float pixelsPerUnit = appliedHeight > 0 ? viewport.height / appliedHeight : 1f;
                Vector2 gridOffset = mapGrid.offset * pixelsPerUnit;
                var grid = new Vector4(gridOffset.x, gridOffset.y,
                    mapGrid.gridLength * pixelsPerUnit, mapGrid.width * pixelsPerUnit);
                mapOpeningImage.image = mapRenderer.RenderOpening(mapImage.image, displaySize, viewport, opening,
                    View.Bearing, grid, mapGrid.color);
            }
            else if (mapOpeningImage.image != null)
            {
                mapOpeningImage.image = null;
                mapRenderer.EndOpening();
            }
        }

        private void RefreshVisuals(Rect viewport)
        {
            foreach (var choice in pinChoices) choice.MarkDirtyRepaint();
            Texture fog = checkpointManager != null ? checkpointManager.GetFog(map) : null;
            int fogRevision = checkpointManager != null ? checkpointManager.Revision : 0;
            mapRenderer.WaterTime = Time.unscaledTime - mapOpenedAt;
            Vector2 screenSize = viewport.size;
            Vector2 projectedSize = View.ProjectedViewSize;
            float turn = TransitionTurn;
            Vector2 displaySize = new Vector2(Screen.width, Screen.height);
            Vector2 mapAnchor = ShowsWholeMap ? viewport.center : displaySize * .5f;
            Vector2 playerScreen = Vector2.Scale(openingPlayerScreenFraction, displaySize);
            Vector2 screenAnchor = Vector2.Lerp(playerScreen, mapAnchor, turn);
            Vector2 focus = Vector2.Lerp(openingPlayerMapPosition, View.Center, turn);
            View.AnchorProjection(focus, (screenAnchor - viewport.center) / screenSize);
            Vector2 projectedCenter = View.ProjectedCenter;
            if (dirty || projectedCenter != lastRenderedCenter || projectedSize != lastRenderedSize
                || View.Bearing != lastRenderedBearing || lastFogRevision != fogRevision)
            {
                mapRenderer.Render(map, View, (int)screenSize.x, (int)screenSize.y, fog);
                lastRenderedCenter = projectedCenter;
                lastRenderedSize = projectedSize;
                lastRenderedBearing = View.Bearing;
                dirty = false;
                lastFogRevision = fogRevision;
            }
            else mapRenderer.RenderWaterFrame(map);
            RefreshMapDataPresentation();
            WorldMapAperture? aperture = transitionProgress < 1f
                ? new WorldMapAperture(displaySize, playerScreen, Mathf.SmoothStep(0f, 1f, transitionProgress))
                : null;
            UpdateOpeningImage(viewport, displaySize, aperture);
            Vector2 uiSize = root.contentRect.size;
            if (uiSize.x <= 0 || uiSize.y <= 0) return;
            float MarkerOpacity(Vector2 position, float extent)
            {
                if (aperture is not WorldMapAperture opening) return 1f;
                Vector2 local = new Vector2(position.x, uiSize.y - position.y) / uiSize;
                Vector2 pixel = viewport.min + Vector2.Scale(local, viewport.size);
                return opening.OpacityAt(pixel, extent * viewport.height / uiSize.y);
            }

            Rect compassBounds = northIndicator.worldBound;
            northIndicator.style.opacity = MarkerOpacity(root.WorldToLocal(compassBounds.center), compassBounds.size.magnitude * .5f);
            Rect noticeBounds = mapDataNotice.worldBound;
            mapDataNoticeLayer.style.opacity = MarkerOpacity(root.WorldToLocal(noticeBounds.center), noticeBounds.size.magnitude * .5f);
            if (unknownSignalWorldPosition is Vector2 signalWorldPosition)
            {
                Vector2 signalPosition = View.ToScreen(WorldToMap(signalWorldPosition), uiSize);
                bool visible = signalPosition.x >= -UnknownSignalRadius && signalPosition.x <= uiSize.x + UnknownSignalRadius
                    && signalPosition.y >= -UnknownSignalRadius && signalPosition.y <= uiSize.y + UnknownSignalRadius;
                unknownSignal.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (visible)
                {
                    PositionCircle(unknownSignal, signalPosition, UnknownSignalRadius);
                    unknownSignal.style.opacity = MarkerOpacity(signalPosition, UnknownSignalRadius);
                    unknownSignal.MarkDirtyRepaint();
                }
            }
            RefreshSyncedLocations(uiSize);
            float radius = circleRadius * uiSize.y / screenSize.y;
            for (int i = 0; i < pinElements.Length; i++)
            {
                bool visible = i < View.Pins.Count;
                pinElements[i].style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                edgePinElements[i].style.display = DisplayStyle.None;
                if (visible)
                {
                    pinElements[i].MarkDirtyRepaint();
                    Vector2 pinPosition = View.ToScreen(View.Pins[i], uiSize);
                    PositionCircle(pinElements[i], pinPosition, radius);
                    pinElements[i].style.opacity = MarkerOpacity(pinPosition, radius * 2f);
                    if (pinPosition.x < 0 || pinPosition.x > uiSize.x
                        || pinPosition.y < 0 || pinPosition.y > uiSize.y)
                    {
                        // Orthogonal projection onto the viewport edge; inset by the scaled
                        // symbol radius so the entire translucent copy stays inside the frame.
                        float inset = radius * 2f + 2f;
                        Vector2 edgePosition = new Vector2(
                            Mathf.Clamp(pinPosition.x, inset, Mathf.Max(inset, uiSize.x - inset)),
                            Mathf.Clamp(pinPosition.y, inset, Mathf.Max(inset, uiSize.y - inset)));
                        var edge = edgePinElements[i];
                        edge.style.display = DisplayStyle.Flex;
                        PositionCircle(edge, edgePosition, radius);
                        edge.style.opacity = .25f * MarkerOpacity(edgePosition, radius * 2f);
                        edge.MarkDirtyRepaint();
                    }
                }
            }
            var bootstrap = HeightMapPlayerSceneBootstrap.inst;
            Vector2 playerPosition = View.ToScreen(WorldToMap(bootstrap.robotObject.transform.position), uiSize);
            PositionCircle(playerIcon, playerPosition, radius * 1.5f);
            playerIcon.style.opacity = MarkerOpacity(playerPosition, radius * 2f);
            playerIcon.style.rotate = new Rotate(openingPlayerBearing - View.Bearing);
            if (!closing && transitionProgress == 1f)
                PositionCircle(cursor, View.ToScreen(View.DisplayCursor, uiSize), radius);
        }

        private void RefreshMapDataPresentation()
        {
            if (previousSurveyFog != null)
            {
                float progress = (Time.unscaledTime - surveyStartedAt) / SurveySweepDuration;
                if (progress < 1f)
                {
                    mapImage.image = mapRenderer.RenderReveal(previousSurveyFog, surveyOrigin, progress);
                    return;
                }
                EndSurveySweep();
                RebuildSyncedLocations(null);
                mapDataNotice.text = surveyTerminal.Station.StationId + "  /  MAP DATA SYNCED";
                mapDataNoticeStartsAt = Time.unscaledTime;
            }
            float age = Time.unscaledTime - mapDataNoticeStartsAt;
            if (age < 0f)
            {
                mapImage.image = mapRenderer.RenderFault(Mathf.Clamp01(1f + age / MapDataFaultDuration));
                return;
            }

            if (mapImage.image != mapRenderer.Texture)
            {
                mapImage.image = mapRenderer.Texture;
                mapRenderer.EndFault();
            }
            if (mapDataNoticeStartsAt < 0f) return;

            float fade = Mathf.Clamp01((age - MapDataNoticeHold) / MapDataNoticeFade);
            if (fade >= 1f)
            {
                mapDataNotice.style.display = DisplayStyle.None;
                mapDataNoticeStartsAt = -1f;
                return;
            }

            mapDataNotice.style.display = DisplayStyle.Flex;
            mapDataNotice.style.opacity = 1f - fade;
        }

        public Vector2 WorldToMap(Vector2 position)
        {
            Bounds bounds = map.WorldBounds;
            return new Vector2((position.x - bounds.min.x) / bounds.size.x * map.MapSizeMeters.x,
                (position.y - bounds.min.y) / bounds.size.y * map.MapSizeMeters.y);
        }

        public float PinBearing(int index, Vector2 playerWorld)
        {
            Vector2 offset = View.Pins[index] - WorldToMap(playerWorld);
            Bounds bounds = map.WorldBounds;
            offset = Vector2.Scale(offset, new Vector2(bounds.size.x / map.MapSizeMeters.x,
                bounds.size.y / map.MapSizeMeters.y));
            return Mathf.Atan2(offset.x, offset.y) * Mathf.Rad2Deg;
        }

        private static void PositionCircle(VisualElement element, Vector2 position, float radius)
        {
            element.style.left = position.x - radius;
            element.style.top = position.y - radius;
            element.style.width = element.style.height = radius * 2;
        }

        private void EndSurveySweep()
        {
            if (previousSurveyFog == null) return;
            mapImage.image = mapRenderer.Texture;
            mapRenderer.EndReveal();
            RenderTexture.ReleaseTemporary(previousSurveyFog);
            previousSurveyFog = null;
            ClearSyncedUnknownSignal();
        }

        internal void CloseFor(StationLogUI terminal)
        {
            if (surveyTerminal == terminal) FinishClose();
        }

        public void Close()
        {
            if (!IsOpen || closing) return;
            if (transitionProgress == 0f) { FinishClose(returnToGameplay: true); return; }
            HideHoverCoordinates(true);
            centeringMapView = false;
            closing = true;
            cursor.style.visibility = Visibility.Hidden;
        }

        private void FinishClose(bool returnToGameplay = false)
        {
            if (!IsOpen) return;
            IsOpen = false;
            closing = false;
            HideHoverCoordinates(true);
            centeringMapView = false;
            if (active == this) active = null;
            closedFrame = Time.frameCount;
            panel.style.display = DisplayStyle.None;
            mapDataNotice.style.display = DisplayStyle.None;
            mapDataNoticeStartsAt = -1f;
            EndSurveySweep();
            ClearSyncedLocations();
            mapImage.image = null;
            mapOpeningImage.image = null;
            checkpointManager = null;
            mapRenderer.Dispose(); mapRenderer = null;
            if (Time.timeScale == 0) Time.timeScale = savedTimeScale;
            UnityEngine.Cursor.lockState = savedCursorLock;
            UnityEngine.Cursor.visible = savedCursorVisible;
            StationLogUI terminal = surveyTerminal;
            surveyTerminal = null;
            if (terminal != null && terminal.isActiveAndEnabled)
            {
                if (returnToGameplay) terminal.FinishSurvey();
                else terminal.ReturnFromMap();
            }
        }

        private void OnDisable()
        {
            FinishClose();
            if (cursor != null) cursor.generateVisualContent -= DrawMapCursor;
            if (northIndicator != null) northIndicator.generateVisualContent -= DrawNorthIndicator;
            if (unknownSignal != null) unknownSignal.generateVisualContent -= DrawUnknownSignal;
            for (int i = 0; i < pinElements.Length; i++)
            {
                if (pinElements[i] != null) pinElements[i].generateVisualContent -= pinDrawHandlers[i];
                edgePinElements[i]?.RemoveFromHierarchy();
                if (pinChoices[i] != null) pinChoices[i].generateVisualContent -= pinChoiceDrawHandlers[i];
            }
            mapRulers?.RemoveFromHierarchy();
            playerIcon?.RemoveFromHierarchy();
            if (Instance == this) Instance = null;
        }
    }
}
