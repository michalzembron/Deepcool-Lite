using DeepCoolLite.Core;
using DeepCoolLite.Service.Devices;

namespace DeepCoolLite.Tests;

public class Ak620ProProtocolTests
{
    private static byte[] Build(CpuReading reading, TemperatureUnit unit = TemperatureUnit.Celsius)
    {
        var report = new byte[64];
        new Ak620ProProtocol().BuildStatusReport(report, reading, unit);
        return report;
    }

    [Fact]
    public void Celsius_report_matches_reference_layout()
    {
        // 65 W, 45 C, 12 %, 4950 MHz
        var report = Build(new CpuReading(TempC: 45f, PowerW: 65f, LoadPct: 12f, ClockMHz: 4950f));

        byte[] expected =
        [
            16, 104, 1, 4, 13, 1, 2, 8, // report id + header
            0, 65,                      // power u16 BE
            0,                          // Celsius
            0x42, 0x34, 0x00, 0x00,     // 45.0f BE
            12,                         // usage
            0x13, 0x56,                 // 4950 u16 BE
            177,                        // (104+1+4+13+1+2+8 + 65 + 66+52 + 12 + 19+86) % 256
            22,                         // terminator
        ];
        Assert.Equal(expected, report[..20]);
        Assert.All(report[20..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void Fahrenheit_sets_unit_flag_and_converts()
    {
        var report = Build(new CpuReading(45f, 65f, 12f, 4950f), TemperatureUnit.Fahrenheit);

        Assert.Equal(1, report[10]);
        Assert.Equal(new byte[] { 0x42, 0xE2, 0x00, 0x00 }, report[11..15]); // 113.0f
    }

    [Theory]
    [InlineData(150f, 100)]
    [InlineData(-5f, 0)]
    [InlineData(float.NaN, 0)]
    [InlineData(49.6f, 50)]
    public void Usage_is_rounded_and_clamped(float load, byte expected) =>
        Assert.Equal(expected, Build(new CpuReading(40, 50, load, 4000))[15]);

    [Fact]
    public void Out_of_range_power_and_clock_are_clamped()
    {
        var report = Build(new CpuReading(40, -10, 0, 1e9f));

        Assert.Equal(new byte[] { 0, 0 }, report[8..10]);
        Assert.Equal(new byte[] { 0xFF, 0xFF }, report[16..18]);
    }

    [Fact]
    public void Checksum_is_sum_of_bytes_1_to_17_mod_256()
    {
        var report = Build(new CpuReading(87.4f, 142.6f, 99f, 5250f));

        int sum = 0;
        for (int i = 1; i <= 17; i++) sum += report[i];
        Assert.Equal((byte)(sum % 256), report[18]);
        Assert.Equal(22, report[19]);
    }

    [Fact]
    public void Too_short_report_is_rejected() =>
        Assert.Throws<ArgumentException>(() =>
            new Ak620ProProtocol().BuildStatusReport(new byte[19], default, TemperatureUnit.Celsius));

    [Fact]
    public void Registered_for_ak620_and_ak500_pro()
    {
        var protocol = DisplayProtocols.All.OfType<Ak620ProProtocol>().Single();
        Assert.Equal(0x3633, protocol.VendorId);
        Assert.Contains(0x0012, protocol.ProductIds);
        Assert.Contains(0x0011, protocol.ProductIds);
    }
}
