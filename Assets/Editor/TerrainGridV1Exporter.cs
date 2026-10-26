using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEngine;

namespace AnimalGame.Editor
{
    /// <summary>Exports the connected authoring grid without changing its height range or sampling scale.</summary>
    public static class TerrainGridV1Exporter
    {
        private const int SourceResolution = 2049;
        private const int BakeResolution = 2048;
        private const float TileSize = 250f;
        private const float HeightRange = 70f;
        private const string TemplatePath = "Assets/Maps/Terrain_Test_03_MapData.asset";
        private const string ExportFolder = "Assets/Maps/TerrainGridV1/data";
        private const string LevelFolder = "Assets/Maps/TerrainGridV1";

        [MenuItem("Animal Game/Terrain Grid V1/Export V1 Height Maps")]
        public static void Export()
        {
            // Validate before writing anything; never repair or normalize authored heights during export.
            string sourceReport = TerrainGridV1Builder.ValidateGrid(checkSeams: false);
            Terrain[] terrains = TerrainGridV1Builder.GetGridTerrains();
            Require(terrains.Length == 4, "The V1 grid must contain exactly four Terrain tiles.");
            Require(AssetDatabase.LoadAssetAtPath<HeightMapLevelAsset>(TemplatePath) != null,
                "Missing V1 presentation template: " + TemplatePath);
            Directory.CreateDirectory(ExportFolder);
            AssetDatabase.Refresh();

            var manifest = new WorldManifest();
            try
            {
                for (int index = 0; index < terrains.Length; index++)
                {
                    int tileX = index % 2;
                    int tileZ = index / 2;
                    string name = "Tile_" + tileX + "_" + tileZ;
                    EditorUtility.DisplayProgressBar("Export V1 Height Maps", name, index / 4f);
                    float[,] heights = terrains[index].terrainData.GetHeights(
                        0, 0, SourceResolution, SourceResolution);
                    byte[] raw = QuantizeRaw(heights, out ushort minimum, out ushort maximum);
                    heights = null;
                    string rawPath = ExportFolder + "/" + name + ".raw";
                    string pngPath = ExportFolder + "/" + name + "_height_R16.png";
                    bool changed = !File.Exists(rawPath) || !FileBytesEqual(rawPath, raw);
                    if (changed)
                        File.WriteAllBytes(rawPath, raw);
                    if (changed || !File.Exists(pngPath))
                        WriteGray16Png(pngPath, raw);
                    ValidatePngHeader(pngPath);
                    Texture2D source = ImportAndVerify(pngPath, raw);
                    HeightMapLevelAsset level = ConfigureLevel(name, source);
                    // Matches checks settings, not source pixel contents. Always rebake so a
                    // partially failed previous export cannot reuse heights from an older RAW.
                    HeightMapPrebakeEditor.Bake(level, preserveAssetGuid: true);
                    Require(level.PrebakedHeightField != null && level.PrebakedHeightField.Matches(level),
                        name + " has no matching V1 prebake.");
                    VerifyBakedChannels(level.PrebakedHeightField);
                    manifest.tiles[index] = new TileManifest
                    {
                        name = name,
                        gridX = tileX,
                        gridZ = tileZ,
                        tileOriginMeters = new Vector2(tileX * TileSize, tileZ * TileSize),
                        sourceWorldOrigin = terrains[index].transform.position,
                        rawAssetPath = rawPath,
                        heightMapAssetPath = pngPath,
                        mapDataAssetPath = AssetDatabase.GetAssetPath(level),
                        prebakeAssetPath = AssetDatabase.GetAssetPath(level.PrebakedHeightField),
                        rawSha256 = Sha256(raw),
                        actualMinimumMeters = minimum / 65535f * HeightRange,
                        actualMaximumMeters = maximum / 65535f * HeightRange
                    };
                    // Only edge arrays survive to the next tile; no complete source arrays are retained.
                    raw = null;
                }
                manifest.validation = "PASS: 4 source tiles, every imported R16 sample equals RAW, "
                    + "V1 channels unsmoothed; shared edges assumed continuous.";
                File.WriteAllText(ExportFolder + "/world-manifest.json", JsonUtility.ToJson(manifest, true),
                    new UTF8Encoding(false));
                string report = sourceReport + "\n\n" + manifest.validation + "\n"
                    + "Source: 2049 x 2049, unsigned 16-bit little-endian RAW; PNG: grayscale 16-bit.\n"
                    + "RAW row 0 is Terrain Z=0. PNG top row is Terrain Z=250; Unity imports it back to Y=Z.\n"
                    + "Each Map Data: 250 x 250 m, 0..70 m, bake 2048, preview 2048, PPU 30, contours 7 m.\n"
                    + "Normalization, smoothing and border masks are disabled. Existing per-tile water/surface authoring is preserved.\n"
                    + "TiledLevelAsset references these tiles for resident runtime sampling and rendering; no streaming.\n";
                File.WriteAllText(ExportFolder + "/validation-report.txt", report, new UTF8Encoding(false));
                TiledLevelAssetEditorUtility.CreateGrid();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log(report);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem("Animal Game/Terrain Grid V1/Select Map Data/Tile_0_0")]
        private static void SelectTile00() => SelectMapData(0, 0);
        [MenuItem("Animal Game/Terrain Grid V1/Select Map Data/Tile_1_0")]
        private static void SelectTile10() => SelectMapData(1, 0);
        [MenuItem("Animal Game/Terrain Grid V1/Select Map Data/Tile_0_1")]
        private static void SelectTile01() => SelectMapData(0, 1);
        [MenuItem("Animal Game/Terrain Grid V1/Select Map Data/Tile_1_1")]
        private static void SelectTile11() => SelectMapData(1, 1);

        private static void SelectMapData(int x, int z)
        {
            string path = LevelFolder + "/Tile_" + x + "_" + z + "_MapData.asset";
            HeightMapLevelAsset level = AssetDatabase.LoadAssetAtPath<HeightMapLevelAsset>(path);
            Require(level != null, "Run Export V1 Height Maps before selecting " + path);
            Selection.activeObject = level;
            EditorGUIUtility.PingObject(level);
        }

        internal static HeightMapLevelAsset ConfigureLevel(string name, Texture2D heightMap,
            string outputFolder = LevelFolder, float mapSizeMeters = TileSize, int bakeResolution = BakeResolution)
        {
            string path = outputFolder + "/" + name + "_MapData.asset";
            HeightMapLevelAsset level = AssetDatabase.LoadAssetAtPath<HeightMapLevelAsset>(path);
            bool isNew = level == null;
            if (isNew)
            {
                Require(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) == null,
                    "Refusing to overwrite an unrelated asset at " + path);
                Require(AssetDatabase.CopyAsset(TemplatePath, path), "Could not create " + path);
                level = AssetDatabase.LoadAssetAtPath<HeightMapLevelAsset>(path);
                level.name = name + "_MapData";
            }
            var serialized = new SerializedObject(level);
            SetObject(serialized, "heightMap", heightMap);
            SetFloat(serialized, "mapWidthMeters", mapSizeMeters);
            SetFloat(serialized, "mapHeightMeters", mapSizeMeters);
            SetFloat(serialized, "minimumHeightMeters", 0f);
            SetFloat(serialized, "maximumHeightMeters", HeightRange);
            SetInt(serialized, "bakedHeightResolution", bakeResolution);
            SetBool(serialized, "normalizeSourceRange", false);
            SetFloat(serialized, "surfaceSmoothingSigmaMeters", 0f);
            SetFloat(serialized, "detailSmoothingSigmaMeters", 0f);
            if (isNew)
            {
                SetFloat(serialized, "contourIntervalMeters", 7f);
                // Copy the V1 presentation, never its physical/painted caches or authored masks.
                SetObject(serialized, "prebakedHeightField", null);
                SetObject(serialized, "staticWaterEditorTexture", null);
                SetObject(serialized, "bakedStaticWaterMask", null);
                SetObject(serialized, "bakedSurfaceVisual", null);
                SetObject(serialized, "surfacePatternTexture", null);
                Property(serialized, "staticWaterDepthMap").arraySize = 0;
                Property(serialized, "surfaceMask").arraySize = 0;
                Property(serialized, "surfacePalette").arraySize = 0;
                SetInt(serialized, "surfaceDataVersion", 0);
                SetInt(serialized, "surfaceBakeRevision", 0);
                SetInt(serialized, "surfaceBakeGeneratorVersion", 0);
                SetInt(serialized, "staticWaterMaskBakeRevision", 0);
                SetInt(serialized, "staticWaterMaskBakeGeneratorVersion", 0);
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(level);
            return level;
        }

        internal static Texture2D ImportAndVerify(string path, byte[] raw,
            int resolution = SourceResolution, int maximumTextureSize = 4096)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Require(importer != null, "Missing texture importer for " + path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.isReadable = true;
            importer.mipmapEnabled = false;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = maximumTextureSize;
            importer.ClearPlatformTextureSettings("Standalone");
            TextureImporterPlatformSettings settings = importer.GetDefaultPlatformTextureSettings();
            settings.format = TextureImporterFormat.R16;
            settings.maxTextureSize = maximumTextureSize;
            settings.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(settings);
            importer.SaveAndReimport();
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Require(texture != null && texture.width == resolution && texture.height == resolution
                    && texture.format == TextureFormat.R16, "PNG was resized or lost R16 precision: " + path);
            var pixels = texture.GetPixelData<ushort>(0);
            Require(pixels.Length == resolution * resolution, "Unexpected imported sample count.");
            for (int index = 0; index < pixels.Length; index++)
                if (pixels[index] != ReadUShort(raw, index))
                    throw new InvalidOperationException("RAW/PNG sample mismatch at ("
                        + index % resolution + ", " + index / resolution + ") in " + path);
            return texture;
        }

        private static byte[] QuantizeRaw(float[,] heights, out ushort minimum, out ushort maximum)
        {
            byte[] raw = new byte[SourceResolution * SourceResolution * 2];
            minimum = ushort.MaxValue;
            maximum = 0;
            for (int z = 0; z < SourceResolution; z++)
            for (int x = 0; x < SourceResolution; x++)
            {
                float height = heights[z, x];
                Require(!float.IsNaN(height) && !float.IsInfinity(height) && height >= 0f && height <= 1f,
                    "Terrain contains a height outside the V1 encoding range.");
                ushort value = (ushort)Mathf.RoundToInt(height * 65535f);
                int offset = (z * SourceResolution + x) * 2;
                raw[offset] = (byte)value;
                raw[offset + 1] = (byte)(value >> 8);
                if (value < minimum) minimum = value;
                if (value > maximum) maximum = value;
            }
            return raw;
        }

        internal static void WriteGray16Png(string path, byte[] raw, int resolution = SourceResolution)
        {
            // PNG uses top-down big-endian samples. Unity RAW uses bottom-up little-endian samples.
            // EncodeToPNG is intentionally not used: this must remain a 16-bit single-channel image.
            Require(raw.Length == resolution * resolution * 2, "RAW byte count differs from PNG dimensions.");
            byte[] scanlines = new byte[resolution * (1 + resolution * 2)];
            int destination = 0;
            for (int z = resolution - 1; z >= 0; z--)
            {
                scanlines[destination++] = 0; // PNG filter: None.
                for (int x = 0; x < resolution; x++)
                {
                    int source = (z * resolution + x) * 2;
                    scanlines[destination++] = raw[source + 1];
                    scanlines[destination++] = raw[source];
                }
            }
            byte[] compressed;
            using (var buffer = new MemoryStream())
            {
                // DeflateStream emits raw DEFLATE; PNG IDAT requires the RFC 1950 zlib envelope.
                buffer.WriteByte(0x78);
                buffer.WriteByte(0x9c);
                using (var deflate = new DeflateStream(buffer, System.IO.Compression.CompressionLevel.Optimal, true))
                    deflate.Write(scanlines, 0, scanlines.Length);
                WriteBigEndian(buffer, Adler32(scanlines));
                compressed = buffer.ToArray();
            }
            using (var output = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
                output.Write(signature, 0, signature.Length);
                using (var header = new MemoryStream())
                {
                    WriteBigEndian(header, (uint)resolution);
                    WriteBigEndian(header, (uint)resolution);
                    header.WriteByte(16); // bit depth
                    header.WriteByte(0);  // grayscale, no alpha
                    header.WriteByte(0);  // compression
                    header.WriteByte(0);  // filter
                    header.WriteByte(0);  // no interlace
                    WriteChunk(output, "IHDR", header.ToArray());
                }
                WriteChunk(output, "IDAT", compressed);
                WriteChunk(output, "IEND", Array.Empty<byte>());
            }
        }

        internal static void ValidatePngHeader(string path, int resolution = SourceResolution)
        {
            byte[] header = new byte[33];
            using (var input = File.OpenRead(path))
                Require(input.Read(header, 0, header.Length) == header.Length, "Truncated PNG: " + path);
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
            for (int index = 0; index < signature.Length; index++)
                Require(header[index] == signature[index], "Invalid PNG signature: " + path);
            Require(ReadBigEndian(header, 8) == 13 && Encoding.ASCII.GetString(header, 12, 4) == "IHDR"
                    && ReadBigEndian(header, 16) == resolution && ReadBigEndian(header, 20) == resolution
                    && header[24] == 16 && header[25] == 0 && header[26] == 0 && header[27] == 0 && header[28] == 0,
                "Expected " + resolution + " x " + resolution + ", 16-bit grayscale PNG: " + path);
            uint crc = Crc32(header, 12, 17);
            Require(crc == ReadBigEndian(header, 29), "PNG IHDR checksum mismatch: " + path);
        }

        private static Edges ReadRawEdges(byte[] raw)
        {
            var edges = new Edges(SourceResolution);
            for (int index = 0; index < SourceResolution; index++)
            {
                edges.left[index] = ReadUShort(raw, index * SourceResolution);
                edges.right[index] = ReadUShort(raw, index * SourceResolution + SourceResolution - 1);
                edges.bottom[index] = ReadUShort(raw, index);
                edges.top[index] = ReadUShort(raw, (SourceResolution - 1) * SourceResolution + index);
            }
            return edges;
        }

        private static Edges ReadFloatEdges(Texture2D texture)
        {
            Require(texture != null && texture.format == TextureFormat.RFloat && texture.width == BakeResolution
                    && texture.height == BakeResolution, "Unexpected V1 baked height texture.");
            var pixels = texture.GetPixelData<float>(0);
            var edges = new Edges(BakeResolution);
            for (int index = 0; index < BakeResolution; index++)
            {
                edges.left[index] = pixels[index * BakeResolution];
                edges.right[index] = pixels[index * BakeResolution + BakeResolution - 1];
                edges.bottom[index] = pixels[index];
                edges.top[index] = pixels[(BakeResolution - 1) * BakeResolution + index];
            }
            return edges;
        }

        internal static void VerifyBakedChannels(HeightMapPrebakedData data)
        {
            var raw = data.RawDetailHeightData.GetData<float>();
            var detail = data.DetailHeightData.GetData<float>();
            var surface = data.SurfaceHeightData.GetData<float>();
            Require(raw.Length == detail.Length && raw.Length == surface.Length, "V1 channel size mismatch.");
            for (int index = 0; index < raw.Length; index++)
                Require(Mathf.Abs(raw[index] - detail[index]) <= 0.000001f
                        && Mathf.Abs(raw[index] - surface[index]) <= 0.000001f,
                    "V1 physical channels unexpectedly contain smoothing.");
        }

        private static void VerifyGridEdges(Edges[] edges, float tolerance, string label)
        {
            VerifyEdge(edges[0].right, edges[1].left, tolerance, label + ": lower X boundary");
            VerifyEdge(edges[2].right, edges[3].left, tolerance, label + ": upper X boundary");
            VerifyEdge(edges[0].top, edges[2].bottom, tolerance, label + ": left Z boundary");
            VerifyEdge(edges[1].top, edges[3].bottom, tolerance, label + ": right Z boundary");
        }

        private static void VerifyEdge(float[] first, float[] second, float tolerance, string label)
        {
            Require(first.Length == second.Length, label + " size mismatch.");
            for (int index = 0; index < first.Length; index++)
                Require(Mathf.Abs(first[index] - second[index]) <= tolerance,
                    label + " mismatch at edge sample " + index + ". Fix the authored seam and export again.");
        }

        private static void WriteChunk(Stream output, string type, byte[] data)
        {
            byte[] typeBytes = Encoding.ASCII.GetBytes(type);
            WriteBigEndian(output, (uint)data.Length);
            output.Write(typeBytes, 0, typeBytes.Length);
            output.Write(data, 0, data.Length);
            uint crc = 0xffffffff;
            foreach (byte value in typeBytes) crc = UpdateCrc(crc, value);
            foreach (byte value in data) crc = UpdateCrc(crc, value);
            WriteBigEndian(output, crc ^ 0xffffffff);
        }

        private static uint Adler32(byte[] bytes)
        {
            uint a = 1;
            uint b = 0;
            // Bound the accumulation to avoid uint overflow and repeated division per byte.
            for (int offset = 0; offset < bytes.Length;)
            {
                int end = Math.Min(offset + 5552, bytes.Length);
                while (offset < end) { a += bytes[offset++]; b += a; }
                a %= 65521;
                b %= 65521;
            }
            return b << 16 | a;
        }

        private static uint Crc32(byte[] bytes, int offset, int count)
        {
            uint crc = 0xffffffff;
            for (int index = offset; index < offset + count; index++) crc = UpdateCrc(crc, bytes[index]);
            return crc ^ 0xffffffff;
        }

        private static uint UpdateCrc(uint crc, byte value)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320;
            return crc;
        }

