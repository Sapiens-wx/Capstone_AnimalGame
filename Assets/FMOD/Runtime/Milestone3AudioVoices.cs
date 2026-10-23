using System;
using AnimalGame.MainUI;
using FMOD;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace Capstone.Audio
{
    // Fixed cue slots and one shared impact voice bound concurrent instances.
    // No source-file IO, sample-loading barrier, or per-frame allocation.
    internal sealed class Milestone3AudioVoices
    {
        private struct Parameter
        {
            internal PARAMETER_ID Id;
            internal int Resolved;
            internal bool Desired, Applied;
            internal float Value, LastValue;
        }
        private sealed class Slot
        {
            internal Milestone3Cue Cue;
            internal EventInstance Instance;
            internal EventDescription Description;
            internal SoundHandle UIHandle;
            internal readonly Parameter[] Parameters = new Parameter[(int)Milestone3Parameter.Count];
            internal bool Active, Failed, Fading, Paused, RecycleHolding;
            internal PARAMETER_ID RecycleHoldId;
            internal float Gain, Envelope = 1f, LastVolume = -1f, PollTime;
        }
        private readonly Milestone3AudioSettings settings;
        private readonly Slot[] slots = new Slot[Milestone3AudioCatalog.CueCount];
        private readonly Milestone3Cue[] impacts =
            { Milestone3Cue.TumbleImpact, Milestone3Cue.TumbleImpact03 };
        private readonly System.Random random = new System.Random();
        private int previousImpact = -1;

        internal Milestone3AudioVoices(Milestone3AudioSettings settings)
        {
            this.settings = settings;
            for (int i = 0; i < slots.Length; i++) slots[i] = new Slot { Cue = (Milestone3Cue)i };
        }

        internal bool Play(Milestone3Cue cue, float intensity = 1f)
        {
            if (cue == Milestone3Cue.TumbleImpact)
            {
                int eligible = 0;
                for (int i = 0; i < impacts.Length; i++)
                    if (settings.HasDirectCue(impacts[i]) && !slots[(int)impacts[i]].Failed) eligible++;
                if (eligible == 0) return false;
                int choices = eligible;
                bool avoidPrevious = eligible > 1 && previousImpact >= 0
                    && settings.HasDirectCue(impacts[previousImpact]) && !slots[(int)impacts[previousImpact]].Failed;
                if (avoidPrevious) choices--;
                int choice = random.Next(choices), selected = 0;
                for (int i = 0; i < impacts.Length; i++)
                {
                    if (!settings.HasDirectCue(impacts[i]) || slots[(int)impacts[i]].Failed
                        || (avoidPrevious && i == previousImpact)) continue;
                    if (choice-- == 0) { selected = i; break; }
                }
                previousImpact = selected;
                for (int i = 0; i < impacts.Length; i++) Release(slots[(int)impacts[i]]);
                if (selected != 0)
                    for (int i = 0; i < (int)Milestone3Parameter.Count; i++)
                    {
                        Parameter value = slots[(int)Milestone3Cue.TumbleImpact].Parameters[i];
                        slots[(int)impacts[selected]].Parameters[i].Desired = value.Desired;
                        slots[(int)impacts[selected]].Parameters[i].Value = value.Value;
                    }
                cue = impacts[selected];
            }
            return Start(cue, intensity);
        }

        // Both sizes own one intake instance. Pauses preserve the suction phase;
        // only an observed feed-to-processing transition releases the close tail.
        internal bool BeginRecycleFeed() => Start(Milestone3Cue.RecycleMediumStep, 1f, true);

        internal void SetRecycleFeedPaused(bool paused)
        {
            Slot slot = slots[(int)Milestone3Cue.RecycleMediumStep];
            if (!slot.RecycleHolding) return;
            SetPaused(Milestone3Cue.RecycleMediumStep, paused);
        }

        // Pause the existing source cursor when observed gameplay progress stalls.
        internal void SetPaused(Milestone3Cue cue, bool paused)
        {
            Slot slot = slots[(int)cue];
            if (!slot.Active || slot.Paused == paused) return;
            try
            {
                Check(slot.Instance.setPaused(paused));
                slot.Paused = paused;
                if (paused)
                {
                    SoundUIPlayback.EndSound(slot.UIHandle);
                    slot.UIHandle = default;
                }
                else
                {
                    AudioCueSettings config = settings.GetCue(slot.Cue);
                    if (config.ReadBySoundUI)
                        slot.UIHandle = SoundUIPlayback.BeginSound(config.SoundUIStrength);
                }
            }
            catch (Exception exception) { Fail(slot, exception.Message); }
        }

        internal bool CloseRecycleFeed()
        {
            Slot slot = slots[(int)Milestone3Cue.RecycleMediumStep];
            if (!slot.Active || !slot.RecycleHolding) return false;
            try
            {
                Check(slot.Instance.setParameterByID(slot.RecycleHoldId, 0f));
                Check(slot.Instance.setTimelinePosition(Milestone3AudioCatalog.RecycleClosePositionMs));
                SetRecycleFeedPaused(false);
                if (!slot.Active) return false;
                slot.RecycleHolding = false;
                return true;
            }
            catch (Exception exception) { Fail(slot, exception.Message); return false; }
        }

        // Select a source entry point only when a new slide voice is created.
        // Continuous motion and short fade recoveries keep the original cursor.
        internal void SetGarbageSlide(bool active, bool steep, float intensity = 1f)
        {
            if (!active) { Stop(Milestone3Cue.GarbageSlide); return; }
            if (!slots[(int)Milestone3Cue.GarbageSlide].Active)
                Start(Milestone3Cue.GarbageSlide, intensity, startPositionMs: steep ? 0 : 1000);
            else SetLoop(Milestone3Cue.GarbageSlide, true, intensity);
        }

        internal bool PlayGarbageImpact(float speed)
        {
            if (float.IsNaN(speed) || float.IsInfinity(speed) || speed <= settings.WorldMotionThreshold) return false;
            float relativeSpeed = speed / settings.GarbageImpactReferenceSpeed;
            float gain = Mathf.Clamp(relativeSpeed, .08f, settings.GarbageImpactMaximumGain);
            float pitch = 1f + settings.GarbageImpactPitchVariation * (Mathf.Clamp(relativeSpeed, 0f, 2f) - 1f);
            SetParameter(Milestone3Cue.GarbageImpact, Milestone3Parameter.Impact, relativeSpeed);
            return Start(Milestone3Cue.GarbageImpact, gain, pitch: pitch,
                maximumIntensity: settings.GarbageImpactMaximumGain);
        }

        private bool Start(Milestone3Cue cue, float intensity, bool recycleFeed = false,
            int startPositionMs = 0, float pitch = 1f, float maximumIntensity = 1f)
        {
            Slot slot = slots[(int)cue];
            AudioCueSettings config = settings.GetCue(cue);
            if (slot.Failed || config == null || config.EventReference.Guid.IsNull) return false;
            Release(slot);
            try
            {
                slot.Instance = RuntimeManager.CreateInstance(config.EventReference);
                slot.Gain = config.LinearGain * Mathf.Clamp(intensity, 0f, maximumIntensity);
                slot.Envelope = 1f;
                Check(slot.Instance.setVolume(slot.Gain));
                slot.LastVolume = slot.Gain;
                if (pitch != 1f) Check(slot.Instance.setPitch(pitch));
                Check(slot.Instance.getDescription(out slot.Description));
                if (recycleFeed)
                {
                    Check(slot.Description.getParameterDescriptionByName(
                        Milestone3AudioCatalog.RecycleHoldParameter, out PARAMETER_DESCRIPTION hold));
                    slot.RecycleHoldId = hold.id;
                    Check(slot.Instance.setParameterByID(slot.RecycleHoldId, 1f));
                    slot.RecycleHolding = true;
                }
                slot.Active = true;
                for (int p = 0; p < slot.Parameters.Length; p++)
                {
                    slot.Parameters[p].Resolved = 0;
                    slot.Parameters[p].Applied = false;
                    ApplyParameter(cue, (Milestone3Parameter)p);
                    if (!slot.Active) return false;
                }
                if (startPositionMs > 0) Check(slot.Instance.setTimelinePosition(startPositionMs));
                Check(slot.Instance.start());
                if (config.ReadBySoundUI)
                    slot.UIHandle = SoundUIPlayback.BeginSound(config.SoundUIStrength * Mathf.Clamp01(intensity));
                return true;
            }
            catch (Exception exception)
            {
                Fail(slot, exception.Message);
                return false;
            }
        }

        internal void SetLoop(Milestone3Cue cue, bool active, float intensity = 1f)
        {
            Slot slot = slots[(int)cue];
            if (!active) { Stop(cue); return; }
            if (!slot.Active) { Start(cue, intensity); return; }
            slot.Fading = false;
            slot.Gain = settings.GetCue(cue).LinearGain * Mathf.Clamp01(intensity);
            ApplyVolume(slot);
        }

        internal void SetParameter(Milestone3Cue cue, Milestone3Parameter parameter, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            Slot slot = slots[(int)cue];
            slot.Parameters[(int)parameter].Desired = true;
            slot.Parameters[(int)parameter].Value = Mathf.Clamp01(value);
            ApplyParameter(cue, parameter);
        }

        private void ApplyParameter(Milestone3Cue cue, Milestone3Parameter parameter)
        {
            Slot slot = slots[(int)cue];
            ref Parameter value = ref slot.Parameters[(int)parameter];
            if (!slot.Active || !value.Desired) return;
            if (value.Resolved == 0)
            {
                RESULT result = slot.Description.getParameterDescriptionByName(
                    Milestone3AudioCatalog.ParameterName(cue, parameter), out PARAMETER_DESCRIPTION description);
                value.Resolved = result == RESULT.OK ? 1 : -1;
                value.Id = description.id;
            }
            if (value.Resolved < 0 || (value.Applied && Mathf.Abs(value.LastValue - value.Value) < settings.ParameterEpsilon)) return;
            RESULT applied = slot.Instance.setParameterByID(value.Id, value.Value);
            if (applied == RESULT.OK)
            { value.Applied = true; value.LastValue = value.Value; }
            else Fail(slot, applied.ToString());
        }

        internal void Stop(Milestone3Cue cue, bool immediate = false)
        {
            if (cue == Milestone3Cue.TumbleImpact)
            {
                StopSlot(slots[(int)Milestone3Cue.TumbleImpact02], immediate);
                StopSlot(slots[(int)Milestone3Cue.TumbleImpact03], immediate);
            }
            StopSlot(slots[(int)cue], immediate);
        }
        private void StopSlot(Slot slot, bool immediate)
        {
            if (!slot.Active) return;
            if (immediate || slot.Paused || settings.FadeOutSeconds <= 0f) Release(slot);
            else slot.Fading = true;
        }
        internal void Tick(float deltaTime)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                Slot slot = slots[i];
                if (!slot.Active || slot.Paused) continue;
                if (slot.Fading || slot.Envelope < 1f)
                {
                    slot.Envelope = Mathf.MoveTowards(slot.Envelope, slot.Fading ? 0f : 1f,
                        deltaTime / Mathf.Max(0.001f, settings.FadeOutSeconds));
                    ApplyVolume(slot);
                    if (!slot.Active) continue;
                    if (slot.Envelope <= 0f) { Release(slot); continue; }
                }
                slot.PollTime += deltaTime;
                if (slot.PollTime < 0.1f) continue;
                slot.PollTime = 0f;
                RESULT polled = slot.Instance.getPlaybackState(out PLAYBACK_STATE state);
                if (polled != RESULT.OK) Fail(slot, polled.ToString());
                else if (state == PLAYBACK_STATE.STOPPED) Release(slot);
            }
        }
        private static void ApplyVolume(Slot slot)
        {
            float volume = slot.Gain * slot.Envelope;
            if (Mathf.Abs(volume - slot.LastVolume) < 0.0001f) return;
            RESULT applied = slot.Instance.setVolume(volume);
            if (applied == RESULT.OK) slot.LastVolume = volume;
            else Fail(slot, applied.ToString());
        }
        internal void StopAllImmediately()
        {
            foreach (Slot slot in slots)
            {
                Release(slot);
                for (int i = 0; i < slot.Parameters.Length; i++) slot.Parameters[i] = default;
            }
        }
        private static void Release(Slot slot)
        {
            SoundUIPlayback.EndSound(slot.UIHandle);
            slot.UIHandle = default;
            if (slot.Instance.isValid())
            {
                slot.Instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                slot.Instance.release();
                slot.Instance.clearHandle();
            }
            slot.Active = slot.Fading = slot.Paused = slot.RecycleHolding = false;
            slot.PollTime = 0f;
            slot.Envelope = 1f;
            slot.LastVolume = -1f;
        }
        private static void Check(RESULT result)
        {
            if (result != RESULT.OK) throw new InvalidOperationException(result.ToString());
        }
        private static void Fail(Slot slot, string message)
        {
            Release(slot);
            if (slot.Failed) return;
            slot.Failed = true;
            UnityEngine.Debug.LogWarning("[Milestone 3 Audio] "
                + Milestone3AudioCatalog.EventPath((int)slot.Cue) + ": " + message);
        }
    }
}
