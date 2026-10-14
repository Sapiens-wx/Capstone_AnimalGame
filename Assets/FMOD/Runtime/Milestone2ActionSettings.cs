using FMODUnity;
using UnityEngine;

namespace Capstone.Audio
{
    [CreateAssetMenu(menuName = "Audio/Milestone 2 Player Action Settings")]
    public sealed class Milestone2ActionSettings : ScriptableObject
    {
        [SerializeField] private bool enablePlayerActions = true;
        [SerializeField] private AudioCueSettings[] cues = CreateCues();
        [SerializeField, Min(0f)] private float fadeOutSeconds = 0.05f;
        [Tooltip("Audio-only actual-motion dead zone, in world units per second.")]
        [SerializeField, Min(0f)] private float movementThreshold = 0.01f;
        [SerializeField, Min(0f)] private float turnThreshold = 0.5f;
        [SerializeField, Min(0f)] private float movementStopDelay = 0.12f;
        [SerializeField, Min(0f)] private float engineFadeOutSeconds = 0.25f;
        [SerializeField, Min(0f)] private float groundFadeOutSeconds = 0.18f;
        [SerializeField, Min(0.001f)] private float movementFadeInSeconds = 0.04f;
        [SerializeField, Min(0.001f)] private float engineCrossfadeSeconds = 0.08f;
        [SerializeField, Min(0.01f)] private float driveReferenceSpeed = 5f;
        [SerializeField, Min(0.01f)] private float turnReferenceSpeed = 100f;
        [SerializeField, Min(0.01f)] private float brakeReferenceDeceleration = 8f;
        [Tooltip("Audio RPM reaches maximum in 3.5 seconds at full throttle.")]
        [SerializeField, Min(0.01f)] private float engineAccelerationSeconds = 3.5f;
        [SerializeField, Min(0.01f)] private float engineCoastDeceleration = 0.2f;
        [SerializeField, Range(0.01f, 1f)] private float engineMinimumPitch = 0.25f;
        [SerializeField, Min(0f)] private float armMovementThreshold = 0.01f;
        [SerializeField, Min(0.01f)] private float armReferenceSpeed = 1f;
        [SerializeField, Min(0f)] private float blockedCooldown = 0.35f;
        [SerializeField, Min(0.0001f)] private float parameterEpsilon = 0.005f;

        internal bool Enabled => enablePlayerActions;
        internal float FadeOutSeconds => Mathf.Max(0f, fadeOutSeconds);
        internal float MovementThreshold => Mathf.Max(0f, movementThreshold);
        internal float TurnThreshold => Mathf.Max(0f, turnThreshold);
        internal float MovementStopDelay => Mathf.Max(0f, movementStopDelay);
        internal float EngineFadeOutSeconds => Mathf.Max(0f, engineFadeOutSeconds);
        internal float GroundFadeOutSeconds => Mathf.Max(0f, groundFadeOutSeconds);
        internal float MovementFadeInSeconds => Mathf.Max(0.001f, movementFadeInSeconds);
        internal float EngineCrossfadeSeconds => Mathf.Max(0.001f, engineCrossfadeSeconds);
        internal float DriveReferenceSpeed => Mathf.Max(0.01f, driveReferenceSpeed);
        internal float TurnReferenceSpeed => Mathf.Max(0.01f, turnReferenceSpeed);
        internal float BrakeReferenceDeceleration => Mathf.Max(0.01f, brakeReferenceDeceleration);
        internal float EngineAccelerationPerSecond => 1f / Mathf.Max(0.01f, engineAccelerationSeconds);
        internal float EngineCoastDeceleration => Mathf.Max(0.01f, engineCoastDeceleration);
        internal float EngineMinimumPitch => Mathf.Clamp(engineMinimumPitch, 0.01f, 1f);
        internal float ArmMovementThreshold => Mathf.Max(0f, armMovementThreshold);
        internal float ArmReferenceSpeed => Mathf.Max(0.01f, armReferenceSpeed);
        internal float BlockedCooldown => Mathf.Max(0f, blockedCooldown);
        internal float ParameterEpsilon => Mathf.Max(0.0001f, parameterEpsilon);

        internal AudioCueSettings GetCue(Milestone2ActionCue cue)
        {
            int index = (int)cue;
            return cues != null && index >= 0 && index < cues.Length ? cues[index] : null;
        }

        internal bool HasAssignedEvents
        {
            get
            {
                if (cues == null) return false;
                foreach (AudioCueSettings cue in cues)
                    if (cue != null && !cue.EventReference.Guid.IsNull) return true;
                return false;
            }
        }

        private static AudioCueSettings[] CreateCues()
        {
            var result = new AudioCueSettings[Milestone2ActionCatalog.CueCount];
            for (int index = 0; index < result.Length; index++) result[index] = new AudioCueSettings();
            return result;
        }
    }
}
