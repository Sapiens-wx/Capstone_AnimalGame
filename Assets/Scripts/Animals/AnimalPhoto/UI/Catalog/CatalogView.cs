using System;
using System.Collections.Generic;
using AnimalGame.Save;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.Animals.UI
{
    public sealed class MainCatalogView
    {
        private const int Columns = 4;
        private readonly ScrollView scroll;
        private readonly VisualElement grid, tree;
        private readonly List<AnimalSpecies> animals = new();
        private readonly List<Button> tiles = new();
        private readonly ArchiveColumnWave waveAnimation;
        private Action<AnimalSpecies> open;
        private int selected, topRow;
        private float rowHeight;

        public MainCatalogView(VisualElement root)
        {
            scroll = root.Q<ScrollView>("catalog-grid-scroll");
            grid = root.Q("catalog-species-grid");
            tree = root.Q("catalog-progress-tree");
            waveAnimation = new ArchiveColumnWave(grid, Columns);
            scroll.contentViewport.RegisterCallback<GeometryChangedEvent>(_ => Resize());
        }

        public void Reset() { StopAnimation(); selected = topRow = 0; scroll.scrollOffset = Vector2.zero; }

        public void StopAnimation() => waveAnimation.Stop();

        public void Render(IEnumerable<AnimalSpecies> species, Action<AnimalSpecies> onOpen)
        {
            animals.Clear(); animals.AddRange(species); open = onOpen;
            int slots = animals.Count;
            selected = Mathf.Clamp(selected, 0, Mathf.Max(0, slots - 1));
            topRow = Mathf.Clamp(topRow, 0, Mathf.Max(0, (slots - 1) / Columns));
            grid.Clear(); tiles.Clear();
            for (int i = 0; i < slots; i++)
            {
                int index = i;
                var tile = new Button { focusable = false };
                tile.AddToClassList("catalog-card");
                var square = new VisualElement(); square.AddToClassList("catalog-card-square");
                var highlight = new VisualElement { pickingMode = PickingMode.Ignore };
                highlight.AddToClassList("catalog-card-highlight"); square.Add(highlight);
                tile.Add(square);
                if (i < animals.Count && SaveSystem.Current.Progress(animals[i])?.Tier > 0)
                {
                    var animal = animals[i];
                    var progress = SaveSystem.Current.Progress(animal);
                    int tier = progress?.Tier ?? 0;
                    var preview = new Image { image = AnimalArchiveAssets.Active.Info[animal].CatalogPreview, scaleMode = ScaleMode.ScaleToFit };
                    preview.AddToClassList("catalog-card-preview"); square.Add(preview);
                    var name = new Label(AnimalArchiveAssets.Active.Photos[animal].DisplayName);
                    name.AddToClassList("catalog-card-name"); tile.Add(name);
                    var status = new VisualElement(); status.AddToClassList("catalog-card-status");
                    for (int level = 2; level >= 0; level--)
                    {
                        var circle = level < tier ? RewardIcon() : new VisualElement();
                        if (level >= tier) circle.AddToClassList("catalog-tier-circle");
                        status.Add(circle);
                    }
                    var percent = new Label($"{Mathf.Clamp(progress?.recognition ?? 0, 0, 100)}%  Identified");
                    percent.AddToClassList("catalog-card-percent"); status.Add(percent); tile.Add(status);
                }
                highlight.BringToFront();
                tile.clicked += () => { selected = index; UpdateSelection(); Confirm(); };
                grid.Add(tile); tiles.Add(tile);
            }
            Resize(); UpdateSelection();
            waveAnimation.Start();
        }

        public void Move(int x, int y)
        {
            if (tiles.Count == 0) return;
            int row = selected / Columns, column = selected % Columns;
            int nextRow = Mathf.Clamp(row - y, 0, (tiles.Count - 1) / Columns);
            selected = Mathf.Min(tiles.Count - 1, nextRow * Columns + Mathf.Clamp(column + x, 0, Columns - 1));
            // Both vertical directions place an out-of-view target row at the top.
            if (nextRow < topRow || nextRow >= topRow + 2) topRow = nextRow;
            UpdateSelection();
        }

        public void Confirm() { if (selected < animals.Count) open(animals[selected]); }

        private void Resize()
        {
            float height = scroll.contentViewport.layout.height;
            float width = scroll.contentViewport.layout.width;
            if (float.IsNaN(height) || float.IsNaN(width) || height <= 0 || width <= 0) return;
            rowHeight = height / 2;
            float side = Mathf.Max(0, Mathf.Min(width / Columns - 32, rowHeight - 80));
            foreach (var tile in tiles)
            {
                tile.style.height = rowHeight;
                var square = tile.Q(className: "catalog-card-square");
                square.style.width = square.style.height = side;
            }
            // Allow the last row to reach the top too.
            grid.style.paddingBottom = rowHeight;
            scroll.scrollOffset = new Vector2(0, topRow * rowHeight);
        }

        private void UpdateSelection()
        {
            for (int i = 0; i < tiles.Count; i++) tiles[i].EnableInClassList("catalog-card-selected", i == selected);
            scroll.scrollOffset = new Vector2(0, topRow * rowHeight);
        }

        public void RenderProgress(int count)
        {
            tree.Clear();
            for (int tick = 0; tick <= 9; tick++)
            {
                var row = new VisualElement(); row.AddToClassList("catalog-tree-tick");
                row.style.top = Length.Percent(tick * 100f / 9);
                row.EnableInClassList("reached", tick <= count);
                bool milestone = tick == 3 || tick == 7;
                row.EnableInClassList("milestone", milestone);
                row.EnableInClassList("origin", tick == 0);
                row.EnableInClassList("current", tick == count && !milestone && tick != 0);
                foreach (string part in new[] { "left", "right" })
                {
                    var line = new VisualElement(); line.AddToClassList("tree-tick-" + part); row.Add(line);
                }
                if (tick < 9)
                {
                    var segment = new VisualElement(); segment.AddToClassList("tree-trunk");
                    segment.EnableInClassList("filled", tick < count); row.Add(segment);
                }
                if (milestone)
                {
                    var frame = new VisualElement(); frame.AddToClassList("tree-reward-frame"); row.Add(frame);
                }
                if (tick == 0 || milestone || tick == count)
                {
                    var label = new Label(tick.ToString()); label.AddToClassList("tree-tick-number"); row.Add(label);
                    if (milestone) { var icon = RewardIcon(); icon.AddToClassList("tree-reward-icon"); row.Add(icon); }
                }
                tree.Add(row);
            }
        }

        private static VisualElement RewardIcon()
        {
            var icon = new VisualElement(); icon.AddToClassList("catalog-reward-icon");
            var inner = new VisualElement(); inner.AddToClassList("catalog-reward-inner");
            var dot = new VisualElement(); dot.AddToClassList("catalog-reward-dot");
            inner.Add(dot); icon.Add(inner); return icon;
        }
    }

    public sealed class CatalogView
    {
        private readonly VisualElement root;
        public CatalogView(VisualElement root)
        {
            this.root = root;
            root.RegisterCallback<GeometryChangedEvent>(_ => ResizeHeader());
            root.Q("species-page").RegisterCallback<GeometryChangedEvent>(_ => ResizeHeader());
            root.Q("catalog-name").RegisterCallback<GeometryChangedEvent>(_ => ResizeHeader());
            root.Q("catalog-details").RegisterCallback<GeometryChangedEvent>(_ => AlignScientificTop());
            root.Q("catalog-sidebar").RegisterCallback<GeometryChangedEvent>(_ => AlignScientificTop());
            root.Q("sidebar-species-previous").RegisterCallback<GeometryChangedEvent>(_ => AlignScientificTop());
            root.Q("catalog-scientific").RegisterCallback<GeometryChangedEvent>(_ => AlignScientificTop());
        }

        private void ResizeHeader()
        {
            var name = root.Q<Label>("catalog-name");
            if (float.IsNaN(name.layout.width)) return;
            var navigation = root.Q("species-navigation");
            var nameRight = name.ChangeCoordinatesTo(navigation.parent, new Vector2(name.layout.width, 0));
            navigation.style.left = nameRight.x + root.layout.width * .03f;
            var scientific = root.Q<ArchiveScientificNameElement>("catalog-scientific");
            scientific.style.width = name.layout.width;
            scientific.TopLineRatio = 1f;
            AlignScientificTop();
        }

        private void AlignScientificTop()
        {
            if (root.Q("species-page").resolvedStyle.display == DisplayStyle.None) return;
            var scientific = root.Q<ArchiveScientificNameElement>("catalog-scientific");
            var previousSpecies = root.Q("sidebar-species-previous");
            scientific.TopLineOffset = previousSpecies.ChangeCoordinatesTo(scientific, Vector2.zero).y + 12f;
        }
        public void Render(AnimalSpecies species)
        {
            var assets = AnimalArchiveAssets.Active;
            var photo = assets.Photos[species]; var info = assets.Info[species];
            var progress = SaveSystem.Current.Progress(species);
            int tier = progress?.Tier ?? 0;
            root.Q<CatalogRecognitionElement>("recognition").Value = progress?.recognition ?? 0;
            root.Q<Label>("catalog-name").text = tier >= 1 ? photo.DisplayName : "More Photos Required";
            root.Q("catalog-name").style.opacity = tier >= 1 ? 1f : .4f;
            root.Q<Label>("catalog-scientific").text = tier >= 1 ? photo.ScientificName : "LOCKED — take a photograph to unlock";
            root.Q<Label>("catalog-region").text = info.Region;
            root.Q<CatalogRulerElement>("physical-ruler").Bind(info, root.Q<CatalogHeadingElement>("physical-heading"));
            Field("classification", info.Classification, tier, 2);
            Field("distribution", info.Distribution, tier, 2);
            Field("habitat", info.Habitat, tier, 2);
            Field("conservation", info.ConservationStatus, tier, 2);
            root.Q<CatalogConservationMeter>("conservation-meter").Bind(info.ConservationStatus, tier >= 2);
            Field("lifespan", info.Lifespan, tier, 3);
            Field("description", info.Description, tier, 3);
            Field("activity", info.ActivityPattern, tier, 3);
            Field("diet", info.Diet, tier, 3);
            Field("social", info.SocialBehavior, tier, 3);
            var distribution = root.Q<Image>("distribution-map");
            distribution.image = tier >= 2 ? info.DistributionMap : null;
            distribution.style.display = distribution.image != null ? DisplayStyle.Flex : DisplayStyle.None;
            root.Q<Label>("distribution-unidentified").style.display = tier < 2 ? DisplayStyle.Flex : DisplayStyle.None;
            root.Q<Label>("distribution").style.display = tier < 2 || distribution.image != null
                ? DisplayStyle.None : DisplayStyle.Flex;
        }
        private void Field(string name, string value, int tier, int required)
        {
            bool locked = tier < required;
            var label = root.Q<Label>(name);
            root.Q(name + "-section").style.opacity = locked ? .4f : 1f;
            label.text = locked ? "More Photos Required"
                : string.IsNullOrWhiteSpace(value) ? "Not documented" : value;
            if (name == "classification" && !locked)
            {
                label.enableRichText = true;
                foreach (string rank in new[] { "Class", "Order", "Family", "Genus" })
                    label.text = label.text.Replace(" " + rank, " <color=#858585>" + rank + "</color>");
            }
        }
    }
}
