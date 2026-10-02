using AnimalGame.RobotArm;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEngine;

namespace AnimalGame.Garbage
{
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WorldInteraction), typeof(GarbageFragmentSpawner))]
    public sealed class HeavyGarbagePull : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float minimumPullInput = .25f;
        [SerializeField, Min(.01f)] private float requiredPullIntentDistance = 2f;
        [SerializeField, Min(.01f)] private float requiredPullDuration = 1f;
        [SerializeField, Range(0f, 1f)] private float maximumRumbleStrength = .65f;
        private WorldInteraction item;
        private GarbageFragmentSpawner fragments;
        private RobotArmController arm;
        private RobotMover mover;
        private RobotCameraShake cameraShake;
        private float pullTime;
        private float pullDistance;
        private bool completed;

        private void Awake()
        {
            item = GetComponent<WorldInteraction>();
            fragments = GetComponent<GarbageFragmentSpawner>();
        }

        private void Update() => Step(Time.deltaTime);

        private void Step(float deltaTime)
        {
            if (completed) return;
            if (item == null || item.Owner == null)
            {
                ResetPull();
                return;
            }
            if (arm == null || item.Owner != arm)
            {
                arm = item.Owner as RobotArmController;
                mover = arm != null ? arm.GetComponent<RobotMover>() : null;
                if (cameraShake == null && arm != null)
                    cameraShake = FindFirstObjectByType<RobotCameraShake>();
            }
            if (arm == null || mover == null || arm.HeldObject != item
                || mover.MovementMode != RobotMovementMode.Driven
                || mover.CurrentThrottleIntent >= -minimumPullInput)
            {
                ResetPull();
                return;
            }
            Vector2 away = (Vector2)(mover.transform.position - transform.position);
            if (away.sqrMagnitude < .000001f) { ResetPull(); return; }
            away.Normalize();
            float effectiveSpeed = Vector2.Dot(mover.UnresistedMovementIntentWorld, away);
            if (effectiveSpeed <= minimumPullInput)
            {
                ResetPull();
                return;
            }
            float dt = Mathf.Max(0f, deltaTime);
            pullTime += dt;
            pullDistance += effectiveSpeed * dt;
            float progress = Mathf.Min(pullTime / requiredPullDuration,
                pullDistance / requiredPullIntentDistance);
            if (cameraShake != null) cameraShake.SetGarbagePullRumble(maximumRumbleStrength * Mathf.Clamp01(progress));
            if (pullTime < requiredPullDuration || pullDistance < requiredPullIntentDistance) return;
            completed = fragments.BeginBreak(true);
            ResetPull();
        }

        private void ResetPull()
        {
            pullTime = 0f;
            pullDistance = 0f;
            if (cameraShake != null) cameraShake.SetGarbagePullRumble(0f);
        }

        private void OnDisable() => ResetPull();
        private void OnDestroy() => ResetPull();
    }
}
