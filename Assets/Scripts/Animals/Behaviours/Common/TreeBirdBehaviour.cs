using System;
using System.Collections.Generic;
using AnimalGame.MapTest;
using UnityEngine;

namespace AnimalGame.Animals
{
    [Serializable]
    public sealed class TreeBirdSettings
    {
        [Min(0f)] public float MinimumTreeStaySeconds = 15f;
        [Tooltip("Minimum time between tree changes, measured from landing. Species daily weights are configured separately.")]
        [Min(0f)] public float FlightCooldownSeconds = 20f;
        [Range(0f, 1f)] public float ArcProbability = .35f;
        [Min(.05f)] public float CrawlSpeedMetersPerSecond = .35f;
        public Vector2 CrawlPauseSeconds = new Vector2(.7f, 2f);
        [Min(1f)] public float FleeSafeDistanceMultiplier = 2f;
        [Min(0f)] public float FleeOffscreenDelaySeconds = 2f;
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("Animal Game/Animals/Tree Bird Behaviour")]
    public class TreeBirdBehaviour : AnimalBehaviourSet
    {
        [Header("Initial Tree (not an activity boundary or escape destination)")]
        [SerializeField] protected TreeHabitat birthTree;
        [SerializeField, Min(0f)] protected float perchClearanceMeters = .04f;
        [Tooltip("Defaults for recovery/direct actions and escape. Configured daily Action assets own their movement parameters.")]
        [SerializeField] private TreeBirdSettings birdSettings = new TreeBirdSettings();
        private readonly List<AnimalDailyBehaviourSettings> available = new();
        private readonly List<TreeHabitat> healthy = new(), dead = new();
        private float quietCountdown, nextFlightTime, nextSearchTime;
        private bool disposed;
        public TreeBirdSettings Settings => birdSettings;
        public TreeHabitat BirthTree => birthTree;
        public TreeHabitat CurrentTree { get; internal set; }
        public TreeHabitat TargetTree { get; internal set; }
        internal FlyToTreeActionDefinition DailyFlightDefinition
        {
            get
            {
                if (Config != null)
                    foreach (var entry in Config.DailyBehaviours)
                        if (entry?.Action is FlyToTreeActionDefinition flight) return flight;
                return null;
            }
        }
        private float MinimumTreeStay => DailyFlightDefinition != null ? DailyFlightDefinition.MinimumTreeStaySeconds : Settings.MinimumTreeStaySeconds;
        private float FlightCooldown => DailyFlightDefinition != null ? DailyFlightDefinition.FlightCooldownSeconds : Settings.FlightCooldownSeconds;
        public override AnimalState ThreatResponse => AnimalState.Fleeing;
        public override bool UsesCustomFleeDespawn => true;
        public override bool SupportsAggression => false;
        public override AnimalAction CreateEscapeAction() => new BirdFleeAction(this);
        protected virtual bool StationaryTreeActivity => false;
        internal bool StationaryActivity => StationaryTreeActivity;
        internal virtual bool FaceTrunkWhilePerched => false;

        public override void Initialize(AnimalAgent agent)
        {
            base.Initialize(agent);
            quietCountdown = Agent.SoundEmitter != null ? Agent.SoundEmitter.ChooseRepeatInterval(AnimalSoundKind.Idle) : 5f;
            if (!IsUsableTree(birthTree)) birthTree = FindNearestTree();
            SnapToBirthPerch();
            nextFlightTime = Agent.Runtime.Time + Mathf.Max(0f, MinimumTreeStay);
        }

        public override void EnterDaily() { disposed = false; nextSearchTime = 0f; }
        public override void ExitDaily() { CancelConfiguredAction(); Motor?.Stop(); Motor?.ClearMovementOverrides(); TargetTree = null; }
        public override void TickDaily(float dt)
        {
            if (TickConfiguredAction(dt)) return;
            if (Agent.Runtime.Time < nextSearchTime)
            {
                if (IsUsableTree(CurrentTree)) TickTreeActivitySound(dt);
                return;
            }
            if (!IsUsableTree(CurrentTree) || !CurrentTree.ContainsActivityPoint(Agent.Map, Motor.CurrentMapPosition, Config.BodyRadiusMeters))
            {
                CurrentTree = null;
                TreeHabitat tree = FindNearestTree();
                if (tree != null) BeginRuntimeAction(new FlyToTreeAction(this, tree, DailyFlightDefinition));
                else { Motor.Stop(); Motor.SetPerchedMode(true); nextSearchTime = Agent.Runtime.Time + 1f; }
                return;
            }
            available.Clear();
            foreach (AnimalDailyBehaviourSettings settings in Config.DailyBehaviours)
                if (settings != null && Agent.DailyScheduler.Weight(settings) > 0f && CanRunDaily(settings)) available.Add(settings);
            AnimalDailyBehaviourSettings selected = Agent.DailyScheduler.Choose(available);
            AnimalAction action = selected != null ? CreateDailyAction(selected) : null;
            if (action != null) BeginRuntimeAction(action, selected);
            else
            {
                Motor.Stop(); TickTreeActivitySound(dt);
                Agent.DailyScheduler.Failed(selected); nextSearchTime = Agent.Runtime.Time + 1f;
            }
        }

