using DeepCoolLite.Core;
using HidSharp;

namespace DeepCoolLite.Service.Devices;

/// <summary>Finds the first supported display and sends reports to it over HID.</summary>
public sealed class DisplayConnection : IDisposable
{
    private HidStream? _stream;
    private IDisplayProtocol? _protocol;
    private byte[] _report = [];

    public bool IsOpen => _stream is not null;
    public string? DeviceName { get; private set; }

    public bool TryOpen(ILogger logger)
    {
        foreach (var protocol in DisplayProtocols.All)
        foreach (var pid in protocol.ProductIds)
        {
            var device = DeviceList.Local.GetHidDevices(protocol.VendorId, pid)
                .FirstOrDefault(d => d.GetMaxOutputReportLength() >= protocol.MinReportLength);
            if (device is null || !device.TryOpen(out var stream)) continue;

            stream.WriteTimeout = 1000;
            _stream = stream;
            _protocol = protocol;
            _report = new byte[device.GetMaxOutputReportLength()];
            DeviceName = SafeProductName(device) ?? protocol.Family;

            logger.LogInformation("Connected to {Device} (PID 0x{Pid:X4}, {Family}, report {Length} B)",
                DeviceName, pid, protocol.Family, _report.Length);
            return true;
        }
        return false;
    }

    public void Send(CpuReading reading, TemperatureUnit unit)
    {
        if (_stream is null || _protocol is null) throw new InvalidOperationException("Display is not open.");
        Array.Clear(_report);
        _protocol.BuildStatusReport(_report, reading, unit);
        _stream.Write(_report);
    }

    public void Close()
    {
        _stream?.Dispose();
        _stream = null;
        _protocol = null;
        DeviceName = null;
    }

    public void Dispose() => Close();

    private static string? SafeProductName(HidDevice device)
    {
        try { return device.GetProductName(); }
        catch (IOException) { return null; }
    }
}
