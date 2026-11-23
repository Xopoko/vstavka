using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace Vstavka.Platform.Windows;

internal sealed class WindowsStartupRegistration(IWindowsRunKey runKey, string? executablePath) : IStartupRegistration
{
    private const string ValueName = "Vstavka.Preview";

    public bool IsSupported => OperatingSystem.IsWindows();

    public bool IsEnabled
    {
        get
        {
            if (!IsSupported || string.IsNullOrEmpty(executablePath))
                return false;

            try
            {
                return ReadExecutable(runKey.Read(ValueName)).Equals(executablePath, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
            {
                return false;
            }
        }
    }

    public bool TrySetEnabled(bool enabled)
    {
        if (!IsSupported || (enabled && string.IsNullOrEmpty(executablePath)))
            return false;

        try
        {
            if (enabled)
                runKey.Write(ValueName, $"\"{executablePath}\" --background");
            else
                runKey.Delete(ValueName);

            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    private static ReadOnlySpan<char> ReadExecutable(string? value)
    {
        var command = value.AsSpan().TrimStart(" \t");
        if (!command.IsEmpty && command[0] == '"')
        {
            var closingQuote = command[1..].IndexOf('"') + 1;
            return closingQuote > 0 && (closingQuote == command.Length - 1 || command[closingQuote + 1] is ' ' or '\t')
                ? command[1..closingQuote] : default;
        }

        var separator = command.IndexOfAny(' ', '\t');
        return separator < 0 ? command : command[..separator];
    }
}

internal interface IWindowsRunKey
{
    string? Read(string name);
    void Write(string name, string value);
    void Delete(string name);
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsRunKey : IWindowsRunKey
{
    // A future MSIX build will use the package's StartupTask instead.
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? Read(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunPath);
        return key?.GetValue(name) as string;
    }

    public void Write(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunPath);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void Delete(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}
