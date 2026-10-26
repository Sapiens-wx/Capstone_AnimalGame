using System;
using System.IO;
using AnimalGame.Tutorial;
using UnityEditor;
using UnityEngine;

namespace AnimalGame.Editor
{
    /// <summary>Navigation exceptions and actual equipment observations for the camera-upgrade chapter.</summary>
    public static class TutorialRefitRegressionChecks
    {
        private static int checks;
        private const float Dt = 1f / 60f;

        [MenuItem("Animal Game/Validation/Run Tutorial Refit Checks")]
        public static void Run()
        {
            checks = 0;
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run refit checks in Edit Mode.");
            CheckNavigationExceptions();
            foreach (int fps in new[] { 30, 60, 120 }) CheckFullLesson(fps);
            CheckEarlyArrivalAndEquippedLens();
            CheckCloseAndReopen();
            CheckChangingBackAndReset();
            CheckEditableText();
            string requested = Environment.GetEnvironmentVariable("TUTORIAL_REFIT_VALIDATION_OUTPUT");
            string output = string.IsNullOrWhiteSpace(requested)
                ? Path.GetFullPath("output/validation/tutorial-refit") : Path.GetFullPath(requested);
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "refit-regression.txt"), "PASS: " + checks + " camera-upgrade assertions.");
            Debug.Log("TUTORIAL REFIT CHECKS PASSED: " + checks + " assertions.");
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void Feed(RefitTutorialProgress progress, float dt = Dt, bool valid = true,
            bool expanded = true, bool atEntrance = false, bool inside = false, bool atWorkbench = false,
            bool stopped = true, bool open = false, bool installed = false) =>
            progress.Sample(new RefitTutorialSample(dt, valid, expanded, atEntrance, inside,
                atWorkbench, stopped, open, installed));

        private static void CheckNavigationExceptions()
        {
            var progress = new RefitTutorialProgress();
            Require(progress.Step == RefitTutorialStep.ReachMachine && !progress.CanContinue && !progress.Continue(),
                "A cannot skip the initial navigation target.");
            Feed(progress, atEntrance: true, stopped: false);
            Require(progress.CanContinue, "Reaching the entrance while moving satisfies its approach.");
            Feed(progress, atEntrance: true, valid: false);
            Require(!progress.CanContinue, "Blocked gameplay cannot finish navigation.");
            Feed(progress, dt: float.NaN, atEntrance: true);
            Feed(progress, dt: 3f, atEntrance: true);
            Require(!progress.CanContinue && float.IsFinite(progress.Value), "Invalid timing cannot finish a target.");
            Feed(progress, atEntrance: true);
            for (int frame = 0; frame < 30; frame++) Feed(progress, atEntrance: true);
            Require(progress.CanContinue && progress.Step == RefitTutorialStep.ReachMachine,
                "Reaching the approach target waits for its explicit A confirmation.");
            Require(progress.Continue() && progress.Step == RefitTutorialStep.EnterMachine,
                "A at the entrance reveals the drive-inside instruction.");
            Feed(progress);
            Require(!progress.CanContinue && !progress.Continue(), "A cannot substitute for actually entering.");
            Feed(progress, inside: true, stopped: false);
            Require(progress.Step == RefitTutorialStep.ReachWorkbench && !progress.CanContinue,
                "Crossing into the actual interior switches directly to the workbench target without A.");
            Feed(progress, inside: true, atWorkbench: true, stopped: false);
            Require(progress.Step == RefitTutorialStep.OpenWorkbench, "Entering station range requests the native interaction without requiring a stop.");
            Feed(progress, inside: true, atWorkbench: true);
            Require(progress.Step == RefitTutorialStep.OpenWorkbench && !progress.CanContinue && !progress.Continue(),
                "Arriving in range directly requests native A-to-open, without an extra tutorial confirmation.");
            Feed(progress, inside: true, atWorkbench: true, open: true);
            Require(progress.Step == RefitTutorialStep.RefitIntroduction && progress.CanContinue,
                "Only the actual open interface reaches the introduction.");
        }

