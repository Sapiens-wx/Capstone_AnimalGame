using AnimalGame.RobotMap;
using UnityEngine;

namespace Capstone.Audio
{
    // Observes the authoritative tumble/recovery signals without changing gameplay.
    internal sealed class Milestone3BalanceAudio
    {
        private readonly Milestone3AudioSettings settings;
        private readonly Milestone3AudioVoices voices;
        private RobotMover source;
        private RobotTumbleController tumble;
        private RobotSelfRightingController righting;
        private Vector2 previousPosition;
        private float previousQuarterTurns, previousPullProgress;
        private RobotSelfRightingState previousRightingState;
        private bool hasTumbleBaseline, hasRightingBaseline;
        private int lastStepIndex = -1, lastStartedFrame = -1, lastLandingFrame = -1;

        internal RobotMover Source => source;
        internal RobotTumbleController TumbleSource => tumble;
        internal RobotSelfRightingController RightingSource => righting;

        internal Milestone3BalanceAudio(Milestone3AudioSettings settings, Milestone3AudioVoices voices)
        {
            this.settings = settings;
            this.voices = voices;
        }

        internal void Bind(RobotMover next, RobotTumbleController nextTumble,
            RobotSelfRightingController nextRighting)
        {
            if (ReferenceEquals(source, next) && ReferenceEquals(tumble, nextTumble)
                && ReferenceEquals(righting, nextRighting)) return;
            if (!ReferenceEquals(tumble, null))
            {
                tumble.Started -= OnStarted;
                tumble.StepCompleted -= OnStepCompleted;
                tumble.RockImpact -= OnRockImpact;
                tumble.Settled -= OnSettled;
            }
            if (!ReferenceEquals(righting, null))
            {
                righting.ForcePulse -= OnForcePulse;
                righting.SupportFailed -= OnSupportFailed;
                righting.RightingLanded -= OnRightingLanded;
            }
            StopAllImmediately();
            source = next;
            tumble = nextTumble;
            righting = nextRighting;
            if (tumble != null)
            {
                tumble.Started += OnStarted;
                tumble.StepCompleted += OnStepCompleted;
                tumble.RockImpact += OnRockImpact;
                tumble.Settled += OnSettled;
            }
            if (righting != null)
            {
                righting.ForcePulse += OnForcePulse;
                righting.SupportFailed += OnSupportFailed;
                righting.RightingLanded += OnRightingLanded;
            }
            EstablishBaselines();
        }

        internal void Tick(float deltaTime)
        {
            if (!CanObserveSource() || Time.timeScale <= 0f || deltaTime <= 0f)
            {
                StopAllImmediately();
                return;
            }
            ObserveTumble(deltaTime);
            ObserveRighting();
        }

        internal bool SuppressesOrdinaryLanding(RobotMover candidate)
        {
            if (!ReferenceEquals(source, candidate) || !CanObserveSource()
                || Time.timeScale <= 0f) return false;
            return lastLandingFrame == Time.frameCount;
        }

        internal void StopAllImmediately()
        {
            voices.Stop(Milestone3Cue.TumbleStart, true);
            voices.Stop(Milestone3Cue.TumbleMove, true);
            voices.Stop(Milestone3Cue.TumbleImpact, true);
            voices.Stop(Milestone3Cue.TumbleImpact02, true);
            voices.Stop(Milestone3Cue.TumbleImpact03, true);
            voices.Stop(Milestone3Cue.RockImpact, true);
            voices.Stop(Milestone3Cue.RecoveredUpright, true);
            voices.Stop(Milestone3Cue.SettledFallen, true);
            voices.Stop(Milestone3Cue.SupportStart, true);
            voices.Stop(Milestone3Cue.ForceAccepted, true);
            voices.Stop(Milestone3Cue.ForceRejected, true);
            voices.Stop(Milestone3Cue.SupportFailed, true);
            voices.Stop(Milestone3Cue.RightingLand, true);
            StopStrain();
            hasTumbleBaseline = hasRightingBaseline = false;
            lastStepIndex = lastStartedFrame = lastLandingFrame = -1;
        }

