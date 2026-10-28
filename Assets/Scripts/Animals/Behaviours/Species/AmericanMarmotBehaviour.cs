using AnimalGame.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AnimalGame.Animals
{
    [DisallowMultipleComponent]
    public sealed class AmericanMarmotBehaviour : GenericAnimalBehaviour
    {
        [SerializeField] private AnimalBurrow burrowPrefab;
        [SerializeField, Min(0f)] private float safeEmergenceSeconds = 5f;
        [SerializeField, Min(0f)] private float alarmDelaySeconds = 1f;
        public AnimalBurrow HomeBurrow { get; private set; }
        public AnimalBurrow HidingBurrow { get; set; }
        private float safeTime, alertTime;
        private bool alarmPlayed;
        public override bool SupportsAggression => false;
        public override Vector2 ActivityCenter => Agent.HomeMapPosition;
        public override void Initialize(AnimalAgent agent)
        {
            base.Initialize(agent);
            HomeBurrow = Instantiate(burrowPrefab, transform.position, Quaternion.identity);
            SceneManager.MoveGameObjectToScene(HomeBurrow.gameObject, gameObject.scene);
            HomeBurrow.name = name + " Burrow";
        }
        public override AnimalAction CreateEscapeAction() => new BurrowAction(0f, true);
        public override void EnterDaily()
        {
            base.EnterDaily();
            if (Vector2.Distance(Motor.CurrentMapPosition, Agent.HomeMapPosition) > Config.ActivityRadiusMeters)
                BeginRuntimeAction(new BurrowAction(.1f, false, true));
        }
        public override void EnterCurious() { alertTime = 0f; alarmPlayed = false; }
        public override void TickCurious(float dt)
        {
            base.TickCurious(dt);
            alertTime += dt;
            if (!alarmPlayed && alertTime >= alarmDelaySeconds)
            { alarmPlayed = true; Agent.SoundEmitter?.Emit(AnimalSoundKind.Alarm); }
        }
        public bool EntranceIsSafe()
        {
            Vector2 entrance = Motor.CurrentMapPosition;
            float radius = Config.AlertRadiusMeters;
            if (Agent.Perception.TryGetPlayerMapPosition(out Vector2 player) && Vector2.Distance(player, entrance) <= radius) return false;
            foreach (AnimalAgent predator in AnimalAgent.ActiveAgents)
                if (predator != Agent && predator != null && predator.gameObject.scene == gameObject.scene
                    && predator.IsPresent && predator.Config.Eats(Config)
                    && Vector2.Distance(predator.Motor.CurrentMapPosition, entrance) <= radius) return false;
            // Explicit threats can include actors without a species configuration.
            foreach (Transform threat in Agent.Runtime.Threats)
                if (threat != null && threat.gameObject.activeInHierarchy && !Agent.Perception.IsIgnoredThreat(threat)
                    && (!threat.TryGetComponent(out AnimalAgent animal) || animal.IsPresent)
                    && Vector2.Distance(InteractionShape.ToQuery(threat.position, Agent.Map), entrance) <= radius) return false;
            return Motor.CanOccupyMapPosition(entrance);
        }
        public override void EnterHiding() { safeTime = 0f; Agent.SetConcealed(true); }
        public override void TickHiding(float dt)
        {
            safeTime = EntranceIsSafe() ? safeTime + dt : 0f;
            if (safeTime >= safeEmergenceSeconds) Agent.CompleteHiding();
        }
        public override void ExitHiding() { Agent.SetConcealed(false); }
    }
}
