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
using Random = UnityEngine.Random;

namespace AnimalGame.Editor
{
    public static class TreeBirdRegressionChecks
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
        public static void RunAllBatch()
        {
            try
            {
                Run(); WoodpeckerSoundRegressionChecks.Run(); AnimalAIRegressionChecks.Run();
                AnimalSpeciesRegressionChecks.Run(); AnimalDebugUIRegressionChecks.Run();
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
        public static void RunPlayModeBatch()
        {
            try { Run(); WoodpeckerSoundRegressionChecks.Run(); AnimalAIPlayModeSmoke.RunBatch(); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
        [MenuItem("Animal Game/Validation/Run Tree Bird Checks")]
        public static void Run()
        {
            Random.State random = Random.state;
            try
            {
                Random.InitState(94631);
                CheckActionAssets(); CheckActivityPaths(); CheckStationaryWoodpecker(); CheckCategoryWeights();
                CheckFlight(); CheckTemporaryFlightDefinition(); CheckEscape(); CheckRecoveryAndCooldown(); CheckEnableLifecycle(); CheckCadence();
                Debug.Log("Tree bird checks PASS: crown/ring crawl, stationary woodpecker, global category weights, variable-speed arcs, threat interruption, bounded escape/despawn, cooldown/recovery and five-minute occupancy.");
            }
            finally { Random.state = random; CleanupActionCopies(); }
        }
        private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Set(object target, string name, object value)
        {
            Type type = target.GetType();
            while (type != null)
            {
                FieldInfo field = type.GetField(name, Flags | BindingFlags.DeclaredOnly);
                if (field != null) { field.SetValue(target, value); return; }
                type = type.BaseType;
            }
            throw new MissingFieldException(name);
        }
        private static void CheckActivityPaths()
        {
            using var f = new Fixture(false);
            var path = new List<Vector2>();
            Vector2 start = new Vector2(50.8f, 50f), end = new Vector2(49.2f, 50f);
            Require(f.Home.TryBuildActivityPath(f.Map, start, end, .12f, path), "Ring route around trunk was not found");
            Vector2 previous = start;
            foreach (Vector2 point in path)
            {
                Require(f.Home.ContainsActivitySegment(f.Map, previous, point, .12f), "Ring route cut through trunk");
                previous = point;
            }
            Require(!f.Home.ContainsActivityPoint(f.Map, new Vector2(50f, 50f), .12f), "Solid trunk is an activity point");
            f.Home.ConfigureEditorDefaults(TreeHealthState.Healthy, null, .6f);
            f.Home.ConfigureActivityArea(new Vector2(3f, 2f), Vector2.zero);
            Require(f.Home.ContainsActivityPoint(f.Map, new Vector2(52f, 50f), .12f), "Healthy crown cannot be used");
            Require(!f.Home.ContainsActivityPoint(f.Map, new Vector2(53f, 50f), .12f), "Bird body crossed crown boundary");
            f.Start(new TreeActivityAction(f.Bird, 30f));
            Vector2 initial = f.Agent.Motor.CurrentMapPosition;
            bool moved = false;
            for (int i = 0; i < 1200; i++)
            {
                f.Step(.02f);
                moved |= Vector2.Distance(initial, f.Agent.Motor.CurrentMapPosition) > .1f;
                Require(f.Home.ContainsActivityPoint(f.Map, f.Agent.Motor.CurrentMapPosition, .12f), "Crawling bird left its crown");
            }
            Require(moved && !f.Agent.Motor.IsFacingLocked, "Generic bird did not crawl freely");
            f.Home.ConfigureEditorDefaults(TreeHealthState.Dead, null, .6f);
            f.Agent.ReturnToDaily();
            Require(f.Bird.SnapToBirthPerch(), "Could not restore trunk ring perch");
            f.Start(new TreeActivityAction(f.Bird, 20f));
            for (int i = 0; i < 600; i++)
            {
                f.Step(.02f);
                Require(f.Home.ContainsActivityPoint(f.Map, f.Agent.Motor.CurrentMapPosition, .12f), "Dead-tree crawl left ring");
            }
        }
        private static void CheckStationaryWoodpecker()
        {
            using var f = new Fixture(true);
            f.Start(new TreeActivityAction(f.Bird, 30f));
            Vector2 position = f.Agent.Motor.CurrentMapPosition;
            for (int i = 0; i < 500; i++) f.Step(.02f);
            Require(Vector2.Distance(position, f.Agent.Motor.CurrentMapPosition) < .0001f, "Woodpecker crawled during tree activity");
            Vector2 towards = new Vector2(50f, 50f) - position;
            Require(Vector2.Dot(towards.normalized, f.Agent.Motor.FacingMapDirection) > .99f, "Woodpecker did not face trunk");
            f.Start(new WoodpeckerPeckAction((PileatedWoodpeckerBehaviour)f.Bird, 10f));
            f.Step(.2f);
            f.Agent.NotifyThreat(f.Threat(new Vector2(48f, 48f)));
            Require(f.Agent.CurrentState == AnimalState.Fleeing && f.Agent.Motor.IsAerialMovement, "Pecking was not immediately interrupted by escape");
            Require(f.Visual.localPosition.sqrMagnitude < .000001f, "Interrupted peck offset leaked into flight");
            Require(!f.Agent.Motor.IsFacingLocked && f.Agent.Motor.SegmentFilter == null, "Perch constraints leaked into escape");
            f.Agent.ReceiveDamage(10000f, null);
            Require(f.Agent.CurrentState == AnimalState.Dead && !f.Agent.Motor.HasTarget, "Death did not interrupt escape");
        }
        private static void CheckCategoryWeights()
        {
            using var f = new Fixture(true);
            Set(f.Config, "activityRadiusMeters", .01f);
            f.Tree(new Vector2(85f, 85f), TreeHealthState.Dead);
            for (int i = 0; i < 6; i++) f.Tree(new Vector2(5f + i * 5f, 10f), TreeHealthState.Healthy);
            int dead = 0, samples = 10000;
            for (int i = 0; i < samples; i++)
            {
                Require(f.Bird.TryChooseTree(out TreeHabitat tree) && tree != f.Home, "Global tree selection failed or selected current tree");
                if (tree.IsDead) dead++;
            }
            Require(Mathf.Abs(dead / (float)samples - .5f / .65f) < .025f, "Tree counts diluted category weights");
            Require((int)TreeHealthState.Dead == 1 && (int)AnimalDailyBehaviourKind.TreeActivity == 9, "Serialized enum values changed");
        }
        private static void CheckFlight()
        {
            foreach (bool woodpecker in new[] { false, true })
            foreach (int fps in new[] { 30, 60, 144 })
            foreach (bool curve in new[] { false, true })
            {
                using var f = new Fixture(woodpecker);
                TreeHabitat other = f.Tree(new Vector2(70f, 50f), TreeHealthState.Dead);
                var flightDefinition = (FlyToTreeActionDefinition)DailySettings(AnimalDailyBehaviourKind.FlyToTree, 1f, 1f, 1f).Action;
                flightDefinition.ArcProbability = curve ? 1f : 0f;
                var action = new FlyToTreeAction(f.Bird, other, flightDefinition);
                Vector2 start = f.Agent.Motor.CurrentMapPosition;
                f.Start(action);
                Require(action.IsCurved == curve, "Flight path selection ignored probability");
                if (curve) CheckCircularPath(f, start, flightDefinition.ArcOffsetRatio, flightDefinition.MaximumArcOffsetMeters);
                float min = float.PositiveInfinity, max = 0f;
                for (int i = 0; i < fps * 8 && f.Bird.CurrentTree != other; i++)
                {
                    f.Step(1f / fps);
                    float speed = f.Agent.Motor.CurrentSpeedMetersPerSecond;
                    if (f.Agent.Motor.HasTarget) { min = Mathf.Min(min, speed); max = Mathf.Max(max, speed); }
                }
                Require(f.Bird.CurrentTree == other, "Flight failed to arrive");
                Require(min >= 8.09f && max <= 9.91f && max - min > .5f, "Flight speed exceeded limits or remained constant");
            }
            using (var f = new Fixture(true))
            {
                var flightDefinition = (FlyToTreeActionDefinition)DailySettings(AnimalDailyBehaviourKind.FlyToTree, 1f, 1f, 1f).Action;
                flightDefinition.ArcProbability = 0f;
                TreeHabitat other = f.Tree(new Vector2(70f, 50f), TreeHealthState.Dead);
                f.Start(new FlyToTreeAction(f.Bird, other, flightDefinition));
                f.Step(.05f);
                Vector2 before = f.Agent.Motor.CurrentMapPosition;
                f.Agent.Motor.SegmentFilter = (a, b) => false;
                f.Step(.5f);
                Require(Vector2.Distance(before, f.Agent.Motor.CurrentMapPosition) < .0001f, "Blocked path consumed movement");
                f.Agent.Motor.SegmentFilter = null;
                f.Step(.05f);
                Require(Vector2.Distance(before, f.Agent.Motor.CurrentMapPosition) < .51f, "Unblocking flight jumped ahead");
            }
        }
        private static void CheckTemporaryFlightDefinition()
        {
            using var f = new Fixture(true);
            var settings = DailySettings(AnimalDailyBehaviourKind.FlyToTree, 1f, 1f, 1f);
            var configured = (FlyToTreeActionDefinition)settings.Action;
            configured.SpeedMetersPerSecond = 2f;
            configured.ArcOffsetRatio = .2f;
            Set(f.Config, "dailyBehaviours", new List<AnimalDailyBehaviourSettings> { settings });
            FieldInfo field = typeof(FlyToTreeAction).GetField("definition", Flags);

            // Exercise interruption, immediate arrival and a failed destination.
            foreach (TreeHabitat target in new[] { f.Tree(new Vector2(70f, 50f), TreeHealthState.Dead), f.Home, null })
            {
                var action = new FlyToTreeAction(f.Bird, target);
                var temporary = (FlyToTreeActionDefinition)field.GetValue(action);
                Require(temporary != null && temporary != configured && temporary.hideFlags == HideFlags.HideAndDontSave,
                    "Omitted flight definition did not create an unsaved temporary instance");
                Require(temporary.SpeedMetersPerSecond == 9f && Mathf.Abs(temporary.ArcOffsetRatio - .08f) < .0001f,
                    "Temporary flight definition read the bird's configured asset instead of class defaults");
                action.Enter(f.Agent.Context);
                action.Exit(f.Agent.Context);
                Require(temporary == null, "Exiting flight leaked its temporary definition");
                Require(configured != null && configured.SpeedMetersPerSecond == 2f,
                    "Temporary flight destroyed or changed the configured asset");
            }
        }
        private static void CheckCircularPath(Fixture f, Vector2 start, float ratio, float maximum)
        {
            var path = (List<Vector2>)typeof(AnimalMotor).GetField("aerialPath", Flags).GetValue(f.Agent.Motor);
            Vector2 end = f.Agent.Motor.TargetMapPosition;
            Vector2 delta = end - start;
            float distance = delta.magnitude;
            Vector2 normal = new Vector2(-delta.y, delta.x) / distance;
            Vector2 midpoint = (start + end) * .5f;
            float height = Mathf.Min(distance * ratio, maximum);
            float radius = distance * distance / (8f * height) + height * .5f;
            float side = Mathf.Sign(Vector2.Dot(path[path.Count / 2] - midpoint, normal));
            Vector2 centre = midpoint + normal * (side * (height - radius));
            float maxHeight = 0f;
            foreach (Vector2 point in path)
            {
                Require(Mathf.Abs(Vector2.Distance(point, centre) - radius) < .002f,
                    "Flight samples do not lie on a single circle");
                float deviation = Vector2.Dot(point - midpoint, normal) * side;
                Require(deviation >= -.001f, "Flight crossed to the other side of its chord");
                maxHeight = Mathf.Max(maxHeight, deviation);
            }
            Require(Mathf.Abs(maxHeight - height) < .01f, "Arc midpoint height ignored the flight settings");
            Require(path[path.Count - 1] == end, "Arc did not end exactly at the destination perch");
        }
        private static void CheckEscape()
        {
            using var f = new Fixture(true);
            f.Bird.Settings.FleeSafeDistanceMultiplier = 1000f;
            Vector2 before = f.Agent.Motor.CurrentMapPosition;
            f.Agent.NotifyThreat(f.Threat(before - Vector2.right * 2f));
            Require(f.Agent.CurrentState == AnimalState.Fleeing && !f.Agent.IsHidden, "Bird entered curiosity or hiding");
            Vector2 initial = (f.Agent.Motor.TargetMapPosition - before).normalized;
            Require(Vector2.Angle(initial, Vector2.right) <= 60.01f, "Initial escape is outside the away cone");
            for (int i = 0; i < 1800; i++)
            {
                f.Step(.02f);
                Require(f.Map.TrySampleMapPosition(f.Agent.Motor.CurrentMapPosition, out _), "Escape crossed map edge");
                Require(f.Bird.TargetTree == null && f.Agent.CurrentState == AnimalState.Fleeing, "Escape returned to tree");
            }
            Require(Vector2.Distance(before, f.Agent.Motor.CurrentMapPosition) > 10f, "Escape made no progress");
            f.Bird.Settings.FleeSafeDistanceMultiplier = 1f;
            // A camera encompassing the whole map must suppress distance-only despawn.
            Camera camera = f.New("Test camera").AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 200f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            typeof(AnimalWorldQuery).GetProperty("Camera").SetValue(f.Agent.Simulation.World, camera);
            for (int i = 0; i < 150; i++) f.Step(.02f);
            Require(!f.Agent.Runtime.Removed, "Visible fleeing bird was despawned by distance only");
            camera.transform.position = new Vector3(-1000f, -1000f, -10f);
            for (int i = 0; i < 150 && !f.Agent.Runtime.Removed; i++) f.Step(.02f);
            Require(f.Agent.Runtime.Removed, "Distant offscreen escape was not reclaimed");
        }
        private static void CheckRecoveryAndCooldown()
        {
            using var f = new Fixture(true);
            var fly = DailySettings(AnimalDailyBehaviourKind.FlyToTree, 1f, 1f, 1f);
            Require(f.Bird.CreateDailyAction(fly) == null, "Initial minimum stay was bypassed");
            f.Tree(new Vector2(70f, 50f), TreeHealthState.Dead);
            f.Agent.Runtime.Time = 16f;
            Require(f.Bird.CreateDailyAction(fly) is FlyToTreeAction, "Global target was unavailable after minimum stay");
            TreeHabitat other = f.Tree(new Vector2(80f, 60f), TreeHealthState.Dead);
            f.Start(new FlyToTreeAction(f.Bird, other));
            other.gameObject.SetActive(false);
            f.Step(.02f);
            Require(!f.Agent.Brain.HasOverrideAction && f.Agent.CurrentState == AnimalState.Daily, "Removed flight target did not fail safely");
            Require(f.Bird.CreateDailyAction(fly) == null, "Failed flight did not establish retry cooldown");
        }
        public static AnimalDailyBehaviourSettings DailySettings(AnimalDailyBehaviourKind kind, float weight, float min, float max)
        {
            var settings = new AnimalDailyBehaviourSettings(kind, weight, min, max);
            var action = AssetDatabase.LoadAssetAtPath<TreeBirdActionDefinition>("Assets/Data/Animals/SpeciesActions/" + kind + ".asset");
            Require(action != null, "Missing tree bird action asset: " + kind);
            var copy = Object.Instantiate(action);
            copy.hideFlags = HideFlags.HideAndDontSave;
            actionCopies.Add(copy);
            var serialized = new SerializedObject(copy);
            var duration = serialized.FindProperty("durationSeconds");
            if (duration != null) duration.vector2Value = new Vector2(min, max);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Set(settings, "action", copy);
            return settings;
        }
        private static readonly List<Object> actionCopies = new();
        public static void CleanupActionCopies()
        {
            foreach (var copy in actionCopies) if (copy != null) Object.DestroyImmediate(copy);
            actionCopies.Clear();
        }
        private static void CheckActionAssets()
        {
            using var f = new Fixture(true);
            var perch = DailySettings(AnimalDailyBehaviourKind.PerchAtTree, 1f, .2f, .2f);
            Set(perch, "behaviour", AnimalDailyBehaviourKind.Idle);
            Set(perch, "durationSeconds", new Vector2(100f, 100f));
            var rest = f.Bird.CreateDailyAction(perch);
            Require(rest is PerchAtTreeAction && perch.Action is PerchAtTreeActionDefinition, "Perch asset did not select its own action type");
            rest.Enter(f.Agent.Context);
            Require(rest.Tick(f.Agent.Context, .21f) == AnimalActionStatus.Succeeded, "Perch asset duration was ignored");
            rest.Exit(f.Agent.Context);

            var peck = DailySettings(AnimalDailyBehaviourKind.PeckAtTree, 1f, .3f, .3f);
            Require(peck.Action is PeckAtTreeActionDefinition, "Peck asset has the wrong type");
            Set(peck.Action, "amplitudeMeters", 0f);
            var pecking = f.Bird.CreateDailyAction(peck);
            pecking.Enter(f.Agent.Context); pecking.Tick(f.Agent.Context, .1f);
            Require(f.Visual.localPosition.sqrMagnitude < .000001f, "Peck asset amplitude was ignored");
            pecking.Exit(f.Agent.Context);

            var fly = DailySettings(AnimalDailyBehaviourKind.FlyToTree, 1f, 1f, 1f);
            var flightDefinition = fly.Action as FlyToTreeActionDefinition;
            Require(flightDefinition != null, "Flight asset has the wrong type");
            flightDefinition.SpeedMetersPerSecond = 2f; flightDefinition.FlightSpeedVariation = 0f;
            flightDefinition.ArcProbability = 0f; flightDefinition.HealthyTreeWeight = 1f; flightDefinition.DeadTreeWeight = 0f;
            var healthy = f.Tree(new Vector2(70f, 50f), TreeHealthState.Healthy);
            f.Tree(new Vector2(30f, 50f), TreeHealthState.Dead);
            var flight = f.Bird.CreateDailyAction(fly, true) as FlyToTreeAction;
            Require(flight != null, "Flight asset failed to create an action");
            flight.Enter(f.Agent.Context);
            Require(f.Bird.TargetTree == healthy && !flight.IsCurved, "Flight asset weights or arc probability were ignored");
            f.Agent.Motor.Tick(.1f);
            Require(Mathf.Abs(f.Agent.Motor.CurrentSpeedMetersPerSecond - 2f) < .001f, "Flight asset speed was ignored");
            flight.Exit(f.Agent.Context);
            Require(flightDefinition != null, "Flight destroyed a supplied definition");

            // Woodpecker daily actions use the shared flight asset, even when
            // the inherited fallback settings request a straight flight.
            f.Bird.Settings.ArcProbability = 0f;
            flightDefinition.ArcProbability = 1f;
            Require(f.Bird.SnapToBirthPerch(), "Could not restore perch for daily curved flight");
            Vector2 start = f.Agent.Motor.CurrentMapPosition;
            flight = f.Bird.CreateDailyAction(fly, true) as FlyToTreeAction;
            Require(flight != null, "Woodpecker daily flight did not use the shared action");
            flight.Enter(f.Agent.Context);
            Require(flight.IsCurved, "Woodpecker daily flight ignored the shared arc probability");
            CheckCircularPath(f, start, flightDefinition.ArcOffsetRatio, flightDefinition.MaximumArcOffsetMeters);
            flight.Exit(f.Agent.Context);
        }
        private static void CheckCadence()
        {
            using var f = new Fixture(true);
            f.Tree(new Vector2(70f, 50f), TreeHealthState.Dead);
            f.Tree(new Vector2(30f, 30f), TreeHealthState.Healthy);
            var shipped = AssetDatabase.LoadAssetAtPath<AnimalSpeciesConfig>("Assets/Data/Animals/PileatedWoodpeckerConfig.asset");
            Require(shipped != null && shipped.DailyBehaviours.Count == 3, "Shipped woodpecker config must have three daily entries");
            Set(f.Config, "dailyBehaviours", new List<AnimalDailyBehaviourSettings>(shipped.DailyBehaviours));
            foreach (var entry in shipped.DailyBehaviours)
            {
                Require(entry.Action is TreeBirdActionDefinition && f.Agent.DailyScheduler.Weight(entry) > 0f,
                    "Shipped daily entry is missing its Action or has zero scheduling weight: " + entry.Behaviour);
                foreach (int month in new[] { 1, 4, 7, 10 })
                    foreach (int hour in new[] { 0, 6, 12, 18 })
                        Require(entry.SeasonMultiplier * entry.TimeOfDayMultiplier * entry.CalendarWeight(new DateTime(2026, month, 1, hour, 0, 0)) > 0f,
                            "Shipped daily entry is disabled by calendar weights: " + entry.Behaviour);
            }
            f.Agent.ReturnToDaily();
            float treeTime = 0f, activityTime = 0f, flightTime = 0f, perchTime = 0f, peckTime = 0f;
            for (int i = 0; i < 6000; i++)
            {
                f.Step(.05f);
                if (f.Bird.CurrentTree != null) treeTime += .05f;
                string name = f.Agent.Brain.CurrentActionName;
                if (name == nameof(TreeActivityAction)) activityTime += .05f;
                else if (name == nameof(FlyToTreeAction)) flightTime += .05f;
                else if (name == nameof(WoodpeckerPeckAction)) peckTime += .05f;
                else if (name == nameof(PerchAtTreeAction)) perchTime += .05f;
            }
            Require(treeTime >= 240f && perchTime > flightTime && perchTime > peckTime && flightTime > 0f && peckTime > 0f,
                $"Daily occupancy target missed: tree {treeTime}, activity {activityTime}, fly {flightTime}, perch {perchTime}, peck {peckTime}");
        }
        private static void CheckEnableLifecycle()
        {
            using var f = new Fixture(true);
            for (int i = 0; i < 2; i++)
            {
                f.Start(new WoodpeckerPeckAction((PileatedWoodpeckerBehaviour)f.Bird, 10f));
                f.Step(.2f);
                f.Agent.gameObject.SetActive(false);
                // Preview-scene edit-mode objects do not receive normal player-loop
                // callbacks; explicitly exercise the callbacks here, as in other fixtures.
                f.Bird.GetType().GetMethod("OnDisable", Flags).Invoke(f.Bird, null);
                Require(f.Visual.localPosition.sqrMagnitude < .000001f && !f.Agent.Motor.HasTarget,
                    "Disabling bird left a peck offset or movement target");
                f.Agent.gameObject.SetActive(true);
                typeof(TreeBirdBehaviour).GetMethod("OnEnable", Flags).Invoke(f.Bird, null);
                Require(!f.Agent.Brain.HasOverrideAction, "Re-enabling bird resumed a stale interrupted action");
            }
        }
        private sealed class Fixture : IDisposable
        {
            public readonly Scene Scene;
            public readonly MapTestSceneController Map;
            public readonly AnimalAgent Agent;
            public readonly AnimalSpeciesConfig Config;
            public readonly TreeBirdBehaviour Bird;
            public readonly TreeHabitat Home;
            public readonly Transform Visual;
            private readonly TiledMapTestFixture tiles;
            private readonly Texture2D texture;
            public Fixture(bool woodpecker)
            {
                Scene = EditorSceneManager.NewPreviewScene();
                Map = New("Bird map").AddComponent<MapTestSceneController>(); Map.enabled = false;
                texture = new Texture2D(2, 2);
                texture.SetPixels(new[] { Color.gray, Color.gray, Color.gray, Color.gray }); texture.Apply();
                tiles = new TiledMapTestFixture(Map, texture, 100f, 1f);
                Home = Tree(new Vector2(50f, 50f), TreeHealthState.Dead);
                Config = ScriptableObject.CreateInstance<AnimalSpeciesConfig>();
                Set(Config, "dailyMoveSpeedMetersPerSecond", 9f); Set(Config, "fleeSpeedMetersPerSecond", 14f);
                Set(Config, "baseDetectionChancePerCheck", 0f);
                Set(Config, "dailyBehaviours", new List<AnimalDailyBehaviourSettings> { DailySettings(AnimalDailyBehaviourKind.PerchAtTree, 1f, 1000f, 1000f) });
                GameObject actor = New("Bird"); actor.transform.position = Map.MapPositionToWorld(new Vector2(50f, 48f));
                Bird = (TreeBirdBehaviour)actor.AddComponent(woodpecker ? typeof(PileatedWoodpeckerBehaviour) : typeof(TreeBirdBehaviour));
                Agent = actor.AddComponent<AnimalAgent>();
                Visual = New("Visual").transform; Visual.SetParent(actor.transform, false);
                Set(actor.GetComponent<HeightMapPlacedObject>(), "map", Map);
                Bird.BindBirthTree(Home, false);
                if (Bird is PileatedWoodpeckerBehaviour wood) wood.ConfigureEditorDefaults(Home, Visual);
                Agent.ConfigureEditorDefaults(Config, Bird, null);
                Require(Agent.TryInitialize(), "Bird fixture did not initialize");
                // Tests below concern behaviour, not renderer-dependent sound wave allocation.
                Set(Agent, "soundEmitter", null);
            }
            public GameObject New(string name) { var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, Scene); return go; }
            public TreeHabitat Tree(Vector2 position, TreeHealthState health)
            {
                GameObject go = New("Tree"); go.transform.position = Map.MapPositionToWorld(position);
                var placed = go.AddComponent<HeightMapPlacedObject>(); Set(placed, "map", Map);
                var tree = go.AddComponent<TreeHabitat>(); tree.ConfigureEditorDefaults(health, null, .6f); return tree;
            }
            public Transform Threat(Vector2 position) { var go = New("Threat"); go.transform.position = Map.MapPositionToWorld(position); return go.transform; }
            public void Start(AnimalAction action) { Agent.ReturnToDaily(); Require(Agent.Brain.StartAction(action), "Action rejected"); }
            public void Step(float dt)
            {
                if (Agent.Runtime.Removed) return;
                Agent.Runtime.Time += dt; Agent.Brain.Tick(dt); Agent.Motor.Tick(dt);
            }
            public void Dispose()
            {
                EditorSceneManager.ClosePreviewScene(Scene); tiles.Dispose(); Object.DestroyImmediate(texture); Object.DestroyImmediate(Config);
            }
        }
    }
}
