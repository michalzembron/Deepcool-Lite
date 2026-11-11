using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepCoolLite.Core;

/// <summary>
/// Protocol between the settings app and the service over the named pipe <see cref="AppInfo.PipeName"/>.
/// One request per connection: the client writes one JSON line, the service answers with one JSON line.
/// </summary>
public static class Ipc
{
    public const int MaxMessageBytes = 64 * 1024;
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    private const byte NewLine = (byte)'\n';

    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task WriteAsync<T>(Stream stream, T message, CancellationToken ct)
    {
        var buffer = new ArrayBufferWriter<byte>(1024);
        using (var json = new Utf8JsonWriter(buffer))
            JsonSerializer.Serialize(json, message, Options);
        buffer.Write([NewLine]);

        await stream.WriteAsync(buffer.WrittenMemory, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Reads a single line, refusing anything longer than <see cref="MaxMessageBytes"/>.</summary>
    public static async Task<T?> ReadAsync<T>(Stream stream, CancellationToken ct) where T : class
    {
        var buffer = ArrayPool<byte>.Shared.Rent(MaxMessageBytes);
        try
        {
            int length = 0;
            while (true)
            {
                if (length == MaxMessageBytes) throw new InvalidDataException("IPC message too large.");
                int read = await stream.ReadAsync(buffer.AsMemory(length, MaxMessageBytes - length), ct).ConfigureAwait(false);
                if (read == 0) break;

                int newLine = Array.IndexOf(buffer, NewLine, length, read);
                length += read;
                if (newLine >= 0) { length = newLine; break; }
            }
            return length == 0 ? null : JsonSerializer.Deserialize<T>(buffer.AsSpan(0, length), Options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

public enum IpcCommand { GetStatus, SetSettings }

public sealed record IpcRequest(IpcCommand Command, LiteSettings? Settings = null);

/// <summary>Everything the settings window shows; returned for every request.</summary>
public sealed record ServiceStatus
{
    public required LiteSettings Settings { get; init; }
    public string? CpuName { get; init; }
    public string[] TemperatureSensors { get; init; } = [];
    public string? DefaultTemperatureSensor { get; init; }
    public string? DisplayName { get; init; }
    public bool DisplayConnected { get; init; }
    public CpuReading? LastReading { get; init; }
    public string[] Warnings { get; init; } = [];
}
