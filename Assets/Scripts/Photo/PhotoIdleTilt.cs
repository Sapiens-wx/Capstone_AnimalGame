using UnityEngine;

namespace AnimalGame.RobotMap
{
    /// <summary>Gentle photo motion that resumes whenever manual input stops.</summary>
    public sealed class PhotoIdleTilt
    {
        private float elapsed;

        public void Reset() { elapsed = 0; }

        public bool TryGetTarget(bool takeControl, float deltaTime, Vector2 restingTilt, out Vector2 target)
        {
            target = restingTilt;
            if (takeControl) return false;
            elapsed += deltaTime;
            float phase = elapsed * (Mathf.PI * 2f / 7f);
            target += new Vector2(Mathf.Sin(phase) * 4.5f, Mathf.Sin(phase * 2f) * 3f);
            return true;
        }
    }
}
