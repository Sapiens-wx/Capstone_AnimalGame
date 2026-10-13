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
            var config = AssetDatabase.LoadAssetAtPath<Milestone2ActionSettings>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<Milestone2ActionSettings>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            Undo.RecordObject(config, "Bind delivered FMOD player actions");
            var serialized = new SerializedObject(config);
            SerializedProperty cues = serialized.FindProperty("cues");
            cues.arraySize = Milestone2ActionCatalog.CueCount;
            int bound = 0;
            for (int index = 0; index < cues.arraySize; index++)
            {
                EditorEventRef audioEvent = EventManager.EventFromPath(Milestone2ActionCatalog.EventPath(index));
                if (audioEvent == null) continue; // Partial deliveries are valid.
                if (audioEvent.Is3D)
                    throw new InvalidOperationException("Player action events must be 2D: " + audioEvent.Path);
                SerializedProperty reference = cues.GetArrayElementAtIndex(index).FindPropertyRelative("eventReference");
                reference.FindPropertyRelative("Path").stringValue = audioEvent.Path;
                reference.FindPropertyRelative("Guid").boxedValue = audioEvent.Guid;
                bound++;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(config);
            Debug.Log("[Milestone 2 Audio] Bound " + bound + "/" + Milestone2ActionCatalog.CueCount
                + " player-action event slots. Missing deliveries remain silent; empty-hand and Drop slots await observable gameplay signals.");
        }
    }
}
