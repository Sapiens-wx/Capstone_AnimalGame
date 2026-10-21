using System;
using System.Collections.Generic;
using System.Linq;
using AnimalGame.Garbage;
using AnimalGame.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AnimalGame.Demos.WasteRobot.Editor
{
    /// <summary>
    /// Explicit asset/geometry checks. No editor initializer, shared asset writes,
    /// production scene save, or frame simulation is performed here. Run in the
    /// isolated validation project; live waves and timing belong to ScanSmoke.
    /// </summary>
    public static class WasteRobotScanValidation
    {
        private static readonly List<string> Passed = new List<string>();

        // Samples are measured independently from the approved registered artwork,
        // rather than copied from the new scan envelope's implementation.
        private static readonly Vector2[] VehiclePixelSamples =
        {
            new Vector2(27f, 278f), new Vector2(106f, 340f), // Upper claw.
            new Vector2(198f, 142f), new Vector2(435f, 101f), // Upper arm joints.
            new Vector2(39f, 777f), new Vector2(122f, 836f),  // Detached lower claw.
            new Vector2(336f, 803f), new Vector2(440f, 750f), // Detached arm.
            new Vector2(640f, 26f), new Vector2(832f, 170f), // Four thick tyres.
            new Vector2(1180f, 26f), new Vector2(1370f, 170f),
            new Vector2(640f, 717f), new Vector2(832f, 865f),
            new Vector2(1180f, 717f), new Vector2(1370f, 865f),
            new Vector2(242f, 460f), new Vector2(966f, 483f), // Collector/body.
            new Vector2(1744f, 225f), new Vector2(1744f, 705f) // Both rear bays.
        };

        [MenuItem("Animal Game/Demos/Waste Robot/Validate Scanning")]
        public static void Run()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode,
                "Exit Play Mode before running scan asset validation.");
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(WasteRobotDemoBuilder.PrefabPath);
            Require(asset != null, "Build the Waste Robot prefab first.");
            Passed.Clear();
            Scene previous = SceneManager.GetActiveScene();
            try
            {
                Check("one explicit root target, exterior sprite and independent interaction ownership",
                    () => CheckPrefab(asset));
                Check("existing static-area scan style and reveal defaults", () => CheckStyle(asset));
                Check("whole-vehicle envelope covers remote parts and excludes transparent canvas corners",
                    () => CheckGeometry(asset));
                Check("translated, rotated and non-uniformly scaled envelope", () => CheckTransforms(asset));
                Check("independent machine instances and cross-scene references", () => CheckInstances(asset));
                Check("exterior reveal, interior suspension and orientation/pattern inheritance",
                    () => CheckSuspension(asset));
                Check("binding, component disable and controller disable clear old reveals",
                    () => CheckResetAndDisable(asset));
                Debug.Log("Waste Robot scan validation PASS (" + Passed.Count + " groups):\n - "
                    + string.Join("\n - ", Passed)
                    + "\nEdit Mode asset/geometry validation only; actual scan-wave travel, temporal expiry and fade suppression require ScanSmoke.");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded && SceneManager.GetActiveScene() != previous)
                    SceneManager.SetActiveScene(previous);
                WorldInteractionQuery.InvalidateSpatialIndex();
            }
        }

        public static void RunBatch()
        {
            try
            {
                Run();
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        private static void CheckPrefab(GameObject asset)
        {
            ScannableInteractable[] allTargets = asset.GetComponentsInChildren<ScannableInteractable>(true);
            WasteRobotScan[] adapters = asset.GetComponentsInChildren<WasteRobotScan>(true);
            Require(allTargets.Length == 1 && allTargets[0].gameObject == asset && allTargets[0].enabled,
                "The prefab must have exactly one enabled scan target on its root.");
            Require(adapters.Length == 1 && adapters[0].gameObject == asset && adapters[0].enabled,
                "The prefab must have exactly one enabled local scan adapter on its root.");
            Require(adapters[0].ScanTarget == allTargets[0], "The adapter must reference its own root target.");
            WasteRobotBase machine = asset.GetComponent<WasteRobotBase>();
            SpriteRenderer expectedSprite = machine.ExteriorRoot.transform.Find("Visual")?.GetComponent<SpriteRenderer>();
            var serialized = new SerializedObject(allTargets[0]);
            Require(expectedSprite != null && serialized.FindProperty("spriteSource").objectReferenceValue == expectedSprite,
                "Scanning must explicitly use Exterior/Visual, including when the interior becomes active.");
            SerializedProperty outline = serialized.FindProperty("customNormalizedOutline");
            Require(outline != null && outline.arraySize >= 8,
                "A measured whole-machine outline is required; the full rectangular sprite mesh includes transparent canvas.");
            for (int i = 0; i < outline.arraySize; i++)
            {
                Vector2 point = outline.GetArrayElementAtIndex(i).vector2Value;
                Require(IsFinite(point.x) && IsFinite(point.y) && Mathf.Abs(point.x) <= .501f && Mathf.Abs(point.y) <= .501f,
                    "A normalized scan point lies outside the registered sprite canvas.");
            }
            Require(asset.GetComponent<WorldInteraction>() == null && asset.GetComponent<GarbageItem>() == null,
                "Scanning must not turn the machine root into garbage or add collision/grab/push interaction.");
            Require(asset.GetComponentsInChildren<WorldInteraction>(true).All(wall =>
                    wall.transform.IsChildOf(machine.ExteriorRoot.transform)
                    || wall.transform.IsChildOf(machine.InteriorRoot.transform)),
                "Adding scan support moved interaction geometry outside its original switched presentation roots.");
        }

        private static void CheckStyle(GameObject asset)
        {
            using (var fixture = new Fixture(asset))
            {
                var referenceObject = new GameObject("Default static scan reference");
                SceneManager.MoveGameObjectToScene(referenceObject, fixture.Scene);
                ScannableInteractable reference = referenceObject.AddComponent<ScannableInteractable>();
                var actual = new SerializedObject(fixture.Scan);
                var defaults = new SerializedObject(reference);
                foreach (string field in new[]
                {
                    "scanType", "displayDuration", "fadeOutDuration", "fadeInDuration", "breathingPeriod",
                    "breathingMinAlpha", "breathingMaxAlpha", "areaColor", "lineWidthPixels", "lineSpacingPixels",
                    "useSmallAreaFill", "smallAreaLineSpacingPixels", "lineAngleDegrees", "showOutlineDots",
                    "outlineDotSizePixels", "outlineDotSpacingPixels", "smallOutlineDotSpacingPixels",
                    "outlineBreathingPeriod", "outlineBreathingMinAlpha", "outlineBreathingMaxAlpha"
                })
                {
                    SerializedProperty a = actual.FindProperty(field), b = defaults.FindProperty(field);
                    Require(a != null && b != null, "The existing scan style field is missing: " + field);
                    bool same;
                    switch (a.propertyType)
                    {
                        case SerializedPropertyType.Float: same = Near(a.floatValue, b.floatValue); break;
                        case SerializedPropertyType.Boolean: same = a.boolValue == b.boolValue; break;
                        case SerializedPropertyType.Color: same = a.colorValue == b.colorValue; break;
                        case SerializedPropertyType.Enum: same = a.enumValueIndex == b.enumValueIndex; break;
                        default: throw new InvalidOperationException("Unsupported scan style comparison: " + field);
                    }
                    Require(same, "The machine overrides the established static scan style: " + field);
                }
                Require(fixture.Scan.ScanType == InteractableScanType.StaticArea && !fixture.Scan.UseSmallAreaFill,
                    "A large machine must use the existing static-area style without small-garbage density compensation.");
            }
        }

        private static void CheckGeometry(GameObject asset)
        {
            using (var fixture = new Fixture(asset))
            {
                var outline = new List<Vector3>();
                Require(fixture.Scan.TryGetWorldOutline(outline) && outline.Count >= 8,
                    "The exterior scan envelope has no usable world geometry.");
                foreach (Vector2 pixel in VehiclePixelSamples)
                    Require(fixture.Scan.IntersectsScanWave(PixelWorld(fixture.Image, pixel), 0f, .015f),
                        "An important machine part is outside the scan envelope at source pixel " + pixel + ".");
                Vector2 size = WasteRobotCollisionLayout.SharedImageSize;
                foreach (Vector2 corner in new[] { Vector2.zero, new Vector2(size.x, 0f), new Vector2(0f, size.y), size })
                    Require(!fixture.Scan.IntersectsScanWave(PixelWorld(fixture.Image, corner), 0f, .001f),
                        "A transparent sprite-canvas corner incorrectly triggers the vehicle scan: " + corner);
                float canvasArea = fixture.Image.sprite.bounds.size.x * fixture.Image.sprite.bounds.size.y
                    * Mathf.Abs(fixture.Image.transform.lossyScale.x * fixture.Image.transform.lossyScale.y);
                float area = PolygonArea(outline);
                Require(area > canvasArea * .5f && area < canvasArea * .97f,
                    "The scan envelope is either body-only or nearly the entire rectangular transparent canvas.");
            }
        }

        private static void CheckTransforms(GameObject asset)
        {
            using (var fixture = new Fixture(asset))
            {
                var before = new List<Vector3>();
                Require(fixture.Scan.TryGetWorldOutline(before), "The baseline outline could not be resolved.");
                Vector3[] local = before.Select(p => fixture.Root.transform.InverseTransformPoint(p)).ToArray();
                fixture.Root.transform.SetPositionAndRotation(new Vector3(19f, -11f, 0f), Quaternion.Euler(0f, 0f, 37f));
                fixture.Root.transform.localScale = new Vector3(1.45f, .75f, 1f);
                var after = new List<Vector3>();
                Require(fixture.Scan.TryGetWorldOutline(after) && after.Count == local.Length,
                    "Transforming the machine changed its authored scan point count.");
                for (int i = 0; i < after.Count; i++)
                    Require(Vector3.Distance(after[i], fixture.Root.transform.TransformPoint(local[i])) < .0002f,
                        "The scan envelope no longer follows the whole machine transform.");
                foreach (Vector2 pixel in VehiclePixelSamples)
                    Require(fixture.Scan.IntersectsScanWave(PixelWorld(fixture.Image, pixel), 0f, .02f),
                        "A transformed remote part is missed by scan-wave collision: " + pixel);
            }
        }

        private static void CheckInstances(GameObject asset)
        {
            using (var first = new Fixture(asset))
            using (var otherScene = new Fixture(asset))
            {
                GameObject secondRoot = (GameObject)PrefabUtility.InstantiatePrefab(asset, first.Scene);
                WasteRobotScan secondAdapter = secondRoot.GetComponent<WasteRobotScan>();
                ScannableInteractable second = secondRoot.GetComponent<ScannableInteractable>();
                secondAdapter.Configure(second);
                secondRoot.transform.SetPositionAndRotation(new Vector3(70f, -24f, 0f), Quaternion.Euler(0f, 0f, -28f));
                SpriteRenderer secondImage = secondRoot.GetComponent<WasteRobotBase>().ExteriorRoot
                    .transform.Find("Visual").GetComponent<SpriteRenderer>();
                Vector2 ownBody = PixelWorld(first.Image, new Vector2(966f, 483f));
                Vector2 secondBody = PixelWorld(secondImage, new Vector2(966f, 483f));
                Require(secondAdapter.ScanTarget == second && second != first.Scan,
                    "A duplicated machine shares its first instance's scan target.");
                Require(second.IntersectsScanWave(secondBody, 0f, .01f)
                    && !first.Scan.IntersectsScanWave(secondBody, 0f, .01f)
                    && !second.IntersectsScanWave(ownBody, 0f, .01f),
                    "Separated machine instances share one spatial scan envelope.");
                first.Scan.Reveal(3f);
                first.Machine.SetInside(true);
                first.Adapter.RefreshState();
                secondAdapter.RefreshState();
                otherScene.Adapter.RefreshState();
                Require(!first.Scan.CanScan && second.CanScan && otherScene.Scan.CanScan,
                    "Suspending one machine affected another machine in the same or a different scene.");
                Require(otherScene.Adapter.ScanTarget.gameObject.scene == otherScene.Scene
                    && secondAdapter.ScanTarget.gameObject.scene == first.Scene,
                    "A reusable scan adapter resolved a target from another scene.");
            }
        }

        private static void CheckSuspension(GameObject asset)
        {
            using (var fixture = new Fixture(asset))
            {
                fixture.Scan.Reveal(3f);
                Require(fixture.Scan.CaptureScanOrientation(17f), "A revealed scan failed to capture its initial orientation.");
                float remaining = fixture.Scan.RevealRemaining, elapsed = fixture.Scan.RevealElapsed;
                float angle = fixture.Scan.CapturedScanWorldAngleDegrees;
                uint seed = fixture.Scan.OutlinePatternSeed;
                Require(remaining > 0f && seed != 0u, "The exterior could not receive the ordinary garbage-style reveal.");
                fixture.Machine.SetInside(true);
                fixture.Adapter.RefreshState();
                Require(!fixture.Scan.CanScan && fixture.Scan.RevealRemaining <= 0f
                    && fixture.Adapter.IsSuspended && fixture.Adapter.SuspendedRevealRemaining > 0f,
                    "The interior still exposes the whole-machine scan marker or lost its suspended reveal clock.");
                fixture.Machine.SetInside(false);
                fixture.Adapter.RefreshState();
                Require(fixture.Scan.CanScan && !fixture.Adapter.IsSuspended && fixture.Scan.RevealRemaining > 0f
                    && fixture.Scan.RevealRemaining <= remaining + .0001f,
                    "Returning outside failed to restore only the reveal time that remained.");
                Require(fixture.Scan.RevealElapsed >= elapsed && fixture.Scan.HasCapturedScanOrientation
                    && Near(fixture.Scan.CapturedScanWorldAngleDegrees, angle) && fixture.Scan.OutlinePatternSeed == seed,
                    "Suspending a reveal restarted its breathing phase, direction or diamond pattern.");
            }
        }

        private static void CheckResetAndDisable(GameObject asset)
        {
            using (var fixture = new Fixture(asset))
            {
                fixture.Scan.Reveal(3f);
                fixture.Machine.SetInside(true);
                fixture.Adapter.RefreshState();
                fixture.Machine.Bind(null);
                fixture.Adapter.RefreshState();
                Require(fixture.Scan.RevealRemaining <= 0f && fixture.Adapter.SuspendedRevealRemaining <= 0f,
                    "Binding/reset restored a reveal that should have been discarded.");

                fixture.Scan.Reveal(3f);
                fixture.Adapter.enabled = false;
                fixture.Adapter.RefreshState();
                Require(!fixture.Scan.CanScan && fixture.Adapter.SuspendedRevealRemaining <= 0f,
                    "Disabling the local adapter retained a visible or suspended machine marker.");
                fixture.Adapter.enabled = true;
                fixture.Adapter.RefreshState();
                Require(fixture.Scan.CanScan && fixture.Scan.RevealRemaining <= 0f,
                    "Re-enabling the adapter resurrected an old reveal or failed to resume exterior eligibility.");

                fixture.Scan.Reveal(3f);
                fixture.Machine.enabled = false;
                fixture.Adapter.RefreshState();
                Require(!fixture.Scan.CanScan && fixture.Adapter.SuspendedRevealRemaining <= 0f,
                    "Disabling the machine controller retained its scan marker.");
                fixture.Machine.enabled = true;
                fixture.Adapter.RefreshState();
                Require(fixture.Scan.CanScan && fixture.Scan.RevealRemaining <= 0f,
                    "Re-enabling the controller resurrected a pre-disable reveal.");
            }
        }

        private static Vector2 PixelWorld(SpriteRenderer renderer, Vector2 pixel)
        {
            Vector2 size = WasteRobotCollisionLayout.SharedImageSize;
            Bounds bounds = renderer.sprite.bounds;
            return renderer.transform.TransformPoint(bounds.center + new Vector3(
                (pixel.x / size.x - .5f) * bounds.size.x,
                (.5f - pixel.y / size.y) * bounds.size.y, 0f));
        }

        private static float PolygonArea(IReadOnlyList<Vector3> points)
        {
            float twiceArea = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 a = points[i], b = points[(i + 1) % points.Count];
                twiceArea += a.x * b.y - a.y * b.x;
            }
            return Mathf.Abs(twiceArea) * .5f;
        }

        private static void Check(string name, Action action)
        {
            action();
            Passed.Add(name);
        }

        private static bool Near(float a, float b) => Mathf.Abs(a - b) < .0001f;
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Waste Robot scan validation: " + message);
        }

        private sealed class Fixture : IDisposable
        {
            public readonly Scene Scene;
            public readonly GameObject Root;
            public readonly WasteRobotBase Machine;
            public readonly WasteRobotScan Adapter;
            public readonly ScannableInteractable Scan;
            public readonly SpriteRenderer Image;

            public Fixture(GameObject asset)
            {
                Scene = EditorSceneManager.NewPreviewScene();
                Root = (GameObject)PrefabUtility.InstantiatePrefab(asset, Scene);
                Machine = Root.GetComponent<WasteRobotBase>();
                Adapter = Root.GetComponent<WasteRobotScan>();
                Scan = Root.GetComponent<ScannableInteractable>();
                Require(Machine != null && Adapter != null && Scan != null,
                    "The instantiated prefab has no complete controller/scan adapter pair.");
                Machine.AutoBindPlayer = false;
                Adapter.Configure(Scan);
                Machine.Bind(null);
                Adapter.RefreshState();
                Image = Machine.ExteriorRoot.transform.Find("Visual").GetComponent<SpriteRenderer>();
            }

            public void Dispose()
            {
                EditorSceneManager.ClosePreviewScene(Scene);
                WorldInteractionQuery.InvalidateSpatialIndex();
            }
        }
    }
}
