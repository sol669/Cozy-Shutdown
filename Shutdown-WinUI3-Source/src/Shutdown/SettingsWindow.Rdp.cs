using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace ShutdownApp;

public sealed partial class SettingsWindow
{
    private CancellationTokenSource? _rdpOperation;
    private CancellationTokenSource? _rdpMonitor;
    private Button? _rdpNavigation;
    private TextBlock? _rdpFeedback;
    private readonly DispatcherTimer _rdpResultTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private Ellipse? _rdpIndicator;
    private Action? _refreshRdpButtons;
    private RdpProfile? _checkedRdpProfile;
    private RdpAvailability _rdpAvailability;
    private RdpAvailability? CurrentRdpAvailability =>
        _checkedRdpProfile?.Address == _draft.Rdp.Address && _checkedRdpProfile?.ServerAddress == _draft.Rdp.ServerAddress
            ? _rdpAvailability : null;
    private readonly SolidColorBrush _rdpGray = new(Windows.UI.Color.FromArgb(255, 142, 142, 142));
    private readonly SolidColorBrush _rdpGreen = new(Windows.UI.Color.FromArgb(255, 30, 153, 74));
    private readonly SolidColorBrush _rdpRed = new(Windows.UI.Color.FromArgb(255, 218, 65, 67));

