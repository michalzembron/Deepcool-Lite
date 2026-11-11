using System.IO.Pipes;
using System.Text.Json;
using DeepCoolLite.Core;

namespace DeepCoolLite.Settings;

/// <summary>Talks to the service over the named pipe. Returns null when the service is not reachable.</summary>
public static class ServiceClient
{
    public static Task<ServiceStatus?> GetStatusAsync() => SendAsync(new IpcRequest(IpcCommand.GetStatus));

    public static Task<ServiceStatus?> SetSettingsAsync(LiteSettings settings) =>
        SendAsync(new IpcRequest(IpcCommand.SetSettings, settings));

    private static async Task<ServiceStatus?> SendAsync(IpcRequest request)
    {
        using var cts = new CancellationTokenSource(Ipc.Timeout);
        await using var pipe = new NamedPipeClientStream(".", AppInfo.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(cts.Token);
            await Ipc.WriteAsync(pipe, request, cts.Token);
            return await Ipc.ReadAsync<ServiceStatus>(pipe, cts.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or TimeoutException
                                       or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return null;
        }
    }
}
