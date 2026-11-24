using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FluentIcons.Common;
using Vstavka.Core;
using Vstavka.Core.Localization;
using Vstavka.Platform;
using Vstavka.Platform.MacOS;

namespace Vstavka.Desktop;

internal sealed class OnboardingWindow : Window
{
    private const int LastStep = 3;
    private readonly IPlatformServices _platform;
    private readonly DesktopHistory _history;
    private readonly bool? _animate;
    private readonly Grid _stage = new() { ClipToBounds = true };
    private readonly Border _footer;
    private readonly (Button Button, Border Marker)[] _dots = new (Button, Border)[LastStep + 1];
    private Button _back;
    private Button _next;
    private Button? _skip;
    private TextBlock? _consentError;
    private int _furthestStep;
    private CancellationTokenSource? _motion;
    private StartupRow? _startup;
    private (PlatformPermission? Blocker, bool Ready) _practiceReadiness;
    private TextBox? _practiceBox;
    private TextBlock? _practiceTitle;
    private TextBlock? _practiceError;
    private readonly Grid[] _checks = new Grid[3];
    private string _practiceText = "";
    private string? _practiceInput;
    private string? _copyError;
    private bool _copied;
    private bool _openedWithShortcut;
    private bool _pasted;
    private bool _welcomePlayed;

    private bool Animated => _animate ?? DesktopMotion.Enabled;
    public int Step { get; private set; }
    public bool Practicing => IsVisible && _practiceBox is not null;
    public bool PracticeCompleted => _copied && _openedWithShortcut && _pasted;
    public ClipItem? PracticeItem => Practicing && _copied
        ? _history.Store.Items.FirstOrDefault(item => item.Kind == ClipKind.Text && item.Text == _practiceText)
        : null;

