using System.Diagnostics;
using System.IO.Compression;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Skia;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentIcons.Avalonia;
using Vstavka.Core;
using Vstavka.Core.Localization;
using Vstavka.Desktop;
using Vstavka.Platform;

if (args.Length != 0 && (args.Length != 2 || args[0] is not ("--render" or "--readme")))
{
    Console.Error.WriteLine("Usage: Vstavka.Desktop.Tests [--render <directory> | --readme <directory>]");
    return 2;
}

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

var failures = 0;
if (args.Length == 2 && args[0] == "--readme")
{
    Run("Render README images", () => RenderReadme(Path.GetFullPath(args[1])));
    return failures == 0 ? 0 : 1;
}

Run("Category filters, search and empty results", () =>
{
    using var demo = new DemoPlatformServices();
    var history = CreateHistory(demo, seed: true);
    var panel = OpenPanel(demo, history);
    try
    {
        var cards = Find<ListBox>(panel, "Cards");
        Click(panel, Find<Button>(panel, "LinksChip"));
        Check(cards.Items.Count == 1 && ((ClipItem)cards.Items[0]!).Kind == ClipKind.Link, "Links chip filters by type.");
        Click(panel, Find<Button>(panel, "TextChip"));
        Check(cards.Items.Cast<ClipItem>().Select(item => item.Kind).ToHashSet().SetEquals([ClipKind.Text, ClipKind.Color]),
            "The Text filter includes color values.");
        Press(panel, Key.F, PhysicalKey.F, CommandModifier());
        var search = Find<TextBox>(panel, "SearchBox");
        Check(search.IsFocused, "Ctrl+F focuses search.");
        panel.KeyTextInput("246ac5");
        Flush(panel);
        Check(cards.Items.Count == 1 && ((ClipItem)cards.Items[0]!).Kind == ClipKind.Color, "Search is scoped to the active filter and is case-insensitive.");
        search.Text = "no matching content";
        Flush(panel);
        Check(cards.Items.Count == 0 && HasLabel(panel, "NoResults"), "Empty search displays its own message.");
    }
    finally { panel.Close(); }
});

Run("Keyboard selection, Enter fallback, quick paste, and Escape", () =>
{
    using var demo = new DemoPlatformServices();
    var panel = OpenPanel(demo, CreateHistory(demo, seed: true));
    try
    {
        var cards = Find<ListBox>(panel, "Cards");
        Check(cards.SelectedIndex == 0 && cards.IsKeyboardFocusWithin, "The first card starts selected and focused.");
        Press(panel, Key.Right, PhysicalKey.ArrowRight);
        Check(cards.SelectedIndex == 1, "Right selects the next card.");
        Press(panel, Key.F, PhysicalKey.F, CommandModifier());
        Press(panel, Key.Down, PhysicalKey.ArrowDown);
        Check(cards.IsKeyboardFocusWithin && cards.SelectedIndex == 1, "Down moves from search to the selected card.");
        var expected = cards.SelectedItem;
        string? feedback = null;
        panel.Feedback += text => feedback = text;
        Press(panel, Key.Enter, PhysicalKey.Enter);
        Check(ReferenceEquals(demo.LastWritten, expected) && demo.PasteRequests == 1 && !panel.IsVisible,
            "Enter writes and hides before requesting paste from the demo service.");
        Check(feedback == Strings.Get("ManualPaste"), "Failed automatic paste provides manual paste feedback.");
        panel.Open(positionOnScreen: false);
        Flush(panel);
        Press(panel, Key.D1, PhysicalKey.Digit1, CommandModifier());
        Check(ReferenceEquals(demo.LastWritten, cards.Items[0]) && demo.PasteRequests == 2 && !panel.IsVisible,
            "Ctrl+1 pastes the first filtered card.");
        panel.Open(positionOnScreen: false);
        Flush(panel);
        Press(panel, Key.Escape, PhysicalKey.Escape);
        Check(!panel.IsVisible && demo.PasteRequests == 2, "Escape hides without requesting paste.");
    }
    finally { panel.Close(); }
});

Run("Deleting a card selects the one that takes its place", () =>
{
    using var demo = new DemoPlatformServices();
    var panel = OpenPanel(demo, CreateHistory(demo, seed: true));
    try
    {
        var cards = Find<ListBox>(panel, "Cards");
        Press(panel, Key.Right, PhysicalKey.ArrowRight);
        var next = cards.Items[2];
        Press(panel, Key.Delete, PhysicalKey.Delete);
        Check(cards.SelectedIndex == 1 && ReferenceEquals(cards.SelectedItem, next) && cards.IsKeyboardFocusWithin,
            "The selection stays in the slot instead of returning to the first card.");
    }
    finally { panel.Close(); }
});

Run("A fading panel turns around when reopened, and a paste hides it", () =>
{
    using var demo = new DemoPlatformServices();
    var panel = new PanelWindow(demo, CreateHistory(demo, seed: true), animate: true) { Width = 1338 };
    panel.Open(positionOnScreen: false);
    Flush(panel);
    try
    {
        panel.Dismiss();
        Check(panel.IsVisible && !panel.IsOpen, "A dismissed panel fades before it hides.");
        panel.Open(positionOnScreen: false);
        var fade = Stopwatch.StartNew();
        PumpUntil(() =>
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            return fade.ElapsedMilliseconds > 300;
        }, "");
        Check(panel.IsVisible && panel.IsOpen, "Reopening during the fade keeps the panel on screen.");
        Press(panel, Key.Enter, PhysicalKey.Enter);
        PumpUntil(() =>
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            return !panel.IsVisible;
        }, "A paste hides the panel.");
        Check(demo.PasteRequests == 1, "The paste follows once the panel is gone.");
    }
    finally { panel.Close(); }
});

Run("Collection chips scope history and display empty collections", () =>
{
    using var demo = new DemoPlatformServices();
    var history = CreateHistory(demo, seed: true);
    var empty = history.Store.CreateCollection("Empty test collection", "CollectionRose").Collection!;
    var panel = OpenPanel(demo, history);
    try
    {
        var cards = Find<ListBox>(panel, "Cards");
        Check(cards.Items.Count == 5 && cards.Items.Cast<ClipItem>().All(item => item.CollectionId is null),
            "All shows uncollected history.");
        Click(panel, ButtonNamed(panel, "Field notes"));
        Check(cards.Items.Count == 1 && ((ClipItem)cards.Items[0]!).Text == "Pack a field notebook and a spare pencil.",
            "The collection chip shows only its own item.");
        Click(panel, ButtonNamed(panel, empty.Name));
        Check(cards.Items.Count == 0 && HasLabel(panel, "EmptyCollection"), "An empty collection has a distinct state.");
    }
    finally { panel.Close(); }
});

Run("Collection editing moves existing clips and confirms deletion", () =>
{
    using var demo = new DemoPlatformServices();
    var history = CreateHistory(demo, seed: true);
    var panel = OpenPanel(demo, history);
    try
    {
        var cards = Find<ListBox>(panel, "Cards");
        var kept = history.Store.Collections.Single();
        var item = (ClipItem)cards.Items[0]!;
        Click(panel, Find<Button>(panel, "NewCollectionButton"));
        var name = Find<TextBox>(panel, "CollectionName");
        Check(name.IsFocused, "New collection focuses its name field.");
        Press(panel, Key.Enter, PhysicalKey.Enter);
        Check(Find<TextBlock>(panel, "CollectionError").IsVisible && history.Store.Collections.Count == 1,
            "A blank name is rejected without changing collections.");
        name.Text = "Saved clips";
        Find<ComboBox>(panel, "CollectionColor").SelectedIndex = 4;
        Press(panel, Key.Enter, PhysicalKey.Enter);
        var created = history.Store.Collections.Single(collection => collection.Id != kept.Id);
        Check(created.Name == "Saved clips" && created.Color == "CollectionOrange" && cards.Items.Count == 0,
            "Creating a collection selects the new empty collection.");

        Click(panel, Find<Button>(panel, "AllChip"));
        var first = cards.ContainerFromIndex(0)!;
        var card = first.GetVisualDescendants().OfType<ClipCardView>().Single();
        first.RaiseEvent(new ContextRequestedEventArgs { RoutedEvent = InputElement.ContextRequestedEvent });
        Flush(panel);
        Check(card.ContextMenu!.IsOpen, "Keyboard context requests open the selected card's menu.");
        var assignment = card.ContextMenu!.Items.OfType<MenuItem>().Single(menu => menu.Name == "AssignCollection");
        var destination = assignment.Items.OfType<MenuItem>().Single(menu => AutomationProperties.GetName(menu) == created.Name);
        destination.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Flush(panel);
        Check(item.CollectionId == created.Id && !cards.Items.Contains(item), "Moving an item removes it from ordinary history.");

        var chip = ButtonNamed(panel, created.Name);
        Click(panel, chip);
        Check(cards.Items.Count == 1 && ReferenceEquals(cards.Items[0], item), "The assigned item is shown in its collection.");
        var rename = chip.ContextMenu!.Items.OfType<MenuItem>().Single(menu => Equals(menu.Header, Strings.Get("RenameCollection")));
        rename.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Flush(panel);
        Find<TextBox>(panel, "CollectionName").Text = "Shared snippets";
        Press(panel, Key.Enter, PhysicalKey.Enter);
        chip = ButtonNamed(panel, "Shared snippets");
        Check(item.CollectionId == created.Id && cards.Items.Count == 1, "Renaming keeps the collection identity and its selected item.");

        var collected = cards.ContainerFromIndex(0)!.GetVisualDescendants().OfType<ClipCardView>().Single();
        var remove = collected.ContextMenu!.Items.OfType<MenuItem>().Single(menu => menu.Name == "RemoveFromCollection");
        remove.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Flush(panel);
        Check(item.CollectionId is null && history.Store.Items.Contains(item) && cards.Items.Count == 0,
            "Removing a card from a collection keeps it in ordinary history.");
        history.Store.AssignCollection(item.Id, created.Id);
        history.HistoryChanged();
        Flush(panel);

        var delete = chip.ContextMenu!.Items.OfType<MenuItem>().Single(menu => Equals(menu.Header, Strings.Get("DeleteCollection")));
        delete.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Flush(panel);
        Check(Find<Button>(panel, "CancelCollectionEdit").IsFocused, "Delete focuses the safe action first.");
        Press(panel, Key.Escape, PhysicalKey.Escape);
        Check(panel.IsOpen && history.Store.Items.Contains(item), "Escape cancels deletion and keeps the panel open.");
        delete.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Flush(panel);
        Click(panel, Find<Button>(panel, "ConfirmCollectionDelete"));
        Check(history.Store.Collections.SequenceEqual([kept]) && !history.Store.Items.Contains(item) && cards.Items.Count > 0,
            "Confirming deletion removes the selected collection and its clips, and returns to history.");
    }
    finally { panel.Close(); }
});

