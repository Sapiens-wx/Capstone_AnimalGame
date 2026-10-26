using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AnimalGame.Animals;
using AnimalGame.Demos.CheckpointRefit;
using AnimalGame.Demos.WasteRobot;
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
    /// <summary>Actual refit chapter cards and unmodified native assembly UI, using owned disposable UI instances.</summary>
    public static class TutorialRefitUiChecks
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string TextPath = "Assets/Data/Tutorial/TutorialText.asset";
        private const string UiPath = "Assets/Demos/CheckpointRefit/Prefabs/CheckpointRefitUI.prefab";
        private static readonly Vector2[] Sizes = { new(1920, 1080), new(1280, 720), new(1440, 1080), new(2560, 1080) };
        private static readonly List<string> screenshots = new();
        private static int checks;

        [Serializable] private sealed class Report
        { public bool passed; public int checks; public string error; public string[] screenshots; }

        [MenuItem("Animal Game/Validation/Run Tutorial Refit UI Checks")]
        public static void Run()
        {
            checks = 0; screenshots.Clear();
            try
            {
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run camera-upgrade UI checks in Edit Mode.");
                var text = AssetDatabase.LoadAssetAtPath<TutorialTextContent>(TextPath);
                Require(text != null && text.Refit != null, "The authored tutorial asset supplies the editable camera-upgrade chapter.");
                string before = EditorJsonUtility.ToJson(text);
                byte[] bytes = File.ReadAllBytes(TextPath);
                CheckEditing(text);
                using (var fixture = new Fixture(text))
                {
                    CheckWorldCards(fixture, text);
                    CheckNativeMenuUnmodified(fixture, text);
                }
                Require(EditorJsonUtility.ToJson(text) == before && Convert.ToBase64String(bytes)
                    == Convert.ToBase64String(File.ReadAllBytes(TextPath)), "UI checks preserve all authored tutorial copy and asset bytes.");
                WriteReport(true, null);
                Debug.Log("TUTORIAL REFIT UI CHECKS PASSED: " + checks + " assertions.");
            }
            catch (Exception error) { WriteReport(false, error.GetBaseException().ToString()); throw; }
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        public static void RunAllBatch()
        {
            try
            {
                TutorialPhotographyUiChecks.RunAll();
                TutorialRefitRegressionChecks.Run();
                Run();
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void CheckEditing(TutorialTextContent source)
        {
            var copy = Object.Instantiate(source);
            string temporary = AssetDatabase.GenerateUniqueAssetPath("Assets/Editor/__TutorialRefitTextCheck.asset");
            UnityEditor.Editor inspector = null;
            try
            {
                string untouched = OtherCopy(copy);
                using (var data = new SerializedObject(copy))
                {
                    var chapter = data.FindProperty("Refit");
                    Require(chapter != null, "The Inspector exposes the Refit chapter.");
                    foreach (FieldInfo field in typeof(RefitLessonText).GetFields(BindingFlags.Public | BindingFlags.Instance))
                    {
                        SerializedProperty property = chapter.FindPropertyRelative(field.Name);
                        Require(property != null, "The Inspector exposes refit field " + field.Name + ".");
                        if (field.FieldType != typeof(TutorialStepText)) continue;
                        foreach (string child in new[] { "ControlGlyph", "Action", "Message", "Detail", "CompleteFeedback" })
                            Require(property.FindPropertyRelative(child) != null, field.Name + " exposes " + child + ".");
                    }
                    string title = copy.Refit.Title;
                    Undo.IncrementCurrentGroup();
                    chapter.FindPropertyRelative("Title").stringValue = "Refit Undo probe";
                    data.ApplyModifiedProperties(); Undo.FlushUndoRecordObjects();
                    Require(copy.Refit.Title == "Refit Undo probe", "Serialized Inspector editing updates the new chapter.");
                    Undo.PerformUndo();
                    Require(copy.Refit.Title == title, "Refit authoring supports Unity Undo.");
                    Undo.ClearUndo(copy);
                    data.Update();
                    chapter.FindPropertyRelative("OpenWorkbench").FindPropertyRelative("Message").stringValue = "Edited exterior lens instruction.";
                    chapter.FindPropertyRelative("EnterMachine").FindPropertyRelative("Message").stringValue = "Edited entrance probe.";
                    chapter.FindPropertyRelative("NewLensControls").FindPropertyRelative("Message").stringValue = "Edited camera frame practice.";
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                Require(OtherCopy(copy) == untouched, "Editing refit instructions preserves all earlier chapters and panel text.");
                inspector = UnityEditor.Editor.CreateEditor(copy);
                Require(inspector is TutorialTextContentEditor, "The organized Chinese tutorial Inspector supports the new chapter.");
                AssetDatabase.CreateAsset(copy, temporary);
                EditorUtility.SetDirty(copy); AssetDatabase.SaveAssetIfDirty(copy);
                string saved = File.ReadAllText(temporary);
                Require(saved.Contains("Edited exterior lens instruction.") && saved.Contains("Edited entrance probe.")
                    && saved.Contains("Edited camera frame practice."),
                    "Exterior station, entrance and final camera-frame card edits persist in a disposable serialized asset.");
                Require(OtherCopy(copy) == untouched, "Saving refit copy preserves every existing chapter.");
            }
            finally
            {
                if (inspector != null) Object.DestroyImmediate(inspector);
                Undo.ClearUndo(copy);
                if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(copy))) AssetDatabase.DeleteAsset(temporary);
                else if (copy != null) Object.DestroyImmediate(copy);
            }
        }

        private static string OtherCopy(TutorialTextContent text) => JsonUtility.ToJson(text.Panel)
            + JsonUtility.ToJson(text.Driving) + JsonUtility.ToJson(text.Terrain) + JsonUtility.ToJson(text.Balance)
            + JsonUtility.ToJson(text.Arms) + JsonUtility.ToJson(text.Discovery) + JsonUtility.ToJson(text.Photography);

        private static void CheckWorldCards(Fixture fixture, TutorialTextContent text)
        {
            Time.timeScale = 1f;
            foreach (Vector2 size in Sizes)
            foreach (RefitTutorialStep step in Enum.GetValues(typeof(RefitTutorialStep)))
            {
                if (step == RefitTutorialStep.Completed || step == RefitTutorialStep.RefitIntroduction || step == RefitTutorialStep.CloseRefit) continue;
                typeof(RefitTutorialProgress).GetProperty("Step", Flags).SetValue(fixture.Progress, step);
                Set(fixture.Lesson, "chapterConfirmed", false);
                Invoke(fixture.Lesson, "RefreshPanel", true);
                fixture.Panel.SetPhotoModeVisible(false); fixture.Panel.SetVisible(true); fixture.Panel.SetCollapsed(false);
                Driving("Settle", fixture.Panel, size);
                CheckText(fixture.Panel.Root, "Refit world " + step + " / " + size);
                Require(fixture.Panel.Root.Q<Label>("TutorialChapter").text == "08 / 09", "Refit teaching is chapter eight of nine.");
                Require(fixture.Panel.Root.Q<Label>("TutorialTitle").text == text.Refit.Title,
                    "The new world card uses the Inspector-editable title.");
                Require(fixture.Panel.Root.Q<Label>("TutorialMessage").text == Tokens().Format(text.Refit.GetStep(step).Message),
                    "The runtime card displays its actual " + step + " instruction.");
                fixture.Panel.SetContinuePrompt(true, text.Panel.ContinuePrompt);
                Driving("Settle", fixture.Panel, size);
                CheckText(fixture.Panel.Root, "Refit ready A prompt " + step + " / " + size);
                Require(fixture.Panel.Root.Q<Label>("TutorialToggleHint").resolvedStyle.display != DisplayStyle.None,
                    "The D-pad hide hint remains visible while waiting for A.");
                if (size == Sizes[0]) Capture(fixture.Panel.Root, fixture.WorldTexture, "refit-" + step.ToString().ToLowerInvariant() + ".png");
                fixture.Panel.SetCollapsed(true); Driving("Settle", fixture.Panel, size);
                CheckText(fixture.Panel.Root, "Refit collapsed " + step + " / " + size);
            }
            typeof(RefitTutorialProgress).GetProperty("Step", Flags).SetValue(fixture.Progress, RefitTutorialStep.Completed);
            Set(fixture.Progress, "lastValid", true);
            Set(fixture.Lesson, "hasStarted", true);
            Set(fixture.Lesson, "chapterConfirmed", false); Invoke(fixture.Lesson, "RefreshPanel", true);
            Driving("Settle", fixture.Panel, Sizes[0]);
            Require(fixture.Lesson.IsComplete && !fixture.Panel.CanContinue && !fixture.Panel.IsVisible,
                "The final camera-frame confirmation completes the chapter and hides the card without another A.");
            fixture.Panel.SetContinuePrompt(false); fixture.Panel.SetVisible(false);
        }

        private static void CheckNativeMenuUnmodified(Fixture fixture, TutorialTextContent text)
        {
            var root = fixture.View.Document.rootVisualElement;
            fixture.View.Show("TUTORIAL ASSEMBLY", "TUT-01");
            fixture.View.NavigateSlot(1); fixture.View.NavigateModule(1);
            Require(fixture.View.SelectedCamera == fixture.Zoom, "The native menu selects the real available replacement lens.");
            Set(fixture.Terminal, "<IsOpen>k__BackingField", true);
            var originals = new Dictionary<string, string>();
            foreach (string name in new[] { "refit-equip-footer", "refit-inspect-footer", "refit-close", "refit-inspection-close" })
                originals.Add(name, root.Q<TextElement>(name).text);
            Func<bool> submit = () => false;
            fixture.Terminal.TutorialSubmitHandler = submit;
            Invoke(fixture.Lesson, "HookTerminal");
            Require(fixture.Terminal.TutorialSubmitHandler == submit,
                "The tutorial never replaces the native equipment-submit callback.");
            Require(fixture.Lesson.Guide == null && fixture.Lesson.GetComponentsInChildren<TutorialRefitGuide>(true).Length == 0,
                "No guide component is created inside the assembly station.");
            Time.timeScale = 0f;
            foreach (Vector2 size in Sizes)
            {
                fixture.LayoutMenu(size);
                Require(root.Q("TutorialRefitGuide") == null,
                    "The native paused assembly interface has no tutorial overlay at " + size + ".");
                foreach (var original in originals)
                    Require(root.Q<TextElement>(original.Key).text == original.Value,
                        "The outside-only tutorial preserves native " + original.Key + ".");
                if (size == Sizes[0]) Capture(root, fixture.MenuTexture, "refit-native-menu-no-tutorial.png");
            }
            fixture.View.OpenInspection(); fixture.LayoutMenu(Sizes[0]);
            Require(root.Q("TutorialRefitGuide") == null && fixture.View.IsInspectionOpen,
                "Native inspection works without a tutorial overlay or confirmation.");
            fixture.View.CloseInspection();
            Require(fixture.Equipment.TryEquip(fixture.Zoom), "The native equipment operation installs the actual target definition.");
            fixture.View.RefreshEquipment(); fixture.LayoutMenu(Sizes[0]);
            Require(root.Q<Button>("refit-equip").text == "INSTALLED",
                "The native installed caption remains fully controlled by the equipment UI.");
            Require(fixture.Terminal.TutorialSubmitHandler == submit,
                "Selecting, inspecting and installing never gives the tutorial submit ownership.");
            Invoke(fixture.Lesson, "UnhookTerminal");
            Require(fixture.Terminal.TutorialSubmitHandler == submit && fixture.Terminal.TutorialOpenBlocked == null,
                "Releasing the outside-only open guard preserves the native submit callback.");
            fixture.Terminal.TutorialSubmitHandler = null;
        }

        private sealed class Fixture : IDisposable
        {
            private readonly Scene scene;
            private readonly bool opened;
            private readonly float oldTimeScale;
            private readonly HeightMapPlayerSceneBootstrap oldBootstrap;
            private readonly List<GameObject> owned = new();
            private readonly PanelSettings menuSettings;
            public readonly TutorialDialoguePanel Panel;
            public readonly TutorialRefitLesson Lesson;
            public readonly RefitTutorialProgress Progress;
            public readonly CheckpointRefitView View;
            public readonly CheckpointRefitTerminal Terminal;
            public readonly RefitCameraEquipment Equipment;
            public readonly PhotoCameraDefinition Zoom;
            public readonly RenderTexture WorldTexture, MenuTexture;

            public Fixture(TutorialTextContent text)
            {
                scene = SceneManager.GetSceneByPath(TutorialDrivingLesson.ScenePath);
                opened = !scene.IsValid() || !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(TutorialDrivingLesson.ScenePath, OpenSceneMode.Additive);
                oldTimeScale = Time.timeScale; oldBootstrap = HeightMapPlayerSceneBootstrap.inst;
                Time.timeScale = 1f;
                Panel = Create("Refit UI validation card").AddComponent<TutorialDialoguePanel>();
                if (Panel.Document == null) Invoke(Panel, "Awake");
                WorldTexture = Texture(); Panel.Document.panelSettings.targetTexture = WorldTexture;
                var fixture = Create("Inactive refit UI fixture", false);
                var bootstrap = fixture.AddComponent<HeightMapPlayerSceneBootstrap>();
                bootstrap.robot = fixture.AddComponent<RobotMover>();
                HeightMapPlayerSceneBootstrap.inst = bootstrap;
                var photo = fixture.AddComponent<TutorialPhotographyLesson>();
                var driving = fixture.GetComponent<TutorialDrivingLesson>(); Set(driving, "panel", Panel);
                var arms = fixture.GetComponent<TutorialArmLesson>(); Set(arms, "driving", driving);
                var discovery = fixture.GetComponent<TutorialDiscoveryLesson>(); Set(discovery, "arms", arms);
                Set(photo, "discovery", discovery);
                Lesson = fixture.AddComponent<TutorialRefitLesson>();
                Terminal = fixture.AddComponent<CheckpointRefitTerminal>();
                var machine = fixture.AddComponent<WasteRobotBase>();
                Set(Lesson, "photography", photo); Set(Lesson, "bootstrap", bootstrap); Set(Lesson, "machine", machine);
                Set(Lesson, "terminal", Terminal); Set(Lesson, "tutorialText", text);
                Progress = new RefitTutorialProgress(); Set(Lesson, "progress", Progress);
                var inspector = UnityEditor.Editor.CreateEditor(Lesson);
                Require(inspector is TutorialRefitLessonTextEditor, "The scene component exposes the organized Chinese text Inspector.");
                Object.DestroyImmediate(inspector);

                var profile = AssetDatabase.LoadAssetAtPath<GameObject>(UiPath).GetComponent<UIDocument>();
                menuSettings = Object.Instantiate(profile.panelSettings); MenuTexture = Texture(); menuSettings.targetTexture = MenuTexture;
                var ui = Create("Refit UI validation menu");
                var document = ui.AddComponent<UIDocument>(); document.panelSettings = menuSettings; document.visualTreeAsset = profile.visualTreeAsset;
                View = ui.AddComponent<CheckpointRefitView>(); Set(Terminal, "view", View);
                var camera = fixture.AddComponent<PhotoModeController>();
                Zoom = AssetDatabase.LoadAssetAtPath<PhotoCameraDefinition>("Assets/Data/AnimalPhoto/Rendering/InstantCamera.asset");
                var damaged = AssetDatabase.LoadAssetAtPath<PhotoCameraDefinition>("Assets/Data/AnimalPhoto/Rendering/HalflyBrokenPrimeCamera50.asset");
                Require(Zoom != null && damaged != null, "The menu fixture uses both real camera definitions.");
                camera.SetCamera(damaged); Equipment = fixture.AddComponent<RefitCameraEquipment>(); Equipment.Configure(camera, Zoom); Equipment.Unlock(damaged);
                View.BindCameraEquipment(Equipment, Zoom, damaged);
            }

            private GameObject Create(string name, bool active = true)
            {
                var owner = new GameObject(name); owner.SetActive(active); SceneManager.MoveGameObjectToScene(owner, scene); owned.Add(owner); return owner;
            }
            private static RenderTexture Texture() { var texture = new RenderTexture(1920, 1080, 24); texture.Create(); return texture; }
            public void LayoutMenu(Vector2 size)
            {
                VisualElement root = View.Document.rootVisualElement;
                Require(root.panel != null, "The native refit UXML attaches to a real runtime Toolkit panel.");
                root.style.width = size.x; root.style.height = size.y;
                for (int pass = 0; pass < 3; pass++) { Layout(root); Invoke(View, "FitStage"); }
                Layout(root);
            }
            public void Dispose()
            {
                foreach (GameObject item in owned) if (item != null) Object.DestroyImmediate(item);
                if (opened) EditorSceneManager.CloseScene(scene, true);
                WorldTexture.Release(); MenuTexture.Release(); Object.DestroyImmediate(WorldTexture); Object.DestroyImmediate(MenuTexture);
                Object.DestroyImmediate(menuSettings); HeightMapPlayerSceneBootstrap.inst = oldBootstrap; Time.timeScale = oldTimeScale;
            }
        }

        private static void CheckText(VisualElement root, string context)
        {
            bool ordinaryFont = root.resolvedStyle.unityFontDefinition.font != null || root.resolvedStyle.unityFont != null;
            foreach (VisualElement element in root.Query<VisualElement>().ToList())
            {
                if (!Visible(element, root) || !(element is Label label)) continue;
                foreach (char character in label.text)
                    Require(!(character >= '\u3400' && character <= '\u9fff'), context + " uses English game text.");
                Require(!label.text.Contains("{") && !label.text.Contains("}"), context + " formats all live tokens.");
                Driving("CheckTextFits", label, context + " / " + label.name);
                if (!ordinaryFont) CheckLabelFont(label, context);
            }
            // The world card resolves a Unity Font. The native refit UI resolves a TextCore
            // FontAsset instead; its own source font (or baked glyphs) must be checked per label.
            if (ordinaryFont) Driving("CheckFont", root);
        }

        private static void CheckLabelFont(Label label, string context)
        {
            FontDefinition definition = label.resolvedStyle.unityFontDefinition;
            var fontAsset = definition.fontAsset;
            Font font = definition.font != null ? definition.font : label.resolvedStyle.unityFont;
            if (font == null && fontAsset != null) font = fontAsset.sourceFontFile;
            Require(font != null || fontAsset != null, context + " / " + label.name + " resolves an actual font or TextCore font asset.");
            string shown = label.text + label.tooltip;
            if (font != null && font.dynamic)
                font.RequestCharactersInTexture(shown, Mathf.Max(1, Mathf.RoundToInt(label.resolvedStyle.fontSize)),
                    label.resolvedStyle.unityFontStyleAndWeight);
            var characters = new HashSet<char>();
            foreach (char character in shown)
                if (!char.IsWhiteSpace(character) && !char.IsControl(character)) characters.Add(character);
            foreach (char character in characters)
                Require(font != null ? font.HasCharacter(character) : fontAsset.HasCharacter(character),
                    context + " / " + label.name + " font contains glyph '" + character + "' (U+"
                    + ((int)character).ToString("X4") + ").");
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
        private static TutorialTextTokens Tokens() => new TutorialTextTokens().Add("confirm", "A").Add("cancel", "B")
            .Add("photo", "X").Add("lb", "LB").Add("rb", "RB").Add("rt", "RT").Add("lt", "LT")
            .Add("lens", "Zoom Lens 300").Add("distance", 0f);
        private static void Capture(VisualElement root, RenderTexture texture, string filename)
        {
            string previous = Environment.GetEnvironmentVariable("TUTORIAL_DRIVING_VALIDATION_OUTPUT");
            try { Environment.SetEnvironmentVariable("TUTORIAL_DRIVING_VALIDATION_OUTPUT", OutputDirectory()); Driving("Capture", root, texture, filename); screenshots.Add(Path.Combine(OutputDirectory(), filename)); }
            finally { Environment.SetEnvironmentVariable("TUTORIAL_DRIVING_VALIDATION_OUTPUT", previous); }
        }
        private static object Driving(string name, params object[] args) => typeof(TutorialDrivingRegressionChecks).GetMethod(name, Flags | BindingFlags.Static).Invoke(null, args);
        private static void Layout(VisualElement root) => root.panel.GetType().GetMethod("ValidateLayout", Flags).Invoke(root.panel, null);
        private static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Flags).Invoke(target, args);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
        private static void Require(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
        private static string OutputDirectory()
        {
            string requested = Environment.GetEnvironmentVariable("TUTORIAL_REFIT_VALIDATION_OUTPUT");
            string output = string.IsNullOrWhiteSpace(requested) ? Path.GetFullPath("output/validation/tutorial-refit") : Path.GetFullPath(requested);
            Directory.CreateDirectory(output); return output;
        }
        private static void WriteReport(bool passed, string error) => File.WriteAllText(Path.Combine(OutputDirectory(), "ui-report.json"),
            JsonUtility.ToJson(new Report { passed = passed, checks = checks, error = error, screenshots = screenshots.ToArray() }, true));
    }
}
