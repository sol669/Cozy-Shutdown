using ShutdownApp;
using System.Net;
using System.Net.Sockets;

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
var clockDefaults = SettingsCodec.Read("""{"DefaultAction":0} """);
Check(!clockDefaults.ShowClock && clockDefaults.ShowCalendar && clockDefaults.ClockScale == 100 &&
    clockDefaults.ClockOpacity == 100 && clockDefaults.ClockPositionX == 50 && clockDefaults.ClockPositionY == 25 &&
    clockDefaults.ClockTextColor == "#FFFFFF", "Old settings receive safe clock defaults");
var clockNormalized = SettingsCodec.Read("""{"ClockScale":999,"ClockOpacity":-1,"ClockPositionX":-1,"ClockPositionY":999,"ClockTextColor":"0af"} """);
Check(clockNormalized.ClockScale == 200 && clockNormalized.ClockOpacity == 0 &&
    clockNormalized.ClockPositionX == 0 && clockNormalized.ClockPositionY == 100 &&
    clockNormalized.ClockTextColor == "#00AAFF", "Normalize clock values and short hex color");
var invalidClockColor = SettingsCodec.Read("""{"ClockTextColor":"not-a-color"} """);
Check(invalidClockColor.ClockTextColor == "#FFFFFF", "Reject invalid clock color");

Check(migrated.Rdp.CanSave && !migrated.Rdp.CanConnect && !migrated.ShowRdpInTray && migrated.Rdp.ConnectAfterWake,
    "Default tray menu off and connect after waking on for older profiles");
var profile = new RdpProfile
{
    PcName = "Test PC", Address = "pc.home:3390", MacAddress = "00:11:22:33:44:55",
    ServerAddress = "http://pc.home:5154"
};
Check(profile.CanSave && profile.CanConnect && profile.CanWake && profile.CanUseServer, "Accept a complete profile");
var savedRdp = SettingsCodec.Read(SettingsCodec.Write(new AppSettings { Rdp = profile, ShowRdpInTray = true }));
Check(savedRdp.Rdp == profile && savedRdp.ShowRdpInTray, "RDP profile and tray option survive save/reload");
var wakeOnly = profile with { ConnectAfterWake = false };
Check(!SettingsCodec.Read(SettingsCodec.Write(new AppSettings { Rdp = wakeOnly })).Rdp.ConnectAfterWake,
    "Keep explicit wake-only preference after reloading settings");
Check(RdpClient.PrimaryAction(profile, new RdpAvailability()) == RdpPrimaryAction.Start &&
    RdpClient.PrimaryAction(wakeOnly, new RdpAvailability()) == RdpPrimaryAction.WakeOnly &&
    RdpClient.PrimaryAction(wakeOnly, new RdpAvailability(false, true, false)) == RdpPrimaryAction.Connect,
    "Primary action follows online status and the wake preference");
Check(RdpClient.CreateDirectStartInfo(profile).ArgumentList.SequenceEqual(new[] { "/v:pc.home:3390" }),
    "Use direct mstsc and Windows session defaults");
Check(RdpClient.CreateDirectStartInfo(profile with { Address = "[2001:db8::1]:3390" })
    .ArgumentList.SequenceEqual(new[] { "/v:[2001:db8::1]:3390" }), "Quick connect supports IPv6 and the supplied port");
Check((profile with { Address = "pc.home" }).ConnectionAddress() == "pc.home:3389", "Use default RDP port");
Check(!(profile with { Address = "pc.home\r\nother" }).CanConnect &&
    !(profile with { Address = "https://pc.home" }).CanConnect, "Reject unsafe profile text");
Check(!(profile with { Address = "pc.home:65536" }).CanSave && !(profile with { MacAddress = "ff:ff:ff:ff:ff:ff" }).CanWake,
    "Reject invalid port and broadcast MAC");
