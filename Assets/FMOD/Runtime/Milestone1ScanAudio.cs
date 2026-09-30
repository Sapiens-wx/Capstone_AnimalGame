using AnimalGame.RobotMap;

namespace Capstone.Audio
{
    internal sealed class Milestone1ScanAudio
    {
        private readonly Milestone1AudioVoices voices;
        private ScanChargeUI source;
        private Milestone1AudioVoice charge;

        internal ScanChargeUI Source => source;

        internal Milestone1ScanAudio(Milestone1AudioSettings settings)
        {
            voices = new Milestone1AudioVoices(settings);
        }

        internal void Bind(ScanChargeUI nextSource)
        {
            if (ReferenceEquals(source, nextSource))
                return;
            if (!ReferenceEquals(source, null))
            {
                source.TerrainScanRequested -= OnTerrainScan;
                source.BiologicalScanChargeStarted -= OnChargeStarted;
                source.BiologicalScanChargeCancelled -= OnChargeCancelled;
                source.FullyChargedBiologicalScanReleased -= OnBiologicalScanReleased;
            }
            voices.StopAll(true);
            charge = null;
            source = nextSource;
            if (source == null)
                return;

            source.TerrainScanRequested += OnTerrainScan;
            source.BiologicalScanChargeStarted += OnChargeStarted;
            source.BiologicalScanChargeCancelled += OnChargeCancelled;
            source.FullyChargedBiologicalScanReleased += OnBiologicalScanReleased;
            if (source.isActiveAndEnabled && source.IsCharging)
                OnChargeStarted();
        }

        internal void Tick(float deltaTime)
        {
            if (source == null || !source.isActiveAndEnabled)
            {
                voices.StopAll(true);
                charge = null;
            }
            else if (!source.IsCharging)
            {
                StopCharge();
            }
            // A fully charged scan remains held. The authored sustain loop must
            // continue until the gameplay controller actually releases or cancels.
            voices.Tick(deltaTime);
        }

        internal void StopAllImmediately()
        {
            voices.StopAll(true);
            charge = null;
        }

        private void OnChargeStarted()
        {
            if (source == null || !source.isActiveAndEnabled)
                return;
            StopCharge();
            charge = voices.Play(Milestone1Cue.ScanCharge);
        }

        private void OnChargeCancelled()
        {
            if (source == null || !source.isActiveAndEnabled)
                StopAllImmediately();
            else
                StopCharge();
        }

        private void OnTerrainScan()
        {
            if (source != null && source.isActiveAndEnabled)
                voices.Play(Milestone1Cue.ScanPulse);
        }

        private void OnBiologicalScanReleased()
        {
            StopCharge();
            if (source != null && source.isActiveAndEnabled)
                voices.Play(Milestone1Cue.ScanPulse);
        }

        private void StopCharge()
        {
            voices.Stop(charge);
            charge = null;
        }
    }
}