Run("Virtualized cards, hover border, selection tint, and keyboard focus ring", () =>
{
    using var demo = new DemoPlatformServices();
    var history = CreateHistory(demo, seed: true);
    for (var i = 0; i < 30; i++)
        history.Store.Add(new ClipItem { Text = $"Synthetic card {i}" });
    var panel = OpenPanel(demo, history);
    try
    {
        var cards = Find<ListBox>(panel, "Cards");
        var first = (ListBoxItem)cards.ContainerFromIndex(0)!;
        var second = (ListBoxItem)cards.ContainerFromIndex(1)!;
        Check(cards.GetVisualDescendants().OfType<ListBoxItem>().Count() < cards.Items.Count, "Only viewport cards are realized.");
        var firstRing = Find<Border>(first, "SelectionRing");
        var secondRing = Find<Border>(second, "SelectionRing");
        var firstSurface = Find<Border>(first, "CardSurface");
        var secondSurface = Find<Border>(second, "CardSurface");
        var background = ColorOf(secondSurface.Background);
        var line = ColorOf(secondSurface.BorderBrush);
        Check(firstRing.IsVisible && firstRing.BorderThickness == new Thickness(2) && ColorOf(firstSurface.Background) != background,
            "Selected card has a tinted surface and a 2-DIP ring.");
        panel.MouseMove(Center(panel, second));
        Flush(panel);
        Check(ColorOf(secondSurface.BorderBrush) != line && ColorOf(secondSurface.Background) == background && !secondRing.IsVisible,
            "Hover changes only the neutral border.");
        var chip = Find<Button>(panel, "AllChip");
        var focusRing = Find<Border>(chip, "FocusRing");
        chip.Focus(NavigationMethod.Pointer);
        Flush(panel);
        Check(!focusRing.IsVisible, "Pointer focus keeps the button ring hidden.");
        Press(panel, Key.Tab, PhysicalKey.Tab);
        var focused = panel.FocusManager?.GetFocusedElement() as Button;
        Check(focused is not null && Find<Border>(focused, "FocusRing").IsVisible, "Keyboard focus shows the button ring.");
    }
    finally { panel.Close(); }
});

Run("RTL mirrors layout and navigation, and grants the requested permission", () =>
{
    foreach (var language in new[] { "ar", "fa", "ur" })
    {
        Strings.SetLanguage(language);
        var requests = 0;
        using var demo = new NoticeDemo
        {
            Notices = [new("accessibility-permission", "Accessibility permission is missing.", () => requests++),
                new("hidden", "Second notice.")]
        };
        var panel = OpenPanel(demo, CreateHistory(demo, seed: true));
        try
        {
            var cards = Find<ListBox>(panel, "Cards");
            var first = (ListBoxItem)cards.ContainerFromIndex(0)!;
            var second = (ListBoxItem)cards.ContainerFromIndex(1)!;
            Check(panel.FlowDirection == FlowDirection.RightToLeft && Center(panel, first).X > Center(panel, second).X,
                $"Cards read from the right in {language}.");
            Press(panel, Key.Left, PhysicalKey.ArrowLeft);
            Check(cards.SelectedIndex == 1, "Left advances through the RTL list.");
            var status = Find<TextBlock>(panel, "PlatformStatus");
            Check(status.Text == Strings.Format("AutomaticPasteOff", SetupVisuals.ShortcutInText("Ctrl+V")), "The first known platform notice is localized.");
            var grant = NoticeButtons(panel).Single();
            Check(Equals(grant.Content, Strings.Get("TurnOn")), "The accessibility button is specific and localized.");
            Click(panel, grant);
            Check(requests == 1, "The footer button invokes the notice action once.");
        }
        finally { panel.Close(); }
    }
    Strings.SetLanguage("en");
});

Run("Clipboard settings action refreshes the footer, and reopening shows new notices", () =>
{
    using var demo = new NoticeDemo();
    var requests = 0;
    demo.Notices = [new("pasteboard-access-denied", "Denied.", () =>
    {
        requests++;
        demo.Notices = [new("keychain-unavailable", "No Keychain.")];
    })];
    var panel = OpenPanel(demo, CreateHistory(demo, seed: true));
    try
    {
        Check(Find<TextBlock>(panel, "PlatformStatus").Text == Strings.Get("PasteboardAccessDenied"),
            "A denied clipboard notice is localized.");
        var settings = NoticeButtons(panel).Single();
        Check(Equals(settings.Content, Strings.Get("OpenSettings")), "The denied notice offers Open settings.");
        Click(panel, settings);
        Check(requests == 1, "The settings button invokes the notice action once.");

        var frame = new DispatcherFrame();
        DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromMilliseconds(500));
        Dispatcher.UIThread.PushFrame(frame);
        Flush(panel);
        Check(Find<TextBlock>(panel, "PlatformStatus").Text == Strings.Get("KeychainUnavailable") && !NoticeButtons(panel).Any(),
            "The pending refresh shows the new informational notice without a button.");

        panel.Dismiss();
        demo.Notices = [new("other", "Adapter-specific message.")];
        panel.Open(positionOnScreen: false);
        Flush(panel);
        Check(Find<TextBlock>(panel, "PlatformStatus").Text == "Adapter-specific message." && !NoticeButtons(panel).Any(),
            "Reopening picks up the unknown notice and shows its own message without a button.");

        panel.Dismiss();
        demo.Notices = [new("gnome-helper-restart", "Restart GNOME.", () => requests++)];
        panel.Open(positionOnScreen: false);
        Flush(panel);
        var signOut = NoticeButtons(panel).Single();
        Check(Find<TextBlock>(panel, "PlatformStatus").Text == Strings.Get("GnomeRestartNotice")
            && Equals(signOut.Content, Strings.Get("SignOut")), "The restart notice names the next action.");
        Click(panel, signOut);
        Check(requests == 2, "The panel signs out through the notice action.");
    }
    finally { panel.Close(); }
});

