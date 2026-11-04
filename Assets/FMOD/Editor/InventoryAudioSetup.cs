using System;
using System.IO;
using FMODUnity;
using UnityEditor;
using UnityEngine;

namespace Capstone.Audio.Editor
{
    public static class InventoryAudioSetup
    {
        private const string ConfigPath = "Assets/FMOD/Resources/InventoryAudioSettings.asset";
        [MenuItem("FMOD/Milestone 3/Bind Delivered Inventory Events", priority = 121)]
        public static void BindDeliveredEvents()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Bind delivered banks from Edit Mode.");
            EventManager.RefreshBanks();
            var events = new EditorEventRef[InventoryAudioCatalog.CueCount];
            int bound = 0;
            for (int i = 0; i < events.Length; i++)
            {
                if (!InventoryAudioCatalog.HasObservableTrigger(i)) continue;
                events[i] = EventManager.EventFromPath(InventoryAudioCatalog.EventPath(i));
                if (events[i] == null) continue;
                if (events[i].Is3D) throw new InvalidOperationException("Inventory UI must be 2D: " + events[i].Path);
                bound++;
            }
            if (bound == 0) { Debug.Log("[Inventory Audio] No delivered inventory events. Configuration unchanged."); return; }
            if (!File.Exists(Path.GetFullPath(Path.Combine(Application.dataPath, "../FMOD/Banks/Desktop/RobotFoley.bank"))))
                throw new InvalidOperationException("Publish RobotFoley.bank and matching Master.strings.bank before binding.");
            var config = AssetDatabase.LoadAssetAtPath<InventoryAudioSettings>(ConfigPath);
            bool created = config == null;
            if (created)
            {
                config = ScriptableObject.CreateInstance<InventoryAudioSettings>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            Undo.RecordObject(config, "Bind delivered inventory audio");
            var serialized = new SerializedObject(config);
            var cues = serialized.FindProperty("cues");
            int previousCount = created ? 0 : cues.arraySize;
            cues.arraySize = InventoryAudioCatalog.CueCount;
            for (int i = 0; i < cues.arraySize; i++)
            {
                var cue = cues.GetArrayElementAtIndex(i);
                var reference = cue.FindPropertyRelative("eventReference");
                if (i >= previousCount)
                {
                    reference.FindPropertyRelative("Path").stringValue = InventoryAudioCatalog.EventPath(i);
                    reference.FindPropertyRelative("Guid").boxedValue = default(FMOD.GUID);
                    cue.FindPropertyRelative("gainDb").floatValue = 0f;
                    cue.FindPropertyRelative("readBySoundUI").boolValue = false;
                    cue.FindPropertyRelative("soundUIStrength").floatValue = 0f;
                }
                if (events[i] == null) continue;
                reference.FindPropertyRelative("Path").stringValue = events[i].Path;
                reference.FindPropertyRelative("Guid").boxedValue = events[i].Guid;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(config);
            if (!Settings.Instance.Banks.Contains(InventoryAudioCatalog.BankName))
            {
                Undo.RecordObject(Settings.Instance, "Load inventory audio bank");
                Settings.Instance.Banks.Add(InventoryAudioCatalog.BankName);
                EditorUtility.SetDirty(Settings.Instance);
                AssetDatabase.SaveAssetIfDirty(Settings.Instance);
            }
            Debug.Log("[Inventory Audio] Bound " + bound + "/6 observable cues. C06 follows actual inventory item quantity loss.");
        }
    }
}
