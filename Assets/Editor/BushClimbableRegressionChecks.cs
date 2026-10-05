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
    // Uses the shipped prefab in isolated preview scenes. No check enters the
    // camera/rumble update loop or changes the player's open scene.
    public static class BushClimbableRegressionChecks
    {
        private const string PrefabFolder = "Assets/Prefabs/Environment/Vegetation/";
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static int passed;

        [MenuItem("Animal Game/Validation/Run Bush Climbable Checks")]
        public static void Run()
        {
            passed = 0;
            Check(CheckProductionPrefab);
            Check(CheckDirectionalSurface);
            Check(CheckCommittedMovement);
            Check(CheckContinuousCrossings);
            Check(CheckContactFade);
            Check(CheckCoordinateScales);
            Check(CheckAnimalSight);
            Check(CheckTreesRemainSolid);
            Debug.Log($"Bush climbable checks PASS: {passed} groups; production prefab, directional climbing, committed driving, fast/overlapping crossings, contact fade/scale, animal sight and solid trees");
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
        { Require(Mathf.Abs(actual - expected) < .0002f, $"{message}: {actual} versus {expected}"); }
        private static object Call(object target, string method, params object[] arguments) =>
            target.GetType().GetMethod(method, Instance).Invoke(target, arguments);
        private static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, Instance).SetValue(target, value);
        private static void Property(object target, string property, object value) =>
            target.GetType().GetProperty(property, Instance).SetValue(target, value);

        private static void CheckProductionPrefab()
        {
            using var f = new Fixture();
            GameObject bush = f.Bush(Vector2.zero);
            WorldInteraction climb = Climb(bush);
            Near(climb.ClimbableRadius, .162f, "Bush local climb radius (0.6 map metres at production scale)");
            Near(climb.SlopeStrength01, .5f, "Bush slope strength");
            Near(climb.TopRadiusRatio01, .5f, "Bush flat-top ratio");
            Require(!climb.Recyclable && !climb.TryGrab(climb), "Bush could be picked up/recycled");
            HeightMapObstacleFootprint footprint = bush.GetComponentInChildren<HeightMapObstacleFootprint>();
            Require(footprint != null && !footprint.BlocksTraversal && !footprint.Available,
                "Old bush solid core still blocks traversal");
            Near(footprint.RadiusMeters, .6f, "Retained bush sight footprint");
            Require(!WorldInteractionQuery.Query(InteractionShape.Capsule(Vector2.left, Vector2.right, .1f),
                WorldInteractionKind.Collision | WorldInteractionKind.BodyCollision | WorldInteractionKind.Grabbable
                    | WorldInteractionKind.Pushable, null, f.Scene), "Bush leaked into body/arm/grab/push queries");
            Require(WorldInteractionQuery.Query(InteractionShape.Capsule(Vector2.zero, Vector2.zero, 0f),
                WorldInteractionKind.Climbable, null, f.Scene), "Bush was not indexed as climbable");
            AnimalFoodSource food = bush.GetComponent<AnimalFoodSource>();
            Require(food != null && food.FoodType == AnimalFoodType.Bush && food.SelectionWeight > 0f,
                "Bush lost its animal food source");
            Require(bush.GetComponent<HeightMapPlacedObject>() != null && bush.GetComponent<VegetationContactFade>() != null,
                "Bush lost placement or contact fade");
            Require(bush.GetComponentInChildren<VegetationCanopyOccluder>() != null,
                "Bush lost its canopy occluder");
            foreach (string path in new[] { "Solid Core/Branch", "Solid Core/Center", "Canopy/Leaves" })
            {
                Transform part = bush.transform.Find(path);
                Require(part != null && part.GetComponent<SpriteRenderer>()?.sprite != null,
                    "Bush lost its artwork: " + path);
            }
        }

        private static void CheckDirectionalSurface()
        {
            using var f = new Fixture();
            GameObject bush = f.Bush(Vector2.zero);
            float radius = Climb(bush).GetShape(null).Radius;
            foreach (Vector2 radial in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down,
                new Vector2(1f, 1f).normalized, new Vector2(-1f, 1f).normalized })
            {
                // The same world path is uphill whether the robot faces inward
                // in forward gear or faces outward and reverses.
                ClimbableSurface slope = f.Sample(radial * radius * .75f);
                Require(slope.Source == Climb(bush), "Production bush slope missing");
                Near(slope.Strength, .5f, "Bush slope strength at band midpoint");
                Near(slope.SpeedMultiplier(-radial), .825f, "Forward/reverse inward speed");
                Near(slope.SpeedMultiplier(radial), 1.1f, "Forward/reverse outward speed");
                Near(slope.SpeedMultiplier(new Vector2(-radial.y, radial.x)), 1f, "Cross-slope speed");
                Require(Vector2.Dot(slope.DownhillWorldDirection, radial) > .999f,
                    "Bush downhill direction did not follow the travel side");
                Require(!f.Sample(radial * radius * 1.02f).IsActive, "Outside bush still supplies a virtual slope");
            }
            ClimbableSurface top = f.Sample(Vector2.zero);
            Require(top.IsActive, "Bush flat top disappeared");
            Near(top.Strength, 0f, "Bush center is not flat");
            Near(top.SpeedMultiplier(Vector2.up), 1f, "Bush center slows movement");
        }

        private static void CheckCommittedMovement()
        {
            using var f = new Fixture();
            f.Bush(Vector2.zero);
            foreach (Vector2 direction in new[] { Vector2.right, Vector2.left, new Vector2(1f, 1f).normalized })
            {
                RobotMover robot = f.New("Crossing robot").AddComponent<RobotMover>();
                Vector2 start = -direction * 1.3f;
                robot.transform.position = start;
                Require((bool)Call(robot, "TryMoveSafely", direction * 2.6f, null, true),
                    "Real robot committed movement was blocked by bush");
                Require(Vector2.Distance(robot.transform.position, direction * 1.3f) < .0002f,
                    "Real robot stopped inside the bush");
                Require(robot.ClimbableLandedThisFrame && Vector2.Dot(robot.ClimbableLandingDirection, direction) > .999f,
                    "Fast committed movement lost the directional landing");
                Object.DestroyImmediate(robot.gameObject);
            }
        }

        private static void CheckContinuousCrossings()
        {
            using var f = new Fixture();
            GameObject bush = f.Bush(Vector2.zero);
            float radius = Climb(bush).GetShape(null).Radius;
            var tracker = new ClimbableContactTracker();
            bool Move(float start, float end, float time) => tracker.Move(new Vector2(start * radius, 0f),
                new Vector2(end * radius, 0f), null, f.Scene, time);
            Require(!Move(-2f, -.75f, 0f) && !Move(-.75f, 0f, .1f), "Bush entry/center fabricated a landing");
            Require(Move(0f, 1.2f, .2f), "Final bush exit did not land");
            Require(!Move(1.2f, .999f, .5f) && !Move(.999f, 1.001f, .7f), "Bush rim jitter repeated landing");
            Require(!tracker.Move(new Vector2(-2f, 1f) * radius, new Vector2(2f, 1f) * radius, null, f.Scene, 1f),
                "Tangent bush touch fabricated a landing");
            Require(Move(-2f, 2f, 2f), "High-speed full bush crossing missed landing");
            f.Bush(new Vector2(radius * 1.5f, 0f));
            tracker.Reset();
            Require(!Move(-2f, 0f, 3f), "First overlap entry landed");
            Require(!Move(0f, 1.5f, 3.2f), "Moving between overlapping bushes landed internally");
            Require(Move(1.5f, 2.7f, 3.4f), "Leaving the final overlapping bush did not land");
        }

        private static void CheckContactFade()
        {
            using var f = new Fixture();
            GameObject bush = f.Bush(Vector2.zero);
            VegetationContactFade fade = bush.GetComponent<VegetationContactFade>();
            Call(fade, "CacheReferences");
            Set(fade, "map", null);
            Near((float)Call(fade, "GetPlantContactRadiusMeters"), .6f, "Map-less fade lost the legacy authored footprint");
            MapTestSceneController map = f.FlatMap(.27f);
            Set(bush.GetComponent<HeightMapPlacedObject>(), "map", map);
            Set(fade, "map", map);
            Near((float)Call(fade, "GetPlantContactRadiusMeters"), .6f, "Fade shrank to the placement radius");
            Near(fade.GetBiologicalScanCollisionRadiusWorld(), .162f, "Biological scan world radius differs from bush climb");
            bush.transform.localScale = new Vector3(2f, .75f, 1f);
            WorldInteraction.MarkHierarchySpatialDirty(bush.transform);
            Near((float)Call(fade, "GetPlantContactRadiusMeters"), Climb(bush).GetShape(map).Radius,
                "Contact fade ignores scaled climbable footprint");
            Near(fade.GetBiologicalScanCollisionRadiusWorld(), .324f, "Scaled biological scan world radius");
            SpriteRenderer leaves = bush.transform.Find("Canopy/Leaves").GetComponent<SpriteRenderer>();
            float originalAlpha = leaves.color.a;
            float reduction = (float)Call(fade, "CalculateActorAlphaReduction", Vector2.zero,
                Vector2.right * 1.2f, 1.2f);
            Near(reduction, .3f, "Actor at the climbable edge does not reach contact fade");
            Call(fade, "ApplyAlphaReduction", reduction, true);
            Near(leaves.color.a, originalAlpha * .7f, "Bush leaves lost their authored alpha during fade");
            Call(fade, "ApplyAlphaReduction", 0f, true);
            Near(leaves.color.a, originalAlpha, "Bush leaves did not restore after contact");
        }

        private static void CheckCoordinateScales()
        {
            foreach (float scale in new[] { 1f, .27f })
            {
                using var f = new Fixture();
                MapTestSceneController map = f.FlatMap(scale);
                GameObject bush = f.Bush(Vector2.zero);
                WorldInteraction climb = Climb(bush);
                InteractionShape shape = climb.GetShape(map);
                Near(shape.Radius, scale == 1f ? .162f : .6f, "Climbable radius did not convert into map metres");
                ClimbableSurface surface = ClimbableSurface.Sample(Vector2.right * .1215f,
                    map, f.Scene, new List<WorldInteraction>());
                Require(surface.Source == climb, "Map-scale conversion missed the bush slope band");
                Near(surface.SpeedMultiplier(Vector2.left), .825f, "Map-scale conversion changed uphill strength");
                VegetationContactFade fade = bush.GetComponent<VegetationContactFade>();
                Call(fade, "CacheReferences"); Set(fade, "map", map);
                Near((float)Call(fade, "GetPlantContactRadiusMeters"), scale == 1f ? .3f : .6f,
                    "Fade radius lost the larger authored/climbable footprint");
            }
        }

        private static void CheckAnimalSight()
        {
            using var f = new Fixture();
            MapTestSceneController map = f.FlatMap(.27f);
            GameObject bush = f.Bush(Vector2.zero);
            HeightMapObstacleFootprint cover = bush.GetComponentInChildren<HeightMapObstacleFootprint>();
            Require(cover.BlocksSight && !cover.BlocksTraversal, "Bush sight and traversal are not independent");
            AnimalPerception animal = f.New("Sight observer").AddComponent<AnimalPerception>();
            animal.transform.position = Vector3.left * .54f;
            AnimalSpeciesConfig config = f.Own(ScriptableObject.CreateInstance<AnimalSpeciesConfig>());
            Set(config, "directVisionAngleDegrees", 360f);
            Set(animal, "map", map); Set(animal, "config", config);
            RobotMover player = f.New("Observed player").AddComponent<RobotMover>();
            player.transform.position = Vector3.right * .54f;
            Property(animal, "Player", player);
            bool Sees() => (bool)Call(animal, "HasDirectSightOfPlayer");
            Require(!Sees(), "Traversable bush no longer occludes actual animal perception");
            cover.SightBlocking = ObstacleSightBlocking.NeverBlock;
            Require(Sees(), "Turning off bush sight blocking did not reveal the player");
            cover.SightBlocking = ObstacleSightBlocking.AlwaysBlock; cover.enabled = false;
            Require(Sees(), "Disabled bush footprint still blocked sight");
            cover.enabled = true;
            Require(!Sees(), "Re-enabled cover did not block sight");
            bush.transform.position = Vector3.up * .54f;
            WorldInteraction.MarkHierarchySpatialDirty(bush.transform);
            Require(Sees(), "Bush outside the line still blocked sight");
        }

        private static void CheckTreesRemainSolid()
        {
            foreach (string name in new[] { "Test_Tree", "Test_Dead_Tree" })
            {
                using var f = new Fixture();
                GameObject tree = f.Prefab(name, Vector2.zero);
                HeightMapObstacleFootprint core = tree.GetComponentInChildren<HeightMapObstacleFootprint>();
                Require(core != null && core.BlocksTraversal && core.Available && core.BlocksSight,
                    "Existing tree lost its hard obstacle or default sight blocking: " + name);
                Require(core.SightBlocking == ObstacleSightBlocking.MatchTraversal,
                    "Existing tree no longer inherits its legacy sight semantics: " + name);
                Require(WorldInteractionQuery.Query(InteractionShape.Capsule(Vector2.left * 2f,
                    Vector2.right * 2f, .1f), WorldInteractionKind.Collision, null, f.Scene),
                    "Tree disappeared from collision queries: " + name);
                RobotMover robot = f.New("Blocked robot").AddComponent<RobotMover>();
                robot.transform.position = Vector3.left * 2f;
                Require(!(bool)Call(robot, "TryMoveSafely", Vector2.right * 4f, null, true),
                    "Real robot could drive through an old solid tree: " + name);
                core.BlocksTraversal = false;
                Require(!core.BlocksSight, "Legacy MatchTraversal no longer follows traversal state: " + name);
                core.BlocksTraversal = true;
                Require(core.BlocksSight, "Legacy tree sight state did not restore: " + name);
            }
        }

        private static WorldInteraction Climb(GameObject bush)
        {
            foreach (WorldInteraction item in bush.GetComponentsInChildren<WorldInteraction>())
                if ((item.Kind & WorldInteractionKind.Climbable) != 0) return item;
            throw new InvalidOperationException("Production bush has no climbable interaction");
        }

        private sealed class Fixture : IDisposable
        {
            public readonly Scene Scene = EditorSceneManager.NewPreviewScene();
            private readonly List<WorldInteraction> scratch = new();
            private readonly List<Object> assets = new();
            public GameObject New(string name)
            {
                var result = new GameObject(name);
                SceneManager.MoveGameObjectToScene(result, Scene);
                return result;
            }
            public T Own<T>(T item) where T : Object { assets.Add(item); return item; }
            public GameObject Bush(Vector2 position) => Prefab("Test_Bush", position);
            public GameObject Prefab(string name, Vector2 position)
            {
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + name + ".prefab");
                Require(asset != null, "Missing production vegetation prefab: " + name);
                var result = (GameObject)PrefabUtility.InstantiatePrefab(asset, Scene);
                result.transform.position = position;
                WorldInteraction.MarkHierarchySpatialDirty(result.transform);
                return result;
            }
            public ClimbableSurface Sample(Vector2 point) => ClimbableSurface.Sample(point, null, Scene, scratch);
            public MapTestSceneController FlatMap(float worldUnitsPerMetre)
            {
                GameObject root = New("Sight test flat map");
                MapTestSceneController map = root.AddComponent<MapTestSceneController>();
                map.enabled = false;
                Texture2D texture = Own(new Texture2D(2, 2));
                texture.SetPixels(new[] { Color.gray, Color.gray, Color.gray, Color.gray }); texture.Apply();
                Sprite sprite = Own(Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * .5f, .2f / worldUnitsPerMetre));
                SpriteRenderer renderer = root.AddComponent<SpriteRenderer>(); renderer.sprite = sprite;
                Set(map, "heightField", BakedHeightField.Bake(texture, 64, Vector2.one * 10f, 0f, 1f, false, 0f));
                Set(map, "mapRenderer", renderer); Set(map, "mapWidthMeters", 10f); Set(map, "mapHeightMeters", 10f);
                Require(map.TrySampleWorldPosition(Vector2.zero, out _, out _), "Sight test map did not initialize");
                return map;
            }
            public void Dispose()
            {
                EditorSceneManager.ClosePreviewScene(Scene);
                foreach (Object asset in assets) if (asset != null) Object.DestroyImmediate(asset);
            }
        }
    }
}
