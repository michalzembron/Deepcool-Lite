using System.Diagnostics;
using DeepCoolLite.Core;

namespace DeepCoolLite.Settings.Setup;

/// <summary>Compares this copy of the app with the one installed in Program Files.</summary>
public static class InstalledCopy
{
    public static bool IsRunningFromInstallDir =>
        Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)
            .Equals(AppInfo.InstallDir, StringComparison.OrdinalIgnoreCase);

    public static Version? CurrentVersion => FileVersion(Environment.ProcessPath);

    public static Version? InstalledVersion =>
        FileVersion(Path.Combine(AppInfo.InstallDir, AppInfo.SettingsExe))
        ?? FileVersion(Path.Combine(AppInfo.InstallDir, LegacySettingsExe));

    /// <summary>Name of the settings exe in the multi-file pre-release builds (0.1-0.2).</summary>
    private const string LegacySettingsExe = "DeepCoolLite.Settings.exe";

    private static Version? FileVersion(string? path)
    {
        if (path is null || !File.Exists(path)) return null;
        var info = FileVersionInfo.GetVersionInfo(path);
        return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
    }
}
