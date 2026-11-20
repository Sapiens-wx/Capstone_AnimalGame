using System.Collections.Generic;
using AnimalGame.Tutorial;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

namespace AnimalGame.Editor
{
    /// <summary>Real virtual-device confirmation after observing a goal remain on its own card.</summary>
    internal static class TutorialContinueSmokeInput
    {
        private sealed class Pending { public string Key; public int Frame; }
        private static readonly Dictionary<int, Pending> pending = new();
        private static readonly Dictionary<int, HashSet<string>> confirmedCards = new();

        public static int ConfirmationCount(TutorialDialoguePanel panel) => panel != null
            && confirmedCards.TryGetValue(panel.GetInstanceID(), out HashSet<string> cards) ? cards.Count : 0;

        public static void Apply(ref GamepadState state, TutorialDialoguePanel panel, string key)
        {
            if (panel == null) return;
            int id = panel.GetInstanceID();
            if (!panel.CanContinue || Time.timeScale <= 0f)
            { pending.Remove(id); return; }
            if (!pending.TryGetValue(id, out Pending item) || item.Key != key)
            { item = new Pending { Key = key, Frame = Time.frameCount }; pending[id] = item; }
            // Delay at least twelve gameplay frames, then press for two frames and release.
            // The real model/controller must remain pending until this physical A press.
            int elapsed = Time.frameCount - item.Frame;
            if (elapsed >= 12 && (elapsed - 12) % 16 < 2)
            {
                state = state.WithButton(GamepadButton.South);
                if (!confirmedCards.TryGetValue(id, out HashSet<string> cards))
                { cards = new HashSet<string>(); confirmedCards[id] = cards; }
                cards.Add(key);
            }
        }
    }
}
