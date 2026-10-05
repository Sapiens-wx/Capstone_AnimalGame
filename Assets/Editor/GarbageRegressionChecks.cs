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
            CheckPushSpeedResponse();
            Debug.Log("Garbage checks PASS: fragment path, movement index and push speed response");
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

        private static void CheckPushSpeedResponse()
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                RobotMover mover = NewObject("Push speed robot", scene, Vector2.zero)
                    .AddComponent<RobotMover>();
                Set(mover, "forwardSpeed", 5f);
                Set(mover, "reverseSpeed", 3f);
                Set(mover, "overallMotionScale", 1f);
                Set(mover, "launchAcceleration", 2.2f);
                Set(mover, "runningAcceleration", 4.8f);
                Set(mover, "coastDeceleration", 2.6f);
                Set(mover, "brakingDeceleration", 8f);
                const float dt = 1f / 60f;
                const float pushMultiplier = .4f;

                Set(mover, "forwardSpeed", 4.2f);
                Set(mover, "overallMotionScale", .88f);
                foreach (float frameTime in new[] { 1f / 30f, 1f / 60f, 1f / 120f })
                {
                    SetCurrentSpeed(mover, 3.696f);
                    StepDrive(mover, 1f, 3.696f, 1f, .201f, frameTime);
                    RequireNear(mover.CurrentSpeed, .742896f,
                        "Configured forward impact retained full speed at dt " + frameTime);
                    Require(mover.CurrentSpeed * frameTime <= .742896f * frameTime + .000001f,
                        "First forward push displacement exceeded its speed limit at dt " + frameTime);

                    SetCurrentSpeed(mover, -2.64f);
                    StepDrive(mover, -1f, -2.64f, 1f, .201f, frameTime);
                    RequireNear(mover.CurrentSpeed, -.53064f,
                        "Configured reverse impact retained full speed at dt " + frameTime);
                    Require(Mathf.Abs(mover.CurrentSpeed * frameTime) <= .53064f * frameTime + .000001f,
                        "First reverse push displacement exceeded its speed limit at dt " + frameTime);
                }
                Set(mover, "forwardSpeed", 5f);
                Set(mover, "overallMotionScale", 1f);

                SetCurrentSpeed(mover, 5f);
                StepDrive(mover, 1f, 5f, 1f, pushMultiplier, dt);
                RequireNear(mover.CurrentSpeed, 2f,
                    "Forward impact did not limit the very first pushing frame");
                for (int i = 0; i < 120; i++)
                    StepDrive(mover, 1f, 5f, 1f, pushMultiplier, dt);
                RequireNear(mover.CurrentSpeed, 2f,
                    "Sustained pushing compounded resistance instead of maintaining speed");

                SetCurrentSpeed(mover, -3f);
                StepDrive(mover, -1f, -3f, 1f, pushMultiplier, dt);
                RequireNear(mover.CurrentSpeed, -1.2f,
                    "Reverse impact used the wrong directional speed limit");

                foreach (float speed in new[] { 5f, -3f })
                {
                    SetCurrentSpeed(mover, speed);
                    StepDrive(mover, Mathf.Sign(speed), speed, 1f, 0f, dt);
                    RequireNear(mover.CurrentSpeed, 0f,
                        "Zero push multiplier allowed driven movement");
                    SetCurrentSpeed(mover, speed);
                    StepDrive(mover, Mathf.Sign(speed), speed, 1f, 1f, dt);
                    RequireNear(mover.CurrentSpeed, speed,
                        "Unit push multiplier changed unobstructed full-speed movement");
                }

                SetCurrentSpeed(mover, 0f);
                StepDrive(mover, 1f, 5f, 1f, 1f, dt);
                RequireNear(mover.CurrentSpeed, 2.2f * dt,
                    "Unit push multiplier changed ordinary launch acceleration");

                SetCurrentSpeed(mover, 1f);
                StepDrive(mover, 0f, 0f, 1f, pushMultiplier, dt);
                RequireNear(mover.CurrentSpeed, 1f - 2.6f * dt,
                    "Releasing throttle while pushing stopped coasting immediately");
                SetCurrentSpeed(mover, -1f);
                StepDrive(mover, 0f, 0f, 1f, pushMultiplier, dt);
                RequireNear(mover.CurrentSpeed, -1f + 2.6f * dt,
                    "Reverse coasting used the throttle's forward direction");

                SetCurrentSpeed(mover, 5f);
                StepDrive(mover, -1f, -3f, 1f, pushMultiplier, dt);
                RequireNear(mover.CurrentSpeed, 2f,
                    "Reverse braking used the requested direction before velocity crossed zero");
                SetCurrentSpeed(mover, 1f);
                StepDrive(mover, -1f, -3f, 1f, pushMultiplier, dt);
                RequireNear(mover.CurrentSpeed, 1f - 8f * dt,
                    "Reverse input skipped braking or applied the wrong deceleration");
                for (int i = 0; i < 120; i++)
                    StepDrive(mover, -1f, -3f, 1f, pushMultiplier, dt);
                RequireNear(mover.CurrentSpeed, -1.2f,
                    "Reverse braking could not settle into sustained reverse pushing");

                SetCurrentSpeed(mover, 0f);
                for (int i = 0; i < 120; i++)
                    StepDrive(mover, .5f, 2.5f, 1f, pushMultiplier, dt);
                RequireNear(mover.CurrentSpeed, 1f,
                    "Partial throttle ignored the reduced pushing target speed");

                Set(mover, "overallMotionScale", .5f);
                mover.SetGrabResistance(.623f);
                float surfaceMultiplier = .55f * .8f * mover.GrabMovementMultiplier;
                Vector2 terrainVelocity = new Vector2(.7f, -.2f);
                typeof(RobotMover).GetProperty("CurrentTerrainVelocity")
                    .SetValue(mover, terrainVelocity);
                SetCurrentSpeed(mover, 2.5f);
                StepDrive(mover, 1f, 2.5f * surfaceMultiplier,
                    surfaceMultiplier, pushMultiplier, dt, 20f);
                RequireNear(mover.CurrentSpeed, 2.5f * surfaceMultiplier * pushMultiplier,
                    "Forward pushing failed to combine overall, slope, water and grab scaling once");
                SetCurrentSpeed(mover, -1.5f);
                StepDrive(mover, -1f, -1.5f * surfaceMultiplier,
                    surfaceMultiplier, pushMultiplier, dt);
                RequireNear(mover.CurrentSpeed, -1.5f * surfaceMultiplier * pushMultiplier,
                    "Reverse pushing failed to combine surface and overall scaling");
                Require(mover.CurrentTerrainVelocity == terrainVelocity,
                    "Driven push resistance changed independent terrain sliding");

                Set(mover, "overallMotionScale", 0f);
                SetCurrentSpeed(mover, 1f);
                StepDrive(mover, 1f, 0f, surfaceMultiplier, pushMultiplier, dt);
                RequireNear(mover.CurrentSpeed, 0f,
                    "Zero overall movement scale left pushing velocity behind");

                Set(mover, "overallMotionScale", 1f);
                mover.SetGrabResistance(0f);
                SetCurrentSpeed(mover, 2f);
                StepDrive(mover, 1f, 5f, 1f, 1f, dt);
                Require(mover.CurrentSpeed > 2f && mover.CurrentSpeed < 5f,
                    "Leaving push contact failed to resume gradual acceleration");
                for (int i = 0; i < 120; i++)
                    StepDrive(mover, 1f, 5f, 1f, 1f, dt);
                RequireNear(mover.CurrentSpeed, 5f,
                    "Leaving push contact retained the pushing speed limit");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static void StepDrive(RobotMover mover, float throttle, float targetSpeed,
            float surfaceMultiplier, float pushMultiplier, float deltaTime,
            float accelerationBonus = 0f) => Invoke(mover, "UpdateDriveSpeed",
                throttle, targetSpeed, surfaceMultiplier, pushMultiplier, accelerationBonus, deltaTime);

        private static void SetCurrentSpeed(RobotMover mover, float speed) =>
            typeof(RobotMover).GetProperty("CurrentSpeed").SetValue(mover, speed);

        private static void RequireNear(float actual, float expected, string message) =>
            Require(Mathf.Abs(actual - expected) < .0001f,
                message + ": expected " + expected + ", got " + actual);

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
