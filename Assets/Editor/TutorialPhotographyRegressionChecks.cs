using System;
using System.IO;
using System.Reflection;
using AnimalGame.Animals;
using AnimalGame.Discovery;
using AnimalGame.MapTest;
using AnimalGame.RobotArm;
using AnimalGame.RobotMap;
using AnimalGame.Tutorial;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AnimalGame.Editor
{
    /// <summary>Native subject, capture and save evidence is retained until each explicit confirmation.</summary>
    public static class TutorialPhotographyRegressionChecks
    {
        private static int checks;
        private const float Dt = 1f / 30f;

        [MenuItem("Animal Game/Validation/Run Tutorial Photography Checks")]
        public static void Run()
        {
            checks = 0;
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run photography checks in Edit Mode.");
            CheckManualObservationAndFraming();
            foreach (int fps in new[] { 30, 60, 120 }) CheckFullLesson(fps);
            CheckEarlyCaptureAndDiscard();
            CheckOldResultsAndReset();
            CheckArmIndependentLessonAvailability();
            string output = Path.GetFullPath("output/validation/tutorial-photography");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "photography-regression.txt"), "PASS: " + checks + " photo assertions.");
            Debug.Log("TUTORIAL PHOTOGRAPHY CHECKS PASSED: " + checks + " assertions.");
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void Feed(PhotographyTutorialProgress progress, float dt = Dt, bool valid = true,
            bool expanded = true, bool photo = false, bool result = false, bool visible = true,
            bool seen = true, bool framed = false, float moved = 0f) =>
            progress.Sample(new PhotographyTutorialSample(dt, valid, expanded, photo, result, visible, seen, framed, moved));

        private static void Observe(PhotographyTutorialProgress progress, float seconds, int fps = 30,
            bool photo = false, bool framed = false, bool expanded = true)
        {
            for (int frame = 0; frame < Mathf.CeilToInt(seconds * fps); frame++)
                Feed(progress, 1f / fps, expanded: expanded, photo: photo, framed: framed);
        }

        private static void CheckManualObservationAndFraming()
        {
            var progress = new PhotographyTutorialProgress(requiredMovementMeters: 0f);
            Require(!progress.CanContinue && !progress.Continue(), "A cannot skip unseen bird observation.");
            Observe(progress, 2f, expanded: false);
            Require(!progress.CanContinue, "Collapsed text does not consume bird-observation reading time.");
            Feed(progress, dt: float.NaN); Feed(progress, dt: 3f); Feed(progress, valid: false);
            Require(!progress.CanContinue && float.IsFinite(progress.Value), "Invalid samples cannot complete an observation.");
            Observe(progress, .8f);
            Require(progress.Step == PhotographyTutorialStep.ObserveMovement && progress.CanContinue,
                "The stationary seen pecking bird completes its goal but waits for A.");
            Require(progress.Continue() && progress.Step == PhotographyTutorialStep.EnterPhoto && !progress.CanContinue,
                "A reveals the next camera action without completing it.");
            Feed(progress, photo: true);
            Require(progress.Step == PhotographyTutorialStep.EnterPhoto && progress.CanContinue,
                "Entering native photo mode waits for its own A confirmation.");
            Require(progress.Continue() && progress.Step == PhotographyTutorialStep.FrameSubject, "A proceeds to framing.");
            Observe(progress, 1f, photo: true);
            Require(!progress.CanContinue && !progress.Continue(), "Camera mode alone does not establish actual target framing.");
            Observe(progress, .5f, photo: true, framed: true);
            Require(progress.CanContinue && progress.Step == PhotographyTutorialStep.FrameSubject,
                "Real framing completes the action without automatically advancing.");
            Require(progress.Continue() && progress.Step == PhotographyTutorialStep.Capture, "A proceeds to actual capture.");
            Observe(progress, 2f, photo: true, framed: true);
            Require(!progress.CanContinue && !progress.RegisterValidPhoto(1, false),
                "Held time and wrong-subject results cannot substitute for an accepted subject photograph.");
        }

        private static void CheckFullLesson(int fps)
        {
            var progress = new PhotographyTutorialProgress(requiredMovementMeters: 0f);
            Observe(progress, .8f, fps);
            Require(progress.Step == PhotographyTutorialStep.ObserveMovement && progress.Continue(), fps + " FPS: observation needs A.");
            Feed(progress, 1f / fps, photo: true);
            Require(progress.Continue() && progress.Step == PhotographyTutorialStep.FrameSubject, "Native camera entry is confirmed.");
            Observe(progress, .5f, fps, photo: true, framed: true);
            Require(progress.Continue() && progress.Step == PhotographyTutorialStep.Capture, "Native target framing is confirmed.");
            Require(progress.RegisterValidPhoto(fps, true) && progress.Step == PhotographyTutorialStep.Capture,
                "The exact new target result is accepted while the capture instruction stays selected.");
            Feed(progress, photo: true, result: true);
            Require(!progress.CanContinue && !progress.Continue(), "A on native results cannot skip a tutorial instruction.");
            Require(progress.RegisterPhotoSaved(fps, true) && progress.HasSavedPhoto
                && progress.Step == PhotographyTutorialStep.Capture, "Native Y save is latched without automatic progression.");
            Feed(progress, photo: true, result: false);
            Require(progress.Continue() && progress.Step == PhotographyTutorialStep.SavePhoto,
                "After closing results A confirms capture, revealing the save explanation.");
            Require(progress.CanContinue && progress.Continue() && progress.Step == PhotographyTutorialStep.ExitPhoto,
                "The earlier genuine save is retained for its own A confirmation.");
            Require(!progress.CanContinue && !progress.Continue(), "Exit still requires the actual camera to close.");
            Feed(progress, photo: false);
            Require(progress.Step == PhotographyTutorialStep.ExitPhoto && progress.CanContinue,
                "Camera exit waits for the player's final action confirmation.");
            Require(progress.Continue() && progress.IsComplete && progress.CanContinue,
                "A reaches the completion card; its chapter confirmation belongs to the controller.");
            Require(!progress.Continue() && !progress.RegisterValidPhoto(fps + 1, true), "The finished model cannot restart itself.");
        }

        private static void CheckEarlyCaptureAndDiscard()
        {
            var progress = new PhotographyTutorialProgress(requiredMovementMeters: 0f);
            Require(progress.RegisterValidPhoto(10, true) && progress.Step == PhotographyTutorialStep.ObserveMovement,
                "An early valid photo does not jump past unconfirmed instructions.");
            Feed(progress, photo: true, result: true);
            Require(!progress.Continue(), "Result-screen A cannot acknowledge any early-photo cards.");
            Require(progress.RegisterResultClosed(10, true) && progress.ActiveResultId == 10,
                "Closing an unsaved result retains its snapshot identity after the teaching bird departs.");
            Feed(progress, photo: false, visible: false, seen: false, framed: false);
            foreach (PhotographyTutorialStep expected in new[] { PhotographyTutorialStep.EnterPhoto,
                PhotographyTutorialStep.FrameSubject, PhotographyTutorialStep.Capture, PhotographyTutorialStep.SavePhoto })
                Require(progress.Continue() && progress.Step == expected,
                    "Every earlier camera accomplishment remains available for one explicit confirmation: " + expected);
            Require(!progress.CanContinue && !progress.RegisterPhotoSaved(9, true),
                "A closed unsaved capture still needs an explicit matching album save.");
            Require(progress.RegisterPhotoSaved(10, true) && progress.Continue(),
                "A tutorial recovery save can retain the original native photo without respawning its subject.");
            Require(progress.Step == PhotographyTutorialStep.ExitPhoto && progress.Continue() && progress.IsComplete,
                "An already closed camera is retained until its exit explanation is acknowledged.");
        }

        private static void CheckOldResultsAndReset()
        {
            var progress = new PhotographyTutorialProgress(float.NaN, float.PositiveInfinity, float.NaN, 50);
            Require(!progress.RegisterValidPhoto(49, true) && !progress.RegisterValidPhoto(50, true)
                && !progress.RegisterValidPhoto(0, true), "Pre-chapter result IDs cannot become tutorial captures.");
            Require(!progress.RegisterPhotoSaved(51, true), "Saving without a matching real capture is insufficient.");
            Require(progress.RegisterValidPhoto(53, true) && !progress.RegisterValidPhoto(53, true),
                "Monotonic actual results reject duplicate callbacks.");
            Require(progress.RegisterValidPhoto(55, true) && !progress.RegisterPhotoSaved(53, true),
                "A newer capture replaces an unsaved snapshot without accepting the old save.");
            progress.Reset();
            Require(progress.Step == PhotographyTutorialStep.ObserveMovement && !progress.HasCapturedPhoto
                && !progress.HasSavedPhoto && !progress.CanContinue && !progress.RegisterValidPhoto(55, true),
                "Restart clears all latched goals and confirmation readiness while preserving the result floor.");
            Require(progress.RegisterValidPhoto(56, true), "A new post-reset capture remains eligible.");
        }
        private static void CheckArmIndependentLessonAvailability()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                // Keep the bootstrap inactive so the fixture cannot create a map or player.
                var owner = new GameObject("Photography availability lesson");
                owner.SetActive(false); SceneManager.MoveGameObjectToScene(owner, scene);
                var bootstrap = owner.AddComponent<HeightMapPlayerSceneBootstrap>();
                var lesson = owner.AddComponent<TutorialPhotographyLesson>();
                var targets = owner.GetComponent<TutorialDiscoveryTargets>();
                typeof(TutorialPhotographyLesson).GetField("bootstrap", flags).SetValue(lesson, bootstrap);
                typeof(TutorialPhotographyLesson).GetField("targets", flags).SetValue(lesson, targets);

                var player = new GameObject("Photography availability player");
                SceneManager.MoveGameObjectToScene(player, scene);
                var arm = player.AddComponent<RobotArmController>();
                var mover = player.GetComponent<RobotMover>();
                var photo = player.AddComponent<PhotoModeController>();
                bootstrap.robot = mover; bootstrap.armController = arm;
                typeof(TutorialPhotographyLesson).GetField("camera", flags).SetValue(lesson, photo);
                Func<bool> valid = () => (bool)typeof(TutorialPhotographyLesson)
                    .GetMethod("CanPractice", flags).Invoke(lesson, null);
                Require(!valid(), "A missing teaching animal still blocks unsaved photography practice.");

                var recordedPhoto = new PhotoResultSnapshot("pileated_woodpecker", "Teaching bird", "Woodpecker",
                    "Dryocopus pileatus", "Tutorial", Color.yellow, 1, 2, 3, null, Vector2.zero, 0f, 1f,
                    new DateTime(2026, 1, 1), AnimalSpecies.PileatedWoodpecker, AnimalState.Eating, true, true);
                typeof(TutorialPhotographyLesson).GetField("acceptedSnapshot", flags).SetValue(lesson, recordedPhoto);
                Require(valid(), "A captured tutorial photo keeps the lesson available after its bird departs.");
                foreach (RobotArmState state in Enum.GetValues(typeof(RobotArmState)))
                {
                    typeof(RobotArmController).GetProperty("State", flags).SetValue(arm, state);
                    typeof(RobotArmController).GetProperty("IsArmModeActive", flags)
                        .SetValue(arm, state != RobotArmState.Retracted);
                    Require(valid(), "The photography lesson permits camera entry during arm " + state + ".");
                }
                var itemObject = new GameObject("Held photography availability garbage");
                SceneManager.MoveGameObjectToScene(itemObject, scene);
                var item = itemObject.AddComponent<AnimalGame.World.WorldInteraction>();
                typeof(RobotArmController).GetField("heldObject", flags).SetValue(arm, item);
                Require(valid(), "A held item does not demand a separate manual stow before photography.");
                typeof(RobotArmController).GetField("heldObject", flags).SetValue(arm, null);
                arm.enabled = false;
                Require(!valid(), "A disabled arm component remains an unavailable player configuration.");
                arm.enabled = true; photo.enabled = false;
                Require(!valid(), "A disabled camera still blocks photography practice.");
                photo.enabled = true;
                typeof(RobotMover).GetProperty("MovementMode", flags).SetValue(mover, RobotMovementMode.Fallen);
                Require(!valid(), "A fallen player still cannot perform photography practice.");
                typeof(RobotMover).GetProperty("MovementMode", flags).SetValue(mover, RobotMovementMode.Driven);
                var tumble = player.AddComponent<RobotTumbleController>(); bootstrap.tumble = tumble;
                typeof(RobotTumbleController).GetProperty("State", flags).SetValue(tumble, RobotTumbleState.Tumbling);
                Require(!valid(), "A player who is tumbling still cannot perform photography practice.");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static void Require(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException("Photography tutorial check failed: " + message);
        }
    }
}
