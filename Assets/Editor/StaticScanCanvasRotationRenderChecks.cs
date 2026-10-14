using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    /// <summary>
    /// Pixel regression through Unity's actual overlay CanvasRenderer list.
    /// This deliberately does not populate a mesh by reflection or draw that
    /// mesh with Graphics.DrawMeshNow. It retains the production overlay mode.
    /// Use only in a disposable batch editor because overlay lists are global.
    /// </summary>
    public static class StaticScanCanvasRotationRenderChecks
    {
        private const int Size = 512;
        private const float ReferenceOrthographicSize = 5f;
        private static readonly Vector2 WorldOrigin = new Vector2(120f, 100f);
        private const string OutputDirectory = "output/StaticScanCanvasRotationRenderChecks";

        public static void RunBatch()
        {
            try
            {
                Require(Application.isBatchMode,
                    "Run the canvas pixel check in an isolated batch editor; do not replace a live editor scene.");
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "The fixture runs in Edit Mode.");
                Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null,
                    "Canvas pixel checks need a graphics device; omit -nographics.");
                Directory.CreateDirectory(OutputDirectory);
                using (var fixture = new Fixture())
                {
                    // The first render request initializes native Canvas and
                    // pipeline render state in a fresh batch editor. Compare
                    // stable frames after this warmup, never that first frame.
                    fixture.Capture("warmup-000", 0f, 5f, 0f, false);
                    Color32[] baseline = fixture.Capture("view-000", 0f, 5f, 0f, false);
                    AssertGoldPixels(baseline, "view-000");
                    foreach (float angle in new[] { 45f, 90f, 180f, 270f })
                    {
                        string name = "view-" + angle.ToString("000");
                        Color32[] actual = fixture.Capture(name, angle, 5f, 0f, false);
                        AssertWorldFixedDirection(baseline, actual, angle, name);
                        if (Mathf.Abs(angle % 90f) < .01f)
                            AssertRotatedFootprint(baseline, actual, Mathf.RoundToInt(angle / 90f), name);
                    }
                    AssertWorldFixedDirection(baseline,
                        fixture.Capture("view-090-zoom-8", 90f, 8f, 0f, false), 90f, "view-090-zoom-8");
                    Color32[] view180 = fixture.Capture("view-180-parent-0", 180f, 5f, 0f, false);
                    AssertStablePixels(view180,
                        fixture.Capture("view-180-parent-37", 180f, 5f, 37f, false), "view-180-parent-37");
                    Color32[] view270 = fixture.Capture("view-270-parent-0", 270f, 5f, 0f, false);
                    AssertStablePixels(view270,
                        fixture.Capture("view-270-parent-late-90", 270f, 5f, 90f, true),
                        "view-270-parent-late-90");

                    // A lattice rebuilt in the current pixel projection can
                    // appear stable at the origin and exact quarter turns,
                    // while its phase slips badly at distant world positions.
                    // Every frame below checks the real GPU pixels against the
                    // same independently generated world endpoints.
                    fixture.Capture("move-positive", 0f, 5f, 0f, false, new Vector2(.8f, -.6f));
                    fixture.Capture("move-negative", 0f, 5f, 0f, false, new Vector2(-.8f, .6f));
                    for (int frame = 0; frame < 16; frame++)
                    {
                        float phase = frame * Mathf.PI * 2f / 16f;
                        fixture.Capture("shake-" + frame.ToString("00"),
                            Mathf.Sin(phase) * .4f,
                            ReferenceOrthographicSize + Mathf.Cos(phase) * .025f,
                            0f, false, new Vector2(Mathf.Cos(phase) * .06f, Mathf.Sin(phase) * .04f));
                    }
                    AssertIdenticalPixels(baseline,
                        fixture.Capture("returned-original-pose", 0f, 5f, 0f, false),
                        "returned-original-pose");
                }
                Debug.Log("Static scan overlay Canvas pixel checks PASS: actual ScreenSpaceOverlay CanvasRenderer "
                    + "list, disconnected perpendicular grid projected through RobotCameraFollow at 0/45/90/180/270 degrees, "
                    + "fourth-harmonic GPU direction, rotating asymmetric footprint, zoom, no extra inherited UI rotation, "
                    + "a parent rotation after mesh rebuild, translation and tiny shake/zoom at world (120,100), "
                    + "independent world-bar GPU pixel coverage, stable connected bar count and exact return-to-pose pixels. Device: "
                    + SystemInfo.graphicsDeviceType
                    + ". Images: " + Path.GetFullPath(OutputDirectory));
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        private static bool IsGold(Color32 pixel) => pixel.r > 35 && pixel.g > 25
            && pixel.r > pixel.b + 15 && pixel.g > pixel.b + 10;

        private static void AssertGoldPixels(Color32[] pixels, string name)
        {
            int gold = 0;
            foreach (Color32 pixel in pixels) if (IsGold(pixel)) gold++;
            Require(gold > 600, name + " has insufficient real Canvas gold pixels: " + gold);
        }

        private static float PixelGridAngle(Color32[] pixels)
        {
            // Two perpendicular families cancel the ordinary second-harmonic
            // image structure tensor. The fourth harmonic combines both
            // families and their square caps into one direction modulo 90°.
            // This uses final GPU pixels, rather than the generated vertices.
            double real = 0, imaginary = 0, energy = 0;
            for (int y = 1; y < Size - 1; y++)
            for (int x = 1; x < Size - 1; x++)
            {
                int index = y * Size + x;
                double dx = pixels[index + 1].r - pixels[index - 1].r;
                double dy = pixels[index + Size].r - pixels[index - Size].r;
                double xx = dx * dx, yy = dy * dy, weight = xx + yy;
                if (weight < 1) continue;
                real += (xx * xx - 6 * xx * yy + yy * yy) / weight;
                imaginary += 4 * dx * dy * (xx - yy) / weight;
                energy += weight;
            }
            double coherence = Math.Sqrt(real * real + imaginary * imaginary) / Math.Max(1, energy);
            Require(coherence > .2, "The final GPU pixels do not contain a coherent pair of perpendicular grid directions.");
            return (float)(.25 * Math.Atan2(imaginary, real) * 180 / Math.PI);
        }

        private static void AssertStablePixels(Color32[] expected, Color32[] actual, string name)
        {
            Require(expected.Length == actual.Length, name + " changed the capture dimensions.");
            AssertGoldPixels(actual, name);
            int union = 0, differences = 0;
            for (int index = 0; index < expected.Length; index++)
            {
                bool first = IsGold(expected[index]), second = IsGold(actual[index]);
                if (first || second) union++;
                if (first != second) differences++;
            }
            float changed = union > 0 ? (float)differences / union : 1f;
            float expectedAngle = PixelGridAngle(expected), actualAngle = PixelGridAngle(actual);
            Debug.Log(name + " Canvas GPU grid angle (modulo 90): " + actualAngle.ToString("0.00")
                + " degrees; baseline " + expectedAngle.ToString("0.00")
                + " degrees; changed gold pixels " + (changed * 100f).ToString("0.00") + "%.");
            Require(Mathf.Abs(Mathf.DeltaAngle(expectedAngle * 4f, actualAngle * 4f)) < 2f,
                name + " changed the line direction measured from final Canvas GPU pixels.");
            Require(changed < .025f, name + " changed " + (changed * 100f).ToString("0.00")
                + "% of the final rendered gold pattern; the lines rotated, scaled or moved.");
        }

        private static void AssertIdenticalPixels(Color32[] expected, Color32[] actual, string name)
        {
            Require(expected.Length == actual.Length, name + " changed capture dimensions.");
            int differences = 0;
            for (int index = 0; index < expected.Length; index++)
                if (!expected[index].Equals(actual[index])) differences++;
            Require(differences == 0, name + " failed to return to the original rendered lattice: "
                + differences + " different RGBA pixels.");
            Debug.Log(name + " restored the original GPU image exactly (zero changed RGBA pixels).");
        }

        private static int CountGoldComponents(Color32[] pixels)
        {
            var visited = new bool[pixels.Length];
            var pending = new int[pixels.Length];
            int components = 0;
            for (int seed = 0; seed < pixels.Length; seed++)
            {
                if (visited[seed] || !IsGold(pixels[seed])) continue;
                components++;
                int remaining = 1;
                pending[0] = seed;
                visited[seed] = true;
                while (remaining > 0)
                {
                    int index = pending[--remaining], x = index % Size, y = index / Size;
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nextX = x + dx, nextY = y + dy;
                        if (nextX < 0 || nextX >= Size || nextY < 0 || nextY >= Size) continue;
                        int next = nextY * Size + nextX;
                        if (visited[next] || !IsGold(pixels[next])) continue;
                        visited[next] = true;
                        pending[remaining++] = next;
                    }
                }
            }
            return components;
        }

        private static void AssertWorldFixedDirection(Color32[] baseline, Color32[] actual,
            float cameraAngle, string name)
        {
            AssertGoldPixels(actual, name);
            float baselineAngle = PixelGridAngle(baseline), actualAngle = PixelGridAngle(actual);
            float expectedAngle = baselineAngle - cameraAngle;
            float difference = Mathf.Abs(Mathf.DeltaAngle(expectedAngle * 4f, actualAngle * 4f)) * .25f;
            Debug.Log(name + " Canvas GPU grid angle (modulo 90): " + actualAngle.ToString("0.00")
                + " degrees; expected world-fixed projection " + expectedAngle.ToString("0.00")
                + " degrees; directional difference " + difference.ToString("0.00") + " degrees.");
            // Gradients have a small rasterization bias. A 45° view rotation
            // remains decisive for two orthogonal families; 90° exchanges the
            // families, so those cases also check the rotated footprint below.
            Require(difference < 7f,
                name + " did not rotate on screen with the fixed world direction.");
        }

        private static void AssertRotatedFootprint(Color32[] baseline, Color32[] actual,
            int clockwiseQuarterTurns, string name)
        {
            int union = 0, differences = 0, unrotatedUnion = 0, unrotatedDifferences = 0;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int rotatedX = x, rotatedY = y;
                for (int turn = 0; turn < clockwiseQuarterTurns; turn++)
                {
                    int previousX = rotatedX;
                    rotatedX = rotatedY;
                    rotatedY = Size - 1 - previousX;
                }
                bool expected = IsGold(baseline[y * Size + x]);
                bool rotated = IsGold(actual[rotatedY * Size + rotatedX]);
                if (expected || rotated) union++;
                if (expected != rotated) differences++;
                bool unrotated = IsGold(actual[y * Size + x]);
                if (expected || unrotated) unrotatedUnion++;
                if (expected != unrotated) unrotatedDifferences++;
            }
            float mismatch = (float)differences / Mathf.Max(1, union);
            float unrotatedMismatch = (float)unrotatedDifferences / Mathf.Max(1, unrotatedUnion);
            Debug.Log(name + " rotated asymmetric GPU footprint mismatch: " + (mismatch * 100f).ToString("0.00")
                + "%; unrotated mismatch: " + (unrotatedMismatch * 100f).ToString("0.00") + "%.");
            Require(mismatch < .1f, name + " did not project the same world grid and footprint through the rotated camera.");
            if (clockwiseQuarterTurns % 2 == 1)
                Require(unrotatedMismatch > .1f,
                    name + " left the asymmetric footprint fixed on screen; a 90° grid-angle equivalence hid its rotation.");
        }

        private sealed class Fixture : IDisposable
        {
            private readonly Scene scene;
            private readonly Camera camera;
            private readonly Transform player;
            private readonly RobotCameraFollow cameraFollow;
            private readonly BioScanStaticAreaUI overlay;
            private readonly Canvas canvas;
            private readonly BioScanStaticAreaGraphic graphic;
            private readonly RectTransform rotationParent;
            private readonly Texture2D texture;
            private readonly Sprite sprite;
            private readonly RenderTexture cameraTarget, canvasTarget;
            private readonly List<StaticScanLineSegment> referenceWorldSegments =
                new List<StaticScanLineSegment>();
            private bool drewOverlay;

            public Fixture()
            {
                // A normal empty scene allows native uGUI to register its actual
                // overlay renderer. A preview scene can suppress overlay lists.
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                player = Create("Canvas pixel check player").transform;
                player.position = WorldOrigin;
                camera = Create("Canvas pixel check follow camera").AddComponent<Camera>();
                camera.transform.position = new Vector3(0f, 0f, -10f);
                camera.orthographic = true;
                camera.orthographicSize = 5f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.cullingMask = 0;
                cameraTarget = NewTarget("Canvas check camera render request");
                canvasTarget = NewTarget("Canvas check overlay pixels");
                camera.targetTexture = cameraTarget;
                camera.pixelRect = new Rect(0f, 0f, Size, Size);
                cameraFollow = camera.gameObject.AddComponent<RobotCameraFollow>();
                cameraFollow.Target = player;
                cameraFollow.SnapToTarget();

                texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                sprite = Sprite.Create(texture, new Rect(0f, 0f, 64f, 64f), Vector2.one * .5f, 16f);
                GameObject targetObject = Create("Canvas pixel check scan target");
                targetObject.transform.position = WorldOrigin;
                targetObject.transform.localScale = new Vector3(1.5f, 1f, 1f);
                targetObject.transform.rotation = Quaternion.Euler(0f, 0f, 23f);
                targetObject.AddComponent<SpriteRenderer>().sprite = sprite;
                var target = targetObject.AddComponent<ScannableInteractable>();
                Set(target, "customNormalizedOutline", new[] {
                    new Vector2(-.5f, -.35f), new Vector2(-.3f, .5f),
                    new Vector2(.5f, .3f), new Vector2(.4f, -.5f) });
                Set(target, "lineWidthPixels", 1.4f);
                Set(target, "lineSpacingPixels", 15.4f);
                Set(target, "intersectionGapPixels", 5.6f);
                Call(target, "OnEnable");
                BuildIndependentWorldReference(target);
                overlay = Create("Canvas pixel check static area presenter").AddComponent<BioScanStaticAreaUI>();
                overlay.Initialize(null, camera);
                // Runtime subscribes here; Edit Mode does not promise the
                // MonoBehaviour callbacks for non-ExecuteAlways components.
                Call(overlay, "OnEnable");
                overlay.ShowScannedInteractable(target);
                // Advance beyond the subtle initial fade-in so this fixture
                // tests visible bars, rather than the transparent reveal frame.
                typeof(ScannableInteractable).GetMethod("TickReveal", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(target, new object[] { .25f });
                Call(overlay, "TickAreas");
                canvas = (Canvas)Get(overlay, "areaCanvas");
                Require(canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay,
                    "Production presenter did not create its overlay Canvas.");
                Debug.Log("Overlay Canvas pixel rect: " + canvas.pixelRect
                    + "; Screen dimensions: " + Screen.width + "x" + Screen.height
                    + "; capture target: " + Size + "x" + Size + ".");
                IList graphics = (IList)Get(overlay, "areaGraphics");
                Require(graphics.Count > 0, "Production presenter did not create a graphic.");
                graphic = (BioScanStaticAreaGraphic)graphics[0];
                GameObject parentObject = new GameObject("Canvas pixel check late rotation pivot", typeof(RectTransform));
                rotationParent = parentObject.GetComponent<RectTransform>();
                rotationParent.SetParent(canvas.transform, false);
                rotationParent.anchorMin = Vector2.zero;
                rotationParent.anchorMax = Vector2.one;
                rotationParent.offsetMin = rotationParent.offsetMax = Vector2.zero;
                graphic.rectTransform.SetParent(rotationParent, false);
                graphic.rectTransform.anchorMin = Vector2.zero;
                graphic.rectTransform.anchorMax = Vector2.one;
                graphic.rectTransform.offsetMin = graphic.rectTransform.offsetMax = Vector2.zero;
                Canvas.ForceUpdateCanvases();
            }

            public Color32[] Capture(string name, float angle, float size, float parentAngle, bool rotateAfterRebuild,
                Vector2 cameraOffset = default)
            {
                player.position = WorldOrigin + cameraOffset;
                player.rotation = Quaternion.Euler(0f, 0f, angle);
                cameraFollow.SnapToTarget();
                Require(Mathf.Abs(Mathf.DeltaAngle(camera.transform.eulerAngles.z, angle)) < .01f,
                    "RobotCameraFollow did not apply the player view rotation.");
                camera.orthographicSize = size;
                rotationParent.localRotation = Quaternion.Euler(0f, 0f, rotateAfterRebuild ? 0f : parentAngle);
                Canvas.ForceUpdateCanvases();
                Call(overlay, "TickAreas");
                Canvas.ForceUpdateCanvases();
                if (rotateAfterRebuild) rotationParent.localRotation = Quaternion.Euler(0f, 0f, parentAngle);
                LogCaptureState(name, "before-render");
                drewOverlay = false;
                RenderPipelineManager.endCameraRendering += DrawActualOverlay;
                try
                {
                    var request = new RenderPipeline.StandardRequest { destination = cameraTarget };
                    RenderPipeline.SubmitRenderRequest(camera, request);
                }
                finally { RenderPipelineManager.endCameraRendering -= DrawActualOverlay; }
                Require(drewOverlay, "URP did not provide a camera rendering context for the overlay capture.");
                LogCaptureState(name, "after-render");
                RenderTexture previous = RenderTexture.active;
                var snapshot = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                try
                {
                    RenderTexture.active = canvasTarget;
                    snapshot.ReadPixels(new Rect(0f, 0f, Size, Size), 0, 0);
                    snapshot.Apply();
                    File.WriteAllBytes(Path.Combine(OutputDirectory, name + ".png"), snapshot.EncodeToPNG());
                    Color32[] pixels = snapshot.GetPixels32();
                    if (!name.StartsWith("warmup", StringComparison.Ordinal))
                        AssertProjectedReferencePixels(pixels, name);
                    return pixels;
                }
                finally
                {
                    RenderTexture.active = previous;
                    Object.DestroyImmediate(snapshot);
                }
            }

            private void BuildIndependentWorldReference(ScannableInteractable target)
            {
                // No presenter layout, UI mesh or shader metadata is read here.
                // Generate once in the normal view's metric, then retain world
                // endpoints as an independent oracle for all final GPU frames.
                var offsets = new List<Vector2>();
                Require(target.TryGetPresentationFootprint(offsets, out Vector3 center),
                    "The pixel fixture target did not supply a world footprint.");
                float pixelsPerWorldUnit = Size / (ReferenceOrthographicSize * 2f);
                for (int index = 0; index < offsets.Count; index++) offsets[index] *= pixelsPerWorldUnit;
                var referenceBars = new List<StaticScanLineSegment>();
                Require(BioScanStaticLinePattern.Build(offsets, 15.4f, -37f, referenceBars,
                    5.6f, 1.4f, -(Vector2)center * pixelsPerWorldUnit),
                    "The independent normal-view lattice did not produce bars.");
                foreach (StaticScanLineSegment bar in referenceBars)
                    referenceWorldSegments.Add(new StaticScanLineSegment(
                        (Vector2)center + bar.Start / pixelsPerWorldUnit,
                        (Vector2)center + bar.End / pixelsPerWorldUnit));
                Require(referenceWorldSegments.Count > 20,
                    "The distant pixel fixture needs enough independent grid bars to expose phase changes.");
            }

            private void AssertProjectedReferencePixels(Color32[] pixels, string name)
            {
                var expectedDistance = new float[pixels.Length];
                for (int index = 0; index < expectedDistance.Length; index++)
                    expectedDistance[index] = float.PositiveInfinity;
                int interiorPixels = 0, missingInterior = 0;
                foreach (StaticScanLineSegment worldBar in referenceWorldSegments)
                {
                    Vector2 start = camera.WorldToScreenPoint((Vector3)worldBar.Start);
                    Vector2 end = camera.WorldToScreenPoint((Vector3)worldBar.End);
                    Vector2 middle = (start + end) * .5f;
                    Vector2 direction = (end - start).normalized;
                    Vector2 normal = new Vector2(-direction.y, direction.x);
                    float halfLength = (end - start).magnitude * .5f;
                    int minimumX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(start.x, end.x) - 3f), 0, Size - 1);
                    int maximumX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(start.x, end.x) + 3f), 0, Size - 1);
                    int minimumY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(start.y, end.y) - 3f), 0, Size - 1);
                    int maximumY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(start.y, end.y) + 3f), 0, Size - 1);
                    int barInterior = 0, barMissing = 0;
                    for (int y = minimumY; y <= maximumY; y++)
                    for (int x = minimumX; x <= maximumX; x++)
                    {
                        Vector2 delta = new Vector2(x + .5f, y + .5f) - middle;
                        Vector2 box = new Vector2(Mathf.Abs(Vector2.Dot(delta, direction)) - halfLength,
                            Mathf.Abs(Vector2.Dot(delta, normal)) - .7f);
                        float distance = Vector2.Max(box, Vector2.zero).magnitude
                            + Mathf.Min(Mathf.Max(box.x, box.y), 0f);
                        int index = y * Size + x;
                        expectedDistance[index] = Mathf.Min(expectedDistance[index], distance);
                        if (distance >= -.35f) continue;
                        barInterior++;
                        if (!IsGold(pixels[index])) barMissing++;
                    }
                    Require(barInterior > 2, name + " moved a reference bar outside the fixture's measurable interior.");
                    Require(barMissing <= Mathf.Max(1, barInterior / 20), name
                        + " lost the lit interior of a fixed world bar; camera motion changed the displayed lattice.");
                    interiorPixels += barInterior;
                    missingInterior += barMissing;
                }
                int goldPixels = 0, unexpectedGold = 0;
                for (int index = 0; index < pixels.Length; index++)
                {
                    if (!IsGold(pixels[index])) continue;
                    goldPixels++;
                    if (expectedDistance[index] > .65f) unexpectedGold++;
                }
                int components = CountGoldComponents(pixels);
                Debug.Log(name + " independently projected world lattice: " + referenceWorldSegments.Count
                    + " bars / " + components + " GPU components; missing interior " + missingInterior
                    + "/" + interiorPixels + "; unexpected gold " + unexpectedGold + "/" + goldPixels + ".");
                Require(missingInterior <= Mathf.Max(2, interiorPixels / 200),
                    name + " does not cover the continuously projected interiors of the reference world lattice.");
                Require(unexpectedGold <= Mathf.Max(2, goldPixels / 200),
                    name + " introduced gold pixels away from the continuously projected reference world lattice.");
                Require(components == referenceWorldSegments.Count, name + " changed the actual visible bar count: "
                    + components + " GPU components instead of " + referenceWorldSegments.Count + ".");
            }

            private void LogCaptureState(string name, string phase)
            {
                Material material = (Material)Get(overlay, "areaMaterial");
                Debug.Log(name + " " + phase + ": camera.pixelRect=" + camera.pixelRect
                    + "; canvas.pixelRect=" + canvas.pixelRect
                    + "; Screen=" + Screen.width + "x" + Screen.height
                    + "; canvas.rect=" + ((RectTransform)canvas.transform).rect
                    + "; material=" + material.GetInstanceID()
                    + "; _AbsoluteScreenPlacement=" + (material.HasProperty("_AbsoluteScreenPlacement")
                        ? material.GetFloat("_AbsoluteScreenPlacement").ToString() : "absent")
                    + "; _ScreenViewportPixels=" + (material.HasProperty("_ScreenViewportPixels")
                        ? material.GetVector("_ScreenViewportPixels").ToString() : "absent")
                    + "; _ScreenParams(global)=" + Shader.GetGlobalVector("_ScreenParams") + ".");
            }

            private void DrawActualOverlay(ScriptableRenderContext context, Camera renderedCamera)
            {
                if (renderedCamera != camera) return;
                // This is Unity's actual UI draw path from URP's
                // DrawScreenSpaceUIPass, with an explicit disposable target.
                using (var command = new CommandBuffer { name = "Actual static scan overlay Canvas pixels" })
                {
                    command.SetRenderTarget(canvasTarget);
                    command.ClearRenderTarget(true, true, Color.black);
                    command.DrawRendererList(context.CreateUIOverlayRendererList(camera));
                    context.ExecuteCommandBuffer(command);
                }
                context.Submit();
                drewOverlay = true;
            }

            private GameObject Create(string name)
            {
                var result = new GameObject(name);
                SceneManager.MoveGameObjectToScene(result, scene);
                return result;
            }

            private static RenderTexture NewTarget(string name)
            {
                var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32)
                { name = name, hideFlags = HideFlags.HideAndDontSave, antiAliasing = 1 };
                target.Create();
                return target;
            }

            public void Dispose()
            {
                RenderPipelineManager.endCameraRendering -= DrawActualOverlay;
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (ScannableInteractable target in root.GetComponentsInChildren<ScannableInteractable>(true))
                        Call(target, "OnDisable");
                camera.targetTexture = null;
                foreach (GameObject root in scene.GetRootGameObjects()) Object.DestroyImmediate(root);
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
                cameraTarget.Release();
                canvasTarget.Release();
                Object.DestroyImmediate(cameraTarget);
                Object.DestroyImmediate(canvasTarget);
            }
        }

        private static object Get(object target, string name) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static object Call(object target, string name) => target.GetType()
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
                | BindingFlags.DeclaredOnly).Invoke(target, null);
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
