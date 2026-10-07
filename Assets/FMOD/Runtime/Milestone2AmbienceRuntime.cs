using System;
using System.IO;
using System.Linq;
using FMODUnity;
using UnityEngine;

namespace Capstone.Audio
{
    [DisallowMultipleComponent]
    public sealed class Milestone2AmbienceRuntime : MonoBehaviour
    {
        public const string SourceRelativePath = "FMOD/SourceAudio/Milestone2/NatureAmbience";
        public const string StreamingRelativePath = "Milestone2/NatureAmbience";

        [SerializeField, Range(0f, 1f)] private float volume = 1f;

        private static Milestone2AmbienceRuntime instance;
        private FMOD.Sound sound;
        private FMOD.Channel channel;
        private FMOD.Studio.Bus masterBus;
        private bool busLocked;
        private bool shuttingDown;

        public string SelectedFile { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateRuntime()
        {
            var existing = FindFirstObjectByType<Milestone2AmbienceRuntime>();
            if (existing != null)
            {
                instance = existing;
                existing.shuttingDown = false;
                if (!existing.channel.hasHandle())
                    existing.PlayRandomAmbience();
                return;
            }

            new GameObject("Milestone 2 Nature Ambience").AddComponent<Milestone2AmbienceRuntime>();
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
        }

        private void OnEnable()
        {
            if (instance == this)
                PlayRandomAmbience();
        }

        public static bool IsAudioFile(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".wav":
                case ".ogg":
                case ".mp3":
                case ".flac":
                case ".aif":
                case ".aiff":
                    return true;
                default:
                    return false;
            }
        }

        private void PlayRandomAmbience()
        {
            if (shuttingDown || !isActiveAndEnabled || instance != this)
                return;

            StopAmbience();

#if UNITY_EDITOR
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", SourceRelativePath));
#else
            string directory = Path.Combine(Application.streamingAssetsPath, StreamingRelativePath);
#endif

            string[] files;
            try
            {
                if (!Directory.Exists(directory))
                    return;
                files = Directory.GetFiles(directory).Where(IsAudioFile).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Milestone 2 Audio] Cannot read nature ambience: " + exception.Message);
                return;
            }

            // No placeholder sound is needed while the source folder is empty.
            if (files.Length == 0)
                return;

            int first = UnityEngine.Random.Range(0, files.Length);
            for (int offset = 0; offset < files.Length; ++offset)
            {
                string file = files[(first + offset) % files.Length];
                try
                {
                    var core = RuntimeManager.CoreSystem;
                    // This file stream needs bus metadata, not other events' sample data.
                    masterBus = RuntimeManager.GetBus("bus:/");
                    Check(masterBus.lockChannelGroup(), "Lock master bus");
                    busLocked = true;
                    Check(RuntimeManager.StudioSystem.flushCommands(), "Flush master bus creation");
                    Check(masterBus.getChannelGroup(out FMOD.ChannelGroup group), "Get master bus channel group");

                    Check(core.createStream(file, FMOD.MODE.LOOP_NORMAL | FMOD.MODE._2D, out sound), "Open " + Path.GetFileName(file));
                    Check(core.playSound(sound, group, true, out channel), "Create ambience channel");
                    Check(channel.setVolume(volume), "Set ambience volume");
                    Check(channel.setPaused(false), "Start ambience");
                    SelectedFile = Path.GetFileName(file);
                    Debug.Log("[Milestone 2 Audio] Looping nature ambience: " + SelectedFile);
                    return;
                }
                catch (Exception exception)
                {
                    StopAmbience();
                    Debug.LogWarning("[Milestone 2 Audio] " + exception.Message);
                }
            }
        }

        private static void Check(FMOD.RESULT result, string operation)
        {
            if (result != FMOD.RESULT.OK)
                throw new InvalidOperationException(operation + ": " + FMOD.Error.String(result));
        }

        private void StopAmbience()
        {
            // FMOD may already have been destroyed when Unity shuts down.
            if (RuntimeManager.IsInitialized)
            {
                if (channel.hasHandle())
                    channel.stop();
                if (sound.hasHandle())
                    sound.release();
                if (busLocked && masterBus.isValid())
                    masterBus.unlockChannelGroup();
            }

            channel.clearHandle();
            sound.clearHandle();
            masterBus.clearHandle();
            busLocked = false;
            SelectedFile = null;
        }

        private void OnValidate()
        {
            if (RuntimeManager.IsInitialized && channel.hasHandle())
                channel.setVolume(volume);
        }

        private void OnDisable()
        {
            StopAmbience();
        }

        private void OnApplicationQuit()
        {
            shuttingDown = true;
            StopAmbience();
        }

        private void OnDestroy()
        {
            StopAmbience();
            if (instance == this)
                instance = null;
        }
    }
}
