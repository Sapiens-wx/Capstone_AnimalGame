using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AnimalGame.RobotMap;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace Capstone.Audio.Editor
{
    /// <summary>
    /// Exercises the real gameplay controllers with a virtual gamepad. Scenes are
    /// loaded only after entering Play Mode; no scene or prefab is saved.
    /// Batch entry: -executeMethod Capstone.Audio.Editor.Milestone1AudioValidation.RunPlayMode
    /// (omit -quit: this asynchronous runner exits the batch editor itself).
    /// </summary>
    [InitializeOnLoad]
    public static class Milestone1AudioValidation
    {
        private const string RunningKey = "Capstone.FMOD.Validation.Running";
        private const string PassKey = "Capstone.FMOD.Validation.Pass";
        private const string ReportPath = "Library/FMODValidation/play-mode.json";
        private const string ScanCharge = "event:/Robot/Scan/Charge";
        private const string ScanPulse = "event:/Robot/Scan/Pulse";
        private const string CameraOpen = "event:/Robot/Camera/Open";
        private const string CameraClose = "event:/Robot/Camera/Close";
        private const string CameraMove = "event:/Robot/Camera/Move";
        private const string CameraFocus = "event:/Robot/Camera/Focus";
        private const string CameraShutter = "event:/Robot/Camera/Shutter";
        private static readonly string[] ScenePaths =
        {
            "Assets/Scenes/HeightMapPlayerScene.unity",
            "Assets/Scenes/AnimalPlacementTestScene.unity"
        };
        private static readonly string[] EventPaths =
        {
            ScanCharge, ScanPulse, CameraOpen, CameraClose,
            CameraMove, CameraFocus, CameraShutter
        };

        [Serializable]
        public sealed class ValidationReport
        {
            public string startedUtc;
            public string finishedUtc;
            public bool passed;
            public List<string> checks = new List<string>();
            public List<string> errors = new List<string>();
            public List<string> notes = new List<string>();
        }

        private static ValidationReport report;
        private static readonly Dictionary<string, int> Starts = new Dictionary<string, int>();
        private static readonly Stack<IEnumerator> Steps = new Stack<IEnumerator>();
        private static Gamepad gamepad;
        private static ScanChargeUI scan;
        private static PhotoModeController photo;
        private static int lastFrame = -1;
        private static double deadline;
        private static bool finishing;
        private static string context;

        static Milestone1AudioValidation()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += Tick;
            if (SessionState.GetBool(RunningKey, false))
                LoadReport();
        }

        [MenuItem("FMOD/Milestone 1/Validate Gameplay Audio")]
        public static void RunPlayMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode
                || SessionState.GetBool(RunningKey, false))
                throw new InvalidOperationException("Start audio validation from Edit Mode with no validation already running.");

            report = new ValidationReport { startedUtc = DateTime.UtcNow.ToString("O") };
            report.notes.Add("Virtual Gamepad drives existing gameplay input. Scene loading and tipping fault injection happen only in Play Mode; no scene/prefab is saved.");
            report.notes.Add("A second complete Play Mode entry checks bootstrap and subscription reset under the project's current domain-reload settings.");
            SessionState.SetBool(RunningKey, true);
            SessionState.SetInt(PassKey, 0);
            SaveReport();
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(RunningKey, false))
                return;

            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                LoadReport();
                finishing = false;
                lastFrame = -1;
                deadline = EditorApplication.timeSinceStartup + 240;
                Starts.Clear();
                Steps.Clear();
                gamepad = InputSystem.AddDevice<Gamepad>("FMOD Milestone 1 Validation");
                gamepad.MakeCurrent();
                Milestone1AudioDiagnostics.Started += OnStarted;
                Application.logMessageReceived += OnLog;
                Steps.Push(SessionState.GetInt(PassKey, 0) == 0 ? MainSuite() : ReentrySuite());
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                Milestone1AudioDiagnostics.Started -= OnStarted;
                Application.logMessageReceived -= OnLog;
                if (gamepad != null && gamepad.added)
                    InputSystem.RemoveDevice(gamepad);
                gamepad = null;
                Steps.Clear();
                if (!finishing)
                {
                    report.errors.Add("Validation was interrupted before its assertions finished.");
                    SaveReport();
                }
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                LoadReport();
                if (SessionState.GetInt(PassKey, 0) == 0 && report.errors.Count == 0)
                {
                    SessionState.SetInt(PassKey, 1);
                    EditorApplication.delayCall += () => EditorApplication.isPlaying = true;
                }
                else
                {
                    report.finishedUtc = DateTime.UtcNow.ToString("O");
                    report.passed = report.errors.Count == 0;
                    SaveReport();
                    SessionState.SetBool(RunningKey, false);
                    Debug.Log($"FMOD M1 validation {(report.passed ? "PASSED" : "FAILED")}: {report.checks.Count} checks; {Path.GetFullPath(ReportPath)}");
                    if (Application.isBatchMode)
                        EditorApplication.delayCall += () => EditorApplication.Exit(report.passed ? 0 : 1);
                }
            }
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(RunningKey, false) || !EditorApplication.isPlaying || finishing || Steps.Count == 0)
                return;
            EditorApplication.QueuePlayerLoopUpdate();
            if (EditorApplication.timeSinceStartup > deadline)
            {
                Fail(new TimeoutException("Audio validation exceeded the 240-second Play Mode deadline."));
                return;
            }
            if (Steps.Count == 0 || lastFrame == Time.frameCount)
                return;
            lastFrame = Time.frameCount;
            try
            {
                for (int count = 0; count < 100 && Steps.Count > 0; count++)
                {
                    IEnumerator current = Steps.Peek();
                    if (!current.MoveNext())
                    {
                        Steps.Pop();
                        continue;
                    }
                    if (current.Current is IEnumerator nested)
                    {
                        Steps.Push(nested);
                        continue;
                    }
                    return;
                }
                if (Steps.Count == 0)
                    FinishPlay();
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        private static IEnumerator MainSuite()
        {
            foreach (string path in ScenePaths)
            {
                yield return LoadScene(path);
                ValidateEventsAndBanks();
                yield return ScanSuite();
                yield return CameraSuite();
                yield return DisableAndReloadSuite(path);
                yield return TippingSuite();
            }
        }

        private static IEnumerator ReentrySuite()
        {
            yield return LoadScene(ScenePaths[0]);
            Check(UnityEngine.Object.FindObjectsByType<Milestone1AudioRuntime>(FindObjectsSortMode.None).Length == 1,
                "second Play Mode entry has exactly one audio runtime");
            int pulse = Count(ScanPulse);
            yield return TerrainGesture();
            Check(Count(ScanPulse) == pulse + 1, "second Play Mode entry emits one pulse per gesture");
            yield return SilenceRuntime();
        }

        private static IEnumerator LoadScene(string path)
        {
            Queue(default);
            int previousHandle = SceneManager.GetActiveScene().handle;
            scan = null;
            photo = null;
            Scene loaded = EditorSceneManager.LoadSceneInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
            context = Path.GetFileNameWithoutExtension(path);
            // The API may return before the previous scene's objects have been
            // destroyed. Never accept controllers from the outgoing scene.
            yield return Frames(2);
            yield return Until(() => loaded.IsValid() && loaded.isLoaded
                                     && loaded.handle != previousHandle
                                     && SceneManager.GetActiveScene().handle == loaded.handle,
                "replacement scene fully loaded and active", 15);
            yield return Until(() => FindInScene<ScanChargeUI>(loaded) != null
                                     && FindInScene<PhotoModeController>(loaded) != null,
                "gameplay controllers spawned", 15);
            yield return Seconds(1);
            scan = FindInScene<ScanChargeUI>(loaded);
            photo = FindInScene<PhotoModeController>(loaded);
            Check(scan != null && photo != null && scan.isActiveAndEnabled && photo.isActiveAndEnabled,
                "scan and photo controllers active in replacement scene");
            Check(UnityEngine.Object.FindObjectsByType<Milestone1AudioRuntime>(FindObjectsSortMode.None).Length == 1,
                "exactly one audio runtime after scene load");
        }

        private static T FindInScene<T>(Scene scene) where T : Behaviour
        {
            return UnityEngine.Object.FindObjectsByType<T>(FindObjectsSortMode.None)
                .FirstOrDefault(candidate => candidate != null && candidate.isActiveAndEnabled
                                             && candidate.gameObject.scene.handle == scene.handle);
        }

        private static void ValidateEventsAndBanks()
        {
            foreach (string path in EventPaths)
            {
                FMOD.RESULT result = FMODUnity.RuntimeManager.StudioSystem.getEvent(path, out FMOD.Studio.EventDescription description);
                Check(result == FMOD.RESULT.OK && description.isValid(), path + " resolves");
                Check(description.is3D(out bool spatial) == FMOD.RESULT.OK && !spatial, path + " is 2D");
            }
            foreach (string path in new[] { "bank:/Master", "bank:/RobotTools" })
            {
                FMOD.RESULT result = FMODUnity.RuntimeManager.StudioSystem.getBank(path, out FMOD.Studio.Bank bank);
                Check(result == FMOD.RESULT.OK && bank.isValid(), path + " loaded");
            }
        }

        private static IEnumerator ScanSuite()
        {
            int charge = Count(ScanCharge);
            int pulse = Count(ScanPulse);
            Queue(new GamepadState().WithButton(GamepadButton.LeftShoulder));
            yield return Until(() => scan.IsCharging, "biological charge begins", 3);
            Check(Count(ScanCharge) == charge + 1, "one charge starts from real LB hold");
            Queue(default);
            yield return Seconds(0.2f);
            Check(!scan.IsCharging && Count(ScanPulse) == pulse, "early charge release cancels without scan pulse");
            Check(Instances(ScanCharge).Length == 0, "cancelled scan charge released after fade");

            yield return Seconds(0.4f);
            Queue(new GamepadState().WithButton(GamepadButton.LeftShoulder));
            yield return Until(() => scan.IsFullyCharged, "biological charge reaches full", 4);
            yield return Seconds(1.4f);
            FMOD.Studio.EventInstance[] held = Instances(ScanCharge);
            Check(held.Length == 1, "one charge voice continues while fully charged");
            held[0].getTimelinePosition(out int position);
            Check(position >= 770 && position <= 910, "held scan timeline remains around 790–880 ms loop (observed " + position + " ms)");
            yield return Seconds(0.4f);
            held[0].getPlaybackState(out FMOD.Studio.PLAYBACK_STATE playback);
            Check(playback == FMOD.Studio.PLAYBACK_STATE.PLAYING, "held scan remains playing beyond source duration");
            Queue(default);
            yield return Seconds(0.2f);
            Check(Count(ScanPulse) == pulse + 1, "fully charged release produces exactly one pulse");
            Check(Instances(ScanCharge).Length == 0, "successful scan release stops charge");

            yield return Seconds(0.6f);
            pulse = Count(ScanPulse);
            charge = Count(ScanCharge);
            yield return TerrainGesture();
            Check(Count(ScanPulse) == pulse + 1 && Count(ScanCharge) == charge,
                "terrain double tap uses common pulse without charge");

            Queue(new GamepadState().WithButton(GamepadButton.LeftShoulder));
            yield return Until(() => scan.IsCharging, "charge before component disable", 3);
            pulse = Count(ScanPulse);
            scan.enabled = false;
            Queue(default);
            yield return Seconds(0.2f);
            Check(Instances(ScanCharge).Length == 0 && Count(ScanPulse) == pulse,
                "scan disable stops charge without pulse");
            scan.enabled = true;
            yield return Seconds(0.3f);
        }

        private static IEnumerator TerrainGesture()
        {
            Queue(new GamepadState().WithButton(GamepadButton.LeftShoulder));
            yield return Seconds(0.05f);
            Queue(default);
            yield return Seconds(0.06f);
            Queue(new GamepadState().WithButton(GamepadButton.LeftShoulder));
            yield return Seconds(0.05f);
            Queue(default);
            yield return Seconds(0.18f);
        }

        private static IEnumerator CameraSuite()
        {
            int opens = Count(CameraOpen);
            Queue(new GamepadState().WithButton(GamepadButton.LeftShoulder));
            yield return Until(() => scan.IsCharging, "scan begins before camera interruption", 3);
            Queue(new GamepadState().WithButton(GamepadButton.LeftShoulder).WithButton(GamepadButton.West));
            yield return Frames(2);
            Queue(default);
            yield return Until(() => photo.IsActive && !photo.IsEntering, "camera entered through X", 4);
            Check(Count(CameraOpen) == opens + 1, "camera open plays once at mode entry");
            Check(Instances(ScanCharge).Length == 0, "camera transition interrupts active scan charge");

            int move = Count(CameraMove);
            Vector2 initialAim = photo.AimLocalPosition;
            Queue(new GamepadState { rightStick = new Vector2(0.5f, 0.5f) });
            yield return Seconds(0.18f);
            Check(Vector2.Distance(initialAim, photo.AimLocalPosition) > 0.001f && Count(CameraMove) == move + 1,
                "actual aim motion starts one movement loop");
            Queue(default);
            yield return Seconds(0.2f);
            Check(Instances(CameraMove).Length == 0, "neutral aim stops movement loop");
            Queue(new GamepadState { rightStick = Vector2.one.normalized });
            yield return Seconds(3);
            Vector2 boundedAim = photo.AimLocalPosition;
            yield return Seconds(0.25f);
            Check(Vector2.Distance(boundedAim, photo.AimLocalPosition) < 0.00001f && Instances(CameraMove).Length == 0,
                "held stick at aim boundary remains silent");
            Queue(default);
            yield return Frames(2);

            int focus = Count(CameraFocus);
            int shutter = Count(CameraShutter);
            Queue(new GamepadState().WithButton(GamepadButton.RightShoulder));
            yield return Until(() => photo.IsFocusing, "RB begins focus", 2);
            yield return Seconds(0.08f);
            Queue(default);
            yield return Seconds(0.2f);
            Check(!photo.IsFocusing && Count(CameraFocus) == focus + 1 && Count(CameraShutter) == shutter,
                "focus cancel produces no shutter");
            Check(Instances(CameraFocus).Length == 0, "cancelled focus voice stops");

            Queue(new GamepadState().WithButton(GamepadButton.RightShoulder));
            yield return Until(() => photo.IsFocusComplete, "focus completes", 3);
            yield return Seconds(0.2f);
            Check(Instances(CameraFocus).Length == 0 && Count(CameraShutter) == shutter,
                "full focus waits silently without premature shutter");
            Queue(default);
            yield return Frames(3);
            int captured = 0;
            Action onCaptured = () => captured++;
            photo.PhotoCaptured += onCaptured;
            Queue(new GamepadState().WithButton(GamepadButton.RightShoulder));
            yield return Seconds(0.4f);
            Queue(default);
            photo.PhotoCaptured -= onCaptured;
            Check(captured == 1 && Count(CameraShutter) == shutter + 1, "actual PhotoCaptured fires exactly one shutter");
            if (photo.IsReviewing)
            {
                Queue(new GamepadState().WithButton(GamepadButton.East));
                yield return Frames(3);
                Queue(default);
                yield return Seconds(0.2f);
            }

            int closes = Count(CameraClose);
            Queue(new GamepadState().WithButton(GamepadButton.West));
            yield return Until(() => photo.IsExiting, "camera exit begins", 2);
            yield return Frames(2);
            Check(Count(CameraClose) == closes + 1, "close sound begins during retract animation");
            Queue(default);
            yield return Until(() => !photo.IsActive, "camera exit completes", 3);
            Check(Count(CameraClose) == closes + 1, "exit completion does not duplicate close sound");

            for (int repeat = 0; repeat < 2; repeat++)
            {
                Queue(new GamepadState().WithButton(GamepadButton.West));
                yield return Frames(2);
                Queue(default);
                yield return Until(() => photo.IsActive && !photo.IsEntering, "repeat camera entry", 3);
                Queue(new GamepadState().WithButton(GamepadButton.West));
                yield return Frames(2);
                Queue(default);
                yield return Until(() => !photo.IsActive, "repeat camera exit", 3);
            }
            Check(Count(CameraOpen) == opens + 3 && Count(CameraClose) == closes + 3,
                "repeated camera toggles do not accumulate subscriptions");

            Queue(new GamepadState().WithButton(GamepadButton.West));
            yield return Frames(2);
            Queue(default);
            yield return Until(() => photo.IsActive && !photo.IsEntering, "camera before component disable", 3);
            Queue(new GamepadState().WithButton(GamepadButton.RightShoulder));
            yield return Until(() => photo.IsFocusing, "focus before component disable", 2);
            yield return Frames(2);
            photo.enabled = false;
            Queue(default);
            yield return Seconds(0.2f);
            Check(Instances(CameraFocus).Length == 0 && Instances(CameraMove).Length == 0,
                "photo component disable releases its continuous sounds");
            photo.enabled = true;
            yield return Seconds(0.3f);
        }

        private static IEnumerator DisableAndReloadSuite(string path)
        {
            Queue(new GamepadState().WithButton(GamepadButton.LeftShoulder));
            yield return Until(() => scan.IsFullyCharged, "charge before runtime disable", 3);
            Milestone1AudioRuntime runtime = UnityEngine.Object.FindFirstObjectByType<Milestone1AudioRuntime>();
            runtime.enabled = false;
            yield return Until(() => Milestone1AudioDiagnostics.ActiveInstanceCount == 0 && Instances(ScanCharge).Length == 0,
                "FMOD processes runtime-disable release commands", 2);
            Check(Milestone1AudioDiagnostics.ActiveInstanceCount == 0 && Instances(ScanCharge).Length == 0,
                "runtime disable stops and releases all instances");

            int startsWhileDisabled = EventPaths.Sum(Count);
            yield return LoadScene(path);
            Check(!runtime.enabled, "scene reload preserves disabled audio runtime");
            yield return TerrainGesture();
            Queue(new GamepadState().WithButton(GamepadButton.LeftShoulder));
            yield return Until(() => scan.IsFullyCharged, "gameplay scan remains functional while audio disabled", 3);
            Queue(default);
            yield return Seconds(0.4f);
            Queue(new GamepadState().WithButton(GamepadButton.West));
            yield return Frames(2);
            Queue(default);
            yield return Until(() => photo.IsActive && !photo.IsEntering, "gameplay photo enters while audio disabled", 3);
            Queue(new GamepadState().WithButton(GamepadButton.West));
            yield return Frames(2);
            Queue(default);
            yield return Until(() => !photo.IsActive, "gameplay photo exits while audio disabled", 3);
            Check(EventPaths.Sum(Count) == startsWhileDisabled
                  && Milestone1AudioDiagnostics.ActiveInstanceCount == 0
                  && EventPaths.All(eventPath => Instances(eventPath).Length == 0),
                "disabled runtime scene callbacks never reconnect or play audio");

            runtime.enabled = true;
            int pulse = Count(ScanPulse);
            yield return TerrainGesture();
            Check(Count(ScanPulse) == pulse + 1, "runtime re-enable immediately reconnects once to reloaded controllers");

            Queue(new GamepadState().WithButton(GamepadButton.LeftShoulder));
            yield return Until(() => scan.IsFullyCharged, "charge before scene reload", 3);
            yield return LoadScene(path);
            Check(Instances(ScanCharge).Length == 0 && Milestone1AudioDiagnostics.ActiveInstanceCount == 0,
                "scene reload clears all previous sound instances");
            pulse = Count(ScanPulse);
            yield return TerrainGesture();
            Check(Count(ScanPulse) == pulse + 1, "scene reload binds new controllers once");
        }

        private static IEnumerator TippingSuite()
        {
            Queue(new GamepadState().WithButton(GamepadButton.West));
            yield return Frames(2);
            Queue(default);
            yield return Until(() => photo.IsActive && !photo.IsEntering, "camera before tipping", 3);
            Queue(new GamepadState().WithButton(GamepadButton.RightShoulder));
            yield return Until(() => photo.IsFocusing, "focus before tipping", 2);
            RobotBalanceController balance = photo.GetComponent<RobotBalanceController>();
            Check(balance != null, "balance controller available for tipping fault injection");
            // Test-only transient state makes the existing tipping method publish
            // its real event. No gameplay source or serialized asset is changed.
            typeof(RobotBalanceController).GetField("<CurrentState>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(balance, new RobotBalanceState(Vector2.right * 2, Vector2.right * 2, Vector2.zero, 2, 1, RobotBalanceLevel.OutsideSupport));
            typeof(RobotBalanceController).GetMethod("TryTipOver", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(balance, null);
            Queue(default);
            yield return Seconds(0.2f);
            Check(balance.IsTippedOver && !photo.IsActive, "real tipping event exits photo mode");
            Check(Instances(CameraFocus).Length == 0 && Instances(CameraMove).Length == 0,
                "tipping stops camera continuous sounds");
            yield return SilenceRuntime();
        }

        private static IEnumerator SilenceRuntime()
        {
            Milestone1AudioRuntime runtime = UnityEngine.Object.FindFirstObjectByType<Milestone1AudioRuntime>();
            if (runtime != null)
                runtime.enabled = false;
            yield return Until(() => Milestone1AudioDiagnostics.ActiveInstanceCount == 0
                                     && EventPaths.All(path => Instances(path).Length == 0),
                "FMOD processes shutdown release commands", 2);
            Check(Milestone1AudioDiagnostics.ActiveInstanceCount == 0, "all tracked voices released on shutdown");
            foreach (string path in EventPaths)
                Check(Instances(path).Length == 0, path + " has no remaining FMOD instances");
            if (runtime != null)
                runtime.enabled = true;
        }

        private static FMOD.Studio.EventInstance[] Instances(string path)
        {
            FMOD.RESULT result = FMODUnity.RuntimeManager.StudioSystem.getEvent(path, out FMOD.Studio.EventDescription description);
            if (result != FMOD.RESULT.OK)
                throw new InvalidOperationException(path + ": " + result);
            result = description.getInstanceList(out FMOD.Studio.EventInstance[] instances);
            if (result != FMOD.RESULT.OK)
                throw new InvalidOperationException(path + " instance list: " + result);
            return instances ?? Array.Empty<FMOD.Studio.EventInstance>();
        }

        private static void Queue(GamepadState state) => InputSystem.QueueStateEvent(gamepad, state);
        private static int Count(string path) => Starts.TryGetValue(path, out int count) ? count : 0;
        private static void OnStarted(string path) => Starts[path] = Count(path) + 1;

        private static IEnumerator Frames(int count)
        {
            int until = Time.frameCount + count;
            while (Time.frameCount < until)
                yield return null;
        }

        private static IEnumerator Seconds(float duration)
        {
            double until = Time.realtimeSinceStartupAsDouble + duration;
            do { yield return null; } while (Time.realtimeSinceStartupAsDouble < until);
        }

        private static IEnumerator Until(Func<bool> condition, string label, double seconds)
        {
            double until = Time.realtimeSinceStartupAsDouble + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartupAsDouble > until)
                    throw new TimeoutException(context + ": " + label);
                yield return null;
            }
        }

        private static void Check(bool success, string label)
        {
            if (!success)
                throw new InvalidOperationException(context + ": " + label);
            report.checks.Add(context + ": " + label);
            SaveReport();
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                report.errors.Add(type + ": " + message + "\n" + stack);
                SaveReport();
            }
        }

        private static void Fail(Exception exception)
        {
            report.errors.Add(exception.ToString());
            FinishPlay();
        }

        private static void FinishPlay()
        {
            finishing = true;
            SaveReport();
            EditorApplication.isPlaying = false;
        }

        private static void SaveReport()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
        }

        private static void LoadReport()
        {
            report = File.Exists(ReportPath)
                ? JsonUtility.FromJson<ValidationReport>(File.ReadAllText(ReportPath))
                : new ValidationReport();
        }

        [MenuItem("FMOD/Milestone 1/Validate Desktop Build")]
        public static void BuildDesktop()
        {
            var buildReport = new ValidationReport { startedUtc = DateTime.UtcNow.ToString("O") };
            // Unity rejects player output inside its internal Library directory.
            // Builds is already excluded by this project's existing .gitignore.
            string directory = "Builds/FMODValidation";
            Directory.CreateDirectory(directory);
            Directory.CreateDirectory("Library/FMODValidation");
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("Build validation requires Edit Mode.");
                BuildReport result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = ScenePaths,
                    locationPathName = directory + "/Milestone1Audio.exe",
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development
                });
                if (result == null)
                    throw new InvalidOperationException("Unity returned no BuildReport. Check the editor log for build path, target, or compilation errors.");
                if (result.summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException("Desktop build result: " + result.summary.result + "; errors: " + result.summary.totalErrors);
                buildReport.checks.Add("Windows x64 development build succeeded with explicit existing scene paths.");
                foreach (string bank in new[] { "Master.bank", "Master.strings.bank", "RobotTools.bank" })
                {
                    string[] files = Directory.GetFiles(directory, bank, SearchOption.AllDirectories);
                    if (files.Length == 0 || !files.Any(file => new FileInfo(file).Length > 0))
                        throw new InvalidOperationException("Built player is missing bank " + bank);
                    buildReport.checks.Add(bank + " included in built player.");
                }
                string[] native = Directory.GetFiles(directory, "fmod*.dll", SearchOption.AllDirectories);
                if (!native.Any(file => Path.GetFileName(file).StartsWith("fmodstudio", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Built player is missing FMOD Studio native library.");
                // The official Windows integration combines Core and Studio in
                // fmodstudio[L].dll (PlatformWindows.cs); no fmod.dll is needed.
                buildReport.checks.Add("FMOD combined Core/Studio native DLL included.");
                buildReport.passed = true;
            }
            catch (Exception exception)
            {
                buildReport.errors.Add(exception.ToString());
            }
            buildReport.finishedUtc = DateTime.UtcNow.ToString("O");
            File.WriteAllText("Library/FMODValidation/build.json", JsonUtility.ToJson(buildReport, true));
            Debug.Log("FMOD M1 desktop build validation " + (buildReport.passed ? "PASSED" : "FAILED"));
            if (Application.isBatchMode)
                EditorApplication.Exit(buildReport.passed ? 0 : 1);
        }
    }
}
