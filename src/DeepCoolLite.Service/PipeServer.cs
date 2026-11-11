using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using DeepCoolLite.Core;

namespace DeepCoolLite.Service;

/// <summary>
/// Named pipe used by the settings app to read status and change settings.
/// Only authenticated users can connect; nobody but SYSTEM/Administrators can create pipe instances,
/// and FirstPipeInstance makes the service refuse to start if another process squatted the name.
/// </summary>
public sealed class PipeServer(ServiceState state, ILogger<PipeServer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var security = CreateSecurity();

        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = NamedPipeServerStreamAcl.Create(
                    AppInfo.PipeName, PipeDirection.InOut, maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance,
                    inBufferSize: 0, outBufferSize: 0, security);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogError("Cannot create pipe '{Pipe}' (is another process using it?): {Message}", AppInfo.PipeName, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                continue;
            }

            await using (server)
            {
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                try
                {
                    await HandleAsync(server, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    logger.LogDebug("Pipe request failed: {Message}", ex.Message);
                }
            }
        }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        // A client that connects and never sends anything must not block the server
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(Ipc.Timeout);

        var request = await Ipc.ReadAsync<IpcRequest>(pipe, timeout.Token).ConfigureAwait(false);
        if (request is null) return;

        if (request is { Command: IpcCommand.SetSettings, Settings: { } settings })
            state.ApplySettings(settings);

        await Ipc.WriteAsync(pipe, state.Snapshot, timeout.Token).ConfigureAwait(false);
    }

    private static PipeSecurity CreateSecurity()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return security;
    }
}
