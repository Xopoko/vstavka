using System.ComponentModel;
using System.Diagnostics;

namespace Vstavka.Platform.Linux;

internal delegate string? LinuxCommand(string fileName, params string[] arguments);

internal static class LinuxProcess
{
    public static string? Run(string fileName, params string[] arguments)
    {
        try
        {
            var start = new ProcessStartInfo(fileName) { UseShellExecute = false, RedirectStandardOutput = true };
            foreach (var argument in arguments)
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            if (process is null)
                return null;
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(3000))
            {
                process.Kill();
                return null;
            }
            return process.ExitCode == 0 ? output.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult() : null;
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException or TimeoutException)
        {
            return null;
        }
    }

    public static string? GetExecutable()
    {
        if (!OperatingSystem.IsLinux())
            return null;
        try
        {
            var path = new FileInfo("/proc/self/exe").LinkTarget;
            return path is not null && Path.IsPathFullyQualified(path) && File.Exists(path) &&
                !string.Equals(Path.GetFileName(path), "dotnet", StringComparison.Ordinal) ? path : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
