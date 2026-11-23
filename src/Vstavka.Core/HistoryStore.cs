using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vstavka.Core.Localization;

namespace Vstavka.Core;

/// <summary>
/// Local clipboard history with optional encrypted storage. Mutate models on their owning UI thread,
/// then call SaveAsync. ImageBytes are immutable after capture and may be shared with save snapshots.
/// </summary>
public sealed class HistoryStore
{
    public const int MaxImageBytes = 10 * 1024 * 1024;
    public const long MaxTotalImageBytes = 100L * 1024 * 1024;
    public const int MaxTextLength = 1024 * 1024;
    public const int MaxItemCount = 5000;
    public const int MaxFilesPerItem = 1000;
    public const int MaxSourceIconBytes = 64 * 1024;
    public const int MaxSourceIcons = 512;
    public const int MaxCollections = 50;
    public const int MaxCollectionNameLength = 40;
    private const long MaxStorageFileBytes = 200L * 1024 * 1024;
    private const int MaxSourceNameLength = 1024;
    private readonly string _directory;
    private readonly string _filePath;
    private readonly IHistoryProtector? _protector;
    private readonly List<ClipItem> _items = [];
    private readonly List<ClipCollection> _collections = [];
    private readonly Dictionary<string, byte[]> _sourceIcons = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyList<ClipItem> _readOnlyItems;
    private readonly object _saveQueueLock = new();
    private Task _pendingWrite = Task.CompletedTask;

    /// <summary>Protects history with Windows DPAPI, as earlier releases did.</summary>
    public HistoryStore(string directory) : this(directory, new WindowsHistoryProtector()) { }

    /// <summary>Keeps history in memory when <paramref name="protector"/> is null.</summary>
    public HistoryStore(string directory, IHistoryProtector? protector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _protector = protector;
        _directory = Path.GetFullPath(directory);
        _filePath = Path.Combine(_directory, "history.vstavka");
        _readOnlyItems = _items.AsReadOnly();
        Collections = _collections.AsReadOnly();
    }

    public IReadOnlyList<ClipItem> Items => _readOnlyItems;
    public bool IsPersistent => _protector is not null;
    public IReadOnlyList<ClipCollection> Collections { get; }
    public AppSettings Settings { get; set; } = new();
    public string? LoadWarning { get; private set; }
    public bool SavingBlocked { get; private set; }
    public string? LastRejectionReason { get; private set; }

