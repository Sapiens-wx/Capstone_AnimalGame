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
    /// <summary>Real panel layout and Inspector copy checks for the continuous recycling exercise.</summary>
    public static class TutorialRecyclingUiChecks
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static int checks;
        private static readonly List<string> screenshots = new();
        [Serializable] private sealed class Report { public bool passed; public int checks; public string error; public string[] screenshots; }

        [MenuItem("Animal Game/Validation/Run Tutorial Recycling UI Checks")]
        public static void Run()
        {
            checks = 0; screenshots.Clear();
            try
            {
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "UI checks run outside Play Mode.");
                const string path = "Assets/Data/Tutorial/TutorialText.asset";
                var copy = AssetDatabase.LoadAssetAtPath<TutorialTextContent>(path);
                Require(copy != null && copy.Recycling != null, "The authored asset supplies recycling copy.");
                byte[] original = File.ReadAllBytes(path); string before = EditorJsonUtility.ToJson(copy);
                CheckEditing(copy); CheckPanel(copy);
                Require(EditorJsonUtility.ToJson(copy) == before && Convert.ToBase64String(original)
                    == Convert.ToBase64String(File.ReadAllBytes(path)), "Validation preserves authored text and file bytes.");
                Write(true, null); Debug.Log("TUTORIAL RECYCLING UI CHECKS PASSED: " + checks + " assertions.");
            }
            catch (Exception error) { Write(false, error.GetBaseException().ToString()); throw; }
        }
        public static void RunBatch()
        { try { Run(); EditorApplication.Exit(0); } catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); } }
        public static void RunAllBatch()
        { try { TutorialRecyclingRegressionChecks.Run(); Run(); EditorApplication.Exit(0); } catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); } }

        private static void CheckEditing(TutorialTextContent source)
        {
            var copy = Object.Instantiate(source);
            string other = OtherCopy(copy), temporary = AssetDatabase.GenerateUniqueAssetPath("Assets/Editor/__TutorialRecyclingTextCheck.asset");
            UnityEditor.Editor inspector = null;
            try
            {
                using (var serialized = new SerializedObject(copy))
                {
                    var chapter = serialized.FindProperty("Recycling");
                    Require(chapter != null, "The Inspector exposes the recycling chapter.");
                    Require(chapter.FindPropertyRelative("ReachLargeWaste") != null
                        && chapter.FindPropertyRelative("RecycleLargeWaste") != null,
                        "The Inspector exposes large-waste navigation and its continuous native operation as editable steps.");
                    foreach (FieldInfo field in copy.Recycling.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                    {
                        var property = chapter.FindPropertyRelative(field.Name);
                        Require(property != null, "Editable recycling field: " + field.Name);
                        if (field.FieldType != typeof(TutorialStepText)) continue;
                        foreach (string child in new[] { "ControlGlyph", "Action", "Message", "Detail", "CompleteFeedback" })
                            Require(property.FindPropertyRelative(child) != null, field.Name + " exposes " + child + ".");
                    }
                    string title = copy.Recycling.Title;
                    Undo.IncrementCurrentGroup(); chapter.FindPropertyRelative("Title").stringValue = "Recycling Undo probe";
                    serialized.ApplyModifiedProperties(); Undo.FlushUndoRecordObjects();
                    Require(copy.Recycling.Title == "Recycling Undo probe", "Inspector edits update the chapter.");
                    Undo.PerformUndo(); Require(copy.Recycling.Title == title, "Inspector text supports Undo."); Undo.ClearUndo(copy);
                    serialized.Update(); chapter.FindPropertyRelative("Title").stringValue = "Saved recycling probe";
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                Require(OtherCopy(copy) == other, "Editing recycling preserves every existing chapter.");
                inspector = UnityEditor.Editor.CreateEditor(copy);
                Require(inspector is TutorialTextContentEditor, "The organized tutorial Inspector authors recycling copy.");
                AssetDatabase.CreateAsset(copy, temporary); EditorUtility.SetDirty(copy); AssetDatabase.SaveAssetIfDirty(copy);
                Require(File.ReadAllText(temporary).Contains("Saved recycling probe"), "Inspector edits save in a disposable asset.");
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
            + JsonUtility.ToJson(text.Discovery) + JsonUtility.ToJson(text.Photography) + JsonUtility.ToJson(text.Refit);

        private static void CheckPanel(TutorialTextContent text)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            float oldTimeScale = Time.timeScale; RenderTexture texture = null;
            try
            {
                Time.timeScale = 1f;
                var owner = new GameObject("Recycling UI validation"); SceneManager.MoveGameObjectToScene(owner, scene);
                var panel = owner.AddComponent<TutorialDialoguePanel>();
                if (panel.Document == null) Invoke(panel, "Awake");
                texture = new RenderTexture(1920, 1080, 24); texture.Create(); panel.Document.panelSettings.targetTexture = texture;
                panel.SetPanelText(text.Panel);
                var tokens = Tokens();
                foreach (Vector2 size in new[] { new Vector2(1920,1080), new Vector2(1280,720), new Vector2(1440,1080), new Vector2(2560,1080) })
                {
                    foreach (RecyclingTutorialStep step in Enum.GetValues(typeof(RecyclingTutorialStep)))
                    {
                        if (step == RecyclingTutorialStep.Completed) continue;
                        var item = text.Recycling.GetStep(step);
                        Show(panel, text, item, tokens);
                        bool inventoryStep = step == RecyclingTutorialStep.OpenInventory || step == RecyclingTutorialStep.ViewMaterials;
                        panel.SetCollapseEnabled(!inventoryStep);
                        panel.SetLessonHint(tokens.Format(step == RecyclingTutorialStep.OpenInventory ? text.Recycling.OpenInventoryHint
                            : step == RecyclingTutorialStep.ViewMaterials ? text.Recycling.InventoryToggleHint
                            : step == RecyclingTutorialStep.ReachLargeWaste || step == RecyclingTutorialStep.RecycleLargeWaste
                                ? text.Recycling.LargeToggleHint : text.Recycling.ToggleHint));
                        Settle(panel, size); CheckText(panel.Root, step + " / " + size);
                        Require(panel.Root.Q<Label>("TutorialChapter").text == "07 / 09", "Recycling is chapter seven of nine.");
                        panel.SetContinuePrompt(!inventoryStep, text.Panel.ContinuePrompt); Settle(panel, size);
                        CheckText(panel.Root, "Ready " + step + " / " + size);
                        Require(panel.CanContinue == !inventoryStep,
                            "Inventory actions preserve actual open/close controls instead of claiming the A confirmation.");
                        Require((panel.Root.Q<Label>("TutorialToggleHint").resolvedStyle.display == DisplayStyle.None) == inventoryStep,
                            "The D-pad hide reminder is suppressed only while the inventory controls own those directions.");
                        if (size == new Vector2(1920,1080)) Capture(panel.Root, texture, "recycling-" + step.ToString().ToLowerInvariant() + ".png");
                        panel.SetCollapsed(true); Settle(panel, size); CheckText(panel.Root, "Collapsed " + step + " / " + size);
                        Require(panel.IsCollapsed == !inventoryStep,
                            "Inventory teaching stays expanded when a D-pad or collapse request would otherwise hide it.");
                    }
                    // Dynamic operational/recovery messages share the exact same runtime message box.
                    foreach (FieldInfo field in text.Recycling.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (field.FieldType != typeof(string) || !(field.Name.EndsWith("Message") || field.Name.EndsWith("Status")
                            || field.Name.EndsWith("Detail") || field.Name.EndsWith("Action") || field.Name.EndsWith("Hint"))) continue;
                        string message = tokens.Format((string)field.GetValue(text.Recycling));
                        if (string.IsNullOrWhiteSpace(message)) continue;
                        var item = text.Recycling.GetStep(RecyclingTutorialStep.RecycleWaste);
                        panel.SetCollapseEnabled(true);
                        Show(panel, text, item, tokens);
                        panel.SetContent(text.Recycling.Title, field.Name.EndsWith("Message") ? message : tokens.Format(item.Message),
                            "A", field.Name.EndsWith("Action") ? message : tokens.Format(item.Action),
                            field.Name.EndsWith("Detail") ? message : tokens.Format(item.Detail),
                            field.Name.EndsWith("Status") ? message : "Waiting", 7, 9);
                        panel.SetLessonHint(field.Name.EndsWith("Hint") ? message : tokens.Format(text.Recycling.ToggleHint));
                        Settle(panel, size); CheckText(panel.Root, field.Name + " / " + size);
                    }
                }
                panel.SetContinuePrompt(false);
                Require(!panel.CanContinue, "An unfinished continuous recycling card does not own the gripping A input.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                if (texture != null) { texture.Release(); Object.DestroyImmediate(texture); }
                Time.timeScale = oldTimeScale;
            }
        }
        private static void Show(TutorialDialoguePanel panel, TutorialTextContent text, TutorialStepText item, TutorialTextTokens tokens)
        {
            panel.SetVisible(true); panel.SetCollapsed(false); panel.SetContinuePrompt(false);
            panel.SetContent(text.Recycling.Title, tokens.Format(item.Message), tokens.Format(item.ControlGlyph),
                tokens.Format(item.Action), tokens.Format(item.Detail), "Waiting", 7, 9);
            panel.SetProgress(0f); panel.SetSuccess(false);
        }
        private static TutorialTextTokens Tokens() => new TutorialTextTokens().Add("confirm", "A").Add("grab", "A")
            .Add("cancel", "B").Add("rt", "RT").Add("lt", "LT").Add("lb", "LB").Add("rb", "RB")
            .Add("inventoryRight", "→").Add("inventoryLeft", "←").Add("materials", "Metal Scrap × 1")
            .Add("distance", 2f).Add("value", .5f).Add("target", 1f).Add("pull", 55f).Add("l3", "L3").Add("stick", "L");
        private static void Settle(TutorialDialoguePanel panel, Vector2 size) => Driving("Settle", panel, size);
        private static void CheckText(VisualElement root, string context)
        {
            foreach (VisualElement element in root.Query<VisualElement>().ToList())
            {
                if (!Visible(element, root) || !(element is Label label)) continue;
                foreach (char c in label.text) Require(!(c >= '\u3400' && c <= '\u9fff'), context + " is English.");
                Require(!label.text.Contains("{") && !label.text.Contains("}"), context + " formats all tokens.");
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
        { string value = Environment.GetEnvironmentVariable("TUTORIAL_RECYCLING_VALIDATION_OUTPUT"); string path = string.IsNullOrWhiteSpace(value) ? Path.GetFullPath("output/validation/tutorial-recycling") : Path.GetFullPath(value); Directory.CreateDirectory(path); return path; }
        private static void Write(bool passed, string error) => File.WriteAllText(Path.Combine(Output(), "ui-report.json"), JsonUtility.ToJson(new Report { passed = passed, checks = checks, error = error, screenshots = screenshots.ToArray() }, true));
    }
}
