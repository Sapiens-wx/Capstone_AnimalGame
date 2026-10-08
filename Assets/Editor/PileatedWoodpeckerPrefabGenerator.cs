using AnimalGame.Animals;
using AnimalGame.Discovery;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEngine;

namespace AnimalGame.Editor
{
    /// <summary>
    /// Owns the generated placeholder assets for the pileated woodpecker.
    /// This intentionally remains separate from the muskrat generator so
    /// rebuilding one prototype cannot rewrite the other.
    /// </summary>
    [InitializeOnLoad]
    public static class PileatedWoodpeckerPrefabGenerator
    {
        private const string DataFolder = "Assets/Data/Animals";
        private const string AnimalPrefabFolder = "Assets/Prefabs/Animals";
        private const string WoodpeckerPrefabFolder =
            AnimalPrefabFolder + "/PileatedWoodpecker";
        private const string ConfigPath =
            DataFolder + "/PileatedWoodpeckerConfig.asset";
        private const string PrefabPath =
            WoodpeckerPrefabFolder
            + "/PileatedWoodpecker_Placeholder.prefab";

        private const string BodySpritePath =
            "Assets/Arts/robot_body_new.png";
        private const string BodyFillSpritePath =
            "Assets/Arts/robot_body_fill.png";
        private const string IndicatorSpritePath =
            "Assets/Arts/indicator_new.png";
        private const string UnknownMaterialPath =
            "Assets/Materials/Animals/UnknownAnimalStatic.mat";
        private const string UnknownMaskPath =
            "Assets/Materials/Animals/UnknownAnimalStaticMask.asset";
        private const string SoundWaveShaderPath =
            "Assets/Shaders/AnimalSoundWave.shader";

        private const float VisibleBodyDiameter = 0.24f;
        private const float BodyArtworkVisibleDiameterPixels = 72.5f;
        private const float BodyFillVisibleDiameterPixels = 82.5f;
        private const float IndicatorScale = 0.55f;
        private const float UnknownFieldScaleFromBodyFill = 2.4f;
        private const int AnimalSortingOrder = 1150;

        private static readonly string[] SoundSettingsPropertyNames =
        {
            "idleSound",
            "lookingSound",
            "curiousSound",
            "eatingSound",
            "landMovementSound",
            "waterMovementSound",
            "fleeingSound",
            "submergingSound",
            "surfacingSound",
            "flyingSound",
            "peckingSound",
            "takeoffSound",
            "landingSound",
            "enteringTreeSound",
            "emergingFromTreeSound"
        };

        static PileatedWoodpeckerPrefabGenerator()
        {
            EditorApplication.delayCall += EnsurePrototypeAssets;
        }

        [MenuItem(
            "Animal Game/Animals/Rebuild Pileated Woodpecker Prototype")]
        public static void RebuildPileatedWoodpeckerPrototype()
        {
            EnsureFolders();
            AnimalSpeciesConfig config = EnsureConfig();
            CreatePrefab(config, true);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "Rebuilt the pileated woodpecker placeholder and species "
                + "configuration. Tree selection uses 40% healthy and "
                + "60% dead trees. Sound waves follow perching, flight, "
                + "pecking, curiosity, fleeing, and tree-entry/exit actions.");
        }

