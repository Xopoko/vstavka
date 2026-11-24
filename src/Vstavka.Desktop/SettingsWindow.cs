using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Vstavka.Core;
using Vstavka.Core.Localization;
using Vstavka.Platform;

namespace Vstavka.Desktop;

internal sealed partial class SettingsWindow : Window
{
    private readonly IPlatformServices _platform;
    private readonly DesktopHistory _history;
    private readonly AppSettings _draft;
    private readonly ComboBox _language;
    private readonly ComboBox _theme;
    private readonly ComboBox _accent;
    private readonly ComboBox _retention;
    private readonly TextBox _limit;
    private readonly TextBox _excluded;
    private readonly TextBlock _error;
    private readonly StartupRow? _startup;
    private readonly ShortcutRecorder _recorder;
    private bool _startupChoiceChanged;
    private bool _saved;

    public SettingsWindow(IPlatformServices platform, DesktopHistory history, bool? animate = null)
    {
        _platform = platform;
        _history = history;
        _draft = history.Store.Settings.Copy();
        Title = Strings.Get("Settings");
        Width = 650;
        Height = 836;
        MinWidth = 500;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Bind(BackgroundProperty, this.GetResourceObservable("WindowBrush"));
        SetupVisuals.Localize(this);
        ActualThemeVariantChanged += (_, _) => ApplyAppearance();
        ApplyAppearance();

        _language = OnboardingWindow.LanguagePicker(_draft.Language, globe: false);
        _language.Name = "SettingsLanguage";
        _theme = Select("Theme", _draft.Theme, ("system", "ThemeSystem"), ("light", "ThemeLight"), ("dark", "ThemeDark"));
        _accent = Select("Accent", _draft.Accent, ("windows", "ThemeSystem"), ("violet", "AccentViolet"), ("blue", "AccentBlue"),
            ("teal", "AccentTeal"), ("rose", "AccentRose"), ("amber", "AccentAmber"));
        var appearance = Section("Appearance", Card(
            Field("Language", _language), Field("Theme", _theme), Field("Accent", _accent)));

        _recorder = new ShortcutRecorder(history, platform.Name, history.Shortcut,
            shortcut => _draft.PanelShortcut = shortcut, animate, large: false) { Name = "ShortcutRecorder" };
        var shortcut = Section("Shortcut", Card(_recorder));
        var contents = new StackPanel { Spacing = 14, Margin = new Thickness(30, 19, 30, 18), Children = { appearance, shortcut } };
        if (platform.ClipboardSounds is { } sounds)
            contents.Children.Add(SoundSection(sounds));
        if (platform.Startup.IsSupported)
        {
            if (history.IsStoreBuild)
                _draft.StartAtLogin = platform.Startup.IsEnabled;
            _startup = new StartupRow(platform.Name, _draft.StartAtLogin, value =>
            {
                _draft.StartAtLogin = value;
                _startupChoiceChanged = true;
                return true;
            });
            contents.Children.Add(Section("Startup", SetupVisuals.Surface(_startup)));
        }

        var permissions = new PermissionRows(platform, history, animate);
        permissions.HotkeyBecameAvailable += history.RegisterSettingsShortcut;
        var permissionSection = Section("Permissions", permissions);
        permissionSection.IsVisible = permissions.IsVisible;
        permissions.AvailabilityChanged += visible => permissionSection.IsVisible = visible;
        contents.Children.Add(permissionSection);

        var retention = _draft.RetentionDays.ToString(CultureInfo.InvariantCulture);
        _retention = Select("Retention", retention,
            ("7", "Days7"), ("30", "Days30"), ("90", "Days90"), ("365", "Year1"), ("0", "Forever"));
        if (_retention.SelectedItem is null)
        {
            var custom = new ComboBoxItem { Tag = retention, Content = Strings.Format("DaysCount", _draft.RetentionDays) };
            _retention.Items.Add(custom);
            _retention.SelectedItem = custom;
        }
        _limit = Input("MaxItems", _draft.MaxItems.ToString(CultureInfo.InvariantCulture));
        _limit.FlowDirection = FlowDirection.LeftToRight;
        _limit.VerticalContentAlignment = VerticalAlignment.Center;
        _excluded = Input("ExcludedApps", string.Join(Environment.NewLine, _draft.ExcludedApps));
        _excluded.AcceptsReturn = true;
        _excluded.TextWrapping = TextWrapping.Wrap;
        _excluded.LineHeight = 20;
        _excluded.MinLines = _excluded.MaxLines = 4;
        ScrollViewer.SetVerticalScrollBarVisibility(_excluded, ScrollBarVisibility.Auto);
        var excluded = new StackPanel
        {
            Spacing = 5,
            Children = { SetupVisuals.Text(Strings.Get("ExcludedApps"), strong: true), _excluded,
                SetupVisuals.Text(Strings.Get("ExcludedAppsHint"), 12, "MutedBrush") }
        };
        var description = SetupVisuals.Text(Strings.Get(history.Store.IsPersistent ? "PrivacySummary" : "PrivacyMemory"), 12, "MutedBrush");
        Control privacy = description;
        if (history.IsStoreBuild)
        {
            var policy = new HyperlinkButton
            {
                Content = Strings.Get("PrivacyPolicy"),
                NavigateUri = new Uri("https://github.com/Xopoko/vstavka-support/blob/main/PRIVACY.md"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(0, 3)
            };
            privacy = new StackPanel { Children = { description, policy } };
        }
        var retentionDescription = SetupVisuals.Text(Strings.Get("PinnedRetention"), 12, "MutedBrush");
        var historySection = Section("History", Card(
            privacy, Field("Retention", _retention), Field("MaxItems", _limit), retentionDescription, excluded, ClearHistory()));
        contents.Children.Add(historySection);

        var scroll = new ScrollViewer
        {
            Name = "SettingsScroll", Content = contents, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        _error = SetupVisuals.Text("", 12, "WarningBrush");
        _error.IsVisible = false;
        var cancel = SetupVisuals.Button("Cancel", Close, "ghost", "CancelSettings");
        var save = SetupVisuals.Button("Save", Save, "primary", "SaveSettings");
        var footer = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 8 };
        SetupVisuals.Add(footer, _error);
        SetupVisuals.Add(footer, cancel, 1);
        SetupVisuals.Add(footer, save, 2);
        var root = new Grid { RowDefinitions = new("*,62") };
        SetupVisuals.Add(root, scroll);
        SetupVisuals.Add(root, SetupVisuals.Footer(footer, new Thickness(30, 0)), row: 1);
        Content = root;

        Opened += (_, _) =>
        {
            if (Screens.ScreenFromWindow(this) is { } screen)
                Height = Math.Min(Height, Math.Max(MinHeight, screen.WorkingArea.Height / screen.Scaling - 48));
        };
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape || _recorder.IsRecording)
                return;
            if (_language.IsDropDownOpen || _theme.IsDropDownOpen || _accent.IsDropDownOpen || _retention.IsDropDownOpen)
                return;
            Close();
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Closed += (_, _) =>
        {
            _recorder.Cancel();
            if (!_saved && _recorder.Gesture != history.Shortcut)
                history.RegisterSettingsShortcut();
        };
    }

