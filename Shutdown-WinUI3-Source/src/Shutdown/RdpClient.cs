using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ShutdownApp;

internal enum RdpStartResult { Connected, WakeSent, NeedsWakeSettings, TimedOut }
internal enum RdpPrimaryAction { Connect, Start, WakeOnly }
internal readonly record struct RdpAvailability(bool RdpReady, bool PingResponded, bool MpsReady)
{
    internal bool Online => RdpReady || PingResponded || MpsReady;
}

internal static class RdpClient
{
    internal static RdpPrimaryAction PrimaryAction(RdpProfile profile, RdpAvailability? availability) =>
        availability is { Online: false } && profile.CanWake
            ? profile.ConnectAfterWake ? RdpPrimaryAction.Start : RdpPrimaryAction.WakeOnly
            : RdpPrimaryAction.Connect;

    internal static async Task<RdpAvailability> GetAvailabilityAsync(RdpProfile profile, CancellationToken token)
    {
        var rdp = IsRdpAvailableAsync(profile, token);
        var ping = IsPingAvailableAsync(profile, token);
        var mps = MpsClient.IsAvailableAsync(profile, token);
        await Task.WhenAll(rdp, ping, mps);
        return new(await rdp, await ping, await mps);
    }

    internal static async Task<bool> IsPingAvailableAsync(RdpProfile profile, CancellationToken cancellationToken)
    {
        if (!profile.TryGetRdp(out string host, out _)) return false;
        using var ping = new Ping();
        try
        {
            var reply = await ping.SendPingAsync(host, 1000).WaitAsync(cancellationToken);
            // A public hostname can answer ICMP at the router even when the forwarded PC is off.
            return reply.Status == IPStatus.Success && IsPrivatePingAddress(reply.Address);
        }
        catch (PingException) { return false; }
    }

    private static bool IsPrivatePingAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal) return true;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        byte[] bytes = address.GetAddressBytes();
        return address.AddressFamily == AddressFamily.InterNetwork
            ? bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127) ||
                (bytes[0] == 169 && bytes[1] == 254)
            : (bytes[0] & 0xfe) == 0xfc;
    }

    internal static void Connect(RdpProfile profile)
    {
        using var process = Start(CreateDirectStartInfo(profile));
    }

    internal static ProcessStartInfo CreateDirectStartInfo(RdpProfile profile)
    {
        var start = CreateStartInfo();
        start.ArgumentList.Add("/v:" + profile.ConnectionAddress());
        return start;
    }

    private static ProcessStartInfo CreateStartInfo() =>
        new(Path.Combine(Environment.SystemDirectory, "mstsc.exe")) { UseShellExecute = false };

    private static Process Start(ProcessStartInfo start) =>
        Process.Start(start) ?? throw new InvalidOperationException("Could not start mstsc.exe.");

    internal static async Task WakeAsync(RdpProfile profile, CancellationToken cancellationToken)
    {
        if (!profile.CanWake)
            throw new InvalidOperationException("Invalid Wake-on-LAN profile.");
        string target = profile.WakeAddress.Trim();
        IPAddress address = IPAddress.TryParse(target, out var ip) ? ip :
            (await Dns.GetHostAddressesAsync(target, AddressFamily.InterNetwork, cancellationToken)).FirstOrDefault()
                ?? throw new SocketException((int)SocketError.HostNotFound);
        byte[] packet = profile.WakePacket();
        using var client = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        await client.SendAsync(packet.AsMemory(), new IPEndPoint(address, profile.WakePort), cancellationToken);
    }

    internal static async Task<bool> IsRdpAvailableAsync(RdpProfile profile, CancellationToken cancellationToken)
    {
        if (!profile.TryGetRdp(out string host, out int port)) return false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            using var connection = new TcpClient();
            await connection.ConnectAsync(host, port, timeout.Token);
            return true;
        }
        catch (SocketException) { return false; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
    }

    internal static async Task<RdpStartResult> StartAndConnectAsync(RdpProfile profile, Action<bool> waiting,
        CancellationToken cancellationToken, Action<RdpProfile>? connect = null,
        RdpPrimaryAction intended = RdpPrimaryAction.Connect)
    {
        connect ??= Connect;
        if (intended != RdpPrimaryAction.WakeOnly && await IsRdpAvailableAsync(profile, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            connect(profile);
            return RdpStartResult.Connected;
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            bool wake = intended == RdpPrimaryAction.WakeOnly;
            if (!wake)
            {
                var ping = IsPingAvailableAsync(profile, timeout.Token);
                var mps = MpsClient.IsAvailableAsync(profile, timeout.Token);
                await Task.WhenAll(ping, mps);
                wake = !(await ping || await mps);
            }
            if (wake)
            {
                if (!profile.CanWake) return RdpStartResult.NeedsWakeSettings;
                if (intended != RdpPrimaryAction.WakeOnly && profile.ConnectAfterWake) waiting(true);
                await WakeAsync(profile, timeout.Token);
                if (intended == RdpPrimaryAction.WakeOnly || !profile.ConnectAfterWake)
                    return RdpStartResult.WakeSent;
            }
            else waiting(false);
            while (!await IsRdpAvailableAsync(profile, timeout.Token))
                await Task.Delay(TimeSpan.FromSeconds(1), timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            connect(profile);
            return RdpStartResult.Connected;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return RdpStartResult.TimedOut;
        }
    }
}