    public OnboardingWindow(IPlatformServices platform, DesktopHistory history, bool? animate = null)
    {
        _platform = platform;
        _history = history;
        _animate = animate;
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
        (_back, _next) = BuildFooter();
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

        ActualThemeVariantChanged += (_, _) => ApplyAppearance();
        _history.SettingsChanged += ApplyAppearance;
        _history.GnomeHelperInstallChanged += RefreshPracticeReadiness;
        ApplyAppearance();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (Step > 0 && e.Key == Key.Left && e.KeyModifiers == KeyModifiers.Alt)
            {
                e.Handled = true;
                Back();
            }
        }, RoutingStrategies.Tunnel);
        Opened += (_, _) =>
        {
            var settings = _history.Store.Settings;
            ShowStep(settings.OnboardingCompleted || _history.ClipboardConsentRequired ? 0 :
                Math.Clamp(settings.OnboardingStep, 0, LastStep), slide: false);
        };
        Activated += (_, _) => RefreshPracticeReadiness();
        Closed += (_, _) =>
        {
            StopMotion();
            _history.SettingsChanged -= ApplyAppearance;
            _history.GnomeHelperInstallChanged -= RefreshPracticeReadiness;
            _practiceBox = null;
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
        var direction = Math.Sign(index - Step);
        if (_practiceBox is not null)
            _practiceInput = _practiceBox.Text;
        _practiceBox = null;
        _startup = null;
        Step = index;
        _furthestStep = Math.Max(_furthestStep, index);
        if (_history.Store.Settings.OnboardingStep != index)
            _history.SetOnboardingStep(index);
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
        // The step comes from the side it lies on, the same way the progress dots run.
        if (Animated && slide && direction != 0)
            _ = DesktopMotion.Arrive(control, $"translateX({24 * direction}px)", DesktopMotion.Travel, token, fade: true);
    }

    private (Button Back, Button Next) BuildFooter()
    {
        var progress = SetupVisuals.Horizontal(0);
        for (var index = 0; index < _dots.Length; index++)
        {
            var dot = new Border
            {
                Classes = { "setup-dot" }, Height = 6, Width = 6, CornerRadius = new CornerRadius(3),
                VerticalAlignment = VerticalAlignment.Center
            };
            if (Animated)
                dot.Transitions = [new DoubleTransition { Property = WidthProperty, Duration = DesktopMotion.Travel, Easing = DesktopMotion.Decelerate }];
            var target = index;
            var button = new Button { Name = $"StepDot{target + 1}", Classes = { "panel-button", "setup-step" }, Content = dot };
            AutomationProperties.SetName(button, Strings.Format("SetupProgress", target + 1, LastStep + 1));
            button.Click += (_, _) => NavigateTo(target);
            _dots[index] = (button, dot);
            progress.Children.Add(button);
        }
        var back = SetupVisuals.Button("BackSetup", Back, "ghost", "BackStep",
            Strings.IsRightToLeft ? Symbol.ChevronRight : Symbol.ChevronLeft);
        var skip = SetupVisuals.Button("SkipSetup", Complete, "ghost", "SkipSetup");
        _skip = skip;
        var next = SetupVisuals.Button("GetStarted", Next, "primary", "NextStep");
        next.IsDefault = true;
        var contents = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
        SetupVisuals.Add(contents, SetupVisuals.Horizontal(8, progress, back));
        SetupVisuals.Add(contents, SetupVisuals.Horizontal(8, skip, next), 1);
        _footer.Child = contents;
        return (back, next);
    }

    private void UpdateFooter()
    {
        for (var index = 0; index < _dots.Length; index++)
        {
            var (button, dot) = _dots[index];
            dot.Width = index == Step ? 18 : 6;
            dot.Classes.Set("active", index == Step);
            dot.Classes.Set("visited", index <= _furthestStep);
            button.IsEnabled = index <= _furthestStep;
        }
        if (Step == 0 && _back.IsFocused)
            _dots[0].Button.Focus();
        _back.IsVisible = Step > 0;
        var consentRequired = _history.ClipboardConsentRequired;
        if (_skip is not null)
            _skip.IsVisible = !consentRequired;
        var nextLabel = Strings.Get(Step switch
        {
            0 => consentRequired ? "EnableHistory" : "GetStarted",
            LastStep => "FinishSetup",
            _ => "ContinueSetup"
        });
        _next.Content = nextLabel;
        AutomationProperties.SetName(_next, nextLabel);
    }

    private void Back() => NavigateTo(Step - 1);

    private void Next()
    {
        if (_history.ClipboardConsentRequired && !_history.TryEnableClipboardHistory())
        {
            if (_consentError is not null)
                _consentError.IsVisible = true;
            return;
        }
        if (Step == LastStep)
        {
            Complete();
            return;
        }
        NavigateTo(Step + 1);
    }

    private void NavigateTo(int step)
    {
        if (step < 0 || step > LastStep || step == Step)
            return;
        if (Step == 2 && step > Step)
        {
            if (!ApplyStartupPreference())
                return;
            _history.RegisterSettingsShortcut();
        }
        ShowStep(step);
    }

    private bool ApplyStartupPreference()
    {
        if (_startup is null || _history.IsStoreBuild ||
            _platform.Startup.IsEnabled == _startup.Enabled || SetStartup(_startup.Enabled))
            return true;
        _startup.SetError(true, _platform.Startup.IsEnabled);
        return false;
    }

    private void RequestSignOut(Action signOut)
    {
        if (ApplyStartupPreference())
            _ = _history.SignOutAsync(signOut);
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
        if (_history.ClipboardConsentRequired)
            return;
        var settings = _history.Store.Settings.Copy();
        settings.OnboardingCompleted = true;
        settings.OnboardingStep = 0;
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
            (_back, _next) = BuildFooter();
            ShowStep(0, slide: false);
        };

        using var stream = AssetLoader.Open(new Uri("avares://Vstavka.Desktop/Assets/Vstavka.png"));
        var logo = new Image { Source = new Bitmap(stream), Width = 56, Height = 56, HorizontalAlignment = HorizontalAlignment.Center };
        var heading = Heading("WelcomeSetupTitle", "WelcomeSetupDescription", 24, out _, out _);
        heading.MaxWidth = 470;
        var intro = new StackPanel
        {
            Spacing = 12, Margin = new Thickness(32, 66, 32, 0), VerticalAlignment = VerticalAlignment.Top,
            Children = { logo, heading }
        };
        _consentError = null;
        if (_history.ClipboardConsentRequired)
        {
            var notice = SetupVisuals.Text(Strings.Get("WelcomePrivacy"), 12, "MutedBrush");
            notice.HorizontalAlignment = HorizontalAlignment.Center;
            notice.TextAlignment = TextAlignment.Center;
            notice.MaxWidth = 470;
            _consentError = SetupVisuals.Text(Strings.Get("SaveFailed"), 12, "WarningBrush");
            _consentError.HorizontalAlignment = HorizontalAlignment.Center;
            _consentError.IsVisible = false;
            intro.Children.Add(notice);
            intro.Children.Add(_consentError);
        }
        var panel = MiniaturePanel();
        panel.Margin = new Thickness(48, 0, 48, -19);
        panel.VerticalAlignment = VerticalAlignment.Bottom;
        var result = new Grid { Children = { panel, intro } };
        var pickerContainer = new Border
        {
            Margin = new Thickness(34, 36, 34, 0), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Right,
            Child = picker
        };
        result.Children.Add(pickerContainer);

        // The welcome moves one thing: the panel rises from the bottom edge, as the real one does. It starts once
        // the window itself has opened.
        if (Animated && !_welcomePlayed)
        {
            _welcomePlayed = true;
            _ = DesktopMotion.Arrive(panel, "translateY(24px)", DesktopMotion.Scene, token, fade: true, delay: DesktopMotion.Travel);
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
        if (Animated)
        {
            var panel = MiniaturePanel();
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
            // Three runs are enough to link the keys to the panel; a loop that never ends only pulls the eye from
            // the text. A newly recorded shortcut is demonstrated again.
            var gesture = recorder.Gesture;
            var runs = 0;
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(720, token);
                if (recorder.Gesture != gesture)
                    (gesture, runs) = (recorder.Gesture, 0);
                if (runs == 3 || recorder.IsRecording || recorder.Keycaps is not StackPanel strip)
                    continue;

                foreach (var cap in strip.Children.OfType<Border>().ToArray())
                {
                    if (recorder.IsRecording)
                        break;
                    var originalThickness = cap.BorderThickness;
                    using var tint = cap.Bind(Border.BackgroundProperty, cap.GetResourceObservable("SelectionBrush"));
                    cap.BorderThickness = new Thickness(1);
                    await DesktopMotion.Animate(cap, DesktopMotion.Enter, DesktopMotion.Linear, token,
                        (0, 1, "none"), (.4, 1, "translateY(3px)"), (1, 1, "none"));
                    cap.BorderThickness = originalThickness;
                }
                if (!recorder.IsRecording)
                {
                    // The control's own opacity is set to where each move ends, so no frame shows it in between.
                    mini.Opacity = 1;
                    await DesktopMotion.Arrive(mini, "translateY(24px)", DesktopMotion.Scene, token, fade: true);
                    await Task.Delay(600, token);
                    mini.Opacity = 0;
                    await DesktopMotion.Animate(mini, DesktopMotion.Travel, DesktopMotion.Linear, token,
                        (0, 1, "none"), (1, 0, "translateY(24px)"));
                    runs++;
                }
                await Task.Delay(350, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private Control Permissions()
    {
        var appStoreBuild = _platform is MacPlatformServices { IsAppStoreBuild: true };
        if (_platform.Startup.IsSupported)
            _startup = new StartupRow(_platform.Name, _history.IsStoreBuild ? _platform.Startup.IsEnabled :
                _history.Store.Settings.StartAtLogin, SetStartup);
        var rows = new PermissionRows(_platform, _history, _animate, _startup, RequestSignOut);
        rows.HotkeyBecameAvailable += _history.RegisterSettingsShortcut;
        var profile = _platform.Name switch
        {
            "macOS" => "Mac",
            "Windows" => "Windows",
            _ => _history.Permissions.Any(permission => permission.Code == "gnome-helper") ? "Gnome" : "Linux"
        };
        var content = new StackPanel
        {
            Spacing = 15, Margin = new Thickness(36, 0, 36, 13),
            Children = { Heading("Permissions" + profile + "Title", appStoreBuild ? null : "Permissions" + profile + "Description",
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
        _practiceReadiness = ReadPracticeReadiness();
        if (!_practiceReadiness.Ready)
            return PracticeNotReady(_practiceReadiness.Blocker);

        _history.RegisterSettingsShortcut();
        if (!_copied)
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
        _practiceError = SetupVisuals.Text(_copyError ?? "", 12, "WarningBrush");
        _practiceError.Name = "PracticeCopyError";
        _practiceError.IsVisible = _copyError is not null;

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
            Classes = { "panel-search", "setup-input" }, Text = _practiceInput ?? ""
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

    private (PlatformPermission? Blocker, bool Ready) ReadPracticeReadiness()
    {
        var blocker = _history.Permissions.FirstOrDefault(permission =>
            permission.Requirement == PermissionRequirement.Required && permission.State != PermissionState.Granted);
        const PlatformCapabilities required = PlatformCapabilities.WatchClipboard | PlatformCapabilities.GlobalHotkey;
        return (blocker, blocker is null && (_platform is DemoPlatformServices || _platform.Capabilities.HasFlag(required)));
    }

    private void RefreshPracticeReadiness()
    {
        if (Step != LastStep || !IsVisible)
            return;
        var current = ReadPracticeReadiness();
        if (current.Ready != _practiceReadiness.Ready || Key(current.Blocker) != Key(_practiceReadiness.Blocker))
            ShowStep(LastStep, slide: false);

        static (string?, PermissionState?, bool) Key(PlatformPermission? permission) =>
            (permission?.Code, permission?.State, permission?.Request is not null);
    }

    private Control PracticeNotReady(PlatformPermission? permission)
    {
        var needsRestart = permission?.State == PermissionState.RestartRequired;
        var text = permission switch
        {
            { State: PermissionState.RestartRequired } => Strings.Format(
                permission.Code == "gnome-helper" ? "PracticeGnomeRestart" : "PracticePermissionRestart",
                SetupVisuals.ShortcutInText(ShortcutRecorder.GestureText(_history.Shortcut, _platform.Name))),
            not null => Strings.Format("PracticePermissionMissing", PermissionRows.Title(permission)),
            _ => Strings.Get("PracticeUnavailable")
        };
        var icon = SetupVisuals.Surface(PanelVisuals.Glyph(Symbol.Info, 20, "AccentBrush"), 10,
            "SelectedSurfaceBrush", "SelectedSurfaceBrush");
        icon.Width = icon.Height = 40;
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        var title = SetupVisuals.Text(Strings.Get("PracticeAlmostThere"), 28, strong: true);
        AutomationProperties.SetHeadingLevel(title, 1);
        var details = new StackPanel { Spacing = 6, Children = { title, SetupVisuals.Text(text, 14, "MutedBrush") } };
        var content = new StackPanel
        {
            Name = "PracticeNotReady", Width = 408, MinHeight = 186, Spacing = 15, Margin = new Thickness(36, 16),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Children = { icon, details }
        };
        Button? action = null;
        if (needsRestart && permission?.Request is { } signOut)
            action = SetupVisuals.Button("SignOut", () => RequestSignOut(signOut), "setup-tinted", "PracticeSignOut");
        else if (permission is not null)
            action = SetupVisuals.Button("BackToPermissions", Back, "secondary", "BackToPermissions");
        if (action is not null)
        {
            action.HorizontalAlignment = HorizontalAlignment.Left;
            content.Children.Add(action);
        }
        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = content,
            VerticalContentAlignment = VerticalAlignment.Center
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
        _copyError = _history.WriteToClipboard(item);
        if (_copyError is null)
        {
            _copied = store.Add(item) || store.LastRejectionReason is null;
            _copyError = store.LastRejectionReason;
            if (_copied)
                _history.HistoryChanged();
        }
        else
            _copied = false;
        if (_practiceError is not null)
        {
            _practiceError.IsVisible = _copyError is not null;
            _practiceError.Text = _copyError;
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
            if (done && animate && Animated)
                _ = DesktopMotion.Confirm(marker, token);
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

    private static Border MiniaturePanel()
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
        var ring = new Border { BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(9), IsHitTestVisible = false };
        ring.Bind(Border.BorderBrushProperty, ring.GetResourceObservable("AccentBrush"));
        for (var index = 0; index < types.Length; index++)
        {
            var label = PanelVisuals.Label(Strings.Get(types[index]), 12, "MutedBrush");
            var card = SetupVisuals.Surface(new StackPanel { Spacing = 4, Children = { label, previews[index] } }, 9,
                index == 0 ? "SelectedSurfaceBrush" : "SurfaceBrush");
            card.Padding = new Thickness(8, 6);
            var grid = new Grid { MinHeight = 94, Children = { card } };
            if (index == 0)
                grid.Children.Add(ring);
            SetupVisuals.Add(strip, grid, index);
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
