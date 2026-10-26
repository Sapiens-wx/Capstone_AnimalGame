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
            Require(!progress.CanContinue && !progress.Continue(), "A cannot skip the initial destination.");
            Feed(progress, atEntrance: true, stopped: false);
            Require(progress.CanContinue && progress.Step == RefitTutorialStep.ReachMachine,
                "Reaching the entrance while moving makes only its outside card ready.");
            progress.Suspend();
            Require(!progress.CanContinue, "Suspended gameplay releases tutorial A.");
            Feed(progress, dt: float.NaN, atEntrance: true);
            Feed(progress, dt: 3f, atEntrance: true);
            Require(!progress.CanContinue && float.IsFinite(progress.Value), "Invalid timing supplies no confirmation.");
            Feed(progress, atEntrance: true);
            Require(progress.Continue() && progress.Step == RefitTutorialStep.EnterMachine,
                "The entrance card retains its normal A confirmation.");
            Feed(progress);
            Require(!progress.Continue(), "A cannot replace native doorway entry.");
            Feed(progress, inside: true, stopped: false);
            Require(progress.Step == RefitTutorialStep.ReachWorkbench && !progress.CanContinue,
                "Real entry immediately selects the workbench without another A.");
            Feed(progress, inside: true, atWorkbench: true, stopped: false);
            Require(progress.Step == RefitTutorialStep.OpenWorkbench && !progress.CanContinue && !progress.Continue(),
                "Actual station range offers native A-to-open with no tutorial arrival confirmation.");
            Feed(progress, inside: true, atWorkbench: true, open: true);
            Require(progress.Step == RefitTutorialStep.EquipLens && !progress.CanContinue && !progress.Continue(),
                "The open native station starts passive equipment observation, with no introduction card.");
        }

        private static void CheckFullLesson(int fps)
        {
            var progress = new RefitTutorialProgress();
            float dt = 1f / fps;
            Feed(progress, dt, inside: true, atWorkbench: true, open: true);
            for (int frame = 0; frame < fps; frame++) Feed(progress, dt, inside: true, open: true);
            Require(progress.Step == RefitTutorialStep.EquipLens && !progress.CanContinue && !progress.HasInstalledLens,
                fps + " FPS: looking at the native menu does not fabricate installation or claim A.");
            Feed(progress, dt, inside: true, open: true, installed: true);
            for (int frame = 0; frame < fps; frame++) Feed(progress, dt, inside: true, open: true, installed: true);
            Require(progress.Step == RefitTutorialStep.EquipLens && progress.HasInstalledLens
                && !progress.CanContinue && !progress.Continue(),
                "Actual lens installation remains native and needs no tutorial success confirmation.");
            Feed(progress, dt, inside: true, installed: true);
            Require(progress.Step == RefitTutorialStep.NewLensControls && !progress.CanContinue,
                "Native menu close goes directly to the outside new-shutter card.");
            float before = progress.ReadingSeconds;
            for (int frame = 0; frame < fps; frame++) Feed(progress, dt, expanded: false, inside: true, installed: true);
            Require(progress.ReadingSeconds == before && !progress.CanContinue,
                "Collapsed outside instructions do not accumulate reading time.");
            progress.Suspend();
            Require(!progress.CanContinue, "Suspension preserves the outside card but releases A.");
            for (int frame = 0; frame < fps; frame++) Feed(progress, dt, inside: true, installed: true);
            Require(progress.CanContinue && progress.Step == RefitTutorialStep.NewLensControls,
                "Reading the outside shutter card retains its normal explicit A.");
            Require(progress.Continue() && progress.IsComplete && !progress.CanContinue && !progress.Continue(),
                "The last real outside instruction completes directly with no completion card.");
        }

        private static void CheckEarlyArrivalAndEquippedLens()
        {
            var progress = new RefitTutorialProgress();
            Feed(progress, inside: true, atWorkbench: true, installed: true);
            Require(progress.Step == RefitTutorialStep.OpenWorkbench && progress.HasInstalledLens,
                "An existing upgraded lens is retained without skipping the actual station interaction.");
            Feed(progress, inside: true, open: true, installed: true);
            Require(progress.Step == RefitTutorialStep.EquipLens && !progress.CanContinue,
                "An already-installed exact lens needs no extra in-menu confirmation.");
            Feed(progress, inside: true, installed: true);
            Require(progress.Step == RefitTutorialStep.NewLensControls,
                "Closing an already-correct native station directly reveals the outside controls.");
        }

        private static void CheckCloseAndReopen()
        {
            var progress = new RefitTutorialProgress();
            Feed(progress, inside: true, atWorkbench: true, open: true);
            Feed(progress, inside: true, atWorkbench: true);
            Require(progress.Step == RefitTutorialStep.OpenWorkbench && !progress.CanContinue,
                "Closing without the target lens restores the exterior native A-to-open instruction.");
            Feed(progress, inside: true, atWorkbench: true, open: true);
            Require(progress.Step == RefitTutorialStep.EquipLens && !progress.CanContinue,
                "Reopening still uses the native equipment controls with no introduction.");
            Feed(progress, inside: false);
            Require(progress.Step == RefitTutorialStep.EnterMachine,
                "Leaving before installation restores the entrance target.");
            Feed(progress, inside: true);
            Require(progress.Step == RefitTutorialStep.ReachWorkbench,
                "Reentry restores the workbench destination without a confirmation.");
        }

        private static void CheckChangingBackAndReset()
        {
            var progress = new RefitTutorialProgress(float.NaN);
            Feed(progress, inside: true, open: true, installed: true);
            Feed(progress, inside: true, open: true, installed: false);
            Require(progress.Step == RefitTutorialStep.EquipLens && !progress.CanContinue && progress.HasInstalledLens,
                "Replacing the target with the old lens cannot use historical installation evidence.");
            Feed(progress, inside: true, atWorkbench: true);
            Require(progress.Step == RefitTutorialStep.OpenWorkbench, "A wrong lens on close remains incomplete.");
            Feed(progress, inside: true, open: true, installed: true);
            Feed(progress, inside: true, installed: true);
            for (int frame = 0; frame < 60; frame++) Feed(progress, inside: true, installed: true);
            Feed(progress, valid: false, inside: true, installed: true);
            Require(!progress.CanContinue, "Invalid outside reading releases confirmation.");
            Feed(progress, inside: true, installed: true);
            Require(progress.CanContinue && float.IsFinite(progress.Value) && float.IsFinite(progress.Target),
                "Finite outside reading resumes normally.");
            Feed(progress, inside: true, open: true, installed: false);
            Require(progress.Step == RefitTutorialStep.EquipLens && !progress.CanContinue,
                "Changing to a wrong lens before final completion returns to passive equipment observation.");
            progress.Reset();
            Require(progress.Step == RefitTutorialStep.ReachMachine && !progress.HasInstalledLens
                && !progress.CanContinue && progress.ReadingSeconds == 0f,
                "Restart clears equipment and navigation evidence.");
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
                if (step == RefitTutorialStep.Completed || step == RefitTutorialStep.RefitIntroduction || step == RefitTutorialStep.CloseRefit) continue;
                TutorialStepText item = asset.Refit.GetStep(step);
                Require(item != null && !string.IsNullOrWhiteSpace(item.Action) && !string.IsNullOrWhiteSpace(item.Message),
                    "Inspector text includes the actual " + step + " instruction.");
                Require(!tokens.Format(item.Message).Contains("{"), "Every " + step + " message has supported placeholders.");
            }
            string controls = tokens.Format(asset.Refit.NewLensControls.Message);
            Require(controls.Contains("Hold RB to focus") && controls.Contains("release") && controls.Contains("again to shoot"),
                "The upgrade explanation uses focus, release and press-to-shoot rather than the damaged prime's automatic shutter.");
            string exterior = tokens.Format(asset.Refit.OpenWorkbench.Message);
            Require(exterior.Contains("CAMERA") && exterior.Contains("300"),
                "The outside task card clearly names the native CAMERA slot and Zoom Lens 300.");
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
