namespace Vstavka.Core;

public sealed class AppSettings
{
    public const string DefaultHotkey = "Ctrl+Shift+V";
    public static readonly string[] SupportedHotkeys = [DefaultHotkey, "Ctrl+Alt+V", "Alt+V"];
    public bool SoundEnabled { get; set; } = true;
    public bool PasteSoundEnabled { get; set; } = true;
    public double Volume { get; set; } = 0.45;
    public bool StartAtLogin { get; set; } = true;
    public int RetentionDays { get; set; } = 30;
    public int MaxItems { get; set; } = 500;
    public string Hotkey { get; set; } = DefaultHotkey;
    public string Language { get; set; } = "en";
    public string Theme { get; set; } = "system";
    public string Accent { get; set; } = "windows";
    public string[] ExcludedApps { get; set; } = ["1Password", "1Password-BrowserSupport", "KeePass", "KeePassXC", "Bitwarden", "Dashlane", "LastPass", "NordPass", "ProtonPass"];

    // Ctrl+Shift+V is the default even though some apps use it for plain-text paste;
    // Ctrl+Alt+V and Alt+V stay available for people who need that binding.
    public static string NormalizeHotkey(string? hotkey) =>
        Array.Find(SupportedHotkeys, x => string.Equals(x, hotkey?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? DefaultHotkey;

    public AppSettings Copy()
    {
        var copy = (AppSettings)MemberwiseClone();
        copy.ExcludedApps = ExcludedApps?.ToArray() ?? [];
        return copy;
    }

    public bool IsAppExcluded(string processName)
    {
        var name = ExecutableName(processName);
        if (name.IsEmpty)
            return false;
        foreach (var excluded in ExcludedApps ?? [])
            if (name.Equals(ExecutableName(excluded), StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static ReadOnlySpan<char> ExecutableName(string? path)
    {
        var name = Path.GetFileName(path.AsSpan().Trim());
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    internal void Normalize()
    {
        Volume = double.IsFinite(Volume) ? Math.Clamp(Volume, 0, 1) : 0.45;
        RetentionDays = Math.Clamp(RetentionDays, 0, 3650);
        MaxItems = Math.Clamp(MaxItems, 1, HistoryStore.MaxItemCount);
        Hotkey = NormalizeHotkey(Hotkey);
        Language = Localization.Strings.NormalizeLanguage(Language);
        Theme = Theme is "light" or "dark" ? Theme : "system";
        Accent = Accent is "violet" or "blue" or "teal" or "rose" or "amber" ? Accent : "windows";
        ExcludedApps = (ExcludedApps ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
