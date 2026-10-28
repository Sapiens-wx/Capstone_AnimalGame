using System.Collections.Generic;
using AnimalGame.Animals;
using AnimalGame.Garbage;
using AnimalGame.MapTest;
using AnimalGame.RobotArm;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEngine;

namespace Capstone.Audio
{
    // Observe public state only. A heavy conversion requires a valid pull session,
    // an inactive retired source and its owned medium replacement together.
    internal sealed class Milestone3WorldAudio
    {
        private const int MaximumTrackedGarbage = 32;
        private struct TrackedGarbage
        {
            internal WorldInteraction Item;
            internal GarbageMotion Motion;
            internal GarbageSlide Slide;
            internal Vector2 Position;
            internal float Speed, DistanceSquared, PreMotionSpeed;
            internal int MotionSampleFrame;
            internal bool WasBlocked, WasFree, Seen, PreMotionFree;
        }

        private readonly Milestone3AudioSettings settings;
        private readonly Milestone3AudioVoices voices;
        private readonly bool observeHeavy, observeLoose, observeClimb;
        private readonly List<WorldInteraction> nearby = new List<WorldInteraction>(64);
        private readonly TrackedGarbage[] tracked = new TrackedGarbage[MaximumTrackedGarbage];
        private RobotMover mover;
        private RobotArmController arm;
        private RobotHeightMotionDetector height;
        private MapTestSceneController map;
        private WorldInteraction heavyItem, surfaceItem;
        private HeavyGarbagePull pull;
        private GarbageFragmentSpawner fragments;
        private Milestone3HeavyBreakObserver breakObserver;
        private bool surfaceIsBush, surfaceIsSmallGarbage;
        private bool hasBaseline, wasPulling, pullStarted;
        private Vector2 previousPosition;
        private float previousAngle, bodyRadius, previousProgress, discoveryRemaining;
        private int heightLandingFrame = -1, climbLandingFrame = -1;
        private bool slideEpisode;

        internal bool NeedsMotionSampling => observeLoose;

        internal Milestone3WorldAudio(Milestone3AudioSettings settings, Milestone3AudioVoices voices)
        {
            this.settings = settings;
            this.voices = voices;
            observeHeavy = settings.HasCue(Milestone3Cue.HeavyPullLoop)
                || settings.HasCue(Milestone3Cue.HeavyTearMiddle) || settings.HasCue(Milestone3Cue.HeavyTearLate)
                || settings.HasCue(Milestone3Cue.HeavyBreak) || settings.HasCue(Milestone3Cue.FragmentScatter);
            observeLoose = settings.HasCue(Milestone3Cue.GarbageSlide) || settings.HasCue(Milestone3Cue.GarbageImpact);
            observeClimb = settings.HasCue(Milestone3Cue.BushRustle)
                || settings.HasCue(Milestone3Cue.SmallGarbageClimb) || settings.HasCue(Milestone3Cue.ClimbableLand);
        }

        internal void Bind(RobotMover source, RobotArmController armSource)
        {
            if (ReferenceEquals(mover, source) && ReferenceEquals(arm, armSource)) return;
            if (!ReferenceEquals(height, null)) height.Landed -= OnHeightLanded;
            StopAllImmediately();
            mover = source;
            arm = armSource;
            height = null;
            map = null;
            if (!Usable(mover) || (!observeHeavy && !observeLoose && !observeClimb)) return;
            if (observeClimb)
            {
                mover.TryGetComponent(out RobotMarkerView marker);
                bodyRadius = (marker != null ? marker.BodyDiameter * .5f : .75f)
                    * Mathf.Max(Mathf.Abs(mover.transform.lossyScale.x), Mathf.Abs(mover.transform.lossyScale.y));
                mover.TryGetComponent(out height);
                if (height != null) height.Landed += OnHeightLanded;
            }
            // Once per robot binding, only when nearby garbage cues actually have events.
            // Per-frame observation uses the existing spatial index and fixed tracked slots.
            if (observeLoose)
                foreach (var candidate in Object.FindObjectsByType<MapTestSceneController>(FindObjectsSortMode.None))
                    if (Usable(candidate) && candidate.gameObject.scene == mover.gameObject.scene)
                    { map = candidate; break; }
        }

