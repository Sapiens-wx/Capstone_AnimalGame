using System.Collections.Generic;
using AnimalGame.Demos.WasteRobot;
using AnimalGame.Garbage;
using AnimalGame.MapTest;
using AnimalGame.RobotArm;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEngine;

namespace Capstone.Audio
{
    // Use a unique body sweep to attribute a hard stop, or confirm a movable
    // medium prop from its actual displacement during a committed body movement.
    internal sealed class Milestone3TerrainCollisionAudio
    {
        private const float MinimumSpeed = .01f;
        private const float PositionToleranceSquared = .00000001f;
        private const float MaximumFrameRotation = 5f;
        // The push plan leaves about .00001 map metres of clearance after a commit.
        private const float ContactClearance = .001f;
        private const double RuinImpactCooldownSeconds = .35;
        private const double GarbageImpactCooldownSeconds = .5;
        private readonly Milestone3AudioSettings settings;
        private readonly Milestone3AudioVoices voices;
        private readonly List<WorldInteraction> contacts = new List<WorldInteraction>(32);
        private RobotMover mover;
        private RobotArmController arm;
        private MapTestSceneController map;
        private Vector2 lastPosition, samplePosition, sampleVelocity;
        private Quaternion sampleRotation;
        private float sampleDeltaTime, sampleSpeed, sampleDriveSpeed, sampleReferenceSpeed;
        private double nextRuinImpactTime, nextGarbageImpactTime;
        private int baselineFrame = -1, sampleFrame = -1;
        private bool wasBlocked;
        private readonly bool observeGarbage;
        private WorldInteraction mediumCandidate, touchingGarbage;
        private float releasePenetration;
        private Vector2 mediumStart;
        private InteractionShape mediumStartShape;

        internal Milestone3TerrainCollisionAudio(Milestone3AudioSettings settings, Milestone3AudioVoices voices)
        {
            this.settings = settings;
            this.voices = voices;
            observeGarbage = settings.HasCue(Milestone3Cue.BigGarbageImpact);
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
            mediumCandidate = null;
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
            if (!observeGarbage) return;
            if (touchingGarbage != null && touchingGarbage.Available && touchingGarbage.MotionOwner == null
                && WorldInteractionQuery.Penetration(BodyAt(position), touchingGarbage.GetShape(map)) >= releasePenetration)
                return;
            touchingGarbage = null;
            mover.QueryCollisionSweep(position, position + velocity * deltaTime, map, contacts);
            if (contacts.Count == 1 && (contacts[0].Kind & WorldInteractionKind.Pushable) != 0
                && IsFreeMedium(contacts[0]))
            {
                mediumCandidate = contacts[0];
                mediumStart = mediumCandidate.transform.position;
                mediumStartShape = mediumCandidate.GetShape(map);
            }
            contacts.Clear();
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
            if (observedFrame != Time.frameCount || (!newStop && mediumCandidate == null)
                || Quaternion.Angle(sampleRotation, mover.transform.rotation) > MaximumFrameRotation) return;
            // A new reverse intent can move the private integration through zero
            // before the stop. Its old sampled path could then identify a blocker
            // on the opposite side. Conservatively reject active reversal rather
            // than assigning that material to the failed movement transaction.
            if (sampleDriveSpeed * mover.CurrentThrottleIntent < 0f
                || Vector2.Dot(sampleVelocity, mover.UnresistedMovementIntentWorld) < 0f) return;

            Vector2 bodyTravel = position - samplePosition;
            if (!newStop)
            {
                // Check the committed body path and the prop's actual displacement.
                // A speed probe alone can reach a prop even when resistance stops the
                // robot short. The old shape must now overlap the body, and the moved
                // prop must end at its surface, in the same direction as the body.
                if (blocked || !IsFreeMedium(mediumCandidate) || Vector2.Dot(bodyTravel, sampleVelocity) <= 0f
                    || Vector2.Dot((Vector2)mediumCandidate.transform.position - mediumStart, bodyTravel) <= 0f) return;
                InteractionShape body = BodyAt(position);
                float depth = WorldInteractionQuery.Penetration(body, mediumCandidate.GetShape(map));
                if (Mathf.Abs(depth) <= ContactClearance && WorldInteractionQuery.Penetration(body, mediumStartShape) > 0f)
                    PlayGarbageImpact(mediumCandidate, depth);
                return;
            }
            if (bodyTravel.sqrMagnitude > PositionToleranceSquared) return;
            mover.QueryCollisionSweep(samplePosition, samplePosition + sampleVelocity * sampleDeltaTime, map, contacts);
            // An unknown or second blocker must never be discarded to select a
            // convenient known material; the failed transaction is ambiguous.
            if (contacts.Count != 1) { contacts.Clear(); return; }
            WorldInteraction contact = contacts[0];
            contacts.Clear();
            if (contact == null || !contact.Available || contact.Owner != null
                || contact.gameObject.scene != mover.gameObject.scene) return;
            if (observeGarbage && IsFreeMedium(contact))
            {
                PlayGarbageImpact(contact, WorldInteractionQuery.Penetration(BodyAt(position), contact.GetShape(map)));
                return;
            }
            if ((contact.Kind & (WorldInteractionKind.Pushable | WorldInteractionKind.Actor)) != 0) return;
            TreeHabitat tree = contact.GetComponentInParent<TreeHabitat>();
            WasteRobotBase machine = contact.GetComponentInParent<WasteRobotBase>();
            if (tree != null && machine != null) return;
            Milestone3Cue cue;
            if (Usable(tree))
            {
                cue = tree.IsDead ? Milestone3Cue.DeadTreeImpact : Milestone3Cue.LiveTreeImpact;
            }
            else if (Usable(machine))
            {
                double now = Time.timeAsDouble;
                if (now >= nextRuinImpactTime && settings.HasCue(Milestone3Cue.RuinImpact)
                    && voices.Play(Milestone3Cue.RuinImpact))
                    nextRuinImpactTime = now + RuinImpactCooldownSeconds;
                return;
            }
            else if (observeGarbage && contact.TryGetComponent(out GarbageItem garbage) && Usable(garbage)
                && garbage.Size == RecyclableSize.Big && contact.MotionOwner == null)
            {
                PlayGarbageImpact(contact, WorldInteractionQuery.Penetration(BodyAt(position), contact.GetShape(map)));
                return;
            }
            else return;
            if (settings.HasCue(cue))
                voices.Play(cue, Mathf.Clamp(sampleSpeed / sampleReferenceSpeed, .08f, 1f));
        }

