using System;
using System.IO;
using AnimalGame.Tutorial;
using UnityEditor;
using UnityEngine;

namespace AnimalGame.Editor
{
    /// <summary>Real scan evidence is latched, but each displayed instruction requires confirmation.</summary>
    public static class TutorialDiscoveryRegressionChecks
    {
        private static int checks;
        private const float Dt = 1f / 30f;

        [MenuItem("Animal Game/Validation/Run Tutorial Discovery Checks")]
        public static void Run()
        {
            checks = 0;
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run discovery checks in Edit Mode.");
            CheckInvalidEvidence();
            foreach (int fps in new[] { 30, 60, 120 }) CheckManualSequence(fps);
            CheckCompletionWithoutObjectHighlight();
            CheckResetAndMalformedSamples();
            string output = Path.GetFullPath("output/validation/tutorial-discovery");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "discovery-regression.txt"), "PASS: " + checks + " scan assertions.");
            Debug.Log("TUTORIAL DISCOVERY CHECKS PASSED: " + checks + " assertions.");
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void Feed(DiscoveryTutorialProgress progress, float dt = Dt, bool valid = true,
            bool expanded = true, bool full = false, bool animal = false, bool seen = false, bool item = false) =>
            progress.Sample(new DiscoveryTutorialSample(dt, valid, expanded, full, animal, seen, item));

        private static void CheckInvalidEvidence()
        {
            var progress = new DiscoveryTutorialProgress();
            Require(!progress.CanContinue && !progress.Continue(), "A cannot skip an incomplete charge.");
            Feed(progress, full: true, animal: true, seen: true, item: true);
            Require(!progress.CanContinue && progress.RegisterScanReleased(true) == 0,
                "A full flag without an accepted charge event is not a scan.");
            progress.RegisterChargeStarted(true); Feed(progress);
            progress.RegisterChargeCancelled(true);
            Require(progress.RegisterScanReleased(true) == 0 && !progress.CanContinue,
                "A cancelled partial charge cannot complete the charge card.");
            progress.RegisterChargeStarted(false); Feed(progress, full: true);
            Require(!progress.CanContinue && !progress.RegisterAnimalHit(0, true)
                && !progress.RegisterObjectHit(19, true), "Invalid charge and unrelated target hits do not count.");
            progress.RegisterChargeStarted(true); Feed(progress, full: true);
            Require(progress.CanContinue && progress.Step == DiscoveryTutorialStep.Charge,
                "Full native charge waits on the same card for A.");
            Require(progress.Continue() && progress.Step == DiscoveryTutorialStep.Release, "A advances exactly one card.");
            progress.RegisterChargeCancelled(true);
            Require(!progress.CanContinue && !progress.Continue(), "Release still requires an actual accepted scan.");
        }

        private static void CheckManualSequence(int fps)
        {
            var progress = new DiscoveryTutorialProgress();
            progress.RegisterChargeStarted(true); Feed(progress, full: true);
            int wave = progress.RegisterScanReleased(true);
            Require(wave > 0 && progress.Step == DiscoveryTutorialStep.Charge,
                fps + " FPS: releasing while waiting for A preserves the charge card.");
            Require(progress.RegisterAnimalHit(wave, true) && progress.RegisterObjectHit(wave, true),
                "The accepted scan wave records its exact teaching targets.");
            Require(!progress.RegisterAnimalHit(wave + 1, true), "Unassociated waves remain invalid.");
            for (int frame = 0; frame < fps * 2; frame++)
                Feed(progress, 1f / fps, animal: true, seen: true, item: true);
            Require(progress.Step == DiscoveryTutorialStep.Charge && progress.CanContinue,
                "Long native observation never automatically advances a displayed step.");
            Feed(progress, valid: false); Feed(progress, expanded: false);
            Feed(progress, animal: false, seen: false, item: false);
            Require(progress.Continue() && progress.Step == DiscoveryTutorialStep.Release && progress.CanContinue,
                "An early release is retained after a manual charge confirmation.");
            Require(progress.Continue() && progress.Step == DiscoveryTutorialStep.FindAnimal && progress.CanContinue,
                "Actual marker observation is retained even if its native mark has since faded.");
            Require(progress.Continue() && progress.Step == DiscoveryTutorialStep.ConfirmAnimal && progress.CanContinue,
                "Previously observed first sight is retained until its explanation is confirmed.");
            Require(progress.Continue() && progress.Step == DiscoveryTutorialStep.Completed && progress.IsComplete,
                "Confirming the seen animal completes discovery directly, without an object legend or rescan card.");
            Require(!progress.Continue(), "A completed discovery chapter needs no additional confirmation.");
        }

        private static void CheckCompletionWithoutObjectHighlight()
        {
            var progress = new DiscoveryTutorialProgress();
            progress.RegisterChargeStarted(true); Feed(progress, full: true);
            int wave = progress.RegisterScanReleased(true);
            Require(progress.RegisterAnimalHit(wave, true), "The accepted scan records its actual bird hit.");
            for (int frame = 0; frame < 60; frame++) Feed(progress, animal: true, seen: true);
            Require(progress.Continue() && progress.Step == DiscoveryTutorialStep.Release, "Charge confirmation is retained.");
            Require(progress.Continue() && progress.Step == DiscoveryTutorialStep.FindAnimal, "Release confirmation is retained.");
            Require(progress.Continue() && progress.Step == DiscoveryTutorialStep.ConfirmAnimal, "The bird legend still needs confirmation.");
            Require(progress.Continue() && progress.IsComplete,
                "A real seen bird completes discovery without any object hit, highlight, reading delay or extra A.");
        }

        private static void CheckResetAndMalformedSamples()
        {
            var progress = new DiscoveryTutorialProgress(float.NaN, float.PositiveInfinity);
            progress.RegisterChargeStarted(true); Feed(progress, full: true);
            int oldWave = progress.RegisterScanReleased(true);
            progress.Reset();
            Require(progress.Step == DiscoveryTutorialStep.Charge && !progress.CanContinue && !progress.HasReleasedScan,
                "Restart clears every pending confirmation and native accomplishment.");
            Require(!progress.RegisterAnimalHit(oldWave, true), "Pre-restart scan callbacks cannot satisfy a new lesson.");
            progress.RegisterChargeStarted(true); Feed(progress, full: true, dt: float.NaN);
            Require(!progress.CanContinue, "Non-finite elapsed time cannot establish a new completed charge.");
            progress.RegisterChargeStarted(true); Feed(progress, full: true, dt: 5f);
            Require(!progress.CanContinue && float.IsFinite(progress.Value) && float.IsFinite(progress.Target),
                "Stalled samples and malformed settings remain finite and do not complete actions.");
            progress.RegisterChargeStarted(true); Feed(progress, full: true);
            Require(progress.RegisterScanReleased(true) > oldWave, "Accepted wave identifiers remain monotonic across restart.");
        }

        private static void Require(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException("Discovery tutorial check failed: " + message);
        }
    }
}
