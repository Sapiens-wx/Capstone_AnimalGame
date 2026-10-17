using System;
using FMODUnity;
using UnityEngine;

namespace Capstone.Audio
{
    [CreateAssetMenu(menuName = "Audio/Milestone 1 Audio Settings")]
    public sealed class Milestone1AudioSettings : ScriptableObject
    {
        [SerializeField] private AudioCueSettings scanCharge = new AudioCueSettings();
        [SerializeField] private AudioCueSettings scanPulse = new AudioCueSettings();
        [SerializeField] private AudioCueSettings cameraOpen = new AudioCueSettings();
        [SerializeField] private AudioCueSettings cameraClose = new AudioCueSettings();
        [SerializeField] private AudioCueSettings cameraMove = new AudioCueSettings();
        [SerializeField] private AudioCueSettings cameraFocus = new AudioCueSettings();
        [SerializeField] private AudioCueSettings cameraShutter = new AudioCueSettings();

        [SerializeField, Min(0f)] private float fadeOutSeconds = 0.05f;
        [Tooltip("Minimum actual aim-point speed required to hear camera movement.")]
        [SerializeField, Min(0f)] private float cameraMoveThreshold = 0.01f;
        [Tooltip("Brief grace period for intermittent movement samples.")]
        [SerializeField, Min(0f)] private float cameraMoveStopDelay = 0.05f;

        [Header("Camera Movement Playback Rate")]
        [Tooltip("Change only the Camera Move sound's playback rate from actual aim movement. Pitch changes with rate; the source WAV is untouched.")]
        [SerializeField] private bool cameraMovePlaybackRateEnabled = true;
        [SerializeField, Min(0.01f)] private float cameraMoveMinimumPlaybackRate = 0.8f;
        [SerializeField, Min(0.01f)] private float cameraMoveMaximumPlaybackRate = 2f;

        [Header("Optional Camera Dynamics")]
        [Tooltip("Enable after authoring the camera parameters and their tempo/volume curves in FMOD Studio.")]
        [SerializeField] private bool cameraDynamicsEnabled = false;
        [Tooltip("Actual aim-point speed that maps to a MoveSpeed value of 1. Does not change gameplay movement.")]
        [SerializeField, Min(0.001f)] private float cameraMoveReferenceSpeed = 5f;
        [Tooltip("Local continuous 0..1 FMOD parameter on the Camera Move event. Configure before playback.")]
        [SerializeField] private string cameraMoveParameterName = "MoveSpeed";
        [Tooltip("Local continuous 0..1 FMOD parameter on the Camera Focus event. Configure before playback.")]
        [SerializeField] private string cameraFocusParameterName = "FocusProgress";
        [Tooltip("Smoothing time for normalized camera parameters. Zero follows the observed state directly.")]
        [SerializeField, Min(0f)] private float cameraParameterSmoothingSeconds = 0.05f;
        [Tooltip("Minimum normalized change before sending another FMOD parameter command.")]
        [SerializeField, Range(0f, 0.1f)] private float cameraParameterEpsilon = 0.005f;

        internal float FadeOutSeconds => Mathf.Max(0f, fadeOutSeconds);
        internal float CameraMoveThreshold => Mathf.Max(0f, cameraMoveThreshold);
        internal float CameraMoveStopDelay => Mathf.Max(0f, cameraMoveStopDelay);
        internal bool CameraMovePlaybackRateEnabled => cameraMovePlaybackRateEnabled;
        internal float CameraMoveMinimumPlaybackRate => Mathf.Max(0.01f, cameraMoveMinimumPlaybackRate);
        internal float CameraMoveMaximumPlaybackRate => Mathf.Max(CameraMoveMinimumPlaybackRate, cameraMoveMaximumPlaybackRate);
        internal bool CameraDynamicsEnabled => cameraDynamicsEnabled;
        internal float CameraMoveReferenceSpeed => Mathf.Max(0.001f, cameraMoveReferenceSpeed);
        internal string CameraMoveParameterName => cameraMoveParameterName;
        internal string CameraFocusParameterName => cameraFocusParameterName;
        internal float CameraParameterSmoothingSeconds => Mathf.Max(0f, cameraParameterSmoothingSeconds);
        internal float CameraParameterEpsilon => Mathf.Clamp(cameraParameterEpsilon, 0f, 0.1f);

        internal float CameraMovePlaybackRate(float actualSpeed)
        {
            if (!CameraMovePlaybackRateEnabled) return 1f;
            float fraction = Mathf.InverseLerp(CameraMoveThreshold,
                Mathf.Max(CameraMoveThreshold + 0.001f, CameraMoveReferenceSpeed), actualSpeed);
            return Mathf.Lerp(CameraMoveMinimumPlaybackRate, CameraMoveMaximumPlaybackRate, fraction);
        }

        internal AudioCueSettings GetCue(Milestone1Cue cue)
        {
            switch (cue)
            {
                case Milestone1Cue.ScanCharge: return scanCharge;
                case Milestone1Cue.ScanPulse: return scanPulse;
                case Milestone1Cue.CameraOpen: return cameraOpen;
                case Milestone1Cue.CameraClose: return cameraClose;
                case Milestone1Cue.CameraMove: return cameraMove;
                case Milestone1Cue.CameraFocus: return cameraFocus;
                case Milestone1Cue.CameraShutter: return cameraShutter;
                default: throw new ArgumentOutOfRangeException(nameof(cue), cue, null);
            }
        }
    }

    [Serializable]
    internal sealed class AudioCueSettings
    {
        [SerializeField] private EventReference eventReference;
        [SerializeField, Range(-80f, 12f)] private float gainDb;
        [Tooltip("Report this cue's actual playback lifetime to the Main UI sound wave.")]
        [SerializeField] private bool readBySoundUI;
        [Tooltip("Designed noise strength (0..1), independent of the player's mixer volume.")]
        [SerializeField, Range(0f, 1f)] private float soundUIStrength = .5f;

        internal EventReference EventReference => eventReference;
        internal float LinearGain => Mathf.Pow(10f, Mathf.Clamp(gainDb, -80f, 12f) / 20f);
        internal bool ReadBySoundUI => readBySoundUI;
        internal float SoundUIStrength => Mathf.Clamp01(soundUIStrength);
    }

    internal enum Milestone1Cue
    {
        ScanCharge,
        ScanPulse,
        CameraOpen,
        CameraClose,
        CameraMove,
        CameraFocus,
        CameraShutter
    }
}
