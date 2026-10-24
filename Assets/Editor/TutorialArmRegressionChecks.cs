using System;
using System.IO;
using System.Reflection;
using AnimalGame.Tutorial;
using UnityEditor;
using UnityEngine;

namespace AnimalGame.Editor
{
    /// <summary>Measured empty-hand lesson checks. Does not load a scene or alter arm/gameplay settings.</summary>
    public static class TutorialArmRegressionChecks
    {
        private static int checks;
        private const float Dt = 1f / 60f;

        [MenuItem("Animal Game/Validation/Run Tutorial Arm Checks")]
        public static void Run()
        {
            checks = 0;
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run arm lesson checks in Edit Mode.");
            CheckFreshDeployment();
            CheckActualMotionAndDirection();
            CheckInterruptedMotion();
            CheckContinuousHoldAndFreshStow();
            foreach (int fps in new[] { 30, 60, 120 }) CheckAlternateManualStow(fps);
            CheckBadSamples();
            foreach (int fps in new[] { 30, 60, 120 }) CheckFullLesson(fps);
            string output = Path.GetFullPath("output/validation/tutorial-arm");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "arm-regression.txt"), "PASS: " + checks + " measured arm lesson assertions.");
            Debug.Log("TUTORIAL ARM CHECKS PASSED: " + checks + " assertions.");
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static ArmTutorialProgress Fixture() => new ArmTutorialProgress(.2f, 15f, .6f);
        private static Vector2 Pose(float radius = 1f, float degrees = 0f) =>
            Quaternion.Euler(0f, 0f, degrees) * (Vector3)(Vector2.up * radius);

        private static void Feed(ArmTutorialProgress progress, Vector2 centre, Vector2 stick = default,
            float dt = Dt, bool mode = true, float deployment = 1f, bool operating = true,
            bool retracted = false, bool empty = true, bool upright = true, bool valid = true,
            bool toggle = false, bool blocked = false) =>
            progress.Sample(new ArmTutorialSample(centre, stick, deployment, mode, operating,
                retracted, empty, upright, valid, dt, toggle, blocked));

        private static ArmTutorialProgress At(ArmTutorialStep step)
        {
            var progress = Fixture();
            while (progress.Step < step)
            {
                FinishCurrent(progress, 60);
                Require(progress.IsStepSatisfied && progress.Advance(), "Fixture completes each prerequisite with actual eligible samples.");
            }
            return progress;
        }

        private static void FinishCurrent(ArmTutorialProgress progress, int fps)
        {
            float dt = 1f / fps;
            int frames = fps;
            switch (progress.Step)
            {
                case ArmTutorialStep.Deploy:
                    Feed(progress, Pose(), dt: dt, toggle: true);
                    break;
                case ArmTutorialStep.Extend:
                case ArmTutorialStep.RetractReach:
                    bool extend = progress.Step == ArmTutorialStep.Extend;
                    float start = extend ? 1f : 1.4f;
                    Feed(progress, Pose(start), dt: dt);
                    for (int frame = 1; frame <= frames; frame++)
                        Feed(progress, Pose(start + (extend ? .21f : -.21f) * frame / frames),
                            extend ? Vector2.up : Vector2.down, dt: dt);
                    break;
                case ArmTutorialStep.SwingLeft:
                case ArmTutorialStep.SwingRight:
                    bool left = progress.Step == ArmTutorialStep.SwingLeft;
                    Feed(progress, Pose(), dt: dt);
                    for (int frame = 1; frame <= frames; frame++)
                        Feed(progress, Pose(1f, (left ? 16f : -16f) * frame / frames),
                            left ? Vector2.left : Vector2.right, dt: dt);
                    break;
                case ArmTutorialStep.HoldStill:
                    Feed(progress, Pose(), dt: dt);
                    for (int frame = 0; frame < Mathf.CeilToInt(.6f * fps) + 2; frame++) Feed(progress, Pose(), dt: dt);
                    break;
                case ArmTutorialStep.Stow:
                    Feed(progress, Pose(), dt: dt, mode: false, deployment: .8f, operating: false, toggle: true);
                    Feed(progress, Pose(), dt: dt, mode: false, deployment: 0f, operating: false, retracted: true);
                    break;
            }
        }

