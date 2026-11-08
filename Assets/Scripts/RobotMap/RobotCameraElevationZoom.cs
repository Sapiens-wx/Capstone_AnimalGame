using System;
using AnimalGame.MapTest;
using UnityEngine;

namespace AnimalGame.RobotMap
{
    /// <summary>
    /// Measures low ground across the actual, fully composed camera viewport.
    /// The resulting multiplier enters the camera compositor on the following frame.
    /// </summary>
    [DefaultExecutionOrder(270)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RobotCameraShake))]
    public sealed class RobotCameraElevationZoom : MonoBehaviour
    {
        [Header("Relative Ground Height (Map Meters)")]
        [SerializeField, Min(0f)] private float minimumHeightDifference = 3f;
        [SerializeField, Min(0.01f)] private float fullHeightDifference = 30f;

        [Tooltip("Distance at which a visible terrain sample contributes half as much. This is not a search radius; the entire viewport is sampled.")]
        [SerializeField, Min(1f)] private float distanceFalloffMeters = 60f;

        [Tooltip("Fraction of the viewport required to support the strongest response. Isolated pits contribute only a small part of this area.")]
        [SerializeField, Range(0.05f, 1f)] private float lowlandAreaFraction = 0.25f;
        [SerializeField, Range(1f, 3f)] private float maximumZoomMultiplier = 1.5f;

        [Header("Viewport Sampling")]
        [Tooltip("Uniform sample rows across the whole visible rectangle. Columns follow the camera aspect ratio.")]
        [SerializeField, Range(8, 64)] private int viewportSampleRows = 24;
        [SerializeField, Min(0.02f)] private float sampleInterval = 0.1f;

        [Header("Zoom Smoothing")]
        [SerializeField, Min(0.01f)] private float zoomOutSmoothTime = 0.5f;
        [SerializeField, Min(0.01f)] private float zoomInSmoothTime = 0.8f;

        public float CurrentMultiplier { get; private set; } = 1f;
        public float TargetMultiplier { get; private set; } = 1f;
        public float LowlandScore { get; private set; }
        public int ValidSampleCount { get; private set; }

        private Camera ownedCamera;
        private RobotCameraShake cameraShake;
        private MapTestSceneController map;
        private Transform player;
        private float[] contributions = Array.Empty<float>();
        private float sampleTimer;
        private float zoomVelocity;

        private void Awake()
        {
            ownedCamera = GetComponent<Camera>();
            cameraShake = GetComponent<RobotCameraShake>();
        }

        public void Initialize(MapTestSceneController mapController, Transform playerTarget)
        {
            map = mapController;
            player = playerTarget;
            ResetZoom();
        }

        private void LateUpdate() => Tick(Time.deltaTime);

        private void Tick(float deltaTime)
        {
            if (deltaTime <= 0f || map == null || player == null || !cameraShake.isActiveAndEnabled)
                return;

            sampleTimer -= deltaTime;
            if (sampleTimer <= 0f)
            {
                TargetMultiplier = SampleVisibleTerrain();
                sampleTimer = sampleInterval;
            }

            float smoothTime = TargetMultiplier > CurrentMultiplier
                ? zoomOutSmoothTime : zoomInSmoothTime;
            CurrentMultiplier = Mathf.Clamp(
                Mathf.SmoothDamp(CurrentMultiplier, TargetMultiplier, ref zoomVelocity,
                    smoothTime, Mathf.Infinity, deltaTime),
                1f, maximumZoomMultiplier);
            cameraShake.SetElevationZoomMultiplier(CurrentMultiplier);
        }

        private float SampleVisibleTerrain()
        {
            LowlandScore = 0f;
            ValidSampleCount = 0;
            if (!map.TrySampleWorldPosition(player.position, out Vector2 playerPosition, out float playerHeight))
                return 1f;

            Bounds bounds = map.WorldBounds;
            Vector2 mapPerWorld = new Vector2(map.MapSizeMeters.x / bounds.size.x,
                map.MapSizeMeters.y / bounds.size.y);
            float depth = Vector3.Dot(player.position - ownedCamera.transform.position,
                ownedCamera.transform.forward);
            Vector2 bottomLeft = ToMap(0f, 0f);
            Vector2 bottomRight = ToMap(1f, 0f);
            Vector2 topLeft = ToMap(0f, 1f);
            int columns = Mathf.Max(2, Mathf.CeilToInt(viewportSampleRows * ownedCamera.aspect));
            return EvaluateViewport(map.RuntimeMap, playerPosition, playerHeight,
                bottomLeft, bottomRight, topLeft, columns, viewportSampleRows);

            Vector2 ToMap(float x, float y) => Vector2.Scale(
                (Vector2)ownedCamera.ViewportToWorldPoint(new Vector3(x, y, depth))
                - (Vector2)bounds.min, mapPerWorld);
        }

        private float EvaluateViewport(IMapHeightSource source, Vector2 playerMapPosition,
            float playerHeight, Vector2 bottomLeft, Vector2 bottomRight, Vector2 topLeft,
            int columns, int rows)
        {
            int count = columns * rows;
            if (contributions.Length != count)
                contributions = new float[count];
            ValidSampleCount = 0;
            Vector2 horizontal = bottomRight - bottomLeft;
            Vector2 vertical = topLeft - bottomLeft;
            float falloffSquared = distanceFalloffMeters * distanceFalloffMeters;
            int index = 0;
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < columns; x++)
                {
                    // Cell centers give every sample equal visible area, including
                    // the corners of a rotated camera. There is no circular cutoff.
                    Vector2 point = bottomLeft + horizontal * ((x + 0.5f) / columns)
                        + vertical * ((y + 0.5f) / rows);
                    float contribution = 0f;
                    if (source.TrySample(point, out float height))
                    {
                        ValidSampleCount++;
                        float drop = Mathf.InverseLerp(minimumHeightDifference,
                            fullHeightDifference, playerHeight - height);
                        float distanceWeight = 1f / (1f
                            + (point - playerMapPosition).sqrMagnitude / falloffSquared);
                        contribution = drop * distanceWeight;
                    }
                    contributions[index++] = contribution;
                }

            Array.Sort(contributions);
            int strongestCount = Mathf.Max(1, Mathf.CeilToInt(count * lowlandAreaFraction));
            float total = 0f;
            for (int i = count - strongestCount; i < count; i++)
                total += contributions[i];
            // Keep zero contributions in the area budget. Missing map tiles and
            // small pits must not turn into a whole viewport of low ground.
            LowlandScore = total / strongestCount;
            return Mathf.Lerp(1f, maximumZoomMultiplier,
                Mathf.SmoothStep(0f, 1f, LowlandScore));
        }

        private void ResetZoom()
        {
            CurrentMultiplier = TargetMultiplier = 1f;
            LowlandScore = 0f;
            ValidSampleCount = 0;
            sampleTimer = zoomVelocity = 0f;
            if (cameraShake != null)
                cameraShake.SetElevationZoomMultiplier(1f);
        }

        private void OnDisable() => ResetZoom();

        private void OnValidate()
        {
            minimumHeightDifference = Mathf.Max(0f, minimumHeightDifference);
            fullHeightDifference = Mathf.Max(minimumHeightDifference + 0.01f, fullHeightDifference);
            distanceFalloffMeters = Mathf.Max(1f, distanceFalloffMeters);
            lowlandAreaFraction = Mathf.Clamp(lowlandAreaFraction, 0.05f, 1f);
            maximumZoomMultiplier = Mathf.Clamp(maximumZoomMultiplier, 1f, 3f);
            viewportSampleRows = Mathf.Clamp(viewportSampleRows, 8, 64);
            sampleInterval = Mathf.Max(0.02f, sampleInterval);
            zoomOutSmoothTime = Mathf.Max(0.01f, zoomOutSmoothTime);
            zoomInSmoothTime = Mathf.Max(0.01f, zoomInSmoothTime);
        }
    }
}
