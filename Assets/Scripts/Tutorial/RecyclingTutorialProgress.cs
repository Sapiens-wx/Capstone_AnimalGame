using UnityEngine;

namespace AnimalGame.Tutorial
{
    public enum RecyclingTutorialStep
    {
        ReachWaste, DeployArms, RecycleWaste, ReachLargeWaste, RecycleLargeWaste, StowArms, Completed
    }

    /// <summary>Actual navigation, hands and native recycling observations for the designated waste.</summary>
    public readonly struct RecyclingTutorialSample
    {
        public readonly float DeltaTime;
        public readonly bool Valid, AtWaste, Stopped, ArmsDeployed, ArmsRetracted;
        public readonly bool HoldingWaste, RecycleReady, Processing, GrabHeld, ManualStowRequested;
        public readonly bool AtLargeWaste, HoldingLargeWaste;
        public readonly float LargePullProgress;

        public RecyclingTutorialSample(float deltaTime, bool valid, bool atWaste, bool stopped,
            bool armsDeployed, bool armsRetracted, bool holdingWaste, bool recycleReady,
            bool processing, bool grabHeld, bool manualStowRequested = false,
            bool atLargeWaste = false, bool holdingLargeWaste = false, float largePullProgress = 0f)
        {
            DeltaTime = deltaTime; Valid = valid; AtWaste = atWaste; Stopped = stopped;
            ArmsDeployed = armsDeployed; ArmsRetracted = armsRetracted; HoldingWaste = holdingWaste;
            RecycleReady = recycleReady; Processing = processing; GrabHeld = grabHeld;
            ManualStowRequested = manualStowRequested;
            AtLargeWaste = atLargeWaste; HoldingLargeWaste = holdingLargeWaste;
            LargePullProgress = largePullProgress;
        }
    }

    /// <summary>Observes real recycling without taking grip input or simulating disposal.</summary>
    public sealed class RecyclingTutorialProgress
    {
        private bool reachedWaste, reachedLargeWaste, deployedArms, stowRequested, stowedArms;
        private bool lastValid, confirmationArmed, lastArmsRetracted;

        public RecyclingTutorialStep Step { get; private set; }
        public bool HasRecycledWaste { get; private set; }
        public bool HasGrabbedWaste { get; private set; }
        public bool IsHoldingWaste { get; private set; }
        public bool IsRecycleReady { get; private set; }
        public bool IsProcessing { get; private set; }
        public bool HasDroppedWaste { get; private set; }
        public bool HasBrokenLargeWaste { get; private set; }
        public bool HasRecycledFragment { get; private set; }
        public bool IsHoldingLargeWaste { get; private set; }
        public float LargePullProgress { get; private set; }
        public bool IsComplete => Step == RecyclingTutorialStep.Completed;
        public bool CanContinue => !IsComplete && lastValid && confirmationArmed && IsSatisfied
            && !IsHoldingWaste && !IsHoldingLargeWaste && !IsProcessing;
        public float Target => 1f;
        public float Value => IsSatisfied ? 1f : Step switch
        {
            RecyclingTutorialStep.RecycleWaste => IsProcessing ? .8f : IsHoldingWaste && IsRecycleReady
                ? .6f : IsHoldingWaste ? .35f : HasGrabbedWaste ? .15f : 0f,
            RecyclingTutorialStep.RecycleLargeWaste => IsProcessing ? .9f : HasBrokenLargeWaste
                ? IsHoldingWaste && IsRecycleReady ? .8f : IsHoldingWaste ? .7f : .6f
                : IsHoldingLargeWaste ? .15f + LargePullProgress * .4f : 0f,
            _ => 0f
        };
        public float NormalizedProgress => Mathf.Clamp01(Value);
        private bool IsSatisfied => Step switch
        {
            RecyclingTutorialStep.ReachWaste => reachedWaste,
            RecyclingTutorialStep.DeployArms => deployedArms,
            RecyclingTutorialStep.RecycleWaste => HasRecycledWaste,
            RecyclingTutorialStep.ReachLargeWaste => reachedLargeWaste,
            RecyclingTutorialStep.RecycleLargeWaste => HasBrokenLargeWaste && HasRecycledFragment,
            RecyclingTutorialStep.StowArms => HasRecycledWaste && HasRecycledFragment && stowedArms && lastArmsRetracted,
            RecyclingTutorialStep.Completed => true,
            _ => false
        };

