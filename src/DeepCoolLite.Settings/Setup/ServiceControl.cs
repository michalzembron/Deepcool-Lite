using System.ServiceProcess;
using DeepCoolLite.Core;

namespace DeepCoolLite.Settings.Setup;

public enum LiteServiceState { NotInstalled, Running, Stopped, Pending }

/// <summary>
/// Service operations available without admin rights
/// (start/stop is allowed for interactive users by the ACL set during installation).
/// </summary>
public static class ServiceControl
{
    public static bool IsInstalled => GetState() != LiteServiceState.NotInstalled;

    public static LiteServiceState GetState()
    {
        using var s = new ServiceController(AppInfo.ServiceName);
        try
        {
            return s.Status switch
            {
                ServiceControllerStatus.Running => LiteServiceState.Running,
                ServiceControllerStatus.Stopped => LiteServiceState.Stopped,
                _ => LiteServiceState.Pending,
            };
        }
        catch (InvalidOperationException)
        {
            return LiteServiceState.NotInstalled;
        }
    }

    public static bool IsAutostart()
    {
        using var s = new ServiceController(AppInfo.ServiceName);
        try { return s.StartType == ServiceStartMode.Automatic; }
        catch (InvalidOperationException) { return false; }
    }

    public static void Start()
    {
        using var s = new ServiceController(AppInfo.ServiceName);
        if (s.Status == ServiceControllerStatus.Running) return;
        s.Start();
        s.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
    }

    public static void Stop()
    {
        if (!IsInstalled) return;
        using var s = new ServiceController(AppInfo.ServiceName);
        if (s.Status == ServiceControllerStatus.Stopped) return;
        s.Stop();
        s.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
    }
}
