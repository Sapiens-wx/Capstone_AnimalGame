using UnityEngine;

namespace AnimalGame.MapTest
{
    /// <summary>Meter-based queries independent of tile storage and residency policy.</summary>
    public interface IMapHeightSource
    {
        Vector2 MapSizeMeters { get; }
        bool TrySample(Vector2 position, out float height, int channel = 0);
    }
}
