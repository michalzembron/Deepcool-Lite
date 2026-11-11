using System.ComponentModel;
using System.Diagnostics;

namespace DeepCoolLite.Settings.Setup;

public enum ElevatedResult { Success, Failed, Cancelled }

/// <summary>Runs this app again with admin rights (one UAC prompt) for a single operation.</summary>
public static class Elevation
{
    private const int ErrorCancelled = 1223; // the user declined the UAC prompt

    public static Task<ElevatedResult> RunAsync(params string[] args) => Task.Run(() =>
    {
        var psi = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = true,
            Verb = "runas",
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        try
        {
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            return p.ExitCode == 0 ? ElevatedResult.Success : ElevatedResult.Failed;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return ElevatedResult.Cancelled;
        }
    });
}
