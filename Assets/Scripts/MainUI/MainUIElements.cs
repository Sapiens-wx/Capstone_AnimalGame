using UnityEngine;
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
        private float tickLengthValue = 12;
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
        public float Radius => Mathf.Max(0, Mathf.Min(contentRect.width, contentRect.height) * .5f
            - lineWidth * .5f - (ticks ? tickLength : 0));

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
            HudDrawing.Arc(p, center, radius, radius, 0, 360);
            if (!ticks) return;
            p.lineWidth = lineWidth * 1.6f;
            for (int i = 0; i < 12; i++)
            {
                Vector2 d = HudDrawing.Radial(i * 30);
                HudDrawing.Line(p, center + d * radius, center + d * (radius + tickLength));
            }
        }
    }

    /// <summary>Normalized radial progress: the endpoint retreats counterclockwise as progress is consumed.</summary>
    [UxmlElement]
    public partial class CircularProgressElement : VisualElement
    {
        private float value = 1;
        [UxmlAttribute] public float progress { get => value; set => SetProgress(value); }
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

        private void Draw(MeshGenerationContext context)
        {
            Painter2D p = context.painter2D;
            Vector2 c = contentRect.center;
            float trackRadius = Mathf.Min(contentRect.width, contentRect.height) * .5f - 10;
            if (trackRadius <= 0) return;
            p.lineWidth = 2; p.strokeColor = Color.white;
            HudDrawing.Arc(p, c, trackRadius, trackRadius, 0, 360);
            float radius = trackRadius + (outside ? 4 : -4);
            p.lineWidth = lineWidth; p.strokeColor = HudDrawing.Yellow;
            if (value > 0) HudDrawing.Arc(p, c, radius, radius, 0, value * 360);
            // At zero and one the two endpoints coincide, so draw their short line only once.
            Endcap(p, c, radius, 0);
            if (value > 0 && value < 1) Endcap(p, c, radius, value * 360);
        }

        private static void Endcap(Painter2D p, Vector2 center, float radius, float degrees)
        {
            Vector2 d = HudDrawing.Radial(degrees);
            HudDrawing.Line(p, center + d * (radius - 3), center + d * (radius + 9));
        }
    }

    [UxmlElement]
    public partial class CompassElement : VisualElement
    {
        private readonly Label[] labels = new Label[9];
        private float heading;
        public float Heading => heading;

        public CompassElement()
        {
            pickingMode = PickingMode.Ignore;
            style.overflow = Overflow.Hidden;
            for (int i = 0; i < labels.Length; i++)
            {
                var label = new Label();
                label.style.position = Position.Absolute;
                label.AddToClassList("compass-label");
                label.pickingMode = PickingMode.Ignore;
                labels[i] = label;
                Add(label);
            }
            RegisterCallback<GeometryChangedEvent>(_ => UpdateLabels());
            generateVisualContent += Draw;
        }

        public void SetHeading(float degrees)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees)) return;
            heading = Mathf.Repeat(degrees, 360);
            UpdateLabels();
            MarkDirtyRepaint();
        }

        private float X(float degrees) => contentRect.width * (.5f + (degrees - heading) / 180);

        private void UpdateLabels()
        {
            float start = Mathf.Floor(heading / 30) * 30 - 120;
            for (int i = 0; i < labels.Length; i++)
            {
                float degrees = start + i * 30;
                int normalized = Mathf.RoundToInt(Mathf.Repeat(degrees, 360));
                labels[i].text = normalized switch { 0 => "N", 90 => "E", 180 => "S", 270 => "W", _ => normalized + "°" };
                labels[i].style.left = X(degrees) - 32;
                labels[i].style.top = 0;
            }
        }

        private void Draw(MeshGenerationContext context)
        {
            Painter2D p = context.painter2D;
            p.lineWidth = 2; p.strokeColor = Color.white;
            float y = contentRect.height - 3;
            HudDrawing.Line(p, new Vector2(0, y), new Vector2(contentRect.width, y));
            int start = Mathf.FloorToInt((heading - 90) / 10) * 10;
            for (int degree = start; degree <= heading + 90; degree += 10)
            {
                float x = X(degree);
                if (x < 1 || x > contentRect.width - 1) continue;
                HudDrawing.Line(p, new Vector2(x, y), new Vector2(x, y - (degree % 30 == 0 ? 20 : 10)));
            }
        }
    }

    [UxmlElement]
    public partial class CompassPointerElement : VisualElement
    {
        public CompassPointerElement()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += context =>
            {
                Painter2D p = context.painter2D; p.fillColor = HudDrawing.Yellow;
                HudDrawing.Polygon(p, new[] { new Vector2(contentRect.width * .5f, 0),
                    new Vector2(contentRect.width, contentRect.height), new Vector2(0, contentRect.height) }, true);
            };
        }
    }

    [UxmlElement]
    public partial class TimeOrbitElement : VisualElement
    {
        private float dayProgress = .25f;
        public TimeOrbitElement()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
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
            float rx = contentRect.width * .5f - 12, ry = contentRect.height * .5f - 12;
            if (rx <= 0 || ry <= 0) return;
            p.strokeColor = Color.white; p.lineWidth = 2;
            HudDrawing.Arc(p, c, rx, ry, 180, 180);
            // Actual separate arc segments keep the lower ellipse dashed on every rendering backend.
            for (float angle = 0; angle < 180; angle += 6) HudDrawing.Arc(p, c, rx, ry, angle, 3);
            HudDrawing.Line(p, c + Vector2.left * (rx + 7), c + Vector2.right * (rx + 7));
            Vector2 d = HudDrawing.Radial(180 + dayProgress * 360);
            Vector2 sun = c + new Vector2(d.x * rx, d.y * ry);
            p.fillColor = Color.black; HudDrawing.Arc(p, sun, 10, 10, 0, 360, true);
            p.fillColor = HudDrawing.Yellow; HudDrawing.Arc(p, sun, 6.5f, 6.5f, 0, 360, true);
        }
    }

    /// <summary>Opaque only outside the authored cut-corner boundary. Margin is measured in screen pixels.</summary>
    [UxmlElement]
    public partial class HudFrameElement : VisualElement
    {
        public float PanelUnitsPerPixel { get; set; } = 1;
        private float marginPixelsValue = 20;
        [UxmlAttribute]
        public float marginPixels
        {
            get => marginPixelsValue;
            set
            {
                if (marginPixelsValue == value) return;
                marginPixelsValue = value;
                MarkDirtyRepaint();
            }
        }
        public HudFrameElement()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }
        private void Draw(MeshGenerationContext context)
        {
            float w = contentRect.width, h = contentRect.height;
            if (w < 100 || h < 100) return;
            float inset = marginPixels * PanelUnitsPerPixel;
            float cut = Mathf.Min(46 * PanelUnitsPerPixel, Mathf.Min(w, h) * .1f);
            float topHalf = Mathf.Min(170 * PanelUnitsPerPixel, w * .2f);
            float bottomHalf = Mathf.Min(88 * PanelUnitsPerPixel, w * .15f);
            float notch = 18 * PanelUnitsPerPixel;
            float x = w * .5f;
            Painter2D p = context.painter2D;
            p.fillColor = Color.black;
            HudDrawing.Rect(p, 0, 0, w, inset);
            HudDrawing.Rect(p, 0, h - inset, w, inset);
            HudDrawing.Rect(p, 0, inset, inset, h - 2 * inset);
            HudDrawing.Rect(p, w - inset, inset, inset, h - 2 * inset);
            HudDrawing.Rect(p, x - topHalf, inset, 2 * topHalf, notch);
            HudDrawing.Polygon(p, new[] { new Vector2(x - bottomHalf, h - inset),
                new Vector2(x - bottomHalf, h - inset - notch), new Vector2(x - bottomHalf + 14, h - inset - notch - 14),
                new Vector2(x + bottomHalf - 14, h - inset - notch - 14), new Vector2(x + bottomHalf, h - inset - notch),
                new Vector2(x + bottomHalf, h - inset) }, true);
            HudDrawing.Polygon(p, new[] { new Vector2(inset, inset), new Vector2(inset + cut, inset), new Vector2(inset, inset + cut) }, true);
            HudDrawing.Polygon(p, new[] { new Vector2(w - inset, inset), new Vector2(w - inset - cut, inset), new Vector2(w - inset, inset + cut) }, true);
            HudDrawing.Polygon(p, new[] { new Vector2(inset, h - inset), new Vector2(inset + cut, h - inset), new Vector2(inset, h - inset - cut) }, true);
            HudDrawing.Polygon(p, new[] { new Vector2(w - inset, h - inset), new Vector2(w - inset - cut, h - inset), new Vector2(w - inset, h - inset - cut) }, true);
            p.strokeColor = Color.white; p.lineWidth = 1.5f * PanelUnitsPerPixel;
            HudDrawing.Polygon(p, new[] {
                new Vector2(inset + cut, inset), new Vector2(x - topHalf, inset),
                new Vector2(x - topHalf, inset + notch), new Vector2(x + topHalf, inset + notch),
                new Vector2(x + topHalf, inset), new Vector2(w - inset - cut, inset),
                new Vector2(w - inset, inset + cut), new Vector2(w - inset, h - inset - cut),
                new Vector2(w - inset - cut, h - inset), new Vector2(x + bottomHalf, h - inset),
                new Vector2(x + bottomHalf, h - inset - notch), new Vector2(x + bottomHalf - 14, h - inset - notch - 14),
                new Vector2(x - bottomHalf + 14, h - inset - notch - 14), new Vector2(x - bottomHalf, h - inset - notch),
                new Vector2(x - bottomHalf, h - inset), new Vector2(inset + cut, h - inset),
                new Vector2(inset, h - inset - cut), new Vector2(inset, inset + cut) });
        }
    }
}
