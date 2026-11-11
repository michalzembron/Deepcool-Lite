using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;
using DeepCoolLite.Core;
using DeepCoolLite.Settings.Setup;

namespace DeepCoolLite.Settings;

public partial class MainWindow : Window
{
    private static readonly (int Ms, string Label)[] Intervals =
        [(500, "0.5 s"), (1000, "1 s"), (2000, "2 s"), (3000, "3 s"), (5000, "5 s")];

    private const string OfficialAppWarning =
        "The official DeepCool app is running – both apps send data to the display, so values may flicker.";

    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private LiteSettings? _settings;       // last settings confirmed by the service
    private string[] _shownSensors = [];
    private bool _loading;                 // suppresses Setting_Changed while controls are filled in code
    private bool _busy;                    // an install/start/stop/... operation is in progress
    private bool _refreshing;

    public MainWindow()
    {
        InitializeComponent();
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();

        _loading = true;
        UnitCombo.Items.Add(new ComboBoxItem { Content = "Celsius (°C)", Tag = TemperatureUnit.Celsius });
        UnitCombo.Items.Add(new ComboBoxItem { Content = "Fahrenheit (°F)", Tag = TemperatureUnit.Fahrenheit });
        foreach (var (ms, label) in Intervals) IntervalCombo.Items.Add(new ComboBoxItem { Content = label, Tag = ms });
        ClockCombo.Items.Add(new ComboBoxItem { Content = "Fastest core", Tag = ClockSource.MaxCore });
        ClockCombo.Items.Add(new ComboBoxItem { Content = "Average of all cores", Tag = ClockSource.Average });
        _loading = false;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
        ShowUpdateCard();
        _refreshTimer.Start();
        if (!ServiceControl.IsInstalled) await InstallAsync();
    }

    private void Window_Closed(object? sender, EventArgs e) => _refreshTimer.Stop();

    // ---------------- status ----------------

    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var state = ServiceControl.GetState();
            var status = state == LiteServiceState.Running ? await ServiceClient.GetStatusAsync() : null;
            ShowServiceState(state);
            ShowStatus(status);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void ShowServiceState(LiteServiceState state)
    {
        // Theme brushes (DynamicResource) so the dot follows light/dark mode
        (StatusText.Text, var dotBrush) = state switch
        {
            LiteServiceState.Running => ("Running", "SystemFillColorSuccessBrush"),
            LiteServiceState.Stopped => ("Stopped", "SystemFillColorCriticalBrush"),
            LiteServiceState.Pending => ("Starting / stopping…", "SystemFillColorCautionBrush"),
            _ => ("Not installed", "SystemFillColorNeutralBrush"),
        };
        StatusDot.SetResourceReference(Shape.FillProperty, dotBrush);

        bool installed = state != LiteServiceState.NotInstalled;
        InstallButton.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
        StartButton.Visibility = StopButton.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        InstallButton.IsEnabled = !_busy;
        StartButton.IsEnabled = !_busy && state == LiteServiceState.Stopped;
        StopButton.IsEnabled = !_busy && state == LiteServiceState.Running;
        AutostartCheck.IsEnabled = !_busy && installed;
        AutostartCheck.IsChecked = installed && ServiceControl.IsAutostart();
        UninstallButton.IsEnabled = !_busy && installed;
        UpdateButton.IsEnabled = !_busy;
    }

    /// <summary>
    /// Offers to copy this build over the installed one (settings are kept by the installer).
    /// Shown only when the service is installed and this window runs from somewhere else than Program Files.
    /// </summary>
    private void ShowUpdateCard()
    {
        if (!ServiceControl.IsInstalled || InstalledCopy.IsRunningFromInstallDir)
        {
            UpdateCard.Visibility = Visibility.Collapsed;
            return;
        }

        var current = InstalledCopy.CurrentVersion;
        var installed = InstalledCopy.InstalledVersion;
        (UpdateTitle.Text, UpdateDescription.Text, UpdateButton.Content, bool recommended) = (installed, current) switch
        {
            (null, _) => ("Repair installation", "The installed program files are missing. Settings are kept.", "Repair", true),
            _ when current > installed => ("Update installed version",
                $"Installed: {installed}  →  this copy: {current}. Settings are kept.", "Update", true),
            _ when current == installed => ("Reinstall from this copy",
                $"The installed version is the same ({installed}). Settings are kept.", "Reinstall", false),
            _ => ("Replace with an older version",
                $"Installed: {installed}, this copy: {current} (older). Settings are kept.", "Replace", false),
        };

        if (recommended) UpdateButton.SetResourceReference(StyleProperty, "AccentButtonStyle");
        else UpdateButton.ClearValue(StyleProperty);
        UpdateCard.Visibility = Visibility.Visible;
    }