        private InteractionShape BodyAt(Vector2 position) =>
            mover.GetCollisionShapeAt(position, mover.transform.rotation, map, mover.CollisionSkin);

        private bool IsFreeMedium(WorldInteraction item) => item != null && item.Available
            && item.gameObject.scene == mover.gameObject.scene && (item.Kind & WorldInteractionKind.Actor) == 0
            && item.TryGetComponent(out GarbageItem garbage) && Usable(garbage) && garbage.Size == RecyclableSize.Medium
            && item.MotionOwner == null;

        private void PlayGarbageImpact(WorldInteraction item, float depth)
        {
            if (touchingGarbage == item) return;
            touchingGarbage = item;
            // A rejected move leaves the body just short of the prop. Keep that
            // observed gap as the separation baseline, including for muted contacts.
            releasePenetration = Mathf.Min(0f, depth) - ContactClearance;
            double now = Time.timeAsDouble;
            if (now >= nextGarbageImpactTime && voices.Play(Milestone3Cue.BigGarbageImpact,
                Mathf.Clamp(sampleSpeed / sampleReferenceSpeed, .08f, 1f)))
                nextGarbageImpactTime = now + GarbageImpactCooldownSeconds;
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
            mediumCandidate = touchingGarbage = null;
            contacts.Clear();
            if (!stopVoices) return;
            voices.Stop(Milestone3Cue.DeadTreeImpact, true);
            voices.Stop(Milestone3Cue.LiveTreeImpact, true);
            voices.Stop(Milestone3Cue.RuinImpact, true);
            voices.Stop(Milestone3Cue.BigGarbageImpact, true);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Usable(Behaviour source) => source != null && source.isActiveAndEnabled
            && source.gameObject.scene.IsValid() && source.gameObject.scene.isLoaded;
    }
}
