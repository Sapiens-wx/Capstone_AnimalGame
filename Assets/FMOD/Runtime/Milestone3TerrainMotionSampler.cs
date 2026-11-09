using UnityEngine;

namespace Capstone.Audio
{
    // Arm Update (-50) has settled; RobotMover Update (0) has not yet cleared
    // its velocity on a failed movement transaction.
    [DefaultExecutionOrder(-1)]
    [DisallowMultipleComponent]
    public sealed class Milestone3TerrainMotionSampler : MonoBehaviour
    {
        private Milestone3TerrainCollisionAudio observer;
        internal void Bind(Milestone3TerrainCollisionAudio target)
        {
            observer = target;
            enabled = target != null;
        }
        private void Update() => observer?.CaptureBeforeMotion(Time.deltaTime);
        private void OnDisable() => observer = null;
    }
}
