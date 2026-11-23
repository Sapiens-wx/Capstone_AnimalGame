using System;
using FMODUnity;
using UnityEditor;
using UnityEngine;

namespace Capstone.Audio.Editor
{
    public static class Milestone2ActionSetup
    {
        private const string ConfigPath = "Assets/FMOD/Resources/Milestone2ActionSettings.asset";

        [MenuItem("FMOD/Milestone 2/Bind Delivered Player Action Events", priority = 110)]
        public static void BindDeliveredEvents()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Bind delivered banks from Edit Mode.");
            EventManager.RefreshBanks();
            BindDeliveredEvents(terrainOnly: false);
        }

        // The M3 Foley binder has already refreshed the banks. These four
        // events belong to RobotFoley but share the existing wheel audio slots.
        internal static void BindTerrainEvents() => BindDeliveredEvents(terrainOnly: true);

        private static void BindDeliveredEvents(bool terrainOnly)
        {
            var config = AssetDatabase.LoadAssetAtPath<Milestone2ActionSettings>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<Milestone2ActionSettings>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            Undo.RecordObject(config, "Bind delivered FMOD player actions");
            var serialized = new SerializedObject(config);
            SerializedProperty cues = serialized.FindProperty("cues");
            int previousCount = cues.arraySize;
            cues.arraySize = Milestone2ActionCatalog.CueCount;
            int bound = 0, requested = 0;
            for (int index = 0; index < cues.arraySize; index++)
            {
                var cue = cues.GetArrayElementAtIndex(index);
                SerializedProperty reference = cue.FindPropertyRelative("eventReference");
                if (index >= previousCount)
                {
                    // Unity grows serialized arrays by copying their last entry.
                    reference.FindPropertyRelative("Path").stringValue = Milestone2ActionCatalog.EventPath(index);
                    reference.FindPropertyRelative("Guid").boxedValue = default(FMOD.GUID);
                    cue.FindPropertyRelative("gainDb").floatValue = 0f;
                    cue.FindPropertyRelative("readBySoundUI").boolValue = true;
                    cue.FindPropertyRelative("soundUIStrength").floatValue = 0.5f;
                }
                if (terrainOnly && !Milestone2ActionCatalog.IsSurfaceRoll(index)) continue;
                requested++;
                EditorEventRef audioEvent = EventManager.EventFromPath(Milestone2ActionCatalog.EventPath(index));
                if (audioEvent == null) continue; // Partial deliveries are valid.
                if (audioEvent.Is3D)
                    throw new InvalidOperationException("Player action events must be 2D: " + audioEvent.Path);
                reference.FindPropertyRelative("Path").stringValue = audioEvent.Path;
                reference.FindPropertyRelative("Guid").boxedValue = audioEvent.Guid;
                bound++;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(config);
            Debug.Log("[Milestone 2 Audio] Bound " + bound + "/" + requested + " player-action event slots.");
        }
    }
}
