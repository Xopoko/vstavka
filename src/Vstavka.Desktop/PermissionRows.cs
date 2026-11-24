using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FluentIcons.Common;
using Vstavka.Core.Localization;
using Vstavka.Platform;
using Path = Avalonia.Controls.Shapes.Path;

namespace Vstavka.Desktop;

internal sealed class PermissionRows : UserControl
{
    private static readonly Dictionary<string, (string Title, string Description, string Allowed, string Action, Symbol Icon)> Labels = new()
    {
        ["clipboard-access"] = ("ClipboardAccessTitle", "ClipboardAccessDescription", "Allowed", "Allow", Symbol.Clipboard),
        ["accessibility"] = ("PasteForYou", "PasteForYouDescription", "StatusOn", "OpenSettings", Symbol.Keyboard),
        ["gnome-helper"] = ("GnomeHelperTitle", "GnomeHelperDescription", "Installed", "Install", Symbol.PuzzlePiece)
    };

    private readonly IPlatformServices _platform;
    private readonly DesktopHistory _history;
    private readonly bool? _animate;
    private readonly StartupRow? _startup;
    private readonly Action<Action> _signOut;
    private readonly DispatcherTimer _poll;
    private readonly StackPanel _list = new();
    private readonly Dictionary<string, PermissionRow> _rows = [];
    private bool? _hotkeyAvailable;

    public event Action<bool>? AvailabilityChanged;
    public event Action? HotkeyBecameAvailable;

    public PermissionRows(IPlatformServices platform, DesktopHistory history, bool? animate, StartupRow? startup = null,
        Action<Action>? signOut = null)
    {
        _platform = platform;
        _history = history;
        _animate = animate;
        _startup = startup;
        _signOut = signOut ?? (action => _ = history.SignOutAsync(action));
        Content = SetupVisuals.Surface(_list, 12);
        _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _poll.Tick += (_, _) => Refresh();
        AttachedToVisualTree += (_, _) =>
        {
            _history.GnomeHelperInstallChanged += Refresh;
            Refresh();
            _poll.Start();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _poll.Stop();
            _history.GnomeHelperInstallChanged -= Refresh;
        };
        Refresh();
    }

    public static string Title(PlatformPermission permission) =>
        Labels.TryGetValue(permission.Code, out var labels) ? Strings.Get(labels.Title) : permission.Code;

    public void Refresh()
    {
        var permissions = _history.Permissions.Where(permission => permission.State != PermissionState.NotNeeded).ToArray();
        var hotkeyAvailable = _platform.Capabilities.HasFlag(PlatformCapabilities.GlobalHotkey);
        if (hotkeyAvailable && _hotkeyAvailable == false)
            HotkeyBecameAvailable?.Invoke();
        _hotkeyAvailable = hotkeyAvailable;
        if (!_rows.Keys.SequenceEqual(permissions.Select(permission => permission.Code)))
        {
            _list.Children.Clear();
            _rows.Clear();
            foreach (var permission in permissions)
            {
                if (_rows.ContainsKey(permission.Code))
                    continue;
                var row = new PermissionRow(permission, _platform, _animate, Request);
                AddRow(row);
                _rows.Add(permission.Code, row);
            }
            if (_startup is not null)
                AddRow(_startup);
        }
        else if (_list.Children.Count == 0 && _startup is not null)
            AddRow(_startup);

        foreach (var permission in permissions)
        {
            var gnome = permission.Code == "gnome-helper";
            _rows[permission.Code].Update(permission, _history.AccessibilityRequested, gnome && _history.InstallingGnomeHelper,
                gnome && _history.GnomeHelperInstallFailed);
        }

        var visible = _list.Children.Count > 0;
        if (IsVisible != visible)
        {
            IsVisible = visible;
            AvailabilityChanged?.Invoke(visible);
        }
    }

    private void AddRow(Control row)
    {
        if (_list.Children.Count > 0)
        {
            var rule = new Border { Height = 1 };
            rule.Bind(Border.BackgroundProperty, rule.GetResourceObservable("LineBrush"));
            _list.Children.Add(rule);
        }
        _list.Children.Add(row);
    }

    private void Request(PlatformPermission permission)
    {
        if (permission.Request is not { } action)
            return;
        if (permission.State == PermissionState.RestartRequired)
        {
            _signOut(action);
            return;
        }
        if (permission.Code == "gnome-helper")
        {
            _ = _history.InstallGnomeHelperAsync(action);
            return;
        }
        if (permission.Code == "accessibility")
            _history.AccessibilityRequested = true;
        action();
        Refresh();
    }

    private sealed class PermissionRow : Grid
    {
        private readonly bool? _animate;
        private readonly Action<PlatformPermission> _request;
        private readonly string _allowedKey;
        private readonly string _actionKey;
        private readonly Border _illustration;
        private readonly Border _knob;
        private readonly TextBlock _restartHint;
        private readonly TextBlock _installError;
        private readonly Control _accessibility;
        private Control? _status;
        private PlatformPermission _permission;
        private bool _installing;

