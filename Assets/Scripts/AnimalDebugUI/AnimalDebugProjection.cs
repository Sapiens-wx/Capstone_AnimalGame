using UnityEngine;

namespace AnimalGame.AnimalDebug
{
    // Camera follow and MainUIProjection finish before screen-space arrow placement.
    [DefaultExecutionOrder(400)]
    public sealed class AnimalDebugProjection : MonoBehaviour
    {
        public AnimalDebugUI Owner { get; set; }
        private void LateUpdate() { if (Owner != null) Owner.RefreshArrow(); }
    }
}
