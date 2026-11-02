using UnityEngine;

namespace AnimalGame.Animals.UI
{
    public sealed class PhotoDeleteHold
    {
        public const float Duration = 1f;
        public float Progress => Mathf.Clamp01(elapsed / Duration);
        private float elapsed;
        private string target;
        private bool latched;

        public bool Update(bool held, string photoId, float deltaTime)
        {
            if (!held)
            {
                elapsed = 0; target = null; latched = false;
                return false;
            }
            if (latched) return false;
            if (photoId == null || (target != null && target != photoId))
            {
                Cancel();
                return false;
            }
            target = photoId;
            elapsed += deltaTime;
            if (elapsed < Duration) return false;
            latched = true;
            return true;
        }

        // A new photo or page must never inherit a partially completed hold.
        public void Cancel() { elapsed = 0; target = null; latched = true; }
    }
}