        private void ObserveTumble(float deltaTime)
        {
            if (!CanObserveTumble())
            {
                voices.Stop(Milestone3Cue.TumbleMove, true);
                hasTumbleBaseline = false;
                return;
            }
            Vector2 position = source.MapPosition;
            float quarterTurns = tumble.ContinuousQuarterTurnProgress;
            if (!hasTumbleBaseline)
            {
                previousPosition = position;
                previousQuarterTurns = quarterTurns;
                hasTumbleBaseline = true;
                return;
            }
            // Final rocking rotates the chassis while the root stays in place.
            // Both rates come from actual public presentation/motion progress.
            float speed = Mathf.Max((position - previousPosition).magnitude / deltaTime,
                Mathf.Abs(quarterTurns - previousQuarterTurns)
                    * Mathf.Max(0f, tumble.CurrentStepDistanceMeters) / deltaTime);
            bool moving = (tumble.State == RobotTumbleState.Tumbling
                    || tumble.State == RobotTumbleState.FinalRocking)
                && speed > settings.TumbleMotionThreshold;
            voices.SetParameter(Milestone3Cue.TumbleMove, Milestone3Parameter.Speed,
                Mathf.Clamp01(speed / settings.TumbleReferenceSpeed));
            voices.SetLoop(Milestone3Cue.TumbleMove, moving);
            previousPosition = position;
            previousQuarterTurns = quarterTurns;
        }

        private void ObserveRighting()
        {
            if (!CanObserveRighting())
            {
                StopStrain();
                hasRightingBaseline = false;
                return;
            }
            RobotSelfRightingState state = righting.State;
            float progress = Mathf.Clamp01(righting.PullProgress01);
            if (!hasRightingBaseline)
            {
                previousRightingState = state;
                previousPullProgress = progress;
                hasRightingBaseline = true;
                return;
            }
            bool building = state == RobotSelfRightingState.BuildingSupport;
            bool pulling = state == RobotSelfRightingState.PullingBalance;
            bool wasSupported = previousRightingState == RobotSelfRightingState.BuildingSupport
                || previousRightingState == RobotSelfRightingState.PullingBalance;
            // BuildingSupport can advance to PullingBalance within one gameplay
            // update; this still represents the first observed brace of an attempt.
            if ((building || pulling) && !wasSupported && righting.IsArmSupportCorrect)
                voices.Play(Milestone3Cue.SupportStart);
            float effort = Mathf.Clamp01(righting.CurrentEffortPushFrequency
                / settings.EffortReferenceFrequency);
            voices.SetParameter(Milestone3Cue.SupportLoad, Milestone3Parameter.Effort, effort);
            voices.SetLoop(Milestone3Cue.SupportLoad, building && righting.IsArmSupportCorrect, 0.3f);
            voices.SetParameter(Milestone3Cue.PullBalance, Milestone3Parameter.Progress, progress);
            voices.SetParameter(Milestone3Cue.PullBalance, Milestone3Parameter.Effort, effort);
            voices.SetLoop(Milestone3Cue.PullBalance, pulling
                && progress > previousPullProgress + settings.ProgressEpsilon);
            bool chassis = state == RobotSelfRightingState.RightingChassis
                && CanObserveTumble() && tumble.IsSelfRightingVisualActive;
            voices.SetParameter(Milestone3Cue.RightingChassis, Milestone3Parameter.Progress,
                chassis ? Mathf.Clamp01(tumble.SelfRightingVisualProgress01) : 0f);
            voices.SetLoop(Milestone3Cue.RightingChassis, chassis);
            previousRightingState = state;
            previousPullProgress = progress;
        }

        private void OnStarted(RobotTumbleStartedInfo info)
        {
            if (!CanReceiveTumbleEvent() || lastStartedFrame == Time.frameCount) return;
            lastStartedFrame = Time.frameCount;
            lastStepIndex = -1;
            previousPosition = source.MapPosition;
            previousQuarterTurns = tumble.ContinuousQuarterTurnProgress;
            hasTumbleBaseline = true;
            StopStrain();
            // M2 owns the preceding slide-to-tip pitch accent. Keep the authoritative
            // start reset here without layering a separate B01 onset over that sound.
        }

