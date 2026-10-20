using AnimalGame.World;
using UnityEngine;

namespace AnimalGame.Demos.WasteRobot
{
    /// <summary>
    /// Owns one whole-vehicle scan target. Only its reveal is suspended while
    /// indoors or transitioning; the existing scanner and collision are unchanged.
    /// </summary>
    [DefaultExecutionOrder(70)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WasteRobotBase), typeof(ScannableInteractable))]
    public sealed class WasteRobotScan : MonoBehaviour
    {
        [Tooltip("The single root scan target, explicitly bound to Exterior/Visual. Toggle this adapter to enable/disable vehicle scanning.")]
        [SerializeField] private ScannableInteractable scanTarget;

        private WasteRobotBase machine;
        private WasteRobotTransitionEffects transitions;
        private bool subscribed;
        private bool hasSavedReveal;
        private double suspendedAt;
        private float savedRemaining;
        private float savedElapsed;
        private bool savedHasOrientation;
        private float savedAngle;
        private uint savedSeed;

        public ScannableInteractable ScanTarget => scanTarget;
        public bool IsSuspended { get; private set; }
        public float SuspendedRevealRemaining => hasSavedReveal
            ? Mathf.Max(0f, savedRemaining - SuspendedTime) : 0f;
        private float SuspendedTime => (float)System.Math.Max(0d, Time.timeAsDouble - suspendedAt);

        public void Configure(ScannableInteractable target)
        {
            if (target == null || target.gameObject != gameObject)
                throw new System.ArgumentException("Vehicle scanning requires its one root ScannableInteractable.", nameof(target));
            ClearReveal();
            scanTarget = target;
            RefreshState();
        }

        private void OnEnable()
        {
            ResolveReferences();
            ClearReveal();
            RefreshState();
        }

        private void ResolveReferences()
        {
            if (machine == null) machine = GetComponent<WasteRobotBase>();
            if (scanTarget == null) scanTarget = GetComponent<ScannableInteractable>();
            if (transitions == null) transitions = GetComponent<WasteRobotTransitionEffects>();
            if (subscribed || !isActiveAndEnabled || machine == null) return;
            machine.PresentationChanging += BeforeSwitch;
            machine.PresentationReset += ResetReveal;
            machine.PresentationUnavailable += Unavailable;
            subscribed = true;
        }

        // Explicitly callable by disposable editor fixtures as well as LateUpdate.
        // Runs after effects (60), before the existing scan overlay (321).
        public void RefreshState()
        {
            ResolveReferences();
            if (!isActiveAndEnabled || machine == null || !machine.isActiveAndEnabled)
            {
                Unavailable();
                return;
            }
            bool blocked = machine.IsInside || (transitions != null && transitions.isActiveAndEnabled
                && transitions.IsTransitioning) || machine.ExteriorRoot == null || !machine.ExteriorRoot.activeInHierarchy;
            if (blocked) SuspendReveal();
            else ResumeReveal();
        }

        private void LateUpdate() => RefreshState();

        private void BeforeSwitch(bool wasInside, bool willBeInside)
        {
            // The static scan overlay is above the camera-space black mask.
            // Remove this target before the first transition frame is rendered.
            SuspendReveal();
        }

        private void SuspendReveal()
        {
            IsSuspended = true;
            if (scanTarget == null || !scanTarget.enabled) return;
            // Reversals must keep the first snapshot; disabled Scannable clears
            // its own reveal, so resampling it would erase the saved phase.
            if (!hasSavedReveal && scanTarget.RevealRemaining > 0f)
            {
                hasSavedReveal = true;
                suspendedAt = Time.timeAsDouble;
                savedRemaining = scanTarget.RevealRemaining;
                savedElapsed = scanTarget.RevealElapsed;
                savedHasOrientation = scanTarget.HasCapturedScanOrientation;
                savedAngle = scanTarget.CapturedScanWorldAngleDegrees;
                savedSeed = scanTarget.OutlinePatternSeed;
            }
            scanTarget.enabled = false;
        }

        private void ResumeReveal()
        {
            IsSuspended = false;
            if (scanTarget == null) return;
            bool wasDisabled = !scanTarget.enabled;
            scanTarget.enabled = true;
            if (!hasSavedReveal)
            {
                // Explicitly start clean after reset/re-enable, including editor
                // fixtures where normal non-ExecuteAlways callbacks do not run.
                if (wasDisabled) scanTarget.Reveal(0f);
                return;
            }
            float elapsed = SuspendedTime;
            float remaining = Mathf.Max(0f, savedRemaining - elapsed);
            if (remaining > 0f)
                scanTarget.InheritScanRemaining(remaining, savedElapsed + elapsed,
                    savedHasOrientation, savedAngle, savedSeed);
            else scanTarget.Reveal(0f);
            ClearSnapshot();
        }

        private void ResetReveal()
        {
            ClearReveal();
            RefreshState();
        }

        private void Unavailable()
        {
            ClearReveal();
            IsSuspended = true;
        }

        private void ClearReveal()
        {
            ClearSnapshot();
            // Use the public reveal API as well as ordinary disable cleanup.
            // No reflection or changes to the shared scanner are needed.
            if (scanTarget != null)
            {
                if (scanTarget.CanScan) scanTarget.Reveal(0f);
                scanTarget.enabled = false;
            }
        }

        private void ClearSnapshot()
        {
            hasSavedReveal = false;
            suspendedAt = 0d;
            savedRemaining = savedElapsed = savedAngle = 0f;
            savedHasOrientation = false;
            savedSeed = 0u;
        }

        private void OnDisable()
        {
            if (subscribed && machine != null)
            {
                machine.PresentationChanging -= BeforeSwitch;
                machine.PresentationReset -= ResetReveal;
                machine.PresentationUnavailable -= Unavailable;
            }
            subscribed = false;
            Unavailable();
        }

        private void OnDestroy() => OnDisable();
    }
}
