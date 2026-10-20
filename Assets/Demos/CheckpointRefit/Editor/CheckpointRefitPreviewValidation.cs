using System;
using System.IO;
using System.Linq;
using System.Text;
using AnimalGame.RobotMap;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AnimalGame.Demos.CheckpointRefit.Editor
{
    /// <summary>Assertions shared by the opt-in smoke sessions; never edits saved assets.</summary>
    internal static class CheckpointRefitPreviewValidation
    {
        internal static void CheckSelection(CheckpointRefitView view)
        {
            VisualElement root = view.Document.rootVisualElement;
            for (int i = 0; i < 5; i++)
            {
                Button button = root.Q<Button>("refit-slot-" + i);
                Require(button != null, "Missing body slot " + i + ".");
                bool selected = i == view.SelectedSlotIndex;
                Require(button.ClassListContains("refit-selected") == selected,
                    "Selected class differs from selected body slot " + i + ".");
                Require(IsGold(button.resolvedStyle.color) == selected,
                    "Yellow text differs from selected body slot " + i + ".");
                Color bar = button.resolvedStyle.borderLeftColor;
                Require((bar.a > .01f && IsGold(bar)) == selected,
                    "Yellow left bar differs from selected body slot " + i + ".");
                Color fill = button.resolvedStyle.backgroundColor;
                Require((fill.a > .02f && IsGold(fill)) == selected,
                    "Yellow background differs from selected body slot " + i + ".");
            }
        }

        internal static RenderTexture CheckActualPlayerImage(CheckpointRefitView view, RobotMover player)
        {
            VisualElement root = view.Document.rootVisualElement;
            Image image = root.Q<Image>("refit-player-preview");
            Require(image != null && image.resolvedStyle.display != DisplayStyle.None,
                "Unit preview must use the player's rendered Image element.");
            Require(root.Q<VisualElement>("refit-robot-preview") == null,
                "The fabricated robot drawing must not remain in the unit preview.");
            RenderTexture texture = image.image as RenderTexture;
            Require(texture != null && texture.IsCreated() && texture.width > 32 && texture.height > 32,
                "Unit preview must contain a live player RenderTexture.");
            Require(player.GetComponentsInChildren<SpriteRenderer>(true).Any(renderer => renderer.sprite != null),
                "The bound gameplay player must provide actual sprite artwork for its preview.");

            RenderTexture previous = RenderTexture.active;
            Texture2D pixels = null;
            try
            {
                RenderTexture.active = texture;
                pixels = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                pixels.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                pixels.Apply();
                Color32[] samples = pixels.GetPixels32();
                int visible = samples.Count(pixel => pixel.a > 100 && (pixel.r > 20 || pixel.g > 20 || pixel.b > 20));
                Require(visible > 100, "Player preview is empty or contains only its background.");
                Require(visible < samples.Length * .9f, "Player preview is an unframed full-screen fill.");
            }
            finally
            {
                RenderTexture.active = previous;
                if (pixels != null) Object.DestroyImmediate(pixels);
            }
            return texture;
        }

        internal static void CheckPreviewReleased(CheckpointRefitView view, RenderTexture texture)
        {
            if (view != null)
            {
                // UIDocument detaches and drops its root when its GameObject is disabled.
                // The retained render-target reference below must still have been released.
                VisualElement root = view.Document != null ? view.Document.rootVisualElement : null;
                Image image = root != null ? root.Q<Image>("refit-player-preview") : null;
                Require(image == null || image.image == null, "A closed maintenance screen retains its player image.");
            }
            Require(texture == null || !texture.IsCreated(), "Closing maintenance retains a player preview render target.");
        }

        internal static void CheckSourceArtworkDependency(RobotMover player)
        {
            RobotMarkerView marker = player.GetComponent<RobotMarkerView>();
            Require(marker != null && marker.MarkerVisualRoot != null, "Player has no bound marker visual root.");
            SpriteRenderer source = marker.MarkerVisualRoot.GetComponentsInChildren<SpriteRenderer>(false)
                .FirstOrDefault(renderer => renderer.enabled && !renderer.forceRenderingOff && renderer.sprite != null
                    && renderer.color.a > .1f && renderer.color.maxColorComponent > .3f);
            Require(source != null, "Player has no visible artwork sprite for the source dependency check.");
            string before = AppearanceFingerprint(player);
            Color original = source.color;
            var preview = new RefitPlayerPreview();
            RenderTexture target = null;
            try
            {
                Require(preview.Capture(player) && preview.HasImage && preview.CapturedRendererCount > 0,
                    "Player snapshot did not capture actual marker renderers.");
                target = preview.Texture;
                uint initial = PixelSignature(target);
                Require(before == AppearanceFingerprint(player), "Capturing a player preview modified source artwork or transforms.");
                // The snapshot must follow an actual gameplay sprite, rather than a fixed substitute drawing.
                source.color = new Color(.95f, .05f, .75f, original.a);
                Require(preview.Capture(player), "The changed source artwork failed to render.");
                Require(initial != PixelSignature(preview.Texture),
                    "Preview pixels did not respond to a change in the actual player's artwork.");
            }
            finally
            {
                source.color = original;
                preview.Dispose();
            }
            Require(before == AppearanceFingerprint(player), "The source artwork dependency check did not restore the real player exactly.");
            Require(!preview.HasImage && (target == null || !target.IsCreated()),
                "Disposing the independent preview retains its image or render target.");
            CheckOrientation(player, marker);
        }

        private static void CheckOrientation(RobotMover player, RobotMarkerView marker)
        {
            SpriteRenderer arrow = marker.MarkerVisualRoot.Find("Direction Indicator")?.GetComponent<SpriteRenderer>();
            SpriteRenderer body = marker.MarkerVisualRoot.GetComponentsInChildren<SpriteRenderer>(false)
                .FirstOrDefault(renderer => renderer.name == "Body Artwork");
            Require(arrow != null && body != null && arrow.enabled && body.enabled,
                "Orientation fixture requires the real direction indicator and body artwork.");
            Matrix4x4 worldToPreview = Matrix4x4.TRS(marker.MarkerVisualRoot.position,
                Quaternion.Euler(0f, 0f, player.transform.eulerAngles.z), Vector3.one).inverse;
            float expectedY = SourceArtworkCentroid(arrow, worldToPreview).y - SourceArtworkCentroid(body, worldToPreview).y;
            Require(Mathf.Abs(expectedY) > .001f, "The source indicator/body have no vertical separation for the orientation fixture.");
            Color arrowColor = arrow.color, bodyColor = body.color;
            string before = AppearanceFingerprint(player);
            var preview = new RefitPlayerPreview();
            RenderTexture previous = RenderTexture.active;
            Texture2D pixels = null;
            try
            {
                arrow.color = new Color(.95f, .05f, .75f, arrowColor.a);
                body.color = new Color(.05f, .95f, .1f, bodyColor.a);
                Require(preview.Capture(player), "The orientation probe could not capture the actual player.");
                RenderTexture texture = preview.Texture;
                RenderTexture.active = texture;
                pixels = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                pixels.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                pixels.Apply();
                Color32[] samples = pixels.GetPixels32();
                double arrowY = 0, bodyY = 0, arrowWeight = 0, bodyWeight = 0;
                for (int i = 0; i < samples.Length; i++)
                {
                    Color32 pixel = samples[i];
                    if (pixel.a < 20) continue;
                    double y = i / texture.width + .5d;
                    if (pixel.r > 60 && pixel.b > 60 && pixel.r > pixel.g * 1.8f && pixel.b > pixel.g * 1.8f)
                    { arrowY += y * pixel.a; arrowWeight += pixel.a; }
                    if (pixel.g > 60 && pixel.g > pixel.r * 1.8f && pixel.g > pixel.b * 1.8f)
                    { bodyY += y * pixel.a; bodyWeight += pixel.a; }
                }
                Require(arrowWeight > 100 && bodyWeight > 100, "The orientation probe cannot locate its tinted source artwork.");
                float renderedY = (float)(arrowY / arrowWeight - bodyY / bodyWeight);
                Require(renderedY * expectedY > 0f,
                    "Player preview is vertically inverted: indicator/body ordering differs from the actual source artwork."
                    + " Source delta=" + expectedY + ", rendered delta=" + renderedY + ".");
            }
            finally
            {
                RenderTexture.active = previous;
                if (pixels != null) Object.DestroyImmediate(pixels);
                arrow.color = arrowColor; body.color = bodyColor;
                preview.Dispose();
            }
            Require(before == AppearanceFingerprint(player), "The orientation probe changed the gameplay player's artwork.");
        }

        private static Vector3 SourceArtworkCentroid(SpriteRenderer renderer, Matrix4x4 worldToPreview)
        {
            Sprite sprite = renderer.sprite;
            Require(sprite != null && !sprite.packed, "Orientation fixture expects the player's original unpacked artwork.");
            string path = AssetDatabase.GetAssetPath(sprite.texture);
            Require(File.Exists(path), "Cannot read the original player artwork for orientation verification.");
            Texture2D pixels = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Require(ImageConversion.LoadImage(pixels, File.ReadAllBytes(path), false), "Cannot decode source player artwork.");
                Color32[] samples = pixels.GetPixels32();
                Vector3 weighted = Vector3.zero;
                double weight = 0;
                Matrix4x4 toPreview = worldToPreview * renderer.transform.localToWorldMatrix;
                for (int y = 0; y < pixels.height; y++)
                for (int x = 0; x < pixels.width; x++)
                {
                    byte alpha = samples[y * pixels.width + x].a;
                    if (alpha == 0) continue;
                    float tx = (x + .5f) * sprite.texture.width / pixels.width;
                    float ty = (y + .5f) * sprite.texture.height / pixels.height;
                    if (!sprite.rect.Contains(new Vector2(tx, ty))) continue;
                    Vector3 local = new Vector3((tx - sprite.rect.x - sprite.pivot.x) / sprite.pixelsPerUnit,
                        (ty - sprite.rect.y - sprite.pivot.y) / sprite.pixelsPerUnit, 0f);
                    if (renderer.flipX) local.x = -local.x;
                    if (renderer.flipY) local.y = -local.y;
                    weighted += toPreview.MultiplyPoint3x4(local) * alpha;
                    weight += alpha;
                }
                Require(weight > 0, "Source player artwork contains no visible pixels.");
                return weighted / (float)weight;
            }
            finally { Object.DestroyImmediate(pixels); }
        }

        private static uint PixelSignature(RenderTexture texture)
        {
            RenderTexture previous = RenderTexture.active;
            Texture2D pixels = null;
            try
            {
                RenderTexture.active = texture;
                pixels = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                pixels.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                pixels.Apply();
                uint hash = 2166136261;
                foreach (Color32 pixel in pixels.GetPixels32())
                {
                    hash = unchecked((hash ^ pixel.r) * 16777619);
                    hash = unchecked((hash ^ pixel.g) * 16777619);
                    hash = unchecked((hash ^ pixel.b) * 16777619);
                    hash = unchecked((hash ^ pixel.a) * 16777619);
                }
                return hash;
            }
            finally
            {
                RenderTexture.active = previous;
                if (pixels != null) Object.DestroyImmediate(pixels);
            }
        }

        internal static string AppearanceFingerprint(RobotMover player)
        {
            var value = new StringBuilder();
            foreach (SpriteRenderer renderer in player.GetComponentsInChildren<SpriteRenderer>(true))
            {
                value.Append(renderer.GetInstanceID()).Append('|').Append(renderer.enabled).Append('|')
                    .Append(renderer.gameObject.activeSelf).Append('|')
                    .Append(renderer.sprite != null ? renderer.sprite.GetInstanceID() : 0).Append('|')
                    .Append(renderer.color.ToString("R")).Append('|').Append(renderer.flipX).Append('|').Append(renderer.flipY)
                    .Append('|').Append(renderer.transform.localPosition.ToString("R"))
                    .Append('|').Append(renderer.transform.localRotation.ToString("R"))
                    .Append('|').Append(renderer.transform.localScale.ToString("R")).Append(';');
            }
            return value.ToString();
        }

        private static bool IsGold(Color color) => color.r > .7f && color.g > .6f && color.b < .5f
            && color.r > color.b + .2f && color.g > color.b + .2f;

        private static void Require(bool condition, string message) => CheckpointRefitValidation.Require(condition, message);
    }
}
