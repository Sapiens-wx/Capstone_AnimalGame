using System.Collections.Generic;
using AnimalGame.Animals;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using UnityEngine;

namespace AnimalGame.Tutorial
{
    /// <summary>Owns the teaching bird's pose and one departure, without changing the shared species AI.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(40)]
    public sealed class TutorialWoodpeckerActor : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float departureSeconds = 3f;
        [SerializeField, Min(.1f)] private float departureSpeedMetersPerSecond = 7f;

        private HeightMapPlayerSceneBootstrap bootstrap;
        private AnimalAgent agent;
        private TreeHabitat trunk;
        private bool ready, departureRequested, departureStarted, hasDeparted;
        private Vector2 lastMapPosition;

        public AnimalAgent Agent => agent;
        public TreeHabitat Trunk => trunk;
        public bool IsReady => ready && !departureRequested && agent != null && agent.IsPresent;
        public bool IsDeparting => departureRequested && !hasDeparted;
        public bool HasDeparted => hasDeparted;
        public bool HasStartedDepartureFlight => departureStarted;
        public Vector2 PerchMapPosition { get; private set; }
        public bool IsPecking => IsReady && agent.Brain != null
            && agent.Brain.CurrentActionName == PeckingAction.ActionName;
        public Vector2 LastMapPosition => agent != null && agent.IsInitialized
            ? agent.Motor.CurrentMapPosition : lastMapPosition;
        public string LastFailure { get; private set; }

        public bool Configure(HeightMapPlayerSceneBootstrap sceneBootstrap, AnimalAgent teachingBird, TreeHabitat teachingTrunk)
        {
            if (gameObject.scene.path != TutorialDrivingLesson.ScenePath || sceneBootstrap == null
                || sceneBootstrap.gameObject.scene != gameObject.scene || teachingBird == null
                || teachingBird.gameObject.scene != gameObject.scene || teachingTrunk == null
                || teachingTrunk.gameObject.scene != gameObject.scene || !teachingTrunk.IsDead)
            { LastFailure = "The teaching woodpecker needs its own dead trunk in the tutorial scene."; return false; }
            var woodpecker = teachingBird.GetComponent<PileatedWoodpeckerBehaviour>();
            if (woodpecker == null)
            { LastFailure = "The teaching bird needs a woodpecker behaviour."; return false; }

            bootstrap = sceneBootstrap; agent = teachingBird; trunk = teachingTrunk;
            ready = departureRequested = departureStarted = hasDeparted = false;
            woodpecker.BindBirthTree(trunk, false);
            agent.enabled = true;
            if (!agent.TryInitialize() || !woodpecker.BindBirthTree(trunk, true))
            { LastFailure = "The teaching woodpecker cannot reach a clear perch beside its trunk."; return false; }
            // ReturnToDaily clears any old observation action on this owned teaching clone.
            // The native peck action supplies its real motion, facing, sound and cleanup.
            agent.ReturnToDaily();
            agent.SetPerceptionSuppressed(true);
            if (agent.Brain == null || !agent.Brain.StartAction(new PeckingAction(woodpecker)))
            { LastFailure = "The teaching woodpecker is preparing its pecking pose."; return false; }
            agent.PlaceholderView?.RestoreVisibleAppearance();
            PerchMapPosition = lastMapPosition = agent.Motor.CurrentMapPosition;
            ready = true; LastFailure = string.Empty;
            return true;
        }

        /// <summary>Queue once after an accepted photo. The visible flight waits until the photo result closes.</summary>
        public bool BeginDeparture()
        {
            if (!IsReady) return false;
            departureRequested = true;
            return true;
        }

        private void Update()
        {
            if (agent != null && agent.IsInitialized) lastMapPosition = agent.Motor.CurrentMapPosition;
            if (!departureRequested || departureStarted || hasDeparted || IsPresentationPaused()) return;
            if (agent == null || !agent.IsPresent)
            { hasDeparted = true; ready = false; return; }
            agent.ReturnToDaily();
            agent.SetPerceptionSuppressed(true);
            departureStarted = agent.Brain != null && agent.Brain.StartAction(new DepartureAction(this));
        }

        private bool IsPresentationPaused() => Time.deltaTime <= 0f
            || AnimalGame.MainUI.MainUI.BlocksGameplay || AnimalGame.AnimalDebug.AnimalDebugUI.BlocksOtherPanels
            || (bootstrap != null && ((bootstrap.photoResultView != null && bootstrap.photoResultView.IsShowing)
                || (bootstrap.photoResultUi != null && bootstrap.photoResultUi.IsResultVisible)
                || (bootstrap.photoMode != null && (bootstrap.photoMode.IsCapturing || bootstrap.photoMode.IsReviewing))));

