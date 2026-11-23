using Vstavka.Platform.MacOS;

if (!MacPlatformServices.IsSupported)
{
    Console.Error.WriteLine("Run this probe on macOS.");
    return 1;
}

Console.WriteLine($"OS: macOS {Environment.OSVersion.Version} ({System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture})");
var access = NativePasteboard.GetAccess();
Console.WriteLine($"Pasteboard accessBehavior: {access} ({(long)access})");
Console.WriteLine("Permissions:");
foreach (var permission in MacPermissions.System.GetPermissions())
    Console.WriteLine($"  {permission.Code}: {permission.Requirement}, {permission.State}");
var startup = MacStartupRegistration.System;
Console.WriteLine($"Login item: {startup.Status?.ToString() ?? "Unsupported"}");
return 0;
