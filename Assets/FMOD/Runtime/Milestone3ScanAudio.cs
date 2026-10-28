using System.Collections.Generic;
using AnimalGame.Discovery;
using AnimalGame.RobotMap;
using UnityEngine;

namespace Capstone.Audio
{
    // Supplements the existing M1 charge/pulse voices using authoritative scan state.
    internal sealed class Milestone3ScanAudio
    {
        private const double HitIntervalSeconds = 0.15;
        private readonly Milestone3AudioVoices voices;
        private readonly HashSet<int> hitAnimals = new HashSet<int>(64);
        private ScanChargeUI source;
        private BioScanController hitSource;
        private BioScanController suspendedHitSource;
        private bool hasBaseline, chargeActive, readyPlayed;
        private bool hasHitTime;
        private int hitSequence, audibleHits;
        private double lastHitTime;

        internal ScanChargeUI Source => source;

        internal Milestone3ScanAudio(Milestone3AudioSettings settings, Milestone3AudioVoices voices)
        {
            this.voices = voices;
        }

        internal void Bind(ScanChargeUI next, BioScanController nextHitSource = null)
        {
            BindCharge(next);
            if (!ReferenceEquals(hitSource, nextHitSource) || !ReferenceEquals(suspendedHitSource, null))
            {
                if (!ReferenceEquals(hitSource, null)) hitSource.AnimalDetected -= OnAnimalDetected;
                voices.Stop(Milestone3Cue.ScanHit, true);
                bool sameSource = nextHitSource != null && ReferenceEquals(suspendedHitSource, nextHitSource);
                bool sameRound = sameSource && hitSequence == nextHitSource.ScanSequence;
                hitSource = nextHitSource;
                suspendedHitSource = null;
                if (!sameRound)
                {
                    hitAnimals.Clear();
                    audibleHits = 0;
                }
                if (!sameSource) hasHitTime = false;
                if (hitSource != null)
                {
                    hitSequence = hitSource.ScanSequence;
                    hitSource.AnimalDetected += OnAnimalDetected;
                }
            }
        }

        private void BindCharge(ScanChargeUI next)
        {
            if (!ReferenceEquals(source, next))
            {
                if (!ReferenceEquals(source, null))
                {
                    source.BiologicalScanChargeStarted -= OnChargeStarted;
                    source.BiologicalScanChargeCancelled -= OnChargeEnded;
                    source.FullyChargedBiologicalScanReleased -= OnChargeEnded;
                }
                StopChargeCues();
                source = next;
                if (source != null)
                {
                    source.BiologicalScanChargeStarted += OnChargeStarted;
                    source.BiologicalScanChargeCancelled += OnChargeEnded;
                    source.FullyChargedBiologicalScanReleased += OnChargeEnded;
                }
                EstablishBaseline();
            }
        }

        internal void Suspend()
        {
            // A paused detection wave keeps its sequence. Detach callbacks and
            // voices while retaining its deduplication, pitch and audible clock.
            BindCharge(null);
            voices.Stop(Milestone3Cue.ScanHit, true);
            if (ReferenceEquals(hitSource, null)) return;
            hitSource.AnimalDetected -= OnAnimalDetected;
            suspendedHitSource = hitSource;
            hitSource = null;
        }

        internal void Tick(float deltaTime)
        {
            if (deltaTime <= 0f || Time.timeScale <= 0f)
            {
                StopAllImmediately();
                return;
            }
            if (!CanObserveHits()) voices.Stop(Milestone3Cue.ScanHit, true);
            if (!CanObserve())
            {
                StopChargeCues();
                hasBaseline = false;
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

        private void OnAnimalDetected(int sequence, DiscoverableEntity animal)
        {
            if (!CanObserveHits() || animal == null || sequence != hitSource.ScanSequence) return;
            if (hitSequence != sequence)
            {
                hitSequence = sequence;
                hitAnimals.Clear();
                audibleHits = 0;
            }
            // Suppressed animals are consumed too: no delayed catch-up or repeat hit.
            if (!hitAnimals.Add(animal.GetInstanceID())) return;
            double now = Time.unscaledTimeAsDouble;
            if (hasHitTime && now < lastHitTime + HitIntervalSeconds) return;
            // Start at +2 semitones and add two per audible hit. FMOD caps pitch at 100x;
            // bound the exponent as well, so an unusually long scan cannot overflow.
            float pitch = Mathf.Pow(2f, Mathf.Min(audibleHits + 1, 40) / 6f);
            if (!voices.PlayScanHit(pitch)) return;
            audibleHits = Mathf.Min(audibleHits + 1, 40);
            lastHitTime = now;
            hasHitTime = true;
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
            StopChargeCues();
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

        private bool CanObserveHits() => hitSource != null && hitSource.isActiveAndEnabled
            && hitSource.gameObject.scene.IsValid() && hitSource.gameObject.scene.isLoaded
            && Time.timeScale > 0f;

        private void StopChargeCues()
        {
            voices.Stop(Milestone3Cue.ScanDeploy, true);
            voices.Stop(Milestone3Cue.ScanReady, true);
            chargeActive = readyPlayed = false;
        }

        internal void StopAllImmediately()
        {
            StopChargeCues();
            voices.Stop(Milestone3Cue.ScanHit, true);
            hasBaseline = false;
        }

        internal void Dispose() => Bind(null);
    }
}