        public PermissionRow(PlatformPermission permission, IPlatformServices platform, bool? animate, Action<PlatformPermission> request)
        {
            _permission = permission;
            _request = request;
            _animate = animate;
            Name = permission.Code;
            ColumnDefinitions = new("Auto,*,Auto");
            ColumnSpacing = 8;
            Margin = new Thickness(12, 9);
            var known = Labels.TryGetValue(permission.Code, out var labels);
            var description = known ? Strings.Get(labels.Description)
                : platform.Notices.FirstOrDefault(notice => notice.Code == permission.Code)?.Message;
            var glyph = PanelVisuals.Glyph(known ? labels.Icon : Symbol.Shield, 17, "AccentBrush");
            var icon = SetupVisuals.Surface(glyph, 8, "SelectedSurfaceBrush", "SelectedSurfaceBrush");
            icon.Width = icon.Height = 34;
            icon.VerticalAlignment = VerticalAlignment.Center;
            SetupVisuals.Add(this, icon);
            _allowedKey = known ? labels.Allowed : "Allowed";
            _actionKey = known ? labels.Action : "Allow";

            var tag = SetupVisuals.Surface(SetupVisuals.Text(
                Strings.Get(permission.Requirement == PermissionRequirement.Required ? "Required" : "Optional"), 12, "MutedBrush"),
                5, "WindowBrush");
            tag.Padding = new Thickness(5, 0);
            tag.VerticalAlignment = VerticalAlignment.Center;
            var heading = new WrapPanel { ItemSpacing = 6, LineSpacing = 2 };
            heading.Children.Add(SetupVisuals.Text(Title(permission), 14, strong: true));
            heading.Children.Add(tag);
            var details = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { heading } };
            if (permission.Code == "accessibility")
                description = description?.Replace("⌘V", SetupVisuals.ShortcutInText("⌘V"), StringComparison.Ordinal);
            if (!string.IsNullOrWhiteSpace(description))
                details.Children.Add(SetupVisuals.Text(description, 12, "MutedBrush"));

            _restartHint = SetupVisuals.Text(Strings.Get(permission.Code == "gnome-helper"
                ? "GnomeRestartDescription" : "PermissionRestartDescription"), 12, "MutedBrush");
            details.Children.Add(_restartHint);
            _installError = SetupVisuals.Text(Strings.Get("GnomeInstallFailed"), 12, "WarningBrush");
            details.Children.Add(_installError);
            _knob = PanelVisuals.Dot("SurfaceBrush", 10);
            _knob.HorizontalAlignment = HorizontalAlignment.Left;
            _illustration = SetupVisuals.Surface(_knob, 8, "HoverLineBrush", "HoverLineBrush");
            _illustration.Width = 24;
            _illustration.Height = 14;
            _illustration.Padding = new Thickness(1);
            _illustration.FlowDirection = FlowDirection.LeftToRight;
            var accessibility = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 6, Margin = new Thickness(0, 2, 0, 0) };
            SetupVisuals.Add(accessibility, _illustration);
            SetupVisuals.Add(accessibility, SetupVisuals.Text(Strings.Get("AccessibilityInstruction"), 12, "MutedBrush"), 1);
            _accessibility = accessibility;
            details.Children.Add(accessibility);
            SetupVisuals.Add(this, details, 1);
        }

        public void Update(PlatformPermission permission, bool accessibilityRequested, bool installing, bool installFailed)
        {
            _restartHint.IsVisible = permission.State == PermissionState.RestartRequired;
            _installError.IsVisible = installFailed && permission.State == PermissionState.Missing;
            _accessibility.IsVisible = permission.Code == "accessibility" && accessibilityRequested;
            var stateChanged = _status is null || permission.State != _permission.State
                || (permission.Request is null) != (_permission.Request is null) || installing != _installing;
            var becameGranted = _status is not null && _permission.State != PermissionState.Granted
                && permission.State == PermissionState.Granted;
            _permission = permission;
            _installing = installing;
            if (!stateChanged)
                return;

            if (_status is not null)
                Children.Remove(_status);
            IsEnabled = !installing;
            _status = installing ? InstallingStatus() : permission.State switch
            {
                PermissionState.Granted => SetupVisuals.Horizontal(5, PanelVisuals.Glyph(Symbol.Checkmark, 14, "SuccessBrush"),
                    SetupVisuals.Text(Strings.Get(_allowedKey), 12, "SuccessBrush", strong: true)),
                PermissionState.RestartRequired => RestartStatus(),
                _ when permission.Request is not null =>
                    SetupVisuals.Button(_actionKey, () => _request(_permission), "permission", "Request-" + permission.Code),
                _ => SetupVisuals.Text(Strings.Get("PermissionNotAvailable"), 12, "MutedBrush")
            };
            _status.Name ??= "Status-" + permission.Code;
            _status.VerticalAlignment = VerticalAlignment.Center;
            _status.HorizontalAlignment = HorizontalAlignment.Right;
            SetupVisuals.Add(this, _status, 2);
            if (permission.State == PermissionState.Granted)
            {
                _illustration.Bind(Border.BackgroundProperty, _illustration.GetResourceObservable("PrimaryBrush"));
                _knob.HorizontalAlignment = HorizontalAlignment.Right;
            }
            // The grant happens in another window; the row marks what changed for the user coming back.
            if (becameGranted && (_animate ?? DesktopMotion.Enabled))
            {
                _ = DesktopMotion.Confirm(_status);
                if (_accessibility.IsVisible)
                    _ = DesktopMotion.Arrive(_knob, "translateX(-9px)", DesktopMotion.Enter, CancellationToken.None);
            }
        }

        private Control InstallingStatus()
        {
            var spinner = new Path
            {
                Name = "GnomeInstallSpinner", Width = 14, Height = 14, StrokeThickness = 1.75,
                Data = Geometry.Parse("M7,1 A6,6 0 1 1 1,7"), FlowDirection = FlowDirection.LeftToRight
            };
            spinner.Bind(Shape.StrokeProperty, spinner.GetResourceObservable("AccentBrush"));
            spinner.Classes.Set("setup-install-spinner", _animate ?? DesktopMotion.Enabled);
            return SetupVisuals.Horizontal(6, spinner, SetupVisuals.Text(Strings.Get("InstallingGnomeHelper"), 12, "MutedBrush"));
        }

        private Control RestartStatus()
        {
            var label = SetupVisuals.Text(Strings.Get("SignOutToFinish"), 12, "WarningBrush", strong: true);
            label.TextAlignment = TextAlignment.End;
            var status = new StackPanel { Spacing = 5, MaxWidth = 156, Children = { label } };
            if (_permission.Request is not null)
            {
                var button = SetupVisuals.Button("SignOut", () => _request(_permission), "permission", "Request-" + Name);
                button.Classes.Add("secondary");
                button.HorizontalAlignment = HorizontalAlignment.Right;
                status.Children.Add(button);
            }
            return status;
        }
    }
}

