using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using AnimalGame.RobotArm;
using AnimalGame.RobotMap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using Cursor = UnityEngine.Cursor;

namespace AnimalGame.Demos.CheckpointRefit.Editor
{
    /// <summary>
    /// Explicit isolated-project batch runner. No global initialization or automatic asset writes.
    /// Run without -quit; the session exits after leaving Play Mode and writing its report.
    /// </summary>
    public sealed class CheckpointRefitPlayModeSmoke : ScriptableObject
    {
        private enum Phase
        {
            WaitingForPlay, Warmup, Outside, Nearby, PadOpen, PadHeld, OpenReleased,
            PadDown, PadDownReleased, PadUp, PadUpReleased, KeyDown, KeyDownReleased,
            KeyUp, KeyUpReleased, FocusMoved, FocusRestored,
            ModalOpen, ModalBack, BlockedInputs, PrepareCapture, Capture, CloseSetup,
            PadClose, PadReleased, KeyboardOpen, KeyboardReleased, EscapeModal,
            EscapeRelease, EscapeClose, KeyboardClosed, DisableCleanup, DisableReleased,
            DestroyCleanup, Finishing
        }

        [Serializable] private sealed class SourceHash { public string path; public string hash; }
        [Serializable] private sealed class Report
        {
            public string startedUtc, finishedUtc;
            public bool passed;
            public List<string> checks = new List<string>();
            public List<string> errors = new List<string>();
            public List<string> notes = new List<string>();
            public List<string> screenshots = new List<string>();
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
        [SerializeField] private CheckpointRefitTerminal terminal;
        [SerializeField] private RobotMover player;
        [SerializeField] private Vector3 playerPosition;
        [SerializeField] private Quaternion playerRotation;
        [SerializeField] private List<Behaviour> guarded = new List<Behaviour>();
        [SerializeField] private List<bool> enabledBefore = new List<bool>();
        [SerializeField] private float originalTimeScale;
        [SerializeField] private bool originalCursorVisible;
        [SerializeField] private CursorLockMode originalCursorLock;
        [SerializeField] private PanelSettings capturePanel;
        [SerializeField] private RenderTexture captureTexture, priorTarget;
        [SerializeField] private int selectedBeforeHold;
        [SerializeField] private int navigationExpectedSlot;
        [SerializeField] private RenderTexture playerPreviewTexture;

        public static void RunBatch()
        {
            try
            {
                Require(Application.isBatchMode, "Play Mode smoke is batch-only; use an isolated project mirror.");
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Start the smoke session in Edit Mode.");
                Require(!Resources.FindObjectsOfTypeAll<CheckpointRefitPlayModeSmoke>().Any(session => session.running),
                    "A refit smoke session is already running.");
                for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                    Require(!UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty,
                        "Refusing to replace an unsaved scene.");
                CheckpointRefitValidation.Run();
                EditorSceneManager.OpenScene(CheckpointRefitValidation.ScenePath, OpenSceneMode.Single);
                var session = CreateInstance<CheckpointRefitPlayModeSmoke>();
                session.hideFlags = HideFlags.HideAndDontSave;
                session.outputDirectory = OutputDirectory();
                Directory.CreateDirectory(session.outputDirectory);
                session.report = new Report { startedUtc = DateTime.UtcNow.ToString("O") };
                foreach (string path in SourcePaths())
                    if (File.Exists(path)) session.report.sources.Add(new SourceHash { path = path, hash = Hash(path) });
                session.running = true;
                session.phase = Phase.WaitingForPlay;
                session.deadline = EditorApplication.timeSinceStartup + 150d;
                session.Subscribe();
                session.SaveReport();
                Debug.Log("Checkpoint Refit live input smoke starting: " + session.outputDirectory);
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
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            InputSystem.onBeforeUpdate -= QueueHeldKeyboard;
            InputSystem.onBeforeUpdate += QueueHeldKeyboard;
        }

        private void Tick()
        {
            if (!running) return;
            try
            {
                if (phase == Phase.Finishing)
                {
                    if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode) ExitBatch();
                    else if (EditorApplication.timeSinceStartup > deadline) ExitBatch();
                    return;
                }
                Require(EditorApplication.timeSinceStartup < deadline, "Timed out in phase " + phase + ".");
                EditorApplication.QueuePlayerLoopUpdate();
                if (!EditorApplication.isPlaying)
                {
                    Require(phase == Phase.WaitingForPlay || EditorApplication.isPlayingOrWillChangePlaymode,
                        "Play Mode ended before smoke checks completed.");
                    return;
                }
                if (lastFrame == Time.frameCount) return;
                lastFrame = Time.frameCount;
                switch (phase)
                {
                    case Phase.WaitingForPlay:
                        InstallInputSettings();
                        terminal = Object.FindFirstObjectByType<CheckpointRefitTerminal>();
                        player = Object.FindFirstObjectByType<RobotMover>();
                        if (terminal == null || player == null) return;
                        terminal.Bind(player);
                        gamepadId = InputSystem.AddDevice<Gamepad>("Checkpoint Refit smoke gamepad").deviceId;
                        keyboardId = InputSystem.AddDevice<Keyboard>("Checkpoint Refit smoke keyboard").deviceId;
                        Neutral(); Begin(Phase.Warmup); break;
                    case Phase.Warmup:
                        if (!Settled(8)) break;
                        Require(terminal.Player == player, "Terminal failed to bind the original player.");
                        player.transform.position = terminal.transform.position + Vector3.right * 100f;
                        SendPad(new GamepadState().WithButton(GamepadButton.South));
                        Begin(Phase.Outside); break;
                    case Phase.Outside:
                        if (!Settled()) break;
                        Record(!terminal.IsNearby && !terminal.IsOpen, "Gamepad A outside interaction range cannot open maintenance.");
                        Neutral(); player.transform.position = terminal.transform.position + Vector3.up * .5f;
                        Begin(Phase.Nearby); break;
                    case Phase.Nearby:
                        if (!Settled(8)) break;
                        Require(terminal.IsNearby, "Nearby terminal did not become available.");
                        Snapshot();
                        SendPad(new GamepadState().WithButton(GamepadButton.South));
                        Begin(Phase.PadOpen); break;
                    case Phase.PadOpen:
                        if (!Settled()) break;
                        CheckOpen(); selectedBeforeHold = terminal.View.SelectedModuleIndex;
                        Record(!terminal.View.IsInspectionOpen, "The opening A press does not activate the inspection window.");
                        Begin(Phase.PadHeld); break;
                    case Phase.PadHeld:
                        if (!Settled(8)) break;
                        Record(terminal.IsOpen && terminal.View.SelectedModuleIndex == selectedBeforeHold
                            && !terminal.View.IsInspectionOpen, "Holding the opening A press cannot submit or change the locked selection.");
                        Neutral(); Begin(Phase.OpenReleased); break;
                    case Phase.OpenReleased:
                        if (!Settled()) break;
                        CheckpointRefitPreviewValidation.CheckSelection(terminal.View);
                        CheckpointRefitPreviewValidation.CheckSourceArtworkDependency(player);
                        report.checks.Add("Player snapshot uses the actual marker artwork, responds to source tint changes, and leaves the source unchanged.");
                        navigationExpectedSlot = (terminal.View.SelectedSlotIndex + 1) % 5;
                        SendPad(new GamepadState { leftStick = Vector2.down });
                        Begin(Phase.PadDown); break;
                    case Phase.PadDown:
                        Neutral(); Begin(Phase.PadDownReleased); break;
                    case Phase.PadDownReleased:
                        if (!Settled()) break;
                        CheckNavigation("Left stick down");
                        navigationExpectedSlot = (terminal.View.SelectedSlotIndex + 4) % 5;
                        SendPad(new GamepadState { leftStick = Vector2.up });
                        Begin(Phase.PadUp); break;
                    case Phase.PadUp:
                        Neutral(); Begin(Phase.PadUpReleased); break;
                    case Phase.PadUpReleased:
                        if (!Settled()) break;
                        CheckNavigation("Left stick up");
                        navigationExpectedSlot = (terminal.View.SelectedSlotIndex + 1) % 5;
                        SendKeys(Key.DownArrow); Begin(Phase.KeyDown); break;
                    case Phase.KeyDown:
                        Neutral(); Begin(Phase.KeyDownReleased); break;
                    case Phase.KeyDownReleased:
                        if (!Settled()) break;
                        CheckNavigation("Keyboard down arrow");
                        navigationExpectedSlot = (terminal.View.SelectedSlotIndex + 4) % 5;
                        SendKeys(Key.UpArrow); Begin(Phase.KeyUp); break;
                    case Phase.KeyUp:
                        Neutral(); Begin(Phase.KeyUpReleased); break;
                    case Phase.KeyUpReleased:
                        if (!Settled()) break;
                        CheckNavigation("Keyboard up arrow");
                        navigationExpectedSlot = terminal.View.SelectedSlotIndex;
                        terminal.View.Document.rootVisualElement.Q<Button>("refit-slot-" + ((navigationExpectedSlot + 1) % 5)).Focus();
                        Begin(Phase.FocusMoved); break;
                    case Phase.FocusMoved:
                        if (!Settled()) break;
                        CheckNavigation("Focus on an adjacent unselected body slot");
                        Button selected = terminal.View.Document.rootVisualElement.Q<Button>("refit-slot-" + navigationExpectedSlot);
                        selected.Focus();
                        using (NavigationMoveEvent move = NavigationMoveEvent.GetPooled(Vector2.down)) selected.SendEvent(move);
                        using (NavigationMoveEvent move = NavigationMoveEvent.GetPooled(Vector2.up))
                            terminal.View.Document.rootVisualElement.SendEvent(move);
                        Begin(Phase.FocusRestored); break;
                    case Phase.FocusRestored:
                        if (!Settled()) break;
                        CheckNavigation("Native directional focus navigation");
                        Require(terminal.View.Document.rootVisualElement.focusController.focusedElement
                            == terminal.View.Document.rootVisualElement.Q<Button>("refit-slot-" + navigationExpectedSlot),
                            "Native navigation moved focus a second time after the Demo selected a body slot.");
                        int slotBefore = terminal.View.SelectedSlotIndex;
                        int moduleBefore = terminal.View.SelectedModuleIndex;
                        terminal.View.NavigateSlot(1);
                        terminal.View.NavigateModule(1);
                        Record(terminal.View.SelectedSlotIndex != slotBefore,
                            "Body-slot navigation updates the selected slot without equipping an item.");
                        Record(terminal.View.SelectedModuleIndex != moduleBefore,
                            "Module navigation changes the selected unknown module without equipping an item.");
                        CheckLockedUI();
                        terminal.View.OpenInspection(); Begin(Phase.ModalOpen); break;
                    case Phase.ModalOpen:
                        if (!Settled()) break;
                        Record(terminal.View.IsInspectionOpen, "The selected unknown module has an inspection presentation.");
                        SendPad(new GamepadState().WithButton(GamepadButton.East)); Begin(Phase.ModalBack); break;
                    case Phase.ModalBack:
                        if (!Settled()) break;
                        Record(terminal.IsOpen && !terminal.View.IsInspectionOpen,
                            "Gamepad B closes inspection before closing the maintenance screen.");
                        Neutral(); Begin(Phase.BlockedInputs); break;
                    case Phase.BlockedInputs:
                        if (!Settled()) break;
                        SendPad(new GamepadState { leftStick = Vector2.up, rightTrigger = 1f }
                            .WithButton(GamepadButton.South).WithButton(GamepadButton.RightShoulder)
                            .WithButton(GamepadButton.DpadRight));
                        SendKeys(Key.W, Key.N, Key.P, Key.Enter);
                        Begin(Phase.PrepareCapture); break;
                    case Phase.PrepareCapture:
                        if (!Settled(10)) break;
                        Record(Vector3.Distance(player.transform.position, playerPosition) < .0001f
                            && Quaternion.Angle(player.transform.rotation, playerRotation) < .001f,
                            "Movement input cannot move or rotate the player while maintenance owns input.");
                        PhotoModeController photo = player.GetComponent<PhotoModeController>();
                        RobotArmController arms = player.GetComponent<RobotArmController>();
                        Record(photo == null || !photo.IsActive, "Photo input cannot enter photo mode while maintenance is open.");
                        Record(arms == null || !arms.IsArmModeActive, "Grab input cannot enter mechanical-arm mode while maintenance is open.");
                        CheckInventoryClosed(); CheckLockedUI(); Neutral();
                        terminal.View.NavigateSlot(2 - terminal.View.SelectedSlotIndex);
                        terminal.View.NavigateModule(-terminal.View.SelectedModuleIndex);
                        BeginCapture(); Begin(Phase.Capture); break;
                    case Phase.Capture:
                        if (!Settled(10)) break;
                        EndCapture(); Begin(Phase.CloseSetup); break;
                    case Phase.CloseSetup:
                        if (!Settled()) break;
                        SendPad(new GamepadState().WithButton(GamepadButton.East)); Begin(Phase.PadClose); break;
                    case Phase.PadClose:
                        if (!Settled()) break;
                        Record(!terminal.IsOpen, "A second Gamepad B closes the maintenance UI.");
                        Record(!player.enabled, "The closing B press keeps gameplay suspended until the held input is released.");
                        Neutral(); Begin(Phase.PadReleased); break;
                    case Phase.PadReleased:
                        if (!Settled(8)) break;
                        CheckRestored("Gamepad close");
                        SendKeys(Key.Enter); Begin(Phase.KeyboardOpen); break;
                    case Phase.KeyboardOpen:
                        if (!Settled()) break;
                        CheckOpen(); Record(terminal.IsOpen, "Keyboard Enter opens the nearby maintenance UI.");
                        Neutral(); Begin(Phase.KeyboardReleased); break;
                    case Phase.KeyboardReleased:
                        if (!Settled()) break;
                        terminal.View.OpenInspection(); SendKeys(Key.Escape); Begin(Phase.EscapeModal); break;
                    case Phase.EscapeModal:
                        if (!Settled()) break;
                        Record(terminal.IsOpen && !terminal.View.IsInspectionOpen,
                            "Escape closes inspection while retaining maintenance.");
                        Neutral(); Begin(Phase.EscapeRelease); break;
                    case Phase.EscapeRelease:
                        if (!Settled()) break;
                        SendKeys(Key.Escape); Begin(Phase.EscapeClose); break;
                    case Phase.EscapeClose:
                        if (!Settled()) break;
                        Record(!terminal.IsOpen, "Escape closes maintenance after inspection is closed.");
                        Neutral(); Begin(Phase.KeyboardClosed); break;
                    case Phase.KeyboardClosed:
                        if (!Settled(8)) break;
                        CheckRestored("Keyboard close");
                        Require(terminal.TryOpen(), "Cleanup fixture could not reopen maintenance.");
                        terminal.enabled = false; Begin(Phase.DisableCleanup); break;
                    case Phase.DisableCleanup:
                        if (!Settled()) break;
                        Record(!terminal.IsOpen, "Disabling the terminal closes maintenance.");
                        CheckRestored("Terminal disable");
                        terminal.enabled = true; Neutral(); Begin(Phase.DisableReleased); break;
                    case Phase.DisableReleased:
                        if (!Settled(8)) break;
                        Require(terminal.TryOpen(), "Destroy fixture could not reopen maintenance.");
                        CheckOpen();
                        Object.Destroy(terminal.gameObject); Begin(Phase.DestroyCleanup); break;
                    case Phase.DestroyCleanup:
                        if (!Settled(5)) break;
                        CheckRestored("Terminal destruction");
                        Finish(null); break;
                }
            }
            catch (Exception exception) { Finish(exception); }
        }

        private void Snapshot()
        {
            // Use a non-default value to catch implementations that reset to one.
            Time.timeScale = .65f;
            originalTimeScale = Time.timeScale;
            originalCursorVisible = Cursor.visible;
            originalCursorLock = Cursor.lockState;
            playerPosition = player.transform.position;
            playerRotation = player.transform.rotation;
            guarded.Clear(); enabledBefore.Clear();
            foreach (Behaviour behaviour in player.GetComponentsInChildren<Behaviour>(true))
            {
                if (behaviour == null || !IsGuarded(behaviour)) continue;
                guarded.Add(behaviour); enabledBefore.Add(behaviour.enabled);
            }
            foreach (Behaviour behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (behaviour.GetType().Name == "MainUI" && !guarded.Contains(behaviour))
                { guarded.Add(behaviour); enabledBefore.Add(behaviour.enabled); }
        }

        private static bool IsGuarded(Behaviour behaviour)
        {
            string name = behaviour.GetType().Name;
            return name == "RobotMover" || name == "RobotArmController" || name == "PhotoModeController"
                || name == "BioScanController" || name == "RobotBalanceController"
                || name == "RobotTumbleController" || name == "RobotHeightMotionDetector"
                || name == "RobotSelfRightingController" || name == "MainUI";
        }

        private void CheckOpen()
        {
            bool canAcquire = RefitGameplayLease.CanAcquire(player, out string acquireReason);
            Keyboard currentKeyboard = Keyboard.current;
            Keyboard virtualKeyboard = InputSystem.GetDeviceById(keyboardId) as Keyboard;
            object ReadTerminalField(string name) => terminal == null ? null : typeof(CheckpointRefitTerminal)
                .GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.GetValue(terminal);
            string diagnostic = " [phase=" + phase
                + ", terminalOpen=" + (terminal != null && terminal.IsOpen)
                + ", nearby=" + (terminal != null && terminal.IsNearby)
                + ", viewVisible=" + (terminal != null && terminal.View != null && terminal.View.IsVisible)
                + ", viewActive=" + (terminal != null && terminal.View != null && terminal.View.isActiveAndEnabled)
                + ", playerEnabled=" + (player != null && player.enabled)
                + ", timeScale=" + Time.timeScale
                + ", canAcquire=" + canAcquire + ", acquireReason=" + acquireReason
                + ", keyboardCurrent=" + (currentKeyboard == null ? "null" : currentKeyboard.name + "#" + currentKeyboard.deviceId)
                + ", currentEnter=" + (currentKeyboard != null && currentKeyboard.enterKey.isPressed)
                + ", currentEnterPressedThisFrame=" + (currentKeyboard != null && currentKeyboard.enterKey.wasPressedThisFrame)
                + ", virtualKeyboard=" + (virtualKeyboard == null ? "null" : virtualKeyboard.name + "#" + virtualKeyboard.deviceId)
                + ", virtualEnter=" + (virtualKeyboard != null && virtualKeyboard.enterKey.isPressed)
                + ", inputArmed=" + ReadTerminalField("inputArmed")
                + ", releasing=" + ReadTerminalField("releasing")
                + ", previousInteract=" + ReadTerminalField("previousInteract") + "]";
            bool opened = terminal.IsOpen && terminal.View != null && terminal.View.IsVisible && terminal.View.isActiveAndEnabled;
            if (!opened)
            {
                report.notes.Add("CheckOpen diagnostic" + diagnostic);
                Debug.LogWarning("Checkpoint Refit CheckOpen diagnostic" + diagnostic);
            }
            Record(opened, "Actual input opens the visible, dedicated UIDocument screen." + (opened ? string.Empty : diagnostic));
            Record(Mathf.Abs(Time.timeScale) < .0001f, "Maintenance pauses the world."
                + (Mathf.Abs(Time.timeScale) < .0001f ? string.Empty : diagnostic));
            Record(!player.enabled, "Movement controller is suspended while maintenance is open."
                + (!player.enabled ? string.Empty : diagnostic));
            CheckLockedUI();
            playerPreviewTexture = CheckpointRefitPreviewValidation.CheckActualPlayerImage(terminal.View, player);
            report.checks.Add("Unit preview displays a nonempty render target from the real gameplay player's sprite artwork.");
        }

        private void CheckNavigation(string input)
        {
            Require(terminal.View.SelectedSlotIndex == navigationExpectedSlot,
                input + " changed the selected body slot more than once or in the wrong direction.");
            CheckpointRefitPreviewValidation.CheckSelection(terminal.View);
            report.checks.Add(input + " keeps selection, yellow text, left bar, and background on the same single body slot.");
        }

        private void CheckLockedUI()
        {
            UIDocument document = terminal.View.GetComponent<UIDocument>();
            Require(document != null && document.rootVisualElement.panel != null, "UI document did not attach to a live panel.");
            string visibleText = string.Join(" ", document.rootVisualElement.Query<TextElement>().ToList()
                .Where(element => element.resolvedStyle.display != DisplayStyle.None).Select(element => element.text));
            Record(visibleText.IndexOf("UNKNOWN", StringComparison.OrdinalIgnoreCase) >= 0
                && visibleText.IndexOf("LOCKED", StringComparison.OrdinalIgnoreCase) >= 0,
                "Equipment remains UNKNOWN / LOCKED after navigation and confirmation input.");
            for (int i = 0; i < 3; i++)
            {
                Button module = document.rootVisualElement.Q<Button>("refit-module-" + i);
                Require(module != null, "Missing unknown module card " + i + ".");
                string text = string.Join(" ", module.Query<TextElement>().ToList().Select(element => element.text));
                Require(text.Contains("UNKNOWN") && text.Contains("LOCKED"),
                    "Every module card must remain unidentified and locked.");
            }
            Button equip = document.rootVisualElement.Q<Button>("refit-equip");
            Require(equip != null && !equip.enabledInHierarchy, "The locked equipment action must remain disabled.");
            Require(!document.rootVisualElement.Query<Button>().ToList().Any(button => button.enabledInHierarchy
                && string.Equals((button.text ?? string.Empty).Trim(), "Equip", StringComparison.OrdinalIgnoreCase)),
                "The UI must never offer an enabled Equip action in this Demo.");
        }

        private void CheckInventoryClosed()
        {
            bool checkedInventory = false;
            foreach (Behaviour behaviour in guarded)
            {
                if (behaviour == null || behaviour.GetType().Name != "MainUI") continue;
                var property = behaviour.GetType().GetProperty("IsInventoryOpen");
                if (property != null && property.PropertyType == typeof(bool))
                {
                    Require(!(bool)property.GetValue(behaviour), "Inventory opened while maintenance owned input.");
                    checkedInventory = true;
                }
            }
            if (checkedInventory) report.checks.Add("Inventory toggle input does not open the inventory during maintenance.");
            else report.notes.Add("This independent scene has no MainUI HUD; inventory integration needs a map-scene check.");
        }

        private void CheckRestored(string operation)
        {
            Require(Mathf.Abs(Time.timeScale - originalTimeScale) < .0001f, operation + " did not restore the prior time scale.");
            Require(Cursor.visible == originalCursorVisible && Cursor.lockState == originalCursorLock,
                operation + " did not restore cursor state.");
            for (int i = 0; i < guarded.Count; i++)
                Require(guarded[i] == null || guarded[i].enabled == enabledBefore[i],
                    operation + " changed the prior enabled state of " + guarded[i]?.GetType().Name + ".");
            CheckpointRefitPreviewValidation.CheckPreviewReleased(terminal != null ? terminal.View : null, playerPreviewTexture);
            report.checks.Add(operation + " restores the previous time scale, cursor, and enabled/disabled gameplay states.");
            report.checks.Add(operation + " releases the player preview image and render target.");
        }

        private void BeginCapture()
        {
            capturePanel = terminal.View.GetComponent<UIDocument>().panelSettings;
            priorTarget = capturePanel.targetTexture;
            captureTexture = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGB32)
            { name = "Refit smoke capture", hideFlags = HideFlags.HideAndDontSave };
            captureTexture.Create();
            capturePanel.targetTexture = captureTexture;
        }

        private void EndCapture()
        {
            RenderTexture priorActive = RenderTexture.active;
            Texture2D pixels = null;
            try
            {
                RenderTexture.active = captureTexture;
                pixels = new Texture2D(captureTexture.width, captureTexture.height, TextureFormat.RGBA32, false);
                pixels.ReadPixels(new Rect(0, 0, pixels.width, pixels.height), 0, 0);
                pixels.Apply();
                Color32[] samples = pixels.GetPixels32();
                Require(samples.Count(pixel => pixel.a > 100 && (pixel.r > 120 || pixel.g > 120 || pixel.b > 120)) > 500,
                    "The actual UI Toolkit render contains no readable foreground content.");
                string path = Path.Combine(outputDirectory, "checkpoint-refit-ui.png");
                File.WriteAllBytes(path, pixels.EncodeToPNG());
                report.screenshots.Add(path);
                report.checks.Add("Actual UIDocument renders readable UI content to a 1600 x 1000 capture.");
            }
            finally
            {
                RenderTexture.active = priorActive;
                if (pixels != null) Object.DestroyImmediate(pixels);
                RestoreCapture();
            }
        }

        private void RestoreCapture()
        {
            if (capturePanel != null) capturePanel.targetTexture = priorTarget;
            if (captureTexture != null) { captureTexture.Release(); Object.DestroyImmediate(captureTexture); }
            captureTexture = null; capturePanel = null; priorTarget = null;
        }

        private void Neutral() { SendPad(default(GamepadState)); SendKeys(); }
        private void SendPad(GamepadState state)
        {
            Gamepad pad = InputSystem.GetDeviceById(gamepadId) as Gamepad;
            Require(pad != null && pad.added, "Smoke gamepad was lost.");
            pad.MakeCurrent(); InputSystem.QueueStateEvent(pad, state);
        }
        private void SendKeys(params Key[] keys)
        {
            Keyboard keyboard = InputSystem.GetDeviceById(keyboardId) as Keyboard;
            Require(keyboard != null && keyboard.added, "Smoke keyboard was lost.");
            heldKeys = keys == null ? Array.Empty<Key>() : (Key[])keys.Clone();
        }
        private void QueueHeldKeyboard()
        {
            if (!running || phase == Phase.Finishing || InputState.currentUpdateType != InputUpdateType.Dynamic) return;
            Keyboard keyboard = InputSystem.GetDeviceById(keyboardId) as Keyboard;
            if (keyboard == null || !keyboard.added) return;
            // Deliver to the runtime Dynamic buffer, not the Editor's separately maintained keyboard buffer.
            keyboard.MakeCurrent();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(heldKeys ?? Array.Empty<Key>()));
        }
        private void InstallInputSettings()
        {
            if (inputSettingsOwned) return;
            previousInputSettings = InputSystem.settings;
            previousRunInBackground = Application.runInBackground;
            smokeInputSettings = Object.Instantiate(previousInputSettings);
            smokeInputSettings.name = "Refit smoke input settings (runtime only)";
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
        {
            phase = next; phaseFrame = Time.frameCount;
            phaseStarted = EditorApplication.timeSinceStartup; SaveReport();
        }
        private bool Settled(int frames = 3) => Time.frameCount - phaseFrame >= frames
            && EditorApplication.timeSinceStartup - phaseStarted >= .25d;
        private void Record(bool condition, string message) { Require(condition, message); report.checks.Add(message); }
        private static void Require(bool condition, string message) => CheckpointRefitValidation.Require(condition, message);
        private void OnLog(string message, string trace, LogType type)
        {
            if (!running || phase == Phase.Finishing) return;
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            { report.errors.Add(type + ": " + message + "\n" + trace); SaveReport(); }
        }

        private void Finish(Exception failure)
        {
            if (phase == Phase.Finishing) return;
            if (failure != null) report.errors.Add(failure.ToString());
            InputSystem.onBeforeUpdate -= QueueHeldKeyboard;
            RestoreCapture();
            if (terminal != null) { terminal.Close(); terminal.enabled = false; }
            foreach (int id in new[] { gamepadId, keyboardId })
            {
                InputDevice device = InputSystem.GetDeviceById(id);
                if (device != null && device.added) InputSystem.RemoveDevice(device);
            }
            RestoreInputSettings();
            Time.timeScale = originalTimeScale > 0f ? originalTimeScale : 1f;
            phase = Phase.Finishing;
            deadline = EditorApplication.timeSinceStartup + 20d;
            SaveReport(); EditorApplication.isPlaying = false;
        }

        private void ExitBatch()
        {
            RestoreInputSettings();
            foreach (SourceHash source in report.sources)
                if (!File.Exists(source.path) || source.hash != Hash(source.path))
                    report.errors.Add("Existing source asset changed during smoke: " + source.path);
            report.finishedUtc = DateTime.UtcNow.ToString("O");
            report.passed = report.errors.Count == 0; SaveReport();
            running = false; EditorApplication.update -= Tick; Application.logMessageReceived -= OnLog;
            InputSystem.onBeforeUpdate -= QueueHeldKeyboard;
            Debug.Log("Checkpoint Refit smoke " + (report.passed ? "PASS" : "FAIL") + ": "
                + report.checks.Count + " checks; " + Path.Combine(outputDirectory, "play-mode-smoke.json"));
            int code = report.passed ? 0 : 1;
            Object.DestroyImmediate(this); EditorApplication.Exit(code);
        }

        private void SaveReport()
        {
            if (report != null && !string.IsNullOrEmpty(outputDirectory))
                File.WriteAllText(Path.Combine(outputDirectory, "play-mode-smoke.json"), JsonUtility.ToJson(report, true));
        }
        private static IEnumerable<string> SourcePaths()
        {
            yield return "Assets/Prefabs/Resources/Robot/RobotMarker.prefab";
            yield return "Assets/Prefabs/Resources/Camera/RobotCamera.prefab";
            yield return "ProjectSettings/EditorBuildSettings.asset";
            yield return "ProjectSettings/InputManager.asset";
            foreach (string scene in AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(scene);
                if (!path.StartsWith(CheckpointRefitValidation.RootPath + "/", StringComparison.Ordinal)) yield return path;
            }
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
            return Path.GetFullPath(Path.Combine("output", "validation", "checkpoint-refit"));
        }
    }
}
