using System;
using AnimalGame.World;
using UnityEngine;

namespace Capstone.Audio
{
    // Added only to the currently held heavy source; no Update or global discovery.
    // Reused after cancelled grips, with references cleared on every audio unbind.
    [DisallowMultipleComponent]
    public sealed class Milestone3HeavyBreakObserver : MonoBehaviour
    {
        private Milestone3WorldAudio listener;
        private WorldInteraction source;
        private bool validPullPlan;

        internal void Bind(Milestone3WorldAudio target, WorldInteraction item)
        {
            listener = target;
            source = item;
            validPullPlan = false;
        }

        internal void Observe(bool valid) => validPullPlan = valid;

        internal void Clear(Milestone3WorldAudio target)
        {
            if (!ReferenceEquals(listener, target)) return;
            listener = null;
            source = null;
            validPullPlan = false;
        }

        private void OnDisable()
        {
            Milestone3WorldAudio target = listener;
            WorldInteraction item = source;
            bool eligible = validPullPlan;
            listener = null;
            source = null;
            validPullPlan = false;
            if (!eligible || target == null) return;
            // Audio feedback must never interrupt the gameplay component's SetActive.
            try { target.OnHeavySourceDisabled(this, item); }
            catch (Exception exception) { Debug.LogException(exception, this); }
        }
    }
}
