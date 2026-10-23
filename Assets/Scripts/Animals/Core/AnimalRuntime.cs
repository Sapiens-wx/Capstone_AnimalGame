using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.Animals
{
    public enum AnimalMovementResult { Stopped, Moving, Reached, Blocked, NoProgress }
    public enum AnimalActionStatus { Running, Succeeded, Failed }

    public sealed class AnimalRuntime
    {
        public readonly List<Transform> Threats = new();
        public AnimalAgent Target;
        public AnimalGroup Group;
        public float HuntCooldownUntil;
        public float FeedCooldownUntil;
        public SparringSession Sparring;
        public float SparringCooldownUntil;
        public float Time;
        public float SeasonMultiplier = 1f;
        public float TimeOfDayMultiplier = 1f;
        public bool IsDead;
        public bool Removed;
        public readonly Dictionary<int, float> AttackReadyTimes = new();
        public readonly Dictionary<AnimalAgent, float> FailedTargets = new();
    }

    public sealed class AnimalContext
    {
        public AnimalAgent Agent { get; }
        public AnimalSpeciesConfig Config => Agent.Config;
        public AnimalRuntime Runtime => Agent.Runtime;
        public AnimalMotor Motor => Agent.Motor;
        public AnimalHealth Health => Agent.Health;
        public AnimalWorldQuery World => Agent.Simulation.World;
        public AnimalContext(AnimalAgent agent) { Agent = agent; }
    }

    public readonly struct AnimalSnapshot
    {
        public readonly string Species;
        public readonly AnimalState State;
        public readonly AnimalState PhotoState;
        public readonly string Action;
        public readonly bool IsAlive, IsHidden, IsAerial;
        public readonly float Health;
        public readonly Vector2 PositionMeters;
        public readonly float GroundHeightMeters, WaterDepthMeters;
        public AnimalSnapshot(AnimalAgent agent)
        {
            Species = agent.Config != null ? agent.Config.SpeciesName : "";
            State = agent.CurrentState;
            PhotoState = State == AnimalState.Daily ? agent.DailyPhotoState : State;
            Action = agent.Brain?.CurrentActionName ?? "";
            IsAlive = agent.Health != null && !agent.Health.IsDead;
            IsHidden = agent.IsHidden;
            IsAerial = agent.Motor != null && agent.Motor.IsAerialMovement;
            Health = agent.Health?.CurrentHealth ?? 0f;
            PositionMeters = agent.Motor != null ? agent.Motor.CurrentMapPosition : Vector2.zero;
            GroundHeightMeters = agent.Map != null && agent.Map.TrySampleMapPosition(PositionMeters, out float height) ? height : 0f;
            WaterDepthMeters = agent.Map != null && agent.Map.TrySampleStaticWaterMapPosition(PositionMeters, out float depth) ? depth : 0f;
        }
    }

    public abstract class AnimalAction
    {
        public string FailureReason { get; protected set; }
        protected AnimalActionStatus Fail(string reason)
        { FailureReason = reason; return AnimalActionStatus.Failed; }
        public virtual string Name => GetType().Name;
        public virtual AnimalState PhotoState => AnimalState.Daily;
        public virtual void Enter(AnimalContext context) { }
        public abstract AnimalActionStatus Tick(AnimalContext context, float deltaTime);
        public virtual void Exit(AnimalContext context) { context.Motor.Stop(); }
    }
}
