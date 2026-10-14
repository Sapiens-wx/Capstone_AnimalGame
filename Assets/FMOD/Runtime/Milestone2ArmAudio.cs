using AnimalGame.Garbage;
using AnimalGame.RobotArm;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEngine;

namespace Capstone.Audio
{
    internal sealed class Milestone2ArmAudio
    {
        private const float DeploymentEpsilon = 0.000001f;
        private const int MechanismLoopStartMilliseconds = 350;
        private const int MechanismReleaseMilliseconds = 800;
        private readonly Milestone2ActionSettings settings;
        private readonly Milestone2ActionVoices voices;
        private RobotArmController source;
        private RobotMover mover;
        private RobotMarkerView marker;
        private Transform handFrame;
        private WorldInteraction previousHeldObject;
        private Vector2 previousLeftHandLocal;
        private Vector2 previousRightHandLocal;
        private float previousDeployment;
        private float blockedCooldownRemaining;
        private bool previousBlocked;
        private bool hasBaseline;
        private bool deploymentSession;
        private bool retractionSession;

        internal RobotArmController Source => source;

        internal Milestone2ArmAudio(Milestone2ActionSettings settings, Milestone2ActionVoices voices)
        {
            this.settings = settings;
            this.voices = voices;
        }

        internal void Bind(RobotArmController next, RobotMover nextMover)
        {
            if (source == next && mover == nextMover)
                return;

            StopAllImmediately();
            source = next;
            mover = nextMover;
            marker = source != null && source.TryGetComponent(out RobotMarkerView nextMarker)
                ? nextMarker : null;
            if (IsSourceAvailable())
                EstablishBaseline();
        }

        internal void Tick(float deltaTime)
        {
            if (!IsSourceAvailable())
            {
                if (hasBaseline)
                    StopAllImmediately();
                return;
            }

            if (!hasBaseline)
            {
                EstablishBaseline();
                return;
            }

            float elapsed = Mathf.Max(0f, deltaTime);
            blockedCooldownRemaining = Mathf.Max(0f, blockedCooldownRemaining - elapsed);
            float deployment = source.VisibleDeployment01;
            float deploymentDelta = deployment - previousDeployment;
            Transform currentHandFrame = ResolveHandFrame();
            bool sameHandFrame = currentHandFrame == handFrame;
            handFrame = currentHandFrame;
            Vector2 leftHandLocal = HandLocal(source.LeftHandWorld);
            Vector2 rightHandLocal = HandLocal(source.RightHandWorld);
            bool canObserveMotion = elapsed > DeploymentEpsilon;
            bool upright = mover == null || mover.MovementMode == RobotMovementMode.Driven;

            bool deploying = canObserveMotion && deploymentDelta > DeploymentEpsilon
                && (source.State == RobotArmState.Extending
                    || source.State == RobotArmState.OuterOperating || source.State == RobotArmState.Docking);
            // The controller changes State on the same frame it reaches either endpoint.
            bool finishingDeployment = canObserveMotion && deploymentDelta > DeploymentEpsilon
                && previousDeployment < 1f - DeploymentEpsilon
                && deployment >= 1f - DeploymentEpsilon;
            bool retracting = canObserveMotion && source.State == RobotArmState.Retracting
                && deploymentDelta < -DeploymentEpsilon;
            bool finishingRetraction = canObserveMotion && deploymentDelta < -DeploymentEpsilon
                && previousDeployment > DeploymentEpsilon && deployment <= DeploymentEpsilon;

            float deploymentEffort = canObserveMotion
                ? Mathf.Clamp01(Mathf.Abs(deploymentDelta) / elapsed) : 0f;
            UpdateMechanismParameters(Milestone2ActionCue.ArmDeploy, deployment, deploymentEffort, 1f);
            UpdateMechanismParameters(Milestone2ActionCue.ArmRetract, deployment, deploymentEffort, -1f);
            if (deploying || finishingDeployment)
                CancelMechanism(Milestone2ActionCue.ArmRetract, ref retractionSession);
            if (retracting || finishingRetraction)
                CancelMechanism(Milestone2ActionCue.ArmDeploy, ref deploymentSession);
            UpdateMechanism(Milestone2ActionCue.ArmDeploy, deploying, finishingDeployment,
                source.State == RobotArmState.Extending,
                previousDeployment <= DeploymentEpsilon, ref deploymentSession);
            UpdateMechanism(Milestone2ActionCue.ArmRetract, retracting, finishingRetraction,
                source.State == RobotArmState.Retracting,
                previousDeployment >= 1f - DeploymentEpsilon, ref retractionSession);

            bool manualArmState = source.State == RobotArmState.OuterOperating
                || source.State == RobotArmState.Docking;
            float handSpeed = canObserveMotion && sameHandFrame
                ? Mathf.Max((leftHandLocal - previousLeftHandLocal).magnitude,
                    (rightHandLocal - previousRightHandLocal).magnitude) / elapsed : 0f;
            bool servoMoving = canObserveMotion && upright && source.IsArmModeActive
                && (mover == null || !mover.IsPhotoModeInputLocked) && manualArmState
                && deployment >= 1f - DeploymentEpsilon
                && !deploying && !retracting && !finishingDeployment && !finishingRetraction
                && handSpeed > settings.ArmMovementThreshold;
            float servoSpeed = Mathf.Clamp01(handSpeed / Mathf.Max(0.0001f, settings.ArmReferenceSpeed));
            voices.SetParameter(Milestone2ActionCue.ArmServo, ActionAudioParameter.Speed, servoSpeed);
            voices.SetParameter(Milestone2ActionCue.ArmServo, ActionAudioParameter.Effort, servoSpeed);
            voices.SetParameter(Milestone2ActionCue.ArmServo, ActionAudioParameter.Progress, deployment);
            voices.SetParameter(Milestone2ActionCue.ArmServo, ActionAudioParameter.Load,
                mover != null ? Mathf.Clamp01(1f - mover.GrabMovementMultiplier) : 0f);
            voices.SetLoop(Milestone2ActionCue.ArmServo, servoMoving);

            bool blocked = source.IsBlocked;
            if (canObserveMotion && blocked && !previousBlocked
                && deployment > DeploymentEpsilon && blockedCooldownRemaining <= 0f)
            {
                voices.Play(Milestone2ActionCue.ArmBlocked);
                blockedCooldownRemaining = settings.BlockedCooldown;
            }

            WorldInteraction heldObject = source.HeldObject;
            if (!ReferenceEquals(heldObject, previousHeldObject))
            {
                // A successful fragment handoff replaces one held object with another;
                // it does not represent a new player grab.
                if (canObserveMotion && ReferenceEquals(previousHeldObject, null) && heldObject != null
                    && heldObject.Available && heldObject.Owner == source
                    && heldObject.TryGetComponent(out GarbageItem garbage))
                {
                    Milestone2ActionCue cue = garbage.Size == RecyclableSize.Small
                        ? Milestone2ActionCue.GrabSmall
                        : garbage.Size == RecyclableSize.Medium
                            ? Milestone2ActionCue.GrabMedium : Milestone2ActionCue.GrabLarge;
                    voices.Play(cue);
                }
                previousHeldObject = heldObject;
            }

            // HandClose/HandOpen need a public empty-hand grip transition. Drop needs
            // a public release reason to distinguish player release from forced release.
            // Their authored cues remain available without predicting raw input.
            previousDeployment = deployment;
            previousLeftHandLocal = leftHandLocal;
            previousRightHandLocal = rightHandLocal;
            previousBlocked = blocked;
        }

