using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.System;

namespace ShutdownApp;

public sealed partial class SettingsWindow : Window
{
    private const double ValueWidth = 244;
    private const double NavigationWidth = 280;
    private const double SettingsInset = 24;
    private const double ContentWidth = 540;
    private readonly SettingsStore _store;
    private AppSettings _draft;
    private string _page = "general";
    private bool _loading, _allowClose, _dialogOpen;
    private Button? _save;
    private TextBlock? _validation;
    private bool _validCountdown = true, _validClockColor = true;
    private bool Ru => _store.Current.Language == AppLanguage.Russian;
    private string L(string ru, string en) => Ru ? ru : en;
    private bool Dirty => !_validCountdown || !_validClockColor || SettingsCodec.Write(_draft) != SettingsCodec.Write(_store.Current);
    private bool Valid => _validCountdown && _validClockColor && _draft.Rdp.CanSave && ActionPolicy.Menu(_draft, false, SystemActions.IsAvailable).Count > 0 &&
        ActionPolicy.Menu(_draft, true, SystemActions.IsAvailable).Count > 0;

    public SettingsWindow(SettingsStore store, string initialPage = "general")
    {
        _store = store;
        _draft = store.Current.Clone();
        _page = initialPage;
        InitializeComponent();
        _rdpResultTimer.Tick += (_, _) =>
        {
            _rdpResultTimer.Stop();
            RefreshRdpIndicator();
        };
        Closed += (_, _) =>
        {
            _rdpOperation?.Cancel();
            _rdpOperation = null;
            _rdpMonitor?.Cancel();
            _rdpResultTimer.Stop();
        };
        try { SystemBackdrop = new MicaBackdrop(); } catch { }
        ConfigureWindow();
        AppWindow.Closing += (sender, e) =>
        {
            if (_allowClose || !Dirty) return;
            e.Cancel = true;
            _ = CloseAsync();
        };
        RootGrid.KeyDown += async (_, e) =>
        {
            if (e.Key == VirtualKey.Escape && !_dialogOpen) { e.Handled = true; await CloseAsync(); }
        };
        BuildShell();
        _ = MonitorRdpAsync();
    }

