using System;
using System.Reflection;
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
    // Use actual production arms and sprite bounds: a one-hand pickup naturally sits
    // half a hand-spacing away from the common target controlled by the player.
    public static class SmallRecycleRegressionChecks
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly int[] FrameRates = { 30, 60, 120 };

        [MenuItem("Animal Game/Validation/Run Small Recycle Checks")]
        public static void Run()
        {
            CheckProductionHandLanes();
            CheckStraightInwardDelivery();
            CheckInwardCorridor();
            CheckNeutralHoldsWithoutSnapping();
            CheckSmallToleranceIsIndependent();
            CheckOuterAndBackBoundaries();
            CheckReleasePriorityAndSessionLatch();
            CheckMediumRangeIsUnchanged();
            Debug.Log("Small recycle checks PASS: 8 groups; production one-hand lanes, inward delivery, "
                + "continuous inner corridor, neutral hold, independent side tolerance, outer/back exclusions, "
                + "A-release priority/session latch and unchanged medium range; straight delivery at 30/60/120 FPS.");
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void CheckProductionHandLanes()
        {
            using (var f = new Fixture())
            {
                WorldInteraction item = f.Grab(false, 0f);
                Vector2 zone = f.Zone;
                Require(zone.x > f.Spacing * .5f + f.Diameter * .02f,
                    "Small recycle width still requires a single hand to hit the exact ellipse centreline");
                Require(Mathf.Abs(zone.y - f.Diameter * (float)Get(f.Arms, "recycleZoneHalfDepthOfBodyDiameter")) < .00001f,
                    "Small recycle fix unexpectedly changed the established forward depth");
                Require(item.Size == RecyclableSize.Small && item.RequiredHands == 1,
                    "Production small-garbage fixture is not a single-hand item");
            }
        }

        private static void CheckStraightInwardDelivery()
        {
            foreach (int fps in FrameRates)
            foreach (bool right in new[] { false, true })
            foreach (float heading in new[] { 0f, 37f, 153f })
            foreach (float offset in new[] { -.08f, 0f, .08f })
            using (var f = new Fixture(fps, heading))
            {
                WorldInteraction item = f.Grab(right, offset);
                int recycled = 0;
                Set(item, "onRecycled", (Action)(() => recycled++));
                Quaternion rotation = f.Root.transform.rotation;
                f.TickSeconds(3f, Vector2.down, false, true);
                Require(f.Arms.HeldObject == item && item.Owner == f.Arms && f.Arms.IsArmModeActive,
                    "A-held inward delivery lost its grip after L3 release: " + f.Case(right, offset));
                Require(f.Arms.IsRecycleReady && f.Arms.State == RobotArmState.OuterOperating,
                    "Straight inward input missed small recycling at minimum reach: " + f.Case(right, offset)
                    + "; centre=" + f.ItemLocal(item).ToString("F6"));
                Require(Quaternion.Angle(rotation, f.Root.transform.rotation) < .001f,
                    "Pure inward small-garbage delivery rotated the body");
                Vector3 before = item.WorldPosition;
                f.Step(.25f, Vector2.right, false, false);
                Require(f.Arms.State == RobotArmState.Recycling && item.Owner == f.Arms && recycled == 0,
                    "A release at the natural inward stop dropped small garbage: " + f.Case(right, offset));
                Require(Vector3.Distance(f.Frame.InverseTransformPoint(before),
                    (Vector3)Get(f.Arms, "recycleStart")) < .0001f,
                    "Release-frame stick movement displaced the garbage before recycling latched");
                f.FinishRecycle(item, () => recycled);
            }
        }

        private static void CheckInwardCorridor()
        {
            foreach (bool right in new[] { false, true })
            using (var f = new Fixture(60, 37f))
            {
                WorldInteraction item = f.Grab(right, 0f);
                Vector2 inlet = f.Inlet;
                foreach (float side in new[] { -.98f, 0f, .98f })
                foreach (float fraction in new[] { 1f, .75f, .5f, .25f, .01f })
                {
                    f.PlaceAt(item, new Vector2(inlet.x + side * f.Zone.x, inlet.y * fraction));
                    Call(f.Arms, "UpdateReady");
                    Require(f.Arms.IsRecycleReady && f.Arms.State == RobotArmState.OuterOperating,
                        "Small garbage lost readiness when moved further inward: side=" + side + ", y=" + fraction);
                }
                // Preserve the established curved forward end; only the inward side fills out.
                f.PlaceAt(item, inlet + Vector2.up * f.Zone.y * .99f);
                Call(f.Arms, "UpdateReady");
                Require(f.Arms.IsRecycleReady, "Small garbage was rejected just inside the forward cap");
            }
        }

        private static void CheckNeutralHoldsWithoutSnapping()
        {
            foreach (int fps in FrameRates)
            using (var f = new Fixture(fps, 37f))
            {
                WorldInteraction item = f.Grab(false, 0f);
                f.TickSeconds(3f, Vector2.down, false, true);
                Require(f.Arms.IsRecycleReady, "Neutral-hold fixture never reached the small inlet corridor");
                Vector3 position = item.WorldPosition;
                Vector2 left = f.Arms.LeftHandWorld, right = f.Arms.RightHandWorld;
                f.TickSeconds(1f, Vector2.zero, false, true);
                Require(Vector3.Distance(item.WorldPosition, position) < .0001f
                    && Vector2.Distance(left, f.Arms.LeftHandWorld) < .0001f
                    && Vector2.Distance(right, f.Arms.RightHandWorld) < .0001f
                    && f.Arms.State == RobotArmState.OuterOperating && f.Arms.IsRecycleReady,
                    "Small recycle readiness snapped, auto-docked or drifted a centred stick");
            }
        }

        private static void CheckSmallToleranceIsIndependent()
        {
            using (var f = new Fixture(60, 37f))
            {
                WorldInteraction item = f.Grab(false, 0f);
                float commonWidth = f.Diameter * (float)Get(f.Arms, "recycleZoneHalfWidthOfBodyDiameter");
                Set(f.Arms, "smallRecycleSideToleranceOfBodyDiameter", 0f);
                Require(Mathf.Abs(f.Zone.x - Mathf.Max(commonWidth, f.Spacing * .5f)) < .00001f,
                    "Zero small side tolerance did not retain the authored minimum width");
                Set(f.Arms, "smallRecycleSideToleranceOfBodyDiameter", .2f);
                Require(Mathf.Abs(f.Zone.x - Mathf.Max(commonWidth, f.Spacing * .5f + f.Diameter * .2f)) < .00001f,
                    "Small side tolerance did not follow the natural single-hand lane");
                f.PlaceAt(item, new Vector2(f.Zone.x * .98f, f.Inlet.y * .25f));
                Call(f.Arms, "UpdateReady");
                Require(f.Arms.IsRecycleReady, "The adjusted small side tolerance did not widen the inward lane");
                Set(f.Arms, "smallRecycleSideToleranceOfBodyDiameter", 0f);
                Call(f.Arms, "UpdateReady");
                Require(!f.Arms.IsRecycleReady, "Reducing small side tolerance did not restore its side boundary");
            }
        }

        private static void CheckOuterAndBackBoundaries()
        {
            foreach (string exclusion in new[] { "left", "right", "front", "front corner", "centre", "behind",
                "big", "not recyclable", "fallen" })
            using (var f = new Fixture())
            {
                WorldInteraction item = f.Grab(false, 0f);
                Vector2 position = f.Inlet;
                if (exclusion == "left") position.x -= f.Zone.x * 1.02f;
                else if (exclusion == "right") position.x += f.Zone.x * 1.02f;
                else if (exclusion == "front") position.y += f.Zone.y * 1.02f;
                else if (exclusion == "front corner") position += f.Zone * .75f;
                else if (exclusion == "centre") position = Vector2.zero;
                else if (exclusion == "behind") position = Vector2.down * f.Diameter * .2f;
                f.PlaceAt(item, position);
                if (exclusion == "big") Set(item, "size", RecyclableSize.Big);
                else if (exclusion == "not recyclable") Set(item, "recyclable", false);
                else if (exclusion == "fallen")
                {
                    var tumble = f.Root.AddComponent<RobotTumbleController>();
                    typeof(RobotTumbleController).GetProperty("State").SetValue(tumble, RobotTumbleState.Fallen);
                    Set(f.Arms, "tumble", tumble);
                }
                Call(f.Arms, "UpdateReady");
                Require(!f.Arms.IsRecycleReady, "Small recycling accepted " + exclusion);
                int recycled = 0; Set(item, "onRecycled", (Action)(() => recycled++));
                f.Step(0f, Vector2.zero, false, false);
                Require(f.Arms.State != RobotArmState.Recycling && item.Owner == null
                    && item.gameObject.activeSelf && recycled == 0,
                    "Small A release incorrectly recycled " + exclusion);
            }
        }

        private static void CheckReleasePriorityAndSessionLatch()
        {
            foreach (bool deploy in new[] { false, true })
            using (var f = new Fixture(60, 153f))
            {
                WorldInteraction item = f.Grab(true, .08f);
                f.TickSeconds(3f, Vector2.down, false, true);
                int recycled = 0; Set(item, "onRecycled", (Action)(() => recycled++));
                f.Step(.25f, Vector2.right, deploy, false);
                Require(f.Arms.State == RobotArmState.Recycling && item.Owner == f.Arms,
                    "Small inward corridor release lost priority to a new lateral command");
                f.FinishRecycle(item, () => recycled, deploy);
                f.TickSeconds(.5f, Vector2.up, deploy, true);
                Require(f.Arms.State == RobotArmState.OuterOperating && f.Arms.IsArmModeActive
                    && f.Root.GetComponent<RobotMover>().IsArmInputCaptured && recycled == 1,
                    "Small recycling discarded its deployment switch or repeated completion");
                // This suite expresses desired deployment rather than raw button-held input.
                // Establish the open request, then explicitly request a manual close.
                f.Step(1f / 60f, Vector2.zero, true, false);
                f.Step(1f / 60f, Vector2.zero, false, false);
                f.TickSeconds(2f, Vector2.zero, false, false);
                Require(f.Arms.State == RobotArmState.Retracted && !f.Arms.IsArmModeActive,
                    "An explicit close request failed after small recycling");
            }
        }

        private static void CheckMediumRangeIsUnchanged()
        {
            using (var f = new Fixture())
            {
                WorldInteraction item = f.Grab(false, 0f);
                Set(item, "size", RecyclableSize.Medium);
                float width = f.Diameter * (float)Get(f.Arms, "recycleZoneHalfWidthOfBodyDiameter");
                f.PlaceAt(item, new Vector2(f.Inlet.x + width * .98f, 0f));
                Call(f.Arms, "UpdateReady");
                Require(f.Arms.IsRecycleReady, "Small fix narrowed the existing medium inward corridor");
                f.PlaceAt(item, new Vector2(f.Inlet.x + width * 1.02f, 0f));
                Call(f.Arms, "UpdateReady");
                Require(!f.Arms.IsRecycleReady, "Small-specific hand-lane tolerance widened medium recycling");
            }
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        private static object Get(object target, string name) => target.GetType().GetField(name, Instance).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Instance).SetValue(target, value);
        private static object Call(object target, string name, params object[] args) =>
            target is RobotArmController arms && name == "Step"
                ? RobotArmRegressionChecks.StepForDesiredDeployment(arms, (float)args[0], (Vector2)args[1], (bool)args[2], (bool)args[3])
                : target.GetType().GetMethod(name, Instance).Invoke(target, args);

        private sealed class Fixture : IDisposable
        {
            public Scene Scene { get; }
            public GameObject Root { get; }
            public RobotArmController Arms { get; }
            public Transform Frame { get; }
            private readonly int fps;
            public float Diameter => (float)Get(Arms, "diameter");
            public float Spacing => (float)Get(Arms, "handSpacing");
            public Vector2 Inlet => (Vector2)typeof(RobotArmController).GetProperty("DockLocal", Instance).GetValue(Arms);
            public Vector2 Zone => (Vector2)typeof(RobotArmController).GetProperty("RecycleZoneHalfExtents", Instance).GetValue(Arms);
            public Fixture(int framesPerSecond = 60, float heading = 0f)
            {
                fps = framesPerSecond;
                Scene = EditorSceneManager.NewPreviewScene();
                Root = new GameObject("Small recycle regression robot");
                SceneManager.MoveGameObjectToScene(Root, Scene);
                Root.transform.SetPositionAndRotation(new Vector3(3f, -2f), Quaternion.Euler(0f, 0f, heading));
                Arms = Root.AddComponent<RobotArmController>();
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Resources/Robot/RobotMarker.prefab");
                Require(prefab != null, "Production robot prefab unavailable");
                var marker = Root.GetComponent<RobotMarkerView>();
                EditorUtility.CopySerialized(prefab.GetComponent<RobotArmController>(), Arms);
                EditorUtility.CopySerialized(prefab.GetComponent<RobotMarkerView>(), marker);
                EditorUtility.CopySerialized(prefab.GetComponent<RobotMover>(), Root.GetComponent<RobotMover>());
                Frame = new GameObject("Marker Visual Root").transform; Frame.SetParent(Root.transform, false);
                RobotArmRegressionChecks.CreateFixtureArtwork(Frame, true);
                Set(marker, "markerVisualRoot", Frame);
                Transform body = Frame.Find("Body Visual"); Set(marker, "bodyVisualRoot", body);
                Set(marker, "mediumRecycleBodyBasePosition", body.localPosition);
                Set(marker, "mediumRecycleBodyBaseRotation", body.localRotation);
                Call(Arms, "Awake"); Call(Arms, "EnsureVisuals");
                TickSeconds(3f, Vector2.up, true, false);
            }
            public WorldInteraction Grab(bool right, float gripOffset)
            {
                GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Environment/Garbage/Small_Garbage.prefab");
                Require(source != null, "Production small garbage unavailable");
                var go = new GameObject("Production sprite small garbage");
                SceneManager.MoveGameObjectToScene(go, Scene);
                go.transform.localScale = source.transform.localScale;
                go.transform.rotation = Root.transform.rotation;
                var visual = new GameObject("Garbage Sprite"); visual.transform.SetParent(go.transform, false);
                var renderer = visual.AddComponent<SpriteRenderer>();
                EditorUtility.CopySerialized(source.GetComponent<SpriteRenderer>(), renderer);
                var item = go.AddComponent<WorldInteraction>();
                EditorUtility.CopySerialized(source.GetComponent<WorldInteraction>(), item);
                item.SpriteSource = renderer;
                item.ColliderType = ColliderType.Sprite;
                Vector2 anchor = right ? Arms.RightHandWorld : Arms.LeftHandWorld;
                Vector2 desired = anchor + (Vector2)Frame.TransformVector(Vector2.right * gripOffset * Diameter);
                item.WorldPosition = desired;
                Tick(1, Vector2.zero, true, true);
                Require(Arms.HeldObject == item && item.Owner == Arms,
                    "Production sprite pickup failed: " + Case(right, gripOffset));
                Require((int)Get(Arms, "heldHands") == (right ? 2 : 1),
                    "Production pickup chose the wrong hand lane: " + Case(right, gripOffset));
                return item;
            }
            public string Case(bool right, float offset) => "FPS=" + fps + ", right=" + right
                + ", heading=" + Root.transform.eulerAngles.z + ", offset=" + offset;
            public Vector2 ItemLocal(WorldInteraction item) => Frame.InverseTransformPoint(item.WorldPosition);
            public void PlaceAt(WorldInteraction item, Vector2 local)
            {
                item.WorldPosition += Frame.TransformVector(local - ItemLocal(item));
                int hands = (int)Get(Arms, "heldHands");
                Vector2 anchor = hands == 1 ? Arms.LeftHandWorld : hands == 2 ? Arms.RightHandWorld
                    : (Arms.LeftHandWorld + Arms.RightHandWorld) * .5f;
                Set(Arms, "heldOffset", (Vector2)Frame.InverseTransformVector(item.WorldPosition - (Vector3)anchor));
            }
            public void Step(float deltaTime, Vector2 stick, bool deploy, bool grab) => Call(Arms, "Step", deltaTime, stick, deploy, grab);
            public void Tick(int frames, Vector2 stick, bool deploy, bool grab)
            { for (int i = 0; i < frames; i++) Step(1f / fps, stick, deploy, grab); }
            public void TickSeconds(float seconds, Vector2 stick, bool deploy, bool grab) => Tick(Mathf.CeilToInt(seconds * fps), stick, deploy, grab);
            public void FinishRecycle(WorldInteraction item, Func<int> recycled, bool deploy = false)
            {
                TickSeconds(2f, Vector2.zero, deploy, false);
                Require(Arms.HeldObject == null && item.Owner == null && !item.gameObject.activeSelf && recycled() == 1
                    && Arms.State == RobotArmState.OuterOperating && Arms.IsArmModeActive
                    && Root.GetComponent<RobotMover>().IsArmInputCaptured,
                    "Small recycling did not complete exactly once and retain deployment/input capture");
            }
            public void Dispose()
            {
                Arms.enabled = false; Call(Arms, "OnDisable");
                for (int i = Frame.childCount - 1; i >= 0; i--) Object.DestroyImmediate(Frame.GetChild(i).gameObject);
                EditorSceneManager.ClosePreviewScene(Scene);
            }
        }
    }
}
