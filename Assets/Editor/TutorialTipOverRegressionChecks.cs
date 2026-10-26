using System;
using System.IO;
using System.Reflection;
using AnimalGame.Tutorial;
using UnityEditor;
using UnityEngine;

namespace AnimalGame.Editor
{
    /// <summary>Checks native recovery observations, stable landing and independent A confirmations.</summary>
    public static class TutorialTipOverRegressionChecks
    {
        private static int checks;
        private const float Dt = 1f / 60f;

        [MenuItem("Animal Game/Validation/Run Tutorial Tip-over Checks")]
        public static void Run()
        {
            checks = 0;
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run recovery model checks in Edit Mode.");
            foreach (int fps in new[] { 30, 60, 120 }) CheckFullRecovery(fps);
            CheckFalseSuccess();
            CheckEarlyRecovery();
            CheckSuspension();
            CheckDeferredFallenObservation();
            CheckSecondFall();
            CheckReminder();
            CheckInterruptedLanding();
            CheckEditableCopy();
            CheckRecoveryCopy(source: AssetDatabase.LoadAssetAtPath<TutorialTextContent>("Assets/Data/Tutorial/TutorialText.asset"));
            string requested = Environment.GetEnvironmentVariable("TUTORIAL_TIPOVER_VALIDATION_OUTPUT");
            string output = string.IsNullOrWhiteSpace(requested)
                ? Path.GetFullPath("output/validation/tutorial-tipover") : Path.GetFullPath(requested);
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "tipover-regression.txt"), "PASS: " + checks + " tip-over recovery assertions.");
            Debug.Log("TUTORIAL TIP-OVER CHECKS PASSED: " + checks + " assertions.");
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void Feed(TipOverTutorialProgress progress, float dt = Dt, bool valid = true,
            bool fallen = false, bool deployed = false, bool upright = false, bool righting = false,
            bool stable = false, bool confirm = false) => progress.Sample(new TipOverTutorialSample(
                dt, valid, fallen, deployed, upright, righting, stable, confirm));

        private static TipOverTutorialProgress AtRecovery()
        {
            var progress = new TipOverTutorialProgress();
            Feed(progress, fallen: true);
            Require(progress.Continue(), "A confirms the actual fallen-state explanation.");
            Feed(progress, fallen: true, deployed: true);
            Require(progress.Continue() && progress.Step == TipOverTutorialStep.Recover,
                "Actual deployment and a separate A start the continuous recovery card.");
            return progress;
        }

        private static void CheckFullRecovery(int fps)
        {
            var progress = new TipOverTutorialProgress();
            float dt = 1f / fps;
            Require(progress.Step == TipOverTutorialStep.Explanation && !progress.CanContinue && !progress.Continue(),
                "The initial card cannot advance before native fallen-state evidence.");
            Feed(progress, dt, fallen: true, confirm: true);
            Require(!progress.CanContinue, "A already held while the interrupt starts cannot dismiss the explanation.");
            Feed(progress, dt, fallen: true);
            Require(progress.CanContinue && progress.NormalizedProgress == 1f, "Fallen and neutral input arm explanation confirmation.");
            Feed(progress, dt, fallen: true, confirm: true);
            Require(progress.Continue() && progress.Step == TipOverTutorialStep.DeployArms,
                "A confirms only the explanation card.");
            Feed(progress, dt, fallen: true, deployed: true, confirm: true);
            Require(!progress.CanContinue && !progress.Continue(), "The same held A cannot also confirm arm deployment.");
            Feed(progress, dt, fallen: true, deployed: true);
            Require(progress.Continue() && progress.Step == TipOverTutorialStep.Recover,
                "A after actual deployment transfers to continuous support and effort.");
            for (int frame = 0; frame < fps * 2; frame++)
                Feed(progress, dt, fallen: true, deployed: true);
            Require(!progress.CanContinue && !progress.IsComplete && progress.StableSeconds == 0f,
                "Elapsed time or deployed arms cannot counterfeit successful recovery.");
            Feed(progress, dt, deployed: true, upright: true, righting: true, stable: true);
            Require(!progress.IsComplete && progress.StableSeconds == 0f,
                "A visual upright pose during the native righting animation is not a settled recovery.");
            int stableFrames = Mathf.CeilToInt(progress.RequiredStableSeconds / dt);
            for (int frame = 0; frame < stableFrames - 1; frame++)
            {
                Feed(progress, dt, upright: true, stable: true, confirm: true);
                Require(!progress.IsComplete, fps + " FPS: stable landing must last the full required duration.");
            }
            Feed(progress, dt, upright: true, stable: true, confirm: true);
            Require(progress.IsComplete && progress.Step == TipOverTutorialStep.Completed
                && !progress.CanContinue && progress.StableSeconds == progress.RequiredStableSeconds,
                "A genuine stable landing completes the same continuous card without another action-stage A.");
            for (int frame = 0; frame < fps; frame++) Feed(progress, dt, upright: true, stable: true, confirm: true);
            Require(!progress.CanContinue, "Holding A through the landing cannot immediately resume the main tutorial.");
            Feed(progress, dt, upright: true, stable: true);
            Require(!progress.CanContinue && progress.NormalizedProgress == 1f && !progress.Continue(),
                "Stable recovery completes without requesting an extra confirmation card.");
        }

