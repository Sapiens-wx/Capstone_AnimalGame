using System.Collections.Generic;
using AnimalGame.Animals;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AnimalGame.AnimalDebug
{
    public static class AnimalDebugSelection
    {
        public static bool IsSelectable(AnimalAgent animal, Scene scene) => animal != null
            && animal.gameObject.scene == scene && animal.isActiveAndEnabled && animal.IsInitialized
            && !animal.Runtime.Removed && !animal.Health.IsDead && animal.CurrentState != AnimalState.Despawned
            && (animal.IsPresent || animal.Behaviour is AmericanMarmotBehaviour);

        public static string DisplayName(AnimalAgent animal)
        {
            if (animal == null) return "Select an animal";
            AnimalPhotoSubject photo = animal.GetComponent<AnimalPhotoSubject>();
            string species = photo != null ? photo.EnglishName : animal.Config.SpeciesName;
            return species + " / " + animal.name + " #" + animal.GetInstanceID();
        }

        public static bool TryBounds(AnimalAgent animal, out Bounds bounds)
        {
            bounds = default;
            if (animal == null) return false;
            var photo = animal.GetComponent<AnimalPhotoSubject>();
            if (!animal.IsHidden && photo != null && photo.TryGetWorldBounds(out bounds) && bounds.size.x > .00001f) return true;
            if (animal.Map == null || animal.Motor == null) return false;
            float width = animal.Map.MapMetersToWorldDistance(Vector2.right, animal.Config.BodyRadiusMeters * 2f);
            float height = animal.Map.MapMetersToWorldDistance(Vector2.up, animal.Config.BodyRadiusMeters * 2f);
            bounds = new Bounds(animal.transform.position, new Vector3(width, height, .01f));
            return bounds.size.x > .00001f;
        }

        public static bool ContainsSample(Bounds body, Vector2 point)
        {
            float radius = body.size.x * 2f;
            return (point - (Vector2)body.center).sqrMagnitude <= radius * radius;
        }

        public static AnimalAgent Pick(Camera camera, Vector2 screenPosition, IEnumerable<AnimalAgent> animals)
        {
            if (camera == null || !camera.pixelRect.Contains(screenPosition)) return null;
            Ray ray = camera.ScreenPointToRay(screenPosition);
            AnimalAgent selected = null;
            float closest = float.PositiveInfinity;
            foreach (AnimalAgent animal in animals)
            {
                if (!TryBounds(animal, out Bounds bounds)) continue;
                var plane = new Plane(Vector3.forward, bounds.center);
                if (!plane.Raycast(ray, out float distance)) continue;
                Vector2 point = ray.GetPoint(distance);
                if (!ContainsSample(bounds, point)) continue;
                float square = (point - (Vector2)bounds.center).sqrMagnitude;
                if (square < closest || Mathf.Approximately(square, closest)
                    && (selected == null || animal.GetInstanceID() < selected.GetInstanceID()))
                { closest = square; selected = animal; }
            }
            return selected;
        }

        // Positions are bottom-left screen pixels. Insetting keeps the complete arrow visible.
        public static Vector2 EdgePoint(Rect viewport, Vector2 target, float inset)
        {
            Vector2 half = viewport.size * .5f - Vector2.one * inset;
            half = Vector2.Max(half, Vector2.one);
            Vector2 direction = target - viewport.center;
            if (direction.sqrMagnitude < .00001f) direction = Vector2.up;
            float tx = Mathf.Abs(direction.x) > .00001f ? half.x / Mathf.Abs(direction.x) : float.PositiveInfinity;
            float ty = Mathf.Abs(direction.y) > .00001f ? half.y / Mathf.Abs(direction.y) : float.PositiveInfinity;
            return viewport.center + direction * Mathf.Min(tx, ty);
        }
    }
}
