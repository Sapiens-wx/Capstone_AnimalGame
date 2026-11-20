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
    /// <summary>Real runtime-card layout and result-screen visibility, with disposable authoring fixtures.</summary>
    public static class TutorialPhotographyUiChecks
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string TextPath = "Assets/Data/Tutorial/TutorialText.asset";
        private static readonly List<string> screenshots = new List<string>();
        private static int checks;

        [Serializable] private sealed class Report
        {
            public bool passed;
            public int checks;
            public string error;
            public string[] screenshots;
        }

        [MenuItem("Animal Game/Validation/Run Tutorial Photography UI Checks")]
        public static void Run()
        {
            checks = 0; screenshots.Clear();
            try
            {
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Photography UI checks run in Edit Mode.");
                var text = AssetDatabase.LoadAssetAtPath<TutorialTextContent>(TextPath);
                Require(text != null && text.Photography != null, "The authored text asset supplies photography copy.");
                string before = EditorJsonUtility.ToJson(text);
                byte[] bytes = File.ReadAllBytes(TextPath);
                CheckEditing(text);
                CheckPanel(text);
                Require(EditorJsonUtility.ToJson(text) == before, "Photography checks preserve every authored lesson and panel string.");
                Require(Convert.ToBase64String(File.ReadAllBytes(TextPath)) == Convert.ToBase64String(bytes),
                    "Photography UI checks never write or bake the shared text asset.");
                WriteReport(true, null);
                Debug.Log("TUTORIAL PHOTOGRAPHY UI CHECKS PASSED: " + checks + " assertions.");
            }
            catch (Exception error) { WriteReport(false, error.GetBaseException().ToString()); throw; }
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        [MenuItem("Animal Game/Validation/Run All Six Tutorial Checks")]
        public static void RunAll()
        {
            TutorialBalanceRegressionChecks.Run();
            TutorialBalanceUiChecks.Run();
            TutorialPhotographyRegressionChecks.Run();
            TutorialPhotoResultObservationChecks.Run();
            TutorialDiscoveryUiChecks.RunAll();
            Run();
        }

        public static void RunAllBatch()
        {
            try { RunAll(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void CheckEditing(TutorialTextContent source)
        {
            var copy = Object.Instantiate(source);
            string temporaryPath = AssetDatabase.GenerateUniqueAssetPath("Assets/Editor/__TutorialPhotographyTextCheck.asset");
            UnityEditor.Editor inspector = null;
            try
            {
                string untouched = ExistingCopy(copy);
                using (var data = new SerializedObject(copy))
                {
                    SerializedProperty chapter = data.FindProperty("Photography");
                    Require(chapter != null, "Photography copy is exposed to Unity's serialized Inspector.");
                    foreach (FieldInfo field in typeof(PhotographyLessonText).GetFields(BindingFlags.Instance | BindingFlags.Public))
                    {
                        SerializedProperty property = chapter.FindPropertyRelative(field.Name);
                        Require(property != null, "Inspector serialization exposes photography field " + field.Name + ".");
                        if (field.FieldType != typeof(TutorialStepText)) continue;
                        foreach (string instruction in new[] { "ControlGlyph", "Action", "Message", "Detail", "CompleteFeedback" })
                            Require(property.FindPropertyRelative(instruction) != null,
                                field.Name + " exposes editable instruction " + instruction + ".");
                    }
                    string original = copy.Photography.Title;
                    Undo.IncrementCurrentGroup();
                    data.FindProperty("Photography.Title").stringValue = "Photography Undo probe";
                    data.ApplyModifiedProperties();
                    Require(copy.Photography.Title == "Photography Undo probe", "Serialized Inspector editing updates photography copy.");
                    Undo.FlushUndoRecordObjects();
                    Undo.PerformUndo();
                    Require(copy.Photography.Title == original, "Photography authoring supports Unity Undo.");
                    Undo.ClearUndo(copy);
                }
                Require(ExistingCopy(copy) == untouched, "Editing photography preserves all existing chapter copy.");
                inspector = UnityEditor.Editor.CreateEditor(copy);
                Require(inspector is TutorialTextContentEditor, "The existing organized tutorial Inspector also authors photography.");
                AssetDatabase.CreateAsset(copy, temporaryPath);
                copy.Photography.Title = "Saved photography probe";
                EditorUtility.SetDirty(copy);
                AssetDatabase.SaveAssetIfDirty(copy);
                Require(File.ReadAllText(temporaryPath).Contains("Saved photography probe"), "Photography edits persist to a Unity asset.");
                Require(ExistingCopy(copy) == untouched, "Persisting photography copy preserves earlier chapters.");
            }
            finally
            {
                if (inspector != null) Object.DestroyImmediate(inspector);
                Undo.ClearUndo(copy);
                if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(copy))) AssetDatabase.DeleteAsset(temporaryPath);
                else if (copy != null) Object.DestroyImmediate(copy);
            }
        }

        private static string ExistingCopy(TutorialTextContent text) => JsonUtility.ToJson(text.Panel)
            + JsonUtility.ToJson(text.Driving) + JsonUtility.ToJson(text.Terrain)
            + JsonUtility.ToJson(text.Balance) + JsonUtility.ToJson(text.Arms) + JsonUtility.ToJson(text.Discovery);

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
                var owner = new GameObject("Photography tutorial UI check");
                SceneManager.MoveGameObjectToScene(owner, scene);
                var panel = owner.AddComponent<TutorialDialoguePanel>();
                if (panel.Document == null) Invoke(panel, "Awake");
                texture = new RenderTexture(1920, 1080, 24); texture.Create();
                panel.Document.panelSettings.targetTexture = texture;

                var fixture = new GameObject("Inactive photography UI fixture");
                fixture.SetActive(false);
                SceneManager.MoveGameObjectToScene(fixture, scene);
                var bootstrap = fixture.AddComponent<HeightMapPlayerSceneBootstrap>();
                bootstrap.scanChargeUi = fixture.AddComponent<ScanChargeUI>();
                var driving = fixture.AddComponent<TutorialDrivingLesson>();
                Set(driving, "panel", panel);
                var discovery = fixture.AddComponent<TutorialDiscoveryLesson>();
                var arms = fixture.GetComponent<TutorialArmLesson>();
                Set(arms, "driving", driving); Set(discovery, "arms", arms);
                var lesson = fixture.AddComponent<TutorialPhotographyLesson>();
                Set(lesson, "discovery", discovery); Set(lesson, "bootstrap", bootstrap);
                // No target provider is needed for presentation. A provider with an absent animal
                // would replace every valid instruction with the missing-target recovery message.
                Set(lesson, "tutorialText", text); Set(lesson, "targets", null);
                var progress = new PhotographyTutorialProgress();
                Set(lesson, "progress", progress);

                foreach (Vector2 size in new[] { new Vector2(1920, 1080), new Vector2(1280, 720),
                    new Vector2(1440, 1080), new Vector2(2560, 1080) })
                {
                    foreach (PhotographyTutorialStep step in Enum.GetValues(typeof(PhotographyTutorialStep)))
                    {
                        if (step == PhotographyTutorialStep.Completed) continue;
                        typeof(PhotographyTutorialProgress).GetProperty("Step", Flags).SetValue(progress, step);
                        foreach (bool valid in new[] { true, false })
                        {
                            Invoke(lesson, "RefreshPanel", valid);
                            panel.SetVisible(true); panel.SetCollapsed(false);
                            DrivingHelper("Settle", panel, size);
                            string context = "Photography " + step + " / " + size + " / " + valid;
                            CheckVisibleText(panel.Root, context);
                            panel.SetContinuePrompt(valid, text.Panel.ContinuePrompt);
                            DrivingHelper("Settle", panel, size);
                            CheckVisibleText(panel.Root, context + " / ready A prompt");
                            Label prompt = panel.Root.Q<Label>("TutorialContinuePrompt");
                            Require((prompt.resolvedStyle.display != DisplayStyle.None) == valid,
                                "Available photo confirmations expose the A hint within the existing card.");
                            if (valid) Require(prompt.text == text.Panel.ContinuePrompt,
                                "The photo continuation uses the Inspector-editable prompt.");
                            Require(panel.Root.Q<Label>("TutorialChapter").text == "06 / 09", "Photography teaching is chapter six of nine.");
                            Require(panel.Root.Q<Label>("TutorialTitle").text == text.Photography.Title,
                                "Photography uses the editable chapter title.");
                            Require(panel.Root.Q<Label>("TutorialMessage").text == Tokens().Format(text.Photography.GetStep(step).Message),
                                "The fixture displays the actual " + step + " instruction rather than a recovery override.");
                            Require(panel.Root.Q<Label>("TutorialAction").text == Tokens().Format(text.Photography.GetStep(step).Action),
                                "The fixture displays the actual " + step + " action.");
                            CheckReminders(panel.Root, step != PhotographyTutorialStep.Completed);
                            if (valid && size == new Vector2(1920, 1080))
                            {
                                string filename = "photography-" + step.ToString().ToLowerInvariant() + ".png";
                                DrivingHelper("Capture", panel.Root, texture, filename);
                                screenshots.Add(Path.Combine(OutputDirectory(), filename));
                            }
                            panel.SetCollapsed(true); DrivingHelper("Settle", panel, size);
                            CheckVisibleText(panel.Root, "Collapsed " + context);
                        }
                    }
                    CheckRecoveryCopy(panel, text, size);
                    CheckResultVisibility(lesson, panel, size);
                }
                typeof(PhotographyTutorialProgress).GetProperty("Step", Flags).SetValue(progress, PhotographyTutorialStep.Completed);
                Set(lesson, "chapterConfirmed", false); Invoke(lesson, "RefreshPanel", true);
                DrivingHelper("Settle", panel, new Vector2(1920, 1080));
                Require(!panel.IsVisible && !panel.CanContinue,
                    "Completed photo practice hides its card without requiring an additional chapter confirmation.");
                CheckNativePhotoVisibility(fixture, bootstrap, panel, lesson, discovery);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                if (texture != null) { texture.Release(); Object.DestroyImmediate(texture); }
                Time.timeScale = timeScale;
                Environment.SetEnvironmentVariable("TUTORIAL_DRIVING_VALIDATION_OUTPUT", captureDirectory);
            }
        }

        private static void CheckRecoveryCopy(TutorialDialoguePanel panel, TutorialTextContent text, Vector2 size)
        {
            PhotographyLessonText copy = text.Photography;
            string[] messages = { copy.TargetMissingMessage, copy.NeedRescanMessage,
                copy.RetryPhotoMessage, copy.CapturedMessage };
            string[] statuses = { copy.TargetMissingStatus, copy.NeedRescanStatus,
                copy.RetryPhotoStatus, copy.CapturedStatus };
            TutorialTextTokens tokens = Tokens();
            for (int index = 0; index < messages.Length; index++)
            {
                panel.SetContent(copy.Title, tokens.Format(messages[index]), "RB", "Try again", "Follow the prompt.", statuses[index], 6, 9);
                panel.SetLessonHint(tokens.Format(copy.ToggleHint));
                panel.SetVisible(true); panel.SetCollapsed(false);
                DrivingHelper("Settle", panel, size);
                CheckVisibleText(panel.Root, "Photography recovery copy " + index + " / " + size);
            }
        }

        private static void CheckResultVisibility(TutorialPhotographyLesson lesson, TutorialDialoguePanel panel, Vector2 size)
        {
            UIDocument originalDocument = panel.Document;
            VisualElement originalRoot = panel.Root;
            foreach (bool collapsed in new[] { false, true })
            {
                panel.SetCollapsed(collapsed);
                Invoke(lesson, "UpdatePanelVisibility", false, false);
                DrivingHelper("Settle", panel, size);
                Require(panel.IsVisible && panel.Root.resolvedStyle.display != DisplayStyle.None,
                    "The active photo lesson displays its card or chosen collapsed tab outside results.");
                Invoke(lesson, "UpdatePanelVisibility", false, true);
                DrivingHelper("Settle", panel, size);
                Require(!panel.IsVisible && panel.Root.resolvedStyle.display == DisplayStyle.None,
                    "A photo settlement hides the entire tutorial document, including its collapsed tab.");
                Require(panel.IsCollapsed == collapsed, "Showing a result preserves the player's D-pad collapse choice.");
                Invoke(lesson, "UpdatePanelVisibility", false, false);
                DrivingHelper("Settle", panel, size);
                Require(panel.IsVisible && panel.Root.resolvedStyle.display != DisplayStyle.None && panel.IsCollapsed == collapsed,
                    "Closing results restores the previous tutorial visibility and collapse state.");
                Invoke(lesson, "UpdatePanelVisibility", true, false);
                DrivingHelper("Settle", panel, size);
                Require(!panel.IsVisible && panel.Root.resolvedStyle.display == DisplayStyle.None,
                    "Other modal screens also hide the photo tutorial.");
            }
            Invoke(lesson, "UpdatePanelVisibility", false, false);
            Require(panel.Document == originalDocument && panel.Root == originalRoot,
                "Result visibility changes reuse the private UI document and preserve layout resources.");
        }

        private static void CheckNativePhotoVisibility(GameObject fixture, HeightMapPlayerSceneBootstrap bootstrap,
            TutorialDialoguePanel panel, TutorialPhotographyLesson lesson, TutorialDiscoveryLesson discovery)
        {
            HeightMapPlayerSceneBootstrap previousBootstrap = HeightMapPlayerSceneBootstrap.inst;
            var photo = fixture.AddComponent<PhotoModeController>();
            var result = fixture.AddComponent<PhotoResultView>();
            FieldInfo photoState = typeof(PhotoModeController).GetField("state", Flags);
            try
            {
                bootstrap.photoMode = photo; bootstrap.photoResultView = result;
                HeightMapPlayerSceneBootstrap.inst = bootstrap;
                var completedDiscovery = new DiscoveryTutorialProgress();
                typeof(DiscoveryTutorialProgress).GetProperty("Step", Flags).SetValue(completedDiscovery, DiscoveryTutorialStep.Completed);
                Set(discovery, "progress", completedDiscovery); Set(discovery, "hasStarted", true);
                Set(lesson, "targets", fixture.GetComponent<TutorialDiscoveryTargets>());
                Set(lesson, "hasStarted", true); Set(lesson, "chapterConfirmed", false);
                Set(result, "showing", true);
                Invoke(lesson, "LateUpdate");
                Require(!lesson.IsComplete, "The accepted photo result must close before the next chapter can begin.");
                Set(result, "showing", false);
                Invoke(lesson, "LateUpdate");
                Require(lesson.IsComplete, "Closing the accepted result automatically settles photography without A.");
                Set(result, "showing", true);
                Invoke(lesson, "LateUpdate");
                Require(lesson.IsComplete && !panel.CanContinue,
                    "A later native photo result cannot revoke settled photography or reset following chapters.");
                Set(result, "showing", false);
                photoState.SetValue(photo, Enum.Parse(photoState.FieldType, "Active"));
                panel.SetCollapsed(false); panel.SetPhotoModeVisible(false); panel.SetVisible(true);
                DrivingHelper("Settle", panel, new Vector2(1920, 1080));
                Require(panel.Root.resolvedStyle.display == DisplayStyle.None,
                    "Earlier lessons preserve their existing hidden presentation during native photo mode.");
                panel.SetPhotoModeVisible(true);
                DrivingHelper("Settle", panel, new Vector2(1920, 1080));
                Require(panel.Root.resolvedStyle.display != DisplayStyle.None,
                    "The photography lesson explicitly opts its actual runtime card into native photo mode.");
                Set(result, "showing", true); panel.SetVisible(true);
                DrivingHelper("Settle", panel, new Vector2(1920, 1080));
                Require(panel.Root.resolvedStyle.display == DisplayStyle.None,
                    "The real native result-visible flag overrides even the photography opt-in.");
                Set(result, "showing", false); panel.SetVisible(true);
                DrivingHelper("Settle", panel, new Vector2(1920, 1080));
                Require(panel.Root.resolvedStyle.display != DisplayStyle.None,
                    "The card resumes framing help after the native result screen closes.");
            }
            finally
            {
                Set(result, "showing", false);
                photoState.SetValue(photo, Enum.Parse(photoState.FieldType, "Inactive"));
                HeightMapPlayerSceneBootstrap.inst = previousBootstrap;
                panel.SetPhotoModeVisible(false); panel.SetVisible(true);
            }
        }

        private static void CheckReminders(VisualElement root, bool expected)
        {
            Label hint = root.Q<Label>("TutorialLessonHint");
            Require((hint.resolvedStyle.display != DisplayStyle.None) == expected,
                "Only active photography steps show the extra photo-control reminder.");
            if (expected)
                Require(hint.worldBound.yMin >= root.Q<Label>("TutorialHeader").worldBound.yMax
                    && hint.worldBound.yMax <= root.Q<Label>("TutorialTitle").worldBound.yMin,
                    "Photo controls fit between the header and lesson title.");
            Require(root.Q<Label>("TutorialToggleHint").resolvedStyle.display != DisplayStyle.None,
                "The existing D-pad hide/show help remains independently visible.");
        }

        private static void CheckVisibleText(VisualElement root, string context)
        {
            foreach (VisualElement element in root.Query<VisualElement>().ToList())
            {
                if (!Visible(element, root)) continue;
                string shown = (element is Label label ? label.text : string.Empty) + element.tooltip;
                foreach (char character in shown)
                    Require(!(character >= '\u3400' && character <= '\u9fff'), context + " uses English teaching copy.");
                foreach (string token in new[] { "{photo}", "{rb}", "{save}", "{cancel}", "{rt}", "{lt}", "{lb}", "{value", "{target" })
                    Require(!shown.Contains(token), context + " formats known live token " + token + ".");
                if (!(element is Label text)) continue;
                if (text.name == "TutorialControl" && string.IsNullOrEmpty(text.text)
                    && root.Q("TutorialControlStar")?.resolvedStyle.display != DisplayStyle.None) continue;
                DrivingHelper("CheckTextFits", text, context + " / " + text.name);
            }
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

        private static TutorialTextTokens Tokens()
        {
            string photo = "X", rb = "RB", save = "Y", cancel = "B", rt = "RT", lt = "LT", lb = "LB";
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Gamepad.current != null && UnityEngine.InputSystem.Gamepad.current.layout.Contains("Dual"))
            { photo = "□"; rb = "R1"; save = "△"; cancel = "○"; rt = "R2"; lt = "L2"; lb = "L1"; }
#endif
            return new TutorialTextTokens().Add("photo", photo).Add("rb", rb).Add("save", save)
                .Add("cancel", cancel).Add("rt", rt).Add("lt", lt).Add("lb", lb).Add("value", .5f).Add("target", 1f);
        }

        private static object DrivingHelper(string name, params object[] args) => typeof(TutorialDrivingRegressionChecks)
            .GetMethod(name, Flags | BindingFlags.Static).Invoke(null, args);
        private static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Flags).Invoke(target, args);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
        private static void Require(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
        private static string OutputDirectory()
        {
            string configured = Environment.GetEnvironmentVariable("TUTORIAL_PHOTOGRAPHY_VALIDATION_OUTPUT");
            string path = string.IsNullOrEmpty(configured) ? Path.GetFullPath("output/validation/tutorial-photography") : configured;
            Directory.CreateDirectory(path); return path;
        }
        private static void WriteReport(bool passed, string error) => File.WriteAllText(Path.Combine(OutputDirectory(), "ui-report.json"),
            JsonUtility.ToJson(new Report { passed = passed, checks = checks, error = error, screenshots = screenshots.ToArray() }, true));
    }
}
