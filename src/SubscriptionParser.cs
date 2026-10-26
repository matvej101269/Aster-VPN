using System.Text;
using System.Text.Json.Nodes;
namespace AsterVPN;

public static class SubscriptionParser
{
    private static readonly HashSet<string> NativeTypes = ["vless", "vmess", "trojan", "shadowsocks", "hysteria", "hysteria2", "tuic", "socks", "http", "ssh", "shadowtls", "anytls", "naive", "snell", "wireguard", "openvpn-client", "openconnect"];
    public static List<ServerProfile> Parse(string input) => ParseCore(input, 0);
    private static List<ServerProfile> ParseCore(string input, int depth)
    {
        if (depth > 8) throw new FormatException("Слишком много вложенных контейнеров подписки.");
        input = input.Trim().TrimStart('\uFEFF');
        if (System.Text.RegularExpressions.Regex.IsMatch(input, @"\A(?:\s*#[^\n]*\n)*\s*\[(Interface|Device)\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return [ParseWireGuard(input)];
        if (input.StartsWith('{') || input.StartsWith('['))
        {
            var root = JsonNode.Parse(input)!;
            var entries = root as JsonArray;
            if (entries == null && (root["outbounds"] is JsonArray || root["endpoints"] is JsonArray))
            {
                entries = new JsonArray();
                foreach (var section in new[] { "outbounds", "endpoints" })
                    if (root[section] is JsonArray list) foreach (var item in list) entries.Add(item?.DeepClone());
            }
            entries ??= new JsonArray(root.DeepClone());
            var result = new List<ServerProfile>();
            foreach (var node in entries.OfType<JsonObject>())
            {
                var type = node["type"]?.GetValue<string>() ?? "";
                if (type == "amneziawg" && node["servers"] is JsonArray awgServers)
                {
                    foreach (var item in awgServers)
                    {
                        var value = item!["config"]!.GetValue<string>();
                        var p = ParseWireGuard(value.Contains("[Interface]") ? value : Decode64(value));
                        p.Name = item["name"]?.GetValue<string>() ?? p.Name; result.Add(p);
                    }
                    continue;
                }
                if (node["outbounds"] is JsonArray || node["endpoints"] is JsonArray) { result.AddRange(ParseCore(node.ToJsonString(), depth + 1)); continue; }
                if (type is "direct" or "block" or "dns" or "selector" or "urltest") continue;
                if (!NativeTypes.Contains(type)) throw new FormatException($"Тип {type} не поддерживается импортом.");
                var copy = (JsonObject)node.DeepClone();
                // A provider may supply outbound credentials, never local listeners, files or executable hooks.
                if (copy["detour"] != null) throw new FormatException("Цепочки detour пока не поддерживаются.");
                RejectFileReferences(copy);
                var name = copy["tag"]?.GetValue<string>() ?? type;
                copy.Remove("tag");
                result.Add(new() { Name = name, Outbound = copy, IsEndpoint = type is "wireguard" or "openvpn-client" or "openconnect" });
            }
            return RequireServers(result);
        }
        if (!input.Contains("://"))
        {
            string decoded;
            try { decoded = Decode64(input); }
            catch (FormatException) { throw new FormatException("Ожидается список URI, Base64-подписка, WireGuard .conf или JSON sing-box. Clash YAML пока не поддерживается."); }
            return ParseCore(decoded, depth + 1);
        }
        var profiles = new List<ServerProfile>();
        var lines = input.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith('#')) continue;
            if (Uri.TryCreate(lines[i], UriKind.Absolute, out var extra) && extra.Scheme == "tg" && extra.Host == "proxy")
            {
                continue;
            }
            try { profiles.Add(ParseUri(lines[i])); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { throw new FormatException($"Строка {i + 1}: неподдерживаемый или неверный профиль. Проверьте протокол, порт и параметры. ({ex.GetType().Name})"); }
        }
        return RequireServers(profiles);
    }
    private static void RejectFileReferences(JsonNode node)
    {
        if (node is JsonObject obj)
            foreach (var (key, value) in obj)
            {
                if (key == "insecure" && value?.ToString().Equals("true", StringComparison.OrdinalIgnoreCase) == true) throw new FormatException("Отключение проверки TLS-сертификатов не поддерживается.");
                if (key.EndsWith("_path", StringComparison.Ordinal) || key is "path" or "bind_interface")
                {
                    // Transport URL path is safe; filesystem paths are not.
                    if (key != "path") throw new FormatException("Ссылки на локальные файлы/интерфейсы в подписке запрещены.");
                }
                if (value != null) RejectFileReferences(value);
            }
        else if (node is JsonArray array) foreach (var value in array) if (value != null) RejectFileReferences(value);
    }
    private static ServerProfile ParseWireGuard(string input)
    {
        var title = System.Text.RegularExpressions.Regex.Match(input, @"(?mi)^\s*#\s*Name\s*[:=]\s*(.+)$");
        var displayName = title.Success ? title.Groups[1].Value.Trim() : null;
        var local = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var peer = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var section = ""; var peerCount = 0;
        var awgKeys = new[] { "Jc", "Jmin", "Jmax", "S1", "S2", "S3", "S4", "H1", "H2", "H3", "H4", "I1", "I2", "I3", "I4", "I5", "HeaderProtectionKey", "ContentPaddingAddition", "RekeyAfterTime", "RekeyTimeout", "RejectAfterTime", "KeepaliveTimeout", "MaxHandshakeAttempts", "RandomTrailers", "DisableCookies" };
        foreach (var raw in input.Split('\n'))
        {
            var line = raw.Split('#', 2)[0].Trim(); if (line.Length == 0) continue;
            if (line.StartsWith('[')) { section = line.ToLowerInvariant(); if (section == "[peer]") peerCount++; if (section is not ("[interface]" or "[device]" or "[peer]")) throw new FormatException("Неизвестная секция WireGuard."); continue; }
            var parts = line.Split('=', 2); if (parts.Length != 2) throw new FormatException("Неверный WireGuard .conf.");
            var key = parts[0].Trim();
            var supported = section is "[interface]" or "[device]" ? new[] { "PrivateKey", "Address", "DNS", "MTU", "ListenPort" }.Concat(awgKeys).ToArray() : new[] { "PublicKey", "PresharedKey", "Endpoint", "AllowedIPs", "PersistentKeepalive" };
            if (!supported.Contains(key, StringComparer.OrdinalIgnoreCase)) throw new FormatException($"WireGuard: параметр {key} не поддерживается. Скрипты не выполняются.");
            (section is "[interface]" or "[device]" ? local : peer)[key] = parts[1].Trim();
        }
        if (peerCount != 1) throw new FormatException("Импорт WireGuard поддерживает один Peer.");
        var endpoint = new Uri("udp://" + peer["Endpoint"]);
        if (endpoint.Port < 1) throw new FormatException("WireGuard: нет порта.");
        var p = new JsonObject { ["address"] = endpoint.IdnHost.Trim('[', ']'), ["port"] = endpoint.Port, ["public_key"] = peer["PublicKey"], ["allowed_ips"] = Array(peer["AllowedIPs"].Split(',', StringSplitOptions.TrimEntries)) };
        if (awgKeys.Any(local.ContainsKey)) p["persistent_keepalive_interval"] = peer.GetValueOrDefault("PersistentKeepalive", "0");
        else p["persistent_keepalive_interval"] = int.Parse(peer.GetValueOrDefault("PersistentKeepalive", "0"));
        if (peer.TryGetValue("PresharedKey", out var keyValue)) p["pre_shared_key"] = keyValue;
        var outbound = new JsonObject { ["type"] = "wireguard", ["system"] = false, ["address"] = Array(local["Address"].Split(',', StringSplitOptions.TrimEntries)), ["private_key"] = local["PrivateKey"], ["mtu"] = int.Parse(local.GetValueOrDefault("MTU", "1400")), ["peers"] = new JsonArray(p) };
        if (awgKeys.Any(local.ContainsKey))
        {
            var parameters = new JsonObject();
            foreach (var key in awgKeys) if (local.TryGetValue(key, out var value)) parameters[key] = value;
            outbound["type"] = "amneziawg"; outbound["awg_parameters"] = parameters;
            outbound["dns"] = Array(local.GetValueOrDefault("DNS", "1.1.1.1").Split(',', StringSplitOptions.TrimEntries));
            return new() { Name = displayName ?? "AmneziaWG — " + endpoint.Host, Outbound = outbound };
        }
        return new() { Name = displayName ?? "WireGuard — " + endpoint.Host, Outbound = outbound, IsEndpoint = true };
    }
    private static List<ServerProfile> RequireServers(List<ServerProfile> profiles)
    {
        if (profiles.Count == 0) throw new FormatException("Подписка не содержит серверов.");
        if (profiles.Count > 5000) throw new FormatException("В подписке больше 5000 серверов.");
        return profiles;
    }
    public static string Decode64(string text)
    {
        text = string.Concat(text.Where(c => !char.IsWhiteSpace(c))).Replace('-', '+').Replace('_', '/');
        return Encoding.UTF8.GetString(Convert.FromBase64String(text.PadRight((text.Length + 3) / 4 * 4, '=')));
    }
    public static ServerProfile ParseUri(string link)
    {
        var scheme = link.Split("://", 2)[0].ToLowerInvariant();
        if (scheme is "awg" or "amneziawg" or "vpn" or "wireguard" or "wg") return ParseWgLink(link);
        if (link.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase)) return ParseVmess(link[8..]);
        if (link.StartsWith("ss://", StringComparison.OrdinalIgnoreCase)) return ParseShadowsocks(link);
        var uri = new Uri(link);
        var type = uri.Scheme.ToLowerInvariant() switch { "hy2" => "hysteria2", "socks5" => "socks", "socks4" => "socks", var t => t };
        if (type is not ("vless" or "trojan" or "hysteria2" or "tuic" or "socks" or "http" or "https" or "anytls")) throw new FormatException("Протокол URI не поддерживается.");
        var q = Query(uri.Query);
        if (type == "vless" && Get(q, "type") is "xhttp" or "splithttp") return XrayConfig.ParseVless(uri, q);
        var port = uri.Port > 0 ? uri.Port : type is "trojan" or "hysteria2" or "tuic" or "anytls" ? 443 : throw new FormatException("Нет порта.");
        var outbound = new JsonObject { ["type"] = type == "https" ? "http" : type, ["server"] = uri.IdnHost.Trim('[', ']'), ["server_port"] = port };
        var user = Uri.UnescapeDataString(uri.UserInfo);
        switch (type)
        {
            case "vless": outbound["uuid"] = user; if (Get(q, "flow") is { Length: > 0 } flow) outbound["flow"] = flow; break;
            case "trojan": case "hysteria2": case "anytls": outbound["password"] = user; break;
            case "tuic":
                var auth = user.Split(':', 2); outbound["uuid"] = auth[0]; outbound["password"] = auth.Length > 1 ? auth[1] : "";
                outbound["congestion_control"] = Get(q, "congestion_control", "cubic"); break;
            default:
                var credentials = user.Split(':', 2);
                if (user.Length > 0) { outbound["username"] = credentials[0]; outbound["password"] = credentials.Length > 1 ? credentials[1] : ""; }
                if (type == "socks") outbound["version"] = uri.Scheme == "socks4" ? "4" : "5";
                break;
        }
        var security = Get(q, "security", type is "trojan" or "hysteria2" or "tuic" or "https" or "anytls" ? "tls" : "none");
        if (security is "tls" or "reality")
        {
            var tls = new JsonObject { ["enabled"] = true, ["server_name"] = Get(q, "sni", Get(q, "peer", uri.IdnHost)) };
            if (Get(q, "insecure") is "1" or "true" || Get(q, "allowInsecure") is "1" or "true") throw new FormatException("Профили с отключенной проверкой сертификата не импортируются.");
            if (Get(q, "alpn") is { Length: > 0 } alpn) tls["alpn"] = Array(alpn.Split(','));
            if (Get(q, "fp") is { Length: > 0 } fp) tls["utls"] = new JsonObject { ["enabled"] = true, ["fingerprint"] = fp };
            if (security == "reality")
            {
                tls["utls"] ??= new JsonObject { ["enabled"] = true, ["fingerprint"] = "chrome" };
                tls["reality"] = new JsonObject { ["enabled"] = true, ["public_key"] = Get(q, "pbk"), ["short_id"] = Get(q, "sid") };
            }
            outbound["tls"] = tls;
        }
        else if (security != "none" && security != "") throw new FormatException("Неизвестный security.");
        ApplyTransport(outbound, Get(q, "type", "tcp"), Get(q, "path", "/"), Get(q, "host"), Get(q, "serviceName"));
        if (type == "hysteria2" && Get(q, "obfs") is { Length: > 0 } obfs)
            outbound["obfs"] = new JsonObject { ["type"] = obfs, ["password"] = Get(q, "obfs-password") };
        return new() { Name = Name(uri, type), Outbound = outbound };
    }
    private static ServerProfile ParseWgLink(string link)
    {
        var body = link[(link.IndexOf("://", StringComparison.Ordinal) + 3)..];
        var pieces = body.Split('#', 2); var name = pieces.Length > 1 ? Uri.UnescapeDataString(pieces[1]) : "";
        ServerProfile profile;
        if (pieces[0].Contains('@'))
        {
            var uri = new Uri(link); var q = Query(uri.Query).ToDictionary(p => p.Key.ToLowerInvariant(), p => p.Value);
            var text = new StringBuilder("[Interface]\nPrivateKey = " + Uri.UnescapeDataString(uri.UserInfo) + "\nAddress = " + Get(q, "address") + "\n");
            foreach (var key in new[] { "dns", "mtu", "jc", "jmin", "jmax", "s1", "s2", "s3", "s4", "h1", "h2", "h3", "h4", "i1", "i2", "i3", "i4", "i5", "headerprotectionkey", "contentpaddingaddition", "rekeyaftertime", "rekeytimeout", "rejectaftertime", "keepalivetimeout", "maxhandshakeattempts", "randomtrailers", "disablecookies" })
                if (q.TryGetValue(key, out var value)) text.Append(key).Append(" = ").Append(value).Append('\n');
            text.Append("[Peer]\nPublicKey = ").Append(Get(q, "publickey")).Append("\nEndpoint = ").Append(uri.Host).Append(':').Append(uri.Port).Append("\nAllowedIPs = ").Append(Get(q, "allowedips", "0.0.0.0/0, ::/0")).Append("\nPersistentKeepalive = ").Append(Get(q, "keepalive", "25")).Append('\n');
            if (q.TryGetValue("presharedkey", out var psk)) text.Append("PresharedKey = ").Append(psk).Append('\n');
            profile = ParseWireGuard(text.ToString());
        }
        else
        {
            var payload = Decode64(pieces[0]);
            // vpn:// in mixed subscriptions commonly wraps a plain AWG .conf.
            if (!payload.Contains("[Interface]", StringComparison.OrdinalIgnoreCase)) throw new FormatException("vpn://: поддерживается Base64-конфигурация WireGuard/AmneziaWG.");
            profile = ParseWireGuard(payload);
        }
        if (name.Length > 0) profile.Name = name;
        return profile;
    }
    private static ServerProfile ParseShadowsocks(string link)
    {
        var body = link[5..];
        var fragment = body.IndexOf('#'); var name = fragment >= 0 ? Uri.UnescapeDataString(body[(fragment + 1)..]) : "Shadowsocks";
        if (fragment >= 0) body = body[..fragment];
        if (!body.Contains('@')) body = Decode64(body);
        var uri = new Uri("ss://" + body);
        if (Query(uri.Query).ContainsKey("plugin")) throw new FormatException("Плагины Shadowsocks не поддерживаются.");
        var auth = Uri.UnescapeDataString(uri.UserInfo);
        if (!auth.Contains(':')) auth = Decode64(auth);
        var split = auth.Split(':', 2);
        if (split.Length != 2 || uri.Port < 1) throw new FormatException();
        return new() { Name = name, Outbound = new JsonObject { ["type"] = "shadowsocks", ["server"] = uri.IdnHost.Trim('[', ']'), ["server_port"] = uri.Port, ["method"] = split[0], ["password"] = split[1] } };
    }
    private static ServerProfile ParseVmess(string body)
    {
        var j = JsonNode.Parse(Decode64(body))!;
        string Read(string key, string fallback = "") => j[key]?.ToString() ?? fallback;
        var o = new JsonObject { ["type"] = "vmess", ["server"] = Read("add"), ["server_port"] = int.Parse(Read("port")), ["uuid"] = Read("id"), ["security"] = Read("scy", "auto"), ["alter_id"] = int.Parse(Read("aid", "0")) };
        if (Read("tls") == "tls") o["tls"] = new JsonObject { ["enabled"] = true, ["server_name"] = Read("sni", Read("add")) };
        ApplyTransport(o, Read("net", "tcp"), Read("path", "/"), Read("host"), Read("path"));
        return new() { Name = Read("ps", "VMess"), Outbound = o };
    }
    private static void ApplyTransport(JsonObject o, string type, string path, string host, string service)
    {
        switch (type)
        {
            case "tcp": case "raw": case "": break;
            case "ws":
                var ws = new JsonObject { ["type"] = "ws", ["path"] = path };
                if (host.Length > 0) ws["headers"] = new JsonObject { ["Host"] = host };
                o["transport"] = ws; break;
            case "grpc": o["transport"] = new JsonObject { ["type"] = "grpc", ["service_name"] = service }; break;
            case "http": case "h2": o["transport"] = new JsonObject { ["type"] = "http", ["path"] = path, ["host"] = Array(host.Split(',', StringSplitOptions.RemoveEmptyEntries)) }; break;
            case "httpupgrade": o["transport"] = new JsonObject { ["type"] = "httpupgrade", ["path"] = path, ["host"] = host }; break;
            default: throw new FormatException("Транспорт не поддерживается.");
        }
    }
    private static Dictionary<string, string> Query(string query) => query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split('=', 2)).GroupBy(x => Uri.UnescapeDataString(x[0])).ToDictionary(g => g.Key, g => g.Last().Length > 1 ? Uri.UnescapeDataString(g.Last()[1]) : "");
    private static string Get(Dictionary<string, string> q, string key, string fallback = "") => q.GetValueOrDefault(key, fallback);
    private static string Name(Uri uri, string type) => uri.Fragment.Length > 1 ? Uri.UnescapeDataString(uri.Fragment[1..]) : $"{type} — {uri.Host}";
    public static JsonArray Array(IEnumerable<string> values) => new(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());
}
