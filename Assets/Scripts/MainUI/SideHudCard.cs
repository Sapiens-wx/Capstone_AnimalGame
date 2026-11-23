using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.MainUI
{
    internal static class SideHudCard
    {
        internal const float DesignWidth = 1920f;
        internal const float DesignHeight = 1080f;
        internal const float PanelWidth = 355f;
        internal const float PanelHeight = 565f;
        internal static readonly Color Gold = new Color(212f / 255f, 184f / 255f, 84f / 255f);
        internal static readonly Color Muted = new Color(.72f, .74f, .74f);

        internal const float TabWidth = 58f;
        internal const float TabHeight = 218f;
        internal static readonly Color Divider = new Color(.8f, .82f, .81f, .65f);

        internal static float ScreenFit(Vector2 size) => Mathf.Min(size.x / DesignWidth, size.y / DesignHeight);

        internal static void PositionPanel(VisualElement panel, Vector2 size, float fit, float openness = 1f)
        {
            panel.style.scale = new Scale(new Vector3(fit, fit, 1f));
            // When closed, the right-side card sits past the viewport edge.
            panel.style.left = size.x - (PanelWidth + 78f) * fit * openness;
            panel.style.top = (size.y - DesignHeight * fit) * .5f + 264f * fit;
        }

        internal static void PositionTab(VisualElement tab, Vector2 size, float fit)
        {
            tab.style.scale = new Scale(new Vector3(fit, fit, 1f));
            tab.style.left = size.x - 88f * fit;
            tab.style.top = (size.y - TabHeight * fit) * .5f;
        }

        internal static void FitText(Label label, int preferredSize, int minimumSize)
        {
            if (label == null || label.panel == null || label.resolvedStyle.display == DisplayStyle.None) return;
            Vector2 box = label.contentRect.size;
            float measuredFontSize = label.resolvedStyle.fontSize;
            if (box.x <= 0f || box.y <= 0f || measuredFontSize <= 0f) return;
            bool wrap = label.resolvedStyle.whiteSpace == WhiteSpace.Normal;
            int fittedSize = minimumSize;
            for (int candidate = preferredSize; candidate >= minimumSize; candidate--)
            {
                // Measure against the currently resolved font without resetting it first. Scaling
                // the wrap width predicts the candidate size even before UI Toolkit resolves styles.
                float scale = measuredFontSize / candidate;
                Vector2 measured = label.MeasureTextSize(label.text, wrap ? (box.x - 1f) * scale : 0f,
                    wrap ? VisualElement.MeasureMode.AtMost : VisualElement.MeasureMode.Undefined,
                    0f, VisualElement.MeasureMode.Undefined);
                if (measured.y <= (box.y - 1f) * scale && (wrap || measured.x <= (box.x - 1f) * scale))
                { fittedSize = candidate; break; }
            }
            if (Mathf.Abs(label.style.fontSize.value.value - fittedSize) > .01f) label.style.fontSize = fittedSize;
        }

        internal static Label AddLabel(VisualElement parent, string text, float x, float y,
            float width, float height, int fontSize, Color color, bool bold = false)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            SetRect(label, x, y, width, height);
            label.style.fontSize = fontSize;
            label.style.color = color;
            label.style.marginLeft = label.style.marginRight = label.style.marginTop = label.style.marginBottom = 0;
            label.style.paddingLeft = label.style.paddingRight = label.style.paddingTop = label.style.paddingBottom = 0;
            label.style.unityFontStyleAndWeight = bold ? FontStyle.Bold : FontStyle.Normal;
            label.style.unityTextAlign = TextAnchor.UpperLeft;
            parent.Add(label);
            return label;
        }

        internal static void SetRect(VisualElement element, float x, float y, float width, float height)
        {
            element.style.position = Position.Absolute;
            element.style.left = x; element.style.top = y;
            element.style.width = width; element.style.height = height;
        }

        internal static void FillParent(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = element.style.top = element.style.right = element.style.bottom = 0;
        }

        internal static void AddLine(VisualElement parent, float x, float y, float width, Color color)
        {
            var line = new VisualElement { pickingMode = PickingMode.Ignore };
            SetRect(line, x, y, width, 1);
            line.style.backgroundColor = color;
            parent.Add(line);
        }

        internal static void Stroke(Painter2D painter, params Vector2[] points)
        {
            if (points.Length < 2) return;
            painter.BeginPath(); painter.MoveTo(points[0]);
            for (int i = 1; i < points.Length; i++) painter.LineTo(points[i]);
            painter.Stroke();
        }

        internal sealed class PanelFrame : VisualElement
        {
            public bool IsTab;
            public bool Successful;

            public PanelFrame()
            {
                pickingMode = PickingMode.Ignore;
                generateVisualContent += Draw;
            }

            private void Draw(MeshGenerationContext context)
            {
                float w = contentRect.width, h = contentRect.height;
                if (w <= 0 || h <= 0) return;
                float cut = IsTab ? 10 : 22;
                Painter2D p = context.painter2D;
                p.fillColor = new Color(.015f, .021f, .021f, .93f);
                p.BeginPath(); p.MoveTo(new Vector2(cut, 1));
                p.LineTo(new Vector2(w - cut, 1)); p.LineTo(new Vector2(w - 1, cut));
                p.LineTo(new Vector2(w - 1, h - cut)); p.LineTo(new Vector2(w - cut, h - 1));
                p.LineTo(new Vector2(cut, h - 1)); p.LineTo(new Vector2(1, h - cut));
                p.LineTo(new Vector2(1, cut)); p.ClosePath(); p.Fill();
                p.strokeColor = new Color(.91f, .93f, .92f, .95f); p.lineWidth = 1.6f;
                p.Stroke();
                p.strokeColor = Gold; p.lineWidth = 2.3f;
                Stroke(p, new Vector2(1, 42), new Vector2(1, cut), new Vector2(cut, 1), new Vector2(IsTab ? w - cut : 77, 1));
                Stroke(p, new Vector2(w - (IsTab ? 22 : 72), h - 1), new Vector2(w - cut, h - 1),
                    new Vector2(w - 1, h - cut), new Vector2(w - 1, h - (IsTab ? 33 : 46)));
                if (Successful && !IsTab)
                {
                    p.lineWidth = 2;
                    Stroke(p, new Vector2(w - 36, 92), new Vector2(w - 30, 98), new Vector2(w - 19, 85));
                }
            }
        }
    }
}
