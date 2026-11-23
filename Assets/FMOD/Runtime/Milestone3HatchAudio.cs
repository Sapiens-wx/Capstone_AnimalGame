using AnimalGame.RobotArm;

namespace Capstone.Audio
{
    internal sealed class Milestone3HatchAudio
    {
        private readonly Milestone3AudioVoices voices;
        private RobotArmController arm;
        private bool? previousOpen;

        internal Milestone3HatchAudio(Milestone3AudioVoices voices) => this.voices = voices;

        internal void Bind(RobotArmController source)
        {
            if (arm == source) return;
            Reset();
            arm = source;
        }

        internal void Tick()
        {
            if (arm == null || !arm.isActiveAndEnabled || arm.IsInventoryPresentationActive)
            {
                Reset();
                return;
            }

            // This is the same target the body hatch animation moves toward.
            bool open = arm.ShouldOpenBodyHatches;
            if (previousOpen.HasValue && previousOpen.Value != open)
            {
                voices.Stop(open ? Milestone3Cue.HatchClose : Milestone3Cue.HatchOpen);
                voices.Play(open ? Milestone3Cue.HatchOpen : Milestone3Cue.HatchClose);
            }
            previousOpen = open;
        }

        private void Reset()
        {
            if (!previousOpen.HasValue) return;
            voices.Stop(Milestone3Cue.HatchOpen, true);
            voices.Stop(Milestone3Cue.HatchClose, true);
            previousOpen = null;
        }
    }
}
