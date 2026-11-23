namespace Vstavka.Core;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Meta = 8,
}

/// <summary>
/// A global shortcut in platform-neutral form. <see cref="HotkeyModifiers.Alt"/> is Option on macOS and
/// <see cref="HotkeyModifiers.Meta"/> is Command on macOS, the Windows key on Windows and Super on Linux.
/// </summary>
/// <param name="Key">"A"–"Z", "0"–"9", "F1"–"F24" or "Space".</param>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, string Key)
{
    private const HotkeyModifiers Command = HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Meta;

    /// <summary>True when the gesture has Ctrl, Alt or Meta, so ordinary typing, with or without Shift, never triggers it.</summary>
    public bool IsUsable => (Modifiers & Command) != 0 && NormalizeKey(Key) == Key;

    /// <summary>Modifiers in the order Ctrl, Alt, Shift, Meta, then the key, joined by "+".</summary>
    public override string ToString()
    {
        var parts = new List<string>(5);
        foreach (var modifier in (ReadOnlySpan<HotkeyModifiers>)[HotkeyModifiers.Ctrl, HotkeyModifiers.Alt, HotkeyModifiers.Shift, HotkeyModifiers.Meta])
            if (Modifiers.HasFlag(modifier))
                parts.Add(modifier.ToString());
        parts.Add(Key);
        return string.Join('+', parts);
    }

    /// <summary>Accepts aliases such as Control, Option, Cmd, Command, Win and Super in any case and order.</summary>
    /// <returns>True only for a usable gesture.</returns>
    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return false;
        var modifiers = HotkeyModifiers.None;
        foreach (var part in parts.AsSpan(0, parts.Length - 1))
        {
            var modifier = ParseModifier(part);
            if (modifier == HotkeyModifiers.None || modifiers.HasFlag(modifier))
                return false;
            modifiers |= modifier;
        }
        if (NormalizeKey(parts[^1]) is not { } key)
            return false;
        gesture = new HotkeyGesture(modifiers, key);
        return gesture.IsUsable;
    }

    private static HotkeyModifiers ParseModifier(string name) => name.ToLowerInvariant() switch
    {
        "ctrl" or "control" => HotkeyModifiers.Ctrl,
        "alt" or "option" or "opt" => HotkeyModifiers.Alt,
        "shift" => HotkeyModifiers.Shift,
        "meta" or "cmd" or "command" or "win" or "super" => HotkeyModifiers.Meta,
        _ => HotkeyModifiers.None,
    };

    private static string? NormalizeKey(string? key)
    {
        if (string.IsNullOrEmpty(key))
            return null;
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]))
            return char.ToUpperInvariant(key[0]).ToString();
        if (key.Equals("Space", StringComparison.OrdinalIgnoreCase))
            return "Space";
        if (key.Length is 2 or 3 && (key[0] is 'F' or 'f') && int.TryParse(key.AsSpan(1), out var number) && number is >= 1 and <= 24 && key[1] != '0')
            return "F" + number;
        return null;
    }
}
