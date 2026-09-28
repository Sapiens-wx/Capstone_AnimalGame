using System;
using System.Collections.Generic;
using System.Reflection;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using AnimalGame.RobotArm;
using AnimalGame.World;
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
            Check("Held input retries, one/two-hand grabbing and early release", CheckGrabbing);
            Check("Raw thresholds, dock delay, hysteresis and recycle completion", CheckDocking);
            Check("Release L3 drops before retracting", CheckRetracting);
            Check("Deployment obstacle prevents grabbing; deployed obstacle does not", CheckDeploymentCollision);
            Check("Body translation and rotation are constrained by arm boxes", CheckBodyCollision);
            Check("Player-local input, threshold turning and fallen control restrictions", CheckTurning);
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
        private static void CheckGrabbing()
        {
            using (var f = new Fixture())
            {
                f.Tick(90, Vector2.up, true);
                Require(f.Arms.HeldObject == null, "Empty grab fabricated an object");
                WorldInteraction item = f.Item(f.Arms.LeftHandWorld, Vector2.one * .05f, 2);
                f.Tick(2, Vector2.up, true);
                Require(f.Arms.HeldObject == null && item.Owner == null, "One hand moved a two-hand object");
                Set(item, "localSize", new Vector2(.5f, .1f));
                item.transform.position = (f.Arms.LeftHandWorld + f.Arms.RightHandWorld) * .5f;
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
                f.Root.transform.rotation = Quaternion.Euler(0f, 0f, 125f);
                Quaternion heading = f.Root.transform.rotation;
                f.Tick(120, Vector2.up, false);
                Require(Quaternion.Angle(heading, f.Root.transform.rotation) < .001f,
                    "Local forward input rotated an already-rotated player");
                Require(Vector2.Angle((Vector2)Get(f.Arms, "targetLocal"), Vector2.up) < .1f
                    && f.Arms.CurrentTargetLocal == Vector2.up, "Forward input is not body-local");
                f.Tick(60, Vector2.right, false);
                Require(Mathf.Abs(Mathf.DeltaAngle(heading.eulerAngles.z, f.Root.transform.eulerAngles.z) + 100f) < .1f,
                    "Right local input did not rotate clockwise at configured speed");
                heading = f.Root.transform.rotation;
                f.Tick(60, Vector2.right, false);
                Require(Mathf.Abs(Mathf.DeltaAngle(heading.eulerAngles.z, f.Root.transform.eulerAngles.z) + 100f) < .1f,
                    "Sustained local input stopped turning after reaching a world heading");
                heading = f.Root.transform.rotation;
                f.Tick(60, Direction(-69f), false);
                Require(Quaternion.Angle(heading, f.Root.transform.rotation) < .001f,
                    "Body kept turning after local input returned below 70 degrees");
                f.Tick(60, Vector2.left, false);
                Require(Mathf.Abs(Mathf.DeltaAngle(heading.eulerAngles.z, f.Root.transform.eulerAngles.z) - 100f) < .1f,
                    "Left local input did not rotate counterclockwise");
                heading = f.Root.transform.rotation;
                f.Tick(1, Vector2.zero, false);
                Require(Quaternion.Angle(heading, f.Root.transform.rotation) < .001f, "Centred stick kept turning");
                float upper = (float)Get(f.Arms, "upperLength");
                Set(f.Arms, "connectorLengthOfBodyDiameter", 8f);
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
                Set(item, "kind", WorldInteractionKind.Grabbable); Set(item, "localSize", size); Set(item, "requiredHands", hands);
                return item;
            }
            public WorldInteraction Solid(Vector2 position, Vector2 size)
            { WorldInteraction item = Item(position, size, 1); Set(item, "kind", WorldInteractionKind.Collision); return item; }
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
