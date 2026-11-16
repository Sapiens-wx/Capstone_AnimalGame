using AnimalGame.RobotMap;
using UnityEngine;

namespace AnimalGame.World
{
    internal sealed class WeatherTowerCamera
    {
        private RobotCameraShake composer;
        private Object owner;
        private float groundSize;
        private float indoorBlend;

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

        public void Apply(float travel01, bool inside, float topMultiplier, float indoorSize,
            float interiorBlendSeconds, float deltaTime)
        {
            if (composer == null)
                return;

            float targetBlend = inside ? 1f : 0f;
            indoorBlend = interiorBlendSeconds > 0f
                ? Mathf.MoveTowards(indoorBlend, targetBlend, deltaTime / interiorBlendSeconds)
                : targetBlend;
            float outdoorSize = groundSize * topMultiplier;
            // The composer stays responsible for the live ground, scan and focus sizes.
            float towerSize = Mathf.SmoothStep(outdoorSize, Mathf.Min(indoorSize, outdoorSize), indoorBlend);
            composer.SetWeatherTowerZoom(owner, towerSize, travel01);
        }

        public void Release()
        {
            if (composer != null)
                composer.ReleaseWeatherTowerZoom(owner);
            composer = null;
            owner = null;
            indoorBlend = 0f;
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
