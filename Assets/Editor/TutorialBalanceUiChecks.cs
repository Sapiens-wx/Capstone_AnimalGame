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
    /// <summary>Actual lesson copy and layout. Authored assets and the native indicator remain unchanged.</summary>
    public static class TutorialBalanceUiChecks
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string TextPath = "Assets/Data/Tutorial/TutorialText.asset";
        private static int checks;
        private static readonly List<string> screenshots = new List<string>();
        [Serializable] private sealed class Report
        { public bool passed; public int checks; public string error; public string[] screenshots; }

        [MenuItem("Animal Game/Validation/Run Tutorial Balance UI Checks")]
        public static void Run()
        {
            checks = 0; screenshots.Clear();
            try
            {
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run balance UI checks in Edit Mode.");
                var text = AssetDatabase.LoadAssetAtPath<TutorialTextContent>(TextPath);
                Require(text != null && text.Balance != null, "The existing tutorial asset supplies editable balance copy.");
                string before = EditorJsonUtility.ToJson(text);
                byte[] bytes = File.ReadAllBytes(TextPath);
                var sharedSettings = Resources.Load<PanelSettings>("UI/Main/MainUISetting");
                string settingsBefore = sharedSettings != null ? EditorJsonUtility.ToJson(sharedSettings) : null;
                var sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/VisibilityClip.mat");
                string materialBefore = sharedMaterial != null ? EditorJsonUtility.ToJson(sharedMaterial) : null;
                CheckEditing(text);
                CheckPanel(text);
                Require(EditorJsonUtility.ToJson(text) == before && Convert.ToBase64String(bytes)
                    == Convert.ToBase64String(File.ReadAllBytes(TextPath)), "Balance presentation preserves the complete authored copy asset.");
                Require(sharedSettings == null || EditorJsonUtility.ToJson(sharedSettings) == settingsBefore,
                    "Balance presentation preserves shared HUD panel settings.");
                Require(sharedMaterial == null || EditorJsonUtility.ToJson(sharedMaterial) == materialBefore,
                    "Balance presentation preserves shared rendering materials.");
                WriteReport(true, null);
                Debug.Log("TUTORIAL BALANCE UI CHECKS PASSED: " + checks + " assertions.");
            }
            catch (Exception error) { WriteReport(false, error.GetBaseException().ToString()); throw; }
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void CheckEditing(TutorialTextContent source)
        {
            var copy = Object.Instantiate(source);
            string temporaryPath = AssetDatabase.GenerateUniqueAssetPath("Assets/Editor/__TutorialBalanceTextCheck.asset");
            try
            {
                string untouched = OtherChapterCopy(copy);
                using (var data = new SerializedObject(copy))
                {
                    SerializedProperty balance = data.FindProperty("Balance");
                    Require(balance != null, "The Inspector exposes the serialized Balance chapter.");
                    foreach (FieldInfo field in typeof(BalanceLessonText).GetFields(BindingFlags.Instance | BindingFlags.Public))
                    {
                        SerializedProperty property = balance.FindPropertyRelative(field.Name);
                        Require(property != null, "Balance Inspector serialization exposes " + field.Name + ".");
                        if (field.FieldType != typeof(TutorialStepText)) continue;
                        foreach (string child in new[] { "ControlGlyph", "Action", "Message", "Detail", "CompleteFeedback" })
                            Require(property.FindPropertyRelative(child) != null, field.Name + " exposes editable " + child + ".");
                    }
                    balance.FindPropertyRelative("Title").stringValue = "Balance roundtrip probe";
                    balance.FindPropertyRelative("ReadIndicator").FindPropertyRelative("Message").stringValue = "Edited balance reading probe.";
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                Require(OtherChapterCopy(copy) == untouched, "Editing balance copy preserves every other chapter and fixed panel string.");
                AssetDatabase.CreateAsset(copy, temporaryPath);
                EditorUtility.SetDirty(copy); AssetDatabase.SaveAssetIfDirty(copy);
                AssetDatabase.ImportAsset(temporaryPath, ImportAssetOptions.ForceUpdate);
                var saved = AssetDatabase.LoadAssetAtPath<TutorialTextContent>(temporaryPath);
                Require(saved != null && saved.Balance.Title == "Balance roundtrip probe"
                    && saved.Balance.ReadIndicator.Message == "Edited balance reading probe.",
                    "Inspector edits to balance title and nested instruction survive an asset roundtrip.");
                Require(File.ReadAllText(temporaryPath).Contains("Edited balance reading probe."),
                    "The nested balance edit is persisted to the temporary asset on disk.");
                Require(OtherChapterCopy(saved) == untouched, "Persisting balance copy preserves all earlier and later authored chapters.");
            }
            finally
            {
                if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(copy))) AssetDatabase.DeleteAsset(temporaryPath);
                else if (copy != null) Object.DestroyImmediate(copy);
            }
        }

        private static string OtherChapterCopy(TutorialTextContent text) => JsonUtility.ToJson(text.Panel)
            + JsonUtility.ToJson(text.Driving) + JsonUtility.ToJson(text.Terrain) + JsonUtility.ToJson(text.Arms)
            + JsonUtility.ToJson(text.Discovery) + JsonUtility.ToJson(text.Photography);

        private static void CheckPanel(TutorialTextContent text)
        {
            Scene scene = SceneManager.GetSceneByPath(TutorialDrivingLesson.ScenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(TutorialDrivingLesson.ScenePath, OpenSceneMode.Additive);
            float originalTimeScale = Time.timeScale;
            string previousOutput = Environment.GetEnvironmentVariable("TUTORIAL_DRIVING_VALIDATION_OUTPUT");
            HeightMapPlayerSceneBootstrap previousBootstrap = HeightMapPlayerSceneBootstrap.inst;
            GameObject owner = null, fixture = null, robot = null;
            RenderTexture texture = null;
            try
            {
                Time.timeScale = 1f;
                Environment.SetEnvironmentVariable("TUTORIAL_DRIVING_VALIDATION_OUTPUT", OutputDirectory());
                owner = new GameObject("Temporary balance UI validation"); SceneManager.MoveGameObjectToScene(owner, scene);
                var panel = owner.AddComponent<TutorialDialoguePanel>();
                if (panel.Document == null) Invoke(panel, "Awake");
                texture = new RenderTexture(1920, 1080, 24); texture.Create();
                panel.Document.panelSettings.targetTexture = texture;

                fixture = new GameObject("Inactive balance lesson fixture"); fixture.SetActive(false);
                SceneManager.MoveGameObjectToScene(fixture, scene);
                var bootstrap = fixture.AddComponent<HeightMapPlayerSceneBootstrap>();
                var driving = fixture.AddComponent<TutorialDrivingLesson>(); Set(driving, "panel", panel);
                var lesson = fixture.AddComponent<TutorialBalanceLesson>();
                Set(lesson, "driving", driving); Set(lesson, "terrain", fixture.GetComponent<TutorialTerrainLesson>());
                Set(lesson, "bootstrap", null); Set(lesson, "tutorialText", text);
                var progress = new BalanceTutorialProgress(); Set(lesson, "progress", progress);

                robot = new GameObject("Native balance observation fixture"); SceneManager.MoveGameObjectToScene(robot, scene);
                bootstrap.robot = robot.AddComponent<RobotMover>();
                bootstrap.balance = robot.AddComponent<RobotBalanceController>();
                bootstrap.balanceView = robot.AddComponent<RobotBalanceView>();
                HeightMapPlayerSceneBootstrap.inst = bootstrap;
                SetNativeBalance(bootstrap.balance, new Vector2(.3f, .15f));
                string viewBefore = EditorJsonUtility.ToJson(bootstrap.balanceView);
                string balanceBefore = EditorJsonUtility.ToJson(bootstrap.balance);
                RobotBalanceState stateBefore = bootstrap.balance.CurrentState;

                foreach (Vector2 size in new[] { new Vector2(1920, 1080), new Vector2(1280, 720),
                    new Vector2(1440, 1080), new Vector2(2560, 1080) })
                {
                    foreach (BalanceTutorialStep step in Enum.GetValues(typeof(BalanceTutorialStep)))
                    {
                        if (step == BalanceTutorialStep.Completed) continue;
                        typeof(BalanceTutorialProgress).GetProperty("Step", Flags).SetValue(progress, step);
                        Set(progress, "armed", true);
                        foreach (bool valid in new[] { true, false })
                        {
                            Invoke(lesson, "RefreshPanel", valid);
                            panel.SetVisible(true); panel.SetCollapsed(false); Settle(panel, size);
                            string context = "Balance " + step + " / " + size + " / " + valid;
                            CheckVisibleText(panel.Root, context);
                            Require(panel.Root.Q<Label>("TutorialChapter").text == "03 / 09", "Balance uses chapter three of nine.");
                            Require(panel.Root.Q<Label>("TutorialTitle").text == text.Balance.Title,
                                "The active balance title comes from the editable Inspector content.");
                            var tokens = new TutorialTextTokens().Add("value", progress.Value).Add("target", progress.Target);
                            Require(panel.Root.Q<Label>("TutorialMessage").text == tokens.Format(text.Balance.GetStep(step).Message),
                                context + " displays its actual step copy.");
                            if (valid && size == new Vector2(1920, 1080))
                            {
                                string name = "balance-" + step.ToString().ToLowerInvariant() + ".png";
                                Helper("Capture", panel.Root, texture, name); screenshots.Add(Path.Combine(OutputDirectory(), name));
                            }
                            panel.SetCollapsed(true); Settle(panel, size);
                            CheckVisibleText(panel.Root, "Collapsed " + context);
                        }
                    }
                    CheckRecoveryCopy(panel, text.Balance, size);
                }
                typeof(BalanceTutorialProgress).GetProperty("Step", Flags).SetValue(progress, BalanceTutorialStep.Completed);
                Invoke(lesson, "RefreshPanel", true);
                Require(!panel.IsVisible && !panel.CanContinue,
                    "Terminal balance state hides its card without asking for an extra chapter confirmation.");
                Require(EditorJsonUtility.ToJson(bootstrap.balanceView) == viewBefore
                    && EditorJsonUtility.ToJson(bootstrap.balance) == balanceBefore,
                    "Balance presentation preserves native balance view and controller configuration.");
                Require(bootstrap.balance.CurrentState.NormalizedWorldOffset == stateBefore.NormalizedWorldOffset
                    && bootstrap.balance.CurrentState.Magnitude == stateBefore.Magnitude,
                    "Balance presentation never moves the actual balance point or modifies physical risk.");
            }
            finally
            {
                HeightMapPlayerSceneBootstrap.inst = previousBootstrap;
                if (owner != null) Object.DestroyImmediate(owner);
                if (fixture != null) Object.DestroyImmediate(fixture);
                if (robot != null) Object.DestroyImmediate(robot);
                if (texture != null) { texture.Release(); Object.DestroyImmediate(texture); }
                if (opened && scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
                Time.timeScale = originalTimeScale;
                Environment.SetEnvironmentVariable("TUTORIAL_DRIVING_VALIDATION_OUTPUT", previousOutput);
            }
        }

        private static void CheckRecoveryCopy(TutorialDialoguePanel panel, BalanceLessonText text, Vector2 size)
        {
            string[] messages = { text.NeutralMessage };
            string[] statuses = { text.NeutralStatus };
            for (int index = 0; index < messages.Length; index++)
            {
                panel.SetContent(text.Title, messages[index], "R", "Shift weight", "Follow the prompt.", statuses[index], 3, 9);
                panel.SetVisible(true); panel.SetCollapsed(false); Settle(panel, size);
                CheckVisibleText(panel.Root, "Balance recovery " + index + " / " + size);
            }
        }

        private static void SetNativeBalance(RobotBalanceController balance, Vector2 offset) =>
            typeof(RobotBalanceController).GetProperty("CurrentState", Flags).SetValue(balance,
                new RobotBalanceState(offset, offset, Vector2.zero, offset.magnitude, .1f, RobotBalanceLevel.Stable));
        private static void CheckVisibleText(VisualElement root, string context)
        {
            foreach (VisualElement element in root.Query<VisualElement>().ToList())
            {
                if (!Visible(element, root)) continue;
                string shown = (element is Label label ? label.text : string.Empty) + element.tooltip;
                foreach (char letter in shown)
                    Require(!(letter >= '\u3400' && letter <= '\u9fff'), context + " keeps teaching copy in English.");
                Require(!shown.Contains("{value") && !shown.Contains("{target"), context + " resolves live tutorial tokens.");
                if (element is Label text) Helper("CheckTextFits", text, context + " / " + text.name);
            }
            Helper("CheckFont", root);
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
        private static void Settle(TutorialDialoguePanel panel, Vector2 size) => Helper("Settle", panel, size);
        private static object Helper(string name, params object[] args) => typeof(TutorialDrivingRegressionChecks)
            .GetMethod(name, Flags | BindingFlags.Static).Invoke(null, args);
        private static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Flags).Invoke(target, args);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
        private static void Require(bool condition, string message)
        { checks++; if (!condition) throw new InvalidOperationException(message); }
        private static string OutputDirectory()
        {
            string configured = Environment.GetEnvironmentVariable("TUTORIAL_BALANCE_VALIDATION_OUTPUT");
            string path = string.IsNullOrEmpty(configured) ? Path.GetFullPath("output/validation/tutorial-balance") : configured;
            Directory.CreateDirectory(path); return path;
        }
        private static void WriteReport(bool passed, string error) => File.WriteAllText(Path.Combine(OutputDirectory(), "ui-report.json"),
            JsonUtility.ToJson(new Report { passed = passed, checks = checks, error = error,
                screenshots = screenshots.ToArray() }, true));
    }
}
