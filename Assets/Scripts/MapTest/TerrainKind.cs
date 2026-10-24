namespace AnimalGame.MapTest
{
    // Shared by every map. These values are persisted in surface masks;
    // keep existing values stable when adding new terrain kinds.
    public enum TerrainKind : byte
    {
        Unknown = 0,
        GridTest = 1,
        GrassTest = 2,
        Grass = 3,
        Grassland = 4,
        Gravel = 5,
        Rock = 6,
        Water = 7,
        Web = 8
    }
}
