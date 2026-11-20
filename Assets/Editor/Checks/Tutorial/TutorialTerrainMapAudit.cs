using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AnimalGame.MapTest;
using AnimalGame.Tutorial;
using AnimalGame.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    /// <summary>Read-only terrain/prop survey. Runs only in the disposable validation project.</summary>
    public sealed class TutorialTerrainMapAudit : ScriptableObject
    {
        [Serializable] private sealed class Point
        {
            public string name, direction, reason, level;
            public Vector2 map, world;
            public float height, signedSlope, surfaceSlope, step;
            public bool hasData, passable, hardStop;
        }
        [Serializable] private sealed class PathResult
        {
            public string name, reason, level;
            public Vector2 start, end, firstUnsafe;
            public float length, maxUphill, maxDownhill, maxSurfaceSlope, maxStep;
            public bool hasData, passable, hardStop, allShortSegmentsPassable;
        }
        [Serializable] private sealed class Prop
        {
            public string name, type, kind;
            public Vector2 map, world;
            public bool available;
        }
        [Serializable] private sealed class Report
        {
            public bool passed;
            public string error;
            public float levelOneMax, levelThreeStart, maxDownhill, maxStep, footprintLength, footprintWidth;
            public Vector2 scanPosition;
            public int scanCandidates, scanMarkers, passableVisible, unsafeVisible;
            public List<Point> points = new List<Point>();
            public List<PathResult> paths = new List<PathResult>();
            public List<Prop> props = new List<Prop>();
        }

        [SerializeField] private bool running, finishing, passed;
        [SerializeField] private double deadline;
        [SerializeField] private string output;
        [SerializeField] private Report report;
        [SerializeField] private HeightMapPlayerSceneBootstrap scanBootstrap;
        [SerializeField] private int scanPhase, scanFrame;
        [SerializeField] private float scanStarted;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void RunBatch()
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            if (!Application.isBatchMode || !project.Contains("/output/validation/"))
                throw new InvalidOperationException("Run terrain audit only in a disposable output/validation project.");
            var session = CreateInstance<TutorialTerrainMapAudit>();
            session.hideFlags = HideFlags.HideAndDontSave;
            session.output = Path.Combine(project, "output/validation/tutorial-terrain");
            Directory.CreateDirectory(session.output);
            session.deadline = EditorApplication.timeSinceStartup + 180;
            EditorSceneManager.OpenScene(TutorialDrivingLesson.ScenePath, OpenSceneMode.Single);
            session.running = true;
            session.Subscribe();
            EditorApplication.isPlaying = true;
        }

        private void OnEnable() { if (running) Subscribe(); }
        private void OnDisable() { EditorApplication.update -= Tick; }
        private void Subscribe() { EditorApplication.update -= Tick; EditorApplication.update += Tick; }
        private void Tick()
        {
            if (!running) return;
            if (finishing)
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    running = false;
                    EditorApplication.update -= Tick;
                    EditorApplication.Exit(passed ? 0 : 1);
                }
                return;
            }
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Terrain audit startup timed out.");
                EditorApplication.QueuePlayerLoopUpdate();
                if (!EditorApplication.isPlaying) return;
                if (scanPhase > 0)
                {
                    var scan = scanBootstrap.scanOverlay;
                    if (scanPhase == 1)
                    {
                        if (Time.frameCount - scanFrame < 8) return;
                        typeof(TraversalScanOverlayUI).GetMethod("BeginScannedSnapshot", Private).Invoke(scan, null);
                        scanStarted = Time.unscaledTime; scanPhase = 2;
                        return;
                    }
                    bool revealing = (bool)typeof(TraversalScanOverlayUI).GetField("scanIsRevealing", Private).GetValue(scan);
                    if (revealing && Time.unscaledTime - scanStarted < 5) return;
                    typeof(TraversalScanOverlayUI).GetMethod("RenderMarkers", Private).Invoke(scan, null);
                    report.scanCandidates = scan.ScanCandidateCount;
                    report.scanMarkers = scan.VisibleMarkerCount;
                    report.passableVisible = ((ICollection)typeof(TraversalScanOverlayUI).GetField("passableRenderData", Private).GetValue(scan)).Count;
                    report.unsafeVisible = ((ICollection)typeof(TraversalScanOverlayUI).GetField("unpassableRenderData", Private).GetValue(scan)).Count;
                    report.passed = report.passableVisible > 0 && report.unsafeVisible > 0;
                    SaveReport();
                    Debug.Log("TUTORIAL TERRAIN AUDIT COMPLETE: " + output);
                    Finish(report.passed);
                    return;
                }
                var bootstrap = Object.FindFirstObjectByType<HeightMapPlayerSceneBootstrap>();
                if (bootstrap == null || bootstrap.map == null || !bootstrap.map.HasGeneratedMap
                    || bootstrap.traversalEvaluator == null || !bootstrap.traversalEvaluator.IsInitialized) return;
                report = Audit(bootstrap.map, bootstrap.traversalEvaluator);
                report.scanPosition = new Vector2(163, 140);
                SaveReport();
                // This test-only relocation is confined to the disposable Play Mode session.
                // The scene, height field, obstacles, movement and scanner settings are never saved or edited.
                bootstrap.robot.transform.position = bootstrap.map.MapPositionToWorld(report.scanPosition);
                bootstrap.cameraFollow.SnapToTarget();
                scanBootstrap = bootstrap; scanFrame = Time.frameCount; scanPhase = 1;
            }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(output, "map-audit.json"), JsonUtility.ToJson(new Report { error = error.ToString() }, true));
                Debug.LogException(error);
                Finish(false);
            }
        }

        private static Report Audit(MapTestSceneController map, HeightMapTraversalEvaluator evaluator)
        {
            var report = new Report
            {
                levelOneMax = evaluator.LevelOneMaximumUphillAngle, levelThreeStart = evaluator.LevelThreeUphillAngle,
                maxDownhill = evaluator.MaximumDownhillSlopeAngle, maxStep = evaluator.MaximumStepHeightMeters,
                footprintLength = evaluator.RobotFootprintLengthMeters, footprintWidth = evaluator.RobotFootprintWidthMeters
            };
            var candidates = new Dictionary<string, Vector2>
            {
                { "Spawn", new Vector2(200, 191) },
                { "Survey", new Vector2(163, 140) },
                { "SafeEnd", new Vector2(146, 121) },
                { "MarkedPoint1Exact", new Vector2(163.22f, 139.50f) },
                { "MarkedPoint2Exact", new Vector2(145.64f, 121.25f) },
                { "Observer", new Vector2(165, 140) },
                { "SteepA", new Vector2(158, 145) },
                { "SteepB", new Vector2(155, 140) },
                { "SteepC", new Vector2(155, 142) },
                { "WaypointA", new Vector2(187, 170) },
                { "WaypointB", new Vector2(175, 145) }
            };
            foreach (var candidate in candidates)
                foreach (Vector2 direction in new[] { Vector2.up, Vector2.right, Vector2.down, Vector2.left })
                    report.points.Add(SamplePoint(candidate.Key, candidate.Value, direction, map, evaluator));
            for (int y = 118; y <= 144; y += 2)
                for (int x = 142; x <= 166; x += 2)
                    report.points.Add(SamplePoint("CircleGrid", new Vector2(x, y), Vector2.up, map, evaluator));

            report.paths.Add(SamplePath("Spawn direct to survey", candidates["Spawn"], candidates["Survey"], evaluator));
            report.paths.Add(SamplePath("Point 1 to point 2 slope practice", candidates["Survey"], candidates["SafeEnd"], evaluator));
            report.paths.Add(SamplePath("Spawn to exact marked point 1", candidates["Spawn"], candidates["MarkedPoint1Exact"], evaluator));
            report.paths.Add(SamplePath("Exact marked point 1 to point 2", candidates["MarkedPoint1Exact"], candidates["MarkedPoint2Exact"], evaluator));
            report.paths.Add(SamplePath("Survey to observer", candidates["Survey"], candidates["Observer"], evaluator));
            report.paths.Add(SamplePath("Observer towards steep A", candidates["Observer"], candidates["SteepA"], evaluator));
            report.paths.Add(SamplePath("Observer towards steep B", candidates["Observer"], candidates["SteepB"], evaluator));
            report.paths.Add(SamplePath("Spawn to waypoint A", candidates["Spawn"], candidates["WaypointA"], evaluator));
            report.paths.Add(SamplePath("Waypoint A to survey", candidates["WaypointA"], candidates["Survey"], evaluator));
            report.paths.Add(SamplePath("Spawn to waypoint B", candidates["Spawn"], candidates["WaypointB"], evaluator));
            report.paths.Add(SamplePath("Waypoint B to survey", candidates["WaypointB"], candidates["Survey"], evaluator));
            foreach (WorldInteraction interaction in Object.FindObjectsByType<WorldInteraction>(FindObjectsSortMode.None))
            {
                if (interaction.gameObject.scene != map.gameObject.scene) continue;
                if (!map.TrySampleWorldPosition(interaction.transform.position, out Vector2 position, out _)) continue;
                report.props.Add(new Prop { name = interaction.name, type = interaction.GetType().Name,
                    kind = interaction.Kind.ToString(), map = position, world = interaction.transform.position,
                    available = interaction.Available });
            }
            report.passed = true;
            return report;
        }

        private static Point SamplePoint(string name, Vector2 position, Vector2 direction,
            MapTestSceneController map, HeightMapTraversalEvaluator evaluator)
        {
            Vector2 world = map.MapPositionToWorld(position);
            map.TrySampleMapPosition(position, out float height);
            SlopeTraversalResult result = evaluator.EvaluateCurrentSurface(world, map.MapDirectionToWorldDirection(direction));
            return new Point { name = name, direction = direction.ToString(), map = position, world = world, height = height,
                hasData = result.HasData, passable = result.IsPassable, hardStop = result.RequiresHardStop,
                signedSlope = result.SignedSlopeAngle, surfaceSlope = result.MaximumSurfaceSlopeAngle,
                step = result.MaximumStepHeight, reason = result.BlockReason.ToString(), level = result.UphillLevel.ToString() };
        }

        private static PathResult SamplePath(string name, Vector2 start, Vector2 end, HeightMapTraversalEvaluator evaluator)
        {
            SlopeTraversalResult result = evaluator.EvaluateMapPath(start, end);
            var path = new PathResult { name = name, start = start, end = end, length = Vector2.Distance(start, end),
                hasData = result.HasData, passable = result.IsPassable, hardStop = result.RequiresHardStop,
                reason = result.BlockReason.ToString(), level = result.UphillLevel.ToString(),
                maxUphill = result.MaximumUphillAngle, maxDownhill = result.MaximumDownhillAngle,
                maxSurfaceSlope = result.MaximumSurfaceSlopeAngle, maxStep = result.MaximumStepHeight,
                allShortSegmentsPassable = true };
            int steps = Mathf.CeilToInt(path.length / .5f);
            for (int i = 0; i < steps; i++)
            {
                Vector2 a = Vector2.Lerp(start, end, i / (float)steps);
                Vector2 b = Vector2.Lerp(start, end, (i + 1) / (float)steps);
                SlopeTraversalResult piece = evaluator.EvaluateMapPath(a, b);
                if (piece.IsPassable && !piece.RequiresHardStop) continue;
                path.allShortSegmentsPassable = false; path.firstUnsafe = a; break;
            }
            return path;
        }

        private void Finish(bool result)
        {
            passed = result; finishing = true;
            EditorApplication.isPlaying = false;
        }

        private void SaveReport() => File.WriteAllText(Path.Combine(output, "map-audit.json"), JsonUtility.ToJson(report, true));
    }
}
