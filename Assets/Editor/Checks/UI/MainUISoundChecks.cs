using System;
using System.IO;
using System.Reflection;
using AnimalGame.MainUI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    public static class MainUISoundChecks
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static int checks;

        [MenuItem("Animal Game/Validation/Run Main UI Sound Checks")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run sound checks from Edit Mode.");
            checks = 0;
            ResetPlayback();
            var scene = EditorSceneManager.NewPreviewScene();
            PanelSettings settings = null;
            RenderTexture target = null;
            try
            {
                var obj = new GameObject("Sound UI checks");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(obj, scene);
                var document = obj.AddComponent<UIDocument>();
                settings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(
                    "Assets/Prefabs/Resources/UI/Main/MainUISetting.asset"));
                target = new RenderTexture(640, 320, 0);
                target.Create();
                settings.targetTexture = target;
                document.panelSettings = settings;
                document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                    "Assets/Prefabs/Resources/UI/Main/MainUIDoc.uxml");
                var root = document.rootVisualElement;
                var wave = root.Q<SoundElement>("Sound");
                Assert(wave != null, "Authored Sound element imports");
                wave.RemoveFromHierarchy();
                root.Clear();
                root.style.width = 640;
                root.style.height = 320;
                root.Add(wave);
                wave.style.left = 40;
                wave.style.top = 40;
                wave.style.right = StyleKeyword.Auto;
                wave.style.width = 560;
                wave.style.height = 240;
                root.panel.GetType().GetMethod("ValidateLayout", InstanceFlags | BindingFlags.Public).Invoke(root.panel, null);

                var controller = obj.AddComponent<MainUISoundController>();
                CheckPlayback(controller);
                CheckPhase(controller);
                CheckWave(wave, root, target);
                Debug.Log("MAIN UI SOUND CHECKS PASSED: " + checks);
            }
            finally
            {
                ResetPlayback();
                EditorSceneManager.ClosePreviewScene(scene);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                if (settings != null) Object.DestroyImmediate(settings);
            }
        }

        private static void CheckPlayback(MainUISoundController controller)
        {
            var quiet = SoundUIPlayback.BeginSound(.3f);
            var loud = SoundUIPlayback.BeginSound(.8f);
            var duplicate = SoundUIPlayback.BeginSound(.8f);
            Assert(SoundUIPlayback.ActiveCount == 3 && SoundUIPlayback.MaximumStrength == .8f,
                "Repeated starts are counted independently and use the maximum strength");
            Advance(controller, 0f);
            Advance(controller, .02f);
            float rising = controller.CurrentAmplitude;
            Assert(rising > 0f && rising < .8f, "Amplitude rises smoothly");
            Advance(controller, .5f);
            float expanded = controller.CurrentDamping;
            Assert(expanded > 0f && expanded < 120f, "Playback reduces damping");
            SoundUIPlayback.EndSound(loud);
            SoundUIPlayback.EndSound(loud);
            SoundUIPlayback.EndSound(default);
            Assert(SoundUIPlayback.ActiveCount == 2 && SoundUIPlayback.MaximumStrength == .8f,
                "Duplicate/default ends do not underflow or end another playback");
            SoundUIPlayback.EndSound(duplicate);
            Assert(SoundUIPlayback.MaximumStrength == .3f, "Loudest end restores the quieter sound");
            float beforeFall = controller.CurrentAmplitude;
            Advance(controller, .02f);
            Assert(controller.CurrentAmplitude < beforeFall && controller.CurrentAmplitude > .3f,
                "Amplitude falls smoothly to the remaining maximum");
            Assert(controller.CurrentDamping < expanded, "Partial ends do not reset expansion");
            var overlap = SoundUIPlayback.BeginSound(.6f);
            Advance(controller, .1f);
            Assert(controller.CurrentDamping < expanded, "An overlapping start does not restart expansion");
            float beforePause = controller.CurrentDamping;
            Advance(controller, 0f);
            Assert(controller.CurrentDamping == beforePause, "Zero elapsed time freezes expansion");
            Advance(controller, 3f);
            Assert(controller.CurrentDamping == 0f, "Sustained playback reaches exactly zero damping");
            SoundUIPlayback.EndSound(overlap);
            SoundUIPlayback.EndSound(quiet);
            float beforeEnd = controller.CurrentAmplitude;
            Advance(controller, .02f);
            Assert(controller.CurrentAmplitude > 0f && controller.CurrentAmplitude < beforeEnd
                && controller.CurrentDamping == 0f, "Final end fades out at its existing width");
            Advance(controller, 3f);
            Assert(controller.CurrentAmplitude == 0f && controller.CurrentDamping == 120f,
                "Idle settles to a straight line and resets the next onset");

            var old = SoundUIPlayback.BeginSound(.9f);
            ResetPlayback();
            var fresh = SoundUIPlayback.BeginSound(.4f);
            SoundUIPlayback.EndSound(old);
            Assert(SoundUIPlayback.ActiveCount == 1, "A prior-session handle cannot end a new playback");
            SoundUIPlayback.EndSound(fresh);
            var invalid = SoundUIPlayback.BeginSound(float.NaN);
            Assert(SoundUIPlayback.MaximumStrength == 0f, "Invalid strength stays finite");
            SoundUIPlayback.EndSound(invalid);
        }

        private static void CheckPhase(MainUISoundController controller)
        {
            float before = controller.CurrentPhase;
            Advance(controller, .125f);
            float expected = Mathf.Repeat(before + Mathf.PI * .25f, 2f * Mathf.PI);
            Assert(Mathf.Abs(Mathf.DeltaAngle(controller.CurrentPhase * Mathf.Rad2Deg, expected * Mathf.Rad2Deg)) < .001f,
                "Phase advances at constant speed while silent");
            float paused = controller.CurrentPhase;
            Advance(controller, 0f);
            Assert(controller.CurrentPhase == paused, "Paused game time freezes phase");
            var sound = SoundUIPlayback.BeginSound(.5f);
            Advance(controller, 0f);
            SoundUIPlayback.EndSound(sound);
            Advance(controller, 0f);
            Assert(controller.CurrentPhase == paused, "Sound starts and ends do not reset phase");
            Advance(controller, 10000.25f);
            Assert(controller.CurrentPhase >= 0f && controller.CurrentPhase < Mathf.PI * 2f,
                "Phase remains bounded during long sessions");
            typeof(MainUISoundController).GetField("phaseSpeed", InstanceFlags).SetValue(controller, 2f);
            before = controller.CurrentPhase;
            Advance(controller, .125f);
            expected = Mathf.Repeat(before + Mathf.PI * .5f, 2f * Mathf.PI);
            Assert(Mathf.Abs(Mathf.DeltaAngle(controller.CurrentPhase * Mathf.Rad2Deg, expected * Mathf.Rad2Deg)) < .001f,
                "Inspector phase speed controls cycles per game second");
        }

        private static void CheckWave(SoundElement wave, VisualElement root, RenderTexture target)
        {
            Invoke(wave, "ConfigureWaveform", 97, 5f, .85f, 2f);
            Invoke(wave, "SetWaveState", .8f, 120f);
            Invoke(wave, "RefreshSamples");
            float[] carrier = Field<float[]>(wave, "carrier");
            float[] envelope = Field<float[]>(wave, "envelope");
            Assert(Mathf.Approximately(carrier[48], 1f) && envelope[48] == 1f
                && envelope[0] < .001f, "Narrow onset has a visible center crest and damped edges");
            float phaseSample = carrier[37];
            Capture(root, target, "sound-narrow.png");
            Invoke(wave, "SetWaveState", .8f, 0f);
            Invoke(wave, "RefreshSamples");
            Assert(ReferenceEquals(carrier, Field<float[]>(wave, "carrier"))
                && ReferenceEquals(envelope, Field<float[]>(wave, "envelope")), "Animated changes reuse sample buffers");
            Assert(carrier[37] == phaseSample && envelope[0] == 1f && envelope[96] == 1f,
                "Expansion preserves phase and zero damping reaches the full width");
            Capture(root, target, "sound-expanded.png");
            float[] sine = Field<float[]>(wave, "carrierSine");
            Invoke(wave, "SetWavePhase", Mathf.PI * .5f);
            Invoke(wave, "RefreshSamples");
            Assert(ReferenceEquals(carrier, Field<float[]>(wave, "carrier"))
                && ReferenceEquals(sine, Field<float[]>(wave, "carrierSine"))
                && ReferenceEquals(envelope, Field<float[]>(wave, "envelope")),
                "Phase motion reuses both trig caches and the damping envelope");
            float shiftedCenter = carrier[48] * Field<float>(wave, "phaseCos")
                - sine[48] * Field<float>(wave, "phaseSin");
            Assert(Mathf.Abs(shiftedCenter) < .0001f && envelope[48] == 1f,
                "Quarter-cycle phase shifts the carrier without moving its envelope");
            Capture(root, target, "sound-phase-shifted.png");
            Invoke(wave, "ConfigureWaveform", 129, 7f, .7f, 3f);
            Invoke(wave, "RefreshSamples");
            Assert(Field<float[]>(wave, "carrier").Length == 129, "Point count can be changed at runtime");
            Invoke(wave, "SetWaveState", 0f, 120f);
            Capture(root, target, "sound-idle.png");
        }

        private static void Capture(VisualElement root, RenderTexture target, string filename)
        {
            var flags = InstanceFlags | BindingFlags.Public;
            root.panel.GetType().GetMethod("ValidateLayout", flags).Invoke(root.panel, null);
            var previous = RenderTexture.active;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target;
                GL.Clear(true, true, Color.black);
                root.panel.GetType().GetMethod("Repaint", flags, null, new[] { typeof(Event) }, null)
                    .Invoke(root.panel, new object[] { new Event { type = EventType.Repaint } });
                root.panel.GetType().GetMethod("Render", flags, null, Type.EmptyTypes, null).Invoke(root.panel, null);
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                pixels.Apply();
                int yellow = 0;
                foreach (Color32 pixel in pixels.GetPixels32())
                    if (pixel.r > 120 && pixel.g > 100 && pixel.b < 100) yellow++;
                Assert(yellow > 100, "Painter2D renders the yellow path: " + filename);
                string folder = Path.Combine(Path.GetTempPath(), "AnimalGameSoundUIValidation");
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, filename), pixels.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(pixels); }
        }

        private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, InstanceFlags).GetValue(value);
        private static void Invoke(object value, string method, params object[] args) =>
            value.GetType().GetMethod(method, InstanceFlags).Invoke(value, args);
        private static void Advance(MainUISoundController controller, float seconds) => Invoke(controller, "Advance", seconds);
        private static void ResetPlayback() => typeof(SoundUIPlayback).GetMethod("Reset", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception("Sound UI check failed: " + message);
            checks++;
        }
        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}
