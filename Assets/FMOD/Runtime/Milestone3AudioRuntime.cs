using AnimalGame.RobotArm;
using AnimalGame.RobotMap;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Capstone.Audio
{
    [DefaultExecutionOrder(210)]
    [DisallowMultipleComponent]
    public sealed class Milestone3AudioRuntime : MonoBehaviour
    {
        private static Milestone3AudioRuntime instance;
        private Milestone3AudioSettings settings;
        private Milestone3AudioVoices voices;
        private Milestone3BalanceAudio balance;
        private Milestone3GarbageAudio garbage;
        private RobotMover mover;
        private RobotArmController arm;
        private RobotTumbleController tumble;
        private RobotSelfRightingController righting;
        private bool initialized, applicationPaused, timePaused, shuttingDown;
        private float nextDiscovery;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { instance = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateRuntime()
        {
            var config = Resources.Load<Milestone3AudioSettings>("Milestone3AudioSettings");
            // Empty or only-reserved slots create no object, scan, or Update work.
            if (config == null || !config.Enabled || !config.HasAssignedEvents) return;
            var existing = FindFirstObjectByType<Milestone3AudioRuntime>();
            if (existing != null) { instance = existing; existing.Initialize(config); return; }
            new GameObject("Milestone 3 FMOD Foley").AddComponent<Milestone3AudioRuntime>();
        }
        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
            Initialize(Resources.Load<Milestone3AudioSettings>("Milestone3AudioSettings"));
        }
        private void Initialize(Milestone3AudioSettings config)
        {
            ReleaseBindings();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            settings = config;
            initialized = false;
            shuttingDown = applicationPaused = timePaused = false;
            if (config == null || !config.Enabled || !config.HasAssignedEvents) { enabled = false; return; }
            voices = new Milestone3AudioVoices(config);
            balance = new Milestone3BalanceAudio(config, voices);
            garbage = new Milestone3GarbageAudio(config, voices);
            initialized = true;
            enabled = true;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            Discover();
        }
        private void Update()
        {
            if (!initialized || shuttingDown || applicationPaused) return;
            bool nowPaused = Time.timeScale <= 0f;
            if (nowPaused != timePaused)
            {
                timePaused = nowPaused;
                ReleaseBindings();
                if (!timePaused) Discover();
            }
            if (timePaused) return;
            if (Time.unscaledTime >= nextDiscovery && (!Usable(mover) || !Usable(arm)
                || !Usable(tumble) || !Usable(righting))) Discover();
        }
        private void LateUpdate()
        {
            if (!initialized || shuttingDown || applicationPaused || timePaused || Time.timeScale <= 0f) return;
            balance.Tick(Time.deltaTime);
            garbage.Tick(Time.deltaTime);
            voices.Tick(Time.unscaledDeltaTime);
        }
        private void Discover()
        {
            if (!initialized || shuttingDown || applicationPaused || Time.timeScale <= 0f || !isActiveAndEnabled) return;
            nextDiscovery = Time.unscaledTime + 0.25f;
            if (!Usable(mover)) mover = FindMover();
            arm = null; tumble = null; righting = null;
            if (mover != null)
            {
                mover.TryGetComponent(out arm);
                mover.TryGetComponent(out tumble);
                mover.TryGetComponent(out righting);
            }
            balance.Bind(mover, tumble, righting);
            garbage.Bind(mover, arm);
        }
        private static RobotMover FindMover()
        {
            Scene active = SceneManager.GetActiveScene();
            RobotMover fallback = null;
            foreach (var candidate in FindObjectsByType<RobotMover>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!Usable(candidate)) continue;
                if (candidate.gameObject.scene == active) return candidate;
                if (fallback == null) fallback = candidate;
            }
            return fallback;
        }
        private static bool Usable(Behaviour source) => source != null && source.isActiveAndEnabled
            && source.gameObject.scene.IsValid() && source.gameObject.scene.isLoaded;
        internal static bool SuppressesOrdinaryLanding(RobotMover source) => instance != null
            && instance.isActiveAndEnabled && !instance.shuttingDown && !instance.applicationPaused
            && !instance.timePaused && instance.balance != null && instance.balance.SuppressesOrdinaryLanding(source);
        private void ReleaseBindings()
        {
            balance?.Bind(null, null, null);
            garbage?.Bind(null, null);
            voices?.StopAllImmediately();
            mover = null; arm = null; tumble = null; righting = null;
            nextDiscovery = 0f;
        }
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single) ReleaseBindings();
            Discover();
        }
        private void OnSceneUnloaded(Scene scene)
        {
            if (mover == null || mover.gameObject.scene == scene) ReleaseBindings();
        }
        private void OnApplicationPause(bool value)
        {
            applicationPaused = value;
            if (value) ReleaseBindings(); else Discover();
        }
        private void OnEnable() { Discover(); }
        private void OnDisable() { ReleaseBindings(); }
        private void OnApplicationQuit() { shuttingDown = true; ReleaseBindings(); }
        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            ReleaseBindings();
            if (instance == this) instance = null;
        }
    }
}
