using System.Buffers.Binary;
using DeepCoolLite.Core;

namespace DeepCoolLite.Service.Devices;

/// <summary>
/// AK500 DIGITAL PRO (PID 0x0011) / AK620 DIGITAL PRO (PID 0x0012).
/// Packet layout from https://github.com/Nortank12/deepcool-digital-linux (device-list/tables/ak620-pro.md):
/// <code>
/// D0      report id (16)
/// D1-D7   fixed header 104,1,4,13,1,2,8
/// D8-D9   power [W], u16 BE
/// D10     temperature unit (0 = C, 1 = F)
/// D11-D14 temperature, f32 BE
/// D15     utilization [%]
/// D16-D17 frequency [MHz], u16 BE
/// D18     sum(D1..D17) mod 256
/// D19     terminator (22)
/// </code>
/// The device itself shows the warning (80 C) and limit (90 C) states.
/// </summary>
public sealed class Ak620ProProtocol : IDisplayProtocol
{
    private static ReadOnlySpan<byte> Header => [16, 104, 1, 4, 13, 1, 2, 8];

    public string Family => "AK500 / AK620 DIGITAL PRO";
    public int VendorId => DisplayProtocols.DeepCoolVendorId;
    public IReadOnlyList<int> ProductIds { get; } = [0x0012, 0x0011];
    public int MinReportLength => 20;

    public void BuildStatusReport(Span<byte> report, CpuReading reading, TemperatureUnit unit)
    {
        if (report.Length < MinReportLength)
            throw new ArgumentException($"Report must be at least {MinReportLength} bytes.", nameof(report));

        Header.CopyTo(report);

        BinaryPrimitives.WriteUInt16BigEndian(report[8..], ToUInt16(reading.PowerW));

        report[10] = unit == TemperatureUnit.Fahrenheit ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteSingleBigEndian(report[11..], RoundToInt(reading.Temperature(unit)));

        report[15] = (byte)Math.Clamp(RoundToInt(reading.LoadPct), 0, 100);

        BinaryPrimitives.WriteUInt16BigEndian(report[16..], ToUInt16(reading.ClockMHz));

        int sum = 0;
        for (int i = 1; i <= 17; i++) sum += report[i];
        report[18] = (byte)(sum % 256);
        report[19] = 22;
    }

    private static int RoundToInt(float v) => float.IsFinite(v) ? (int)MathF.Round(v) : 0;
    private static ushort ToUInt16(float v) => (ushort)Math.Clamp(RoundToInt(v), 0, ushort.MaxValue);
}