        internal void Tick(float deltaTime)
        {
            if (!observeHeavy && !observeLoose && !observeClimb) return;
            if (deltaTime <= 0f || !Usable(mover)) { StopAllImmediately(); return; }
            bool first = !hasBaseline;
            Vector2 position = mover.transform.position;
            float angle = mover.transform.eulerAngles.z;
            float distance = first ? 0f : Vector2.Distance(position, previousPosition);
            float turnDistance = first ? 0f : Mathf.Abs(Mathf.DeltaAngle(previousAngle, angle))
                * Mathf.Deg2Rad * bodyRadius;
            previousPosition = position;
            previousAngle = angle;
            hasBaseline = true;
            if (observeHeavy) TickHeavy(first);
            if (observeLoose) TickLoose(deltaTime);
            if (observeClimb) TickClimb((distance + turnDistance) / deltaTime, first);
        }

        private void TickHeavy(bool first)
        {
            WorldInteraction held = Usable(arm) && arm.gameObject == mover.gameObject ? arm.HeldObject : null;
            if (held != heavyItem)
            {
                DetachBreakObserver();
                StopHeavyLoops(true);
                heavyItem = held;
                pull = held != null ? held.GetComponent<HeavyGarbagePull>() : null;
                fragments = pull != null ? held.GetComponent<GarbageFragmentSpawner>() : null;
                previousProgress = 0f;
                wasPulling = pullStarted = false;
                if (Usable(pull) && fragments != null)
                {
                    if (!held.TryGetComponent(out breakObserver))
                        breakObserver = held.gameObject.AddComponent<Milestone3HeavyBreakObserver>();
                    breakObserver.Bind(this, held);
                }
            }
            if (!Usable(pull) || held == null || !held.Available || held.Owner != arm
                || held.gameObject.scene != mover.gameObject.scene || mover.MovementMode != RobotMovementMode.Driven)
            {
                StopHeavyLoops(true);
                if (breakObserver != null) breakObserver.Observe(false);
                previousProgress = 0f;
                wasPulling = pullStarted = false;
                return;
            }
            bool pulling = pull.HasGripAnchor;
            bool blocked = pulling && (mover.HeavyPullMovementBlocked || arm.IsBlocked);
            float progress = Mathf.Clamp01(pull.Progress);
            bool advancing = pulling && !blocked && progress > previousProgress + settings.ProgressEpsilon;
            // The preview contains visual-only objects; it merely proves this held
            // heavy object has a valid pull plan, never that a split has succeeded.
            if (breakObserver != null)
                breakObserver.Observe(pulling && fragments != null && fragments.PullPreviewCount >= 3);
            if (first)
            {
                // Binding/resuming during a pull establishes a baseline without an old onset.
                previousProgress = progress;
                wasPulling = pulling;
                pullStarted = false;
                return;
            }
            if (!pulling)
            {
                if (wasPulling) StopHeavyLoops(false);
                pullStarted = false;
            }
            else
            {
                if (advancing && !pullStarted)
                {
                    // A new attempt may start before the cancelled attempt's short
                    // fade finishes. Restart from its new progress window in that case.
                    StopHeavyLoops(true);
                    pullStarted = true;
                }
                if (pullStarted)
                {
                    float middle = Mathf.Clamp01((progress - .30f) / .10f);
                    float late = Mathf.Clamp01((progress - .65f) / .10f);
                    // Adjacent source windows crossfade at their original rate. No
                    // timeline advance can accidentally reach the final snap sample.
                    SetHeavyStage(Milestone3Cue.HeavyPullLoop, 1f - middle, !advancing);
                    SetHeavyStage(Milestone3Cue.HeavyTearMiddle, middle * (1f - late), !advancing);
                    SetHeavyStage(Milestone3Cue.HeavyTearLate, late, !advancing);
                }
            }
            previousProgress = progress;
            wasPulling = pulling;
        }

        private void SetHeavyStage(Milestone3Cue cue, float weight, bool paused)
        {
            if (weight <= 0f) { voices.Stop(cue, true); return; }
            voices.SetLoop(cue, true, weight);
            voices.SetPaused(cue, paused);
        }

