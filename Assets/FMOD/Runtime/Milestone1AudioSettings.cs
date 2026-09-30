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

        internal float FadeOutSeconds => Mathf.Max(0f, fadeOutSeconds);
        internal float CameraMoveThreshold => Mathf.Max(0f, cameraMoveThreshold);
        internal float CameraMoveStopDelay => Mathf.Max(0f, cameraMoveStopDelay);

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

        internal EventReference EventReference => eventReference;
        internal float LinearGain => Mathf.Pow(10f, Mathf.Clamp(gainDb, -80f, 12f) / 20f);
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
