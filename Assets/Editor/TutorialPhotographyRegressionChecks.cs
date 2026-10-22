using System;
using System.IO;
using AnimalGame.Tutorial;
using UnityEditor;
using UnityEngine;

namespace AnimalGame.Editor
{
    /// <summary>Outcome-based checks for the tutorial state model. Does not alter a scene or the album.</summary>
    public static class TutorialPhotographyRegressionChecks
    {
        private static int checks;
        private const float Dt = 1f / 30f;

        [MenuItem("Animal Game/Validation/Run Tutorial Photography Checks")]
        public static void Run()
        {
            checks = 0;
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run photography state checks in Edit Mode.");
            CheckMovementAndObservation();
            CheckFramingAndInterruptions();
            CheckActualCaptureAndSave();
            CheckDiscardAndEarlyCapture();
            CheckOldResultsAndReset();
            CheckInvalidSettings();
            foreach (int fps in new[] { 30, 60, 120 }) CheckFullLesson(fps);
            string output = Path.GetFullPath("output/validation/tutorial-photography");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "photography-regression.txt"),
                "PASS: " + checks + " photography lesson assertions.");
            Debug.Log("TUTORIAL PHOTOGRAPHY CHECKS PASSED: " + checks + " assertions.");
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void Feed(PhotographyTutorialProgress progress, float dt = Dt, bool valid = true,
            bool expanded = true, bool photo = false, bool result = false, bool visible = true,
            bool seen = true, bool framed = false, float moved = 2f) =>
            progress.Sample(new PhotographyTutorialSample(dt, valid, expanded, photo, result, visible, seen, framed, moved));

        private static void Observe(PhotographyTutorialProgress progress, float seconds, int fps = 30,
            bool photo = false, bool framed = false, float moved = 2f, bool expanded = true)
        {
            for (int frame = 0; frame < Mathf.CeilToInt(seconds * fps); frame++)
                Feed(progress, 1f / fps, expanded: expanded, photo: photo, framed: framed, moved: moved);
        }

        private static PhotographyTutorialProgress At(PhotographyTutorialStep step)
        {
            var progress = new PhotographyTutorialProgress();
            if (step == PhotographyTutorialStep.ObserveMovement) return progress;
            Observe(progress, .8f);
            Require(progress.Step == PhotographyTutorialStep.EnterPhoto, "Fixture observes the seen teaching animal after actual movement.");
            if (step == PhotographyTutorialStep.EnterPhoto) return progress;
            Feed(progress, photo: true);
            Require(progress.Step == PhotographyTutorialStep.FrameSubject, "Fixture enters the native photo mode.");
            if (step == PhotographyTutorialStep.FrameSubject) return progress;
            Observe(progress, .5f, photo: true, framed: true);
            Require(progress.Step == PhotographyTutorialStep.Capture, "Fixture retains the target in the real frame.");
            return progress;
        }

        private static void CheckMovementAndObservation()
        {
            var progress = new PhotographyTutorialProgress();
            Require(progress.Step == PhotographyTutorialStep.ObserveMovement && !progress.IsComplete,
                "The chapter starts by observing its moving teaching animal.");
            Observe(progress, 2f, moved: 0f);
            Require(progress.Step == PhotographyTutorialStep.ObserveMovement,
                "A stationary scanned animal cannot satisfy the movement action.");
            Feed(progress, visible: false, moved: 3f);
            Observe(progress, .4f, moved: 3f);
            Require(progress.Step == PhotographyTutorialStep.ObserveMovement,
                "Movement outside the native circular view needs a visible observation interval.");
            Feed(progress, seen: false);
            Require(progress.ReadingSeconds == 0f, "An unseen native marker resets observation reading.");
            Observe(progress, .9f, expanded: false);
            Require(progress.Step == PhotographyTutorialStep.ObserveMovement && progress.ReadingSeconds == 0f,
                "Collapsed instructions do not consume the initial observation reading interval.");
            Observe(progress, .8f, moved: 3f);
            Require(progress.Step == PhotographyTutorialStep.EnterPhoto && progress.MovementMeters == 3f,
                "Actual accumulated travel and a seen visible subject advance observation.");
            Feed(progress, moved: 0f);
            Require(progress.MovementMeters == 3f, "A shorter latest displacement does not erase prior travel.");

            progress = new PhotographyTutorialProgress();
            Observe(progress, .8f, moved: 1.9f);
            Require(progress.Step == PhotographyTutorialStep.ObserveMovement,
                "Time spent reading is insufficient until the movement threshold is reached.");
            Feed(progress, moved: 2f, photo: true);
            Require(progress.Step == PhotographyTutorialStep.FrameSubject,
                "An already entered photo mode is recognized after prior observation finishes.");
        }

