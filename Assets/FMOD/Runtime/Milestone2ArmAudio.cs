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
        private const int ServoLoopStartMilliseconds = 50;
        private const float JointBindingRetrySeconds = 0.25f;
        private readonly Milestone2ActionSettings settings;
        private readonly Milestone2ActionVoices voices;
        private RobotArmController source;
        private RobotMover mover;
        private RobotMarkerView marker;
        private Transform visualFrame;
        private Transform leftUpper;
        private Transform leftLower;
        private Transform rightUpper;
        private Transform rightLower;
        private SpriteRenderer leftHandRenderer;
        private SpriteRenderer rightHandRenderer;
        private Sprite previousLeftHandSprite;
        private Sprite previousRightHandSprite;
        private WorldInteraction previousHeldObject;
        private GarbageItem previousHeldGarbage;
        private WorldInteraction pendingDrop;
        private int pendingDropFrame;
        private int pendingDropSceneHandle;
        private bool previousHeldWasOwnedGarbage;
        private JointAngles previousJointAngles;
        private float previousDeployment;
        private float blockedCooldownRemaining;
        private float jointBindingRetryRemaining;
        private float handBindingRetryRemaining;
        private float servoPitch;
        private bool previousBlocked;
        private bool hasBaseline;
        private bool hasJointBaseline;
        private bool deploymentSession;
        private bool retractionSession;
        private bool componentDisabled;
        private bool hasHandSpriteBaseline;
        private bool previousHandContextReady;

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
            float elapsed = Mathf.Max(0f, deltaTime);
            if (!IsSourceAvailable())
            {
                ObserveDisabledRelease(elapsed);
                return;
            }

            componentDisabled = false;
            if (!hasBaseline)
            {
                if (elapsed > DeploymentEpsilon)
                    ConfirmPendingDrop(source.HeldObject);
                EstablishBaseline();
                return;
            }

            blockedCooldownRemaining = Mathf.Max(0f, blockedCooldownRemaining - elapsed);
            float deployment = source.VisibleDeployment01;
            float deploymentDelta = deployment - previousDeployment;
            bool canObserveMotion = elapsed > DeploymentEpsilon;
            float jointSpeed = ObserveJointSpeed(elapsed);

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
            bool servoMoving = canObserveMotion && source.IsArmModeActive
                && (mover == null || !mover.IsPhotoModeInputLocked) && manualArmState
                && deployment >= 1f - DeploymentEpsilon
                && !deploying && !retracting && !finishingDeployment && !finishingRetraction
                && jointSpeed > settings.ArmJointMovementThresholdDegrees;
            float servoSpeed = Mathf.Clamp01(jointSpeed / settings.ArmJointReferenceSpeedDegrees);
            WorldInteraction heldObject = source.HeldObject;
            float heldLoad = heldObject != null && heldObject.Available && heldObject.Owner == source
                ? Mathf.Clamp01(heldObject.GrabResistance) : 0f;
            voices.SetParameter(Milestone2ActionCue.ArmServo, ActionAudioParameter.Speed, servoSpeed);
            voices.SetParameter(Milestone2ActionCue.ArmServo, ActionAudioParameter.Effort, servoSpeed);
            voices.SetParameter(Milestone2ActionCue.ArmServo, ActionAudioParameter.Progress, deployment);
            voices.SetParameter(Milestone2ActionCue.ArmServo, ActionAudioParameter.Load, heldLoad);
            if (servoMoving)
            {
                float targetPitch = Mathf.Clamp(
                    Mathf.Lerp(settings.ArmMinimumPitch, settings.ArmMaximumPitch, servoSpeed)
                    * (1f - heldLoad * settings.ArmLoadPitchReduction),
                    settings.ArmMinimumPitch * (1f - settings.ArmLoadPitchReduction), settings.ArmMaximumPitch);
                servoPitch = voices.IsAlive(Milestone2ActionCue.ArmServo)
                    ? Mathf.Lerp(servoPitch, targetPitch,
                        1f - Mathf.Exp(-elapsed / settings.ArmPitchSmoothingSeconds))
                    : targetPitch;
                // Prime the real FMOD pitch before starting the loop's audible window.
                voices.SetPitch(Milestone2ActionCue.ArmServo, servoPitch);
            }
            voices.SetLoopFrom(Milestone2ActionCue.ArmServo, servoMoving, ServoLoopStartMilliseconds);
            ObserveEmptyHandGrip(heldObject, deployment,
                deploying || retracting || finishingDeployment || finishingRetraction,
                canObserveMotion, elapsed);

            bool blocked = source.IsBlocked;
            if (canObserveMotion && blocked && !previousBlocked
                && deployment > DeploymentEpsilon && blockedCooldownRemaining <= 0f)
            {
                voices.Play(Milestone2ActionCue.ArmBlocked);
                blockedCooldownRemaining = settings.BlockedCooldown;
            }

            if (canObserveMotion)
                ConfirmPendingDrop(heldObject);
            if (!ReferenceEquals(heldObject, previousHeldObject))
            {
                WorldInteraction oldHeld = previousHeldObject;
                bool wasOwnedGarbage = previousHeldWasOwnedGarbage;
                CacheHeldObject(heldObject);
                int sceneHandle = source.gameObject.scene.handle;
                if (canObserveMotion && wasOwnedGarbage
                    && IsWorldReleaseCandidate(oldHeld, heldObject, sceneHandle))
                {
                    // Confirm next frame: Destroy is deferred, while recycle or
                    // fragment replacement must never sound like a world release.
                    QueueDrop(oldHeld, sceneHandle);
                }
                // A successful fragment handoff replaces one held object with another;
                // it does not represent a new player grab.
                if (canObserveMotion && ReferenceEquals(oldHeld, null) && heldObject != null
                    && heldObject.Available && heldObject.Owner == source
                    && previousHeldGarbage != null)
                {
                    Milestone2ActionCue cue = previousHeldGarbage.Size == RecyclableSize.Small
                        ? Milestone2ActionCue.GrabSmall
                        : previousHeldGarbage.Size == RecyclableSize.Medium
                            ? Milestone2ActionCue.GrabMedium : Milestone2ActionCue.GrabLarge;
                    voices.Play(cue);
                }
            }
            // Release can clear Owner before the controller clears HeldObject.
            // Keep the confirmed ownership for this held-reference session.
            if (heldObject != null && previousHeldGarbage != null
                && heldObject.Available && heldObject.Owner == source)
                previousHeldWasOwnedGarbage = true;

            // Empty-hand open/close share HandClose; owned grabs use only their
            // material cue. Drop confirms a real release without predicting input.
            previousDeployment = deployment;
            previousBlocked = blocked;
        }

        internal void StopAllImmediately()
        {
            StopArmVoices(false);
            ResetObservation();
            pendingDrop = null;
            componentDisabled = false;
        }

        private void StopArmVoices(bool preserveDrop)
        {
            for (int cue = (int)Milestone2ActionCue.ArmDeploy;
                 cue <= (int)Milestone2ActionCue.ArmRetractLock; cue++)
            {
                if (preserveDrop && cue == (int)Milestone2ActionCue.Drop) continue;
                voices.Stop((Milestone2ActionCue)cue, true);
            }
        }

        private void ResetObservation()
        {
            hasBaseline = false;
            deploymentSession = retractionSession = false;
            previousHeldObject = null;
            previousHeldGarbage = null;
            previousHeldWasOwnedGarbage = false;
            blockedCooldownRemaining = 0f;
            ResetJointBinding();
            ResetHandBinding();
            servoPitch = settings.ArmMinimumPitch;
        }

        private bool IsSourceAvailable() => source != null && source.isActiveAndEnabled
            && source.gameObject.activeInHierarchy
            && (mover == null || mover.isActiveAndEnabled);

        private void ObserveDisabledRelease(float elapsed)
        {
            // Disabling just the arm invokes gameplay Drop. Whole-object/scene
            // teardown and the existing explicit lifecycle cleanup remain silent.
            bool liveWorld = source != null && source.gameObject.activeInHierarchy
                && (mover == null || mover.isActiveAndEnabled) && Time.timeScale > 0f;
            if (liveWorld)
            {
                var scene = source.gameObject.scene;
                liveWorld = scene.IsValid() && scene.isLoaded;
            }
            if (!liveWorld)
            {
                if (hasBaseline || componentDisabled || !ReferenceEquals(pendingDrop, null))
                    StopAllImmediately();
                return;
            }

            WorldInteraction heldObject = source.HeldObject;
            if (elapsed > DeploymentEpsilon)
                ConfirmPendingDrop(heldObject);
            if (componentDisabled) return;

            WorldInteraction oldHeld = previousHeldObject;
            int sceneHandle = source.gameObject.scene.handle;
            bool released = elapsed > DeploymentEpsilon && previousHeldWasOwnedGarbage
                && !ReferenceEquals(oldHeld, heldObject)
                && IsWorldReleaseCandidate(oldHeld, heldObject, sceneHandle);
            StopArmVoices(true);
            ResetObservation();
            componentDisabled = true;
            if (released) QueueDrop(oldHeld, sceneHandle);
        }

        private void EstablishBaseline()
        {
            visualFrame = ResolveVisualFrame();
            ResetJointBinding();
            ObserveJointSpeed(0f);
            previousDeployment = source.VisibleDeployment01;
            CacheHeldObject(source.HeldObject);
            previousBlocked = source.IsBlocked;
            ResetHandBinding();
            ObserveEmptyHandGrip(source.HeldObject, previousDeployment, false, false, 0f);
            hasBaseline = true;
        }

        private Transform ResolveVisualFrame() => marker != null && marker.MarkerVisualRoot != null
            ? marker.MarkerVisualRoot : source.transform;

        private void CacheHeldObject(WorldInteraction heldObject)
        {
            previousHeldObject = heldObject;
            previousHeldGarbage = heldObject != null && heldObject.TryGetComponent(out GarbageItem garbage)
                ? garbage : null;
            previousHeldWasOwnedGarbage = previousHeldGarbage != null
                && heldObject.Available && heldObject.Owner == source;
        }

        private void QueueDrop(WorldInteraction dropped, int sceneHandle)
        {
            pendingDrop = dropped;
            pendingDropFrame = Time.frameCount;
            pendingDropSceneHandle = sceneHandle;
        }

        private void ConfirmPendingDrop(WorldInteraction heldObject)
        {
            WorldInteraction dropped = pendingDrop;
            if (ReferenceEquals(dropped, null) || Time.frameCount == pendingDropFrame) return;
            pendingDrop = null;
            if (IsWorldReleaseCandidate(dropped, heldObject, pendingDropSceneHandle))
                voices.Play(Milestone2ActionCue.Drop);
        }

        private bool IsWorldReleaseCandidate(WorldInteraction dropped, WorldInteraction heldObject,
            int sceneHandle)
        {
            if (dropped == null || ReferenceEquals(heldObject, dropped) || !dropped.Available || dropped.Owner != null
                || (dropped.Kind & WorldInteractionKind.Grabbable) == 0) return false;
            var scene = source.gameObject.scene;
            return scene.IsValid() && scene.isLoaded && scene.handle == sceneHandle
                && dropped.gameObject.scene == scene;
        }

        private float ObserveJointSpeed(float elapsed)
        {
            Transform nextFrame = ResolveVisualFrame();
            if (nextFrame != visualFrame)
            {
                visualFrame = nextFrame;
                ResetJointBinding();
                ResetHandBinding();
            }
            if (leftUpper == null || leftLower == null || rightUpper == null || rightLower == null)
            {
                hasJointBaseline = false;
                jointBindingRetryRemaining = Mathf.Max(0f, jointBindingRetryRemaining - elapsed);
                if (jointBindingRetryRemaining > 0f) return 0f;
                jointBindingRetryRemaining = JointBindingRetrySeconds;
                // These are existing public visual transforms, cached between binds.
                leftUpper = visualFrame.Find("Left Mechanical Arm/Upper Arm");
                leftLower = visualFrame.Find("Left Mechanical Arm/Lower Arm");
                rightUpper = visualFrame.Find("Right Mechanical Arm/Upper Arm");
                rightLower = visualFrame.Find("Right Mechanical Arm/Lower Arm");
                if (leftUpper == null || leftLower == null || rightUpper == null || rightLower == null)
                    return 0f;
            }

            float leftLowerAngle = leftLower.localEulerAngles.z;
            float rightLowerAngle = rightLower.localEulerAngles.z;
            var angles = new JointAngles
            {
                LeftShoulder = leftLowerAngle,
                LeftElbow = leftUpper.localEulerAngles.z - leftLowerAngle,
                RightShoulder = rightLowerAngle,
                RightElbow = rightUpper.localEulerAngles.z - rightLowerAngle
            };
            float speed = hasJointBaseline && elapsed > DeploymentEpsilon
                ? Mathf.Max(
                    Mathf.Max(Mathf.Abs(Mathf.DeltaAngle(previousJointAngles.LeftShoulder, angles.LeftShoulder)),
                        Mathf.Abs(Mathf.DeltaAngle(previousJointAngles.LeftElbow, angles.LeftElbow))),
                    Mathf.Max(Mathf.Abs(Mathf.DeltaAngle(previousJointAngles.RightShoulder, angles.RightShoulder)),
                        Mathf.Abs(Mathf.DeltaAngle(previousJointAngles.RightElbow, angles.RightElbow)))) / elapsed
                : 0f;
            previousJointAngles = angles;
            hasJointBaseline = true;
            return speed;
        }

        private void ResetJointBinding()
        {
            leftUpper = leftLower = rightUpper = rightLower = null;
            jointBindingRetryRemaining = 0f;
            hasJointBaseline = false;
        }

        private void ObserveEmptyHandGrip(WorldInteraction heldObject, float deployment,
            bool mechanismMoving, bool canPlay, float elapsed)
        {
            if (leftHandRenderer == null || rightHandRenderer == null)
            {
                hasHandSpriteBaseline = previousHandContextReady = false;
                handBindingRetryRemaining = Mathf.Max(0f, handBindingRetryRemaining - elapsed);
                if (handBindingRetryRemaining > 0f) return;
                handBindingRetryRemaining = JointBindingRetrySeconds;
                Transform leftHand = visualFrame.Find("Left Mechanical Arm/Mechanical Hand");
                Transform rightHand = visualFrame.Find("Right Mechanical Arm/Mechanical Hand");
                leftHandRenderer = leftHand != null && leftHand.TryGetComponent(out SpriteRenderer leftRenderer)
                    ? leftRenderer : null;
                rightHandRenderer = rightHand != null && rightHand.TryGetComponent(out SpriteRenderer rightRenderer)
                    ? rightRenderer : null;
                if (leftHandRenderer == null || rightHandRenderer == null) return;
            }

            Sprite leftSprite = leftHandRenderer.sprite;
            Sprite rightSprite = rightHandRenderer.sprite;
            bool contextReady = source.IsArmModeActive
                && (source.State == RobotArmState.OuterOperating || source.State == RobotArmState.Docking)
                && deployment >= 1f - DeploymentEpsilon
                && (mover == null || (mover.MovementMode == RobotMovementMode.Driven && !mover.IsPhotoModeInputLocked))
                && leftHandRenderer.enabled && rightHandRenderer.enabled
                && leftHandRenderer.gameObject.activeInHierarchy && rightHandRenderer.gameObject.activeInHierarchy
                && leftSprite != null && rightSprite != null;
            bool emptyBeforeAndAfter = ReferenceEquals(previousHeldObject, null) && ReferenceEquals(heldObject, null);
            if (canPlay && hasHandSpriteBaseline && previousHandContextReady && contextReady
                && emptyBeforeAndAfter && !mechanismMoving
                && (leftSprite != previousLeftHandSprite || rightSprite != previousRightHandSprite))
            {
                // SetHandGrip changes both visible sprites on the actual edge.
                // One event covers either direction and retriggers even while alive.
                voices.Play(Milestone2ActionCue.HandClose);
            }
            previousLeftHandSprite = leftSprite;
            previousRightHandSprite = rightSprite;
            previousHandContextReady = contextReady;
            hasHandSpriteBaseline = true;
        }

        private void ResetHandBinding()
        {
            leftHandRenderer = rightHandRenderer = null;
            previousLeftHandSprite = previousRightHandSprite = null;
            handBindingRetryRemaining = 0f;
            hasHandSpriteBaseline = previousHandContextReady = false;
        }

        private struct JointAngles
        {
            internal float LeftShoulder, LeftElbow, RightShoulder, RightElbow;
        }

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
