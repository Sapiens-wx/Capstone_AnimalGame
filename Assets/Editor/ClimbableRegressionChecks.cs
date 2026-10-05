using System;
using System.Collections.Generic;
using System.Reflection;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AnimalGame.Editor
{
    public static class ClimbableRegressionChecks
    {
        [MenuItem("Animal Game/Validation/Run Climbable Checks")]
        public static void Run()
        {
            CheckGeometryAndDriving();
            CheckCrossings();
            CheckBalance();
            CheckTerrainOverride();
            CheckFeedback();
            Debug.Log("Climbable checks PASS: geometry/driving, crossings/lifecycle, balance safety/manual tipping, terrain override, feedback merging");
        }

        public static void RunBatch()
        {
            try
            {
                Run();
                RobotArmRegressionChecks.Run();
                GarbageRegressionChecks.Run();
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void CheckGeometryAndDriving()
        {
            using var f = new Fixture();
            WorldInteraction item = f.Prop(Vector2.zero);
            foreach (float ratio in new[] { 0f, 0.001f, 0.5f, 0.999f, 1f })
            {
                item.TopRadiusRatio01 = ratio;
                Require(item.TopRadiusRatio01 > 0f && item.TopRadiusRatio01 < 1f, "Top ratio accepted an endpoint");
                float radial = (1f + item.TopRadiusRatio01) * 0.5f;
                foreach (Vector2 direction in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
                {
                    ClimbableSurface surface = f.Sample(direction * radial);
                    Require(surface.IsActive && surface.Strength > 0.99f, "Slope band not sampled at maximum strength");
                    Require(Vector2.Dot(surface.DownhillWorldDirection, direction) > 0.999f, "Downhill was not radial");
                    Near(surface.SpeedMultiplier(-direction), 0.65f, "Uphill/reverse speed");
                    Near(surface.SpeedMultiplier(direction), 1.2f, "Downhill speed");
                    Near(surface.SpeedMultiplier(new Vector2(-direction.y, direction.x)), 1f, "Cross-slope speed");
                    Near(surface.SpeedMultiplier(Vector2.zero), 1f, "Stationary speed");
                }
                Near(f.Sample(Vector2.zero).Strength, 0f, "Flat top");
                Require(!f.Sample(Vector2.right).IsActive, "Outer boundary counted as inside");
            }
            item.SlopeStrength01 = 0f;
            Near(f.Sample(Vector2.right * 0.9995f).Strength, 0f, "Zero strength");
            var sweep = InteractionShape.Capsule(Vector2.left * 2f, Vector2.right * 2f, 0.1f);
            Require(!WorldInteractionQuery.Query(sweep, WorldInteractionKind.Collision | WorldInteractionKind.BodyCollision
                | WorldInteractionKind.Grabbable, null, f.Scene), "Climbable leaked into arm/body/grab masks");
            item.LocalCenter = new Vector2(0.25f, 0f);
            item.LocalScale = new Vector3(-2f, 3f, 1f);
            item.LocalRotation = Quaternion.Euler(0f, 0f, 42f);
            item.WorldPosition = new Vector3(4f, 3f);
            InteractionShape shape = item.GetShape(null);
            Near(shape.Radius, 3f, "Scaled circle radius");
            Require(f.Sample(shape.Center).IsActive, "Offset/rotated circle center missing from index");
            item.ClimbableRadius = 2f;
            Require(f.Sample(shape.Center + Vector2.right * 5f).IsActive, "Radius edit did not update index");
            item.SetKind(WorldInteractionKind.Climbable | WorldInteractionKind.Grabbable);
            Require(item.TryGrab(item), "Explicit grabbable flag failed");
            Require(!f.Sample(shape.Center).IsActive, "Held prop still supplied a slope");
            item.Release(item);
        }

        private static void CheckCrossings()
        {
            using var f = new Fixture();
            WorldInteraction item = f.Prop(Vector2.zero);
            var tracker = new ClimbableContactTracker();
            bool Move(float a, float b, float t) => tracker.Move(new Vector2(a, 0f), new Vector2(b, 0f), null, f.Scene, t);
            Require(!Move(-2f, -0.75f, 0f), "Entry landed");
            Require(!Move(-0.75f, 0f, 0.1f), "Top entry landed");
            Require(Move(0f, 1.1f, 0.2f), "Exit did not land");
            Require(!Move(1.1f, 0.999f, 0.4f) && !Move(0.999f, 1.001f, 0.6f), "Rim jitter rearmed");
            Require(Move(-2f, 2f, 1f), "Fast full crossing was missed");
            Require(!tracker.Move(new Vector2(-2f, 1f), new Vector2(2f, 1f), null, f.Scene, 2f), "Tangent touch landed");
            item.SlopeStrength01 = 0f;
            Require(!Move(-2f, 2f, 3f), "Zero slope landed");
            item.SlopeStrength01 = 1f;
            WorldInteraction other = f.Prop(new Vector2(1.5f, 0f));
            Require(!Move(0f, 1.6f, 4f), "Overlapping support switch landed");
            Require(Move(1.6f, 3f, 5f), "Final overlapping support exit missed");
            other.enabled = false;
            Move(0f, 0.75f, 6f);
            item.enabled = false;
            Require(!Move(0.75f, 1.2f, 7f), "Disabled prop fabricated a landing");
            item.enabled = true;
            tracker.Reset();
            Require(!Move(3f, 4f, 8f), "Reset/teleport fabricated a landing");
            item.WorldPosition = Vector3.right * 8f;
            Require(!Move(0.75f, 0.8f, 9f), "Moving prop away fabricated a landing");
            item.WorldPosition = Vector3.zero;
            tracker.Reset();
            Move(0.5f, 0.75f, 10f);
            item.WorldPosition = Vector3.left * 0.2f;
            Require(!Move(0.75f, 0.85f, 11f), "Moving the rim across a driving robot fabricated a landing");
        }

        private static void CheckBalance()
        {
            using var f = new Fixture();
            f.Prop(Vector2.zero);
            RobotMover mover = f.New("Robot").AddComponent<RobotMover>();
            RobotBalanceController balance = mover.gameObject.AddComponent<RobotBalanceController>();
            Call(balance, "Awake");
            // Strongly underdamped settings exercise spring overshoot, not just the default steady state.
            Set(balance, "balanceDampingRatio", 0.1f);
            for (int i = 0; i < 1200; i++)
            {
                Vector2 radial = new Vector2(Mathf.Cos(i * 0.035f), Mathf.Sin(i * 0.035f));
                mover.transform.position = radial * 0.75f;
                Call(mover, "RefreshClimbableSurface");
                Set(balance, "filteredWorldAcceleration", radial * (i % 2 == 0 ? 30f : -30f));
                Call(balance, "SimulateBalance", 1f / 60f);
                Require(!balance.IsTippedOver && balance.CurrentState.Magnitude < 1f,
                    "Normal driving tipped on maximum slope");
            }
            mover.transform.position = Vector2.right * 2f;
            Call(mover, "RefreshClimbableSurface");
            Set(balance, "filteredWorldAcceleration", Vector2.zero);
            for (int i = 0; i < 180; i++) Call(balance, "SimulateBalance", 1f / 60f);
            Require(!balance.IsTippedOver, "Automatic slope momentum tipped after exit");
            Set(balance, "balanceDampingRatio", 1.3f);
            mover.transform.position = Vector2.right * 0.75f;
            Call(mover, "RefreshClimbableSurface");
            Set(balance, "currentBalanceLocal", Vector2.zero);
            Set(balance, "balanceVelocityLocal", Vector2.zero);
            Set(balance, "currentCounterbalanceLocal", Vector2.right * 0.6f);
            for (int i = 0; i < 600 && !balance.IsTippedOver; i++) Call(balance, "SimulateBalance", 1f / 60f);
            Require(balance.IsTippedOver, "Climbable safety incorrectly prevented deliberate counterweight tipping");
        }

        private static void CheckTerrainOverride()
        {
            using var f = new Fixture();
            GameObject terrain = f.New("Terrain");
            MapTestSceneController map = terrain.AddComponent<MapTestSceneController>();
            map.enabled = false;
            var texture = new Texture2D(2, 2);
            texture.SetPixels(new[] { Color.black, Color.white, Color.black, Color.white });
            texture.Apply();
            BakedHeightField field = BakedHeightField.Bake(texture, 64, Vector2.one * 100f, 0f, 300f, false, 0f);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f, 0.02f);
            SpriteRenderer renderer = terrain.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            Set(map, "heightField", field);
            Set(map, "mapRenderer", renderer);
            Set(map, "mapWidthMeters", 100f);
            Set(map, "mapHeightMeters", 100f);
            HeightMapTraversalEvaluator evaluator = terrain.AddComponent<HeightMapTraversalEvaluator>();
            evaluator.Initialize(map);
            SlopeTraversalResult original = evaluator.EvaluateImmediateSafety(Vector2.zero, Vector2.left);
            Require(original.RequiresHardStop, "Synthetic terrain did not create unsafe descent");
            WorldInteraction item = f.Prop(Vector2.zero);
            item.ClimbableRadius = 10f;
            Require(!evaluator.EvaluateImmediateSafety(Vector2.zero, Vector2.left, true).RequiresHardStop,
                "Climbable did not cover original slope");
            Require(evaluator.EvaluateImmediateSafety(Vector2.zero, Vector2.left).RequiresHardStop,
                "Climbable altered ordinary terrain queries");
            var scratch = new List<WorldInteraction>();
            Near(ClimbableSurface.Sample(Vector2.zero, map, f.Scene, scratch).Strength, 0f, "Platform was not flat");
            Require(evaluator.EvaluateImmediateSafety(new Vector2(49f, 0f), Vector2.right, true).BlockReason
                == TraversalBlockReason.Boundary, "Climbable bypassed map boundary");
            WorldInteraction wall = f.Prop(new Vector2(2f, 0f));
            wall.SetKind(WorldInteractionKind.Collision);
            wall.LocalSize = Vector2.one;
            Require(!evaluator.TryPlanPush(Vector2.zero, new Vector2(3f, 0f), null, new List<WorldInteraction>()),
                "Climbable bypassed a physical wall");
            // Map owns the field after assignment; detach before disposing the fixture.
            Set(map, "heightField", null);
            field.Dispose();
            UnityEngine.Object.DestroyImmediate(sprite);
            UnityEngine.Object.DestroyImmediate(texture);
        }

        private static void CheckFeedback()
        {
            using var f = new Fixture();
            RobotMover mover = f.New("Robot").AddComponent<RobotMover>();
            RobotHeightMotionDetector height = mover.gameObject.AddComponent<RobotHeightMotionDetector>();
            GameObject camera = f.New("Camera");
            camera.AddComponent<Camera>();
            RobotCameraShake shake = camera.AddComponent<RobotCameraShake>();
            Set(shake, "mover", mover);
            Set(shake, "heightMotion", height);
            Property(mover, "ClimbableLandedThisFrame", true);
            Property(mover, "ClimbableLandingDirection", Vector2.right);
            Call(shake, "DetectDiscreteImpacts", 1f / 60f);
            Vector2 singleVelocity = (Vector2)Get(shake, "springPositionVelocity");
            Require(singleVelocity.sqrMagnitude > 0f, "Exit did not produce a camera impulse");
            Call(shake, "ResetShakeState");
            Property(height, "LandedThisFrame", true);
            Property(height, "LastLandingImpact", new RobotLandingImpact(1f, 0.5f, 0.1f, 0.2f, Vector2.right));
            Call(shake, "DetectDiscreteImpacts", 1f / 60f);
            Require(((Vector2)Get(shake, "springPositionVelocity") - singleVelocity).sqrMagnitude < 0.000001f,
                "Real and climbable landings stacked twice");
            Near((float)Get(shake, "climbableLandingLowFrequency"), 0.45f, "Low-frequency default");
            Near((float)Get(shake, "climbableLandingHighFrequency"), 0.3f, "High-frequency default");
            Call(shake, "StopGamepadRumble");
            Require(float.IsNegativeInfinity((float)Get(shake, "climbableLandingTime")), "Disable did not clear rumble");
        }

        private sealed class Fixture : IDisposable
        {
            public readonly Scene Scene = EditorSceneManager.NewPreviewScene();
            private readonly List<WorldInteraction> scratch = new();
            public GameObject New(string name)
            {
                var go = new GameObject(name);
                SceneManager.MoveGameObjectToScene(go, Scene);
                return go;
            }
            public WorldInteraction Prop(Vector2 position)
            {
                WorldInteraction item = New("Climbable").AddComponent<WorldInteraction>();
                item.SetKind(WorldInteractionKind.Climbable);
                item.WorldPosition = position;
                item.SlopeStrength01 = 1f;
                return item;
            }
            public ClimbableSurface Sample(Vector2 point) => ClimbableSurface.Sample(point, null, Scene, scratch);
            public void Dispose() => EditorSceneManager.ClosePreviewScene(Scene);
        }

        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        private static object Get(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
        private static void Property(object target, string name, object value) => target.GetType().GetProperty(name).SetValue(target, value);
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void Near(float actual, float expected, string message) => Require(Mathf.Abs(actual - expected) < 0.0001f, message);
    }
}
