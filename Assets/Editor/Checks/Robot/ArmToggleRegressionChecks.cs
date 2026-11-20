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
                CheckSimultaneousOutsideReleaseAndClose(fps);
                CheckDropCallbacksDoNotRestoreDeployment(fps);
                CheckExternalReleaseStillCloses(fps);
                CheckRecycleKeepsDeployment(fps);
                CheckConsecutiveRecycling(fps);
                CheckRecyclePriorityAndIgnoredPress(fps);
                CheckUnavailableFreshPress(fps);
                CheckDisableFreshPress(fps);
                CheckTumbleToggleAndStanding(fps);
                CheckFallingDropsGripAndKeepsToggle(fps);
                CheckEmptyA(fps);
                CheckBCloseOnlyAndButtonPriority(fps);
                CheckBCloseDuringDeployment(fps);
                CheckBHeldGarbageRelease(fps);
                CheckBOutsideRelease(fps);
                CheckBTumbleClose(fps);
                CheckBSimultaneousRecycleRelease(fps);
                CheckBDuringRecycle(fps);
                CheckBUnavailableInput(fps);
            }
            Debug.Log("Arm toggle checks PASS: 25 raw-input groups at 30/60/120 FPS; short press, release hold, "
                + "long hold, second press, animation reversal, A-held manual close, A-release deployment/pose retention, "
                + "small/medium/big/ordinary drops, re-grab, simultaneous manual close, release callback cancellation, external release, recycle priority/no queue, "
                + "small/medium recycle deployment retention, neutral pose/capture restoration, consecutive garbage, "
                + "external control, disable/rearm, tumble/stand, falling grip release and empty A isolation; "
                + "B close-only/closed-input isolation, L3 priority, animation close, held-item release, tumble close, "
                + "same-frame recycle priority, deferred close during feeding/processing and unavailable input.");
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
            foreach (string kind in new[] { "small", "medium", "big", "ordinary" })
            foreach (bool l3Held in new[] { false, true })
            using (var f = new Fixture(fps))
            {
                bool ordinary = kind == "ordinary";
                RecyclableSize size = kind == "medium" ? RecyclableSize.Medium
                    : kind == "big" ? RecyclableSize.Big : RecyclableSize.Small;
                f.Deploy(l3Held); WorldInteraction item = f.GrabOutside(size, ordinary, l3Held, .35f);
                Require(f.Mover.GrabMovementMultiplier < 1f && (bool)Get(f.Arms, "grabArmSession"),
                    "Drop fixture did not establish grip resistance/session; kind=" + kind);
                float[] pose = f.Snapshot(); Vector3 dropped = item.WorldPosition;
                int released = 0, recycled = 0;
                Set(item, "onReleased", (Action)(() => released++));
                Set(item, "onRecycled", (Action)(() => recycled++));
                f.Step(Vector2.zero, l3Held, false);
                f.RequireOpen("A release outside inlet closed deployment; kind=" + kind);
                Require(f.Arms.HeldObject == null && item.Owner == null && item.gameObject.activeSelf
                    && !(bool)Get(f.Arms, "grabArmSession") && (int)Get(f.Arms, "heldHands") == 0
                    && (bool)Get(f.Arms, "armToggleActive") && !f.Arms.IsRecycleReady
                    && Mathf.Abs(f.Mover.GrabMovementMultiplier - 1f) < .00001f && released == 1 && recycled == 0,
                    "A release failed owner/resistance/session cleanup; kind=" + kind + ", L3=" + l3Held);
                f.RequireSnapshot(pose, "A-release frame changed the actual hand pose; kind=" + kind);
                f.Tick(.5f, Vector2.zero, l3Held, false);
                f.RequireOpen("Neutral input closed arms after an outside drop; kind=" + kind);
                f.RequireSnapshot(pose, "Neutral input moved post-drop hands; kind=" + kind);
                Vector2 before = f.Centre;
                f.Tick(.08f, Vector2.down, l3Held, false);
                Require(f.Centre.magnitude < before.magnitude - .001f
                    && Vector3.Distance(dropped, item.WorldPosition) < .0001f && released == 1,
                    "Post-drop stick failed to move empty arms or moved the released item; kind=" + kind);
                // Remove the first item from pickup queries, then acquire a new one through
                // the still-open controls, without another L3 edge.
                item.enabled = false;
                WorldInteraction next = f.GrabOutside(RecyclableSize.Small, false, l3Held);
                Require(next.Owner == f.Arms && (bool)Get(f.Arms, "grabArmSession"),
                    "A could not grab the next item without redeploying; kind=" + kind);
                if (l3Held) f.Step(Vector2.zero, false, true);
                f.Step(Vector2.zero, true, true);
                f.Tick(3f, Vector2.zero, true, true);
                f.RequireClosed("Fresh L3 could not close after a retained-deployment drop; kind=" + kind);
                Require(next.Owner == null && next.gameObject.activeSelf && f.Arms.HeldObject == null,
                    "Manual close failed to release the next item; kind=" + kind);
            }
        }

        private static void CheckSimultaneousOutsideReleaseAndClose(int fps)
        {
            using (var f = new Fixture(fps))
            {
                f.Deploy(); WorldInteraction item = f.GrabOutside(); Vector3 dropped = item.WorldPosition;
                f.Step(Vector2.right, true, false);
                Require(f.Arms.HeldObject == null && item.Owner == null && item.gameObject.activeSelf
                    && !f.Arms.IsArmModeActive && !f.Mover.IsArmInputCaptured
                    && !(bool)Get(f.Arms, "armToggleActive") && f.Arms.State == RobotArmState.Retracting,
                    "Outside A release overrode a simultaneous manual L3 close");
                f.Tick(3f, Vector2.up, true, false);
                f.RequireClosed("Simultaneous release/close reopened from the old L3 hold");
                Require(Vector3.Distance(dropped, item.WorldPosition) < .0001f,
                    "Simultaneous manual close moved dropped garbage");
            }
        }

        private static void CheckDropCallbacksDoNotRestoreDeployment(int fps)
        {
            foreach (string cancellation in new[] { "disable", "disable/re-enable", "photo", "external control" })
            using (var f = new Fixture(fps))
            {
                f.Deploy(); WorldInteraction item = f.GrabOutside();
                PhotoModeController photo = null;
                if (cancellation == "photo")
                {
                    photo = f.Root.AddComponent<PhotoModeController>();
                    Set(f.Arms, "photoMode", photo);
                }
                Set(item, "onReleased", (Action)(() =>
                {
                    if (cancellation.StartsWith("disable", StringComparison.Ordinal))
                    {
                        f.Arms.enabled = false; Call(f.Arms, "OnDisable");
                        if (cancellation == "disable/re-enable") f.Arms.enabled = true;
                    }
                    else if (cancellation == "photo")
                    {
                        FieldInfo state = typeof(PhotoModeController).GetField("state", Instance);
                        state.SetValue(photo, Enum.Parse(state.FieldType, "Active"));
                    }
                    else SetProperty(f.Mover, "MovementMode", RobotMovementMode.ExternalTumble);
                }));
                f.Step(Vector2.zero, false, false);
                Require(item.Owner == null && f.Arms.HeldObject == null && !f.Arms.IsArmModeActive
                    && !f.Mover.IsArmInputCaptured && !(bool)Get(f.Arms, "armToggleActive"),
                    "Drop retention revived controls after release callback: " + cancellation);
                if (!f.Arms.enabled) f.Arms.enabled = true;
                if (photo != null)
                {
                    FieldInfo state = typeof(PhotoModeController).GetField("state", Instance);
                    state.SetValue(photo, Activator.CreateInstance(state.FieldType));
                }
                SetProperty(f.Mover, "MovementMode", RobotMovementMode.Driven);
                f.Tick(3f, Vector2.left, false, false);
                f.RequireClosed("Old deployment resumed after release callback ended: " + cancellation);
                f.Step(Vector2.zero, true, false); f.Tick(3f, Vector2.zero, false, false);
                f.RequireOpen("Fresh L3 could not deploy after release callback: " + cancellation);
            }
        }

        private static void CheckExternalReleaseStillCloses(int fps)
        {
            using (var f = new Fixture(fps))
            {
                f.Deploy(); WorldInteraction item = f.GrabOutside();
                Require(f.Arms.ReleaseHeldObject(item), "External-release fixture did not own its item");
                Require(item.Owner == null && f.Arms.HeldObject == null && !f.Arms.IsArmModeActive
                    && !f.Mover.IsArmInputCaptured && !(bool)Get(f.Arms, "armToggleActive"),
                    "External ReleaseHeldObject incorrectly retained deployment");
                f.Tick(3f, Vector2.right, false, true);
                f.RequireClosed("A alone resumed deployment after external release");
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

        private static void CheckBCloseOnlyAndButtonPriority(int fps)
        {
            using (var f = new Fixture(fps))
            {
                f.Step(Vector2.up, false, true, true);
                f.Tick(.5f, Vector2.up, false, true);
                f.RequireClosed("B opened fully retracted arms");
                // Closing is meaningful only while the arms are out. B must not consume
                // a fresh deployment press when there is nothing to close.
                f.Step(Vector2.zero, true, false, true);
                f.Tick(3f, Vector2.zero, true, false);
                f.RequireOpen("B on retracted arms swallowed a simultaneous fresh L3 deployment");
                f.Step(Vector2.zero, false, false);
                f.Step(Vector2.right, true, false, true);
                Require(!f.Arms.IsArmModeActive && !f.Mover.IsArmInputCaptured
                    && f.Arms.State == RobotArmState.Retracting,
                    "B and L3 on deployed arms did not close immediately");
                f.Tick(3f, Vector2.up, true, false);
                f.RequireClosed("A held L3 reopened after B closed the arms");
                f.Step(Vector2.up, true, true, true);
                f.Tick(.5f, Vector2.up, true, true);
                f.RequireClosed("B or A reopened closed arms after a manual close");
                f.Step(Vector2.zero, false, false); f.Step(Vector2.zero, true, false);
                f.Tick(3f, Vector2.zero, false, false);
                f.RequireOpen("B close prevented a later fresh L3 deployment");
                f.Step(Vector2.left, false, false, true);
                f.Tick(3f, Vector2.left, false, false);
                f.RequireClosed("B alone could not close empty deployed arms");
            }
        }

        private static void CheckBCloseDuringDeployment(int fps)
        {
            using (var f = new Fixture(fps))
            {
                f.Step(Vector2.zero, true, false);
                f.Tick(.15f, Vector2.zero, false, false);
                Require(f.Arms.VisibleDeployment01 > 0f && f.Arms.VisibleDeployment01 < 1f,
                    "B extension fixture did not stop during deployment");
                f.Step(Vector2.up, false, false, true);
                Require(!f.Arms.IsArmModeActive && f.Arms.State == RobotArmState.Retracting,
                    "B did not reverse an in-progress deployment");
                // L3 normally reverses retraction. A simultaneous B must keep the
                // visible arms closing instead of treating it as a new deployment.
                f.Step(Vector2.up, true, false, true);
                Require(!f.Arms.IsArmModeActive && f.Arms.State == RobotArmState.Retracting,
                    "Simultaneous B/L3 reopened partially retracting arms");
                f.Tick(3f, Vector2.up, true, false);
                f.RequireClosed("B deployment reversal did not finish closing");
            }
        }

        private static void CheckBHeldGarbageRelease(int fps)
        {
            foreach (string kind in new[] { "small", "medium", "big", "ordinary" })
            using (var f = new Fixture(fps))
            {
                RecyclableSize size = kind == "medium" ? RecyclableSize.Medium
                    : kind == "big" ? RecyclableSize.Big : RecyclableSize.Small;
                f.Deploy(); WorldInteraction item = f.GrabOutside(size, kind == "ordinary", false, .35f);
                int released = 0, recycled = 0;
                Set(item, "onReleased", (Action)(() => released++));
                Set(item, "onRecycled", (Action)(() => recycled++));
                Vector3 position = item.WorldPosition;
                f.Step(Vector2.down, false, true, true);
                Require(item.Owner == null && item.gameObject.activeSelf && f.Arms.HeldObject == null
                    && released == 1 && recycled == 0 && !f.Arms.IsArmModeActive
                    && !f.Mover.IsArmInputCaptured && f.Arms.State == RobotArmState.Retracting
                    && Mathf.Abs(f.Mover.GrabMovementMultiplier - 1f) < .00001f,
                    "B with A held failed to drop once and close; kind=" + kind);
                Require(Vector3.Distance(position, item.WorldPosition) < .0001f,
                    "B moved the held item before dropping it; kind=" + kind);
                f.Tick(3f, Vector2.down, false, true);
                f.RequireClosed("A reopened the arms after a B drop; kind=" + kind);
                Require(released == 1 && recycled == 0, "B repeated an item release; kind=" + kind);
            }
        }

        private static void CheckBOutsideRelease(int fps)
        {
            using (var f = new Fixture(fps))
            {
                f.Deploy(); WorldInteraction item = f.GrabOutside(); Vector3 position = item.WorldPosition;
                int released = 0; Set(item, "onReleased", (Action)(() => released++));
                f.Step(Vector2.right, false, false, true);
                Require(item.Owner == null && item.gameObject.activeSelf && f.Arms.HeldObject == null
                    && released == 1 && !f.Arms.IsArmModeActive && !f.Mover.IsArmInputCaptured
                    && f.Arms.State == RobotArmState.Retracting,
                    "A release outside the inlet overrode a simultaneous B close");
                f.Tick(3f, Vector2.right, false, false);
                f.RequireClosed("A release/B close retained the previous deployment switch");
                Require(Vector3.Distance(position, item.WorldPosition) < .0001f && released == 1,
                    "A release/B close moved or repeatedly released the garbage");
            }
        }

        private static void CheckBTumbleClose(int fps)
        {
            using (var f = new Fixture(fps))
            {
                f.Fall(); f.Step(Vector2.one.normalized, true, false);
                f.Tick(3f, Vector2.one.normalized, false, false);
                Require(f.Arms.IsTumbleArmLayoutActive && f.Arms.IsArmModeActive,
                    "B tumble fixture did not deploy the side layout");
                f.Step(Vector2.one.normalized, false, false, true);
                Require(!f.Arms.IsArmModeActive && !f.Mover.IsArmInputCaptured,
                    "B failed to release side-layout input capture");
                f.Tick(3f, Vector2.one.normalized, false, false);
                f.RequireClosed("B failed to retract tumble arms");
                SetProperty(f.Tumble, "State", RobotTumbleState.Upright);
                f.Tick(3f, Vector2.zero, false, false);
                f.RequireClosed("Standing reopened a tumble layout that B had closed");
            }
        }

        private static void CheckBSimultaneousRecycleRelease(int fps)
        {
            foreach (RecyclableSize size in new[] { RecyclableSize.Small, RecyclableSize.Medium })
            using (var f = new Fixture(fps))
            {
                f.Deploy(); WorldInteraction item = f.GrabOutside(size); f.PlaceInlet(item);
                int released = 0, recycled = 0;
                Set(item, "onReleased", (Action)(() => released++));
                Set(item, "onRecycled", (Action)(() => recycled++));
                f.Step(Vector2.right, false, false, true);
                Require(f.Arms.State == RobotArmState.Recycling && f.Arms.HeldObject == item
                    && item.Owner == f.Arms && released == 0 && recycled == 0,
                    "B on the inlet A-release frame cancelled recycling; size=" + size);
                f.Tick(8f, Vector2.right, false, true);
                f.RequireClosed("B on the inlet A-release frame did not close after recycling; size=" + size);
                Require(recycled == 1 && released == 0 && item.Owner == null && !item.gameObject.activeSelf,
                    "B changed the inlet item's successful one-shot recycle; size=" + size);
            }
        }

        private static void CheckBDuringRecycle(int fps)
        {
            foreach (RecyclableSize size in new[] { RecyclableSize.Small, RecyclableSize.Medium })
            foreach (bool processing in new[] { false, true })
            using (var f = new Fixture(fps))
            {
                f.Deploy(); WorldInteraction item = f.GrabOutside(size); f.PlaceInlet(item);
                int released = 0, recycled = 0; float completedAt = 0f;
                Set(item, "onReleased", (Action)(() => released++));
                Set(item, "onRecycled", (Action)(() => { recycled++; completedAt = (float)Get(f.Arms, "recycleTime"); }));
                f.Step(Vector2.zero, false, false);
                if (processing)
                {
                    for (int i = 0; i < fps * 8 && f.Arms.State == RobotArmState.Recycling
                        && f.Arms.CurrentMediumRecycleFrame.Phase != MediumRecyclePhase.Processing; i++)
                        f.Step(Vector2.zero, false, false);
                    Require(f.Arms.State == RobotArmState.Recycling
                        && f.Arms.CurrentMediumRecycleFrame.Phase == MediumRecyclePhase.Processing,
                        "B recycle fixture did not reach body processing; size=" + size);
                }
                f.Step(Vector2.right, true, true, true);
                Require(f.Arms.State == RobotArmState.Recycling && f.Arms.HeldObject == item
                    && item.Owner == f.Arms && item.gameObject.activeSelf && released == 0 && recycled == 0,
                    "B interrupted an active recycle; size=" + size + ", processing=" + processing);
                // Distinct L3 presses during the operation must not clear B's close
                // request or queue a later reopening, including a hold across completion.
                f.Step(Vector2.left, false, true); f.Step(Vector2.right, true, true);
                f.Tick(8f, Vector2.up, true, true);
                f.RequireClosed("B during recycling was lost or later input reopened it; size=" + size);
                float expectedDuration = size == RecyclableSize.Small
                    ? (float)Get(f.Arms, "recycleDuration") + MediumRecycleMotion.ProcessingDuration(
                        (float)Get(f.Arms, "mediumRecycleFinishDuration"), (float)Get(f.Arms, "smallRecycleProcessingDuration"))
                    : MediumRecycleMotion.Duration((float)Get(f.Arms, "mediumRecycleClampDuration"),
                        (Vector3)Get(f.Arms, "mediumRecycleStrokeDurations"), (float)Get(f.Arms, "mediumRecycleFinishDuration"),
                        (float)Get(f.Arms, "mediumRecycleProcessingDuration"));
                Require(recycled == 1 && released == 0 && item.Owner == null && !item.gameObject.activeSelf
                    && completedAt >= expectedDuration - .0001f,
                    "B shortened the recycle or changed item completion; size=" + size);
                f.Step(Vector2.zero, false, false); f.Step(Vector2.zero, true, false);
                f.Tick(3f, Vector2.zero, false, false);
                f.RequireOpen("A fresh post-B press could not reopen after recycling; size=" + size);
            }
        }

        private static void CheckBUnavailableInput(int fps)
        {
            foreach (bool photoActive in new[] { false, true })
            using (var f = new Fixture(fps))
            {
                f.Deploy(); PhotoModeController photo = null;
                FieldInfo photoState = typeof(PhotoModeController).GetField("state", Instance);
                if (photoActive)
                {
                    photo = f.Root.AddComponent<PhotoModeController>(); Set(f.Arms, "photoMode", photo);
                    photoState.SetValue(photo, Enum.Parse(photoState.FieldType, "Active"));
                }
                else SetProperty(f.Mover, "MovementMode", RobotMovementMode.ExternalTumble);
                f.Step(Vector2.right, true, false, true);
                Require(!f.Arms.IsArmModeActive && !f.Mover.IsArmInputCaptured,
                    "Unavailable B input retained gameplay capture");
                if (photo != null)
                {
                    Require(photo.IsActive, "Arm B handling exited photo mode through simulation");
                    photoState.SetValue(photo, Activator.CreateInstance(photoState.FieldType));
                }
                SetProperty(f.Mover, "MovementMode", RobotMovementMode.Driven);
                f.Tick(3f, Vector2.up, true, false);
                f.RequireClosed("Unavailable B/L3 input queued a deployment");
                f.Step(Vector2.zero, false, false); f.Step(Vector2.zero, true, false);
                f.Tick(3f, Vector2.zero, false, false);
                f.RequireOpen("Unavailable B prevented a fresh allowed L3 press");
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
            public void Step(Vector2 stick, bool rawL3, bool grab, bool retractPressed = false)
                => Call(Arms, "StepInput", 1f / fps, stick, rawL3, grab, retractPressed);
            public void Tick(float seconds, Vector2 stick, bool rawL3, bool grab)
            { for (int i = 0; i < Mathf.CeilToInt(seconds * fps); i++) Step(stick, rawL3, grab); }
            public void Deploy(bool keepL3Held = false)
            { Step(Vector2.zero, true, false); Tick(3f, Vector2.zero, keepL3Held, false); RequireOpen("Short press failed to remain deployed"); }
            public void RequireOpen(string message) => Require(Arms.IsArmModeActive && Mover.IsArmInputCaptured
                && Arms.VisibleDeployment01 > .999f && Arms.State == RobotArmState.OuterOperating, message + "; FPS=" + fps);
            public void RequireClosed(string message) => Require(!Arms.IsArmModeActive && !Mover.IsArmInputCaptured
                && Arms.State == RobotArmState.Retracted && Arms.VisibleDeployment01 < .0001f, message + "; FPS=" + fps);
            public WorldInteraction GrabOutside(RecyclableSize size = RecyclableSize.Small, bool ordinary = false,
                bool rawL3 = false, float resistance = 0f)
            {
                Tick(1f, Vector2.up, rawL3, false);
                var go = new GameObject("Raw toggle held garbage"); SceneManager.MoveGameObjectToScene(go, Root.scene);
                bool twoHands = !ordinary && size != RecyclableSize.Small;
                go.transform.position = twoHands ? (Arms.LeftHandWorld + Arms.RightHandWorld) * .5f : Arms.LeftHandWorld;
                go.transform.rotation = Root.transform.rotation;
                var item = go.AddComponent<WorldInteraction>(); item.SetKind(WorldInteractionKind.Grabbable);
                item.LocalSize = twoHands ? new Vector2(.7f, .15f) : Vector2.one * .05f;
                Set(item, "requiredHands", twoHands ? 2 : 1);
                Set(item, "size", size); Set(item, "grabResistance", resistance);
                if (ordinary || size == RecyclableSize.Big) Set(item, "recyclable", false);
                Step(Vector2.zero, rawL3, true);
                Require(Arms.HeldObject == item && item.Owner == Arms && !Arms.IsRecycleReady,
                    "Raw toggle fixture did not grab outside the inlet at " + fps + " FPS");
                return item;
            }
            public float[] Snapshot()
            {
                float[] pose = new float[15]; int i = 0;
                foreach (string side in new[] { "left", "right" })
                {
                    object armPose = Get(Get(Arms, side), "Pose");
                    foreach (string field in new[] { "LowerAngle", "UpperAngle", "LowerLength", "UpperLength", "Hand" })
                        pose[i++] = (float)Get(armPose, field);
                }
                pose[i++] = Arms.LeftHandWorld.x; pose[i++] = Arms.LeftHandWorld.y;
                pose[i++] = Arms.RightHandWorld.x; pose[i++] = Arms.RightHandWorld.y;
                pose[i] = Root.transform.eulerAngles.z; return pose;
            }
            public void RequireSnapshot(float[] before, string message)
            {
                float[] after = Snapshot();
                for (int i = 0; i < before.Length; i++)
                    Require(Mathf.Abs(before[i] - after[i]) < .0001f, message + "; pose=" + i + "; FPS=" + fps);
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
