using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace ShutdownApp;

public sealed class TrayService : IDisposable
{
    private const uint TrayMessage = NativeMethods.WM_APP + 1;
    private const int HotkeyId = 669;
    private const uint ActionBase = 1100;
    private const uint ScheduleBase = 2100;
    private const uint IdCancelScheduled = 2900;
    private const uint IdRdp = 3001;
    private const uint IdSettings = 3002;
    private const uint IdExit = 3003;

    private readonly SettingsStore _settings;
    private readonly NativeMethods.WndProc _wndProc;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _schedulerTimer;
    private nint _window;
    private nint _trayIcon;
    private bool _ownsTrayIcon;
    private string? _trayIconKey;
    private uint _taskbarCreatedMessage;
    private NativeMethods.NOTIFYICONDATA _notifyData;
    private SettingsWindow? _settingsWindow;
    private PowerActionKind? _scheduledAction;
    private DateTime? _scheduledFor;
    private DateTime _lastScheduleCheck = DateTime.Now;
    private bool _warningOpen;
    private int _scheduleGeneration;

    public TrayService(SettingsStore settings)
    {
        _settings = settings;
        _wndProc = WindowProc;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _schedulerTimer = _dispatcher.CreateTimer();
        _schedulerTimer.Interval = TimeSpan.FromSeconds(1);
        _schedulerTimer.Tick += (_, _) => SchedulerTick();
    }

