using System;
using System.IO;
using System.Linq;
using AnimalGame.Animals.Communication;
using AnimalGame.Animals;
using UnityEngine;

namespace AnimalGame.Save
{
    public static class SaveSystem
    {
        private const int FileSignature = 0x41475356; // AGSV
        private const int FileVersion = 3;
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
            var save = ReadFrom(path, out int version);
            if (version < FileVersion)
            {
                foreach (var photo in save.photos.Where(p => !p.sent)) CreditCapture(save, photo);
                WriteTo(path, save);
            }
            return save;
        }

        private static GameSave ReadFrom(string path, out int version)
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (reader.ReadInt32() != FileSignature) throw new InvalidDataException("Invalid game save signature.");
            version = reader.ReadInt32();
            if (version < 1 || version > FileVersion) throw new InvalidDataException($"Unsupported save version: {version}");
            var save = new GameSave();
            int photoCount = ReadCount(reader);
            for (int i = 0; i < photoCount; i++) save.photos.Add(ReadPhoto(reader));
            int progressCount = ReadCount(reader);
            for (int i = 0; i < progressCount; i++)
                save.speciesProgress.Add(new SpeciesProgress {
                    species = (AnimalSpecies)reader.ReadInt32(), recognition = reader.ReadInt32(), sentPhotos = reader.ReadInt32() });
            if (version >= 2)
            {
                int conversationCount = ReadCount(reader);
                for (int i = 0; i < conversationCount; i++)
                {
                    var conversation = new ContactConversation { contact = (ContactId)reader.ReadInt32() };
                    int messageCount = ReadCount(reader);
                    for (int j = 0; j < messageCount; j++)
                    {
                        var message = new ChatMessage { fromPlayer = reader.ReadBoolean(), hour = reader.ReadInt32(),
                            minute = reader.ReadInt32(), kind = (MessageKind)reader.ReadInt32() };
                        if (message.kind == MessageKind.Text) message.text = ReadString(reader);
                        else if (message.kind == MessageKind.Photos)
                        {
                            message.photoIndices = new int[ReadCount(reader)];
                            for (int k = 0; k < message.photoIndices.Length; k++)
                            {
                                int index = reader.ReadInt32();
                                if (index < 0 || index >= save.photos.Count) throw new InvalidDataException("Invalid message photo index.");
                                message.photoIndices[k] = index;
                            }
                        }
                        else throw new InvalidDataException("Unknown chat message kind.");
                        conversation.messages.Add(message);
                    }
                    save.conversations.Add(conversation);
                }
            }
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
                    writer.Write(data.conversations.Count);
                    foreach (var conversation in data.conversations)
                    {
                        writer.Write((int)conversation.contact);
                        writer.Write(conversation.messages.Count);
                        foreach (var message in conversation.messages)
                        {
                            writer.Write(message.fromPlayer); writer.Write(message.hour); writer.Write(message.minute);
                            writer.Write((int)message.kind);
                            if (message.kind == MessageKind.Text) WriteString(writer, message.text);
                            else if (message.kind == MessageKind.Photos)
                            {
                                writer.Write(message.photoIndices.Length);
                                foreach (int index in message.photoIndices) writer.Write(index);
                            }
                            else throw new InvalidDataException("Unknown chat message kind.");
                        }
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

        public static bool StorePhoto(AnimalPhotoRecord photo)
        {
            Initialize();
            if (Current.photos.Exists(p => p.id == photo.id)) return false;
            var previous = Current.Progress(photo.species);
            int previousRecognition = previous?.recognition ?? 0;
            int previousTier = previous?.Tier ?? 0;
            Current.photos.Add(photo);
            CreditCapture(Current, photo);
            try { WriteTo(SavePath, Current); }
            catch
            {
                Current.photos.Remove(photo);
                if (previous != null) previous.recognition = previousRecognition;
                else Current.speciesProgress.RemoveAll(p => p.species == photo.species);
                throw;
            }
            bool advanced = (Current.Progress(photo.species)?.Tier ?? 0) > previousTier;
            Changed?.Invoke();
            return advanced;
        }

        private static void CreditCapture(GameSave save, AnimalPhotoRecord photo)
        {
            if (photo.species == AnimalSpecies.Unknown) return;
            var progress = save.Progress(photo.species);
            if (progress == null) { progress = new SpeciesProgress { species = photo.species }; save.speciesProgress.Add(progress); }
            progress.recognition += Math.Max(1, photo.recognitionValue);
        }

        public static void DeletePhoto(string photoId)
        {
            Initialize();
            int index = Current.photos.FindIndex(photo => photo.id == photoId);
            if (index < 0) throw new InvalidOperationException($"Photo does not exist: {photoId}");
            var photo = Current.photos[index];
            var messages = Current.conversations.SelectMany(c => c.messages).Where(m => m.kind == MessageKind.Photos).ToArray();
            var previousIndices = messages.Select(m => m.photoIndices).ToArray();
            foreach (var message in messages)
                message.photoIndices = message.photoIndices.Where(i => i != index).Select(i => i > index ? i - 1 : i).ToArray();
            Current.photos.RemoveAt(index);
            try { WriteTo(SavePath, Current); }
            catch
            {
                Current.photos.Insert(index, photo);
                for (int i = 0; i < messages.Length; i++) messages[i].photoIndices = previousIndices[i];
                throw;
            }
            Changed?.Invoke();
        }

        public static void SendText(ContactId contact, string text, DateTime time)
        {
            Initialize();
            AppendMessage(contact, new ChatMessage { fromPlayer = true, hour = time.Hour, minute = time.Minute,
                kind = MessageKind.Text, text = text });
            Changed?.Invoke();
        }

        public static void SharePhotos(ContactId contact, int[] indices, DateTime time)
        {
            Initialize();
            if (indices.Length == 0) return;
            if (indices.Distinct().Count() != indices.Length) throw new ArgumentException("Duplicate photo selection.");
            foreach (int index in indices)
                if (index < 0 || index >= Current.photos.Count || Current.photos[index].sent)
                    throw new InvalidOperationException("Only unsent photos can be shared.");
            var oldProgress = Current.speciesProgress.Select(p => new SpeciesProgress {
                species = p.species, recognition = p.recognition, sentPhotos = p.sentPhotos }).ToList();
            foreach (int index in indices)
            {
                var photo = Current.photos[index]; photo.sent = true;
                var progress = Current.Progress(photo.species);
                if (progress == null) { progress = new SpeciesProgress { species = photo.species }; Current.speciesProgress.Add(progress); }
                progress.sentPhotos++;
            }
            try
            {
                AppendMessage(contact, new ChatMessage { fromPlayer = true, hour = time.Hour, minute = time.Minute,
                    kind = MessageKind.Photos, photoIndices = (int[])indices.Clone() });
            }
            catch
            {
                foreach (int index in indices) Current.photos[index].sent = false;
                Current.speciesProgress = oldProgress;
                throw;
            }
            Changed?.Invoke();
        }

        private static void AppendMessage(ContactId contact, ChatMessage message)
        {
            var conversation = Current.conversations.Find(c => c.contact == contact);
            bool created = conversation == null;
            if (created) { conversation = new ContactConversation { contact = contact }; Current.conversations.Add(conversation); }
            conversation.messages.Add(message);
            try { WriteTo(SavePath, Current); }
            catch
            {
                conversation.messages.Remove(message);
                if (created) Current.conversations.Remove(conversation);
                throw;
            }
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