    public void ApplyAppearance() => PanelTheme.Apply(this, _history.Store.Settings.Accent);

    private Control ClearHistory()
    {
        var confirmationContent = new StackPanel { Spacing = 8 };
        var confirmation = SetupVisuals.Surface(confirmationContent, 9, "DangerSurfaceBrush", "DangerLineBrush");
        confirmation.Name = "ClearConfirmation";
        confirmation.IsVisible = false;
        confirmation.Padding = new Thickness(10);
        var status = SetupVisuals.Text("", 12, "MutedBrush");
        status.IsVisible = false;
        Button? clear = null;
        void DismissConfirmation()
        {
            confirmation.IsVisible = false;
            clear?.Focus(NavigationMethod.Tab);
        }
        var cancel = SetupVisuals.Button("Cancel", DismissConfirmation, "ghost", "CancelClear");
        var confirm = SetupVisuals.Button("DeleteAll", async () =>
        {
            DismissConfirmation();
            clear!.IsEnabled = false;
            var cleared = await _history.ClearHistoryAsync();
            clear.IsEnabled = true;
            status.Text = cleared ? Strings.Get("HistoryCleared") :
                _history.Store.SavingBlocked ? _history.Store.LoadWarning : Strings.Get("SaveFailed");
            status.IsVisible = true;
        }, "danger-filled", "ConfirmClear");
        confirmationContent.Children.Add(SetupVisuals.Text(Strings.Get("ClearConfirmation"), 12, "DangerBrush"));
        confirmationContent.Children.Add(SetupVisuals.Horizontal(6, cancel, confirm));
        clear = SetupVisuals.Button("ClearHistory", () =>
        {
            status.IsVisible = false;
            confirmation.IsVisible = true;
            UpdateLayout();
            cancel.Focus(NavigationMethod.Tab);
            confirmation.BringIntoView();
        }, "danger", "ClearHistory");
        clear.HorizontalAlignment = HorizontalAlignment.Left;
        return new StackPanel { Spacing = 8, Children = { clear, confirmation, status } };
    }

