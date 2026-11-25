using System;
using System.IO;
using AnimalGame.Animals;
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
        RestartConfirmed, StatusScan, StatusGlitch, Title, Confirm,
        Typing, TestResult, InstrumentOnline, InstrumentGlitch,
        ArmDeploy, ArmServo, ArmJam, ArmRetract, ArmSettled,
        SensorsRestart, ContourReveal, ContourDrop, TerrainReveal, TerrainDrop,
        MapReady, Handoff, WoodpeckerPeck, Count
    }

    [CreateAssetMenu(menuName = "Audio/Opening Audio Settings")]
    public sealed class OpeningAudioSettings : ScriptableObject
    {
        public const string ResourceName = "OpeningAudioSettings";

        [Header("Woodpecker")]
        [SerializeField] private AudioCueSettings woodpeckerPeck = new();
        [SerializeField] private Shader woodpeckerWaveShader;

        [Header("Restart and diagnostics")]
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

        internal Shader WoodpeckerWaveShader => woodpeckerWaveShader;

        internal AudioCueSettings GetCue(OpeningAudioCue cue) => cue switch
        {
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
            OpeningAudioCue.WoodpeckerPeck => woodpeckerPeck,
            _ => throw new ArgumentOutOfRangeException(nameof(cue), cue, null)
        };
    }

    // Owns only opening voices. The shared FMOD listener and gameplay voices stay active independently.
    internal sealed class OpeningAudioPlayer
    {
        private static readonly AnimalSoundWaveSettings peckWave = new(2.5f, .42f, 3, 1f, 1f);
        private static readonly AnimalSoundWaveSettings takeoffWave = new(3.8f, .55f, 4, 1f, 1f);
        private const string LandingRecording = "365485__shanaygroen__bird-in-cage-flapping-wings.wav";
        private const string TakeoffRecording = "Starting Raven.wav";

        private struct Voice
        {
            public EventInstance Instance;
            public SoundHandle Sound;
            public double StopAt;
        }

        private readonly OpeningAudioSettings settings;
        private readonly Voice[] voices = new Voice[(int)OpeningAudioCue.Count];
        private readonly bool[] failed = new bool[(int)OpeningAudioCue.Count];
        private Bus recordingBus;
        private Sound woodpeckerRecording;
        private Channel woodpeckerChannel;
        private SoundHandle woodpeckerSound;
        private bool recordingBusLocked;
        private double woodpeckerRecordingStopAt = double.PositiveInfinity;

        internal OpeningAudioPlayer(OpeningAudioSettings settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        internal void PlayWoodpeckerPeck(Vector3 position, float bodyRadius)
        {
            PlayCue(OpeningAudioCue.WoodpeckerPeck, pitch: 1.45f, maximumDuration: .21f);
            EmitWoodpeckerWave(position, bodyRadius, peckWave);
        }

        internal void PlayWoodpeckerLanding() => PlayWoodpeckerRecording(LandingRecording, -11f, .22f,
            startMs: 560, maximumDuration: .95f);

        internal void PlayWoodpeckerTakeoff(Vector3 position, float bodyRadius)
        {
            PlayWoodpeckerRecording(TakeoffRecording, 4f, .35f);
            EmitWoodpeckerWave(position, bodyRadius, takeoffWave);
        }

        private void PlayWoodpeckerRecording(string file, float gainDb, float strength, uint startMs = 0,
            float maximumDuration = 0f)
        {
            StopWoodpeckerRecording();
            string path = Path.Combine(Application.streamingAssetsPath, "Opening", "Woodpecker", file);
            try
            {
                // Route raw recordings through the existing FMOD mixer and its mute controls.
                recordingBus = RuntimeManager.GetBus("bus:/");
                RESULT lockResult = recordingBus.lockChannelGroup();
                // The nature ambience can already own this shared lock.
                if (lockResult != RESULT.ERR_ALREADY_LOCKED) Check(lockResult);
                recordingBusLocked = lockResult == RESULT.OK;
                Check(RuntimeManager.StudioSystem.flushCommands());
                Check(recordingBus.getChannelGroup(out ChannelGroup group));

                var core = RuntimeManager.CoreSystem;
                Check(core.createSound(path, MODE._2D | MODE.LOOP_OFF, out woodpeckerRecording));
                Check(core.playSound(woodpeckerRecording, group, true, out woodpeckerChannel));
                Check(woodpeckerChannel.setVolume(Mathf.Pow(10f, gainDb / 20f)));
                if (startMs != 0) Check(woodpeckerChannel.setPosition(startMs, TIMEUNIT.MS));
                Check(woodpeckerChannel.setPaused(false));
                if (maximumDuration > 0f) woodpeckerRecordingStopAt = Time.unscaledTimeAsDouble + maximumDuration;
                woodpeckerSound = SoundUIPlayback.BeginSound(strength);
            }
            catch (Exception exception)
            {
                StopWoodpeckerRecording();
                UnityEngine.Debug.LogWarning($"[Opening Audio] {file}: {exception.Message}", settings);
            }
        }

        private void StopWoodpeckerRecording()
        {
            SoundUIPlayback.EndSound(woodpeckerSound);
            woodpeckerSound = default;
            if (RuntimeManager.IsInitialized)
            {
                if (woodpeckerChannel.hasHandle()) woodpeckerChannel.stop();
                if (woodpeckerRecording.hasHandle()) woodpeckerRecording.release();
                if (recordingBusLocked && recordingBus.isValid()) recordingBus.unlockChannelGroup();
            }
            woodpeckerChannel.clearHandle();
            woodpeckerRecording.clearHandle();
            woodpeckerRecordingStopAt = double.PositiveInfinity;
            recordingBusLocked = false;
        }

        private void EmitWoodpeckerWave(Vector3 position, float bodyRadius, AnimalSoundWaveSettings wave)
        {
            AnimalSoundWaveManager.Emit(position, wave, settings.WoodpeckerWaveShader,
                new Color(.88f, .88f, .88f, .9f), .38f, .28f, 995, bodyRadius, 1f, opening: true);
        }

        internal void Play(OpeningAudioCue cue) => PlayCue(cue);

        private void PlayCue(OpeningAudioCue cue, float pitch = 1f, float maximumDuration = 0f)
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
                if (pitch != 1f) Check(voice.Instance.setPitch(pitch));
                Check(voice.Instance.start());
                if (maximumDuration > 0f) voice.StopAt = Time.unscaledTimeAsDouble + maximumDuration;
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
            double now = Time.unscaledTimeAsDouble;
            if (woodpeckerChannel.hasHandle() && (now >= woodpeckerRecordingStopAt
                || woodpeckerChannel.isPlaying(out bool playing) != RESULT.OK || !playing))
                StopWoodpeckerRecording();

            for (int i = 0; i < voices.Length; i++)
            {
                if (!voices[i].Instance.hasHandle()) continue;
                if (voices[i].StopAt > 0 && now >= voices[i].StopAt)
                {
                    Stop((OpeningAudioCue)i);
                    continue;
                }
                RESULT result = voices[i].Instance.getPlaybackState(out PLAYBACK_STATE state);
                if (result != RESULT.OK || state == PLAYBACK_STATE.STOPPED) Release(ref voices[i], null);
            }
        }

        internal void Stop(OpeningAudioCue cue)
        {
            ref Voice voice = ref voices[(int)cue];
            voice.StopAt = 0;
            if (voice.Instance.hasHandle() && voice.Instance.stop(STOP_MODE.ALLOWFADEOUT) != RESULT.OK)
                Release(ref voice, STOP_MODE.IMMEDIATE);
        }

        internal void StopAll()
        {
            for (int i = 0; i < voices.Length; i++)
                Release(ref voices[i], STOP_MODE.IMMEDIATE);
            StopWoodpeckerRecording();
            AnimalSoundWaveManager.ClearOpeningWaves();
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
