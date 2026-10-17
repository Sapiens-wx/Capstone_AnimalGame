using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.MainUI
{
    /// <summary>A moving sine wave with a centered Gaussian envelope and two white end lines.</summary>
    [UxmlElement]
    public partial class SoundElement : VisualElement
    {
        private int pointCount = 97;
        private float frequency = 5f, maximumAmplitude = .85f, waveLineWidth = 2f;
        private float amplitude, damping = 120f;
        private float phase, phaseSin, phaseCos = 1f;
        private float[] sampleX, carrier, carrierSine, xSquared, envelope;
        private bool carrierDirty = true, envelopeDirty = true;

        internal void ConfigureWaveform(int points, float cycles, float height, float strokeWidth)
        {
            points = Mathf.Clamp(points, 16, 512);
            cycles = Mathf.Clamp(cycles, .5f, 16f);
            height = Mathf.Clamp01(height);
            strokeWidth = Mathf.Max(.1f, strokeWidth);
            if (pointCount == points && frequency == cycles && maximumAmplitude == height && waveLineWidth == strokeWidth) return;
            carrierDirty |= pointCount != points || frequency != cycles;
            envelopeDirty |= pointCount != points;
            pointCount = points;
            frequency = cycles;
            maximumAmplitude = height;
            waveLineWidth = strokeWidth;
            MarkDirtyRepaint();
        }

        internal void SetWaveState(float strength, float gaussianDamping)
        {
            strength = Mathf.Clamp01(strength);
            gaussianDamping = Mathf.Max(0f, gaussianDamping);
            if (amplitude == strength && damping == gaussianDamping) return;
            envelopeDirty |= damping != gaussianDamping;
            amplitude = strength;
            damping = gaussianDamping;
            MarkDirtyRepaint();
        }

        internal void SetWavePhase(float radians)
        {
            if (phase == radians) return;
            phase = radians;
            // Only two trig calls per phase update, independent of point count.
            phaseSin = Mathf.Sin(radians);
            phaseCos = Mathf.Cos(radians);
            if (amplitude > 0f) MarkDirtyRepaint();
        }

        private void RefreshSamples()
        {
            if (sampleX == null || sampleX.Length != pointCount)
            {
                sampleX = new float[pointCount];
                carrier = new float[pointCount];
                carrierSine = new float[pointCount];
                xSquared = new float[pointCount];
                envelope = new float[pointCount];
                carrierDirty = envelopeDirty = true;
            }
            if (carrierDirty)
            {
                for (int i = 0; i < pointCount; i++)
                {
                    sampleX[i] = (float)i / (pointCount - 1);
                    float x = sampleX[i] * 2f - 1f;
                    xSquared[i] = x * x;
                    float angle = Mathf.PI * frequency * x;
                    carrier[i] = Mathf.Cos(angle);
                    carrierSine[i] = Mathf.Sin(angle);
                }
                carrierDirty = false;
            }
            if (envelopeDirty)
            {
                for (int i = 0; i < pointCount; i++)
                    envelope[i] = damping == 0f ? 1f : Mathf.Exp(-damping * xSquared[i]);
                envelopeDirty = false;
            }
        }

        private float lineWidthValue = 2;
        /// <summary>Stroke width of the white end lines only.</summary>
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

        private float lineLengthValue = 72;
        [UxmlAttribute]
        public float lineLength
        {
            get => lineLengthValue;
            set
            {
                value = float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Max(0, value);
                if (lineLengthValue == value) return;
                lineLengthValue = value;
                MarkDirtyRepaint();
            }
        }

        public SoundElement()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        private void Draw(MeshGenerationContext context)
        {
            Rect rect = contentRect;
            if (rect.width <= 2 || rect.height <= 2) return;
            float left = rect.xMin + 1, right = rect.xMax - 1;
            float top = rect.yMin + 1, bottom = rect.yMax - 1;
            float halfLength = Mathf.Min(lineLength, bottom - top) * .5f;
            Painter2D p = context.painter2D;
            p.lineWidth = waveLineWidth;
            p.strokeColor = HudDrawing.Yellow;
            if (amplitude <= 0f)
                HudDrawing.Line(p, new Vector2(left, rect.center.y), new Vector2(right, rect.center.y));
            else
            {
                RefreshSamples();
                float height = Mathf.Max(0f, (bottom - top - waveLineWidth) * .5f) * maximumAmplitude * amplitude;
                p.BeginPath();
                for (int i = 0; i < pointCount; i++)
                {
                    var point = new Vector2(Mathf.Lerp(left, right, sampleX[i]),
                        rect.center.y - height * envelope[i]
                            * (carrier[i] * phaseCos - carrierSine[i] * phaseSin));
                    if (i == 0) p.MoveTo(point);
                    else p.LineTo(point);
                }
                p.Stroke();
            }
            if (lineWidth <= 0) return;
            p.lineWidth = lineWidth;
            p.strokeColor = Color.white;
            HudDrawing.Line(p, new Vector2(left, rect.center.y - halfLength), new Vector2(left, rect.center.y + halfLength));
            HudDrawing.Line(p, new Vector2(right, rect.center.y - halfLength), new Vector2(right, rect.center.y + halfLength));
        }
    }
}