        private static void CheckFullLesson(int fps)
        {
            var progress = new RefitTutorialProgress();
            float dt = 1f / fps;
            Feed(progress, dt, inside: true, atWorkbench: true, open: true);
            for (int frame = 0; frame < fps; frame++) Feed(progress, dt, inside: true, atWorkbench: true, open: true);
            Require(progress.Step == RefitTutorialStep.RefitIntroduction && progress.Continue(),
                fps + " FPS: an open paused-menu introduction stays pending until A.");
            Require(progress.Step == RefitTutorialStep.EquipLens && !progress.CanContinue && !progress.Continue(),
                "An interface confirmation cannot count as an actual lens installation.");
            for (int frame = 0; frame < fps; frame++) Feed(progress, dt, inside: true, open: true);
            Require(progress.Step == RefitTutorialStep.EquipLens && !progress.HasInstalledLens,
                "Looking at the camera slot or spending time in the menu never installs equipment.");
            Feed(progress, dt, inside: true, open: true, installed: true);
            for (int frame = 0; frame < fps; frame++) Feed(progress, dt, inside: true, open: true, installed: true);
            Require(progress.Step == RefitTutorialStep.EquipLens && progress.HasInstalledLens && progress.CanContinue,
                "The real target lens is remembered while its success message waits for a fresh A press.");
            Require(progress.Continue() && progress.Step == RefitTutorialStep.CloseRefit,
                "A after installation explains the actual B-to-close operation.");
            Feed(progress, dt, inside: true, open: true, installed: true);
            Require(progress.Step == RefitTutorialStep.CloseRefit && !progress.Continue(),
                "A cannot pretend to close the native refit interface.");
            Feed(progress, dt, inside: true, installed: true);
            Require(progress.Step == RefitTutorialStep.NewLensControls && !progress.CanContinue,
                "Closing the native interface restores the controls explanation without another navigation gate.");
            float before = progress.ReadingSeconds;
            for (int frame = 0; frame < fps; frame++) Feed(progress, dt, expanded: false, inside: true, installed: true);
            Require(progress.ReadingSeconds == before && !progress.CanContinue,
                "A collapsed controls explanation does not accumulate reading time.");
            progress.Suspend();
            Require(!progress.CanContinue, "Suspended text cannot claim a confirmation.");
            for (int frame = 0; frame < fps; frame++) Feed(progress, dt, inside: true, installed: true);
            Require(progress.Step == RefitTutorialStep.NewLensControls && progress.CanContinue,
                "Reading the new focus-and-shutter instructions never advances the card automatically.");
            Require(progress.Continue() && progress.IsComplete && !progress.CanContinue,
                "A confirms the last explanation and completes the chapter directly.");
            Require(!progress.Continue(), "A completed model cannot advance past its final card.");
        }

        private static void CheckEarlyArrivalAndEquippedLens()
        {
            var progress = new RefitTutorialProgress();
            Feed(progress, inside: true);
            Require(progress.Step == RefitTutorialStep.ReachWorkbench,
                "An already-interior player is not sent back outside or asked to confirm doorway entry.");
            Feed(progress, inside: true, atWorkbench: true, installed: true);
            Require(progress.Step == RefitTutorialStep.OpenWorkbench && progress.HasInstalledLens,
                "An already-owned upgraded lens is retained without skipping the real station interaction.");
            Feed(progress, inside: true, open: true, installed: true);
            Require(progress.Step == RefitTutorialStep.RefitIntroduction && progress.Continue(),
                "An early open still needs its interface explanation acknowledged.");
            Require(progress.Step == RefitTutorialStep.EquipLens && progress.CanContinue,
                "An already-installed exact target lens satisfies the installation goal but keeps its own card.");
            Feed(progress, inside: true, installed: true);
            Require(progress.Step == RefitTutorialStep.EquipLens && progress.CanContinue,
                "Closing an already-successful installation does not lose its pending confirmation.");
            Require(progress.Continue() && progress.Step == RefitTutorialStep.CloseRefit,
                "The installed success is acknowledged separately after an early close.");
            Feed(progress, inside: true, installed: true);
            Require(progress.Step == RefitTutorialStep.NewLensControls,
                "An already-closed interface is respected after installation confirmation.");
        }

        private static void CheckCloseAndReopen()
        {
            var progress = new RefitTutorialProgress();
            Feed(progress, inside: true, atWorkbench: true, open: true);
            Feed(progress, inside: true, atWorkbench: true);
            Require(progress.Step == RefitTutorialStep.OpenWorkbench && !progress.CanContinue,
                "Closing the introduction before confirming returns to native A-to-open.");
            Feed(progress, inside: true, atWorkbench: true, open: true);
            Require(progress.Step == RefitTutorialStep.RefitIntroduction && progress.Continue(),
                "Reopening an unconfirmed introduction does not skip it.");
            Feed(progress, inside: true, atWorkbench: true);
            Require(progress.Step == RefitTutorialStep.OpenWorkbench && !progress.CanContinue,
                "Closing before installation gives a recoverable station-open instruction.");
            Feed(progress, inside: true, atWorkbench: true, open: true);
            Require(progress.Step == RefitTutorialStep.EquipLens && !progress.CanContinue,
                "A confirmed introduction stays confirmed when recovering to install the lens.");
            Feed(progress, inside: false, atWorkbench: false);
            Require(progress.Step == RefitTutorialStep.EnterMachine,
                "Leaving the building before installation restores entrance guidance.");
            Feed(progress, inside: true);
            Require(progress.Step == RefitTutorialStep.ReachWorkbench,
                "Reentry directly restores workbench guidance without asking for A.");
        }

