using System;
using AnimalGame.MapTest;
using UnityEngine;

namespace AnimalGame.RobotMap
{
    public readonly struct RobotLandingImpact
    {
        public float RelativeImpactSpeedMetersPerSecond { get; }
        public float Strength01 { get; }
        public float AirborneDurationSeconds { get; }
        public float GroundDropMeters { get; }
        public Vector2 TravelWorldDirection { get; }

        public RobotLandingImpact(
            float relativeImpactSpeedMetersPerSecond,
            float strength01,
            float airborneDurationSeconds,
            float groundDropMeters,
            Vector2 travelWorldDirection)
        {
            RelativeImpactSpeedMetersPerSecond =
                relativeImpactSpeedMetersPerSecond;
            Strength01 = strength01;
            AirborneDurationSeconds = airborneDurationSeconds;
            GroundDropMeters = groundDropMeters;
            TravelWorldDirection = travelWorldDirection;
        }
    }

    [DefaultExecutionOrder(150)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RobotMover))]
    public sealed class RobotHeightMotionDetector : MonoBehaviour
    {
        private const float MotionStepSeconds = 1f / 60f;

        [Header("Height Motion Detection")]
        [SerializeField] private bool enableHeightMotionDetection = true;

        [Tooltip("Gravity used by the virtual vertical body while the sampled ground falls away, in logical map metres per second squared.")]
        [SerializeField, Min(0.1f)] private float virtualGravityMetersPerSecondSquared = 9.81f;

        [Tooltip("Minimum planar robot speed required before a sudden loss of ground support can count as takeoff.")]
        [SerializeField, Min(0f)] private float minimumTakeoffPlanarSpeed = 0.65f;

        [Tooltip("Minimum speed at which the ground must fall away relative to the virtual body before takeoff is possible.")]
        [SerializeField, Min(0f)] private float minimumGroundFallAwaySpeed = 1.25f;

        [Tooltip("Minimum vertical gap between the virtual body and sampled ground required to enter the airborne state.")]
        [SerializeField, Min(0f)] private float takeoffClearanceMeters = 0.025f;

        [Tooltip("Vertical tolerance used when the falling virtual body reconnects with the sampled ground.")]
        [SerializeField, Min(0f)] private float landingClearanceMeters = 0.02f;

        [Tooltip("Airborne periods shorter than this are treated as ordinary suspension travel and do not create landing impacts.")]
        [SerializeField, Min(0f)] private float minimumAirborneDurationSeconds = 0.055f;

        [Tooltip("How long Was Airborne Recently remains true after ground contact is restored.")]
        [SerializeField, Min(0f)] private float recentAirborneMemorySeconds = 0.6f;

        [Header("Landing Impact Scale")]
        [Tooltip("Relative landing speed that begins producing a camera landing impact.")]
        [SerializeField, Min(0f)] private float minimumLandingImpactSpeed = 0.45f;

        [Tooltip("Relative landing speed mapped to a full-strength camera landing impact.")]
        [SerializeField, Min(0.01f)] private float fullLandingImpactSpeed = 3.6f;

        [Tooltip("Shapes landing strength after speed normalization. Values below one make medium landings stronger while preserving zero and full-strength endpoints.")]
        [SerializeField, Range(0.1f, 3f)] private float landingImpactStrengthExponent = 0.7f;

        [Header("Signal Filtering")]
        [Tooltip("Smoothing time applied to sampled ground vertical speed. This affects telemetry while takeoff still reacts to the unfiltered ground change.")]
        [SerializeField, Min(0.001f)] private float groundVerticalSpeedSmoothingTime = 0.06f;

        [Tooltip("Maximum absolute sampled ground vertical speed accepted from one motion sample.")]
        [SerializeField, Min(0.1f)] private float maximumGroundVerticalSpeed = 20f;

        [Tooltip("Maximum absolute vertical acceleration exposed by the detector.")]
        [SerializeField, Min(0.1f)] private float maximumVerticalAcceleration = 60f;

        public bool HasData { get; private set; }
        public bool IsAirborne { get; private set; }
        public bool LandedThisFrame { get; private set; }
        public bool WasAirborneRecently => HasData
            && lastAirborneTime > float.NegativeInfinity
            && Time.time - lastAirborneTime <= recentAirborneMemorySeconds;
        public float CurrentGroundHeightMeters { get; private set; }
        public float VirtualBodyHeightMeters => IsAirborne
            ? airborneBodyHeight
            : CurrentGroundHeightMeters;
        public float HeightChangeThisFrameMeters { get; private set; }
        public float GroundVerticalSpeedMetersPerSecond { get; private set; }
        public float VerticalSpeedMetersPerSecond { get; private set; }
        public float VerticalAccelerationMetersPerSecondSquared { get; private set; }
        public float AirborneDurationSeconds { get; private set; }
        public float CurrentAirGapMeters => HasData
            ? Mathf.Max(0f, VirtualBodyHeightMeters - CurrentGroundHeightMeters)
            : 0f;
        public float TimeSinceLastAirborne => lastAirborneTime > float.NegativeInfinity
            ? Mathf.Max(0f, Time.time - lastAirborneTime)
            : float.PositiveInfinity;
        public RobotLandingImpact LastLandingImpact { get; private set; }

        public event Action<RobotLandingImpact> Landed;

        private RobotMover mover;
        private MapTestSceneController map;
        private bool initialized;
        private bool wasOnClimbable;
        private Vector2 previousFramePosition;
        private float motionTimeRemainder;
        private float previousGroundHeight;
        private float filteredGroundVerticalSpeed;
        private float airborneBodyHeight;
        private float airborneStartGroundHeight;
        private float lastAirborneTime = float.NegativeInfinity;
        private Vector2 lastTravelWorldDirection;

        private void Awake()
        {
            mover = GetComponent<RobotMover>();
        }

        public void Initialize(MapTestSceneController mapController)
        {
            map = mapController;
            ResetDetectorState();
        }

        private void LateUpdate()
        {
            LandedThisFrame = false;
            if (!enableHeightMotionDetection || map == null || mover == null)
                return;

            if (mover.IgnoreCollisionAndTerrain) { ResetDetectorState(); return; }
            bool onClimbable = mover.CurrentClimbableSurface.IsActive;
            if (onClimbable || wasOnClimbable)
            {
                // The circular surface replaces terrain slope without adding height.
                // Underlying terrain must not fabricate takeoff/landing during contact.
                wasOnClimbable = onClimbable;
                SynchronizeToCurrentGround();
                return;
            }

            if (mover.IsMovementLocked)
            {
                SynchronizeToCurrentGround();
                return;
            }

            Vector2 position = transform.position;
            if (!map.TrySampleWorldPosition(position, out _, out float sampledGroundHeight))
            {
                ResetDetectorState();
                return;
            }

            float deltaTime = Mathf.Min(Time.deltaTime, 0.05f);
            if (deltaTime <= 0.000001f)
                return;

            Vector2 planarVelocity = (Vector2)transform.up * mover.CurrentSpeed
                                     + mover.CurrentTerrainVelocity;
            if (planarVelocity.sqrMagnitude > 0.000001f)
                lastTravelWorldDirection = planarVelocity.normalized;

            if (!initialized)
            {
                InitializeAtHeight(position, sampledGroundHeight);
                return;
            }

            HeightChangeThisFrameMeters = sampledGroundHeight - CurrentGroundHeightMeters;
            CurrentGroundHeightMeters = sampledGroundHeight;
            float planarSpeed = planarVelocity.magnitude;

            // The takeoff clearance applies to a fixed interval. Sample the path
            // between render frames so the interval also works below 60 FPS.
            motionTimeRemainder += deltaTime;
            while (motionTimeRemainder >= MotionStepSeconds)
            {
                motionTimeRemainder -= MotionStepSeconds;
                float stepHeight = sampledGroundHeight;
                if (motionTimeRemainder > 0f)
                {
                    float progress = 1f - motionTimeRemainder / deltaTime;
                    Vector2 stepPosition = Vector2.Lerp(previousFramePosition, position, progress);
                    if (!map.TrySampleWorldPosition(stepPosition, out _, out stepHeight))
                    {
                        ResetDetectorState();
                        InitializeAtHeight(position, sampledGroundHeight);
                        return;
                    }
                }

                AdvanceMotion(stepHeight, planarSpeed);
            }

            previousFramePosition = position;
        }

        private void AdvanceMotion(float groundHeight, float planarSpeed)
        {
            float previousVerticalSpeed = VerticalSpeedMetersPerSecond;
            float groundChange = groundHeight - previousGroundHeight;
            float rawGroundVerticalSpeed = Mathf.Clamp(
                groundChange / MotionStepSeconds,
                -maximumGroundVerticalSpeed,
                maximumGroundVerticalSpeed);
            float filterResponse = 1f - Mathf.Exp(
                -MotionStepSeconds / Mathf.Max(0.001f, groundVerticalSpeedSmoothingTime));
            filteredGroundVerticalSpeed = Mathf.Lerp(
                filteredGroundVerticalSpeed,
                rawGroundVerticalSpeed,
                filterResponse);
            GroundVerticalSpeedMetersPerSecond = filteredGroundVerticalSpeed;
            if (IsAirborne)
            {
                UpdateAirborneMotion(groundHeight, rawGroundVerticalSpeed);
            }
            else
            {
                UpdateSupportedMotion(groundHeight, rawGroundVerticalSpeed, planarSpeed);
            }

            VerticalAccelerationMetersPerSecondSquared = Mathf.Clamp(
                (VerticalSpeedMetersPerSecond - previousVerticalSpeed)
                / MotionStepSeconds,
                -maximumVerticalAcceleration,
                maximumVerticalAcceleration);
            previousGroundHeight = groundHeight;
        }

        private void SynchronizeToCurrentGround()
        {
            ResetDetectorState();
            Vector2 position = transform.position;
            if (map.TrySampleWorldPosition(position, out _, out float sampledGroundHeight))
                InitializeAtHeight(position, sampledGroundHeight);
        }

        private void UpdateSupportedMotion(
            float groundHeight,
            float rawGroundVerticalSpeed,
            float planarSpeed)
        {
            float predictedVerticalSpeed = VerticalSpeedMetersPerSecond
                                           - virtualGravityMetersPerSecondSquared
                                           * MotionStepSeconds;
            float predictedHeight = previousGroundHeight
                                    + VerticalSpeedMetersPerSecond * MotionStepSeconds
                                    - 0.5f
                                    * virtualGravityMetersPerSecondSquared
                                    * MotionStepSeconds
                                    * MotionStepSeconds;
            float separation = predictedHeight - groundHeight;
            float relativeFallAwaySpeed = VerticalSpeedMetersPerSecond
                                          - rawGroundVerticalSpeed;
            bool groundActuallyFell = groundHeight < previousGroundHeight;
            bool shouldTakeOff = groundActuallyFell
                                 && planarSpeed >= minimumTakeoffPlanarSpeed
                                 && relativeFallAwaySpeed
                                 >= minimumGroundFallAwaySpeed
                                 && separation >= takeoffClearanceMeters;

            if (shouldTakeOff)
            {
                IsAirborne = true;
                AirborneDurationSeconds = MotionStepSeconds;
                airborneStartGroundHeight = previousGroundHeight;
                airborneBodyHeight = predictedHeight;
                VerticalSpeedMetersPerSecond = predictedVerticalSpeed;
                lastAirborneTime = Time.time;
                return;
            }

            VerticalSpeedMetersPerSecond = filteredGroundVerticalSpeed;
            AirborneDurationSeconds = 0f;
        }

        private void UpdateAirborneMotion(
            float groundHeight,
            float rawGroundVerticalSpeed)
        {
            VerticalSpeedMetersPerSecond -=
                virtualGravityMetersPerSecondSquared * MotionStepSeconds;
            airborneBodyHeight +=
                VerticalSpeedMetersPerSecond * MotionStepSeconds;
            AirborneDurationSeconds += MotionStepSeconds;
            lastAirborneTime = Time.time;

            float relativeApproachSpeed = rawGroundVerticalSpeed
                                          - VerticalSpeedMetersPerSecond;
            bool reachedGround = airborneBodyHeight
                                 <= groundHeight
                                 + landingClearanceMeters;
            if (!reachedGround || relativeApproachSpeed <= 0f)
                return;

            float completedAirborneDuration = AirborneDurationSeconds;
            float groundDrop = Mathf.Max(
                0f,
                airborneStartGroundHeight - groundHeight);
            IsAirborne = false;
            VerticalSpeedMetersPerSecond = filteredGroundVerticalSpeed;
            AirborneDurationSeconds = 0f;

            if (completedAirborneDuration < minimumAirborneDurationSeconds)
                return;

            float impactSpeed = Mathf.Max(0f, relativeApproachSpeed);
            float linearStrength = Mathf.InverseLerp(
                minimumLandingImpactSpeed,
                Mathf.Max(minimumLandingImpactSpeed + 0.01f,
                    fullLandingImpactSpeed),
                impactSpeed);
            float strength = Mathf.Pow(
                linearStrength,
                landingImpactStrengthExponent);
            LastLandingImpact = new RobotLandingImpact(
                impactSpeed,
                strength,
                completedAirborneDuration,
                groundDrop,
                lastTravelWorldDirection);
            LandedThisFrame = true;
            Landed?.Invoke(LastLandingImpact);
        }

        private void ResetDetectorState()
        {
            HasData = false;
            initialized = false;
            motionTimeRemainder = 0f;
            IsAirborne = false;
            LandedThisFrame = false;
            AirborneDurationSeconds = 0f;
            HeightChangeThisFrameMeters = 0f;
            GroundVerticalSpeedMetersPerSecond = 0f;
            VerticalSpeedMetersPerSecond = 0f;
            VerticalAccelerationMetersPerSecondSquared = 0f;
            filteredGroundVerticalSpeed = 0f;
            lastAirborneTime = float.NegativeInfinity;
            lastTravelWorldDirection = Vector2.zero;
            LastLandingImpact = default;
        }

        private void InitializeAtHeight(Vector2 position, float heightMeters)
        {
            initialized = true;
            HasData = true;
            previousFramePosition = position;
            previousGroundHeight = heightMeters;
            CurrentGroundHeightMeters = heightMeters;
            HeightChangeThisFrameMeters = 0f;
            GroundVerticalSpeedMetersPerSecond = 0f;
            VerticalSpeedMetersPerSecond = 0f;
            VerticalAccelerationMetersPerSecondSquared = 0f;
        }

        private void OnValidate()
        {
            virtualGravityMetersPerSecondSquared = Mathf.Max(
                0.1f,
                virtualGravityMetersPerSecondSquared);
            minimumTakeoffPlanarSpeed = Mathf.Max(0f, minimumTakeoffPlanarSpeed);
            minimumGroundFallAwaySpeed = Mathf.Max(0f, minimumGroundFallAwaySpeed);
            takeoffClearanceMeters = Mathf.Max(0f, takeoffClearanceMeters);
            landingClearanceMeters = Mathf.Max(0f, landingClearanceMeters);
            minimumAirborneDurationSeconds = Mathf.Max(
                0f,
                minimumAirborneDurationSeconds);
            recentAirborneMemorySeconds = Mathf.Max(0f, recentAirborneMemorySeconds);
            minimumLandingImpactSpeed = Mathf.Max(0f, minimumLandingImpactSpeed);
            fullLandingImpactSpeed = Mathf.Max(
                minimumLandingImpactSpeed + 0.01f,
                fullLandingImpactSpeed);
            landingImpactStrengthExponent = Mathf.Clamp(
                landingImpactStrengthExponent,
                0.1f,
                3f);
            groundVerticalSpeedSmoothingTime = Mathf.Max(
                0.001f,
                groundVerticalSpeedSmoothingTime);
            maximumGroundVerticalSpeed = Mathf.Max(0.1f, maximumGroundVerticalSpeed);
            maximumVerticalAcceleration = Mathf.Max(0.1f, maximumVerticalAcceleration);
        }
    }
}
