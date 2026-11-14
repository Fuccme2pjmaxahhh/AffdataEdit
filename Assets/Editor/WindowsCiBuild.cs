using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

public static class WindowsCiBuild
{
    public static void Build()
    {
        PlayerSettings.bundleVersion = System.DateTime.UtcNow.ToString("yyyy.MM.dd", System.Globalization.CultureInfo.InvariantCulture);
        const string output = "Build/Windows/AffdataEdit.exe";
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/_Scenes/Startup.unity", "Assets/_Scenes/ArcEditor.unity" },
            locationPathName = output,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.StrictMode | BuildOptions.CompressWithLz4HC
        });
        if (report.summary.result != BuildResult.Succeeded || report.summary.totalWarnings != 0)
            throw new BuildFailedException($"Windows build: {report.summary.result}, " +
                $"{report.summary.totalErrors} errors, {report.summary.totalWarnings} warnings.");
        foreach (string backup in Directory.GetDirectories("Build/Windows", "*_BackUpThisFolder_ButDontShipItWithYourGame"))
            Directory.Delete(backup, true);
    }
}
