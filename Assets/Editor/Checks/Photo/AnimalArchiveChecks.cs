using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnimalGame.Animals;
using AnimalGame.Animals.UI;
using AnimalGame.RobotMap;
using AnimalGame.Save;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    public static class AnimalArchiveChecks
    {
        private static int checks;
        private static void Require(bool condition, string message)
        { checks++; if (!condition) throw new InvalidOperationException(message); }

        [MenuItem("Animal Game/Validation/Run Animal Archive Checks")]
        public static void Run()
        {
            checks = 0; AnimalArchiveAssets.Initialize();
            var assets = AnimalArchiveAssets.Active;
            Require(assets.Photos.Count == assets.Info.Count, "Every photo species has an info entry.");
            Require(assets.SpeciesConfigs.Count == 9, "All current species configurations are indexed.");
            Require(assets.images.Count > 0, "Source image manifest is populated.");
            foreach (var source in assets.images)
                Require(ReferenceEquals(assets.Source(assets.SourceId(source.image)), source.image), "Source identity round trip.");
            string folder = Path.Combine(Application.dataPath, "output/validation/animal-archive");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "roundtrip-" + Guid.NewGuid().ToString("N") + ".bin");
            var data = SaveSystem.LoadOrCreate(path);
            try
            {
                Require(File.Exists(path) && data.photos.Count == 0 && data.speciesProgress.Count == 0,
                    "Missing save creates an empty binary save.");
                Require(!ReferenceEquals(data, SaveSystem.LoadOrCreate(path)), "Binary load creates independent save data.");
                var source = assets.Photos[AnimalSpecies.AmericanBison].States[0].Photos[0].Image;
                var snapshots = new PhotoRenderSnapshot[] {
                    new BasicPhotoRenderSnapshot("basic", 2, 42, .7f),
                    new InstantFilmPhotoRenderSnapshot("film", 3, 83, InstantFilmSettings.Default),
                    new DamagedPrimePhotoRenderSnapshot("damaged", 4, 123, DamagedPrimeSettings.Default) };
                for (int i = 0; i < snapshots.Length; i++)
                {
                    var photo = new AnimalPhoto(source, new Rect(.2f, .1f, .5f, .5f), .8f,
                        new PhotoCaptureSettings("camera", "Test camera", snapshots[i]));
                    var record = AnimalPhotoRecord.Capture(Snapshot(photo, new DateTime(2026, 11, 1, 9, i, 0)));
                    data.photos.Add(record);
                    Require(record.recognitionValue == (i == 2 ? 1 : 5), "Camera recognition classification.");
                    Require(!record.sent, "Saving alone does not send a photo.");
                }
                data.speciesProgress.Add(new SpeciesProgress { species = AnimalSpecies.AmericanBison, sentPhotos = 1, recognition = 30 });
                SaveSystem.WriteTo(path, data);
                SaveSystem.WriteTo(path, data);
                var loaded = SaveSystem.LoadOrCreate(path);
                Require(loaded.photos.Count == 3 && loaded.Progress(AnimalSpecies.AmericanBison).Tier == 2, "Disk load restores photos and progress.");
                for (int i = 0; i < snapshots.Length; i++)
                {
                    var record = loaded.photos[i]; var original = data.photos[i];
                    Require(record.id == original.id && record.sourceId == original.sourceId, "Persistent photo and source identities.");
                    Require(record.capturedTicks == original.capturedTicks && record.altitude == 112 && record.playerPosition == new Vector3(4, 5, 6), "Frozen shutter telemetry.");
                    Require(record.crop == original.crop && record.seed == original.seed && record.shaderName == original.shaderName, "Render metadata survives disk round trip.");
                    Require(record.instantFilm.Equals(original.instantFilm) && record.damagedPrime.Equals(original.damagedPrime),
                        "Every film and damaged-camera parameter survives binary load.");
                    Require(record.mapPosition == original.mapPosition && record.latitude == original.latitude
                        && record.longitude == original.longitude && record.hasHeight == original.hasHeight
                        && record.hasCoordinates == original.hasCoordinates && record.sent == original.sent
                        && record.displayName == original.displayName && record.scientificName == original.scientificName
                        && record.regionName == original.regionName && record.cameraId == original.cameraId
                        && record.cameraName == original.cameraName && record.renderSetId == original.renderSetId
                        && record.renderVersion == original.renderVersion && record.recognitionValue == original.recognitionValue
                        && record.species == original.species && record.effect == original.effect
                        && record.saturation == original.saturation && record.basicSaturation == original.basicSaturation,
                        "Photo metadata and flags survive binary load.");
                    var a = original.RestorePhoto().Render(128); var b = record.RestorePhoto().Render(128);
                    try { Require(Pixels(a).SequenceEqual(Pixels(b)), "Saved render produces identical pixels after disk reload."); }
                    finally { AnimalPhotoProcessing.Release(a); AnimalPhotoProcessing.Release(b); }
                }
                byte[] binary = File.ReadAllBytes(path);
                Require(binary[0] != (byte)'{', "Save file contains binary data.");
                File.WriteAllBytes(path, new byte[] { 0, 0, 0, 0 });
                RequireInvalidSave(path, "Invalid binary signature is rejected.");
                var unsupported = (byte[])binary.Clone(); unsupported[4] = 99;
                File.WriteAllBytes(path, unsupported);
                RequireInvalidSave(path, "Unsupported binary version is rejected.");
                File.WriteAllBytes(path, binary.Take(binary.Length - 1).ToArray());
                RequireInvalidSave(path, "Truncated binary save is rejected.");
                var progress = loaded.speciesProgress[0];
                foreach (var pair in new[] { (0, 0), (1, 1), (29, 1), (30, 2), (99, 2), (100, 3) })
                { progress.recognition = pair.Item1; Require(progress.Tier == pair.Item2, "Recognition tier boundary."); }
                progress.sentPhotos = 0; Require(progress.Tier == 3, "Capture recognition unlocks without uploading.");

                var records = new List<AnimalPhotoRecord>();
                for (int i = 0; i < 10; i++) records.Add(new AnimalPhotoRecord { id = i.ToString(), species = AnimalSpecies.AmericanBison, capturedTicks = 100 + i });
                var collection = new PhotoCollection(); collection.Bind(records, AnimalSpecies.AmericanBison);
                Require(collection.Current.id == "9", "Default newest-first sort.");
                collection.Move(0, 1, false); Require(collection.SortFocused, "Up from first row selects sorting.");
                collection.ToggleSort(); Require(collection.Photos[0].id == "0" && collection.SortFocused, "Sort toggles with retained focus.");
                collection.Move(0, -1, false); Require(!collection.SortFocused && collection.Selected == 0, "Down from sort returns to first row.");
                collection.Move(0, -1, false); collection.Move(0, -1, false);
                Require(collection.Selected == 6 && collection.TopRow == 1, "Second row scrolls exactly one row.");
                collection.Move(0, -1, false); collection.Move(0, -1, false);
                Require(collection.Selected == 9 && collection.TopRow == 2, "No movement below final row.");
                collection.Bind(records, AnimalSpecies.Wolf); collection.Move(1, 0, false);
                Require(collection.Current == null, "Empty collection remains valid.");
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Resources/UI/AnimalCatalog/AnimalArchiveUI.prefab");
                Require(prefab.GetComponent<AnimalArchiveUI>().compositeShader != null, "Perspective shader is build-referenced.");
                var tree = prefab.GetComponent<UIDocument>().visualTreeAsset.CloneTree();
                foreach (string name in new[] { "photos-page", "photo-index", "photo-tiles", "photo-image", "catalog-page", "species-page", "catalog-details", "recognition", "back" })
                    Require(tree.Q(name) != null, "UXML binding: " + name);
                File.WriteAllText(Path.Combine(folder, "checks.txt"), $"PASS: {checks} archive assertions.\nRender pixels matched for Basic, InstantFilm and DamagedPrime after disk reload.\n");
                Debug.Log($"ANIMAL ARCHIVE CHECKS PASSED: {checks} assertions.");
            }
            finally { File.Delete(path); }
        }

        private static void RequireInvalidSave(string path, string message)
        {
            bool rejected = false;
            try { SaveSystem.LoadOrCreate(path); }
            catch (InvalidDataException) { rejected = true; }
            catch (EndOfStreamException) { rejected = true; }
            Require(rejected, message);
        }
        private static Color32[] Pixels(RenderTexture texture)
        {
            var previous = RenderTexture.active; var readback = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            try { RenderTexture.active = texture; readback.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); readback.Apply(); return readback.GetPixels32(); }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(readback); }
        }

        internal static PhotoResultSnapshot Snapshot(AnimalPhoto photo, DateTime time) => new PhotoResultSnapshot(
            "American Bison", "Bison bison", "Rocky Mountains", Color.yellow, 0, 0, 0, photo,
            new Vector2(50, 60), 112, 1, time, AnimalSpecies.AmericanBison, AnimalState.Daily, true, true,
            44.4286f, -110.5876f, true, new Vector3(4, 5, 6));
    }
}
