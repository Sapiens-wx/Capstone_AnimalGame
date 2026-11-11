using AnimalGame.MapTest;
using AnimalGame.RobotMap;
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

        private static WorldMapUI active;
        private static int closedFrame = -1;
        public static bool BlocksGameplay => active != null || closedFrame == Time.frameCount;
        public bool IsOpen { get; private set; }
        public float MinimumZoom { get; private set; }
        public float MaximumZoom { get; private set; }
        public float CurrentZoom { get; private set; }
        public MapViewState View { get; } = new MapViewState();

        private VisualElement root, cursor;
        private readonly VisualElement[] pinElements = new VisualElement[MapViewState.PinLimit];
        private Image mapImage;
        private Label controls;
        private WorldMapRenderer mapRenderer;
        private MapTestSceneController map;
        private Vector2 baseViewSize, lastSize;
        private float savedTimeScale;
        private CursorLockMode savedCursorLock;
        private bool savedCursorVisible, dirty;
        private Vector2 lastRenderedCenter, lastRenderedSize;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { active = null; closedFrame = -1; }

        private void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement.Q("world-map");
            mapImage = root.Q<Image>("map-image");
            mapImage.scaleMode = ScaleMode.StretchToFill;
            cursor = root.Q("map-cursor");
            controls = root.Q<Label>("map-controls");
            for (int i = 0; i < pinElements.Length; i++) pinElements[i] = root.Q("pin-" + i);
            root.style.display = DisplayStyle.None;
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
            Vector2 size = new Vector2(Screen.width, Screen.height);
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
            View.Snap(size, circleRadius * 1.4f);
            if ((keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
                || (pad != null && pad.buttonSouth.wasPressedThisFrame)) View.Submit();
            controls.text = pad != null
                ? "LS / D-pad  Move     RS  Zoom     A  Place / remove pin     B  Close"
                : "WASD  Move     Wheel  Zoom     Space  Place / remove pin     Esc  Close";
            RefreshVisuals(size);
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
            float aspect = Screen.width / (float)Screen.height;
            baseViewSize.y = Mathf.Max(footprint.y, footprint.x / aspect);
            baseViewSize.x = baseViewSize.y * aspect;
            RecalculateZoomLimits();
            CurrentZoom = Mathf.Clamp(defaultZoom, MinimumZoom, MaximumZoom);
            Bounds bounds = map.WorldBounds;
            Vector2 player = bootstrap.robotObject.transform.position;
            player = new Vector2((player.x - bounds.min.x) / bounds.size.x * map.MapSizeMeters.x,
                (player.y - bounds.min.y) / bounds.size.y * map.MapSizeMeters.y);
            View.Open(player, baseViewSize * CurrentZoom, map.MapSizeMeters);
            FitWholeMap();
            mapRenderer = new WorldMapRenderer(contourShader);
            savedTimeScale = Time.timeScale;
            savedCursorLock = UnityEngine.Cursor.lockState;
            savedCursorVisible = UnityEngine.Cursor.visible;
            Time.timeScale = 0;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = false;
            active = this;
            IsOpen = true;
            lastSize = new Vector2(Screen.width, Screen.height);
            root.style.display = DisplayStyle.Flex;
            dirty = true;
            RefreshVisuals(lastSize);
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
            if (dirty || View.Center != lastRenderedCenter || View.ViewSize != lastRenderedSize)
            {
                mapRenderer.Render(map, View, (int)screenSize.x, (int)screenSize.y);
                mapImage.image = mapRenderer.Texture;
                lastRenderedCenter = View.Center;
                lastRenderedSize = View.ViewSize;
                dirty = false;
            }
            Vector2 uiSize = root.contentRect.size;
            if (uiSize.x <= 0 || uiSize.y <= 0) return;
            float radius = circleRadius * uiSize.y / screenSize.y;
            for (int i = 0; i < pinElements.Length; i++)
            {
                bool visible = i < View.Pins.Count;
                pinElements[i].style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (visible) PositionCircle(pinElements[i], View.ToScreen(View.Pins[i], uiSize), radius);
            }
            PositionCircle(cursor, View.ToScreen(View.DisplayCursor, uiSize), radius);
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
            root.style.display = DisplayStyle.None;
            mapImage.image = null;
            mapRenderer.Dispose(); mapRenderer = null;
            if (Time.timeScale == 0) Time.timeScale = savedTimeScale;
            UnityEngine.Cursor.lockState = savedCursorLock;
            UnityEngine.Cursor.visible = savedCursorVisible;
        }

        private void OnDisable() => Close();
    }
}
