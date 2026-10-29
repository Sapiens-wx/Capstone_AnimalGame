using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AnimalGame.Animals;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using Hud = AnimalGame.MainUI.MainUI;

namespace AnimalGame.Editor
{
    /// <summary>Exercises the real nested HUD/result documents in a disposable graphics-enabled project.</summary>
    public static class StartupUiVisibilityRegressionChecks
    {
        private const string Pending = "AnimalGame.StartupUiVisibilityChecks.Pending";

        [InitializeOnLoadMethod]
        private static void Subscribe() => EditorApplication.playModeStateChanged += OnPlayState;

        public static void RunBatch()
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            if (!Application.isBatchMode || !project.Contains("/Temp/StartupUiVisibilityValidation-"))
                throw new InvalidOperationException("Run startup visibility checks in a disposable Temp/StartupUiVisibilityValidation project.");
            try
            {
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                    throw new InvalidOperationException("Startup UI verification requires native graphics.");
                TutorialPhotoResultObservationChecks.Run();
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); return; }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(Pending, true);
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayState(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            new GameObject("Startup UI visibility checks").AddComponent<StartupUiVisibilityRuntimeChecks>();
        }
    }

    public sealed class StartupUiVisibilityRuntimeChecks : MonoBehaviour
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        [Serializable] private sealed class Report
        {
            public bool passed;
            public string unityVersion, graphicsDevice;
            public List<string> checks = new List<string>();
            public List<string> images = new List<string>();
            public string error;
        }
        private readonly Report report = new Report();
        private GameObject instance;
        private GameObject bootstrapObject;
        private HeightMapPlayerSceneBootstrap previousBootstrap;
        private UIDocument hudDocument, resultDocument;
        private PhotoResultView result;
        private Hud hud;
        private PanelSettings panelSettings;
        private RenderTexture target;
        private Texture2D photoSource;
        private string prefix;

        private IEnumerator Start()
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            prefix = Path.Combine(Path.GetDirectoryName(project), Path.GetFileName(project));
            report.unityVersion = Application.unityVersion;
            report.graphicsDevice = SystemInfo.graphicsDeviceType + ": " + SystemInfo.graphicsDeviceName;
            IEnumerator steps = Checks();
            while (true)
            {
                object wait;
                try { if (!steps.MoveNext()) break; wait = steps.Current; }
                catch (Exception error)
                {
                    report.error = error.ToString(); Save(); Debug.LogException(error);
                    EditorApplication.Exit(1); yield break;
                }
                yield return wait;
            }
            report.passed = true; Save();
            Debug.Log("STARTUP UI VISIBILITY CHECKS PASSED: " + report.checks.Count + " assertions; native " + report.graphicsDevice);
            EditorApplication.Exit(0);
        }

        private IEnumerator Checks()
        {
            previousBootstrap = HeightMapPlayerSceneBootstrap.inst;
            bootstrapObject = new GameObject("Inactive HUD dependency fixture");
            bootstrapObject.SetActive(false);
            HeightMapPlayerSceneBootstrap.inst = bootstrapObject.AddComponent<HeightMapPlayerSceneBootstrap>();
            var editorAudio = AssetDatabase.LoadAllAssetsAtPath("Assets/Plugins/FMOD/Resources/FMODStudioSettings.asset")
                .OfType<FMODUnity.PlatformPlayInEditor>().Single();
            Require(editorAudio.Overlay == FMODUnity.TriStateBool.Disabled,
                "The FMOD editor performance overlay is disabled instead of obscuring the HUD.");
            Require(Resources.Load<Font>("UI/Font/Orbitron-Medium") != null
                && Resources.Load<Font>("UI/Font/Orbitron-Regular") != null
                && Resources.Load<UnityEngine.TextCore.Text.FontAsset>("UI/Photo/GeosansLightStatic") != null,
                "The actual HUD and photo-result resource fonts are available to native Toolkit rendering.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Resources/UI/MainUI.prefab");
            Require(prefab != null, "Real MainUI prefab is available.");
            instance = Instantiate(prefab);
            hud = instance.GetComponent<Hud>();
            hudDocument = instance.GetComponent<UIDocument>();
            result = instance.GetComponentInChildren<PhotoResultView>(true);
            resultDocument = result.GetComponent<UIDocument>();
            Require(hud != null && hudDocument != null && result != null && resultDocument != null,
                "Real MainUI prefab contains the nested photograph result document.");
            for (int i = 0; i < 6; i++) yield return null;
            CheckHidden("Initial Play Mode startup");
            Require(Object.FindObjectsByType<Hud>(FindObjectsSortMode.None).Length == 1 && Hud.Active == hud,
                "Startup creates exactly one active HUD owner.");

            panelSettings = Instantiate(hudDocument.panelSettings);
            SetTarget(1920, 1080);
            hudDocument.panelSettings = panelSettings;
            resultDocument.panelSettings = panelSettings;
            for (int i = 0; i < 3; i++) yield return null;
            CheckHidden("Native 1920x1080 panel");
            Capture("Startup-1920x1080");

            SetTarget(2200, 1240);
            for (int i = 0; i < 3; i++) yield return null;
            CheckHidden("Native 2200x1240 panel");
            Capture("Startup-2200x1240");

            VisualElement before = resultDocument.rootVisualElement.Q("stage");
            hudDocument.enabled = false;
            yield return null;
            hudDocument.enabled = true;
            for (int i = 0; i < 3; i++) yield return null;
            CheckHidden("Parent UIDocument recreation");
            Capture("After-Parent-Reenable");

            before = resultDocument.rootVisualElement.Q("stage");
            resultDocument.enabled = false;
            yield return null;
            resultDocument.enabled = true;
            for (int i = 0; i < 3; i++) yield return null;
            CheckHidden("Nested UIDocument recreation");

            VisualTreeAsset resultTree = resultDocument.visualTreeAsset;
            before = resultDocument.rootVisualElement.Q("stage");
            resultDocument.visualTreeAsset = null;
            resultDocument.visualTreeAsset = resultTree;
            for (int i = 0; i < 3; i++) yield return null;
            Require(before != resultDocument.rootVisualElement.Q("stage"), "Changing the actual UIDocument source forces Unity to construct a new result tree.");
            CheckHidden("Native result source reconstruction");

            instance.SetActive(false);
            yield return null;
            instance.SetActive(true);
            for (int i = 0; i < 3; i++) yield return null;
            CheckHidden("Whole HUD GameObject reactivation");

            photoSource = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            var pixels = new Color[64 * 64];
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                pixels[y * 64 + x] = new Color(.15f + .7f * x / 63f, .25f + .5f * y / 63f, .4f);
            photoSource.SetPixels(pixels); photoSource.Apply();
            var snapshot = new PhotoResultSnapshot("Validation woodpecker",
                "Validation scientific name", "Validation region", Color.yellow, 1, 2, 3,
                new AnimalPhoto(photoSource, new Rect(0, 0, 1, 1), 1), new Vector2(12, 34), 56, 1,
                new DateTime(2026, 10, 25, 12, 0, 0), AnimalSpecies.PileatedWoodpecker, AnimalState.Daily, true, true);
            Invoke(result, "ShowCore", snapshot, Texture2D.blackTexture);
            yield return new WaitForSecondsRealtime(1.4f);
            Require(result.IsShowing && IsDisplayed(resultDocument.rootVisualElement)
                && resultDocument.rootVisualElement.Q<Label>("animal-name").text == snapshot.DisplayName,
                "A genuine result opens with its captured identity instead of the authored Muskrat example.");
            result.SetSaved();
            yield return new WaitForSecondsRealtime(1.2f);
            var savedTexture = resultDocument.rootVisualElement.Q<Image>("photo-image").image;
            Require(savedTexture != null && resultDocument.rootVisualElement.Q<Label>("save-label").text == "Photo Saved",
                "The shown result renders a native photo and supports saved feedback.");

            // Unity rebuilds from the real source without disabling the result presenter.
            before = resultDocument.rootVisualElement.Q("stage");
            resultDocument.visualTreeAsset = null;
            resultDocument.visualTreeAsset = resultTree;
            for (int i = 0; i < 3; i++) yield return null;
            Require(before != resultDocument.rootVisualElement.Q("stage"), "The live-review check exercises a genuine new result tree.");
            Require(result.IsShowing && IsDisplayed(resultDocument.rootVisualElement)
                && resultDocument.rootVisualElement.Q<Label>("animal-name").text == snapshot.DisplayName
                && resultDocument.rootVisualElement.Q<Label>("save-label").text == "Photo Saved"
                && ReferenceEquals(resultDocument.rootVisualElement.Q<Image>("photo-image").image, savedTexture),
                "A live result survives root replacement with identity, saved feedback and the existing photograph intact.");
            Capture("Real-Photo-Result");
            bool closed = false;
            result.Closed += () => closed = true;
            result.Close();
            yield return new WaitForSecondsRealtime(.75f);
            Require(closed, "The genuine result still completes its close animation and notifies its presenter.");
            CheckHidden("After closing a real photograph");

            Require(result.TryBeginWarmup(), "The hidden result still supports the real graphics warmup path.");
            result.SetWarmupProgress(.5f);
            result.EndWarmup();
            CheckHidden("After native photo warmup");
            Capture("Final-Gameplay");
        }

        private void CheckHidden(string context)
        {
            VisualElement root = resultDocument.rootVisualElement;
            Validate(root);
            Require(root.panel != null && ReferenceEquals(Get(result, "root"), root)
                && ReferenceEquals(Get(result, "stage"), root.Q("stage")), context + ": presenter is bound to the actual current tree.");
            Require(!result.IsShowing && !result.IsWarmingUp && !IsDisplayed(root),
                context + ": the photograph result is hidden, including its Catalog, Save Photo and Muskrat example.");
            Require(hudDocument.rootVisualElement.Query("HudView").ToList().Count == 1
                && hudDocument.rootVisualElement.Query("InnerRing").ToList().Count == 1,
                context + ": exactly one gameplay HUD view and inner ring remain in the actual panel.");
        }

        private void SetTarget(int width, int height)
        {
            if (target != null) { panelSettings.targetTexture = null; target.Release(); Destroy(target); }
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            target.Create(); panelSettings.targetTexture = target;
        }

        private void Capture(string suffix)
        {
            VisualElement root = hudDocument.rootVisualElement;
            Validate(root); hud.RefreshLatePresentation(); Validate(root);
            RenderTexture previous = RenderTexture.active;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target; GL.Clear(true, true, new Color(.035f, .065f, .07f));
                var repaint = root.panel.GetType().GetMethods(Flags)
                    .FirstOrDefault(m => m.Name == "Repaint" && m.GetParameters().Length == 1);
                repaint?.Invoke(root.panel, new object[] { new Event { type = EventType.Repaint } });
                root.panel.GetType().GetMethod("Render", Flags, null, Type.EmptyTypes, null).Invoke(root.panel, null);
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                string filename = prefix + "-" + suffix + ".png";
                File.WriteAllBytes(filename, image.EncodeToPNG()); report.images.Add(filename);
                Require(image.GetPixels32().Count(p => p.r > 180 && p.g > 180 && p.b > 180) > 100,
                    "Native panel render contains visible HUD artwork: " + suffix);
            }
            finally { RenderTexture.active = previous; Destroy(image); }
        }

        private static bool IsDisplayed(VisualElement item)
        {
            for (VisualElement current = item; current != null; current = current.parent)
                if (current.resolvedStyle.display == DisplayStyle.None || current.resolvedStyle.visibility == Visibility.Hidden)
                    return false;
            return true;
        }
        private static void Validate(VisualElement root) => root.panel.GetType().GetMethod("ValidateLayout", Flags).Invoke(root.panel, null);
        private static object Get(object target, string field) => target.GetType().GetField(field, Flags).GetValue(target);
        private static void Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, Flags).Invoke(target, args);
        private void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            report.checks.Add(message);
        }
        private void Save() => File.WriteAllText(prefix + "-Report.json", JsonUtility.ToJson(report, true));
        private void OnDestroy()
        {
            if (instance != null) Destroy(instance);
            HeightMapPlayerSceneBootstrap.inst = previousBootstrap;
            if (bootstrapObject != null) Destroy(bootstrapObject);
            if (panelSettings != null) { panelSettings.targetTexture = null; Destroy(panelSettings); }
            if (target != null) { target.Release(); Destroy(target); }
            if (photoSource != null) Destroy(photoSource);
        }
    }
}
