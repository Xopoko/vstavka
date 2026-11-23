using System;
using System.Threading.Tasks;
using Microsoft.Win32;
using Vstavka.Core.Localization;

namespace Vstavka.Services;

internal static class StartupService
{
    private const string RunPath=@"Software\Microsoft\Windows\CurrentVersion\Run";
    internal static async Task<string?> SetEnabledAsync(bool enabled)
    {
        try
        {
            if(Native.IsPackaged)
            {
                var task=await Windows.ApplicationModel.StartupTask.GetAsync("VstavkaStartup");
                if(!enabled)task.Disable();
                else
                {
                    var state=await task.RequestEnableAsync();
                    if(state!=Windows.ApplicationModel.StartupTaskState.Enabled && state!=Windows.ApplicationModel.StartupTaskState.EnabledByPolicy)
                        return Strings.Get("StartupDisabled");
                }
            }
            else
            {
                using var key=Registry.CurrentUser.CreateSubKey(RunPath);
                if(enabled) key.SetValue("Vstavka",$"\"{Environment.ProcessPath}\" --background");
                else key.DeleteValue("Vstavka",false);
            }
            return null;
        }
        catch {return Strings.Get("StartupFailed");}
    }
}