    private void Save()
    {
        if (!int.TryParse(_limit.Text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var max)
            || max is < 1 or > HistoryStore.MaxItemCount)
        {
            SetError("InvalidMaxItems");
            _limit.Focus(NavigationMethod.Tab);
            _limit.SelectAll();
            return;
        }
        var previous = _history.Store.Settings;
        var requestedShortcut = _draft.PanelShortcut ?? _platform.DefaultGesture;
        if (_draft.PanelShortcut != previous.PanelShortcut && !_history.TryRegisterShortcut(requestedShortcut))
        {
            SetError("ShortcutTaken");
            _recorder.BringIntoView();
            return;
        }
        if (_startup is not null && (_startupChoiceChanged || !_history.IsStoreBuild) &&
            _platform.Startup.IsEnabled != _draft.StartAtLogin && !_platform.Startup.TrySetEnabled(_draft.StartAtLogin))
        {
            _draft.StartAtLogin = _platform.Startup.IsEnabled;
            _startup.SetError(true, _draft.StartAtLogin);
            _startup.BringIntoView();
            SetError("StartupChangeFailed");
            return;
        }

        _draft.OnboardingCompleted = previous.OnboardingCompleted;
        _draft.OnboardingStep = previous.OnboardingStep;
        _draft.MaxItems = max;
        _draft.ExcludedApps = (_excluded.Text ?? "").Split(['\r', '\n', ';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _draft.RetentionDays = int.Parse(Value(_retention), CultureInfo.InvariantCulture);
        _draft.Language = Value(_language);
        _draft.Theme = Value(_theme);
        _draft.Accent = Value(_accent);
        _saved = true;
        _history.ApplySettings(_draft);
        Close();
    }

    private void SetError(string key)
    {
        _error.IsVisible = true;
        _error.Text = Strings.Get(key);
    }

    private static string Value(ComboBox box) => (string)((ComboBoxItem)box.SelectedItem!).Tag!;

    private static ComboBox Select(string key, string value, params (string Value, string Key)[] options)
    {
        var box = new ComboBox { Name = key + "Setting", Classes = { "setup-select" }, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(box, Strings.Get(key));
        foreach (var (tag, text) in options)
        {
            var item = new ComboBoxItem { Tag = tag, Content = Strings.Get(text) };
            box.Items.Add(item);
            if (tag == value)
                box.SelectedItem = item;
        }
        return box;
    }

    private static TextBox Input(string key, string value)
    {
        var box = new TextBox { Name = key + "Setting", Classes = { "panel-search", "setup-input" }, Text = value };
        AutomationProperties.SetName(box, Strings.Get(key));
        return box;
    }

    private static Grid Field(string label, Control control)
    {
        var row = new Grid { ColumnDefinitions = new("*,210"), ColumnSpacing = 16 };
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        SetupVisuals.Add(row, SetupVisuals.Text(Strings.Get(label)));
        SetupVisuals.Add(row, control, 1);
        return row;
    }

    private static Border Card(params Control[] children)
    {
        var content = new StackPanel { Spacing = 7 };
        foreach (var child in children)
            content.Children.Add(child);
        var result = SetupVisuals.Surface(content);
        result.Padding = new Thickness(13, 10);
        return result;
    }

    private static StackPanel Section(string label, Control body) => new()
    {
        Spacing = 7, Children = { SetupVisuals.Text(Strings.Get(label), 18, strong: true), body }
    };
}
