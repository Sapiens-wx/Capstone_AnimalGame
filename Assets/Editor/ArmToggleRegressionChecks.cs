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
    // These checks send raw button-held samples directly to production Step. They deliberately
    // bypass the desired-deployment adapter used by the older geometry and feedback fixtures.
    public static class ArmToggleRegressionChecks
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly int[] FrameRates = { 30, 60, 120 };

        [MenuItem("Animal Game/Validation/Run Arm Toggle Checks")]
        public static void Run()
        {
            foreach (int fps in FrameRates)
            {
                CheckShortPressAndNeutral(fps);
                CheckLongHoldAndSecondPress(fps);
                CheckDeploymentReversal(fps);
                CheckRetractionReversal(fps);
                CheckGrabToggleOff(fps);
                CheckGrabReleaseOutside(fps);
                CheckRecycleKeepsDeployment(fps);
                CheckConsecutiveRecycling(fps);
                CheckRecyclePriorityAndIgnoredPress(fps);
                CheckUnavailableFreshPress(fps);
                CheckDisableFreshPress(fps);
                CheckTumbleToggleAndStanding(fps);
                CheckFallingDropsGripAndKeepsToggle(fps);
                CheckEmptyA(fps);
            }
            Debug.Log("Arm toggle checks PASS: 14 raw-input groups at 30/60/120 FPS; short press, release hold, "
                + "long hold, second press, animation reversal, A-held drop, A-release close, recycle priority/no queue, "
                + "small/medium recycle deployment retention, neutral pose/capture restoration, consecutive garbage, "
                + "external control, disable/rearm, tumble/stand, falling grip release and empty A isolation.");
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void CheckShortPressAndNeutral(int fps)
        {
            using (var f = new Fixture(fps))
            {
                f.Deploy();
                Vector2 before = f.Centre;
                f.Tick(.05f, Vector2.up, false, false);
                Require(f.Centre.magnitude > before.magnitude + .001f,
                    "Released L3 did not retain stick control at " + fps + " FPS");
                before = f.Centre;
                f.Tick(.5f, Vector2.zero, false, false);
                Require(Vector2.Distance(before, f.Centre) < .0001f && f.Arms.IsArmModeActive
                    && f.Mover.IsArmInputCaptured, "Neutral input moved or closed toggled arms");
            }
        }

        private static void CheckLongHoldAndSecondPress(int fps)
        {
            using (var f = new Fixture(fps))
            {
                f.Tick(3f, Vector2.zero, true, false); f.RequireOpen("Long first hold toggled repeatedly");
                f.Step(Vector2.zero, false, false);
                f.Tick(3f, Vector2.zero, true, false); f.RequireClosed("Second press did not close, or held input reopened");
                f.Tick(.5f, Vector2.up, true, false); f.RequireClosed("Continued second hold reopened the arms");
                f.Step(Vector2.zero, false, false); f.Step(Vector2.zero, true, false);
                f.Tick(3f, Vector2.zero, false, false); f.RequireOpen("Third distinct press did not reopen");
            }
        }

        private static void CheckDeploymentReversal(int fps)
        {
            using (var f = new Fixture(fps))
            {
                f.Step(Vector2.zero, true, false);
                f.Tick(.15f, Vector2.zero, false, false);
                Require(f.Arms.VisibleDeployment01 > 0f && f.Arms.VisibleDeployment01 < 1f,
                    "Reversal fixture did not stop partway through deployment");
                f.Step(Vector2.zero, true, false);
                Require(f.Arms.State == RobotArmState.Retracting && !f.Arms.IsArmModeActive,
                    "Second press during extension did not reverse immediately");
                f.Tick(3f, Vector2.zero, false, false); f.RequireClosed("Extension reversal did not finish retracting");
            }
        }

        private static void CheckRetractionReversal(int fps)
        {
            using (var f = new Fixture(fps))
            {
                f.Deploy(); f.Step(Vector2.zero, true, false);
                f.Tick(.1f, Vector2.zero, false, false);
                f.Step(Vector2.zero, true, false);
                Require(f.Arms.IsArmModeActive && f.Arms.State != RobotArmState.Retracted,
                    "Press during retraction did not start reopening");
                f.Tick(3f, Vector2.zero, false, false); f.RequireOpen("Retraction reversal did not deploy");
            }
        }

        private static void CheckGrabToggleOff(int fps)
        {
            using (var f = new Fixture(fps))
            {
                f.Deploy(); WorldInteraction item = f.GrabOutside();
                Vector3 start = item.WorldPosition;
                f.Tick(.03f, Vector2.down, false, true);
                Require(f.Arms.HeldObject == item && Vector3.Distance(start, item.WorldPosition) > .001f,
                    "Released L3 could not move A-held garbage");
                f.Step(Vector2.zero, true, true);
                Require(f.Arms.HeldObject == null && item.Owner == null && item.gameObject.activeSelf
                    && !f.Arms.IsArmModeActive && f.Arms.State == RobotArmState.Retracting,
                    "Second L3 press while holding A did not drop and close");
                f.Tick(3f, Vector2.zero, true, true); f.RequireClosed("A or old L3 hold reopened a dropped session");
            }
        }

        private static void CheckGrabReleaseOutside(int fps)
        {
            using (var f = new Fixture(fps))
            {
                f.Deploy(); WorldInteraction item = f.GrabOutside();
                f.Step(Vector2.zero, false, false);
                Require(f.Arms.HeldObject == null && item.Owner == null && item.gameObject.activeSelf
                    && !f.Arms.IsArmModeActive, "A release outside inlet did not close the toggled session");
                f.Tick(3f, Vector2.zero, false, true); f.RequireClosed("A re-press reopened closed arms");
                f.Step(Vector2.zero, true, false); f.Tick(3f, Vector2.zero, false, false);
                f.RequireOpen("Fresh L3 after A release could not deploy");
            }
        }

        private static void CheckRecyclePriorityAndIgnoredPress(int fps)
        {
            foreach (RecyclableSize size in new[] { RecyclableSize.Small, RecyclableSize.Medium })
            using (var f = new Fixture(fps))
            {
                f.Deploy(); WorldInteraction item = f.GrabOutside(size); f.PlaceInlet(item);
                int recycled = 0; Set(item, "onRecycled", (Action)(() => recycled++));
                f.Step(Vector2.right, true, false);
                Require(f.Arms.State == RobotArmState.Recycling && f.Arms.HeldObject == item,
                    "Simultaneous A release and toggle-off dropped an inlet item instead of recycling");
                // Consume several distinct presses while the animation is running, then keep
                // the last one held across its final frame. None may queue deployment.
                f.Step(Vector2.left, false, true); f.Step(Vector2.right, true, true);
                f.Step(Vector2.left, false, true); f.Step(Vector2.right, true, true);
                f.Tick(8f, Vector2.up, true, true);
                f.RequireClosed("A press during recycling queued a reopen");
                Require(recycled == 1 && !item.gameObject.activeSelf && item.Owner == null,
                    "Toggle commands changed recycle completion or ownership");
                f.Step(Vector2.zero, false, false); f.Step(Vector2.zero, true, false);
                f.Tick(3f, Vector2.zero, false, false); f.RequireOpen("Fresh post-recycle press could not reopen");
            }
        }

        private static void CheckRecycleKeepsDeployment(int fps)
        {
            foreach (RecyclableSize size in new[] { RecyclableSize.Small, RecyclableSize.Medium })
            using (var f = new Fixture(fps))
            {
                f.Deploy(); WorldInteraction item = f.GrabOutside(size); f.PlaceInlet(item);
                int recycled = 0; Set(item, "onRecycled", (Action)(() => recycled++));
                f.Step(Vector2.right, false, false);
                Require(f.Arms.State == RobotArmState.Recycling && f.Arms.HeldObject == item,
                    "A release did not begin the retained-deployment recycle; size=" + size);
                // Distinct L3 edges inside the operation must not replace the switch that
                // was active when recycling began. Hold the final press across completion.
                f.Step(Vector2.left, true, true); f.Step(Vector2.right, false, true);
                f.Step(Vector2.left, true, true); f.Step(Vector2.right, false, true);
                f.Step(Vector2.left, true, true);
                bool sawProcessing = false;
                for (int i = 0; i < fps * 8 && f.Arms.State == RobotArmState.Recycling; i++)
                {
                    if (f.Arms.CurrentMediumRecycleFrame.Phase == MediumRecyclePhase.Processing)
                    {
                        sawProcessing = true;
                        Require(!f.Mover.IsArmInputCaptured,
                            "Processing stopped releasing movement input; size=" + size);
                    }
                    f.Step(Vector2.right, true, true);
                }
                f.RequireOpen("Successful recycling discarded the deployment switch; size=" + size);
                Require(sawProcessing && recycled == 1 && item.Owner == null && !item.gameObject.activeSelf
                    && f.Arms.HeldObject == null && f.Arms.CurrentInputMagnitude == 0f
                    && f.Arms.CurrentTargetLocal == Vector2.zero && !f.Arms.IsRecycleReady,
                    "Recycle completion failed ownership, one-shot, processing or input cleanup; size=" + size);
                Vector2 left = f.Arms.LeftHandWorld, right = f.Arms.RightHandWorld;
                f.Tick(.5f, Vector2.zero, true, true);
                f.RequireOpen("Held animation input queued a toggle after completion; size=" + size);
                Require(Vector2.Distance(left, f.Arms.LeftHandWorld) < .0001f
                    && Vector2.Distance(right, f.Arms.RightHandWorld) < .0001f,
                    "Neutral input snapped the post-recycle hands to another pose; size=" + size);
                Vector2 before = f.Centre;
                f.Tick(.05f, Vector2.up, true, false);
                Require(f.Centre.magnitude > before.magnitude + .001f,
                    "Post-recycle stick control did not resume from the actual hands; size=" + size);
                f.Step(Vector2.zero, false, false); f.Step(Vector2.zero, true, false);
                f.Tick(3f, Vector2.zero, true, false);
                f.RequireClosed("A fresh manual L3 press failed to close after recycling; size=" + size);
            }
        }

        private static void CheckConsecutiveRecycling(int fps)
        {
            foreach (RecyclableSize first in new[] { RecyclableSize.Small, RecyclableSize.Medium })
            using (var f = new Fixture(fps))
            {
                f.Deploy(); int recycled = 0;
                foreach (RecyclableSize size in new[] { first, first == RecyclableSize.Small
                    ? RecyclableSize.Medium : RecyclableSize.Small })
                {
                    WorldInteraction item = f.GrabOutside(size);
                    Set(item, "onRecycled", (Action)(() => recycled++)); f.PlaceInlet(item);
                    int before = recycled;
                    f.Step(Vector2.zero, false, false);
                    Require(f.Arms.State == RobotArmState.Recycling,
                        "Consecutive garbage could not enter recycling without another L3 press");
                    for (int i = 0; i < fps * 8 && f.Arms.State == RobotArmState.Recycling; i++)
                        f.Step(Vector2.zero, false, false);
                    f.RequireOpen("Consecutive recycling closed the arms");
                    Require(recycled == before + 1 && item.Owner == null && !item.gameObject.activeSelf,
                        "Consecutive recycling lost ownership cleanup or repeated rewards");
                }
                Require(recycled == 2, "Both consecutive garbage items did not finish exactly once");
                f.Step(Vector2.zero, true, false); f.Tick(3f, Vector2.zero, false, false);
                f.RequireClosed("Manual L3 could not close after two consecutive garbage items");
            }
        }

        private static void CheckUnavailableFreshPress(int fps)
        {
            using (var f = new Fixture(fps))
            {
                f.Deploy(); SetProperty(f.Mover, "MovementMode", RobotMovementMode.ExternalTumble);
                f.Step(Vector2.zero, true, false);
                Require(!f.Arms.IsArmModeActive && !f.Mover.IsArmInputCaptured,
                    "Unavailable controller retained toggle input capture");
                SetProperty(f.Mover, "MovementMode", RobotMovementMode.Driven);
                f.Tick(3f, Vector2.zero, true, false); f.RequireClosed("Blocked press reopened after external control ended");
                f.Step(Vector2.zero, false, false); f.Step(Vector2.zero, true, false);
                f.Tick(3f, Vector2.zero, false, false); f.RequireOpen("External control did not rearm on a fresh press");
            }
        }

        private static void CheckDisableFreshPress(int fps)
        {
            using (var f = new Fixture(fps))
            {
                f.Tick(3f, Vector2.zero, true, false); f.RequireOpen("Disable fixture failed deployment");
                f.Arms.enabled = false; Call(f.Arms, "OnDisable"); f.Arms.enabled = true;
                f.Tick(3f, Vector2.zero, true, false); f.RequireClosed("Disable/re-enable resumed an old press");
                f.Step(Vector2.zero, false, false); f.Step(Vector2.zero, true, false);
                f.Tick(3f, Vector2.zero, false, false); f.RequireOpen("Disable did not permit a fresh press");
            }
        }

        private static void CheckTumbleToggleAndStanding(int fps)
        {
            using (var f = new Fixture(fps))
            {
                f.Fall(); f.Step(Vector2.one.normalized, true, false);
                f.Tick(3f, Vector2.one.normalized, false, false);
                Require(f.Arms.IsTumbleArmLayoutActive && f.Arms.IsArmModeActive,
                    "A released first press did not retain tumble arms");
                f.Step(Vector2.zero, true, false); f.Tick(3f, Vector2.zero, false, false);
                f.RequireClosed("Second tumble press did not retract");
                f.Step(Vector2.one.normalized, true, false); f.Tick(3f, Vector2.one.normalized, false, false);
                SetProperty(f.Tumble, "State", RobotTumbleState.Upright);
                f.Tick(3f, Vector2.zero, false, false);
                Require(f.Arms.IsArmModeActive && !f.Arms.IsTumbleArmLayoutActive
                    && f.Arms.VisibleDeployment01 > .999f, "Standing discarded a released deployment toggle");
            }
        }

        private static void CheckFallingDropsGripAndKeepsToggle(int fps)
        {
            using (var f = new Fixture(fps))
            {
                f.Deploy(); WorldInteraction item = f.GrabOutside(); f.Fall();
                f.Step(Vector2.one.normalized, false, true);
                Require(f.Arms.HeldObject == null && item.Owner == null && f.Arms.IsArmModeActive
                    && f.Mover.IsArmInputCaptured && f.Arms.IsTumbleArmLayoutActive,
                    "Falling failed to drop the grip while preserving the deployment switch");
                f.Step(Vector2.zero, true, true); f.Tick(3f, Vector2.zero, false, true);
                f.RequireClosed("A-held garbage session prevented closing after a fall");
            }
        }

        private static void CheckEmptyA(int fps)
        {
            using (var f = new Fixture(fps))
            {
                f.Tick(2f, Vector2.up, false, true); f.RequireClosed("A alone deployed empty arms");
                f.Step(Vector2.zero, true, true); f.Tick(3f, Vector2.zero, false, true);
                f.Step(Vector2.zero, false, false); f.RequireOpen("Releasing A without a grip closed the deployment toggle");
            }
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        private static object Get(object target, string name) => target.GetType().GetField(name, Instance).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Instance).SetValue(target, value);
        private static void SetProperty(object target, string name, object value) => target.GetType().GetProperty(name, Instance).SetValue(target, value);
        private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Instance).Invoke(target, args);

        private sealed class Fixture : IDisposable
        {
            private readonly object artworkFixture;
            private readonly int fps;
            public GameObject Root { get; }
            public RobotArmController Arms { get; }
            public RobotMover Mover { get; }
            public RobotTumbleController Tumble { get; }
            public Transform Frame { get; }
            public Vector2 Centre => Frame.InverseTransformPoint((Arms.LeftHandWorld + Arms.RightHandWorld) * .5f);
            public Fixture(int frameRate)
            {
                fps = frameRate;
                Type type = typeof(RobotArmRegressionChecks).GetNestedType("Fixture", BindingFlags.NonPublic);
                artworkFixture = Activator.CreateInstance(type, Instance, null, new object[] { true }, null);
                Root = (GameObject)type.GetProperty("Root", Instance).GetValue(artworkFixture);
                Arms = (RobotArmController)type.GetProperty("Arms", Instance).GetValue(artworkFixture);
                Mover = Root.GetComponent<RobotMover>(); Call(Mover, "Awake");
                Tumble = Root.AddComponent<RobotTumbleController>();
                Call(Arms, "Awake");
                Frame = Root.GetComponent<RobotMarkerView>().MarkerVisualRoot;
            }
            public void Step(Vector2 stick, bool rawL3, bool grab) => Call(Arms, "Step", 1f / fps, stick, rawL3, grab);
            public void Tick(float seconds, Vector2 stick, bool rawL3, bool grab)
            { for (int i = 0; i < Mathf.CeilToInt(seconds * fps); i++) Step(stick, rawL3, grab); }
            public void Deploy()
            { Step(Vector2.zero, true, false); Tick(3f, Vector2.zero, false, false); RequireOpen("Short press failed to remain deployed"); }
            public void RequireOpen(string message) => Require(Arms.IsArmModeActive && Mover.IsArmInputCaptured
                && Arms.VisibleDeployment01 > .999f && Arms.State == RobotArmState.OuterOperating, message + "; FPS=" + fps);
            public void RequireClosed(string message) => Require(!Arms.IsArmModeActive && !Mover.IsArmInputCaptured
                && Arms.State == RobotArmState.Retracted && Arms.VisibleDeployment01 < .0001f, message + "; FPS=" + fps);
            public WorldInteraction GrabOutside(RecyclableSize size = RecyclableSize.Small)
            {
                Tick(1f, Vector2.up, false, false);
                var go = new GameObject("Raw toggle held garbage"); SceneManager.MoveGameObjectToScene(go, Root.scene);
                bool medium = size == RecyclableSize.Medium;
                go.transform.position = medium ? (Arms.LeftHandWorld + Arms.RightHandWorld) * .5f : Arms.LeftHandWorld;
                go.transform.rotation = Root.transform.rotation;
                var item = go.AddComponent<WorldInteraction>(); item.SetKind(WorldInteractionKind.Grabbable);
                item.LocalSize = medium ? new Vector2(.7f, .15f) : Vector2.one * .05f;
                Set(item, "requiredHands", medium ? 2 : 1);
                Set(item, "size", size); Set(item, "grabResistance", 0f);
                Step(Vector2.zero, false, true);
                Require(Arms.HeldObject == item && item.Owner == Arms && !Arms.IsRecycleReady,
                    "Raw toggle fixture did not grab outside the inlet at " + fps + " FPS");
                return item;
            }
            public void PlaceInlet(WorldInteraction item)
            {
                Vector2 inlet = (Vector2)typeof(RobotArmController).GetProperty("DockLocal", Instance).GetValue(Arms);
                item.WorldPosition = Frame.TransformPoint(inlet);
                int hands = (int)Get(Arms, "heldHands");
                Vector2 anchor = hands == 3 ? (Arms.LeftHandWorld + Arms.RightHandWorld) * .5f
                    : hands == 1 ? Arms.LeftHandWorld : Arms.RightHandWorld;
                Set(Arms, "heldOffset", (Vector2)Frame.InverseTransformVector(item.WorldPosition - (Vector3)anchor));
                Require((bool)Call(Arms, "IsHeldInRecycleZone"), "Raw toggle inlet fixture is outside the reception channel");
            }
            public void Fall()
            {
                SetProperty(Tumble, "DirectionWorld", (Vector2)Root.transform.TransformDirection(Vector2.one.normalized));
                SetProperty(Tumble, "State", RobotTumbleState.Fallen);
            }
            public void Dispose() => ((IDisposable)artworkFixture).Dispose();
        }
    }
}
