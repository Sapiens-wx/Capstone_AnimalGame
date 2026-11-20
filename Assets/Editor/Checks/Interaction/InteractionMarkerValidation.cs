using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnimalGame.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AnimalGame.Interaction.Editor
{
    /// <summary>Disposable GPU and Play Mode checks; run only in an isolated project mirror.</summary>
    public sealed class InteractionMarkerValidation : ScriptableObject
    {
        private const int Size = 768;
        private const string SourceProject = "C:/Users/30791/Documents/GitHub/Capstone_AnimalGame";
        private enum Phase { WaitingForPlay, Rotation, Suppressed, ParentHidden, Resumed, Finishing }

        [Serializable] private sealed class Report
        {
            public string startedUtc, finishedUtc, isolatedProject;
            public bool passed;
            public int fullPixels, interiorBaselinePixels, exteriorBaselinePixels, clippedInteriorPixels, clippedExteriorPixels;
            public int stationAddedGoldPixels;
            public float sampledSeconds, outerRotationDegrees, innerRotationDegrees;
            public List<string> checks = new List<string>();
            public List<string> errors = new List<string>();
            public List<string> screenshots = new List<string>();
        }

        [SerializeField] private bool running;
        [SerializeField] private Phase phase;
        [SerializeField] private Report report;
        [SerializeField] private string outputDirectory;
        [SerializeField] private double deadline;
        [SerializeField] private int lastFrame = -1, phaseFrame;
        [SerializeField] private float sampleTime, initialOuter, initialInner, initialSecondOuter, previousTimeScale;
        [SerializeField] private bool previousRunInBackground;
        [SerializeField] private InteractionMarker first, second;
        [SerializeField] private GameObject secondParent;

        public static void RunBatch()
        {
            InteractionMarkerValidation session = null;
            try
            {
                Require(Application.isBatchMode, "Marker validation is batch-only.");
                Require(!Environment.GetCommandLineArgs().Any(value => string.Equals(value, "-quit", StringComparison.OrdinalIgnoreCase)),
                    "Omit -quit for the asynchronous Play Mode checks.");
                string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd('/', '\\');
                Require(!string.Equals(project.Replace('\\', '/'), SourceProject, StringComparison.OrdinalIgnoreCase),
                    "Refusing to run in the source project; use an isolated mirror.");
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Start marker validation in Edit Mode.");
                Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "GPU checks require a graphics device; omit -nographics.");
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    Require(!SceneManager.GetSceneAt(i).isDirty, "Refusing to replace an unsaved scene.");
                session = CreateInstance<InteractionMarkerValidation>();
                session.hideFlags = HideFlags.HideAndDontSave;
                session.outputDirectory = OutputDirectory();
                Directory.CreateDirectory(session.outputDirectory);
                session.report = new Report { startedUtc = DateTime.UtcNow.ToString("O"), isolatedProject = project };
                session.SaveReport();
                GameObject asset = InteractionMarkerBuilder.EnsureAssets();
                session.CheckAsset(asset);
                session.CheckPixels(asset);
                session.CheckStationPixels();
                session.CreatePlayFixture(asset);
                session.previousTimeScale = Time.timeScale;
                session.previousRunInBackground = Application.runInBackground;
                Time.timeScale = 1f;
                Application.runInBackground = true;
                session.running = true;
                session.phase = Phase.WaitingForPlay;
                session.deadline = EditorApplication.timeSinceStartup + 100d;
                session.Subscribe();
                EditorApplication.isPlaying = true;
            }
            catch (Exception exception)
            {
                if (session != null && session.report != null)
                {
                    session.report.errors.Add(exception.ToString());
                    session.ExitBatch();
                }
                Debug.LogException(exception);
                if (Application.isBatchMode) EditorApplication.Exit(1); else throw;
            }
        }

        private void CheckAsset(GameObject asset)
        {
            Require(asset != null, "The reusable marker prefab is missing.");
            InteractionMarker marker = asset.GetComponent<InteractionMarker>();
            Require(marker != null && marker.OuterRing != null && marker.InnerRing != null, "The prefab must reference both rings.");
            Require(Mathf.Abs(new SerializedObject(marker).FindProperty("diameter").floatValue - .75f) < .0001f,
                "The reusable marker must use the approved .75 default diameter.");
            Require(asset.GetComponentsInChildren<Renderer>(true).Length == 2
                && asset.GetComponentsInChildren<Collider>(true).Length == 0,
                "The generic presentation prefab must contain exactly two renderers and no interaction collider.");
            Material material = marker.OuterRing.sharedMaterial;
            Require(material != null && marker.InnerRing.sharedMaterial == material && AssetDatabase.Contains(material),
                "The rings must use one persistent shared material.");
            Require(material.shader != null && material.shader.name == "AnimalGame/Interaction Marker"
                && !ShaderUtil.ShaderHasError(material.shader), "The fail-closed marker shader did not compile.");
            CheckMesh(marker.OuterRing, .45f, .5f);
            CheckMesh(marker.InnerRing, .25f, .3f);
            Require(marker.OuterRing.sortingOrder == 25 && marker.InnerRing.sortingOrder == 25,
                "Both rings must sort above the station artwork at order 25.");
            report.checks.Add("Reusable prefab has two persistent ring meshes, one supported shared material, correct equal stroke widths and no interaction collider.");
        }

        private static void CheckMesh(MeshRenderer renderer, float minimumRadius, float maximumRadius)
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            Require(filter != null && filter.sharedMesh != null && AssetDatabase.Contains(filter.sharedMesh), "Each ring needs a persistent mesh asset.");
            Vector3[] vertices = filter.sharedMesh.vertices;
            Require(vertices.Length >= 32 && filter.sharedMesh.triangles.Length >= 24, "The ring mesh is empty or undersampled.");
            float minimum = float.MaxValue, maximum = 0f;
            foreach (Vector3 vertex in vertices)
            {
                Require(Mathf.Abs(vertex.z) < .00001f, "Ring geometry must lie strictly in its XY plane.");
                float radius = new Vector2(vertex.x, vertex.y).magnitude;
                minimum = Mathf.Min(minimum, radius); maximum = Mathf.Max(maximum, radius);
            }
            Require(Mathf.Abs(minimum - minimumRadius) < .0001f && Mathf.Abs(maximum - maximumRadius) < .0001f,
                "The saved ring radii do not match the approved diameters and equal .05 stroke width.");
        }

        private void CheckPixels(GameObject asset)
        {
            string[] floatNames = { "_PlayerUiClipEnabled", "_PlayerUiClipRadiusPixels", "_PlayerUiClipSoftnessPixels" };
            string[] vectorNames = { "_PlayerUiClipCenterPixels", "_ScreenParams", "_ProjectionParams" };
            float[] savedFloats = Array.ConvertAll(floatNames, Shader.GetGlobalFloat);
            Vector4[] savedVectors = Array.ConvertAll(vectorNames, Shader.GetGlobalVector);
            RenderTexture previousTarget = RenderTexture.active;
            Scene preview = EditorSceneManager.NewPreviewScene();
            RenderTexture target = null;
            var images = new List<Texture2D>();
            try
            {
                GameObject owner = (GameObject)PrefabUtility.InstantiatePrefab(asset, preview);
                owner.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                owner.transform.localScale = Vector3.one;
                InteractionMarker marker = owner.GetComponent<InteractionMarker>();
                marker.SetSuppressed(false);
                var cameraObject = new GameObject("Interaction marker GPU camera") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(cameraObject, preview);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false; camera.scene = preview;
                camera.transform.SetPositionAndRotation(new Vector3(0f, 0f, -10f), Quaternion.identity);
                camera.orthographic = true; camera.orthographicSize = .75f; camera.aspect = 1f;
                camera.nearClipPlane = .1f; camera.farClipPlane = 30f;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.allowHDR = camera.allowMSAA = false;
                target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32)
                    { hideFlags = HideFlags.HideAndDontSave, antiAliasing = 1 };
                target.Create(); camera.targetTexture = target;

                PlayerUiOrganicVisibility.DisableClip();
                Require(CountLit(Capture(camera, target, images)) == 0, "A marker flashed before the first valid circular-view clip.");
                report.checks.Add("The initial clip-disabled frame contains no marker pixels.");
                PlayerUiOrganicVisibility.SetClip(Vector2.one * (Size * .5f), Size * .49f, 1f);
                Texture2D full = Capture(camera, target, images);
                SaveImage(full, "marker-full.png");
                report.fullPixels = CountLit(full);
                Require(report.fullPixels > 10000, "The enabled marker is missing or too faint.");
                float diameter = new SerializedObject(marker).FindProperty("diameter").floatValue;
                CheckArcPattern(full, .475f * diameter);
                CheckArcPattern(full, .275f * diameter);
                report.checks.Add("GPU output shows four 60-degree arcs per ring, separated by 30-degree gaps, with an empty center.");
                Require(!Lit(full.GetPixel(Size / 2, Size / 2)), "The center of the marker must be empty.");

                Vector2 center = new Vector2(Size * .3f, Size * .5f);
                float radius = Size * .32f;
                CountRegions(full, center, radius, out report.interiorBaselinePixels, out report.exteriorBaselinePixels);
                Require(report.interiorBaselinePixels > 1000 && report.exteriorBaselinePixels > 1000,
                    "The partial-circle fixture must cross both rings rather than hide or reveal them entirely.");
                PlayerUiOrganicVisibility.SetClip(center, radius, 1f);
                Texture2D edge = Capture(camera, target, images);
                SaveImage(edge, "marker-circle-edge.png");
                CountRegions(edge, center, radius, out report.clippedInteriorPixels, out report.clippedExteriorPixels);
                Require(report.clippedExteriorPixels == 0 && report.clippedInteriorPixels >= report.interiorBaselinePixels * .99f,
                    "Per-pixel clipping must retain interior arcs and remove all pixels outside the view circle.");
                report.checks.Add("A circle crossing both rings retains interior pixels and removes every exterior pixel, rather than toggling by the marker center.");
                PlayerUiOrganicVisibility.SetClip(new Vector2(Size * 2f, Size * 2f), 30f, 1f);
                Require(CountLit(Capture(camera, target, images)) == 0, "A marker remains visible wholly outside the circle.");
                PlayerUiOrganicVisibility.SetClip(Vector2.one * (Size * .5f), 0f, 1f);
                Require(CountLit(Capture(camera, target, images)) == 0, "A zero-radius clip must hide the marker.");
                PlayerUiOrganicVisibility.DisableClip();
                Require(CountLit(Capture(camera, target, images)) == 0, "Disabling the player-view clip leaked marker pixels.");
                report.checks.Add("Outside-circle, zero-radius and subsequently disabled clips all hide the marker completely.");

                camera.orthographicSize = 1.4f;
                owner.transform.position = new Vector3(-.65f, 0f, 0f);
                GameObject otherOwner = new GameObject("Second independent owner");
                SceneManager.MoveGameObjectToScene(otherOwner, preview);
                otherOwner.transform.position = new Vector3(.65f, 0f, 0f);
                GameObject other = (GameObject)PrefabUtility.InstantiatePrefab(asset, preview);
                other.transform.SetParent(otherOwner.transform, false);
                InteractionMarker otherMarker = other.GetComponent<InteractionMarker>(); otherMarker.SetSuppressed(false);
                PlayerUiOrganicVisibility.SetClip(Vector2.one * (Size * .5f), Size * 2f, 1f);
                Texture2D both = Capture(camera, target, images); SaveImage(both, "marker-two-owners.png");
                int leftBefore = CountHalf(both, false), rightBefore = CountHalf(both, true);
                Require(leftBefore > 1000 && rightBefore > 1000, "Both independent marker instances must render.");
                marker.SetSuppressed(true);
                Texture2D suppressed = Capture(camera, target, images); SaveImage(suppressed, "marker-one-owner-suppressed.png");
                Require(CountHalf(suppressed, false) == 0 && CountHalf(suppressed, true) >= rightBefore * .99f,
                    "Suppressing one owner must not affect another marker using the shared material.");
                otherOwner.SetActive(false);
                Require(CountLit(Capture(camera, target, images)) == 0, "An inactive owner hierarchy leaked marker pixels.");
                report.checks.Add("Independent markers render together; suppressing one leaves the other intact, and deactivating its parent removes its pixels.");
            }
            finally
            {
                RenderTexture.active = previousTarget;
                EditorSceneManager.ClosePreviewScene(preview);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                foreach (Texture2D image in images) if (image != null) Object.DestroyImmediate(image);
                for (int i = 0; i < floatNames.Length; i++) Shader.SetGlobalFloat(floatNames[i], savedFloats[i]);
                for (int i = 0; i < vectorNames.Length; i++) Shader.SetGlobalVector(vectorNames[i], savedVectors[i]);
            }
        }

        private static void CheckArcPattern(Texture2D image, float worldRadius)
        {
            const int samples = 720;
            bool[] lit = new bool[samples];
            float pixels = worldRadius * Size / 1.5f;
            int count = 0, groups = 0;
            for (int i = 0; i < samples; i++)
            {
                float radians = i * Mathf.PI * 2f / samples;
                int x = Mathf.RoundToInt(Size * .5f + Mathf.Cos(radians) * pixels);
                int y = Mathf.RoundToInt(Size * .5f + Mathf.Sin(radians) * pixels);
                lit[i] = Lit(image.GetPixel(x, y)); if (lit[i]) count++;
            }
            for (int i = 0; i < samples; i++) if (lit[i] && !lit[(i + samples - 1) % samples]) groups++;
            Require(groups == 4 && count >= 466 && count <= 494,
                "Rendered ring must have exactly four arcs occupying approximately 240 degrees; got " + groups + " arcs and " + count * .5f + " degrees.");
        }

        private void CheckStationPixels()
        {
            const string stationPath = "Assets/Demos/CheckpointRefit/Prefabs/CheckpointRefitTerminal.prefab";
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(stationPath);
            Require(asset != null, "The integrated station prefab is missing.");
            string[] floatNames = { "_PlayerUiClipEnabled", "_PlayerUiClipRadiusPixels", "_PlayerUiClipSoftnessPixels" };
            string[] vectorNames = { "_PlayerUiClipCenterPixels", "_ScreenParams", "_ProjectionParams" };
            float[] savedFloats = Array.ConvertAll(floatNames, Shader.GetGlobalFloat);
            Vector4[] savedVectors = Array.ConvertAll(vectorNames, Shader.GetGlobalVector);
            RenderTexture previousTarget = RenderTexture.active;
            Scene preview = EditorSceneManager.NewPreviewScene();
            RenderTexture target = null;
            var images = new List<Texture2D>();
            try
            {
                GameObject station = (GameObject)PrefabUtility.InstantiatePrefab(asset, preview);
                station.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                InteractionMarker[] markers = station.GetComponentsInChildren<InteractionMarker>(true);
                Require(markers.Length == 1, "The station must contain exactly one reusable interaction marker.");
                InteractionMarker marker = markers[0];
                Require(Vector3.Distance(marker.transform.position, station.transform.position) < .001f,
                    "The station marker must be centered on the interaction point.");
                var cameraObject = new GameObject("Integrated station marker GPU camera") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(cameraObject, preview);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false; camera.scene = preview;
                camera.transform.SetPositionAndRotation(new Vector3(0f, 0f, -10f), Quaternion.identity);
                camera.orthographic = true; camera.orthographicSize = 2.2f; camera.aspect = 1f;
                camera.nearClipPlane = .1f; camera.farClipPlane = 30f;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.allowHDR = camera.allowMSAA = false;
                target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32)
                    { hideFlags = HideFlags.HideAndDontSave, antiAliasing = 1 };
                target.Create(); camera.targetTexture = target;
                PlayerUiOrganicVisibility.SetClip(Vector2.one * (Size * .5f), Size, 1f);
                marker.SetSuppressed(true);
                Texture2D baseline = Capture(camera, target, images);
                SaveImage(baseline, "station-without-marker.png");
                marker.SetSuppressed(false);
                Texture2D integrated = Capture(camera, target, images);
                SaveImage(integrated, "station-with-marker.png");
                Color32[] before = baseline.GetPixels32(), after = integrated.GetPixels32();
                Vector3 viewport = camera.WorldToViewportPoint(marker.transform.position);
                Vector2 center = new Vector2(viewport.x * Size, viewport.y * Size);
                float radiusPixels = .4f * Size / (camera.orthographicSize * 2f);
                for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
                {
                    if (Vector2.Distance(new Vector2(x + .5f, y + .5f), center) > radiusPixels) continue;
                    int index = y * Size + x;
                    if (Gold(after[index]) && !Gold(before[index])) report.stationAddedGoldPixels++;
                }
                Require(report.stationAddedGoldPixels > 100,
                    "The integrated marker must add visible gold arcs above the station artwork in the central docking area.");
                report.checks.Add("Saved station prefab centers one reusable marker; its two rings add " + report.stationAddedGoldPixels
                    + " visible gold pixels above the station artwork in the docking area.");
            }
            finally
            {
                RenderTexture.active = previousTarget;
                EditorSceneManager.ClosePreviewScene(preview);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                foreach (Texture2D image in images) if (image != null) Object.DestroyImmediate(image);
                for (int i = 0; i < floatNames.Length; i++) Shader.SetGlobalFloat(floatNames[i], savedFloats[i]);
                for (int i = 0; i < vectorNames.Length; i++) Shader.SetGlobalVector(vectorNames[i], savedVectors[i]);
            }
        }

        private void CreatePlayFixture(GameObject asset)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject a = (GameObject)PrefabUtility.InstantiatePrefab(asset); a.name = "Marker A";
            var parent = new GameObject("Marker B Owner");
            GameObject b = (GameObject)PrefabUtility.InstantiatePrefab(asset); b.name = "Marker B";
            b.transform.SetParent(parent.transform, false);
            var serialized = new SerializedObject(b.GetComponent<InteractionMarker>());
            serialized.FindProperty("tint").colorValue = new Color(.3f, .8f, .9f, 1f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private void OnEnable() { if (running) Subscribe(); }
        private void OnDisable() { EditorApplication.update -= Tick; Application.logMessageReceived -= OnLog; }
        private void Subscribe()
        {
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
        }

        private void Tick()
        {
            if (!running) return;
            try
            {
                if (phase == Phase.Finishing)
                {
                    if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode) ExitBatch();
                    else if (EditorApplication.timeSinceStartup > deadline) { report.errors.Add("Timed out leaving Play Mode."); ExitBatch(); }
                    return;
                }
                Require(EditorApplication.timeSinceStartup < deadline, "Timed out during " + phase + ".");
                EditorApplication.QueuePlayerLoopUpdate();
                if (!EditorApplication.isPlaying) return;
                if (lastFrame == Time.frameCount) return;
                lastFrame = Time.frameCount;
                switch (phase)
                {
                    case Phase.WaitingForPlay:
                        first = GameObject.Find("Marker A").GetComponent<InteractionMarker>();
                        second = GameObject.Find("Marker B").GetComponent<InteractionMarker>();
                        secondParent = second.transform.parent.gameObject;
                        Require(first.PresentationEnabled && second.PresentationEnabled, "New instances must allow presentation independently.");
                        Require(first.OuterRing.sharedMaterial == second.OuterRing.sharedMaterial, "Instances must retain the same material asset.");
                        var propertiesA = new MaterialPropertyBlock(); var propertiesB = new MaterialPropertyBlock();
                        first.OuterRing.GetPropertyBlock(propertiesA); second.OuterRing.GetPropertyBlock(propertiesB);
                        Require(propertiesA.GetColor("_Color") != propertiesB.GetColor("_Color"), "Per-instance tint must use independent property blocks.");
                        Sample(Phase.Rotation); break;
                    case Phase.Rotation:
                        if (!Settled(.75f)) break;
                        float elapsed = Time.time - sampleTime;
                        report.sampledSeconds = elapsed;
                        report.outerRotationDegrees = Mathf.DeltaAngle(initialOuter, Angle(first.OuterRing));
                        report.innerRotationDegrees = Mathf.DeltaAngle(initialInner, Angle(first.InnerRing));
                        Require(Mathf.Abs(report.outerRotationDegrees - elapsed * -30f) < 2f
                            && Mathf.Abs(report.innerRotationDegrees - elapsed * 45f) < 2f,
                            "Actual LateUpdate rotation must be clockwise 30 degrees/sec and counterclockwise 45 degrees/sec.");
                        report.checks.Add("Real Play Mode samples confirm independent opposite ring rotation at -30 and +45 degrees per scaled second.");
                        first.SetSuppressed(true); Sample(Phase.Suppressed); break;
                    case Phase.Suppressed:
                        if (!Settled(.35f)) break;
                        Require(first.IsSuppressed && !first.PresentationEnabled && !first.OuterRing.enabled && !first.InnerRing.enabled,
                            "Owner suppression must disable both ring renderers.");
                        Require(Mathf.Abs(Mathf.DeltaAngle(initialOuter, Angle(first.OuterRing))) < .01f
                            && Mathf.Abs(Mathf.DeltaAngle(initialInner, Angle(first.InnerRing))) < .01f,
                            "Suppressed rings must stop rotating.");
                        Require(second.PresentationEnabled && second.OuterRing.enabled
                            && Mathf.Abs(Mathf.DeltaAngle(initialSecondOuter, Angle(second.OuterRing))) > 3f,
                            "Suppressing one instance must not freeze another instance.");
                        report.checks.Add("Owner suppression stops only its own renderers and rotation; the other differently tinted instance continues using the shared material.");
                        secondParent.SetActive(false); Sample(Phase.ParentHidden); break;
                    case Phase.ParentHidden:
                        if (!Settled(.2f)) break;
                        Require(!second.PresentationEnabled && !second.OuterRing.enabled && !second.InnerRing.enabled,
                            "OnDisable must hide both rings when the parent becomes inactive.");
                        secondParent.SetActive(true);
                        Require(second.PresentationEnabled && second.OuterRing.enabled && second.InnerRing.enabled,
                            "Parent reactivation must restore both renderers.");
                        second.enabled = false;
                        Require(!second.OuterRing.enabled && !second.InnerRing.enabled, "Disabling just the component must hide both rings.");
                        second.enabled = true;
                        Require(second.OuterRing.enabled && second.InnerRing.enabled, "Re-enabling just the component must restore both rings.");
                        Require(first.IsSuppressed && !first.OuterRing.enabled, "Another owner's reactivation cleared the first owner's suppression.");
                        first.SetSuppressed(false); Sample(Phase.Resumed); break;
                    case Phase.Resumed:
                        if (!Settled(.35f)) break;
                        Require(first.PresentationEnabled && first.OuterRing.enabled && first.InnerRing.enabled
                            && Mathf.DeltaAngle(initialOuter, Angle(first.OuterRing)) < -3f
                            && Mathf.DeltaAngle(initialInner, Angle(first.InnerRing)) > 3f,
                            "Unsuppressing must resume both rotations.");
                        Require(Shader.GetGlobalFloat("_PlayerUiClipEnabled") == 0f,
                            "Generic marker components must not enable or write the player's global view clip.");
                        report.checks.Add("Parent/component disable and re-enable restore the correct local owner state; unsuppression resumes rotation without modifying view globals.");
                        Finish(null); break;
                }
            }
            catch (Exception exception) { Finish(exception); }
        }

        private void Sample(Phase next)
        {
            phase = next; sampleTime = Time.time; phaseFrame = Time.frameCount;
            initialOuter = Angle(first.OuterRing); initialInner = Angle(first.InnerRing); initialSecondOuter = Angle(second.OuterRing);
            SaveReport();
        }
        private bool Settled(float seconds) => Time.time - sampleTime >= seconds && Time.frameCount - phaseFrame >= 4;
        private static float Angle(MeshRenderer ring) => ring.transform.localEulerAngles.z;
        private void OnLog(string message, string stack, LogType type)
        {
            if (!running || phase == Phase.Finishing) return;
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            { report.errors.Add(type + ": " + message + "\n" + stack); SaveReport(); }
        }
        private void Finish(Exception failure)
        {
            if (phase == Phase.Finishing) return;
            if (failure != null) report.errors.Add(failure.ToString());
            phase = Phase.Finishing; deadline = EditorApplication.timeSinceStartup + 20d;
            Time.timeScale = previousTimeScale; Application.runInBackground = previousRunInBackground;
            SaveReport(); EditorApplication.isPlaying = false;
        }
        private void ExitBatch()
        {
            if (report == null) return;
            report.finishedUtc = DateTime.UtcNow.ToString("O"); report.passed = report.errors.Count == 0;
            SaveReport(); running = false;
            EditorApplication.update -= Tick; Application.logMessageReceived -= OnLog;
            Debug.Log("Interaction marker validation " + (report.passed ? "PASS" : "FAIL") + ": " + report.checks.Count + " checks; " + outputDirectory);
            int code = report.passed ? 0 : 1; Object.DestroyImmediate(this); EditorApplication.Exit(code);
        }

        private static Texture2D Capture(Camera camera, RenderTexture target, List<Texture2D> images)
        {
            camera.Render(); RenderTexture.active = target;
            var image = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            images.Add(image); image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); image.Apply(); return image;
        }
        private static bool Lit(Color32 color) => Mathf.Max(color.r, Mathf.Max(color.g, color.b)) > 20;
        private static bool Gold(Color32 color) => color.r > 80 && color.g > 60 && color.b < color.r * .7f && color.b < color.g * .85f;
        private static int CountLit(Texture2D image) => image.GetPixels32().Count(Lit);
        private static int CountHalf(Texture2D image, bool right)
        {
            Color32[] pixels = image.GetPixels32(); int count = 0;
            for (int y = 0; y < Size; y++) for (int x = right ? Size / 2 : 0; x < (right ? Size : Size / 2); x++)
                if (Lit(pixels[y * Size + x])) count++;
            return count;
        }
        private static void CountRegions(Texture2D image, Vector2 center, float radius, out int inside, out int outside)
        {
            inside = outside = 0; Color32[] pixels = image.GetPixels32();
            for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
            {
                if (!Lit(pixels[y * Size + x])) continue;
                float distance = Vector2.Distance(new Vector2(x + .5f, y + .5f), center);
                if (distance < radius - 3f) inside++; else if (distance > radius + 3f) outside++;
            }
        }
        private void SaveImage(Texture2D image, string name)
        { string path = Path.Combine(outputDirectory, name); File.WriteAllBytes(path, image.EncodeToPNG()); report.screenshots.Add(path); }
        private void SaveReport()
        { if (report != null && !string.IsNullOrEmpty(outputDirectory)) File.WriteAllText(Path.Combine(outputDirectory, "marker-validation.json"), JsonUtility.ToJson(report, true)); }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static string OutputDirectory()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], "-logFile", StringComparison.OrdinalIgnoreCase) && args[i + 1] != "-")
                    return Path.GetDirectoryName(Path.GetFullPath(args[i + 1]));
            return Path.GetFullPath(Path.Combine("output", "validation", "interaction-marker"));
        }
    }
}
