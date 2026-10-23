using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.MainUI
{
    /// <summary>Centered tilt readout with one rotating line on each side.</summary>
    [UxmlElement]
    public partial class LevelIndicatorElement : VisualElement
    {
        private readonly Label readout;
        private float widthValue = 2, lengthValue = 360, heightValue = 180;
        private float centerMarginValue = 64;
        private float degreeValue;

        [UxmlAttribute] public float width { get => widthValue; set => SetDimension(ref widthValue, value); }
        [UxmlAttribute] public float length { get => lengthValue; set => SetDimension(ref lengthValue, value); }
        [UxmlAttribute] public float height { get => heightValue; set => SetDimension(ref heightValue, value); }
        /// <summary>Total gap between the two lines, across the readout.</summary>
        [UxmlAttribute] public float centerMargin { get => centerMarginValue; set => SetDimension(ref centerMarginValue, value); }
        [UxmlAttribute] public float degree { get => degreeValue; set => SetDegree(value); }

        public LevelIndicatorElement()
        {
            pickingMode = PickingMode.Ignore;
            readout = new Label("0\u00b0") { name = "DegreeText", pickingMode = PickingMode.Ignore };
            readout.AddToClassList("level-degree");
            readout.style.position = Position.Absolute;
            readout.style.unityTextAlign = TextAnchor.MiddleCenter;
            readout.style.marginLeft = readout.style.marginRight = 0;
            readout.style.marginTop = readout.style.marginBottom = 0;
            readout.style.paddingLeft = readout.style.paddingRight = 0;
            readout.style.paddingTop = readout.style.paddingBottom = 0;
            readout.style.fontSize = 20;
            readout.style.unityFontStyleAndWeight = FontStyle.Normal;
            Add(readout);
            RegisterCallback<GeometryChangedEvent>(_ => CenterReadout());
            generateVisualContent += Draw;
        }

        /// <summary>Runtime API; clamps to -90–90 degrees and refreshes both the text and lines.</summary>
        public void SetDegree(float value)
        {
            value = Finite(value) ? Mathf.Clamp(value, -90, 90) : 0;
            if (degreeValue == value) return;
            degreeValue = value;
            int displayedDegree = Mathf.RoundToInt(value);
            readout.text = displayedDegree + "\u00b0";
            Color indicatorColor = Mathf.Abs(displayedDegree) > 12
                ? HudDrawing.Yellow
                : Color.white;
            style.color = indicatorColor;
            readout.style.color = indicatorColor;
            MarkDirtyRepaint();
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private void SetDimension(ref float field, float value, float minimum = 0)
        {
            value = Finite(value) ? Mathf.Max(minimum, value) : minimum;
            if (field == value) return;
            field = value;
            MarkDirtyRepaint();
        }

        private void CenterReadout()
        {
            Rect rect = contentRect;
            readout.style.left = rect.x;
            readout.style.top = rect.y;
            readout.style.width = rect.width;
            readout.style.height = rect.height;
        }

        private void Draw(MeshGenerationContext context)
        {
            float degreeNormalized = Mathf.Abs(degree) / 90;
            float span = Mathf.Lerp(length, height, 1 - Mathf.Pow(1 - degreeNormalized, 3));
            float half = span * .5f;
            float inner = Mathf.Min(centerMargin * .5f, half);
            if (width <= 0 || half <= inner) return;

            Painter2D painter = context.painter2D;
            painter.strokeColor = resolvedStyle.color;
            painter.lineWidth = width;
            painter.lineCap = LineCap.Butt;
            Vector2 center = contentRect.center;
            Vector2 direction = HudDrawing.Radial(degree);
            for (int sign = -1; sign <= 1; sign += 2)
            {
                Vector2 axis = direction * sign;
                HudDrawing.Line(painter, center + axis * inner, center + axis * half);
            }
        }
    }
}
