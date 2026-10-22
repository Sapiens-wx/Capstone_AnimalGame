using UnityEngine;

namespace AnimalGame.Tutorial
{
    public enum DiscoveryTutorialStep { Charge, Release, FindAnimal, ConfirmAnimal, ReadObject, Rescan, Completed }

    /// <summary>Actual target presentation, sampled after the existing scanner and marker systems.</summary>
    public readonly struct DiscoveryTutorialSample
    {
        public readonly float DeltaTime;
        public readonly bool Valid, PanelExpanded, FullyCharged;
        public readonly bool AnimalMarkerVisible, AnimalHasBeenSeen, ObjectHighlightVisible;

        public DiscoveryTutorialSample(float deltaTime, bool valid, bool panelExpanded, bool fullyCharged,
            bool animalMarkerVisible, bool animalHasBeenSeen, bool objectHighlightVisible)
        {
            DeltaTime = deltaTime; Valid = valid; PanelExpanded = panelExpanded; FullyCharged = fullyCharged;
            AnimalMarkerVisible = animalMarkerVisible; AnimalHasBeenSeen = animalHasBeenSeen;
            ObjectHighlightVisible = objectHighlightVisible;
        }
    }

    /// <summary>Observes real scan cycles and target hits. Does not reveal, spawn or move gameplay objects.</summary>
    public sealed class DiscoveryTutorialProgress
    {
        private readonly float animalReadSeconds, objectReadSeconds;
        private bool chargeStarted, chargeReady, animalHit, objectHit, animalVisible, objectVisible;
        private int waveSequence, chargeSequence, rescanAfterCharge, rescanWave;

        public DiscoveryTutorialStep Step { get; private set; }
        public float ReadingSeconds { get; private set; }
        public int LatestWaveId { get; private set; }
        public bool IsComplete => Step == DiscoveryTutorialStep.Completed;
        public bool IsChargeReady => chargeStarted && chargeReady;
        public bool NeedsFreshScan => Step switch
        {
            DiscoveryTutorialStep.Release => !chargeStarted,
            DiscoveryTutorialStep.FindAnimal or DiscoveryTutorialStep.ConfirmAnimal => !animalHit || !animalVisible,
            DiscoveryTutorialStep.ReadObject => !objectHit || !objectVisible,
            _ => false
        };
        public float Value => IsComplete ? 1f : ReadingSeconds;
        public float Target => Step switch
        {
            DiscoveryTutorialStep.FindAnimal => animalReadSeconds,
            DiscoveryTutorialStep.ReadObject => objectReadSeconds,
            _ => 1f
        };
        public float NormalizedProgress => IsComplete ? 1f : Mathf.Clamp01(Value / Target);

        public DiscoveryTutorialProgress(float animalReadSeconds = 1.2f, float objectReadSeconds = 1.5f)
        {
            this.animalReadSeconds = float.IsFinite(animalReadSeconds) ? Mathf.Max(.1f, animalReadSeconds) : 1.2f;
            this.objectReadSeconds = float.IsFinite(objectReadSeconds) ? Mathf.Max(.1f, objectReadSeconds) : 1.5f;
        }

        public void Reset()
        {
            Step = DiscoveryTutorialStep.Charge; ReadingSeconds = 0f; LatestWaveId = 0;
            chargeStarted = chargeReady = animalHit = objectHit = animalVisible = objectVisible = false;
            rescanWave = 0; rescanAfterCharge = chargeSequence;
            // Keep event identifiers monotonic so callbacks from a pre-reset wave stay invalid.
        }

        public void Suspend()
        {
            ReadingSeconds = 0f;
            chargeStarted = chargeReady = false;
        }

        public void RegisterChargeStarted(bool valid)
        {
            if (IsComplete) return;
            if (!valid) { Suspend(); return; }
            chargeSequence++; chargeStarted = true; chargeReady = false;
        }

        public void RegisterChargeCancelled(bool valid)
        {
            if (IsComplete) return;
            chargeStarted = chargeReady = false;
        }

        /// <summary>Returns the accepted wave ID, or zero for a tap, cancellation, old charge or modal release.</summary>
        public int RegisterScanReleased(bool valid)
        {
            if (IsComplete) return 0;
            bool accepted = valid && chargeStarted && chargeReady;
            chargeStarted = chargeReady = false;
            if (!accepted) return 0;
            LatestWaveId = ++waveSequence;
            if (Step == DiscoveryTutorialStep.Release) MoveTo(DiscoveryTutorialStep.FindAnimal);
            else if (Step == DiscoveryTutorialStep.Rescan && chargeSequence > rescanAfterCharge)
                rescanWave = LatestWaveId;
            return LatestWaveId;
        }

        public bool RegisterAnimalHit(int waveId, bool valid) => RegisterHit(waveId, valid, true);
        public bool RegisterObjectHit(int waveId, bool valid) => RegisterHit(waveId, valid, false);

        private bool RegisterHit(int waveId, bool valid, bool isAnimal)
        {
            if (!valid || IsComplete || waveId <= 0 || waveId != LatestWaveId) return false;
            if (isAnimal) animalHit = true; else objectHit = true;
            if (Step == DiscoveryTutorialStep.Rescan && waveId == rescanWave)
                MoveTo(DiscoveryTutorialStep.Completed);
            return true;
        }

        public void Sample(in DiscoveryTutorialSample sample)
        {
            if (IsComplete) return;
            if (!sample.Valid || !float.IsFinite(sample.DeltaTime) || sample.DeltaTime <= 0f || sample.DeltaTime > .25f)
            { Suspend(); return; }
            animalVisible = sample.AnimalMarkerVisible;
            objectVisible = sample.ObjectHighlightVisible;
            if (chargeStarted && sample.FullyCharged) chargeReady = true;
            switch (Step)
            {
                case DiscoveryTutorialStep.Charge:
                    if (IsChargeReady) MoveTo(DiscoveryTutorialStep.Release);
                    break;
                case DiscoveryTutorialStep.FindAnimal:
                    Read(animalHit && animalVisible && sample.PanelExpanded, sample.DeltaTime,
                        animalReadSeconds, DiscoveryTutorialStep.ConfirmAnimal);
                    break;
                case DiscoveryTutorialStep.ConfirmAnimal:
                    if (animalHit && animalVisible && sample.AnimalHasBeenSeen)
                        MoveTo(DiscoveryTutorialStep.ReadObject);
                    break;
                case DiscoveryTutorialStep.ReadObject:
                    Read(objectHit && objectVisible && sample.PanelExpanded, sample.DeltaTime,
                        objectReadSeconds, DiscoveryTutorialStep.Rescan);
                    break;
            }
        }

        private void Read(bool eligible, float deltaTime, float duration, DiscoveryTutorialStep next)
        {
            ReadingSeconds = eligible ? Mathf.Min(duration, ReadingSeconds + deltaTime) : 0f;
            if (duration - ReadingSeconds <= duration * .00001f) MoveTo(next);
        }

        private void MoveTo(DiscoveryTutorialStep next)
        {
            Step = next; ReadingSeconds = 0f;
            if (next == DiscoveryTutorialStep.Rescan) { rescanAfterCharge = chargeSequence; rescanWave = 0; }
        }
    }
}
