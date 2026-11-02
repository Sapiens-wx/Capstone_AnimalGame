using UnityEngine;
using UnityEngine.SceneManagement;
using InventoryUI = AnimalGame.MainUI.MainUI;

namespace Capstone.Audio
{
    [DefaultExecutionOrder(220)]
    [DisallowMultipleComponent]
    public sealed class InventoryAudioRuntime : MonoBehaviour
    {
        private static InventoryAudioRuntime instance;
        private InventoryAudioVoices voices;
        private InventoryAudioObserver observer;
        private InventoryUI source;
        private bool initialized, applicationPaused, shuttingDown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { instance = null; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateRuntime()
        {
            var config = Resources.Load<InventoryAudioSettings>("InventoryAudioSettings");
            // Empty or reserved GUIDs create no object, discovery or Update work.
            if (config == null || !config.Enabled || !config.HasAssignedEvents) return;
            var existing = FindFirstObjectByType<InventoryAudioRuntime>();
            if (existing != null) { instance = existing; existing.Initialize(config); return; }
            new GameObject("FMOD Inventory UI").AddComponent<InventoryAudioRuntime>();
        }
        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
            Initialize(Resources.Load<InventoryAudioSettings>("InventoryAudioSettings"));
        }
        private void Initialize(InventoryAudioSettings config)
        {
            ReleaseBindings();
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            initialized = false;
            applicationPaused = shuttingDown = false;
            if (config == null || !config.Enabled || !config.HasAssignedEvents) { enabled = false; return; }
            voices = new InventoryAudioVoices(config);
            observer = new InventoryAudioObserver(voices);
            initialized = true;
            enabled = true;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }
        private void LateUpdate()
        {
            if (!initialized || applicationPaused || shuttingDown) return;
            // Inventory owns zero time scale. Follow its unscaled UI animation
            // without unpausing existing gameplay audio.
            InventoryUI next = InventoryUI.Active;
            if (next != null && (!next.isActiveAndEnabled || !next.gameObject.scene.IsValid()
                || !next.gameObject.scene.isLoaded)) next = null;
            if (!ReferenceEquals(next, source)) { source = next; observer.Bind(source); }
            observer.Tick();
            voices.Tick(Time.unscaledTime);
        }
        private void ReleaseBindings()
        {
            observer?.Bind(null);
            voices?.StopAll();
            source = null;
        }
        private void OnSceneUnloaded(Scene scene)
        {
            if (source == null || source.gameObject.scene == scene) ReleaseBindings();
        }
        private void OnApplicationPause(bool value)
        {
            applicationPaused = value;
            if (value) ReleaseBindings();
        }
        private void OnDisable() { ReleaseBindings(); }
        private void OnApplicationQuit() { shuttingDown = true; ReleaseBindings(); }
        private void OnDestroy()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            ReleaseBindings();
            if (instance == this) instance = null;
        }
    }
}
