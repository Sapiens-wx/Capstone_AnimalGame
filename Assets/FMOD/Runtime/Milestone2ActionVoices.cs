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
            internal bool Active, Failed, Fading, Recovering, Paused, KeyedOff;
            internal float Gain, FadeElapsed, PollElapsed, DesiredPitch = 1f, AppliedPitch = 1f;
            internal float Envelope = 1f, FadeStartEnvelope, FadeDuration;
            internal float RecoveryElapsed, RecoveryStartEnvelope, RecoveryDuration;
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
        internal bool Play(Milestone2ActionCue cue) => Start(cue, 0, 1f);

        internal bool PlayFrom(Milestone2ActionCue cue, int timelineMs) => Start(cue, timelineMs, 1f);

        internal bool PlayWithGain(Milestone2ActionCue cue, float gain) => Start(cue, 0, Mathf.Clamp01(gain));

        internal bool IsActive(Milestone2ActionCue cue) => slots[(int)cue].Active && !slots[(int)cue].Fading;
        internal bool IsAlive(Milestone2ActionCue cue) => slots[(int)cue].Active;

        internal bool TryGetTimelinePosition(Milestone2ActionCue cue, out int milliseconds)
        {
            Slot slot = slots[(int)cue];
            milliseconds = 0;
            return slot.Active && slot.Instance.getTimelinePosition(out milliseconds) == RESULT.OK;
        }

        private bool Start(Milestone2ActionCue cue, int timelineMs, float gain, float fadeInSeconds = 0f)
        {
            Slot slot = slots[(int)cue];
            AudioCueSettings config = settings.GetCue(cue);
            if (slot.Failed || config == null || config.EventReference.Guid.IsNull) return false;
            Release(slot);
            try
            {
                slot.Instance = RuntimeManager.CreateInstance(config.EventReference);
                slot.Gain = config.LinearGain * gain;
                slot.Envelope = fadeInSeconds > 0f ? 0f : 1f;
                RESULT result = slot.Instance.setVolume(slot.Gain * slot.Envelope);
                if (result == RESULT.OK) result = slot.Instance.getDescription(out slot.Description);
                if (result != RESULT.OK) throw new InvalidOperationException(result.ToString());
                slot.Active = true;
                slot.Fading = false;
                slot.Recovering = fadeInSeconds > 0f;
                slot.RecoveryElapsed = 0f;
                slot.RecoveryStartEnvelope = slot.Envelope;
                slot.RecoveryDuration = fadeInSeconds;
                slot.Paused = slot.KeyedOff = false;
                slot.AppliedPitch = slot.DesiredPitch;
                result = slot.Instance.setPitch(slot.AppliedPitch);
                if (result == RESULT.OK && timelineMs > 0) result = slot.Instance.setTimelinePosition(timelineMs);
                if (result != RESULT.OK) throw new InvalidOperationException(result.ToString());
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

        internal void SetLoop(Milestone2ActionCue cue, bool active) => SetLoopFrom(cue, active, 0);

        internal void SetLoopFrom(Milestone2ActionCue cue, bool active, int timelineMs, float fadeInSeconds = -1f)
        {
            Slot slot = slots[(int)cue];
            if (active)
            {
                if (!slot.Active || slot.KeyedOff)
                    Start(cue, timelineMs, 1f, fadeInSeconds >= 0f ? fadeInSeconds
                        : IsMovementLoop(cue) ? settings.MovementFadeInSeconds : 0f);
                else if (slot.Fading)
                {
                    // A brief motion dropout resumes the same sample/phase,
                    // restoring its current gain smoothly instead of restarting.
                    slot.Fading = false;
                    slot.Recovering = true;
                    slot.RecoveryStartEnvelope = slot.Envelope;
                    slot.RecoveryElapsed = 0f;
                    slot.RecoveryDuration = settings.MovementFadeInSeconds;
                    SetPitch(cue, slot.DesiredPitch);
                    for (int index = 0; index < slot.Parameters.Length; index++)
                        if (slot.Parameters[index].HasDesiredValue) ApplyParameter(slot, (ActionAudioParameter)index);
                }
            }
            else Stop(cue);
        }

        internal void SetPitch(Milestone2ActionCue cue, float pitch)
        {
            Slot slot = slots[(int)cue];
            slot.DesiredPitch = Mathf.Clamp(pitch, 0.01f, 4f);
            if (!slot.Active || slot.Fading || Mathf.Abs(slot.DesiredPitch - slot.AppliedPitch) < 0.001f) return;
            if (slot.Instance.setPitch(slot.DesiredPitch) == RESULT.OK) slot.AppliedPitch = slot.DesiredPitch;
        }

        internal void SetPaused(Milestone2ActionCue cue, bool paused)
        {
            Slot slot = slots[(int)cue];
            if (!slot.Active || slot.Fading || slot.Paused == paused) return;
            if (slot.Instance.setPaused(paused) == RESULT.OK) slot.Paused = paused;
        }

        internal void KeyOff(Milestone2ActionCue cue)
        {
            Slot slot = slots[(int)cue];
            if (!slot.Active || slot.Fading || slot.KeyedOff) return;
            SetPaused(cue, false);
            // Disable the conditional loop, then enter the exact supplied tail
            // immediately, so locking/recovery does not wait for another cycle.
            int tail = cue == Milestone2ActionCue.Slide ? 2480 : 800;
            string hold = cue == Milestone2ActionCue.Slide ? "SlideHold"
                : cue == Milestone2ActionCue.ArmDeploy ? "DeployHold" : "RetractHold";
            RESULT result = slot.Instance.setParameterByName(hold, 0f);
            if (result == RESULT.OK) result = slot.Instance.setTimelinePosition(tail);
            if (result == RESULT.OK) slot.KeyedOff = true;
            else
            {
                Release(slot);
                slot.Failed = true;
                UnityEngine.Debug.LogWarning("[Milestone 2 Audio] Cannot release "
                    + Milestone2ActionCatalog.EventPath((int)cue) + ": " + result);
            }
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
            if (immediately) Release(slot);
            else FadeStop(cue, cue == Milestone2ActionCue.Drive || cue == Milestone2ActionCue.DriveAcceleration
                ? settings.EngineFadeOutSeconds : IsMovementLoop(cue) ? settings.GroundFadeOutSeconds : settings.FadeOutSeconds);
        }

        internal void FadeStop(Milestone2ActionCue cue, float duration)
        {
            Slot slot = slots[(int)cue];
            if (!slot.Active) return;
            if (duration <= 0f) Release(slot);
            else if (!slot.Fading)
            {
                slot.Fading = true;
                slot.Recovering = false;
                slot.FadeElapsed = 0f;
                slot.FadeDuration = duration;
                slot.FadeStartEnvelope = slot.Envelope;
            }
        }

        private static bool IsMovementLoop(Milestone2ActionCue cue) => cue == Milestone2ActionCue.Drive
            || cue == Milestone2ActionCue.GroundMove || cue == Milestone2ActionCue.GroundTurn;

        internal void Tick(float deltaTime)
        {
            foreach (Slot slot in slots)
            {
                if (!slot.Active) continue;
                if (!slot.Instance.isValid()) { Release(slot); continue; }
                if (slot.Fading)
                {
                    slot.FadeElapsed += deltaTime;
                    if (slot.FadeElapsed >= slot.FadeDuration) { Release(slot); continue; }
                    slot.Envelope = slot.FadeStartEnvelope * (1f - slot.FadeElapsed / slot.FadeDuration);
                    slot.Instance.setVolume(slot.Gain * slot.Envelope);
                }
                else if (slot.Recovering)
                {
                    slot.RecoveryElapsed += deltaTime;
                    float progress = Mathf.Clamp01(slot.RecoveryElapsed / slot.RecoveryDuration);
                    slot.Envelope = Mathf.Lerp(slot.RecoveryStartEnvelope, 1f, progress);
                    slot.Instance.setVolume(slot.Gain * slot.Envelope);
                    if (progress >= 1f) slot.Recovering = false;
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
            slot.Recovering = false;
            slot.Paused = slot.KeyedOff = false;
        }
    }
}