        internal void OnHeavySourceDisabled(Milestone3HeavyBreakObserver observer, WorldInteraction source)
        {
            // CompletePullBreak commits the handoff, retires Kind, spawns its batch,
            // then disables the original synchronously. OnDisable observes this before
            // Destroy or another frame can erase it; component callback order is immaterial.
            if (observer != breakObserver || source == null || source != heavyItem || !wasPulling
                || source.Kind != WorldInteractionKind.None || source.gameObject.activeInHierarchy
                || Time.timeScale <= 0f || !Usable(mover) || !Usable(arm)
                || mover.MovementMode != RobotMovementMode.Driven || arm.gameObject != mover.gameObject
                || source.gameObject.scene != mover.gameObject.scene) return;
            WorldInteraction replacement = arm.HeldObject;
            if (replacement == null || replacement == source || !replacement.Available
                || replacement.Size != RecyclableSize.Medium || replacement.Owner != arm
                || replacement.Kind == WorldInteractionKind.None
                || replacement.gameObject.scene != source.gameObject.scene) return;
            StopHeavyLoops(true);
            wasPulling = pullStarted = false;
            breakObserver = null;
            voices.Play(Milestone3Cue.HeavyBreak);
            voices.Play(Milestone3Cue.FragmentScatter);
        }

        private void DetachBreakObserver()
        {
            if (breakObserver != null) breakObserver.Clear(this);
            breakObserver = null;
        }

        // One audio-only sampler runs after GarbageSlide (20), before GarbageMotion
        // (40). This is the latest public velocity before collision zeroes it; the
        // gameplay component's private acceleration/friction integration is not read.
        internal void CaptureBeforeMotion()
        {
            if (!observeLoose || !Usable(mover) || Time.timeScale <= 0f) return;
            for (int i = 0; i < tracked.Length; i++)
            {
                ref TrackedGarbage entry = ref tracked[i];
                if (!Usable(entry.Item) || !entry.Item.Available || !Usable(entry.Motion)) continue;
                entry.PreMotionSpeed = entry.Motion.Velocity.magnitude;
                entry.PreMotionFree = entry.Item.MotionOwner == null;
                entry.MotionSampleFrame = Time.frameCount;
            }
        }

        private void TickLoose(float deltaTime)
        {
            discoveryRemaining -= deltaTime;
            if (discoveryRemaining <= 0f)
            {
                discoveryRemaining = settings.WorldDiscoveryInterval;
                DiscoverNearby();
            }
            float slideStrength = 0f, impactSpeed = 0f;
            Vector2 slidePosition = default, slideDirection = default;
            Vector2 listener = mover.transform.position;
            float radiusSquared = settings.WorldObservationRadius * settings.WorldObservationRadius;
            for (int i = 0; i < tracked.Length; i++)
            {
                ref TrackedGarbage entry = ref tracked[i];
                if (!Usable(entry.Item) || !entry.Item.Available || !Usable(entry.Motion)
                    || entry.Item.gameObject.scene != mover.gameObject.scene
                    || ((Vector2)entry.Item.transform.position - listener).sqrMagnitude > radiusSquared)
                { entry = default; continue; }
                Vector2 position = entry.Item.transform.position;
                float speed = entry.Motion.Velocity.magnitude;
                bool free = entry.Item.MotionOwner == null;
                bool blocked = entry.Motion.WasBlocked;
                bool sampled = entry.MotionSampleFrame == Time.frameCount;
                float beforeCollision = sampled ? entry.PreMotionSpeed : entry.Speed;
                bool wasFree = sampled ? entry.PreMotionFree : entry.WasFree;
                // WasBlocked persists at rest and on a natural stop. Only a new true
                // edge proves a collision; nonzero velocity becoming zero alone does not.
                if (free && wasFree && blocked && !entry.WasBlocked
                    && beforeCollision > settings.WorldMotionThreshold)
                    impactSpeed = Mathf.Max(impactSpeed, beforeCollision);
                Vector2 displacement = position - entry.Position;
                float movedSpeed = displacement.magnitude / deltaTime;
                if (free && entry.WasFree && !blocked && Usable(entry.Slide)
                    && entry.Item.Size == RecyclableSize.Medium
                    && entry.Item.Kind != WorldInteractionKind.None
                    && speed > settings.WorldMotionThreshold && movedSpeed > settings.WorldMotionThreshold)
                {
                    float strength = Mathf.Clamp01(Mathf.Min(speed, movedSpeed) / settings.WorldReferenceSpeed);
                    if (strength > slideStrength)
                    {
                        slideStrength = strength;
                        slidePosition = position;
                        slideDirection = displacement;
                    }
                }
                entry.Position = position;
                entry.Speed = speed;
                entry.WasBlocked = blocked;
                entry.WasFree = free;
            }
            // Nearby piles share one sustained layer and one strongest collision per frame.
            voices.SetParameter(Milestone3Cue.GarbageSlide, Milestone3Parameter.Speed, slideStrength);
            bool sliding = slideStrength > 0f;
            // Classification is made once when real motion starts, so changing the
            // strongest nearby pile cannot replay the bounce intro every frame.
            bool steep = sliding && !slideEpisode && IsSteepSlide(slidePosition, slideDirection);
            voices.SetGarbageSlide(sliding, steep, slideStrength);
            slideEpisode = sliding;
            if (impactSpeed > 0f) voices.PlayGarbageImpact(impactSpeed);
        }

