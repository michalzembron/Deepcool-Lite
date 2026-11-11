using DeepCoolLite.Core;
using DeepCoolLite.Service.Devices;

namespace DeepCoolLite.Service;

/// <summary>Display loop: read CPU sensors, send them to the cooler display, publish status for the settings app.</summary>
public sealed class Worker(ServiceState state, ILogger<Worker> logger, IConfiguration config) : BackgroundService
{
    private const string NoDisplayWarning = "No supported DeepCool display found. Retrying in the background.";
    private const string SensorsWarning =
        "Temperature, power or clock is unavailable. The service must run as SYSTEM and the PawnIO driver must be installed.";

    /// <summary>Looking for the display enumerates all HID devices - don't do it on every tick.</summary>
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Opening LibreHardwareMonitor takes a moment - don't block host startup (and the pipe server) on it
        await Task.Yield();
        bool logReadings = config.GetValue<bool>("LogReadings");

        using var sensors = new CpuSensors(logger);
        state.Update(s => s with
        {
            CpuName = sensors.CpuName,
            TemperatureSensors = sensors.TemperatureSensorNames,
            DefaultTemperatureSensor = sensors.DefaultTemperatureSensor,
        });

        using var display = new DisplayConnection();
        var interval = state.Settings.UpdateIntervalMs;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(interval));
        var nextConnectAttempt = DateTime.MinValue;
        bool loggedMissing = false;

        do
        {
            var settings = state.Settings;
            if (settings.UpdateIntervalMs != interval)
            {
                interval = settings.UpdateIntervalMs;
                timer.Period = TimeSpan.FromMilliseconds(interval);
            }

            var reading = sensors.Read(settings);

            if (!display.IsOpen && DateTime.UtcNow >= nextConnectAttempt)
            {
                if (display.TryOpen(logger)) loggedMissing = false;
                else
                {
                    nextConnectAttempt = DateTime.UtcNow + ReconnectDelay;
                    if (!loggedMissing) logger.LogWarning(NoDisplayWarning);
                    loggedMissing = true;
                }
            }

            if (display.IsOpen)
            {
                try
                {
                    display.Send(reading, settings.TemperatureUnit);
                }
                catch (Exception ex) when (ex is IOException or TimeoutException or ObjectDisposedException)
                {
                    // USB unplugged, resume from sleep, ... - reconnect on the next tick
                    logger.LogWarning("Lost connection to the display: {Message}", ex.Message);
                    display.Close();
                }
            }

            if (logReadings)
                logger.LogInformation("{Temp:F1} C | {Power:F1} W | {Load:F0} % | {Clock:F0} MHz",
                    reading.TempC, reading.PowerW, reading.LoadPct, reading.ClockMHz);

            string[] warnings = (reading.IsIncomplete, display.IsOpen) switch
            {
                (true, true) => [SensorsWarning],
                (true, false) => [SensorsWarning, NoDisplayWarning],
                (false, false) => [NoDisplayWarning],
                _ => [],
            };
            state.Update(s => s with
            {
                LastReading = reading,
                DisplayConnected = display.IsOpen,
                DisplayName = display.DeviceName,
                Warnings = warnings,
            });
        }
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
    }
}
