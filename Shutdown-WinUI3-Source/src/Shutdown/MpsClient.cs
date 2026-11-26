using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ShutdownApp;

internal static class MpsClient
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
    {
        Timeout = TimeSpan.FromSeconds(4)
    };

    internal static async Task<bool> IsAvailableAsync(RdpProfile profile, CancellationToken cancellationToken)
    {
        if (!profile.TryGetServer(out var server)) return false;
        try
        {
            using var response = await Client.GetAsync(new Uri(server, "/info/status"), cancellationToken);
            if (!response.IsSuccessStatusCode) return false;
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.True;
        }
        catch (HttpRequestException) { return false; }
        catch (JsonException) { return false; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
    }

    internal static async Task SendPowerAsync(RdpProfile profile, PowerActionKind action)
    {
        if (!profile.TryGetServer(out var server)) throw new InvalidOperationException("Invalid Magic Packet Server address.");
        string path = action switch
        {
            PowerActionKind.Shutdown => "/control/shutdown",
            PowerActionKind.Restart => "/control/reboot",
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(server, path)) { Content = new ByteArrayContent(Array.Empty<byte>()) };
        using var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
}
