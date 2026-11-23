namespace Vstavka.Platform.Linux;

internal sealed class LinuxStartupRegistration(string configHome, string? executable) : IStartupRegistration
{
    private readonly string _path = Path.Combine(configHome, "autostart", "io.github.xopoko.Vstavka.desktop");

    public bool IsSupported => executable is not null && !executable.Contains('=');

    public bool IsEnabled
    {
        get
        {
            if (!IsSupported)
                return false;
            try
            {
                string? command = null;
                var inEntry = false;
                foreach (var line in File.ReadLines(_path))
                {
                    var text = line.Trim();
                    if (text.StartsWith('['))
                    {
                        if (inEntry)
                            break;
                        inEntry = text == "[Desktop Entry]";
                    }
                    if (!inEntry || text.StartsWith('#'))
                        continue;
                    var parts = text.Split('=', 2, StringSplitOptions.TrimEntries);
                    if (parts.Length != 2)
                        continue;
                    if ((parts[0] == "Hidden" && parts[1].Equals("true", StringComparison.OrdinalIgnoreCase)) ||
                        (parts[0] == "X-GNOME-Autostart-enabled" && parts[1].Equals("false", StringComparison.OrdinalIgnoreCase)))
                        return false;
                    if (parts[0] == "Exec")
                        command = parts[1];
                }
                return command is not null &&
                    (StartsWithExecutable(command, Quote(executable!)) ||
                     (CanBeUnquoted(executable!) && StartsWithExecutable(command, executable!)));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    public bool TrySetEnabled(bool enabled)
    {
        if (!IsSupported)
            return false;
        try
        {
            if (!enabled)
            {
                File.Delete(_path);
                return true;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + "." + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, $"""
                    [Desktop Entry]
                    Type=Application
                    Name=Vstavka
                    Exec={Quote(executable!)} --background
                    X-GNOME-Autostart-enabled=true
                    """ + "\n");
                File.Move(temporary, _path, overwrite: true);
            }
            finally
            {
                File.Delete(temporary);
            }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool StartsWithExecutable(string command, string path) =>
        command.StartsWith(path, StringComparison.Ordinal) &&
        (command.Length == path.Length || command[path.Length] is ' ' or '\t');

    private static bool CanBeUnquoted(string path) =>
        !path.Any(character => char.IsWhiteSpace(character) || "\"'\\><~|&;$*?#()\u0060%".Contains(character));

    private static string Quote(string path) => "\"" + path
        .Replace("\\", "\\\\\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\\\"", StringComparison.Ordinal)
        .Replace("\u0060", "\\\\\u0060", StringComparison.Ordinal)
        .Replace("$", "\\\\$", StringComparison.Ordinal)
        .Replace("%", "%%", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\t", "\\t", StringComparison.Ordinal) + "\"";
}
