namespace Vstavka.Platform.Linux;

internal static class LinuxTerminals
{
    private static readonly HashSet<string> TerminalClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "gnome-terminal", "gnome-terminal-server", "org.gnome.terminal",
        "kgx", "org.gnome.console", "ptyxis", "org.gnome.ptyxis",
        "tilix", "com.gexperts.tilix", "terminator", "konsole", "org.kde.konsole",
        "xfce4-terminal", "mate-terminal", "lxterminal", "qterminal",
        "kitty", "alacritty", "wezterm", "org.wezfurlong.wezterm", "foot", "footclient",
        "st", "st-256color", "terminology", "ghostty", "com.mitchellh.ghostty",
        "blackbox", "com.raggesilver.blackbox", "guake", "tilda", "yakuake", "dev.warp.warp",
    };

    public static bool IsTerminalClass(string? wmClass) =>
        wmClass?.Split('\0').Any(TerminalClasses.Contains) == true;
}
