using System;
using System.Collections.Generic;
using AnimalGame.Animals;
using UnityEngine;
using UnityEngine.Serialization;

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
        [Tooltip("Optional filled outer envelope relative to the sprite bounds center, from -0.5 to 0.5. Empty derives a convex envelope from the sprite mesh. The scan wave uses the full envelope; line presentation rounds its corners inward. Interior transparent holes are intentionally filled.")]
        [SerializeField] private Vector2[] customNormalizedOutline;
        [SerializeField, Min(0f)] private float displayDuration = 7f;
        [SerializeField, Min(0f)] private float fadeOutDuration = .6f;
        [Tooltip("Smooth entrance when a hidden target is revealed. Repeated scans preserve the animation phase while refreshing lifetime and direction.")]
        [SerializeField, Min(0f)] private float fadeInDuration = .65f;
        [Tooltip("Seconds per interior-hatch pulse. Short bright peaks and deeper dim phases approach a gentle flicker. Pauses with the simulation.")]
        [SerializeField, Min(.1f)] private float breathingPeriod = 2f;
        [SerializeField, Range(0f, 1f)] private float breathingMinAlpha = .14f;
        [SerializeField, Range(0f, 1f)] private float breathingMaxAlpha = .62f;
        [SerializeField] private Color areaColor = new(212f / 255f, 184f / 255f, 84f / 255f, 1f);
        [SerializeField, Min(.5f)] private float lineWidthPixels = .9f;
        [Tooltip("Parallel-line spacing at the normal view scale. Fixed scene lines scale continuously with camera zoom.")]
        [SerializeField, Min(1f)] private float lineSpacingPixels = 7.7f;
        [Tooltip("Keep extra complete short rows on small garbage. Density is chosen once from its normal-view footprint, then cached in world space.")]
        [SerializeField] private bool useSmallAreaFill;
        [Tooltip("Preferred spacing in fixed normal-view pixels. The footprint may widen it to retain a few readable rows, or narrow it to the safe minimum.")]
        [SerializeField, Min(1f)] private float smallAreaLineSpacingPixels = 4f;
        [Tooltip("Screen angle captured when a scan is first presented. 45 degrees runs from lower left to upper right. The direction then stays fixed in world XY until another scan.")]
        [SerializeField, Range(-90f, 90f)] private float lineAngleDegrees = 45f;
        [Tooltip("Adds solid diamond particles along the boundary. A scan chooses their positions once; they share the reveal clock and their own gentle breathing.")]
        [FormerlySerializedAs("showDashedOutline")]
        [SerializeField] private bool showOutlineDots = true;
        [Tooltip("Tip-to-tip particle size in final screen pixels, independent of camera zoom.")]
        [SerializeField, Min(1f)] private float outlineDotSizePixels = 3.6f;
        [Tooltip("Preferred particle centre spacing at normal view scale. A scan slightly varies the spacing and starting phase, then caches world-space centres.")]
        [SerializeField, Min(1f)] private float outlineDotSpacingPixels = 13f;
        [SerializeField, Min(1f)] private float smallOutlineDotSpacingPixels = 9f;
        [SerializeField, Min(.1f)] private float outlineBreathingPeriod = 2.4f;
        [SerializeField, Range(0f, 1f)] private float outlineBreathingMinAlpha = .42f;
        [SerializeField, Range(0f, 1f)] private float outlineBreathingMaxAlpha = .5f;

        private WorldInteraction interaction;
        private Sprite cachedSprite;
        private bool outlineDirty = true;
        private readonly List<Vector2> normalizedOutline = new();
        private readonly List<Vector2> geometry = new();
        private readonly List<Vector3> waveOutline = new();
        private readonly List<Vector3> presentationOutline = new();
        private float revealRemaining, revealElapsed;
        private bool hasCapturedScanOrientation, isScanOrientationPending;
        private float capturedScanWorldAngleDegrees;
        private uint scanOrientationRevision;
        private uint outlinePatternSeed, outlinePatternSequence;

        public event Action<ScannableInteractable> ScanRevealed;
        public InteractableScanType ScanType => scanType;
        public float DisplayDuration => Mathf.Max(0f, displayDuration);
        public float FadeOutDuration => Mathf.Max(0f, fadeOutDuration);
        public float FadeInDuration => Mathf.Max(0f, fadeInDuration);
        public float BreathingPeriod => Mathf.Max(.1f, breathingPeriod);
        public float BreathingMinAlpha => Mathf.Clamp01(Mathf.Min(breathingMinAlpha, breathingMaxAlpha));
        public float BreathingMaxAlpha => Mathf.Clamp01(Mathf.Max(breathingMinAlpha, breathingMaxAlpha));
        public Color AreaColor => areaColor;
        public float LineWidthPixels => Mathf.Max(.5f, lineWidthPixels);
        public float LineSpacingPixels => Mathf.Max(1f, lineSpacingPixels);
        public bool UseSmallAreaFill => useSmallAreaFill;
        public float SmallAreaLineSpacingPixels => Mathf.Max(1f, smallAreaLineSpacingPixels);
        public float LineAngleDegrees => lineAngleDegrees;
        public bool ShowOutlineDots => showOutlineDots;
        public float OutlineDotSizePixels => Mathf.Max(1f, outlineDotSizePixels);
        public float OutlineDotSpacingPixels => Mathf.Max(1f, outlineDotSpacingPixels);
        public float SmallOutlineDotSpacingPixels => Mathf.Max(1f, smallOutlineDotSpacingPixels);
        public float OutlineBreathingPeriod => Mathf.Max(.1f, outlineBreathingPeriod);
        public float OutlineBreathingMinAlpha => Mathf.Clamp01(Mathf.Min(outlineBreathingMinAlpha, outlineBreathingMaxAlpha));
        public float OutlineBreathingMaxAlpha => Mathf.Clamp01(Mathf.Max(outlineBreathingMinAlpha, outlineBreathingMaxAlpha));
        public uint OutlinePatternSeed => outlinePatternSeed;
        public float RevealRemaining => CanScan ? revealRemaining : 0f;
        public float RevealElapsed => revealElapsed;
        public bool HasCapturedScanOrientation => hasCapturedScanOrientation;
        public bool IsScanOrientationPending => isScanOrientationPending;
        public float CapturedScanWorldAngleDegrees => capturedScanWorldAngleDegrees;
        public uint ScanOrientationRevision => scanOrientationRevision;
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

        private void ClearReveal()
        {
            revealRemaining = revealElapsed = 0f;
            hasCapturedScanOrientation = isScanOrientationPending = false;
            capturedScanWorldAngleDegrees = 0f;
            outlinePatternSeed = 0u;
        }

        /// <summary>Also enables the presentation on legacy and runtime-created small garbage.</summary>
        public void EnableSmallAreaFill() => useSmallAreaFill = true;

        public void Reveal(float duration = -1f)
        {
            if (!CanScan) return;
            bool wasVisible = revealRemaining > 0f;
            revealRemaining = Mathf.Max(0f, duration < 0f ? DisplayDuration : duration);
            // Repeated waves must not make an existing marker vanish and
            // restart either its entrance or its breathing phase.
            if (!wasVisible || revealRemaining <= 0f) revealElapsed = 0f;
            if (revealRemaining <= 0f) { ClearReveal(); return; }
            // Wait for the presenter's LateUpdate, after camera follow/shake.
            // Only real scan hits request a new direction; inheritance does not.
            hasCapturedScanOrientation = false;
            isScanOrientationPending = true;
            scanOrientationRevision++;
            outlinePatternSeed = NextOutlinePatternSeed();
            ScanRevealed?.Invoke(this);
        }

        private uint NextOutlinePatternSeed()
        {
            // Local deterministic state must not disturb gameplay's random
            // stream. Keep the sequence across disable/re-enable cycles.
            uint result;
            do
            {
                unchecked
                {
                    uint value = (uint)GetInstanceID() + ++outlinePatternSequence * 0x9E3779B9u;
                    value = (value ^ (value >> 16)) * 0x7FEB352Du;
                    value = (value ^ (value >> 15)) * 0x846CA68Bu;
                    result = value ^ (value >> 16);
                }
            } while (result == 0u || result == outlinePatternSeed);
            return result;
        }

        /// <summary>Locks this reveal to the final camera pose once, not to later player or camera rotation.</summary>
        public bool CaptureScanOrientation(float cameraWorldAngleDegrees)
        {
            if (!isScanOrientationPending || RevealRemaining <= 0f
                || float.IsNaN(cameraWorldAngleDegrees) || float.IsInfinity(cameraWorldAngleDegrees))
                return false;
            capturedScanWorldAngleDegrees = Mathf.DeltaAngle(0f, cameraWorldAngleDegrees + LineAngleDegrees);
            hasCapturedScanOrientation = true;
            isScanOrientationPending = false;
            return true;
        }

        public void InheritScanFrom(ScannableInteractable source)
        {
            if (source != null)
                InheritScanRemaining(source.RevealRemaining, source.RevealElapsed,
                    source.HasCapturedScanOrientation, source.CapturedScanWorldAngleDegrees,
                    source.OutlinePatternSeed);
        }

        /// <summary>Use a captured remaining duration when a source has already stopped interacting during disassembly.</summary>
        public void InheritScanRemaining(float remaining, float elapsed = -1f,
            bool hasCapturedOrientation = false, float capturedWorldAngleDegrees = 0f, uint outlineSeed = 0u)
        {
            if (!CanScan || remaining <= 0f) return;
            bool wasVisible = revealRemaining > 0f;
            revealRemaining = remaining;
            if (elapsed >= 0f) revealElapsed = elapsed;
            else if (!wasVisible) revealElapsed = 0f;
            hasCapturedScanOrientation = hasCapturedOrientation
                && !float.IsNaN(capturedWorldAngleDegrees) && !float.IsInfinity(capturedWorldAngleDegrees);
            capturedScanWorldAngleDegrees = hasCapturedScanOrientation
                ? Mathf.DeltaAngle(0f, capturedWorldAngleDegrees) : 0f;
            isScanOrientationPending = !hasCapturedScanOrientation;
            outlinePatternSeed = outlineSeed != 0u ? outlineSeed : NextOutlinePatternSeed();
            scanOrientationRevision++;
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
