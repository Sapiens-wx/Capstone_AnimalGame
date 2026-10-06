using System;
using System.Reflection;
using AnimalGame.Garbage;
using AnimalGame.MapTest;
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
    public static class HeldCollisionRegressionChecks
    {
        [MenuItem("Animal Game/Validation/Run Held Collision Checks")]
        public static void Run()
        {
            CheckMaskAndTransaction();
            CheckThinWallAndEscape();
            CheckCompoundOwnership();
            CheckBodyCommitOrder();
            CheckArmPushResistance();
            CheckRotation();
            CheckMapScaleAndBoundary();
            CheckGripLifecycle();
            CheckArmBlocksBeforeCommit();
            Debug.Log("Held collision checks PASS: masks, transactions, thin walls, overlap escape, compound ownership, body/arm pushing, resistance and rotation");
        }
        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void CheckMaskAndTransaction()
        {
            foreach (WorldInteractionKind sourceKind in new[] { WorldInteractionKind.Collision, WorldInteractionKind.BodyCollision })
            foreach (WorldInteractionKind targetKind in new[] { WorldInteractionKind.Collision, WorldInteractionKind.BodyCollision })
            foreach (bool pushable in new[] { false, true })
            using (var f = new Fixture(false))
            {
                WorldInteraction held = f.Item(new Vector2(0, 2), new Vector2(.4f, .4f), sourceKind | WorldInteractionKind.Grabbable);
                WorldInteraction target = f.Item(new Vector2(0, 2.5f), new Vector2(.4f, .4f),
                    targetKind | (pushable ? WorldInteractionKind.Pushable : 0));
                GarbageMotion motion = target.gameObject.AddComponent<GarbageMotion>(); Call(motion, "Awake");
                InteractionShape shape = held.GetShape(null);
                var plan = new InteractionPushPlan();
                plan.Begin(null, f.Scene, null, held.transform);
                plan.Add(shape, shape.Translated(Vector2.up * .2f), true);
                plan.Add(shape, shape.Translated(Vector2.up * .2f), true); // Body/held duplicate contact.
                Vector3 start = target.WorldPosition;
                Require(plan.Evaluate() == pushable, "Solid/pushable mask mismatch");
                Require(target.WorldPosition == start, "Planning moved a target");
                Require(plan.Commit() == pushable, "Invalid plan could commit");
                if (!pushable) continue;
                float displacement = target.WorldPosition.y - start.y;
                Require(displacement > .09f && displacement < .11f, "Duplicate sources pushed a target twice");
                Require(!plan.Commit(), "A plan committed twice");
                Vector3 committed = target.WorldPosition;
                Call(motion, "Step", 1f / 60f);
                Require(target.WorldPosition == committed, "GarbageMotion replayed an immediate push");
                Require(WorldInteractionQuery.Query(InteractionShape.Capsule(new Vector2(0, 2.78f), new Vector2(0, 2.78f), 0),
                    targetKind, null, f.Scene), "Same-frame spatial index missed committed push");
            }
        }

        private static void CheckThinWallAndEscape()
        {
            using (var f = new Fixture(false))
            {
                WorldInteraction item = f.Item(new Vector2(-2, 3), Vector2.one * .1f, WorldInteractionKind.BodyCollision);
                WorldInteraction wall = f.Item(new Vector2(0, 3), new Vector2(.001f, 3), WorldInteractionKind.BodyCollision);
                var plan = new InteractionPushPlan();
                plan.Begin(null, f.Scene, null, item.transform);
                InteractionShape shape = item.GetShape(null);
                plan.Add(shape, shape.Translated(Vector2.right * 4), true);
                Require(!plan.Evaluate() && !plan.Commit(), "Endpoint-only sweep crossed a thin wall");
                item.WorldPosition = new Vector2(-.02f, 3);
                shape = item.GetShape(null);
                plan.Begin(null, f.Scene, null, item.transform);
                plan.Add(shape, shape.Translated(Vector2.left * .01f), true);
                Require(plan.Evaluate(), "Initial overlap could not retreat");
                plan.Begin(null, f.Scene, null, item.transform);
                plan.Add(shape, shape.Translated(Vector2.right * .01f), true);
                Require(!plan.Evaluate(), "Initial overlap deepened into a wall");
                wall.SetKind(WorldInteractionKind.Grabbable | WorldInteractionKind.Pushable);
                Require(plan.Evaluate(), "Non-solid flags became a wall");
            }
        }

        private static void CheckCompoundOwnership()
        {
            using (var f = new Fixture(false))
            {
                WorldInteraction root = f.Item(new Vector2(0, 3), Vector2.one * .2f, WorldInteractionKind.Grabbable);
                WorldInteraction child = f.Item(new Vector2(0, 3), Vector2.one * .2f,
                    WorldInteractionKind.BodyCollision | WorldInteractionKind.Pushable);
                child.SetParent(root.transform);
                Require(child.MotionRoot == root.transform, "Child solid did not resolve grabbable ancestor");
                var plan = new InteractionPushPlan();
                InteractionShape before = InteractionShape.WorldBox(new Vector2(-.2f, 3), Vector2.one * .2f, null);
                plan.Begin(null, f.Scene, null, null); plan.Add(before, before.Translated(Vector2.right * .1f), true);
                Require(plan.Evaluate() && plan.Commit(), "Compound root could not be pushed");
                Require(Mathf.Abs(root.WorldPosition.x - .1f) < .001f && Mathf.Abs(child.WorldPosition.x - .1f) < .001f,
                    "Compound hierarchy moved twice");
                Require(root.TryGrab(root), "Could not establish compound ownership");
                plan.Begin(null, f.Scene, null, null);
                plan.Add(before.Translated(Vector2.right * .1f), before.Translated(Vector2.right * .2f), true);
                Require(!plan.Evaluate(), "Solid sibling ignored ownership on grab component");
                root.Release(root);
                WorldInteraction fixedPart = root.gameObject.AddComponent<WorldInteraction>();
                fixedPart.SetKind(WorldInteractionKind.BodyCollision);
                Require(!plan.Evaluate(), "Non-pushable solid on same prop was overridden");
            }
        }

        private static void CheckBodyCommitOrder()
        {
            foreach (bool wallBehind in new[] { false, true })
            using (var f = new Fixture())
            {
                WorldInteraction held = f.Grab();
                Vector2 origin = held.WorldPosition;
                WorldInteraction target = f.Item(origin + Vector2.up * .42f, Vector2.one * .4f,
                    WorldInteractionKind.BodyCollision | WorldInteractionKind.Pushable);
                GarbageMotion motion = target.gameObject.AddComponent<GarbageMotion>(); Call(motion, "Awake");
                if (wallBehind) f.Item(origin + Vector2.up * .9f, new Vector2(2f, .05f), WorldInteractionKind.BodyCollision);
                Vector3 oldTarget = target.WorldPosition, oldRobot = f.Root.transform.position;
                bool moved = (bool)Call(f.Mover, "TryMoveSafely", Vector2.up * .6f, held.transform, false);
                Require(moved != wallBehind, "Body plan did not respect target obstruction");
                Call(motion, "Step", .016f);
                Call(f.Arms, "LateUpdate"); Call(f.Mover, "LateUpdate");
                Call(motion, "Step", .016f);
                if (wallBehind)
                    Require(target.WorldPosition == oldTarget && f.Root.transform.position == oldRobot,
                        "Rejected body motion left a ghost push");
                else
                {
                    Require(target.WorldPosition.y > oldTarget.y + .5f, "Body did not push through held shape");
                    Require(Mathf.Abs(f.Root.transform.position.y - oldRobot.y - .6f) < .001f,
                        "LateUpdate rolled back a committed body move");
                    Require(WorldInteractionQuery.Penetration(held.GetShape(null), target.GetShape(null)) < .0001f,
                        "Held shape penetrated pushed target");
                }
            }
        }

        private static void CheckArmPushResistance()
        {
            float fast = ArmDistance(1f, 60), resisted = ArmDistance(.6f, 60), stopped = ArmDistance(0f, 60);
            Require(fast > .005f && resisted > .001f && resisted < fast * .95f,
                "Arm-only push did not have measurable resistance: " + fast + ", " + resisted);
            Require(stopped < .0001f, "Zero push multiplier allowed arm pushing");
            float slowFrames = ArmDistance(.6f, 30), fastFrames = ArmDistance(.6f, 120);
            Require(Mathf.Abs(slowFrames - fastFrames) < .025f, "Arm pushing depends excessively on frame rate");
        }
        private static float ArmDistance(float multiplier, int fps)
        {
            using (var f = new Fixture())
            {
                WorldInteraction held = f.Grab();
                WorldInteraction target = f.Item((Vector2)held.WorldPosition + Vector2.up * .40001f,
                    Vector2.one * .4f, WorldInteractionKind.BodyCollision | WorldInteractionKind.Pushable);
                Set(target, "pushSpeedMultiplier", multiplier);
                Vector3 initial = target.WorldPosition, body = f.Root.transform.position;
                for (int i = 0; i < fps / 5; i++) Call(f.Arms, "Step", 1f / fps, Vector2.up, true, true);
                Require(f.Root.transform.position == body, "Arm push displaced stationary body");
                Require(WorldInteractionQuery.Penetration(held.GetShape(null), target.GetShape(null)) < .0001f,
                    "Arm-only push penetrated target: depth=" + WorldInteractionQuery.Penetration(held.GetShape(null), target.GetShape(null))
                    + ", multiplier=" + multiplier + ", fps=" + fps);
                return target.WorldPosition.y - initial.y;
            }
        }

        private static void CheckRotation()
        {
            using (var f = new Fixture())
            {
                WorldInteraction held = f.Grab();
                held.LocalSize = new Vector2(2f, .1f);
                Vector2 origin = held.WorldPosition;
                WorldInteraction obstacle = f.Item(origin + new Vector2(.8f, .2f), Vector2.one * .1f,
                    WorldInteractionKind.BodyCollision);
                f.Arms.ConstrainBodyRotation(Quaternion.Euler(0, 0, 35));
                Require(f.Arms.IsBlocked && Quaternion.Angle(f.Root.transform.rotation, Quaternion.Euler(0, 0, 35)) > 1,
                    "Rotating held corner crossed body-only wall");
                Require(WorldInteractionQuery.Penetration(held.GetShape(null), obstacle.GetShape(null)) < .0001f,
                    "Held corner ended inside wall");
            }
        }

        private static void CheckMapScaleAndBoundary()
        {
            using (var f = new Fixture(false))
            {
                var mapRoot = new GameObject("Nonuniform map"); SceneManager.MoveGameObjectToScene(mapRoot, f.Scene);
                var map = mapRoot.AddComponent<MapTestSceneController>(); map.enabled = false;
                var texture = new Texture2D(2, 2);
                texture.SetPixels(new[] { Color.gray, Color.gray, Color.gray, Color.gray }); texture.Apply();
                Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * .5f, .2f);
                try
                {
                    mapRoot.transform.localScale = new Vector3(2, 3, 1);
                    SpriteRenderer renderer = mapRoot.AddComponent<SpriteRenderer>(); renderer.sprite = sprite;
                    Set(map, "heightField", BakedHeightField.Bake(texture, 64, Vector2.one * 10, 0, 1, false, 0));
                    Set(map, "mapRenderer", renderer); Set(map, "mapWidthMeters", 10f); Set(map, "mapHeightMeters", 10f);
                    Require(map.HasGeneratedMap, "Map fixture unavailable");
                    foreach (Vector2 direction in new[] { Vector2.up, Vector2.right })
                    {
                        WorldInteraction held = f.Item(Vector2.zero, Vector2.one, WorldInteractionKind.BodyCollision);
                        WorldInteraction target = f.Item(direction, Vector2.one,
                            WorldInteractionKind.BodyCollision | WorldInteractionKind.Pushable);
                        var plan = new InteractionPushPlan(); plan.Begin(map, f.Scene, null, held.transform);
                        InteractionShape before = held.GetShape(map);
                        Vector2 delta = InteractionShape.ToQuery(direction * .2f, map) - InteractionShape.ToQuery(Vector2.zero, map);
                        plan.Add(before, before.Translated(delta), true);
                        Require(plan.Evaluate() && plan.Commit(), "Scaled-map push rejected");
                        Require(Vector2.Distance(target.WorldPosition, direction * 1.2f) < .001f,
                            "Push used map metres as world displacement");
                        Object.DestroyImmediate(held.gameObject); Object.DestroyImmediate(target.gameObject);
                    }
                    WorldInteraction edgeHeld = f.Item(new Vector2(8.6f, 0), Vector2.one, WorldInteractionKind.BodyCollision);
                    WorldInteraction edgeTarget = f.Item(new Vector2(9.6f, 0), Vector2.one,
                        WorldInteractionKind.BodyCollision | WorldInteractionKind.Pushable);
                    var edgePlan = new InteractionPushPlan(); edgePlan.Begin(map, f.Scene, null, edgeHeld.transform);
                    InteractionShape shape = edgeHeld.GetShape(map);
                    edgePlan.Add(shape, shape.Translated(Vector2.right * .2f), true);
                    Require(!edgePlan.Evaluate() && !edgePlan.Commit() && edgeTarget.WorldPosition.x == 9.6f,
                        "Target escaped map boundary or failed plan moved it");
                }
                finally { Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture); }
            }
        }

        private static void CheckGripLifecycle()
        {
            using (var f = new Fixture())
            {
                WorldInteraction held = f.Grab();
                WorldInteraction child = f.Item(held.WorldPosition, Vector2.one * .3f, WorldInteractionKind.BodyCollision);
                child.SetParent(held.transform);
                GarbageMotion childMotion = child.gameObject.AddComponent<GarbageMotion>(); Call(childMotion, "Awake");
                childMotion.Launch(Vector2.right, 0f);
                Vector3 childPosition = child.WorldPosition;
                Call(childMotion, "Step", .05f);
                Require(child.WorldPosition == childPosition, "Owned compound's child moved autonomously");
                WorldInteraction siblingGrab = held.gameObject.AddComponent<WorldInteraction>();
                siblingGrab.SetKind(WorldInteractionKind.Grabbable);
                Require(!siblingGrab.TryGrab(siblingGrab), "Two owners grabbed the same motion root");
                Set(held, "grabResistance", 1f);
                Vector3 initial = held.WorldPosition;
                Call(f.Arms, "Step", .1f, Vector2.up, true, true);
                Require(held.WorldPosition == initial, "Full grab resistance failed to freeze held shape");
                Require(f.Arms.ReleaseHeldObject(held) && held.Owner == null && f.Mover.GrabMovementMultiplier == 1f,
                    "Release retained ownership/resistance");
                Call(f.Arms, "Step", .1f, Vector2.up, true, false);
                Require(held.WorldPosition == initial, "Released object followed arm");
            }
        }

        private static void CheckArmBlocksBeforeCommit()
        {
            using (var f = new Fixture())
            {
                WorldInteraction held = f.Grab();
                WorldInteraction target = f.Item((Vector2)held.WorldPosition + Vector2.up * .41f,
                    Vector2.one * .4f, WorldInteractionKind.BodyCollision | WorldInteractionKind.Pushable);
                // A separate arm collision must reject the entire body/push transaction.
                f.Item((Vector2)f.Arms.RightHandWorld + Vector2.up * .1f, Vector2.one * .04f,
                    WorldInteractionKind.Collision);
                Vector3 targetStart = target.WorldPosition;
                Require(!(bool)Call(f.Mover, "TryMoveSafely", Vector2.up * .3f, held.transform, false),
                    "Arm wall did not reject body transaction");
                Call(f.Arms, "LateUpdate");
                Require(target.WorldPosition == targetStart && f.Root.transform.position == Vector3.zero,
                    "Arm rejection left ghost target motion");
            }
        }

        private static object Call(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
        private static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

        private sealed class Fixture : IDisposable
        {
            public Scene Scene { get; }
            public GameObject Root { get; }
            public RobotArmController Arms { get; }
            public RobotMover Mover { get; }
            private Texture2D texture;
            private Sprite sprite;
            public Fixture(bool robot = true)
            {
                Scene = EditorSceneManager.NewPreviewScene();
                if (!robot) return;
                Root = new GameObject("Held collision robot"); SceneManager.MoveGameObjectToScene(Root, Scene);
                Arms = Root.AddComponent<RobotArmController>(); Mover = Root.GetComponent<RobotMover>();
                var frame = new GameObject("Marker Visual Root"); frame.transform.SetParent(Root.transform, false);
                texture = new Texture2D(8, 8);
                sprite = Sprite.Create(texture, new Rect(0, 0, 8, 8), new Vector2(.5f, 0f), 8f);
                foreach (string side in new[] { "Left Mechanical Arm", "Right Mechanical Arm" })
                {
                    var arm = new GameObject(side); arm.transform.SetParent(frame.transform, false);
                    foreach (string name in new[] { "Upper Arm", "Lower Arm", "Mechanical Hand" })
                    {
                        var part = new GameObject(name); part.transform.SetParent(arm.transform, false);
                        part.AddComponent<SpriteRenderer>().sprite = sprite;
                        part.transform.localScale = new Vector3(.1f, .6f, 1f);
                        if (name != "Lower Arm") continue;
                        var loop = new GameObject("Loopable"); loop.transform.SetParent(part.transform, false);
                        loop.AddComponent<SpriteRenderer>().sprite = sprite;
                    }
                }
                Set(Root.GetComponent<RobotMarkerView>(), "markerVisualRoot", frame.transform);
                Call(Mover, "Awake"); Call(Arms, "Awake"); Call(Arms, "EnsureVisuals");
                for (int i = 0; i < 90; i++) Call(Arms, "Step", 1f / 60f, Vector2.up * .6f, true, false);
            }
            public WorldInteraction Grab()
            {
                WorldInteraction item = Item(Arms.LeftHandWorld, Vector2.one * .4f,
                    WorldInteractionKind.Grabbable | WorldInteractionKind.BodyCollision);
                Call(Arms, "Step", 1f / 60f, Vector2.up * .6f, true, true);
                Require(Arms.HeldObject == item, "Fixture failed to grab");
                return item;
            }
            public WorldInteraction Item(Vector2 position, Vector2 size, WorldInteractionKind kind)
            {
                var go = new GameObject("Held collision prop"); SceneManager.MoveGameObjectToScene(go, Scene);
                go.transform.position = position;
                var item = go.AddComponent<WorldInteraction>(); item.LocalSize = size; item.SetKind(kind); return item;
            }
            public void Dispose()
            {
                if (Arms != null)
                {
                    Arms.enabled = false;
                    Transform frame = Root.GetComponent<RobotMarkerView>().MarkerVisualRoot;
                    for (int i = frame.childCount - 1; i >= 0; i--) Object.DestroyImmediate(frame.GetChild(i).gameObject);
                }
                EditorSceneManager.ClosePreviewScene(Scene);
                if (sprite != null) Object.DestroyImmediate(sprite);
                if (texture != null) Object.DestroyImmediate(texture);
            }
        }
    }
}
