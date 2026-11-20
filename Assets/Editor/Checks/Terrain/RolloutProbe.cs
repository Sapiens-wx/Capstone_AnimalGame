using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using Capstone.Audio;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    internal static class RolloutProbe
    {
        private const string Key = "AnimalGame.RolloutProbe";
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly int[] Rates = { 60, 120, 180 };
        private static readonly float[] Throttles = { .48f, 1f };

        [Serializable]
        private sealed class Config
        {
            public string scenePath, reportPath;
            public Route[] routes;
        }

        [Serializable]
        private sealed class Route
        {
            public string name;
            public Vector2 startMeters, center, endMeters;
            public int[] rates;
            public float[] throttles;
        }

        [Serializable]
        private sealed class Result
        {
            public string status = "RUNNING";
            public Config route;
            public List<Run> runs = new();
        }

        [Serializable]
        private sealed class Run
        {
            public string routeName;
            public bool completed, endedAirborne;
            public int simulationHz, frames, takeoffs, blockedFrames, skippedFrames;
            public float inputTrigger, averageWallFps, minStepSeconds = 1f, maxStepSeconds,
                simulationSeconds, maximumSpeedMetersPerSecond, maximumSurfaceSlopeDegrees,
                maxCrossTrackMeters, maximumAirGapMeters, maxCameraOffset, maxCameraRollDegrees,
                maximumThrottleIntent;
            public Vector2 center, startMeters, endMeters, finishMeters;
            public List<Impact> landings = new();
        }

        [Serializable]
        private sealed class Impact
        {
            public Vector2 meters;
            public float timeSeconds, distancePastCrestMeters, impactSpeedMetersPerSecond, airSeconds, strength,
                cameraOffset, cameraRollDegrees, cameraZoomFraction;
            public string landCue, partsCue;
        }

        private static Config config;
        private static Result result;
        private static HeightMapPlayerSceneBootstrap player;
        private static Gamepad pad;
        private static readonly List<Gamepad> removedPads = new();
        private static InputSettings originalSettings, testSettings;
        private static bool originalBackground;
        private static float originalCaptureDelta;
        private static int originalFrameRate, originalVSync;
        private static int lastFrame = -1, runFrame, routeIndex, runIndex;
        private static double playStarted, runStarted, runTime;
        private static bool driveActive, wasAirborne, settingsCaptured;
        private static Vector2 direction, lastMeters;
        private static Run run;
        private static object landSlot, partsSlot;
        private static FieldInfo eventInstanceField;
        private static StringBuilder trace;

        internal static void Begin(string scenePath, Vector2[] centers, Vector2[] forward, string reportPath)
        {
            if (centers.Length == 0 || forward.Length != centers.Length)
                throw new ArgumentException("Provide a forward direction for each mound center.");
            var routes = new Route[centers.Length];
            for (int index = 0; index < routes.Length; index++)
            {
                Vector2 direction = forward[index].normalized;
                Vector2 center = centers[index];
                routes[index] = new Route
                {
                    name = $"mound-{index + 1}", center = center,
                    startMeters = center - direction * 15f, endMeters = center + direction * 13f,
                    rates = Rates, throttles = Throttles,
                };
            }
            BeginPlan(new Config { scenePath = scenePath, reportPath = reportPath, routes = routes });
        }

        internal static void BeginFromFile(string planPath) =>
            BeginPlan(JsonUtility.FromJson<Config>(File.ReadAllText(planPath)));

        private static void BeginPlan(Config request)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !string.IsNullOrEmpty(SessionState.GetString(Key, "")))
                throw new InvalidOperationException("Exit Play and wait for the active probe to end first.");
            if (request.routes == null || request.routes.Length == 0)
                throw new ArgumentException("Provide at least one driving route.");
            foreach (Route route in request.routes)
            {
                if (string.IsNullOrWhiteSpace(route.name) || route.name.Contains(',') || route.startMeters == route.endMeters)
                    throw new ArgumentException("Each route needs a name without commas and distinct start and finish positions.");
                if (route.rates == null || route.rates.Length == 0 || Array.Exists(route.rates, rate => rate <= 0)
                    || route.throttles == null || route.throttles.Length == 0
                    || Array.Exists(route.throttles, throttle => !(throttle > 0f && throttle <= 1f)))
                    throw new ArgumentException($"Route {route.name} requires positive simulation rates and throttle in (0, 1].");
            }
            for (int index = 0; index < SceneManager.sceneCount; index++)
                if (SceneManager.GetSceneAt(index).isDirty)
                    throw new InvalidOperationException("Save the open scenes before running the probe.");
            if (SceneManager.GetActiveScene().path != request.scenePath || SceneManager.sceneCount != 1)
                EditorSceneManager.OpenScene(request.scenePath, OpenSceneMode.Single);

            request.reportPath = Path.GetFullPath(request.reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(request.reportPath));
            File.WriteAllText(request.reportPath, JsonUtility.ToJson(new Result { route = request }, true));
            SessionState.SetString(Key, JsonUtility.ToJson(request));
            Attach();
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        private static void Attach()
        {
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            if (string.IsNullOrEmpty(SessionState.GetString(Key, ""))) return;
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            try
            {
                if (config == null)
                {
                    config = JsonUtility.FromJson<Config>(SessionState.GetString(Key, ""));
                    result = new Result { route = config };
                    trace = new StringBuilder("simulationHz,input,seconds,x,z,height,speed,airborne,landing,cameraOffset,cameraRoll,blocked,route\n");
                    routeIndex = runIndex = 0;
                    playStarted = EditorApplication.timeSinceStartup;
                }
                if (player == null)
                {
                    if (SceneManager.GetActiveScene().path != config.scenePath)
                        throw new InvalidOperationException("Unexpected active Play scene.");
                    player = Object.FindFirstObjectByType<HeightMapPlayerSceneBootstrap>();
                    if (player == null || player.robot == null || player.map == null
                        || !player.map.HasGeneratedMap || player.heightMotion == null || player.cameraShake == null
                        || !player.heightMotion.HasData || Time.frameCount < 45)
                    {
                        player = null;
                        if (EditorApplication.timeSinceStartup - playStarted > 90d)
                            throw new InvalidOperationException("Play scene did not finish bootstrapping.");
                        return;
                    }

                    OfficialPlainsGameplay.Validate();
                    SetUpInputAndClock();
                    CacheAudioSlots();
                    player.heightMotion.Landed += OnLanded;
                    StartRun();
                    return;
                }

                if (!player.map.TrySampleWorldPosition(player.robot.transform.position, out Vector2 meters, out float height))
                    throw new InvalidOperationException("Robot left the playable map at " + player.robot.transform.position);
                if (player.robot.IsMovementLocked)
                    throw new InvalidOperationException("Robot became " + player.robot.MovementMode + " at " + meters);
                float dt = (float)(Time.timeAsDouble - runTime);
                if (dt <= 0f) throw new InvalidOperationException("Gameplay was paused during the route.");
                runTime = Time.timeAsDouble;
                int frameStep = Time.frameCount - runFrame;
                runFrame = Time.frameCount;
                float speed = Vector2.Distance(meters, lastMeters) / dt;
                lastMeters = meters;
                run.frames += frameStep;
                run.skippedFrames += frameStep - 1;
                run.simulationSeconds += dt;
                run.minStepSeconds = Mathf.Min(run.minStepSeconds, Time.deltaTime);
                run.maxStepSeconds = Mathf.Max(run.maxStepSeconds, Time.deltaTime);
                run.maximumThrottleIntent = Mathf.Max(run.maximumThrottleIntent, player.robot.CurrentThrottleIntent);
                run.maximumSpeedMetersPerSecond = Mathf.Max(run.maximumSpeedMetersPerSecond, speed);
                run.maximumSurfaceSlopeDegrees = Mathf.Max(run.maximumSurfaceSlopeDegrees, player.robot.CurrentTraversalResult.MaximumSurfaceSlopeAngle);
                Vector2 displacement = meters - run.startMeters;
                float progress = Vector2.Dot(displacement, direction);
                run.maxCrossTrackMeters = Mathf.Max(run.maxCrossTrackMeters, (displacement - direction * progress).magnitude);
                run.maximumAirGapMeters = Mathf.Max(run.maximumAirGapMeters, player.heightMotion.CurrentAirGapMeters);
                float cameraOffset = player.cameraShake.CurrentLocalPositionOffset.magnitude;
                float cameraRoll = player.cameraShake.CurrentRotationOffsetDegrees;
                run.maxCameraOffset = Mathf.Max(run.maxCameraOffset, cameraOffset);
                run.maxCameraRollDegrees = Mathf.Max(run.maxCameraRollDegrees, Mathf.Abs(cameraRoll));
                if (player.heightMotion.IsAirborne && !wasAirborne) run.takeoffs++;
                wasAirborne = player.heightMotion.IsAirborne;
                if (player.robot.IsSlopeBlocked) run.blockedFrames++;
                if (player.heightMotion.LandedThisFrame && run.landings.Count > 0)
                {
                    Impact impact = run.landings[run.landings.Count - 1];
                    impact.cameraOffset = cameraOffset;
                    impact.cameraRollDegrees = cameraRoll;
                    impact.cameraZoomFraction = player.cameraShake.CurrentZoomOffsetFraction;
                }

                trace.AppendFormat(CultureInfo.InvariantCulture,
                    "{0},{1:F2},{2:F6},{3:F5},{4:F5},{5:F5},{6:F4},{7},{8},{9:F5},{10:F5},{11},{12}\n",
                    run.simulationHz, run.inputTrigger, run.simulationSeconds, meters.x, meters.y, height, speed,
                    player.heightMotion.IsAirborne ? 1 : 0, player.heightMotion.LandedThisFrame ? 1 : 0,
                    cameraOffset, cameraRoll, player.robot.IsSlopeBlocked ? 1 : 0, run.routeName);

                if (run.frames > run.simulationHz * 45)
                    throw new InvalidOperationException($"Robot failed to finish {run.routeName}. Last position {meters}.");
                if (progress < Vector2.Distance(run.startMeters, run.endMeters)) return;
                run.completed = true;
                run.endedAirborne = wasAirborne;
                run.finishMeters = meters;
                run.averageWallFps = (float)(run.frames / (EditorApplication.timeSinceStartup - runStarted));
                SaveReport();
                Route route = config.routes[routeIndex];
                if (++runIndex == route.rates.Length * route.throttles.Length)
                {
                    routeIndex++;
                    runIndex = 0;
                }
                if (routeIndex < config.routes.Length) StartRun();
                else Finish("COMPLETE");
            }
            catch (Exception exception)
            {
                Finish("FAIL: " + exception);
            }
        }

        private static void SetUpInputAndClock()
        {
            originalBackground = Application.runInBackground;
            originalFrameRate = Application.targetFrameRate;
            originalVSync = QualitySettings.vSyncCount;
            originalCaptureDelta = Time.captureDeltaTime;
            originalSettings = InputSystem.settings;
            settingsCaptured = true;
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            testSettings = Object.Instantiate(originalSettings);
            testSettings.hideFlags = HideFlags.HideAndDontSave;
            testSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            testSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings = testSettings;
            removedPads.AddRange(Gamepad.all);
            foreach (Gamepad oldPad in removedPads) InputSystem.RemoveDevice(oldPad);
            pad = InputSystem.AddDevice<Gamepad>("Microrelief rollout probe virtual gamepad");
            pad.MakeCurrent();
            InputSystem.onBeforeUpdate += Drive;
        }

        private static void Drive()
        {
            if (driveActive && InputState.currentUpdateType == InputUpdateType.Dynamic)
                InputSystem.QueueStateEvent(pad, new GamepadState { rightTrigger = run.inputTrigger });
        }

        private static void StartRun()
        {
            Route route = config.routes[routeIndex];
            int rate = route.rates[runIndex / route.throttles.Length];
            float throttle = route.throttles[runIndex % route.throttles.Length];
            Time.captureDeltaTime = 1f / rate;
            Application.targetFrameRate = rate;

            Vector2 startMeters = route.startMeters;
            Vector2 endMeters = route.endMeters;
            direction = (endMeters - startMeters).normalized;
            if (!player.map.TrySampleMapPosition(startMeters, out _)
                || !player.map.TrySampleMapPosition(endMeters, out _))
                throw new InvalidOperationException($"Probe route {route.name} starts or ends outside the playable map.");

            player.robot.Teleport(player.map.MapPositionToWorld(startMeters));
            player.robot.transform.up = player.map.MapDirectionToWorldDirection(direction);
            player.heightMotion.Initialize(player.map);
            lastMeters = startMeters;
            run = new Run
            {
                routeName = route.name, simulationHz = rate, inputTrigger = throttle,
                center = route.center, startMeters = startMeters, endMeters = endMeters,
            };
            result.runs.Add(run);
            wasAirborne = false;
            driveActive = true;
            runFrame = Time.frameCount;
            runTime = Time.timeAsDouble;
            runStarted = EditorApplication.timeSinceStartup;
        }

        private static void CacheAudioSlots()
        {
            var runtime = Object.FindFirstObjectByType<Milestone2ActionRuntime>();
            if (runtime == null) return;
            object voices = runtime.GetType().GetField("voices", Fields).GetValue(runtime);
            var slots = (Array)voices.GetType().GetField("slots", Fields).GetValue(voices);
            Type cue = voices.GetType().Assembly.GetType("Capstone.Audio.Milestone2ActionCue", true);
            landSlot = slots.GetValue(Convert.ToInt32(Enum.Parse(cue, "Land")));
            partsSlot = slots.GetValue(Convert.ToInt32(Enum.Parse(cue, "LandParts")));
            eventInstanceField = landSlot.GetType().GetField("Instance", Fields);
        }

        private static string AudioState(object slot)
        {
            if (slot == null) return "runtime missing";
            var instance = (FMOD.Studio.EventInstance)eventInstanceField.GetValue(slot);
            if (!instance.isValid()) return "invalid";
            return instance.getPlaybackState(out FMOD.Studio.PLAYBACK_STATE state) == FMOD.RESULT.OK
                ? state.ToString() : "unavailable";
        }

        private static void OnLanded(RobotLandingImpact landing)
        {
            if (!driveActive) return;
            player.map.TrySampleWorldPosition(player.robot.transform.position, out Vector2 meters, out _);
            run.landings.Add(new Impact
            {
                meters = meters,
                timeSeconds = (float)(Time.timeAsDouble - runTime) + run.simulationSeconds,
                distancePastCrestMeters = Vector2.Dot(meters - run.center, direction),
                airSeconds = landing.AirborneDurationSeconds,
                strength = landing.Strength01,
                impactSpeedMetersPerSecond = landing.RelativeImpactSpeedMetersPerSecond,
                landCue = AudioState(landSlot),
                partsCue = AudioState(partsSlot),
            });
        }

        private static void SaveReport()
        {
            File.WriteAllText(config.reportPath, JsonUtility.ToJson(result, true));
            File.WriteAllText(Path.ChangeExtension(config.reportPath, ".csv"), trace.ToString());
        }

        private static void Finish(string status, bool exitPlay = true)
        {
            string request = SessionState.GetString(Key, "");
            SessionState.EraseString(Key);
            Attach();
            try
            {
                if (result != null)
                {
                    result.status = status;
                    SaveReport();
                }
                else if (!string.IsNullOrEmpty(request))
                {
                    Config plan = JsonUtility.FromJson<Config>(request);
                    File.WriteAllText(plan.reportPath, JsonUtility.ToJson(new Result { status = status, route = plan }, true));
                }
            }
            finally
            {
                InputSystem.onBeforeUpdate -= Drive;
                if (player != null && player.heightMotion != null) player.heightMotion.Landed -= OnLanded;
                if (pad != null && pad.added) InputSystem.RemoveDevice(pad);
                foreach (Gamepad oldPad in removedPads)
                    if (!oldPad.added) InputSystem.AddDevice(oldPad);
                removedPads.Clear();
                if (settingsCaptured)
                {
                    Time.captureDeltaTime = originalCaptureDelta;
                    Application.targetFrameRate = originalFrameRate;
                    QualitySettings.vSyncCount = originalVSync;
                    Application.runInBackground = originalBackground;
                    InputSystem.settings = originalSettings;
                    Object.Destroy(testSettings);
                }
                config = null;
                result = null;
                player = null;
                pad = null;
                landSlot = partsSlot = null;
                driveActive = settingsCaptured = false;
                lastFrame = -1;
                if (exitPlay && EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            }
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
                Finish("FAIL: Play mode exited before the probe completed.", false);
        }
    }
}
