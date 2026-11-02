using System;
using System.Collections.Generic;
using System.Globalization;
using AnimalGame.Save;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.Animals.UI
{
    public sealed class PhotosView : IDisposable
    {
        private readonly VisualElement root, index, tiles, preview, heading, information;
        private readonly ArchivePhotoImageElement image;
        private readonly List<RenderTexture> thumbnails = new();
        private RenderTexture processed, card;
        private Material cardMaterial;
        private Vector2 tilt;
        private PhotoCollection currentCollection;
        private Action<int> selectPhoto;
        public PhotosView(VisualElement root, Shader compositeShader)
        {
            this.root = root; index = root.Q("photo-index"); tiles = root.Q("photo-tiles");
            preview = root.Q("photo-preview"); image = root.Q<ArchivePhotoImageElement>("photo-image");
            heading = root.Q("photo-heading"); information = root.Q(className: "photo-information");
            cardMaterial = new Material(compositeShader) { hideFlags = HideFlags.HideAndDontSave };
            cardMaterial.SetVector("_Crop", new Vector4(0, 0, 1, 1));
            card = new RenderTexture(1024, 1024, 0, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave };
            card.Create();
            root.Q<Button>("photo-previous").clicked += () => selectPhoto(currentCollection.Selected - 1);
            root.Q<Button>("photo-next").clicked += () => selectPhoto(currentCollection.Selected + 1);
        }

        public void Render(PhotoCollection collection, bool inspection, Action<int> select, Action open, Action catalog, Action sort)
        {
            currentCollection = collection;
            selectPhoto = select;
            ReleaseThumbnails(); tiles.Clear();
            index.style.display = inspection ? DisplayStyle.None : DisplayStyle.Flex;
            preview.EnableInClassList("inspection", inspection);
            var headingParent = inspection ? information : preview;
            if (heading.parent != headingParent) headingParent.Insert(0, heading);
            image.Square = !inspection;
            var sortButton = root.Q<Button>("photo-sort");
            sortButton.text = collection.OldestFirst ? "Oldest first ↑" : "Newest first ↓";
            sortButton.EnableInClassList("selected", collection.SortFocused);
            sortButton.clicked -= sortHandler; sortHandler = sort; sortButton.clicked += sortHandler;
            for (int i = collection.TopRow * 3; i < Math.Min(collection.Photos.Count, collection.TopRow * 3 + 6); i++)
            {
                int photoIndex = i; var record = collection.Photos[i];
                var button = new Button(() => select(photoIndex)) { focusable = false }; button.AddToClassList("photo-tile");
                button.EnableInClassList("selected", i == collection.Selected && !collection.SortFocused);
                var thumbnail = record.RestorePhoto().Render(256); thumbnails.Add(thumbnail);
                var picture = new VisualElement { pickingMode = PickingMode.Ignore };
                picture.AddToClassList("photo-tile-picture");
                var thumbnailImage = new Image { image = thumbnail, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                thumbnailImage.AddToClassList("photo-tile-image");
                float thumbnailAspect = (float)thumbnail.height / thumbnail.width;
                thumbnailImage.RegisterCallback<GeometryChangedEvent>(evt => {
                    if (evt.newRect.width != evt.oldRect.width)
                        thumbnailImage.style.height = evt.newRect.width * thumbnailAspect;
                });
                picture.Add(thumbnailImage);
                var number = new Label($"{i + 1:000}") { pickingMode = PickingMode.Ignore };
                number.AddToClassList("photo-tile-number");
                picture.Add(number);
                button.Add(picture);
                var name = new Label(record.displayName) { pickingMode = PickingMode.Ignore };
                name.AddToClassList("photo-tile-name");
                button.Add(name);
                var date = new Label(record.CapturedAt.ToString("yyyy.MM.dd\nHH:mm", CultureInfo.InvariantCulture)) { pickingMode = PickingMode.Ignore };
                date.AddToClassList("photo-tile-date");
                button.Add(date);
                if (record.sent) button.Add(new Label("✓ UPLOADED"));
                button.RegisterCallback<ClickEvent>(e => { if (e.clickCount == 2) open(); });
                tiles.Add(button);
            }
            var current = collection.Current;
            root.Q("photo-delete").style.display = current != null ? DisplayStyle.Flex : DisplayStyle.None;
            root.Q<Label>("photo-name").text = current?.displayName ?? "No saved photographs";
            root.Q<Label>("photo-scientific").text = current?.scientificName ?? "";
            root.Q<Label>("photo-region").text = current?.regionName ?? "";
            root.Q<Label>("photo-date").text = current?.CapturedAt.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture) ?? "";
            root.Q("photo-uploaded").style.display = current != null && current.sent ? DisplayStyle.Flex : DisplayStyle.None;
            root.Q<Label>("photo-captured").text = current?.CapturedAt.ToString("yyyy.MM.dd  HH:mm", CultureInfo.InvariantCulture) ?? "";
            root.Q<Label>("photo-altitude").text = current == null ? "" : current.hasHeight ? $"{current.altitude:0} m" : "- m";
            root.Q<Label>("photo-position").text = current == null ? "" : Position(current);
            root.Q<Label>("photo-camera").text = current?.cameraName ?? "";
            root.Q("photo-navigation").style.display = inspection ? DisplayStyle.Flex : DisplayStyle.None;
            root.Q<Button>("photo-previous").SetEnabled(current != null && collection.Selected > 0);
            root.Q<Button>("photo-next").SetEnabled(current != null && collection.Selected < collection.Photos.Count - 1);
            var view = root.Q<Button>("photo-open"); view.style.display = inspection ? DisplayStyle.None : DisplayStyle.Flex;
            view.SetEnabled(current != null); view.clicked -= openHandler; openHandler = open; view.clicked += openHandler;
            var guide = root.Q<Button>("photo-catalog"); guide.SetEnabled(current != null);
            guide.clicked -= catalogHandler; catalogHandler = catalog; guide.clicked += catalogHandler;
            AnimalPhotoProcessing.Release(processed); processed = null; image.image = null;
            if (current != null) { processed = current.RestorePhoto().Render(1024); tilt = Vector2.zero; image.image = inspection ? card : processed; }
            root.Q<Label>("record-count").text = $"{collection.Photos.Count:00} RECORDS   {collection.Selected + (current == null ? 0 : 1):00} / {collection.Photos.Count:00}";
        }
        private Action sortHandler, openHandler, catalogHandler;

        public void SetDeleteProgress(float progress) => root.Q<ArchiveHoldCircleElement>("photo-delete-progress").Progress = progress;

        private static string Position(AnimalPhotoRecord p)
        {
            return p.hasCoordinates
                ? $"{Mathf.Abs(p.latitude).ToString("F5", CultureInfo.InvariantCulture)}° {(p.latitude < 0 ? "S" : "N")} / {Mathf.Abs(p.longitude).ToString("F5", CultureInfo.InvariantCulture)}° {(p.longitude < 0 ? "W" : "E")}" 
                : $"Coordinates ({p.mapPosition.x:0.0}, {p.mapPosition.y:0.0})";
        }

        public void Tilt(Vector2 input)
        {
            if (processed == null) return;
            Vector2 target = input.magnitude < .16f ? Vector2.zero : new Vector2(-input.y, input.x) * 16;
            tilt = Vector2.Lerp(tilt, target, 1 - Mathf.Exp(-3 * Time.unscaledDeltaTime));
            cardMaterial.SetFloat("_ImageAspect", (float)processed.width / processed.height);
            cardMaterial.SetVector("_Tilt", new Vector4(tilt.x * Mathf.Deg2Rad, tilt.y * Mathf.Deg2Rad, 0, 0));
            RenderTexture previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            try { GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear && card.sRGB; Graphics.Blit(processed, card, cardMaterial); }
            finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
        }
        private void ReleaseThumbnails() { foreach (var texture in thumbnails) AnimalPhotoProcessing.Release(texture); thumbnails.Clear(); }
        public void Clear() { image.image = null; tiles.Clear(); ReleaseThumbnails(); AnimalPhotoProcessing.Release(processed); processed = null; }
        public void Dispose() { Clear(); AnimalPhotoProcessing.Release(card); UnityEngine.Object.Destroy(cardMaterial); }
    }
}
