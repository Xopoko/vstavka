namespace Vstavka.Core;

/// <summary>A parsed query shared by history and the ribbon; all terms must match.</summary>
public sealed class ClipSearch
{
    private readonly string[] _terms;

    public ClipSearch(string? query) => _terms = (query ?? string.Empty).Split(
        (char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public bool Matches(ClipItem item)
    {
        foreach (var term in _terms)
        {
            if (Contains(item.Text, term) || Contains(item.SourceApp, term) || Contains(item.TypeLabel, term))
                continue;
            var found = false;
            foreach (var file in item.Files)
                if (Contains(file, term)) { found = true; break; }
            if (found) continue;
            // Text/link titles and previews are excerpts of Text. Avoid constructing
            // them, especially joined file previews, on every unsuccessful keystroke.
            if (item.Kind == ClipKind.Files && Contains(item.Title, term)) continue;
            if (item.Kind == ClipKind.Image && Contains(item.Preview, term)) continue;
            return false;
        }
        return true;
    }

    private static bool Contains(string value, string term) => value.Contains(term, StringComparison.OrdinalIgnoreCase);
}
