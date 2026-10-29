using System;
using AnimalGame.Animals;
using AnimalGame.Discovery;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    // Explicit menu only: opening the editor never overwrites authored assets.
    public static class SpecificAnimalAssetBuilder
    {
        private const string Data = "Assets/Data/Animals/";
        private const string Actions = Data + "SpeciesActions/";
        private const string Prefabs = "Assets/Prefabs/Animals/";
        public const string BurrowPath = "Assets/Prefabs/Environment/Vegetation/MarmotBurrow.prefab";
        private readonly struct Daily
        {
            public readonly string Name;
            public readonly AnimalActionDefinition Definition;
            public readonly Vector2 Duration;
            public readonly float Weight, Cooldown;
            public Daily(string name, AnimalActionDefinition definition, float min, float max, float weight = 1f, float cooldown = 3f)
            { Name = name; Definition = definition; Duration = new Vector2(min, max); Weight = weight; Cooldown = cooldown; }
        }
        [MenuItem("Animal Game/Animals/Build Specific Animal Prototypes")]
        public static void Build()
        {
            AnimalBurrow burrow = BuildBurrow();
            BuildSpecies<AmericanBisonBehaviour>(AnimalSpecies.AmericanBison, 20f, .36f, 1.2f, 4f, true, burrow,
                Terrain("BisonGraze", TerrainAnimalActionDefinition.ActionKind.Graze, 12, 20),
                Specific("BisonWander", SpecificAnimalActionDefinition.ActionKind.SlowWander, 6, 10),
                Terrain("BisonLook", TerrainAnimalActionDefinition.ActionKind.LookAround, 4, 8),
                Terrain("BisonSpar", TerrainAnimalActionDefinition.ActionKind.Spar, 10, 15, .35f, 15));
            BuildSpecies<PronghornBehaviour>(AnimalSpecies.Pronghorn, 10f, .22f, 1.4f, 5f, true, burrow,
                Specific("PronghornEat", SpecificAnimalActionDefinition.ActionKind.BushOrGrassEating, 10, 16),
                Terrain("PronghornLook", TerrainAnimalActionDefinition.ActionKind.LookAround, 4, 8),
                Specific("PronghornRest", SpecificAnimalActionDefinition.ActionKind.RestOnGrass, 25, 40),
                Specific("PronghornWander", SpecificAnimalActionDefinition.ActionKind.SlowWander, 6, 10),
                Terrain("PronghornSpar", TerrainAnimalActionDefinition.ActionKind.Spar, 8, 12, .35f, 15));
            BuildSpecies<PrairieGrouseBehaviour>(AnimalSpecies.GreaterPrairieChicken, 3f, .12f, .9f, 4f, false, burrow,
                Specific("GrouseForage", SpecificAnimalActionDefinition.ActionKind.ZigzagForaging, 10, 16),
                Specific("GrouseWander", SpecificAnimalActionDefinition.ActionKind.SlowWander, 4, 7),
                Terrain("GrouseLook", TerrainAnimalActionDefinition.ActionKind.LookAround, 4, 8, 1f, 3f, 720f),
                Specific("GrouseShortFlight", SpecificAnimalActionDefinition.ActionKind.ShortFlight, 1, 2, .4f, 12));
            BuildSpecies<AmericanMarmotBehaviour>(AnimalSpecies.Groundhog, 3f, .14f, .8f, 2.8f, false, burrow,
                Terrain("MarmotGraze", TerrainAnimalActionDefinition.ActionKind.Graze, 8, 14),
                Specific("MarmotWander", SpecificAnimalActionDefinition.ActionKind.SlowWander, 4, 8),
                Terrain("MarmotLook", TerrainAnimalActionDefinition.ActionKind.LookAround, 4, 8, 1f, 3f, 720f),
                Specific("MarmotHide", SpecificAnimalActionDefinition.ActionKind.HideInBurrow, 10, 18),
                Specific("MarmotAtBurrow", SpecificAnimalActionDefinition.ActionKind.RestAtBurrow, 6, 12));
            BuildSpecies<NorthAmericanPorcupineBehaviour>(AnimalSpecies.NorthAmericanPorcupine, 5f, .18f, .75f, 2f, false, burrow,
                Specific("PorcupineTreeEat", SpecificAnimalActionDefinition.ActionKind.TreeEating, 15, 25),
                Specific("PorcupineBarkEat", SpecificAnimalActionDefinition.ActionKind.BarkEating, 12, 20),
                Specific("PorcupineChangeTree", SpecificAnimalActionDefinition.ActionKind.ChangeTree, 1, 2, .5f, 12),
                Specific("PorcupineTreeLook", SpecificAnimalActionDefinition.ActionKind.TreeLooking, 6, 10),
                Specific("PorcupineSniff", SpecificAnimalActionDefinition.ActionKind.GroundSniff, 8, 14));
            AssetDatabase.SaveAssets();
            Debug.Log("Specific animal assets built: five species, seven animal prefabs and invisible burrow.");
        }
        private static Daily Terrain(string name, TerrainAnimalActionDefinition.ActionKind kind, float min, float max,
            float weight = 1f, float cooldown = 3f, float turnSpeed = 35f)
        {
            var asset = LoadOrCreate<TerrainAnimalActionDefinition>(Actions + name + ".asset");
            var s = new SerializedObject(asset); s.FindProperty("action").enumValueIndex = (int)kind;
            s.FindProperty("durationSeconds").floatValue = (min + max) * .5f;
            s.FindProperty("lookTurnSpeedDegreesPerSecond").floatValue = turnSpeed;
            s.ApplyModifiedPropertiesWithoutUndo(); return new Daily(name, asset, min, max, weight, cooldown);
        }
        private static Daily Specific(string name, SpecificAnimalActionDefinition.ActionKind kind, float min, float max, float weight = 1f, float cooldown = 3f)
        {
            var asset = LoadOrCreate<SpecificAnimalActionDefinition>(Actions + name + ".asset");
            var s = new SerializedObject(asset); s.FindProperty("action").enumValueIndex = (int)kind;
            s.FindProperty("durationSeconds").floatValue = (min + max) * .5f;
            s.ApplyModifiedPropertiesWithoutUndo(); return new Daily(name, asset, min, max, weight, cooldown);
        }
        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) { asset = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset, path); }
            return asset;
        }
        private static AnimalBurrow BuildBurrow()
        {
            var root = new GameObject("Marmot Burrow");
            root.AddComponent<AnimalBurrow>();
            try { return PrefabUtility.SaveAsPrefabAsset(root, BurrowPath).GetComponent<AnimalBurrow>(); }
            finally { Object.DestroyImmediate(root); }
        }
        private static void BuildSpecies<T>(AnimalSpecies species,
            float health, float radius, float walk, float flee, bool juvenile, AnimalBurrow burrow, params Daily[] daily) where T : GenericAnimalBehaviour
        {
            string id = species.ToString();
            var config = LoadOrCreate<AnimalSpeciesConfig>(Data + id + "Config.asset");
            var s = new SerializedObject(config);
            s.FindProperty("speciesName").stringValue = id;
            s.FindProperty("maximumHealth").floatValue = health;
            s.FindProperty("initialHealthRange").vector2Value = Vector2.one * health;
            s.FindProperty("maximumWalkSlopeDegrees").floatValue = 38f;
            s.FindProperty("slideSlopeDegrees").floatValue = 38f;
            s.FindProperty("maximumWalkWaterDepthMeters").floatValue = .01f;
            s.FindProperty("bodyRadiusMeters").floatValue = radius;
            s.FindProperty("arrivalDistanceMeters").floatValue = .08f;
            s.FindProperty("activityRadiusMeters").floatValue = typeof(T) == typeof(AmericanMarmotBehaviour) ? 8f : 20f;
            s.FindProperty("dailyMoveSpeedMetersPerSecond").floatValue = walk;
            s.FindProperty("fleeSpeedMetersPerSecond").floatValue = flee;
            s.FindProperty("alertRadiusMeters").floatValue = 10f;
            s.FindProperty("aggressiveRadiusMeters").floatValue = 10f;
            s.FindProperty("attackDamage").floatValue = species == AnimalSpecies.AmericanBison ? 3f : 0f;
            s.FindProperty("aggressiveSpeedMetersPerSecond").floatValue = flee;
            s.FindProperty("baseAggressionChancePerCheck").floatValue = species == AnimalSpecies.AmericanBison || species == AnimalSpecies.NorthAmericanPorcupine ? .035f : 0f;
            s.FindProperty("corpseNutrition").floatValue = health;
            bool quickLook = species == AnimalSpecies.GreaterPrairieChicken || species == AnimalSpecies.Groundhog;
            s.FindProperty("lookIntervalSeconds").vector2Value = quickLook ? new Vector2(.5f, 2f) : new Vector2(1f, 2f);
            var foods = s.FindProperty("foodPreferences"); foods.arraySize = species == AnimalSpecies.Pronghorn ? 1 : 0;
            if (foods.arraySize > 0) { foods.GetArrayElementAtIndex(0).FindPropertyRelative("foodType").enumValueIndex = 0; foods.GetArrayElementAtIndex(0).FindPropertyRelative("selectionWeight").floatValue = 1f; }
            var entries = s.FindProperty("dailyBehaviours"); entries.arraySize = daily.Length;
            for (int i = 0; i < daily.Length; i++)
            {
                var entry = entries.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("behaviour").enumValueIndex = (int)AnimalDailyBehaviourKind.Idle;
                entry.FindPropertyRelative("action").objectReferenceValue = daily[i].Definition;
                entry.FindPropertyRelative("durationSeconds").vector2Value = daily[i].Duration;
                entry.FindPropertyRelative("selectionWeight").floatValue = daily[i].Weight;
                entry.FindPropertyRelative("cooldownSeconds").floatValue = daily[i].Cooldown;
                entry.FindPropertyRelative("seasonMultiplier").floatValue = entry.FindPropertyRelative("timeOfDayMultiplier").floatValue = 1f;
                entry.FindPropertyRelative("seasonalWeights").vector4Value = entry.FindPropertyRelative("timeOfDayWeights").vector4Value = Vector4.one;
            }
            s.ApplyModifiedPropertiesWithoutUndo();
            if (!AssetDatabase.IsValidFolder(Prefabs + id)) AssetDatabase.CreateFolder("Assets/Prefabs/Animals", id);
            BuildPrefab<T>(config, species, false, burrow);
            if (juvenile) BuildPrefab<T>(config, species, true, burrow);
        }
        private static void BuildPrefab<T>(AnimalSpeciesConfig config, AnimalSpecies species, bool juvenile, AnimalBurrow burrow) where T : GenericAnimalBehaviour
        {
            // Reuse the established placeholder artwork, discovery field and photo components.
            var template = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "MountainGoat/MountainGoat_Placeholder.prefab");
            var root = Object.Instantiate(template);
            try
            {
                root.name = species + (juvenile ? " Juvenile" : "") + " Placeholder";
                root.transform.position = Vector3.zero;
                Object.DestroyImmediate(root.GetComponent<AnimalBehaviourSet>());
                var behaviour = root.AddComponent<T>();
                var bs = new SerializedObject(behaviour);
                bs.FindProperty("juvenileIndividual").boolValue = juvenile;
                if (behaviour is AmericanMarmotBehaviour) bs.FindProperty("burrowPrefab").objectReferenceValue = burrow;
                bs.ApplyModifiedPropertiesWithoutUndo();
                var agent = root.GetComponent<AnimalAgent>();
                agent.ConfigureEditorDefaults(config, behaviour, root.GetComponent<AnimalPlaceholderView>());
                var ag = new SerializedObject(agent); ag.FindProperty("groupId").stringValue = ""; ag.ApplyModifiedPropertiesWithoutUndo();
                var placement = new SerializedObject(root.GetComponent<HeightMapPlacedObject>());
                placement.FindProperty("map").objectReferenceValue = null;
                placement.FindProperty("mapPositionMeters").vector2Value = Vector2.zero;
                placement.FindProperty("sampledSurfaceHeightMeters").floatValue = 0f;
                placement.FindProperty("footprintRadiusMeters").floatValue = config.BodyRadiusMeters;
                placement.ApplyModifiedPropertiesWithoutUndo();
                var visualRoot = root.transform.Find("Visual Root");
                visualRoot.localScale *= config.BodyRadiusMeters / .22f;
                if (behaviour is NorthAmericanPorcupineBehaviour)
                {
                    var quills = new GameObject("Quills"); quills.transform.SetParent(visualRoot, false);
                    var indicator = visualRoot.Find("Direction Indicator").GetComponent<SpriteRenderer>();
                    var discoverySettings = new SerializedObject(root.GetComponent<AnimalDiscoveryVisual>());
                    var known = discoverySettings.FindProperty("knownRenderers");
                    int firstQuill = known.arraySize; known.arraySize += 12;
                    for (int i = 0; i < 12; i++)
                    {
                        float angle = i * 30f;
                        var tip = new GameObject("Quill " + i); tip.transform.SetParent(quills.transform, false);
                        tip.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
                        tip.transform.localPosition = tip.transform.up * .12f;
                        tip.transform.localScale = Vector3.one * .065f;
                        var renderer = tip.AddComponent<SpriteRenderer>(); renderer.sprite = indicator.sprite;
                        renderer.sharedMaterial = indicator.sharedMaterial; renderer.sortingOrder = indicator.sortingOrder;
                        renderer.color = new Color(212f / 255f, 184f / 255f, 84f / 255f);
                        known.GetArrayElementAtIndex(firstQuill + i).objectReferenceValue = renderer;
                    }
                    discoverySettings.ApplyModifiedPropertiesWithoutUndo();
                    bs.Update(); bs.FindProperty("quillVisual").objectReferenceValue = quills; bs.ApplyModifiedPropertiesWithoutUndo();
                    quills.SetActive(false);
                }
                var discovery = root.GetComponent<DiscoverableEntity>();
                discovery.ConfigureEditorDefaults(DiscoverableKind.Animal, species.ToString(), false);
                var photo = new SerializedObject(root.GetComponent<AnimalPhotoSubject>());
                photo.FindProperty("species").enumValueIndex = (int)species;
                photo.ApplyModifiedPropertiesWithoutUndo();
                var sound = new SerializedObject(root.GetComponent<AnimalSoundEmitter>());
                foreach (string field in new[] { "flyingSound", "takeoffSound", "landingSound", "peckingSound" })
                    sound.FindProperty(field).FindPropertyRelative("enabled").boolValue = true;
                sound.ApplyModifiedPropertiesWithoutUndo();
                string path = Prefabs + species + "/" + species + (juvenile ? "_Juvenile" : "") + "_Placeholder.prefab";
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