        private static void CheckFalseSuccess()
        {
            var progress = new TipOverTutorialProgress();
            for (int frame = 0; frame < 120; frame++) Feed(progress, upright: true, stable: true);
            Require(!progress.IsComplete && !progress.CanContinue && progress.StableSeconds == 0f,
                "Ordinary upright gameplay before any real fallen sample must not finish an untriggered lesson.");
            Feed(progress, fallen: true, upright: true, stable: true);
            Require(!progress.IsComplete && progress.StableSeconds == 0f,
                "Contradictory upright and fallen observations never start stable-landing time.");
            foreach (float invalid in new[] { 0f, -.1f, .3f, float.NaN, float.PositiveInfinity })
            {
                Feed(progress, invalid, fallen: true, deployed: true, upright: true, stable: true);
                Require(!progress.CanContinue && progress.StableSeconds == 0f,
                    "Invalid or stalled timing suspends confirmations and stable-landing time.");
            }
            progress = AtRecovery();
            for (int frame = 0; frame < 47; frame++) Feed(progress, upright: true, stable: true);
            Feed(progress, upright: true, stable: false);
            Require(!progress.IsComplete && progress.StableSeconds == 0f,
                "Leaving the safe balance range breaks the continuous stability requirement.");
            Feed(progress, upright: true, stable: true);
            Require(progress.StableSeconds < .03f, "Stable landing restarts rather than retaining disconnected partial periods.");
        }

        private static void CheckEarlyRecovery()
        {
            foreach (bool afterExplanation in new[] { false, true })
            {
                var progress = new TipOverTutorialProgress();
                Feed(progress, fallen: true, deployed: true);
                if (afterExplanation) Require(progress.Continue(), "The optional explanation confirmation is retained.");
                for (int frame = 0; frame < 48; frame++) Feed(progress, upright: true, stable: true);
                Require(progress.IsComplete && !progress.CanContinue,
                    "Native recovery before the next instruction is recognized without forcing a second tumble.");
                Feed(progress, upright: true, stable: true);
                Require(!progress.CanContinue, "Early recovery completes without a final A.");
            }
            var invalidRequirement = new TipOverTutorialProgress(float.NaN);
            var shortRequirement = new TipOverTutorialProgress(0f);
            Require(invalidRequirement.RequiredStableSeconds == .8f && shortRequirement.RequiredStableSeconds == .1f,
                "Serialized invalid stable-time settings keep a finite positive requirement.");
        }

