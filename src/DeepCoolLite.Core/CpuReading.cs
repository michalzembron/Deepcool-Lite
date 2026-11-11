namespace DeepCoolLite.Core;

/// <summary>One CPU sample, always in Celsius / W / % / MHz.</summary>
public readonly record struct CpuReading(float TempC, float PowerW, float LoadPct, float ClockMHz)
{
    public float Temperature(TemperatureUnit unit) =>
        unit == TemperatureUnit.Fahrenheit ? TempC * 9 / 5 + 32 : TempC;

    /// <summary>True when the privileged sensors (temperature, power, clock) returned nothing.</summary>
    public bool IsIncomplete => TempC <= 0 || PowerW <= 0 || ClockMHz <= 0;
}
