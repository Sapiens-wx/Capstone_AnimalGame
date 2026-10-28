using System;
using System.Collections.Generic;
using System.Reflection;
using AnimalGame.Animals;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    public static class AnimalSpeciesRegressionChecks
    {
        [MenuItem("Animal Game/Validation/Run Goat And Wolf AI Checks")]
        public static void Run()
        {
            UnityEngine.Random.State random = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(3841);
                CheckWolfReactionsAndFacing();
                CheckSlopesAndSliding();
                CheckSlopeEscapeValley();
                CheckCorpseOnlyFeeding();
                CheckSparringAndAreaImpact();
                CheckDailyActions();
                Debug.Log("Goat and wolf AI checks PASS: aggression guards, retreat facing, species slopes/sliding, peak-to-valley escape, corpse-only feeding/cooldowns, paired cancellation, juvenile exclusion, area damage and configured durations.");
            }
            finally { UnityEngine.Random.state = random; }
        }
        public static void RunBatch()
        {
            try { AnimalAIRegressionChecks.Run(); Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
        private static void CheckWolfReactionsAndFacing()
        {
            using var f = new Fixture();
            AnimalAgent wolf = f.Animal("Wolf", new Vector2(10f, 8f), typeof(WolfBehaviour));
            Set(wolf.Config, "baseAggressionChancePerCheck", 1f);
            wolf.Brain.Request(AnimalState.Aggressive);
            Require(wolf.CurrentState == AnimalState.Fleeing, "Direct aggression bypassed wolf capability");
            wolf.ReturnToDaily();
            Set(wolf.Config, "baseAggressionChancePerCheck", 0f);
            wolf.ForceGroupReaction(null);
            Require(wolf.CurrentState == AnimalState.Fleeing, "Double-zero group reaction made wolf angry");
            wolf.ReturnToDaily();
            var threat = f.New("Threat", f.Map.MapPositionToWorld(new Vector2(12f, 8f)));
            Set(wolf.Config, "baseAggressionChancePerCheck", 1f);
            wolf.NotifyThreat(threat.transform);
            Vector2 before = wolf.Motor.CurrentMapPosition;
            f.Tick(1f);
            Require(wolf.CurrentState == AnimalState.Curious, "Wolf used aggression probability");
            Require(wolf.Motor.CurrentMapPosition.x < before.x - .05f, "Curious wolf did not retreat");
            Require(Vector2.Dot(wolf.Motor.FacingMapDirection, Vector2.right) > .9f, "Retreat overwrote threat-facing direction");
            wolf.Brain.Request(AnimalState.Fleeing);
            Require(!wolf.Motor.IsFacingLocked, "Curious facing leaked into escape");
        }
        private static void CheckSlopesAndSliding()
        {
            using var f = new Fixture(x => x / 32f, 32f * Mathf.Tan(70f * Mathf.Deg2Rad));
            AnimalAgent goat = f.Animal("Goat slope", new Vector2(10f, 6f), typeof(MountainGoatBehaviour));
            AnimalAgent wolf = f.Animal("Wolf slope", new Vector2(20f, 20f), typeof(WolfBehaviour));
            Set(goat.Config, "maximumWalkSlopeDegrees", 80f); Set(goat.Config, "slideSlopeDegrees", 80f);
            Set(wolf.Config, "maximumWalkSlopeDegrees", 38f); Set(wolf.Config, "slideSlopeDegrees", 38f);
            Require(AnimalTerrainQuery.TrySample(f.Map, new Vector2(15f, 10f), out var ground) && Mathf.Abs(ground.SlopeDegrees - 70f) < 1f, "Logical map gradient units");
            Require(goat.Motor.CanTraverseSegment(new Vector2(10f, 6f), new Vector2(11f, 6f)), "Goat blocked on 70 degree terrain");
            Require(!wolf.Motor.CanTraverseSegment(new Vector2(20f, 20f), new Vector2(20f, 21f)), "Contour walking bypassed wolf slope limit");
            float gx = goat.Motor.CurrentMapPosition.x, wx = wolf.Motor.CurrentMapPosition.x;
            f.Tick(.5f);
            Require(Mathf.Abs(goat.Motor.CurrentMapPosition.x - gx) < .001f, "Goat slid below its threshold");
            Require(wolf.Motor.CurrentMapPosition.x < wx - .05f, "Wolf did not slide above its own threshold");
            Set(goat.Config, "slideSlopeDegrees", 60f);
            f.Tick(.5f);
            Require(goat.Motor.CurrentMapPosition.x < gx - .05f, "Species slide threshold ignored");
        }
        private static void CheckSlopeEscapeValley()
        {
            using var f = new Fixture(x => .5f + .5f * Mathf.Cos(x * Mathf.PI / 8f), 6f);
            AnimalAgent goat = f.Animal("Escape goat", new Vector2(14f, 16f), typeof(MountainGoatBehaviour));
            Set(goat.Config, "maximumWalkSlopeDegrees", 80f); Set(goat.Config, "slideSlopeDegrees", 80f);
            goat.Brain.Request(AnimalState.Fleeing);
            bool descending = false, climbedNext = false;
            float peakX = 0f, furthestLeft = 32f;
            for (int i = 0; i < 600; i++)
            {
                f.Tick(.05f);
                string action = goat.Brain.CurrentActionName;
                // Escape state actions expose their names through Brain, not the daily behaviour.
                if (action.Contains("DownhillTraverse")) { if (!descending) peakX = goat.Motor.CurrentMapPosition.x; descending = true; }
                if (descending) furthestLeft = Mathf.Min(furthestLeft, goat.Motor.CurrentMapPosition.x);
                if (descending && action.Contains("Uphill"))
                { Require(furthestLeft < peakX - 4f, "Goat immediately climbed back to the same peak"); climbedNext = true; break; }
            }
            Require(descending && climbedNext, "Escape never traversed the valley to the next uphill slope");
            Require(goat.CurrentState == AnimalState.Fleeing, "Slope escape unexpectedly returned to daily");
            goat.ReturnToDaily();
            Require(goat.Motor.SegmentFilter == null, "Slope escape filter leaked after exit");
        }
        private static void CheckCorpseOnlyFeeding()
        {
            using var f = new Fixture();
            AnimalAgent wolf = f.Animal("Scavenger", new Vector2(3f, 3f), typeof(WolfBehaviour));
            AnimalAgent corpse = f.Animal("Corpse", new Vector2(7f, 3f));
            AnimalAgent alive = f.Animal("Closer live prey", new Vector2(4f, 5f));
            Set(wolf.Config, "preySpecies", new List<AnimalSpeciesConfig> { corpse.Config, alive.Config });
            Set(wolf.Config, "attackDamage", 3f);
            corpse.ReceiveDamage(1000f, null);
            float health = alive.Health.CurrentHealth;
            Require(wolf.Brain.StartAction(new HuntAction(corpse, true)), "Corpse hunt did not start");
            for (int i = 0; i < 270; i++)
            {
                f.Tick(.1f);
                Require(wolf.Runtime.Target != alive, "Corpse-only action switched to living prey");
            }
            Require(wolf.Runtime.FeedCooldownUntil > f.Simulation.Time, "Corpse feeding lost cooldown");
            Require(Mathf.Abs(alive.Health.CurrentHealth - health) < .001f, "Scavenging damaged live prey");
            Require(wolf.Runtime.Target == null, "Finished feeding retained corpse reservation/target");
            Require(AnimalHuntTargets.Select(wolf.Context, null, true) == null, "Scavenging bypassed feeding cooldown");
        }
        private static void CheckSparringAndAreaImpact()
        {
            using var f = new Fixture();
            AnimalAgent a = f.Animal("Adult A", new Vector2(5f, 5f), typeof(MountainGoatBehaviour));
            AnimalAgent b = f.Animal("Adult B", new Vector2(5.45f, 5f), typeof(MountainGoatBehaviour));
            AnimalAgent juvenile = f.Animal("Juvenile", new Vector2(8f, 5f), typeof(MountainGoatBehaviour));
            Set(juvenile.GetComponent<MountainGoatBehaviour>(), "juvenile", true);
            AnimalGroup group = f.Simulation.GetGroup("goats"); group.Add(a); group.Add(b); group.Add(juvenile);
            Require(juvenile.Brain.StartAction(new SparringAction(10f)), "Juvenile exclusion action failed to start");
            Require(juvenile.Runtime.Sparring == null, "Juvenile entered adult sparring");
            f.Tick(.01f);
            Require(a.Brain.StartAction(new SparringAction(10f)), "Adult sparring did not start");
            Require(a.Runtime.Sparring != null && ReferenceEquals(a.Runtime.Sparring, b.Runtime.Sparring), "Partner session was not reserved atomically");
            SparringSession session = a.Runtime.Sparring;
            Set(a.Config, "attackDamage", 2f);
            float health = b.Health.CurrentHealth;
            AnimalImpact.Apply(a.Context, Vector2.right, true);
            Require(b.CurrentState == AnimalState.Daily && !session.Ended, "Expected sparring impact triggered threat reaction");
            Require(!b.Motor.IsDisplaced, "Social sparring caused knockback");
            f.Tick(1f);
            Require(Mathf.Approximately(b.Health.CurrentHealth, health), "Social sparring caused damage");
            b.NotifyThreat(juvenile.transform);
            Require(session.Ended && a.Runtime.Sparring == null && b.Runtime.Sparring == null, "External threat leaked paired reservations");
            Require(b.CurrentState == AnimalState.Curious, "Sparring cancellation overwrote threat state");

            AnimalAgent activeA = f.Animal("Charge A", new Vector2(22f, 6f), typeof(MountainGoatBehaviour));
            AnimalAgent activeB = f.Animal("Charge B", new Vector2(24f, 6f), typeof(MountainGoatBehaviour));
            Set(activeA.Config, "attackDamage", 2f); Set(activeB.Config, "attackDamage", 2f);
            AnimalGroup activeGroup = f.Simulation.GetGroup("active-pair"); activeGroup.Add(activeA); activeGroup.Add(activeB);
            Require(activeA.Brain.StartAction(new SparringAction(4f)), "Active sparring failed to start");
            f.Tick(8f);
            Require(activeA.Health.CurrentHealth == 100f && activeB.Health.CurrentHealth == 100f, "Active sparring caused damage");
            Require(activeA.Runtime.Sparring == null && activeB.Runtime.Sparring == null, "Timed sparring did not release both members");

            AnimalAgent impact = f.Animal("Area attacker", new Vector2(15f, 15f));
            AnimalAgent first = f.Animal("Area first", new Vector2(15.4f, 15f));
            AnimalAgent second = f.Animal("Area second", new Vector2(15.3f, 15.4f));
            float firstHealth = first.Health.CurrentHealth, secondHealth = second.Health.CurrentHealth;
            GameObject player = f.New("Impact player", f.Map.MapPositionToWorld(new Vector2(15.4f, 14.5f)));
            player.AddComponent<AnimalGame.RobotMap.PlayerWorldBody>();
            var receiver = player.AddComponent<AnimalGame.RobotMap.PlayerCombatReceiver>();
            int playerHits = 0; receiver.Damaged += _ => playerHits++;
            AnimalImpact.Apply(impact.Context, Vector2.right);
            Require(receiver.IsDisplaced, "Area impact omitted player knockback");
            f.Tick(1.05f);
            Require(first.Health.CurrentHealth < firstHealth && second.Health.CurrentHealth < secondHealth, "Impact missed bystanders in radius");
            Require(playerHits == 1, "Area impact omitted delayed player damage");
        }
        private static void CheckDailyActions()
        {
            using var f = new Fixture();
            AnimalAgent animal = f.Animal("Daily", new Vector2(8f, 8f));
            var definition = ScriptableObject.CreateInstance<TerrainAnimalActionDefinition>();
            try
            {
                AnimalAction action = definition.CreateRuntime(new AnimalDailyBehaviourSettings(AnimalDailyBehaviourKind.Idle, 1f, .2f, .2f));
                action.Enter(animal.Context);
                Require(animal.Motor.FacingTurnSpeedDegreesPerSecond == 35f, "Looking did not slow turning");
                Require(action.Tick(animal.Context, .21f) == AnimalActionStatus.Succeeded, "Definition ignored configured duration");
                action.Exit(animal.Context);
                Require(animal.Motor.FacingTurnSpeedDegreesPerSecond == null, "Looking speed leaked after exit");
                var rest = new RestAtTerrainAction(1f, TerrainKind.Rock, 10f);
                rest.Enter(animal.Context);
                Require(rest.Tick(animal.Context, .1f) == AnimalActionStatus.Failed, "Missing rock falsely succeeded");
                rest.Exit(animal.Context);
                Require(f.Map.RuntimeMap.TryLocate(new Vector2(8f, 8f), out var tile, out _), "Missing test tile");
                Set(tile.Level, "surfacePalette", new List<TerrainSurfaceDefinition> { new TerrainSurfaceDefinition() });
                tile.Level.EnsureSurfaceAuthoringData();
                Require(tile.Level.FillSurfaceType(TerrainKind.Grass), "Could not paint grass test surface");
                var graze = new RestAtTerrainAction(.2f, TerrainKind.Grass, 90f, AnimalState.Eating);
                graze.Enter(animal.Context); animal.Motor.Tick(.1f);
                graze.Tick(animal.Context, .1f);
                Require(graze.PhotoState == AnimalState.Eating, "Grass grazing never entered eating phase");
                Require(graze.Tick(animal.Context, .2f) == AnimalActionStatus.Succeeded, "Grass grazing duration failed");
                graze.Exit(animal.Context);
            }
            finally { Object.DestroyImmediate(definition); }
        }
        private sealed class Fixture : IDisposable
        {
            public readonly Scene Scene = EditorSceneManager.NewPreviewScene();
            public readonly MapTestSceneController Map;
            public readonly AnimalSimulation Simulation;
            private readonly Texture2D texture;
            private readonly TiledMapTestFixture tile;
            private readonly List<Object> assets = new List<Object>();
            public Fixture(Func<float, float> height = null, float maximumHeight = 1f)
            {
                Map = New("Species test map", Vector3.zero).AddComponent<MapTestSceneController>(); Map.enabled = false;
                texture = new Texture2D(257, 2, TextureFormat.RGBAFloat, false, true);
                var pixels = new Color[514];
                for (int y = 0; y < 2; y++) for (int x = 0; x < 257; x++)
                { float value = height != null ? height(x * 32f / 256f) : .5f; pixels[y * 257 + x] = new Color(value, value, value, 1f); }
                texture.SetPixels(pixels); texture.Apply();
                tile = new TiledMapTestFixture(Map, texture, 32f, .25f, maximumHeight);
                Simulation = AnimalSimulation.ForScene(Scene);
            }
            public void Tick(float seconds)
            { for (float remaining = seconds; remaining > .00001f; remaining -= .05f) Simulation.Tick(Mathf.Min(.05f, remaining)); }
            public GameObject New(string name, Vector3 position)
            { var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, Scene); go.transform.position = position; return go; }
            public AnimalAgent Animal(string name, Vector2 position, Type behaviour = null)
            {
                var config = ScriptableObject.CreateInstance<AnimalSpeciesConfig>(); assets.Add(config);
                Set(config, "initialHealthRange", new Vector2(100f, 100f));
                Set(config, "baseDetectionChancePerCheck", 0f); Set(config, "baseFleeChancePerCheck", 0f);
                Set(config, "dailyBehaviours", new List<AnimalDailyBehaviourSettings> { new AnimalDailyBehaviourSettings(AnimalDailyBehaviourKind.Idle, 1f, 100f, 100f) });
                var go = New(name, Map.MapPositionToWorld(position));
                if (behaviour != null) go.AddComponent(behaviour);
                var agent = go.AddComponent<AnimalAgent>(); agent.ConfigureEditorDefaults(config, null, null);
                Set(go.GetComponent<HeightMapPlacedObject>(), "map", Map);
                Require(agent.TryInitialize(), "Species fixture animal failed to initialize");
                return agent;
            }
            public void Dispose()
            {
                EditorSceneManager.ClosePreviewScene(Scene); tile.Dispose();
                foreach (Object asset in assets) Object.DestroyImmediate(asset);
                Object.DestroyImmediate(texture); AnimalGame.World.WorldInteractionQuery.InvalidateSpatialIndex();
            }
        }
        private static void Set(object instance, string field, object value)
        {
            var info = instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (info == null) throw new Exception("Missing test field: " + field);
            info.SetValue(instance, value);
        }
        private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    }
}