        public RecyclingTutorialProgress() => Reset();

        public void Reset()
        {
            Step = RecyclingTutorialStep.ReachWaste;
            reachedWaste = reachedLargeWaste = deployedArms = stowRequested = stowedArms = lastValid = confirmationArmed = lastArmsRetracted = false;
            HasRecycledWaste = HasGrabbedWaste = IsHoldingWaste = IsRecycleReady = IsProcessing = HasDroppedWaste = false;
            HasBrokenLargeWaste = HasRecycledFragment = IsHoldingLargeWaste = false;
            LargePullProgress = 0f;
        }

        public void Suspend() { lastValid = false; confirmationArmed = false; }

        public void Sample(in RecyclingTutorialSample sample)
        {
            if (!sample.Valid || !float.IsFinite(sample.DeltaTime) || sample.DeltaTime <= 0f || sample.DeltaTime > .25f)
            { Suspend(); return; }
            lastValid = true;
            lastArmsRetracted = sample.ArmsRetracted;
            bool previouslyHeld = IsHoldingWaste || IsHoldingLargeWaste;
            IsHoldingLargeWaste = sample.HoldingLargeWaste;
            IsHoldingWaste = sample.HoldingWaste;
            LargePullProgress = float.IsFinite(sample.LargePullProgress) ? Mathf.Clamp01(sample.LargePullProgress) : 0f;
            IsRecycleReady = sample.HoldingWaste && !sample.HoldingLargeWaste && sample.RecycleReady;
            IsProcessing = sample.Processing;
            HasGrabbedWaste |= sample.HoldingWaste || sample.Processing;
            if (IsHoldingWaste || IsHoldingLargeWaste) HasDroppedWaste = false;
            else if (previouslyHeld && !sample.Processing
                && !(Step >= RecyclingTutorialStep.ReachLargeWaste ? HasRecycledFragment : HasRecycledWaste)) HasDroppedWaste = true;
            reachedWaste |= sample.AtWaste;
            reachedLargeWaste |= sample.AtLargeWaste;
            deployedArms |= sample.ArmsDeployed;
            // An accepted close during processing may finish once the native animation releases the arms.
            if (sample.ManualStowRequested && HasRecycledWaste
                && (HasRecycledFragment || (HasBrokenLargeWaste && sample.Processing))) stowRequested = true;
            stowedArms |= HasRecycledWaste && HasRecycledFragment && stowRequested
                && sample.ArmsRetracted && !IsHoldingWaste && !IsHoldingLargeWaste && !sample.Processing;
            if (IsHoldingWaste || IsHoldingLargeWaste || sample.Processing) confirmationArmed = false;
            else if (!sample.GrabHeld && IsSatisfied) confirmationArmed = true;
        }

        /// <summary>Forward only the real OnRecycled callback for this lesson's exact waste object.</summary>
        public bool RegisterRecycled(bool valid)
        {
            if (!valid || HasRecycledWaste || IsComplete) return false;
            HasRecycledWaste = HasGrabbedWaste = reachedWaste = deployedArms = true;
            HasDroppedWaste = false;
            confirmationArmed = false;
            return true;
        }

        /// <summary>Accept only a verified split of this tutorial's intact large target.</summary>
        public bool RegisterLargeWasteBroken(bool valid)
        {
            if (!valid || HasBrokenLargeWaste || IsComplete) return false;
            HasBrokenLargeWaste = reachedLargeWaste = true;
            HasDroppedWaste = false;
            confirmationArmed = false;
            return true;
        }

        /// <summary>Accept only the real recycling receipt of a fragment from the verified split.</summary>
        public bool RegisterFragmentRecycled(bool valid)
        {
            if (!valid || !HasBrokenLargeWaste || HasRecycledFragment || IsComplete) return false;
            HasRecycledFragment = true;
            HasDroppedWaste = false;
            confirmationArmed = false;
            return true;
        }

        public bool Continue()
        {
            if (!CanContinue || IsComplete) return false;
            Step++;
            confirmationArmed = false;
            return true;
        }
    }
}
