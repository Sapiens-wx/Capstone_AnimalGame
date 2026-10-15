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
        private const float LineWidth = .9f;
        private const int GoldMinRed = 35, GoldMinGreen = 25, GoldRedBlueGap = 15, GoldGreenBlueGap = 10;
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
                    AssertSnapshotDirection(baseline, fixture.ReferenceWorldAngle, 0f, "view-000");
                    foreach (float angle in new[] { 45f, 90f, 180f, 270f, 359f, 1f, 360f })
                    {
                        string name = "view-" + angle.ToString("000");
                        Color32[] actual = fixture.Capture(name, angle, 5f, 0f, false);
                        AssertSnapshotDirection(actual, fixture.ReferenceWorldAngle, angle, name);
                    }
                    AssertSnapshotDirection(
                        fixture.Capture("view-090-zoom-8", 90f, 8f, 0f, false),
                        fixture.ReferenceWorldAngle, 90f, "view-090-zoom-8");
                    // The scan captured a world direction once. Only the
                    // actual camera turn projects it; the player's heading
                    // contributes no additional rotation after that capture.
                    AssertSnapshotDirection(
                        fixture.Capture("view-045-camera-lag-30", 45f, 5f, 0f, false, default, -30f),
                        fixture.ReferenceWorldAngle, 15f, "view-045-camera-lag-30");
                    AssertSnapshotDirection(
                        fixture.Capture("view-000-camera-independent-35", 0f, 5f, 0f, false, default, 35f),
                        fixture.ReferenceWorldAngle, 35f, "view-000-camera-independent-35");
                    Color32[] view180 = fixture.Capture("view-180-parent-0", 180f, 5f, 0f, false);
                    AssertStablePixels(view180,
                        fixture.Capture("view-180-parent-37", 180f, 5f, 37f, false), "view-180-parent-37");
                    Color32[] view270 = fixture.Capture("view-270-parent-0", 270f, 5f, 0f, false);
                    AssertStablePixels(view270,
                        fixture.Capture("view-270-parent-late-90", 270f, 5f, 90f, true),
                        "view-270-parent-late-90");

                    // Every frame projects the exact same independent world
                    // endpoints captured on the scan's first presentation.
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

                    // A real Reveal on a still-visible target captures a fresh
                    // screen-slash direction, without hiding/recreating the UI.
                    fixture.BeginRescan(60f, 60f);
                    Color32[] rescanned60 = fixture.Capture("rescan-view-060", 60f, 5f, 0f, false);
                    AssertSnapshotDirection(rescanned60, fixture.ReferenceWorldAngle, 60f, "rescan-view-060");
                    AssertSnapshotDirection(
                        fixture.Capture("rescan-060-view-145", 145f, 7f, 0f, false),
                        fixture.ReferenceWorldAngle, 145f, "rescan-060-view-145");
                    AssertIdenticalPixels(rescanned60,
                        fixture.Capture("rescan-060-returned", 60f, 5f, 0f, false),
                        "rescan-060-returned");

                    // Scan before the camera's late update, then draw after it
                    // reaches a different final angle. Capture that final view,
                    // not the camera at Reveal and not the player's own angle.
                    fixture.BeginRescan(-25f, 120f);
                    Color32[] delayed = fixture.Capture("rescan-final-camera-120", 100f, 5f, 0f,
                        false, default, 20f);
                    AssertSnapshotDirection(delayed, fixture.ReferenceWorldAngle, 120f,
                        "rescan-final-camera-120");
                    AssertSnapshotDirection(
                        fixture.Capture("rescan-120-camera-145", 180f, 5f, 0f, false, default, -35f),
                        fixture.ReferenceWorldAngle, 145f, "rescan-120-camera-145");
                    AssertIdenticalPixels(delayed,
                        fixture.Capture("rescan-120-returned", 100f, 5f, 0f, false, default, 20f),
                        "rescan-120-returned");
                }
                using (var outlined = new Fixture(true))
                {
                    outlined.Capture("outline-warmup", 0f, 5f, 0f, false);
                    Color32[] baseline = outlined.Capture("outline-view-000", 0f, 5f, 0f, false);
                    foreach (float angle in new[] { 45f, 90f, 180f, 270f })
                        outlined.Capture("outline-view-" + angle.ToString("000"), angle, 5f, 0f, false);
                    outlined.Capture("outline-move-zoom", 35f, 7f, 0f, false, new Vector2(.45f, -.3f));
                    AssertIdenticalPixels(baseline,
                        outlined.Capture("outline-parent-rotation", 0f, 5f, 37f, true),
                        "outline-parent-rotation");
                    AssertIdenticalPixels(baseline,
                        outlined.Capture("outline-returned", 0f, 5f, 0f, false), "outline-returned");
                    outlined.BeginRescan(-25f, 120f);
                    outlined.Capture("outline-rescan-120", 100f, 5f, 0f, false, default, 20f);
                    outlined.Capture("outline-rescan-turned", 180f, 6f, 0f, false, new Vector2(-.2f, .3f));
                }
                Debug.Log("Static scan overlay Canvas pixel checks PASS: actual ScreenSpaceOverlay CanvasRenderer "
                    + "list, scan-time 45 degree screen slash captured into fixed world bars, later views "
                    + "at 0/45/90/180/270 degrees and heading wrap, independent camera rotation/lag, "
                    + "real asymmetric footprint, rendered-component PCA direction, zoom, no extra inherited UI rotation, "
                    + "a parent rotation after mesh rebuild, translation and tiny shake/zoom at world (120,100), "
                    + "independent captured world-bar GPU coverage, exact expected connected bar count, "
                    + "visible-target rescans, late final camera orientation capture and exact return-to-pose pixels; "
                    + "separate enabled diamond-outline fixture with scan-seeded world point centers fixed until rescan, "
                    + "independent layer tint thresholds and a combined pixel oracle through turns, movement, "
                    + "zoom, UI-parent rotation and fresh seeded rescans. Device: "
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

        private static bool IsGold(Color32 pixel) => pixel.r > GoldMinRed && pixel.g > GoldMinGreen
            && pixel.r > pixel.b + GoldRedBlueGap && pixel.g > pixel.b + GoldGreenBlueGap;

        private static float PredictableGoldCoverage(Color32 tint)
        {
            // The fixture is drawn over black. Linear SrcAlpha RGB therefore
            // reaches tint.rgb * tint.a * SDF coverage. Demand three byte levels
            // above every gold threshold, plus >=40% coverage, so conversion and
            // byte rounding cannot turn a predicted visible AA pixel into noise.
            const float margin = 3f;
            float alpha = tint.a / 255f;
            Require(alpha > 0f && tint.r > tint.b && tint.g > tint.b,
                "The pixel fixture needs a measurable quantized gold tint.");
            float required = Mathf.Max(.4f,
                (GoldMinRed + margin) / (tint.r * alpha),
                (GoldMinGreen + margin) / (tint.g * alpha),
                (GoldRedBlueGap + margin) / ((tint.r - tint.b) * alpha),
                (GoldGreenBlueGap + margin) / ((tint.g - tint.b) * alpha));
            Require(required < 1f, "The fixture tint is too dim to predict visible gold AA pixels.");
            return required;
        }

        private static void AssertGoldPixels(Color32[] pixels, string name)
        {
            int gold = 0;
            foreach (Color32 pixel in pixels) if (IsGold(pixel)) gold++;
            Require(gold > 600, name + " has insufficient real Canvas gold pixels: " + gold);
        }

        private static float PixelLineAngle(Color32[] pixels)
        {
            // Gradients of subpixel AA strokes inherit the square pixel grid's
            // angular bias. Recover each rendered connected bar's long axis
            // instead, using only its gold pixels and their covariance. Long
            // components carry more weight; short cap pixels cannot dominate.
            double real = 0, imaginary = 0, energy = 0;
            int measuredBars = 0;
            var visited = new bool[pixels.Length];
            var pending = new int[pixels.Length];
            for (int seed = 0; seed < pixels.Length; seed++)
            {
                if (visited[seed] || !IsGold(pixels[seed])) continue;
                int remaining = 1, count = 0;
                double sumX = 0, sumY = 0, sumXX = 0, sumYY = 0, sumXY = 0;
                pending[0] = seed;
                visited[seed] = true;
                while (remaining > 0)
                {
                    int index = pending[--remaining], x = index % Size, y = index / Size;
                    count++;
                    sumX += x; sumY += y;
                    sumXX += x * x; sumYY += y * y; sumXY += x * y;
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
                if (count < 6) continue;
                double meanX = sumX / count, meanY = sumY / count;
                double xx = sumXX / count - meanX * meanX;
                double yy = sumYY / count - meanY * meanY;
                double xy = sumXY / count - meanX * meanY;
                double axisReal = xx - yy, axisImaginary = 2 * xy;
                double axisMagnitude = Math.Sqrt(axisReal * axisReal + axisImaginary * axisImaginary);
                double variance = xx + yy;
                if (variance <= 0 || axisMagnitude / variance < .8) continue;
                double majorVariance = (variance + axisMagnitude) * .5;
                if (majorVariance < 4) continue;
                // atan2(axisImaginary, axisReal) is twice the PCA axis angle.
                // Major variance weights roughly by the squared bar length.
                real += majorVariance * axisReal / axisMagnitude;
                imaginary += majorVariance * axisImaginary / axisMagnitude;
                energy += majorVariance;
                measuredBars++;
            }
            Require(measuredBars > 3, "Too few elongated rendered bars were available for the pixel PCA direction check.");
            double coherence = Math.Sqrt(real * real + imaginary * imaginary) / Math.Max(1, energy);
            Require(coherence > .4, "The final GPU pixels do not contain a coherent single parallel direction.");
            return (float)(.5 * Math.Atan2(imaginary, real) * 180 / Math.PI);
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
            float expectedAngle = PixelLineAngle(expected), actualAngle = PixelLineAngle(actual);
            Debug.Log(name + " Canvas GPU line angle (modulo 180): " + actualAngle.ToString("0.00")
                + " degrees; baseline " + expectedAngle.ToString("0.00")
                + " degrees; changed gold pixels " + (changed * 100f).ToString("0.00") + "%.");
            Require(Mathf.Abs(Mathf.DeltaAngle(expectedAngle * 2f, actualAngle * 2f)) < 1f,
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

        private static void AssertSnapshotDirection(Color32[] actual, float capturedWorldAngle,
            float cameraAngle, string name)
        {
            AssertGoldPixels(actual, name);
            float actualAngle = PixelLineAngle(actual);
            float expectedAngle = capturedWorldAngle - cameraAngle;
            float difference = Mathf.Abs(Mathf.DeltaAngle(expectedAngle * 2f, actualAngle * 2f)) * .5f;
            Debug.Log(name + " Canvas GPU line angle (modulo 180): " + actualAngle.ToString("0.00")
                + " degrees; expected captured-world projection " + expectedAngle.ToString("0.00")
                + " degrees; directional difference " + difference.ToString("0.00") + " degrees.");
            // Pixel PCA can have a small rasterization bias; direction modulo 180°
            // must project the captured world direction through this camera.
            Require(difference < 7f,
                name + " did not project the scan-time captured world direction.");
        }

        private sealed class Fixture : IDisposable
        {
            private readonly Scene scene;
            private readonly Camera camera;
            private readonly Transform player;
            private readonly ScannableInteractable scanTarget;
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
            private readonly List<Vector2> referenceWorldOutlinePoints = new List<Vector2>();
            private readonly bool includeOutline;
            private bool drewOverlay;
            private bool expectedPendingCapture;
            private long referenceRevision;
            private uint referenceOutlineSeed;
            public float ReferenceWorldAngle { get; private set; }

            public Fixture(bool enableOutline = false)
            {
                includeOutline = enableOutline;
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
                scanTarget = targetObject.AddComponent<ScannableInteractable>();
                Set(scanTarget, "customNormalizedOutline", new[] {
                    new Vector2(-.5f, -.35f), new Vector2(-.3f, .5f),
                    new Vector2(.5f, .3f), new Vector2(.4f, -.5f) });
                Set(scanTarget, "lineWidthPixels", LineWidth);
                Set(scanTarget, "lineSpacingPixels", 7.7f);
                Set(scanTarget, "showOutlineDots", enableOutline);
                // Geometry checks freeze both independently animated layers at
                // a shared bright phase; RenderChecks probes real defaults.
                Set(scanTarget, "breathingPeriod", 1.4f);
                Set(scanTarget, "outlineBreathingPeriod", 1.4f);
                Set(scanTarget, "fadeInDuration", 0f);
                Require(Mathf.Abs(scanTarget.LineAngleDegrees - 45f) < .001f,
                    "The production static scan must author a 45 degree screen slash at capture.");

                Call(scanTarget, "OnEnable");
                overlay = Create("Canvas pixel check static area presenter").AddComponent<BioScanStaticAreaUI>();
                overlay.Initialize(null, camera);
                // Runtime subscribes here; Edit Mode does not promise the
                // MonoBehaviour callbacks for non-ExecuteAlways components.
                Call(overlay, "OnEnable");
                overlay.ShowScannedInteractable(scanTarget);
                Require(scanTarget.IsScanOrientationPending && !scanTarget.HasCapturedScanOrientation,
                    "A real scan must wait for the first presentation's final camera orientation.");
                // Advance beyond the subtle initial fade-in so this fixture
                // tests visible bars, rather than the transparent reveal frame.
                typeof(ScannableInteractable).GetMethod("TickReveal", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(scanTarget, new object[] { scanTarget.BreathingPeriod });
                ReferenceWorldAngle = camera.transform.eulerAngles.z + scanTarget.LineAngleDegrees;
                Call(overlay, "TickAreas");
                referenceRevision = Convert.ToInt64(scanTarget.ScanOrientationRevision);
                AssertCapturedOrientation("initial scan");
                BuildIndependentWorldReference();
                if (includeOutline) BuildIndependentOutlineReference();
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
                if (includeOutline)
                {
                    IList outlines = (IList)Get(overlay, "outlineGraphics");
                    Require(outlines.Count > 0, "Enabled production outline did not create its own graphic.");
                    foreach (BioScanStaticAreaGraphic outline in outlines)
                    {
                        outline.rectTransform.SetParent(rotationParent, false);
                        outline.rectTransform.anchorMin = Vector2.zero;
                        outline.rectTransform.anchorMax = Vector2.one;
                        outline.rectTransform.offsetMin = outline.rectTransform.offsetMax = Vector2.zero;
                    }
                }
                Canvas.ForceUpdateCanvases();
            }

            public void BeginRescan(float cameraAngleAtReveal, float finalCameraAngleAtFirstDraw)
            {
                Require(!expectedPendingCapture && scanTarget.HasCapturedScanOrientation,
                    "Finish the preceding snapshot before starting a new scan.");
                camera.transform.rotation = Quaternion.Euler(0f, 0f, cameraAngleAtReveal);
                scanTarget.Reveal();
                Require(scanTarget.IsScanOrientationPending && !scanTarget.HasCapturedScanOrientation,
                    "Rescanning a visible target must invalidate its old orientation and defer recapture.");
                ReferenceWorldAngle = Mathf.DeltaAngle(0f,
                    finalCameraAngleAtFirstDraw + scanTarget.LineAngleDegrees);
                expectedPendingCapture = true;
            }

            private void AssertCapturedOrientation(string name)
            {
                Require(scanTarget.HasCapturedScanOrientation && !scanTarget.IsScanOrientationPending,
                    name + " did not capture the pending scan orientation on first presentation.");
                Require(Mathf.Abs(Mathf.DeltaAngle(scanTarget.CapturedScanWorldAngleDegrees,
                    ReferenceWorldAngle)) < .001f,
                    name + " captured a different world direction than the first draw's final camera angle.");
                long actualRevision = Convert.ToInt64(scanTarget.ScanOrientationRevision);
                if (expectedPendingCapture)
                {
                    Require(actualRevision > referenceRevision,
                        name + " did not advance the orientation revision after a real rescan.");
                    referenceRevision = actualRevision;
                    expectedPendingCapture = false;
                    BuildIndependentWorldReference();
                    if (includeOutline)
                    {
                        Require(scanTarget.OutlinePatternSeed != 0
                            && scanTarget.OutlinePatternSeed != referenceOutlineSeed,
                            name + " did not assign a fresh nonzero outline arrangement seed on real rescan.");
                        BuildIndependentOutlineReference();
                    }
                }
                else
                {
                    Require(actualRevision == referenceRevision,
                        name + " changed the orientation revision without a new scan.");
                    if (includeOutline && referenceWorldOutlinePoints.Count > 0)
                        Require(scanTarget.OutlinePatternSeed == referenceOutlineSeed,
                            name + " changed its outline arrangement seed without a real rescan.");
                }
            }

            public Color32[] Capture(string name, float angle, float size, float parentAngle, bool rotateAfterRebuild,
                Vector2 cameraOffset = default, float cameraAngleOffset = 0f)
            {
                player.position = WorldOrigin + cameraOffset;
                player.rotation = Quaternion.Euler(0f, 0f, angle);
                cameraFollow.SnapToTarget();
                Require(Mathf.Abs(Mathf.DeltaAngle(camera.transform.eulerAngles.z, angle)) < .01f,
                    "RobotCameraFollow did not apply the player view rotation.");
                if (Mathf.Abs(cameraAngleOffset) > .0001f)
                    camera.transform.rotation = Quaternion.Euler(0f, 0f, angle + cameraAngleOffset);
                Require(Mathf.Abs(Mathf.DeltaAngle(camera.transform.eulerAngles.z, angle + cameraAngleOffset)) < .01f,
                    "The camera did not reach its requested independent view rotation.");
                camera.orthographicSize = size;
                rotationParent.localRotation = Quaternion.Euler(0f, 0f, rotateAfterRebuild ? 0f : parentAngle);
                Canvas.ForceUpdateCanvases();
                Call(overlay, "TickAreas");
                AssertCapturedOrientation(name);
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
                    if (name.IndexOf("warmup", StringComparison.Ordinal) < 0)
                        AssertProjectedReferencePixels(pixels, name);
                    return pixels;
                }
                finally
                {
                    RenderTexture.active = previous;
                    Object.DestroyImmediate(snapshot);
                }
            }

            private void BuildIndependentWorldReference()
            {
                // No presenter layout, UI mesh or shader metadata is read here.
                // Generate once per real scan in a fixed normal-view metric.
                // Keep these independent world endpoints through every later
                // player/camera turn, movement and zoom until the next scan.
                referenceWorldSegments.Clear();
                var offsets = new List<Vector2>();
                Require(scanTarget.TryGetPresentationFootprint(offsets, out Vector3 center),
                    "The pixel fixture target did not supply a world footprint.");
                float pixelsPerWorldUnit = Size / (ReferenceOrthographicSize * 2f);
                for (int index = 0; index < offsets.Count; index++) offsets[index] *= pixelsPerWorldUnit;
                var referenceBars = new List<StaticScanLineSegment>();
                Require(BioScanStaticLinePattern.Build(offsets, scanTarget.LineSpacingPixels,
                    ReferenceWorldAngle, referenceBars, LineWidth, Vector2.zero),
                    "The independent scan-time world lattice did not produce bars.");
                foreach (StaticScanLineSegment bar in referenceBars)
                    referenceWorldSegments.Add(new StaticScanLineSegment(
                        (Vector2)center + bar.Start / pixelsPerWorldUnit,
                        (Vector2)center + bar.End / pixelsPerWorldUnit));
                Require(referenceWorldSegments.Count > 10,
                    "The distant pixel fixture needs enough independent parallel bars to expose phase changes.");
            }

            private void BuildIndependentOutlineReference()
            {
                referenceWorldOutlinePoints.Clear();
                referenceOutlineSeed = scanTarget.OutlinePatternSeed;
                Require(referenceOutlineSeed != 0, "A real scan must generate a nonzero outline arrangement seed.");
                var offsets = new List<Vector2>();
                Require(scanTarget.TryGetPresentationFootprint(offsets, out Vector3 center),
                    "The outline fixture has no world footprint.");
                float pixelsPerWorldUnit = Size / (ReferenceOrthographicSize * 2f);
                for (int index = 0; index < offsets.Count; index++) offsets[index] *= pixelsPerWorldUnit;
                var points = new List<Vector2>();
                float spacing = scanTarget.UseSmallAreaFill ? scanTarget.SmallOutlineDotSpacingPixels
                    : scanTarget.OutlineDotSpacingPixels;
                Require(BioScanStaticOutlinePattern.BuildDots(offsets, spacing, points,
                    scanTarget.OutlineDotSizePixels, referenceOutlineSeed),
                    "The independent fixed outline did not produce diamond point centers.");
                foreach (Vector2 point in points)
                    referenceWorldOutlinePoints.Add((Vector2)center + point / pixelsPerWorldUnit);
                Require(referenceWorldOutlinePoints.Count > 10,
                    "The outline fixture needs enough visible diamond points and gaps to measure.");
            }

            private float MinimumExpectedGoldCoverage(bool forOutline = false)
            {
                float period = forOutline ? scanTarget.OutlineBreathingPeriod : scanTarget.BreathingPeriod;
                Require(scanTarget.RevealElapsed >= scanTarget.FadeInDuration
                    && scanTarget.RevealRemaining >= scanTarget.FadeOutDuration
                    && Mathf.Repeat(scanTarget.RevealElapsed, period) < .0001f,
                    "The AA oracle requires the fixture's fully appeared bright breathing frame.");
                Color tint = scanTarget.AreaColor;
                tint.a *= forOutline ? scanTarget.OutlineBreathingMaxAlpha : scanTarget.BreathingMaxAlpha;
                return PredictableGoldCoverage((Color32)tint);
            }

            private float[] ProjectedDistances(List<StaticScanLineSegment> bars, float width, bool inScreenSpace = false)
            {
                var distances = new float[Size * Size];
                for (int index = 0; index < distances.Length; index++) distances[index] = float.PositiveInfinity;
                foreach (StaticScanLineSegment world in bars)
                {
                    Vector2 start = inScreenSpace ? world.Start : (Vector2)camera.WorldToScreenPoint((Vector3)world.Start);
                    Vector2 end = inScreenSpace ? world.End : (Vector2)camera.WorldToScreenPoint((Vector3)world.End);
                    Vector2 midpoint = (start + end) * .5f;
                    Vector2 direction = (end - start).normalized;
                    Vector2 normal = new(-direction.y, direction.x);
                    float halfLength = Vector2.Distance(start, end) * .5f;
                    int minimumX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(start.x, end.x) - 3f), 0, Size - 1);
                    int maximumX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(start.x, end.x) + 3f), 0, Size - 1);
                    int minimumY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(start.y, end.y) - 3f), 0, Size - 1);
                    int maximumY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(start.y, end.y) + 3f), 0, Size - 1);
                    for (int y = minimumY; y <= maximumY; y++)
                    for (int x = minimumX; x <= maximumX; x++)
                    {
                        Vector2 delta = new Vector2(x + .5f, y + .5f) - midpoint;
                        Vector2 box = new(Mathf.Abs(Vector2.Dot(delta, direction)) - halfLength,
                            Mathf.Abs(Vector2.Dot(delta, normal)) - width * .5f);
                        float distance = Vector2.Max(box, Vector2.zero).magnitude
                            + Mathf.Min(Mathf.Max(box.x, box.y), 0f);
                        int index = y * Size + x;
                        distances[index] = Mathf.Min(distances[index], distance);
                    }
                }
                return distances;
            }

            private static double Cross(Vector2 first, Vector2 second)
                => (double)first.x * second.y - (double)first.y * second.x;

            private static List<Vector2> IndependentPointHull(List<Vector2> points)
            {
                var sorted = new List<Vector2>(points);
                sorted.Sort((first, second) => first.x != second.x
                    ? first.x.CompareTo(second.x) : first.y.CompareTo(second.y));
                var unique = new List<Vector2>();
                foreach (Vector2 point in sorted)
                    if (unique.Count == 0 || (point - unique[unique.Count - 1]).sqrMagnitude > .00000001f)
                        unique.Add(point);
                if (unique.Count < 3) return new List<Vector2>();
                var hull = new List<Vector2>();
                foreach (Vector2 point in unique)
                {
                    while (hull.Count >= 2 && Cross(hull[hull.Count - 1] - hull[hull.Count - 2],
                        point - hull[hull.Count - 1]) <= 0d) hull.RemoveAt(hull.Count - 1);
                    hull.Add(point);
                }
                int lower = hull.Count;
                for (int index = unique.Count - 2; index >= 0; index--)
                {
                    Vector2 point = unique[index];
                    while (hull.Count > lower && Cross(hull[hull.Count - 1] - hull[hull.Count - 2],
                        point - hull[hull.Count - 1]) <= 0d) hull.RemoveAt(hull.Count - 1);
                    hull.Add(point);
                }
                hull.RemoveAt(hull.Count - 1);
                return hull.Count >= 3 ? hull : new List<Vector2>();
            }

            private static List<StaticScanLineSegment> IndependentBoundaryClip(
                List<StaticScanLineSegment> sources, List<Vector2> hull, float width)
            {
                var clipped = new List<StaticScanLineSegment>();
                if (hull.Count < 3) return clipped;
                foreach (StaticScanLineSegment source in sources)
                {
                    Vector2 vector = source.End - source.Start;
                    double length = vector.magnitude;
                    if (length < .0001d) continue;
                    Vector2 direction = vector.normalized, normal = new(-direction.y, direction.x);
                    double minimum = 0d, maximum = length;
                    for (int index = 0; index < hull.Count && maximum >= minimum; index++)
                    {
                        Vector2 start = hull[index], edge = hull[(index + 1) % hull.Count] - start;
                        double along = Cross(edge, direction);
                        // The full AA rectangle is supported independently in
                        // the bar's two axes; clearance is a Euclidean 1 px
                        // offset from this hull edge, not one extra px per axis.
                        double support = Math.Abs(Cross(edge, normal)) * (width * .5d + 1d)
                            + Math.Abs(along) + edge.magnitude * 1.01d;
                        double remainder = Cross(edge, start - source.Start) + support;
                        if (Math.Abs(along) < .0000001d)
                        {
                            if (remainder > .000001d) maximum = -1d;
                        }
                        else if (along > 0d) minimum = Math.Max(minimum, remainder / along);
                        else maximum = Math.Min(maximum, remainder / along);
                    }
                    if (maximum - minimum < .25d) continue;
                    clipped.Add(new StaticScanLineSegment(source.Start + direction * (float)minimum,
                        source.Start + direction * (float)maximum));
                }
                return clipped;
            }

            private static float BoundaryDistance(Vector2 point, List<Vector2> hull)
            {
                if (hull.Count < 3) return float.NegativeInfinity;
                double minimum = double.PositiveInfinity;
                for (int index = 0; index < hull.Count; index++)
                {
                    Vector2 edge = hull[(index + 1) % hull.Count] - hull[index];
                    minimum = Math.Min(minimum, Cross(edge, point - hull[index]) / edge.magnitude);
                }
                return (float)minimum;
            }

            private void AssertCombinedReferencePixels(Color32[] pixels, string name)
            {
                float requiredHatchCoverage = MinimumExpectedGoldCoverage();
                float requiredOutlineCoverage = MinimumExpectedGoldCoverage(true);
                float[] originalHatch = ProjectedDistances(referenceWorldSegments, scanTarget.LineWidthPixels);
                var projectedCenters = new List<Vector2>();
                foreach (Vector2 point in referenceWorldOutlinePoints)
                    projectedCenters.Add(camera.WorldToScreenPoint((Vector3)point));
                List<Vector2> pointHull = IndependentPointHull(projectedCenters);
                var projectedSourceHatch = new List<StaticScanLineSegment>();
                foreach (StaticScanLineSegment source in referenceWorldSegments)
                    projectedSourceHatch.Add(new StaticScanLineSegment(
                        camera.WorldToScreenPoint((Vector3)source.Start), camera.WorldToScreenPoint((Vector3)source.End)));
                List<StaticScanLineSegment> clippedHatch = IndependentBoundaryClip(projectedSourceHatch,
                    pointHull, scanTarget.LineWidthPixels);
                float[] hatch = ProjectedDistances(clippedHatch, scanTarget.LineWidthPixels, true);
                // Independently project cached centers, then construct a square
                // of fixed screen size. Never scale the glyph by world zoom or
                // re-sample its perimeter points in the current camera view.
                Vector2 projectedOrigin = camera.WorldToScreenPoint((Vector3)WorldOrigin);
                Vector2 projectedWorldDiagonal = (Vector2)camera.WorldToScreenPoint(
                    (Vector3)(WorldOrigin + Vector2.one.normalized)) - projectedOrigin;
                Vector2 glyphDirection = projectedWorldDiagonal.normalized;
                float squareSide = scanTarget.OutlineDotSizePixels / Mathf.Sqrt(2f);
                var diamonds = new List<StaticScanLineSegment>();
                foreach (Vector2 center in projectedCenters)
                {
                    Vector2 extent = glyphDirection * (squareSide * .5f);
                    diamonds.Add(new StaticScanLineSegment(center - extent, center + extent));
                }
                float[] outline = ProjectedDistances(diamonds, squareSide, true);
                int predicted = 0, missing = 0, unexpected = 0, outlineOnly = 0,
                    removedProtrusions = 0, escapedHatchPixels = 0;
                for (int index = 0; index < pixels.Length; index++)
                {
                    float distance = Mathf.Min(hatch[index], outline[index]);
                    float hatchCoverage = 1f - Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(-.5f, .5f, hatch[index]));
                    float outlineCoverage = 1f - Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(-.5f, .5f, outline[index]));
                    if (hatchCoverage >= requiredHatchCoverage || outlineCoverage >= requiredOutlineCoverage)
                    {
                        predicted++;
                        if (!IsGold(pixels[index])) missing++;
                        if (outlineCoverage >= requiredOutlineCoverage && hatch[index] > .65f) outlineOnly++;
                    }
                    if (distance > .65f && IsGold(pixels[index])) unexpected++;
                    if (outline[index] > .65f && (IsGold(pixels[index])
                        || (originalHatch[index] < -.2f && hatch[index] > .65f)))
                    {
                        Vector2 point = new(index % Size + .5f, index / Size + .5f);
                        float boundaryDistance = BoundaryDistance(point, pointHull);
                        if (IsGold(pixels[index]) && boundaryDistance < -.001f) escapedHatchPixels++;
                        if (originalHatch[index] < -.2f && hatch[index] > .65f && boundaryDistance < 0f)
                        {
                            removedProtrusions++;
                            Require(!IsGold(pixels[index]), name + " retained an old hatch tip outside its diamond point hull.");
                        }
                    }
                }
                Require(predicted > 600 && outlineOnly > 20,
                    name + " did not exercise both the hatch and independently visible diamond outline pixels.");
                Require(missing == 0, name + " lost " + missing + "/" + predicted
                    + " predictable pixels of the fixed hatch/outline world endpoints.");
                Require(unexpected <= 2, name + " introduced " + unexpected
                    + " gold pixels outside the independently projected hatch/outline union.");
                Require(escapedHatchPixels == 0, name + " drew " + escapedHatchPixels
                    + " hatch-only pixels outside the actual diamond-center boundary.");
                Require(removedProtrusions > 2, name + " did not exercise the original protruding hatch tips.");
                int measuredGaps = 0;
                for (int index = 0; index < referenceWorldOutlinePoints.Count; index++)
                {
                    Vector2 previous = referenceWorldOutlinePoints[index];
                    Vector2 next = referenceWorldOutlinePoints[(index + 1) % referenceWorldOutlinePoints.Count];
                    Vector2 midpoint = camera.WorldToScreenPoint((Vector3)((previous + next) * .5f));
                    int x = Mathf.FloorToInt(midpoint.x), y = Mathf.FloorToInt(midpoint.y);
                    if (x < 0 || x >= Size || y < 0 || y >= Size) continue;
                    int pixel = y * Size + x;
                    if (Mathf.Min(hatch[pixel], outline[pixel]) <= .65f) continue;
                    Require(!IsGold(pixels[pixel]), name + " filled a measurable outline gap away from the hatch.");
                    measuredGaps++;
                }
                Require(measuredGaps > 5, name + " did not expose enough actual diamond-outline gaps.");
                Debug.Log(name + " fixed hatch/outline union PASS: " + predicted + " predicted visible pixels, "
                    + outlineOnly + " outline-only pixels, " + measuredGaps + " transparent outline gaps; "
                    + referenceWorldOutlinePoints.Count + " fixed world point centers with constant pixel-sized diamonds, scan seed "
                    + referenceOutlineSeed + "; " + removedProtrusions + " old protruding tip pixels removed, "
                    + clippedHatch.Count + "/" + referenceWorldSegments.Count + " source bars retained after independent clipping.");
            }

            private void AssertProjectedReferencePixels(Color32[] pixels, string name)
            {
                if (includeOutline) { AssertCombinedReferencePixels(pixels, name); return; }
                // This fixture deliberately freezes the fully appeared target
                // at its bright breathing limit. Quantize the same tint that
                // reaches UGUI, rather than assuming every AA pixel is opaque.
                float minimumGoldCoverage = MinimumExpectedGoldCoverage();
                var expectedDistance = new float[pixels.Length];
                for (int index = 0; index < expectedDistance.Length; index++)
                    expectedDistance[index] = float.PositiveInfinity;
                int interiorPixels = 0, missingInterior = 0, aaOnlyBars = 0;
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
                    int barInterior = 0, barMissing = 0, predictableAaPixels = 0, missingAaPixels = 0;
                    float maximumCoverage = 0f;
                    for (int y = minimumY; y <= maximumY; y++)
                    for (int x = minimumX; x <= maximumX; x++)
                    {
                        Vector2 delta = new Vector2(x + .5f, y + .5f) - middle;
                        Vector2 box = new Vector2(Mathf.Abs(Vector2.Dot(delta, direction)) - halfLength,
                            Mathf.Abs(Vector2.Dot(delta, normal)) - LineWidth * .5f);
                        float distance = Vector2.Max(box, Vector2.zero).magnitude
                            + Mathf.Min(Mathf.Max(box.x, box.y), 0f);
                        int index = y * Size + x;
                        expectedDistance[index] = Mathf.Min(expectedDistance[index], distance);
                        float coverage = 1f - Mathf.SmoothStep(0f, 1f,
                            Mathf.InverseLerp(-.5f, .5f, distance));
                        maximumCoverage = Mathf.Max(maximumCoverage, coverage);
                        if (coverage >= minimumGoldCoverage)
                        {
                            predictableAaPixels++;
                            if (!IsGold(pixels[index])) missingAaPixels++;
                        }
                        // A 0.9 px bar has no distance < -0.5 opaque core;
                        // -0.2 still guarantees at least 78.4% AA coverage.
                        if (distance >= -.2f) continue;
                        barInterior++;
                        if (!IsGold(pixels[index])) barMissing++;
                    }
                    if (barInterior == 0)
                    {
                        // A short subpixel bar can land entirely between pixel
                        // centers. Its SDF still predicts visible AA pixels:
                        // require those pixels individually, never skip the bar.
                        Require(predictableAaPixels > 0, name
                            + " has no predictable visible pixel for a reference bar: maximum SDF coverage "
                            + maximumCoverage.ToString("F4") + ", required "
                            + minimumGoldCoverage.ToString("F4") + ".");
                        Require(missingAaPixels == 0, name + " lost " + missingAaPixels + "/"
                            + predictableAaPixels + " predictable AA gold pixels of a coreless reference bar.");
                        aaOnlyBars++;
                        interiorPixels += predictableAaPixels;
                        missingInterior += missingAaPixels;
                        continue;
                    }
                    // Zoom can reduce a valid complete fringe bar to only one
                    // or two high-coverage pixel centers. Require all of those
                    // pixels, while retaining the long-bar rounding tolerance.
                    int allowedMissing = barInterior < 3 ? 0 : Mathf.Max(1, barInterior / 20);
                    Require(barMissing <= allowedMissing, name
                        + " lost the lit interior of an independently captured world bar.");
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
                Debug.Log(name + " independently captured world lattice: " + referenceWorldSegments.Count
                    + " bars / " + components + " GPU components; missing interior " + missingInterior
                    + "/" + interiorPixels + "; AA-only bars individually verified " + aaOnlyBars
                    + "; unexpected gold " + unexpectedGold + "/" + goldPixels + ".");
                Require(missingInterior <= Mathf.Max(2, interiorPixels / 200),
                    name + " does not cover the projected interiors of the scan-time reference lattice.");
                Require(unexpectedGold <= Mathf.Max(2, goldPixels / 200),
                    name + " introduced gold pixels away from the projected scan-time reference lattice.");
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
