using System.Collections.Generic;
using AnimalGame.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AnimalGame.RobotMap
{
    /// <summary>
    /// Projects cached scene-aligned grid segments. Camera motion changes only
    /// their projection, never their identity or world position. Stroke width
    /// uses final screen pixels; spacing/gap use the normal view as a reference.
    /// </summary>
    [DefaultExecutionOrder(321)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Animal Game/Robot/Biological Scan Static Areas")]
    public sealed class BioScanStaticAreaUI : MonoBehaviour
    {
        private static readonly int AbsoluteScreenPlacementProperty = Shader.PropertyToID("_AbsoluteScreenPlacement");
        [SerializeField] private Shader areaShader;

        private readonly List<BioScanStaticAreaGraphic> areaGraphics =
            new List<BioScanStaticAreaGraphic>();
        private sealed class AreaLayout
        {
            public readonly List<StaticScanLineSegment> WorldSegments = new List<StaticScanLineSegment>();
            public readonly List<StaticScanLineSegment> Segments = new List<StaticScanLineSegment>();
            public readonly List<Vector2> SourceFootprint = new List<Vector2>();
            public Vector2 Minimum, Maximum;
            public float Spacing, Angle, Gap, Width;
            public bool Built;
        }

        private readonly struct VisibleArea
        {
            public readonly AreaLayout Layout;
            public readonly Vector2 Anchor;
            public readonly Vector2 ReferenceAnchor;
            public readonly Color Tint;
            public readonly float Width;
            public readonly int TargetId;
            public VisibleArea(AreaLayout layout, Vector2 anchor, Vector2 referenceAnchor,
                Color tint, float width, int targetId)
            {
                Layout = layout; Anchor = anchor; ReferenceAnchor = referenceAnchor;
                Tint = tint; Width = width; TargetId = targetId;
            }
        }

        private readonly Dictionary<ScannableInteractable, AreaLayout> layouts =
            new Dictionary<ScannableInteractable, AreaLayout>();
        private readonly List<ScannableInteractable> retiredLayouts = new List<ScannableInteractable>();
        private readonly List<Vector2> footprint = new List<Vector2>(32);
        private readonly List<Vector2> referenceFootprint = new List<Vector2>(32);
        private readonly List<VisibleArea> visibleAreas = new List<VisibleArea>();
        private readonly List<StaticScanLineSegment> acceptedSegments = new List<StaticScanLineSegment>();
        private readonly BioScanStaticGridCoverage gridCoverage = new BioScanStaticGridCoverage();
        private ScanChargeUI scanInput;
        private Camera worldCamera;
        private Canvas areaCanvas;
        private RectTransform canvasRect;
        private Material areaMaterial;
        private bool warnedMissingShader;
        private float referencePixelsPerWorldUnit;

        public void Initialize(ScanChargeUI input, Camera camera = null)
        {
            scanInput = input;
            worldCamera = camera;
            referencePixelsPerWorldUnit = 0f;
            layouts.Clear();
            if (areaCanvas != null)
                ConfigureCanvasSorting();
        }

        public void ShowScannedInteractable(ScannableInteractable target)
        {
            if (target != null && target.CanScan
                && target.ScanType == InteractableScanType.StaticArea)
                target.Reveal();
        }

        private void LateUpdate()
        {
            TickAreas();
        }

        private void TickAreas()
        {
            visibleAreas.Clear();
            gridCoverage.Clear();
            for (int index = areaGraphics.Count - 1; index >= 0; index--)
            {
                if (areaGraphics[index] == null) areaGraphics.RemoveAt(index);
                else areaGraphics[index].ClearAreas();
            }

            retiredLayouts.Clear();
            foreach (ScannableInteractable target in layouts.Keys)
                if (target == null || !target.CanScan || target.gameObject.scene != gameObject.scene)
                    retiredLayouts.Add(target);
            foreach (ScannableInteractable target in retiredLayouts)
                layouts.Remove(target);

            if (worldCamera == null || !worldCamera.isActiveAndEnabled)
                worldCamera = Camera.main;
            if (worldCamera == null || !worldCamera.isActiveAndEnabled)
                return;
            if (referencePixelsPerWorldUnit <= 0f)
            {
                RobotCameraShake shake = worldCamera.GetComponent<RobotCameraShake>();
                float normalSize = scanInput != null ? scanInput.BaseCameraOrthographicSize
                    : shake != null ? shake.BaseOrthographicSize : worldCamera.orthographicSize;
                referencePixelsPerWorldUnit = Mathf.Max(1f, worldCamera.pixelRect.height)
                    / (2f * Mathf.Max(.01f, normalSize));
            }

            Rect viewport = worldCamera.pixelRect;
            foreach (ScannableInteractable target in ScannableInteractable.Active)
            {
                if (target == null || target.gameObject.scene != gameObject.scene || !target.CanScan
                    || target.ScanType != InteractableScanType.StaticArea
                    || target.RevealRemaining <= 0f)
                    continue;

                if (!target.TryGetPresentationFootprint(footprint, out Vector3 worldCenter))
                    continue;

                Vector3 projectedCenter = worldCamera.WorldToScreenPoint(worldCenter);
                if (projectedCenter.z < worldCamera.nearClipPlane
                    || projectedCenter.z > worldCamera.farClipPlane)
                    continue;
                Vector2 anchor = projectedCenter;
                if (!layouts.TryGetValue(target, out AreaLayout layout))
                {
                    layout = new AreaLayout();
                    layouts[target] = layout;
                }
                if (!BuildWorldLayout(target, worldCenter, layout)) continue;
                ProjectLayout(layout, worldCenter, anchor);
                Vector2 padding = Vector2.one * (target.LineWidthPixels * 0.5f + 1f);
                Vector2 minimum = anchor + layout.Minimum - padding;
                Vector2 maximum = anchor + layout.Maximum + padding;
                if (maximum.x < viewport.xMin || minimum.x > viewport.xMax
                    || maximum.y < viewport.yMin || minimum.y > viewport.yMax)
                    continue;

                visibleAreas.Add(new VisibleArea(layout, anchor,
                    (Vector2)worldCenter * referencePixelsPerWorldUnit,
                    GetAreaTint(target), target.LineWidthPixels, target.GetInstanceID()));
            }

            if (visibleAreas.Count == 0 || !EnsureCanvas()) return;
            UpdateScreenSpaceMaterial();
            // Keep the strongest reveal when several targets supply the same
            // bar, including when one is expiring and another was just scanned.
            visibleAreas.Sort(CompareVisibleAreas);
            int batch = 0;
            foreach (VisibleArea area in visibleAreas)
            {
                acceptedSegments.Clear();
                for (int index = 0; index < area.Layout.WorldSegments.Count; index++)
                {
                    StaticScanLineSegment world = area.Layout.WorldSegments[index];
                    var reference = new StaticScanLineSegment(world.Start * referencePixelsPerWorldUnit,
                        world.End * referencePixelsPerWorldUnit);
                    // Use a fixed reference space for overlap decisions too.
                    // Screen-space collision thresholds can delete bars during
                    // zoom even after the world layout itself is cached.
                    if (gridCoverage.TryReserve(reference, area.ReferenceAnchor, area.Width))
                        acceptedSegments.Add(area.Layout.Segments[index]);
                }
                if (acceptedSegments.Count == 0) continue;
                BioScanStaticAreaGraphic graphic = GetGraphic(batch);
                if (!graphic.TryAddArea(acceptedSegments, area.Anchor, area.Tint,
                        area.Width, viewport))
                {
                    // Split large populations across meshes rather than lose
                    // targets at Unity UI's 65,000 vertex limit.
                    graphic = GetGraphic(++batch);
                    graphic.TryAddArea(acceptedSegments, area.Anchor, area.Tint,
                        area.Width, viewport);
                }
            }
        }

        private static int CompareVisibleAreas(VisibleArea first, VisibleArea second)
        {
            int opacity = second.Tint.a.CompareTo(first.Tint.a);
            return opacity != 0 ? opacity : first.TargetId.CompareTo(second.TargetId);
        }

        private bool BuildWorldLayout(ScannableInteractable target, Vector3 worldCenter, AreaLayout layout)
        {
            bool changed = !layout.Built || layout.Spacing != target.LineSpacingPixels
                || layout.Angle != target.LineAngleDegrees || layout.Gap != target.IntersectionGapPixels
                || layout.Width != target.LineWidthPixels || layout.SourceFootprint.Count != footprint.Count;
            if (!changed)
                for (int index = 0; index < footprint.Count; index++)
                    if ((layout.SourceFootprint[index] - footprint[index]).sqrMagnitude > .00000001f)
                    { changed = true; break; }
            if (!changed) return layout.WorldSegments.Count > 0;

            // Build only from actual object geometry/style. In particular, do
            // not feed current camera zoom/position into the lattice phase.
            layout.Spacing = target.LineSpacingPixels;
            layout.Angle = target.LineAngleDegrees;
            layout.Gap = target.IntersectionGapPixels;
            layout.Width = target.LineWidthPixels;
            layout.SourceFootprint.Clear();
            layout.SourceFootprint.AddRange(footprint);
            layout.Built = true;
            referenceFootprint.Clear();
            foreach (Vector2 offset in footprint)
                referenceFootprint.Add(offset * referencePixelsPerWorldUnit);
            Vector2 latticeOrigin = -(Vector2)worldCenter * referencePixelsPerWorldUnit;
            if (!BioScanStaticLinePattern.Build(referenceFootprint, layout.Spacing, layout.Angle,
                    layout.WorldSegments, layout.Gap, layout.Width, latticeOrigin))
                return false;
            for (int index = 0; index < layout.WorldSegments.Count; index++)
            {
                StaticScanLineSegment segment = layout.WorldSegments[index];
                layout.WorldSegments[index] = new StaticScanLineSegment(
                    segment.Start / referencePixelsPerWorldUnit, segment.End / referencePixelsPerWorldUnit);
            }
            return true;
        }

        private void ProjectLayout(AreaLayout layout, Vector3 worldCenter, Vector2 anchor)
        {
            layout.Segments.Clear();
            layout.Minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            layout.Maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (StaticScanLineSegment segment in layout.WorldSegments)
            {
                Vector2 start = (Vector2)worldCamera.WorldToScreenPoint(
                    worldCenter + (Vector3)segment.Start) - anchor;
                Vector2 end = (Vector2)worldCamera.WorldToScreenPoint(
                    worldCenter + (Vector3)segment.End) - anchor;
                layout.Segments.Add(new StaticScanLineSegment(start, end));
                layout.Minimum = Vector2.Min(layout.Minimum, Vector2.Min(start, end));
                layout.Maximum = Vector2.Max(layout.Maximum, Vector2.Max(start, end));
            }
        }

        private static Color GetAreaTint(ScannableInteractable target)
        {
            Color tint = target.AreaColor;
            float fadeDuration = Mathf.Min(target.FadeOutDuration, target.DisplayDuration);
            bool fading = fadeDuration > 0f && target.RevealRemaining < fadeDuration;
            // A scene marker holds its opacity instead of repeatedly pulsing
            // like a HUD notification. Only its entrance and expiry fade.
            float opacity = 0.8f * Mathf.SmoothStep(0f, 1f, target.RevealElapsed / 0.18f);
            if (fading)
                opacity *= Mathf.SmoothStep(0f, 1f, target.RevealRemaining / fadeDuration);
            tint.a *= opacity;
            return tint;
        }

        private bool EnsureCanvas()
        {
            if (areaCanvas != null)
                return true;

            // Resources retains this procedural shader in player builds even
            // when older robot prefabs lack an explicit serialized reference.
            Shader shader = areaShader != null ? areaShader
                : Resources.Load<Shader>("BioScanStaticArea");
            if (shader == null || (!shader.isSupported
                && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null))
            {
                if (!warnedMissingShader)
                {
                    Debug.LogWarning("Static scan areas require the BioScanStaticArea shader.", this);
                    warnedMissingShader = true;
                }
                return false;
            }

            if (areaMaterial != null)
            {
                if (Application.isPlaying) Destroy(areaMaterial);
                else DestroyImmediate(areaMaterial);
            }
            areaMaterial = new Material(shader)
            {
                name = "Runtime Biological Scan Static Areas",
                hideFlags = HideFlags.HideAndDontSave
            };
            // The Canvas may have been removed during a scene/play-mode
            // transition while this component's managed lists survived.
            areaGraphics.Clear();
            var canvasObject = new GameObject("Biological Scan Static Areas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.layer = LayerMask.NameToLayer("UI");
            SceneManager.MoveGameObjectToScene(canvasObject, gameObject.scene);
            canvasRect = canvasObject.GetComponent<RectTransform>();
            areaCanvas = canvasObject.GetComponent<Canvas>();
            areaCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            areaCanvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1
                | AdditionalCanvasShaderChannels.TexCoord2;
            canvasObject.GetComponent<CanvasScaler>().uiScaleMode =
                CanvasScaler.ScaleMode.ConstantPixelSize;
            ConfigureCanvasSorting();
            UpdateScreenSpaceMaterial();
            canvasObject.SetActive(isActiveAndEnabled);
            return true;
        }

        private void UpdateScreenSpaceMaterial()
        {
            if (areaCanvas == null || areaMaterial == null) return;
            areaMaterial.SetFloat(AbsoluteScreenPlacementProperty, 1f);
        }

        private void ConfigureCanvasSorting()
        {
            Canvas mainCanvas = scanInput != null
                ? scanInput.GetComponentInParent<Canvas>() : null;
            if (mainCanvas != null)
                mainCanvas = mainCanvas.rootCanvas;
            areaCanvas.sortingLayerID = mainCanvas != null ? mainCanvas.sortingLayerID : 0;
            // Above world rendering, below both the animal icons and the HUD.
            areaCanvas.sortingOrder = mainCanvas != null ? mainCanvas.sortingOrder - 2 : 28;
        }

        private BioScanStaticAreaGraphic GetGraphic(int index)
        {
            if (index < areaGraphics.Count)
                return areaGraphics[index];
            var graphicObject = new GameObject("Scanned Interactable Footprints " + index,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(BioScanStaticAreaGraphic));
            graphicObject.layer = areaCanvas.gameObject.layer;
            RectTransform rect = graphicObject.GetComponent<RectTransform>();
            rect.SetParent(canvasRect, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            BioScanStaticAreaGraphic graphic = graphicObject.GetComponent<BioScanStaticAreaGraphic>();
            graphic.material = areaMaterial;
            graphic.raycastTarget = false;
            graphic.maskable = false;
            areaGraphics.Add(graphic);
            return graphic;
        }

        private void OnEnable()
        {
            Canvas.willRenderCanvases -= UpdateScreenSpaceMaterial;
            Canvas.willRenderCanvases += UpdateScreenSpaceMaterial;
            if (areaCanvas != null)
                areaCanvas.gameObject.SetActive(true);
        }

        private void OnDisable()
        {
            Canvas.willRenderCanvases -= UpdateScreenSpaceMaterial;
            if (areaCanvas != null)
                areaCanvas.gameObject.SetActive(false);
            foreach (BioScanStaticAreaGraphic graphic in areaGraphics)
                if (graphic != null) graphic.ClearAreas();
        }

        private void OnDestroy()
        {
            Canvas.willRenderCanvases -= UpdateScreenSpaceMaterial;
            if (areaCanvas != null)
            {
                if (Application.isPlaying)
                    Destroy(areaCanvas.gameObject);
                else
                    DestroyImmediate(areaCanvas.gameObject);
            }
            if (areaMaterial != null)
            {
                if (Application.isPlaying)
                    Destroy(areaMaterial);
                else
                    DestroyImmediate(areaMaterial);
            }
            areaGraphics.Clear();
            layouts.Clear();
        }
    }
}
