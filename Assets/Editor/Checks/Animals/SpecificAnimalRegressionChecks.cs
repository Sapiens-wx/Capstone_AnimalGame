using System;
using System.Collections.Generic;
using System.Reflection;
using AnimalGame.AnimalDebug;
using AnimalGame.Animals;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    public static class SpecificAnimalRegressionChecks
    {
        [MenuItem("Animal Game/Validation/Run Specific Animal AI Checks")]
        public static void Run()
        {
            var random = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(281026);
                CheckAssetsAndDailyCommands();
                CheckJuvenilesAndSocialCombat();
                CheckBurrowSafetyAndIdentity();
                CheckFlightAndTreeProtection();
                Debug.Log("Specific animal AI checks PASS: all configured daily commands, prefabs, juvenile rules, harmless sparring, burrow identity/safety, flying prey protection, dead-tree refuge and quill defense.");
            }
            finally { UnityEngine.Random.state = random; }
        }
        public static void RunBatch()
        {
            try
            {
                Run();
                AnimalAIRegressionChecks.Run();
                AnimalSpeciesRegressionChecks.Run();
                TreeBirdRegressionChecks.Run();
                AnimalDebugUIRegressionChecks.Run();
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
        private static Fixture playFixture;
        private static AnimalAgent playBird, playMarmot, playPorcupine;
        private static double playStarted;
        private static int playPhase;
        private static float pausedSimulationTime;
        private static Vector2 pausedBirdPosition;
        public static void RunPlayModeBatch()
        {
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorApplication.playModeStateChanged += StartPlayChecks;
            EditorApplication.EnterPlaymode();
        }
        private static void StartPlayChecks(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= StartPlayChecks;
            try
            {
                playFixture = new Fixture(false);
                playFixture.Tree(new Vector2(18f, 18f), true);
                playFixture.Spawn("AmericanBison", new Vector2(4f, 4f));
                playFixture.Spawn("Pronghorn", new Vector2(6f, 4f));
                playBird = playFixture.Spawn("PrairieGrouse", new Vector2(8f, 8f));
                playMarmot = playFixture.Spawn("AmericanMarmot", new Vector2(12f, 12f));
                playPorcupine = playFixture.Spawn("NorthAmericanPorcupine", new Vector2(16f, 16f));
                playBird.Brain.Request(AnimalState.Fleeing);
                playMarmot.BeginHiding();
                playPorcupine.Brain.Request(AnimalState.Fleeing);
                playStarted = EditorApplication.timeSinceStartup; playPhase = 0;
                EditorApplication.update += CheckPlayDispatch;
            }
            catch (Exception error) { FinishPlayChecks(error); }
        }
        private static void CheckPlayDispatch()
        {
            try
            {
                double elapsed = EditorApplication.timeSinceStartup - playStarted;
                if (elapsed > 20) throw new Exception("Specific animal Play Mode timed out.");
                if (playPhase == 0 && playFixture.Simulation.Time > .3f)
                {
                    Require(playBird.Motor.IsAerialMovement && playMarmot.IsHidden, "Play Mode flight/hiding did not dispatch");
                    pausedSimulationTime = playFixture.Simulation.Time;
                    pausedBirdPosition = playBird.Motor.CurrentMapPosition;
                    Time.timeScale = 0f; playPhase = 1; playStarted = EditorApplication.timeSinceStartup;
                }
                else if (playPhase == 1 && elapsed > .4)
                {
                    Require(playFixture.Simulation.Time == pausedSimulationTime && playBird.Motor.CurrentMapPosition == pausedBirdPosition,
                        "Pause advanced animal simulation or flight");
                    Time.timeScale = 1f; playPhase = 2;
                }
                else if (playPhase == 2 && playFixture.Simulation.Time > pausedSimulationTime + 1.5f)
                {
                    Require(playBird.Motor.CurrentMapPosition != pausedBirdPosition, "Flight did not resume");
                    Require(((AmericanMarmotBehaviour)playMarmot.Behaviour).HomeBurrow != null, "Play Mode lost spawned burrow");
                    Require(((NorthAmericanPorcupineBehaviour)playPorcupine.Behaviour).CurrentTree != null, "Play Mode escape did not reach tree");
                    Debug.Log("Specific animal Play Mode PASS: real dispatcher, five species, spawned burrow, tree refuge, flying escape and pause/resume.");
                    FinishPlayChecks(null);
                }
            }
            catch (Exception error) { FinishPlayChecks(error); }
        }
        private static void FinishPlayChecks(Exception error)
        {
            EditorApplication.update -= CheckPlayDispatch; Time.timeScale = 1f;
            if (error != null) Debug.LogException(error);
            EditorApplication.Exit(error == null ? 0 : 1);
        }
        private static readonly string[] Species = { "AmericanBison", "Pronghorn", "PrairieGrouse", "AmericanMarmot", "NorthAmericanPorcupine" };
        private static void CheckAssetsAndDailyCommands()
        {
            foreach (string species in Species)
            {
                using var f = new Fixture();
                f.Tree(new Vector2(12f, 10f)); f.Tree(new Vector2(16f, 10f));
                var animal = f.Spawn(species, new Vector2(10f, 10f));
                float[] health = { 20f, 10f, 3f, 3f, 5f };
                Require(animal.Health.CurrentHealth == health[Array.IndexOf(Species, species)], species + " health default");
                Require(animal.Config.MaximumWalkSlopeDegrees == 38f, species + " slope default");
                Require(animal.GetComponent<AnimalPhotoSubject>().Species != AnimalSpecies.Unknown, species + " photo identity");
                Require(AnimalDebugSelection.IsSelectable(animal, f.Scene), species + " missing from Animal Inspector selection");
                if (species == "AmericanBison" || species == "Pronghorn")
                {
                    var partner = f.Spawn(species, new Vector2(10f, 12f));
                    var group = f.Simulation.GetGroup("social"); group.Add(animal); group.Add(partner);
                }
                foreach (var settings in animal.Config.DailyBehaviours)
                {
                    var result = AnimalDebugCommandService.ExecuteDaily(animal, settings);
                    Require(result.Status != AnimalDebugCommandStatus.Rejected, species + " daily rejected: " + settings.Action.name + ": " + result.Message);
                    for (int i = 0; i < 1600 && result.Status == AnimalDebugCommandStatus.Started; i++) f.Tick(.05f);
                    Require(result.Status == AnimalDebugCommandStatus.Succeeded, species + "/" + settings.Action.name + ": " + result.Message);
                }
                Require(!animal.IsHidden && animal.Health.CurrentHealth == health[Array.IndexOf(Species, species)], species + " daily lost identity/health");
            }
        }
        private static void CheckJuvenilesAndSocialCombat()
        {
            foreach (string species in new[] { "AmericanBison", "Pronghorn" })
            {
                using var f = new Fixture();
                var a = f.Spawn(species, new Vector2(8f, 8f));
                var b = f.Spawn(species, new Vector2(10f, 8f));
                var juvenile = f.Spawn(species, new Vector2(12f, 8f), true);
                var group = f.Simulation.GetGroup("herd"); group.Add(a); group.Add(b); group.Add(juvenile);
                Require(juvenile.Behaviour.IsJuvenile && !juvenile.SupportsAggression, "Juvenile aggression capability");
                Require(!SparringAction.CanStart(juvenile.Context), "Juvenile can initiate sparring");
                juvenile.Brain.Request(AnimalState.Aggressive);
                Require(juvenile.CurrentState == AnimalState.Fleeing, "Juvenile did not flee instead of attacking");
                juvenile.ReturnToDaily();
                a.Brain.StartAction(new SparringAction(2f));
                Require(a.Runtime.Sparring != null && ReferenceEquals(a.Runtime.Sparring, b.Runtime.Sparring), "Adult partner selection");
                float ah = a.Health.CurrentHealth, bh = b.Health.CurrentHealth, jh = juvenile.Health.CurrentHealth;
                AnimalImpact.Apply(a.Context, Vector2.right, true);
                Require(!f.Simulation.Hit(a, juvenile.transform, true, true), "Social hit accepted");
                f.Tick(8f);
                Require(a.Health.CurrentHealth == ah && b.Health.CurrentHealth == bh && juvenile.Health.CurrentHealth == jh, "Social sparring injured an animal");
                Require(!a.Motor.IsDisplaced && !b.Motor.IsDisplaced && !juvenile.Motor.IsDisplaced, "Social sparring caused displacement");
                Require(a.Runtime.Sparring == null && b.Runtime.Sparring == null, "Social pair never released");
            }
        }
        private static void CheckBurrowSafetyAndIdentity()
        {
            using var f = new Fixture();
            var marmot = f.Spawn("AmericanMarmot", new Vector2(8f, 8f));
            var other = f.Spawn("AmericanMarmot", new Vector2(14f, 8f));
            var behaviour = (AmericanMarmotBehaviour)marmot.Behaviour;
            var home = behaviour.HomeBurrow;
            Require(home != null && home.GetComponentInChildren<SpriteRenderer>() == null, "Burrow is missing or visible");
            marmot.Brain.StartAction(new BurrowAction(2f)); f.Tick(.2f);
            Require(marmot.IsHidden && marmot.CurrentState == AnimalState.Daily, "Daily burrow action did not conceal marmot");
            Require(AnimalDebugSelection.IsSelectable(marmot, f.Scene), "Daily hidden marmot disappeared from inspector");
            Require(AnimalDebugSelection.TryBounds(marmot, out var dailyBounds) && dailyBounds.center == marmot.transform.position,
                "Daily hidden marmot marker is not anchored to the burrow");
            Require(!marmot.IsPresent && !marmot.GetComponent<AnimalPhotoSubject>().IsPhotographable(), "Debug visibility exposed marmot to gameplay");
            marmot.ReturnToDaily();
            marmot.Motor.TeleportToMapPosition(new Vector2(14f, 9f));
            // A resident can be elsewhere while its burrow is shared.
            other.Motor.TeleportToMapPosition(new Vector2(20f, 8f));
            var predator = f.Spawn("AmericanBison", new Vector2(14f, 11f));
            Set(predator.Config, "preySpecies", new List<AnimalSpeciesConfig> { marmot.Config });
            marmot.NotifyThreat(predator.transform);
            f.Tick(1.05f);
            Require(Get<bool>(behaviour, "alarmPlayed"), "Marmot alarm did not fire after one second");
            marmot.Brain.Request(AnimalState.Fleeing);
            for (int i = 0; i < 150 && marmot.CurrentState != AnimalState.Hiding; i++) f.Tick(.05f);
            Require(marmot.CurrentState == AnimalState.Hiding && marmot.IsHidden, "Marmot failed to hide: " + marmot.CurrentState + "/" + marmot.Brain.CurrentActionName + " at " + marmot.Motor.CurrentMapPosition);
            Require(AnimalDebugSelection.IsSelectable(marmot, f.Scene), "Frightened hidden marmot disappeared from inspector");
            Require(AnimalDebugSelection.TryBounds(marmot, out var escapeBounds) && escapeBounds.center == marmot.transform.position,
                "Frightened hidden marmot marker is not anchored to the borrowed burrow");
            Require(behaviour.HidingBurrow == ((AmericanMarmotBehaviour)other.Behaviour).HomeBurrow, "Marmot did not use nearest burrow");
            float health = marmot.Health.CurrentHealth;
            Require(!marmot.ReceiveDamage(1f, predator.transform) && !AnimalHuntTargets.Eligible(predator.Context, marmot), "Hidden marmot can be attacked/hunted");
            Require(!marmot.GetComponent<AnimalPhotoSubject>().IsPhotographable(), "Hidden marmot can be photographed");
            f.Tick(6f); Require(marmot.IsHidden, "Marmot emerged beside a predator");
            predator.Motor.TeleportToMapPosition(new Vector2(30f, 30f));
            f.Tick(4.8f); Require(marmot.IsHidden, "Marmot emerged before five safe seconds");
            predator.Motor.TeleportToMapPosition(new Vector2(14f, 11f));
            f.Tick(.1f); predator.Motor.TeleportToMapPosition(new Vector2(30f, 30f));
            f.Tick(4.8f); Require(marmot.IsHidden, "Interrupted safe time was not reset");
            f.Tick(.3f);
            Require(!marmot.IsHidden && marmot.Health.CurrentHealth == health && behaviour.HomeBurrow == home, "Emergence reset home or health");
            Require(marmot.ActivityCenter == marmot.HomeMapPosition, "Foreign burrow changed daily activity center");
        }
        private static void CheckFlightAndTreeProtection()
        {
            using var f = new Fixture();
            var bird = f.Spawn("PrairieGrouse", new Vector2(8f, 8f));
            var predator = f.Spawn("AmericanBison", new Vector2(10f, 8f));
            var porcupine = f.Spawn("NorthAmericanPorcupine", new Vector2(16f, 16f));
            Set(predator.Config, "preySpecies", new List<AnimalSpeciesConfig> { bird.Config, porcupine.Config });
            bird.Brain.Request(AnimalState.Fleeing); f.Tick(.05f);
            Require(bird.Motor.IsAerialMovement && !AnimalHuntTargets.Eligible(predator.Context, bird), "Flying grouse is ground prey");
            Require(!f.Simulation.Hit(predator, bird.transform, true), "Ground melee hit a flying bird");
            Require(bird.GetComponent<AnimalPhotoSubject>().IsPhotographable(), "Flying bird is not photographable");
            float before = bird.Motor.CurrentMapPosition.x; f.Tick(.3f);
            Require(Vector2.Distance(bird.Motor.CurrentMapPosition, new Vector2(before, 8f)) > .1f, "Flight did not move");
            var deadTree = f.Tree(new Vector2(18f, 16f), true);
            porcupine.NotifyThreat(predator.transform);
            porcupine.Brain.Request(AnimalState.Aggressive);
            f.Tick(.05f);
            var p = (NorthAmericanPorcupineBehaviour)porcupine.Behaviour;
            Require(p.QuillsRaised, "Porcupine did not raise quills");
            predator.Motor.TeleportToMapPosition(new Vector2(17f, 16f));
            f.Tick(.4f);
            Vector2 away = porcupine.Motor.CurrentMapPosition - predator.Motor.CurrentMapPosition;
            Require(Vector2.Dot(porcupine.Motor.FacingMapDirection, away.normalized) > .9f, "Porcupine did not face away");
            float health = predator.Health.CurrentHealth;
            porcupine.ReceiveDamage(1f, predator.transform);
            Require(predator.Health.CurrentHealth == health - 3f, "Quill retaliation was not exactly three damage");
            Require(!porcupine.ReceiveDamage(0f, predator.transform) && predator.Health.CurrentHealth == health - 3f, "Zero damage caused retaliation");
            predator.Motor.TeleportToMapPosition(new Vector2(30f, 30f)); f.Tick(.1f);
            Require(porcupine.CurrentState == AnimalState.Daily && !p.QuillsRaised, "Quill defense did not end at alert radius");
            porcupine.Brain.Request(AnimalState.Fleeing);
            for (int i = 0; i < 300 && p.CurrentTree == null; i++) f.Tick(.05f);
            Require(p.CurrentTree == deadTree && porcupine.Motor.IsPerchedMovement, "Porcupine could not seek a dead tree");
            Require(!AnimalHuntTargets.Eligible(predator.Context, porcupine) && !f.Simulation.Hit(predator, porcupine.transform, false), "Ground predator can attack tree refuge");
            porcupine.NotifyThreat(predator.transform);
            Require(!porcupine.SupportsAggression && !p.QuillsRaised, "Tree porcupine became aggressive");
            Set(predator.Config, "canClimbTrees", true);
            Require(AnimalHuntTargets.Eligible(predator.Context, porcupine), "Tree-climbing capability is ignored");
        }
        private sealed class Fixture : IDisposable
        {
            public readonly Scene Scene;
            public readonly MapTestSceneController Map;
            public readonly AnimalSimulation Simulation;
            private readonly Texture2D texture;
            private readonly TiledMapTestFixture tile;
            private readonly List<Object> assets = new();
            public Fixture(bool preview = true)
            {
                Scene = preview ? EditorSceneManager.NewPreviewScene() : SceneManager.GetActiveScene();
                Map = New("Specific animal map", Vector2.zero).AddComponent<MapTestSceneController>(); Map.enabled = false;
                texture = new Texture2D(2, 2); texture.SetPixels(new[] { Color.gray, Color.gray, Color.gray, Color.gray }); texture.Apply();
                tile = new TiledMapTestFixture(Map, texture, 32f, .25f);
                Map.RuntimeMap.TryLocate(Vector2.one * 8f, out var runtimeTile, out _);
                Set(runtimeTile.Level, "surfacePalette", new List<TerrainSurfaceDefinition> { new TerrainSurfaceDefinition() });
                runtimeTile.Level.EnsureSurfaceAuthoringData(); runtimeTile.Level.FillSurfaceType(TerrainKind.Grass);
                Simulation = AnimalSimulation.ForScene(Scene);
            }
            private GameObject New(string name, Vector2 point)
            { var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, Scene); go.transform.position = Map != null ? Map.MapPositionToWorld(point) : Vector3.zero; return go; }
            public TreeHabitat Tree(Vector2 point, bool dead = false)
            {
                var tree = New("Tree", point).AddComponent<TreeHabitat>();
                tree.ConfigureEditorDefaults(dead ? TreeHealthState.Dead : TreeHealthState.Healthy, null, .6f); return tree;
            }
            public AnimalAgent Spawn(string species, Vector2 point, bool juvenile = false)
            {
                string path = "Assets/Prefabs/Animals/" + species + "/" + species + (juvenile ? "_Juvenile" : "") + "_Placeholder.prefab";
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(path); Require(source != null, "Missing prefab " + path);
                var root = Object.Instantiate(source); SceneManager.MoveGameObjectToScene(root, Scene); root.transform.position = Map.MapPositionToWorld(point);
                var agent = root.GetComponent<AnimalAgent>();
                var config = Object.Instantiate(agent.Config); assets.Add(config);
                Set(config, "baseDetectionChancePerCheck", 0f); Set(config, "baseFleeChancePerCheck", 0f); Set(config, "baseAggressionChancePerCheck", 0f);
                foreach (var setting in config.DailyBehaviours)
                { Set(setting, "selectionWeight", 0f); Set(setting, "durationSeconds", new Vector2(.4f, .4f)); }
                agent.ConfigureEditorDefaults(config, agent.Behaviour, agent.PlaceholderView);
                Set(root.GetComponent<HeightMapPlacedObject>(), "map", Map);
                Require(agent.TryInitialize(), "Could not initialize " + species);
                return agent;
            }
            public void Tick(float seconds)
            { for (float remaining = seconds; remaining > .00001f; remaining -= .05f) Simulation.Tick(Mathf.Min(.05f, remaining)); }
            public void Dispose()
            {
                EditorSceneManager.ClosePreviewScene(Scene); tile.Dispose();
                foreach (Object asset in assets) Object.DestroyImmediate(asset);
                Object.DestroyImmediate(texture); AnimalGame.World.WorldInteractionQuery.InvalidateSpatialIndex();
            }
        }
        private static void Set(object instance, string field, object value)
        {
            for (Type type = instance.GetType(); type != null; type = type.BaseType)
            {
                var info = type.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (info != null) { info.SetValue(instance, value); return; }
            }
            throw new Exception("Missing fixture field " + field);
        }
        private static T Get<T>(object instance, string field) => (T)instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
        private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    }
}
