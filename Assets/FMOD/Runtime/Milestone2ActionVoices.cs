using System;
using FMOD;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace Capstone.Audio
{
    internal sealed class Milestone2ActionVoices
    {
        private sealed class Slot
        {
            internal EventInstance Instance;
            internal EventDescription Description;
            internal readonly ParameterState[] Parameters = new ParameterState[(int)ActionAudioParameter.Count];
            internal bool Active, Failed, Fading;
            internal float Gain, FadeElapsed, PollElapsed;
        }

        private struct ParameterState
        {
            internal PARAMETER_ID Id;
            internal int Resolved; // 0 not queried, 1 valid, -1 unavailable.
            internal bool HasValue;
            internal float Value;
            internal bool HasDesiredValue;
            internal float DesiredValue;
        }

        private readonly Milestone2ActionSettings settings;
        private readonly Slot[] slots = new Slot[Milestone2ActionCatalog.CueCount];

        internal Milestone2ActionVoices(Milestone2ActionSettings settings)
        {
            this.settings = settings;
            for (int index = 0; index < slots.Length; index++) slots[index] = new Slot();
        }

        // A fixed slot per cue bounds concurrency; repeatedly requesting a loop
        // does not create new instances or allocate. Empty references stay silent.
        internal bool Play(Milestone2ActionCue cue)
        {
            Slot slot = slots[(int)cue];
            AudioCueSettings config = settings.GetCue(cue);
            if (slot.Failed || config == null || config.EventReference.Guid.IsNull) return false;
            Release(slot);
            try
            {
                slot.Instance = RuntimeManager.CreateInstance(config.EventReference);
                slot.Gain = config.LinearGain;
                RESULT result = slot.Instance.setVolume(slot.Gain);
                if (result == RESULT.OK) result = slot.Instance.getDescription(out slot.Description);
                if (result != RESULT.OK) throw new InvalidOperationException(result.ToString());
                slot.Active = true;
                slot.Fading = false;
                slot.PollElapsed = 0f;
                for (int index = 0; index < slot.Parameters.Length; index++)
                {
                    slot.Parameters[index].Resolved = 0;
                    slot.Parameters[index].HasValue = false;
                    if (slot.Parameters[index].HasDesiredValue)
                        ApplyParameter(slot, (ActionAudioParameter)index);
                }
                result = slot.Instance.start();
                if (result != RESULT.OK) throw new InvalidOperationException(result.ToString());
                return true;
            }
            catch (Exception exception)
            {
                Release(slot);
                slot.Failed = true;
                UnityEngine.Debug.LogWarning("[Milestone 2 Audio] " + Milestone2ActionCatalog.EventPath((int)cue) + ": " + exception.Message);
                return false;
            }
        }

        internal bool Play(Milestone2ActionCue cue, ActionAudioParameter parameter, float value)
        {
            Slot slot = slots[(int)cue];
            StoreDesiredParameter(slot, parameter, value);
            return Play(cue);
        }

        internal void SetLoop(Milestone2ActionCue cue, bool active)
        {
            Slot slot = slots[(int)cue];
            if (active)
            {
                if (!slot.Active || slot.Fading) Play(cue);
            }
            else Stop(cue);
        }

        internal void SetParameter(Milestone2ActionCue cue, ActionAudioParameter parameter, float value)
        {
            Slot slot = slots[(int)cue];
            StoreDesiredParameter(slot, parameter, value);
            ApplyParameter(slot, parameter);
        }

        private static void StoreDesiredParameter(Slot slot, ActionAudioParameter parameter, float value)
        {
            int index = (int)parameter;
            slot.Parameters[index].HasDesiredValue = true;
            slot.Parameters[index].DesiredValue = parameter == ActionAudioParameter.Direction
                ? Mathf.Clamp(value, -1f, 1f) : Mathf.Clamp01(value);
        }

        private void ApplyParameter(Slot slot, ActionAudioParameter parameter)
        {
            if (!slot.Active || slot.Fading || !slot.Instance.isValid()) return;
            int index = (int)parameter;
            ParameterState state = slot.Parameters[index];
            if (state.Resolved == 0)
            {
                RESULT result = slot.Description.getParameterDescriptionByName(
                    Milestone2ActionCatalog.ParameterName(parameter), out PARAMETER_DESCRIPTION description);
                const PARAMETER_FLAGS unsupported = PARAMETER_FLAGS.GLOBAL | PARAMETER_FLAGS.READONLY
                                                   | PARAMETER_FLAGS.DISCRETE | PARAMETER_FLAGS.LABELED;
                state.Resolved = result == RESULT.OK && description.type == PARAMETER_TYPE.GAME_CONTROLLED
                    && (description.flags & unsupported) == 0 ? 1 : -1;
                if (state.Resolved == 1) state.Id = description.id;
                slot.Parameters[index] = state;
            }
            if (state.Resolved != 1) return;
            float value = state.DesiredValue;
            if (state.HasValue && Mathf.Abs(state.Value - value) < settings.ParameterEpsilon) return;
            if (slot.Instance.setParameterByID(state.Id, value) != RESULT.OK) state.Resolved = -1;
            state.Value = value;
            state.HasValue = true;
            slot.Parameters[index] = state;
        }

        internal void Stop(Milestone2ActionCue cue, bool immediately = false)
        {
            Slot slot = slots[(int)cue];
            if (!slot.Active) return;
            if (immediately || settings.FadeOutSeconds <= 0f) Release(slot);
            else if (!slot.Fading)
            {
                slot.Fading = true;
                slot.FadeElapsed = 0f;
            }
        }

        internal void Tick(float deltaTime)
        {
            foreach (Slot slot in slots)
            {
                if (!slot.Active) continue;
                if (!slot.Instance.isValid()) { Release(slot); continue; }
                if (slot.Fading)
                {
                    slot.FadeElapsed += deltaTime;
                    if (slot.FadeElapsed >= settings.FadeOutSeconds) { Release(slot); continue; }
                    slot.Instance.setVolume(slot.Gain * (1f - slot.FadeElapsed / settings.FadeOutSeconds));
                }
                // One-shots complete asynchronously. Poll only live handles at
                // 10 Hz rather than querying every slot on every frame.
                slot.PollElapsed += deltaTime;
                if (slot.PollElapsed >= 0.1f)
                {
                    slot.PollElapsed = 0f;
                    if (slot.Instance.getPlaybackState(out PLAYBACK_STATE state) == RESULT.OK
                        && state == PLAYBACK_STATE.STOPPED) Release(slot);
                }
            }
        }

        internal void StopAllImmediately()
        {
            foreach (Slot slot in slots) Release(slot);
        }

        private static void Release(Slot slot)
        {
            if (slot.Instance.hasHandle() && RuntimeManager.IsInitialized && slot.Instance.isValid())
            {
                slot.Instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                slot.Instance.release();
            }
            slot.Instance.clearHandle();
            slot.Active = false;
            slot.Fading = false;
        }
    }
}
