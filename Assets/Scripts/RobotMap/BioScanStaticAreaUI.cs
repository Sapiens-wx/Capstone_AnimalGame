using System.Collections.Generic;
using AnimalGame.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AnimalGame.RobotMap
{
    /// <summary>
    /// Projects complete parallel bars inside each target's actual footprint.
    /// Captures the screen angle once per scan, then caches scene-fixed bars
    /// inside the rounded footprint at normal-view density. Stroke width uses
    /// final screen pixels; translating or zooming does not rebuild the bars.
    /// </summary>
    [DefaultExecutionOrder(321)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Animal Game/Robot/Biological Scan Static Areas")]
    public sealed class BioScanStaticAreaUI : MonoBehaviour
    {
        private static readonly int AbsoluteScreenPlacementProperty = Shader.PropertyToID("_AbsoluteScreenPlacement");
        [SerializeField] private Shader areaShader;
        [Tooltip("Captures the authored screen angle once after each scan, then locks the lines in world space. Disable to use the authored angle directly in world XY.")]
        [SerializeField] private bool alignOnScan = true;

        private readonly List<BioScanStaticAreaGraphic> areaGraphics =
            new List<BioScanStaticAreaGraphic>();
        private readonly List<BioScanStaticAreaGraphic> outlineGraphics =
            new List<BioScanStaticAreaGraphic>();
        private sealed class AreaLayout
        {
            public readonly List<StaticScanLineSegment> WorldSegments = new List<StaticScanLineSegment>();
            public readonly List<StaticScanLineSegment> Segments = new List<StaticScanLineSegment>();
            public readonly List<Vector2> SourceFootprint = new List<Vector2>();
            public readonly List<Vector2> PreparedFootprint = new List<Vector2>();
            public readonly List<Vector2> WorldOutlinePoints = new List<Vector2>();
            public readonly List<Vector2> OutlinePoints = new List<Vector2>();
            public readonly List<Vector2> WorldPointBoundary = new List<Vector2>();
            public readonly List<Vector2> PointBoundary = new List<Vector2>();
            public readonly List<StaticScanLineSegment> OutlineSegments = new List<StaticScanLineSegment>();
            public readonly List<Vector2> OutlineSourceFootprint = new List<Vector2>();
            public Vector2 Minimum, Maximum;
            public float Spacing, Angle, AuthoredAngle, Width, SmallSpacing, ResolvedSmallSpacing;
            public float OutlineDotSize, OutlineDotSpacing;
            public uint OutlineSeed;
            public bool Built, SmallFill, AlignOnScan, OutlineBuilt;
        }

        private readonly struct VisibleArea
        {
            public readonly AreaLayout Layout;
            public readonly Vector2 Anchor;
            public readonly Vector2 ReferenceAnchor;
            public readonly Vector3 WorldCenter;
            public readonly Color Tint, OutlineTint;
            public readonly float Width, OutlineWidth, CoveragePriority;
            public readonly bool ConstrainHatch;
            public readonly int TargetId;
            public VisibleArea(AreaLayout layout, Vector2 anchor, Vector2 referenceAnchor, Vector3 worldCenter,
                Color tint, Color outlineTint, float width, float outlineWidth, int targetId, float coveragePriority,
                bool constrainHatch)
            {
                Layout = layout; Anchor = anchor; ReferenceAnchor = referenceAnchor; WorldCenter = worldCenter;
                Tint = tint; OutlineTint = outlineTint; Width = width; OutlineWidth = outlineWidth; TargetId = targetId;
                CoveragePriority = coveragePriority;
                ConstrainHatch = constrainHatch;
            }
        }

        private readonly Dictionary<ScannableInteractable, AreaLayout> layouts =
            new Dictionary<ScannableInteractable, AreaLayout>();
        private readonly List<ScannableInteractable> retiredLayouts = new List<ScannableInteractable>();
        private readonly List<Vector2> footprint = new List<Vector2>(32);
        private readonly List<Vector2> referenceFootprint = new List<Vector2>(32);
        private readonly List<VisibleArea> visibleAreas = new List<VisibleArea>();
        private readonly List<StaticScanLineSegment> acceptedSegments = new List<StaticScanLineSegment>();
        private readonly List<StaticScanLineSegment> uncoveredSegments = new List<StaticScanLineSegment>();
        private readonly List<StaticScanLineSegment> boundedSegments = new List<StaticScanLineSegment>();
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

        /// <summary>Whether a target's real hatch or outline is currently presented in the camera viewport.</summary>
        public bool IsTargetVisible(ScannableInteractable target)
        {
            if (target == null || areaCanvas == null || !areaCanvas.isActiveAndEnabled) return false;
            int id = target.GetInstanceID();
            foreach (VisibleArea area in visibleAreas)
                if (area.TargetId == id && (area.Tint.a > .001f || area.OutlineTint.a > .001f)) return true;
            return false;
        }

        private void LateUpdate()
        {
            TickAreas();
        }

        private void TickAreas()
        {
            visibleAreas.Clear();
            gridCoverage.Clear();
            ClearGraphics(areaGraphics);
            ClearGraphics(outlineGraphics);

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

                if (alignOnScan)
                    target.CaptureScanOrientation(worldCamera.transform.eulerAngles.z);
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
                bool hasHatch = BuildWorldLayout(target, worldCenter, layout);
                bool hasOutline = BuildOutlineLayout(target, layout);
                if (!hasHatch && !hasOutline) continue;
                ProjectLayout(layout, worldCenter, anchor);
                float outlineWidth = target.OutlineDotSizePixels / Mathf.Sqrt(2f);
                float maximumWidth = hasOutline ? Mathf.Max(target.LineWidthPixels, outlineWidth)
                    : target.LineWidthPixels;
                Vector2 padding = Vector2.one * (maximumWidth * 0.5f + 1f);
                Vector2 minimum = anchor + layout.Minimum - padding;
                Vector2 maximum = anchor + layout.Maximum + padding;
                if (maximum.x < viewport.xMin || minimum.x > viewport.xMax
                    || maximum.y < viewport.yMin || minimum.y > viewport.yMax)
                    continue;

                visibleAreas.Add(new VisibleArea(layout, anchor,
                    (Vector2)worldCenter * referencePixelsPerWorldUnit, worldCenter,
                    GetAreaTint(target), GetOutlineTint(target), target.LineWidthPixels, outlineWidth, target.GetInstanceID(),
                    target.AreaColor.a * GetRevealEnvelope(target), target.ShowOutlineDots));
            }

            if (visibleAreas.Count == 0 || !EnsureCanvas()) return;
            UpdateScreenSpaceMaterial();
            // Entrance/expiry can hand shared coverage to a stronger reveal.
            // Breathing must never swap ownership and move the line endpoints.
            visibleAreas.Sort(CompareVisibleAreas);
            int batch = 0;
            foreach (VisibleArea area in visibleAreas)
            {
                acceptedSegments.Clear();
                boundedSegments.Clear();
                if (area.ConstrainHatch)
                    BioScanStaticLinePattern.ClipToBoundary(area.Layout.Segments,
                        area.Layout.PointBoundary, boundedSegments, area.Width);
                int segmentCount = area.ConstrainHatch ? boundedSegments.Count : area.Layout.WorldSegments.Count;
                float depth = area.ConstrainHatch ? worldCamera.WorldToScreenPoint(area.WorldCenter).z : 0f;
                for (int index = 0; index < segmentCount; index++)
                {
                    StaticScanLineSegment reference;
                    if (area.ConstrainHatch)
                    {
                        // Reserve only spans that will actually be drawn. A
                        // clipped-away tail must not hide another area's lines.
                        StaticScanLineSegment bounded = boundedSegments[index];
                        Vector2 screenStart = area.Anchor + bounded.Start;
                        Vector2 screenEnd = area.Anchor + bounded.End;
                        Vector2 worldStart = worldCamera.ScreenToWorldPoint(new Vector3(screenStart.x, screenStart.y, depth))
                            - area.WorldCenter;
                        Vector2 worldEnd = worldCamera.ScreenToWorldPoint(new Vector3(screenEnd.x, screenEnd.y, depth))
                            - area.WorldCenter;
                        reference = new StaticScanLineSegment(worldStart * referencePixelsPerWorldUnit,
                            worldEnd * referencePixelsPerWorldUnit);
                    }
                    else
                    {
                        StaticScanLineSegment world = area.Layout.WorldSegments[index];
                        reference = new StaticScanLineSegment(world.Start * referencePixelsPerWorldUnit,
                            world.End * referencePixelsPerWorldUnit);
                    }
                    // Use a fixed reference space for overlap decisions too.
                    // Screen-space collision thresholds can delete bars during
                    // zoom even after the world layout itself is cached.
                    uncoveredSegments.Clear();
                    gridCoverage.AppendUncoveredSegments(reference, area.ReferenceAnchor,
                        area.Width, uncoveredSegments);
                    foreach (StaticScanLineSegment piece in uncoveredSegments)
                    {
                        Vector2 start = (Vector2)worldCamera.WorldToScreenPoint(area.WorldCenter
                            + (Vector3)(piece.Start / referencePixelsPerWorldUnit)) - area.Anchor;
                        Vector2 end = (Vector2)worldCamera.WorldToScreenPoint(area.WorldCenter
                            + (Vector3)(piece.End / referencePixelsPerWorldUnit)) - area.Anchor;
                        acceptedSegments.Add(new StaticScanLineSegment(start, end));
                    }
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

            // Separate batches preserve the existing hatch layout. Draw the
            // boundary particles last so their solid shapes stay legible.
            int outlineBatch = 0;
            foreach (VisibleArea area in visibleAreas)
            {
                if (area.Layout.OutlineSegments.Count == 0) continue;
                BioScanStaticAreaGraphic graphic = GetOutlineGraphic(outlineBatch);
                if (!graphic.TryAddArea(area.Layout.OutlineSegments, area.Anchor, area.OutlineTint,
                        area.OutlineWidth, viewport))
                {
                    graphic = GetOutlineGraphic(++outlineBatch);
                    graphic.TryAddArea(area.Layout.OutlineSegments, area.Anchor, area.OutlineTint,
                        area.OutlineWidth, viewport);
                }
                graphic.rectTransform.SetAsLastSibling();
            }
        }

        private static void ClearGraphics(List<BioScanStaticAreaGraphic> graphics)
        {
            for (int index = graphics.Count - 1; index >= 0; index--)
            {
                if (graphics[index] == null) graphics.RemoveAt(index);
                else graphics[index].ClearAreas();
            }
        }

        private static int CompareVisibleAreas(VisibleArea first, VisibleArea second)
        {
            int opacity = second.CoveragePriority.CompareTo(first.CoveragePriority);
            return opacity != 0 ? opacity : first.TargetId.CompareTo(second.TargetId);
        }

        private bool BuildWorldLayout(ScannableInteractable target, Vector3 worldCenter, AreaLayout layout)
        {
            float worldAngle = alignOnScan && target.HasCapturedScanOrientation
                ? target.CapturedScanWorldAngleDegrees : target.LineAngleDegrees;
            bool geometryChanged = !layout.Built || layout.Spacing != target.LineSpacingPixels
                || layout.SmallFill != target.UseSmallAreaFill
                || layout.SmallSpacing != target.SmallAreaLineSpacingPixels
                || layout.AuthoredAngle != target.LineAngleDegrees
                || layout.AlignOnScan != alignOnScan
                || layout.Width != target.LineWidthPixels || layout.SourceFootprint.Count != footprint.Count;
            if (!geometryChanged)
                for (int index = 0; index < footprint.Count; index++)
                    if ((layout.SourceFootprint[index] - footprint[index]).sqrMagnitude > .00000001f)
                    { geometryChanged = true; break; }
            bool angleChanged = Mathf.Abs(Mathf.DeltaAngle(layout.Angle, worldAngle)) > .00001f;
            if (!geometryChanged && !angleChanged) return layout.WorldSegments.Count > 0;

            // Only a new scan or changed shape/style can recut the bars.
            // Live camera/player rotation, position and zoom do not enter here.
            layout.Spacing = target.LineSpacingPixels;
            layout.SmallFill = target.UseSmallAreaFill;
            layout.SmallSpacing = target.SmallAreaLineSpacingPixels;
            layout.AuthoredAngle = target.LineAngleDegrees;
            layout.Angle = worldAngle;
            layout.Width = target.LineWidthPixels;
            layout.AlignOnScan = alignOnScan;
            if (geometryChanged)
            {
                layout.SourceFootprint.Clear();
                layout.SourceFootprint.AddRange(footprint);
                referenceFootprint.Clear();
                foreach (Vector2 offset in footprint)
                    referenceFootprint.Add(offset * referencePixelsPerWorldUnit);
                layout.PreparedFootprint.Clear();
                if (alignOnScan)
                    BioScanStaticLinePattern.PrepareFootprint(referenceFootprint, layout.PreparedFootprint);
            }
            layout.Built = true;
            bool built;
            if (alignOnScan)
            {
                // Each target has a stable centre phase. Freeze small-target
                // density so a refreshed direction does not change spacing.
                if (layout.SmallFill && geometryChanged)
                    built = BioScanStaticLinePattern.BuildSmallAreaPrepared(referenceFootprint,
                        layout.PreparedFootprint, layout.SmallSpacing, layout.Angle,
                        layout.WorldSegments, layout.Width, out layout.ResolvedSmallSpacing, Vector2.zero);
                else
                    built = BioScanStaticLinePattern.BuildPrepared(layout.PreparedFootprint,
                        layout.SmallFill ? layout.ResolvedSmallSpacing : layout.Spacing, layout.Angle,
                        layout.WorldSegments, layout.Width, Vector2.zero, layout.SmallFill ? 4f : -1f);
            }
            else
            {
                Vector2 latticeOrigin = -(Vector2)worldCenter * referencePixelsPerWorldUnit;
                built = layout.SmallFill
                    ? BioScanStaticLinePattern.BuildSmallArea(referenceFootprint, layout.SmallSpacing,
                        layout.Angle, layout.WorldSegments, layout.Width)
                    : BioScanStaticLinePattern.Build(referenceFootprint, layout.Spacing, layout.Angle,
                        layout.WorldSegments, layout.Width, latticeOrigin);
            }
            if (!built)
                return false;
            for (int index = 0; index < layout.WorldSegments.Count; index++)
            {
                StaticScanLineSegment segment = layout.WorldSegments[index];
                layout.WorldSegments[index] = new StaticScanLineSegment(
                    segment.Start / referencePixelsPerWorldUnit, segment.End / referencePixelsPerWorldUnit);
            }
            return true;
        }

        private bool BuildOutlineLayout(ScannableInteractable target, AreaLayout layout)
        {
            if (!target.ShowOutlineDots)
            {
                layout.OutlineBuilt = false;
                layout.WorldOutlinePoints.Clear();
                layout.WorldPointBoundary.Clear();
                return false;
            }
            float spacing = target.UseSmallAreaFill ? target.SmallOutlineDotSpacingPixels : target.OutlineDotSpacingPixels;
            bool changed = !layout.OutlineBuilt || layout.OutlineDotSize != target.OutlineDotSizePixels
                || layout.OutlineSeed != target.OutlinePatternSeed
                || layout.OutlineDotSpacing != spacing
                || layout.OutlineSourceFootprint.Count != footprint.Count;
            if (!changed)
                for (int index = 0; index < footprint.Count; index++)
                    if ((layout.OutlineSourceFootprint[index] - footprint[index]).sqrMagnitude > .00000001f)
                    { changed = true; break; }
            if (!changed) return layout.WorldOutlinePoints.Count > 0;

            layout.OutlineBuilt = true;
            layout.OutlineDotSize = target.OutlineDotSizePixels;
            layout.OutlineDotSpacing = spacing;
            layout.OutlineSeed = target.OutlinePatternSeed;
            layout.OutlineSourceFootprint.Clear();
            layout.OutlineSourceFootprint.AddRange(footprint);
            referenceFootprint.Clear();
            foreach (Vector2 offset in footprint)
                referenceFootprint.Add(offset * referencePixelsPerWorldUnit);
            bool built = BioScanStaticOutlinePattern.BuildDots(referenceFootprint, layout.OutlineDotSpacing,
                layout.WorldOutlinePoints, layout.OutlineDotSize, layout.OutlineSeed);
            layout.WorldPointBoundary.Clear();
            if (!built) return false;
            for (int index = 0; index < layout.WorldOutlinePoints.Count; index++)
                layout.WorldOutlinePoints[index] /= referencePixelsPerWorldUnit;
            BioScanStaticLinePattern.BuildPointBoundaryHull(layout.WorldOutlinePoints, layout.WorldPointBoundary);
            return true;
        }

        private void ProjectLayout(AreaLayout layout, Vector3 worldCenter, Vector2 anchor)
        {
            layout.Segments.Clear();
            layout.OutlineSegments.Clear();
            layout.OutlinePoints.Clear();
            layout.PointBoundary.Clear();
            layout.Minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            layout.Maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            ProjectSegments(layout.WorldSegments, layout.Segments, layout, worldCenter, anchor);
            foreach (Vector2 point in layout.WorldOutlinePoints)
                layout.OutlinePoints.Add((Vector2)worldCamera.WorldToScreenPoint(worldCenter + (Vector3)point) - anchor);
            foreach (Vector2 point in layout.WorldPointBoundary)
                layout.PointBoundary.Add((Vector2)worldCamera.WorldToScreenPoint(worldCenter + (Vector3)point) - anchor);
            // A square bar rotated in world XY produces a solid diamond. Keep
            // its screen-pixel size while only projecting the cached centres.
            Vector2 glyphDirection = (Vector2)worldCamera.WorldToScreenPoint(worldCenter + new Vector3(1f, 1f, 0f)) - anchor;
            BioScanStaticOutlinePattern.BuildDiamondSegments(layout.OutlinePoints, glyphDirection,
                layout.OutlineDotSize, layout.OutlineSegments);
            foreach (StaticScanLineSegment segment in layout.OutlineSegments)
            {
                layout.Minimum = Vector2.Min(layout.Minimum, Vector2.Min(segment.Start, segment.End));
                layout.Maximum = Vector2.Max(layout.Maximum, Vector2.Max(segment.Start, segment.End));
            }
        }

        private void ProjectSegments(List<StaticScanLineSegment> worldSegments,
            List<StaticScanLineSegment> projectedSegments, AreaLayout layout, Vector3 worldCenter, Vector2 anchor)
        {
            foreach (StaticScanLineSegment segment in worldSegments)
            {
                Vector2 start = (Vector2)worldCamera.WorldToScreenPoint(
                    worldCenter + (Vector3)segment.Start) - anchor;
                Vector2 end = (Vector2)worldCamera.WorldToScreenPoint(
                    worldCenter + (Vector3)segment.End) - anchor;
                projectedSegments.Add(new StaticScanLineSegment(start, end));
                layout.Minimum = Vector2.Min(layout.Minimum, Vector2.Min(start, end));
                layout.Maximum = Vector2.Max(layout.Maximum, Vector2.Max(start, end));
            }
        }

        private static Color GetAreaTint(ScannableInteractable target)
        {
            Color tint = target.AreaColor;
            // Keep the entrance a smooth fade to full brightness. Only begin
            // pulsing after it has appeared; later scan refreshes keep this clock.
            float pulseElapsed = Mathf.Max(0f, target.RevealElapsed - target.FadeInDuration);
            float phase = Mathf.Repeat(pulseElapsed, target.BreathingPeriod)
                / target.BreathingPeriod;
            float breath = .5f + .5f * Mathf.Cos(phase * Mathf.PI * 2f);
            tint.a *= GetRevealEnvelope(target)
                * Mathf.Lerp(target.BreathingMinAlpha, target.BreathingMaxAlpha, breath * breath);
            return tint;
        }

        private static Color GetOutlineTint(ScannableInteractable target)
        {
            Color tint = target.AreaColor;
            float phase = Mathf.Repeat(target.RevealElapsed, target.OutlineBreathingPeriod)
                / target.OutlineBreathingPeriod;
            float breath = .5f + .5f * Mathf.Cos(phase * Mathf.PI * 2f);
            tint.a *= GetRevealEnvelope(target)
                * Mathf.Lerp(target.OutlineBreathingMinAlpha, target.OutlineBreathingMaxAlpha, breath);
            return tint;
        }

        private static float GetRevealEnvelope(ScannableInteractable target)
        {
            if (target.RevealRemaining <= 0f)
                return 0f;
            float fadeDuration = Mathf.Min(target.FadeOutDuration, target.DisplayDuration);
            bool fading = fadeDuration > 0f && target.RevealRemaining < fadeDuration;
            float opacity = target.FadeInDuration > 0f
                ? Mathf.SmoothStep(0f, 1f, target.RevealElapsed / target.FadeInDuration) : 1f;
            if (fading)
                opacity *= Mathf.SmoothStep(0f, 1f, target.RevealRemaining / fadeDuration);
            return opacity;
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
            outlineGraphics.Clear();
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
            => GetGraphic(index, areaGraphics, "Scanned Interactable Footprints ");

        private BioScanStaticAreaGraphic GetOutlineGraphic(int index)
            => GetGraphic(index, outlineGraphics, "Scanned Interactable Diamond Boundaries ");

        private BioScanStaticAreaGraphic GetGraphic(int index, List<BioScanStaticAreaGraphic> graphics, string name)
        {
            if (index < graphics.Count)
                return graphics[index];
            var graphicObject = new GameObject(name + index,
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
            graphics.Add(graphic);
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
            ClearGraphics(areaGraphics);
            ClearGraphics(outlineGraphics);
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
            outlineGraphics.Clear();
            layouts.Clear();
        }
    }
}