Run("Toolbar actions and the Settings save and clear flows", () =>
{
    using var demo = new SetupDemo("Windows");
    var history = CreateHistory(demo, seed: true);
    var windows = new DesktopWindows(demo, history, animate: false, positionPanel: false);
    windows.OpenPanel();
    try
    {
        var panel = windows.Panel;
        Flush(panel);
        Click(panel, ButtonNamed(panel, Strings.Get("Pause")));
        Check(history.Paused, "Pause toggles saving.");
        Click(panel, ButtonNamed(panel, Strings.Get("Resume")));
        Check(!history.Paused, "Resume toggles saving back on.");
        Click(panel, ButtonNamed(panel, Strings.Get("Close")));
        Check(!panel.IsVisible, "Close hides the panel.");
        windows.OpenPanel();
        Flush(panel);
        Click(panel, ButtonNamed(panel, Strings.Get("Settings")));
        var settings = windows.Settings ?? throw new InvalidOperationException("Settings did not open.");
        Check(!panel.IsVisible && settings.IsVisible, "Settings hides the panel and opens a window.");
        windows.OpenSettings();
        Check(windows.Settings == settings, "Settings has one instance.");
        Flush(settings);

        var excluded = Find<TextBox>(settings, "ExcludedAppsSetting");
        Check(Find<TextBox>(settings, "MaxItemsSetting").VerticalContentAlignment == VerticalAlignment.Center
            && excluded.MinLines == 4 && excluded.MaxLines == 4
            && ScrollViewer.GetVerticalScrollBarVisibility(excluded) == ScrollBarVisibility.Auto,
            "The item limit is centered and the exclusions have a scrollable four-line viewport.");
        var clear = Find<Button>(settings, "ClearHistory");
        clear.BringIntoView();
        Flush(settings);
        Check(ColorOf(clear.BorderBrush) == ColorOf(settings.Resources["DangerLineBrush"] as IBrush)
            && ColorOf(clear.Foreground) == ColorOf(settings.Resources["DangerBrush"] as IBrush),
            "Clear history signals its destructive action.");
        Click(settings, clear);
        var confirmation = Find<Border>(settings, "ClearConfirmation");
        Check(confirmation.IsVisible && history.Store.Items.Any(item => item.IsPinned),
            "Clear opens inline confirmation and preserves all items before confirmation.");
        var settingsScroll = Find<ScrollViewer>(settings, "SettingsScroll");
        Check(confirmation.TranslatePoint(new Point(0, confirmation.Bounds.Height), settingsScroll) is { } bottom
            && bottom.Y <= settingsScroll.Bounds.Height, "The confirmation scrolls fully into view.");
        var confirm = Find<Button>(settings, "ConfirmClear");
        Check(ColorOf(confirm.Background) == ColorOf(settings.Resources["DangerFillBrush"] as IBrush),
            "The confirmation uses a filled destructive button.");
        Click(settings, confirm);
        Check(history.Store.Items.Count == 0 && HasLabel(settings, "HistoryCleared"), "Confirmation clears even pinned items.");

        var theme = Find<ComboBox>(settings, "ThemeSetting");
        theme.SelectedIndex = 2;
        Find<ComboBox>(settings, "AccentSetting").SelectedIndex = 3;
        Find<ComboBox>(settings, "SettingsLanguage").SelectedItem = Find<ComboBox>(settings, "SettingsLanguage").Items
            .OfType<ComboBoxItem>().Single(item => Equals(item.Tag, "fr"));
        Find<TextBox>(settings, "MaxItemsSetting").Text = "42";
        Find<TextBox>(settings, "ExcludedAppsSetting").Text = "Photos\nnotes; photos";
        Check(history.Store.Settings.Theme == "system" && Strings.Language == "en", "Appearance stays unchanged while editing.");
        Click(settings, Find<Button>(settings, "SaveSettings"));
        var saved = history.Store.Settings;
        Check(windows.Settings is null && saved.Theme == "dark" && saved.Accent == "teal" && saved.Language == "fr"
            && Strings.Language == "fr" && saved.MaxItems == 42 && saved.ExcludedApps.SequenceEqual(["Photos", "notes"]) && demo.Startup.IsEnabled,
            "Save applies the edited appearance and history settings.");
    }
    finally
    {
        windows.Settings?.Close();
        windows.Panel.Close();
        Strings.SetLanguage("en");
        SetupVisuals.ApplyTheme("system");
    }
});

Run("Clear history waits for persistence and reports a failed write", () =>
{
    var directory = Directory.CreateTempSubdirectory("Vstavka.ClearTests.");
    var keys = new TestKeyStore();
    var protector = new AesGcmHistoryProtector(keys);
    using var demo = new SetupDemo("Windows") { DataDirectory = directory.FullName, HistoryProtector = protector };
    var history = CreateHistory(demo, seed: true);
    history.Store.Save();
    var settings = new SettingsWindow(demo, history, animate: false);
    settings.Show();
    try
    {
        var clear = Find<Button>(settings, "ClearHistory");
        clear.BringIntoView();
        Flush(settings);
        keys.Unavailable = true;
        Click(settings, clear);
        Click(settings, Find<Button>(settings, "ConfirmClear"));
        PumpUntil(() => HasLabel(settings, "SaveFailed"), "A failed write is visible in Settings.");
        Check(!HasLabel(settings, "HistoryCleared"), "Settings has not confirmed deletion.");

        keys.Unavailable = false;
        Check(CreateHistory(demo, seed: false).Store.Items.Count > 0, "The previous history is still on disk.");
        Click(settings, clear);
        Click(settings, Find<Button>(settings, "ConfirmClear"));
        PumpUntil(() => HasLabel(settings, "HistoryCleared"), "The successful retry confirms deletion.");
        Check(CreateHistory(demo, seed: false).Store.Items.Count == 0, "The saved history is empty.");
    }
    finally
    {
        settings.Close();
        directory.Delete(recursive: true);
    }
});

Run("Pause and Clear discard clipboard events that were already in flight", () =>
{
    using var monitor = new ClipboardMonitorDouble();
    using var demo = new SetupDemo("Windows") { Monitor = monitor };
    var history = CreateHistory(demo, seed: false);
    history.Start();
    try
    {
        history.TogglePause();
        var copiedDuringPause = DateTimeOffset.UtcNow;
        history.TogglePause();
        monitor.Publish("Copied while paused", copiedDuringPause);
        Check(history.Store.Items.Count == 0, "Resuming does not import copies that arrived late.");

        var copiedBeforeClear = DateTimeOffset.UtcNow;
        history.ClearHistoryAsync().GetAwaiter().GetResult();
        monitor.Publish("Copied before clearing", copiedBeforeClear);
        monitor.Publish("Copied after clearing", DateTimeOffset.UtcNow.AddSeconds(1));
        Check(history.Store.Items.Single().Text == "Copied after clearing", "Only new copies can repopulate cleared history.");
    }
    finally { history.Stop(); }
});

Run("Windows sound settings preview the draft and save independent preferences", () =>
{
    using var sounds = new ClipboardSoundsDouble();
    using var demo = new SetupDemo("Windows") { ClipboardSounds = sounds };
    var history = CreateHistory(demo, seed: false);
    var windows = new DesktopWindows(demo, history, animate: false, positionPanel: false);
    windows.OpenSettings();
    var settings = windows.Settings!;
    try
    {
        Flush(settings);
        Find<CheckBox>(settings, "CopySoundSetting").IsChecked = false;
        Find<Slider>(settings, "VolumeSetting").Value = 0.25;
        Find<Button>(settings, "PlayCopySound").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Find<Button>(settings, "PlayPasteSound").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(sounds.Played.SequenceEqual([(false, 0.25), (true, 0.25)]) &&
              history.Store.Settings.SoundEnabled && history.Store.Settings.Volume == 0.45,
            "The previews use the current draft value, including when its toggle is disabled, without saving.");
        Click(settings, Find<Button>(settings, "SaveSettings"));
        Check(history.Store.Settings is { SoundEnabled: false, PasteSoundEnabled: true, Volume: 0.25 } &&
              sounds.WatchCalls.All(enabled => enabled),
            "Saving the copy mute keeps keyboard paste feedback enabled and persists the slider value.");
    }
    finally
    {
        settings.Close();
        windows.Panel.Close();
    }
});

