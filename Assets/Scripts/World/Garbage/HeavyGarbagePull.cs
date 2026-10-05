using AnimalGame.RobotArm;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEngine;

namespace AnimalGame.Garbage
{
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WorldInteraction), typeof(GarbageFragmentSpawner), typeof(HeavyGarbageVisual))]
    public sealed class HeavyGarbagePull : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float minimumPullInput = .25f;
        [SerializeField, Min(.01f)] private float requiredPullIntentDistance = 2f;
        [SerializeField, Min(.01f)] private float requiredPullDuration = 3f;
        [Header("Retreat and tension")]
        [SerializeField, Min(0f)] private float maximumRetreatOfBodyDiameter = .3f;
        [SerializeField, Min(0f)] private float retreatSpeedOfBodyDiameter = .15f;
        [SerializeField, Range(0f, 1f)] private float gripRetreatRatio = .7f;
        [SerializeField, Range(0f, 1f)] private float releaseSpeedMultiplier = 1f;
        [SerializeField, Min(.01f)] private float releaseDecayDuration = .25f;
        [SerializeField, Min(.01f)] private float cancelReturnDuration = .2f;
        [SerializeField, Range(0f, 1f)] private float maximumRumbleStrength = .65f;
        private WorldInteraction item;
        private GarbageFragmentSpawner fragments;
        private RobotArmController arm;
        private RobotMover mover;
        private RobotCameraShake cameraShake;
        private HeavyGarbageVisual visual;
        private float pullTime;
        private float pullDistance;
        private float progress;
        private float storedPullSpeed;
        private float visualProgress;
        private float bodyDiameter;
        private float returnStartProgress, returnTime;
        private Vector2 bodyStart, gripStart, pullDirection;
        private bool pulling, returning, previewReady;
        private bool gripSession;
        private bool completed;

        public float Progress => progress;
        public float StoredPullSpeed => storedPullSpeed;
        public bool HasGripAnchor => pulling;
        public bool IsReturningGrip => returning && item != null && item.Owner == arm && mover != null;
        public Vector2 GripAnchorWorld => gripStart + pullDirection *
            (Mathf.Clamp(Vector2.Dot((Vector2)mover.transform.position - bodyStart, pullDirection),
                0f, bodyDiameter * maximumRetreatOfBodyDiameter) * gripRetreatRatio
                * (returning && returnStartProgress > 0f ? visualProgress / returnStartProgress : 1f));

        private void Awake()
        {
            item = GetComponent<WorldInteraction>();
            fragments = GetComponent<GarbageFragmentSpawner>();
            visual = GetComponent<HeavyGarbageVisual>();
        }

        // The arm checks body/arm collisions in LateUpdate before we advance tension.
        private void LateUpdate() => Step(Time.deltaTime);

        private void Step(float deltaTime)
        {
            if (completed) return;
            if (item == null || item.Owner == null)
            {
                gripSession = false;
                CancelPull(deltaTime);
                return;
            }
            if (arm == null || item.Owner != arm)
            {
                gripSession = false;
                arm = item.Owner as RobotArmController;
                mover = arm != null ? arm.GetComponent<RobotMover>() : null;
                if (cameraShake == null && arm != null)
                    cameraShake = FindFirstObjectByType<RobotCameraShake>();
            }
            if (arm == null || mover == null || arm.HeldObject != item
                || mover.MovementMode != RobotMovementMode.Driven
                || mover.CurrentThrottleIntent >= -minimumPullInput)
            {
                CancelPull(deltaTime);
                return;
            }
            Vector2 away = (Vector2)(mover.transform.position - transform.position);
            if (away.sqrMagnitude < .000001f) { CancelPull(deltaTime); return; }
            away.Normalize();
            float effectiveSpeed = Vector2.Dot(mover.UnresistedMovementIntentWorld, away);
            if (effectiveSpeed <= minimumPullInput)
            {
                CancelPull(deltaTime);
                return;
            }
            float dt = Mathf.Max(0f, deltaTime);
            if (!pulling)
            {
                FinishReturn();
                pulling = true;
                if (!gripSession)
                {
                    bodyStart = mover.transform.position;
                    gripStart = (arm.LeftHandWorld + arm.RightHandWorld) * .5f;
                    pullDirection = away;
                    gripSession = true;
                }
                RobotMarkerView marker = mover.GetComponent<RobotMarkerView>();
                bodyDiameter = marker != null ? marker.BodyDiameter : .72f;
                if (marker != null && marker.MarkerVisualRoot != null)
                    bodyDiameter *= Mathf.Abs(marker.MarkerVisualRoot.lossyScale.x);
                visual.SetPull(gripStart, pullDirection, 0f, 1f);
                previewReady = fragments.BeginPullPreview(arm, pullDirection);
            }
            if (!previewReady && fragments.HasPullFragmentPrefabs)
            {
                // A blocked fragment layout must never make an intact obstacle invisible.
                CancelPull(dt);
                return;
            }
            if (Vector2.Dot(mover.UnresistedMovementIntentWorld, pullDirection) <= minimumPullInput)
            {
                CancelPull(deltaTime);
                return;
            }
            if (mover.HeavyPullMovementBlocked || arm.IsBlocked)
            {
                float pendingRetreat = bodyDiameter * maximumRetreatOfBodyDiameter * Mathf.SmoothStep(0f, 1f, progress);
                mover.SetHeavyPullConstraint(this, bodyStart + pullDirection * pendingRetreat,
                    pullDirection, bodyDiameter * retreatSpeedOfBodyDiameter);
                if (cameraShake != null) cameraShake.SetGarbagePullRumble(0f);
                return;
            }
            pullTime += dt;
            pullDistance += effectiveSpeed * dt;
            progress = Mathf.Clamp01(Mathf.Min(pullTime / requiredPullDuration,
                pullDistance / requiredPullIntentDistance));
            visualProgress = previewReady ? progress : 0f;
            float maximumStoredSpeed = mover.UnloadedReverseSpeed * releaseSpeedMultiplier;
            storedPullSpeed = Mathf.MoveTowards(storedPullSpeed,
                maximumStoredSpeed * Mathf.Clamp01(-mover.CurrentThrottleIntent),
                maximumStoredSpeed / Mathf.Max(.01f, requiredPullDuration) * dt);
            float retreat = bodyDiameter * maximumRetreatOfBodyDiameter * Mathf.SmoothStep(0f, 1f, progress);
            mover.SetHeavyPullConstraint(this, bodyStart + pullDirection * retreat,
                pullDirection, bodyDiameter * retreatSpeedOfBodyDiameter);
            UpdateVisual(previewReady ? progress : 0f);
            if (cameraShake != null) cameraShake.SetGarbagePullRumble(maximumRumbleStrength * progress);
            if (pullTime < requiredPullDuration || pullDistance < requiredPullIntentDistance) return;
            if (!previewReady) return;
            RobotMover releaseMover = mover;
            RobotCameraShake releaseFeedback = cameraShake;
            Vector2 releaseDirection = pullDirection;
            float releaseSpeed = storedPullSpeed;
            completed = true;
            if (!fragments.CompletePullBreak())
            {
                completed = false;
                CancelPull(dt);
                return;
            }
            releaseMover.ClearHeavyPullConstraint(this);
            releaseMover.ReleaseHeavyPullVelocity(releaseDirection, releaseSpeed, releaseDecayDuration);
            // Successful splitting disables this object synchronously. The camera owns
            // the recoil pulse so it can finish after the source and its pull rumble disappear.
            if (releaseFeedback != null && releaseFeedback.FollowsRobot(releaseMover))
                releaseFeedback.PlayHeavyGarbageBreakFeedback(releaseDirection);
        }

        private void UpdateVisual(float value)
        {
            Vector2 anchor = arm != null ? (arm.LeftHandWorld + arm.RightHandWorld) * .5f : gripStart;
            float stretch = Mathf.Max(0f, Vector2.Dot(anchor - gripStart, pullDirection));
            visual.SetPull(gripStart, pullDirection, stretch, 1f - value);
            fragments.UpdatePullPreview(value, anchor);
        }

        private void CancelPull(float deltaTime)
        {
            if (pulling)
            {
                returning = true;
                returnStartProgress = visualProgress;
                returnTime = 0f;
            }
            pulling = false;
            pullTime = 0f;
            pullDistance = 0f;
            progress = 0f;
            storedPullSpeed = 0f;
            if (mover != null) mover.ClearHeavyPullConstraint(this);
            if (cameraShake != null) cameraShake.SetGarbagePullRumble(0f);
            if (!returning) return;
            returnTime += Mathf.Max(0f, deltaTime);
            visualProgress = returnStartProgress * (1f - Mathf.Clamp01(returnTime / cancelReturnDuration));
            Vector2 anchor = arm != null ? (arm.LeftHandWorld + arm.RightHandWorld) * .5f : gripStart;
            float stretch = Mathf.Max(0f, Vector2.Dot(anchor - gripStart, pullDirection))
                            * (returnStartProgress > 0f ? visualProgress / returnStartProgress : 0f);
            visual.SetPull(gripStart, pullDirection, stretch, 1f - visualProgress);
            fragments.UpdatePullPreview(visualProgress, anchor);
            if (visualProgress <= 0f) FinishReturn();
        }

        private void FinishReturn()
        {
            returning = false;
            previewReady = false;
            visualProgress = 0f;
            if (fragments != null) fragments.CancelPullPreview();
            if (visual != null) visual.ResetVisual();
        }

        private void OnDisable()
        {
            pulling = false;
            gripSession = false;
            pullTime = pullDistance = progress = storedPullSpeed = 0f;
            if (mover != null) mover.ClearHeavyPullConstraint(this);
            if (cameraShake != null) cameraShake.SetGarbagePullRumble(0f);
            FinishReturn();
        }

        private void OnDestroy() => OnDisable();
    }
}
