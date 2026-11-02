using System;
using FMOD;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace Capstone.Audio
{
    // One fixed voice per short UI cue. No SoundUI noise report or new listener.
    internal sealed class InventoryAudioVoices
    {
        private readonly InventoryAudioSettings settings;
        private readonly EventInstance[] instances = new EventInstance[InventoryAudioCatalog.CueCount];
        private readonly bool[] failed = new bool[InventoryAudioCatalog.CueCount];
        private float nextPoll;
        internal InventoryAudioVoices(InventoryAudioSettings settings) { this.settings = settings; }

        internal bool Play(InventoryCue cue)
        {
            int index = (int)cue;
            AudioCueSettings config = settings.GetCue(cue);
            if (failed[index] || !InventoryAudioCatalog.HasObservableTrigger(index) || config.EventReference.Guid.IsNull)
                return false;
            Stop(cue);
            EventInstance instance = default;
            try
            {
                instance = RuntimeManager.CreateInstance(config.EventReference);
                Check(instance.setVolume(config.LinearGain));
                Check(instance.start());
                instances[index] = instance;
                return true;
            }
            catch (Exception exception)
            {
                Release(ref instance);
                failed[index] = true;
                UnityEngine.Debug.LogWarning("[Inventory Audio] " + InventoryAudioCatalog.EventPath(index) + ": " + exception.Message);
                return false;
            }
        }
        internal void Tick(float now)
        {
            if (now < nextPoll) return;
            nextPoll = now + .1f;
            for (int i = 0; i < instances.Length; i++)
            {
                if (!instances[i].isValid()) continue;
                RESULT result = instances[i].getPlaybackState(out PLAYBACK_STATE state);
                if (result != RESULT.OK || state == PLAYBACK_STATE.STOPPED) Release(ref instances[i]);
            }
        }
        internal void Stop(InventoryCue cue) { Release(ref instances[(int)cue]); }
        internal void StopAll()
        {
            for (int i = 0; i < instances.Length; i++) Release(ref instances[i]);
        }
        private static void Release(ref EventInstance instance)
        {
            if (instance.isValid())
            {
                instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                instance.release();
            }
            instance.clearHandle();
        }
        private static void Check(RESULT result)
        {
            if (result != RESULT.OK) throw new InvalidOperationException(result.ToString());
        }
    }
}
