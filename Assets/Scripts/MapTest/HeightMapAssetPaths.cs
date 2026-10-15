#if UNITY_EDITOR
using System;
using System.IO;

namespace AnimalGame.MapTest
{
    /// <summary>One path policy for exporters, painters and local cache discovery.</summary>
    public static class HeightMapAssetPaths
    {
        public static bool IsOrganized(string path) => path.StartsWith("Assets/Maps/TerrainGridV1/", StringComparison.Ordinal)
            || path.StartsWith("Assets/Maps/MainHeightMapLevel/", StringComparison.Ordinal);
        public static string DataDirectory(string levelPath)
        {
            string directory = Path.GetDirectoryName(levelPath).Replace('\\', '/');
            return IsOrganized(levelPath) ? directory + "/data" : directory;
        }
        public static string PrebakePath(string levelPath) => DataDirectory(levelPath) + "/"
            + Path.GetFileNameWithoutExtension(levelPath) + "_HeightPrebake.asset";
    }
}
#endif
