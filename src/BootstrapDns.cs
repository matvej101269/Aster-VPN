using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace AsterVPN;

public static class BootstrapDns
{
    // Literal HTTPS endpoints avoid asking the Windows DNS service outside TUN.
    public static async Task<IPAddress[]> ResolveAsync(string host, bool protectedConnection, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var address)) return [address];
        if (!protectedConnection) return await Dns.GetHostAddressesAsync(host, ct);
        using var handler = new HttpClientHandler { UseProxy = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        foreach (var resolver in new[] { "1.1.1.1", "1.0.0.1" })
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"https://{resolver}/dns-query?name={Uri.EscapeDataString(host)}&type=A");
                request.Headers.Accept.ParseAdd("application/dns-json");
                using var response = await client.SendAsync(request, ct);
                response.EnsureSuccessStatusCode();
                var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
                var addresses = json?["Answer"]?.AsArray().Where(a => a?["type"]?.GetValue<int>() == 1)
                    .Select(a => IPAddress.Parse(a!["data"]!.GetValue<string>())).ToArray();
                if (addresses is { Length: > 0 }) return addresses;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException) { }
        }
        throw new System.IO.IOException("Не удалось разрешить адрес VPN-сервера через защищённый DNS.");
    }
}
