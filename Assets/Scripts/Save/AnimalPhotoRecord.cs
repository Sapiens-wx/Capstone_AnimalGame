using System;
using AnimalGame.Animals;
using AnimalGame.RobotMap;
using UnityEngine;

namespace AnimalGame.Save
{
    public enum SavedPhotoEffect { Basic, InstantFilm, DamagedPrime }

    [Serializable]
    public sealed class AnimalPhotoRecord
    {
        public string id, sourceId, displayName, scientificName, regionName;
        public AnimalSpecies species;
        public long capturedTicks;
        public Vector2 mapPosition;
        public Vector3 playerPosition;
        public float altitude, latitude, longitude;
        public bool hasHeight, hasCoordinates, sent;
        public string cameraId, cameraName, renderSetId, shaderName;
        public int renderVersion, seed, recognitionValue;
        public Rect crop;
        public float saturation, basicSaturation;
        public SavedPhotoEffect effect;
        public InstantFilmSettings instantFilm;
        public DamagedPrimeSettings damagedPrime;
        public DateTime CapturedAt => new DateTime(capturedTicks);

        public static AnimalPhotoRecord Capture(PhotoResultSnapshot result)
        {
            var photo = result.Photo;
            var render = photo.RenderSnapshot;
            var record = new AnimalPhotoRecord {
                id = Guid.NewGuid().ToString("N"),
                sourceId = AnimalArchiveAssets.Active.SourceId(photo.Source),
                species = result.Species, displayName = result.DisplayName,
                scientificName = result.ScientificName, regionName = result.RegionName,
                capturedTicks = result.CapturedAt.Ticks, mapPosition = result.MapPositionMeters,
                playerPosition = result.PlayerPosition, altitude = result.HeightMeters,
                latitude = result.Latitude, longitude = result.Longitude,
                hasHeight = result.HasHeight, hasCoordinates = result.HasGeographicCoordinates,
                cameraId = photo.CaptureSettings.CameraId, cameraName = photo.CaptureSettings.CameraName,
                crop = photo.Crop, saturation = photo.Saturation,
                renderSetId = render.SetId, renderVersion = render.Version, seed = render.Seed
            };
            switch (render)
            {
                case BasicPhotoRenderSnapshot basic:
                    record.effect = SavedPhotoEffect.Basic; record.basicSaturation = basic.Saturation;
                    record.shaderName = "AnimalGame/Photos/AnimalPhotoProcess";
                    record.recognitionValue = basic.Saturation * photo.Saturation <= 0 ? 1 : 5;
                    break;
                case InstantFilmPhotoRenderSnapshot film:
                    record.effect = SavedPhotoEffect.InstantFilm; record.instantFilm = film.Settings;
                    record.shaderName = "AnimalGame/Photos/InstantFilm"; record.recognitionValue = 5;
                    break;
                case DamagedPrimePhotoRenderSnapshot damaged:
                    record.effect = SavedPhotoEffect.DamagedPrime; record.damagedPrime = damaged.Settings;
                    record.shaderName = "AnimalGame/Photos/DamagedPrime"; record.recognitionValue = 1;
                    break;
                default: throw new NotSupportedException(render.GetType().FullName);
            }
            return record;
        }

        public AnimalPhoto RestorePhoto()
        {
            PhotoRenderSnapshot render;
            switch (effect)
            {
                case SavedPhotoEffect.Basic: render = new BasicPhotoRenderSnapshot(renderSetId, renderVersion, seed, basicSaturation); break;
                case SavedPhotoEffect.InstantFilm: render = new InstantFilmPhotoRenderSnapshot(renderSetId, renderVersion, seed, instantFilm); break;
                case SavedPhotoEffect.DamagedPrime: render = new DamagedPrimePhotoRenderSnapshot(renderSetId, renderVersion, seed, damagedPrime); break;
                default: throw new NotSupportedException(effect.ToString());
            }
            return new AnimalPhoto(AnimalArchiveAssets.Active.Source(sourceId), crop, saturation,
                new PhotoCaptureSettings(cameraId, cameraName, render));
        }
    }
}
