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
        private readonly VisualElement root, index, tiles, preview;
        private readonly Image image;
        private readonly List<RenderTexture> thumbnails = new();
        private RenderTexture processed, card;
        private Material cardMaterial;
        private Vector2 tilt;
        public PhotosView(VisualElement root, Shader compositeShader)
        {
            this.root = root; index = root.Q("photo-index"); tiles = root.Q("photo-tiles");
            preview = root.Q("photo-preview"); image = root.Q<Image>("photo-image");
            cardMaterial = new Material(compositeShader) { hideFlags = HideFlags.HideAndDontSave };
            cardMaterial.SetVector("_Crop", new Vector4(0, 0, 1, 1));
            card = new RenderTexture(1024, 1024, 0, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave };
            card.Create();
        }

        public void Render(PhotoCollection collection, bool inspection, Action<int> select, Action open, Action catalog, Action sort)
        {
            ReleaseThumbnails(); tiles.Clear();
            index.style.display = inspection ? DisplayStyle.None : DisplayStyle.Flex;
            preview.EnableInClassList("inspection", inspection);
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
                button.Add(new Image { image = thumbnail, scaleMode = ScaleMode.ScaleToFit });
                button.Add(new Label($"{i + 1:000}  {record.displayName}"));
                button.Add(new Label(record.CapturedAt.ToString("yyyy.MM.dd\nHH:mm", CultureInfo.InvariantCulture)));
                button.Add(new Label(record.sent ? "✓ UPLOADED" : "STORED"));
                button.RegisterCallback<ClickEvent>(e => { if (e.clickCount == 2) open(); });
                tiles.Add(button);
            }
            var current = collection.Current;
            root.Q<Label>("photo-name").text = current?.displayName ?? "No saved photographs";
            root.Q<Label>("photo-scientific").text = current?.scientificName ?? "";
            root.Q<Label>("photo-region").text = current?.regionName ?? "";
            root.Q<Label>("photo-metadata").text = current == null ? "Take a photograph and save it from the result screen." : Metadata(current);
            root.Q<Label>("photo-status").text = current == null ? "" : current.sent ? "✓ UPLOADED" : "STORED";
            var view = root.Q<Button>("photo-open"); view.style.display = inspection ? DisplayStyle.None : DisplayStyle.Flex;
            view.SetEnabled(current != null); view.clicked -= openHandler; openHandler = open; view.clicked += openHandler;
            var guide = root.Q<Button>("photo-catalog"); guide.SetEnabled(current != null);
            guide.clicked -= catalogHandler; catalogHandler = catalog; guide.clicked += catalogHandler;
            AnimalPhotoProcessing.Release(processed); processed = null; image.image = null;
            if (current != null) { processed = current.RestorePhoto().Render(1024); tilt = Vector2.zero; image.image = inspection ? card : processed; }
            root.Q<Label>("record-count").text = $"{collection.Photos.Count:00} RECORDS   {collection.Selected + (current == null ? 0 : 1):00} / {collection.Photos.Count:00}";
        }
        private Action sortHandler, openHandler, catalogHandler;

        public static string Metadata(AnimalPhotoRecord p)
        {
            string position = p.hasCoordinates
                ? $"{Mathf.Abs(p.latitude).ToString("F5", CultureInfo.InvariantCulture)}° {(p.latitude < 0 ? "S" : "N")} / {Mathf.Abs(p.longitude).ToString("F5", CultureInfo.InvariantCulture)}° {(p.longitude < 0 ? "W" : "E")}" 
                : $"Coordinates ({p.mapPosition.x:0.0}, {p.mapPosition.y:0.0})";
            return $"Captured:  {p.CapturedAt:yyyy.MM.dd  HH:mm}\nAltitude:  {(p.hasHeight ? $"{p.altitude:0}m" : "- m")}\nPosition:  {position}\nCamera:  {p.cameraName}";
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
