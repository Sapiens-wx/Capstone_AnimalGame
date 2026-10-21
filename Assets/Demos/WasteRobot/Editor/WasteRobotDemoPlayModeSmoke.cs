using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace AnimalGame.Demos.WasteRobot.Editor
{
    /// <summary>
    /// Explicit batch-only Play Mode smoke runner. The transient ScriptableObject
    /// preserves its serialized session across domain reload; OnEnable merely
    /// resumes that manually created session. There are no global initializers,
    /// runtime bootstraps, Play Mode setting changes or production-asset writes.
    /// Do not supply -quit: the asynchronous runner exits after leaving Play Mode.
    /// </summary>
    public sealed class WasteRobotDemoPlayModeSmoke : ScriptableObject
    {
        private enum Phase
        {
            WaitingForPlay, Warmup, DriveIn, StopInside, DriveOut, StopOutside,
            WallSetup, DriveAtWall, StopAtWall,
            RoomSetup, DriveIntoRoom, StopInRoom, DriveBackFromRoom, StopAfterRoom,
            Finishing
        }
        private const WorldInteractionKind SolidKinds = WorldInteractionKind.Collision | WorldInteractionKind.BodyCollision;
        private const double SmokeTimeoutSeconds = 180d;
        private static readonly string[] InteriorAppendageWalls =
        {
            "Wheel.UpperLeft", "Wheel.UpperRight", "Wheel.LowerLeft", "Wheel.LowerRight",
            "UpperArm.MiddleBeam", "LowerArm.MiddleBeam"
        };

        [Serializable]
        private sealed class SmokeReport
        {
            public string startedUtc;
            public string finishedUtc;
            public bool passed;
            public int simulatedFramesPerSecond;
            public float simulationDeltaTime;
            public float traversalThrottle;
            public List<string> checks = new List<string>();
            public List<string> errors = new List<string>();
            public List<string> notes = new List<string>();
            public List<string> screenshots = new List<string>();
        }

        [SerializeField] private bool running;
        [SerializeField] private Phase phase;
        [SerializeField] private SmokeReport report;
        [SerializeField] private string reportDirectory;
        [SerializeField] private double deadline;
        [SerializeField] private double phaseStarted;
        [SerializeField] private int lastFrame = -1;
        [SerializeField] private int phaseFrame;
        [SerializeField] private int gamepadId;
        [SerializeField] private WasteRobotBase machine;
        [SerializeField] private RobotMover player;
        [SerializeField] private Camera demoCamera;
        [SerializeField] private Vector2 driveStart;
        [SerializeField] private WorldInteraction wall;
        [SerializeField] private Vector2 wallStart;
        [SerializeField] private Vector2 wallOutward;
        [SerializeField] private float wallSupport;
        [SerializeField] private float bodyRadius;
        [SerializeField] private float minimumWallSeparation;
        [SerializeField] private bool wallExpectedInside;
        [SerializeField] private int interiorWallIndex;
        [SerializeField] private int roomIndex;
        [SerializeField] private Vector2 roomStart;
        [SerializeField] private Vector2 roomGoal;
        [SerializeField] private Vector2 roomForward;
        [SerializeField] private int roomTransitionCount;
        [SerializeField] private Bounds sharedCaptureBounds;
        [SerializeField] private int simulatedFramesPerSecond;
        [SerializeField] private float traversalThrottle = .4f;
        [SerializeField] private float previousCaptureDeltaTime;
        [SerializeField] private bool captureClockSaved;

        public static void RunBatch()
        {
            try
            {
                Require(Application.isBatchMode, "This smoke entry point is batch-only; run it in the isolated validation project.");
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Start the smoke runner in Edit Mode.");
                Require(!Resources.FindObjectsOfTypeAll<WasteRobotDemoPlayModeSmoke>().Any(session => session.running),
                    "A Waste Robot Play Mode smoke session is already running.");
                ReadOptionalParameters(out int requestedFps, out float requestedThrottle);
                Require(AssetDatabase.LoadAssetAtPath<SceneAsset>(WasteRobotDemoBuilder.ScenePath) != null,
                    "Build the Demo assets before running its Play Mode smoke check.");
                for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                    Require(!UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty,
                        "Refusing to replace an unsaved scene; use a clean isolated project process.");

                EditorSceneManager.OpenScene(WasteRobotDemoBuilder.ScenePath, OpenSceneMode.Single);
                var session = CreateInstance<WasteRobotDemoPlayModeSmoke>();
                session.hideFlags = HideFlags.HideAndDontSave;
                session.reportDirectory = OutputDirectory();
                session.simulatedFramesPerSecond = requestedFps;
                session.traversalThrottle = requestedThrottle;
                Directory.CreateDirectory(session.reportDirectory);
                session.report = new SmokeReport
                {
                    startedUtc = DateTime.UtcNow.ToString("O"),
                    simulatedFramesPerSecond = requestedFps,
                    traversalThrottle = requestedThrottle
                };
                session.report.notes.Add("Virtual InputSystem Gamepad drives the original RobotMover Update; no portal observation or player movement method is invoked manually.");
                session.report.notes.Add("Each room/wall fixture sets its start pose and presentation once; every doorway traversal and wall approach then uses the original controller with virtual forward/reverse input, never direct position steps.");
                session.running = true;
                session.phase = Phase.WaitingForPlay;
                session.deadline = EditorApplication.timeSinceStartup + SmokeTimeoutSeconds;
                session.phaseStarted = EditorApplication.timeSinceStartup;
                session.Subscribe();
                session.SaveReport();
                Debug.Log("Waste Robot Play Mode smoke starting. Report directory: " + session.reportDirectory);
                EditorApplication.isPlaying = true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        private void OnEnable()
        {
            if (running) Subscribe();
        }

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
                    if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
                        ExitBatch();
                    else if (EditorApplication.timeSinceStartup > deadline)
                    {
                        report.errors.Add("Timed out leaving Play Mode.");
                        ExitBatch();
                    }
                    return;
                }
                if (EditorApplication.timeSinceStartup > deadline)
                    throw new TimeoutException("Waste Robot Play Mode smoke exceeded " + SmokeTimeoutSeconds + " seconds.");
                EditorApplication.QueuePlayerLoopUpdate();
                if (!EditorApplication.isPlaying)
                {
                    if (phase != Phase.WaitingForPlay && !EditorApplication.isPlayingOrWillChangePlaymode)
                        throw new InvalidOperationException("Play Mode ended before the smoke checks completed.");
                    return;
                }
                if (lastFrame == Time.frameCount) return;
                lastFrame = Time.frameCount;

                switch (phase)
                {
                    case Phase.WaitingForPlay:
                        InitializePlay();
                        break;
                    case Phase.Warmup:
                        Drive(0f);
                        if (Time.frameCount - phaseFrame >= 5)
                        {
                            Require(machine.Target == player.transform, "Scene bootstrap did not bind the original player.");
                            CheckPresentation(false);
                            Capture("exterior-play.png");
                            driveStart = player.transform.position;
                            Begin(Phase.DriveIn);
                            Drive(traversalThrottle);
                        }
                        break;
                    case Phase.DriveIn:
                        Drive(traversalThrottle);
                        RequireElapsed(20d, "Actual player did not enter through the doorway.");
                        Require(Mathf.Abs(machine.WorldToPortal(player.transform.position).x) < .5f,
                            "Unexpected steering moved the player away from the entrance.");
                        if (machine.IsInside && machine.WorldToPortal(player.transform.position).y <= -.65f)
                        {
                            Record(Vector2.Distance(driveStart, player.transform.position) > 1.5f,
                                "Original RobotMover physically drove from outside through the entrance.");
                            Record(machine.TransitionCount == 1, "One real inward crossing produced exactly one transition.");
                            CheckPresentation(true);
                            Begin(Phase.StopInside);
                            Drive(0f);
                        }
                        break;
                    case Phase.StopInside:
                        Drive(0f);
                        RequireElapsed(10d, "Player did not coast to a stop inside.");
                        if (Stopped())
                        {
                            Require(machine.IsInside, "Interior state changed while stopping inside.");
                            Capture("interior-play.png");
                            driveStart = player.transform.position;
                            Begin(Phase.DriveOut);
                            Drive(-traversalThrottle);
                        }
                        break;
                    case Phase.DriveOut:
                        Drive(-traversalThrottle);
                        RequireElapsed(20d, "Actual reverse input did not move the player out through the doorway.");
                        if (!machine.IsInside && machine.WorldToPortal(player.transform.position).y >= .65f)
                        {
                            Record(Vector2.Distance(driveStart, player.transform.position) > .8f,
                                "Original RobotMover physically reversed back out while still facing inward.");
                            Record(machine.TransitionCount == 2, "The real return crossing produced exactly two total transitions.");
                            CheckPresentation(false);
                            Begin(Phase.StopOutside);
                            Drive(0f);
                        }
                        break;
                    case Phase.StopOutside:
                        Drive(0f);
                        RequireElapsed(10d, "Player did not stop outside before the wall fixture.");
                        if (Stopped())
                        {
                            PrepareWallFixture();
                            Begin(Phase.WallSetup);
                        }
                        break;
                    case Phase.WallSetup:
                        Drive(0f);
                        RequireElapsed(5d, "Wall fixture did not settle: " + wall.name);
                        if (Time.frameCount - phaseFrame >= 3)
                        {
                            wallStart = player.transform.position;
                            minimumWallSeparation = Vector2.Dot(wallStart - wall.GetShape(null).Center, wallOutward);
                            Begin(Phase.DriveAtWall);
                            Drive(.5f);
                        }
                        break;
                    case Phase.DriveAtWall:
                        Drive(.5f);
                        RequireElapsed(8d, "Wall-driving fixture did not complete: " + wall.name);
                        float separation = Vector2.Dot((Vector2)player.transform.position - wall.GetShape(null).Center, wallOutward);
                        minimumWallSeparation = Mathf.Min(minimumWallSeparation, separation);
                        Require(separation >= wallSupport + bodyRadius - .04f,
                            "The original player penetrated or crossed a hard wall: " + wall.name);
                        Require(machine.IsInside == wallExpectedInside,
                            "Driving against a wheel/arm wall changed the presentation: " + wall.name);
                        if (EditorApplication.timeSinceStartup - phaseStarted >= 2.5d)
                        {
                            float moved = Vector2.Distance(wallStart, player.transform.position);
                            Record(moved >= .25f, "Real input approached the solid wall: " + wall.name);
                            Record(moved <= .85f, "Real RobotMover motion stopped before crossing the wall: " + wall.name);
                            Record(minimumWallSeparation <= wallSupport + bodyRadius + .2f,
                                "The player reached the wall rather than merely failing to move: " + wall.name);
                            Record(player.CurrentThrottleIntent > .2f, "Positive input remained applied at wall contact: " + wall.name);
                            CheckPresentation(wallExpectedInside);
                            Begin(Phase.StopAtWall);
                            Drive(0f);
                        }
                        break;
                    case Phase.StopAtWall:
                        Drive(0f);
                        RequireElapsed(10d, "Player did not stop after the wall check.");
                        if (Stopped())
                        {
                            if (!wallExpectedInside)
                            {
                                roomIndex = 0;
                                PrepareRoomFixture();
                                Begin(Phase.RoomSetup);
                            }
                            else if (++interiorWallIndex < InteriorAppendageWalls.Length)
                            {
                                PrepareInteriorWallFixture();
                                Begin(Phase.WallSetup);
                            }
                            else Finish(null);
                        }
                        break;
                    case Phase.RoomSetup:
                        Drive(0f);
                        RequireElapsed(5d, "Room fixture did not settle: " + RoomName);
                        if (Time.frameCount - phaseFrame >= 3)
                        {
                            RequireRoomPresentation();
                            Begin(Phase.DriveIntoRoom);
                            Drive(traversalThrottle);
                        }
                        break;
                    case Phase.DriveIntoRoom:
                        Drive(traversalThrottle);
                        RequireElapsed(12d, "Real input could not drive through the " + RoomName + " doorway.");
                        RequireRoomPresentation();
                        if (Vector2.Dot((Vector2)player.transform.position - roomGoal, roomForward) >= 0f)
                        {
                            Record(Vector2.Dot((Vector2)player.transform.position - roomStart, roomForward)
                                >= Vector2.Distance(roomStart, roomGoal) - .02f,
                                "Original RobotMover drove through the full " + RoomName + " doorway into the room.");
                            Record(player.CurrentThrottleIntent > .1f,
                                "Forward input, not fixture relocation, completed entry to the " + RoomName + ".");
                            CheckPresentation(true);
                            Begin(Phase.StopInRoom);
                            Drive(0f);
                        }
                        break;
                    case Phase.StopInRoom:
                        Drive(0f);
                        RequireElapsed(10d, "Player did not stop in the " + RoomName + ".");
                        RequireRoomPresentation();
                        if (Stopped())
                        {
                            Capture(roomIndex == 0 ? "upper-bay-play.png" : "lower-bay-play.png");
                            driveStart = player.transform.position;
                            Begin(Phase.DriveBackFromRoom);
                            Drive(-traversalThrottle);
                        }
                        break;
                    case Phase.DriveBackFromRoom:
                        Drive(-traversalThrottle);
                        RequireElapsed(12d, "Real reverse input could not return through the " + RoomName + " doorway.");
                        RequireRoomPresentation();
                        if (Vector2.Dot((Vector2)player.transform.position - roomStart, roomForward) <= 0f)
                        {
                            Record(Vector2.Distance(driveStart, player.transform.position) > .8f,
                                "Original RobotMover reversed through the " + RoomName + " doorway back to the main room.");
                            Record(player.CurrentThrottleIntent < -.1f,
                                "Reverse input remained applied while returning from the " + RoomName + ".");
                            Begin(Phase.StopAfterRoom);
                            Drive(0f);
                        }
                        break;
                    case Phase.StopAfterRoom:
                        Drive(0f);
                        RequireElapsed(10d, "Player did not stop after returning from the " + RoomName + ".");
                        RequireRoomPresentation();
                        if (Stopped())
                        {
                            if (++roomIndex < 2)
                            {
                                PrepareRoomFixture();
                                Begin(Phase.RoomSetup);
                            }
                            else
                            {
                                interiorWallIndex = 0;
                                PrepareInteriorWallFixture();
                                Begin(Phase.WallSetup);
                            }
                        }
                        break;
                }
            }
            catch (Exception exception)
            {
                Finish(exception);
            }
        }

        private void InitializePlay()
        {
            if (simulatedFramesPerSecond > 0 && !captureClockSaved)
            {
                previousCaptureDeltaTime = Time.captureDeltaTime;
                captureClockSaved = true;
                Time.captureDeltaTime = 1f / simulatedFramesPerSecond;
                report.simulationDeltaTime = Time.captureDeltaTime;
                report.notes.Add("Simulated frame rate: " + simulatedFramesPerSecond
                    + " fps via Time.captureDeltaTime; traversal throttle: "
                    + traversalThrottle.ToString("0.###", CultureInfo.InvariantCulture)
                    + ". The previous capture delta is restored when this smoke session finishes.");
                SaveReport();
            }
            WasteRobotDemoScene bootstrap = Object.FindFirstObjectByType<WasteRobotDemoScene>();
            if (bootstrap == null)
            {
                RequireElapsed(15d, "Demo scene did not load in Play Mode.");
                return;
            }
            machine = bootstrap.Machine;
            player = bootstrap.Player;
            demoCamera = bootstrap.DemoCamera;
            Require(machine != null && player != null && demoCamera != null, "Demo scene references are missing.");
            Require(machine.gameObject.scene == player.gameObject.scene && player.gameObject.scene == demoCamera.gameObject.scene,
                "Machine, original player and camera must remain in the same Demo scene.");
            Require(player.isActiveAndEnabled && demoCamera.isActiveAndEnabled, "Player or camera is disabled in Play Mode.");
            RobotMarkerView marker = player.GetComponent<RobotMarkerView>();
            Require(marker != null, "Original player body size is unavailable.");
            bodyRadius = marker.BodyDiameter * Mathf.Max(Mathf.Abs(player.transform.lossyScale.x),
                Mathf.Abs(player.transform.lossyScale.y)) * .5f;
            CheckSharedImageRegistration();
            Gamepad gamepad = InputSystem.AddDevice<Gamepad>("Waste Robot smoke virtual controller");
            gamepad.MakeCurrent();
            gamepadId = gamepad.deviceId;
            Record(machine.WorldToPortal(player.transform.position).y > .5f, "Authored player starts outside the shared entrance.");
            Begin(Phase.Warmup);
        }

        private void PrepareWallFixture()
        {
            PrepareWallFixture(machine.ExteriorRoot, false, "Wheel.UpperLeft", machine.transform.up);
        }

        private void PrepareInteriorWallFixture()
        {
            Require(interiorWallIndex >= 0 && interiorWallIndex < InteriorAppendageWalls.Length,
                "Interior appendage fixture index is outside its finite test list.");
            string suffix = InteriorAppendageWalls[interiorWallIndex];
            bool belowHull = suffix.StartsWith("Wheel.Lower", StringComparison.Ordinal)
                || suffix.StartsWith("LowerArm.", StringComparison.Ordinal);
            PrepareWallFixture(machine.InteriorRoot, true, suffix,
                belowHull ? -machine.transform.up : machine.transform.up);
        }

        private void PrepareWallFixture(GameObject view, bool inside, string suffix, Vector2 preferredOutward)
        {
            wallExpectedInside = inside;
            // Bind only establishes this separate fixture's starting state. All
            // subsequent wall contact is driven by real RobotMover input.
            machine.Bind(player.transform, inside);
            wall = view.GetComponentsInChildren<WorldInteraction>()
                .FirstOrDefault(candidate => candidate.name.EndsWith("." + suffix, StringComparison.Ordinal));
            Require(wall != null, "Cannot locate the authored " + (inside ? "interior" : "exterior")
                + " appendage collision: " + suffix);
            Require(wall.Available && (wall.Kind & SolidKinds) != 0,
                "Expected the selected wheel/arm to remain an active solid: " + wall.name);
            InteractionShape shape = wall.GetShape(null);
            Require(shape.IsBox, "Expected the selected wheel/arm beam to be an oriented solid box: " + wall.name);
            Vector2 edge = shape.B - shape.A;
            wallOutward = new Vector2(-edge.y, edge.x).normalized;
            if (Vector2.Dot(wallOutward, preferredOutward) < 0f) wallOutward = -wallOutward;
            wallSupport = 0f;
            for (int i = 0; i < 4; i++)
                wallSupport = Mathf.Max(wallSupport, Vector2.Dot(shape.Vertex(i) - shape.Center, wallOutward));
            Vector2 start = shape.Center + wallOutward * (wallSupport + bodyRadius + .65f);
            Require(!WorldInteractionQuery.Query(InteractionShape.Circle(start, bodyRadius + .02f),
                SolidKinds, null, player.gameObject.scene, null, player.transform),
                "The wall fixture starts overlapped with another obstacle.");
            // Relocation is fixture setup only. The following phase uses the real
            // original controller's Update, normal input and collision queries.
            player.transform.SetPositionAndRotation(start,
                Quaternion.Euler(0f, 0f, Mathf.Atan2(-wallOutward.y, -wallOutward.x) * Mathf.Rad2Deg - 90f));
            machine.Bind(player.transform, inside);
            demoCamera.GetComponent<RobotCameraFollow>()?.SnapToTarget();
            CheckPresentation(inside);
            report.notes.Add("Wall fixture: " + wall.name + "; presentation=" + (inside ? "interior" : "exterior")
                + "; starting gap 0.65 world units beyond the player's body radius.");
        }

        private string RoomName => roomIndex == 0 ? "upper right bay" : "lower right bay";

        private void PrepareRoomFixture()
        {
            Require(roomIndex >= 0 && roomIndex < 2, "Room fixture index is outside its two-room test list.");
            float sourceY = roomIndex == 0 ? 340f : 588f;
            roomStart = ImagePointToWorld(new Vector2(1335f, sourceY));
            roomGoal = ImagePointToWorld(new Vector2(1515f, sourceY));
            roomForward = (roomGoal - roomStart).normalized;
            machine.Bind(player.transform, true);
            Require(!WorldInteractionQuery.Query(InteractionShape.Circle(roomStart, bodyRadius + .02f),
                SolidKinds, null, player.gameObject.scene, null, player.transform),
                "The " + RoomName + " fixture starts overlapped with an obstacle.");
            Require(!WorldInteractionQuery.Query(InteractionShape.Circle(roomGoal, bodyRadius + .02f),
                SolidKinds, null, player.gameObject.scene, null, player.transform),
                "The " + RoomName + " destination does not leave room for the actual player body.");
            // This one setup relocation does not perform the doorway crossing.
            // The following phases must drive both directions through the gap.
            player.transform.SetPositionAndRotation(roomStart,
                Quaternion.Euler(0f, 0f, Mathf.Atan2(roomForward.y, roomForward.x) * Mathf.Rad2Deg - 90f));
            machine.Bind(player.transform, true);
            roomTransitionCount = machine.TransitionCount;
            demoCamera.GetComponent<RobotCameraFollow>()?.SnapToTarget();
            CheckPresentation(true);
            report.notes.Add("Room fixture: " + RoomName + "; shared image coordinates (1335,"
                + sourceY.ToString("0", CultureInfo.InvariantCulture) + ") -> (1515,"
                + sourceY.ToString("0", CultureInfo.InvariantCulture) + "); real forward/reverse input crosses the doorway.");
        }

        private Vector2 ImagePointToWorld(Vector2 pixel)
        {
            Vector2 origin = WasteRobotCollisionLayout.SharedEntranceCenter;
            float scale = machine.EntranceWidth / WasteRobotCollisionLayout.SharedEntranceWidth;
            return machine.PortalToWorld(new Vector2((pixel.x - origin.x) * scale, (origin.y - pixel.y) * scale));
        }

        private void RequireRoomPresentation()
        {
            Require(machine.IsInside && machine.InteriorRoot.activeInHierarchy && !machine.ExteriorRoot.activeInHierarchy,
                "Crossing a right-bay doorway unexpectedly left the interior presentation.");
            Require(machine.TransitionCount == roomTransitionCount,
                "An internal bay crossing was incorrectly treated as an exterior entrance crossing.");
            Vector2 perpendicular = new Vector2(-roomForward.y, roomForward.x);
            Require(Mathf.Abs(Vector2.Dot((Vector2)player.transform.position - roomStart, perpendicular)) < .1f,
                "Unexpected steering took the actual player away from the " + RoomName + " doorway lane.");
        }

        private void CheckSharedImageRegistration()
        {
            SpriteRenderer exterior = MainImage(machine.ExteriorRoot);
            SpriteRenderer interior = MainImage(machine.InteriorRoot);
            foreach (SpriteRenderer image in new[] { exterior, interior })
            {
                Require(image.sprite.rect.size == WasteRobotCollisionLayout.SharedImageSize,
                    "The actual Demo images must retain the shared 1779 x 884 canvas.");
                Require(image.sprite.pivot == image.sprite.rect.size * .5f,
                    "The actual Demo images must retain the same centered pivot.");
            }
            Require(Mathf.Approximately(exterior.sprite.pixelsPerUnit, interior.sprite.pixelsPerUnit),
                "The two presentation images use different pixels per unit.");
            sharedCaptureBounds = ImageBounds(exterior);
            Bounds insideBounds = ImageBounds(interior);
            Record(Vector3.Distance(sharedCaptureBounds.center, insideBounds.center) < .0001f
                && Vector3.Distance(sharedCaptureBounds.size, insideBounds.size) < .0001f,
                "Exterior and interior artwork have identical registered world bounds before real traversal.");
            report.notes.Add("All screenshots use one shared 1600x900 camera fit, centered at "
                + sharedCaptureBounds.center.ToString("F4") + "; bounds=" + sharedCaptureBounds.size.ToString("F4") + ".");
        }

        private static SpriteRenderer MainImage(GameObject view)
        {
            SpriteRenderer image = view.GetComponentsInChildren<SpriteRenderer>(true)
                .Where(renderer => renderer.sprite != null)
                .OrderByDescending(renderer => renderer.sprite.bounds.size.sqrMagnitude).FirstOrDefault();
            Require(image != null, "A Demo presentation has no SpriteRenderer artwork.");
            return image;
        }

        private static Bounds ImageBounds(SpriteRenderer image)
        {
            // Derive inactive-view bounds without briefly toggling presentation
            // or incrementing the real doorway transition count.
            Bounds source = image.sprite.bounds;
            Bounds bounds = new Bounds(image.transform.TransformPoint(source.min), Vector3.zero);
            bounds.Encapsulate(image.transform.TransformPoint(new Vector3(source.max.x, source.min.y, source.center.z)));
            bounds.Encapsulate(image.transform.TransformPoint(new Vector3(source.min.x, source.max.y, source.center.z)));
            bounds.Encapsulate(image.transform.TransformPoint(source.max));
            return bounds;
        }

        private void Drive(float throttle)
        {
            Gamepad gamepad = InputSystem.GetDeviceById(gamepadId) as Gamepad;
            Require(gamepad != null && gamepad.added, "Virtual input device was lost during Play Mode.");
            gamepad.MakeCurrent();
            var state = new GamepadState
            {
                leftStick = new Vector2(0f, throttle),
                rightTrigger = Mathf.Max(0f, throttle),
                leftTrigger = Mathf.Max(0f, -throttle)
            };
            InputSystem.QueueStateEvent(gamepad, state);
        }

        private void CheckPresentation(bool inside)
        {
            Require(machine.IsInside == inside && machine.InteriorRoot.activeInHierarchy == inside
                && machine.ExteriorRoot.activeInHierarchy != inside, "Actual traversal did not switch presentation roots exclusively.");
            GameObject active = inside ? machine.InteriorRoot : machine.ExteriorRoot;
            GameObject hidden = inside ? machine.ExteriorRoot : machine.InteriorRoot;
            var hits = new List<WorldInteraction>();
            foreach (WorldInteraction collision in active.GetComponentsInChildren<WorldInteraction>(true))
            {
                if ((collision.Kind & SolidKinds) == 0) continue;
                Require(collision.Available && WorldInteraction.Active.Contains(collision),
                    "An active-view collision was not registered in Play Mode: " + collision.name);
                WorldInteractionQuery.Query(InteractionShape.Circle(collision.GetShape(null).Center, .01f),
                    SolidKinds, null, player.gameObject.scene, hits, player.transform);
                Require(hits.Contains(collision), "An active-view wall is absent from real Play Mode queries: " + collision.name);
            }
            foreach (WorldInteraction collision in hidden.GetComponentsInChildren<WorldInteraction>(true))
            {
                Require(!collision.Available && !WorldInteraction.Active.Contains(collision),
                    "A hidden-view collision stayed registered in Play Mode: " + collision.name);
                WorldInteractionQuery.Query(InteractionShape.Circle(collision.GetShape(null).Center, .01f),
                    SolidKinds, null, player.gameObject.scene, hits, player.transform);
                Require(!hits.Contains(collision), "A hidden-view wall still blocks Play Mode queries: " + collision.name);
            }
            Record(true, (inside ? "Interior" : "Exterior") + " presentation, active collisions and hidden collision removal match actual traversal.");
        }

        private void Capture(string filename)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                report.notes.Add("Screenshot skipped: null graphics device. Omit -nographics to render the two views.");
                SaveReport();
                return;
            }
            RenderTexture target = null;
            Texture2D pixels = null;
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = demoCamera.targetTexture;
            Vector3 previousPosition = demoCamera.transform.position;
            Quaternion previousRotation = demoCamera.transform.rotation;
            float previousSize = demoCamera.orthographicSize;
            float previousAspect = demoCamera.aspect;
            try
            {
                // Fitting each active image independently can hide registration
                // errors. Every exterior/interior/room capture uses this exact
                // same world rectangle and camera framing instead.
                Bounds bounds = sharedCaptureBounds;
                target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
                target.Create();
                demoCamera.targetTexture = target;
                demoCamera.aspect = 1600f / 900f;
                demoCamera.transform.SetPositionAndRotation(new Vector3(bounds.center.x, bounds.center.y, -10f), Quaternion.identity);
                demoCamera.orthographicSize = Mathf.Max(bounds.extents.y, bounds.extents.x / demoCamera.aspect) * 1.08f;
                if (GraphicsSettings.currentRenderPipeline == null) demoCamera.Render();
                else
                {
                    var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                    Require(RenderPipeline.SupportsRenderRequest(demoCamera, request), "The renderer cannot capture a single Demo camera.");
                    RenderPipeline.SubmitRenderRequest(demoCamera, request);
                }
                RenderTexture.active = target;
                pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                pixels.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
                pixels.Apply();
                Color32[] samples = pixels.GetPixels32();
                Require(samples.Count(pixel => pixel.r > 100 || pixel.g > 100 || pixel.b > 100) > 1000,
                    "Rendered Demo screenshot is blank or has no visible line artwork.");
                string path = Path.Combine(reportDirectory, filename);
                File.WriteAllBytes(path, pixels.EncodeToPNG());
                report.screenshots.Add(path);
                report.notes.Add(filename + " uses the same shared image bounds and camera fit as every other view; camera settings are restored immediately.");
                SaveReport();
            }
            catch (Exception exception)
            {
                // Rendering is best effort; movement/collision validation remains
                // authoritative, and capture failure is clearly recorded.
                report.notes.Add("Screenshot unavailable (" + filename + "): " + exception.Message);
                SaveReport();
            }
            finally
            {
                demoCamera.targetTexture = previousTarget;
                demoCamera.transform.SetPositionAndRotation(previousPosition, previousRotation);
                demoCamera.orthographicSize = previousSize;
                demoCamera.aspect = previousAspect;
                RenderTexture.active = previousActive;
                if (pixels != null) Object.DestroyImmediate(pixels);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            }
        }

        private bool Stopped() => Time.frameCount - phaseFrame >= 3
            && Mathf.Abs(player.CurrentSpeed) < .03f && Mathf.Abs(player.CurrentTurnSpeed) < .1f;

        private void Begin(Phase next)
        {
            phase = next;
            phaseFrame = Time.frameCount;
            phaseStarted = EditorApplication.timeSinceStartup;
            Debug.Log("Waste Robot smoke phase: " + phase);
        }

        private void RequireElapsed(double seconds, string message)
        {
            Require(EditorApplication.timeSinceStartup - phaseStarted <= seconds, message);
        }

        private void Record(bool success, string message)
        {
            Require(success, message);
            report.checks.Add(message);
            SaveReport();
        }

        private void OnLog(string message, string stackTrace, LogType type)
        {
            if (!running || phase == Phase.Finishing) return;
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                report.errors.Add(type + ": " + message + "\n" + stackTrace);
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
            RestoreCaptureClock();
            phase = Phase.Finishing;
            deadline = EditorApplication.timeSinceStartup + 20d;
            SaveReport();
            EditorApplication.isPlaying = false;
        }

        private void ExitBatch()
        {
            RestoreCaptureClock();
            report.finishedUtc = DateTime.UtcNow.ToString("O");
            report.passed = report.errors.Count == 0;
            SaveReport();
            running = false;
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            int code = report.passed ? 0 : 1;
            Debug.Log("Waste Robot Play Mode smoke " + (report.passed ? "PASS" : "FAIL")
                + ": " + report.checks.Count + " checks; " + Path.Combine(reportDirectory, "play-mode-smoke.json"));
            Object.DestroyImmediate(this);
            EditorApplication.Exit(code);
        }

        private void SaveReport()
        {
            if (report != null && !string.IsNullOrEmpty(reportDirectory))
                File.WriteAllText(Path.Combine(reportDirectory, "play-mode-smoke.json"), JsonUtility.ToJson(report, true));
        }

        private static string OutputDirectory()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], "-logFile", StringComparison.OrdinalIgnoreCase)
                    && args[i + 1] != "-" && !string.IsNullOrWhiteSpace(args[i + 1]))
                    return Path.GetDirectoryName(Path.GetFullPath(args[i + 1]));
            return Path.GetFullPath(Path.Combine("output", "validation", "waste-robot"));
        }

        private void RestoreCaptureClock()
        {
            if (captureClockSaved) Time.captureDeltaTime = previousCaptureDeltaTime;
        }

        private static void ReadOptionalParameters(out int fps, out float throttle)
        {
            fps = 0;
            throttle = .4f;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "-wasteRobotSmokeFps", StringComparison.OrdinalIgnoreCase))
                {
                    Require(i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out fps) && (fps == 15 || fps == 30 || fps == 60),
                        "-wasteRobotSmokeFps requires 15, 30 or 60.");
                    i++;
                }
                else if (string.Equals(args[i], "-wasteRobotSmokeThrottle", StringComparison.OrdinalIgnoreCase))
                {
                    Require(i + 1 < args.Length && float.TryParse(args[i + 1], NumberStyles.Float,
                        CultureInfo.InvariantCulture, out throttle) && throttle > 0f && throttle <= 1f,
                        "-wasteRobotSmokeThrottle requires a number greater than zero and at most one.");
                    i++;
                }
            }
        }

        private static void Require(bool success, string message)
        {
            if (!success) throw new InvalidOperationException("Waste Robot Play Mode smoke: " + message);
        }
    }
}
