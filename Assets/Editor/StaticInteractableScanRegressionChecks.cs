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
            CheckBrokenGridPattern();
            CheckOverlayProjection();
            CheckContinuousCameraMotion();
            CheckOverlappingAreas();
            CheckSteadyAreaTint();
            CheckOverlayCanvasRecovery();
            CheckFragmentTransfer();
            CheckShader();
            Debug.Log("Static interactable scan checks PASS: production garbage prefabs, sprite envelope, transforms, edge contact, origin crossing, lifetime, per-wave deduplication, scene isolation, complete two-direction grid bars with open intersections, full baseline antialias rectangles inside the footprint, tiny-area fallback, shared world grid and overlap deduplication, off-view overlay geometry, cached world endpoints through camera rotation, translation, micro-motion and scan zoom, fixed pixel stroke width with continuously projected grid spacing and intersection gaps, steady reveal opacity, repeated reveal stability, partial viewport overlap, destroyed-canvas recovery, cleanup, fragment inheritance and shader import.");
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
                    Require(((Color32)target.AreaColor).Equals(new Color32(212, 184, 84, 255)),
                        "Garbage scan must use interaction gold #D4B854.");
                    RequireNear(target.LineAngleDegrees, -37f,
                        "Garbage scan grid must use the authored first world direction.");
                    RequireNear(target.LineWidthPixels, 1.4f,
                        "Garbage scan grid must use the authored 1.4-pixel bars.");
                    RequireNear(target.LineSpacingPixels, 15.4f,
                        "Garbage scan grid must use the authored 15.4-pixel lattice.");
                    RequireNear(target.IntersectionGapPixels, 5.6f,
                        "Garbage scan grid must leave 5.6 pixels open around each theoretical intersection.");
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

        private static void CheckBrokenGridPattern()
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
                    target.LineAngleDegrees, segments, target.IntersectionGapPixels,
                    target.LineWidthPixels, origin),
                    "A large convex envelope must generate an open-intersection grid.");
                Require(segments.Count > 16,
                    "The grid fixture must include many complete cells in both directions.");
                List<Vector2> geometry = SegmentRectangles(segments, target.LineWidthPixels);
                float angle = target.LineAngleDegrees * Mathf.Deg2Rad;
                AssertBrokenGridGeometry(geometry, target,
                    new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), origin, true);
                AssertAllCornersInside(geometry, polygon,
                    "Every full grid bar, including its antialias margin, must fit inside the convex envelope.");

                Vector2 translation = new Vector2(-37f, 61f);
                var translatedPolygon = new List<Vector2>();
                foreach (Vector2 point in polygon) translatedPolygon.Add(point + translation);
                var translatedSegments = new List<StaticScanLineSegment>();
                Require(BioScanStaticLinePattern.Build(translatedPolygon, target.LineSpacingPixels,
                    target.LineAngleDegrees, translatedSegments, target.IntersectionGapPixels,
                    target.LineWidthPixels, origin + translation),
                    "Translating the footprint and shared grid must retain a valid pattern.");
                List<Vector2> translatedGeometry = SegmentRectangles(translatedSegments, target.LineWidthPixels);
                for (int index = 0; index < translatedGeometry.Count; index++)
                    translatedGeometry[index] -= translation;
                RequireGeometryEqual(geometry, translatedGeometry,
                    "A shared-grid translation must preserve the complete cells and intersection gaps.");

                var tinyPolygon = new List<Vector2> {
                    new Vector2(-5f, -5f), new Vector2(5f, -5f),
                    new Vector2(5f, 5f), new Vector2(-5f, 5f) };
                Require(BioScanStaticLinePattern.Build(tinyPolygon, target.LineSpacingPixels,
                    target.LineAngleDegrees, segments, target.IntersectionGapPixels,
                    target.LineWidthPixels),
                    "A small envelope must retain a readable complete fallback bar.");
                Require(segments.Count == 1,
                    "An area unable to contain one full grid unit must use one bar rather than a crowded miniature grid.");
                List<Vector2> tinyGeometry = SegmentRectangles(segments, target.LineWidthPixels);
                AssertBrokenGridGeometry(tinyGeometry, target,
                    new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), Vector2.zero, false);
                AssertAllCornersInside(tinyGeometry, tinyPolygon,
                    "The tiny-area fallback must keep its full square ends and antialias margin inside the area.");
            }
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
                AssertBrokenGridGeometry(baseline, target, ProjectedLineDirection(f.Camera, target),
                    ProjectedLatticeOrigin(f.Camera, target), true);
                AssertBarsInsideProjectedFootprint(baseline, f, target);

                // A world-fixed pattern must rotate on screen with the terrain.
                // Undo only the camera rotation to compare it with the original
                // world orientation; an angle locked to the HUD fails this.
                foreach (float rotation in new[] { 0f, 45f, 90f, 180f, 270f })
                {
                    f.Camera.transform.rotation = Quaternion.Euler(0f, 0f, rotation);
                    List<Vector2> rotated = CaptureLocalGeometry(f, overlay, target);
                    AssertBrokenGridGeometry(rotated, target, ProjectedLineDirection(f.Camera, target),
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
                        AssertBrokenGridGeometry(geometry, target, direction,
                            ProjectedLatticeOrigin(f.Camera, target), true, 5f / f.Camera.orthographicSize);
                        RequireCenterlinesEqual(baselineCenterlines, CaptureWorldCenterlines(f, overlay),
                            "Every frame of scan zoom must retain the same world centerlines and cell count; only their continuous projection may change.");
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
                AssertBrokenGridGeometry(moved, target, ProjectedLineDirection(f.Camera, target),
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
                        AssertBrokenGridGeometry(initialGeometry, initialTarget,
                            ProjectedLineDirection(initial.Camera, initialTarget),
                            ProjectedLatticeOrigin(initial.Camera, initialTarget), true, 5f / firstRevealZoom);
                        RequireCenterlinesEqual(baselineCenterlines,
                            CaptureWorldCenterlines(initial, initialOverlay),
                            "A first reveal during scan zoom must use the normal-view world lattice rather than rebuilding cells at the current camera scale.");
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
                var overlay = f.Create("Static world grid continuous camera motion")
                    .AddComponent<BioScanStaticAreaUI>();
                overlay.Initialize(f.View, f.Camera);
                ScannableInteractable target = f.Target(new Vector2(120f, 100f));
                ConfigureAsymmetricTarget(target);
                f.Camera.transform.SetPositionAndRotation(new Vector3(118f, 100f, -10f),
                    Quaternion.Euler(0f, 0f, 63f));
                overlay.ShowScannedInteractable(target);
                List<Vector3> baseline = CaptureWorldCenterlines(f, overlay);
                Require(baseline.Count > 16,
                    "The distant-world motion fixture must contain multiple complete cells in both directions.");

                // Floating-origin distance and tiny motion expose cell-phase
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
                        "Subpixel camera translation, turn and zoom must not move a short segment in world space or create/delete lattice cells.");
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
                    "Refreshing a reveal must not shift a distant target's cached world-grid phase.");
            }
        }

        private static void CheckOverlappingAreas()
        {
            using (var f = new Fixture())
            {
                var overlay = f.Create("Overlapping garbage scan grid")
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
                List<Vector3> combinedWorld = CaptureWorldCenterlines(f, overlay);
                Require(combined.Count >= Mathf.Max(firstOnly.Count, secondOnly.Count)
                    && combined.Count < firstOnly.Count + secondOnly.Count,
                    "Overlapping revealed garbage must share and deduplicate their world-grid bars.");
                AssertNonIntersectingBars(combined);
                var expectedUnion = new List<Vector2>(firstOnly);
                for (int index = 0; index < secondOnly.Count; index += 4)
                {
                    if (ContainsBar(firstOnly, secondOnly, index, .02f)) continue;
                    for (int corner = 0; corner < 4; corner++)
                        expectedUnion.Add(secondOnly[index + corner]);
                }
                RequireGeometryEqual(expectedUnion, combined,
                    "The combined highlight must preserve the complete union of shared-grid cells without drawing duplicates.");

                // A nearly expired target must not reserve shared bars ahead
                // of an overlapping target that still has its steady reveal.
                Tick(first, 6.75f);
                Tick(second, .25f);
                byte weakAlpha = ((Color32)AreaTint(first)).a;
                byte strongAlpha = ((Color32)AreaTint(second)).a;
                Require(strongAlpha > weakAlpha + 80,
                    "The overlap-opacity fixture must contain visibly different fade strengths.");
                var renderedAlphas = new List<byte>();
                List<Vector2> opacityGeometry = CaptureScreenGeometry(f, overlay, renderedAlphas);
                RequireGeometryEqual(combined, opacityGeometry,
                    "Prioritizing the stronger reveal must preserve the deduplicated scene grid.");
                int sharedBarCount = 0;
                for (int index = 0; index < opacityGeometry.Count; index += 4)
                {
                    bool fromStrong = ContainsBar(secondOnly, opacityGeometry, index, .02f);
                    bool fromWeak = ContainsBar(firstOnly, opacityGeometry, index, .02f);
                    Require(fromStrong || fromWeak,
                        "Every rendered overlap bar must originate from one of the two envelopes.");
                    Require(renderedAlphas[index / 4] == (fromStrong ? strongAlpha : weakAlpha),
                        "Each shared bar must retain the strongest reveal opacity rather than inherit the fading target's tint.");
                    if (fromStrong && fromWeak) sharedBarCount++;
                }
                Require(sharedBarCount > 0,
                    "The overlap-opacity check must exercise duplicated bars from both targets.");

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
                        "Overlapping target cells must remain the same scene grid when the camera turns.");
                }
                f.Camera.transform.rotation = Quaternion.identity;
                foreach (float zoom in new[] { 3f, 5f, 8f })
                {
                    f.Camera.orthographicSize = zoom;
                    List<Vector2> zoomed = CaptureScreenGeometry(f, overlay);
                    Require(zoomed.Count >= 4, "Overlapping garbage must remain visible through scan zoom.");
                    AssertNonIntersectingBars(VisibleBarRectangles(zoomed));
                    RequireCenterlinesEqual(combinedWorld, CaptureWorldCenterlines(f, overlay),
                        "Camera zoom must retain the same deduplicated union of world-grid endpoints without suddenly adding or suppressing overlap cells.");
                }
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
            List<byte> barAlphas = null)
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
            Require(geometry.Count > 0, "At least one revealed area must render screen-pixel bars.");
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
                RequireNear((rectangles[index + 1] - rectangles[index]).magnitude, 1.4f + 2f,
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

        private static void AssertBrokenGridGeometry(List<Vector2> geometry,
            ScannableInteractable target, Vector2 direction, Vector2 latticeOrigin,
            bool requireBothDirections, float projectionScale = 1f)
        {
            Require(geometry.Count >= 4 && geometry.Count % 4 == 0,
                "An unclipped footprint must contain complete bars with four corners per bar.");
            Vector2 normal = new Vector2(-direction.y, direction.x);
            bool hasFirst = false, hasSecond = false;
            for (int index = 0; index < geometry.Count; index += 4)
            {
                Vector2 lengthEdge = geometry[index + 2] - geometry[index + 1];
                Vector2 widthEdge = geometry[index + 1] - geometry[index];
                bool first = Mathf.Abs(Vector2.Dot(lengthEdge.normalized, direction)) > .9999f;
                bool second = Mathf.Abs(Vector2.Dot(lengthEdge.normalized, normal)) > .9999f;
                Require(first || second,
                    "Every yellow bar must belong to one of the two perpendicular world-projected directions.");
                hasFirst |= first;
                hasSecond |= second;
                Require(Mathf.Abs(Vector2.Dot(lengthEdge.normalized, widthEdge.normalized)) < .0001f,
                    "Complete bar ends must remain perpendicular square caps rather than mask-cut fragments.");
                RequireNear(widthEdge.magnitude, target.LineWidthPixels + 2f,
                    "A complete bar must retain its authored width plus one antialias pixel per side.");
                float visibleLength = lengthEdge.magnitude - 2f;
                float projectedSpacing = target.LineSpacingPixels * projectionScale;
                float standardLength = (target.LineSpacingPixels - target.IntersectionGapPixels) * projectionScale;
                Require(visibleLength > 0f && visibleLength <= standardLength + .02f,
                    "Every bar must be a finite lattice unit; long mask-clipped boundary lines are not allowed.");
                bool fallback = geometry.Count == 4 && visibleLength < standardLength - .02f;
                if (fallback) continue;
                RequireNear(visibleLength, standardLength,
                    "A grid bar must span one cell minus the full open-intersection gap.");
                Vector2 center = (geometry[index] + geometry[index + 1]
                    + geometry[index + 2] + geometry[index + 3]) * .25f;
                Vector2 axis = first ? direction : normal;
                Vector2 across = new Vector2(-axis.y, axis.x);
                float alongCell = Vector2.Dot(center - latticeOrigin, axis) / projectedSpacing;
                float acrossCell = Vector2.Dot(center - latticeOrigin, across) / projectedSpacing;
                RequireNear(alongCell - .5f, Mathf.Round(alongCell - .5f),
                    "Short bars must sit midway between two shared-grid intersections.");
                RequireNear(acrossCell, Mathf.Round(acrossCell),
                    "Both families must share the same cached world lattice, with spacing projected continuously through the camera.");
            }
            Require(!requireBothDirections || (hasFirst && hasSecond),
                "A sufficiently large scanned area must contain both grid directions.");
            // The stroke and its antialias apron stay in screen pixels while
            // gaps scale with the scene. At zoom-out the aprons may overlap,
            // though the visible bars must still leave their crossing open.
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
                "The area must expose its presentation center for the shared scene grid.");
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

        private static bool ContainsBar(List<Vector2> geometry, List<Vector2> candidate,
            int candidateStart, float tolerance)
        {
            for (int index = 0; index < geometry.Count; index += 4)
                if (BarsMatch(geometry, index, candidate, candidateStart, tolerance)) return true;
            return false;
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
                        "A shared-grid bar must be drawn once even when two garbage areas overlap.");
                    bool separated = HasSeparatingAxis(geometry, first, second, first)
                        || HasSeparatingAxis(geometry, first, second, second);
                    Require(separated,
                        "Complete bars, including antialias padding, must not intersect or touch at a grid crossing.");
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

        private static void CheckSteadyAreaTint()
        {
            using (var fixture = new Fixture())
            {
                ScannableInteractable target = fixture.Target(Vector2.zero);
                target.Reveal();
                float initialAlpha = AreaTint(target).a;
                Tick(target, .6f);
                float steadyAlpha = AreaTint(target).a;
                Require(steadyAlpha > initialAlpha,
                    "Static highlights must gently fade in when first revealed.");
                foreach (float step in new[] { .6f, .9f, 1.7f, 2.2f })
                {
                    Tick(target, step);
                    RequireNear(AreaTint(target).a, steadyAlpha,
                        "The visible static area must maintain steady opacity without a periodic breathing cycle.");
                }
                Tick(target, .8f);
                Require(AreaTint(target).a < steadyAlpha,
                    "The static area must still fade out at the end of its reveal lifetime.");
            }
        }

        private static Color AreaTint(ScannableInteractable target) => (Color)typeof(BioScanStaticAreaUI)
            .GetMethod("GetAreaTint", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { target });

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
            }
        }

        private static void CheckShader()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/Resources/BioScanStaticArea.shader");
            Require(shader != null && !ShaderUtil.ShaderHasError(shader),
                "The finite grid-bar area shader must import without errors.");
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
            private readonly Texture2D bodyTexture;
            private readonly Sprite bodySprite;

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

            public ScannableInteractable Target(Vector2 position)
            {
                GameObject go = Create("Static scan validation target");
                go.transform.position = position;
                go.AddComponent<SpriteRenderer>().sprite = bodySprite;
                ScannableInteractable target = go.AddComponent<ScannableInteractable>();
                Set(target, "customNormalizedOutline", new[] { Vector2.left * .5f, Vector2.up * .5f,
                    Vector2.right * .5f, Vector2.down * .5f });
                Call(target, "OnValidate");
                // Preview-scene components may not receive play-mode callbacks.
                Call(target, "OnEnable");
                return target;
            }

            public void UpdateOverlay(BioScanStaticAreaUI overlay)
            {
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
                EditorSceneManager.ClosePreviewScene(Scene);
                Object.DestroyImmediate(bodySprite);
                Object.DestroyImmediate(bodyTexture);
            }
        }
    }
}