        private bool IsSteepSlide(Vector2 position, Vector2 direction)
        {
            if (!Usable(map) || !map.HasGeneratedMap || direction.sqrMagnitude <= .000000000001f) return false;
            float probe = settings.WorldSlideProbeMeters;
            Vector2 bearing = direction.normalized;
            float worldDistance = map.MapMetersToWorldDistance(bearing, probe);
            if (worldDistance <= 0f
                || !map.TrySampleWorldPosition(position, out _, out float currentHeight)
                || !map.TrySampleWorldPosition(position + bearing * worldDistance,
                    out _, out float downhillHeight)) return false;
            float drop = currentHeight - downhillHeight;
            return drop >= settings.WorldSlideMinimumDropMeters
                && Mathf.Atan(drop / probe) * Mathf.Rad2Deg >= settings.WorldSlideSteepAngle;
        }

        private void DiscoverNearby()
        {
            Vector2 listener = mover.transform.position;
            float radius = settings.WorldObservationRadius;
            Vector2 center = InteractionShape.ToQuery(listener, map);
            float queryRadius = radius;
            if (map != null && map.HasGeneratedMap)
                queryRadius /= Mathf.Max(.000001f, Mathf.Min(
                    Mathf.Abs(map.MapMetersToWorldDistance(Vector2.right, 1f)),
                    Mathf.Abs(map.MapMetersToWorldDistance(Vector2.up, 1f))));
            WorldInteractionQuery.Query(InteractionShape.Circle(center, queryRadius),
                WorldInteractionKind.Grabbable | WorldInteractionKind.Pushable | WorldInteractionKind.Climbable
                    | WorldInteractionKind.Collision | WorldInteractionKind.BodyCollision,
                map, mover.gameObject.scene, nearby);
            for (int i = 0; i < tracked.Length; i++)
            {
                tracked[i].Seen = false;
                if (tracked[i].Item != null)
                    tracked[i].DistanceSquared = ((Vector2)tracked[i].Item.transform.position - listener).sqrMagnitude;
            }
            foreach (WorldInteraction item in nearby)
            {
                float distance = ((Vector2)item.transform.position - listener).sqrMagnitude;
                if (distance > radius * radius || !item.TryGetComponent<GarbageItem>(out _)
                    || !item.TryGetComponent(out GarbageMotion motion) || !Usable(motion)) continue;
                int slot = -1, farthest = -1;
                float farthestDistance = distance;
                for (int i = 0; i < tracked.Length; i++)
                {
                    if (tracked[i].Item == item) { slot = i; break; }
                    if (tracked[i].Item == null) farthest = i;
                    else if (farthest < 0 || tracked[farthest].Item != null)
                        if (tracked[i].DistanceSquared > farthestDistance)
                        { farthest = i; farthestDistance = tracked[i].DistanceSquared; }
                }
                if (slot < 0)
                {
                    if (farthest < 0) continue;
                    slot = farthest;
                    item.TryGetComponent(out GarbageSlide slide);
                    tracked[slot] = new TrackedGarbage
                    {
                        Item = item, Motion = motion, Slide = slide, Position = item.transform.position,
                        Speed = motion.Velocity.magnitude, WasBlocked = motion.WasBlocked,
                        WasFree = item.MotionOwner == null, DistanceSquared = distance, MotionSampleFrame = -1
                    };
                }
                tracked[slot].Seen = true;
            }
            for (int i = 0; i < tracked.Length; i++)
                if (!tracked[i].Seen) tracked[i] = default;
        }

