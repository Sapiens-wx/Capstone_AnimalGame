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
            Check(CheckEntryResistance);
            Check(CheckNormalSpeedEntry);
            Check(CheckResistanceFrameRates);
            Check(CheckUncommittedPlan);
            Check(CheckResistanceOverlap);
            Check(CheckWholeAreaAndBodyContact);
            Check(CheckLowSpeedAcceleration);
            Check(CheckReleaseTail);
            Check(CheckGarbageDefaults);
            Check(CheckLandingFeedback);
            Check(CheckDecelerationFeedback);
            Debug.Log($"Bush climbable checks PASS: {passed} groups; production prefab, directional climbing, committed driving, crossings, contact fade/scale, animal sight, solid trees, smooth entry resistance/frame rates, transactional movement, weak cached feedback and unchanged garbage");
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
        private static object Get(object target, string field) =>
            target.GetType().GetField(field, Instance).GetValue(target);
        private static void Property(object target, string property, object value) =>
            target.GetType().GetProperty(property, Instance).SetValue(target, value);

        private static void CheckProductionPrefab()
        {
            using var f = new Fixture();
            GameObject bush = f.Bush(Vector2.zero);
            WorldInteraction climb = Climb(bush);
            Near(climb.ClimbableRadius, .4f, "Bush local radius covers its outer leaves");
            Near(climb.SlopeStrength01, .5f, "Bush slope strength");
            Near(climb.TopRadiusRatio01, .5f, "Bush flat-top ratio");
            Near(climb.ClimbableEntrySpeedMultiplier, .6f, "Bush whole-area speed multiplier");
            Require(climb.ClimbableAffectsWholeArea && climb.ClimbableUseBodyOverlap,
                "Production bush lost whole-area resistance or body-overlap activation");
            Near(climb.ClimbableEntryBlendDuration, .08f, "Bush entry blend duration");
            Near(climb.ClimbableCameraMultiplier, .4f, "Bush camera multiplier");
            Near(climb.ClimbableRumbleMultiplier, .4f, "Bush rumble multiplier");
            Near(climb.ClimbableLandingDurationMultiplier, .6f, "Bush rumble duration multiplier");
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
            Require(f.Sample(Vector2.right * .325f).Source == Climb(bush),
                "Bush outer leaf circle still uses the old narrow solid-core area");
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
                bool landed = false;
                for (int frame = 0; frame < 120 && Vector2.Dot(robot.transform.position, direction) <= 1.3f; frame++)
                {
                    Require((bool)Call(robot, "TryMoveSafely", direction * .1f, null, true),
                        "Real robot committed movement was blocked by bush");
                    landed |= robot.ClimbableLandedThisFrame;
                }
                Require(Vector2.Dot(robot.transform.position, direction) > 1.3f,
                    "Real robot failed to cross the enlarged bush and clear its body overlap");
                Require(landed && Vector2.Dot(robot.ClimbableLandingDirection, direction) > .999f,
                    "Committed movement lost the directional final-overlap landing");
                Near(robot.ClimbableLandingCameraMultiplier, .4f, "Committed bush landing lost its camera profile");
                Near(robot.ClimbableLandingRumbleMultiplier, .4f, "Committed bush landing lost its rumble profile");
                Near(robot.ClimbableLandingDurationMultiplier, .6f, "Committed bush landing lost its pulse duration");
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
            Near(tracker.LandingCameraMultiplier, .4f, "Tracker lost the bush screen multiplier on exit");
            Near(tracker.LandingRumbleMultiplier, .4f, "Tracker lost the bush motor multiplier on exit");
            Near(tracker.LandingDurationMultiplier, .6f, "Tracker lost the bush pulse duration on exit");
            Require(!Move(1.2f, .999f, .5f) && !Move(.999f, 1.001f, .7f), "Bush rim jitter repeated landing");
            Require(!tracker.Move(new Vector2(-2f, 1f) * radius, new Vector2(2f, 1f) * radius, null, f.Scene, 1f),
                "Tangent bush touch fabricated a landing");
            Require(Move(-2f, 2f, 2f), "High-speed full bush crossing missed landing");
            f.Bush(new Vector2(radius * 1.5f, 0f));
            tracker.Reset();
            Require(!Move(-2f, 0f, 3f), "First overlap entry landed");
            Require(!Move(0f, 1.5f, 3.2f), "Moving between overlapping bushes landed internally");
            Require(Move(1.5f, 2.7f, 3.4f), "Leaving the final overlapping bush did not land");
            Near(tracker.LandingRumbleMultiplier, .4f, "Overlapping bushes restored full-strength rumble");
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
            Near((float)Call(fade, "GetPlantContactRadiusMeters"), .4f / .27f, "Fade shrank to the old solid-core radius");
            Near(fade.GetBiologicalScanCollisionRadiusWorld(), .4f, "Biological scan world radius differs from bush climb");
            bush.transform.localScale = new Vector3(2f, .75f, 1f);
            WorldInteraction.MarkHierarchySpatialDirty(bush.transform);
            Near((float)Call(fade, "GetPlantContactRadiusMeters"), Climb(bush).GetShape(map).Radius,
                "Contact fade ignores scaled climbable footprint");
            Near(fade.GetBiologicalScanCollisionRadiusWorld(), .8f, "Scaled biological scan world radius");
            SpriteRenderer leaves = bush.transform.Find("Canopy/Leaves").GetComponent<SpriteRenderer>();
            float originalAlpha = leaves.color.a;
            float contactRadius = (float)Call(fade, "GetPlantContactRadiusMeters");
            float reduction = (float)Call(fade, "CalculateActorAlphaReduction", Vector2.zero,
                Vector2.right * contactRadius, contactRadius);
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
                Near(shape.Radius, .4f / scale, "Climbable radius did not convert into map metres");
                ClimbableSurface surface = ClimbableSurface.Sample(Vector2.right * .3f,
                    map, f.Scene, new List<WorldInteraction>());
                Require(surface.Source == climb, "Map-scale conversion missed the bush slope band");
                Near(surface.SpeedMultiplier(Vector2.left), .825f, "Map-scale conversion changed uphill strength");
                VegetationContactFade fade = bush.GetComponent<VegetationContactFade>();
                Call(fade, "CacheReferences"); Set(fade, "map", map);
                Near((float)Call(fade, "GetPlantContactRadiusMeters"), .4f / scale,
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

        private static void CheckEntryResistance()
        {
            foreach (Vector2 inward in new[] { Vector2.right, Vector2.left, new Vector2(1f, 1f).normalized })
            {
                using var f = new Fixture();
                GameObject bush = f.Bush(Vector2.zero);
                float radius = Climb(bush).GetShape(null).Radius;
                var resistance = new ClimbableEntryResistance();
                const float dt = 1f / 60f;
                const float speed = .05f;
                // Leave three quarters of the first frame inside the band so
                // the cubic blend's displacement is resolvable for diagonal floats.
                Vector2 position = -inward * (radius + speed * dt * .25f);
                bool testedFirstEntry = false;
                for (int frame = 0; frame < 30; frame++)
                {
                    Vector2 proposed = position + inward * speed * dt;
                    Vector2 actual = resistance.Plan(position, proposed, null, f.Scene, dt, 0f, speed);
                    if (position.magnitude >= radius && actual.magnitude < radius)
                    {
                        Require(Vector2.Distance(position, actual) < speed * dt,
                            "Bush first entry frame had no resistance: " + inward);
                        Require(Vector2.Dot(resistance.VelocityLoss, inward) > 0f,
                            "Bush entry lost the forward/reverse velocity difference");
                        testedFirstEntry = true;
                    }
                    Require(resistance.VelocityScale >= .599f && resistance.VelocityScale <= 1.0001f,
                        "Bush entry resistance accumulated below the configured cap");
                    if (frame >= 12)
                    {
                        Near(resistance.VelocityScale, .6f, "Bush ramp did not settle after 0.08 seconds");
                        Near(Vector2.Distance(position, actual) / dt, speed * .6f,
                            "Continuous bush contact repeatedly multiplied speed");
                        Near(Vector2.Dot(resistance.VelocityLoss, inward), speed * .4f,
                            "Bush velocity-loss telemetry does not match the settled speed cap");
                    }
                    resistance.Commit(); position = actual;
                }
                Require(testedFirstEntry, "Resistance fixture never entered the bush");
                // Cover a full diameter even if the entire crossing stayed at
                // its 60-percent cap, plus one second for recovery/exit.
                int exitFrames = Mathf.CeilToInt((2.2f * radius / (speed * .6f) + 1f) / dt);
                for (int frame = 0; frame < exitFrames; frame++)
                {
                    Vector2 actual = resistance.Plan(position, position + inward * speed * dt, null, f.Scene, dt, 0f, speed);
                    resistance.Commit(); position = actual;
                }
                Require(Vector2.Dot(position, inward) > radius, "Entry resistance became a permanent bush slowdown");
                Near(resistance.VelocityScale, 1f, "Entry resistance did not recover after the complete area exit");
                Near(resistance.VelocityLoss.magnitude, 0f, "Entry velocity difference persisted outside the bush");
                resistance.Reset();
                position = -inward * radius * 2f;
                Vector2 fast = resistance.Plan(position, inward * radius * 2f, null, f.Scene, dt);
                Require(Vector2.Dot(fast - position, inward) < radius * 4f
                    && Vector2.Dot(fast - position, inward) > radius,
                    "A high-speed crossing skipped resistance or stopped all progress");
                resistance.Commit();
                Vector2 cleared = resistance.Plan(fast, fast + inward * radius * 4f, null, f.Scene, dt);
                Require(Vector2.Dot(cleared, inward) > radius,
                    "Whole-area resistance trapped the robot after its high-speed crossing");
            }
        }

        private static void CheckResistanceFrameRates()
        {
            var completed = new List<Vector2>();
            foreach (int rate in new[] { 30, 60, 120 })
            {
                using var f = new Fixture();
                float radius = Climb(f.Bush(Vector2.zero)).GetShape(null).Radius;
                var resistance = new ClimbableEntryResistance();
                Vector2 position = Vector2.left * radius * 2f;
                float dt = 1f / rate;
                int seconds = Mathf.CeilToInt(3.2f * radius / (.2f * .6f) + 1f);
                for (int frame = 0; frame < rate * seconds; frame++)
                {
                    Vector2 next = resistance.Plan(position, position + Vector2.right * .2f * dt, null, f.Scene, dt, 0f, .2f);
                    resistance.Commit(); position = next;
                }
                Require(position.x > radius, "A frame-rate trial never left the bush: " + rate);
                Near(resistance.VelocityScale, 1f, "Frame-rate trial retained bush resistance outside");
                completed.Add(position);
            }
            Require(Vector2.Distance(completed[0], completed[1]) < .003f
                && Vector2.Distance(completed[1], completed[2]) < .003f,
                "Bush entry smoothness depends materially on 30/60/120 FPS");
        }

        private static void CheckNormalSpeedEntry()
        {
            using var f = new Fixture();
            float radius = Climb(f.Bush(Vector2.zero)).GetShape(null).Radius;
            var resistance = new ClimbableEntryResistance();
            Vector2 position = Vector2.left * radius * 1.01f;
            const float dt = 1f / 120f;
            const float speed = 3.7f;
            bool slowedBeforeTop = false;
            bool reachedConfiguredCap = false;
            int crossingFrames = Mathf.CeilToInt((2.2f * radius / (speed * .6f) + .25f) / dt);
            for (int frame = 0; frame < crossingFrames; frame++)
            {
                Vector2 next = resistance.Plan(position, position + Vector2.right * speed * dt,
                    null, f.Scene, dt, 0f, speed);
                Require(resistance.VelocityScale >= .599f && resistance.VelocityScale <= 1.0001f,
                    "Normal-speed bush crossing multiplied the configured entry cap");
                if (next.x < -.5f * radius && resistance.VelocityScale <= .70f)
                    slowedBeforeTop = true;
                if (resistance.VelocityScale <= .601f) reachedConfiguredCap = true;
                resistance.Commit(); position = next;
            }
            Require(slowedBeforeTop && reachedConfiguredCap,
                "Production-sized bush did not reach a perceptible slowdown or its new 60-percent speed cap");
            Require(position.x > radius, "Normal-speed entry resistance prevented the robot from crossing the bush");
            Near(resistance.VelocityScale, 1f, "Normal-speed bush entry did not restore ordinary drive after exit");
        }

        private static void CheckUncommittedPlan()
        {
            using var f = new Fixture();
            float radius = Climb(f.Bush(Vector2.zero)).GetShape(null).Radius;
            var discarded = new ClimbableEntryResistance();
            var fresh = new ClimbableEntryResistance();
            Vector2 start = Vector2.left * radius * 1.01f;
            Vector2 end = Vector2.left * radius * .75f;
            discarded.Plan(start, end, null, f.Scene, 1f / 60f);
            Vector2 afterBlocked = discarded.Plan(start, end, null, f.Scene, 1f / 60f);
            Vector2 expected = fresh.Plan(start, end, null, f.Scene, 1f / 60f);
            Require(Vector2.Distance(afterBlocked, expected) < .00001f,
                "A rejected movement consumed the pending entry ramp");
            Near(discarded.VelocityScale, fresh.VelocityScale, "Uncommitted plan changed the next velocity cap");
            discarded.Commit(); fresh.Commit();
            Vector2 next = discarded.Plan(afterBlocked, afterBlocked + Vector2.right * .002f, null, f.Scene, 1f / 60f);
            Vector2 nextExpected = fresh.Plan(expected, expected + Vector2.right * .002f, null, f.Scene, 1f / 60f);
            Require(Vector2.Distance(next, nextExpected) < .00001f, "Rejected movement changed committed follow-up state");
            WorldInteraction wall = f.New("Blocked entry wall").AddComponent<WorldInteraction>();
            wall.SetKind(WorldInteractionKind.Collision); wall.LocalSize = Vector2.one * .1f;
            // The whole-area cap shortens this synthetic high-speed request on
            // first body contact. Place the wall within that resisted segment.
            wall.WorldPosition = Vector3.left * .35f;
            RobotMover robot = f.New("Blocked entry robot").AddComponent<RobotMover>();
            robot.transform.position = Vector3.left * 1.3f;
            Require(!(bool)Call(robot, "TryMoveSafely", Vector2.right * 2.6f, null, true),
                "Rejected-plan fixture did not encounter its hard wall");
            Near(robot.transform.position.x, -1.3f, "Rejected movement changed the actual robot position");
            Near(robot.ClimbableResistanceVelocityLossThisFrame.magnitude, 0f,
                "Rejected movement published an entry slowdown that never occurred");
            wall.enabled = false;
            Require((bool)Call(robot, "TryMoveSafely", Vector2.right * 2.6f, null, true),
                "Removing a wall failed to retry the same bush movement");
            RobotMover freshRobot = f.New("Fresh entry robot").AddComponent<RobotMover>();
            freshRobot.transform.position = Vector3.left * 1.3f;
            Require((bool)Call(freshRobot, "TryMoveSafely", Vector2.right * 2.6f, null, true),
                "Fresh entry fixture could not commit the equivalent movement");
            Require(Vector2.Distance(robot.transform.position, freshRobot.transform.position) < .0001f,
                "Actual mover committed a rejected entry plan before its retry");
        }

        private static void CheckResistanceOverlap()
        {
            using var f = new Fixture();
            float radius = Climb(f.Bush(Vector2.zero)).GetShape(null).Radius;
            f.Bush(Vector2.right * radius * .3f);
            var resistance = new ClimbableEntryResistance();
            Vector2 position = Vector2.left * radius * 1.01f;
            const float dt = 1f / 60f;
            bool fullResistance = false;
            bool recovered = false;
            bool coveredCenter = false;
            int crossingFrames = Mathf.CeilToInt((2.6f * radius / (.2f * .6f) + 1f) / dt);
            for (int frame = 0; frame < crossingFrames; frame++)
            {
                Vector2 next = resistance.Plan(position, position + Vector2.right * .2f * dt, null, f.Scene, dt, 0f, .2f);
                if (resistance.VelocityScale < .601f) fullResistance = true;
                if (fullResistance && position.x > radius * 1.3f) recovered |= resistance.VelocityScale > .999f;
                Require(resistance.VelocityScale >= .599f,
                    "Overlapping bushes multiplied their entry resistance");
                if (fullResistance && position.x >= 0f && next.x < radius * 1.3f)
                {
                    Near(resistance.VelocityScale, .6f, "Center/overlap source changes released whole-area resistance");
                    coveredCenter = true;
                }
                resistance.Commit(); position = next;
            }
            Require(fullResistance && coveredCenter && recovered && position.x > radius * 1.3f,
                "Overlap trial did not exercise ramp, sustained center contact and final exit");
            resistance.Reset();
            Vector2 tangentStart = new Vector2(-2f, 1f) * radius;
            Vector2 tangentEnd = new Vector2(2f, 1f) * radius;
            Require(Vector2.Distance(resistance.Plan(tangentStart, tangentEnd, null, f.Scene, dt), tangentEnd) < .0001f,
                "Tangent edge contact triggered bush entry resistance");
        }

        private static void CheckGarbageDefaults()
        {
            using var f = new Fixture();
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Environment/Garbage/Small_Garbage.prefab");
            Require(asset != null, "Missing production small garbage");
            var item = (GameObject)PrefabUtility.InstantiatePrefab(asset, f.Scene);
            WorldInteraction climb = Climb(item);
            Near(climb.ClimbableEntrySpeedMultiplier, 1f, "Bush tuning changed garbage entry resistance");
            Near(climb.ClimbableCameraMultiplier, 1f, "Bush tuning changed garbage screen feedback");
            Near(climb.ClimbableRumbleMultiplier, 1f, "Bush tuning changed garbage motor strength");
            Near(climb.ClimbableLandingDurationMultiplier, 1f, "Bush tuning changed garbage motor duration");
            Require(!climb.ClimbableAffectsWholeArea && !climb.ClimbableUseBodyOverlap,
                "Production small garbage inherited bush whole-area/body-overlap behavior");
            var resistance = new ClimbableEntryResistance();
            float radius = climb.GetShape(null).Radius;
            Near(ClimbableSurface.GetContactShape(climb, null, .5f).Radius, radius,
                "Ordinary garbage support was expanded by the robot body");
            Vector2 center = climb.GetShape(null).Center;
            Require(!ClimbableSurface.Sample(center + Vector2.right * (radius + .01f), null,
                f.Scene, new List<WorldInteraction>(), null, .5f).IsActive,
                "Garbage virtual slope began before its original center-contact boundary");
            var tracker = new ClimbableContactTracker();
            Require(tracker.Move(center, center + Vector2.right * (radius + .01f), null, f.Scene, 0f, .5f),
                "Garbage landing was delayed by bush body-overlap semantics");
            Vector2 end = (Vector2)item.transform.position + Vector2.right * radius * 2f;
            Vector2 start = (Vector2)item.transform.position - Vector2.right * radius * 2f;
            Require(Vector2.Distance(resistance.Plan(start, end, null, f.Scene, 1f / 60f), end) < .0001f,
                "Production small garbage inherited the bush-only entry cap");
        }

        private static void CheckWholeAreaAndBodyContact()
        {
            using var f = new Fixture();
            WorldInteraction bush = Climb(f.Bush(Vector2.zero));
            float radius = bush.GetShape(null).Radius;
            const float body = .15f;
            var scratch = new List<WorldInteraction>();
            Near(ClimbableSurface.GetContactShape(bush, null, body).Radius, radius + body,
                "Bush contact radius does not include the robot body");
            foreach (float x in new[] { 0f, .325f, radius + body * .8f })
                Near(ClimbableSurface.AreaSpeedMultiplier(Vector2.right * x, null, f.Scene, scratch, body), .6f,
                    "Whole-area bush speed cap missing at center, leaves or body edge");
            Near(ClimbableSurface.AreaSpeedMultiplier(Vector2.right * (radius + body + .02f),
                null, f.Scene, scratch, body), 1f, "Whole-area speed cap persists after the body clears the bush");
            Require(ClimbableSurface.Sample(Vector2.right * (radius + body * .8f), null,
                f.Scene, scratch, null, body).Source == bush,
                "Body-only contact did not activate the bush virtual surface");
            var resistance = new ClimbableEntryResistance();
            Vector2 position = Vector2.left * (radius + body * .9f);
            const float dt = 1f / 60f;
            for (int frame = 0; frame < 12; frame++)
            {
                Vector2 next = resistance.Plan(position, position + Vector2.right * .05f * dt,
                    null, f.Scene, dt, body, .05f);
                resistance.Commit(); position = next;
            }
            Require(position.x < -radius, "Body-contact fixture accidentally moved its center into the bush");
            Near(resistance.VelocityScale, .6f, "Body-only contact did not sustain the 60-percent cap");
            var tracker = new ClimbableContactTracker();
            Require(!tracker.Move(Vector2.zero, Vector2.right * (radius + .01f), null, f.Scene, 0f, body),
                "Bush landing triggered when the robot center left but its body remained inside");
            Require(!tracker.Move(Vector2.right * (radius + .01f), Vector2.right * (radius + body * .8f),
                null, f.Scene, .1f, body), "Partial bush body overlap fabricated an exit");
            Require(tracker.Move(Vector2.right * (radius + body * .8f), Vector2.right * (radius + body + .02f),
                null, f.Scene, .2f, body), "Clearing the final bush body overlap did not land");
            GameObject second = f.Bush(Vector2.right * .6f);
            tracker.Reset();
            Require(!tracker.Move(Vector2.zero, Vector2.right * .6f, null, f.Scene, 1f, body),
                "Switching overlapping bushes triggered an internal body-exit landing");
            Require(!tracker.Move(Vector2.right * .6f, Vector2.right * 1.1f, null, f.Scene, 1.1f, body),
                "Last bush still overlapped the body when landing triggered");
            Require(tracker.Move(Vector2.right * 1.1f, Vector2.right * 1.18f, null, f.Scene, 1.2f, body),
                "Final overlapping bush body exit was lost");
            Near(tracker.LandingRumbleMultiplier, .4f, "Body-overlap exit lost the bush feedback profile");
            second.SetActive(false);
            MapTestSceneController map = f.FlatMap(.27f);
            Near(ClimbableSurface.GetContactShape(bush, map, .5f).Radius, .4f / .27f + .5f,
                "Generated-map body contact mixed world and map units");
            Require(ClimbableSurface.Sample(Vector2.right * .5f, map, f.Scene,
                scratch, null, .5f).Source == bush, "Map-space body radius failed to reach the bush leaves");
            RobotMover robot = f.New("Whole-area robot").AddComponent<RobotMover>();
            Call(robot, "RefreshClimbableSurface");
            Near(robot.CurrentClimbableAreaSpeedMultiplier, .6f, "Actual mover lost the bush center-area multiplier");
            robot.transform.position = Vector3.right * 1.17f;
            Call(robot, "RefreshClimbableSurface");
            Near(robot.CurrentClimbableAreaSpeedMultiplier, 1f,
                "Actual mover failed to restore speed after its complete body exit");
        }

        private static void CheckLowSpeedAcceleration()
        {
            using var f = new Fixture();
            float radius = Climb(f.Bush(Vector2.zero)).GetShape(null).Radius;
            var resistance = new ClimbableEntryResistance();
            Vector2 position = Vector2.zero;
            const float dt = 1f / 60f;
            Vector2 Move(float speed)
            {
                Vector2 next = resistance.Plan(position, position + Vector2.right * speed * dt,
                    null, f.Scene, dt, 0f, 1f);
                Vector2 velocity = (next - position) / dt;
                resistance.Commit(); position = next;
                return velocity;
            }
            for (int frame = 0; frame < 8; frame++)
                Near(Move(.1f).x, .1f, "Low-speed bush entry was capped from its initial tiny speed");
            foreach (float speed in new[] { .2f, .4f, .7f, 1f })
                Near(Move(speed).x, Mathf.Min(speed, .6f), "Whole-area cap prevented normal acceleration toward 60 percent");
            for (int frame = 0; frame < 20; frame++)
                Near(Move(1f).x, .6f, "Continuous center contact compounded the 60-percent cap");
            Require(position.x < radius, "Low-speed acceleration trial left the bush before its final checks");
            RobotMover robot = f.New("Slow entering robot").AddComponent<RobotMover>();
            Set(robot, "forwardSpeed", 1f); Set(robot, "currentSpeed", .1f);
            Call(robot, "RefreshClimbableSurface");
            for (int frame = 0; frame < 20; frame++)
            {
                Call(robot, "UpdateDriveSpeed", 1f, robot.CurrentClimbableAreaSpeedMultiplier,
                    robot.CurrentClimbableAreaSpeedMultiplier, 1f, 0f, dt);
                Require((bool)Call(robot, "TryMoveSafely", Vector2.up * robot.CurrentSpeed * dt, null, false),
                    "Slowly entering actual robot could not continue through the bush");
            }
            Near(robot.CurrentSpeed, .6f, "Actual robot remained capped at its initial low entry speed");
            Call(robot, "UpdateDriveSpeed", 0f, 0f, .6f, 1f, 0f, dt);
            Require(robot.CurrentSpeed > 0f && robot.CurrentSpeed < .6f,
                "Releasing movement input stopped the robot immediately instead of coasting");
        }

        private static void CheckReleaseTail()
        {
            using var f = new Fixture();
            f.Bush(Vector2.zero);
            RobotMover robot = f.New("Release tail robot").AddComponent<RobotMover>();
            robot.SetGrabResistance(1f);
            robot.ReleaseHeavyPullVelocity(Vector2.right, .01f, .01f);
            Vector2 step = (Vector2)Call(robot, "StepHeavyReleaseVelocity", 1f / 60f);
            Require(step.sqrMagnitude > 0f && ((Vector2)Get(robot, "heavyReleaseVelocity")).sqrMagnitude == 0f,
                "Release-tail fixture did not retain its last nonzero integrated displacement");
            Require((bool)Call(robot, "TryMoveSafely", step, null, false),
                "A bush blocked the last valid heavy-release integration step");
            Require(Vector2.Distance(robot.transform.position, step) < .000001f,
                "Zero grab speed limit discarded the last valid release displacement inside a bush");
        }

        private static void CheckLandingFeedback()
        {
            using var f = new Fixture();
            RobotMover mover = f.New("Feedback robot").AddComponent<RobotMover>();
            RobotHeightMotionDetector height = mover.gameObject.AddComponent<RobotHeightMotionDetector>();
            GameObject camera = f.New("Feedback camera"); camera.AddComponent<Camera>();
            RobotCameraShake shake = camera.AddComponent<RobotCameraShake>();
            Set(shake, "mover", mover); Set(shake, "heightMotion", height);
            Property(mover, "ClimbableLandedThisFrame", true);
            Property(mover, "ClimbableLandingDirection", Vector2.right);
            Property(mover, "ClimbableLandingCameraMultiplier", 1f);
            Property(mover, "ClimbableLandingRumbleMultiplier", 1f);
            Property(mover, "ClimbableLandingDurationMultiplier", 1f);
            Call(shake, "DetectDiscreteImpacts", 1f / 60f);
            Vector2 original = (Vector2)Get(shake, "springPositionVelocity");
            Require(original.sqrMagnitude > 0f, "Ordinary climbable landing lost screen feedback");
            Call(shake, "ResetShakeState");
            Property(mover, "ClimbableLandingCameraMultiplier", .4f);
            Property(mover, "ClimbableLandingRumbleMultiplier", .4f);
            Property(mover, "ClimbableLandingDurationMultiplier", .6f);
            Call(shake, "DetectDiscreteImpacts", 1f / 60f);
            Require(Vector2.Distance((Vector2)Get(shake, "springPositionVelocity"), original * .4f) < .0001f,
                "Bush landing screen feedback was not reduced to 40 percent");
            Property(mover, "ClimbableLandingRumbleMultiplier", 1f);
            Property(mover, "ClimbableLandingDurationMultiplier", 1f);
            float onset = (float)Get(shake, "climbableLandingTime");
            Vector2 peak = (Vector2)Call(shake, "GetClimbableLandingMotorSpeeds", onset);
            Near(peak.x, .18f, "Cached bush low-frequency motor strength");
            Near(peak.y, .12f, "Cached bush high-frequency motor strength");
            Vector2 middle = (Vector2)Call(shake, "GetClimbableLandingMotorSpeeds", onset + .06f);
            Near(middle.x, .09f, "Bush motor envelope midpoint");
            Near(middle.y, .06f, "Bush high-frequency envelope midpoint");
            Near(((Vector2)Call(shake, "GetClimbableLandingMotorSpeeds", onset + .121f)).magnitude, 0f,
                "Bush rumble did not expire after 0.12 seconds");
            Call(shake, "ResetShakeState");
            Property(height, "LandedThisFrame", true);
            Property(height, "LastLandingImpact", new RobotLandingImpact(1f, .5f, .1f, .2f, Vector2.right));
            Call(shake, "DetectDiscreteImpacts", 1f / 60f);
            Require(Vector2.Distance((Vector2)Get(shake, "springPositionVelocity"), original) < .0001f,
                "A real airborne landing was weakened by simultaneous bush landing");
            Call(shake, "StopGamepadRumble");
            Near(((Vector2)Call(shake, "GetClimbableLandingMotorSpeeds", Time.time)).magnitude, 0f,
                "Stopping feedback left the cached bush motor envelope active");
        }

        private static void CheckDecelerationFeedback()
        {
            using var f = new Fixture();
            RobotMover mover = f.New("Braking robot").AddComponent<RobotMover>();
            GameObject camera = f.New("Braking camera"); camera.AddComponent<Camera>();
            RobotCameraShake shake = camera.AddComponent<RobotCameraShake>();
            Set(shake, "mover", mover);
            Set(shake, "previousMotionInitialized", true);
            Set(shake, "previousWorldVelocity", Vector2.up * 4f);
            Set(shake, "nextDiscreteImpactTime", float.NegativeInfinity);
            Set(mover, "currentSpeed", 3f);
            Property(mover, "ClimbableResistanceVelocityLossThisFrame", Vector2.up);
            Call(shake, "DetectDiscreteImpacts", 1f / 60f);
            Near(((Vector2)Get(shake, "springPositionVelocity")).magnitude, 0f,
                "Bush entry slowdown leaked into a strong generic braking impact");
            Property(mover, "ClimbableResistanceVelocityLossThisFrame", Vector2.zero);
            Call(shake, "DetectDiscreteImpacts", 1f / 60f);
            Require(((Vector2)Get(shake, "springPositionVelocity")).sqrMagnitude > 0f,
                "Filtering bush slowdown also disabled ordinary braking impacts");
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
