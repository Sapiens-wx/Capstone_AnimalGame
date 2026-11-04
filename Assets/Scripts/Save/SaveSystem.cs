using System;
using System.IO;
using AnimalGame.Animals;
using UnityEngine;

namespace AnimalGame.Save
{
    public static class SaveSystem
    {
        private const int FileSignature = 0x41475356; // AGSV
        private const int FileVersion = 1;
        public static GameSave Current { get; private set; }
        public static string SavePath => Path.Combine(Application.persistentDataPath, "animal-save.bin");
        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { Current = null; Changed = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Initialize()
        {
            if (Current != null) return;
            AnimalArchiveAssets.Initialize();
            Current = LoadOrCreate(SavePath);
        }

        public static GameSave LoadOrCreate(string path)
        {
            if (!File.Exists(path))
            {
                var empty = new GameSave();
                WriteTo(path, empty);
                return empty;
            }
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (reader.ReadInt32() != FileSignature) throw new InvalidDataException("Invalid game save signature.");
            int version = reader.ReadInt32();
            if (version != FileVersion) throw new InvalidDataException($"Unsupported save version: {version}");
            var save = new GameSave();
            int photoCount = ReadCount(reader);
            for (int i = 0; i < photoCount; i++) save.photos.Add(ReadPhoto(reader));
            int progressCount = ReadCount(reader);
            for (int i = 0; i < progressCount; i++)
                save.speciesProgress.Add(new SpeciesProgress {
                    species = (AnimalSpecies)reader.ReadInt32(), recognition = reader.ReadInt32(), sentPhotos = reader.ReadInt32() });
            if (stream.Position != stream.Length) throw new InvalidDataException("Unexpected trailing game save data.");
            return save;
        }

        private static int ReadCount(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count < 0) throw new InvalidDataException("Negative game save record count.");
            return count;
        }