internal sealed class StartupRow : Grid
{
    private readonly ToggleButton _toggle;
    private readonly TextBlock _error;
    private readonly Func<bool, bool> _apply;
    private bool _reverting;

    public StartupRow(string platform, bool enabled, Func<bool, bool> apply)
    {
        _apply = apply;
        var titleKey = platform == "Windows" ? "StartWithWindows" : "OpenAtLogin";
        var descriptionKey = platform switch
        {
            "Windows" => "StartupWindowsDescription",
            "macOS" => "StartupMacDescription",
            _ => "StartupLinuxDescription"
        };
        _toggle = new ToggleButton { Name = "StartupToggle", Classes = { "setup-switch" }, IsChecked = enabled };
        AutomationProperties.SetName(_toggle, Strings.Get(titleKey));
        _toggle.IsCheckedChanged += (_, _) => Change();
        _toggle.TemplateApplied += (_, e) =>
        {
            if (DesktopMotion.Enabled && e.NameScope.Find<Border>("Knob") is { } knob)
                knob.Transitions = [new TransformOperationsTransition
                    { Property = RenderTransformProperty, Duration = DesktopMotion.Enter, Easing = DesktopMotion.Decelerate }];
        };
        _error = SetupVisuals.Text(Strings.Get("StartupChangeFailed"), 12, "WarningBrush");
        _error.Name = "StartupError";
        _error.IsVisible = false;
        var text = new StackPanel
        {
            Spacing = 2, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                SetupVisuals.Text(Strings.Get(titleKey), 14, strong: true),
                SetupVisuals.Text(Strings.Get(descriptionKey), 12, "MutedBrush"),
                _error
            }
        };
        Margin = new Thickness(12, 9);
        ColumnDefinitions = new("*,Auto");
        ColumnSpacing = 14;
        SetupVisuals.Add(this, text);
        SetupVisuals.Add(this, _toggle, 1);
        _toggle.VerticalAlignment = VerticalAlignment.Center;
    }

    public bool Enabled => _toggle.IsChecked == true;

    public void SetError(bool value, bool displayedValue)
    {
        _reverting = true;
        _toggle.IsChecked = displayedValue;
        _reverting = false;
        _error.IsVisible = value;
    }

    private void Change()
    {
        if (_reverting)
            return;
        var enabled = Enabled;
        if (!_apply(enabled))
            SetError(true, !enabled);
        else
            _error.IsVisible = false;
    }
}
