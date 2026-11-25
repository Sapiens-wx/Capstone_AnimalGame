using System;
using System.Collections.Generic;
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
        private const int CaptureRecognitionVersion = 3;
        private const int NodeMessageVersion = 5;
        private const int StationSyncVersion = 6;
        private const int RuinStateVersion = 7;
        private const int RepairTutorialVersion = 8;
        private const int PhotoMessageSecondsVersion = 9;
        private const int FileVersion = PhotoMessageSecondsVersion;
        public static GameSave Current { get; private set; }
        public static DateTime CurrentGameTime => Current?.gameTime ?? GameSave.InitialGameTime;
        public static string SavePath => Path.Combine(Application.persistentDataPath, "animal-save.bin");
        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Application.quitting -= SaveOnQuit;
            Current = null;
            Changed = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Initialize()
        {
            if (Current != null) return;
            AnimalArchiveAssets.Initialize();
            Current = LoadOrCreate(SavePath);
            if (Application.isPlaying) Application.quitting += SaveOnQuit;
        }

        private static void SaveOnQuit()
        {
            // Clearing the save through the editor menu deletes this file.
            if (File.Exists(SavePath)) WriteTo(SavePath, Current);
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
            if (version < CaptureRecognitionVersion)
            {
                foreach (var photo in save.photos.Where(p => !p.sent)) CreditCapture(save, photo);
            }
            if (version < FileVersion) WriteTo(path, save);
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
                        var message = new ChatMessage();
                        if (version >= NodeMessageVersion)
                        {
                            message.kind = (MessageKind)reader.ReadInt32();
                            if (message.kind == MessageKind.Text)
                            {
                                message.nodeId = reader.ReadInt32();
                                message.sentAt = new DateTime(reader.ReadInt64());
                                conversation.messages.Add(message);
                                continue;
                            }
                            if (message.kind != MessageKind.Photos) throw new InvalidDataException("Unknown chat message kind.");
                        }

                        message.fromPlayer = reader.ReadBoolean();
                        message.hour = reader.ReadInt32();
                        message.minute = reader.ReadInt32();
                        if (version >= PhotoMessageSecondsVersion) message.second = reader.ReadInt32();
                        if (version < NodeMessageVersion)
                        {
                            message.kind = (MessageKind)reader.ReadInt32();
                            if (message.kind == MessageKind.Text)
                            {
                                // Legacy free text has no node ID and cannot be replayed by the graph.
                                ReadString(reader);
                                continue;
                            }
                            if (message.kind != MessageKind.Photos) throw new InvalidDataException("Unknown chat message kind.");
                        }
                        message.photoIndices = new int[ReadCount(reader)];
                        for (int k = 0; k < message.photoIndices.Length; k++)
                        {
                            int index = reader.ReadInt32();
                            if (index < 0 || index >= save.photos.Count) throw new InvalidDataException("Invalid message photo index.");
                            message.photoIndices[k] = index;
                        }
                        conversation.messages.Add(message);
                    }
                    save.conversations.Add(conversation);
                }
            }
            if (version >= 4) save.gameTime = new DateTime(reader.ReadInt64());
            if (version >= StationSyncVersion) ReadIds(reader, save.syncedStationIds);
            if (version >= RuinStateVersion)
            {
                ReadIds(reader, save.poweredStationIds);
                ReadIds(reader, save.discoveredRuinIds);
            }
            if (version >= RepairTutorialVersion) ReadIds(reader, save.completedRepairTutorialIds);
            if (stream.Position != stream.Length) throw new InvalidDataException("Unexpected trailing game save data.");
            return save;
        }

        private static int ReadCount(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count < 0) throw new InvalidDataException("Negative game save record count.");
            return count;
        }

        private static void ReadIds(BinaryReader reader, List<string> ids)
        {
            int count = ReadCount(reader);
            for (int i = 0; i < count; i++) ids.Add(reader.ReadString());
        }

        private static void WriteIds(BinaryWriter writer, List<string> ids)
        {
            writer.Write(ids.Count);
            foreach (string id in ids) writer.Write(id);
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
                            writer.Write((int)message.kind);
                            if (message.kind == MessageKind.Text)
                            {
                                writer.Write(message.nodeId);
                                writer.Write(message.sentAt.Ticks);
                            }
                            else if (message.kind == MessageKind.Photos)
                            {
                                writer.Write(message.fromPlayer); writer.Write(message.hour); writer.Write(message.minute);
                                writer.Write(message.second);
                                writer.Write(message.photoIndices.Length);
                                foreach (int index in message.photoIndices) writer.Write(index);
                            }
                            else throw new InvalidDataException("Unknown chat message kind.");
                        }
                    }
                    writer.Write(data.gameTime.Ticks);
                    WriteIds(writer, data.syncedStationIds);
                    WriteIds(writer, data.poweredStationIds);
                    WriteIds(writer, data.discoveredRuinIds);
                    WriteIds(writer, data.completedRepairTutorialIds);
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

        public static bool IsStationSynced(string stationId)
        {
            Initialize();
            return Current.syncedStationIds.Contains(stationId);
        }

        public static bool SyncStation(string stationId) => SyncStation(stationId, SavePath);

        internal static bool SyncStation(string stationId, string path)
        {
            if (string.IsNullOrWhiteSpace(stationId))
                throw new ArgumentException("A station must have an ID to sync.", nameof(stationId));
            Initialize();
            return RecordId(Current.syncedStationIds, stationId, path);
        }

        public static bool IsStationPowered(string stationId)
        {
            Initialize();
            return Current.poweredStationIds.Contains(stationId);
        }

        public static bool PowerStation(string stationId) => PowerStation(stationId, SavePath);

        internal static bool PowerStation(string stationId, string path)
        {
            if (string.IsNullOrWhiteSpace(stationId))
                throw new ArgumentException("A station must have an ID to restore power.", nameof(stationId));
            Initialize();
            return RecordId(Current.poweredStationIds, stationId, path);
        }

        public static bool IsRuinDiscovered(string ruinId)
        {
            Initialize();
            return Current.discoveredRuinIds.Contains(ruinId);
        }

        public static bool DiscoverRuin(string ruinId) => DiscoverRuin(ruinId, SavePath);

        internal static bool DiscoverRuin(string ruinId, string path)
        {
            if (string.IsNullOrWhiteSpace(ruinId))
                throw new ArgumentException("A ruin must have an ID to be discovered.", nameof(ruinId));
            Initialize();
            return RecordId(Current.discoveredRuinIds, ruinId, path);
        }

        internal static bool IsRepairTutorialComplete(string stationId)
        {
            Initialize();
            return Current.completedRepairTutorialIds.Contains(stationId);
        }

        internal static bool CompleteRepairTutorial(string stationId)
        {
            if (string.IsNullOrWhiteSpace(stationId))
                throw new ArgumentException("A repair tutorial station must have an ID.", nameof(stationId));
            Initialize();
            return RecordId(Current.completedRepairTutorialIds, stationId, SavePath);
        }

        private static bool RecordId(List<string> ids, string id, string path)
        {
            if (ids.Contains(id)) return false;
            ids.Add(id);
            try { WriteTo(path, Current); }
            catch
            {
                ids.Remove(id);
                throw;
            }
            Changed?.Invoke();
            return true;
        }

        public static bool StorePhoto(AnimalPhotoRecord photo) => StorePhoto(photo, SavePath);

        internal static bool StorePhoto(AnimalPhotoRecord photo, string path)
        {
            Initialize();
            if (Current.photos.Exists(p => p.id == photo.id)) return false;
            var previous = Current.Progress(photo.species);
            int previousRecognition = previous?.recognition ?? 0;
            int previousTier = previous?.Tier ?? 0;
            Current.photos.Add(photo);
            CreditCapture(Current, photo);
            try { WriteTo(path, Current); }
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

        internal static bool HasPendingIncomingMessage(ContactDefinition contact)
        {
            var options = contact.options;
            if (options == null || options.NodeCount == 0) return false;
            Initialize();
            var conversation = Current.conversations.Find(c => c.contact == contact.contact);
            int current = options.GetCurrentNodeId(conversation);
            int first = FirstIncomingNode(options, current);
            return first != 0 && first != current && !CurrentIncomingTurn(options, conversation).Contains(first);
        }

        public static bool AdvanceConversation(ContactDefinition contact, DateTime time, int replyId = 0)
        {
            Initialize();
            var options = contact.options;
            if (options == null) return false;
            var conversation = Current.conversations.Find(c => c.contact == contact.contact);
            int currentId = options.GetCurrentNodeId(conversation);
            var pending = new List<ChatMessage>();
            var visited = new HashSet<int>();
            var sentAt = new DateTime(time.Ticks - time.Ticks % TimeSpan.TicksPerSecond);

            if (replyId != 0)
            {
                if (!options.GetReplyNodeIds(currentId).Contains(replyId)) return false;
                currentId = replyId;
                AddText(currentId);
            }
            else visited.UnionWith(CurrentIncomingTurn(options, conversation));

            visited.Add(currentId);
            for (int nextId = FirstIncomingNode(options, currentId); nextId != 0 && visited.Add(nextId);
                nextId = FirstIncomingNode(options, nextId))
                AddText(nextId);

            if (pending.Count == 0) return false;
            AppendMessages(contact.contact, pending);
            Changed?.Invoke();
            return true;

            void AddText(int id) => pending.Add(new ChatMessage { kind = MessageKind.Text, nodeId = id, sentAt = sentAt });
        }

        private static int FirstIncomingNode(ContactOptions options, int currentId)
        {
            if (currentId == 0) return 0;
            var children = options.GetNode(currentId).Children;
            return children.Count > 0 && options.GetNode(children[0]).Sender == Sender.Npc ? children[0] : 0;
        }

        private static IEnumerable<int> CurrentIncomingTurn(ContactOptions options, ContactConversation conversation)
        {
            if (conversation == null) yield break;
            // Read awareness and delivery both stop an NPC-only cycle from replaying on reopen.
            for (int i = conversation.messages.Count - 1; i >= 0; i--)
            {
                var message = conversation.messages[i];
                if (message.kind != MessageKind.Text || !options.ContainsNode(message.nodeId)) continue;
                if (options.GetNode(message.nodeId).Sender == Sender.Player) yield break;
                yield return message.nodeId;
            }
            yield return options.GetCurrentNodeId(null);
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
                AppendMessages(contact, new[] { new ChatMessage { fromPlayer = true, hour = time.Hour, minute = time.Minute, second = time.Second,
                    kind = MessageKind.Photos, photoIndices = (int[])indices.Clone() } });
            }
            catch
            {
                foreach (int index in indices) Current.photos[index].sent = false;
                Current.speciesProgress = oldProgress;
                throw;
            }
            Changed?.Invoke();
        }

        private static void AppendMessages(ContactId contact, IReadOnlyList<ChatMessage> messages)
        {
            var conversation = Current.conversations.Find(c => c.contact == contact);
            bool created = conversation == null;
            if (created) { conversation = new ContactConversation { contact = contact }; Current.conversations.Add(conversation); }
            int first = conversation.messages.Count;
            conversation.messages.AddRange(messages);
            try { WriteTo(SavePath, Current); }
            catch
            {
                conversation.messages.RemoveRange(first, messages.Count);
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
