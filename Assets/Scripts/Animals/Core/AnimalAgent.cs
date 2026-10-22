using System.Collections.Generic;
using AnimalGame.MapTest;
using AnimalGame.Rendering;
using UnityEngine;

namespace AnimalGame.Animals
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HeightMapPlacedObject), typeof(AnimalMotor), typeof(AnimalPerception))]
    [RequireComponent(typeof(AnimalSoundEmitter), typeof(AnimalBody), typeof(AnimalView))]
    [AddComponentMenu("Animal Game/Animals/Animal Agent")]
    public sealed class AnimalAgent : MonoBehaviour
    {
        private static readonly HashSet<AnimalAgent> activeAgents = new();
        [SerializeField] private AnimalSpeciesConfig config;
        [SerializeField] private AnimalBehaviourSet behaviourSet;
        [SerializeField] private AnimalPlaceholderView placeholderView;
        [SerializeField] private AnimalSoundEmitter soundEmitter;
        [Tooltip("Animals with the same non-empty ID in this scene form a group, including mixed species.")]
        [SerializeField] private string groupId;
        public AnimalSpeciesConfig Config => config;
        public bool SupportsAggression => behaviourSet != null && behaviourSet.SupportsAggression;
        public bool CanAcceptSocialAction => CurrentState == AnimalState.Daily && behaviourSet != null
            && behaviourSet.CanAcceptSocialAction && !Brain.HasOverrideAction;
        public static IEnumerable<AnimalAgent> ActiveAgents => activeAgents;
        public AnimalMotor Motor { get; private set; }
        public AnimalPerception Perception { get; private set; }
        public AnimalPlaceholderView PlaceholderView => placeholderView;
        public AnimalSoundEmitter SoundEmitter => soundEmitter;
        public MapTestSceneController Map { get; private set; }
        // Compatibility anchor for returning to a specific tree/water site, not an activity boundary.
        public Vector2 HomeMapPosition { get; private set; }
        public Vector2 ActivityCenter => Runtime?.Group?.Leader != null
            ? Runtime.Group.Leader.Motor.CurrentMapPosition : Motor.CurrentMapPosition;
        public AnimalState CurrentState => Brain?.State ?? AnimalState.Daily;
        public AnimalState DailyPhotoState => Brain != null ? Brain.PhotoState : AnimalState.Daily;
        public bool PerceptionSuppressed { get; private set; }
        public bool IsHidden => CurrentState == AnimalState.Hiding || CurrentState == AnimalState.Despawned;
        public bool IsPresent => initialized && !IsHidden && !Health.IsDead;
        public bool IsInitialized => initialized;
        public AnimalRuntime Runtime { get; private set; }
        public AnimalContext Context { get; private set; }
        public AnimalBrain Brain { get; private set; }
        public AnimalHealth Health { get; private set; }
        public AnimalDailyScheduler DailyScheduler { get; private set; }
        public AnimalSimulation Simulation { get; private set; }
        public AnimalSnapshot Snapshot => new AnimalSnapshot(this);
        public int ReactionVersion { get; private set; }
        public bool IsCalmReady => CurrentState != AnimalState.Curious
            || (!Perception.HasThreatInRange && outsideAlertTimer >= config.CuriousLostPlayerDelaySeconds);
        private AnimalBody body;
        private bool initialized, rejected;
        private float reactionCountdown, outsideAlertTimer, detectionGraceRemaining;
        private SpriteRenderer[] playerUiVisibilityRenderers;
        private AggressiveAction aggressiveAction;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry() { activeAgents.Clear(); }

        private void OnEnable()
        {
            playerUiVisibilityRenderers = GetComponentsInChildren<SpriteRenderer>(true);
            PlayerUiOrganicVisibility.RegisterRenderers(playerUiVisibilityRenderers);
            if (gameObject.scene.IsValid()) activeAgents.Add(this);
            if (Application.isPlaying)
            {
                Simulation = AnimalSimulation.ForScene(gameObject.scene);
                Simulation.Register(this);
            }
        }
        private void OnDisable()
        {
            activeAgents.Remove(this);
            Simulation?.Unregister(this);
            // A temporary disable preserves identity; only Despawn removes it permanently.
        }
        private void OnDestroy()
        {
            Brain?.Stop();
            Runtime?.Group?.Remove(this);
            activeAgents.Remove(this);
            Simulation?.Unregister(this);
            PlayerUiOrganicVisibility.UnregisterRenderers(playerUiVisibilityRenderers);
        }
        public void SimulationTick(float deltaTime)
        {
            if (rejected || (!initialized && !TryInitialize()) || deltaTime <= 0f) return;
            Runtime.Time = Simulation.Time;
            if (Health.IsDead)
            {
                Health.Tick(deltaTime);
                if (Health.CorpseRemaining <= .00001f) Despawn();
                return;
            }
            if (Simulation.World.IsBeyondView(this)) { Despawn(); return; }
            detectionGraceRemaining = Mathf.Max(0f, detectionGraceRemaining - deltaTime);
            if (!PerceptionSuppressed && detectionGraceRemaining <= 0f && Perception.TickDetection(deltaTime))
                NotifyThreat(Perception.PrimaryThreat, true);
            Brain.Tick(deltaTime);
            if (Runtime.Removed) return;
            Motor.Tick(deltaTime);
            soundEmitter?.Tick(deltaTime);
        }
        public void ConfigureEditorDefaults(AnimalSpeciesConfig speciesConfig, AnimalBehaviourSet speciesBehaviour, AnimalPlaceholderView view)
        { config = speciesConfig; behaviourSet = speciesBehaviour; placeholderView = view; }
        public void SetPerceptionSuppressed(bool suppressed) { PerceptionSuppressed = suppressed; }
        public void ReturnToDaily()
        {
            if (!CanAct) return;
            detectionGraceRemaining = 0f;
            Perception.ResetDetection();
            Brain.Request(AnimalState.Daily);
        }
        public void BeginHiding() { if (CanAct && !IsHidden) Brain.Request(AnimalState.Hiding); }
        public void CompleteHiding()
        {
            if (!CanAct || CurrentState != AnimalState.Hiding) return;
            detectionGraceRemaining = config.PostReappearGraceDurationSeconds;
            Perception.ResetDetection();
            Brain.Request(AnimalState.Daily);
        }
        public void Despawn()
        {
            if (Runtime != null && Runtime.Removed) return;
            if (Runtime != null) Runtime.Removed = true;
            Brain?.Request(AnimalState.Despawned);
            Runtime?.Group?.Remove(this);
            Motor?.Stop();
            if (body != null) body.MarkSpatialDirty();
            if (Application.isPlaying) Destroy(gameObject);
        }
        private bool CanAct => initialized && !Health.IsDead && !Runtime.Removed;
        public bool TryInitialize()
        {
            if (initialized) return true;
            if (rejected) return false;
            if (config == null) return Reject("Animal Agent has no species configuration.");
            HeightMapPlacedObject placed = GetComponent<HeightMapPlacedObject>();
            Map = placed != null ? placed.Map : null;
            if (Map == null)
                foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                { Map = root.GetComponentInChildren<MapTestSceneController>(); if (Map != null) break; }
            if (Map == null || !Map.HasGeneratedMap) return false;
            if (!Map.TrySampleWorldPosition(transform.position, out Vector2 home, out _))
                return Reject("Animal spawn is outside the generated map.");
            Motor = GetComponent<AnimalMotor>();
            Perception = GetComponent<AnimalPerception>();
            body = GetComponent<AnimalBody>();
            if (body == null) body = gameObject.AddComponent<AnimalBody>();
            if (behaviourSet == null) behaviourSet = GetComponent<AnimalBehaviourSet>();
            if (behaviourSet == null) behaviourSet = gameObject.AddComponent<GenericAnimalBehaviour>();
            if (placeholderView == null) placeholderView = GetComponent<AnimalPlaceholderView>();
            if (soundEmitter == null) soundEmitter = GetComponent<AnimalSoundEmitter>();
            if (Simulation == null) Simulation = AnimalSimulation.ForScene(gameObject.scene);
            Simulation.Register(this);
            Runtime = new AnimalRuntime { Time = Simulation.Time };
            Context = new AnimalContext(this);
            DailyScheduler = new AnimalDailyScheduler(Context);
            Health = new AnimalHealth(config);
            Health.Died += Die;
            HomeMapPosition = home;
            Motor.Initialize(Map, config);
            body.Initialize(this);
            if (!Motor.CanOccupyMapPosition(home, behaviourSet is PileatedWoodpeckerBehaviour))
                return Reject("Animal spawn overlaps an obstacle or another actor.");
            Perception.Initialize(Map, config);
            behaviourSet.Initialize(this);
            soundEmitter?.Initialize(this);
            placeholderView?.RestoreVisibleAppearance();
            Brain = new AnimalBrain(Context, behaviourSet);
            initialized = true;
            Simulation.GetGroup(groupId)?.Add(this);
            Brain.Request(AnimalState.Daily);
            body.MarkSpatialDirty();
            return true;
        }
        private bool Reject(string message)
        {
            rejected = true;
            Debug.LogError(message, this);
            enabled = false;
            if (Application.isPlaying) gameObject.SetActive(false);
            return false;
        }
        internal void OnStateEntered(AnimalState state)
        {
            PerceptionSuppressed = state == AnimalState.Hiding || state == AnimalState.Dead || state == AnimalState.Despawned;
            outsideAlertTimer = 0f;
            reactionCountdown = config.ReactionIntervalSeconds;
            body?.MarkSpatialDirty();
            if (state == AnimalState.Dead)
            {
                AnimalView view = GetComponent<AnimalView>();
                if (view == null) view = gameObject.AddComponent<AnimalView>();
                view.ShowCorpse(this);
                body.BecomeCorpse();
            }
            if (state == AnimalState.Fleeing || state == AnimalState.Aggressive)
            {
                ReactionVersion++;
                Runtime.Group?.Reaction(this, Perception.PrimaryThreat);
            }
        }
        public void NotifyThreat(Transform threat, bool broadcast = true)
        {
            if (!CanAct) return;
            Perception.Remember(threat);
            if (CurrentState != AnimalState.Fleeing && CurrentState != AnimalState.Aggressive && CurrentState != AnimalState.Curious)
                Brain.Request(AnimalState.Curious);
            if (broadcast) Runtime.Group?.Alert(this, threat);
        }
        public void ForceGroupReaction(Transform threat)
        {
            if (!CanAct) return;
            Perception.Remember(threat);
            float flee = config.BaseFleeChancePerCheck, aggression = SupportsAggression ? config.BaseAggressionChancePerCheck : 0f;
            float total = flee + aggression;
            bool angry = SupportsAggression && (total <= 0f ? Random.value < .5f : Random.value * total < aggression);
            Brain.Request(angry ? AnimalState.Aggressive : AnimalState.Fleeing);
        }
        internal void TickCurious(float deltaTime)
        {
            bool hasThreat = Perception.TryGetThreatProximity(out float proximity);
            behaviourSet.MoveWhileCurious(deltaTime);
            behaviourSet.TickCurious(deltaTime);
            if (!hasThreat)
            {
                outsideAlertTimer += deltaTime;
                if (outsideAlertTimer >= config.CuriousLostPlayerDelaySeconds && (Runtime.Group == null || Runtime.Group.CanCalm())) ReturnToDaily();
                return;
            }
            outsideAlertTimer = 0f;
            reactionCountdown -= deltaTime;
            if (reactionCountdown > 0f) return;
            reactionCountdown = config.ReactionIntervalSeconds;
            float flee = config.BaseFleeChancePerCheck * Mathf.Lerp(1f, config.NearestFleeMultiplier, proximity);
            float angry = SupportsAggression ? config.BaseAggressionChancePerCheck * Mathf.Lerp(1f, config.NearestAggressionMultiplier, proximity) : 0f;
            float normalization = Mathf.Max(1f, flee + angry);
            float roll = Random.value;
            if (roll < angry / normalization) Brain.Request(AnimalState.Aggressive);
            else if (roll < (angry + flee) / normalization) Brain.Request(AnimalState.Fleeing);
        }
        public bool ReceiveDamage(float damage, Transform attacker, bool notifyThreat = true)
        {
            if (!CanAct) return false;
            bool applied = Health.Damage(damage);
            if (applied && !Health.IsDead && notifyThreat) NotifyThreat(attacker);
            return applied;
        }
        private void Die()
        {
            Runtime.IsDead = true;
            Brain.Request(AnimalState.Dead);
            Runtime.Group?.Remove(this);
            Motor.Stop();
        }
        internal void EnterAggressiveAction()
        { aggressiveAction = new AggressiveAction(); aggressiveAction.Enter(Context); }
        internal void TickAggressive(float dt)
        { if (aggressiveAction.Tick(Context, dt) != AnimalActionStatus.Running) ReturnToDaily(); }
        internal void ExitAggressiveAction() { aggressiveAction?.Exit(Context); aggressiveAction = null; }
        public bool StartHunt(AnimalAgent target = null)
        {
            if (!CanAct || !config.CanHunt || CurrentState != AnimalState.Daily) return false;
            return Brain.StartAction(new HuntAction(target));
        }
    }
}
