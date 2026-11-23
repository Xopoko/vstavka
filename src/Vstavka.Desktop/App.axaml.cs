using System.Buffers.Binary;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Vstavka.Core;
using Vstavka.Core.Localization;

namespace Vstavka.Desktop;

public sealed partial class App : Application
{
    private Window? _feedback;
    private DispatcherTimer? _feedbackTimer;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

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
        var instance = TryLock(platform.DataDirectory);
        if (instance is null)
        {
            platform.Dispose();
            Dispatcher.UIThread.Post(() => desktop.Shutdown());
            return;
        }

        history = new DesktopHistory(platform);
        if (PlatformFactory.IsDemo(args))
            DemoSamples.Seed(history.Store);
        var windows = new DesktopWindows(platform, history, ShowFeedback);

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
        using var iconStream = CreateTrayIcon();
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
        if (!history.Store.Settings.OnboardingCompleted && (!PlatformFactory.IsDemo(args) || args.Contains("--onboarding")))
            Dispatcher.UIThread.Post(windows.OpenOnboarding);
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
            Content = PanelVisuals.Feedback(text)
        };
        _feedback = feedback;
        feedback.Show();
        _feedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _feedbackTimer.Tick += (_, _) => CloseFeedback();
        _feedbackTimer.Start();
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

    private static MemoryStream CreateTrayIcon()
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(32, 32));
        using (var drawing = bitmap.CreateDrawingContext())
        {
            var blue = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#246AC5"));
            drawing.DrawRectangle(blue, null, new Rect(1, 1, 30, 30), 7, 7);
            var white = new Avalonia.Media.Pen(Avalonia.Media.Brushes.White, 3);
            drawing.DrawLine(white, new Point(8, 10), new Point(15, 23));
            drawing.DrawLine(white, new Point(15, 23), new Point(24, 10));
        }
        using var png = new MemoryStream();
        bitmap.Save(png, PngBitmapEncoderOptions.Default);
        // Win32's tray backend expects an ICO stream. PNG payloads inside ICO work on modern Windows.
        if (!OperatingSystem.IsWindows())
            return new MemoryStream(png.ToArray());
        var ico = new MemoryStream();
        Span<byte> header = stackalloc byte[22];
        header.Clear();
        header[2] = 1;
        header[4] = 1;
        header[6] = 32;
        header[7] = 32;
        header[10] = 1;
        header[12] = 32;
        BinaryPrimitives.WriteInt32LittleEndian(header[14..], (int)png.Length);
        BinaryPrimitives.WriteInt32LittleEndian(header[18..], header.Length);
        ico.Write(header);
        png.Position = 0;
        png.CopyTo(ico);
        ico.Position = 0;
        return ico;
    }
}
