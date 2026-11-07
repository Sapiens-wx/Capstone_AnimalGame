using System;
using System.Collections.Generic;
using System.Linq;
using AnimalGame.Save;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.Animals.Communication
{
    public sealed class EarthlinePhotoPicker : IDisposable
    {
        public const int Columns = 4, VisibleRows = 3;
        private readonly VisualElement root, grid;
        private readonly ScrollView scroll;
        private readonly List<int> available = new();
        private readonly HashSet<int> selected = new();
        private readonly List<Button> tiles = new();
        private readonly List<RenderTexture> thumbnails = new();
        private GameSave save;
        private int highlighted, topRow;
        public int Highlighted => highlighted;
        public int TopRow => topRow;
        public int AvailableCount => available.Count;
        public int SelectedCount => selected.Count;
        public int[] SelectedIndices => available.Where(selected.Contains).ToArray();

        public EarthlinePhotoPicker(VisualElement root)
        {
            this.root = root;
            scroll = root.Q<ScrollView>("picker-scroll"); grid = root.Q("picker-tiles");
            scroll.contentViewport.RegisterCallback<GeometryChangedEvent>(_ => Resize());
        }

        public void Bind(GameSave data, string contact)
        {
            save = data; highlighted = topRow = 0; available.Clear(); selected.Clear();
            for (int i = 0; i < save.photos.Count; i++) if (!save.photos[i].sent) available.Add(i);
            root.Q<Label>("picker-title").text = "Photos for " + contact;
            root.Q("picker-empty").style.display = available.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            Render();
        }

        public void Move(int x, int y)
        {
            if (available.Count == 0) return;
            int row = highlighted / Columns, column = highlighted % Columns;
            int targetRow = Mathf.Clamp(row - y, 0, (available.Count - 1) / Columns);
            int targetColumn = Mathf.Clamp(column + x, 0, Columns - 1);
            highlighted = Math.Min(available.Count - 1, targetRow * Columns + targetColumn);
            int oldTop = topRow;
            if (targetRow < topRow) topRow = targetRow;
            else if (targetRow >= topRow + VisibleRows) topRow = targetRow - VisibleRows + 1;
            if (oldTop != topRow) Render(); else UpdateSelection();
        }

        public void ToggleHighlighted()
        {
            if (available.Count == 0) return;
            int index = available[highlighted];
            if (!selected.Add(index)) selected.Remove(index);
            UpdateSelection();
        }

        public void ToggleAll()
        {
            if (selected.Count == available.Count) selected.Clear();
            else { selected.Clear(); foreach (int index in available) selected.Add(index); }
            UpdateSelection();
        }

        private void Render()
        {
            Clear();
            for (int i = topRow * Columns; i < Math.Min(available.Count, (topRow + VisibleRows) * Columns); i++)
            {
                int local = i;
                var record = save.photos[available[i]];
                var tile = new Button(() => { highlighted = local; ToggleHighlighted(); }) { focusable = false };
                tile.AddToClassList("picker-tile");
                var picture = new VisualElement(); picture.AddToClassList("picker-picture");
                var thumbnail = record.RestorePhoto().Render(256); thumbnails.Add(thumbnail);
                var image = new Image { image = thumbnail, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                image.AddToClassList("picker-image"); picture.Add(image);
                var number = new Label($"{i + 1:000}") { pickingMode = PickingMode.Ignore }; number.AddToClassList("picker-number"); picture.Add(number);
                tile.Add(picture);
                var name = new Label(record.displayName) { pickingMode = PickingMode.Ignore }; name.AddToClassList("picker-name"); tile.Add(name);
                var region = new Label(record.regionName) { pickingMode = PickingMode.Ignore }; region.AddToClassList("picker-region"); tile.Add(region);
                grid.Add(tile); tiles.Add(tile);
            }
            Resize(); UpdateSelection();
        }

        private void Resize()
        {
            float width = scroll.contentViewport.layout.width, height = scroll.contentViewport.layout.height;
            if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0) return;
            float rowHeight = height / VisibleRows;
            float side = Mathf.Max(1, Mathf.Min(width * .24f - 14, rowHeight - 70));
            foreach (var tile in tiles)
            {
                tile.style.height = rowHeight - 6;
                tile.style.marginBottom = 6;
                var picture = tile.Q(className: "picker-picture"); picture.style.width = picture.style.height = side;
            }
            scroll.scrollOffset = Vector2.zero;
        }

        private void UpdateSelection()
        {
            for (int i = 0; i < tiles.Count; i++)
            {
                int local = topRow * Columns + i;
                tiles[i].EnableInClassList("highlighted", local == highlighted);
                tiles[i].EnableInClassList("chosen", selected.Contains(available[local]));
            }
            root.Q<Label>("picker-count").text = $"{selected.Count} selected";
            root.Q<Label>("picker-select-caption").text = available.Count > 0 && selected.Count == available.Count ? "Deselect all" : "Select all";
            root.Q<Label>("picker-send-caption").text = $"Send {selected.Count} photos";
            root.Q<Button>("picker-send").SetEnabled(selected.Count > 0);
            root.Q<Button>("picker-select-all").SetEnabled(available.Count > 0);
        }

        public void Clear()
        {
            grid.Clear(); tiles.Clear();
            foreach (var texture in thumbnails) AnimalPhotoProcessing.Release(texture);
            thumbnails.Clear();
        }
        public void Dispose() => Clear();
    }
}