using System;
using System.Collections.Generic;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AnimalGame.Demos.WasteRobot.Editor
{
    /// <summary>
    /// Explicit, isolated Edit Mode regression checks for a reusable machine
    /// prefab discovering its own scene's runtime-spawned player. Every player
    /// is an instance of the complete production player prefab; no production
    /// asset is changed, saved or rebuilt by this validator.
    /// </summary>
    public static class WasteRobotPlayerBindingValidation
    {
        private const string PlayerPath = "Assets/Prefabs/Resources/Robot/RobotMarker.prefab";
        private static readonly List<string> Passed = new List<string>();

        [MenuItem("Animal Game/Demos/Waste Robot/Validate Player Binding")]
        public static void Run()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode,
                "Run player binding validation from Edit Mode.");
            Passed.Clear();
            Scene previous = SceneManager.GetActiveScene();
            try
            {
                Check("default prefab enables automatic binding without a saved player", CheckDefaultPrefab);
                Check("unique complete player prefab binds within the same scene", CheckUniquePlayer);
                Check("a player spawned after an empty scene is found on retry", CheckDelayedPlayer);
                Check("foreign-scene candidates and explicit foreign motion are isolated", CheckSceneIsolation);
                Check("multiple unowned players are rejected as ambiguous", CheckAmbiguousPlayers);
                Check("an existing automatic target is stable when another player appears", CheckStableAutomaticTarget);
                Check("explicit binding wins over candidates and bootstrap preference", CheckExplicitTarget);
                Check("manual automatic-binding disable remains effective", CheckAutomaticBindingDisabled);
                Check("destroyed, disabled and migrated automatic targets are resampled safely", CheckInvalidAutomaticTargets);
                Check("the active same-scene map bootstrap player has priority", CheckBootstrapPriority);
                Debug.Log("Waste Robot player binding validation PASS (" + Passed.Count + " groups):\n - "
                    + string.Join("\n - ", Passed));
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded && SceneManager.GetActiveScene() != previous)
                    SceneManager.SetActiveScene(previous);
                WorldInteractionQuery.InvalidateSpatialIndex();
            }
        }

        private static void CheckDefaultPrefab()
        {
            GameObject asset = MachineAsset();
            WasteRobotBase machine = asset.GetComponent<WasteRobotBase>();
            Require(machine != null && machine.AutoBindPlayer,
                "Reusable machine prefab must enable scene-local automatic binding by default.");
            Require(machine.Target == null,
                "Reusable machine prefab must not save a Demo scene player reference.");
            using (var fixture = new BindingFixture())
            {
                Require(fixture.Machine.AutoBindPlayer && fixture.Machine.Target == null,
                    "Instantiating the prefab changed its default automatic-binding configuration.");
                fixture.Machine.Bind(null);
                Require(fixture.Machine.AutoBindPlayer,
                    "Bind(null) must clear the current target without disabling configured automatic binding.");
            }
        }

        private static void CheckUniquePlayer()
        {
            using (var fixture = new BindingFixture())
            {
                RobotMover player = fixture.SpawnPlayer(new Vector2(0f, 1f));
                Require(fixture.Machine.TryBindScenePlayer() && fixture.Machine.Target == player.transform,
                    "The unique active same-scene original player was not selected.");
                Require(!fixture.Machine.IsInside && fixture.Machine.TransitionCount == 0,
                    "Selecting an initial player must not count as a portal crossing.");
                fixture.MoveAndObserve(player, new Vector2(0f, -.5f));
                Require(fixture.Machine.IsInside && fixture.Machine.TransitionCount == 1,
                    "The automatically selected player cannot enter through the normal portal sampler.");
            }
        }

        private static void CheckDelayedPlayer()
        {
            using (var fixture = new BindingFixture())
            {
                for (int i = 0; i < 3; i++)
                    Require(!fixture.Machine.TryBindScenePlayer() && fixture.Machine.Target == null,
                        "A machine with no local player must remain safely unbound.");
                RobotMover player = fixture.SpawnPlayer(new Vector2(0f, 1f));
                Require(fixture.Machine.TryBindScenePlayer() && fixture.Machine.Target == player.transform,
                    "Retrying after the complete player prefab was spawned did not bind it.");
                fixture.MoveAndObserve(player, new Vector2(0f, -.5f));
                Require(fixture.Machine.IsInside && fixture.Machine.TransitionCount == 1,
                    "The delayed player could not perform its first real portal crossing.");
            }
        }

        private static void CheckSceneIsolation()
        {
            using (var fixture = new BindingFixture())
            using (var foreign = new PreviewSceneScope())
            {
                RobotMover other = fixture.SpawnPlayer(new Vector2(0f, 1f), foreign.Scene);
                Require(!fixture.Machine.TryBindScenePlayer() && fixture.Machine.Target == null,
                    "A player in another loaded scene must not be automatically selected.");
                RobotMover local = fixture.SpawnPlayer(new Vector2(0f, 3f));
                Require(fixture.Machine.TryBindScenePlayer() && fixture.Machine.Target == local.transform,
                    "A closer foreign player prevented the unique local player from binding.");

                // Explicit foreign references may remain assigned, but the normal
                // frame callback must never interpret their motion in this scene.
                fixture.Machine.Bind(other.transform);
                fixture.Machine.TryBindScenePlayer();
                Require(fixture.Machine.Target == other.transform,
                    "Automatic resolution overwrote an explicit foreign reference.");
                other.transform.position = fixture.Machine.PortalToWorld(new Vector2(0f, -.5f));
                fixture.Machine.SendMessage("LateUpdate", SendMessageOptions.RequireReceiver);
                Require(!fixture.Machine.IsInside && fixture.Machine.TransitionCount == 0,
                    "A player in a different scene drove this machine's portal state.");
            }
        }

        private static void CheckAmbiguousPlayers()
        {
            using (var fixture = new BindingFixture())
            {
                fixture.SpawnPlayer(new Vector2(0f, .5f));
                RobotMover second = fixture.SpawnPlayer(new Vector2(0f, 2f));
                Require(!fixture.Machine.TryBindScenePlayer() && fixture.Machine.Target == null,
                    "Ambiguous local players must not be selected by discovery order or proximity.");
                second.enabled = false;
                Require(fixture.Machine.TryBindScenePlayer(),
                    "A disabled RobotMover was counted as an eligible competing player.");
                Require(fixture.Machine.Target != second.transform,
                    "Automatic resolution selected a disabled RobotMover.");
            }
        }

        private static void CheckStableAutomaticTarget()
        {
            using (var fixture = new BindingFixture())
            {
                RobotMover first = fixture.SpawnPlayer(new Vector2(0f, 3f));
                Require(fixture.Machine.TryBindScenePlayer(), "The first unique player was not selected.");
                fixture.SpawnPlayer(new Vector2(0f, .1f));
                for (int i = 0; i < 3; i++) fixture.Machine.TryBindScenePlayer();
                Require(fixture.Machine.Target == first.transform,
                    "The stable automatic target was replaced when a closer second player appeared.");
            }
        }

        private static void CheckExplicitTarget()
        {
            using (var fixture = new BindingFixture())
            {
                RobotMover chosen = fixture.SpawnPlayer(new Vector2(0f, 1f));
                RobotMover suggested = fixture.SpawnPlayer(new Vector2(0f, 2f));
                fixture.CreateBootstrap(suggested);
                fixture.Machine.Bind(chosen.transform);
                for (int i = 0; i < 3; i++) fixture.Machine.TryBindScenePlayer();
                Require(fixture.Machine.Target == chosen.transform,
                    "Automatic bootstrap preference must not replace an explicitly bound target.");
                fixture.MoveAndObserve(chosen, new Vector2(0f, -.5f));
                Require(fixture.Machine.IsInside && fixture.Machine.TransitionCount == 1,
                    "Explicit binding lost its initial movement sample during automatic retries.");
            }
        }

        private static void CheckAutomaticBindingDisabled()
        {
            using (var fixture = new BindingFixture())
            {
                RobotMover player = fixture.SpawnPlayer(new Vector2(0f, 1f));
                fixture.Machine.AutoBindPlayer = false;
                fixture.Machine.Bind(null);
                Require(!fixture.Machine.TryBindScenePlayer() && fixture.Machine.Target == null,
                    "Automatic resolution ignored the manual disabled configuration.");
                Require(!fixture.Machine.AutoBindPlayer,
                    "Bind(null) unexpectedly re-enabled manually disabled automatic binding.");
                fixture.Machine.AutoBindPlayer = true;
                Require(fixture.Machine.TryBindScenePlayer() && fixture.Machine.Target == player.transform,
                    "Re-enabling automatic binding did not allow a new resolution attempt.");
            }
        }

        private static void CheckInvalidAutomaticTargets()
        {
            foreach (string failure in new[] { "destroyed", "disabled", "migrated" })
            {
                using (var fixture = new BindingFixture())
                using (var foreign = new PreviewSceneScope())
                {
                    RobotMover original = fixture.SpawnPlayer(new Vector2(0f, 1f));
                    Require(fixture.Machine.TryBindScenePlayer(), "Could not prepare the original automatic target.");
                    float pendingY = -fixture.Machine.CrossingHysteresis * .4f;
                    fixture.MoveAndObserve(original, new Vector2(0f, pendingY));
                    Require(!fixture.Machine.IsInside && fixture.Machine.TransitionCount == 0,
                        "The pending-crossing fixture switched too early.");
                    RobotMover replacement = fixture.SpawnPlayer(new Vector2(0f, -1f));
                    if (failure == "destroyed") Object.DestroyImmediate(original.gameObject);
                    else if (failure == "disabled") original.enabled = false;
                    else SceneManager.MoveGameObjectToScene(original.gameObject, foreign.Scene);

                    Vector3 before = replacement.transform.position;
                    Require(fixture.Machine.TryBindScenePlayer() && fixture.Machine.Target == replacement.transform,
                        "The " + failure + " automatic target was not replaced by the unique valid local player.");
                    Require(replacement.transform.position == before,
                        "Resolving a replacement target teleported the original player prefab.");
                    fixture.Machine.ObserveWorldPosition(replacement.transform.position);
                    Require(!fixture.Machine.IsInside && fixture.Machine.TransitionCount == 0,
                        "The " + failure + " target's old pending sample caused a false crossing after replacement.");
                    fixture.MoveAndObserve(replacement, new Vector2(0f, -.5f));
                    Require(!fixture.Machine.IsInside && fixture.Machine.TransitionCount == 0,
                        "Same-side replacement motion was mistaken for a crossing.");
                    fixture.MoveAndObserve(replacement, new Vector2(0f, 1f));
                    Require(!fixture.Machine.IsInside && fixture.Machine.TransitionCount == 0,
                        "Moving outward from an unentered machine caused an inverse entry.");
                    fixture.MoveAndObserve(replacement, new Vector2(0f, -.5f));
                    Require(fixture.Machine.IsInside && fixture.Machine.TransitionCount == 1,
                        "The replacement player cannot subsequently make a valid inward crossing.");
                }
            }
        }

        private static void CheckBootstrapPriority()
        {
            using (var fixture = new BindingFixture())
            {
                fixture.SpawnPlayer(new Vector2(0f, .5f));
                RobotMover actual = fixture.SpawnPlayer(new Vector2(0f, 2f));
                HeightMapPlayerSceneBootstrap bootstrap = fixture.CreateBootstrap(actual);
                Require(fixture.Machine.TryBindScenePlayer() && fixture.Machine.Target == actual.transform,
                    "The active local map bootstrap's runtime player must resolve an otherwise ambiguous pair.");
                Require(fixture.Machine.TransitionCount == 0,
                    "Bootstrap player selection counted an artificial crossing.");
                fixture.Machine.Bind(null);
                bootstrap.enabled = false;
                Require(!fixture.Machine.TryBindScenePlayer() && fixture.Machine.Target == null,
                    "A disabled map bootstrap must not resolve an ambiguous set of active players.");
            }
        }

        private static GameObject MachineAsset()
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(WasteRobotDemoBuilder.PrefabPath);
            Require(asset != null, "Build the reusable machine prefab before running player binding validation.");
            return asset;
        }

        private static void Check(string label, Action check)
        {
            check();
            Passed.Add(label);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Waste Robot player binding: " + message);
        }

        private sealed class PreviewSceneScope : IDisposable
        {
            public readonly Scene Scene = EditorSceneManager.NewPreviewScene();

            public void Dispose()
            {
                if (Scene.IsValid() && Scene.isLoaded) EditorSceneManager.ClosePreviewScene(Scene);
            }
        }

        private sealed class BindingFixture : IDisposable
        {
            private readonly PreviewSceneScope scene = new PreviewSceneScope();
            public Scene Scene => scene.Scene;
            public readonly WasteRobotBase Machine;

            public BindingFixture()
            {
                try
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(MachineAsset(), Scene);
                    Machine = instance.GetComponent<WasteRobotBase>();
                    Require(Machine != null, "Reusable machine prefab has no controller.");
                }
                catch
                {
                    scene.Dispose();
                    throw;
                }
            }

            public RobotMover SpawnPlayer(Vector2 localPosition, Scene destination = default)
            {
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
                Require(asset != null, "The complete original RobotMarker player prefab is missing.");
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset,
                    destination.IsValid() ? destination : Scene);
                instance.name = "Binding validation player";
                instance.transform.position = Machine.PortalToWorld(localPosition);
                RobotMover player = instance.GetComponent<RobotMover>();
                Require(player != null && player.isActiveAndEnabled,
                    "The complete original player prefab has no active RobotMover.");
                return player;
            }

            public HeightMapPlayerSceneBootstrap CreateBootstrap(RobotMover player)
            {
                var root = new GameObject("Binding validation bootstrap reference");
                SceneManager.MoveGameObjectToScene(root, Scene);
                // No ExecuteAlways: adding the real bootstrap in Edit Mode does
                // not execute its runtime Awake or create production resources.
                HeightMapPlayerSceneBootstrap bootstrap = root.AddComponent<HeightMapPlayerSceneBootstrap>();
                bootstrap.robot = player;
                return bootstrap;
            }

            public void MoveAndObserve(RobotMover player, Vector2 localPosition)
            {
                player.transform.position = Machine.PortalToWorld(localPosition);
                Machine.ObserveWorldPosition(player.transform.position);
            }

            public void Dispose() => scene.Dispose();
        }
    }
}
