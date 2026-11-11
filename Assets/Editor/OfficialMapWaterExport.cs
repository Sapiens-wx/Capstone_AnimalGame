using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AnimalGame.Editor
{
    internal static class OfficialMapWaterExport
    {
        private const string DepthFile = "water-depth.f32";
        private const string SurfaceFile = "water-surface.f32";
        private const string CoverageFile = "water-coverage.u8";
        private const string LayoutFile = "layout.json";

        internal static void Write(string output, OfficialMapWaterLayout layout)
        {
            string directory = Path.GetFullPath(output);
            Directory.CreateDirectory(directory);
            string staging = Path.Combine(directory, ".water-export-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            string[] files = { DepthFile, SurfaceFile, CoverageFile, LayoutFile };
            try
            {
                WriteFields(staging, layout, out int painted, out int wet);
                string metadata = MakeMetadata(staging, painted, wet);
                File.WriteAllText(Path.Combine(staging, LayoutFile), metadata, new UTF8Encoding(false));

                // Publish the metadata last. Its checksums cover all three fields.
                foreach (string name in files)
                {
                    string source = Path.Combine(staging, name);
                    string target = Path.Combine(directory, name);
                    if (File.Exists(target)) File.Replace(source, target, null);
                    else File.Move(source, target);
                }
            }
            finally
            {
                foreach (string name in files) File.Delete(Path.Combine(staging, name));
                Directory.Delete(staging);
            }
        }

        private static void WriteFields(string directory, OfficialMapWaterLayout layout, out int painted, out int wet)
        {
            using var depth = CreateField(directory, DepthFile);
            using var surface = CreateField(directory, SurfaceFile);
            using var coverage = CreateField(directory, CoverageFile);
            painted = 0;
            wet = 0;
            for (int row = 0; row < OfficialMapWaterLayout.Height; row++)
            {
                int z = OfficialMapWaterLayout.OriginZ + row * OfficialMapWaterLayout.Step;
                for (int column = 0; column < OfficialMapWaterLayout.Width; column++)
                {
                    int x = OfficialMapWaterLayout.OriginX + column * OfficialMapWaterLayout.Step;
                    ushort encodedStage = layout.ReadStage(x, z);
                    float stageMeters = encodedStage / 100f;
                    float depthMeters = 0f;
                    if (encodedStage != 0)
                    {
                        painted++;
                        if (OfficialMapWaterTerrain.TryGround(x, z, out float ground))
                            depthMeters = Math.Max(0f, stageMeters - ground);
                        if (depthMeters > 0f) wet++;
                    }
                    depth.Write(depthMeters);
                    surface.Write(stageMeters);
                    coverage.Write(encodedStage == 0 ? (byte)0 : byte.MaxValue);
                }
            }
        }

        private static BinaryWriter CreateField(string directory, string filename)
        {
            return new BinaryWriter(new FileStream(Path.Combine(directory, filename), FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 65536, FileOptions.SequentialScan));
        }

        private static string MakeMetadata(string directory, int painted, int wet)
        {
            return FormattableString.Invariant($@"{{
  ""grid"": {{ ""originX"": {OfficialMapWaterLayout.OriginX}, ""originZ"": {OfficialMapWaterLayout.OriginZ}, ""width"": {OfficialMapWaterLayout.Width}, ""height"": {OfficialMapWaterLayout.Height}, ""step"": {OfficialMapWaterLayout.Step}, ""row0"": ""south"" }},
  ""fields"": {{ ""depth"": ""{DepthFile}"", ""surface"": ""{SurfaceFile}"", ""coverage"": ""{CoverageFile}"" }},
  ""encoding"": ""little-endian float32 meters for depth and surface; coverage unsigned 8-bit (0 outside, 255 inside); unpainted samples zero"",
  ""sha256"": {{
    ""{DepthFile}"": ""{Checksum(directory, DepthFile)}"",
    ""{SurfaceFile}"": ""{Checksum(directory, SurfaceFile)}"",
    ""{CoverageFile}"": ""{Checksum(directory, CoverageFile)}""
  }},
  ""paintedSamples"": {painted},
  ""wetSamples"": {wet},
  ""paintedSamplesAtOrBelowGround"": {painted - wet}
}}
");
        }

        private static string Checksum(string directory, string filename)
        {
            using var hash = SHA256.Create();
            using var file = File.OpenRead(Path.Combine(directory, filename));
            return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }
    }
}