        private void OnStepCompleted(RobotTumbleStepInfo info)
        {
            if (!CanReceiveTumbleEvent() || info.StepIndex == lastStepIndex) return;
            lastStepIndex = info.StepIndex;
            float impact = Mathf.Sqrt(Mathf.Clamp01(info.ImpactLostSpecificEnergy
                / settings.TumbleImpactEnergyReference));
            float incoming = Mathf.Sqrt(Mathf.Clamp01(info.SpecificEnergyBeforeImpact
                / settings.TumbleImpactEnergyReference));
            voices.SetParameter(Milestone3Cue.TumbleImpact, Milestone3Parameter.Impact, impact);
            voices.SetParameter(Milestone3Cue.TumbleImpact, Milestone3Parameter.Speed, incoming);
            MarkLanding(voices.Play(Milestone3Cue.TumbleImpact, impact));
        }

        private void OnRockImpact(RobotTumbleRockImpactInfo info)
        {
            if (!CanReceiveTumbleEvent()) return;
            float impact = Mathf.Clamp01(info.Strength);
            voices.SetParameter(Milestone3Cue.RockImpact, Milestone3Parameter.Impact, impact);
            MarkLanding(voices.Play(Milestone3Cue.RockImpact, impact));
        }

        private void OnSettled(RobotTumbleSettledInfo info)
        {
            if (!CanReceiveTumbleEvent()) return;
            voices.Stop(Milestone3Cue.TumbleMove, true);
            Milestone3Cue cue = info.RecoveredUpright
                ? Milestone3Cue.RecoveredUpright : Milestone3Cue.SettledFallen;
            MarkLanding(voices.Play(cue));
        }

        private void OnForcePulse(RobotSelfRightingForcePulseInfo info)
        {
            if (!CanReceiveRightingEvent()) return;
            voices.Play(info.Accepted ? Milestone3Cue.ForceAccepted : Milestone3Cue.ForceRejected);
        }

        private void OnSupportFailed(RobotSelfRightingFailureInfo info)
        {
            if (!CanReceiveRightingEvent()) return;
            StopStrain();
            voices.Play(Milestone3Cue.SupportFailed);
        }

        private void OnRightingLanded(RobotSelfRightingLandedInfo info)
        {
            if (!CanReceiveRightingEvent()) return;
            StopStrain();
            MarkLanding(voices.Play(Milestone3Cue.RightingLand));
        }

        private void MarkLanding(bool played)
        {
            // Assignment alone is insufficient: a missing bank or failed instance
            // must leave the delivered M2 landing available. RightingLanded runs
            // before the height detector's LateUpdate, so this frame marker covers it.
            if (played) lastLandingFrame = Time.frameCount;
        }

        private void StopStrain()
        {
            voices.Stop(Milestone3Cue.SupportLoad, true);
            voices.Stop(Milestone3Cue.PullBalance, true);
            voices.Stop(Milestone3Cue.RightingChassis, true);
        }

        private void EstablishBaselines()
        {
            if (!CanObserveSource()) return;
            if (CanObserveTumble())
            {
                previousPosition = source.MapPosition;
                previousQuarterTurns = tumble.ContinuousQuarterTurnProgress;
                hasTumbleBaseline = true;
            }
            if (CanObserveRighting())
            {
                previousRightingState = righting.State;
                previousPullProgress = Mathf.Clamp01(righting.PullProgress01);
                hasRightingBaseline = true;
            }
        }

        private bool CanObserveSource() => source != null && source.isActiveAndEnabled
            && source.gameObject.scene.isLoaded;
        private bool CanObserveTumble() => tumble != null && tumble.isActiveAndEnabled;
        private bool CanObserveRighting() => righting != null && righting.isActiveAndEnabled;
        private bool CanReceiveTumbleEvent() => CanObserveSource() && CanObserveTumble()
            && Time.timeScale > 0f;
        private bool CanReceiveRightingEvent() => CanObserveSource() && CanObserveRighting()
            && Time.timeScale > 0f;
    }
}
