using System.Net;
using System.Text;
using System.Text.Json.Nodes;
namespace AsterVPN;
public static class AmneziaConfig
{
    public static async Task<JsonObject> BuildAsync(ServerProfile profile, int port, string password, CancellationToken ct = default, bool protectedDns = false)
    {
        var o = profile.Outbound;
        var peer = o["peers"]![0]!;
        var host = peer["address"]!.GetValue<string>();
        var addresses = await BootstrapDns.ResolveAsync(host, protectedDns, ct);
        var address = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) ?? addresses.FirstOrDefault() ?? throw new FormatException("AmneziaWG: адрес сервера не найден.");
        string Key(string value)
        {
            var bytes = value.Length == 64 && value.All(Uri.IsHexDigit) ? Convert.FromHexString(value) : Convert.FromBase64String(value);
            if (bytes.Length != 32) throw new FormatException("AmneziaWG: ключ должен содержать 32 байта.");
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
        var uapi = new StringBuilder();
        void Set(string key, string value)
        {
            if (value.Contains('\n') || value.Contains('\r')) throw new FormatException("AmneziaWG: недопустимый перенос строки в параметре.");
            uapi.Append(key).Append('=').Append(value).Append('\n');
        }
        Set("private_key", Key(o["private_key"]!.GetValue<string>()));
        var special = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["HeaderProtectionKey"] = "header_protection_key", ["ContentPaddingAddition"] = "content_padding_addition", ["RekeyAfterTime"] = "rekey_after_time", ["RekeyTimeout"] = "rekey_timeout", ["RejectAfterTime"] = "reject_after_time", ["KeepaliveTimeout"] = "keepalive_timeout", ["MaxHandshakeAttempts"] = "max_handshake_attempts", ["RandomTrailers"] = "random_trailers", ["DisableCookies"] = "disable_cookies"
        };
        foreach (var (key, node) in o["awg_parameters"]!.AsObject())
        {
            var value = node!.GetValue<string>();
            if (key.Equals("HeaderProtectionKey", StringComparison.OrdinalIgnoreCase)) value = Key(value);
            if (key is "RandomTrailers" or "DisableCookies") value = value.ToLowerInvariant() switch { "true" or "1" or "on" or "yes" or "enabled" => "true", "false" or "0" or "off" or "no" or "disabled" => "false", _ => throw new FormatException("AmneziaWG: неверный логический параметр.") };
            Set(special.GetValueOrDefault(key, key.ToLowerInvariant()), value);
        }
        Set("replace_peers", "true"); Set("public_key", Key(peer["public_key"]!.GetValue<string>()));
        if (peer["pre_shared_key"] is { } psk) Set("preshared_key", Key(psk.GetValue<string>()));
        Set("endpoint", new IPEndPoint(address, peer["port"]!.GetValue<int>()).ToString());
        Set("replace_allowed_ips", "true");
        foreach (var ip in peer["allowed_ips"]!.AsArray()) Set("allowed_ip", ip!.GetValue<string>());
        Set("persistent_keepalive_interval", peer["persistent_keepalive_interval"]?.ToString() ?? "0");
        return new JsonObject
        {
            ["addresses"] = SubscriptionParser.Array(o["address"]!.AsArray().Select(a => a!.GetValue<string>().Split('/')[0])),
            ["dns"] = o["dns"]!.DeepClone(), ["mtu"] = o["mtu"]!.DeepClone(), ["uapi"] = uapi.ToString(), ["port"] = port, ["password"] = password
        };
    }
}
