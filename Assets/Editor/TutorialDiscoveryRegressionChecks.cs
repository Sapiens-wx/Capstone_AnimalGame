using System;
using System.IO;
using AnimalGame.Tutorial;
using UnityEditor;
using UnityEngine;

namespace AnimalGame.Editor
{
    /// <summary>Pure scan-lesson state checks; no scene, target or UI is altered.</summary>
    public static class TutorialDiscoveryRegressionChecks
    {
        private static int checks;
        private const float Dt = 1f / 30f;

        [MenuItem("Animal Game/Validation/Run Tutorial Discovery Checks")]
        public static void Run()
        {
            checks = 0;
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run discovery lesson checks in Edit Mode.");
            CheckNewChargeAndRelease();
            CheckRealHitsAndObservation();
            CheckExpiryAndInterruptedReading();
            CheckNewRescanAndReset();
            foreach (int fps in new[] { 30, 60, 120 }) CheckFullLesson(fps);
            string output = Path.GetFullPath("output/validation/tutorial-discovery");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "discovery-regression.txt"), "PASS: " + checks + " scan lesson assertions.");
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

        private static void Observe(DiscoveryTutorialProgress progress, float seconds, int fps = 30,
            bool expanded = true, bool animal = false, bool seen = false, bool item = false)
        {
            for (int frame = 0; frame < Mathf.CeilToInt(seconds * fps); frame++)
                Feed(progress, 1f / fps, expanded: expanded, animal: animal, seen: seen, item: item);
        }

        private static int Wave(DiscoveryTutorialProgress progress)
        {
            progress.RegisterChargeStarted(true); Feed(progress, full: true);
            int wave = progress.RegisterScanReleased(true);
            Require(wave > 0, "A new eligible full charge followed by real release produces a tracked wave.");
            return wave;
        }

        private static DiscoveryTutorialProgress At(DiscoveryTutorialStep step)
        {
            var progress = new DiscoveryTutorialProgress();
            if (step == DiscoveryTutorialStep.Charge) return progress;
            progress.RegisterChargeStarted(true); Feed(progress, full: true);
            if (step == DiscoveryTutorialStep.Release) return progress;
            int wave = progress.RegisterScanReleased(true);
            if (step == DiscoveryTutorialStep.FindAnimal) return progress;
            progress.RegisterAnimalHit(wave, true); Observe(progress, 1.2f, animal: true);
            Require(progress.Step == DiscoveryTutorialStep.ConfirmAnimal, "Fixture observes a real active scanned-animal marker.");
            if (step == DiscoveryTutorialStep.ConfirmAnimal) return progress;
            Feed(progress, animal: true, seen: true);
            Require(progress.Step == DiscoveryTutorialStep.ReadObject, "Fixture confirms actual seen state before inspecting objects.");
            if (step == DiscoveryTutorialStep.ReadObject) return progress;
            progress.RegisterObjectHit(wave, true); Observe(progress, 1.5f, item: true);
            Require(progress.Step == DiscoveryTutorialStep.Rescan, "Fixture finishes the actual visible-object reading interval.");
            return progress;
        }

        private static void CheckNewChargeAndRelease()
        {
            var progress = new DiscoveryTutorialProgress();
            Require(progress.Step == DiscoveryTutorialStep.Charge && progress.LatestWaveId == 0, "A new chapter starts with a new scan charge.");
            Feed(progress, full: true);
            Require(progress.Step == DiscoveryTutorialStep.Charge && progress.RegisterScanReleased(true) == 0,
                "A charge already held before this chapter and its release cannot skip the first action.");
            Require(!progress.RegisterAnimalHit(0, true) && !progress.RegisterObjectHit(44, true),
                "Unassociated target state and old scan hits are not lesson evidence.");
            progress.RegisterChargeStarted(true); Feed(progress);
            progress.RegisterChargeCancelled(true);
            Require(progress.Step == DiscoveryTutorialStep.Charge && progress.RegisterScanReleased(true) == 0,
                "An early release/cancel retains the charge step and never launches a qualifying scan.");
            progress.RegisterChargeStarted(false); Feed(progress, full: true);
            Require(progress.Step == DiscoveryTutorialStep.Charge, "A menu-only charge-start event is ignored.");
            progress.RegisterChargeStarted(true); Feed(progress, full: true);
            Require(progress.Step == DiscoveryTutorialStep.Release && progress.IsChargeReady && progress.LatestWaveId == 0,
                "Full charge advances only to Release; holding fully charged never counts as an emitted scan.");
            progress.Suspend();
            Require(progress.Step == DiscoveryTutorialStep.Release && !progress.IsChargeReady && progress.NeedsFreshScan
                && progress.RegisterScanReleased(true) == 0, "Pause cancels a pending charge without consuming a resumed release.");
            int wave = Wave(progress);
            Require(progress.Step == DiscoveryTutorialStep.FindAnimal && progress.LatestWaveId == wave,
                "A genuinely new full charge and release resume the pending action.");
            Require(progress.RegisterScanReleased(true) == 0 && progress.LatestWaveId == wave,
                "Repeated release callbacks cannot fabricate additional waves.");
        }

