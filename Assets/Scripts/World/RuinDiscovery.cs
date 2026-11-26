using System.Collections.Generic;
using AnimalGame.MapTest;
using AnimalGame.Save;
using UnityEngine;
using UnityEngine.UIElements;
using GameHud = AnimalGame.MainUI.MainUI;

namespace AnimalGame.World
{
    [DefaultExecutionOrder(320)]
    [DisallowMultipleComponent]
    public sealed class RuinDiscovery : MonoBehaviour
    {
        private const float FadeInSeconds = .24f;
        private const float HoldSeconds = 1.45f;
        private const float FadeOutSeconds = .42f;
        private const float ExitViewScale = 1.12f;

        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [Tooltip("The visible silhouette in local XY, excluding transparent sprite margins. The last point joins the first.")]
        [SerializeField] private Vector2[] visibleOutline;

        private Label banner;
        private float elapsed;
        private bool visitActive;

        public string Id => id;
        internal string DisplayName => displayName;
        internal IReadOnlyList<Vector2> VisibleOutline => visibleOutline;

        private void LateUpdate()
        {
            GameHud hud = GameHud.Active;
            if (hud == null || hud.gameObject.scene != gameObject.scene || !hud.CanPresentRuinName)
            {
                if (banner != null) banner.style.display = DisplayStyle.None;
                return;
            }

            UpdateVisit(hud);
            if (banner != null) UpdateBanner(hud);
        }

        private void UpdateVisit(GameHud hud)
        {
            var bootstrap = HeightMapPlayerSceneBootstrap.inst;
            if (bootstrap == null || bootstrap.gameObject.scene != gameObject.scene || bootstrap.mapCamera == null
                || !hud.TryGetRingScreenGeometry(out Vector2 center, out float radius)) return;

            // Rearm beyond the entry edge so small movements at the boundary cannot replay the title.
            if (!IntersectsView(bootstrap.mapCamera, center, visitActive ? radius * ExitViewScale : radius))
            {
                visitActive = false;
                return;
            }
            if (visitActive) return;

            SaveSystem.DiscoverRuin(id);
            visitActive = true;
            elapsed = 0f;
            banner ??= hud.CreateRuinName(displayName);
        }

        private void UpdateBanner(GameHud hud)
        {
            elapsed += Time.deltaTime;
            float fadeOutStart = FadeInSeconds + HoldSeconds;
            if (elapsed >= fadeOutStart + FadeOutSeconds)
            {
                RemoveBanner();
                return;
            }

            hud.PlaceRuinName(banner);
            float fade = Mathf.Min(elapsed / FadeInSeconds, (fadeOutStart + FadeOutSeconds - elapsed) / FadeOutSeconds);
            banner.style.opacity = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(fade));
        }

        internal bool IntersectsView(Camera camera, Vector2 center, float radius)
        {
            Vector3 last = camera.WorldToScreenPoint(transform.TransformPoint(visibleOutline[^1]));
            // The registered art and its local outline lie on the same XY plane.
            if (last.z < camera.nearClipPlane || last.z > camera.farClipPlane) return false;
            Vector2 from = last;
            float radiusSquared = radius * radius;
            bool containsCenter = false;
            foreach (Vector2 local in visibleOutline)
            {
                Vector2 to = camera.WorldToScreenPoint(transform.TransformPoint(local));
                Vector2 edge = to - from;
                float along = Mathf.Clamp01(Vector2.Dot(center - from, edge) / edge.sqrMagnitude);
                if ((from + edge * along - center).sqrMagnitude < radiusSquared) return true;
                if ((from.y > center.y) != (to.y > center.y) && center.x < from.x + (center.y - from.y) * edge.x / edge.y)
                    containsCenter = !containsCenter;
                from = to;
            }
            return containsCenter;
        }

        private void OnDisable() => RemoveBanner();

        private void RemoveBanner()
        {
            banner?.RemoveFromHierarchy();
            banner = null;
        }
    }
}
