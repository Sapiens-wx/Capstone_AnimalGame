using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnimalGame.RobotMap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace AnimalGame.Demos.CheckpointRefit.Editor
{
    /// <summary>Explicit checks only. Never rebuilds assets or saves an existing scene.</summary>
    public static class CheckpointRefitValidation
    {
        public const string RootPath = "Assets/Demos/CheckpointRefit";
        public const string TerminalPath = RootPath + "/Prefabs/CheckpointRefitTerminal.prefab";
        public const string UIPath = RootPath + "/Prefabs/CheckpointRefitUI.prefab";
        public const string ScenePath = RootPath + "/Scenes/CheckpointRefitDemo.unity";

        [MenuItem("Animal Game/Demos/Checkpoint Refit/Validate")]
        public static void Run()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Leave Play Mode before validating.");
            GameObject terminalAsset = AssetDatabase.LoadAssetAtPath<GameObject>(TerminalPath);
            GameObject uiAsset = AssetDatabase.LoadAssetAtPath<GameObject>(UIPath);
            Require(terminalAsset != null && uiAsset != null, "Build the independent Demo prefabs first.");
            CheckpointRefitTerminal terminal = terminalAsset.GetComponent<CheckpointRefitTerminal>();
            Require(terminal != null, "Terminal prefab has no interaction controller.");
            SerializedProperty uiReference = new SerializedObject(terminal).FindProperty("uiPrefab");
            Require(uiReference != null && uiReference.objectReferenceValue == uiAsset,
                "Terminal must reference the separate Demo UI prefab.");
            Require(terminalAsset.GetComponentsInChildren<Renderer>(true).Length > 0,
                "The reusable interaction terminal needs visible placeholder geometry.");
            CheckpointRefitView view = uiAsset.GetComponent<CheckpointRefitView>();
            UIDocument document = uiAsset.GetComponent<UIDocument>();
            Require(view != null && document != null, "UI prefab needs its View and UIDocument on the root.");
            Require(document.visualTreeAsset != null && document.panelSettings != null,
                "UI prefab must have persisted UXML and PanelSettings references.");
            Require(AssetDatabase.GetAssetPath(document.visualTreeAsset).StartsWith(RootPath + "/", StringComparison.Ordinal)
                && AssetDatabase.GetAssetPath(document.panelSettings).StartsWith(RootPath + "/", StringComparison.Ordinal),
                "UI layout and PanelSettings must be dedicated Demo assets.");
            string markup = File.ReadAllText(AssetDatabase.GetAssetPath(document.visualTreeAsset));
            Require(!markup.Contains("使用限制") && !markup.Contains("用途"), "UI must retain the approved English copy.");

            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.OpenPreviewScene(ScenePath);
            try
            {
                CheckpointRefitTerminal[] terminals = Components<CheckpointRefitTerminal>(scene);
                RobotMover[] players = Components<RobotMover>(scene);
                Require(terminals.Length == 1 && players.Length == 1,
                    "Independent Demo scene must contain one terminal and one original player.");
                Require(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(terminals[0].gameObject) == TerminalPath,
                    "Demo scene must instance the reusable terminal prefab.");
                Require(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(players[0].gameObject)
                    == "Assets/Prefabs/Resources/Robot/RobotMarker.prefab",
                    "Demo must instance the original player prefab without changing that asset.");
                Require(!Components<MonoBehaviour>(scene).Any(component => component != null
                    && component.GetType().Name == "HeightMapPlayerSceneBootstrap"),
                    "Independent flat-ground Demo must not start a map bootstrap.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
            Debug.Log("Checkpoint Refit validation PASS: isolated terminal/UI prefabs, dedicated UI assets, and original-player Demo scene.");
        }

        public static void RunBatch()
        {
            try { Run(); if (Application.isBatchMode) EditorApplication.Exit(0); }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode) EditorApplication.Exit(1); else throw;
            }
        }

        private static T[] Components<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

        internal static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Checkpoint Refit: " + message);
        }
    }
}
