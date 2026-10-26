using System.Text.Json.Nodes;
namespace AsterVPN;
public static class XrayConfig
{
    public static void BindTransport(JsonObject config, string address, string interfaceName, string? sourceAddress = null)
    {
        if (!System.Net.IPAddress.TryParse(address, out _) || string.IsNullOrWhiteSpace(interfaceName))
            throw new ArgumentException("Не определён исходящий интерфейс Xray.");
        var outbound = config["outbounds"]![0]!;
        if (sourceAddress != null)
        {
            if (!System.Net.IPAddress.TryParse(sourceAddress, out var source) || source.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                throw new ArgumentException("Не определён исходящий IPv4 Xray.");
            outbound["sendThrough"] = sourceAddress;
        }
        outbound["settings"]!["vnext"]![0]!["address"] = address;
        var stream = outbound["streamSettings"]!;
        stream["sockopt"] ??= new JsonObject();
        stream["sockopt"]!["interface"] = interfaceName;
    }
    public static ServerProfile ParseVless(Uri uri, Dictionary<string, string> q)
    {
        string Get(string key, string fallback = "") => q.GetValueOrDefault(key, fallback);
        var security = Get("security", "none");
        var stream = new JsonObject { ["network"] = "xhttp", ["security"] = security };
        var xhttp = new JsonObject { ["path"] = Get("path", "/"), ["host"] = Get("host"), ["mode"] = Get("mode", "auto") };
        if (Get("extra") is { Length: > 0 } extra)
        {
            var parsed = JsonNode.Parse(extra);
            if (parsed is JsonObject obj) xhttp["extra"] = obj;
            else if (parsed != null) throw new FormatException("XHTTP extra должен быть объектом или null.");
        }
        else if (Get("x_padding_bytes") is { Length: > 0 } padding) xhttp["extra"] = new JsonObject { ["xPaddingBytes"] = padding };
        stream["xhttpSettings"] = xhttp;
        if (Get("allowInsecure") is "1" or "true" || Get("insecure") is "1" or "true") throw new FormatException("Отключение проверки TLS не поддерживается.");
        if (security == "reality") stream["realitySettings"] = new JsonObject { ["serverName"] = Get("sni", uri.IdnHost), ["fingerprint"] = Get("fp", "chrome"), ["publicKey"] = Get("pbk"), ["shortId"] = Get("sid"), ["spiderX"] = Get("spx", "/") };
        else if (security == "tls") stream["tlsSettings"] = new JsonObject { ["serverName"] = Get("sni", uri.IdnHost), ["fingerprint"] = Get("fp", "chrome"), ["allowInsecure"] = false };
        else if (security != "none") throw new FormatException("Неизвестный security XHTTP.");
        if (security == "tls" && Get("alpn") is { Length: > 0 } alpn)
            stream["tlsSettings"]!["alpn"] = SubscriptionParser.Array(alpn.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var user = new JsonObject { ["id"] = Uri.UnescapeDataString(uri.UserInfo), ["encryption"] = Get("encryption", "none") };
        if (Get("flow") is { Length: > 0 } flow) user["flow"] = flow;
        var outbound = new JsonObject
        {
            ["protocol"] = "vless", ["tag"] = "proxy",
            ["settings"] = new JsonObject { ["vnext"] = new JsonArray(new JsonObject { ["address"] = uri.IdnHost.Trim('[', ']'), ["port"] = uri.Port, ["users"] = new JsonArray(user) }) },
            ["streamSettings"] = stream
        };
        return new() { Name = uri.Fragment.Length > 1 ? Uri.UnescapeDataString(uri.Fragment[1..]) : "VLESS XHTTP", UsesXray = true, Outbound = new JsonObject { ["type"] = "vless", ["server"] = uri.IdnHost.Trim('[', ']'), ["server_port"] = uri.Port, ["xray"] = outbound } };
    }
    public static JsonObject Build(ServerProfile profile, int port, string password)
    {
        var outbound = (JsonObject)profile.Outbound["xray"]!.DeepClone();
        // Preserve the provider's XHTTP pool settings and Xray's native defaults.
        // XHTTP already multiplexes HTTP streams; do not add a second mux layer.
        return new()
    {
        ["log"] = new JsonObject { ["loglevel"] = "warning" },
        ["inbounds"] = new JsonArray(new JsonObject { ["listen"] = "127.0.0.1", ["port"] = port, ["protocol"] = "socks", ["settings"] = new JsonObject { ["auth"] = "password", ["accounts"] = new JsonArray(new JsonObject { ["user"] = "aster", ["pass"] = password }), ["udp"] = true, ["ip"] = "127.0.0.1" } }),
        ["outbounds"] = new JsonArray(outbound)
    };
    }
}