    public void Initialize()
    {
        const string className = "sol669.Shutdown.TrayWindow";
        nint instance = NativeMethods.GetModuleHandle(null);
        var wc = new NativeMethods.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(), lpfnWndProc = _wndProc,
            hInstance = instance, lpszClassName = className
        };
        NativeMethods.RegisterClassEx(ref wc);
        _window = NativeMethods.CreateWindowEx(0, className, "Shutdown Trey", 0, 0, 0, 0, 0,
            nint.Zero, nint.Zero, instance, nint.Zero);
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        LoadTrayIcon();
        _notifyData = new NativeMethods.NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(), hWnd = _window, uID = 1,
            uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP,
            uCallbackMessage = TrayMessage, hIcon = _trayIcon,
            szTip = Strings.TrayTip(_settings.Current.DefaultAction, null, null),
            szInfo = string.Empty, szInfoTitle = string.Empty
        };
        AddTrayIcon();
        RefreshHotkey();
        _schedulerTimer.Start();
    }

    private void LoadTrayIcon()
    {
        if (NativeMethods.GetSystemMetrics(NativeMethods.SM_REMOTESESSION) != 0)
        {
            const string systemIconKey = "rdp_disconnect";
            if (_trayIconKey == systemIconKey && _trayIcon != nint.Zero) return;
            DestroyTrayIcon();
            var smallIcons = new nint[1];
            string disconnectTool = Path.Combine(Environment.SystemDirectory, "tsdiscon.exe");
            if (NativeMethods.ExtractIconEx(disconnectTool, 0, null, smallIcons, 1) != 0)
            {
                _trayIcon = smallIcons[0];
                _ownsTrayIcon = _trayIcon != nint.Zero;
                _trayIconKey = systemIconKey;
                return;
            }
        }

        string action = _settings.Current.DefaultAction.ToString().ToLowerInvariant();
        string scheduled = _scheduledAction is null ? string.Empty : "_scheduled";
        string tone = NativeTheme.IsTaskbarDark() ? "white" : "black";
        string key = $"tray_{action}{scheduled}_{tone}.ico";
        if (_trayIconKey == key && _trayIcon != nint.Zero) return;
        DestroyTrayIcon();
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", key);
        _trayIcon = NativeMethods.LoadImage(nint.Zero, path, NativeMethods.IMAGE_ICON, 0, 0,
            NativeMethods.LR_LOADFROMFILE | NativeMethods.LR_DEFAULTSIZE);
        _ownsTrayIcon = _trayIcon != nint.Zero;
        _trayIconKey = key;
    }

    private void DestroyTrayIcon()
    {
        if (_trayIcon != nint.Zero && _ownsTrayIcon) NativeMethods.DestroyIcon(_trayIcon);
        _trayIcon = nint.Zero;
        _ownsTrayIcon = false;
    }

    private void AddTrayIcon()
    {
        if (_window == nint.Zero) return;
        _notifyData.uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP;
        _notifyData.uCallbackMessage = TrayMessage;
        _notifyData.hIcon = _trayIcon;
        _notifyData.szTip = Strings.TrayTip(_settings.Current.DefaultAction, _scheduledAction, _scheduledFor);
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref _notifyData);
    }

    public void RefreshHotkey()
    {
        if (_window == nint.Zero) return;
        NativeMethods.UnregisterHotKey(_window, HotkeyId);
        if (_settings.Current.ShowRdpMenu)
            NativeMethods.RegisterHotKey(_window, HotkeyId,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT, NativeMethods.VK_Q);
    }

    public void RefreshAfterSettingsChanged()
    {
        RefreshHotkey();
        if (_scheduledAction is not null &&
            (!_settings.Current.ShowScheduledActions ||
             !_settings.Current.EnabledActions.HasFlag(_scheduledAction.Value.ToFlag()) ||
             !SystemActions.IsAvailable(_scheduledAction.Value)))
            CancelSchedule(true);
        LoadTrayIcon();
        UpdateTray();
    }

    private nint WindowProc(nint hWnd, uint msg, nuint wParam, nint lParam)
    {
        try
        {
            if (msg == _taskbarCreatedMessage)
            {
                // Explorer lost its icon table; register again without restarting the app.
                AddTrayIcon();
                return nint.Zero;
            }
            if (msg == TrayMessage)
            {
                uint mouseMessage = unchecked((uint)lParam.ToInt64());
                if (mouseMessage == NativeMethods.WM_RBUTTONUP) ShowMenu();
                else if (mouseMessage == NativeMethods.WM_LBUTTONDBLCLK)
                    _dispatcher.TryEnqueue(() => _ = PerformPowerActionAsync(_settings.Current.DefaultAction));
                return nint.Zero;
            }
            if (msg == NativeMethods.WM_HOTKEY && (int)wParam == HotkeyId)
            {
                _dispatcher.TryEnqueue(SystemActions.DisconnectRdp);
                return nint.Zero;
            }
            if (msg == NativeMethods.WM_POWERBROADCAST && (uint)wParam == NativeMethods.PBT_APMRESUMEAUTOMATIC)
            {
                _dispatcher.TryEnqueue(HandleResume);
                return nint.Zero;
            }
            if (msg == NativeMethods.WM_DESTROY) return nint.Zero;
        }
        catch (Exception ex) { SettingsStore.Log(ex); }
        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private IEnumerable<PowerActionKind> EnabledActions()
    {
        foreach (PowerActionKind action in Enum.GetValues<PowerActionKind>())
            if (_settings.Current.EnabledActions.HasFlag(action.ToFlag()) && SystemActions.IsAvailable(action))
                yield return action;
    }

    private void ShowMenu()
    {
        NativeTheme.Apply(_settings.Current.Theme, _window);
        nint menu = NativeMethods.CreatePopupMenu();
        try
        {
            foreach (var action in EnabledActions())
                NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, ActionBase + (uint)action, Strings.ActionName(action));
            NativeMethods.SetMenuDefaultItem(menu, ActionBase + (uint)_settings.Current.DefaultAction, 0);

            if (_settings.Current.ShowScheduledActions)
            {
                NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, 0, null);
                if (_scheduledAction is not null && _scheduledFor is not null)
                {
                    NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING | NativeMethods.MF_GRAYED, 0,
                        Strings.ScheduledStatus(_scheduledAction.Value, _scheduledFor.Value));
                    NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, IdCancelScheduled, Strings.CancelScheduled);
                }
                nint scheduledMenu = NativeMethods.CreatePopupMenu();
                foreach (var action in EnabledActions())
                {
                    nint actionMenu = NativeMethods.CreatePopupMenu();
                    uint root = ScheduleBase + (uint)action * 10;
                    NativeMethods.AppendMenu(actionMenu, NativeMethods.MF_STRING, root, Strings.In30Minutes);
                    NativeMethods.AppendMenu(actionMenu, NativeMethods.MF_STRING, root + 1, Strings.In1Hour);
                    NativeMethods.AppendMenu(actionMenu, NativeMethods.MF_STRING, root + 2, Strings.In3Hours);
                    NativeMethods.AppendMenu(actionMenu, NativeMethods.MF_SEPARATOR, 0, null);
                    NativeMethods.AppendMenu(actionMenu, NativeMethods.MF_STRING, root + 3, Strings.CustomInterval);
                    NativeMethods.AppendMenu(actionMenu, NativeMethods.MF_STRING, root + 4, Strings.ChooseDateTime);
                    NativeMethods.AppendMenu(scheduledMenu, NativeMethods.MF_POPUP, (nuint)actionMenu, Strings.ActionName(action));
                }
                NativeMethods.AppendMenu(menu, NativeMethods.MF_POPUP, (nuint)scheduledMenu, Strings.ScheduledAction);
            }

            if (_settings.Current.ShowRdpMenu)
                NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, IdRdp, Strings.DisconnectRdp + "\tCtrl+Alt+Shift+Q");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, IdSettings, Strings.Settings);
            NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, 0, null);
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, IdExit, Strings.Exit);
            NativeMethods.GetCursorPos(out var point);
            NativeMethods.SetForegroundWindow(_window);
            uint command = NativeMethods.TrackPopupMenu(menu, NativeMethods.TPM_RIGHTBUTTON | NativeMethods.TPM_RETURNCMD,
                point.X, point.Y, 0, _window, nint.Zero);
            NativeMethods.PostMessage(_window, 0, 0, 0);
            _dispatcher.TryEnqueue(() => ExecuteCommand(command));
        }
        finally { NativeMethods.DestroyMenu(menu); }
    }

    private void ExecuteCommand(uint command)
    {
        if (command >= ActionBase && command < ActionBase + 5)
        {
            _ = PerformPowerActionAsync((PowerActionKind)(command - ActionBase));
            return;
        }
        if (command >= ScheduleBase && command < ScheduleBase + 50)
        {
            uint value = command - ScheduleBase;
            _ = ScheduleCommandAsync((PowerActionKind)(value / 10), (int)(value % 10));
            return;
        }
        switch (command)
        {
            case IdCancelScheduled: CancelSchedule(true); break;
            case IdRdp: SystemActions.DisconnectRdp(); break;
            case IdSettings: ShowSettings(); break;
            case IdExit: _ = ExitAsync(); break;
        }
    }

    private async Task ExitAsync()
    {
        if (_scheduledAction is not null && !await ConfirmWindow.ShowMessageAsync(Strings.ExitWithScheduleQuestion))
            return;
        App.Quit();
    }

    private async Task PerformPowerActionAsync(PowerActionKind action)
    {
        var current = _settings.Current;
        bool confirmed = current.ConfirmationMode switch
        {
            ConfirmationMode.None => true,
            ConfirmationMode.Ask => await ConfirmWindow.ShowAsync(action, null),
            _ => await ConfirmWindow.ShowAsync(action, current.CountdownSeconds)
        };
        if (confirmed) SystemActions.Execute(action);
    }

    private async Task ScheduleCommandAsync(PowerActionKind action, int option)
    {
        DateTime? when = option switch
        {
            0 => DateTime.Now.AddMinutes(30),
            1 => DateTime.Now.AddHours(1),
            2 => DateTime.Now.AddHours(3),
            3 => await ScheduleWindow.ShowAsync(action, false),
            4 => await ScheduleWindow.ShowAsync(action, true),
            _ => null
        };
        if (when is null) return;
        if (_scheduledFor is not null && !await ConfirmWindow.ShowMessageAsync(Strings.ReplaceScheduleQuestion(_scheduledFor.Value)))
            return;
        _scheduledAction = action;
        _scheduledFor = when;
        _warningOpen = false;
        _lastScheduleCheck = DateTime.Now;
        _scheduleGeneration++;
        UpdateTray();
        ShowNotification(Strings.ScheduledNotification(action, when.Value));
    }

    private void SchedulerTick()
    {
        DateTime now = DateTime.Now;
        if (_scheduledAction is null || _scheduledFor is null)
        {
            _lastScheduleCheck = now;
            return;
        }
        UpdateTray();
        TimeSpan gap = now - _lastScheduleCheck;
        _lastScheduleCheck = now;
        if (now >= _scheduledFor.Value && gap > TimeSpan.FromSeconds(90))
        {
            HandleMissedSchedule();
            return;
        }
        if (!_warningOpen && now >= _scheduledFor.Value.AddSeconds(-30))
            _ = RunScheduledWarningAsync();
    }

    private async Task RunScheduledWarningAsync()
    {
        if (_scheduledAction is null || _scheduledFor is null) return;
        _warningOpen = true;
        int generation = _scheduleGeneration;
        PowerActionKind action = _scheduledAction.Value;
        int seconds = Math.Clamp((int)Math.Ceiling((_scheduledFor.Value - DateTime.Now).TotalSeconds), 1, 30);
        bool execute = await ConfirmWindow.ShowAsync(action, seconds);
        if (generation != _scheduleGeneration) return;
        if (!execute) { CancelSchedule(true); return; }
        ClearSchedule();
        SystemActions.Execute(action);
    }

    private void HandleResume()
    {
        if (_scheduledAction is not null && _scheduledFor is not null && DateTime.Now >= _scheduledFor.Value)
            HandleMissedSchedule();
        _lastScheduleCheck = DateTime.Now;
    }

    private void HandleMissedSchedule()
    {
        if (_scheduledAction is null) return;
        PowerActionKind action = _scheduledAction.Value;
        if (action is not PowerActionKind.Sleep and not PowerActionKind.Hibernate)
            ShowNotification(Strings.ScheduleMissed(action));
        ClearSchedule();
    }

    private void CancelSchedule(bool notify)
    {
        if (_scheduledAction is null) return;
        ClearSchedule();
        if (notify) ShowNotification(Strings.ScheduleCancelled);
    }

    private void ClearSchedule()
    {
        _scheduledAction = null;
        _scheduledFor = null;
        _warningOpen = false;
        _scheduleGeneration++;
        UpdateTray();
    }

    private void UpdateTray()
    {
        if (_window == nint.Zero) return;
        LoadTrayIcon();
        _notifyData.hIcon = _trayIcon;
        _notifyData.szTip = Strings.TrayTip(_settings.Current.DefaultAction, _scheduledAction, _scheduledFor);
        _notifyData.uFlags = NativeMethods.NIF_ICON | NativeMethods.NIF_TIP;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref _notifyData);
    }

    private void ShowNotification(string text)
    {
        _notifyData.uFlags = NativeMethods.NIF_INFO;
        _notifyData.szInfoTitle = "Shutdown Trey";
        _notifyData.szInfo = text;
        _notifyData.dwInfoFlags = NativeMethods.NIIF_INFO;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref _notifyData);
    }

    private void ShowSettings()
    {
        if (_settingsWindow is not null) { _settingsWindow.Activate(); return; }
        _settingsWindow = new SettingsWindow(_settings);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Activate();
    }

    public void Dispose()
    {
        _schedulerTimer.Stop();
        if (_window != nint.Zero)
        {
            NativeMethods.UnregisterHotKey(_window, HotkeyId);
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref _notifyData);
            NativeMethods.DestroyWindow(_window);
            _window = nint.Zero;
        }
        DestroyTrayIcon();
    }
}
