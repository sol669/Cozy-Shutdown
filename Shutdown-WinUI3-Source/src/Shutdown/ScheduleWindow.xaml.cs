using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace ShutdownApp;

public sealed partial class ScheduleWindow : Window
{
    private readonly TaskCompletionSource<DateTime?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly bool _exactTime;
    private bool _completed;

    private ScheduleWindow(PowerActionKind action, bool exactTime)
    {
        _exactTime = exactTime;
        InitializeComponent();
        bool ru = Strings.Ru;
        QuestionText.Text = ru
            ? $"Когда выполнить «{Strings.ActionName(action)}»?"
            : $"When should “{Strings.ActionName(action)}” be performed?";
        MinutesItem.Content = ru ? "минут" : "minutes";
        HoursItem.Content = ru ? "часов" : "hours";
        DateLabel.Text = ru ? "Дата" : "Date";
        TimeLabel.Text = ru ? "Время" : "Time";
        CancelButton.Content = Strings.Cancel;
        ScheduleButton.Content = Strings.Schedule;
        IntervalPanel.Visibility = exactTime ? Visibility.Collapsed : Visibility.Visible;
        DateTimePanel.Visibility = exactTime ? Visibility.Visible : Visibility.Collapsed;
        DateTime initial = DateTime.Now.AddHours(1);
        ActionDate.Date = new DateTimeOffset(initial.Date);
        ActionDate.MinDate = new DateTimeOffset(DateTime.Today);
        ActionTime.Time = initial.TimeOfDay;
        RootGrid.RequestedTheme = App.Settings.Current.Theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        try { SystemBackdrop = new MicaBackdrop(); } catch { }
        ConfigureWindow();
        Closed += (_, _) => Complete(null, false);
    }

    private void ConfigureWindow()
    {
        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        NativeTheme.ApplyWindowTitleBar(App.Settings.Current.Theme, hwnd);
        WindowId id = Win32Interop.GetWindowIdFromWindow(hwnd);
        AppWindow appWindow = AppWindow.GetFromWindowId(id);
        AppBranding.ApplyWindowIcons(appWindow);
        appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
        double scale = Math.Max(1, GetDpiForWindow(hwnd) / 96.0);
        int width = (int)Math.Round((_exactTime ? 620 : 560) * scale);
        int height = (int)Math.Round((_exactTime ? 300 : 240) * scale);
        NativeMethods.GetCursorPos(out var cursor);
        DisplayArea area = DisplayArea.GetFromPoint(new Windows.Graphics.PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Primary);
        var work = area.WorkArea;
        appWindow.MoveAndResize(new Windows.Graphics.RectInt32(work.X + Math.Max(0, (work.Width - width) / 2), work.Y + Math.Max(0, (work.Height - height) / 2), width, height));
    }

    public static async Task<DateTime?> ShowAsync(PowerActionKind action, bool exactTime)
    {
        var window = new ScheduleWindow(action, exactTime);
        window.Activate();
        return await window._result.Task;
    }

    private void ScheduleButton_Click(object sender, RoutedEventArgs e)
    {
        DateTime when;
        if (_exactTime)
        {
            if (ActionDate.Date is not DateTimeOffset selectedDate) return;
            DateTime date = selectedDate.LocalDateTime.Date;
            when = date + ActionTime.Time;
            if (when <= DateTime.Now) return;
        }
        else
        {
            double value = double.IsNaN(IntervalNumber.Value) ? 30 : IntervalNumber.Value;
            value = Math.Clamp(value, 1, UnitCombo.SelectedIndex == 1 ? 168 : 10080);
            when = UnitCombo.SelectedIndex == 1 ? DateTime.Now.AddHours(value) : DateTime.Now.AddMinutes(value);
        }
        Complete(when, true);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Complete(null, true);
    private void Complete(DateTime? value, bool close)
    {
        if (_completed) return;
        _completed = true;
        _result.TrySetResult(value);
        if (close) Close();
    }
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
}
