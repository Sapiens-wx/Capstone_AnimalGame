using System;
using System.IO;
using AnimalGame.Animals;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using static AnimalGame.Animals.AnimalSpawnRegionsAsset;

namespace AnimalGame.Editor
{
    [ScriptedImporter(1, Extension)]
    public sealed class AnimalSpawnRegionsImporter : ScriptedImporter
    {
        internal const string Extension = "spawnregions";
        private const uint Magic = 0x52505341; // ASPR in little-endian order.
        private const int FormatVersion = 1;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            using var stream = File.OpenRead(ctx.assetPath);
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != FormatVersion)
                throw new InvalidDataException("Unsupported animal spawn regions format.");

            var entries = new SpeciesRegions[ReadCount(reader, 8)];
            for (int i = 0; i < entries.Length; i++)
            {
                var species = (AnimalSpecies)reader.ReadInt32();
                var polygons = new Polygon[ReadCount(reader, 8)];
                for (int j = 0; j < polygons.Length; j++)
                {
                    var vertices = ReadRing(reader);
                    var holes = new Ring[ReadCount(reader, 4)];
                    for (int k = 0; k < holes.Length; k++)
                        holes[k] = new Ring { vertices = ReadRing(reader) };
                    polygons[j] = new Polygon { vertices = vertices, holes = holes };
                }
                entries[i] = new SpeciesRegions { species = species, polygons = polygons };
            }

            if (stream.Position != stream.Length)
                throw new InvalidDataException("Unexpected trailing data in animal spawn regions.");

            var asset = ScriptableObject.CreateInstance<AnimalSpawnRegionsAsset>();
            asset.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            asset.EditableRegions = entries;
            ctx.AddObjectToAsset("Regions", asset);
            ctx.SetMainObject(asset);
        }

        public static AnimalSpawnRegionsAsset Save(string path, AnimalSpawnRegionsAsset regions)
        {
            if (Path.GetExtension(path) != "." + Extension)
                throw new IOException($"Expected a .{Extension} asset path.");

            string temp = Path.Combine(Path.GetDirectoryName(path), $".{Guid.NewGuid():N}.tmp");
            try
            {
                using (var writer = new BinaryWriter(File.Create(temp)))
                {
                    writer.Write(Magic);
                    writer.Write(FormatVersion);
                    writer.Write(regions.Regions.Count);
                    foreach (var entry in regions.Regions)
                    {
                        writer.Write((int)entry.species);
                        writer.Write(entry.polygons.Length);
                        foreach (var polygon in entry.polygons)
                        {
                            WriteRing(writer, polygon.vertices);
                            writer.Write(polygon.holes.Length);
                            foreach (var hole in polygon.holes)
                                WriteRing(writer, hole.vertices);
                        }
                    }
                }

                if (File.Exists(path))
                    File.Replace(temp, path, null);
                else
                    File.Move(temp, path);
            }
            finally
            {
                File.Delete(temp);
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<AnimalSpawnRegionsAsset>(path)
                ?? throw new IOException($"Could not import animal spawn regions at {path}.");
        }

        private static int ReadCount(BinaryReader reader, int minimumBytesPerItem)
        {
            int count = reader.ReadInt32();
            var stream = reader.BaseStream;
            if (count < 0 || count > (stream.Length - stream.Position) / minimumBytesPerItem)
                throw new InvalidDataException("Invalid array length in animal spawn regions.");
            return count;
        }

        private static Vector2[] ReadRing(BinaryReader reader)
        {
            var vertices = new Vector2[ReadCount(reader, 8)];
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            return vertices;
        }

        private static void WriteRing(BinaryWriter writer, Vector2[] vertices)
        {
            writer.Write(vertices.Length);
            foreach (Vector2 vertex in vertices)
            {
                writer.Write(vertex.x);
                writer.Write(vertex.y);
            }
        }
    }
}
