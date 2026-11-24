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
    private readonly bool _animate;
    private readonly bool _large;
    private readonly StackPanel _caps = SetupVisuals.Horizontal(7);
    private readonly StackPanel _live = SetupVisuals.Horizontal(5);
    private readonly Grid _field;
    private readonly Button _change;
    private readonly TextBlock _hint = SetupVisuals.Text("", 12, "MutedBrush");
    private readonly StackPanel _savedStatus;
    private readonly DispatcherTimer _savedTimer;
    private CancellationTokenSource? _recordingMotion;

    public string Gesture { get; private set; }
    public bool IsRecording { get; private set; }
    public Control Keycaps => _caps;

    public ShortcutRecorder(DesktopHistory history, string platform, string gesture, Action<string> saved, bool animate, bool large)
    {
        _history = history;
        _platform = platform;
        _saved = saved;
        _animate = animate;
        _large = large;
        Gesture = gesture;
        Focusable = true;
        _caps.FlowDirection = _live.FlowDirection = FlowDirection.LeftToRight;
        _caps.Name = "ShortcutKeycaps";
        _live.HorizontalAlignment = HorizontalAlignment.Center;
        var dashed = new Rectangle
        {
            RadiusX = 14, RadiusY = 14, StrokeThickness = 1.5, StrokeDashArray = [4, 3],
            IsHitTestVisible = false
        };
        dashed.Bind(Shape.StrokeProperty, dashed.GetResourceObservable("AccentBrush"));
        dashed.Bind(Shape.FillProperty, dashed.GetResourceObservable("SelectedSurfaceBrush"));
        var prompt = SetupVisuals.Text(Strings.Get("RecordShortcut"), 14, strong: true);
        prompt.TextAlignment = _hint.TextAlignment = TextAlignment.Center;
        _hint.Name = "ShortcutHint";
        var message = new StackPanel
        {
            Spacing = 3, Margin = new Thickness(10, 6), VerticalAlignment = VerticalAlignment.Center,
            Children = { prompt, _live, _hint }
        };
        _field = new Grid { Width = large ? 380 : double.NaN, MaxWidth = 380, MinHeight = 80, Children = { dashed, message } };
        _change = SetupVisuals.Button("ChangeShortcut", ToggleRecording, name: "ChangeShortcut", symbol: Symbol.Keyboard);
        _change.HorizontalAlignment = HorizontalAlignment.Center;
        _savedStatus = SetupVisuals.Horizontal(5, PanelVisuals.Glyph(Symbol.Checkmark, 14, "SuccessBrush"),
            SetupVisuals.Text(Strings.Get("ShortcutSaved"), 12, "SuccessBrush"));
        _savedStatus.HorizontalAlignment = HorizontalAlignment.Center;
        _savedStatus.IsVisible = false;
        _savedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
        _savedTimer.Tick += (_, _) => { _savedTimer.Stop(); _savedStatus.IsVisible = false; };

        var display = new Grid { MinHeight = large ? 82 : 36, Children = { _caps, _field } };
        _caps.HorizontalAlignment = large ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        if (large)
            Content = new StackPanel
            {
                Spacing = 5, HorizontalAlignment = HorizontalAlignment.Center,
                Children = { display, _change, _savedStatus }
            };
        else
        {
            var grid = new Grid { ColumnDefinitions = new("*,Auto"), RowDefinitions = new("Auto,Auto"), ColumnSpacing = 10 };
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
        {
            Focus(NavigationMethod.Directional);
            if (_animate && _recordingMotion is { } motion)
                _ = BreatheAsync(_field.Children[0], motion.Token);
        }
    }

    private void SetRecording(bool recording)
    {
        IsRecording = recording;
        _history.SetRecording(this, recording);
        _savedStatus.IsVisible = false;
        _savedTimer.Stop();
        _recordingMotion?.Cancel();
        _recordingMotion?.Dispose();
        _recordingMotion = recording ? new CancellationTokenSource() : null;
        _hint.Text = Strings.Get("RecordEscapeHint");
        _change.Content = new TextBlock { Text = Strings.Get(recording ? "Cancel" : "ChangeShortcut") };
        if (!recording)
            _change.Content = SetupVisuals.Horizontal(7, PanelVisuals.Glyph(Symbol.Keyboard, 16),
                new TextBlock { Text = Strings.Get("ChangeShortcut") });
        Avalonia.Automation.AutomationProperties.SetName(_change, Strings.Get(recording ? "Cancel" : "ChangeShortcut"));
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
        if (!_history.TryRegisterShortcut(gesture.ToString()))
        {
            ShowError("ShortcutTaken");
            return;
        }

        Gesture = gesture.ToString();
        SetRecording(false);
        _saved(Gesture);
        _savedStatus.IsVisible = true;
        _savedTimer.Start();
        _change.Focus(NavigationMethod.Tab);
        if (_animate)
            _ = SetupVisuals.Animate(_caps, 300, CancellationToken.None,
                (0, 1, "scale(.95)"), (1, 1, "scale(1)"));
    }

    private void ShowError(string key)
    {
        _hint.Text = Strings.Get(key);
        if (_animate && _recordingMotion is { } motion)
            _ = SetupVisuals.Animate(_field, 360, motion.Token,
                (0, 1, "translateX(0px)"), (.2, 1, "translateX(-5px)"), (.4, 1, "translateX(5px)"),
                (.6, 1, "translateX(-3px)"), (.8, 1, "translateX(2px)"), (1, 1, "translateX(0px)"));
    }

    private static async Task BreatheAsync(Control field, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
            await SetupVisuals.Animate(field, 1700, token,
                (0, 1, "scale(1)"), (.52, .55, "scale(1)"), (1, 1, "scale(1)"));
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
        var strip = SetupVisuals.Horizontal(4);
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
            : [gesture];

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
        var label = SetupVisuals.Text(value, large ? 22 : 12, strong: true);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        if (value is "⌃" or "⌥" or "⇧" or "⌘")
        {
            label.FontFamily = ModifierSymbols;
            TextOptions.SetTextRenderingMode(label, TextRenderingMode.Antialias);
        }
        var cap = SetupVisuals.Surface(label, large ? 12 : 5);
        cap.Height = large ? 60 : 24;
        cap.MinWidth = large ? 56 : 24;
        cap.Padding = large ? new Thickness(12, 0) : new Thickness(5, 0);
        cap.BorderThickness = new Thickness(1, 1, 1, large ? 4 : 2);
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
