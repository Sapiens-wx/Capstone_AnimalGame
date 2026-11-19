using System;
using AnimalGame.MainUI;
using FMOD;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace Capstone.Audio
{
    // Fixed cue slots, a shared impact voice, and a bounded scan-tail pool.
    // No source-file IO, sample-loading barrier, or per-frame allocation.
    internal sealed class Milestone3AudioVoices
    {
        private enum WaterPhase { Accelerating, Sustaining, Decelerating }

        // Source positions in the WaterMove event authored by BuildMilestone3.js.
        private const int WaterAttackEndMs = 2100;
        private const int WaterTailStartMs = 30000;
        private const int WaterTailEndMs = 32000;
        private const float WaterCrossfadeSeconds = .06f;
        private const float WaterFlowStartThreshold = .04f;
        private const float WaterFlowSettleThreshold = .02f;

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
            internal float AppliedPitch = 1f, FadeInSeconds, FadeOutRate;
            internal int LengthMilliseconds;
        }
        private readonly Milestone3AudioSettings settings;
        // At -3 semitones the 1.673 s source lasts about 1.99 s. Include
        // the 0.1 s release poll and event-before-poll frame boundary.
        private const int ScanHitVoiceCount = 15;
        private readonly Slot[] slots;
        private readonly Slot waterSecond;
        private Slot waterCurrent;
        private WaterPhase waterPhase;
        private float waterFlow;
        private int waterStartPosition;
        private readonly Milestone3Cue[] impacts =
            { Milestone3Cue.TumbleImpact, Milestone3Cue.TumbleImpact03 };
        private readonly System.Random random = new System.Random();
        private int previousImpact = -1;

        internal Milestone3AudioVoices(Milestone3AudioSettings settings)
        {
            this.settings = settings;
            int extraScanVoices = settings.HasDirectCue(Milestone3Cue.ScanHit) ? ScanHitVoiceCount - 1 : 0;
            slots = new Slot[Milestone3AudioCatalog.CueCount + extraScanVoices];
            for (int i = 0; i < slots.Length; i++) slots[i] = new Slot
            {
                Cue = i < Milestone3AudioCatalog.CueCount ? (Milestone3Cue)i : Milestone3Cue.ScanHit
            };
            if (settings.HasDirectCue(Milestone3Cue.WaterMove))
                waterSecond = new Slot { Cue = Milestone3Cue.WaterMove };
        }

        internal bool PlayScanHit(float pitch)
        {
            // FMOD's final combined pitch is limited to 100x. Preserve every
            // two-semitone step up to that native limit, including values >4x.
            pitch = Mathf.Clamp(pitch, 0.01f, 100f);
            Slot first = slots[(int)Milestone3Cue.ScanHit];
            if (!first.Active) return Start(first, 1f, pitch: pitch);
            for (int i = Milestone3AudioCatalog.CueCount; i < slots.Length; i++)
                if (!slots[i].Active) return Start(slots[i], 1f, pitch: pitch);
            return false;
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

        internal void SetWaterMove(float targetFlow, float pitch, float deltaTime)
        {
            if (waterSecond == null || !(deltaTime > 0f) || float.IsNaN(targetFlow)) return;
            targetFlow = Mathf.Clamp01(targetFlow);
            pitch = Mathf.Clamp(pitch, settings.WaterMinimumPitch, 1f);
            if (waterCurrent == null || !waterCurrent.Active)
            {
                waterCurrent = null;
                waterFlow = 0f;
                if (targetFlow > 0f) BeginWaterPhase(WaterPhase.Accelerating, targetFlow, pitch);
                return;
            }

            // Advance the audible phase at the same rate as its source segment.
            float sourceStepMs = deltaTime * waterCurrent.AppliedPitch * 1000f;
            if (waterPhase == WaterPhase.Accelerating)
                waterFlow = Mathf.Min(1f, waterFlow + sourceStepMs / WaterAttackEndMs);
            else if (waterPhase == WaterPhase.Decelerating)
                waterFlow = Mathf.Max(0f, waterFlow - sourceStepMs / (WaterTailEndMs - WaterTailStartMs));

            float change = targetFlow - waterFlow;
            float threshold = waterPhase == WaterPhase.Sustaining
                ? WaterFlowStartThreshold : WaterFlowSettleThreshold;
            WaterPhase desired = targetFlow <= 0f || change < -threshold ? WaterPhase.Decelerating
                : change > threshold ? WaterPhase.Accelerating : WaterPhase.Sustaining;
            if (desired != waterPhase)
            {
                // The attack already leads into the full-strength steady source.
                if (desired == WaterPhase.Sustaining && waterPhase == WaterPhase.Accelerating
                    && waterFlow >= 1f - WaterFlowSettleThreshold && targetFlow >= 1f - WaterFlowSettleThreshold)
                    waterPhase = WaterPhase.Sustaining;
                else BeginWaterPhase(desired, targetFlow, pitch);
            }

            Slot current = waterCurrent;
            if (current == null || !current.Active) return;
            if (waterPhase == WaterPhase.Sustaining && desired == WaterPhase.Sustaining)
            {
                float sectionMs = change >= 0f ? WaterAttackEndMs : WaterTailEndMs - WaterTailStartMs;
                waterFlow = Mathf.MoveTowards(waterFlow, targetFlow, sourceStepMs / sectionMs);
                current.Gain = settings.GetCue(Milestone3Cue.WaterMove).LinearGain * waterFlow;
                ApplyVolume(current);
            }
            StoreWaterSpeed(current, targetFlow);
            ApplyParameter(current, Milestone3Parameter.Speed);

            // A normal stop keeps the rate at the instant of exit for the natural tail.
            if (targetFlow <= 0f || !current.Active || pitch == current.AppliedPitch
                || (pitch != 1f && Mathf.Abs(pitch - current.AppliedPitch) < 0.001f)) return;
            RESULT applied = current.Instance.setPitch(pitch);
            if (applied == RESULT.OK) current.AppliedPitch = pitch;
            else Fail(current, applied.ToString());
        }

        private void BeginWaterPhase(WaterPhase phase, float targetFlow, float pitch)
        {
            Slot first = slots[(int)Milestone3Cue.WaterMove];
            Slot previous = waterCurrent;
            if (previous != null && waterPhase == WaterPhase.Decelerating && targetFlow > 0f)
            {
                RESULT state = previous.Instance.getPlaybackState(out PLAYBACK_STATE playback);
                if (state != RESULT.OK)
                {
                    Fail(previous, state.ToString());
                    StopWaterImmediately();
                    return;
                }
                if (playback == PLAYBACK_STATE.STOPPED)
                {
                    Release(previous);
                    previous = waterCurrent = null;
                    waterFlow = 0f;
                    phase = WaterPhase.Accelerating;
                }
            }
            Slot next = ReferenceEquals(previous, first) || (previous == null && first.Active)
                ? waterSecond : first;
            // Wait out the short overlap rather than cut a fading voice during rapid reversals.
            if (next.Active) return;

            bool switching = previous != null && previous.Active;
            // Read only on a handoff. The mixer cursor is a better match for the
            // audible amplitude than the frame clock, which can drift during seeks.
            if (switching && waterPhase != WaterPhase.Sustaining
                && previous.Instance.getTimelinePosition(out int cursor) == RESULT.OK && cursor >= waterStartPosition)
            {
                if (waterPhase == WaterPhase.Accelerating && cursor < WaterTailStartMs)
                    waterFlow = Mathf.Clamp01(cursor / (float)WaterAttackEndMs);
                else if (waterPhase == WaterPhase.Decelerating && cursor >= WaterTailStartMs)
                    waterFlow = Mathf.Clamp01((WaterTailEndMs - cursor) / (float)(WaterTailEndMs - WaterTailStartMs));
            }

            int position;
            float intensity = 1f;
            switch (phase)
            {
                case WaterPhase.Accelerating:
                    position = (int)(WaterAttackEndMs * waterFlow);
                    break;
                case WaterPhase.Sustaining:
                    position = WaterAttackEndMs;
                    // Blend to the requested steady level once close. Small mixer
                    // cursor corrections must not immediately start another ramp.
                    waterFlow = intensity = targetFlow;
                    break;
                default:
                    position = (int)(WaterTailEndMs - (WaterTailEndMs - WaterTailStartMs) * waterFlow);
                    break;
            }
            if (targetFlow <= 0f && switching) pitch = previous.AppliedPitch;
            StoreWaterSpeed(next, targetFlow);
            if (!Start(next, intensity, startPositionMs: position, pitch: pitch,
                    fadeInSeconds: switching ? WaterCrossfadeSeconds : settings.TerrainFadeInSeconds,
                    reportSoundUI: !switching))
            {
                StopWaterImmediately();
                return;
            }
            if (switching)
            {
                next.UIHandle = previous.UIHandle;
                previous.UIHandle = default;
                previous.Fading = true;
                previous.FadeOutRate = previous.Envelope / WaterCrossfadeSeconds;
            }
            waterCurrent = next;
            waterPhase = phase;
            waterStartPosition = position;
        }

        private static void StoreWaterSpeed(Slot slot, float flow)
        {
            ref Parameter speed = ref slot.Parameters[(int)Milestone3Parameter.Speed];
            speed.Desired = true;
            speed.Value = flow;
        }

        // Each new contact starts at the source beginning (bush) or one random
        // point in the full authored loop (reeds), including quick re-entries.
        internal void SetTerrainContact(Milestone3Cue cue, bool active)
        {
            Slot slot = slots[(int)cue];
            if (!active) { Stop(cue); return; }
            if (!slot.Active || slot.Fading)
                Start(slot, 1f, randomLoopStart: cue == Milestone3Cue.ReedRustle,
                    fadeInSeconds: settings.TerrainFadeInSeconds);
        }

        private bool Start(Milestone3Cue cue, float intensity, bool recycleFeed = false,
            int startPositionMs = 0, float pitch = 1f, float maximumIntensity = 1f)
            => Start(slots[(int)cue], intensity, recycleFeed, startPositionMs, pitch, maximumIntensity);

        private bool Start(Slot slot, float intensity, bool recycleFeed = false,
            int startPositionMs = 0, float pitch = 1f, float maximumIntensity = 1f,
            bool randomLoopStart = false, float fadeInSeconds = 0f, bool reportSoundUI = true)
        {
            Milestone3Cue cue = slot.Cue;
            AudioCueSettings config = settings.GetCue(cue);
            if (slot.Failed || config == null || config.EventReference.Guid.IsNull) return false;
            Release(slot);
            try
            {
                slot.Instance = RuntimeManager.CreateInstance(config.EventReference);
                slot.Gain = config.LinearGain * Mathf.Clamp(intensity, 0f, maximumIntensity);
                slot.FadeInSeconds = fadeInSeconds;
                slot.Envelope = fadeInSeconds > 0f ? 0f : 1f;
                Check(slot.Instance.setVolume(slot.Gain * slot.Envelope));
                slot.LastVolume = slot.Gain * slot.Envelope;
                if (pitch != 1f) Check(slot.Instance.setPitch(pitch));
                slot.AppliedPitch = pitch;
                Check(slot.Instance.getDescription(out slot.Description));
                if (randomLoopStart)
                {
                    if (slot.LengthMilliseconds == 0)
                        Check(slot.Description.getLength(out slot.LengthMilliseconds));
                    startPositionMs = random.Next(Math.Max(1,
                        slot.LengthMilliseconds - settings.ReedLoopCrossfadeMilliseconds));
                }
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
                    ApplyParameter(slot, (Milestone3Parameter)p);
                    if (!slot.Active) return false;
                }
                if (startPositionMs > 0) Check(slot.Instance.setTimelinePosition(startPositionMs));
                Check(slot.Instance.start());
                if (reportSoundUI && config.ReadBySoundUI)
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
            => ApplyParameter(slots[(int)cue], parameter);

        private void ApplyParameter(Slot slot, Milestone3Parameter parameter)
        {
            ref Parameter value = ref slot.Parameters[(int)parameter];
            if (!slot.Active || !value.Desired) return;
            if (value.Resolved == 0)
            {
                RESULT result = slot.Description.getParameterDescriptionByName(
                    Milestone3AudioCatalog.ParameterName(slot.Cue, parameter), out PARAMETER_DESCRIPTION description);
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
            if (cue == Milestone3Cue.WaterMove)
            {
                StopWaterImmediately();
                return;
            }
            if (cue == Milestone3Cue.ScanHit)
                for (int i = Milestone3AudioCatalog.CueCount; i < slots.Length; i++) StopSlot(slots[i], immediate);
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
            else if (!slot.Fading)
            {
                slot.FadeOutRate = 1f / Mathf.Max(0.001f, settings.FadeOutSeconds);
                slot.Fading = true;
            }
        }
        internal void Tick(float deltaTime)
        {
            for (int i = 0; i < slots.Length; i++) TickSlot(slots[i], deltaTime);
            if (waterSecond != null) TickSlot(waterSecond, deltaTime);
        }

        private void TickSlot(Slot slot, float deltaTime)
        {
            if (!slot.Active || slot.Paused) return;
            if (slot.Fading || slot.Envelope < 1f)
            {
                float step = slot.Fading ? deltaTime * slot.FadeOutRate
                    : deltaTime / Mathf.Max(0.001f, slot.FadeInSeconds > 0f
                        ? slot.FadeInSeconds : settings.FadeOutSeconds);
                slot.Envelope = Mathf.MoveTowards(slot.Envelope, slot.Fading ? 0f : 1f, step);
                ApplyVolume(slot);
                if (!slot.Active) return;
                if (slot.Envelope <= 0f) { Release(slot); return; }
            }
            slot.PollTime += deltaTime;
            if (slot.PollTime < 0.1f) return;
            slot.PollTime = 0f;
            RESULT polled = slot.Instance.getPlaybackState(out PLAYBACK_STATE state);
            if (polled != RESULT.OK) Fail(slot, polled.ToString());
            else if (state == PLAYBACK_STATE.STOPPED) Release(slot);
        }

        private void StopWaterImmediately()
        {
            Slot first = slots[(int)Milestone3Cue.WaterMove];
            if (first.Active) Release(first);
            if (waterSecond != null && waterSecond.Active) Release(waterSecond);
            waterCurrent = null;
            waterFlow = 0f;
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
            StopWaterImmediately();
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
