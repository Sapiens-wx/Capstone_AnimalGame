using System;
using System.Reflection;
using AnimalGame.RobotMap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AnimalGame.Editor
{
    /// <summary>Exercises scan presentation against the real camera compositor.</summary>
    public static class ScanCameraZoomRegressionChecks
    {
        private const string UiPrefabPath = "Assets/Prefabs/Resources/UI/MainUI.prefab";
        private static int passedChecks;

        [MenuItem("Animal Game/Validation/Run Scan Camera Zoom Checks")]
        public static void Run()
        {
            UnityEngine.Random.State randomState = UnityEngine.Random.state;
            try
            {
                passedChecks = 0;
                Check("Authored scan targets retain their original reference", CheckAuthoredProfile);
                foreach (float baseSize in new[] { 6f, 9f, 11f })
                {
                    float size = baseSize;
                    Check("Charge and release scale with camera Size " + size,
                        () => CheckChargeAndRelease(size));
                }
                Check("Partial charge cancels and recharges continuously", CheckCancellation);
                Check("Recharging during release starts from the current view", CheckReleaseInterruption);
                Check("Scan zoom composes with photo focus and impact zoom", CheckCameraComposition);
                Check("Disabling scan zoom and resetting restore the current base", CheckReset);
                Check("Camera without a shake component uses the same proportions", CheckCameraWithoutShake);
                Check("Terrain taps do not trigger biological camera zoom", CheckTerrainGesture);
                Check("Out-of-order zoom tuning cannot invert charge and release", CheckDirectionClamps);
                Debug.Log("Scan camera zoom checks PASS: " + passedChecks
                    + " checks; camera sizes 6/9/11, authored ratios, transition continuity, cancellation,"
                    + " recharge, photo/impact composition, reset, camera fallback, terrain input and direction clamps.");
            }
            finally
            {
                UnityEngine.Random.state = randomState;
            }
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static ScanChargeUI LoadAuthoredProfile()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(UiPrefabPath);
            Require(prefab != null, "MainUI prefab is missing.");
            ScanChargeUI profile = prefab.GetComponentInChildren<ScanChargeUI>(true);
            Require(profile != null, "MainUI has no ScanChargeUI profile.");
            return profile;
        }

        private static void CheckAuthoredProfile()
        {
            ScanChargeUI profile = LoadAuthoredProfile();
            Require(Near((float)Get(profile, "scanZoomReferenceOrthographicSize"), 9f),
                "Existing scan settings must keep their original Size 9 reference.");
            Require(Near((float)Get(profile, "holdTargetOrthographicSize"), 8.5f)
                && Near((float)Get(profile, "releasePeakOrthographicSize"), 9.5f),
                "The camera fix unexpectedly rewrote the authored scan intensity.");
        }

        private static void CheckChargeAndRelease(float baseSize)
        {
            using (var f = new Fixture(baseSize))
            {
                float hold = baseSize * 8.5f / 9f;
                float peak = baseSize * 9.5f / 9f;
                f.Charge(0f);
                Require(Near(f.Size, baseSize), "Starting a charge jumped away from the current camera base.");
                float previous = f.Size;
                for (int frame = 1; frame <= 120; frame++)
                {
                    f.Charge(frame / 120f);
                    Require(f.Size <= previous + .0001f && f.Size >= hold - .0001f,
                        "Charge zoom is reversed or overshoots at camera Size " + baseSize + ".");
                    Require(Mathf.Abs(f.Size - previous) < baseSize * .01f,
                        "Charging contains a discontinuous camera-size step.");
                    previous = f.Size;
                }
                Require(Near(f.Size, hold), "Full charge did not preserve its authored zoom ratio.");
                f.SetPhase("Charged");
                f.Tick(0f);
                Require(Near(f.Size, previous), "Charging to fully charged snapped the camera.");
                Call(f.View, "BeginRelease");
                f.Tick(0f);
                Require(Near(f.Size, previous), "Releasing started from a different view than the charged view.");
                float outward = (float)Get(f.View, "releaseZoomOutDuration");
                float returning = (float)Get(f.View, "releaseZoomReturnDuration");
                for (int frame = 0; frame < 120; frame++)
                {
                    f.Tick(outward / 120f);
                    Require(f.Size >= previous - .0001f && f.Size <= peak + .0001f,
                        "The outward release phase is reversed or overshoots.");
                    Require(Mathf.Abs(f.Size - previous) < baseSize * .01f,
                        "The outward release phase contains a discontinuous step.");
                    previous = f.Size;
                }
                Require(Near(f.Size, peak), "Release peak did not scale with the camera base.");
                for (int frame = 0; frame < 120; frame++)
                {
                    f.Tick(returning / 120f);
                    Require(f.Size <= previous + .0001f && f.Size >= baseSize - .0001f,
                        "The return phase is reversed or overshoots the current camera base.");
                    Require(Mathf.Abs(f.Size - previous) < baseSize * .01f,
                        "The release-to-return boundary contains a discontinuous step.");
                    previous = f.Size;
                }
                f.Tick(.001f);
                Require(Near(f.Size, baseSize), "Completed release returned to the old Size 9 base.");
            }
        }

        private static void CheckCancellation()
        {
            using (var f = new Fixture(11f))
            {
                f.Charge(.65f);
                float cancelledAt = f.Size;
                Call(f.View, "CancelCharge");
                f.Tick(0f);
                Require(Near(f.Size, cancelledAt), "Cancelling partial charge snapped the camera.");
                float duration = (float)Get(f.View, "cancelledChargeZoomReturnDuration");
                f.Tick(duration * .4f);
                Require(f.Size > cancelledAt && f.Size < 11f, "Cancellation failed to return towards the new base.");
                float resumedAt = f.Size;
                Call(f.View, "BeginCharge");
                f.Charge(0f);
                Require(Near(f.Size, resumedAt), "Recharging during cancellation ignored the current view.");
                f.Charge(.35f);
                Call(f.View, "CancelCharge");
                f.Tick(duration + .001f);
                Require(Near(f.Size, 11f), "Cancelled charge restored the old camera size.");
            }
        }

        private static void CheckReleaseInterruption()
        {
            using (var f = new Fixture(6f))
            {
                f.Charge(1f);
                Call(f.View, "BeginRelease");
                f.Tick((float)Get(f.View, "releaseZoomOutDuration") * .7f);
                float resumedAt = f.Size;
                Call(f.View, "BeginCharge");
                f.Charge(0f);
                Require(Near(f.Size, resumedAt), "A new charge snapped a still-releasing camera back to base.");
                f.Charge(1f);
                Require(Near(f.Size, 6f * 8.5f / 9f), "Interrupted release lost the proportional charge target.");
            }
        }

        private static void CheckCameraComposition()
        {
            using (var f = new Fixture(11f))
            {
                f.Shake.SetPhotoFocusMagnification(2f);
                Set(f.Shake, "springZoom", .02f);
                Set(f.Shake, "globalIntensity", 1f);
                f.Charge(1f);
                Require(Near(f.Size, 11f * 8.5f / 9f / 2f * 1.02f),
                    "Scan zoom overwrote photo magnification or the impact zoom spring.");
                Require(Near(f.Shake.BaseOrthographicSize, 11f),
                    "The effect-modified output was incorrectly captured as the camera base.");
                Call(f.View, "ResetScanCameraZoomImmediate");
                f.Apply();
                Require(Near(f.Size, 11f / 2f * 1.02f),
                    "Resetting scan zoom removed the independent photo or impact effects.");
            }
        }

        private static void CheckReset()
        {
            using (var f = new Fixture(11f))
            {
                f.Charge(1f);
                Set(f.View, "enableScanCameraZoom", false);
                f.Tick(0f);
                Require(Near(f.Size, 11f) && Near((float)Get(f.Shake, "scanZoomMultiplier"), 1f),
                    "Disabling scan camera zoom retained an old scan multiplier.");
                Set(f.View, "enableScanCameraZoom", true);
                Call(f.View, "BeginCharge");
                f.Charge(1f);
                Call(f.View, "ResetScanCameraZoomImmediate");
                f.Apply();
                Require(Near(f.Size, 11f), "Immediate reset returned to a hard-coded size.");
                f.Tick(1f);
                Require(Near(f.Size, 11f), "A reset camera resumed a stale release or cancel transition.");
            }
        }

        private static void CheckCameraWithoutShake()
        {
            using (var f = new Fixture(6f, false))
            {
                f.Charge(1f);
                Require(Near(f.Size, 6f * 8.5f / 9f), "The direct camera path still uses absolute scan targets.");
                Call(f.View, "BeginRelease");
                f.Tick((float)Get(f.View, "releaseZoomOutDuration"));
                Require(Near(f.Size, 6f * 9.5f / 9f), "The direct camera path lost the proportional release peak.");
                f.Tick((float)Get(f.View, "releaseZoomReturnDuration") + .001f);
                Require(Near(f.Size, 6f), "The direct camera path restored the old base.");
            }
        }

        private static void CheckTerrainGesture()
        {
            using (var f = new Fixture(11f))
            {
                int terrainRequests = 0;
                f.View.TerrainScanRequested += () => terrainRequests++;
                for (int tap = 0; tap < 2; tap++)
                {
                    Require(!(bool)Call(f.View, "UpdateScanGesture", true, .1f),
                        "A short terrain-scan tap incorrectly started biological charging.");
                    Call(f.View, "UpdateScanGesture", false, .01f);
                    f.Tick(0f);
                    Require(Near(f.Size, 11f), "A short scan tap changed the camera size.");
                }
                Require(terrainRequests == 1, "The camera fix disrupted terrain double-tap input.");
            }
        }

        private static void CheckDirectionClamps()
        {
            using (var f = new Fixture(11f))
            {
                Set(f.View, "holdTargetOrthographicSize", 20f);
                Set(f.View, "releasePeakOrthographicSize", 2f);
                f.Charge(1f);
                Require(Near(f.Size, 11f), "An invalid hold setting inverted the intended charge zoom.");
                Call(f.View, "BeginRelease");
                f.Tick((float)Get(f.View, "releaseZoomOutDuration"));
                Require(Near(f.Size, 11f), "An invalid release setting inverted the intended release zoom.");
            }
        }

        private static void Check(string name, Action action)
        {
            try { action(); passedChecks++; }
            catch (Exception error) { throw new InvalidOperationException(name + ": " + error.Message, error); }
        }

        private static bool Near(float a, float b) => Mathf.Abs(a - b) <= .0005f;
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        private static object Get(object target, string name) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static object Call(object target, string name, params object[] args)
        {
            try
            {
                return target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(target, args);
            }
            catch (TargetInvocationException error) when (error.InnerException != null)
            {
                throw error.InnerException;
            }
        }

        private sealed class Fixture : IDisposable
        {
            private readonly Scene scene;
            public readonly Camera Camera;
            public readonly RobotCameraShake Shake;
            public readonly ScanChargeUI View;
            public float Size => Camera.orthographicSize;

            public Fixture(float baseSize, bool withShake = true)
            {
                scene = EditorSceneManager.NewPreviewScene();
                Camera = Create("Scan zoom validation camera").AddComponent<Camera>();
                Camera.orthographic = true;
                Camera.orthographicSize = baseSize;
                if (withShake)
                {
                    Shake = Camera.gameObject.AddComponent<RobotCameraShake>();
                    Set(Shake, "enableGamepadRumble", false);
                    Call(Shake, "Awake");
                }
                GameObject viewObject = new GameObject("Scan zoom validation UI", typeof(RectTransform));
                SceneManager.MoveGameObjectToScene(viewObject, scene);
                View = viewObject.AddComponent<ScanChargeUI>();
                Set(View, "animator", null);
                Set(View, "showScanRing", false);
                ScanChargeUI profile = LoadAuthoredProfile();
                foreach (string field in new[] { "scanZoomReferenceOrthographicSize", "holdTargetOrthographicSize",
                    "releasePeakOrthographicSize", "maximumChargeDuration", "releaseZoomOutDuration",
                    "releaseZoomReturnDuration", "cancelledChargeZoomReturnDuration", "biologicalScanMinimumHoldDuration",
                    "terrainScanDoubleTapWindow" })
                    Set(View, field, Get(profile, field));
                Set(View, "mapCamera", Camera);
                Set(View, "cameraZoomInitialized", false);
                Call(View, "ResolveTrackingReferences");
                Tick(0f);
            }

            private GameObject Create(string name)
            {
                var go = new GameObject(name);
                SceneManager.MoveGameObjectToScene(go, scene);
                return go;
            }

            public void SetPhase(string phase)
            {
                FieldInfo field = typeof(ScanChargeUI).GetField("cameraZoomPhase", BindingFlags.Instance | BindingFlags.NonPublic);
                field.SetValue(View, Enum.Parse(field.FieldType, phase));
            }

            public void Charge(float progress)
            {
                if (Get(View, "cameraZoomPhase").ToString() != "Charging")
                    Call(View, "BeginCharge");
                Set(View, "chargeElapsed", (float)Get(View, "maximumChargeDuration") * progress);
                SetPhase("Charging");
                Tick(0f);
            }

            public void Tick(float deltaTime)
            {
                Call(View, "UpdateScanCameraZoom", deltaTime);
                Apply();
            }

            public void Apply()
            {
                Call(View, "ApplyScanCameraZoom");
                if (Shake != null)
                    Call(Shake, "ApplyShakeToCamera");
            }

            public void Dispose() => EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