Run("Settings requests a store review once while closing and saves the version", () =>
{
    var directory = Path.Combine(Path.GetTempPath(), "Vstavka.ReviewTests." + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var protector = new AesGcmHistoryProtector(new TestKeyStore());
    var review = new ReviewDemo();
    using var demo = new SetupDemo("macOS") { DataDirectory = directory, HistoryProtector = protector, StoreReview = review };
    var history = CreateHistory(demo, seed: true);
    var windows = new DesktopWindows(demo, history, animate: false, positionPanel: false);
    var first = DateTimeOffset.UtcNow.AddDays(-4);
    review.Requesting = () => Check(Dispatcher.UIThread.CheckAccess() && windows.Settings is { IsVisible: true } && !windows.Panel.IsVisible,
        "The service is called on the UI thread before Settings disappears, with the panel hidden.");
    try
    {
        history.Store.Settings.StoreReview = new(9, first, null);
        windows.OpenSettings();
        var settings = windows.Settings!;
        Flush(settings);
        Click(settings, Find<Button>(settings, "CancelSettings"));
        Check(windows.Settings is null && review.Calls == 0, "Cancel below the threshold does not request a review.");

        history.Store.Settings.StoreReview = new(10, first, null);
        windows.OpenSettings();
        windows.OpenPanel();
        Flush(windows.Panel);
        Check(windows.Panel.IsVisible, "The panel is visible when Settings starts closing.");
        windows.Settings!.Close();
        Check(windows.Settings is null && review.Calls == 0, "An open panel prevents a review request.");
        windows.Panel.Dismiss();

        history.Store.Settings.StoreReview = new(9, first, null);
        windows.OpenSettings();
        settings = windows.Settings!;
        windows.OpenPanel();
        Flush(windows.Panel);
        Press(windows.Panel, Key.Enter, PhysicalKey.Enter);
        Check(review.Calls == 0 && history.Store.Settings.StoreReview.UseCount == 10, "A pick reaches the threshold without asking.");
        Flush(settings);
        Click(settings, Find<Button>(settings, "SaveSettings"));
        var expected = new StoreReviewState(10, first, review.AppVersion);
        Check(windows.Settings is null && review.Calls == 1 && history.Store.Settings.StoreReview == expected,
            "Save preserves the latest use and records the version immediately.");
        windows.OpenSettings();
        windows.Settings!.Close();
        Check(review.Calls == 1, "Closing Settings again does not request a review for the same version.");
        history.Stop();
        Check(CreateHistory(demo, seed: false).Store.Settings.StoreReview == expected, "Review state is available after reopening the history.");

        using var unsupported = new SetupDemo("macOS") { DataDirectory = directory, HistoryProtector = protector };
        var unsupportedHistory = CreateHistory(unsupported, seed: false);
        var unsupportedWindows = new DesktopWindows(unsupported, unsupportedHistory, animate: false, positionPanel: false);
        var eligible = expected with { LastRequestedVersion = null };
        unsupportedHistory.Store.Settings.StoreReview = eligible;
        unsupportedWindows.OpenSettings();
        unsupportedWindows.Settings!.Close();
        Check(unsupportedWindows.Settings is null && unsupportedHistory.Store.Settings.StoreReview == eligible,
            "Settings closes with eligible state on a platform without the review service.");
        unsupportedWindows.Panel.Close();
    }
    finally
    {
        windows.Settings?.Close();
        windows.Panel.Close();
        history.Stop();
        Directory.Delete(directory, recursive: true);
    }
});

Run("Welcome changes language immediately and Skip completes setup", () =>
{
    using var demo = new DemoPlatformServices();
    var history = CreateHistory(demo, seed: false);
    var window = new OnboardingWindow(demo, history, animate: false) { RequestedThemeVariant = ThemeVariant.Light };
    window.Show();
    try
    {
        Flush(window);
        var language = Find<ComboBox>(window, "WelcomeLanguage");
        language.SelectedItem = language.Items.OfType<ComboBoxItem>().Single(item => Equals(item.Tag, "ar"));
        Flush(window);
        Check(window.FlowDirection == FlowDirection.RightToLeft && HasLabel(window, "WelcomeSetupTitle")
            && history.Store.Settings.Language == "ar", "Changing language redraws the current step in RTL.");
        Advance(window, 1);
        Click(window, Find<Button>(window, "SkipSetup"));
        Check(history.Store.Settings.OnboardingCompleted && history.Store.Settings.OnboardingStep == 0
            && !window.IsVisible, "Skip saves completion, clears the saved step and closes.");
    }
    finally { if (window.IsVisible) window.Close(); Strings.SetLanguage("en"); }
});

Run("The shortcut recorder validates, registers, saves and cancels", () =>
{
    using var demo = new SetupDemo("Windows");
    var history = CreateHistory(demo, seed: false);
    demo.UnavailableShortcut = demo.DefaultGesture;
    history.Start();
    var panel = OpenPanel(demo, history);
    try
    {
        Check(Find<TextBlock>(panel, "PlatformStatus").Text == Strings.Get("HotkeyBusy") &&
            Equals(NoticeButtons(panel).Single().Content, Strings.Get("Settings")), "A failed binding is actionable in the footer.");
    }
    finally { panel.Close(); }
    var toggles = 0;
    history.ToggleRequested += () => toggles++;
    var window = new OnboardingWindow(demo, history, animate: false) { RequestedThemeVariant = ThemeVariant.Light };
    window.Show();
    try
    {
        Flush(window);
        Click(window, Find<Button>(window, "NextStep"));
        Check(window.Step == 1, "Get started advances to the shortcut step.");
        Click(window, Find<Button>(window, "ChangeShortcut"));
        demo.TriggerShortcut();
        Check(toggles == 0, "Global hotkeys are ignored while recording.");
        Press(window, Key.V, PhysicalKey.V, RawInputModifiers.Shift);
        Check(Find<TextBlock>(window, "ShortcutHint").Text == Strings.Get("ShortcutModifier") &&
            history.Store.Settings.PanelShortcut is null, "Shift+V is rejected with the typing hint.");
        demo.UnavailableShortcut = "Ctrl+Alt+J";
        Press(window, Key.J, PhysicalKey.J, RawInputModifiers.Control | RawInputModifiers.Alt);
        Check(Find<TextBlock>(window, "ShortcutHint").Text == Strings.Get("ShortcutTaken"), "A busy shortcut reports the conflict.");
        Press(window, Key.K, PhysicalKey.K, RawInputModifiers.Control | RawInputModifiers.Alt);
        Check(history.Store.Settings.PanelShortcut == "Ctrl+Alt+K" && demo.RegisteredShortcut == "Ctrl+Alt+K"
            && !history.ShortcutRegistrationFailed && HasLabel(window, "ShortcutSaved"), "A usable gesture is registered and saved.");
        Click(window, Find<Button>(window, "ChangeShortcut"));
        Press(window, Key.Escape, PhysicalKey.Escape);
        Check(!Find<ShortcutRecorder>(window, "ShortcutRecorder").IsRecording && history.Shortcut == "Ctrl+Alt+K", "Escape cancels recording.");
        Advance(window, 2);
        Click(window, Find<Button>(window, "BackStep"));
        Check(window.Step == 1 && Find<ShortcutRecorder>(window, "ShortcutRecorder").Gesture == "Ctrl+Alt+K",
            "Back keeps the chosen shortcut.");
        Press(window, Key.Left, PhysicalKey.ArrowLeft, RawInputModifiers.Alt);
        Check(window.Step == 0 && !Find<Button>(window, "BackStep").IsVisible, "Alt+Left returns to the first step, where Back is hidden.");
        var permissionsDot = Find<Button>(window, "StepDot3");
        Check(AutomationProperties.GetName(permissionsDot) == Strings.Format("SetupProgress", 3, 4)
            && !Find<Button>(window, "StepDot4").IsEnabled, "Progress names each step and skips no unreached step.");
        Click(window, permissionsDot);
        Check(window.Step == 2 && history.Shortcut == "Ctrl+Alt+K", "An already reached dot navigates forward without resetting the shortcut.");
    }
    finally { window.Close(); history.Stop(); }
});

Run("Permission requests, live state and startup use the adapter", () =>
{
    using var demo = new SetupDemo("macOS");
    var calls = 0;
    demo.DemoPermissions.AddRange([
        new("clipboard-access", PermissionRequirement.Required, PermissionState.Missing, () => calls++),
        new("accessibility", PermissionRequirement.Optional, PermissionState.Missing, () => calls++)]);
    var history = CreateHistory(demo, seed: false);
    var window = new OnboardingWindow(demo, history, animate: false);
    window.Show();
    try
    {
        Advance(window, 2);
        Check(HasLabel(window, "Required") && HasLabel(window, "Optional"), "Rows show their requirement tags.");
        Click(window, Find<Button>(window, "Request-clipboard-access"));
        Click(window, Find<Button>(window, "Request-accessibility"));
        Check(calls == 2 && HasLabel(window, "AccessibilityInstruction"), "Missing permission buttons request access and show the next action.");
        Click(window, Find<Button>(window, "BackStep"));
        Click(window, Find<Button>(window, "StepDot3"));
        Check(HasLabel(window, "AccessibilityInstruction"), "Navigation preserves the instructions for a permission already requested.");
        demo.DemoPermissions[0] = new("clipboard-access", PermissionRequirement.Required, PermissionState.Granted);
        var frame = new DispatcherFrame();
        DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromMilliseconds(1150));
        Dispatcher.UIThread.PushFrame(frame);
        Flush(window);
        var status = Find<StackPanel>(window, "Status-clipboard-access");
        Check(HasLabel(window, "Allowed") && status.Children.OfType<FluentIcons.Avalonia.SymbolIcon>().Any(),
            "Polling replaces the request with a check and Allowed.");

        var startup = Find<ToggleButton>(window, "StartupToggle");
        Check(startup.IsChecked == true, "Startup shows its saved default.");
        Click(window, startup);
        Check(!history.Store.Settings.StartAtLogin && !demo.Startup.IsEnabled, "Switching off applies and saves.");
        Click(window, startup);
        Check(history.Store.Settings.StartAtLogin && demo.Startup.IsEnabled, "Switching on calls the adapter.");
    }
    finally { window.Close(); }
});

Run("The GNOME Snap waits for a chosen shortcut and a live extension", () =>
{
    using var demo = new SetupDemo("Linux") { RequiresExplicitShortcut = true };
    var opened = false;
    var installed = new PlatformPermission("gnome-snap-helper", PermissionRequirement.Required, PermissionState.Granted);
    demo.DemoPermissions.Add(installed);
    var history = CreateHistory(demo, seed: false);
    history.Start();
    var window = new OnboardingWindow(demo, history, animate: false);
    window.Show();
    try
    {
        Advance(window, 3);
        Check(demo.RegisteredShortcut is null && HasLabel(window, "ShortcutNotSet"), "The Snap installs no default shortcut.");
        Click(window, Find<Button>(window, "PracticeChooseShortcut"));
        Check(window.Step == 1, "The practice action returns to the shortcut picker.");
        demo.Capabilities = PlatformCapabilities.None;
        demo.DemoPermissions[0] = new("gnome-snap-helper", PermissionRequirement.Required, PermissionState.Missing,
            () => opened = true);
        var choose = Find<Button>(window, "ChangeShortcut");
        Check(AutomationProperties.GetName(choose) == Strings.Get("ChooseShortcut"), "An unset shortcut says Choose shortcut.");
        Click(window, choose);
        Press(window, Key.V, PhysicalKey.V, RawInputModifiers.Control | RawInputModifiers.Alt);
        Check(history.Store.Settings.PanelShortcut == "Ctrl+Alt+V" && demo.RegisteredShortcut is null,
            "The chosen shortcut is saved but not registered while the extension is unavailable.");

        Advance(window, 2);
        var action = Find<Button>(window, "Request-gnome-snap-helper");
        Check(HasLabel(window, "PermissionsGnomeTitle") && Equals(action.Content, Strings.Get("OpenGnomeExtensions")),
            "The guide explains the GNOME requirement and offers to open the extension page.");
        Click(window, action);
        Check(opened && !history.InstallingGnomeHelper && !HasLabel(window, "SignOutToFinish"),
            "The Snap action opens the page without a local installation or sign-out flow.");

        demo.DemoPermissions[0] = installed;
        demo.Capabilities = PlatformCapabilities.WatchClipboard | PlatformCapabilities.GlobalHotkey;
        PumpUntil(() => HasLabel(window, "Installed") && demo.RegisteredShortcut == history.Shortcut,
            "The permission poll detects installation and registers the shortcut.");
        Advance(window, 3);
        Check(window.Practicing, "The guide is ready for practice once the extension is available.");
    }
    finally { window.Close(); history.Stop(); }
});

