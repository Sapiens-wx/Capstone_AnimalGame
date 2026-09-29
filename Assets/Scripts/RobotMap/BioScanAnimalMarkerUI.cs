using System.Collections.Generic;
using AnimalGame.Animals;
using AnimalGame.Discovery;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace AnimalGame.RobotMap
{
    /// <summary>
    /// Tracks scan hits independently from discovery and draws their markers
    /// in screen space, including outside the player's circular view.
    /// </summary>
    [DefaultExecutionOrder(320)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Animal Game/Robot/Biological Scan Animal Markers")]
    public sealed class BioScanAnimalMarkerUI : MonoBehaviour
    {
        [Header("Marker Artwork")]
        [Tooltip("Draws an antialiased, curved four-point star: hollow before first sight and solid after confirmation.")]
        [SerializeField] private Shader markerShader;
        [SerializeField] private Color markerColor =
            new Color32(212, 184, 84, 255);
        [Tooltip("Maximum UI frame size in screen pixels. The star fills 45% of its height and shrinks with the animal's projected body size.")]
        [SerializeField, Min(1f)] private float markerSizePixels = 36f;
        [Tooltip("Size of the marker frame relative to the body's shortest projected sprite axis. A value of 0.5 keeps the visible star roughly one third of the visible body width.")]
        [SerializeField, Range(0.1f, 0.75f)] private float markerBodySizeFraction = 0.75f;
        [SerializeField, Min(0.1f)] private float markerDuration = 7f;
        [Tooltip("Fade to transparent during the final seconds of the marker lifetime, without extending it.")]
        [SerializeField, Min(0f)] private float markerFadeOutDuration = 0.6f;

        [Header("Marker Breathing")]
        [SerializeField] private bool enableBreathing = true;
        [Tooltip("Seconds for one smooth bright-to-dim-to-bright cycle. Each scan starts at the bright peak.")]
        [SerializeField, Min(0.2f)] private float breathingPeriod = 1.2f;
        [Tooltip("Scale variation around the configured marker size. 0.08 means 92% to 108%.")]
        [SerializeField, Range(0f, 0.2f)] private float breathingScaleAmplitude = 0.08f;
        [Tooltip("Minimum fraction of the configured opacity, keeping the marker visible during each breath.")]
        [SerializeField, Range(0f, 1f)] private float breathingMinimumOpacity = 0.2f;

        [Header("Scan Hit Burst")]
        [SerializeField] private bool enableScanHitBurst = true;
        [SerializeField, Min(0.1f)] private float scanHitBurstDuration = 0.7f;
        [Tooltip("Maximum screen-space radius of the short burst at the animal's scan-hit position.")]
        [SerializeField, Min(4f)] private float scanHitBurstRadiusPixels = 48f;
        [SerializeField, Range(4, 16)] private int scanHitBurstRayCount = 12;

        [Header("Scan Movement Trail")]
        [SerializeField] private bool enableMovementTrail = true;
        [FormerlySerializedAs("trailSegmentLifetime")]
        [SerializeField, Min(0.1f)] private float trailPointLifetime = 1.8f;
        [Tooltip("Minimum world-space distance between footprint samples. Stationary animals leave no trail.")]
        [FormerlySerializedAs("trailSegmentSpacing")]
        [SerializeField, Min(0.02f)] private float trailPointSpacing = 0.25f;
        [Tooltip("Seconds between footprint samples. Larger values make the trail quieter and sparser.")]
        [SerializeField, Min(0.05f)] private float trailSpawnInterval = 0.24f;
        [Tooltip("Screen-space width of each softly edged footprint segment.")]
        [FormerlySerializedAs("trailWidthPixels")]
        [SerializeField, Min(1f)] private float trailPointSizePixels = 3.2f;
        [SerializeField, Range(0f, 1f)] private float trailOpacity = 0.5f;

        private static readonly int FilledProperty = Shader.PropertyToID("_Filled");

        private sealed class AnimalMarker
        {
            public DiscoverableEntity Entity;
            public AnimalPhotoSubject Subject;
            public RectTransform Root;
            public Image Icon;
            public float Remaining;
            public float BreathingElapsed;
            public bool HasBeenSeen;
            public bool JustScanned;
            public Vector3 TrailAnchor;
            public Vector3 PreviousTrailPosition;
            public float TrailSampleElapsed;
        }

        private readonly Dictionary<DiscoverableEntity, AnimalMarker> markers =
            new Dictionary<DiscoverableEntity, AnimalMarker>();
        private readonly List<DiscoverableEntity> destroyedEntities =
            new List<DiscoverableEntity>();
        private ScanChargeUI scanInput;
        private Camera worldCamera;
        private Canvas markerCanvas;
        private RectTransform canvasRect;
        private Material hollowMarkerMaterial;
        private Material solidMarkerMaterial;
        private BioScanAnimalTrailGraphic trailGraphic;
        private BioScanAnimalBurstGraphic burstGraphic;

        public void Initialize(ScanChargeUI input, Camera camera)
        {
            scanInput = input;
            worldCamera = camera;
            if (markerCanvas != null)
                ConfigureCanvasSorting();
        }

        public void ShowScannedAnimal(DiscoverableEntity entity)
        {
            if (entity == null || entity.Kind != DiscoverableKind.Animal
                || !entity.IsScannable || !EnsureCanvas())
                return;

            if (!markers.TryGetValue(entity, out AnimalMarker marker))
            {
                marker = CreateMarker(entity);
                markers.Add(entity, marker);
            }

            // Keep confirmation across repeat scans, including after expiry.
            if (marker.Remaining <= 0f)
                ResetTrailPosition(marker, entity.transform.position);
            marker.Remaining = Mathf.Max(0.1f, markerDuration);
            marker.BreathingElapsed = 0f;
            marker.JustScanned = true;
            if (enableScanHitBurst)
            {
                Vector3 center = marker.Subject != null
                                 && marker.Subject.TryGetWorldBounds(out Bounds bounds)
                    ? bounds.center : entity.transform.position;
                burstGraphic.Play(entity.GetInstanceID(), center, scanHitBurstDuration,
                    scanHitBurstRadiusPixels, scanHitBurstRayCount);
            }
        }

        private void LateUpdate()
        {
            bool paused = AnimalSimulation.IsPaused || Time.timeScale <= 0f;
            TickMarkers(paused ? 0f : Time.deltaTime, !paused);
        }

        private void TickMarkers(float deltaTime, bool canObserve)
        {
            if (worldCamera == null)
                worldCamera = Camera.main;
            if (burstGraphic != null)
            {
                if (!enableScanHitBurst)
                    burstGraphic.ClearBursts();
                burstGraphic.Tick(deltaTime, worldCamera, markerColor);
            }
            // Existing footprints finish their own lifetime even if the last
            // animal has expired, disappeared or been destroyed.
            if (trailGraphic != null)
            {
                if (!enableMovementTrail)
                    trailGraphic.ClearFootprints();
                Color trailColor = markerColor;
                trailColor.a *= trailOpacity;
                trailGraphic.Tick(deltaTime, worldCamera, trailColor, trailPointSizePixels);
            }
            if (markers.Count == 0)
                return;

            bool hasView = worldCamera != null && worldCamera.isActiveAndEnabled;
            bool hasPlayerView = hasView && scanInput != null
                                 && scanInput.isActiveAndEnabled;
            Vector2 viewCenter = hasPlayerView
                ? scanInput.GetUiCenterScreenPoint() : Vector2.zero;
            float viewRadius = hasPlayerView
                ? scanInput.GetUiRingScreenRadiusPixels() : 0f;

            destroyedEntities.Clear();
            foreach (AnimalMarker marker in markers.Values)
            {
                if (marker.Entity == null)
                {
                    if (marker.Root != null)
                        Destroy(marker.Root.gameObject);
                    destroyedEntities.Add(marker.Entity);
                    continue;
                }

                float trailDeltaTime = marker.JustScanned
                    ? 0f : Mathf.Min(deltaTime, marker.Remaining);
                bool present = marker.Entity.IsScannable;
                UpdateMovementTrail(marker, deltaTime, trailDeltaTime,
                    canObserve && present && enableMovementTrail);

                if (!marker.JustScanned)
                {
                    marker.Remaining = Mathf.Max(0f, marker.Remaining - deltaTime);
                    if (marker.Remaining > 0f)
                    {
                        // Share the lifetime's gameplay clock so pauses freeze
                        // both animation and expiry, even when off screen.
                        marker.BreathingElapsed = Mathf.Repeat(
                            marker.BreathingElapsed + deltaTime,
                            Mathf.Max(0.2f, breathingPeriod));
                    }
                }
                marker.JustScanned = false;

                if (!marker.Entity.isActiveAndEnabled)
                    marker.Remaining = 0f;
                if (marker.Remaining <= 0f && marker.HasBeenSeen)
                {
                    marker.Root.gameObject.SetActive(false);
                    continue;
                }
                // Continue recording first sight even after the seven-second
                // marker expires. The next scan then uses the confirmed icon.
                if (!marker.HasBeenSeen && canObserve && hasPlayerView && present
                    && IsBodyInPlayerView(marker.Subject, viewCenter, viewRadius))
                {
                    marker.HasBeenSeen = true;
                }

                bool visible = hasView && present && marker.Remaining > 0f;
                Vector3 screenPoint = Vector3.zero;
                if (visible)
                {
                    Vector3 center = marker.Subject != null
                                     && marker.Subject.TryGetWorldBounds(out Bounds bounds)
                        ? bounds.center : marker.Entity.transform.position;
                    screenPoint = worldCamera.WorldToScreenPoint(center);
                    visible = screenPoint.z >= worldCamera.nearClipPlane
                              && screenPoint.z <= worldCamera.farClipPlane
                              && worldCamera.pixelRect.Contains(screenPoint);
                }

                marker.Root.gameObject.SetActive(visible);
                if (!visible)
                    continue;

                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect, screenPoint, null, out Vector2 localPoint);
                marker.Root.anchoredPosition = localPoint;
                marker.Root.sizeDelta = Vector2.one * GetMarkerSizePixels(marker);
                marker.Icon.material = marker.HasBeenSeen
                    ? solidMarkerMaterial : hollowMarkerMaterial;
                ApplyBreathing(marker);
            }

            foreach (DiscoverableEntity entity in destroyedEntities)
                markers.Remove(entity);
        }

        private void ApplyBreathing(AnimalMarker marker)
        {
            float scale = 1f;
            Color color = markerColor;
            float fadeDuration = Mathf.Min(markerFadeOutDuration, markerDuration);
            bool fading = fadeDuration > 0f && marker.Remaining < fadeDuration;
            // Hold the breathing pose at the fade boundary to avoid one last
            // pulse brightening the marker while it is supposed to disappear.
            float breathingTime = marker.BreathingElapsed
                - (fading ? fadeDuration - marker.Remaining : 0f);
            if (enableBreathing)
            {
                float breath = 0.5f + 0.5f * Mathf.Cos(
                    breathingTime / Mathf.Max(0.2f, breathingPeriod)
                    * Mathf.PI * 2f);
                scale += Mathf.Lerp(-breathingScaleAmplitude,
                    breathingScaleAmplitude, breath);
                color.a *= Mathf.Lerp(breathingMinimumOpacity, 1f, breath);
            }
            if (fading)
                color.a *= Mathf.SmoothStep(0f, 1f, marker.Remaining / fadeDuration);

            marker.Root.localScale = Vector3.one * scale;
            marker.Icon.color = color;
        }

        private static void ResetTrailPosition(AnimalMarker marker, Vector3 position)
        {
            marker.TrailAnchor = position;
            marker.PreviousTrailPosition = position;
            marker.TrailSampleElapsed = 0f;
        }

        private void UpdateMovementTrail(AnimalMarker marker, float deltaTime,
            float activeTime, bool canEmit)
        {
            Vector3 position = marker.Entity.transform.position;
            Vector3 movement = position - marker.PreviousTrailPosition;
            float spacing = Mathf.Max(0.02f, trailPointSpacing);
            // Reset across pauses, absence, rescans after expiry and teleports;
            // these must not draw a line connecting unrelated positions.
            if (!canEmit || activeTime <= 0f || trailGraphic == null
                || movement.magnitude > Mathf.Max(2f, spacing * 10f))
            {
                ResetTrailPosition(marker, position);
                return;
            }

            // Sample at a slower, fixed cadence rather than on every distance
            // step. Interpolation preserves that cadence at lower frame rates.
            // activeTime clips samples at the seven-second scan boundary.
            float interval = Mathf.Max(0.05f, trailSpawnInterval);
            float elapsed = marker.TrailSampleElapsed + activeTime;
            // Use the same accumulated clock for emission and the remainder.
            // Subtracting phase from interval separately can round past a
            // frame boundary, skip a sample, then wrap its phase back to zero.
            int sampleCount = Mathf.FloorToInt((elapsed + 0.000001f) / interval);
            for (int sample = 1; sample <= sampleCount; sample++)
            {
                float sampleTime = Mathf.Clamp(sample * interval - marker.TrailSampleElapsed,
                    0f, activeTime);
                Vector3 point = Vector3.Lerp(marker.PreviousTrailPosition, position,
                    sampleTime / deltaTime);
                if ((point - marker.TrailAnchor).sqrMagnitude < spacing * spacing)
                    continue;
                trailGraphic.AddFootprint(marker.TrailAnchor, point,
                    trailPointLifetime, deltaTime - sampleTime);
                marker.TrailAnchor = point;
            }
            marker.TrailSampleElapsed = Mathf.Max(0f, elapsed - sampleCount * interval);
            marker.PreviousTrailPosition = position;
        }

        private float GetMarkerSizePixels(AnimalMarker marker)
        {
            float bodySizePixels = float.PositiveInfinity;
            if (marker.Subject != null && marker.Subject.BodyRenderers != null)
            {
                foreach (SpriteRenderer body in marker.Subject.BodyRenderers)
                {
                    if (body == null || body.sprite == null)
                        continue;

                    // Project the sprite's own axes instead of its world AABB:
                    // rotating the animal or camera must not enlarge the icon.
                    // Include hidden body renderers so off-view markers retain
                    // the same size as the animal they belong to.
                    Bounds bounds = body.localBounds;
                    Vector3 horizontal = Vector3.right * bounds.extents.x;
                    Vector3 vertical = Vector3.up * bounds.extents.y;
                    Vector2 left = worldCamera.WorldToScreenPoint(
                        body.transform.TransformPoint(bounds.center - horizontal));
                    Vector2 right = worldCamera.WorldToScreenPoint(
                        body.transform.TransformPoint(bounds.center + horizontal));
                    Vector2 bottom = worldCamera.WorldToScreenPoint(
                        body.transform.TransformPoint(bounds.center - vertical));
                    Vector2 top = worldCamera.WorldToScreenPoint(
                        body.transform.TransformPoint(bounds.center + vertical));
                    bodySizePixels = Mathf.Min(bodySizePixels,
                        Mathf.Min(Vector2.Distance(left, right),
                            Vector2.Distance(bottom, top)));
                }
            }

            return Mathf.Min(markerSizePixels,
                bodySizePixels * markerBodySizeFraction);
        }

        private bool IsBodyInPlayerView(
            AnimalPhotoSubject subject, Vector2 center, float radius)
        {
            if (subject == null || subject.BodyRenderers == null)
                return false;

            foreach (SpriteRenderer body in subject.BodyRenderers)
            {
                if (body == null || body.sprite == null || !body.enabled
                    || !body.gameObject.activeInHierarchy || body.color.a <= 0.001f
                    || (worldCamera.cullingMask & (1 << body.gameObject.layer)) == 0)
                    continue;

                Bounds bounds = body.bounds;
                Vector2 minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
                Vector2 maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
                bool inFront = true;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((corner & 1) == 0 ? -1f : 1f,
                            (corner & 2) == 0 ? -1f : 1f,
                            (corner & 4) == 0 ? -1f : 1f));
                    Vector3 screen = worldCamera.WorldToScreenPoint(point);
                    if (screen.z < worldCamera.nearClipPlane
                        || screen.z > worldCamera.farClipPlane)
                    {
                        inFront = false;
                        break;
                    }
                    minimum = Vector2.Min(minimum, screen);
                    maximum = Vector2.Max(maximum, screen);
                }
                if (!inFront)
                    continue;

                // Intersect with the actual camera viewport before testing the
                // player's circle, so off-screen bodies cannot confirm a hit.
                Rect viewport = worldCamera.pixelRect;
                minimum = Vector2.Max(minimum, viewport.min);
                maximum = Vector2.Min(maximum, viewport.max);
                if (minimum.x >= maximum.x || minimum.y >= maximum.y)
                    continue;
                Vector2 closest = new Vector2(
                    Mathf.Clamp(center.x, minimum.x, maximum.x),
                    Mathf.Clamp(center.y, minimum.y, maximum.y));
                if ((closest - center).sqrMagnitude < radius * radius)
                    return true;
            }
            return false;
        }

        private bool EnsureCanvas()
        {
            if (markerCanvas != null)
                return true;
            Shader shader = markerShader != null
                ? markerShader : Shader.Find("UI/Biological Scan Animal Marker");
            if (shader == null)
            {
                Debug.LogError("Biological scan markers are missing their UI shader.", this);
                return false;
            }
            hollowMarkerMaterial = new Material(shader)
            {
                name = "Runtime Hollow Biological Scan Star",
                hideFlags = HideFlags.HideAndDontSave
            };
            hollowMarkerMaterial.SetFloat(FilledProperty, 0f);
            solidMarkerMaterial = new Material(shader)
            {
                name = "Runtime Solid Biological Scan Star",
                hideFlags = HideFlags.HideAndDontSave
            };
            solidMarkerMaterial.SetFloat(FilledProperty, 1f);

            var canvasObject = new GameObject("Biological Scan Animal Markers",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.layer = LayerMask.NameToLayer("UI");
            SceneManager.MoveGameObjectToScene(canvasObject, gameObject.scene);
            canvasRect = canvasObject.GetComponent<RectTransform>();
            markerCanvas = canvasObject.GetComponent<Canvas>();
            markerCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.GetComponent<CanvasScaler>().uiScaleMode =
                CanvasScaler.ScaleMode.ConstantPixelSize;
            var trailObject = new GameObject("Scanned Animal Movement Trails",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(BioScanAnimalTrailGraphic));
            trailObject.layer = canvasObject.layer;
            var trailRect = trailObject.GetComponent<RectTransform>();
            trailRect.SetParent(canvasRect, false);
            trailRect.anchorMin = Vector2.zero;
            trailRect.anchorMax = Vector2.one;
            trailRect.offsetMin = trailRect.offsetMax = Vector2.zero;
            trailGraphic = trailObject.GetComponent<BioScanAnimalTrailGraphic>();
            trailGraphic.raycastTarget = false;
            trailGraphic.maskable = false;
            var burstObject = new GameObject("Scanned Animal Hit Bursts",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(BioScanAnimalBurstGraphic));
            burstObject.layer = canvasObject.layer;
            var burstRect = burstObject.GetComponent<RectTransform>();
            burstRect.SetParent(canvasRect, false);
            burstRect.anchorMin = Vector2.zero;
            burstRect.anchorMax = Vector2.one;
            burstRect.offsetMin = burstRect.offsetMax = Vector2.zero;
            burstGraphic = burstObject.GetComponent<BioScanAnimalBurstGraphic>();
            burstGraphic.raycastTarget = false;
            burstGraphic.maskable = false;
            ConfigureCanvasSorting();
            canvasObject.SetActive(isActiveAndEnabled);
            return true;
        }

        private void ConfigureCanvasSorting()
        {
            Canvas mainCanvas = scanInput != null
                ? scanInput.GetComponentInParent<Canvas>() : null;
            if (mainCanvas != null)
                mainCanvas = mainCanvas.rootCanvas;
            markerCanvas.sortingLayerID = mainCanvas != null
                ? mainCanvas.sortingLayerID : 0;
            // Above world rendering and below the existing HUD/photo overlays.
            markerCanvas.sortingOrder = mainCanvas != null
                ? mainCanvas.sortingOrder - 1 : 29;
        }

        private AnimalMarker CreateMarker(DiscoverableEntity entity)
        {
            Image icon = CreateImage("Scanned Animal Star", canvasRect);
            icon.rectTransform.sizeDelta = Vector2.one * markerSizePixels;
            icon.gameObject.SetActive(false);
            return new AnimalMarker
            {
                Entity = entity,
                Subject = entity.GetComponent<AnimalPhotoSubject>(),
                Root = icon.rectTransform,
                Icon = icon
            };
        }

        private Image CreateImage(string name, RectTransform parent)
        {
            var imageObject = new GameObject(name,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imageObject.layer = LayerMask.NameToLayer("UI");
            var rect = imageObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
            Image image = imageObject.GetComponent<Image>();
            image.material = hollowMarkerMaterial;
            image.color = markerColor;
            image.raycastTarget = false;
            image.maskable = false;
            return image;
        }

        private void OnEnable()
        {
            if (markerCanvas != null)
                markerCanvas.gameObject.SetActive(true);
        }

        private void OnDisable()
        {
            if (markerCanvas != null)
                markerCanvas.gameObject.SetActive(false);
            foreach (AnimalMarker marker in markers.Values)
                marker.Remaining = 0f;
            if (trailGraphic != null)
                trailGraphic.ClearFootprints();
            if (burstGraphic != null)
                burstGraphic.ClearBursts();
        }

        private void OnDestroy()
        {
            if (markerCanvas != null)
                Destroy(markerCanvas.gameObject);
            if (hollowMarkerMaterial != null)
                Destroy(hollowMarkerMaterial);
            if (solidMarkerMaterial != null)
                Destroy(solidMarkerMaterial);
            markers.Clear();
        }

        private void OnValidate()
        {
            markerDuration = Mathf.Max(0.1f, markerDuration);
            markerFadeOutDuration = Mathf.Max(0f, markerFadeOutDuration);
            markerSizePixels = Mathf.Max(1f, markerSizePixels);
            markerBodySizeFraction = Mathf.Clamp(markerBodySizeFraction, 0.1f, 0.75f);
            breathingPeriod = Mathf.Max(0.2f, breathingPeriod);
            breathingScaleAmplitude = Mathf.Clamp(breathingScaleAmplitude, 0f, 0.2f);
            breathingMinimumOpacity = Mathf.Clamp01(breathingMinimumOpacity);
            scanHitBurstDuration = Mathf.Max(0.1f, scanHitBurstDuration);
            scanHitBurstRadiusPixels = Mathf.Max(4f, scanHitBurstRadiusPixels);
            scanHitBurstRayCount = Mathf.Clamp(scanHitBurstRayCount, 4, 16);
            trailPointLifetime = Mathf.Max(0.1f, trailPointLifetime);
            trailPointSpacing = Mathf.Max(0.02f, trailPointSpacing);
            trailSpawnInterval = Mathf.Max(0.05f, trailSpawnInterval);
            trailPointSizePixels = Mathf.Max(1f, trailPointSizePixels);
            trailOpacity = Mathf.Clamp01(trailOpacity);
        }
    }
}
