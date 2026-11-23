using System.Text;
using System.Text.Json;
using Vstavka.Core;

var testRoot = Path.Combine(Path.GetTempPath(), "Vstavka-Core-Tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
var results = new List<(string Name, Exception? Error)>();
// DPAPI on Windows; elsewhere the same AES-GCM protector that macOS and Linux use, with an in-memory key.
IHistoryProtector protector = OperatingSystem.IsWindows() ? new WindowsHistoryProtector() : new AesGcmHistoryProtector(new MemoryKeyStore());

try
{
    Run("Defaults and content classification", () =>
    {
        var settings = new AppSettings();
        Check(settings.SoundEnabled && settings.PasteSoundEnabled && settings.StartAtLogin, "Requested defaults are enabled.");
        Check(settings.Volume == 0.45 && settings.RetentionDays == 30 && settings.MaxItems == 500 && settings.Hotkey == "Ctrl+Shift+V", "Expected defaults open history with Ctrl+Shift+V.");
        Check(ClipItem.DetectTextKind("https://example.com/path") == ClipKind.Link, "HTTPS link recognized.");
        Check(ClipItem.DetectTextKind("#aAcCfF") == ClipKind.Color && ClipItem.DetectTextKind("#abc") == ClipKind.Color, "Hex colors recognized.");
        Check(ClipItem.DetectTextKind("#nothex") == ClipKind.Text && ClipItem.DetectTextKind("file:///c:/private") == ClipKind.Text, "Other values are text.");
        Check(new ClipItem { Text = "\r\nA useful line\r\nAnother" }.Title == "A useful line", "Title ignores empty opening lines.");
        Check(new ClipItem { Kind = ClipKind.Link, Text = "https://example.com/path" }.Title == "example.com", "URL title uses host.");
    });

    Run("Legacy encrypted settings migrate without losing history or preferences", () =>
    {
        var cases = new (string? Saved, string Expected)[]
        {
            ("Ctrl+Shift+V", "Ctrl+Shift+V"), (" ctrl+shift+v ", "Ctrl+Shift+V"),
            (null, "Ctrl+Shift+V"), (" ", "Ctrl+Shift+V"), ("unknown", "Ctrl+Shift+V"),
            ("Alt+V", "Alt+V"), (" ctrl+alt+v ", "Ctrl+Alt+V")
        };
        foreach (var (saved, expected) in cases)
        {
            var directory = Path.Combine(testRoot, "legacy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var item = Text("Synthetic formatted-code sample");
            item.IsPinned = true;
            // Write an actual old-format encrypted document, bypassing today's save normalization.
            var document = JsonSerializer.SerializeToUtf8Bytes(new
            {
                Version = 1,
                Settings = new { Hotkey = saved, Volume = 0.63, Language = "ru", StartAtLogin = false, ExcludedApps = new[] { "SyntheticExcluded" } },
                Items = new[] { item }
            });
            var encrypted = protector.Protect(document);
            File.WriteAllBytes(Path.Combine(directory, "history.vstavka"), encrypted);
            var restored = NewStore(directory);
            restored.Load();
            Check(restored.LoadWarning is null && restored.Settings.Hotkey == expected, "Old settings load with a supported non-conflicting shortcut.");
            Check(restored.Items.Single().Id == item.Id && restored.Items[0].Text == item.Text && restored.Items[0].IsPinned, "Migration preserves stored content and pins.");
            Check(restored.GetSourceIcon(item.SourceApp).IsEmpty, "A legacy document without SourceIcons loads without icons.");
            Check(restored.Collections.Count == 0 && restored.Items[0].CollectionId is null, "Legacy documents load as uncollected history.");
            Check(restored.Settings.Volume == 0.63 && restored.Settings.Language == "ru" && !restored.Settings.StartAtLogin && restored.Settings.ExcludedApps.Single() == "SyntheticExcluded", "Other preferences survive migration.");
            restored.Save();
            var restarted = NewStore(directory);
            restarted.Load();
            Check(restarted.LoadWarning is null && restarted.Settings.Hotkey == expected && restarted.Items.Single().Id == item.Id, "Migrated shortcut and history survive a save and restart.");
        }
    });

    Run("Collections roundtrip in encrypted history and invalid metadata is discarded", () =>
    {
        var directory = Path.Combine(testRoot, "collection-roundtrip");
        var store = NewStore(directory);
        var valid = store.CreateCollection("  Prompts  ", "CollectionViolet").Collection!;
        var other = store.CreateCollection("Work", "CollectionTeal").Collection!;
        var item = Text("Synthetic prompt");
        var orphan = Text("Synthetic orphan");
        store.Add(item);
        store.Add(orphan);
        store.AssignCollection(item.Id, valid.Id);
        store.AssignCollection(orphan.Id, other.Id);
        Check(store.RenameCollection(valid.Id, "  Saved prompts  ") == CollectionEditError.None &&
            store.SetCollectionColor(valid.Id, "CollectionRose"), "Rename and color use the same collection identity.");
        store.Save();
        var loaded = NewStore(directory);
        loaded.Load();
        Check(loaded.LoadWarning is null && loaded.Collections.SequenceEqual(store.Collections) &&
            loaded.Search("prompt", collectionId: valid.Id).Single().Id == item.Id && loaded.Search("").Count == 0,
            "Metadata, creation order and memberships roundtrip and the general search excludes collections.");

        var broken = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version = 1, Settings = store.Settings, Items = store.Items,
            Collections = new object[] { other with { Color = "unknown" }, new { Id = 7 }, store.Collections[0], valid }
        });
        File.WriteAllBytes(Path.Combine(directory, "history.vstavka"), protector.Protect(broken));
        loaded.Load();
        Check(loaded.LoadWarning is null && loaded.Collections.Single() == store.Collections[0] && loaded.Items.Count == 2 &&
            loaded.Items.Single(x => x.Id == item.Id).CollectionId == valid.Id && loaded.Search("").Single().Id == orphan.Id,
            "Invalid and duplicate collections are skipped and orphaned references return to general history.");
    });

    Run("Collection protection covers age, count, deduplication, clear, and source icons", () =>
    {
        var store = Store("collection-retention");
        var prompts = store.CreateCollection("Prompts", "CollectionBlue").Collection!;
        var work = store.CreateCollection("Work", "CollectionGreen").Collection!;
        var saved = Text("kept in collection");
        var expired = Text("expires after removal");
        var pin = Text("separate pin");
        pin.IsPinned = true;
        store.Add(saved);
        store.Add(expired);
        store.Add(pin);
        store.Add(Text("general entry"));
        store.AssignCollection(saved.Id, prompts.Id);
        store.AssignCollection(expired.Id, work.Id);
        saved.CapturedAt = expired.CapturedAt = DateTimeOffset.UtcNow.AddDays(-60);
        store.Settings.RetentionDays = 1;
        store.Settings.MaxItems = 1;
        store.ApplyRetention();
        Check(store.Items.Select(x => x.Id).ToHashSet().SetEquals(new[] { saved.Id, expired.Id, pin.Id }) &&
            !store.Add(Text("another general entry")) && store.LastRejectionReason is not null, "Collections and pins both survive retention and count against the history limit.");
        Check(store.AssignCollection(expired.Id, null) && store.Items.All(x => x.Id != expired.Id), "An expired item is pruned when uncollected.");
        var copied = Text(saved.Text);
        copied.SourceApp = "SyntheticNewSource";
        Check(!store.Add(copied) && store.LastRejectionReason is null && saved.CollectionId == prompts.Id &&
            saved.CapturedAt == copied.CapturedAt && saved.SourceApp == copied.SourceApp, "Recopy refreshes the original item and keeps its membership.");
        Check(store.SetSourceIcon(saved.SourceApp, SolidRedIcon()), "The collection item can store a source icon.");

        store.Settings.MaxItems = 10;
        store.Add(Text("remove on regular clear"));
        store.Clear();
        Check(store.Items.Count == 2 && store.Collections.Count == 2, "Regular clear keeps pins and collection items.");
        Check(store.DeleteCollection(prompts.Id) && store.Items.Single().Id == pin.Id && store.GetSourceIcon(saved.SourceApp).IsEmpty,
            "Deleting a collection removes its items and unused source icons.");
        store.AssignCollection(pin.Id, work.Id);
        store.Clear(includePinned: true);
        Check(store.Items.Count == 0 && store.Collections.Single() == work, "Delete all empties the surviving collections.");
    });

    Run("Collection names and creation count have bounded validation", () =>
    {
        var store = Store("collection-limits");
        const string color = "CollectionSlate";
        var maxName = new string('a', HistoryStore.MaxCollectionNameLength);
        Check(store.CreateCollection("   ", color).Error == CollectionEditError.EmptyName &&
            store.CreateCollection(maxName + "b", color).Error == CollectionEditError.NameTooLong &&
            store.CreateCollection("Unknown color", "invalid").Error == CollectionEditError.InvalidColor,
            "Empty, oversized, and unknown-color collections are rejected.");
        var first = store.CreateCollection("  " + maxName + "  ", color).Collection!;
        Check(first.Name == maxName && store.RenameCollection(first.Id, maxName + "b") == CollectionEditError.NameTooLong,
            "The trimmed maximum name is accepted and renames use the same limit.");
        for (int i = 1; i < HistoryStore.MaxCollections; i++)
            Check(store.CreateCollection($"Collection {i}", color).Error == CollectionEditError.None, "Creation succeeds below the limit.");
        Check(store.Collections.Count == 50 && store.CreateCollection("One too many", color).Error == CollectionEditError.Limit &&
            store.Collections[0] == first, "The limit leaves the existing collections in creation order.");
    });

    Run("Source icons survive encrypted saves, replace case-insensitively, and follow retention", () =>
    {
        var store = Store("source-icon-roundtrip");
        var first = Text("first icon reference");
        var second = Text("second icon reference");
        second.SourceApp = "SYNTHETICTEST";
        store.Add(first);
        store.Add(second);
        Check(store.SetSourceIcon(first.SourceApp, SolidRedIcon()), "A referenced source can receive an icon.");
        var blue = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNgYPj/HwADAgH/5ncLrgAAAABJRU5ErkJggg==");
        Check(store.SetSourceIcon(second.SourceApp, blue), "A fresh icon replaces the stored one regardless of source casing.");
        store.Save();
        store.Load();
        Check(store.LoadWarning is null && store.Items.Count == 2 && store.GetSourceIcon("synthetictest").Span.SequenceEqual(blue),
            "The replacement and both references survive an encrypted round trip.");

        store.Remove(first.Id);
        Check(!store.GetSourceIcon(first.SourceApp).IsEmpty, "A remaining reference with different casing retains the icon.");
        store.Items.Single().CapturedAt = DateTimeOffset.UtcNow.AddDays(-31);
        store.ApplyRetention();
        Check(store.Items.Count == 0 && store.GetSourceIcon(first.SourceApp).IsEmpty, "Retention removes the icon with its last reference.");
        store.Save();
        store.Load();
        Check(store.LoadWarning is null && store.GetSourceIcon(first.SourceApp).IsEmpty, "The pruned icon no longer returns on reload.");
    });

    Run("Malformed, oversized and excess stored icons leave history intact", () =>
    {
        var directory = Path.Combine(testRoot, "invalid-icons");
        Directory.CreateDirectory(directory);
        var valid = SolidRedIcon();
        var corrupt = (byte[])valid.Clone();
        corrupt[^5] ^= 1;
        var icons = new Dictionary<string, object>
        {
            ["bad-base64"] = "%%%",
            ["bad-value"] = new { nested = 42 },
            ["bad-png"] = corrupt,
            ["oversized"] = new byte[HistoryStore.MaxSourceIconBytes + 1]
        };
        for (int i = 0; i <= HistoryStore.MaxSourceIcons; i++) icons[$"icon-{i}"] = valid;
        var items = icons.Keys.Select(source => new ClipItem { Text = source, SourceApp = source }).ToArray();
        var document = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version = 1, Settings = new AppSettings { MaxItems = items.Length }, Items = items, SourceIcons = icons
        });
        File.WriteAllBytes(Path.Combine(directory, "history.vstavka"), protector.Protect(document));
        var store = NewStore(directory);
        store.Load();
        Check(store.LoadWarning is null && store.Items.Count == items.Length, "Invalid icons leave the full history readable.");
        Check(store.Items.Count(item => !store.GetSourceIcon(item.SourceApp).IsEmpty) == HistoryStore.MaxSourceIcons &&
            icons.Keys.Take(4).All(source => store.GetSourceIcon(source).IsEmpty) &&
            store.GetSourceIcon($"icon-{HistoryStore.MaxSourceIcons}").IsEmpty, "Only the bounded set of valid icons is loaded.");
    });

    Run("Exact deduplication retains identity and pins; order follows recency", () =>
    {
        var store = Store("dedup");
        var first = Text("original", minutesAgo: 10);
        var second = Text("recent", minutesAgo: 2);
        Check(store.Add(first) && store.Add(second), "Initial entries accepted.");
        store.TogglePin(first.Id);
        Check(store.Items[0].Id == first.Id, "Pin ranks above newer entry.");
        var duplicate = Text("original");
        duplicate.SourceApp = "synthetic-new-source";
        Check(!store.Add(duplicate) && store.LastRejectionReason is null, "Exact duplicate indicated without rejection.");
        Check(store.Items.Count == 2 && store.Items[0].Id == first.Id && store.Items[0].IsPinned, "Dedup preserves ID and pin.");
        Check(store.Items[0].SourceApp == "synthetic-new-source", "Dedup updates source.");
        store.TogglePin(first.Id);
        Check(store.Items[0].Id == first.Id, "Recopied item becomes most recent.");
        Check(store.Add(Text("Original")), "Case differences in copied text remain distinct.");
        var collision = Text("Different contents");
        collision.Id = first.Id;
        Check(store.Add(collision) && store.Items.Select(x => x.Id).Distinct().Count() == 4, "ID collision is repaired.");
    });

    Run("Deduplication observes mutable content and preserves UTF-8 equivalence", () =>
    {
        var store = Store("mutable-dedup");
        var text = Text("before edit");
        store.Add(text);
        text.Text = "after edit";
        Check(store.Add(Text("before edit")) && !store.Add(Text("after edit")), "Text edits immediately change duplicate identity.");
        Check(store.Add(new ClipItem { Kind = ClipKind.Link, Text = "after edit" }), "Different content kinds stay distinct.");

        var files = new ClipItem { Kind = ClipKind.Files, Files = [@"C:\Synthetic\before.txt", @"C:\Synthetic\second.txt"] };
        store.Add(files);
        files.Files[0] = @"C:\Synthetic\changed.txt";
        Check(!store.Add(new ClipItem { Kind = ClipKind.Files, Files = [@"c:\synthetic\CHANGED.TXT", @"c:\synthetic\SECOND.TXT"] }),
            "In-place file-array edits and case normalization remain visible.");
        Check(store.Add(new ClipItem { Kind = ClipKind.Files, Files = files.Files.Reverse().ToArray() }), "File ordering remains significant.");

        var image = new ClipItem { Kind = ClipKind.Image, ImageBytes = [1, 2, 3, 4] };
        store.Add(image);
        image.ImageBytes = [4, 3, 2, 1];
        Check(store.Add(new ClipItem { Kind = ClipKind.Image, ImageBytes = [1, 2, 3, 4] }) &&
              !store.Add(new ClipItem { Kind = ClipKind.Image, ImageBytes = [4, 3, 2, 1] }), "Replacing image contents changes duplicate identity.");

        var samples = new[] { "plain", "PLAIN", "straße", "STRAẞE", "i", "ı", "İ", "Σ", "ς", "K", "k", "\ud800", "\ud801", "\udc00", "\ufffd", "\ud83d\ude42", "\ud800\ufffd", "\ufffd\udc00" };
        foreach (var kind in new[] { ClipKind.Text, ClipKind.Files })
        foreach (var left in samples)
        foreach (var right in samples)
        {
            var pair = Store("utf8-dedup");
            var first = kind == ClipKind.Files ? new ClipItem { Kind = kind, Files = [left] } : Text(left);
            var next = kind == ClipKind.Files ? new ClipItem { Kind = kind, Files = [right] } : Text(right);
            var firstValue = kind == ClipKind.Files ? left.ToUpperInvariant() : left;
            var nextValue = kind == ClipKind.Files ? right.ToUpperInvariant() : right;
            var equal = Encoding.UTF8.GetBytes(firstValue).AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(nextValue));
            Check(pair.Add(first), "Synthetic Unicode entry is valid.");
            Check(pair.Add(next) == !equal && pair.LastRejectionReason is null,
                "Capture compares file paths case-insensitively and treats invalid Unicode as its UTF-8 replacement.");
        }

        var edited = Store("duplicate-edit");
        edited.Add(Text("\ud800"));
        var changed = Text("distinct until edited");
        edited.Add(changed);
        changed.Text = "\ufffd";
        try
        {
            edited.Save();
            throw new InvalidOperationException("Saving accepted text that capture considers duplicate content.");
        }
        catch (InvalidDataException) { }
    });

    Run("Titles retain newline and whitespace behavior for large clipboard text", () =>
    {
        var samples = new[] { "", "\r\n", "\r\nHello\nWorld", " \nSecond", "\t\r\nSecond", "\u2003Title\u2003\nSecond", new string('x', 80), new string('x', 81), "First\rSecond\nThird", "\n\r\n" + new string('x', 100) + string.Concat(Enumerable.Repeat("\nSynthetic continuation", 10_000)) };
        foreach (var value in samples)
        {
            var firstLine = value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? string.Empty;
            var expected = firstLine.Length <= 80 ? firstLine : firstLine[..80] + "…";
            Check(new ClipItem { Text = value }.Title == expected, "Title shows the same trimmed first nonempty line and ellipsis.");
        }
    });

    Run("Retention removes expired/unpinned entries and honors count", () =>
    {
        var store = Store("retention");
        store.Settings.RetentionDays = 0;
        store.Settings.MaxItems = 3;
        var oldPin = Text("old pin");
        oldPin.CapturedAt = DateTimeOffset.UtcNow.AddDays(-365);
        oldPin.IsPinned = true;
        var old = Text("old");
        old.CapturedAt = DateTimeOffset.UtcNow.AddDays(-31);
        store.Add(oldPin);
        store.Add(old);
        store.Add(Text("recent", 3));
        store.Settings.RetentionDays = 30;
        store.ApplyRetention();
        Check(store.Items.Count == 2 && store.Items.Any(x => x.Id == oldPin.Id) && store.Items.All(x => x.Id != old.Id), "Age retention exempts pinned history.");
        store.Add(Text("newer", 2));
        store.Add(Text("newest", 1));
        Check(store.Items.Count == 3 && store.Items.All(x => x.Text != "recent"), "Oldest unpinned entry evicted at count limit.");
        store.Clear();
        Check(store.Items.Count == 1 && store.Items[0].Id == oldPin.Id, "Default clear preserves pins.");
        store.Settings.MaxItems = 1;
        Check(!store.Add(Text("new item with no room")) && store.LastRejectionReason is not null,
            "A pin at the configured item limit blocks capture with a reason.");
        store.Settings.MaxItems = 0;
        Check(!store.Add(Text("old pin")) && store.LastRejectionReason is null && store.Items[0].Id == oldPin.Id,
            "Recopying the pin still refreshes it after an invalid limit is normalized.");
        var secondPin = Text("second pin");
        secondPin.IsPinned = true;
        Check(store.Add(secondPin) && store.Items.Count == 2, "Pinned items can exceed the configured item limit.");
        store.Clear(includePinned: true);
        Check(store.Items.Count == 0, "Explicit full clear removes pins.");
    });

    Run("Search combines text, source, files, type, and pinned filters", () =>
    {
        var store = Store("search");
        var russian = Text("Привет проекту Вставка");
        russian.SourceApp = "Notepad";
        russian.IsPinned = true;
        store.Add(russian);
        store.Add(new ClipItem { Kind = ClipKind.Link, Text = "https://example.com/docs", SourceApp = "Browser" });
        store.Add(new ClipItem { Kind = ClipKind.Files, Files = [@"C:\Synthetic\Design.pdf", @"C:\Synthetic\Report.txt"], SourceApp = "Explorer" });
        Check(store.Search("ПРИВЕТ notepad").Single().Id == russian.Id, "Terms combine across source and case-insensitive Unicode text.");
        Check(store.Search("design", ClipKind.Files).Count == 1, "File path search.");
        Check(store.Search("  design\tEXPLORER  ").Count == 1, "Whitespace-separated terms combine file paths and source.");
        Check(store.Search("design missing").Count == 0, "Every term is required.");
        Check(store.Search("", ClipKind.Link).Count == 1 && store.Search("", pinnedOnly: true).Count == 1, "Type and pin filters.");
    Check(store.Search(Vstavka.Core.Localization.Strings.Get("Link")+" example").Count == 1, "Localized type label search.");
        store.Remove(russian.Id);
        Check(store.Search("привет").Count == 0, "Removal is reflected immediately.");
    });

    Run("Encrypted save/load roundtrip and atomic replacement", () =>
    {
        var directory = Path.Combine(testRoot, "encrypted");
        var store = NewStore(directory);
        const string marker = "SYNTHETIC-VSTAVKA-ROUNDTRIP-CONTENT-5708";
        var text = Text(marker);
        text.IsPinned = true;
        store.Add(text);
        store.Add(new ClipItem { Kind = ClipKind.Image, ImageBytes = [137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3, 4] });
        store.Add(new ClipItem { Kind = ClipKind.Files, Files = [@"C:\Synthetic\invoice.pdf"] });
        store.Settings.Volume = 0.72;
        store.Settings.StartAtLogin = false;
        store.Save();
        var storagePath = Path.Combine(directory, "history.vstavka");
        var firstCiphertext = File.ReadAllBytes(storagePath);
        Check(!Contains(firstCiphertext, Encoding.UTF8.GetBytes(marker)), "Storage does not contain plaintext test marker.");
        Check(!Contains(firstCiphertext, Encoding.Unicode.GetBytes(marker)), "Storage does not contain UTF-16 plaintext marker.");
        Check(firstCiphertext[0] != (byte)'{', "Storage is not JSON plaintext.");
        var reloaded = NewStore(directory);
        reloaded.Load();
        Check(reloaded.LoadWarning is null && reloaded.Items.Count == 3, "All item kinds load from DPAPI storage.");
        Check(reloaded.Items.Single(x => x.Kind == ClipKind.Text).Text == marker && reloaded.Items[0].IsPinned, "Text and pin preserved.");
        Check(reloaded.Items.Single(x => x.Kind == ClipKind.Image).ImageBytes!.SequenceEqual(store.Items.Single(x => x.Kind == ClipKind.Image).ImageBytes!), "PNG bytes roundtrip.");
        Check(reloaded.Settings.Volume == 0.72 && !reloaded.Settings.StartAtLogin, "Settings roundtrip.");
        reloaded.Add(Text("second atomic save"));
        reloaded.Save();
        var final = NewStore(directory);
        final.Load();
        Check(final.Items.Count == 4 && final.LoadWarning is null, "Existing file replaced successfully.");
        Check(!File.ReadAllBytes(storagePath).SequenceEqual(firstCiphertext), "Replacement contains new encrypted data.");
        Check(Directory.GetFiles(directory).Length == 1, "No leftover temporary or plaintext files.");
    });

    Run("Corrupt ciphertext is quarantined byte-for-byte, never silently replaced", () =>
    {
        var directory = Path.Combine(testRoot, "corrupt");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "history.vstavka");
        var corruptBytes = Encoding.UTF8.GetBytes("synthetic-invalid-history-for-recovery-test");
        File.WriteAllBytes(path, corruptBytes);
        var store = NewStore(directory);
        store.Load();
        Check(!store.SavingBlocked && store.LoadWarning is not null && store.Items.Count == 0,
            "Successful quarantine reports corruption and leaves the replacement store usable.");
        Check(!File.Exists(path), "Unreadable active file moved away before any replacement.");
        var quarantine = Directory.GetFiles(directory, "history.recovery-*.vstavka").Single();
        Check(File.ReadAllBytes(quarantine).SequenceEqual(corruptBytes), "Quarantine retains exact original bytes.");
        store.Add(Text("synthetic replacement"));
        store.Save();
        Check(File.Exists(path) && File.ReadAllBytes(quarantine).SequenceEqual(corruptBytes), "New history leaves recovery file untouched.");
        var restored = NewStore(directory);
        restored.Load();
        Check(restored.LoadWarning is null && restored.Items.Count == 1, "New encrypted store operates after quarantine.");
    });

    Run("A directory at the history path blocks saving and produces a warning", () =>
    {
        var directory = Path.Combine(testRoot, "history-path-blocked");
        var path = Path.Combine(directory, "history.vstavka");
        Directory.CreateDirectory(path);
        var store = NewStore(directory);
        store.Load();
        Check(store.SavingBlocked && store.LoadWarning is not null && store.Items.Count == 0,
            "The caller can distinguish unavailable settings from a new or recovered profile.");

        store.Add(Text("synthetic temporary history"));
        Directory.Delete(path);
        try
        {
            store.Save();
            throw new InvalidOperationException("Saving should stay blocked until history can be reloaded.");
        }
        catch (IOException) { }
        Check(!File.Exists(path), "Saving did not create a replacement for history whose contents could not be read.");
        store.Load();
        Check(!store.SavingBlocked && store.LoadWarning is null, "Reloading an available profile clears the failure.");
    });

    Run("Tampered encrypted payload is rejected and preserved", () =>
    {
        var directory = Path.Combine(testRoot, "tampered");
        var store = NewStore(directory);
        store.Add(Text("synthetic integrity proof"));
        store.Save();
        var path = Path.Combine(directory, "history.vstavka");
        var bytes = File.ReadAllBytes(path);
        bytes[^1] ^= 0x5a;
        File.WriteAllBytes(path, bytes);
        var readback = NewStore(directory);
        readback.Load();
        Check(readback.Items.Count == 0 && readback.LoadWarning is not null, "Tamper detected.");
        Check(File.ReadAllBytes(Directory.GetFiles(directory, "history.recovery-*.vstavka").Single()).SequenceEqual(bytes), "Tampered original retained.");
    });

    Run("An invalid edited item cannot replace an existing valid saved history", () =>
    {
        var directory = Path.Combine(testRoot, "invalid-edit");
        var store = NewStore(directory);
        var original = Text("synthetic valid original");
        store.Add(original);
        store.Save();
        var path = Path.Combine(directory, "history.vstavka");
        var originalBytes = File.ReadAllBytes(path);
        original.ImageBytes = [1, 2, 3]; // Invalid: a text entry cannot own image bytes.
        var rejected = false;
        try { store.Save(); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Invalid mutable model snapshot rejected before persistence.");
        Check(File.ReadAllBytes(path).SequenceEqual(originalBytes), "Prior saved history stays byte-for-byte unchanged.");
        var readback = NewStore(directory);
        readback.Load();
        Check(readback.LoadWarning is null && readback.Items.Single().Text == "synthetic valid original", "Last good history remains recoverable.");
    });

    Run("Async saves snapshot mutable items, file arrays, and settings before returning", () =>
    {
        var directory = Path.Combine(testRoot, "async-snapshot");
        var store = NewStore(directory);
        var item = Text("before queued snapshot");
        var files = new ClipItem { Kind = ClipKind.Files, Files = [@"C:\Synthetic\before.txt"] };
        store.Add(item);
        store.Add(files);
        // A realistic image write keeps the FIFO busy while later snapshots are queued.
        store.Add(new ClipItem { Kind = ClipKind.Image, ImageBytes = new byte[HistoryStore.MaxImageBytes] });
        var first = store.SaveAsync();
        item.Text = "captured second snapshot";
        files.Files[0] = @"C:\Synthetic\captured.txt";
        store.Settings.Volume = 0.63;
        store.Settings.ExcludedApps = ["CapturedApp"];
        var second = store.SaveAsync();
        item.Text = "changed after save call";
        item.IsPinned = true;
        files.Files[0] = @"C:\Synthetic\changed.txt";
        store.Settings.Volume = 0.11;
        store.Settings.ExcludedApps[0] = "ChangedApp";
        Task.WhenAll(first, second).GetAwaiter().GetResult();
        var readback = NewStore(directory);
        readback.Load();
        Check(readback.Items.Single(x => x.Kind == ClipKind.Text).Text == "captured second snapshot", "Item text belongs to the call-time snapshot.");
        Check(!readback.Items.Single(x => x.Kind == ClipKind.Text).IsPinned, "Later pin mutations are isolated.");
        Check(readback.Items.Single(x => x.Kind == ClipKind.Files).Files.Single() == @"C:\Synthetic\captured.txt", "File-array mutations are isolated.");
        Check(readback.Settings.Volume == 0.63 && readback.Settings.ExcludedApps.Single() == "CapturedApp", "Settings and exclusion-array mutations are isolated.");
    });

    Run("FIFO async saves followed by synchronous exit save preserve the latest snapshot", () =>
    {
        var directory = Path.Combine(testRoot, "async-exit");
        var store = NewStore(directory);
        var item = Text("initial");
        store.Add(item);
        store.Add(new ClipItem { Kind = ClipKind.Image, ImageBytes = new byte[HistoryStore.MaxImageBytes] });
        var writes = new List<Task>();
        for (var index = 0; index < 6; index++)
        {
            item.Text = $"queued snapshot {index}";
            store.Settings.Volume = index / 10d;
            writes.Add(store.SaveAsync());
        }
        item.Text = "final synchronous exit snapshot";
        store.Settings.Volume = 0.91;
        var originalContext = SynchronizationContext.Current;
        try
        {
            // A worker must never require a callback to the blocked UI/caller context.
            SynchronizationContext.SetSynchronizationContext(new RejectingSynchronizationContext());
            store.Save();
        }
        finally { SynchronizationContext.SetSynchronizationContext(originalContext); }
        Check(writes.All(x => x.IsCompletedSuccessfully), "Synchronous save completed all earlier queued saves first.");
        var readback = NewStore(directory);
        readback.Load();
        Check(readback.Items.Single(x => x.Kind == ClipKind.Text).Text == "final synchronous exit snapshot" && readback.Settings.Volume == 0.91,
            "No older queued snapshot can overwrite the final synchronous state.");
        Check(Directory.GetFiles(directory).Length == 1, "Queued saves leave one encrypted file and no temporary files.");
    });

    Run("A failed async save does not poison subsequent queued persistence", () =>
    {
        var directory = Path.Combine(testRoot, "async-recovery");
        File.WriteAllText(directory, "synthetic file blocks the requested directory");
        var store = NewStore(directory);
        var item = Text("before temporary failure");
        store.Add(item);
        var failed = store.SaveAsync();
        var rejected = false;
        try { failed.GetAwaiter().GetResult(); }
        catch (IOException) { rejected = true; }
        Check(rejected && failed.IsFaulted, "Failed save reports its IO error to its caller.");
        File.Delete(directory);
        item.Text = "saved after failure";
        store.SaveAsync().GetAwaiter().GetResult();
        var readback = NewStore(directory);
        readback.Load();
        Check(readback.LoadWarning is null && readback.Items.Single().Text == "saved after failure", "A later valid save succeeds after an earlier task faults.");
    });

    Run("Load waits for a pending async write before reading history", () =>
    {
        var store = Store("async-load");
        var item = Text("snapshot before reload");
        store.Add(item);
        var pending = store.SaveAsync();
        item.Text = "unsaved live change";
        store.Load();
        Check(pending.IsCompletedSuccessfully && store.LoadWarning is null && store.Items.Single().Text == "snapshot before reload",
            "Reload waits for pending persistence and restores the saved snapshot.");
    });

    Run("Input limits and password-manager exclusions reject capture", () =>
    {
        var store = Store("limits");
        Check(!store.Add(Text("")) && store.LastRejectionReason is not null, "Empty content rejected.");
        Check(!store.Add(Text(new string('x', HistoryStore.MaxTextLength + 1))), "Oversized text rejected.");
        Check(!store.Add(new ClipItem { Kind = ClipKind.Image, ImageBytes = new byte[HistoryStore.MaxImageBytes + 1] }), "Oversized image rejected.");
        var excluded = Text("synthetic exclusion probe");
        excluded.SourceApp = @"C:\Synthetic\1Password.exe";
        Check(!store.Add(excluded) && store.LastRejectionReason is not null, "Excluded process name recognized through executable path.");
        Check(store.Items.Count == 0, "Rejected inputs never enter history.");

        store.Settings.ExcludedApps = [@" C:\Synthetic\Example.App.EXE "];
        Check(!store.Add(new ClipItem { Text = "excluded", SourceApp = "example.app" }) && store.LastRejectionReason is not null,
            "An exclusion accepts an executable path and matches a process name with dots.");
        Check(store.Add(new ClipItem { Text = "allowed", SourceApp = "Example.Other" }),
            "Different process names are allowed even when they have the same prefix before a dot.");
    });

    Run("AES-GCM protection authenticates history and needs its stored key", () =>
    {
        var keys = new MemoryKeyStore();
        var aes = new AesGcmHistoryProtector(keys);
        var clear = Encoding.UTF8.GetBytes("synthetic history payload");
        var first = aes.Protect(clear);
        var key = keys.TryLoadKey();
        var second = aes.Protect(clear);
        Check(key is { Length: 32 } && !second.SequenceEqual(first) && keys.TryLoadKey()!.SequenceEqual(key),
            "One random key is created and reused; every payload gets a fresh nonce.");
        Check(aes.Unprotect(first).SequenceEqual(clear) && !Contains(first, clear), "Payloads roundtrip and never contain the clear text.");
        foreach (var index in new[] { 0, 20, first.Length - 1 })
        {
            var damaged = (byte[])first.Clone();
            damaged[index] ^= 0x5a;
            Check(Throws<System.Security.Cryptography.CryptographicException>(() => aes.Unprotect(damaged)), $"A changed byte at {index} is rejected.");
        }
        Check(Throws<System.Security.Cryptography.CryptographicException>(() => new AesGcmHistoryProtector(new MemoryKeyStore()).Unprotect(first)),
            "A missing key is reported as unreadable history.");
        var otherKeys = new MemoryKeyStore();
        otherKeys.SaveKey(new byte[32]);
        Check(Throws<System.Security.Cryptography.CryptographicException>(() => new AesGcmHistoryProtector(otherKeys).Unprotect(first)),
            "Another key cannot open the history.");

        var directory = Path.Combine(testRoot, "aes-store");
        var store = new HistoryStore(directory, aes);
        store.Add(Text("synthetic AES history entry"));
        store.Save();
        var readback = new HistoryStore(directory, new AesGcmHistoryProtector(keys));
        readback.Load();
        Check(readback.Items.Single().Text == "synthetic AES history entry", "A history store saved with AES-GCM reloads with the same key.");
        var locked = new HistoryStore(directory, new AesGcmHistoryProtector(new MemoryKeyStore()));
        locked.Load();
        Check(locked.Items.Count == 0 && locked.LoadWarning is not null && Directory.GetFiles(directory, "history.recovery-*.vstavka").Length == 1,
            "Without its key the file is kept aside instead of being overwritten.");
    });

    Run("100 MiB image budget evicts old images and protects pins and collection items", () =>
    {
        var store = Store("image-budget");
        for (var index = 0; index < 11; index++)
        {
            var bytes = new byte[HistoryStore.MaxImageBytes];
            bytes[0] = (byte)index;
            Check(store.Add(new ClipItem { Kind = ClipKind.Image, ImageBytes = bytes, CapturedAt = DateTimeOffset.UtcNow.AddMinutes(index - 20) }), "Budget accepts newest image by evicting old unpinned content.");
        }
        Check(store.Items.Count == 10 && store.Items.Sum(x => (long)x.ImageBytes!.Length) == HistoryStore.MaxTotalImageBytes, "Exact total budget enforced.");
        Check(store.Items.All(x => x.ImageBytes![0] != 0), "Oldest image evicted.");
        var pictures = store.CreateCollection("Pictures", "CollectionBlue").Collection!;
        foreach (var item in store.Items.Take(5).ToArray())
            store.TogglePin(item.Id);
        foreach (var item in store.Items.Where(x => !x.IsPinned).ToArray())
            store.AssignCollection(item.Id, pictures.Id);
        Check(!store.Add(new ClipItem { Kind = ClipKind.Image, ImageBytes = [255] }) && store.LastRejectionReason is not null, "Pins and collections share the image budget.");
        Check(store.Items.Count == 10 && store.Items.Count(x => x.CollectionId == pictures.Id) == 5, "All protected images are preserved.");
    });
}
finally
{
    // Only the unique directory created above is removed; no user clipboard is accessed.
    var cleanupPath = Path.GetFullPath(testRoot);
    var expectedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Vstavka-Core-Tests")) + Path.DirectorySeparatorChar;
    if (cleanupPath.StartsWith(expectedParent, StringComparison.OrdinalIgnoreCase) && Directory.Exists(cleanupPath))
        Directory.Delete(cleanupPath, true);
}

