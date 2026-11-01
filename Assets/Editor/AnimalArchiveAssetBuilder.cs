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
            var seen = new HashSet<Texture2D>(); assets.images.Clear();
            foreach (var animal in assets.animalInfo.Photos.Values)
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
            System.IO.Directory.CreateDirectory("Assets/Data/Animals/Info");
            var save = GetOrCreate<GameSave>("Assets/Data/Saves/GameSave.asset");
            var info = GetOrCreate<AnimalInfoLibrary>("Assets/Data/Animals/AnimalInfoLibrary.asset");
            var assets = GetOrCreate<AnimalArchiveAssets>(Folder + "AnimalArchiveAssets.asset");
            assets.defaultSave = save; assets.animalInfo = info;
            EditorUtility.SetDirty(assets);
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
            Debug.Log($"Archive assets ready: {assets.images.Count} source images, {assets.SpeciesConfigs.Count} species configs.");
        }

        private static T GetOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) { asset = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset, path); }
            return asset;
        }
    }
}
