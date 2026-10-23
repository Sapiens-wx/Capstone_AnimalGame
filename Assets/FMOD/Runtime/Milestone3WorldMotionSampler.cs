using UnityEngine;

namespace Capstone.Audio
{
    // A single audio-owned callback samples the fixed nearby slots before custom
    // garbage motion can clear Velocity. No gameplay component is changed or added.
    [DefaultExecutionOrder(39)]
    [DisallowMultipleComponent]
    public sealed class Milestone3WorldMotionSampler : MonoBehaviour
    {
        private Milestone3WorldAudio listener;

        internal void Bind(Milestone3WorldAudio target)
        {
            listener = target;
            enabled = target != null && target.NeedsMotionSampling;
        }

        private void Update() => listener?.CaptureBeforeMotion();
        private void OnDisable() => listener = null;
    }
}
