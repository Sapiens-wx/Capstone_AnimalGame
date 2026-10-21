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

        internal bool Enabled => enableAudio;
        internal float FadeOutSeconds => Mathf.Max(0f, fadeOutSeconds);
        internal float ParameterEpsilon => Mathf.Max(0.0001f, parameterEpsilon);
        internal float TumbleMotionThreshold => Mathf.Max(0f, tumbleMotionThreshold);
        internal float TumbleReferenceSpeed => Mathf.Max(0.01f, tumbleReferenceSpeed);
        internal float TumbleImpactEnergyReference => Mathf.Max(0.01f, tumbleImpactEnergyReference);
        internal float EffortReferenceFrequency => Mathf.Max(0.01f, effortReferenceFrequency);
        internal float ProgressEpsilon => Mathf.Max(0.000001f, progressEpsilon);
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
