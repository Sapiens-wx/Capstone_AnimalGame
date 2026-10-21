using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using AnimalGame.Demos.WasteRobot;
using AnimalGame.Demos.WasteRobot.Editor;
using AnimalGame.Interaction;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using Cursor = UnityEngine.Cursor;

namespace AnimalGame.Demos.CheckpointRefit.Editor
{
    /// <summary>Batch-only regression in a disposable project mirror; never saves scenes or assets.</summary>
    public sealed class CheckpointRefitInteriorSmoke : ScriptableObject
    {
        private const string MapPath = "Assets/Scenes/GameMaps/Official_Map_Test_02.unity";
        private const string SourceProject = "C:/Users/30791/Documents/GitHub/Capstone_AnimalGame";
        private enum Phase
        {
            WaitingForPlay, WaitingForScene, Neutral, Opening, OpenHeld, OpenReleased,
            CloseHeld, CloseReleased, ExitInside, ExitOutside, Finishing
        }

        [Serializable] private sealed class Report
        {
            public string startedUtc, finishedUtc, isolatedProject, scene = MapPath;
            public string sceneHashBefore, sceneHashAfter;
            public bool passed;
            public List<string> checks = new List<string>();
            public List<string> errors = new List<string>();
            public List<string> notes = new List<string>();
        }

        [SerializeField] private bool running;
        [SerializeField] private Phase phase;
        [SerializeField] private Report report;
        [SerializeField] private string outputDirectory;
        [SerializeField] private double deadline, phaseStarted;
        [SerializeField] private int lastFrame = -1, phaseFrame, gamepadId, keyboardId, cycle, insideTransitions;
        [SerializeField] private Key[] heldKeys = Array.Empty<Key>();
        [SerializeField] private InputSettings previousInputSettings, smokeInputSettings;
        [SerializeField] private bool previousRunInBackground, inputSettingsOwned;
        [SerializeField] private HeightMapPlayerSceneBootstrap bootstrap;
        [SerializeField] private RobotMover player;
        [SerializeField] private WasteRobotBase machine;
        [SerializeField] private CheckpointRefitTerminal terminal;
        [SerializeField] private Transform terminalParent;
        [SerializeField] private Vector3 terminalLocalPosition, terminalLocalScale, openingPosition;
        [SerializeField] private Quaternion terminalLocalRotation;
        [SerializeField] private List<Behaviour> captured = new List<Behaviour>();
        [SerializeField] private List<bool> enabledBefore = new List<bool>();
        [SerializeField] private float previousTimeScale;
        [SerializeField] private bool previousCursorVisible;
        [SerializeField] private CursorLockMode previousCursorLock;
        private string InputName => cycle == 0 ? "Gamepad A / B" : "Keyboard Enter / Escape";

        public static void RunBatch()
        {
            CheckpointRefitInteriorSmoke session = null;
            try
            {
                Require(Application.isBatchMode, "Interior smoke is batch-only.");
                Require(!Environment.GetCommandLineArgs().Any(argument => string.Equals(argument, "-quit", StringComparison.OrdinalIgnoreCase)),
                    "Do not pass -quit to this asynchronous Play Mode smoke.");
                string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd('/', '\\');
                Require(!string.Equals(project.Replace('\\', '/'), SourceProject, StringComparison.OrdinalIgnoreCase),
                    "Refusing to run in the source project; use an isolated mirror.");
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Start in Edit Mode.");
                Require(!Resources.FindObjectsOfTypeAll<CheckpointRefitInteriorSmoke>().Any(value => value.running),
                    "An interior smoke session is already running.");
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    Require(!SceneManager.GetSceneAt(i).isDirty, "Refusing to replace an unsaved scene.");
                Require(AssetDatabase.LoadAssetAtPath<SceneAsset>(MapPath) != null, "The saved test map is missing.");

                session = CreateInstance<CheckpointRefitInteriorSmoke>();
                session.hideFlags = HideFlags.HideAndDontSave;
                session.outputDirectory = OutputDirectory();
                Directory.CreateDirectory(session.outputDirectory);
                session.report = new Report
                {
                    startedUtc = DateTime.UtcNow.ToString("O"), isolatedProject = project,
                    sceneHashBefore = Hash(MapPath)
                };
                session.SaveReport();
                WasteRobotPlayerBindingValidation.Run();
                session.report.checks.Add("All player-binding regression groups pass, including paused input, doorway resampling and disabled, destroyed and migrated target replacement.");
                EditorSceneManager.OpenScene(MapPath, OpenSceneMode.Single);
                session.running = true;
                session.deadline = EditorApplication.timeSinceStartup + 180d;
                session.Begin(Phase.WaitingForPlay);
                session.Subscribe();
                EditorApplication.isPlaying = true;
            }
            catch (Exception exception)
            {
                if (session != null && session.report != null)
                {
                    session.report.errors.Add(exception.ToString());
                    session.ExitBatch();
                }
                Debug.LogException(exception);
                if (Application.isBatchMode) EditorApplication.Exit(1); else throw;
            }
        }

