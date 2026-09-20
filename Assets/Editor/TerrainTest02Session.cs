using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AnimalGame.Editor
{
    // Temporary, read-only export inspection for this import session.
    internal static class TerrainTest02Session
    {
        [InitializeOnLoadMethod]
        private static void InspectOnce()
        {
            EditorApplication.delayCall += () =>
            {
                if (SessionState.GetBool("TerrainTest02.Inspected", false)) return;
                SessionState.SetBool("TerrainTest02.Inspected", true);
                var data = AssetDatabase.LoadAssetAtPath<TerrainData>("Assets/New Terrain 1.asset");
                if (data == null) throw new InvalidOperationException("Source TerrainData not found.");
                int n = data.heightmapResolution;
                byte[] raw = File.ReadAllBytes("Assets/Maps/Terrain_Test_02.raw");
                if (raw.Length != n * n * 2) throw new InvalidOperationException("RAW dimensions differ from TerrainData.");
                float[,] heights = data.GetHeights(0, 0, n, n);
                var report = new StringBuilder();
                report.AppendLine($"Resolution: {n}; size: {data.size.ToString("F6")}");
                float min = 1, max = 0;
                foreach (float h in heights) { min = Mathf.Min(min, h); max = Mathf.Max(max, h); }
                report.AppendLine($"Terrain normalized range {min:R} .. {max:R}; meters {min * data.size.y:R} .. {max * data.size.y:R}");
                for (int mode = 0; mode < 4; mode++)
                {
                    float maximum = 0;
                    double total = 0;
                    for (int z = 0; z < n; z++)
                        for (int x = 0; x < n; x++)
                        {
                            int offset = ((mode < 2 ? z : n - 1 - z) * n + x) * 2;
                            int value = (mode % 2 == 0) ? raw[offset] | raw[offset + 1] << 8 : raw[offset] << 8 | raw[offset + 1];
                            float error = Mathf.Abs(value / 65535f - heights[z, x]);
                            maximum = Mathf.Max(maximum, error);
                            total += error;
                        }
                    report.AppendLine($"Mode {mode} (0=LE bottom first,1=BE bottom first,2=LE top first,3=BE top first): mean {total / (n*n):R}, max {maximum:R}");
                }
                for (int z = 1; z < 10; z++)
                    for (int x = 1; x < 10; x++)
                    {
                        float u = x / 10f, v = z / 10f;
                        report.AppendLine($"Point ({u * data.size.x:F2}, {v * data.size.z:F2}): height {data.GetInterpolatedHeight(u, v):F4}; slope {data.GetSteepness(u,v):F2}");
                    }
                Directory.CreateDirectory("Temp/TerrainTest02");
                File.WriteAllText("Temp/TerrainTest02/SourceInspection.txt", report.ToString());
                Debug.Log("Terrain_Test_02 source inspection completed.");
            };
        }
    }
}
