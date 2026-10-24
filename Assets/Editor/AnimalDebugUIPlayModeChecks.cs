using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using AnimalGame.AnimalDebug;
using AnimalGame.Animals;
using AnimalGame.MapTest;
using AnimalGame.Tutorial;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using MouseButton = UnityEngine.InputSystem.LowLevel.MouseButton;

namespace AnimalGame.Editor
{
    public static class AnimalDebugUIPlayModeChecks
    {
        private const string Pending = "AnimalInspector.PlayModeChecks";
        [InitializeOnLoadMethod]
        private static void Subscribe() => EditorApplication.playModeStateChanged += OnPlayState;
        public static void RunBatch()
        {
            try
            {
                AnimalAIRegressionChecks.Run();
                AnimalSpeciesRegressionChecks.Run();
                AnimalDebugUIRegressionChecks.Run();
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); return; }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(Pending, true);
            EditorApplication.EnterPlaymode();
        }
        private static void OnPlayState(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            new GameObject("Animal inspector input checks").AddComponent<AnimalInspectorInputChecks>();
        }
    }

    public sealed class AnimalInspectorInputChecks : MonoBehaviour
    {
        private Keyboard keyboard;
        private Gamepad pad;
        private Mouse mouse;
        private AnimalDebugUI ui;
        private KeyboardState heldKeyboard;
        private GamepadState heldPad;
        private MouseState heldMouse;
        private InputSettings originalInput, testInput;
        private bool originalBackground;
        private TiledMapTestFixture tile;
        private Texture2D texture;
        private readonly List<Object> assets = new();

        private IEnumerator Start()
        {
            IEnumerator checks = Checks();
            while (true)
            {
                object wait;
                try { if (!checks.MoveNext()) break; wait = checks.Current; }
                catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); yield break; }
                yield return wait;
            }
            Debug.Log("Animal inspector PlayMode checks PASS: M/Up opening, open-panel Up navigation, A/B list selection, circular world sampling, live details, proportional layout, late arrow, UI mutual exclusion unpaused simulation, indented daily commands, scrolling and one-shot execution.");
            EditorApplication.Exit(0);
        }
        private IEnumerator Checks()
        {
            keyboard = InputSystem.AddDevice<Keyboard>(); pad = InputSystem.AddDevice<Gamepad>(); mouse = InputSystem.AddDevice<Mouse>();
            originalInput = InputSystem.settings; testInput = Object.Instantiate(originalInput);
            testInput.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            testInput.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            originalBackground = Application.runInBackground; Application.runInBackground = true;
            InputSystem.settings = testInput;
            InputSystem.onBeforeUpdate += QueueInput;
            var map = new GameObject("Flat map").AddComponent<MapTestSceneController>(); map.enabled = false;
            texture = new Texture2D(2, 2); texture.SetPixels(new[] { Color.gray, Color.gray, Color.gray, Color.gray }); texture.Apply();
            tile = new TiledMapTestFixture(map, texture, 32, .25f);
            AnimalAgent first = Animal(map, "First", new Vector2(12, 12));
            AnimalAgent second = Animal(map, "Second", new Vector2(18, 12));
            Camera camera = new GameObject("Game camera").AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0, 0, -10); camera.orthographic = true; camera.orthographicSize = 5;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/AnimalAi/AnimalAi.prefab");
            ui = Object.Instantiate(prefab).GetComponent<AnimalDebugUI>();
            yield return null;
            heldKeyboard = new KeyboardState(Key.M);
            yield return null;
            heldKeyboard = new KeyboardState();
            yield return null;
            Require(AnimalDebugUI.IsOpen, "M did not open the inspector");
            Require(Mathf.Approximately(Time.timeScale, 1), "Inspector paused gameplay");
            yield return new WaitForSecondsRealtime(.2f);
            VisualElement root = ui.Document.rootVisualElement, panel = root.Q("AnimalPanel");
            Require(root.panel != null && panel.resolvedStyle.display == DisplayStyle.Flex, "Inspector document is not attached");
            Require(Mathf.Abs(panel.layout.width / root.layout.width - 1f / 3) < .015f, "Panel width is not one third");
            Require(Mathf.Abs(panel.layout.height / root.layout.height - 2f / 3) < .015f, "Panel height is not two thirds");
            Color gold = new Color32(0xD4, 0xB8, 0x54, 0xFF);
            Require(root.Q("HealthFill").resolvedStyle.backgroundColor == gold
                && root.Q<Label>("Message").resolvedStyle.color == gold, "Inspector accents do not use D4B854");
            Focusable originalFocus = root.focusController.focusedElement;
            foreach (Key movement in new[] { Key.W, Key.A, Key.S, Key.D })
            {
                heldKeyboard = new KeyboardState(movement);
                yield return new WaitForSecondsRealtime(.1f);
                Require(root.focusController.focusedElement == originalFocus, "WASD changed inspector control focus: " + movement);
            }
            heldKeyboard = new KeyboardState(); heldPad = new GamepadState { leftStick = Vector2.down };
            yield return new WaitForSecondsRealtime(.5f);
            Require(root.focusController.focusedElement == originalFocus, "Left stick changed inspector control focus");
            heldKeyboard = new KeyboardState(); heldPad = new GamepadState(); yield return null;
            heldPad = new GamepadState().WithButton(GamepadButton.DpadUp);
            yield return null;
            heldPad = new GamepadState(); yield return null;
            Require(AnimalDebugUI.IsOpen, "D-pad Up closed the panel instead of navigating");
            Invoke("ToggleAnimals"); yield return null;
            Require(root.Q<ListView>("AnimalList").itemsSource.Count == 2, "Scene animal catalog is incorrect");
            int originalAnimalIndex = (int)typeof(AnimalDebugUI).GetField("animalIndex", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ui);
            heldKeyboard = new KeyboardState(Key.W, Key.A, Key.S, Key.D);
            heldPad = new GamepadState { leftStick = Vector2.down };
            yield return new WaitForSecondsRealtime(.5f);
            Require((int)typeof(AnimalDebugUI).GetField("animalIndex", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ui) == originalAnimalIndex,
                "Gameplay movement changed the animal list selection");
            heldKeyboard = new KeyboardState(); heldPad = new GamepadState();
            yield return null;
            Require(root.Q("AnimalArrow").resolvedStyle.display == DisplayStyle.Flex, "Focused animal has no arrow");
            heldPad = new GamepadState().WithButton(GamepadButton.South);
            yield return null;
            heldPad = new GamepadState(); yield return null;
            Require(ui.Selected != null && root.Q("AnimalPopup").resolvedStyle.display == DisplayStyle.None, "A did not select and close the list");
            Require(root.Q("AnimalArrow").resolvedStyle.display == DisplayStyle.Flex, "Selected animal lost its persistent arrow");
            Require(root.Q("AnimalArrow").resolvedStyle.color == gold, "Arrow does not use D4B854");
            Require(root.Q<Label>("Group").text == "Group size: 1", "Live group details are incorrect");
            Invoke("ToggleAnimals"); yield return null;
            heldPad = new GamepadState().WithButton(GamepadButton.DpadDown);
            yield return null;
            heldPad = new GamepadState(); yield return null;
            Require(root.Q("AnimalArrow").resolvedStyle.display == DisplayStyle.Flex
                && root.Q("AnimalPreviewArrow").resolvedStyle.display == DisplayStyle.Flex, "Preview replaced the selected animal marker");
            heldPad = new GamepadState().WithButton(GamepadButton.East);
            yield return null;
            heldPad = new GamepadState(); yield return null;
            Require(AnimalDebugUI.IsOpen && root.Q("AnimalPopup").resolvedStyle.display == DisplayStyle.None, "B did not close only the list");
            Require(root.Q("AnimalArrow").resolvedStyle.display == DisplayStyle.Flex
                && root.Q("AnimalPreviewArrow").resolvedStyle.display == DisplayStyle.None, "Closing the list removed the selected marker or retained its preview");
            // Expand/select through real gamepad input and force a long virtualized
            // list so navigation must scroll to the selected specific daily entry.
            ui.Select(first);
            var dailyEntries = new List<AnimalDailyBehaviourSettings>();
            for (int i = 0; i < 24; i++) dailyEntries.Add(new AnimalDailyBehaviourSettings(AnimalDailyBehaviourKind.Idle, 0, .1f, .1f));
            Set(first.Config, "dailyBehaviours", dailyEntries);
            Invoke("ToggleCommands"); yield return null;
            ListView commands = root.Q<ListView>("CommandList");
            Require(commands.itemsSource.Count == 6, "Closed groups did not show only general behaviours");
            heldPad = new GamepadState().WithButton(GamepadButton.South); yield return null;
            heldPad = new GamepadState(); yield return null;
            Require(commands.itemsSource.Count == 31, "Daily expansion lost automatic mode or configured entries");
            for (int i = 0; i < 25; i++)
            {
                heldPad = new GamepadState().WithButton(GamepadButton.DpadDown); yield return null;
                heldPad = new GamepadState(); yield return null;
            }
            yield return new WaitForSecondsRealtime(.1f);
            ScrollView commandScroll = commands.Q<ScrollView>();
            Require(commandScroll.scrollOffset.y > 0 && commandScroll.verticalScroller.resolvedStyle.display == DisplayStyle.Flex,
                "Long behaviour list did not expose its scrollbar or scroll with D-pad navigation");
            heldPad = new GamepadState().WithButton(GamepadButton.South); yield return null;
            heldPad = new GamepadState(); yield return null;
            Require(root.Q("CommandPopup").resolvedStyle.display == DisplayStyle.None
                && root.Q<Button>("Command").text.Contains("Element 23"), "A did not select the indented daily item");
            Require(!first.Brain.HasOverrideAction, "Selecting a child immediately executed it");
            Invoke("Execute");
            Require(first.Brain.HasOverrideAction, "Execute did not start the specific daily behaviour");
            yield return new WaitForSecondsRealtime(.3f);
            Require(!first.Brain.HasOverrideAction && root.Q<Label>("Message").text.Contains("Completed"),
                "Specific behaviour did not complete once and return to autonomous Daily");
            ui.Select(second); Invoke("ToggleCommands"); yield return null;
            Require(commands.itemsSource.Count == 6 && root.Q<Button>("Command").text.Contains("autonomous"),
                "Switching animals retained stale daily actions");
            heldPad = new GamepadState().WithButton(GamepadButton.East); yield return null;
            heldPad = new GamepadState(); yield return null;
            Invoke("ToggleSampling"); yield return null;
            AnimalDebugSelection.TryBounds(first, out Bounds bounds);
            Vector2 point = camera.WorldToScreenPoint(bounds.center);
            heldMouse = new MouseState { position = point }.WithButton(MouseButton.Left);
            yield return null;
            heldMouse = new MouseState { position = point }; yield return null;
            Require(ui.Selected == first && root.Q<Button>("Sample").text == "Pick", "World click did not finish sampling");
            ui.Close(); yield return null;
            heldPad = new GamepadState().WithButton(GamepadButton.DpadUp);
            yield return null;
            heldPad = new GamepadState(); yield return null;
            Require(AnimalDebugUI.IsOpen, "D-pad Up did not open the closed inspector");
            var tutorial = new GameObject("Tutorial ownership check").AddComponent<TutorialDialoguePanel>();
            yield return null;
            Require(tutorial.IsCollapsed, "New tutorial expanded over the inspector");
            tutorial.SetCollapsed(false);
            Require(tutorial.IsCollapsed, "Tutorial expanded while the inspector owned the UI");
            ui.Close(); yield return null;
            tutorial.SetCollapsed(false);
            Require(!ui.Open(), "Inspector opened over the tutorial");
            Object.Destroy(tutorial.gameObject);
        }
        private AnimalAgent Animal(MapTestSceneController map, string name, Vector2 position)
        {
            var config = ScriptableObject.CreateInstance<AnimalSpeciesConfig>(); assets.Add(config);
            Set(config, "baseDetectionChancePerCheck", 0f);
            Set(config, "dailyBehaviours", new List<AnimalDailyBehaviourSettings> { new AnimalDailyBehaviourSettings(AnimalDailyBehaviourKind.Idle, 1, 100, 100) });
            var go = new GameObject(name); go.transform.position = map.MapPositionToWorld(position);
            var agent = go.AddComponent<AnimalAgent>(); agent.ConfigureEditorDefaults(config, null, null);
            Set(go.GetComponent<HeightMapPlacedObject>(), "map", map);
            Require(agent.TryInitialize(), "PlayMode animal initialization failed"); return agent;
        }
        private void Invoke(string method) => typeof(AnimalDebugUI).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(ui, null);
        private void QueueInput()
        {
            if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
            keyboard.MakeCurrent(); mouse.MakeCurrent(); pad.MakeCurrent();
            InputSystem.QueueStateEvent(keyboard, heldKeyboard);
            InputSystem.QueueStateEvent(pad, heldPad);
            InputSystem.QueueStateEvent(mouse, heldMouse);
        }
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        private static void Require(bool valid, string reason) { if (!valid) throw new Exception(reason); }
        private void OnDestroy()
        {
            InputSystem.onBeforeUpdate -= QueueInput;
            if (originalInput != null) InputSystem.settings = originalInput;
            Application.runInBackground = originalBackground;
            if (testInput != null) Object.Destroy(testInput);
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (pad != null && pad.added) InputSystem.RemoveDevice(pad);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            tile?.Dispose();
            foreach (Object asset in assets) if (asset != null) Object.Destroy(asset);
            if (texture != null) Object.Destroy(texture);
        }
    }
}
