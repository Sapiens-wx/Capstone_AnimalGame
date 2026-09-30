using System;
using System.IO;
using Capstone.Audio;
using FMODUnity;
using UnityEditor;
using UnityEngine;

namespace Capstone.Audio.Editor
{
    public static class Milestone1AudioSetup
    {
        private const string ConfigPath = "Assets/FMOD/Resources/Milestone1AudioSettings.asset";
        private const string BankPath = "FMOD/Banks/Desktop";
        private static readonly string[] Fields =
        {
            "scanCharge", "scanPulse", "cameraOpen", "cameraClose",
            "cameraMove", "cameraFocus", "cameraShutter"
        };
        private static readonly string[] Events =
        {
            "event:/Robot/Scan/Charge", "event:/Robot/Scan/Pulse",
            "event:/Robot/Camera/Open", "event:/Robot/Camera/Close",
            "event:/Robot/Camera/Move", "event:/Robot/Camera/Focus",
            "event:/Robot/Camera/Shutter"
        };

        [MenuItem("FMOD/Milestone 1/Configure Integration", priority = 100)]
        public static void Configure()
        {
            // A fresh package carries the editor libraries in FMOD/staging.
            // Batch executeMethod can run before the wizard's delayed startup.
            if (StagingSystem.SourceLibsExist)
            {
                StagingSystem.Startup();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                if (StagingSystem.SourceLibsExist)
                    throw new InvalidOperationException("Finish FMOD's native-library installation and restart Unity before configuring audio.");
            }
            foreach (string bank in new[] { "Master.bank", "Master.strings.bank", "RobotTools.bank" })
                if (!File.Exists(Path.Combine(BankPath, bank)))
                    throw new FileNotFoundException("Build and copy the Desktop FMOD banks first.", bank);

            // Only the new FMOD settings asset is configured; existing Unity
            // audio, gameplay, scenes, prefabs and Build Settings are untouched.
            Settings settings = Settings.Instance;
            settings.HasSourceProject = false;
            settings.HasPlatforms = false;
            settings.SourceProjectPath = string.Empty;
            settings.SourceBankPath = BankPath;
            settings.ImportType = ImportType.StreamingAssets;
            settings.TargetBankFolder = "FMOD";
            settings.BankLoadType = BankLoadType.All;
            settings.AutomaticEventLoading = true;
            settings.AutomaticSampleLoading = true;
            settings.ShowBankRefreshWindow = false;
            settings.HideSetupWizard = true;
            SetPlatformProperty(settings.DefaultPlatform, "SpeakerMode", (int)FMOD.SPEAKERMODE.STEREO);
            SetPlatformProperty(settings.PlayInEditorPlatform, "LiveUpdate", (int)TriStateBool.Enabled);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);

            EventManager.RefreshBanks();
            Directory.CreateDirectory("Assets/FMOD/Resources");
            AssetDatabase.Refresh();
            var config = AssetDatabase.LoadAssetAtPath<Milestone1AudioSettings>(ConfigPath);
            bool isNew = config == null;
            if (isNew)
            {
                config = ScriptableObject.CreateInstance<Milestone1AudioSettings>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }

            var serialized = new SerializedObject(config);
            for (int i = 0; i < Events.Length; ++i)
            {
                EditorEventRef audioEvent = EventManager.EventFromPath(Events[i]);
                if (audioEvent == null)
                    throw new InvalidOperationException("FMOD event missing from the built banks: " + Events[i]);
                if (audioEvent.Is3D)
                    throw new InvalidOperationException("Milestone 1 events must be 2D: " + Events[i]);
                SerializedProperty cue = serialized.FindProperty(Fields[i]);
                SerializedProperty reference = cue.FindPropertyRelative("eventReference");
                reference.FindPropertyRelative("Path").stringValue = audioEvent.Path;
                reference.FindPropertyRelative("Guid").boxedValue = audioEvent.Guid;
                if (isNew)
                    cue.FindPropertyRelative("gainDb").floatValue = 0f;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(config);
            AssetDatabase.SaveAssetIfDirty(settings);
            Directory.CreateDirectory("Library/FMODValidation");
            File.WriteAllText("Library/FMODValidation/setup.json",
                "{\"configured\":true,\"events\":7,\"source\":\"FMOD/Banks/Desktop\",\"config\":\"" + ConfigPath + "\"}");
            Debug.Log("[Milestone 1 Audio] Configured 7 two-dimensional events and relative Desktop banks.");
        }

        private static void SetPlatformProperty(Platform platform, string name, int value)
        {
            var serialized = new SerializedObject(platform);
            SerializedProperty property = serialized.FindProperty("Properties").FindPropertyRelative(name);
            property.FindPropertyRelative("HasValue").boolValue = true;
            property.FindPropertyRelative("Value").intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(platform);
        }
    }
}
