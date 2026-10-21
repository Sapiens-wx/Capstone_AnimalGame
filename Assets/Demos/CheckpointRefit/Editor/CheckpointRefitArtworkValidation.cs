using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AnimalGame.Demos.CheckpointRefit.Editor
{
    /// <summary>Disposable artwork and GPU checks. Never rebuilds assets or saves a scene.</summary>
    public static class CheckpointRefitArtworkValidation
    {
        private const int Size = 768;
        private static readonly string[] FloatGlobals =
            { "_PlayerUiClipEnabled", "_PlayerUiClipRadiusPixels", "_PlayerUiClipSoftnessPixels" };
        private static readonly string[] VectorGlobals =
            { "_PlayerUiClipCenterPixels", "_ScreenParams", "_ProjectionParams" };

        [Serializable] private sealed class Report
        {
            public string startedUtc, finishedUtc;
            public bool passed;
            public int baselineInsidePixels, baselineOutsidePixels, clippedInsidePixels, clippedOutsidePixels;
            public List<string> checks = new List<string>();
            public List<string> errors = new List<string>();
            public List<string> screenshots = new List<string>();
        }

        [MenuItem("Animal Game/Demos/Checkpoint Refit/Validate Station Artwork")]
        public static void Run()
        {
            string output = OutputDirectory();
            Directory.CreateDirectory(output);
            var report = new Report { startedUtc = DateTime.UtcNow.ToString("O") };
            try
            {
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Leave Play Mode before validating artwork.");
                Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null,
                    "Artwork pixel checks require a graphics device; omit -nographics.");
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(CheckpointRefitDemoBuilder.TerminalPath);
                Require(asset != null, "The saved terminal prefab is missing.");
                Require(asset.GetComponentsInChildren<LineRenderer>(true).Length == 0,
                    "The terminal still contains placeholder LineRenderers.");
                SpriteRenderer[] artwork = asset.GetComponentsInChildren<SpriteRenderer>(true);
                Require(artwork.Length == 1 && artwork[0].sprite != null, "The terminal must contain its assembly-station sprite.");
                SpriteRenderer renderer = artwork[0];
                Require(AssetDatabase.GetAssetPath(renderer.sprite) == CheckpointRefitDemoBuilder.ArtworkPath,
                    "The terminal does not reference the approved assembly-station artwork.");
                Require(Mathf.Approximately(renderer.sprite.pixelsPerUnit, CheckpointRefitDemoBuilder.ArtworkPixelsPerUnit)
                    && renderer.sortingOrder == CheckpointRefitDemoBuilder.ArtworkSortingOrder,
                    "Station sprite scale or sorting does not match its builder configuration.");
                Require(renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null
                    && renderer.sharedMaterial.shader.name == "AnimalGame/Player UI Clipped Sprite"
                    && !ShaderUtil.ShaderHasError(renderer.sharedMaterial.shader),
                    "Station artwork must use the supported player-circle sprite shader.");
                report.checks.Add("Saved prefab uses the approved SpriteRenderer artwork, configured scale/sorting and circle shader, with no placeholder lines.");
                CheckPixels(asset, output, report);
                report.passed = true;
                Debug.Log("Checkpoint Refit artwork validation PASS: saved station artwork and circular visibility. Previews: " + output);
            }
            catch (Exception error) { report.errors.Add(error.ToString()); throw; }
            finally
            {
                report.finishedUtc = DateTime.UtcNow.ToString("O");
                File.WriteAllText(Path.Combine(output, "artwork-validation.json"), JsonUtility.ToJson(report, true));
            }
        }

        public static void RunBatch()
        {
            try { Run(); if (Application.isBatchMode) EditorApplication.Exit(0); }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1); else throw;
            }
        }

        private static void CheckPixels(GameObject asset, string output, Report report)
        {
            float[] floats = Array.ConvertAll(FloatGlobals, Shader.GetGlobalFloat);
            Vector4[] vectors = Array.ConvertAll(VectorGlobals, Shader.GetGlobalVector);
            RenderTexture previousTarget = RenderTexture.active;
            Scene preview = EditorSceneManager.NewPreviewScene();
            RenderTexture target = null;
            Texture2D baseline = null, clippedImage = null;
            try
            {
                GameObject station = (GameObject)PrefabUtility.InstantiatePrefab(asset, preview);
                station.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var cameraObject = new GameObject("Station artwork validation camera") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(cameraObject, preview);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.scene = preview;
                camera.transform.SetPositionAndRotation(new Vector3(0f, 0f, -10f), Quaternion.identity);
                camera.orthographic = true;
                camera.orthographicSize = 3.2f;
                camera.aspect = 1f;
                camera.nearClipPlane = .1f;
                camera.farClipPlane = 30f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.025f, .025f, .025f, 1f);
                camera.allowHDR = camera.allowMSAA = false;
                target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32)
                    { hideFlags = HideFlags.HideAndDontSave, antiAliasing = 1 };
                target.Create();
                camera.targetTexture = target;

                Shader.SetGlobalFloat("_PlayerUiClipEnabled", 0f);
                baseline = Capture(camera, target);
                SaveImage(baseline, output, "station-artwork.png", report);
                Color32[] first = baseline.GetPixels32();
                Vector2 center = new Vector2(Size * .36f, Size * .5f);
                float radius = Size * .29f;
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                    {
                        if (Brightness(first[y * Size + x]) <= 80) continue;
                        float distance = Vector2.Distance(new Vector2(x + .5f, y + .5f), center);
                        if (distance < radius - 3f) report.baselineInsidePixels++;
                        else if (distance > radius + 3f) report.baselineOutsidePixels++;
                    }
                Require(report.baselineInsidePixels > 100 && report.baselineOutsidePixels > 100,
                    "The station-only baseline must place visible lines inside and outside the test circle.");
                report.checks.Add("The station-only baseline contains visible linework on both sides of the test-circle boundary.");

                Shader.SetGlobalFloat("_PlayerUiClipEnabled", 1f);
                Shader.SetGlobalVector("_PlayerUiClipCenterPixels", new Vector4(center.x, center.y, 0f, 0f));
                Shader.SetGlobalFloat("_PlayerUiClipRadiusPixels", radius);
                Shader.SetGlobalFloat("_PlayerUiClipSoftnessPixels", 1f);
                clippedImage = Capture(camera, target);
                SaveImage(clippedImage, output, "station-visibility-clip.png", report);
                Color32[] clipped = clippedImage.GetPixels32();
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                    {
                        int index = y * Size + x;
                        float distance = Vector2.Distance(new Vector2(x + .5f, y + .5f), center);
                        bool litAfter = Brightness(clipped[index]) > 80;
                        if (distance < radius - 3f)
                        {
                            if (litAfter) report.clippedInsidePixels++;
                        }
                        else if (distance > radius + 3f)
                        {
                            if (litAfter) report.clippedOutsidePixels++;
                        }
                    }
                Require(report.clippedOutsidePixels == 0, "Station lines remain visible outside the player circle.");
                Require(report.clippedInsidePixels >= report.baselineInsidePixels * .95f,
                    "Clipping removed the station lines inside the player circle.");
                report.checks.Add("An offset player-view circle removes every exterior bright pixel while retaining its interior station linework.");
            }
            finally
            {
                RenderTexture.active = previousTarget;
                EditorSceneManager.ClosePreviewScene(preview);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                if (baseline != null) Object.DestroyImmediate(baseline);
                if (clippedImage != null) Object.DestroyImmediate(clippedImage);
                for (int i = 0; i < FloatGlobals.Length; i++) Shader.SetGlobalFloat(FloatGlobals[i], floats[i]);
                for (int i = 0; i < VectorGlobals.Length; i++) Shader.SetGlobalVector(VectorGlobals[i], vectors[i]);
            }
        }

        private static Texture2D Capture(Camera camera, RenderTexture target)
        {
            camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            try { image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); image.Apply(); return image; }
            catch { Object.DestroyImmediate(image); throw; }
        }

        private static int Brightness(Color32 color) => Mathf.Max(color.r, Mathf.Max(color.g, color.b));
        private static void Require(bool condition, string message) => CheckpointRefitValidation.Require(condition, message);
        private static void SaveImage(Texture2D image, string output, string name, Report report)
        {
            string path = Path.Combine(output, name);
            File.WriteAllBytes(path, image.EncodeToPNG());
            report.screenshots.Add(path);
        }
        private static string OutputDirectory()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], "-logFile", StringComparison.OrdinalIgnoreCase) && args[i + 1] != "-")
                    return Path.GetDirectoryName(Path.GetFullPath(args[i + 1]));
            return Path.GetFullPath(Path.Combine("output", "validation", "checkpoint-refit-artwork"));
        }
    }
}
