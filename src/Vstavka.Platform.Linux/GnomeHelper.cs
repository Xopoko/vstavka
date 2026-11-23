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
    private readonly string _extensions = Path.Combine(dataHome, "gnome-shell", "extensions");

    public bool NeedsRestart { get; private set; }

    public PlatformPermission Permission(GnomeConnection shell)
    {
        var allowed = shell.Desktop is not null || shell.Access == GnomeAccess.Busy;
        if (allowed || shell.Access == GnomeAccess.Denied)
            NeedsRestart = false;
        Action? request = allowed || NeedsRestart || executable is null ? null : () => Request(shell.Access == GnomeAccess.Denied);
        return new("gnome-helper", PermissionRequirement.Required,
            allowed ? PermissionState.Granted : PermissionState.Missing, request);
    }

    private void Request(bool active)
    {
        if (NeedsRestart || executable is null)
            return;
        try
        {
            var userExtension = Path.Combine(_extensions, Uuid);
            var installed = FindInstalled(userExtension);
            var copied = installed is null;
            if (copied)
            {
                CopyFiles(Path.Combine(bundledRoot, "gnome-extension", Uuid), userExtension);
                installed = userExtension;
            }

            var schemas = Path.Combine(installed!, "schemas");
            if (installed == userExtension && (copied || !File.Exists(Path.Combine(schemas, "gschemas.compiled"))) &&
                run("glib-compile-schemas", "--strict", schemas) is null)
                return;
            if (!Append(schemas, HelperSchema, AllowedKey, executable))
                return;
            if (!active && Append(null, ShellSchema, EnabledKey, Uuid))
                NeedsRestart = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning("Could not set up the Vstavka GNOME helper: {0}", error.Message);
        }
    }

    private string? FindInstalled(string userExtension)
    {
        if (File.Exists(Path.Combine(userExtension, "metadata.json")))
            return userExtension;
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
        File.Copy(manifest, Path.Combine(destination, "metadata.json"));
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
