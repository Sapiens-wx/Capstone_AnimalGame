using AnimalGame.MapTest;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalGame.Tutorial
{
    /// <summary>
    /// Scene-owned waypoint presentation. Reads the final camera projection and live HUD ring without
    /// changing either, and never decides whether a lesson destination has been reached.
    /// </summary>
    [DefaultExecutionOrder(1100)]
    [DisallowMultipleComponent]
    public sealed class TutorialWaypointIndicator : MonoBehaviour
    {
        public enum WaypointMode { Hidden, Edge, InView }

        public readonly struct IndicatorLayout
        {
            public readonly WaypointMode Mode;
            public readonly Vector2 ScreenPosition;
            public readonly float RotationDegrees;

            public IndicatorLayout(WaypointMode mode, Vector2 screenPosition, float rotationDegrees)
            {
                Mode = mode;
                ScreenPosition = screenPosition;
                RotationDegrees = rotationDegrees;
            }
        }

        [SerializeField] private HeightMapPlayerSceneBootstrap bootstrap;
        [Tooltip("Use the cropped Target_0 sprite from Assets/Arts/UI/Target.png, whose tip points down.")]
        [SerializeField] private Sprite targetSprite;
        [SerializeField] private Vector2 iconSizeAt1080 = new Vector2(24f, 34f);
        [SerializeField, Min(0f)] private float targetGapAt1080 = 7f;
        [SerializeField, Min(0f)] private float bounceHeightAt1080 = 7f;
        [SerializeField, Min(.1f)] private float bouncePeriodSeconds = 1.1f;
        [SerializeField, Min(0f)] private float edgePaddingAt1080 = 6f;

        private GameObject canvasObject;
        private Canvas canvas;
        private RectTransform canvasRect;
        private Image icon;
        private Vector2 targetMapMeters;
        private bool hasTarget;
        private bool visible = true;
        private bool showDuringPhotoMode;
        private float bounceStartTime;
        private IndicatorLayout currentLayout;

        public WaypointMode CurrentMode => currentLayout.Mode;
        public Vector2 CurrentScreenPosition => currentLayout.ScreenPosition;
        public float CurrentRotationDegrees => currentLayout.RotationDegrees;
        public IndicatorLayout CurrentLayout => currentLayout;
        public bool HasTarget => hasTarget;
        public Vector2 TargetMapMeters => targetMapMeters;
        public Canvas OverlayCanvas => canvas;
        public Image Image => icon;

        public void Configure(HeightMapPlayerSceneBootstrap sceneBootstrap, Sprite sprite)
        {
            bootstrap = sceneBootstrap;
            targetSprite = sprite;
            if (icon != null) icon.sprite = sprite;
            RefreshPresentation();
        }

        public void SetTarget(Vector2 logicalMapMeters)
        {
            if (!Finite(logicalMapMeters)) { ClearTarget(); return; }
            // A controller may refresh the same destination every frame; preserve its bounce phase.
            if (!hasTarget || (logicalMapMeters - targetMapMeters).sqrMagnitude > .000001f)
                bounceStartTime = Time.time;
            targetMapMeters = logicalMapMeters;
            hasTarget = true;
            RefreshPresentation();
        }

        public void ClearTarget()
        {
            hasTarget = false;
            Hide();
        }

        public void SetVisible(bool value)
        {
            visible = value;
            RefreshPresentation();
        }

        // Bird teaching shares the normal ring/hover target while the native camera is open.
        // Destination markers retain their existing default of hiding in photo mode.
        public void SetPhotoModeVisible(bool value)
        { showDuringPhotoMode = value; RefreshPresentation(); }

        private void OnEnable() => Canvas.willRenderCanvases += RefreshPresentation;

        private void OnDisable()
        {
            Canvas.willRenderCanvases -= RefreshPresentation;
            Hide();
        }

        private void LateUpdate() => RefreshPresentation();

        private void RefreshPresentation()
        {
            if (!isActiveAndEnabled || !visible || !hasTarget || targetSprite == null
                || gameObject.scene.path != TutorialDrivingLesson.ScenePath
                || bootstrap == null || bootstrap.gameObject.scene != gameObject.scene
                || bootstrap.map == null || !bootstrap.map.HasGeneratedMap || bootstrap.mapCamera == null
                || Time.timeScale <= 0f || AnimalGame.MainUI.MainUI.BlocksGameplay
                || (bootstrap.photoMode != null && bootstrap.photoMode.IsActive && !showDuringPhotoMode)
                || (bootstrap.photoResultView != null && bootstrap.photoResultView.IsShowing))
            {
                Hide();
                return;
            }

            AnimalGame.MainUI.MainUI hud = bootstrap.mainUiObject != null
                ? bootstrap.mainUiObject.GetComponent<AnimalGame.MainUI.MainUI>() : null;
            if (hud == null || !hud.isActiveAndEnabled
                || !hud.TryGetRingScreenGeometry(out Vector2 center, out float radius))
            {
                Hide();
                return;
            }

            // Execution is after camera follow/shake/projection; the render callback also catches
            // a HUD geometry update later in the frame. No substitute camera pose is reconstructed.
            Vector3 worldTarget = bootstrap.map.MapPositionToWorld(targetMapMeters);
            Vector3 projected = bootstrap.mapCamera.WorldToScreenPoint(worldTarget);
            float fit = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
            float maxBounce = bounceHeightAt1080 * fit;
            float phase = (Time.time - bounceStartTime) / Mathf.Max(.1f, bouncePeriodSeconds);
            float bounce = (.5f - .5f * Mathf.Cos(phase * Mathf.PI * 2f)) * maxBounce;
            Vector2 size = iconSizeAt1080 * fit;
            currentLayout = CalculateLayout(projected, center, radius, size,
                targetGapAt1080 * fit, bounce, maxBounce, edgePaddingAt1080 * fit,
                projected.z >= bootstrap.mapCamera.nearClipPlane);
            if (currentLayout.Mode == WaypointMode.Hidden) { Hide(); return; }

            EnsureCanvas();
            icon.rectTransform.sizeDelta = size;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,
                currentLayout.ScreenPosition, null, out Vector2 localPoint);
            icon.rectTransform.anchoredPosition = localPoint;
            icon.rectTransform.localRotation = Quaternion.Euler(0f, 0f, currentLayout.RotationDegrees);
            icon.enabled = true;
        }

        /// <summary>
        /// Pure screen-space placement, with screen Y pointing up and the source sprite pointing down.
        /// A destination switches to InView only when its complete hover/bounce rectangle fits inside
        /// the circle. This prevents clipping, phase-dependent mode flicker, and lateral displacement
        /// of an arrow that should point exactly at the destination.
        /// </summary>
        public static IndicatorLayout CalculateLayout(Vector2 projectedTarget, Vector2 ringCenter,
            float ringRadius, Vector2 iconSize, float gap, float bounceOffset, float maxBounce,
            float edgePadding, bool targetInFront = true)
        {
            if (!Finite(projectedTarget) || !Finite(ringCenter) || !Finite(iconSize)
                || !Finite(ringRadius) || !Finite(gap) || !Finite(bounceOffset)
                || !Finite(maxBounce) || !Finite(edgePadding)
                || iconSize.x <= 0f || iconSize.y <= 0f) return default;

            float radius = ringRadius - Mathf.Max(0f, edgePadding);
            Vector2 half = iconSize * .5f;
            if (radius <= half.magnitude) return default;
            gap = Mathf.Max(0f, gap);
            maxBounce = Mathf.Max(0f, maxBounce);
            bounceOffset = Mathf.Clamp(bounceOffset, 0f, maxBounce);
            Vector2 hoverCenter = projectedTarget + Vector2.up * (gap + (iconSize.y + maxBounce) * .5f);
            Vector2 hoverHalf = new Vector2(half.x, (iconSize.y + maxBounce) * .5f);
            if (targetInFront && RectInsideCircle(hoverCenter - ringCenter, hoverHalf, radius))
                return new IndicatorLayout(WaypointMode.InView,
                    projectedTarget + Vector2.up * (gap + half.y + bounceOffset), 0f);

            Vector2 direction = projectedTarget - ringCenter;
            if (!targetInFront) direction = -direction;
            direction = direction.sqrMagnitude > .000001f ? direction.normalized : Vector2.up;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg + 90f;
            float radialDistance = SafeRadialDistance(direction, half, angle, radius);
            return new IndicatorLayout(WaypointMode.Edge,
                ringCenter + direction * radialDistance, angle);
        }

        private static bool RectInsideCircle(Vector2 center, Vector2 half, float radius)
        {
            // The furthest corner is obtained independently along each axis.
            Vector2 farthest = new Vector2(Mathf.Abs(center.x) + half.x, Mathf.Abs(center.y) + half.y);
            return farthest.sqrMagnitude <= radius * radius;
        }

        private static float SafeRadialDistance(Vector2 direction, Vector2 half, float angle, float radius)
        {
            // Solve |r * direction + rotatedCorner| <= radius for each of the four corners.
            // Using the complete rectangle also covers non-square sprites at oblique edge angles.
            float radians = angle * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians), sine = Mathf.Sin(radians);
            float distance = radius;
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                {
                    Vector2 corner = new Vector2(x * half.x, y * half.y);
                    Vector2 rotated = new Vector2(corner.x * cosine - corner.y * sine,
                        corner.x * sine + corner.y * cosine);
                    float along = Vector2.Dot(direction, rotated);
                    float limit = -along + Mathf.Sqrt(Mathf.Max(0f,
                        along * along + radius * radius - rotated.sqrMagnitude));
                    distance = Mathf.Min(distance, limit);
                }
            return Mathf.Max(0f, distance);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);

        private void EnsureCanvas()
        {
            if (canvasObject != null) return;
            canvasObject = new GameObject("Tutorial Waypoint (Runtime)", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            canvasObject.transform.SetParent(transform, false);
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) canvasObject.layer = uiLayer;
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 32;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            CanvasGroup group = canvasObject.GetComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            canvasRect = canvasObject.GetComponent<RectTransform>();
            var imageObject = new GameObject("Destination Triangle", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            imageObject.layer = canvasObject.layer;
            imageObject.transform.SetParent(canvasRect, false);
            icon = imageObject.GetComponent<Image>();
            icon.sprite = targetSprite;
            icon.type = Image.Type.Simple;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.maskable = false;
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = icon.rectTransform.pivot = Vector2.one * .5f;
            // The supplied cropped sprite contains its own gold; never tint or mutate its texture.
            icon.color = Color.white;
        }

        private void Hide()
        {
            currentLayout = default;
            if (icon != null) icon.enabled = false;
        }

        private void OnDestroy()
        {
            Canvas.willRenderCanvases -= RefreshPresentation;
            // Only this component's canvas is owned. Sprite, camera, map and HUD remain untouched.
            if (canvasObject == null) return;
            if (Application.isPlaying) Destroy(canvasObject);
            else DestroyImmediate(canvasObject);
        }
    }
}
