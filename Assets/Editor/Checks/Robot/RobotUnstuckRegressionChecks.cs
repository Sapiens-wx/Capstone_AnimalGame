using System;
using System.Reflection;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    public static class RobotUnstuckRegressionChecks
    {
        private static readonly Vector2 Centre = new(16f, 16f);

        public static void RunBatch()
        {
            try
            {
                CheckNearestOverlapEscape();
                CheckWallBlocksTeleport();
                CheckBodyStaysInsideMap();
                CheckWaterAndSlope();
                Debug.Log("Robot unstuck checks PASS: clear pose, nearest overlap escape, wall, map edge, water and steep slope.");
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        private static void CheckNearestOverlapEscape()
        {
            using var f = new Fixture();
            f.Find("AlreadyClear", out _);
            WorldInteraction prop = f.Obstacle(Centre + Vector2.left, new Vector2(1.5f, 2f),
                WorldInteractionKind.BodyCollision | WorldInteractionKind.Pushable);
            Vector3 propBefore = prop.WorldPosition;
            f.Find("Found", out Vector2 destination);
            Require(Vector2.Distance(destination, Centre + Vector2.right) < 0.001f,
                "Unstuck did not choose the nearest sample with room for the full vehicle and its collision skin.");
            Require(prop.WorldPosition == propBefore, "Searching moved the pushable obstacle.");
        }

        private static void CheckWallBlocksTeleport()
        {
            using var f = new Fixture();
            f.Obstacle(new Vector2(17f, 16f), new Vector2(0.1f, 32f));
            f.Obstacle(new Vector2(8.25f, 17.75f), new Vector2(16.5f, 0.2f));
            f.Obstacle(new Vector2(8.25f, 14.25f), new Vector2(16.5f, 0.2f));
            f.Place(new Vector2(18f, 16f));
            f.Find("AlreadyClear", out _);
            f.Place(Centre);
            f.Find("NoSafePosition", out _);
        }

        private static void CheckBodyStaysInsideMap()
        {
            using var f = new Fixture();
            f.Mover.LocalSize = new Vector2(3.5f, 2f);
            f.Place(new Vector2(1f, 16f));
            f.Find("Found", out Vector2 inside);
            Require(Vector2.Distance(inside, new Vector2(2f, 16f)) < 0.001f,
                "Unstuck accepted a vehicle that overhangs the map boundary.");
        }

        private static void CheckWaterAndSlope()
        {
            using (var f = new Fixture())
            {
                f.Map.Presentation.FillStaticWaterDepth(0.5f);
                f.Find("AlreadyClear", out _);
                f.Map.Presentation.PaintStaticWaterDepth(Centre, 2.5f, StaticWaterDepthPaintMode.Set,
                    targetDepthMeters: 3f, depthStepMeters: 0f, strength: 1f, hardness: 1f);
                f.Find("Found", out Vector2 destination);
                Require(f.Map.TrySampleStaticWaterMapPosition(destination, out float depth)
                    && depth <= f.Map.Presentation.MaximumPassableStaticWaterDepthMeters,
                    "Unstuck accepted a deep-water landing.");
                f.Map.Presentation.FillStaticWaterDepth(3f);
                f.Find("NoSafePosition", out _);
            }

            using var steep = new Fixture(160f);
            steep.Find("NoSafePosition", out _);
        }

        private sealed class Fixture : IDisposable
        {
            private static readonly MethodInfo FindDestination = typeof(RobotMover).Assembly
                .GetType("AnimalGame.RobotMap.RobotUnstuckSearch", throwOnError: true)
                .GetMethod("Find", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            private readonly Scene scene = EditorSceneManager.NewPreviewScene();
            private readonly Texture2D height = new(2, 2, TextureFormat.R16, false, true);
            private readonly TiledMapTestFixture mapFixture;
            private readonly HeightMapTraversalEvaluator evaluator;
            public readonly MapTestSceneController Map;
            public readonly RobotMover Mover;

            public Fixture(float elevationRange = 1f)
            {
                ushort high = elevationRange > 1f ? ushort.MaxValue : (ushort)0;
                height.SetPixelData(new ushort[] { 0, 0, high, high }, 0);
                height.Apply();
                Map = MakeObject("Unstuck map").AddComponent<MapTestSceneController>();
                Map.enabled = false;
                mapFixture = new TiledMapTestFixture(Map, height, 32f, 2f, elevationRange);
                evaluator = Map.gameObject.AddComponent<HeightMapTraversalEvaluator>();
                var evaluatorSettings = new SerializedObject(evaluator);
                evaluatorSettings.FindProperty("robotFootprintLengthMeters").floatValue = 2f;
                evaluatorSettings.FindProperty("robotFootprintWidthMeters").floatValue = 1.5f;
                evaluatorSettings.ApplyModifiedPropertiesWithoutUndo();
                evaluator.Initialize(Map);
                Mover = MakeObject("Robot").AddComponent<RobotMover>();
                Mover.transform.localScale = new Vector3(2f, 2f, 1f);
                Mover.CollisionShape = CollisionShape.Box;
                Mover.LocalSize = new Vector2(1.5f, 2f);
                Mover.SetTraversalEvaluator(evaluator);
                Place(Centre);
            }

            public void Place(Vector2 point) => Mover.transform.position = Map.MapPositionToWorld(point);

            public void Find(string expected, out Vector2 destination)
            {
                var before = Mover.transform.position;
                var args = new object[] { Mover, evaluator, Map, Vector2.zero };
                object outcome = FindDestination.Invoke(null, args);
                Require(outcome.ToString() == expected, $"Expected {expected}, received {outcome}.");
                Require(Mover.transform.position == before, "Destination search moved the robot.");
                destination = default;
                if (expected == "Found")
                    Require(Map.TrySampleWorldPosition((Vector2)args[3], out destination, out _),
                        "Search returned a position without map height data.");
            }

            public WorldInteraction Obstacle(Vector2 point, Vector2 size,
                WorldInteractionKind kind = WorldInteractionKind.Collision)
            {
                var prop = MakeObject("Unstuck obstacle").AddComponent<WorldInteraction>();
                prop.ColliderType = ColliderType.Custom;
                prop.ColliderShape = WorldInteractionColliderShape.Box;
                prop.LocalSize = size;
                prop.LocalScale = new Vector3(2f, 2f, 1f);
                prop.WorldPosition = Map.MapPositionToWorld(point);
                prop.SetKind(kind);
                return prop;
            }

            private GameObject MakeObject(string name)
            {
                var go = new GameObject(name);
                SceneManager.MoveGameObjectToScene(go, scene);
                return go;
            }

            public void Dispose()
            {
                mapFixture.Dispose();
                Object.DestroyImmediate(height);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
