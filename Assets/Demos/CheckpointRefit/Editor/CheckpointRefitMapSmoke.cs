using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
    /// <summary>
    /// Explicit integration check in a disposable project mirror. Opens the saved map read-only,
    /// adds the maintenance fixture only in Play Mode, and never saves any scene or source asset.
    /// Run without -quit; this session exits after its asynchronous Play Mode checks finish.
    /// </summary>
    public sealed class CheckpointRefitMapSmoke : ScriptableObject
    {
        private const string MapPath = "Assets/Scenes/GameMaps/Official_Map_Test_02.unity";
        private const string SourceProject = "C:/Users/30791/Documents/GitHub/Capstone_AnimalGame";
        private enum Phase
        {
            WaitingForPlay, WaitingForScene, Neutral, Opening, Opened, Challenge,
            ChallengeReleased, Closing, ClosedReleased, DisableCleanup, Disabled,
            Reenabled, DestroyCleanup, Finishing
        }

        [Serializable] private sealed class SourceHash { public string path, hash; }
        [Serializable] private sealed class Report
        {
            public string startedUtc, finishedUtc, isolatedProject, scene = MapPath;
            public bool passed;
            public List<string> checks = new List<string>();
            public List<string> errors = new List<string>();
            public List<SourceHash> sources = new List<SourceHash>();
        }

        [SerializeField] private bool running;
        [SerializeField] private Phase phase;
        [SerializeField] private Report report;
        [SerializeField] private string outputDirectory;
        [SerializeField] private double deadline, phaseStarted;
        [SerializeField] private int lastFrame = -1, phaseFrame, gamepadId, keyboardId;
        [SerializeField] private Key[] heldKeys = Array.Empty<Key>();
        [SerializeField] private InputSettings previousInputSettings, smokeInputSettings;
        [SerializeField] private bool previousRunInBackground, inputSettingsOwned;
        [SerializeField] private HeightMapPlayerSceneBootstrap bootstrap;
        [SerializeField] private RobotMover player;
        [SerializeField] private AnimalGame.MainUI.MainUI hud;
        [SerializeField] private ScanChargeUI scan;
        [SerializeField] private CheckpointRefitTerminal terminal;
        [SerializeField] private List<Behaviour> captured = new List<Behaviour>();
        [SerializeField] private List<bool> enabledBefore = new List<bool>();
        [SerializeField] private float previousTimeScale;
        [SerializeField] private bool previousCursorVisible;
        [SerializeField] private CursorLockMode previousCursorLock;
        [SerializeField] private Vector3 openingPosition;
        [SerializeField] private Quaternion openingRotation;
        [SerializeField] private RenderTexture playerPreviewTexture;

        public static void RunBatch()
        {
            try
            {
                Require(Application.isBatchMode, "Map integration smoke is batch-only.");
                Require(!Environment.GetCommandLineArgs().Any(argument => string.Equals(argument, "-quit", StringComparison.OrdinalIgnoreCase)),
                    "Do not pass -quit to the asynchronous map smoke.");
                string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd('/', '\\');
                Require(!string.Equals(project.Replace('\\', '/'), SourceProject, StringComparison.OrdinalIgnoreCase),
                    "Refusing to run in the original project. Use an isolated project mirror.");
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Start in Edit Mode.");
                Require(!Resources.FindObjectsOfTypeAll<CheckpointRefitMapSmoke>().Any(session => session.running),
                    "A refit map smoke session is already running.");
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    Require(!SceneManager.GetSceneAt(i).isDirty, "Refusing to replace an unsaved scene.");
                Require(AssetDatabase.LoadAssetAtPath<SceneAsset>(MapPath) != null, "The saved test map is missing from the mirror.");
                Require(AssetDatabase.LoadAssetAtPath<GameObject>(CheckpointRefitValidation.TerminalPath) != null,
                    "Build the independent terminal prefab before the map smoke.");

                var session = CreateInstance<CheckpointRefitMapSmoke>();
                session.hideFlags = HideFlags.HideAndDontSave;
                session.outputDirectory = OutputDirectory();
                Directory.CreateDirectory(session.outputDirectory);
                session.report = new Report { startedUtc = DateTime.UtcNow.ToString("O"), isolatedProject = project };
                foreach (string path in SourcePaths().Distinct())
                    if (File.Exists(path)) session.report.sources.Add(new SourceHash { path = path, hash = Hash(path) });
                EditorSceneManager.OpenScene(MapPath, OpenSceneMode.Single);
                session.running = true;
                session.deadline = EditorApplication.timeSinceStartup + 180d;
                session.Begin(Phase.WaitingForPlay);
                session.Subscribe();
                session.SaveReport();
                Debug.Log("Checkpoint Refit real-map smoke starting: " + session.outputDirectory);
                EditorApplication.isPlaying = true;
            }
            catch (Exception exception)
            {
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
                        "Map Play Mode ended before integration checks completed.");
                    return;
                }
                if (lastFrame == Time.frameCount) return;
                lastFrame = Time.frameCount;
                switch (phase)
                {
                    case Phase.WaitingForPlay:
                        InstallInputSettings();
                        gamepadId = InputSystem.AddDevice<Gamepad>("Refit map smoke gamepad").deviceId;
                        keyboardId = InputSystem.AddDevice<Keyboard>("Refit map smoke keyboard").deviceId;
                        Neutral(); Begin(Phase.WaitingForScene); break;
                    case Phase.WaitingForScene:
                        if (!ResolveReadyScene()) break;
                        CreateFixture(); Neutral(); Begin(Phase.Neutral); break;
                    case Phase.Neutral:
                        if (!Settled(8)) break;
                        Require(terminal.IsNearby, "Fixture terminal did not register the same-scene map player nearby.");
                        Require(RefitGameplayLease.CanAcquire(player, out string reason), "Authored player is not ready for maintenance: " + reason);
                        Snapshot();
                        Pad(new GamepadState().WithButton(GamepadButton.South)); Begin(Phase.Opening); break;
                    case Phase.Opening:
                        if (!Settled()) break;
                        CheckOpen();
                        CheckpointRefitPreviewValidation.CheckSourceArtworkDependency(player);
                        report.checks.Add("The live map player's actual marker artwork drives the preview; capture and disposal preserve its source renderers.");
                        Record(terminal.IsOpen, "Actual Gamepad A opens maintenance beside the map player.");
                        Neutral(); Begin(Phase.Opened); break;
                    case Phase.Opened:
                        if (!Settled()) break;
                        Pad(new GamepadState { leftStick = new Vector2(.75f, 1f), rightTrigger = 1f }
                            .WithButton(GamepadButton.South).WithButton(GamepadButton.LeftShoulder)
                            .WithButton(GamepadButton.RightShoulder).WithButton(GamepadButton.DpadRight));
                        Keys(Key.N, Key.P, Key.W, Key.E, Key.Enter);
                        Begin(Phase.Challenge); break;
                    case Phase.Challenge:
                        if (!Settled(12) || EditorApplication.timeSinceStartup - phaseStarted < .8d) break;
                        CheckOpen();
                        Record(!hud.IsInventoryOpen && hud.InventoryProgress < .001f,
                            "The real MainUI inventory ignores held D-pad right and keyboard N during maintenance.");
                        Record(bootstrap.photoMode == null || !bootstrap.photoMode.IsActive,
                            "Photo mode ignores right shoulder and keyboard P during maintenance.");
                        Record(!scan.IsCharging && scan.Charge01 < .001f
                            && (bootstrap.bioScan == null || !bootstrap.bioScan.IsScanning),
                            "The real scan HUD cannot charge or emit a scan while maintenance owns input.");
                        Record(bootstrap.armController == null || !bootstrap.armController.IsArmModeActive,
                            "Confirmation input cannot activate the mechanical arms in maintenance.");
                        Record(Vector3.Distance(player.transform.position, openingPosition) < .0001f
                            && Quaternion.Angle(player.transform.rotation, openingRotation) < .001f,
                            "Map player position and heading remain unchanged under movement input.");
                        CheckAllLocked(); Neutral(); Begin(Phase.ChallengeReleased); break;
                    case Phase.ChallengeReleased:
                        if (!Settled()) break;
                        Pad(new GamepadState().WithButton(GamepadButton.East)); Begin(Phase.Closing); break;
                    case Phase.Closing:
                        if (!Settled()) break;
                        Record(!terminal.IsOpen && !player.enabled && !hud.enabled && !scan.enabled,
                            "B closes maintenance while the held closing input still suspends gameplay and HUD input.");
                        Neutral(); Begin(Phase.ClosedReleased); break;
                    case Phase.ClosedReleased:
                        if (!Settled(8)) break;
                        CheckRestored("Normal close and input release");
                        Require(terminal.TryOpen(), "Disable fixture could not reopen maintenance.");
                        CheckOpen(); terminal.enabled = false; Begin(Phase.DisableCleanup); break;
                    case Phase.DisableCleanup:
                        if (!Settled()) break;
                        Record(!terminal.IsOpen, "Disabling a map terminal closes its UI.");
                        CheckRestored("Terminal disable");
                        terminal.enabled = true; Neutral(); Begin(Phase.Disabled); break;
                    case Phase.Disabled:
                        if (!Settled(8)) break;
                        Require(terminal.TryOpen(), "Terminal failed to reopen after re-enable.");
                        CheckOpen(); Begin(Phase.Reenabled); break;
                    case Phase.Reenabled:
                        if (!Settled()) break;
                        Object.Destroy(terminal.gameObject); Begin(Phase.DestroyCleanup); break;
                    case Phase.DestroyCleanup:
                        if (!Settled(5)) break;
                        CheckRestored("Terminal destruction"); Finish(null); break;
                }
            }
            catch (Exception exception) { Finish(exception); }
        }

        private bool ResolveReadyScene()
        {
            if (bootstrap == null)
                bootstrap = Object.FindObjectsByType<HeightMapPlayerSceneBootstrap>(FindObjectsSortMode.None)
                    .FirstOrDefault(candidate => candidate.gameObject.scene.path == MapPath);
            if (bootstrap == null || bootstrap.map == null || !bootstrap.map.HasGeneratedMap
                || bootstrap.robot == null || bootstrap.mainUiObject == null || bootstrap.scanChargeUi == null) return false;
            player = bootstrap.robot;
            hud = bootstrap.mainUiObject.GetComponentInChildren<AnimalGame.MainUI.MainUI>(true);
            scan = bootstrap.scanChargeUi;
            if (hud == null || !hud.isActiveAndEnabled || !scan.isActiveAndEnabled || !player.isActiveAndEnabled) return false;
            UIDocument document = hud.GetComponent<UIDocument>();
            if (document == null || document.rootVisualElement.panel == null || document.rootVisualElement.childCount == 0) return false;
            return RefitGameplayLease.CanAcquire(player, out _);
        }

        private void CreateFixture()
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(CheckpointRefitValidation.TerminalPath);
            GameObject instance = Object.Instantiate(asset);
            SceneManager.MoveGameObjectToScene(instance, player.gameObject.scene);
            instance.name = "Checkpoint Refit map smoke fixture (unsaved)";
            instance.hideFlags = HideFlags.DontSave;
            instance.transform.position = player.transform.position;
            // Only fixture geometry may be changed: the terminal is interaction-only in this test.
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (Collider2D collider in instance.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
            terminal = instance.GetComponent<CheckpointRefitTerminal>();
            Require(terminal != null, "Reusable terminal prefab has no controller.");
            terminal.Bind(player);
            Record(terminal.gameObject.scene == player.gameObject.scene,
                "Runtime-only terminal prefab fixture shares the original map player's scene.");
        }

        private void Snapshot()
        {
            previousTimeScale = Time.timeScale;
            previousCursorVisible = Cursor.visible;
            previousCursorLock = Cursor.lockState;
            openingPosition = player.transform.position;
            openingRotation = player.transform.rotation;
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
            Require(terminal.IsOpen && terminal.View != null && terminal.View.IsVisible && terminal.View.isActiveAndEnabled,
                "Maintenance did not open a visible active UI instance.");
            Require(!player.enabled && !hud.enabled && !scan.enabled,
                "The real map player, MainUI, and ScanChargeUI input consumers must be suspended.");
            Require(Mathf.Abs(Time.timeScale) < .0001f, "Maintenance must pause the map.");
            Require(terminal.View.GetComponent<UIDocument>().rootVisualElement.panel != null,
                "Dedicated maintenance UIDocument must attach while the original HUD is suspended.");
            playerPreviewTexture = CheckpointRefitPreviewValidation.CheckActualPlayerImage(terminal.View, player);
        }

        private void CheckAllLocked()
        {
            for (int slot = 0; slot < 5; slot++)
            {
                terminal.View.NavigateSlot(1);
                VisualElement root = terminal.View.Document.rootVisualElement;
                for (int module = 0; module < 3; module++)
                {
                    terminal.View.NavigateModule(1);
                    Button card = root.Q<Button>("refit-module-" + module);
                    Require(card != null, "Missing unknown module card.");
                    string text = string.Join(" ", card.Query<TextElement>().ToList().Select(element => element.text));
                    Require(text.Contains("UNKNOWN") && text.Contains("LOCKED"), "Map UI exposed an unlocked or identified module.");
                    Button equip = root.Q<Button>("refit-equip");
                    Require(equip != null && !equip.enabledInHierarchy, "Map UI exposed an enabled equip action.");
                }
            }
            report.checks.Add("All five body slots and all module cards remain UNKNOWN / LOCKED with no enabled equip action.");
        }

        private void CheckRestored(string operation)
        {
            Require(Mathf.Abs(Time.timeScale - previousTimeScale) < .0001f, operation + " changed the previous map time scale.");
            Require(Cursor.visible == previousCursorVisible && Cursor.lockState == previousCursorLock,
                operation + " changed the previous cursor state.");
            for (int i = 0; i < captured.Count; i++)
                Require(captured[i] == null || captured[i].enabled == enabledBefore[i],
                    operation + " changed the initially enabled/disabled state of " + captured[i]?.GetType().Name + ".");
            CheckpointRefitPreviewValidation.CheckPreviewReleased(terminal != null ? terminal.View : null, playerPreviewTexture);
            Record(hud.enabled && scan.enabled && player.enabled,
                operation + " restores real map HUD input, scan input, and player movement.");
            report.checks.Add(operation + " restores every captured enabled flag, time scale, and cursor exactly.");
            report.checks.Add(operation + " clears the player image and releases its preview render target.");
        }

        private void Neutral() { Pad(default(GamepadState)); Keys(); }
        private void Pad(GamepadState state)
        {
            Gamepad gamepad = InputSystem.GetDeviceById(gamepadId) as Gamepad;
            Require(gamepad != null && gamepad.added, "Virtual gamepad was lost.");
            gamepad.MakeCurrent(); InputSystem.QueueStateEvent(gamepad, state);
        }
        private void Keys(params Key[] keys)
        {
            Keyboard keyboard = InputSystem.GetDeviceById(keyboardId) as Keyboard;
            Require(keyboard != null && keyboard.added, "Virtual keyboard was lost.");
            heldKeys = keys == null ? Array.Empty<Key>() : (Key[])keys.Clone();
        }
        private void QueueHeldKeyboard()
        {
            if (!running || phase == Phase.Finishing || InputState.currentUpdateType != InputUpdateType.Dynamic) return;
            Keyboard keyboard = InputSystem.GetDeviceById(keyboardId) as Keyboard;
            if (keyboard == null || !keyboard.added) return;
            // Runtime keyboard input needs a Dynamic event; Editor updates use a separate state buffer.
            keyboard.MakeCurrent();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(heldKeys ?? Array.Empty<Key>()));
        }
        private void InstallInputSettings()
        {
            if (inputSettingsOwned) return;
            previousInputSettings = InputSystem.settings;
            previousRunInBackground = Application.runInBackground;
            smokeInputSettings = Object.Instantiate(previousInputSettings);
            smokeInputSettings.name = "Refit map smoke input settings (runtime only)";
            smokeInputSettings.hideFlags = HideFlags.HideAndDontSave;
            inputSettingsOwned = true;
            smokeInputSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            smokeInputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            Application.runInBackground = true;
            InputSystem.settings = smokeInputSettings;
        }
        private void RestoreInputSettings()
        {
            if (!inputSettingsOwned) return;
            if (previousInputSettings != null) InputSystem.settings = previousInputSettings;
            Application.runInBackground = previousRunInBackground;
            if (smokeInputSettings != null) Object.DestroyImmediate(smokeInputSettings);
            previousInputSettings = smokeInputSettings = null;
            inputSettingsOwned = false;
        }
        private void Begin(Phase next)
        { phase = next; phaseFrame = Time.frameCount; phaseStarted = EditorApplication.timeSinceStartup; SaveReport(); }
        private bool Settled(int frames = 3) => Time.frameCount - phaseFrame >= frames
            && EditorApplication.timeSinceStartup - phaseStarted >= .3d;
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
            if (terminal != null) { terminal.Close(); terminal.enabled = false; Object.Destroy(terminal.gameObject); }
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
            foreach (SourceHash source in report.sources)
                if (!File.Exists(source.path) || source.hash != Hash(source.path))
                    report.errors.Add("Source asset changed: " + source.path);
            report.finishedUtc = DateTime.UtcNow.ToString("O"); report.passed = report.errors.Count == 0;
            if (report.passed) report.checks.Add("Saved scenes, referenced prefabs, and all ProjectSettings files retain their pre-run hashes.");
            SaveReport(); running = false;
            EditorApplication.update -= Tick; Application.logMessageReceived -= OnLog;
            InputSystem.onBeforeUpdate -= QueueHeldKeyboard;
            Debug.Log("Checkpoint Refit map integration smoke " + (report.passed ? "PASS" : "FAIL")
                + ": " + report.checks.Count + " checks; " + Path.Combine(outputDirectory, "map-smoke.json"));
            int code = report.passed ? 0 : 1; Object.DestroyImmediate(this); EditorApplication.Exit(code);
        }

        private void SaveReport()
        {
            if (report != null && !string.IsNullOrEmpty(outputDirectory))
                File.WriteAllText(Path.Combine(outputDirectory, "map-smoke.json"), JsonUtility.ToJson(report, true));
        }
        private static IEnumerable<string> SourcePaths()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets" })
                         .Concat(AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" })))
                yield return AssetDatabase.GUIDToAssetPath(guid);
            foreach (string path in Directory.GetFiles("ProjectSettings", "*", SearchOption.TopDirectoryOnly)) yield return path;
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
            return Path.GetFullPath(Path.Combine("output", "validation", "checkpoint-refit-map"));
        }
    }
}
