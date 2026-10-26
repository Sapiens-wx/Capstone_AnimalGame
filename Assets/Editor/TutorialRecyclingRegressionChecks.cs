using System;
using System.IO;
using System.Reflection;
using AnimalGame.Tutorial;
using UnityEditor;
using UnityEngine;

namespace AnimalGame.Editor
{
    /// <summary>Real disposal facts and confirmation ownership for the continuous recycling exercise.</summary>
    public static class TutorialRecyclingRegressionChecks
    {
        private static int checks;
        private const float Dt = 1f / 60f;

        [MenuItem("Animal Game/Validation/Run Tutorial Recycling Checks")]
        public static void Run()
        {
            checks = 0;
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run recycling regression checks in Edit Mode.");
            foreach (int fps in new[] { 30, 60, 120 }) CheckFullLesson(fps);
            CheckDroppingAndFalseSuccess();
            CheckLargeWasteFalseSuccess();
            CheckEarlyRecyclingAndSuspension();
            CheckLessonOrder();
            CheckEditableCopy();
            string requested = Environment.GetEnvironmentVariable("TUTORIAL_RECYCLING_VALIDATION_OUTPUT");
            string output = string.IsNullOrWhiteSpace(requested)
                ? Path.GetFullPath("output/validation/tutorial-recycling") : Path.GetFullPath(requested);
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "recycling-regression.txt"), "PASS: " + checks + " recycling assertions.");
            Debug.Log("TUTORIAL RECYCLING CHECKS PASSED: " + checks + " assertions.");
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void CheckLessonOrder()
        {
            Require(((RequireComponent)Attribute.GetCustomAttribute(typeof(TutorialPhotographyLesson), typeof(RequireComponent))).m_Type0
                == typeof(TutorialDiscoveryLesson), "Photography follows discovery without depending on recycling.");
            Require(((RequireComponent)Attribute.GetCustomAttribute(typeof(TutorialRecyclingLesson), typeof(RequireComponent))).m_Type0
                == typeof(TutorialPhotographyLesson), "Recycling follows photography with no circular component dependency.");
            Require(((RequireComponent)Attribute.GetCustomAttribute(typeof(TutorialRefitLesson), typeof(RequireComponent))).m_Type0
                == typeof(TutorialRecyclingLesson), "Camera upgrade follows the completed recycling chapter.");
        }

        private static void Feed(RecyclingTutorialProgress progress, float dt = Dt, bool valid = true,
            bool atWaste = false, bool stopped = true, bool deployed = false, bool retracted = false,
            bool holding = false, bool ready = false, bool processing = false, bool grab = false,
            bool manualStow = false, bool atLarge = false, bool holdingLarge = false,
            float pull = 0f) => progress.Sample(new RecyclingTutorialSample(dt, valid,
                atWaste, stopped, deployed, retracted, holding, ready, processing, grab, manualStow,
                atLarge, holdingLarge, pull));

        private static RecyclingTutorialProgress AtRecycle()
        {
            var progress = new RecyclingTutorialProgress();
            Feed(progress, atWaste: true, retracted: true);
            Require(progress.Continue(), "A confirms reaching the waste without requiring a stop.");
            Feed(progress, deployed: true);
            Require(progress.Continue(), "A confirms actual arm deployment.");
            Require(progress.Step == RecyclingTutorialStep.RecycleWaste, "The same exercise now owns the continuous disposal action.");
            return progress;
        }

