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
    /// <summary>GPU and capture-lifetime checks using disposable fixtures; never saves source assets or scenes.</summary>
    public static class PhotoRenderingValidation
    {
        private const string OutputDirectory = "output/photo-render-validation";
        private static readonly Rect Full = new Rect(0, 0, 1, 1);

        [Serializable]
        private sealed class Report
        {
            public string unityVersion;
            public string graphicsDevice;
            public string colorSpace;
            public string completedUtc;
            public bool passed;
            public List<string> checks = new List<string>();
            public List<string> errors = new List<string>();
            public List<string> samples = new List<string>();
        }

        private sealed class Pixels
        {
            public int Width;
            public int Height;
            public Color32[] Colors;
        }

        [MenuItem("Animal Game/Photos/Validate Rendering")]
        public static void RunMenu() => Run(false);

        // Unity -batchmode -projectPath ... -executeMethod AnimalGame.EditorTools.PhotoRenderingValidation.RunBatch
        // Keep graphics enabled. The runner writes its report before exiting with status 0/1.
        public static void RunBatch() => Run(true);

        private static void Run(bool exit)
        {
            var report = new Report
            {
                unityVersion = Application.unityVersion,
                graphicsDevice = SystemInfo.graphicsDeviceType + ": " + SystemInfo.graphicsDeviceName,
                colorSpace = QualitySettings.activeColorSpace.ToString()
            };
            RenderTexture initialActive = RenderTexture.active;
            bool initialWrite = GL.sRGBWrite;
            UnityEngine.Random.State initialRandom = UnityEngine.Random.state;
            try
            {
                Directory.CreateDirectory(OutputDirectory);
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run validation in Edit Mode.");
                Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null,
                    "Rendering validation needs a graphics device; omit -nographics.");
                Check(report, "Basic matches the existing crop/saturation shader", ValidateBasic);
                Check(report, "Fixed grain seed is repeatable and affects only enabled grain", ValidateGrain);
                Check(report, "Strength zero returns the source, including with softness enabled", ValidateBypass);
                Check(report, "Output preserves alpha, crop proportions and does not upscale", ValidateGeometryAndAlpha);
                Check(report, "Shutter settings survive Set edits and camera switches", ValidateCaptureFreeze);
                Check(report, "Missing camera Set uses the existing Basic processing", ValidateFallback);
                Check(report, "Default robot uses the authored Instant Camera and independent Sets", ValidateDefaultAssets);
                Check(report, "Export real-photo comparisons through both shader passes", () => ExportSamples(report));
                Check(report, "Basic and Instant Film shaders have no compilation errors", ValidateShaders);
            }
            catch (Exception exception)
            {
                report.errors.Add(exception.ToString());
            }
            finally
            {
                RenderTexture.active = initialActive;
                GL.sRGBWrite = initialWrite;
                UnityEngine.Random.state = initialRandom;
                report.completedUtc = DateTime.UtcNow.ToString("O");
                report.passed = report.errors.Count == 0;
                Directory.CreateDirectory(OutputDirectory);
                File.WriteAllText(Path.Combine(OutputDirectory, "report.json"), JsonUtility.ToJson(report, true));
                Debug.Log($"Photo rendering validation {(report.passed ? "PASSED" : "FAILED")}: " +
                    $"{report.checks.Count} checks; report at {Path.GetFullPath(OutputDirectory)}/report.json");
                if (!report.passed) Debug.LogError(string.Join("\n", report.errors));
                if (exit && Application.isBatchMode) EditorApplication.Exit(report.passed ? 0 : 1);
            }
        }

        private static void Check(Report report, string label, Action action)
        {
            try { action(); report.checks.Add(label); }
            catch (Exception exception) { report.errors.Add(label + ": " + exception); }
        }

        private static void ValidateBasic()
        {
            Texture2D source = MakeFixture(192, 96);
            var set = ScriptableObject.CreateInstance<BasicPhotoRenderSet>();
            try
            {
                Rect crop = new Rect(0.25f, 0, 0.5f, 1);
                var settings = new PhotoCaptureSettings("basic", "Basic", set.Capture(25));
                Pixels actual = Render(new AnimalPhoto(source, crop, 0.3f, settings), 64);
                Pixels expected = RenderLegacy(source, crop, 0.3f, 64);
                RequireDifferenceAtMost(actual, expected, 1, "Basic changed the legacy result");
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(set); }
        }

        private static void ValidateGrain()
        {
            Texture2D source = MakeFixture(192, 128);
            try
            {
                InstantFilmSettings settings = InstantFilmSettings.Default;
                settings.grainAmount = 0.025f;
                Pixels first = Render(Film(source, Full, settings, 711), 192);
                RequireDifferenceAtMost(first, Render(Film(source, Full, settings, 711), 192), 0,
                    "Identical seed/parameters produced moving grain");
                Require(MaxDifference(first, Render(Film(source, Full, settings, 712), 192)) > 2,
                    "Changing the grain seed did not change visible grain");
                Require(MaxDifference(Render(Film(source, Full, settings, int.MaxValue - 1), 192),
                    Render(Film(source, Full, settings, int.MaxValue), 192)) > 2,
                    "Adjacent large seeds lost integer precision during material upload");
                settings.grainEnabled = false;
                RequireDifferenceAtMost(Render(Film(source, Full, settings, 711), 192),
                    Render(Film(source, Full, settings, 712), 192), 0,
                    "Disabled grain still depends on seed");
            }
            finally { Object.DestroyImmediate(source); }
        }

        private static void ValidateBypass()
        {
            Texture2D source = MakeFixture(192, 96);
            try
            {
                Rect crop = new Rect(0.25f, 0, 0.5f, 1);
                Pixels original = Render(new AnimalPhoto(source, crop, 1), 96);
                InstantFilmSettings settings = InstantFilmSettings.Default;
                settings.strength = 0;
                settings.softnessEnabled = true;
                settings.softness = 1;
                settings.exposureEV = 2;
                var photo = Film(source, crop, settings, 7, 0.15f);
                RequireDifferenceAtMost(original, Render(photo, 96), 1,
                    "Strength zero altered the source or applied the legacy saturation twice");
            }
            finally { Object.DestroyImmediate(source); }
        }

        private static void ValidateGeometryAndAlpha()
        {
            Texture2D landscape = MakeFixture(192, 96, 0.37f);
            Texture2D portrait = MakeFixture(96, 192, 0.37f);
            try
            {
                InstantFilmSettings settings = InstantFilmSettings.Default;
                settings.softnessEnabled = true;
                settings.softness = 0.75f;
                foreach (Texture2D source in new[] { landscape, portrait })
                {
                    foreach (AnimalPhoto photo in new[]
                    {
                        new AnimalPhoto(source, Full, 1), Film(source, Full, settings, 8)
                    })
                    {
                        Pixels native = Render(photo, 1024);
                        Require(native.Width == source.width && native.Height == source.height,
                            "Small source was upscaled or its aspect ratio changed");
                        Pixels small = Render(photo, 64);
                        Require(Mathf.Max(small.Width, small.Height) == 64 && Mathf.Min(small.Width, small.Height) == 32,
                            "Landscape/portrait dimensions were not preserved");
                        foreach (Color32 color in native.Colors)
                            Require(Math.Abs(color.a - 94) <= 1, "Photo alpha was changed by processing");
                    }
                }
                Rect square = new Rect(0.25f, 0, 0.5f, 1);
                Pixels cropped = Render(Film(landscape, square, settings, 8), 1024);
                Require(cropped.Width == 96 && cropped.Height == 96, "Source-pixel square crop has wrong dimensions");
            }
            finally { Object.DestroyImmediate(landscape); Object.DestroyImmediate(portrait); }
        }

        private static void ValidateCaptureFreeze()
        {
            var film = ScriptableObject.CreateInstance<InstantFilmPhotoRenderSet>();
            var basic = ScriptableObject.CreateInstance<BasicPhotoRenderSet>();
            var firstCamera = MakeCamera("validation-instant", film);
            var secondCamera = MakeCamera("validation-basic", basic);
            Texture2D source = MakeFixture(192, 128);
            Scene scene = EditorSceneManager.NewPreviewScene();
            var owner = new GameObject("Photo rendering validation fixture") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(owner, scene);
            try
            {
                PhotoModeController controller = owner.AddComponent<PhotoModeController>();
                controller.SetCamera(firstCamera);
                Invoke(controller, "BeginCapture");
                PhotoCaptureSettings captured = controller.LastCaptureSettings;
                Require(captured != null && captured.CameraId == firstCamera.CameraId,
                    "BeginCapture did not freeze the active model");
                var photo = new AnimalPhoto(source, Full, 1, captured);
                Pixels before = Render(photo, 192);

                var serialized = new SerializedObject(film);
                serialized.FindProperty("settings.exposureEV").floatValue = 1.5f;
                serialized.FindProperty("settings.grainAmount").floatValue = 0;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                controller.SetCamera(secondCamera);
                int events = 0;
                PhotoCaptureSettings eventSettings = null;
                controller.PhotoCaptured += () => { events++; eventSettings = controller.LastCaptureSettings; };
                Invoke(controller, "UpdateShutter", 0.05f);
                Require(events == 1 && ReferenceEquals(eventSettings, captured),
                    "Delayed shutter event used a different capture context");
                RequireDifferenceAtMost(before, Render(photo, 192), 0,
                    "An already taken photo changed after editing its Set/switching camera");
                Require(MaxDifference(before, Render(photo.WithCaptureSettings(firstCamera.Capture(captured.RenderSnapshot.Seed)), 192)) > 5,
                    "Fresh capture ignored new Set parameters");
                Invoke(controller, "BeginCapture");
                Require(controller.LastCaptureSettings.CameraId == secondCamera.CameraId,
                    "The next capture ignored the selected camera model");
            }
            finally
            {
                Object.DestroyImmediate(owner);
                EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(firstCamera);
                Object.DestroyImmediate(secondCamera);
                Object.DestroyImmediate(film);
                Object.DestroyImmediate(basic);
            }
        }

        private static void ValidateFallback()
        {
            PhotoCameraDefinition camera = MakeCamera("validation-empty", null);
            Texture2D source = MakeFixture(96, 64);
            try
            {
                Pixels expected = Render(new AnimalPhoto(source, Full, 0.3f), 96);
                Pixels actual = Render(new AnimalPhoto(source, Full, 0.3f, camera.Capture(9)), 96);
                RequireDifferenceAtMost(expected, actual, 0, "Missing Set did not select Basic fallback");
            }
            finally { Object.DestroyImmediate(camera); Object.DestroyImmediate(source); }
        }

        private static void ValidateDefaultAssets()
        {
            const string directory = "Assets/Data/AnimalPhoto/Rendering/";
            var basic = AssetDatabase.LoadAssetAtPath<PhotoCameraDefinition>(directory + "BasicCamera.asset");
            var instant = AssetDatabase.LoadAssetAtPath<PhotoCameraDefinition>(directory + "InstantCamera.asset");
            var robot = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Resources/Robot/RobotMarker.prefab");
            Require(basic != null && basic.RenderSet is BasicPhotoRenderSet, "Basic camera asset is not bound correctly");
            Require(instant != null && instant.RenderSet is InstantFilmPhotoRenderSet, "Instant camera asset is not bound correctly");
            Require(basic.CameraId != instant.CameraId && basic.RenderSet.SetId != instant.RenderSet.SetId,
                "Default models/Sets do not have independent identities");
            Require(robot != null && robot.GetComponent<PhotoModeController>() != null &&
                robot.GetComponent<PhotoModeController>().CurrentCamera == instant,
                "Runtime robot prefab is not using the Instant Camera");
            Require(((InstantFilmPhotoRenderSnapshot)instant.Capture(42).RenderSnapshot).Settings.strength > 0,
                "Authored Instant Set contains disabled or missing settings");
        }

        private static void ExportSamples(Report report)
        {
            string[] paths =
            {
                "Assets/Arts/Animal_Photos/Rocky_Mountains/Muskrat/Muskrat_10.jpg",
                "Assets/Arts/Animal_Photos/Rocky_Mountains/Pileated_Woodpecker/PileatedWoodpecker_06.JPG"
            };
            var html = new System.Text.StringBuilder("<!doctype html><meta charset='utf-8'><title>Photo rendering comparison</title>" +
                "<style>body{background:#202124;color:#eee;font:16px system-ui;margin:30px}section{display:flex;gap:16px;flex-wrap:wrap}figure{margin:0;width:30%}img{width:100%}figcaption{margin:8px 0 24px}</style>" +
                "<h1>Photo rendering validation</h1><p>Same original image and crop for every column. Default Instant Film settings, fixed grain seed 1234.</p>");
            foreach (string path in paths)
            {
                Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Require(source != null, "Sample asset is missing: " + path);
                string stem = Path.GetFileNameWithoutExtension(path);
                InstantFilmSettings settings = InstantFilmSettings.Default;
                var versions = new List<KeyValuePair<string, AnimalPhoto>>
                {
                    new KeyValuePair<string, AnimalPhoto>("input", new AnimalPhoto(source, Full, 1)),
                    new KeyValuePair<string, AnimalPhoto>("instant", Film(source, Full, settings, 1234))
                };
                settings.softnessEnabled = true;
                settings.softness = 0.5f;
                versions.Add(new KeyValuePair<string, AnimalPhoto>("instant-softness", Film(source, Full, settings, 1234)));
                html.Append("<h2>").Append(stem).Append("</h2><section>");
                foreach (var version in versions)
                {
                    string file = stem + "-" + version.Key + ".png";
                    Pixels pixels = Render(version.Value, 1024);
                    Require(HasUsefulRange(pixels), "Sample has no useful visible range: " + file);
                    WritePng(pixels, Path.Combine(OutputDirectory, file));
                    report.samples.Add(file);
                    html.Append("<figure><img src='").Append(file).Append("'><figcaption>").Append(version.Key).Append("</figcaption></figure>");
                }
                html.Append("</section>");
            }
            File.WriteAllText(Path.Combine(OutputDirectory, "index.html"), html.ToString());
        }

        private static void ValidateShaders()
        {
            foreach (string name in new[] { "AnimalPhotoProcess", "AnimalPhotoInstantFilm" })
            {
                Shader shader = Resources.Load<Shader>(name);
                Require(shader != null && shader.isSupported, name + " is missing or unsupported");
                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                    Require(message.severity.ToString() != "Error", name + ": " + message.message);
            }
        }

        private static AnimalPhoto Film(Texture2D source, Rect crop, InstantFilmSettings settings, int seed, float legacySaturation = 1)
        {
            return new AnimalPhoto(source, crop, legacySaturation, new PhotoCaptureSettings("validation", "Validation",
                new InstantFilmPhotoRenderSnapshot("validation-instant", 1, seed, settings)));
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
            MethodInfo info = target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
            Require(info != null, "Capture integration method is missing: " + method);
            info.Invoke(target, arguments);
        }

        private static Texture2D MakeFixture(int width, int height, float alpha = 1)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            { name = "Photo validation gradient", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float u = (float)x / (width - 1);
                float v = (float)y / (height - 1);
                float detail = ((x / 3 + y / 3) % 2 == 0) ? 0.03f : -0.03f;
                pixels[y * width + x] = new Color(Mathf.Clamp01(0.08f + u * 0.84f + detail),
                    Mathf.Clamp01(0.12f + v * 0.73f), Mathf.Clamp01(0.8f - u * 0.55f), alpha);
            }
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static Pixels Render(AnimalPhoto photo, int maximumSize)
        {
            RenderTexture previous = RenderTexture.active;
            bool previousWrite = GL.sRGBWrite;
            var sentinel = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32);
            RenderTexture output = null;
            try
            {
                sentinel.Create();
                RenderTexture.active = sentinel;
                GL.sRGBWrite = !previousWrite;
                bool expectedWrite = GL.sRGBWrite;
                output = photo.Render(maximumSize);
                Require(RenderTexture.active == sentinel && GL.sRGBWrite == expectedWrite,
                    "Renderer did not restore RenderTexture.active / GL.sRGBWrite");
                return Read(output);
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = previousWrite;
                AnimalPhotoProcessing.Release(output);
                AnimalPhotoProcessing.Release(sentinel);
            }
        }

        private static Pixels RenderLegacy(Texture2D source, Rect crop, float saturation, int size)
        {
            var output = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32);
            var material = new Material(Resources.Load<Shader>("AnimalPhotoProcess"));
            RenderTexture previous = RenderTexture.active;
            bool previousWrite = GL.sRGBWrite;
            try
            {
                output.Create();
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                material.SetVector("_Crop", new Vector4(crop.x, crop.y, crop.width, crop.height));
                material.SetFloat("_Saturation", saturation);
                Graphics.Blit(source, output, material);
                return Read(output);
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = previousWrite;
                AnimalPhotoProcessing.Release(output);
                Object.DestroyImmediate(material);
            }
        }

        private static Pixels Read(RenderTexture output)
        {
            var readable = new Texture2D(output.width, output.height, TextureFormat.RGBA32, false, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = output;
                readable.ReadPixels(new Rect(0, 0, output.width, output.height), 0, 0, false);
                readable.Apply(false, false);
                return new Pixels { Width = output.width, Height = output.height, Colors = readable.GetPixels32() };
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(readable); }
        }

        private static int MaxDifference(Pixels first, Pixels second)
        {
            Require(first.Width == second.Width && first.Height == second.Height, "Compared output sizes differ");
            int maximum = 0;
            for (int i = 0; i < first.Colors.Length; i++)
            {
                Color32 a = first.Colors[i], b = second.Colors[i];
                maximum = Math.Max(maximum, Math.Max(Math.Abs(a.r - b.r), Math.Max(Math.Abs(a.g - b.g),
                    Math.Max(Math.Abs(a.b - b.b), Math.Abs(a.a - b.a)))));
            }
            return maximum;
        }

        private static void RequireDifferenceAtMost(Pixels first, Pixels second, int tolerance, string message)
        {
            int difference = MaxDifference(first, second);
            Require(difference <= tolerance, message + "; maximum byte difference=" + difference);
        }

        private static bool HasUsefulRange(Pixels pixels)
        {
            int minimum = 255, maximum = 0;
            foreach (Color32 color in pixels.Colors)
            {
                int value = (color.r + color.g + color.b) / 3;
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
            }
            return maximum - minimum > 40;
        }

        private static void WritePng(Pixels pixels, string path)
        {
            var texture = new Texture2D(pixels.Width, pixels.Height, TextureFormat.RGBA32, false, false);
            try { texture.SetPixels32(pixels.Colors); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); }
            finally { Object.DestroyImmediate(texture); }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
