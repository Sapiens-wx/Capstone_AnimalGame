using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;

namespace AnimalGame.Editor
{
    /// <summary>Bakes the result view's English character set before runtime.</summary>
    public static class PhotoResultFontBaker
    {
        private const string Directory = "Assets/Prefabs/Resources/UI/Photo/";
        private const string Output = Directory + "GeosansLightStatic.asset";

        [MenuItem("Animal Game/Photo/Bake Result Font")]
        public static void Bake()
        {
            if (AssetDatabase.LoadMainAssetAtPath(Output) != null)
                throw new InvalidOperationException("Font already exists. Remove it explicitly before regenerating: " + Output);

            Font source = AssetDatabase.LoadAssetAtPath<Font>(Directory + "GeosansLight.ttf");
            if (source == null) throw new InvalidOperationException("GeosansLight.ttf was not found.");

            // All 95 printable ASCII characters: English, digits, punctuation and space.
            string characters = new string(Enumerable.Range(32, 95).Select(value => (char)value).ToArray());
            FontAsset font = FontAsset.CreateFontAsset(source, 90, 9,
                GlyphRenderMode.SDF16, 1024, 1024, AtlasPopulationMode.Dynamic, false);
            font.name = "GeosansLightStatic";
            if (!font.TryAddCharacters(characters, out string missing, true))
                throw new InvalidOperationException("Font bake could not include: " + missing);

            font.atlasPopulationMode = AtlasPopulationMode.Static;
            font.getFontFeatures = false;
            AssetDatabase.CreateAsset(font, Output);
            foreach (Texture2D atlas in font.atlasTextures)
            {
                atlas.name = font.name + " Atlas";
                AssetDatabase.AddObjectToAsset(atlas, font);
            }
            if (font.material != null)
                AssetDatabase.AddObjectToAsset(font.material, font);
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(Output, ImportAssetOptions.ForceUpdate);

            FontAsset saved = AssetDatabase.LoadAssetAtPath<FontAsset>(Output);
            if (saved.atlasPopulationMode != AtlasPopulationMode.Static || saved.getFontFeatures
                || characters.Any(character => !saved.HasCharacter(character)))
                throw new InvalidOperationException("Saved font failed character coverage validation.");
            Debug.Log($"PHOTO_FONT_BAKE_OK: {saved.characterTable.Count} characters; "
                + $"{saved.atlasWidth}x{saved.atlasHeight}; Static; runtime font features disabled.");
        }
    }
}
