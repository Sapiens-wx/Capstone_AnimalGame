using System;
using System.IO;
using AnimalGame.Animals;
using AnimalGame.MainUI;
using FMOD;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace Capstone.Audio
{
    internal sealed class RavenFlyoverAudio : IDisposable
    {
        internal const string RecordingName = "Starting Raven.wav";

        // Use the two strongest calls in the recording, separated by their quiet tails.
        private const uint FirstCallStartMs = 220;
        private const uint FirstCallDurationMs = 1040;
        private const uint SecondCallStartMs = 1280;
        private const uint SecondCallDurationMs = 680;
        private const float CallVolume = 2.8f;
        private static readonly AnimalSoundWaveSettings callWave = new(4.2f, .8f, 4, 1f, 1f);

        private Sound sample;
        private Channel channel;
        private Bus masterBus;
        private SoundHandle hudSound;
        private bool busLocked;

        internal void Preload()
        {
            if (sample.hasHandle()) return;
            string path = Path.Combine(Application.streamingAssetsPath, "Opening", "Woodpecker", RecordingName);
            try
            {
                Check(RuntimeManager.CoreSystem.createSound(path, MODE._3D | MODE.LOOP_OFF, out sample));
            }
            catch (Exception error)
            {
                Dispose();
                UnityEngine.Debug.LogWarning($"[Raven Audio] {error.Message}");
            }
        }

        internal void PlayCall(Vector3 worldPosition, bool secondCall)
        {
            if (!sample.hasHandle()) return;
            StopCall();
            try
            {
                masterBus = RuntimeManager.GetBus("bus:/");
                RESULT lockResult = masterBus.lockChannelGroup();
                // Nature ambience may already have the master group locked.
                if (lockResult != RESULT.ERR_ALREADY_LOCKED) Check(lockResult);
                busLocked = lockResult == RESULT.OK;
                Check(RuntimeManager.StudioSystem.flushCommands());
                Check(masterBus.getChannelGroup(out ChannelGroup group));

                var core = RuntimeManager.CoreSystem;
                Check(core.playSound(sample, group, true, out channel));
                uint startMs = secondCall ? SecondCallStartMs : FirstCallStartMs;
                uint durationMs = secondCall ? SecondCallDurationMs : FirstCallDurationMs;
                Check(channel.setPosition(startMs, TIMEUNIT.MS));
                Check(channel.setVolume(secondCall ? CallVolume * 1.65f : CallVolume));
                Check(channel.set3DMinMaxDistance(14f, 80f));
                Check(PlaceChannel(worldPosition));
                Check(core.getSoftwareFormat(out int rate, out _, out _));
                Check(channel.getDSPClock(out _, out ulong clock));
                ulong end = clock + (ulong)rate * durationMs / 1000;
                Check(channel.addFadePoint(clock, 0f));
                Check(channel.addFadePoint(clock + (ulong)rate * 15 / 1000, 1f));
                Check(channel.addFadePoint(end - (ulong)rate * 200 / 1000, 1f));
                Check(channel.addFadePoint(end, 0f));
                Check(channel.setDelay(0, end));
                Check(channel.setPaused(false));

                hudSound = SoundUIPlayback.BeginSound(.65f);
                AnimalSoundWaveManager.Emit(worldPosition, callWave, null, new Color(.88f, .88f, .88f, .9f),
                    .38f, .28f, 995, 1f, 1f);
            }
            catch (Exception error)
            {
                StopCall();
                UnityEngine.Debug.LogWarning($"[Raven Audio] {error.Message}");
            }
        }

        internal void Tick(Vector3 worldPosition)
        {
            if (!channel.hasHandle()) return;
            if (channel.isPlaying(out bool playing) != RESULT.OK || !playing)
                StopCall();
            else
                PlaceChannel(worldPosition);
        }

        private RESULT PlaceChannel(Vector3 worldPosition)
        {
            VECTOR position = RuntimeUtils.ToFMODVector(worldPosition);
            VECTOR velocity = default;
            return channel.set3DAttributes(ref position, ref velocity);
        }

        private void StopCall()
        {
            SoundUIPlayback.EndSound(hudSound);
            hudSound = default;
            if (RuntimeManager.IsInitialized)
            {
                if (channel.hasHandle()) channel.stop();
                if (busLocked && masterBus.isValid()) masterBus.unlockChannelGroup();
            }
            channel.clearHandle();
            masterBus.clearHandle();
            busLocked = false;
        }

        public void Dispose()
        {
            StopCall();
            if (sample.hasHandle() && RuntimeManager.IsInitialized) sample.release();
            sample.clearHandle();
        }

        private static void Check(RESULT result)
        {
            if (result != RESULT.OK) throw new InvalidOperationException(Error.String(result));
        }
    }
}