foreach (var result in results)
    Console.WriteLine(result.Error is null ? $"PASS {result.Name}" : $"FAIL {result.Name}: {result.Error.GetType().Name}: {result.Error.Message}");
Console.WriteLine($"{results.Count(x => x.Error is null)}/{results.Count} test groups passed. Synthetic data only; no user clipboard read.");
return results.Any(x => x.Error is not null) ? 1 : 0;

void Run(string name, Action test)
{
    try { test(); results.Add((name, null)); }
    catch (Exception exception) { results.Add((name, exception)); }
}

HistoryStore Store(string name) => NewStore(Path.Combine(testRoot, name));
HistoryStore NewStore(string directory) => new(directory, protector);
static ClipItem Text(string value, int minutesAgo = 0) => new() { Text = value, SourceApp = "SyntheticTest", CapturedAt = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo) };
static byte[] SolidRedIcon() => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==");
static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
static bool Contains(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle) >= 0;
static bool Throws<T>(Action action) where T : Exception
{
    try { action(); return false; }
    catch (T) { return true; }
}

sealed class MemoryKeyStore : IHistoryKeyStore
{
    private byte[]? _key;
    public byte[]? TryLoadKey() => _key?.ToArray();
    public void SaveKey(byte[] key) => _key = key.ToArray();
}

sealed class RejectingSynchronizationContext : SynchronizationContext
{
    public override void Post(SendOrPostCallback callback, object? state) => throw new InvalidOperationException("Persistence must not post back to the caller context.");
    public override void Send(SendOrPostCallback callback, object? state) => throw new InvalidOperationException("Persistence must not send to the caller context.");
}