    private void ConfigureWindow()
    {
        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        Title = L("Настройки Cozy Shutdown", "Cozy Shutdown Settings") + (App.LocalTest ? " · " + AppBranding.Version + " Test" : "");
        AppBranding.ApplyWindowIcons(AppWindow);
        NativeTheme.ApplyWindowTitleBar(_store.Current.Theme, hwnd);
        double scale = Math.Max(1, GetDpiForWindow(hwnd) / 96.0);
        NativeMethods.GetCursorPos(out var cursor);
        var work = DisplayArea.GetFromPoint(new Windows.Graphics.PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Primary).WorkArea;
        int width = Math.Min((int)Math.Round(900 * scale), Math.Max(780, work.Width - 48));
        int height = Math.Min((int)Math.Round(820 * scale), Math.Max(640, work.Height - 48));
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(work.X + Math.Max(0, (work.Width - width) / 2),
            work.Y + Math.Max(0, (work.Height - height) / 2), width, height));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
    }

    private void BuildShell()
    {
        _rdpOperation?.Cancel();
        _rdpOperation = null;
        _rdpResultTimer.Stop();
        _rdpFeedback = null;
        _refreshRdpButtons = null;
        _loading = true;
        RootGrid.Children.Clear();
        RootGrid.RowDefinitions.Clear();
        RootGrid.ColumnDefinitions.Clear();
        RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(NavigationWidth) });
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = ContentWidth + SettingsInset });
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SettingsInset) });
        var nav = new StackPanel { Margin = new Thickness(20, 28, 16, 12), Spacing = 5 };
        nav.Children.Add(Navigation(L("Основные", "General"), "general"));
        nav.Children.Add(Navigation(L("Действия в трее", "Tray actions"), "actions"));
        nav.Children.Add(Navigation(L("Часы и календарь", "clock"), "clock"));
        nav.Children.Add(Navigation(L("RDP-модуль", "RDP module"), "rdp"));
        RootGrid.Children.Add(nav);
        var panel = new StackPanel { Margin = new Thickness(0, 28, 0, 0), Spacing = 5 };
        switch (_page)
        {
            case "actions": BuildActions(panel); break;
            case "clock": BuildClock(panel); break;
            case "rdp": BuildRdp(panel); break;
            default: BuildGeneral(panel); break;
        }
        _validation = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = .68, Margin = new Thickness(2, 5, 0, 0) };
        panel.Children.Add(_validation);
        var scroll = new ScrollViewer { Margin = new Thickness(SettingsInset, 0, 0, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        Grid.SetColumn(scroll, 1);
        RootGrid.Children.Add(scroll);

        var info = new StackPanel { Opacity = .68, Margin = new Thickness(28, 10, 0, 18) };
        info.Children.Add(new TextBlock { Text = "Cozy Shutdown " + AppBranding.Version +
            (App.LocalTest ? " · Test" : App.Preview ? " · Preview" : "") });
        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        links.Children.Add(new TextBlock { Text = "sol669 ·", VerticalAlignment = VerticalAlignment.Center });
        links.Children.Add(new HyperlinkButton { Content = "GitHub", NavigateUri = new Uri("https://github.com/sol669/Cozy-Shutdown"), Padding = new Thickness(0) });
        info.Children.Add(links);
        Grid.SetRow(info, 1);
        RootGrid.Children.Add(info);
        if (_page == "rdp")
        {
            var rdpLayout = new Grid();
            rdpLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rdpLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _rdpFeedback = new TextBlock
            {
                Opacity = .8, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 6, 8, 6)
            };
            scroll.Content = rdpLayout;
            rdpLayout.Children.Add(panel);
            Grid.SetRow(_rdpFeedback, 1);
            rdpLayout.Children.Add(_rdpFeedback);
            scroll.SizeChanged += (_, e) => rdpLayout.MinHeight = e.NewSize.Height;
            RefreshRdpIndicator();
        }
        else scroll.Content = panel;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(SettingsInset, 10, 0, 18) };
        var close = new Button { Content = L("Закрыть", "Close"), MinWidth = 110, Style = ResourceStyle("AppDefaultButtonStyle") };
        close.Click += async (_, _) => await CloseAsync();
        _save = new Button { Content = L("Сохранить", "Save"), MinWidth = 110, Style = ResourceStyle("AppAccentButtonStyle") };
        _save.Click += async (_, _) => await SaveAsync();
        buttons.Children.Add(close);
        buttons.Children.Add(_save);
        Grid.SetColumn(buttons, 1);
        Grid.SetRow(buttons, 1);
        RootGrid.Children.Add(buttons);
        ApplyTheme();
        _loading = false;
        UpdateState();
    }

    private Button Navigation(string title, string page)
    {
        var button = new Button { Content = title, Height = 46, Padding = new Thickness(14, 0, 14, 0),
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            Style = ResourceStyle(page == _page ? "AppSelectedNavigationButtonStyle" : "AppNavigationButtonStyle") };
        if (page == "rdp") AddRdpIndicator(button, title);
        button.Click += async (_, _) =>
        {
            if (page == _page || _dialogOpen) return;
            if (!await CanLeaveAsync()) return;
            _page = page;
            BuildShell();
        };
        return button;
    }

    private void BuildGeneral(StackPanel panel)
    {
        panel.Children.Add(Header(L("Действие по умолчанию", "Default action")));
        panel.Children.Add(DefaultRow(false));
        panel.Children.Add(DefaultRow(true));
        panel.Children.Add(Header(L("Система", "System")));
        panel.Children.Add(ToggleRow(L("Автозапуск", "Autostart"), _draft.StartWithWindows, !App.LocalTest, value => _draft.StartWithWindows = value));
        panel.Children.Add(Row(L("Тема", "Theme"), Choice(new[] { L("Как в Windows", "Use Windows setting"), L("Светлая", "Light"), L("Темная", "Dark") },
            (int)_draft.Theme, i => _draft.Theme = (AppTheme)i)));
        panel.Children.Add(Row(L("Язык", "Language"), Choice(new[] { "Русский", "English" }, (int)_draft.Language, i => _draft.Language = (AppLanguage)i)));
        panel.Children.Add(Header(L("Поведение", "Behavior")));

        var countdown = new TextBox { Text = _draft.CountdownSeconds.ToString(), Width = ValueWidth, Height = 34,
            Style = ResourceStyle("AppDeviceAliasTextBoxStyle") };
        var countdownRow = Row(L("Обратный отсчет, сек.", "Countdown, sec."), countdown);
        countdownRow.Visibility = _draft.ConfirmationMode == ConfirmationMode.Countdown ? Visibility.Visible : Visibility.Collapsed;
        countdown.TextChanged += (_, _) =>
        {
            if (_loading) return;
            _validCountdown = int.TryParse(countdown.Text, out int seconds) && seconds >= 1 && seconds <= 300;
            if (_validCountdown) _draft.CountdownSeconds = seconds;
            UpdateState();
        };
        panel.Children.Add(Row(L("Подтверждение", "Confirmation"), Choice(new[] {
            L("Без подтверждения", "No confirmation"), L("Спрашивать Да / Нет", "Ask Yes / No"), L("С обратным отсчетом", "With countdown") },
            (int)_draft.ConfirmationMode, i =>
            {
                _draft.ConfirmationMode = (ConfirmationMode)i;
                countdownRow.Visibility = i == (int)ConfirmationMode.Countdown ? Visibility.Visible : Visibility.Collapsed;
                if (i != (int)ConfirmationMode.Countdown) { _validCountdown = true; countdown.Text = _draft.CountdownSeconds.ToString(); }
            })));
        panel.Children.Add(countdownRow);
        panel.Children.Add(ToggleRow(L("Отложенные действия в трее", "Scheduled actions in tray"), _draft.ShowScheduledActions, true,
            value => _draft.ShowScheduledActions = value));
    }

    private Border DefaultRow(bool remote)
    {
        var actions = ActionPolicy.Menu(_draft, remote, SystemActions.IsAvailable);
        var combo = new ComboBox { Width = ValueWidth, Style = ResourceStyle("AppSettingsComboBoxStyle"), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var action in ActionPolicy.Actions(remote).Where(actions.Contains))
            combo.Items.Add(new ComboBoxItem { Content = Strings.ActionName(action), Tag = action });
        combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (PowerActionKind)i.Tag == ActionPolicy.Preferred(_draft, remote));
        if (combo.SelectedIndex < 0 && combo.Items.Count > 0)
        {
            combo.SelectedIndex = 0;
            if (combo.SelectedItem is ComboBoxItem first) SetDefault(remote, (PowerActionKind)first.Tag);
        }
        combo.IsEnabled = combo.Items.Count > 0;
        combo.PlaceholderText = L("Нет доступных действий", "No available actions");
        combo.SelectionChanged += (_, _) =>
        {
            if (_loading || combo.SelectedItem is not ComboBoxItem item) return;
            SetDefault(remote, (PowerActionKind)item.Tag);
            UpdateState();
        };
        return Row(remote ? L("В удаленном сеансе", "In remote session") : L("В локальном сеансе", "In local session"), combo);
    }

    private void SetDefault(bool remote, PowerActionKind action)
    {
        if (remote) _draft.RemoteDefaultAction = action; else _draft.DefaultAction = action;
    }

    private void BuildActions(StackPanel panel)
    {
        panel.Children.Add(Header(L("Активируйте нужные действия", "Enable the actions you need")));
        foreach (var action in ActionPolicy.Actions(false))
        {
            bool available = SystemActions.IsAvailable(action);
            panel.Children.Add(ToggleRow(Strings.ActionName(action), ActionPolicy.Enabled(_draft, false).HasFlag(action.ToFlag()), available, value =>
            {
                _draft.EnabledActions = value ? _draft.EnabledActions | action.ToFlag() : _draft.EnabledActions & ~action.ToFlag();
                ReconcileDefaults();
            }));
        }
        var disconnect = PowerActionKind.Disconnect;
        panel.Children.Add(ToggleRow(L("Отключиться от удаленного сеанса", "Disconnect remote session"),
            _draft.EnabledActions.HasFlag(disconnect.ToFlag()), true, value =>
            {
                _draft.EnabledActions = value ? _draft.EnabledActions | disconnect.ToFlag() : _draft.EnabledActions & ~disconnect.ToFlag();
                ReconcileDefaults();
            }));
        panel.Children.Add(ToggleRow(L("Меню RDP", "RDP menu"),
            _draft.ShowRdpInTray, true, value => _draft.ShowRdpInTray = value));
    }

    private void BuildClock(StackPanel panel)
    {
        panel.Children.Add(Header(L("Система", "System")));
        panel.Children.Add(ToggleRow(L("Часы на рабочем столе", "Desktop clock"), _draft.ShowClock, true, value => { _draft.ShowClock = value; BuildShell(); }));
        panel.Children.Add(ToggleRow(L("Показывать календарь", "Show calendar"), _draft.ShowCalendar, _draft.ShowClock,
            value => _draft.ShowCalendar = value, preserveValueWhenUnavailable: true));
        panel.Children.Add(Header(L("Положение", "Position")));
        panel.Children.Add(Row(L("Размер", "Size"), PercentageSlider(50, 200, _draft.ClockScale, value => _draft.ClockScale = value)));
        panel.Children.Add(Row(L("По горизонтали", "Horizontal"), PercentageSlider(0, 100, _draft.ClockPositionX, value => _draft.ClockPositionX = value)));
        panel.Children.Add(Row(L("По вертикали", "Vertical"), PercentageSlider(0, 100, _draft.ClockPositionY, value => _draft.ClockPositionY = value)));
        panel.Children.Add(Header(L("Вид", "Appearance")));
        panel.Children.Add(Row(L("Непрозрачность", "Opacity"), PercentageSlider(0, 100, _draft.ClockOpacity, value => _draft.ClockOpacity = value)));
        var color = new TextBox { Text = _draft.ClockTextColor.TrimStart('#'), Width = ValueWidth, Height = 34,
            PlaceholderText = "FFF", Style = ResourceStyle("AppDeviceAliasTextBoxStyle") };
        color.TextChanged += (_, _) =>
        {
            if (_loading) return;
            string text = color.Text.Trim().TrimStart('#');
            _validClockColor = (text.Length == 3 || text.Length == 6) && text.All(Uri.IsHexDigit);
            if (_validClockColor) _draft.ClockTextColor = NormalizeHex(text);
            UpdateState();
        };
        panel.Children.Add(Row(L("Цвет текста", "Text color"), color));
    }

    internal void ReloadFromStore()
    {
        _draft = _store.Current.Clone();
        _validCountdown = _validClockColor = true;
        BuildShell();
    }

    private Grid PercentageSlider(int minimum, int maximum, int value, Action<int> changed)
    {
        var row = new Grid { Width = ValueWidth, ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var slider = new Slider { Minimum = minimum, Maximum = maximum, StepFrequency = 25, Value = Math.Clamp(value, minimum, maximum) };
        var display = new TextBlock { Text = $"{(int)slider.Value}%", Width = 42, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        slider.ValueChanged += (_, e) => { int next = (int)Math.Round(e.NewValue / 25) * 25; next = Math.Clamp(next, minimum, maximum); display.Text = $"{next}%"; if (!_loading) { changed(next); UpdateState(); } };
        Grid.SetColumn(display, 1); row.Children.Add(slider); row.Children.Add(display);
        return row;
    }

    private static string NormalizeHex(string text) => "#" + (text.Length == 3 ? string.Concat(text.Select(c => new string(c, 2))) : text).ToUpperInvariant();

    private void ReconcileDefaults()
    {
        var local = ActionPolicy.Menu(_draft, false, SystemActions.IsAvailable);
        if (!local.Contains(_draft.DefaultAction) && local.Count > 0) _draft.DefaultAction = local[0];
        var remote = ActionPolicy.Menu(_draft, true, SystemActions.IsAvailable);
        if (!remote.Contains(_draft.RemoteDefaultAction) && remote.Count > 0) _draft.RemoteDefaultAction = remote[0];
    }

    private ComboBox Choice(string[] labels, int selected, Action<int> changed)
    {
        var combo = new ComboBox { Width = ValueWidth, Style = ResourceStyle("AppSettingsComboBoxStyle"), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (string label in labels) combo.Items.Add(label);
        combo.SelectedIndex = selected;
        combo.SelectionChanged += (_, _) => { if (!_loading && combo.SelectedIndex >= 0) { changed(combo.SelectedIndex); UpdateState(); } };
        return combo;
    }

    private Border ToggleRow(string label, bool enabled, bool available, Action<bool> changed, bool preserveValueWhenUnavailable = false)
    {
        var toggle = new ToggleSwitch { IsOn = enabled && (available || preserveValueWhenUnavailable), IsEnabled = available,
            OffContent = "", OnContent = "", MinWidth = 0, Width = 44, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(toggle, label);
        var state = new TextBlock { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Opacity = .68 };
        void UpdateLabel() => state.Text = !available ? L("Недоступно", "Unavailable") : toggle.IsOn ? L("Вкл.", "On") : L("Откл.", "Off");
        UpdateLabel();
        toggle.Toggled += (_, _) => { UpdateLabel(); if (!_loading) { changed(toggle.IsOn); UpdateState(); } };
        var content = new Grid { Width = ValueWidth, Height = 34, ColumnSpacing = 12 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(state, 1);
        Grid.SetColumn(toggle, 2);
        content.Children.Add(state);
        content.Children.Add(toggle);
        var card = Row(label, content);
        if (!available) card.Opacity = .55;
        return card;
    }

    private static Border Row(string label, FrameworkElement control)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        control.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(control, label);
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return new Border { Child = grid, Style = ResourceStyle("AppSettingsCardStyle") };
    }

    private static Border Header(string title) => new() { Height = 46, Child = new TextBlock
        { Text = title, Opacity = .52, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) } };
    private static Style ResourceStyle(string key) => (Style)Application.Current.Resources[key];

    private void UpdateState()
    {
        if (_save is not null) _save.IsEnabled = !_loading && Valid && Dirty;
        if (_validation is not null)
        {
            _validation.Text = !_validCountdown ? L("Введите целое число от 1 до 300 секунд.", "Enter a whole number from 1 to 300 seconds.") :
                !_validClockColor ? L("Введите код цвета: FFF или FFFFFF.", "Enter a color code: FFF or FFFFFF.") :
                !_draft.Rdp.CanSave ? L("Проверьте адрес ПК, настройки WoL и адрес сервера MPS.",
                    "Check the PC address, WoL settings, and MPS server address.") :
                !Valid ? L("Оставьте хотя бы одно доступное действие для локального и удаленного сеансов.", "Keep at least one available action for local and remote sessions.") : "";
            _validation.Visibility = Valid ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private async Task<bool> SaveAsync()
    {
        if (!Valid) return false;
        try
        {
            _store.Replace(_draft.Clone());
            _draft = _store.Current.Clone();
            DesktopClockService.Refresh();
            App.Tray?.RefreshAfterSettingsChanged();
            BuildShell();
            return true;
        }
        catch (Exception ex)
        {
            SettingsStore.Log(ex);
            if (_validation is not null) { _validation.Text = L("Не удалось сохранить настройки.", "Unable to save settings."); _validation.Visibility = Visibility.Visible; }
            await Task.CompletedTask;
            return false;
        }
    }

    private async Task<bool> CanLeaveAsync()
    {
        if (!Dirty) return true;
        if (_dialogOpen || RootGrid.XamlRoot is null) return false;
        _dialogOpen = true;
        try
        {
            var dialog = new ContentDialog { XamlRoot = RootGrid.XamlRoot, RequestedTheme = RootGrid.RequestedTheme,
                Title = L("Сохранить изменения?", "Save changes?"), Content = Valid ? L("Остались несохраненные изменения.", "There are unsaved changes.") :
                    L("Проверьте параметры. Можно вернуться к редактированию или не сохранять изменения.", "Check your settings. Continue editing or discard the changes."),
                PrimaryButtonText = Valid ? L("Сохранить", "Save") : "",
                SecondaryButtonText = L("Не сохранять", "Don't save"), CloseButtonText = L("Отмена", "Cancel"), DefaultButton = ContentDialogButton.Close };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary) return await SaveAsync();
            if (result == ContentDialogResult.Secondary) { _draft = _store.Current.Clone(); _validCountdown = _validClockColor = true; return true; }
            return false;
        }
        finally { _dialogOpen = false; }
    }

    private async Task CloseAsync()
    {
        if (_dialogOpen || !await CanLeaveAsync()) return;
        _allowClose = true;
        Close();
#if DEBUG
        if (App.Preview) App.Quit();
#endif
    }

    private void ApplyTheme()
    {
        RootGrid.RequestedTheme = _store.Current.Theme switch
        { AppTheme.Light => ElementTheme.Light, AppTheme.Dark => ElementTheme.Dark, _ => ElementTheme.Default };
        NativeTheme.ApplyWindowTitleBar(_store.Current.Theme, WinRT.Interop.WindowNative.GetWindowHandle(this));
    }

    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
}
