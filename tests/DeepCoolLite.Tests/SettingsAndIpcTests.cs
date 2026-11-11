using System.Text;
using DeepCoolLite.Core;

namespace DeepCoolLite.Tests;

public class LiteSettingsTests
{
    [Fact]
    public void Defaults_are_valid() => Assert.Equal(new LiteSettings(), new LiteSettings().Normalize());

    [Theory]
    [InlineData(0, LiteSettings.MinIntervalMs)]
    [InlineData(1_000_000, LiteSettings.MaxIntervalMs)]
    [InlineData(2000, 2000)]
    public void Interval_is_clamped(int requested, int expected) =>
        Assert.Equal(expected, new LiteSettings { UpdateIntervalMs = requested }.Normalize().UpdateIntervalMs);

    [Fact]
    public void Undefined_enum_values_fall_back_to_defaults()
    {
        var s = new LiteSettings { TemperatureUnit = (TemperatureUnit)42, ClockSource = (ClockSource)42 }.Normalize();
        Assert.Equal(TemperatureUnit.Celsius, s.TemperatureUnit);
        Assert.Equal(ClockSource.MaxCore, s.ClockSource);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_sensor_means_automatic(string name) =>
        Assert.Null(new LiteSettings { TemperatureSensor = name }.Normalize().TemperatureSensor);

    [Fact]
    public void Overlong_sensor_name_is_dropped() =>
        Assert.Null(new LiteSettings { TemperatureSensor = new string('x', 500) }.Normalize().TemperatureSensor);
}

public class IpcTests
{
    [Fact]
    public async Task Request_round_trips()
    {
        var request = new IpcRequest(IpcCommand.SetSettings,
            new LiteSettings { TemperatureUnit = TemperatureUnit.Fahrenheit, UpdateIntervalMs = 2000, TemperatureSensor = "CCD1 (Tdie)" });

        using var stream = new MemoryStream();
        await Ipc.WriteAsync(stream, request, CancellationToken.None);
        stream.Position = 0;

        Assert.Equal(request, await Ipc.ReadAsync<IpcRequest>(stream, CancellationToken.None));
    }

    [Fact]
    public async Task Status_round_trips()
    {
        var status = new ServiceStatus
        {
            Settings = new LiteSettings(),
            CpuName = "AMD Ryzen 7 7800X3D",
            TemperatureSensors = ["Core (Tctl/Tdie)", "CCD1 (Tdie)"],
            DisplayConnected = true,
            DisplayName = "AK620-DIGITAL-PRO",
            LastReading = new CpuReading(45, 65, 12, 4950),
            Warnings = ["test"],
        };

        using var stream = new MemoryStream();
        await Ipc.WriteAsync(stream, status, CancellationToken.None);
        stream.Position = 0;
        var read = await Ipc.ReadAsync<ServiceStatus>(stream, CancellationToken.None);

        Assert.NotNull(read);
        Assert.Equal(status.CpuName, read.CpuName);
        Assert.Equal(status.TemperatureSensors, read.TemperatureSensors);
        Assert.Equal(status.LastReading, read.LastReading);
        Assert.Equal(status.Warnings, read.Warnings);
    }

    [Fact]
    public async Task Only_first_line_is_read()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"Command\":\"GetStatus\"}\n{garbage"));
        var request = await Ipc.ReadAsync<IpcRequest>(stream, CancellationToken.None);
        Assert.Equal(IpcCommand.GetStatus, request!.Command);
    }

    [Fact]
    public async Task Oversized_message_is_rejected()
    {
        using var stream = new MemoryStream(new byte[Ipc.MaxMessageBytes + 10]);
        await Assert.ThrowsAsync<InvalidDataException>(() => Ipc.ReadAsync<IpcRequest>(stream, CancellationToken.None));
    }

    [Fact]
    public async Task Empty_stream_returns_null()
    {
        using var stream = new MemoryStream();
        Assert.Null(await Ipc.ReadAsync<IpcRequest>(stream, CancellationToken.None));
    }
}
