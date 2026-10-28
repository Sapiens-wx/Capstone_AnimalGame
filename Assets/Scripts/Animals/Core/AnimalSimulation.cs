using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AnimalGame.Animals
{
    [DefaultExecutionOrder(50)]
    public sealed class AnimalSimulation : MonoBehaviour
    {
        private static readonly Dictionary<int, AnimalSimulation> scenes = new();
        private readonly List<AnimalAgent> animals = new();
        private readonly Dictionary<string, AnimalGroup> groups = new();
        public AnimalWorldQuery World { get; private set; }
        public float Time { get; private set; }
        private struct PendingHit
        {
            public Transform Target, Attacker;
            public float Damage, Due;
        }
        private readonly List<PendingHit> hits = new();
        public bool Hit(AnimalAgent attacker, Transform target, bool knockback, bool sparring = false)
        {
            if (sparring) return false;
            if (target == null || attacker == null || !attacker.IsInitialized || attacker.Health.IsDead
                || attacker.Runtime.Removed || target.gameObject.scene != gameObject.scene) return false;
            if (attacker.Perception.IsIgnoredThreat(target)) return false;
            AnimalAgent animal = target.GetComponent<AnimalAgent>();
            if (animal != null && (!animal.IsInitialized || animal.Health.IsDead || animal.Runtime.Removed
                || animal.IsHidden || !animal.Behaviour.CanBeHuntedBy(attacker))) return false;
            int id = target.GetInstanceID();
            if (attacker.Runtime.AttackReadyTimes.TryGetValue(id, out float ready) && Time < ready) return false;
            attacker.Runtime.AttackReadyTimes[id] = Time + 1f;
            hits.Add(new PendingHit { Target = target, Attacker = attacker.transform, Damage = attacker.Config.AttackDamage, Due = Time + 1f });
            if (animal != null) animal.NotifyThreat(attacker.transform);
            if (knockback)
            {
                Vector2 direction = (Vector2)(target.position - attacker.transform.position);
                if (direction.sqrMagnitude < .000001f) direction = attacker.transform.up;
                float distance = attacker.Config.KnockbackDistance(attacker.Config.AttackDamage);
                if (animal != null) animal.Motor.Knockback(attacker.Map.WorldDirectionToMapDirection(direction), distance);
                else if (target.TryGetComponent(out AnimalGame.RobotMap.PlayerCombatReceiver player))
                    player.Knockback(direction, attacker.Map.MapMetersToWorldDistance(direction.normalized, distance));
            }
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry() { scenes.Clear(); }

        public static AnimalSimulation ForScene(Scene scene)
        {
            if (scenes.TryGetValue(scene.handle, out AnimalSimulation value) && value != null) return value;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                value = root.GetComponentInChildren<AnimalSimulation>();
                if (value != null) { value.Initialize(); return value; }
            }
            GameObject host = new GameObject("Animal Simulation");
            SceneManager.MoveGameObjectToScene(host, scene);
            value = host.AddComponent<AnimalSimulation>();
            value.Initialize();
            return value;
        }

        private void Awake() { Initialize(); }
        private void Initialize()
        {
            if (World != null) return;
            World = new AnimalWorldQuery(gameObject.scene);
            scenes[gameObject.scene.handle] = this;
        }
        public void Register(AnimalAgent animal)
        {
            if (!animals.Contains(animal)) animals.Add(animal);
        }
        public void Unregister(AnimalAgent animal) { animals.Remove(animal); }
        public AnimalGroup GetGroup(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            if (!groups.TryGetValue(id, out AnimalGroup group)) groups.Add(id, group = new AnimalGroup());
            return group;
        }
        private void Update() { Tick(UnityEngine.Time.deltaTime); }
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f) return;
            Time += deltaTime;
            World.Resolve(Time);
            for (int i = hits.Count - 1; i >= 0; i--)
            {
                PendingHit hit = hits[i];
                if (Time < hit.Due) continue;
                hits.RemoveAt(i);
                if (hit.Target == null || !hit.Target.gameObject.activeInHierarchy) continue;
                if (hit.Target.TryGetComponent(out AnimalAgent animal))
                {
                    if (hit.Attacker == null || !hit.Attacker.TryGetComponent(out AnimalAgent attacker)
                        || !animal.Behaviour.CanBeHuntedBy(attacker)) continue;
                    animal.ReceiveDamage(hit.Damage, hit.Attacker);
                }
                else if (hit.Target.TryGetComponent(out AnimalGame.RobotMap.PlayerCombatReceiver player)) player.Damage(hit.Damage);
            }
            foreach (AnimalGroup group in groups.Values) group.Tick(Time);
            // Disabling/destroying an agent during Tick may remove it from this list.
            for (int i = animals.Count - 1; i >= 0; i--)
                if (i < animals.Count && animals[i] != null && animals[i].isActiveAndEnabled)
                    animals[i].SimulationTick(deltaTime);
        }
        private void OnDestroy()
        {
            if (scenes.TryGetValue(gameObject.scene.handle, out AnimalSimulation current) && current == this)
                scenes.Remove(gameObject.scene.handle);
        }
    }
}
