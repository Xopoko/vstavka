using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FluentIcons.Common;
using Vstavka.Core;
using Vstavka.Core.Localization;

namespace Vstavka.Desktop;

internal sealed class ShortcutRecorder : UserControl
{
    private static readonly FontFamily ModifierSymbols =
        FontFamily.Parse("Segoe UI Symbol, Apple Symbols, Menlo, DejaVu Sans, Noto Sans Symbols 2, Noto Sans Symbols");

    private readonly DesktopHistory _history;
    private readonly string _platform;
    private readonly Action<string> _saved;
    private readonly bool? _animate;
    private readonly bool _large;
    private readonly bool _registerOnRecord;
    private readonly StackPanel _caps = SetupVisuals.Horizontal(Space.M);
    private readonly StackPanel _live = SetupVisuals.Horizontal(Space.Xs);
    private readonly Grid _field;
    private readonly Button _change;
    private readonly TextBlock _hint = SetupVisuals.Text("", TextSize.Caption, "MutedBrush");
    private readonly StackPanel _savedStatus;
    private readonly DispatcherTimer _savedTimer;

    public string Gesture { get; private set; }
    public bool IsRecording { get; private set; }
    public Control Keycaps => _caps;

    private string ShortcutAction => Gesture.Length == 0 ? "ChooseShortcut" : "ChangeShortcut";

