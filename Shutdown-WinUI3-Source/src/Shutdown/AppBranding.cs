using Microsoft.UI.Windowing;
using System;
using System.IO;

namespace ShutdownApp;

internal static class AppBranding
{
    internal const string Name = "Cozy Shutdown";
    internal const string AutostartValueName = "Cozy Shutdown";
    internal static string IconPath => Path.Combine(AppContext.BaseDirectory, "Assets", "ShutdownTrey.ico");

    internal static void ApplyWindowIcons(AppWindow window)
    {
        window.SetIcon(IconPath);
        window.SetTaskbarIcon(IconPath);
    }
}
