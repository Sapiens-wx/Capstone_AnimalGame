using System;
using System.Collections.Generic;
using AnimalGame.Animals;
using UnityEngine;

namespace AnimalGame.World
{
    public enum InteractableScanType { StaticArea }

    /// <summary>A scan presentation attribute, independent of grabbing, pushing and collision behaviour.</summary>
    [DisallowMultipleComponent]
    public sealed class ScannableInteractable : MonoBehaviour
    {
        private static readonly HashSet<ScannableInteractable> active = new();
        public static IEnumerable<ScannableInteractable> Active => active;

        [SerializeField] private InteractableScanType scanType = InteractableScanType.StaticArea;
        [Tooltip("The main visual, including when it lives on a child Transform. Empty uses WorldInteraction's sprite, then a child sprite.")]
        [SerializeField] private SpriteRenderer spriteSource;
        [Tooltip("Optional filled outer envelope relative to the sprite bounds center, from -0.5 to 0.5. Empty derives a convex envelope from the sprite mesh. Interior transparent holes are intentionally filled.")]
        [SerializeField] private Vector2[] customNormalizedOutline;
        [SerializeField, Min(0f)] private float displayDuration = 7f;
        [SerializeField, Min(0f)] private float fadeOutDuration = .6f;
        [SerializeField] private Color areaColor = new(212f / 255f, 184f / 255f, 84f / 255f, 1f);
        [SerializeField, Min(.5f)] private float lineWidthPixels = 1.4f;
        [Tooltip("Grid spacing at the normal view scale. The fixed scene grid scales continuously with camera zoom.")]
        [SerializeField, Min(1f)] private float lineSpacingPixels = 15.4f;
        [Tooltip("Total clear length around each intersection at the normal view scale. Fixed scene endpoints retain this gap during camera motion.")]
        [SerializeField, Min(0f)] private float intersectionGapPixels = 5.6f;
        [Tooltip("Fixed direction in the world XY plane. The camera projects it with the scene, rather than keeping it attached to the screen.")]
        [SerializeField, Range(-90f, 90f)] private float lineAngleDegrees = -37f;

        private WorldInteraction interaction;
        private Sprite cachedSprite;
        private bool outlineDirty = true;
        private readonly List<Vector2> normalizedOutline = new();
        private readonly List<Vector2> geometry = new();
        private readonly List<Vector3> waveOutline = new();
        private readonly List<Vector3> presentationOutline = new();
        private float revealRemaining, revealElapsed;