        private static void CheckFullLesson(int fps)
        {
            var progress = new RecyclingTutorialProgress();
            float dt = 1f / fps;
            Require(!progress.CanContinue && !progress.Continue(), "A cannot skip the initial target.");
            Feed(progress, dt, atWaste: true, stopped: false);
            Require(progress.CanContinue, "Reaching the target while moving satisfies arrival.");
            Feed(progress, dt, valid: false, atWaste: true);
            Feed(progress, float.NaN, atWaste: true);
            Feed(progress, 3f, atWaste: true);
            Require(!progress.CanContinue, "Blocked gameplay or invalid timing suspends arrival confirmation.");
            Feed(progress, dt, atWaste: true, retracted: true);
            for (int frame = 0; frame < fps; frame++) Feed(progress, dt, atWaste: true, retracted: true);
            Require(progress.CanContinue && progress.Step == RecyclingTutorialStep.ReachWaste,
                fps + " FPS: the reached target stays on its own card until A.");
            Feed(progress, dt, atWaste: true, retracted: true, grab: true);
            Require(progress.Continue(), "The new confirming A stays valid after neutral readiness was already displayed.");
            Feed(progress, dt, grab: true);
            Require(!progress.CanContinue && !progress.Continue(), "The arrival A cannot also satisfy deployment.");
            Feed(progress, dt, deployed: true, grab: true);
            Require(!progress.CanContinue, "Holding an old A while arms finish deploying cannot arm another confirmation.");
            Feed(progress, dt, deployed: true);
            Require(progress.Continue() && progress.Step == RecyclingTutorialStep.RecycleWaste,
                "A after full deployment starts one continuous grip-to-processing exercise.");
            for (int frame = 0; frame < fps; frame++) Feed(progress, dt, deployed: true);
            Require(!progress.CanContinue && !progress.HasRecycledWaste, "Waiting and deployed arms never count as recycling.");
            Feed(progress, dt, deployed: true, holding: true, grab: true);
            Require(progress.HasGrabbedWaste && progress.IsHoldingWaste && !progress.CanContinue,
                "A belongs to real grip; grabbing is a fact but does not finish the card.");
            Feed(progress, dt, deployed: true, holding: true, ready: true, grab: true);
            Require(progress.IsRecycleReady && !progress.CanContinue && !progress.Continue(),
                "Bringing held waste to the inlet still cannot steal its held A for a lesson confirmation.");
            Feed(progress, dt, deployed: true, holding: true, processing: true);
            Require(progress.IsProcessing && !progress.CanContinue, "Release starts native processing, not success.");
            Require(progress.RegisterRecycled(true) && !progress.RegisterRecycled(true),
                "Only the exact native disposal callback supplies one completion fact.");
            Feed(progress, dt, processing: true, grab: true);
            Require(!progress.CanContinue, "A pressed during processing cannot advance at completion.");
            Feed(progress, dt, deployed: true, grab: true);
            Require(!progress.CanContinue, "Holding A through the completion callback waits for its release.");
            Feed(progress, dt, deployed: true);
            for (int frame = 0; frame < fps; frame++) Feed(progress, dt, deployed: true);
            Require(progress.CanContinue && progress.Step == RecyclingTutorialStep.RecycleWaste,
                "Actual completed recycling is retained on the same card until a new A.");
            Feed(progress, dt, deployed: true, grab: true);
            Require(progress.Continue() && progress.Step == RecyclingTutorialStep.ReachLargeWaste,
                "The new A confirms small disposal and starts large-waste practice before final stowing.");
            Feed(progress, dt, manualStow: true, retracted: true);
            Require(!progress.CanContinue, "Closing early cannot skip the large target or satisfy the final stow.");
            Feed(progress, dt, atLarge: true, stopped: false, deployed: true);
            Require(progress.CanContinue, "The large target has the same moving-arrival freedom as the small target.");
            Feed(progress, dt, atLarge: true, grab: true);
            Require(progress.Continue() && progress.Step == RecyclingTutorialStep.RecycleLargeWaste,
                "Arrival confirmation starts one continuous large breakup and fragment disposal action.");
            Feed(progress, dt, holdingLarge: true, deployed: true, grab: true, pull: .3f);
            Require(progress.IsHoldingLargeWaste && !progress.IsHoldingWaste && !progress.CanContinue
                && Mathf.Approximately(progress.LargePullProgress, .3f), "A remains native grip while LT creates observed heavy-pull tension.");
            Feed(progress, dt, holdingLarge: true, deployed: true, grab: true, pull: 0f);
            Require(progress.LargePullProgress == 0f && !progress.HasBrokenLargeWaste,
                "Cancelling native reverse tension resets its display but cannot manufacture a breakup.");
            for (int frame = 0; frame < fps; frame++)
                Feed(progress, dt, holdingLarge: true, deployed: true, grab: true, pull: (frame + 1f) / fps);
            Require(!progress.CanContinue && !progress.HasBrokenLargeWaste,
                "Even displayed full native tension needs the exact successful breakup callback.");
            Require(progress.RegisterLargeWasteBroken(true) && !progress.RegisterLargeWasteBroken(true),
                "The designated intact target supplies exactly one verified breakup.");
            Feed(progress, dt, holding: true, deployed: true, grab: true);
            Require(progress.HasBrokenLargeWaste && !progress.HasRecycledFragment && !progress.CanContinue,
                "Native transfer into the held medium fragment continues the same card without consuming A.");
            Feed(progress, dt, deployed: true);
            Require(progress.HasDroppedWaste && !progress.CanContinue,
                "Dropping the fragment is retryable even after the earlier small waste was recycled.");
            Feed(progress, dt, holding: true, deployed: true, grab: true, ready: true);
            Require(!progress.HasDroppedWaste && progress.IsRecycleReady && !progress.CanContinue,
                "Regripping the real medium fragment restores its inlet instruction without tutorial A ownership.");
            Feed(progress, dt, holding: true, processing: true);
            Require(!progress.CanContinue, "Native medium processing is not itself a material receipt.");
            Require(progress.RegisterFragmentRecycled(true) && !progress.RegisterFragmentRecycled(true),
                "Only the exact fragment's completed native disposal supplies the large-exercise receipt.");
            Feed(progress, dt, grab: true);
            Require(!progress.CanContinue, "Holding an old A through the fragment callback cannot confirm the card.");
            Feed(progress, dt, deployed: true);
            Require(progress.CanContinue && progress.Step == RecyclingTutorialStep.RecycleLargeWaste,
                "Both breakup and fragment recycling remain confirmed facts on the continuous card until a new A.");
            Feed(progress, dt, grab: true);
            Require(progress.Continue() && progress.Step == RecyclingTutorialStep.StowArms,
                "The fresh A after large-waste disposal reaches the only final arm-stow instruction.");
            Feed(progress, dt, retracted: true);
            Require(!progress.CanContinue, "An unrelated automatic arm retraction cannot stand in for the requested L3 action.");
            Feed(progress, dt, manualStow: true);
            Feed(progress, dt, retracted: true);
            Require(progress.CanContinue, "The accepted manual close and actual fully retracted pose unlock continuation.");
            Feed(progress, dt, deployed: true);
            Require(!progress.CanContinue, "Reopening the arms before confirming no longer presents them as safely stowed.");
            Feed(progress, dt, retracted: true);
            Require(progress.Continue() && progress.IsComplete, "A after real stowing completes the chapter directly.");
            Feed(progress, dt, retracted: true);
            Require(!progress.CanContinue && !progress.Continue(), "A completed chapter releases confirmation without an extra card.");
        }

