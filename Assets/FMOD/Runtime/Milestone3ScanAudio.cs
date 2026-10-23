using AnimalGame.RobotMap;
using UnityEngine;

namespace Capstone.Audio
{
    // Supplements the existing M1 charge/pulse voices using authoritative scan state.
    // Scan hits remain reserved: marker creation/breathing and discovery changes do not
    // expose a reliable, repeatable per-target hit signal through the current public API.
    internal sealed class Milestone3ScanAudio
    {
        private readonly Milestone3AudioVoices voices;
        private ScanChargeUI source;
        private bool hasBaseline, chargeActive, readyPlayed;

        internal ScanChargeUI Source => source;

        internal Milestone3ScanAudio(Milestone3AudioSettings settings, Milestone3AudioVoices voices)
        {
            this.voices = voices;
        }

        internal void Bind(ScanChargeUI next)
        {
            if (ReferenceEquals(source, next)) return;
            if (!ReferenceEquals(source, null))
            {
                source.BiologicalScanChargeStarted -= OnChargeStarted;
                source.BiologicalScanChargeCancelled -= OnChargeEnded;
                source.FullyChargedBiologicalScanReleased -= OnChargeEnded;
            }
            StopAllImmediately();
            source = next;
            if (source == null) return;
            source.BiologicalScanChargeStarted += OnChargeStarted;
            source.BiologicalScanChargeCancelled += OnChargeEnded;
            source.FullyChargedBiologicalScanReleased += OnChargeEnded;
            EstablishBaseline();
        }

        internal void Tick(float deltaTime)
        {
            if (deltaTime <= 0f || !CanObserve())
            {
                StopAllImmediately();
                return;
            }
            if (!hasBaseline)
            {
                // Resume/re-enable during a completed charge must not replay its ready cue.
                EstablishBaseline();
                return;
            }
            if (!source.IsCharging)
            {
                if (chargeActive) OnChargeEnded();
                return;
            }
            if (!chargeActive)
            {
                // A state first encountered after its onset is a baseline, not a new deploy.
                chargeActive = true;
                readyPlayed = source.IsFullyCharged;
                return;
            }
            if (source.IsFullyCharged && !readyPlayed)
            {
                readyPlayed = true;
                voices.Play(Milestone3Cue.ScanReady);
            }
        }

        private void OnChargeStarted()
        {
            if (!CanObserve() || chargeActive) return;
            hasBaseline = chargeActive = true;
            readyPlayed = false;
            voices.Play(Milestone3Cue.ScanDeploy);
        }

        private void OnChargeEnded()
        {
            voices.Stop(Milestone3Cue.ScanDeploy, true);
            voices.Stop(Milestone3Cue.ScanReady, true);
            chargeActive = readyPlayed = false;
            hasBaseline = CanObserve();
        }

        private void EstablishBaseline()
        {
            hasBaseline = CanObserve();
            chargeActive = hasBaseline && source.IsCharging;
            readyPlayed = hasBaseline && source.IsFullyCharged;
        }

        private bool CanObserve() => source != null && source.isActiveAndEnabled
            && source.gameObject.scene.IsValid() && source.gameObject.scene.isLoaded
            && Time.timeScale > 0f;

        internal void StopAllImmediately()
        {
            voices.Stop(Milestone3Cue.ScanDeploy, true);
            voices.Stop(Milestone3Cue.ScanReady, true);
            hasBaseline = chargeActive = readyPlayed = false;
        }

        internal void Dispose() => Bind(null);
    }
}
