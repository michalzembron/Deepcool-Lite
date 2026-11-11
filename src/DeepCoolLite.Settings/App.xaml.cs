using System.Diagnostics;
using System.Windows;
using DeepCoolLite.Core;
using DeepCoolLite.Settings.Setup;

namespace DeepCoolLite.Settings;

public partial class App : Application
{
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Never die silently - show what went wrong
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.ToString(), "DeepCool Lite - unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
            Shutdown(1);
        };

        // Elevated mode: perform one operation without a window and exit
        if (e.Args.Length > 0)
        {
            try
            {
                Shutdown(Installer.Run(e.Args));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "DeepCool Lite - error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
            return;
        }

        _singleInstance = new Mutex(true, @"Local\DeepCoolLite.Settings", out bool isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        ThemeMode = ThemeMode.System; // Fluent (Windows 11): Mica, light/dark following the system, accent colour
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    /// <summary>Closes this window and opens the copy installed in Program Files (after install / update).</summary>
    public void SwitchToInstalledCopy()
    {
        // Let the installed copy pass the single-instance check
        _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        _singleInstance = null;
        Process.Start(new ProcessStartInfo(Path.Combine(AppInfo.InstallDir, AppInfo.SettingsExe)) { UseShellExecute = true });
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
