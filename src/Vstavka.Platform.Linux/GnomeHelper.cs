using System.Globalization;
using System.Text.RegularExpressions;

namespace Vstavka.Platform.Linux;

internal sealed class GnomeHelper(string dataHome, string bundledRoot, string? executable,
    LinuxCommand run, string? dataDirs)
{
    private const string Uuid = "vstavka@xopoko.github.io";
    private const string ShellSchema = "org.gnome.shell";
    private const string HelperSchema = "io.github.xopoko.Vstavka.Shell";
    private const string EnabledKey = "enabled-extensions";
    private const string AllowedKey = "allowed-executables";
    private const string SessionManager = "org.gnome.SessionManager";
    private readonly string _userExtension = Path.Combine(dataHome, "gnome-shell", "extensions", Uuid);
    private readonly object _setupLock = new();

    public PlatformPermission Permission(GnomeConnection shell)
    {
        if (shell.Desktop is not null || shell.Access == GnomeAccess.Busy)
            return Result(PermissionState.Granted);
        if (shell.Access == GnomeAccess.Denied)
            return Result(PermissionState.Missing, executable is null ? null : () => Configure(allowOnly: true));
        if (shell.Access == GnomeAccess.SessionUnavailable)
            return Result(PermissionState.Missing);
        if (FindInstalled() is null)
            return Result(PermissionState.Missing, executable is null ? null : () => Configure(allowOnly: false));

        var enabled = run("gsettings", "get", ShellSchema, EnabledKey);
        if (enabled is null || !GVariantStringArray.TryAppend(enabled, Uuid, out var updated) || updated is not null)
            return Result(PermissionState.Missing, () => Configure(allowOnly: false));
        if (shell.Access != GnomeAccess.Unavailable)
            return Result(PermissionState.Missing);

        var hasOwner = run("gdbus", "call", "--session", "--dest", "org.freedesktop.DBus", "--object-path",
            "/org/freedesktop/DBus", "--method", "org.freedesktop.DBus.NameHasOwner", SessionManager);
        return Result(PermissionState.RestartRequired, hasOwner?.Trim() == "(true,)" ? SignOut : null);
    }

    private static PlatformPermission Result(PermissionState state, Action? request = null) =>
        new("gnome-helper", PermissionRequirement.Required, state, request);

    private void SignOut() => run("gdbus", "call", "--session", "--dest", SessionManager, "--object-path",
        "/org/gnome/SessionManager", "--method", SessionManager + ".Logout", "0");

    private void Configure(bool allowOnly)
    {
        lock (_setupLock)
        {
            try
            {
                var installed = FindInstalled();
                if (allowOnly)
                {
                    if (installed is not null && executable is not null)
                        AllowExecutable(installed, executable);
                    return;
                }

                if (installed is null)
                {
                    if (executable is null)
                        return;
                    var source = Path.Combine(bundledRoot, "gnome-extension", Uuid);
                    CopyFiles(source, _userExtension);
                    if (!AllowExecutable(_userExtension, executable, newInstall: true))
                        return;
                    // The manifest marks a prepared install and is copied last so setup can be retried.
                    File.Copy(Path.Combine(source, "metadata.json"), Path.Combine(_userExtension, "metadata.json"));
                }

                Append(null, ShellSchema, EnabledKey, Uuid);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Trace.TraceWarning("Could not set up the Vstavka GNOME helper: {0}", error.Message);
            }
        }
    }

    private bool AllowExecutable(string installed, string path, bool newInstall = false)
    {
        var schemas = Path.Combine(installed, "schemas");
        if (installed == _userExtension && (newInstall || !File.Exists(Path.Combine(schemas, "gschemas.compiled"))) &&
            run("glib-compile-schemas", "--strict", schemas) is null)
            return false;
        return Append(schemas, HelperSchema, AllowedKey, path);
    }

    private string? FindInstalled()
    {
        if (File.Exists(Path.Combine(_userExtension, "metadata.json")))
            return _userExtension;
        foreach (var directory in (dataDirs ?? "/usr/local/share:/usr/share").Split(':'))
        {
            if (!Path.IsPathFullyQualified(directory))
                continue;
            var extension = Path.Combine(directory, "gnome-shell", "extensions", Uuid);
            if (File.Exists(Path.Combine(extension, "metadata.json")))
                return extension;
        }
        return null;
    }

    private bool Append(string? schemaDir, string schema, string key, string value)
    {
        string[] prefix = schemaDir is null ? [] : ["--schemadir", schemaDir];
        var current = run("gsettings", [.. prefix, "get", schema, key]);
        if (current is null || !GVariantStringArray.TryAppend(current, value, out var updated))
            return false;
        return updated is null || run("gsettings", [.. prefix, "set", schema, key, updated]) is not null;
    }

    private static void CopyFiles(string source, string destination)
    {
        var manifest = Path.Combine(source, "metadata.json");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if (file == manifest || Path.GetFileName(file) == "gschemas.compiled")
                continue;
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
        Directory.CreateDirectory(destination);
    }
}

internal static partial class GVariantStringArray
{
    private const string Literal = """(?<item>'(?:[^'\\]|\\.)*'|"(?:[^"\\]|\\.)*")""";

    [GeneratedRegex(@"\A(?:@as\s+)?\[(?<items>\s*(?:" + Literal + @"(?:\s*,\s*" + Literal + @")*)?\s*)\]\z",
        RegexOptions.Singleline)]
    private static partial Regex ArrayPattern();

    [GeneratedRegex("""\\(?:u[0-9a-fA-F]{4}|U[0-9a-fA-F]{8}|.)""", RegexOptions.Singleline)]
    private static partial Regex EscapePattern();

    public static bool TryAppend(string current, string value, out string? updated)
    {
        updated = null;
        var array = ArrayPattern().Match(current.Trim());
        if (!array.Success)
            return false;
        var items = array.Groups["item"].Captures;
        if (items.Any(item => EscapePattern().Replace(item.Value[1..^1], DecodeEscape) == value))
            return true;

        var literal = "'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal) + "'";
        updated = items.Count == 0 ? $"[{literal}]" : $"[{array.Groups["items"].Value.Trim()}, {literal}]";
        return true;
    }

    private static string DecodeEscape(Match match) => match.Value[1] switch
    {
        'a' => "\a",
        'b' => "\b",
        'f' => "\f",
        'n' => "\n",
        'r' => "\r",
        't' => "\t",
        'v' => "\v",
        'u' or 'U' when match.Length > 2 => char.ConvertFromUtf32(
            int.Parse(match.Value.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)),
        _ => match.Value[1..],
    };
}
