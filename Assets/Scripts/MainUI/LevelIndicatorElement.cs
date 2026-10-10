using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.MainUI
{
    /// <summary>Centered tilt readout with rotating long/short segments on each side.</summary>
    [UxmlElement]
    public partial class LevelIndicatorElement : VisualElement
    {
        private readonly Label readout;
        private float widthValue = 2, lengthValue = 360, heightValue = 180;
        private float centerMarginValue = 64, lineMarginValue = 12, lineRatioValue = 2;
        private float degreeValue;

        [UxmlAttribute] public float width { get => widthValue; set => SetDimension(ref widthValue, value); }
        [UxmlAttribute] public float length { get => lengthValue; set => SetDimension(ref lengthValue, value); }
        [UxmlAttribute] public float height { get => heightValue; set => SetDimension(ref heightValue, value); }
        /// <summary>Total gap between the two inner segments, across the readout.</summary>
        [UxmlAttribute] public float centerMargin { get => centerMarginValue; set => SetDimension(ref centerMarginValue, value); }
        [UxmlAttribute] public float lineMargin { get => lineMarginValue; set => SetDimension(ref lineMarginValue, value); }
        /// <summary>Length of the outer long segment divided by the inner short segment.</summary>
        [UxmlAttribute] public float lineRatio { get => lineRatioValue; set => SetDimension(ref lineRatioValue, value, 1); }
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
            readout.style.unityFontStyleAndWeight=FontStyle.Bold;
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
            readout.text = Mathf.RoundToInt(value) + "\u00b0";
            MarkDirtyRepaint();
        }

        /// <summary>Signed balance: positive is right/forward, negative is left/backward; absolute 1 is the support boundary.</summary>
        public void SetBalance(float signedMagnitude, bool tippedOver = false)
        {
            signedMagnitude = Finite(signedMagnitude) ? signedMagnitude : 0;
            SetDegree(tippedOver ? (signedMagnitude < 0 ? -90 : 90) : Mathf.Clamp(signedMagnitude, -1, 1) * 90);
        }

        /// <summary>The dominant local axis determines the sign; ties prefer right/left (x).</summary>
        public void SetBalance(Vector2 localOffset, bool tippedOver = false)
        {
            if (!Finite(localOffset.x) || !Finite(localOffset.y)) localOffset = Vector2.zero;
            float axis = Mathf.Abs(localOffset.x) >= Mathf.Abs(localOffset.y) ? localOffset.x : localOffset.y;
            SetBalance(localOffset.magnitude * (axis < 0 ? -1 : 1), tippedOver);
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
            float gap = Mathf.Min(Mathf.Lerp(lineMargin, 0, degreeNormalized), half - inner);
            float shortLength = (half - inner - gap) / (lineRatio + 1);
            if (width <= 0 || shortLength <= 0) return;

            Painter2D painter = context.painter2D;
            painter.strokeColor = resolvedStyle.color;
            painter.lineWidth = width;
            painter.lineCap = LineCap.Butt;
            Vector2 center = contentRect.center;
            Vector2 direction = HudDrawing.Radial(degree);
            for (int sign = -1; sign <= 1; sign += 2)
            {
                Vector2 axis = direction * sign;
                HudDrawing.Line(painter, center + axis * inner, center + axis * (inner + shortLength));
                HudDrawing.Line(painter, center + axis * (inner + shortLength + gap), center + axis * half);
            }
        }
    }
}