        private static void CheckRealHitsAndObservation()
        {
            var progress = At(DiscoveryTutorialStep.FindAnimal);
            Observe(progress, 2f, animal: true, seen: true, item: true);
            Require(progress.Step == DiscoveryTutorialStep.FindAnimal && progress.Value == 0f,
                "Visible/discovered targets alone cannot replace this chapter's actual wave hit.");
            Require(!progress.RegisterAnimalHit(progress.LatestWaveId, false), "Modal target callbacks cannot satisfy a scan action.");
            int wave = progress.LatestWaveId;
            Require(progress.RegisterAnimalHit(wave, true) && progress.RegisterObjectHit(wave, true),
                "One real emitted wave can register both the designated animal and object.");
            Observe(progress, 1.5f, expanded: false, animal: true);
            Require(progress.Value == 0f, "A collapsed legend cannot complete marker reading.");
            Observe(progress, .5f, animal: true);
            Require(progress.Value > .4f && progress.Step == DiscoveryTutorialStep.FindAnimal, "Partial marker reading remains on the current step.");
            Feed(progress, animal: false);
            Require(progress.Value == 0f && progress.NeedsFreshScan, "An expired or offscreen marker cannot bank continuous observation time.");
            Observe(progress, 1.2f, animal: true);
            Require(progress.Step == DiscoveryTutorialStep.ConfirmAnimal, "The active marker and expanded legend complete actual reading time.");
            Feed(progress, animal: true, seen: false);
            Require(progress.Step == DiscoveryTutorialStep.ConfirmAnimal, "A scanned/discovered hollow marker cannot substitute for real first-sight confirmation.");
            Feed(progress, animal: false, seen: true);
            Require(progress.Step == DiscoveryTutorialStep.ConfirmAnimal, "A remembered seen state still requires the live solid marker for this explanation.");
            Feed(progress, animal: true, seen: true);
            Require(progress.Step == DiscoveryTutorialStep.ReadObject, "Real active solid confirmation unlocks object reading.");
            Observe(progress, 1.5f, item: true);
            Require(progress.Step == DiscoveryTutorialStep.Rescan, "The object hit from the same first wave remains valid for its later explanation.");
        }

        private static void CheckExpiryAndInterruptedReading()
        {
            var progress = At(DiscoveryTutorialStep.ReadObject);
            Observe(progress, 2f, item: true);
            Require(progress.Step == DiscoveryTutorialStep.ReadObject && progress.Value == 0f,
                "Unrelated visible object highlights do not stand in for a designated-object hit.");
            progress.RegisterObjectHit(progress.LatestWaveId, true);
            Observe(progress, .6f, item: true);
            Require(progress.Value > .5f, "The real highlighted object earns visible reading time.");
            Feed(progress, item: false);
            Require(progress.Step == DiscoveryTutorialStep.ReadObject && progress.Value == 0f && progress.NeedsFreshScan,
                "Highlight expiry keeps completed animal actions while requesting a refresh of the current object explanation.");
            int wave = Wave(progress);
            Require(progress.Step == DiscoveryTutorialStep.ReadObject && progress.RegisterObjectHit(wave, true),
                "A refresh scan during object reading stays on the unfinished explanation.");
            Observe(progress, .6f, item: true); progress.Suspend();
            Require(progress.Value == 0f, "Pause breaks continuous reading rather than awarding hidden time.");
            foreach (float badTime in new[] { 0f, float.NaN, float.PositiveInfinity, 2f }) Feed(progress, badTime, item: true);
            Require(progress.Value == 0f, "Invalid and suspended frame durations do not advance reading.");
            Feed(progress, valid: false, item: true);
            Observe(progress, 1.5f, item: true);
            Require(progress.Step == DiscoveryTutorialStep.Rescan, "A later uninterrupted observation finishes without replaying earlier animal actions.");
        }