        private static void CheckFramingAndInterruptions()
        {
            var progress = At(PhotographyTutorialStep.FrameSubject);
            Observe(progress, 1f, photo: true);
            Require(progress.Step == PhotographyTutorialStep.FrameSubject,
                "Being in photo mode does not establish target composition.");
            Observe(progress, .3f, photo: true, framed: true);
            Feed(progress, photo: true, framed: true, valid: false);
            Require(progress.ReadingSeconds == 0f, "Menus pause and reset continuous composition reading.");
            Observe(progress, .3f, photo: true, framed: true);
            Feed(progress, photo: true, framed: false);
            Require(progress.ReadingSeconds == 0f, "Losing the real frame breaks the composition interval.");
            Observe(progress, .3f, photo: true, framed: true);
            Feed(progress, photo: true, framed: true, dt: float.NaN);
            Require(progress.ReadingSeconds == 0f, "Non-finite elapsed time cannot complete composition.");
            Feed(progress, photo: true, framed: true, dt: 3f);
            Require(progress.ReadingSeconds == 0f, "A stalled frame cannot fabricate the entire observation interval.");
            Observe(progress, .5f, photo: true, framed: true);
            Require(progress.Step == PhotographyTutorialStep.Capture, "A continuous real composition enables capture.");
            Feed(progress, photo: true, framed: false);
            Require(progress.Step == PhotographyTutorialStep.FrameSubject,
                "A subject leaving the frame returns to useful framing instructions.");
            Feed(progress);
            Require(progress.Step == PhotographyTutorialStep.EnterPhoto,
                "Leaving photo mode before capture restores the enter-photo action.");
        }

        private static void CheckActualCaptureAndSave()
        {
            var progress = At(PhotographyTutorialStep.Capture);
            Observe(progress, 3f, photo: true, framed: true);
            Require(progress.Step == PhotographyTutorialStep.Capture,
                "Held shutter input and elapsed time alone cannot substitute for a real capture result.");
            Require(!progress.RegisterValidPhoto(1, false) && progress.Step == PhotographyTutorialStep.Capture,
                "An empty or wrong-subject result does not satisfy capture.");
            Require(progress.RegisterValidPhoto(2, true) && progress.Step == PhotographyTutorialStep.SavePhoto
                && progress.ActiveResultId == 2, "The teaching animal's actual result advances to saving.");
            Require(!progress.RegisterPhotoSaved(1, true) && !progress.RegisterPhotoSaved(2, false)
                && !progress.HasSavedPhoto, "A different photo's album save and invalid save notifications are ignored.");
            Feed(progress, photo: true, result: true, framed: true);
            Require(progress.Step == PhotographyTutorialStep.SavePhoto && progress.ReadingSeconds == 0f,
                "The result UI keeps the save action pending without consuming reading time.");
            Require(progress.RegisterPhotoSaved(2, true) && progress.HasSavedPhoto
                && progress.Step == PhotographyTutorialStep.ExitPhoto, "A real album save of this result advances to returning to exploration.");
            Feed(progress, photo: false, result: true);
            Require(!progress.IsComplete, "Even an inactive camera cannot complete while its result UI remains open.");
            Require(progress.RegisterResultClosed(2, true), "The accepted saved result can be closed normally.");
            Feed(progress, photo: true);
            Require(progress.Step == PhotographyTutorialStep.ExitPhoto,
                "Closing results while still in photo mode is not returning to exploration.");
            Feed(progress, photo: false, valid: false);
            Require(!progress.IsComplete, "A suspended gameplay sample cannot complete the exit action.");
            Feed(progress);
            Require(progress.IsComplete && progress.NormalizedProgress == 1f,
                "Completion requires a saved target photo, closed result UI and real photo-mode exit.");
            Require(!progress.RegisterValidPhoto(3, true), "Later photos cannot restart a completed lesson.");
        }