        private static void CheckSuspension()
        {
            var progress = new TipOverTutorialProgress();
            Feed(progress, fallen: true, deployed: true);
            progress.Suspend();
            Require(!progress.CanContinue, "A modal UI suppresses the interrupt's pending confirmation.");
            Feed(progress, fallen: true, confirm: true);
            Require(!progress.CanContinue, "Resuming while A is held still requires its release.");
            Feed(progress, fallen: true);
            Require(progress.Continue(), "Suspension retains the real fallen-state observation.");
            Feed(progress, fallen: true);
            Require(progress.Continue(), "Suspension also retains actual prior deployment.");
            for (int frame = 0; frame < 40; frame++) Feed(progress, upright: true, stable: true);
            Require(progress.StableSeconds > .6f, "The fixture has a real partial stable period.");
            progress.Suspend();
            Require(progress.StableSeconds == 0f && !progress.CanContinue && progress.Step == TipOverTutorialStep.Recover,
                "Suspension preserves the active card but restarts its incomplete consecutive stability period.");
            for (int frame = 0; frame < 48; frame++) Feed(progress, upright: true, stable: true);
            Feed(progress, upright: true, stable: true);
            Require(progress.IsComplete && !progress.CanContinue, "Fresh stable landing after resume can finish normally.");
            progress.Suspend();
            Require(progress.IsComplete && !progress.CanContinue, "A modal suspension retains completed recovery but clears A readiness.");
            Feed(progress, upright: true, stable: true, confirm: true);
            Require(!progress.CanContinue, "A held through a modal is not the separate resume confirmation.");
        }

        private static void CheckDeferredFallenObservation()
        {
            foreach (bool reminder in new[] { false, true })
            {
                var progress = new TipOverTutorialProgress(skipIntroduction: reminder);
                progress.ObserveFallen();
                Require(!progress.CanContinue && !progress.IsComplete && progress.StableSeconds == 0f,
                    "A native Fallen observation during a modal retains a fact without arming A or inventing recovery.");
                progress.Suspend();
                for (int frame = 0; frame < 48; frame++) Feed(progress, upright: true, stable: true);
                Require(progress.IsComplete && !progress.CanContinue,
                    "Recovery completed behind a modal is recognized even if the first visible sample is already upright.");
                Feed(progress, upright: true, stable: true);
                Require(!progress.CanContinue, "Deferred recovery releases A without a final confirmation.");
            }
        }

        private static void CheckSecondFall()
        {
            foreach (int interruption in new[] { 0, 1, 2, 3 })
            {
                var progress = AtRecovery();
                for (int frame = 0; frame < 49; frame++) Feed(progress, upright: true, stable: true);
                Require(progress.IsComplete && !progress.CanContinue, "The first recovery reached its internal completed state.");
                Feed(progress, fallen: interruption == 0, upright: interruption == 2,
                    righting: interruption == 1, stable: interruption != 2);
                Require(!progress.IsComplete && !progress.CanContinue && progress.Step == TipOverTutorialStep.Recover
                    && progress.StableSeconds == 0f,
                    "A second fall, another righting animation, unsafe balance or a rolling state cancels pending success.");
                for (int frame = 0; frame < 48; frame++) Feed(progress, upright: true, stable: true);
                Feed(progress, upright: true, stable: true);
                Require(progress.IsComplete && !progress.CanContinue,
                    "A second successful recovery stays in this one interrupt without replaying explanation and deployment.");
                progress.Reset();
                Require(progress.Step == TipOverTutorialStep.Explanation && !progress.CanContinue
                    && progress.StableSeconds == 0f, "An explicit restart clears earlier observed recovery facts.");
            }
        }

        private static void CheckReminder()
        {
            var progress = new TipOverTutorialProgress(skipIntroduction: true);
            Require(progress.Step == TipOverTutorialStep.Recover && !progress.CanContinue,
                "A later tumble opens only the continuous reminder without faking introductory confirmations.");
            for (int frame = 0; frame < 60; frame++) Feed(progress, upright: true, stable: true);
            Require(!progress.IsComplete, "A reminder still needs an actual fallen-state observation.");
            Feed(progress, fallen: true);
            for (int frame = 0; frame < 49; frame++) Feed(progress, upright: true, stable: true);
            Require(progress.IsComplete && !progress.CanContinue,
                "A later real recovery finishes its reminder without an extra A.");
            progress.Reset();
            Require(progress.Step == TipOverTutorialStep.Recover && !progress.CanContinue && progress.StableSeconds == 0f,
                "Restarting a reminder keeps its short form but clears all old recovery facts.");
        }

