using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalGame.RobotMap
{
    /// <summary>
    /// Draws soft short trail segments in one UI mesh underneath the animal stars.
    /// World-space anchors keep old footprints attached to the map during camera motion.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BioScanAnimalTrailGraphic : MaskableGraphic
    {
        private struct Footprint
        {
            public Vector3 Start;
            public Vector3 Position;
            public float Remaining;
            public float Lifetime;
        }

        private readonly List<Footprint> footprints = new List<Footprint>();
        private Camera worldCamera;
        private float widthPixels = 3.2f;
        // Each segment uses eight vertices; stay below Unity's UI mesh vertex limit.
        private const int MaximumFootprints = 2000;

        public void AddFootprint(Vector3 start, Vector3 position, float lifetime, float age)
        {
            if (age >= lifetime)
                return;
            if (footprints.Count >= MaximumFootprints)
                footprints.RemoveAt(0);
            footprints.Add(new Footprint
            {
                Start = start,
                Position = position,
                Remaining = lifetime - age,
                Lifetime = lifetime
            });
            SetVerticesDirty();
        }

        public void Tick(float deltaTime, Camera camera, Color tint, float width)
        {
            worldCamera = camera;
            color = tint;
            widthPixels = width;
            bool hadFootprints = footprints.Count > 0;
            int kept = 0;
            for (int index = 0; index < footprints.Count; index++)
            {
                Footprint footprint = footprints[index];
                footprint.Remaining -= deltaTime;
                if (footprint.Remaining > 0f)
                    footprints[kept++] = footprint;
            }
            if (kept < footprints.Count)
                footprints.RemoveRange(kept, footprints.Count - kept);
            if (hadFootprints)
                SetVerticesDirty();
        }

        public void ClearFootprints()
        {
            if (footprints.Count == 0)
                return;
            footprints.Clear();
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (worldCamera == null || !worldCamera.isActiveAndEnabled)
                return;
            Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera : null;
            float scaleFactor = canvas != null ? Mathf.Max(0.001f, canvas.scaleFactor) : 1f;
            float halfWidth = widthPixels * 0.5f / scaleFactor;
            float feather = 0.5f / scaleFactor;
            Rect viewport = worldCamera.pixelRect;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform, viewport.min, uiCamera, out Vector2 viewportMin);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform, viewport.max, uiCamera, out Vector2 viewportMax);
            foreach (Footprint footprint in footprints)
            {
                // Keep the dash centered, at 70% of its former 65% sample span.
                Vector3 startScreen = worldCamera.WorldToScreenPoint(
                    Vector3.Lerp(footprint.Start, footprint.Position, 0.2725f));
                Vector3 endScreen = worldCamera.WorldToScreenPoint(
                    Vector3.Lerp(footprint.Start, footprint.Position, 0.7275f));
                if (startScreen.z < worldCamera.nearClipPlane || endScreen.z < worldCamera.nearClipPlane
                    || startScreen.z > worldCamera.farClipPlane || endScreen.z > worldCamera.farClipPlane)
                    continue;
                Vector2 start = startScreen;
                Vector2 end = endScreen;
                if (!ClipToViewport(ref start, ref end, viewport))
                    continue;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rectTransform, start, uiCamera, out Vector2 localStart);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rectTransform, end, uiCamera, out Vector2 localEnd);
                Vector2 direction = localEnd - localStart;
                float length = direction.magnitude;
                if (length < 0.001f)
                    continue;
                direction /= length;
                Vector2 normal = new Vector2(-direction.y, direction.x);
                Vector2 cap = direction * Mathf.Min(feather, length * 0.25f);
                Vector2 innerNormal = normal * Mathf.Max(0f, halfWidth - feather);
                Vector2 outerNormal = normal * (halfWidth + feather);
                Color tint = color;
                // A softer, longer fade keeps the sparse trail understated.
                tint.a *= Mathf.Clamp01(footprint.Remaining / Mathf.Min(0.6f, footprint.Lifetime));
                Color edge = tint;
                edge.a = 0f;
                int first = mesh.currentVertCount;
                AddVertex(mesh, localStart + cap - innerNormal, tint, viewportMin, viewportMax);
                AddVertex(mesh, localStart + cap + innerNormal, tint, viewportMin, viewportMax);
                AddVertex(mesh, localEnd - cap + innerNormal, tint, viewportMin, viewportMax);
                AddVertex(mesh, localEnd - cap - innerNormal, tint, viewportMin, viewportMax);
                AddVertex(mesh, localStart - cap - outerNormal, edge, viewportMin, viewportMax);
                AddVertex(mesh, localStart - cap + outerNormal, edge, viewportMin, viewportMax);
                AddVertex(mesh, localEnd + cap + outerNormal, edge, viewportMin, viewportMax);
                AddVertex(mesh, localEnd + cap - outerNormal, edge, viewportMin, viewportMax);
                mesh.AddTriangle(first, first + 1, first + 2);
                mesh.AddTriangle(first, first + 2, first + 3);
                for (int side = 0; side < 4; side++)
                {
                    int inner = first + side;
                    int next = first + (side + 1) % 4;
                    mesh.AddTriangle(inner, inner + 4, next + 4);
                    mesh.AddTriangle(inner, next + 4, next);
                }
            }
        }

        private static void AddVertex(VertexHelper mesh, Vector2 position, Color tint,
            Vector2 viewportMin, Vector2 viewportMax)
        {
            mesh.AddVert(Vector2.Min(Vector2.Max(position, viewportMin), viewportMax), tint, Vector2.zero);
        }

        private static bool ClipToViewport(ref Vector2 start, ref Vector2 end, Rect viewport)
        {
            Vector2 delta = end - start;
            float enter = 0f;
            float exit = 1f;
            if (!ClipEdge(-delta.x, start.x - viewport.xMin, ref enter, ref exit)
                || !ClipEdge(delta.x, viewport.xMax - start.x, ref enter, ref exit)
                || !ClipEdge(-delta.y, start.y - viewport.yMin, ref enter, ref exit)
                || !ClipEdge(delta.y, viewport.yMax - start.y, ref enter, ref exit))
                return false;
            end = start + delta * exit;
            start += delta * enter;
            return true;
        }

        private static bool ClipEdge(float direction, float distance, ref float enter, ref float exit)
        {
            if (Mathf.Abs(direction) < 0.000001f)
                return distance >= 0f;
            float fraction = distance / direction;
            if (direction < 0f)
                enter = Mathf.Max(enter, fraction);
            else
                exit = Mathf.Min(exit, fraction);
            return enter <= exit;
        }
    }
}
