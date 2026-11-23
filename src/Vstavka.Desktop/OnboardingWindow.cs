using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FluentIcons.Common;
using Vstavka.Core;
using Vstavka.Core.Localization;
using Vstavka.Platform;

namespace Vstavka.Desktop;

internal sealed class OnboardingWindow : Window
{
    private const int LastStep = 3;
    private readonly IPlatformServices _platform;
    private readonly DesktopHistory _history;
    private readonly bool _animate;
    private readonly Grid _stage = new() { ClipToBounds = true };
    private readonly StackPanel _progress = SetupVisuals.Horizontal(6);
    private readonly Border _footer;
    private readonly Border[] _dots = new Border[4];
    private CancellationTokenSource? _motion;
    private StartupRow? _startup;
    private TextBox? _practiceBox;
    private TextBlock? _practiceTitle;
    private TextBlock? _practiceError;
    private readonly Grid[] _checks = new Grid[3];
    private string _practiceText = "";
    private bool _copied;
    private bool _openedWithShortcut;
    private bool _pasted;
    private bool _welcomePlayed;

    public int Step { get; private set; }
    public bool Practicing => IsVisible && Step == LastStep;
    public bool PracticeCompleted => _copied && _openedWithShortcut && _pasted;
    public ClipItem? PracticeItem => Practicing && _copied
        ? _history.Store.Items.FirstOrDefault(item => item.Kind == ClipKind.Text && item.Text == _practiceText)
        : null;

    public OnboardingWindow(IPlatformServices platform, DesktopHistory history, bool animate = true)
    {
        _platform = platform;
        _history = history;
        _animate = animate && DesktopMotion.Enabled;
        Title = Strings.Get("AppName");
        Width = 640;
        Height = 460;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Bind(BackgroundProperty, this.GetResourceObservable("WindowBrush"));
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaTitleBarHeightHint = 30;
        }

        _footer = SetupVisuals.Footer(new Grid(), new Thickness(32, 0));
        var root = new Grid { RowDefinitions = new("*,62") };
        SetupVisuals.Add(root, _stage);
        SetupVisuals.Add(root, _footer, row: 1);
        if (ExtendClientAreaToDecorationsHint)
        {
            var dragArea = new Border { Background = Brushes.Transparent, Height = 30, VerticalAlignment = VerticalAlignment.Top };
            WindowDecorationProperties.SetElementRole(dragArea, WindowDecorationsElementRole.TitleBar);
            SetupVisuals.Add(root, dragArea);
        }
        Content = root;

        for (var index = 0; index < _dots.Length; index++)
        {
            var dot = new Border
            {
                Classes = { "setup-dot" }, Height = 6, Width = 6, CornerRadius = new CornerRadius(3),
                VerticalAlignment = VerticalAlignment.Center
            };
            if (_animate)
                dot.Transitions = new Transitions
                {
                    new DoubleTransition { Property = WidthProperty, Duration = TimeSpan.FromMilliseconds(300), Easing = new ExponentialEaseOut() }
                };
            _dots[index] = dot;
            _progress.Children.Add(dot);
        }

