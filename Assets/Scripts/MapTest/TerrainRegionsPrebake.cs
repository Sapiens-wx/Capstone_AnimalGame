using System;
using System.IO;
using UnityEngine;

namespace AnimalGame.MapTest
{
    /// <summary>Terrain coverage in map XY metres, measured from the lower-left corner.</summary>
    public sealed class TerrainRegionsPrebake
    {
        private const uint Magic = 0x4E475254; // TRGN
        private const int Version = 2;

        // Rings are implicitly closed. Holes belong to the outer ring of their polygon.
        public sealed class Polygon
        {
            public Vector2[] Outer { get; }
            public Vector2[][] Holes { get; }

            public Polygon(Vector2[] outer, Vector2[][] holes)
            {
                Outer = outer;
                Holes = holes;
            }
        }

        public sealed class Region
        {
            public TerrainKind Terrain { get; }
            public Polygon[] Polygons { get; }

            public Region(TerrainKind terrain, Polygon[] polygons)
            {
                Terrain = terrain;
                Polygons = polygons;
            }
        }

        public Region[] Regions { get; }
        public bool IncludesWaterMask { get; }

        public TerrainRegionsPrebake(Region[] regions) : this(regions, true) { }

        private TerrainRegionsPrebake(Region[] regions, bool includesWaterMask)
        {
            Regions = regions;
            IncludesWaterMask = includesWaterMask;
        }

        public static TerrainRegionsPrebake Read(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes, false);
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt32() != Magic)
                throw new InvalidDataException("Unsupported terrain regions prebake format.");
            int version = reader.ReadInt32();
            if (version != 1 && version != Version)
                throw new InvalidDataException("Unsupported terrain regions prebake format.");

            var regions = new Region[ReadCount(reader, 5)];
            for (int i = 0; i < regions.Length; i++)
            {
                var terrain = (TerrainKind)reader.ReadByte();
                if (!Enum.IsDefined(typeof(TerrainKind), terrain))
                    throw new InvalidDataException("Invalid terrain ID in terrain regions prebake.");
                var polygons = new Polygon[ReadCount(reader, 32)];
                for (int j = 0; j < polygons.Length; j++)
                {
                    Vector2[] outer = ReadRing(reader);
                    var holes = new Vector2[ReadCount(reader, 28)][];
                    for (int k = 0; k < holes.Length; k++)
                        holes[k] = ReadRing(reader);
                    polygons[j] = new Polygon(outer, holes);
                }
                regions[i] = new Region(terrain, polygons);
            }
            if (stream.Position != stream.Length)
                throw new InvalidDataException("Unexpected trailing terrain region data.");
            return new TerrainRegionsPrebake(regions, version == Version);
        }

        private static Vector2[] ReadRing(BinaryReader reader)
        {
            int count = ReadCount(reader, 8);
            if (count < 3) throw new InvalidDataException("A terrain region ring needs at least three vertices.");
            var ring = new Vector2[count];
            for (int i = 0; i < count; i++)
                ring[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            return ring;
        }

        private static int ReadCount(BinaryReader reader, int minimumBytesPerItem)
        {
            int count = reader.ReadInt32();
            Stream stream = reader.BaseStream;
            if (count < 0 || count > (stream.Length - stream.Position) / minimumBytesPerItem)
                throw new InvalidDataException("Invalid item count in terrain regions prebake.");
            return count;
        }

#if UNITY_EDITOR
        public void Write(Stream stream)
        {
            using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            writer.Write(Magic);
            writer.Write(IncludesWaterMask ? Version : 1);
            writer.Write(Regions.Length);
            foreach (Region region in Regions)
            {
                writer.Write((byte)region.Terrain);
                writer.Write(region.Polygons.Length);
                foreach (Polygon polygon in region.Polygons)
                {
                    WriteRing(writer, polygon.Outer);
                    writer.Write(polygon.Holes.Length);
                    foreach (Vector2[] hole in polygon.Holes)
                        WriteRing(writer, hole);
                }
            }
        }

        private static void WriteRing(BinaryWriter writer, Vector2[] ring)
        {
            writer.Write(ring.Length);
            foreach (Vector2 point in ring)
            {
                writer.Write(point.x);
                writer.Write(point.y);
            }
        }
#endif
    }
}
