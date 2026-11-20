using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AnimalGame.Tutorial;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    /// <summary>Checks actual task-panel rendering and editable copy for the contextual recovery lesson.</summary>
    public static class TutorialTipOverUiChecks
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static int checks;
        private static readonly List<string> screenshots = new();
        [Serializable] private sealed class Report { public bool passed; public int checks; public string error; public string[] screenshots; }

        [MenuItem("Animal Game/Validation/Run Tutorial Tip-over UI Checks")]
        public static void Run()
        {
            checks = 0; screenshots.Clear();
            try
            {
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "UI checks run outside Play Mode.");
                const string assetPath = "Assets/Data/Tutorial/TutorialText.asset";
                var text = AssetDatabase.LoadAssetAtPath<TutorialTextContent>(assetPath);
                Require(text != null && text.TipOver != null, "The shared authoring asset supplies the contextual recovery text.");
                byte[] original = File.ReadAllBytes(assetPath); string before = EditorJsonUtility.ToJson(text);
                CheckEditing(text); CheckPanel(text);
                Require(before == EditorJsonUtility.ToJson(text) && Convert.ToBase64String(original)
                    == Convert.ToBase64String(File.ReadAllBytes(assetPath)), "UI validation preserves all original tutorial copy and asset bytes.");
                Write(true, null); Debug.Log("TUTORIAL TIPOVER UI CHECKS PASSED: " + checks + " assertions.");
            }
            catch (Exception error) { Write(false, error.GetBaseException().ToString()); throw; }
        }
        public static void RunBatch()
        { try { Run(); EditorApplication.Exit(0); } catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); } }
        public static void RunAllBatch()
        { try { TutorialTipOverRegressionChecks.Run(); Run(); EditorApplication.Exit(0); } catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); } }

        private static void CheckEditing(TutorialTextContent source)
        {
            var copy = Object.Instantiate(source);
            string other = OtherCopy(copy), temporary = AssetDatabase.GenerateUniqueAssetPath("Assets/Editor/__TutorialTipOverTextCheck.asset");
            UnityEditor.Editor inspector = null;
            try
            {
                using (var serialized = new SerializedObject(copy))
                {
                    var group = serialized.FindProperty("TipOver");
                    Require(group != null, "Recovery text is an independent Inspector group rather than a numbered chapter.");
                    foreach (FieldInfo field in copy.TipOver.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                    {
                        var property = group.FindPropertyRelative(field.Name);
                        Require(property != null, "Editable contextual text: " + field.Name);
                        if (field.FieldType == typeof(TutorialStepText))
                            foreach (string child in new[] { "ControlGlyph", "Action", "Message", "Detail", "CompleteFeedback" })
                                Require(property.FindPropertyRelative(child) != null, field.Name + " exposes " + child);
                    }
                    string title = copy.TipOver.Title;
                    Undo.IncrementCurrentGroup(); group.FindPropertyRelative("Title").stringValue = "Recovery Undo probe";
                    serialized.ApplyModifiedProperties(); Undo.FlushUndoRecordObjects();
                    Require(copy.TipOver.Title == "Recovery Undo probe", "Inspector edits update the recovery chapter.");
                    Undo.PerformUndo(); Require(copy.TipOver.Title == title, "Recovery copy supports Undo."); Undo.ClearUndo(copy);
                    serialized.Update(); group.FindPropertyRelative("Title").stringValue = "Saved recovery probe";
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                Require(OtherCopy(copy) == other, "Editing recovery preserves every existing chapter and task-panel text.");
                inspector = UnityEditor.Editor.CreateEditor(copy);
                Require(inspector is TutorialTextContentEditor, "The organized text Inspector supports contextual recovery.");
                AssetDatabase.CreateAsset(copy, temporary); EditorUtility.SetDirty(copy); AssetDatabase.SaveAssetIfDirty(copy);
                Require(File.ReadAllText(temporary).Contains("Saved recovery probe"), "Inspector changes save in a disposable asset.");
            }
            finally
            {
                if (inspector != null) Object.DestroyImmediate(inspector);
                Undo.ClearUndo(copy);
                if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(copy))) AssetDatabase.DeleteAsset(temporary);
                else if (copy != null) Object.DestroyImmediate(copy);
            }
        }
        private static string OtherCopy(TutorialTextContent text) => JsonUtility.ToJson(text.Panel) + JsonUtility.ToJson(text.Driving)
            + JsonUtility.ToJson(text.Terrain) + JsonUtility.ToJson(text.Balance) + JsonUtility.ToJson(text.Arms)
            + JsonUtility.ToJson(text.Discovery) + JsonUtility.ToJson(text.Recycling) + JsonUtility.ToJson(text.Photography) + JsonUtility.ToJson(text.Refit);

        private static void CheckPanel(TutorialTextContent text)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            float oldTimeScale = Time.timeScale; RenderTexture texture = null;
            try
            {
                Time.timeScale = 1f;
                var owner = new GameObject("Tip-over UI validation"); SceneManager.MoveGameObjectToScene(owner, scene);
                var panel = owner.AddComponent<TutorialDialoguePanel>();
                if (panel.Document == null) Invoke(panel, "Awake");
                texture = new RenderTexture(1920, 1080, 24); texture.Create(); panel.Document.panelSettings.targetTexture = texture;
                panel.SetPanelText(text.Panel);
                foreach (Vector2 size in new[] { new Vector2(1920,1080), new Vector2(1280,720), new Vector2(1440,1080), new Vector2(2560,1080) })
                foreach (string arrow in new[] { "↑", "→", "↓", "←" })
                {
                    var tokens = new TutorialTextTokens().Add("confirm", "A").Add("brace", arrow).Add("effort", Opposite(arrow))
                        .Add("counter", arrow).Add("inertia", Opposite(arrow)).Add("cadence", 3f);
                    foreach (FieldInfo field in text.TipOver.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                    {
                        if (field.FieldType != typeof(TutorialStepText)) continue;
                        var step = (TutorialStepText)field.GetValue(text.TipOver);
                        Show(panel, text, tokens.Format(step.Message), tokens.Format(step.ControlGlyph),
                            tokens.Format(step.Action), tokens.Format(step.Detail), tokens.Format(text.TipOver.WaitingStatus));
                        Settle(panel, size); CheckText(panel.Root, field.Name + " / " + arrow + " / " + size);
                        Require(panel.Root.Q<Label>("TutorialChapter").text == string.Empty,
                            "Contextual recovery hides the numbered mainline chapter rather than consuming a chapter number.");
                        Require(panel.Root.Q<Label>("TutorialCurrentLesson").text == text.TipOver.ContextLabel,
                            "The existing lesson eyebrow identifies recovery as a contextual tutorial.");
                        panel.SetContinuePrompt(true, text.TipOver.ResumePrompt); Settle(panel, size);
                        CheckText(panel.Root, "Confirmation / " + field.Name + " / " + size);
                        if (size == new Vector2(1920,1080) && arrow == "→")
                            Capture(panel.Root, texture, "tipover-" + field.Name.ToLowerInvariant() + ".png");
                        panel.SetCollapsed(true); Settle(panel, size); CheckText(panel.Root, "Collapsed / " + field.Name);
                    }
                    foreach (FieldInfo field in text.TipOver.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                    {
                        if (field.FieldType != typeof(string) || !(field.Name.EndsWith("Message") || field.Name.EndsWith("Status"))) continue;
                        string value = tokens.Format((string)field.GetValue(text.TipOver));
                        if (string.IsNullOrWhiteSpace(value)) continue;
                        Show(panel, text, field.Name.EndsWith("Message") ? value : tokens.Format(text.TipOver.Recover.Message),
                            "L / R", tokens.Format(text.TipOver.Recover.Action), tokens.Format(text.TipOver.Recover.Detail),
                            field.Name.EndsWith("Status") ? value : text.TipOver.WaitingStatus);
                        Settle(panel, size); CheckText(panel.Root, field.Name + " / " + arrow + " / " + size);
                    }
                    if (arrow == "→") CheckContinuousGuides(panel, text, tokens, size);
                    Show(panel, text, tokens.Format(text.TipOver.InertiaMessage), "R", tokens.Format(text.TipOver.InertiaAction),
                        tokens.Format(text.TipOver.InertiaDetail), tokens.Format(text.TipOver.InertiaStatus));
                    panel.SetContent(tokens.Format(text.TipOver.InertiaTitle), tokens.Format(text.TipOver.InertiaMessage), "R",
                        tokens.Format(text.TipOver.InertiaAction), tokens.Format(text.TipOver.InertiaDetail), tokens.Format(text.TipOver.InertiaStatus));
                    panel.SetContextLabel(text.TipOver.ContextLabel);
                    panel.SetLessonHint(tokens.Format(text.TipOver.InertiaStatus));
                    panel.SetContinuePrompt(true, text.TipOver.InertiaContinuePrompt);
                    Settle(panel, size); CheckText(panel.Root, "Inertia reading / " + arrow + " / " + size);
                    if (size == new Vector2(1920,1080) && arrow == "→")
                        Capture(panel.Root, texture, "tipover-inertia-reading.png");
                    Time.timeScale = 0f; panel.SetTutorialPauseVisible(false); Settle(panel, size);
                    Require(panel.Root.resolvedStyle.display == DisplayStyle.None,
                        "A normal external time-scale pause continues to hide the task panel.");
                    panel.SetTutorialPauseVisible(true); Settle(panel, size);
                    Require(panel.ShowDuringTutorialPause && panel.Root.resolvedStyle.display == DisplayStyle.None,
                        "A preview panel cannot authorize showing over a pause without the real scene-owned recovery owner.");
                    var snapshot = panel.CaptureContent(); panel.RestoreContent(snapshot); Settle(panel, size);
                    Require(!panel.ShowDuringTutorialPause && panel.Root.resolvedStyle.display == DisplayStyle.None,
                        "Restoring ordinary task content cannot retain permission to show over an unrelated pause.");
                    Time.timeScale = 1f; panel.SetTutorialPauseVisible(false);
                }
                panel.SetContent(text.Terrain.Title, "Resume the original lesson.", "L", "Follow the marker", "Original target", "Ready", 2, 9);
                Settle(panel, new Vector2(1920,1080));
                Require(panel.Root.Q<Label>("TutorialChapter").text == "02 / 09",
                    "Ordinary content clears the contextual label and restores its unchanged chapter number.");
                panel.SetContinuePrompt(false);
                Require(!panel.CanContinue, "An active recovery operation does not claim an unfinished A confirmation.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                if (texture != null) { texture.Release(); Object.DestroyImmediate(texture); }
                Time.timeScale = oldTimeScale;
            }
        }
        private static void CheckContinuousGuides(TutorialDialoguePanel panel, TutorialTextContent text,
            TutorialTextTokens tokens, Vector2 size)
        {
            foreach (float degrees in new[] { 17f, 73f })
            {
                float angle = degrees * Mathf.Deg2Rad;
                Vector2 brace = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 effort = -brace;
                Vector2 body = new Vector2(size.x * .45f, size.y * .5f);
                Vector2 point = body + new Vector2(brace.x, -brace.y) * 90f;
                Show(panel, text, tokens.Format(text.TipOver.EffortMessage), "R", text.TipOver.EffortStatus,
                    text.TipOver.Recover.Detail, text.TipOver.EffortStatus);
                panel.SetRecoveryDirectionGuide(true, false, brace, effort, effort, true, true, body, point);
                Settle(panel, size);
                Require(panel.IsRecoveryDirectionGuideVisible && !panel.RecoveryGuideIsBracing,
                    "Continuous effort guidance is visible while teaching the right-stick action.");
                Require(Vector2.Angle(panel.RecoveryStickTargetDirection, effort) < .05f,
                    degrees + " degrees: the stick target preserves the exact required angle instead of rounding to an eight-way character.");
                Require(Visible(panel.Root.Q("TutorialRecoveryStickGuide"), panel.Root)
                    && Visible(panel.Root.Q("TutorialRecoveryInwardGuide"), panel.Root),
                    "The continuous stick display and the balance-point-to-body arrow both render in the actual panel tree.");
                string firstLine = panel.Root.Q<Label>("TutorialMessage").text.Split('\n')[0];
                Require(firstLine.Contains("3") && firstLine.Contains("second"),
                    "The push cadence remains in the first line of the main tutorial body.");
                CheckText(panel.Root, "Continuous effort / " + degrees + " / " + size);
                if (size == new Vector2(1920, 1080))
                    Capture(panel.Root, texture: panel.Document.panelSettings.targetTexture,
                        "tipover-continuous-" + degrees.ToString("F0") + ".png");

                panel.SetRecoveryDirectionGuide(true, true, brace, effort, brace, false, false, body, point);
                Settle(panel, size);
                Require(panel.RecoveryGuideIsBracing && Vector2.Angle(panel.RecoveryStickTargetDirection, brace) < .05f,
                    "The left-stick support target uses the continuous outward brace direction.");
                Require(!Visible(panel.Root.Q("TutorialRecoveryInwardGuide"), panel.Root),
                    "The inward force arrow waits until support has been established.");
                panel.SetCollapsed(true); Settle(panel, size);
                Require(!panel.IsRecoveryDirectionGuideVisible, "Collapsing the task removes temporary recovery guidance.");
                panel.SetCollapsed(false);
                panel.SetRecoveryDirectionGuide(true, false, brace, effort, effort, true, true, body, point);
                panel.SetVisible(false); Settle(panel, size);
                Require(!panel.IsRecoveryDirectionGuideVisible, "Hiding for another modal removes the temporary recovery guide.");
                panel.SetVisible(true);
                panel.SetRecoveryDirectionGuide(true, false, brace, effort, effort, true, true, body, point);
                var snapshot = panel.CaptureContent(); panel.RestoreContent(snapshot); Settle(panel, size);
                Require(!panel.IsRecoveryDirectionGuideVisible, "Restoring another chapter cannot retain recovery overlay permission.");
                panel.SetRecoveryDirectionGuide(true, false, brace, effort, effort, true, true, body, point);
                panel.SetContent(text.Terrain.Title, "Resume the original lesson.", "L", "Follow target", "", "Ready", 2, 9);
                Settle(panel, size);
                Require(!panel.IsRecoveryDirectionGuideVisible, "Changing card content clears old recovery instructions and geometry.");
            }
        }
        private static string Opposite(string arrow) => arrow == "↑" ? "↓" : arrow == "↓" ? "↑" : arrow == "→" ? "←" : "→";
        private static void Show(TutorialDialoguePanel panel, TutorialTextContent text, string message, string glyph, string action, string detail, string status)
        {
            panel.SetVisible(true); panel.SetCollapsed(false); panel.SetContinuePrompt(false);
            panel.SetContent(text.TipOver.Title, message, glyph, action, detail, status, 2, 9);
            panel.SetContextLabel(text.TipOver.ContextLabel); panel.SetLessonHint(text.TipOver.ToggleHint);
            panel.SetProgress(0f); panel.SetSuccess(false);
        }
        private static void Settle(TutorialDialoguePanel panel, Vector2 size) => Driving("Settle", panel, size);
        private static void CheckText(VisualElement root, string context)
        {
            foreach (VisualElement element in root.Query<VisualElement>().ToList())
            {
                if (!Visible(element, root) || !(element is Label label)) continue;
                foreach (char c in label.text) Require(!(c >= '\u3400' && c <= '\u9fff'), context + " is English.");
                Require(!label.text.Contains("{") && !label.text.Contains("}"), context + " formats all directional placeholders.");
                Driving("CheckTextFits", label, context + " / " + label.name);
            }
            Driving("CheckFont", root);
        }
        private static bool Visible(VisualElement value, VisualElement root)
        {
            for (var item = value; item != null; item = item.parent)
            { if (item.resolvedStyle.display == DisplayStyle.None || item.resolvedStyle.visibility != Visibility.Visible) return false; if (item == root) return true; }
            return false;
        }
        private static void Capture(VisualElement root, RenderTexture texture, string filename)
        {
            string previous = Environment.GetEnvironmentVariable("TUTORIAL_DRIVING_VALIDATION_OUTPUT");
            try { Environment.SetEnvironmentVariable("TUTORIAL_DRIVING_VALIDATION_OUTPUT", Output()); Driving("Capture", root, texture, filename); screenshots.Add(Path.Combine(Output(), filename)); }
            finally { Environment.SetEnvironmentVariable("TUTORIAL_DRIVING_VALIDATION_OUTPUT", previous); }
        }
        private static object Driving(string name, params object[] values) => typeof(TutorialDrivingRegressionChecks).GetMethod(name, Flags | BindingFlags.Static).Invoke(null, values);
        private static void Invoke(object target, string name) => target.GetType().GetMethod(name, Flags).Invoke(target, null);
        private static void Require(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
        private static string Output()
        { string value = Environment.GetEnvironmentVariable("TUTORIAL_TIPOVER_VALIDATION_OUTPUT"); string path = string.IsNullOrWhiteSpace(value) ? Path.GetFullPath("output/validation/tutorial-tipover") : Path.GetFullPath(value); Directory.CreateDirectory(path); return path; }
        private static void Write(bool passed, string error) => File.WriteAllText(Path.Combine(Output(), "ui-report.json"), JsonUtility.ToJson(new Report { passed = passed, checks = checks, error = error, screenshots = screenshots.ToArray() }, true));
    }
}