    public void Load()
    {
        // A reload must not read a file that an older queued snapshot will replace afterward.
        Task pending;
        lock (_saveQueueLock) pending = _pendingWrite;
        try { pending.GetAwaiter().GetResult(); }
        catch { /* The caller of the failed save receives its error; reload the last good file. */ }
        _items.Clear();
        _collections.Clear();
        _sourceIcons.Clear();
        Settings = new AppSettings();
        LoadWarning = null;
        SavingBlocked = false;
        if (_protector is not { } protector)
        {
            LastRejectionReason = null;
            return;
        }

        byte[]? plaintext = null;
        try
        {
            using (var stream = File.OpenRead(_filePath))
            {
                var length = stream.Length;
                if (length > MaxStorageFileBytes)
                    throw new InvalidDataException("History exceeds its storage limit.");
                var ciphertext = new byte[(int)length];
                stream.ReadExactly(ciphertext);
                plaintext = protector.Unprotect(ciphertext);
            }
            var document = JsonSerializer.Deserialize<HistoryDocument>(plaintext) ?? throw new InvalidDataException("History is empty.");
            if (document.Version != 1)
                throw new InvalidDataException("Unsupported history format.");
            if (document.Items is null || document.Settings is null || document.Items.Count > MaxItemCount)
                throw new InvalidDataException("Invalid history document.");
            Settings = document.Settings;
            if (document.Collections is not null)
                _collections.AddRange(document.Collections);
            var collectionIds = _collections.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var contents = new HashSet<ClipItem>(ClipItem.ContentComparer);
            foreach (var item in document.Items)
            {
                if (item is null || !IsValid(item) || !ids.Add(item.Id) || !contents.Add(item))
                    throw new InvalidDataException("Invalid or duplicate history item.");
                if (item.CollectionId is { } collectionId && !collectionIds.Contains(collectionId))
                    item.CollectionId = null;
                _items.Add(item);
            }
            if (_items.Where(IsProtected).Sum(ImageSize) > MaxTotalImageBytes)
                throw new InvalidDataException("Protected images exceed storage limit.");
            if (document.SourceIcons is not null)
                foreach (var (source, png) in document.SourceIcons)
                    _sourceIcons.Add(source, png);
            ApplyRetention();
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            // A new profile has no history file yet.
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            BlockSaving();
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or InvalidDataException)
        {
            _items.Clear();
            _collections.Clear();
            _sourceIcons.Clear();
            Settings = new AppSettings();
            var quarantinePath = Path.Combine(_directory, $"history.recovery-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.vstavka");
            try
            {
                File.Move(_filePath, quarantinePath, false);
                LoadWarning = Strings.Format("RecoverySaved", quarantinePath);
            }
            catch (Exception quarantineError) when (quarantineError is IOException or UnauthorizedAccessException)
            {
                BlockSaving();
            }
        }
        finally
        {
            if (plaintext is not null)
                CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private void BlockSaving()
    {
        SavingBlocked = true;
        LoadWarning = Strings.Format("RecoveryBlocked", _filePath);
    }

    /// <summary>
    /// Saves the current snapshot after all earlier writes. Safe for shutdown: the worker never
    /// posts to the caller's synchronization context. This method blocks until its own save completes.
    /// </summary>
    public void Save() => SaveAsync().GetAwaiter().GetResult();

    /// <summary>
    /// Captures a detached model/settings snapshot synchronously, then encrypts and writes it off-thread.
    /// Saves are committed in call order. Each returned task reports only that save's success or failure.
    /// </summary>
    public Task SaveAsync()
    {
        if (_protector is not { } protector)
            return Task.CompletedTask;

        var snapshot = CaptureSnapshot();
        lock (_saveQueueLock)
        {
            var predecessor = _pendingWrite;
            _pendingWrite = Task.Run(async () =>
            {
                try { await predecessor.ConfigureAwait(false); }
                catch { /* A failed earlier save must not prevent a later valid save. */ }
                WriteSnapshot(snapshot, protector);
            });
            return _pendingWrite;
        }
    }

    private HistoryDocument CaptureSnapshot()
    {
        if (SavingBlocked)
            throw new IOException("Saving is blocked to preserve unreadable clipboard history. " + LoadWarning);
        // Validate mutable fields before handing them to a worker. Hashing image contents stays off the UI thread.
        if (_items.Any(x => !IsValid(x)) || _items.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != _items.Count ||
            _items.Where(IsProtected).Sum(ImageSize) > MaxTotalImageBytes || _items.Count(IsProtected) > MaxItemCount)
            throw new InvalidDataException("Clipboard history contains an invalid item or exceeds a protected storage limit. The saved history has not been changed.");
        ApplyRetention();
        return new HistoryDocument
        {
            Items = _items.Select(item => item.CopyForSave()).ToList(),
            Collections = _collections.ToList(),
            Settings = Settings.Copy(),
            SourceIcons = new(_sourceIcons, StringComparer.OrdinalIgnoreCase)
        };
    }

    private void WriteSnapshot(HistoryDocument snapshot, IHistoryProtector protector)
    {
        if (snapshot.Items.ToHashSet(ClipItem.ContentComparer).Count != snapshot.Items.Count)
            throw new InvalidDataException("Clipboard history contains duplicate content. The saved history has not been changed.");
        Directory.CreateDirectory(_directory);
        var clearBytes = JsonSerializer.SerializeToUtf8Bytes(snapshot);
        byte[] protectedBytes;
        try
        {
            if (clearBytes.LongLength > MaxStorageFileBytes - 4096)
                throw new IOException("Clipboard history exceeds its storage limit. Remove some entries and try again.");
            protectedBytes = protector.Protect(clearBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
        }

        var temporaryPath = Path.Combine(_directory, $".history-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.WriteThrough))
            {
                stream.Write(protectedBytes);
                stream.Flush(true);
            }
            if (File.Exists(_filePath))
                File.Replace(temporaryPath, _filePath, null);
            else
                File.Move(temporaryPath, _filePath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    /// <returns>True for a new entry. False for a duplicate or rejected entry; see LastRejectionReason.</returns>
    public bool Add(ClipItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        LastRejectionReason = null;
        if (!IsValid(item))
        {
            LastRejectionReason = Strings.Get("ItemLimit");
            return false;
        }
        if (Settings.IsAppExcluded(item.SourceApp))
        {
            LastRejectionReason = Strings.Get("AppExcluded");
            return false;
        }
        Settings.Normalize();
        // Compare only the candidate's kind and content. Rehashing every retained image here
        // stalled the UI thread even when the new clipboard entry was a few characters long.
        // Do not cache mutable text/file arrays: edits must be reflected in the next capture.
        var existing = _items.FirstOrDefault(x => x.HasSameContent(item));
        if (existing is not null)
        {
            existing.CapturedAt = item.CapturedAt;
            existing.SourceApp = item.SourceApp;
            existing.IsPinned |= item.IsPinned;
            ApplyRetentionToItems();
            return false;
        }
        if (item.CollectionId is not null && !_collections.Any(collection => collection.Id == item.CollectionId))
            item.CollectionId = null;
        if (_items.Where(IsProtected).Sum(ImageSize) + ImageSize(item) > MaxTotalImageBytes)
        {
            LastRejectionReason = Strings.Get("PinnedImageLimit");
            return false;
        }
        var itemLimit = IsProtected(item) ? MaxItemCount : Settings.MaxItems;
        if (_items.Count(IsProtected) >= itemLimit)
        {
            LastRejectionReason = Strings.Format("PinnedItemLimit", itemLimit);
            return false;
        }
        if (_items.Any(x => x.Id == item.Id))
            item.Id = Guid.NewGuid().ToString("N");
        _items.Add(item);
        ApplyRetentionToItems();
        return _items.Contains(item);
    }

    public void TogglePin(string id)
    {
        var item = _items.FirstOrDefault(x => x.Id == id);
        if (item is not null)
        {
            item.IsPinned = !item.IsPinned;
            ApplyRetention();
        }
    }

    public ReadOnlyMemory<byte> GetSourceIcon(string source) =>
        _sourceIcons.TryGetValue(source, out var png) ? png : ReadOnlyMemory<byte>.Empty;

    /// <returns>True when a referenced, permitted source receives new icon bytes.</returns>
    public bool SetSourceIcon(string source, ReadOnlySpan<byte> png)
    {
        if (string.IsNullOrWhiteSpace(source) || source.Length > MaxSourceNameLength || Settings.IsAppExcluded(source))
            return false;
        var existing = _sourceIcons.GetValueOrDefault(source);
        if ((existing is null ? _sourceIcons.Count >= MaxSourceIcons : png.SequenceEqual(existing)) ||
            !_items.Any(item => StringComparer.OrdinalIgnoreCase.Equals(item.SourceApp, source)) || !SourceIconPng.IsValid(png))
            return false;
        _sourceIcons[source] = png.ToArray();
        return true;
    }

    public void Remove(string id)
    {
        if (_items.RemoveAll(x => x.Id == id) != 0)
            PruneSourceIcons();
    }

    public void Clear(bool includePinned = false)
    {
        if (includePinned)
            _items.Clear();
        else
            _items.RemoveAll(x => !IsProtected(x));
        PruneSourceIcons();
    }

    public (ClipCollection? Collection, CollectionEditError Error) CreateCollection(string? name, string color)
    {
        var cleanName = name?.Trim() ?? string.Empty;
        var error = CheckCollectionName(cleanName);
        if (error != CollectionEditError.None)
            return (null, error);
        if (_collections.Count >= MaxCollections)
            return (null, CollectionEditError.Limit);
        if (!ClipCollection.Colors.Contains(color))
            return (null, CollectionEditError.InvalidColor);
        var collection = new ClipCollection(Guid.NewGuid().ToString("N"), cleanName, color, DateTimeOffset.UtcNow);
        _collections.Add(collection);
        return (collection, CollectionEditError.None);
    }

    public CollectionEditError RenameCollection(string id, string? name)
    {
        var cleanName = name?.Trim() ?? string.Empty;
        var error = CheckCollectionName(cleanName);
        if (error != CollectionEditError.None)
            return error;
        var index = _collections.FindIndex(x => x.Id == id);
        if (index < 0)
            return CollectionEditError.NotFound;
        _collections[index] = _collections[index] with { Name = cleanName };
        return CollectionEditError.None;
    }

    public bool SetCollectionColor(string id, string color)
    {
        var index = _collections.FindIndex(x => x.Id == id);
        if (index < 0 || !ClipCollection.Colors.Contains(color))
            return false;
        _collections[index] = _collections[index] with { Color = color };
        return true;
    }

    public bool DeleteCollection(string id)
    {
        if (_collections.RemoveAll(x => x.Id == id) == 0)
            return false;
        _items.RemoveAll(x => x.CollectionId == id);
        PruneSourceIcons();
        return true;
    }

    public bool AssignCollection(string itemId, string? collectionId)
    {
        var item = _items.FirstOrDefault(x => x.Id == itemId);
        if (item is null || (collectionId is not null && !_collections.Any(x => x.Id == collectionId)))
            return false;
        item.CollectionId = collectionId;
        ApplyRetention();
        return true;
    }

    private static CollectionEditError CheckCollectionName(string name) => name.Length switch
    {
        0 => CollectionEditError.EmptyName,
        > MaxCollectionNameLength => CollectionEditError.NameTooLong,
        _ => CollectionEditError.None
    };

    public IReadOnlyList<ClipItem> Search(string query, ClipKind? kind = null, bool pinnedOnly = false, string? collectionId = null)
    {
        var search = new ClipSearch(query);
        return _items.Where(item => item.CollectionId == collectionId && (!kind.HasValue || item.Kind == kind.Value) && (!pinnedOnly || item.IsPinned) &&
            search.Matches(item)).ToArray();
    }

    public void ApplyRetention()
    {
        Settings ??= new AppSettings();
        Settings.Normalize();
        ApplyRetentionToItems();
    }

    private void ApplyRetentionToItems()
    {
        if (Settings.RetentionDays > 0)
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-Settings.RetentionDays);
            _items.RemoveAll(x => !IsProtected(x) && x.CapturedAt < cutoff);
        }
        _items.Sort((left, right) =>
        {
            var pinOrder = right.IsPinned.CompareTo(left.IsPinned);
            return pinOrder != 0 ? pinOrder : right.CapturedAt.CompareTo(left.CapturedAt);
        });
        var available = Math.Max(0, Settings.MaxItems - _items.Count(IsProtected));
        _items.RemoveAll(item =>
        {
            if (IsProtected(item)) return false;
            if (available == 0) return true;
            available--;
            return false;
        });
        var totalImages = _items.Sum(ImageSize);
        for (var index = _items.Count - 1; index >= 0 && totalImages > MaxTotalImageBytes; index--)
        {
            var item = _items[index];
            if (IsProtected(item) || item.Kind != ClipKind.Image)
                continue;
            totalImages -= ImageSize(item);
            _items.RemoveAt(index);
        }
        PruneSourceIcons();
    }

    private void PruneSourceIcons()
    {
        if (_sourceIcons.Count == 0)
            return;
        var sources = _items.Select(item => item.SourceApp).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var source in _sourceIcons.Keys)
            if (!sources.Contains(source))
                _sourceIcons.Remove(source);
    }

    private static long ImageSize(ClipItem item) => item.Kind == ClipKind.Image ? item.ImageBytes?.LongLength ?? 0 : 0;
    private static bool IsProtected(ClipItem item) => item.IsPinned || item.CollectionId is not null;

    private static bool IsValid(ClipItem item) =>
        !string.IsNullOrWhiteSpace(item.Id) && item.Id.Length <= 128 && Enum.IsDefined(item.Kind) && item.Text is not null &&
        item.Text.Length <= MaxTextLength && item.SourceApp is not null && item.SourceApp.Length <= MaxSourceNameLength && item.Files is not null &&
        item.Files.Length <= MaxFilesPerItem && item.Files.All(x => !string.IsNullOrWhiteSpace(x) && x.Length <= 32768) &&
        (item.Kind == ClipKind.Image ? item.ImageBytes is { Length: > 0 and <= MaxImageBytes } : item.ImageBytes is null) &&
        (item.Kind == ClipKind.Files ? item.Files.Length > 0 : item.Files.Length == 0) &&
        (item.Kind is ClipKind.Text or ClipKind.Link or ClipKind.Color ? !string.IsNullOrWhiteSpace(item.Text) : true);

    private sealed class HistoryDocument
    {
        public int Version { get; set; } = 1;
        public AppSettings Settings { get; set; } = new();
        public List<ClipItem> Items { get; set; } = [];
        [JsonConverter(typeof(CollectionListConverter))]
        public List<ClipCollection>? Collections { get; set; }
        [JsonConverter(typeof(SourceIconMapConverter))]
        public Dictionary<string, byte[]>? SourceIcons { get; set; }
    }

    private sealed class CollectionListConverter : JsonConverter<List<ClipCollection>>
    {
        public override List<ClipCollection>? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                reader.Skip();
                return null;
            }

            var collections = new List<ClipCollection>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (collections.Count == MaxCollections)
                {
                    reader.Skip();
                    continue;
                }
                using var element = JsonDocument.ParseValue(ref reader);
                ClipCollection? collection;
                try { collection = element.RootElement.Deserialize<ClipCollection>(options); }
                catch (JsonException) { continue; }
                if (collection is null || string.IsNullOrWhiteSpace(collection.Id) || collection.Id.Length > 128 ||
                    collection.Name is null || !ClipCollection.Colors.Contains(collection.Color) || collection.CreatedAt == default)
                    continue;
                var name = collection.Name.Trim();
                if (CheckCollectionName(name) == CollectionEditError.None && ids.Add(collection.Id))
                    collections.Add(collection with { Name = name });
            }
            return collections.OrderBy(collection => collection.CreatedAt).ToList();
        }

        public override void Write(Utf8JsonWriter writer, List<ClipCollection> collections, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, collections, options);
    }

