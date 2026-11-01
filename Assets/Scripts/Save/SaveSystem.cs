using System;
using System.IO;
using AnimalGame.Animals;
using UnityEngine;

namespace AnimalGame.Save
{
    public static class SaveSystem
    {
        public static GameSave Current { get; private set; }
        public static string SavePath => Path.Combine(Application.persistentDataPath, "animal-save.json");
        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { Current = null; Changed = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Initialize()
        {
            if (Current != null) return;
            AnimalArchiveAssets.Initialize();
            var loaded = UnityEngine.Object.Instantiate(AnimalArchiveAssets.Active.defaultSave);
            loaded.name = "Current Game Save";
            if (File.Exists(SavePath)) LoadFrom(SavePath, loaded);
            Current = loaded;
        }

        public static void LoadFrom(string path, GameSave target)
        {
            JsonUtility.FromJsonOverwrite(File.ReadAllText(path), target);
            if (target.version != 1) throw new InvalidDataException($"Unsupported save version: {target.version}");
        }

        public static void WriteTo(string path, GameSave data)
        {
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(data, true));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        public static void Save()
        {
            Initialize();
            WriteTo(SavePath, Current);
            Changed?.Invoke();
        }

        public static void StorePhoto(AnimalPhotoRecord photo)
        {
            Initialize();
            if (Current.photos.Exists(p => p.id == photo.id)) return;
            Current.photos.Add(photo);
            try { WriteTo(SavePath, Current); }
            catch { Current.photos.Remove(photo); throw; }
            Changed?.Invoke();
        }
    }
}
