using AnimalGame.Garbage;
using AnimalGame.RobotArm;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEngine;

namespace Capstone.Audio
{
    // Observe the existing recycling presentation; do not run a second gameplay clock.
    // Processing proves ingestion finished, not that the later recycle callback succeeded.
    internal sealed class Milestone3GarbageAudio
    {
        private readonly Milestone3AudioVoices voices;
        private RobotMover mover;
        private RobotArmController arm;
        private WorldInteraction inspectedItem, recyclingItem;
        private bool inspectedItemIsGarbage, hasBaseline, wasRecycling;
        private bool feedStarted, feedPaused, feedEnded, closeTailAllowed;
        private bool crushRequested;
        private int observedStages, activeStage = -1;

        internal RobotMover Source => mover;
        internal RobotArmController ArmSource => arm;

        internal Milestone3GarbageAudio(Milestone3AudioSettings settings, Milestone3AudioVoices voices)
        {
            this.voices = voices;
        }

        internal void Bind(RobotMover source, RobotArmController armSource)
        {
            if (mover == source && arm == armSource) return;
            StopAllImmediately();
            mover = source;
            arm = armSource;
        }

        internal void Tick(float deltaTime)
        {
            if (deltaTime <= 0f || !Usable(mover) || !Usable(arm)
                || mover.gameObject != arm.gameObject)
            {
                StopAllImmediately();
                return;
            }

            WorldInteraction held = arm.HeldObject;
            if (held != inspectedItem)
            {
                inspectedItem = held;
                inspectedItemIsGarbage = held != null && held.TryGetComponent<GarbageItem>(out _);
            }
            bool recycling = arm.State == RobotArmState.Recycling && held != null
                && inspectedItemIsGarbage && held.Available && held.Recyclable && held.Owner == arm
                && held.gameObject.scene == arm.gameObject.scene
                && held.Size != RecyclableSize.Big;
            MediumRecycleFrame frame = arm.CurrentMediumRecycleFrame;

            if (!hasBaseline)
            {
                // Bind, resume, and component re-enable establish state without replaying an onset.
                hasBaseline = true;
                wasRecycling = recycling;
                recyclingItem = recycling ? held : null;
                observedStages = recycling ? StagesAlreadyObserved(frame) : 0;
                feedEnded = recycling && IsPostFeed(frame.Phase);
                return;
            }

            if (!recycling)
            {
                StopCrush(true);
                // A close already justified by Processing may finish naturally after ClearHeld.
                // Object disappearance itself must never start a close or complete sound.
                if (!closeTailAllowed) AbortFeed();
                wasRecycling = false;
                recyclingItem = null;
                observedStages = 0;
                feedStarted = feedPaused = feedEnded = false;
                activeStage = -1;
                return;
            }

            bool began = !wasRecycling || held != recyclingItem;
            if (began)
            {
                AbortFeed();
                observedStages = 0;
                feedEnded = false;
                recyclingItem = held;
            }
            wasRecycling = true;
            UpdateCrush(began, held.Size, frame);

            if (IsPostFeed(frame.Phase))
            {
                if (!feedEnded)
                {
                    // Only a witnessed feed -> Processing transition can release the suction
                    // hold and seek to the closing tail. Never synthesize a missed onset.
                    if (!began && feedStarted && frame.Phase == MediumRecyclePhase.Processing)
                    {
                        closeTailAllowed = voices.CloseRecycleFeed();
                        if (!closeTailAllowed) AbortFeed(stopCrush: false);
                    }
                    else AbortFeed(stopCrush: false);
                    feedEnded = true;
                    feedStarted = feedPaused = false;
                    activeStage = -1;
                }
                return;
            }
            if (feedEnded) return;

            if (held.Size == RecyclableSize.Small)
            {
                if (began) feedStarted = voices.BeginRecycleFeed();
                return;
            }

            // Current public phases already include the real load, push and lock durations.
            // A missed phase is intentionally not replayed late as a burst of old impacts.
            if (frame.Phase != MediumRecyclePhase.Push || frame.Stage < 0 || frame.Stage > 2)
            {
                PauseFeed();
                activeStage = -1;
                observedStages |= StagesAlreadyObserved(frame);
                return;
            }

            int stageBit = 1 << frame.Stage;
            if ((observedStages & stageBit) != 0)
            {
                if (activeStage == frame.Stage && feedStarted && !feedPaused)
                    UpdateMediumParameters(frame);
                else PauseFeed();
                return;
            }

            observedStages |= (stageBit << 1) - 1;
            activeStage = frame.Stage;
            // All three pushes resume one suction instance; their gaps pause its timeline.
            UpdateMediumParameters(frame);
            if (!feedStarted) feedStarted = voices.BeginRecycleFeed();
            else if (feedPaused) voices.SetRecycleFeedPaused(false);
            feedPaused = false;
        }

