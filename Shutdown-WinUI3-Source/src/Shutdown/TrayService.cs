using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Linq;
using System.Threading;

namespace ShutdownApp;

public sealed class TrayService : IDisposable
{
    private const uint TrayMessage = NativeMethods.WM_APP + 1;
    private const uint ActionBase = 1100;
    private const uint ScheduleBase = 2100;
    private const uint IdCancelScheduled = 2900;
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
    private bool _isRdpSession;
    private bool _scheduledRemote;
    private bool _actionInProgress;
    private CancellationTokenSource? _scheduledWarningCancellation;
    private List<PowerActionKind> CurrentActions => ActionPolicy.Menu(_settings.Current, _isRdpSession, SystemActions.IsAvailable);
    private PowerActionKind? CurrentDefault => CurrentActions.Count > 0 ? CurrentActions[0] : null;

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
        string className = App.Preview ? "sol669.CozyShutdown.PreviewTrayWindow" : "sol669.CozyShutdown.TrayWindow";
        nint instance = NativeMethods.GetModuleHandle(null);
        var wc = new NativeMethods.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(), lpfnWndProc = _wndProc,
            hInstance = instance, lpszClassName = className
        };
        NativeMethods.RegisterClassEx(ref wc);
        _window = NativeMethods.CreateWindowEx(0, className, AppBranding.Name, 0, 0, 0, 0, 0,
            nint.Zero, nint.Zero, instance, nint.Zero);
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        NativeMethods.WTSRegisterSessionNotification(_window, NativeMethods.NOTIFY_FOR_THIS_SESSION);
        _isRdpSession = RdpSession.IsCurrentSessionRemote();

        LoadTrayIcon();
        _notifyData = new NativeMethods.NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(), hWnd = _window, uID = 1,
            uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP,
            uCallbackMessage = TrayMessage, hIcon = _trayIcon,
            szTip = CurrentTrayTip(),
            szInfo = string.Empty, szInfoTitle = string.Empty
        };
        AddTrayIcon();
        _schedulerTimer.Start();
    }

    private void LoadTrayIcon()
    {
        var primary = CurrentDefault;
        bool disconnect = primary == PowerActionKind.Disconnect;
        string action = disconnect ? "rdp" : (primary ?? PowerActionKind.Shutdown).ToString().ToLowerInvariant();
        string scheduled = _scheduledAction is null ? string.Empty : "_scheduled";
        string tone = NativeTheme.IsTaskbarDark() ? "white" : "black";
        string key = $"tray_{action}{scheduled}_{tone}.ico";
        if (disconnect && !File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", key))) key = $"tray_rdp_{tone}.ico";
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
        _notifyData.szTip = CurrentTrayTip();
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref _notifyData);
    }

    public void RefreshAfterSettingsChanged()
    {
        if (_scheduledAction is not null &&
            (!_settings.Current.ShowScheduledActions ||
             !ActionPolicy.Enabled(_settings.Current, _scheduledRemote).HasFlag(_scheduledAction.Value.ToFlag()) ||
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
                    _dispatcher.TryEnqueue(PerformDefaultAction);
                return nint.Zero;
            }
            if (msg == 0x001A || msg == 0x031A) // Settings/theme change, including taskbar icon contrast.
                _dispatcher.TryEnqueue(UpdateTray);
            if (msg == NativeMethods.WM_POWERBROADCAST && (uint)wParam == NativeMethods.PBT_APMRESUMEAUTOMATIC)
            {
                _dispatcher.TryEnqueue(HandleResume);
                return nint.Zero;
            }
            if (msg == NativeMethods.WM_WTSSESSION_CHANGE)
            {
                _dispatcher.TryEnqueue(RefreshSessionState);
                return nint.Zero;
            }
            if (msg == NativeMethods.WM_DESTROY) return nint.Zero;
        }
        catch (Exception ex) { SettingsStore.Log(ex); }
        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowMenu()
    {
        RefreshSessionState();
        NativeTheme.Apply(_settings.Current.Theme, _window);
        nint menu = NativeMethods.CreatePopupMenu();
        try
        {
            var actions = CurrentActions;
            for (int index = 0; index < actions.Count; index++)
            {
                var action = actions[index];
                if (index == 1) NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, 0, null);
                NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, ActionBase + (uint)action, Strings.ActionName(action));
            }
            if (actions.Count > 0) NativeMethods.SetMenuDefaultItem(menu, ActionBase + (uint)actions[0], 0);

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
                foreach (var action in actions)
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
                NativeMethods.AppendMenu(menu, NativeMethods.MF_POPUP | (actions.Count == 0 ? NativeMethods.MF_GRAYED : 0),
                    (nuint)scheduledMenu, Strings.ScheduledAction);
            }

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
        if (command >= ActionBase && command < ActionBase + 6)
        {
            _ = RunSafelyAsync(() => PerformPowerActionAsync((PowerActionKind)(command - ActionBase)));
            return;
        }
        if (command >= ScheduleBase && command < ScheduleBase + 60)
        {
            uint value = command - ScheduleBase;
            _ = RunSafelyAsync(() => ScheduleCommandAsync((PowerActionKind)(value / 10), (int)(value % 10)));
            return;
        }
        switch (command)
        {
            case IdCancelScheduled: CancelSchedule(true); break;
            case IdSettings: ShowSettings(); break;
            case IdExit: _ = RunSafelyAsync(ExitAsync); break;
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
        RefreshSessionState();
        if (_actionInProgress || _warningOpen || !CurrentActions.Contains(action)) return;
        _actionInProgress = true;
        bool remote = _isRdpSession;
        try
        {
            var current = _settings.Current;
            bool confirmed = current.ConfirmationMode switch
            {
                ConfirmationMode.None => true,
                ConfirmationMode.Ask => await ConfirmWindow.ShowAsync(action, null),
                _ => await ConfirmWindow.ShowAsync(action, current.CountdownSeconds)
            };
            RefreshSessionState();
            if (confirmed && remote == _isRdpSession && CurrentActions.Contains(action)) SystemActions.Execute(action);
        }
        finally { _actionInProgress = false; }
    }

    private void PerformDefaultAction()
    {
        RefreshSessionState();
        if (CurrentDefault is PowerActionKind action)
            _ = RunSafelyAsync(() => PerformPowerActionAsync(action));
    }

    private void RefreshSessionState()
    {
        bool remote = RdpSession.IsCurrentSessionRemote();
        if (_isRdpSession == remote) return;
        _isRdpSession = remote;
        if (!remote && _scheduledAction == PowerActionKind.Disconnect) CancelSchedule(true);
        UpdateTray();
    }

    private async Task ScheduleCommandAsync(PowerActionKind action, int option)
    {
        RefreshSessionState();
        if (!_settings.Current.ShowScheduledActions || !CurrentActions.Contains(action)) return;
        bool remote = _isRdpSession;
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
        RefreshSessionState();
        if (remote != _isRdpSession || !_settings.Current.ShowScheduledActions || !CurrentActions.Contains(action)) return;
        _scheduledWarningCancellation?.Cancel();
        _scheduledAction = action;
        _scheduledRemote = remote;
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
        if (!_warningOpen && !_actionInProgress && now >= _scheduledFor.Value.AddSeconds(-30))
            _ = RunSafelyAsync(RunScheduledWarningAsync);
    }

    private async Task RunScheduledWarningAsync()
    {
        if (_scheduledAction is null || _scheduledFor is null) return;
        _warningOpen = true;
        int generation = _scheduleGeneration;
        PowerActionKind action = _scheduledAction.Value;
        int seconds = Math.Clamp((int)Math.Ceiling((_scheduledFor.Value - DateTime.Now).TotalSeconds), 1, 30);
        using var cancellation = new CancellationTokenSource();
        _scheduledWarningCancellation = cancellation;
        bool execute;
        try { execute = await ConfirmWindow.ShowAsync(action, seconds, cancellation.Token); }
        catch
        {
            if (generation == _scheduleGeneration) ClearSchedule();
            throw;
        }
        finally
        {
            if (ReferenceEquals(_scheduledWarningCancellation, cancellation)) _scheduledWarningCancellation = null;
        }
        if (generation != _scheduleGeneration) return;
        if (!execute) { CancelSchedule(true); return; }
        ClearSchedule();
        if (action != PowerActionKind.Disconnect || RdpSession.IsCurrentSessionRemote()) SystemActions.Execute(action);
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
        _scheduledWarningCancellation?.Cancel();
        _scheduledWarningCancellation = null;
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
        _notifyData.szTip = CurrentTrayTip();
        _notifyData.uFlags = NativeMethods.NIF_ICON | NativeMethods.NIF_TIP;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref _notifyData);
    }

    private void ShowNotification(string text)
    {
        _notifyData.uFlags = NativeMethods.NIF_INFO;
        _notifyData.szInfoTitle = AppBranding.Name;
        _notifyData.szInfo = text;
        _notifyData.dwInfoFlags = NativeMethods.NIIF_INFO;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref _notifyData);
    }

    private string CurrentTrayTip() => Strings.TrayTip(
        CurrentDefault is PowerActionKind action ? Strings.ActionName(action) : AppBranding.Name,
        _scheduledAction,
        _scheduledFor);

    private async Task RunSafelyAsync(Func<Task> operation)
    {
        try { await operation(); }
        catch (Exception ex)
        {
            SettingsStore.Log(ex);
            ShowNotification(Strings.Ru ? "Не удалось выполнить действие. Подробности записаны в журнал." :
                "The action could not be completed. Details were written to the log.");
        }
    }

    public void ShowSettings()
    {
        if (_settingsWindow is not null) { _settingsWindow.Activate(); return; }
        _settingsWindow = new SettingsWindow(_settings);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Activate();
    }

    public void Dispose()
    {
        _schedulerTimer.Stop();
        _scheduledWarningCancellation?.Cancel();
        if (_window != nint.Zero)
        {
            NativeMethods.WTSUnRegisterSessionNotification(_window);
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref _notifyData);
            NativeMethods.DestroyWindow(_window);
            _window = nint.Zero;
        }
        DestroyTrayIcon();
    }
}