        private static void CheckDiscardAndEarlyCapture()
        {
            var progress = At(PhotographyTutorialStep.Capture);
            Require(progress.RegisterValidPhoto(10, true), "Fixture accepts a new target photo.");
            Feed(progress, photo: true, result: true, framed: true);
            Require(progress.RegisterResultClosed(10, true) && progress.Step == PhotographyTutorialStep.Capture
                && progress.ActiveResultId == 0 && !progress.HasSavedPhoto,
                "Discarding the result returns directly to capture when the target remains framed.");
            Require(!progress.RegisterPhotoSaved(10, true) && !progress.RegisterValidPhoto(10, true),
                "An old discarded result cannot later satisfy a save or become a fresh capture.");
            Require(progress.RegisterValidPhoto(11, true), "A subsequent successful retry is accepted.");
            Feed(progress, photo: true, result: true, framed: false);
            Feed(progress, photo: true, result: false, framed: false);
            Require(progress.Step == PhotographyTutorialStep.FrameSubject,
                "An observed native result UI closing unsaved also falls back without a dedicated close callback.");

            progress = new PhotographyTutorialProgress();
            Require(progress.RegisterValidPhoto(20, true) && progress.Step == PhotographyTutorialStep.SavePhoto,
                "An early successful subject photo proves entering, framing and capture without demanding a repeat.");
            Require(progress.RegisterPhotoSaved(20, true), "The same early result can immediately be saved.");
            Feed(progress, photo: false, result: true);
            Require(!progress.IsComplete, "An early saved result still needs its result UI dismissed.");
            Feed(progress, photo: false, result: false);
            Require(progress.IsComplete, "An early photo plus its real save can finish once normal exploration resumes.");
        }

        private static void CheckOldResultsAndReset()
        {
            var progress = new PhotographyTutorialProgress(existingResultId: 50);
            Require(!progress.RegisterValidPhoto(49, true) && !progress.RegisterValidPhoto(50, true)
                && !progress.RegisterValidPhoto(0, true), "Pre-chapter result IDs cannot count as new tutorial photos.");
            Require(!progress.RegisterPhotoSaved(51, true), "A save without a matching accepted subject capture is insufficient.");
            Require(progress.RegisterValidPhoto(53, true), "Fresh monotonic IDs may skip unrelated result IDs.");
            Require(!progress.RegisterValidPhoto(52, true) && !progress.RegisterValidPhoto(53, true),
                "Out-of-order and duplicate capture callbacks cannot replace an accepted result.");
            Require(progress.RegisterValidPhoto(55, true) && progress.ActiveResultId == 55,
                "A newer actual target capture may replace an earlier unsaved result.");
            Require(!progress.RegisterResultClosed(53, true) && !progress.RegisterPhotoSaved(53, true),
                "The replaced result cannot satisfy closing or saving the current result.");
            progress.Reset();
            Require(progress.Step == PhotographyTutorialStep.ObserveMovement && progress.ActiveResultId == 0
                && !progress.HasSavedPhoto && !progress.RegisterValidPhoto(55, true),
                "Restart clears progress while retaining the result floor against stale callbacks.");
            Require(progress.RegisterValidPhoto(56, true), "A post-reset actual capture remains valid.");
            progress.Reset(80);
            Require(!progress.RegisterValidPhoto(79, true) && progress.RegisterValidPhoto(81, true),
                "A newly supplied camera result floor is honored on restart.");
        }

        private static void CheckInvalidSettings()
        {
            var progress = new PhotographyTutorialProgress(float.NaN, float.PositiveInfinity, float.NaN);
            Feed(progress, moved: float.NaN);
            Require(float.IsFinite(progress.Value) && float.IsFinite(progress.Target) && progress.MovementMeters == 0f,
                "Malformed timing settings and non-finite travel do not poison lesson progress.");
            Observe(progress, .8f, moved: 2f);
            Require(progress.Step == PhotographyTutorialStep.EnterPhoto,
                "Malformed Inspector timings use safe finite observation defaults.");
        }

        private static void CheckFullLesson(int fps)
        {
            var progress = new PhotographyTutorialProgress();
            Observe(progress, .8f, fps, moved: 2.4f);
            Require(progress.Step == PhotographyTutorialStep.EnterPhoto, fps + " FPS: movement observation completes.");
            Feed(progress, 1f / fps, photo: true);
            Observe(progress, .5f, fps, photo: true, framed: true);
            Require(progress.Step == PhotographyTutorialStep.Capture, fps + " FPS: framing completes at a stable real-time duration.");
            Require(progress.RegisterValidPhoto(fps, true), fps + " FPS: real target capture is accepted.");
            Feed(progress, 1f / fps, photo: true, result: true);
            Require(progress.RegisterPhotoSaved(fps, true), fps + " FPS: matching album save is accepted.");
            Feed(progress, 1f / fps, photo: true, result: false);
            Feed(progress, 1f / fps, photo: false);
            Require(progress.IsComplete, fps + " FPS: full actual outcome sequence completes.");
        }

        private static void Require(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException("Photography tutorial check failed: " + message);
        }
    }
}
