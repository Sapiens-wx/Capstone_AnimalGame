using System;
using System.Reflection;
using AnimalGame.RobotMap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AnimalGame.Editor
{
    public static class AnimalCollisionFeedbackChecks
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        public static void RunBatch()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            float originalTimeScale = Time.timeScale;
            try
            {
                Time.timeScale = 1f;
                var player = New(scene, "Player");
                var mover = player.AddComponent<RobotMover>();
                var receiver = player.AddComponent<PlayerCombatReceiver>();
                Call(receiver, "Awake");
                var shake = New(scene, "Animal impact camera").AddComponent<RobotCameraShake>();
                var reference = New(scene, "Solid obstacle reference").AddComponent<RobotCameraShake>();
                shake.Initialize(mover, null, null);
                Vector2 direction = new Vector2(.8f, .6f);
                receiver.Knockback(direction, .1f);
                Call(reference, "AddObstacleCollisionImpact", direction, 1f);
                EqualFeedback(shake, reference);
                Require((float)Get(shake, "springZoom") > 0f, "Animal impact produced no immediate feedback");

                // Reinitializing a camera must not multiply its collision subscription.
                shake.Initialize(mover, null, null); shake.Initialize(mover, null, null);
                receiver.Knockback(direction, .1f);
                Call(reference, "AddObstacleCollisionImpact", direction, 1f);
                EqualFeedback(shake, reference);

                mover.IgnoreAnimalDetection = true;
                receiver.Knockback(direction, .1f); EqualFeedback(shake, reference);
                mover.IgnoreAnimalDetection = false;
                Time.timeScale = 0f;
                receiver.Knockback(direction, .1f); EqualFeedback(shake, reference);
                Time.timeScale = 1f;
                typeof(RobotCameraShake).GetField("enableCameraShake", Fields).SetValue(shake, false);
                receiver.Knockback(direction, .1f); EqualFeedback(shake, reference);
                typeof(RobotCameraShake).GetField("enableCameraShake", Fields).SetValue(shake, true);

                var otherPlayer = New(scene, "Other player");
                var otherMover = otherPlayer.AddComponent<RobotMover>();
                var otherReceiver = otherPlayer.AddComponent<PlayerCombatReceiver>(); Call(otherReceiver, "Awake");
                shake.Initialize(otherMover, null, null);
                receiver.Knockback(direction, .1f); EqualFeedback(shake, reference);
                otherReceiver.Knockback(direction, .1f);
                Call(reference, "AddObstacleCollisionImpact", direction, 1f); EqualFeedback(shake, reference);
                EditorSceneManager.ClosePreviewScene(scene);
                AnimalSpeciesRegressionChecks.Run();
                Debug.Log("Animal collision feedback PASS: exact solid-obstacle spring/rumble feedback, immediate simulation hit, repeated initialization, ignored player, pause, shake toggle and camera rebinding.");
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
                EditorApplication.Exit(1);
            }
            finally { Time.timeScale = originalTimeScale; }
        }
        private static GameObject New(Scene scene, string name)
        { var root = new GameObject(name); SceneManager.MoveGameObjectToScene(root, scene); return root; }
        private static object Get(object instance, string field) => instance.GetType().GetField(field, Fields).GetValue(instance);
        private static void Call(object instance, string method, params object[] args) => instance.GetType().GetMethod(method, Fields).Invoke(instance, args);
        private static void EqualFeedback(RobotCameraShake actual, RobotCameraShake expected)
        {
            foreach (string field in new[] { "springPosition", "springPositionVelocity", "springRotation", "springRotationVelocity", "springZoom", "springZoomVelocity", "obstacleCollisionRumbleStrength" })
                Require(Get(actual, field).Equals(Get(expected, field)), "Animal feedback differs from solid-obstacle feedback: " + field);
        }
        private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    }
}
