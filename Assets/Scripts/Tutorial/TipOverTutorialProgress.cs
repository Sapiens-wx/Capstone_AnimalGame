using UnityEngine;

namespace AnimalGame.Tutorial
{
    public enum TipOverTutorialStep { Explanation, DeployArms, Recover, Completed }

    /// <summary>Read-only observations from the existing tumble, arms and self-righting systems.</summary>
    public readonly struct TipOverTutorialSample
    {
        public readonly float DeltaTime;
        public readonly bool Valid, Fallen, ArmsDeployed, Upright, Righting, Stable, ConfirmHeld;

        public TipOverTutorialSample(float deltaTime, bool valid, bool fallen, bool armsDeployed,
            bool upright, bool righting, bool stable, bool confirmHeld)
        {
            DeltaTime = deltaTime; Valid = valid; Fallen = fallen; ArmsDeployed = armsDeployed;
            Upright = upright; Righting = righting; Stable = stable; ConfirmHeld = confirmHeld;
        }
    }

    /// <summary>Retains observed actions while an interrupt owns the shared tutorial card.</summary>
    public sealed class TipOverTutorialProgress
    {
        private bool lastValid, confirmationArmed, observedFallen, observedDeployment;
        private readonly bool skipIntroduction;

        public TipOverTutorialStep Step { get; private set; }
        public bool IsComplete => Step == TipOverTutorialStep.Completed;
        public bool CanContinue => !IsComplete && lastValid && confirmationArmed && IsSatisfied;
        public float StableSeconds { get; private set; }
        public float RequiredStableSeconds { get; }
        public float NormalizedProgress => Step switch
        {
            TipOverTutorialStep.Explanation => observedFallen ? 1f : 0f,
            TipOverTutorialStep.DeployArms => observedDeployment ? 1f : 0f,
            TipOverTutorialStep.Completed => 1f,
            _ => Mathf.Clamp01(StableSeconds / RequiredStableSeconds)
        };

        private bool IsSatisfied => Step switch
        {
            TipOverTutorialStep.Explanation => observedFallen,
            TipOverTutorialStep.DeployArms => observedDeployment,
            TipOverTutorialStep.Completed => true,
            _ => false
        };

        public TipOverTutorialProgress(float stableSeconds = .8f, bool skipIntroduction = false)
        {
            RequiredStableSeconds = float.IsFinite(stableSeconds) ? Mathf.Max(.1f, stableSeconds) : .8f;
            this.skipIntroduction = skipIntroduction;
            Reset();
        }

        public void Reset()
        {
            Step = skipIntroduction ? TipOverTutorialStep.Recover : TipOverTutorialStep.Explanation;
            lastValid = confirmationArmed = observedFallen = observedDeployment = false;
            StableSeconds = 0f;
        }

        public void Suspend()
        {
            lastValid = confirmationArmed = false;
            if (!IsComplete) StableSeconds = 0f;
        }

        /// <summary>Retain an actual native Fallen observation even while a modal hides this card.</summary>
        public void ObserveFallen() => observedFallen = true;

        /// <summary>A new native fall after landing returns directly to the ongoing recovery attempt.</summary>
        public void ResumeRecovery()
        {
            Step = TipOverTutorialStep.Recover;
            StableSeconds = 0f;
            lastValid = confirmationArmed = false;
        }

        public void Sample(in TipOverTutorialSample sample)
        {
            if (!sample.Valid || !float.IsFinite(sample.DeltaTime) || sample.DeltaTime <= 0f || sample.DeltaTime > .25f)
            { Suspend(); return; }

            lastValid = true;
            if (sample.Fallen) ObserveFallen();
            observedDeployment |= sample.ArmsDeployed;
            bool uprightAndSettled = !sample.Fallen && sample.Upright && !sample.Righting;
            if (IsComplete)
            {
                // A second fall before the owner resumes keeps this same recovery attempt active.
                if (!uprightAndSettled || !sample.Stable)
                {
                    Step = TipOverTutorialStep.Recover;
                    StableSeconds = 0f; confirmationArmed = false;
                }
            }
            else if (observedFallen && uprightAndSettled && sample.Stable)
            {
                StableSeconds = Mathf.Min(RequiredStableSeconds, StableSeconds + sample.DeltaTime);
                if (StableSeconds + .00001f >= RequiredStableSeconds)
                {
                    // Native recovery is valid even if the player acted ahead of the instruction cards.
                    StableSeconds = RequiredStableSeconds;
                    Step = TipOverTutorialStep.Completed;
                    confirmationArmed = false;
                    return;
                }
            }
            else StableSeconds = 0f;

            if (!sample.ConfirmHeld && IsSatisfied) confirmationArmed = true;
        }

        /// <summary>Advance only the introductory instruction cards; stable recovery resumes automatically.</summary>
        public bool Continue()
        {
            if (!CanContinue || IsComplete) return false;
            Step++;
            confirmationArmed = false;
            return true;
        }
    }
}
