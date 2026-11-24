using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Vstavka.Core.Localization;
using Vstavka.Platform;

namespace Vstavka.Desktop;

internal sealed class DesktopWindows
{
    private readonly IPlatformServices _platform;
    private readonly DesktopHistory _history;
    private readonly bool? _animate;
    private readonly bool _positionPanel;
    private readonly Action<string, PanelWindow>? _feedback;

    public PanelWindow Panel { get; private set; }
    public OnboardingWindow? Onboarding { get; private set; }
    public SettingsWindow? Settings { get; private set; }

    public DesktopWindows(IPlatformServices platform, DesktopHistory history, Action<string, PanelWindow>? feedback = null,
        bool? animate = null, bool positionPanel = true)
    {
        _platform = platform;
        _history = history;
        _feedback = feedback;
        _animate = animate;
        _positionPanel = positionPanel;
        Strings.SetLanguage(history.Store.Settings.Language);
        SetupVisuals.ApplyTheme(history.Store.Settings.Theme);
        Panel = CreatePanel();
        history.ToggleRequested += ToggleFromShortcut;
        history.SettingsChanged += ApplySettings;
        if (platform.ClipboardSounds is { } sounds)
        {
            sounds.PasteShortcutPressed += () => Dispatcher.UIThread.Post(PlayKeyboardPasteSound);
            UpdatePasteShortcutSounds();
        }
    }

    public void OpenPanel() => Panel.Open(_positionPanel, Onboarding?.PracticeItem);

    public void OpenSettings()
    {
        if (Settings is { } existing)
        {
            existing.Activate();
            return;
        }
        var window = new SettingsWindow(_platform, _history, _animate);
        Settings = window;
        window.Closing += (_, e) =>
        {
            if (!e.Cancel && (e.CloseReason is WindowCloseReason.WindowClosing or WindowCloseReason.Undefined) && !Panel.IsVisible)
                _history.TryRequestStoreReview();
        };
        window.Closed += (_, _) => Settings = null;
        window.Show();
    }

    public void OpenOnboarding()
    {
        if (Onboarding is { } existing)
        {
            existing.Activate();
            return;
        }
        var window = new OnboardingWindow(_platform, _history, _animate);
        if (_platform is DemoPlatformServices)
            window.AddHandler(InputElement.KeyDownEvent, (_, e) =>
            {
                if (window.Practicing && ShortcutRecorder.TryReadGesture(e, out var gesture) && gesture.ToString() == _history.Shortcut)
                {
                    ToggleFromShortcut();
                    e.Handled = true;
                }
            }, RoutingStrategies.Tunnel);
        Onboarding = window;
        window.Closed += (_, _) => Onboarding = null;
        window.Show();
    }

    private void ToggleFromShortcut()
    {
        if (Panel.IsOpen)
        {
            Panel.Dismiss();
            return;
        }

        OpenPanel();
        if (Panel.IsOpen)
            Onboarding?.ShortcutOpened();
    }

    private PanelWindow CreatePanel()
    {
        var panel = new PanelWindow(_platform, _history, _animate);
        panel.SettingsRequested += OpenSettings;
        panel.Feedback += text => _feedback?.Invoke(text, panel);
        panel.PracticeTarget = () => Onboarding is { Practicing: true } window ? window : null;
        return panel;
    }

    private void ApplySettings()
    {
        var settings = _history.Store.Settings;
        UpdatePasteShortcutSounds();
        if (Strings.Language != settings.Language)
        {
            Strings.SetLanguage(settings.Language);
            Panel.Close();
            Panel = CreatePanel();
        }
        SetupVisuals.ApplyTheme(settings.Theme);
        Panel.ApplyAppearance();
    }

    private void UpdatePasteShortcutSounds()
    {
        var settings = _history.Store.Settings;
        _platform.ClipboardSounds?.WatchPasteShortcuts(settings.PasteSoundEnabled && settings.Volume > 0);
    }

    private void PlayKeyboardPasteSound()
    {
        var settings = _history.Store.Settings;
        if (settings.PasteSoundEnabled && !Panel.IsActive && Settings?.IsActive != true && Onboarding?.IsActive != true)
            _platform.ClipboardSounds?.Play(paste: true, settings.Volume);
    }
}
