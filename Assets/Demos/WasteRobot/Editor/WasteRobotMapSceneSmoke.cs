using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AnimalGame.Demos.WasteRobot.Editor
{
    /// <summary>
    /// Explicit isolated-project batch test of the user's saved map instance.
    /// Never use -quit: the transient session survives the Play Mode domain reload
    /// and exits after returning to Edit Mode. No scene, prefab or settings are saved.
    /// </summary>
    public sealed class WasteRobotMapSceneSmoke : ScriptableObject
    {
        private const string ScenePath = "Assets/Scenes/GameMaps/Official_Map_Test_02.unity";
        private const string SourceProject = "C:/Users/30791/Documents/GitHub/Capstone_AnimalGame";
        private const double TotalTimeout = 180d;
        private const float OutsideY = 1f;
        private const float InsideY = -.65f;
        private const float Throttle = .65f;
        private const WorldInteractionKind Solids = WorldInteractionKind.Collision | WorldInteractionKind.BodyCollision;
        private enum Phase { WaitingForPlay, WaitingForPlayer, WaitingForAutoBind, ChoosePath, SettleOutside, DriveIn, StopInside, DriveOut, StopOutside, Finishing }

        [Serializable]
        private sealed class TransformSnapshot
        {
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 localScale;
            public float rotationZ;
            public static TransformSnapshot Read(Transform value) => new TransformSnapshot
            {
                position = value.position,
                rotation = value.rotation,
                localScale = value.localScale,
                rotationZ = Mathf.DeltaAngle(0f, value.eulerAngles.z)
            };
        }

        [Serializable]
        private sealed class Report
        {
            public string startedUtc;
            public string finishedUtc;
            public string scene = ScenePath;
            public string isolatedProject;
            public string sceneHashBefore;
            public string sceneHashAfter;
            public bool passed;
            public bool savedTargetWasEmpty;
            public bool originalLocationWasSuitable;
            public bool usedFallbackLocation;
            public int candidateAttempts;
            public TransformSnapshot originalMachine;
            public TransformSnapshot fixtureMachine;
            public Vector3 originalPlayerPosition;
            public Vector3 fixturePlayerPosition;
            public List<string> checks = new List<string>();
            public List<string> notes = new List<string>();
            public List<string> errors = new List<string>();
            public List<string> screenshots = new List<string>();
        }

        [SerializeField] private bool running;
        [SerializeField] private Phase phase;
        [SerializeField] private Report report;
        [SerializeField] private string reportDirectory;
        [SerializeField] private double deadline;
        [SerializeField] private double phaseStarted;
        [SerializeField] private int phaseFrame;
        [SerializeField] private int lastFrame = -1;
        [SerializeField] private int gamepadId;
        [SerializeField] private HeightMapPlayerSceneBootstrap bootstrap;
        [SerializeField] private MapTestSceneController map;
        [SerializeField] private HeightMapTraversalEvaluator evaluator;
        [SerializeField] private RobotMover player;
        [SerializeField] private Camera mapCamera;
        [SerializeField] private WasteRobotBase machine;
        [SerializeField] private Vector2 driveStart;
        [SerializeField] private int initialTransitions;

        public static void RunBatch()
        {
            try
            {
                Require(Application.isBatchMode, "This entry point is batch-only.");
                Require(!Environment.GetCommandLineArgs().Any(argument => string.Equals(argument, "-quit", StringComparison.OrdinalIgnoreCase)),
                    "Do not pass -quit; the asynchronous map smoke exits after Play Mode.");
                string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                Require(!SamePath(project, SourceProject), "Refusing to run in the original user project. Use the isolated validation mirror.");
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Start in Edit Mode.");
                Require(!Resources.FindObjectsOfTypeAll<WasteRobotMapSceneSmoke>().Any(session => session.running),
                    "A map smoke session is already running.");
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    Require(!SceneManager.GetSceneAt(i).isDirty, "Refusing to replace an unsaved scene in the validation project.");
                Require(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null, "The saved user map has not been copied into the validation mirror.");
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                WasteRobotBase original = FindMachine();
                var session = CreateInstance<WasteRobotMapSceneSmoke>();
                session.hideFlags = HideFlags.HideAndDontSave;
                session.reportDirectory = OutputDirectory();
                Directory.CreateDirectory(session.reportDirectory);
                session.report = new Report
                {
                    startedUtc = DateTime.UtcNow.ToString("O"),
                    isolatedProject = project,
                    sceneHashBefore = SceneHash(),
                    originalMachine = TransformSnapshot.Read(original.transform),
                    savedTargetWasEmpty = original.Target == null
                };
                Require(session.report.savedTargetWasEmpty, "The saved map fixture must exercise an initially empty player target.");
                session.report.notes.Add("No Bind, SetInside, ObserveWorldPosition, DemoScene bootstrap, movement override or evaluator replacement is used. Start-pose relocation is confined to Play Mode fixture setup.");
                session.report.notes.Add("Original machine pose is recorded exactly; only its position may be moved while finding a safe fixture. Scale and rotation remain authored by the user.");
                session.running = true;
                session.deadline = EditorApplication.timeSinceStartup + TotalTimeout;
                session.Begin(Phase.WaitingForPlay);
                session.Subscribe();
                session.SaveReport();
                EditorApplication.isPlaying = true;
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        private void OnEnable() { if (running) Subscribe(); }
        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
        }
        private void Subscribe()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
        }

        private void Tick()
        {
            if (!running) return;
            try
            {
                if (phase == Phase.Finishing)
                {
                    if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode) ExitBatch();
                    else if (EditorApplication.timeSinceStartup > deadline)
                    {
                        report.errors.Add("Timed out leaving Play Mode.");
                        ExitBatch();
                    }
                    return;
                }
                Require(EditorApplication.timeSinceStartup <= deadline, "The map smoke exceeded its 180-second total timeout.");
                EditorApplication.QueuePlayerLoopUpdate();
                if (!EditorApplication.isPlaying)
                {
                    Require(phase == Phase.WaitingForPlay || EditorApplication.isPlayingOrWillChangePlaymode,
                        "Play Mode ended before the map smoke finished.");
                    return;
                }
                if (lastFrame == Time.frameCount) return;
                lastFrame = Time.frameCount;
                switch (phase)
                {
                    case Phase.WaitingForPlay:
                        Begin(Phase.WaitingForPlayer);
                        break;
                    case Phase.WaitingForPlayer:
                        WaitForRealPlayer();
                        break;
                    case Phase.WaitingForAutoBind:
                        Drive(0f);
                        RequireElapsed(20d, "The user map machine did not automatically bind the real map player.");
                        if (machine.Target == player.transform)
                        {
                            CheckRealMapReferences();
                            Require(!machine.IsInside, "The map fixture must begin in the exterior presentation.");
                            initialTransitions = machine.TransitionCount;
                            Record(true, "The originally target-empty map instance automatically bound HeightMapPlayerSceneBootstrap.robot.");
                            CheckMapBodyClearance();
                            CheckPresentation(false);
                            Begin(Phase.ChoosePath);
                        }
                        break;
                    case Phase.ChoosePath:
                        Drive(0f);
                        ChooseSafePath();
                        Begin(Phase.SettleOutside);
                        break;
                    case Phase.SettleOutside:
                        Drive(0f);
                        CheckRealMapReferences();
                        RequireElapsed(15d, "The real map player did not settle at the selected outside start.");
                        Require(!machine.IsInside && machine.TransitionCount == initialTransitions,
                            "Fixture placement, rather than real driving, changed the entrance state.");
                        if (Stopped())
                        {
                            Require(machine.WorldToPortal(player.transform.position).y > .5f,
                                "The selected outside approach drifted too close to the threshold before driving.");
                            Capture("map-exterior-play.png");
                            driveStart = player.transform.position;
                            Begin(Phase.DriveIn);
                            Drive(Throttle);
                        }
                        break;
                    case Phase.DriveIn:
                        Drive(Throttle);
                        CheckRealMapReferences();
                        RequireElapsed(35d, "The real map player could not drive through the machine entrance. Last traversal: " + TraversalSummary(player.CurrentTraversalResult));
                        Require(Mathf.Abs(machine.WorldToPortal(player.transform.position).x) < .65f,
                            "Terrain or steering took the map player outside the selected entrance lane.");
                        if (machine.IsInside && machine.WorldToPortal(player.transform.position).y <= InsideY)
                        {
                            Record(Vector2.Distance(driveStart, player.transform.position) > .8f,
                                "Virtual forward input physically drove the original map player through the entrance.");
                            Record(machine.TransitionCount == initialTransitions + 1, "Real map entry produced exactly one transition.");
                            CheckPresentation(true);
                            Begin(Phase.StopInside);
                            Drive(0f);
                        }
                        break;
                    case Phase.StopInside:
                        Drive(0f);
                        CheckRealMapReferences();
                        RequireElapsed(15d, "The map player did not settle inside.");
                        Require(machine.IsInside, "The interior state changed while the player was stopping.");
                        if (Stopped())
                        {
                            Capture("map-interior-play.png");
                            driveStart = player.transform.position;
                            Begin(Phase.DriveOut);
                            Drive(-Throttle);
                        }
                        break;
                    case Phase.DriveOut:
                        Drive(-Throttle);
                        CheckRealMapReferences();
                        RequireElapsed(35d, "The real map player could not reverse out. Last traversal: " + TraversalSummary(player.CurrentTraversalResult));
                        if (!machine.IsInside && machine.WorldToPortal(player.transform.position).y >= .65f)
                        {
                            Record(Vector2.Distance(driveStart, player.transform.position) > .8f,
                                "Virtual reverse input physically returned the original map player to the exterior.");
                            Record(machine.TransitionCount == initialTransitions + 2, "The map round trip produced exactly two entrance transitions.");
                            CheckPresentation(false);
                            Begin(Phase.StopOutside);
                            Drive(0f);
                        }
                        break;
                    case Phase.StopOutside:
                        Drive(0f);
                        CheckRealMapReferences();
                        RequireElapsed(15d, "The map player did not settle after exiting.");
                        if (Stopped())
                        {
                            Record(report.screenshots.Any(path => path.EndsWith("map-interior-play.png", StringComparison.Ordinal)),
                                "An actual interior screenshot was saved from the original map camera.");
                            Finish(null);
                        }
                        break;
                }
            }
            catch (Exception error) { Finish(error); }
        }

        private void WaitForRealPlayer()
        {
            RequireElapsed(75d, "The saved map bootstrap did not produce its real player, tiled map, evaluator and camera.");
            bootstrap = Object.FindObjectsByType<HeightMapPlayerSceneBootstrap>(FindObjectsSortMode.None)
                .FirstOrDefault(value => value.gameObject.scene.path == ScenePath);
            if (bootstrap == null || bootstrap.robot == null || bootstrap.map == null || !bootstrap.map.HasGeneratedMap
                || bootstrap.traversalEvaluator == null || !bootstrap.traversalEvaluator.IsInitialized || bootstrap.mapCamera == null) return;
            player = bootstrap.robot;
            map = bootstrap.map;
            evaluator = bootstrap.traversalEvaluator;
            mapCamera = bootstrap.mapCamera;
            machine = FindMachine();
            Require(player.isActiveAndEnabled && mapCamera.isActiveAndEnabled, "The real player or map camera is disabled.");
            Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "This map smoke requires an interior screenshot; run without -nographics.");
            CheckRealMapReferences(false);
            CheckAuthoredScaleAndRotation();
            report.originalPlayerPosition = player.transform.position;
            var gamepad = InputSystem.AddDevice<Gamepad>("Waste Robot map smoke virtual controller");
            gamepad.MakeCurrent();
            gamepadId = gamepad.deviceId;
            Drive(0f);
            Record(true, "The original HeightMapPlayerSceneBootstrap generated and retained the map player, evaluator and camera.");
            Begin(Phase.WaitingForAutoBind);
        }

        private void CheckRealMapReferences(bool checkTarget = true)
        {
            Require(bootstrap != null && bootstrap.robot == player && bootstrap.map == map
                && bootstrap.traversalEvaluator == evaluator && bootstrap.mapCamera == mapCamera,
                "The original map bootstrap references changed during the smoke.");
            Require(map.HasGeneratedMap && evaluator.IsInitialized && evaluator.Map == map,
                "The original terrain evaluator was cleared or disconnected from its tiled map.");
            FieldInfo field = typeof(RobotMover).GetField("traversalEvaluator", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(field != null && ReferenceEquals(field.GetValue(player), evaluator),
                "The real RobotMover no longer uses its original terrain evaluator.");
            Require(machine.gameObject.scene == player.gameObject.scene && player.gameObject.scene == map.gameObject.scene,
                "The user machine and map player must stay in their original shared scene.");
            if (checkTarget) Require(machine.Target == player.transform, "The auto-bound player target changed.");
            CheckAuthoredScaleAndRotation();
        }

        private void CheckAuthoredScaleAndRotation()
        {
            Require(Vector3.Distance(machine.transform.localScale, report.originalMachine.localScale) < .0001f
                && Quaternion.Angle(machine.transform.rotation, report.originalMachine.rotation) < .001f,
                "The fixture must preserve the user's machine scale and rotation.");
        }

        private void CheckMapBodyClearance()
        {
            float x = Mathf.Abs(map.MapMetersToWorldDistance(Vector2.right, 1f));
            float y = Mathf.Abs(map.MapMetersToWorldDistance(Vector2.up, 1f));
            Vector2 right = machine.transform.right;
            float expected = (evaluator.RobotObstacleCollisionRadiusMeters + .02f)
                * Mathf.Sqrt(right.x * right.x * x * x + right.y * right.y * y * y);
            Record(Mathf.Abs(machine.BodyClearanceWorld - expected) < .0001f,
                "Portal body clearance uses the real map-metres-to-world projection, not the visual robot diameter.");
            report.notes.Add("Map portal clearance world=" + machine.BodyClearanceWorld.ToString("F6", CultureInfo.InvariantCulture)
                + "; expected=" + expected.ToString("F6", CultureInfo.InvariantCulture) + ".");
            Vector3 savedScale = machine.transform.localScale;
            try
            {
                machine.transform.localScale = Vector3.one * .25f;
                float halfClear = machine.EntranceWidth * .5f
                    - machine.BodyClearanceWorld / Mathf.Abs(machine.transform.lossyScale.x);
                Record(halfClear > 0f, "At temporary scale 0.25, map-aware portal clearance still leaves a positive opening.");
            }
            finally
            {
                machine.transform.localScale = savedScale;
                WorldInteraction.MarkHierarchySpatialDirty(machine.transform);
            }
            CheckAuthoredScaleAndRotation();
        }

        private void ChooseSafePath()
        {
            CheckRealMapReferences();
            Vector3 original = report.originalMachine.position;
            var candidates = new List<Vector3> { original };
            if (map.TrySampleWorldPosition(player.transform.position, out Vector2 playerMapPosition, out _))
            {
                Vector3 approachOffset = machine.transform.TransformVector(new Vector3(0f, OutsideY, 0f));
                candidates.Add(map.MapPositionToWorld(playerMapPosition) - approachOffset);
                foreach (float radius in new[] { 6f, 12f, 24f, 40f, 64f, 96f })
                    for (int direction = 0; direction < 8; direction++)
                    {
                        float angle = direction * Mathf.PI * .25f;
                        Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                        candidates.Add(map.MapPositionToWorld(playerMapPosition + offset) - approachOffset);
                    }
            }
            bool selected = false;
            for (int i = 0; i < candidates.Count; i++)
            {
                Require(EditorApplication.timeSinceStartup <= deadline, "Map fixture search exhausted the total timeout.");
                Vector3 position = candidates[i];
                position.z = original.z;
                machine.transform.position = position;
                WorldInteraction.MarkHierarchySpatialDirty(machine.transform);
                report.candidateAttempts++;
                bool suitable = CandidateIsSuitable(out string details);
                report.notes.Add((i == 0 ? "Original authored location" : "Fallback candidate " + i)
                    + " at " + position.ToString("F3") + ": " + details);
                if (i == 0) report.originalLocationWasSuitable = suitable;
                if (!suitable) continue;
                report.usedFallbackLocation = i > 0;
                selected = true;
                break;
            }
            Require(selected, "No nearby flat, bidirectionally passable map entrance path was found; the terrain evaluator was not bypassed.");
            Vector2 start = machine.PortalToWorld(new Vector2(0f, OutsideY));
            Vector3 inward = machine.transform.TransformDirection(Vector3.down);
            player.transform.SetPositionAndRotation(new Vector3(start.x, start.y, player.transform.position.z),
                Quaternion.Euler(0f, 0f, Mathf.Atan2(inward.y, inward.x) * Mathf.Rad2Deg - 90f));
            bootstrap.cameraFollow?.SnapToTarget();
            report.fixtureMachine = TransformSnapshot.Read(machine.transform);
            report.fixturePlayerPosition = player.transform.position;
            Record(true, "The selected short entrance lane passed the real map evaluator in both directions and the real solid-body sweep.");
            SaveReport();
        }

        private bool CandidateIsSuitable(out string details)
        {
            // Include coasting margin beyond both actual stopping targets.
            Vector2 start = machine.PortalToWorld(new Vector2(0f, OutsideY + .2f));
            Vector2 end = machine.PortalToWorld(new Vector2(0f, InsideY - .2f));
            if (!map.TrySampleWorldPosition(start, out Vector2 startMap, out _)
                || !map.TrySampleWorldPosition(end, out Vector2 endMap, out _))
            {
                details = "outside generated map bounds";
                return false;
            }
            // EvaluateMapPath includes active-view roof collisions. Its short
            // threshold segment stays within the exterior's approved switch lane;
            // do not falsely reject the interior floor because its exterior roof
            // would be solid if the real presentation switch never happened.
            map.TrySampleWorldPosition(machine.PortalToWorld(new Vector2(0f, .3f)), out Vector2 gateStart, out _);
            map.TrySampleWorldPosition(machine.PortalToWorld(new Vector2(0f, -.3f)), out Vector2 gateEnd, out _);
            SlopeTraversalResult forward = evaluator.EvaluateMapPath(gateStart, gateEnd);
            SlopeTraversalResult reverse = evaluator.EvaluateMapPath(gateEnd, gateStart);
            float flatLimit = Mathf.Min(8f, evaluator.LevelOneMaximumUphillAngle);
            bool terrain = forward.HasData && reverse.HasData && forward.IsPassable && reverse.IsPassable
                && !forward.RequiresHardStop && !reverse.RequiresHardStop
                && forward.MaximumSurfaceSlopeAngle <= flatLimit && reverse.MaximumSurfaceSlopeAngle <= flatLimit;
            Vector2 direction = (end - start).normalized;
            for (int sample = 0; sample <= 16 && terrain; sample++)
            {
                Vector2 point = Vector2.Lerp(start, end, sample / 16f);
                foreach (Vector2 heading in new[] { direction, -direction })
                {
                    SlopeTraversalResult ground = evaluator.EvaluateImmediateSafety(point, heading);
                    if (!ground.HasData || !ground.IsPassable || ground.RequiresHardStop
                        || ground.MaximumSurfaceSlopeAngle > flatLimit) terrain = false;
                }
            }
            float radius = evaluator.RobotObstacleCollisionRadiusMeters + .02f;
            var contacts = new List<WorldInteraction>();
            WorldInteractionQuery.Query(InteractionShape.Capsule(startMap, endMap, radius), Solids,
                map, player.gameObject.scene, contacts, player.transform);
            bool blocked = contacts.Any(wall => !wall.transform.IsChildOf(machine.transform));
            map.TrySampleWorldPosition(machine.PortalToWorld(new Vector2(0f, -.15f)), out Vector2 switchPoint, out _);
            foreach (GameObject view in new[] { machine.ExteriorRoot, machine.InteriorRoot })
            {
                InteractionShape path = view == machine.ExteriorRoot
                    ? InteractionShape.Capsule(startMap, switchPoint, radius)
                    : InteractionShape.Capsule(switchPoint, endMap, radius);
                foreach (WorldInteraction wall in view.GetComponentsInChildren<WorldInteraction>(true))
                    if ((wall.Kind & Solids) != 0 && WorldInteractionQuery.Penetration(path, wall.GetShape(map)) >= 0f)
                        blocked = true;
            }
            details = "forward=" + TraversalSummary(forward) + "; reverse=" + TraversalSummary(reverse)
                + "; flatLimit=" + flatLimit.ToString("0.###", CultureInfo.InvariantCulture) + "; solidSweepBlocked=" + blocked;
            return terrain && !blocked;
        }

        private void CheckPresentation(bool inside)
        {
            Require(machine.IsInside == inside && machine.InteriorRoot.activeInHierarchy == inside
                && machine.ExteriorRoot.activeInHierarchy != inside, "The real map crossing did not exclusively switch exterior/interior views.");
            GameObject active = inside ? machine.InteriorRoot : machine.ExteriorRoot;
            GameObject hidden = inside ? machine.ExteriorRoot : machine.InteriorRoot;
            var hits = new List<WorldInteraction>();
            int solids = 0;
            foreach (WorldInteraction wall in active.GetComponentsInChildren<WorldInteraction>(true))
            {
                if ((wall.Kind & Solids) == 0) continue;
                solids++;
                Require(wall.Available && WorldInteraction.Active.Contains(wall), "Visible-view collision is not registered: " + wall.name);
                WorldInteractionQuery.Query(InteractionShape.Circle(wall.GetShape(map).Center, .01f),
                    Solids, map, player.gameObject.scene, hits, player.transform);
                Require(hits.Contains(wall), "Visible-view wall is missing from the actual map-unit query: " + wall.name);
            }
            Require(solids > 0, "The visible machine has no real collision walls.");
            foreach (WorldInteraction wall in hidden.GetComponentsInChildren<WorldInteraction>(true))
            {
                Require(!wall.Available && !WorldInteraction.Active.Contains(wall), "A hidden-view wall remained registered: " + wall.name);
                WorldInteractionQuery.Query(InteractionShape.Circle(wall.GetShape(map).Center, .01f),
                    Solids, map, player.gameObject.scene, hits, player.transform);
                Require(!hits.Contains(wall), "A hidden-view wall still blocks map-unit queries: " + wall.name);
            }
            Record(true, (inside ? "Interior" : "Exterior") + " real map presentation and collision registration are correct.");
        }

        private void Capture(string filename)
        {
            Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "A graphics device is required for the map screenshot.");
            RenderTexture target = null;
            Texture2D pixels = null;
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = mapCamera.targetTexture;
            Vector3 previousPosition = mapCamera.transform.position;
            Quaternion previousRotation = mapCamera.transform.rotation;
            float previousSize = mapCamera.orthographicSize;
            float previousAspect = mapCamera.aspect;
            try
            {
                SpriteRenderer image = machine.ExteriorRoot.GetComponentsInChildren<SpriteRenderer>(true)
                    .Where(renderer => renderer.sprite != null).OrderByDescending(renderer => renderer.sprite.bounds.size.sqrMagnitude).First();
                Bounds local = image.sprite.bounds;
                Bounds bounds = new Bounds(image.transform.TransformPoint(local.min), Vector3.zero);
                bounds.Encapsulate(image.transform.TransformPoint(new Vector3(local.max.x, local.min.y, 0f)));
                bounds.Encapsulate(image.transform.TransformPoint(new Vector3(local.min.x, local.max.y, 0f)));
                bounds.Encapsulate(image.transform.TransformPoint(local.max));
                target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
                target.Create();
                mapCamera.targetTexture = target;
                mapCamera.aspect = 1600f / 900f;
                mapCamera.transform.SetPositionAndRotation(new Vector3(bounds.center.x, bounds.center.y, previousPosition.z), Quaternion.identity);
                mapCamera.orthographicSize = Mathf.Max(bounds.extents.y, bounds.extents.x / mapCamera.aspect) * 1.08f;
                if (GraphicsSettings.currentRenderPipeline == null) mapCamera.Render();
                else
                {
                    var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                    Require(RenderPipeline.SupportsRenderRequest(mapCamera, request), "The map renderer does not support a camera capture request.");
                    RenderPipeline.SubmitRenderRequest(mapCamera, request);
                }
                RenderTexture.active = target;
                pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                pixels.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
                pixels.Apply();
                Require(pixels.GetPixels32().Count(pixel => pixel.r > 100 || pixel.g > 100 || pixel.b > 100) > 1000,
                    "The actual map camera rendered a blank screenshot.");
                string path = Path.Combine(reportDirectory, filename);
                File.WriteAllBytes(path, pixels.EncodeToPNG());
                report.screenshots.Add(path);
                report.notes.Add(filename + " uses the real map camera, temporarily fitted to the same registered machine bounds. Camera settings were restored.");
                SaveReport();
            }
            finally
            {
                mapCamera.targetTexture = previousTarget;
                mapCamera.transform.SetPositionAndRotation(previousPosition, previousRotation);
                mapCamera.orthographicSize = previousSize;
                mapCamera.aspect = previousAspect;
                RenderTexture.active = previousActive;
                if (pixels != null) Object.DestroyImmediate(pixels);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            }
        }

        private void Drive(float throttle)
        {
            Gamepad gamepad = InputSystem.GetDeviceById(gamepadId) as Gamepad;
            Require(gamepad != null && gamepad.added, "The virtual map-test gamepad was lost.");
            gamepad.MakeCurrent();
            InputSystem.QueueStateEvent(gamepad, new GamepadState
            {
                leftStick = new Vector2(0f, throttle),
                rightTrigger = Mathf.Max(0f, throttle),
                leftTrigger = Mathf.Max(0f, -throttle)
            });
        }
        private bool Stopped() => Time.frameCount - phaseFrame >= 4 && Mathf.Abs(player.CurrentSpeed) < .03f
            && Mathf.Abs(player.CurrentTurnSpeed) < .1f && player.CurrentTerrainVelocity.magnitude < .03f;
        private void Begin(Phase value)
        {
            phase = value;
            phaseFrame = Time.frameCount;
            phaseStarted = EditorApplication.timeSinceStartup;
            Debug.Log("Waste Robot map smoke phase: " + value);
        }
        private void RequireElapsed(double seconds, string message) => Require(EditorApplication.timeSinceStartup - phaseStarted <= seconds, message);
        private void Record(bool passed, string message)
        {
            Require(passed, message);
            report.checks.Add(message);
            SaveReport();
        }
        private void OnLog(string message, string stack, LogType type)
        {
            if (!running || phase == Phase.Finishing) return;
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                report.errors.Add(type + ": " + message + "\n" + stack);
                SaveReport();
            }
        }
        private void Finish(Exception failure)
        {
            if (phase == Phase.Finishing) return;
            if (failure != null) report.errors.Add(failure.ToString());
            Gamepad gamepad = InputSystem.GetDeviceById(gamepadId) as Gamepad;
            if (gamepad != null && gamepad.added)
            {
                InputSystem.QueueStateEvent(gamepad, default(GamepadState));
                InputSystem.RemoveDevice(gamepad);
            }
            gamepadId = 0;
            Begin(Phase.Finishing);
            deadline = EditorApplication.timeSinceStartup + 20d;
            SaveReport();
            EditorApplication.isPlaying = false;
        }
        private void ExitBatch()
        {
            try
            {
                Require(!EditorApplication.isPlaying, "Cannot verify restoration while Play Mode is still active.");
                WasteRobotBase restored = FindMachine();
                Record(Vector3.Distance(restored.transform.position, report.originalMachine.position) < .0001f
                    && Quaternion.Angle(restored.transform.rotation, report.originalMachine.rotation) < .001f
                    && Vector3.Distance(restored.transform.localScale, report.originalMachine.localScale) < .0001f,
                    "Leaving Play Mode restored the user's exact machine position, rotation and scale.");
                report.sceneHashAfter = SceneHash();
                Record(report.sceneHashAfter == report.sceneHashBefore, "The saved user map file was not changed or saved by the smoke runner.");
                Require(!restored.gameObject.scene.isDirty, "The saved scene became dirty after the isolated smoke.");
            }
            catch (Exception error) { report.errors.Add(error.ToString()); }
            report.finishedUtc = DateTime.UtcNow.ToString("O");
            report.passed = report.errors.Count == 0;
            SaveReport();
            running = false;
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            int code = report.passed ? 0 : 1;
            Debug.Log("Waste Robot map smoke " + (report.passed ? "PASS" : "FAIL") + ": " + Path.Combine(reportDirectory, "map-scene-smoke.json"));
            Object.DestroyImmediate(this);
            EditorApplication.Exit(code);
        }
        private void SaveReport()
        {
            if (report != null && !string.IsNullOrEmpty(reportDirectory))
                File.WriteAllText(Path.Combine(reportDirectory, "map-scene-smoke.json"), JsonUtility.ToJson(report, true));
        }
        private static WasteRobotBase FindMachine()
        {
            WasteRobotBase[] machines = Object.FindObjectsByType<WasteRobotBase>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(value => value.gameObject.scene.path == ScenePath).ToArray();
            Require(machines.Length == 1, "Expected exactly one saved user WasteRobotBase instance in " + ScenePath + ".");
            return machines[0];
        }
        private static string TraversalSummary(SlopeTraversalResult value) => "hasData=" + value.HasData + ",passable=" + value.IsPassable
            + ",hardStop=" + value.RequiresHardStop + ",reason=" + value.BlockReason + ",maxSlope="
            + value.MaximumSurfaceSlopeAngle.ToString("0.###", CultureInfo.InvariantCulture) + ",step="
            + value.MaximumStepHeight.ToString("0.###", CultureInfo.InvariantCulture);
        private static string SceneHash()
        {
            using (var stream = File.OpenRead(Path.GetFullPath(ScenePath)))
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty);
        }
        private static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        private static string OutputDirectory()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], "-logFile", StringComparison.OrdinalIgnoreCase) && args[i + 1] != "-"
                    && !string.IsNullOrWhiteSpace(args[i + 1]))
                    return Path.GetDirectoryName(Path.GetFullPath(args[i + 1]));
            return Path.GetFullPath(Path.Combine("output", "validation", "waste-robot-map"));
        }
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Waste Robot map smoke: " + message);
        }
    }
}
