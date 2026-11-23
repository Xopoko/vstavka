using System.Text;
using System.Text.Json.Serialization;
using Vstavka.Core.Localization;

namespace Vstavka.Core;

public enum ClipKind { Text, Link, Color, Image, Files }

public sealed class ClipItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ClipKind Kind { get; set; }
    public string Text { get; set; } = string.Empty;
    public byte[]? ImageBytes { get; set; }
    public string[] Files { get; set; } = [];
    public string SourceApp { get; set; } = string.Empty;
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsPinned { get; set; }
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CollectionId { get; internal set; }

    [JsonIgnore]
    public string Title => Kind switch
    {
        ClipKind.Image => Strings.Get("Image"),
        ClipKind.Files => Files.Length == 1 ? FileName(Files[0]) : Strings.Format("FilesCount",Files.Length),
        ClipKind.Link => Uri.TryCreate(Text.Trim(), UriKind.Absolute, out var uri) ? uri.Host : FirstLine(Text, 80),
        ClipKind.Color => Text.Trim(),
        _ => FirstLine(Text, 80)
    };

    [JsonIgnore]
    public string Preview => Kind switch
    {
        ClipKind.Image => Strings.Get("ImagePreview"),
        ClipKind.Files => string.Join(Environment.NewLine, Files.Select(FileName)),
        _ => Text.Length <= 500 ? Text : Text[..500] + "…"
    };

    [JsonIgnore]
    public string TypeLabel => Kind switch
    {
        ClipKind.Link => Strings.Get("Link"),
        ClipKind.Color => Strings.Get("Color"),
        ClipKind.Image => Strings.Get("Image"),
        ClipKind.Files => Strings.Get("Files"),
        _ => Strings.Get("Text")
    };

    internal static IEqualityComparer<ClipItem> ContentComparer { get; } = new ContentEqualityComparer();

    internal ClipItem CopyForSave()
    {
        var copy = (ClipItem)MemberwiseClone();
        copy.Files = Files.ToArray();
        return copy;
    }

    public static ClipKind DetectTextKind(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length is 4 or 7 or 9 && trimmed[0] == '#' && trimmed.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0)
            return ClipKind.Color;
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            return ClipKind.Link;
        return ClipKind.Text;
    }

    internal bool HasSameContent(ClipItem other)
    {
        if (Kind != other.Kind)
            return false;
        switch (Kind)
        {
            case ClipKind.Image:
                return (ImageBytes ?? []).AsSpan().SequenceEqual(other.ImageBytes ?? []);
            case ClipKind.Files:
                if (Files.Length != other.Files.Length)
                    return false;
                for (var index = 0; index < Files.Length; index++)
                    if (!SameFilePath(Files[index], other.Files[index]))
                        return false;
                return true;
            default:
                return SameText(Text, other.Text);
        }
    }

    private static bool SameFilePath(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
            return true;
        // The ASCII comparison avoids allocating uppercase copies of common paths.
        if (Ascii.IsValid(left) && Ascii.IsValid(right))
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        return SameText(left.ToUpperInvariant(), right.ToUpperInvariant());
    }

    private static bool SameText(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
            return true;
        return left.Length == right.Length && string.Equals(StorageText(left), StorageText(right), StringComparison.Ordinal);
    }

    // JSON storage replaces unpaired UTF-16 surrogates with U+FFFD.
    private static string StorageText(string value) => value.AsSpan().IndexOfAnyInRange('\ud800', '\udfff') < 0
        ? value
        : Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(value));

    private static string FirstLine(string text, int maxLength)
    {
        var line = text.AsSpan().TrimStart("\r\n");
        var separator = line.IndexOfAny('\r', '\n');
        if (separator >= 0)
            line = line[..separator];
        line = line.Trim();
        if (line.Length > maxLength)
            return string.Concat(line[..maxLength], "…");
        return line.Length == text.Length ? text : line.ToString();
    }

    private static string FileName(string path) => Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    private sealed class ContentEqualityComparer : IEqualityComparer<ClipItem>
    {
        public bool Equals(ClipItem? left, ClipItem? right) =>
            ReferenceEquals(left, right) || left is not null && right is not null && left.HasSameContent(right);

        public int GetHashCode(ClipItem item)
        {
            var hash = new HashCode();
            hash.Add(item.Kind);
            switch (item.Kind)
            {
                case ClipKind.Image:
                    hash.AddBytes(item.ImageBytes ?? []);
                    break;
                case ClipKind.Files:
                    foreach (var path in item.Files)
                    {
                        // Equivalent to hashing uppercase ASCII without allocating the uppercase paths.
                        var normalized = Ascii.IsValid(path) ? path : StorageText(path.ToUpperInvariant());
                        hash.Add(normalized, StringComparer.OrdinalIgnoreCase);
                    }
                    break;
                default:
                    hash.Add(StorageText(item.Text), StringComparer.Ordinal);
                    break;
            }
            return hash.ToHashCode();
        }
    }
}
