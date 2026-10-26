using AnimalGame.Garbage;
using AnimalGame.RobotArm;
using AnimalGame.World;
using UnityEngine;

namespace AnimalGame.Tutorial
{
    /// <summary>Attached only to the owned tutorial pile; observes its native atomic fragment handoff.</summary>
    [AddComponentMenu("")]
    [DefaultExecutionOrder(1510)]
    public sealed class TutorialLargeWasteBreakObserver : MonoBehaviour
    {
        private TutorialLargeWasteTarget owner;
        private WorldInteraction source;
        private RobotArmController arm;
        private HeavyGarbagePull pull;
        private GarbageFragmentSpawner fragments;
        private bool observedGrip, observedPreview;

        public void Configure(TutorialLargeWasteTarget targetOwner, WorldInteraction sourceItem, RobotArmController nativeArm)
        {
            owner = targetOwner; source = sourceItem; arm = nativeArm;
            pull = sourceItem != null ? sourceItem.GetComponent<HeavyGarbagePull>() : null;
            fragments = sourceItem != null ? sourceItem.GetComponent<GarbageFragmentSpawner>() : null;
            observedGrip = observedPreview = false;
        }

        private void LateUpdate()
        {
            if (owner == null || source == null || arm == null) return;
            observedGrip = source.Owner == arm && arm.HeldObject == source;
            if (!observedGrip) { observedPreview = false; return; }
            observedPreview |= pull != null && pull.HasGripAnchor && pull.Progress > 0f
                && fragments != null && fragments.PullPreviewRoot != null && fragments.PullPreviewCount > 0;
        }

        private void OnDisable()
        {
            // Native CompletePullBreak replaces the held source with its medium fragment, changes
            // Kind to None, then disables/destroys the pile. Drops and ordinary disabling do not.
            if (owner != null && source != null && arm != null && observedGrip && observedPreview
                && source.Kind == WorldInteractionKind.None)
                owner.AcceptNativeBreak(this, source, arm.HeldObject);
            Disarm();
        }

        public void Disarm()
        { owner = null; source = null; arm = null; pull = null; fragments = null; observedGrip = observedPreview = false; }
    }
}
