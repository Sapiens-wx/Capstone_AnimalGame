using AnimalGame.RobotMap;
using UnityEngine;

namespace AnimalGame.World
{
    internal sealed class WeatherTowerCamera
    {
        private RobotCameraShake composer;
        private Object owner;
        private float groundSize;

        public bool Acquire(Object tower, RobotMover player)
        {
            if (composer != null && owner == tower)
                return composer.TryAcquireWeatherTowerZoom(tower);

            Release();
            RobotCameraShake candidate = FindComposer(player);
            if (candidate == null || !candidate.TryAcquireWeatherTowerZoom(tower))
                return false;

            composer = candidate;
            owner = tower;
            groundSize = candidate.ElevationAdjustedOrthographicSize;
            return true;
        }

        public void Apply(float travel01, float topMultiplier)
        {
            if (composer == null)
                return;

            // The composer stays responsible for the live ground, scan and focus sizes.
            composer.SetWeatherTowerZoom(owner, groundSize * topMultiplier, travel01);
        }

        public void ApplyLiftFeedback(float progress, float duration, bool ascending)
        {
            if (composer == null)
                return;

            float elapsed = progress * duration;
            float settle = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(.84f, 1f, progress));
            float impact = Mathf.SmoothStep(1f, 0f, elapsed / .24f) * settle;
            float kick = impact * Mathf.Cos(elapsed * 32f);
            float sway = Mathf.SmoothStep(0f, 1f, elapsed / .16f) * settle;
            float phase = elapsed * (2f * Mathf.PI);

            // The launch kick is brief. The smaller mechanical sway settles as the lift docks.
            var cameraOffset = new Vector3(
                .055f * kick + .042f * Mathf.Sin(phase * 3.4f) * sway,
                (ascending ? -.15f : .15f) * kick + .034f * Mathf.Sin(phase * 5.7f) * sway,
                .78f * kick + .24f * Mathf.Sin(phase * 2.9f) * sway);
            composer.SetWeatherTowerLiftFeedback(owner, cameraOffset, new Vector2(.58f, .34f) * impact);
        }

        public void Release()
        {
            if (composer != null)
                composer.ReleaseWeatherTowerZoom(owner);
            composer = null;
            owner = null;
        }

        private static RobotCameraShake FindComposer(RobotMover player)
        {
            foreach (RobotCameraShake candidate in Object.FindObjectsByType<RobotCameraShake>(FindObjectsSortMode.None))
            {
                if (!candidate.isActiveAndEnabled || candidate.gameObject.scene != player.gameObject.scene)
                    continue;
                Camera camera = candidate.GetComponent<Camera>();
                if (!camera.isActiveAndEnabled || !camera.orthographic)
                    continue;

                // Map cameras can follow a balance target child of the robot body.
                Transform target = candidate.GetComponent<RobotCameraFollow>().Target;
                if (candidate.FollowsRobot(player)
                    || target == player.transform || (target != null && target.IsChildOf(player.transform)))
                    return candidate;
            }
            return null;
        }
    }
}