        private static void CheckFreshDeployment()
        {
            var progress = Fixture();
            Require(progress.Step == ArmTutorialStep.Deploy && !progress.Advance(),
                "The lesson begins directly with deployment and cannot skip the requested action.");
            Feed(progress, Pose());
            Require(progress.NeedsInitialStow && !progress.IsStepSatisfied,
                "Arms already open before this step request initial stow instead of auto-completing.");
            Feed(progress, Pose(), mode: false, deployment: 0f, operating: false, retracted: true, toggle: true);
            Require(!progress.NeedsInitialStow && !progress.IsStepSatisfied, "Closing old arms prepares a genuinely new deployment.");
            Feed(progress, Pose(), deployment: .5f, operating: false, toggle: true);
            Require(!progress.IsStepSatisfied, "The L3 edge alone cannot substitute for the completed deployment animation.");
            Feed(progress, Pose());
            Require(progress.IsStepSatisfied && progress.Advance(), "A fresh tap followed by fully operating arms completes deployment.");
            Feed(progress, Pose(1.4f), Vector2.up, deployment: .6f, operating: false);
            Feed(progress, Pose(1.6f), Vector2.up);
            Require(progress.Value == 0f, "Deployment animation and its hand displacement cannot complete extension.");
        }

        private static void CheckActualMotionAndDirection()
        {
            var extend = At(ArmTutorialStep.Extend);
            Feed(extend, Pose());
            for (int frame = 0; frame < 30; frame++) Feed(extend, Pose(), Vector2.up);
            Require(extend.Value == 0f, "Motionless local hands with live stick input cannot masquerade as arm extension.");
            Feed(extend, Pose(1.01f));
            Require(extend.Value == 0f, "Hand motion without the corresponding live stick command is ignored.");
            Feed(extend, Pose(1.02f), Vector2.down);
            Require(extend.Value == 0f, "Actual outward motion with an inward command is ignored.");
            Feed(extend, Pose(1.03f), Vector2.up, blocked: true);
            Feed(extend, Pose(1.04f), Vector2.up);
            Require(extend.Value == 0f, "Blocked samples break the baseline rather than banking hidden motion.");
            Feed(extend, Pose(1.14f), Vector2.up);
            Require(Near(extend.Value, .1f), "Real outward displacement and matching input earn their measured distance.");
            Feed(extend, Pose(1.09f), Vector2.down);
            Require(Near(extend.Value, .05f), "Returning in the wrong direction removes equivalent radial progress.");

            var retract = At(ArmTutorialStep.RetractReach);
            Feed(retract, Pose(1.4f)); Feed(retract, Pose(1.3f), Vector2.down);
            Require(Near(retract.Value, .1f), "Inward reach is measured separately from closing the arm mode.");
            Feed(retract, Pose(1.35f), Vector2.up);
            Require(Near(retract.Value, .05f), "An outward correction removes equivalent inward progress.");

            foreach (bool left in new[] { true, false })
            {
                var turn = At(left ? ArmTutorialStep.SwingLeft : ArmTutorialStep.SwingRight);
                float start = left ? 170f : -170f;
                float sign = left ? 1f : -1f;
                Vector2 command = left ? Vector2.left : Vector2.right;
                Feed(turn, Pose(1f, start));
                Feed(turn, Pose(1f, start + sign * 5f), -command);
                Require(turn.Value == 0f, "A swing in the requested direction still needs the matching stick direction.");
                Feed(turn, Pose(1f, start + sign * 10f), command);
                Require(Near(turn.Value, 5f), "Real hand rotation earns its signed angle.");
                Feed(turn, Pose(1f, start + sign * 7f), -command);
                Require(Near(turn.Value, 2f), "Wrong-way correction removes its angle rather than rewarding oscillation.");
                for (int frame = 8; frame <= 23; frame++) Feed(turn, Pose(1f, start + sign * frame), command);
                Require(turn.IsStepSatisfied && Near(turn.Value, 15f), "Both directions cross the angle wrap without a false large rotation.");
            }
        }

