using System;
using System.Collections.Generic;
using System.Reflection;
using AnimalGame.AnimalDebug;
using AnimalGame.Animals;
using AnimalGame.MapTest;
using AnimalGame.Tutorial;
using AnimalGame.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    public static class AnimalDebugUIRegressionChecks
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        [MenuItem("Animal Game/Validation/Run Animal Inspector Checks")]
        public static void Run()
        {
            CheckRejectedCommandsPreserveAction();
            CheckReplacementAndSpeciesEscape();
            CheckForcedFeedingCooldown();
            CheckLiveHuntAndDeferredValidation();
            CheckSamplingAndProjection();
            CheckDocumentAndModalOwnership();
            Debug.Log("Animal inspector checks PASS: rejection without interruption, species escape, forced cooldown reset, live-only hunt, deferred validation, circular sampling, edge arrows, UXML/prefab bindings and menu ownership.");
        }
        public static void RunBatch()
        {
            try { AnimalAIRegressionChecks.Run(); AnimalSpeciesRegressionChecks.Run(); Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
        private static void CheckRejectedCommandsPreserveAction()
        {
            using var f = new Fixture();
            AnimalAgent wolf = f.Animal("Wolf", new Vector2(5, 5), typeof(WolfBehaviour));
            var action = new ProbeAction(); Require(wolf.Brain.StartAction(action), "Cannot install original action");
            var rejected = AnimalDebugCommandService.Execute(wolf, AnimalDebugCommand.Aggressive);
            Require(rejected.Status == AnimalDebugCommandStatus.Rejected && rejected.Message.Contains("aggression"), "Wolf aggression was not rejected explicitly");
            Require(action.Exits == 0 && wolf.Brain.HasOverrideAction, "Rejected command interrupted the original action");
            rejected = AnimalDebugCommandService.Execute(wolf, AnimalDebugCommand.Hunt);
            Require(rejected.Status == AnimalDebugCommandStatus.Rejected && action.Exits == 0, "No-prey command changed behavior");
            wolf.ReceiveDamage(1000, null);
            Require(!AnimalDebugSelection.IsSelectable(wolf, f.Scene), "Dead animal appears in the directory");
        }
        private static void CheckReplacementAndSpeciesEscape()
        {
            using var f = new Fixture();
            AnimalAgent goat = f.Animal("Goat", new Vector2(8, 8), typeof(MountainGoatBehaviour));
            goat.Perception.Remember(f.New("Threat", f.Map.MapPositionToWorld(new Vector2(12, 8))).transform);
            var action = new ProbeAction(); goat.Brain.StartAction(action);
            goat.Motor.LockFacing(Vector2.up);
            var result = AnimalDebugCommandService.Execute(goat, AnimalDebugCommand.Fleeing);
            Require(result.Status == AnimalDebugCommandStatus.Started && action.Exits == 1, "Replacement did not exit exactly once");
            Require(goat.CurrentState == AnimalState.Fleeing && goat.Brain.CurrentActionName.Contains("Slope"), "Debug escape bypassed the goat override");
            result = AnimalDebugCommandService.Execute(goat, AnimalDebugCommand.Daily);
            Require(result.Status == AnimalDebugCommandStatus.Started && goat.CurrentState == AnimalState.Daily && !goat.Motor.IsFacingLocked, "Daily did not release movement overrides");
            goat.BeginHiding();
            Require(!AnimalDebugSelection.IsSelectable(goat, f.Scene), "Hidden animal appears in the directory");
        }
        private static void CheckForcedFeedingCooldown()
        {
            using var f = new Fixture();
            AnimalAgent corpse = f.Animal("Corpse", new Vector2(5, 5)); corpse.ReceiveDamage(1000, null);
            AnimalAgent eater = f.Animal("Eater", new Vector2(5.4f, 5));
            Set(eater.Config, "preySpecies", new List<AnimalSpeciesConfig> { corpse.Config });
            Set(eater.Config, "foodPreferences", new List<AnimalFoodPreference>());
            eater.Runtime.HuntCooldownUntil = eater.Runtime.FeedCooldownUntil = eater.Runtime.Time + 999;
            var result = AnimalDebugCommandService.Execute(eater, AnimalDebugCommand.Eat);
            Require(result.Status == AnimalDebugCommandStatus.Started, "Forced eating did not bypass existing cooldown");
            Near(eater.Runtime.FeedCooldownUntil, eater.Runtime.Time, "Old feed cooldown survived replacement");
            eater.Brain.Tick(20);
            Require(result.Status == AnimalDebugCommandStatus.Succeeded, "Forced feeding did not finish naturally: " + result.Message);
            Near(eater.Runtime.FeedCooldownUntil, eater.Runtime.Time + 300, "Natural feeding cooldown was not reset after completion");
            Require(eater.CurrentState == AnimalState.Daily && !eater.Brain.HasOverrideAction, "Completed command did not resume daily behavior");
            var occupants = new List<AnimalAgent>();
            for (int i = 0; i < 5; i++)
            { var occupant = f.Animal("Occupant" + i, new Vector2(10 + i, 10)); occupants.Add(occupant); Require(corpse.Health.Reserve(occupant), "Cannot reserve feeding slot"); }
            bool allowed = AnimalDebugCommandService.Validate(eater, AnimalDebugCommand.Eat, out string reason);
            Require(!allowed && reason.Contains("occupied"), "Full corpse did not explain capacity failure: " + reason);
        }
        private static void CheckLiveHuntAndDeferredValidation()
        {
            using var f = new Fixture();
            AnimalAgent hunter = f.Animal("Hunter", new Vector2(5, 5));
            AnimalAgent corpse = f.Animal("Old corpse", new Vector2(6, 5)); corpse.ReceiveDamage(1000, null);
            AnimalAgent live = f.Animal("Live prey", new Vector2(7, 5));
            Set(hunter.Config, "preySpecies", new List<AnimalSpeciesConfig> { corpse.Config, live.Config });
            var result = AnimalDebugCommandService.Execute(hunter, AnimalDebugCommand.Hunt);
            Require(result.Status == AnimalDebugCommandStatus.Started && hunter.Runtime.Target == live, "Hunt selected a corpse instead of live prey");
            hunter.Brain.Tick(.01f);
            Require(hunter.Runtime.Target == live, "Hunt retargeted the closer corpse");
            AnimalDebugCommandService.Execute(hunter, AnimalDebugCommand.Daily);
            AnimalDebugCommandResult queued = null;
            var action = new ProbeAction();
            action.OnTick = () =>
            {
                queued = AnimalDebugCommandService.Execute(hunter, AnimalDebugCommand.Hunt);
                Require(queued.Status == AnimalDebugCommandStatus.Queued, "Reentrant command was not queued");
                live.ReceiveDamage(1000, null);
                action.OnTick = null;
            };
            hunter.Brain.StartAction(action); hunter.Brain.Tick(.01f);
            Require(queued.Status == AnimalDebugCommandStatus.Rejected && action.Exits == 0, "Deferred validation disrupted the old action after target loss");
        }
        private static void CheckSamplingAndProjection()
        {
            var bounds = new Bounds(new Vector3(3, 4), new Vector3(2, 10, .1f));
            Require(AnimalDebugSelection.ContainsSample(bounds, new Vector2(7, 4)), "Sampling radius is not twice the width");
            Require(!AnimalDebugSelection.ContainsSample(bounds, new Vector2(3, 8.01f)), "Sampling uses height instead of width");
            Rect viewport = new Rect(100, 50, 800, 600);
            Near(AnimalDebugSelection.EdgePoint(viewport, new Vector2(1500, 350), 20).x, 880, "Right edge arrow");
            Near(AnimalDebugSelection.EdgePoint(viewport, new Vector2(500, -500), 20).y, 70, "Bottom edge arrow");
            using var f = new Fixture();
            AnimalAgent animal = f.Animal("Pickable", new Vector2(10, 10));
            Camera camera = f.New("Camera", new Vector3(4, 4, -10)).AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 5; camera.pixelRect = new Rect(0, 0, 800, 600);
            Require(AnimalDebugSelection.TryBounds(animal, out Bounds body), "Physical fallback bounds unavailable");
            camera.transform.position = body.center + Vector3.back * 10;
            Vector2 screen = camera.WorldToScreenPoint(body.center);
            Require(AnimalDebugSelection.Pick(camera, screen, new[] { animal }) == animal, "World projection failed to select the animal");
        }
        private static void CheckDocumentAndModalOwnership()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/AnimalAi/AnimalAi.prefab");
            Require(prefab != null && prefab.GetComponent<AnimalDebugUI>() != null, "Inspector prefab/controller reference is broken");
            GameObject hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Resources/UI/MainUI.prefab");
            Require(hudPrefab != null, "Main HUD prefab is missing");
            var hud = new SerializedObject(hudPrefab.GetComponent<AnimalGame.MainUI.MainUI>());
            Require(hud.FindProperty("animalDebugPrefab").objectReferenceValue == prefab, "Main HUD does not instantiate the inspector prefab");
            UIDocument source = prefab.GetComponent<UIDocument>();
            Require(source != null && source.visualTreeAsset != null, "UXML reference is broken");
            var tree = source.visualTreeAsset.CloneTree();
            foreach (string name in new[] { "AnimalPanel", "AnimalName", "Sample", "AnimalList", "CommandList", "HealthFill", "Message" })
                Require(tree.Q(name) != null, "Missing UXML binding: " + name);
            using var f = new Fixture();
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, f.Scene);
            AnimalDebugUI ui = instance.GetComponent<AnimalDebugUI>();
            Invoke(ui, "Awake"); Invoke(ui, "OnEnable");
            float timeScale = Time.timeScale;
            var tutorial = f.New("Tutorial", Vector3.zero).AddComponent<TutorialDialoguePanel>();
            Invoke(tutorial, "Awake"); tutorial.SetCollapsed(false);
            Require(!ui.Open(), "Inspector opened over an expanded tutorial");
            tutorial.SetCollapsed(true); Object.DestroyImmediate(tutorial.gameObject);
            Require(ui.Open() && AnimalDebugUI.BlocksOtherPanels, "Cannot open inspector");
            Near(Time.timeScale, timeScale, "Inspector paused the simulation");
            AnimalAgent animal = f.Animal("Details", new Vector2(5, 5)); ui.Select(animal);
            Require(ui.Document.rootVisualElement.Q<Label>("Group").text == "Group size: 1", "Ungrouped count excludes self");
            Require(ui.Document.rootVisualElement.Q<Button>("Execute").enabledSelf, "Selected animal cannot execute");
            ui.Close(); Require(!AnimalDebugUI.IsOpen, "Closing left modal ownership active");
        }
        private sealed class ProbeAction : AnimalAction
        {
            public int Exits; public Action OnTick;
            public override AnimalActionStatus Tick(AnimalContext c, float dt) { OnTick?.Invoke(); return AnimalActionStatus.Running; }
            public override void Exit(AnimalContext c) { Exits++; base.Exit(c); }
        }
        private sealed class Fixture : IDisposable
        {
            public readonly Scene Scene = EditorSceneManager.NewPreviewScene();
            public readonly MapTestSceneController Map;
            private readonly Texture2D texture;
            private readonly TiledMapTestFixture tile;
            private readonly List<Object> assets = new();
            public Fixture()
            {
                Map = New("Flat map", Vector3.zero).AddComponent<MapTestSceneController>(); Map.enabled = false;
                texture = new Texture2D(2, 2); texture.SetPixels(new[] { Color.gray, Color.gray, Color.gray, Color.gray }); texture.Apply();
                tile = new TiledMapTestFixture(Map, texture, 32, .25f);
            }
            public GameObject New(string name, Vector3 position)
            { var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, Scene); go.transform.position = position; return go; }
            public AnimalAgent Animal(string name, Vector2 position, Type behaviour = null)
            {
                var config = ScriptableObject.CreateInstance<AnimalSpeciesConfig>(); assets.Add(config);
                Set(config, "initialHealthRange", new Vector2(100, 100));
                Set(config, "baseDetectionChancePerCheck", 0f); Set(config, "baseFleeChancePerCheck", 0f);
                Set(config, "dailyBehaviours", new List<AnimalDailyBehaviourSettings> { new AnimalDailyBehaviourSettings(AnimalDailyBehaviourKind.Idle, 1, 1, 1) });
                var go = New(name, Map.MapPositionToWorld(position));
                if (behaviour != null) go.AddComponent(behaviour);
                var agent = go.AddComponent<AnimalAgent>(); agent.ConfigureEditorDefaults(config, null, null);
                // Non-ExecuteAlways agents do not receive OnEnable in an EditMode preview scene.
                Invoke(agent, "OnEnable");
                Set(go.GetComponent<HeightMapPlacedObject>(), "map", Map);
                Require(agent.TryInitialize(), "Animal fixture could not initialize"); return agent;
            }
            public void Dispose()
            {
                tile.Dispose(); EditorSceneManager.ClosePreviewScene(Scene);
                foreach (Object asset in assets) Object.DestroyImmediate(asset);
                Object.DestroyImmediate(texture); WorldInteractionQuery.InvalidateSpatialIndex();
            }
        }
        private static void Invoke(object target, string method) => target.GetType().GetMethod(method, Flags).Invoke(target, null);
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Flags).SetValue(target, value);
        private static void Require(bool condition, string reason) { if (!condition) throw new Exception(reason); }
        private static void Near(float value, float expected, string reason) => Require(Mathf.Abs(value - expected) < .002f, reason + ": " + value + " != " + expected);
    }
}
