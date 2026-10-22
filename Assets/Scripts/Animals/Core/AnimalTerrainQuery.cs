using AnimalGame.MapTest;
using AnimalGame.World;
using UnityEngine;

namespace AnimalGame.Animals
{
    public readonly struct AnimalTerrainSample
    {
        public readonly float Height, SlopeDegrees;
        public readonly Vector2 Gradient;
        public readonly TerrainKind Terrain;
        public AnimalTerrainSample(float height, Vector2 gradient, TerrainKind terrain)
        { Height = height; Gradient = gradient; Terrain = terrain; SlopeDegrees = Mathf.Atan(gradient.magnitude) * Mathf.Rad2Deg; }
    }

    public static class AnimalTerrainQuery
    {
        public static bool TrySample(MapTestSceneController map, Vector2 position, out AnimalTerrainSample sample)
        {
            sample = default;
            if (map == null || !map.TrySampleMapPosition(position, out float height)) return false;
            Vector2 spacing = map.SampleSpacing(position);
            if (!Derivative(map, position, Vector2.right * Mathf.Max(.05f, spacing.x), height, out float x)
                || !Derivative(map, position, Vector2.up * Mathf.Max(.05f, spacing.y), height, out float y)) return false;
            WorldTerrainQuery.TrySampleMapPosition(map, position, out TerrainKind terrain);
            sample = new AnimalTerrainSample(height, new Vector2(x, y), terrain);
            return true;
        }

        private static bool Derivative(MapTestSceneController map, Vector2 p, Vector2 step, float height, out float slope)
        {
            bool left = map.TrySampleMapPosition(p - step, out float a);
            bool right = map.TrySampleMapPosition(p + step, out float b);
            slope = left && right ? (b - a) / (2f * step.magnitude)
                : right ? (b - height) / step.magnitude : left ? (height - a) / step.magnitude : 0f;
            return left || right;
        }
    }
}
