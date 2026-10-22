using System;
using System.Reflection;
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
    public static class TumbleArmRegressionChecks
    {
        [MenuItem("Animal Game/Validation/Run Tumble Arm Checks")]
        public static void Run()
        {
            CheckNormalLayout();
            CheckQuadrantsAndLocalDirection();
            CheckCardinalBoundaries();
            CheckVisibleMigration();
            CheckChassisAndArmClearance();
            CheckDirectionReachability();
            CheckPartialDeploymentAndRetraction();
            CheckMigrationCollision();
            CheckSupportDirection();
            CheckUprightReturnAndGrab();
            CheckDisableAndReenable();
            Debug.Log("Tumble arm checks PASS: normal isolation, four exits, local direction, cardinal boundaries, "
                + "circular migration, chassis/arm clearance, deployment/retraction, collision retry, support, upright return and disable cleanup.");
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        public static void RunReachabilityBatch()
        {
            try { CheckDirectionReachability(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void CheckNormalLayout()
        {
            using (var f = new Fixture())
            {
                Vector2 left = f.Socket("left"), right = f.Socket("right");
                float duration = (float)Get(f.Arms, "connectorExtendDuration")
                    + (float)Get(f.Arms, "extendDuration") + (float)Get(f.Arms, "handExtendDuration");
                f.Tick(1, Vector2.up, true, false, duration * .5f);
                Require(Mathf.Abs(f.Arms.VisibleDeployment01 - .5f) < .0001f,
                    "Upright deployment no longer uses the original deployment durations");
                f.Tick(120, Vector2.up);
                Require(!f.Arms.IsTumbleArmLayoutActive && !f.Arms.IsTumbleArmSupportReady,
                    "The tumble layout or support leaked into upright operation");
                Require(f.Socket("left") == left && f.Socket("right") == right
                    && !f.PoseBool("left", "HasSocket") && !f.PoseBool("right", "HasSocket"),
                    "Upright arms no longer use their original sockets");
                f.Tick(1, Vector2.zero, false);
                Require(f.Arms.VisibleDeployment01 < 1f && f.Arms.State == RobotArmState.Retracting,
                    "Upright release no longer starts normal retraction");
            }
        }

        private static void CheckQuadrantsAndLocalDirection()
        {
            Vector2[] directions = { new Vector2(1f, 1f), new Vector2(1f, -1f),
                new Vector2(-1f, -1f), new Vector2(-1f, 1f) };
            Vector2[] left = { Vector2.up, Vector2.right, Vector2.down, Vector2.left };
            Vector2[] right = { Vector2.right, Vector2.down, Vector2.left, Vector2.up };
            foreach (float heading in new[] { 0f, 73f, -147f })
            for (int quadrant = 0; quadrant < directions.Length; quadrant++)
            using (var f = new Fixture())
            {
                f.Root.transform.rotation = Quaternion.Euler(0f, 0f, heading);
                Vector2 direction = directions[quadrant].normalized;
                f.Fall(direction);
                Quaternion rotation = f.Root.transform.rotation;
                f.Tick(1, direction);
                f.RequireSockets(left[quadrant], right[quadrant]);
                Require(f.Arms.IsTumbleArmLayoutActive && !f.Arms.IsTumbleArmSupportReady,
                    "A hidden tumble arm did not select exits before deployment, or became support prematurely");
                f.Tick(180, direction);
                f.RequireSockets(left[quadrant], right[quadrant]);
                Require(Vector2.Angle(direction, f.Arms.TumbleArmSupportDirectionLocal) < .01f,
                    "World tumble direction was not converted into chassis-local coordinates");
                Require(Vector2.Angle(direction, f.HandMidpointLocal()) < 18.01f
                    && f.Arms.IsTumbleArmSupportReady,
                    "Fully deployed tumble hands do not provide support along the falling direction");
                Require(Quaternion.Angle(rotation, f.Root.transform.rotation) < .001f,
                    "Tumble arm aiming rotated the chassis");

                // Stick aim and later tumble samples do not reselect the exits of this tumble.
                f.SetDirection(-direction);
                f.Tick(120, -direction);
                f.RequireSockets(left[quadrant], right[quadrant]);
                Require(Vector2.Angle(direction, f.Arms.TumbleArmSupportDirectionLocal) < .01f,
                    "An active tumble changed its locked support direction");
            }
        }

        private static void CheckCardinalBoundaries()
        {
            Vector2[] directions = { Vector2.up, Vector2.right, Vector2.down, Vector2.left };
            Vector2[] right = { Vector2.right, Vector2.down, Vector2.left, Vector2.up };
            for (int i = 0; i < directions.Length; i++)
            using (var f = new Fixture())
            {
                f.Fall(directions[i]);
                f.Tick(1, directions[i]);
                f.RequireSockets(directions[i], right[i]);
                f.Tick(120, directions[i]);
                f.RequireSockets(directions[i], right[i]);
            }

            using (var f = new Fixture())
            {
                f.Fall(Vector2.one.normalized);
                f.Tick(120, Vector2.one.normalized);
                f.Stand();
                f.Tick(120, Vector2.up);
                f.Fall(Vector2.right);
                f.Tick(120, Vector2.right);
                f.RequireSockets(Vector2.up, Vector2.right);
            }
        }

        private static void CheckVisibleMigration()
        {
            using (var f = new Fixture())
            {
                f.Tick(120, Vector2.up);
                Vector2 oldLeft = f.Socket("left");
                float radius = oldLeft.magnitude;
                f.Fall(Vector2.one.normalized);
                f.Tick(1, Vector2.one.normalized);
                Require(Vector2.Distance(oldLeft, f.Socket("left")) > .00001f
                    && Vector2.Distance(oldLeft, f.Socket("left")) < radius * .5f,
                    "A visible arm snapped to its tumble exit instead of beginning a smooth migration");
                Require(f.Arms.VisibleDeployment01 == 1f && !f.Arms.IsTumbleArmSupportReady,
                    "Visible exit migration restarted deployment or supplied support before arrival");
                Vector2 previousLeft = f.Socket("left"), previousRight = f.Socket("right");
                for (int frame = 0; frame < 90; frame++)
                {
                    f.Tick(1, Vector2.one.normalized);
                    Vector2 left = f.Socket("left"), right = f.Socket("right");
                    Require(Mathf.Abs(left.magnitude - radius) < .0001f
                        && Mathf.Abs(right.magnitude - radius) < .0001f,
                        "An exposed socket cut through the chassis instead of following its edge");
                    Require(Vector2.Angle(previousLeft, left) < 15f
                        && Vector2.Angle(previousRight, right) < 15f,
                        "A visible socket jumped between migration frames");
                    Require(Vector2.Angle(left, right) >= 89.99f,
                        "The arms crossed or collapsed onto the same exit during migration");
                    Require(f.Arms.VisibleDeployment01 == 1f,
                        "Socket migration changed the already completed deployment");
                    f.RequireVisualAttachment("left");
                    f.RequireVisualAttachment("right");
                    previousLeft = left; previousRight = right;
                }
                f.RequireSockets(Vector2.up, Vector2.right);
                Require(f.Arms.IsTumbleArmSupportReady, "The migrated arms never became usable support");
            }
        }

        private static void CheckPartialDeploymentAndRetraction()
        {
            using (var f = new Fixture())
            {
                f.Tick(10, Vector2.up);
                float before = f.Arms.VisibleDeployment01;
                f.Fall(new Vector2(-1f, -1f).normalized);
                f.Tick(1, new Vector2(-1f, -1f).normalized);
                Require(f.Arms.VisibleDeployment01 >= before && f.Arms.VisibleDeployment01 < before + .1f,
                    "Entering the tumble layout reset or completed a partially exposed arm");
                f.Tick(180, new Vector2(-1f, -1f).normalized);
                Vector2 left = f.Socket("left"), right = f.Socket("right");
                for (int frame = 0; frame < 30; frame++)
                {
                    f.Tick(1, Vector2.zero, false);
                    if (f.Arms.VisibleDeployment01 <= 0f) break;
                    Require(Vector2.Distance(left, f.Socket("left")) < .0001f
                        && Vector2.Distance(right, f.Socket("right")) < .0001f,
                        "Releasing the arm button moved visible tumble sockets during retraction");
                    Require(!f.Arms.IsTumbleArmSupportReady,
                        "Retracting arms continued to count as a support");
                }
                Require(f.Arms.VisibleDeployment01 == 0f && f.Arms.State == RobotArmState.Retracted,
                    "Tumble arms did not retract completely");
            }
        }

        [MenuItem("Animal Game/Validation/Run Tumble Arm Reachability Checks")]
        public static void CheckDirectionReachability()
        {
            for (int angle = 0; angle < 360; angle += 15)
            foreach (bool exposed in new[] { false, true })
                CheckReachableDirection(angle, exposed, 60);

            foreach (int fps in new[] { 30, 120 })
            foreach (int angle in new[] { 15, 225 })
            foreach (bool exposed in new[] { false, true })
                CheckReachableDirection(angle, exposed, fps);

            foreach (bool production in new[] { false, true })
            foreach (float normalAngle in new[] { -70f, 70f })
            foreach (int fallAngle in new[] { 45, 135, 225, 315 })
            using (var f = new Fixture(production))
            {
                Set(f.Root.GetComponent<RobotMover>(), "overallMotionScale", 0f);
                Vector2 normalInput = Quaternion.Euler(0f, 0f, normalAngle) * Vector2.up;
                f.Tick(180, normalInput);
                Vector2 direction = Quaternion.Euler(0f, 0f, fallAngle) * Vector2.right;
                f.Fall(direction);
                f.Tick(300, direction);
                string context = (production ? "Production" : "Synthetic") + " normal aim " + normalAngle
                    + " -> tumble " + fallAngle;
                f.RequireSelectedSockets(direction, context);
                Require(f.Arms.IsTumbleArmSupportReady, context + " could not become usable support: " + f.Describe());
                f.RequireArmClearance(context);
                f.Stand();
                f.Tick(300, Vector2.up);
                Require(!f.Arms.IsTumbleArmLayoutActive,
                    context + " could not return to normal sockets: " + f.Describe());
                f.RequireSockets(Vector2.left, Vector2.right);
            }
            Debug.Log("Tumble arm reachability checks PASS: 15 degree direction sweep, hidden/exposed entries, "
                + "30/60/120 FPS, production/synthetic extreme initial aim, and upright returns.");
        }

        private static void CheckReachableDirection(float angle, bool exposed, int fps)
        {
            using (var f = new Fixture())
            {
                float dt = 1f / fps;
                if (exposed) f.Tick(fps * 2, Vector2.up, true, false, dt);
                Vector2 direction = Quaternion.Euler(0f, 0f, angle) * Vector2.right;
                f.Fall(direction);
                f.Tick(fps * 4, direction, true, false, dt);
                string context = "Direction " + angle + " at " + fps + " FPS, " + (exposed ? "exposed" : "hidden");
                f.RequireSelectedSockets(direction, context);
                Require(f.Arms.IsTumbleArmSupportReady, context + " could not become usable support: " + f.Describe());
                f.RequireArmClearance(context);
                f.Stand();
                f.Tick(fps * 4, Vector2.up, true, false, dt);
                Require(!f.Arms.IsTumbleArmLayoutActive,
                    context + " could not return to normal sockets: " + f.Describe());
                f.RequireSockets(Vector2.left, Vector2.right);
            }
        }

        private static void CheckChassisAndArmClearance()
        {
            foreach (Vector2 direction in new[] { new Vector2(1f, 1f).normalized,
                new Vector2(1f, -1f).normalized, new Vector2(-1f, -1f).normalized,
                new Vector2(-1f, 1f).normalized })
            using (var f = new Fixture())
            {
                f.Tick(120, Vector2.up);
                f.Fall(direction);
                for (int frame = 0; frame < 120; frame++)
                {
                    f.Tick(1, direction);
                    f.RequireArmClearance("Migration " + direction + " frame " + frame);
                }
                foreach (float angle in new[] { -70f, 70f })
                {
                    Vector2 input = Quaternion.Euler(0f, 0f, angle) * direction;
                    for (int frame = 0; frame < 180; frame++)
                    {
                        f.Tick(1, input);
                        f.RequireArmClearance("Extreme aim " + direction + " / " + angle + " frame " + frame);
                        if (f.Arms.IsBlocked || Vector2.Angle(direction, f.HandMidpointLocal()) > 18f)
                            Require(!f.Arms.IsTumbleArmSupportReady,
                                "A blocked or misaligned extreme aim retained tumble support");
                    }
                }
                f.Tick(180, direction);
                Require(f.Arms.IsTumbleArmSupportReady,
                    "Correct aim could not recover after the chassis clearance constrained an extreme aim");
                f.Stand();
                for (int frame = 0; frame < 120; frame++)
                {
                    f.Tick(1, Vector2.up);
                    f.RequireArmClearance("Upright return " + direction + " frame " + frame);
                }
                Require(!f.Arms.IsTumbleArmLayoutActive,
                    "The chassis clearance prevented an unobstructed upright return");
            }
        }

        private static void CheckMigrationCollision()
        {
            using (var f = new Fixture())
            {
                f.Tick(120, Vector2.up);
                float radius = f.Socket("right").magnitude;
                Vector2 point = new Vector2(1f, -1f).normalized * radius;
                WorldInteraction blocker = f.Item(f.Frame.TransformPoint(point), Vector2.one * .025f,
                    WorldInteractionKind.Collision);
                InteractionShape obstacle = blocker.GetShape(null);
                Require(!f.IntersectsArms(obstacle), "Migration blocker starts inside an upright arm");
                f.Fall(new Vector2(1f, -1f).normalized);
                bool blocked = false;
                for (int frame = 0; frame < 120; frame++)
                {
                    f.Tick(1, new Vector2(1f, -1f).normalized);
                    blocked |= f.Arms.IsBlocked;
                    Require(!f.IntersectsArms(obstacle),
                        "A migrating arm or its collision geometry passed through the blocker");
                }
                Require(blocked && !f.Arms.IsTumbleArmSupportReady,
                    "An obstructed socket migration did not block support");
                Vector2 left = f.Socket("left"), right = f.Socket("right");
                f.Tick(30, new Vector2(1f, -1f).normalized);
                Require(Vector2.Distance(left, f.Socket("left")) < .0001f
                    && Vector2.Distance(right, f.Socket("right")) < .0001f,
                    "The paired sockets kept moving after one arm was obstructed");
                Object.DestroyImmediate(blocker.gameObject);
                f.Tick(180, new Vector2(1f, -1f).normalized);
                f.RequireSockets(Vector2.right, Vector2.down);
                Require(f.Arms.IsTumbleArmSupportReady,
                    "Socket migration did not resume after the obstacle was removed");
            }
        }

        private static void CheckSupportDirection()
        {
            using (var f = new Fixture())
            {
                Vector2 direction = new Vector2(-1f, 1f).normalized;
                f.Fall(direction);
                f.Tick(180, direction);
                Require(f.Arms.IsTumbleArmSupportReady, "Correctly aimed deployed arms were not ready");
                Vector2 misalignedInput = Quaternion.Euler(0f, 0f, 35f) * direction;
                f.Tick(180, misalignedInput);
                Require(Vector2.Angle(direction, f.HandMidpointLocal()) > 18f
                    && !f.Arms.IsTumbleArmSupportReady,
                    "Input or socket readiness replaced the actual hand-direction support check");
                f.Tick(180, -direction);
                Vector2 target = (Vector2)Get(f.Arms, "targetLocal");
                Require(Vector2.Angle(direction, target) <= 70.01f,
                    "Tumble aim did not use the original 70 degree limit relative to the falling direction");
                if (f.Arms.IsBlocked || Vector2.Angle(direction, f.HandMidpointLocal()) > 18f)
                    Require(!f.Arms.IsTumbleArmSupportReady,
                        "A blocked or misaligned arm retained support at the aim limit");
                f.Tick(180, direction);
                Require(f.Arms.IsTumbleArmSupportReady, "Correcting arm aim did not restore support");
            }
        }

        private static void CheckUprightReturnAndGrab()
        {
            using (var f = new Fixture())
            {
                f.Fall(Vector2.one.normalized);
                f.Tick(180, Vector2.one.normalized);
                f.Stand();
                WorldInteraction item = f.Item(f.Arms.LeftHandWorld, Vector2.one * 10f,
                    WorldInteractionKind.Grabbable);
                f.Tick(1, Vector2.up, true, true);
                Require(f.Arms.IsTumbleArmLayoutActive && f.Arms.HeldObject == null,
                    "The upright return snapped its sockets or allowed grabbing before arrival");
                f.Tick(180, Vector2.up, true, true);
                Require(!f.Arms.IsTumbleArmLayoutActive && !f.Arms.IsTumbleArmSupportReady,
                    "Upright return left the tumble layout or support active");
                f.RequireSockets(Vector2.left, Vector2.right);
                Require(!f.PoseBool("left", "HasSocket") && !f.PoseBool("right", "HasSocket")
                    && f.Arms.HeldObject == item,
                    "Completing upright return did not restore normal sockets and grabbing");
            }
        }

        private static void CheckDisableAndReenable()
        {
            foreach (bool standBeforeDisable in new[] { false, true })
            using (var f = new Fixture())
            {
                Vector2 direction = new Vector2(-1f, 1f).normalized;
                f.Fall(direction);
                f.Tick(180, direction);
                if (standBeforeDisable) { f.Stand(); f.Tick(1, Vector2.up); }
                f.Arms.enabled = false;
                Call(f.Arms, "OnDisable");
                Require(f.Arms.VisibleDeployment01 == 0f && !f.Arms.IsTumbleArmLayoutActive
                    && !f.Arms.IsTumbleArmSupportReady,
                    "Disabling the arm controller retained a tumble migration or support");
                f.Arms.enabled = true;
                // A disabled deployment switch needs a new release/press sequence.
                f.Tick(1, Vector2.zero, false);
                f.Tick(1, standBeforeDisable ? Vector2.up : direction);
                if (standBeforeDisable)
                {
                    Require(!f.Arms.IsTumbleArmLayoutActive,
                        "Re-enabling an upright robot restored a stale tumble layout");
                    f.RequireSockets(Vector2.left, Vector2.right);
                }
                else
                {
                    f.RequireSockets(Vector2.left, Vector2.up);
                    Require(!f.Arms.IsTumbleArmSupportReady,
                        "Re-enabling hidden arms restored stale support before deployment");
                    f.Tick(180, direction);
                    Require(f.Arms.IsTumbleArmSupportReady,
                        "Re-enabled tumble arms could not redeploy into valid support");
                }
            }
        }

        private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        private static object Get(object target, string name) =>
            target.GetType().GetField(name, InstancePrivate | BindingFlags.Public).GetValue(target);
        private static void Set(object target, string name, object value) =>
            target.GetType().GetField(name, InstancePrivate | BindingFlags.Public).SetValue(target, value);
        private static object Call(object target, string name, params object[] args) =>
            target is RobotArmController arms && name == "Step"
                ? RobotArmRegressionChecks.StepForDesiredDeployment(arms, (float)args[0], (Vector2)args[1], (bool)args[2], (bool)args[3])
                : target.GetType().GetMethod(name, InstancePrivate).Invoke(target, args);
        private static void SetProperty(object target, string name, object value) =>
            target.GetType().GetProperty(name).SetValue(target, value);

        private sealed class Fixture : IDisposable
        {
            public Scene Scene { get; }
            public GameObject Root { get; }
            public RobotArmController Arms { get; }
            public RobotTumbleController Tumble { get; }
            public Transform Frame { get; }

            public Fixture(bool production = true)
            {
                Scene = EditorSceneManager.NewPreviewScene();
                Root = new GameObject("Tumble arm regression robot");
                SceneManager.MoveGameObjectToScene(Root, Scene);
                Arms = Root.AddComponent<RobotArmController>();
                Tumble = Root.AddComponent<RobotTumbleController>();
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/Resources/Robot/RobotMarker.prefab");
                Require(prefab != null, "Production robot prefab is unavailable");
                RobotMarkerView marker = Root.GetComponent<RobotMarkerView>();
                if (production)
                {
                    EditorUtility.CopySerialized(prefab.GetComponent<RobotArmController>(), Arms);
                    EditorUtility.CopySerialized(prefab.GetComponent<RobotMarkerView>(), marker);
                    EditorUtility.CopySerialized(prefab.GetComponent<RobotMover>(), Root.GetComponent<RobotMover>());
                }
                Frame = new GameObject("Marker Visual Root").transform;
                Frame.SetParent(Root.transform, false);
                RobotArmRegressionChecks.CreateFixtureArtwork(Frame, production);
                Set(marker, "markerVisualRoot", Frame);
                Transform body = Frame.Find("Body Visual");
                Set(marker, "bodyVisualRoot", body);
                Set(marker, "mediumRecycleBodyBasePosition", body.localPosition);
                Set(marker, "mediumRecycleBodyBaseRotation", body.localRotation);
                Call(Arms, "Awake");
                Call(Arms, "EnsureVisuals");
            }

            public void Tick(int frames, Vector2 stick, bool deploy = true, bool grab = false, float dt = 1f / 60f)
            { for (int i = 0; i < frames; i++) Call(Arms, "Step", dt, stick, deploy, grab); }

            public void Fall(Vector2 localDirection)
            {
                SetDirection(localDirection);
                SetProperty(Tumble, "State", RobotTumbleState.Fallen);
            }

            public void SetDirection(Vector2 localDirection) =>
                SetProperty(Tumble, "DirectionWorld", (Vector2)Root.transform.TransformDirection(localDirection.normalized));

            public void Stand() => SetProperty(Tumble, "State", RobotTumbleState.Upright);

            public Vector2 Socket(string name)
            {
                object arm = Get(Arms, name);
                return (Vector2)typeof(RobotArmController).GetMethod("SocketAt",
                    BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new[] { arm, Get(arm, "Pose") });
            }

            public bool PoseBool(string name, string field) => (bool)Get(Get(Get(Arms, name), "Pose"), field);

            public Vector2 HandMidpointLocal() => Frame.InverseTransformPoint(
                (Arms.LeftHandWorld + Arms.RightHandWorld) * .5f);

            public void RequireSockets(Vector2 leftDirection, Vector2 rightDirection, string context = "")
            {
                float radius = ((Vector2)Get(Get(Arms, "left"), "Socket")).magnitude;
                Require(Vector2.Distance(Socket("left"), leftDirection * radius) < .0001f
                    && Vector2.Distance(Socket("right"), rightDirection * radius) < .0001f,
                    context + ": unexpected arm exits: " + Socket("left") + " / " + Socket("right")
                    + "; expected " + leftDirection + " / " + rightDirection + "; " + Describe());
            }

            public void RequireSelectedSockets(Vector2 direction, string context)
            {
                if (Mathf.Abs(direction.x) <= .0001f)
                {
                    if (direction.y > 0f) RequireSockets(Vector2.up, Vector2.right, context);
                    else RequireSockets(Vector2.down, Vector2.left, context);
                }
                else if (Mathf.Abs(direction.y) <= .0001f)
                {
                    if (direction.x > 0f) RequireSockets(Vector2.right, Vector2.down, context);
                    else RequireSockets(Vector2.left, Vector2.up, context);
                }
                else if (direction.x > 0f)
                {
                    if (direction.y > 0f) RequireSockets(Vector2.up, Vector2.right, context);
                    else RequireSockets(Vector2.right, Vector2.down, context);
                }
                else
                {
                    if (direction.y > 0f) RequireSockets(Vector2.left, Vector2.up, context);
                    else RequireSockets(Vector2.down, Vector2.left, context);
                }
            }

            public string Describe() => "target=" + Get(Arms, "targetLocal")
                + ", direction=" + Arms.TumbleArmSupportDirectionLocal + ", hands=" + HandMidpointLocal()
                + ", blocked=" + Arms.IsBlocked + ", deployment=" + Arms.VisibleDeployment01
                + ", sockets=" + Socket("left") + " @ " + Get(Get(Get(Arms, "left"), "Pose"), "SocketAngle")
                + " / " + Socket("right") + " @ " + Get(Get(Get(Arms, "right"), "Pose"), "SocketAngle");

            public void RequireVisualAttachment(string name)
            {
                object arm = Get(Arms, name), pose = Get(arm, "Pose");
                var lower = (SpriteRenderer)Get(arm, "LowerSprite");
                float growth = (float)Get(pose, "LowerLength") / (float)Get(arm, "LowerLength");
                float bottom = (float)Get(arm, "LowerBottom");
                Vector3 start = lower.transform.localPosition
                    + lower.transform.localRotation * new Vector3(0f, bottom * growth);
                Require(Vector2.Distance(start, Socket(name)) < .0001f,
                    "The visible body-side arm detached from its moving socket");
                var hand = (Transform)Get(arm, "Hand");
                Vector2 expectedHand = name == "left" ? Arms.LeftHandWorld : Arms.RightHandWorld;
                Require(Vector2.Distance(hand.position, expectedHand) < .0001f,
                    "The reported hand endpoint differs from the visible migrating hand");
            }

            public bool IntersectsArms(InteractionShape obstacle)
            {
                foreach (string name in new[] { "left", "right" })
                {
                    object arm = Get(Arms, name), pose = Get(arm, "Pose");
                    for (int segment = 0; segment < 2; segment++)
                    {
                        InteractionShape shape = (InteractionShape)Call(Arms, "SegmentShape", arm, pose, segment);
                        if (WorldInteractionQuery.Penetration(shape, obstacle) > .0001f) return true;
                    }
                }
                return false;
            }

            public void RequireArmClearance(string context)
            {
                float radius = ((Vector2)Get(Get(Arms, "left"), "Socket")).magnitude * .9f;
                Vector2[][] chains = new Vector2[2][];
                int chain = 0;
                foreach (string name in new[] { "left", "right" })
                {
                    object arm = Get(Arms, name), pose = Get(arm, "Pose");
                    Vector2 socket = Socket(name);
                    Vector2 lower = Quaternion.Euler(0f, 0f, (float)Get(pose, "LowerAngle")) * Vector2.up;
                    Vector2 upper = Quaternion.Euler(0f, 0f, (float)Get(pose, "UpperAngle")) * Vector2.up;
                    Vector2 elbow = socket + lower * (float)Get(pose, "LowerLength");
                    Vector2 hand = elbow + upper * (float)Get(pose, "UpperLength");
                    chains[chain++] = new[] { socket, elbow, hand };
                    Require(PointSegmentDistance(Vector2.zero, socket, elbow) >= radius - .0001f
                        && PointSegmentDistance(Vector2.zero, elbow, hand) >= radius - .0001f,
                        context + ": " + name + " arm cut through the chassis interior");
                }
                for (int leftSegment = 0; leftSegment < 2; leftSegment++)
                for (int rightSegment = 0; rightSegment < 2; rightSegment++)
                    Require(!SegmentsCross(chains[0][leftSegment], chains[0][leftSegment + 1],
                            chains[1][rightSegment], chains[1][rightSegment + 1]),
                        context + ": the two exposed arm chains crossed");
            }

            private static float PointSegmentDistance(Vector2 point, Vector2 a, Vector2 b)
            {
                Vector2 delta = b - a;
                float t = delta.sqrMagnitude > .000000001f
                    ? Mathf.Clamp01(Vector2.Dot(point - a, delta) / delta.sqrMagnitude) : 0f;
                return Vector2.Distance(point, a + delta * t);
            }

            private static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
            {
                Vector2 ab = b - a, cd = d - c;
                float cross = ab.x * cd.y - ab.y * cd.x;
                if (Mathf.Abs(cross) < .00000001f)
                    return PointSegmentDistance(a, c, d) < .00001f
                        || PointSegmentDistance(b, c, d) < .00001f
                        || PointSegmentDistance(c, a, b) < .00001f
                        || PointSegmentDistance(d, a, b) < .00001f;
                Vector2 ac = c - a;
                float t = (ac.x * cd.y - ac.y * cd.x) / cross;
                float u = (ac.x * ab.y - ac.y * ab.x) / cross;
                return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
            }

            public WorldInteraction Item(Vector2 position, Vector2 size, WorldInteractionKind kind)
            {
                var go = new GameObject("Tumble arm regression item");
                SceneManager.MoveGameObjectToScene(go, Scene);
                go.transform.position = position;
                var item = go.AddComponent<WorldInteraction>();
                item.SetKind(kind);
                item.LocalSize = size;
                Set(item, "requiredHands", 1);
                return item;
            }

            public void Dispose()
            {
                Arms.enabled = false;
                Call(Arms, "OnDisable");
                for (int i = Frame.childCount - 1; i >= 0; i--)
                    Object.DestroyImmediate(Frame.GetChild(i).gameObject);
                EditorSceneManager.ClosePreviewScene(Scene);
            }
        }
    }
}