Run("Signing out on permissions saves the step and starts the guide again at login", () =>
{
    var directory = Path.Combine(Path.GetTempPath(), "Vstavka.OnboardingTests." + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        using var demo = new SetupDemo("Linux") { DataDirectory = directory, HistoryProtector = new AesGcmHistoryProtector(new TestKeyStore()) };
        DesktopHistory? signedOutWith = null;
        demo.DemoPermissions.Add(GnomePermission(PermissionState.RestartRequired, () => signedOutWith = CreateHistory(demo, seed: false)));
        var history = CreateHistory(demo, seed: false);
        var window = new OnboardingWindow(demo, history, animate: false);
        var settings = new SettingsWindow(demo, history, animate: false);
        window.Show();
        try
        {
            Advance(window, 2);
            Check(history.Store.Settings.OnboardingStep == 2 && HasLabel(window, "SignOutToFinish")
                && HasLabel(window, "GnomeRestartDescription"), "The permissions step saves its index and explains the restart.");
            var action = Find<Button>(window, "Request-gnome-helper");
            Check(Equals(action.Content, Strings.Get("SignOut")), "A pending restart offers Sign out.");
            settings.Show();
            Flush(settings);
            Click(settings, Find<Button>(settings, "SaveSettings"));
            Check(history.Store.Settings.OnboardingStep == 2, "A settings draft from before navigation preserves the latest step.");
            Click(window, action);
            PumpUntil(() => signedOutWith is not null, "The row invokes the sign-out action.");
            Check(signedOutWith!.Store.Settings.OnboardingStep == 2 && !signedOutWith.Store.Settings.OnboardingCompleted
                && demo.Startup.IsEnabled, "The disk snapshot and startup registration are ready before sign-out.");
        }
        finally
        {
            if (settings.IsVisible)
                settings.Close();
            window.Close();
            history.Stop();
        }

        demo.DemoPermissions[0] = GnomePermission(PermissionState.Granted);
        var resumed = new OnboardingWindow(demo, signedOutWith!, animate: false);
        resumed.Show();
        try
        {
            Flush(resumed);
            Check(resumed.Step == 2 && HasLabel(resumed, "Installed") && Find<Button>(resumed, "StepDot2").IsEnabled,
                "Reopening from the disk snapshot shows the installed helper on the saved step.");
        }
        finally { resumed.Close(); signedOutWith!.Stop(); }
    }
    finally { Directory.Delete(directory, recursive: true); }
});

Run("Installing the GNOME helper stays responsive and refreshes after the worker finishes", () =>
{
    using var demo = new SetupDemo("Linux") { Capabilities = PlatformCapabilities.None };
    var release = new TaskCompletionSource();
    var entered = new TaskCompletionSource<bool>();
    demo.DemoPermissions.Add(GnomePermission(PermissionState.Missing, () =>
    {
        entered.SetResult(Dispatcher.UIThread.CheckAccess());
        release.Task.Wait(TimeSpan.FromSeconds(5));
    }));
    var history = CreateHistory(demo, seed: false);
    var window = new OnboardingWindow(demo, history, animate: false);
    window.Show();
    try
    {
        Advance(window, 2);
        Click(window, Find<Button>(window, "Request-gnome-helper"));
        PumpUntil(() => entered.Task.IsCompleted, "The install action starts.");
        Check(!entered.Task.Result && HasLabel(window, "InstallingGnomeHelper")
            && Find<Control>(window, "GnomeInstallSpinner").IsVisible
            && !Find<Grid>(window, "gnome-helper").IsEnabled, "Installing uses a worker and replaces the row action with a spinner.");
        Click(window, Find<Button>(window, "BackStep"));
        Click(window, Find<Button>(window, "StepDot3"));
        Check(window.Step == 2 && HasLabel(window, "InstallingGnomeHelper") && !release.Task.IsCompleted,
            "Navigation works while installation is still in flight and the state stays visible when returning.");
        Advance(window, 3);
        demo.DemoPermissions[0] = GnomePermission(PermissionState.RestartRequired, () => { });
        release.SetResult();
        PumpUntil(() => !history.InstallingGnomeHelper, "The install completes.");
        Flush(window);
        Check(Find<Button>(window, "PracticeSignOut").IsVisible, "Try it updates when the installation changes the required action.");
        Click(window, Find<Button>(window, "BackStep"));
        Check(HasLabel(window, "SignOutToFinish") && Find<Grid>(window, "gnome-helper").IsEnabled
            && Find<Button>(window, "Request-gnome-helper").IsEnabled, "A completed install refreshes to the sign-out state.");
    }
    finally
    {
        release.TrySetResult();
        window.Close();
    }
});

Run("Try it reflects missing permissions, pending restart and adapter capabilities", () =>
{
    using var demo = new SetupDemo("Linux") { Capabilities = PlatformCapabilities.None };
    var signOuts = 0;
    demo.DemoPermissions.Add(GnomePermission(PermissionState.Missing));
    var history = CreateHistory(demo, seed: false);
    var window = new OnboardingWindow(demo, history, animate: false);
    window.Show();
    try
    {
        Advance(window, 2);
        Check(HasLabel(window, "PermissionNotAvailable"), "A missing permission without an action says Not available.");
        Advance(window, 3);
        Check(HasLabel(window, "PracticeAlmostThere") && HasText(window, Strings.Format("PracticePermissionMissing", Strings.Get("GnomeHelperTitle")))
            && !window.Practicing && Equals(Find<Button>(window, "NextStep").Content, Strings.Get("FinishSetup")),
            "Missing permission shows its name, with Finish still available.");
        Click(window, Find<Button>(window, "BackToPermissions"));
        Check(window.Step == 2, "The missing-permission action returns to the permissions step.");
        demo.DemoPermissions[0] = GnomePermission(PermissionState.RestartRequired, () => signOuts++);
        Advance(window, 3);
        Check(!window.Practicing && HasText(window, Strings.Format("PracticeGnomeRestart", demo.DefaultGesture)),
            "A pending restart explains signing back in and includes the configured shortcut.");
        Click(window, Find<Button>(window, "PracticeSignOut"));
        Check(signOuts == 1, "Try it can start sign-out.");

        Click(window, Find<Button>(window, "BackStep"));
        demo.DemoPermissions[0] = GnomePermission(PermissionState.Granted);
        demo.Capabilities = PlatformCapabilities.WatchClipboard;
        Advance(window, 3);
        Check(!window.Practicing && HasLabel(window, "PracticeUnavailable"), "An unavailable global hotkey prevents practice.");

        Click(window, Find<Button>(window, "BackStep"));
        demo.DemoPermissions.Add(new("accessibility", PermissionRequirement.Optional, PermissionState.Missing));
        demo.Capabilities |= PlatformCapabilities.GlobalHotkey;
        Advance(window, 3);
        Check(window.Practicing && Find<Button>(window, "CopyPracticeSample").IsVisible && HasLabel(window, "PracticeCopy"),
            "Capabilities and all required permissions unlock the checklist with an optional permission still missing.");
    }
    finally { window.Close(); }
});

Run("Try it uses the real panel and completes setup with an in-app paste", () =>
{
    using var demo = new SetupDemo("Windows") { StoreReview = new ReviewDemo() };
    var history = CreateHistory(demo, seed: true);
    var windows = new DesktopWindows(demo, history, animate: false, positionPanel: false);
    history.Start();
    windows.OpenOnboarding();
    var window = windows.Onboarding!;
    try
    {
        Advance(window, 3);
        Click(window, Find<Button>(window, "CopyPracticeSample"));
        var text = Strings.Get("PracticeSample");
        Check(history.Store.Items.Any(item => item.Text == text && item.SourceApp == "Vstavka") && demo.LastWritten?.Text == text,
            "Copy inserts the sample into history and writes through the demo clipboard.");
        demo.TriggerShortcut();
        Flush(windows.Panel);
        Check(windows.Panel.IsOpen, "The global shortcut opens the real panel.");
        Press(windows.Panel, Key.Enter, PhysicalKey.Enter);
        Flush(window);
        Check(!windows.Panel.IsVisible && Find<TextBox>(window, "PracticeBox").Text == text && demo.PasteRequests == 0
            && window.PracticeCompleted && HasLabel(window, "PracticeComplete"), "Return inserts the sample without injecting input.");
        Check(history.Store.Settings.StoreReview == default, "The practice pick does not count toward a store review.");
        Find<TextBox>(window, "PracticeBox").Text += " Edited.";
        Click(window, Find<Button>(window, "StepDot2"));
        Click(window, Find<Button>(window, "StepDot4"));
        Check(window.PracticeCompleted && Find<TextBox>(window, "PracticeBox").Text == text + " Edited.",
            "Leaving and returning via the dots retains the completed checklist and edited text.");
        Click(window, Find<Button>(window, "NextStep"));
        Check(history.Store.Settings.OnboardingCompleted && history.Store.Settings.OnboardingStep == 0
            && windows.Onboarding is null, "Finish saves completion, clears the saved step and closes the guide.");
    }
    finally
    {
        windows.Onboarding?.Close();
        windows.Panel.Close();
        history.Stop();
    }

    using var preview = new DemoPlatformServices();
    var previewHistory = CreateHistory(preview, seed: false);
    var previewWindows = new DesktopWindows(preview, previewHistory, animate: false, positionPanel: false);
    previewWindows.OpenOnboarding();
    var guide = previewWindows.Onboarding!;
    try
    {
        Advance(guide, 3);
        Click(guide, Find<Button>(guide, "CopyPracticeSample"));
        Press(guide, Key.V, PhysicalKey.V, RawInputModifiers.Control | RawInputModifiers.Shift);
        Check(previewWindows.Panel.IsOpen, "The focused demo guide can practice its shortcut without a global binding.");
    }
    finally
    {
        previewWindows.Panel.Close();
        guide.Close();
    }
});

