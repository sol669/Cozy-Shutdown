using ShutdownApp;

int checks = 0;
void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
var migrated = SettingsCodec.Read("""{"DefaultAction":1,"EnabledActions":3,"UseRdpAsDefaultAction":false,"CountdownSeconds":7,"Theme":2,"Language":0,"StartWithWindows":false} """);
Check(migrated.DefaultAction == PowerActionKind.Restart && migrated.RemoteDefaultAction == PowerActionKind.Restart, "Keep legacy remote power default");
Check(migrated.EnabledActions == (EnabledPowerActions)35, "Migrate legacy shared list and enable disconnect");
Check(!migrated.StartWithWindows && migrated.CountdownSeconds == 7 && migrated.Theme == AppTheme.Dark, "Preserve existing preferences");
Check(SettingsCodec.Read("""{"UseRdpAsDefaultAction":true} """).RemoteDefaultAction == PowerActionKind.Disconnect, "Legacy disconnect default");
var settings = new AppSettings { EnabledActions = EnabledPowerActions.Disconnect | EnabledPowerActions.Shutdown };
Check(ActionPolicy.Menu(settings, true, _ => true).SequenceEqual(new[] { PowerActionKind.Disconnect, PowerActionKind.Shutdown }), "Remote contains only chosen actions");
Check(ActionPolicy.Menu(settings, false, _ => true).SequenceEqual(new[] { PowerActionKind.Shutdown }), "Local excludes disconnect");
settings.RemoteDefaultAction = PowerActionKind.Shutdown;
Check(ActionPolicy.Menu(settings, true, _ => true).SequenceEqual(new[] { PowerActionKind.Shutdown, PowerActionKind.Disconnect }), "Remote primary sorts first once");
settings.EnabledActions = EnabledPowerActions.Shutdown;
Check(!ActionPolicy.Menu(settings, true, _ => true).Contains(PowerActionKind.Disconnect), "Disconnect can be disabled; never appended separately");
var roundtrip = SettingsCodec.Read(SettingsCodec.Write(settings));
Check(roundtrip.EnabledActions == EnabledPowerActions.Shutdown, "Unified set survives save/reload");
var v120 = SettingsCodec.Read("""{"EnabledActions":3,"RemoteEnabledActions":33,"RemoteDefaultAction":5} """);
Check(v120.EnabledActions == (EnabledPowerActions)35 && v120.RemoteDefaultAction == PowerActionKind.Disconnect, "Merge v1.2.0 lists without losing actions");
var corrupt = SettingsCodec.Read("""{"DefaultAction":99,"RemoteDefaultAction":99,"EnabledActions":2048,"RemoteEnabledActions":0,"CountdownSeconds":999,"Theme":42,"Language":42} """);
Check(corrupt.EnabledActions == EnabledPowerActions.Shutdown, "Normalize invalid configuration");
Check(corrupt.CountdownSeconds == 300 && corrupt.Theme == AppTheme.System, "Bound invalid values");

// Exhaustively exercise the unified enabled list, hardware availability, defaults and contexts.
foreach (bool remote in new[] { false, true })
for (int flags = 0; flags < 64; flags++)
for (int availability = 0; availability < 64; availability++)
foreach (var preferred in Enum.GetValues<PowerActionKind>())
{
    settings = new AppSettings { EnabledActions = (EnabledPowerActions)flags,
        DefaultAction = preferred, RemoteDefaultAction = preferred };
    bool Available(PowerActionKind action) => ((int)action.ToFlag() & availability) != 0;
    var menu = ActionPolicy.Menu(settings, remote, Available);
    var expected = ActionPolicy.Actions(remote).Where(a => ((int)a.ToFlag() & flags) != 0 && Available(a)).ToArray();
    Check(menu.Count == expected.Length && menu.Distinct().Count() == menu.Count && expected.All(menu.Contains), "Membership/uniqueness/availability");
    Check(!expected.Contains(preferred) || menu[0] == preferred, "Primary first");
    Check(remote || !menu.Contains(PowerActionKind.Disconnect), "Never disconnect locally");
    Check(menu.All(a => ((int)a.ToFlag() & flags) != 0), "Every entry comes from the shared list");
}
Console.WriteLine($"PASS: {checks} checks; no OS actions invoked.");