        private static void CheckDroppingAndFalseSuccess()
        {
            var progress = AtRecycle();
            Feed(progress, deployed: true, holding: true, grab: true);
            Feed(progress, deployed: true);
            Require(progress.HasDroppedWaste && !progress.HasRecycledWaste && !progress.CanContinue,
                "Dropping outside the inlet is retryable and never counts as disposal.");
            Feed(progress, deployed: true, holding: true, grab: true);
            Require(!progress.HasDroppedWaste && progress.IsHoldingWaste, "Picking the same dropped waste up clears only drop feedback.");
            Feed(progress, deployed: true, holding: true, ready: true, grab: true);
            Feed(progress, processing: true);
            Feed(progress, retracted: true);
            Require(!progress.HasRecycledWaste && !progress.CanContinue,
                "Processing animation, an absent held target, elapsed time or retracted arms cannot counterfeit the native callback.");
            Require(!progress.RegisterRecycled(false) && !progress.CanContinue,
                "A callback rejected by the exact-target observer never satisfies the lesson.");
            progress.Reset();
            Require(progress.Step == RecyclingTutorialStep.ReachWaste && !progress.HasRecycledWaste
                && !progress.HasBrokenLargeWaste && !progress.HasRecycledFragment
                && !progress.HasGrabbedWaste && !progress.CanContinue, "A restart clears prior completion and grip facts.");
        }

        private static void CheckLargeWasteFalseSuccess()
        {
            var progress = AtRecycle();
            Require(!progress.RegisterFragmentRecycled(true), "A fragment callback cannot precede a verified large breakup.");
            Require(progress.RegisterRecycled(true), "The real small receipt is retained while preparing the large test.");
            Feed(progress);
            Require(progress.Continue() && progress.Step == RecyclingTutorialStep.ReachLargeWaste,
                "Small recycling cannot finish the expanded chapter.");
            Feed(progress, atLarge: true);
            Require(progress.Continue(), "Large arrival still requires the player's confirmation.");
            Feed(progress, holdingLarge: true, ready: true, grab: true, pull: float.NaN);
            Require(!progress.IsRecycleReady && progress.LargePullProgress == 0f,
                "Intact large waste cannot be shown ready for the small/medium inlet, and invalid tension stays finite.");
            Feed(progress, holdingLarge: true, pull: 9f, grab: true);
            Require(progress.LargePullProgress == 1f && !progress.HasBrokenLargeWaste && !progress.CanContinue,
                "Clamped tension is display only and never counterfeit completion.");
            Require(!progress.RegisterLargeWasteBroken(false) && !progress.RegisterFragmentRecycled(false),
                "Rejected source/fragment observations cannot advance the lesson.");
            Feed(progress, retracted: true, manualStow: true);
            Require(!progress.CanContinue, "A vanished target and fully closed arms do not prove breakup or recycling.");
            Require(progress.RegisterLargeWasteBroken(true), "An observed native replacement verifies breakup.");
            Feed(progress, holding: true, ready: true, grab: true);
            Feed(progress, processing: true);
            Feed(progress, retracted: true, manualStow: true);
            Require(!progress.HasRecycledFragment && !progress.CanContinue,
                "Even a medium-processing animation followed by disappearance needs its exact recycling receipt.");
            progress.Reset();
            Require(!progress.HasBrokenLargeWaste && !progress.HasRecycledFragment && !progress.IsHoldingLargeWaste
                && progress.LargePullProgress == 0f, "Restart removes the previous large-grip/tension/disposal facts.");
        }

