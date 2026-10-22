using System;
using System.Collections.Generic;
using AnimalGame.Animals;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AnimalGame.Editor
{
    // Run in an isolated project: validates Unity's actual OnEnable/Update dispatch,
    // not just explicit calls from the deterministic edit-mode regression fixtures.
    public static class AnimalAIPlayModeSmoke
    {
        private static AnimalAgent first, second;
        private static AnimalSimulation simulation;
        private static TiledMapTestFixture tile;
        private static Texture2D texture;
        private static AnimalSpeciesConfig config;
        private static double started;
        private static float pausedAt, initialHealth;
        private static bool paused, resumed, hit;
        public static void RunBatch()
        {
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.isPlaying = true;
        }
        private static void OnPlayMode(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            try
            {
                GameObject mapObject = new GameObject("Smoke map");
                var map = mapObject.AddComponent<MapTestSceneController>(); map.enabled = false;
                texture = new Texture2D(2, 2);
                texture.SetPixels(new[] { Color.gray, Color.gray, Color.gray, Color.gray }); texture.Apply();
                tile = new TiledMapTestFixture(map, texture, 32f, .25f);
                config = ScriptableObject.CreateInstance<AnimalSpeciesConfig>();
                var settings = new SerializedObject(config);
                settings.FindProperty("initialHealthRange").vector2Value = new Vector2(100f, 100f);
                settings.FindProperty("baseDetectionChancePerCheck").floatValue = 0f;
                settings.FindProperty("baseFleeChancePerCheck").floatValue = 0f;
                settings.FindProperty("dailyBehaviours").ClearArray();
                settings.ApplyModifiedPropertiesWithoutUndo();
                first = Spawn(map, new Vector2(3f, 3f));
                second = Spawn(map, new Vector2(8f, 3f));
                simulation = AnimalSimulation.ForScene(first.gameObject.scene);
                started = EditorApplication.timeSinceStartup;
                paused = resumed = hit = false;
                EditorApplication.update += Check;
            }
            catch (Exception error) { Finish(error); }
        }
        private static AnimalAgent Spawn(MapTestSceneController map, Vector2 position)
        {
            GameObject go = new GameObject("Smoke animal"); go.transform.position = map.MapPositionToWorld(position);
            var animal = go.AddComponent<AnimalAgent>();
            animal.ConfigureEditorDefaults(config, null, null);
            var placed = new SerializedObject(go.GetComponent<HeightMapPlacedObject>());
            placed.FindProperty("map").objectReferenceValue = map;
            placed.ApplyModifiedPropertiesWithoutUndo();
            return animal;
        }
        private static void Check()
        {
            try
            {
                double elapsed = EditorApplication.timeSinceStartup - started;
                if (elapsed > 20d) throw new Exception("Animal PlayMode dispatch timed out.");
                if (!first.IsInitialized || !second.IsInitialized) return;
                if (!hit)
                {
                    initialHealth = second.Health.CurrentHealth;
                    Require(simulation.Hit(first, second.transform, false), "PlayMode attack not registered");
                    hit = true;
                }
                if (!paused && simulation.Time > .4f)
                {
                    Time.timeScale = 0f;
                    pausedAt = simulation.Time;
                    paused = true;
                    started = EditorApplication.timeSinceStartup;
                    return;
                }
                if (paused && !resumed)
                {
                    if (elapsed < .25d) return;
                    Require(Mathf.Abs(simulation.Time - pausedAt) < .0001f, "Real Update dispatch advanced while paused");
                    Require(Mathf.Abs(second.Health.CurrentHealth - initialHealth) < .0001f, "Delayed damage resolved while paused");
                    Time.timeScale = 1f;
                    resumed = true;
                }
                if (resumed && simulation.Time >= 1.5f)
                {
                    Require(Mathf.Abs(second.Health.CurrentHealth - (initialHealth - config.AttackDamage)) < .001f, "PlayMode delayed damage did not resolve once");
                    Require(first.Runtime != second.Runtime, "Real spawned agents share runtime state");
                    Require(UnityEngine.Object.FindObjectsByType<AnimalSimulation>(FindObjectsSortMode.None).Length == 1, "More than one automatic simulation was created");
                    Debug.Log("Animal AI PlayMode smoke PASS: automatic registration/update, one scene simulation, scaled pause and delayed damage.");
                    Finish(null);
                }
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Check;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            Time.timeScale = 1f;
            if (error != null) Debug.LogException(error);
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