Check(!(profile with { ServerAddress = "http://user:pass@pc.home:5154" }).CanSave, "Reject credentials in MPS URLs");
Check((profile with { WakeAddress = "router.home" }).CanWake, "Accept hostname as the WoL destination");
Check(SettingsCodec.Read("""{"Rdp":{"Host":"2001:db8::1","Port":3390}}""").Rdp.Address == "[2001:db8::1]:3390",
    "Migrate previous profile address");
var recovered = SettingsCodec.Read("""{"Rdp":null} """);
Check(recovered.Rdp.CanSave && !recovered.Rdp.CanConnect, "Recover a null persisted profile");

byte[] packet = profile.WakePacket();
byte[] expectedMac = Convert.FromHexString("001122334455");
Check(packet.Length == 102 && packet.Take(6).All(b => b == 255) &&
    packet.Skip(6).Chunk(6).All(mac => mac.SequenceEqual(expectedMac)), "Standard Wake-on-LAN magic packet");
using (var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
{
    int port = ((IPEndPoint)receiver.Client.LocalEndPoint!).Port;
    await RdpClient.WakeAsync(profile with { WakeAddress = "127.0.0.1", WakePort = port }, timeout.Token);
    Check((await receiver.ReceiveAsync(timeout.Token)).Buffer.SequenceEqual(packet), "Send WoL packet to the configured UDP endpoint");
    var wakeOnlyResult = await RdpClient.StartAndConnectAsync(
        wakeOnly with { WakeAddress = "127.0.0.1", WakePort = port }, _ => throw new Exception("Wake-only should not wait for RDP"),
        timeout.Token, _ => throw new Exception("Wake-only must not launch RDP"), RdpPrimaryAction.WakeOnly);
    Check(wakeOnlyResult == RdpStartResult.WakeSent &&
        (await receiver.ReceiveAsync(timeout.Token)).Buffer.SequenceEqual(packet), "Wake-only sends one packet without launching RDP");
}
var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
try
{
    var local = profile with { Address = $"127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}", MacAddress = "" };
    Check(await RdpClient.IsRdpAvailableAsync(local, CancellationToken.None), "Recognize an open TCP port");
    bool launched = false;
    var result = await RdpClient.StartAndConnectAsync(local, _ => throw new Exception("Online PC should not need WoL"),
        CancellationToken.None, _ => launched = true);
    Check(result == RdpStartResult.Connected && launched, "Start and connect skips WoL when RDP already answers");
}
finally { listener.Stop(); }

async Task CheckMpsAsync(string expected, Func<RdpProfile, Task> call)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var http = new TcpListener(IPAddress.Loopback, 0);
    http.Start();
    try
    {
        var server = profile with { ServerAddress = $"http://127.0.0.1:{((IPEndPoint)http.LocalEndpoint).Port}" };
        var pending = call(server);
        using var incoming = await http.AcceptTcpClientAsync(timeout.Token);
        using var stream = incoming.GetStream();
        using var reader = new StreamReader(stream, leaveOpen: true);
        string? request = await reader.ReadLineAsync(timeout.Token);
        while (!string.IsNullOrEmpty(await reader.ReadLineAsync(timeout.Token))) {}
        Check(request?.StartsWith(expected + " ", StringComparison.Ordinal) == true, $"MPS request: {expected}");
        const string body = "{\"status\":true}";
        await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}"), timeout.Token);
        await pending;
    }
    finally { http.Stop(); }
}
await CheckMpsAsync("GET /info/status", async server =>
    Check(await MpsClient.IsAvailableAsync(server, CancellationToken.None), "Parse online MPS status"));
await CheckMpsAsync("POST /control/shutdown", server => MpsClient.SendPowerAsync(server, PowerActionKind.Shutdown));
await CheckMpsAsync("POST /control/reboot", server => MpsClient.SendPowerAsync(server, PowerActionKind.Restart));

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
Console.WriteLine($"PASS: {checks} checks, including loopback UDP and TCP. No power actions or RDP sessions launched.");
