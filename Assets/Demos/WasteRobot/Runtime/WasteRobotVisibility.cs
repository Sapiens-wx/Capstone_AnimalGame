using System.Collections.Generic;
using AnimalGame.Rendering;
using UnityEngine;

namespace AnimalGame.Demos.WasteRobot
{
    /// <summary>Clips the machine's visuals to the existing player view without changing its simulation.</summary>
    [DisallowMultipleComponent]
    public sealed class WasteRobotVisibility : MonoBehaviour
    {
        private readonly Dictionary<SpriteRenderer, Material[]> originalMaterials =
            new Dictionary<SpriteRenderer, Material[]>();
        private SpriteRenderer[] registeredRenderers;

        private void OnEnable() => RefreshRenderers();

        // Includes the inactive interior so the first frame after entering is
        // already clipped. Call again after explicitly adding new visual parts.
        public void RefreshRenderers()
        {
            ReleaseRenderers();
            if (!isActiveAndEnabled) return;

            var renderers = new List<SpriteRenderer>();
            foreach (SpriteRenderer renderer in GetComponentsInChildren<SpriteRenderer>(true))
            {
                // Transition snapshots own their own registration and cleanup.
                if (renderer.GetComponentInParent<WasteRobotVisibility>(true) != this) continue;
                originalMaterials.Add(renderer, renderer.sharedMaterials);
                renderers.Add(renderer);
            }
            registeredRenderers = renderers.ToArray();
            PlayerUiOrganicVisibility.RegisterRenderers(registeredRenderers);
        }

        private void ReleaseRenderers()
        {
            PlayerUiOrganicVisibility.UnregisterRenderers(registeredRenderers);
            registeredRenderers = null;
            foreach (KeyValuePair<SpriteRenderer, Material[]> pair in originalMaterials)
                if (pair.Key != null) pair.Key.sharedMaterials = pair.Value;
            originalMaterials.Clear();
        }

        private void OnDisable() => ReleaseRenderers();
        private void OnDestroy() => ReleaseRenderers();
    }
}
