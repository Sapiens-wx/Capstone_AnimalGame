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
        [MenuItem("Animal Game/Validation/Run Arm Scale Checks")]
        public static void RunArmScaleChecks()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Resources/Robot/RobotMarker.prefab");
            foreach (float size in new[] { .5f, 2f })
                foreach (float loopSize in new[] { 0f, 8f })
                {
                    Scene scene = EditorSceneManager.NewPreviewScene();
                    try
                    {
                        var root = new GameObject("Arm scale regression");
                        root.SetActive(false);
                        SceneManager.MoveGameObjectToScene(root, scene);
                        var arms = root.AddComponent<RobotArmController>();
                        EditorUtility.CopySerialized(prefab.GetComponent<RobotArmController>(), arms);
                        Set(arms, "armColor", Color.white);
                        Transform frame = Object.Instantiate(prefab.transform.Find("Marker Visual Root"), root.transform);
                        Set(root.GetComponent<RobotMarkerView>(), "markerVisualRoot", frame);
                        Set(arms, "armScale", size);
                        Set(arms, "handScale", .37f);
                        Set(arms, "lowerArmScale", loopSize);
                        Set(arms, "stepDelta", 1f);
                        Call(arms, "Awake");
                        Call(arms, "EnsureVisuals");
                        float connector = (float)Get(arms, "connectorExtendDuration");
                        float extension = (float)Get(arms, "extendDuration");
                        float total = connector + extension + (float)Get(arms, "handExtendDuration");
                        foreach (string side in new[] { "left", "right" })
                        {
                            object arm = Get(arms, side);
                            var upper = (SpriteRenderer)arm.GetType().GetField("UpperSprite").GetValue(arm);
                            var lower = (SpriteRenderer)arm.GetType().GetField("LowerSprite").GetValue(arm);
                            var hand = (SpriteRenderer)arm.GetType().GetField("HandSprite").GetValue(arm);
                            Transform loop = lower.transform.Find("Loopable");
                            Vector3 loopPosition = loop.localPosition;
                            Vector3 upperTarget = upper.transform.localScale, lowerTarget = lower.transform.localScale;
                            Transform source = prefab.transform.Find("Marker Visual Root").Find(upper.transform.parent.name);
                            Require(upperTarget == source.Find("Upper Arm").localScale * size
                                && lowerTarget == source.Find("Lower Arm").localScale * size,
                                "Arm Scale did not multiply both authored scales");
                            // Traverse the same phases forward and backward, including repeated deployment.
                            foreach (float time in new[] { 0f, connector * .5f, connector,
                                connector + extension * .5f, total, connector + extension * .5f,
                                connector, connector * .5f, 0f, total })
                            {
                                object pose = typeof(RobotArmController).GetMethod("DesiredPose",
                                    BindingFlags.NonPublic | BindingFlags.Instance).Invoke(arms, new[] { arm, (object)time });
                                arm.GetType().GetField("Pose").SetValue(arm, pose);
                                Set(arms, "deploymentTime", time);
                                Call(arms, "ApplyVisuals", arm);
                                Require(loop.localPosition == loopPosition, "Loopable origin moved during scaling");
                                Require(upper.color.a == 1f && lower.color.a == 1f,
                                    "Deployment faded the arm instead of scaling it");
                                if (time == 0f)
                                    Require(upper.transform.localScale == Vector3.zero && lower.transform.localScale == Vector3.zero
                                        && hand.transform.localScale == Vector3.zero, "Retracted artwork was not zero scale");
                                if (time == connector * .5f)
                                    Require(Vector3.Distance(lower.transform.localScale, lowerTarget * .5f) < .00001f
                                        && upper.transform.localScale == Vector3.zero, "Body-side growth phase failed");
                                if (time == connector + extension * .5f)
                                    Require(Vector3.Distance(upper.transform.localScale, upperTarget * .5f) < .00001f
                                        && lower.transform.localScale == lowerTarget, "Hand-side growth phase failed");
                                if (time == total)
                                    Require(upper.transform.localScale == upperTarget && lower.transform.localScale == lowerTarget
                                        && hand.transform.localScale == Vector3.one * .37f,
                                        "Final arm/hand scales changed or accumulated across deployment");
                                Vector3 upperTip = upper.transform.TransformPoint(new Vector3(0f, upper.sprite.bounds.max.y, 0f));
                                Vector3 handBase = hand.transform.TransformPoint(new Vector3(0f, hand.sprite.bounds.min.y, 0f));
                                Require(Vector3.Distance(upperTip, handBase) < .0001f, "Hand detached from Upper Arm");
                                Vector2 endpoint = side == "left" ? arms.LeftHandWorld : arms.RightHandWorld;
                                Require(Vector2.Distance(endpoint, upperTip) < .0001f,
                                    "Interaction endpoint differs from the visible arm tip");
                            }
                        }
                    }
                    finally { EditorSceneManager.ClosePreviewScene(scene); }
                }
            Debug.Log("Arm scale checks PASS: independent arm/hand scales, Loopable origin, growth/retraction, attachment and interaction endpoints.");
        }

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
            Check("Immediate docking, adjustable raw thresholds, hysteresis and recycle completion", CheckDocking);
            Check("Production dock settings, raw drift, fixed inlet and wide hysteresis", CheckControllerDocking);
            Check("Chest-zone A release starts recycling immediately without docking or alignment", CheckInstantRecycle);
            Check("Chest-zone recycling excludes outside positions, big garbage and falling", CheckInstantRecycleExclusions);
            Check("Medium edge grips dock after reachable adjustment without changing outer grip", CheckDockGripAdjustment);
            Check("Empty and heavy arm control remain outside automatic docking", CheckDockExclusions);
            Check("Garbage docking creates no runtime indicator", CheckNoDockIndicator);
            Check("Recycle hand tracking, inward limit, medium pauses, shrinking and completion", CheckRecycleAnimation);
            Check("Small and medium recycling survives L3 release and ignores arm/grab input", CheckRecycleInputLatch);
            Check("Chest-zone A and L3 release starts recycling without a drop", CheckSimultaneousRecycleRelease);
            Check("Recycle cancels cleanly on falling, photo mode, external control or disable", CheckRecycleCancellation);
            Check("Recycle completion may disable the controller without restoring its input capture", CheckRecycleCompletionDisable);
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
                // Keep checking that designers can still configure narrower entry/exit bands.
                Set(f.Arms, "dockEnterMagnitude", .18f);
                Set(f.Arms, "dockExitMagnitude", .26f);
                f.Tick(90, Vector2.up, true);
                WorldInteraction item = f.Item((f.Arms.LeftHandWorld + f.Arms.RightHandWorld) * .5f, new Vector2(.5f, .1f), 2);
                f.Tick(1, Vector2.up, true);
                Call(f.Arms, "Step", 0f, Vector2.up * .17f, true, true);
                Require(f.Arms.State == RobotArmState.Docking,
                    "Raw .17 failed to enter docking immediately with zero elapsed time");
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
        private static void CheckControllerDocking()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Resources/Robot/RobotMarker.prefab");
            RobotArmController settings = prefab != null ? prefab.GetComponent<RobotArmController>() : null;
            Require(settings != null,
                "Production robot prefab is missing its arm settings");
            Require(typeof(RobotArmController).GetField("dockEnterDelay", BindingFlags.NonPublic | BindingFlags.Instance) == null
                && typeof(RobotArmController).GetField("dockTimer", BindingFlags.NonPublic | BindingFlags.Instance) == null,
                "Docking still exposes or accumulates a delay before chest-zone recycling");
            Require(Mathf.Approximately((float)Get(settings, "leftStickDeadZone"), .08f)
                && Mathf.Approximately((float)Get(settings, "dockEnterMagnitude"), .4f)
                && Mathf.Approximately((float)Get(settings, "dockExitMagnitude"), .5f)
                && Mathf.Approximately((float)Get(settings, "aimSmoothingTime"), .37f)
                && Mathf.Approximately((float)Get(settings, "recycleZoneHalfWidthOfBodyDiameter"), .35f)
                && Mathf.Approximately((float)Get(settings, "recycleZoneHalfDepthOfBodyDiameter"), .3f),
                "Production prefab did not retain smoothing and apply .08/.4/.5 docking settings");

            foreach (bool medium in new[] { false, true })
            foreach (float drift in new[] { .01f, .39f })
            using (var f = new Fixture(useProductionSettings: true))
            {
                f.Root.transform.rotation = Quaternion.Euler(0f, 0f, 37f);
                f.Tick(90, Vector2.up, true);
                string garbagePath = medium ? "Assets/Prefabs/Environment/Garbage/Medium_Garbage.prefab"
                    : "Assets/Prefabs/Environment/Garbage/Small_Garbage.prefab";
                WorldInteraction garbageSettings = AssetDatabase.LoadAssetAtPath<GameObject>(garbagePath)
                    ?.GetComponent<WorldInteraction>();
                Require(garbageSettings != null, "Production garbage prefab is missing its interaction");
                Vector2 anchor = medium ? (f.Arms.LeftHandWorld + f.Arms.RightHandWorld) * .5f
                    : f.Arms.LeftHandWorld;
                // A slightly off-center grip must place the object, rather than only the hands, at the inlet.
                WorldInteraction item = f.Item(anchor + new Vector2(.012f, -.006f),
                    medium ? new Vector2(.7f, .15f) : Vector2.one * .05f, medium ? 2 : 1);
                // The medium fixture spans the hands along robot-local X, including the rotated heading.
                item.WorldRotation = f.Root.transform.rotation;
                Set(item, "size", garbageSettings.Size);
                Set(item, "grabResistance", garbageSettings.GrabResistance);
                int recycleCount = 0;
                Set(item, "onRecycled", (Action)(() => recycleCount++));
                f.Tick(1, Vector2.up, true);
                Require(f.Arms.HeldObject == item,
                    "Production-settings fixture failed to grab garbage: medium=" + medium + ", drift=" + drift);
                Vector2 stick = Vector2.right * drift;
                Call(f.Arms, "Step", 0f, Vector2.right * .4f, true, true);
                Require(f.Arms.State == RobotArmState.Docking,
                    "Production .4 input did not enter docking on the same zero-time frame");
                Call(f.Arms, "Step", 0f, stick, true, true);
                Require(f.Arms.State == RobotArmState.Docking && !f.Arms.IsRecycleReady,
                    "Inner-circle input did not enter docking immediately or reported ready outside the chest zone");
                Quaternion heading = f.Root.transform.rotation;
                f.Tick(240, stick, true);
                Vector2 inlet = (float)Get(f.Arms, "armLength")
                    * (Vector2)Get(f.Arms, medium ? "mediumDockPosition" : "smallDockPosition");
                Vector2 actual = f.Root.GetComponent<RobotMarkerView>().MarkerVisualRoot
                    .InverseTransformPoint(item.WorldPosition);
                Require(f.Arms.State == RobotArmState.Docking && f.Arms.IsRecycleReady
                    && Vector2.Distance(actual, inlet) < .003f,
                    "Raw side drift prevented garbage from automatically reaching the fixed inlet");
                Require(Mathf.Approximately(f.Arms.CurrentInputMagnitude, drift)
                    && f.Arms.CurrentTargetLocal == stick,
                    "Automatic docking rescaled or erased raw input used by other control states");
                Require(Quaternion.Angle(heading, f.Root.transform.rotation) < .001f,
                    "Inner-circle side input kept rotating the body while docking");

                foreach (Vector2 bandInput in new[] { Vector2.left * .45f, Vector2.down * .49f,
                    Vector2.right * .5f })
                {
                    f.Tick(30, bandInput, true);
                    Require(f.Arms.State == RobotArmState.Docking && f.Arms.IsRecycleReady
                        && Quaternion.Angle(heading, f.Root.transform.rotation) < .001f,
                        "Hysteresis band changed the inlet target, rotated the body or exited docking");
                }
                f.Tick(1, Vector2.right * .501f, true);
                Require(f.Arms.State == RobotArmState.OuterOperating && f.Arms.IsRecycleReady
                    && Quaternion.Angle(heading, f.Root.transform.rotation) > .001f,
                    "Input above .5 did not restore outer control while preserving spatial chest-zone readiness");
                f.Tick(120, Vector2.right * .501f, true);
                Require(!f.Arms.IsRecycleReady,
                    "Garbage remained ready after outer arm control moved it outside the chest zone");
                f.Tick(240, stick, true);
                Require(f.Arms.IsRecycleReady, "Returning to the inner circle failed to dock again");
                f.Tick(1, stick, false);
                Require(f.Arms.State == RobotArmState.Recycling && !f.Arms.IsRecycleReady
                    && item.gameObject.activeSelf && item.Owner == f.Arms && recycleCount == 0,
                    "Ready A release skipped recycling or cleared ownership before the animation");
                f.Tick(90, Vector2.right, false);
                Require(f.Arms.HeldObject == null && item.Owner == null && !item.gameObject.activeSelf
                    && recycleCount == 1, "Controller docking failed to finish one complete recycling operation");
            }
        }
        private static void CheckInstantRecycle()
        {
            Vector2[] positions = {
                Vector2.zero, Vector2.left * .9f, Vector2.right * .9f,
                Vector2.up * .9f, Vector2.down * .9f,
                new Vector2(.6f, .6f), new Vector2(-.6f, -.6f)
            };
            foreach (bool medium in new[] { false, true })
            foreach (Vector2 normalized in positions)
            using (var f = new Fixture(useProductionSettings: true))
            {
                f.Root.transform.SetPositionAndRotation(new Vector3(3f, -2f), Quaternion.Euler(0f, 0f, 37f));
                WorldInteraction item = GrabProductionGarbage(f, medium);
                int recycled = 0;
                Set(item, "onRecycled", (Action)(() => recycled++));
                Vector2 inlet = (float)Get(f.Arms, "armLength")
                    * (Vector2)Get(f.Arms, medium ? "mediumDockPosition" : "smallDockPosition");
                Vector2 zone = (float)Get(f.Arms, "diameter") * new Vector2(
                    (float)Get(f.Arms, "recycleZoneHalfWidthOfBodyDiameter"),
                    (float)Get(f.Arms, "recycleZoneHalfDepthOfBodyDiameter"));
                Require(zone.x > .2f && zone.y > .2f,
                    "Production chest zone is still the old precise inlet tolerance");
                Vector2 localPosition = inlet + Vector2.Scale(normalized, zone);
                Require(f.Arms.State == RobotArmState.OuterOperating && !f.Arms.IsRecycleReady,
                    "Immediate recycle fixture was already docked or ready at full arm extension");
                PlaceHeldAtLocal(f, item, localPosition);
                if (normalized == Vector2.zero)
                {
                    // Readiness describes position, even with the stick still outside.
                    Call(f.Arms, "UpdateReady");
                    Require(f.Arms.IsRecycleReady && f.Arms.State == RobotArmState.OuterOperating,
                        "Chest-zone readiness still required the automatic Docking state");
                }
                // A large nonzero frame and sideways input would pull an edge position out of the
                // zone if aim/body movement ran before sampling the player's release intention.
                Call(f.Arms, "Step", .25f, Vector2.right, true, false);
                Require(f.Arms.State == RobotArmState.Recycling && f.Arms.HeldObject == item
                    && item.Owner == f.Arms && item.gameObject.activeSelf && recycled == 0,
                    "Chest-zone A release did not start its animation immediately: medium=" + medium
                    + ", normalized=" + normalized.ToString("F3"));
                Require(Vector2.Distance((Vector2)(Vector3)Get(f.Arms, "recycleStart"), localPosition) < .0001f,
                    "Release-frame arm motion moved garbage away before the recycling animation began");
                f.Tick(90, Vector2.right, false);
                Require(f.Arms.HeldObject == null && item.Owner == null && !item.gameObject.activeSelf
                    && recycled == 1, "Immediate chest-zone recycling failed to finish exactly once");
            }
        }

        private static void CheckInstantRecycleExclusions()
        {
            foreach (bool medium in new[] { false, true })
            foreach (string exclusion in new[] { "side", "forward", "diagonal", "behind", "extended",
                "big", "heavy", "not recyclable", "fallen" })
            using (var f = new Fixture(useProductionSettings: true))
            {
                WorldInteraction item = GrabProductionGarbage(f, medium);
                Vector2 inlet = (float)Get(f.Arms, "armLength")
                    * (Vector2)Get(f.Arms, medium ? "mediumDockPosition" : "smallDockPosition");
                Vector2 zone = (float)Get(f.Arms, "diameter") * new Vector2(
                    (float)Get(f.Arms, "recycleZoneHalfWidthOfBodyDiameter"),
                    (float)Get(f.Arms, "recycleZoneHalfDepthOfBodyDiameter"));
                Vector2 position = inlet;
                if (exclusion == "side") position += Vector2.right * zone.x * 1.02f;
                else if (exclusion == "forward") position += Vector2.up * zone.y * 1.02f;
                else if (exclusion == "diagonal") position += zone * .75f;
                else if (exclusion == "behind") position = Vector2.down * .1f;
                if (exclusion != "extended") PlaceHeldAtLocal(f, item, position);
                if (exclusion == "big") Set(item, "size", RecyclableSize.Big);
                else if (exclusion == "heavy") item.gameObject.AddComponent<HeavyGarbagePull>();
                else if (exclusion == "not recyclable") Set(item, "recyclable", false);
                else if (exclusion == "fallen")
                {
                    var tumble = f.Root.AddComponent<RobotTumbleController>();
                    typeof(RobotTumbleController).GetProperty("State").SetValue(tumble, RobotTumbleState.Fallen);
                    Set(f.Arms, "tumble", tumble);
                }
                int recycled = 0;
                Set(item, "onRecycled", (Action)(() => recycled++));
                Vector3 start = item.WorldPosition;
                // Positional tests have no elapsed time, so moving out of the zone cannot hide a
                // false positive. Falling still prevents recycling at the exact inlet.
                Call(f.Arms, "Step", 0f, Vector2.up, true, false);
                Require(f.Arms.State != RobotArmState.Recycling && f.Arms.HeldObject == null
                    && item.Owner == null && item.gameObject.activeSelf && recycled == 0,
                    "Chest-zone recycle incorrectly accepted " + exclusion + ": medium=" + medium);
                Require(Vector3.Distance(item.WorldPosition, start) < .0001f,
                    "Excluded zero-time release moved garbage before dropping it");
            }
        }

        private static WorldInteraction GrabProductionGarbage(Fixture f, bool medium)
        {
            f.Tick(240, Vector2.up, true);
            string path = medium ? "Assets/Prefabs/Environment/Garbage/Medium_Garbage.prefab"
                : "Assets/Prefabs/Environment/Garbage/Small_Garbage.prefab";
            WorldInteraction settings = AssetDatabase.LoadAssetAtPath<GameObject>(path)?.GetComponent<WorldInteraction>();
            Require(settings != null, "Production garbage settings unavailable for immediate recycle check");
            Vector2 anchor = medium ? (f.Arms.LeftHandWorld + f.Arms.RightHandWorld) * .5f : f.Arms.LeftHandWorld;
            WorldInteraction item = f.Item(anchor, medium ? new Vector2(.7f, .15f) : Vector2.one * .05f,
                medium ? 2 : 1);
            item.WorldRotation = f.Root.transform.rotation;
            Set(item, "size", settings.Size);
            Set(item, "grabResistance", settings.GrabResistance);
            f.Tick(1, Vector2.up, true);
            Require(f.Arms.HeldObject == item && item.Owner == f.Arms,
                "Immediate recycle fixture failed to grab production-sized garbage");
            return item;
        }

        private static void PlaceHeldAtLocal(Fixture f, WorldInteraction item, Vector2 localPosition)
        {
            Transform frame = f.Root.GetComponent<RobotMarkerView>().MarkerVisualRoot;
            int hands = (int)Get(f.Arms, "heldHands");
            Vector2 anchor = hands == 3 ? (f.Arms.LeftHandWorld + f.Arms.RightHandWorld) * .5f
                : hands == 1 ? f.Arms.LeftHandWorld : f.Arms.RightHandWorld;
            item.WorldPosition = frame.TransformPoint(localPosition);
            // Keep a real, owned grip consistent with the requested garbage position.
            Set(f.Arms, "heldOffset", (Vector2)frame.InverseTransformVector(item.WorldPosition - (Vector3)anchor));
        }

        private static void CheckDockExclusions()
        {
            using (var f = new Fixture(useProductionSettings: true))
            {
                f.Tick(90, Vector2.up, false);
                Quaternion heading = f.Root.transform.rotation;
                f.Tick(60, Vector2.right * .01f, false);
                Require(f.Arms.State == RobotArmState.OuterOperating && !f.Arms.IsRecycleReady
                    && Quaternion.Angle(heading, f.Root.transform.rotation) < .001f,
                    "Empty-arm drift entered docking or bypassed the stick dead zone");
                f.Tick(30, Vector2.right * .3f, false);
                Require(f.Arms.State == RobotArmState.OuterOperating && f.Arms.HeldObject == null
                    && Quaternion.Angle(heading, f.Root.transform.rotation) > 1f,
                    "Inner-circle input incorrectly captured empty-arm aiming and body turning");
            }
            using (var f = new Fixture(useProductionSettings: true))
            {
                f.Tick(90, Vector2.up, true);
                WorldInteraction heavy = f.Item((f.Arms.LeftHandWorld + f.Arms.RightHandWorld) * .5f,
                    new Vector2(.7f, .15f), 2);
                Set(heavy, "size", RecyclableSize.Big);
                Set(heavy, "grabResistance", 1f);
                heavy.gameObject.AddComponent<HeavyGarbagePull>();
                f.Tick(1, Vector2.up, true);
                Require(f.Arms.HeldObject == heavy, "Heavy docking-exclusion fixture failed to grab");
                f.Tick(90, Vector2.right * .3f, true);
                Require(f.Arms.State == RobotArmState.OuterOperating && !f.Arms.IsRecycleReady
                    && f.Arms.CurrentTargetLocal == Vector2.right * .3f,
                    "Automatic docking captured heavy-garbage control");
                f.Tick(1, Vector2.zero, false);
                Require(f.Arms.HeldObject == null && heavy.Owner == null && heavy.gameObject.activeSelf,
                    "Heavy A release entered recycling instead of releasing the grip");
            }
        }
        private static void CheckNoDockIndicator()
        {
            Require(typeof(RobotArmController).GetField("showDockIndicator", BindingFlags.NonPublic | BindingFlags.Instance) == null
                && typeof(RobotArmController).GetField("dockWaitingColor", BindingFlags.NonPublic | BindingFlags.Instance) == null
                && typeof(RobotArmController).GetField("dockReadyColor", BindingFlags.NonPublic | BindingFlags.Instance) == null,
                "Removed garbage indicators still expose display/color settings");
            foreach (bool medium in new[] { false, true })
            using (var f = new Fixture(useProductionSettings: true))
            {
                WorldInteraction item = GrabProductionGarbage(f, medium);
                Transform frame = f.Root.GetComponent<RobotMarkerView>().MarkerVisualRoot;
                RequireNoIndicator();
                Call(f.Arms, "Step", 0f, Vector2.right * .39f, true, true);
                Require(f.Arms.State == RobotArmState.Docking && !f.Arms.IsRecycleReady,
                    "Indicator-removal fixture did not enter automatic docking");
                RequireNoIndicator();
                f.Tick(240, Vector2.right * .39f, true);
                Require(f.Arms.IsRecycleReady, "Removing the indicator also removed chest-zone readiness");
                RequireNoIndicator();
                f.Tick(1, Vector2.zero, false);
                Require(f.Arms.State == RobotArmState.Recycling, "Invisible chest zone failed to begin recycling");
                RequireNoIndicator();
                f.Tick(90, Vector2.zero, false, false);
                RequireNoIndicator();
                Require(f.Arms.HeldObject == null && item.Owner == null && !item.gameObject.activeSelf,
                    "Removing the indicator prevented recycling from completing");

                void RequireNoIndicator()
                {
                    Require(frame.Find("Garbage Dock Indicator") == null
                        && frame.GetComponentsInChildren<LineRenderer>(true).Length == 0,
                        "Garbage docking still creates a runtime yellow/green indicator");
                }
            }
        }
        private static void CheckDockGripAdjustment()
        {
            foreach (float side in new[] { -1f, 1f })
            using (var f = new Fixture(useProductionSettings: true))
            {
                f.Root.transform.rotation = Quaternion.Euler(0f, 0f, -52f);
                // Let production aim smoothing settle before measuring the intended edge grip.
                f.Tick(240, Vector2.up, true);
                Transform frame = f.Root.GetComponent<RobotMarkerView>().MarkerVisualRoot;
                Vector2 anchor = (f.Arms.LeftHandWorld + f.Arms.RightHandWorld) * .5f;
                Vector2 gripOffset = new Vector2(side * .12f, -.45f);
                WorldInteraction item = f.Item(anchor + (Vector2)frame.TransformVector(gripOffset),
                    new Vector2(.94f, .95f), 2);
                item.WorldRotation = f.Root.transform.rotation;
                WorldInteraction settings = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/Environment/Garbage/Medium_Garbage.prefab").GetComponent<WorldInteraction>();
                Set(item, "size", settings.Size);
                Set(item, "grabResistance", settings.GrabResistance);
                f.Tick(1, Vector2.up, true);
                Vector2 initialOffset = (Vector2)Get(f.Arms, "heldOffset");
                Require(f.Arms.HeldObject == item && Vector2.Distance(initialOffset, gripOffset) < .0001f,
                    "Medium edge-grip fixture failed: initial=" + initialOffset.ToString("F6")
                    + ", intended=" + gripOffset.ToString("F6"));
                f.Tick(30, Vector2.up, true);
                Require(f.Arms.State == RobotArmState.OuterOperating
                    && Vector2.Distance((Vector2)Get(f.Arms, "heldOffset"), initialOffset) < .0001f,
                    "Reachable docking adjustment changed the object's outer-operation grip");
                f.Tick(300, Vector2.right * .39f, true);
                Vector2 inlet = (float)Get(f.Arms, "armLength") * (Vector2)Get(f.Arms, "mediumDockPosition");
                Require(f.Arms.State == RobotArmState.Docking && f.Arms.IsRecycleReady
                    && Vector2.Distance(frame.InverseTransformPoint(item.WorldPosition), inlet) < .003f,
                    "Medium rear-edge grip remained outside the inlet because the hand target exceeded IK reach");
                Require(Vector2.Distance((Vector2)Get(f.Arms, "heldOffset"), initialOffset) > .1f,
                    "Impossible edge grip was reported ready without a reachable grip adjustment");
                Quaternion heading = f.Root.transform.rotation;
                f.Tick(30, Vector2.down * .49f, true);
                Require(f.Arms.IsRecycleReady && Quaternion.Angle(heading, f.Root.transform.rotation) < .001f,
                    "Lateral/rearward adjusted grip lost ready alignment inside the hysteresis band");
                f.Tick(1, Vector2.down * .49f, false);
                Require(f.Arms.State == RobotArmState.Recycling, "Adjusted medium grip dropped on ready release");
                f.Tick(90, Vector2.zero, false);
                Require(!item.gameObject.activeSelf && item.Owner == null && f.Arms.HeldObject == null,
                    "Adjusted medium grip did not complete recycling");
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
                // A delta crossing the remaining animation phases must still finish once.
                Call(f.Arms, "Step", 2f, Vector2.zero, true, false);
                Require(!item.gameObject.activeSelf && item.Owner == null && f.Arms.HeldObject == null
                    && item.LocalScale == originalScale, "Recycle completion failed to clear ownership or restore reusable scale");
                Require(((Vector2)item.WorldPosition - (Vector2)f.Root.transform.position).magnitude < .0001f,
                    "Recycled garbage did not reach player center");
            }
        }
        private static void CheckRecycleInputLatch()
        {
            foreach (bool medium in new[] { false, true })
            foreach (bool finalArmHeld in new[] { false, true })
            foreach (float releaseFraction in new[] { .1f, .65f })
            using (var f = new Fixture(useProductionSettings: true))
            {
                f.Root.transform.SetPositionAndRotation(new Vector3(3f, -2f), Quaternion.Euler(0f, 0f, 37f));
                WorldInteraction item = GrabProductionGarbage(f, medium);
                Vector3 originalScale = new Vector3(1.2f, .8f, 1f);
                item.LocalScale = originalScale;
                int recycled = 0;
                Set(item, "onRecycled", (Action)(() => recycled++));
                PlaceHeldAtLocal(f, item, ChestInlet(f.Arms, medium));
                Call(f.Arms, "Step", 0f, Vector2.right, true, false);
                Require(f.Arms.State == RobotArmState.Recycling,
                    "Latch fixture failed to start recycling immediately");
                float duration = medium
                    ? 3f * (float)Get(f.Arms, "mediumRecycleMoveDuration")
                        + 2f * (float)Get(f.Arms, "mediumRecyclePauseDuration")
                    : (float)Get(f.Arms, "recycleDuration");
                Call(f.Arms, "Step", duration * releaseFraction, Vector2.left, true, false);
                float beforeTime = (float)Get(f.Arms, "recycleTime");
                Quaternion heading = f.Root.transform.rotation;
                Vector3 beforePosition = item.WorldPosition;

                // Releasing L3 and pressing A again must not drop, re-grab, or steer the animation.
                Call(f.Arms, "Step", .04f, Vector2.right, false, true);
                Require(f.Arms.State == RobotArmState.Recycling && f.Arms.IsArmModeActive
                    && f.Root.GetComponent<RobotMover>().IsArmInputCaptured
                    && f.Arms.HeldObject == item && item.Owner == f.Arms
                    && item.gameObject.activeSelf && recycled == 0,
                    "L3 release interrupted or released an in-progress recycle: medium=" + medium
                    + ", release fraction=" + releaseFraction);
                Require(Mathf.Abs((float)Get(f.Arms, "recycleTime") - beforeTime - .04f) < .0001f
                    && f.Arms.CurrentInputMagnitude == 0f && f.Arms.CurrentTargetLocal == Vector2.zero
                    && Quaternion.Angle(heading, f.Root.transform.rotation) < .001f,
                    "Latched recycling stopped advancing or allowed arm input to turn the body");
                if (!medium)
                    Require(Vector3.Distance(item.WorldPosition, beforePosition) > .00001f,
                        "Small recycle froze when L3 was released");
                else Require(item.LocalScale.x < originalScale.x,
                    "Medium recycle lost its staged crushing while L3 was released");

                // Re-pressing L3 during the operation also keeps control with the animation.
                Call(f.Arms, "Step", .02f, Vector2.left, true, true);
                Require(f.Arms.State == RobotArmState.Recycling && item.Owner == f.Arms
                    && recycled == 0 && Quaternion.Angle(heading, f.Root.transform.rotation) < .001f,
                    "Re-pressing L3/A regained aiming or recycled garbage before completion");
                Call(f.Arms, "Step", 2f, Vector2.right, finalArmHeld, true);
                RobotMover mover = f.Root.GetComponent<RobotMover>();
                Require(f.Arms.HeldObject == null && item.Owner == null && !item.gameObject.activeSelf
                    && item.LocalScale == originalScale && recycled == 1,
                    "Latched recycle did not finish exactly once and restore reusable garbage scale");
                Require(f.Arms.State == (finalArmHeld ? RobotArmState.OuterOperating : RobotArmState.Retracting)
                    && f.Arms.IsArmModeActive == finalArmHeld && mover.IsArmInputCaptured == finalArmHeld,
                    "Recycle completion did not choose the state/capture matching current L3 input");
                Require(Vector2.Distance(item.WorldPosition, f.Root.transform.position) < .0001f,
                    "Latched garbage did not finish at the player inlet");
                f.Tick(90, Vector2.zero, false, finalArmHeld);
                Require(recycled == 1 && f.Arms.HeldObject == null
                    && f.Arms.State == (finalArmHeld ? RobotArmState.OuterOperating : RobotArmState.Retracted),
                    "Post-recycle input repeated completion or failed to finish retraction");
            }
        }

        private static void CheckSimultaneousRecycleRelease()
        {
            foreach (bool medium in new[] { false, true })
            foreach (string release in new[] { "A and L3 inside", "A and L3 outside", "L3 only inside" })
            using (var f = new Fixture(useProductionSettings: true))
            {
                WorldInteraction item = GrabProductionGarbage(f, medium);
                Vector2 position = ChestInlet(f.Arms, medium);
                bool accepts = release == "A and L3 inside";
                if (release == "A and L3 outside")
                    position.x += (float)Get(f.Arms, "diameter")
                        * (float)Get(f.Arms, "recycleZoneHalfWidthOfBodyDiameter") * 1.02f;
                PlaceHeldAtLocal(f, item, position);
                int recycled = 0;
                Set(item, "onRecycled", (Action)(() => recycled++));
                Call(f.Arms, "Step", 0f, Vector2.right, false, release == "L3 only inside");
                if (accepts)
                {
                    Require(f.Arms.State == RobotArmState.Recycling && f.Arms.IsArmModeActive
                        && f.Root.GetComponent<RobotMover>().IsArmInputCaptured
                        && f.Arms.HeldObject == item && item.Owner == f.Arms && recycled == 0,
                        "Simultaneous A/L3 release dropped garbage inside the chest zone");
                    Require(Vector2.Distance((Vector2)(Vector3)Get(f.Arms, "recycleStart"), position) < .0001f,
                        "Simultaneous release moved garbage before starting recycling");
                    f.Tick(90, Vector2.right, false, false);
                    Require(recycled == 1 && !item.gameObject.activeSelf && item.Owner == null
                        && f.Arms.State == RobotArmState.Retracted
                        && !f.Root.GetComponent<RobotMover>().IsArmInputCaptured,
                        "Simultaneous release failed to complete and retract automatically");
                }
                else
                {
                    Require(f.Arms.State == RobotArmState.Retracting && f.Arms.HeldObject == null
                        && item.Owner == null && item.gameObject.activeSelf && recycled == 0,
                        "Release accepted garbage without chest-zone A release: " + release);
                    Require(Vector2.Distance(item.WorldPosition,
                        f.Root.GetComponent<RobotMarkerView>().MarkerVisualRoot.TransformPoint(position)) < .0001f,
                        "Zero-time L3 drop changed garbage position");
                }
            }
        }

        private static void CheckRecycleCancellation()
        {
            foreach (bool medium in new[] { false, true })
            foreach (string cancellation in new[] { "fallen", "photo mode", "external control", "disable",
                "disabled item", "lost owner", "destroyed item" })
            using (var f = new Fixture(useProductionSettings: true))
            {
                WorldInteraction item = GrabProductionGarbage(f, medium);
                Vector3 originalScale = new Vector3(1.2f, .8f, 1f);
                item.LocalScale = originalScale;
                int recycled = 0;
                Set(item, "onRecycled", (Action)(() => recycled++));
                PlaceHeldAtLocal(f, item, ChestInlet(f.Arms, medium));
                Call(f.Arms, "Step", .1f, Vector2.zero, true, false);
                Require(f.Arms.State == RobotArmState.Recycling && item.Owner == f.Arms,
                    "Cancellation fixture failed to start the animation");
                if (medium) Require(item.LocalScale.x < originalScale.x,
                    "Cancellation fixture did not shrink medium garbage before interruption");

                if (cancellation == "fallen")
                {
                    var tumble = f.Root.AddComponent<RobotTumbleController>();
                    typeof(RobotTumbleController).GetProperty("State").SetValue(tumble, RobotTumbleState.Fallen);
                    Set(f.Arms, "tumble", tumble);
                    typeof(RobotMover).GetProperty("MovementMode")
                        .SetValue(f.Root.GetComponent<RobotMover>(), RobotMovementMode.Fallen);
                }
                else if (cancellation == "photo mode")
                {
                    var photo = f.Root.AddComponent<PhotoModeController>();
                    FieldInfo state = typeof(PhotoModeController).GetField("state", BindingFlags.NonPublic | BindingFlags.Instance);
                    state.SetValue(photo, Enum.Parse(state.FieldType, "Active"));
                    Set(f.Arms, "photoMode", photo);
                }
                else if (cancellation == "external control")
                    typeof(RobotMover).GetProperty("MovementMode")
                        .SetValue(f.Root.GetComponent<RobotMover>(), RobotMovementMode.ExternalTumble);
                else if (cancellation == "disabled item") item.enabled = false;
                else if (cancellation == "lost owner") item.Release(f.Arms);
                else if (cancellation == "destroyed item") Object.DestroyImmediate(item.gameObject);

                if (cancellation == "disable") Call(f.Arms, "OnDisable");
                else Call(f.Arms, "Step", .05f, Vector2.right, false, false);
                bool itemClean = item == null || (item.Owner == null && item.LocalScale == originalScale
                    && item.gameObject.activeSelf);
                Require(f.Arms.State != RobotArmState.Recycling && f.Arms.HeldObject == null
                    && itemClean
                    && recycled == 0 && !f.Root.GetComponent<RobotMover>().IsArmInputCaptured,
                    "Strong recycle cancellation failed to release/restore without completing: " + cancellation
                    + ", medium=" + medium);
                f.Tick(120, Vector2.zero, false, false);
                Require(recycled == 0 && f.Arms.State != RobotArmState.Recycling
                    && !f.Root.GetComponent<RobotMover>().IsArmInputCaptured,
                    "Cancelled operation resumed or left arm input captured: " + cancellation);
            }
        }

        private static void CheckRecycleCompletionDisable()
        {
            foreach (bool medium in new[] { false, true })
            using (var f = new Fixture(useProductionSettings: true))
            {
                WorldInteraction item = GrabProductionGarbage(f, medium);
                int recycled = 0;
                Set(item, "onRecycled", (Action)(() => {
                    recycled++;
                    f.Arms.enabled = false;
                    // Edit-mode fixtures drive runtime lifecycle callbacks explicitly.
                    Call(f.Arms, "OnDisable");
                }));
                PlaceHeldAtLocal(f, item, ChestInlet(f.Arms, medium));
                Call(f.Arms, "Step", 0f, Vector2.zero, true, false);
                Call(f.Arms, "Step", 2f, Vector2.right, true, true);
                Require(recycled == 1 && !item.gameObject.activeSelf && item.Owner == null
                    && f.Arms.HeldObject == null && f.Arms.State == RobotArmState.Retracted
                    && !f.Arms.IsArmModeActive && !f.Root.GetComponent<RobotMover>().IsArmInputCaptured,
                    "Recycle completion restored arm state or input after its callback disabled the controller");
            }
        }

        private static Vector2 ChestInlet(RobotArmController arms, bool medium) =>
            (float)Get(arms, "armLength") * (Vector2)Get(arms, medium ? "mediumDockPosition" : "smallDockPosition");

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
            public Fixture(bool useProductionSettings = false)
            {
                Scene = EditorSceneManager.NewPreviewScene();
                Root = new GameObject("Arm regression robot");
                SceneManager.MoveGameObjectToScene(Root, Scene);
                Arms = Root.AddComponent<RobotArmController>();
                // Edit-mode fixtures only need the existing marker coordinate frame, not UI/art generation.
                RobotMarkerView marker = Root.GetComponent<RobotMarkerView>();
                if (useProductionSettings)
                {
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/Prefabs/Resources/Robot/RobotMarker.prefab");
                    Require(prefab != null, "Production robot prefab is unavailable to regression fixtures");
                    EditorUtility.CopySerialized(prefab.GetComponent<RobotArmController>(), Arms);
                    EditorUtility.CopySerialized(prefab.GetComponent<RobotMarkerView>(), marker);
                    EditorUtility.CopySerialized(prefab.GetComponent<RobotMover>(), Root.GetComponent<RobotMover>());
                }
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