        public event Action<ScannableInteractable> ScanRevealed;
        public InteractableScanType ScanType => scanType;
        public float DisplayDuration => Mathf.Max(0f, displayDuration);
        public float FadeOutDuration => Mathf.Max(0f, fadeOutDuration);
        public Color AreaColor => areaColor;
        public float LineWidthPixels => Mathf.Max(.5f, lineWidthPixels);
        public float LineSpacingPixels => Mathf.Max(1f, lineSpacingPixels);
        public float IntersectionGapPixels => Mathf.Max(0f, intersectionGapPixels);
        public float LineAngleDegrees => lineAngleDegrees;
        public float RevealRemaining => CanScan ? revealRemaining : 0f;
        public float RevealElapsed => revealElapsed;
        public bool CanScan
        {
            get
            {
                // Required components can be added in either order, and a
                // generic scan target may gain interaction behaviour at runtime.
                if (interaction == null) interaction = GetComponent<WorldInteraction>();
                return isActiveAndEnabled && gameObject.activeInHierarchy
                    && (interaction == null || (interaction.Available && interaction.Kind != WorldInteractionKind.None));
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry() => active.Clear();

        private void Awake() => interaction = GetComponent<WorldInteraction>();
        private void OnEnable()
        {
            interaction = GetComponent<WorldInteraction>();
            if (gameObject.scene.IsValid()) active.Add(this);
        }
        private void OnDisable()
        {
            active.Remove(this);
            ClearReveal();
        }
        private void OnDestroy() => active.Remove(this);
        private void OnValidate() => outlineDirty = true;
        private void Update() => TickReveal(Time.deltaTime);

        private void TickReveal(float deltaTime)
        {
            if (!CanScan) { ClearReveal(); return; }
            if (AnimalSimulation.IsPaused || revealRemaining <= 0f) return;
            float step = Mathf.Min(revealRemaining, Mathf.Max(0f, deltaTime));
            revealRemaining -= step;
            revealElapsed += step;
        }

        private void ClearReveal() { revealRemaining = 0f; revealElapsed = 0f; }

        public void Reveal(float duration = -1f)
        {
            if (!CanScan) return;
            revealRemaining = Mathf.Max(0f, duration < 0f ? DisplayDuration : duration);
            revealElapsed = 0f;
            if (revealRemaining > 0f) ScanRevealed?.Invoke(this);
        }

        public void InheritScanFrom(ScannableInteractable source)
        {
            if (source != null) InheritScanRemaining(source.RevealRemaining);
        }

        /// <summary>Use a captured remaining duration when a source has already stopped interacting during disassembly.</summary>
        public void InheritScanRemaining(float remaining)
        {
            if (CanScan && remaining > 0f) Reveal(remaining);
        }

        public bool TryGetWorldOutline(List<Vector3> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            if (!CanScan) return false;
            SpriteRenderer renderer = ResolveSprite();
            if (renderer == null || renderer.sprite == null) return false;
            Sprite sprite = renderer.sprite;
            if (outlineDirty || cachedSprite != sprite) CacheOutline(sprite);
            if (normalizedOutline.Count < 3) return false;

            // Simple sprites are mirrored around their pivot. Sliced/tiled sprites have
            // dynamic bounds, so scale their filled envelope to the current renderer size.
            Bounds bounds = renderer.drawMode == SpriteDrawMode.Simple ? sprite.bounds : renderer.localBounds;
            foreach (Vector2 point in normalizedOutline)
            {
                Vector3 local = bounds.center + new Vector3(point.x * bounds.size.x, point.y * bounds.size.y, 0f);
                if (renderer.flipX) local.x = -local.x;
                if (renderer.flipY) local.y = -local.y;
                destination.Add(renderer.transform.TransformPoint(local));
            }
            return true;
        }

        /// <summary>
        /// Supplies the actual world-oriented footprint around the visual's
        /// centre. Retaining rotation lets the area hint stay on the scene
        /// instead of acting like a billboard that only follows the centre.
        /// </summary>
        public bool TryGetPresentationFootprint(List<Vector2> destination, out Vector3 worldCenter)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            worldCenter = transform.position;
            if (!TryGetWorldOutline(presentationOutline)) return false;
            SpriteRenderer renderer = ResolveSprite();
            Bounds bounds = renderer.drawMode == SpriteDrawMode.Simple
                ? renderer.sprite.bounds : renderer.localBounds;
            Vector3 localCenter = bounds.center;
            if (renderer.flipX) localCenter.x = -localCenter.x;
            if (renderer.flipY) localCenter.y = -localCenter.y;
            worldCenter = renderer.transform.TransformPoint(localCenter);
            foreach (Vector3 point in presentationOutline)
                destination.Add((Vector2)(point - worldCenter));
            return destination.Count >= 3;
        }

        /// <summary>Tests the swept annulus against the filled envelope, including contact with any edge.</summary>
        public bool IntersectsScanWave(Vector2 origin, float innerRadius, float outerRadius)
        {
            if (!TryGetWorldOutline(waveOutline)) return false;
            float inner = Mathf.Max(0f, Mathf.Min(innerRadius, outerRadius));
            float outer = Mathf.Max(0f, Mathf.Max(innerRadius, outerRadius));
            float nearestSquared = float.PositiveInfinity, farthestSquared = 0f;
            bool positive = false, negative = false;
            for (int i = 0; i < waveOutline.Count; i++)
            {
                Vector2 a = waveOutline[i], b = waveOutline[(i + 1) % waveOutline.Count];
                Vector2 edge = b - a, offset = origin - a;
                float cross = Cross(edge, offset);
                positive |= cross > .000001f;
                negative |= cross < -.000001f;
                float t = edge.sqrMagnitude > .0000001f ? Mathf.Clamp01(Vector2.Dot(offset, edge) / edge.sqrMagnitude) : 0f;
                nearestSquared = Mathf.Min(nearestSquared, (origin - (a + edge * t)).sqrMagnitude);
                farthestSquared = Mathf.Max(farthestSquared, (a - origin).sqrMagnitude);
            }
            if (!(positive && negative)) nearestSquared = 0f; // Scan origin inside the filled hull.
            return nearestSquared <= outer * outer + .000001f
                && farthestSquared + .000001f >= inner * inner;
        }

        private SpriteRenderer ResolveSprite()
        {
            if (spriteSource != null) return spriteSource;
            if (interaction != null && interaction.SpriteSource != null) return interaction.SpriteSource;
            return GetComponentInChildren<SpriteRenderer>();
        }

        private void CacheOutline(Sprite sprite)
        {
            cachedSprite = sprite;
            outlineDirty = false;
            geometry.Clear();
            if (customNormalizedOutline != null && customNormalizedOutline.Length >= 3)
            {
                geometry.AddRange(customNormalizedOutline);
            }
            else
            {
                Bounds bounds = sprite.bounds;
                foreach (Vector2 point in sprite.vertices)
                    geometry.Add(new Vector2((point.x - bounds.center.x) / Mathf.Max(.000001f, bounds.size.x),
                        (point.y - bounds.center.y) / Mathf.Max(.000001f, bounds.size.y)));
            }
            BuildConvexHull(geometry, normalizedOutline);
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static void BuildConvexHull(List<Vector2> points, List<Vector2> hull)
        {
            hull.Clear();
            points.RemoveAll(point => float.IsNaN(point.x) || float.IsNaN(point.y)
                || float.IsInfinity(point.x) || float.IsInfinity(point.y));
            points.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            for (int i = points.Count - 1; i > 0; i--)
                if ((points[i] - points[i - 1]).sqrMagnitude < .000000001f) points.RemoveAt(i);
            if (points.Count < 3) return;
            foreach (Vector2 point in points)
            {
                while (hull.Count >= 2 && Cross(hull[hull.Count - 1] - hull[hull.Count - 2], point - hull[hull.Count - 1]) <= 0f)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(point);
            }
            int lowerCount = hull.Count;
            for (int i = points.Count - 2; i >= 0; i--)
            {
                Vector2 point = points[i];
                while (hull.Count > lowerCount && Cross(hull[hull.Count - 1] - hull[hull.Count - 2], point - hull[hull.Count - 1]) <= 0f)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(point);
            }
            hull.RemoveAt(hull.Count - 1);
        }
    }
}
