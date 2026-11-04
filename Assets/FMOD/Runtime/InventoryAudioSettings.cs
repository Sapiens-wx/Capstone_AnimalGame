using UnityEngine;

namespace Capstone.Audio
{
    [CreateAssetMenu(menuName = "Audio/Inventory Audio Settings")]
    public sealed class InventoryAudioSettings : ScriptableObject
    {
        [SerializeField] private bool enableAudio = true;
        [SerializeField] private AudioCueSettings[] cues = CreateCues();
        internal bool Enabled => enableAudio;
        internal AudioCueSettings GetCue(InventoryCue cue) => cues[(int)cue];
        internal bool HasAssignedEvents
        {
            get
            {
                for (int i = 0; i < InventoryAudioCatalog.CueCount; i++)
                    if (InventoryAudioCatalog.HasObservableTrigger(i) && !cues[i].EventReference.Guid.IsNull)
                        return true;
                return false;
            }
        }
        private static AudioCueSettings[] CreateCues()
        {
            var result = new AudioCueSettings[InventoryAudioCatalog.CueCount];
            for (int i = 0; i < result.Length; i++) result[i] = new AudioCueSettings();
            return result;
        }
    }

    internal enum InventoryCue { Open, Close, Navigate, MoveSelect, MoveConfirm, Discard }

    public static class InventoryAudioCatalog
    {
        public const int CueCount = 6;
        public const string BankName = "RobotFoley";
        internal const string SourceDirectory = "FMOD/SourceAudio/Milestone3/UI/Inventory/";
        private static readonly string[] Names = { "Open", "Close", "Navigate", "MoveSelect", "MoveConfirm", "Discard" };
        private static readonly string[] Files = {
            "C01_Inventory_Open.wav", "C02_Inventory_Close.wav", "C03_Inventory_Navigate.wav",
            "C04_Inventory_Move_Select.wav", "C05_Inventory_Move_Confirm.wav", "C06_Inventory_Discard.wav"
        };
        public static string EventPath(int index) => "event:/Robot/Inventory/" + Names[index];
        internal static string SourceFile(int index) => SourceDirectory + Files[index];
        // Discard observes actual item quantity loss through InventoryStore.Changed.
        public static bool HasObservableTrigger(int index) => index >= 0 && index < CueCount;
    }
}