    private void AddRdpIndicator(Button button, string title)
    {
        _rdpNavigation = button;
        _rdpIndicator = new Ellipse { Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center };
        var content = new Grid { ColumnSpacing = 12 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(_rdpIndicator, 1);
        content.Children.Add(_rdpIndicator);
        button.Content = content;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(button, title);
        RefreshRdpIndicator();
    }

    private void RefreshRdpIndicator()
    {
        if (_rdpNavigation is null || _rdpIndicator is null) return;
        bool configured = _draft.Rdp.CanConnect;
        var availability = CurrentRdpAvailability;
        _rdpIndicator.Fill = !configured || availability is null ? _rdpGray : availability.Value.Online ? _rdpGreen : _rdpRed;
        RdpProfile profile = _draft.Rdp;
        string name = string.IsNullOrWhiteSpace(profile.PcName) ? profile.Address?.Trim() ?? "" : profile.PcName.Trim();
        string status = !configured ? L("RDP не настроен", "RDP is not configured") :
            availability is null ? L($"Проверяем {name}…", $"Checking {name}…") :
            availability.Value.Online ? L($"{name} в сети", $"{name} is online") : L($"{name} не отвечает", $"{name} is not responding");
        AutomationProperties.SetHelpText(_rdpNavigation, status);
        if (_rdpFeedback is not null && _rdpOperation is null && !_rdpResultTimer.IsEnabled)
            _rdpFeedback.Text = status;
    }

    private async Task MonitorRdpAsync()
    {
        using var monitor = new CancellationTokenSource();
        _rdpMonitor = monitor;
        try
        {
            while (!monitor.IsCancellationRequested)
            {
                RdpProfile profile = _draft.Rdp;
                var availability = await RdpClient.GetAvailabilityAsync(profile, monitor.Token);
                if (monitor.IsCancellationRequested) break;
                if (profile == _draft.Rdp)
                {
                    _checkedRdpProfile = profile;
                    _rdpAvailability = availability;
                    RefreshRdpIndicator();
                    _refreshRdpButtons?.Invoke();
                }
                await Task.Delay(TimeSpan.FromSeconds(5), monitor.Token);
            }
        }
        catch (OperationCanceledException) when (monitor.IsCancellationRequested) {}
        finally { _rdpMonitor = null; }
    }

    private void BuildRdp(StackPanel panel)
    {
        var connect = Button("", accent: true);
        var shutdown = Button(L("Завершение работы", "Shut down"));
        var restart = Button(L("Перезапуск", "Restart"));
        RdpPrimaryAction primaryAction = RdpPrimaryAction.Connect;

        void RefreshButtons()
        {
            bool idle = _rdpOperation is null;
            primaryAction = RdpClient.PrimaryAction(_draft.Rdp, CurrentRdpAvailability);
            connect.Content = Strings.RdpActionName(primaryAction);
            connect.IsEnabled = idle && _draft.Rdp.CanConnect;
            shutdown.IsEnabled = restart.IsEnabled = idle && CurrentRdpAvailability is { MpsReady: true };
        }

        void Change(RdpProfile profile)
        {
            _rdpOperation?.Cancel();
            _rdpResultTimer.Stop();
            _draft.Rdp = profile;
            RefreshRdpIndicator();
            RefreshButtons();
            UpdateState();
        }

        Border Input(string label, string text, string placeholder, int maxLength, Action<string> changed)
        {
            var box = new TextBox { Text = text, PlaceholderText = placeholder, MaxLength = maxLength,
                Width = ValueWidth, Height = 34, Style = ResourceStyle("AppDeviceAliasTextBoxStyle") };
            box.TextChanged += (_, _) => { if (!_loading) changed(box.Text); };
            return Row(label, box);
        }

        async Task RunAsync(Func<RdpProfile, CancellationToken, Action<string>, Task<string>> run)
        {
            if (_rdpOperation is not null || _rdpFeedback is not TextBlock feedback) return;
            _rdpResultTimer.Stop();
            using var operation = new CancellationTokenSource();
            _rdpOperation = operation;
            void Report(string text) => feedback.Text = text;
            Report(L("Выполняем действие…", "Working…"));
            RefreshButtons();
            try { Report(await run(_draft.Rdp, operation.Token, Report)); }
            catch (OperationCanceledException) when (operation.IsCancellationRequested)
            { Report(L("Ожидание отменено.", "Waiting canceled.")); }
            catch (Exception ex)
            {
                SettingsStore.Log(ex);
                Report(L("Не удалось подтвердить выполнение. Проверьте состояние ПК.",
                    "Could not confirm the action. Check the PC status."));
            }
            finally
            {
                if (ReferenceEquals(_rdpOperation, operation))
                {
                    _rdpOperation = null;
                    if (feedback.Text.Length > 0) _rdpResultTimer.Start();
                    RefreshRdpIndicator();
                    RefreshButtons();
                }
            }
        }

        async Task<string> PowerAsync(RdpProfile profile, PowerActionKind action, CancellationToken token)
        {
            if (RootGrid.XamlRoot is null || !profile.TryGetServer(out var server)) return "";
            string name = string.IsNullOrWhiteSpace(profile.PcName) ? server.Host : profile.PcName;
            bool reboot = action == PowerActionKind.Restart;
            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                RequestedTheme = RootGrid.RequestedTheme,
                Title = reboot ? L($"Перезапустить {name}?", $"Restart {name}?") :
                    L($"Завершить работу {name}?", $"Shut down {name}?"),
                Content = server.Authority,
                PrimaryButtonText = reboot ? L("Перезапустить", "Restart") : L("Завершить работу", "Shut down"),
                CloseButtonText = L("Отмена", "Cancel"),
                DefaultButton = ContentDialogButton.Close
            };
            _dialogOpen = true;
            try { if (await dialog.ShowAsync() != ContentDialogResult.Primary) return ""; }
            finally { _dialogOpen = false; }
            if (!await MpsClient.IsAvailableAsync(profile, token)) return Strings.MpsNotAvailable;
            token.ThrowIfCancellationRequested();
            await MpsClient.SendPowerAsync(profile, action);
            return L("Сервер принял команду.", "The server accepted the command.");
        }

        panel.Children.Add(Header("RDP"));
        panel.Children.Add(Input(L("Имя ПК", "PC name"), _draft.Rdp.PcName, "PC", 80,
            value => Change(_draft.Rdp with { PcName = value })));
        panel.Children.Add(Input(L("Адрес ПК / порт", "PC address / port"), _draft.Rdp.Address, "pc.home:3389", 256,
            value => Change(_draft.Rdp with { Address = value })));
        panel.Children.Add(ToggleRow(L("Подключаться после пробуждения", "Connect after waking"), _draft.Rdp.ConnectAfterWake,
            true, value => Change(_draft.Rdp with { ConnectAfterWake = value })));

        panel.Children.Add(Header("WOL"));
        panel.Children.Add(Input(L("MAC-адрес ПК", "PC MAC address"), _draft.Rdp.MacAddress, "00:11:22:33:44:55", 17,
            value => Change(_draft.Rdp with { MacAddress = value })));
        panel.Children.Add(Input(L("Адрес WoL", "WoL address"), _draft.Rdp.WakeAddress, "255.255.255.255", 256,
            value => Change(_draft.Rdp with { WakeAddress = value })));
        panel.Children.Add(Input(L("Порт WoL", "WoL port"), _draft.Rdp.WakePort == 0 ? "" :
            _draft.Rdp.WakePort.ToString(CultureInfo.InvariantCulture), "9", 5,
            value => Change(_draft.Rdp with { WakePort = int.TryParse(value, NumberStyles.None,
                CultureInfo.InvariantCulture, out int port) ? port : 0 })));
        panel.Children.Add(Input(L("Адрес сервера MPS", "MPS server address"), _draft.Rdp.ServerAddress,
            "http://pc.home:5154", 256, value => Change(_draft.Rdp with { ServerAddress = value })));

        panel.Children.Add(Header(L("Управление", "Controls")));
        var buttons = new Grid { ColumnSpacing = 9, UseLayoutRounding = true };
        foreach (var button in new[] { connect, shutdown, restart })
        {
            Grid.SetColumn(button, buttons.ColumnDefinitions.Count);
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttons.Children.Add(button);
        }
        panel.Children.Add(buttons);

        connect.Click += async (_, _) =>
        {
            RdpPrimaryAction intended = primaryAction;
            await RunAsync(async (profile, token, report) =>
                Strings.RdpResult(await RdpClient.StartAndConnectAsync(profile,
                    waking => report(Strings.RdpWaiting(waking)), token, intended: intended)));
        };
        shutdown.Click += async (_, _) => await RunAsync((profile, token, _) => PowerAsync(profile, PowerActionKind.Shutdown, token));
        restart.Click += async (_, _) => await RunAsync((profile, token, _) => PowerAsync(profile, PowerActionKind.Restart, token));
        RefreshButtons();
        _refreshRdpButtons = RefreshButtons;
    }

    private Button Button(string label, bool accent = false) => new()
    {
        Content = label, Height = 44, HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Center, CornerRadius = new CornerRadius(8),
        Style = ResourceStyle(accent ? "AppAccentButtonStyle" : "AppDefaultButtonStyle")
    };
}
