namespace Vstavka.Core;

public sealed record ClipCollection(string Id, string Name, string Color, DateTimeOffset CreatedAt)
{
    public static IReadOnlyList<string> Colors { get; } = Array.AsReadOnly(new[]
    {
        "CollectionBlue", "CollectionTeal", "CollectionGreen", "CollectionAmber",
        "CollectionOrange", "CollectionRose", "CollectionViolet", "CollectionSlate"
    });
}

public enum CollectionEditError { None, EmptyName, NameTooLong, Limit, InvalidColor, NotFound }
