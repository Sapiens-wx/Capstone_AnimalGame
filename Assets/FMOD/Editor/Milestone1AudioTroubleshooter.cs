using System;
using System.IO;
using System.Text;
using AnimalGame.RobotMap;
using FMODUnity;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Capstone.Audio.Editor
{
    public sealed class Milestone1AudioTroubleshooter : EditorWindow
    {
        private const string TestEvent = "event:/Robot/Scan/Pulse";
        private string report = "Enter Play Mode to inspect gameplay audio.";
        private string testStatus = "Not run";
        private Vector2 scroll;
        private double nextRefresh;
        private double testDeadline;
        private int peakChannels;
        private float peakDb = -80f;
        private bool measuredOutput;
        private bool measuringTest;
        private FMOD.Studio.EventInstance testInstance;

        [MenuItem("FMOD/Milestone 1/Troubleshoot No Sound")]
        public static void Open()
        {
            GetWindow<Milestone1AudioTroubleshooter>("M1 Audio Check");
        }

        private void OnEnable()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            StopTest();
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
                StopTest();
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                testStatus = "Not run";
                peakChannels = 0;
                peakDb = -80f;
                measuredOutput = false;
            }
            nextRefresh = 0;
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Enter Play Mode and use Scan / Camera. Play Test Scan checks the same FMOD output without requiring gameplay input. No mute or device settings are changed.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
                if (GUILayout.Button("Play Test Scan"))
                    PlayTest();
            if (GUILayout.Button("Copy Report"))
                EditorGUIUtility.systemCopyBuffer = report;
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.TextArea(report, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void Tick()
        {
            if (EditorApplication.timeSinceStartup < nextRefresh)
                return;
            nextRefresh = EditorApplication.timeSinceStartup + 0.1;
            if (measuringTest && EditorApplication.timeSinceStartup >= testDeadline)
            {
                measuringTest = false;
                string outputSummary = measuredOutput ? $"{peakDb:F1} dB" : "meter unavailable";
                testStatus += $"; peak real channels={peakChannels}, peak output={outputSummary}";
                StopTest();
            }
            report = CaptureReport();
            Repaint();
        }

        private void PlayTest()
        {
            if (!EditorApplication.isPlaying)
                return;
            StopTest();
            peakChannels = 0;
            peakDb = -80f;
            measuredOutput = false;
            try
            {
                testInstance = RuntimeManager.CreateInstance(TestEvent);
                FMOD.RESULT result = testInstance.start();
                testStatus = $"{TestEvent}: start={result}";
                measuringTest = result == FMOD.RESULT.OK;
                testDeadline = EditorApplication.timeSinceStartup + 2.0;
                if (!measuringTest)
                    StopTest();
            }
            catch (Exception exception)
            {
                testStatus = exception.ToString();
                StopTest();
            }
            nextRefresh = 0;
        }

        private void StopTest()
        {
            measuringTest = false;
            if (testInstance.isValid())
            {
                testInstance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                testInstance.release();
            }
            testInstance.clearHandle();
        }

        private string CaptureReport()
        {
            var text = new StringBuilder();
            text.AppendLine($"Unity: {Application.unityVersion}; platform: {Application.platform}");
            text.AppendLine($"Project: {Path.GetDirectoryName(Application.dataPath)}; working folder: {Directory.GetCurrentDirectory()}");
            text.AppendLine($"Scene: {SceneManager.GetActiveScene().path}");
            text.AppendLine($"Play={EditorApplication.isPlaying}; Paused={EditorApplication.isPaused}; Game Mute Audio={EditorUtility.audioMasterMute}");
            text.AppendLine($"Test: {testStatus}");
            if (!EditorApplication.isPlaying)
                return text.AppendLine("Enter Play Mode to collect runtime information.").ToString();

            try
            {
                var runtime = UnityEngine.Object.FindFirstObjectByType<Milestone1AudioRuntime>(FindObjectsInactive.Include);
                text.AppendLine($"Audio runtime: {Describe(runtime)}; initialized={runtime != null && runtime.IsInitialized}");
                text.AppendLine($"Bound scan: {Describe(runtime != null ? runtime.BoundScanSource : null)}");
                text.AppendLine($"Bound camera: {Describe(runtime != null ? runtime.BoundCameraSource : null)}");
                var scans = UnityEngine.Object.FindObjectsByType<ScanChargeUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                var cameras = UnityEngine.Object.FindObjectsByType<PhotoModeController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                text.AppendLine($"Scene controllers: {scans.Length} ScanChargeUI, {cameras.Length} PhotoModeController");
                foreach (var scan in scans)
                    text.AppendLine($"  Scan: {Describe(scan)}; charging={scan.IsCharging}; charge={scan.Charge01:F2}");
                foreach (var camera in cameras)
                    text.AppendLine($"  Camera: {Describe(camera)}; active={camera.IsActive}; focusing={camera.IsFocusing}");
                text.AppendLine($"Gameplay sound starts={Milestone1AudioDiagnostics.StartedCount}; active instances={Milestone1AudioDiagnostics.ActiveInstanceCount}; last={Milestone1AudioDiagnostics.LastStartedEvent ?? "none"}");
                text.AppendLine($"Audio configuration: {(Resources.Load<Milestone1AudioSettings>("Milestone1AudioSettings") != null ? "found" : "MISSING")}");

                string bankFolder = Settings.Instance.SourceBankPath;
                text.AppendLine($"Banks folder: {Path.GetFullPath(bankFolder)}");
                foreach (string bank in new[] { "Master", "Master.strings", "RobotTools" })
                    text.AppendLine($"  {bank}: file={File.Exists(Path.Combine(bankFolder, bank + ".bank"))}, loaded={(RuntimeManager.IsInitialized ? RuntimeManager.HasBankLoaded(bank).ToString() : "not initialized")}");
                if (!RuntimeManager.IsInitialized)
                    return text.AppendLine("FMOD runtime is not initialized. Play Test Scan to test initialization independently.").ToString();

                foreach (var manager in Resources.FindObjectsOfTypeAll<RuntimeManager>())
                    text.AppendLine($"FMOD manager: enabled={manager.enabled}, active={manager.gameObject.activeInHierarchy}");

                var core = RuntimeManager.CoreSystem;
                FMOD.RESULT outputResult = core.getOutput(out FMOD.OUTPUTTYPE output);
                text.AppendLine($"Output: {output} ({outputResult}); FMOD mute={RuntimeManager.IsMuted}");
                FMOD.RESULT driverResult = core.getDriver(out int driver);
                text.AppendLine($"Selected device index: {driver} ({driverResult})");
                FMOD.RESULT countResult = core.getNumDrivers(out int count);
                text.AppendLine($"Available devices: {count} ({countResult})");
                for (int i = 0; i < count; ++i)
                {
                    FMOD.RESULT result = core.getDriverInfo(i, out string name, 512, out Guid guid, out int rate, out FMOD.SPEAKERMODE mode, out int channels);
                    text.AppendLine($"  {(i == driver ? "SELECTED " : "")}{i}: {name}; {rate} Hz; {mode}; {channels} channels ({result})");
                }
                FMOD.RESULT channelResult = core.getChannelsPlaying(out int total, out int real);
                text.AppendLine($"Channels: real={real}, total={total} ({channelResult})");
                if (measuringTest)
                    peakChannels = Math.Max(peakChannels, real);

                FMOD.RESULT busResult = RuntimeManager.StudioSystem.getBus("bus:/", out FMOD.Studio.Bus bus);
                if (busResult == FMOD.RESULT.OK)
                {
                    var muteResult = bus.getMute(out bool mute);
                    var pauseResult = bus.getPaused(out bool paused);
                    var volumeResult = bus.getVolume(out float volume, out float finalVolume);
                    text.AppendLine($"Master bus: mute={mute} ({muteResult}), paused={paused} ({pauseResult}), volume={volume:F3}, final={finalVolume:F3} ({volumeResult})");
                }
                else
                    text.AppendLine($"Master bus: {busResult}");

                if (core.getMasterChannelGroup(out FMOD.ChannelGroup group) == FMOD.RESULT.OK
                    && group.getDSP(0, out FMOD.DSP dsp) == FMOD.RESULT.OK
                    && dsp.getMeteringEnabled(out bool inputMeter, out bool outputMeter) == FMOD.RESULT.OK
                    && outputMeter
                    && dsp.getMeteringInfo(IntPtr.Zero, out FMOD.DSP_METERING_INFO meter) == FMOD.RESULT.OK
                    && meter.numchannels > 0)
                {
                    float power = 0;
                    for (int i = 0; i < meter.numchannels; ++i)
                        power += meter.rmslevel[i] * meter.rmslevel[i];
                    float rms = Mathf.Sqrt(power / meter.numchannels);
                    float db = rms > 0 ? 20f * Mathf.Log10(rms * Mathf.Sqrt(2f)) : -80f;
                    if (measuringTest)
                    {
                        peakDb = Mathf.Max(peakDb, db);
                        measuredOutput = true;
                    }
                    text.AppendLine($"Output RMS: {db:F1} dB");
                }
                else
                    text.AppendLine("Output meter unavailable (enable FMOD Debug Overlay to measure).");

                foreach (string path in new[] { "Robot/Scan/Charge", "Robot/Scan/Pulse", "Robot/Camera/Open", "Robot/Camera/Close", "Robot/Camera/Move", "Robot/Camera/Focus", "Robot/Camera/Shutter" })
                    text.AppendLine($"event:/{path}: {RuntimeManager.StudioSystem.getEvent("event:/" + path, out FMOD.Studio.EventDescription description)}");
            }
            catch (Exception exception)
            {
                text.AppendLine(exception.ToString());
            }
            return text.ToString();
        }

        private static string Describe(Behaviour source)
        {
            return source == null ? "NONE" : $"{source.name} [{source.gameObject.scene.name}], enabled={source.isActiveAndEnabled}";
        }
    }
}
