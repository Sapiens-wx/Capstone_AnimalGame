using System;
using System.IO;
using System.Reflection;
using AnimalGame.MapTest;
using AnimalGame.Tutorial;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    /// <summary>Actual runtime-panel layout and editable-copy checks in a disposable PreviewScene.</summary>
    public static class TutorialArmUiChecks
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string TextPath = "Assets/Data/Tutorial/TutorialText.asset";
        private static int checks;

        [Serializable] private sealed class Report { public bool passed; public int checks; public string error; }

        [MenuItem("Animal Game/Validation/Run Tutorial Arm UI Checks")]
        public static void Run()
        {
            checks = 0;
            try
            {
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Arm UI checks run in Edit Mode.");
                TutorialTextContent text = BakeArmsCopy();
                CheckEditing(text);
                CheckPanel(text);
                WriteReport(true, null);
                Debug.Log("TUTORIAL ARM UI CHECKS PASSED: " + checks + " assertions.");
            }
            catch (Exception error) { WriteReport(false, error.GetBaseException().ToString()); throw; }
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        [MenuItem("Animal Game/Validation/Run All Three Tutorial UI Checks")]
        public static void RunAll()
        {
            TutorialArmRegressionChecks.Run();
            // This validation already runs the original driving and terrain suites once each.
            TutorialTextContentValidation.Run();
            Run();
        }

        public static void RunAllBatch()
        {
            try { RunAll(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static TutorialTextContent BakeArmsCopy()
        {
            TutorialTextContent text = AssetDatabase.LoadAssetAtPath<TutorialTextContent>(TextPath);
            Require(text != null, "The existing tutorial copy asset is available.");
            string panel = JsonUtility.ToJson(text.Panel), driving = JsonUtility.ToJson(text.Driving), terrain = JsonUtility.ToJson(text.Terrain);
            Undo.RecordObject(text, "Add mechanical arm tutorial copy");
            if (text.Arms == null) text.Arms = new ArmsLessonText();
            EditorUtility.SetDirty(text);
            AssetDatabase.SaveAssetIfDirty(text);
            Require(File.ReadAllText(TextPath).Contains("  Arms:"), "The new Arms section is serialized into the existing copy asset.");
            Require(panel == JsonUtility.ToJson(text.Panel) && driving == JsonUtility.ToJson(text.Driving)
                && terrain == JsonUtility.ToJson(text.Terrain), "Baking Arms preserves all current panel, driving and terrain author copy.");
            return text;
        }

        private static void CheckEditing(TutorialTextContent source)
        {
            var copy = Object.Instantiate(source);
            string temporaryPath = AssetDatabase.GenerateUniqueAssetPath("Assets/Editor/__TutorialArmTextCheck.asset");
            try
            {
                string original = copy.Arms.Title;
                Undo.IncrementCurrentGroup();
                Undo.RecordObject(copy, "Arm copy Undo check");
                copy.Arms.Title = "Undo probe";
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Require(copy.Arms.Title == original, "Editing arm copy supports Unity Undo.");
                Undo.ClearUndo(copy);
                AssetDatabase.CreateAsset(copy, temporaryPath);
                copy.Arms.Title = "Saved arm copy probe";
                EditorUtility.SetDirty(copy);
                AssetDatabase.SaveAssetIfDirty(copy);
                Require(File.ReadAllText(temporaryPath).Contains("Saved arm copy probe"), "Edited arm copy is persisted to an asset.");
                Require(JsonUtility.ToJson(source.Driving) == JsonUtility.ToJson(copy.Driving)
                    && JsonUtility.ToJson(source.Terrain) == JsonUtility.ToJson(copy.Terrain),
                    "Arm authoring leaves copied driving and terrain content intact.");
            }
            finally
            {
                Undo.ClearUndo(copy);
                if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(copy))) AssetDatabase.DeleteAsset(temporaryPath);
                else if (copy != null) Object.DestroyImmediate(copy);
            }
        }

        private static void CheckPanel(TutorialTextContent text)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            float timeScale = Time.timeScale;
            string previousCaptureDirectory = Environment.GetEnvironmentVariable("TUTORIAL_DRIVING_VALIDATION_OUTPUT");
            RenderTexture texture = null;
            try
            {
                Environment.SetEnvironmentVariable("TUTORIAL_DRIVING_VALIDATION_OUTPUT", Path.GetFullPath("output/validation/tutorial-arms"));
                Time.timeScale = 1;
                var owner = new GameObject("Arm tutorial UI check");
                SceneManager.MoveGameObjectToScene(owner, scene);
                var panel = owner.AddComponent<TutorialDialoguePanel>();
                if (panel.Document == null) Invoke(panel, "Awake");
                texture = new RenderTexture(1920, 1080, 24); texture.Create();
                panel.Document.panelSettings.targetTexture = texture;
                var fixture = new GameObject("Inactive arm UI fixture");
                fixture.SetActive(false);
                SceneManager.MoveGameObjectToScene(fixture, scene);
                var bootstrap = fixture.AddComponent<HeightMapPlayerSceneBootstrap>();
                var driving = fixture.AddComponent<TutorialDrivingLesson>();
                Set(driving, "panel", panel);
                var lesson = fixture.AddComponent<TutorialArmLesson>();
                Set(lesson, "driving", driving); Set(lesson, "bootstrap", bootstrap); Set(lesson, "tutorialText", text);
                var progress = new ArmTutorialProgress();
                Require(progress.Step == ArmTutorialStep.Deploy, "Arm teaching starts with deployment at the player's current position.");
                Set(lesson, "progress", progress);

                foreach (Vector2 size in new[] { new Vector2(1920, 1080), new Vector2(1280, 720),
                    new Vector2(1440, 1080), new Vector2(2560, 1080) })
                    foreach (ArmTutorialStep step in Enum.GetValues(typeof(ArmTutorialStep)))
                    {
                        if (step == ArmTutorialStep.Completed) continue;
                        typeof(ArmTutorialProgress).GetProperty("Step", Flags).SetValue(progress, step);
                        foreach (bool valid in new[] { true, false })
                        {
                            Invoke(lesson, "RefreshPanel", valid);
                            panel.SetCollapsed(false);
                            DrivingHelper("Settle", panel, size);
                            DrivingHelper("CheckVisibleEnglishText", panel.Root, "Arms " + step + " / " + size + " / " + valid);
                            Require(panel.Root.Q<Label>("TutorialChapter").text == "04 / 09", "Arm teaching is chapter four of nine.");
                            Label hint = panel.Root.Q<Label>("TutorialLessonHint");
                            bool expectsHint = step != ArmTutorialStep.Completed;
                            Require((hint.resolvedStyle.display != DisplayStyle.None) == expectsHint,
                                "Only active arm-operation stages show the additional L3 reminder.");
                            if (expectsHint)
                            {
                                Require(hint.text == text.Arms.ToggleHint, "The arm reminder uses editable chapter copy.");
                                Require(hint.worldBound.yMin >= panel.Root.Q<Label>("TutorialHeader").worldBound.yMax
                                    && hint.worldBound.yMax <= panel.Root.Q<Label>("TutorialTitle").worldBound.yMin,
                                    "The L3 reminder does not cover the header or lesson title.");
                                Require(hint.worldBound.yMax < panel.Root.Q<Label>("TutorialToggleHint").worldBound.yMin,
                                    "The L3 reminder is independent of the existing D-pad help row.");
                            }
                            if (valid && size == new Vector2(1920, 1080))
                                DrivingHelper("Capture", panel.Root, texture, "arms-" + step.ToString().ToLowerInvariant() + ".png");
                            panel.SetCollapsed(true); DrivingHelper("Settle", panel, size);
                            DrivingHelper("CheckVisibleEnglishText", panel.Root, "Collapsed arms " + step + " / " + size);
                        }
                    }

                typeof(ArmTutorialProgress).GetProperty("Step", Flags).SetValue(progress, ArmTutorialStep.Completed);
                Invoke(lesson, "RefreshPanel", true);
                Require(!panel.IsVisible && !panel.CanContinue,
                    "Terminal arms state hides its card without asking for an extra chapter confirmation.");
                panel.SetVisible(true);
                panel.SetCollapsed(false);
                UIDocument document = panel.Document;
                VisualElement root = panel.Root;
                panel.SetLessonHint("L3 · Deploy / Retract");
                panel.SetLessonHint(string.Empty);
                DrivingHelper("Settle", panel, new Vector2(1920, 1080));
                Require(root.Q("TutorialLessonHint").resolvedStyle.display == DisplayStyle.None,
                    "Clearing the chapter reminder leaves earlier tutorial cards free of arm controls.");
                Require(panel.Document == document && panel.Root == root, "Changing chapter reminders never rebuilds the UI document.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                if (texture != null) { texture.Release(); Object.DestroyImmediate(texture); }
                Time.timeScale = timeScale;
                Environment.SetEnvironmentVariable("TUTORIAL_DRIVING_VALIDATION_OUTPUT", previousCaptureDirectory);
            }
        }

        private static object DrivingHelper(string name, params object[] args) =>
            typeof(TutorialDrivingRegressionChecks).GetMethod(name, Flags | BindingFlags.Static).Invoke(null, args);
        private static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Flags).Invoke(target, args);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
        private static void Require(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
        private static void WriteReport(bool passed, string error)
        {
            string directory = Path.GetFullPath("output/validation/tutorial-arms");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "ui-report.json"), JsonUtility.ToJson(new Report { passed = passed, checks = checks, error = error }, true));
        }
    }
}
