using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.MainUI
{
    /// <summary>One BeginSound call owns one handle. Default and already-ended handles are safe to end.</summary>
    public readonly struct SoundHandle
    {
        internal readonly long Id;
        internal SoundHandle(long id) { Id = id; }
    }

    /// <summary>Main-thread playback notifications, independent of the HUD's visibility and lifetime.</summary>
    public static class SoundUIPlayback
    {
        private static readonly Dictionary<long, float> sounds = new Dictionary<long, float>();
        private static long nextId;
        public static int ActiveCount => sounds.Count;
        public static float MaximumStrength { get; private set; }
        internal static long Session { get; private set; }
        internal static double SessionStartedAt { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            sounds.Clear();
            MaximumStrength = 0f;
            SessionStartedAt = 0;
            // Keep IDs monotonic when domain reload is disabled: an old handle
            // must never end a newly started sound in the next play session.
            Session++;
        }

        public static SoundHandle BeginSound(float strength)
        {
            strength = float.IsNaN(strength) || float.IsInfinity(strength) ? 0f : Mathf.Clamp01(strength);
            if (sounds.Count == 0)
            {
                Session++;
                SessionStartedAt = Time.timeAsDouble;
            }
            var handle = new SoundHandle(++nextId);
            sounds.Add(handle.Id, strength);
            MaximumStrength = Mathf.Max(MaximumStrength, strength);
            return handle;
        }

        public static void EndSound(SoundHandle handle)
        {
            if (!sounds.Remove(handle.Id)) return;
            MaximumStrength = 0f;
            foreach (float strength in sounds.Values)
                MaximumStrength = Mathf.Max(MaximumStrength, strength);
        }
    }

    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class MainUISoundController : MonoBehaviour
    {
        [SerializeField] private UIDocument document;
        [Header("Wave shape")]
        [Tooltip("Samples in one continuous Painter2D path. Buffers are reused until this count changes.")]
        [SerializeField, Range(16, 512)] private int pointCount = 97;
        [Tooltip("Sine cycles across the full width.")]
        [SerializeField, Range(0.5f, 16f)] private float frequency = 5f;
        [Tooltip("Constant phase speed in cycles per game second. Zero stops motion; game pause freezes phase.")]
        [SerializeField, Min(0f)] private float phaseSpeed = 1f;
        [Tooltip("Maximum wave height as a fraction of the available half-height.")]
        [SerializeField, Range(0f, 1f)] private float maximumAmplitude = 0.85f;
        [SerializeField, Min(0.1f)] private float waveLineWidth = 2f;
        [Header("Amplitude transition")]
        [Tooltip("Exponential smoothing time in seconds, on both rising and falling amplitude. Zero is immediate.")]
        [SerializeField, Min(0f)] private float amplitudeSmoothingSeconds = 0.08f;
        [Header("Damping")]
        [Tooltip("Initial Gaussian damping: larger values make the initial wave narrower. Zero spans the width.")]
        [SerializeField, Range(0f, 512f)] private float initialDamping = 120f;
        [Tooltip("Continuous playback seconds until damping reaches zero. Overlapping sounds do not restart this timer.")]
        [SerializeField, Min(0f)] private float expansionSeconds = 2f;

        private VisualElement root;
        private SoundElement element;
        private long session = -1;
        private float elapsed, amplitude, damping, phase;

        public int ActiveSoundCount => SoundUIPlayback.ActiveCount;
        public float TargetAmplitude => SoundUIPlayback.MaximumStrength;
        public float CurrentAmplitude => amplitude;
        public float CurrentDamping => damping;
        public float CurrentPhase => phase;

        private void OnEnable()
        {
            if (document == null) document = GetComponent<UIDocument>();
            session = -1;
            amplitude = 0f;
            damping = initialDamping;
            root = null;
            element = null;
        }

        private void LateUpdate()
        {
            Advance(Time.deltaTime);
            if (document == null || !document.isActiveAndEnabled) return;
            VisualElement nextRoot = document.rootVisualElement;
            if (root != nextRoot || element == null)
            {
                root = nextRoot;
                element = root?.Q<SoundElement>("Sound");
            }
            // Playback state keeps advancing even when the document is hidden.
            if (element == null || element.panel == null || !element.visible
                || element.resolvedStyle.display == DisplayStyle.None) return;
            element.ConfigureWaveform(pointCount, frequency, maximumAmplitude, waveLineWidth);
            element.SetWaveState(amplitude, damping);
            element.SetWavePhase(phase);
        }

        private void Advance(float deltaTime)
        {
            deltaTime = Mathf.Max(0f, deltaTime);
            // Advance even while silent or hidden, without resetting on sound edges.
            // Keep radians bounded to preserve precision during long sessions.
            if (deltaTime > 0f && phaseSpeed > 0f)
                phase = Mathf.Repeat(phase + 2f * Mathf.PI * phaseSpeed * deltaTime, 2f * Mathf.PI);
            if (SoundUIPlayback.ActiveCount > 0)
            {
                if (session != SoundUIPlayback.Session)
                {
                    session = SoundUIPlayback.Session;
                    elapsed = (float)System.Math.Max(0, Time.timeAsDouble - SoundUIPlayback.SessionStartedAt);
                }
                else elapsed += deltaTime;
                float progress = expansionSeconds > 0f ? Mathf.Clamp01(elapsed / expansionSeconds) : 1f;
                damping = initialDamping * (1f - progress) * (1f - progress);
            }

            float target = SoundUIPlayback.MaximumStrength;
            float blend = amplitudeSmoothingSeconds > 0f
                ? 1f - Mathf.Exp(-deltaTime / amplitudeSmoothingSeconds) : 1f;
            amplitude = Mathf.Lerp(amplitude, target, blend);
            if (Mathf.Abs(amplitude - target) < 0.0001f) amplitude = target;
            // Preserve the last width during the release, then prepare a narrow onset.
            if (SoundUIPlayback.ActiveCount == 0 && amplitude == 0f)
            {
                elapsed = 0f;
                damping = initialDamping;
            }
        }

        private void OnDisable()
        {
            element?.SetWaveState(0f, initialDamping);
            root = null;
            element = null;
        }
    }
}
