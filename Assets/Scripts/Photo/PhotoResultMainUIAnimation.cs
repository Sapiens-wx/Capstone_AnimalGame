using UnityEngine;

namespace AnimalGame.RobotMap
{
    /// <summary>Moves the live HUD with the photo zoom, relative to its original pose.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
    public sealed class PhotoResultMainUIAnimation : MonoBehaviour
    {
        private RectTransform rect;
        private CanvasGroup group;
        private Vector3 initialScale;
        private Vector3 initialPosition;
        private float initialAlpha;
        private float relativeScale = 1f;
        private Vector2 designSize;
        private Vector2 designDisplacement;
        private bool initialized;

        private void Awake() => Initialize();

        private void Initialize()
        {
            if (initialized) return;
            rect = GetComponent<RectTransform>();
            group = GetComponent<CanvasGroup>();
            initialScale = rect.localScale;
            initialPosition = rect.localPosition;
            initialAlpha = group.alpha;
            initialized = true;
        }

        /// <summary>Receives the same evaluated zoom and translation as zoom-content.</summary>
        public void SetZoomPose(float scale, Vector2 design, Vector2 displacement)
        {
            relativeScale = scale;
            designSize = design;
            designDisplacement = displacement;
        }

        /// <summary>Applies the supplied pose and evaluated Main UI window (0 opaque, 1 transparent).</summary>
        public void RevealAnimation(float value)
        {
            Initialize();
            rect.localScale = new Vector3(initialScale.x * relativeScale,
                initialScale.y * relativeScale, initialScale.z);

            Canvas canvas = GetComponentInParent<Canvas>();
            RectTransform parent = rect.parent as RectTransform;
            if (canvas != null && parent != null)
            {
                Canvas rootCanvas = canvas.rootCanvas;
                Camera camera = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                    ? null : rootCanvas.worldCamera;
                Rect viewport = rootCanvas.pixelRect;
                float fit = Mathf.Min(viewport.width / Mathf.Max(1f, designSize.x),
                    viewport.height / Mathf.Max(1f, designSize.y));
                Vector2 displacement = designDisplacement * fit;
                displacement.y = -displacement.y; // UI Toolkit's Y axis points down.
                Vector2 screenPivot = RectTransformUtility.WorldToScreenPoint(camera,
                    parent.TransformPoint(initialPosition));
                Vector2 target = viewport.center + displacement
                    + (screenPivot - viewport.center) * relativeScale;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, target,
                    camera, out Vector2 local))
                    rect.localPosition = new Vector3(local.x, local.y, initialPosition.z);
            }
            group.alpha = initialAlpha * (1f - Mathf.Clamp01(value));
        }

        public void Restore()
        {
            relativeScale = 1f;
            designDisplacement = Vector2.zero;
            if (!initialized) return;
            rect.localScale = initialScale;
            rect.localPosition = initialPosition;
            group.alpha = initialAlpha;
        }

        private void OnDisable() => Restore();
    }
}
