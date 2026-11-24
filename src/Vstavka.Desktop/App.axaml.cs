using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Vstavka.Core;
using Vstavka.Core.Localization;
using Vstavka.Platform.Windows;

namespace Vstavka.Desktop;

public sealed partial class App : Application
{
    private Window? _feedback;
    private DispatcherTimer? _feedbackTimer;

    public override void Initialize()
    {
        Animation.RegisterCustomAnimator<ITransform, TransformKeyFrameAnimator>();
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var args = desktop.Args ?? [];
        DesktopHistory? history = null;
        var defaultSettings = new AppSettings();
        var platform = PlatformFactory.Create(args, () => history?.Store.Settings ?? defaultSettings);
        var storeProfile = (platform as WindowsPlatformServices)?.StoreProfile;
        var background = args.Contains("--startup") || args.Contains("--background");
        Action? activate = null;
        var instance = storeProfile is null ? TryLock(platform.DataDirectory) :
            storeProfile.TryAcquireInstance(() => Dispatcher.UIThread.Post(() => activate?.Invoke()), !background);
        if (instance is null)
        {
            platform.Dispose();
            Dispatcher.UIThread.Post(() => desktop.Shutdown());
            return;
        }

        history = new DesktopHistory(platform);
        if (history.ClipboardConsentRequired && background)
        {
            platform.Dispose();
            instance.Dispose();
            Dispatcher.UIThread.Post(() => desktop.Shutdown());
            return;
        }
        if (PlatformFactory.IsDemo(args))
            DemoSamples.Seed(history.Store);
        var windows = new DesktopWindows(platform, history, ShowFeedback);
        activate = () =>
        {
            if (history.ClipboardConsentRequired)
                windows.OpenOnboarding();
            else
                windows.OpenPanel();
        };

        var open = new NativeMenuItem(Strings.Get("TrayOpen"));
        var pause = new NativeMenuItem(Strings.Get("Pause"));
        var settings = new NativeMenuItem(Strings.Get("TraySettings"));
        var welcome = new NativeMenuItem(Strings.Get("WelcomeGuide"));
        var quit = new NativeMenuItem(Strings.Get("Exit"));
        open.Click += (_, _) => windows.OpenPanel();
        pause.Click += (_, _) => history.TogglePause();
        settings.Click += (_, _) => windows.OpenSettings();
        welcome.Click += (_, _) => windows.OpenOnboarding();
        quit.Click += (_, _) => desktop.Shutdown();
        history.Changed += () => pause.Header = Strings.Get(history.Paused ? "Resume" : "Pause");
        var iconFormat = OperatingSystem.IsWindows() ? "ico" : "png";
        using var iconStream = AssetLoader.Open(new Uri($"avares://Vstavka.Desktop/Assets/Vstavka.{iconFormat}"));
        var tray = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            ToolTipText = Strings.Get("AppName"),
            Menu = new NativeMenu { Items = { open, pause, new NativeMenuItemSeparator(), settings, welcome, new NativeMenuItemSeparator(), quit } },
            IsVisible = true
        };
        tray.Clicked += (_, _) => windows.OpenPanel();
        history.SettingsChanged += () =>
        {
            open.Header = Strings.Get("TrayOpen");
            settings.Header = Strings.Get("TraySettings");
            welcome.Header = Strings.Get("WelcomeGuide");
            quit.Header = Strings.Get("Exit");
        };
        TrayIcon.SetIcons(this, new TrayIcons { tray });
        desktop.Exit += (_, _) =>
        {
            try
            {
                history.Stop();
            }
            finally
            {
                CloseFeedback();
                tray.Dispose();
                platform.Dispose();
                instance.Dispose();
            }
        };
        history.Start();
        if ((history.ClipboardConsentRequired || !history.Store.Settings.OnboardingCompleted) &&
            (!PlatformFactory.IsDemo(args) || args.Contains("--onboarding")))
            Dispatcher.UIThread.Post(windows.OpenOnboarding);
        else if (storeProfile is not null && !background)
            Dispatcher.UIThread.Post(windows.OpenPanel);
    }

    private void ShowFeedback(string text, PanelWindow panel)
    {
        CloseFeedback();
        var screen = panel.Screens.Primary;
        if (screen is null)
            return;

        const int width = 360;
        const int height = 64;
        var work = screen.WorkingArea;
        var scale = screen.Scaling;
        var message = PanelVisuals.Feedback(text);
        var feedback = new Window
        {
            Title = "Vstavka",
            Width = width,
            Height = height,
            CanResize = false,
            WindowDecorations = WindowDecorations.None,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            Background = Avalonia.Media.Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            FlowDirection = panel.FlowDirection,
            Position = new PixelPoint(work.Right - (int)Math.Ceiling((width + 16) * scale),
                work.Bottom - (int)Math.Ceiling((height + 16) * scale)),
            Content = message
        };
        _feedback = feedback;
        feedback.Show();
        // The message answers an action, so it is there at once. Nobody sends it away: it leaves on its own while
        // the user works elsewhere, and a fade keeps that from catching the eye.
        var timer = _feedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        var leaving = false;
        timer.Tick += (_, _) =>
        {
            if (leaving || !DesktopMotion.Enabled)
            {
                CloseFeedback();
                return;
            }
            leaving = true;
            message.Transitions = [new DoubleTransition { Property = Visual.OpacityProperty, Duration = DesktopMotion.Travel }];
            message.Opacity = 0;
            timer.Interval = DesktopMotion.Travel;
        };
        timer.Start();
    }

    private void CloseFeedback()
    {
        _feedbackTimer?.Stop();
        _feedbackTimer = null;
        _feedback?.Close();
        _feedback = null;
    }

    private static FileStream? TryLock(string directory)
    {
        Directory.CreateDirectory(directory);
        try
        {
            return new FileStream(Path.Combine(directory, "desktop.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
    }
}
