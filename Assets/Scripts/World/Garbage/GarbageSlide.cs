using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEngine;

namespace AnimalGame.Garbage
{
    [DefaultExecutionOrder(20)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WorldInteraction), typeof(GarbageMotion), typeof(GarbageFragmentSpawner))]
    public sealed class GarbageSlide : MonoBehaviour
    {
        [SerializeField, Range(0f, 89f)] private float slideStartAngle = 22f;
        [SerializeField, Range(0f, 89f)] private float slideStopAngle = 15f;
        [SerializeField, Min(0f)] private float slideAcceleration = 3f;
        [SerializeField, Min(0f)] private float slideFriction = .6f;
        [SerializeField, Min(.1f)] private float maximumSlideSpeed = 4f;
        [SerializeField, Min(0f)] private float breakDropMeters = 3f;
        [SerializeField, Min(.1f)] private float sampleRadiusMeters = .5f;
        [SerializeField, Min(0f)] private float stopStableDuration = .2f;
        [SerializeField, Min(0f)] private float stopSpeed = .12f;
        private WorldInteraction item;
        private GarbageMotion motion;
        private GarbageFragmentSpawner fragments;
        private MapTestSceneController map;
        private bool sliding;
        private bool broken;
        private float slideStartHeightMeters;
        private float stableTime;

        private void Awake()
        {
            item = GetComponent<WorldInteraction>();
            motion = GetComponent<GarbageMotion>();
            fragments = GetComponent<GarbageFragmentSpawner>();
            foreach (MapTestSceneController candidate in FindObjectsByType<MapTestSceneController>(FindObjectsSortMode.None))
                if (candidate.gameObject.scene == gameObject.scene) { map = candidate; break; }
            RobotMover player = null;
            foreach (RobotMover candidate in FindObjectsByType<RobotMover>(FindObjectsSortMode.None))
                if (candidate.gameObject.scene == gameObject.scene) { player = candidate; break; }
            motion.Initialize(map, player);
        }

        private void OnDisable() => ResetSlide();

        private void Update()
        {
            if (broken || map == null || !map.HasGeneratedMap) return;
            if (item.MotionOwner != null)
            {
                ResetSlide();
                return;
            }
            if (!SampleSlope(out float height, out float angle, out Vector2 downhill))
            {
                ResetSlide();
                return;
            }
            if (!sliding)
            {
                if (angle < slideStartAngle || downhill.sqrMagnitude < .000001f) return;
                sliding = true;
                slideStartHeightMeters = height;
                stableTime = 0f;
            }
            if (slideStartHeightMeters - height > breakDropMeters)
            {
                broken = fragments.BeginBreak(false);
                motion.Stop();
                return;
            }
            bool steep = angle > slideStopAngle && downhill.sqrMagnitude > .000001f;
            motion.SetSlide(steep ? downhill : Vector2.zero,
                slideAcceleration, slideFriction, maximumSlideSpeed);
            stableTime = !steep && motion.Velocity.magnitude <= stopSpeed
                ? stableTime + Time.deltaTime : 0f;
            if (stableTime >= stopStableDuration) ResetSlide();
        }

        private bool SampleSlope(out float height, out float angle, out Vector2 downhill)
        {
            height = angle = 0f;
            downhill = Vector2.zero;
            Vector2 center = transform.position;
            float xStep = map.MapMetersToWorldDistance(Vector2.right, sampleRadiusMeters);
            float yStep = map.MapMetersToWorldDistance(Vector2.up, sampleRadiusMeters);
            if (xStep <= 0f || yStep <= 0f
                || !map.TrySampleWorldPosition(center, out _, out height)
                || !map.TrySampleWorldPosition(center + Vector2.right * xStep, out _, out float right)
                || !map.TrySampleWorldPosition(center - Vector2.right * xStep, out _, out float left)
                || !map.TrySampleWorldPosition(center + Vector2.up * yStep, out _, out float up)
                || !map.TrySampleWorldPosition(center - Vector2.up * yStep, out _, out float down)) return false;
            Vector2 gradient = new Vector2((right - left) / (2f * sampleRadiusMeters),
                (up - down) / (2f * sampleRadiusMeters));
            angle = Mathf.Atan(gradient.magnitude) * Mathf.Rad2Deg;
            downhill = gradient.sqrMagnitude > .000001f
                ? map.MapDirectionToWorldDirection(-gradient) : Vector2.zero;
            return true;
        }

        private void ResetSlide()
        {
            sliding = false;
            stableTime = 0f;
            if (motion != null) motion.Stop();
        }

        private void OnValidate()
        {
            slideStartAngle = Mathf.Max(.1f, slideStartAngle);
            slideStopAngle = Mathf.Clamp(slideStopAngle, 0f, slideStartAngle - .1f);
        }
    }
}
