using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Skia;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Vstavka.Core;
using Vstavka.Core.Localization;
using Vstavka.Desktop;
using Vstavka.Platform;

if (args.Length != 0 && (args.Length != 2 || args[0] != "--render"))
{
    Console.Error.WriteLine("Usage: Vstavka.Desktop.Tests [--render <directory>]");
    return 2;
}

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

var failures = 0;
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
            "Text includes colors as in the WPF panel.");
        Press(panel, Key.F, PhysicalKey.F, RawInputModifiers.Control);
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
        Press(panel, Key.F, PhysicalKey.F, RawInputModifiers.Control);
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
        Press(panel, Key.D1, PhysicalKey.Digit1, RawInputModifiers.Control);
        Check(ReferenceEquals(demo.LastWritten, cards.Items[0]) && demo.PasteRequests == 2 && !panel.IsVisible,
            "Ctrl+1 pastes the first filtered card.");
        panel.Open(positionOnScreen: false);
        Flush(panel);
        Press(panel, Key.Escape, PhysicalKey.Escape);
        Check(!panel.IsVisible && demo.PasteRequests == 2, "Escape hides without requesting paste.");
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
        Click(window, Find<Button>(window, "SkipSetup"));
        Check(history.Store.Settings.OnboardingCompleted && !window.IsVisible, "Skip saves completion and closes.");
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

Run("Try it uses the real panel and completes setup with an in-app paste", () =>
{
    using var demo = new SetupDemo("Windows");
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
        Click(window, Find<Button>(window, "NextStep"));
        Check(history.Store.Settings.OnboardingCompleted && windows.Onboarding is null, "Finish saves completion and closes the guide.");
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
        foreach (var (variant, language, name) in new[]
        {
            (ThemeVariant.Light, "en", "light"), (ThemeVariant.Dark, "en", "dark"), (ThemeVariant.Light, "ar", "rtl")
        })
        {
            using var demo = new SetupDemo("macOS");
            demo.DemoPermissions.AddRange(MacPermissions());
            RenderOnboarding(demo, variant, language, name, allSteps: true);
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

static T Find<T>(Visual root, string name) where T : Control =>
    root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

static Button ButtonNamed(Window root, string name) =>
    root.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == name);

static IEnumerable<Button> NoticeButtons(Window root) =>
    root.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("permission"));

static bool HasLabel(Window root, string key) =>
    root.GetVisualDescendants().OfType<TextBlock>().Any(label => label.IsVisible && label.Text == Strings.Get(key));

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

sealed class NoticeDemo : IPlatformServices, IPasteService
{
    private readonly DemoPlatformServices _demo = new();

    public string Name => _demo.Name;
    public PlatformCapabilities Capabilities => _demo.Capabilities;
    public IReadOnlyList<PlatformNotice> Notices { get; set; } = [];
    public string DataDirectory => _demo.DataDirectory;
    public string DefaultGesture => _demo.DefaultGesture;
    public IReadOnlyList<string> SupportedGestures => _demo.SupportedGestures;
    public IClipboardMonitor Clipboard => _demo.Clipboard;
    public IClipboardWriter ClipboardWriter => _demo.ClipboardWriter;
    public IGlobalHotkey Hotkey => _demo.Hotkey;
    public IPasteService Paste => this;
    public IPanelWindow PanelWindow => _demo.PanelWindow;
    public IHistoryProtector? HistoryProtector => _demo.HistoryProtector;
    public PermissionState Permission => PermissionState.Missing;
    public void CaptureTarget() => _demo.CaptureTarget();
    public Task<bool> PasteAsync() => _demo.PasteAsync();
    public void RequestPermission() => throw new InvalidOperationException("The footer must call the notice action.");
    public void Dispose() => _demo.Dispose();
}

sealed class SetupDemo(string profile) : IPlatformServices, IGlobalHotkey
{
    private readonly DemoPlatformServices _demo = new();
    public string Name => profile;
    public PlatformCapabilities Capabilities => PlatformCapabilities.GlobalHotkey;
    public IReadOnlyList<PlatformNotice> Notices => _demo.Notices;
    public string DataDirectory => _demo.DataDirectory;
    public string DefaultGesture => profile == "macOS" ? "Shift+Meta+V" : _demo.DefaultGesture;
    public IReadOnlyList<string> SupportedGestures => [DefaultGesture];
    public List<PlatformPermission> DemoPermissions => _demo.DemoPermissions;
    public IReadOnlyList<PlatformPermission> Permissions => _demo.Permissions;
    public IStartupRegistration Startup => _demo.Startup;
    public IClipboardMonitor Clipboard => _demo.Clipboard;
    public IClipboardWriter ClipboardWriter => _demo.ClipboardWriter;
    public IGlobalHotkey Hotkey => this;
    public IPasteService Paste => _demo.Paste;
    public IPanelWindow PanelWindow => _demo.PanelWindow;
    public IHistoryProtector? HistoryProtector => _demo.HistoryProtector;
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
