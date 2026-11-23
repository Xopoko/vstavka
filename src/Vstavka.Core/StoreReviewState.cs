namespace Vstavka.Core;

public readonly record struct StoreReviewState(int UseCount, DateTimeOffset? FirstUsedUtc, string? LastRequestedVersion)
{
    private const int RequiredUses = 10;
    private static readonly TimeSpan RequiredAge = TimeSpan.FromDays(3);

    public StoreReviewState CountUse(DateTimeOffset utcNow) => UseCount >= RequiredUses ? this : this with
    {
        UseCount = UseCount + 1,
        FirstUsedUtc = FirstUsedUtc ?? utcNow
    };

    public bool ShouldRequest(string? version, DateTimeOffset utcNow) =>
        !string.IsNullOrWhiteSpace(version) && !string.Equals(LastRequestedVersion, version, StringComparison.Ordinal) &&
        UseCount >= RequiredUses && FirstUsedUtc is { } first && utcNow - first >= RequiredAge;

    public StoreReviewState MarkRequested(string version) => this with { LastRequestedVersion = version };
}
