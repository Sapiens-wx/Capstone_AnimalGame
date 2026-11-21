using System;
using AnimalGame.MainUI;
using FMOD;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using STOP_MODE = FMOD.Studio.STOP_MODE;

namespace Capstone.Audio
{
    internal enum OpeningAudioCue
    {
        RestartHold, RestartConfirmed, StatusScan, StatusGlitch, Title, Confirm,
        Typing, TestResult, InstrumentOnline, InstrumentGlitch,
        ArmDeploy, ArmServo, ArmJam, ArmRetract, ArmSettled,
        SensorsRestart, ContourReveal, ContourDrop, TerrainReveal, TerrainDrop,
        MapReady, Handoff, Count
    }

    [CreateAssetMenu(menuName = "Audio/Opening Audio Settings")]
    public sealed class OpeningAudioSettings : ScriptableObject
    {
        public const string ResourceName = "OpeningAudioSettings";

        [Header("Restart and diagnostics")]
        [SerializeField] private AudioCueSettings restartHold = new();
        [SerializeField] private AudioCueSettings restartConfirmed = new();
        [SerializeField] private AudioCueSettings statusScan = new();
        [SerializeField] private AudioCueSettings statusGlitch = new();
        [SerializeField] private AudioCueSettings title = new();
        [SerializeField] private AudioCueSettings confirm = new();
        [SerializeField] private AudioCueSettings typing = new();
        [SerializeField] private AudioCueSettings testResult = new();
        [SerializeField] private AudioCueSettings instrumentOnline = new();
        [SerializeField] private AudioCueSettings instrumentGlitch = new();

        [Header("Arm self-test")]
        [SerializeField] private AudioCueSettings armDeploy = new();
        [SerializeField] private AudioCueSettings armServo = new();
        [SerializeField] private AudioCueSettings armJam = new();
        [SerializeField] private AudioCueSettings armRetract = new();
        [SerializeField] private AudioCueSettings armSettled = new();

        [Header("Sensor recovery")]
        [SerializeField] private AudioCueSettings sensorsRestart = new();
        [SerializeField] private AudioCueSettings contourReveal = new();
        [SerializeField] private AudioCueSettings contourDrop = new();
        [SerializeField] private AudioCueSettings terrainReveal = new();
        [SerializeField] private AudioCueSettings terrainDrop = new();
        [SerializeField] private AudioCueSettings mapReady = new();
        [SerializeField] private AudioCueSettings handoff = new();

        internal AudioCueSettings GetCue(OpeningAudioCue cue) => cue switch
        {
            OpeningAudioCue.RestartHold => restartHold,
            OpeningAudioCue.RestartConfirmed => restartConfirmed,
            OpeningAudioCue.StatusScan => statusScan,
            OpeningAudioCue.StatusGlitch => statusGlitch,
            OpeningAudioCue.Title => title,
            OpeningAudioCue.Confirm => confirm,
            OpeningAudioCue.Typing => typing,
            OpeningAudioCue.TestResult => testResult,
            OpeningAudioCue.InstrumentOnline => instrumentOnline,
            OpeningAudioCue.InstrumentGlitch => instrumentGlitch,
            OpeningAudioCue.ArmDeploy => armDeploy,
            OpeningAudioCue.ArmServo => armServo,
            OpeningAudioCue.ArmJam => armJam,
            OpeningAudioCue.ArmRetract => armRetract,
            OpeningAudioCue.ArmSettled => armSettled,
            OpeningAudioCue.SensorsRestart => sensorsRestart,
            OpeningAudioCue.ContourReveal => contourReveal,
            OpeningAudioCue.ContourDrop => contourDrop,
            OpeningAudioCue.TerrainReveal => terrainReveal,
            OpeningAudioCue.TerrainDrop => terrainDrop,
            OpeningAudioCue.MapReady => mapReady,
            OpeningAudioCue.Handoff => handoff,
            _ => throw new ArgumentOutOfRangeException(nameof(cue), cue, null)
        };
    }

    // Owns only opening voices. The shared FMOD listener and gameplay voices stay active independently.
    internal sealed class OpeningAudioPlayer
    {
        private struct Voice
        {
            public EventInstance Instance;
            public SoundHandle Sound;
        }

        private readonly OpeningAudioSettings settings;
        private readonly Voice[] voices = new Voice[(int)OpeningAudioCue.Count];
        private readonly bool[] failed = new bool[(int)OpeningAudioCue.Count];

        internal OpeningAudioPlayer(OpeningAudioSettings settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        internal void Play(OpeningAudioCue cue)
        {
            AudioCueSettings binding = settings.GetCue(cue);
            int index = (int)cue;
            if (failed[index] || binding.EventReference.IsNull) return;

            Release(ref voices[index], STOP_MODE.IMMEDIATE);
            Voice voice = default;
            try
            {
                voice.Instance = RuntimeManager.CreateInstance(binding.EventReference);
                Check(voice.Instance.setVolume(binding.LinearGain));
                Check(voice.Instance.start());
                if (binding.ReadBySoundUI)
                    voice.Sound = SoundUIPlayback.BeginSound(binding.SoundUIStrength);
                voices[index] = voice;
            }
            catch (Exception exception)
            {
                Release(ref voice, STOP_MODE.IMMEDIATE);
                failed[index] = true;
                UnityEngine.Debug.LogWarning($"[Opening Audio] {cue}: {exception.Message}", settings);
            }
        }

        internal void Tick()
        {
            for (int i = 0; i < voices.Length; i++)
            {
                if (!voices[i].Instance.hasHandle()) continue;
                RESULT result = voices[i].Instance.getPlaybackState(out PLAYBACK_STATE state);
                if (result != RESULT.OK || state == PLAYBACK_STATE.STOPPED) Release(ref voices[i], null);
            }
        }

        internal void Stop(OpeningAudioCue cue)
        {
            ref Voice voice = ref voices[(int)cue];
            if (voice.Instance.hasHandle() && voice.Instance.stop(STOP_MODE.ALLOWFADEOUT) != RESULT.OK)
                Release(ref voice, STOP_MODE.IMMEDIATE);
        }

        internal void StopAll()
        {
            for (int i = 0; i < voices.Length; i++)
                Release(ref voices[i], STOP_MODE.IMMEDIATE);
        }

        internal void Finish()
        {
            for (int i = 0; i < voices.Length; i++)
            {
                ref Voice voice = ref voices[i];
                if (!voice.Instance.hasHandle()) continue;
                if (voice.Sound.Id != 0)
                {
                    // The opening stops ticking at handoff, so end the tracked sound with its voice.
                    Release(ref voice, STOP_MODE.IMMEDIATE);
                    continue;
                }
                bool oneShot = voice.Instance.getDescription(out EventDescription description) == RESULT.OK
                    && description.isOneshot(out bool finite) == RESULT.OK && finite;
                Release(ref voice, oneShot ? null : STOP_MODE.ALLOWFADEOUT);
            }
        }

        private static void Release(ref Voice voice, STOP_MODE? stop)
        {
            SoundUIPlayback.EndSound(voice.Sound);
            if (voice.Instance.hasHandle() && voice.Instance.isValid())
            {
                if (stop.HasValue) voice.Instance.stop(stop.Value);
                voice.Instance.release();
            }
            voice = default;
        }

        private static void Check(RESULT result)
        {
            if (result != RESULT.OK) throw new InvalidOperationException(Error.String(result));
        }
    }
}