        private void UpdateMediumParameters(MediumRecycleFrame frame)
        {
            voices.SetParameter(Milestone3Cue.RecycleMediumStep, Milestone3Parameter.Progress, frame.Progress);
            voices.SetParameter(Milestone3Cue.RecycleMediumStep, Milestone3Parameter.Effort, frame.Effort01);
        }

        private void UpdateCrush(bool began, RecyclableSize size, MediumRecycleFrame frame)
        {
            // The last medium push has already swallowed the item before its final
            // lock/processing wait. Small waste exposes its end through Processing.
            bool swallowed = IsPostFeed(frame.Phase)
                || (size == RecyclableSize.Medium && frame.Stage == 2
                    && frame.Phase == MediumRecyclePhase.Lock);
            if (swallowed)
            {
                if (crushRequested) StopCrush(false);
                return;
            }
            // Play the progressive compaction once, including alignment and inter-stroke gaps.
            // The short event uses ordered windows of the same source at original pitch,
            // so a small item's brief feed still reaches the final compacted texture.
            // Missing delivery/binding is a no-op; never retry or replay mid-feed.
            if (!began) return;
            crushRequested = voices.Play(size == RecyclableSize.Small
                ? Milestone3Cue.RecycleCrushSmall : Milestone3Cue.RecycleCrush);
        }

        private void StopCrush(bool immediate)
        {
            voices.Stop(Milestone3Cue.RecycleCrush, immediate);
            voices.Stop(Milestone3Cue.RecycleCrushSmall, immediate);
            crushRequested = false;
        }

        private static bool IsPostFeed(MediumRecyclePhase phase) =>
            phase == MediumRecyclePhase.Processing || phase == MediumRecyclePhase.Finish
            || phase == MediumRecyclePhase.Complete;

        private static int StagesAlreadyObserved(MediumRecycleFrame frame)
        {
            if (IsPostFeed(frame.Phase)) return 7;
            if (frame.Phase == MediumRecyclePhase.Clamp || frame.Stage < 0) return 0;
            if (frame.Stage > 2) return 7;
            int stageBit = 1 << frame.Stage;
            return frame.Phase == MediumRecyclePhase.Push || frame.Phase == MediumRecyclePhase.Lock
                ? (stageBit << 1) - 1 : stageBit - 1;
        }

        private static bool Usable(Behaviour source) => source != null && source.isActiveAndEnabled
            && source.gameObject.scene.IsValid() && source.gameObject.scene.isLoaded;

        private void PauseFeed()
        {
            if (!feedStarted || feedPaused) return;
            voices.SetRecycleFeedPaused(true);
            feedPaused = true;
        }

        private void AbortFeed(bool stopCrush = true)
        {
            if (stopCrush) StopCrush(true);
            // Keep legacy small-slot cleanup while both sizes now share the intake slot.
            voices.Stop(Milestone3Cue.RecycleSmall, true);
            voices.Stop(Milestone3Cue.RecycleMediumStep, true);
            feedStarted = feedPaused = closeTailAllowed = false;
            activeStage = -1;
        }

        internal void StopAllImmediately()
        {
            AbortFeed();
            feedEnded = false;
            hasBaseline = wasRecycling = inspectedItemIsGarbage = false;
            inspectedItem = recyclingItem = null;
            observedStages = 0;
        }
    }
}
