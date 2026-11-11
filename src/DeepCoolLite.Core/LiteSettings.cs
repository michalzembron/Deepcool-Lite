namespace DeepCoolLite.Core;

public enum TemperatureUnit { Celsius, Fahrenheit }

public enum ClockSource
{
    /// <summary>Highest clock among all cores.</summary>
    MaxCore,
    /// <summary>Average clock of all cores.</summary>
    Average,
}

public sealed record LiteSettings
{
    public const int MinIntervalMs = 250;
    public const int MaxIntervalMs = 10_000;
    public const int MaxSensorNameLength = 128;

    public TemperatureUnit TemperatureUnit { get; init; } = TemperatureUnit.Celsius;
    public int UpdateIntervalMs { get; init; } = 1000;
    public ClockSource ClockSource { get; init; } = ClockSource.MaxCore;

    /// <summary>LibreHardwareMonitor temperature sensor name; null = automatic.</summary>
    public string? TemperatureSensor { get; init; }

    /// <summary>Returns a copy with every value in its valid range (input comes from an untrusted client).</summary>
    public LiteSettings Normalize() => this with
    {
        TemperatureUnit = Enum.IsDefined(TemperatureUnit) ? TemperatureUnit : TemperatureUnit.Celsius,
        UpdateIntervalMs = Math.Clamp(UpdateIntervalMs, MinIntervalMs, MaxIntervalMs),
        ClockSource = Enum.IsDefined(ClockSource) ? ClockSource : ClockSource.MaxCore,
        TemperatureSensor = string.IsNullOrWhiteSpace(TemperatureSensor) || TemperatureSensor.Length > MaxSensorNameLength
            ? null
            : TemperatureSensor,
    };
}
