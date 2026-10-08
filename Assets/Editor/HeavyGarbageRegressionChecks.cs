using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using AnimalGame.Garbage;
using AnimalGame.RobotArm;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    public static class HeavyGarbageRegressionChecks
    {
        [MenuItem("Animal Game/Validation/Run Heavy Garbage Checks")]
        public static void Run()
        {
            CheckVisualDeformation();
            CheckPreviewStateAndBreak();
            CheckInteriorLayoutVariationAndOutline();
            CheckUnavailablePreviewKeepsSourceVisible();
            CheckPullProgressAndCancellation();
            CheckPullMovementAndRelease();
            CheckFragmentSeparationMotion();
            CheckSeparationStillRespectsObstacles();
            CheckSeparationCleanup();
            Debug.Log("Heavy garbage checks PASS: local deformation, distributed interior visual-only previews, stable extraction/handoff, bounded sibling separation, collision safety, cancellation and 30/60/120 fps retreat/release");
        }

        private static void CheckUnavailablePreviewKeepsSourceVisible()
        {
            using (var f = new Fixture())
            {
                WorldInteraction source = PrepareSource(f, out GarbageFragmentSpawner spawner, out _);
                WorldInteraction surroundingWall = f.Item(source.WorldPosition, 10f, RecyclableSize.Small);
                surroundingWall.SetKind(WorldInteractionKind.Collision);
                HeavyGarbagePull pull = source.gameObject.AddComponent<HeavyGarbagePull>();
                Call(pull, "Awake");
                Set(pull, "requiredPullDuration", 1f);
                Set(pull, "requiredPullIntentDistance", .2f);
                Vector2 away = ((Vector2)f.Robot.transform.position - (Vector2)source.WorldPosition).normalized;
                SetProperty(f.Mover, "CurrentThrottleIntent", -1f);
                SetProperty(f.Mover, "UnresistedMovementIntentWorld", away * 3f);
                Call(pull, "Step", 1.1f);
                HeavyGarbageVisual visual = source.GetComponent<HeavyGarbageVisual>();
                Require(spawner.PullPreviewCount == 0 && spawner.PullPreviewRoot == null,
                    "An incomplete fragment layout was displayed as a valid preview");
                RequireNear(visual.CurrentOpacity, 1f,
                    "Unavailable fragments turned fixed large garbage into an invisible obstacle");
                Require(((Vector2)Get(f.Mover, "heavyReleaseVelocity")).sqrMagnitude == 0f,
                    "Unavailable fragments released an impulse before a successful break");
            }
        }

        private static void CheckPreviewStateAndBreak()
        {
            foreach (int scatteredCount in new[] { 2, 3 })
            using (var f = new Fixture())
            {
                WorldInteraction source = PrepareSource(f, out GarbageFragmentSpawner spawner, out WorldInteraction template);
                Set(spawner, "minimumFragments", scatteredCount);
                Set(spawner, "maximumFragments", scatteredCount);
                source.WorldRotation = Quaternion.Euler(0f, 0f, 35f);
                f.SetSpritePivot(template, new Vector2(.1f, .2f));
                HeavyGarbageVisual visual = source.gameObject.AddComponent<HeavyGarbageVisual>();
                Vector2 anchor = (f.Arms.LeftHandWorld + f.Arms.RightHandWorld) * .5f;
                Vector2 direction = ((Vector2)f.Robot.transform.position - anchor).normalized;
                visual.SetPull(anchor, direction, 0f, 1f);
                InteractionShape bounds = source.GetShape(null);
                Require(spawner.BeginPullPreview(f.Arms, direction), "Valid heavy pull could not prepare its fragments");
                Transform preview = spawner.PullPreviewRoot;
                Require(preview != null && spawner.PullPreviewCount >= 3 && spawner.PullPreviewCount <= 4,
                    "Heavy preview did not include one held and two or three scattered fragments");
                Require(preview.GetComponentsInChildren<WorldInteraction>(true).Length == 0
                    && preview.GetComponentsInChildren<Collider2D>(true).Length == 0
                    && preview.GetComponentsInChildren<Rigidbody2D>(true).Length == 0
                    && preview.GetComponentsInChildren<MonoBehaviour>(true).Length == 0,
                    "Fragment preview created gameplay or physics components");
                Require(!WorldInteractionQuery.Query(InteractionShape.Capsule(source.WorldPosition, source.WorldPosition, 2f),
                    WorldInteractionKind.Collision | WorldInteractionKind.BodyCollision | WorldInteractionKind.Grabbable,
                    null, f.Scene, ignore: source.transform), "Visual preview registered as an interaction");
                var plan = (IList)Get(spawner, "pullFragments");
                var snapshots = new List<FragmentSnapshot>();
                foreach (object fragment in plan) snapshots.Add(new FragmentSnapshot(fragment));
                AssertInteriorFragmentLayout(source, snapshots, direction);
                foreach (float progress in new[] { 0f, .5f, .8f, .2f, 1f })
                {
                    spawner.UpdatePullPreview(progress, anchor);
                    visual.SetPull(anchor, direction, .1f * progress, 1f - progress);
                    Require(spawner.PullPreviewRoot == preview && plan.Count == snapshots.Count,
                        "A changing pull progress regenerated its fragment plan");
                    for (int i = 0; i < plan.Count; i++) snapshots[i].RequireUnchanged(plan[i], progress);
                    AssertPreviewOpacity(preview, progress);
                    RequireNear(visual.SourceRenderer.color.a, 1f - progress,
                        "Large body alpha did not crossfade with its fragments");
                    RequireNear(source.transform.Find("Center Icon").GetComponent<SpriteRenderer>().color.a,
                        .33f * (1f - progress), "Large icon did not crossfade from its .33 baseline");
                    RequireShape(source.GetShape(null), bounds, "Crossfading changed the solid large garbage bounds");
                }
                Vector2 handDelta = direction * .04f;
                spawner.UpdatePullPreview(.6f, anchor + handDelta);
                for (int i = 0; i < plan.Count; i++) snapshots[i].RequireUnchanged(plan[i], .6f, handDelta);
                spawner.UpdatePullPreview(.6f, anchor);
                for (int i = 0; i < plan.Count; i++) snapshots[i].RequireUnchanged(plan[i], .6f);
                spawner.CancelPullPreview();
                visual.ResetVisual();
                Require(spawner.PullPreviewRoot == null && spawner.PullPreviewCount == 0,
                    "Cancelling left a preview or random fragment plan behind");
                Require(f.Arms.HeldObject == source && source.Owner == f.Arms,
                    "Cancelling a visual preview changed garbage ownership");

                // The final arrangement must be exactly the arrangement visible before the break.
                visual.SetPull(anchor, direction, 0f, 1f);
                Require(spawner.BeginPullPreview(f.Arms, direction), "A cancelled preview could not restart");
                snapshots.Clear();
                foreach (object fragment in (IList)Get(spawner, "pullFragments")) snapshots.Add(new FragmentSnapshot(fragment));
                AssertInteriorFragmentLayout(source, snapshots, direction);
                spawner.UpdatePullPreview(1f, anchor);
                snapshots.Clear();
                foreach (object fragment in (IList)Get(spawner, "pullFragments")) snapshots.Add(new FragmentSnapshot(fragment));
                Require(spawner.CompletePullBreak(), "Heavy break failed to complete in its completion frame");
                Require(source == null || !source.gameObject.activeSelf,
                    "Heavy break retained the original large object for another fade stage");
                WorldInteraction carried = f.Arms.HeldObject;
                Require(carried != null && carried.Size == RecyclableSize.Medium && carried.RequiredHands == 2
                    && carried.Owner == f.Arms, "Break did not immediately transfer one middle fragment into both hands");
                Vector3 handoffPosition = carried.WorldPosition;
                Call(f.Arms, "FollowHeldObject");
                Require(Vector3.Distance(carried.WorldPosition, handoffPosition) < .0001f,
                    "Held middle fragment jumped from its final extraction preview during handoff");
                var actual = new List<WorldInteraction>();
                foreach (WorldInteraction candidate in WorldInteraction.Active)
                    if (candidate != null && candidate.gameObject.scene == f.Scene && candidate != template
                        && candidate.Size == RecyclableSize.Medium) actual.Add(candidate);
                Require(actual.Count == snapshots.Count, "Break produced a different fragment count than its preview");
                foreach (FragmentSnapshot fragment in snapshots)
                {
                    Vector2 expectedVelocity = fragment.Held ? Vector2.zero : fragment.Direction * fragment.Speed;
                    WorldInteraction match = actual.Find(item =>
                        Vector3.Distance(item.WorldPosition, fragment.Position) < .0001f
                        && Quaternion.Angle(item.WorldRotation, fragment.Rotation) < .1f
                        && item.TryGetComponent(out GarbageMotion movement)
                        && Vector2.Distance(movement.Velocity, expectedVelocity) < .0001f
                        && (item.Owner == f.Arms) == fragment.Held);
                    Require(match != null && Quaternion.Angle(match.WorldRotation, fragment.Rotation) < .1f,
                        "Completed fragment did not preserve its preview position and rotation");
                    Require(match.TryGetComponent(out GarbageMotion motion), "Completed fragment has no motion controller");
                    Require(Vector2.Distance(motion.Velocity, expectedVelocity) < .0001f,
                        "Completed fragment changed its planned scatter direction or launch speed");
                    RequireNear(match.transform.Find("Center Icon").GetComponent<SpriteRenderer>().color.a, .33f,
                        "Completed fragment icon lost its .33 baseline");
                }
            }
        }

        private static void AssertInteriorFragmentLayout(WorldInteraction source,
            List<FragmentSnapshot> fragments, Vector2 pullDirection, Vector2[] outline = null)
        {
            SpriteRenderer container = source.SpriteSource;
            Vector3 localCenter = container.sprite.bounds.center;
            if (container.flipX) localCenter.x = -localCenter.x;
            if (container.flipY) localCenter.y = -localCenter.y;
            Vector2 center = container.transform.TransformPoint(localCenter);
            Vector3 size = container.sprite.bounds.size;
            float shortestDimension = Mathf.Min(container.transform.TransformVector(Vector3.right * size.x).magnitude,
                container.transform.TransformVector(Vector3.up * size.y).magnitude);
            Vector2 outward = -pullDirection.normalized;
            Vector2 right = new Vector2(outward.y, -outward.x);
            var scattered = fragments.FindAll(fragment => !fragment.Held);
            Require(scattered.Count >= 2 && scattered.Count <= 3,
                "Interior layout did not preserve two or three scattered middle fragments");
            Require(fragments.Count == scattered.Count + 1 && fragments.FindAll(fragment => fragment.Held).Count == 1,
                "Interior layout did not include exactly one held middle fragment");
            foreach (FragmentSnapshot fragment in fragments)
            {
                Bounds visualBounds = fragment.VisualBounds;
                float radius = Vector2.Distance(visualBounds.center, center);
                Require(radius >= shortestDimension * .1f && radius <= shortestDimension * .36f,
                    "Interior fragment positions collapsed to the center or spread to the outer rim");
                fragment.RequireInsideSpriteRectangle(source.SpriteSource);
                if (outline != null) fragment.RequireInsideOutline(source.SpriteSource, outline);
            }
            for (int i = 0; i < fragments.Count; i++)
                for (int j = i + 1; j < fragments.Count; j++)
                {
                    Require(Vector2.Distance(fragments[i].VisualBounds.center, fragments[j].VisualBounds.center)
                        >= shortestDimension * .14f,
                        "Distributed interior fragments retained an overlapping central pile");
                }
            for (int i = 0; i < scattered.Count; i++)
                for (int j = i + 1; j < scattered.Count; j++)
                    Require(Vector2.Angle(scattered[i].Direction, scattered[j].Direction) > 20f,
                        "Interior fragments lost their distinct outward scatter directions");
            foreach (FragmentSnapshot fragment in scattered)
            {
                Vector2 region = (Vector2)fragment.VisualBounds.center - center;
                float lateral = Vector2.Dot(region, right);
                if (Mathf.Abs(lateral) > shortestDimension * .08f)
                    Require(Vector2.Dot(fragment.Direction, right) * lateral > 0f,
                        "A left or right interior fragment was launched toward the center instead of outward");
                else
                    Require(Vector2.Dot(fragment.Direction, outward) > 0f,
                        "The upper interior fragment was launched back toward the player");
            }
            FragmentSnapshot held = fragments.Find(fragment => fragment.Held);
            Require(Vector2.Dot((Vector2)held.VisualBounds.center - center, outward) < -shortestDimension * .08f,
                "Held preview did not begin in the lower interior region toward the player");
            Require(scattered.Exists(fragment => Vector2.Dot((Vector2)fragment.VisualBounds.center - center, right)
                < -shortestDimension * .08f)
                && scattered.Exists(fragment => Vector2.Dot((Vector2)fragment.VisualBounds.center - center, right)
                    > shortestDimension * .08f), "Interior layout did not cover both left and right regions");
            if (scattered.Count == 3)
                Require(scattered.Exists(fragment => Vector2.Dot((Vector2)fragment.VisualBounds.center - center, outward)
                    > shortestDimension * .12f
                    && Mathf.Abs(Vector2.Dot((Vector2)fragment.VisualBounds.center - center, right))
                        < shortestDimension * .12f), "Three-fragment layout did not cover its upper interior region");
            else
                foreach (FragmentSnapshot fragment in scattered)
                    Require(Vector2.Dot((Vector2)fragment.VisualBounds.center - center, outward) >= 0f,
                        "Two-fragment layout did not stay slightly farther from the player than the held fragment");
        }

        private static void CheckInteriorLayoutVariationAndOutline()
        {
            UnityEngine.Random.State previousRandom = UnityEngine.Random.state;
            Vector2[] outline = {
                new Vector2(-.38f, -.28f), new Vector2(-.24f, -.44f),
                new Vector2(.28f, -.44f), new Vector2(.46f, -.28f),
                new Vector2(.46f, .28f), new Vector2(.28f, .46f),
                new Vector2(-.24f, .46f), new Vector2(-.38f, .28f)
            };
            try
            {
                foreach (int scatteredCount in new[] { 2, 3 })
                {
                    Vector2[] previousCenters = null;
                    foreach (int seed in new[] { 19, 67 })
                    using (var f = new Fixture())
                    {
                        UnityEngine.Random.InitState(seed);
                        WorldInteraction source = PrepareSource(f, out GarbageFragmentSpawner spawner,
                            out WorldInteraction template);
                        Set(spawner, "minimumFragments", scatteredCount);
                        Set(spawner, "maximumFragments", scatteredCount);
                        Set(spawner, "interiorOutline", outline);
                        source.WorldRotation = Quaternion.Euler(0f, 0f, 32f);
                        source.SpriteSource.flipX = true;
                        source.SpriteSource.flipY = scatteredCount == 3;
                        f.SetSpritePivot(template, new Vector2(.16f, .23f));
                        template.SpriteSource.flipX = true;
                        Vector3 iconCenter = template.SpriteSource.sprite.bounds.center;
                        iconCenter.x = -iconCenter.x;
                        template.transform.Find("Center Icon").localPosition = iconCenter;
                        HeavyGarbageVisual visual = source.gameObject.AddComponent<HeavyGarbageVisual>();
                        Vector2 anchor = (f.Arms.LeftHandWorld + f.Arms.RightHandWorld) * .5f;
                        Vector2 direction = ((Vector2)f.Robot.transform.position - (Vector2)source.WorldPosition).normalized;
                        visual.SetPull(anchor, direction, 0f, 1f);
                        Require(spawner.BeginPullPreview(f.Arms, direction),
                            "Rotated and flipped interior outline could not prepare its assigned fragment regions");
                        var plan = (IList)Get(spawner, "pullFragments");
                        var snapshots = new List<FragmentSnapshot>();
                        foreach (object fragment in plan) snapshots.Add(new FragmentSnapshot(fragment));
                        Require(snapshots.Count == scatteredCount + 1, "Fixed interior fragment count changed with its seed");
                        AssertInteriorFragmentLayout(source, snapshots, direction, outline);
                        foreach (FragmentSnapshot fragment in snapshots)
                            Require(Quaternion.Angle(fragment.Rotation, source.SpriteSource.transform.rotation) <= 12.1f,
                                "Interior preview rotation varied beyond its subtle configured jitter");
                        foreach (float progress in new[] { .25f, .75f, 1f, 0f })
                        {
                            spawner.UpdatePullPreview(progress, anchor);
                            for (int i = 0; i < plan.Count; i++) snapshots[i].RequireUnchanged(plan[i], progress);
                        }
                        Vector2 right = new Vector2(-direction.y, direction.x);
                        List<FragmentSnapshot> scattered = snapshots.FindAll(fragment => !fragment.Held);
                        scattered.Sort((a, b) => Vector2.Dot(a.VisualBounds.center, right)
                            .CompareTo(Vector2.Dot(b.VisualBounds.center, right)));
                        var centers = new Vector2[scattered.Count];
                        for (int i = 0; i < centers.Length; i++)
                            centers[i] = (Vector2)scattered[i].VisualBounds.center - (Vector2)source.WorldPosition;
                        if (previousCenters != null)
                        {
                            bool changed = false;
                            for (int i = 0; i < centers.Length; i++)
                            {
                                float distance = Vector2.Distance(previousCenters[i], centers[i]);
                                changed |= distance > .0001f;
                                Require(distance < 1.2f * .08f,
                                    "Random interior placement moved a fragment out of its assigned region");
                            }
                            Require(changed, "Different pull sessions lost their small random interior placement variation");
                        }
                        previousCenters = centers;
                    }
                }
            }
            finally { UnityEngine.Random.state = previousRandom; }
        }

        private static void CheckFragmentSeparationMotion()
        {
            foreach (float dt in new[] { 1f / 30f, 1f / 60f, 1f / 120f })
            {
                using (var f = new Fixture())
                {
                    Vector2 origin = new Vector2(0f, 2f);
                    Vector2[] velocities = {
                        (Vector2)(Quaternion.Euler(0f, 0f, -25f) * Vector2.up) * 2f,
                        Vector2.up * 2f,
                        (Vector2)(Quaternion.Euler(0f, 0f, 25f) * Vector2.up) * 2f
                    };
                    List<GarbageMotion> cohort = CreateCohort(f, origin, velocities);
                    BeginSeparation(cohort, .8f);
                    foreach (GarbageMotion motion in cohort)
                        Require(((IList)Get(motion, "separationSiblings")).Count == 2,
                            "An initially overlapping fragment was not part of its batch's separation period");
                    int frames = Mathf.RoundToInt(.5f / dt);
                    for (int frame = 0; frame < frames; frame++)
                        foreach (GarbageMotion motion in cohort) Call(motion, "Step", dt);
                    for (int i = 0; i < cohort.Count; i++)
                    {
                        Require(Vector2.Distance(cohort[i].transform.position, origin + velocities[i] * .5f) < .0001f,
                            "Overlapping central fragments blocked one another while scattering at dt " + dt);
                        Require(!cohort[i].WasBlocked && cohort[i].Velocity.sqrMagnitude > 0f,
                            "A central fragment stopped against its own separating batch");
                        Require(((IList)Get(cohort[i], "separationSiblings")).Count == 0,
                            "Separated middle fragments retained their temporary collision exception");
                    }
                }
            }
        }

        private static void CheckSeparationStillRespectsObstacles()
        {
            foreach (float dt in new[] { 1f / 30f, 1f / 60f, 1f / 120f })
            {
                using (var f = new Fixture())
                {
                    Vector2 origin = new Vector2(0f, 2f);
                    List<GarbageMotion> cohort = CreateCohort(f, origin, Vector2.right * 2f, Vector2.up * 2f);
                    WorldInteraction wall = f.Item(origin + Vector2.right * .4f, .1f, RecyclableSize.Small);
                    wall.SetKind(WorldInteractionKind.Collision);
                    BeginSeparation(cohort, .8f);
                    for (int frame = 0; frame < Mathf.RoundToInt(.5f / dt); frame++)
                        foreach (GarbageMotion motion in cohort) Call(motion, "Step", dt);
                    Require(cohort[0].WasBlocked && cohort[0].Velocity == Vector2.zero,
                        "Batch separation also disabled collision with an unrelated wall");
                    Require(cohort[0].GetComponent<WorldInteraction>().GetShape(null).B.x < wall.GetShape(null).A.x,
                        "A separating fragment entered or crossed a wall");
                    Require(((IList)Get(cohort[0], "separationSiblings")).Count == 0,
                        "A wall-stopped fragment kept ignoring its siblings");
                }
                using (var f = new Fixture())
                {
                    List<GarbageMotion> cohort = CreateCohort(f, Vector2.right, Vector2.left * 10f, Vector2.up * 10f);
                    BeginSeparation(cohort, .8f);
                    for (int frame = 0; frame < Mathf.RoundToInt(.5f / dt); frame++)
                        foreach (GarbageMotion motion in cohort) Call(motion, "Step", dt);
                    Require(cohort[0].WasBlocked && cohort[0].Velocity == Vector2.zero,
                        "Batch separation let a fragment travel through the player");
                    Require(cohort[0].transform.position.x >= .36f + .09f,
                        "A separating fragment entered the player's body");
                }
            }
            using (var f = new Fixture())
            {
                Vector2 origin = new Vector2(0f, 2f);
                List<GarbageMotion> cohort = CreateCohort(f, origin, Vector2.right * 2f, Vector2.up * 2f);
                WorldInteraction otherGarbage = f.Item(origin + Vector2.right * .15f, .18f, RecyclableSize.Medium);
                BeginSeparation(cohort, .8f);
                Call(cohort[0], "Step", 1f / 60f);
                Require(cohort[0].WasBlocked && (Vector2)cohort[0].transform.position == origin,
                    "Batch separation ignored unrelated middle garbage outside the cohort");
            }
        }

        private static void CheckSeparationCleanup()
        {
            using (var f = new Fixture())
            {
                List<GarbageMotion> cohort = CreateCohort(f, new Vector2(0f, 2f), Vector2.right * 2f, Vector2.up * 2f);
                BeginSeparation(cohort, .8f);
                cohort[0].Stop();
                RequireNoSeparation(cohort, "Explicit stopping did not clear both sides of the batch exception");

                cohort[0].Launch(Vector2.right * .01f, 0f);
                BeginSeparation(cohort, .8f);
                Call(cohort[0], "Step", 1f / 60f);
                RequireNoSeparation(cohort, "Natural low-speed stopping did not clear both sides of the batch exception");

                cohort[0].Launch(Vector2.right * 2f, 0f);
                BeginSeparation(cohort, .8f);
                WorldInteraction grabbed = cohort[0].GetComponent<WorldInteraction>();
                Require(grabbed.TryGrab(f.Arms), "Cleanup fixture could not grab a middle fragment");
                Call(cohort[0], "Step", 1f / 60f);
                RequireNoSeparation(cohort, "Grabbing did not clear both sides of the batch exception");
                grabbed.Release(f.Arms);

                cohort[0].Launch(Vector2.right * 2f, 0f);
                BeginSeparation(cohort, .01f);
                Call(cohort[0], "Step", .02f);
                RequireNoSeparation(cohort, "The separation exception outlasted its bounded duration");
            }
        }

        private static List<GarbageMotion> CreateCohort(Fixture fixture, Vector2 position, params Vector2[] velocities)
        {
            var result = new List<GarbageMotion>();
            foreach (Vector2 velocity in velocities)
            {
                WorldInteraction item = fixture.Item(position, .18f, RecyclableSize.Medium);
                GarbageMotion motion = item.gameObject.AddComponent<GarbageMotion>();
                Call(motion, "Awake");
                motion.Initialize(null, fixture.Mover);
                motion.Launch(velocity, 0f);
                result.Add(motion);
            }
            return result;
        }

        private static void BeginSeparation(List<GarbageMotion> cohort, float duration)
        {
            foreach (GarbageMotion motion in cohort) motion.BeginFragmentSeparation(cohort, duration);
        }

        private static void RequireNoSeparation(List<GarbageMotion> cohort, string message)
        {
            foreach (GarbageMotion motion in cohort)
                Require(((IList)Get(motion, "separationSiblings")).Count == 0
                    && (float)Get(motion, "separationTimeRemaining") == 0f, message);
        }

        private static void CheckPullProgressAndCancellation()
        {
            foreach (float dt in new[] { 1f / 30f, 1f / 60f, 1f / 120f })
            {
                using (var f = new Fixture())
                {
                    WorldInteraction source = PrepareSource(f, out GarbageFragmentSpawner spawner, out _);
                    HeavyGarbagePull pull = source.gameObject.AddComponent<HeavyGarbagePull>();
                    Call(pull, "Awake");
                    Set(pull, "requiredPullDuration", 1f);
                    Set(pull, "requiredPullIntentDistance", .2f);
                    Vector3 sourcePosition = source.WorldPosition;
                    Vector2 away = ((Vector2)f.Robot.transform.position - (Vector2)sourcePosition).normalized;
                    SetProperty(f.Mover, "CurrentThrottleIntent", -1f);
                    SetProperty(f.Mover, "UnresistedMovementIntentWorld", away * 3f);
                    int frames = Mathf.RoundToInt(.4f / dt);
                    for (int frame = 0; frame < frames; frame++)
                    {
                        Call(f.Mover, "StepHeavyPullMovement", dt);
                        f.TickArms(dt, Vector2.up, true);
                        Call(pull, "Step", dt);
                    }
                    RequireNear(pull.Progress, .4f, "Heavy pull progress changed with frame rate at dt " + dt);
                    Require(pull.StoredPullSpeed > 0f && pull.StoredPullSpeed <= f.Mover.UnloadedReverseSpeed,
                        "Effective pulling failed to accumulate a bounded release speed");
                    Require(source.WorldPosition == sourcePosition, "Pull moved the entire fixed large garbage");
                    Require(((Vector2)f.Robot.transform.position).magnitude > 0f
                        && ((Vector2)f.Robot.transform.position).magnitude <= .72f * .3f + .0001f,
                        "Pull did not create a small bounded backward movement");
                    AssertPreviewOpacity(spawner.PullPreviewRoot, .4f);
                    HeavyGarbageVisual visual = source.GetComponent<HeavyGarbageVisual>();
                    RequireNear(visual.CurrentOpacity, .6f, "Heavy pull did not update its large body crossfade");

                    float previousProgress = pull.Progress;
                    float previousStoredSpeed = pull.StoredPullSpeed;
                    SetProperty(f.Mover, "HeavyPullMovementBlocked", true);
                    Call(pull, "Step", dt);
                    RequireNear(pull.Progress, previousProgress, "Blocked movement continued breaking heavy garbage");
                    RequireNear(pull.StoredPullSpeed, previousStoredSpeed, "Blocked movement continued charging release speed");
                    SetProperty(f.Mover, "HeavyPullMovementBlocked", false);

                    SetProperty(f.Mover, "CurrentThrottleIntent", 0f);
                    SetProperty(f.Mover, "UnresistedMovementIntentWorld", Vector2.zero);
                    Call(pull, "Step", .1f);
                    Require(pull.Progress == 0f && pull.StoredPullSpeed == 0f && !pull.HasGripAnchor,
                        "Cancelling retained progress, tension or a constrained grip");
                    Call(pull, "Step", .2f);
                    Require(spawner.PullPreviewRoot == null && spawner.PullPreviewCount == 0,
                        "Cancelled pull did not clear its previews after the return animation");
                    RequireNear(visual.CurrentOpacity, 1f, "Cancelled pull did not restore the original opacity");
                    Require(((Vector2)Get(f.Mover, "heavyReleaseVelocity")).sqrMagnitude == 0f,
                        "Cancelled pull unexpectedly released a backward impulse");

                    for (int attempt = 0; attempt < 4; attempt++)
                    {
                        SetProperty(f.Mover, "CurrentThrottleIntent", -1f);
                        SetProperty(f.Mover, "UnresistedMovementIntentWorld", away * 3f);
                        for (int frame = 0; frame < frames; frame++)
                        {
                            Call(f.Mover, "StepHeavyPullMovement", dt);
                            f.TickArms(dt, Vector2.up, true);
                            Call(pull, "Step", dt);
                        }
                        SetProperty(f.Mover, "CurrentThrottleIntent", 0f);
                        SetProperty(f.Mover, "UnresistedMovementIntentWorld", Vector2.zero);
                        Call(pull, "Step", .3f);
                        Require(((Vector2)f.Robot.transform.position).magnitude <= .72f * .3f + .0001f,
                            "Repeated cancellation moved the player beyond the same grip session's retreat limit");
                        Require(((Vector2)Get(f.Mover, "heavyReleaseVelocity")).sqrMagnitude == 0f,
                            "Repeated cancellation charged or released an impulse");
                    }

                    SetProperty(f.Mover, "CurrentThrottleIntent", -1f);
                    SetProperty(f.Mover, "UnresistedMovementIntentWorld", away * 3f);
                    int completionLimit = Mathf.CeilToInt(1.1f / dt);
                    for (int frame = 0; frame < completionLimit && source != null; frame++)
                    {
                        Call(f.Mover, "StepHeavyPullMovement", dt);
                        f.TickArms(dt, Vector2.up, true);
                        Call(pull, "Step", dt);
                    }
                    Require(source == null || !source.gameObject.activeSelf,
                        "Completed pull waited for an additional fade before breaking");
                    Require(f.Arms.HeldObject != null && f.Arms.HeldObject.Size == RecyclableSize.Medium,
                        "Completed pull did not retain its middle fragment in the player's hands");
                    Require(((Vector2)f.Robot.transform.position).magnitude <= .72f * .3f + .0001f,
                        "Final pull exceeded the original grip session's maximum retreat distance");
                    Vector2 released = (Vector2)Get(f.Mover, "heavyReleaseVelocity");
                    Require(released.magnitude > .1f && released.magnitude <= f.Mover.UnloadedReverseSpeed + .0001f
                        && Vector2.Angle(released, away) < .01f,
                        "Break did not release the accumulated bounded inertia in the pull direction");
                }
            }
        }

        private static WorldInteraction PrepareSource(Fixture fixture,
            out GarbageFragmentSpawner spawner, out WorldInteraction template)
        {
            for (int frame = 0; frame < 90; frame++) fixture.TickArms(1f / 60f, Vector2.up, false);
            Vector2 anchor = (fixture.Arms.LeftHandWorld + fixture.Arms.RightHandWorld) * .5f;
            // A real body collision stops the robot outside the large garbage. The
            // hands therefore grip its near edge, while its central fragments start
            // farther from the player. Keep this fixture's hands .1 units inside
            // the 1.2-unit box instead of putting its center between the hands.
            WorldInteraction source = fixture.Item(anchor + Vector2.up * .5f, 1.2f, RecyclableSize.Big);
            spawner = source.gameObject.AddComponent<GarbageFragmentSpawner>();
            Call(spawner, "Awake");
            template = fixture.Item(new Vector2(20f, 20f), .18f, RecyclableSize.Medium);
            template.gameObject.AddComponent<GarbageMotion>();
            Set(spawner, "heldFragments", new[] { template.gameObject });
            Set(spawner, "scatteredFragments", new[] { template.gameObject });
            Set(spawner, "minimumFragments", 2);
            Set(spawner, "maximumFragments", 3);
            Set(spawner, "minimumLaunchSpeed", .5f);
            Set(spawner, "maximumLaunchSpeed", .8f);
            fixture.TickArms(1f / 60f, Vector2.up, true);
            Require(fixture.Arms.HeldObject == source && source.Owner == fixture.Arms,
                "Fixture could not grab heavy garbage using both hands");
            return source;
        }

        private static void AssertPreviewOpacity(Transform preview, float opacity)
        {
            Require(preview != null, "Pull preview disappeared before the break");
            foreach (SpriteRenderer renderer in preview.GetComponentsInChildren<SpriteRenderer>(true))
                RequireNear(renderer.color.a, renderer.name == "Center Icon" ? .33f * opacity : opacity,
                    "Fragment alpha did not track pull progress");
        }

        private sealed class FragmentSnapshot
        {
            private readonly Transform preview;
            private readonly GameObject prefab;
            public readonly Vector3 Position;
            public readonly Vector3 InitialPosition;
            public readonly Vector3 ExtractionOffset;
            public readonly Quaternion Rotation;
            public readonly Vector2 Direction;
            public readonly float Speed;
            public readonly bool Held;
            public Bounds VisualBounds
            {
                get
                {
                    SpriteRenderer[] sprites = preview.GetComponentsInChildren<SpriteRenderer>(true);
                    Require(sprites.Length > 0, "Fragment preview had no visual bounds");
                    Bounds bounds = sprites[0].bounds;
                    for (int i = 1; i < sprites.Length; i++) bounds.Encapsulate(sprites[i].bounds);
                    return bounds;
                }
            }
            public void RequireInsideSpriteRectangle(SpriteRenderer container)
            {
                Bounds containerBounds = container.sprite.bounds;
                Vector3 inset = containerBounds.size * .01f;
                foreach (SpriteRenderer renderer in preview.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (renderer.sprite == null || !renderer.enabled) continue;
                    Bounds bounds = renderer.sprite.bounds;
                    for (int corner = 0; corner < 4; corner++)
                    {
                        Vector3 point = new Vector3((corner & 1) == 0 ? bounds.min.x : bounds.max.x,
                            (corner & 2) == 0 ? bounds.min.y : bounds.max.y, 0f);
                        if (renderer.flipX) point.x = -point.x;
                        if (renderer.flipY) point.y = -point.y;
                        point = container.transform.InverseTransformPoint(renderer.transform.TransformPoint(point));
                        if (container.flipX) point.x = -point.x;
                        if (container.flipY) point.y = -point.y;
                        Require(point.x >= containerBounds.min.x + inset.x
                            && point.x <= containerBounds.max.x - inset.x
                            && point.y >= containerBounds.min.y + inset.y
                            && point.y <= containerBounds.max.y - inset.y,
                            "Interior preview fit the world's AABB but escaped the rotated large sprite rectangle");
                    }
                }
            }
            public void RequireInsideOutline(SpriteRenderer container, Vector2[] outline)
            {
                Bounds containerBounds = container.sprite.bounds;
                foreach (SpriteRenderer renderer in preview.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (renderer.sprite == null || !renderer.enabled) continue;
                    Bounds bounds = renderer.sprite.bounds;
                    for (int corner = 0; corner < 4; corner++)
                    {
                        Vector3 point = new Vector3((corner & 1) == 0 ? bounds.min.x : bounds.max.x,
                            (corner & 2) == 0 ? bounds.min.y : bounds.max.y, 0f);
                        if (renderer.flipX) point.x = -point.x;
                        if (renderer.flipY) point.y = -point.y;
                        point = container.transform.InverseTransformPoint(renderer.transform.TransformPoint(point));
                        if (container.flipX) point.x = -point.x;
                        if (container.flipY) point.y = -point.y;
                        Vector2 normalized = new Vector2((point.x - containerBounds.center.x) / containerBounds.size.x,
                            (point.y - containerBounds.center.y) / containerBounds.size.y);
                        for (int i = 0; i < outline.Length; i++)
                        {
                            Vector2 edge = outline[(i + 1) % outline.Length] - outline[i];
                            Vector2 offset = normalized - outline[i];
                            float distance = (edge.x * offset.y - edge.y * offset.x) / edge.magnitude;
                            Require(distance >= .01f - .0001f,
                                "Interior fragment escaped the configured filled outline or its boundary inset");
                        }
                    }
                }
            }
            public FragmentSnapshot(object fragment)
            {
                preview = (Transform)GetPublic(fragment, "Preview");
                prefab = (GameObject)GetPublic(fragment, "Prefab");
                Position = (Vector3)GetPublic(fragment, "Position");
                InitialPosition = (Vector3)GetPublic(fragment, "InitialPosition");
                ExtractionOffset = (Vector3)GetPublic(fragment, "ExtractionOffset");
                Rotation = (Quaternion)GetPublic(fragment, "Rotation");
                Direction = (Vector2)GetPublic(fragment, "Direction");
                Speed = (float)GetPublic(fragment, "Speed");
                Held = (bool)GetPublic(fragment, "Held");
            }
            public void RequireUnchanged(object fragment, float progress = 0f, Vector2 handDelta = default)
            {
                Vector3 expectedPosition = Held ? InitialPosition + (Vector3)handDelta
                    + ExtractionOffset * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress)) : InitialPosition;
                Require((Transform)GetPublic(fragment, "Preview") == preview
                    && (GameObject)GetPublic(fragment, "Prefab") == prefab
                    && Vector3.Distance((Vector3)GetPublic(fragment, "InitialPosition"), InitialPosition) < .0001f
                    && Vector3.Distance((Vector3)GetPublic(fragment, "ExtractionOffset"), ExtractionOffset) < .0001f
                    && Vector3.Distance((Vector3)GetPublic(fragment, "Position"), expectedPosition) < .0001f
                    && Vector3.Distance(preview.position, expectedPosition) < .0001f
                    && Quaternion.Angle((Quaternion)GetPublic(fragment, "Rotation"), Rotation) < .1f
                    && Vector2.Distance((Vector2)GetPublic(fragment, "Direction"), Direction) < .0001f
                    && Mathf.Approximately((float)GetPublic(fragment, "Speed"), Speed),
                    "Pull rerolled its plan or moved a preview outside its saved hand/extraction offset");
            }
            private static object GetPublic(object target, string name) => target.GetType().GetField(name).GetValue(target);
        }

        private static void CheckVisualDeformation()
        {
            foreach (bool useBoxBounds in new[] { true, false })
            using (var f = new Fixture())
            {
                WorldInteraction source = f.Item(new Vector2(2f, 2f), 1.6f, RecyclableSize.Big);
                if (!useBoxBounds) source.BoxSource = null;
                HeavyGarbageVisual visual = source.gameObject.AddComponent<HeavyGarbageVisual>();
                InteractionShape originalBounds = source.GetShape(null);
                Vector3 originalPosition = source.WorldPosition;
                Vector3 originalScale = source.LocalScale;
                Vector2 direction = new Vector2(-1f, -.4f).normalized;
                Vector2 grip = (Vector2)source.WorldPosition + direction * .6f;
                visual.SetPull(grip, direction, 0f, 1f);
                Require(visual.DeformationMesh != null, "Pull visual did not create a deformable mesh");
                Vector3[] originalVertices = visual.DeformationMesh.vertices;
                visual.SetPull(grip, direction, .25f, .5f);
                Vector3[] stretchedVertices = visual.DeformationMesh.vertices;
                float maximumMovement = 0f;
                float minimumMovement = float.MaxValue;
                for (int i = 0; i < originalVertices.Length; i++)
                {
                    float distance = Vector3.Distance(originalVertices[i], stretchedVertices[i]);
                    maximumMovement = Mathf.Max(maximumMovement, distance);
                    minimumMovement = Mathf.Min(minimumMovement, distance);
                }
                Require(maximumMovement > .05f, "Local pull did not visibly stretch the body");
                Require(minimumMovement < .0001f, "Pull stretched the entire body instead of a local area");
                RequireNear(visual.CurrentOpacity, .5f, "Body fade did not track pull opacity");
                SpriteRenderer icon = source.transform.Find("Center Icon").GetComponent<SpriteRenderer>();
                RequireNear(icon.color.a, .165f, "Source icon did not preserve its .33 baseline during fading");
                RequireShape(source.GetShape(null), originalBounds, "Deformation changed collision bounds");
                Require(source.WorldPosition == originalPosition && source.LocalScale == originalScale,
                    "Deformation moved or scaled the fixed large garbage");
                visual.ResetVisual();
                RequireNear(visual.CurrentStretchWorld, 0f, "Cancel left local stretch behind");
                RequireNear(visual.CurrentOpacity, 1f, "Cancel left the large body transparent");
                RequireNear(icon.color.a, .33f, "Cancel did not restore the source icon");
                RequireShape(source.GetShape(null), originalBounds, "Reset changed collision bounds");
            }
        }

        private static void CheckPullMovementAndRelease()
        {
            var releaseDistances = new List<float>();
            foreach (float dt in new[] { 1f / 30f, 1f / 60f, 1f / 120f })
            {
                using (var f = new Fixture())
                {
                    GameObject root = f.NewObject("Constrained movement robot", Vector2.zero);
                    RobotMover mover = root.AddComponent<RobotMover>();
                    Set(mover, "reverseSpeed", 3f);
                    Set(mover, "overallMotionScale", 1f);
                    SetProperty(mover, "CurrentThrottleIntent", -1f);
                    SetProperty(mover, "UnresistedMovementIntentWorld", Vector2.left * 3f);
                    Object owner = f.NewObject("Constraint owner", new Vector2(20f, 20f));
                    Vector2 target = Vector2.left * .2f;
                    for (int frame = 0; frame < Mathf.RoundToInt(2f / dt); frame++)
                    {
                        mover.SetHeavyPullConstraint(owner, target, Vector2.left, .15f);
                        Vector2 before = root.transform.position;
                        Call(mover, "StepHeavyPullMovement", dt);
                        float step = Vector2.Distance(before, root.transform.position);
                        Require(step <= .15f * dt + .00001f,
                            "Constrained retreat exceeded its slow speed at dt " + dt);
                    }
                    RequireNear(root.transform.position.x, -.2f,
                        "Constrained retreat failed to settle at its small maximum distance");
                    RequireNear(root.transform.position.y, 0f, "Constrained retreat drifted sideways");
                    mover.ClearHeavyPullConstraint(owner);
                    Require(((Vector2)Get(mover, "heavyReleaseVelocity")).sqrMagnitude == 0f,
                        "Cancelling a pull created a release impulse");

                    root.transform.position = Vector3.zero;
                    Vector2 releaseDirection = new Vector2(-1f, -.3f).normalized;
                    mover.ReleaseHeavyPullVelocity(releaseDirection, 100f, .25f);
                    Vector2 velocity = (Vector2)Get(mover, "heavyReleaseVelocity");
                    Require(velocity.magnitude <= mover.UnloadedReverseSpeed + .00001f,
                        "Release impulse exceeded unloaded reverse speed");
                    Require(Vector2.Angle(velocity, releaseDirection) < .05f,
                        "Release impulse ignored the actual pull direction");
                    float oldMagnitude = velocity.magnitude;
                    for (int frame = 0; frame < Mathf.RoundToInt(.5f / dt); frame++)
                    {
                        Vector2 displacement = (Vector2)Call(mover, "StepHeavyReleaseVelocity", dt);
                        Call(mover, "TryMoveSafely", displacement, null, true);
                        velocity = (Vector2)Get(mover, "heavyReleaseVelocity");
                        Require(velocity.magnitude <= oldMagnitude + .00001f,
                            "Release impulse gained speed while decaying");
                        oldMagnitude = velocity.magnitude;
                    }
                    Require(velocity.sqrMagnitude < .000001f, "Release impulse outlasted its decay duration");
                    Require(Vector2.Angle(root.transform.position, releaseDirection) < .05f,
                        "Release displacement changed direction during decay");
                    RequireNear(((Vector2)root.transform.position).magnitude, .375f,
                        "Release travel did not integrate the bounded speed and .25 second decay");
                    releaseDistances.Add(((Vector2)root.transform.position).magnitude);

                    root.transform.position = Vector3.zero;
                    WorldInteraction wall = f.Item(Vector2.left, .1f, RecyclableSize.Small);
                    wall.SetKind(WorldInteractionKind.Collision);
                    mover.SetHeavyPullConstraint(owner, Vector2.left, Vector2.left, 3f);
                    Call(mover, "StepHeavyPullMovement", .5f);
                    Require(mover.HeavyPullMovementBlocked && root.transform.position == Vector3.zero,
                        "Pull movement crossed a wall or failed to report blockage");
                    mover.ClearHeavyPullConstraint(owner);
                    mover.ReleaseHeavyPullVelocity(Vector2.left, 3f, .25f);
                    Require(!(bool)Call(mover, "TryMoveSafely", Vector2.left, null, true)
                        && root.transform.position == Vector3.zero,
                        "Released inertia tunneled through a wall");
                }
            }
            Require(Mathf.Abs(releaseDistances[0] - releaseDistances[2]) < .04f,
                "Release impulse travel depends materially on frame rate");
        }

        private static void RequireShape(InteractionShape actual, InteractionShape expected, string message)
        {
            Require(actual.IsBox == expected.IsBox && Mathf.Approximately(actual.Radius, expected.Radius), message);
            for (int i = 0; i < 4; i++) Require(Vector2.Distance(actual.Vertex(i), expected.Vertex(i)) < .0001f, message);
        }

        private static void RequireNear(float actual, float expected, string message) =>
            Require(Mathf.Abs(actual - expected) < .0001f,
                message + ": expected " + expected + ", got " + actual);

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Set(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static object Get(object target, string name) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private static void SetProperty(object target, string name, object value) =>
            target.GetType().GetProperty(name).SetValue(target, value);
        private static object Call(object target, string name, params object[] args) =>
            target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(target, args);

        private sealed class Fixture : IDisposable
        {
            public Scene Scene { get; }
            public GameObject Robot { get; }
            public RobotArmController Arms { get; }
            public RobotMover Mover => Robot.GetComponent<RobotMover>();
            private readonly Texture2D texture;
            private readonly Sprite sprite;
            private readonly List<Sprite> extraSprites = new();
            public Fixture()
            {
                Scene = EditorSceneManager.NewPreviewScene();
                texture = new Texture2D(32, 32);
                var pixels = new Color[32 * 32];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
                texture.SetPixels(pixels);
                texture.Apply();
                sprite = Sprite.Create(texture, new Rect(0f, 0f, 32f, 32f), Vector2.one * .5f, 32f);
                Robot = NewObject("Heavy regression robot", Vector2.zero);
                Arms = Robot.AddComponent<RobotArmController>();
                GameObject visualFrame = NewObject("Marker Visual Root", Vector2.zero);
                visualFrame.transform.SetParent(Robot.transform, false);
                RobotArmRegressionChecks.CreateFixtureArtwork(visualFrame.transform);
                Set(Robot.GetComponent<RobotMarkerView>(), "markerVisualRoot", visualFrame.transform);
                Set(Robot.GetComponent<RobotMarkerView>(), "bodyVisualRoot", visualFrame.transform.Find("Body Visual"));
                Call(Arms, "Awake");
                Call(Arms, "EnsureVisuals");
                Call(Mover, "Awake");
            }
            public GameObject NewObject(string name, Vector2 position)
            {
                var result = new GameObject(name);
                SceneManager.MoveGameObjectToScene(result, Scene);
                result.transform.position = position;
                return result;
            }
            public WorldInteraction Item(Vector2 position, float diameter, RecyclableSize size)
            {
                GameObject root = NewObject("Regression " + size + " garbage", position);
                SpriteRenderer body = root.AddComponent<SpriteRenderer>();
                body.sprite = sprite;
                root.transform.localScale = Vector3.one * diameter;
                BoxCollider2D box = root.AddComponent<BoxCollider2D>();
                box.size = Vector2.one;
                WorldInteraction item = root.AddComponent<WorldInteraction>();
                item.SetKind(WorldInteractionKind.BodyCollision | WorldInteractionKind.Grabbable);
                item.BoxSource = box;
                item.SpriteSource = body;
                Set(item, "size", size);
                Set(item, "requiredHands", size == RecyclableSize.Small ? 1 : 2);
                Set(item, "grabResistance", size == RecyclableSize.Big ? 1f : .4f);
                GameObject iconObject = NewObject("Center Icon", position);
                iconObject.transform.SetParent(root.transform, false);
                iconObject.transform.localPosition = Vector3.zero;
                iconObject.transform.localScale = Vector3.one * .2f;
                SpriteRenderer icon = iconObject.AddComponent<SpriteRenderer>();
                icon.sprite = sprite;
                icon.color = new Color(1f, 1f, 1f, .33f);
                return item;
            }
            public void SetSpritePivot(WorldInteraction item, Vector2 pivot)
            {
                Sprite replacement = Sprite.Create(texture, new Rect(0f, 0f, 32f, 32f), pivot, 32f);
                extraSprites.Add(replacement);
                item.SpriteSource.sprite = replacement;
                item.transform.Find("Center Icon").localPosition = replacement.bounds.center;
                item.MarkSpatialDirty();
            }
            public void TickArms(float dt, Vector2 stick, bool grab, bool deploy = true) =>
                Call(Arms, "Step", dt, stick, deploy, grab);
            public void Dispose()
            {
                Arms.enabled = false;
                Transform frame = Robot.GetComponent<RobotMarkerView>().MarkerVisualRoot;
                if (frame != null)
                    for (int i = frame.childCount - 1; i >= 0; i--)
                        Object.DestroyImmediate(frame.GetChild(i).gameObject);
                EditorSceneManager.ClosePreviewScene(Scene);
                foreach (Sprite extra in extraSprites) Object.DestroyImmediate(extra);
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
            }
        }
    }
}
