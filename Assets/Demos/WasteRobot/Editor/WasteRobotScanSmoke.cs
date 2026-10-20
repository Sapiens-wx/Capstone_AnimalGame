using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AnimalGame.Demos.WasteRobot.Editor
{
    /// <summary>
    /// Explicit opt-in Play Mode smoke in a disposable batch project. Invoke
    /// RunBatch without -quit or -nographics; no automatic startup hooks exist.
    /// Optional -wasteRobotSmokeFps 15|30|60 supplies deterministic long frames.
    /// Real scan-wave detection and the production overlay presenter are used;
    /// this fixture deliberately does not emulate the scan charge input gesture.
    /// </summary>
    public sealed class WasteRobotScanSmoke : ScriptableObject
    {
        private enum Phase
        {
            Waiting, Warmup, FirstReveal, Rescan, Enter, Inside, Exit, ReverseIn,
            ReverseOut, ReverseFinish, ExpireStart, ExpireEnter, ExpireInside,
            ExpireExit, DisableStart, Disabled, Reenabled, RootDisabled,
            RootEnabled, Destroyed, Finishing
        }

        [Serializable]
        private sealed class Report
        {
            public string startedUtc, finishedUtc;
            public bool passed;
            public int simulatedFramesPerSecond, suspendedFrames, goldPixels;
            public int hatchMeshVertices, outlineMeshVertices;
            public float beforeEntryRemaining, afterExitRemaining;
            public List<string> checks = new List<string>();
            public List<string> errors = new List<string>();
            public List<string> notes = new List<string>();
            public List<string> screenshots = new List<string>();
        }

        [SerializeField] private bool running;
        [SerializeField] private Phase phase;
        [SerializeField] private Report report;
        [SerializeField] private string directory;
        [SerializeField] private int requestedFps, phaseFrame, lastFrame = -1;
        [SerializeField] private double deadline, phaseStart, lastFrameWallTime;
        [SerializeField] private float phaseSimulationStart, previousCaptureDelta;
        [SerializeField] private bool clockSaved;
        [SerializeField] private WasteRobotBase machine;
        [SerializeField] private WasteRobotTransitionEffects effects;
        [SerializeField] private WasteRobotScan scan;
        [SerializeField] private ScannableInteractable target;
        [SerializeField] private Camera camera;
        [SerializeField] private RobotMover player;
        [SerializeField] private RobotCameraShake shake;
        [SerializeField] private BioScanController controller;
        [SerializeField] private BioScanStaticAreaUI overlay;
        [SerializeField] private bool originalShakeEnabled, shakeSaved;
        [SerializeField] private float outsideSize, initialRemaining, initialElapsed, originalDuration;
        [SerializeField] private float entryRemaining, entryElapsed, entryAngle;
        [SerializeField] private uint entrySeed;
        [SerializeField] private int destroyedId;
        private RenderTexture cameraTarget, overlayTarget;
        private bool drewOverlay;

        public static void RunBatch()
        {
            try
            {
                Require(Application.isBatchMode && !EditorApplication.isPlayingOrWillChangePlaymode,
                    "Run in a clean isolated batch project's Edit Mode.");
                Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null,
                    "The scan pixel proof needs a graphics device; omit -nographics.");
                Require(!Resources.FindObjectsOfTypeAll<WasteRobotScanSmoke>().Any(value => value.running),
                    "A scan smoke session is already running.");
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    Require(!SceneManager.GetSceneAt(i).isDirty, "Do not replace unsaved scenes.");
                Require(AssetDatabase.LoadAssetAtPath<SceneAsset>(WasteRobotDemoBuilder.ScenePath) != null,
                    "Build the independent Demo before running its scan smoke.");
                EditorSceneManager.OpenScene(WasteRobotDemoBuilder.ScenePath, OpenSceneMode.Single);
                var session = CreateInstance<WasteRobotScanSmoke>();
                session.hideFlags = HideFlags.HideAndDontSave;
                session.directory = OutputDirectory();
                Directory.CreateDirectory(session.directory);
                session.requestedFps = RequestedFps();
                session.report = new Report
                {
                    startedUtc = DateTime.UtcNow.ToString("O"),
                    simulatedFramesPerSecond = session.requestedFps
                };
                session.report.notes.Add("Real BioScanController.RevealTargetsCrossedByWave handles fixture annuli at the remote arm and wheel. The original disabled Demo controller is initialized for its production static-area UI, without scan-charge input emulation.");
                session.report.notes.Add("Screenshots render the original camera and actual ScreenSpaceOverlay CanvasRenderer list; no surrogate UI mesh is drawn. The overlay evidence is one GPU copy of the real world-plus-overlay result, preserving native alpha blending. Temporary render targets retain the runtime camera's position, rotation, size and pixel dimensions.");
                session.running = true;
                session.phase = Phase.Waiting;
                session.phaseStart = EditorApplication.timeSinceStartup;
                session.deadline = session.phaseStart + 180d;
                session.Subscribe();
                session.Save();
                EditorApplication.isPlaying = true;
            }
            catch (Exception error)
            {
                Debug.LogException(error);
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
                Require(EditorApplication.timeSinceStartup <= deadline, "Exceeded the 180 second deadline.");
                EditorApplication.QueuePlayerLoopUpdate();
                if (!EditorApplication.isPlaying)
                {
                    Require(phase == Phase.Waiting || EditorApplication.isPlayingOrWillChangePlaymode,
                        "Play Mode ended before the scan checks completed.");
                    return;
                }
                if (lastFrame == Time.frameCount) return;
                lastFrame = Time.frameCount;
                ThrottleRequestedFrameRate();
                Require(EditorApplication.timeSinceStartup - phaseStart < 30d, "Timed out in phase " + phase);
                AssertSuspendedFrame();

                switch (phase)
                {
                    case Phase.Waiting:
                        InitializePlay();
                        break;
                    case Phase.Warmup:
                        if (Frames(8))
                        {
                            outsideSize = shake.BaseOrthographicSize;
                            Record(!machine.IsInside && target.CanScan && !scan.IsSuspended,
                                "The exterior registers one available whole-vehicle scan target.");
                            Record(machine.GetComponentsInChildren<ScannableInteractable>(true).Length == 1
                                && target.gameObject == machine.gameObject,
                                "The vehicle, wheels and arms share exactly one root scan target.");
                            Record(machine.GetComponentsInChildren<WorldInteraction>(true).All(value =>
                                (value.Kind & (WorldInteractionKind.Grabbable | WorldInteractionKind.Pushable)) == 0),
                                "Adding scan presentation does not create grabbable or pushable garbage.");
                            WaveAtHullExtreme(false);
                            initialRemaining = target.RevealRemaining;
                            Require(initialRemaining > 0f, "The actual scan wave failed to reveal the vehicle's remote arm.");
                            Record(true, "A real scan-wave annulus reaching the remote arm reveals the whole vehicle before reaching its center.");
                            Begin(Phase.FirstReveal);
                        }
                        break;
                    case Phase.FirstReveal:
                        if (SimulationSeconds(1.05f) && target.HasCapturedScanOrientation)
                        {
                            AssertVisibleMesh();
                            Capture("scan-whole-vehicle", true);
                            initialElapsed = target.RevealElapsed;
                            uint oldSeed = target.OutlinePatternSeed;
                            WaveAtHullExtreme(true);
                            Record(target.RevealRemaining >= initialRemaining - .15f
                                && target.RevealElapsed >= initialElapsed - .001f
                                && target.OutlinePatternSeed != 0u && target.OutlinePatternSeed != oldSeed,
                                "A real wave reaching the wheel refreshes lifetime and seed while preserving the existing reveal animation phase.");
                            Begin(Phase.Rescan);
                        }
                        break;
                    case Phase.Rescan:
                        if (Frames(3) && target.HasCapturedScanOrientation)
                        {
                            SaveEntrySnapshot();
                            SetInside(true);
                            Begin(Phase.Enter);
                        }
                        break;
                    case Phase.Enter:
                        if (Settled())
                        {
                            Record(machine.IsInside && scan.IsSuspended && !target.CanScan,
                                "Entering hides and unregisters the scan throughout the fade and inside view.");
                            Record(Mathf.Abs(camera.orthographicSize - 6.5f) < .065f,
                                "The existing interior transition still settles at camera size 6.5.");
                            Record(GraphicVertices("areaGraphics") == 0 && GraphicVertices("outlineGraphics") == 0,
                                "The production presenter leaves neither hatch nor diamond mesh over the interior.");
                            Capture("scan-interior-hidden", false);
                            Begin(Phase.Inside);
                        }
                        break;
                    case Phase.Inside:
                        if (SimulationSeconds(.4f))
                        {
                            Require(scan.SuspendedRevealRemaining > 0f && scan.SuspendedRevealRemaining < entryRemaining,
                                "Suspended remaining time did not decrease in simulation time.");
                            SetInside(false);
                            Begin(Phase.Exit);
                        }
                        break;
                    case Phase.Exit:
                        if (Settled() && target.CanScan)
                        {
                            report.afterExitRemaining = target.RevealRemaining;
                            Record(!machine.IsInside && !scan.IsSuspended && target.RevealRemaining > 0f
                                && target.RevealRemaining < entryRemaining - .35f,
                                "Returning restores only the still-unexpired duration, without refreshing the original reveal.");
                            Record(target.OutlinePatternSeed == entrySeed && target.HasCapturedScanOrientation
                                && Mathf.Abs(Mathf.DeltaAngle(target.CapturedScanWorldAngleDegrees, entryAngle)) < .001f
                                && target.RevealElapsed > entryElapsed,
                                "Returning preserves the scan's world angle, diamond seed and continuously advancing reveal clock.");
                            Record(Mathf.Abs(camera.orthographicSize - outsideSize) < .065f,
                                "Exiting restores the camera's original exterior size.");
                            AssertVisibleMesh();
                            Capture("scan-returned-remaining", true);
                            SetInside(true);
                            Begin(Phase.ReverseIn);
                        }
                        break;
                    case Phase.ReverseIn:
                        if (effects.FadeAlpha > .05f)
                        {
                            SetInside(false);
                            Begin(Phase.ReverseOut);
                        }
                        break;
                    case Phase.ReverseOut:
                        if (Frames(2))
                        {
                            SetInside(true);
                            SetInside(false);
                            Begin(Phase.ReverseFinish);
                        }
                        break;
                    case Phase.ReverseFinish:
                        if (Settled() && target.CanScan)
                        {
                            Record(!machine.IsInside && !scan.IsSuspended && target.OutlinePatternSeed == entrySeed,
                                "Rapid inward/outward reversals settle on the latest exterior state without replacing the scan seed.");
                            SetDisplayDuration(.5f);
                            WaveAtHullExtreme(false);
                            Require(target.RevealRemaining > 0f && target.RevealRemaining <= .51f,
                                "The expiry fixture did not begin with a short real wave reveal.");
                            Begin(Phase.ExpireStart);
                        }
                        break;
                    case Phase.ExpireStart:
                        if (Frames(2))
                        {
                            SetInside(true);
                            Begin(Phase.ExpireEnter);
                        }
                        break;
                    case Phase.ExpireEnter:
                        if (Settled()) Begin(Phase.ExpireInside);
                        break;
                    case Phase.ExpireInside:
                        if (SimulationSeconds(.8f))
                        {
                            Record(scan.IsSuspended && scan.SuspendedRevealRemaining <= .001f,
                                "A reveal expires normally while hidden inside the vehicle.");
                            SetInside(false);
                            Begin(Phase.ExpireExit);
                        }
                        break;
                    case Phase.ExpireExit:
                        if (Settled() && target.CanScan)
                        {
                            Record(target.RevealRemaining <= .001f && GraphicVertices("areaGraphics") == 0
                                && GraphicVertices("outlineGraphics") == 0,
                                "An expired indoor reveal does not reappear when the exterior returns.");
                            SetDisplayDuration(originalDuration);
                            WaveAtHullExtreme(false);
                            SetInside(true);
                            Begin(Phase.DisableStart);
                        }
                        break;
                    case Phase.DisableStart:
                        if (effects.IsTransitioning && effects.FadeAlpha > .05f)
                        {
                            scan.enabled = false;
                            Begin(Phase.Disabled);
                        }
                        break;
                    case Phase.Disabled:
                        if (Frames(3))
                        {
                            Record(!target.CanScan && !Registered() && target.RevealRemaining <= .001f,
                                "Disabling the local adapter unregisters the target and clears its reveal.");
                            machine.Bind(player.transform, false);
                            scan.enabled = true;
                            scan.RefreshState();
                            Begin(Phase.Reenabled);
                        }
                        break;
                    case Phase.Reenabled:
                        if (Frames(4))
                        {
                            Record(target.CanScan && !scan.IsSuspended && target.RevealRemaining <= .001f,
                                "Re-enabling outside permits future scans without resurrecting the discarded reveal.");
                            WaveAtHullExtreme(true);
                            machine.gameObject.SetActive(false);
                            Begin(Phase.RootDisabled);
                        }
                        break;
                    case Phase.RootDisabled:
                        if (Frames(3))
                        {
                            Record(!target.CanScan && !Registered(),
                                "Disabling the complete vehicle removes its scan registry entry.");
                            machine.gameObject.SetActive(true);
                            Begin(Phase.RootEnabled);
                        }
                        break;
                    case Phase.RootEnabled:
                        if (Frames(5))
                        {
                            Record(target.CanScan && target.RevealRemaining <= .001f,
                                "Reactivating the complete vehicle cannot revive a discarded scan reveal.");
                            destroyedId = target.GetInstanceID();
                            Object.Destroy(machine.gameObject);
                            Begin(Phase.Destroyed);
                        }
                        break;
                    case Phase.Destroyed:
                        if (Frames(4))
                        {
                            Record(!ScannableInteractable.Active.Any(value => value != null
                                && value.GetInstanceID() == destroyedId),
                                "Destroying the vehicle leaves no stale scan registry entry.");
                            Record(GraphicVertices("areaGraphics") == 0 && GraphicVertices("outlineGraphics") == 0,
                                "Destroying the vehicle leaves no hatch or diamond presentation mesh.");
                            Finish(null);
                        }
                        break;
                }
            }
            catch (Exception error) { Finish(error); }
        }

        private void InitializePlay()
        {
            WasteRobotDemoScene bootstrap = Object.FindFirstObjectByType<WasteRobotDemoScene>();
            if (bootstrap == null) return;
            machine = bootstrap.Machine;
            player = bootstrap.Player;
            camera = bootstrap.DemoCamera;
            effects = machine != null ? machine.GetComponent<WasteRobotTransitionEffects>() : null;
            scan = machine != null ? machine.GetComponent<WasteRobotScan>() : null;
            target = scan != null ? scan.ScanTarget : null;
            Require(player != null && camera != null && effects != null && scan != null && target != null,
                "The generated Demo is missing its player, camera, transitions or whole-vehicle scan adapter.");
            originalDuration = target.DisplayDuration;
            controller = player.GetComponent<BioScanController>();
            overlay = player.GetComponent<BioScanStaticAreaUI>();
            Require(controller != null && !controller.enabled && overlay != null,
                "The fixture expects the original Demo player's existing disabled scan controller.");
            controller.Initialize(null, camera);
            overlay.enabled = true;
            scan.RefreshState();
            shake = camera.GetComponent<RobotCameraShake>();
            Require(shake != null && shake.enabled, "The original camera Shake controller is missing.");
            var settings = new SerializedObject(shake);
            SerializedProperty property = settings.FindProperty("enableCameraShake");
            Require(property != null, "The deterministic instance-only shake setting is missing.");
            originalShakeEnabled = property.boolValue;
            shakeSaved = true;
            property.boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
            if (requestedFps > 0)
            {
                previousCaptureDelta = Time.captureDeltaTime;
                clockSaved = true;
                Time.captureDeltaTime = 1f / requestedFps;
            }
            Begin(Phase.Warmup);
        }

        private void WaveAtHullExtreme(bool wheel)
        {
            Require(target.CanScan, "Cannot emit a fixture wave while the vehicle is suspended.");
            var hull = new List<Vector3>();
            Require(target.TryGetWorldOutline(hull) && hull.Count >= 3, "The whole-vehicle outline is unavailable.");
            Vector3 center = Vector3.zero;
            foreach (Vector3 point in hull) center += point;
            center /= hull.Count;
            Vector3 edge = wheel ? hull.OrderByDescending(point => point.y).First()
                : hull.OrderBy(point => point.x).First();
            Vector2 outward = ((Vector2)(edge - center)).normalized;
            Vector2 origin = (Vector2)edge + outward * .7f;
            Require(Vector2.Distance(origin, center) > 1.2f
                && Vector2.Distance(origin, machine.transform.position) > 1.2f,
                "The remote-part fixture must stop its wave before the vehicle center and root.");
            ((HashSet<int>)Get(controller, "revealedEntityIds")).Clear();
            Set(controller, "scanWaveOrigin", origin);
            Call(controller, "RevealTargetsCrossedByWave", .5f, .8f);
            Require(((HashSet<int>)Get(controller, "revealedEntityIds")).Contains(target.GetInstanceID())
                && target.RevealRemaining > 0f, "The production wave function did not hit the whole-vehicle target.");
            report.notes.Add((wheel ? "Wheel" : "Remote arm") + " annulus: origin=" + origin
                + ", radius 0.5 to 0.8 plus production wave thickness; vehicle center distance="
                + Vector2.Distance(origin, center).ToString("F3", CultureInfo.InvariantCulture) + ".");
        }

        private void SaveEntrySnapshot()
        {
            entryRemaining = target.RevealRemaining;
            entryElapsed = target.RevealElapsed;
            entrySeed = target.OutlinePatternSeed;
            entryAngle = target.CapturedScanWorldAngleDegrees;
            report.beforeEntryRemaining = entryRemaining;
            Require(entryRemaining > 3f && entrySeed != 0u && target.HasCapturedScanOrientation,
                "Enter only after the production presenter captured the visible scan's orientation and seed.");
        }
        private void SetInside(bool inside)
        {
            machine.SetInside(inside);
            scan.RefreshState();
            Require(machine.IsInside == inside && machine.InteriorRoot.activeInHierarchy == inside
                && machine.ExteriorRoot.activeInHierarchy != inside,
                "The scan adapter changed authoritative exterior/interior collision root selection.");
        }
        private void AssertSuspendedFrame()
        {
            if (scan == null || machine == null || target == null || effects == null) return;
            if (machine.IsInside || effects.IsTransitioning || !scan.enabled || !machine.gameObject.activeInHierarchy)
            {
                Require(!target.CanScan && !Registered(),
                    "A frame of the interior, fade or disabled vehicle left its scan target active.");
                report.suspendedFrames++;
            }
        }
        private bool Registered() => target != null && ScannableInteractable.Active.Contains(target);
        private void SetDisplayDuration(float duration)
        {
            var settings = new SerializedObject(target);
            settings.FindProperty("displayDuration").floatValue = duration;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }
        private int GraphicVertices(string field)
        {
            if (overlay == null) return 0;
            var graphics = (IEnumerable<BioScanStaticAreaGraphic>)Get(overlay, field);
            return graphics.Sum(value => value != null ? value.MeshVertexCount : 0);
        }
        private void AssertVisibleMesh()
        {
            int hatch = GraphicVertices("areaGraphics"), dots = GraphicVertices("outlineGraphics");
            Require(hatch > 40 && dots > 40, "The production UI did not produce both hatch and diamond geometry.");
            report.hatchMeshVertices = Mathf.Max(report.hatchMeshVertices, hatch);
            report.outlineMeshVertices = Mathf.Max(report.outlineMeshVertices, dots);
            Record(target.ShowOutlineDots && target.AreaColor.r > target.AreaColor.b
                && target.AreaColor.g > target.AreaColor.b,
                "The unmodified production static-area UI draws the vehicle's gold hatch and seeded diamond boundary.");
        }

        private void Capture(string name, bool expectGold)
        {
            Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null,
                "Actual scan screenshots require a graphics device.");
            Require(GraphicsSettings.currentRenderPipeline != null,
                "The actual overlay capture follows this project's scripted render pipeline.");
            int width = camera.pixelWidth, height = camera.pixelHeight;
            Require(width >= 64 && height >= 64, "The actual camera viewport is too small for a pixel proof.");
            RenderTexture priorTarget = camera.targetTexture, priorActive = RenderTexture.active;
            Vector3 priorPosition = camera.transform.position;
            Quaternion priorRotation = camera.transform.rotation;
            float priorSize = camera.orthographicSize;
            Rect priorPixelRect = camera.pixelRect;
            try
            {
                cameraTarget = NewTarget(width, height, "Scan smoke original world camera");
                overlayTarget = NewTarget(width, height, "Scan smoke actual overlay renderer list");
                camera.targetTexture = cameraTarget;
                camera.pixelRect = new Rect(0f, 0f, width, height);
                Call(overlay, "TickAreas");
                Canvas.ForceUpdateCanvases();
                drewOverlay = false;
                RenderPipelineManager.endCameraRendering += DrawActualOverlay;
                var request = new RenderPipeline.StandardRequest { destination = cameraTarget };
                Require(RenderPipeline.SupportsRenderRequest(camera, request),
                    "The active pipeline does not support the actual camera render request.");
                RenderPipeline.SubmitRenderRequest(camera, request);
                Require(drewOverlay, "The camera render did not provide an actual UI overlay renderer list.");
                Color32[] pixels = SaveTarget(overlayTarget, name + "-overlay.png");
                SaveTarget(cameraTarget, name + ".png");
                int gold = pixels.Count(IsGold);
                report.goldPixels = Mathf.Max(report.goldPixels, gold);
                if (expectGold)
                    Record(gold > 200, "Actual Canvas GPU pixels contain visible gold hatch and boundary: " + name + " (" + gold + " gold pixels).");
                else
                    Record(gold == 0, "Actual Canvas GPU pixels contain no vehicle scan overlay inside: " + name + ".");
                report.notes.Add(name + ": actual unchanged camera position=" + priorPosition
                    + ", size=" + priorSize.ToString("F4", CultureInfo.InvariantCulture)
                    + ", pixels=" + width + "x" + height + ".");
            }
            finally
            {
                RenderPipelineManager.endCameraRendering -= DrawActualOverlay;
                camera.targetTexture = priorTarget;
                camera.pixelRect = priorPixelRect;
                RenderTexture.active = priorActive;
                if (cameraTarget != null) { cameraTarget.Release(); Object.DestroyImmediate(cameraTarget); }
                if (overlayTarget != null) { overlayTarget.Release(); Object.DestroyImmediate(overlayTarget); }
                cameraTarget = overlayTarget = null;
                Canvas.ForceUpdateCanvases();
                Require(Vector3.Distance(camera.transform.position, priorPosition) < .00001f
                    && Quaternion.Angle(camera.transform.rotation, priorRotation) < .0001f
                    && Mathf.Abs(camera.orthographicSize - priorSize) < .00001f,
                    "Capturing scan pixels changed the gameplay camera framing.");
            }
        }
        private void DrawActualOverlay(ScriptableRenderContext context, Camera renderedCamera)
        {
            if (renderedCamera != camera || cameraTarget == null || overlayTarget == null) return;
            // Same renderer list that URP's DrawScreenSpaceUIPass consumes.
            // A UIOverlay renderer list is single-use per frame. Draw it once
            // over the real world, so native SrcAlpha blending remains exact,
            // then GPU-copy that completed result for the gold pixel proof.
            // The Demo world art is monochrome; only the scan contributes gold.
            using (var command = new CommandBuffer { name = "Waste Robot actual static scan overlay" })
            {
                var list = context.CreateUIOverlayRendererList(camera);
                command.SetRenderTarget(cameraTarget);
                command.DrawRendererList(list);
                command.Blit(cameraTarget, overlayTarget);
                context.ExecuteCommandBuffer(command);
            }
            context.Submit();
            drewOverlay = true;
        }
        private Color32[] SaveTarget(RenderTexture targetTexture, string filename)
        {
            RenderTexture previous = RenderTexture.active;
            Texture2D pixels = null;
            try
            {
                RenderTexture.active = targetTexture;
                pixels = new Texture2D(targetTexture.width, targetTexture.height, TextureFormat.RGBA32, false);
                pixels.ReadPixels(new Rect(0f, 0f, pixels.width, pixels.height), 0, 0);
                pixels.Apply();
                string path = Path.Combine(directory, filename);
                File.WriteAllBytes(path, pixels.EncodeToPNG());
                report.screenshots.Add(path);
                return pixels.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                if (pixels != null) Object.DestroyImmediate(pixels);
            }
        }
        private static RenderTexture NewTarget(int width, int height, string label)
        {
            var result = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            { name = label, hideFlags = HideFlags.HideAndDontSave, antiAliasing = 1 };
            result.Create();
            return result;
        }
        private static bool IsGold(Color32 pixel) => pixel.r > 35 && pixel.g > 25
            && pixel.r > pixel.g + 4 && pixel.r > pixel.b + 15 && pixel.g > pixel.b + 10;

        private bool Frames(int count) => Time.frameCount - phaseFrame >= count;
        private void ThrottleRequestedFrameRate()
        {
            if (requestedFps <= 0) return;
            // captureDeltaTime supplies the simulation step, but alone lets a
            // batch editor consume seven simulated seconds during one real
            // fade. Pace only newly observed gameplay frames in wall time too,
            // so the native seven-second reveal and unscaled fade stay aligned.
            // The frame gate above prevents repeated sleeps for the same frame.
            double now = EditorApplication.timeSinceStartup;
            if (lastFrameWallTime > 0d)
            {
                double remaining = 1d / requestedFps - (now - lastFrameWallTime);
                if (remaining > 0d)
                    System.Threading.Thread.Sleep((int)Math.Min(67d, Math.Ceiling(remaining * 1000d)));
            }
            lastFrameWallTime = EditorApplication.timeSinceStartup;
        }
        private bool SimulationSeconds(float seconds) => Frames(2) && Time.time - phaseSimulationStart >= seconds;
        private bool Settled() => Frames(3) && !effects.IsTransitioning
            && (effects.ActiveZoom == null || !effects.ActiveZoom.IsBlending);
        private void Begin(Phase next)
        {
            phase = next;
            phaseFrame = Time.frameCount;
            phaseSimulationStart = Time.time;
            phaseStart = EditorApplication.timeSinceStartup;
            Debug.Log("Waste Robot scan smoke phase: " + next);
        }
        private void Record(bool success, string message) { Require(success, message); report.checks.Add(message); Save(); }
        private void Save()
        {
            if (report != null && !string.IsNullOrEmpty(directory))
                File.WriteAllText(Path.Combine(directory, "scan-smoke.json"), JsonUtility.ToJson(report, true));
        }
        private void OnLog(string message, string stack, LogType type)
        {
            if (running && phase != Phase.Finishing
                && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
            {
                report.errors.Add(type + ": " + message + "\n" + stack);
                Save();
            }
        }
        private void Finish(Exception error)
        {
            if (phase == Phase.Finishing) return;
            if (error != null) report.errors.Add(error.ToString());
            if (target != null) SetDisplayDuration(originalDuration);
            if (scan != null) scan.enabled = false;
            if (effects != null) effects.enabled = false;
            if (shake != null && shakeSaved)
            {
                var settings = new SerializedObject(shake);
                settings.FindProperty("enableCameraShake").boolValue = originalShakeEnabled;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
            if (clockSaved) Time.captureDeltaTime = previousCaptureDelta;
            phase = Phase.Finishing;
            deadline = EditorApplication.timeSinceStartup + 20d;
            Save();
            EditorApplication.isPlaying = false;
        }
        private void Exit()
        {
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
            Debug.Log("Waste Robot scan smoke " + (report.passed ? "PASS" : "FAIL")
                + ": " + report.checks.Count + " checks; " + Path.Combine(directory, "scan-smoke.json"));
            Object.DestroyImmediate(this);
            EditorApplication.Exit(code);
        }
        private static int RequestedFps()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (args[i].Equals("-wasteRobotSmokeFps", StringComparison.OrdinalIgnoreCase))
                {
                    Require(i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out int fps) && (fps == 15 || fps == 30 || fps == 60),
                        "-wasteRobotSmokeFps requires 15, 30 or 60.");
                    return int.Parse(args[i + 1], CultureInfo.InvariantCulture);
                }
            return 0;
        }
        private static string OutputDirectory()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i].Equals("-logFile", StringComparison.OrdinalIgnoreCase) && args[i + 1] != "-")
                    return Path.GetDirectoryName(Path.GetFullPath(args[i + 1]));
            return Path.GetFullPath(Path.Combine("output", "validation", "waste-robot-scan"));
        }
        private static object Get(object value, string name) => value.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value);
        private static void Set(object value, string name, object setting) => value.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(value, setting);
        private static object Call(object value, string name, params object[] args) => value.GetType()
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
                | BindingFlags.DeclaredOnly).Invoke(value, args);
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Waste Robot scan smoke: " + message); }
    }
}
