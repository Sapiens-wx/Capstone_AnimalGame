using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.RobotMap
{
    public sealed class PhotoUnlockPulse : VisualElement
    {
        public const float PressSeconds = .04f;
        public const float ReboundSeconds = .1f;
        public const float SettleSeconds = .06f;
        public const float RippleSeconds = .2f;
        private Vector2 center;
        private float radius;

        public PhotoUnlockPulse()
        {
            name = "catalog-unlock-pulse";
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = style.top = style.right = style.bottom = 0;
            style.display = DisplayStyle.None;
            generateVisualContent += Draw;
        }

        public void SetPulse(Vector2 center, float minimumRadius, float maximumRadius, float expansion)
        {
            this.center = center;
            radius = Mathf.Lerp(minimumRadius, maximumRadius, expansion);
            style.opacity = Mathf.Pow(1f - expansion, 3f);
            MarkDirtyRepaint();
        }

        public static bool EvaluatePress(float elapsed, out float badgeScale, out float expansion)
        {
            badgeScale = 1f;
            expansion = Mathf.Clamp01(elapsed / RippleSeconds);
            if (elapsed < PressSeconds)
                badgeScale = Mathf.Lerp(1f, .78f, Mathf.SmoothStep(0, 1, elapsed / PressSeconds));
            else if (elapsed < PressSeconds + ReboundSeconds)
                badgeScale = Mathf.Lerp(.78f, 1.1f, Mathf.SmoothStep(0, 1, (elapsed - PressSeconds) / ReboundSeconds));
            else if (elapsed < PressSeconds + ReboundSeconds + SettleSeconds)
                badgeScale = Mathf.Lerp(1.1f, 1f, Mathf.SmoothStep(0, 1, (elapsed - PressSeconds - ReboundSeconds) / SettleSeconds));
            return elapsed < RippleSeconds;
        }

        private void Draw(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            painter.strokeColor = new Color32(212, 184, 84, 255);
            painter.lineWidth = 4f;
            painter.BeginPath();
            for (int i = 0; i <= 128; i++)
            {
                float angle = i * Mathf.PI * 2f / 128;
                Vector2 point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                if (i == 0) painter.MoveTo(point); else painter.LineTo(point);
            }
            painter.Stroke();
        }
    }
}
