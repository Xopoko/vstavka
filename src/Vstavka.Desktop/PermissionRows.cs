using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FluentIcons.Common;
using Vstavka.Core.Localization;
using Vstavka.Platform;

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
    private readonly bool _animate;
    private readonly StartupRow? _startup;
    private readonly DispatcherTimer _poll;
    private readonly StackPanel _list = new();
    private readonly Dictionary<string, PermissionRow> _rows = [];
    private bool? _hotkeyAvailable;
    private bool _accessibilityRequested;

    public event Action<bool>? AvailabilityChanged;
    public event Action? HotkeyBecameAvailable;

    public PermissionRows(IPlatformServices platform, bool animate, StartupRow? startup = null)
    {
        _platform = platform;
        _animate = animate;
        _startup = startup;
        Content = SetupVisuals.Surface(_list, 12);
        _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _poll.Tick += (_, _) => Refresh();
        AttachedToVisualTree += (_, _) => { Refresh(); _poll.Start(); };
        DetachedFromVisualTree += (_, _) => _poll.Stop();
        Refresh();
    }

    public void Refresh()
    {
        var permissions = _platform.Permissions.Where(permission => permission.State != PermissionState.NotNeeded).ToArray();
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
            _rows[permission.Code].Update(permission, _accessibilityRequested);

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
        if (permission.Code == "accessibility")
            _accessibilityRequested = true;
        permission.Request?.Invoke();
        Refresh();
    }

    private sealed class PermissionRow : Grid
    {
        private readonly bool _animate;
        private readonly Action<PlatformPermission> _request;
        private readonly string _allowedKey;
        private readonly string _actionKey;
        private readonly Border _illustration;
        private readonly Border _knob;
        private readonly TextBlock _waiting;
        private readonly Control _accessibility;
        private Control? _status;
        private PlatformPermission _permission;

        public PermissionRow(PlatformPermission permission, IPlatformServices platform, bool animate, Action<PlatformPermission> request)
        {
            _permission = permission;
            _request = request;
            _animate = animate;
            Name = permission.Code;
            ColumnDefinitions = new("Auto,*,Auto");
            ColumnSpacing = 8;
            Margin = new Thickness(12, 9);
            var known = Labels.TryGetValue(permission.Code, out var labels);
            var title = known ? Strings.Get(labels.Title) : permission.Code;
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
            heading.Children.Add(SetupVisuals.Text(title, 14, strong: true));
            heading.Children.Add(tag);
            var details = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { heading } };
            if (permission.Code == "accessibility")
                description = description?.Replace("⌘V", SetupVisuals.ShortcutInText("⌘V"), StringComparison.Ordinal);
            if (!string.IsNullOrWhiteSpace(description))
                details.Children.Add(SetupVisuals.Text(description, 12, "MutedBrush"));

            _waiting = SetupVisuals.Text(Strings.Get("GnomeSignOut"), 12, "MutedBrush");
            details.Children.Add(_waiting);
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
            Update(permission, false);
        }

        public void Update(PlatformPermission permission, bool accessibilityRequested)
        {
            _waiting.IsVisible = permission.Code == "gnome-helper" && permission.State == PermissionState.Missing && permission.Request is null;
            _accessibility.IsVisible = permission.Code == "accessibility" && accessibilityRequested;
            var stateChanged = _status is null || permission.State != _permission.State || (permission.Request is null) != (_permission.Request is null);
            var becameGranted = _status is not null && _permission.State == PermissionState.Missing && permission.State == PermissionState.Granted;
            _permission = permission;
            if (!stateChanged)
                return;

            if (_status is not null)
                Children.Remove(_status);
            var granted = permission.State == PermissionState.Granted;
            _status = granted
                ? SetupVisuals.Horizontal(5, PanelVisuals.Glyph(Symbol.Checkmark, 14, "SuccessBrush"),
                    SetupVisuals.Text(Strings.Get(_allowedKey), 12, "SuccessBrush", strong: true))
                : permission.Request is not null
                    ? SetupVisuals.Button(_actionKey, () => _request(_permission), "permission", "Request-" + permission.Code)
                    : SetupVisuals.Text(Strings.Get("Waiting"), 12, "MutedBrush");
            _status.Name ??= "Status-" + permission.Code;
            _status.VerticalAlignment = VerticalAlignment.Center;
            _status.HorizontalAlignment = HorizontalAlignment.Right;
            SetupVisuals.Add(this, _status, 2);
            if (granted)
            {
                _illustration.Bind(Border.BackgroundProperty, _illustration.GetResourceObservable("PrimaryBrush"));
                _knob.HorizontalAlignment = HorizontalAlignment.Right;
            }
            if (becameGranted && _animate)
            {
                _ = SetupVisuals.Animate(_status, 320, CancellationToken.None,
                    (0, 0, "scale(.85)"), (1, 1, "scale(1)"));
                if (_accessibility.IsVisible)
                    _ = SetupVisuals.Animate(_knob, 320, CancellationToken.None,
                        (0, 1, "translateX(-9px)"), (1, 1, "translateX(0px)"));
            }
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
