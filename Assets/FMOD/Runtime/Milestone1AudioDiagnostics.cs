using System;
using UnityEngine;

namespace Capstone.Audio
{
    /// <summary>Read-only observations for audio validation and profiling.</summary>
    public static class Milestone1AudioDiagnostics
    {
        public static event Action<string> Started;
        public static int ActiveInstanceCount { get; private set; }
        public static int StartedCount { get; private set; }
        public static string LastStartedEvent { get; private set; }

        internal static void RecordStarted(string eventPath)
        {
            ActiveInstanceCount++;
            StartedCount++;
            LastStartedEvent = eventPath;
            Action<string> observers = Started;
            if (observers == null)
                return;

            foreach (Action<string> observer in observers.GetInvocationList())
            {
                try { observer(eventPath); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }

        internal static void RecordReleased()
        {
            ActiveInstanceCount = Mathf.Max(0, ActiveInstanceCount - 1);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Started = null;
            ActiveInstanceCount = 0;
            StartedCount = 0;
            LastStartedEvent = null;
        }
    }
}
