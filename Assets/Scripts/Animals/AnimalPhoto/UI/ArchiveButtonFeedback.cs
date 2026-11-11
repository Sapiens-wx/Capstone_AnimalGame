using System;
using AnimalGame.RobotMap;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.Animals.UI
{
    public sealed class ArchiveButtonFeedback
    {
        private readonly VisualElement root;
        private readonly PhotoUnlockPulse ripple;
        private readonly IVisualElementScheduledItem animation;
        private VisualElement badge;
        private Action pendingAction;
        private float started;
        private bool showRipple, scaleBadge;
        private Vector2 rippleCenter;
        private float minimumRadius;
        public bool IsPlaying => badge != null;
        public bool IsWaitingForAction => pendingAction != null;

        public ArchiveButtonFeedback(VisualElement root)
        {
            this.root = root;
            ripple = new PhotoUnlockPulse { name = "archive-button-ripple" };
            root.Add(ripple);
            animation = root.schedule.Execute(Tick).Every(16);
            animation.Pause();
            root.RegisterCallback<DetachFromPanelEvent>(_ => Stop());
            foreach (string name in new[] { "photo-open", "photo-previous", "photo-next", "photo-catalog", "photo-delete", "back" })
            {
                var button = root.Q<Button>(name);
                var circle = button.Q(className: "photo-action-circle");
                var line = button.Q(className: "photo-action-underline");
                void AlignUnderline()
                {
                    line.style.bottom = StyleKeyword.Auto;
                    line.style.top = circle.layout.yMax - 2f;
                }
                button.RegisterCallback<GeometryChangedEvent>(_ => AlignUnderline());
                circle.RegisterCallback<GeometryChangedEvent>(_ => AlignUnderline());
            }
        }

        private VisualElement FindBadge(Button button) => button.Q(className: "photo-action-circle")
            ?? button.Q(className: "key-badge") ?? button.Q(className: "neighbor-key");

        public void Run(string buttonName, Action action, bool emitRipple = true)
        {
            if (IsWaitingForAction) return;
            var button = root.Q<Button>(buttonName);
            if (!button.enabledInHierarchy) return;
            var target = FindBadge(button);
            if (target == null || target.worldBound.width <= 0) { action?.Invoke(); return; }
            Stop();
            badge = target;
            rippleCenter = badge.ChangeCoordinatesTo(ripple, badge.layout.size * .5f);
            minimumRadius = badge.layout.width * .5f - 2f;
            showRipple = emitRipple; scaleBadge = true;
            pendingAction = action;
            started = Time.unscaledTime;
            Tick();
            animation.Resume();
        }

        public void RunAfterSuccess(string buttonName, Action action)
        {
            var target = FindBadge(root.Q<Button>(buttonName));
            Vector2 center = target.ChangeCoordinatesTo(ripple, target.layout.size * .5f);
            float radius = target.layout.width * .5f - 2f;
            action();
            Stop();
            badge = target; rippleCenter = center; minimumRadius = radius;
            showRipple = true; scaleBadge = false;
            started = Time.unscaledTime;
            Tick(); animation.Resume();
        }

        private void Tick()
        {
            float elapsed = Time.unscaledTime - started;
            if (pendingAction != null && elapsed >= PhotoUnlockPulse.ActionDelaySeconds)
            {
                Action action = pendingAction; pendingAction = null;
                action();
                if (badge == null) return;
            }
            bool active = PhotoUnlockPulse.EvaluatePress(elapsed, out float scale, out float expansion);
            if (scaleBadge) badge.style.scale = new Scale(Vector3.one * scale);
            if (!active)
            {
                Stop();
                return;
            }
            ripple.style.display = showRipple ? DisplayStyle.Flex : DisplayStyle.None;
            if (showRipple) ripple.SetPulse(rippleCenter, minimumRadius, PhotoUnlockPulse.MaximumRadius, expansion);
        }

        public void Stop()
        {
            animation.Pause();
            if (badge != null) badge.style.scale = new Scale(Vector3.one);
            badge = null; pendingAction = null;
            ripple.style.display = DisplayStyle.None;
        }
    }
}
