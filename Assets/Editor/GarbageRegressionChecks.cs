using System;
using System.Reflection;
using AnimalGame.Garbage;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AnimalGame.Editor
{
    public static class GarbageRegressionChecks
    {
        [MenuItem("Animal Game/Validation/Run Garbage Checks")]
        public static void Run()
        {
            CheckFragmentPath();
            CheckMotionUpdatesSpatialIndex();
            Debug.Log("Garbage checks PASS: fragment path and movement index");
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void CheckFragmentPath()
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject source = NewObject("Source", scene, Vector2.zero);
                WorldInteraction original = source.AddComponent<WorldInteraction>();
                original.SetKind(WorldInteractionKind.None);
                GarbageFragmentSpawner spawner = source.AddComponent<GarbageFragmentSpawner>();
                Invoke(spawner, "Awake");
                Vector3 origin = new Vector3(2f, 2f);
                Vector2 right = Vector2.right;
                GameObject wall = NewObject("Wall", scene, new Vector2(2.5f, 2f));
                WorldInteraction obstacle = wall.AddComponent<WorldInteraction>();
                obstacle.SetKind(WorldInteractionKind.Collision);
                obstacle.LocalSize = Vector2.one * .2f;
                Require(!SafePath(spawner, origin, right, 2f, .2f),
                    "Fragment path crossed a wall");
                obstacle.enabled = false;
                Require(SafePath(spawner, origin, right, 2f, .2f),
                    "Clear fragment path was rejected");
                GameObject robot = NewObject("Robot", scene, new Vector2(2.15f, 2f));
                RobotMover mover = robot.AddComponent<RobotMover>();
                Set(spawner, "player", mover);
                Require(!SafePath(spawner, origin, right, 2f, .2f),
                    "Fragment path crossed the player");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static void CheckMotionUpdatesSpatialIndex()
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject go = NewObject("Moving garbage", scene, Vector2.zero);
                WorldInteraction item = go.AddComponent<WorldInteraction>();
                item.SetKind(WorldInteractionKind.Collision | WorldInteractionKind.Pushable);
                item.LocalSize = Vector2.one * .2f;
                GarbageMotion motion = go.AddComponent<GarbageMotion>();
                Invoke(motion, "Awake");
                motion.ApplyExternalPush(Vector2.right * 3f);
                Invoke(motion, "Update");
                Require(WorldInteractionQuery.Query(InteractionShape.Capsule(Vector2.right * 3f,
                        Vector2.right * 3f, 0f), WorldInteractionKind.Collision, null, scene),
                    "Spatial index missed moved garbage");
                Require(!WorldInteractionQuery.Query(InteractionShape.Capsule(Vector2.zero,
                        Vector2.zero, 0f), WorldInteractionKind.Collision, null, scene),
                    "Spatial index retained the old garbage position");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static GameObject NewObject(string name, Scene scene, Vector2 position)
        {
            var result = new GameObject(name);
            SceneManager.MoveGameObjectToScene(result, scene);
            result.transform.position = position;
            return result;
        }

        private static bool SafePath(GarbageFragmentSpawner spawner, Vector3 origin,
            Vector2 direction, float speed, float radius) => (bool)Invoke(spawner,
            "SafePath", origin, direction, speed, radius);

        private static object Invoke(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);

        private static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
