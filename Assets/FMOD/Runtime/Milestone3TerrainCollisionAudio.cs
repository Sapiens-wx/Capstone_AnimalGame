using System.Collections.Generic;
using AnimalGame.Demos.WasteRobot;
using AnimalGame.MapTest;
using AnimalGame.RobotArm;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEngine;

namespace Capstone.Audio
{
    // Confirm a new gameplay obstacle stop first, then attribute a unique direct
    // body blocker using public geometry and the sampled incoming velocity.
    // This is conservative observation, not the mover's private contact list:
    // deployed arms, held items, ambiguous sweeps and changed poses stay silent.
    internal sealed class Milestone3TerrainCollisionAudio
    {
        private const float MinimumSpeed = .01f;
        private const float PositionToleranceSquared = .00000001f;
        private const float MaximumFrameRotation = 5f;
        private readonly Milestone3AudioSettings settings;
        private readonly Milestone3AudioVoices voices;
        private readonly List<WorldInteraction> contacts = new List<WorldInteraction>(32);
        private RobotMover mover;
        private RobotArmController arm;
        private MapTestSceneController map;
        private Vector2 lastPosition, samplePosition, sampleVelocity;
        private Quaternion sampleRotation;
        private float sampleDeltaTime, sampleSpeed, sampleDriveSpeed, sampleReferenceSpeed;
        private int baselineFrame = -1, sampleFrame = -1;
        private bool wasBlocked;

        internal Milestone3TerrainCollisionAudio(Milestone3AudioSettings settings, Milestone3AudioVoices voices)
        {
            this.settings = settings;
            this.voices = voices;
        }

        internal void Bind(RobotMover source, RobotArmController armSource)
        {
            if (ReferenceEquals(mover, source) && ReferenceEquals(arm, armSource)) return;
            ResetObservation(true);
            mover = source;
            arm = armSource;
            map = null;
            if (!Usable(mover)) return;
            foreach (var candidate in Object.FindObjectsByType<MapTestSceneController>(FindObjectsSortMode.None))
                if (Usable(candidate) && candidate.gameObject.scene == mover.gameObject.scene)
                { map = candidate; break; }
            wasBlocked = IsObstacleBlocked();
        }

        internal void CaptureBeforeMotion(float deltaTime)
        {
            sampleFrame = -1;
            if (deltaTime <= 0f || !CanObserve() || baselineFrame != Time.frameCount - 1
                || IsObstacleBlocked()) return;
            Vector2 position = mover.transform.position;
            // Includes relocation before this callback, not just after it.
            if ((position - lastPosition).sqrMagnitude > PositionToleranceSquared) return;
            Vector2 velocity = (Vector2)mover.transform.up * mover.CurrentSpeed + mover.CurrentTerrainVelocity;
            float speed = velocity.magnitude;
            float reference = mover.DefaultMoveSpeed;
            if (!Finite(speed) || speed <= MinimumSpeed || !Finite(reference) || reference <= 0f) return;
            samplePosition = position;
            sampleRotation = mover.transform.rotation;
            sampleVelocity = velocity;
            sampleSpeed = speed;
            sampleDriveSpeed = mover.CurrentSpeed;
            sampleReferenceSpeed = reference;
            sampleDeltaTime = deltaTime;
            sampleFrame = Time.frameCount;
        }

        internal void Tick(float deltaTime)
        {
            if (deltaTime <= 0f || !Usable(mover)) { ResetObservation(true); return; }
            if (!CanObserve()) { ResetObservation(false); return; }
            bool blocked = IsObstacleBlocked();
            bool newStop = blocked && !wasBlocked;
            wasBlocked = blocked;
            Vector2 position = mover.transform.position;
            lastPosition = position;
            baselineFrame = Time.frameCount;
            int observedFrame = sampleFrame;
            sampleFrame = -1;
            if (!newStop || observedFrame != Time.frameCount
                || (position - samplePosition).sqrMagnitude > PositionToleranceSquared
                || Quaternion.Angle(sampleRotation, mover.transform.rotation) > MaximumFrameRotation) return;
            // A new reverse intent can move the private integration through zero
            // before the stop. Its old sampled path could then identify a blocker
            // on the opposite side. Conservatively reject active reversal rather
            // than assigning that material to the failed movement transaction.
            if (sampleDriveSpeed * mover.CurrentThrottleIntent < 0f
                || Vector2.Dot(sampleVelocity, mover.UnresistedMovementIntentWorld) < 0f) return;

            mover.QueryCollisionSweep(samplePosition, samplePosition + sampleVelocity * sampleDeltaTime, map, contacts);
            // An unknown or second blocker must never be discarded to select a
            // convenient known material; the failed transaction is ambiguous.
            if (contacts.Count != 1) { contacts.Clear(); return; }
            WorldInteraction contact = contacts[0];
            contacts.Clear();
            if (contact == null || !contact.Available || contact.Owner != null
                || contact.gameObject.scene != mover.gameObject.scene
                || (contact.Kind & (WorldInteractionKind.Pushable | WorldInteractionKind.Actor)) != 0) return;
            TreeHabitat tree = contact.GetComponentInParent<TreeHabitat>();
            WasteRobotBase machine = contact.GetComponentInParent<WasteRobotBase>();
            if (tree != null && machine != null) return;
            Milestone3Cue cue;
            float gain;
            if (Usable(tree))
            {
                cue = tree.IsDead ? Milestone3Cue.DeadTreeImpact : Milestone3Cue.LiveTreeImpact;
                gain = Mathf.Clamp(sampleSpeed / sampleReferenceSpeed, .08f, 1f);
            }
            else if (Usable(machine))
            {
                cue = Milestone3Cue.RuinImpact;
                gain = 1f;
            }
            else return;
            if (settings.HasCue(cue)) voices.Play(cue, gain);
        }

        private bool CanObserve() => Usable(mover) && Usable(arm) && arm.gameObject == mover.gameObject
            && Usable(map) && map.HasGeneratedMap && map.gameObject.scene == mover.gameObject.scene
            && mover.MovementMode == RobotMovementMode.Driven && !mover.IgnoreCollisionAndTerrain
            && arm.State == RobotArmState.Retracted && arm.VisibleDeployment01 <= 0f && arm.HeldObject == null;

        private bool IsObstacleBlocked() => mover.IsSlopeBlocked && mover.CurrentTraversalResult.HasData
            && mover.CurrentTraversalResult.BlockReason == TraversalBlockReason.Obstacle;

        private void ResetObservation(bool stopVoices)
        {
            sampleFrame = baselineFrame = -1;
            wasBlocked = false;
            contacts.Clear();
            if (!stopVoices) return;
            voices.Stop(Milestone3Cue.DeadTreeImpact, true);
            voices.Stop(Milestone3Cue.LiveTreeImpact, true);
            voices.Stop(Milestone3Cue.RuinImpact, true);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Usable(Behaviour source) => source != null && source.isActiveAndEnabled
            && source.gameObject.scene.IsValid() && source.gameObject.scene.isLoaded;
    }
}
