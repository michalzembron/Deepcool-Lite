namespace DeepCoolLite.Core;

/// <summary>Constants shared by the service and the settings app.</summary>
public static class AppInfo
{
    public const string ProductName = "DeepCool Lite";
    public const string ServiceName = "DeepCoolLite";
    public const string ServiceDisplayName = "DeepCool Lite";
    public const string ServiceDescription = "Sends CPU temperature, power, usage and clock to the DeepCool cooler display.";
    public const string ServiceExe = "DeepCoolLite.Service.exe";
    public const string SettingsExe = "DeepCoolLite.exe";
    public const string PipeName = "DeepCoolLite";

    /// <summary>C:\Program Files\DeepCool Lite - admin-writable only (the service runs as SYSTEM).</summary>
    public static string InstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), ProductName);

    /// <summary>
    /// C:\ProgramData\DeepCool Lite - written only by the service (SYSTEM) and the installer.
    /// Users have read-only access; settings changes go through the named pipe.
    /// </summary>
    public static string DataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), ProductName);

    /// <summary>%LOCALAPPDATA%\DeepCool Lite - per-user backup of the official DeepCool app autostart.</summary>
    public static string UserDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductName);

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
}
