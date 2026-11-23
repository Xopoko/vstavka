using Vstavka.Core;
using System.Security.Principal;

namespace Vstavka.Platform.Windows;

public sealed class WindowsStoreProfile
{
    internal const string LegacyConsentFile = "history-consent-v1";
    private const string ConsentFile = "history-consent-v2";

    internal WindowsStoreProfile(string localAppData)
    {
        if (!Path.IsPathFullyQualified(localAppData))
            throw new InvalidOperationException("Windows did not return an absolute LocalAppData path.");
        DataDirectory = Path.Combine(localAppData, "Vstavka");
    }

    public string DataDirectory { get; }
    public bool HasClipboardConsent => HasLegacyConsent || File.Exists(Path.Combine(DataDirectory, ConsentFile));
    private bool HasLegacyConsent => File.Exists(Path.Combine(DataDirectory, LegacyConsentFile));

    public bool TryGrantClipboardConsent()
    {
        try
        {
            if (!HasClipboardConsent)
            {
                Directory.CreateDirectory(DataDirectory);
                using var marker = new FileStream(Path.Combine(DataDirectory, ConsentFile), FileMode.OpenOrCreate, FileAccess.Write);
            }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void ApplyLegacyPreferences(AppSettings settings)
    {
        if (!HasLegacyConsent || settings.OnboardingCompleted)
            return;
        settings.PanelShortcut ??= AppSettings.NormalizeHotkey(settings.Hotkey);
        settings.OnboardingCompleted = true;
        settings.OnboardingStep = 0;
    }

    public IDisposable? TryAcquireInstance(Action activate, bool notifyOwner)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The Windows Store profile requires Windows.");
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value ?? throw new InvalidOperationException("The current Windows user has no SID.");
        var name = $@"Local\Vstavka.{sid}";
        var mutex = new Mutex(false, name);
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Activate");
        if (TryOwn(mutex))
            return new Instance(mutex, signal, activate);

        if (notifyOwner)
            signal.Set();
        signal.Dispose();
        mutex.Dispose();
        return null;
    }

    private static bool TryOwn(Mutex mutex)
    {
        try { return mutex.WaitOne(0); }
        catch (AbandonedMutexException) { return true; }
    }

    private sealed class Instance(Mutex mutex, EventWaitHandle signal, Action activate) : IDisposable
    {
        private readonly RegisteredWaitHandle _activation = ThreadPool.RegisterWaitForSingleObject(signal,
            (_, _) => activate(), null, Timeout.Infinite, executeOnlyOnce: false);

        public void Dispose()
        {
            _activation.Unregister(null);
            signal.Dispose();
            mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }
}
