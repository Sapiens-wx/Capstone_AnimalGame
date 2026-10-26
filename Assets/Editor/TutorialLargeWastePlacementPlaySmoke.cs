using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using AnimalGame.Tutorial;
using AnimalGame.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    /// <summary>Checks native large-pile placement at actual tutorial-map poses in a disposable project.</summary>
    public sealed class TutorialLargeWastePlacementPlaySmoke : ScriptableObject
    {
        private const string SessionKey = "AnimalGame.TutorialLargeWastePlacementPlaySmoke.CurrentRun";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string KnownSceneHeader = "Problem detected while opening the Scene file: 'Assets/Scenes/GameMaps/TutorialTemporaryMap.unity'.\nCheck the following logs for more details.";
        private const string KnownMissingStaticAnimal = "Prefab instance problem: 'Static_Animal (Missing Prefab with guid: 2645e53e76c8ec846b09ee78b13275be)'. 2 instances are missing the same Prefab Asset with guid 2645e53e76c8ec846b09ee78b13275be.";
        private const WorldInteractionKind BlockingKinds = WorldInteractionKind.Collision | WorldInteractionKind.BodyCollision;
        private enum Phase { Starting, PrepareCase, Settle, Spawn, Finishing }

        [Serializable] private sealed class PlacementCase
        {
            public string name, failureLastFailure, skippedReason;
            public Vector2 requestedPlayer, player, target, approach;
            public float heading, clearanceRadiusMeters, targetRadiusMeters;
            public bool syntheticStartOverlap, skipped, passed, nativeBigPile, dryFootprint, collisionFree;
            public bool legacyBlocksAllBearings, previousAllowsEscape, helperPreservesPlayerPose;
            public int ensureCalls;
            public double totalEnsureMilliseconds, maximumEnsureMilliseconds;
        }

        [Serializable] private sealed class Report
        {
            public bool passed, legacyRegressionReproduced;
            public string project, phase, failureLastFailure, originalLargePrefab, originalMediumPrefab;
            public int validCases, passedCases, skippedCases, knownSceneHeaders, knownMissingPrefabMessages;
            public double maximumEnsureMilliseconds;
            public List<PlacementCase> cases = new();
            public List<string> checks = new(), errors = new(), knownSceneDiagnostics = new();
        }

        [SerializeField] private string runId, output;
        [SerializeField] private bool running, originalBackground;
        [SerializeField] private Phase phase;
        [SerializeField] private Report report;
        [SerializeField] private HeightMapPlayerSceneBootstrap bootstrap;
        [SerializeField] private TutorialLargeWasteTarget targets;
        [SerializeField] private GameObject syntheticObstacle;
        [SerializeField] private InputSettings originalInput, testInput;
        [SerializeField] private int gamepadId, caseIndex, lastFrame = -1, settleFrame;
        [SerializeField] private double deadline, caseDeadline;
        [SerializeField] private Vector2 baselinePosition;
        [SerializeField] private bool hasBaseline;

        public static void RunBatch()
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            if (!Application.isBatchMode || !project.Contains("/output/validation/"))
                throw new InvalidOperationException("Run large-waste placement checks only in a disposable output/validation project.");
            SessionState.EraseString(SessionKey);
            foreach (var old in Resources.FindObjectsOfTypeAll<TutorialLargeWastePlacementPlaySmoke>())
            { old.running = false; old.Unsubscribe(); DestroyImmediate(old); }
            var test = CreateInstance<TutorialLargeWastePlacementPlaySmoke>();
            test.hideFlags = HideFlags.HideAndDontSave;
            test.runId = Guid.NewGuid().ToString("N"); SessionState.SetString(SessionKey, test.runId);
            string configured = Environment.GetEnvironmentVariable("TUTORIAL_LARGE_WASTE_PLACEMENT_OUTPUT");
            test.output = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(project, "output/validation/tutorial-large-waste-placement") : Path.GetFullPath(configured);
            Directory.CreateDirectory(test.output);
            test.report = new Report { project = project };
            test.AddCases(); test.phase = Phase.Starting; test.Subscribe();
            EditorSceneManager.OpenScene(TutorialDrivingLesson.ScenePath, OpenSceneMode.Single);
            test.deadline = EditorApplication.timeSinceStartup + 240;
            test.running = true; EditorApplication.isPlaying = true;
        }

        private void AddCases()
        {
            Add("shoreline control", new Vector2(146, 121), 180);
            Add("same pose with overlapping start clearance", new Vector2(146, 121), 180, true);
            Vector2[] seeds = { new(146, 121), new(140, 107), new(124, 79), new(160, 130), new(132, 110), new(150, 100) };
            for (int i = 0; i < seeds.Length; i++)
            {
                Add("native map seed " + i + " north", seeds[i], 0);
                Add("native map seed " + i + " east", seeds[i], 270);
            }
        }
        private void Add(string label, Vector2 point, float heading, bool overlap = false) =>
            report.cases.Add(new PlacementCase { name = label, requestedPlayer = point, heading = heading, syntheticStartOverlap = overlap });
        private bool Current => !string.IsNullOrEmpty(runId) && SessionState.GetString(SessionKey, "") == runId;
        private PlacementCase Case => caseIndex < report.cases.Count ? report.cases[caseIndex] : null;
        private void OnEnable() { if (running && Current) Subscribe(); }
        private void OnDisable() => Unsubscribe();
        private void Subscribe()
        { if (!Current) return; Unsubscribe(); EditorApplication.update += Tick; InputSystem.onBeforeUpdate += NeutralInput; Application.logMessageReceived += OnLog; }
        private void Unsubscribe()
        { EditorApplication.update -= Tick; InputSystem.onBeforeUpdate -= NeutralInput; Application.logMessageReceived -= OnLog; }

        private void Tick()
        {
            if (!running || !Current) return;
            try
            {
                if (phase == Phase.Finishing) { if (!EditorApplication.isPlayingOrWillChangePlaymode) Exit(); return; }
                Require(EditorApplication.timeSinceStartup < deadline, "Large-pile placement smoke timed out in " + phase + ".");
                EditorApplication.QueuePlayerLoopUpdate();
                if (!EditorApplication.isPlaying || Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount;
                switch (phase)
                {
                    case Phase.Starting:
                        bootstrap = Object.FindFirstObjectByType<HeightMapPlayerSceneBootstrap>();
                        targets = Object.FindFirstObjectByType<TutorialLargeWasteTarget>();
                        if (bootstrap?.map == null || !bootstrap.map.HasGeneratedMap || bootstrap.robot == null
                            || bootstrap.armController == null || bootstrap.traversalEvaluator == null
                            || !bootstrap.traversalEvaluator.IsInitialized || targets == null || Time.frameCount < 5) return;
                        PrepareFixture(); phase = Phase.PrepareCase; break;
                    case Phase.PrepareCase:
                        if (Case == null) { Finish(null); return; }
                        PrepareCase(); break;
                    case Phase.Settle:
                        if (Time.frameCount - settleFrame < 2) return;
                        Require(bootstrap.map.TrySampleWorldPosition(bootstrap.robot.transform.position, out Case.player, out _), "The settled native fixture player must remain inside the map.");
                        if (Case.syntheticStartOverlap) CreateStartObstacle();
                        caseDeadline = EditorApplication.timeSinceStartup + 7;
                        phase = Phase.Spawn; break;
                    case Phase.Spawn:
                        TryPlacement(); break;
                }
            }
            catch (Exception error)
            {
                if (phase == Phase.Starting || Case == null) Finish(error);
                else FailCase(error);
            }
        }

        private void PrepareFixture()
        {
            // Disable tutorial advancement only after normal scene Start; native map/player systems stay active.
            foreach (MonoBehaviour component in targets.GetComponents<MonoBehaviour>())
            {
                string name = component.GetType().Name;
                if (name.StartsWith("Tutorial", StringComparison.Ordinal) && name.EndsWith("Lesson", StringComparison.Ordinal))
                    component.enabled = false;
            }
            targets.Configure(bootstrap);
            var large = (GameObject)typeof(TutorialLargeWasteTarget).GetField("largeGarbagePrefab", Flags)?.GetValue(targets);
            var medium = (GameObject)typeof(TutorialLargeWasteTarget).GetField("mediumFragmentPrefab", Flags)?.GetValue(targets);
            report.originalLargePrefab = AssetDatabase.GetAssetPath(large);
            report.originalMediumPrefab = AssetDatabase.GetAssetPath(medium);
            Require(report.originalLargePrefab == "Assets/Prefabs/Environment/Garbage/Big_Garbage.prefab"
                && report.originalMediumPrefab == "Assets/Prefabs/Environment/Garbage/Medium_Garbage.prefab",
                "The fixture must use the actual scene's original Big_Garbage and Medium_Garbage prefabs.");
            originalInput = InputSystem.settings; testInput = Instantiate(originalInput);
            testInput.hideFlags = HideFlags.HideAndDontSave;
            testInput.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            testInput.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus; InputSystem.settings = testInput;
            originalBackground = Application.runInBackground; Application.runInBackground = true;
            foreach (Gamepad other in new List<Gamepad>(Gamepad.all)) InputSystem.RemoveDevice(other);
            gamepadId = InputSystem.AddDevice<Gamepad>("Large-waste placement validation gamepad").deviceId;
            report.checks.Add("Loaded the real tutorial scene, generated its real map and retained native player, traversal and prefab systems. Only lesson advancement is suspended in this isolated fixture.");
        }

        private void PrepareCase()
        {
            ClearOwned();
            Vector2 point;
            if (Case.syntheticStartOverlap)
            {
                Require(hasBaseline, "The start-overlap regression needs a passing control at the exact same native map pose.");
                point = baselinePosition;
            }
            else if (!FindDryBodyPoint(Case.requestedPlayer, out point))
            {
                Case.skipped = true; Case.skippedReason = "No valid dry collision-free native body position within the fixture's 16 m search.";
                NextCase(); return;
            }
            // Pose changes are deliberate fixture setup, never production tutorial behavior.
            bootstrap.robot.transform.SetPositionAndRotation(bootstrap.map.MapPositionToWorld(point), Quaternion.Euler(0, 0, Case.heading));
            bootstrap.cameraFollow?.SnapToTarget();
            Case.player = point; Case.clearanceRadiusMeters = BodyRadiusMeters() + .15f;
            settleFrame = Time.frameCount; phase = Phase.Settle; Save();
        }

        private bool FindDryBodyPoint(Vector2 seed, out Vector2 point)
        {
            float radius = Mathf.Max(.1f, bootstrap.traversalEvaluator.RobotObstacleCollisionRadiusMeters);
            for (int ring = 0; ring <= 8; ring++)
                for (int y = -ring; y <= ring; y++)
                    for (int x = -ring; x <= ring; x++)
                    {
                        if (ring > 0 && Mathf.Abs(x) != ring && Mathf.Abs(y) != ring) continue;
                        Vector2 candidate = seed + new Vector2(x, y) * 2f;
                        if (!DryAndPassable(candidate) || WorldInteractionQuery.Query(InteractionShape.Circle(candidate, radius),
                            BlockingKinds, bootstrap.map, targets.gameObject.scene, ignoreHeld: bootstrap.robot.transform)) continue;
                        bool dry = true;
                        for (int i = 0; i < 8 && dry; i++)
                        {
                            float angle = i * Mathf.PI / 4f;
                            dry = DryAndPassable(candidate + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
                        }
                        if (dry) { point = candidate; return true; }
                    }
            point = default; return false;
        }

        private void CreateStartObstacle()
        {
            Require(bootstrap.map.TrySampleWorldPosition(bootstrap.robot.transform.position, out Vector2 start, out _), "The native player must remain on the map.");
            Case.player = start;
            float clearance = Case.clearanceRadiusMeters;
            float obstacleRadius = .22f;
            // Touch only the extra placement clearance where possible, leaving real body room.
            Vector2 obstaclePoint = start + Vector2.left * (clearance + obstacleRadius - .06f);
            syntheticObstacle = new GameObject("Placement fixture owned start-clearance obstacle");
            syntheticObstacle.transform.position = bootstrap.map.MapPositionToWorld(obstaclePoint);
            var interaction = syntheticObstacle.AddComponent<WorldInteraction>();
            interaction.ColliderType = ColliderType.Custom;
            interaction.ColliderShape = WorldInteractionColliderShape.Circle;
            interaction.ColliderRadius = bootstrap.map.MapMetersToWorldDistance(Vector2.right, obstacleRadius);
            interaction.SetKind(WorldInteractionKind.BodyCollision);
            bool blocked = true;
            for (int i = 0; i < 24; i++)
            {
                float angle = i * Mathf.PI / 12f;
                Vector2 end = start + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 6f;
                blocked &= WorldInteractionQuery.Query(InteractionShape.Capsule(start, end, clearance),
                    BlockingKinds, bootstrap.map, targets.gameObject.scene, ignoreHeld: bootstrap.robot.transform);
            }
            Case.legacyBlocksAllBearings = blocked;
            Require(blocked, "The fixture must reproduce the legacy whole-capsule deadlock at every one of the original 24 bearings.");
            Save();
        }

        private void TryPlacement()
        {
            Vector3 beforePosition = bootstrap.robot.transform.position;
            Quaternion beforeRotation = bootstrap.robot.transform.rotation;
            var watch = Stopwatch.StartNew();
            bool available = targets.EnsureTarget();
            watch.Stop(); Case.ensureCalls++;
            Case.totalEnsureMilliseconds += watch.Elapsed.TotalMilliseconds;
            Case.maximumEnsureMilliseconds = Math.Max(Case.maximumEnsureMilliseconds, watch.Elapsed.TotalMilliseconds);
            report.maximumEnsureMilliseconds = Math.Max(report.maximumEnsureMilliseconds, watch.Elapsed.TotalMilliseconds);
            Case.failureLastFailure = targets.LastFailure;
            Case.helperPreservesPlayerPose = bootstrap.robot.transform.position == beforePosition && bootstrap.robot.transform.rotation == beforeRotation;
            Require(Case.helperPreservesPlayerPose, "EnsureTarget must never teleport or rotate the player.");
            if (!available)
            {
                Save();
                Require(EditorApplication.timeSinceStartup < caseDeadline, "Native large-pile placement did not recover: " + targets.LastFailure);
                return;
            }
            ValidateTarget();
            if (caseIndex == 0) { baselinePosition = Case.player; hasBaseline = true; }
            Case.passed = true; NextCase();
        }

        private void ValidateTarget()
        {
            WorldInteraction target = targets.Target;
            Case.nativeBigPile = target != null && target.Available && target.Size == RecyclableSize.Big
                && !target.Recyclable && target.RequiredHands == 2 && targets.Pull != null;
            Require(Case.nativeBigPile, "The placed target must retain the real non-recyclable, two-hand Big_Garbage interaction and HeavyGarbagePull.");
            Require(bootstrap.map.TrySampleWorldPosition(target.transform.position, out Case.target, out _), "The pile must be inside the generated native map.");
            Case.approach = targets.ApproachMapPosition;
            InteractionShape shape = target.GetShape(bootstrap.map);
            float radius = ShapeRadius(shape);
            Case.targetRadiusMeters = radius;
            float clearRadius = radius + Mathf.Max(.75f, BodyRadiusMeters() * .4f);
            Case.dryFootprint = DryAndPassable(shape.Center) && DryAndPassable(Case.approach);
            for (int ring = 1; ring <= 2; ring++)
                for (int i = 0; i < 16; i++)
                {
                    float angle = i * Mathf.PI / 8f;
                    Case.dryFootprint &= DryAndPassable(shape.Center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * clearRadius * ring * .5f);
                }
            Require(Case.dryFootprint, "The large pile's whole clearance footprint and approach must remain dry and gently passable.");
            Transform owned = (GameObject)typeof(TutorialLargeWasteTarget).GetField("ownedRoot", Flags)?.GetValue(targets) is GameObject root
                ? root.transform : target.transform;
            Case.collisionFree = !WorldInteractionQuery.Query(InteractionShape.Circle(shape.Center, clearRadius),
                BlockingKinds | WorldInteractionKind.Grabbable | WorldInteractionKind.Climbable,
                bootstrap.map, targets.gameObject.scene, ignore: owned, ignoreHeld: bootstrap.robot.transform);
            Require(Case.collisionFree, "The placed large-pile clearance must not overlap existing native props, waste or the fixture obstacle.");
            var path = bootstrap.traversalEvaluator.EvaluateMapPath(Case.player, Case.approach);
            Require(path.HasData && path.IsPassable && !path.RequiresHardStop, "The native traversal evaluator must accept the approach.");
            if (Case.syntheticStartOverlap)
            {
                var capsule = InteractionShape.Capsule(Case.player, Case.approach, Case.clearanceRadiusMeters);
                bool legacyBlocked = WorldInteractionQuery.Query(capsule, BlockingKinds, bootstrap.map, targets.gameObject.scene,
                    ignore: owned, ignoreHeld: bootstrap.robot.transform);
                bool escapeBlocked = WorldInteractionQuery.Query(capsule, BlockingKinds, bootstrap.map, targets.gameObject.scene,
                    ignore: owned, previous: InteractionShape.Circle(Case.player, Case.clearanceRadiusMeters), ignoreHeld: bootstrap.robot.transform);
                Case.previousAllowsEscape = legacyBlocked && !escapeBlocked;
                Require(Case.previousAllowsEscape, "The actual accepted approach must reproduce legacy blockage and safely escape with the native previous-circle rule.");
                report.legacyRegressionReproduced = true;
                report.checks.Add("The exact passing shoreline control pose also spawns safely with an overlapping start-clearance obstacle. Legacy queries block all 24 bearings; previous-circle queries permit the actual native escape route.");
            }
        }

        private float BodyRadiusMeters()
        {
            var marker = bootstrap.robot.GetComponent<RobotMarkerView>();
            float bodyWorld = marker != null ? marker.BodyDiameter : .72f;
            if (marker != null && marker.MarkerVisualRoot != null)
                bodyWorld *= Mathf.Max(Mathf.Abs(marker.MarkerVisualRoot.lossyScale.x), Mathf.Abs(marker.MarkerVisualRoot.lossyScale.y));
            float scale = Mathf.Min(bootstrap.map.MapMetersToWorldDistance(Vector2.right, 1), bootstrap.map.MapMetersToWorldDistance(Vector2.up, 1));
            return bodyWorld * .5f / Mathf.Max(.0001f, scale);
        }
        private bool DryAndPassable(Vector2 point)
        {
            if (!bootstrap.map.TrySampleMapPosition(point, out _) || (bootstrap.map.TrySampleStaticWaterMapPosition(point, out float depth) && depth > .01f)) return false;
            var result = bootstrap.traversalEvaluator.EvaluateLocalSurface(bootstrap.map.MapPositionToWorld(point));
            return result.HasData && result.IsPassable && !result.RequiresHardStop
                && result.MaximumSurfaceSlopeAngle <= bootstrap.traversalEvaluator.LevelOneMaximumUphillAngle;
        }
        private static float ShapeRadius(InteractionShape shape)
        {
            if (!shape.IsBox) return shape.Radius + Vector2.Distance(shape.A, shape.B) * .5f;
            float radius = 0;
            for (int i = 0; i < 4; i++) radius = Mathf.Max(radius, Vector2.Distance(shape.Center, shape.Vertex(i)));
            return radius;
        }
        private void NeutralInput()
        {
            if (!running || !Current || phase == Phase.Finishing || InputState.currentUpdateType != InputUpdateType.Dynamic) return;
            if (InputSystem.GetDeviceById(gamepadId) is Gamepad pad) InputSystem.QueueStateEvent(pad, new GamepadState());
        }
        private void FailCase(Exception error)
        {
            Case.failureLastFailure = targets != null ? targets.LastFailure : null;
            report.failureLastFailure = Case.failureLastFailure;
            report.errors.Add(Case.name + ": " + error); NextCase();
        }
        private void NextCase() { ClearOwned(); caseIndex++; phase = Phase.PrepareCase; Save(); }
        private void ClearOwned()
        {
            if (targets != null) targets.ResetTarget();
            if (syntheticObstacle != null) { syntheticObstacle.SetActive(false); DestroyImmediate(syntheticObstacle); syntheticObstacle = null; }
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private void OnLog(string message, string stack, LogType type)
        {
            if (!Current || report == null) return;
            string normalized = message.Replace("\r\n", "\n").Trim();
            if (type == LogType.Error && (phase == Phase.Starting || phase == Phase.Finishing)
                && (normalized == KnownSceneHeader || normalized == KnownMissingStaticAnimal))
            {
                if (normalized == KnownSceneHeader) report.knownSceneHeaders++; else report.knownMissingPrefabMessages++;
                if (!report.knownSceneDiagnostics.Contains(normalized)) report.knownSceneDiagnostics.Add(normalized); return;
            }
            if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && !report.errors.Contains(message))
            { report.errors.Add(message); Save(); }
        }
        private void Save()
        {
            if (report == null || string.IsNullOrEmpty(output)) return;
            report.phase = phase.ToString();
            if (targets != null && !string.IsNullOrEmpty(targets.LastFailure)) report.failureLastFailure = targets.LastFailure;
            report.validCases = report.passedCases = report.skippedCases = 0;
            foreach (PlacementCase item in report.cases)
            { if (item.skipped) report.skippedCases++; else if (item.ensureCalls > 0) report.validCases++; if (item.passed) report.passedCases++; }
            File.WriteAllText(Path.Combine(output, "large-waste-placement-play-smoke.json"), JsonUtility.ToJson(report, true));
        }
        private void Finish(Exception error)
        {
            if (phase == Phase.Finishing) return;
            if (error != null) report.errors.Add(error.ToString());
            ClearOwned(); InputSystem.onBeforeUpdate -= NeutralInput;
            if (InputSystem.GetDeviceById(gamepadId) is InputDevice pad) InputSystem.RemoveDevice(pad);
            if (originalInput != null) InputSystem.settings = originalInput;
            if (testInput != null) DestroyImmediate(testInput);
            Application.runInBackground = originalBackground;
            phase = Phase.Finishing; Save(); EditorApplication.isPlaying = false;
        }
        private void Exit()
        {
            if (report.knownSceneHeaders > 0 && report.knownMissingPrefabMessages == 0)
                report.errors.Add("Scene-load error lacked the exact known missing-two-Static_Animal diagnostic.");
            if (report.passedCases < 8) report.errors.Add("Expected at least eight valid passing native map poses/headings; got " + report.passedCases + ".");
            if (!report.legacyRegressionReproduced) report.errors.Add("The start-overlap legacy deadlock and successful native escape were not both demonstrated.");
            report.passed = report.errors.Count == 0; Save(); running = false; Unsubscribe();
            EditorApplication.Exit(report.passed ? 0 : 1);
        }
    }
}