        private static void CheckEarlyRecyclingAndSuspension()
        {
            var progress = new RecyclingTutorialProgress();
            Require(progress.RegisterRecycled(true), "A verified early native recycling outcome is retained before its card.");
            Require(progress.RegisterLargeWasteBroken(true) && progress.RegisterFragmentRecycled(true),
                "Real early large breakup and exact fragment recycling are retained without skipping instructions.");
            Feed(progress, grab: true);
            Require(!progress.CanContinue, "The early held A cannot also confirm any earlier instruction.");
            for (int step = 0; step < 5; step++)
            {
                Feed(progress, retracted: true);
                Require(progress.CanContinue, "A real early outcome proves the relevant preceding action.");
                progress.Suspend();
                Require(!progress.CanContinue && progress.HasRecycledWaste, "Modal suspension keeps the real disposal fact but cannot claim A.");
                Feed(progress, retracted: true, grab: true);
                Require(!progress.CanContinue, "Resuming with A held still needs a neutral frame before continuing.");
                Feed(progress, retracted: true);
                Require(progress.Continue(), "Each earlier card still requires its own fresh A confirmation.");
            }
            Require(progress.Step == RecyclingTutorialStep.StowArms, "An early real outcome never skips the final stowing instruction.");
            Feed(progress, retracted: true);
            Require(!progress.CanContinue, "An early disposal cannot invent the requested manual arm close.");
            Feed(progress, manualStow: true, retracted: true);
            Require(progress.CanContinue, "A real close after an early completed outcome remains usable.");
        }

        private static void CheckEditableCopy()
        {
            var text = ScriptableObject.CreateInstance<TutorialTextContent>();
            try
            {
                Require(text.Recycling != null && text.Recycling.Title == "Waste Recycling", "New assets supply the English recycling chapter.");
                using (var data = new SerializedObject(text))
                {
                    SerializedProperty chapter = data.FindProperty("Recycling");
                    Require(chapter != null, "The new copy is Inspector-serialized.");
                    foreach (FieldInfo field in typeof(RecyclingLessonText).GetFields(BindingFlags.Public | BindingFlags.Instance))
                        Require(chapter.FindPropertyRelative(field.Name) != null, "Inspector exposes recycling field " + field.Name + ".");
                }
                var tokens = new TutorialTextTokens().Add("confirm", "A").Add("distance", 3.5f);
                foreach (RecyclingTutorialStep step in Enum.GetValues(typeof(RecyclingTutorialStep)))
                {
                    if (step == RecyclingTutorialStep.Completed) continue;
                    TutorialStepText item = text.Recycling.GetStep(step);
                    Require(item != null && !string.IsNullOrWhiteSpace(item.Action) && item.Action.Length <= 18,
                        "The " + step + " action stays short enough for the existing right card.");
                    Require(item.Message.Split('\n').Length <= 4 && item.Detail.Split('\n').Length <= 2,
                        "The " + step + " copy retains the current card's short format.");
                    Require(!tokens.Format(item.Message).Contains("{confirm}"), "The actual confirmation glyph formats for " + step + ".");
                }
                bool containsChinese = false;
                foreach (char character in JsonUtility.ToJson(text.Recycling))
                    containsChinese |= character >= '\u3400' && character <= '\u9fff';
                Require(!containsChinese, "Player-facing recycling copy remains English.");
            }
            finally { UnityEngine.Object.DestroyImmediate(text); }
        }

        private static void Require(bool condition, string message)
        { checks++; if (!condition) throw new InvalidOperationException(message); }
    }
}