        private static void EnsurePrototypeAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            EnsureFolders();
            AnimalSpeciesConfig config = EnsureConfig();
            CreatePrefab(config, false);
            AssetDatabase.SaveAssets();
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets", "Data");
            EnsureFolder("Assets/Data", "Animals");
            EnsureFolder("Assets/Prefabs", "Animals");
            EnsureFolder(AnimalPrefabFolder, "PileatedWoodpecker");
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }

        private static AnimalSpeciesConfig EnsureConfig()
        {
            AnimalSpeciesConfig existing =
                AssetDatabase.LoadAssetAtPath<AnimalSpeciesConfig>(
                    ConfigPath);
            if (existing != null)
                return existing;

            AnimalSpeciesConfig config =
                ScriptableObject.CreateInstance<AnimalSpeciesConfig>();
            config.name = "Pileated Woodpecker Config";
            ConfigureNewConfig(config);
            AssetDatabase.CreateAsset(config, ConfigPath);
            return config;
        }

        private static void ConfigureNewConfig(AnimalSpeciesConfig config)
        {
            var serialized = new SerializedObject(config);
            serialized.FindProperty("speciesName").stringValue =
                "Pileated Woodpecker";

            serialized.FindProperty("activityRadiusMeters").floatValue = 45f;
            serialized.FindProperty("dailyMoveSpeedMetersPerSecond")
                .floatValue = 9f;
            serialized.FindProperty("fleeSpeedMetersPerSecond")
                .floatValue = 14f;
            serialized.FindProperty("turnSpeedDegreesPerSecond")
                .floatValue = 540f;
            serialized.FindProperty("arrivalDistanceMeters").floatValue =
                0.12f;
            serialized.FindProperty("bodyRadiusMeters").floatValue =
                VisibleBodyDiameter * 0.5f;
            serialized.FindProperty("maximumTravelTimeSeconds")
                .floatValue = 20f;

            SerializedProperty dailyBehaviours =
                serialized.FindProperty("dailyBehaviours");
            dailyBehaviours.arraySize = 3;
            ConfigureDailyBehaviour(
                dailyBehaviours.GetArrayElementAtIndex(0),
                AnimalDailyBehaviourKind.PerchAtTree,
                1.15f,
                new Vector2(4f, 8f));
            ConfigureDailyBehaviour(
                dailyBehaviours.GetArrayElementAtIndex(1),
                AnimalDailyBehaviourKind.FlyToTree,
                1f,
                new Vector2(0.5f, 1f));
            ConfigureDailyBehaviour(
                dailyBehaviours.GetArrayElementAtIndex(2),
                AnimalDailyBehaviourKind.PeckAtTree,
                1.35f,
                new Vector2(2.5f, 5f));

            serialized.FindProperty("foodPreferences").arraySize = 0;
            serialized.FindProperty("alertRadiusMeters").floatValue = 18f;
            serialized.FindProperty("detectionIntervalSeconds")
                .floatValue = 0.5f;
            serialized.FindProperty("baseDetectionChancePerCheck")
                .floatValue = 0.12f;
            serialized.FindProperty("nearestDetectionMultiplier")
                .floatValue = 3f;
            serialized.FindProperty("playerSpeedForMaximumBonus")
                .floatValue = 3f;
            serialized.FindProperty(
                    "maximumPlayerSpeedDetectionMultiplier")
                .floatValue = 2f;
            serialized.FindProperty("directVisionAngleDegrees")
                .floatValue = 160f;
            serialized.FindProperty(
                    "directLineOfSightDetectionMultiplier")
                .floatValue = 1.5f;

            serialized.FindProperty("reactionIntervalSeconds")
                .floatValue = 0.2f;
            serialized.FindProperty("baseFleeChancePerCheck")
                .floatValue = 0.15f;
            serialized.FindProperty("nearestFleeMultiplier")
                .floatValue = 3f;
            serialized.FindProperty("baseAggressionChancePerCheck")
                .floatValue = 0f;
            serialized.FindProperty("nearestAggressionMultiplier")
                .floatValue = 1f;
            serialized.FindProperty("curiousLostPlayerDelaySeconds")
                .floatValue = 3f;
            serialized.FindProperty("frightenedHideDurationSeconds")
                .vector2Value = new Vector2(8f, 14f);
            serialized.FindProperty("hideSafetyCheckIntervalSeconds")
                .floatValue = 1f;
            serialized.FindProperty("reappearSafeDistanceMultiplier")
                .floatValue = 1.05f;
            serialized.FindProperty("postReappearGraceDurationSeconds")
                .floatValue = 1.5f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureDailyBehaviour(
            SerializedProperty property,
            AnimalDailyBehaviourKind behaviour,
            float weight,
            Vector2 duration)
        {
            property.FindPropertyRelative("behaviour").enumValueIndex =
                (int)behaviour;
            property.FindPropertyRelative("selectionWeight").floatValue =
                weight;
            property.FindPropertyRelative("durationSeconds").vector2Value =
                duration;
        }

        private static void CreatePrefab(
            AnimalSpeciesConfig config,
            bool overwrite)
        {
            if (!overwrite
                && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)
                != null)
            {
                return;
            }

            Sprite bodySprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                BodySpritePath);
            Sprite bodyFillSprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                BodyFillSpritePath);
            Sprite indicatorSprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                IndicatorSpritePath);
            Sprite unknownMask = LoadSpriteSubAsset(UnknownMaskPath);
            Material unknownMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(
                    UnknownMaterialPath);
            if (bodySprite == null
                || bodyFillSprite == null
                || indicatorSprite == null
                || unknownMask == null
                || unknownMaterial == null)
            {
                Debug.LogWarning(
                    "Cannot create the pileated woodpecker placeholder: "
                    + "robot placeholder art or the shared unknown-animal "
                    + "material/mask is missing.");
                return;
            }

            var root = new GameObject("Pileated Woodpecker Placeholder");
            try
            {
                HeightMapPlacedObject placedObject =
                    root.AddComponent<HeightMapPlacedObject>();
                ConfigurePlacedObject(placedObject, config);

                root.AddComponent<AnimalMotor>();
                root.AddComponent<AnimalPerception>();
                AnimalPlaceholderView view =
                    root.AddComponent<AnimalPlaceholderView>();
                PileatedWoodpeckerBehaviour behaviour =
                    root.AddComponent<PileatedWoodpeckerBehaviour>();
                AnimalAgent agent = root.AddComponent<AnimalAgent>();
                AnimalSoundEmitter soundEmitter =
                    root.GetComponent<AnimalSoundEmitter>();

                var visualRootObject = new GameObject("Visual Root");
                visualRootObject.transform.SetParent(root.transform, false);

                SpriteRenderer fillRenderer = CreateRenderer(
                    visualRootObject.transform,
                    "Body Fill",
                    bodyFillSprite,
                    Color.black,
                    AnimalSortingOrder,
                    CalculateArtworkScale(
                        bodyFillSprite,
                        BodyFillVisibleDiameterPixels,
                        VisibleBodyDiameter));
                SpriteRenderer outlineRenderer = CreateRenderer(
                    visualRootObject.transform,
                    "Body Outline",
                    bodySprite,
                    new Color(0.92f, 0.98f, 1f, 1f),
                    AnimalSortingOrder + 1,
                    CalculateArtworkScale(
                        bodySprite,
                        BodyArtworkVisibleDiameterPixels,
                        VisibleBodyDiameter));
                SpriteRenderer indicatorRenderer = CreateRenderer(
                    visualRootObject.transform,
                    "Direction Indicator",
                    indicatorSprite,
                    new Color(0.92f, 0.98f, 1f, 1f),
                    AnimalSortingOrder + 2,
                    IndicatorScale);

                DiscoverableEntity discoverable =
                    root.AddComponent<DiscoverableEntity>();
                discoverable.ConfigureEditorDefaults(
                    DiscoverableKind.Animal,
                    "pileated_woodpecker",
                    false);

                SpriteRenderer unknownRenderer = CreateRenderer(
                    visualRootObject.transform,
                    "Unknown Animal Static",
                    unknownMask,
                    Color.white,
                    AnimalSortingOrder + 3,
                    fillRenderer.transform.localScale.x
                    * UnknownFieldScaleFromBodyFill);
                unknownRenderer.sortingLayerID =
                    outlineRenderer.sortingLayerID;
                unknownRenderer.sharedMaterial = unknownMaterial;

                AnimalDiscoveryVisual discoveryVisual =
                    root.AddComponent<AnimalDiscoveryVisual>();
                discoveryVisual.ConfigureEditorReferences(
                    discoverable,
                    new[]
                    {
                        fillRenderer,
                        outlineRenderer,
                        indicatorRenderer
                    },
                    unknownRenderer,
                    0.4f);
                ConfigureUnknownPositionRefresh(discoveryVisual);

                view.ConfigureEditorReferences(
                    visualRootObject.transform,
                    fillRenderer,
                    outlineRenderer,
                    indicatorRenderer);
                view.ConfigureDiscoveryVisual(discoveryVisual);
                behaviour.ConfigureEditorDefaults(
                    null,
                    visualRootObject.transform);
                agent.ConfigureEditorDefaults(config, behaviour, view);
                ConfigureAgentSoundReference(agent, soundEmitter);
                ConfigureAnimalSounds(soundEmitter);

                // The placeholder is an actor rather than a map obstacle.
                // Its flight must not add an obstacle footprint or collider.
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void ConfigurePlacedObject(
            HeightMapPlacedObject placedObject,
            AnimalSpeciesConfig config)
        {
            var serialized = new SerializedObject(placedObject);
            serialized.FindProperty("map").objectReferenceValue = null;
            serialized.FindProperty("mapPositionMeters").vector2Value =
                Vector2.zero;
            serialized.FindProperty("sampledSurfaceHeightMeters")
                .floatValue = 0f;
            serialized.FindProperty("footprintRadiusMeters").floatValue =
                config != null
                    ? config.BodyRadiusMeters
                    : VisibleBodyDiameter * 0.5f;
            serialized.FindProperty("clampInsideMap").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureUnknownPositionRefresh(
            AnimalDiscoveryVisual discoveryVisual)
        {
            var serialized = new SerializedObject(discoveryVisual);
            serialized.FindProperty("unknownPositionUpdateInterval")
                .floatValue = 2f;
            serialized.FindProperty(
                    "unknownPositionRefreshAnimationDuration")
                .floatValue = 0.2f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureAgentSoundReference(
            AnimalAgent agent,
            AnimalSoundEmitter soundEmitter)
        {
            var serialized = new SerializedObject(agent);
            serialized.FindProperty("soundEmitter").objectReferenceValue =
                soundEmitter;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureAnimalSounds(
            AnimalSoundEmitter soundEmitter)
        {
            if (soundEmitter == null)
                return;

            var serialized = new SerializedObject(soundEmitter);
            for (int index = 0;
                 index < SoundSettingsPropertyNames.Length;
                 index++)
            {
                SerializedProperty settings = serialized.FindProperty(
                    SoundSettingsPropertyNames[index]);
                SerializedProperty enabled = settings != null
                    ? settings.FindPropertyRelative("enabled")
                    : null;
                if (enabled != null)
                    enabled.boolValue = false;
            }

            serialized.FindProperty("soundWaveShader")
                .objectReferenceValue = AssetDatabase.LoadAssetAtPath<Shader>(
                    SoundWaveShaderPath);
            serialized.FindProperty("overallRadiusMultiplier")
                .floatValue = 0.5f;
            serialized.FindProperty("overallRingCountMultiplier")
                .floatValue = 0.5f;
            // The profile intervals are the intended visible cadence.
            serialized.FindProperty("overallEmissionFrequencyMultiplier")
                .floatValue = 1f;

            ConfigureSoundProfile(
                serialized, "idleSound", 0.28f, 0.5f, 3, 4f, 6f, 0.55f);
            ConfigureSoundProfile(
                serialized, "curiousSound", 0.58f, 0.6f, 5,
                2.5f, 4f, 0.72f);
            ConfigureSoundProfile(
                serialized, "fleeingSound", 1f, 0.55f, 5,
                0.5f, 0.8f, 0.84f);
            ConfigureSoundProfile(
                serialized, "flyingSound", 0.65f, 0.45f, 4,
                1.2f, 1.8f, 0.68f);
            ConfigureSoundProfile(
                serialized, "peckingSound", 0.52f, 0.35f, 3,
                0.55f, 0.55f, 0.76f);
            ConfigureSoundProfile(
                serialized, "takeoffSound", 0.75f, 0.4f, 4,
                1f, 1f, 0.76f);
            ConfigureSoundProfile(
                serialized, "landingSound", 0.6f, 0.4f, 4,
                1f, 1f, 0.7f);
            ConfigureSoundProfile(
                serialized, "enteringTreeSound", 0.5f, 0.4f, 3,
                1f, 1f, 0.7f);
            ConfigureSoundProfile(
                serialized, "emergingFromTreeSound", 0.45f, 0.45f, 3,
                1f, 1f, 0.65f);

            SerializedProperty gizmos = serialized.FindProperty(
                "showSoundRangeGizmos");
            if (gizmos != null)
                gizmos.boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureSoundProfile(
            SerializedObject serialized,
            string propertyName,
            float radius,
            float duration,
            int ringCount,
            float minimumInterval,
            float maximumInterval,
            float opacity)
        {
            SerializedProperty settings = serialized.FindProperty(
                propertyName);
            settings.FindPropertyRelative("enabled").boolValue = true;
            settings.FindPropertyRelative("maximumRadiusMeters")
                .floatValue = radius;
            settings.FindPropertyRelative("durationSeconds")
                .floatValue = duration;
            settings.FindPropertyRelative("ringCount")
                .intValue = ringCount;
            settings.FindPropertyRelative("repeatIntervalSeconds")
                .vector2Value = new Vector2(minimumInterval, maximumInterval);
            settings.FindPropertyRelative("opacity").floatValue = opacity;
        }

        private static SpriteRenderer CreateRenderer(
            Transform parent,
            string objectName,
            Sprite sprite,
            Color colour,
            int sortingOrder,
            float localScale)
        {
            var child = new GameObject(objectName);
            child.transform.SetParent(parent, false);
            child.transform.localScale = Vector3.one * localScale;
            SpriteRenderer renderer = child.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = colour;
            renderer.sortingOrder = sortingOrder;
            renderer.sharedMaterial =
                AssetDatabase.GetBuiltinExtraResource<Material>(
                    "Sprites-Default.mat");
            return renderer;
        }

        private static Sprite LoadSpriteSubAsset(string assetPath)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            for (int index = 0; index < assets.Length; index++)
            {
                if (assets[index] is Sprite sprite)
                    return sprite;
            }

            return null;
        }

        private static float CalculateArtworkScale(
            Sprite sprite,
            float visibleDiameterPixels,
            float targetDiameter)
        {
            float artworkDiameter = visibleDiameterPixels
                                    / Mathf.Max(1f, sprite.pixelsPerUnit);
            return targetDiameter / Mathf.Max(0.0001f, artworkDiameter);
        }
    }
}
