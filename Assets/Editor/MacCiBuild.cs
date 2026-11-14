using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

// Apple Silicon player; release.yml publishes it separately from Windows.
public static class MacCiBuild
{
    public static void Build()
    {
        PlayerSettings.bundleVersion = System.DateTime.UtcNow.ToString("yyyy.MM.dd", System.Globalization.CultureInfo.InvariantCulture);
        const string output = "Build/macOS/AffdataEdit.app";
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        EditorUserBuildSettings.SetPlatformSettings("Standalone", "OSXUniversal", "Architecture", "ARM64");
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/_Scenes/Startup.unity", "Assets/_Scenes/ArcEditor.unity" },
            locationPathName = output,
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.StrictMode | BuildOptions.CompressWithLz4HC
        });
        if (report.summary.result != BuildResult.Succeeded || report.summary.totalWarnings != 0)
            throw new BuildFailedException($"macOS build: {report.summary.result}, " +
                $"{report.summary.totalErrors} errors, {report.summary.totalWarnings} warnings.");
        foreach (string backup in Directory.GetDirectories("Build/macOS", "*_BackUpThisFolder_ButDontShipItWithYourGame"))
            Directory.Delete(backup, true);
    }
}