        private void CompleteDeparture(AnimalContext context)
        {
            lastMapPosition = context.Motor.CurrentMapPosition;
            hasDeparted = true; ready = false;
            context.Agent.Despawn();
        }

        private sealed class PeckingAction : AnimalAction
        {
            public const string ActionName = "Tutorial woodpecker pecking";
            private readonly WoodpeckerPeckAction native;
            public override string Name => ActionName;
            // The tutorial photograph uses the existing Daily catalog entry while showing native pecking.
            public override AnimalState PhotoState => AnimalState.Daily;
            public PeckingAction(PileatedWoodpeckerBehaviour woodpecker)
            { native = new WoodpeckerPeckAction(woodpecker, float.MaxValue); }
            public override void Enter(AnimalContext context) => native.Enter(context);
            public override AnimalActionStatus Tick(AnimalContext context, float deltaTime)
            { return native.Tick(context, deltaTime); }
            public override void Exit(AnimalContext context) => native.Exit(context);
        }

        private sealed class DepartureAction : AnimalAction
        {
            private readonly TutorialWoodpeckerActor owner;
            private readonly List<Vector2> path = new();
            private float elapsed, visibleLifetime;
            private bool paused, hasFlightPath;
            private Vector2 direction;
            public override string Name => "Tutorial woodpecker departure";
            public override AnimalState PhotoState => AnimalState.Moving;
            public DepartureAction(TutorialWoodpeckerActor owner) { this.owner = owner; }
            public override void Enter(AnimalContext context)
            {
                context.Motor.Stop(); context.Motor.ClearMovementOverrides();
                Vector2 current = context.Motor.CurrentMapPosition;
                direction = context.Motor.FacingMapDirection;
                if (owner.bootstrap.robot != null && context.Agent.Map.TrySampleWorldPosition(
                    owner.bootstrap.robot.transform.position, out Vector2 player, out _))
                    direction = current - player;
                if (direction.sqrMagnitude < .000001f) direction = Vector2.up;
                direction.Normalize();
                hasFlightPath = TryStartPath(context);
                context.Agent.SoundEmitter?.Emit(AnimalSoundKind.Takeoff);
                context.Agent.SoundEmitter?.BeginMovementSound(AnimalSoundKind.Flying);
            }

            private bool TryStartPath(AnimalContext context)
            {
                Vector2 start = context.Motor.CurrentMapPosition;
                float speed = Mathf.Min(owner.departureSpeedMetersPerSecond,
                    Mathf.Max(.1f, context.Config.FleeSpeedMetersPerSecond));
                float distance = Mathf.Max(1f, speed * (owner.departureSeconds - elapsed + .3f));
                // Bounds or another bird may block a heading. Prefer an available route away from the player.
                for (int i = 0; i < 12; i++)
                {
                    int side = (i + 1) / 2 * (i % 2 == 0 ? 1 : -1);
                    float angle = side * 15f * Mathf.Deg2Rad;
                    Vector2 heading = new Vector2(direction.x * Mathf.Cos(angle) - direction.y * Mathf.Sin(angle),
                        direction.x * Mathf.Sin(angle) + direction.y * Mathf.Cos(angle));
                    for (float length = distance; length >= 1f; length *= .5f)
                    {
                        Vector2 end = start + heading * length;
                        if (!context.Motor.CanFlySegment(start, end)) continue;
                        path.Clear(); path.Add(end);
                        if (!context.Motor.SetAerialPath(path, speed)) continue;
                        direction = heading; return true;
                    }
                }
                context.Motor.Stop(); return false;
            }

            public override AnimalActionStatus Tick(AnimalContext context, float deltaTime)
            {
                if (owner.IsPresentationPaused())
                { if (!paused) context.Motor.Stop(); paused = true; return AnimalActionStatus.Running; }
                if (paused || !hasFlightPath || context.Motor.HasArrived
                    || context.Motor.Result == AnimalMovementResult.NoProgress)
                { paused = false; hasFlightPath = TryStartPath(context); }
                // Do not count hidden result time or a blocked takeoff as visible flight.
                visibleLifetime += deltaTime;
                if (hasFlightPath) elapsed += deltaTime;
                if (elapsed < Mathf.Max(1f, owner.departureSeconds)
                    && visibleLifetime < Mathf.Max(1f, owner.departureSeconds) + 1f) return AnimalActionStatus.Running;
                owner.CompleteDeparture(context);
                return AnimalActionStatus.Running;
            }

            public override void Exit(AnimalContext context)
            { base.Exit(context); context.Motor.ClearMovementOverrides(); }
        }
    }
}
