#if VSTAVKA_WINDOWS_STORE
using System.Runtime.InteropServices;
using global::Windows.ApplicationModel;

namespace Vstavka.Platform.Windows;

internal sealed class WindowsPackageStartup : IStartupRegistration
{
    private const string TaskId = "VstavkaStartup";
    private const int InsufficientBuffer = 122;

    internal static bool IsPackaged
    {
        get
        {
            uint length = 0;
            return GetCurrentPackageFullName(ref length, 0) == InsufficientBuffer;
        }
    }

    public bool IsSupported => true;

    public bool IsEnabled => Query(async () => IsEnabledState((await StartupTask.GetAsync(TaskId)).State));

    public bool TrySetEnabled(bool enabled) => Query(async () =>
    {
        var task = await StartupTask.GetAsync(TaskId);
        if (enabled)
            return IsEnabledState(task.State) ||
                (task.State == StartupTaskState.Disabled && IsEnabledState(await task.RequestEnableAsync()));
        if (task.State == StartupTaskState.Enabled)
            task.Disable();
        return !IsEnabledState(task.State);
    });

    private static bool IsEnabledState(StartupTaskState state) =>
        state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;

    private static bool Query(Func<Task<bool>> action)
    {
        try
        {
            // Packaged desktop apps do not show the UWP consent dialog on this API.
            return Task.Run(action).GetAwaiter().GetResult();
        }
        catch (Exception error) when (error is COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, nint packageFullName);
}
#endif
