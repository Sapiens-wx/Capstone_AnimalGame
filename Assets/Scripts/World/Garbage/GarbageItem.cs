using AnimalGame.World;
using UnityEngine;

namespace AnimalGame.Garbage
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WorldInteraction))]
    [RequireComponent(typeof(ScannableInteractable))]
    public sealed class GarbageItem : MonoBehaviour
    {
        [SerializeField] private RecyclableSize size = RecyclableSize.Small;
        [Header("Scan outline flash")]
        [SerializeField, Min(.01f)] private float scanHitFlashDuration = .5f;
        [SerializeField, Min(0f)] private float scanEchoDuration = 3f;
        [Tooltip("Duration of one echo cycle, including its white pause and yellow flash.")]
        [SerializeField, Min(.01f)] private float scanEchoInterval = 1.5f;
        [SerializeField, Min(.01f)] private float scanEchoFlashDuration = .75f;
        [SerializeField, Range(0f, 1f)] private float scanEchoOpacity = .25f;
        public RecyclableSize Size => size;
        public WorldInteraction Interaction { get; private set; }
        private ScannableInteractable scan;
        private SpriteRenderer outlineRenderer;
        private Color originalOutlineColor;
        private float lastScanHitElapsed;
        private bool hasDirectScanHit;

        private void Awake()
        {
            Interaction = GetComponent<WorldInteraction>();
            // Covers existing scene objects and dynamically created garbage as well as prefabs.
            if (!TryGetComponent(out scan))
                scan = gameObject.AddComponent<ScannableInteractable>();
            if (size == RecyclableSize.Small) scan.EnableSmallAreaFill();
            outlineRenderer = scan.SpriteSource;
            if (outlineRenderer != null) originalOutlineColor = outlineRenderer.color;
            scan.ScanRevealed += HandleScanRevealed;
        }

        private void LateUpdate() => RefreshScanOutlineColor();
        private void OnDisable()
        {
            hasDirectScanHit = false;
            lastScanHitElapsed = 0f;
            ApplyOutlineTint(0f);
        }

        private void OnDestroy()
        {
            if (scan != null) scan.ScanRevealed -= HandleScanRevealed;
        }

        private void HandleScanRevealed(ScannableInteractable target)
        {
            if (!isActiveAndEnabled) return;
            lastScanHitElapsed = target.RevealElapsed;
            hasDirectScanHit = true;
            ApplyOutlineTint(1f);
        }

        internal void RefreshScanOutlineColor()
        {
            if (outlineRenderer == null) return;
            ApplyOutlineTint(ScanOutlineVisibility());
        }

        private float ScanOutlineVisibility()
        {
            if (!isActiveAndEnabled || scan.RevealRemaining <= 0f) return 0f;
            float elapsed = Mathf.Max(0f, scan.RevealElapsed - lastScanHitElapsed);
            float hitDuration = Mathf.Max(.01f, scanHitFlashDuration);
            if (hasDirectScanHit && elapsed < hitDuration)
                return 1f - Mathf.SmoothStep(0f, 1f, elapsed / hitDuration);

            float echoElapsed = elapsed - hitDuration;
            float interval = Mathf.Max(.01f, scanEchoInterval);
            if (echoElapsed < 0f || echoElapsed >= scanEchoDuration) return 0f;
            float phase = Mathf.Repeat(echoElapsed, interval);
            float duration = Mathf.Min(interval, Mathf.Max(.01f, scanEchoFlashDuration));
            float flashPhase = phase - (interval - duration);
            return flashPhase >= 0f
                ? Mathf.Sin(flashPhase / duration * Mathf.PI) * Mathf.Clamp01(scanEchoOpacity)
                : 0f;
        }

        private void ApplyOutlineTint(float visibility)
        {
            if (outlineRenderer == null) return;
            Color color = Color.Lerp(originalOutlineColor,
                originalOutlineColor * scan.AreaColor, visibility);
            // The sprite material already clips this artwork to the player view.
            // Leave icon children and opacity controlled by pulling/recycling alone.
            color.a = outlineRenderer.color.a;
            outlineRenderer.color = color;
        }
    }
}
