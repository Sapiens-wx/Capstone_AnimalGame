using System.Runtime.CompilerServices;
using AnimalGame.Garbage;

namespace AnimalGame.MainUI.Inventory
{
    /// <summary>Only the successful WorldInteraction.Recycle path calls this bridge.</summary>
    public static class GarbageInventoryRewards
    {
        private sealed class Receipt { }
        // Weak keys do not retain destroyed garbage across levels. Multiple calls in the same
        // completion/destruction frame must never grant a second reward.
        private static readonly ConditionalWeakTable<GarbageItem, Receipt> receipts = new();

        public static void Grant(GarbageItem garbage)
        {
            if (garbage == null || !UnityEngine.Application.isPlaying || receipts.TryGetValue(garbage, out _)) return;
            receipts.Add(garbage, new Receipt());
            MainUI.Active?.GrantRecycleRewards(garbage.Size);
        }
    }
}
