using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.MainUI
{
    /// <summary>Projects the shared frame's notch into the compass and pointer coordinates.</summary>
    [UxmlElement]
    public partial class CompassHostElement : VisualElement
    {
        private HudFrameElement frame;
        private readonly IVisualElementScheduledItem notchRefresh;

        public CompassHostElement()
        {
            pickingMode = PickingMode.Ignore;
            style.overflow = Overflow.Hidden;
            RegisterCallback<AttachToPanelEvent>(_ => BindFrame());
            RegisterCallback<DetachFromPanelEvent>(_ => UnsubscribeFrame());
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            notchRefresh = schedule.Execute(RefreshNotch);
            notchRefresh.Pause();
        }

        private void BindFrame()
        {
            VisualElement scope = this;
            while (scope.parent != null && scope.Q<HudFrameElement>("Frame") == null) scope = scope.parent;
            var nextFrame = scope.Q<HudFrameElement>("Frame");
            if (frame != nextFrame)
            {
                UnsubscribeFrame();
                frame = nextFrame;
                if (frame != null)
                {
                    frame.ShapeChanged += RequestNotchRefresh;
                    frame.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
                }
            }
            if (frame != null) RequestNotchRefresh();
        }

        private void OnGeometryChanged(GeometryChangedEvent evt) => RequestNotchRefresh();

        /// <summary>Notify after changing transforms, which do not emit GeometryChangedEvent.</summary>
        internal void RequestNotchRefresh()
        {
            if (panel != null) notchRefresh.ExecuteLater(0);
        }

        private void UnsubscribeFrame()
        {
            if (frame == null) return;
            frame.ShapeChanged -= RequestNotchRefresh;
            frame.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            frame = null;
        }

        private void RefreshNotch()
        {
            if (frame == null || frame.panel != panel) BindFrame();
            if (frame == null || frame.panel != panel) return;
            Vector2 size = frame.contentRect.size;
            if (size.x <= 0 || size.y <= 0 || contentRect.width <= 0 || contentRect.height <= 0) return;
            var notch = frame.GetCompassNotch(size);
            var compass = this.Q<CompassElement>();
            Vector2 min = frame.ChangeCoordinatesTo(compass, notch.bounds.min);
            Vector2 max = frame.ChangeCoordinatesTo(compass, notch.bounds.max);
            float bevel = frame.ChangeCoordinatesTo(compass, notch.bounds.min + Vector2.right * notch.bevel).x - min.x;
            compass.SetNotchClip(Rect.MinMaxRect(min.x, min.y, max.x, max.y), bevel);
            var pointer = this.Q<CompassPointerElement>();
            pointer.SetClipBottom(frame.ChangeCoordinatesTo(pointer, notch.bounds.max).y);
        }
    }
}