        private static void CheckInterruptedMotion()
        {
            var progress = At(ArmTutorialStep.Extend);
            Feed(progress, Pose()); Feed(progress, Pose(1.05f), Vector2.up);
            float earned = progress.Value;
            Feed(progress, Pose(.3f), mode: false, deployment: .5f, operating: false);
            Feed(progress, Pose(.1f), mode: false, deployment: 0f, operating: false, retracted: true);
            Require(Near(progress.Value, earned) && progress.Step == ArmTutorialStep.Extend,
                "Accidentally stowing preserves the current lesson and earned movement.");
            Feed(progress, Pose(1.8f), Vector2.up);
            Require(Near(progress.Value, earned), "Re-deployment seeds the new actual pose without counting the animation jump.");
            Feed(progress, Pose(1.85f), Vector2.up);
            Require(Near(progress.Value, earned + .05f), "A new manual movement resumes the preserved lesson normally.");
            earned = progress.Value;
            progress.Suspend(); Feed(progress, Pose(2.2f), Vector2.up);
            Require(Near(progress.Value, earned), "The first post-pause frame is a baseline, not missed movement credit.");
            Feed(progress, Pose(20f), Vector2.up); Feed(progress, Pose(20.05f), Vector2.up);
            Require(Near(progress.Value, earned), "A teleport and its reset frame cannot award hand distance.");
            Feed(progress, Pose(20.1f), Vector2.up);
            Require(Near(progress.Value, earned + .05f), "Ordinary hand movement resumes after an invalid jump.");
        }

        private static void CheckContinuousHoldAndFreshStow()
        {
            var hold = At(ArmTutorialStep.HoldStill);
            Feed(hold, Pose());
            for (int i = 0; i < 20; i++) Feed(hold, Pose());
            Require(hold.Value > .25f && !hold.IsStepSatisfied, "A partial stable hold cannot complete the required continuous interval.");
            Feed(hold, Pose(), Vector2.right);
            Require(hold.Value == 0f, "Active stick input resets the stable hold even when a boundary prevents visible movement.");
            for (int i = 0; i < 20; i++) Feed(hold, Pose());
            Feed(hold, Pose(1.01f));
            Require(hold.Value == 0f, "Unexpected hand movement with a centred stick resets the stable hold.");
            for (int i = 0; i < 20; i++) Feed(hold, Pose(1.01f));
            hold.Suspend();
            Require(hold.Value == 0f, "Paused time cannot bridge two otherwise separate stable intervals.");
            FinishCurrent(hold, 60);
            Require(hold.IsStepSatisfied && hold.Advance(), "A complete eligible stable interval unlocks stowing.");
            Feed(hold, Pose(), mode: false, deployment: 0f, operating: false, retracted: true);
            Require(!hold.IsStepSatisfied, "Automatic stow from another gameplay mode cannot complete the explicit L3 lesson.");
            hold.Suspend();
            Feed(hold, Pose(), mode: false, deployment: 0f, operating: false, retracted: true);
            Require(!hold.IsStepSatisfied, "Resuming after automatic retraction still needs a new manual closing action.");
            Feed(hold, Pose());
            Feed(hold, Pose(), mode: false, deployment: .4f, operating: false, toggle: true);
            Require(!hold.IsStepSatisfied, "A closing command still waits for real full retraction.");
            hold.Suspend();
            Feed(hold, Pose(), mode: false, deployment: .2f, operating: false, valid: false);
            Feed(hold, Pose(), mode: false, deployment: 0f, operating: false, retracted: true);
            Require(hold.IsStepSatisfied && hold.Advance() && hold.IsComplete,
                "A manually requested close survives a paused animation and completes only after actual stowing.");
            Feed(hold, Pose(99f), Vector2.one);
            Require(hold.IsComplete && !hold.Advance() && hold.NormalizedProgress == 1f, "Completion remains stable after unrelated later inputs.");
        }

