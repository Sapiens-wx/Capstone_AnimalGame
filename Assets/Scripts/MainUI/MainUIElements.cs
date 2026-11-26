using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UIElements;

namespace AnimalGame.MainUI
{
    internal static class HudDrawing
    {
        public static readonly Color Yellow = new Color(212f / 255, 184f / 255, 84f / 255);

        public static void Line(Painter2D p, Vector2 a, Vector2 b)
        {
            p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.Stroke();
        }

        public static void Polygon(Painter2D p, Vector2[] points, bool fill = false)
        {
            if (points.Length == 0) return;
            p.BeginPath(); p.MoveTo(points[0]);
            for (int i = 1; i < points.Length; i++) p.LineTo(points[i]);
            p.ClosePath();
            if (fill) p.Fill(); else p.Stroke();
        }

        public static void Rect(Painter2D p, float x, float y, float w, float h)
        {
            Polygon(p, new[] { new Vector2(x, y), new Vector2(x + w, y),
                new Vector2(x + w, y + h), new Vector2(x, y + h) }, true);
        }

        public static Vector2 Radial(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        public static void Arc(Painter2D p, Vector2 center, float rx, float ry,
            float start, float sweep, bool fill = false)
        {
            int segments = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(sweep) / 2));
            p.BeginPath();
            for (int i = 0; i <= segments; i++)
            {
                Vector2 d = Radial(start + sweep * i / segments);
                Vector2 point = center + new Vector2(d.x * rx, d.y * ry);
                if (i == 0) p.MoveTo(point); else p.LineTo(point);
            }
            if (fill) { p.ClosePath(); p.Fill(); } else p.Stroke();
        }
    }

    [UxmlElement]
    public partial class ViewRingGroupElement : VisualElement
    {
        private float lineWidthValue = 2;
        /// <summary>Shared stroke width for both rings and their ticks.</summary>
        [UxmlAttribute]
        public float lineWidth
        {
            get => lineWidthValue;
            set
            {
                lineWidthValue = float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Max(0, value);
                ApplyLineWidth();
            }
        }

        public ViewRingGroupElement()
        {
            pickingMode = PickingMode.Ignore;
            RegisterCallback<AttachToPanelEvent>(_ => ApplyLineWidth(), TrickleDown.TrickleDown);
        }

        private void ApplyLineWidth()
        {
            this.Query<ViewRingElement>().ForEach(ring => ring.lineWidth = lineWidth);
        }
    }

    [UxmlElement]
    public partial class ViewRingElement : VisualElement
    {
        private bool ticksValue;
        [UxmlAttribute]
        public bool ticks
        {
            get => ticksValue;
            set
            {
                if (ticksValue == value) return;
                ticksValue = value;
                MarkDirtyRepaint();
            }
        }
        private float lineWidthValue = 2;
        [UxmlAttribute]
        public float lineWidth
        {
            get => lineWidthValue;
            set
            {
                if (lineWidthValue == value) return;
                lineWidthValue = value;
                MarkDirtyRepaint();
            }
        }
        private float tickLengthValue = 28;
        [UxmlAttribute]
        public float tickLength
        {
            get => tickLengthValue;
            set
            {
                if (tickLengthValue == value) return;
                tickLengthValue = value;
                MarkDirtyRepaint();
            }
        }
        private float radiusInsetValue;
        /// <summary>Inset of the circle, independent of the inward tick length.</summary>
        [UxmlAttribute]
        public float radiusInset
        {
            get => radiusInsetValue;
            set
            {
                value = float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Max(0, value);
                if (radiusInsetValue == value) return;
                radiusInsetValue = value;
                MarkDirtyRepaint();
            }
        }
        public float Radius => Mathf.Max(0, Mathf.Min(contentRect.width, contentRect.height) * .5f
            - lineWidth * .5f - radiusInset);

        private float fillInsetValue = 0;
        /// <summary>Black fill depth inward from the circle in UI units.</summary>
        [UxmlAttribute]
        public float fillInset
        {
            get => fillInsetValue;
            set
            {
                value = float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Max(0, value);
                if (fillInsetValue == value) return;
                fillInsetValue = value;
                MarkDirtyRepaint();
            }
        }
        public ViewRingElement()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        private void Draw(MeshGenerationContext context)
        {
            Painter2D p = context.painter2D;
            p.strokeColor = Color.white; p.lineWidth = lineWidth;
            Vector2 center = contentRect.center;
            float radius = Radius;
            if (radius <= 0) return;
            float fillDepth = Mathf.Min(fillInset, radius);
            if (fillDepth > 0)
            {
                p.fillColor = Color.black;
                p.BeginPath();
                for (int i = 0; i <= 180; i++)
                {
                    Vector2 point = center + HudDrawing.Radial(i * 2) * radius;
                    if (i == 0) p.MoveTo(point); else p.LineTo(point);
                }
                for (int i = 180; i >= 0; i--)
                    p.LineTo(center + HudDrawing.Radial(i * 2) * (radius - fillDepth));
                p.ClosePath(); p.Fill();
            }
            HudDrawing.Arc(p, center, radius, radius, 0, 360);
            if (!ticks) return;
            float depth = Mathf.Clamp(tickLength, 0, radius);
            for (int i = 0; i < 12 && depth > 0; i++)
            {
                Vector2 d = HudDrawing.Radial(i * 30);
                HudDrawing.Line(p, center + d * radius, center + d * (radius - depth));
            }
        }
    }

    /// <summary>Normalized radial progress: the endpoint retreats counterclockwise as progress is consumed.</summary>
    [UxmlElement]
    public partial class CircularProgressElement : VisualElement
    {
        private static readonly Color TrackColor = new Color32(61, 61, 61, 255);
        private float value = 1;
        [UxmlAttribute] public float progress { get => value; set => SetProgress(value); }
        private float preview;
        private bool showTrackValue;
        [UxmlAttribute]
        public bool showTrack
        {
            get => showTrackValue;
            set
            {
                if (showTrackValue == value) return;
                showTrackValue = value;
                MarkDirtyRepaint();
            }
        }

        private float lengthValue = 8;
        /// <summary>Length of the yellow radial indicator, measured outward from the ring's inner edge.</summary>
        [UxmlAttribute]
        public float length
        {
            get => lengthValue;
            set
            {
                value = float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Max(0, value);
                if (lengthValue == value) return;
                lengthValue = value;
                MarkDirtyRepaint();
            }
        }

        private bool outsideValue;
        [UxmlAttribute]
        public bool outside
        {
            get => outsideValue;
            set
            {
                if (outsideValue == value) return;
                outsideValue = value;
                MarkDirtyRepaint();
            }
        }
        private float lineWidthValue = 4;
        [UxmlAttribute]
        public float lineWidth
        {
            get => lineWidthValue;
            set
            {
                if (lineWidthValue == value) return;
                lineWidthValue = value;
                MarkDirtyRepaint();
            }
        }

        private float notchDepthValue = 10;
        /// <summary>Depth of the three inward integrity points in UI units.</summary>
        [UxmlAttribute]
        public float notchDepth
        {
            get => notchDepthValue;
            set
            {
                value = float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Max(0, value);
                if (notchDepthValue == value) return;
                notchDepthValue = value;
                MarkDirtyRepaint();
            }
        }
        public CircularProgressElement()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public void SetProgress(float normalized)
        {
            normalized = float.IsNaN(normalized) || float.IsInfinity(normalized) ? 0 : Mathf.Clamp01(normalized);
            if (value == normalized) return;
            value = normalized;
            MarkDirtyRepaint();
        }

        public void SetPreviewProgress(float normalized)
        {
            normalized = float.IsNaN(normalized) || float.IsInfinity(normalized) ? 0 : Mathf.Clamp01(normalized);
            if (preview == normalized) return;
            preview = normalized;
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext context)
        {
            Painter2D p = context.painter2D;
            Vector2 c = contentRect.center;
            float radius = Mathf.Min(contentRect.width, contentRect.height) * .5f - lineWidth * .5f - 2;
            if (radius <= 0 || lineWidth <= 0) return;
            p.lineWidth = lineWidth; p.lineCap = LineCap.Butt;
            if (outside)
            {
                // Screen angles increase clockwise. Consume counterclockwise from
                // the upper-right start, keeping the right-hand endpoint fixed.
                float consumed = (1 - value) * 300;
                if (showTrack) DrawPowerArc(p, c, radius, 0f, TrackColor);
                if (value <= 0f) return;
                DrawPowerArc(p, c, radius, consumed, HudDrawing.Yellow);
                Vector2 d = HudDrawing.Radial(-60 - consumed);
                float innerRadius = Mathf.Max(0, radius - lineWidth * .5f);
                if (length > 0)
                    HudDrawing.Line(p, c + d * innerRadius, c + d * (innerRadius + length));
                return;
            }
            if (showTrack) DrawIntegrityArc(p, c, radius, 0f, 360f, TrackColor);
            float sweep = value * 360f;
            if (value > 0f) DrawIntegrityArc(p, c, radius, 0f, sweep, Color.white);
            if (preview > value) DrawIntegrityArc(p, c, radius, sweep, preview * 360f, HudDrawing.Yellow);
        }

        private static void DrawPowerArc(Painter2D painter, Vector2 center, float radius, float consumed, Color color)
        {
            painter.strokeColor = color;
            for (int i = 0; i < 10; i++)
            {
                float start = Mathf.Max(consumed, i * 30 + (i == 0 ? 0 : 2));
                float end = (i + 1) * 30 - (i == 9 ? 0 : 2);
                if (end > start) HudDrawing.Arc(painter, center, radius, radius, -60 - start, start - end);
            }
        }

        private void DrawIntegrityArc(Painter2D painter, Vector2 center, float radius, float start, float end, Color color)
        {
            // Three asymmetric inward points on the white integrity outline.
            float sweep = end - start;
            painter.strokeColor = color;
            painter.BeginPath();
            int steps = Mathf.Max(1, Mathf.CeilToInt(sweep));
            for (int i = 0; i <= steps; i++)
            {
                float angle = start + sweep * i / steps;
                float phase = Mathf.Repeat(angle, 120);
                float inset = phase <= 12 ? (1 - phase / 12) * Mathf.Min(notchDepth, radius) : 0;
                Vector2 point = center + HudDrawing.Radial(-60 + angle) * (radius - inset);
                if (i == 0) painter.MoveTo(point); else painter.LineTo(point);
            }
            if (sweep >= 360f) painter.ClosePath();
            painter.Stroke();
        }
    }

    [UxmlElement]
    public partial class CompassElement : VisualElement
    {
        private readonly Label[] labels = new Label[15];
        private float heading;
        private readonly float[] pinBearings = new float[AnimalGame.World.Map.MapViewState.PinLimit];
        private readonly int[] pinShapes = new int[AnimalGame.World.Map.MapViewState.PinLimit];
        private int pinCount;

        public void SetPins(AnimalGame.World.Map.WorldMapUI map, Vector2 playerWorld)
        {
            pinCount = map != null ? map.View.Pins.Count : 0;
            for (int i = 0; i < pinCount; i++)
            {
                pinBearings[i] = map.PinBearing(i, playerWorld);
                pinShapes[i] = map.View.GetPinShape(i);
            }
            MarkDirtyRepaint();
        }
        private float lineWidthValue = 2;
        private float degreeFontSizeValue = 17;
        private float directionFontSizeValue = 20;
        private float baselineOffsetValue = 33;
        private float displayRangeValue = 180;
        private Font metricsFont;
        private float fontDescentRatio;
        private Rect notchClip;
        private float notchBevel;
        private VectorImage notchMask;
        public float Heading => heading;

        [UxmlAttribute] public float lineWidth { get => lineWidthValue; set => SetDimension(ref lineWidthValue, value); }
        [UxmlAttribute] public float degreeFontSize { get => degreeFontSizeValue; set => SetDimension(ref degreeFontSizeValue, value, 1); }
        [UxmlAttribute] public float directionFontSize { get => directionFontSizeValue; set => SetDimension(ref directionFontSizeValue, value, 1); }
        /// <summary>Distance from the Compass content bottom to the shared text bottom, in local UI units.</summary>
        [UxmlAttribute] public float baselineOffset { get => baselineOffsetValue; set => SetDimension(ref baselineOffsetValue, value); }
        /// <summary>Total visible angular range in degrees, centered on the heading and clamped to 1–360.</summary>
        [UxmlAttribute]
        public float displayRange
        {
            get => displayRangeValue;
            set
            {
                value = float.IsNaN(value) || float.IsInfinity(value) ? 180 : Mathf.Clamp(value, 1, 360);
                if (displayRangeValue == value) return;
                displayRangeValue = value;
                UpdateLabels();
                MarkDirtyRepaint();
            }
        }

        private void SetDimension(ref float field, float value, float minimum = 0)
        {
            value = float.IsNaN(value) || float.IsInfinity(value) ? minimum : Mathf.Max(minimum, value);
            if (field == value) return;
            field = value;
            UpdateLabels();
            MarkDirtyRepaint();
        }

        public CompassElement()
        {
            pickingMode = PickingMode.Ignore;
            style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
            style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left);
            style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top);
            for (int i = 0; i < labels.Length; i++)
            {
                var label = new Label();
                label.style.position = Position.Absolute;
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.unityTextAlign = TextAnchor.LowerCenter;
                label.AddToClassList("compass-label");
                label.pickingMode = PickingMode.Ignore;
                label.RegisterCallback<GeometryChangedEvent>(_ => AlignLabelBaseline(label));
                labels[i] = label;
                Add(label);
            }
            RegisterCallback<GeometryChangedEvent>(_ =>
            {
                UpdateLabels();
                RefreshNotchMask();
            });
            RegisterCallback<DetachFromPanelEvent>(_ => ReleaseNotchMask());
            generateVisualContent += Draw;
            UpdateLabels();
        }

        public void SetHeading(float degrees)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees)) return;
            heading = Mathf.Repeat(degrees, 360);
            UpdateLabels();
            MarkDirtyRepaint();
        }

        private float X(float degrees) => contentRect.width * (.5f + (degrees - heading) / displayRange);

        internal void SetNotchClip(Rect bounds, float bevel)
        {
            if (notchClip == bounds && notchBevel == bevel && notchMask != null) return;
            notchClip = bounds;
            notchBevel = bevel;
            RefreshNotchMask();
            MarkDirtyRepaint();
        }

        private void RefreshNotchMask()
        {
            float width = contentRect.width;
            const float borderInset = .75f;
            if (panel == null) return;
            float maskHeight = NotchBottom(Mathf.Clamp(notchClip.center.x, 0f, width), 0f) - borderInset;
            if (!(width > 0f && notchClip.height > 0f && maskHeight > 0f))
            {
                ReleaseNotchMask();
                return;
            }

            // Build the stencil as one solid shape in compass coordinates. The full
            // HUD frame has a hole and must not be cropped down to serve as this stencil.
            Vector2 EdgePoint(float x) => new Vector2(x, Mathf.Max(0f, NotchBottom(x, 0f) - borderInset));
            var image = ScriptableObject.CreateInstance<VectorImage>();
            image.hideFlags = HideFlags.HideAndDontSave;
            using var painter = new Painter2D();
            painter.fillColor = Color.black;
            painter.BeginPath();
            painter.MoveTo(Vector2.zero);
            painter.LineTo(new Vector2(width, 0f));
            painter.LineTo(EdgePoint(width));
            foreach (float x in new[] { notchClip.xMax + notchBevel, notchClip.xMax, notchClip.xMin, notchClip.xMin - notchBevel })
                if (x > 0f && x < width) painter.LineTo(EdgePoint(x));
            painter.LineTo(EdgePoint(0f));
            painter.ClosePath();
            painter.Fill();
            if (!painter.SaveToVectorImage(image))
            {
                DestroyMask(image);
                return;
            }

            ReleaseNotchMask();
            notchMask = image;
            style.backgroundSize = new BackgroundSize(new Length(width), new Length(maskHeight));
            style.backgroundImage = Background.FromVectorImage(notchMask);
            // A background change alone only dirties the paint. Enable clipping
            // after the vector is installed so Unity selects stencil clipping.
            style.overflow = Overflow.Hidden;
        }

        private void ReleaseNotchMask()
        {
            if (notchMask == null) return;
            style.overflow = Overflow.Visible;
            style.backgroundImage = StyleKeyword.Null;
            DestroyMask(notchMask);
            notchMask = null;
        }

        private static void DestroyMask(VectorImage image)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(image);
            else UnityEngine.Object.DestroyImmediate(image);
        }

        private float NotchBottom(float x, float halfWidth)
        {
            if (notchClip.height <= 0f) return contentRect.yMax;
            float beyondFlatEdge = Mathf.Abs(x - notchClip.center.x) + halfWidth - notchClip.width * .5f;
            return Mathf.Lerp(notchClip.yMax, notchClip.yMin, beyondFlatEdge / Mathf.Max(.001f, notchBevel));
        }

        private void UpdateLabels()
        {
            float start = Mathf.Floor((heading - displayRange * .5f) / 30) * 30 - 30;
            int labelCount = Mathf.CeilToInt(displayRange / 30) + 3;
            for (int i = 0; i < labels.Length; i++)
            {
                labels[i].style.display = i < labelCount ? DisplayStyle.Flex : DisplayStyle.None;
                if (i >= labelCount) continue;
                float degrees = start + i * 30;
                int normalized = Mathf.RoundToInt(Mathf.Repeat(degrees, 360));
                bool isDirection = normalized % 90 == 0;
                labels[i].text = normalized switch { 0 => "N", 90 => "E", 180 => "S", 270 => "W", _ => normalized + "°" };
                labels[i].EnableInClassList("compass-degree", !isDirection);
                labels[i].style.fontSize = isDirection ? directionFontSize : degreeFontSize;
                labels[i].style.left = X(degrees);
                AlignLabelBaseline(labels[i]);
                labels[i].style.translate = new Translate(new Length(-50, LengthUnit.Percent), 0);
            }
        }

        private void AlignLabelBaseline(Label label)
        {
            var definition = label.resolvedStyle.unityFontDefinition;
            float descentRatio = 0;
            if (definition.fontAsset != null)
            {
                var face = definition.fontAsset.faceInfo;
                if (face.pointSize > 0) descentRatio = face.descentLine * face.scale / face.pointSize;
            }
            else
            {
                Font font = definition.font != null ? definition.font : label.resolvedStyle.unityFont;
                if (font != null && font != metricsFont)
                {
                    // Cache face metrics; heading changes must not reload the shared font engine.
                    metricsFont = font;
                    fontDescentRatio = 0;
                    if (FontEngine.LoadFontFace(font, 64) == FontEngineError.Success)
                    {
                        var face = FontEngine.GetFaceInfo();
                        if (face.pointSize > 0) fontDescentRatio = face.descentLine * face.scale / face.pointSize;
                    }
                }
                if (font != null) descentRatio = fontDescentRatio;
            }
            // LowerCenter aligns the font's descent line. Shift by its signed distance
            // from the baseline so different font sizes share the actual text baseline.
            label.style.bottom = baselineOffset + descentRatio * label.style.fontSize.value.value;
        }

        private void Draw(MeshGenerationContext context)
        {
            Painter2D p = context.painter2D;
            if (lineWidth <= 0) return;
            p.lineWidth = lineWidth; p.strokeColor = Color.white;
            p.lineCap = LineCap.Butt;
            float y = contentRect.height - 3;
            float halfRange = displayRange * .5f;
            int start = Mathf.FloorToInt((heading - halfRange) / 10) * 10;
            for (int degree = start; degree <= heading + halfRange; degree += 10)
            {
                float x = X(degree);
                if (x < lineWidth * .5f || x > contentRect.width - lineWidth * .5f) continue;
                float top = y - (degree % 30 == 0 ? 20 : 10);
                float bottom = Mathf.Min(y, NotchBottom(x, lineWidth * .5f));
                if (bottom > top) HudDrawing.Line(p, new Vector2(x, top), new Vector2(x, bottom));
            }
            for (int i = 0; i < pinCount; i++)
            {
                float delta = Mathf.DeltaAngle(heading, pinBearings[i]);
                if (Mathf.Abs(delta) > halfRange) continue;
                float x = X(heading + delta);
                if (x - 8f < 0f || x + 8f > contentRect.width || y - 11f > NotchBottom(x, 8f)) continue;
                AnimalGame.World.Map.WorldMapUI.DrawPinShape(p, new Vector2(x, y - 19), 7, pinShapes[i]);
            }
        }
    }

    [UxmlElement]
    public partial class CompassPointerElement : VisualElement
    {
        private float clipBottom = float.PositiveInfinity;

        public CompassPointerElement()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += context =>
            {
                float y = Mathf.Min(clipBottom, contentRect.height);
                if (y <= 0f) return;
                float center = contentRect.width * .5f;
                float halfWidth = center * (y / contentRect.height);
                Painter2D p = context.painter2D; p.fillColor = HudDrawing.Yellow;
                HudDrawing.Polygon(p, new[] { new Vector2(center, 0f),
                    new Vector2(center + halfWidth, y), new Vector2(center - halfWidth, y) }, true);
            };
        }

        internal void SetClipBottom(float y)
        {
            if (clipBottom == y) return;
            clipBottom = y;
            MarkDirtyRepaint();
        }
    }

    [UxmlElement]
    public partial class TimeOrbitElement : VisualElement
    {
        private float dayProgress = .25f;
        private float radiusValue;
        private const float SunPulsePeriodSeconds = 1.8f;
        private float sunPulse = 1f;
        /// <summary>Orbit radius in UI units. Zero sizes the orbit automatically.</summary>
        [UxmlAttribute]
        public float radius
        {
            get => radiusValue;
            set
            {
                value = float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Max(0, value);
                if (radiusValue == value) return;
                radiusValue = value;
                MarkDirtyRepaint();
            }
        }

        private float tickLengthValue = 12;
        /// <summary>Total tick length in UI units, centered on the orbit.</summary>
        [UxmlAttribute]
        public float tickLength
        {
            get => tickLengthValue;
            set
            {
                value = float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Max(0, value);
                if (tickLengthValue == value) return;
                tickLengthValue = value;
                MarkDirtyRepaint();
            }
        }

        private float lineWidthValue = 2;
        /// <summary>Stroke width of the white orbit and ticks only.</summary>
        [UxmlAttribute]
        public float lineWidth
        {
            get => lineWidthValue;
            set
            {
                value = float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Max(0, value);
                if (lineWidthValue == value) return;
                lineWidthValue = value;
                MarkDirtyRepaint();
            }
        }
        private float dashGapValue = 10;
        /// <summary>Angular gap between lower segments in degrees (0 to 29).</summary>
        [UxmlAttribute]
        public float dashGap
        {
            get => dashGapValue;
            set
            {
                value = float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Clamp(value, 0, 29);
                if (dashGapValue == value) return;
                dashGapValue = value;
                MarkDirtyRepaint();
            }
        }
        public TimeOrbitElement()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            schedule.Execute(UpdateSunPulse).Every(33);
        }

        private void UpdateSunPulse()
        {
            float phase = Mathf.Repeat(Time.unscaledTime / SunPulsePeriodSeconds, 1f);
            sunPulse = .5f - .5f * Mathf.Cos(phase * Mathf.PI * 2f);
            MarkDirtyRepaint();
        }
        public void SetDayProgress(float normalized)
        {
            dayProgress = Mathf.Repeat(normalized, 1);
            MarkDirtyRepaint();
        }
        private void Draw(MeshGenerationContext context)
        {
            Painter2D p = context.painter2D;
            Vector2 c = contentRect.center;
            float orbitRadius = radius > 0 ? radius : Mathf.Min(contentRect.width, contentRect.height) * .5f - 22;
            if (orbitRadius <= 0) return;
            if (lineWidth > 0)
            {
                p.strokeColor = Color.white; p.lineWidth = lineWidth;
                HudDrawing.Arc(p, c, orbitRadius, orbitRadius, 180, 180);
                // Separate arc segments keep the lower half dashed on every rendering backend.
                for (int i = 0; i < 6; i++)
                    HudDrawing.Arc(p, c, orbitRadius, orbitRadius, i * 30 + dashGap * .5f, 30 - dashGap);
                float halfTickLength = tickLength * .5f;
                for (float angle = 180; angle <= 360 && tickLength > 0; angle += 30)
                {
                    Vector2 tick = HudDrawing.Radial(angle);
                    HudDrawing.Line(p, c + tick * (orbitRadius - halfTickLength), c + tick * (orbitRadius + halfTickLength));
                }
            }
            // UI Y increases downward: midnight is bottom, noon is top.
            Vector2 d = HudDrawing.Radial(90 + dayProgress * 360);
            Vector2 sun = c + d * orbitRadius;
            //float sunRadius = Mathf.Min(16, orbitRadius * .18f);
            float sunRadius = orbitRadius * .14f * Mathf.Lerp(.9f, 1.1f, sunPulse);
            float sunMaskRadius = sunRadius + 4;
            p.fillColor = Color.black; HudDrawing.Arc(p, sun, sunMaskRadius, sunMaskRadius, 0, 360, true);
            p.lineWidth = 2;
            //p.strokeColor = HudDrawing.Yellow; HudDrawing.Arc(p, sun, sunRadius, sunRadius, 0, 360);
            Color sunColor = HudDrawing.Yellow;
            sunColor.a *= Mathf.Lerp(.75f, 1f, sunPulse);
            p.fillColor = sunColor;
            HudDrawing.Arc(p, sun, sunRadius, sunRadius, 0, 360, true);
        }
    }

    /// <summary>Frame dimensions use 1920x1080 design units and scale uniformly with this element's size.</summary>
    [UxmlElement]
    public partial class HudFrameElement : VisualElement
    {
        private const float MapFooterHeightFraction = .07f;
        private const float MinimumMapFooterHeight = 48f;
        private float marginValue = 20;
        private float cornerChamferValue = 46;
        private float topNotchWidthValue = 340;
        private float topNotchHeightValue = 18;
        private float bottomNotchWidthValue = 176;
        private float bottomNotchHeightValue = 32;
        private float topNotchChamferValue = 14;
        private float bottomNotchChamferValue = 14;
        private OpeningScanBand[] openingScanBands;
        private int openingScanMask;
        private float mapFrameProgress;

        [UxmlAttribute] public float margin { get => marginValue; set => SetDimension(ref marginValue, value); }
        [UxmlAttribute] public float cornerChamfer { get => cornerChamferValue; set => SetDimension(ref cornerChamferValue, value); }
        /// <summary>Width of the notch's inner horizontal edge, excluding both sloped sides.</summary>
        [UxmlAttribute] public float topNotchWidth { get => topNotchWidthValue; set => SetDimension(ref topNotchWidthValue, value); }
        [UxmlAttribute] public float topNotchHeight { get => topNotchHeightValue; set => SetDimension(ref topNotchHeightValue, value); }
        /// <summary>Width of the notch's inner horizontal edge, excluding both sloped sides.</summary>
        [UxmlAttribute] public float bottomNotchWidth { get => bottomNotchWidthValue; set => SetDimension(ref bottomNotchWidthValue, value); }
        [UxmlAttribute] public float bottomNotchHeight { get => bottomNotchHeightValue; set => SetDimension(ref bottomNotchHeightValue, value); }
        /// <summary>Horizontal width of each sloped notch side, independent of notch height.</summary>
        [UxmlAttribute] public float topNotchChamfer { get => topNotchChamferValue; set => SetDimension(ref topNotchChamferValue, value); }
        [UxmlAttribute] public float bottomNotchChamfer { get => bottomNotchChamferValue; set => SetDimension(ref bottomNotchChamferValue, value); }

        internal event System.Action ShapeChanged;

        private void SetDimension(ref float field, float value)
        {
            value = float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Max(0, value);
            if (field == value) return;
            field = value;
            MarkDirtyRepaint();
            ShapeChanged?.Invoke();
        }

        public HudFrameElement()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        internal void SetOpeningScan(OpeningScanBand[] bands, int mask)
        {
            openingScanBands = bands;
            if (openingScanMask == mask) return;
            openingScanMask = mask;
            MarkDirtyRepaint();
        }

        private static float DesignScale(Vector2 size) => Mathf.Min(size.x / 1920f, size.y / 1080f);

        internal Rect GetInteriorBounds(Vector2 size, float footerProgress)
        {
            float inset = Mathf.Min(margin * DesignScale(size), Mathf.Min(size.x, size.y) * .45f);
            float mapFooter = Mathf.Clamp(size.y * MapFooterHeightFraction, MinimumMapFooterHeight, size.y * .5f);
            float footer = Mathf.Lerp(inset, Mathf.Max(inset, mapFooter), footerProgress);
            return Rect.MinMaxRect(inset, inset, size.x - inset, size.y - footer);
        }

        internal (float width, float bevel, float depth) GetBottomNotch(Vector2 size, Rect bounds)
        {
            float scale = DesignScale(size);
            float maxWidth = Mathf.Max(0, bounds.width - 2 * GetCornerCut(size, bounds));
            float bevel = Mathf.Min(bottomNotchChamfer * scale, maxWidth * .5f);
            float width = Mathf.Min(bottomNotchWidth * scale, Mathf.Max(0, maxWidth - 2 * bevel));
            float depth = Mathf.Min(bottomNotchHeight * scale, bounds.height * .4f);
            return (width, bevel, depth);
        }

        private float GetCornerCut(Vector2 size, Rect bounds) =>
            Mathf.Min(cornerChamfer * DesignScale(size), Mathf.Min(bounds.width, bounds.height) * .5f);

        private (float width, float bevel, float depth) GetTopNotch(Vector2 size, Rect bounds, float cut)
        {
            float scale = DesignScale(size);
            float maxWidth = Mathf.Max(0, bounds.width - 2 * cut);
            float bevel = Mathf.Min(topNotchChamfer * scale, maxWidth * .5f);
            float width = Mathf.Min(topNotchWidth * scale, Mathf.Max(0, maxWidth - 2 * bevel));
            float depth = Mathf.Lerp(Mathf.Min(topNotchHeight * scale, bounds.height * .4f), 0f, mapFrameProgress);
            return (width, bevel, depth);
        }

        internal (Rect bounds, float bevel) GetCompassNotch(Vector2 size)
        {
            Rect bounds = GetInteriorBounds(size, mapFrameProgress);
            var notch = GetTopNotch(size, bounds, GetCornerCut(size, bounds));
            return (new Rect(bounds.center.x - notch.width * .5f, bounds.yMin, notch.width, notch.depth), notch.bevel);
        }

        internal (float cut, float height, float hiddenTopHalfWidth) GetMapRulerGeometry(Vector2 size)
        {
            Rect bounds = GetInteriorBounds(size, mapFrameProgress);
            float cut = GetCornerCut(size, bounds);
            var notch = GetTopNotch(size, bounds, cut);
            return (cut, bounds.height, notch.depth > 0f ? notch.width * .5f + notch.bevel : 0f);
        }

        internal bool SetMapFrameProgress(float progress)
        {
            if (mapFrameProgress == progress) return false;
            mapFrameProgress = progress;
            MarkDirtyRepaint();
            return true;
        }

        private void Draw(MeshGenerationContext context)
        {
            Painter2D p = context.painter2D;
            float w = contentRect.width, h = contentRect.height;
            if (w <= 0 || h <= 0) return;
            Vector2 size = contentRect.size;
            float scale = DesignScale(size);
            Rect bounds = GetInteriorBounds(size, mapFrameProgress);
            float left = bounds.xMin, right = bounds.xMax, topEdge = bounds.yMin, bottomEdge = bounds.yMax;
            float cut = GetCornerCut(size, bounds);
            var top = GetTopNotch(size, bounds, cut);
            var bottomNotch = GetBottomNotch(size, bounds);
            Vector2[] bottom = Notch(w * .5f, bottomEdge, bottomNotch.width, bottomNotch.depth, bottomNotch.bevel, -1);
            var border = new System.Collections.Generic.List<Vector2> { new Vector2(left + cut, topEdge) };
            if (top.depth > 0f)
                border.AddRange(Notch(w * .5f, topEdge, top.width, top.depth, top.bevel, 1));
            border.Add(new Vector2(right - cut, topEdge));
            border.Add(new Vector2(right, topEdge + cut));
            border.Add(new Vector2(right, bottomEdge - cut));
            border.Add(new Vector2(right - cut, bottomEdge));
            // Traverse the bottom indentation from right to left to keep the border continuous.
            for (int i = bottom.Length - 1; i >= 0; i--) border.Add(bottom[i]);
            border.Add(new Vector2(left + cut, bottomEdge));
            border.Add(new Vector2(left, bottomEdge - cut));
            border.Add(new Vector2(left, topEdge + cut));
            Vector2[] points = border.ToArray();

            // Fill the outside as one path so the moving notches cannot expose seams between masks.
            p.fillColor = Color.black;
            p.BeginPath();
            p.MoveTo(Vector2.zero);
            p.LineTo(new Vector2(w, 0f));
            p.LineTo(new Vector2(w, h));
            p.LineTo(new Vector2(0f, h));
            p.ClosePath();
            p.MoveTo(points[0]);
            for (int i = 1; i < points.Length; i++) p.LineTo(points[i]);
            p.ClosePath();
            p.Fill(FillRule.OddEven);

            p.strokeColor = Color.white;
            p.lineWidth = 1.5f * scale;
            if (openingScanMask == 0) HudDrawing.Polygon(p, points);
            else DrawOpeningOutline(p, points, w, h);
        }

        private void DrawOpeningOutline(Painter2D painter, Vector2[] points, float width, float height)
        {
            // Project onto the preview's 118-degree scan direction in the native frame's viewport.
            Vector2 axis = HudDrawing.Radial(28f);
            float span = axis.x * width + axis.y * height;
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 a = points[i], b = points[(i + 1) % points.Length];
                float from = Vector2.Dot(axis, a) / span, to = Vector2.Dot(axis, b) / span;
                if (to < from)
                {
                    (a, b) = (b, a);
                    (from, to) = (to, from);
                }
                float cursor = 0f;
                for (int j = 0; j < openingScanBands.Length; j++)
                {
                    if ((openingScanMask & (1 << j)) == 0) continue;
                    OpeningScanBand cut = openingScanBands[j];
                    if (cut.To <= from || cut.From >= to) continue;
                    if (Mathf.Approximately(from, to)) { cursor = 1f; break; }
                    float start = Mathf.Clamp01((cut.From - from) / (to - from));
                    if (start > cursor) HudDrawing.Line(painter, Vector2.Lerp(a, b, cursor), Vector2.Lerp(a, b, start));
                    cursor = Mathf.Clamp01((cut.To - from) / (to - from));
                }
                if (cursor < 1f) HudDrawing.Line(painter, Vector2.Lerp(a, b, cursor), b);
            }
        }

        private static Vector2[] Notch(float x, float edge, float width, float height, float bevel, float direction)
        {
            float half = width * .5f;
            return new[] {
                new Vector2(x - half - bevel, edge),
                new Vector2(x - half, edge + direction * height),
                new Vector2(x + half, edge + direction * height),
                new Vector2(x + half + bevel, edge)
            };
        }
    }
}
