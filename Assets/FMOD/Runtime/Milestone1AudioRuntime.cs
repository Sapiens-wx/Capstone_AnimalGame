using AnimalGame.RobotMap;
using FMODUnity;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Capstone.Audio
{
    // Bind before the photo controller's Update. LateUpdate observes completed
    // gameplay updates, including scan input, without altering their ordering.
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class Milestone1AudioRuntime : MonoBehaviour
    {
        private static Milestone1AudioRuntime instance;
        private Milestone1ScanAudio scanAudio;
        private Milestone1CameraAudio cameraAudio;
        private Transform listenerTransform;
        private Camera listenerCamera;
        private float nextDiscoveryTime;
        private bool initialized;
        private bool shuttingDown;

        // Audio-only diagnostics; inspecting these never changes gameplay state.
        public bool IsInitialized => initialized;
        public ScanChargeUI BoundScanSource => scanAudio?.Source;
        public PhotoModeController BoundCameraSource => cameraAudio?.Source;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateRuntime()
        {
            Milestone1AudioRuntime existing = FindFirstObjectByType<Milestone1AudioRuntime>();
            if (existing != null)
            {
                instance = existing;
                existing.Initialize();
                return;
            }
            var root = new GameObject("Milestone 1 FMOD Audio");
            root.AddComponent<Milestone1AudioRuntime>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
            Initialize();
        }

        private void Initialize()
        {
            if (initialized)
            {
                ReleaseBindings();
                shuttingDown = false;
                enabled = true;
                DiscoverSources();
                return;
            }

            shuttingDown = false;
            enabled = true;

            Milestone1AudioSettings settings = Resources.Load<Milestone1AudioSettings>(
                "Milestone1AudioSettings");
            if (settings == null)
            {
                Debug.LogWarning("[Milestone 1 Audio] Resources/Milestone1AudioSettings is missing. Audio is disabled.");
                enabled = false;
                return;
            }

            scanAudio = new Milestone1ScanAudio(settings);
            cameraAudio = new Milestone1CameraAudio(settings);
            var listenerObject = new GameObject("FMOD Listener");
            listenerTransform = listenerObject.transform;
            listenerTransform.SetParent(transform, false);
            listenerObject.AddComponent<StudioListener>();
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            initialized = true;
            DiscoverSources();
        }

        private void Update()
        {
            if (!initialized || shuttingDown)
                return;
            if (!IsUsable(scanAudio.Source) || !IsUsable(cameraAudio.Source))
            {
                if (Time.unscaledTime >= nextDiscoveryTime)
                    DiscoverSources();
            }
        }

        private void LateUpdate()
        {
            if (!initialized || shuttingDown)
                return;
            float deltaTime = Mathf.Max(0f, Time.unscaledDeltaTime);
            scanAudio.Tick(deltaTime);
            cameraAudio.Tick(deltaTime);

            if (listenerCamera == null || !listenerCamera.isActiveAndEnabled)
                listenerCamera = Camera.main;
            if (listenerCamera != null && listenerTransform != null)
            {
                listenerTransform.SetPositionAndRotation(
                    listenerCamera.transform.position, listenerCamera.transform.rotation);
            }
        }

        private void DiscoverSources()
        {
            if (!initialized || shuttingDown || !isActiveAndEnabled)
                return;
            nextDiscoveryTime = Time.unscaledTime + 0.25f;
            scanAudio.Bind(FindGameplaySource<ScanChargeUI>());
            cameraAudio.Bind(FindGameplaySource<PhotoModeController>());
            listenerCamera = Camera.main;
        }

        private static T FindGameplaySource<T>() where T : Behaviour
        {
            T fallback = null;
            Scene activeScene = SceneManager.GetActiveScene();
            T[] candidates = FindObjectsByType<T>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (T candidate in candidates)
            {
                if (!candidate.isActiveAndEnabled)
                    continue;
                if (candidate.gameObject.scene == activeScene)
                    return candidate;
                if (fallback == null)
                    fallback = candidate;
            }
            return fallback;
        }

        private static bool IsUsable(Behaviour source)
        {
            return source != null && source.isActiveAndEnabled;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!initialized || shuttingDown || !isActiveAndEnabled)
                return;
            if (mode == LoadSceneMode.Single)
                ReleaseBindings();
            DiscoverSources();
        }

        private void OnSceneUnloaded(Scene scene)
        {
            if (!initialized)
                return;
            if (scanAudio.Source == null || scanAudio.Source.gameObject.scene == scene)
                scanAudio.Bind(null);
            if (cameraAudio.Source == null || cameraAudio.Source.gameObject.scene == scene)
                cameraAudio.Bind(null);
            nextDiscoveryTime = 0f;
            listenerCamera = null;
        }

        private void ReleaseBindings()
        {
            scanAudio?.Bind(null);
            cameraAudio?.Bind(null);
            scanAudio?.StopAllImmediately();
            cameraAudio?.StopAllImmediately();
            listenerCamera = null;
        }

        private void OnApplicationQuit()
        {
            shuttingDown = true;
            ReleaseBindings();
        }

        private void OnEnable()
        {
            // OnEnable can run before initialization has finished. Discovery's
            // guard also keeps disabled runtimes out of scene-load callbacks.
            DiscoverSources();
        }

        private void OnDisable()
        {
            ReleaseBindings();
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            ReleaseBindings();
            if (instance == this)
                instance = null;
        }
    }
}
