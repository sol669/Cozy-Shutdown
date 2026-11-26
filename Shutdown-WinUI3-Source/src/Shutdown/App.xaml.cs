using Microsoft.UI.Xaml;
using System;
using System.Threading;
using System.Linq;

namespace ShutdownApp;

public partial class App : Application
{
#if COZY_LOCAL_TEST
    internal static bool LocalTest => true;
#else
    internal static bool LocalTest => false;
#endif
#if DEBUG
    // Local UI harness: intentionally unavailable in Release builds.
    internal static bool Preview { get; } = Environment.GetCommandLineArgs().Contains("--preview");
#else
    internal const bool Preview = false;
#endif
    private Mutex? _singleInstance;
    private Window? _lifetimeWindow;
    internal static TrayService? Tray { get; private set; }
    internal static SettingsStore Settings { get; } = new();

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            SettingsStore.Log(e.Exception);
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string instance = LocalTest ? "LocalTest" : Preview ? "Preview" : "Singleton";
        _singleInstance = new Mutex(true, "sol669.CozyShutdown." + instance, out bool createdNew);
        if (!createdNew)
        {
            Exit();
            return;
        }

        Settings.Load();
        Settings.ApplyAutostart();
        CreateLifetimeWindow();
        Tray = new TrayService(Settings);
        Tray.Initialize();
        DesktopClockService.Initialize(Settings);
        if (LocalTest) Tray.ShowSettings("rdp");
        else if (Preview || Environment.GetCommandLineArgs().Contains("--settings")) Tray.ShowSettings();
    }

    private void CreateLifetimeWindow()
    {
        _lifetimeWindow = new Window();
        AppBranding.ApplyWindowIcons(_lifetimeWindow.AppWindow);
        _lifetimeWindow.AppWindow.IsShownInSwitchers = false;
        _lifetimeWindow.AppWindow.Move(new Windows.Graphics.PointInt32(-32000, -32000));
        _lifetimeWindow.Activate();
        _lifetimeWindow.AppWindow.Hide();
    }

    internal static void Quit()
    {
        DesktopClockService.Dispose();
        Tray?.Dispose();
        Current.Exit();
    }
}
