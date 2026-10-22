using System;
using System.Collections.Generic;
using System.Reflection;
using AnimalGame.Animals;
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
    public static class AnimalAIRegressionChecks
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        [MenuItem("Animal Game/Validation/Run Animal AI Checks")]
        public static void Run()
        {
            CheckIndependentHealthAndDecay();
            CheckBodyAndIndex();
            CheckDelayedDamage();
            CheckHiddenIdentityAndPhoto();
            CheckGroupPropagation();
            CheckFeedingAndCapacity();
            CheckEscapeDirection();
            CheckActionsAndPause();
            CheckAerialAndPlayerExclusion();
            CheckSpeciesAdapters();
            CheckViewLifecycle();
            RobotArmRegressionChecks.RunSpatialIndexChecks();
            GarbageRegressionChecks.Run();
            Debug.Log("Animal AI checks PASS: individual data, continuous decay, bodies/index/self-exclusion, delayed pair damage, hidden identity/photo, groups, feeding/capacity, escape direction, cancellation and pause.");
        }
        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
        private static void CheckIndependentHealthAndDecay()
        {
            var config = ScriptableObject.CreateInstance<AnimalSpeciesConfig>();
            try
            {
                Set(config, "initialHealthRange", new Vector2(100f, 100f));
                var a = new AnimalHealth(config);
                var b = new AnimalHealth(config);
                a.Damage(20f);
                Near(a.CurrentHealth, 80f, "Damage amount"); Near(b.CurrentHealth, 100f, "Shared config leaked health");
                int deaths = 0; a.Died += () => deaths++;
                a.Damage(100f); a.Damage(100f);
                Require(deaths == 1, "Death fired more than once");
                a.Tick(60f); Near(a.CorpseRemaining, 50f, "Continuous half-life");
                a.Consume(10f); a.Tick(48f); Near(a.CorpseRemaining, 0f, "Feeding must advance depletion");
                Near(b.CorpseRemaining, 100f, "Corpse value leaked between individuals");
                var natural = new AnimalHealth(config); natural.Damage(100f); natural.Tick(120f);
                Near(natural.CorpseRemaining, 0f, "Corpse must expire in 120 scaled seconds");
            }
            finally { Object.DestroyImmediate(config); }
        }
        private static void CheckBodyAndIndex()
        {
            using var f = new Fixture();
            AnimalAgent a = f.Animal("A", new Vector2(3f, 3f));
            AnimalAgent b = f.Animal("B", new Vector2(5f, 3f));
            Require(a.Motor.CanOccupyMapPosition(a.Motor.CurrentMapPosition), "Body collided with itself");
            Require(!a.Motor.CanOccupyMapPosition(b.Motor.CurrentMapPosition), "Animal missed another living body");
            var hits = new List<WorldInteraction>();
            Require(WorldInteractionQuery.Query(InteractionShape.Circle(new Vector2(5f, 3f), .01f), WorldInteractionKind.Collision, f.Map, f.Scene, hits), "Body is absent from shared index");
            Require(hits.Contains(b.GetComponent<AnimalBody>()), "Wrong collision candidate");
            b.Motor.TeleportToMapPosition(new Vector2(7f, 3f));
            Require(a.Motor.CanOccupyMapPosition(new Vector2(5f, 3f)), "Index kept stale body position");
            Require(!a.Motor.CanOccupyMapPosition(new Vector2(7f, 3f)), "Index missed moved body");
            b.ReceiveDamage(1000f, a.transform);
            AnimalBody body = b.GetComponent<AnimalBody>();
            Require((body.Kind & WorldInteractionKind.Pushable) != 0 && (body.Kind & WorldInteractionKind.Grabbable) == 0, "Corpse interaction capabilities");
            Require(body.Available && !b.IsPresent, "Corpse physical/photo availability");
            b.GetComponent<AnimalPhotoSubject>();
            GameObject player = f.New("Player", f.Map.MapPositionToWorld(new Vector2(10f, 3f)));
            player.AddComponent<RobotMover>();
            if (player.GetComponent<PlayerWorldBody>() == null) player.AddComponent<PlayerWorldBody>();
            Require(!a.Motor.CanOccupyMapPosition(new Vector2(10f, 3f)), "Animal missed player body");
        }
        private static void CheckDelayedDamage()
        {
            using var f = new Fixture();
            AnimalAgent attacker = f.Animal("Attacker", new Vector2(3f, 3f));
            AnimalAgent second = f.Animal("Second", new Vector2(4f, 3f));
            AnimalAgent target = f.Animal("Target", new Vector2(5f, 3f));
            float health = target.Health.CurrentHealth;
            Require(attacker.Simulation.Hit(attacker, target.transform, false), "First hit not registered");
            Require(!attacker.Simulation.Hit(attacker, target.transform, false), "Pair cooldown allowed duplicate hit");
            Require(attacker.Simulation.Hit(second, target.transform, false), "Independent attacker cooldown");
            Near(target.Health.CurrentHealth, health, "Health reduced before delay");
            target.Motor.TeleportToMapPosition(new Vector2(12f, 12f));
            f.Simulation.Tick(.5f); Near(target.Health.CurrentHealth, health, "Damage before one second");
            f.Simulation.Tick(.5f); Near(target.Health.CurrentHealth, health - 30f, "Detached target lost registered hits");
            Require(attacker.Simulation.Hit(attacker, target.transform, false), "Pair cooldown did not expire");
        }
        private static void CheckHiddenIdentityAndPhoto()
        {
            using var f = new Fixture();
            AnimalAgent animal = f.Animal("Identity", new Vector2(3f, 3f));
            var subject = animal.gameObject.AddComponent<AnimalPhotoSubject>();
            AnimalHealth original = animal.Health;
            animal.BeginHiding(); Require(!subject.IsPhotographable(), "Hidden animal photographable");
            animal.CompleteHiding();
            Require(ReferenceEquals(original, animal.Health), "Hiding recreated individual health");
            animal.gameObject.SetActive(false); animal.gameObject.SetActive(true);
            Require(ReferenceEquals(original, animal.Health), "Re-enable reset same individual");
            animal.Brain.Request(AnimalState.Fleeing);
            Require(subject.PhotoState == AnimalState.Fleeing, "Photo action overrode fleeing");
            animal.ReceiveDamage(1000f, null);
            Require(!subject.IsPhotographable(), "Corpse photographable");
        }
        private static void CheckGroupPropagation()
        {
            using var f = new Fixture();
            AnimalAgent a = f.Animal("Leader", new Vector2(3f, 3f));
            AnimalAgent b = f.Animal("Member", new Vector2(5f, 3f));
            var group = new AnimalGroup(); group.Add(a); group.Add(b);
            a.Brain.Request(AnimalState.Fleeing);
            Require(b.CurrentState == AnimalState.Daily, "Group reacted before delay");
            group.Remove(a);
            group.Tick(.49f); Require(b.CurrentState == AnimalState.Daily, "Group reacted too early");
            group.Tick(.5f); Require(b.CurrentState == AnimalState.Fleeing || b.CurrentState == AnimalState.Aggressive, "Trigger removal cancelled propagation");
            Require(group.Leader == b, "Leader was not replaced");
            int version = b.ReactionVersion; group.Tick(1f); Require(b.ReactionVersion == version, "Propagation looped");
        }
        private static void CheckFeedingAndCapacity()
        {
            using var f = new Fixture();
            var prey = f.Animal("Corpse", new Vector2(5f, 5f));
            prey.ReceiveDamage(1000f, null);
            var eater = f.Animal("Eater", new Vector2(5.4f, 5f));
            Set(eater.Config, "preySpecies", new List<AnimalSpeciesConfig> { prey.Config });
            var action = new HuntAction(prey);
            action.Enter(eater.Context);
            float before = prey.Health.CorpseRemaining;
            action.Tick(eater.Context, 10f);
            Near(prey.Health.CorpseRemaining, before - eater.Config.AttackDamage * .5f, "Eating was not continuous/proportional");
            action.Exit(eater.Context);
            Near(eater.Runtime.FeedCooldownUntil, eater.Runtime.Time + 150f, "Partial feeding cooldown");
            var members = new List<AnimalAgent>();
            for (int i = 0; i < 6; i++) members.Add(f.Animal("Eater " + i, new Vector2(8f + i, 6f)));
            for (int i = 0; i < 5; i++) Require(prey.Health.Reserve(members[i]), "Valid feeding slot rejected");
            Require(!prey.Health.Reserve(members[5]), "Sixth eater accepted");
            prey.Health.Release(members[0]); Require(prey.Health.Reserve(members[5]), "Feeding slot not released");
        }
        private static void CheckEscapeDirection()
        {
            using var f = new Fixture();
            var animal = f.Animal("Prey", new Vector2(5f, 5f));
            var east = f.New("East threat", f.Map.MapPositionToWorld(new Vector2(6f, 5f)));
            var north = f.New("North threat", f.Map.MapPositionToWorld(new Vector2(5f, 6f)));
            animal.Perception.Remember(east.transform); animal.Perception.Remember(north.transform);
            Vector2 direction = animal.Perception.EscapeDirection();
            Require(direction.x < -.5f && direction.y < -.5f, "Escape did not bisect largest empty sector");
        }
        private static void CheckActionsAndPause()
        {
            using var f = new Fixture();
            var animal = f.Animal("Action lifecycle", new Vector2(3f, 3f));
            var action = new TrackingAction();
            Require(animal.Brain.StartAction(action), "Cannot start runtime action");
            animal.Brain.Request(AnimalState.Curious);
            Require(action.Exits == 1, "Interrupt did not clean up exactly once");
            float time = f.Simulation.Time;
            f.Simulation.Tick(0f); Near(f.Simulation.Time, time, "Pause advanced simulation clock");
            var configAction = ScriptableObject.CreateInstance<BasicAnimalActionDefinition>();
            try { Require(!ReferenceEquals(configAction.CreateRuntime(), configAction.CreateRuntime()), "Shared action runtime"); }
            finally { Object.DestroyImmediate(configAction); }
        }
        private static void CheckAerialAndPlayerExclusion()
        {
            using var f = new Fixture();
            AnimalAgent animal = f.Animal("Flying actor", new Vector2(5f, 5f));
            GameObject rock = f.New("Ground rock", f.Map.MapPositionToWorld(new Vector2(6f, 5f)));
            WorldInteraction obstacle = rock.AddComponent<WorldInteraction>();
            obstacle.ColliderType = ColliderType.Custom;
            obstacle.ColliderShape = WorldInteractionColliderShape.Circle;
            obstacle.ColliderRadius = .1f;
            Require(!animal.Motor.CanOccupyMapPosition(new Vector2(6f, 5f)), "Ground obstacle ignored by walking");
            Require(animal.Motor.CanOccupyMapPosition(new Vector2(6f, 5f), true), "Flight failed to ignore ground props");
            AnimalAgent other = f.Animal("Other actor", new Vector2(8f, 5f));
            Require(!animal.Motor.CanOccupyMapPosition(other.Motor.CurrentMapPosition, true), "Flight ignored actor separation");
            var player = f.New("Player with separate terrain evaluator", f.Map.MapPositionToWorld(new Vector2(12f, 5f))).AddComponent<RobotMover>();
            var evaluator = f.New("Separate evaluator", Vector3.zero).AddComponent<HeightMapTraversalEvaluator>();
            evaluator.Initialize(f.Map); player.SetTraversalEvaluator(evaluator);
            if (player.GetComponent<PlayerWorldBody>() == null) player.gameObject.AddComponent<PlayerWorldBody>();
            Require(evaluator.CollisionQueryIgnore == player.transform, "Evaluator excludes its own host instead of robot root");
            var plan = new InteractionPushPlan();
            plan.Begin(f.Map, f.Scene, player.transform, null);
            Vector2 start = new Vector2(12f, 5f);
            plan.Add(InteractionShape.Circle(start, .75f), InteractionShape.Circle(start + Vector2.right * .2f, .75f), true);
            Require(plan.Evaluate(), "Player's own indexed body blocked a clear move");
            GameObject artwork = f.New("Robot artwork", Vector3.zero);
            artwork.transform.SetParent(player.transform, false);
            SpriteRenderer renderer = artwork.AddComponent<SpriteRenderer>();
            MethodInfo capture = typeof(AnimalGame.Demos.CheckpointRefit.RefitPlayerPreview).GetMethod("ShouldCapture", BindingFlags.NonPublic | BindingFlags.Static);
            Require((bool)capture.Invoke(null, new object[] { renderer }), "Player's indexed body excluded robot artwork from refit preview");
            artwork.AddComponent<WorldInteraction>();
            Require(!(bool)capture.Invoke(null, new object[] { renderer }), "Refit preview included a nested world prop");
        }
        private static void CheckSpeciesAdapters()
        {
            using var f = new Fixture();
            AnimalAgent muskrat = f.Animal("Muskrat adapter", new Vector2(4f, 4f), typeof(MuskratBehaviour));
            AnimalAgent bird = f.Animal("Woodpecker adapter", new Vector2(10f, 10f), typeof(PileatedWoodpeckerBehaviour));
            Require(muskrat.GetComponent<MuskratBehaviour>().CreateEscapeAction() is DiveEscapeAction, "Muskrat lost dive escape adapter");
            Require(bird.GetComponent<PileatedWoodpeckerBehaviour>().CreateEscapeAction() is ReturnToTreeEscapeAction, "Woodpecker lost tree escape adapter");
            AnimalHealth identity = muskrat.Health;
            muskrat.Brain.Request(AnimalState.Fleeing);
            muskrat.BeginHiding(); muskrat.CompleteHiding();
            Require(ReferenceEquals(identity, muskrat.Health) && muskrat.CurrentState == AnimalState.Daily, "Special species failed hide/return lifecycle");
            bird.Brain.Request(AnimalState.Fleeing); bird.BeginHiding(); bird.CompleteHiding();
            Require(bird.CurrentState == AnimalState.Daily, "Bird failed hide/return lifecycle");
        }
        private static void CheckViewLifecycle()
        {
            using var f = new Fixture();
            var player = f.New("Lifecycle player", f.Map.MapPositionToWorld(new Vector2(2f, 2f))).AddComponent<RobotMover>();
            Camera camera = f.New("Lifecycle camera", new Vector3(0f, 0f, -10f)).AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = .25f; camera.aspect = 1f;
            Set(f.Simulation.World, "<Player>k__BackingField", player);
            Set(f.Simulation.World, "<Camera>k__BackingField", camera);
            var animal = f.Animal("Distant animal", new Vector2(20f, 20f));
            Require(f.Simulation.World.IsBeyondView(animal), "Three-screen-width distance threshold");
            animal.ReceiveDamage(1000f, null);
            f.Simulation.Tick(.01f);
            Require(!animal.Runtime.Removed, "Distance culling removed a corpse before decay");
        }
        private sealed class TrackingAction : AnimalAction
        {
            public int Exits;
            public override AnimalActionStatus Tick(AnimalContext c, float dt) => AnimalActionStatus.Running;
            public override void Exit(AnimalContext c) { Exits++; base.Exit(c); }
        }
        private sealed class Fixture : IDisposable
        {
            public readonly Scene Scene = EditorSceneManager.NewPreviewScene();
            public readonly MapTestSceneController Map;
            public readonly AnimalSimulation Simulation;
            private readonly Texture2D texture;
            private readonly TiledMapTestFixture tile;
            private readonly List<Object> assets = new();
            public Fixture()
            {
                Map = New("Flat map", Vector3.zero).AddComponent<MapTestSceneController>();
                Map.enabled = false;
                texture = new Texture2D(2, 2); texture.SetPixels(new[] { Color.gray, Color.gray, Color.gray, Color.gray }); texture.Apply();
                tile = new TiledMapTestFixture(Map, texture, 32f, .25f);
                Simulation = AnimalSimulation.ForScene(Scene);
            }
            public GameObject New(string name, Vector3 position)
            { var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, Scene); go.transform.position = position; return go; }
            public AnimalAgent Animal(string name, Vector2 position, Type speciesBehaviour = null)
            {
                var config = ScriptableObject.CreateInstance<AnimalSpeciesConfig>(); assets.Add(config);
                Set(config, "initialHealthRange", new Vector2(100f, 100f));
                Set(config, "baseDetectionChancePerCheck", 0f);
                Set(config, "baseFleeChancePerCheck", 0f);
                Set(config, "dailyBehaviours", new List<AnimalDailyBehaviourSettings> { new AnimalDailyBehaviourSettings(AnimalDailyBehaviourKind.Idle, 1f, 1f, 1f) });
                var go = New(name, Map.MapPositionToWorld(position));
                if (speciesBehaviour != null) go.AddComponent(speciesBehaviour);
                var agent = go.AddComponent<AnimalAgent>();
                agent.ConfigureEditorDefaults(config, null, null);
                Set(go.GetComponent<HeightMapPlacedObject>(), "map", Map);
                Require(agent.TryInitialize(), "Fixture animal did not initialize");
                return agent;
            }
            public void Dispose()
            {
                tile.Dispose(); EditorSceneManager.ClosePreviewScene(Scene);
                foreach (Object asset in assets) Object.DestroyImmediate(asset);
                Object.DestroyImmediate(texture); WorldInteractionQuery.InvalidateSpatialIndex();
            }
        }
        private static void Set(object instance, string field, object value)
        {
            FieldInfo info = instance.GetType().GetField(field, Flags);
            if (info == null) throw new Exception("Missing test field: " + field);
            info.SetValue(instance, value);
        }
        private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Near(float actual, float expected, string message) { Require(Mathf.Abs(actual - expected) < .002f, message + ": " + actual + " != " + expected); }
    }
}
