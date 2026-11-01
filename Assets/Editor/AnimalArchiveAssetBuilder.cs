using System.Collections.Generic;
using System.Linq;
using AnimalGame.Animals;
using AnimalGame.Animals.UI;
using AnimalGame.Save;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.Editor
{
    public sealed class AnimalArchiveAssetBuilder : IPreprocessBuildWithReport
    {
        private const string Folder = "Assets/Prefabs/Resources/UI/AnimalCatalog/";
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report) => UpdateReferences();

        [MenuItem("Animal Game/Photos/Update Archive Asset References")]
        public static void UpdateReferences()
        {
            var assets = AssetDatabase.LoadAssetAtPath<AnimalArchiveAssets>(Folder + "AnimalArchiveAssets.asset");
            assets.speciesConfigs = AssetDatabase.FindAssets("t:AnimalSpeciesConfig", new[] { "Assets/Data/Animals" })
                .Select(id => AssetDatabase.LoadAssetAtPath<AnimalSpeciesConfig>(AssetDatabase.GUIDToAssetPath(id))).ToList();
            var seen = new HashSet<Texture2D>(); assets.images.Clear();
            foreach (var animal in assets.photoLibrary.Animals)
                foreach (var state in animal.States)
                    foreach (var photo in state.Photos)
                        if (seen.Add(photo.Image)) assets.images.Add(new AnimalArchiveAssets.ImageReference {
                            id = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(photo.Image)), image = photo.Image });
            EditorUtility.SetDirty(assets); AssetDatabase.SaveAssets();
        }

        [MenuItem("Animal Game/Photos/Create Archive Assets")]
        public static void CreateAssets()
        {
            System.IO.Directory.CreateDirectory("Assets/Data/Saves");
            System.IO.Directory.CreateDirectory("Assets/Data/AnimalInfo");
            var save = GetOrCreate<GameSave>("Assets/Data/Saves/GameSave.asset");
            var info = GetOrCreate<AnimalInfoLibrary>("Assets/Data/AnimalInfo/AnimalInfoLibrary.asset");
            var assets = GetOrCreate<AnimalArchiveAssets>(Folder + "AnimalArchiveAssets.asset");
            assets.defaultSave = save; assets.animalInfo = info;
            assets.photoLibrary = AssetDatabase.LoadAssetAtPath<PhotoLibrary>("Assets/Data/AnimalPhoto/AnimalPhotoLibrary.asset");
            var serialized = new SerializedObject(info); var animals = serialized.FindProperty("animals");
            var existing = new HashSet<AnimalSpecies>();
            for (int i = 0; i < animals.arraySize; i++) existing.Add((AnimalSpecies)animals.GetArrayElementAtIndex(i).FindPropertyRelative("Species").intValue);
            foreach (var photo in assets.photoLibrary.Animals)
            {
                if (!existing.Add(photo.Species)) continue;
                int i = animals.arraySize++; var entry = animals.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("Species").intValue = (int)photo.Species;
                entry.FindPropertyRelative("Region").stringValue = photo.RegionName;
                foreach (string field in new[] { "PhysicalCharacteristics", "Classification", "Distribution", "Habitat", "ConservationStatus", "Lifespan", "Description", "ActivityPattern", "Diet", "SocialBehavior" })
                    entry.FindPropertyRelative(field).stringValue = "";
                entry.FindPropertyRelative("Illustration").objectReferenceValue = null;
                entry.FindPropertyRelative("DistributionMap").objectReferenceValue = null;
                // Text supplied by the Catalog reference image, editable in the info library.
                if (photo.Species == AnimalSpecies.AmericanBison)
                {
                    entry.FindPropertyRelative("PhysicalCharacteristics").stringValue = "Shoulder height: 1.5–2.0 m\nBody mass: 320–1000 kg\nTotal length: 2.4–4.4 m";
                    entry.FindPropertyRelative("Classification").stringValue = "Mammalia Class\nArtiodactyla Order\nBovidae Family\nBison Genus";
                    entry.FindPropertyRelative("Distribution").stringValue = "North America";
                    entry.FindPropertyRelative("Habitat").stringValue = "Grasslands and prairies";
                    entry.FindPropertyRelative("ConservationStatus").stringValue = "Near Threatened";
                    entry.FindPropertyRelative("Lifespan").stringValue = "15–20 years";
                    entry.FindPropertyRelative("Diet").stringValue = "Herbivorous";
                    entry.FindPropertyRelative("SocialBehavior").stringValue = "Gregarious";
                    entry.FindPropertyRelative("ActivityPattern").stringValue = "Primarily diurnal";
                    entry.FindPropertyRelative("Description").stringValue = "The American bison is the largest land mammal in North America. Once roaming the Great Plains in vast herds, it is a powerful herbivore known for its massive shoulder hump and thick fur. As a keystone species, it plays a vital role in maintaining grassland ecosystems.";
                }
            }
            serialized.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(assets);
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(Folder + "AnimalArchivePanel.asset");
            if (panel == null)
            {
                panel = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Prefabs/Resources/UI/Photo/PhotoResultSettings.asset"));
                panel.name = "Animal Archive Panel"; panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                panel.referenceResolution = new Vector2Int(1920, 1080); panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                panel.match = .5f; panel.sortingOrder = 300;
                AssetDatabase.CreateAsset(panel, Folder + "AnimalArchivePanel.asset");
            }
            string path = Folder + "AnimalArchiveUI.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                var root = new GameObject("AnimalArchiveUI");
                var document = root.AddComponent<UIDocument>(); document.panelSettings = panel;
                document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Folder + "AnimalArchive.uxml");
                root.AddComponent<AnimalArchiveUI>().compositeShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/PhotoResultComposite.shader");
                PrefabUtility.SaveAsPrefabAsset(root, path); Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets(); UpdateReferences();
            Debug.Log($"Archive assets ready: {assets.images.Count} source images, {assets.speciesConfigs.Count} species configs.");
        }

        private static T GetOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) { asset = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset, path); }
            return asset;
        }
    }
}
