using DeepCoolLite.Core;

namespace DeepCoolLite.Service;

/// <summary>
/// State shared between the display loop (<see cref="Worker"/>) and the named pipe server.
/// Settings are persisted to %ProgramData%\DeepCool Lite, which only SYSTEM/Administrators can write.
/// </summary>
public sealed class ServiceState(ILogger<ServiceState> logger)
{
    private readonly Lock _lock = new();
    private ServiceStatus _status = new() { Settings = LoadSettings(logger) };

    public LiteSettings Settings
    {
        get { lock (_lock) return _status.Settings; }
    }

    public ServiceStatus Snapshot
    {
        get { lock (_lock) return _status; }
    }

    public void Update(Func<ServiceStatus, ServiceStatus> change)
    {
        lock (_lock) _status = change(_status);
    }

    public void ApplySettings(LiteSettings requested)
    {
        var settings = requested.Normalize();
        Update(s => s with { Settings = settings });

        try
        {
            if (Directory.Exists(AppInfo.DataDir)) JsonStore.Save(AppInfo.SettingsFile, settings);
            else logger.LogWarning("{Dir} does not exist - settings are not persisted. Reinstall to fix.", AppInfo.DataDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Could not save settings: {Message}", ex.Message);
        }

        logger.LogInformation("Settings changed: {Settings}", settings);
    }

    private static LiteSettings LoadSettings(ILogger logger)
    {
        var settings = (JsonStore.Load<LiteSettings>(AppInfo.SettingsFile) ?? new LiteSettings()).Normalize();
        logger.LogInformation("Settings: {Settings}", settings);
        return settings;
    }
}