Run("The guide reports clipboard write failure without advancing the practice", () =>
{
    var writer = new ThrowingClipboardWriter();
    using var demo = new SetupDemo("Linux") { Writer = writer };
    var history = CreateHistory(demo, seed: false);
    var window = new OnboardingWindow(demo, history, animate: false);
    window.Show();
    try
    {
        Advance(window, 3);
        Click(window, Find<Button>(window, "CopyPracticeSample"));
        Check(writer.Calls == 1 && window.PracticeItem is null && history.Store.Items.Count == 0
            && HasLabel(window, "ClipboardWriteFailed"), "An unsuccessful write leaves practice and history unchanged.");
    }
    finally { window.Close(); }
});

Run("Key frame animations move and scale controls", () =>
{
    // The other tests turn motion off, so this one runs the shared animation helper directly.
    var target = new Border();
    var window = new Window { Content = target };
    window.Show();
    try
    {
        using var stop = new CancellationTokenSource();
        var motion = DesktopMotion.Animate(target, TimeSpan.FromSeconds(10), DesktopMotion.Linear, stop.Token,
            (0, 1, "translateY(8px) scale(.9)"), (1, 1, "none"));
        PumpUntil(() =>
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            return motion.IsFaulted || target.RenderTransform?.Value.M32 > 1;
        }, "The animation moves the control.");
        Check(!motion.IsFaulted, motion.Exception?.GetBaseException().Message ?? "");
        stop.Cancel();
        PumpUntil(() =>
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            return motion.IsCompleted;
        }, "Cancelling stops the animation.");
        Check(!motion.IsFaulted, "Cancelling an animation does not fault it.");
    }
    finally { window.Close(); }
});

Run("A button is drawn in the app's colours when hovered, pressed and disabled", () =>
{
    // Fluent's own button theme recolours the same template part; the app's states have to win over it.
    var button = SetupVisuals.Button("Save", () => { }, "secondary");
    var window = new Window { Width = 200, Height = 100, Content = button, RequestedThemeVariant = ThemeVariant.Light };
    PanelTheme.Apply(window);
    window.Show();
    try
    {
        Flush(window);
        var part = button.GetVisualDescendants().OfType<ContentPresenter>().First();
        Color Role(string key) => ColorOf((IBrush)window.Resources[key]!);
        var point = Center(window, button);
        window.MouseMove(point);
        Flush(window);
        Check(ColorOf(part.Background) == Role("HoverBrush") && ColorOf(part.BorderBrush) == Role("HoverLineBrush"),
            "A hovered button takes the hover roles.");
        window.MouseDown(point, MouseButton.Left);
        Flush(window);
        Check(ColorOf(part.Background) == Role("PressedBrush"), "A pressed button takes the pressed role.");
        window.MouseUp(point, MouseButton.Left);
        window.MouseMove(new Point(1, 1));
        button.IsEnabled = false;
        Flush(window);
        Check(ColorOf(part.Background) == Role("SurfaceBrush") && ColorOf(part.Foreground) == Role("InkBrush")
            && ColorOf(part.BorderBrush) == Role("LineBrush") && button.Opacity < 1, "A disabled button keeps its colours and fades.");
    }
    finally { window.Close(); }
});

Run("Views take their text sizes, icons, radii and gaps from the design tokens", () =>
{
    double[] text = [TextSize.Caption, TextSize.Body, TextSize.Subtitle, TextSize.Keycap, TextSize.Title, TextSize.Display];
    double[] icons = [IconSize.Inline, IconSize.Control, IconSize.Feature, IconSize.Hero];
    double[] gaps = [0, Space.Xxs, Space.Xs, Space.S, Space.M, Space.L, Space.Xl, Space.Xxl, Space.Xxxl];
    double[] radii = [0, Radius.Small.TopLeft, Radius.Control.TopLeft, Radius.Surface.TopLeft, Radius.Panel.TopLeft];
    var strays = new List<string>();

    void Audit(Window window)
    {
        Flush(window);
        // Fluent sizes the parts of its own templates; everything the app builds itself has to be on the scales.
        foreach (var control in window.GetVisualDescendants().OfType<Control>().Where(control => control.TemplatedParent is null))
        {
            (string Kind, double[] Values, double[] Allowed) check = control switch
            {
                SymbolIcon icon => ("icon size", [icon.FontSize], icons),
                TextBlock label => ("text size", [label.FontSize], text),
                StackPanel stack => ("gap", [stack.Spacing], gaps),
                WrapPanel wrap => ("gap", [wrap.ItemSpacing, wrap.LineSpacing], gaps),
                Grid grid => ("gap", [grid.ColumnSpacing, grid.RowSpacing], gaps),
                Border border when !IsPill(border) => ("radius", [border.CornerRadius.TopLeft, border.CornerRadius.BottomRight], radii),
                _ => ("", [], [])
            };
            strays.AddRange(check.Values.Where(value => !check.Allowed.Contains(value))
                .Select(value => $"{window.GetType().Name}: {check.Kind} {value} on {control.GetType().Name} {control.Name}"));
        }
    }

    static bool IsPill(Border border)
    {
        // A hidden border has no bounds yet, only the size it was given.
        var height = Math.Min(double.IsNaN(border.Width) ? border.Bounds.Width : border.Width,
            double.IsNaN(border.Height) ? border.Bounds.Height : border.Height);
        return height > 0 && Math.Abs(border.CornerRadius.TopLeft - height / 2) <= .5;
    }

    using var demo = new SetupDemo("macOS") { ClipboardSounds = new ClipboardSoundsDouble() };
    demo.DemoPermissions.AddRange(MacPermissions());
    var history = CreateHistory(demo, seed: true);
    var panel = OpenPanel(demo, history);
    var guide = new OnboardingWindow(demo, history, animate: false);
    var settings = new SettingsWindow(demo, history, animate: false);
    try
    {
        Audit(panel);
        Click(panel, Find<Button>(panel, "NewCollectionButton"));
        Audit(panel);
        Press(panel, Key.Escape, PhysicalKey.Escape);
        Find<TextBox>(panel, "SearchBox").Text = "no matching content";
        Audit(panel);

        guide.Show();
        for (var step = 0; step <= 3; step++)
        {
            Advance(guide, step);
            Audit(guide);
        }
        Click(guide, Find<Button>(guide, "CopyPracticeSample"));
        Audit(guide);

        settings.Show();
        Audit(settings);
        Click(settings, Find<Button>(settings, "ChangeShortcut"));
        Audit(settings);
        Check(strays.Count == 0, string.Join(Environment.NewLine, strays.Distinct()));
    }
    finally
    {
        settings.Close();
        guide.Close();
        panel.Close();
    }
});

