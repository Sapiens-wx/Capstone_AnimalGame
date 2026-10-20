using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AnimalGame.Demos.WasteRobot.Editor
{
    /// <summary>
    /// Explicit isolated-project batch runner. The transient session survives
    /// Play Mode domain reload; no InitializeOnLoad or runtime global hook exists.
    /// Omit -quit. Optional -wasteRobotSmokeFps 15|30|60 simulates long frames.
    /// </summary>
    public sealed class WasteRobotTransitionSmoke : ScriptableObject
    {
        private enum Phase
        {
            Waiting, Warmup, DriveIn, SettleIn, Multipliers, NeutralIn, DriveOut, SettleOut,
            ReverseStart, ReverseOut, SettleReverse, NoFadeOut, NoFadeIn, NoZoomPrepare,
            NoZoomFade, RestoreOutside, BindFade, BindCheck, DisableFade, DisableCheck,
            Reenable, DestroyFade, DestroyCheck, PauseFade, PauseStable, CameraSwitchFade,
            CameraSwitchWait, CameraDestroyFade, CameraDestroyWait, CameraFixtureCleanup,
            OwnerAcquire, OwnerRelease, Finishing
        }

        [Serializable]
        private sealed class Report
        {
            public string startedUtc;
            public string finishedUtc;
            public bool passed;
            public int simulatedFramesPerSecond;
            public int opaqueFramesObserved;
            public int completedBlackFrameCount;
            public float exteriorNeutralSize;
            public float renderedBlackPixelRatio;
            public List<string> checks = new List<string>();
            public List<string> errors = new List<string>();
            public List<string> notes = new List<string>();
            public List<string> screenshots = new List<string>();
        }

        [SerializeField] private bool running;
        [SerializeField] private Phase phase;
        [SerializeField] private Report report;
        [SerializeField] private string directory;
        [SerializeField] private int requestedFps;
        [SerializeField] private float previousCaptureDelta;
        [SerializeField] private bool clockSaved;
        [SerializeField] private double deadline;
        [SerializeField] private double phaseStart;
        [SerializeField] private int phaseFrame;
        [SerializeField] private int lastFrame = -1;
        [SerializeField] private int deviceId;
        [SerializeField] private WasteRobotBase machine;
        [SerializeField] private WasteRobotTransitionEffects effects;
        [SerializeField] private Camera camera;
        [SerializeField] private RobotMover player;
        [SerializeField] private RobotCameraShake shake;
        [SerializeField] private bool originalShakeEnabled;
        [SerializeField] private bool shakeSettingSaved;
        [SerializeField] private float exteriorSize;
        [SerializeField] private int blackBaseline;
        [SerializeField] private int opaqueBaseline;
        [SerializeField] private GameObject retiredOverlay;
        [SerializeField] private GameObject retiredVisual;
        [SerializeField] private WasteRobotInteriorCameraZoom retiredZoom;
        [SerializeField] private WasteRobotInteriorCameraZoom testZoom;
        [SerializeField] private GameObject ownerA;
        [SerializeField] private GameObject ownerB;
        [SerializeField] private bool capturedBlack;
        [SerializeField] private GameObject cameraFixture;
        [SerializeField] private WasteRobotBase fixtureMachine;
        [SerializeField] private WasteRobotTransitionEffects fixtureEffects;
        [SerializeField] private Camera fixtureCameraA;
        [SerializeField] private Camera fixtureCameraB;
        [SerializeField] private GameObject fixtureCameraBRoot;
        [SerializeField] private float previousTimeScale;
        [SerializeField] private bool timeScaleSaved;
        [SerializeField] private int fixtureBlackBaseline;
        [SerializeField] private int fixtureOpaqueBaseline;
        [SerializeField] private GameObject cameraRetiredOverlay;
        [SerializeField] private GameObject cameraRetiredVisual;
        [SerializeField] private WasteRobotInteriorCameraZoom cameraRetiredZoom;

        public static void RunBatch()
        {
            try
            {
                Require(Application.isBatchMode && !EditorApplication.isPlayingOrWillChangePlaymode,
                    "Start this entry point in a clean isolated batch project's Edit Mode.");
                Require(!Resources.FindObjectsOfTypeAll<WasteRobotTransitionSmoke>().Any(session => session.running),
                    "A transition smoke session is already running.");
                Require(AssetDatabase.LoadAssetAtPath<SceneAsset>(WasteRobotDemoBuilder.ScenePath) != null,
                    "Build the independent Demo scene first.");
                int fps = RequestedFps();
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    Require(!SceneManager.GetSceneAt(i).isDirty, "Do not replace unsaved scenes; run in a fresh isolated project process.");
                EditorSceneManager.OpenScene(WasteRobotDemoBuilder.ScenePath, OpenSceneMode.Single);
                var session = CreateInstance<WasteRobotTransitionSmoke>();
                session.hideFlags = HideFlags.HideAndDontSave;
                session.directory = OutputDirectory();
                Directory.CreateDirectory(session.directory);
                session.requestedFps = fps;
                session.report = new Report { startedUtc = DateTime.UtcNow.ToString("O"), simulatedFramesPerSecond = fps };
                session.report.notes.Add("Original Demo player uses real Gamepad input for inward/outward traversal. Subsequent effects/lifecycle cases use explicit SetInside requests while stopped.");
                session.report.notes.Add("Only the running camera instance's continuous-shake setting is disabled for deterministic size checks; the original enabled Shake component still composes scan/focus multipliers.");
                session.running = true;
                session.phase = Phase.Waiting;
                session.deadline = EditorApplication.timeSinceStartup + 180d;
                session.phaseStart = EditorApplication.timeSinceStartup;
                session.Subscribe();
                session.Save();
                EditorApplication.isPlaying = true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        private void OnEnable() { if (running) Subscribe(); }
        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
        }
        private void Subscribe()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
        }

        private void Tick()
        {
            if (!running) return;
            try
            {
                if (phase == Phase.Finishing)
                {
                    if ((!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
                        || EditorApplication.timeSinceStartup > deadline) Exit();
                    return;
                }
                Require(EditorApplication.timeSinceStartup <= deadline, "Transition smoke exceeded 180 seconds.");
                EditorApplication.QueuePlayerLoopUpdate();
                if (!EditorApplication.isPlaying)
                {
                    Require(phase == Phase.Waiting || EditorApplication.isPlayingOrWillChangePlaymode,
                        "Play Mode ended before transition validation completed.");
                    return;
                }
                if (lastFrame == Time.frameCount) return;
                lastFrame = Time.frameCount;
                SampleEffects();
                Require(EditorApplication.timeSinceStartup - phaseStart <= 25d, "Timed out in phase " + phase);

                switch (phase)
                {
                    case Phase.Waiting:
                        InitializePlay();
                        break;
                    case Phase.Warmup:
                        Drive(0f);
                        if (Frames(5))
                        {
                            exteriorSize = shake.BaseOrthographicSize;
                            report.exteriorNeutralSize = exteriorSize;
                            Require(!machine.IsInside && SizeNear(exteriorSize), "Initial exterior camera size is not neutral.");
                            CaptureCurrentCamera("transition-outside.png", false);
                            BaselineBlack();
                            Begin(Phase.DriveIn);
                        }
                        break;
                    case Phase.DriveIn:
                        Drive(.4f);
                        if (machine.IsInside && machine.WorldToPortal(player.transform.position).y <= -.65f)
                        {
                            ImmediateRoots(true);
                            Drive(0f);
                            Begin(Phase.SettleIn);
                        }
                        break;
                    case Phase.SettleIn:
                        Drive(0f);
                        if (Stopped() && Settled())
                        {
                            CompletedBlack("Real inward traversal");
                            Record(SizeNear(6.5f), "Real inward crossing settles at neutral interior size 6.5.");
                            Record(effects.ActiveCamera == camera && effects.ActiveZoom != null
                                && effects.ActiveZoom.Owner == effects, "Local effects own the correct player's camera zoom adapter.");
                            Record(Mathf.Abs(shake.BaseOrthographicSize - exteriorSize) < .001f,
                                "Interior zoom preserves the original Shake base camera size.");
                            CaptureCurrentCamera("transition-inside-6_5.png", false);
                            shake.SetScanZoomMultiplier(1.4f);
                            shake.SetPhotoFocusMagnification(2f);
                            Begin(Phase.Multipliers);
                        }
                        break;
                    case Phase.Multipliers:
                        if (Frames(5))
                        {
                            Record(SizeNear(6.5f * 1.4f / 2f), "Interior zoom composes with scan 1.4 and focus magnification 2.");
                            Record(shake.enabled && Mathf.Abs(shake.BaseOrthographicSize - exteriorSize) < .001f,
                                "Scan/focus compatibility retains the enabled original camera controller and its base size.");
                            shake.SetScanZoomMultiplier(1f);
                            shake.SetPhotoFocusMagnification(1f);
                            Begin(Phase.NeutralIn);
                        }
                        break;
                    case Phase.NeutralIn:
                        if (Frames(4))
                        {
                            Record(SizeNear(6.5f), "Releasing scan/focus returns to neutral interior zoom.");
                            BaselineBlack();
                            Begin(Phase.DriveOut);
                        }
                        break;
                    case Phase.DriveOut:
                        Drive(-.4f);
                        if (!machine.IsInside && machine.WorldToPortal(player.transform.position).y >= .65f)
                        {
                            ImmediateRoots(false);
                            Drive(0f);
                            Begin(Phase.SettleOut);
                        }
                        break;
                    case Phase.SettleOut:
                        Drive(0f);
                        if (Stopped() && Settled())
                        {
                            CompletedBlack("Real outward traversal");
                            Record(SizeNear(exteriorSize), "Real outward crossing restores the exterior camera size.");
                            Record(machine.TransitionCount == 2, "Real forward/reverse driving caused exactly two crossings.");
                            BaselineBlack();
                            SetInside(true);
                            Begin(Phase.ReverseStart);
                        }
                        break;
                    case Phase.ReverseStart:
                        if (effects.FadeAlpha > .05f)
                        {
                            SetInside(false);
                            Begin(Phase.ReverseOut);
                        }
                        break;
                    case Phase.ReverseOut:
                        if (Frames(1))
                        {
                            SetInside(true);
                            Begin(Phase.SettleReverse);
                        }
                        break;
                    case Phase.SettleReverse:
                        if (Settled())
                        {
                            CompletedBlack("Latest rapid reverse request");
                            Record(machine.IsInside && effects.ActiveZoom != null && effects.ActiveZoom.DesiredInterior
                                && SizeNear(6.5f), "Rapid inward/outward/inward requests finish in the latest authoritative state.");
                            effects.FadeEnabled = false;
                            BaselineBlack();
                            SetInside(false);
                            Begin(Phase.NoFadeOut);
                        }
                        break;
                    case Phase.NoFadeOut:
                        Require(effects.FadeAlpha <= .001f, "Disabling fade still produced a dark mask.");
                        if (Settled() && Frames(4))
                        {
                            Record(SizeNear(exteriorSize), "Zoom still restores outside while fade is disabled.");
                            SetInside(true);
                            Begin(Phase.NoFadeIn);
                        }
                        break;
                    case Phase.NoFadeIn:
                        Require(effects.FadeAlpha <= .001f, "Fade-disabled entry produced a dark mask.");
                        if (Settled() && Frames(4))
                        {
                            Record(SizeNear(6.5f) && effects.BlackFrameCount == blackBaseline,
                                "Fade can be disabled independently while interior zoom still works.");
                            effects.ZoomEnabled = false;
                            SetInside(false);
                            Begin(Phase.NoZoomPrepare);
                        }
                        break;
                    case Phase.NoZoomPrepare:
                        if (Frames(4))
                        {
                            Require(SizeNear(exteriorSize), "Disabling zoom did not remove its camera multiplier.");
                            effects.FadeEnabled = true;
                            BaselineBlack();
                            SetInside(true);
                            Begin(Phase.NoZoomFade);
                        }
                        break;
                    case Phase.NoZoomFade:
                        if (Settled())
                        {
                            CompletedBlack("Fade with zoom disabled");
                            Record(SizeNear(exteriorSize), "Fade can run independently without changing camera zoom.");
                            effects.ZoomEnabled = true;
                            SetInside(false);
                            Begin(Phase.RestoreOutside);
                        }
                        break;
                    case Phase.RestoreOutside:
                        if (Settled() && SizeNear(exteriorSize))
                        {
                            SetInside(true);
                            Begin(Phase.BindFade);
                        }
                        break;
                    case Phase.BindFade:
                        if (effects.FadeAlpha > .05f)
                        {
                            machine.Bind(player.transform, true);
                            Begin(Phase.BindCheck);
                        }
                        break;
                    case Phase.BindCheck:
                        if (Frames(3))
                        {
                            Record(!effects.IsTransitioning && effects.FadeAlpha <= .001f
                                && Inactive(effects.OutgoingVisualObject) && Inactive(effects.FadeOverlayObject),
                                "Bind reset cancels an unfinished fade without retaining a black mask or outgoing visual.");
                            SetInside(false);
                            Begin(Phase.DisableFade);
                        }
                        break;
                    case Phase.DisableFade:
                        if (effects.FadeAlpha > .05f)
                        {
                            RetireObjects();
                            effects.enabled = false;
                            Begin(Phase.DisableCheck);
                        }
                        break;
                    case Phase.DisableCheck:
                        if (Frames(4))
                        {
                            Record(!effects.IsTransitioning && effects.FadeAlpha <= .001f && RetiredClean()
                                && SizeNear(exteriorSize), "Disabling effects clears mask/proxy, releases zoom ownership and restores the camera.");
                            effects.enabled = true;
                            Begin(Phase.Reenable);
                        }
                        break;
                    case Phase.Reenable:
                        if (Frames(4))
                        {
                            SetInside(true);
                            Begin(Phase.DestroyFade);
                        }
                        break;
                    case Phase.DestroyFade:
                        if (effects.FadeAlpha > .05f)
                        {
                            RetireObjects();
                            Object.Destroy(effects);
                            Begin(Phase.DestroyCheck);
                        }
                        break;
                    case Phase.DestroyCheck:
                        if (Frames(4))
                        {
                            Record(effects == null && RetiredClean() && SizeNear(exteriorSize),
                                "Destroying active effects leaves no mask/proxy or owned camera multiplier.");
                            BeginCameraFixture();
                        }
                        break;
                    case Phase.PauseFade:
                        Require(Time.timeScale == 0f, "The paused transition fixture unexpectedly resumed simulation.");
                        Require(fixtureCameraA.orthographicSize >= 6.45f && fixtureCameraA.orthographicSize <= 10.05f,
                            "Paused zoom compounded its multiplier instead of replacing its own previous write.");
                        if (FixtureSettled())
                        {
                            Record(fixtureEffects.BlackFrameCount > fixtureBlackBaseline
                                && report.opaqueFramesObserved > fixtureOpaqueBaseline,
                                "At timeScale zero, fade reaches a complete black frame and finishes using unscaled time.");
                            Require(Mathf.Abs(fixtureCameraA.orthographicSize - 6.5f) < .04f,
                                "Paused interior zoom failed to settle at 6.5.");
                            Begin(Phase.PauseStable);
                        }
                        break;
                    case Phase.PauseStable:
                        Require(Time.timeScale == 0f && Mathf.Abs(fixtureCameraA.orthographicSize - 6.5f) < .04f,
                            "The paused camera drifted or repeatedly multiplied an already-applied zoom.");
                        if (Frames(20))
                        {
                            Record(true, "Paused interior zoom stays at 6.5 for twenty complete frames without cumulative scaling.");
                            RestoreTimeScale();
                            fixtureCameraB = CreateFixtureCamera("Transition camera B", 12f);
                            fixtureCameraBRoot = fixtureCameraB.gameObject;
                            fixtureMachine.SetInside(false);
                            Begin(Phase.CameraSwitchFade);
                        }
                        break;
                    case Phase.CameraSwitchFade:
                        if (fixtureEffects.FadeAlpha > .05f)
                        {
                            RetireFixtureCameraObjects();
                            OverrideFixtureCamera(fixtureCameraB);
                            Begin(Phase.CameraSwitchWait);
                        }
                        break;
                    case Phase.CameraSwitchWait:
                        if (fixtureEffects.ActiveCamera == fixtureCameraB && FixtureSettled() && Frames(4))
                        {
                            Record(fixtureCameraA.isActiveAndEnabled && CameraRetiredClean()
                                && Mathf.Abs(fixtureCameraA.orthographicSize - 10f) < .04f
                                && Mathf.Abs(fixtureCameraB.orthographicSize - 12f) < .04f,
                                "Switching override to another active same-player camera releases the old zoom and old fade objects without disabling that camera.");
                            Record(SizeNear(exteriorSize), "Temporary camera replacement never modifies the real Demo camera.");
                            fixtureMachine.SetInside(true);
                            Begin(Phase.CameraDestroyFade);
                        }
                        break;
                    case Phase.CameraDestroyFade:
                        if (fixtureEffects.FadeAlpha > .05f)
                        {
                            RetireFixtureCameraObjects();
                            OverrideFixtureCamera(fixtureCameraA);
                            // Keep the GameObject, follow component, zoom adapter
                            // and old child mask alive: only the Camera becomes
                            // a Unity fake-null reference, exercising cleanup.
                            Object.Destroy(fixtureCameraB);
                            Begin(Phase.CameraDestroyWait);
                        }
                        break;
                    case Phase.CameraDestroyWait:
                        if (fixtureEffects.ActiveCamera == fixtureCameraA && FixtureSettled() && Frames(4))
                        {
                            Record(fixtureCameraB == null && !ReferenceEquals(fixtureCameraB, null)
                                && fixtureCameraBRoot != null && fixtureCameraBRoot.activeInHierarchy,
                                "The replaced-camera fixture retains a destroyed Camera's managed fake-null reference while its GameObject remains active.");
                            Record(CameraRetiredClean() && Mathf.Abs(fixtureCameraA.orthographicSize - 6.5f) < .04f,
                                "Destroying the current Camera clears its old mask/proxy and zoom owner, then binds the valid replacement camera.");
                            fixtureEffects.enabled = false;
                            Object.Destroy(cameraFixture);
                            Object.Destroy(fixtureCameraA.gameObject);
                            Object.Destroy(fixtureCameraBRoot);
                            Begin(Phase.CameraFixtureCleanup);
                        }
                        break;
                    case Phase.CameraFixtureCleanup:
                        if (Frames(4))
                        {
                            Record(cameraFixture == null && fixtureCameraA == null && fixtureCameraBRoot == null
                                && SizeNear(exteriorSize), "Independent camera/pause fixtures clean up without changing the real player's camera.");
                            BeginOwnerChecks();
                        }
                        break;
                    case Phase.OwnerAcquire:
                        if (Frames(4))
                        {
                            Record(testZoom.Owner == ownerA && SizeNear(6.5f),
                                "A non-owner release cannot undo the active owner's composed zoom.");
                            testZoom.Release(ownerA, true);
                            Begin(Phase.OwnerRelease);
                        }
                        break;
                    case Phase.OwnerRelease:
                        if (Frames(4))
                        {
                            Record(testZoom.Owner == null && SizeNear(exteriorSize), "The correct owner's release restores neutral camera composition.");
                            Finish(null);
                        }
                        break;
                }
            }
            catch (Exception exception) { Finish(exception); }
        }

        private void InitializePlay()
        {
            WasteRobotDemoScene bootstrap = Object.FindFirstObjectByType<WasteRobotDemoScene>();
            if (bootstrap == null) return;
            machine = bootstrap.Machine;
            player = bootstrap.Player;
            camera = bootstrap.DemoCamera;
            Require(machine != null && player != null && camera != null, "Demo references are missing.");
            effects = machine.GetComponent<WasteRobotTransitionEffects>();
            shake = camera.GetComponent<RobotCameraShake>();
            Require(effects != null && effects.enabled && shake != null && shake.enabled, "Expected local effects and original camera controller.");
            if (requestedFps > 0)
            {
                previousCaptureDelta = Time.captureDeltaTime;
                clockSaved = true;
                Time.captureDeltaTime = 1f / requestedFps;
            }
            var settings = new SerializedObject(shake);
            SerializedProperty shakeProperty = settings.FindProperty("enableCameraShake");
            Require(shakeProperty != null, "The camera's deterministic shake setting is unavailable.");
            originalShakeEnabled = shakeProperty.boolValue;
            shakeSettingSaved = true;
            shakeProperty.boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Gamepad device = InputSystem.AddDevice<Gamepad>("Waste Robot transition smoke controller");
            device.MakeCurrent();
            deviceId = device.deviceId;
            Begin(Phase.Warmup);
        }

        private void SampleEffects()
        {
            SampleEffect(effects, true);
            SampleEffect(fixtureEffects, false);
        }

        private void SampleEffect(WasteRobotTransitionEffects value, bool primary)
        {
            if (value == null) return;
            report.completedBlackFrameCount = Mathf.Max(report.completedBlackFrameCount, value.BlackFrameCount);
            if (value.FadeAlpha >= .999f)
            {
                report.opaqueFramesObserved++;
                Require(value.FadeOverlayObject != null && value.FadeOverlayObject.activeInHierarchy,
                    "An opaque fade has no active runtime mask.");
                if (primary && !capturedBlack)
                {
                    CaptureCurrentCamera("transition-black.png", true);
                    capturedBlack = true;
                }
            }
            if (value.OutgoingVisualObject != null)
                Require(value.OutgoingVisualObject.GetComponentsInChildren<WorldInteraction>(true).Length == 0,
                    "The old-view visual proxy contains gameplay collision components.");
        }

        private void BeginCameraFixture()
        {
            fixtureCameraA = CreateFixtureCamera("Transition camera A", 10f);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(WasteRobotDemoBuilder.PrefabPath);
            Require(asset != null, "The independent camera fixture needs the generated prefab.");
            cameraFixture = Object.Instantiate(asset, new Vector3(10000f, 10000f, 0f), Quaternion.identity);
            cameraFixture.name = "Transition smoke independent camera fixture";
            SceneManager.MoveGameObjectToScene(cameraFixture, machine.gameObject.scene);
            fixtureMachine = cameraFixture.GetComponent<WasteRobotBase>();
            fixtureEffects = cameraFixture.GetComponent<WasteRobotTransitionEffects>();
            Require(fixtureMachine != null && fixtureEffects != null, "The independent camera fixture is missing its local components.");
            fixtureMachine.AutoBindPlayer = false;
            OverrideFixtureCamera(fixtureCameraA);
            fixtureMachine.Bind(player.transform, false);
            fixtureBlackBaseline = fixtureEffects.BlackFrameCount;
            fixtureOpaqueBaseline = report.opaqueFramesObserved;
            previousTimeScale = Time.timeScale;
            timeScaleSaved = true;
            Time.timeScale = 0f;
            fixtureMachine.SetInside(true);
            Begin(Phase.PauseFade);
        }

        private Camera CreateFixtureCamera(string name, float size)
        {
            var root = new GameObject(name);
            SceneManager.MoveGameObjectToScene(root, machine.gameObject.scene);
            root.transform.position = new Vector3(player.transform.position.x, player.transform.position.y, -10f);
            Camera value = root.AddComponent<Camera>();
            value.orthographic = true;
            value.orthographicSize = size;
            value.depth = camera.depth - 10f;
            value.clearFlags = CameraClearFlags.SolidColor;
            value.backgroundColor = camera.backgroundColor;
            value.cullingMask = camera.cullingMask;
            RobotCameraFollow follow = root.AddComponent<RobotCameraFollow>();
            follow.FollowTarget(player.transform, 0f);
            follow.SnapToTarget();
            return value;
        }

        private void OverrideFixtureCamera(Camera value)
        {
            var settings = new SerializedObject(fixtureEffects);
            SerializedProperty property = settings.FindProperty("cameraOverride");
            Require(property != null, "The local camera override configuration is unavailable.");
            property.objectReferenceValue = value;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        private bool FixtureSettled() => Frames(3) && fixtureEffects != null && !fixtureEffects.IsTransitioning
            && fixtureEffects.FadeAlpha <= .001f
            && (fixtureEffects.ActiveZoom == null || !fixtureEffects.ActiveZoom.IsBlending);

        private void RetireFixtureCameraObjects()
        {
            cameraRetiredOverlay = fixtureEffects.FadeOverlayObject;
            cameraRetiredVisual = fixtureEffects.OutgoingVisualObject;
            cameraRetiredZoom = fixtureEffects.ActiveZoom;
            Require(cameraRetiredOverlay != null && cameraRetiredVisual != null,
                "The camera-change regression must begin with a live fade and outgoing proxy.");
        }

        private bool CameraRetiredClean() => Inactive(cameraRetiredOverlay) && Inactive(cameraRetiredVisual)
            && (cameraRetiredZoom == null || cameraRetiredZoom.Owner == null);

        private void RestoreTimeScale() { if (timeScaleSaved) Time.timeScale = previousTimeScale; }

        private void BeginOwnerChecks()
        {
            testZoom = WasteRobotInteriorCameraZoom.GetOrCreate(camera);
            ownerA = new GameObject("Transition smoke zoom owner A");
            ownerB = new GameObject("Transition smoke zoom owner B");
            SceneManager.MoveGameObjectToScene(ownerA, machine.gameObject.scene);
            SceneManager.MoveGameObjectToScene(ownerB, machine.gameObject.scene);
            Require(testZoom.Acquire(ownerA, camera), "Zoom adapter rejected its available owner.");
            Record(!testZoom.Acquire(ownerB, camera) && testZoom.Owner == ownerA,
                "Another owner cannot steal an acquired camera zoom adapter.");
            testZoom.SetInterior(ownerA, true, 6.5f, 0f);
            testZoom.Release(ownerB, true);
            Begin(Phase.OwnerAcquire);
        }

        private void CaptureCurrentCamera(string filename, bool proveOpaque)
        {
            Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null,
                "Actual transition screenshots require a graphics device; omit -nographics.");
            RenderTexture target = null;
            Texture2D pixels = null;
            RenderTexture priorActive = RenderTexture.active;
            RenderTexture priorTarget = camera.targetTexture;
            Color priorBackground = camera.backgroundColor;
            float priorAspect = camera.aspect;
            float priorSize = camera.orthographicSize;
            Vector3 priorPosition = camera.transform.position;
            Quaternion priorRotation = camera.transform.rotation;
            try
            {
                int width = Mathf.Clamp(camera.pixelWidth, 640, 1920);
                int height = Mathf.Max(64, Mathf.RoundToInt(width / Mathf.Max(.1f, priorAspect)));
                target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                target.Create();
                camera.targetTexture = target;
                camera.aspect = priorAspect;
                if (proveOpaque)
                {
                    Require(camera.clearFlags == CameraClearFlags.SolidColor,
                        "The black-frame proof needs the Demo camera's authored solid-color clearing.");
                    // A black background alone cannot prove an opaque mask. This
                    // deliberately visible background is restored in the same
                    // synchronous render call, before another gameplay frame.
                    camera.backgroundColor = new Color(.15f, .45f, .9f, 1f);
                }
                Canvas.ForceUpdateCanvases();
                if (GraphicsSettings.currentRenderPipeline == null) camera.Render();
                else
                {
                    var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                    Require(RenderPipeline.SupportsRenderRequest(camera, request),
                        "The active renderer cannot capture the actual Demo camera.");
                    RenderPipeline.SubmitRenderRequest(camera, request);
                }
                RenderTexture.active = target;
                pixels = new Texture2D(width, height, TextureFormat.RGBA32, false);
                pixels.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                pixels.Apply();
                Color32[] image = pixels.GetPixels32();
                if (proveOpaque)
                {
                    float dark = image.Count(pixel => pixel.r <= 24 && pixel.g <= 24 && pixel.b <= 24) / (float)image.Length;
                    report.renderedBlackPixelRatio = dark;
                    Record(dark >= .995f,
                        "Actual camera render is at least 99.5% black at FadeAlpha one even with a temporarily bright blue camera background.");
                }
                else
                    Require(image.Count(pixel => pixel.r > 100 || pixel.g > 100 || pixel.b > 100) > 100,
                        "The actual-camera screenshot contains no visible white artwork.");
                string path = Path.Combine(directory, filename);
                File.WriteAllBytes(path, pixels.EncodeToPNG());
                report.screenshots.Add(path);
                report.notes.Add(filename + ": actual camera framing, position=" + priorPosition
                    + ", orthographicSize=" + priorSize.ToString("F4", CultureInfo.InvariantCulture)
                    + "; no camera fit, snap, position, rotation or size change. Camera render excludes overlay IMGUI HUD.");
                Save();
            }
            finally
            {
                camera.targetTexture = priorTarget;
                camera.backgroundColor = priorBackground;
                camera.aspect = priorAspect;
                RenderTexture.active = priorActive;
                Canvas.ForceUpdateCanvases();
                if (pixels != null) Object.DestroyImmediate(pixels);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                Require(Vector3.Distance(camera.transform.position, priorPosition) < .00001f
                    && Quaternion.Angle(camera.transform.rotation, priorRotation) < .0001f
                    && Mathf.Abs(camera.orthographicSize - priorSize) < .00001f,
                    "Screenshot rendering changed the runtime camera's gameplay framing.");
            }
        }

        private void ImmediateRoots(bool inside)
        {
            Require(machine.IsInside == inside && machine.InteriorRoot.activeInHierarchy == inside
                && machine.ExteriorRoot.activeInHierarchy != inside, "Fade delayed the collision/presentation root handoff.");
            GameObject hidden = inside ? machine.ExteriorRoot : machine.InteriorRoot;
            Require(hidden.GetComponentsInChildren<WorldInteraction>(true).All(wall => !wall.Available
                && !WorldInteraction.Active.Contains(wall)), "The outgoing gameplay collision root stayed registered during fade.");
        }

        private void SetInside(bool inside) { machine.SetInside(inside); ImmediateRoots(inside); }
        private bool Frames(int count) => Time.frameCount - phaseFrame >= count;
        private bool SizeNear(float value) => camera != null && Mathf.Abs(camera.orthographicSize - value) < .065f;
        private bool Stopped() => Frames(3) && Mathf.Abs(player.CurrentSpeed) < .03f;
        private bool Settled() => Frames(3) && effects != null && !effects.IsTransitioning
            && effects.FadeAlpha <= .001f && (effects.ActiveZoom == null || !effects.ActiveZoom.IsBlending);
        private static bool Inactive(GameObject value) => value == null || !value.activeInHierarchy;
        private void BaselineBlack() { blackBaseline = effects.BlackFrameCount; opaqueBaseline = report.opaqueFramesObserved; }
        private void CompletedBlack(string label)
        {
            Record(effects.BlackFrameCount > blackBaseline && report.opaqueFramesObserved > opaqueBaseline,
                label + " includes an observed opaque frame and a complete-frame black hold at "
                + (requestedFps > 0 ? requestedFps + " simulated fps." : "normal frame timing."));
        }
        private void RetireObjects()
        {
            retiredOverlay = effects.FadeOverlayObject;
            retiredVisual = effects.OutgoingVisualObject;
            retiredZoom = effects.ActiveZoom;
        }
        private bool RetiredClean() => Inactive(retiredOverlay) && Inactive(retiredVisual)
            && (retiredZoom == null || retiredZoom.Owner == null);
        private void Drive(float amount)
        {
            Gamepad device = InputSystem.GetDeviceById(deviceId) as Gamepad;
            Require(device != null && device.added, "Virtual driving device was lost.");
            device.MakeCurrent();
            InputSystem.QueueStateEvent(device, new GamepadState
            {
                leftStick = new Vector2(0f, amount), rightTrigger = Mathf.Max(0f, amount), leftTrigger = Mathf.Max(0f, -amount)
            });
        }
        private void Begin(Phase next)
        {
            phase = next;
            phaseFrame = Time.frameCount;
            phaseStart = EditorApplication.timeSinceStartup;
            Debug.Log("Waste Robot transition smoke phase: " + next);
        }
        private void Record(bool success, string message) { Require(success, message); report.checks.Add(message); Save(); }
        private void Save() { if (report != null && !string.IsNullOrEmpty(directory)) File.WriteAllText(Path.Combine(directory, "transition-smoke.json"), JsonUtility.ToJson(report, true)); }
        private void OnLog(string message, string stack, LogType type)
        {
            if (running && phase != Phase.Finishing && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
            { report.errors.Add(type + ": " + message + "\n" + stack); Save(); }
        }
        private void Finish(Exception exception)
        {
            if (phase == Phase.Finishing) return;
            if (exception != null) report.errors.Add(exception.ToString());
            RestoreTimeScale();
            Gamepad device = InputSystem.GetDeviceById(deviceId) as Gamepad;
            if (device != null && device.added) InputSystem.RemoveDevice(device);
            if (shake != null)
            {
                shake.SetScanZoomMultiplier(1f);
                shake.SetPhotoFocusMagnification(1f);
                if (shakeSettingSaved)
                {
                    var settings = new SerializedObject(shake);
                    settings.FindProperty("enableCameraShake").boolValue = originalShakeEnabled;
                    settings.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            if (testZoom != null && ownerA != null) testZoom.Release(ownerA, true);
            if (effects != null) effects.enabled = false;
            if (fixtureEffects != null) fixtureEffects.enabled = false;
            if (cameraFixture != null) Object.Destroy(cameraFixture);
            if (fixtureCameraA != null) Object.Destroy(fixtureCameraA.gameObject);
            if (fixtureCameraBRoot != null) Object.Destroy(fixtureCameraBRoot);
            if (ownerA != null) Object.Destroy(ownerA);
            if (ownerB != null) Object.Destroy(ownerB);
            if (clockSaved) Time.captureDeltaTime = previousCaptureDelta;
            phase = Phase.Finishing;
            deadline = EditorApplication.timeSinceStartup + 20d;
            Save();
            EditorApplication.isPlaying = false;
        }
        private void Exit()
        {
            RestoreTimeScale();
            if (clockSaved) Time.captureDeltaTime = previousCaptureDelta;
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                report.errors.Add("Timed out leaving Play Mode.");
            report.finishedUtc = DateTime.UtcNow.ToString("O");
            report.passed = report.errors.Count == 0;
            Save();
            running = false;
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            int code = report.passed ? 0 : 1;
            Debug.Log("Waste Robot transition smoke " + (report.passed ? "PASS" : "FAIL")
                + ": " + report.checks.Count + " checks; " + Path.Combine(directory, "transition-smoke.json"));
            Object.DestroyImmediate(this);
            EditorApplication.Exit(code);
        }
        private static int RequestedFps()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (args[i].Equals("-wasteRobotSmokeFps", StringComparison.OrdinalIgnoreCase))
                {
                    int fps = 0;
                    Require(i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out fps) && (fps == 15 || fps == 30 || fps == 60),
                        "-wasteRobotSmokeFps requires 15, 30 or 60.");
                    return fps;
                }
            return 0;
        }
        private static string OutputDirectory()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i].Equals("-logFile", StringComparison.OrdinalIgnoreCase) && args[i + 1] != "-")
                    return Path.GetDirectoryName(Path.GetFullPath(args[i + 1]));
            return Path.GetFullPath(Path.Combine("output", "validation", "waste-robot-transition"));
        }
        private static void Require(bool success, string message)
        { if (!success) throw new InvalidOperationException("Waste Robot transition smoke: " + message); }
    }
}