        private static void CheckAlternateManualStow(int fps)
        {
            MethodInfo inputMethod = typeof(TutorialArmLesson).GetMethod("ManualActionPressed",
                BindingFlags.NonPublic | BindingFlags.Static);
            Require(inputMethod != null, "The lesson exposes its real manual-action input boundary to regression checks.");
            var manualAction = (Func<ArmTutorialStep, bool, bool, bool>)inputMethod.CreateDelegate(
                typeof(Func<ArmTutorialStep, bool, bool, bool>));
            float dt = 1f / fps;
            var deploy = Fixture();
            Feed(deploy, Pose(), dt: dt, toggle: manualAction(deploy.Step, false, true));
            Require(!deploy.IsStepSatisfied, fps + " FPS: B cannot masquerade as the requested fresh L3 deployment.");
            Require(manualAction(ArmTutorialStep.Deploy, true, false),
                fps + " FPS: L3 remains eligible to start a genuine deployment from hidden arms.");

            var stow = At(ArmTutorialStep.Stow);
            Feed(stow, Pose(), dt: dt, mode: false, deployment: 0f, operating: false, retracted: true,
                toggle: manualAction(stow.Step, false, false));
            Require(!stow.IsStepSatisfied, fps + " FPS: B on already hidden arms cannot turn an automatic stow into a manual one.");
            Feed(stow, Pose(), dt: dt);
            Feed(stow, Pose(), dt: dt, mode: false, deployment: .8f, operating: false,
                toggle: manualAction(stow.Step, false, true));
            Require(!stow.IsStepSatisfied, fps + " FPS: B's closing request still waits for the real retraction animation.");
            stow.Suspend();
            Feed(stow, Pose(), dt: dt, mode: false, deployment: .3f, operating: false);
            Require(!stow.IsStepSatisfied, fps + " FPS: a paused B close cannot finish while arms are still visible.");
            Feed(stow, Pose(), dt: dt, mode: false, deployment: 0f, operating: false, retracted: true);
            Require(stow.IsStepSatisfied && stow.Advance() && stow.IsComplete,
                fps + " FPS: B completes the final manual stow once both arms are actually hidden.");

            var instantStow = At(ArmTutorialStep.Stow);
            Feed(instantStow, Pose(), dt: dt, mode: false, deployment: 0f, operating: false, retracted: true,
                toggle: manualAction(instantStow.Step, false, true));
            Require(instantStow.IsStepSatisfied,
                fps + " FPS: an accepted B close can complete Stow even if the arms became fully hidden on its command frame.");
        }

        private static void CheckBadSamples()
        {
            foreach (int gate in new[] { 0, 1, 2, 3, 4, 5 })
            {
                var progress = At(ArmTutorialStep.Extend);
                Feed(progress, Pose());
                Feed(progress, Pose(1.1f), Vector2.up, valid: gate != 0, upright: gate != 1, empty: gate != 2,
                    dt: gate == 3 ? 0f : gate == 4 ? float.NaN : gate == 5 ? 1f : Dt);
                Feed(progress, Pose(1.15f), Vector2.up);
                Require(progress.Value == 0f, "Pause, non-upright, held-object and invalid-time samples cannot bridge movement baselines.");
            }
            var nan = At(ArmTutorialStep.Extend);
            Feed(nan, Pose()); Feed(nan, new Vector2(float.NaN, 1f), Vector2.up); Feed(nan, Pose(1.1f), Vector2.up);
            Require(nan.Value == 0f && float.IsFinite(nan.NormalizedProgress), "Non-finite pose data cannot poison progress.");
            nan.Reset();
            Require(nan.Step == ArmTutorialStep.Deploy && nan.Value == 0f && !nan.IsComplete,
                "Reset starts a new lesson without retaining stale movement or toggle requests.");
        }

        private static void CheckFullLesson(int fps)
        {
            var progress = Fixture();
            foreach (ArmTutorialStep expected in Enum.GetValues(typeof(ArmTutorialStep)))
            {
                Require(progress.Step == expected, fps + " FPS preserves the complete authored step order.");
                if (expected == ArmTutorialStep.Completed) break;
                Require(!progress.Advance(), fps + " FPS cannot advance a fresh unsatisfied step.");
                FinishCurrent(progress, fps);
                Require(progress.IsStepSatisfied && progress.NormalizedProgress == 1f,
                    fps + " FPS reaches the same measured target for " + expected + ".");
                Require(progress.Advance(), fps + " FPS advances exactly one completed step.");
            }
            Require(progress.IsComplete, fps + " FPS finishes all seven arm actions in place.");
        }

        private static bool Near(float actual, float expected) => Mathf.Abs(actual - expected) < .0001f;
        private static void Require(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