    public ShortcutRecorder(DesktopHistory history, string platform, string gesture, Action<string> saved, bool? animate, bool large,
        bool registerOnRecord = true)
    {
        _history = history;
        _platform = platform;
        _saved = saved;
        _animate = animate;
        _large = large;
        _registerOnRecord = registerOnRecord;
        Gesture = gesture;
        Focusable = true;
        // The dashed field is the sign that the recorder has the keyboard; the system's focus frame would double it.
        FocusAdorner = null;
        _caps.FlowDirection = _live.FlowDirection = FlowDirection.LeftToRight;
        _caps.Name = "ShortcutKeycaps";
        _live.HorizontalAlignment = HorizontalAlignment.Center;
        var dashed = new Rectangle
        {
            RadiusX = Radius.Surface.TopLeft, RadiusY = Radius.Surface.TopLeft, StrokeThickness = 1.5, StrokeDashArray = [4, 3],
            IsHitTestVisible = false
        };
        dashed.Bind(Shape.StrokeProperty, dashed.GetResourceObservable("AccentBrush"));
        dashed.Bind(Shape.FillProperty, dashed.GetResourceObservable("SelectedSurfaceBrush"));
        var prompt = SetupVisuals.Text(Strings.Get("RecordShortcut"), strong: true);
        prompt.TextAlignment = _hint.TextAlignment = TextAlignment.Center;
        _hint.Name = "ShortcutHint";
        var message = new StackPanel
        {
            Spacing = Space.Xs, Margin = new Thickness(Space.M, Space.S), VerticalAlignment = VerticalAlignment.Center,
            Children = { prompt, _live, _hint }
        };
        _field = new Grid { Width = large ? 380 : double.NaN, MaxWidth = 380, MinHeight = 80, Children = { dashed, message } };
        _change = SetupVisuals.Button(ShortcutAction, ToggleRecording, name: "ChangeShortcut", symbol: Symbol.Keyboard);
        _change.HorizontalAlignment = HorizontalAlignment.Center;
        _savedStatus = SetupVisuals.Horizontal(Space.S, PanelVisuals.Glyph(Symbol.Checkmark, IconSize.Inline, "SuccessBrush"),
            SetupVisuals.Text(Strings.Get("ShortcutSaved"), TextSize.Caption, "SuccessBrush"));
        _savedStatus.HorizontalAlignment = HorizontalAlignment.Center;
        _savedStatus.IsVisible = false;
        _savedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
        _savedTimer.Tick += (_, _) => { _savedTimer.Stop(); _savedStatus.IsVisible = false; };

        var display = new Grid { MinHeight = large ? 82 : ControlSize.Default, Children = { _caps, _field } };
        _caps.HorizontalAlignment = large ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        if (large)
            Content = new StackPanel
            {
                Spacing = Space.S, HorizontalAlignment = HorizontalAlignment.Center,
                Children = { display, _change, _savedStatus }
            };
        else
        {
            var grid = new Grid { ColumnDefinitions = new("*,Auto"), RowDefinitions = new("Auto,Auto"), ColumnSpacing = Space.L };
            SetupVisuals.Add(grid, display);
            SetupVisuals.Add(grid, _change, 1);
            SetupVisuals.Add(grid, _savedStatus, 1, 1);
            Content = grid;
        }
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, (_, e) =>
        {
            if (IsRecording)
                ShowHeldModifiers(Modifiers(e.KeyModifiers) & ~Modifier(e.Key));
        }, RoutingStrategies.Tunnel);
        DetachedFromVisualTree += (_, _) =>
        {
            Cancel();
            _savedTimer.Stop();
        };
        Refresh();
    }

    public void Cancel()
    {
        if (IsRecording)
            SetRecording(false);
    }

    private void ToggleRecording()
    {
        SetRecording(!IsRecording);
        if (IsRecording)
            Focus(NavigationMethod.Directional);
    }

    private void SetRecording(bool recording)
    {
        IsRecording = recording;
        _history.SetRecording(this, recording);
        _savedStatus.IsVisible = false;
        _savedTimer.Stop();
        _hint.Text = Strings.Get("RecordEscapeHint");
        var label = Strings.Get(recording ? "Cancel" : ShortcutAction);
        _change.Content = new TextBlock { Text = label };
        if (!recording)
            _change.Content = SetupVisuals.Horizontal(Space.S, PanelVisuals.Glyph(Symbol.Keyboard, IconSize.Control),
                new TextBlock { Text = label });
        Avalonia.Automation.AutomationProperties.SetName(_change, label);
        _live.Children.Clear();
        Refresh();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsRecording)
            return;
        if (e.Key == Key.Tab)
        {
            Cancel();
            return;
        }
        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            Cancel();
            _change.Focus(NavigationMethod.Tab);
            return;
        }

        var mods = Modifiers(e.KeyModifiers) | Modifier(e.Key);
        ShowHeldModifiers(mods);
        if (Modifier(e.Key) != HotkeyModifiers.None || e.Key == Key.None)
            return;

        if (!TryReadGesture(e, out var gesture))
        {
            ShowError((mods & (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Meta)) == 0
                ? _platform == "macOS" ? "ShortcutModifierMac" : "ShortcutModifier"
                : "ShortcutUnsupported");
            return;
        }
        string requested = gesture.ToString();
        if (_registerOnRecord && !_history.TryRegisterShortcut(requested))
        {
            ShowError("ShortcutTaken");
            return;
        }

        Gesture = requested;
        SetRecording(false);
        _saved(Gesture);
        _savedStatus.IsVisible = true;
        _savedTimer.Start();
        _change.Focus(NavigationMethod.Tab);
    }

    private void ShowError(string key)
    {
        _hint.Text = Strings.Get(key);
        if (_animate ?? DesktopMotion.Enabled)
            _ = DesktopMotion.Reject(_field);
    }

    private void ShowHeldModifiers(HotkeyModifiers modifiers)
    {
        _live.Children.Clear();
        foreach (var label in Labels(modifiers, _platform))
            _live.Children.Add(Cap(label, false));
    }

    private void Refresh()
    {
        _field.IsVisible = IsRecording;
        _caps.IsVisible = !IsRecording;
        _caps.Children.Clear();
        foreach (var label in GestureLabels(Gesture, _platform))
            _caps.Children.Add(Cap(label, _large));
    }

    public static Control CompactKeycaps(string gesture, string platform)
    {
        var strip = SetupVisuals.Horizontal(Space.Xs);
        strip.FlowDirection = FlowDirection.LeftToRight;
        foreach (var label in GestureLabels(gesture, platform))
            strip.Children.Add(Cap(label, false));
        return strip;
    }

    public static string GestureText(string gesture, string platform) =>
        string.Join(platform == "macOS" ? "" : "+", GestureLabels(gesture, platform));

    private static IEnumerable<string> GestureLabels(string gesture, string platform) =>
        HotkeyGesture.TryParse(gesture, out var parsed)
            ? Labels(parsed.Modifiers, platform).Append(parsed.Key == "Space" ? Strings.Get("SpaceKey") : parsed.Key)
            : gesture.Length == 0 ? [] : [gesture];

    private static IEnumerable<string> Labels(HotkeyModifiers modifiers, string platform)
    {
        bool mac = platform == "macOS";
        if (modifiers.HasFlag(HotkeyModifiers.Ctrl)) yield return mac ? "⌃" : "Ctrl";
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) yield return mac ? "⌥" : "Alt";
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) yield return mac ? "⇧" : "Shift";
        if (modifiers.HasFlag(HotkeyModifiers.Meta)) yield return mac ? "⌘" : platform is "Windows" or "Demo" ? "Win" : "Super";
    }

    private static Border Cap(string value, bool large)
    {
        var label = SetupVisuals.Text(value, large ? TextSize.Keycap : TextSize.Caption, strong: true);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        if (value is "⌃" or "⌥" or "⇧" or "⌘")
        {
            label.FontFamily = ModifierSymbols;
            TextOptions.SetTextRenderingMode(label, TextRenderingMode.Antialias);
        }
        var cap = SetupVisuals.Surface(label, large ? Radius.Surface : Radius.Small);
        cap.Height = large ? 60 : 24;
        cap.MinWidth = large ? 56 : 24;
        cap.Padding = new Thickness(large ? Space.L : Space.Xs, 0);
        // The thicker bottom edge is the side of the key.
        cap.BorderThickness = new Thickness(Stroke.Hairline, Stroke.Hairline, Stroke.Hairline, large ? 4 : 2);
        return cap;
    }

    private static HotkeyModifiers Modifiers(KeyModifiers value)
    {
        var result = HotkeyModifiers.None;
        if (value.HasFlag(KeyModifiers.Control)) result |= HotkeyModifiers.Ctrl;
        if (value.HasFlag(KeyModifiers.Alt)) result |= HotkeyModifiers.Alt;
        if (value.HasFlag(KeyModifiers.Shift)) result |= HotkeyModifiers.Shift;
        if (value.HasFlag(KeyModifiers.Meta)) result |= HotkeyModifiers.Meta;
        return result;
    }

    public static bool TryReadGesture(KeyEventArgs e, out HotkeyGesture gesture)
    {
        var name = e.Key switch
        {
            >= Key.D0 and <= Key.D9 => ((int)e.Key - (int)Key.D0).ToString(System.Globalization.CultureInfo.InvariantCulture),
            >= Key.NumPad0 and <= Key.NumPad9 => ((int)e.Key - (int)Key.NumPad0).ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => e.Key.ToString()
        };
        gesture = new HotkeyGesture(Modifiers(e.KeyModifiers), name);
        return gesture.IsUsable;
    }

    private static HotkeyModifiers Modifier(Key key) => key switch
    {
        Key.LeftCtrl or Key.RightCtrl => HotkeyModifiers.Ctrl,
        Key.LeftAlt or Key.RightAlt => HotkeyModifiers.Alt,
        Key.LeftShift or Key.RightShift => HotkeyModifiers.Shift,
        Key.LWin or Key.RWin => HotkeyModifiers.Meta,
        _ => HotkeyModifiers.None
    };
}
