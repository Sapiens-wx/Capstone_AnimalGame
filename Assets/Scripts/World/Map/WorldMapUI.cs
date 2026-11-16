using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using AnimalGame.World.Checkpoints;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace AnimalGame.World.Map
{
    [DefaultExecutionOrder(-300)]
    [DisallowMultipleComponent, RequireComponent(typeof(UIDocument))]
    public sealed class WorldMapUI : MonoBehaviour
    {
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
        [SerializeField, Min(1f)] private float checkpointIconSizePixels = 26f;

        public static WorldMapUI Instance { get; private set; }
        private static WorldMapUI active;
        private static int closedFrame = -1;
        public static bool BlocksGameplay => active != null || closedFrame == Time.frameCount;
        public bool IsOpen { get; private set; }
        public float MinimumZoom { get; private set; }
        public float MaximumZoom { get; private set; }
        public float CurrentZoom { get; private set; }
        public MapViewState View { get; } = new MapViewState();

        private VisualElement panel, root, cursor, playerIcon;
        private readonly VisualElement[] pinElements = new VisualElement[MapViewState.PinLimit];
        private Image mapImage;
        private PhotoResultGridElement mapGrid;
        private float gridCellMeters;
        private float cursorPinSpread;
        private VisualElement controls;
        private Label moveKey, zoomKey, pinKey, closeKey;
        private WorldMapRenderer mapRenderer;
        private MapTestSceneController map;
        private Vector2 baseViewSize, lastSize;
        private float savedTimeScale;
        private float openingPlayerBearing;
        private float openingMapViewHeight;
        private CursorLockMode savedCursorLock;
        private bool savedCursorVisible, dirty;
        private Vector2 lastRenderedCenter, lastRenderedSize;
        private CheckpointManager checkpointManager;
        private VisualElement checkpointLayer;
        private readonly List<Image> checkpointIcons = new();
        private int lastFogRevision = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { active = null; Instance = null; closedFrame = -1; }

        private void OnEnable()
        {
            Instance = this;
            panel = GetComponent<UIDocument>().rootVisualElement.Q("world-map");
            root = panel.Q("map-viewport");
            mapImage = root.Q<Image>("map-image");
            mapGrid = root.Q<PhotoResultGridElement>("map-grid");
            mapImage.scaleMode = ScaleMode.StretchToFill;
            checkpointLayer = new VisualElement { name = "map-checkpoints", pickingMode = PickingMode.Ignore };
            checkpointLayer.style.position = Position.Absolute;
            checkpointLayer.style.left = checkpointLayer.style.top = 0;
            checkpointLayer.style.right = checkpointLayer.style.bottom = 0;
            checkpointLayer.style.overflow = Overflow.Hidden;
            root.Insert(1, checkpointLayer);
            playerIcon = new VisualElement { name = "map-player", pickingMode = PickingMode.Ignore };
            playerIcon.style.position = Position.Absolute;
            playerIcon.generateVisualContent += context =>
            {
                var painter = context.painter2D;
                painter.strokeColor = new Color32(0xD4, 0xB8, 0x54, 255);
                painter.lineWidth = 1.5f;
                float width = playerIcon.contentRect.width;
                float height = playerIcon.contentRect.height;
                float radius = Mathf.Max(0, Mathf.Min((width - 4f) * .5f, (height - 4f) / 3f));
                Vector2 center = new Vector2(width * .5f, height - 2f - radius);
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
            closeKey = controls.Q<Label>("map-close-key");
            UpdateControlHints();
            for (int i = 0; i < pinElements.Length; i++) pinElements[i] = root.Q("pin-" + i);
            panel.style.display = DisplayStyle.None;
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
            for (int i = 0; i < 3; i++)
            {
                float angle = (90f + i * 120f) * Mathf.Deg2Rad;
                Vector2 radial = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                Vector2 tangent = new Vector2(-Mathf.Sin(angle), Mathf.Cos(angle)) * radius;
                Vector2 pieceCenter = center + radial * (.3f * cursorPinSpread);
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
            if ((keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                || (pad != null && pad.buttonEast.wasPressedThisFrame))
            {
                Close();
                return;
            }
            Vector2 size = MapScreenSize();
            if (size != lastSize)
            {
                baseViewSize.x = baseViewSize.y * size.x / size.y;
                RecalculateZoomLimits();
                CurrentZoom = Mathf.Clamp(CurrentZoom, MinimumZoom, MaximumZoom);
                ResizeView();
                dirty = true;
                lastSize = size;
            }
            Vector2 movement = pad != null ? pad.leftStick.ReadValue() : Vector2.zero;
            if (movement.magnitude < stickDeadZone) movement = Vector2.zero;
            if (pad != null) movement += pad.dpad.ReadValue();
            if (keyboard != null)
                movement += new Vector2((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                    (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
            View.Move(Vector2.ClampMagnitude(movement, 1) * (cursorSpeed * Time.unscaledDeltaTime), size, map.MapSizeMeters);
            float zoomAxis = pad != null ? pad.rightStick.ReadValue().y : 0;
            if (Mathf.Abs(zoomAxis) < stickDeadZone) zoomAxis = 0;
            float wheel = Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0;
            float nextZoom = Mathf.Clamp(CurrentZoom * Mathf.Exp(-zoomAxis * zoomSpeed * Time.unscaledDeltaTime
                - wheel * wheelZoomSpeed), MinimumZoom, MaximumZoom);
            if (!Mathf.Approximately(nextZoom, CurrentZoom))
            {
                CurrentZoom = nextZoom;
                ResizeView();
            }
            View.Snap(size, circleRadius * 1.4f, checkpointManager?.Positions, checkpointIconSizePixels * .6f);
            if ((keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
                || (pad != null && pad.buttonSouth.wasPressedThisFrame)) View.Submit();
            float nextSpread = Mathf.MoveTowards(cursorPinSpread, View.SelectedPin >= 0 ? 1f : 0f,
                Time.unscaledDeltaTime / .12f);
            if (!Mathf.Approximately(nextSpread, cursorPinSpread))
            {
                cursorPinSpread = nextSpread;
                cursor.MarkDirtyRepaint();
            }
            UpdateControlHints();
            RefreshVisuals(size);
        }

        private void UpdateControlHints()
        {
            bool gamepad = Gamepad.current != null;
            controls.EnableInClassList("map-keyboard", !gamepad);
            moveKey.text = gamepad ? "LS" : "WASD";
            zoomKey.text = gamepad ? "RS" : "Wheel";
            pinKey.text = gamepad ? "A" : "Space";
            closeKey.text = gamepad ? "B" : "Esc";
        }

        public void Open()
        {
            if (IsOpen || BlocksGameplay || AnimalGame.MainUI.MainUI.BlocksGameplay
                || AnimalGame.AnimalDebug.DebugUI.BlocksOtherPanels || Time.timeScale <= 0) return;
            var bootstrap = HeightMapPlayerSceneBootstrap.inst;
            if (bootstrap == null || bootstrap.gameObject.scene != gameObject.scene || bootstrap.map == null
                || !bootstrap.map.HasGeneratedMap || bootstrap.mapCamera == null || bootstrap.robotObject == null) return;
            if ((bootstrap.photoMode != null && bootstrap.photoMode.IsActive)
                || (bootstrap.photoResultView != null && bootstrap.photoResultView.IsShowing)
                || AnimalGame.Tutorial.TutorialTipOverLesson.BlocksGameplayForReading(bootstrap.robotObject)) return;
            map = bootstrap.map;
            checkpointManager = CheckpointManager.inst;
            if (checkpointManager != null && checkpointManager.gameObject.scene != gameObject.scene) checkpointManager = null;
            checkpointLayer.Clear();
            checkpointIcons.Clear();
            if (checkpointManager != null)
            {
                checkpointManager.GetFog(map);
                foreach (Checkpoint checkpoint in checkpointManager.Checkpoints)
                {
                    var icon = new Image { sprite = checkpoint.Icon, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                    icon.style.position = Position.Absolute;
                    checkpointLayer.Add(icon);
                    checkpointIcons.Add(icon);
                }
            }
            lastFogRevision = -1;
            Camera camera = bootstrap.mapCamera;
            // Measure the camera footprint on the map plane without inflating it for camera roll.
            Plane plane = new Plane(Vector3.forward, map.transform.position);
            var corners = new Vector2[3];
            for (int i = 0; i < corners.Length; i++)
            {
                Ray ray = camera.ViewportPointToRay(new Vector3(i % 2, i / 2));
                if (!plane.Raycast(ray, out float distance)) return;
                corners[i] = ray.GetPoint(distance);
            }
            Vector3 boundsSize = map.WorldBounds.size;
            Vector2 metresPerUnit = new Vector2(map.MapSizeMeters.x / boundsSize.x, map.MapSizeMeters.y / boundsSize.y);
            Vector2 footprint = new Vector2(Vector2.Scale(corners[1] - corners[0], metresPerUnit).magnitude,
                Vector2.Scale(corners[2] - corners[0], metresPerUnit).magnitude);
            Vector2 viewportSize = MapScreenSize();
            float aspect = viewportSize.x / viewportSize.y;
            baseViewSize.y = Mathf.Max(footprint.y, footprint.x / aspect);
            baseViewSize.x = baseViewSize.y * aspect;
            RecalculateZoomLimits();
            CurrentZoom = Mathf.Clamp(defaultZoom, MinimumZoom, MaximumZoom);
            Vector2 player = WorldToMap(bootstrap.robotObject.transform.position);
            Vector2 forward = bootstrap.robot.Forward;
            openingPlayerBearing = Mathf.Atan2(forward.x, forward.y) * Mathf.Rad2Deg;
            View.Open(player, baseViewSize * CurrentZoom, map.MapSizeMeters);
            cursorPinSpread = 0f;
            cursor.MarkDirtyRepaint();
            FitWholeMap();
            openingMapViewHeight = View.ViewSize.y;
            gridCellMeters = 0f;
            mapRenderer = new WorldMapRenderer(contourShader);
            savedTimeScale = Time.timeScale;
            savedCursorLock = UnityEngine.Cursor.lockState;
            savedCursorVisible = UnityEngine.Cursor.visible;
            Time.timeScale = 0;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = false;
            active = this;
            IsOpen = true;
            lastSize = viewportSize;
            panel.style.display = DisplayStyle.Flex;
            dirty = true;
            RefreshVisuals(lastSize);
        }

        private Vector2 MapScreenSize()
        {
            Vector2 size = root.contentRect.size;
            float panelHeight = panel.contentRect.height;
            return size.x > 0 && size.y > 0 && panelHeight > 0
                ? size * (Screen.height / panelHeight)
                : new Vector2(Screen.width * .96f, Screen.height * .9f);
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
            if (View.ViewSize.x >= map.MapSizeMeters.x - .001f
                && View.ViewSize.y >= map.MapSizeMeters.y - .001f) View.FitMap(map.MapSizeMeters);
        }

        private void RefreshVisuals(Vector2 screenSize)
        {
            Texture fog = checkpointManager != null ? checkpointManager.GetFog(map) : null;
            int fogRevision = checkpointManager != null ? checkpointManager.Revision : 0;
            if (dirty || View.Center != lastRenderedCenter || View.ViewSize != lastRenderedSize || lastFogRevision != fogRevision)
            {
                mapRenderer.Render(map, View, (int)screenSize.x, (int)screenSize.y, fog);
                mapImage.image = mapRenderer.Texture;
                lastRenderedCenter = View.Center;
                lastRenderedSize = View.ViewSize;
                dirty = false;
                lastFogRevision = fogRevision;
            }
            Vector2 uiSize = root.contentRect.size;
            if (uiSize.x <= 0 || uiSize.y <= 0) return;
            // Anchor the grid to map metres rather than to the screen.
            if (gridCellMeters <= 0f)
                gridCellMeters = 56f * openingMapViewHeight / uiSize.y;
            mapGrid.gridLength = gridCellMeters * uiSize.y / View.ViewSize.y;
            mapGrid.offset = View.ToScreen(Vector2.zero, uiSize);
            float radius = circleRadius * uiSize.y / screenSize.y;
            float iconRadius = checkpointIconSizePixels * .5f * uiSize.y / screenSize.y;
            for (int i = 0; i < checkpointIcons.Count; i++)
                PositionCircle(checkpointIcons[i], View.ToScreen(checkpointManager.Positions[i], uiSize), iconRadius);
            for (int i = 0; i < pinElements.Length; i++)
            {
                bool visible = i < View.Pins.Count;
                pinElements[i].style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (visible)
                {
                    pinElements[i].style.backgroundColor = View.GetPinColor(i);
                    PositionCircle(pinElements[i], View.ToScreen(View.Pins[i], uiSize), radius);
                }
            }
            var bootstrap = HeightMapPlayerSceneBootstrap.inst;
            PositionCircle(playerIcon, View.ToScreen(WorldToMap(bootstrap.robotObject.transform.position), uiSize), radius * 1.5f);
            float playerScale = 1.5f * openingMapViewHeight / View.ViewSize.y;
            playerIcon.style.scale = new Scale(new Vector3(playerScale, playerScale, 1f));
            playerIcon.style.rotate = new Rotate(openingPlayerBearing);
            PositionCircle(cursor, View.ToScreen(View.DisplayCursor, uiSize), radius);
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

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (active == this) active = null;
            closedFrame = Time.frameCount;
            panel.style.display = DisplayStyle.None;
            mapImage.image = null;
            checkpointLayer.Clear();
            checkpointIcons.Clear();
            checkpointManager = null;
            mapRenderer.Dispose(); mapRenderer = null;
            if (Time.timeScale == 0) Time.timeScale = savedTimeScale;
            UnityEngine.Cursor.lockState = savedCursorLock;
            UnityEngine.Cursor.visible = savedCursorVisible;
        }

        private void OnDisable()
        {
            Close();
            if (cursor != null) cursor.generateVisualContent -= DrawMapCursor;
            checkpointLayer?.RemoveFromHierarchy();
            playerIcon?.RemoveFromHierarchy();
            if (Instance == this) Instance = null;
        }
    }
}
