using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.MainUI
{
    /// <summary>Clips the compass to the frame's filled area and draws the shared border.</summary>
    [UxmlElement]
    public partial class CompassHostElement : VisualElement
    {
        private HudFrameElement frame;
        private VectorImage mask;
        private Vector2[] border;
        private Vector2 frameSize, maskOrigin, maskSize;
        private bool shapeDirty = true;
        private float lineWidthValue = 1.5f;
        private readonly IVisualElementScheduledItem maskRefresh;

        /// <summary>Border width in 1920x1080 design units.</summary>
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

        public CompassHostElement()
        {
            pickingMode = PickingMode.Ignore;
            style.overflow = Overflow.Hidden;
            style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
            RegisterCallback<AttachToPanelEvent>(_ => BindFrame());
            RegisterCallback<DetachFromPanelEvent>(_ => ReleaseMask());
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            generateVisualContent += DrawBorder;
            // Run once after style/layout changes have resolved, coalescing notifications.
            maskRefresh = schedule.Execute(RefreshMask);
            maskRefresh.Pause();
        }

        private void BindFrame()
        {
            VisualElement scope = this;
            while (scope.parent != null && scope.Q<HudFrameElement>("Frame") == null) scope = scope.parent;
            var nextFrame = scope.Q<HudFrameElement>("Frame");
            if (frame == nextFrame) return;
            UnsubscribeFrame();
            frame = nextFrame;
            if (frame != null)
            {
                frame.ShapeChanged += InvalidateShape;
                frame.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            }
            InvalidateShape();
        }

        private void InvalidateShape()
        {
            shapeDirty = true;
            RequestMaskRefresh();
        }

        private void OnGeometryChanged(GeometryChangedEvent evt) => RequestMaskRefresh();

        /// <summary>Notify after changing transforms, which do not emit GeometryChangedEvent.</summary>
        public void RequestMaskRefresh()
        {
            if (panel != null) maskRefresh.ExecuteLater(0);
        }

        private void UnsubscribeFrame()
        {
            if (frame == null) return;
            frame.ShapeChanged -= InvalidateShape;
            frame.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        private void RefreshMask()
        {
            if (frame == null || frame.panel != panel) BindFrame();
            if (frame == null || frame.panel != panel) return;
            Vector2 size = frame.contentRect.size;
            if (size.x <= 0 || size.y <= 0 || contentRect.width <= 0 || contentRect.height <= 0) return;
            if (mask == null || shapeDirty || frameSize != size)
            {
                var nextMask = ScriptableObject.CreateInstance<VectorImage>();
                nextMask.hideFlags = HideFlags.HideAndDontSave;
                using (var painter = new Painter2D())
                {
                    border = frame.DrawShape(painter, false);
                    if (!painter.SaveToVectorImage(nextMask))
                    {
                        DestroyMask(nextMask);
                        return;
                    }
                }
                style.backgroundImage = Background.FromVectorImage(nextMask);
                DestroyMask(mask);
                mask = nextMask;
                frameSize = size;
                shapeDirty = false;
                maskSize = Vector2.zero;
                MarkDirtyRepaint();
            }

            Vector2 origin = frame.ChangeCoordinatesTo(this, Vector2.zero);
            Vector2 extent = frame.ChangeCoordinatesTo(this, size) - origin;
            if (extent.x <= 0 || extent.y <= 0) return;
            if (maskOrigin == origin && maskSize == extent) return;
            maskOrigin = origin;
            maskSize = extent;
            style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left, new Length(origin.x));
            style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top, new Length(origin.y));
            style.backgroundSize = new BackgroundSize(new Length(extent.x), new Length(extent.y));
            MarkDirtyRepaint();
        }

        private void DrawBorder(MeshGenerationContext context)
        {
            if (border == null || mask == null || lineWidth <= 0 || frameSize.x <= 0 || frameSize.y <= 0) return;
            Vector2 scale = new Vector2(maskSize.x / frameSize.x, maskSize.y / frameSize.y);
            Painter2D p = context.painter2D;
            p.strokeColor = Color.white;
            p.lineWidth = lineWidth * Mathf.Min(frameSize.x / 1920f, frameSize.y / 1080f) * Mathf.Min(scale.x, scale.y);
            for (int i = 0; i < border.Length; i++)
            {
                Vector2 a = maskOrigin + Vector2.Scale(border[i], scale);
                Vector2 b = maskOrigin + Vector2.Scale(border[(i + 1) % border.Length], scale);
                // Custom drawing is limited explicitly to this host, independently of child masking.
                float start = 0, end = 1;
                Vector2 delta = b - a;
                if (!ClipAxis(a.x, delta.x, contentRect.xMin, contentRect.xMax, ref start, ref end)
                    || !ClipAxis(a.y, delta.y, contentRect.yMin, contentRect.yMax, ref start, ref end)) continue;
                HudDrawing.Line(p, a + delta * start, a + delta * end);
            }
        }

        private static bool ClipAxis(float origin, float delta, float min, float max, ref float start, ref float end)
        {
            if (Mathf.Abs(delta) < .00001f) return origin >= min && origin <= max;
            float a = (min - origin) / delta, b = (max - origin) / delta;
            start = Mathf.Max(start, Mathf.Min(a, b));
            end = Mathf.Min(end, Mathf.Max(a, b));
            return start <= end;
        }

        private void ReleaseMask()
        {
            maskRefresh.Pause();
            UnsubscribeFrame();
            frame = null;
            style.backgroundImage = StyleKeyword.Null;
            DestroyMask(mask);
            mask = null;
            border = null;
            shapeDirty = true;
        }

        private static void DestroyMask(VectorImage image)
        {
            if (image == null) return;
            if (Application.isPlaying) Object.Destroy(image);
            else Object.DestroyImmediate(image);
        }
    }
}
