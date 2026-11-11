using System.Diagnostics;
using System.IO.Compression;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using DeepCoolLite.Core;

namespace DeepCoolLite.Settings.Setup;

/// <summary>
/// Operations that run in an elevated copy of the settings app.
/// Usage: DeepCoolLite.exe --install [--disable-deepcool] [--desktop-shortcut] | --uninstall | --autostart on|off
/// </summary>
public static class Installer
{
    public const string InstallArg = "--install";
    public const string DisableDeepCoolArg = "--disable-deepcool";
    public const string DesktopShortcutArg = "--desktop-shortcut";
    public const string UninstallArg = "--uninstall";
    public const string AutostartArg = "--autostart";

    // Default service permissions + start/stop (RP/WP) for interactive users (IU), so the settings app can
    // start/stop the service without UAC. Changing the service configuration still requires admin.
    private const string ServiceSddl =
        "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWRPWPLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)";

    /// <summary>Our own small files in the data folder, kept across reinstalls.</summary>
    private static readonly string[] KeptDataFiles = [Path.GetFileName(AppInfo.SettingsFile), OfficialDeepCool.SystemBackupFileName];

    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);

    /// <summary>The service exe (single file), zipped and embedded by dotnet publish. Absent in development builds.</summary>
    private const string ServicePayloadResource = "ServicePayload.zip";

    private static string StartMenuShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), AppInfo.ProductName + ".lnk");

    private static string DesktopShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), AppInfo.ProductName + ".lnk");

    /// <summary>Runs one elevated-mode operation. Returns the process exit code.</summary>
    public static int Run(string[] args)
    {
        switch (args[0])
        {
            case InstallArg: Install(args.Contains(DisableDeepCoolArg), args.Contains(DesktopShortcutArg)); return 0;
            case UninstallArg: Uninstall(); return 0;
            case AutostartArg: SetAutostart(args.ElementAtOrDefault(1) == "on"); return 0;
            default: return 2;
        }
    }

    private static void Install(bool disableDeepCool, bool desktopShortcut)
    {
        using var payload = typeof(Installer).Assembly.GetManifestResourceStream(ServicePayloadResource)
            ?? throw new InvalidOperationException(
                "This is a development build without the embedded service. Install from a published DeepCoolLite.exe (build/publish.ps1).");

        // 1. Program files -> Program Files (not writable by regular users)
        StopServiceIfExists();
        var target = AppInfo.InstallDir;
        var self = Environment.ProcessPath!;
        var settingsExe = Path.Combine(target, AppInfo.SettingsExe);
        Directory.CreateDirectory(target);
        RemoveOldFiles(target, keep: self);
        if (!self.Equals(settingsExe, StringComparison.OrdinalIgnoreCase))
            File.Copy(self, settingsExe, overwrite: true);
        ExtractPayload(payload, target);

        // 2. Data folder writable only by SYSTEM/Administrators
        PrepareDataDir();

        // 3. Service registration
        var exe = Path.Combine(target, AppInfo.ServiceExe);
        var binPath = $"\"\\\"{exe}\\\"\"";
        if (!ServiceControl.IsInstalled)
            Sc.Run("create", AppInfo.ServiceName, "binPath=", binPath, "start=", "auto", "DisplayName=", $"\"{AppInfo.ServiceDisplayName}\"");
        else
            Sc.Run("config", AppInfo.ServiceName, "binPath=", binPath);
        Sc.Run("description", AppInfo.ServiceName, $"\"{AppInfo.ServiceDescription}\"");
        Sc.Run("failure", AppInfo.ServiceName, "reset=", "86400", "actions=", "restart/5000/restart/5000/restart/30000");
        Sc.Run("sdset", AppInfo.ServiceName, ServiceSddl);

        // 4. Shortcuts (all users)
        CreateShortcut(StartMenuShortcut, settingsExe);
        if (desktopShortcut) CreateShortcut(DesktopShortcut, settingsExe);

        // 5. Official app - system part (the per-user part is done by the non-elevated process)
        if (disableDeepCool) OfficialDeepCool.DisableHelperService();

        // 6. Start
        ServiceControl.Start();
    }

    private static void Uninstall()
    {
        StopServiceIfExists();
        if (ServiceControl.IsInstalled) Sc.Run("delete", AppInfo.ServiceName);

        OfficialDeepCool.RestoreHelperService();
        File.Delete(StartMenuShortcut);
        File.Delete(DesktopShortcut);
        DeleteDirectory(AppInfo.DataDir);

        // The settings app may still be running from the install folder (e.g. showing the "uninstalled" message),
        // so a detached cmd retries every 2 s for up to 10 minutes until the folder is gone.
        if (Directory.Exists(AppInfo.InstallDir))
        {
            var dir = AppInfo.InstallDir;
            Process.Start(new ProcessStartInfo("cmd.exe",
                $"/c for /l %i in (1,1,300) do (rmdir /s /q \"{dir}\" 2>nul & if not exist \"{dir}\" (exit /b) else (ping -n 3 127.0.0.1 >nul))")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
    }

    /// <summary>
    /// Removes files of the previous version (older releases had ~50 files; names may change between versions).
    /// Best effort: a file still in use (e.g. an old settings window) is simply left behind.
    /// </summary>
    private static void RemoveOldFiles(string dir, string keep)
    {
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            if (file.Equals(keep, StringComparison.OrdinalIgnoreCase)) continue;
            try { File.Delete(file); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static void ExtractPayload(Stream payload, string targetDir)
    {
        using var zip = new ZipArchive(payload, ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            // Flat list of files only - never write outside the install folder
            if (entry.FullName != Path.GetFileName(entry.FullName))
                throw new InvalidDataException($"Unexpected path in the service package: {entry.FullName}");
            entry.ExtractToFile(Path.Combine(targetDir, entry.Name), overwrite: true);
        }
    }

    private static void SetAutostart(bool on) =>
        Sc.Run("config", AppInfo.ServiceName, "start=", on ? "auto" : "demand");

    /// <summary>
    /// Recreates %ProgramData%\DeepCool Lite owned by Administrators with an explicit ACL
    /// (SYSTEM/Administrators: full, Users: read). The folder might have been pre-created by a regular user
    /// (ProgramData lets users create folders) or replaced by a junction - in both cases the service (SYSTEM)
    /// must not write into it.
    /// </summary>
    private static void PrepareDataDir()
    {
        var dir = new DirectoryInfo(AppInfo.DataDir);

        // Content of the kept files is only ever parsed as JSON by this app
        var keep = new Dictionary<string, byte[]>();
        if (dir.Exists)
        {
            if (!dir.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                foreach (var name in KeptDataFiles)
                {
                    var file = new FileInfo(Path.Combine(dir.FullName, name));
                    if (file.Exists && !file.Attributes.HasFlag(FileAttributes.ReparsePoint) && file.Length <= Ipc.MaxMessageBytes)
                        keep[name] = File.ReadAllBytes(file.FullName);
                }
            }
            DeleteDirectory(dir.FullName);
        }

        var security = new DirectorySecurity();
        security.SetOwner(Administrators);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(Administrators,
            FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));

        // Create() silently does nothing if the folder already exists again (someone re-created it after the delete),
        // so verify we really got our own, empty, non-junction folder.
        dir.Create(security);
        dir.Refresh();
        if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
            !Administrators.Equals(dir.GetAccessControl().GetOwner(typeof(SecurityIdentifier))) ||
            dir.EnumerateFileSystemInfos().Any())
            throw new IOException($"{dir.FullName} was modified by another process during installation. Please try again.");

        foreach (var (name, content) in keep)
            File.WriteAllBytes(Path.Combine(dir.FullName, name), content);
    }

    /// <summary>Deletes a folder; a junction/symlink is removed itself, never followed.</summary>
    private static void DeleteDirectory(string path)
    {
        var dir = new DirectoryInfo(path);
        if (!dir.Exists) return;
        if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint)) dir.Delete();
        else dir.Delete(recursive: true);
    }

    private static void StopServiceIfExists()
    {
        try { ServiceControl.Stop(); }
        catch (System.ServiceProcess.TimeoutException) { /* continue anyway */ }
    }

    private static void CreateShortcut(string lnkPath, string targetExe)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) return;
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic lnk = shell.CreateShortcut(lnkPath);
        lnk.TargetPath = targetExe;
        lnk.WorkingDirectory = Path.GetDirectoryName(targetExe);
        lnk.Description = AppInfo.ProductName;
        lnk.Save();
    }
}
