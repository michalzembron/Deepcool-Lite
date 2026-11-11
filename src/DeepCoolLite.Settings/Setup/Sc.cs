using System.Diagnostics;

namespace DeepCoolLite.Settings.Setup;

/// <summary>Runs sc.exe (requires admin for anything but queries).</summary>
internal static class Sc
{
    public static string Run(params string[] args)
    {
        var psi = new ProcessStartInfo("sc.exe", string.Join(' ', args))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"sc.exe {psi.Arguments}\n(exit code {p.ExitCode}) {output.Trim()}");
        return output;
    }
}
