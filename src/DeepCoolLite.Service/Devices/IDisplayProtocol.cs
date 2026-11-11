using DeepCoolLite.Core;

namespace DeepCoolLite.Service.Devices;

/// <summary>
/// Packet format of one family of DeepCool displays. Pure byte building - no I/O - so it can be unit tested.
/// To add a new model: implement this interface and register it in <see cref="DisplayProtocols.All"/>.
/// </summary>
public interface IDisplayProtocol
{
    string Family { get; }
    int VendorId { get; }
    IReadOnlyList<int> ProductIds { get; }

    /// <summary>Minimum HID output report length this protocol needs.</summary>
    int MinReportLength { get; }

    /// <summary>Writes the status packet into <paramref name="report"/> (already zeroed, full report length).</summary>
    void BuildStatusReport(Span<byte> report, CpuReading reading, TemperatureUnit unit);
}

public static class DisplayProtocols
{
    public const int DeepCoolVendorId = 0x3633;

    public static readonly IReadOnlyList<IDisplayProtocol> All =
    [
        new Ak620ProProtocol(),
    ];
}
