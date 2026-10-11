using System;
using System.IO;
using System.Text;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEngine;

namespace AnimalGame.Editor
{
    /// <summary>Combines the four authoring tiles into one continuous high-resolution test level.</summary>
    public static class TerrainGridV1CombinedExporter
    {
        public const string Folder = "Assets/Maps/TerrainGridV1/Combined";
        public const string Name = "Combined_500m";
        public const string LevelPath = Folder + "/" + Name + "_MapData.asset";
        public const int SourceResolution = 4097;
        public const int BakeResolution = 4096;
        public const float MapSizeMeters = 500f;
        private const int TileResolution = TerrainGridV1Builder.Resolution;
        private const float HeightRangeMeters = TerrainGridV1Builder.HeightRange;

        [MenuItem("Animal Game/Terrain Grid V1/Export Combined 500m Height Map")]
        private static void ExportMenu()
        {
            HeightMapLevelAsset level = Export();
            Selection.activeObject = level;
            EditorGUIUtility.PingObject(level);
        }

        public static HeightMapLevelAsset Export()
        {
            string sourceReport = TerrainGridV1Builder.ValidateGrid();
            Terrain[] tiles = TerrainGridV1Builder.GetGridTerrains();
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            try
            {
                EditorUtility.DisplayProgressBar("Export Combined V1 Map", "Combining source heights...", 0.05f);
                byte[] raw = MergeRaw(tiles, out ushort minimum, out ushort maximum, out int duplicateSamples);
                Require(duplicateSamples == 4 * TileResolution * TileResolution - SourceResolution * SourceResolution,
                    "The combined grid did not share exactly one row/column at each tile boundary.");
                string rawPath = Folder + "/" + Name + ".raw";
                string pngPath = Folder + "/" + Name + "_height_R16.png";
                if (!File.Exists(rawPath) || !TerrainGridV1Exporter.FileBytesEqual(rawPath, raw))
                    WriteBytesAtomic(rawPath, raw);

                // Recreate the PNG even when RAW is unchanged: this also recovers an interrupted prior PNG write.
                EditorUtility.DisplayProgressBar("Export Combined V1 Map", "Writing lossless 4097 x 4097 R16 PNG...", 0.2f);
                string temporaryPng = pngPath + ".tmp";
                try
                {
                    TerrainGridV1Exporter.WriteGray16Png(temporaryPng, raw, SourceResolution);
                    TerrainGridV1Exporter.ValidatePngHeader(temporaryPng, SourceResolution);
                    ReplaceFile(temporaryPng, pngPath);
                }
                finally
                {
                    if (File.Exists(temporaryPng)) File.Delete(temporaryPng);
                }
                EditorUtility.DisplayProgressBar("Export Combined V1 Map", "Verifying all imported 16-bit samples...", 0.35f);
                Texture2D heightMap = TerrainGridV1Exporter.ImportAndVerify(pngPath, raw, SourceResolution, 8192);
                HeightMapLevelAsset level = TerrainGridV1Exporter.ConfigureLevel(
                    Name, heightMap, Folder, MapSizeMeters, BakeResolution);
                ConfigureAuthoringResolutions(level);
                Require(level.BakedHeightResolution == BakeResolution && level.PreviewResolution == BakeResolution,
                    "The level asset still clamps the combined bake or preview below 4096.");
                Require(level.MapSizeMeters == new Vector2(MapSizeMeters, MapSizeMeters)
                        && level.MinimumHeightMeters == 0f && level.MaximumHeightMeters == HeightRangeMeters
                        && level.PixelsPerUnit == 30f && !level.NormalizeSourceRange,
                    "Combined level settings differ from the V1 scale.");

                // Settings-only Matches cannot establish source-pixel freshness. Rebuild every export,
                // preserving the Map Data/prebake GUIDs and this level's authored water/surface data.
                HeightMapPrebakeEditor.Bake(level, preserveAssetGuid: true);
                Require(level.PrebakedHeightField != null && level.PrebakedHeightField.Matches(level),
                    "The combined height prebake did not complete.");
                TerrainGridV1Exporter.VerifyBakedChannels(level.PrebakedHeightField);
                int bakedProbes = VerifyBakedSamples(level.PrebakedHeightField, raw);
                var manifest = new CombinedManifest
                {
                    sourceRawAssetPath = rawPath,
                    sourcePngAssetPath = pngPath,
                    mapDataAssetPath = LevelPath,
                    prebakeAssetPath = AssetDatabase.GetAssetPath(level.PrebakedHeightField),
                    rawSha256 = TerrainGridV1Exporter.Sha256(raw),
                    rawByteCount = raw.Length,
                    sharedSamplesVerified = duplicateSamples,
                    bakedSampleProbes = bakedProbes,
                    actualMinimumHeightMeters = minimum / 65535f * HeightRangeMeters,
                    actualMaximumHeightMeters = maximum / 65535f * HeightRangeMeters,
                    validation = "PASS: four source tiles merged with exact shared R16 edges; every imported PNG "
                        + "sample equals RAW; all V1 physical channels unsmoothed; baked corner/seam/interior samples verified."
                };
                for (int index = 0; index < tiles.Length; index++)
                    manifest.sourceTiles[index] = new SourceTile
                    {
                        name = tiles[index].name,
                        gridX = index % 2,
                        gridZ = index / 2,
                        tileOriginMeters = new Vector2(index % 2 * 250f, index / 2 * 250f),
                        sourceWorldOrigin = tiles[index].transform.position,
                        terrainDataAssetPath = AssetDatabase.GetAssetPath(tiles[index].terrainData)
                    };
                WriteTextAtomic(Folder + "/world-manifest.json", JsonUtility.ToJson(manifest, true));
                string report = sourceReport + "\n" + manifest.validation + "\n"
                    + $"Combined source: {SourceResolution} x {SourceResolution}, {raw.Length} RAW bytes, "
                    + $"{duplicateSamples} shared samples checked and stored once.\n"
                    + "RAW: 16-bit little-endian, row 0 is Z=0. PNG: gray16, top row is Z=500; Unity imports Y=Z.\n"
                    + "Physical map: 500 x 500 m, absolute heights 0..70 m; no normalization or smoothing.\n"
                    + "Bake/preview: 4096, PPU 30, contours 7 m; world units per map meter = 4096 / 30 / 500.\n"
                    + "Water authoring: 1024; surface ID mask: 1024; surface visual: 4096; closed-contour alpha: 1024.\n"
                    + "All water/terrain pattern sizes, transition widths and noise lengths remain in V1 map meters.\n"
                    + $"Source spacing: {MapSizeMeters / (SourceResolution - 1):F9} m; "
                    + $"baked spacing: {MapSizeMeters / (BakeResolution - 1):F9} m.\n"
                    + $"Actual range: {manifest.actualMinimumHeightMeters:F6}..{manifest.actualMaximumHeightMeters:F6} m.\n"
                    + "This is one continuous height field for the four-tile prototype, not a streaming implementation.\n";
                WriteTextAtomic(Folder + "/validation-report.txt", report);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log(report, level);
                return level;
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        private static void ConfigureAuthoringResolutions(HeightMapLevelAsset level)
        {
            var serialized = new SerializedObject(level);
            foreach (string name in new[] { "staticWaterResolution", "surfaceMaskResolution", "surfaceClosedContourAlphaResolution" })
            {
                SerializedProperty property = serialized.FindProperty(name);
                Require(property != null, "Missing combined authoring resolution: " + name);
                property.intValue = 1024;
            }
            SerializedProperty visual = serialized.FindProperty("surfaceBakeResolution");
            Require(visual != null, "Missing combined surface bake resolution.");
            visual.intValue = BakeResolution;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(level);
        }

        private static byte[] MergeRaw(Terrain[] tiles, out ushort minimum, out ushort maximum, out int duplicates)
        {
            Require(tiles.Length == 4, "Expected four authoring Terrain tiles.");
            byte[] raw = new byte[SourceResolution * SourceResolution * 2];
            minimum = ushort.MaxValue;
            maximum = 0;
            duplicates = 0;
            // Read small bands instead of keeping four full float[,] fields in memory.
            const int bandRows = 64;
            for (int index = 0; index < tiles.Length; index++)
            {
                int tileX = index % 2;
                int tileZ = index / 2;
                for (int firstRow = 0; firstRow < TileResolution; firstRow += bandRows)
                {
                    int rows = Math.Min(bandRows, TileResolution - firstRow);
                    float[,] band = tiles[index].terrainData.GetHeights(0, firstRow, TileResolution, rows);
                    for (int row = 0; row < rows; row++)
                    for (int x = 0; x < TileResolution; x++)
                    {
                        float normalized = band[row, x];
                        Require(!float.IsNaN(normalized) && !float.IsInfinity(normalized)
                                && normalized >= 0f && normalized <= 1f, "Source height is outside 0..70 m.");
                        ushort value = (ushort)Mathf.RoundToInt(normalized * 65535f);
                        int localZ = firstRow + row;
                        int globalX = tileX * (TileResolution - 1) + x;
                        int globalZ = tileZ * (TileResolution - 1) + localZ;
                        int sample = globalZ * SourceResolution + globalX;
                        if ((tileX > 0 && x == 0) || (tileZ > 0 && localZ == 0))
                        {
                            if (TerrainGridV1Exporter.ReadUShort(raw, sample) != value)
                                throw new InvalidOperationException("Shared R16 sample differs at combined ("
                                    + globalX + ", " + globalZ + "). Fix the authored seam before export.");
                            duplicates++;
                        }
                        else
                        {
                            raw[sample * 2] = (byte)value;
                            raw[sample * 2 + 1] = (byte)(value >> 8);
                        }
                        if (value < minimum) minimum = value;
                        if (value > maximum) maximum = value;
                    }
                }
            }
            return raw;
        }

        private static int VerifyBakedSamples(HeightMapPrebakedData data, byte[] raw)
        {
            Require(data.Resolution == BakeResolution && data.RawDetailTexture.width == BakeResolution
                    && data.RawDetailTexture.height == BakeResolution, "Combined baked dimensions differ.");
            var baked = data.RawDetailTexture.GetPixelData<float>(0);
            int[] important = { 0, 1, BakeResolution / 2 - 1, BakeResolution / 2,
                BakeResolution - 2, BakeResolution - 1 };
            int count = 0;
            foreach (int z in important)
                foreach (int x in important)
                {
                    VerifyBakedSample(baked[z * BakeResolution + x], raw, x, z);
                    count++;
                }
            for (int index = 0; index < 257; index++)
            {
                int x = index * 977 % BakeResolution;
                int z = index * 1597 % BakeResolution;
                VerifyBakedSample(baked[z * BakeResolution + x], raw, x, z);
                count++;
            }
            return count;
        }

        private static void VerifyBakedSample(float actual, byte[] raw, int x, int z)
        {
            float sourceX = x / (BakeResolution - 1f) * (SourceResolution - 1);
            float sourceZ = z / (BakeResolution - 1f) * (SourceResolution - 1);
            int x0 = Mathf.FloorToInt(sourceX), z0 = Mathf.FloorToInt(sourceZ);
            int x1 = Math.Min(x0 + 1, SourceResolution - 1), z1 = Math.Min(z0 + 1, SourceResolution - 1);
            const float inverseUShort = 1f / 65535f;
            float bottom = Mathf.Lerp(TerrainGridV1Exporter.ReadUShort(raw, z0 * SourceResolution + x0) * inverseUShort,
                TerrainGridV1Exporter.ReadUShort(raw, z0 * SourceResolution + x1) * inverseUShort, sourceX - x0);
            float top = Mathf.Lerp(TerrainGridV1Exporter.ReadUShort(raw, z1 * SourceResolution + x0) * inverseUShort,
                TerrainGridV1Exporter.ReadUShort(raw, z1 * SourceResolution + x1) * inverseUShort, sourceX - x0);
            float expected = Mathf.Lerp(bottom, top, sourceZ - z0);
            if (Mathf.Abs(actual - expected) > 0.000002f || float.IsNaN(actual))
                throw new InvalidOperationException("Combined bake differs from source at (" + x + ", " + z + ").");
        }

        private static void WriteBytesAtomic(string path, byte[] bytes)
        {
            string temporary = path + ".tmp";
            try { File.WriteAllBytes(temporary, bytes); ReplaceFile(temporary, path); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static void WriteTextAtomic(string path, string text)
            => WriteBytesAtomic(path, new UTF8Encoding(false).GetBytes(text));

        private static void ReplaceFile(string temporary, string path)
        {
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        [Serializable]
        private sealed class CombinedManifest
        {
            public string format = "Terrain Grid V1 Combined";
            public int version = 1;
            public Vector2 mapSizeMeters = new Vector2(MapSizeMeters, MapSizeMeters);
            public float minimumHeightMeters = 0f;
            public float maximumHeightMeters = HeightRangeMeters;
            public int sourceResolution = SourceResolution;
            public int bakedResolution = BakeResolution;
            public int previewResolution = BakeResolution;
            public float pixelsPerUnit = 30f;
            public float worldUnitsPerMapMeter = BakeResolution / 30f / MapSizeMeters;
            public float contourIntervalMeters = 7f;
            public int staticWaterResolution = 1024;
            public int surfaceMaskResolution = 1024;
            public int surfaceBakeResolution = BakeResolution;
            public int surfaceClosedContourAlphaResolution = 1024;
            public bool normalizeSourceRange = false;
            public float surfaceSmoothingSigmaMeters = 0f;
            public float detailSmoothingSigmaMeters = 0f;
            public string sourceRawAssetPath, sourcePngAssetPath, mapDataAssetPath, prebakeAssetPath;
            public string rawSha256;
            public string rawByteOrder = "little";
            public string rawRowOrder = "Z=0 first; no Flip Vertically";
            public string pngFormat = "16-bit grayscale; top row Z=500";
            public int rawByteCount;
            public int sharedSamplesVerified;
            public int bakedSampleProbes;
            public float actualMinimumHeightMeters, actualMaximumHeightMeters;
            public string validation;
            public SourceTile[] sourceTiles = new SourceTile[4];
        }

        [Serializable]
        private sealed class SourceTile
        {
            public string name;
            public int gridX, gridZ;
            public Vector2 tileOriginMeters;
            public Vector3 sourceWorldOrigin;
            public string terrainDataAssetPath;
        }
    }
}
