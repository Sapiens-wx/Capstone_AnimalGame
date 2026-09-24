using System;
using UnityEngine;

namespace AnimalGame.RobotMap
{
    /// <summary>One reversible, unscaled timeline. Tracks use normalized windows.</summary>
    [Serializable]
    public sealed class PhotoResultAnimation
    {
        [Min(0.05f)] public float openDuration = 1.1f;
        [Min(0.05f)] public float closeDuration = 0.55f;
        [Min(0.01f)] public float zoomedScale = 6f;
        [Min(0.01f)] public float restingScale = 1f;
        public Vector2 zoomWindow = new Vector2(0f, 1f);
        public Vector2 contentPositionWindow = new Vector2(0f, 1f);
        public Vector2 circleWindow = new Vector2(0f, 0.7f);
        public Vector2 arcWindow = new Vector2(0.1f, 0.85f);
        public Vector2 lineWindow = new Vector2(0.25f, 0.9f);
        public Vector2 photoWindow = new Vector2(0.18f, 0.8f);
        public Vector2 textWindow = new Vector2(0.45f, 1f);
        public AnimationCurve easing = AnimationCurve.EaseInOut(0, 0, 1, 1);

        public float Progress { get; private set; }
        public bool IsClosing { get; private set; }
        public bool IsClosed => IsClosing && Progress <= 0f;
        public void Open() { Progress = 0f; IsClosing = false; }
        public void Close() { IsClosing = true; }
        public void Tick(float deltaTime)
        {
            float duration = IsClosing ? closeDuration : openDuration;
            Progress = Mathf.Clamp01(Progress + (IsClosing ? -1f : 1f)
                * Mathf.Max(0, deltaTime) / Mathf.Max(0.05f, duration));
        }
        public float Evaluate(Vector2 window)
        {
            float t = Mathf.Clamp01((Progress - window.x) / Mathf.Max(0.001f, window.y - window.x));
            return Mathf.Clamp01(easing.Evaluate(t));
        }
    }
}
