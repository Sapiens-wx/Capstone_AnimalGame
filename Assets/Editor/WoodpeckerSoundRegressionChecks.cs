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
                Check(CheckCuriousCadence);
                Check(CheckPeckCadenceAndContact);
                Check(CheckInterruptedPecking);
                Check(CheckFleeAndTreeEntry);
                Check(CheckHiddenAndEmergenceRetries);
                Check(CheckSimulationPause);
                Check(CheckWaveFadeLifetimeAndPool);
                Debug.Log($"Woodpecker sound checks PASS: {passed} groups; shipped/factory profiles, Muskrat compatibility, world origins, aerial classification, state pulses, contact-synchronized pecking, hiding/retries, pause and pooled wave lifetime");
            }
            finally { Random.state = originalRandom; }
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
        private static void Property(object target, string name, object value) => target.GetType().GetProperty(name, Instance).SetValue(target, value);
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
            Near((float)Get(f.Behaviour, "emergenceSoundRetryDelaySeconds"), 2f, "Emergence retry sound delay");
            Require((int)Get(f.Behaviour, "pecksPerSoundWave") == 3, "Default peck grouping is not three contacts");
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
            HeightMapLevelAsset water = f.Own(ScriptableObject.CreateInstance<HeightMapLevelAsset>());
            Set(water, "mapWidthMeters", 100f); Set(water, "mapHeightMeters", 100f);
            Set(water, "staticWaterDepthMap", new byte[] { 255, 255, 255, 255 }); Set(f.Map, "levelAsset", water);
            Require(f.Map.TrySampleStaticWaterMapPosition(f.Motor.CurrentMapPosition, out float depth) && depth > .01f, "Water-classification fixture is dry");
            Require(f.Motor.SetAerialTarget(new Vector2(80f, 50f), 9f), "Aerial fixture target rejected");
            Require((AnimalSoundKind)Call(f.Emitter, "ChooseMovementSoundKind") == AnimalSoundKind.Flying, "Flight over water was classified as swimming");
            Property(f.Agent, "CurrentState", AnimalState.Fleeing);
            Require((AnimalSoundKind)Call(f.Emitter, "ChooseMovementSoundKind") == AnimalSoundKind.Fleeing, "Aerial flee lost alarm cadence");
            Property(f.Agent, "CurrentState", AnimalState.Daily);
            Require(f.Motor.SetTarget(new Vector2(80f, 50f), 1f), "Ground fixture target rejected");
            Require((AnimalSoundKind)Call(f.Emitter, "ChooseMovementSoundKind") == AnimalSoundKind.MovingInWater, "Shared emitter lost swimming classification");
            Set(f.Map, "levelAsset", null);
            Require((AnimalSoundKind)Call(f.Emitter, "ChooseMovementSoundKind") == AnimalSoundKind.MovingOnLand, "Shared emitter lost ground classification");
        }

        private static void CheckFlightOnsetsAndArrival()
        {
            using var f = new Fixture();
            FieldInfo arrival = Field(f.Behaviour.GetType(), "arrivalAction");
            Require((bool)Call(f.Behaviour, "TryBeginFlight", f.OtherTree, Enum.Parse(arrival.FieldType, "Perch")), "Bird flight failed");
            Require(f.Count == 1, "Takeoff should emit exactly one pulse");
            Near((float)Get(f.Last, "Duration"), .4f, "Takeoff profile duration");
            f.Motor.Tick(.01f); f.Emitter.Tick(.01f);
            Require(f.Count == 1, "Takeoff stacked an immediate movement wave");
            for (int index = 0; index < 11; index++) { f.Motor.Tick(.1f); f.Emitter.Tick(.1f); }
            Require(f.Count == 1, "Flight cadence repeated before 1.2 seconds");
            for (int index = 0; index < 7; index++) { f.Motor.Tick(.1f); f.Emitter.Tick(.1f); }
            Require(f.Count == 2, "Flight failed to repeat within 1.8 seconds");
            object originalWave = f.Waves[0];
            Vector3 originalPosition = ((Transform)Get(originalWave, "Transform")).position;
            f.Motor.TeleportToMapPosition(f.Motor.TargetMapPosition);
            Property(f.Motor, "HasArrived", true);
            f.Behaviour.TickDaily(.01f);
            Require(f.Count == 3 && Phase(f.Behaviour, "dailyPhase") == "Perched", "Arrival lost its single landing pulse or perch phase");
            Near((float)Get(f.Last, "Duration"), .4f, "Landing profile duration");
            f.Behaviour.TickDaily(.01f); f.Emitter.Tick(.01f);
            Require(f.Count == 3, "Landing pulse repeated while stationary");
            Require(Vector3.Distance(((Transform)Get(originalWave, "Transform")).position, originalPosition) < .0001f, "Flight trail moved with arrival");
        }

        private static void CheckPassiveCadenceAcrossFallbackRetries()
        {
            using var f = new Fixture();
            for (int index = 0; index < 39; index++) f.Behaviour.TickDaily(.1f);
            Require(f.Count == 0, "Perched bird emitted before its quiet interval");
            for (int index = 0; index < 22; index++) f.Behaviour.TickDaily(.1f);
            Require(f.Count == 1, "Perched bird did not emit within 4–6 seconds");
            f.ClearWaves();
            Set(f.Config, "dailyBehaviours", new List<AnimalDailyBehaviourSettings>());
            Set(f.Behaviour, "passiveSoundCountdown", 5f);
            f.Behaviour.ExitDaily(); f.Behaviour.EnterDaily();
            Require(Phase(f.Behaviour, "dailyPhase") == "FallbackIdle", "Fallback fixture did not enter retry phase");
            for (int index = 0; index < 39; index++) f.Behaviour.TickDaily(.1f);
            Require(f.Count == 0, "One-second fallback retries spammed idle waves");
            for (int index = 0; index < 25; index++) f.Behaviour.TickDaily(.1f);
            Require(f.Count == 1, "Fallback retries reset quiet countdown and prevented all idle waves");
        }

        private static void CheckStationaryFlightRequest()
        {
            using var f = new Fixture();
            FieldInfo arrival = Field(f.Behaviour.GetType(), "arrivalAction");
            Vector2 originalPosition = f.Motor.CurrentMapPosition;
            Require((bool)Call(f.Behaviour, "TryBeginFlight", f.HomeTree, Enum.Parse(arrival.FieldType, "Perch")), "Current-perch target rejected");
            Require(Vector2.Distance(f.Motor.TargetMapPosition, originalPosition) <= f.Config.ArrivalDistanceMeters, "Stationary-flight fixture targets a different perch");
            Require(f.Count == 0, "Requesting current perch emitted takeoff without movement");
            f.Motor.Tick(1f / 60f); f.Emitter.Tick(1f / 60f); f.Behaviour.TickDaily(1f / 60f);
            Require(f.Motor.HasArrived == false && Phase(f.Behaviour, "dailyPhase") == "Perched", "Current-perch flight did not return to stationary daily state");
            Require(f.Count == 0 && Vector2.Distance(f.Motor.CurrentMapPosition, originalPosition) < .0001f, "Current-perch request emitted landing or moved the bird");
        }

        private static void CheckMuskratMovementCountdown()
        {
            using var f = new Fixture();
            AnimalSoundEmitter original = AssetDatabase.LoadAssetAtPath<GameObject>(MuskratPrefab).GetComponent<AnimalSoundEmitter>();
            AnimalSoundEmitter emitter = f.New("Muskrat cadence compatibility").AddComponent<AnimalSoundEmitter>();
            EditorUtility.CopySerialized(original, emitter); emitter.Initialize(f.Agent);
            Require(f.Motor.SetTarget(new Vector2(80f, 50f), 1f), "Ground cadence fixture target rejected");
            f.Motor.Tick(.01f); emitter.Tick(.01f);
            Require(f.Count == 1, "Muskrat movement lost immediate initial sound");
            float countdown = (float)Get(emitter, "movementSoundCountdown");
            Require(countdown >= 3.2f && countdown <= 4.54f, "Muskrat initial land cadence changed");
            HeightMapLevelAsset water = f.Own(ScriptableObject.CreateInstance<HeightMapLevelAsset>());
            Set(water, "mapWidthMeters", 100f); Set(water, "mapHeightMeters", 100f);
            Set(water, "staticWaterDepthMap", new byte[] { 255, 255, 255, 255 }); Set(f.Map, "levelAsset", water);
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

        private static void CheckCuriousCadence()
        {
            using var f = new Fixture();
            f.Behaviour.ExitDaily(); f.Behaviour.EnterCurious();
            Require(f.Count == 1, "Curious entry did not emit a single observing pulse");
            for (int index = 0; index < 24; index++) f.Behaviour.TickCurious(.1f);
            Require(f.Count == 1, "Curious pulse repeated before 2.5 seconds");
            for (int index = 0; index < 18; index++) f.Behaviour.TickCurious(.1f);
            Require(f.Count == 2, "Curious pulse did not repeat within four seconds");
            Require(f.Motor.CurrentSpeedMetersPerSecond == 0f, "Sound integration made curious bird move");
        }

        private static void CheckPeckCadenceAndContact()
        {
            foreach (int framesPerSecond in new[] { 30, 60, 144 })
            {
                using var f = new Fixture();
                f.BeginPecking();
                for (int frame = 0; frame < framesPerSecond * 2; frame++) f.Behaviour.TickDaily(1f / framesPerSecond);
                Require(f.Count == 3, $"Peck pulse count depends on frame rate ({framesPerSecond} Hz): {f.Count}");
                Vector3 origin = ((Transform)Get(f.Waves[0], "Transform")).position;
                Vector3 centre = f.HomeTree.TrunkWorldPosition;
                Near(Vector2.Distance(origin, centre), f.HomeTree.PerchRadiusMeters, "Peck wave is not anchored at trunk contact surface");
                Vector2 birdSide = (Vector2)(f.Agent.transform.position - centre);
                Require(Vector2.Dot(((Vector2)(origin - centre)).normalized, birdSide.normalized) > .999f, "Peck sound emitted on the far side of the tree");
                Near((float)Get(f.Last, "Duration"), .35f, "Peck pulse duration");
            }
            using (var f = new Fixture())
            {
                f.BeginPecking(); f.Behaviour.TickDaily(.4f);
                Require(f.Count == 0, "Sound emitted before third peck contact");
                f.Behaviour.TickDaily(.011f);
                Require(f.Count == 1, "Third contact did not emit a wave");
                f.ClearWaves(); f.BeginPecking(); f.Behaviour.TickDaily(2f);
                Require(f.Count == 1, "Long frame caught up several sound pulses at once");
            }
        }

        private static void CheckInterruptedPecking()
        {
            using var f = new Fixture();
            f.BeginPecking();
            f.HomeTree.ConfigureEditorDefaults(TreeHealthState.Healthy, null, .6f);
            f.Behaviour.TickDaily(.8f);
            Require(f.Count == 0 && Phase(f.Behaviour, "dailyPhase") != "Pecking", "Invalid peck target continued to emit contact sounds");
            f.HomeTree.ConfigureEditorDefaults(TreeHealthState.Dead, null, .6f);
            f.BeginPecking(); f.Behaviour.TickDaily(.3f);
            f.Behaviour.ExitDaily(); f.Behaviour.EnterCurious();
            Require(f.Count == 1, "Interrupted peck leaked a delayed contact pulse into curious entry");
        }

        private static void CheckFleeAndTreeEntry()
        {
            using var f = new Fixture();
            f.Motor.TeleportToMapPosition(new Vector2(70f, 50f));
            f.Behaviour.ExitDaily(); Property(f.Agent, "CurrentState", AnimalState.Fleeing); f.Behaviour.EnterFleeing();
            Require(f.Count == 1, "Fleeing entry did not emit one alarm wave");
            f.Motor.Tick(.01f); f.Emitter.Tick(.01f);
            Require(f.Count == 1, "Alarm stacked immediate wing pulse");
            for (int index = 0; index < 4; index++) { f.Motor.Tick(.1f); f.Emitter.Tick(.1f); }
            Require(f.Count == 1, "Flee movement repeated before .5 seconds");
            for (int index = 0; index < 5; index++) { f.Motor.Tick(.1f); f.Emitter.Tick(.1f); }
            Require(f.Count == 2, "Flee movement did not repeat within .8 seconds");
            f.Motor.TeleportToMapPosition(f.Motor.TargetMapPosition); Property(f.Motor, "HasArrived", true);
            f.Behaviour.TickFleeing(.01f);
            Require(f.Count == 3 && Phase(f.Behaviour, "fleePhase") == "EnteringHome", "Arrival did not emit exactly one entrance pulse");
            Vector3 entrance = ((Transform)Get(f.Last, "Transform")).position;
            f.Behaviour.TickFleeing(.2f); f.Emitter.Tick(.2f);
            Require(f.Count == 3, "Moving into tree emitted additional movement pulses");
            Require(Vector3.Distance(((Transform)Get(f.Last, "Transform")).position, entrance) < .0001f, "Entrance pulse followed the hiding bird");
            f.Behaviour.TickFleeing(.3f);
            Require(f.Agent.CurrentState == AnimalState.Hiding && f.Count == 3, "Tree entry failed to finish quietly in hiding");
        }

        private static void CheckHiddenAndEmergenceRetries()
        {
            using var f = new Fixture();
            Set(f.Config, "frightenedHideDurationSeconds", new Vector2(2f, 2f));
            f.Agent.BeginHiding();
            for (int index = 0; index < 15; index++) { f.Behaviour.TickHiding(.1f); f.Emitter.Tick(.1f); }
            Require(f.Count == 0 && Phase(f.Behaviour, "hidingPhase") == "Hidden", "Hidden bird emitted visible sound waves");
            for (int index = 0; index < 6; index++) f.Behaviour.TickHiding(.1f);
            Require(f.Count == 1 && Phase(f.Behaviour, "hidingPhase") == "Emerging", "Safe emergence did not begin with one pulse");
            Vector3 entrance = ((Transform)Get(f.Last, "Transform")).position;
            Require(Vector3.Distance(entrance, f.Map.MapPositionToWorld((Vector2)Get(f.Behaviour, "hidingPerchMapPosition"))) < .0001f, "Emergence sound is not at tree entrance");
            RobotMover player = f.New("Emergence safety player").AddComponent<RobotMover>();
            Property(f.Agent.Perception, "Player", player);
            player.transform.position = entrance;
            f.Behaviour.TickHiding(.01f);
            Require(Phase(f.Behaviour, "hidingPhase") == "Hidden" && f.Count == 1, "Unsafe emergence failed to cancel quietly");
            Property(f.Agent.Perception, "Player", null);
            Set(f.Behaviour, "hidingSafetyCheckCountdown", 0f);
            f.Behaviour.TickHiding(.1f);
            Require(Phase(f.Behaviour, "hidingPhase") == "Emerging" && f.Count == 1, "Rapid retry stacked another emergence pulse");
            Property(f.Agent.Perception, "Player", player); f.Behaviour.TickHiding(.01f);
            for (int index = 0; index < 22; index++) f.Behaviour.TickHiding(.1f);
            Require(f.Count == 1, "Unsafe hidden safety checks emitted sounds");
            Property(f.Agent.Perception, "Player", null);
            Set(f.Behaviour, "hidingSafetyCheckCountdown", 0f); f.Behaviour.TickHiding(.1f);
            Require(f.Count == 2, "A genuine later emergence lost its pulse after retry cooldown");
            f.Behaviour.TickHiding(.5f);
            Require(f.Agent.CurrentState == AnimalState.Daily, "Emergence failed to restore daily behavior");
        }

        private static void CheckSimulationPause()
        {
            using var f = new Fixture();
            f.BeginPecking(); Set(f.Behaviour, "actionTimer", 0f);
            f.Emitter.Emit(AnimalSoundKind.Pecking);
            object wave = f.Last; Set(wave, "Elapsed", .1f);
            float peckElapsed = (float)Get(f.Behaviour, "peckElapsed");
            using (AnimalSimulation.AcquirePause())
            {
                Call(f.Agent, "Update"); Call(f.Manager, "Update");
                Near((float)Get(f.Behaviour, "peckElapsed"), peckElapsed, "Paused animal advanced peck motion");
                Near((float)Get(wave, "Elapsed"), .1f, "Paused wave advanced lifetime");
                Require(Phase(f.Behaviour, "dailyPhase") == "Pecking" && f.Count == 1, "Paused animal changed phase or emitted another sound");
            }
            Require(!AnimalSimulation.IsPaused, "Regression pause handle was not released");
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
                Set(Map, "heightField", BakedHeightField.Bake(texture, 64, Vector2.one * 100f, 0f, 1f, false, 0f));
                Set(Map, "mapRenderer", renderer); Set(Map, "mapWidthMeters", 100f); Set(Map, "mapHeightMeters", 100f);
                Require(Map.TrySampleMapPosition(Vector2.one * 50f, out _), "Flat fixture map is not playable");
                HomeTree = Tree(new Vector2(50f, 50f)); OtherTree = Tree(new Vector2(70f, 50f));
                AnimalSpeciesConfig shippedConfig = AssetDatabase.LoadAssetAtPath<AnimalSpeciesConfig>(WoodConfig);
                Require(shippedConfig != null, "Missing woodpecker species configuration");
                Config = Own(Object.Instantiate(shippedConfig));
                Set(Config, "dailyBehaviours", new List<AnimalDailyBehaviourSettings> { new AnimalDailyBehaviourSettings(AnimalDailyBehaviourKind.PerchAtTree, 1f, 1000f, 1000f) });
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
                Set(Behaviour, "currentTree", HomeTree); Set(Behaviour, "actionTimer", 1000f);
                Call(Behaviour, "BeginPecking"); Motor.Tick(1f);
            }
            public void ClearWaves()
            {
                foreach (object wave in Waves) Object.DestroyImmediate((GameObject)Get(wave, "GameObject"));
                Waves.Clear();
            }
            public void Dispose()
            {
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