if (args.Length == 2)
{
    Run("Render light, dark, RTL, empty and platform notice PNGs", () =>
    {
        var directory = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(directory);
        Render("light", ThemeVariant.Light, "en", seed: true);
        Render("dark", ThemeVariant.Dark, "en", seed: true);
        Render("rtl", ThemeVariant.Light, "ar", seed: true);
        Render("empty", ThemeVariant.Light, "en", seed: false);
        using var withNotice = new NoticeDemo { Notices = [new("pasteboard-access-denied", "Denied.", () => { })] };
        Render("notice", ThemeVariant.Light, "en", seed: true, withNotice);
        withNotice.Notices = [new("gnome-helper-restart", "Restart GNOME.", () => { })];
        Render("notice-gnome-restart", ThemeVariant.Light, "en", seed: true, withNotice);
        foreach (var (variant, language, name) in new[]
        {
            (ThemeVariant.Light, "en", "light"), (ThemeVariant.Dark, "en", "dark"), (ThemeVariant.Light, "ar", "rtl")
        })
        {
            using var demo = new SetupDemo("macOS");
            demo.DemoPermissions.AddRange(MacPermissions());
            RenderOnboarding(demo, variant, language, name, allSteps: true);
            RenderGnomeStates(variant, language, name);
        }
        using var windows = new SetupDemo("Windows");
        RenderOnboarding(windows, ThemeVariant.Light, "en", "windows", allSteps: false);
        using var gnome = new SetupDemo("Linux");
        gnome.DemoPermissions.Add(new("gnome-helper", PermissionRequirement.Required, PermissionState.Missing, () => { }));
        RenderOnboarding(gnome, ThemeVariant.Light, "en", "gnome", allSteps: false);
        using var mac = new SetupDemo("macOS");
        mac.DemoPermissions.AddRange(MacPermissions());
        RenderOnboarding(mac, ThemeVariant.Light, "en", "macos", allSteps: false);
        foreach (var (variant, name) in new[] { (ThemeVariant.Light, "light"), (ThemeVariant.Dark, "dark") })
        {
            using var demo = new SetupDemo("Windows");
            var settings = new SettingsWindow(demo, CreateHistory(demo, seed: true), animate: false) { RequestedThemeVariant = variant };
            settings.Show();
            try
            {
                SaveFrame(settings, "settings-" + name);
                var clear = Find<Button>(settings, "ClearHistory");
                clear.BringIntoView();
                Flush(settings);
                Click(settings, clear);
                SaveFrame(settings, "settings-confirm-clear-" + name);
            }
            finally { settings.Close(); }
        }

        void RenderOnboarding(SetupDemo demo, ThemeVariant variant, string language, string suffix, bool allSteps)
        {
            Strings.SetLanguage(language);
            var history = CreateHistory(demo, seed: false);
            history.Store.Settings.Language = language;
            var window = new OnboardingWindow(demo, history, animate: false) { RequestedThemeVariant = variant };
            window.Show();
            try
            {
                if (allSteps)
                {
                    for (var step = 0; step < 4; step++)
                    {
                        Advance(window, step);
                        SaveFrame(window, $"onboarding-{step + 1}-{suffix}");
                    }
                }
                else
                {
                    Advance(window, 2);
                    SaveFrame(window, $"onboarding-3-{suffix}");
                }
            }
            finally { window.Close(); }
        }

        void RenderGnomeStates(ThemeVariant variant, string language, string suffix)
        {
            Strings.SetLanguage(language);
            using var demo = new SetupDemo("Linux") { Capabilities = PlatformCapabilities.None };
            var release = new TaskCompletionSource();
            demo.DemoPermissions.Add(GnomePermission(PermissionState.Missing, () => release.Task.Wait(TimeSpan.FromSeconds(5))));
            var history = CreateHistory(demo, seed: false);
            history.Store.Settings.Language = language;
            history.Store.Settings.OnboardingStep = 2;
            var window = new OnboardingWindow(demo, history, animate: false) { RequestedThemeVariant = variant };
            window.Show();
            try
            {
                Advance(window, 3);
                SaveFrame(window, "onboarding-4-gnome-missing-" + suffix);
                Click(window, Find<Button>(window, "BackToPermissions"));
                Click(window, Find<Button>(window, "Request-gnome-helper"));
                SaveFrame(window, "onboarding-3-gnome-installing-" + suffix);
                demo.DemoPermissions[0] = GnomePermission(PermissionState.RestartRequired, () => { });
                release.SetResult();
                PumpUntil(() => !history.InstallingGnomeHelper, "The render installer completes.");
                SaveFrame(window, "onboarding-3-gnome-restart-" + suffix);
                Advance(window, 3);
                SaveFrame(window, "onboarding-4-gnome-restart-" + suffix);
            }
            finally { release.TrySetResult(); window.Close(); }
        }

        void SaveFrame(Window window, string name)
        {
            window.MouseMove(new Point(12, 12));
            Flush(window);
            using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered frame for " + name);
            frame.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
        }

        void Render(string name, ThemeVariant variant, string language, bool seed, IPlatformServices? services = null)
        {
            Strings.SetLanguage(language);
            using var demo = new DemoPlatformServices();
            services ??= demo;
            var panel = OpenPanel(services, CreateHistory(services, seed), variant);
            try
            {
                using var frame = panel.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered frame.");
                Check(frame.PixelSize.Width == 1338 && frame.PixelSize.Height == 338, "Expected a full-size drawer image.");
                frame.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
            }
            finally { panel.Close(); }
        }
    });
}

Strings.SetLanguage("en");
return failures == 0 ? 0 : 1;

void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception error)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}\n{error}");
    }
}

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void RenderReadme(string directory)
{
    Directory.CreateDirectory(directory);
    using var context = AvaloniaSynchronizationContext.Ensure(DispatcherPriority.Normal);
    Strings.SetLanguage("en");
    var scratch = Directory.CreateTempSubdirectory("vstavka-readme-");
    using var demo = new SetupDemo("macOS")
    {
        DataDirectory = Path.Combine(scratch.FullName, "history"),
        HistoryProtector = new AesGcmHistoryProtector(new TestKeyStore())
    };
    demo.DemoPermissions.AddRange(MacPermissions());
    demo.Startup.TrySetEnabled(true);
    var history = CreateHistory(demo, seed: true);
    try
    {
        RenderStills();
        history.Store.Settings.OnboardingStep = 0;
        RenderReadmeAnimation(new OnboardingWindow(demo, history) { RequestedThemeVariant = ThemeVariant.Light }, directory,
            scratch.CreateSubdirectory("frames").FullName);
    }
    finally
    {
        try { history.Stop(); }
        finally { scratch.Delete(recursive: true); }
    }

    void RenderStills()
    {
        var samples = history.Store.Items;
        var note = samples.Single(item => item.IsPinned);
        note.Text = Strings.Get("WelcomeSampleText") + "\nBring the printed trail map and the field notebook.";
        history.Store.Add(new ClipItem
        {
            Text = "const route = [\n  'North path',\n  'Old bridge',\n  'River loop',\n];",
            SourceApp = "Code",
            CapturedAt = samples.Min(item => item.CapturedAt).AddMinutes(-7)
        });
        history.Store.SetSourceIcon("Code", DemoSamples.SourceIcon("Code"));
        Check(history.Store.Items.All(item => !history.Store.GetSourceIcon(item.SourceApp).IsEmpty),
            "Every README card shows its source app icon.");

        foreach (var (variant, name) in new[] { (ThemeVariant.Light, "light"), (ThemeVariant.Dark, "dark") })
        {
            var panel = OpenPanel(demo, history, variant);
            byte[] shot;
            try
            {
                panel.Width = MacDesktop.Width - MacDesktop.PanelMargin * 2;
                Flush(panel);
                var cards = Find<ListBox>(panel, "Cards");
                Check(cards.Items.Count == 6 && ReferenceEquals(cards.SelectedItem, note), "The selected note and all six demo items are in the panel.");
                var scroll = (ScrollViewer)cards.Scroll!;
                var margin = cards.ContainerFromIndex(0)!.Margin;
                var width = Math.Floor(scroll.Viewport.Width / cards.Items.Count - margin.Left - margin.Right);
                cards.Styles.Add(new Style(selector => selector.OfType<ListBoxItem>()) { Setters = { new Setter(Layoutable.WidthProperty, width) } });
                Flush(panel);
                Check(scroll.Extent.Width <= scroll.Viewport.Width, "All six cards fit in the README panel.");
                using var frame = Capture(panel);
                using var png = new MemoryStream();
                frame.Save(png, PngBitmapEncoderOptions.Default);
                shot = png.ToArray();
            }
            finally { panel.Close(); }

            var desktop = MacDesktop.Create(shot, variant == ThemeVariant.Dark);
            desktop.Show();
            try { SaveStill(desktop, "desktop-" + name); }
            finally { desktop.Close(); }
        }

        var guide = new OnboardingWindow(demo, history, animate: false) { RequestedThemeVariant = ThemeVariant.Light };
        guide.Show();
        try
        {
            Advance(guide, 2);
            SaveStill(guide, "onboarding-permissions");
        }
        finally { guide.Close(); }

        history.Store.Settings.Theme = "light";
        var settings = new SettingsWindow(demo, history, animate: false) { RequestedThemeVariant = ThemeVariant.Light };
        settings.Show();
        try
        {
            Flush(settings);
            var scroll = Find<ScrollViewer>(settings, "SettingsScroll");
            settings.Height += Math.Max(0, Math.Ceiling(scroll.Extent.Height - scroll.Viewport.Height));
            Flush(settings);
            Check(scroll.Extent.Height <= scroll.Viewport.Height, "All settings fields fit in the README window.");
            SaveStill(settings, "settings");
        }
        finally { settings.Close(); }
    }

    void SaveStill(Window window, string name)
    {
        using var frame = Capture(window);
        frame.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }

    static RenderTargetBitmap Capture(Window window)
    {
        window.MouseMove(new Point(0, 0));
        window.FocusManager?.Focus(null);
        Flush(window);
        var frame = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 2), new Vector(192, 192));
        frame.Render(window);
        return frame;
    }
}

