using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Serialization;

namespace ShutdownApp;

public sealed record RdpProfile
{
    private const int DefaultRdpPort = 3389;
    public string PcName { get; init; } = "";
    public string Address { get; init; } = "";
    public bool ConnectAfterWake { get; init; } = true;
    public string MacAddress { get; init; } = "";
    public string WakeAddress { get; init; } = "255.255.255.255";
    public int WakePort { get; init; } = 9;
    public string ServerAddress { get; init; } = "";

    [JsonIgnore] public bool CanConnect => TryGetRdp(out _, out _);
    [JsonIgnore] public bool CanWake => TryGetMac(out _) && IsWakeHost(WakeAddress) && IsPort(WakePort);
    [JsonIgnore] public bool CanUseServer => TryGetServer(out _);
    [JsonIgnore] public bool CanSave => IsSingleLine(PcName) &&
        (string.IsNullOrWhiteSpace(Address) || CanConnect) &&
        (string.IsNullOrWhiteSpace(MacAddress) || TryGetMac(out _)) &&
        IsWakeHost(WakeAddress) && IsPort(WakePort) &&
        (string.IsNullOrWhiteSpace(ServerAddress) || CanUseServer);

    internal bool TryGetRdp(out string host, out int port)
    {
        host = "";
        port = DefaultRdpPort;
        if (!IsSingleLine(Address)) return false;
        string value = Address.Trim();
        if (value.Length == 0 || value.Any(char.IsWhiteSpace)) return false;
        if (value.StartsWith('['))
        {
            int end = value.IndexOf(']');
            if (end < 0 || !IPAddress.TryParse(value[1..end], out var ip) ||
                ip.AddressFamily != AddressFamily.InterNetworkV6) return false;
            host = ip.ToString();
            return end == value.Length - 1 || (value[end + 1] == ':' && TryPort(value[(end + 2)..], out port));
        }
        if (IPAddress.TryParse(value, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            host = address.ToString();
            return true;
        }
        int colon = value.IndexOf(':');
        host = colon < 0 ? value : value[..colon];
        return IsWakeHost(host) && (colon < 0 || TryPort(value[(colon + 1)..], out port));
    }

    internal string ConnectionAddress()
    {
        if (!TryGetRdp(out string host, out int port)) throw new InvalidOperationException("Invalid RDP address.");
        return host.Contains(':') ? $"[{host}]:{port}" : $"{host}:{port}";
    }

    internal bool TryGetServer(out Uri uri)
    {
        if (IsSingleLine(ServerAddress) && Uri.TryCreate(ServerAddress.Trim(), UriKind.Absolute, out var parsed) &&
            (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps) &&
            parsed.Host.Length > 0 && parsed.UserInfo.Length == 0 && parsed.AbsolutePath == "/" &&
            parsed.Query.Length == 0 && parsed.Fragment.Length == 0)
        {
            uri = parsed;
            return true;
        }
        uri = null!;
        return false;
    }

    internal byte[] WakePacket()
    {
        if (!TryGetMac(out var mac)) throw new InvalidOperationException("Invalid Wake-on-LAN MAC address.");
        byte[] packet = new byte[6 + 16 * mac.Length];
        Array.Fill(packet, (byte)0xff, 0, 6);
        for (int i = 0; i < 16; i++) mac.CopyTo(packet, 6 + i * mac.Length);
        return packet;
    }

    internal RdpProfile Normalize() => this with
    {
        PcName = IsSingleLine(PcName) ? PcName.Trim() : "",
        Address = CanConnect ? Address.Trim() : "",
        MacAddress = TryGetMac(out _) ? MacAddress.Trim() : "",
        WakeAddress = IsWakeHost(WakeAddress) ? WakeAddress.Trim() : "255.255.255.255",
        WakePort = IsPort(WakePort) ? WakePort : 9,
        ServerAddress = CanUseServer ? ServerAddress.Trim() : ""
    };

    private bool TryGetMac(out byte[] mac)
    {
        mac = Array.Empty<byte>();
        string value = (MacAddress ?? "").Trim().Replace("-", "").Replace(":", "");
        if (value.Length != 12 || !value.All(Uri.IsHexDigit)) return false;
        mac = Convert.FromHexString(value);
        return mac.Any(b => b != 0) && (mac[0] & 1) == 0;
    }

    private static bool IsSingleLine(string? value) =>
        value is not null && value.Length <= 256 && !value.Any(char.IsControl);

    private static bool IsWakeHost(string? value) => IsSingleLine(value) &&
        Uri.CheckHostName(value!.Trim()) is UriHostNameType.Dns or UriHostNameType.IPv4;

    private static bool IsPort(int value) => value is >= 1 and <= 65535;
    private static bool TryPort(string text, out int port) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out port) && IsPort(port);
}