    private sealed class SourceIconMapConverter : JsonConverter<Dictionary<string, byte[]>>
    {
        private const int MaxBase64Length = ((MaxSourceIconBytes + 2) / 3) * 4;

        public override Dictionary<string, byte[]>? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                reader.Skip();
                return null;
            }

            var icons = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var source = icons.Count < MaxSourceIcons && TokenLength(ref reader) <= 6 * MaxSourceNameLength
                    ? ReadSource(ref reader) : null;
                if (!reader.Read())
                    throw new JsonException();
                if (!string.IsNullOrWhiteSpace(source) && source.Length <= MaxSourceNameLength &&
                    ReadPng(ref reader) is { } png)
                    icons[source] = png;
                reader.Skip();
            }
            return icons;
        }

        private static string? ReadSource(ref Utf8JsonReader reader)
        {
            try { return reader.GetString(); }
            catch (InvalidOperationException) { return null; }
        }

        private static byte[]? ReadPng(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.String || TokenLength(ref reader) > MaxBase64Length)
                return null;
            try { return reader.TryGetBytesFromBase64(out var png) && SourceIconPng.IsValid(png) ? png : null; }
            catch (InvalidOperationException) { return null; }
        }

        private static long TokenLength(ref Utf8JsonReader reader) =>
            reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length;

        public override void Write(Utf8JsonWriter writer, Dictionary<string, byte[]> icons, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var (source, png) in icons)
                writer.WriteBase64String(source, png);
            writer.WriteEndObject();
        }
    }
}
