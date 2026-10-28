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
        private Milestone3WorldAudio world;
        private Milestone3WorldMotionSampler worldMotionSampler;
        private Milestone3ScanAudio scan;
        private RobotMover mover;
        private RobotArmController arm;
        private RobotTumbleController tumble;
        private RobotSelfRightingController righting;
        private ScanChargeUI scanSource;
        private BioScanController hitSource;
        private bool initialized, applicationPaused, timePaused, shuttingDown;
        private bool needsScanChargeSource, needsScanHitSource;
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
            // Unbound extensions do not discover world objects or scan UI.
            world = config.HasWorldEvents ? new Milestone3WorldAudio(config, voices) : null;
            if (world != null && world.NeedsMotionSampling)
            {
                if (worldMotionSampler == null)
                    worldMotionSampler = gameObject.AddComponent<Milestone3WorldMotionSampler>();
                worldMotionSampler.Bind(world);
            }
            scan = config.HasScanEvents ? new Milestone3ScanAudio(config, voices) : null;
            needsScanChargeSource = config.HasCue(Milestone3Cue.ScanDeploy) || config.HasCue(Milestone3Cue.ScanReady);
            needsScanHitSource = config.HasCue(Milestone3Cue.ScanHit);
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
                ReleaseBindings(preserveScanHitState: true);
                if (!timePaused) Discover();
            }
            if (timePaused) return;
            if (Time.unscaledTime >= nextDiscovery && (!Usable(mover) || !Usable(arm)
                || !Usable(tumble) || !Usable(righting) || (scan != null
                    && ((needsScanChargeSource && !Usable(scanSource))
                        || (needsScanHitSource && !Usable(hitSource)))))) Discover();
        }
        private void LateUpdate()
        {
            if (!initialized || shuttingDown || applicationPaused || timePaused || Time.timeScale <= 0f) return;
            balance.Tick(Time.deltaTime);
            garbage.Tick(Time.deltaTime);
            world?.Tick(Time.deltaTime);
            scan?.Tick(Time.deltaTime);
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
            world?.Bind(mover, arm);
            worldMotionSampler?.Bind(world);
            if (scan != null)
            {
                if (needsScanChargeSource && !Usable(scanSource)) scanSource = FindScanSource();
                hitSource = null;
                if (needsScanHitSource && mover != null) mover.TryGetComponent(out hitSource);
                scan.Bind(scanSource, hitSource);
            }
        }
        private ScanChargeUI FindScanSource()
        {
            Scene preferred = mover != null ? mover.gameObject.scene : SceneManager.GetActiveScene();
            ScanChargeUI fallback = null;
            foreach (var candidate in FindObjectsByType<ScanChargeUI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!Usable(candidate)) continue;
                if (candidate.gameObject.scene == preferred) return candidate;
                if (fallback == null) fallback = candidate;
            }
            return fallback;
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
        private void ReleaseBindings(bool preserveScanHitState = false)
        {
            balance?.Bind(null, null, null);
            garbage?.Bind(null, null);
            world?.Bind(null, null);
            worldMotionSampler?.Bind(null);
            if (preserveScanHitState) scan?.Suspend();
            else scan?.Bind(null);
            voices?.StopAllImmediately();
            mover = null; arm = null; tumble = null; righting = null; scanSource = null; hitSource = null;
            nextDiscovery = 0f;
        }
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single) ReleaseBindings();
            Discover();
        }
        private void OnSceneUnloaded(Scene scene)
        {
            if (mover == null || mover.gameObject.scene == scene
                || (scanSource != null && scanSource.gameObject.scene == scene)) ReleaseBindings();
        }
        private void OnApplicationPause(bool value)
        {
            applicationPaused = value;
            if (value) ReleaseBindings(preserveScanHitState: true); else Discover();
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
