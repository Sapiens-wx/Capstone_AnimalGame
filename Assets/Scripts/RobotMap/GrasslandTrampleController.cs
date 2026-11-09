using UnityEngine;

namespace AnimalGame.RobotMap
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RobotMarkerView))]
    public sealed class GrasslandTrampleController : MonoBehaviour
    {
        public const int Capacity = 8;
        [SerializeField, Min(.01f)] private float recoverySeconds = .2f;
        [SerializeField, Range(0f, .2f)] private float windTipOffset = .12f;
        [SerializeField, Range(0f, .5f)] private float pressedTipOffset = .45f;
        [SerializeField, Range(.3f, 1f)] private float pressedLength = .45f;
        [SerializeField, Min(.5f)] private float trampleRadiusOfBodyDiameter = .9f;
        [SerializeField] private Vector2 windPeriodSeconds = new Vector2(1.1f, 2f);
        private readonly Vector4[] footprints = new Vector4[Capacity];
        private RobotMarkerView marker;
        private RobotHeightMotionDetector heightMotion;
        private int next;
        private float nextSample;
        private static readonly int FootprintsId = Shader.PropertyToID("_GrassFootprints");
        private static readonly int ActiveId = Shader.PropertyToID("_GrassActive");
        private static readonly int NowId = Shader.PropertyToID("_GrassNow");
        private static readonly int DynamicsId = Shader.PropertyToID("_GrassDynamics");
        private static readonly int PeriodsId = Shader.PropertyToID("_GrassWindPeriods");

        private void OnEnable()
        {
            marker = GetComponent<RobotMarkerView>();
            heightMotion = GetComponent<RobotHeightMotionDetector>();
            next = 0;
            nextSample = 0f;
            for (int i = 0; i < Capacity; i++) footprints[i] = new Vector4(0, 0, -10000f, 0);
            Publish();
        }

        private void LateUpdate()
        {
            float now = Time.time;
            bool airborne = heightMotion != null && heightMotion.HasData && heightMotion.IsAirborne;
            if (!airborne)
                footprints[Capacity - 1] = new Vector4(transform.position.x, transform.position.y,
                    now, marker.VisualBodyDiameter * trampleRadiusOfBodyDiameter);
            if (!airborne && now >= nextSample)
            {
                footprints[next] = footprints[Capacity - 1];
                next = (next + 1) % (Capacity - 1);
                nextSample = now + 1f / 30f;
            }
            Publish();
        }

        private void Publish()
        {
            Shader.SetGlobalFloat(ActiveId, 1f);
            Shader.SetGlobalFloat(NowId, Time.time);
            Shader.SetGlobalVectorArray(FootprintsId, footprints);
            Shader.SetGlobalVector(DynamicsId,
                new Vector4(windTipOffset, pressedTipOffset, pressedLength, recoverySeconds));
            Shader.SetGlobalVector(PeriodsId, new Vector4(windPeriodSeconds.x, windPeriodSeconds.y, 0, 0));
        }

        private void OnDisable()
        {
            Shader.SetGlobalFloat(ActiveId, 0f);
        }
    }
}
