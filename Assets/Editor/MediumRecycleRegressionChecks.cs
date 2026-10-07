using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using AnimalGame.Garbage;
using AnimalGame.RobotArm;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    // Production sprites, controller transactions and pure output inspection live in
    // isolated preview scenes. No check calls the gamepad transport.
    public static class MediumRecycleRegressionChecks
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly List<string> passed = new();
        private static readonly Vector3 Strokes = new(.75f, .85f, .95f);
        private const float Clamp = .20f, Finish = .25f, Recoil = .012f, Processing = .95f, ProcessingFade = .10f;
        private const float SmallProcessing = .45f, SmallProcessingFeedback = .6f;

        [MenuItem("Animal Game/Validation/Run Medium Recycle Checks")]
        public static void Run()
        {
            passed.Clear();
            Check("Production stages last 3.7 seconds with a complete in-body processing interval", CheckProductionSettings);
            Check("Three loading, pressing and locking strokes retain an intact piece", CheckMotion);
            Check("Every actual press takes time to build force and traverse its central travel", CheckSlowPress);
            Check("Motion sampled at equal time is independent of frame rate", CheckMotionRates);
            Check("Chest A release starts immediately and preserves production sprite scales", CheckProductionRecycle);
            Check("All sprite and icon vertices cross the inlet before completion", CheckTailAndPlane);
            Check("Temporary clipping preserves sprite properties, material assets and property blocks", CheckClipRestoration);
            Check("Visible claw strain and body recoil leave gameplay roots and logical hand poses stable", CheckVisualStrain);
            Check("Rigid waste strain is shader-only, synchronized with its icon and cleared on ingestion", CheckWasteVisualStrain);
            Check("Three stops and final confirmation fire once at 30, 60 and 120 FPS", CheckEventRates);
            Check("One large delta crosses every marker without losing or repeating feedback", CheckLargeDelta);
            Check("Cancellation restores art and clears only the operation's feedback", CheckCancellation);
            Check("External item destruction and same-frame controller disable clear all recycle presentation", CheckDestroyedDisable);
            Check("Small recycling retains its 0.35-second feed and completes its shorter processing phase", CheckSmallUnchanged);
            Check("Both sizes keep independent processing durations with shared release and fade settings", CheckSharedProcessingSettings);
            Check("Camera and controller switches remain independent with global intensity", CheckIndependentChannels);
            Check("Motor stages increase strength; compatibility finish and explicit completion retain their separate contracts", CheckMotorOutputs);
            Check("Pressing has continuous stage-scaled screen motion with independent motor control", CheckContinuousScreen);
            Check("Fully ingested waste restores driving while processing, stays hidden and ends silently", CheckProcessingFeedback);
            Check("Motor channels mix by maximum after independent Sony calibration", CheckMotorComposition);
            Check("Focus and pause discard the active session without replaying on resume", CheckFeedbackLifecycle);
            Debug.Log("Medium recycle checks PASS: " + passed.Count + " groups\n" + string.Join("\n", passed));
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        // Run in a graphics-enabled Unity process. A nographics process must not
        // claim pixel validation from its null graphics device.
        [MenuItem("Animal Game/Validation/Run Medium Recycle Render Checks")]
        public static void RunRenderChecks()
        {
            Require(SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null,
                "Medium recycle render checks require a graphics device");
            Scene scene = EditorSceneManager.NewPreviewScene();
            var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            Sprite sprite = null; RenderTexture target = null; Camera renderCamera = null;
            try
            {
                Color[] fill = new Color[32 * 32]; for (int i = 0; i < fill.Length; i++) fill[i] = Color.white;
                texture.SetPixels(fill); texture.Apply();
                sprite = Sprite.Create(texture, new Rect(0f, 0f, 32f, 32f), Vector2.one * .5f, 40f,
                    0, SpriteMeshType.FullRect);
                var itemRoot = new GameObject("Medium inlet render probe"); SceneManager.MoveGameObjectToScene(itemRoot, scene);
                WorldInteraction item = itemRoot.AddComponent<WorldInteraction>();
                item.SetKind(WorldInteractionKind.Grabbable);
                var robotRoot = new GameObject("Robot inlet coordinate frame"); SceneManager.MoveGameObjectToScene(robotRoot, scene);
                var body = new GameObject("Waste body").AddComponent<SpriteRenderer>();
                body.transform.SetParent(itemRoot.transform, false); body.transform.localPosition = Vector3.left * .5f;
                body.sprite = sprite; body.color = Color.red;
                var icon = new GameObject("Waste centre icon").AddComponent<SpriteRenderer>();
                icon.transform.SetParent(itemRoot.transform, false); icon.transform.localPosition = Vector3.right * .5f;
                icon.sprite = sprite; icon.color = new Color(0f, 1f, 0f, .33f);
                var cameraRoot = new GameObject("Medium inlet render camera"); SceneManager.MoveGameObjectToScene(cameraRoot, scene);
                Camera camera = cameraRoot.AddComponent<Camera>(); camera.transform.position = new Vector3(0f, 0f, -10f);
                renderCamera = camera;
                camera.orthographic = true; camera.orthographicSize = 1.2f; camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear; camera.nearClipPlane = .1f; camera.farClipPlane = 20f;
                camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
                camera.allowHDR = false; camera.allowMSAA = false;
                UniversalAdditionalCameraData urpCamera = null;
                bool urp = GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset;
                if (urp)
                {
                    urpCamera = camera.GetUniversalAdditionalCameraData();
                    urpCamera.renderPostProcessing = false; urpCamera.renderShadows = false;
                    urpCamera.requiresColorOption = CameraOverrideOption.Off;
                    urpCamera.requiresDepthOption = CameraOverrideOption.Off;
                }
                target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
                using (var clip = new MediumRecycleInletClip(item))
                {
                    Require(clip.Begin(robotRoot.transform, 0f), "Render inlet material failed to initialize");
                    foreach (int rendererIndex in urp ? new[] { 0, 1 } : new[] { -1 })
                    {
                        string rendererName = rendererIndex == 0 ? "URP2D" : rendererIndex == 1 ? "URPForward" : "Builtin";
                        if (urpCamera != null) urpCamera.SetRenderer(rendererIndex);
                        foreach (float angle in new[] { 0f, 37f, -73f })
                        {
                            robotRoot.transform.rotation = Quaternion.Euler(0f, 0f, angle);
                            clip.SetVisualOffset(robotRoot.transform, Vector2.zero);
                            foreach (SpriteRenderer renderer in new[] { body, icon })
                                renderer.sharedMaterial.SetFloat("_RecycleInletClipEnabled", 0f);
                            Texture2D unshifted = Capture(camera, target);
                            // Exaggerate only the probe offset enough to measure its rigid motion
                            // at pixel resolution; production strain remains the smaller .03D setting.
                            Vector2 strain = new Vector2(.035f, -.018f);
                            clip.SetVisualOffset(robotRoot.transform, strain);
                            Texture2D before = Capture(camera, target);
                            clip.Update(robotRoot.transform, 0f);
                            Texture2D after = Capture(camera, target);
                            try
                            {
                                Vector2 expectedShift = robotRoot.transform.TransformVector(strain);
                                Vector2 bodyShift = PixelCentroidWorld(before, camera, false) - PixelCentroidWorld(unshifted, camera, false);
                                Vector2 iconShift = PixelCentroidWorld(before, camera, true) - PixelCentroidWorld(unshifted, camera, true);
                                NearVector(bodyShift, expectedShift, "The waste shader did not render its rigid world offset in " + rendererName, .017f);
                                NearVector(iconShift, expectedShift, "The icon shader did not follow the same waste strain in " + rendererName, .017f);
                                NearVector(bodyShift, iconShift, "Waste and icon moved differently under strain in " + rendererName, .017f);
                                int visible = 0, hidden = 0, iconVisible = 0;
                                Color[] original = before.GetPixels(), clipped = after.GetPixels();
                                for (int y = 0; y < target.height; y++)
                                for (int x = 0; x < target.width; x++)
                                {
                                    int index = y * target.width + x;
                                    if (original[index].a < .15f) continue;
                                    Vector3 world = camera.ViewportToWorldPoint(new Vector3((x + .5f) / target.width,
                                        (y + .5f) / target.height, 10f));
                                    float localY = robotRoot.transform.InverseTransformPoint(world).y;
                                    if (Mathf.Abs(localY) < .04f) continue; // raster boundary is not a semantic error
                                    if (localY > 0f)
                                    {
                                        visible++;
                                        Require(Mathf.Abs(clipped[index].a - original[index].a) < .015f,
                                            "Inlet clipping changed an exterior pixel alpha in " + rendererName + " at angle " + angle);
                                        if (world.x > 0f) { iconVisible++; Require(Near(clipped[index].a, .33f, .015f),
                                            "Rendered centre icon did not preserve its .33 alpha"); }
                                    }
                                    else
                                    {
                                        hidden++;
                                        Require(clipped[index].a < .01f,
                                            "A waste or centre-icon pixel survived inside the inlet in " + rendererName + " at angle " + angle);
                                    }
                                }
                                Require(visible > 500 && hidden > 500 && iconVisible > 100,
                                    "Render validation did not cover both sprites and both halves of the inlet in " + rendererName);
                                string directory = Path.GetFullPath("Temp/MediumRecycleRenderChecks"); Directory.CreateDirectory(directory);
                                File.WriteAllBytes(Path.Combine(directory, "inlet-" + rendererName + "-" + angle.ToString("0") + ".png"), after.EncodeToPNG());
                            }
                            finally { Object.DestroyImmediate(unshifted); Object.DestroyImmediate(before); Object.DestroyImmediate(after); }
                        }
                    }
                }
            }
            finally
            {
                if (renderCamera != null) renderCamera.targetTexture = null;
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                if (sprite != null) Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture); EditorSceneManager.ClosePreviewScene(scene);
            }
            Debug.Log("Medium recycle render checks PASS: original-size body and .33-alpha icon move together under rigid strain and clip correctly at 0, 37 and -73 degrees in every production renderer.");
        }

        private static Vector2 PixelCentroidWorld(Texture2D snapshot, Camera camera, bool icon)
        {
            Color[] pixels = snapshot.GetPixels();
            Vector2 sum = Vector2.zero; int count = 0;
            for (int y = 0; y < snapshot.height; y++)
            for (int x = 0; x < snapshot.width; x++)
            {
                Color pixel = pixels[y * snapshot.width + x];
                bool role = icon ? pixel.g > .05f && pixel.r < .05f : pixel.r > .1f && pixel.g < .05f;
                if (!role || pixel.a < .15f) continue;
                sum += (Vector2)camera.ViewportToWorldPoint(new Vector3((x + .5f) / snapshot.width,
                    (y + .5f) / snapshot.height, 10f));
                count++;
            }
            Require(count > 500, "The unclipped strain probe did not render enough pixels for a body/icon centroid");
            return sum / count;
        }

        private static Texture2D Capture(Camera camera, RenderTexture target)
        {
            RenderTexture previous = RenderTexture.active;
            var snapshot = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try
            {
                if (GraphicsSettings.currentRenderPipeline == null) camera.Render();
                else
                {
                    var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                    Require(RenderPipeline.SupportsRenderRequest(camera, request),
                        "The active pipeline does not support the medium recycle single-camera render request");
                    RenderPipeline.SubmitRenderRequest(camera, request);
                }
                RenderTexture.active = target;
                snapshot.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); snapshot.Apply();
                return snapshot;
            }
            catch { Object.DestroyImmediate(snapshot); throw; }
            finally { RenderTexture.active = previous; }
        }

        private static void Check(string name, Action test) { test(); passed.Add(name); }
        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }
        private static bool Near(float a, float b, float epsilon = .0002f) => Mathf.Abs(a - b) <= epsilon;
        private static void NearVector(Vector2 a, Vector2 b, string message, float epsilon = .0002f) =>
            Require(Vector2.Distance(a, b) <= epsilon, message + ": " + a + " vs " + b);
        private static float Duration => MediumRecycleMotion.Duration(Clamp, Strokes, Finish, Processing);
        private static float ProcessingStart => Duration - Processing;
        private static MediumRecycleFrame Sample(float time) => MediumRecycleMotion.Sample(time, Clamp, Strokes, Finish, Recoil, Processing, ProcessingFade);

        private static void CheckProductionSettings()
        {
            RobotArmController arms = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Resources/Robot/RobotMarker.prefab").GetComponent<RobotArmController>();
            Require(Near((float)Get(arms, "mediumRecycleClampDuration"), Clamp)
                && (Vector3)Get(arms, "mediumRecycleStrokeDurations") == Strokes
                && Near((float)Get(arms, "mediumRecycleFinishDuration"), Finish)
                && Near((float)Get(arms, "mediumRecycleProcessingDuration"), Processing)
                && Near((float)Get(arms, "smallRecycleProcessingDuration"), SmallProcessing)
                && Near((float)Get(arms, "smallRecycleProcessingFeedbackMultiplier"), SmallProcessingFeedback)
                && Near((float)Get(arms, "mediumRecycleProcessingFadeDuration"), ProcessingFade)
                && Near((float)Get(arms, "mediumRecycleInitialFeedFraction"), .12f)
                && Near((float)Get(arms, "recycleDuration"), .35f) && Near(Duration, 3.7f),
                "Production independent processing times or the small feedback multiplier do not match their defaults");
            Require((float)Get(arms, "mediumRecycleHandShakeOfBodyDiameter") > 0f
                && (float)Get(arms, "mediumRecycleHandShakeFrequency") >= 15f
                && (float)Get(arms, "mediumRecycleBodyRecoilOfBodyDiameter") > 0f
                && Near((float)Get(arms, "mediumRecycleGarbageShakeOfBodyDiameter"), .03f)
                && Near((float)Get(arms, "mediumRecycleGarbageShakeFrequency"), 19f),
                "Production strain and body recoil are disabled");
            Require(typeof(RobotArmController).GetField("mediumRecycleEndScale", Instance) == null
                && typeof(RobotArmController).GetField("mediumRecycleMoveDuration", Instance) == null
                && typeof(RobotArmController).GetField("mediumRecyclePauseDuration", Instance) == null,
                "Retired shrinking/short-cycle controls are still exposed");
            Shader shader = (Shader)Get(arms, "mediumRecycleInletShader");
            Require(shader != null && shader.name == MediumRecycleInletClip.ShaderName
                && !ShaderUtil.ShaderHasError(shader), "Production inlet shader is missing or has compiler errors");
        }

        private static void CheckMotion()
        {
            Require(Sample(0f).Phase.ToString() == "Clamp" && Near(Sample(0f).Progress, 0f),
                "The visible clamp did not begin immediately at the original position");
            float previousEnd = 0f;
            float[] goals = { .30f, .65f, 1f };
            for (int stage = 0; stage < 3; stage++)
            {
                float push = MediumRecycleMotion.PushTime(Clamp, Strokes, stage);
                float stop = MediumRecycleMotion.StopTime(Clamp, Strokes, stage);
                float start = push - Strokes[stage] * .22f;
                MediumRecycleFrame load = Sample(start + Strokes[stage] * .12f);
                MediumRecycleFrame pressed = Sample((push + stop) * .5f);
                MediumRecycleFrame lockEnd = Sample(start + Strokes[stage] * .94f);
                Require(load.Stage == stage && load.Phase.ToString() == "Load"
                    && Near(load.Progress, previousEnd) && load.Effort01 > 0f,
                    "A loading interval moved the waste or failed to show force in stage " + stage);
                Require(pressed.Phase.ToString() == "Push" && pressed.Progress > previousEnd + .04f
                    && pressed.Progress < goals[stage] && pressed.Effort01 > 0f,
                    "A stroke did not visibly press the intact waste in stage " + stage);
                Require(lockEnd.Phase.ToString() == "Lock" && Near(lockEnd.Progress, goals[stage]),
                    "A stroke did not settle at its intended hard-stop position in stage " + stage);
                float maximumRecoil = 0f;
                for (int sample = 1; sample < 18; sample++)
                {
                    MediumRecycleFrame frame = Sample(stop + Strokes[stage] * .28f * sample / 18f);
                    maximumRecoil = Mathf.Max(maximumRecoil, frame.Recoil01);
                    Require(frame.Progress >= previousEnd - .0001f && frame.Progress <= goals[stage] + .0001f,
                        "Lock recoil overshot either stroke endpoint");
                }
                Require(maximumRecoil > .2f, "A hard stop has no mechanical recoil");
                previousEnd = goals[stage];
            }
            MediumRecycleFrame processing = Sample(ProcessingStart + Finish * .5f);
            Require(processing.Phase.ToString() == "Processing" && Near(processing.Progress, 1f)
                && processing.HandRelease01 > 0f && processing.HandRelease01 < 1f
                && Near(processing.ProcessingEnvelope01, 1f) && Near(processing.Effort01, 0f),
                "In-body processing did not retain full ingestion while the claws release");
            MediumRecycleFrame fading = Sample(Duration - ProcessingFade * .5f);
            Require(fading.Phase.ToString() == "Processing" && Near(fading.HandRelease01, 1f)
                && fading.ProcessingEnvelope01 > 0f && fading.ProcessingEnvelope01 < 1f,
                "The final processing interval did not fade after the claws released");
            Require(Sample(Duration).Phase.ToString() == "Complete" && Near(Sample(Duration).Progress, 1f),
                "The full timeline did not complete at exactly its configured duration");
            Require(Near(Sample(Duration).ProcessingEnvelope01, 0f), "Exact completion retained processing feedback");
        }

        private static void CheckSlowPress()
        {
            float from = 0f;
            float[] ends = { .30f, .65f, 1f };
            for (int stage = 0; stage < 3; stage++)
            {
                float push = MediumRecycleMotion.PushTime(Clamp, Strokes, stage);
                float stop = MediumRecycleMotion.StopTime(Clamp, Strokes, stage);
                float span = stop - push, distance = ends[stage] - from;
                Require(span >= .374f, "The actual inward press is still too short in stage " + stage);
                Require((Sample(push + span * .1f).Progress - from) / distance < .05f,
                    "A press still jumps most of its travel into its first few frames");
                Require((Sample(push + span * .25f).Progress - from) / distance < .20f,
                    "A press accelerates too abruptly instead of slowly engaging its load");
                float twentyTime = -1f, eightyTime = -1f;
                for (int step = 0; step <= 240; step++)
                {
                    float elapsed = push + span * step / 240f;
                    float progress = (Sample(elapsed).Progress - from) / distance;
                    if (twentyTime < 0f && progress >= .2f) twentyTime = elapsed;
                    if (eightyTime < 0f && progress >= .8f) eightyTime = elapsed;
                }
                Require(eightyTime - twentyTime >= span * .4f,
                    "The central part of a press still looks like a single quick jump");
                from = ends[stage];
            }
        }

        private static void CheckMotionRates()
        {
            foreach (float checkpoint in new[] { .1f, .5f, 1.1f, 1.5f, 2.3f,
                Duration - ProcessingFade * 1.5f, Duration - ProcessingFade * .5f, Duration })
            {
                MediumRecycleFrame expected = Sample(checkpoint);
                foreach (int fps in new[] { 30, 60, 120 })
                {
                    float elapsed = 0f;
                    while (elapsed + 1f / fps < checkpoint) elapsed += 1f / fps;
                    elapsed += checkpoint - elapsed;
                    MediumRecycleFrame actual = Sample(elapsed);
                    Require(actual.Stage == expected.Stage && actual.Phase == expected.Phase
                        && Near(actual.Progress, expected.Progress) && Near(actual.Effort01, expected.Effort01),
                        "Equal-time motion changed with frame rate at " + fps);
                }
            }
        }

        private static void CheckProductionRecycle()
        {
            foreach (float angle in new[] { 0f, 37f, -110f })
            using (var f = new Fixture())
            {
                f.Root.transform.SetPositionAndRotation(new Vector3(3f, -2f), Quaternion.Euler(0f, 0f, angle));
                WorldInteraction item = f.Garbage();
                Vector3 originalScale = item.LocalScale;
                f.Begin(item);
                Require(f.Arms.State == RobotArmState.Recycling && f.Arms.HeldObject == item
                    && item.Owner == f.Arms && f.Recycled == 0 && Near((float)Get(f.Arms, "recycleTime"), 0f),
                    "A release did not begin recycling on the same frame without a delay");
                Require((bool)Get(f.Arms, "previousGrab"), "Medium recycle opened its claws at the start");
                Vector3 start = (Vector3)Get(f.Arms, "recycleStart"), end = (Vector3)Get(f.Arms, "mediumRecycleEnd");
                Require(Vector3.Distance(f.Frame.InverseTransformPoint(item.WorldPosition), start) < .0001f,
                    "Beginning the new initial feed teleported waste away from its release position");
                float initialFeed = (float)Get(f.Arms, "mediumRecycleInitialFeedFraction");
                f.Step(Clamp * .25f, false, false);
                float earlyFeed = InwardFraction();
                Require(earlyFeed > 0f && earlyFeed < initialFeed * .25f && item.LocalScale == originalScale,
                    "The initial clamp did not begin a continuous, slow inward feed from the player's release position");
                f.Step(Clamp * .25f, false, false);
                Require(Near(InwardFraction(), .06f), "Half of the clamping time did not continuously pre-feed six percent of the full travel");
                f.Step(Clamp * .5f, false, false);
                Require(Near(InwardFraction(), initialFeed), "The first loading position did not reach the configured initial feed depth");
                Vector3 loadedPosition = item.WorldPosition;
                f.Step(.04f, false, false);
                Require(f.Arms.CurrentMediumRecycleFrame.Phase == MediumRecyclePhase.Load
                    && Vector3.Distance(item.WorldPosition, loadedPosition) < .0001f
                    && Near(InwardFraction(), initialFeed), "The first loading hold lost its deeper initial feed position");
                f.Step(MediumRecycleMotion.StopTime(Clamp, Strokes, 0) - (float)Get(f.Arms, "recycleTime"), false, false);
                float firstStop = InwardFraction();
                Require(Near(firstStop, .384f) && firstStop > Sample(MediumRecycleMotion.StopTime(Clamp, Strokes, 0)).Progress + .05f,
                    "The first pressing stop did not move noticeably farther inside the chassis");
                int totalSteps = Mathf.CeilToInt((Duration - (float)Get(f.Arms, "recycleTime")) * 60f);
                for (int i = 0; i < totalSteps; i++)
                {
                    f.Step(1f / 60f, false, i % 2 == 0);
                    Require(item.LocalScale == originalScale, "An intact medium sprite changed scale at frame " + i);
                    Require(f.Recycled == 0 || (float)Get(f.Arms, "recycleTime") >= Duration - .0001f,
                        "Medium waste completed before all stages ended");
                }
                f.Step(.05f, false, false);
                Require(f.Recycled == 1 && !item.gameObject.activeSelf && item.Owner == null
                    && f.Arms.HeldObject == null && item.LocalScale == originalScale,
                    "The medium operation did not finish once after A and arm-mode release");
                f.Step(Duration, false, false);
                Require(f.Recycled == 1, "Completion repeated after the operation ended");

                float InwardFraction()
                {
                    float y = f.Frame.InverseTransformPoint(item.WorldPosition).y;
                    return (y - start.y) / (end.y - start.y);
                }
            }
        }

        private static void CheckTailAndPlane()
        {
            using (var f = new Fixture())
            {
                f.Root.transform.SetPositionAndRotation(new Vector3(3f, -2f), Quaternion.Euler(0f, 0f, 57f));
                WorldInteraction item = f.Garbage();
                item.transform.rotation = Quaternion.Euler(0f, 0f, -19f);
                item.LocalScale = Vector3.Scale(item.LocalScale, new Vector3(1.3f, .8f, 1f));
                // Match a legitimate rotated held grip before recording its trailing extent.
                Set(f.Arms, "heldRotation", Quaternion.Inverse(f.Root.transform.rotation) * item.transform.rotation);
                f.Begin(item);
                var clip = (MediumRecycleInletClip)Get(f.Arms, "mediumRecycleClip");
                float inlet = (float)Get(f.Arms, "mediumRecycleInletY");
                Require(clip != null && clip.MaximumLocalY(f.Frame) > inlet,
                    "Initial waste had no exposed rear edge to press through the inlet");
                f.Step(.6f);
                Require(clip.MaximumLocalY(f.Frame) > inlet && f.Recycled == 0,
                    "Partially fed waste was completed before its rear edge entered");
                Quaternion relative = Quaternion.Inverse(f.Frame.rotation) * item.transform.rotation;
                f.Root.transform.rotation = Quaternion.Euler(0f, 0f, 94f);
                f.Step(.01f);
                Require(Quaternion.Angle(Quaternion.Inverse(f.Frame.rotation) * item.transform.rotation, relative) < .001f,
                    "Turning during Update changed the ingested object's local rotation and trailing extent");
                f.Root.transform.rotation = Quaternion.Euler(0f, 0f, -24f);
                Call(f.Arms, "LateUpdate");
                Require(Quaternion.Angle(Quaternion.Inverse(f.Frame.rotation) * item.transform.rotation, relative) < .001f,
                    "Late player steering did not carry the partially ingested object with the inlet");
                SpriteRenderer[] sprites = item.GetComponentsInChildren<SpriteRenderer>(true);
                Require(sprites.Length >= 2, "Production waste did not include its centre icon");
                Vector4 plane = sprites[0].sharedMaterial.GetVector("_RecycleInletPlane");
                foreach (SpriteRenderer sprite in sprites)
                {
                    Require(sprite.sharedMaterial.shader.name == MediumRecycleInletClip.ShaderName
                        && sprite.sharedMaterial.GetVector("_RecycleInletPlane") == plane
                        && Near(sprite.sharedMaterial.GetFloat("_RecycleInletClipEnabled"), 1f),
                        "The icon and waste were not clipped at the same world-space inlet");
                }
                Require(Near(sprites[1].color.a, .33f), "Clipping changed the centre icon opacity");
                f.Step(Clamp + Strokes.x + Strokes.y + Strokes.z - (float)Get(f.Arms, "recycleTime") + .001f);
                Require(clip.MaximumLocalY(f.Frame) <= inlet + .0002f && f.Recycled == 0
                    && f.Arms.State == RobotArmState.Recycling,
                    "Complete ingestion did not precede final locking and release");
                f.Step(Processing + .001f);
                Require(f.Recycled == 1 && f.Arms.HeldObject == null,
                    "Ingested waste was not recycled once after final confirmation");
            }
        }

        private static void CheckClipRestoration()
        {
            using (var f = new Fixture())
            {
                WorldInteraction item = f.Garbage();
                SpriteRenderer[] sprites = item.GetComponentsInChildren<SpriteRenderer>(true);
                var originals = new Material[sprites.Length][];
                var colors = new Color[sprites.Length];
                var sorting = new int[sprites.Length];
                int probe = Shader.PropertyToID("_RegressionRetainedProperty");
                for (int i = 0; i < sprites.Length; i++)
                {
                    originals[i] = sprites[i].sharedMaterials; colors[i] = sprites[i].color;
                    sorting[i] = sprites[i].sortingOrder;
                    var block = new MaterialPropertyBlock(); block.SetFloat(probe, 11f + i);
                    sprites[i].SetPropertyBlock(block);
                }
                var clip = new MediumRecycleInletClip(item);
                using (clip)
                {
                    Vector3 logicalPosition = item.WorldPosition, logicalScale = item.LocalScale;
                    Require(clip.Begin(f.Frame, .2f), "Production sprite clipping failed to initialize");
                    clip.SetVisualOffset(f.Frame, new Vector2(.012f, -.007f));
                    clip.Update(f.Frame, .25f);
                    Require(item.WorldPosition == logicalPosition && item.LocalScale == logicalScale,
                        "Applying an inlet visual offset changed garbage interaction geometry");
                    for (int i = 0; i < sprites.Length; i++)
                    {
                        Require(sprites[i].sharedMaterial != originals[i][0]
                            && originals[i][0].shader.name != MediumRecycleInletClip.ShaderName
                            && sprites[i].color == colors[i] && sprites[i].sortingOrder == sorting[i],
                            "Runtime clipping modified an asset or a sprite property");
                        var block = new MaterialPropertyBlock(); sprites[i].GetPropertyBlock(block);
                        Require(Near(block.GetFloat(probe), 11f + i), "Clipping discarded an existing property block");
                        block.SetFloat(probe, 21f + i); sprites[i].SetPropertyBlock(block);
                    }
                }
                Require(clip.VisualOffsetWorld == Vector3.zero,
                    "Disposal retained a rigid waste visual offset");
                for (int i = 0; i < sprites.Length; i++)
                {
                    Require(sprites[i].sharedMaterials.Length == originals[i].Length
                        && sprites[i].sharedMaterial == originals[i][0], "Disposal did not restore the original material array");
                    var block = new MaterialPropertyBlock(); sprites[i].GetPropertyBlock(block);
                    Require(Near(block.GetFloat(probe), 21f + i), "Disposal erased a gameplay property update made during clipping");
                }
            }
        }

        private static void CheckVisualStrain()
        {
            using (var f = new Fixture())
            {
                WorldInteraction item = f.Garbage(); f.Begin(item);
                float load = MediumRecycleMotion.PushTime(Clamp, Strokes, 0) - .09f;
                f.Step(load);
                Vector2 left = f.Arms.LeftHandWorld, right = f.Arms.RightHandWorld;
                Vector3 root = f.Root.transform.position, frame = f.Frame.localPosition;
                Quaternion rootRotation = f.Root.transform.rotation, frameRotation = f.Frame.localRotation;
                Vector3 waste = item.WorldPosition;
                Vector2 offset = f.Arms.MediumRecycleHandVisualOffset;
                Vector3 visibleHand = HandSprite(f.Arms, "left").transform.position;
                f.Step(.013f);
                NearVector(f.Arms.LeftHandWorld, left, "Visual strain moved the logical left claw");
                NearVector(f.Arms.RightHandWorld, right, "Visual strain moved the logical right claw");
                Require(Vector3.Distance(item.WorldPosition, waste) < .0001f
                    && f.Root.transform.position == root && f.Frame.localPosition == frame
                    && f.Root.transform.rotation == rootRotation && f.Frame.localRotation == frameRotation,
                    "Visual effort moved held physics or the player coordinate frame");
                Require(f.Arms.MediumRecycleHandVisualOffset.sqrMagnitude > 0f
                    && Vector2.Distance(offset, f.Arms.MediumRecycleHandVisualOffset) > .00001f
                    && Vector3.Distance(visibleHand, HandSprite(f.Arms, "left").transform.position) > .00001f,
                    "Pressing claws do not visibly tremble under load");
                f.Step(MediumRecycleMotion.StopTime(Clamp, Strokes, 1) - (float)Get(f.Arms, "recycleTime") + .025f);
                Require(f.Body.localPosition != Vector3.zero || Quaternion.Angle(f.Body.localRotation, Quaternion.identity) > .001f,
                    "A stop did not make the chassis visually recoil");
                Require(f.Root.transform.position == root && f.Root.transform.rotation == rootRotation
                    && f.Frame.localPosition == frame && f.Frame.localRotation == frameRotation,
                    "Body recoil changed a movement or arm coordinate transform");
                f.Step(Duration);
                Require(f.Arms.MediumRecycleHandVisualOffset == Vector2.zero
                    && f.Body.localPosition == Vector3.zero && f.Body.localRotation == Quaternion.identity,
                    "Recycle completion retained its visual strain offsets");
            }
        }

        private static void CheckEventRates()
        {
            foreach (int fps in new[] { 30, 60, 120 })
            using (var f = new Fixture())
            {
                WorldInteraction item = f.Garbage(); f.Begin(item);
                float elapsed = 0f;
                int observedStageMask = 0;
                for (int i = 0; i < Mathf.CeilToInt((Duration + .08f) * fps); i++)
                {
                    f.Step(1f / fps, false, false); elapsed = (float)Get(f.Arms, "recycleTime");
                    int expectedStops = 0;
                    bool expectedProcessing = elapsed >= ProcessingStart;
                    for (int stage = 0; stage < 3; stage++)
                        if (elapsed + .000001f >= MediumRecycleMotion.StopTime(Clamp, Strokes, stage)) expectedStops |= 1 << stage;
                    int expectedMask = expectedStops | (expectedProcessing ? 8 : 0);
                    int cameraMask = (int)Get(f.Shake, "mediumRecyclePlayedStageMask");
                    observedStageMask |= cameraMask;
                    Require((int)Get(f.Arms, "mediumRecycleStopsPlayed") == expectedStops
                        && (bool)Get(f.Arms, "mediumRecycleCompletionPlayed") == expectedProcessing
                        && cameraMask == (f.Recycled == 0 ? expectedMask : 0),
                        "A stop feedback marker was lost, early or duplicated at " + fps + " FPS and time " + elapsed);
                    // Live pulse storage is not an event-history counter: the shared fade
                    // legitimately clears it when its envelope quantizes to zero.
                    var liveStages = new HashSet<int>();
                    foreach (object pulse in (IList)Get(f.Shake, "mediumRecyclePulses"))
                    {
                        int stage = (int)pulse.GetType().GetField("Stage", Instance).GetValue(pulse);
                        Require(liveStages.Add(stage) && (expectedMask & (1 << stage)) != 0,
                            "A live feedback source duplicated a stage or preceded its timeline marker");
                    }
                    if (f.Recycled != 0 || (f.Arms.CurrentMediumRecycleFrame.Phase == MediumRecyclePhase.Processing
                        && f.Arms.CurrentMediumRecycleFrame.ProcessingEnvelope01 <= 0f))
                        Require(PulseCount(f.Shake) == 0 && Motors(f.Shake, Time.time) == Vector2.zero,
                            "Zero processing envelope or completion retained a live feedback source");
                }
                Require(f.Recycled == 1 && observedStageMask == 15 && PulseCount(f.Shake) == 0
                    && (int)Get(f.Arms, "mediumRecycleStopsPlayed") == 7 && (bool)Get(f.Arms, "mediumRecycleCompletionPlayed"),
                    "Frame-stepped operation did not produce three stops and one completion");
            }
        }

        private static void CheckWasteVisualStrain()
        {
            using (var f = new Fixture())
            {
                WorldInteraction item = f.Garbage(); Vector3 scale = item.LocalScale;
                f.Begin(item);
                var clip = (MediumRecycleInletClip)Get(f.Arms, "mediumRecycleClip");
                float push = MediumRecycleMotion.PushTime(Clamp, Strokes, 0);
                float stop = MediumRecycleMotion.StopTime(Clamp, Strokes, 0);
                f.Step((push + stop) * .5f);
                Vector2 firstOffset = f.Arms.MediumRecycleGarbageVisualOffset;
                Require(firstOffset.sqrMagnitude > .000001f,
                    "A loaded press has no perceptible waste strain");
                ValidateOffset();
                f.Step(.021f);
                Require(Vector2.Distance(firstOffset, f.Arms.MediumRecycleGarbageVisualOffset) > .00001f,
                    "Rigid waste did not visibly tremble across loaded press frames");
                ValidateOffset();
                Vector2 localOffset = f.Arms.MediumRecycleGarbageVisualOffset;
                f.Root.transform.rotation = Quaternion.Euler(0f, 0f, 61f);
                Call(f.Arms, "LateUpdate");
                NearVector(clip.VisualOffsetWorld, f.Frame.TransformVector(localOffset),
                    "Late steering did not rotate the pure shader strain with the inlet");
                ValidateOffset();
                Require(item.LocalScale == scale && f.Arms.MediumRecycleGarbageVisualOffset == localOffset,
                    "Late steering changed the rigid waste scale or its local effort offset");
                f.Step(ProcessingStart - (float)Get(f.Arms, "recycleTime") + .01f);
                Require(f.Arms.MediumRecycleGarbageVisualOffset == Vector2.zero && clip.VisualOffsetWorld == Vector3.zero
                    && clip.MaximumLocalY(f.Frame) <= (float)Get(f.Arms, "mediumRecycleInletY") + .0002f,
                    "Fully ingested waste retained strain or exposed a rear edge during final release");
                f.Step(Processing + .01f);
                Require(f.Recycled == 1 && f.Arms.MediumRecycleGarbageVisualOffset == Vector2.zero
                    && Get(f.Arms, "mediumRecycleClip") == null,
                    "Completed recycling retained its waste shader offset");

                void ValidateOffset()
                {
                    float diameter = (float)Get(f.Arms, "diameter");
                    Vector2 local = f.Arms.MediumRecycleGarbageVisualOffset;
                    Require(Mathf.Abs(local.x) <= diameter * .03f + .0001f
                        && local.y <= .000001f && local.y >= -diameter * .015f - .0001f,
                        "Waste strain exceeded its bounded sideways/inward motion");
                    Vector3 expectedWorld = f.Frame.TransformVector((Vector3)local);
                    Require(Vector3.Distance(clip.VisualOffsetWorld, expectedWorld) < .0001f,
                        "Material visual offset did not match the configured robot-local strain");
                    Require(Vector3.Distance(item.WorldPosition, f.Frame.TransformPoint((Vector3)Get(f.Arms, "recyclePosition"))) < .0001f
                        && item.LocalScale == scale,
                        "Waste strain altered the authoritative position or original size");
                    foreach (SpriteRenderer renderer in item.GetComponentsInChildren<SpriteRenderer>(true))
                    {
                        Vector4 materialOffset = renderer.sharedMaterial.GetVector("_RecycleVisualOffset");
                        Require(Vector3.Distance(new Vector3(materialOffset.x, materialOffset.y, materialOffset.z), expectedWorld) < .0001f
                            && Near(materialOffset.w, 0f), "Waste and icon did not use the same rigid shader offset");
                    }
                }
            }
        }

        private static void CheckLargeDelta()
        {
            foreach (bool small in new[] { false, true })
            using (var f = new Fixture())
            {
                float duration = small ? .35f + Mathf.Max(Finish, SmallProcessing) : Duration;
                f.Begin(f.Garbage(small)); f.Step(duration + .2f, false, false);
                Require(PulseCount(f.Shake) == 0 && f.Recycled == 1
                    && (int)Get(f.Arms, "mediumRecycleStopsPlayed") == (small ? 0 : 7)
                    && (bool)Get(f.Arms, "mediumRecycleCompletionPlayed"),
                    "A delta crossing all stages did not produce one feedback for each event");
                f.Step(duration, false, false);
                Require(PulseCount(f.Shake) == 0 && f.Recycled == 1,
                    "Post-completion stepping repeated cross-frame markers");
            }
        }

        private static void CheckCancellation()
        {
            foreach (bool small in new[] { false, true })
            foreach (string reason in new[] { "fall", "photo", "external", "disable", "owner", "item" })
            using (var f = new Fixture())
            {
                WorldInteraction item = f.Garbage(small);
                SpriteRenderer[] sprites = item.GetComponentsInChildren<SpriteRenderer>(true);
                var materials = new Material[sprites.Length];
                for (int i = 0; i < sprites.Length; i++) materials[i] = sprites[i].sharedMaterial;
                Vector3 scale = item.LocalScale; f.Begin(item); f.Step(small ? .35f + .02f : .8f);
                Require(PulseCount(f.Shake) > 0, "Cancellation fixture did not pass its first stop");
                if (reason == "fall")
                {
                    var tumble = f.Root.AddComponent<RobotTumbleController>();
                    typeof(RobotTumbleController).GetProperty("State").SetValue(tumble, RobotTumbleState.Fallen);
                    Set(f.Arms, "tumble", tumble);
                    typeof(RobotMover).GetProperty("MovementMode").SetValue(f.Mover, RobotMovementMode.Fallen);
                }
                else if (reason == "photo")
                {
                    var photo = f.Root.AddComponent<PhotoModeController>();
                    FieldInfo state = typeof(PhotoModeController).GetField("state", Instance);
                    state.SetValue(photo, Enum.Parse(state.FieldType, "Active")); Set(f.Arms, "photoMode", photo);
                }
                else if (reason == "external")
                    typeof(RobotMover).GetProperty("MovementMode").SetValue(f.Mover, RobotMovementMode.ExternalTumble);
                else if (reason == "owner") item.Release(f.Arms);
                else if (reason == "item") item.enabled = false;
                if (reason == "disable") Call(f.Arms, "OnDisable"); else f.Step(.05f, false, false);
                Require(f.Arms.State != RobotArmState.Recycling && f.Arms.HeldObject == null
                    && item.Owner == null && item.LocalScale == scale && item.gameObject.activeSelf
                    && f.Recycled == 0 && PulseCount(f.Shake) == 0 && !f.Mover.IsArmInputCaptured,
                    "Cancellation left active ownership, feedback or shrink state: " + reason);
                for (int i = 0; i < sprites.Length; i++)
                    Require(sprites[i].sharedMaterial == materials[i], "Cancellation retained an inlet material: " + reason);
                Require(f.Arms.MediumRecycleHandVisualOffset == Vector2.zero && f.Body.localPosition == Vector3.zero,
                    "Cancellation did not clear visible hand/body force: " + reason);
                Require(f.Arms.MediumRecycleGarbageVisualOffset == Vector2.zero,
                    "Cancellation retained rigid waste strain: " + reason);
                Require(Get(f.Arms, "mediumRecycleClip") == null && CameraSample(f.Shake) == Vector3.zero
                    && Motors(f.Shake, Time.time) == Vector2.zero && f.Body.localRotation == Quaternion.identity,
                    "Cancellation retained an inlet clip, screen, motor or body tail: small=" + small + ", " + reason);
            }
        }

        private static void CheckSmallUnchanged()
        {
            foreach (bool oversized in new[] { false, true })
            using (var f = new Fixture())
            {
                WorldInteraction item = f.Garbage(small: true);
                if (oversized) item.LocalScale = Vector3.Scale(item.LocalScale, new Vector3(3f, 10f, 1f));
                Vector3 scale = item.LocalScale;
                int hands = (int)Get(f.Arms, "heldHands"); f.Begin(item);
                Vector3 start = (Vector3)Get(f.Arms, "recycleStart"), end = (Vector3)Get(f.Arms, "mediumRecycleEnd");
                var clip = (MediumRecycleInletClip)Get(f.Arms, "mediumRecycleClip");
                Require((hands == 1 || hands == 2) && !(bool)Get(f.Arms, "previousGrab")
                    && !(bool)Get(f.Arms, "mediumRecycleActive") && (bool)Get(f.Arms, "recyclePresentationActive")
                    && clip != null && Vector3.Distance(f.Frame.InverseTransformPoint(item.WorldPosition), start) < .0001f,
                    "Small recycle lost its single-hand feed or teleported when the shared inlet began");
                Require(!oversized || end.y < 0f,
                    "An oversized small sprite did not receive the deeper endpoint needed to conceal its full tail");
                var replacementRoot = new GameObject("Rejected in-feed small replacement");
                SceneManager.MoveGameObjectToScene(replacementRoot, f.Root.scene);
                replacementRoot.transform.position = item.WorldPosition + Vector3.right * 3f;
                var replacement = replacementRoot.AddComponent<WorldInteraction>();
                replacement.SetKind(WorldInteractionKind.Grabbable);
                Set(replacement, "requiredHands", 1); Set(replacement, "size", RecyclableSize.Small);
                int replacementGrabs = 0; Set(replacement, "onGrabbed", (Action)(() => replacementGrabs++));
                Require(replacement.Available && !f.Arms.TryReplaceHeldObject(item, replacement)
                    && replacement.Owner == null && replacementGrabs == 0
                    && f.Arms.HeldObject == item && item.Owner == f.Arms
                    && ReferenceEquals(Get(f.Arms, "mediumRecycleClip"), clip)
                    && (int)Get(f.Arms, "heldHands") == hands,
                    "Recycling allowed a handoff to replace the item/clip session or grabbed its rejected candidate");
                f.Step(.34f, false, false);
                Require(f.Recycled == 0 && f.Arms.State == RobotArmState.Recycling && item.LocalScale == scale
                    && f.Mover.IsArmInputCaptured
                    && PulseCount(f.Shake) == 0 && CameraSample(f.Shake) == Vector3.zero
                    && Motors(f.Shake, Time.time) == Vector2.zero && f.Body.localPosition == Vector3.zero
                    && Vector3.Distance(f.Frame.InverseTransformPoint(item.WorldPosition),
                        Vector3.Lerp(start, end, Mathf.SmoothStep(0f, 1f, .34f / .35f))) < .0001f,
                    "Small recycle changed its original .35-second feed, intact size or pre-processing feedback");
                f.Step(.02f, false, false);
                Require(f.Arms.CurrentMediumRecycleFrame.Phase == MediumRecyclePhase.Processing
                    && f.Arms.CurrentMediumRecycleFrame.Progress == 1f && f.Recycled == 0
                    && f.Arms.HeldObject == item && item.Owner == f.Arms && !f.Mover.IsArmInputCaptured
                    && Near(f.Mover.GrabMovementMultiplier, 1f)
                    && (int)Get(f.Arms, "mediumRecycleStopsPlayed") == 0
                    && (bool)Get(f.Arms, "mediumRecycleCompletionPlayed")
                    && (int)Get(f.Shake, "mediumRecyclePlayedStageMask") == 8 && PulseCount(f.Shake) == 1,
                    "Small ingestion did not retain processing while releasing drive capture/resistance, or emitted medium press-stop events");
                Require(clip.MaximumLocalY(f.Frame) <= (float)Get(f.Arms, "mediumRecycleClipY") + .0002f
                    && item.LocalScale == scale && CameraSample(f.Shake).sqrMagnitude > 0f
                    && Motors(f.Shake, Time.time).sqrMagnitude > 0f && f.Body.localPosition.sqrMagnitude > 0f,
                    "Small processing left visible waste outside or omitted its shared body/screen/controller feedback");

                Quaternion heldRotation = (Quaternion)Get(f.Arms, "heldRotation");
                f.Root.transform.rotation = Quaternion.Euler(0f, 0f, 73f); Call(f.Arms, "LateUpdate");
                Require(Quaternion.Angle(item.transform.rotation, f.Root.transform.rotation * heldRotation) < .001f
                    && Vector3.Distance(f.Frame.InverseTransformPoint(item.WorldPosition), end) < .0001f
                    && clip.MaximumLocalY(f.Frame) <= (float)Get(f.Arms, "mediumRecycleClipY") + .0002f,
                    "Small ingested waste did not stay fully hidden and aligned after the robot turned");
                f.Step(SmallProcessing - ProcessingFade - .01f, false, false);
                Require(f.Recycled == 0 && f.Arms.State == RobotArmState.Recycling && !f.Mover.IsArmInputCaptured
                    && Near(f.Arms.CurrentMediumRecycleFrame.ProcessingEnvelope01, 1f),
                    "Small processing finished early or faded before the shared final tenth of a second");
                f.Step(ProcessingFade * .5f, false, false);
                float envelope = f.Arms.CurrentMediumRecycleFrame.ProcessingEnvelope01;
                Require(envelope > 0f && envelope < 1f
                    && Near((float)Get(f.Shake, "mediumRecycleProcessingEnvelope"), envelope * SmallProcessingFeedback)
                    && f.Recycled == 0 && item.LocalScale == scale && !f.Mover.IsArmInputCaptured,
                    "Small processing did not retain its reduced feedback strength throughout the shared final fade");
                f.Step(ProcessingFade * .5f + .001f, false, false);
                Require(f.Recycled == 1 && !item.gameObject.activeSelf && item.LocalScale == scale
                    && f.Arms.HeldObject == null && item.Owner == null && !f.Mover.IsArmInputCaptured
                    && Get(f.Arms, "mediumRecycleClip") == null && !(bool)Get(f.Arms, "recyclePresentationActive")
                    && PulseCount(f.Shake) == 0 && CameraSample(f.Shake) == Vector3.zero
                    && Motors(f.Shake, Time.time) == Vector2.zero
                    && f.Body.localPosition == Vector3.zero && f.Body.localRotation == Quaternion.identity,
                    "Small processing did not complete once after its shorter processing interval, with every feedback source cleared");
                f.Step(1f, false, false);
                Require(f.Recycled == 1 && PulseCount(f.Shake) == 0,
                    "Post-completion small processing replayed its inlet event or recycled twice");
            }
        }

        private static void CheckSharedProcessingSettings()
        {
            foreach (Vector4 settings in new[] { new Vector4(.70f, .45f, .20f, .15f), new Vector4(.30f, .70f, .08f, .50f) })
            foreach (bool small in new[] { false, true })
            using (var f = new Fixture())
            {
                WorldInteraction item = f.Garbage(small); Vector3 scale = item.LocalScale;
                f.Begin(item);
                // Change both clocks after starting. Each size must use its own duration,
                // while both retain the common claw-release minimum and final fade.
                Set(f.Arms, "mediumRecycleProcessingDuration", settings.x);
                Set(f.Arms, "smallRecycleProcessingDuration", settings.y);
                Set(f.Arms, "mediumRecycleProcessingFadeDuration", settings.z);
                Set(f.Arms, "mediumRecycleFinishDuration", settings.w);
                float feedDuration = small ? .35f : ProcessingStart;
                float tailDuration = Mathf.Max(small ? settings.y : settings.x, settings.w);
                f.Step(feedDuration + .137f, false, false);
                var clip = (MediumRecycleInletClip)Get(f.Arms, "mediumRecycleClip");
                Require(f.Arms.CurrentMediumRecycleFrame.Phase == MediumRecyclePhase.Processing
                    && Near(f.Arms.CurrentMediumRecycleFrame.ProcessingEnvelope01, 1f)
                    && f.Arms.CurrentMediumRecycleFrame.HandRelease01 > 0f
                    && f.Arms.CurrentMediumRecycleFrame.HandRelease01 < 1f
                    && item.Owner == f.Arms && !f.Mover.IsArmInputCaptured
                    && Near(f.Mover.GrabMovementMultiplier, 1f) && f.Recycled == 0
                    && clip.MaximumLocalY(f.Frame) <= (float)Get(f.Arms, "mediumRecycleClipY") + .0002f,
                    "A garbage size ignored its independent clock/shared release settings or exposed its ingested tail");
                f.Step(tailDuration - settings.z - .137f, false, false);
                Require(f.Recycled == 0 && f.Arms.State == RobotArmState.Recycling
                    && Near(f.Arms.CurrentMediumRecycleFrame.ProcessingEnvelope01, 1f),
                    "A garbage size faded early after its independent processing duration changed");
                f.Step(settings.z * .5f, false, false);
                float envelope = f.Arms.CurrentMediumRecycleFrame.ProcessingEnvelope01;
                Require(Near(envelope, .5f) && Near((float)Get(f.Shake, "mediumRecycleProcessingEnvelope"),
                    envelope * (small ? SmallProcessingFeedback : 1f))
                    && f.Recycled == 0 && item.LocalScale == scale,
                    "A garbage size did not use the changed common final-fade duration");
                f.Step(settings.z * .5f + .001f, false, false);
                Require(f.Recycled == 1 && f.Arms.HeldObject == null && !item.gameObject.activeSelf
                    && item.LocalScale == scale && CameraSample(f.Shake) == Vector3.zero
                    && Motors(f.Shake, Time.time) == Vector2.zero && PulseCount(f.Shake) == 0
                    && f.Body.localPosition == Vector3.zero && Get(f.Arms, "mediumRecycleClip") == null,
                    "An independent processing clock/shared release minimum did not determine its size's completion time");
            }
        }

        private static void CheckDestroyedDisable()
        {
            using (var f = new Fixture())
            {
                WorldInteraction item = f.Garbage();
                Material asset = item.GetComponent<SpriteRenderer>().sharedMaterial;
                f.Begin(item); f.Step(MediumRecycleMotion.StopTime(Clamp, Strokes, 0) + .01f);
                SpriteRenderer[] sprites = item.GetComponentsInChildren<SpriteRenderer>(true);
                var runtimeMaterials = new Material[sprites.Length];
                for (int i = 0; i < sprites.Length; i++) runtimeMaterials[i] = sprites[i].sharedMaterial;
                Require(PulseCount(f.Shake) > 0 && f.Arms.MediumRecycleHandVisualOffset.sqrMagnitude > 0f,
                    "Destroyed-disable fixture had no active presentation to clean");
                Object.DestroyImmediate(item.gameObject);
                // No Step in between: Unity's destroyed-object null must not skip disposal.
                Call(f.Arms, "OnDisable");
                Require(f.Arms.HeldObject == null && f.Arms.State == RobotArmState.Retracted
                    && !f.Mover.IsArmInputCaptured && f.Recycled == 0 && Get(f.Arms, "mediumRecycleClip") == null
                    && f.Arms.MediumRecycleHandVisualOffset == Vector2.zero
                    && f.Arms.MediumRecycleGarbageVisualOffset == Vector2.zero
                    && f.Body.localPosition == Vector3.zero && f.Body.localRotation == Quaternion.identity
                    && PulseCount(f.Shake) == 0 && Motors(f.Shake, Time.time) == Vector2.zero,
                    "Same-frame controller disable skipped cleanup after external waste destruction");
                foreach (Material material in runtimeMaterials)
                    Require(material == null, "A temporary inlet material survived external destruction and controller disable");
                Require(asset != null && asset.shader.name != MediumRecycleInletClip.ShaderName,
                    "Destroyed-item cleanup damaged the shared production material asset");
            }
        }

        private static void CheckIndependentChannels()
        {
            foreach (bool cameraOn in new[] { false, true })
            foreach (bool motorsOn in new[] { false, true })
            using (var f = new Fixture())
            {
                Set(f.Shake, "mediumRecycleCameraShakeEnabled", cameraOn);
                Set(f.Shake, "mediumRecycleRumbleEnabled", motorsOn);
                f.Shake.PlayMediumRecycleImpact(f.Arms, 1, Vector2.right);
                Vector2 camera = (Vector2)Get(f.Shake, "mediumRecycleCameraPositionVelocity");
                Vector2 motors = Motors(f.Shake, Time.time + .02f);
                Require((camera.sqrMagnitude > 0f) == cameraOn && (motors.sqrMagnitude > 0f) == motorsOn,
                    "Medium screen and controller switches were not independent");
                Require((Vector2)Get(f.Shake, "springPositionVelocity") == Vector2.zero
                    && (Vector2)Get(f.Shake, "garbageGrabCameraPositionVelocity") == Vector2.zero,
                    "Medium impact leaked into ordinary or grab feedback channels");
                f.Shake.CancelMediumRecycleFeedback(f.Arms); Call(f.Shake, "ResetShakeState");
                Set(f.Shake, "globalIntensity", 0f);
                f.Shake.PlayMediumRecycleImpact(f.Arms, 2, Vector2.right);
                Call(f.Shake, "IntegrateSprings", .04f); Call(f.Shake, "ApplyShakeToCamera");
                Require(Motors(f.Shake, Time.time + .02f) == Vector2.zero
                    && f.Shake.CurrentLocalPositionOffset == Vector2.zero && Near(f.Shake.CurrentRotationOffsetDegrees, 0f),
                    "Global silence left a medium recycle output");
            }
            using (var f = new Fixture())
            {
                Set(f.Shake, "enableCameraShake", false);
                f.Shake.PlayMediumRecycleImpact(f.Arms, 1, Vector2.right);
                Require((Vector2)Get(f.Shake, "mediumRecycleCameraPositionVelocity") == Vector2.zero
                    && Motors(f.Shake, Time.time + .02f).sqrMagnitude > 0f,
                    "Global screen-shake disabling also silenced the independent recycle motors");
                f.Shake.CancelMediumRecycleFeedback(f.Arms); Call(f.Shake, "ResetShakeState");
                Set(f.Shake, "enableCameraShake", true); Set(f.Shake, "enableGamepadRumble", false);
                f.Shake.PlayMediumRecycleImpact(f.Arms, 1, Vector2.right);
                Call(f.Shake, "IntegrateSprings", .04f); Call(f.Shake, "ApplyShakeToCamera");
                Require(PulseCount(f.Shake) == 0 && Motors(f.Shake, Time.time + .02f) == Vector2.zero
                    && f.Shake.CurrentLocalPositionOffset.sqrMagnitude > 0f
                    && (Vector2)Get(f.Shake, "regularRumblePositionOffset") == Vector2.zero
                    && Near((float)Get(f.Shake, "regularRumbleRotationOffsetDegrees"), 0f),
                    "Disabled global rumble either cut screen recoil or leaked it into ordinary motors");
            }
        }

        private static void CheckMotorOutputs()
        {
            using (var f = new Fixture())
            {
                Vector2 previous = Vector2.zero;
                for (int stage = 0; stage < 3; stage++)
                {
                    f.Shake.CancelMediumRecycleFeedback(f.Arms);
                    f.Shake.PlayMediumRecycleImpact(f.Arms, stage, Vector2.up);
                    Vector2 motors = Motors(f.Shake, Time.time + .02f);
                    Require(motors.x > previous.x && motors.y > previous.y,
                        "Later pressing strokes did not increase their motor force");
                    previous = motors;
                }
                f.Shake.CancelMediumRecycleFeedback(f.Arms);
                f.Shake.SetMediumRecycleEffort(f.Arms, 1f);
                Require(Motors(f.Shake, Time.time).sqrMagnitude > 0f, "Loaded claws have no continuous controller strain");
                f.Shake.PlayMediumRecycleImpact(f.Arms, 3, Vector2.up);
                f.Shake.FinishMediumRecycleFeedback(f.Arms);
                Vector2 tail = Motors(f.Shake, Time.time + .02f);
                Require(tail.sqrMagnitude > 0f && tail.x < previous.x && tail.y < previous.y,
                    "Completion cut off its confirmation tail or exceeded the third stop");
                Require(Motors(f.Shake, Time.time + 1f) == Vector2.zero,
                    "Completed recycling retained continuous effort or an expired motor pulse");
                f.Shake.CompleteMediumRecycleFeedback(f.Arms);
                Require(Motors(f.Shake, Time.time) == Vector2.zero && CameraSample(f.Shake) == Vector3.zero,
                    "Explicit completion retained a compatibility feedback tail");
            }
        }

        private static void CheckContinuousScreen()
        {
            using (var f = new Fixture())
            {
                float previousEnergy = 0f;
                for (int stage = 0; stage < 3; stage++)
                {
                    float energy = 0f;
                    foreach (float phase in new[] { .021f, .047f, .083f, .119f, .157f, .193f })
                    {
                        f.Shake.SetMediumRecycleContinuous(f.Arms, stage, 1f, phase, Vector2.down);
                        Vector3 sample = CameraSample(f.Shake);
                        energy += sample.x * sample.x + sample.y * sample.y + sample.z * sample.z * .01f;
                    }
                    Require(energy > previousEnergy && energy > 0f,
                        "Continuous screen pressing is missing or does not grow across stage " + stage);
                    previousEnergy = energy;
                }
                f.Shake.SetMediumRecycleContinuous(f.Arms, 1, 1f, .119f, Vector2.down);
                Vector3 full = CameraSample(f.Shake);
                f.Shake.SetMediumRecycleContinuous(f.Arms, 1, .5f, .119f, Vector2.down);
                Vector3 moderate = CameraSample(f.Shake);
                f.Shake.SetMediumRecycleContinuous(f.Arms, 1, 0f, .119f, Vector2.down);
                Vector3 idleEffort = CameraSample(f.Shake);
                Require(idleEffort.sqrMagnitude > 0f && idleEffort.sqrMagnitude < moderate.sqrMagnitude
                    && moderate.sqrMagnitude < full.sqrMagnitude,
                    "Pressing screen motion either loses its low-effort operation floor or fails to increase with effort");
                f.Shake.SetMediumRecycleContinuous(f.Arms, 3, 1f, .119f, Vector2.down);
                Require(CameraSample(f.Shake) == Vector3.zero,
                    "An invalid continuous pressing stage retained screen motion");
                f.Shake.SetMediumRecycleContinuous(f.Arms, -1, 0f, .119f, Vector2.down);
                Require(CameraSample(f.Shake) == Vector3.zero,
                    "Continuous pressing persists outside the loading/pressing phases");
            }
            foreach (bool screen in new[] { false, true })
            foreach (bool motors in new[] { false, true })
            using (var f = new Fixture())
            {
                Set(f.Shake, "mediumRecycleCameraShakeEnabled", screen);
                Set(f.Shake, "mediumRecycleRumbleEnabled", motors);
                f.Shake.SetMediumRecycleEffort(f.Arms, 1f);
                f.Shake.SetMediumRecycleContinuous(f.Arms, 1, 1f, .119f, Vector2.down);
                Require((CameraSample(f.Shake).sqrMagnitude > 0f) == screen
                    && (Motors(f.Shake, Time.time).sqrMagnitude > 0f) == motors,
                    "Continuous pressing coupled the screen and controller switches");
                f.Shake.SetMediumRecycleProcessing(f.Arms, 1f, .119f, Vector2.down);
                Require((CameraSample(f.Shake).sqrMagnitude > 0f) == screen
                    && (Motors(f.Shake, Time.time).sqrMagnitude > 0f) == motors,
                    "In-body processing coupled the screen and controller switches");
                Set(f.Shake, "globalIntensity", 0f);
                Require(CameraSample(f.Shake) == Vector3.zero && Motors(f.Shake, Time.time) == Vector2.zero,
                    "Global silence left a continuous pressing or processing output");
            }
        }

        private static void CheckProcessingFeedback()
        {
            Vector3 mediumScreen = Vector3.zero, mediumBody = Vector3.zero;
            Vector2 mediumMotors = Vector2.zero;
            Quaternion mediumBodyRotation = Quaternion.identity;
            foreach (bool small in new[] { false, true })
            using (var f = new Fixture())
            {
                WorldInteraction item = f.Garbage(small); Vector3 originalScale = item.LocalScale;
                float feedDuration = small ? .35f : ProcessingStart;
                f.Begin(item); f.Step(feedDuration - .01f, false, false);
                Require(f.Arms.State == RobotArmState.Recycling && f.Mover.IsArmInputCaptured
                    && Near(f.Mover.GrabMovementMultiplier, 1f - item.GrabResistance) && f.Recycled == 0,
                    "Feeding released the arm input reservation or held-item resistance before full ingestion");
                f.Step(.03f, false, false);
                var clip = (MediumRecycleInletClip)Get(f.Arms, "mediumRecycleClip");
                Require(f.Arms.CurrentMediumRecycleFrame.Phase == MediumRecyclePhase.Processing
                    && f.Recycled == 0 && item.Owner == f.Arms && !f.Mover.IsArmInputCaptured
                    && Near(f.Mover.GrabMovementMultiplier, 1f)
                    && clip.MaximumLocalY(f.Frame) <= (float)Get(f.Arms, "mediumRecycleInletY") + .0002f
                    && !(bool)Get(f.Arms, "previousGrab") && item.LocalScale == originalScale,
                    "Waste did not remain fully inside, with released claws, for its processing phase");
                Require(CameraSample(f.Shake).sqrMagnitude > 0f && Motors(f.Shake, Time.time).sqrMagnitude > 0f
                    && Near((float)Get(f.Shake, "mediumRecycleProcessingEnvelope"), small ? SmallProcessingFeedback : 1f),
                    "Fully ingested waste has no continuous screen/controller processing feedback");
                // Remove pre-ingestion impacts, then resample the controller's current frame.
                // Comparing real output at equal phase isolates processing strength from its inlet pulse.
                f.Shake.CancelMediumRecycleFeedback(f.Arms); f.Step(0f, false, false);
                if (!small)
                {
                    Set(f.Arms, "smallRecycleProcessingDuration", Finish);
                    Set(f.Arms, "smallRecycleProcessingFeedbackMultiplier", 0f);
                    f.Step(0f, false, false);
                    Require(Near((float)Get(f.Shake, "mediumRecycleProcessingEnvelope"), 1f),
                        "Changing the small-only feedback control attenuated medium processing");
                    mediumScreen = CameraSample(f.Shake); mediumMotors = Motors(f.Shake, Time.time);
                    mediumBody = f.Body.localPosition; mediumBodyRotation = f.Body.localRotation;
                }
                else
                {
                    Require(Vector3.Distance(CameraSample(f.Shake), mediumScreen * SmallProcessingFeedback) < .0002f,
                        "Default small processing screen motion was not forty percent weaker than unchanged medium processing");
                    NearVector(Motors(f.Shake, Time.time), mediumMotors * SmallProcessingFeedback,
                        "Default small processing motors were not forty percent weaker than unchanged medium processing");
                    Require(Vector3.Distance(f.Body.localPosition, mediumBody) < .0001f
                        && Quaternion.Angle(f.Body.localRotation, mediumBodyRotation) < .001f,
                        "The small screen/controller multiplier also weakened the shared body animation");
                    Set(f.Arms, "smallRecycleProcessingFeedbackMultiplier", 0f); f.Step(0f, false, false);
                    Require(CameraSample(f.Shake) == Vector3.zero && Motors(f.Shake, Time.time) == Vector2.zero
                        && f.Arms.CurrentMediumRecycleFrame.ProcessingEnvelope01 == 1f
                        && Vector3.Distance(f.Body.localPosition, mediumBody) < .0001f && f.Recycled == 0,
                        "A zero small feedback multiplier either retained output or silenced/stopped the shared body processing");
                    Set(f.Arms, "smallRecycleProcessingFeedbackMultiplier", 1f); f.Step(0f, false, false);
                    Require(Vector3.Distance(CameraSample(f.Shake), mediumScreen) < .0002f,
                        "Changing the small feedback multiplier to one did not restore full processing screen motion");
                    NearVector(Motors(f.Shake, Time.time), mediumMotors,
                        "Changing the small feedback multiplier to one did not restore full processing motors");
                    Require(PulseCount(f.Shake) == 0 && (bool)Get(f.Arms, "mediumRecycleCompletionPlayed"),
                        "Changing small processing strength replayed its already-fired completion impact");
                    Set(f.Arms, "smallRecycleProcessingFeedbackMultiplier", SmallProcessingFeedback); f.Step(0f, false, false);
                }
                CheckProcessingCollisionContract(f, item);
                Vector3 movingStart = f.Root.transform.position;
                Quaternion turningStart = f.Root.transform.rotation;
                Quaternion heldRotation = (Quaternion)Get(f.Arms, "heldRotation");
                Vector3 ingestedPosition = (Vector3)Get(f.Arms, "mediumRecycleEnd");
                const float dt = 1f / 60f;
                float forwardSpeed = (float)Call(f.Mover, "ScaleMotion", (float)Get(f.Mover, "forwardSpeed"));
                for (int frame = 0; frame < 12; frame++)
                {
                    // Controller runs first in production. L3/A changes may not retake the
                    // normal movement stick or steer the automatically finishing claws.
                    f.Step(dt, frame % 2 == 0, frame % 3 == 0);
                    Require(!f.Mover.IsArmInputCaptured && Near(f.Mover.GrabMovementMultiplier, 1f)
                        && f.Arms.State == RobotArmState.Recycling && item.Owner == f.Arms
                        && f.Arms.CurrentInputMagnitude == 0f && f.Arms.CurrentTargetLocal == Vector2.zero
                        && f.Recycled == 0,
                        "Processing recaptured drive input, reapplied held resistance or allowed arm/grab input to interrupt it");
                    // Use the same acceleration, turn and movement methods as ordinary drive,
                    // with an explicit clock instead of sampling connected input hardware.
                    Call(f.Mover, "UpdateDriveSpeed", 1f, forwardSpeed, 1f, 1f, 0f, dt);
                    Call(f.Mover, "StepTurning", .7f, false, dt);
                    Require((bool)Call(f.Mover, "TryMoveSafely", (Vector2)f.Root.transform.up * f.Mover.CurrentSpeed * dt,
                        item.transform, false), "Ordinary driving was blocked during in-body processing");
                    Call(f.Arms, "LateUpdate");
                    Require(Vector3.Distance(f.Frame.InverseTransformPoint(item.WorldPosition), ingestedPosition) < .0002f
                        && Quaternion.Angle(item.transform.rotation, f.Root.transform.rotation * heldRotation) < .001f
                        && clip.MaximumLocalY(f.Frame) <= (float)Get(f.Arms, "mediumRecycleClipY") + .0002f
                        && item.LocalScale == originalScale && f.Arms.HeldObject == item,
                        "Driving/turning exposed an ingested body/icon tail or displaced the recycling session");
                }
                Require(Vector3.Distance(f.Root.transform.position, movingStart) > .001f
                    && Quaternion.Angle(f.Root.transform.rotation, turningStart) > .1f
                    && f.Mover.CurrentSpeed > 0f && f.Mover.CurrentTurnSpeed > 0f,
                    "Ordinary movement and steering did not advance while processing stayed active");
                float processingTime = (float)Get(f.Arms, "recycleTime") - feedDuration;
                f.Step((small ? SmallProcessing : Processing) - ProcessingFade - processingTime, false, false);
                Require(f.Recycled == 0 && f.Arms.State == RobotArmState.Recycling
                    && !f.Mover.IsArmInputCaptured && Near(f.Mover.GrabMovementMultiplier, 1f)
                    && Near(f.Arms.CurrentMediumRecycleFrame.ProcessingEnvelope01, 1f),
                    "Processing finished early or faded before its final tenth of a second");
                f.Step(ProcessingFade * .5f, false, false);
                float envelope = f.Arms.CurrentMediumRecycleFrame.ProcessingEnvelope01;
                Require(envelope > 0f && envelope < 1f
                    && Near((float)Get(f.Shake, "mediumRecycleProcessingEnvelope"),
                        envelope * (small ? SmallProcessingFeedback : 1f))
                    && item.LocalScale == originalScale && f.Recycled == 0,
                    "Screen/controller processing did not use the same final fade envelope as the body animation");
                f.Step(ProcessingFade * .5f + .001f, false, false);
                Require(f.Recycled == 1 && f.Arms.HeldObject == null && !item.gameObject.activeSelf
                    && !f.Mover.IsArmInputCaptured && Near(f.Mover.GrabMovementMultiplier, 1f) && f.Mover.CurrentSpeed > 0f
                    && PulseCount(f.Shake) == 0 && CameraSample(f.Shake) == Vector3.zero
                    && Motors(f.Shake, Time.time) == Vector2.zero
                    && f.Body.localPosition == Vector3.zero && f.Body.localRotation == Quaternion.identity,
                    "Processing completion retained a camera, motor or body tail");
            }
            using (var f = new Fixture())
            {
                f.Shake.PlayMediumRecycleImpact(f.Arms, 3, Vector2.down);
                f.Shake.SetMediumRecycleProcessing(f.Arms, 1f, .137f, Vector2.down);
                Vector3 fullScreen = CameraSample(f.Shake); Vector2 fullMotor = Motors(f.Shake, Time.time);
                Require(fullScreen.sqrMagnitude > 0f && fullMotor.sqrMagnitude > 0f,
                    "Pure processing fade fixture has no output");
                f.Shake.SetMediumRecycleProcessing(f.Arms, .5f, .137f, Vector2.down);
                Require(Vector3.Distance(CameraSample(f.Shake), fullScreen * .5f) < .0001f,
                    "Processing fade accumulated over frames or applied differently to screen sources");
                NearVector(Motors(f.Shake, Time.time), fullMotor * .5f,
                    "Processing fade did not scale continuous motors and the remaining inlet pulse together");
                f.Shake.SetMediumRecycleProcessing(f.Arms, .5f, .137f, Vector2.down);
                NearVector(Motors(f.Shake, Time.time), fullMotor * .5f,
                    "Repeating one processing frame compounded its envelope");
                f.Shake.SetMediumRecycleProcessing(f.Arms, 0f, .137f, Vector2.down);
                Require(CameraSample(f.Shake) == Vector3.zero && Motors(f.Shake, Time.time) == Vector2.zero
                    && PulseCount(f.Shake) == 0, "Zero processing envelope retained a spring or event source");
                f.Shake.CompleteMediumRecycleFeedback(f.Arms);
                Require(CameraSample(f.Shake) == Vector3.zero && Motors(f.Shake, Time.time) == Vector2.zero,
                    "Final completion regenerated a processing tail");
            }
        }

        private static void CheckProcessingCollisionContract(Fixture f, WorldInteraction item)
        {
            float diameter = f.Root.GetComponent<RobotMarkerView>().BodyDiameter;
            Vector2 origin = f.Root.transform.position, endpoint = origin;
            foreach (string side in new[] { "left", "right" })
            {
                object arm = Get(f.Arms, side), pose = Get(arm, "Pose");
                for (int segment = 0; segment < 2; segment++)
                {
                    InteractionShape shape = (InteractionShape)Call(f.Arms, "SegmentShape", arm, pose, segment);
                    Vector2 candidate = (shape.B + shape.C) * .5f;
                    if ((candidate - origin).sqrMagnitude > (endpoint - origin).sqrMagnitude) endpoint = candidate;
                }
            }
            float clearance = Vector2.Distance(endpoint, origin) - diameter * .5f;
            float armWidth = (float)Get(f.Arms, "armWidth");
            Require(clearance > armWidth * 2f, "Production processing fixture has no arm-only endpoint clear of the physical body");
            Vector2 movement = (endpoint - origin).normalized * Mathf.Min(diameter * .15f, clearance * .5f);
            var armWallRoot = new GameObject("Processing arm-only contact"); SceneManager.MoveGameObjectToScene(armWallRoot, f.Root.scene);
            var armWall = armWallRoot.AddComponent<WorldInteraction>();
            armWall.SetKind(WorldInteractionKind.Collision); armWall.LocalSize = Vector2.one * armWidth * .25f;
            armWall.WorldPosition = endpoint + movement;
            Require((bool)Call(f.Mover, "TryMoveSafely", movement, item.transform, true),
                "An obstacle clear of the physical body blocked processing movement");
            Vector3 committed = f.Root.transform.position;
            bool armContact = false;
            foreach (string side in new[] { "left", "right" })
            {
                object arm = Get(f.Arms, side), pose = Get(arm, "Pose");
                for (int segment = 0; segment < 2; segment++)
                    armContact |= WorldInteractionQuery.Query((InteractionShape)Call(f.Arms, "SegmentShape", arm, pose, segment),
                        WorldInteractionKind.Collision, null, f.Root.scene, ignore: f.Root.transform, ignoreHeld: item.transform);
            }
            Require(armContact, "Processing arm-only fixture did not intersect an actual deployed arm segment");
            Call(f.Arms, "LateUpdate");
            Require(Vector3.Distance(f.Root.transform.position, committed) < .0001f,
                "Processing arm sweep rolled back a move accepted by the physical-body safety check");
            armWall.enabled = false;

            var bodyWallRoot = new GameObject("Processing physical-body wall"); SceneManager.MoveGameObjectToScene(bodyWallRoot, f.Root.scene);
            var bodyWall = bodyWallRoot.AddComponent<WorldInteraction>();
            bodyWall.SetKind(WorldInteractionKind.BodyCollision);
            bodyWall.LocalSize = new Vector2(diameter * 3f, diameter * .02f);
            bodyWall.WorldPosition = f.Root.transform.position + f.Root.transform.up * diameter * .60f;
            bodyWall.WorldRotation = f.Root.transform.rotation;
            Require(!(bool)Call(f.Mover, "TryMoveSafely", (Vector2)f.Root.transform.up * diameter * .2f, item.transform, true)
                && Vector3.Distance(f.Root.transform.position, committed) < .0001f && item.Owner == f.Arms,
                "Processing bypassed the mover's body-obstacle safety or released the owned waste");
            bodyWall.enabled = false;
        }

        private static void CheckMotorComposition()
        {
            using (var f = new Fixture())
            {
                Type adaptive = typeof(RobotCameraShake).Assembly.GetType("AnimalGame.RobotMap.AdaptiveGamepadRumble", true);
                Vector2 regular = new(.4f, .2f), grab = new(.3f, .6f), heavy = new(.8f, .5f), medium = new(.55f, .3f);
                object ordinaryCalibration = Call(f.Shake, "CreateSonyRumbleCalibration", false, false);
                object grabCalibration = Call(f.Shake, "CreateSonyGrabRumbleCalibration");
                object heavyCalibration = Call(f.Shake, "CreateSonyHeavyBreakRumbleCalibration");
                object mediumCalibration = Call(f.Shake, "CreateSonyMediumRecycleRumbleCalibration");
                MethodInfo compose = adaptive.GetMethod("ComposeAllFeedbackMotorSpeeds", Static);
                Vector2 xbox = (Vector2)compose.Invoke(null, new[] { (object)regular, grab, heavy, medium, false,
                    ordinaryCalibration, grabCalibration, heavyCalibration, mediumCalibration });
                NearVector(xbox, new Vector2(.8f, .6f), "Mixed recycle motors were summed or attenuated on Xbox");
                Vector2 Calibrate(object calibration, Vector2 input)
                {
                    object[] values = { input.x, input.y };
                    calibration.GetType().GetMethod("Apply", Instance).Invoke(calibration, values);
                    return new Vector2((float)values[0], (float)values[1]);
                }
                Vector2 expected = Vector2.Max(Vector2.Max(Calibrate(ordinaryCalibration, regular), Calibrate(grabCalibration, grab)),
                    Vector2.Max(Calibrate(heavyCalibration, heavy), Calibrate(mediumCalibration, medium)));
                Vector2 sony = (Vector2)compose.Invoke(null, new[] { (object)regular, grab, heavy, medium, true,
                    ordinaryCalibration, grabCalibration, heavyCalibration, mediumCalibration });
                NearVector(sony, expected, "Sony channel calibration was applied after mixing or omitted for recycling");
            }
        }

        private static void CheckFeedbackLifecycle()
        {
            foreach (string reason in new[] { "focus", "pause" })
            using (var f = new Fixture())
            {
                f.Shake.SetMediumRecycleEffort(f.Arms, .8f); f.Shake.PlayMediumRecycleImpact(f.Arms, 0, Vector2.up);
                f.Shake.SetMediumRecycleContinuous(f.Arms, 0, .8f, .137f, Vector2.up);
                f.Shake.SetMediumRecycleProcessing(f.Arms, 1f, .137f, Vector2.up);
                Require(PulseCount(f.Shake) == 1, "Lifecycle fixture did not produce a loaded recycle pulse");
                if (reason == "focus") Call(f.Shake, "OnApplicationFocus", false);
                else Call(f.Shake, "OnApplicationPause", true);
                Require(PulseCount(f.Shake) == 0 && Motors(f.Shake, Time.time) == Vector2.zero
                    && CameraSample(f.Shake) == Vector3.zero,
                    "Lifecycle interruption left a recycle motor active: " + reason);
                if (reason == "focus") Call(f.Shake, "OnApplicationFocus", true);
                else Call(f.Shake, "OnApplicationPause", false);
                f.Shake.SetMediumRecycleEffort(f.Arms, .8f); f.Shake.PlayMediumRecycleImpact(f.Arms, 1, Vector2.up);
                f.Shake.SetMediumRecycleContinuous(f.Arms, 1, .8f, .137f, Vector2.up);
                f.Shake.SetMediumRecycleProcessing(f.Arms, 1f, .137f, Vector2.up);
                Require(PulseCount(f.Shake) == 0 && Motors(f.Shake, Time.time) == Vector2.zero
                    && CameraSample(f.Shake) == Vector3.zero,
                    "Resuming replayed the suppressed operation's recycle feedback");
                f.Shake.CancelMediumRecycleFeedback(f.Arms);
                f.Shake.SetMediumRecycleEffort(f.Arms, .8f); f.Shake.PlayMediumRecycleImpact(f.Arms, 0, Vector2.up);
                Require(PulseCount(f.Shake) == 1 && Motors(f.Shake, Time.time).sqrMagnitude > 0f,
                    "A new operation could not recover after cancellation of the suppressed one");
            }
        }

        private static int PulseCount(RobotCameraShake shake) => ((IList)Get(shake, "mediumRecyclePulses")).Count;
        private static Vector2 Motors(RobotCameraShake shake, float now) => (Vector2)Call(shake, "GetMediumRecycleMotorSpeeds", now);
        private static Vector3 CameraSample(RobotCameraShake shake) => (Vector3)Call(shake, "GetMediumRecycleCameraSample");
        private static SpriteRenderer HandSprite(RobotArmController arms, string side)
        { object arm = Get(arms, side); return (SpriteRenderer)arm.GetType().GetField("HandSprite").GetValue(arm); }
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Instance).SetValue(target, value);
        private static object Get(object target, string name) => target.GetType().GetField(name, Instance).GetValue(target);
        private static object Call(object target, string name, params object[] values) => target.GetType().GetMethod(name, Instance).Invoke(target, values);

        private sealed class Fixture : IDisposable
        {
            private readonly object armsFixture;
            public GameObject Root { get; }
            public RobotArmController Arms { get; }
            public RobotMover Mover { get; }
            public RobotCameraShake Shake { get; }
            public Transform Frame { get; }
            public Transform Body { get; }
            public int Recycled { get; private set; }
            public Fixture()
            {
                Type type = typeof(RobotArmRegressionChecks).GetNestedType("Fixture", BindingFlags.NonPublic);
                armsFixture = Activator.CreateInstance(type, Instance, null, new object[] { true }, null);
                Root = (GameObject)type.GetProperty("Root", Instance).GetValue(armsFixture);
                Arms = (RobotArmController)type.GetProperty("Arms", Instance).GetValue(armsFixture);
                Mover = Root.GetComponent<RobotMover>();
                Call(Mover, "Awake");
                RobotMarkerView marker = Root.GetComponent<RobotMarkerView>(); Frame = marker.MarkerVisualRoot;
                Body = Frame.Find("Body Visual");
                Require(Body != null && Body.GetComponentsInChildren<SpriteRenderer>(true).Length > 0,
                    "Production body artwork was not available to the recoil fixture");
                Set(marker, "bodyVisualRoot", Body);
                Set(marker, "mediumRecycleBodyBasePosition", Body.localPosition);
                Set(marker, "mediumRecycleBodyBaseRotation", Body.localRotation);
                var camera = new GameObject("Medium recycle regression camera"); SceneManager.MoveGameObjectToScene(camera, Root.scene);
                Shake = camera.AddComponent<RobotCameraShake>();
                EditorUtility.CopySerialized(AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/Resources/Camera/RobotCamera.prefab").GetComponent<RobotCameraShake>(), Shake);
                Call(Shake, "Awake"); Shake.Initialize(Mover, null, null); Set(Arms, "grabFeedback", Shake);
                for (int i = 0; i < 240; i++) Call(Arms, "Step", 1f / 60f, Vector2.up, true, true);
            }
            public WorldInteraction Garbage(bool small = false)
            {
                GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Environment/Garbage/"
                    + (small ? "Small" : "Medium") + "_Garbage.prefab");
                GameObject root = Object.Instantiate(source); SceneManager.MoveGameObjectToScene(root, Root.scene);
                var item = root.GetComponent<WorldInteraction>();
                item.WorldPosition = small ? Arms.LeftHandWorld : (Arms.LeftHandWorld + Arms.RightHandWorld) * .5f;
                Call(root.GetComponent<GarbageItem>(), "Awake");
                Call(Arms, "Step", 1f / 60f, Vector2.up, true, true);
                Require(Arms.HeldObject == item && item.Owner == Arms, "Fixture failed to grab production waste with the required claws");
                Call(Shake, "ClearGarbageGrabFeedback"); Call(Shake, "ResetShakeState");
                Set(item, "onRecycled", (Action)(() => Recycled++));
                return item;
            }
            public void Begin(WorldInteraction item)
            {
                bool small = item.Size == RecyclableSize.Small;
                Vector2 inlet = (float)Get(Arms, "armLength") * (Vector2)Get(Arms,
                    small ? "smallDockPosition" : "mediumDockPosition");
                int hands = (int)Get(Arms, "heldHands");
                Vector2 anchor = hands == 3 ? (Arms.LeftHandWorld + Arms.RightHandWorld) * .5f
                    : hands == 1 ? Arms.LeftHandWorld : Arms.RightHandWorld;
                item.WorldPosition = Frame.TransformPoint(inlet);
                Set(Arms, "heldOffset", (Vector2)Frame.InverseTransformVector(item.WorldPosition - (Vector3)anchor));
                Call(Arms, "Step", 0f, Vector2.right, true, false);
                Require(Arms.State == RobotArmState.Recycling, "Fixture chest-zone release failed to start recycling");
            }
            public void Step(float dt, bool deploy = true, bool grab = false) => Call(Arms, "Step", dt, Vector2.right, deploy, grab);
            public void Dispose()
            {
                Shake.CancelMediumRecycleFeedback(Arms); Call(Shake, "ClearGarbageGrabFeedback");
                ((IDisposable)armsFixture).Dispose();
            }
        }
    }
}