        private static void CheckChangingBackAndReset()
        {
            var progress = new RefitTutorialProgress(float.NaN);
            Feed(progress, inside: true, open: true);
            Require(progress.Continue(), "The introduction can be confirmed for a fresh attempt.");
            Feed(progress, inside: true, open: true, installed: true);
            Require(progress.Continue() && progress.Step == RefitTutorialStep.CloseRefit,
                "The real installation unlocks its closing instruction.");
            Feed(progress, inside: true, open: true, installed: false);
            Require(progress.Step == RefitTutorialStep.EquipLens && !progress.CanContinue && progress.HasInstalledLens,
                "Replacing the new lens with the old one cannot complete the chapter using stale success.");
            Feed(progress, inside: true, open: true, installed: true);
            Require(progress.Continue(), "Reinstalling the exact target restores the installation confirmation.");
            Feed(progress, inside: true, installed: true);
            for (int frame = 0; frame < 60; frame++) Feed(progress, inside: true, installed: true);
            Feed(progress, valid: false, inside: true, installed: true);
            Require(!progress.CanContinue, "An invalid final reading sample releases confirmation ownership.");
            Feed(progress, inside: true, installed: true);
            Require(progress.CanContinue && float.IsFinite(progress.Value) && float.IsFinite(progress.Target),
                "Reading resumes with finite timing after a transient suspension.");
            progress.Reset();
            Require(progress.Step == RefitTutorialStep.ReachMachine && !progress.HasInstalledLens
                && !progress.CanContinue && progress.ReadingSeconds == 0f,
                "Restart clears installation, introduction and navigation evidence.");
        }

        private static void CheckEditableText()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TutorialTextContent>("Assets/Data/Tutorial/TutorialText.asset");
            Require(asset != null && asset.Refit != null, "The scene's normal text asset contains the new editable chapter.");
            var tokens = new TutorialTextTokens().Add("confirm", "A").Add("cancel", "B").Add("photo", "X")
                .Add("rb", "RB").Add("lb", "LB").Add("rt", "RT").Add("lt", "LT")
                .Add("lens", "Zoom Lens 300").Add("distance", 4f);
            foreach (RefitTutorialStep step in Enum.GetValues(typeof(RefitTutorialStep)))
            {
                if (step == RefitTutorialStep.Completed) continue;
                TutorialStepText item = asset.Refit.GetStep(step);
                Require(item != null && !string.IsNullOrWhiteSpace(item.Action) && !string.IsNullOrWhiteSpace(item.Message),
                    "Inspector text includes the actual " + step + " instruction.");
                Require(!tokens.Format(item.Message).Contains("{"), "Every " + step + " message has supported placeholders.");
            }
            string controls = tokens.Format(asset.Refit.NewLensControls.Message);
            Require(controls.Contains("Hold RB to focus") && controls.Contains("release") && controls.Contains("again to shoot"),
                "The upgrade explanation uses focus, release and press-to-shoot rather than the damaged prime's automatic shutter.");
            foreach (string message in new[] { asset.Refit.MenuIntroductionMessage, asset.Refit.MenuEquipMessage,
                asset.Refit.MenuInstalledMessage, asset.Refit.MenuCloseMessage })
            {
                Require(message.Split('\n').Length <= 2 && !tokens.Format(message).Contains("{"),
                    "The in-menu guide uses two editable lines with supported controller placeholders.");
            }
            Require(tokens.Format(asset.Refit.MenuInstalledMessage).Contains("Release A"),
                "The installed message explicitly separates the equipment action from the next confirmation.");
            var copy = ScriptableObject.CreateInstance<TutorialTextContent>();
            try
            {
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(asset), copy);
                Require(JsonUtility.ToJson(copy.Refit) == JsonUtility.ToJson(asset.Refit),
                    "The editable upgrade chapter survives a complete Unity serialization round trip.");
            }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
        }

        private static void Require(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException("Refit tutorial check failed: " + message);
        }
    }
}