        public static void WriteTo(string path, GameSave data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + ".tmp";
            try
            {
                using (var stream = File.Create(temporary))
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(FileSignature);
                    writer.Write(FileVersion);
                    writer.Write(data.photos.Count);
                    foreach (var photo in data.photos) WritePhoto(writer, photo);
                    writer.Write(data.speciesProgress.Count);
                    foreach (var progress in data.speciesProgress)
                    {
                        writer.Write((int)progress.species);
                        writer.Write(progress.recognition);
                        writer.Write(progress.sentPhotos);
                    }
                    writer.Flush();
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
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

        public static void DeletePhoto(string photoId)
        {
            Initialize();
            int index = Current.photos.FindIndex(photo => photo.id == photoId);
            if (index < 0) throw new InvalidOperationException($"Photo does not exist: {photoId}");
            var photo = Current.photos[index];
            Current.photos.RemoveAt(index);
            try { WriteTo(SavePath, Current); }
            catch { Current.photos.Insert(index, photo); throw; }
            Changed?.Invoke();
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            writer.Write(value != null);
            if (value != null) writer.Write(value);
        }

        private static string ReadString(BinaryReader reader) => reader.ReadBoolean() ? reader.ReadString() : null;

        private static void WritePhoto(BinaryWriter writer, AnimalPhotoRecord value)
        {
            WriteString(writer, value.id);
            WriteString(writer, value.sourceId);
            WriteString(writer, value.displayName);
            WriteString(writer, value.scientificName);
            WriteString(writer, value.regionName);
            writer.Write((int)value.species);
            writer.Write(value.capturedTicks);
            writer.Write(value.mapPosition.x); writer.Write(value.mapPosition.y);
            writer.Write(value.playerPosition.x); writer.Write(value.playerPosition.y); writer.Write(value.playerPosition.z);
            writer.Write(value.altitude);
            writer.Write(value.latitude);
            writer.Write(value.longitude);
            writer.Write(value.hasHeight);
            writer.Write(value.hasCoordinates);
            writer.Write(value.sent);
            WriteString(writer, value.cameraId);
            WriteString(writer, value.cameraName);
            WriteString(writer, value.renderSetId);
            WriteString(writer, value.shaderName);
            writer.Write(value.renderVersion);
            writer.Write(value.seed);
            writer.Write(value.recognitionValue);
            writer.Write(value.crop.x); writer.Write(value.crop.y); writer.Write(value.crop.width); writer.Write(value.crop.height);
            writer.Write(value.saturation);
            writer.Write(value.basicSaturation);
            writer.Write((int)value.effect);
            WriteFilm(writer, value.instantFilm);
            WriteDamage(writer, value.damagedPrime);
        }

        private static AnimalPhotoRecord ReadPhoto(BinaryReader reader) => new AnimalPhotoRecord
        {
            id = ReadString(reader),
            sourceId = ReadString(reader),
            displayName = ReadString(reader),
            scientificName = ReadString(reader),
            regionName = ReadString(reader),
            species = (AnimalSpecies)reader.ReadInt32(),
            capturedTicks = reader.ReadInt64(),
            mapPosition = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
            playerPosition = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
            altitude = reader.ReadSingle(),
            latitude = reader.ReadSingle(),
            longitude = reader.ReadSingle(),
            hasHeight = reader.ReadBoolean(),
            hasCoordinates = reader.ReadBoolean(),
            sent = reader.ReadBoolean(),
            cameraId = ReadString(reader),
            cameraName = ReadString(reader),
            renderSetId = ReadString(reader),
            shaderName = ReadString(reader),
            renderVersion = reader.ReadInt32(),
            seed = reader.ReadInt32(),
            recognitionValue = reader.ReadInt32(),
            crop = new Rect(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
            saturation = reader.ReadSingle(),
            basicSaturation = reader.ReadSingle(),
            effect = (SavedPhotoEffect)reader.ReadInt32(),
            instantFilm = ReadFilm(reader),
            damagedPrime = ReadDamage(reader),
        };

        private static void WriteFilm(BinaryWriter writer, InstantFilmSettings value)
        {
            writer.Write(value.strength);
            writer.Write(value.toneEnabled);
            writer.Write(value.exposureEV);
            writer.Write(value.toneStrength);
            writer.Write(value.contrast);
            writer.Write(value.midtoneLift);
            writer.Write(value.blackLift);
            writer.Write(value.highlightCompression);
            writer.Write(value.colorEnabled);
            writer.Write(value.saturation);
            writer.Write(value.highlightWarmth);
            writer.Write(value.shadowCoolness);
            writer.Write(value.colorProtection);
            writer.Write(value.grainEnabled);
            writer.Write(value.grainAmount);
            writer.Write(value.grainSize);
            writer.Write(value.grainChroma);
            writer.Write(value.vignetteEnabled);
            writer.Write(value.vignetteEV);
            writer.Write(value.softnessEnabled);
            writer.Write(value.softness);
        }

        private static InstantFilmSettings ReadFilm(BinaryReader reader) => new InstantFilmSettings
        {
            strength = reader.ReadSingle(),
            toneEnabled = reader.ReadBoolean(),
            exposureEV = reader.ReadSingle(),
            toneStrength = reader.ReadSingle(),
            contrast = reader.ReadSingle(),
            midtoneLift = reader.ReadSingle(),
            blackLift = reader.ReadSingle(),
            highlightCompression = reader.ReadSingle(),
            colorEnabled = reader.ReadBoolean(),
            saturation = reader.ReadSingle(),
            highlightWarmth = reader.ReadSingle(),
            shadowCoolness = reader.ReadSingle(),
            colorProtection = reader.ReadSingle(),
            grainEnabled = reader.ReadBoolean(),
            grainAmount = reader.ReadSingle(),
            grainSize = reader.ReadSingle(),
            grainChroma = reader.ReadSingle(),
            vignetteEnabled = reader.ReadBoolean(),
            vignetteEV = reader.ReadSingle(),
            softnessEnabled = reader.ReadBoolean(),
            softness = reader.ReadSingle(),
        };

        private static void WriteDamage(BinaryWriter writer, DamagedPrimeSettings value)
        {
            writer.Write(value.blurRadius);
            writer.Write(value.grainAmount);
            writer.Write(value.grainSize);
            writer.Write(value.exposureEV);
            writer.Write(value.contrast);
            writer.Write(value.blackLift);
            writer.Write(value.vignetteEV);
        }

        private static DamagedPrimeSettings ReadDamage(BinaryReader reader) => new DamagedPrimeSettings
        {
            blurRadius = reader.ReadSingle(),
            grainAmount = reader.ReadSingle(),
            grainSize = reader.ReadSingle(),
            exposureEV = reader.ReadSingle(),
            contrast = reader.ReadSingle(),
            blackLift = reader.ReadSingle(),
            vignetteEV = reader.ReadSingle(),
        };
    }
}
