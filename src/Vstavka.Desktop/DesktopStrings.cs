using System.Text.Json;
using Vstavka.Core.Localization;
using Vstavka.Platform;

namespace Vstavka.Desktop;

internal static class DesktopStrings
{
    private static readonly Dictionary<string, string[]> Catalog = ReadCatalog();

    public static string Get(string key)
    {
        var index = key switch
        {
            "adapter-not-wired" => 0,
            "Permission" => 1,
            "Grant" => 2,
            "ManualPaste" => 3,
            "gnome-extension-missing" => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(key))
        };
        return Catalog.GetValueOrDefault(Strings.Language, Catalog["en"])[index];
    }

    public static string Notice(PlatformNotice notice) => notice.Code switch
    {
        "adapter-not-wired" or "gnome-extension-missing" => Get(notice.Code),
        "accessibility-permission" => Get("Permission"),
        _ => notice.Message
    };

    private static Dictionary<string, string[]> ReadCatalog()
    {
        using var stream = typeof(DesktopStrings).Assembly.GetManifestResourceStream("Vstavka.Desktop.DesktopStrings.json")
            ?? throw new InvalidOperationException("The desktop localization catalog is missing.");
        return JsonSerializer.Deserialize<Dictionary<string, string[]>>(stream)
            ?? throw new InvalidDataException("Invalid desktop localization catalog.");
    }
}