    private void ShowStatus(ServiceStatus? status)
    {
        bool connected = status is not null;
        SettingsPanel.IsEnabled = connected && !_busy;
        SettingsHint.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;

        DisplayRun.Text = status switch
        {
            null => "Display: –",
            { DisplayConnected: true } => $"{status.DisplayName}",
            _ => "Display not found",
        };
        CpuRun.Text = status?.CpuName ?? "CPU: –";
        ShowReading(status?.LastReading, status?.Settings.TemperatureUnit ?? TemperatureUnit.Celsius);

        var warnings = status?.Warnings.ToList() ?? [];
        if (status is not null && OfficialDeepCool.IsRunning()) warnings.Add(OfficialAppWarning);
        WarningText.Text = string.Join(Environment.NewLine, warnings);
        WarningBar.Visibility = warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (status is not null) ShowSettings(status);
    }

    private void ShowReading(CpuReading? reading, TemperatureUnit unit)
    {
        TempUnit.Text = unit == TemperatureUnit.Fahrenheit ? "°F" : "°C";
        if (reading is not { } r)
        {
            TempValue.Text = PowerValue.Text = LoadValue.Text = ClockValue.Text = "–";
            return;
        }
        TempValue.Text = r.Temperature(unit).ToString("F0");
        PowerValue.Text = r.PowerW.ToString("F0");
        LoadValue.Text = r.LoadPct.ToString("F0");
        ClockValue.Text = r.ClockMHz.ToString("F0");
    }

    // ---------------- settings ----------------

    private void ShowSettings(ServiceStatus status)
    {
        var settings = status.Settings;
        bool sensorsChanged = !status.TemperatureSensors.SequenceEqual(_shownSensors);
        if (settings == _settings && !sensorsChanged) return;

        _loading = true;
        _settings = settings;

        SelectByTag(UnitCombo, settings.TemperatureUnit);

        if (!Intervals.Any(i => i.Ms == settings.UpdateIntervalMs) &&
            !IntervalCombo.Items.Cast<ComboBoxItem>().Any(i => Equals(i.Tag, settings.UpdateIntervalMs)))
            IntervalCombo.Items.Add(new ComboBoxItem { Content = $"{settings.UpdateIntervalMs} ms", Tag = settings.UpdateIntervalMs });
        SelectByTag(IntervalCombo, settings.UpdateIntervalMs);
        SelectByTag(ClockCombo, settings.ClockSource);

        if (sensorsChanged || SensorCombo.Items.Count == 0)
        {
            _shownSensors = status.TemperatureSensors;
            SensorCombo.Items.Clear();
            var auto = status.DefaultTemperatureSensor is { } d ? $"Automatic ({d})" : "Automatic";
            SensorCombo.Items.Add(new ComboBoxItem { Content = auto, Tag = null });
            foreach (var n in status.TemperatureSensors) SensorCombo.Items.Add(new ComboBoxItem { Content = n, Tag = n });
        }
        SelectByTag(SensorCombo, settings.TemperatureSensor);

        _loading = false;
    }

