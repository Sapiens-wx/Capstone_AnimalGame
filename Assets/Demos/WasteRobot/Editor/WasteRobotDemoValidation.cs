using System;
using System.Collections.Generic;
using System.Linq;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AnimalGame.Demos.WasteRobot.Editor
{
    /// <summary>
    /// Explicit Edit Mode checks. No initializer, asset rebuild, production-scene
    /// save or Input Manager repair is performed by this validator.
    /// Run in an isolated project copy: existing project initializers still run
    /// before any -executeMethod entry point is reached.
    /// </summary>
    public static class WasteRobotDemoValidation
    {
        private const string PlayerPrefabPath = "Assets/Prefabs/Resources/Robot/RobotMarker.prefab";
        private const string CameraPrefabPath = "Assets/Prefabs/Resources/Camera/RobotCamera.prefab";
        private const WorldInteractionKind SolidKinds = WorldInteractionKind.Collision | WorldInteractionKind.BodyCollision;
        private static readonly List<string> Passed = new List<string>();

        [MenuItem("Animal Game/Demos/Waste Robot/Validate")]
        public static void Run()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode,
                "Exit Play Mode before running Waste Robot validation.");
            Passed.Clear();
            Scene previousActiveScene = SceneManager.GetActiveScene();
            try
            {
                Check("authored prefab, artwork and segmented walls", CheckPrefab);
                Check("aligned sprite metadata, transforms and full world bounds", CheckAlignedArtwork);
                Check("fixed wheel and arm collision coordinates match both views", CheckAlignedFixedParts);
                Check("both interior bay corridors and surrounding solid walls", CheckInteriorBayPassages);
                Check("forward entry and reverse exit", CheckForwardAndReverse);
                Check("threshold dwell and hysteresis", CheckThresholdDwell);
                Check("diagonal and high-speed sweeps", CheckSweptCrossings);
                Check("crossing outside the entrance never enters", CheckOutsideAperture);
                Check("twenty complete entry/exit cycles", CheckRepeatedCrossings);
                Check("translated and rotated portal frame", CheckTransformedPortal);
                Check("authored scene and original player/camera references", CheckScene);
                Debug.Log("Waste Robot Demo validation PASS (" + Passed.Count + " groups):\n - "
                    + string.Join("\n - ", Passed)
                    + "\nEdit Mode geometry/controller validation only; live input and rendered Play Mode still require a smoke check.");
            }
            finally
            {
                // Preview fixtures own every temporary object. Restore the active
                // scene only if Unity changed it while opening a preview scene.
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded
                    && SceneManager.GetActiveScene() != previousActiveScene)
                    SceneManager.SetActiveScene(previousActiveScene);
                WorldInteractionQuery.InvalidateSpatialIndex();
            }
        }

        public static void RunBatch()
        {
            try
            {
                Run();
                if (Application.isBatchMode)
                    EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode)
                    EditorApplication.Exit(1);
                else
                    throw;
            }
        }

        private static void CheckPrefab()
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(WasteRobotDemoBuilder.PrefabPath);
            Require(asset != null, "Build the independent Waste Robot prefab first.");
            using (var fixture = new PreviewFixture(asset))
            {
                WasteRobotBase machine = fixture.Machine;
                Require(machine.ExteriorRoot != null && machine.InteriorRoot != null,
                    "Prefab must assign both exterior and interior roots.");
                Require(machine.ExteriorRoot != machine.InteriorRoot,
                    "Exterior and interior must be separate objects.");
                Require(machine.ExteriorRoot.transform.IsChildOf(machine.transform)
                    && machine.InteriorRoot.transform.IsChildOf(machine.transform),
                    "Both presentation roots must belong to the reusable machine prefab.");
                Require(!machine.ExteriorRoot.transform.IsChildOf(machine.InteriorRoot.transform)
                    && !machine.InteriorRoot.transform.IsChildOf(machine.ExteriorRoot.transform),
                    "Presentation roots must not contain each other.");
                Require(machine.EntranceWidth > 0f && machine.CrossingHysteresis > 0f,
                    "Portal width and hysteresis must be positive.");

                SpriteRenderer exteriorImage = LargestImage(machine.ExteriorRoot);
                SpriteRenderer interiorImage = LargestImage(machine.InteriorRoot);
                Require(exteriorImage.sprite.texture != interiorImage.sprite.texture,
                    "Exterior and interior must reference two different imported images.");
                foreach (SpriteRenderer renderer in new[] { exteriorImage, interiorImage })
                {
                    string path = AssetDatabase.GetAssetPath(renderer.sprite.texture);
                    Require(path.StartsWith(WasteRobotDemoBuilder.RootPath + "/", StringComparison.Ordinal),
                        "Demo artwork must be imported inside the independent Demo folder: " + path);
                    Require(renderer.sprite.texture.width >= 512 && renderer.sprite.texture.height >= 256,
                        "Demo image is missing or unexpectedly reduced in resolution: " + path);
                }

                WorldInteraction[] exteriorWalls = SolidWalls(machine.ExteriorRoot);
                WorldInteraction[] interiorWalls = SolidWalls(machine.InteriorRoot);
                machine.SetInside(false);
                CheckWallShapes(exteriorWalls, exteriorImage, "exterior");
                machine.SetInside(true);
                CheckWallShapes(interiorWalls, interiorImage, "interior");
                Require(exteriorWalls.Any(w => w.name.IndexOf("Wheel", StringComparison.OrdinalIgnoreCase) >= 0
                    || w.name.Contains("轮")), "At least one exterior wheel wall must be identified for validation.");
                Require(machine.GetComponentsInChildren<WorldInteraction>(true).All(w =>
                    w.transform.IsChildOf(machine.ExteriorRoot.transform)
                    || w.transform.IsChildOf(machine.InteriorRoot.transform)),
                    "A collision object outside the two switched roots would remain active in both views.");

                float bodyRadius = PlayerBodyRadius();
                Require(machine.EntranceWidth > bodyRadius * 2f,
                    "Entrance is narrower than the original player's body diameter.");

                // Deliberately do not globally invalidate between switches: this
                // exercises registration/removal through normal OnEnable/OnDisable.
                for (int i = 0; i < 4; i++)
                {
                    bool inside = (i & 1) != 0;
                    machine.SetInside(inside);
                    CheckExclusiveRoots(machine, inside);
                    WorldInteraction[] visible = inside ? interiorWalls : exteriorWalls;
                    WorldInteraction[] hidden = inside ? exteriorWalls : interiorWalls;
                    CheckActiveWalls(fixture.Scene, visible, hidden);
                    CheckEntranceClearance(machine, fixture.Scene, bodyRadius);
                }
            }
        }

        private static void CheckWallShapes(WorldInteraction[] walls, SpriteRenderer image, string group)
        {
            Require(walls.Length >= 4, group + " must use multiple explicit wall segments.");
            Bounds imageBounds = image.bounds;
            float imageArea = imageBounds.size.x * imageBounds.size.y;
            Require(imageArea > .01f, group + " artwork has no measurable world bounds.");
            foreach (WorldInteraction wall in walls)
            {
                Require((wall.Kind & WorldInteractionKind.Grabbable) == 0,
                    "Fixed machine walls must not be grabbable: " + wall.name);
                Require(wall.ColliderType != ColliderType.Sprite || wall.SpriteSource != image,
                    "The full background sprite must not be used as a solid wall: " + wall.name);
                Bounds shapeBounds = ShapeBounds(wall.GetShape(null));
                Require(shapeBounds.size.x > .00001f && shapeBounds.size.y > .00001f,
                    "Wall has zero-sized geometry: " + wall.name);
                Require(shapeBounds.size.x * shapeBounds.size.y < imageArea * .35f,
                    "Wall appears to block a large image-sized rectangle instead of a segment: " + wall.name);
            }
        }

        private static void CheckAlignedArtwork()
        {
            Require(WasteRobotCollisionLayout.ExteriorImageSize == new Vector2(1779f, 884f)
                && WasteRobotCollisionLayout.InteriorImageSize == new Vector2(1779f, 884f),
                "Both layouts must use the approved 1779 x 884 aligned canvas.");
            Vector2 approvedEntrance = new Vector2(991.5f, 205f);
            Require(WasteRobotCollisionLayout.ExteriorEntranceCenter == approvedEntrance
                && WasteRobotCollisionLayout.InteriorEntranceCenter == approvedEntrance,
                "Interior and exterior must use the same approved source-image entrance coordinate.");
            Require(Mathf.Approximately(WasteRobotCollisionLayout.ExteriorEntranceWidth, 289f)
                && Mathf.Approximately(WasteRobotCollisionLayout.InteriorEntranceWidth, 289f),
                "Both aligned images must retain the 289-pixel doorway width.");
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(WasteRobotDemoBuilder.PrefabPath);
            Require(asset != null, "Build the aligned Waste Robot prefab first.");
            using (var fixture = new PreviewFixture(asset))
            {
                WasteRobotBase machine = fixture.Machine;
                SpriteRenderer exterior = LargestImage(machine.ExteriorRoot);
                SpriteRenderer interior = LargestImage(machine.InteriorRoot);
                Rect fullCanvas = new Rect(0f, 0f, 1779f, 884f);
                foreach (SpriteRenderer renderer in new[] { exterior, interior })
                {
                    Require(renderer.sprite.rect == fullCanvas,
                        "Aligned sprite rect must include the complete canvas: " + renderer.name);
                    Require(renderer.sprite.texture.width == 1779 && renderer.sprite.texture.height == 884,
                        "Aligned source texture must retain its full approved resolution.");
                    Require(Mathf.Approximately(renderer.sprite.pixelsPerUnit, 100f),
                        "Aligned sprites must both use 100 pixels per unit.");
                    Require(Vector2.Distance(renderer.sprite.pivot, fullCanvas.size * .5f) < .0001f,
                        "Aligned sprites must share the complete-canvas center pivot.");
                }
                Require(Vector2.Distance(exterior.sprite.pivot, interior.sprite.pivot) < .0001f,
                    "The two sprite pivots differ.");
                RequireSameLocalTransform(exterior.transform, interior.transform, "image renderers");
                RequireSameLocalTransform(machine.ExteriorRoot.transform, machine.InteriorRoot.transform,
                    "presentation roots");
                machine.SetInside(false);
                Bounds exteriorBounds = exterior.bounds;
                machine.SetInside(true);
                Bounds interiorBounds = interior.bounds;
                Require(Vector3.Distance(exteriorBounds.center, interiorBounds.center) < .0001f
                    && Vector3.Distance(exteriorBounds.size, interiorBounds.size) < .0001f,
                    "Switching view changes the full artwork world bounds; canvas alignment has drifted.");
            }
        }

        private static void CheckAlignedFixedParts()
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(WasteRobotDemoBuilder.PrefabPath);
            Require(asset != null, "Build the aligned Waste Robot prefab first.");
            using (var fixture = new PreviewFixture(asset))
            {
                Dictionary<string, WorldInteraction> exterior = FixedParts(fixture.Machine.ExteriorRoot);
                Dictionary<string, WorldInteraction> interior = FixedParts(fixture.Machine.InteriorRoot);
                foreach (string family in new[] { "Wheel.", "UpperArm.", "LowerArm." })
                    Require(exterior.Keys.Any(key => key.StartsWith(family, StringComparison.Ordinal)),
                        "Exterior fixed-part regression fixtures are missing family " + family);
                foreach (string wheel in new[] { "UpperLeft", "UpperRight", "LowerLeft", "LowerRight" })
                    Require(exterior.ContainsKey("Wheel." + wheel),
                        "Aligned artwork must retain all four exterior wheel collision fixtures.");
                Require(exterior.Count == interior.Count && exterior.Keys.All(interior.ContainsKey),
                    "Interior is missing or adding a fixed wheel/arm collision part relative to exterior.");
                foreach (KeyValuePair<string, WorldInteraction> part in exterior)
                {
                    InteractionShape expected = part.Value.GetShape(null);
                    InteractionShape actual = interior[part.Key].GetShape(null);
                    Require(expected.IsBox == actual.IsBox && Mathf.Abs(expected.Radius - actual.Radius) < .0001f,
                        "Fixed-part primitive shape differs across presentations: " + part.Key);
                    if (expected.IsBox)
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            Vector2 vertex = expected.Vertex(i);
                            Require(Enumerable.Range(0, 4).Any(j => Vector2.Distance(vertex, actual.Vertex(j)) < .0001f),
                                "Fixed-part world coordinates differ across presentations: " + part.Key);
                        }
                    }
                    else
                    {
                        Require(Vector2.Distance(expected.A, actual.A) < .0001f
                            && Vector2.Distance(expected.B, actual.B) < .0001f,
                            "Fixed-part world coordinates differ across presentations: " + part.Key);
                    }
                }
            }
        }

        private static void CheckInteriorBayPassages()
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(WasteRobotDemoBuilder.PrefabPath);
            Require(asset != null, "Build the aligned Waste Robot prefab first.");
            using (var fixture = new PreviewFixture(asset))
            {
                WasteRobotBase machine = fixture.Machine;
                machine.SetInside(true);
                float radius = PlayerBodyRadius() + .02f;
                // These measured source-image lanes deliberately cross the two
                // compartment walls. A point query alone would miss body clipping.
                CheckBaySweep(machine, fixture.Scene, 340f, radius, false, "upper bay entrance");
                CheckBaySweep(machine, fixture.Scene, 588f, radius, false, "lower bay entrance");
                // An accidentally erased entire wall must not make the passage
                // test pass. Probe both shoulders with the same physical body.
                CheckBaySweep(machine, fixture.Scene, 245f, radius, true, "upper bay upper shoulder");
                CheckBaySweep(machine, fixture.Scene, 440f, radius, true, "upper bay lower shoulder");
                CheckBaySweep(machine, fixture.Scene, 510f, radius, true, "lower bay upper shoulder");
                CheckBaySweep(machine, fixture.Scene, 685f, radius, true, "lower bay lower shoulder");
            }
        }

        private static void CheckBaySweep(WasteRobotBase machine, Scene scene, float sourceY,
            float radius, bool shouldBlock, string label)
        {
            float pixelScale = machine.EntranceWidth / 289f;
            Vector2 entrance = new Vector2(991.5f, 205f);
            Vector2 sourceA = new Vector2(1340f, sourceY);
            Vector2 sourceB = new Vector2(1515f, sourceY);
            Vector2 a = machine.PortalToWorld(new Vector2(sourceA.x - entrance.x,
                entrance.y - sourceA.y) * pixelScale);
            Vector2 b = machine.PortalToWorld(new Vector2(sourceB.x - entrance.x,
                entrance.y - sourceB.y) * pixelScale);
            var hits = new List<WorldInteraction>();
            bool blocked = WorldInteractionQuery.Query(InteractionShape.Capsule(a, b, radius),
                SolidKinds, null, scene, hits);
            Require(blocked == shouldBlock,
                label + (shouldBlock ? " has no surrounding hard wall." : " is blocked for the original player's body.")
                + " Source path: " + sourceA + " -> " + sourceB + "; radius=" + radius.ToString("F3")
                + "; hits=" + string.Join(", ", hits.Select(hit => hit.name)));
            if (shouldBlock)
                Require(hits.Any(hit => hit.transform.IsChildOf(machine.InteriorRoot.transform)),
                    label + " was blocked by something other than the active interior walls.");
        }

        private static Dictionary<string, WorldInteraction> FixedParts(GameObject root)
        {
            var parts = new Dictionary<string, WorldInteraction>(StringComparer.Ordinal);
            foreach (WorldInteraction wall in SolidWalls(root))
            {
                foreach (string family in new[] { "Wheel.", "UpperArm.", "LowerArm." })
                {
                    int start = wall.name.IndexOf("." + family, StringComparison.Ordinal);
                    if (start < 0) continue;
                    string key = wall.name.Substring(start + 1);
                    Require(!parts.ContainsKey(key), "Duplicate fixed-part collision suffix: " + key);
                    parts.Add(key, wall);
                    break;
                }
            }
            return parts;
        }

        private static void RequireSameLocalTransform(Transform a, Transform b, string label)
        {
            Require(Vector3.Distance(a.localPosition, b.localPosition) < .0001f
                && Vector3.Distance(a.localScale, b.localScale) < .0001f
                && Quaternion.Angle(a.localRotation, b.localRotation) < .0001f,
                "The two " + label + " must share the same local position, rotation and scale.");
        }

        private static void CheckActiveWalls(Scene scene, WorldInteraction[] visible, WorldInteraction[] hidden)
        {
            var results = new List<WorldInteraction>();
            foreach (WorldInteraction wall in visible)
            {
                Require(wall.Available && WorldInteraction.Active.Contains(wall),
                    "Visible wall was not registered: " + wall.name);
                WorldInteractionQuery.Query(InteractionShape.Circle(wall.GetShape(null).Center, .01f),
                    SolidKinds, null, scene, results);
                Require(results.Contains(wall), "Visible wall did not block an actual interaction query: " + wall.name);
                Require(results.All(hit => Array.IndexOf(hidden, hit) < 0),
                    "Hidden presentation contributed a stale collision result.");
            }
            foreach (WorldInteraction wall in hidden)
            {
                Require(!wall.Available && !WorldInteraction.Active.Contains(wall),
                    "Hidden wall remained in the active registry: " + wall.name);
                WorldInteractionQuery.Query(InteractionShape.Circle(wall.GetShape(null).Center, .01f),
                    SolidKinds, null, scene, results);
                Require(!results.Contains(wall), "Hidden wall remained in the spatial index: " + wall.name);
            }
        }

        private static void CheckEntranceClearance(WasteRobotBase machine, Scene scene, float bodyRadius)
        {
            float halfTravel = Mathf.Max(.5f, machine.CrossingHysteresis * 3f);
            float handoff = machine.CrossingHysteresis + .02f;
            // The exterior stops participating as soon as entry crosses the hysteresis,
            // and the interior stops participating on exit. Do not require the old view's
            // machinery to stay clear far beyond the actual presentation handoff.
            float startY = machine.IsInside ? -halfTravel : halfTravel;
            float endY = machine.IsInside ? handoff : -handoff;
            // RobotMover.AppendBodyShape adds this same .02 world-unit collision margin.
            float collisionRadius = bodyRadius + .02f;
            float edge = machine.EntranceWidth * .5f - bodyRadius - .04f;
            Require(edge > 0f, "No clearance remains after allowing for the player's body.");
            foreach (float x in new[] { -edge, 0f, edge })
            {
                Vector2 localStart = new Vector2(x, startY);
                Vector2 localEnd = new Vector2(x, endY);
                Vector2 start = machine.PortalToWorld(localStart);
                Vector2 end = machine.PortalToWorld(localEnd);
                InteractionShape sweep = InteractionShape.Capsule(start, end, collisionRadius);
                var hits = new List<WorldInteraction>();
                bool blocked = WorldInteractionQuery.Query(sweep, SolidKinds, null, scene, hits);
                string details = blocked ? string.Join("; ", hits.Select(wall =>
                {
                    InteractionShape shape = wall.GetShape(null);
                    Bounds bounds = ShapeBounds(shape);
                    return wall.name + " [penetration=" + WorldInteractionQuery.Penetration(sweep, shape).ToString("F4")
                        + ", localCenter=" + machine.WorldToPortal(shape.Center).ToString("F3")
                        + ", worldMin=" + bounds.min.ToString("F3")
                        + ", worldMax=" + bounds.max.ToString("F3") + "]";
                })) : "none";
                Require(!blocked,
                    "A wall seals the entrance corridor in " + (machine.IsInside ? "interior" : "exterior")
                    + " view at local x=" + x.ToString("F3")
                    + "; local path=" + localStart.ToString("F3") + " -> " + localEnd.ToString("F3")
                    + "; world path=" + start.ToString("F3") + " -> " + end.ToString("F3")
                    + "; body collision radius=" + collisionRadius.ToString("F3")
                    + "; hits=" + details + ".");
            }
        }

        private static void CheckForwardAndReverse()
        {
            using (var fixture = new PreviewFixture())
            {
                fixture.Start(new Vector2(0f, 1f), false);
                fixture.Target.rotation = Quaternion.Euler(0f, 0f, 180f);
                fixture.Move(new Vector2(0f, .3f));
                Require(!fixture.Machine.IsInside, "Approaching the entrance entered early.");
                fixture.Move(new Vector2(0f, -.4f));
                Require(fixture.Machine.IsInside && fixture.Machine.TransitionCount == 1,
                    "Moving forward through the entrance did not enter exactly once.");
                fixture.Move(new Vector2(0f, .4f));
                Require(!fixture.Machine.IsInside && fixture.Machine.TransitionCount == 2,
                    "Reversing out while facing inward did not exit exactly once.");
                Require(Quaternion.Angle(fixture.Target.rotation, Quaternion.Euler(0f, 0f, 180f)) < .001f,
                    "Portal observation must not turn the original player.");
                fixture.Machine.SetInside(false);
                Require(fixture.Machine.TransitionCount == 2, "Setting the current state counted a false transition.");
                fixture.Machine.SetInside(true);
                Require(fixture.Machine.TransitionCount == 3, "An explicit real state change was not counted.");
            }
        }

        private static void CheckThresholdDwell()
        {
            using (var fixture = new PreviewFixture())
            {
                fixture.Start(new Vector2(0f, 1f), false);
                float h = fixture.Machine.CrossingHysteresis;
                foreach (float y in new[] { h * .5f, 0f, -h * .4f, h * .3f, 0f, -h * .5f })
                {
                    fixture.Move(new Vector2(0f, y));
                    Require(!fixture.Machine.IsInside && fixture.Machine.TransitionCount == 0,
                        "Stopping or jittering on the threshold caused an early switch.");
                }
                fixture.Move(new Vector2(0f, -h * 2f));
                Require(fixture.Machine.IsInside && fixture.Machine.TransitionCount == 1,
                    "A valid pending crossing was lost before reaching the stable interior side.");
                foreach (float y in new[] { -h * .3f, 0f, h * .4f, -h * .2f, h * .5f })
                {
                    fixture.Move(new Vector2(0f, y));
                    Require(fixture.Machine.IsInside && fixture.Machine.TransitionCount == 1,
                        "Threshold dwell toggled the interior back to exterior.");
                }
                fixture.Move(new Vector2(0f, h * 2f));
                Require(!fixture.Machine.IsInside && fixture.Machine.TransitionCount == 2,
                    "Completing the outward crossing did not exit exactly once.");
            }
        }

        private static void CheckSweptCrossings()
        {
            using (var fixture = new PreviewFixture())
            {
                float x = fixture.Machine.EntranceWidth * .3f;
                fixture.Start(new Vector2(-x, 1f), false);
                fixture.Move(new Vector2(x, -1f));
                Require(fixture.Machine.IsInside && fixture.Machine.TransitionCount == 1,
                    "A diagonal segment through the aperture did not enter.");
            }
            using (var fixture = new PreviewFixture())
            {
                fixture.Start(new Vector2(0f, 8f), false);
                fixture.Move(new Vector2(0f, -8f));
                Require(fixture.Machine.IsInside && fixture.Machine.TransitionCount == 1,
                    "A high-speed segment skipped the entrance.");
                fixture.Move(new Vector2(0f, 8f));
                Require(!fixture.Machine.IsInside && fixture.Machine.TransitionCount == 2,
                    "A high-speed return segment skipped the exit.");
            }
        }

        private static void CheckOutsideAperture()
        {
            using (var fixture = new PreviewFixture())
            {
                float x = fixture.Machine.EntranceWidth * .5f + .3f;
                fixture.Start(new Vector2(x, 1f), false);
                fixture.Move(new Vector2(x, -1f));
                Require(!fixture.Machine.IsInside && fixture.Machine.TransitionCount == 0,
                    "Crossing the infinite portal plane outside the aperture entered the machine.");
                fixture.Move(new Vector2(0f, -1f));
                Require(!fixture.Machine.IsInside, "Moving under the doorway without crossing entered the machine.");
                fixture.Move(new Vector2(0f, 1f));
                Require(!fixture.Machine.IsInside && fixture.Machine.TransitionCount == 0,
                    "An outward crossing while already outside caused an inverse entry.");
                fixture.Move(new Vector2(0f, -1f));
                Require(fixture.Machine.IsInside && fixture.Machine.TransitionCount == 1,
                    "The valid entrance stopped working after an out-of-aperture crossing.");
            }
        }

        private static void CheckRepeatedCrossings()
        {
            using (var fixture = new PreviewFixture())
            {
                fixture.Start(new Vector2(0f, 1f), false);
                for (int i = 0; i < 20; i++)
                {
                    fixture.Move(new Vector2(0f, -1f));
                    CheckExclusiveRoots(fixture.Machine, true);
                    fixture.Move(new Vector2(0f, 1f));
                    CheckExclusiveRoots(fixture.Machine, false);
                }
                Require(fixture.Machine.TransitionCount == 40, "Twenty full visits did not produce forty transitions.");
            }
        }

        private static void CheckTransformedPortal()
        {
            using (var fixture = new PreviewFixture())
            {
                fixture.Machine.transform.SetPositionAndRotation(new Vector3(23f, -17f, 0f),
                    Quaternion.Euler(0f, 0f, 37f));
                foreach (Vector2 point in new[] { Vector2.zero, new Vector2(.6f, 1.2f), new Vector2(-.3f, -1.6f) })
                    Require(Vector2.Distance(fixture.Machine.WorldToPortal(fixture.Machine.PortalToWorld(point)), point) < .0001f,
                        "Portal coordinate conversion failed after translation/rotation.");
                fixture.Start(new Vector2(0f, 1f), false);
                fixture.Move(new Vector2(.2f, -.8f));
                Require(fixture.Machine.IsInside, "Rotated portal did not recognize entry.");
                fixture.Move(new Vector2(-.2f, .8f));
                Require(!fixture.Machine.IsInside && fixture.Machine.TransitionCount == 2,
                    "Rotated portal did not recognize exit.");
            }
        }

        private static void CheckScene()
        {
            Require(AssetDatabase.LoadAssetAtPath<SceneAsset>(WasteRobotDemoBuilder.ScenePath) != null,
                "Build the independent Demo scene first.");
            Scene scene = EditorSceneManager.OpenPreviewScene(WasteRobotDemoBuilder.ScenePath);
            try
            {
                var machines = ComponentsInScene<WasteRobotBase>(scene);
                var players = ComponentsInScene<RobotMover>(scene);
                var follows = ComponentsInScene<RobotCameraFollow>(scene);
                Require(machines.Length == 1 && players.Length == 1 && follows.Length == 1,
                    "Demo scene must contain exactly one machine, original player and follow camera.");
                Camera camera = follows[0].GetComponent<Camera>();
                Require(camera != null && camera.orthographic, "Demo must use the original orthographic camera.");
                Require(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(players[0].gameObject) == PlayerPrefabPath,
                    "Demo player is not an instance of the original RobotMarker prefab.");
                Require(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(camera.gameObject) == CameraPrefabPath,
                    "Demo camera is not an instance of the original RobotCamera prefab.");
                Require(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(machines[0].gameObject)
                    == WasteRobotDemoBuilder.PrefabPath, "Demo scene must instance the reusable machine prefab.");

                // This preview is reopened from disk, so these assertions verify persisted
                // prefab-instance overrides rather than values left in the builder's memory.
                Vector2 expectedSpawn = machines[0].PortalToWorld(new Vector2(0f, 2f));
                Require(Vector3.Distance(players[0].transform.position, (Vector3)expectedSpawn) < .0001f,
                    "Saved Demo player spawn must remain two portal units outside the entrance.");
                Require(Quaternion.Angle(players[0].transform.rotation,
                    Quaternion.Euler(0f, 0f, 180f)) < .001f,
                    "Saved Demo player rotation must remain Z=180 degrees, facing the entrance.");
                Require(Mathf.Abs(camera.orthographicSize - 8f) < .0001f,
                    "Saved Demo camera orthographic size must remain 8.");
                Require(camera.CompareTag("MainCamera"),
                    "Saved Demo camera must retain the MainCamera tag.");
                Require(camera.clearFlags == CameraClearFlags.SolidColor,
                    "Saved Demo camera must retain SolidColor clearing.");
                var followSettings = new SerializedObject(follows[0]);
                SerializedProperty followRotation = followSettings.FindProperty("followTargetRotation");
                Require(followRotation != null && !followRotation.boolValue,
                    "Saved Demo follow camera must not rotate with the player.");
                foreach (string typeName in new[] { "PhotoModeController", "BioScanController" })
                {
                    Behaviour[] controllers = players[0].GetComponents<Behaviour>()
                        .Where(component => component != null && component.GetType().Name == typeName)
                        .ToArray();
                    Require(controllers.Length == 1 && !controllers[0].enabled,
                        "Saved Demo player must retain exactly one disabled " + typeName + ".");
                }
                Require(machines[0].Target == players[0].transform,
                    "Saved Demo machine target must reference its own player instance.");

                MonoBehaviour bootstrap = ComponentsInScene<MonoBehaviour>(scene)
                    .SingleOrDefault(component => component != null && component.GetType().FullName
                        == "AnimalGame.Demos.WasteRobot.WasteRobotDemoScene");
                Require(bootstrap != null, "Demo scene is missing its explicit local bootstrap.");
                var settings = new SerializedObject(bootstrap);
                RequireReference(settings, "machine", machines[0], scene);
                RequireReference(settings, "player", players[0], scene);
                RequireReference(settings, "demoCamera", camera, scene);
                Require(!ComponentsInScene<MonoBehaviour>(scene).Any(component => component != null
                    && component.GetType().Name == "HeightMapPlayerSceneBootstrap"),
                    "This independent flat-ground Demo must not instantiate a map bootstrap.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static void RequireReference(SerializedObject owner, string field, Component expected, Scene scene)
        {
            SerializedProperty property = owner.FindProperty(field);
            Require(property != null && property.objectReferenceValue == expected,
                "Demo bootstrap reference '" + field + "' is missing or points to the wrong instance.");
            Require(expected.gameObject.scene == scene && !EditorUtility.IsPersistent(expected),
                "Demo bootstrap reference '" + field + "' must resolve inside its own scene.");
        }

        private static T[] ComponentsInScene<T>(Scene scene) where T : Component
        {
            return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
        }

        private static SpriteRenderer LargestImage(GameObject root)
        {
            SpriteRenderer renderer = root.GetComponentsInChildren<SpriteRenderer>(true)
                .Where(image => image.sprite != null)
                .OrderByDescending(image => image.sprite.rect.width * image.sprite.rect.height)
                .FirstOrDefault();
            Require(renderer != null, "Missing imported artwork under " + root.name + ".");
            return renderer;
        }

        private static WorldInteraction[] SolidWalls(GameObject root)
        {
            return root.GetComponentsInChildren<WorldInteraction>(true)
                .Where(wall => (wall.Kind & SolidKinds) != 0).ToArray();
        }

        private static float PlayerBodyRadius()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Require(prefab != null, "Original player prefab is missing.");
            RobotMarkerView marker = prefab.GetComponent<RobotMarkerView>();
            Require(marker != null, "Original player has no body size reference.");
            return marker.BodyDiameter * .5f * Mathf.Max(Mathf.Abs(prefab.transform.lossyScale.x),
                Mathf.Abs(prefab.transform.lossyScale.y));
        }

        private static Bounds ShapeBounds(InteractionShape shape)
        {
            if (!shape.IsBox)
            {
                Vector2 min = Vector2.Min(shape.A, shape.B) - Vector2.one * shape.Radius;
                Vector2 max = Vector2.Max(shape.A, shape.B) + Vector2.one * shape.Radius;
                return new Bounds((min + max) * .5f, max - min);
            }
            var bounds = new Bounds(shape.A, Vector3.zero);
            for (int i = 1; i < 4; i++)
                bounds.Encapsulate((Vector3)shape.Vertex(i));
            return bounds;
        }

        private static void CheckExclusiveRoots(WasteRobotBase machine, bool inside)
        {
            Require(machine.IsInside == inside && machine.InteriorRoot.activeSelf == inside
                && machine.ExteriorRoot.activeSelf != inside,
                "Inside/outside state and presentation roots are not mutually exclusive.");
        }

        private static void Check(string label, Action test)
        {
            test();
            Passed.Add(label);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException("Waste Robot Demo: " + message);
        }

        private sealed class PreviewFixture : IDisposable
        {
            public readonly Scene Scene;
            public readonly WasteRobotBase Machine;
            public readonly Transform Target;

            public PreviewFixture(GameObject prefab = null)
            {
                Scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    GameObject root;
                    if (prefab != null)
                    {
                        root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, Scene);
                        Machine = root.GetComponent<WasteRobotBase>();
                        Require(Machine != null, "Machine prefab has no WasteRobotBase controller.");
                    }
                    else
                    {
                        root = new GameObject("Waste Robot validation fixture");
                        SceneManager.MoveGameObjectToScene(root, Scene);
                        GameObject exterior = new GameObject("Exterior");
                        exterior.transform.SetParent(root.transform, false);
                        GameObject interior = new GameObject("Interior");
                        interior.transform.SetParent(root.transform, false);
                        Machine = root.AddComponent<WasteRobotBase>();
                        Machine.Configure(exterior, interior, 2.36f);
                    }
                    var targetObject = new GameObject("Portal validation target");
                    SceneManager.MoveGameObjectToScene(targetObject, Scene);
                    Target = targetObject.transform;
                }
                catch
                {
                    EditorSceneManager.ClosePreviewScene(Scene);
                    throw;
                }
            }

            public void Start(Vector2 localPosition, bool inside)
            {
                Target.position = Machine.PortalToWorld(localPosition);
                int before = Machine.TransitionCount;
                Machine.Bind(Target, inside);
                Require(Machine.TransitionCount == before, "Binding the initial sample counted a traversal.");
                CheckExclusiveRoots(Machine, inside);
            }

            public void Move(Vector2 localPosition)
            {
                Vector2 world = Machine.PortalToWorld(localPosition);
                Target.position = world;
                Machine.ObserveWorldPosition(world);
                Require(Vector2.Distance(Target.position, world) < .00001f,
                    "Portal observation teleported the player.");
            }

            public void Dispose()
            {
                if (Scene.IsValid() && Scene.isLoaded)
                    EditorSceneManager.ClosePreviewScene(Scene);
            }
        }
    }
}