        private static void CheckInterruptedLanding()
        {
            var progress = new TipOverTutorialProgress();
            progress.ObserveFallen(); Feed(progress, fallen: true);
            progress.ResumeRecovery();
            Require(progress.Step == TipOverTutorialStep.Recover && !progress.CanContinue && progress.StableSeconds == 0f,
                "A native re-fall during the landing guide goes directly back to recovery without stale A readiness.");
            Feed(progress, upright: true, stable: true);
            Require(!progress.IsComplete, "An interrupted landing still needs a fresh stable interval.");
            for (int i = 0; i < 60; i++) Feed(progress, upright: true, stable: true);
            Require(progress.IsComplete, "The new attempt retains the real fallen-state evidence and can finish normally.");
            progress.ResumeRecovery();
            Require(!progress.IsComplete && !progress.CanContinue, "Another fall invalidates a pending completion card.");
        }

        private static void CheckEditableCopy()
        {
            var source = AssetDatabase.LoadAssetAtPath<TutorialTextContent>("Assets/Data/Tutorial/TutorialText.asset");
            Require(source != null && source.TipOver != null, "The scene's shared text asset has its independent tip-over group.");
            var tokens = new TutorialTextTokens().Add("confirm", "A").Add("brace", "→").Add("effort", "←")
                .Add("counter", "→").Add("inertia", "←").Add("cadence", 3f);
            using (var serialized = new SerializedObject(source))
            {
                var chapter = serialized.FindProperty("TipOver");
                Require(chapter != null, "Tip-over copy is a serialized, Inspector-editable group.");
                foreach (FieldInfo field in typeof(TipOverLessonText).GetFields(BindingFlags.Instance | BindingFlags.Public))
                {
                    Require(chapter.FindPropertyRelative(field.Name) != null, "Editable recovery field: " + field.Name);
                    if (field.FieldType == typeof(string))
                        CheckText(tokens.Format((string)field.GetValue(source.TipOver)), field.Name);
                    else if (field.FieldType == typeof(TutorialStepText))
                    {
                        var item = (TutorialStepText)field.GetValue(source.TipOver);
                        Require(item != null, "Recovery card is present: " + field.Name);
                        CheckText(tokens.Format(item.Message), field.Name + " message");
                        CheckText(tokens.Format(item.Detail), field.Name + " detail");
                        CheckText(tokens.Format(item.Action), field.Name + " action");
                        CheckText(tokens.Format(item.ControlGlyph), field.Name + " glyph");
                    }
                }
            }
        }

        private static void CheckText(string text, string field)
        {
            Require(!string.IsNullOrWhiteSpace(text), "Recovery copy is present: " + field);
            Require(text.IndexOf('{') < 0 && text.IndexOf('}') < 0, "Known recovery placeholders resolve: " + field);
            Require(text.Split('\n').Length <= 6, "Recovery copy stays within six short lines before actual layout validation: " + field);
            foreach (char letter in text) Require(letter < '\u4e00' || letter > '\u9fff', "Recovery copy remains English: " + field);
        }

        private static void CheckRecoveryCopy(TutorialTextContent source)
        {
            Require(source != null && source.TipOver != null, "The real authoring asset supplies recovery instructions.");
            var text = source.TipOver;
            var tokens = new TutorialTextTokens().Add("cadence", 3f);
            foreach (string name in new[] { "EffortMessage", "CadenceLostMessage", "DirectionLostMessage", "ReminderMessage" })
            {
                string raw = (string)typeof(TipOverLessonText).GetField(name).GetValue(text);
                string firstLine = tokens.Format(raw).Split('\n')[0];
                Require(firstLine.Contains("3") && firstLine.Contains("second"),
                    name + " puts the approximately three pushes per second instruction in the first main-body line.");
                Require(!raw.Contains("{brace}") && !raw.Contains("{effort}"),
                    name + " explains the physical action without a quantized direction character.");
            }
            string recover = text.Recover.Message;
            Require(tokens.Format(recover).Split('\n')[0].Contains("3"),
                "The initial recovery card also keeps cadence in its main body rather than only the detail row.");
            foreach (string raw in new[] { recover, text.SupportMessage, text.ArmLostMessage, text.SupportStatus,
                text.EffortStatus, text.Recover.Action, text.Recover.Detail, text.ToggleHint })
                Require(!raw.Contains("{brace}") && !raw.Contains("{effort}"),
                    "Recovery-stage text never substitutes a rounded eight-way arrow for continuous support or force direction.");
        }

        private static void Require(bool condition, string message)
        { checks++; if (!condition) throw new InvalidOperationException(message); }
    }
}