        internal bool CanStartDailyFlight => Agent.Runtime.Time >= nextFlightTime;
        private bool CanRunDaily(AnimalDailyBehaviourSettings settings)
            => settings.Action is TreeBirdActionDefinition action ? action.CanRun(Agent.Context)
                : settings.Action != null && settings.Action.CanRunForDebug(Agent.Context, out _);
        public AnimalAction CreateDailyAction(AnimalDailyBehaviourSettings settings, bool ignoreScheduling = false)
            => settings?.Action != null && (ignoreScheduling || CanRunDaily(settings))
                ? settings.Action.CreateRuntime(settings, Agent.Context) : null;
        internal void ArrivedAt(TreeHabitat tree)
        {
            CurrentTree = tree;
            TargetTree = null;
            nextFlightTime = Agent.Runtime.Time + Mathf.Max(0f, Mathf.Max(MinimumTreeStay, FlightCooldown));
            Motor.SetPerchedMode(true);
        }
        internal void FlightFailed()
        {
            TargetTree = null;
            nextFlightTime = Agent.Runtime.Time + Mathf.Max(1f, FlightCooldown);
            // A failed or interrupted flight may have left the bird between trees.
            if (!IsUsableTree(CurrentTree) || !CurrentTree.ContainsActivityPoint(Agent.Map, Motor.CurrentMapPosition, Config.BodyRadiusMeters))
                CurrentTree = null;
            nextSearchTime = Agent.Runtime.Time + 1f;
        }
        internal virtual void TickTreeActivitySound(float dt)
            => Agent.SoundEmitter?.TickRepeated(AnimalSoundKind.Idle, ref quietCountdown, dt);
        internal bool TryGetPerch(TreeHabitat tree, out Vector2 point)
        {
            point = default;
            if (!IsUsableTree(tree)) return false;
            Vector2 approach = Motor != null ? Motor.CurrentMapPosition : Vector2.zero;
            if (Motor == null) ResolveMap().TrySampleWorldPosition(transform.position, out approach, out _);
            return tree.TryGetTrunkPerchPoint(ResolveMap(), approach,
                Config != null ? Config.BodyRadiusMeters : .12f, perchClearanceMeters, out point);
        }
        internal bool IsUsableTree(TreeHabitat tree) => tree != null && tree.isActiveAndEnabled
            && tree.gameObject.scene == gameObject.scene && ResolveMap() != null
            && tree.TryGetMapPosition(ResolveMap(), out _);
        protected virtual float GetTreeSelectionWeight(TreeHealthState health) => 1f;
        private void GatherTrees()
        {
            healthy.Clear(); dead.Clear();
            foreach (TreeHabitat tree in TreeHabitat.ActiveTrees)
                if (tree != CurrentTree && IsUsableTree(tree) && TryGetPerch(tree, out _))
                    (tree.IsDead ? dead : healthy).Add(tree);
        }
        internal bool HasOtherTree(float healthyWeight = 1f, float deadWeight = 1f)
        { GatherTrees(); return healthyWeight > 0f && healthy.Count > 0 || deadWeight > 0f && dead.Count > 0; }
        public bool TryChooseTree(out TreeHabitat tree, float? healthyWeight = null, float? deadWeight = null)
        {
            GatherTrees();
            float h = healthy.Count > 0 ? Mathf.Max(0f, healthyWeight ?? GetTreeSelectionWeight(TreeHealthState.Healthy)) : 0f;
            float d = dead.Count > 0 ? Mathf.Max(0f, deadWeight ?? GetTreeSelectionWeight(TreeHealthState.Dead)) : 0f;
            tree = null;
            if (h + d <= 0f) return false;
            List<TreeHabitat> list = UnityEngine.Random.value * (h + d) < d ? dead : healthy;
            tree = list[UnityEngine.Random.Range(0, list.Count)];
            return true;
        }
        private TreeHabitat FindNearestTree()
        {
            TreeHabitat nearest = null;
            float distance = float.PositiveInfinity;
            foreach (TreeHabitat tree in TreeHabitat.ActiveTrees)
            {
                if (!IsUsableTree(tree) || !TryGetPerch(tree, out Vector2 point)) continue;
                float candidate = ((Vector2)ResolveMap().MapPositionToWorld(point) - (Vector2)transform.position).sqrMagnitude;
                if (candidate < distance) { distance = candidate; nearest = tree; }
            }
            return nearest;
        }
        private MapTestSceneController ResolveMap()
        {
            if (Agent != null) return Agent.Map;
            HeightMapPlacedObject placed = GetComponent<HeightMapPlacedObject>();
            return placed != null ? placed.Map : null;
        }
        public bool BindBirthTree(TreeHabitat tree, bool snapToPerch)
        { birthTree = tree; return tree != null && (!snapToPerch || SnapToBirthPerch()); }
        public bool BindNearestTree(bool snapToPerch) => BindBirthTree(FindNearestTree(), snapToPerch);
        public bool SnapToBirthPerch()
        {
            if (!TryGetPerch(birthTree, out Vector2 point)) return false;
            if (Motor != null)
            {
                if (!Motor.CanOccupyMapPosition(point, true) || !Motor.TeleportToMapPosition(point)) return false;
                Motor.SetPerchedMode(true);
            }
            else
            {
                Vector3 position = ResolveMap().MapPositionToWorld(point);
                position.z = transform.position.z;
                transform.position = position;
            }
            CurrentTree = birthTree;
            return true;
        }
        public override void EnterCurious() { Agent.Brain.Request(AnimalState.Fleeing); }
        public override void EnterHiding() { Agent.Brain.Request(AnimalState.Fleeing); }
        // The brain executes CreateEscapeAction directly; no return-to-tree adapter.
        public override void EnterFleeing() { }
        public override void TickFleeing(float dt) { }
        public override void ExitFleeing() { }
        protected virtual void OnEnable()
        {
            disposed = false;
            if (Agent != null && Agent.IsInitialized && Agent.CurrentState == AnimalState.Daily)
                Agent.ReturnToDaily();
        }
        protected virtual void OnDisable()
        {
            if (disposed || Agent == null) return;
            disposed = true;
            ExitDaily();
        }
    }
}
