using UnityEngine;

namespace Capstone.Audio
{
    [CreateAssetMenu(menuName = "Audio/Milestone 3 Foley Settings")]
    public sealed class Milestone3AudioSettings : ScriptableObject
    {
        [SerializeField] private bool enableAudio = true;
        [SerializeField] private AudioCueSettings[] cues = CreateCues();
        [SerializeField, Min(0f)] private float fadeOutSeconds = 0.05f;
        [SerializeField, Min(0.0001f)] private float parameterEpsilon = 0.005f;
        [SerializeField, Min(0f)] private float tumbleMotionThreshold = 0.01f;
        [SerializeField, Min(0.01f)] private float tumbleReferenceSpeed = 4f;
        [Tooltip("Audio normalization only; does not change gameplay energy.")]
        [SerializeField, Min(0.01f)] private float tumbleImpactEnergyReference = 12f;
        [SerializeField, Min(0.01f)] private float effortReferenceFrequency = 3f;
        [SerializeField, Min(0.000001f)] private float progressEpsilon = 0.00001f;
        [SerializeField, Min(0f)] private float worldMotionThreshold = 0.01f;
        [SerializeField, Min(0.01f)] private float worldReferenceSpeed = 3f;
        [SerializeField, Min(1f)] private float worldObservationRadius = 12f;
        [SerializeField, Min(0.1f)] private float worldDiscoveryInterval = 0.25f;
        [Tooltip("Audio-only threshold along the garbage's actual movement direction.")]
        [SerializeField, Range(0f, 89f)] private float worldSlideSteepAngle = 35f;
        [SerializeField, Min(0.05f)] private float worldSlideProbeMeters = 0.5f;
        [SerializeField, Min(0f)] private float worldSlideMinimumDropMeters = 0.35f;
        [SerializeField, Min(0.01f)] private float garbageImpactReferenceSpeed = 3f;
        [SerializeField, Range(1f, 2f)] private float garbageImpactMaximumGain = 1.5f;
        [SerializeField, Range(0f, 0.12f)] private float garbageImpactPitchVariation = 0.06f;

        internal bool Enabled => enableAudio;
        internal float FadeOutSeconds => Mathf.Max(0f, fadeOutSeconds);
        internal float ParameterEpsilon => Mathf.Max(0.0001f, parameterEpsilon);
        internal float TumbleMotionThreshold => Mathf.Max(0f, tumbleMotionThreshold);
        internal float TumbleReferenceSpeed => Mathf.Max(0.01f, tumbleReferenceSpeed);
        internal float TumbleImpactEnergyReference => Mathf.Max(0.01f, tumbleImpactEnergyReference);
        internal float EffortReferenceFrequency => Mathf.Max(0.01f, effortReferenceFrequency);
        internal float ProgressEpsilon => Mathf.Max(0.000001f, progressEpsilon);
        internal float WorldMotionThreshold => Mathf.Max(0f, worldMotionThreshold);
        internal float WorldReferenceSpeed => Mathf.Max(0.01f, worldReferenceSpeed);
        internal float WorldObservationRadius => Mathf.Max(1f, worldObservationRadius);
        internal float WorldDiscoveryInterval => Mathf.Max(0.1f, worldDiscoveryInterval);
        internal float WorldSlideSteepAngle => Mathf.Clamp(worldSlideSteepAngle, 0f, 89f);
        internal float WorldSlideProbeMeters => Mathf.Max(0.05f, worldSlideProbeMeters);
        internal float WorldSlideMinimumDropMeters => Mathf.Max(0f, worldSlideMinimumDropMeters);
        internal float GarbageImpactReferenceSpeed => Mathf.Max(0.01f, garbageImpactReferenceSpeed);
        internal float GarbageImpactMaximumGain => Mathf.Clamp(garbageImpactMaximumGain, 1f, 2f);
        internal float GarbageImpactPitchVariation => Mathf.Clamp(garbageImpactPitchVariation, 0f, .12f);
        internal bool HasWorldEvents => HasEvents(Milestone3Cue.HeavyPullStart, Milestone3Cue.ClimbableLand)
            || HasCue(Milestone3Cue.HeavyTearMiddle) || HasCue(Milestone3Cue.HeavyTearLate);
        internal bool HasScanEvents => HasEvents(Milestone3Cue.ScanDeploy, Milestone3Cue.ScanHit);
        private bool HasEvents(Milestone3Cue first, Milestone3Cue last)
        {
            for (int i = (int)first; i <= (int)last; i++)
                if (Milestone3AudioCatalog.HasObservableTrigger(i) && HasDirectCue((Milestone3Cue)i)) return true;
            return false;
        }
        internal AudioCueSettings GetCue(Milestone3Cue cue) => cue != Milestone3Cue.TumbleImpact02
            && cue != Milestone3Cue.RecycleSmall
            && cues != null && (int)cue < cues.Length
            ? cues[(int)cue] : null;
        internal bool HasCue(Milestone3Cue cue) => HasDirectCue(cue)
            || (cue == Milestone3Cue.TumbleImpact && HasDirectCue(Milestone3Cue.TumbleImpact03));
        internal bool HasDirectCue(Milestone3Cue cue) => GetCue(cue) != null && !GetCue(cue).EventReference.Guid.IsNull;
        internal bool HasAssignedEvents
        {
            get
            {
                for (int i = 0; i < Milestone3AudioCatalog.CueCount; i++)
                    if (Milestone3AudioCatalog.HasObservableTrigger(i) && HasCue((Milestone3Cue)i)) return true;
                return false;
            }
        }
        private static AudioCueSettings[] CreateCues()
        {
            var result = new AudioCueSettings[Milestone3AudioCatalog.CueCount];
            for (int i = 0; i < result.Length; i++) result[i] = new AudioCueSettings();
            return result;
        }
    }
}
