using System;
using System.Collections.Generic;
using System.Globalization;
using AnimalGame.Save;
using AnimalGame.RobotMap;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.Animals.UI
{
    public sealed class PhotosView : IDisposable
    {
        private readonly VisualElement root, index, tiles, preview, heading, information;
        private readonly ArchivePhotoImageElement image;
        private readonly VisualElement photoFrame;
        private readonly Image outgoingImage;
        private readonly List<RenderTexture> thumbnails = new();
        private readonly ArchiveColumnWave waveAnimation;
        private readonly ArchiveButtonFeedback buttonFeedback;
        private RenderTexture processed, card, outgoingCard, outgoingProcessed;
        private Material cardMaterial, outgoingMaterial;
        private Vector2 tilt;
        private readonly PhotoIdleTilt idleTilt = new PhotoIdleTilt(1.5f);
        private string motionPhotoId;
        private bool viewingInspection;
        private bool sliding;
        private float slideElapsed, incomingOffset, outgoingStart;
        private Vector2 outgoingBaseTilt;
        private int slideDirection;
        private const float SlideDuration = .32f;
        private const float SlideRetreatDegrees = 32f;
        private PhotoCollection currentCollection;
        private Action<int> selectPhoto;
        public PhotosView(VisualElement root, Shader compositeShader, ArchiveButtonFeedback buttonFeedback)
        {
            this.buttonFeedback = buttonFeedback;
            this.root = root; index = root.Q("photo-index"); tiles = root.Q("photo-tiles");
            waveAnimation = new ArchiveColumnWave(tiles, 3, 12f);
            preview = root.Q("photo-preview"); image = root.Q<ArchivePhotoImageElement>("photo-image");
            photoFrame = root.Q("photo-frame"); outgoingImage = root.Q<Image>("photo-outgoing");
            heading = root.Q("photo-heading"); information = root.Q(className: "photo-information");
            root.RegisterCallback<GeometryChangedEvent>(_ => AlignCollectionNavigation());
            preview.RegisterCallback<GeometryChangedEvent>(_ => AlignCollectionNavigation());
            information.RegisterCallback<GeometryChangedEvent>(_ => AlignCollectionNavigation());
            root.Q("photos-page").RegisterCallback<GeometryChangedEvent>(_ => ResizePhotoRegions());
            cardMaterial = new Material(compositeShader) { hideFlags = HideFlags.HideAndDontSave };
            cardMaterial.SetVector("_Crop", new Vector4(0, 0, 1, 1));
            outgoingMaterial = new Material(cardMaterial) { hideFlags = HideFlags.HideAndDontSave };
            card = new RenderTexture(1024, 1024, 0, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave };
            card.Create();
            outgoingCard = new RenderTexture(1024, 1024, 0, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave };
            outgoingCard.Create();
            root.Q<Button>("photo-previous").clicked += () => buttonFeedback.Run("photo-previous", () => selectPhoto(currentCollection.Selected - 1));
            root.Q<Button>("photo-next").clicked += () => buttonFeedback.Run("photo-next", () => selectPhoto(currentCollection.Selected + 1));
        }

        private void AlignCollectionNavigation()
        {
            var navigation = root.Q("collection-navigation");
            float left = information.ChangeCoordinatesTo(navigation.parent, Vector2.zero).x;
            if (!float.IsNaN(left))
            {
                navigation.style.left = left;
                root.Q("photo-divider").style.left = left;
            }
        }

        private void ResizePhotoRegions()
        {
            float side = root.Q("photos-page").contentRect.height;
            if (side > 0 && !float.IsNaN(side))
            {
                index.style.width = side;
                photoFrame.style.width = side;
            }
        }

        public void Render(PhotoCollection collection, bool inspection, Action<int> select, Action open, Action catalog, Action sort)
        {
            currentCollection = collection;
            selectPhoto = select;
            ReleaseThumbnails(); tiles.Clear();
            index.style.display = inspection ? DisplayStyle.None : DisplayStyle.Flex;
            ResizePhotoRegions();
            if (heading.parent != information) information.Insert(0, heading);
            image.Square = false;
            photoFrame.style.display = inspection ? DisplayStyle.Flex : DisplayStyle.None;
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
                if (record.sent)
                {
                    var uploaded = new Label("Uploaded");
                    uploaded.AddToClassList("photo-tile-uploaded"); button.Add(uploaded);
                }
                button.RegisterCallback<ClickEvent>(e => { if (e.clickCount == 2) open(); });
                tiles.Add(button);
            }
            if (inspection) waveAnimation.Stop(); else waveAnimation.Start();
            var current = collection.Current;
            bool changePhoto = inspection && viewingInspection && current != null
                && motionPhotoId != null && motionPhotoId != current.id;
            int previousIndex = changePhoto ? collection.Photos.FindIndex(p => p.id == motionPhotoId) : -1;
            if (changePhoto)
            {
                AnimalPhotoProcessing.Release(outgoingProcessed);
                outgoingProcessed = processed;
                processed = null;
                outgoingBaseTilt = tilt;
                outgoingStart = incomingOffset;
            }
            if (!inspection || current == null) StopSlide();
            if (inspection && (!viewingInspection || motionPhotoId != current?.id)) idleTilt.Reset();
            viewingInspection = inspection;
            motionPhotoId = current?.id;
            root.Q("photo-delete").style.display = current != null ? DisplayStyle.Flex : DisplayStyle.None;
            root.Q<Label>("photo-name").text = current?.displayName ?? "No saved photographs";
            root.Q<Label>("photo-scientific").text = current?.scientificName ?? "";
            root.Q<Label>("photo-region").text = current?.regionName ?? "";
            root.Q("photo-uploaded").style.display = current != null && current.sent ? DisplayStyle.Flex : DisplayStyle.None;
            root.Q<Label>("photo-captured").text = current?.CapturedAt.ToString("HH:mm  yyyy.MM.dd", CultureInfo.InvariantCulture) ?? "";
            root.Q<Label>("photo-altitude").text = current == null ? "" : current.hasHeight ? $"{current.altitude:0} m" : "- m";
            root.Q<Label>("photo-position").text = current == null ? "" : Position(current);
            root.Q<Label>("photo-camera").text = current?.cameraName ?? "";
            root.Q("photo-navigation").style.display = inspection ? DisplayStyle.Flex : DisplayStyle.None;
            root.Q<Button>("photo-previous").SetEnabled(current != null && collection.Selected > 0);
            root.Q<Button>("photo-next").SetEnabled(current != null && collection.Selected < collection.Photos.Count - 1);
            var view = root.Q<Button>("photo-open"); view.style.display = inspection ? DisplayStyle.None : DisplayStyle.Flex;
            view.SetEnabled(current != null); view.clicked -= openHandler; openHandler = () => buttonFeedback.Run("photo-open", open); view.clicked += openHandler;
            var guide = root.Q<Button>("photo-catalog"); guide.SetEnabled(current != null);
            guide.clicked -= catalogHandler; catalogHandler = () => buttonFeedback.Run("photo-catalog", catalog); guide.clicked += catalogHandler;
            AnimalPhotoProcessing.Release(processed); processed = null; image.image = null;
            if (current != null)
            {
                processed = current.RestorePhoto().Render(1024); tilt = Vector2.zero;
                if (inspection) RenderCard();
                image.image = inspection ? card : processed;
                if (changePhoto) StartSlide(previousIndex >= 0 && collection.Selected < previousIndex ? -1 : 1);
            }
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
            if (idleTilt.TryGetTarget(input.magnitude > .16f, Time.unscaledDeltaTime, Vector2.zero, out Vector2 automatic))
                target = automatic;
            tilt = Vector2.Lerp(tilt, target, 1 - Mathf.Exp(-3 * Time.unscaledDeltaTime));
            RenderCard();
            if (sliding)
            {
                slideElapsed += Time.unscaledDeltaTime;
                UpdateSlide();
            }
        }

        private void RenderCard()
        {
            RenderTiltedCard(processed, card, cardMaterial, tilt);
        }

        private static void RenderTiltedCard(RenderTexture source, RenderTexture destination, Material material, Vector2 angles)
        {
            material.SetFloat("_ImageAspect", (float)source.width / source.height);
            material.SetVector("_Tilt", new Vector4(angles.x * Mathf.Deg2Rad, angles.y * Mathf.Deg2Rad, 0, 0));
            RenderTexture previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            try { GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear && destination.sRGB; Graphics.Blit(source, destination, material); }
            finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
        }

        private void StartSlide(int direction)
        {
            slideDirection = direction; slideElapsed = 0; sliding = true;
            outgoingImage.image = outgoingCard;
            outgoingImage.style.display = DisplayStyle.Flex;
            UpdateSlide();
        }

        private void UpdateSlide()
        {
            float t = Mathf.Clamp01(slideElapsed / SlideDuration);
            float eased = Mathf.SmoothStep(0, 1, t);
            // Positive yaw puts the right edge farther from the camera when sliding left.
            Vector2 outgoingTilt = Vector2.Lerp(outgoingBaseTilt,
                new Vector2(outgoingBaseTilt.x, slideDirection * SlideRetreatDegrees), eased);
            RenderTiltedCard(outgoingProcessed, outgoingCard, outgoingMaterial, outgoingTilt);
            float width = photoFrame.contentRect.width;
            incomingOffset = slideDirection * width * (1 - eased);
            image.style.translate = new Translate(incomingOffset, 0);
            outgoingImage.style.translate = new Translate(Mathf.Lerp(outgoingStart, -slideDirection * width, eased), 0);
            if (t >= 1) StopSlide();
        }

        private void StopSlide()
        {
            sliding = false; incomingOffset = 0;
            image.style.translate = new Translate(0, 0);
            outgoingImage.style.display = DisplayStyle.None;
            outgoingImage.image = null;
            AnimalPhotoProcessing.Release(outgoingProcessed); outgoingProcessed = null;
        }
        private void ReleaseThumbnails() { foreach (var texture in thumbnails) AnimalPhotoProcessing.Release(texture); thumbnails.Clear(); }
        public void Clear() { waveAnimation.Stop(); StopSlide(); viewingInspection = false; image.image = null; tiles.Clear(); ReleaseThumbnails(); AnimalPhotoProcessing.Release(processed); processed = null; }
        public void Dispose() { Clear(); AnimalPhotoProcessing.Release(card); AnimalPhotoProcessing.Release(outgoingCard); UnityEngine.Object.Destroy(cardMaterial); UnityEngine.Object.Destroy(outgoingMaterial); }
    }
}