        private void TickClimb(float speed, bool first)
        {
            WorldInteraction contact = mover.CurrentClimbableSurface.Source;
            if (surfaceItem != contact)
            {
                surfaceItem = contact;
                // Bush contact lives on Solid Core, below the plant's food source.
                AnimalFoodSource food = contact != null ? contact.GetComponentInParent<AnimalFoodSource>() : null;
                surfaceIsBush = food != null && food.FoodType == AnimalFoodType.Bush;
                surfaceIsSmallGarbage = contact != null && contact.TryGetComponent(out GarbageItem garbage)
                    && garbage.Size == RecyclableSize.Small;
            }
            bool touching = contact != null && ClimbableSurface.IsUsable(contact)
                && contact.gameObject.scene == mover.gameObject.scene;
            bool moving = !first && mover.MovementMode == RobotMovementMode.Driven && touching
                && speed > settings.WorldMotionThreshold && (!Usable(height) || !height.IsAirborne);
            float strength = Mathf.Clamp01(speed / settings.WorldReferenceSpeed);
            voices.SetParameter(Milestone3Cue.BushRustle, Milestone3Parameter.Speed, strength);
            voices.SetParameter(Milestone3Cue.SmallGarbageClimb, Milestone3Parameter.Speed, strength);
            voices.SetLoop(Milestone3Cue.BushRustle, moving && surfaceIsBush, strength);
            voices.SetLoop(Milestone3Cue.SmallGarbageClimb, moving && surfaceIsSmallGarbage, strength);
            if (!first && mover.ClimbableLandedThisFrame && climbLandingFrame != Time.frameCount
                && heightLandingFrame != Time.frameCount
                && !Milestone3AudioRuntime.SuppressesOrdinaryLanding(mover))
            {
                climbLandingFrame = Time.frameCount;
                voices.SetParameter(Milestone3Cue.ClimbableLand, Milestone3Parameter.Impact, 1f);
                voices.Play(Milestone3Cue.ClimbableLand);
            }
        }

        private void OnHeightLanded(RobotLandingImpact impact) => heightLandingFrame = Time.frameCount;
        private static bool Usable(Behaviour source) => source != null && source.isActiveAndEnabled
            && source.gameObject.scene.IsValid() && source.gameObject.scene.isLoaded;

        private void StopHeavyLoops(bool immediate)
        {
            voices.Stop(Milestone3Cue.HeavyPullLoop, immediate);
            voices.Stop(Milestone3Cue.HeavyPullTension, immediate);
            voices.Stop(Milestone3Cue.HeavyTearMiddle, immediate);
            voices.Stop(Milestone3Cue.HeavyTearLate, immediate);
        }

        internal void StopAllImmediately()
        {
            DetachBreakObserver();
            StopHeavyLoops(true);
            // Include onsets so binding, disable and pause cannot leave any world voice alive.
            for (int cue = (int)Milestone3Cue.HeavyPullStart; cue <= (int)Milestone3Cue.ClimbableLand; cue++)
                voices.Stop((Milestone3Cue)cue, true);
            for (int i = 0; i < tracked.Length; i++) tracked[i] = default;
            nearby.Clear();
            heavyItem = surfaceItem = null;
            pull = null;
            fragments = null;
            hasBaseline = wasPulling = pullStarted = false;
            surfaceIsBush = surfaceIsSmallGarbage = false;
            slideEpisode = false;
            previousProgress = discoveryRemaining = 0f;
            heightLandingFrame = climbLandingFrame = -1;
        }

        internal void Dispose() => Bind(null, null);
    }
}
