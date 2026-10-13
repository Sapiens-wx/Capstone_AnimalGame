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
        [SerializeField, Min(0f)] private float movementStopDelay = 0.05f;
        [SerializeField, Min(0.01f)] private float driveReferenceSpeed = 5f;
        [SerializeField, Min(0.01f)] private float turnReferenceSpeed = 100f;
        [SerializeField, Min(0.01f)] private float brakeReferenceDeceleration = 8f;
        [SerializeField, Min(0f)] private float armMovementThreshold = 0.01f;
        [SerializeField, Min(0.01f)] private float armReferenceSpeed = 1f;
        [SerializeField, Min(0f)] private float blockedCooldown = 0.35f;
        [SerializeField, Min(0.0001f)] private float parameterEpsilon = 0.005f;

        internal bool Enabled => enablePlayerActions;
        internal float FadeOutSeconds => Mathf.Max(0f, fadeOutSeconds);
        internal float MovementThreshold => Mathf.Max(0f, movementThreshold);
        internal float TurnThreshold => Mathf.Max(0f, turnThreshold);
        internal float MovementStopDelay => Mathf.Max(0f, movementStopDelay);
        internal float DriveReferenceSpeed => Mathf.Max(0.01f, driveReferenceSpeed);
        internal float TurnReferenceSpeed => Mathf.Max(0.01f, turnReferenceSpeed);
        internal float BrakeReferenceDeceleration => Mathf.Max(0.01f, brakeReferenceDeceleration);
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
