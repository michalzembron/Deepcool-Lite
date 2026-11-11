using System.Diagnostics;
using System.ServiceProcess;
using DeepCoolLite.Core;
using Microsoft.Win32;

namespace DeepCoolLite.Settings.Setup;

/// <summary>
/// Disables / restores the autostart of the official DeepCool app.
/// The per-user part (HKCU\...\Run, Startup folder shortcut) must run as the logged-in user without elevation;
/// the system part (Deep Cool Helper Service) requires admin.
/// </summary>
public static class OfficialDeepCool
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "DeepCool";
    private const string ProcessName = "DeepCool";
    public const string HelperServiceName = "Deep Cool Helper Service";

    private static string StartupLnk => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "DeepCool.lnk");
    private static string UserBackupFile => Path.Combine(AppInfo.UserDataDir, "deepcool-autostart.json");
    private static string UserBackupLnk => Path.Combine(AppInfo.UserDataDir, "DeepCool.lnk");
    public const string SystemBackupFileName = "deepcool-helper-backup.json";
    private static string SystemBackupFile => Path.Combine(AppInfo.DataDir, SystemBackupFileName);

    private sealed record UserBackup(string? RunCommand, bool HadStartupLnk);
    private sealed record SystemBackup(ServiceStartMode StartMode);

    /// <summary>Whether the official app is installed and may start automatically (or is running).</summary>
    public static bool IsActive()
    {
        using (var run = Registry.CurrentUser.OpenSubKey(RunKey))
            if (run?.GetValue(RunValue) is not null || File.Exists(StartupLnk)) return true;
        if (IsRunning()) return true;
        var mode = HelperStartMode();
        return mode is not null && mode != ServiceStartMode.Disabled;
    }

    public static bool IsRunning()
    {
        var processes = Process.GetProcessesByName(ProcessName);
        foreach (var p in processes) p.Dispose();
        return processes.Length > 0;
    }

    // ---------- per-user part (no admin) ----------

    public static void DisableUserAutostart()
    {
        using (var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
        {
            var cmd = run?.GetValue(RunValue) as string;
            bool hadLnk = File.Exists(StartupLnk);
            if (cmd is not null || hadLnk)
            {
                Directory.CreateDirectory(AppInfo.UserDataDir);
                if (!File.Exists(UserBackupFile)) JsonStore.Save(UserBackupFile, new UserBackup(cmd, hadLnk));
                if (cmd is not null) run!.DeleteValue(RunValue);
                if (hadLnk) File.Move(StartupLnk, UserBackupLnk, overwrite: true);
            }
        }
        KillProcesses();
    }

    public static void RestoreUserAutostart()
    {
        var b = JsonStore.Load<UserBackup>(UserBackupFile);
        if (b is null) return;

        if (b.RunCommand is not null)
        {
            using var run = Registry.CurrentUser.CreateSubKey(RunKey);
            run.SetValue(RunValue, b.RunCommand);
        }
        if (b.HadStartupLnk && File.Exists(UserBackupLnk)) File.Move(UserBackupLnk, StartupLnk, overwrite: true);
        File.Delete(UserBackupFile);
    }

    public static void KillProcesses()
    {
        foreach (var p in Process.GetProcessesByName(ProcessName))
        {
            try { p.Kill(entireProcessTree: true); } catch { /* already exited / access denied */ }
            p.Dispose();
        }
    }

    // ---------- system part (admin) ----------

    public static void DisableHelperService()
    {
        var mode = HelperStartMode();
        if (mode is null) return;

        if (!File.Exists(SystemBackupFile))
            JsonStore.Save(SystemBackupFile, new SystemBackup(mode.Value));

        using (var helper = new ServiceController(HelperServiceName))
        {
            if (helper.Status != ServiceControllerStatus.Stopped)
            {
                try { helper.Stop(); helper.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15)); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ServiceProcess.TimeoutException) { /* disable anyway */ }
            }
        }
        Sc.Run("config", $"\"{HelperServiceName}\"", "start=", "disabled");
        KillProcesses();
    }

    public static void RestoreHelperService()
    {
        var b = JsonStore.Load<SystemBackup>(SystemBackupFile);
        if (b is null || HelperStartMode() is null) return;

        Sc.Run("config", $"\"{HelperServiceName}\"", "start=", b.StartMode switch
        {
            ServiceStartMode.Automatic => "auto",
            ServiceStartMode.Disabled => "disabled",
            _ => "demand",
        });
        if (b.StartMode == ServiceStartMode.Automatic)
        {
            using var helper = new ServiceController(HelperServiceName);
            try { helper.Start(); } catch (InvalidOperationException) { /* starts on next boot */ }
        }
    }

    private static ServiceStartMode? HelperStartMode()
    {
        using var helper = new ServiceController(HelperServiceName);
        try { return helper.StartType; }
        catch (InvalidOperationException) { return null; }
    }
}
