using System;
using System.IO;
using FMODUnity;
using UnityEditor;
using UnityEngine;

namespace Capstone.Audio.Editor
{
    public static class Milestone3AudioSetup
    {
        private const string ConfigPath = "Assets/FMOD/Resources/Milestone3AudioSettings.asset";

        [MenuItem("FMOD/Milestone 3/Bind Delivered Foley Events", priority = 120)]
        public static void BindDeliveredEvents()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Bind delivered banks from Edit Mode.");
            EventManager.RefreshBanks();
            var events = new EditorEventRef[Milestone3AudioCatalog.CueCount];
            int bound = 0;
            for (int i = 0; i < events.Length; i++)
            {
                if (!Milestone3AudioCatalog.HasSeparateEvent(i)) continue;
                events[i] = EventManager.EventFromPath(Milestone3AudioCatalog.EventPath(i));
                if (events[i] == null) continue;
                if (events[i].Is3D) throw new InvalidOperationException("M3 robot foley must be 2D: " + events[i].Path);
                bound++;
            }
            if (bound == 0) { Debug.Log("[Milestone 3 Audio] No delivered RobotFoley events yet. Configuration unchanged."); return; }
            string bank = Path.GetFullPath(Path.Combine(Application.dataPath, "../FMOD/Banks/Desktop/RobotFoley.bank"));
            if (!File.Exists(bank)) throw new InvalidOperationException("Publish RobotFoley.bank and matching Master.strings.bank before binding.");
            var config = AssetDatabase.LoadAssetAtPath<Milestone3AudioSettings>(ConfigPath);
            bool created = config == null;
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<Milestone3AudioSettings>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            Undo.RecordObject(config, "Bind delivered milestone 3 foley");
            var serialized = new SerializedObject(config);
            SerializedProperty cues = serialized.FindProperty("cues");
            int previousCount = created ? 0 : cues.arraySize;
            cues.arraySize = Milestone3AudioCatalog.CueCount;
            for (int i = 0; i < cues.arraySize; i++)
            {
                // Unity duplicates the last array entry on growth. New, undelivered
                // slots must never inherit an older event GUID or its tuning.
                if (i >= previousCount)
                {
                    SerializedProperty cue = cues.GetArrayElementAtIndex(i);
                    SerializedProperty addedReference = cue.FindPropertyRelative("eventReference");
                    addedReference.FindPropertyRelative("Path").stringValue = Milestone3AudioCatalog.EventPath(i);
                    addedReference.FindPropertyRelative("Guid").boxedValue = default(FMOD.GUID);
                    cue.FindPropertyRelative("gainDb").floatValue = Milestone3AudioCatalog.DefaultGainDb(i);
                    cue.FindPropertyRelative("readBySoundUI").boolValue = true;
                    cue.FindPropertyRelative("soundUIStrength").floatValue = 0.5f;
                }
                if (events[i] == null) continue;
                SerializedProperty reference = cues.GetArrayElementAtIndex(i).FindPropertyRelative("eventReference");
                reference.FindPropertyRelative("Path").stringValue = events[i].Path;
                reference.FindPropertyRelative("Guid").boxedValue = events[i].Guid;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(config);
            if (!Settings.Instance.Banks.Contains(Milestone3AudioCatalog.BankName))
            {
                Undo.RecordObject(Settings.Instance, "Load delivered RobotFoley bank");
                Settings.Instance.Banks.Add(Milestone3AudioCatalog.BankName);
                EditorUtility.SetDirty(Settings.Instance);
                AssetDatabase.SaveAssetIfDirty(Settings.Instance);
            }
            Debug.Log("[Milestone 3 Audio] Bound " + bound + "/" + events.Length
                + " slots. G01/G04/G12 remain reserved pending public gameplay signals.");
        }
    }
}
