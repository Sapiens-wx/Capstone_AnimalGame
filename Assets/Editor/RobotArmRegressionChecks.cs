using System;
using System.Collections.Generic;
using System.Reflection;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using AnimalGame.RobotArm;
using AnimalGame.World;
using AnimalGame.Garbage;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    public static class RobotArmRegressionChecks
    {
        private static readonly List<string> passed = new();
        [MenuItem("Animal Game/Validation/Run Mechanical Arm Checks")]
        public static void Run()
        {
            passed.Clear();
            Check("IK reachable, mirrored, near and far limits", CheckIK);
            Check("Box/circle geometry and swept body collision", CheckGeometry);
            Check("Unified types, multiple components, legacy obstacles and overlap escape", CheckRegistry);
            Check("Body-only collision, grabbing and per-object push resistance", CheckInteractionKinds);
            Check("QuadTree matches exhaustive queries after movement, resizing, kind and scene changes", CheckQuadTree);
            Check("Dirty queue skips unchanged shapes and coalesces geometry edits", CheckSpatialDirty);
            Check("Held input retries, one/two-hand grabbing and early release", CheckGrabbing);
            Check("Raw thresholds, dock delay, hysteresis and recycle completion", CheckDocking);
            Check("Recycle hand tracking, inward limit, medium pauses, shrinking and cancellation", CheckRecycleAnimation);
            Check("Release L3 drops before retracting", CheckRetracting);
            Check("Deployment obstacle prevents grabbing; deployed obstacle does not", CheckDeploymentCollision);
            Check("Body translation and rotation are constrained by arm boxes", CheckBodyCollision);
            Check("Player-local input, threshold turning and fallen control restrictions", CheckTurning);
            Check("Stopped steering retains reverse only while steering continuously", CheckStoppedSteering);
            Check("Grab resistance scales movement and freezes held movement at one", CheckGrabResistance);
            Check("Held replacement updates owner, hand count and resistance", CheckHeldReplacement);
            Check("Held fragments spawn at the hand midpoint despite overlaps", CheckHeldFragmentSpawn);
            Check("Heavy pull needs reverse movement intent away from garbage", CheckHeavyPullIntent);
            Debug.Log("Mechanical arm checks PASS: " + passed.Count + " groups\n" + string.Join("\n", passed));
        }
        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
        private static void Check(string name, Action test) { test(); passed.Add(name); }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        private static void CheckIK()
        {
            foreach (float side in new[] { -1f, 1f })
                foreach (Vector2 target in new[] { new Vector2(.3f, .8f), new Vector2(0f, 10f), Vector2.zero })
                {
                    RobotArmController.SolveIK(target, .45f, .65f, side, out float a, out float b);
                    Vector2 end = Direction(a) * .45f + Direction(b) * .65f;
                    float expected = Mathf.Clamp(target.magnitude, .20001f, 1.09999f);
                    Require(!float.IsNaN(a) && !float.IsNaN(b), "IK produced NaN");
                    Require(Mathf.Abs(end.magnitude - expected) < .0002f, "IK length or clamping failed");
                    if (target.sqrMagnitude > 0f) Require(Vector2.Angle(end, target) < .1f, "IK missed requested direction");
                }
        }
        private static void CheckGeometry()
        {
            InteractionShape box = InteractionShape.WorldBox(Vector2.zero, new Vector2(2f, 1f), null);
            Require(WorldInteractionQuery.Penetration(InteractionShape.Capsule(new Vector2(0f, 2f), new Vector2(0f, -2f), .1f), box) >= 0f,
                "Sweep skipped a box between endpoints");
            Require(WorldInteractionQuery.Penetration(InteractionShape.Capsule(new Vector2(1.2f, .7f), new Vector2(1.2f, .7f), .1f), box) < 0f,
                "Box corner false positive");
            Require(WorldInteractionQuery.Penetration(InteractionShape.Capsule(Vector2.zero, Vector2.zero, 0f), box) > 0f,
                "Grab point inside box missed");
            Require(WorldInteractionQuery.Penetration(box, InteractionShape.WorldBox(new Vector2(4f, 0f), Vector2.one, null)) < 0f,
                "Separated boxes overlap");
            float startingDepth = WorldInteractionQuery.Penetration(InteractionShape.Capsule(new Vector2(.9f, 0f), new Vector2(.9f, 0f), .1f), box);
            Require(WorldInteractionQuery.Penetration(InteractionShape.Capsule(new Vector2(.9f, 0f), new Vector2(-2f, 0f), .1f), box) > startingDepth,
                "Overlapped sweep could tunnel through the opposite box edge");
        }
        private static void CheckRegistry()
        {
            using (var f = new Fixture())
            {
                WorldInteraction grab = f.Item(Vector2.zero, Vector2.one, 1);
                WorldInteraction solid = grab.gameObject.AddComponent<WorldInteraction>();
                InteractionShape point = InteractionShape.Capsule(Vector2.zero, Vector2.zero, 0f);
                Require(WorldInteractionQuery.Query(point, WorldInteractionKind.Grabbable, null, f.Scene), "Grab registry missing");
                Require(WorldInteractionQuery.Query(point, WorldInteractionKind.Collision, null, f.Scene), "Multiple components not supported");
                solid.enabled = false;
                Require(!WorldInteractionQuery.Query(point, WorldInteractionKind.Collision, null, f.Scene), "Disabled solid still registered");
                HeightMapObstacleFootprint legacy = grab.gameObject.AddComponent<HeightMapObstacleFootprint>();
                Require(WorldInteractionQuery.Query(point, WorldInteractionKind.Collision, null, f.Scene), "Legacy tree not in unified registry");
                InteractionShape start = InteractionShape.Capsule(new Vector2(.25f, 0f), new Vector2(.25f, 0f), .1f);
                Require(!WorldInteractionQuery.Query(InteractionShape.Capsule(new Vector2(.25f, 0f), Vector2.right, .1f),
                    WorldInteractionKind.Collision, null, f.Scene, previous: start), "Cannot escape existing circle contact");
                Require(WorldInteractionQuery.Query(InteractionShape.Capsule(new Vector2(.25f, 0f), Vector2.zero, .1f),
                    WorldInteractionKind.Collision, null, f.Scene, previous: start), "Inward movement escaped collision check");
                legacy.enabled = false;
                Require(!WorldInteractionQuery.Query(point, WorldInteractionKind.Collision, null, f.Scene), "Disabled legacy obstacle remains");
            }
        }
        private static void CheckInteractionKinds()
        {
            using (var f = new Fixture())
            {
                WorldInteraction item = f.Item(Vector2.zero, Vector2.one, 1);
                item.SetKind(WorldInteractionKind.BodyCollision | WorldInteractionKind.Grabbable | WorldInteractionKind.Pushable);
                Set(item, "pushSpeedMultiplier", .35f);
                InteractionShape point = InteractionShape.Capsule(Vector2.zero, Vector2.zero, 0f);
                Require(!WorldInteractionQuery.Query(point, WorldInteractionKind.Collision, null, f.Scene),
                    "Body-only object blocked the mechanical arm");
                Require(WorldInteractionQuery.Query(point,
                    WorldInteractionKind.Collision | WorldInteractionKind.BodyCollision, null, f.Scene),
                    "Body-only object did not block the body mask");
                Require(WorldInteractionQuery.Query(point, WorldInteractionKind.Grabbable, null, f.Scene)
                    && item.TryGrab(f.Arms), "Body-only object could not be grabbed");
                Require(Mathf.Approximately(item.PushSpeedMultiplier, .35f), "Per-object push resistance was lost");
                item.Release(f.Arms);
            }
        }
        private static void CheckQuadTree()
        {
            using (var f = new Fixture())
            using (var other = new Fixture())
            {
                var random = new System.Random(741);
                var items = new List<WorldInteraction>();
                var actual = new List<WorldInteraction>();
                var expected = new HashSet<WorldInteraction>();
                float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);
                for (int i = 0; i < 100; i++)
                {
                    var item = f.Item(new Vector2(Range(-20, 20), Range(-20, 20)),
                        new Vector2(Range(.1f, 4), Range(.1f, 4)), 1);
                    item.SetKind((WorldInteractionKind)(1 + i % 3));
                    item.WorldRotation = Quaternion.Euler(0, 0, Range(-180, 180));
                    items.Add(item);
                }
                items.Add(f.Solid(Vector2.zero, new Vector2(80, .5f))); // Parent-resident object.
                var edge = f.Solid(new Vector2(100, 100), Vector2.one * 2);
                Compare(InteractionShape.Capsule(new Vector2(101, 100), new Vector2(101, 100), 0),
                    WorldInteractionKind.Collision, f.Scene);
                for (int step = 0; step < 240; step++)
                {
                    WorldInteraction changed = items[step % items.Count];
                    changed.WorldPosition = new Vector2(Range(-40, 40), Range(-40, 40));
                    changed.WorldRotation = Quaternion.Euler(0, 0, Range(-180, 180));
                    changed.LocalSize = new Vector2(Range(.1f, 6), Range(.1f, 6));
                    changed.SetKind((WorldInteractionKind)(step % 4));
                    changed.enabled = step % 7 != 0;
                    Vector2 a = new Vector2(Range(-40, 40), Range(-40, 40));
                    InteractionShape shape = step % 2 == 0
                        ? InteractionShape.Capsule(a, a + new Vector2(Range(-15, 15), Range(-15, 15)), Range(0, 2))
                        : InteractionShape.WorldBox(a, new Vector2(Range(0, 12), Range(0, 12)), null);
                    Compare(shape, (WorldInteractionKind)(step % 4), f.Scene,
                        step % 3 == 0 ? InteractionShape.Capsule(a, a, 1) : (InteractionShape?)null,
                        step % 5 == 0 ? changed.transform : null);
                }
                edge.MoveToScene(other.Scene);
                var edgePoint = InteractionShape.Capsule(new Vector2(100, 100), new Vector2(100, 100), 0);
                Compare(edgePoint, WorldInteractionKind.Collision, f.Scene);
                Compare(edgePoint, WorldInteractionKind.Collision, other.Scene);
                edge.gameObject.SetActive(false);
                Compare(edgePoint, WorldInteractionKind.Collision, other.Scene);
                edge.gameObject.SetActive(true);
                Compare(edgePoint, WorldInteractionKind.Collision, other.Scene);
                Object.DestroyImmediate(edge.gameObject);
                Compare(edgePoint, WorldInteractionKind.Collision, other.Scene);

                void Compare(InteractionShape shape, WorldInteractionKind mask, Scene scene,
                    InteractionShape? previous = null, Transform ignore = null)
                {
                    expected.Clear();
                    foreach (WorldInteraction item in WorldInteraction.Active)
                    {
                        if (item == null || !item.Available || (item.Kind & mask) == 0 || item.gameObject.scene != scene ||
                            (ignore != null && (item.transform == ignore || item.transform.IsChildOf(ignore)))) continue;
                        InteractionShape obstacle = item.GetShape(null);
                        float depth = WorldInteractionQuery.Penetration(shape, obstacle);
                        if (depth < 0 || (previous.HasValue &&
                            WorldInteractionQuery.Penetration(previous.Value, obstacle) >= depth - .000001f)) continue;
                        expected.Add(item);
                    }
                    bool found = WorldInteractionQuery.Query(shape, mask, null, scene, actual, ignore, previous);
                    Require(found == (expected.Count > 0) && expected.SetEquals(actual) && actual.Count == expected.Count,
                        "QuadTree result differs from exhaustive query");
                    Require(WorldInteractionQuery.Query(shape, mask, null, scene, ignore: ignore, previous: previous) == found,
                        "QuadTree early exit differs from collected results");
                    Require(WorldInteractionQuery.Query(shape, mask, null, scene, ignoreHeld: ignore, previous: previous) == found,
                        "QuadTree held hierarchy filtering differs from ignore filtering");
                }
            }
        }

        private static void CheckSpatialDirty()
        {
            using (var f = new Fixture())
            {
                var probes = new List<WorldInteractionQueryProbe>();
                var hits = new List<WorldInteraction>();
                for (int i = 0; i < 32; i++)
                {
                    var go = new GameObject("Spatial dirty probe");
                    SceneManager.MoveGameObjectToScene(go, f.Scene);
                    var probe = go.AddComponent<WorldInteractionQueryProbe>();
                    probe.WorldPosition = new Vector3(i * 4, 0, 0);
                    probes.Add(probe);
                }
                bool Query(Vector2 point, WorldInteractionKind mask = WorldInteractionKind.Collision) =>
                    WorldInteractionQuery.Query(InteractionShape.Capsule(point, point, 0), mask, null, f.Scene, hits);
                Query(Vector2.zero);
                foreach (var probe in probes) probe.ShapeReads = 0;
                for (int i = 0; i < 10; i++) Query(Vector2.zero);
                Require(probes.TrueForAll(p => p.ShapeReads == 0), "Unchanged queries still read registered shapes");

                var moved = probes[0];
                moved.WorldPosition = new Vector3(0, 10, 0);
                moved.LocalSize = Vector2.one * 2;
                moved.MarkSpatialDirty();
                moved.MarkSpatialDirty();
                Require(Query(new Vector2(0, 10)) && hits.Contains(moved), "Dirty movement missing from next query");
                Require(moved.ShapeReads == 1 && probes.GetRange(1, 31).TrueForAll(p => p.ShapeReads == 0),
                    "Dirty queue failed to coalesce or refreshed unchanged entries");
                Require(!Query(Vector2.zero), "Old cached position still hits");

                moved.SetKind(WorldInteractionKind.Grabbable);
                Require(!Query(new Vector2(0, 10)) && Query(new Vector2(0, 10), WorldInteractionKind.Grabbable),
                    "KindMask did not follow notified kind changes");
                moved.SetKind(WorldInteractionKind.Collision);
                moved.transform.position = new Vector3(0, 20, 0);
                moved.MarkSpatialDirty();
                Require(Query(new Vector2(0, 20)) && hits.Contains(moved), "Manual notification failed");

                var sibling = moved.gameObject.AddComponent<WorldInteraction>();
                var child = probes[1];
                child.SetParent(moved.transform);
                child.LocalPosition = Vector3.right * 3;
                Query(new Vector2(0, 20));
                moved.WorldPosition = new Vector3(0, 30, 0);
                Require(Query(new Vector2(0, 30)) && hits.Contains(moved) && hits.Contains(sibling),
                    "Transform wrapper did not update sibling interaction");
                Require(Query(new Vector2(3, 30)) && hits.Contains(child), "Transform wrapper did not update descendant");

                moved.UseCustomShape = true;
                moved.CustomShape = InteractionShape.Capsule(new Vector2(-1, -1), new Vector2(1, 1), .1f);
                moved.MarkSpatialDirty();
                Require(Query(new Vector2(.8f, .8f)) && hits.Contains(moved), "Custom shape missing");
                moved.CustomShape = InteractionShape.Capsule(new Vector2(-1, 1), new Vector2(1, -1), .1f);
                moved.MarkSpatialDirty();
                Require(!Query(new Vector2(.8f, .8f)), "Shape cache not refreshed when AABB stayed identical");

                moved.UseCustomShape = false;
                moved.WorldPosition = Vector3.up * 1000;
                Require(Query(Vector2.up * 1000) && hits.Contains(moved), "Root expansion lost dirty object");
                moved.MarkSpatialDirty();
                moved.enabled = false;
                Query(Vector2.up * 1000);
                Require(!hits.Contains(moved), "Disabled dirty entry reinserted");
                moved.enabled = true;
                Query(Vector2.up * 1000);
                Require(hits.Contains(moved), "Reenabled entry missing");

                var source = moved.gameObject.AddComponent<BoxCollider2D>();
                moved.BoxSource = source;
                moved.WorldPosition = Vector3.zero;
                Query(Vector2.zero);
                source.offset = Vector2.up * 10;
                source.size = Vector2.one * 2;
                moved.MarkSpatialDirty();
                Require(Query(Vector2.up * 10) && hits.Contains(moved), "External collider edit notification failed");

                foreach (var probe in probes) probe.ShapeReads = 0;
                WorldInteractionQuery.InvalidateSpatialIndex();
                Query(Vector2.zero);
                Require(probes.TrueForAll(p => p.ShapeReads == 1), "Global invalidation did not refresh every shape once");
                foreach (var probe in probes) probe.ShapeReads = 0;
                Query(Vector2.zero);
                Require(probes.TrueForAll(p => p.ShapeReads == 0), "Global invalidation stayed active after flush");
            }
        }

        private static void CheckGrabbing()
        {
            using (var f = new Fixture())
            {
                f.Tick(90, Vector2.up, true);
                Require(f.Arms.HeldObject == null, "Empty grab fabricated an object");
                WorldInteraction item = f.Item(f.Arms.LeftHandWorld, Vector2.one * .05f, 2);
                f.Tick(2, Vector2.up, true);
                Require(f.Arms.HeldObject == null && item.Owner == null, "One hand moved a two-hand object");
                item.LocalSize = new Vector2(.5f, .1f);
                item.WorldPosition = (f.Arms.LeftHandWorld + f.Arms.RightHandWorld) * .5f;
                f.Tick(2, Vector2.up, true);
                Require(f.Arms.HeldObject == item, "Sustained A did not retry a now-valid two-hand grab");
                f.Tick(1, Vector2.up, false);
                Require(f.Arms.HeldObject == null && item.Owner == null && item.gameObject.activeSelf, "Early A release did not drop");
                item.gameObject.SetActive(false);
                WorldInteraction single = f.Item(f.Arms.LeftHandWorld, Vector2.one * .05f, 1);
                f.Tick(1, Vector2.up, true);
                Require(f.Arms.HeldObject == single, "Single hand grab failed");
            }
        }
        private static void CheckDocking()
        {
            using (var f = new Fixture())
            {
                f.Tick(90, Vector2.up, true);
                WorldInteraction item = f.Item((f.Arms.LeftHandWorld + f.Arms.RightHandWorld) * .5f, new Vector2(.5f, .1f), 2);
                f.Tick(1, Vector2.up, true);
                f.Tick(4, Vector2.up * .17f, true);
                Require(f.Arms.State == RobotArmState.OuterOperating, "Dock entered before .10 seconds");
                f.Tick(3, Vector2.up * .17f, true);
                Require(f.Arms.State == RobotArmState.Docking, "Raw .17 did not enter dock after delay");
                f.Tick(5, Vector2.up * .22f, true);
                Require(f.Arms.State == RobotArmState.Docking, "Hysteresis did not retain docking");
                f.Tick(1, Vector2.up * .27f, true);
                Require(f.Arms.State == RobotArmState.OuterOperating, ".27 did not leave docking");
                f.Tick(150, Vector2.zero, true);
                Require(f.Arms.IsRecycleReady, "Held object never reached inlet");
                f.Tick(1, Vector2.zero, false);
                Require(f.Arms.State == RobotArmState.Recycling && item.gameObject.activeSelf, "Ready release skipped recycle animation");
                f.Tick(40, Vector2.zero, false);
                Require(!item.gameObject.activeSelf && f.Arms.HeldObject == null, "Recycle failed to finish/clear ownership");
            }
        }
        private static void CheckRecycleAnimation()
        {
            foreach (bool medium in new[] { false, true })
            using (var f = new Fixture())
            {
                f.Tick(90, Vector2.up, true);
                WorldInteraction item = f.Item(medium ? (f.Arms.LeftHandWorld + f.Arms.RightHandWorld) * .5f
                    : f.Arms.LeftHandWorld, medium ? new Vector2(.5f, .1f) : Vector2.one * .05f, medium ? 2 : 1);
                Set(item, "size", medium ? RecyclableSize.Medium : RecyclableSize.Small);
                Vector3 originalScale = new Vector3(1.2f, .8f, 1f);
                item.LocalScale = originalScale;
                f.Tick(1, Vector2.up, true);
                f.Tick(150, Vector2.zero, true);
                Require(f.Arms.IsRecycleReady, "Recycle fixture failed to dock");
                f.Tick(1, Vector2.zero, false);
                Vector3 start = item.WorldPosition;
                Vector2 handStart = f.Arms.LeftHandWorld;
                Call(f.Arms, "Step", .05f, Vector2.right, true, false);
                Require(Vector2.Distance(f.Arms.LeftHandWorld - handStart, (Vector2)(item.WorldPosition - start)) < .0002f,
                    "Recycle hand did not track garbage displacement or was steered by input");
                Require(medium ? item.LocalScale.x < originalScale.x : item.LocalScale == originalScale,
                    "Wrong size changed scale during recycling");
                if (medium)
                {
                    // Land inside each pause, then verify both object and hands remain still.
                    foreach (float pauseTime in new[] { .24f, .58f })
                    {
                        Call(f.Arms, "Step", pauseTime - (float)Get(f.Arms, "recycleTime"), Vector2.zero, true, false);
                        Vector3 pausedPosition = item.WorldPosition, pausedScale = item.LocalScale;
                        Vector2 pausedHand = f.Arms.LeftHandWorld;
                        Call(f.Arms, "Step", .05f, Vector2.zero, true, false);
                        Require(Vector3.Distance(item.WorldPosition, pausedPosition) < .00001f
                            && item.LocalScale == pausedScale && Vector2.Distance(f.Arms.LeftHandWorld, pausedHand) < .0002f,
                            "Medium recycle failed to pause position, scale and hands");
                    }
                    Call(f.Arms, "Step", .2f, Vector2.zero, true, false);
                    Require(item.LocalScale.x < originalScale.x * .5f, "Medium third movement did not shrink");
                }
                else Call(f.Arms, "Step", .24f, Vector2.zero, true, false);
                Vector2 stoppedHand = f.Arms.LeftHandWorld;
                Vector3 before = item.WorldPosition;
                Call(f.Arms, "Step", .01f, Vector2.zero, true, false);
                Require(Vector2.Distance(stoppedHand, f.Arms.LeftHandWorld) < .0002f
                    && Vector3.Distance(before, item.WorldPosition) > .00001f, "Hand did not stop while garbage continued inward");
                if (medium)
                {
                    f.Tick(1, Vector2.zero, false, false);
                    Require(item.LocalScale == originalScale && item.Owner == null && item.gameObject.activeSelf,
                        "Cancelled recycle did not restore size and release garbage");
                    // Also verify completion with a delta that crosses all five phases.
                    Require(item.TryGrab(f.Arms), "Could not regrab cancelled garbage");
                    Set(f.Arms, "heldObject", item);
                    Set(f.Arms, "heldHands", 3);
                    Call(f.Arms, "BeginRecycle");
                }
                Call(f.Arms, "Step", 2f, Vector2.zero, true, false);
                Require(!item.gameObject.activeSelf && item.Owner == null && f.Arms.HeldObject == null
                    && item.LocalScale == originalScale, "Recycle completion failed to clear ownership or restore reusable scale");
                Require(((Vector2)item.WorldPosition - (Vector2)f.Root.transform.position).magnitude < .0001f,
                    "Recycled garbage did not reach player center");
            }
        }
        private static void CheckRetracting()
        {
            using (var f = new Fixture())
            {
                f.Tick(90, Vector2.up, true);
                WorldInteraction item = f.Item(f.Arms.LeftHandWorld, Vector2.one * .05f, 1);
                f.Tick(1, Vector2.up, true);
                f.Tick(1, Vector2.zero, true, false);
                Require(f.Arms.State == RobotArmState.Retracting && f.Arms.HeldObject == null && item.Owner == null,
                    "L3 release failed to drop before retracting");
                Vector3 dropped = item.transform.position;
                f.Tick(60, Vector2.zero, true, false);
                Require(f.Arms.State == RobotArmState.Retracted && item.transform.position == dropped, "Dropped object moved with retracting arm");
            }
        }
        private static void CheckDeploymentCollision()
        {
            using (var f = new Fixture())
            {
                WorldInteraction obstacle = f.Solid(new Vector2(-.3f, .1f), new Vector2(.5f, .12f));
                WorldInteraction item = f.Item(Vector2.zero, Vector2.one * 3f, 1);
                f.Tick(120, Vector2.up, true);
                Require(f.Arms.State == RobotArmState.Extending && f.Arms.HeldObject == null && f.Arms.IsBlocked,
                    "Blocked deployment allowed grabbing or finished");
                obstacle.enabled = false;
                f.Tick(120, Vector2.up, true);
                Require(f.Arms.HeldObject == item, "Removing obstacle did not resume deployment/grab");
                f.Tick(1, Vector2.up, false);
                obstacle.enabled = true;
                f.Tick(1, Vector2.up, true);
                Require(f.Arms.HeldObject == item, "Post-deployment collision incorrectly disabled grab");
            }
        }
        private static void CheckBodyCollision()
        {
            using (var f = new Fixture())
            {
                f.Tick(90, Vector2.up, false);
                f.Solid(new Vector2(0f, .85f), new Vector2(2f, .04f));
                Call(f.Arms, "ConstrainBodyPose", Vector3.zero, Quaternion.identity, Vector3.up, Quaternion.identity);
                Require(f.Arms.transform.position.y < .5f && f.Arms.IsBlocked, "Body carried arms through wall");
            }
            using (var f = new Fixture())
            {
                f.Tick(90, Vector2.up, false);
                f.Solid(new Vector2(.5f, 0f), new Vector2(.05f, .8f));
                Call(f.Arms, "ConstrainBodyPose", Vector3.zero, Quaternion.identity, Vector3.zero, Quaternion.Euler(0f, 0f, -90f));
                Require(Quaternion.Angle(f.Arms.transform.rotation, Quaternion.Euler(0f, 0f, -90f)) > 1f && f.Arms.IsBlocked,
                    "Body rotation swept arms through obstacle");
            }
        }
        private static void CheckTurning()
        {
            using (var f = new Fixture())
            {
                RobotMover mover = f.Root.GetComponent<RobotMover>();
                Set(mover, "turnSpeed", 60f);
                Set(mover, "overallMotionScale", .88f);
                f.Root.transform.rotation = Quaternion.Euler(0f, 0f, 125f);
                Quaternion heading = f.Root.transform.rotation;
                f.Tick(120, Vector2.up, false);
                Require(Quaternion.Angle(heading, f.Root.transform.rotation) < .001f,
                    "Local forward input rotated an already-rotated player");
                Require(Vector2.Angle((Vector2)Get(f.Arms, "targetLocal"), Vector2.up) < .1f
                    && f.Arms.CurrentTargetLocal == Vector2.up, "Forward input is not body-local");
                f.Tick(60, Vector2.right, false);
                Require(Mathf.Abs(Mathf.DeltaAngle(heading.eulerAngles.z, f.Root.transform.eulerAngles.z) + 52.8f) < .1f,
                    "Right local input did not use the scaled base turning speed");
                heading = f.Root.transform.rotation;
                f.Tick(60, Vector2.right, false);
                Require(Mathf.Abs(Mathf.DeltaAngle(heading.eulerAngles.z, f.Root.transform.eulerAngles.z) + 52.8f) < .1f,
                    "Sustained local input stopped turning after reaching a world heading");
                heading = f.Root.transform.rotation;
                f.Tick(60, Direction(-69f), false);
                Require(Quaternion.Angle(heading, f.Root.transform.rotation) < .001f,
                    "Body kept turning after local input returned below 70 degrees");
                f.Tick(60, Vector2.left, false);
                Require(Mathf.Abs(Mathf.DeltaAngle(heading.eulerAngles.z, f.Root.transform.eulerAngles.z) - 52.8f) < .1f,
                    "Left local input did not rotate counterclockwise");
                heading = f.Root.transform.rotation;
                f.Tick(1, Vector2.zero, false);
                Require(Quaternion.Angle(heading, f.Root.transform.rotation) < .001f, "Centred stick kept turning");
                Set(mover, "turnSpeed", 90f);
                Set(mover, "overallMotionScale", .5f);
                heading = f.Root.transform.rotation;
                f.Tick(60, Vector2.right, false);
                Require(Mathf.Abs(Mathf.DeltaAngle(heading.eulerAngles.z, f.Root.transform.eulerAngles.z) + 45f) < .1f,
                    "Arm turning did not follow updated base movement settings");
                Set(mover, "overallMotionScale", 0f);
                heading = f.Root.transform.rotation;
                f.Tick(60, Vector2.right, false);
                Require(Quaternion.Angle(heading, f.Root.transform.rotation) < .001f,
                    "Arm turning ignored zero overall motion scale");
                Set(mover, "turnSpeed", 60f);
                Set(mover, "overallMotionScale", .88f);
                float upper = (float)Get(f.Arms, "upperLength");
                Set(f.Arms, "armLength", 8f);
                f.Tick(1, Vector2.right, false);
                Require((float)Get(f.Arms, "upperLength") == upper, "Runtime inspector edit changed fixed length");
                var tumble = f.Root.AddComponent<RobotTumbleController>();
                typeof(RobotTumbleController).GetProperty("State").SetValue(tumble, RobotTumbleState.Fallen);
                Set(f.Arms, "tumble", tumble);
                Quaternion rotation = f.Root.transform.rotation;
                f.Item(Vector2.zero, Vector2.one * 10f, 1);
                f.Tick(180, Vector2.left, true);
                Require(f.Arms.HeldObject == null && Quaternion.Angle(rotation, f.Root.transform.rotation) < .001f,
                    "Fallen robot grabbed or rotated");
                Vector2 target = (Vector2)Get(f.Arms, "targetLocal");
                Require(Mathf.Abs(Vector2.SignedAngle(Vector2.up, target)) <= 70.01f, "Fallen arm exceeded angle limit");
            }
        }
        private static void CheckGrabResistance()
        {
            using (var f = new Fixture())
            {
                RobotMover mover = f.Root.GetComponent<RobotMover>();
                mover.SetGrabResistance(0f);
                Require(Mathf.Approximately(mover.GrabMovementMultiplier, 1f), "Zero resistance slowed movement");
                mover.SetGrabResistance(.5f);
                Require(Mathf.Approximately(mover.GrabMovementMultiplier, .5f), "Half resistance used wrong multiplier");
                typeof(RobotMover).GetProperty("CurrentSpeed").SetValue(mover, 2f);
                typeof(RobotMover).GetProperty("CurrentTerrainVelocity").SetValue(mover, Vector2.right);
                mover.SetGrabResistance(1f);
                Require(mover.CurrentSpeed == 0f && mover.CurrentTerrainVelocity == Vector2.zero,
                    "Full resistance retained forward or terrain velocity");
                f.Tick(90, Vector2.up, true);
                WorldInteraction item = f.Item(f.Arms.LeftHandWorld, Vector2.one * .05f, 1);
                Set(item, "grabResistance", 1f);
                f.Tick(1, Vector2.up, true);
                Require(f.Arms.HeldObject == item && mover.GrabMovementMultiplier == 0f,
                    "Grabbing did not apply full resistance immediately");
                Vector3 itemPosition = item.transform.position;
                Quaternion heading = f.Root.transform.rotation;
                f.Tick(30, Vector2.right, true);
                Require(item.transform.position == itemPosition && f.Root.transform.rotation == heading,
                    "Full resistance moved the held object or rotated the player");
                f.Tick(1, Vector2.zero, false);
                Require(mover.GrabMovementMultiplier == 1f, "Release left grab resistance active");
            }
        }

        private static void CheckStoppedSteering()
        {
            using (var f = new Fixture())
            {
                RobotMover mover = f.Root.GetComponent<RobotMover>();
                var speed = typeof(RobotMover).GetProperty("CurrentSpeed");
                var turn = typeof(RobotMover).GetProperty("CurrentTurnSpeed");
                Require((float)Get(mover, "lastMovingSpeedSign") == 1f,
                    "Initial stationary steering should use the forward direction");
                foreach (float sign in new[] { -1f, 1f })
                {
                    // A slow movement still determines direction, even below the old .05 threshold.
                    speed.SetValue(mover, sign * .01f);
                    Require((float)Get(mover, "lastMovingSpeedSign") == sign,
                        "Nonzero speed did not update steering direction");
                    Call(mover, "UpdateTurning", 1f, false);
                    mover.SetPhotoModeInputLocked(true);
                    mover.SetPhotoModeInputLocked(false);
                    Require(mover.CurrentSpeed == 0f && (float)Get(mover, "lastMovingSpeedSign") == sign,
                        "An external stop erased the previous movement direction");
                    speed.SetValue(mover, -sign * Mathf.Epsilon);
                    Require((float)Get(mover, "lastMovingSpeedSign") == sign,
                        "Approximately zero speed changed the remembered direction");
                    speed.SetValue(mover, 0f);
                    f.Root.transform.rotation = Quaternion.identity;
                    turn.SetValue(mover, 100f);
                    Call(mover, "UpdateTurning", 1f, false);
                    Quaternion expected = Quaternion.Euler(0f, 0f, -100f * sign * Time.deltaTime);
                    Require(Quaternion.Angle(f.Root.transform.rotation, expected) < .001f,
                        "Stationary steering did not use the last movement direction");
                    Require((float)Get(mover, "lastMovingSpeedSign") == sign,
                        "Continuous steering lost direction at rest");
                    Call(mover, "UpdateTurning", 0f, false);
                    Require((float)Get(mover, "lastMovingSpeedSign") == 1f,
                        "Releasing steering at rest did not reset direction");
                    Call(mover, "UpdateTurning", 1f, false);
                    Require((float)Get(mover, "lastMovingSpeedSign") == 1f,
                        "Resuming stationary steering restored the old reverse direction");
                }
                speed.SetValue(mover, -1f);
                Call(mover, "UpdateTurning", 0f, false);
                Require((float)Get(mover, "lastMovingSpeedSign") == -1f,
                    "Releasing steering while moving changed the movement direction");
                speed.SetValue(mover, 0f);
                Call(mover, "UpdateTurning", 1f, false);
                Require((float)Get(mover, "lastMovingSpeedSign") == 1f,
                    "Starting a new turn at rest incorrectly retained reverse direction");
            }
        }

        private static void CheckHeldReplacement()
        {
            using (var f = new Fixture())
            {
                f.Tick(90, Vector2.up, true);
                WorldInteraction oldItem = f.Item(
                    (f.Arms.LeftHandWorld + f.Arms.RightHandWorld) * .5f,
                    new Vector2(.5f, .1f), 2);
                Set(oldItem, "grabResistance", 1f);
                f.Tick(1, Vector2.up, true);
                Require(f.Arms.HeldObject == oldItem && (int)Get(f.Arms, "heldHands") == 3,
                    "Fixture did not grab the two-hand source");
                WorldInteraction replacement = f.Item(oldItem.transform.position, Vector2.one * .05f, 1);
                Set(replacement, "grabResistance", .5f);
                Require(f.Arms.TryReplaceHeldObject(oldItem, replacement), "Direct handoff failed");
                Require(f.Arms.HeldObject == replacement && replacement.Owner == f.Arms && oldItem.Owner == null,
                    "Handoff left ownership on the old object");
                Require((int)Get(f.Arms, "heldHands") != 3
                    && Mathf.Approximately(f.Root.GetComponent<RobotMover>().GrabMovementMultiplier, .5f),
                    "Handoff kept the old hand count or resistance");
                f.Tick(1, Vector2.zero, false);
                Require(f.Arms.HeldObject == null && replacement.Owner == null && oldItem.Owner == null,
                    "Release after handoff retained an owner");
            }
        }

        private static void CheckHeldFragmentSpawn()
        {
            using (var f = new Fixture())
            {
                f.Root.transform.SetPositionAndRotation(new Vector3(3f, -2f), Quaternion.Euler(0f, 0f, 35f));
                f.Tick(90, Vector2.up, true);
                Vector2 midpoint = (f.Arms.LeftHandWorld + f.Arms.RightHandWorld) * .5f;
                WorldInteraction source = f.Item(midpoint, new Vector2(.5f, .5f), 2);
                f.Tick(1, Vector2.up, true);
                Require(f.Arms.HeldObject == source, "Fixture did not grab the source");
                midpoint = (f.Arms.LeftHandWorld + f.Arms.RightHandWorld) * .5f;
                source.WorldPosition += new Vector3(.12f, -.08f, .3f);
                GarbageFragmentSpawner spawner = source.gameObject.AddComponent<GarbageFragmentSpawner>();
                Call(spawner, "Awake");
                Set(spawner, "arm", f.Arms);
                Set(spawner, "player", f.Root.GetComponent<RobotMover>());
                Set(spawner, "placementAttempts", 0);
                WorldInteraction template = f.Item(new Vector2(20f, 20f), Vector2.one * .05f, 1);
                Set(spawner, "heldFragments", new GameObject[] { null, template.gameObject });
                f.Solid(midpoint, Vector2.one * 2f);
                Require(WorldInteractionQuery.Query(InteractionShape.Capsule(midpoint, midpoint, 0f),
                    WorldInteractionKind.Collision, null, f.Scene), "Fixture has no overlapping obstacle");
                object[] args = { null };
                bool spawned = (bool)typeof(GarbageFragmentSpawner).GetMethod("TrySpawnHeldFragment",
                    BindingFlags.Instance | BindingFlags.NonPublic).Invoke(spawner, args);
                WorldInteraction fragment = args[0] as WorldInteraction;
                try
                {
                    Require(spawned && fragment != null, "Overlap or placement attempts prevented held spawning");
                    Vector3 expected = new Vector3(midpoint.x, midpoint.y, source.WorldPosition.z);
                    Require(Vector3.Distance(fragment.WorldPosition, expected) < .0001f,
                        "Held fragment did not spawn at the hand midpoint");
                    Require(fragment.TryGetComponent(out GarbageMotion motion) && motion.Velocity == Vector2.zero,
                        "Held fragment has missing motion or was launched");
                    Require(f.Arms.TryReplaceHeldObject(source, fragment)
                        && fragment.Owner == f.Arms && source.Owner == null, "Spawned fragment handoff failed");
                    Call(f.Arms, "FollowHeldObject");
                    Require(Vector3.Distance(fragment.WorldPosition, expected) < .0001f,
                        "Handoff moved the fragment away from the hand midpoint");
                    f.Arms.ReleaseHeldObject(fragment);
                }
                finally { if (fragment != null) Object.DestroyImmediate(fragment.gameObject); }
            }
        }

        private static void CheckHeavyPullIntent()
        {
            using (var f = new Fixture())
            {
                f.Tick(90, Vector2.up, true);
                WorldInteraction item = f.Item(
                    (f.Arms.LeftHandWorld + f.Arms.RightHandWorld) * .5f,
                    new Vector2(.5f, .1f), 2);
                Set(item, "grabResistance", 1f);
                HeavyGarbagePull pull = item.gameObject.AddComponent<HeavyGarbagePull>();
                Call(pull, "Awake");
                Set(pull, "requiredPullDuration", 2f);
                Set(pull, "requiredPullIntentDistance", 10f);
                f.Tick(1, Vector2.up, true);
                Require(f.Arms.HeldObject == item, "Fixture did not grab heavy garbage");
                RobotMover mover = f.Root.GetComponent<RobotMover>();
                Vector2 away = ((Vector2)f.Root.transform.position - (Vector2)item.transform.position).normalized;
                var throttle = typeof(RobotMover).GetProperty("CurrentThrottleIntent");
                var velocity = typeof(RobotMover).GetProperty("UnresistedMovementIntentWorld");
                throttle.SetValue(mover, -1f);
                velocity.SetValue(mover, -away * 3f);
                Call(pull, "Step", .05f);
                Require((float)Get(pull, "pullTime") == 0f, "Moving toward garbage counted as pulling");
                throttle.SetValue(mover, 1f);
                velocity.SetValue(mover, away * 3f);
                Call(pull, "Step", .05f);
                Require((float)Get(pull, "pullTime") == 0f, "Forward throttle counted as pulling");
                throttle.SetValue(mover, -1f);
                for (int i = 0; i < 8; i++) Call(pull, "Step", .05f);
                Require(Mathf.Abs((float)Get(pull, "pullTime") - .4f) < .0001f
                    && Mathf.Abs((float)Get(pull, "pullDistance") - 1.2f) < .0001f,
                    "Valid pull did not integrate time and unresisted intended distance");
                Call(pull, "Step", .2f);
                Require(Mathf.Abs((float)Get(pull, "pullTime") - .6f) < .0001f
                    && Mathf.Abs((float)Get(pull, "pullDistance") - 1.8f) < .0001f,
                    "Pull accumulation depends on frame rate");
                velocity.SetValue(mover, Vector2.zero);
                Call(pull, "Step", .05f);
                Require((float)Get(pull, "pullTime") == 0f && (float)Get(pull, "pullDistance") == 0f,
                    "Interrupted pull retained progress");
            }
        }
        private static Vector2 Direction(float angle) => (Vector2)(Quaternion.Euler(0f, 0f, angle) * Vector3.up);
        private static void Set(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        private static object Get(object target, string name) =>
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
        private static void Call(object target, string name, params object[] args) =>
            target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args);

        private sealed class Fixture : IDisposable
        {
            public Scene Scene { get; }
            public GameObject Root { get; }
            public RobotArmController Arms { get; }
            public Fixture()
            {
                Scene = EditorSceneManager.NewPreviewScene();
                Root = new GameObject("Arm regression robot");
                SceneManager.MoveGameObjectToScene(Root, Scene);
                Arms = Root.AddComponent<RobotArmController>();
                // Edit-mode fixtures only need the existing marker coordinate frame, not UI/art generation.
                RobotMarkerView marker = Root.GetComponent<RobotMarkerView>();
                var visual = new GameObject("Marker Visual Root"); visual.transform.SetParent(Root.transform, false);
                Set(marker, "markerVisualRoot", visual.transform);
                Call(Arms, "Awake"); Call(Arms, "EnsureVisuals");
            }
            public void Tick(int frames, Vector2 stick, bool grab, bool deploy = true)
            {
                for (int i = 0; i < frames; i++) Call(Arms, "Step", 1f / 60f, stick, deploy, grab);
            }
            public WorldInteraction Item(Vector2 position, Vector2 size, int hands)
            {
                var go = new GameObject("Regression item"); SceneManager.MoveGameObjectToScene(go, Scene);
                go.transform.position = position;
                var item = go.AddComponent<WorldInteraction>();
                item.SetKind(WorldInteractionKind.Grabbable); item.LocalSize = size; Set(item, "requiredHands", hands);
                return item;
            }
            public WorldInteraction Solid(Vector2 position, Vector2 size)
            { WorldInteraction item = Item(position, size, 1); item.SetKind(WorldInteractionKind.Collision); return item; }
            public void Dispose()
            {
                // Destroy children before the component's runtime cleanup (Destroy is deferred in play mode).
                Arms.enabled = false;
                Transform frame = Root.GetComponent<RobotMarkerView>().MarkerVisualRoot;
                for (int i = frame.childCount - 1; i >= 0; i--) Object.DestroyImmediate(frame.GetChild(i).gameObject);
                EditorSceneManager.ClosePreviewScene(Scene);
            }
        }
    }
}