        internal void StopAllImmediately()
        {
            for (int cue = (int)Milestone2ActionCue.ArmDeploy;
                 cue <= (int)Milestone2ActionCue.ArmRetractLock; cue++)
                voices.Stop((Milestone2ActionCue)cue, true);
            hasBaseline = false;
            deploymentSession = retractionSession = false;
            previousHeldObject = null;
            blockedCooldownRemaining = 0f;
        }

        private bool IsSourceAvailable() => source != null && source.isActiveAndEnabled
            && (mover == null || mover.isActiveAndEnabled);

        private void EstablishBaseline()
        {
            handFrame = ResolveHandFrame();
            previousDeployment = source.VisibleDeployment01;
            previousLeftHandLocal = HandLocal(source.LeftHandWorld);
            previousRightHandLocal = HandLocal(source.RightHandWorld);
            previousHeldObject = source.HeldObject;
            previousBlocked = source.IsBlocked;
            hasBaseline = true;
        }

        private Vector2 HandLocal(Vector2 worldPosition) =>
            handFrame.InverseTransformPoint(new Vector3(worldPosition.x, worldPosition.y,
                handFrame.position.z));

        private Transform ResolveHandFrame() => marker != null && marker.MarkerVisualRoot != null
            ? marker.MarkerVisualRoot : source.transform;

        private void UpdateMechanism(Milestone2ActionCue cue, bool moving, bool completed,
            bool waitingForMotion, bool startsAtEndpoint, ref bool session)
        {
            if (completed)
            {
                // The same sustain event owns the lock tail. A missed/very short
                // movement starts at the sustain boundary, never replays its intro.
                if (!voices.IsActive(cue))
                    voices.PlayFrom(cue, MechanismReleaseMilliseconds);
                voices.KeyOff(cue);
                session = false;
                return;
            }
            if (moving)
            {
                if (!session || !voices.IsActive(cue))
                    voices.PlayFrom(cue, startsAtEndpoint ? 0 : MechanismLoopStartMilliseconds);
                voices.SetPaused(cue, false);
                session = true;
                return;
            }
            if (!session) return;
            if (waitingForMotion)
                voices.SetPaused(cue, true);
            else
                CancelMechanism(cue, ref session);
        }

        private void CancelMechanism(Milestone2ActionCue cue, ref bool session)
        {
            // Cancellation must not cross the sustain marker into the lock tail.
            voices.Stop(cue, true);
            session = false;
        }

        private void UpdateMechanismParameters(Milestone2ActionCue cue, float progress,
            float effort, float direction)
        {
            voices.SetParameter(cue, ActionAudioParameter.Progress, progress);
            voices.SetParameter(cue, ActionAudioParameter.Effort, effort);
            voices.SetParameter(cue, ActionAudioParameter.Direction, direction);
        }
    }
}
