using System;
using System.IO;
using System.Linq;
using System.Text;
using AnimalGame.RobotMap;
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
            for (int i = 0; i < view.SlotCount; i++)
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

        internal static void CheckModulePresentation(CheckpointRefitView view)
        {
            VisualElement root = view.Document.rootVisualElement;
            string[] slots = { "LEGS", "CAMERA", "BACK", "EXPANSION" };
            Require(view.SlotCount == slots.Length, "The maintenance demo must have four body slots.");
            for (int i = 0; i < slots.Length; i++)
            {
                Button slot = root.Q<Button>("refit-slot-" + i);
                Require(slot != null && slot.text == slots[i], "Unexpected maintenance body slot " + i + ".");
            }
            Require(root.Q<Button>("refit-slot-4") == null, "The removed ARMS or SENSORS slot remains in the layout.");
            string visibleText = string.Join(" ", root.Query<TextElement>().ToList()
                .Where(IsShown).Select(element => element.text));
            Require(visibleText.IndexOf("UNKNOWN", StringComparison.OrdinalIgnoreCase) < 0
                && visibleText.IndexOf("LOCKED", StringComparison.OrdinalIgnoreCase) < 0,
                "Unavailable equipment must use a slash presentation instead of UNKNOWN / LOCKED text.");
            bool installedSlot = view.SelectedSlotIndex == 0 || view.SelectedSlotIndex == 1;
            for (int module = 0; module < view.ModuleCount; module++)
            {
                Button card = root.Q<Button>("refit-module-" + module);
                Require(card != null, "Missing module card " + module + ".");
                bool installed = installedSlot && module == 0;
                Require(card.ClassListContains("refit-module-unavailable") != installed,
                    "Installed and undiscovered module presentation differs from the initial configuration.");
                VisualElement slash = card.Q<VisualElement>(className: "refit-module-slash");
                Require(slash != null && IsShown(slash) != installed,
                    "Undiscovered module cards must be crossed through; installed cards must retain their labels.");
                string text = string.Join(" ", card.Query<TextElement>().ToList().Where(IsShown).Select(element => element.text)).Trim();
                if (installed)
                {
                    string title = view.SelectedSlotIndex == 0 ? "TRACKS" : "ZOOM 300";
                    Require(text.Contains(title) && text.Contains("INSTALLED"),
                        "The initial configuration must show " + title + " as installed.");
                }
                else Require(string.IsNullOrWhiteSpace(text), "Unavailable module bars must have no name, number, or locked text.");
            }
            bool selectedInstalled = installedSlot && view.SelectedModuleIndex == 0;
            Require(view.IsSelectedModuleInstalled == selectedInstalled, "Selected module installation state is inconsistent.");
            Button equip = root.Q<Button>("refit-equip");
            Require(equip != null && !equip.enabledInHierarchy, "This presentation demo must never expose a live equip action.");
            Require(!root.Query<Button>().ToList().Any(button => button.enabledInHierarchy
                && string.Equals((button.text ?? string.Empty).Trim(), "Equip", StringComparison.OrdinalIgnoreCase)),
                "The UI must never offer an enabled Equip action in this demo.");
            if (selectedInstalled && view.SelectedSlotIndex == 1)
            {
                Label hint = root.Q<Label>("refit-equip-hint");
                Require(hint != null && hint.text.IndexOf("GOOD", StringComparison.OrdinalIgnoreCase) >= 0,
                    "The installed Zoom 300 lens must report good condition.");
            }
        }

        private static bool IsShown(VisualElement element)
        {
            for (VisualElement current = element; current != null; current = current.parent)
            {
                // Inline presentation changes are immediate; the next panel layout resolves them later.
                DisplayStyle display = current.style.display.keyword == StyleKeyword.Null
                    ? current.resolvedStyle.display : current.style.display.value;
                if (display == DisplayStyle.None || current.resolvedStyle.visibility == Visibility.Hidden) return false;
            }
            return true;
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
            Require(texture != null && texture.IsCreated() && texture.width >= 1536 && texture.height >= 1536,
                "Unit preview must contain a high-resolution live player RenderTexture.");
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
                .FirstOrDefault(renderer => renderer.name == "Body Artwork" && renderer.enabled && !renderer.forceRenderingOff && renderer.sprite != null
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
                Require(preview.ReconstructedBodyRendererCount >= 2,
                    "The known low-resolution body fill and outline must use the source-driven crisp preview path.");
                Require(preview.ExcludedDirectionIndicatorCount > 0,
                    "The direction indicator must be excluded from the player's equipment preview.");
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
            CheckDirectionIndicatorExcluded(player, marker);
        }

        private static void CheckDirectionIndicatorExcluded(RobotMover player, RobotMarkerView marker)
        {
            SpriteRenderer arrow = marker.MarkerVisualRoot.Find("Direction Indicator")?.GetComponent<SpriteRenderer>();
            Require(arrow != null, "Direction-glyph exclusion fixture requires the real source indicator.");
            Color arrowColor = arrow.color;
            bool arrowEnabled = arrow.enabled;
            Vector3 arrowPosition = arrow.transform.localPosition;
            Quaternion arrowRotation = arrow.transform.localRotation;
            Vector3 arrowScale = arrow.transform.localScale;
            string before = AppearanceFingerprint(player);
            string sourceBefore = AppearanceFingerprint(player, true);
            var preview = new RefitPlayerPreview();
            Texture2D initialPixels = null;
            Texture2D changedPixels = null;
            Texture2D disabledPixels = null;
            try
            {
                Require(preview.Capture(player), "The direction-glyph exclusion probe could not capture the player.");
                initialPixels = ReadPreviewPixels(preview.Texture);
                uint initial = PixelSignature(initialPixels);
                Require(before == AppearanceFingerprint(player), "Preview capture changed the live direction indicator.");
                arrow.enabled = true;
                arrow.color = new Color(.95f, .05f, .75f, 1f);
                arrow.transform.localPosition += new Vector3(50f, 20f, 0f);
                arrow.transform.localRotation = Quaternion.Euler(0f, 0f, 47f);
                arrow.transform.localScale *= 7f;
                Require(preview.Capture(player), "The modified-direction probe could not capture the player.");
                changedPixels = ReadPreviewPixels(preview.Texture);
                string sourceChanged = AppearanceFingerprint(player, true);
                bool changedMatches = initial == PixelSignature(changedPixels);
                Require(changedMatches, changedMatches
                    ? "Changing the direction indicator affected equipment preview pixels or framing."
                    : PreviewMismatch(initialPixels, changedPixels, null, "changed-indicator", sourceBefore, sourceChanged,
                        preview.CapturedRendererCount, preview.ReconstructedBodyRendererCount, preview.ExcludedDirectionIndicatorCount));
                arrow.enabled = false;
                Require(preview.Capture(player), "The disabled-direction probe could not capture the player.");
                disabledPixels = ReadPreviewPixels(preview.Texture);
                string sourceDisabled = AppearanceFingerprint(player, true);
                bool disabledMatches = initial == PixelSignature(disabledPixels);
                Require(disabledMatches, disabledMatches
                    ? "Disabling the direction indicator changed the equipment preview."
                    : PreviewMismatch(initialPixels, disabledPixels, changedPixels, "disabled-indicator", sourceBefore, sourceDisabled,
                        preview.CapturedRendererCount, preview.ReconstructedBodyRendererCount, preview.ExcludedDirectionIndicatorCount));
            }
            finally
            {
                arrow.color = arrowColor;
                arrow.enabled = arrowEnabled;
                arrow.transform.localPosition = arrowPosition;
                arrow.transform.localRotation = arrowRotation;
                arrow.transform.localScale = arrowScale;
                preview.Dispose();
                if (initialPixels != null) Object.DestroyImmediate(initialPixels);
                if (changedPixels != null) Object.DestroyImmediate(changedPixels);
                if (disabledPixels != null) Object.DestroyImmediate(disabledPixels);
            }
            Require(before == AppearanceFingerprint(player), "The exclusion probe did not restore the live direction indicator exactly.");
        }

        private static uint PixelSignature(RenderTexture texture)
        {
            Texture2D pixels = ReadPreviewPixels(texture);
            try { return PixelSignature(pixels); }
            finally { Object.DestroyImmediate(pixels); }
        }

        private static uint PixelSignature(Texture2D pixels)
        {
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

        private static Texture2D ReadPreviewPixels(RenderTexture texture)
        {
            RenderTexture previous = RenderTexture.active;
            Texture2D pixels = null;
            try
            {
                RenderTexture.active = texture;
                pixels = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                pixels.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                pixels.Apply();
                return pixels;
            }
            catch
            {
                if (pixels != null) Object.DestroyImmediate(pixels);
                throw;
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        private static string PreviewMismatch(Texture2D expected, Texture2D actual, Texture2D changed,
            string probe, string sourceBefore, string sourceAfter, int rendererCount, int reconstructedCount, int excludedCount)
        {
            Color32[] first = expected.GetPixels32(), second = actual.GetPixels32();
            int pixelsChanged = 0, maxError = 0, minX = actual.width, minY = actual.height, maxX = -1, maxY = -1;
            long totalError = 0;
            for (int i = 0; i < Mathf.Min(first.Length, second.Length); i++)
            {
                int red = Math.Abs(first[i].r - second[i].r), green = Math.Abs(first[i].g - second[i].g);
                int blue = Math.Abs(first[i].b - second[i].b), alpha = Math.Abs(first[i].a - second[i].a);
                int error = Math.Max(Math.Max(red, green), Math.Max(blue, alpha));
                if (error == 0) continue;
                pixelsChanged++; maxError = Math.Max(maxError, error); totalError += red + green + blue + alpha;
                int x = i % actual.width, y = i / actual.width;
                minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
            string logDirectory = Path.GetFullPath(Path.Combine("output", "validation", "checkpoint-refit"));
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], "-logFile", StringComparison.OrdinalIgnoreCase) && args[i + 1] != "-")
                { logDirectory = Path.GetDirectoryName(Path.GetFullPath(args[i + 1])); break; }
            string diagnosticDirectory = Path.Combine(logDirectory, "preview-diagnostics");
            Directory.CreateDirectory(diagnosticDirectory);
            File.WriteAllBytes(Path.Combine(diagnosticDirectory, probe + "-initial.png"), expected.EncodeToPNG());
            File.WriteAllBytes(Path.Combine(diagnosticDirectory, probe + "-actual.png"), actual.EncodeToPNG());
            if (changed != null) File.WriteAllBytes(Path.Combine(diagnosticDirectory, probe + "-changed-enabled.png"), changed.EncodeToPNG());
            string detail = "Direction-indicator exclusion pixel mismatch (" + probe + "): changedPixels=" + pixelsChanged
                + ", maxChannelError=" + maxError + ", totalChannelError=" + totalError
                + ", changedBounds=" + minX + "," + minY + ".." + maxX + "," + maxY
                + ", nonIndicatorSourceUnchanged=" + string.Equals(sourceBefore, sourceAfter, StringComparison.Ordinal)
                + ", captured=" + rendererCount + ", reconstructed=" + reconstructedCount + ", excluded=" + excludedCount
                + ". Diagnostics: " + diagnosticDirectory;
            File.WriteAllText(Path.Combine(diagnosticDirectory, probe + "-diagnostic.txt"), detail
                + "\n\nSource before (excluding indicator):\n" + sourceBefore + "\n\nSource after (excluding indicator):\n" + sourceAfter);
            Debug.LogWarning(detail);
            return detail;
        }

        internal static string AppearanceFingerprint(RobotMover player, bool excludeDirectionIndicator = false)
        {
            var value = new StringBuilder();
            foreach (SpriteRenderer renderer in player.GetComponentsInChildren<SpriteRenderer>(true))
            {
                bool indicator = false;
                if (excludeDirectionIndicator)
                    for (Transform current = renderer.transform; current != null && current != player.transform; current = current.parent)
                        if (current.name == "Direction Indicator") { indicator = true; break; }
                if (indicator) continue;
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
