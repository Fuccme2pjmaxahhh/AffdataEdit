using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// macOS CI build. It is uploaded as a workflow artifact and not published to R2.
public static class MacCiBuild
{
    public static void Build()
    {
        const string output = "Build/macOS/AffdataEdit.app";
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        EditorUserBuildSettings.SetPlatformSettings("Standalone", "OSXUniversal", "Architecture", "ARM64");
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/_Scenes/ArcEditor.unity" },
            locationPathName = output,
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.StrictMode
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new BuildFailedException($"macOS build: {report.summary.result}, {report.summary.totalErrors} errors.");
        if (report.summary.totalWarnings != 0)
            Debug.LogWarning($"macOS build finished with {report.summary.totalWarnings} warnings.");
        foreach (string backup in Directory.GetDirectories("Build/macOS", "*_BackUpThisFolder_ButDontShipItWithYourGame"))
            Directory.Delete(backup, true);
    }
}
