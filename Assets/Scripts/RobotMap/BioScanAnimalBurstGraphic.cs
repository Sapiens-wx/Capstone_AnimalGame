using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalGame.RobotMap
{
    /// <summary>
    /// One-shot scan feedback drawn above the world, independent of player sight.
    /// Bursts stay at the world-space hit position and share the marker's pause clock.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BioScanAnimalBurstGraphic : MaskableGraphic
    {
        private struct Burst
        {
            public int EntityId;
            public Vector3 Position;
            public float Age;
            public float Lifetime;
            public float RadiusPixels;
            public int RayCount;
            public float Rotation;
            public bool JustStarted;
        }

        private const int MaximumBursts = 96;
        private const int RingSides = 32;
        private readonly List<Burst> bursts = new List<Burst>();
        private Camera worldCamera;

        public void Play(int entityId, Vector3 position, float lifetime, float radiusPixels, int rayCount)
        {
            var burst = new Burst
            {
                EntityId = entityId,
                Position = position,
                Lifetime = Mathf.Max(0.1f, lifetime),
                RadiusPixels = Mathf.Max(4f, radiusPixels),
                RayCount = Mathf.Clamp(rayCount, 4, 16),
                Rotation = (entityId & 255) / 256f * Mathf.PI * 2f,
                JustStarted = true
            };
            for (int index = 0; index < bursts.Count; index++)
            {
                if (bursts[index].EntityId != entityId)
                    continue;
                // A quick rescan restarts this animal's feedback without stacking
                // multiple flashes on the same target.
                bursts[index] = burst;
                SetVerticesDirty();
                return;
            }
            if (bursts.Count >= MaximumBursts)
                bursts.RemoveAt(0);
            bursts.Add(burst);
            SetVerticesDirty();
        }

        public void Tick(float deltaTime, Camera camera, Color tint)
        {
            worldCamera = camera;
            color = tint;
            bool hadBursts = bursts.Count > 0;
            int kept = 0;
            for (int index = 0; index < bursts.Count; index++)
            {
                Burst burst = bursts[index];
                if (!burst.JustStarted)
                    burst.Age += deltaTime;
                burst.JustStarted = false;
                if (burst.Age < burst.Lifetime)
                    bursts[kept++] = burst;
            }
            if (kept < bursts.Count)
                bursts.RemoveRange(kept, bursts.Count - kept);
            if (hadBursts)
                SetVerticesDirty();
        }

        public void ClearBursts()
        {
            if (bursts.Count == 0)
                return;
            bursts.Clear();
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (worldCamera == null || !worldCamera.isActiveAndEnabled)
                return;
            Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera : null;
            float pixel = 1f / (canvas != null ? Mathf.Max(0.001f, canvas.scaleFactor) : 1f);
            Rect viewport = worldCamera.pixelRect;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform, viewport.min, uiCamera, out Vector2 viewportMin);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform, viewport.max, uiCamera, out Vector2 viewportMax);
            foreach (Burst burst in bursts)
            {
                Vector3 screen = worldCamera.WorldToScreenPoint(burst.Position);
                if (screen.z < worldCamera.nearClipPlane || screen.z > worldCamera.farClipPlane
                    || !viewport.Contains(screen))
                    continue;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rectTransform, screen, uiCamera, out Vector2 center);
                float progress = Mathf.Clamp01(burst.Age / burst.Lifetime);
                float travel = 1f - Mathf.Pow(1f - progress, 3f);
                float radius = Mathf.Lerp(3f, burst.RadiusPixels, travel) * pixel;
                Color sparkColor = color;
                sparkColor.a *= 1f - Mathf.SmoothStep(0f, 1f, progress);
                float length = Mathf.Lerp(11.5f, 2.5f, progress) * pixel;
                float halfWidth = Mathf.Lerp(1.6f, 0.5f, progress) * pixel;
                for (int ray = 0; ray < burst.RayCount; ray++)
                {
                    float angle = burst.Rotation + ray * Mathf.PI * 2f / burst.RayCount;
                    Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    Vector2 normal = new Vector2(-direction.y, direction.x) * halfWidth;
                    Vector2 middle = center + direction * radius * (ray % 2 == 0 ? 1f : 0.82f);
                    Vector2 halfLength = direction * length * 0.5f;
                    Color edge = sparkColor;
                    edge.a *= 0.25f;
                    int first = mesh.currentVertCount;
                    AddVertex(mesh, middle - halfLength, edge, viewportMin, viewportMax);
                    AddVertex(mesh, middle + normal, sparkColor, viewportMin, viewportMax);
                    AddVertex(mesh, middle + halfLength, edge, viewportMin, viewportMax);
                    AddVertex(mesh, middle - normal, sparkColor, viewportMin, viewportMax);
                    mesh.AddTriangle(first, first + 1, first + 2);
                    mesh.AddTriangle(first, first + 2, first + 3);
                }

                Color ringColor = color;
                ringColor.a *= 0.75f * Mathf.Pow(1f - progress, 1.5f);
                DrawRing(mesh, center, Mathf.Lerp(2.5f, burst.RadiusPixels * 0.8f, travel) * pixel,
                    pixel * 1.5f, ringColor, viewportMin, viewportMax);
            }
        }

        private static void DrawRing(VertexHelper mesh, Vector2 center, float radius,
            float pixel, Color tint, Vector2 viewportMin, Vector2 viewportMax)
        {
            Color edge = tint;
            edge.a = 0f;
            int first = mesh.currentVertCount;
            for (int side = 0; side < RingSides; side++)
            {
                float angle = side * Mathf.PI * 2f / RingSides;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                AddVertex(mesh, center + direction * (radius - pixel), edge, viewportMin, viewportMax);
                AddVertex(mesh, center + direction * (radius - pixel * 0.3f), tint, viewportMin, viewportMax);
                AddVertex(mesh, center + direction * (radius + pixel * 0.3f), tint, viewportMin, viewportMax);
                AddVertex(mesh, center + direction * (radius + pixel), edge, viewportMin, viewportMax);
            }
            for (int side = 0; side < RingSides; side++)
            {
                int current = first + side * 4;
                int next = first + (side + 1) % RingSides * 4;
                for (int strip = 0; strip < 3; strip++)
                {
                    mesh.AddTriangle(current + strip, next + strip, next + strip + 1);
                    mesh.AddTriangle(current + strip, next + strip + 1, current + strip + 1);
                }
            }
        }

        private static void AddVertex(VertexHelper mesh, Vector2 position, Color tint,
            Vector2 viewportMin, Vector2 viewportMax)
        {
            mesh.AddVert(Vector2.Min(Vector2.Max(position, viewportMin), viewportMax), tint, Vector2.zero);
        }
    }
}
