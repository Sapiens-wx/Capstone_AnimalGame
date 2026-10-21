using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AnimalGame.Animals;
using AnimalGame.RobotMap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    /// <summary>Lens handling checks; batch input checks run only in a disposable project copy.</summary>
    public sealed class PhotoCameraHandlingValidation : ScriptableObject
    {
        private const string SourceProject = "C:/Users/30791/Documents/GitHub/Capstone_AnimalGame";
        private const string DamagedPath = "Assets/Data/AnimalPhoto/Rendering/HalflyBrokenPrimeCamera50.asset";
        private const string ZoomPath = "Assets/Data/AnimalPhoto/Rendering/InstantCamera.asset";
        private const string UiPath = "Assets/Prefabs/Resources/UI/MainUI.prefab";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [Serializable] private sealed class Report
        {
            public bool passed;
            public string startedUtc, finishedUtc, project;
            public List<string> checks = new List<string>();
            public List<string> errors = new List<string>();
            public List<string> metrics = new List<string>();
        }

        private enum Phase
        {
            WaitingForPlay, Neutral, Entering, PrimeReady, PrimeShortHold, PrimeCancelled,
            PrimeHold, PrimeFlash, PrimeReviewHeld, PrimeReturnedHeld, PrimeReleased,
            ZoomAim, ZoomReady, ZoomFocus, ZoomFocusedHeld, ZoomRelease, ZoomShutter, Finishing
        }

        [SerializeField] private bool running;
        [SerializeField] private Report report;
        [SerializeField] private string outputDirectory;
        [SerializeField] private Phase phase;
        [SerializeField] private double deadline;
        [SerializeField] private float phaseStarted;
        [SerializeField] private int lastFrame = -1, phaseFrame, gamepadId, captured;
        [SerializeField] private bool rbHeld;
        [SerializeField] private Vector2 stick;
        [SerializeField] private Vector2 initialAim;
        [SerializeField] private PhotoModeController controller;
        [SerializeField] private InputSettings originalInput, testInput;
        [SerializeField] private bool originalRunInBackground;

        [MenuItem("Animal Game/Validation/Run Photo Camera Handling Checks")]
        public static void Run()
        {
            var result = new Report();
            RunDeterministicChecks(result);
            if (result.errors.Count > 0) throw new InvalidOperationException(string.Join("\n", result.errors));
            Debug.Log("Photo camera handling checks PASS: " + result.checks.Count + " deterministic groups.");
        }

        public static void RunBatch()
        {
            PhotoCameraHandlingValidation session = null;
            try
            {
                Require(Application.isBatchMode, "The input smoke is batch-only.");
                string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/').TrimEnd('/');
                Require(!project.Equals(SourceProject, StringComparison.OrdinalIgnoreCase), "Use a disposable project copy, not the source project.");
                Require(!Environment.GetCommandLineArgs().Contains("-quit"), "Do not pass -quit to the asynchronous input smoke.");
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Start in Edit Mode.");
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    Require(!SceneManager.GetSceneAt(i).isDirty, "Refusing to replace an unsaved scene.");
                session = CreateInstance<PhotoCameraHandlingValidation>();
                session.hideFlags = HideFlags.HideAndDontSave;
                session.outputDirectory = OutputDirectory();
                Directory.CreateDirectory(session.outputDirectory);
                session.report = new Report { startedUtc = DateTime.UtcNow.ToString("O"), project = project };
                RunDeterministicChecks(session.report);
                if (session.report.errors.Count > 0) { session.FinishBatch(); return; }
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                session.running = true;
                session.phase = Phase.WaitingForPlay;
                session.deadline = EditorApplication.timeSinceStartup + 90;
                session.Subscribe();
                EditorApplication.isPlaying = true;
            }
            catch (Exception error)
            {
                if (session != null && session.report != null)
                { session.report.errors.Add(error.ToString()); session.FinishBatch(); }
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        private static void RunDeterministicChecks(Report result)
        {
            Check(result, "Authored damaged and zoom cameras have separate handling profiles", () =>
            {
                PhotoCameraDefinition prime = Load(DamagedPath), zoom = Load(ZoomPath);
                Require(prime.Handling == PhotoCameraHandling.FixedPrimeHold && Near(prime.AutomaticHoldSeconds, .3f), "Damaged prime must use a 0.3 second automatic shutter.");
                Require(Near(prime.FixedFrameAspect, 2f) && Near(prime.FixedFrameWidthOfRange, .9f), "Fixed prime frame should be wide and substantially fill its range.");
                Require(zoom.Handling == PhotoCameraHandling.ZoomFocus, "Zoom lens must retain the original focus handling.");
                using (var f = new Fixture(null)) Require(!f.Controller.UsesFixedPrimeHandling, "A camera without a profile no longer uses original zoom handling.");
            });
            Check(result, "Prime short hold cancels; 0.29 seconds cannot fire; 0.3 seconds starts capture", () =>
            {
                using (var f = new Fixture(Load(DamagedPath)))
                {
                    var c = f.Controller;
                    Step(c, true, true, 0);
                    Step(c, true, false, .29f);
                    Require(c.IsFocusing && !c.IsCapturing && c.FocusProgress01 < 1, "Prime fired before 0.3 seconds.");
                    Step(c, false, false, 0);
                    Require(!c.IsFocusing && !c.IsCapturing && Near(c.FocusProgress01, 0), "Releasing before the threshold did not cancel.");
                    Step(c, true, true, 0);
                    Step(c, true, false, .29f);
                    Require(!c.IsCapturing, "A cancelled hold leaked elapsed time into the next hold.");
                    Step(c, true, false, .01001f);
                    Require(c.IsCapturing && !c.IsFocused, "A continuous 0.3 second hold requires an unwanted second press.");
                    Require(c.LastCaptureSettings.CameraId == Load(DamagedPath).CameraId, "Automatic shutter did not capture the damaged camera settings.");
                }
            });
            Check(result, "Prime emits one photograph and held input cannot repeat after review", () =>
            {
                using (var f = new Fixture(Load(DamagedPath)))
                {
                    int count = 0;
                    var c = f.Controller;
                    c.PhotoCaptured += () => { count++; Require(c.RequestPhotoReview(), "Shutter could not enter photo review."); };
                    Step(c, true, true, 0); Step(c, true, false, .3f);
                    Step(c, true, false, .3f);
                    Require(count == 1 && c.IsReviewing, "Prime capture/review fired a duplicate or missed event.");
                    Step(c, true, false, 1);
                    c.EndPhotoReview();
                    for (int i = 0; i < 5; i++) Step(c, true, true, 1);
                    Require(count == 1 && !c.IsCapturing && !c.IsFocusing, "Returning from review while RB remains held repeated the shutter.");
                    Step(c, false, false, 0); Step(c, true, true, 0); Step(c, true, false, .3f);
                    Require(c.IsCapturing, "Releasing then holding RB failed to re-arm the prime.");
                }
            });
            Check(result, "Prime fixed aim follows the player heading without focus travel", () =>
            {
                using (var f = new Fixture(Load(DamagedPath)))
                {
                    var c = f.Controller;
                    float minimum = (float)Get(c, "minimumAimDistance"), maximum = (float)Get(c, "maximumAimDistance");
                    Vector2 expected = new Vector2(0, Mathf.Lerp(minimum, maximum, Load(DamagedPath).FixedAimDistance01));
                    Require(Vector2.Distance(c.AimLocalPosition, expected) < .0001f, "Fixed aim is not at its authored forward position.");
                    f.Owner.transform.position = new Vector3(2, -1, 0);
                    f.Owner.transform.rotation = Quaternion.Euler(0, 0, 35);
                    Vector3 world = f.Owner.transform.position + f.Owner.transform.up * expected.y;
                    Require(Vector3.Distance(c.AimWorldPosition, world) < .0001f, "The fixed frame no longer follows the player's facing.");
                    Step(c, true, true, 0); Step(c, true, false, .15f); Call(c, "UpdateFocusPresentation");
                    Require(Near(c.FocusPresentation01, 0), "A damaged prime incorrectly performs zoom-focus travel.");
                }
            });
            Check(result, "Switching back restores original aim and focus / release / second-press shutter", () =>
            {
                using (var f = new Fixture(Load(DamagedPath)))
                {
                    var c = f.Controller;
                    Step(c, true, true, 0); Step(c, true, false, .15f);
                    c.SetCamera(Load(ZoomPath));
                    Require(!c.UsesFixedPrimeHandling && !c.IsFocusing && Near(c.AimLocalPosition.y, (float)Get(c, "minimumAimDistance")), "Changing lens retained prime framing or a partial hold.");
                    Step(c, true, true, 0); Step(c, true, false, .3f);
                    Require(c.IsFocusing && !c.IsCapturing, "Zoom lens incorrectly kept the prime's automatic 0.3 second shutter.");
                    Step(c, true, false, .45001f);
                    Require(c.IsFocused && !c.IsCapturing, "Zoom lens did not complete its original 0.75 second focus.");
                    Step(c, true, false, 1);
                    Require(c.IsFocused && !c.IsCapturing, "Zoom lens fired without release and a second press.");
                    Step(c, false, false, 0); Step(c, true, true, 0);
                    Require(c.IsCapturing && c.LastCaptureSettings.CameraId == Load(ZoomPath).CameraId, "Zoom shutter did not resume after a release and second press.");
                }
            });
            Check(result, "Changing equipment during exposure preserves current photo geometry until review ends", () =>
            {
                using (var f = new Fixture(Load(DamagedPath)))
                {
                    var c = f.Controller;
                    c.PhotoCaptured += () => c.RequestPhotoReview();
                    Step(c, true, true, 0); Step(c, true, false, .3f);
                    PhotoCaptureSettings captured = c.LastCaptureSettings;
                    Vector2 aim = c.AimLocalPosition;
                    c.SetCamera(Load(ZoomPath));
                    Require(c.UsesFixedPrimeHandling && c.AimLocalPosition == aim && ReferenceEquals(c.LastCaptureSettings, captured), "Changing a model mid-exposure altered the current photo.");
                    Step(c, true, false, .3f);
                    Require(c.IsReviewing && c.UsesFixedPrimeHandling, "Review lost its original fixed-prime framing.");
                    c.EndPhotoReview();
                    Require(!c.UsesFixedPrimeHandling && Near(c.AimLocalPosition.y, (float)Get(c, "minimumAimDistance")), "Pending zoom handling did not apply when review ended.");
                }
            });
            Check(result, "Fixed rectangular UI capture bounds match the wide frame and restore with zoom", () =>
            {
                using (var f = new Fixture(Load(DamagedPath), true))
                {
                    f.Layout();
                    var corners = new Vector2[4];
                    Require(f.Ui.TryGetCaptureFrameScreenCorners(corners), "The fixed frame cannot be sampled for photo capture.");
                    float width = Vector2.Distance(corners[0], corners[3]);
                    float height = Vector2.Distance(corners[0], corners[1]);
                    Require(width > 1 && height > 1 && Mathf.Abs(width / height - 2f) < .02f, "The displayed and captured fixed frame is not a 2:1 rectangle.");
                    f.Controller.GetFixedFrameWorldPoints(out Vector3 left, out Vector3 right);
                    float fixedWidth = Vector2.Distance(f.Camera.WorldToScreenPoint(left), f.Camera.WorldToScreenPoint(right));
                    Require(Mathf.Abs(width - fixedWidth) < 2f, "The fixed frame changed size when its surrounding guide area was expanded.");
                    f.Controller.GetGuideWorldPoints(out Vector3 leftStart, out Vector3 leftEnd, out Vector3 rightStart, out Vector3 rightEnd);
                    var guidePolygon = new Vector2[] { f.Camera.WorldToScreenPoint(leftStart), f.Camera.WorldToScreenPoint(leftEnd),
                        f.Camera.WorldToScreenPoint(rightEnd), f.Camera.WorldToScreenPoint(rightStart) };
                    for (int i = 0; i < 4; i++)
                        Require(InsideConvexPolygon(corners[i], guidePolygon, 1.1f), "Fixed frame corner " + i + " falls outside the undimmed photo guide area.");
                    Require(((Graphic)Get(f.Ui, "fixedFrameGraphic")).enabled && !((Image)Get(f.Ui, "frameImage")).enabled
                        && !((RectTransform)Get(f.Ui, "aimRoot")).gameObject.activeSelf,
                        "Prime UI retained the old movable aim artwork instead of the fixed rectangle and central cross.");
                    Vector2 expectedCenter = f.Camera.WorldToScreenPoint(f.Controller.AimWorldPosition);
                    Require(Vector2.Distance((corners[0] + corners[2]) * .5f, expectedCenter) < 2, "Photo crop is offset from the visible prime frame.");
                    Vector2[] before = (Vector2[])corners.Clone();
                    Step(f.Controller, true, true, 0); Step(f.Controller, true, false, .15f); Call(f.Controller, "UpdateFocusPresentation"); f.Layout();
                    f.Ui.TryGetCaptureFrameScreenCorners(corners);
                    for (int i = 0; i < 4; i++) Require(Vector2.Distance(corners[i], before[i]) < .1f, "Holding a fixed prime changed its capture bounds.");
                    f.Controller.SetCamera(Load(ZoomPath)); f.Layout();
                    f.Ui.TryGetCaptureFrameScreenCorners(corners);
                    float zoomWidth = Vector2.Distance(corners[0], corners[3]), zoomHeight = Vector2.Distance(corners[0], corners[1]);
                    Require(Mathf.Abs(zoomWidth / zoomHeight - 1920f / 1080f) < .02f && zoomWidth < width, "Switching to zoom did not restore its original smaller frame.");
                    Require(!((Graphic)Get(f.Ui, "fixedFrameGraphic")).enabled && ((Image)Get(f.Ui, "frameImage")).enabled
                        && ((RectTransform)Get(f.Ui, "aimRoot")).gameObject.activeSelf,
                        "Switching to zoom did not restore its original aim artwork.");
                }
            });
        }

        private void OnEnable() { if (running) Subscribe(); }
        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            InputSystem.onBeforeUpdate -= QueueHeldInput;
        }
        private void Subscribe()
        {
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            InputSystem.onBeforeUpdate -= QueueHeldInput; InputSystem.onBeforeUpdate += QueueHeldInput;
        }

        private void Tick()
        {
            if (!running) return;
            try
            {
                if (phase == Phase.Finishing)
                {
                    if (!EditorApplication.isPlayingOrWillChangePlaymode) FinishBatch();
                    return;
                }
                Require(EditorApplication.timeSinceStartup < deadline, "Input smoke timed out during " + phase);
                EditorApplication.QueuePlayerLoopUpdate();
                if (!EditorApplication.isPlaying) return;
                if (lastFrame == Time.frameCount) return;
                lastFrame = Time.frameCount;
                switch (phase)
                {
                    case Phase.WaitingForPlay:
                        originalInput = InputSystem.settings;
                        testInput = Instantiate(originalInput);
                        testInput.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                        testInput.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                        InputSystem.settings = testInput;
                        originalRunInBackground = Application.runInBackground;
                        Application.runInBackground = true;
                        gamepadId = InputSystem.AddDevice<Gamepad>("Photo camera handling validation").deviceId;
                        GameObject owner = new GameObject("Lens handling input fixture");
                        new GameObject("Photo Aim Camera Target").transform.SetParent(owner.transform, false);
                        controller = owner.AddComponent<PhotoModeController>();
                        controller.GetComponent<RobotMover>().enabled = false;
                        controller.SetCamera(Load(DamagedPath));
                        Set(controller, "entryDuration", .05f);
                        controller.PhotoCaptured += OnCaptured;
                        Begin(Phase.Neutral); break;
                    case Phase.Neutral:
                        if (!Settled(3)) break;
                        controller.SetPhotoModeActive(true); Begin(Phase.Entering); break;
                    case Phase.Entering:
                        if (controller.IsEntering) break;
                        Require(controller.IsActive && !controller.IsFocusing, "Prime did not enter a ready framing state.");
                        initialAim = controller.AimLocalPosition; stick = new Vector2(1, 1); Begin(Phase.PrimeReady); break;
                    case Phase.PrimeReady:
                        Require(Vector2.Distance(controller.AimLocalPosition, initialAim) < .0001f, "Right stick moved fixed prime aim before holding.");
                        if (!Settled(4)) break;
                        rbHeld = true; Begin(Phase.PrimeShortHold); break;
                    case Phase.PrimeShortHold:
                        Require(captured == 0 && !controller.IsCapturing, "Prime fired during a short real RB hold.");
                        if (Elapsed < .12f) break;
                        rbHeld = false; Begin(Phase.PrimeCancelled); break;
                    case Phase.PrimeCancelled:
                        if (!Settled(3)) break;
                        Require(!controller.IsFocusing && captured == 0, "Releasing real RB did not cancel a short hold.");
                        rbHeld = true; Begin(Phase.PrimeHold); break;
                    case Phase.PrimeHold:
                        Require(Vector2.Distance(controller.AimLocalPosition, initialAim) < .0001f, "Right stick moved prime aim during the hold.");
                        if (!controller.IsCapturing) { Require(Elapsed < .7f, "Holding real RB did not automatically start a prime capture."); break; }
                        Require(Elapsed >= .27f, "Real input started capture before the intended 0.3 second threshold.");
                        report.metrics.Add("Real RB hold to prime capture: " + Elapsed.ToString("F4") + " s");
                        Begin(Phase.PrimeFlash); break;
                    case Phase.PrimeFlash:
                        if (!controller.IsReviewing) break;
                        Require(captured == 1, "Real RB hold did not produce exactly one photo.");
                        report.checks.Add("Actual Input System RB hold automatically captures after 0.3 seconds; short release cancels and the right stick cannot move prime aim.");
                        Begin(Phase.PrimeReviewHeld); break;
                    case Phase.PrimeReviewHeld:
                        if (Elapsed < .4f) break;
                        Require(captured == 1, "A held prime shutter repeated during review.");
                        controller.EndPhotoReview(); Begin(Phase.PrimeReturnedHeld); break;
                    case Phase.PrimeReturnedHeld:
                        if (Elapsed < .5f) break;
                        Require(captured == 1 && !controller.IsFocusing && !controller.IsCapturing, "RB held after review triggered another capture.");
                        report.checks.Add("Holding RB through review and return to framing never repeats the shutter.");
                        rbHeld = false; stick = Vector2.zero; Begin(Phase.PrimeReleased); break;
                    case Phase.PrimeReleased:
                        if (!Settled(3)) break;
                        controller.SetCamera(Load(ZoomPath));
                        Require(!controller.UsesFixedPrimeHandling, "Changing equipment did not restore zoom handling.");
                        initialAim = controller.AimLocalPosition; stick = new Vector2(.6f, .6f); Begin(Phase.ZoomAim); break;
                    case Phase.ZoomAim:
                        if (Elapsed < .15f) break;
                        Require(Vector2.Distance(controller.AimLocalPosition, initialAim) > .02f, "Zoom lens did not regain right-stick aiming.");
                        stick = Vector2.zero; Begin(Phase.ZoomReady); break;
                    case Phase.ZoomReady:
                        if (!Settled(3)) break;
                        rbHeld = true; Begin(Phase.ZoomFocus); break;
                    case Phase.ZoomFocus:
                        Require(captured == 1 && !controller.IsCapturing, "Zoom lens retained the prime automatic shutter.");
                        if (!controller.IsFocused) { Require(Elapsed < 1.4f, "Zoom focus did not complete."); break; }
                        Require(Elapsed >= .70f, "Zoom focus retained the damaged prime's short hold.");
                        report.metrics.Add("Restored zoom focus duration: " + Elapsed.ToString("F4") + " s");
                        Begin(Phase.ZoomFocusedHeld); break;
                    case Phase.ZoomFocusedHeld:
                        if (Elapsed < .35f) break;
                        Require(controller.IsFocused && captured == 1, "Zoom shutter fired before release and second press.");
                        rbHeld = false; Begin(Phase.ZoomRelease); break;
                    case Phase.ZoomRelease:
                        if (!Settled(3)) break;
                        rbHeld = true; Begin(Phase.ZoomShutter); break;
                    case Phase.ZoomShutter:
                        if (!controller.IsReviewing) break;
                        Require(captured == 2 && controller.LastCaptureSettings.CameraId == Load(ZoomPath).CameraId, "Restored zoom shutter captured the wrong model.");
                        report.checks.Add("Equipping zoom lens restores actual right-stick aiming, 0.75 second focus and release / second-press shutter input.");
                        BeginFinish(); break;
                }
            }
            catch (Exception error) { report.errors.Add(phase + ": " + error); BeginFinish(); }
        }

        private void OnCaptured() { captured++; controller.RequestPhotoReview(); }
        private float Elapsed => Time.unscaledTime - phaseStarted;
        private bool Settled(int frames) => Time.frameCount - phaseFrame >= frames;
        private void Begin(Phase next) { phase = next; phaseStarted = Time.unscaledTime; phaseFrame = Time.frameCount; }
        private void QueueHeldInput()
        {
            if (!running || gamepadId == 0) return;
            Gamepad pad = InputSystem.GetDeviceById(gamepadId) as Gamepad;
            if (pad == null) return;
            var state = new GamepadState { rightStick = stick };
            if (rbHeld) state = state.WithButton(GamepadButton.RightShoulder);
            InputSystem.QueueStateEvent(pad, state);
        }
        private void BeginFinish()
        {
            if (phase == Phase.Finishing) return;
            InputSystem.onBeforeUpdate -= QueueHeldInput;
            if (controller != null) { controller.PhotoCaptured -= OnCaptured; controller.SetPhotoModeActive(false); }
            var pad = InputSystem.GetDeviceById(gamepadId);
            if (pad != null) InputSystem.RemoveDevice(pad);
            if (originalInput != null) InputSystem.settings = originalInput;
            if (testInput != null) DestroyImmediate(testInput);
            Application.runInBackground = originalRunInBackground;
            phase = Phase.Finishing;
            EditorApplication.isPlaying = false;
        }
        private void FinishBatch()
        {
            running = false;
            EditorApplication.update -= Tick;
            InputSystem.onBeforeUpdate -= QueueHeldInput;
            report.finishedUtc = DateTime.UtcNow.ToString("O");
            report.passed = report.errors.Count == 0;
            File.WriteAllText(Path.Combine(outputDirectory, "camera-handling-validation.json"), JsonUtility.ToJson(report, true));
            Debug.Log("Camera handling validation " + (report.passed ? "PASS" : "FAIL") + ": " + report.checks.Count + " groups.");
            EditorApplication.Exit(report.passed ? 0 : 1);
        }

        private static void Step(PhotoModeController c, bool held, bool pressed, float deltaTime) => Call(c, "AdvanceCaptureState", held, pressed, deltaTime);
        private static PhotoCameraDefinition Load(string path)
        {
            var value = AssetDatabase.LoadAssetAtPath<PhotoCameraDefinition>(path);
            Require(value != null, "Missing camera asset: " + path); return value;
        }
        private static void Check(Report report, string label, Action action)
        { try { action(); report.checks.Add(label); } catch (Exception error) { report.errors.Add(label + ": " + error); } }
        private static bool Near(float a, float b) => Mathf.Abs(a - b) < .0001f;
        private static bool InsideConvexPolygon(Vector2 point, Vector2[] polygon, float tolerancePixels)
        {
            Vector2 center = Vector2.zero;
            foreach (Vector2 corner in polygon) center += corner;
            center /= polygon.Length;
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 origin = polygon[i], edge = polygon[(i + 1) % polygon.Length] - origin;
                if (edge.sqrMagnitude < .0001f) continue;
                float centerSide = Cross(edge, center - origin);
                float pointSide = Cross(edge, point - origin) / edge.magnitude;
                if (pointSide * Mathf.Sign(centerSide) < -tolerancePixels) return false;
            }
            return true;
        }
        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private static object Get(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        private static object Call(object target, string method, params object[] args)
        {
            try { return target.GetType().GetMethod(method, Private).Invoke(target, args); }
            catch (TargetInvocationException error) when (error.InnerException != null) { throw error.InnerException; }
        }
        private static void SetEnum(object target, string field, string name)
        { FieldInfo info = target.GetType().GetField(field, Private); info.SetValue(target, Enum.Parse(info.FieldType, name)); }
        private static string OutputDirectory()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i].Equals("-logFile", StringComparison.OrdinalIgnoreCase) && args[i + 1] != "-") return Path.GetDirectoryName(Path.GetFullPath(args[i + 1]));
            return Path.GetFullPath("output/validation/photo-camera-handling");
        }

        private sealed class Fixture : IDisposable
        {
            public readonly Scene Scene;
            public readonly GameObject Owner;
            public readonly PhotoModeController Controller;
            public readonly PhotoModeUI Ui;
            public readonly Camera Camera;
            private readonly RenderTexture target;

            public Fixture(PhotoCameraDefinition profile, bool withUi = false)
            {
                Scene = EditorSceneManager.NewPreviewScene();
                Owner = Create("Camera handling fixture");
                new GameObject("Photo Aim Camera Target").transform.SetParent(Owner.transform, false);
                Controller = Owner.AddComponent<PhotoModeController>();
                Call(Controller, "Awake"); Controller.SetCamera(profile);
                SetEnum(Controller, "state", "Active");
                if (!withUi) return;
                Camera = Create("Camera handling UI camera").AddComponent<Camera>();
                Camera.transform.position = new Vector3(0, 3, -10);
                Camera.orthographic = true; Camera.orthographicSize = 5; Camera.aspect = 16f / 9f;
                target = new RenderTexture(1920, 1080, 24); target.Create(); Camera.targetTexture = target;
                GameObject canvasObject = Create("Camera handling UI canvas", typeof(RectTransform), typeof(Canvas));
                Canvas canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                GameObject uiObject = Create("Camera handling UI", typeof(RectTransform)); uiObject.transform.SetParent(canvasObject.transform, false);
                var uiRect = uiObject.GetComponent<RectTransform>(); uiRect.anchorMin = Vector2.zero; uiRect.anchorMax = Vector2.one; uiRect.sizeDelta = Vector2.zero;
                Ui = uiObject.AddComponent<PhotoModeUI>();
                PhotoModeUI authored = AssetDatabase.LoadAssetAtPath<GameObject>(UiPath).GetComponentInChildren<PhotoModeUI>(true);
                Require(authored != null, "The authored photo UI profile is missing.");
                EditorUtility.CopySerialized(authored, Ui);
                Call(Ui, "Awake"); Ui.Initialize(Controller, Camera);
                foreach (string field in new[] { "playerRangeCanvasRoot", "shutterFlashCanvasRoot" })
                {
                    var root = Get(Ui, field) as RectTransform;
                    if (root != null && root.parent == null) SceneManager.MoveGameObjectToScene(root.gameObject, Scene);
                }
            }
            private GameObject Create(string name, params Type[] components)
            { var go = new GameObject(name, components); SceneManager.MoveGameObjectToScene(go, Scene); return go; }
            public void Layout() { Canvas.ForceUpdateCanvases(); Call(Ui, "LateUpdate"); Canvas.ForceUpdateCanvases(); }
            public void Dispose()
            {
                if (Camera != null) Camera.targetTexture = null;
                EditorSceneManager.ClosePreviewScene(Scene);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            }
        }
    }
}
