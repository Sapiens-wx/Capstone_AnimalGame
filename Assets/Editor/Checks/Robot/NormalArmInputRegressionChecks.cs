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
    // Exercise production Step, artwork and collision queries in isolated preview scenes.
    // Inspect actual hand endpoints and arm poses, not merely a requested target.
    public static class NormalArmInputRegressionChecks
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly int[] FrameRates = { 30, 60, 120 };

        [MenuItem("Animal Game/Validation/Run Normal Arm Input Checks")]
        public static void Run()
        {
            CheckProductionSettings();
            CheckCentreHoldsActualPose();
            CheckRadialDirectionAndStops();
            CheckProgressiveSpeedAndFrameRates();
            CheckTangentialTravelSpeed();
            CheckTravelDurationControlsBothAxes();
            CheckLiveTurnSpeedAndResistance();
            CheckDiagonalAndAxisIsolation();
            CheckReachLimits();
            CheckBoundaryBodyFollow();
            CheckBlockedMotionHasNoBacklog();
            CheckDeploymentAndSessionReset();
            CheckGrabMaintainsControlWithoutL3();
            CheckGrabReleaseKeepsDeployment();
            CheckGrabSessionCancellation();
            CheckEmptyArmRequiresL3();
            CheckManualDeliveryAndRecycle();
            CheckHeavyAnchorIsolation();
            CheckTumbleIsolation();
            Debug.Log("Normal arm input checks PASS: 19 groups; production settings, actual-pose hold, radial rates, "
                + "progressive speed, shared radial/tangential travel rates, duration scaling, turn-speed isolation/resistance, axis isolation, reach limits, "
                + "boundary follow, collision backlog, deployment/session reset, A-held control after L3 release, "
                + "grab release deployment/actual-pose retention, cancellation, empty-arm input, manual recycling, heavy anchors and "
                + "tumble isolation; key cases at 30/60/120 FPS.");
        }
        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
        public static void RunAllBatch()
        {
            try
            {
                Run(); RobotArmRegressionChecks.Run(); TumbleArmRegressionChecks.Run();
                MediumRecycleRegressionChecks.Run(); EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void CheckProductionSettings()
        {
            using (var f = new Fixture(60, false))
            {
                foreach (var setting in new[] { ("normalControlDeadZone", .14f), ("normalAxisDeadZone", .10f),
                    ("normalFollowEnterMagnitude", .55f), ("normalFollowExitMagnitude", .45f) })
                    Require(Mathf.Abs((float)Get(f.Arms, setting.Item1) - setting.Item2) < .000001f,
                        "Production robot uses an unexpected " + setting.Item1);
                float duration = (float)Get(f.Arms, "normalFullTravelDuration");
                float exponent = (float)Get(f.Arms, "normalSpeedExponent");
                Require(!float.IsNaN(duration) && !float.IsInfinity(duration) && duration >= .01f,
                    "Production full-travel duration must remain an editable positive setting");
                Require(!float.IsNaN(exponent) && exponent >= 1f && exponent <= 3f,
                    "Production speed exponent is outside its supported range");
            }
        }
        private static void CheckCentreHoldsActualPose()
        {
            foreach (int fps in FrameRates)
            foreach (float angle in new[] { -55f, 0f, 55f })
            foreach (float radius in new[] { .48f, .72f })
            using (var f = new Fixture(fps))
            {
                f.Place(f.At(angle, radius));
                var before = f.Snapshot();
                // An unfinished previous command must be discarded on the first neutral frame.
                Set(f.Arms, "targetLocal", f.At(-angle, .85f));
                foreach (Vector2 stick in new[] { Vector2.zero, Vector2.right * .09f,
                    Vector2.left * .14f, Vector2.down * .14f, new Vector2(.08f, -.08f) })
                {
                    f.TickSeconds(.5f, stick);
                    f.RequireSnapshot(before, "Dead-zone input chased an old target or recentered: FPS="
                        + fps + ", angle=" + angle + ", stick=" + stick);
                    Require(Vector2.Distance(f.Target, f.Centre) < f.Length * .0001f,
                        "Neutral left a hidden target away from the actual hand centre");
                    f.RequireRaw(stick);
                }
            }
        }
        private static void CheckRadialDirectionAndStops()
        {
            foreach (int fps in FrameRates)
            foreach (float angle in new[] { -45f, 0f, 45f })
            using (var f = new Fixture(fps))
            {
                f.Place(f.At(angle, .62f));
                float radius = f.Centre.magnitude, startAngle = f.Angle;
                f.TickSeconds(.1f, Vector2.up);
                Require(f.Centre.magnitude > radius + f.Length * .005f
                    && Mathf.Abs(Mathf.DeltaAngle(startAngle, f.Angle)) < .05f,
                    "Forward input did not extend along the current arm direction at " + fps + " FPS");
                radius = f.Centre.magnitude;
                f.TickSeconds(.1f, Vector2.down);
                Require(f.Centre.magnitude < radius - f.Length * .005f
                    && Mathf.Abs(Mathf.DeltaAngle(startAngle, f.Angle)) < .05f,
                    "Full rear input turned the arms instead of retracting along the current direction");
                f.RequireStopped("Releasing radial input continued moving");
            }
        }
        private static void CheckProgressiveSpeedAndFrameRates()
        {
            float? reference = null;
            foreach (int fps in FrameRates)
            {
                float previous = 0f;
                foreach (float magnitude in new[] { .2f, .5f, 1f })
                using (var f = new Fixture(fps))
                {
                    f.Place(f.At(0f, .6f));
                    float start = f.Centre.magnitude;
                    f.TickSeconds(.2f, Vector2.up * magnitude);
                    float distance = f.Centre.magnitude - start;
                    Require(distance > previous + f.Length * .0002f,
                        "Larger radial input did not move faster: FPS=" + fps + ", magnitude=" + magnitude);
                    previous = distance; f.RequireRaw(Vector2.up * magnitude);
                    if (magnitude != 1f) continue;
                    float expected = ((float)Call(f.Arms, "MaximumCommonReach") - f.MinimumRadius) * .2f;
                    Require(Mathf.Abs(distance - expected) < f.Length * .002f,
                        "Full extension no longer covers the forward radial range in one second");
                    if (reference.HasValue)
                        Require(Mathf.Abs(distance - reference.Value) < f.Length * .001f,
                            "Radial speed changed with simulation frame rate");
                    else reference = distance;
                }
            }
        }
        private static void CheckTangentialTravelSpeed()
        {
            foreach (int fps in FrameRates)
            foreach (float sign in new[] { -1f, 1f })
            using (var f = new Fixture(fps))
            {
                f.Place(f.At(0f, .6f));
                Set(f.Mover, "turnSpeed", 60f); Set(f.Mover, "overallMotionScale", .88f);
                f.Root.transform.rotation = Quaternion.Euler(0f, 0f, 37f);
                Call(f.Arms, "ConfirmBodyMotion");
                float radius = f.Centre.magnitude;
                f.TickSeconds(.2f, Vector2.left * sign);
                float expected = sign * f.LinearSpeed / radius * Mathf.Rad2Deg * .2f;
                Require(Mathf.Abs(f.Angle - expected) < .15f,
                    "Full-stick lateral motion differs from configured tangential travel speed: FPS=" + fps
                    + ", expected=" + expected + ", actual=" + f.Angle);
                Require(Mathf.Abs(f.Centre.magnitude - radius) < f.Length * .001f,
                    "Pure turning changed extension distance");
                Require(Mathf.Abs(Mathf.DeltaAngle(37f, f.Root.transform.eulerAngles.z)) < .001f && !f.Following,
                    "The body followed before actual hands reached the angular boundary");
                f.RequireStopped("Releasing turning input continued moving");
            }
        }
        private static void CheckTravelDurationControlsBothAxes()
        {
            float productionDuration;
            using (var production = new Fixture(60, false))
                productionDuration = (float)Get(production.Arms, "normalFullTravelDuration");
            foreach (int fps in FrameRates)
            foreach (float duration in new[] { .12f, 1f, 2f, productionDuration })
            using (var radial = new Fixture(fps))
            using (var lateral = new Fixture(fps))
            {
                Set(radial.Arms, "normalFullTravelDuration", duration);
                Set(lateral.Arms, "normalFullTravelDuration", duration);
                radial.Place(radial.At(0f, .6f)); lateral.Place(lateral.At(0f, .6f));
                float radius = radial.Centre.magnitude;
                // Keep even the fastest legal user setting inside the workspace for this measurement.
                float deltaTime = Mathf.Min(1f / fps, duration * .05f);
                radial.TickStep(deltaTime, Vector2.up); lateral.TickStep(deltaTime, Vector2.left);
                float radialDistance = radial.Centre.magnitude - radius;
                float lateralDistance = lateral.Angle * Mathf.Deg2Rad * radius;
                float expected = lateral.LinearSpeed * deltaTime;
                Require(Mathf.Abs(radialDistance - expected) < radial.Length * .002f
                    && Mathf.Abs(lateralDistance - expected) < lateral.Length * .002f,
                    "Full-travel duration did not scale radial and tangential speed equally: FPS=" + fps
                    + ", duration=" + duration + ", radial=" + radialDistance + ", lateral=" + lateralDistance
                    + ", expected=" + expected);
                Require(Mathf.Abs(lateral.Centre.magnitude - radius) < lateral.Length * .001f
                    && !lateral.Following && Mathf.Abs(lateral.Root.transform.eulerAngles.z) < .001f,
                    "Internal lateral-speed measurement moved radially or started body follow");
            }
        }
        private static void CheckLiveTurnSpeedAndResistance()
        {
            using (var f = new Fixture())
            {
                f.Place(f.At(0f, .6f));
                float expectedStep = f.LinearSpeed / f.Centre.magnitude * Mathf.Rad2Deg * .1f;
                foreach (var setting in new[] { (90f, .5f), (600f, 1f), (60f, 0f) })
                {
                    Set(f.Mover, "turnSpeed", setting.Item1); Set(f.Mover, "overallMotionScale", setting.Item2);
                    float angle = f.Angle;
                    f.TickSeconds(.1f, Vector2.left);
                    Require(Mathf.Abs(Mathf.DeltaAngle(angle, f.Angle) - expectedStep) < .1f,
                        "Player turn-speed settings changed lateral arm motion inside its workspace");
                    Require(!f.Following && Mathf.Abs(f.Root.transform.eulerAngles.z) < .001f,
                        "Player speed-isolation fixture reached body follow too early");
                }
            }
            using (var f = new Fixture())
            {
                f.Place(f.At(0f, .6f));
                Set(f.Mover, "turnSpeed", 60f); Set(f.Mover, "overallMotionScale", .88f);
                WorldInteraction item = f.GrabSmall(); Set(item, "grabResistance", .4f);
                float angle = f.Angle;
                float expected = f.LinearSpeed / f.Centre.magnitude * Mathf.Rad2Deg * .6f * .25f;
                f.TickSeconds(.25f, Vector2.left, true, true);
                Require(Mathf.Abs(Mathf.DeltaAngle(angle, f.Angle) - expected) < .15f,
                    "Arm tangential speed failed to apply held-object resistance exactly once");
            }
        }
        private static void CheckDiagonalAndAxisIsolation()
        {
            foreach (int fps in FrameRates)
            using (var f = new Fixture(fps))
            {
                f.Place(f.At(20f, .6f));
                float angle = f.Angle, radius = f.Centre.magnitude;
                Vector2 down = new Vector2(.08f, -Mathf.Sqrt(1f - .08f * .08f));
                f.TickSeconds(.1f, down);
                Require(f.Centre.magnitude < radius && Mathf.Abs(f.Angle - angle) < .05f,
                    "Small horizontal error during full pull introduced turning");
                f.RequireRaw(down); radius = f.Centre.magnitude;
                f.TickSeconds(.1f, Vector2.one.normalized);
                Require(f.Centre.magnitude > radius && f.Angle < angle - .1f,
                    "Diagonal input did not extend and turn together");
                angle = f.Angle; radius = f.Centre.magnitude;
                Vector2 side = new Vector2(-Mathf.Sqrt(1f - .08f * .08f), .08f);
                f.TickSeconds(.1f, side);
                Require(f.Angle > angle + .1f && Mathf.Abs(f.Centre.magnitude - radius) < f.Length * .001f,
                    "Small vertical error during turning introduced radial movement");
                f.RequireRaw(side);
            }
        }
        private static void CheckReachLimits()
        {
            foreach (int fps in FrameRates)
            using (var f = new Fixture(fps))
            {
                f.Place(f.At(0f, .6f)); f.TickSeconds(2f, Vector2.up);
                float outer = f.Centre.magnitude;
                Require(Mathf.Abs(outer - (float)Call(f.Arms, "MaximumCommonReach")) < f.Length * .002f,
                    "Extension failed to reach the legal forward limit");
                f.TickSeconds(1f, Vector2.up);
                Require(Mathf.Abs(f.Centre.magnitude - outer) < f.Length * .0001f,
                    "Holding at the reach limit accumulated movement");
                f.Tick(1, Vector2.down);
                Require(f.Centre.magnitude < outer - f.Length * .0005f,
                    "Reverse input at maximum reach consumed a hidden target first");
                f.TickSeconds(2f, Vector2.down);
                Require(Mathf.Abs(f.Centre.magnitude - f.MinimumRadius) < f.Length * .002f
                    && Mathf.Abs(f.Angle) < .05f, "Full rear input crossed the inner limit or became rear turning");
                f.RequireStopped("Neutral at a radial limit failed to hold");
            }
        }
        private static void CheckBoundaryBodyFollow()
        {
            foreach (int fps in FrameRates)
            foreach (float sign in new[] { -1f, 1f })
            using (var f = new Fixture(fps))
            {
                Set(f.Mover, "turnSpeed", 60f); Set(f.Mover, "overallMotionScale", .88f);
                f.Place(f.At(sign * 70f, .55f));
                float boundary = f.Angle;
                f.TickSeconds(.25f, Vector2.left * sign * .5f);
                Require(!f.Following && Mathf.Abs(Mathf.DeltaAngle(0f, f.Root.transform.eulerAngles.z)) < .001f,
                    "Weak outward input started body follow at the boundary");
                f.TickSeconds(.5f, Vector2.left * sign);
                Require(f.Following && Mathf.Abs(Mathf.DeltaAngle(0f, f.Root.transform.eulerAngles.z)
                    - sign * f.Mover.BaseTurnSpeed * .5f) < .15f,
                    "Sustained outward input failed to turn the body at player maximum speed");
                Require(Mathf.Abs(f.Angle - boundary) < .1f, "Body follow pushed arms beyond their local limit");
                Quaternion heading = f.Root.transform.rotation;
                f.Tick(1, Vector2.right * sign);
                Require(!f.Following && f.Angle * sign < boundary * sign - .1f
                    && Quaternion.Angle(heading, f.Root.transform.rotation) < .001f,
                    "Opposite input failed to immediately move away from the boundary");
                f.RequireStopped("Recentring at a boundary retained follow");
            }
            foreach (int fps in FrameRates)
            foreach (float sign in new[] { -1f, 1f })
            foreach (float duration in new[] { .12f, 1f, 2f })
            using (var f = new Fixture(fps))
            {
                Set(f.Mover, "turnSpeed", 60f); Set(f.Mover, "overallMotionScale", 1f);
                Set(f.Arms, "normalFullTravelDuration", duration);
                float radius = f.At(sign * 70f, .55f).magnitude;
                float angularSpeed = f.LinearSpeed / radius * Mathf.Rad2Deg;
                float gap = Mathf.Min(1f, angularSpeed / fps * .4f);
                f.Place(Direction(sign * (70f - gap)) * radius);
                float remainingTime = 1f / fps - gap / angularSpeed;
                f.Tick(1, Vector2.left * sign);
                float bodyAngle = Mathf.DeltaAngle(0f, f.Root.transform.eulerAngles.z);
                float expected = sign * f.Mover.BaseTurnSpeed * remainingTime;
                Require(Mathf.Abs(f.Angle - sign * 70f) < .1f
                    && Mathf.Abs(bodyAngle - expected) < .05f,
                    "Crossing the boundary did not reserve body speed for the remaining frame time: FPS=" + fps
                    + ", duration=" + duration + ", expected=" + expected + ", actual=" + bodyAngle);
                Require(Mathf.Abs(bodyAngle) <= f.Mover.BaseTurnSpeed / fps + .001f,
                    "Fast lateral arms made boundary body-follow exceed the player maximum turn speed");
            }
        }
        private static void CheckBlockedMotionHasNoBacklog()
        {
            foreach (int fps in FrameRates)
            using (var f = new Fixture(fps))
            {
                f.Place(f.At(0f, .58f));
                Vector2 wallPosition = f.Frame.TransformPoint(f.Centre + Vector2.up * f.Length * .08f);
                WorldInteraction wall = f.Item(wallPosition, new Vector2(f.Length * 4f, f.Length * .02f));
                wall.SetKind(WorldInteractionKind.Collision);
                f.TickSeconds(2f, Vector2.up);
                Require(f.Arms.IsBlocked, "Collision fixture failed to block extension at " + fps + " FPS");
                Vector2 stopped = f.Centre;
                Require(Vector2.Distance(f.Target, stopped) < f.Length * .0001f,
                    "Blocked extension retained an unreachable target beyond the hands");
                wall.enabled = false;
                f.TickSeconds(.25f, Vector2.zero);
                Require(Vector2.Distance(f.Centre, stopped) < f.Length * .0001f,
                    "Removing an obstacle released accumulated blocked movement");
                f.Tick(1, Vector2.down);
                Require(f.Centre.magnitude < stopped.magnitude - f.Length * .0005f,
                    "Reverse input after a collision did not move immediately");
            }
        }
        private static void CheckDeploymentAndSessionReset()
        {
            foreach (int fps in FrameRates)
            using (var f = new Fixture(fps))
            {
                f.TickSeconds(2f, Vector2.zero);
                Require(f.Arms.VisibleDeployment01 > .999f && f.Arms.State == RobotArmState.OuterOperating,
                    "Neutral L3 deployment did not complete the original animation");
                f.RequireStopped("Neutral after deployment did not hold the resulting actual pose");
            }
            foreach (string reset in new[] { "release", "disable", "external", "fall" })
            using (var f = new Fixture())
            {
                f.Place(f.At(70f, .55f)); f.Tick(1, Vector2.left);
                Require(f.Following, "Session fixture did not start body follow");
                if (reset == "release") f.Tick(1, Vector2.left, false);
                else if (reset == "disable") { f.Arms.enabled = false; Call(f.Arms, "OnDisable"); }
                else if (reset == "external")
                {
                    SetProperty(f.Mover, "MovementMode", RobotMovementMode.ExternalTumble); f.Tick(1, Vector2.left);
                }
                else { f.Fall(Vector2.one.normalized); f.Tick(1, Vector2.left); }
                Require(!f.Following, "Normal body-follow command survived " + reset);
                if (reset == "fall") continue;
                if (reset == "disable") f.Arms.enabled = true;
                if (reset == "external") SetProperty(f.Mover, "MovementMode", RobotMovementMode.Driven);
                f.TickSeconds(2f, Vector2.zero);
                f.RequireStopped("A new operation replayed previous input after " + reset);
            }
        }
        private static void CheckGrabMaintainsControlWithoutL3()
        {
            foreach (int fps in FrameRates)
            using (var f = new Fixture(fps))
            {
                f.Place(f.At(20f, .6f));
                WorldInteraction item = f.GrabSmall();
                float[] stopped = f.Snapshot(); Vector3 position = item.WorldPosition;
                f.TickSeconds(.5f, Vector2.zero, false, true);
                f.RequireSnapshot(stopped, "Releasing L3 changed a grab maintained by A at " + fps + " FPS");
                Require(f.Arms.HeldObject == item && item.Owner == f.Arms && f.Arms.IsArmModeActive
                    && f.Mover.IsArmInputCaptured && f.Arms.State == RobotArmState.OuterOperating
                    && Vector3.Distance(position, item.WorldPosition) < f.Length * .0001f,
                    "A failed to retain owned garbage, deployment or input capture after L3 release");

                float radius = f.Centre.magnitude;
                f.TickSeconds(.1f, Vector2.up, false, true); f.RequireRaw(Vector2.up);
                Require(f.Centre.magnitude > radius + f.Length * .005f
                    && Vector3.Distance(position, item.WorldPosition) > f.Length * .005f,
                    "Left-stick extension stopped controlling held garbage without L3");
                float angle = f.Angle;
                f.TickSeconds(.1f, Vector2.left, false, true); f.RequireRaw(Vector2.left);
                Require(f.Angle > angle + .1f && f.Arms.HeldObject == item && item.Owner == f.Arms,
                    "Left-stick lateral control dropped the A-held session after L3 release");
                stopped = f.Snapshot(); position = item.WorldPosition;
                f.TickSeconds(.5f, new Vector2(.08f, -.08f), false, true);
                f.RequireSnapshot(stopped, "A-held neutral input failed to preserve the actual arm pose");
                Require(Vector3.Distance(position, item.WorldPosition) < f.Length * .0001f,
                    "A-held neutral input moved garbage toward the body");
            }
        }
        private static void CheckGrabReleaseKeepsDeployment()
        {
            foreach (int fps in FrameRates)
            foreach (bool l3Held in new[] { false, true })
            using (var f = new Fixture(fps))
            {
                f.Place(f.At(20f, .7f)); WorldInteraction item = f.GrabSmall();
                f.Tick(1, Vector2.zero, l3Held, true);
                Vector3 position = item.WorldPosition; float[] pose = f.Snapshot(); float radius = f.Centre.magnitude;
                f.Tick(1, Vector2.zero, l3Held, false);
                Require(f.Arms.HeldObject == null && item.Owner == null && item.gameObject.activeSelf
                    && f.Arms.State == RobotArmState.OuterOperating && f.Arms.IsArmModeActive
                    && f.Mover.IsArmInputCaptured && !(bool)Get(f.Arms, "grabArmSession")
                    && (bool)Get(f.Arms, "armToggleActive") && f.Mover.GrabMovementMultiplier == 1f,
                    "A release did not retain deployment after dropping outside garbage: FPS=" + fps + ", L3=" + l3Held);
                f.RequireSnapshot(pose, "A release changed the actual arm pose");
                f.TickSeconds(.5f, Vector2.zero, l3Held, false);
                f.RequireSnapshot(pose, "Neutral input moved empty hands after dropping garbage");
                f.TickSeconds(.1f, Vector2.down, l3Held, false);
                Require(f.Arms.State == RobotArmState.OuterOperating && f.Arms.IsArmModeActive
                    && f.Mover.IsArmInputCaptured && f.Arms.HeldObject == null
                    && f.Centre.magnitude < radius - .001f
                    && Vector3.Distance(position, item.WorldPosition) < f.Length * .0001f,
                    "Post-drop normal input did not move only the empty arms");
            }
        }
        private static void CheckGrabSessionCancellation()
        {
            foreach (int fps in FrameRates)
            foreach (string cancellation in new[] { "lost owner", "disabled item", "destroyed item",
                "photo mode", "external control", "disable" })
            using (var f = new Fixture(fps))
            {
                f.Place(f.At(20f, .7f)); WorldInteraction item = f.GrabSmall();
                f.Tick(1, Vector2.zero, false, true);
                Require(f.Arms.HeldObject == item && f.Mover.IsArmInputCaptured,
                    "Cancellation fixture never established A-only control");
                PhotoModeController photo = null;
                if (cancellation == "lost owner") item.Release(f.Arms);
                else if (cancellation == "disabled item") item.enabled = false;
                else if (cancellation == "destroyed item") Object.DestroyImmediate(item.gameObject);
                else if (cancellation == "fall") f.Fall(Vector2.one.normalized);
                else if (cancellation == "photo mode")
                {
                    photo = f.Root.AddComponent<PhotoModeController>();
                    FieldInfo state = typeof(PhotoModeController).GetField("state", Instance);
                    state.SetValue(photo, Enum.Parse(state.FieldType, "Active")); Set(f.Arms, "photoMode", photo);
                }
                else if (cancellation == "external control")
                    SetProperty(f.Mover, "MovementMode", RobotMovementMode.ExternalTumble);
                else { f.Arms.enabled = false; Call(f.Arms, "OnDisable"); }
                f.Tick(1, Vector2.left, false, true);
                Require(f.Arms.HeldObject == null && (item == null || item.Owner == null)
                    && !f.Arms.IsArmModeActive && !f.Mover.IsArmInputCaptured,
                    "Invalid grab retained an A-only control session: " + cancellation + ", FPS=" + fps);

                if (cancellation == "fall") SetProperty(f.Tumble, "State", RobotTumbleState.Upright);
                if (cancellation == "external control") SetProperty(f.Mover, "MovementMode", RobotMovementMode.Driven);
                if (photo != null)
                {
                    FieldInfo state = typeof(PhotoModeController).GetField("state", Instance);
                    state.SetValue(photo, Activator.CreateInstance(state.FieldType));
                }
                if (cancellation == "disable") f.Arms.enabled = true;
                f.TickSeconds(2f, Vector2.left, false, true);
                Require(f.Arms.HeldObject == null && !f.Arms.IsArmModeActive && !f.Mover.IsArmInputCaptured
                    && f.Arms.State == RobotArmState.Retracted,
                    "Holding A resumed a cancelled grab without a new L3 deployment: " + cancellation);
            }
            using (var f = new Fixture())
            {
                f.Place(f.At(20f, .7f));
                WorldInteraction item = f.Item(f.Arms.LeftHandWorld, Vector2.one * .05f);
                Set(item, "onGrabbed", (Action)(() => { f.Arms.enabled = false; Call(f.Arms, "OnDisable"); }));
                f.Tick(1, Vector2.zero, true, true);
                Require(f.Arms.HeldObject == null && item.Owner == null && !f.Arms.IsArmModeActive
                    && !f.Mover.IsArmInputCaptured && f.Arms.State == RobotArmState.Retracted,
                    "A grab callback that disabled the controller still established an A-held session");
                f.Arms.enabled = true;
                f.TickSeconds(2f, Vector2.left, false, true);
                Require(f.Arms.State == RobotArmState.Retracted && f.Arms.HeldObject == null
                    && !f.Mover.IsArmInputCaptured, "Failed grab callback left an A-only session after re-enable");
            }
        }
        private static void CheckEmptyArmRequiresL3()
        {
            foreach (int fps in FrameRates)
            using (var f = new Fixture(fps))
            {
                f.TickSeconds(2f, Vector2.up, false, true);
                Require(f.Arms.State == RobotArmState.Retracted && !f.Arms.IsArmModeActive
                    && !f.Mover.IsArmInputCaptured, "A alone deployed empty arms");
                f.TickSeconds(2f, Vector2.zero, true, true);
                Require(f.Arms.State == RobotArmState.OuterOperating && f.Arms.HeldObject == null,
                    "L3 failed to deploy empty arms while A was already held");
                f.TickSeconds(.1f, Vector2.left, true, false);
                Require(f.Arms.State == RobotArmState.OuterOperating && f.Arms.IsArmModeActive
                    && f.Mover.IsArmInputCaptured && f.Angle > .1f,
                    "Releasing A without a successful grab cancelled the ordinary L3 session");
                f.TickSeconds(2f, Vector2.zero, false, true);
                Require(f.Arms.State == RobotArmState.Retracted && !f.Mover.IsArmInputCaptured,
                    "An unsuccessful held A kept empty arms deployed after L3 release");
            }
        }
        private static void CheckManualDeliveryAndRecycle()
        {
            foreach (int fps in FrameRates)
            using (var f = new Fixture(fps))
            {
                f.Place(f.At(50f, .7f));
                WorldInteraction item = f.GrabSmall(); Vector3 start = item.WorldPosition;
                f.TickSeconds(1f, Vector2.zero, false, true);
                Require(f.Arms.State == RobotArmState.OuterOperating
                    && Vector3.Distance(start, item.WorldPosition) < f.Length * .0001f,
                    "Recentring held garbage still triggered automatic docking");
                f.TickSeconds(.5f, Vector2.left * .09f, false, true);
                Require(f.Arms.State != RobotArmState.Docking
                    && Vector3.Distance(start, item.WorldPosition) < f.Length * .0001f,
                    "Near-centre input still snapped held garbage to the inlet");
                // Actively steer into the existing chest area; the fixture never teleports the prop.
                for (int i = 0; i < fps * 8 && !f.Arms.IsRecycleReady; i++)
                {
                    Vector2 desired = f.Length * (Vector2)Get(f.Arms, "smallDockPosition")
                        - (Vector2)Get(f.Arms, "heldOffset") - (Vector2)Call(f.Arms, "AnchorOffset");
                    float angle = Vector2.SignedAngle(f.Centre, desired);
                    float radial = desired.magnitude - f.Centre.magnitude;
                    Vector2 command = new Vector2(Mathf.Abs(angle) > 1f ? -Mathf.Sign(angle) * .6f : 0f,
                        Mathf.Abs(radial) > f.Length * .005f ? Mathf.Sign(radial) * .6f : 0f);
                    f.Tick(1, command, false, true);
                    Require(f.Arms.State != RobotArmState.Docking, "Manual delivery re-entered magnitude docking");
                }
                Require(f.Arms.IsRecycleReady && f.Arms.HeldObject == item,
                    "Player-controlled motion could not deliver held garbage to the chest area");
                f.Tick(1, Vector2.zero, true, false);
                Require(f.Arms.State == RobotArmState.Recycling && !f.Following,
                    "Releasing A at the actual chest position failed to start recycling");
                for (int i = 0; i < fps * 8 && f.Arms.State == RobotArmState.Recycling; i++)
                    f.Tick(1, Vector2.right, true, false);
                Require(f.Arms.State != RobotArmState.Recycling && f.Arms.HeldObject == null && !f.Following
                    && f.Arms.IsArmModeActive && f.Mover.IsArmInputCaptured
                    && f.Arms.State == RobotArmState.OuterOperating,
                    "Normal input interfered with recycling or completion lost deployment/input capture");
                float[] completedPose = f.Snapshot();
                f.TickSeconds(2f, Vector2.zero, true, false);
                f.RequireSnapshot(completedPose, "Neutral input moved the hands after recycling");
                Require(f.Arms.State == RobotArmState.OuterOperating && f.Arms.IsArmModeActive,
                    "The deployment switch was cleared after successful recycling");
            }
        }
        private static void CheckHeavyAnchorIsolation()
        {
            using (var f = new Fixture())
            {
                var item = f.Item(Vector2.up * 2f, Vector2.one * .1f);
                Set(item, "size", RecyclableSize.Big);
                var pull = item.gameObject.AddComponent<HeavyGarbagePull>();
                Require(item.TryGrab(f.Arms), "Heavy-anchor fixture failed ownership acquisition");
                Set(f.Arms, "heldObject", item); Set(f.Arms, "heldHands", 3);
                Set(pull, "pulling", true); Set(pull, "mover", f.Mover);
                Set(pull, "bodyStart", (Vector2)f.Root.transform.position);
                Set(pull, "gripStart", new Vector2(.2f, .5f)); Set(pull, "pullDirection", Vector2.up);
                Set(pull, "bodyDiameter", 1f);
                foreach (Vector2 stick in new[] { Vector2.zero, Vector2.down, Vector2.left * .09f })
                {
                    f.Tick(1, stick, true, true);
                    Require(Vector2.Distance(f.Target, f.Frame.InverseTransformPoint(pull.GripAnchorWorld)) < .00001f,
                        "Normal incremental control replaced the heavy-garbage grip anchor");
                    f.RequireRaw(stick);
                }
            }
        }
        private static void CheckTumbleIsolation()
        {
            foreach (int fps in FrameRates)
            using (var f = new Fixture(fps))
            {
                Vector2 basis = Vector2.one.normalized; f.Fall(basis); f.TickSeconds(3f, basis);
                Require(f.Arms.IsTumbleArmLayoutActive && (bool)typeof(RobotArmController)
                    .GetProperty("TumbleSocketsAtTarget", Instance).GetValue(f.Arms),
                    "Tumble isolation fixture did not reach its selected exits");
                Vector2 stick = (Vector2)(Quaternion.Euler(0f, 0f, 35f) * basis) * .09f;
                f.TickSeconds(3f, stick);
                Require(Vector2.Angle(f.Target, stick) < .1f && !f.Following,
                    "Normal incremental/dead-zone input changed tumble-specific absolute aiming");
                f.RequireRaw(stick);
                Vector2 rear = Vector2.down * .6f; f.TickSeconds(3f, rear);
                Vector2 expected = Quaternion.Euler(0f, 0f,
                    Mathf.Clamp(Vector2.SignedAngle(basis, rear), -70f, 70f)) * basis;
                Require(Vector2.Angle(f.Target, expected) < .1f && !f.Following,
                    "Normal inward-pull input changed the independent tumble target frame");
                f.RequireRaw(rear);
            }
        }

        private static Vector2 Direction(float angle) => Quaternion.Euler(0f, 0f, angle) * Vector2.up;
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        private static object Get(object target, string name) => target.GetType().GetField(name, Instance).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Instance).SetValue(target, value);
        private static object Call(object target, string name, params object[] args) =>
            target is RobotArmController arms && name == "Step"
                ? RobotArmRegressionChecks.StepForDesiredDeployment(arms, (float)args[0], (Vector2)args[1], (bool)args[2], (bool)args[3])
                : target.GetType().GetMethod(name, Instance).Invoke(target, args);
        private static void SetProperty(object target, string name, object value) =>
            target.GetType().GetProperty(name, Instance).SetValue(target, value);

        private sealed class Fixture : IDisposable
        {
            public Scene Scene { get; }
            public GameObject Root { get; }
            public RobotArmController Arms { get; }
            public RobotMover Mover { get; }
            public RobotTumbleController Tumble { get; }
            public Transform Frame { get; }
            private readonly int fps;
            public float Length => (float)Get(Arms, "armLength");
            public float MinimumRadius => Length * (float)Get(Arms, "staticArmLengthPercent");
            public float LinearSpeed => ((float)Call(Arms, "MaximumCommonReach") - MinimumRadius)
                / (float)Get(Arms, "normalFullTravelDuration");
            public Vector2 Target => (Vector2)Get(Arms, "targetLocal");
            public Vector2 Left => Frame.InverseTransformPoint(Arms.LeftHandWorld);
            public Vector2 Right => Frame.InverseTransformPoint(Arms.RightHandWorld);
            public Vector2 Centre => (Left + Right) * .5f;
            public float Angle => Vector2.SignedAngle(Vector2.up, Centre);
            public bool Following => (bool)Get(Arms, "normalBodyFollowActive");
            public Fixture(int framesPerSecond = 60, bool deterministicRates = true)
            {
                fps = framesPerSecond;
                Scene = EditorSceneManager.NewPreviewScene();
                Root = new GameObject("Normal arm rate-input regression robot");
                SceneManager.MoveGameObjectToScene(Root, Scene);
                Arms = Root.AddComponent<RobotArmController>(); Mover = Root.GetComponent<RobotMover>();
                Tumble = Root.AddComponent<RobotTumbleController>();
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Resources/Robot/RobotMarker.prefab");
                Require(prefab != null, "Production robot prefab is unavailable");
                RobotMarkerView marker = Root.GetComponent<RobotMarkerView>();
                EditorUtility.CopySerialized(prefab.GetComponent<RobotArmController>(), Arms);
                EditorUtility.CopySerialized(prefab.GetComponent<RobotMarkerView>(), marker);
                EditorUtility.CopySerialized(prefab.GetComponent<RobotMover>(), Mover);
                if (deterministicRates)
                {
                    Set(Arms, "normalFullTravelDuration", 1f);
                    Set(Arms, "normalSpeedExponent", 1.5f);
                }
                Frame = new GameObject("Marker Visual Root").transform; Frame.SetParent(Root.transform, false);
                RobotArmRegressionChecks.CreateFixtureArtwork(Frame, true); Set(marker, "markerVisualRoot", Frame);
                Transform body = Frame.Find("Body Visual"); Set(marker, "bodyVisualRoot", body);
                Set(marker, "mediumRecycleBodyBasePosition", body.localPosition);
                Set(marker, "mediumRecycleBodyBaseRotation", body.localRotation);
                Call(Arms, "Awake"); Call(Arms, "EnsureVisuals");
            }
            // Establish an already-deployed legal starting pose independently of the input under test.
            public void Place(Vector2 centre)
            {
                Require((bool)Call(Arms, "CommonTargetReachable", centre), "Unreachable fixture pose: " + centre);
                Set(Arms, "targetLocal", centre);
                Set(Arms, "deploymentTime", (float)typeof(RobotArmController).GetProperty("TotalDeploymentTime", Instance).GetValue(Arms));
                SetProperty(Arms, "State", RobotArmState.OuterOperating); SetProperty(Arms, "IsArmModeActive", true);
                Set(Arms, "armToggleActive", true);
                foreach (string side in new[] { "left", "right" })
                {
                    object arm = Get(Arms, side), pose = Get(arm, "Pose"); float sign = (float)Get(arm, "Side");
                    Vector2 delta = centre + Vector2.right * sign * (float)Get(Arms, "handSpacing") * .5f
                        - (Vector2)Get(arm, "Socket");
                    RobotArmController.SolveIK(delta, (float)Get(arm, "LowerLength"), (float)Get(arm, "UpperLength"),
                        sign, out float lower, out float upper);
                    Set(pose, "LowerAngle", lower); Set(pose, "UpperAngle", upper);
                    Set(pose, "LowerLength", Get(arm, "LowerLength")); Set(pose, "UpperLength", Get(arm, "UpperLength"));
                    Set(pose, "Hand", 1f); Set(pose, "HasSocket", false); Set(arm, "Pose", pose);
                    Call(Arms, "ApplyVisuals", arm);
                }
                Call(Arms, "ConfirmBodyMotion");
                Require(Vector2.Distance(centre, Centre) < Length * .0001f, "Fixture IK missed its starting actual pose");
            }
            public Vector2 At(float angle, float preferredRadius)
            {
                Vector2 preferred = Direction(angle) * Length * preferredRadius;
                if ((bool)Call(Arms, "CommonTargetReachable", preferred)) return preferred;
                Vector2 best = Vector2.zero; float closest = float.PositiveInfinity;
                for (int i = 0; i <= 100; i++)
                {
                    float fraction = Mathf.Lerp((float)Get(Arms, "staticArmLengthPercent"), .98f, i / 100f);
                    Vector2 candidate = Direction(angle) * Length * fraction;
                    float distance = Mathf.Abs(fraction - preferredRadius);
                    if (distance >= closest || !(bool)Call(Arms, "CommonTargetReachable", candidate)) continue;
                    best = candidate; closest = distance;
                }
                Require(!float.IsPositiveInfinity(closest), "No reachable fixture radius at " + angle + " degrees");
                return best;
            }
            public void Tick(int frames, Vector2 stick, bool deploy = true, bool grab = false)
            { for (int i = 0; i < frames; i++) Call(Arms, "Step", 1f / fps, stick, deploy, grab); }
            public void TickStep(float deltaTime, Vector2 stick) => Call(Arms, "Step", deltaTime, stick, true, false);
            public void TickSeconds(float seconds, Vector2 stick, bool deploy = true, bool grab = false) =>
                Tick(Mathf.RoundToInt(seconds * fps), stick, deploy, grab);
            public void RequireRaw(Vector2 stick) => Require(Vector2.Distance(Arms.CurrentTargetLocal, stick) < .000001f
                && Mathf.Abs(Arms.CurrentInputMagnitude - stick.magnitude) < .000001f,
                "Normal rate control changed public raw input direction or magnitude");
            public float[] Snapshot()
            {
                float[] result = new float[15]; int i = 0;
                foreach (string side in new[] { "left", "right" })
                {
                    object pose = Get(Get(Arms, side), "Pose");
                    foreach (string field in new[] { "UpperAngle", "LowerAngle", "UpperLength", "LowerLength", "Hand" })
                        result[i++] = (float)Get(pose, field);
                }
                result[i++] = Left.x; result[i++] = Left.y; result[i++] = Right.x; result[i++] = Right.y;
                result[i] = Root.transform.eulerAngles.z; return result;
            }
            public void RequireSnapshot(float[] before, string reason)
            {
                float[] after = Snapshot();
                for (int i = 0; i < before.Length; i++)
                    Require(Mathf.Abs(before[i] - after[i]) < .0001f, reason + "; pose value=" + i);
                Require(!Following, reason + "; body follow remained active");
            }
            public void RequireStopped(string reason)
            { float[] before = Snapshot(); TickSeconds(.5f, Vector2.zero); RequireSnapshot(before, reason); }
            public void Fall(Vector2 localDirection)
            {
                SetProperty(Tumble, "DirectionWorld", (Vector2)Root.transform.TransformDirection(localDirection));
                SetProperty(Tumble, "State", RobotTumbleState.Fallen);
            }
            public WorldInteraction Item(Vector2 position, Vector2 size)
            {
                var go = new GameObject("Normal input regression item"); SceneManager.MoveGameObjectToScene(go, Scene);
                go.transform.position = position;
                var item = go.AddComponent<WorldInteraction>(); item.SetKind(WorldInteractionKind.Grabbable);
                item.LocalSize = size; Set(item, "requiredHands", 1); Set(item, "grabResistance", 0f); return item;
            }
            public WorldInteraction GrabSmall()
            {
                WorldInteraction item = Item(Arms.LeftHandWorld, Vector2.one * .05f); Set(item, "size", RecyclableSize.Small);
                Tick(1, Vector2.zero, true, true);
                Require(Arms.HeldObject == item, "Fixture failed to grab small garbage at the actual hand"); return item;
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
