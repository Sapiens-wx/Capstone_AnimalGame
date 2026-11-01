using AnimalGame.Save;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.Animals.UI
{
    public sealed class CatalogView
    {
        private readonly VisualElement root;
        public CatalogView(VisualElement root) => this.root = root;
        public void Render(AnimalSpecies species)
        {
            var assets = AnimalArchiveAssets.Active;
            var photo = assets.Photos[species]; var info = assets.Info[species];
            var progress = SaveSystem.Current.Progress(species);
            int tier = progress?.Tier ?? 0;
            root.Q<Label>("catalog-name").text = tier >= 1 ? photo.DisplayName : "Unidentified species";
            root.Q<Label>("catalog-scientific").text = tier >= 1 ? photo.ScientificName : "LOCKED — send a photograph to unlock";
            root.Q<Label>("catalog-region").text = info.Region;
            root.Q<ProgressBar>("recognition").value = progress?.recognition ?? 0;
            root.Q<ProgressBar>("recognition").title = $"Species Recognition  {progress?.recognition ?? 0} / 100";
            Field("physical", info.PhysicalCharacteristics, tier, 1);
            Field("classification", info.Classification, tier, 2);
            Field("distribution", info.Distribution, tier, 2);
            Field("habitat", info.Habitat, tier, 2);
            Field("conservation", info.ConservationStatus, tier, 2);
            Field("lifespan", info.Lifespan, tier, 3);
            Field("description", info.Description, tier, 3);
            Field("activity", info.ActivityPattern, tier, 3);
            Field("diet", info.Diet, tier, 3);
            Field("social", info.SocialBehavior, tier, 3);
            var illustration = root.Q<Image>("catalog-illustration");
            illustration.image = tier >= 1 ? info.Illustration : null;
            illustration.style.display = illustration.image != null ? DisplayStyle.Flex : DisplayStyle.None;
            var distribution = root.Q<Image>("distribution-map");
            distribution.image = tier >= 2 ? info.DistributionMap : null;
            distribution.style.display = distribution.image != null ? DisplayStyle.Flex : DisplayStyle.None;
        }
        private void Field(string name, string value, int tier, int required) =>
            root.Q<Label>(name).text = tier < required
                ? required == 1 ? "Send 1 photograph to unlock" : $"Requires {(required == 2 ? 30 : 100)} recognition"
                : string.IsNullOrWhiteSpace(value) ? "Not documented" : value;
    }
}
