using System;
using System.Globalization;
using System.IO;
using System.Linq;
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
    /// <summary>Content authoring checks; temporary edits never overwrite the user's tutorial copy.</summary>
    public static class TutorialTextContentValidation
    {
        private const string ContentPath = "Assets/Data/Tutorial/TutorialText.asset";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static int checks;

        [MenuItem("Animal Game/Validation/Run Tutorial Text Content Checks")]
        public static void Run()
        {
            checks = 0;
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Start content validation in Edit Mode.");
            var asset = AssetDatabase.LoadAssetAtPath<TutorialTextContent>(ContentPath);
            Require(asset != null, "The shared tutorial content asset exists.");
            string before = EditorJsonUtility.ToJson(asset);
            // Serialize existing values, including field-initialized defaults in the initial minimal asset.
            // Never reset or substitute a new default object for an existing author's content.
            EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset);
            Require(EditorJsonUtility.ToJson(asset) == before, "Baking retains every existing content value.");
            CheckDefaultsAndFormatting();
            CheckSaveAndUndo(asset);
            CheckSceneReferences();
            CheckActualLessonViews();
            Require(EditorJsonUtility.ToJson(asset) == before, "Authoring fixtures never modify the shared tutorial copy.");
            TutorialDrivingRegressionChecks.Run();
            TutorialTerrainRegressionChecks.Run();
            string output = Path.GetFullPath("output/validation/tutorial-text-content");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "content-validation.txt"), "PASS: " + checks
                + " content assertions; existing driving and terrain regression suites also passed.");
            Debug.Log("TUTORIAL TEXT CONTENT CHECKS PASSED: " + checks + " assertions and both existing regression suites.");
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void CheckDefaultsAndFormatting()
        {
            var text = ScriptableObject.CreateInstance<TutorialTextContent>();
            try
            {
                Require(text.Panel.Header == "TUTORIAL" && text.Driving.Title == "Driving Practice"
                    && text.Terrain.Title == "Terrain & Slopes", "Default section titles retain the existing English copy.");
                string[] actions = { "Turn left", "Turn right", "Drive forward", "Reverse", "Drive and steer" };
                foreach (DrivingTutorialStep step in Enum.GetValues(typeof(DrivingTutorialStep)))
                {
                    if (step == DrivingTutorialStep.Completed) continue;
                    TutorialStepText item = text.Driving.GetStep(step);
                    Require(item.Action == actions[(int)step], "Default driving action is preserved for " + step);
                    Require(!string.IsNullOrWhiteSpace(item.Message) && !string.IsNullOrWhiteSpace(item.Detail), "Default driving copy is populated.");
                }
                foreach (TerrainTutorialStep step in Enum.GetValues(typeof(TerrainTutorialStep)))
                    if (step != TerrainTutorialStep.Completed) Require(!string.IsNullOrWhiteSpace(text.Terrain.GetStep(step).Action)
                        && !string.IsNullOrWhiteSpace(text.Terrain.GetStep(step).Message), "Default terrain copy is populated for " + step);
                string json = EditorJsonUtility.ToJson(text);
                Require(!json.Any(c => c >= '\u3400' && c <= '\u9fff'), "New tutorial content defaults are entirely English.");
            }
            finally { Object.DestroyImmediate(text); }

            var tokens = new TutorialTextTokens().Add("value", 2.34f).Add("target", 45f)
                .Add("rt", "RT").Add("lt", "LT").Add("lb", "LB");
            Require(tokens.Format("{value:F1}/{target:F0} {rt}/{lt}/{lb}") == "2.3/45 RT/LT/LB",
                "Numeric precision and controller placeholders format correctly.");
            Require(tokens.Format("{unknown}") == "{unknown}", "Unknown placeholders stay readable.");
            Require(tokens.Format("{value:D2}") == "{value:D2}", "Invalid numeric formats cannot throw or hide the author's typo.");
            Require(tokens.Format("{rt:F1}") == "{rt:F1}", "A mistaken format on a button label is harmless.");
            foreach (string template in new[] { null, "", "{", "}", "{value", "{value:}", "{{unknown}}", "{target:invalid}" })
                Require(tokens.Format(template) != null, "Incomplete or malformed authoring input remains safe: " + template);
        }

        private static void CheckSaveAndUndo(TutorialTextContent original)
        {
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/TutorialTextValidationScratch.asset");
            TutorialTextContent scratch = Object.Instantiate(original);
            try
            {
                scratch.name = "Tutorial text validation scratch";
                AssetDatabase.CreateAsset(scratch, path);
                string initial = scratch.Driving.Forward.Action;
                using (var serialized = new SerializedObject(scratch))
                {
                    var action = serialized.FindProperty("Driving.Forward.Action");
                    Require(action != null, "Inspector exposes the nested action field.");
                    Undo.IncrementCurrentGroup();
                    Undo.SetCurrentGroupName("Tutorial text validation edit");
                    action.stringValue = "Saved Inspector edit";
                    Require(serialized.ApplyModifiedProperties(), "An Inspector content edit is applied.");
                    Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); serialized.Update();
                    Require(action.stringValue == initial, "Undo restores the authored content before the edit.");
                    Undo.PerformRedo(); serialized.Update();
                    Require(action.stringValue == "Saved Inspector edit", "Redo restores the Inspector content edit.");
                }
                EditorUtility.SetDirty(scratch); AssetDatabase.SaveAssetIfDirty(scratch);
                Undo.ClearUndo(scratch); Resources.UnloadAsset(scratch); scratch = null;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                scratch = AssetDatabase.LoadAssetAtPath<TutorialTextContent>(path);
                Require(scratch != null && scratch.Driving.Forward.Action == "Saved Inspector edit",
                    "Saved content survives unloading and AssetDatabase reimport.");
            }
            finally
            {
                if (scratch != null) Undo.ClearUndo(scratch);
                AssetDatabase.DeleteAsset(path);
            }
        }

        private static void CheckSceneReferences()
        {
            string guid = AssetDatabase.AssetPathToGUID(ContentPath);
            string scene = File.ReadAllText(TutorialDrivingLesson.ScenePath);
            Require(scene.Split(new[] { "guid: " + guid }, StringSplitOptions.None).Length - 1 == 9,
                "The eight main lessons and contextual recovery reference one shared content asset.");
            foreach (string sceneGuid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(sceneGuid);
                if (path == TutorialDrivingLesson.ScenePath) continue;
                Require(!File.ReadAllText(path).Contains("guid: " + guid), "Content is isolated from unrelated scenes: " + path);
            }
        }

        private static void CheckActualLessonViews()
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            float timeBefore = Time.timeScale;
            var copy = ScriptableObject.CreateInstance<TutorialTextContent>();
            TutorialDialoguePanel panel = null;
            try
            {
                Time.timeScale = 1;
                var owner = new GameObject("Tutorial text content preview");
                SceneManager.MoveGameObjectToScene(owner, scene);
                panel = owner.AddComponent<TutorialDialoguePanel>();
                if (panel.Document == null) Invoke(panel, "Awake");
                var fixture = new GameObject("Inactive text lesson fixture"); fixture.SetActive(false);
                SceneManager.MoveGameObjectToScene(fixture, scene);
                var bootstrap = fixture.AddComponent<HeightMapPlayerSceneBootstrap>();
                bootstrap.robot = fixture.AddComponent<RobotMover>();
                var driving = fixture.AddComponent<TutorialDrivingLesson>();
                var terrain = fixture.AddComponent<TutorialTerrainLesson>();
                Set(driving, "panel", panel); Set(driving, "tutorialText", copy);
                Set(terrain, "driving", driving); Set(terrain, "bootstrap", bootstrap); Set(terrain, "tutorialText", copy);
                string drivingRules = EditorJsonUtility.ToJson(driving), terrainRules = EditorJsonUtility.ToJson(terrain);
                copy.Panel.Header = "GUIDANCE";
                copy.Driving.WaitingStatus = "Custom waiting"; copy.Driving.BlockedStatus = "Custom blocked";
                copy.Driving.CompleteStatus = "Custom complete";
                foreach (DrivingTutorialStep step in Enum.GetValues(typeof(DrivingTutorialStep)))
                {
                    if (step == DrivingTutorialStep.Completed) continue;
                    var progress = new DrivingTutorialProgress();
                    typeof(DrivingTutorialProgress).GetProperty("Step").SetValue(progress, step);
                    typeof(DrivingTutorialProgress).GetProperty("Value").SetValue(progress, 2.34f);
                    Set(driving, "progress", progress); Set(driving, "feedbackRemaining", 0f);
                    string state = DrivingState(progress);
                    TutorialStepText item = copy.Driving.GetStep(step); Edit(item);
                    item.Detail = "Value {value:F1}/{target:F0}"; item.CompleteFeedback = "Edited feedback {value:F1}";
                    Invoke(driving, "RefreshPanel", true);
                    CheckEditedView(panel, "Value 2.3/" + progress.Target.ToString("F0", CultureInfo.InvariantCulture));
                    Require(Text(panel, "TutorialStatus") == (progress.IsComplete ? "Custom complete" : "Custom waiting"), "Driving status uses editable content.");
                    Invoke(driving, "RefreshPanel", false);
                    Require(Text(panel, "TutorialStatus") == (progress.IsComplete ? "Custom complete" : "Custom blocked"), "Blocked driving status uses editable content.");
                    string feedback = (string)Invoke(driving, "GetCompletionFeedback", step);
                    Require(feedback == "Edited feedback 2.3", "Each completion feedback uses the current template and measurements.");
                    Set(driving, "lastFeedback", feedback); Set(driving, "feedbackRemaining", .5f);
                    Invoke(driving, "RefreshPanel", true);
                    Require(Text(panel, "TutorialStatus") == (progress.IsComplete ? "Custom complete" : feedback), "Formatted feedback reaches the actual panel.");
                    Require(DrivingState(progress) == state, "Copy edits cannot change measured driving progress or thresholds.");
                }
                copy.Terrain.FollowMarkerStatus = "Custom marker"; copy.Terrain.WaitingScanStatus = "Custom scan";
                copy.Terrain.WaitingMarkersStatus = "Custom markers"; copy.Terrain.StopToReadStatus = "Custom stop";
                copy.Terrain.ObserveStatus = "Custom observe"; copy.Terrain.BlockedStatus = "Custom blocked";
                copy.Terrain.CompleteStatus = "Custom complete";
                foreach (TerrainTutorialStep step in Enum.GetValues(typeof(TerrainTutorialStep)))
                {
                    if (step == TerrainTutorialStep.Completed) continue;
                    var progress = (TerrainTutorialProgress)typeof(TutorialTerrainRegressionChecks)
                        .GetMethod("At", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { step });
                    Set(terrain, "progress", progress);
                    string state = TerrainState(progress);
                    TutorialStepText item = copy.Terrain.GetStep(step); Edit(item);
                    item.Detail = "Distance {distance:F1}, read {readingTarget:F0}";
                    Invoke(terrain, "RefreshPanel", true, true, true);
                    CheckEditedView(panel, "Distance " + progress.DistanceMeters.ToString("F1", CultureInfo.InvariantCulture)
                        + ", read " + progress.ReadingDuration.ToString("F0", CultureInfo.InvariantCulture));
                    string expected = step == TerrainTutorialStep.ScanTerrain ? "Custom scan" : step == TerrainTutorialStep.ReadSigns
                        ? "Custom observe" : progress.IsComplete ? "Custom complete" : "Custom marker";
                    Require(Text(panel, "TutorialStatus") == expected, "Terrain status uses the current asset for " + step);
                    Invoke(terrain, "RefreshPanel", false, false, false);
                    Require(Text(panel, "TutorialStatus") == (progress.IsComplete ? "Custom complete" : "Custom blocked"), "Blocked terrain status remains editable.");
                    if (step == TerrainTutorialStep.ReadSigns)
                    {
                        Invoke(terrain, "RefreshPanel", true, false, true);
                        Require(Text(panel, "TutorialStatus") == "Custom observe", "Acknowledged scan reading continues after its temporary markers expire.");
                        Invoke(terrain, "RefreshPanel", true, true, false);
                        Require(Text(panel, "TutorialStatus") == "Custom observe", "Reading signs while moving keeps the active lesson copy.");
                    }
                    Require(TerrainState(progress) == state, "Copy edits cannot change terrain progress, destinations or reading time.");
                }
                Require(EditorJsonUtility.ToJson(driving) == drivingRules && EditorJsonUtility.ToJson(terrain) == terrainRules,
                    "Content editing leaves all serialized lesson rules, routes and asset references intact.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(copy);
                Time.timeScale = timeBefore;
            }
        }

        private static void Edit(TutorialStepText item)
        {
            item.ControlGlyph = "EDIT"; item.Action = "Edited action"; item.Message = "Edited message {rt} / {lt} / {lb}";
        }

        private static void CheckEditedView(TutorialDialoguePanel panel, string detail)
        {
            Require(Text(panel, "TutorialHeader") == "GUIDANCE", "Shared panel copy reaches the real header.");
            Require(Text(panel, "TutorialControl") == "EDIT" && Text(panel, "TutorialAction") == "Edited action", "Edited control and action reach the real panel.");
            string message = Text(panel, "TutorialMessage");
            Require(message.StartsWith("Edited message ", StringComparison.Ordinal) && !message.Contains("{"), "Edited dialogue resolves all controller tokens.");
            Require(Text(panel, "TutorialDetail") == detail, "Edited detail resolves live progress numbers.");
        }

        private static string DrivingState(DrivingTutorialProgress p) => p.Step + "|" + p.Value + "|" + p.Target + "|" + p.MovingTurnDistanceMeters + "|" + p.MovingTurnTargetMeters;
        private static string TerrainState(TerrainTutorialProgress p) => p.Step + "|" + p.ApproachIndex + "|" + p.CurrentTarget + "|" + p.ReadingSeconds + "|" + p.DistanceMeters + "|" + p.ReadingDuration;
        private static string Text(TutorialDialoguePanel panel, string name) => panel.Root.Q<Label>(name).text;
        private static object Invoke(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Flags).Invoke(owner, args);
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Flags).SetValue(owner, value);
        private static void Require(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
    }
}
