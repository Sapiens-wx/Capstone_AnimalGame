using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using AnimalGame.Animals;
using AnimalGame.Garbage;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    public static class StaticInteractableScanRegressionChecks
    {
        private const string PrefabDirectory = "Assets/Prefabs/Environment/Garbage/";

        [MenuItem("Animal Game/Validation/Run Static Interactable Scan Checks")]
        public static void Run()
        {
            CheckProductionPrefabs();
            CheckOutlineAndWaveContact();
            CheckLifetimeAndInheritance();
            CheckControllerWave();
            CheckParallelLinePattern();
            CheckSmoothInsetEnvelope();
            CheckSmallGarbageAreaFill();
            CheckDashedOutlinePattern();
            CheckOutlineDotPattern();
            CheckPointBoundaryClipping();
            CheckPointBoundaryCameraAndOverlap();
            CheckOutlineRandomization();
            CheckOutlineDotOverlay();
            CheckScanCapturedDirection();
            CheckCapturedDirectionInheritanceAndRebuild();
            CheckCapturedSmallAreaAndOverlap();
            CheckOverlayProjection();
            CheckContinuousCameraMotion();
            CheckOverlappingAreas();
            CheckOverlapBreathingStability();
            CheckAnimatedAreaTint();
            CheckOverlayCanvasRecovery();
            CheckFragmentTransfer();
            CheckShader();
            Debug.Log("Static interactable scan checks PASS: production garbage prefabs, sprite envelope, transforms, edge contact, origin crossing, lifetime, per-wave deduplication, scene isolation, thin continuous single-direction parallel bars, inset smoothed envelopes and complete short edge rows, winding-independent layout and explicit unsmoothed control, small-garbage interior fill and legacy-object opt-in, matching new medium/big world patterns, full baseline antialias rectangles inside the footprint, tiny-area fallback, legacy dashed-pattern baselines plus inset solid-diamond perimeter points and winding/translation invariance, independent outline batches outside circular clipping, source-row-preserving outline toggles/style, actual diamond-center hull clipping with full AA rectangles and one-pixel clearance, near/far camera yaw/zoom and rescan containment, tiny-area omission and no invisible overlap reservations, shared reveal/expiry clock with independent hatch pulse and outline breathing, per-scan seeded perimeter variation and repeatability, Unity Random isolation, cached perimeter centers and fixed-pixel diamond size through camera motion and presenter reconstruction, final-camera scan capture, 45-degree initial slash alignment, locked world endpoints through camera turning/movement/zoom, rescan direction replacement without breathing reset, inherited orientation and overlay reconstruction, independent directions for different scan times and breathing-stable overlap, shared world line rows and overlap coverage, off-view overlay geometry, fixed pixel stroke width with continuously projected line spacing, smooth reveal entrance, strong entrance-delayed hatch pulses and independent gentle perimeter breathing, animation pause and refresh continuity, configurable color and animation, partial viewport overlap, destroyed-canvas recovery, cleanup, actual fragment direction and perimeter seed inheritance after source retirement and shader import.");
        }

        public static void RunBatch() => ExitAfter(Run);

        public static void RunIntegrationBatch() => ExitAfter(() =>
        {
            Run();
            BioScanRegressionChecks.Run();
            GarbageRegressionChecks.Run();
            HeavyGarbageRegressionChecks.Run();
        });

        public static void RunFullBatch() => ExitAfter(() =>
        {
            Run();
            BioScanRegressionChecks.Run();
            GarbageRegressionChecks.Run();
            HeavyGarbageRegressionChecks.Run();
            StaticInteractableScanRenderChecks.Run();
        });

        private static void ExitAfter(Action checks)
        {
            try { checks(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void CheckProductionPrefabs()
        {
            using (var f = new Fixture())
            {
                float previousArea = 0f;
                foreach (string name in new[] { "Small_Garbage", "Medium_Garbage", "Big_Garbage" })
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDirectory + name + ".prefab");
                    Require(prefab != null, name + " prefab must exist.");
                    var instance = Object.Instantiate(prefab);
                    SceneManager.MoveGameObjectToScene(instance, f.Scene);
                    ScannableInteractable target = instance.GetComponent<ScannableInteractable>();
                    Require(target != null && target.ScanType == InteractableScanType.StaticArea,
                        name + " must carry the static-area scan attribute on its root.");
                    Call(target, "OnEnable");
                    Require(target.CanScan, name + " must be scannable while active.");
                    RequireNear(target.DisplayDuration, 7f, "Garbage scan duration must match the animal marker.");
                    RequireNear(target.FadeOutDuration, .6f, "Garbage scan must have the authored final fade.");
                    RequireNear(target.FadeInDuration, .65f, "Garbage scan must use the smooth authored entrance duration.");
                    RequireNear(target.BreathingPeriod, 2f, "Garbage hatch must use its authored two-second pulse cycle.");
                    RequireNear(target.BreathingMinAlpha, .14f, "Garbage hatch must retain a visible pulse trough.");
                    RequireNear(target.BreathingMaxAlpha, .62f, "Garbage hatch must use its stronger pulse peak.");
                    RequireNear(target.OutlineBreathingPeriod, 2.4f, "The perimeter must retain its independent slower breathing cycle.");
                    RequireNear(target.OutlineBreathingMinAlpha, .42f, "The perimeter must retain its quiet breathing minimum.");
                    RequireNear(target.OutlineBreathingMaxAlpha, .5f, "The perimeter must retain its quiet breathing peak.");
                    Require(((Color32)target.AreaColor).Equals(new Color32(212, 184, 84, 255)),
                        "Garbage scan must use interaction gold #D4B854.");
                    RequireNear(target.LineAngleDegrees, 45f,
                        "Garbage scan lines must use the authored 45-degree slash at scan capture.");
                    RequireNear(target.LineWidthPixels, .9f,
                        "Garbage scan lines must use the authored 0.9-pixel bars.");
                    RequireNear(target.LineSpacingPixels, 7.7f,
                        "Garbage scan lines must use the authored 7.7-pixel row spacing.");
                    Require(target.ShowOutlineDots,
                        "Production garbage prefabs must enable the solid-diamond area boundary by default.");
                    RequireNear(target.OutlineDotSizePixels, 3.6f,
                        "Production perimeter diamonds must use the authored 3.6-pixel diagonal.");
                    RequireNear(target.OutlineDotSpacingPixels, 13f,
                        "Production perimeter dots must use thirteen-pixel reference spacing.");
                    RequireNear(target.SmallOutlineDotSpacingPixels, 9f,
                        "Small garbage must use nine-pixel reference perimeter spacing.");
                    Require(target.UseSmallAreaFill == (name == "Small_Garbage"),
                        "Only the small garbage prefab must enable the denser interior-fill presentation.");
                    var outline = new List<Vector3>();
                    Require(target.TryGetWorldOutline(outline) && outline.Count >= 3,
                        name + " must expose a filled scan envelope.");
                    float area = PolygonArea(outline);
                    Require(area > previousArea, "Small, medium and big garbage must expose increasing actual world areas.");
                    previousArea = area;

                    // Exercise automatic sprite-mesh extraction independently
                    // of the production prefabs' explicit outer envelopes.
                    SpriteRenderer body = (SpriteRenderer)Get(target, "spriteSource");
                    GameObject automaticObject = f.Create("Automatic " + name + " scan envelope");
                    automaticObject.transform.SetPositionAndRotation(body.transform.position, body.transform.rotation);
                    automaticObject.transform.localScale = body.transform.lossyScale;
                    SpriteRenderer automaticBody = automaticObject.AddComponent<SpriteRenderer>();
                    automaticBody.sprite = body.sprite;
                    ScannableInteractable automatic = automaticObject.AddComponent<ScannableInteractable>();
                    Call(automatic, "OnEnable");
                    var automaticOutline = new List<Vector3>();
                    Require(automatic.TryGetWorldOutline(automaticOutline),
                        "The production sprite mesh must support automatic scan geometry without a custom envelope.");
                    float rectangleArea = body.sprite.bounds.size.x * body.sprite.bounds.size.y
                        * Mathf.Abs(body.transform.lossyScale.x * body.transform.lossyScale.y);
                    Require(PolygonArea(automaticOutline) > 0f
                        && PolygonArea(automaticOutline) < rectangleArea * .99f,
                        "Automatic garbage envelopes must follow the sprite shape rather than its rectangular bounds.");
                    foreach (Vector2 vertex in body.sprite.vertices)
                        Require(InsideConvexOutline(automaticObject.transform.TransformPoint(vertex), automaticOutline),
                            "The automatic envelope must contain the actual production sprite mesh vertices.");
                }
            }
        }

        private static void CheckOutlineAndWaveContact()
        {
            using (var f = new Fixture())
            {
                ScannableInteractable target = f.Target(new Vector2(4f, 0f));
                var original = new List<Vector3>();
                Require(target.TryGetWorldOutline(original), "A target with a custom footprint must expose a scan envelope.");
                RequireNear(PolygonArea(original), 2f,
                    "A diamond scan footprint must use its filled envelope rather than the sprite rectangle.");
                Require(target.IntersectsScanWave(Vector2.zero, 2.9f, 3.05f),
                    "The wave must reveal a large object as soon as its nearest edge is touched.");
                Require(!target.IntersectsScanWave(Vector2.zero, 0f, 2.8f)
                    && !target.IntersectsScanWave(Vector2.zero, 5.1f, 6f),
                    "Objects ahead of or behind the wave must not be revealed.");
                Require(target.IntersectsScanWave(new Vector2(4f, 0f), 0f, .01f),
                    "A scan originating inside a filled envelope must reveal it, including radius zero.");
                Require(!target.IntersectsScanWave(new Vector2(4f, 0f), 1.1f, 1.2f),
                    "An annulus already outside a target must not retrigger it.");

                target.transform.SetPositionAndRotation(new Vector3(1f, 2f), Quaternion.Euler(0f, 0f, 37f));
                target.transform.localScale = new Vector3(2f, 3f, 1f);
                var transformed = new List<Vector3>();
                Require(target.TryGetWorldOutline(transformed), "Moving and scaling must retain a valid envelope.");
                RequireNear(PolygonArea(transformed), 12f,
                    "World scan area must follow the object's rotation and non-uniform scale.");
                foreach (Vector3 point in transformed)
                {
                    Vector3 local = target.transform.InverseTransformPoint(point);
                    Require(Mathf.Abs(Mathf.Abs(local.x) + Mathf.Abs(local.y) - 1f) < .001f,
                        "Transformed envelope vertices must remain on the diamond scan footprint.");
                }

                var presentation = new List<Vector2>();
                Require(target.TryGetPresentationFootprint(presentation, out Vector3 center),
                    "Static targets must expose their actual world-oriented presentation footprint.");
                Require(Vector3.Distance(center, target.transform.position) < .001f,
                    "A centered sprite must anchor the presentation to its actual world center.");
                var presentedWorld = new List<Vector3>();
                foreach (Vector2 offset in presentation) presentedWorld.Add(center + (Vector3)offset);
                RequireNear(PolygonArea(presentedWorld), 12f,
                    "Presentation geometry must preserve the physical area and non-uniform scale.");
                foreach (Vector3 point in presentedWorld)
                {
                    Vector3 local = target.transform.InverseTransformPoint(point);
                    Require(Mathf.Abs(Mathf.Abs(local.x) + Mathf.Abs(local.y) - 1f) < .001f,
                        "Presentation vertices must retain the target's actual world rotation and footprint.");
                }
            }
        }

        private static void CheckLifetimeAndInheritance()
        {
            using (var f = new Fixture())
            {
                ScannableInteractable source = f.Target(Vector2.zero);
                source.Reveal();
                Tick(source, 3f);
                RequireNear(source.RevealRemaining, 4f, "A scan must count down from seven seconds.");
                float previousTimeScale = Time.timeScale;
                try
                {
                    Time.timeScale = 0;
                    Tick(source, 1f);
                    RequireNear(source.RevealRemaining, 4f, "Simulation pause must freeze static scan lifetime.");
                }
                finally { Time.timeScale = previousTimeScale; }
                ScannableInteractable fragment = f.Target(Vector2.right * 4f);
                fragment.InheritScanFrom(source);
                RequireNear(fragment.RevealRemaining, 4f,
                    "Fragments must inherit the source's remaining time, without restarting seven seconds.");
                RequireNear(fragment.RevealElapsed, source.RevealElapsed,
                    "Fragments must inherit the source animation phase without restarting the entrance.");
                RequireNear(AreaTint(fragment).a, AreaTint(source).a,
                    "Identically styled fragments must inherit the visible source opacity on their first frame.");
                Require(fragment.OutlinePatternSeed == source.OutlinePatternSeed && fragment.OutlinePatternSeed != 0,
                    "Inherited reveals must retain the source's active randomized perimeter pattern.");
                RequireNear(OutlineTint(fragment).a, OutlineTint(source).a,
                    "Identically styled fragments must inherit the perimeter's independent breathing phase.");
                Tick(fragment, 4.1f);
                RequireNear(fragment.RevealRemaining, 0f, "An inherited reveal must expire independently.");
                source.Reveal();
                RequireNear(source.RevealRemaining, 7f, "A fresh scan must refresh the reveal duration.");
                source.gameObject.SetActive(false);
                Call(source, "OnDisable");
                Require(!source.CanScan && source.RevealRemaining == 0f,
                    "Disabling a target must immediately retire its reveal.");
                source.gameObject.SetActive(true);
                Call(source, "OnEnable");
                RequireNear(source.RevealRemaining, 0f, "Re-enabling a target must not resurrect a stale reveal.");
                WorldInteraction interaction = source.gameObject.AddComponent<WorldInteraction>();
                interaction.SetKind(WorldInteractionKind.Grabbable);
                Call(source, "OnEnable");
                source.Reveal();
                interaction.SetKind(WorldInteractionKind.None);
                Tick(source, 0f);
                Require(!source.CanScan && source.RevealRemaining == 0f,
                    "Recycling or disassembly must clear a target whose interaction kind becomes None.");
            }
        }

        private static void CheckControllerWave()
        {
            using (var f = new Fixture())
            using (var other = new Fixture())
            {
                BioScanController controller = f.Create("Static scan controller").AddComponent<BioScanController>();
                controller.Initialize(f.View, f.Camera);
                ScannableInteractable near = f.Target(new Vector2(4f, 0f));
                ScannableInteractable far = f.Target(new Vector2(8f, 0f));
                ScannableInteractable foreign = other.Target(new Vector2(4f, 0f));
                Set(controller, "scanWaveOrigin", Vector2.zero);
                Call(controller, "RevealTargetsCrossedByWave", 0f, 3f);
                RequireNear(near.RevealRemaining, 7f, "Controller must reveal a static target by its edge.");
                Require(far.RevealRemaining == 0f && foreign.RevealRemaining == 0f,
                    "A wave must ignore untouched targets and targets in a different scene.");
                Tick(near, 3f);
                Call(controller, "RevealTargetsCrossedByWave", 3f, 4f);
                RequireNear(near.RevealRemaining, 4f,
                    "A single wave must not refresh a large target as it continues through the envelope.");
                ((HashSet<int>)Get(controller, "revealedEntityIds")).Clear();
                Call(controller, "RevealTargetsCrossedByWave", 0f, 3f);
                RequireNear(near.RevealRemaining, 7f, "A subsequent wave must refresh an existing reveal.");

                BioScanAnimalMarkerUI animals = controller.GetComponent<BioScanAnimalMarkerUI>();
                Call(animals, "OnDisable");
                Canvas animalCanvas = (Canvas)Get(animals, "markerCanvas");
                if (animalCanvas != null) Object.DestroyImmediate(animalCanvas.gameObject);
                Object.DestroyImmediate((Material)Get(animals, "hollowMarkerMaterial"));
                Object.DestroyImmediate((Material)Get(animals, "solidMarkerMaterial"));
            }
        }

        private static void CheckParallelLinePattern()
        {
            using (var f = new Fixture())
            {
                ScannableInteractable target = f.Target(Vector2.zero);
                var polygon = new List<Vector2> {
                    new Vector2(-180f, -180f), new Vector2(180f, -180f),
                    new Vector2(180f, 180f), new Vector2(-180f, 180f) };
                var segments = new List<StaticScanLineSegment>();
                Vector2 origin = new Vector2(13f, -21f);
                Require(BioScanStaticLinePattern.Build(polygon, target.LineSpacingPixels,
                    target.LineAngleDegrees, segments, target.LineWidthPixels, origin),
                    "A large convex envelope must generate continuous parallel lines.");
                Require(segments.Count > 16,
                    "The large fixture must include many complete parallel rows.");
                List<Vector2> geometry = SegmentRectangles(segments, target.LineWidthPixels);
                float angle = target.LineAngleDegrees * Mathf.Deg2Rad;
                AssertParallelLineGeometry(geometry, target,
                    new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), origin, true);
                Require(GeometrySpan(geometry, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)))
                    > target.LineSpacingPixels * 10f,
                    "Parallel lines must span the area continuously rather than stop at former grid intersections.");
                bool hasLongContinuousLine = false;
                foreach (StaticScanLineSegment segment in segments)
                    hasLongContinuousLine |= (segment.End - segment.Start).magnitude
                        > target.LineSpacingPixels * 10f;
                Require(hasLongContinuousLine,
                    "A large footprint must contain long uninterrupted bars, rather than repeated short grid units.");
                AssertAllCornersInside(geometry, polygon,
                    "Every full parallel bar, including its antialias margin, must fit inside the convex envelope.");

                Vector2 translation = new Vector2(-37f, 61f);
                var translatedPolygon = new List<Vector2>();
                foreach (Vector2 point in polygon) translatedPolygon.Add(point + translation);
                var translatedSegments = new List<StaticScanLineSegment>();
                Require(BioScanStaticLinePattern.Build(translatedPolygon, target.LineSpacingPixels,
                    target.LineAngleDegrees, translatedSegments, target.LineWidthPixels, origin + translation),
                    "Translating the footprint and shared line rows must retain a valid pattern.");
                List<Vector2> translatedGeometry = SegmentRectangles(translatedSegments, target.LineWidthPixels);
                for (int index = 0; index < translatedGeometry.Count; index++)
                    translatedGeometry[index] -= translation;
                RequireGeometryEqual(geometry, translatedGeometry,
                    "A shared-row translation must preserve the complete continuous bars.");

                var tinyPolygon = new List<Vector2> {
                    new Vector2(-5f, -5f), new Vector2(5f, -5f),
                    new Vector2(5f, 5f), new Vector2(-5f, 5f) };
                Require(BioScanStaticLinePattern.Build(tinyPolygon, target.LineSpacingPixels,
                    target.LineAngleDegrees, segments, target.LineWidthPixels),
                    "A small envelope must retain a readable complete fallback bar.");
                Require(segments.Count == 1,
                    "An area unable to contain a full regular line must use one complete bar rather than fragments.");
                List<Vector2> tinyGeometry = SegmentRectangles(segments, target.LineWidthPixels);
                AssertParallelLineGeometry(tinyGeometry, target,
                    new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), Vector2.zero, false);
                AssertAllCornersInside(tinyGeometry, tinyPolygon,
                    "The tiny-area fallback must keep its full square ends and antialias margin inside the area.");
            }
        }

        private static void CheckSmoothInsetEnvelope()
        {
            const float width = .9f;
            var outline = new List<Vector2> {
                new Vector2(-20f, -20f), new Vector2(20f, -20f),
                new Vector2(20f, 20f), new Vector2(-20f, 20f) };
            var originalOutline = new List<Vector2>(outline);
            var smoothedOutline = new List<Vector2>(outline);
            // Construct the expected two rounds independently in edge order.
            // Convex interpolation can only remove sharp corners; it must
            // never make the marked physical footprint larger.
            for (int round = 0; round < 2; round++)
            {
                var next = new List<Vector2>(smoothedOutline.Count * 2);
                for (int index = 0; index < smoothedOutline.Count; index++)
                {
                    Vector2 a = smoothedOutline[index];
                    Vector2 b = smoothedOutline[(index + 1) % smoothedOutline.Count];
                    next.Add(a * .82f + b * .18f);
                    next.Add(a * .18f + b * .82f);
                }
                smoothedOutline = next;
            }
            var originalWorldOutline = new List<Vector3>();
            foreach (Vector2 point in outline) originalWorldOutline.Add(point);
            foreach (Vector2 point in smoothedOutline)
                Require(InsideConvexOutline(point, originalWorldOutline),
                    "Smoothing must form an inset contour inside the actual scan envelope.");
            var smooth = new List<StaticScanLineSegment>();
            var oracle = new List<StaticScanLineSegment>();
            var sharp = new List<StaticScanLineSegment>();
            Require(BioScanStaticLinePattern.Build(outline, 5f, 0f, smooth, width),
                "The default presentation must produce thin bars within a smoothed envelope.");
            Require(BioScanStaticLinePattern.Build(smoothedOutline, 5f, 0f,
                oracle, width, Vector2.zero, -1f, 0f),
                "The explicit inset contour must produce a matching unsmoothed oracle.");
            RequireGeometryEqual(SegmentRectangles(oracle, width), SegmentRectangles(smooth, width),
                "Default smoothing must use exactly the two authored inset rounds rather than expand or repeatedly shrink the footprint.");
            Require(outline.Count == originalOutline.Count,
                "Presentation smoothing must preserve the source scan envelope vertex count.");
            for (int index = 0; index < outline.Count; index++)
                Require(outline[index] == originalOutline[index],
                    "Presentation smoothing must not mutate the physical scan-contact envelope.");
            AssertAllCornersInside(SegmentRectangles(smooth, width), smoothedOutline,
                "Every complete thin bar and antialias fringe must remain within the smooth inset contour.");

            Require(BioScanStaticLinePattern.Build(outline, 5f, 0f,
                sharp, width, Vector2.zero, -1f, 0f),
                "A zero smoothing setting must retain an explicit sharp-envelope control.");
            Require(sharp.Count == 7,
                "The unsmoothed square control must retain its seven original complete horizontal rows.");
            foreach (StaticScanLineSegment segment in sharp)
            {
                RequireNear(segment.Start.x, -18.99f,
                    "Zero smoothing must retain the original square-envelope left endpoint with full cap padding.");
                RequireNear(segment.End.x, 18.99f,
                    "Zero smoothing must retain the original square-envelope right endpoint with full cap padding.");
            }
            Require(GeometryDiffers(SegmentRectangles(sharp, width), SegmentRectangles(smooth, width)),
                "The new smooth presentation must actually soften the corner rows compared with zero smoothing.");
            outline.Reverse();
            var reversed = new List<StaticScanLineSegment>();
            Require(BioScanStaticLinePattern.Build(outline, 5f, 0f, reversed, width),
                "Clockwise envelopes must support the same inset presentation.");
            RequireGeometryEqual(SegmentRectangles(smooth, width), SegmentRectangles(reversed, width),
                "Reversing the outline winding must retain the exact same complete thin bars.");

            // Sample phases across one row period to exercise a short edge
            // bar without coupling the fixture to one accidental row phase.
            var diamond = new List<Vector2> {
                new Vector2(-30f, 0f), new Vector2(0f, -20f),
                new Vector2(30f, 0f), new Vector2(0f, 20f) };
            bool keptShortEdgeBar = false;
            for (int phase = 0; phase < 32 && !keptShortEdgeBar; phase++)
            {
                Vector2 origin = Vector2.up * (phase * 7.7f / 32f);
                var complete = new List<StaticScanLineSegment>();
                var oldLengthFilter = new List<StaticScanLineSegment>();
                Require(BioScanStaticLinePattern.Build(diamond, 7.7f, 0f,
                    complete, width, origin), "The thin edge-row fixture must generate a valid area.");
                Require(BioScanStaticLinePattern.Build(diamond, 7.7f, 0f,
                    oldLengthFilter, width, origin, 7.7f * 1.25f),
                    "The original longer edge-row filter must remain a valid comparison.");
                if (complete.Count <= oldLengthFilter.Count) continue;
                foreach (StaticScanLineSegment segment in complete)
                {
                    float length = (segment.End - segment.Start).magnitude;
                    Require(length >= 2.98f,
                        "The softened edge must retain readable complete bars rather than subpixel slivers.");
                    keptShortEdgeBar |= length < 7.7f * 1.25f - .02f;
                }
                AssertAllCornersInside(SegmentRectangles(complete, width), diamond,
                    "The newly retained shorter edge bars must keep their full square caps and antialias fringes inside the physical area.");
            }
            Require(keptShortEdgeBar,
                "The new edge treatment must retain complete short rows that the old minimum-length filter discarded.");
        }

        private static void CheckSmallGarbageAreaFill()
        {
            using (var f = new Fixture())
            {
                ConfigureNormalGarbageView(f);
                var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                    PrefabDirectory + "Small_Garbage.prefab"));
                SceneManager.MoveGameObjectToScene(instance, f.Scene);
                instance.transform.position = Vector3.zero;
                ScannableInteractable target = instance.GetComponent<ScannableInteractable>();
                Call(target, "OnEnable");
                var overlay = f.Create("Small garbage readable area fill")
                    .AddComponent<BioScanStaticAreaUI>();
                overlay.Initialize(f.View, f.Camera);
                overlay.ShowScannedInteractable(target);

                var referenceOutline = new List<Vector2>();
                Require(target.TryGetPresentationFootprint(referenceOutline, out Vector3 center),
                    "The production small garbage must expose its actual footprint.");
                for (int index = 0; index < referenceOutline.Count; index++)
                    referenceOutline[index] *= 60f;
                var previousSegments = new List<StaticScanLineSegment>();
                Require(BioScanStaticLinePattern.Build(referenceOutline, target.LineSpacingPixels,
                    target.LineAngleDegrees, previousSegments, 1.4f,
                    -(Vector2)center * 60f, target.LineSpacingPixels * 1.25f, 0f),
                    "The original sparse small-garbage row rule must remain a valid comparison.");
                var expectedSmallSegments = new List<StaticScanLineSegment>();
                Require(BioScanStaticLinePattern.BuildSmallArea(referenceOutline,
                    target.SmallAreaLineSpacingPixels, target.LineAngleDegrees,
                    expectedSmallSegments, target.LineWidthPixels),
                    "The direct small-area rule must produce readable production garbage geometry.");

                List<Vector2> geometry = CaptureLocalGeometry(f, overlay, target);
                RequireNear((float)Get(overlay, "referencePixelsPerWorldUnit"), 60f,
                    "The small-garbage fixture must use a stable 1080/18 normal-view reference, independently of the editor Game view size.");
                int filledRowCount = geometry.Count / 4;
                Require(filledRowCount >= 3 && filledRowCount <= 5,
                    "The actual small garbage at the normal 1080/18 reference scale should contain three to five readable rows."
                    + " Filled: " + filledRowCount + ", previous: " + previousSegments.Count
                    + ", direct small build: " + expectedSmallSegments.Count
                    + ", cached reference PPU: " + Get(overlay, "referencePixelsPerWorldUnit")
                    + ", camera pixel height: " + f.Camera.pixelRect.height
                    + ", normal ortho size: " + f.View.BaseCameraOrthographicSize + ".");
                Require(filledRowCount > previousSegments.Count,
                    "Small garbage must gain interior rows instead of retaining the previous sparse one-line appearance.");
                AssertSmallFillGeometry(geometry, target, ProjectedLineDirection(f.Camera, target), true);
                AssertBarsInsideProjectedFootprint(geometry, f, target);
                bool retainedShortEdgeBar = false;
                foreach (StaticScanLineSegment segment in ScreenCenterlines(geometry))
                    retainedShortEdgeBar |= (segment.End - segment.Start).magnitude
                        < Mathf.Max(6f, target.LineSpacingPixels * 1.25f) - .02f;
                Require(retainedShortEdgeBar,
                    "The small fill must retain at least one complete short edge bar previously removed by the generic minimum length.");

                List<Vector3> baseline = CaptureWorldCenterlines(f, overlay);
                for (int frame = 0; frame <= 30; frame++)
                {
                    float phase = frame / 30f;
                    f.Camera.transform.SetPositionAndRotation(new Vector3(
                        Mathf.Sin(phase * 6f) * .025f,
                        Mathf.Cos(phase * 5f) * .017f, -10f),
                        Quaternion.Euler(0f, 0f, phase * 270f));
                    f.Camera.orthographicSize = frame <= 15
                        ? Mathf.Lerp(9f, 14f, frame / 15f)
                        : Mathf.Lerp(14f, 6f, (frame - 15) / 15f);
                    RequireCenterlinesEqual(baseline, CaptureWorldCenterlines(f, overlay),
                        "Small-garbage density and short edge bars must keep the same cached world endpoints during camera motion and scan zoom.");
                }
                f.Camera.transform.SetPositionAndRotation(new Vector3(0f, 0f, -10f), Quaternion.identity);
                f.Camera.orthographicSize = 9f;
                overlay.ShowScannedInteractable(target);
                RequireCenterlinesEqual(baseline, CaptureWorldCenterlines(f, overlay),
                    "Refreshing a small-garbage reveal must retain the cached dense pattern.");

                Tick(target, 7.1f);
                ScannableInteractable tiny = f.Target(Vector2.zero);
                tiny.EnableSmallAreaFill();
                tiny.transform.localScale = Vector3.one * .08f;
                overlay.ShowScannedInteractable(tiny);
                List<Vector2> tinyGeometry = CaptureLocalGeometry(f, overlay, tiny);
                Require(tinyGeometry.Count == 4,
                    "An extremely small fill must use one readable complete fallback instead of forcing three rows into a yellow blob.");
                AssertSmallFillGeometry(tinyGeometry, tiny, ProjectedLineDirection(f.Camera, tiny), false);
                AssertBarsInsideProjectedFootprint(tinyGeometry, f, tiny);
            }

            // The presentation opt-in also covers old serialized scene objects
            // and runtime creations whose new attribute still has its default.
            using (var f = new Fixture())
            {
                ScannableInteractable generic = f.Target(Vector2.zero);
                Require(!generic.UseSmallAreaFill,
                    "A generic static-area target must retain the default parallel-row rule.");
                foreach (RecyclableSize size in new[] {
                    RecyclableSize.Small, RecyclableSize.Medium, RecyclableSize.Big })
                {
                    GarbageItem item = f.Create("Legacy garbage " + size).AddComponent<GarbageItem>();
                    Set(item, "size", size);
                    ScannableInteractable target = item.GetComponent<ScannableInteractable>();
                    Set(target, "useSmallAreaFill", false);
                    Call(item, "Awake");
                    Require(target.UseSmallAreaFill == (size == RecyclableSize.Small),
                        "GarbageItem.Awake must enable the new fill for old/dynamic small garbage only.");
                }
            }

            foreach (string name in new[] { "Medium_Garbage", "Big_Garbage" })
            {
                using (var f = new Fixture())
                {
                    ConfigureNormalGarbageView(f);
                    var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                        PrefabDirectory + name + ".prefab"));
                    SceneManager.MoveGameObjectToScene(instance, f.Scene);
                    instance.transform.position = Vector3.zero;
                    ScannableInteractable target = instance.GetComponent<ScannableInteractable>();
                    Call(target, "OnEnable");
                    Call(instance.GetComponent<GarbageItem>(), "Awake");
                    Require(!target.UseSmallAreaFill,
                        name + " must preserve the generic parallel-line presentation after Awake.");
                    var outline = new List<Vector2>();
                    Require(target.TryGetPresentationFootprint(outline, out Vector3 center),
                        name + " must expose its actual production footprint.");
                    for (int index = 0; index < outline.Count; index++) outline[index] *= 60f;
                    var expectedSegments = new List<StaticScanLineSegment>();
                    Require(BioScanStaticLinePattern.Build(outline, target.LineSpacingPixels,
                        target.LineAngleDegrees, expectedSegments, target.LineWidthPixels,
                        -(Vector2)center * 60f), name + " must use the new thin, smooth generic line layout.");
                    var expectedWorldEndpoints = new List<Vector3>();
                    foreach (StaticScanLineSegment segment in expectedSegments)
                    {
                        expectedWorldEndpoints.Add(center + (Vector3)(segment.Start / 60f));
                        expectedWorldEndpoints.Add(center + (Vector3)(segment.End / 60f));
                    }
                    var oldStyleSegments = new List<StaticScanLineSegment>();
                    Require(BioScanStaticLinePattern.Build(outline, target.LineSpacingPixels,
                        target.LineAngleDegrees, oldStyleSegments, 1.4f,
                        -(Vector2)center * 60f, target.LineSpacingPixels * 1.25f, 0f),
                        name + " must retain a valid old-style control.");
                    Require(GeometryDiffers(SegmentRectangles(oldStyleSegments, 1.4f),
                        SegmentRectangles(expectedSegments, target.LineWidthPixels)),
                        name + " must actually receive the thinner, smoother style rather than retain the original geometry.");
                    var overlay = f.Create("Thin smooth " + name + " rows").AddComponent<BioScanStaticAreaUI>();
                    overlay.Initialize(f.View, f.Camera);
                    overlay.ShowScannedInteractable(target);
                    RequireCenterlinesEqual(expectedWorldEndpoints, CaptureWorldCenterlines(f, overlay),
                        name + " must match every new smooth world endpoint, as well as the 0.9/7.7 style values.");
                    AssertBarsInsideProjectedFootprint(CaptureLocalGeometry(f, overlay, target), f, target);
                }
            }
        }

        private static void ConfigureNormalGarbageView(Fixture fixture)
        {
            Canvas.ForceUpdateCanvases();
            fixture.SetCameraRenderTarget(1920, 1080);
            fixture.Camera.orthographicSize = 9f;
            Set(fixture.View, "baseCameraOrthographicSize", 9f);
            RequireNear(fixture.Camera.pixelRect.height, 1080f,
                "The normal-view production garbage fixture must retain its render-target viewport.");
        }

        private static List<StaticScanLineSegment> ScreenCenterlines(List<Vector2> geometry)
        {
            var segments = new List<StaticScanLineSegment>(geometry.Count / 4);
            for (int index = 0; index < geometry.Count; index += 4)
            {
                Vector2 direction = (geometry[index + 2] - geometry[index + 1]).normalized;
                segments.Add(new StaticScanLineSegment(
                    (geometry[index] + geometry[index + 1]) * .5f + direction,
                    (geometry[index + 2] + geometry[index + 3]) * .5f - direction));
            }
            return segments;
        }

        private static void AssertSmallFillGeometry(List<Vector2> geometry,
            ScannableInteractable target, Vector2 direction, bool regularRows)
        {
            Require(geometry.Count >= 4 && geometry.Count % 4 == 0,
                "Small-area fill must consist entirely of complete four-corner bars.");
            foreach (StaticScanLineSegment segment in ScreenCenterlines(geometry))
            {
                Vector2 edge = segment.End - segment.Start;
                Require(Mathf.Abs(Vector2.Dot(edge.normalized, direction)) > .9999f,
                    "The extra small-garbage rows must retain the one existing world direction.");
                Require(edge.magnitude > 0f && (!regularRows || edge.magnitude >= 3.98f),
                    "Small fill must retain readable four-pixel edge bars, with a shorter fallback only for an extremely small area.");
            }
            for (int index = 0; index < geometry.Count; index += 4)
            {
                Vector2 lengthEdge = geometry[index + 2] - geometry[index + 1];
                Vector2 widthEdge = geometry[index + 1] - geometry[index];
                Require(Mathf.Abs(Vector2.Dot(lengthEdge.normalized, widthEdge.normalized)) < .0001f,
                    "Dense small-garbage lines must retain complete perpendicular square caps.");
                RequireNear(widthEdge.magnitude, target.LineWidthPixels + 2f,
                    "The denser small fill must preserve the new 0.9-pixel stroke and full antialias margin.");
            }
            AssertNonIntersectingBars(geometry);
        }

        private static void CheckDashedOutlinePattern()
        {
            var polygon = new List<Vector2> {
                new Vector2(-50f, -30f), new Vector2(30f, -35f), new Vector2(65f, 0f),
                new Vector2(40f, 45f), new Vector2(-35f, 50f), new Vector2(-60f, 8f) };
            var original = new List<Vector2>(polygon);
            var segments = new List<StaticScanLineSegment>();
            Require(BioScanStaticOutlinePattern.Build(polygon, 6f, 4f, segments, .9f),
                "A convex scan footprint must support a closed dashed outline.");
            Require(segments.Count > 10,
                "A large footprint must receive dashes around its perimeter rather than only isolated corner marks.");
            foreach (StaticScanLineSegment segment in segments)
            {
                float length = (segment.End - segment.Start).magnitude;
                Require(length >= 3f && length <= 6.01f,
                    "Each perimeter dash must remain a readable complete chord no longer than its authored arc interval.");
                Require(!float.IsNaN(segment.Start.x) && !float.IsNaN(segment.Start.y)
                    && !float.IsNaN(segment.End.x) && !float.IsNaN(segment.End.y),
                    "Outline generation must not emit non-finite dash endpoints.");
            }
            List<Vector2> baseline = SegmentRectangles(segments, .9f);
            AssertAllCornersInside(baseline, polygon,
                "Every outline dash must keep its complete square caps, stroke and antialias rectangle within the actual scan envelope.");
            AssertNonIntersectingBars(VisibleBarRectangles(baseline));
            Require(polygon.Count == original.Count,
                "Outline presentation must not alter the physical envelope vertex count.");
            for (int index = 0; index < polygon.Count; index++)
                Require(polygon[index] == original[index],
                    "Building a dashed outline must not mutate scan-contact geometry.");
            polygon.Reverse();
            Require(BioScanStaticOutlinePattern.Build(polygon, 6f, 4f, segments, .9f),
                "A clockwise outline must support the same dashed presentation.");
            RequireGeometryEqual(baseline, SegmentRectangles(segments, .9f),
                "Opposite envelope winding must retain identical perimeter dashes and closure phase.");
            Vector2 translation = new Vector2(17f, -23f);
            for (int index = 0; index < polygon.Count; index++) polygon[index] += translation;
            Require(BioScanStaticOutlinePattern.Build(polygon, 6f, 4f, segments, .9f),
                "Translating a footprint must retain a valid dashed perimeter.");
            List<Vector2> translated = SegmentRectangles(segments, .9f);
            for (int index = 0; index < translated.Count; index++) translated[index] -= translation;
            RequireGeometryEqual(baseline, translated,
                "Object translation must preserve dash identities and their relative world endpoints.");
            var small = new List<Vector2> {
                new Vector2(-7f, -7f), new Vector2(7f, -7f),
                new Vector2(7f, 7f), new Vector2(-7f, 7f) };
            Require(BioScanStaticOutlinePattern.Build(small, 6f, 4f, segments, .9f)
                && segments.Count >= 3,
                "A small readable footprint must adapt its perimeter into several complete dashes.");
            AssertAllCornersInside(SegmentRectangles(segments, .9f), small,
                "Small outlines must retain complete inset strokes instead of mask-cut edge fragments.");
            var tooSmall = new List<Vector2> {
                new Vector2(-.2f, -.2f), new Vector2(.2f, -.2f),
                new Vector2(.2f, .2f), new Vector2(-.2f, .2f) };
            Require(!BioScanStaticOutlinePattern.Build(tooSmall, 6f, 4f, segments, .9f)
                && segments.Count == 0,
                "A footprint unable to fit a complete outline stroke must emit no broken slivers.");
            var tooNarrow = new List<Vector2> {
                new Vector2(-2.1f, -50f), new Vector2(2.1f, -50f),
                new Vector2(2.1f, 50f), new Vector2(-2.1f, 50f) };
            Require(!BioScanStaticOutlinePattern.Build(tooNarrow, 6f, 4f, segments, .9f)
                && segments.Count == 0,
                "A tall narrow footprint must omit its outline when opposing inset dashes cannot retain a visible gap.");
            float rotation = 37f * Mathf.Deg2Rad;
            for (int index = 0; index < tooNarrow.Count; index++)
            {
                Vector2 point = tooNarrow[index];
                tooNarrow[index] = new Vector2(point.x * Mathf.Cos(rotation) - point.y * Mathf.Sin(rotation),
                    point.x * Mathf.Sin(rotation) + point.y * Mathf.Cos(rotation));
            }
            Require(!BioScanStaticOutlinePattern.Build(tooNarrow, 6f, 4f, segments, .9f)
                && segments.Count == 0,
                "The opposing-edge clearance guard must also reject the same narrow footprint after rotation.");
            Require(!BioScanStaticOutlinePattern.Build(original, float.NaN, 4f, segments, .9f)
                && !BioScanStaticOutlinePattern.Build(original, 6f, -1f, segments, .9f)
                && !BioScanStaticOutlinePattern.Build(null, 6f, 4f, segments, .9f),
                "Invalid dashed-outline input must fail cleanly without stale geometry.");
        }

        private static void CheckOutlineDotPattern()
        {
            const float dotSize = 3.6f;
            var polygon = new List<Vector2> {
                new Vector2(-50f, -30f), new Vector2(30f, -35f), new Vector2(65f, 0f),
                new Vector2(40f, 45f), new Vector2(-35f, 50f), new Vector2(-60f, 8f) };
            var original = new List<Vector2>(polygon);
            var points = new List<Vector2>();
            var repeated = new List<Vector2>();
            var different = new List<Vector2>();
            UnityEngine.Random.State globalRandom = UnityEngine.Random.state;
            Require(BioScanStaticOutlinePattern.BuildDots(polygon, 13f, points, dotSize, 12345u)
                && points.Count > 10,
                "A readable large footprint must receive separate diamond points around its perimeter.");
            Require(BioScanStaticOutlinePattern.BuildDots(polygon, 13f, repeated, dotSize, 12345u)
                && BioScanStaticOutlinePattern.BuildDots(polygon, 13f, different, dotSize, 67890u),
                "Seeded perimeter points must support reproducible layouts.");
            RequireWorldPointsEqual(ToWorldPoints(points), ToWorldPoints(repeated),
                "The same scan seed must reproduce every perimeter point center.");
            Require(!WorldPointsMatch(ToWorldPoints(points), ToWorldPoints(different)),
                "Different scan rounds must produce different point arrangements.");
            var diamonds = new List<StaticScanLineSegment>();
            Vector2 direction = Vector2.one.normalized;
            Require(BioScanStaticOutlinePattern.BuildDiamondSegments(points, direction, dotSize, diamonds)
                && diamonds.Count == points.Count,
                "Each perimeter point must produce exactly one complete solid diamond glyph.");
            List<Vector2> geometry = SegmentRectangles(diamonds, dotSize / Mathf.Sqrt(2f));
            AssertDiamondGeometry(geometry, direction, dotSize);
            AssertAllCornersInside(geometry, polygon,
                "Each diamond's complete square, including antialias padding, must fit within the physical footprint.");
            AssertNonIntersectingBars(VisibleBarRectangles(geometry));
            float minimumX = float.PositiveInfinity, maximumX = float.NegativeInfinity;
            float minimumY = float.PositiveInfinity, maximumY = float.NegativeInfinity;
            for (int index = 0; index < points.Count; index++)
            {
                Require(Vector2.Distance((diamonds[index].Start + diamonds[index].End) * .5f, points[index]) < .0001f,
                    "Diamond construction must preserve the supplied point center instead of extending into a dash.");
                minimumX = Mathf.Min(minimumX, points[index].x); maximumX = Mathf.Max(maximumX, points[index].x);
                minimumY = Mathf.Min(minimumY, points[index].y); maximumY = Mathf.Max(maximumY, points[index].y);
            }
            Require(minimumX < -40f && maximumX > 45f && minimumY < -25f && maximumY > 35f,
                "Perimeter points must surround the object rather than collapse into a central dot cluster.");
            for (int index = 0; index < polygon.Count; index++)
                Require(polygon[index] == original[index],
                    "Generating perimeter dots must not mutate physical scan geometry.");
            polygon.Reverse();
            Require(BioScanStaticOutlinePattern.BuildDots(polygon, 13f, repeated, dotSize, 12345u),
                "Perimeter dots must support clockwise envelopes.");
            RequireWorldPointsEqual(ToWorldPoints(points), ToWorldPoints(repeated),
                "Reversing envelope winding must preserve seeded perimeter point placement.");
            Vector2 translation = new Vector2(17f, -23f);
            for (int index = 0; index < polygon.Count; index++) polygon[index] += translation;
            Require(BioScanStaticOutlinePattern.BuildDots(polygon, 13f, repeated, dotSize, 12345u),
                "Translated footprints must retain a valid dotted boundary.");
            for (int index = 0; index < repeated.Count; index++) repeated[index] -= translation;
            RequireWorldPointsEqual(ToWorldPoints(points), ToWorldPoints(repeated),
                "Object translation must preserve each seeded diamond center relative to its footprint.");
            var small = new List<Vector2> { new Vector2(-12f, -12f), new Vector2(12f, -12f),
                new Vector2(12f, 12f), new Vector2(-12f, 12f) };
            Require(BioScanStaticOutlinePattern.BuildDots(small, 9f, repeated, dotSize, 12345u)
                && repeated.Count >= 3,
                "A small readable footprint must retain several isolated diamond boundary points.");
            Require(BioScanStaticOutlinePattern.BuildDiamondSegments(repeated, direction, dotSize, diamonds),
                "Small boundary points must remain complete glyphs.");
            AssertAllCornersInside(SegmentRectangles(diamonds, dotSize / Mathf.Sqrt(2f)), small,
                "Small boundary diamonds must not be clipped by an area mask.");
            var tooSmall = new List<Vector2> { new Vector2(-.2f, -.2f), new Vector2(.2f, -.2f),
                new Vector2(.2f, .2f), new Vector2(-.2f, .2f) };
            Require(!BioScanStaticOutlinePattern.BuildDots(tooSmall, 9f, repeated, dotSize, 12345u)
                && repeated.Count == 0,
                "A footprint too small for one complete glyph must emit no broken point geometry.");
            Require(!BioScanStaticOutlinePattern.BuildDots(original, float.NaN, repeated, dotSize)
                && !BioScanStaticOutlinePattern.BuildDots(original, 13f, repeated, float.NaN)
                && !BioScanStaticOutlinePattern.BuildDots(null, 13f, repeated, dotSize),
                "Invalid dot-pattern parameters must fail cleanly.");
            Require(globalRandom.Equals(UnityEngine.Random.state),
                "Perimeter point generation must leave Unity's shared gameplay Random state unchanged.");
        }

        private static void CheckPointBoundaryClipping()
        {
            var footprint = new List<Vector2> { new Vector2(-40f, -35f), new Vector2(40f, -35f),
                new Vector2(40f, 35f), new Vector2(-40f, 35f) };
            var points = new List<Vector2>();
            Require(BioScanStaticOutlinePattern.BuildDots(footprint, 13f, points, 3.6f, 12345u),
                "The clipping counterexample must have a real inset diamond-point boundary.");
            var hull = new List<Vector2>();
            Require(BioScanStaticLinePattern.BuildPointBoundaryHull(points, hull) && hull.Count >= 3,
                "Actual diamond centers must produce a filled convex boundary without another smoothing pass.");
            foreach (Vector2 vertex in hull)
            {
                bool actualCenter = false;
                foreach (Vector2 point in points) actualCenter |= Vector2.Distance(vertex, point) < .0001f;
                Require(actualCenter,
                    "The clipping boundary must use actual diamond centers rather than newly smoothed or expanded vertices.");
            }
            AssertAllCornersInside(points, hull,
                "The boundary hull must contain every actual diamond center.");
            const float worldScale = .001f;
            var worldPoints = new List<Vector2>(points.Count);
            foreach (Vector2 point in points) worldPoints.Add(point * worldScale);
            var worldHull = new List<Vector2>();
            Require(BioScanStaticLinePattern.BuildPointBoundaryHull(worldPoints, worldHull)
                && worldHull.Count >= 3,
                "Building the actual point hull must remain valid in world units; a screen-pixel collinearity tolerance must not collapse its smaller coordinates.");
            var restoredHull = new List<Vector2>(worldHull.Count);
            foreach (Vector2 point in worldHull) restoredHull.Add(point / worldScale);
            float normalArea = PolygonArea(ToWorldPoints(hull));
            Require(Mathf.Abs(PolygonArea(ToWorldPoints(restoredHull)) - normalArea) <= normalArea * .001f,
                "Scaling the same actual diamond centers into world units must preserve the point hull's normalized area.");
            AssertQuadClearance(points, restoredHull, 0f,
                "The world-unit point hull must still contain the same actual centers after restoring the reference scale.");
            AssertQuadClearance(restoredHull, hull, 0f,
                "World-unit hull construction must not expand the normalized point boundary.");
            var source = new List<StaticScanLineSegment>();
            Require(BioScanStaticLinePattern.Build(footprint, 7.7f, 45f, source, .9f),
                "The clipping counterexample must retain the existing source hatch.");
            List<Vector2> original = SegmentRectangles(source, .9f);
            bool formerlyOutside = false;
            foreach (Vector2 corner in original)
                formerlyOutside |= !InsideConvexOutline(corner, ToWorldPoints(hull));
            Require(formerlyOutside,
                "The regression fixture must reproduce the old hatch extending outside the inset point-center hull.");
            var clipped = new List<StaticScanLineSegment>();
            Require(BioScanStaticLinePattern.ClipToBoundary(source, hull, clipped, .9f, 1f, .25f),
                "A readable point boundary must retain complete clipped interior bars.");
            AssertQuadClearance(SegmentRectangles(clipped, .9f), hull, 1f,
                "Every complete visible hatch rectangle must stay one pixel inside the actual diamond boundary.");
            AssertSegmentsOnlyShorten(source, clipped);
            RequireGeometryEqual(original, SegmentRectangles(source, .9f),
                "Boundary clipping must not mutate the cached source rows.");
            hull.Reverse();
            var reversed = new List<StaticScanLineSegment>();
            Require(BioScanStaticLinePattern.ClipToBoundary(source, hull, reversed, .9f, 1f, .25f),
                "The same point boundary must support reversed winding.");
            RequireGeometryEqual(SegmentRectangles(clipped, .9f), SegmentRectangles(reversed, .9f),
                "Reversing the point hull must retain the same shortened rows and complete flat ends.");
            var insufficientPoints = new List<Vector2> { Vector2.left, Vector2.right };
            Require(!BioScanStaticLinePattern.BuildPointBoundaryHull(insufficientPoints, hull) && hull.Count == 0,
                "One or two boundary points must not invent an interior hatch area.");
            Require(!BioScanStaticLinePattern.ClipToBoundary(source, hull, clipped, .9f, 1f, .25f)
                && clipped.Count == 0,
                "An invalid or degenerate boundary must cleanly omit interior hatching.");
        }

        private static void CheckPointBoundaryCameraAndOverlap()
        {
            foreach (Vector2 position in new[] { new Vector2(3f, 0f), new Vector2(120f, 100f) })
            {
                using (var f = new Fixture())
                {
                    f.AlignOnScan = true;
                    f.ShowOutlineDots = true;
                    ScannableInteractable target = f.Target(position);
                    ConfigureAsymmetricTarget(target);
                    f.Camera.transform.position = new Vector3(position.x - 2f, position.y, -10f);
                    var overlay = f.Create("Camera-stable diamond boundary clipping").AddComponent<BioScanStaticAreaUI>();
                    overlay.Initialize(f.View, f.Camera);
                    overlay.ShowScannedInteractable(target);
                    f.UpdateOverlay(overlay);
                    List<Vector3> source = CaptureCachedWorldCenterlines(overlay, target);
                    List<Vector3> dots = CaptureOutlineWorldPoints(f, overlay);
                    for (int frame = 0; frame <= 24; frame++)
                    {
                        float phase = frame / 24f;
                        f.Camera.transform.SetPositionAndRotation(new Vector3(position.x - 2f
                            + Mathf.Sin(phase * 6f) * .025f, position.y + Mathf.Cos(phase * 5f) * .017f, -10f),
                            Quaternion.Euler(0f, 0f, frame * 13f));
                        f.Camera.orthographicSize = frame <= 12
                            ? Mathf.Lerp(5f, 8f, frame / 12f) : Mathf.Lerp(8f, 5f, (frame - 12) / 12f);
                        AssertRenderedHatchInsideDots(f, overlay, target);
                        RequireCenterlinesEqual(source, CaptureCachedWorldCenterlines(overlay, target),
                            "Boundary clipping must retain source world rows and phase at every camera turn/zoom, including distant world coordinates.");
                        RequireWorldPointsEqual(dots, CaptureOutlineWorldPoints(f, overlay),
                            "Clipping hatching must never move the enclosing diamond centers.");
                    }
                    uint previousSeed = target.OutlinePatternSeed;
                    overlay.ShowScannedInteractable(target);
                    f.Camera.transform.rotation = Quaternion.Euler(0f, 0f, 67f);
                    AssertRenderedHatchInsideDots(f, overlay, target);
                    Require(target.OutlinePatternSeed != previousSeed,
                        "The rescan containment fixture must exercise a newly randomized boundary.");
                    List<Vector3> rescannedSource = CaptureCachedWorldCenterlines(overlay, target);
                    f.Camera.transform.rotation = Quaternion.Euler(0f, 0f, -35f);
                    f.Camera.orthographicSize = 8f;
                    AssertRenderedHatchInsideDots(f, overlay, target);
                    RequireCenterlinesEqual(rescannedSource, CaptureCachedWorldCenterlines(overlay, target),
                        "A replacement scan must still clip only the new source endpoints after subsequent movement.");
                }
            }

            using (var f = new Fixture())
            {
                f.ShowOutlineDots = true;
                // Lock this size-sensitive fixture to 60 reference px/world.
                // Its 9.6 px diamond has a 3.394 px inradius: enough for the
                // 3.224 px complete-glyph inset, but not separated extra dots.
                f.SetCameraRenderTarget(800, 600);
                ScannableInteractable tiny = f.Target(Vector2.zero);
                tiny.transform.localScale = Vector3.one * .08f;
                var overlay = f.Create("Insufficient diamond area omits interior hatch").AddComponent<BioScanStaticAreaUI>();
                overlay.Initialize(f.View, f.Camera);
                overlay.ShowScannedInteractable(tiny);
                List<Vector2> outline = CaptureOutlineGeometry(f, overlay, null, false);
                RequireNear((float)Get(overlay, "referencePixelsPerWorldUnit"), 60f,
                    "The single-diamond omission fixture must retain its explicit reference scale independently of the editor window.");
                Require(outline.Count == 4,
                    "The tiny-area fixture must retain one readable diamond without inventing a polygonal interior.");
                Require(CaptureScreenGeometry(f, overlay, null, false).Count == 0,
                    "Fewer than three usable diamond centers must cleanly omit interior lines while retaining the point marker.");
                f.ShowOutlineDots = false;
                Require(CaptureScreenGeometry(f, overlay).Count >= 4,
                    "Disabling the insufficient point boundary must restore the original readable source hatch.");
            }

            using (var f = new Fixture())
            {
                f.ShowOutlineDots = true;
                var overlay = f.Create("No invisible hatch overlap reservations").AddComponent<BioScanStaticAreaUI>();
                overlay.Initialize(f.View, f.Camera);
                ScannableInteractable first = f.Target(new Vector2(2f, 0f));
                ScannableInteractable second = f.Target(new Vector2(2f, 0f) + Vector2.one.normalized * .4f);
                first.transform.localScale = second.transform.localScale = Vector3.one * 3f;
                overlay.ShowScannedInteractable(first);
                overlay.ShowScannedInteractable(second);
                Tick(first, first.FadeInDuration);
                Tick(second, second.FadeInDuration);
                float firstRemaining = first.RevealRemaining, secondRemaining = second.RevealRemaining;
                Set(second, "revealRemaining", 0f);
                List<Vector2> firstOnly = CaptureScreenGeometry(f, overlay);
                List<Vector2> uncutFirst = ProjectCachedHatchRectangles(f, overlay, first);
                Set(second, "revealRemaining", secondRemaining);
                Set(first, "revealRemaining", 0f);
                List<Vector2> secondOnly = CaptureScreenGeometry(f, overlay);
                Set(first, "revealRemaining", firstRemaining);
                Vector2 direction = ProjectedLineDirection(f.Camera, first);
                Require(HasGhostSensitiveCoverage(uncutFirst, firstOnly, secondOnly, direction),
                    "The overlap fixture must include visible second-target coverage inside the first target's discarded source fringe.");
                List<Vector2> combined = CaptureScreenGeometry(f, overlay);
                AssertCoverageUnion(firstOnly, secondOnly, combined, direction,
                    "Only boundary-clipped visible pieces may reserve overlap coverage; discarded source fringes must not punch holes in another target.");
                AssertNonIntersectingBars(combined);
            }
        }

        private static bool HasGhostSensitiveCoverage(List<Vector2> uncutFirst, List<Vector2> visibleFirst,
            List<Vector2> visibleSecond, Vector2 direction)
        {
            List<LineInterval> source = LineIntervals(uncutFirst, direction);
            List<LineInterval> first = LineIntervals(visibleFirst, direction);
            foreach (LineInterval interval in LineIntervals(visibleSecond, direction))
            {
                int samples = Mathf.Max(2, Mathf.CeilToInt((interval.End - interval.Start) * 2f));
                for (int sample = 0; sample <= samples; sample++)
                {
                    float along = Mathf.Lerp(interval.Start, interval.End, sample / (float)samples);
                    if (ContainsCoverage(source, interval.Row, along, .02f)
                        && !ContainsCoverage(first, interval.Row, along, 3f)) return true;
                }
            }
            return false;
        }

        private static void CheckOutlineRandomization()
        {
            var polygon = new List<Vector2> {
                new Vector2(-50f, -30f), new Vector2(30f, -35f), new Vector2(65f, 0f),
                new Vector2(40f, 45f), new Vector2(-35f, 50f), new Vector2(-60f, 8f) };
            var first = new List<StaticScanLineSegment>();
            var repeated = new List<StaticScanLineSegment>();
            var different = new List<StaticScanLineSegment>();
            UnityEngine.Random.State globalRandom = UnityEngine.Random.state;
            Require(BioScanStaticOutlinePattern.Build(polygon, 6f, 4f, first, .9f, 12345u)
                && BioScanStaticOutlinePattern.Build(polygon, 6f, 4f, repeated, .9f, 12345u)
                && BioScanStaticOutlinePattern.Build(polygon, 6f, 4f, different, .9f, 67890u),
                "Seeded perimeter patterns must produce readable complete dashes.");
            RequireGeometryEqual(SegmentRectangles(first, .9f), SegmentRectangles(repeated, .9f),
                "The same scan seed must reproduce exactly the same perimeter pattern.");
            Require(GeometryDiffers(SegmentRectangles(first, .9f), SegmentRectangles(different, .9f)),
                "Different scan seeds must produce different dash lengths, gaps or starting phases.");
            float minimumLength = float.PositiveInfinity, maximumLength = 0f;
            foreach (StaticScanLineSegment segment in first)
            {
                float length = (segment.End - segment.Start).magnitude;
                Require(length > 0f && length <= 8.01f,
                    "Randomized dashes must remain complete positive-length chords within the authored random length range.");
                minimumLength = Mathf.Min(minimumLength, length);
                maximumLength = Mathf.Max(maximumLength, length);
            }
            Require(maximumLength - minimumLength > 1f,
                "A seeded perimeter must visibly vary its dash lengths instead of only rotating the former uniform pattern.");
            AssertAllCornersInside(SegmentRectangles(first, .9f), polygon,
                "Randomized dashes must retain their full stroke and antialias rectangles inside the scan footprint.");
            AssertNonIntersectingBars(VisibleBarRectangles(SegmentRectangles(first, .9f)));
            polygon.Reverse();
            Require(BioScanStaticOutlinePattern.Build(polygon, 6f, 4f, repeated, .9f, 12345u),
                "The seeded perimeter must support reversed envelope winding.");
            RequireGeometryEqual(SegmentRectangles(first, .9f), SegmentRectangles(repeated, .9f),
                "A scan seed must retain its complete pattern when envelope winding is reversed.");

            using (var f = new Fixture())
            {
                ScannableInteractable target = f.Target(Vector2.zero);
                target.Reveal();
                uint firstSeed = target.OutlinePatternSeed;
                Require(firstSeed != 0, "Every real scan must assign a nonzero perimeter seed.");
                target.CaptureScanOrientation(17f);
                Tick(target, 1.1f);
                Require(target.OutlinePatternSeed == firstSeed,
                    "Direction capture and animation time must not consume another perimeter seed.");
                ScannableInteractable other = f.Target(Vector2.right * 4f);
                other.Reveal();
                Require(other.OutlinePatternSeed != 0 && other.OutlinePatternSeed != firstSeed,
                    "Different objects scanned during the same session must receive independent perimeter seeds.");
                float elapsed = target.RevealElapsed;
                target.Reveal();
                uint refreshedSeed = target.OutlinePatternSeed;
                Require(refreshedSeed != 0 && refreshedSeed != firstSeed,
                    "A repeat real scan must replace the object's perimeter seed exactly for the new round.");
                RequireNear(target.RevealElapsed, elapsed,
                    "Randomizing a rescanned perimeter must preserve the visible target's animation clock.");
                ScannableInteractable inherited = f.Target(Vector2.left * 4f);
                inherited.InheritScanFrom(target);
                Require(inherited.OutlinePatternSeed == refreshedSeed,
                    "Fragments must inherit the source's active perimeter seed rather than start a new randomized scan.");
                ScannableInteractable snapshot = f.Target(Vector2.up * 4f);
                snapshot.InheritScanRemaining(target.RevealRemaining, target.RevealElapsed,
                    target.HasCapturedScanOrientation, target.CapturedScanWorldAngleDegrees, refreshedSeed);
                Require(snapshot.OutlinePatternSeed == refreshedSeed,
                    "Captured inheritance snapshots must preserve the perimeter seed after their source retires.");
            }
            Require(globalRandom.Equals(UnityEngine.Random.state),
                "Pattern generation, real scans and inheritance must not consume or reset Unity's shared gameplay Random state.");
        }

        private static void CheckOutlineDotOverlay()
        {
            using (var f = new Fixture())
            {
                f.AlignOnScan = true;
                ScannableInteractable target = f.Target(new Vector2(3f, 0f));
                ConfigureAsymmetricTarget(target);
                var overlay = f.Create("Diamond-point area overlay").AddComponent<BioScanStaticAreaUI>();
                overlay.Initialize(f.View, f.Camera);
                f.Camera.transform.rotation = Quaternion.Euler(0f, 0f, 19f);
                overlay.ShowScannedInteractable(target);
                List<Vector2> hatchOnly = CaptureScreenGeometry(f, overlay);
                List<Vector3> sourceHatchBeforeOutline = CaptureCachedWorldCenterlines(overlay, target);
                f.ShowOutlineDots = true;
                List<Vector2> clippedHatch = CaptureScreenGeometry(f, overlay);
                RequireCenterlinesEqual(sourceHatchBeforeOutline, CaptureCachedWorldCenterlines(overlay, target),
                    "Enabling the point boundary must retain the source hatch rows while clipping their visible endpoints.");
                Require(GeometryDiffers(hatchOnly, clippedHatch),
                    "The inset diamond boundary must trim source hatching that formerly protruded outside its point hull.");
                AssertRenderedHatchInsideDots(f, overlay, target);
                var outlineAlphas = new List<byte>();
                List<Vector2> outlineScreen = CaptureOutlineGeometry(f, overlay, outlineAlphas);
                Require(outlineScreen.Count >= 12,
                    "A revealed footprint must render a separate solid-diamond perimeter layer.");
                IList hatchGraphics = (IList)Get(overlay, "areaGraphics");
                IList outlineGraphics = (IList)Get(overlay, "outlineGraphics");
                var outlineGraphic = (BioScanStaticAreaGraphic)outlineGraphics[0];
                Require(!outlineGraphic.maskable && !outlineGraphic.raycastTarget,
                    "The diamond perimeter must remain outside circular UI clipping and must not intercept input.");
                Require(((BioScanStaticAreaGraphic)hatchGraphics[hatchGraphics.Count - 1]).transform.GetSiblingIndex()
                    < outlineGraphic.transform.GetSiblingIndex(),
                    "The separate perimeter batches must draw after the interior hatch layer.");
                Vector2 projectedCenter = f.Camera.WorldToScreenPoint(target.transform.position);
                Require(Vector2.Distance(projectedCenter, f.View.GetUiCenterScreenPoint())
                    > f.View.GetUiRingScreenRadiusPixels(),
                    "The perimeter fixture must exercise a target outside the player's circular display.");
                foreach (byte alpha in outlineAlphas)
                    Require(alpha == 0, "The outline must start transparent together with the hatch entrance.");
                List<Vector3> baselineOutline = CaptureOutlineWorldPoints(f, overlay);
                uint baselineOutlineSeed = target.OutlinePatternSeed;
                Require(baselineOutlineSeed != 0,
                    "A visible randomized perimeter must own its current scan's nonzero seed.");
                var rawFootprint = new List<Vector2>();
                Require(target.TryGetPresentationFootprint(rawFootprint, out Vector3 outlineCenter),
                    "The perimeter must use the actual target footprint.");
                float referenceScale = (float)Get(overlay, "referencePixelsPerWorldUnit");
                for (int index = 0; index < rawFootprint.Count; index++) rawFootprint[index] *= referenceScale;
                var expectedOutline = new List<Vector2>();
                Require(BioScanStaticOutlinePattern.BuildDots(rawFootprint,
                    target.UseSmallAreaFill ? target.SmallOutlineDotSpacingPixels : target.OutlineDotSpacingPixels,
                    expectedOutline, target.OutlineDotSizePixels, target.OutlinePatternSeed),
                    "The overlay fixture must have a valid diamond-point perimeter at its fixed reference scale.");
                var expectedWorld = new List<Vector3>();
                foreach (Vector2 point in expectedOutline)
                    expectedWorld.Add(outlineCenter + (Vector3)(point / referenceScale));
                RequireWorldPointsEqual(expectedWorld, baselineOutline,
                    "The overlay must render every expected inset diamond center at its actual world position.");
                List<Vector3> baselineHatch = CaptureCachedWorldCenterlines(overlay, target);
                for (int step = 0; step < 4; step++)
                {
                    Tick(target, target.FadeInDuration * .25f);
                    AssertOutlineAndHatchOpacity(f, overlay, target);
                    RequireWorldPointsEqual(baselineOutline, CaptureOutlineWorldPoints(f, overlay),
                        "Entrance animation must not move perimeter diamond centers.");
                }
                Tick(target, target.OutlineBreathingPeriod - target.FadeInDuration);
                byte peak = AssertOutlineAndHatchOpacity(f, overlay, target);
                Tick(target, target.OutlineBreathingPeriod * .5f);
                byte trough = AssertOutlineAndHatchOpacity(f, overlay, target);
                Require(trough < peak,
                    "The perimeter must retain its own gentle breathing rather than inherit the stronger hatch pulse.");
                float previousTimeScale = Time.timeScale;
                try
                {
                    Time.timeScale = 0f;
                    Tick(target, 1f);
                    Require(AssertOutlineAndHatchOpacity(f, overlay, target) == trough,
                        "Pausing simulation must freeze the perimeter's independent breathing clock.");
                }
                finally { Time.timeScale = previousTimeScale; }
                for (int frame = 0; frame <= 24; frame++)
                {
                    float phase = frame / 24f;
                    f.Camera.transform.SetPositionAndRotation(new Vector3(
                        Mathf.Sin(phase * 5f) * .2f, Mathf.Cos(phase * 4f) * .1f, -10f),
                        Quaternion.Euler(0f, 0f, phase * 270f));
                    f.Camera.orthographicSize = Mathf.Lerp(5f, 8f, phase);
                    RequireWorldPointsEqual(baselineOutline, CaptureOutlineWorldPoints(f, overlay),
                        "Camera movement, turning and zoom must only project the same cached perimeter centers.");
                    AssertDiamondGeometry(CaptureOutlineGeometry(f, overlay),
                        ProjectedDiamondDirection(f.Camera), target.OutlineDotSizePixels);
                    Require(target.OutlinePatternSeed == baselineOutlineSeed,
                        "Camera movement and breathing must not randomize the perimeter again.");
                    RequireCenterlinesEqual(baselineHatch, CaptureCachedWorldCenterlines(overlay, target),
                        "Moving the camera must preserve source hatch rows while continuously clipping only their visible endpoints.");
                    AssertRenderedHatchInsideDots(f, overlay, target);
                }
                overlay.ShowScannedInteractable(target);
                uint refreshedOutlineSeed = target.OutlinePatternSeed;
                Require(refreshedOutlineSeed != 0 && refreshedOutlineSeed != baselineOutlineSeed,
                    "A real rescan must assign one fresh seed for its replacement perimeter.");
                f.Camera.transform.SetPositionAndRotation(new Vector3(0f, 0f, -10f),
                    Quaternion.Euler(0f, 0f, -51f));
                f.Camera.orthographicSize = 5f;
                List<Vector3> refreshedOutline = CaptureOutlineWorldPoints(f, overlay);
                Require(!WorldPointsMatch(baselineOutline, refreshedOutline),
                    "A new scan round must visibly replace its perimeter point arrangement rather than retain the previous phase.");
                for (int frame = 0; frame < 3; frame++)
                {
                    RequireWorldPointsEqual(refreshedOutline, CaptureOutlineWorldPoints(f, overlay),
                        "Presenting the refreshed scan repeatedly must reuse its one generated perimeter pattern.");
                    Require(target.OutlinePatternSeed == refreshedOutlineSeed,
                        "Rendering a refreshed scan must not consume additional perimeter seeds.");
                }
                RequireAngleNear(target.CapturedScanWorldAngleDegrees, -51f + target.LineAngleDegrees,
                    "The perimeter layer must not interfere with the new scan's hatch-direction capture.");
                List<Vector2> rescannedHatch = CaptureScreenGeometry(f, overlay);
                List<Vector3> rescannedSource = CaptureCachedWorldCenterlines(overlay, target);
                List<Vector2> unclippedExpected = ProjectCachedHatchRectangles(f, overlay, target);
                f.ShowOutlineDots = false;
                Require(CaptureOutlineGeometry(f, overlay, null, false).Count == 0,
                    "Disabling the perimeter must immediately clear its pooled draw geometry.");
                RequireGeometryEqual(unclippedExpected, CaptureScreenGeometry(f, overlay),
                    "Disabling the point boundary must restore the original complete source hatch geometry.");
                f.ShowOutlineDots = true;
                RequireWorldPointsEqual(refreshedOutline, CaptureOutlineWorldPoints(f, overlay),
                    "Re-enabling the perimeter must restore the same cached world point phase.");
                RequireGeometryEqual(rescannedHatch, CaptureScreenGeometry(f, overlay),
                    "Restoring the same point boundary must reproduce its clipped hatch without changing source rows.");
                Call(overlay, "OnDisable");
                Call(overlay, "OnEnable");
                RequireWorldPointsEqual(refreshedOutline, CaptureOutlineWorldPoints(f, overlay),
                    "Disabling and re-enabling the presenter must preserve the active scan's perimeter seed and geometry.");
                Object.DestroyImmediate(overlay.gameObject);
                overlay = f.Create("Rebuilt randomized perimeter overlay").AddComponent<BioScanStaticAreaUI>();
                overlay.Initialize(f.View, f.Camera);
                RequireWorldPointsEqual(refreshedOutline, CaptureOutlineWorldPoints(f, overlay),
                    "Rebuilding the entire presenter must reproduce the same active seeded perimeter.");
                Require(target.OutlinePatternSeed == refreshedOutlineSeed,
                    "Presenter reconstruction must not turn an existing reveal into another randomized scan.");
                Set(target, "outlineDotSizePixels", 4.2f);
                Set(target, "outlineDotSpacingPixels", 15f);
                Set(target, "smallOutlineDotSpacingPixels", 10f);
                CaptureScreenGeometry(f, overlay);
                RequireCenterlinesEqual(rescannedSource, CaptureCachedWorldCenterlines(overlay, target),
                    "Changing dot styling must keep the original hatch rows while updating their visible boundary clipping.");
                AssertRenderedHatchInsideDots(f, overlay, target);
                AssertOutlineAndHatchOpacity(f, overlay, target);
                RequireWorldPointsEqual(ExpectedWorldOutlineDotPoints(target, referenceScale),
                    CaptureOutlineWorldPoints(f, overlay),
                    "Changing diamond size and spacing must refresh the cached point positions using the new style.");
                Tick(target, target.DisplayDuration - target.FadeOutDuration * .5f);
                Require(AssertOutlineAndHatchOpacity(f, overlay, target)
                    < Mathf.RoundToInt(target.OutlineBreathingMinAlpha * 255f),
                    "The perimeter must share the final expiry envelope below its own normal breathing range.");
                Tick(target, target.FadeOutDuration);
                Require(CaptureOutlineGeometry(f, overlay, null, false).Count == 0,
                    "An expired scan must immediately clear the perimeter's pooled geometry.");
            }
            using (var f = new Fixture())
            {
                ConfigureNormalGarbageView(f);
                f.AlignOnScan = true;
                f.ShowOutlineDots = true;
                var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                    PrefabDirectory + "Small_Garbage.prefab"));
                SceneManager.MoveGameObjectToScene(instance, f.Scene);
                instance.transform.position = Vector3.zero;
                ScannableInteractable target = instance.GetComponent<ScannableInteractable>();
                Call(target, "OnEnable");
                var overlay = f.Create("Small-garbage diamond boundary overlay").AddComponent<BioScanStaticAreaUI>();
                overlay.Initialize(f.View, f.Camera);
                overlay.ShowScannedInteractable(target);
                List<Vector3> smallPoints = CaptureOutlineWorldPoints(f, overlay);
                Require(smallPoints.Count >= 3,
                    "Actual small garbage must retain several distinguishable boundary diamonds.");
                float referenceScale = (float)Get(overlay, "referencePixelsPerWorldUnit");
                RequireWorldPointsEqual(ExpectedWorldOutlineDotPoints(target, referenceScale), smallPoints,
                    "Actual small garbage must use the denser small-boundary spacing.");
                AssertRenderedHatchInsideDots(f, overlay, target);
                Set(target, "useSmallAreaFill", false);
                RequireWorldPointsEqual(ExpectedWorldOutlineDotPoints(target, referenceScale),
                    CaptureOutlineWorldPoints(f, overlay),
                    "Changing the small-area mode must refresh the boundary using normal-object point spacing.");
                Set(target, "useSmallAreaFill", true);
                RequireWorldPointsEqual(smallPoints, CaptureOutlineWorldPoints(f, overlay),
                    "Restoring small-area mode must reproduce its existing seeded diamond positions.");
                AssertDiamondGeometry(CaptureOutlineGeometry(f, overlay),
                    ProjectedDiamondDirection(f.Camera), target.OutlineDotSizePixels);
                List<Vector3> smallSource = CaptureCachedWorldCenterlines(overlay, target);
                for (int frame = 0; frame <= 12; frame++)
                {
                    float phase = frame / 12f;
                    f.Camera.transform.SetPositionAndRotation(new Vector3(Mathf.Sin(frame) * .01f,
                        Mathf.Cos(frame) * .007f, -10f), Quaternion.Euler(0f, 0f, phase * 180f));
                    f.Camera.orthographicSize = Mathf.Lerp(9f, 14f, phase);
                    AssertRenderedHatchInsideDots(f, overlay, target, true);
                    RequireCenterlinesEqual(smallSource, CaptureCachedWorldCenterlines(overlay, target),
                        "Small-garbage clipping during scan zoom must never regenerate the cached source rows.");
                    RequireWorldPointsEqual(smallPoints, CaptureOutlineWorldPoints(f, overlay),
                        "Small-garbage zoom must retain the same enclosing diamond centers.");
                }
            }
        }

        private static List<Vector2> CaptureOutlineGeometry(Fixture fixture, BioScanStaticAreaUI overlay,
            List<byte> barAlphas = null, bool expectVisible = true)
        {
            fixture.UpdateOverlay(overlay);
            var geometry = new List<Vector2>();
            foreach (BioScanStaticAreaGraphic graphic in (IList)Get(overlay, "outlineGraphics"))
            {
                if (graphic == null) continue;
                using (var mesh = new VertexHelper())
                {
                    Call(graphic, "OnPopulateMesh", mesh);
                    Require(mesh.currentVertCount % 4 == 0,
                        "Each perimeter batch must consist entirely of complete four-corner diamond glyphs.");
                    for (int index = 0; index < mesh.currentVertCount; index++)
                    {
                        UIVertex vertex = default;
                        mesh.PopulateUIVertex(ref vertex, index);
                        geometry.Add(new Vector2(vertex.uv1.z, vertex.uv1.w));
                        if (index % 4 == 0 && barAlphas != null) barAlphas.Add(vertex.color.a);
                    }
                }
            }
            Require(!expectVisible || geometry.Count >= 4,
                "An enabled readable perimeter must produce complete diamond glyph geometry.");
            return geometry;
        }

        private static List<Vector3> CaptureOutlineWorldPoints(Fixture fixture, BioScanStaticAreaUI overlay)
        {
            var result = new List<Vector3>();
            float depth = fixture.Camera.WorldToScreenPoint(Vector3.zero).z;
            List<Vector2> geometry = CaptureOutlineGeometry(fixture, overlay);
            for (int index = 0; index < geometry.Count; index += 4)
            {
                Vector2 center = (geometry[index] + geometry[index + 1]
                    + geometry[index + 2] + geometry[index + 3]) * .25f;
                result.Add(fixture.Camera.ScreenToWorldPoint(new Vector3(center.x, center.y, depth)));
            }
            return result;
        }

        private static void AssertRenderedHatchInsideDots(Fixture fixture, BioScanStaticAreaUI overlay,
            ScannableInteractable target, bool allowEmpty = false)
        {
            List<Vector2> outline = CaptureOutlineGeometry(fixture, overlay);
            var centers = new List<Vector2>(outline.Count / 4);
            for (int index = 0; index < outline.Count; index += 4)
                centers.Add((outline[index] + outline[index + 1] + outline[index + 2] + outline[index + 3]) * .25f);
            var boundary = new List<Vector2>();
            bool valid = BioScanStaticLinePattern.BuildPointBoundaryHull(centers, boundary);
            List<Vector2> geometry = CaptureScreenGeometry(fixture, overlay, null, false);
            if (!valid)
            {
                Require(geometry.Count == 0,
                    "An insufficient actual point hull must not render interior hatching.");
                return;
            }
            Require(allowEmpty || geometry.Count >= 4,
                "A readable diamond boundary must retain complete interior hatch bars. "
                    + (geometry.Count == 0 ? DescribeEmptyPointBoundary(fixture, overlay, target) : string.Empty));
            if (geometry.Count == 0) return;
            AssertQuadClearance(geometry, boundary, 1f,
                "Rendered hatching must keep its complete stroke and antialias rectangle one pixel inside the actual projected diamond-center hull.");
            for (int index = 0; index < geometry.Count; index += 4)
            {
                Vector2 width = geometry[index + 1] - geometry[index];
                Vector2 length = geometry[index + 2] - geometry[index + 1];
                RequireNear(width.magnitude, target.LineWidthPixels + 2f,
                    "Boundary clipping must preserve the fixed visible line width and full antialias margin.");
                Require(Mathf.Abs(Vector2.Dot(width.normalized, length.normalized)) < .0001f
                    && length.magnitude - 2f >= .23f,
                    "Boundary clipping must produce complete perpendicular caps, never mask-cut fringe fragments.");
            }
            AssertSegmentsOnlyShorten(ScreenCenterlines(ProjectCachedHatchRectangles(fixture, overlay, target)),
                ScreenCenterlines(geometry));
        }

        private static string DescribeEmptyPointBoundary(Fixture fixture, BioScanStaticAreaUI overlay,
            ScannableInteractable target)
        {
            object layout = ((IDictionary)Get(overlay, "layouts"))[target];
            var projected = (List<StaticScanLineSegment>)Get(layout, "Segments");
            var boundary = (List<Vector2>)Get(layout, "PointBoundary");
            var points = (List<Vector2>)Get(layout, "OutlinePoints");
            var worldHull = (List<Vector2>)Get(layout, "WorldPointBoundary");
            var clipped = new List<StaticScanLineSegment>();
            bool uiClip = BioScanStaticLinePattern.ClipToBoundary(projected, boundary, clipped, target.LineWidthPixels);
            int uiClipCount = clipped.Count;
            var rebuilt = new List<Vector2>();
            bool rebuiltHull = BioScanStaticLinePattern.BuildPointBoundaryHull(points, rebuilt);
            bool rebuiltClip = BioScanStaticLinePattern.ClipToBoundary(projected, rebuilt, clipped, target.LineWidthPixels);
            var report = new System.Text.StringBuilder();
            report.Append("target=").Append(target.transform.position.ToString("R"))
                .Append(" camera=").Append(fixture.Camera.transform.position.ToString("R"))
                .Append(" yaw=").Append(fixture.Camera.transform.eulerAngles.z.ToString("R"))
                .Append(" zoom=").Append(fixture.Camera.orthographicSize.ToString("R"))
                .Append(" viewport=").Append(fixture.Camera.pixelRect)
                .Append(" refPPU=").Append(Get(overlay, "referencePixelsPerWorldUnit"))
                .Append(" source=").Append(projected.Count).Append(" dots=").Append(points.Count)
                .Append(" worldHull=").Append(worldHull.Count).Append(" screenHull=").Append(boundary.Count)
                .Append(" UIClip=").Append(uiClip).Append('/').Append(uiClipCount)
                .Append(" rebuiltHull=").Append(rebuiltHull).Append('/').Append(rebuilt.Count)
                .Append(" rebuiltClip=").Append(rebuiltClip).Append('/').Append(clipped.Count)
                .Append(" visibleAreas=").Append(((IList)Get(overlay, "visibleAreas")).Count)
                .Append("; PointBoundary vertices/turns:");
            for (int index = 0; index < boundary.Count; index++)
            {
                Vector2 a = boundary[(index + boundary.Count - 1) % boundary.Count];
                Vector2 b = boundary[index], c = boundary[(index + 1) % boundary.Count];
                double firstX = (double)b.x - a.x, firstY = (double)b.y - a.y;
                double secondX = (double)c.x - b.x, secondY = (double)c.y - b.y;
                double turn = firstX * secondY - firstY * secondX;
                double product = Math.Sqrt((firstX * firstX + firstY * firstY)
                    * (secondX * secondX + secondY * secondY));
                report.Append(" [").Append(index).Append(':').Append(b.ToString("R"))
                    .Append(" turn=").Append(turn.ToString("R"))
                    .Append(" normalized=").Append((product > 0d ? turn / product : 0d).ToString("R"))
                    .Append(" tolerance=").Append((product * 1e-10d).ToString("R")).Append(']');
            }
            return report.ToString();
        }

        private static List<Vector2> ProjectCachedHatchRectangles(Fixture fixture, BioScanStaticAreaUI overlay,
            ScannableInteractable target)
        {
            List<Vector3> world = CaptureCachedWorldCenterlines(overlay, target);
            var projected = new List<StaticScanLineSegment>(world.Count / 2);
            for (int index = 0; index < world.Count; index += 2)
                projected.Add(new StaticScanLineSegment(fixture.Camera.WorldToScreenPoint(world[index]),
                    fixture.Camera.WorldToScreenPoint(world[index + 1])));
            return SegmentRectangles(projected, target.LineWidthPixels);
        }

        private static void AssertQuadClearance(List<Vector2> geometry, List<Vector2> boundary,
            float clearance, string message)
        {
            Require(boundary.Count >= 3, message + " A filled point hull is required.");
            double twiceArea = 0d;
            for (int index = 0; index < boundary.Count; index++)
            {
                Vector2 a = boundary[index], b = boundary[(index + 1) % boundary.Count];
                twiceArea += (double)a.x * b.y - (double)a.y * b.x;
            }
            double winding = twiceArea >= 0d ? 1d : -1d;
            foreach (Vector2 corner in geometry)
                for (int index = 0; index < boundary.Count; index++)
                {
                    Vector2 a = boundary[index], b = boundary[(index + 1) % boundary.Count];
                    double edgeX = (double)b.x - a.x, edgeY = (double)b.y - a.y;
                    double length = Math.Sqrt(edgeX * edgeX + edgeY * edgeY);
                    if (length < .00001d) continue;
                    double distance = winding * (edgeX * ((double)corner.y - a.y)
                        - edgeY * ((double)corner.x - a.x)) / length;
                    Require(distance >= clearance - .02d,
                        message + " Edge clearance: " + distance + ".");
                }
        }

        private static void AssertSegmentsOnlyShorten(List<StaticScanLineSegment> source,
            List<StaticScanLineSegment> clipped)
        {
            Require(clipped.Count <= source.Count,
                "Clipping a convex point boundary must not add rows or split one source row into fragments.");
            foreach (StaticScanLineSegment piece in clipped)
            {
                bool found = false;
                foreach (StaticScanLineSegment original in source)
                {
                    Vector2 delta = original.End - original.Start;
                    float length = delta.magnitude;
                    if (length < .0001f) continue;
                    Vector2 direction = delta / length, normal = new Vector2(-direction.y, direction.x);
                    float start = Vector2.Dot(piece.Start - original.Start, direction);
                    float end = Vector2.Dot(piece.End - original.Start, direction);
                    if (Mathf.Abs(Vector2.Dot(piece.Start - original.Start, normal)) > .02f
                        || Mathf.Abs(Vector2.Dot(piece.End - original.Start, normal)) > .02f
                        || start < -.02f || end > length + .02f || end <= start) continue;
                    found = true;
                    break;
                }
                Require(found,
                    "Every visible clipped bar must be a shortened interval on an existing source row, with no new direction or phase.");
            }
        }

        private static Vector2 ProjectedDiamondDirection(Camera camera)
        {
            Vector2 origin = camera.WorldToScreenPoint(Vector3.zero);
            Vector2 diagonal = camera.WorldToScreenPoint(new Vector3(1f, 1f, 0f));
            return (diagonal - origin).normalized;
        }

        private static void AssertDiamondGeometry(List<Vector2> geometry, Vector2 direction, float dotSize)
        {
            Require(geometry.Count >= 4 && geometry.Count % 4 == 0,
                "Boundary glyphs must consist of complete four-corner diamonds.");
            float side = dotSize / Mathf.Sqrt(2f);
            List<Vector2> visible = VisibleBarRectangles(geometry);
            for (int index = 0; index < geometry.Count; index += 4)
            {
                Vector2 width = geometry[index + 1] - geometry[index];
                Vector2 length = geometry[index + 2] - geometry[index + 1];
                RequireNear(width.magnitude, side + 2f,
                    "A diamond's screen-pixel side must retain the authored diagonal size plus its antialias margin.");
                RequireNear(length.magnitude, side + 2f,
                    "Boundary points must be squares rather than short elongated dashes.");
                Require(Mathf.Abs(Vector2.Dot(width.normalized, length.normalized)) < .0001f,
                    "Each boundary point must retain four perpendicular square edges.");
                Require(Vector2.Dot(length.normalized, direction.normalized) > .9999f,
                    "The diamond glyph must follow the projected world diagonal rather than billboard its angle to the HUD.");
                RequireNear(Vector2.Distance(visible[index], visible[index + 2]), dotSize,
                    "A boundary diamond's visible diagonal must remain fixed in screen pixels through zoom.");
            }
        }

        private static List<Vector3> ExpectedWorldOutlineDotPoints(ScannableInteractable target, float referenceScale)
        {
            var footprint = new List<Vector2>();
            Require(target.TryGetPresentationFootprint(footprint, out Vector3 center),
                "The diamond boundary must use its target's actual footprint.");
            for (int index = 0; index < footprint.Count; index++) footprint[index] *= referenceScale;
            var points = new List<Vector2>();
            Require(BioScanStaticOutlinePattern.BuildDots(footprint,
                target.UseSmallAreaFill ? target.SmallOutlineDotSpacingPixels : target.OutlineDotSpacingPixels,
                points, target.OutlineDotSizePixels, target.OutlinePatternSeed),
                "The production footprint must support its configured diamond boundary.");
            var world = new List<Vector3>(points.Count);
            foreach (Vector2 point in points) world.Add(center + (Vector3)(point / referenceScale));
            return world;
        }

        private static List<Vector3> ToWorldPoints(List<Vector2> points)
        {
            var result = new List<Vector3>(points.Count);
            foreach (Vector2 point in points) result.Add(point);
            return result;
        }

        private static void RequireWorldPointsEqual(List<Vector3> expected, List<Vector3> actual, string message)
        {
            Require(WorldPointsMatch(expected, actual), message + " Point count or world center changed.");
        }

        private static bool WorldPointsMatch(List<Vector3> expected, List<Vector3> actual)
        {
            if (expected.Count != actual.Count) return false;
            var consumed = new bool[actual.Count];
            foreach (Vector3 point in expected)
            {
                bool found = false;
                for (int index = 0; index < actual.Count; index++)
                {
                    if (consumed[index] || Vector3.Distance(point, actual[index]) >= .0005f) continue;
                    consumed[index] = true;
                    found = true;
                    break;
                }
                if (!found) return false;
            }
            return true;
        }

        private static byte AssertOutlineAndHatchOpacity(Fixture fixture, BioScanStaticAreaUI overlay,
            ScannableInteractable target)
        {
            var hatchAlphas = new List<byte>();
            var outlineAlphas = new List<byte>();
            CaptureScreenGeometry(fixture, overlay, hatchAlphas);
            List<Vector2> outline = CaptureOutlineGeometry(fixture, overlay, outlineAlphas);
            byte expectedHatch = ((Color32)AreaTint(target)).a;
            byte expectedOutline = ((Color32)OutlineTint(target)).a;
            float envelope = ExpectedRevealEnvelope(target);
            RequireNear(AreaTint(target).a, target.AreaColor.a * envelope * ExpectedHatchPulse(target),
                "Hatch alpha must combine its own short pulse with the shared reveal envelope.");
            RequireNear(OutlineTint(target).a, target.AreaColor.a * envelope * ExpectedOutlineBreath(target),
                "Perimeter alpha must combine its independent gentle breathing with the shared reveal envelope.");
            foreach (byte alpha in hatchAlphas)
                Require(alpha == expectedHatch, "Hatch geometry must carry the authored reveal/pulse alpha.");
            foreach (byte alpha in outlineAlphas)
                Require(alpha == expectedOutline, "Perimeter diamonds must carry their independent breathing intensity on the shared reveal clock.");
            AssertDiamondGeometry(outline, ProjectedDiamondDirection(fixture.Camera), target.OutlineDotSizePixels);
            return expectedOutline;
        }

        private static void CheckScanCapturedDirection()
        {
            using (var f = new Fixture())
            {
                f.AlignOnScan = true;
                var overlay = f.Create("Scan-time world-direction overlay").AddComponent<BioScanStaticAreaUI>();
                Require((bool)Get(overlay, "alignOnScan"),
                    "The production overlay must align the scan direction once by default.");
                overlay.Initialize(f.View, f.Camera);
                ScannableInteractable target = f.Target(new Vector2(2f, 0f));
                ConfigureAsymmetricTarget(target);
                Require(!target.HasCapturedScanOrientation && !target.IsScanOrientationPending,
                    "An unscanned static area must not own a stale scan direction.");
                f.Camera.transform.rotation = Quaternion.Euler(0f, 0f, 7f);
                overlay.ShowScannedInteractable(target);
                Require(target.IsScanOrientationPending && !target.HasCapturedScanOrientation,
                    "Reveal must request orientation capture without sampling a camera before its final frame update.");
                uint firstRevision = target.ScanOrientationRevision;
                f.Camera.transform.rotation = Quaternion.Euler(0f, 0f, 31f);
                List<Vector2> initial = CaptureLocalGeometry(f, overlay, target);
                Require(target.HasCapturedScanOrientation && !target.IsScanOrientationPending,
                    "The presentation update must consume a pending direction request exactly once.");
                RequireAngleNear(target.CapturedScanWorldAngleDegrees, 31f + target.LineAngleDegrees,
                    "Capture must use the final camera orientation from the presentation frame.");
                AssertScreenLineAngle(initial, target.LineAngleDegrees,
                    "A newly captured scan must appear at the authored slash angle on screen.");
                AssertBarsInsideProjectedFootprint(initial, f, target);
                float firstWorldAngle = target.CapturedScanWorldAngleDegrees;
                Require(!target.CaptureScanOrientation(127f),
                    "A captured reveal must reject further orientation captures until another real scan.");
                RequireAngleNear(target.CapturedScanWorldAngleDegrees, firstWorldAngle,
                    "A rejected capture must preserve the scan's locked world direction.");
                List<Vector3> baselineWorld = CaptureWorldCenterlines(f, overlay);
                foreach (float cameraYaw in new[] { 0f, 45f, 90f, -35f, 179f, 359.9f, 360f })
                {
                    f.Camera.transform.rotation = Quaternion.Euler(0f, 0f, cameraYaw);
                    List<Vector2> geometry = CaptureLocalGeometry(f, overlay, target);
                    AssertScreenLineAngle(geometry, firstWorldAngle - cameraYaw,
                        "Turning after a scan must project its locked world direction rather than realign the slash to the screen.");
                    RequireCenterlinesEqual(baselineWorld, CaptureWorldCenterlines(f, overlay),
                        "Camera turns must preserve all complete world endpoints from the captured scan.");
                    AssertBarsInsideProjectedFootprint(geometry, f, target);
                    Require(target.ScanOrientationRevision == firstRevision && !target.IsScanOrientationPending,
                        "Camera turns must not silently begin another scan-direction revision.");
                }
                for (int frame = 0; frame <= 24; frame++)
                {
                    float phase = frame / 24f;
                    f.Camera.transform.SetPositionAndRotation(new Vector3(
                        Mathf.Sin(phase * 6f) * .4f, Mathf.Cos(phase * 5f) * .25f, -10f),
                        Quaternion.Euler(0f, 0f, phase * 270f));
                    f.Camera.orthographicSize = frame <= 12
                        ? Mathf.Lerp(5f, 8f, frame / 12f)
                        : Mathf.Lerp(8f, 3f, (frame - 12) / 12f);
                    RequireCenterlinesEqual(baselineWorld, CaptureWorldCenterlines(f, overlay),
                        "Movement, turning and scan zoom must only project the captured endpoints, never regenerate them.");
                }
                Tick(target, 3f);
                float elapsedBeforeRescan = target.RevealElapsed;
                overlay.ShowScannedInteractable(target);
                Require(target.IsScanOrientationPending && target.ScanOrientationRevision != firstRevision,
                    "A subsequent real scan must request a fresh locked world direction.");
                RequireNear(target.RevealElapsed, elapsedBeforeRescan,
                    "Requesting another direction must preserve an already-visible scan's breathing phase.");
                f.Camera.transform.SetPositionAndRotation(new Vector3(0f, 0f, -10f),
                    Quaternion.Euler(0f, 0f, -58f));
                f.Camera.orthographicSize = 5f;
                List<Vector2> rescanned = CaptureLocalGeometry(f, overlay, target);
                RequireAngleNear(target.CapturedScanWorldAngleDegrees, -58f + target.LineAngleDegrees,
                    "The next scan must capture its own final camera angle rather than reuse the first scan's direction.");
                AssertScreenLineAngle(rescanned, target.LineAngleDegrees,
                    "A new scan must again align the slash once to the authored on-screen angle.");
                Require(Mathf.Abs(Mathf.DeltaAngle(firstWorldAngle, target.CapturedScanWorldAngleDegrees)) > 30f,
                    "The rescan fixture must actually exercise a materially different locked direction.");
                List<Vector3> rescannedWorld = CaptureWorldCenterlines(f, overlay);
                f.Camera.transform.rotation = Quaternion.Euler(0f, 0f, 82f);
                RequireCenterlinesEqual(rescannedWorld, CaptureWorldCenterlines(f, overlay),
                    "The refreshed scan must lock its replacement endpoints through later camera turns.");
            }
        }

        private static void CheckCapturedDirectionInheritanceAndRebuild()
        {
            using (var f = new Fixture())
            {
                f.AlignOnScan = true;
                var overlay = f.Create("Captured direction inheritance overlay").AddComponent<BioScanStaticAreaUI>();
                overlay.Initialize(f.View, f.Camera);
                ScannableInteractable source = f.Target(new Vector2(-2f, 0f));
                f.Camera.transform.rotation = Quaternion.Euler(0f, 0f, 23f);
                overlay.ShowScannedInteractable(source);
                f.UpdateOverlay(overlay);
                Tick(source, 3f);
                ScannableInteractable fragment = f.Target(new Vector2(2f, 0f));
                fragment.InheritScanFrom(source);
                Require(fragment.HasCapturedScanOrientation && !fragment.IsScanOrientationPending,
                    "A fragment must inherit the captured orientation without asking the camera for a new scan direction.");
                RequireAngleNear(fragment.CapturedScanWorldAngleDegrees, source.CapturedScanWorldAngleDegrees,
                    "Fragment inheritance must retain the source scan's locked world direction.");
                RequireNear(fragment.RevealElapsed, source.RevealElapsed,
                    "Captured direction inheritance must also preserve the source breathing phase.");
                f.Camera.transform.rotation = Quaternion.Euler(0f, 0f, -41f);
                f.UpdateOverlay(overlay);
                List<Vector3> sourceBefore = CaptureCachedWorldCenterlines(overlay, source);
                List<Vector3> fragmentBefore = CaptureCachedWorldCenterlines(overlay, fragment);
                RequireAngleNear(fragment.CapturedScanWorldAngleDegrees, 23f + fragment.LineAngleDegrees,
                    "A fragment first presented after a camera turn must retain the inherited scan direction.");
                Object.DestroyImmediate(overlay.gameObject);
                overlay = f.Create("Rebuilt captured direction overlay").AddComponent<BioScanStaticAreaUI>();
                overlay.Initialize(f.View, f.Camera);
                f.UpdateOverlay(overlay);
                RequireCenterlinesEqual(sourceBefore, CaptureCachedWorldCenterlines(overlay, source),
                    "Rebuilding the presentation must retain the source's captured world endpoints.");
                RequireCenterlinesEqual(fragmentBefore, CaptureCachedWorldCenterlines(overlay, fragment),
                    "Rebuilding the presentation must retain inherited world endpoints without a fake scan.");
                Require(!source.IsScanOrientationPending && !fragment.IsScanOrientationPending,
                    "Presentation reconstruction must not mark visible inherited scans as new scans.");

                float fragmentWorldAngle = fragment.CapturedScanWorldAngleDegrees;
                overlay.ShowScannedInteractable(source);
                f.Camera.transform.rotation = Quaternion.Euler(0f, 0f, 85f);
                f.UpdateOverlay(overlay);
                RequireAngleNear(source.CapturedScanWorldAngleDegrees, 85f + source.LineAngleDegrees,
                    "A source rescanned later must capture that scan's own camera orientation.");
                RequireAngleNear(fragment.CapturedScanWorldAngleDegrees, fragmentWorldAngle,
                    "Rescanning one target must not change a previously inherited target's independent locked direction.");
                RequireCenterlinesEqual(fragmentBefore, CaptureCachedWorldCenterlines(overlay, fragment),
                    "Targets scanned at different times must retain their own complete cached world endpoints.");
                overlay.ShowScannedInteractable(fragment);
                f.Camera.transform.rotation = Quaternion.Euler(0f, 0f, 359.9f);
                f.UpdateOverlay(overlay);
                RequireAngleNear(fragment.CapturedScanWorldAngleDegrees, 359.9f + fragment.LineAngleDegrees,
                    "Scan capture must handle camera yaw near the 360-degree wrap without reversing the slash.");
                RequireAngleNear(source.CapturedScanWorldAngleDegrees, 85f + source.LineAngleDegrees,
                    "A later fragment scan must not relock another target's earlier direction.");
            }
        }

        private static void CheckCapturedSmallAreaAndOverlap()
        {
            using (var f = new Fixture())
            {
                ConfigureNormalGarbageView(f);
                f.AlignOnScan = true;
                var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                    PrefabDirectory + "Small_Garbage.prefab"));
                SceneManager.MoveGameObjectToScene(instance, f.Scene);
                instance.transform.position = Vector3.zero;
                ScannableInteractable target = instance.GetComponent<ScannableInteractable>();
                Call(target, "OnEnable");
                var overlay = f.Create("Captured small-garbage scan overlay").AddComponent<BioScanStaticAreaUI>();
                overlay.Initialize(f.View, f.Camera);
                f.Camera.transform.rotation = Quaternion.Euler(0f, 0f, -12f);
                overlay.ShowScannedInteractable(target);
                List<Vector2> initial = CaptureLocalGeometry(f, overlay, target);
                Require(initial.Count / 4 >= 3 && initial.Count / 4 <= 5,
                    "A captured small-garbage scan must retain three to five readable complete rows at the normal reference scale.");
                AssertScreenLineAngle(initial, target.LineAngleDegrees,
                    "Small-garbage capture must initially use the same authored slash angle.");
                object layout = ((IDictionary)Get(overlay, "layouts"))[target];
                float frozenSpacing = (float)Get(layout, "ResolvedSmallSpacing");
                List<Vector3> baseline = CaptureWorldCenterlines(f, overlay);
                foreach (float cameraYaw in new[] { 0f, 45f, 90f, 135f, -35f, 359.9f })
                {
                    f.Camera.transform.rotation = Quaternion.Euler(0f, 0f, cameraYaw);
                    List<Vector2> geometry = CaptureLocalGeometry(f, overlay, target);
                    float screenAngle = target.CapturedScanWorldAngleDegrees - cameraYaw;
                    AssertScreenLineAngle(geometry, screenAngle,
                        "Small-garbage bars must remain locked to their scan's world direction after turning.");
                    AssertSmallFillGeometry(geometry, target,
                        new Vector2(Mathf.Cos(screenAngle * Mathf.Deg2Rad), Mathf.Sin(screenAngle * Mathf.Deg2Rad)), true);
                    AssertBarsInsideProjectedFootprint(geometry, f, target);
                    RequireCenterlinesEqual(baseline, CaptureWorldCenterlines(f, overlay),
                        "Small-garbage turns must preserve the original scan's full short-bar endpoints.");
                    RequireNear((float)Get(layout, "ResolvedSmallSpacing"), frozenSpacing,
                        "Camera turns must never recalculate a captured small target's density.");
                }
                for (int frame = 0; frame <= 16; frame++)
                {
                    f.Camera.transform.position = new Vector3(frame * .003f, Mathf.Sin(frame) * .01f, -10f);
                    f.Camera.orthographicSize = Mathf.Lerp(9f, 14f, frame / 16f);
                    RequireCenterlinesEqual(baseline, CaptureWorldCenterlines(f, overlay),
                        "Captured small-garbage endpoints must remain fixed through camera movement and zoom.");
                }
            }
            using (var f = new Fixture())
            {
                f.AlignOnScan = true;
                var overlay = f.Create("Overlapping captured-direction scans").AddComponent<BioScanStaticAreaUI>();
                overlay.Initialize(f.View, f.Camera);
                ScannableInteractable first = f.Target(new Vector2(2f, 0f));
                ScannableInteractable second = f.Target(new Vector2(2.2f, .15f));
                first.transform.localScale = second.transform.localScale = Vector3.one * 3f;
                f.Camera.transform.rotation = Quaternion.Euler(0f, 0f, 20f);
                overlay.ShowScannedInteractable(first);
                f.UpdateOverlay(overlay);
                f.Camera.transform.rotation = Quaternion.Euler(0f, 0f, 55f);
                overlay.ShowScannedInteractable(second);
                Tick(first, first.FadeInDuration + first.BreathingPeriod * .5f);
                Tick(second, second.FadeInDuration + second.BreathingPeriod);
                List<Vector2> combined = CaptureScreenGeometry(f, overlay);
                RequireAngleNear(first.CapturedScanWorldAngleDegrees, 20f + first.LineAngleDegrees,
                    "The first overlapping target must retain its earlier scan direction.");
                RequireAngleNear(second.CapturedScanWorldAngleDegrees, 55f + second.LineAngleDegrees,
                    "The second overlapping target must capture its own later scan direction.");
                AssertNonIntersectingBars(combined);
                foreach (StaticScanLineSegment segment in ScreenCenterlines(combined))
                {
                    Vector2 direction = (segment.End - segment.Start).normalized;
                    float firstAngle = (first.CapturedScanWorldAngleDegrees - 55f) * Mathf.Deg2Rad;
                    float secondAngle = (second.CapturedScanWorldAngleDegrees - 55f) * Mathf.Deg2Rad;
                    Require(Vector2.Dot(direction, new Vector2(Mathf.Cos(firstAngle), Mathf.Sin(firstAngle))) > .9999f
                        || Vector2.Dot(direction, new Vector2(Mathf.Cos(secondAngle), Mathf.Sin(secondAngle))) > .9999f,
                        "Overlap trimming must preserve each segment's source scan direction without inventing a third family.");
                }
                for (int frame = 0; frame < 4; frame++)
                {
                    Tick(first, first.BreathingPeriod * .25f);
                    Tick(second, second.BreathingPeriod * .25f);
                    RequireGeometryEqual(combined, CaptureScreenGeometry(f, overlay),
                        "Breathing crossings must not repartition overlapping areas with independently captured scan directions.");
                }
                List<Vector3> baseline = CaptureWorldCenterlines(f, overlay);
                foreach (float cameraYaw in new[] { 0f, 90f, -65f })
                {
                    f.Camera.transform.rotation = Quaternion.Euler(0f, 0f, cameraYaw);
                    AssertNonIntersectingBars(CaptureScreenGeometry(f, overlay));
                    RequireCenterlinesEqual(baseline, CaptureWorldCenterlines(f, overlay),
                        "Later camera turns must keep the independently captured overlap cut boundaries fixed in the scene.");
                }
            }
        }

        private static List<Vector3> CaptureCachedWorldCenterlines(BioScanStaticAreaUI overlay,
            ScannableInteractable target)
        {
            object layout = ((IDictionary)Get(overlay, "layouts"))[target];
            Require(layout != null, "A visible captured target must own a presentation layout.");
            var segments = (List<StaticScanLineSegment>)Get(layout, "WorldSegments");
            var outline = new List<Vector2>();
            Require(target.TryGetPresentationFootprint(outline, out Vector3 center),
                "The captured target must retain a valid physical presentation center.");
            var result = new List<Vector3>(segments.Count * 2);
            foreach (StaticScanLineSegment segment in segments)
            {
                result.Add(center + (Vector3)segment.Start);
                result.Add(center + (Vector3)segment.End);
            }
            return result;
        }

        private static void RequireAngleNear(float actual, float expected, string message) =>
            Require(Mathf.Abs(Mathf.DeltaAngle(actual, expected)) < .001f,
                message + " Actual: " + actual + ", expected: " + expected + ".");

        private static void AssertScreenLineAngle(List<Vector2> geometry, float angleDegrees, string message)
        {
            Require(geometry.Count >= 4 && geometry.Count % 4 == 0,
                message + " The area must contain complete four-corner bars.");
            float angle = angleDegrees * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            foreach (StaticScanLineSegment segment in ScreenCenterlines(geometry))
                Require(Vector2.Dot((segment.End - segment.Start).normalized, direction) > .9999f,
                    message + " Expected screen angle: " + angleDegrees + ".");
        }

        private static void CheckOverlayProjection()
        {
            using (var f = new Fixture())
            {
                var overlay = f.Create("Static scan overlay").AddComponent<BioScanStaticAreaUI>();
                overlay.Initialize(f.View, f.Camera);
                ScannableInteractable target = f.Target(new Vector2(2f, 0f));
                ConfigureAsymmetricTarget(target);
                overlay.ShowScannedInteractable(target);
                f.UpdateOverlay(overlay);
                Canvas canvas = (Canvas)Get(overlay, "areaCanvas");
                Require(canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay
                    && canvas.GetComponent<GraphicRaycaster>() == null
                    && canvas.GetComponentInParent<Mask>() == null
                    && canvas.GetComponentInParent<RectMask2D>() == null,
                    "Area highlights must use an independent overlay without view clipping or input interception.");
                Require((canvas.additionalShaderChannels & AdditionalCanvasShaderChannels.TexCoord1) != 0
                    && (canvas.additionalShaderChannels & AdditionalCanvasShaderChannels.TexCoord2) != 0,
                    "The overlay must transmit the authored bar width and viewport clipping coordinates to the shader.");
                Material material = (Material)Get(overlay, "areaMaterial");
                Require(material != null && material.HasProperty("_AbsoluteScreenPlacement")
                    && Mathf.Approximately(material.GetFloat("_AbsoluteScreenPlacement"), 1f),
                    "The runtime overlay must place its final GPU vertices in absolute screen pixels, without inheriting camera or Canvas rotation.");
                Vector2 screenCenter = f.Camera.WorldToScreenPoint(target.transform.position);
                Require(Vector2.Distance(screenCenter, f.View.GetUiCenterScreenPoint()) > f.View.GetUiRingScreenRadiusPixels(),
                    "The test target must be outside the player's circular view.");
                IList graphics = (IList)Get(overlay, "areaGraphics");
                Require(graphics.Count > 0, "An off-view target must create an area graphic.");
                var graphic = (BioScanStaticAreaGraphic)graphics[0];
                Require(!graphic.maskable && !graphic.raycastTarget,
                    "Static area graphics must remain visible outside the circle without intercepting clicks.");
                List<Vector2> baseline = CaptureLocalGeometry(f, overlay, target);
                List<Vector3> baselineCenterlines = CaptureWorldCenterlines(f, overlay);
                AssertParallelLineGeometry(baseline, target, ProjectedLineDirection(f.Camera, target),
                    ProjectedLatticeOrigin(f.Camera, target), true);
                AssertBarsInsideProjectedFootprint(baseline, f, target);

                // A world-fixed pattern must rotate on screen with the terrain.
                // Undo only the camera rotation to compare it with the original
                // world orientation; an angle locked to the HUD fails this.
                foreach (float rotation in new[] { 0f, 45f, 90f, 180f, 270f })
                {
                    f.Camera.transform.rotation = Quaternion.Euler(0f, 0f, rotation);
                    List<Vector2> rotated = CaptureLocalGeometry(f, overlay, target);
                    AssertParallelLineGeometry(rotated, target, ProjectedLineDirection(f.Camera, target),
                        ProjectedLatticeOrigin(f.Camera, target), true);
                    AssertBarsInsideProjectedFootprint(rotated, f, target);
                    RequireGeometryEqual(baseline, RotateGeometry(rotated, rotation),
                        "Undoing the camera rotation must restore the same world-fixed bars and physical footprint.");
                }

                // The scan zoom animates over many frames; comparing only its
                // endpoints misses topology/length jumps during the transition.
                float nearSpan = 0f, farSpan = 0f;
                foreach (float rotation in new[] { 0f, 63f, 180f })
                {
                    f.Camera.transform.rotation = Quaternion.Euler(0f, 0f, rotation);
                    for (int frame = 0; frame <= 24; frame++)
                    {
                        f.Camera.orthographicSize = frame <= 12
                            ? Mathf.Lerp(5f, 8f, frame / 12f)
                            : Mathf.Lerp(8f, 3f, (frame - 12) / 12f);
                        List<Vector2> geometry = CaptureLocalGeometry(f, overlay, target);
                        Vector2 direction = ProjectedLineDirection(f.Camera, target);
                        AssertParallelLineGeometry(geometry, target, direction,
                            ProjectedLatticeOrigin(f.Camera, target), true, 5f / f.Camera.orthographicSize);
                        RequireCenterlinesEqual(baselineCenterlines, CaptureWorldCenterlines(f, overlay),
                            "Every frame of scan zoom must retain the same world centerlines and row count; only their continuous projection may change.");
                        if (rotation == 0f && frame == 12) farSpan = GeometrySpan(geometry, direction);
                        if (rotation == 0f && frame == 24) nearSpan = GeometrySpan(geometry, direction);
                    }
                }
                Require(nearSpan > farSpan * 1.5f,
                    "The highlighted extent must follow the object's projected area through zoom rather than remain a fixed-size HUD badge.");

                f.Camera.transform.SetPositionAndRotation(new Vector3(.5f, -.25f, -10f),
                    Quaternion.Euler(0f, 0f, 37f));
                f.Camera.orthographicSize = 5f;
                target.transform.SetPositionAndRotation(new Vector3(2.3f, .3f),
                    Quaternion.Euler(0f, 0f, 71f));
                List<Vector2> moved = CaptureLocalGeometry(f, overlay, target);
                AssertParallelLineGeometry(moved, target, ProjectedLineDirection(f.Camera, target),
                    ProjectedLatticeOrigin(f.Camera, target), true);
                AssertBarsInsideProjectedFootprint(moved, f, target);
                Require(GeometryDiffers(baseline, RotateGeometry(moved, 37f)),
                    "Changing the target's actual rotation must update its footprint instead of retaining a canonical unrotated badge.");
                Tick(target, 3f);
                overlay.ShowScannedInteractable(target);
                RequireNear(target.RevealRemaining, 7f, "A repeated scan must refresh its lifetime.");
                RequireGeometryEqual(moved, CaptureLocalGeometry(f, overlay, target),
                    "Scanning the same target again must preserve its current world-oriented projection.");
                f.Camera.transform.SetPositionAndRotation(new Vector3(0f, 0f, -10f), Quaternion.identity);
                f.Camera.orthographicSize = 5f;
                target.transform.position = new Vector3(2f, 0f);
                target.transform.rotation = Quaternion.Euler(0f, 0f, 23f);
                RequireGeometryEqual(baseline, CaptureLocalGeometry(f, overlay, target),
                    "Restoring the camera must retain the original bar geometry.");

                foreach (float firstRevealZoom in new[] { 3f, 5f, 8f })
                {
                    using (var initial = new Fixture())
                    {
                        initial.Camera.orthographicSize = firstRevealZoom;
                        initial.Camera.transform.rotation = Quaternion.Euler(0f, 0f, 63f);
                        var initialOverlay = initial.Create("Initial zoom static scan overlay")
                            .AddComponent<BioScanStaticAreaUI>();
                        initialOverlay.Initialize(initial.View, initial.Camera);
                        ScannableInteractable initialTarget = initial.Target(new Vector2(2f, 0f));
                        ConfigureAsymmetricTarget(initialTarget);
                        initialOverlay.ShowScannedInteractable(initialTarget);
                        List<Vector2> initialGeometry = CaptureLocalGeometry(initial, initialOverlay, initialTarget);
                        AssertParallelLineGeometry(initialGeometry, initialTarget,
                            ProjectedLineDirection(initial.Camera, initialTarget),
                            ProjectedLatticeOrigin(initial.Camera, initialTarget), true, 5f / firstRevealZoom);
                        RequireCenterlinesEqual(baselineCenterlines,
                            CaptureWorldCenterlines(initial, initialOverlay),
                            "A first reveal during scan zoom must use the normal-view world line rows rather than rebuilding rows at the current camera scale.");
                        if (firstRevealZoom == 5f)
                        {
                            AssertBarsInsideProjectedFootprint(initialGeometry, initial, initialTarget);
                            RequireGeometryEqual(baseline, RotateGeometry(initialGeometry, 63f),
                                "A first scan must use the same physical world pattern at the same projection scale.");
                        }
                    }
                }

                using (var mesh = new VertexHelper())
                {
                    Rect viewport = f.Camera.pixelRect;
                    target.transform.position = f.Camera.ScreenToWorldPoint(
                        new Vector3(viewport.xMax + 20f, viewport.center.y, 10f));
                    Require(f.Camera.WorldToScreenPoint(target.transform.position).x > f.Camera.pixelRect.xMax,
                        "The partial-overlap test target's center must be outside the screen.");
                    f.UpdateOverlay(overlay);
                    Call(graphic, "OnPopulateMesh", mesh);
                    Require(mesh.currentVertCount >= 3,
                        "The visible part of an area must render even when its center has left the screen.");
                    bool hasOutsideCorner = false;
                    for (int index = 0; index < mesh.currentVertCount; index++)
                    {
                        UIVertex vertex = default;
                        mesh.PopulateUIVertex(ref vertex, index);
                        Vector2 projected = RectTransformUtility.WorldToScreenPoint(null,
                            graphic.rectTransform.TransformPoint(vertex.position));
                        hasOutsideCorner |= !viewport.Contains(projected);
                        RequireNear(vertex.uv2.x, viewport.xMin,
                            "Screen-edge clipping must use the actual camera viewport xmin.");
                        RequireNear(vertex.uv2.y, viewport.yMin,
                            "Screen-edge clipping must use the actual camera viewport ymin.");
                        RequireNear(vertex.uv2.z, viewport.xMax,
                            "Screen-edge clipping must use the actual camera viewport xmax.");
                        RequireNear(vertex.uv2.w, viewport.yMax,
                            "Screen-edge clipping must use the actual camera viewport ymax.");
                    }
                    Require(hasOutsideCorner,
                        "A partial screen overlap must retain complete bar geometry and let the viewport shader clip only the window edge.");

                    target.gameObject.SetActive(false);
                    Call(target, "OnDisable");
                    f.UpdateOverlay(overlay);
                    Call(graphic, "OnPopulateMesh", mesh);
                    Require(mesh.currentVertCount == 0, "Disabling a target must remove its overlay geometry.");
                    target.gameObject.SetActive(true);
                    Call(target, "OnEnable");
                    overlay.ShowScannedInteractable(target);
                    Tick(target, 7.1f);
                    f.UpdateOverlay(overlay);
                    Call(graphic, "OnPopulateMesh", mesh);
                    Require(mesh.currentVertCount == 0, "Expired scans must remove the highlight geometry.");
                    WorldInteraction interaction = target.gameObject.AddComponent<WorldInteraction>();
                    interaction.SetKind(WorldInteractionKind.Grabbable);
                    Call(target, "OnEnable");
                    overlay.ShowScannedInteractable(target);
                    interaction.SetKind(WorldInteractionKind.None);
                    f.UpdateOverlay(overlay);
                    Call(graphic, "OnPopulateMesh", mesh);
                    Require(mesh.currentVertCount == 0, "Recycled targets must immediately leave the overlay.");
                }
            }
        }

        private static void CheckContinuousCameraMotion()
        {
            using (var f = new Fixture())
            {
                var overlay = f.Create("Static world lines continuous camera motion")
                    .AddComponent<BioScanStaticAreaUI>();
                overlay.Initialize(f.View, f.Camera);
                ScannableInteractable target = f.Target(new Vector2(120f, 100f));
                ConfigureAsymmetricTarget(target);
                f.Camera.transform.SetPositionAndRotation(new Vector3(118f, 100f, -10f),
                    Quaternion.Euler(0f, 0f, 63f));
                overlay.ShowScannedInteractable(target);
                List<Vector3> baseline = CaptureWorldCenterlines(f, overlay);
                Require(baseline.Count > 16,
                    "The distant-world motion fixture must contain multiple long complete parallel bars.");

                // Floating-origin distance and tiny motion expose row-phase
                // rounding that coarse 90-degree endpoint tests can miss.
                for (int frame = 0; frame < 80; frame++)
                {
                    float phase = frame * .173f;
                    f.Camera.transform.SetPositionAndRotation(new Vector3(
                            118f + Mathf.Sin(phase) * .006f,
                            100f + Mathf.Cos(phase * 1.31f) * .004f, -10f),
                        Quaternion.Euler(0f, 0f, 63f + Mathf.Sin(phase * .71f) * .19f));
                    f.Camera.orthographicSize = 5f + Mathf.Cos(phase * .87f) * .018f;
                    RequireCenterlinesEqual(baseline, CaptureWorldCenterlines(f, overlay),
                        "Subpixel camera translation, turn and zoom must not move a line in world space or create/delete parallel rows.");
                }

                // Combine walking, turning and the whole scan-zoom range while
                // keeping the physical target comfortably in the viewport.
                for (int frame = 0; frame <= 40; frame++)
                {
                    float fraction = frame / 40f;
                    f.Camera.transform.SetPositionAndRotation(new Vector3(
                            118f + .25f * Mathf.Sin(fraction * Mathf.PI * 2f),
                            100f + .2f * Mathf.Sin(fraction * Mathf.PI), -10f),
                        Quaternion.Euler(0f, 0f, fraction * 270f));
                    f.Camera.orthographicSize = frame <= 20
                        ? Mathf.Lerp(5f, 8f, frame / 20f)
                        : Mathf.Lerp(8f, 3f, (frame - 20) / 20f);
                    RequireCenterlinesEqual(baseline, CaptureWorldCenterlines(f, overlay),
                        "Walking and turning during scan zoom must project the same cached world endpoints in every frame.");
                }
                f.Camera.orthographicSize = 5f;
                f.Camera.transform.SetPositionAndRotation(new Vector3(118f, 100f, -10f), Quaternion.identity);
                AssertBarsInsideProjectedFootprint(CaptureLocalGeometry(f, overlay, target), f, target);
                overlay.ShowScannedInteractable(target);
                RequireCenterlinesEqual(baseline, CaptureWorldCenterlines(f, overlay),
                    "Refreshing a reveal must not shift a distant target's cached world-line phase.");
            }
        }

        private static void CheckOverlappingAreas()
        {
            using (var f = new Fixture())
            {
                var overlay = f.Create("Overlapping garbage parallel scan lines")
                    .AddComponent<BioScanStaticAreaUI>();
                overlay.Initialize(f.View, f.Camera);
                ScannableInteractable first = f.Target(new Vector2(2f, 0f));
                ScannableInteractable second = f.Target(new Vector2(2.2f, .15f));
                first.transform.localScale = second.transform.localScale = Vector3.one * 3f;
                overlay.ShowScannedInteractable(first);
                List<Vector2> firstOnly = CaptureScreenGeometry(f, overlay);
                Tick(first, 7.1f);
                overlay.ShowScannedInteractable(second);
                List<Vector2> secondOnly = CaptureScreenGeometry(f, overlay);
                overlay.ShowScannedInteractable(first);
                List<Vector2> combined = CaptureScreenGeometry(f, overlay);
                Vector2 direction = ProjectedLineDirection(f.Camera, first);
                AssertNonIntersectingBars(combined);
                AssertCoverageUnion(firstOnly, secondOnly, combined, direction,
                    "Overlapping targets must preserve the union of both line areas while removing repeated coverage.");

                // A nearly expired target must not reserve shared coverage
                // ahead of an overlapping target with a complete entrance envelope.
                Tick(first, 6.75f);
                Tick(second, second.FadeInDuration + second.BreathingPeriod);
                byte weakAlpha = ((Color32)AreaTint(first)).a;
                byte strongAlpha = ((Color32)AreaTint(second)).a;
                Require(strongAlpha > weakAlpha + Mathf.RoundToInt(second.BreathingMaxAlpha * 255f * .5f),
                    "The overlap-opacity fixture must contain visibly different fade strengths.");
                var renderedAlphas = new List<byte>();
                List<Vector2> opacityGeometry = CaptureScreenGeometry(f, overlay, renderedAlphas);
                AssertCoverageUnion(firstOnly, secondOnly, opacityGeometry, direction,
                    "Changing reveal priority must retain coverage from both envelopes, including the fading target's exclusive area.");
                AssertNonIntersectingBars(opacityGeometry);
                List<LineInterval> firstIntervals = LineIntervals(firstOnly, direction);
                List<LineInterval> secondIntervals = LineIntervals(secondOnly, direction);
                List<LineInterval> actualIntervals = LineIntervals(opacityGeometry, direction);
                int sharedCount = 0, weakExclusiveCount = 0;
                for (int index = 0; index < actualIntervals.Count; index++)
                {
                    LineInterval interval = actualIntervals[index];
                    float middle = (interval.Start + interval.End) * .5f;
                    bool fromStrong = ContainsCoverage(secondIntervals, interval.Row, middle, .02f);
                    bool fromWeak = ContainsCoverage(firstIntervals, interval.Row, middle, .02f);
                    Require(fromStrong || fromWeak,
                        "Every rendered line piece must originate within one of the target envelopes.");
                    Require(renderedAlphas[index] == (fromStrong ? strongAlpha : weakAlpha),
                        "Every overlapping line piece must use the strongest reveal opacity.");
                    if (fromStrong && fromWeak) sharedCount++;
                    if (fromWeak && !fromStrong) weakExclusiveCount++;
                }
                Require(sharedCount > 0,
                    "The opacity fixture must exercise shared coverage from both targets.");
                Require(weakExclusiveCount > 0,
                    "Trimming shared coverage must retain the fading target's exclusive line area rather than deleting its whole bar.");

                // Prioritization may repartition a row at a different source
                // endpoint. Hold that reveal state constant while checking motion.
                combined = opacityGeometry;
                List<Vector3> combinedWorld = CaptureWorldCenterlines(f, overlay);
                foreach (float rotation in new[] { 0f, 45f, 90f, 180f, 270f })
                {
                    f.Camera.transform.rotation = Quaternion.Euler(0f, 0f, rotation);
                    List<Vector2> rotated = CaptureScreenGeometry(f, overlay);
                    AssertNonIntersectingBars(rotated);
                    Vector2 screenOrigin = f.Camera.WorldToScreenPoint(Vector3.zero);
                    Vector2 baselineOrigin = f.Camera.pixelRect.center;
                    for (int index = 0; index < rotated.Count; index++)
                        rotated[index] -= screenOrigin;
                    rotated = RotateGeometry(rotated, rotation);
                    for (int index = 0; index < rotated.Count; index++)
                        rotated[index] += baselineOrigin;
                    RequireGeometryEqual(combined, rotated,
                        "Overlapping line coverage and trim boundaries must remain fixed in the scene as the camera turns.");
                }
                f.Camera.transform.rotation = Quaternion.identity;
                foreach (float zoom in new[] { 3f, 5f, 8f })
                {
                    f.Camera.orthographicSize = zoom;
                    List<Vector2> zoomed = CaptureScreenGeometry(f, overlay);
                    Require(zoomed.Count >= 4, "Overlapping garbage must remain visible through scan zoom.");
                    AssertNonIntersectingBars(VisibleBarRectangles(zoomed));
                    RequireCenterlinesEqual(combinedWorld, CaptureWorldCenterlines(f, overlay),
                        "Camera zoom must retain the same cached union endpoints without adding or suppressing overlap pieces.");
                }
            }
        }

        private readonly struct LineInterval
        {
            public readonly float Row;
            public readonly float Start;
            public readonly float End;
            public LineInterval(float row, float start, float end)
            { Row = row; Start = start; End = end; }
        }

        private static void CheckOverlapBreathingStability()
        {
            using (var f = new Fixture())
            {
                var overlay = f.Create("Out-of-phase breathing overlap")
                    .AddComponent<BioScanStaticAreaUI>();
                overlay.Initialize(f.View, f.Camera);
                ScannableInteractable first = f.Target(new Vector2(2f, 0f));
                ScannableInteractable second = f.Target(new Vector2(2.2f, .15f));
                first.transform.localScale = second.transform.localScale = Vector3.one * 3f;
                overlay.ShowScannedInteractable(first);
                overlay.ShowScannedInteractable(second);
                Tick(first, first.FadeInDuration + first.BreathingPeriod * .5f);
                Tick(second, second.FadeInDuration + second.BreathingPeriod);
                Require(AreaTint(first).a < AreaTint(second).a,
                    "The overlap fixture must start with different breathing phases.");
                List<Vector2> baseline = CaptureScreenGeometry(f, overlay);
                AssertNonIntersectingBars(baseline);
                for (int frame = 0; frame < 4; frame++)
                {
                    Tick(first, first.BreathingPeriod * .25f);
                    Tick(second, second.BreathingPeriod * .25f);
                    RequireGeometryEqual(baseline, CaptureScreenGeometry(f, overlay),
                        "Crossing breathing alpha levels must only change line opacity, never repartition overlapping scene lines.");
                    if (frame == 1)
                        Require(AreaTint(first).a > AreaTint(second).a,
                            "The overlap fixture must cross which target has the greater rendered alpha.");
                }
            }
        }

        private static List<LineInterval> LineIntervals(List<Vector2> geometry, Vector2 direction)
        {
            var result = new List<LineInterval>(geometry.Count / 4);
            Vector2 normal = new Vector2(-direction.y, direction.x);
            for (int index = 0; index < geometry.Count; index += 4)
            {
                Vector2 barDirection = (geometry[index + 2] - geometry[index + 1]).normalized;
                Vector2 start = (geometry[index] + geometry[index + 1]) * .5f + barDirection;
                Vector2 end = (geometry[index + 2] + geometry[index + 3]) * .5f - barDirection;
                Require(Mathf.Abs(Vector2.Dot(barDirection, direction)) > .9999f,
                    "Overlap output must retain the same single parallel direction.");
                float a = Vector2.Dot(start, direction), b = Vector2.Dot(end, direction);
                result.Add(new LineInterval(Vector2.Dot((start + end) * .5f, normal),
                    Mathf.Min(a, b), Mathf.Max(a, b)));
            }
            return result;
        }

        private static bool ContainsCoverage(List<LineInterval> intervals,
            float row, float along, float tolerance)
        {
            foreach (LineInterval interval in intervals)
                if (Mathf.Abs(interval.Row - row) < .02f
                    && along >= interval.Start - tolerance && along <= interval.End + tolerance)
                    return true;
            return false;
        }

        private static void AssertCoverageUnion(List<Vector2> first, List<Vector2> second,
            List<Vector2> actual, Vector2 direction, string message)
        {
            List<LineInterval> expected = LineIntervals(first, direction);
            expected.AddRange(LineIntervals(second, direction));
            List<LineInterval> result = LineIntervals(actual, direction);
            const float trimTolerance = 2.53f;
            // One antialias pixel at either side of a trim boundary needs
            // a small clean gap; sub-half-pixel leftovers may be omitted.
            // Permit that seam while checking the full
            // source extent; never accept deleting a whole overlapping bar.
            foreach (LineInterval interval in expected)
            {
                int samples = Mathf.Max(2, Mathf.CeilToInt((interval.End - interval.Start) / 2f));
                for (int index = 0; index <= samples; index++)
                {
                    float along = Mathf.Lerp(interval.Start, interval.End, index / (float)samples);
                    Require(ContainsCoverage(result, interval.Row, along, trimTolerance),
                        message + " Source coverage was dropped beyond a clean endpoint seam.");
                }
            }
            foreach (LineInterval interval in result)
            {
                foreach (float along in new[] { interval.Start,
                    (interval.Start + interval.End) * .5f, interval.End })
                    Require(ContainsCoverage(expected, interval.Row, along, .03f),
                        message + " Output coverage extended outside both source rows.");
            }
        }

        private static void CheckOverlayCanvasRecovery()
        {
            using (var f = new Fixture())
            {
                var overlay = f.Create("Static scan recovery overlay").AddComponent<BioScanStaticAreaUI>();
                overlay.Initialize(f.View, f.Camera);
                ScannableInteractable target = f.Target(Vector2.zero);
                target.transform.localScale = Vector3.one * 2f;
                overlay.ShowScannedInteractable(target);
                List<Vector2> baseline = CaptureLocalGeometry(f, overlay, target);
                Canvas originalCanvas = (Canvas)Get(overlay, "areaCanvas");
                var originalGraphic = (BioScanStaticAreaGraphic)((IList)Get(overlay, "areaGraphics"))[0];

                // Scene teardown can destroy the separate Canvas children before
                // disabling the scanner component on the robot. Its managed pool
                // still contains Unity's destroyed-object references at that point.
                Object.DestroyImmediate(originalGraphic.gameObject);
                Call(overlay, "OnDisable");
                RequireNear(target.RevealRemaining, 7f,
                    "Disabling a scanner after its graphic is destroyed must preserve the target's remaining reveal.");
                Object.DestroyImmediate(originalCanvas.gameObject);
                Call(overlay, "OnEnable");
                f.UpdateOverlay(overlay);

                Canvas rebuiltCanvas = (Canvas)Get(overlay, "areaCanvas");
                IList rebuiltGraphics = (IList)Get(overlay, "areaGraphics");
                Require(rebuiltCanvas != null && rebuiltCanvas.isActiveAndEnabled
                    && rebuiltCanvas.renderMode == RenderMode.ScreenSpaceOverlay,
                    "A scanner whose separate Canvas was destroyed must recreate its active screen overlay.");
                Require(rebuiltGraphics.Count == 1 && rebuiltGraphics[0] is BioScanStaticAreaGraphic rebuiltGraphic
                    && rebuiltGraphic != null && rebuiltGraphic.AreaCount == 1,
                    "Recreating the Canvas must replace destroyed pool entries and restore the revealed target.");
                RequireGeometryEqual(baseline, CaptureLocalGeometry(f, overlay, target),
                    "Recreating an overlay Canvas must preserve the projected line direction, spacing and endpoints.");
                RequireNear(target.RevealRemaining, 7f,
                    "Recreating an overlay Canvas must not reset or clear the target's reveal lifetime.");
            }
        }

        private static List<Vector2> CaptureLocalGeometry(Fixture fixture,
            BioScanStaticAreaUI overlay, ScannableInteractable target)
        {
            fixture.UpdateOverlay(overlay);
            IList graphics = (IList)Get(overlay, "areaGraphics");
            Require(graphics.Count > 0, "A revealed target must produce a screen overlay graphic.");
            var graphic = (BioScanStaticAreaGraphic)graphics[0];
            var footprint = new List<Vector2>();
            Require(target.TryGetPresentationFootprint(footprint, out Vector3 worldCenter),
                "A revealed target must expose its actual world presentation center.");
            Vector2 anchor = fixture.Camera.WorldToScreenPoint(worldCenter);
            var geometry = new List<Vector2>();
            using (var mesh = new VertexHelper())
            {
                Call(graphic, "OnPopulateMesh", mesh);
                Require(mesh.currentVertCount >= 4,
                    "A revealed target must produce complete visible line geometry.");
                for (int index = 0; index < mesh.currentVertCount; index++)
                {
                    UIVertex vertex = default;
                    mesh.PopulateUIVertex(ref vertex, index);
                    RequireNear(vertex.uv1.y * 2f, target.LineWidthPixels,
                        "The line shader must receive the fixed authored pixel width.");
                    // The runtime shader consumes these absolute screen pixels,
                    // independent of the RectTransform's own matrix.
                    Vector2 screen = new Vector2(vertex.uv1.z, vertex.uv1.w);
                    geometry.Add(screen - anchor);
                }
            }
            return geometry;
        }

        private static List<Vector2> CaptureScreenGeometry(Fixture fixture, BioScanStaticAreaUI overlay,
            List<byte> barAlphas = null, bool expectVisible = true)
        {
            fixture.UpdateOverlay(overlay);
            var geometry = new List<Vector2>();
            foreach (BioScanStaticAreaGraphic graphic in (IList)Get(overlay, "areaGraphics"))
            {
                if (graphic == null) continue;
                using (var mesh = new VertexHelper())
                {
                    Call(graphic, "OnPopulateMesh", mesh);
                    Require(mesh.currentVertCount % 4 == 0,
                        "Each scan batch must contain complete rectangular bars.");
                    for (int index = 0; index < mesh.currentVertCount; index++)
                    {
                        UIVertex vertex = default;
                        mesh.PopulateUIVertex(ref vertex, index);
                        geometry.Add(new Vector2(vertex.uv1.z, vertex.uv1.w));
                        if (index % 4 == 0 && barAlphas != null) barAlphas.Add(vertex.color.a);
                    }
                }
            }
            Require(!expectVisible || geometry.Count > 0, "At least one revealed area must render screen-pixel bars.");
            return geometry;
        }

        private static List<Vector3> CaptureWorldCenterlines(Fixture fixture,
            BioScanStaticAreaUI overlay)
        {
            List<Vector2> rectangles = CaptureScreenGeometry(fixture, overlay);
            var centerlines = new List<Vector3>(rectangles.Count / 2);
            float depth = fixture.Camera.WorldToScreenPoint(Vector3.zero).z;
            for (int index = 0; index < rectangles.Count; index += 4)
            {
                Vector2 direction = (rectangles[index + 2] - rectangles[index + 1]).normalized;
                Vector2 start = (rectangles[index] + rectangles[index + 1]) * .5f + direction;
                Vector2 end = (rectangles[index + 2] + rectangles[index + 3]) * .5f - direction;
                RequireNear((rectangles[index + 1] - rectangles[index]).magnitude, .9f + 2f,
                    "Camera projection must preserve the authored visible stroke plus its two antialias pixels.");
                centerlines.Add(fixture.Camera.ScreenToWorldPoint(new Vector3(start.x, start.y, depth)));
                centerlines.Add(fixture.Camera.ScreenToWorldPoint(new Vector3(end.x, end.y, depth)));
            }
            return centerlines;
        }

        private static void RequireCenterlinesEqual(List<Vector3> expected,
            List<Vector3> actual, string message)
        {
            Require(expected.Count == actual.Count && expected.Count % 2 == 0,
                message + " The number of world centerlines changed.");
            var consumed = new bool[actual.Count / 2];
            const float tolerance = .0005f;
            for (int index = 0; index < expected.Count; index += 2)
            {
                bool found = false;
                for (int candidate = 0; candidate < actual.Count; candidate += 2)
                {
                    if (consumed[candidate / 2]) continue;
                    bool forwards = Vector3.Distance(expected[index], actual[candidate]) < tolerance
                        && Vector3.Distance(expected[index + 1], actual[candidate + 1]) < tolerance;
                    bool backwards = Vector3.Distance(expected[index], actual[candidate + 1]) < tolerance
                        && Vector3.Distance(expected[index + 1], actual[candidate]) < tolerance;
                    if (!forwards && !backwards) continue;
                    consumed[candidate / 2] = true;
                    found = true;
                    break;
                }
                Require(found, message + " A segment's inverse-projected endpoints moved in world space.");
            }
        }

        private static List<Vector2> SegmentRectangles(List<StaticScanLineSegment> segments, float width)
        {
            var geometry = new List<Vector2>(segments.Count * 4);
            foreach (StaticScanLineSegment segment in segments)
            {
                Vector2 direction = (segment.End - segment.Start).normalized;
                Vector2 normal = new Vector2(-direction.y, direction.x);
                Vector2 halfWidth = normal * (width * .5f + 1f);
                geometry.Add(segment.Start - direction - halfWidth);
                geometry.Add(segment.Start - direction + halfWidth);
                geometry.Add(segment.End + direction + halfWidth);
                geometry.Add(segment.End + direction - halfWidth);
            }
            return geometry;
        }

        private static void RequireGeometryEqual(List<Vector2> expected,
            List<Vector2> actual, string message)
        {
            Require(expected.Count == actual.Count, message + " Vertex count changed.");
            Require(GeometryMatches(expected, actual, .02f),
                message + " The unordered complete bar rectangles changed.");
        }

        private static void AssertParallelLineGeometry(List<Vector2> geometry,
            ScannableInteractable target, Vector2 direction, Vector2 latticeOrigin,
            bool requireMultipleRows, float projectionScale = 1f)
        {
            Require(geometry.Count >= 4 && geometry.Count % 4 == 0,
                "An unclipped footprint must contain complete bars with four corners per bar.");
            Vector2 normal = new Vector2(-direction.y, direction.x);
            var occupiedRows = new List<float>();
            float projectedSpacing = target.LineSpacingPixels * projectionScale;
            for (int index = 0; index < geometry.Count; index += 4)
            {
                Vector2 lengthEdge = geometry[index + 2] - geometry[index + 1];
                Vector2 widthEdge = geometry[index + 1] - geometry[index];
                Require(Mathf.Abs(Vector2.Dot(lengthEdge.normalized, direction)) > .9999f,
                    "Every yellow bar must use the same world-projected direction; a second crossing family is not allowed.");
                Require(Mathf.Abs(Vector2.Dot(lengthEdge.normalized, widthEdge.normalized)) < .0001f,
                    "Complete bar ends must remain perpendicular square caps rather than mask-cut fragments.");
                RequireNear(widthEdge.magnitude, target.LineWidthPixels + 2f,
                    "A complete bar must retain its authored width plus one antialias pixel per side.");
                float visibleLength = lengthEdge.magnitude - 2f;
                Require(visibleLength > 0f,
                    "Every parallel row must contain one positive-length continuous line.");
                Vector2 center = (geometry[index] + geometry[index + 1]
                    + geometry[index + 2] + geometry[index + 3]) * .25f;
                float acrossCell = Vector2.Dot(center - latticeOrigin, normal) / projectedSpacing;
                bool fallback = geometry.Count == 4 && !requireMultipleRows;
                if (!fallback)
                {
                    RequireNear(acrossCell, Mathf.Round(acrossCell),
                        "Parallel rows must retain the same shared world phase while their spacing projects continuously through the camera.");
                    Require(visibleLength + .02f >= Mathf.Max(3f, target.LineWidthPixels * 3f) * projectionScale,
                        "Regular rows must keep readable complete short edge bars while omitting tiny fringe slivers.");
                }
                foreach (float row in occupiedRows)
                    Require(Mathf.Abs(acrossCell - row) > .001f,
                        "A target must generate one continuous bar per row, without open-intersection breaks.");
                occupiedRows.Add(acrossCell);
            }
            Require(!requireMultipleRows || occupiedRows.Count >= 3,
                "A sufficiently large scanned area must contain several parallel lines.");
            AssertNonIntersectingBars(Mathf.Approximately(projectionScale, 1f)
                ? geometry : VisibleBarRectangles(geometry));
        }

        private static List<Vector2> VisibleBarRectangles(List<Vector2> geometry)
        {
            var visible = new List<Vector2>(geometry.Count);
            for (int index = 0; index < geometry.Count; index += 4)
            {
                Vector2 direction = (geometry[index + 2] - geometry[index + 1]).normalized;
                Vector2 normal = (geometry[index + 1] - geometry[index]).normalized;
                visible.Add(geometry[index] + direction + normal);
                visible.Add(geometry[index + 1] + direction - normal);
                visible.Add(geometry[index + 2] - direction - normal);
                visible.Add(geometry[index + 3] - direction + normal);
            }
            return visible;
        }

        private static void ConfigureAsymmetricTarget(ScannableInteractable target)
        {
            Set(target, "customNormalizedOutline", new[] {
                new Vector2(-.5f, -.35f), new Vector2(-.3f, .5f),
                new Vector2(.5f, .3f), new Vector2(.4f, -.5f) });
            target.transform.localScale = new Vector3(2.4f, 1.6f, 1f);
            target.transform.rotation = Quaternion.Euler(0f, 0f, 23f);
            Call(target, "OnValidate");
        }

        private static Vector2 ProjectedLineDirection(Camera camera, ScannableInteractable target)
        {
            var outline = new List<Vector2>();
            Require(target.TryGetPresentationFootprint(outline, out Vector3 worldCenter),
                "A revealed target must expose its physical world footprint.");
            float angle = target.LineAngleDegrees * Mathf.Deg2Rad;
            Vector3 worldDirection = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            Vector2 projected = camera.WorldToScreenPoint(worldCenter + worldDirection)
                - camera.WorldToScreenPoint(worldCenter);
            return projected.normalized;
        }

        private static Vector2 ProjectedLatticeOrigin(Camera camera, ScannableInteractable target)
        {
            var outline = new List<Vector2>();
            Require(target.TryGetPresentationFootprint(outline, out Vector3 worldCenter),
                "The area must expose its presentation center for the shared scene line rows.");
            return (Vector2)camera.WorldToScreenPoint(new Vector3(0f, 0f, worldCenter.z))
                - (Vector2)camera.WorldToScreenPoint(worldCenter);
        }

        private static List<Vector2> RotateGeometry(List<Vector2> geometry, float angle)
        {
            var rotated = new List<Vector2>(geometry.Count);
            Quaternion rotation = Quaternion.Euler(0f, 0f, angle);
            foreach (Vector2 point in geometry) rotated.Add(rotation * point);
            return rotated;
        }

        private static bool GeometryDiffers(List<Vector2> expected, List<Vector2> actual)
        {
            return !GeometryMatches(expected, actual, .1f);
        }

        private static bool GeometryMatches(List<Vector2> expected, List<Vector2> actual, float tolerance)
        {
            if (expected.Count != actual.Count || expected.Count % 4 != 0) return false;
            var consumed = new bool[actual.Count / 4];
            for (int index = 0; index < expected.Count; index += 4)
            {
                bool found = false;
                for (int candidate = 0; candidate < actual.Count; candidate += 4)
                {
                    if (consumed[candidate / 4] || !BarsMatch(expected, index, actual, candidate, tolerance))
                        continue;
                    consumed[candidate / 4] = true;
                    found = true;
                    break;
                }
                if (!found) return false;
            }
            return true;
        }

        private static bool BarsMatch(List<Vector2> first, int firstStart,
            List<Vector2> second, int secondStart, float tolerance)
        {
            for (int index = 0; index < 4; index++)
            {
                bool matched = false;
                for (int candidate = 0; candidate < 4; candidate++)
                    matched |= Vector2.Distance(first[firstStart + index], second[secondStart + candidate]) < tolerance;
                if (!matched) return false;
            }
            return true;
        }

        private static void AssertNonIntersectingBars(List<Vector2> geometry)
        {
            for (int first = 0; first < geometry.Count; first += 4)
                for (int second = first + 4; second < geometry.Count; second += 4)
                {
                    Require(!BarsMatch(geometry, first, geometry, second, .02f),
                        "The same line coverage must be drawn once even when two garbage areas overlap.");
                    bool separated = HasSeparatingAxis(geometry, first, second, first)
                        || HasSeparatingAxis(geometry, first, second, second);
                    Require(separated,
                        "Complete bars, including antialias padding, must not overlap or touch another line piece.");
                }
        }

        private static bool HasSeparatingAxis(List<Vector2> geometry, int first, int second, int axes)
        {
            for (int edge = 0; edge < 2; edge++)
            {
                Vector2 axis = (geometry[axes + edge + 1] - geometry[axes + edge]).normalized;
                float firstMin = float.PositiveInfinity, firstMax = float.NegativeInfinity;
                float secondMin = float.PositiveInfinity, secondMax = float.NegativeInfinity;
                for (int corner = 0; corner < 4; corner++)
                {
                    float a = Vector2.Dot(geometry[first + corner], axis);
                    float b = Vector2.Dot(geometry[second + corner], axis);
                    firstMin = Mathf.Min(firstMin, a);
                    firstMax = Mathf.Max(firstMax, a);
                    secondMin = Mathf.Min(secondMin, b);
                    secondMax = Mathf.Max(secondMax, b);
                }
                if (firstMax < secondMin - .001f || secondMax < firstMin - .001f) return true;
            }
            return false;
        }

        private static float GeometrySpan(List<Vector2> geometry, Vector2 direction)
        {
            float minimum = float.PositiveInfinity, maximum = float.NegativeInfinity;
            foreach (Vector2 point in geometry)
            {
                float along = Vector2.Dot(point, direction);
                minimum = Mathf.Min(minimum, along);
                maximum = Mathf.Max(maximum, along);
            }
            return maximum - minimum;
        }

        private static void AssertBarsInsideProjectedFootprint(List<Vector2> geometry,
            Fixture fixture, ScannableInteractable target)
        {
            var worldOutline = new List<Vector3>();
            Require(target.TryGetWorldOutline(worldOutline), "The target must have an actual world outline.");
            var offsets = new List<Vector2>();
            Require(target.TryGetPresentationFootprint(offsets, out Vector3 worldCenter),
                "The target must expose its world center.");
            Vector2 anchor = fixture.Camera.WorldToScreenPoint(worldCenter);
            var projectedOutline = new List<Vector3>();
            foreach (Vector3 point in worldOutline)
                projectedOutline.Add((Vector2)fixture.Camera.WorldToScreenPoint(point) - anchor);
            foreach (Vector2 corner in geometry)
                Require(InsideConvexOutline(corner, projectedOutline),
                    "Every complete bar rectangle, including square ends and one-pixel antialias margins, must remain inside the object's actual projected world footprint.");
        }

        private static void AssertAllCornersInside(List<Vector2> geometry,
            List<Vector2> polygon, string message)
        {
            var outline = new List<Vector3>(polygon.Count);
            foreach (Vector2 point in polygon) outline.Add(point);
            foreach (Vector2 corner in geometry)
                Require(InsideConvexOutline(corner, outline), message);
        }

        private static void CheckAnimatedAreaTint()
        {
            using (var fixture = new Fixture())
            {
                ScannableInteractable target = fixture.Target(Vector2.zero);
                // Keep the repeated-cycle fixture clear of expiry; the
                // independent expiring target below retains production timing.
                Set(target, "displayDuration", 12f);
                target.Reveal();
                RequireNear(AreaTint(target).a, 0f,
                    "A newly scanned static area must start fully transparent.");
                float previous = 0f;
                for (int quarter = 0; quarter < 4; quarter++)
                {
                    Tick(target, target.FadeInDuration * .25f);
                    float alpha = AreaTint(target).a;
                    Require(alpha > previous && alpha <= target.BreathingMaxAlpha,
                        "The authored entrance must progressively reveal the lines through several intermediate opacity levels.");
                    previous = alpha;
                    if (quarter == 0)
                    {
                        float enteringElapsed = target.RevealElapsed;
                        target.Reveal();
                        RequireNear(target.RevealElapsed, enteringElapsed,
                            "A repeat scan during entrance must preserve its progress.");
                        RequireNear(AreaTint(target).a, alpha,
                            "A repeat scan during entrance must not flash the partially visible lines off.");
                    }
                }
                RequireNear(AreaTint(target).a, target.BreathingMaxAlpha,
                    "The smooth entrance must finish at the pulse peak before the pulse clock starts.");
                Tick(target, target.BreathingPeriod);
                RequireNear(AreaTint(target).a, target.BreathingMaxAlpha,
                    "A full breathing cycle must reach the readable authored peak.");
                Tick(target, target.BreathingPeriod * .25f);
                RequireNear(AreaTint(target).a,
                    Mathf.Lerp(target.BreathingMinAlpha, target.BreathingMaxAlpha, .25f),
                    "The squared-cosine pulse must pass through a lower quarter-cycle alpha, keeping its bright peak brief.");
                Tick(target, target.BreathingPeriod * .25f);
                RequireNear(AreaTint(target).a, target.BreathingMinAlpha,
                    "Half a cycle after the peak must smoothly reach the authored minimum.");
                Require(AreaTint(target).a > 0f
                    && target.BreathingMaxAlpha - AreaTint(target).a > .45f,
                    "The stronger pulse must remain faintly visible at its trough while producing a substantial brightness change.");
                float previousTimeScale = Time.timeScale;
                try
                {
                    Time.timeScale = 0f;
                    float remaining = target.RevealRemaining, elapsed = target.RevealElapsed;
                    float alpha = AreaTint(target).a;
                    Tick(target, target.BreathingPeriod);
                    RequireNear(target.RevealRemaining, remaining,
                        "Pause must freeze the remaining scan lifetime.");
                    RequireNear(target.RevealElapsed, elapsed,
                        "Pause must freeze the reveal animation clock.");
                    RequireNear(AreaTint(target).a, alpha,
                        "Pause must freeze the breathing opacity on its current frame.");
                }
                finally { Time.timeScale = previousTimeScale; }
                Tick(target, target.BreathingPeriod * .5f);
                RequireNear(AreaTint(target).a, target.BreathingMaxAlpha,
                    "The following cycle must return to the same peak opacity.");
                float elapsedBeforeRefresh = target.RevealElapsed;
                float alphaBeforeRefresh = AreaTint(target).a;
                target.Reveal();
                RequireNear(target.RevealRemaining, target.DisplayDuration,
                    "Scanning a visible target must still refresh its full lifetime.");
                RequireNear(target.RevealElapsed, elapsedBeforeRefresh,
                    "Scanning a visible target must retain its animation phase.");
                RequireNear(AreaTint(target).a, alphaBeforeRefresh,
                    "A repeated scan must not hide and restart a visible area entrance.");
                Tick(target, target.BreathingPeriod * .5f);
                RequireNear(AreaTint(target).a, target.BreathingMinAlpha,
                    "Breathing must continue from its original phase after a scan refresh.");
                Tick(target, target.DisplayDuration);
                RequireNear(AreaTint(target).a, 0f,
                    "An expired scan must render no residual alpha.");
                target.Reveal();
                RequireNear(target.RevealElapsed, 0f,
                    "A new scan after expiry must restart the entrance clock.");
                RequireNear(AreaTint(target).a, 0f,
                    "A newly revealed expired target must fade in again.");

                // Use a separate lifetime so a refresh cannot disguise the
                // relationship between breathing and the final expiry envelope.
                ScannableInteractable expiring = fixture.Target(Vector2.right * 4f);
                expiring.Reveal();
                Tick(expiring, expiring.DisplayDuration - expiring.FadeOutDuration);
                Tick(expiring, expiring.FadeOutDuration * .5f);
                RequireNear(ExpectedRevealEnvelope(expiring), .5f,
                    "The final expiry envelope must reach half opacity at its midpoint independently of pulse phase.");
                RequireNear(AreaTint(expiring).a, ExpectedHatchPulse(expiring) * .5f,
                    "The final fade must multiply the current pulse rather than replace or restart its phase.");
                Tick(expiring, expiring.FadeOutDuration);
                RequireNear(AreaTint(expiring).a, 0f,
                    "The final fade must reach full transparency at expiry.");

                ScannableInteractable custom = fixture.Target(Vector2.left * 4f);
                Set(custom, "areaColor", new Color(.2f, .4f, .7f, .5f));
                Set(custom, "fadeInDuration", 1f);
                Set(custom, "breathingPeriod", 4f);
                Set(custom, "breathingMinAlpha", .2f);
                Set(custom, "breathingMaxAlpha", .6f);
                custom.Reveal();
                Tick(custom, 1f);
                Color customTint = AreaTint(custom);
                RequireNear(customTint.r, .2f, "Opacity animation must preserve the authored red channel.");
                RequireNear(customTint.g, .4f, "Opacity animation must preserve the authored green channel.");
                RequireNear(customTint.b, .7f, "Opacity animation must preserve the authored blue channel.");
                RequireNear(customTint.a, .3f,
                    "Custom entrance must finish at its peak pulse intensity multiplied by the authored color alpha.");
                float independentOutline = OutlineTint(custom).a;
                Set(custom, "breathingPeriod", 5f);
                Set(custom, "breathingMinAlpha", .1f);
                Set(custom, "breathingMaxAlpha", .8f);
                RequireNear(OutlineTint(custom).a, independentOutline,
                    "Custom hatch pulse parameters must not alter the perimeter's own breathing intensity.");
                Set(custom, "breathingPeriod", 4f);
                Set(custom, "breathingMinAlpha", .2f);
                Set(custom, "breathingMaxAlpha", .6f);
                Set(custom, "outlineBreathingPeriod", 3f);
                Set(custom, "outlineBreathingMinAlpha", .25f);
                Set(custom, "outlineBreathingMaxAlpha", .45f);
                RequireNear(AreaTint(custom).a, .3f,
                    "Custom perimeter breathing parameters must not alter the hatch pulse.");
                RequireNear(OutlineTint(custom).a, .5f * ExpectedOutlineBreath(custom),
                    "Perimeter breathing must use its own configurable period and bounds.");
                Tick(custom, 1f);
                RequireNear(AreaTint(custom).a, .15f,
                    "A custom pulse's quarter cycle must use the squared-cosine intensity.");
                Tick(custom, 1f);
                RequireNear(AreaTint(custom).a, .1f,
                    "Custom breathing minimum must multiply the authored color alpha.");
                Tick(custom, 2f);
                RequireNear(AreaTint(custom).a, .3f,
                    "Custom breathing period must return to the custom maximum.");
                Tick(custom, custom.DisplayDuration);
                Set(custom, "fadeInDuration", 0f);
                Set(custom, "breathingMinAlpha", .4f);
                Set(custom, "breathingMaxAlpha", .4f);
                custom.Reveal();
                RequireNear(AreaTint(custom).a, .2f,
                    "A zero entrance and equal breathing bounds must support a configured constant-opacity area.");
            }
        }

        private static Color AreaTint(ScannableInteractable target) => (Color)typeof(BioScanStaticAreaUI)
            .GetMethod("GetAreaTint", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { target });

        private static Color OutlineTint(ScannableInteractable target) => (Color)typeof(BioScanStaticAreaUI)
            .GetMethod("GetOutlineTint", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { target });

        private static float ExpectedHatchPulse(ScannableInteractable target)
        {
            float elapsed = Mathf.Max(0f, target.RevealElapsed - target.FadeInDuration);
            float cosine = .5f + .5f * Mathf.Cos(elapsed / target.BreathingPeriod * Mathf.PI * 2f);
            return Mathf.Lerp(target.BreathingMinAlpha, target.BreathingMaxAlpha, cosine * cosine);
        }

        private static float ExpectedOutlineBreath(ScannableInteractable target)
        {
            float cosine = .5f + .5f * Mathf.Cos(target.RevealElapsed
                / target.OutlineBreathingPeriod * Mathf.PI * 2f);
            return Mathf.Lerp(target.OutlineBreathingMinAlpha, target.OutlineBreathingMaxAlpha, cosine);
        }

        private static float ExpectedRevealEnvelope(ScannableInteractable target)
        {
            if (target.RevealRemaining <= 0f) return 0f;
            float envelope = target.FadeInDuration > 0f
                ? Mathf.SmoothStep(0f, 1f, target.RevealElapsed / target.FadeInDuration) : 1f;
            float fade = Mathf.Min(target.FadeOutDuration, target.DisplayDuration);
            if (fade > 0f && target.RevealRemaining < fade)
                envelope *= Mathf.SmoothStep(0f, 1f, target.RevealRemaining / fade);
            return envelope;
        }

        private static void CheckFragmentTransfer()
        {
            using (var f = new Fixture())
            {
                ScannableInteractable target = f.Target(Vector2.zero);
                WorldInteraction source = target.gameObject.AddComponent<WorldInteraction>();
                source.SetKind(WorldInteractionKind.Grabbable);
                source.SpriteSource = target.GetComponent<SpriteRenderer>();
                Call(target, "OnEnable");
                GarbageFragmentSpawner spawner = target.gameObject.AddComponent<GarbageFragmentSpawner>();
                Call(spawner, "Awake");
                Set(spawner, "scatteredFragments", new[] {
                    AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDirectory + "Small_Garbage.prefab") });
                target.Reveal();
                Require(target.CaptureScanOrientation(32f),
                    "The real fragment-transfer source must capture a non-default scan orientation before breaking.");
                float capturedWorldAngle = target.CapturedScanWorldAngleDegrees;
                uint capturedOutlineSeed = target.OutlinePatternSeed;
                Tick(target, 3f);
                // Pause the real break coroutine at its first fade frame. This
                // exercises capture-before-None without edit-mode deferred Destroy.
                var routine = (IEnumerator)Call(spawner, "BreakRoutine", false);
                Require(routine.MoveNext(), "The source must enter its break fade before spawning fragments.");
                Tick(target, 0f);
                Require(target.RevealRemaining == 0f, "The dissolving source must retire its own area highlight.");
                object[] args = { new Vector3(10f, 0f), Vector2.right, 0f, null };
                bool spawned = (bool)Call(spawner, "TrySpawnScatteredFragment", args);
                Require(spawned && args[3] is WorldInteraction, "The clear path must produce a real small garbage fragment.");
                var fragment = (WorldInteraction)args[3];
                Require(fragment.gameObject.scene == f.Scene,
                    "Spawned fragments must stay in their source scene so the same scanner can find them.");
                ScannableInteractable scan = fragment.GetComponent<ScannableInteractable>();
                Require(scan != null && scan.RevealRemaining > 0f && scan.RevealRemaining <= 4.001f,
                    "Actual spawned fragments must retain the captured remaining reveal after source retirement.");
                Require(scan.RevealElapsed >= 3f && scan.RevealElapsed <= 3f + Mathf.Max(0f, Time.deltaTime) + .001f,
                    "Actual spawned fragments must retain the captured three-second animation phase rather than fade in from zero.");
                RequireNear(scan.RevealElapsed + scan.RevealRemaining, 7f,
                    "The captured source clock must advance animation and remaining lifetime together before the fragment appears.");
                Require(scan.HasCapturedScanOrientation && !scan.IsScanOrientationPending,
                    "An actual spawned fragment must inherit a resolved orientation rather than request a new scan direction after source retirement.");
                RequireAngleNear(scan.CapturedScanWorldAngleDegrees, capturedWorldAngle,
                    "The real spawner must preserve the captured non-default direction even after the source interaction kind becomes None.");
                Require(scan.OutlinePatternSeed == capturedOutlineSeed && capturedOutlineSeed != 0,
                    "The real spawner must preserve the source perimeter seed after retirement rather than randomize each fragment.");
            }
        }

        private static void CheckShader()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/Resources/BioScanStaticArea.shader");
            Require(shader != null && !ShaderUtil.ShaderHasError(shader),
                "The finite parallel-line area shader must import without errors.");
            var material = new Material(shader);
            try
            {
                Require(!material.HasProperty("_ClipRadiusPixels"),
                    "Static area highlights must not use the circular player-view clipping shader.");
            }
            finally { Object.DestroyImmediate(material); }
        }

        private static float PolygonArea(List<Vector3> points)
        {
            float twiceArea = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 a = points[i], b = points[(i + 1) % points.Count];
                twiceArea += a.x * b.y - a.y * b.x;
            }
            return Mathf.Abs(twiceArea) * .5f;
        }

        private static bool InsideConvexOutline(Vector2 point, List<Vector3> outline)
        {
            bool positive = false, negative = false;
            for (int index = 0; index < outline.Count; index++)
            {
                Vector2 edge = outline[(index + 1) % outline.Count] - outline[index];
                Vector2 offset = point - (Vector2)outline[index];
                float cross = edge.x * offset.y - edge.y * offset.x;
                positive |= cross > .0001f;
                negative |= cross < -.0001f;
            }
            return !(positive && negative);
        }

        private static void Tick(ScannableInteractable target, float seconds) => Call(target, "TickReveal", seconds);
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        private static void RequireNear(float actual, float expected, string message) =>
            Require(Mathf.Abs(actual - expected) < .001f, message + " Actual: " + actual);
        private static object Get(object target, string name) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).SetValue(target, value);
        private static object Call(object target, string name, params object[] args) => target.GetType()
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
                | BindingFlags.DeclaredOnly).Invoke(target, args);

        private sealed class Fixture : IDisposable
        {
            public readonly Scene Scene;
            public readonly Camera Camera;
            public readonly ScanChargeUI View;
            public bool AlignOnScan;
            public bool ShowOutlineDots;
            private readonly Texture2D bodyTexture;
            private readonly Sprite bodySprite;
            private RenderTexture cameraRenderTarget;

            public Fixture()
            {
                Scene = EditorSceneManager.NewPreviewScene();
                Camera = Create("Static scan validation camera").AddComponent<Camera>();
                Camera.transform.position = new Vector3(0f, 0f, -10f);
                Camera.orthographic = true;
                Camera.orthographicSize = 5f;
                Camera.pixelRect = new Rect(0f, 0f, 800f, 600f);
                var viewObject = new GameObject("Static scan validation view", typeof(RectTransform), typeof(Canvas));
                SceneManager.MoveGameObjectToScene(viewObject, Scene);
                viewObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                View = viewObject.AddComponent<ScanChargeUI>();
                Set(View, "uiRingRadiusPixels", 50f);
                Set(View, "baseCameraOrthographicSize", 5f);
                Canvas.ForceUpdateCanvases();
                Vector2 center = View.GetUiCenterScreenPoint();
                Camera.pixelRect = new Rect(center.x - 400f, center.y - 300f, 800f, 600f);
                bodyTexture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                var pixels = new Color[64 * 64];
                for (int index = 0; index < pixels.Length; index++) pixels[index] = Color.white;
                bodyTexture.SetPixels(pixels);
                bodyTexture.Apply(false, false);
                bodySprite = Sprite.Create(bodyTexture, new Rect(0f, 0f, 64f, 64f), Vector2.one * .5f, 32f);
            }

            public GameObject Create(string name)
            {
                var go = new GameObject(name);
                SceneManager.MoveGameObjectToScene(go, Scene);
                return go;
            }

            public void SetCameraRenderTarget(int width, int height)
            {
                ReleaseCameraRenderTarget();
                cameraRenderTarget = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
                {
                    name = "Static scan normal-view validation target",
                    hideFlags = HideFlags.HideAndDontSave
                };
                cameraRenderTarget.Create();
                Camera.targetTexture = cameraRenderTarget;
                Camera.rect = new Rect(0f, 0f, 1f, 1f);
                Camera.pixelRect = new Rect(0f, 0f, width, height);
            }

            private void ReleaseCameraRenderTarget()
            {
                if (cameraRenderTarget == null) return;
                if (Camera != null) Camera.targetTexture = null;
                cameraRenderTarget.Release();
                Object.DestroyImmediate(cameraRenderTarget);
                cameraRenderTarget = null;
            }

            public ScannableInteractable Target(Vector2 position)
            {
                GameObject go = Create("Static scan validation target");
                go.transform.position = position;
                go.AddComponent<SpriteRenderer>().sprite = bodySprite;
                ScannableInteractable target = go.AddComponent<ScannableInteractable>();
                Set(target, "showOutlineDots", ShowOutlineDots);
                Set(target, "customNormalizedOutline", new[] { Vector2.left * .5f, Vector2.up * .5f,
                    Vector2.right * .5f, Vector2.down * .5f });
                Call(target, "OnValidate");
                // Preview-scene components may not receive play-mode callbacks.
                Call(target, "OnEnable");
                return target;
            }

            public void UpdateOverlay(BioScanStaticAreaUI overlay)
            {
                // Existing scene-fixed checks remain a regression control;
                // scan-aligned cases opt in explicitly above.
                Set(overlay, "alignOnScan", AlignOnScan);
                // The established hatch regressions intentionally isolate the
                // interior layer. Outline checks opt in, including prefabs.
                foreach (GameObject root in Scene.GetRootGameObjects())
                    foreach (ScannableInteractable target in root.GetComponentsInChildren<ScannableInteractable>(true))
                        Set(target, "showOutlineDots", ShowOutlineDots);
                Canvas.ForceUpdateCanvases();
                Call(overlay, "TickAreas");
            }

            public void Dispose()
            {
                foreach (GameObject root in Scene.GetRootGameObjects())
                {
                    foreach (ScannableInteractable target in root.GetComponentsInChildren<ScannableInteractable>(true))
                        Call(target, "OnDisable");
                }
                ReleaseCameraRenderTarget();
                EditorSceneManager.ClosePreviewScene(Scene);
                Object.DestroyImmediate(bodySprite);
                Object.DestroyImmediate(bodyTexture);
            }
        }
    }
}
