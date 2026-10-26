using System.IO;
using System.Net.Http;
namespace AsterVPN;
public sealed class ConnectionProfileUnavailableException(string message) : IOException(message);

public static class SubscriptionService
{
    public static async Task<ServerProfile> RefreshForRecoveryAsync(Settings settings, ServerProfile server, CancellationToken ct = default,
        Func<string, CancellationToken, Task<SubscriptionProfile>>? fetch = null, Action? onCachedFallback = null, TimeSpan? timeout = null)
    {
        ct.ThrowIfCancellationRequested();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(timeout ?? TimeSpan.FromSeconds(8));
        try { return await RefreshConnectionAsync(settings, server, budget.Token, fetch).WaitAsync(budget.Token); }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException || ex is IOException and not ConnectionProfileUnavailableException)
        {
            // A subscription API outage must not prevent a cached VPN reconnect.
            // Explicit removal/ambiguity is different: never silently revive that profile.
            ct.ThrowIfCancellationRequested();
            onCachedFallback?.Invoke();
            return server;
        }
    }

    public static async Task<ServerProfile> RefreshConnectionAsync(Settings settings, ServerProfile server, CancellationToken ct = default,
        Func<string, CancellationToken, Task<SubscriptionProfile>>? fetch = null)
    {
        // REALITY credentials can rotate independently of the ordinary update
        // interval. Refresh only subscribed Xray profiles; leave AWG untouched.
        if (!settings.AutoUpdate || !server.UsesXray) return server;
        var owner = settings.Subscriptions.FirstOrDefault(s => s.Servers.Any(p => p.Id == server.Id));
        if (owner == null || string.IsNullOrWhiteSpace(owner.Url)) return server;
        fetch ??= FetchAsync;
        var latest = await fetch(owner.Url, ct);
        var matches = latest.Servers.Where(p => p.Name == server.Name && p.Protocol == server.Protocol && p.UsesXray == server.UsesXray).ToArray();
        if (matches.Length != 1) throw new ConnectionProfileUnavailableException("Выбранный VLESS-сервер удалён или неоднозначен в новой подписке. Обновите список и выберите сервер.");
        var fresh = matches[0];
        return new ServerProfile { Id = server.Id, Name = fresh.Name, Outbound = (System.Text.Json.Nodes.JsonObject)fresh.Outbound.DeepClone(), IsEndpoint = fresh.IsEndpoint, UsesXray = fresh.UsesXray };
    }
    public static string NormalizeAddress(string address)
    {
        address = address.Trim();
        var markdown = System.Text.RegularExpressions.Regex.Match(address, @"^\[[^\]\r\n]*\]\((https://[^\s)]+)\)$");
        if (markdown.Success) address = markdown.Groups[1].Value;
        return address.Trim('<', '>', '"');
    }
    public static async Task<List<ServerProfile>> DownloadAsync(string address, CancellationToken ct = default) => (await FetchAsync(address, ct)).Servers;
    public static async Task<SubscriptionProfile> FetchAsync(string address, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(NormalizeAddress(address), UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0)
            throw new FormatException("Для подписки нужна ссылка HTTPS без user:password перед адресом.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(40));
        ct = timeout.Token;
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(40) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"AsterVPN/{ApplicationVersion.Current}");
        var origin = uri.GetLeftPart(UriPartial.Authority);
        for (var redirects = 0; redirects < 6; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (uri.GetLeftPart(UriPartial.Authority) == origin) DeviceIdentity.AddHeaders(request, DeviceIdentity.Hwid);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var next = response.Headers.Location ?? throw new IOException("Пустой адрес перенаправления.");
                uri = next.IsAbsoluteUri ? next : new Uri(uri, next);
                if (uri.Scheme != "https" || uri.UserInfo.Length > 0) throw new IOException("Небезопасное перенаправление подписки.");
                continue;
            }
            if (!response.IsSuccessStatusCode) throw new IOException($"Сервер подписки вернул HTTP {(int)response.StatusCode}.");
            const int limit = 8 * 1024 * 1024;
            if (response.Content.Headers.ContentLength > limit) throw new IOException("Подписка больше 8 МБ.");
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream(); var chunk = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + count > limit) throw new IOException("Подписка больше 8 МБ.");
                buffer.Write(chunk, 0, count);
            }
            var profile = ReadMetadata(key => response.Headers.TryGetValues(key, out var values) ? values.FirstOrDefault() : null);
            profile.Url = NormalizeAddress(address);
            profile.Servers = SubscriptionParser.Parse(System.Text.Encoding.UTF8.GetString(buffer.ToArray()));
            profile.LastUpdated = DateTimeOffset.UtcNow;
            if (profile.Name == "Подписка") profile.Name = uri.Host;
            return profile;
        }
        throw new IOException("Слишком много перенаправлений подписки.");
    }
    public static SubscriptionProfile ReadMetadata(Func<string, string?> header)
    {
        string Decode(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            if (value.StartsWith("base64:", StringComparison.OrdinalIgnoreCase))
                try { value = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value[7..])); } catch (FormatException) { return ""; }
            return new string(value.Where(c => !char.IsControl(c)).Take(500).ToArray()).Trim();
        }
        var result = new SubscriptionProfile();
        var title = Decode(header("Profile-Title")); if (title.Length > 0) result.Name = title;
        result.Announcement = Decode(header("Announce"));
        if (int.TryParse(header("Profile-Update-Interval"), out var hours)) result.UpdateIntervalHours = Math.Clamp(hours, 1, 168);
        foreach (var field in (header("Subscription-Userinfo") ?? "").Split(';', StringSplitOptions.TrimEntries))
        {
            var pair = field.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2 || !long.TryParse(pair[1], out var value) || value < 0) continue;
            switch (pair[0].ToLowerInvariant())
            {
                case "upload": result.Upload = value; break;
                case "download": result.Download = value; break;
                case "total": result.Total = value; break;
                case "expire": if (value <= 253402300799) result.Expire = value; break;
            }
        }
        return result;
    }
}
