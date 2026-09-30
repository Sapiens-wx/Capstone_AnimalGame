using System;
using System.Collections.Generic;
using FMOD;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace Capstone.Audio
{
    internal sealed class Milestone1AudioVoices
    {
        private readonly Milestone1AudioSettings settings;
        private readonly List<Milestone1AudioVoice> voices = new List<Milestone1AudioVoice>();
        private readonly HashSet<Milestone1Cue> reportedFailures = new HashSet<Milestone1Cue>();

        internal Milestone1AudioVoices(Milestone1AudioSettings settings)
        {
            this.settings = settings;
        }

        internal Milestone1AudioVoice Play(Milestone1Cue cue)
        {
            AudioCueSettings cueSettings = settings.GetCue(cue);
            if (cueSettings == null || cueSettings.EventReference.IsNull)
            {
                ReportFailure(cue, "No FMOD event is assigned in Milestone1AudioSettings.");
                return null;
            }

            EventInstance instance = default;
            try
            {
                instance = RuntimeManager.CreateInstance(cueSettings.EventReference);
                RESULT result = instance.setVolume(cueSettings.LinearGain);
                if (result == RESULT.OK)
                    result = instance.start();
                if (result != RESULT.OK)
                {
                    if (instance.isValid())
                        instance.release();
                    ReportFailure(cue, result.ToString());
                    return null;
                }

                var voice = new Milestone1AudioVoice(instance, cueSettings.LinearGain);
                voices.Add(voice);
                Milestone1AudioDiagnostics.RecordStarted(EventPath(cue));
                return voice;
            }
            catch (Exception exception)
            {
                if (instance.isValid())
                {
                    instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                    instance.release();
                }
                ReportFailure(cue, exception.Message);
                return null;
            }
        }

        internal void Tick(float deltaTime)
        {
            for (int index = voices.Count - 1; index >= 0; index--)
            {
                voices[index].Tick(deltaTime);
                if (voices[index].IsReleased)
                    voices.RemoveAt(index);
            }
        }

        internal void StopAll(bool immediately)
        {
            foreach (Milestone1AudioVoice voice in voices)
                voice.Stop(immediately ? 0f : settings.FadeOutSeconds);
            if (immediately)
                voices.Clear();
        }

        internal void Stop(Milestone1AudioVoice voice, bool immediately = false)
        {
            voice?.Stop(immediately ? 0f : settings.FadeOutSeconds);
        }

        private void ReportFailure(Milestone1Cue cue, string reason)
        {
            if (reportedFailures.Add(cue))
                UnityEngine.Debug.LogWarning($"[Milestone 1 Audio] {EventPath(cue)}: {reason}");
        }

        private static string EventPath(Milestone1Cue cue)
        {
            switch (cue)
            {
                case Milestone1Cue.ScanCharge: return "event:/Robot/Scan/Charge";
                case Milestone1Cue.ScanPulse: return "event:/Robot/Scan/Pulse";
                case Milestone1Cue.CameraOpen: return "event:/Robot/Camera/Open";
                case Milestone1Cue.CameraClose: return "event:/Robot/Camera/Close";
                case Milestone1Cue.CameraMove: return "event:/Robot/Camera/Move";
                case Milestone1Cue.CameraFocus: return "event:/Robot/Camera/Focus";
                case Milestone1Cue.CameraShutter: return "event:/Robot/Camera/Shutter";
                default: return cue.ToString();
            }
        }
    }

    internal sealed class Milestone1AudioVoice
    {
        private EventInstance instance;
        private readonly float initialVolume;
        private float age;
        private float fadeElapsed;
        private float fadeDuration;
        private bool fading;

        internal bool IsReleased { get; private set; }
        internal bool IsPlaying => !IsReleased && !fading;

        internal Milestone1AudioVoice(EventInstance instance, float initialVolume)
        {
            this.instance = instance;
            this.initialVolume = initialVolume;
        }

        internal void Stop(float seconds)
        {
            if (IsReleased)
                return;
            if (seconds <= 0f)
            {
                Release();
                return;
            }
            if (fading)
                return;
            fading = true;
            fadeElapsed = 0f;
            fadeDuration = seconds;
        }

        internal void Tick(float deltaTime)
        {
            if (IsReleased)
                return;
            age += deltaTime;
            if (!instance.isValid())
            {
                Release();
                return;
            }

            if (fading)
            {
                fadeElapsed += deltaTime;
                if (fadeElapsed >= fadeDuration)
                {
                    Release();
                    return;
                }
                instance.setVolume(initialVolume * (1f - fadeElapsed / fadeDuration));
            }

            // FMOD starts asynchronously; allow the first command update to complete.
            if (age >= 0.1f
                && instance.getPlaybackState(out PLAYBACK_STATE state) == RESULT.OK
                && state == PLAYBACK_STATE.STOPPED)
            {
                Release();
            }
        }

        private void Release()
        {
            if (IsReleased)
                return;
            if (instance.isValid())
            {
                instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                instance.release();
            }
            instance.clearHandle();
            IsReleased = true;
            Milestone1AudioDiagnostics.RecordReleased();
        }
    }
}
