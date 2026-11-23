using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
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
            Check(status.Text == Strings.Get("AccessibilityPermission"), "The first known platform notice is localized.");
            var grant = NoticeButtons(panel).Single();
            Check(Equals(grant.Content, Strings.Get("GrantAccess")), "The grant button is localized.");
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
