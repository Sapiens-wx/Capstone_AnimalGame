using System;
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
    // Exercise the production controller transaction and artwork in isolated scenes;
    // these checks never sample a gamepad or modify a saved scene/prefab.
    public static class NormalArmInputRegressionChecks
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly int[] FrameRates = { 30, 60, 120 };

        [MenuItem("Animal Game/Validation/Run Normal Arm Input Checks")]
        public static void Run()
        {
            CheckProductionSettings();
            CheckCentreStability();
            CheckProgressiveAimAndReach();
            CheckBodyFollowThresholds();
            CheckBodyFollowSpeedAndRates();
            CheckRearTransitionAndRawReach();
            CheckRearConfirmationAndHysteresis();
            CheckRearShortPushes();
            CheckRearSideLock();
            CheckRearSessionReset();
            CheckFollowSessionReset();
            CheckDockingAndRecycleIsolation();
            CheckHeavyAnchorIsolation();
            CheckTumbleIsolation();
            Debug.Log("Normal arm input checks PASS: 14 groups; production settings, centre stability, progressive "
                + "mirrored aim, unchanged reach/raw input, follow hysteresis and speed, session resets, docking/recycling, "
                + "rear transition, timed confirmation, interrupted intent, locked rear turning, "
                + "heavy grip anchors and tumble isolation; key cases at 30/60/120 FPS.");
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
                Run();
                RobotArmRegressionChecks.Run();
                TumbleArmRegressionChecks.Run();
                MediumRecycleRegressionChecks.Run();
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void CheckProductionSettings()
        {
            using (var f = new Fixture())
            {
                foreach (var expected in new[] {
                    ("normalAimInnerMagnitude", .14f), ("normalAimFullMagnitude", .45f),
                    ("normalFollowEnterMagnitude", .55f), ("normalFollowExitMagnitude", .45f),
                    ("normalFollowFullMagnitude", .85f),
                    ("normalRearEnterMagnitude", .65f), ("normalRearExitMagnitude", .45f),
                    ("normalRearEnterBackward", .35f), ("normalRearExitBackward", .20f),
                    ("normalRearConfirmDuration", .10f), ("normalRearSideMagnitude", .18f),
                    ("normalRearSideSwitchDuration", .08f), ("normalRearBlendDuration", .10f) })
                    Require(Mathf.Abs((float)Get(f.Arms, expected.Item1) - expected.Item2) < .000001f,
                        "Production robot uses an unexpected " + expected.Item1);
            }
        }

        private static void CheckCentreStability()
        {
            foreach (int fps in FrameRates)
            using (var f = new Fixture(fps))
            {
                // Starting with a visible sideways arm also checks returning towards the front.
                f.TickSeconds(2f, Direction(60f));
                Quaternion heading = f.Root.transform.rotation;
                foreach (Vector2 stick in new[] { Vector2.right * .09f, Vector2.left * .09f,
                    Vector2.right * .14f, Vector2.left * .14f, Vector2.down * .14f, Vector2.zero })
                {
                    // Keep the production .37-second smoothing and allow its steady target to converge.
                    f.TickSeconds(4f, stick);
                    Require(Mathf.Abs(f.TargetAngle) < .01f,
                        "Near-centre input retained side aim at " + fps + " FPS: " + stick + "; " + f.TargetAngle);
                    Require(Quaternion.Angle(heading, f.Root.transform.rotation) < .001f && !f.Following,
                        "Near-centre drift or returning the arm to front rotated the body");
                    f.RequireRaw(stick);
                }
            }
        }

        private static void CheckProgressiveAimAndReach()
        {
            foreach (int fps in FrameRates)
            foreach (float sign in new[] { -1f, 1f })
            using (var f = new Fixture(fps))
            {
                Quaternion heading = f.Root.transform.rotation;
                float previousAngle = -1f;
                foreach (float magnitude in new[] { .14f, .2f, .3f, .4f, .45f, .7f, 1f })
                {
                    Vector2 stick = Direction(sign * 60f) * magnitude;
                    // Reach assertions inspect the steady target after production smoothing settles.
                    f.TickSeconds(4f, stick);
                    float angle = f.TargetAngle * sign;
                    Require(angle >= previousAngle - .01f && angle <= 60.01f,
                        "Normal aim is not monotonic and mirrored: magnitude=" + magnitude + ", angle=" + angle);
                    if (magnitude == .3f)
                        Require(angle > 25f && angle < 40f, "Intermediate input did not provide a small, gradual aim angle");
                    if (magnitude >= .45f)
                        Require(Mathf.Abs(angle - 60f) < .01f, "Outer-stick normal aim lost its original angle");
                    previousAngle = angle;
                    float length = (float)Get(f.Arms, "armLength");
                    float expectedReach = Mathf.Lerp(length * (float)Get(f.Arms, "staticArmLengthPercent"),
                        (float)Call(f.Arms, "MaximumCommonReach"),
                        Mathf.InverseLerp((float)Get(f.Arms, "dockEnterMagnitude"),
                            (float)Get(f.Arms, "maximumMagnitude"), magnitude));
                    Require(Mathf.Abs(f.Target.magnitude - expectedReach) < .0001f,
                        "The normal direction response changed raw-magnitude extension reach");
                    f.RequireRaw(stick);
                }
                Require(Quaternion.Angle(heading, f.Root.transform.rotation) < .001f,
                    "Full magnitude inside the existing 70-degree angle limit started body follow");
                // Rear confirmation has its own short delay before the production aim smoothing starts.
                f.TickSeconds(4f, Direction(sign * 130f));
                Require(Mathf.Abs(f.TargetAngle - sign * 70f) < .01f,
                    "Full-stick aiming lost the original 70-degree clamp");
            }
        }

        private static void CheckBodyFollowThresholds()
        {
            foreach (int fps in FrameRates)
            using (var f = new Fixture(fps))
            {
                f.TickSeconds(1f, Vector2.right * .549f);
                Require(!f.Following && Quaternion.Angle(Quaternion.identity, f.Root.transform.rotation) < .001f,
                    "Body follow entered below .55");
                float entered = f.TurnOneFrame(Vector2.right * .55f);
                Require(f.Following && entered > .001f, "Body follow did not enter at exactly .55");
                float last = entered;
                foreach (float magnitude in new[] { .53f, .5f, .46f })
                {
                    float turn = f.TurnOneFrame(Vector2.right * magnitude);
                    Require(f.Following && turn > 0f && turn < last,
                        "Body follow did not remain active and decelerate in the hysteresis band: " + magnitude);
                    last = turn;
                }
                Require(f.TurnOneFrame(Vector2.right * .45f) < .001f && !f.Following,
                    "Body follow did not stop at exactly .45");
                Require(f.TurnOneFrame(Vector2.right * .5f) < .001f && !f.Following,
                    "Body follow restarted within the hysteresis band");
                f.Tick(1, Vector2.right * .6f);
                Require(f.Following, "Follow angle reset fixture failed to enter body follow");
                Require(f.TurnOneFrame(Direction(69f)) < .001f && !f.Following,
                    "Returning inside the original angle limit failed to clear body follow");
                Require(f.TurnOneFrame(Vector2.right * .5f) < .001f && !f.Following,
                    "Angle re-entry reused a previous follow session below .55");
                f.Tick(1, Vector2.right * .6f);
                Require(f.TurnOneFrame(Vector2.zero) < .001f && !f.Following,
                    "Recentring the stick retained body follow");
            }
        }

        private static void CheckBodyFollowSpeedAndRates()
        {
            foreach (int fps in FrameRates)
            foreach (float sign in new[] { -1f, 1f })
            foreach (float magnitude in new[] { .55f, .65f, .85f, 1f })
            using (var f = new Fixture(fps))
            {
                // .65 is the response midpoint, while .55 has the smooth curve's 0.15625 response.
                float response = magnitude == .55f ? .15625f : magnitude == .65f ? .5f : 1f;
                Set(f.Mover, "turnSpeed", 60f);
                Set(f.Mover, "overallMotionScale", .88f);
                f.Root.transform.rotation = Quaternion.Euler(0f, 0f, 37f);
                f.Tick(fps, Vector2.left * sign * magnitude);
                float actual = Mathf.DeltaAngle(37f, f.Root.transform.eulerAngles.z);
                Require(Mathf.Abs(actual - sign * 52.8f * response) < .03f,
                    "Body follow lost its gradual speed, sign, local heading or frame-rate independence: FPS="
                    + fps + ", magnitude=" + magnitude + ", actual=" + actual);
            }
        }

        private static void CheckRearTransitionAndRawReach()
        {
            foreach (int fps in FrameRates)
            using (var f = new Fixture(fps))
            {
                Vector2 accidental = Vector2.down * .6f;
                f.TickSeconds(4f, accidental);
                Require(!f.RearConfirmed && !f.Following && Mathf.Abs(f.TargetAngle) < .01f
                    && Mathf.Abs(Mathf.DeltaAngle(0f, f.Root.transform.eulerAngles.z)) < .001f,
                    "A sustained accidental rear input below .65 aimed sideways or rotated the body");
                f.RequireRaw(accidental);
                Require(Mathf.Abs(f.Target.magnitude - f.ExpectedReach(.6f)) < .0001f,
                    "Suppressing accidental rear direction changed its raw-magnitude extension reach");
            }
            foreach (float sign in new[] { -1f, 1f })
            using (var f = new Fixture())
            {
                float previous = 71f;
                // At .8 magnitude these shallow rear inputs never reach -Y=.35,
                // so this measures the unconfirmed 90..110-degree transition.
                foreach (float angle in new[] { 90f, 95f, 100f, 105f, 110f })
                {
                    Vector2 stick = Direction(sign * angle) * .8f;
                    f.TickSeconds(4f, stick);
                    float aim = f.TargetAngle * sign;
                    Require(!f.RearConfirmed && aim >= -.01f && aim <= previous + .01f,
                        "Unconfirmed rear transition was discontinuous or reversed: angle=" + angle + ", aim=" + aim);
                    if (angle == 90f) Require(Mathf.Abs(aim - 70f) < .01f,
                        "Pure sideways input lost its original full aiming angle");
                    if (angle == 95f) Require(aim > 35f,
                        "A sideways input slightly beyond Y=0 was abruptly treated as rear intent");
                    if (angle == 105f) Require(aim < 35f,
                        "Approaching the deep rear boundary did not reduce side aiming");
                    if (angle == 110f) Require(Mathf.Abs(aim) < .01f && !f.Following,
                        "Unconfirmed deep rear input retained arm direction or body following");
                    previous = aim;
                    f.RequireRaw(stick);
                }
            }
        }

        private static void CheckRearConfirmationAndHysteresis()
        {
            foreach (int fps in FrameRates)
            using (var f = new Fixture(fps))
            {
                // Immediately before .10 seconds, both visual intent and body motion remain suppressed.
                int before = Mathf.CeilToInt(.1f * fps) - 1;
                f.Tick(before, Vector2.down * .65f);
                Require(!f.RearConfirmed && !f.Following
                    && Mathf.Abs(Mathf.DeltaAngle(0f, f.Root.transform.eulerAngles.z)) < .001f,
                    "Deep rear intent confirmed before .10 seconds at " + fps + " FPS");
                f.Tick(2, Vector2.down * .65f);
                Require(f.RearConfirmed,
                    "Sustained rear input at exactly .65 did not confirm within one frame after .10 seconds");
                // Confirmation then blends into the rear direction for .10 seconds.
                f.TickSeconds(.2f, Vector2.down * .65f);
                Require(f.Following, "Confirmed rear intent did not start body following after its blend");
                f.RequireRaw(Vector2.down * .65f);
                float heading = f.Root.transform.eulerAngles.z;
                f.Tick(1, Vector2.down * .5f);
                Require(f.RearConfirmed && f.Following
                    && Mathf.DeltaAngle(heading, f.Root.transform.eulerAngles.z) < 0f,
                    "Confirmed rear intent did not survive the .45..65 magnitude hysteresis band");
                f.Tick(1, Vector2.down * .45f);
                Require(!f.RearConfirmed && !f.Following,
                    "Confirmed rear intent did not exit at exactly .45 magnitude");
                heading = f.Root.transform.eulerAngles.z;
                f.TickSeconds(.5f, Vector2.down * .6f);
                Require(!f.RearConfirmed && !f.Following
                    && Mathf.Abs(Mathf.DeltaAngle(heading, f.Root.transform.eulerAngles.z)) < .001f,
                    "Released rear confirmation restarted below its .65 enter threshold");
                f.TickSeconds(.2f, new Vector2(.6f, -.35f));
                Require(f.RearConfirmed, "The exact -.35 rear component did not permit sustained confirmation");
                f.Tick(1, new Vector2(.6f, -.2f));
                Require(!f.RearConfirmed, "Rear confirmation did not exit at the exact -.20 Y boundary");
                // High overall magnitude alone cannot confirm a shallow rear component.
                f.TickSeconds(.5f, new Vector2(.6f, -.349f));
                Require(!f.RearConfirmed && !f.Following,
                    "Rear confirmation ignored its independent -.35 component threshold");
            }
        }

        private static void CheckRearShortPushes()
        {
            foreach (int fps in FrameRates)
            using (var f = new Fixture(fps))
            {
                int shortFrames = Mathf.Max(1, Mathf.FloorToInt(.075f * fps));
                foreach (Vector2 interruption in new[] { Vector2.zero, Vector2.down * .6f, Vector2.up })
                for (int repeat = 0; repeat < 4; repeat++)
                {
                    f.Tick(shortFrames, Vector2.down);
                    Require(!f.RearConfirmed && !f.Following,
                        "Separated rear pushes accumulated confirmation time across an interruption");
                    f.Tick(1, interruption);
                    Require(!f.RearConfirmed,
                        "A neutral, below-threshold or front input failed to cancel pending rear confirmation");
                }
                Require(Mathf.Abs(Mathf.DeltaAngle(0f, f.Root.transform.eulerAngles.z)) < .001f,
                    "Unconfirmed short rear pushes rotated the body");
                f.TickSeconds(.2f, Vector2.down);
                Require(f.RearConfirmed && f.Following,
                    "Cancelling short pushes prevented a later continuous rear confirmation");
            }
        }

        private static void CheckRearSideLock()
        {
            foreach (int fps in FrameRates)
            foreach (float preferredSign in new[] { -1f, 1f })
            using (var f = new Fixture(fps))
            {
                // Positive signed angles mean left. Clear side input establishes a preferred turn side
                // without starting the magnitude-gated body follow.
                f.Tick(1, Vector2.left * preferredSign * .3f);
                f.TickSeconds(.2f, Vector2.down);
                Require(f.RearConfirmed, "Rear side-lock fixture failed to confirm intentional rear input");
                for (int i = 0; i < fps; i++)
                {
                    float before = f.Root.transform.eulerAngles.z;
                    f.Tick(1, new Vector2(i % 2 == 0 ? -.03f : .03f, -.99f));
                    Require(Mathf.DeltaAngle(before, f.Root.transform.eulerAngles.z) * preferredSign > 0f,
                        "Small lateral noise around straight back reversed the remembered turn side");
                }
                f.TickSeconds(4f, new Vector2(.03f, -.99f));
                Require(f.TargetAngle * preferredSign > 69f,
                    "Rear hand aiming and body following did not share the locked turn side");
                Vector2 opposite = new Vector2(preferredSign * .18f, -.95f);
                for (int i = 0; i < 3; i++)
                {
                    f.Tick(1, opposite);
                    float headingBeforeNoise = f.Root.transform.eulerAngles.z;
                    f.Tick(1, new Vector2(.03f, -.99f));
                    Require(Mathf.DeltaAngle(headingBeforeNoise, f.Root.transform.eulerAngles.z) * preferredSign > 0f,
                        "Separated opposite-side pulses accumulated a rear side-switch decision");
                }
                int beforeSwitch = Mathf.CeilToInt(.08f * fps) - 1;
                for (int i = 0; i < beforeSwitch; i++)
                {
                    float before = f.Root.transform.eulerAngles.z;
                    f.Tick(1, opposite);
                    Require(Mathf.DeltaAngle(before, f.Root.transform.eulerAngles.z) * preferredSign > 0f,
                        "Rear turn side switched before the opposite input persisted for .08 seconds");
                }
                float beforeDecision = f.Root.transform.eulerAngles.z;
                f.Tick(1, opposite);
                Require(Mathf.DeltaAngle(beforeDecision, f.Root.transform.eulerAngles.z) * preferredSign > 0f,
                    "A confirmed rear side decision reversed body motion without blending through zero");
                // The side decision is timed separately from its continuous .10-second visual/control blend.
                f.TickSeconds(.15f, opposite);
                float heading = f.Root.transform.eulerAngles.z;
                f.Tick(1, opposite);
                Require(Mathf.DeltaAngle(heading, f.Root.transform.eulerAngles.z) * preferredSign < 0f,
                    "An explicit opposite .18 lateral input did not switch the rear turn side after .08 seconds");
                f.Tick(1, Vector2.down * .5f);
                Vector2 reverseAgain = new Vector2(-preferredSign * .18f, -.466f);
                f.TickSeconds(.4f, reverseAgain);
                heading = f.Root.transform.eulerAngles.z;
                f.Tick(1, reverseAgain);
                Require(f.RearConfirmed && f.Following
                    && Mathf.DeltaAngle(heading, f.Root.transform.eulerAngles.z) * preferredSign > 0f,
                    "Blending a rear side change through zero cleared follow hysteresis below the .55 restart threshold");
            }
            using (var f = new Fixture())
            {
                f.TickSeconds(.2f, Vector2.down);
                float heading = f.Root.transform.eulerAngles.z;
                f.Tick(1, Vector2.down);
                Require(Mathf.DeltaAngle(heading, f.Root.transform.eulerAngles.z) < 0f,
                    "Straight-back input without a remembered lateral side did not default to a right turn");
            }
            using (var f = new Fixture(120))
            {
                f.Tick(11, Vector2.down);
                Require(!f.RearConfirmed,
                    "Confirmation-frame lateral intent fixture confirmed before .10 seconds");
                float start = f.Root.transform.eulerAngles.z;
                for (int i = 0; i < 24; i++)
                {
                    float before = f.Root.transform.eulerAngles.z;
                    // The first explicit lateral choice arrives on the confirmation frame itself.
                    f.Tick(1, new Vector2(-.2f, -.98f));
                    Require(f.RearConfirmed
                        && Mathf.DeltaAngle(before, f.Root.transform.eulerAngles.z) >= -.0001f,
                        "A first left-side choice on the rear confirmation frame briefly turned right");
                }
                Require(Mathf.DeltaAngle(start, f.Root.transform.eulerAngles.z) > .01f,
                    "A confirmation-frame first lateral choice failed to establish its requested left turn");
            }
        }

        private static void CheckRearSessionReset()
        {
            foreach (string reset in new[] { "release", "disable", "external", "fall" })
            using (var f = new Fixture())
            {
                f.Tick(1, Vector2.left * .3f);
                f.TickSeconds(.2f, Vector2.down);
                Require(f.RearConfirmed, "Rear session reset fixture failed to confirm rear intent");
                if (reset == "release") f.Tick(1, Vector2.down, false);
                else if (reset == "disable") { f.Arms.enabled = false; Call(f.Arms, "OnDisable"); }
                else if (reset == "external")
                {
                    SetProperty(f.Mover, "MovementMode", RobotMovementMode.ExternalTumble);
                    f.Tick(1, Vector2.down);
                }
                else { f.Fall(Vector2.one.normalized); f.Tick(1, Vector2.down); }
                Require(!f.RearConfirmed && !f.Following,
                    "Rear confirmation or follow survived " + reset);
                if (reset == "disable") f.Arms.enabled = true;
                if (reset == "external") SetProperty(f.Mover, "MovementMode", RobotMovementMode.Driven);
                if (reset == "fall") continue; // The tumble checks inspect their own independent direction frame.
                float heading = f.Root.transform.eulerAngles.z;
                f.TickSeconds(.2f, Vector2.down * .6f);
                Require(!f.RearConfirmed && !f.Following
                    && Mathf.Abs(Mathf.DeltaAngle(heading, f.Root.transform.eulerAngles.z)) < .001f,
                    "A new operation inherited confirmed rear intent after " + reset);
                // Side memory also belongs to the old operation: a fresh exact rear confirmation defaults right.
                f.TickSeconds(.2f, Vector2.down);
                heading = f.Root.transform.eulerAngles.z;
                f.Tick(1, Vector2.down);
                Require(Mathf.DeltaAngle(heading, f.Root.transform.eulerAngles.z) < 0f,
                    "A new operation inherited its old rear turning side after " + reset);
            }
        }

        private static void CheckFollowSessionReset()
        {
            foreach (string reset in new[] { "release", "disable", "external", "fall" })
            using (var f = new Fixture())
            {
                f.Tick(1, Vector2.right * .6f);
                Require(f.Following, "Session reset fixture failed to enter body follow");
                if (reset == "release") f.Tick(1, Vector2.right, false);
                else if (reset == "disable") { f.Arms.enabled = false; Call(f.Arms, "OnDisable"); }
                else if (reset == "external")
                {
                    SetProperty(f.Mover, "MovementMode", RobotMovementMode.ExternalTumble);
                    f.Tick(1, Vector2.right);
                }
                else { f.Fall(Vector2.one.normalized); f.Tick(1, Vector2.right); }
                Require(!f.Following, "Body follow survived " + reset);
                if (reset == "disable") f.Arms.enabled = true;
                if (reset == "external") SetProperty(f.Mover, "MovementMode", RobotMovementMode.Driven);
                float turn = f.TurnOneFrame(Vector2.right * .5f);
                Require(turn < .001f && !f.Following,
                    "A new operation reused the previous follow session after " + reset);
            }
        }

        private static void CheckDockingAndRecycleIsolation()
        {
            using (var f = new Fixture())
            {
                WorldInteraction item = f.GrabSmall();
                f.Tick(1, Vector2.right * .6f, true, true);
                Require(f.Following, "Dock reset fixture failed to start outer body follow");
                Quaternion heading = f.Root.transform.rotation;
                f.TickSeconds(3f, Vector2.left * .3f, true, true);
                Require(f.Arms.State == RobotArmState.Docking && f.Arms.IsRecycleReady && !f.Following,
                    "Docking did not clear body follow and reach the fixed inlet");
                Require(!f.RearConfirmed, "Docking retained a pending or confirmed rear operation");
                f.RequireRaw(Vector2.left * .3f);
                Vector2 inlet = (float)Get(f.Arms, "armLength") * (Vector2)Get(f.Arms, "smallDockPosition");
                Vector2 actual = f.Frame.InverseTransformPoint(item.WorldPosition);
                Require(Vector2.Distance(actual, inlet) < .003f
                    && Quaternion.Angle(heading, f.Root.transform.rotation) < .001f,
                    "The normal aim curve moved the docking target or rotated the docking robot");
                f.Tick(1, Vector2.right * .501f, true, true);
                Require(f.Arms.State == RobotArmState.OuterOperating && !f.Following,
                    "Leaving the raw .5 dock boundary started body follow below .55");
                f.TickSeconds(3f, Vector2.left * .3f, true, true);
                f.Tick(1, Vector2.left * .3f, true, false);
                Require(f.Arms.State == RobotArmState.Recycling && !f.Following && !f.RearConfirmed,
                    "Recycling retained manual body follow");
                f.TickSeconds(2f, Vector2.right, true, false);
                // Release once before another operation, then enter below the follow threshold.
                f.Tick(1, Vector2.zero, false);
                Require(f.TurnOneFrame(Vector2.right * .5f) < .001f && !f.Following,
                    "The next operation inherited body follow from docking/recycling");
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
                Set(f.Arms, "heldObject", item);
                Set(f.Arms, "heldHands", 3);
                Set(pull, "pulling", true);
                Set(pull, "mover", f.Mover);
                Set(pull, "bodyStart", (Vector2)f.Root.transform.position);
                Set(pull, "gripStart", new Vector2(.2f, .5f));
                Set(pull, "pullDirection", Vector2.up);
                Set(pull, "bodyDiameter", 1f);
                f.Tick(1, Vector2.left * .09f, true, true);
                Require(Vector2.Distance(f.Target, f.Frame.InverseTransformPoint(pull.GripAnchorWorld)) < .00001f,
                    "Near-centre normal aiming replaced the heavy-garbage grip anchor");
                f.RequireRaw(Vector2.left * .09f);
                f.TickSeconds(.5f, Vector2.down * .6f, true, true);
                Require(Vector2.Distance(f.Target, f.Frame.InverseTransformPoint(pull.GripAnchorWorld)) < .00001f,
                    "Rear intent protection replaced the heavy-garbage grip anchor");
                f.RequireRaw(Vector2.down * .6f);
            }
        }

        private static void CheckTumbleIsolation()
        {
            foreach (int fps in FrameRates)
            using (var f = new Fixture(fps))
            {
                Vector2 basis = Vector2.one.normalized;
                f.Fall(basis);
                f.TickSeconds(3f, basis);
                Require(f.Arms.IsTumbleArmLayoutActive && (bool)typeof(RobotArmController)
                    .GetProperty("TumbleSocketsAtTarget", Instance).GetValue(f.Arms),
                    "Tumble isolation fixture did not reach its selected exits");
                Vector2 stick = (Vector2)(Quaternion.Euler(0f, 0f, 35f) * basis) * .09f;
                f.TickSeconds(3f, stick);
                Require(Vector2.Angle(f.Target, stick) < .1f && !f.Following,
                    "The normal centre response leaked into side-flipped aiming");
                f.RequireRaw(stick);
                Vector2 rear = Vector2.down * .6f;
                f.TickSeconds(4f, rear);
                Vector2 expected = Quaternion.Euler(0f, 0f,
                    Mathf.Clamp(Vector2.SignedAngle(basis, rear), -70f, 70f)) * basis;
                Require(Vector2.Angle(f.Target, expected) < .1f && !f.RearConfirmed && !f.Following,
                    "Normal rear intent protection leaked into the tumble-specific target frame");
                f.RequireRaw(rear);
            }
        }

        private static Vector2 Direction(float angle) => Quaternion.Euler(0f, 0f, angle) * Vector2.up;
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        private static object Get(object target, string name) => target.GetType().GetField(name, Instance).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Instance).SetValue(target, value);
        private static object Call(object target, string name, params object[] args) =>
            target.GetType().GetMethod(name, Instance).Invoke(target, args);
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
            public Vector2 Target => (Vector2)Get(Arms, "targetLocal");
            public float TargetAngle => Vector2.SignedAngle(Vector2.up, Target);
            public bool Following => (bool)Get(Arms, "normalBodyFollowActive");
            public bool RearConfirmed => (bool)Get(Arms, "normalRearIntentConfirmed");

            public Fixture(int framesPerSecond = 60)
            {
                fps = framesPerSecond;
                Scene = EditorSceneManager.NewPreviewScene();
                Root = new GameObject("Normal arm input regression robot");
                SceneManager.MoveGameObjectToScene(Root, Scene);
                Arms = Root.AddComponent<RobotArmController>();
                Mover = Root.GetComponent<RobotMover>();
                Tumble = Root.AddComponent<RobotTumbleController>();
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Resources/Robot/RobotMarker.prefab");
                Require(prefab != null, "Production robot prefab is unavailable");
                RobotMarkerView marker = Root.GetComponent<RobotMarkerView>();
                EditorUtility.CopySerialized(prefab.GetComponent<RobotArmController>(), Arms);
                EditorUtility.CopySerialized(prefab.GetComponent<RobotMarkerView>(), marker);
                EditorUtility.CopySerialized(prefab.GetComponent<RobotMover>(), Mover);
                Frame = new GameObject("Marker Visual Root").transform;
                Frame.SetParent(Root.transform, false);
                RobotArmRegressionChecks.CreateFixtureArtwork(Frame, true);
                Set(marker, "markerVisualRoot", Frame);
                Transform body = Frame.Find("Body Visual");
                Set(marker, "bodyVisualRoot", body);
                Set(marker, "mediumRecycleBodyBasePosition", body.localPosition);
                Set(marker, "mediumRecycleBodyBaseRotation", body.localRotation);
                Call(Arms, "Awake");
                Call(Arms, "EnsureVisuals");
            }

            public void Tick(int frames, Vector2 stick, bool deploy = true, bool grab = false)
            { for (int i = 0; i < frames; i++) Call(Arms, "Step", 1f / fps, stick, deploy, grab); }
            public void TickSeconds(float seconds, Vector2 stick, bool deploy = true, bool grab = false) =>
                Tick(Mathf.RoundToInt(seconds * fps), stick, deploy, grab);
            public float TurnOneFrame(Vector2 stick)
            {
                float heading = Root.transform.eulerAngles.z;
                Tick(1, stick);
                return Mathf.Abs(Mathf.DeltaAngle(heading, Root.transform.eulerAngles.z));
            }
            public void RequireRaw(Vector2 stick) => Require(Arms.CurrentTargetLocal == stick
                && Mathf.Abs(Arms.CurrentInputMagnitude - stick.magnitude) < .000001f,
                "Normal input response changed raw target or magnitude");
            public float ExpectedReach(float magnitude) => Mathf.Lerp(
                (float)Get(Arms, "armLength") * (float)Get(Arms, "staticArmLengthPercent"),
                (float)Call(Arms, "MaximumCommonReach"),
                Mathf.InverseLerp((float)Get(Arms, "dockEnterMagnitude"),
                    (float)Get(Arms, "maximumMagnitude"), magnitude));
            public void Fall(Vector2 localDirection)
            {
                SetProperty(Tumble, "DirectionWorld", (Vector2)Root.transform.TransformDirection(localDirection));
                SetProperty(Tumble, "State", RobotTumbleState.Fallen);
            }
            public WorldInteraction Item(Vector2 position, Vector2 size)
            {
                var go = new GameObject("Normal input regression item");
                SceneManager.MoveGameObjectToScene(go, Scene);
                go.transform.position = position;
                var item = go.AddComponent<WorldInteraction>();
                item.SetKind(WorldInteractionKind.Grabbable);
                item.LocalSize = size;
                Set(item, "requiredHands", 1);
                Set(item, "grabResistance", 0f);
                return item;
            }
            public WorldInteraction GrabSmall()
            {
                TickSeconds(3f, Vector2.up, true, true);
                WorldInteraction item = Item(Arms.LeftHandWorld, Vector2.one * .05f);
                Set(item, "size", RecyclableSize.Small);
                Tick(1, Vector2.up, true, true);
                Require(Arms.HeldObject == item, "Normal-input docking fixture failed to grab small garbage");
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
