using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AnimalGame.Animals;
using AnimalGame.RobotMap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AnimalGame.EditorTools
{
    /// <summary>GPU checks for the damaged 50 camera. Uses disposable fixtures and never saves project assets.</summary>
    public static class DamagedPrimePhotoValidation
    {
        private static readonly Rect Full = new Rect(0, 0, 1, 1);
        private const string AssetDirectory = "Assets/Data/AnimalPhoto/Rendering/";

        [Serializable]
        private sealed class Report
        {
            public string unityVersion;
            public string graphicsDevice;
            public string colorSpace;
            public string completedUtc;
            public bool passed;
            public List<string> checks = new List<string>();
            public List<string> metrics = new List<string>();
            public List<string> errors = new List<string>();
            public List<string> samples = new List<string>();
        }

        private sealed class Pixels
        {
            public int Width;
            public int Height;
            public Color32[] Colors;
        }

        [MenuItem("Animal Game/Photos/Validate Damaged Prime 50")]
        public static void RunMenu() => Run(false);

        // Keep the graphics device enabled. Put -logFile under the requested validation output directory.
        public static void RunBatch() => Run(true);

        private static void Run(bool exit)
        {
            string outputDirectory = FindOutputDirectory();
            var report = new Report
            {
                unityVersion = Application.unityVersion,
                graphicsDevice = SystemInfo.graphicsDeviceType + ": " + SystemInfo.graphicsDeviceName,
                colorSpace = QualitySettings.activeColorSpace.ToString()
            };
            RenderTexture active = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            UnityEngine.Random.State random = UnityEngine.Random.state;
            try
            {
                Directory.CreateDirectory(outputDirectory);
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run validation in Edit Mode.");
                Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null,
                    "GPU validation requires a graphics device; omit -nographics.");
                Check(report, "New authored camera and Set are independent; existing default remains Instant Camera", ValidateAssets);
                Check(report, "All RGB output channels are monochrome", ValidateMonochrome);
                Check(report, "Blur removes high frequency detail before grain is added", () => ValidateBlur(report));
                Check(report, "Heavy grain is frozen per photograph and preserves distinct large adjacent seeds", () => ValidateGrain(report));
                Check(report, "Crop dimensions, alpha, maximum size and crop boundaries are preserved", ValidateCropAndAlpha);
                Check(report, "Rendering preserves active texture, GL state and Unity random sequence", ValidateGlobalState);
                Check(report, "Captured output survives Set edits and a camera switch", ValidateCaptureFreeze);
                Check(report, "Real game photos export through the authored camera and actual GPU pipeline", () => ExportSamples(report, outputDirectory));
                Check(report, "Damaged prime shader is supported and has no shader compilation errors", ValidateShader);
            }
            catch (Exception exception) { report.errors.Add(exception.ToString()); }
            finally
            {
                RenderTexture.active = active;
                GL.sRGBWrite = srgb;
                UnityEngine.Random.state = random;
                report.completedUtc = DateTime.UtcNow.ToString("O");
                report.passed = report.errors.Count == 0;
                Directory.CreateDirectory(outputDirectory);
                File.WriteAllText(Path.Combine(outputDirectory, "report.json"), JsonUtility.ToJson(report, true));
                Debug.Log($"Damaged prime validation {(report.passed ? "PASSED" : "FAILED")}: " +
                    $"{report.checks.Count} checks; report at {outputDirectory}/report.json");
                if (!report.passed) Debug.LogError(string.Join("\n", report.errors));
                if (exit && Application.isBatchMode) EditorApplication.Exit(report.passed ? 0 : 1);
            }
        }

        private static void ValidateAssets()
        {
            var camera = LoadCamera();
            var set = AssetDatabase.LoadAssetAtPath<DamagedPrimePhotoRenderSet>(AssetDirectory + "DamagedPrime50Set.asset");
            var instant = AssetDatabase.LoadAssetAtPath<PhotoCameraDefinition>(AssetDirectory + "InstantCamera.asset");
            var robot = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Resources/Robot/RobotMarker.prefab");
            Require(camera.DisplayName == "halfly-broken prime camera 50", "Camera display name differs from the requested name.");
            Require(set != null && camera.RenderSet == set, "New camera is not bound to its own damaged rendering Set.");
            Require(instant != null && instant.RenderSet is InstantFilmPhotoRenderSet &&
                camera.CameraId != instant.CameraId && set.SetId != instant.RenderSet.SetId,
                "New camera or Set overwrote an existing model identity.");
            Require(robot != null && robot.GetComponent<PhotoModeController>() != null &&
                robot.GetComponent<PhotoModeController>().CurrentCamera == instant,
                "Existing robot default camera was changed.");
            Require(camera.Capture(1).RenderSnapshot is DamagedPrimePhotoRenderSnapshot,
                "Authored model does not capture the damaged rendering pipeline.");
            DamagedPrimeSettings settings = ((DamagedPrimePhotoRenderSnapshot)camera.Capture(1).RenderSnapshot).Settings;
            Require(settings.blurRadius >= 10 && settings.grainAmount >= 0.1f,
                "Authored Set is missing the requested strong blur and heavy grain.");
        }

        private static void ValidateMonochrome()
        {
            Texture2D source = MakeFixture(256, 128);
            try
            {
                Pixels image = Render(new AnimalPhoto(source, Full, 1, LoadCamera().Capture(912)), 256);
                foreach (Color32 pixel in image.Colors)
                    Require(Math.Abs(pixel.r - pixel.g) <= 1 && Math.Abs(pixel.g - pixel.b) <= 1,
                        "Damaged output retains colored image content or colored grain.");
            }
            finally { Object.DestroyImmediate(source); }
        }

        private static void ValidateBlur(Report report)
        {
            Texture2D source = MakeChecker(512, 256);
            try
            {
                DamagedPrimeSettings clear = Neutral();
                Pixels crisp = Render(Photo(source, Full, clear, 30), 512);
                DamagedPrimeSettings blurred = clear;
                blurred.blurRadius = DamagedPrimeSettings.Default.blurRadius;
                Pixels soft = Render(Photo(source, Full, blurred, 30), 512);
                double crispEdges = EdgeMagnitude(crisp), softEdges = EdgeMagnitude(soft);
                report.metrics.Add($"Blur fixture average neighbor delta: crisp {crispEdges:F3}, damaged {softEdges:F3}");
                Require(crispEdges > 15 && softEdges < crispEdges * 0.65,
                    "Blur did not significantly reduce high frequency image detail.");
                Require(Mean(soft) > 70 && Mean(soft) < 240, "Blur unexpectedly erased or clipped the image.");
            }
            finally { Object.DestroyImmediate(source); }
        }

        private static void ValidateGrain(Report report)
        {
            Texture2D source = MakeSolid(256, 128, new Color(0.45f, 0.45f, 0.45f, 1));
            try
            {
                DamagedPrimeSettings settings = Neutral();
                settings.grainAmount = DamagedPrimeSettings.Default.grainAmount;
                Pixels first = Render(Photo(source, Full, settings, 711), 256);
                Require(MaxDifference(first, Render(Photo(source, Full, settings, 711), 256)) == 0,
                    "Re-rendering one photo changes its grain pattern.");
                Require(MaxDifference(first, Render(Photo(source, Full, settings, 712), 256)) > 8,
                    "Different photographs share the same grain pattern.");
                Require(MaxDifference(Render(Photo(source, Full, settings, int.MaxValue - 1), 256),
                    Render(Photo(source, Full, settings, int.MaxValue), 256)) > 8,
                    "Adjacent large seeds lose precision when uploaded to the GPU.");
                double deviation = StandardDeviation(first);
                report.metrics.Add($"Flat-field grain standard deviation: {deviation:F3} byte levels");
                Require(deviation > 5, "Grain is too weak to visibly texture a flat field.");
                settings.grainAmount = 0;
                Pixels clean = Render(Photo(source, Full, settings, 711), 256);
                Require(StandardDeviation(clean) < 1, "Noise remains after setting grain amount to zero.");
                Require(MaxDifference(clean, Render(Photo(source, Full, settings, 712), 256)) == 0,
                    "Seed affects the image when grain is disabled.");
            }
            finally { Object.DestroyImmediate(source); }
        }

        private static void ValidateCropAndAlpha()
        {
            Texture2D landscape = MakeFixture(192, 96, 0.37f);
            Texture2D portrait = MakeFixture(96, 192, 0.37f);
            Texture2D border = MakeSolid(256, 128, new Color(0, 0, 0, 0.37f));
            try
            {
                foreach (Texture2D source in new[] { landscape, portrait })
                {
                    Pixels native = Render(Photo(source, Full, DamagedPrimeSettings.Default, 8), 1024);
                    Require(native.Width == source.width && native.Height == source.height, "Rendering upscaled the source.");
                    Pixels smaller = Render(Photo(source, Full, DamagedPrimeSettings.Default, 8), 64);
                    Require(Mathf.Max(smaller.Width, smaller.Height) == 64 && Mathf.Min(smaller.Width, smaller.Height) == 32,
                        "Photo aspect ratio or maximum dimension changed.");
                    foreach (Color32 color in native.Colors)
                        Require(Math.Abs(color.a - 94) <= 1, "Photo alpha was altered by blur or grain.");
                }
                Pixels square = Render(Photo(landscape, new Rect(0.25f, 0, 0.5f, 1), DamagedPrimeSettings.Default, 8), 1024);
                Require(square.Width == 96 && square.Height == 96, "A square source crop became non-square.");
                for (int y = 32; y < 96; y++)
                for (int x = 64; x < 192; x++) border.SetPixel(x, y, new Color(1, 1, 1, 0.37f));
                border.Apply(false, false);
                DamagedPrimeSettings blurred = Neutral();
                blurred.blurRadius = DamagedPrimeSettings.Default.blurRadius;
                Pixels isolated = Render(Photo(border, new Rect(0.25f, 0.25f, 0.5f, 0.5f), blurred, 8), 128);
                Require(isolated.Width == 128 && isolated.Height == 64, "Cropped dimensions are incorrect.");
                foreach (Color32 color in isolated.Colors)
                    Require(color.r >= 250, "Blur sampled dark source pixels outside the selected crop.");
            }
            finally { Object.DestroyImmediate(landscape); Object.DestroyImmediate(portrait); Object.DestroyImmediate(border); }
        }

        private static void ValidateGlobalState()
        {
            Texture2D source = MakeFixture(128, 64);
            UnityEngine.Random.State before = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(987654);
                UnityEngine.Random.State expected = UnityEngine.Random.state;
                int next = UnityEngine.Random.Range(0, int.MaxValue);
                UnityEngine.Random.state = expected;
                Render(Photo(source, Full, DamagedPrimeSettings.Default, 92), 128);
                Require(UnityEngine.Random.Range(0, int.MaxValue) == next,
                    "Photo processing consumed or reseeded the gameplay random generator.");
            }
            finally { UnityEngine.Random.state = before; Object.DestroyImmediate(source); }
        }

        private static void ValidateCaptureFreeze()
        {
            var damaged = ScriptableObject.CreateInstance<DamagedPrimePhotoRenderSet>();
            var basic = ScriptableObject.CreateInstance<BasicPhotoRenderSet>();
            var firstCamera = MakeCamera("validation-damaged", damaged);
            var nextCamera = MakeCamera("validation-basic", basic);
            Texture2D source = MakeFixture(192, 128);
            Scene scene = EditorSceneManager.NewPreviewScene();
            var owner = new GameObject("Damaged camera capture fixture") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(owner, scene);
            try
            {
                PhotoModeController controller = owner.AddComponent<PhotoModeController>();
                controller.SetCamera(firstCamera);
                Invoke(controller, "BeginCapture");
                PhotoCaptureSettings captured = controller.LastCaptureSettings;
                Require(captured != null && captured.CameraId == firstCamera.CameraId,
                    "Shutter did not capture the selected camera identity.");
                var photo = new AnimalPhoto(source, Full, 1, captured);
                Pixels before = Render(photo, 192);
                var serialized = new SerializedObject(damaged);
                serialized.FindProperty("settings.blurRadius").floatValue = 0;
                serialized.FindProperty("settings.grainAmount").floatValue = 0;
                serialized.FindProperty("settings.exposureEV").floatValue = 1;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                controller.SetCamera(nextCamera);
                int events = 0;
                PhotoCaptureSettings eventSettings = null;
                controller.PhotoCaptured += () => { events++; eventSettings = controller.LastCaptureSettings; };
                Invoke(controller, "UpdateShutter", 0.05f);
                Require(events == 1 && ReferenceEquals(eventSettings, captured),
                    "Delayed shutter completed using different camera settings.");
                Require(MaxDifference(before, Render(photo, 192)) == 0,
                    "Editing the Set or switching camera changed an existing photo.");
                Require(MaxDifference(before, Render(photo.WithCaptureSettings(firstCamera.Capture(captured.RenderSnapshot.Seed)), 192)) > 8,
                    "A fresh capture ignored edited Set parameters.");
                Invoke(controller, "BeginCapture");
                Require(controller.LastCaptureSettings.CameraId == nextCamera.CameraId,
                    "The next photograph did not use the newly selected model.");
            }
            finally
            {
                Object.DestroyImmediate(owner);
                EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(firstCamera);
                Object.DestroyImmediate(nextCamera);
                Object.DestroyImmediate(damaged);
                Object.DestroyImmediate(basic);
            }
        }

        private static void ExportSamples(Report report, string outputDirectory)
        {
            string[] paths =
            {
                "Assets/Arts/Animal_Photos/Rocky_Mountains/Muskrat/Muskrat_10.jpg",
                "Assets/Arts/Animal_Photos/Rocky_Mountains/Pileated_Woodpecker/PileatedWoodpecker_06.JPG"
            };
            var html = new System.Text.StringBuilder("<!doctype html><meta charset='utf-8'><title>Damaged Prime 50</title>" +
                "<style>body{background:#111;color:#eee;font:16px system-ui;margin:30px}section{display:flex;gap:18px}figure{margin:0;width:48%}img{width:100%}figcaption{margin:8px 0 24px}</style>" +
                "<h1>halfly-broken prime camera 50</h1><p>Actual game photo assets. Left: original. Right: authored camera GPU output, fixed capture seed 1234.</p>");
            foreach (string path in paths)
            {
                Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Require(source != null, "Real photo sample missing: " + path);
                string stem = Path.GetFileNameWithoutExtension(path);
                html.Append("<h2>").Append(stem).Append("</h2><section>");
                foreach (string version in new[] { "original", "damaged-prime-50" })
                {
                    AnimalPhoto photo = version == "original" ? new AnimalPhoto(source, Full, 1) :
                        new AnimalPhoto(source, Full, 1, LoadCamera().Capture(1234));
                    Pixels pixels = Render(photo, 1024);
                    Require(StandardDeviation(pixels) > 10, "Exported photograph has no useful image variation.");
                    string file = stem + "-" + version + ".png";
                    WritePng(pixels, Path.Combine(outputDirectory, file));
                    report.samples.Add(file);
                    html.Append("<figure><img src='").Append(file).Append("'><figcaption>").Append(version).Append("</figcaption></figure>");
                }
                html.Append("</section>");
            }
            File.WriteAllText(Path.Combine(outputDirectory, "index.html"), html.ToString());
        }

        private static void ValidateShader()
        {
            Shader shader = Resources.Load<Shader>("AnimalPhotoDamagedPrime");
            Require(shader != null && shader.isSupported, "Damaged prime shader is missing or unsupported.");
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            try { Require(material.passCount == 2, "The optical blur / sensor grading passes are incomplete."); }
            finally { Object.DestroyImmediate(material); }
            foreach (var message in ShaderUtil.GetShaderMessages(shader))
                Require(message.severity.ToString() != "Error", "Damaged prime shader: " + message.message);
        }

        private static DamagedPrimeSettings Neutral()
        {
            DamagedPrimeSettings settings = DamagedPrimeSettings.Default;
            settings.blurRadius = 0;
            settings.grainAmount = 0;
            settings.exposureEV = 0;
            settings.contrast = 1;
            settings.blackLift = 0;
            settings.vignetteEV = 0;
            return settings;
        }

        private static AnimalPhoto Photo(Texture2D source, Rect crop, DamagedPrimeSettings settings, int seed) =>
            new AnimalPhoto(source, crop, 1, new PhotoCaptureSettings("validation-damaged", "Validation damaged",
                new DamagedPrimePhotoRenderSnapshot("validation-damaged-set", 1, seed, settings)));

        private static PhotoCameraDefinition LoadCamera()
        {
            var camera = AssetDatabase.LoadAssetAtPath<PhotoCameraDefinition>(AssetDirectory + "HalflyBrokenPrimeCamera50.asset");
            Require(camera != null, "Authored damaged camera asset is missing.");
            return camera;
        }

        private static PhotoCameraDefinition MakeCamera(string id, PhotoRenderSet set)
        {
            var camera = ScriptableObject.CreateInstance<PhotoCameraDefinition>();
            var serialized = new SerializedObject(camera);
            serialized.FindProperty("cameraId").stringValue = id;
            serialized.FindProperty("displayName").stringValue = id;
            serialized.FindProperty("renderSet").objectReferenceValue = set;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return camera;
        }

        private static void Invoke(object target, string method, params object[] arguments)
        {
            MethodInfo info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            Require(info != null, "Capture integration method is missing: " + method);
            info.Invoke(target, arguments);
        }

        private static Texture2D MakeFixture(int width, int height, float alpha = 1)
        {
            Texture2D texture = MakeSolid(width, height, Color.clear);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float u = (float)x / (width - 1), v = (float)y / (height - 1);
                texture.SetPixel(x, y, new Color(0.05f + u * 0.9f, 0.1f + v * 0.75f, 0.9f - u * 0.6f, alpha));
            }
            texture.Apply(false, false);
            return texture;
        }

        private static Texture2D MakeChecker(int width, int height)
        {
            Texture2D texture = MakeSolid(width, height, Color.clear);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                texture.SetPixel(x, y, (x / 4 + y / 4) % 2 == 0 ? Color.white : Color.black);
            texture.Apply(false, false);
            return texture;
        }

        private static Texture2D MakeSolid(int width, int height, Color color)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var colors = new Color[width * height];
            for (int i = 0; i < colors.Length; i++) colors[i] = color;
            texture.SetPixels(colors);
            texture.Apply(false, false);
            return texture;
        }

        private static Pixels Render(AnimalPhoto photo, int maximumSize)
        {
            RenderTexture active = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            var sentinel = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32);
            RenderTexture output = null;
            try
            {
                sentinel.Create();
                RenderTexture.active = sentinel;
                GL.sRGBWrite = !srgb;
                bool expected = GL.sRGBWrite;
                output = photo.Render(maximumSize);
                Require(RenderTexture.active == sentinel && GL.sRGBWrite == expected,
                    "Photo renderer failed to restore active target / GL.sRGBWrite.");
                return Read(output);
            }
            finally
            {
                RenderTexture.active = active;
                GL.sRGBWrite = srgb;
                AnimalPhotoProcessing.Release(output);
                AnimalPhotoProcessing.Release(sentinel);
            }
        }

        private static Pixels Read(RenderTexture output)
        {
            Require(output != null, "Renderer returned no texture.");
            var texture = new Texture2D(output.width, output.height, TextureFormat.RGBA32, false, false);
            RenderTexture active = RenderTexture.active;
            try
            {
                RenderTexture.active = output;
                texture.ReadPixels(new Rect(0, 0, output.width, output.height), 0, 0, false);
                texture.Apply(false, false);
                return new Pixels { Width = output.width, Height = output.height, Colors = texture.GetPixels32() };
            }
            finally { RenderTexture.active = active; Object.DestroyImmediate(texture); }
        }

        private static int MaxDifference(Pixels a, Pixels b)
        {
            Require(a.Width == b.Width && a.Height == b.Height, "Compared images have different dimensions.");
            int maximum = 0;
            for (int i = 0; i < a.Colors.Length; i++)
            {
                Color32 first = a.Colors[i], second = b.Colors[i];
                maximum = Math.Max(maximum, Math.Max(Math.Abs(first.r - second.r), Math.Max(Math.Abs(first.g - second.g),
                    Math.Max(Math.Abs(first.b - second.b), Math.Abs(first.a - second.a)))));
            }
            return maximum;
        }

        private static double Mean(Pixels pixels)
        {
            double sum = 0;
            foreach (Color32 color in pixels.Colors) sum += color.r;
            return sum / pixels.Colors.Length;
        }

        private static double StandardDeviation(Pixels pixels)
        {
            double mean = Mean(pixels), sum = 0;
            foreach (Color32 color in pixels.Colors) sum += (color.r - mean) * (color.r - mean);
            return Math.Sqrt(sum / pixels.Colors.Length);
        }

        private static double EdgeMagnitude(Pixels pixels)
        {
            double sum = 0;
            int count = 0;
            for (int y = 1; y < pixels.Height; y++)
            for (int x = 1; x < pixels.Width; x++)
            {
                int i = y * pixels.Width + x;
                sum += Math.Abs(pixels.Colors[i].r - pixels.Colors[i - 1].r);
                sum += Math.Abs(pixels.Colors[i].r - pixels.Colors[i - pixels.Width].r);
                count += 2;
            }
            return sum / count;
        }

        private static void WritePng(Pixels pixels, string path)
        {
            var texture = new Texture2D(pixels.Width, pixels.Height, TextureFormat.RGBA32, false, false);
            try { texture.SetPixels32(pixels.Colors); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); }
            finally { Object.DestroyImmediate(texture); }
        }

        private static string FindOutputDirectory()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i].Equals("-logFile", StringComparison.OrdinalIgnoreCase) && args[i + 1] != "-")
                    return Path.GetDirectoryName(Path.GetFullPath(args[i + 1]));
            return Path.GetFullPath("output/validation/damaged-prime-50");
        }

        private static void Check(Report report, string label, Action action)
        {
            try { action(); report.checks.Add(label); }
            catch (Exception exception) { report.errors.Add(label + ": " + exception); }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