        private static void CheckNewRescanAndReset()
        {
            var progress = At(DiscoveryTutorialStep.ReadObject);
            int oldWave = progress.LatestWaveId;
            progress.RegisterObjectHit(oldWave, true);
            progress.RegisterChargeStarted(true); Feed(progress, full: true, item: true);
            Observe(progress, 1.5f, item: true);
            Require(progress.Step == DiscoveryTutorialStep.Rescan, "The rescan lesson begins only after object reading finishes.");
            Require(progress.RegisterAnimalHit(oldWave, true) && !progress.IsComplete,
                "The first wave finishing late cannot complete the rescan lesson.");
            int earlierChargeWave = progress.RegisterScanReleased(true);
            Require(earlierChargeWave > 0 && progress.RegisterObjectHit(earlierChargeWave, true) && !progress.IsComplete,
                "A charge started before Rescan is not the requested new scan cycle.");
            progress.RegisterChargeStarted(true); Feed(progress); progress.RegisterChargeCancelled(true);
            Require(progress.RegisterScanReleased(true) == 0 && !progress.IsComplete, "A short or cancelled rescan cannot count.");
            int newWave = Wave(progress);
            Require(!progress.RegisterAnimalHit(oldWave, true) && !progress.IsComplete,
                "Old wave callbacks are rejected after the actual new release.");
            Require(!progress.RegisterObjectHit(newWave, false) && !progress.IsComplete,
                "A new release still waits for a valid target hit outside menus.");
            Require(progress.RegisterObjectHit(newWave, true) && progress.IsComplete && progress.NormalizedProgress == 1f,
                "The fresh wave hitting either designated teaching target completes rescan.");
            progress.Suspend(); Feed(progress, valid: false);
            Require(progress.IsComplete, "Later pauses leave a completed chapter stable.");
            progress.Reset();
            Require(progress.Step == DiscoveryTutorialStep.Charge && progress.LatestWaveId == 0 && progress.Value == 0f,
                "Scene restart clears all lesson evidence and returns to charging.");
            int resetWave = Wave(progress);
            Require(resetWave != newWave && !progress.RegisterObjectHit(newWave, true),
                "A reset does not reuse a wave identifier that can admit an old callback.");
        }

        private static void CheckFullLesson(int fps)
        {
            var progress = new DiscoveryTutorialProgress();
            progress.RegisterChargeStarted(true); Feed(progress, 1f / fps, full: true);
            Require(progress.Step == DiscoveryTutorialStep.Release, fps + " FPS waits for actual release after full charge.");
            int wave = progress.RegisterScanReleased(true);
            progress.RegisterAnimalHit(wave, true); progress.RegisterObjectHit(wave, true);
            Observe(progress, 1.2f, fps, animal: true);
            Require(progress.Step == DiscoveryTutorialStep.ConfirmAnimal, fps + " FPS observes the same marker reading duration.");
            Feed(progress, 1f / fps, animal: true, seen: true);
            Require(progress.Step == DiscoveryTutorialStep.ReadObject, fps + " FPS requires actual solid-marker confirmation.");
            Observe(progress, 1.5f, fps, item: true);
            Require(progress.Step == DiscoveryTutorialStep.Rescan, fps + " FPS observes the same object reading duration.");
            int next = Wave(progress); progress.RegisterAnimalHit(next, true);
            Require(progress.IsComplete, fps + " FPS completes only after the fresh wave hits a teaching target.");
        }

        private static void Require(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
