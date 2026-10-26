using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AnimalGame.Animals;
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
            RunOrderingChecks();
            Debug.Log("Checkpoint Refit validation PASS: isolated terminal/UI prefabs, dedicated UI assets, and original-player Demo scene.");
        }

        internal static PhotoCameraDefinition CameraDefinition(string propertyName)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(TerminalPath);
            var terminal = asset != null ? asset.GetComponent<CheckpointRefitTerminal>() : null;
            Require(terminal != null, "The camera ordering fixture requires the authored terminal.");
            SerializedProperty property = new SerializedObject(terminal).FindProperty(propertyName);
            return property != null ? property.objectReferenceValue as PhotoCameraDefinition : null;
        }

        /// <summary>Small real Toolkit/equipment checks, independent of the full gameplay map.</summary>
        [MenuItem("Animal Game/Demos/Checkpoint Refit/Validate Equipment Order")]
        public static void RunOrderingChecks()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Leave Play Mode before validating equipment order.");
            int checks = 0;
            void Check(bool condition, string message) { Require(condition, message); checks++; }
            PhotoCameraDefinition zoom = CameraDefinition("zoomLens300");
            PhotoCameraDefinition damaged = CameraDefinition("damagedPrime50");
            Check(zoom != null && damaged != null && zoom != damaged, "The two authored camera definitions must resolve independently.");

            using (var fixture = new OrderingFixture(zoom, damaged))
            {
                CheckpointRefitView view = fixture.View;
                view.Show("ORDER CHECK", "ORDER-01"); fixture.Layout();
                Check(view.SelectedSlotIndex == 0 && view.IsSelectedModuleInstalled,
                    "The installed tracked undercarriage must remain the first LEGS module.");
                view.NavigateSlot(1); fixture.Layout();
                Check(view.GetCameraModule(0) == zoom && view.GetCameraModule(1) == damaged
                    && view.GetCameraModule(2) == null && view.SelectedModuleIndex == 0 && view.IsSelectedModuleInstalled,
                    "CAMERA must initially begin with the actual installed zoom lens and retain its unavailable third card.");

                view.NavigateModule(1);
                Check(fixture.Equipment.TryEquip(view.SelectedCamera), "The selected unlocked damaged camera must equip.");
                view.RefreshEquipment(); fixture.Layout();
                Check(view.GetCameraModule(0) == damaged && view.GetCameraModule(1) == zoom
                    && view.SelectedModuleIndex == 0 && view.SelectedCamera == damaged && view.IsSelectedModuleInstalled
                    && fixture.Root.focusController.focusedElement == fixture.Root.Q<Button>("refit-module-0"),
                    "Equipping the second camera must move it, selection and focus to the leftmost card.");
                CheckpointRefitPreviewValidation.CheckModulePresentation(view);
                Check(fixture.Root.Q<Label>("refit-detail-title").text == damaged.DisplayName
                    && fixture.Root.Q<Label>("refit-equip-hint").text.Contains("DAMAGED")
                    && !fixture.Root.Q<Button>("refit-equip").enabledInHierarchy,
                    "Details and installation actions must continue to describe the moved damaged camera.");

                view.Hide(); view.Show("ORDER CHECK", "ORDER-02");
                view.NavigateSlot(1); view.NavigateSlot(-1); fixture.Layout();
                Check(view.GetCameraModule(0) == damaged && view.SelectedModuleIndex == 0 && view.IsSelectedModuleInstalled,
                    "Reopening and returning to CAMERA must retain the equipped-first order.");
                VisualTreeAsset tree = view.Document.visualTreeAsset;
                view.Document.visualTreeAsset = null; view.Document.visualTreeAsset = tree;
                view.Show("ORDER CHECK", "ORDER-03"); fixture.Layout();
                Check(view.GetCameraModule(0) == damaged && view.SelectedCamera == damaged
                    && fixture.Root.Q<Button>("refit-module-0").ClassListContains("refit-selected"),
                    "Rebuilding the actual Toolkit document must preserve the equipped card and selection.");

                PhotoCameraDefinition requested = null;
                int requests = 0;
                view.EquipRequested += () => { requested = view.SelectedCamera; requests++; };
                view.NavigateModule(1); view.RequestEquipSelected();
                Check(requests == 1 && requested == zoom && fixture.Equipment.TryEquip(requested),
                    "The second displayed card must request the zoom camera after the damaged camera has moved left.");
                view.RefreshEquipment(); fixture.Layout();
                Check(view.GetCameraModule(0) == zoom && view.GetCameraModule(1) == damaged
                    && view.SelectedModuleIndex == 0 && view.SelectedCamera == zoom
                    && fixture.Root.Q<Label>("refit-equip-hint").text.Contains("GOOD"),
                    "Equipping back to the zoom camera must restore its first position and good-condition details.");

                fixture.Controller.SetCamera(damaged);
                OrderingFixture.Call(view, "LateUpdate"); fixture.Layout();
                Check(view.GetCameraModule(0) == damaged && view.IsModuleInstalled(1, 0)
                    && view.SelectedCamera == zoom && view.SelectedModuleIndex == 1,
                    "An external equipment change must update the order without changing the selected camera's identity.");
                view.NavigateModule(1); view.RequestEquipSelected();
                Check(view.SelectedModuleIndex == 2 && !view.IsSelectedModuleAvailable && requests == 1,
                    "The unavailable third card must never become equipable after either sort direction.");
            }
            using (var fixture = new OrderingFixture(zoom, damaged, false))
            {
                fixture.View.Show("LOCK CHECK", "ORDER-04"); fixture.View.NavigateSlot(1); fixture.View.NavigateModule(1);
                Check(!fixture.View.IsSelectedModuleAvailable && !fixture.Equipment.TryEquip(damaged)
                    && fixture.Controller.CurrentCamera == zoom && fixture.View.GetCameraModule(0) == zoom,
                    "Sorting must not unlock an undiscovered camera or change the installed camera.");
            }
            Debug.Log("Checkpoint Refit equipment ordering checks PASS: " + checks
                + " checks; equipped-first cards, selection and focus, camera identities, reopening, document rebuild, external changes and unavailable modules.");
        }

        private sealed class OrderingFixture : IDisposable
        {
            private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            private readonly Scene scene;
            private readonly PanelSettings settings;
            private readonly RenderTexture texture;
            public readonly CheckpointRefitView View;
            public readonly PhotoModeController Controller;
            public readonly RefitCameraEquipment Equipment;
            public VisualElement Root => View.Document.rootVisualElement;

            public OrderingFixture(PhotoCameraDefinition zoom, PhotoCameraDefinition damaged, bool unlockDamaged = true)
            {
                scene = EditorSceneManager.NewPreviewScene();
                var profile = AssetDatabase.LoadAssetAtPath<GameObject>(UIPath).GetComponent<UIDocument>();
                settings = UnityEngine.Object.Instantiate(profile.panelSettings);
                texture = new RenderTexture(1920, 1080, 24); texture.Create(); settings.targetTexture = texture;
                var ui = new GameObject("Refit ordering UI"); SceneManager.MoveGameObjectToScene(ui, scene);
                var document = ui.AddComponent<UIDocument>();
                document.panelSettings = settings; document.visualTreeAsset = profile.visualTreeAsset;
                View = ui.AddComponent<CheckpointRefitView>();
                var owner = new GameObject("Refit ordering camera owner"); owner.SetActive(false);
                SceneManager.MoveGameObjectToScene(owner, scene);
                Controller = owner.AddComponent<PhotoModeController>(); Controller.SetCamera(zoom);
                Equipment = owner.AddComponent<RefitCameraEquipment>(); Equipment.Configure(Controller, zoom);
                if (unlockDamaged) Equipment.Unlock(damaged);
                View.BindCameraEquipment(Equipment, zoom, damaged);
            }

            public void Layout()
            {
                Require(Root.panel != null, "The real refit UXML must attach to a runtime Toolkit panel.");
                Call(Root.panel, "ValidateLayout");
                Call(View, "FitStage");
                Call(Root.panel, "ValidateLayout");
            }

            public static void Call(object target, string method)
                => target.GetType().GetMethod(method, Flags).Invoke(target, null);

            public void Dispose()
            {
                EditorSceneManager.ClosePreviewScene(scene);
                texture.Release(); UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(settings);
            }
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
