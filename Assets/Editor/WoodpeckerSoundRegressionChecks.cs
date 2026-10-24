using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using AnimalGame.Animals;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace AnimalGame.Editor
{
    // Exercises the shipped sound profiles and actual behaviour/motor code in
    // preview scenes. The factory comparison never rewrites production assets.
    public static class WoodpeckerSoundRegressionChecks
    {
        private const string WoodPrefab = "Assets/Prefabs/Animals/PileatedWoodpecker/PileatedWoodpecker_Placeholder.prefab";
        private const string WoodConfig = "Assets/Data/Animals/PileatedWoodpeckerConfig.asset";
        private const string MuskratPrefab = "Assets/Prefabs/Animals/Muskrat/Muskrat_Placeholder.prefab";
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly string[] ProfileNames =
        {
            "idleSound", "lookingSound", "curiousSound", "eatingSound", "landMovementSound", "waterMovementSound",
            "fleeingSound", "submergingSound", "surfacingSound", "flyingSound", "peckingSound", "takeoffSound",
            "landingSound", "enteringTreeSound", "emergingFromTreeSound"
        };
        private static int passed;

        [MenuItem("Animal Game/Validation/Run Woodpecker Sound Checks")]
        public static void Run()
        {
            Random.State originalRandom = Random.state;
            try
            {
                Random.InitState(198403);
                passed = 0;
                Check(CheckSerializedKinds);
                Check(CheckProductionAndFactoryProfiles);
                Check(CheckMuskratCompatibility);
                Check(CheckActualRepeatIntervals);
                Check(CheckWorldAnchoredEmission);
                Check(CheckAerialWaterAndFleeClassification);
                Check(CheckFlightOnsetsAndArrival);
                Check(CheckStationaryFlightRequest);
                Check(CheckMuskratMovementCountdown);
                Check(CheckPassiveCadenceAcrossFallbackRetries);
                Check(CheckDirectEscape);
                Check(CheckPeckCadenceAndContact);
                Check(CheckInterruptedPecking);
                Check(CheckFleeCadence);
                Check(CheckNoTreeHiding);
                Check(CheckWaveFadeLifetimeAndPool);
                Debug.Log($"Woodpecker sound checks PASS: {passed} groups; shipped/factory profiles, Muskrat compatibility, world origins, aerial classification, state pulses, contact-synchronized pecking, direct escape, pause and pooled wave lifetime");
            }
            finally { Random.state = originalRandom; TreeBirdRegressionChecks.CleanupActionCopies(); }
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void Check(Action action) { action(); passed++; }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        private static void Near(float actual, float expected, string message)
        { Require(Mathf.Abs(actual - expected) < .0003f, $"{message}: {actual} versus {expected}"); }
        private static FieldInfo Field(Type type, string name)
        {
            while (type != null)
            {
                FieldInfo field = type.GetField(name, Instance | Static | BindingFlags.DeclaredOnly);
                if (field != null) return field;
                type = type.BaseType;
            }
            throw new MissingFieldException(name);
        }
        private static object Get(object target, string name) => Field(target.GetType(), name).GetValue(target);
        private static void Set(object target, string name, object value) => Field(target.GetType(), name).SetValue(target, value);
        private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Instance).Invoke(target, args);
        private static void Property(object target, string name, object value)
        {
            if (target is AnimalAgent agent && name == "CurrentState") { typeof(AnimalBrain).GetProperty("State").SetValue(agent.Brain, value); return; }
            target.GetType().GetProperty(name, Instance).SetValue(target, value);
        }
        private static AnimalSoundWaveSettings Profile(AnimalSoundEmitter emitter, string name) => (AnimalSoundWaveSettings)Get(emitter, name);
        private static void Phase(PileatedWoodpeckerBehaviour behaviour, string field, string phase)
        {
            FieldInfo info = Field(behaviour.GetType(), field);
            info.SetValue(behaviour, Enum.Parse(info.FieldType, phase));
        }
        private static string Phase(PileatedWoodpeckerBehaviour behaviour, string field) => Get(behaviour, field).ToString();

        private static void CheckSerializedKinds()
        {
            string[] original = { "Idle", "Looking", "Curious", "Eating", "MovingOnLand", "MovingInWater", "Fleeing", "Submerging", "Surfacing" };
            for (int index = 0; index < original.Length; index++)
                Require((int)Enum.Parse(typeof(AnimalSoundKind), original[index]) == index, "Existing serialized sound kind changed: " + original[index]);
            foreach (string added in new[] { "Flying", "Pecking", "Takeoff", "Landing", "EnteringTree", "EmergingFromTree" })
                Require((int)Enum.Parse(typeof(AnimalSoundKind), added) >= original.Length, "Bird sound overwrote an existing serialized value");
        }

        private static void CheckProductionAndFactoryProfiles()
        {
            using var f = new Fixture();
            AnimalSoundEmitter generated = f.New("Factory profile check").AddComponent<AnimalSoundEmitter>();
            typeof(PileatedWoodpeckerPrefabGenerator).GetMethod("ConfigureAnimalSounds", Static).Invoke(null, new object[] { generated });
            foreach (string name in ProfileNames)
                Require(JsonUtility.ToJson(Profile(f.Emitter, name)) == JsonUtility.ToJson(Profile(generated, name)), "Shipped prefab differs from factory profile: " + name);
            foreach (string name in new[] { "overallRadiusMultiplier", "overallRingCountMultiplier", "overallEmissionFrequencyMultiplier", "ringBreakup", "ringIrregularity" })
                Near((float)Get(f.Emitter, name), (float)Get(generated, name), "Factory appearance mismatch: " + name);
            Require(Get(f.Emitter, "soundWaveShader") is Shader shader && shader.name == "AnimalGame/Animal Sound Wave", "Bird prefab has no direct sound shader reference");
            Require(Get(generated, "soundWaveShader") == Get(f.Emitter, "soundWaveShader"), "Factory lost sound shader reference");
            foreach (string name in new[] { "idleSound", "curiousSound", "fleeingSound", "flyingSound", "peckingSound", "takeoffSound", "landingSound", "enteringTreeSound", "emergingFromTreeSound" })
                Require(Profile(f.Emitter, name).Enabled, "Bird profile disabled: " + name);
            foreach (string name in new[] { "lookingSound", "eatingSound", "landMovementSound", "waterMovementSound", "submergingSound", "surfacingSound" })
                Require(!Profile(f.Emitter, name).Enabled, "Bird acquired an unrelated land/water/eating profile: " + name);
            Near((float)Get(f.Emitter, "overallEmissionFrequencyMultiplier"), 1f, "Bird cadence multiplier");
            Require(!f.Behaviour.SupportsAggression, "Sound integration changed woodpecker aggression support");
            var flight = AssetDatabase.LoadAssetAtPath<FlyToTreeActionDefinition>("Assets/Data/Animals/SpeciesActions/FlyToTree.asset");
            var peck = AssetDatabase.LoadAssetAtPath<PeckAtTreeActionDefinition>("Assets/Data/Animals/SpeciesActions/PeckAtTree.asset");
            Near(flight.MinimumTreeStaySeconds, 15f, "Minimum tree stay");
            Require((int)Get(peck, "pecksPerSoundWave") == 3, "Default peck grouping is not three contacts");
        }

        private static void CheckMuskratCompatibility()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MuskratPrefab);
            Require(prefab != null, "Missing shipped Muskrat prefab");
            AnimalSoundEmitter emitter = prefab.GetComponent<AnimalSoundEmitter>();
            Near((float)Get(emitter, "overallEmissionFrequencyMultiplier"), .15f, "Muskrat frequency changed");
            Near((float)Get(emitter, "overallRadiusMultiplier"), .5f, "Muskrat radius changed");
            for (int index = 0; index < 9; index++) Require(Profile(emitter, ProfileNames[index]).Enabled, "Muskrat existing profile disabled");
            for (int index = 9; index < ProfileNames.Length; index++) Require(!Profile(emitter, ProfileNames[index]).Enabled, "Bird-only profile enabled on Muskrat");
            Near(Profile(emitter, "landMovementSound").MaximumRadiusMeters, .78f, "Muskrat land wave changed");
            Near(Profile(emitter, "fleeingSound").DurationSeconds, .78f, "Muskrat fleeing wave changed");
        }

        private static void CheckActualRepeatIntervals()
        {
            using var f = new Fixture();
            void Range(AnimalSoundKind kind, float minimum, float maximum)
            {
                for (int index = 0; index < 80; index++)
                {
                    float interval = f.Emitter.ChooseRepeatInterval(kind);
                    Require(interval >= minimum && interval <= maximum, $"Actual {kind} interval {interval} outside {minimum}–{maximum}");
                }
            }
            Range(AnimalSoundKind.Idle, 4f, 6f);
            Range(AnimalSoundKind.Curious, 2.5f, 4f);
            Range(AnimalSoundKind.Flying, 1.2f, 1.8f);
            Range(AnimalSoundKind.Fleeing, .5f, .8f);
            Set(f.Emitter, "overallEmissionFrequencyMultiplier", .5f);
            Range(AnimalSoundKind.Flying, 2.4f, 3.6f);
        }

        private static void CheckWorldAnchoredEmission()
        {
            using var f = new Fixture();
            Vector3 origin = new Vector3(7f, -5f, .3f);
            Set(Profile(f.Emitter, "peckingSound"), "radiusVariation", 0f);
            f.Emitter.EmitAt(AnimalSoundKind.Pecking, origin);
            Require(f.Count == 1, "Contact wave not emitted");
            Transform wave = (Transform)Get(f.Last, "Transform");
            Require(Vector3.Distance(wave.position, origin) < .0001f, "Wave did not use explicit contact origin");
            f.Agent.transform.position += Vector3.right * 10f;
            f.Agent.transform.rotation = Quaternion.Euler(0f, 0f, 170f);
            Require(Vector3.Distance(wave.position, origin) < .0001f && wave.parent != f.Agent.transform, "Wave followed/rotated with the flying bird");
            Near(wave.localScale.x, .52f, "Profile maximum diameter after global radius multiplier");
            Require((float)Get(f.Last, "RingCount") == 2f, "Global ring restraint did not round bird peck rings");
        }

        private static void CheckAerialWaterAndFleeClassification()
        {
            using var f = new Fixture();
            HeightMapLevelAsset water = f.Map.Presentation;
            Set(water, "mapWidthMeters", 100f); Set(water, "mapHeightMeters", 100f);
            Set(water, "staticWaterDepthMap", new byte[] { 255, 255, 255, 255 });
            Require(f.Map.TrySampleStaticWaterMapPosition(f.Motor.CurrentMapPosition, out float depth) && depth > .01f, "Water-classification fixture is dry");
            Require(f.Motor.SetAerialTarget(new Vector2(80f, 50f), 9f), "Aerial fixture target rejected");
            Require((AnimalSoundKind)Call(f.Emitter, "ChooseMovementSoundKind") == AnimalSoundKind.Flying, "Flight over water was classified as swimming");
            Property(f.Agent, "CurrentState", AnimalState.Fleeing);
            Require((AnimalSoundKind)Call(f.Emitter, "ChooseMovementSoundKind") == AnimalSoundKind.Fleeing, "Aerial flee lost alarm cadence");
            Property(f.Agent, "CurrentState", AnimalState.Daily);
            Require(f.Motor.SetTarget(new Vector2(80f, 50f), 1f), "Ground fixture target rejected");
            Require((AnimalSoundKind)Call(f.Emitter, "ChooseMovementSoundKind") == AnimalSoundKind.MovingInWater, "Shared emitter lost swimming classification");
            Set(water, "staticWaterDepthMap", new byte[0]);
            Require((AnimalSoundKind)Call(f.Emitter, "ChooseMovementSoundKind") == AnimalSoundKind.MovingOnLand, "Shared emitter lost ground classification");
        }

        private static void CheckFlightOnsetsAndArrival()
        {
            using var f = new Fixture();
            f.Agent.Brain.StartAction(new FlyToTreeAction(f.Behaviour, f.OtherTree));
            Require(f.Count == 1, "Takeoff should emit exactly one pulse");
            Near((float)Get(f.Last, "Duration"), .4f, "Takeoff profile duration");
            object originalWave = f.Waves[0];
            Vector3 originalPosition = ((Transform)Get(originalWave, "Transform")).position;
            f.Motor.Tick(.01f); f.Emitter.Tick(.01f);
            Require(f.Count == 1, "Takeoff stacked an immediate movement wave");
            for (int i = 0; i < 11; i++) { f.Motor.Tick(.1f); f.Emitter.Tick(.1f); }
            Require(f.Count == 1, "Flight repeated before its configured interval");
            for (int i = 0; i < 7; i++) { f.Motor.Tick(.1f); f.Emitter.Tick(.1f); }
            Require(f.Count == 2, "Flight did not repeat within its configured interval");
            f.Motor.TeleportToMapPosition(f.Motor.TargetMapPosition);
            Property(f.Motor, "HasArrived", true);
            f.Agent.Brain.Tick(.01f);
            Require(f.Count == 3 && f.Behaviour.CurrentTree == f.OtherTree, "Arrival lost its landing pulse or perch");
            f.Agent.Brain.Tick(.01f); f.Emitter.Tick(.01f);
            Require(f.Count == 3, "Landing pulse repeated while stationary");
            Require(Vector3.Distance(((Transform)Get(originalWave, "Transform")).position, originalPosition) < .0001f, "Flight trail moved with bird");
        }

        private static void CheckPassiveCadenceAcrossFallbackRetries()
        {
            using var f = new Fixture();
            for (int i = 0; i < 39; i++) f.Agent.Brain.Tick(.1f);
            Require(f.Count == 0, "Perched bird emitted before quiet interval");
            for (int i = 0; i < 22; i++) f.Agent.Brain.Tick(.1f);
            Require(f.Count == 1, "Perched bird missed quiet interval");
            f.ClearWaves();
            Set(f.Config, "dailyBehaviours", new List<AnimalDailyBehaviourSettings>());
            Set(f.Behaviour, "quietCountdown", 5f);
            f.Agent.ReturnToDaily();
            for (int i = 0; i < 39; i++) f.Agent.Brain.Tick(.1f);
            Require(f.Count == 0, "Fallback retries spammed idle waves");
            for (int i = 0; i < 25; i++) f.Agent.Brain.Tick(.1f);
            Require(f.Count == 1, "Fallback retries reset quiet timer");
        }

        private static void CheckStationaryFlightRequest()
        {
            using var f = new Fixture();
            Vector2 original = f.Motor.CurrentMapPosition;
            f.Agent.Brain.StartAction(new FlyToTreeAction(f.Behaviour, f.HomeTree));
            f.Agent.Brain.Tick(.01f); f.Motor.Tick(.01f); f.Emitter.Tick(.01f);
            Require(f.Behaviour.CurrentTree == f.HomeTree && f.Count == 0, "Stationary flight emitted movement pulses");
            Require(Vector2.Distance(original, f.Motor.CurrentMapPosition) < .0001f, "Stationary flight moved the bird");
        }

        private static void CheckMuskratMovementCountdown()
        {
            using var f = new Fixture();
            AnimalSoundEmitter original = AssetDatabase.LoadAssetAtPath<GameObject>(MuskratPrefab).GetComponent<AnimalSoundEmitter>();
            AnimalSoundEmitter emitter = f.New("Muskrat cadence compatibility").AddComponent<AnimalSoundEmitter>();
            EditorUtility.CopySerialized(original, emitter); emitter.Initialize(f.Agent);
            f.Motor.ClearMovementOverrides();
            Require(f.Motor.SetTarget(new Vector2(80f, 50f), 1f), "Ground cadence fixture target rejected");
            f.Motor.Tick(.01f); emitter.Tick(.01f);
            Require(f.Count == 1, "Muskrat movement lost immediate initial sound");
            float countdown = (float)Get(emitter, "movementSoundCountdown");
            Require(countdown >= 3.2f && countdown <= 4.54f, "Muskrat initial land cadence changed");
            HeightMapLevelAsset water = f.Map.Presentation;
            Set(water, "mapWidthMeters", 100f); Set(water, "mapHeightMeters", 100f);
            Set(water, "staticWaterDepthMap", new byte[] { 255, 255, 255, 255 });
            emitter.Tick(.1f);
            Require(f.Count == 1, "Muskrat land-to-water transition stacked another sound");
            Near((float)Get(emitter, "movementSoundCountdown"), countdown - .1f, "Land-to-water transition reset Muskrat countdown");
            Property(f.Agent, "CurrentState", AnimalState.Fleeing); emitter.Tick(.1f);
            Require(f.Count == 1, "Muskrat flee transition stacked another movement sound");
            Near((float)Get(emitter, "movementSoundCountdown"), countdown - .2f, "Flee transition reset Muskrat countdown");
            emitter.Tick(countdown);
            Require(f.Count == 2, "Muskrat did not emit fleeing profile at preserved next pulse");
            Near((float)Get(f.Last, "Duration"), .78f, "Preserved next Muskrat pulse used wrong sound profile");
        }

        private static void CheckDirectEscape()
        {
            using var f = new Fixture();
            GameObject threat = f.New("Bird threat");
            threat.transform.position = f.Map.MapPositionToWorld(new Vector2(49f, 49f));
            f.Agent.NotifyThreat(threat.transform);
            Require(f.Agent.CurrentState == AnimalState.Fleeing, "Bird paused to observe threat");
            Require(f.Count == 1 && f.Motor.IsAerialMovement, "Direct escape lost its alarm or aerial movement");
        }

        private static void CheckPeckCadenceAndContact()
        {
            foreach (int fps in new[] { 30, 60, 144 })
            {
                using var f = new Fixture();
                f.BeginPecking();
                for (int i = 0; i < fps * 2; i++) f.Agent.Brain.Tick(1f / fps);
                Require(f.Count == 3, $"Peck cadence depends on frame rate {fps}: {f.Count}");
                Vector3 origin = ((Transform)Get(f.Waves[0], "Transform")).position;
                Near(Vector2.Distance(origin, f.HomeTree.TrunkWorldPosition), f.HomeTree.PerchRadiusMeters, "Peck sound missed trunk contact");
                Near((float)Get(f.Last, "Duration"), .35f, "Peck wave duration");
            }
            using (var f = new Fixture())
            {
                f.BeginPecking(); f.Agent.Brain.Tick(2f);
                Require(f.Count == 1, "Long frame emitted a burst of missed pecks");
            }
        }

        private static void CheckInterruptedPecking()
        {
            using var f = new Fixture();
            f.BeginPecking();
            f.HomeTree.ConfigureEditorDefaults(TreeHealthState.Healthy, null, .6f);
            f.Agent.Brain.Tick(.8f);
            Require(f.Count == 0 && !f.Agent.Brain.HasOverrideAction, "Invalid peck target kept pecking");
            f.HomeTree.ConfigureEditorDefaults(TreeHealthState.Dead, null, .6f);
            f.BeginPecking(); f.Agent.Brain.Tick(.3f);
            GameObject threat = f.New("Peck interruption");
            threat.transform.position = f.Map.MapPositionToWorld(new Vector2(49f, 49f));
            f.Agent.NotifyThreat(threat.transform);
            Require(f.Count == 1 && f.Agent.CurrentState == AnimalState.Fleeing, "Interrupted peck leaked a contact pulse");
        }

        private static void CheckFleeCadence()
        {
            using var f = new Fixture();
            GameObject threat = f.New("Flee threat");
            threat.transform.position = f.Map.MapPositionToWorld(new Vector2(49f, 49f));
            f.Agent.NotifyThreat(threat.transform);
            Require(f.Count == 1, "Escape should emit one alarm pulse");
            f.Motor.Tick(.01f); f.Emitter.Tick(.01f);
            Require(f.Count == 1, "Alarm stacked immediate wing pulse");
            for (int i = 0; i < 4; i++) { f.Agent.Brain.Tick(.1f); f.Motor.Tick(.1f); f.Emitter.Tick(.1f); }
            Require(f.Count == 1, "Escape repeated too early");
            for (int i = 0; i < 5; i++) { f.Agent.Brain.Tick(.1f); f.Motor.Tick(.1f); f.Emitter.Tick(.1f); }
            Require(f.Count == 2 && f.Agent.CurrentState == AnimalState.Fleeing, "Escape cadence or state changed");
            Require(f.Behaviour.TargetTree == null, "Escape chose a home tree");
        }

        private static void CheckNoTreeHiding()
        {
            using var f = new Fixture();
            f.Agent.BeginHiding();
            Require(f.Agent.CurrentState == AnimalState.Fleeing, "Bird entered obsolete tree hiding state");
            Require(f.Count == 1 && f.Behaviour.TargetTree == null, "Bird emitted obsolete tree-entry sounds");
        }

        private static void CheckWaveFadeLifetimeAndPool()
        {
            using var f = new Fixture();
            f.Emitter.Emit(AnimalSoundKind.Flying);
            object wave = f.Last;
            MethodInfo updateVisual = typeof(AnimalSoundWaveManager).GetMethod("UpdateWaveVisual", Static);
            updateVisual.Invoke(null, new object[] { wave, .2f / .45f });
            var properties = (MaterialPropertyBlock)Get(wave, "Properties");
            Require(properties.GetFloat("_Progress") > .5f && properties.GetFloat("_Opacity") > .1f, "Wave did not expand/fade in through actual visual update");
            updateVisual.Invoke(null, new object[] { wave, .44f / .45f });
            Require(properties.GetFloat("_Opacity") < .1f, "Wave did not fade at end of lifetime");
            Set(wave, "Elapsed", .45f); Call(f.Manager, "Update");
            Require(!(bool)Get(wave, "Active") && !((GameObject)Get(wave, "GameObject")).activeSelf, "Expired wave remained visible");
            f.Emitter.Emit(AnimalSoundKind.Landing);
            Require(f.Waves.Count == 1 && ReferenceEquals(f.Last, wave), "Wave pool did not reuse expired pulse");
            for (int index = 0; index < 300; index++) f.Emitter.Emit(AnimalSoundKind.Flying);
            Require(f.Waves.Count == 256, "Wave pool exceeded its bounded size");
        }

        private sealed class Fixture : IDisposable
        {
            private TiledMapTestFixture tiledFixture;
            private readonly List<Object> assets = new List<Object>();
            private readonly AnimalSoundWaveManager previousManager;
            public Scene Scene { get; }
            public MapTestSceneController Map { get; }
            public AnimalAgent Agent { get; }
            public AnimalSpeciesConfig Config { get; }
            public PileatedWoodpeckerBehaviour Behaviour { get; }
            public AnimalMotor Motor => Agent.Motor;
            public AnimalSoundEmitter Emitter => Agent.SoundEmitter;
            public TreeHabitat HomeTree { get; }
            public TreeHabitat OtherTree { get; }
            public AnimalSoundWaveManager Manager { get; }
            public IList Waves => (IList)Get(Manager, "waves");
            public int Count => Waves.Count;
            public object Last => Waves[Waves.Count - 1];

            public Fixture()
            {
                Scene = EditorSceneManager.NewPreviewScene();
                previousManager = (AnimalSoundWaveManager)Field(typeof(AnimalSoundWaveManager), "instance").GetValue(null);
                Field(typeof(AnimalSoundWaveManager), "instance").SetValue(null, null);
                Manager = New("Woodpecker check wave manager").AddComponent<AnimalSoundWaveManager>();
                Field(typeof(AnimalSoundWaveManager), "instance").SetValue(null, Manager);
                GameObject mapRoot = New("Woodpecker check flat map");
                Map = mapRoot.AddComponent<MapTestSceneController>(); Map.enabled = false;
                Texture2D texture = Own(new Texture2D(2, 2));
                texture.SetPixels(new[] { Color.gray, Color.gray, Color.gray, Color.gray }); texture.Apply();
                Sprite sprite = Own(Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f), Vector2.one * .5f, .02f));
                SpriteRenderer renderer = mapRoot.AddComponent<SpriteRenderer>(); renderer.sprite = sprite;
                tiledFixture = new TiledMapTestFixture(Map, texture, 100, 1);
                Require(Map.TrySampleMapPosition(Vector2.one * 50f, out _), "Flat fixture map is not playable");
                HomeTree = Tree(new Vector2(50f, 50f)); OtherTree = Tree(new Vector2(70f, 50f));
                AnimalSpeciesConfig shippedConfig = AssetDatabase.LoadAssetAtPath<AnimalSpeciesConfig>(WoodConfig);
                Require(shippedConfig != null, "Missing woodpecker species configuration");
                Config = Own(Object.Instantiate(shippedConfig));
                Set(Config, "dailyBehaviours", new List<AnimalDailyBehaviourSettings> { TreeBirdRegressionChecks.DailySettings(AnimalDailyBehaviourKind.PerchAtTree, 1f, 1000f, 1000f) });
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WoodPrefab);
                Require(prefab != null, "Missing shipped woodpecker prefab");
                GameObject actor = (GameObject)PrefabUtility.InstantiatePrefab(prefab, Scene);
                actor.transform.position = Map.MapPositionToWorld(new Vector2(50f, 48f));
                Set(actor.GetComponent<HeightMapPlacedObject>(), "map", Map);
                Agent = actor.GetComponent<AnimalAgent>(); Behaviour = actor.GetComponent<PileatedWoodpeckerBehaviour>();
                Set(Behaviour, "birthTree", HomeTree);
                Agent.ConfigureEditorDefaults(Config, Behaviour, actor.GetComponent<AnimalPlaceholderView>());
                Require((bool)Call(Agent, "TryInitialize"), "Actual woodpecker agent failed to initialize in flat fixture");
                Property(Agent.Perception, "Player", null);
                ClearWaves();
            }

            public T Own<T>(T item) where T : Object { assets.Add(item); return item; }
            public GameObject New(string name)
            {
                var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, Scene); return go;
            }
            private TreeHabitat Tree(Vector2 position)
            {
                GameObject root = New("Woodpecker check tree"); root.transform.position = Map.MapPositionToWorld(position);
                HeightMapPlacedObject placement = root.AddComponent<HeightMapPlacedObject>(); Set(placement, "map", Map);
                TreeHabitat tree = root.AddComponent<TreeHabitat>(); tree.ConfigureEditorDefaults(TreeHealthState.Dead, null, .6f); return tree;
            }
            public void BeginPecking()
            {
                Agent.ReturnToDaily();
                Require(Agent.Brain.StartAction(new WoodpeckerPeckAction(Behaviour, 1000f)), "Could not start pecking");
                Motor.Tick(1f);
            }
            public void ClearWaves()
            {
                foreach (object wave in Waves) Object.DestroyImmediate((GameObject)Get(wave, "GameObject"));
                Waves.Clear();
            }
            public void Dispose()
            {
                tiledFixture?.Dispose();
                ClearWaves();
                foreach (string resource in new[] { "waveMaterial", "quadMesh" })
                {
                    Object item = (Object)Get(Manager, resource);
                    if (item != null) Object.DestroyImmediate(item);
                    Set(Manager, resource, null);
                }
                EditorSceneManager.ClosePreviewScene(Scene);
                Field(typeof(AnimalSoundWaveManager), "instance").SetValue(null, previousManager);
                foreach (Object asset in assets) if (asset != null) Object.DestroyImmediate(asset);
            }
        }
    }
}
