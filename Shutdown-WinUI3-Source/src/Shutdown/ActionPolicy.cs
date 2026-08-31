using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace ShutdownApp;

// One source of truth for configuration, menu order, double-click and tray icon.
public static class ActionPolicy
{
    public const EnabledPowerActions LocalMask = EnabledPowerActions.Shutdown | EnabledPowerActions.Restart |
        EnabledPowerActions.Sleep | EnabledPowerActions.Hibernate | EnabledPowerActions.Lock;
    public const EnabledPowerActions RemoteMask = LocalMask | EnabledPowerActions.Disconnect;

    public static PowerActionKind[] Actions(bool remote) => remote
        ? new[] { PowerActionKind.Disconnect, PowerActionKind.Shutdown, PowerActionKind.Restart,
            PowerActionKind.Sleep, PowerActionKind.Hibernate, PowerActionKind.Lock }
        : new[] { PowerActionKind.Shutdown, PowerActionKind.Restart, PowerActionKind.Sleep,
            PowerActionKind.Hibernate, PowerActionKind.Lock };

    public static EnabledPowerActions Enabled(AppSettings settings, bool remote) =>
        settings.EnabledActions & (remote ? RemoteMask : LocalMask);

    public static PowerActionKind Preferred(AppSettings settings, bool remote) =>
        remote ? settings.RemoteDefaultAction : settings.DefaultAction;

    public static List<PowerActionKind> Menu(AppSettings settings, bool remote, Func<PowerActionKind, bool> available)
    {
        var enabled = Enabled(settings, remote);
        var result = Actions(remote).Where(action => enabled.HasFlag(action.ToFlag()) && available(action)).ToList();
        var preferred = Preferred(settings, remote);
        if (result.Remove(preferred)) result.Insert(0, preferred);
        return result;
    }

    public static void Normalize(AppSettings settings)
    {
        settings.EnabledActions &= RemoteMask;
        if ((settings.EnabledActions & LocalMask) == 0) settings.EnabledActions |= EnabledPowerActions.Shutdown;
        if (!Actions(false).Contains(settings.DefaultAction) || !settings.EnabledActions.HasFlag(settings.DefaultAction.ToFlag()))
            settings.DefaultAction = Actions(false).First(a => settings.EnabledActions.HasFlag(a.ToFlag()));
        if (!Actions(true).Contains(settings.RemoteDefaultAction) || !settings.EnabledActions.HasFlag(settings.RemoteDefaultAction.ToFlag()))
            settings.RemoteDefaultAction = settings.EnabledActions.HasFlag(EnabledPowerActions.Disconnect)
                ? PowerActionKind.Disconnect
                : settings.DefaultAction;
        settings.CountdownSeconds = Math.Clamp(settings.CountdownSeconds, 1, 300);
        if (!Enum.IsDefined(settings.ConfirmationMode)) settings.ConfirmationMode = ConfirmationMode.Countdown;
        if (!Enum.IsDefined(settings.Theme)) settings.Theme = AppTheme.System;
        if (!Enum.IsDefined(settings.Language)) settings.Language = AppLanguage.English;
    }
}

public static class SettingsCodec
{
    public static AppSettings Read(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        // v1.2.0 briefly used a separate remote list. Merge it into the unified list so no chosen
        // action disappears during the migration. Earlier releases get Disconnect enabled by default.
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("RemoteEnabledActions", out var legacyRemote))
        {
            if (legacyRemote.TryGetInt32(out int flags)) settings.EnabledActions |= (EnabledPowerActions)flags;
        }
        else if (root.ValueKind == JsonValueKind.Object && !root.TryGetProperty(nameof(AppSettings.RemoteDefaultAction), out _))
        {
            settings.EnabledActions |= EnabledPowerActions.Disconnect;
        }
        if (root.ValueKind == JsonValueKind.Object && !root.TryGetProperty(nameof(AppSettings.RemoteDefaultAction), out _) &&
            root.TryGetProperty("UseRdpAsDefaultAction", out var legacy) && legacy.ValueKind == JsonValueKind.False)
            settings.RemoteDefaultAction = settings.DefaultAction;
        ActionPolicy.Normalize(settings);
        return settings;
    }

    public static string Write(AppSettings settings) => JsonSerializer.Serialize(settings,
        new JsonSerializerOptions { WriteIndented = true });
}

public static class PowerActionSettingsExtensions
{
    public static EnabledPowerActions ToFlag(this PowerActionKind action) => action switch
    {
        PowerActionKind.Shutdown => EnabledPowerActions.Shutdown,
        PowerActionKind.Restart => EnabledPowerActions.Restart,
        PowerActionKind.Sleep => EnabledPowerActions.Sleep,
        PowerActionKind.Hibernate => EnabledPowerActions.Hibernate,
        PowerActionKind.Lock => EnabledPowerActions.Lock,
        PowerActionKind.Disconnect => EnabledPowerActions.Disconnect,
        _ => EnabledPowerActions.None
    };
}
