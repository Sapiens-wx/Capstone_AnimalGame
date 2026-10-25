using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using AnimalGame.Tutorial;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    /// <summary>Discovery copy and the real runtime panel, without changing the authored text asset.</summary>
    public static class TutorialDiscoveryUiChecks
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string TextPath = "Assets/Data/Tutorial/TutorialText.asset";
        private static readonly List<string> screenshots = new List<string>();
        private static int checks;
        [Serializable] private sealed class Report { public bool passed; public int checks; public string error; public string[] screenshots; }

        [MenuItem("Animal Game/Validation/Run Tutorial Discovery UI Checks")]
        public static void Run()
        {
            checks = 0; screenshots.Clear();
            try
            {
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Discovery UI checks run in Edit Mode.");
                var text = AssetDatabase.LoadAssetAtPath<TutorialTextContent>(TextPath);
                Require(text != null && text.Discovery != null, "The current text asset supplies discovery copy.");
                string before = EditorJsonUtility.ToJson(text);
                byte[] fileBefore = File.ReadAllBytes(TextPath);
                CheckPanel(text);
                Require(EditorJsonUtility.ToJson(text) == before, "Discovery presentation preserves all authored chapter copy.");
                Require(Convert.ToBase64String(File.ReadAllBytes(TextPath)) == Convert.ToBase64String(fileBefore),
                    "Discovery UI checks never write or bake the shared text asset.");
                WriteReport(true, null);
                Debug.Log("TUTORIAL DISCOVERY UI CHECKS PASSED: " + checks + " assertions.");
            }
            catch (Exception error) { WriteReport(false, error.GetBaseException().ToString()); throw; }
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        [MenuItem("Animal Game/Validation/Run All Four Tutorial Checks")]
        public static void RunAll()
        {
            TutorialDiscoveryRegressionChecks.Run();
            // Includes arm state/UI, content authoring, and the original driving/terrain suites once.
            TutorialArmUiChecks.RunAll();
            Run();
        }

        public static void RunAllBatch()
        {
            try { RunAll(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void CheckPanel(TutorialTextContent text)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            float timeScale = Time.timeScale;
            string captureDirectory = Environment.GetEnvironmentVariable("TUTORIAL_DRIVING_VALIDATION_OUTPUT");
            RenderTexture texture = null;
            try
            {
                Time.timeScale = 1;
                Environment.SetEnvironmentVariable("TUTORIAL_DRIVING_VALIDATION_OUTPUT", OutputDirectory());
                var owner = new GameObject("Discovery tutorial UI check");
                SceneManager.MoveGameObjectToScene(owner, scene);
                var panel = owner.AddComponent<TutorialDialoguePanel>();
                if (panel.Document == null) Invoke(panel, "Awake");
                texture = new RenderTexture(1920, 1080, 24); texture.Create();
                panel.Document.panelSettings.targetTexture = texture;

                var fixture = new GameObject("Inactive discovery UI fixture");
                fixture.SetActive(false);
                SceneManager.MoveGameObjectToScene(fixture, scene);
                var bootstrap = fixture.AddComponent<HeightMapPlayerSceneBootstrap>();
                bootstrap.scanChargeUi = fixture.AddComponent<ScanChargeUI>();
                var driving = fixture.AddComponent<TutorialDrivingLesson>();
                Set(driving, "panel", panel);
                var lesson = fixture.AddComponent<TutorialDiscoveryLesson>();
                var arms = fixture.GetComponent<TutorialArmLesson>();
                Set(arms, "driving", driving);
                Set(lesson, "arms", arms); Set(lesson, "bootstrap", bootstrap); Set(lesson, "tutorialText", text);
                Set(lesson, "scanInput", bootstrap.scanChargeUi);
                var progress = new DiscoveryTutorialProgress();
                Set(lesson, "progress", progress);

                foreach (Vector2 size in new[] { new Vector2(1920, 1080), new Vector2(1280, 720),
                    new Vector2(1440, 1080), new Vector2(2560, 1080) })
                    foreach (DiscoveryTutorialStep step in Enum.GetValues(typeof(DiscoveryTutorialStep)))
                    {
                        typeof(DiscoveryTutorialProgress).GetProperty("Step", Flags).SetValue(progress, step);
                        // Keep stage-specific copy visible; the pure suite checks how real hits earn it.
                        Set(progress, "animalHit", true); Set(progress, "animalVisible", true);
                        Set(progress, "objectHit", true); Set(progress, "objectVisible", true);
                        Set(progress, "chargeStarted", true); Set(progress, "chargeReady", true);
                        foreach (bool valid in new[] { true, false })
                        {
                            Invoke(lesson, "RefreshPanel", valid);
                            panel.SetCollapsed(false); DrivingHelper("Settle", panel, size);
                            string context = "Discovery " + step + " / " + size + " / " + valid;
                            CheckVisibleText(panel.Root, context);
                            Require(panel.Root.Q<Label>("TutorialChapter").text == "05 / 09", "Discovery teaching is chapter five of nine.");
                            string actualControl = (string)typeof(TutorialDialoguePanel).GetField("controlGlyph", Flags).GetValue(panel);
                            CheckStar(panel.Root, actualControl);
                            CheckReminders(panel.Root, step != DiscoveryTutorialStep.Completed);
                            if (valid && size == new Vector2(1920, 1080))
                            {
                                string filename = "discovery-" + step.ToString().ToLowerInvariant() + ".png";
                                DrivingHelper("Capture", panel.Root, texture, filename);
                                screenshots.Add(Path.Combine(OutputDirectory(), filename));
                            }
                            panel.SetCollapsed(true); DrivingHelper("Settle", panel, size);
                            CheckVisibleText(panel.Root, "Collapsed " + context);
                        }

                        if (step == DiscoveryTutorialStep.FindAnimal || step == DiscoveryTutorialStep.ConfirmAnimal
                            || step == DiscoveryTutorialStep.ReadObject)
                        {
                            Set(progress, "animalHit", false); Set(progress, "objectHit", false);
                            Set(progress, "animalVisible", false); Set(progress, "objectVisible", false);
                            Invoke(lesson, "RefreshPanel", true);
                            panel.SetCollapsed(false); DrivingHelper("Settle", panel, size);
                            CheckVisibleText(panel.Root, "Expired discovery marks / " + step + " / " + size);
                        }
                    }

                foreach (string glyph in new[] { "✧", "✦" })
                {
                    TutorialStepText instruction = glyph == "✧" ? text.Discovery.FindAnimal : text.Discovery.ConfirmAnimal;
                    panel.SetContent(text.Discovery.Title, instruction.Message, glyph, instruction.Action,
                        instruction.Detail, text.Discovery.WaitingStatus, 5, 9);
                    panel.SetCollapsed(false); DrivingHelper("Settle", panel, new Vector2(1920, 1080));
                    CheckVisibleText(panel.Root, "Native star badge " + glyph);
                    CheckStar(panel.Root, glyph);
                }
                panel.SetLessonHint(string.Empty);
                panel.SetCollapsed(false); DrivingHelper("Settle", panel, new Vector2(1920, 1080));
                Require(panel.Root.Q("TutorialLessonHint").resolvedStyle.display == DisplayStyle.None,
                    "Clearing scan help leaves earlier chapter cards free of discovery reminders.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                if (texture != null) { texture.Release(); Object.DestroyImmediate(texture); }
                Time.timeScale = timeScale;
                Environment.SetEnvironmentVariable("TUTORIAL_DRIVING_VALIDATION_OUTPUT", captureDirectory);
            }
        }

        private static void CheckStar(VisualElement root, string glyph)
        {
            VisualElement star = root.Q("TutorialControlStar");
            bool expected = glyph == "✧" || glyph == "✦";
            Require(star != null, "The badge provides a native four-point star element.");
            Require((star.resolvedStyle.display != DisplayStyle.None) == expected, "Only hollow/solid star instructions use the vector badge.");
            if (!expected) return;
            Require(Mathf.Abs(star.layout.width - 38) < .1f && Mathf.Abs(star.layout.height - 38) < .1f,
                "The native control star has a complete 38 by 38 layout.");
            Require(string.IsNullOrEmpty(root.Q<Label>("TutorialControl").text),
                "A vector star does not rely on unsupported OS-font star glyphs.");
        }

        private static void CheckReminders(VisualElement root, bool expected)
        {
            Label hint = root.Q<Label>("TutorialLessonHint");
            Require((hint.resolvedStyle.display != DisplayStyle.None) == expected, "The scan gesture reminder follows chapter activity.");
            if (expected)
                Require(hint.worldBound.yMin >= root.Q<Label>("TutorialHeader").worldBound.yMax
                    && hint.worldBound.yMax <= root.Q<Label>("TutorialChapter").worldBound.yMin,
                    "Discovery help fits between the title and chapter readout.");
            Require(root.Q<Label>("TutorialToggleHint").resolvedStyle.display != DisplayStyle.None,
                "The existing D-pad hide/show reminder remains visible independently.");
        }

        private static void CheckVisibleText(VisualElement root, string context)
        {
            foreach (VisualElement element in root.Query<VisualElement>().ToList())
            {
                if (!Visible(element, root)) continue;
                string shown = (element is Label label ? label.text : string.Empty) + element.tooltip;
                foreach (char character in shown)
                    Require(!(character >= '\u3400' && character <= '\u9fff'), context + " uses English teaching copy.");
                foreach (string token in new[] { "{lb}", "{value", "{target" })
                    Require(!shown.Contains(token), context + " formats its known live token: " + token);
                if (element is Label text)
                {
                    // The vector badge intentionally replaces its original glyph with no text.
                    if (text.name == "TutorialControl" && string.IsNullOrEmpty(text.text)
                        && root.Q("TutorialControlStar")?.resolvedStyle.display != DisplayStyle.None) continue;
                    DrivingHelper("CheckTextFits", text, context + " / " + text.name);
                }
            }
            // This uses actual visible strings only; vector stars contribute no font characters.
            DrivingHelper("CheckFont", root);
        }

        private static bool Visible(VisualElement element, VisualElement root)
        {
            for (VisualElement item = element; item != null; item = item.parent)
            {
                if (item.resolvedStyle.display == DisplayStyle.None || item.resolvedStyle.visibility != Visibility.Visible) return false;
                if (item == root) return true;
            }
            return false;
        }

        private static object DrivingHelper(string name, params object[] args) => typeof(TutorialDrivingRegressionChecks)
            .GetMethod(name, Flags | BindingFlags.Static).Invoke(null, args);
        private static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Flags).Invoke(target, args);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
        private static void Require(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
        private static string OutputDirectory()
        {
            string configured = Environment.GetEnvironmentVariable("TUTORIAL_DISCOVERY_VALIDATION_OUTPUT");
            string path = string.IsNullOrEmpty(configured) ? Path.GetFullPath("output/validation/tutorial-discovery") : configured;
            Directory.CreateDirectory(path); return path;
        }
        private static void WriteReport(bool passed, string error) => File.WriteAllText(Path.Combine(OutputDirectory(), "ui-report.json"),
            JsonUtility.ToJson(new Report { passed = passed, checks = checks, error = error, screenshots = screenshots.ToArray() }, true));
    }
}