        ActualThemeVariantChanged += (_, _) => ApplyAppearance();
        _history.SettingsChanged += ApplyAppearance;
        ApplyAppearance();
        Opened += (_, _) => ShowStep(0, slide: false);
        Closed += (_, _) =>
        {
            StopMotion();
            _history.SettingsChanged -= ApplyAppearance;
            _stage.Children.Clear();
        };
    }

    public void ShortcutOpened()
    {
        if (!Practicing)
            return;
        _openedWithShortcut = true;
        UpdatePractice();
    }

    public void ReceivePracticePaste(ClipItem item)
    {
        if (!Practicing || _practiceBox is null)
            return;

        var text = item.Kind == ClipKind.Files ? string.Join(Environment.NewLine, item.Files) : item.Text;
        Activate();
        _practiceBox.Focus();
        _practiceBox.SelectedText = text;
        if (_copied && item.Kind == ClipKind.Text && text == _practiceText)
            _pasted = true;
        UpdatePractice();
    }

    public void ApplyAppearance() => PanelTheme.Apply(this, _history.Store.Settings.Accent);

    private void ShowStep(int index, bool slide = true)
    {
        StopMotion();
        _startup = null;
        Step = index;
        SetupVisuals.Localize(this);
        _motion = new CancellationTokenSource();
        var token = _motion.Token;
        _stage.Children.Clear();
        var control = index switch
        {
            0 => Welcome(token),
            1 => Shortcut(token),
            2 => Permissions(),
            _ => Practice(token)
        };
        _stage.Children.Add(control);
        UpdateFooter();
        if (_animate && slide)
            _ = SetupVisuals.Animate(control, 300, token,
                (0, 0, Strings.IsRightToLeft ? "translateX(-28px)" : "translateX(28px)"), (1, 1, "translateX(0px)"));
    }

    private void UpdateFooter()
    {
        for (var index = 0; index < _dots.Length; index++)
        {
            var dot = _dots[index];
            dot.Width = index == Step ? 18 : 6;
            dot.Classes.Set("active", index == Step);
        }
        if (_progress.Parent is Grid old)
            old.Children.Remove(_progress);
        AutomationProperties.SetName(_progress, Strings.Format("SetupProgress", Step + 1, LastStep + 1));
        var skip = SetupVisuals.Button("SkipSetup", Complete, "ghost", "SkipSetup");
        var next = SetupVisuals.Button(Step switch { 0 => "GetStarted", LastStep => "FinishSetup", _ => "ContinueSetup" },
            Next, "primary", "NextStep");
        next.IsDefault = true;
        var contents = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 8 };
        SetupVisuals.Add(contents, _progress);
        SetupVisuals.Add(contents, skip, 1);
        SetupVisuals.Add(contents, next, 2);
        _footer.Child = contents;
    }

    private void Next()
    {
        if (Step == LastStep)
        {
            Complete();
            return;
        }
        if (Step == 2 && _startup is not null && _platform.Startup.IsEnabled != _startup.Enabled
            && !SetStartup(_startup.Enabled))
        {
            _startup.SetError(true, _platform.Startup.IsEnabled);
            return;
        }
        if (Step == 2)
            _history.RegisterSettingsShortcut();
        ShowStep(Step + 1);
    }

    private bool SetStartup(bool enabled)
    {
        if (!_platform.Startup.TrySetEnabled(enabled))
            return false;
        var settings = _history.Store.Settings.Copy();
        settings.StartAtLogin = enabled;
        _history.ApplySettings(settings);
        return true;
    }

    private void Complete()
    {
        var settings = _history.Store.Settings.Copy();
        settings.OnboardingCompleted = true;
        _history.ApplySettings(settings);
        Close();
    }

    private Control Welcome(CancellationToken token)
    {
        var picker = LanguagePicker(Strings.Language);
        picker.HorizontalAlignment = HorizontalAlignment.Right;
        picker.Name = "WelcomeLanguage";
        picker.MinWidth = 112;
        WindowDecorationProperties.SetElementRole(picker, WindowDecorationsElementRole.User);
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedItem is not ComboBoxItem { Tag: string code } || code == Strings.Language)
                return;
            var settings = _history.Store.Settings.Copy();
            settings.Language = code;
            _history.ApplySettings(settings);
            if (Strings.Language != code)
                Strings.SetLanguage(code);
            ShowStep(0, slide: false);
        };

        using var stream = AssetLoader.Open(new Uri("avares://Vstavka.Desktop/Assets/Vstavka.png"));
        var logo = new Image { Source = new Bitmap(stream), Width = 56, Height = 56, HorizontalAlignment = HorizontalAlignment.Center };
        var heading = Heading("WelcomeSetupTitle", "WelcomeSetupDescription", 24, out var title, out var body);
        heading.MaxWidth = 470;
        var intro = new StackPanel
        {
            Spacing = 12, Margin = new Thickness(32, 66, 32, 0), VerticalAlignment = VerticalAlignment.Top,
            Children = { logo, heading }
        };
        var panel = MiniaturePanel(out var cards, out var firstRing);
        panel.Margin = new Thickness(48, 0, 48, -19);
        panel.VerticalAlignment = VerticalAlignment.Bottom;
        var result = new Grid { Children = { panel, intro } };
        var pickerContainer = new Border
        {
            Margin = new Thickness(34, 36, 34, 0), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Right,
            Child = picker
        };
        result.Children.Add(pickerContainer);

        if (_animate && !_welcomePlayed)
        {
            _welcomePlayed = true;
            _ = SetupVisuals.Animate(logo, 550, token, (0, 1, "scale(.82)"), (.64, 1, "scale(1.05)"), (1, 1, "scale(1)"));
            _ = SetupVisuals.Reveal(title, 160, 440, "translateY(8px)", token);
            _ = SetupVisuals.Reveal(body, 240, 440, "translateY(8px)", token);
            _ = SetupVisuals.Reveal(panel, 350, 700, "translateY(46px)", token);
            for (var index = 0; index < cards.Length; index++)
                _ = SetupVisuals.Reveal(cards[index], 440 + index * 65, 470, "translateY(10px) scale(.97)", token);
            _ = SetupVisuals.Reveal(firstRing, 1250, 250, "translateY(0px)", token);
        }
        return result;
    }

    public static ComboBox LanguagePicker(string language, bool globe = true)
    {
        var picker = new ComboBox { Classes = { "setup-select" }, MaxDropDownHeight = 310 };
        AutomationProperties.SetName(picker, Strings.Get("SelectLanguage"));
        foreach (var option in Strings.AvailableLanguages)
        {
            Control name = new TextBlock { Text = option.Name };
            var item = new ComboBoxItem
            {
                Tag = option.Code, Content = globe ? SetupVisuals.Horizontal(7, PanelVisuals.Glyph(Symbol.Globe, 15, "MutedBrush"), name) : name
            };
            picker.Items.Add(item);
            if (option.Code == language)
                picker.SelectedItem = item;
        }
        return picker;
    }

    private Control Shortcut(CancellationToken token)
    {
        var recorder = new ShortcutRecorder(_history, _platform.Name, _history.Shortcut, gesture =>
        {
            var settings = _history.Store.Settings.Copy();
            settings.PanelShortcut = gesture;
            _history.ApplySettings(settings);
        }, _animate, large: true) { Name = "ShortcutRecorder" };
        var content = new StackPanel
        {
            Spacing = 17, Margin = new Thickness(42, 57, 42, 0),
            Children = { Heading("ShortcutSetupTitle", "ShortcutSetupDescription", 24, out _, out _), recorder }
        };
        if (_animate)
        {
            var panel = MiniaturePanel(out _, out _);
            panel.VerticalAlignment = VerticalAlignment.Top;
            var mini = new Border { MaxWidth = 330, Height = 67, ClipToBounds = true, Opacity = 0, Child = panel };
            content.Children.Add(mini);
            _ = ShortcutDemo(recorder, mini, token);
        }
        return content;
    }

    private static async Task ShortcutDemo(ShortcutRecorder recorder, Border mini, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(720, token);
                if (!recorder.IsRecording && recorder.Keycaps is StackPanel strip)
                {
                    foreach (var cap in strip.Children.OfType<Border>().ToArray())
                    {
                        if (recorder.IsRecording)
                            break;
                        var originalThickness = cap.BorderThickness;
                        using var tint = cap.Bind(Border.BackgroundProperty, cap.GetResourceObservable("SelectionBrush"));
                        cap.BorderThickness = new Thickness(1);
                        await SetupVisuals.Animate(cap, 200, token, (0, 1, "translateY(0px)"), (.35, 1, "translateY(3px)"), (1, 1, "translateY(0px)"));
                        cap.BorderThickness = originalThickness;
                    }
                    if (!recorder.IsRecording)
                    {
                        mini.Opacity = 1;
                        await SetupVisuals.Animate(mini, 420, token, (0, 0, "translateY(24px)"), (1, 1, "translateY(0px)"));
                        await Task.Delay(540, token);
                        await SetupVisuals.Animate(mini, 260, token, (0, 1, "translateY(0px)"), (1, 0, "translateY(15px)"));
                        mini.Opacity = 0;
                    }
                    await Task.Delay(350, token);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private Control Permissions()
    {
        if (_platform.Startup.IsSupported)
            _startup = new StartupRow(_platform.Name, _history.Store.Settings.StartAtLogin, SetStartup);
        var rows = new PermissionRows(_platform, _animate, _startup);
        rows.HotkeyBecameAvailable += _history.RegisterSettingsShortcut;
        var profile = _platform.Name switch
        {
            "macOS" => "Mac",
            "Windows" => "Windows",
            _ => _platform.Permissions.Any(permission => permission.Code == "gnome-helper") ? "Gnome" : "Linux"
        };
        var content = new StackPanel
        {
            Spacing = 15, Margin = new Thickness(36, 0, 36, 13),
            Children = { Heading("Permissions" + profile + "Title", "Permissions" + profile + "Description",
                24, out _, out _, TextAlignment.Start), rows }
        };
        return new ScrollViewer
        {
            Name = "PermissionStep", Content = content, Margin = new Thickness(0, 49, 0, 0),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }

    private Control Practice(CancellationToken token)
    {
        _practiceText = Strings.Get("PracticeSample");
        var heading = Heading(PracticeCompleted ? "PracticeComplete" : "PracticeTitle", null,
            28, out _practiceTitle, out _, TextAlignment.Start);
        var copyButton = SetupVisuals.Button("Copy", CopyPracticeSample, "permission", "CopyPracticeSample");
        var sample = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6 };
        SetupVisuals.Add(sample, SetupVisuals.Text(_practiceText, 12));
        SetupVisuals.Add(sample, copyButton, 1);
        var sampleCard = SetupVisuals.Surface(sample, 8);
        sampleCard.Padding = new Thickness(9, 5);
        sampleCard.Margin = new Thickness(0, 6, 0, 0);
        _practiceError = SetupVisuals.Text("", 12, "WarningBrush");
        _practiceError.IsVisible = false;

        var left = new StackPanel { Spacing = 15 };
        left.Children.Add(Checklist(0, new StackPanel { Children = { SetupVisuals.Text(Strings.Get("PracticeCopy"), strong: true), sampleCard, _practiceError } }));
        var press = new WrapPanel { ItemSpacing = 6, LineSpacing = 4 };
        press.Children.Add(SetupVisuals.Text(Strings.Get("PracticePress"), strong: true));
        press.Children.Add(ShortcutRecorder.CompactKeycaps(_history.Shortcut, _platform.Name));
        left.Children.Add(Checklist(1, press));
        left.Children.Add(Checklist(2, SetupVisuals.Text(Strings.Get("PracticeChoose"), strong: true)));

        _practiceBox = new TextBox
        {
            Name = "PracticeBox", AcceptsReturn = true, MinHeight = 166, MaxHeight = 190, TextWrapping = TextWrapping.Wrap,
            Classes = { "panel-search", "setup-input" }, Text = _pasted ? _practiceText : ""
        };
        AutomationProperties.SetName(_practiceBox, Strings.Get("PracticePasteHere"));
        var right = new StackPanel
        {
            Spacing = 6,
            Children = { SetupVisuals.Text(Strings.Get("PracticePasteHere"), 14, strong: true), _practiceBox }
        };
        var columns = new Grid { ColumnDefinitions = new("1.26*,*"), ColumnSpacing = 24 };
        SetupVisuals.Add(columns, left);
        SetupVisuals.Add(columns, right, 1);
        UpdatePractice(token, animate: false);
        return new ScrollViewer
        {
            Margin = new Thickness(0, 61, 0, 0), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new StackPanel { Spacing = 26, Margin = new Thickness(36, 0, 36, 12), Children = { heading, columns } }
        };
    }

    private Control Checklist(int index, Control control)
    {
        var circle = new Grid { Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Top };
        _checks[index] = circle;
        var row = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 9 };
        SetupVisuals.Add(row, circle);
        SetupVisuals.Add(row, control, 1);
        return row;
    }

    private void CopyPracticeSample()
    {
        var item = new ClipItem { Text = _practiceText, SourceApp = "Vstavka" };
        var store = _history.Store;
        _copied = (store.Add(item) || store.LastRejectionReason is null) && _history.TryWrite(item);
        _history.HistoryChanged();
        if (_practiceError is not null)
        {
            _practiceError.IsVisible = !_copied;
            _practiceError.Text = store.LastRejectionReason ?? Strings.Get("ClipboardWriteFailed");
        }
        UpdatePractice();
    }

    private void UpdatePractice(CancellationToken token = default, bool animate = true)
    {
        var states = new[] { _copied, _openedWithShortcut, _pasted };
        for (var index = 0; index < states.Length; index++)
        {
            var circle = _checks[index];
            var done = states[index];
            if (circle.Children.Count > 0 && done == (circle.Children[0].Classes.Contains("done")))
                continue;
            Control body = done ? PanelVisuals.Glyph(Symbol.Checkmark, 13, "PrimaryInkBrush")
                : SetupVisuals.Text((index + 1).ToString(Strings.Culture), 12, "MutedBrush", strong: true);
            if (body is TextBlock number)
                number.TextAlignment = TextAlignment.Center;
            var marker = SetupVisuals.Surface(body, 11, done ? "PrimaryBrush" : "SurfaceBrush", done ? "PrimaryBrush" : "LineBrush");
            marker.Classes.Set("done", done);
            circle.Children.Clear();
            circle.Children.Add(marker);
            if (done && animate && _animate)
                _ = SetupVisuals.Animate(marker, 320, token, (0, 0, "scale(.78)"), (1, 1, "scale(1)"));
        }
        if (_practiceTitle is not null)
            _practiceTitle.Text = Strings.Get(PracticeCompleted ? "PracticeComplete" : "PracticeTitle");
    }

    private static StackPanel Heading(string titleKey, string? bodyKey, double size, out TextBlock title, out TextBlock body,
        TextAlignment alignment = TextAlignment.Center)
    {
        title = SetupVisuals.Text(Strings.Get(titleKey), size, strong: true);
        body = SetupVisuals.Text(bodyKey is null ? "" : Strings.Get(bodyKey), 14, "MutedBrush");
        title.TextAlignment = body.TextAlignment = alignment;
        body.IsVisible = bodyKey is not null;
        return new StackPanel { Spacing = 8, Children = { title, body } };
    }

    private static Border MiniaturePanel(out Control[] cards, out Border ring)
    {
        var chips = SetupVisuals.Horizontal(12);
        foreach (var key in new[] { "All", "Pinned", "Text", "Links", "Images" })
        {
            var chip = SetupVisuals.Text(Strings.Get(key), 12, key == "All" ? "AccentBrush" : "MutedBrush", strong: key == "All");
            chip.MaxLines = 1;
            if (key == "All")
            {
                var active = SetupVisuals.Surface(chip, 8, "SelectionBrush", "SelectionBrush");
                active.Padding = new Thickness(7, 2);
                chips.Children.Add(active);
            }
            else
                chips.Children.Add(chip);
        }

        var strip = new Grid { ColumnDefinitions = new("*,*,*,*"), ColumnSpacing = 6 };
        var swatch = new Border { Height = 19, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.Parse("#246AC5")) };
        var hex = SetupVisuals.Text("#246AC5", 12);
        hex.FlowDirection = FlowDirection.LeftToRight;
        var color = new StackPanel { Spacing = 4, Children = { swatch, hex } };
        using var stream = new MemoryStream(DemoSamples.ExampleImage());
        var landscape = new Border
        {
            CornerRadius = new CornerRadius(4), Height = 57, ClipToBounds = true,
            Child = new Image { Source = new Bitmap(stream), Stretch = Stretch.UniformToFill }
        };
        var link = SetupVisuals.Text("example.org", 12, "LinkTypeBrush", strong: true);
        link.FlowDirection = FlowDirection.LeftToRight;
        Control[] previews = { SetupVisuals.Text(Strings.Get("WelcomeSampleText"), 12, strong: true), link, color, landscape };
        string[] types = [ "Text", "Link", "Color", "Image" ];
        cards = new Control[4];
        ring = new Border { BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(9), IsHitTestVisible = false };
        ring.Bind(Border.BorderBrushProperty, ring.GetResourceObservable("AccentBrush"));
        for (var index = 0; index < cards.Length; index++)
        {
            var label = PanelVisuals.Label(Strings.Get(types[index]), 12, "MutedBrush");
            var card = SetupVisuals.Surface(new StackPanel { Spacing = 4, Children = { label, previews[index] } }, 9,
                index == 0 ? "SelectedSurfaceBrush" : "SurfaceBrush");
            card.Padding = new Thickness(8, 6);
            var grid = new Grid { MinHeight = 94, Children = { card } };
            if (index == 0)
                grid.Children.Add(ring);
            SetupVisuals.Add(strip, grid, index);
            cards[index] = grid;
        }
        var mini = SetupVisuals.Surface(new StackPanel { Spacing = 7, Children = { chips, strip } }, 14, "WindowBrush");
        mini.Padding = new Thickness(10, 8);
        mini.IsHitTestVisible = false;
        return mini;
    }

    private void StopMotion()
    {
        _motion?.Cancel();
        _motion?.Dispose();
        _motion = null;
    }
}
