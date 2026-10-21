using AnimalGame.RobotArm;
using AnimalGame.RobotMap;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Capstone.Audio
{
    // Observe after arm corrections (-50), pulling (100) and height detection (150).
    // M1 owns the listener; this runtime must not add a competing StudioListener.
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public sealed class Milestone2ActionRuntime : MonoBehaviour
    {
        private static Milestone2ActionRuntime instance;
        private Milestone2ActionSettings settings;
        private Milestone2ActionVoices voices;
        private Milestone2MovementAudio movement;
        private Milestone2ArmAudio arm;
        private float nextDiscoveryTime;
        private bool initialized, shuttingDown, paused;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { instance = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateRuntime()
        {
            var config = Resources.Load<Milestone2ActionSettings>("Milestone2ActionSettings");
            // An empty delivery configuration costs no discovery or
            // per-frame work until at least one delivered event is GUID-bound.
            if (config == null || !config.Enabled || !config.HasAssignedEvents) return;
            var existing = FindFirstObjectByType<Milestone2ActionRuntime>();
            if (existing != null)
            {
                instance = existing;
                existing.Initialize(config);
                return;
            }
            new GameObject("Milestone 2 Player FMOD Audio").AddComponent<Milestone2ActionRuntime>();
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
            Initialize(Resources.Load<Milestone2ActionSettings>("Milestone2ActionSettings"));
        }

        private void Initialize(Milestone2ActionSettings config)
        {
            if (initialized) ReleaseBindings();
            settings = config;
            shuttingDown = paused = false;
            if (settings == null || !settings.Enabled || !settings.HasAssignedEvents) { enabled = false; return; }
            if (!initialized)
            {
                voices = new Milestone2ActionVoices(settings);
                movement = new Milestone2MovementAudio(settings, voices);
                arm = new Milestone2ArmAudio(settings, voices);
                SceneManager.sceneLoaded += OnSceneLoaded;
                SceneManager.sceneUnloaded += OnSceneUnloaded;
                initialized = true;
            }
            enabled = true;
            DiscoverSources();
        }

        private void Update()
        {
            if (!initialized || shuttingDown || paused) return;
            if (Time.unscaledTime >= nextDiscoveryTime
                && (!IsUsable(movement.Source) || !IsUsable(arm.Source)
                    || !IsUsable(movement.HeightSource) || !IsUsable(movement.TumbleSource))) DiscoverSources();
        }

        private void LateUpdate()
        {
            if (!initialized || shuttingDown || paused) return;
            if (Time.timeScale <= 0f)
            {
                movement.StopAllImmediately();
                arm.StopAllImmediately();
                return;
            }
            float deltaTime = Mathf.Max(0f, Time.deltaTime);
            movement.Tick(deltaTime);
            arm.Tick(deltaTime);
            voices.Tick(Mathf.Max(0f, Time.unscaledDeltaTime));
        }

        private void DiscoverSources()
        {
            if (!initialized || !isActiveAndEnabled || shuttingDown) return;
            nextDiscoveryTime = Time.unscaledTime + 0.25f;
            RobotMover mover = movement.Source;
            if (!IsUsable(mover)) mover = FindMover();
            // Components are resolved on the selected robot only, never by two
            // unrelated global searches that could bind an NPC or outgoing scene.
            RobotArmController armSource = null;
            RobotHeightMotionDetector heightSource = null;
            RobotTumbleController tumbleSource = null;
            if (mover != null)
            {
                mover.TryGetComponent(out armSource);
                mover.TryGetComponent(out heightSource);
                mover.TryGetComponent(out tumbleSource);
            }
            movement.Bind(mover, heightSource, tumbleSource);
            arm.Bind(armSource, mover);
        }

        private static RobotMover FindMover()
        {
            Scene active = SceneManager.GetActiveScene();
            RobotMover fallback = null;
            foreach (RobotMover candidate in FindObjectsByType<RobotMover>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!IsUsable(candidate)) continue;
                if (candidate.gameObject.scene == active) return candidate;
                if (fallback == null) fallback = candidate;
            }
            return fallback;
        }

        private static bool IsUsable(Behaviour source) => source != null && source.isActiveAndEnabled;

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single) ReleaseBindings();
            DiscoverSources();
        }

        private void OnSceneUnloaded(Scene scene)
        {
            if (movement?.Source == null || movement.Source.gameObject.scene == scene) ReleaseBindings();
            nextDiscoveryTime = 0f;
        }

        private void ReleaseBindings()
        {
            movement?.Bind(null, null);
            arm?.Bind(null, null);
            voices?.StopAllImmediately();
        }

        private void OnApplicationPause(bool isPaused)
        {
            paused = isPaused;
            if (isPaused) ReleaseBindings();
            else DiscoverSources();
        }

        private void OnApplicationQuit() { shuttingDown = true; ReleaseBindings(); }
        private void OnEnable() { DiscoverSources(); }
        private void OnDisable() { ReleaseBindings(); }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            ReleaseBindings();
            if (instance == this) instance = null;
        }
    }
}