static void RenderReadmeAnimation(OnboardingWindow guide, string directory, string frames)
{
    const int framesPerSecond = 25;
    const int shortcutFrame = 50;
    const int frameCount = 195;
    Check(DesktopMotion.Enabled, "Enable system animations before recording onboarding.");
    guide.Show();
    try
    {
        var clock = Stopwatch.StartNew();
        Flush(guide);
        var size = PixelSize.FromSize(guide.ClientSize, 1);
        Check(size.Width == 640, "The animation uses the onboarding window's 640-pixel width.");
        using var frame = new RenderTargetBitmap(size);
        var png = new PngBitmapEncoderOptions { CompressionLevel = CompressionLevel.Fastest };
        Border[] keycaps = [];
        var keypresses = new List<Border>();
        Border? previous = null;
        for (var index = 0; index < frameCount; index++)
        {
            var remaining = TimeSpan.FromSeconds((double)index / framesPerSecond) - clock.Elapsed;
            if (remaining > TimeSpan.Zero)
            {
                // Pump the UI dispatcher while the guide's real Task.Delay calls and animations advance.
                var wait = new DispatcherFrame();
                using var timer = DispatcherTimer.RunOnce(() => wait.Continue = false, remaining);
                Dispatcher.UIThread.PushFrame(wait);
            }

            if (index == shortcutFrame)
            {
                Advance(guide, 1);
                guide.MouseMove(new Point(0, 0));
                guide.FocusManager?.Focus(null);
                keycaps = Find<StackPanel>(guide, "ShortcutKeycaps").Children.OfType<Border>().ToArray();
            }

            Flush(guide);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var pressed = keycaps.FirstOrDefault(cap => cap.RenderTransform?.Value.M32 > 0.05);
            if (pressed is not null && pressed != previous)
                keypresses.Add(pressed);
            previous = pressed;
            frame.Render(guide);
            frame.Save(Path.Combine(frames, FormattableString.Invariant($"frame-{index:D3}.png")), png);
        }
        Check(keycaps.Length == 3 && keypresses.SequenceEqual(keycaps.Concat(keycaps)),
            "The GIF captures two full runs of the shortcut animation.");
    }
    finally { guide.Close(); }

    var gif = Path.Combine(directory, "onboarding.gif");
    var ffmpeg = new ProcessStartInfo("ffmpeg") { UseShellExecute = false, CreateNoWindow = true };
    foreach (var argument in new[]
    {
        "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-framerate", FormattableString.Invariant($"{framesPerSecond}"),
        "-i", Path.Combine(frames, "frame-%03d.png"), "-filter_complex_threads", "1", "-filter_complex",
        "split[frames][colors];[colors]palettegen=stats_mode=diff[palette];[frames][palette]paletteuse=dither=bayer:bayer_scale=3:diff_mode=rectangle",
        "-loop", "0", gif
    })
        ffmpeg.ArgumentList.Add(argument);
    using var process = Process.Start(ffmpeg) ?? throw new InvalidOperationException("Could not start ffmpeg.");
    process.WaitForExit();
    Check(process.ExitCode == 0, $"ffmpeg exited with code {process.ExitCode}.");
    Check(new FileInfo(gif).Length <= 3_000_000, "The onboarding GIF must be under 3 MB.");
}

static DesktopHistory CreateHistory(IPlatformServices demo, bool seed)
{
    var history = new DesktopHistory(demo);
    if (seed)
        DemoSamples.Seed(history.Store);
    return history;
}

static PanelWindow OpenPanel(IPlatformServices demo, DesktopHistory history, ThemeVariant? theme = null)
{
    var panel = new PanelWindow(demo, history, animate: false)
    {
        Width = 1338,
        RequestedThemeVariant = theme ?? ThemeVariant.Light
    };
    panel.Open(positionOnScreen: false);
    Flush(panel);
    return panel;
}

static void Flush(Window panel)
{
    Dispatcher.UIThread.RunJobs();
    panel.UpdateLayout();
    Dispatcher.UIThread.RunJobs();
}

static void PumpUntil(Func<bool> condition, string message)
{
    if (condition())
        return;
    var frame = new DispatcherFrame();
    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
    timer.Tick += (_, _) => { if (condition()) frame.Continue = false; };
    using var timeout = DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromSeconds(3));
    timer.Start();
    try { Dispatcher.UIThread.PushFrame(frame); }
    finally { timer.Stop(); }
    Check(condition(), message);
}

static void Advance(OnboardingWindow window, int step)
{
    Flush(window);
    while (window.Step < step)
    {
        var before = window.Step;
        Click(window, Find<Button>(window, "NextStep"));
        Check(window.Step == before + 1, "Continue advances one step.");
    }
}

static PlatformPermission[] MacPermissions() =>
[
    new("clipboard-access", PermissionRequirement.Required, PermissionState.Granted),
    new("accessibility", PermissionRequirement.Optional, PermissionState.Missing, () => { })
];

static PlatformPermission GnomePermission(PermissionState state, Action? request = null) =>
    new("gnome-helper", PermissionRequirement.Required, state, request);

static RawInputModifiers CommandModifier() => OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
static T Find<T>(Visual root, string name) where T : Control =>
    root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

static Button ButtonNamed(Window root, string name) =>
    root.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == name);

static IEnumerable<Button> NoticeButtons(Window root) =>
    root.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("permission"));

static bool HasLabel(Window root, string key) => HasText(root, Strings.Get(key));

static bool HasText(Window root, string text) =>
    root.GetVisualDescendants().OfType<TextBlock>().Any(label => label.IsVisible && label.Text == text);

static Point Center(Window root, Control control) =>
    control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)
        ?? throw new InvalidOperationException("Control is detached.");

static void Click(Window panel, Control control)
{
    var point = Center(panel, control);
    panel.MouseMove(point);
    panel.MouseDown(point, MouseButton.Left);
    panel.MouseUp(point, MouseButton.Left);
    Flush(panel);
}

static void Press(Window panel, Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
{
    panel.KeyPress(key, modifiers, physical, "");
    panel.KeyRelease(key, modifiers, physical, "");
    Flush(panel);
}

static Color ColorOf(IBrush? brush) => ((ISolidColorBrush)brush!).Color;

sealed class NoticeDemo : IPlatformServices
{
    private readonly DemoPlatformServices _demo = new();

    public string Name => _demo.Name;
    public PlatformCapabilities Capabilities => _demo.Capabilities;
    public IReadOnlyList<PlatformNotice> Notices { get; set; } = [];
    public string DataDirectory => _demo.DataDirectory;
    public string DefaultGesture => _demo.DefaultGesture;
    public IClipboardMonitor Clipboard => _demo.Clipboard;
    public IClipboardWriter ClipboardWriter => _demo.ClipboardWriter;
    public IGlobalHotkey Hotkey => _demo.Hotkey;
    public IPasteService Paste => _demo.Paste;
    public IPanelWindow PanelWindow => _demo.PanelWindow;
    public IHistoryProtector? HistoryProtector => _demo.HistoryProtector;
    public void Dispose() => _demo.Dispose();
}

sealed class ClipboardSoundsDouble : IClipboardSounds
{
    public List<bool> WatchCalls { get; } = [];
    public List<(bool Paste, double Volume)> Played { get; } = [];
    public event Action? PasteShortcutPressed { add { } remove { } }
    public void WatchPasteShortcuts(bool enabled) => WatchCalls.Add(enabled);
    public void Play(bool paste, double volume) => Played.Add((paste, volume));
    public void Dispose() { }
}

sealed class SetupDemo(string profile) : IPlatformServices, IGlobalHotkey
{
    private readonly DemoPlatformServices _demo = new();
    public string Name => profile;
    public PlatformCapabilities Capabilities { get; set; } = PlatformCapabilities.GlobalHotkey | PlatformCapabilities.WatchClipboard;
    public IReadOnlyList<PlatformNotice> Notices => _demo.Notices;
    public string DataDirectory { get; init; } = Path.Combine(Path.GetTempPath(), "Vstavka.Demo");
    public string DefaultGesture => profile == "macOS" ? "Shift+Meta+V" : _demo.DefaultGesture;
    public bool RequiresExplicitShortcut { get; init; }
    public List<PlatformPermission> DemoPermissions => _demo.DemoPermissions;
    public IReadOnlyList<PlatformPermission> Permissions => _demo.Permissions;
    public IStartupRegistration Startup => _demo.Startup;
    public IStoreReview? StoreReview { get; init; }
    public IClipboardSounds? ClipboardSounds { get; init; }
    public IClipboardMonitor? Monitor { get; init; }
    public IClipboardMonitor Clipboard => Monitor ?? _demo.Clipboard;
    public IClipboardWriter? Writer { get; init; }
    public IClipboardWriter ClipboardWriter => Writer ?? _demo.ClipboardWriter;
    public IGlobalHotkey Hotkey => this;
    public IPasteService Paste => _demo.Paste;
    public IPanelWindow PanelWindow => _demo.PanelWindow;
    public IHistoryProtector? HistoryProtector { get; init; }
    public ClipItem? LastWritten => _demo.LastWritten;
    public int PasteRequests => _demo.PasteRequests;
    public string? RegisteredShortcut { get; private set; }
    public string? UnavailableShortcut { get; set; }

    public event Action? Pressed;
    public void TriggerShortcut() => Pressed?.Invoke();

    public bool TryRegister(string gesture)
    {
        if (gesture == UnavailableShortcut || !_demo.TryRegister(gesture))
            return false;
        RegisteredShortcut = gesture;
        return true;
    }

    public void Dispose() => _demo.Dispose();
}

sealed class ReviewDemo : IStoreReview
{
    public string AppVersion => "1.0.0";
    public int Calls { get; private set; }
    public Action? Requesting { get; set; }

    public bool TryRequest()
    {
        Calls++;
        Requesting?.Invoke();
        return true;
    }
}

sealed class TestKeyStore : IHistoryKeyStore
{
    private byte[]? _key;
    public bool Unavailable { get; set; }
    public byte[]? TryLoadKey() => Unavailable ? throw new IOException("Injected key-store failure.") : _key?.ToArray();
    public void SaveKey(byte[] key) => _key = key.ToArray();
}

sealed class ThrowingClipboardWriter : IClipboardWriter
{
    public int Calls { get; private set; }

    public void Write(ClipItem item, bool plainText)
    {
        Calls++;
        throw new InvalidOperationException("Injected clipboard failure.");
    }
}

sealed class ClipboardMonitorDouble : IClipboardMonitor
{
    public event Action<ClipboardCapture>? Captured;
    public void Start() { }
    public void Dispose() { }

    public void Publish(string text, DateTimeOffset copiedAt) =>
        Captured?.Invoke(new ClipboardCapture(new ClipItem { Text = text, CapturedAt = copiedAt }, null));
}
