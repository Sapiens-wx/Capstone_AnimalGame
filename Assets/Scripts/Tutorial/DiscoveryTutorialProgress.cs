using UnityEngine;

namespace AnimalGame.Tutorial
{
    public enum DiscoveryTutorialStep { Charge = 0, Release = 1, FindAnimal = 2, ConfirmAnimal = 3, Rescan = 5, Completed = 6 }

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
        private bool chargeStarted, chargeReady, chargeAchieved, scanReleased, animalHit, objectHit;
        private bool animalObserved, animalSeen, objectObserved;
        private float animalReading, objectReading;
        private int waveSequence;

        public DiscoveryTutorialStep Step { get; private set; }
        public float ReadingSeconds { get; private set; }
        public int LatestWaveId { get; private set; }
        public bool IsComplete => Step == DiscoveryTutorialStep.Completed;
        public bool IsChargeReady => chargeStarted && chargeReady;
        public bool HasReleasedScan => scanReleased;
        public bool CanContinue => Step switch
        {
            DiscoveryTutorialStep.Charge => chargeAchieved,
            DiscoveryTutorialStep.Release => scanReleased,
            DiscoveryTutorialStep.FindAnimal => animalObserved && IsRead(animalReading, animalReadSeconds),
            DiscoveryTutorialStep.ConfirmAnimal => animalSeen,
            DiscoveryTutorialStep.Completed => true,
            _ => false
        };
        public bool NeedsFreshScan => Step switch
        {
            DiscoveryTutorialStep.Release => !scanReleased && !chargeStarted,
            DiscoveryTutorialStep.FindAnimal or DiscoveryTutorialStep.ConfirmAnimal => !animalObserved,
            _ => false
        };
        public float Value => CanContinue ? Target : ReadingSeconds;
        public float Target => Step switch
        {
            DiscoveryTutorialStep.FindAnimal => animalReadSeconds,
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
            chargeStarted = chargeReady = chargeAchieved = scanReleased = animalHit = objectHit = false;
            animalObserved = animalSeen = objectObserved = false;
            animalReading = objectReading = 0f;
            // Keep event identifiers monotonic so callbacks from a pre-reset wave stay invalid.
        }

        public void Suspend()
        {
            chargeStarted = chargeReady = false;
        }

        public void RegisterChargeStarted(bool valid)
        {
            if (IsComplete) return;
            if (!valid) { Suspend(); return; }
            chargeStarted = true; chargeReady = false;
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
            scanReleased = true;
            return LatestWaveId;
        }

        public bool RegisterAnimalHit(int waveId, bool valid) => RegisterHit(waveId, valid, true);
        public bool RegisterObjectHit(int waveId, bool valid) => RegisterHit(waveId, valid, false);

        private bool RegisterHit(int waveId, bool valid, bool isAnimal)
        {
            if (!valid || IsComplete || waveId <= 0 || waveId != LatestWaveId) return false;
            if (isAnimal) animalHit = true; else objectHit = true;
            return true;
        }

        public void Sample(in DiscoveryTutorialSample sample)
        {
            if (IsComplete) return;
            if (!sample.Valid || !float.IsFinite(sample.DeltaTime) || sample.DeltaTime <= 0f || sample.DeltaTime > .25f)
            { Suspend(); return; }
            if (chargeStarted && sample.FullyCharged) chargeReady = chargeAchieved = true;
            // A scan may finish while the player is reading an earlier card. Preserve its real
            // observations so confirming that card cannot lose the wave, seen state or object reveal.
            animalObserved |= animalHit && sample.AnimalMarkerVisible;
            animalSeen |= animalHit && sample.AnimalHasBeenSeen;
            objectObserved |= objectHit && sample.ObjectHighlightVisible;
            if (sample.PanelExpanded)
            {
                if (animalObserved)
                    animalReading = Mathf.Min(animalReadSeconds, animalReading + sample.DeltaTime);
                if (objectObserved)
                    objectReading = Mathf.Min(objectReadSeconds, objectReading + sample.DeltaTime);
            }
            switch (Step)
            {
                case DiscoveryTutorialStep.FindAnimal:
                    ReadingSeconds = animalReading;
                    break;
            }
        }

        /// <summary>Only an explicit tutorial confirmation changes the displayed instruction.</summary>
        public bool Continue()
        {
            if (!CanContinue || IsComplete) return false;
            MoveTo(Step switch
            {
                DiscoveryTutorialStep.Charge => DiscoveryTutorialStep.Release,
                DiscoveryTutorialStep.Release => DiscoveryTutorialStep.FindAnimal,
                DiscoveryTutorialStep.FindAnimal => DiscoveryTutorialStep.ConfirmAnimal,
                DiscoveryTutorialStep.ConfirmAnimal => DiscoveryTutorialStep.Completed,
                _ => DiscoveryTutorialStep.Completed
            });
            return true;
        }

        private void MoveTo(DiscoveryTutorialStep next)
        {
            Step = next; ReadingSeconds = 0f;
        }
        private static bool IsRead(float value, float duration) => duration - value <= duration * .00001f;
    }
}
