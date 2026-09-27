using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.Animals
{
    /// <summary>Pauses animal simulation without changing the game's clock.</summary>
    public static class AnimalSimulation
    {
        private static readonly HashSet<PauseHandle> pauses = new HashSet<PauseHandle>();

        public static bool IsPaused => pauses.Count > 0;

        /// <summary>The caller must dispose its handle when it no longer needs the pause.</summary>
        public static IDisposable AcquirePause()
        {
            var handle = new PauseHandle();
            pauses.Add(handle);
            return handle;
        }

        // Also reset when entering Play Mode with domain reload disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            pauses.Clear();
        }

        private sealed class PauseHandle : IDisposable
        {
            public void Dispose()
            {
                // Removal is idempotent; stale handles cannot release newer pauses.
                pauses.Remove(this);
            }
        }
    }
}