        private static void WriteBigEndian(Stream stream, uint value)
        {
            stream.WriteByte((byte)(value >> 24));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
        }

        private static uint ReadBigEndian(byte[] bytes, int offset)
        {
            return (uint)bytes[offset] << 24 | (uint)bytes[offset + 1] << 16
                | (uint)bytes[offset + 2] << 8 | bytes[offset + 3];
        }

        internal static ushort ReadUShort(byte[] bytes, int sample)
        {
            int offset = sample * 2;
            return (ushort)(bytes[offset] | bytes[offset + 1] << 8);
        }

        internal static bool FileBytesEqual(string path, byte[] bytes)
        {
            if (new FileInfo(path).Length != bytes.Length) return false;
            using (var stream = File.OpenRead(path))
            {
                var buffer = new byte[65536];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int count = stream.Read(buffer, 0, Math.Min(buffer.Length, bytes.Length - offset));
                    if (count == 0) return false;
                    for (int index = 0; index < count; index++)
                        if (buffer[index] != bytes[offset + index]) return false;
                    offset += count;
                }
            }
            return true;
        }

        internal static string Sha256(byte[] raw)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(raw)).Replace("-", "").ToLowerInvariant();
        }

        private static SerializedProperty Property(SerializedObject serialized, string name)
        {
            SerializedProperty property = serialized.FindProperty(name);
            Require(property != null, "Missing V1 Map Data property: " + name);
            return property;
        }

        private static void SetObject(SerializedObject serialized, string name, UnityEngine.Object value)
            => Property(serialized, name).objectReferenceValue = value;
        private static void SetFloat(SerializedObject serialized, string name, float value)
            => Property(serialized, name).floatValue = value;
        private static void SetInt(SerializedObject serialized, string name, int value)
            => Property(serialized, name).intValue = value;
        private static void SetBool(SerializedObject serialized, string name, bool value)
            => Property(serialized, name).boolValue = value;
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class Edges
        {
            public readonly float[] left, right, bottom, top;
            public Edges(int resolution)
            {
                left = new float[resolution]; right = new float[resolution];
                bottom = new float[resolution]; top = new float[resolution];
            }
        }

        [Serializable]
        private sealed class WorldManifest
        {
            public string format = "Terrain Grid V1";
            public int version = 1;
            public int gridWidth = 2;
            public int gridHeight = 2;
            public Vector2 worldSizeMeters = new Vector2(500f, 500f);
            public float tileSizeMeters = TileSize;
            public float minimumHeightMeters = 0f;
            public float maximumHeightMeters = HeightRange;
            public int sourceResolution = SourceResolution;
            public int bakedResolution = BakeResolution;
            public float contourIntervalMeters = 7f;
            public bool normalizeSourceRange = false;
            public float surfaceSmoothingSigmaMeters = 0f;
            public float detailSmoothingSigmaMeters = 0f;
            public string rawByteOrder = "little";
            public string rawRowOrder = "Z=0 first; no Flip Vertically";
            public string pngFormat = "16-bit grayscale; top row Z=250";
            public string validation;
            public TileManifest[] tiles = new TileManifest[4];
        }

        [Serializable]
        private sealed class TileManifest
        {
            public string name;
            public int gridX;
            public int gridZ;
            public Vector2 tileOriginMeters;
            public Vector3 sourceWorldOrigin;
            public string rawAssetPath;
            public string heightMapAssetPath;
            public string mapDataAssetPath;
            public string prebakeAssetPath;
            public string rawSha256;
            public float actualMinimumMeters;
            public float actualMaximumMeters;
        }
    }
}