    private async void Setting_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsInitialized || _settings is null) return;

        var requested = _settings with
        {
            TemperatureUnit = (UnitCombo.SelectedItem as ComboBoxItem)?.Tag as TemperatureUnit? ?? _settings.TemperatureUnit,
            UpdateIntervalMs = (IntervalCombo.SelectedItem as ComboBoxItem)?.Tag as int? ?? _settings.UpdateIntervalMs,
            ClockSource = (ClockCombo.SelectedItem as ComboBoxItem)?.Tag as ClockSource? ?? _settings.ClockSource,
            TemperatureSensor = (SensorCombo.SelectedItem as ComboBoxItem)?.Tag as string,
        };
        if (requested == _settings) return;

        var status = await ServiceClient.SetSettingsAsync(requested);
        if (status is null)
        {
            ShowError("Could not reach the service. Is it running?");
            _settings = null; // force controls to be reloaded from the service
            await RefreshAsync();
            return;
        }
        ShowStatus(status); // the service may have adjusted the values (Normalize)
    }

    private static void SelectByTag(ComboBox combo, object? tag) =>
        combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, tag)) ?? combo.Items[0];

    // ---------------- actions ----------------

    private async Task RunBusyAsync(Func<Task> action)
    {
        _busy = true;
        await RefreshAsync();
        try { await action(); }
        catch (Exception ex) { ShowError(ex.Message); }
        finally
        {
            _busy = false;
            await RefreshAsync();
            ShowUpdateCard();
        }
    }

    private async void Install_Click(object sender, RoutedEventArgs e) => await InstallAsync();

    private async Task InstallAsync()
    {
        var dialog = new InstallDialog(OfficialDeepCool.IsActive()) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        bool disableOfficial = dialog.DisableOfficialApp;
        List<string> args = [Installer.InstallArg];
        if (disableOfficial) args.Add(Installer.DisableDeepCoolArg);
        if (dialog.CreateDesktopShortcut) args.Add(Installer.DesktopShortcutArg);

        bool done = false;
        await RunBusyAsync(async () =>
        {
            if (await Elevation.RunAsync([.. args]) != ElevatedResult.Success) return; // UAC declined, or the elevated process showed the error

            // Per-user part (HKCU, Startup folder) - must run as the current user, not elevated
            if (disableOfficial) OfficialDeepCool.DisableUserAutostart();
            done = true;
        });

        if (done) SwitchToInstalledCopy();
    }

    /// <summary>A downloaded copy hands over to the installed one, so the Downloads file can be deleted.</summary>
    private void SwitchToInstalledCopy()
    {
        if (!InstalledCopy.IsRunningFromInstallDir) ((App)Application.Current).SwitchToInstalledCopy();
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        var current = InstalledCopy.CurrentVersion;
        if (current < InstalledCopy.InstalledVersion &&
            MessageBox.Show(this, $"This copy ({current}) is older than the installed version ({InstalledCopy.InstalledVersion}). Replace it anyway?",
                "DeepCool Lite", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        bool done = false;
        // Same elevated operation as a fresh install: stop service, copy files, keep settings, start service
        await RunBusyAsync(async () => done = await Elevation.RunAsync(Installer.InstallArg) == ElevatedResult.Success);

        if (done) SwitchToInstalledCopy();
    }

    private async void Start_Click(object sender, RoutedEventArgs e) =>
        await RunBusyAsync(() => Task.Run(ServiceControl.Start));

    private async void Stop_Click(object sender, RoutedEventArgs e) =>
        await RunBusyAsync(() => Task.Run(ServiceControl.Stop));

    private async void Autostart_Click(object sender, RoutedEventArgs e)
    {
        bool on = AutostartCheck.IsChecked == true;
        await RunBusyAsync(() => Elevation.RunAsync(Installer.AutostartArg, on ? "on" : "off"));
    }

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this,
            "Uninstall DeepCool Lite?\n\nThe official DeepCool app autostart will be restored (if DeepCool Lite disabled it).",
            "DeepCool Lite", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        bool done = false;
        await RunBusyAsync(async () =>
        {
            if (await Elevation.RunAsync(Installer.UninstallArg) != ElevatedResult.Success) return;
            OfficialDeepCool.RestoreUserAutostart();
            done = true;
        });

        if (done)
        {
            MessageBox.Show(this, "DeepCool Lite has been uninstalled.", "DeepCool Lite", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
    }

    private void ShowError(string message) =>
        MessageBox.Show(this, message, "DeepCool Lite - error", MessageBoxButton.OK, MessageBoxImage.Error);
}