        private void OnEnable() { if (running) Subscribe(); }
        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            InputSystem.onBeforeUpdate -= QueueHeldKeyboard;
            RestoreInputSettings();
        }
        private void Subscribe()
        {
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
            InputSystem.onBeforeUpdate -= QueueHeldKeyboard; InputSystem.onBeforeUpdate += QueueHeldKeyboard;
        }

        private void Tick()
        {
            if (!running) return;
            try
            {
                if (phase == Phase.Finishing)
                {
                    if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode) ExitBatch();
                    else if (EditorApplication.timeSinceStartup > deadline)
                    { report.errors.Add("Timed out leaving Play Mode."); ExitBatch(); }
                    return;
                }
                Require(EditorApplication.timeSinceStartup < deadline, "Timed out during " + phase + ".");
                EditorApplication.QueuePlayerLoopUpdate();
                if (!EditorApplication.isPlaying)
                {
                    Require(phase == Phase.WaitingForPlay || EditorApplication.isPlayingOrWillChangePlaymode,
                        "Play Mode ended before the interior smoke completed.");
                    return;
                }
                if (lastFrame == Time.frameCount) return;
                lastFrame = Time.frameCount;
                if (terminal != null && phase != Phase.ExitOutside) CheckInterior();
                switch (phase)
                {
                    case Phase.WaitingForPlay:
                        InstallInputSettings();
                        gamepadId = InputSystem.AddDevice<Gamepad>("Interior refit smoke gamepad").deviceId;
                        keyboardId = InputSystem.AddDevice<Keyboard>("Interior refit smoke keyboard").deviceId;
                        Neutral(); Begin(Phase.WaitingForScene); break;
                    case Phase.WaitingForScene:
                        if (!ResolveReadyScene()) break;
                        PrepareAuthoredFixture(); Neutral(); Begin(Phase.Neutral); break;
                    case Phase.Neutral:
                        if (!Settled(8)) break;
                        Require(terminal.Player == player && terminal.IsNearby, "The authored nested terminal did not resolve its nearby map player.");
                        Require(RefitGameplayLease.CanAcquire(player, out string reason), "Player not ready for maintenance: " + reason);
                        CheckMarkerAvailable();
                        report.checks.Add("The nested station marker is presented only after the map HUD publishes a valid circular-view clip.");
                        Snapshot(); PressOpen(); Begin(Phase.Opening); break;
                    case Phase.Opening:
                        if (!Settled(8)) break;
                        CheckOpen(); Begin(Phase.OpenHeld); break;
                    case Phase.OpenHeld:
                        CheckOpen();
                        if (!Settled(12)) break;
                        report.checks.Add(InputName + " opens the authored interior terminal and preserves interior state across multiple LateUpdates.");
                        Neutral(); Begin(Phase.OpenReleased); break;
                    case Phase.OpenReleased:
                        CheckOpen();
                        if (!Settled()) break;
                        PressClose(); Begin(Phase.CloseHeld); break;
                    case Phase.CloseHeld:
                        if (!Settled(8)) break;
                        Require(!terminal.IsOpen && !terminal.View.IsVisible, "The closing button did not hide maintenance.");
                        Require(Time.timeScale == 0f && !player.enabled, "Held closing input released gameplay ownership too early.");
                        CheckMarkerSuppressed();
                        report.checks.Add(InputName + " closes the UI while retaining the pause and disabled player until the closing button is released.");
                        report.checks.Add(InputName + " keeps both marker rings suppressed while the closing input still owns the gameplay lease.");
                        Neutral(); Begin(Phase.CloseReleased); break;
                    case Phase.CloseReleased:
                        if (!Settled(8)) break;
                        CheckRestored();
                        if (++cycle < 2) { Snapshot(); PressOpen(); Begin(Phase.Opening); }
                        else { MoveToPortal(-.5f); Begin(Phase.ExitInside); }
                        break;
                    case Phase.ExitInside:
                        if (!Settled(5)) break;
                        MoveToPortal(.5f); Begin(Phase.ExitOutside); break;
                    case Phase.ExitOutside:
                        if (!Settled(8)) break;
                        Record(!machine.IsInside && !machine.InteriorRoot.activeInHierarchy && machine.ExteriorRoot.activeInHierarchy,
                            "After maintenance, the original player crossing outward through the entrance still switches to the exterior.");
                        Record(machine.Target == player.transform && machine.TransitionCount == insideTransitions + 1,
                            "The outward crossing retains the automatic target and produces exactly one portal transition.");
                        Record(Time.timeScale == previousTimeScale && player.enabled,
                            "Disabling the nested terminal on a normal exit leaves gameplay restored.");
                        InteractionMarker marker = RequireMarker();
                        Record(!marker.gameObject.activeInHierarchy && !marker.PresentationEnabled
                            && !marker.OuterRing.enabled && !marker.InnerRing.enabled,
                            "Leaving the machine hides both marker rings with the inactive Interior hierarchy.");
                        CheckNestedTransform(); Finish(null); break;
                }
            }
            catch (Exception exception) { Finish(exception); }
        }

        private bool ResolveReadyScene()
        {
            if (bootstrap == null)
                bootstrap = Object.FindObjectsByType<HeightMapPlayerSceneBootstrap>(FindObjectsSortMode.None)
                    .FirstOrDefault(value => value.gameObject.scene.path == MapPath);
            if (bootstrap == null || bootstrap.map == null || !bootstrap.map.HasGeneratedMap
                || bootstrap.robot == null || bootstrap.mainUiObject == null || bootstrap.scanChargeUi == null) return false;
            player = bootstrap.robot;
            var hud = bootstrap.mainUiObject.GetComponentInChildren<AnimalGame.MainUI.MainUI>(true);
            if (hud == null || !hud.isActiveAndEnabled || !bootstrap.scanChargeUi.isActiveAndEnabled || !player.isActiveAndEnabled) return false;
            UIDocument document = hud.GetComponent<UIDocument>();
            return document != null && document.rootVisualElement.panel != null
                && document.rootVisualElement.childCount > 0 && RefitGameplayLease.CanAcquire(player, out _);
        }

        private void PrepareAuthoredFixture()
        {
            machine = Object.FindObjectsByType<WasteRobotBase>(FindObjectsSortMode.None)
                .Single(value => value.gameObject.scene == player.gameObject.scene
                    && value.InteriorRoot != null && value.InteriorRoot.GetComponentsInChildren<CheckpointRefitTerminal>(true).Length > 0);
            terminal = machine.InteriorRoot.GetComponentsInChildren<CheckpointRefitTerminal>(true).Single();
            Require(machine.AutoBindPlayer && machine.TryBindScenePlayer() && machine.Target == player.transform,
                "The saved machine must automatically resolve the original same-scene map player.");
            FieldInfo automatic = typeof(WasteRobotBase).GetField("targetWasAutoBound", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(automatic != null && (bool)automatic.GetValue(machine), "The fixture must use automatic binding, never explicit Bind.");
            terminalParent = terminal.transform.parent;
            terminalLocalPosition = terminal.transform.localPosition;
            terminalLocalRotation = terminal.transform.localRotation;
            terminalLocalScale = terminal.transform.localScale;
            Require(terminal.transform.IsChildOf(machine.InteriorRoot.transform), "Use the saved terminal nested under Interior.");
            InteractionMarker marker = RequireMarker();
            Require(marker.transform.IsChildOf(terminal.transform)
                && marker.transform.IsChildOf(machine.InteriorRoot.transform),
                "The reusable interaction marker must remain nested under the saved terminal and machine Interior.");
            Vector3 originalPosition = machine.transform.position;
            machine.transform.position += player.transform.position - terminal.transform.position;
            foreach (Collider value in machine.GetComponentsInChildren<Collider>(true)) value.enabled = false;
            foreach (Collider2D value in machine.GetComponentsInChildren<Collider2D>(true)) value.enabled = false;
            machine.SetInside(true);
            insideTransitions = machine.TransitionCount;
            report.notes.Add("Runtime-only fixture: translated the saved machine from " + originalPosition + " to " + machine.transform.position
                + " so the saved nested terminal meets the original player's flat starting position; disabled only fixture colliders. "
                + "Kept every nested local transform and used SetInside(true) to prepare the interior. No scene or prefab is saved.");
            Record(machine.Target == player.transform, "The original saved machine automatically binds the original map player without calling Bind.");
            CheckNestedTransform(); CheckInterior();
        }

        private void CheckInterior()
        {
            Require(machine != null && machine.Target == player.transform && machine.IsInside
                && machine.InteriorRoot.activeInHierarchy && !machine.ExteriorRoot.activeInHierarchy
                && terminal.isActiveAndEnabled && machine.TransitionCount == insideTransitions,
                "Interior maintenance lost its automatic target, changed portal state, or disabled the nested terminal during " + phase + ".");
        }
        private void CheckNestedTransform()
        {
            Require(terminal.transform.parent == terminalParent && terminal.transform.localPosition == terminalLocalPosition
                && terminal.transform.localRotation == terminalLocalRotation && terminal.transform.localScale == terminalLocalScale,
                "The authored nested terminal hierarchy or local transform was changed.");
        }
        private void Snapshot()
        {
            previousTimeScale = Time.timeScale; previousCursorVisible = Cursor.visible; previousCursorLock = Cursor.lockState;
            openingPosition = player.transform.position;
            captured.Clear(); enabledBefore.Clear();
            foreach (Behaviour value in player.GetComponentsInChildren<Behaviour>(true)) Capture(value);
            foreach (Behaviour value in bootstrap.mainUiObject.GetComponentsInChildren<Behaviour>(true)) Capture(value);
            Capture(bootstrap.cameraFollow); Capture(bootstrap.cameraShake); Capture(bootstrap.traversalOverlay);
        }
        private void Capture(Behaviour value)
        {
            if (value == null || captured.Contains(value)) return;
            captured.Add(value); enabledBefore.Add(value.enabled);
        }
        private void CheckOpen()
        {
            CheckInterior();
            Require(terminal.IsOpen && terminal.View != null && terminal.View.IsVisible && terminal.View.isActiveAndEnabled,
                "The nested station did not retain a visible maintenance UI.");
            Require(terminal.View.Document.rootVisualElement.panel != null, "The maintenance UI did not attach to a panel.");
            Require(Time.timeScale == 0f && !player.enabled, "Maintenance must pause and suspend the original player.");
            Require(Vector3.Distance(player.transform.position, openingPosition) < .0001f, "The paused player moved.");
            Require(machine.TryBindScenePlayer() && machine.Target == player.transform && machine.IsInside,
                "An explicit automatic-resolution retry dropped the temporarily disabled paused player.");
            CheckMarkerSuppressed();
        }
        private void CheckRestored()
        {
            CheckInterior(); CheckNestedTransform();
            Require(Time.timeScale == previousTimeScale && Cursor.visible == previousCursorVisible && Cursor.lockState == previousCursorLock,
                "Closing maintenance did not restore the previous time scale and cursor.");
            for (int i = 0; i < captured.Count; i++)
                Require(captured[i] == null || captured[i].enabled == enabledBefore[i],
                    "Closing maintenance changed the original enabled state of " + captured[i]?.GetType().Name + ".");
            Record(player.isActiveAndEnabled, InputName + " release restores the original gameplay states and retains the machine interior.");
            CheckMarkerAvailable();
            report.checks.Add(InputName + " release restores both marker rings after gameplay ownership and the circular-view clip return.");
        }
        private InteractionMarker RequireMarker()
        {
            InteractionMarker marker = terminal != null ? terminal.Marker : null;
            Require(marker != null && marker.OuterRing != null && marker.InnerRing != null,
                "The authored terminal must reference a reusable marker with both ring renderers.");
            return marker;
        }
        private void CheckMarkerAvailable()
        {
            InteractionMarker marker = RequireMarker();
            Require(marker.isActiveAndEnabled && !marker.IsSuppressed && marker.PresentationEnabled
                && marker.OuterRing.enabled && marker.InnerRing.enabled,
                "Both marker rings must be enabled when the nested terminal resumes world presentation during " + phase + ".");
            Require(Shader.GetGlobalFloat("_PlayerUiClipEnabled") > .5f
                && Shader.GetGlobalFloat("_PlayerUiClipRadiusPixels") > 0f,
                "The map must publish an enabled circular-view clip with a positive radius before marker presentation is restored.");
        }
        private void CheckMarkerSuppressed()
        {
            InteractionMarker marker = RequireMarker();
            Require(marker.IsSuppressed && !marker.PresentationEnabled
                && !marker.OuterRing.enabled && !marker.InnerRing.enabled,
                "Maintenance must suppress both marker rings throughout UI ownership and held closing input during " + phase + ".");
        }
        private void MoveToPortal(float y)
        {
            Vector2 world = machine.PortalToWorld(new Vector2(0f, y));
            player.transform.position = new Vector3(world.x, world.y, player.transform.position.z);
        }

        private void PressOpen()
        {
            if (cycle == 0) Pad(new GamepadState().WithButton(GamepadButton.South));
            else Keys(Key.Enter);
        }
        private void PressClose()
        {
            if (cycle == 0) Pad(new GamepadState().WithButton(GamepadButton.East));
            else Keys(Key.Escape);
        }
        private void Neutral() { Pad(default(GamepadState)); Keys(); }
        private void Pad(GamepadState state)
        {
            Gamepad device = InputSystem.GetDeviceById(gamepadId) as Gamepad;
            Require(device != null && device.added, "Virtual gamepad was lost.");
            device.MakeCurrent(); InputSystem.QueueStateEvent(device, state);
        }
        private void Keys(params Key[] keys) => heldKeys = keys == null ? Array.Empty<Key>() : (Key[])keys.Clone();
        private void QueueHeldKeyboard()
        {
            if (!running || phase == Phase.Finishing || InputState.currentUpdateType != InputUpdateType.Dynamic) return;
            Keyboard device = InputSystem.GetDeviceById(keyboardId) as Keyboard;
            if (device == null || !device.added) return;
            device.MakeCurrent(); InputSystem.QueueStateEvent(device, new KeyboardState(heldKeys ?? Array.Empty<Key>()));
        }
        private void InstallInputSettings()
        {
            if (inputSettingsOwned) return;
            previousInputSettings = InputSystem.settings; previousRunInBackground = Application.runInBackground;
            smokeInputSettings = Object.Instantiate(previousInputSettings);
            smokeInputSettings.hideFlags = HideFlags.HideAndDontSave;
            smokeInputSettings.name = "Interior refit smoke input settings (runtime only)";
            inputSettingsOwned = true;
            smokeInputSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            smokeInputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            Application.runInBackground = true; InputSystem.settings = smokeInputSettings;
        }
        private void RestoreInputSettings()
        {
            if (!inputSettingsOwned) return;
            if (previousInputSettings != null) InputSystem.settings = previousInputSettings;
            Application.runInBackground = previousRunInBackground;
            if (smokeInputSettings != null) Object.DestroyImmediate(smokeInputSettings);
            previousInputSettings = smokeInputSettings = null; inputSettingsOwned = false;
        }
        private void Begin(Phase next)
        { phase = next; phaseFrame = Time.frameCount; phaseStarted = EditorApplication.timeSinceStartup; SaveReport(); }
        private bool Settled(int frames = 3) => Time.frameCount - phaseFrame >= frames && EditorApplication.timeSinceStartup - phaseStarted >= .3d;
        private void Record(bool condition, string message) { Require(condition, message); report.checks.Add(message); }
        private static void Require(bool condition, string message) => CheckpointRefitValidation.Require(condition, message);
        private void OnLog(string message, string stack, LogType type)
        {
            if (!running || phase == Phase.Finishing) return;
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            { report.errors.Add(type + ": " + message + "\n" + stack); SaveReport(); }
        }
        private void Finish(Exception failure)
        {
            if (phase == Phase.Finishing) return;
            if (failure != null) report.errors.Add(failure.ToString());
            InputSystem.onBeforeUpdate -= QueueHeldKeyboard;
            if (terminal != null) terminal.enabled = false;
            foreach (int id in new[] { gamepadId, keyboardId })
            {
                InputDevice device = InputSystem.GetDeviceById(id);
                if (device != null && device.added) InputSystem.RemoveDevice(device);
            }
            RestoreInputSettings();
            phase = Phase.Finishing; deadline = EditorApplication.timeSinceStartup + 20d;
            SaveReport(); EditorApplication.isPlaying = false;
        }
        private void ExitBatch()
        {
            RestoreInputSettings();
            report.sceneHashAfter = Hash(MapPath);
            if (report.sceneHashAfter != report.sceneHashBefore) report.errors.Add("The saved map scene changed during validation.");
            else report.checks.Add("The saved Official_Map_Test_02 scene hash is unchanged after the runtime-only test.");
            report.finishedUtc = DateTime.UtcNow.ToString("O"); report.passed = report.errors.Count == 0;
            SaveReport(); running = false;
            EditorApplication.update -= Tick; Application.logMessageReceived -= OnLog; InputSystem.onBeforeUpdate -= QueueHeldKeyboard;
            Debug.Log("Checkpoint Refit interior smoke " + (report.passed ? "PASS" : "FAIL") + ": "
                + report.checks.Count + " checks; " + Path.Combine(outputDirectory, "interior-smoke.json"));
            int code = report.passed ? 0 : 1; Object.DestroyImmediate(this); EditorApplication.Exit(code);
        }
        private void SaveReport()
        {
            if (report != null && !string.IsNullOrEmpty(outputDirectory))
                File.WriteAllText(Path.Combine(outputDirectory, "interior-smoke.json"), JsonUtility.ToJson(report, true));
        }
        private static string Hash(string path)
        {
            using (SHA256 algorithm = SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
        }
        private static string OutputDirectory()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], "-logFile", StringComparison.OrdinalIgnoreCase) && args[i + 1] != "-")
                    return Path.GetDirectoryName(Path.GetFullPath(args[i + 1]));
            return Path.GetFullPath(Path.Combine("output", "validation", "checkpoint-refit-interior"));
        }
    }
}
