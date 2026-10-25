using System;
using System.IO;
using AnimalGame.Tutorial;
using UnityEditor;
using UnityEngine;

namespace AnimalGame.Editor
{
    /// <summary>Outcome-based balance lesson checks; no robot or physics values are written.</summary>
    public static class TutorialBalanceRegressionChecks
    {
        private static int checks;
        private const float Dt = 1f / 30f;

        [MenuItem("Animal Game/Validation/Run Tutorial Balance Checks")]
        public static void Run()
        {
            checks = 0;
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run balance lesson checks in Edit Mode.");
            CheckManualContinueGates();
            CheckReadVisibilityAndPause();
            CheckFreshNeutralAndOldHeldInput();
            CheckActualManualAndPointMovement();
            CheckNeutralBaselineFollowsAutomaticDrift();
            CheckSmoothingAndSingleDirections();
            CheckSuspendDisarmsAnAttempt();
            CheckMalformedSamplesAndSettings();
            CheckReset();
            foreach (int fps in new[] { 30, 60, 120 }) CheckFullLesson(fps);
            string requested = Environment.GetEnvironmentVariable("TUTORIAL_BALANCE_VALIDATION_OUTPUT");
            string output = string.IsNullOrWhiteSpace(requested)
                ? Path.GetFullPath("output/validation/tutorial-balance") : Path.GetFullPath(requested);
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "balance-regression.txt"), "PASS: " + checks + " balance lesson assertions.");
            Debug.Log("TUTORIAL BALANCE CHECKS PASSED: " + checks + " assertions.");
        }
        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void Feed(BalanceTutorialProgress progress, float dt = Dt, bool valid = true, bool expanded = true,
            Vector2 raw = default, Vector2 manual = default, Vector2 point = default)
        {
            progress.Sample(new BalanceTutorialSample(dt, valid, expanded, raw, manual, point));
            if (progress.CanContinue && !progress.IsComplete) progress.Continue();
        }

        private static void CheckManualContinueGates()
        {
            var progress = new BalanceTutorialProgress(.1f, .1f, .05f);
            Require(!progress.CanContinue && !progress.Continue(), "A cannot skip an unread indicator instruction.");
            progress.Sample(new BalanceTutorialSample(.1f, true, true, Vector2.zero, Vector2.zero, Vector2.zero));
            Require(progress.Step == BalanceTutorialStep.ReadIndicator && progress.CanContinue,
                "Finished indicator reading stays on its own card until A.");
            progress.Suspend();
            Require(progress.CanContinue && progress.Continue() && progress.Step == BalanceTutorialStep.AdjustWeight,
                "Pausing retains completed reading, and one confirmation starts adjustment.");
            Require(!progress.Continue(), "Another A cannot skip the actual balance adjustment.");
            progress.Sample(new BalanceTutorialSample(.1f, true, true, Vector2.zero, Vector2.zero, Vector2.zero));
            progress.Sample(new BalanceTutorialSample(.1f, true, true, Vector2.right, Vector2.right * .08f, Vector2.right * .07f));
            Require(progress.Step == BalanceTutorialStep.AdjustWeight && progress.CanContinue,
                "An effective native adjustment waits for A on the adjustment card.");
            progress.Suspend();
            Require(progress.CanContinue && progress.Continue() && progress.Step == BalanceTutorialStep.ReadRisk,
                "Completed manual movement is retained through pause and confirms exactly one step.");
            progress.Sample(new BalanceTutorialSample(.1f, true, true, Vector2.zero, Vector2.zero, Vector2.zero));
            Require(progress.Step == BalanceTutorialStep.ReadRisk && progress.CanContinue && !progress.IsComplete,
                "Finished rollover-risk reading waits for A rather than completing automatically.");
            Require(progress.Continue() && progress.IsComplete && !progress.Continue(),
                "The terminal completion card cannot advance without its separate controller confirmation.");
        }
        private static void Read(BalanceTutorialProgress progress, float seconds, int fps = 30, bool expanded = true, bool valid = true)
        {
            for (int frame = 0; frame < Mathf.CeilToInt(seconds * fps); frame++)
                Feed(progress, 1f / fps, valid, expanded);
        }
        private static BalanceTutorialProgress AtAdjust()
        {
            var progress = new BalanceTutorialProgress(); Read(progress, 3f);
            Require(progress.Step == BalanceTutorialStep.AdjustWeight && progress.NeedsNeutral,
                "Reading the indicator enters adjustment and requires a fresh neutral sample.");
            return progress;
        }
        private static void EffectivePush(BalanceTutorialProgress progress, Vector2 direction)
        {
            Feed(progress);
            Feed(progress, raw: direction.normalized * .7f, manual: direction.normalized * .08f,
                point: direction.normalized * .07f);
        }

        private static void CheckReadVisibilityAndPause()
        {
            var progress = new BalanceTutorialProgress();
            Require(progress.Step == BalanceTutorialStep.ReadIndicator && !progress.IsComplete,
                "The lesson starts by reading the existing indicator.");
            Read(progress, 1f);
            float before = progress.ReadingSeconds;
            Read(progress, 5f, expanded: false); Read(progress, 5f, valid: false); progress.Suspend();
            Require(progress.Step == BalanceTutorialStep.ReadIndicator && Mathf.Abs(progress.ReadingSeconds - before) < .00001f,
                "Collapsed and paused instructions retain read time without consuming additional seconds.");
            Read(progress, 2f);
            Require(progress.Step == BalanceTutorialStep.AdjustWeight && progress.ReadingSeconds == 0f,
                "Only three visible valid seconds finish the indicator instructions.");
            EffectivePush(progress, Vector2.right);
            Require(progress.Step == BalanceTutorialStep.ReadRisk, "One effective push enters the risk explanation.");
            Read(progress, 1f); before = progress.ReadingSeconds;
            Read(progress, 4f, expanded: false); Read(progress, 4f, valid: false);
            Require(progress.Step == BalanceTutorialStep.ReadRisk && Mathf.Abs(progress.ReadingSeconds - before) < .00001f,
                "Risk reading also freezes while hidden or paused.");
            Read(progress, 2f);
            Require(progress.IsComplete && progress.NormalizedProgress == 1f,
                "The risk explanation completes after its remaining visible reading time.");
        }

        private static void CheckFreshNeutralAndOldHeldInput()
        {
            var progress = new BalanceTutorialProgress();
            for (int frame = 0; frame < 91; frame++)
                Feed(progress, raw: Vector2.right, manual: Vector2.right * .4f, point: Vector2.right * .4f);
            Require(progress.Step == BalanceTutorialStep.AdjustWeight && progress.NeedsNeutral,
                "A stick held throughout indicator reading cannot become a fresh adjustment at the boundary.");
            for (int frame = 0; frame < 20; frame++)
                Feed(progress, raw: Vector2.right, manual: Vector2.right * .6f, point: Vector2.right * .8f);
            Require(progress.Step == BalanceTutorialStep.AdjustWeight && progress.NeedsNeutral && progress.Value == 0f,
                "Even a large old held-input movement remains unarmed until neutral is observed.");
            Feed(progress, raw: Vector2.right * .2f, manual: Vector2.right * .3f, point: Vector2.right * .4f);
            Require(!progress.NeedsNeutral, "The authored neutral boundary at magnitude .2 arms the next movement.");
            Feed(progress, raw: Vector2.right * .7f, manual: Vector2.right * .38f, point: Vector2.right * .47f);
            Require(progress.Step == BalanceTutorialStep.ReadRisk,
                "A genuinely new push after neutral succeeds relative to its actual non-zero baseline.");
        }

        private static void CheckActualManualAndPointMovement()
        {
            var progress = AtAdjust(); Feed(progress);
            Feed(progress, raw: Vector2.right, point: Vector2.right * .8f);
            Require(progress.Step == BalanceTutorialStep.AdjustWeight && progress.Value == 0f,
                "Terrain or spring point movement without real manual counterweight movement never earns credit.");
            Feed(progress, raw: Vector2.right, manual: Vector2.right * .039f, point: Vector2.right * .3f);
            Require(progress.Step == BalanceTutorialStep.AdjustWeight,
                "Sub-threshold manual noise cannot count despite large automatic point motion.");
            Feed(progress, raw: Vector2.right, manual: Vector2.right * .1f, point: Vector2.right * .049f);
            Require(progress.Step == BalanceTutorialStep.AdjustWeight && progress.Value < progress.Target,
                "Actual manual movement alone is insufficient until the displayed point moves far enough.");
            Feed(progress, raw: Vector2.left, manual: Vector2.right * .1f, point: Vector2.right * .1f);
            Require(progress.Step == BalanceTutorialStep.AdjustWeight,
                "A manual response in the opposite direction to current input is not a new effective action.");
            Feed(progress, raw: Vector2.right, manual: Vector2.right * .1f, point: Vector2.left * .1f);
            Require(progress.Step == BalanceTutorialStep.AdjustWeight,
                "An automatically drifting point opposite the manual adjustment is not attributed to the player.");
            Feed(progress, raw: Vector2.right, manual: Vector2.right * .1f, point: Vector2.up * .1f);
            Require(progress.Step == BalanceTutorialStep.AdjustWeight,
                "A perpendicular automatic point drift does not demonstrate the same-direction effect.");
            Feed(progress, raw: Vector2.right * .249f, manual: Vector2.right * .1f, point: Vector2.right * .1f);
            Require(progress.Step == BalanceTutorialStep.AdjustWeight,
                "The gap above neutral but below deliberate input does not award adjustment credit.");
            Feed(progress, raw: Vector2.right * .25f, manual: Vector2.right * .04f, point: Vector2.right * .05f);
            Require(progress.Step == BalanceTutorialStep.ReadRisk,
                "One deliberate input with the actual threshold manual and point displacement completes adjustment.");
        }

        private static void CheckNeutralBaselineFollowsAutomaticDrift()
        {
            var progress = AtAdjust(); Feed(progress);
            Feed(progress, point: Vector2.right * .5f);
            Feed(progress, raw: Vector2.right * .7f, manual: Vector2.right * .05f, point: Vector2.right * .52f);
            Require(progress.Step == BalanceTutorialStep.AdjustWeight,
                "Automatic drift during neutral becomes the baseline, rather than future manual movement credit.");
            Feed(progress, raw: Vector2.right * .7f, manual: Vector2.right * .08f, point: Vector2.right * .56f);
            Require(progress.Step == BalanceTutorialStep.ReadRisk,
                "Only new effective point travel beyond the neutral baseline completes the action.");

            progress = AtAdjust();
            Feed(progress, manual: Vector2.left * .1f, point: Vector2.left * .1f);
            Feed(progress, manual: Vector2.left * .04f, point: Vector2.left * .02f);
            Feed(progress, raw: Vector2.right * .7f, manual: Vector2.zero, point: Vector2.right * .01f);
            Require(progress.Step == BalanceTutorialStep.AdjustWeight,
                "A previous adjustment returning to centre during neutral cannot contribute old spring travel.");
        }

        private static void CheckSmoothingAndSingleDirections()
        {
            var progress = AtAdjust(); Feed(progress);
            Feed(progress, raw: Vector2.right * .7f, manual: Vector2.right * .02f, point: Vector2.zero);
            Feed(progress, raw: Vector2.right * .7f, manual: Vector2.right * .06f, point: Vector2.right * .02f);
            Feed(progress, raw: Vector2.right * .7f, manual: Vector2.right * .1f, point: Vector2.right * .04f);
            Require(progress.Step == BalanceTutorialStep.AdjustWeight,
                "Smoothing is tolerated while the visible native point catches up to an already effective manual shift.");
            Feed(progress, raw: Vector2.right * .7f, manual: Vector2.right * .1f, point: Vector2.right * .06f);
            Require(progress.Step == BalanceTutorialStep.ReadRisk,
                "Continuously held deliberate input can finish when the real spring response arrives.");
            foreach (Vector2 direction in new[] { Vector2.left, Vector2.right, Vector2.up, Vector2.down, new Vector2(1f, 1f) })
            {
                progress = AtAdjust(); EffectivePush(progress, direction);
                Require(progress.Step == BalanceTutorialStep.ReadRisk,
                    "A single effective direction is sufficient without requiring recentering, two directions or a dangerous flip.");
            }
        }

        private static void CheckSuspendDisarmsAnAttempt()
        {
            foreach (bool collapse in new[] { false, true })
            {
                var progress = AtAdjust(); Feed(progress);
                Feed(progress, raw: Vector2.right, manual: Vector2.right * .08f, point: Vector2.right * .02f);
                Feed(progress, valid: collapse, expanded: !collapse, raw: Vector2.right,
                    manual: Vector2.right * .12f, point: Vector2.right * .12f);
                Require(progress.Step == BalanceTutorialStep.AdjustWeight && progress.NeedsNeutral && progress.Value == 0f,
                    "Pause or collapse discards a pending manual movement instead of crediting invisible spring travel.");
                Feed(progress, raw: Vector2.right, manual: Vector2.right * .2f, point: Vector2.right * .2f);
                Require(progress.Step == BalanceTutorialStep.AdjustWeight && progress.NeedsNeutral,
                    "Held input after resume requires a fresh neutral boundary.");
                Feed(progress, manual: Vector2.right * .05f, point: Vector2.right * .07f);
                Feed(progress, raw: Vector2.right, manual: Vector2.right * .13f, point: Vector2.right * .14f);
                Require(progress.Step == BalanceTutorialStep.ReadRisk,
                    "A resumed new push can complete normally after neutral without losing previously completed reading.");
            }
        }

        private static void CheckMalformedSamplesAndSettings()
        {
            var progress = new BalanceTutorialProgress(float.NaN, float.PositiveInfinity, float.NaN);
            Read(progress, 1f); float before = progress.Value;
            foreach (float dt in new[] { float.NaN, float.PositiveInfinity, 0f, -.1f, .251f, 20f }) Feed(progress, dt);
            Feed(progress, raw: new Vector2(float.NaN, 0f));
            Feed(progress, manual: new Vector2(0f, float.PositiveInfinity));
            Feed(progress, point: new Vector2(float.NegativeInfinity, 0f));
            Require(progress.Step == BalanceTutorialStep.ReadIndicator && Mathf.Abs(progress.Value - before) < .00001f,
                "Non-finite vectors, zero/negative delta and a stalled frame never fabricate reading time.");
            Read(progress, 2f);
            Require(progress.Step == BalanceTutorialStep.AdjustWeight && Mathf.Abs(progress.Target - .05f) < .00001f,
                "Malformed Inspector timings and movement targets fall back to the finite authored defaults.");
            Feed(progress);
            Feed(progress, dt: .5f, raw: Vector2.right, manual: Vector2.right, point: Vector2.right);
            Require(progress.Step == BalanceTutorialStep.AdjustWeight && progress.NeedsNeutral,
                "A stalled-frame manual jump invalidates arming rather than counting an effective movement.");
        }

        private static void CheckReset()
        {
            var progress = AtAdjust(); EffectivePush(progress, Vector2.right); Read(progress, 3f);
            Require(progress.IsComplete, "Fixture finishes without recentering or any tumble event.");
            Feed(progress, valid: false);
            Require(progress.IsComplete && progress.Value == 1f, "Later invalid observations do not undo a completed lesson.");
            progress.Reset();
            Require(progress.Step == BalanceTutorialStep.ReadIndicator && progress.ReadingSeconds == 0f
                && progress.Value == 0f && !progress.IsComplete,
                "Restart clears reading, movement baselines and completion state.");
        }

        private static void CheckFullLesson(int fps)
        {
            var progress = new BalanceTutorialProgress();
            Read(progress, 3f, fps);
            Require(progress.Step == BalanceTutorialStep.AdjustWeight, fps + " FPS: three visible indicator seconds finish consistently.");
            Feed(progress, 1f / fps);
            for (int frame = 1; frame <= fps && progress.Step == BalanceTutorialStep.AdjustWeight; frame++)
            {
                float t = frame / (float)fps;
                Feed(progress, 1f / fps, raw: Vector2.right * .7f, manual: Vector2.right * (.15f * t),
                    point: Vector2.right * (.1f * t));
            }
            Require(progress.Step == BalanceTutorialStep.ReadRisk, fps + " FPS: actual smoothed manual point movement is recognized.");
            Read(progress, 3f, fps);
            Require(progress.IsComplete && progress.NormalizedProgress == 1f,
                fps + " FPS: normal visible risk reading completes the three-step lesson.");
        }

        private static void Require(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException("Balance tutorial check failed: " + message);
        }
    }
}
