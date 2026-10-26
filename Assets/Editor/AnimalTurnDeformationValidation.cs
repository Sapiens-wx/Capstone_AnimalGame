using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using AnimalGame.Animals;
using AnimalGame.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AnimalGame.EditorTools
{
    /// <summary>Isolated player-loop and GPU checks. Saves only a disposable empty validation scene.</summary>
    public static class AnimalTurnDeformationValidation
    {
        private const string OutputDirectory = "output/validation/animal-turn";
        private const string RequestPath = "Temp/AnimalTurnDeformationValidation.request";
        private const string SessionPrefix = "AnimalTurnDeformationValidation.";
        private const string MaterialPath = "Assets/Materials/Animals/AnimalTurnDeformation.mat";
        private const string ShaderName = "AnimalGame/Animal Turn Deformation";
        private static readonly string[] Prefabs =
        {
            "Assets/Prefabs/Animals/Muskrat/Muskrat_Placeholder.prefab",
            "Assets/Prefabs/Animals/PileatedWoodpecker/PileatedWoodpecker_Placeholder.prefab",
            "Assets/Prefabs/Animals/MountainGoat/MountainGoat_Placeholder.prefab",
            "Assets/Prefabs/Animals/MountainGoat/MountainGoat_Juvenile_Placeholder.prefab",
            "Assets/Prefabs/Animals/Wolf/Wolf_Placeholder.prefab"
        };
        private static readonly int RendererToVisual = Shader.PropertyToID("_TurnRendererToVisual");
        private static readonly int VisualToRenderer = Shader.PropertyToID("_TurnVisualToRenderer");
        private static readonly int HeadTail = Shader.PropertyToID("_TurnHeadTail");
        private static readonly int Bend = Shader.PropertyToID("_TurnBendRadians");
        private static readonly int Sentinel = Shader.PropertyToID("_AnimalTurnValidationSentinel");
        private static readonly string[] ClipProperties =
        {
            "_PlayerUiClipEnabled", "_PlayerUiClipCenterPixels", "_PlayerUiClipRadiusPixels", "_PlayerUiClipSoftnessPixels"
        };
        private static readonly List<Sprite> PendingDestroyedSprites = new List<Sprite>();
        private static Report runtimeReport;
        private static PlayerLoopSystem originalPlayerLoop;
        private static bool playerLoopInstalled;

        [Serializable]
        private sealed class Report
        {
            public string unityVersion;
            public string graphicsDevice;
            public string completedUtc;
            public bool passed;
            public List<string> checks = new List<string>();
            public List<string> measurements = new List<string>();
            public List<string> errors = new List<string>();
            public List<string> samples = new List<string>();
        }

        private sealed class Pixels
        {
            public int Width;
            public int Height;
            public Color32[] Colors;
        }

        private sealed class SpriteSnapshot
        {
            public readonly Sprite Sprite;
            public readonly Material Material;
            public readonly Color Color;
            public readonly bool Enabled;
            private readonly Vector2[] vertices;
            private readonly Vector2[] uv;
            private readonly ushort[] triangles;
            private readonly Rect rect;
            private readonly Vector2 pivot;
            private readonly float pixelsPerUnit;

            public SpriteSnapshot(SpriteRenderer renderer)
            {
                Sprite = renderer.sprite;
                Material = renderer.sharedMaterial;
                Color = renderer.color;
                Enabled = renderer.enabled;
                Require(Sprite != null, renderer.name + " is missing its authored body Sprite");
                vertices = Sprite.vertices;
                uv = Sprite.uv;
                triangles = Sprite.triangles;
                rect = Sprite.rect;
                pivot = Sprite.pivot;
                pixelsPerUnit = Sprite.pixelsPerUnit;
            }

            public void CheckSourceUnmodified()
            {
                Require(Sprite != null && Sprite.rect == rect && Sprite.pivot == pivot
                    && Sprite.pixelsPerUnit == pixelsPerUnit, "An authored Sprite was modified or destroyed");
                Equal(vertices, Sprite.vertices, "Source geometry was modified");
                Equal(uv, Sprite.uv, "Source UVs were modified");
                Equal(triangles, Sprite.triangles, "Source triangles were modified");
            }

            public void CheckRuntime(Sprite runtime)
            {
                Require(runtime != null && runtime != Sprite, "Body still uses the unsubdivided source Sprite");
                Require(runtime.texture == Sprite.texture && runtime.rect == rect && runtime.pivot == pivot
                    && Mathf.Abs(runtime.pixelsPerUnit - pixelsPerUnit) < .0001f,
                    "Runtime subdivision changed texture, crop, pivot or pixels per unit");
                Require(runtime.vertices.Length == 125 && runtime.triangles.Length == 576,
                    "Runtime Sprite must have a 4 by 24 grid (125 vertices / 576 indices)");

                // Fit the source's affine position-to-UV transform. This also handles an atlas rotation.
                int second = -1, third = -1;
                for (int i = 1; i < vertices.Length && third < 0; i++)
                for (int j = i + 1; j < vertices.Length; j++)
                    if (Mathf.Abs(Cross(vertices[i] - vertices[0], vertices[j] - vertices[0])) > .000001f)
                    { second = i; third = j; break; }
                Require(third >= 0, "Source Sprite has no nondegenerate UV reference triangle");
                Vector2 a = vertices[second] - vertices[0], b = vertices[third] - vertices[0];
                float determinant = Cross(a, b);
                Vector2[] actualVertices = runtime.vertices, actualUv = runtime.uv;
                for (int i = 0; i < actualVertices.Length; i++)
                {
                    Vector2 d = actualVertices[i] - vertices[0];
                    float u = Cross(d, b) / determinant, v = Cross(a, d) / determinant;
                    Vector2 expected = uv[0] + u * (uv[second] - uv[0]) + v * (uv[third] - uv[0]);
                    Require(Vector2.Distance(actualUv[i], expected) < .00005f,
                        "Subdivision changed the source's UV mapping at vertex " + i);
                }
                CheckSourceUnmodified();
            }
        }

        private sealed class Fixture : IDisposable
        {
            public readonly Scene Scene;
            public readonly GameObject Root;
            public readonly AnimalTurnDeformation Turn;
            public readonly Transform Visual;
            public readonly SpriteRenderer[] Bodies;
            public readonly SpriteSnapshot[] Sources;

            public Fixture(string path)
            {
                GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Require(source != null, "Missing animal prefab: " + path);
                Scene = Application.isPlaying ? SceneManager.CreateScene("Animal turn fixture " + Guid.NewGuid().ToString("N"))
                    : EditorSceneManager.NewPreviewScene();
                GameObject inactiveParent = null;
                try
                {
                    if (Application.isPlaying)
                    {
                        inactiveParent = new GameObject("Inactive validation setup");
                        SceneManager.MoveGameObjectToScene(inactiveParent, Scene);
                        inactiveParent.SetActive(false);
                    }
                    Root = inactiveParent != null ? Object.Instantiate(source, inactiveParent.transform) : Object.Instantiate(source);
                    Root.hideFlags = HideFlags.HideAndDontSave;
                    Root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    if (Root.transform.parent == null) SceneManager.MoveGameObjectToScene(Root, Scene);
                    Turn = Root.GetComponent<AnimalTurnDeformation>();
                    Require(Turn != null, path + " has no AnimalTurnDeformation");
                    Visual = Field<Transform>(Turn, "visualRoot");
                    Bodies = Field<SpriteRenderer[]>(Turn, "bodyRenderers");
                    Require(Visual != null && Bodies != null && Bodies.Length == 2, "Expected a Visual Root and two body renderers: " + path);
                    Sources = new SpriteSnapshot[Bodies.Length];
                    for (int i = 0; i < Bodies.Length; i++) Sources[i] = new SpriteSnapshot(Bodies[i]);
                    if (Application.isPlaying)
                    {
                        foreach (MonoBehaviour behaviour in Root.GetComponentsInChildren<MonoBehaviour>(true))
                            if (behaviour != Turn) behaviour.enabled = false;
                        Root.transform.SetParent(null, false);
                        Root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                        // Disabled discovery scripts still receive Awake; restore their authored body state afterwards.
                        for (int i = 0; i < Bodies.Length; i++)
                        { Bodies[i].enabled = Sources[i].Enabled; Bodies[i].color = Sources[i].Color; }
                    }
                }
                catch
                {
                    if (Root != null) Object.DestroyImmediate(Root);
                    CloseScene(Scene);
                    throw;
                }
                finally { if (inactiveParent != null) Object.DestroyImmediate(inactiveParent); }
            }

            public void Tick(float deltaTime) => Invoke(Turn, "TickVisuals", deltaTime);
            public void Reset(float angle = 0f)
            {
                Root.transform.rotation = Quaternion.Euler(0f, 0f, angle);
                Turn.ResetDeformation();
                Tick(1f / 60f);
            }
            public void Rotate(float angle, float deltaTime = 1f / 60f)
            {
                Root.transform.rotation = Quaternion.Euler(0f, 0f, angle);
                Tick(deltaTime);
            }
            public void Stop()
            {
                Turn.enabled = false;
                // Ordinary MonoBehaviours do not get deterministic Edit Mode lifecycle events.
                Invoke(Turn, "OnDisable");
            }
            public void Dispose()
            {
                if (Root != null)
                {
                    if (Turn != null) Stop();
                    Object.DestroyImmediate(Root);
                }
                if (Scene.IsValid()) CloseScene(Scene);
            }
            private static void CloseScene(Scene scene)
            {
                if (Application.isPlaying) SceneManager.UnloadSceneAsync(scene);
                else EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [MenuItem("Animal Game/Validation/Run Animal Turn Deformation Checks")]
        public static void RunMenu() => Run(false);

        // Explicit request files let an already open editor run once after its next script import.
        // No request means no callbacks, no polling and no effect on the current scene.
        [InitializeOnLoadMethod]
        private static void QueueRequestedValidation()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeState;
            EditorApplication.playModeStateChanged += OnPlayModeState;
            if (SessionState.GetString(SessionPrefix + "Phase", "") == "Exiting" && !EditorApplication.isPlaying)
                EditorApplication.delayCall += FinishEditorRun;
            if (File.Exists(RequestPath)) EditorApplication.delayCall += RunRequestedValidation;
        }

        private static void RunRequestedValidation()
        {
            if (!File.Exists(RequestPath)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += RunRequestedValidation;
                return;
            }
            File.Delete(RequestPath);
            Run(false);
        }

        // Keep graphics enabled: Unity -batchmode -executeMethod AnimalGame.EditorTools.AnimalTurnDeformationValidation.RunBatch
        public static void RunBatch() => Run(true);

        private static void Run(bool exit)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(SessionPrefix + "Restore", false))
            {
                Debug.LogError("Start animal turn validation from Edit Mode after the previous run has finished.");
                return;
            }
            var report = new Report
            {
                unityVersion = Application.unityVersion,
                graphicsDevice = SystemInfo.graphicsDeviceType + ": " + SystemInfo.graphicsDeviceName
            };
            try
            {
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Start animal turn validation from Edit Mode");
                Require(!SessionState.GetBool(SessionPrefix + "Restore", false), "Animal turn validation is already running");
                Check(report, "Existing animal AI, motor teleport, species and corpse regressions", ValidateExistingAnimalRegressions);
                SessionState.SetBool(SessionPrefix + "Restore", true);
                SessionState.SetBool(SessionPrefix + "Exit", exit);
                SessionState.SetString(SessionPrefix + "OriginalStartScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
                SessionState.SetBool(SessionPrefix + "OriginalOptionsEnabled", EditorSettings.enterPlayModeOptionsEnabled);
                SessionState.SetInt(SessionPrefix + "OriginalOptions", (int)EditorSettings.enterPlayModeOptions);
                SessionState.SetBool(SessionPrefix + "OriginalPaused", EditorApplication.isPaused);
                SessionState.SetFloat(SessionPrefix + "OriginalTimeScale", Time.timeScale);
                string folder = "__AnimalTurnValidation_" + Guid.NewGuid().ToString("N");
                string directory = "Assets/" + folder;
                SessionState.SetString(SessionPrefix + "TemporaryDirectory", directory);
                AssetDatabase.CreateFolder("Assets", folder);
                Scene active = SceneManager.GetActiveScene();
                Scene temporary = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                string scenePath = directory + "/Validation.unity";
                try { Require(EditorSceneManager.SaveScene(temporary, scenePath), "Could not save the isolated validation scene"); }
                finally
                {
                    EditorSceneManager.CloseScene(temporary, true);
                    if (active.IsValid()) SceneManager.SetActiveScene(active);
                }
                EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
                Require(EditorSceneManager.playModeStartScene != null, "Could not import the isolated validation scene");
                // Force scene and domain reload; the user's loaded scenes are restored by Unity on exit.
                EditorSettings.enterPlayModeOptionsEnabled = false;
                EditorApplication.isPaused = false;
                SessionState.SetString(SessionPrefix + "Report", JsonUtility.ToJson(report));
                SessionState.SetString(SessionPrefix + "Phase", "Entering");
                EditorApplication.EnterPlaymode();
            }
            catch (Exception error)
            {
                report.errors.Add(error.ToString());
                SaveReport(report);
                SessionState.SetBool(SessionPrefix + "Passed", false);
                FinishEditorRun();
            }
        }

        private static void OnPlayModeState(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(SessionPrefix + "Restore", false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                runtimeReport = JsonUtility.FromJson<Report>(SessionState.GetString(SessionPrefix + "Report", "{}"));
                Time.timeScale = 1f;
                EditorApplication.isPaused = false;
                PendingDestroyedSprites.Clear();
                originalPlayerLoop = PlayerLoop.GetCurrentPlayerLoop();
                PlayerLoopSystem loop = originalPlayerLoop;
                loop.subSystemList = (PlayerLoopSystem[])originalPlayerLoop.subSystemList.Clone();
                bool installed = false;
                for (int i = 0; i < loop.subSystemList.Length; i++)
                    if (loop.subSystemList[i].type == typeof(UnityEngine.PlayerLoop.Update))
                    {
                        PlayerLoopSystem update = loop.subSystemList[i];
                        var systems = new List<PlayerLoopSystem>(update.subSystemList);
                        systems.Add(new PlayerLoopSystem { type = typeof(AnimalTurnDeformationValidation), updateDelegate = RuntimeUpdate });
                        update.subSystemList = systems.ToArray();
                        loop.subSystemList[i] = update;
                        installed = true;
                        break;
                    }
                if (!installed)
                {
                    runtimeReport.errors.Add("Unity player Update loop is unavailable");
                    CompleteRuntimeRun();
                    return;
                }
                playerLoopInstalled = true;
                SessionState.SetString(SessionPrefix + "Phase", "Running");
                PlayerLoop.SetPlayerLoop(loop);
            }
            else if (state == PlayModeStateChange.ExitingPlayMode) RestorePlayerLoop();
            else if (state == PlayModeStateChange.EnteredEditMode) FinishEditorRun();
        }

        private static void RuntimeUpdate()
        {
            string phase = SessionState.GetString(SessionPrefix + "Phase", "");
            if (phase == "Running")
            {
                // This executes inside Unity's real player Update, where mutable Sprite geometry is supported.
                RunRuntimeChecks(runtimeReport);
                SessionState.SetString(SessionPrefix + "Phase", "Drain");
            }
            else if (phase == "Drain")
            {
                try
                {
                    foreach (Sprite sprite in PendingDestroyedSprites)
                        Require(sprite == null, "Last animal did not release its generated Sprite after the frame ended");
                    runtimeReport.measurements.Add("Deferred destruction: " + PendingDestroyedSprites.Count + " generated Sprites released after one frame");
                }
                catch (Exception error) { runtimeReport.errors.Add("Shared sprite cache deferred cleanup: " + error); }
                PendingDestroyedSprites.Clear();
                CompleteRuntimeRun();
            }
        }

        private static void CompleteRuntimeRun()
        {
            SaveReport(runtimeReport);
            SessionState.SetBool(SessionPrefix + "Passed", runtimeReport.passed);
            SessionState.SetString(SessionPrefix + "Phase", "Exiting");
            RestorePlayerLoop();
            EditorApplication.ExitPlaymode();
        }

        private static void RestorePlayerLoop()
        {
            if (!playerLoopInstalled) return;
            PlayerLoop.SetPlayerLoop(originalPlayerLoop);
            playerLoopInstalled = false;
        }

        private static void FinishEditorRun()
        {
            if (!SessionState.GetBool(SessionPrefix + "Restore", false)) return;
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(SessionPrefix + "OriginalStartScene", ""));
            EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)SessionState.GetInt(SessionPrefix + "OriginalOptions", 0);
            EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool(SessionPrefix + "OriginalOptionsEnabled", false);
            Time.timeScale = SessionState.GetFloat(SessionPrefix + "OriginalTimeScale", 1f);
            EditorApplication.isPaused = SessionState.GetBool(SessionPrefix + "OriginalPaused", false);
            string directory = SessionState.GetString(SessionPrefix + "TemporaryDirectory", "");
            if (directory.StartsWith("Assets/__AnimalTurnValidation_", StringComparison.Ordinal) && AssetDatabase.IsValidFolder(directory))
                AssetDatabase.DeleteAsset(directory);
            bool exit = SessionState.GetBool(SessionPrefix + "Exit", false);
            bool passed = SessionState.GetBool(SessionPrefix + "Passed", false);
            SessionState.SetBool(SessionPrefix + "Restore", false);
            SessionState.EraseString(SessionPrefix + "Phase");
            if (exit && Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1);
        }

        private static void RunRuntimeChecks(Report report)
        {
            RenderTexture initialActive = RenderTexture.active;
            bool initialSrgb = GL.sRGBWrite;
            UnityEngine.Random.State initialRandom = UnityEngine.Random.state;
            Vector4[] initialClip = new Vector4[ClipProperties.Length];
            for (int i = 0; i < ClipProperties.Length; i++)
                initialClip[i] = i == 1 ? Shader.GetGlobalVector(ClipProperties[i]) : new Vector4(Shader.GetGlobalFloat(ClipProperties[i]), 0, 0, 0);
            try
            {
                Directory.CreateDirectory(OutputDirectory);
                Require(Application.isPlaying, "Run mutable geometry checks inside the player loop");
                PlayerUiOrganicVisibility.DisableClip();
                Check(report, "Five authored prefabs, body-only renderer selection and shader compatibility", () => ValidateConfiguration(report));
                Check(report, "Continuous heading, wraparound, reversal, pause, settling and frame rates", () => ValidateTiming(report));
                Check(report, "Subdivision, UV fidelity, shared cache, independent property blocks and cleanup", ValidateCacheAndLifecycle);
                Check(report, "Visual-space mapping, mirrored vertices and renderer bounds", ValidateBounds);
                Check(report, "Sprite replacement, juvenile scaling, visual offset and death", ValidateReplacementAndDeath);
                Check(report, "Real animal straight / left / right GPU samples", () => ExportAnimals(report));
                Check(report, "GPU deformation agrees with CPU mesh, transparency and player UI clip", () => ValidateGpuReference(report));
                Check(report, "Shader remains supported with no compilation errors", ValidateShader);
            }
            catch (Exception exception) { report.errors.Add(exception.ToString()); }
            finally
            {
                RenderTexture.active = initialActive;
                GL.sRGBWrite = initialSrgb;
                UnityEngine.Random.state = initialRandom;
                for (int i = 0; i < ClipProperties.Length; i++)
                    if (i == 1) Shader.SetGlobalVector(ClipProperties[i], initialClip[i]);
                    else Shader.SetGlobalFloat(ClipProperties[i], initialClip[i].x);
            }
        }

        private static void SaveReport(Report report)
        {
            report.completedUtc = DateTime.UtcNow.ToString("O");
            report.passed = report.errors.Count == 0;
            Directory.CreateDirectory(OutputDirectory);
            File.WriteAllText(Path.Combine(OutputDirectory, "report.json"), JsonUtility.ToJson(report, true));
            WriteGallery(report);
            Debug.Log($"Animal turn deformation validation {(report.passed ? "PASSED" : "FAILED")}: " +
                $"{report.checks.Count} checks; report: {Path.GetFullPath(OutputDirectory)}/report.json");
            if (!report.passed) Debug.LogError(string.Join("\n", report.errors));
        }

        private static void Check(Report report, string name, Action action)
        {
            try { action(); report.checks.Add(name); }
            catch (Exception exception) { report.errors.Add(name + ": " + exception); }
        }

        private static void ValidateConfiguration(Report report)
        {
            Material expected = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Require(expected != null && expected.shader != null && expected.shader.name == ShaderName,
                "Missing shared animal deformation material");
            string materialTag = expected.GetTag("DisableBatching", false, "<missing>");
            string shaderTag = expected.shader.FindSubshaderTagValue(0, new ShaderTagId("DisableBatching")).name;
            var passProbe = new Material(expected) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                Require(passProbe.SetPass(0), "Animal turn shader could not select its rendering pass");
                string activatedTag = passProbe.GetTag("DisableBatching", false, "<missing>");
                report.measurements.Add("DisableBatching: material='" + materialTag + "', activatedMaterial='" + activatedTag +
                    "', declaredSubshader='" + shaderTag + "', shaderName='" + expected.shader.name + "'");
                // Query the declaration directly: GetTag(false) depends on an already selected subshader,
                // which is not guaranteed when validation starts before its first editor camera render.
                Require(string.Equals(shaderTag, "True", StringComparison.OrdinalIgnoreCase),
                    "Shader must declare object-space dynamic batching disabled; material tag='" + materialTag +
                    "', activated material tag='" + activatedTag + "', declared shader tag='" + shaderTag + "'");
            }
            finally { Object.DestroyImmediate(passProbe); }
            foreach (string path in Prefabs)
            {
                using var f = new Fixture(path);
                Require(Mathf.Abs(f.Turn.FollowTimeSeconds - .12f) < .0001f
                    && Mathf.Abs(f.Turn.MaximumBendDegrees - 22f) < .0001f, "Unexpected authored defaults: " + path);
                Require(f.Visual.name == "Visual Root" && Vector2.Distance(f.Turn.HeadAnchor, f.Turn.TailAnchor) > .001f,
                    "Missing shared coordinate space or degenerate head / tail anchors: " + path);
                Require(Field<Material>(f.Turn, "turnMaterial") == expected, "Prefab does not reference shared turn material: " + path);
                var selected = new HashSet<SpriteRenderer>(f.Bodies);
                Require(selected.Count == 2, "Body renderer is listed twice: " + path);
                foreach (SpriteRenderer body in f.Bodies)
                    Require(body != null && body.transform.IsChildOf(f.Visual)
                        && (body.name == "Body Fill" || body.name == "Body Outline"),
                        "Only fill and outline may be deformed: " + path);
                foreach (SpriteRenderer renderer in f.Root.GetComponentsInChildren<SpriteRenderer>(true))
                    if (renderer.name.Contains("Direction") || renderer.name.Contains("Unknown"))
                        Require(!selected.Contains(renderer), "Direction / unknown visual was selected for bending: " + path);
                f.Tick(1f / 60f);
                PlayerUiOrganicVisibility.RegisterRenderers(f.Bodies);
                try
                {
                    foreach (SpriteRenderer body in f.Bodies)
                        Require(body.sharedMaterial == expected, "Player UI visibility replaced the animal shader: " + path);
                }
                finally { PlayerUiOrganicVisibility.UnregisterRenderers(f.Bodies); }
            }
        }

        private static void ValidateTiming(Report report)
        {
            using var f = new Fixture(Prefabs[0]);
            f.Reset();
            f.Rotate(90);
            Require(f.Turn.CurrentBendDegrees < -1 && f.Turn.CurrentBendDegrees >= -22.001f, "Left turn did not produce bounded tail lag");
            float left = f.Turn.CurrentBendDegrees;
            for (int i = 0; i < 30; i++) f.Tick(0);
            Require(Mathf.Abs(f.Turn.CurrentBendDegrees - left) < .00001f, "Pause continued relaxing the tail");
            for (int i = 0; i < 120; i++)
            {
                f.Tick(1f / 60f);
                Require(f.Turn.CurrentBendDegrees <= .00001f, "Settling overshot or rebounded");
            }
            Require(Mathf.Abs(f.Turn.CurrentBendDegrees) < .01f, "Body did not return straight after a turn");
            f.Reset();
            f.Rotate(-90);
            Require(f.Turn.CurrentBendDegrees > 1 && f.Turn.CurrentBendDegrees <= 22.001f, "Right turn did not produce the opposite bounded tail lag");
            f.Reset(359);
            f.Rotate(1);
            Require(f.Turn.CurrentBendDegrees < 0 && Mathf.Abs(f.Turn.CurrentBendDegrees) <= 2.001f,
                "359 to 1 degree wrap was interpreted as a full turn");
            f.Reset();
            f.Rotate(180);
            Require(Mathf.Abs(f.Turn.CurrentBendDegrees) > 1 && Mathf.Abs(f.Turn.CurrentBendDegrees) <= 22.001f,
                "180 degree reversal produced invalid / missing bend");
            f.Reset();
            for (int i = 1; i <= 30; i++) f.Rotate(i * 3);
            Require(f.Turn.CurrentBendDegrees < -1, "Sustained left turn lost lag");
            for (int i = 1; i <= 30; i++) f.Rotate(90 - i * 3);
            Require(f.Turn.CurrentBendDegrees > 1, "Continuous reverse turn did not change bend direction");
            float minimum = float.MaxValue, maximum = float.MinValue;
            foreach (int fps in new[] { 30, 60, 120 })
            {
                f.Reset();
                for (int frame = 1; frame <= fps; frame++) f.Rotate(90f * frame / fps, 1f / fps);
                float bend = f.Turn.CurrentBendDegrees;
                Require(bend < -1 && bend > -22, "Ordinary turn was either rigid or permanently clamped at " + fps + " FPS");
                report.measurements.Add(fps + " FPS constant 90 deg/s turn: tail lag " + bend.ToString("F4") + " deg");
                minimum = Mathf.Min(minimum, bend); maximum = Mathf.Max(maximum, bend);
            }
            Require(maximum - minimum < 1f, "Frame rates disagree by more than 1 degree: " + (maximum - minimum));
            f.Turn.ResetDeformation();
            Require(Mathf.Abs(f.Turn.CurrentBendDegrees) < .0001f, "Explicit spawn / teleport reset retained turn history");

            // Camera/debug Update can query before simulation; LateUpdate must still see the final heading.
            const float deltaTime = 1f / 60f;
            f.Reset();
            Invoke(f.Turn, "SynchronizeFrame", deltaTime, 100);
            f.Root.transform.rotation = Quaternion.Euler(0, 0, 10);
            Invoke(f.Turn, "SynchronizeFrame", deltaTime, 100);
            float finalPose = f.Turn.CurrentBendDegrees;
            Require(finalPose < -1, "An early bounds query locked the animal to its old pose for the rest of the frame");
            Invoke(f.Turn, "SynchronizeFrame", deltaTime, 100);
            Require(Mathf.Abs(f.Turn.CurrentBendDegrees - finalPose) < .00001f,
                "Repeated same-frame query integrated lag twice");
            Invoke(f.Turn, "SynchronizeFrame", deltaTime, 101);
            float nextPose = f.Turn.CurrentBendDegrees;
            Require(nextPose > finalPose && nextPose < 0, "Next-frame stationary query did not relax exactly once");
            Invoke(f.Turn, "SynchronizeFrame", deltaTime, 101);
            Require(Mathf.Abs(f.Turn.CurrentBendDegrees - nextPose) < .00001f,
                "Repeated second-frame query continued relaxing the lag");
            f.Reset(); f.Rotate(10, deltaTime);
            Require(Mathf.Abs(f.Turn.CurrentBendDegrees - finalPose) < .00001f,
                "Early / late queries changed the result compared with one final-pose integration");
        }

        private static void ValidateCacheAndLifecycle()
        {
            foreach (string path in Prefabs)
            {
                Sprite[] generated;
                using (var first = new Fixture(path))
                using (var second = new Fixture(path))
                {
                    first.Tick(1f / 60f); second.Tick(1f / 60f);
                    generated = new[] { first.Bodies[0].sprite, first.Bodies[1].sprite };
                    for (int i = 0; i < first.Bodies.Length; i++)
                    {
                        first.Sources[i].CheckRuntime(generated[i]);
                        Require(second.Bodies[i].sprite == generated[i], "Identical source Sprite was not cached across animals: " + path);
                        var block = new MaterialPropertyBlock();
                        first.Bodies[i].GetPropertyBlock(block);
                        block.SetFloat(Sentinel, 13 + i);
                        first.Bodies[i].SetPropertyBlock(block);
                    }
                    first.Reset(); first.Rotate(45);
                    var originalFirst = new MaterialPropertyBlock(); first.Bodies[0].GetPropertyBlock(originalFirst);
                    float firstBend = originalFirst.GetFloat(Bend);
                    second.Reset(); second.Rotate(-45);
                    var other = new MaterialPropertyBlock(); second.Bodies[0].GetPropertyBlock(other);
                    var after = new MaterialPropertyBlock(); first.Bodies[0].GetPropertyBlock(after);
                    Require(firstBend < 0 && other.GetFloat(Bend) > 0 && Mathf.Abs(after.GetFloat(Bend) - firstBend) < .000001f,
                        "Shared material leaked turn parameters between animals");
                    for (int i = 0; i < first.Bodies.Length; i++)
                    {
                        first.Bodies[i].GetPropertyBlock(after);
                        Require(after.GetFloat(Sentinel) == 13 + i, "Turn update erased another component's property block");
                        Require(first.Bodies[i].sprite == generated[i], "Runtime Sprite was recreated on an ordinary frame");
                    }
                    first.Stop();
                    for (int i = 0; i < first.Bodies.Length; i++)
                    {
                        Require(first.Bodies[i].sprite == first.Sources[i].Sprite
                            && first.Bodies[i].sharedMaterial == first.Sources[i].Material,
                            "Disable did not restore source Sprite / material: " + path);
                        Require(generated[i] != null && second.Bodies[i].sprite == generated[i], "Disable destroyed a cache entry another animal uses");
                        first.Sources[i].CheckSourceUnmodified();
                    }
                    first.Turn.enabled = true;
                    Invoke(first.Turn, "OnEnable");
                    first.Tick(1f / 60f);
                    Require(Mathf.Abs(first.Turn.CurrentBendDegrees) < .0001f, "Re-enable reused stale heading history");
                    for (int i = 0; i < first.Bodies.Length; i++)
                        Require(first.Bodies[i].sprite == generated[i], "Re-enable missed an existing shared cache entry");
                }
                foreach (Sprite sprite in generated)
                    if (Application.isPlaying) PendingDestroyedSprites.Add(sprite);
                    else Require(sprite == null, "Last animal did not release its generated Sprite: " + path);
            }
        }

        private static void ValidateBounds()
        {
            foreach (string path in Prefabs)
            {
                using var f = new Fixture(path);
                foreach (int direction in new[] { -1, 1 })
                foreach (int flips in new[] { 0, 1, 2, 3 })
                {
                    foreach (SpriteRenderer body in f.Bodies) { body.flipX = (flips & 1) != 0; body.flipY = (flips & 2) != 0; }
                    f.Reset(); f.Rotate(direction * 90f);
                    foreach (SpriteRenderer body in f.Bodies) CheckRendererBounds(f, body);
                }
                f.Visual.localPosition += new Vector3(.027f, -.018f, 0f); // Peck / body offsets do not change the shared axis.
                f.Visual.localScale *= .65f;
                f.Tick(1f / 60f);
                foreach (SpriteRenderer body in f.Bodies) CheckRendererBounds(f, body);
            }
        }

        private static void CheckRendererBounds(Fixture f, SpriteRenderer renderer)
        {
            var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
            Matrix4x4 toVisual = block.GetMatrix(RendererToVisual), fromVisual = block.GetMatrix(VisualToRenderer);
            Matrix4x4 expected = f.Visual.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            for (int i = 0; i < 16; i++) Require(Mathf.Abs(toVisual[i] - expected[i]) < .0001f, "Uploaded renderer-to-Visual Root matrix is incorrect");
            Vector4 headTail = block.GetVector(HeadTail);
            Require(Vector2.Distance(new Vector2(headTail.x, headTail.y), f.Turn.HeadAnchor) < .0001f
                && Vector2.Distance(new Vector2(headTail.z, headTail.w), f.Turn.TailAnchor) < .0001f,
                "Fill / outline did not receive the shared head and tail anchors");
            Bounds local = renderer.localBounds, world = renderer.bounds;
            local.Expand(.0002f); world.Expand(.0002f);
            foreach (Vector2 vertex in renderer.sprite.vertices)
            {
                Vector3 point = new Vector3(vertex.x * (renderer.flipX ? -1 : 1), vertex.y * (renderer.flipY ? -1 : 1), 0);
                Vector3 reference = Deform(point, toVisual, fromVisual, headTail, block.GetFloat(Bend));
                Require(local.Contains(reference), "Shader-deformed vertex lies outside SpriteRenderer.localBounds: " + renderer.name);
                Require(world.Contains(renderer.transform.TransformPoint(reference)), "Shader-deformed vertex lies outside world rendering / photo bounds");
            }
        }

        private static void ValidateReplacementAndDeath()
        {
            using var f = new Fixture(Prefabs[3]);
            var texture = new Texture2D(24, 40, TextureFormat.RGBA32, false);
            Sprite replacement = Sprite.Create(texture, new Rect(2, 3, 18, 31), new Vector2(.3f, .6f), 100);
            replacement.name = "Validation replacement body";
            try
            {
                f.Reset(); f.Rotate(90);
                f.Bodies[0].sprite = replacement;
                var replacementSource = new SpriteSnapshot(f.Bodies[0]);
                f.Visual.localScale *= .65f;
                f.Visual.localPosition += Vector3.up * .03f;
                f.Tick(1f / 60f);
                replacementSource.CheckRuntime(f.Bodies[0].sprite);
                CheckRendererBounds(f, f.Bodies[0]);
                f.Stop();
                Require(f.Bodies[0].sprite == replacement, "Disable lost an externally replaced juvenile body Sprite");
                f.Turn.enabled = true; Invoke(f.Turn, "OnEnable"); f.Tick(1f / 60f);
                AnimalAgent agent = f.Root.GetComponent<AnimalAgent>();
                Require(agent != null && agent.Config != null, "Juvenile has no health configuration");
                var health = new AnimalHealth(agent.Config);
                Set(agent, "<Health>k__BackingField", health);
                health.Damage(float.MaxValue);
                f.Tick(1f / 60f);
                Require(f.Bodies[0].sprite == replacement, "Death did not restore the latest undeformed body Sprite");
                Require(Mathf.Abs(f.Turn.CurrentBendDegrees) < .0001f, "Dead animal retained turn lag");
                f.Bodies[0].sprite = f.Sources[0].Sprite; // A corpse swap must remain authoritative on subsequent ticks.
                f.Tick(1f / 60f);
                Require(f.Bodies[0].sprite == f.Sources[0].Sprite, "Dead tick overwrote the corpse Sprite swap");
            }
            finally { Object.DestroyImmediate(replacement); Object.DestroyImmediate(texture); }
        }

        private static void ExportAnimals(Report report)
        {
            RequireGraphics();
            foreach (string path in Prefabs)
            {
                using var f = new Fixture(path);
                ShowOnlyBodies(f);
                float framing = Framing(f);
                Pixels straight = null;
                foreach (int direction in new[] { 0, 1, -1 })
                {
                    f.Reset(); if (direction != 0) f.Rotate(direction * 90);
                    string label = direction == 0 ? "straight" : direction > 0 ? "left" : "right";
                    string file = Path.GetFileNameWithoutExtension(path) + "-" + label + ".png";
                    Pixels pixels = Capture(f.Scene, f.Visual, framing);
                    Require(VisiblePixels(pixels) > 30, "Animal was invisible in GPU sample: " + file);
                    Require(!TouchesEdge(pixels), "Animal was clipped at the camera edge: " + file);
                    if (direction == 0) straight = pixels;
                    else Require(PixelDifference(straight, pixels) > .002f, "Turn did not change rendered body pixels: " + file);
                    WritePng(pixels, file, report);
                }
            }
        }

        private static void ValidateGpuReference(Report report)
        {
            RequireGraphics();
            using var f = new Fixture(Prefabs[0]);
            ShowOnlyBodies(f);
            f.Bodies[1].enabled = false;
            SpriteRenderer body = f.Bodies[0];
            Texture2D texture = MakeGrid();
            Sprite grid = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 64);
            grid.name = "Turn validation grid";
            GameObject cpuObject = null;
            Mesh cpuMesh = null;
            try
            {
                body.sprite = grid;
                body.color = Color.white;
                body.transform.localScale = new Vector3(.08f, .12f, 1);
                body.transform.localRotation = Quaternion.Euler(0, 0, 13);
                f.Turn.HeadAnchor = new Vector2(0, .12f);
                f.Turn.TailAnchor = new Vector2(0, -.12f);
                f.Reset(); f.Rotate(90);
                Sprite runtime = body.sprite;
                var block = new MaterialPropertyBlock(); body.GetPropertyBlock(block);
                float framing = Framing(f);
                Pixels gpu = Capture(f.Scene, f.Visual, framing);
                WritePng(gpu, "grid-gpu-bend.png", report);
                Require(VisiblePixels(gpu) > 1000, "GPU reference fixture is too small to distinguish bend / clipping");

                // Independent CPU mesh preserves the runtime UVs and compares the shader's actual rendered result.
                Vector2[] vertices = runtime.vertices;
                var positions = new Vector3[vertices.Length];
                var colors = new Color[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                {
                    positions[i] = Deform(vertices[i], block.GetMatrix(RendererToVisual), block.GetMatrix(VisualToRenderer), block.GetVector(HeadTail), block.GetFloat(Bend));
                    colors[i] = body.color;
                }
                ushort[] indices = runtime.triangles;
                var meshIndices = new int[indices.Length];
                for (int i = 0; i < indices.Length; i++) meshIndices[i] = indices[i];
                cpuMesh = new Mesh { name = "CPU turn reference", hideFlags = HideFlags.HideAndDontSave };
                cpuMesh.vertices = positions; cpuMesh.uv = runtime.uv; cpuMesh.colors = colors; cpuMesh.triangles = meshIndices; cpuMesh.RecalculateBounds();
                cpuObject = new GameObject("CPU deformed reference") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(cpuObject, f.Scene);
                cpuObject.transform.SetParent(body.transform, false);
                cpuObject.AddComponent<MeshFilter>().sharedMesh = cpuMesh;
                var cpuRenderer = cpuObject.AddComponent<MeshRenderer>();
                cpuRenderer.sharedMaterial = body.sharedMaterial;
                var cpuBlock = new MaterialPropertyBlock();
                cpuBlock.SetTexture("_MainTex", texture);
                cpuBlock.SetColor("_RendererColor", Color.white);
                cpuBlock.SetFloat(Bend, 0);
                cpuRenderer.SetPropertyBlock(cpuBlock);
                body.enabled = false;
                Pixels cpu = Capture(f.Scene, f.Visual, framing);
                WritePng(cpu, "grid-cpu-reference.png", report);
                float difference = PixelDifference(gpu, cpu);
                report.measurements.Add("CPU mesh / GPU shader mean byte difference: " + difference.ToString("F6"));
                Require(difference < .008f, "GPU bend disagreed with the CPU reference mesh: " + difference);

                cpuObject.SetActive(false); body.enabled = true;
                body.color = new Color(.8f, .9f, .7f, .35f);
                Pixels faded = Capture(f.Scene, f.Visual, framing);
                Require(TotalAlpha(faded) < TotalAlpha(gpu) * .7f && VisiblePixels(faded) > 20,
                    "Renderer alpha did not fade the turn material");
                WritePng(faded, "grid-alpha.png", report);
                body.color = Color.white;
                Pixels clipBaseline = Capture(f.Scene, f.Visual, framing);
                WritePng(clipBaseline, "grid-ui-clip-before.png", report);
                RectInt visibleBounds = VisibleBounds(clipBaseline);
                Vector2 clipCenter = visibleBounds.center;
                float clipRadius = Mathf.Max(4, Mathf.Max(visibleBounds.width, visibleBounds.height) * .18f);
                const float clipSoftness = 3f;
                PlayerUiOrganicVisibility.SetClip(clipCenter, clipRadius, clipSoftness);
                Pixels clipped = Capture(f.Scene, f.Visual, framing);
                WritePng(clipped, "grid-ui-clip.png", report);
                float alphaRatio = (float)TotalAlpha(clipped) / Mathf.Max(1, TotalAlpha(clipBaseline));
                report.measurements.Add("GPU grid framing=" + framing.ToString("F5") + ", baseline bounds=" + visibleBounds +
                    ", clip center=" + clipCenter + ", radius=" + clipRadius.ToString("F2") +
                    ", alpha ratio=" + alphaRatio.ToString("F5") +
                    ", visible before/after=" + VisiblePixels(clipBaseline) + "/" + VisiblePixels(clipped) +
                    ", globals after render: enabled=" + Shader.GetGlobalFloat("_PlayerUiClipEnabled") +
                    ", center=" + Shader.GetGlobalVector("_PlayerUiClipCenterPixels") +
                    ", radius=" + Shader.GetGlobalFloat("_PlayerUiClipRadiusPixels"));
                Require(VisiblePixels(clipped) > 100 && alphaRatio > .05f && alphaRatio < .5f,
                    "Player UI circle should preserve a visible center and mask at least half of fixture alpha; alpha ratio=" + alphaRatio);
                for (int y = 0; y < clipped.Height; y++)
                for (int x = 0; x < clipped.Width; x++)
                    if (Vector2.Distance(new Vector2(x + .5f, y + .5f), clipCenter) > clipRadius + clipSoftness + 2)
                        Require(clipped.Colors[y * clipped.Width + x].a <= 1, "Deformed pixels leaked outside the player UI circle");
                PlayerUiOrganicVisibility.DisableClip();
            }
            finally
            {
                PlayerUiOrganicVisibility.DisableClip();
                if (cpuObject != null) Object.DestroyImmediate(cpuObject);
                if (cpuMesh != null) Object.DestroyImmediate(cpuMesh);
                f.Stop();
                Object.DestroyImmediate(grid); Object.DestroyImmediate(texture);
            }
        }

        private static Vector3 Deform(Vector3 point, Matrix4x4 toVisual, Matrix4x4 fromVisual, Vector4 anchors, float bend)
        {
            if (Mathf.Abs(bend) < .000001f) return point;
            Vector3 visual = toVisual.MultiplyPoint3x4(point);
            Vector2 head = new Vector2(anchors.x, anchors.y), tail = new Vector2(anchors.z, anchors.w);
            Vector2 axis = head - tail;
            float length = Mathf.Max(axis.magnitude, .0001f);
            float t = Mathf.Clamp01(Vector2.Dot(head - (Vector2)visual, axis / length) / length);
            float angle = bend * t * t * (3 - 2 * t);
            Vector2 offset = (Vector2)visual - head;
            float sine = Mathf.Sin(angle), cosine = Mathf.Cos(angle);
            Vector2 rotated = head + new Vector2(cosine * offset.x - sine * offset.y, sine * offset.x + cosine * offset.y);
            Vector3 local = fromVisual.MultiplyPoint3x4(new Vector3(rotated.x, rotated.y, visual.z));
            return new Vector3(local.x, local.y, point.z);
        }

        private static void ValidateShader()
        {
            Shader shader = Shader.Find(ShaderName);
            Require(shader != null && shader.isSupported, "Turn shader is missing or unsupported");
            foreach (var message in ShaderUtil.GetShaderMessages(shader))
                Require(message.severity.ToString() != "Error", message.message);
        }

        private static void ValidateExistingAnimalRegressions()
        {
            AnimalGame.Editor.AnimalAIRegressionChecks.Run();
            AnimalGame.Editor.AnimalSpeciesRegressionChecks.Run();
        }

        private static void ShowOnlyBodies(Fixture f)
        {
            var selected = new HashSet<SpriteRenderer>(f.Bodies);
            foreach (SpriteRenderer renderer in f.Root.GetComponentsInChildren<SpriteRenderer>(true))
                renderer.enabled = selected.Contains(renderer);
            foreach (SpriteRenderer body in f.Bodies) { body.gameObject.SetActive(true); body.color = Color.white; }
        }

        private static float Framing(Fixture f)
        {
            float radius = 0;
            foreach (SpriteRenderer body in f.Bodies)
            {
                if (!body.enabled || !body.gameObject.activeInHierarchy) continue;
                Bounds bounds = body.sprite.bounds;
                foreach (int x in new[] { -1, 1 })
                foreach (int y in new[] { -1, 1 })
                {
                    Vector3 point = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, 0));
                    radius = Mathf.Max(radius, (body.transform.TransformPoint(point) - f.Visual.position).magnitude);
                }
            }
            return Mathf.Max(.001f, radius * 1.5f);
        }

        private static Pixels Capture(Scene scene, Transform visual, float halfHeight)
        {
            const int size = 384;
            GameObject cameraObject = new GameObject("Animal turn validation camera") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var output = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                { hideFlags = HideFlags.HideAndDontSave, antiAliasing = 1 };
            RenderTexture previous = RenderTexture.active;
            var readable = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.scene = scene;
                camera.enabled = false;
                camera.orthographic = true; camera.orthographicSize = halfHeight;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
                camera.nearClipPlane = .01f; camera.farClipPlane = 100;
                camera.transform.rotation = visual.rotation;
                camera.transform.position = visual.position - camera.transform.forward * 10;
                camera.targetTexture = output;
                output.Create(); camera.Render();
                RenderTexture.active = output;
                readable.ReadPixels(new Rect(0, 0, size, size), 0, 0, false); readable.Apply(false, false);
                return new Pixels { Width = size, Height = size, Colors = readable.GetPixels32() };
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(readable); Object.DestroyImmediate(cameraObject);
                output.Release(); Object.DestroyImmediate(output);
            }
        }

        private static Texture2D MakeGrid()
        {
            const int width = 64, height = 128;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
                { name = "Animal turn colored grid", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var colors = new Color32[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                colors[y * width + x] = x < 2 || x >= width - 2 || y < 2 || y >= height - 2
                    ? new Color32(0, 0, 0, 0)
                    : (x % 8 == 0 || y % 8 == 0 ? new Color32(245, 245, 245, 255)
                    : new Color32((byte)(30 + x * 3), (byte)(30 + y), (byte)((x / 8 + y / 8) % 2 == 0 ? 60 : 210), 255));
            texture.SetPixels32(colors); texture.Apply(false, false); return texture;
        }

        private static void WritePng(Pixels pixels, string file, Report report)
        {
            var texture = new Texture2D(pixels.Width, pixels.Height, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixels32(pixels.Colors); texture.Apply();
                File.WriteAllBytes(Path.Combine(OutputDirectory, file), texture.EncodeToPNG());
                report.samples.Add(file);
            }
            finally { Object.DestroyImmediate(texture); }
        }

        private static void WriteGallery(Report report)
        {
            var html = new StringBuilder("<!doctype html><meta charset='utf-8'><title>Animal turn deformation validation</title><style>body{font:15px system-ui;background:#17212b;color:#eee;margin:2rem}main{display:flex;flex-wrap:wrap;gap:1rem}figure{margin:0;width:28rem}img{width:100%;background:repeating-conic-gradient(#39434c 0% 25%,#29333c 0% 50%) 0 0/24px 24px}figcaption{padding:.4rem;overflow-wrap:anywhere}</style>");
            html.Append("<h1>Animal turn deformation: ").Append(report.passed ? "PASS" : "FAIL").Append("</h1><p>Full results: report.json</p><main>");
            foreach (string file in report.samples)
                html.Append("<figure><img src='").Append(file).Append("'><figcaption>").Append(file).Append("</figcaption></figure>");
            html.Append("</main>"); File.WriteAllText(Path.Combine(OutputDirectory, "index.html"), html.ToString());
        }

        private static long TotalAlpha(Pixels pixels)
        {
            long total = 0; foreach (Color32 color in pixels.Colors) total += color.a; return total;
        }
        private static int VisiblePixels(Pixels pixels)
        {
            int count = 0; foreach (Color32 color in pixels.Colors) if (color.a > 5) count++; return count;
        }
        private static RectInt VisibleBounds(Pixels pixels)
        {
            int left = pixels.Width, right = -1, bottom = pixels.Height, top = -1;
            for (int y = 0; y < pixels.Height; y++)
            for (int x = 0; x < pixels.Width; x++)
                if (pixels.Colors[y * pixels.Width + x].a > 5)
                { left = Math.Min(left, x); right = Math.Max(right, x); bottom = Math.Min(bottom, y); top = Math.Max(top, y); }
            Require(right >= left && top >= bottom, "GPU reference image has no visible alpha bounds");
            return new RectInt(left, bottom, right - left + 1, top - bottom + 1);
        }
        private static bool TouchesEdge(Pixels pixels)
        {
            for (int i = 0; i < pixels.Width; i++)
                if (pixels.Colors[i].a > 1 || pixels.Colors[(pixels.Height - 1) * pixels.Width + i].a > 1) return true;
            for (int i = 0; i < pixels.Height; i++)
                if (pixels.Colors[i * pixels.Width].a > 1 || pixels.Colors[i * pixels.Width + pixels.Width - 1].a > 1) return true;
            return false;
        }
        private static float PixelDifference(Pixels first, Pixels second)
        {
            Require(first.Width == second.Width && first.Height == second.Height, "GPU image dimensions differ");
            long total = 0;
            for (int i = 0; i < first.Colors.Length; i++)
            {
                Color32 a = first.Colors[i], b = second.Colors[i];
                total += Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b) + Math.Abs(a.a - b.a);
            }
            return total / (first.Colors.Length * 4f * 255f);
        }
        private static void RequireGraphics() => Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "GPU checks require a graphics device; omit -nographics");
        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        private static T Field<T>(object owner, string name)
        {
            FieldInfo field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Require(field != null, "Missing integration field: " + name); return (T)field.GetValue(owner);
        }
        private static void Set(object owner, string name, object value)
        {
            FieldInfo field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Require(field != null, "Missing integration field: " + name); field.SetValue(owner, value);
        }
        private static void Invoke(object owner, string name, params object[] args)
        {
            MethodInfo method = owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Require(method != null, "Missing integration method: " + name);
            try { method.Invoke(owner, args); }
            catch (TargetInvocationException exception) { throw exception.InnerException ?? exception; }
        }
        private static void Equal<T>(T[] first, T[] second, string message)
        {
            Require(first.Length == second.Length, message);
            for (int i = 0; i < first.Length; i++) Require(EqualityComparer<T>.Default.Equals(first[i], second[i]), message);
        }
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
