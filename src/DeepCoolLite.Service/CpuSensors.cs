using DeepCoolLite.Core;
using LibreHardwareMonitor.Hardware;

namespace DeepCoolLite.Service;

/// <summary>CPU sensors via LibreHardwareMonitor (CPU only, everything else disabled).</summary>
public sealed class CpuSensors : IDisposable
{
    private readonly Computer _computer = new() { IsCpuEnabled = true };
    private readonly IHardware _cpu;
    private readonly ISensor[] _temps;
    private readonly ISensor? _defaultTemp, _power, _load;
    private readonly ISensor[] _clocks;

    public CpuSensors(ILogger logger)
    {
        _computer.Open();
        _cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu)
               ?? throw new InvalidOperationException("No CPU found by LibreHardwareMonitor.");
        _cpu.Update();

        var sensors = _cpu.Sensors;
        _temps = sensors.Where(s => s.SensorType == SensorType.Temperature).ToArray();
        _defaultTemp = _temps.FirstOrDefault(s => s.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase))
                       ?? _temps.FirstOrDefault(s => s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))
                       ?? _temps.FirstOrDefault();
        _power = sensors.FirstOrDefault(s => s.SensorType == SensorType.Power && s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))
                 ?? sensors.FirstOrDefault(s => s.SensorType == SensorType.Power);
        _load = sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name == "CPU Total");

        // "Core #N" - skip the "(Effective)" variants when both are present
        var coreClocks = sensors.Where(s => s.SensorType == SensorType.Clock && s.Name.StartsWith("Core #", StringComparison.Ordinal)).ToArray();
        var plain = coreClocks.Where(s => !s.Name.Contains('(')).ToArray();
        _clocks = plain.Length > 0 ? plain : coreClocks;

        logger.LogInformation("CPU: {Name} | temperature: {Temp} | power: {Power} | load: {Load} | clocks: {Clocks}",
            _cpu.Name, _defaultTemp?.Name ?? "n/a", _power?.Name ?? "n/a", _load?.Name ?? "n/a",
            string.Join(", ", _clocks.Select(c => c.Name)));
    }

    public string CpuName => _cpu.Name;
    public string[] TemperatureSensorNames => _temps.Select(t => t.Name).ToArray();
    public string? DefaultTemperatureSensor => _defaultTemp?.Name;

    public CpuReading Read(LiteSettings settings)
    {
        _cpu.Update();

        var tempSensor = settings.TemperatureSensor is { } name
            ? _temps.FirstOrDefault(t => t.Name == name) ?? _defaultTemp
            : _defaultTemp;

        float clock = 0;
        int count = 0;
        foreach (var c in _clocks)
        {
            if (c.Value is not float v || v <= 0) continue;
            count++;
            clock = settings.ClockSource == ClockSource.MaxCore ? Math.Max(clock, v) : clock + v;
        }
        if (settings.ClockSource == ClockSource.Average && count > 0) clock /= count;

        return new CpuReading(
            tempSensor?.Value ?? 0,
            _power?.Value ?? 0,
            _load?.Value ?? 0,
            clock);
    }

    public void Dispose() => _computer.Close();
}
