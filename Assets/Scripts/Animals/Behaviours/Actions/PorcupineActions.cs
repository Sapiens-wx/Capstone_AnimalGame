using AnimalGame.MapTest;
using AnimalGame.World;
using UnityEngine;

namespace AnimalGame.Animals
{
    public sealed class PorcupineTreeAction : AnimalAction
    {
        private readonly float duration;
        private readonly AnimalSoundKind sound;
        private readonly bool change, escaping;
        private NorthAmericanPorcupineBehaviour porcupine;
        private TreeHabitat tree;
        private bool arrived, valid;
        private float elapsed, travel, soundCountdown, lookCountdown;
        public PorcupineTreeAction(float duration, AnimalSoundKind sound, bool change = false, bool escaping = false)
        { this.duration = duration; this.sound = sound; this.change = change; this.escaping = escaping; }
        public override string Name => escaping ? "EscapeToTree" : change ? "ChangeTreeOnGround" : sound == AnimalSoundKind.BarkEating ? "EatTreeBark" : sound == AnimalSoundKind.Eating ? "EatInTree" : "LookAroundInTree";
        public override AnimalState PhotoState => !arrived ? AnimalState.Moving
            : sound == AnimalSoundKind.Eating || sound == AnimalSoundKind.BarkEating ? AnimalState.Eating : AnimalState.Resting;
        public override void Enter(AnimalContext c)
        {
            porcupine = (NorthAmericanPorcupineBehaviour)c.Agent.Behaviour;
            valid = porcupine.TryFindTree(change, out tree, out Vector2 destination, escaping);
            if (!valid) return;
            if (tree == porcupine.CurrentTree) { arrived = true; c.Motor.Stop(); c.Motor.SetPerchedMode(true); }
            else
            {
                porcupine.LeaveTree();
                valid = c.Motor.SetTarget(destination, escaping ? c.Config.FleeSpeedMetersPerSecond : c.Config.DailyMoveSpeedMetersPerSecond);
            }
        }
        public override AnimalActionStatus Tick(AnimalContext c, float dt)
        {
            if (!valid || tree == null || !tree.isActiveAndEnabled) return Fail("No reachable tree is available.");
            if (!arrived)
            {
                travel += dt;
                if (travel > c.Config.MaximumTravelTimeSeconds || c.Motor.Result == AnimalMovementResult.NoProgress) return Fail("Could not reach the selected tree.");
                if (!c.Motor.HasArrived) return AnimalActionStatus.Running;
                arrived = true; porcupine.EnterTree(tree);
                c.Agent.SoundEmitter?.Emit(AnimalSoundKind.EnteringTree);
            }
            if (escaping) return AnimalActionStatus.Running;
            if (change) return AnimalActionStatus.Succeeded;
            elapsed += dt;
            if (sound == AnimalSoundKind.Looking)
            {
                c.Motor.FacingTurnSpeedDegreesPerSecond = 35f;
                lookCountdown -= dt;
                if (lookCountdown <= 0f)
                { lookCountdown = c.Config.ChooseLookInterval(); c.Motor.FaceMapDirection(Random.insideUnitCircle.normalized); }
            }
            else if (tree.TryGetMapPosition(c.Agent.Map, out Vector2 center)) c.Motor.FaceMapPosition(center);
            c.Agent.SoundEmitter?.TickRepeated(sound, ref soundCountdown, dt);
            return elapsed >= duration ? AnimalActionStatus.Succeeded : AnimalActionStatus.Running;
        }
        public override void Exit(AnimalContext c)
        { c.Motor.FacingTurnSpeedDegreesPerSecond = null; base.Exit(c); }
    }

    public sealed class PorcupineGroundSniffAction : AnimalAction
    {
        private readonly SniffWanderAction sniff;
        public PorcupineGroundSniffAction(float duration, float speed) { sniff = new SniffWanderAction(duration, speed); }
        public override AnimalState PhotoState => AnimalState.Moving;
        public override void Enter(AnimalContext c)
        { ((NorthAmericanPorcupineBehaviour)c.Agent.Behaviour).LeaveTree(); sniff.Enter(c); }
        public override AnimalActionStatus Tick(AnimalContext c, float dt) => sniff.Tick(c, dt);
        public override void Exit(AnimalContext c) { sniff.Exit(c); }
    }

    public sealed class QuillDefenseAction : AnimalAction
    {
        private NorthAmericanPorcupineBehaviour porcupine;
        public override string Name => "QuillDefense";
        public override AnimalState PhotoState => AnimalState.Aggressive;
        public override void Enter(AnimalContext c)
        { porcupine = (NorthAmericanPorcupineBehaviour)c.Agent.Behaviour; c.Motor.Stop(); porcupine.SetQuills(true); }
        public override AnimalActionStatus Tick(AnimalContext c, float dt)
        {
            if (!c.Agent.Perception.TryGetThreatProximity(out _)) return AnimalActionStatus.Succeeded;
            Vector2 target = InteractionShape.ToQuery(c.Agent.Perception.PrimaryThreat.position, c.Agent.Map);
            c.Motor.FaceMapDirection(c.Motor.CurrentMapPosition - target);
            return AnimalActionStatus.Running;
        }
        public override void Exit(AnimalContext c) { porcupine.SetQuills(false); base.Exit(c); }
    }
}
