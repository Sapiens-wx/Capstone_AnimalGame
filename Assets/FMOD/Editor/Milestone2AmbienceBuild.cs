using System;
using System.Collections.Generic;
using System.IO;
using Capstone.Audio;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Capstone.Audio.Editor
{
    public sealed class Milestone2AmbienceBuild : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            try
            {
                StageAudio(report.summary.platform);
            }
            catch (BuildFailedException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new BuildFailedException("[Milestone 2 Ambience] Could not prepare ambience audio: "
                    + exception.Message);
            }
        }

        private static void StageAudio(BuildTarget target)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string source = Path.GetFullPath(Path.Combine(projectRoot,
                Milestone2AmbienceRuntime.SourceRelativePath));
            string destination = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath,
                Milestone2AmbienceRuntime.StreamingRelativePath));
            string expectedDestination = Path.GetFullPath(Path.Combine(projectRoot,
                "Assets", "StreamingAssets", "Milestone2", "NatureAmbience"));
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            // This callback owns only this generated folder, never FMOD banks or
            // any other StreamingAssets content. Reject redirected directories.
            if (!string.Equals(destination, expectedDestination, comparison))
                throw new IOException("The generated ambience folder is outside its expected project location.");
            VerifyDirectory(destination, projectRoot, comparison);

            var files = new List<string>();
            if (Directory.Exists(source))
                foreach (string file in Directory.GetFiles(source, "*", SearchOption.TopDirectoryOnly))
                    if (Milestone2AmbienceRuntime.IsAudioFile(file))
                        files.Add(file);

            if (files.Count > 0 && !IsDesktop(target))
                throw new BuildFailedException("[Milestone 2 Ambience] Raw ambience audio currently supports "
                    + "Desktop builds only. Remove the audio from "
                    + Milestone2AmbienceRuntime.SourceRelativePath + " before building for " + target + ".");

            Directory.CreateDirectory(destination);
            var currentNames = new HashSet<string>(Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                string stagedFile = Path.Combine(destination, name);
                VerifyFile(stagedFile);
                File.Copy(file, stagedFile, true);
                currentNames.Add(name);
            }

            foreach (string file in Directory.GetFiles(destination, "*", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(file);
                if (Milestone2AmbienceRuntime.IsAudioFile(file) && !currentNames.Contains(name))
                {
                    DeleteGeneratedFile(file);
                    DeleteGeneratedFile(file + ".meta");
                }
                else if (name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    string audioName = Path.GetFileNameWithoutExtension(name);
                    if (Milestone2AmbienceRuntime.IsAudioFile(audioName) && !currentNames.Contains(audioName))
                        DeleteGeneratedFile(file);
                }
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static bool IsDesktop(BuildTarget target)
        {
            return target == BuildTarget.StandaloneWindows
                || target == BuildTarget.StandaloneWindows64
                || target == BuildTarget.StandaloneLinux64
                || target == BuildTarget.StandaloneOSX;
        }

        private static void VerifyDirectory(string destination, string projectRoot, StringComparison comparison)
        {
            for (var directory = new DirectoryInfo(destination); directory != null; directory = directory.Parent)
            {
                if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("The generated ambience path contains a symbolic link: " + directory.FullName);
                if (string.Equals(directory.FullName, projectRoot, comparison))
                    return;
            }
            throw new IOException("The generated ambience folder is outside the project.");
        }

        private static void VerifyFile(string file)
        {
            if (File.Exists(file) && (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The generated ambience file is a symbolic link: " + file);
        }

        private static void DeleteGeneratedFile(string file)
        {
            if (!File.Exists(file))
                return;
            VerifyFile(file);
            File.Delete(file);
        }
    }
}
