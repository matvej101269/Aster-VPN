using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace AsterVPN;
public static class ConfigBuilder
{
    public static string[] Sites(string input)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in input.Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var host = entry;
            if (host.Contains("://", StringComparison.Ordinal))
            {
                if (!Uri.TryCreate(host, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https") || url.UserInfo.Length > 0)
                    throw new FormatException("Правила сайтов: укажите домен или HTTP/HTTPS-ссылку без логина и пароля.");
                host = url.Host;
            }
            if (host.StartsWith("*.", StringComparison.Ordinal)) host = host[2..];
            host = host.TrimEnd('.');
            try { host = new System.Globalization.IdnMapping().GetAscii(host).ToLowerInvariant(); }
            catch (ArgumentException) { throw new FormatException("Правила сайтов: некорректное доменное имя."); }
            if (host.Length > 253 || !host.Contains('.') || System.Net.IPAddress.TryParse(host, out _) ||
                host.Split('.').Any(label => !Regex.IsMatch(label, "^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$")))
                throw new FormatException("Правила сайтов: нужен домен, например youtube.com. IP-адреса и пути без HTTP/HTTPS не поддерживаются.");
            result.Add(host);
        }
        return result.Order(StringComparer.Ordinal).ToArray();
    }
    public static (string Domain, bool ViaVpn)[] SiteRules(string direct, string vpn)
    {
        var directDomains = Sites(direct);
        var vpnDomains = Sites(vpn);
        if (directDomains.Intersect(vpnDomains, StringComparer.Ordinal).Any())
            throw new FormatException("Один сайт указан и для VPN, и для прямого доступа. Оставьте его в одном списке.");
        // A more specific subdomain can override its parent domain's policy.
        return directDomains.Select(d => (Domain: d, ViaVpn: false)).Concat(vpnDomains.Select(d => (Domain: d, ViaVpn: true)))
            .OrderByDescending(r => r.Domain.Count(c => c == '.')).ThenBy(r => r.Domain, StringComparer.Ordinal).ToArray();
    }
    public static bool RequiresIpv4(ServerProfile server) => server.Protocol is "amneziawg" or "vless";
    public static string[] Countries(string input)
    {
        var codes = input.ToLowerInvariant().Split([',', ' ', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();
        if (codes.Any(c => !Regex.IsMatch(c, "^[a-z]{2}$"))) throw new FormatException("GeoIP: укажите двухбуквенные коды стран, например ru, by, kz.");
        return codes;
    }
    public static JsonObject Build(Settings settings, ServerProfile server, int probePort, string probePassword, string dataDir, int awgPort = 0, string awgPassword = "", string awgPath = "")
    {
        var ipv4Only = RequiresIpv4(server);
        if (settings.SiteMode is < 0 or > 2 || settings.GeoMode is < 0 or > 2) throw new FormatException("Неизвестный режим маршрутизации.");
        // Null keeps the combined domain policy from 1.0.0 until the user saves a mode.
        var sites = settings.SiteMode switch
        {
            0 => [],
            1 => SiteRules("", settings.VpnSites),
            2 => SiteRules(settings.DirectSites, ""),
            _ => SiteRules(settings.DirectSites, settings.VpnSites)
        };
        // The browser sees this MTU when selecting QUIC datagram sizes. A larger
        // TUN than the AWG netstack creates avoidable inner-IP fragmentation.
        var tunMtu = server.Protocol == "amneziawg" ? Math.Clamp(server.Outbound["mtu"]?.GetValue<int>() ?? 1400, 1280, 1400) : 1400;
        var proxy = (JsonObject)server.Outbound.DeepClone(); proxy["tag"] = "proxy";
        if (server.Protocol == "amneziawg" || server.UsesXray)
        {
            if (awgPort < 1) throw new InvalidOperationException("Адаптер AmneziaWG не запущен.");
            proxy = new JsonObject { ["type"] = "socks", ["tag"] = "proxy", ["server"] = "127.0.0.1", ["server_port"] = awgPort, ["version"] = "5", ["username"] = "aster", ["password"] = awgPassword };
        }
        var rules = new JsonArray
        {
            new JsonObject { ["inbound"] = SubscriptionParser.Array(["probe"]), ["action"] = "route", ["outbound"] = "proxy" },
            new JsonObject { ["port"] = 53, ["action"] = "sniff", ["sniffer"] = SubscriptionParser.Array(["dns"]) },
            new JsonObject { ["protocol"] = "dns", ["action"] = "hijack-dns" }
        };
        if (server.Protocol == "amneziawg" || server.UsesXray)
            rules.Insert(1, new JsonObject { ["process_path"] = SubscriptionParser.Array([awgPath]), ["action"] = "route", ["outbound"] = "direct" });
        // Only the controller's explicitly TUN-bound health connection bypasses app filters.
        if (Environment.ProcessPath is { } controller)
            rules.Add(new JsonObject { ["process_path"] = SubscriptionParser.Array([controller]), ["source_ip_cidr"] = SubscriptionParser.Array(["172.29.0.1/32"]), ["action"] = "route", ["outbound"] = "proxy" });
        if (settings.BypassLan) rules.Add(new JsonObject { ["ip_is_private"] = true, ["action"] = "route", ["outbound"] = "direct" });
        if (sites.Length > 0)
        {
            // DNS mapping covers ordinary system DNS. Sniff HTTP/TLS/QUIC as a
            // fallback for browser DoH and already cached addresses, only on web ports.
            rules.Add(new JsonObject { ["port"] = new JsonArray(80, 443), ["action"] = "sniff", ["sniffer"] = SubscriptionParser.Array(["http", "tls", "quic"]), ["timeout"] = "300ms" });
            foreach (var site in sites)
            {
                JsonObject DomainRule(string action) => new() { ["domain_suffix"] = SubscriptionParser.Array([site.Domain]), ["action"] = action };
                if (site.ViaVpn && server.UsesXray)
                {
                    var quic = DomainRule("reject"); quic["network"] = "udp"; quic["port"] = 443; rules.Add(quic);
                }
                if (site.ViaVpn && ipv4Only)
                {
                    var ipv6 = DomainRule("reject"); ipv6["ip_version"] = 6; rules.Add(ipv6);
                }
                var route = DomainRule("route"); route["outbound"] = site.ViaVpn ? "proxy" : "direct"; rules.Add(route);
            }
        }
        if (settings.SiteMode == 1)
        {
            var otherSites = new JsonObject { ["action"] = "route", ["outbound"] = "direct" };
            if (sites.Length > 0) { otherSites["domain_suffix"] = SubscriptionParser.Array(sites.Select(s => s.Domain)); otherSites["invert"] = true; }
            rules.Add(otherSites);
        }
        if (settings.AppMode == 1)
        {
            if (settings.Apps.Count == 0) rules.Add(new JsonObject { ["action"] = "route", ["outbound"] = "direct" });
            else rules.Add(new JsonObject { ["process_path"] = SubscriptionParser.Array(settings.Apps), ["invert"] = true, ["action"] = "route", ["outbound"] = "direct" });
        }
        if (settings.AppMode == 2 && settings.Apps.Count > 0)
            rules.Add(new JsonObject { ["process_path"] = SubscriptionParser.Array(settings.Apps), ["action"] = "route", ["outbound"] = "direct" });
        var sets = new JsonArray();
        var countries = settings.EffectiveGeoMode == 0 ? [] : Countries(settings.DirectCountries);
        foreach (var country in countries)
        {
            sets.Add(new JsonObject { ["type"] = "remote", ["tag"] = $"geoip-{country}", ["format"] = "binary", ["url"] = $"https://raw.githubusercontent.com/SagerNet/sing-geoip/rule-set/geoip-{country}.srs", ["download_detour"] = "direct", ["update_interval"] = "168h" });
            if (settings.EffectiveGeoMode == 2)
                rules.Add(new JsonObject { ["rule_set"] = SubscriptionParser.Array([$"geoip-{country}"]), ["action"] = "route", ["outbound"] = "direct" });
        }
        if (settings.EffectiveGeoMode == 1)
        {
            var otherCountries = new JsonObject { ["action"] = "route", ["outbound"] = "direct" };
            if (countries.Length > 0) { otherCountries["rule_set"] = SubscriptionParser.Array(countries.Select(c => $"geoip-{c}")); otherCountries["invert"] = true; }
            rules.Add(otherCountries);
        }
        // Reject QUIC only on the XHTTP path, after all direct exceptions.
        // Immediate ICMP rejection lets browsers select HTTP/2 without a UDP timeout.
        if (server.UsesXray)
            rules.Add(new JsonObject { ["network"] = "udp", ["port"] = 443, ["action"] = "reject" });
        // Keep IPv6 inside TUN, but fail unsupported destinations immediately so apps
        // can try IPv4 instead of forwarding them to an IPv4-only SOCKS bridge.
        if (ipv4Only) rules.Add(new JsonObject { ["ip_version"] = 6, ["action"] = "reject" });
        return new JsonObject
        {
            ["log"] = new JsonObject { ["level"] = "warn", ["timestamp"] = true },
            ["dns"] = new JsonObject
            {
                ["servers"] = new JsonArray
                {
                    settings.KillSwitch
                        ? new JsonObject { ["type"] = "https", ["tag"] = "bootstrap", ["server"] = "1.1.1.1", ["server_port"] = 443, ["path"] = "/dns-query", ["detour"] = "direct", ["tls"] = new JsonObject { ["enabled"] = true, ["server_name"] = "cloudflare-dns.com" } }
                        : new JsonObject { ["type"] = "local", ["tag"] = "bootstrap" },
                    new JsonObject { ["type"] = "https", ["tag"] = "secure", ["server"] = "1.1.1.1", ["server_port"] = 443, ["path"] = "/dns-query", ["detour"] = "proxy", ["tls"] = new JsonObject { ["enabled"] = true, ["server_name"] = "cloudflare-dns.com" } },
                    new JsonObject { ["type"] = "https", ["tag"] = "secure-backup", ["server"] = "8.8.8.8", ["server_port"] = 443, ["path"] = "/dns-query", ["detour"] = "proxy", ["tls"] = new JsonObject { ["enabled"] = true, ["server_name"] = "dns.google" } }
                },
                ["rules"] = new JsonArray
                {
                    new JsonObject { ["action"] = "evaluate", ["server"] = "secure", ["tag"] = "primary", ["timeout"] = "5s" },
                    new JsonObject { ["action"] = "evaluate", ["server"] = "secure-backup", ["tag"] = "backup", ["timeout"] = "5s" },
                    new JsonObject { ["action"] = "respond", ["match_response"] = "primary", ["response_rcode"] = "NOERROR", ["race"] = true },
                    new JsonObject { ["action"] = "respond", ["match_response"] = "primary", ["response_rcode"] = "NXDOMAIN", ["race"] = true },
                    new JsonObject { ["action"] = "respond", ["match_response"] = "backup", ["response_rcode"] = "NOERROR", ["race"] = true },
                    new JsonObject { ["action"] = "respond", ["match_response"] = "backup", ["response_rcode"] = "NXDOMAIN", ["race"] = true },
                    new JsonObject { ["action"] = "respond", ["match_response"] = "backup" }
                },
                ["final"] = "secure", ["strategy"] = ipv4Only ? "ipv4_only" : "prefer_ipv4", ["reverse_mapping"] = sites.Length > 0
            },
            ["inbounds"] = new JsonArray
            {
                new JsonObject { ["type"] = "tun", ["tag"] = "tun", ["interface_name"] = "AsterVPN", ["address"] = SubscriptionParser.Array(["172.29.0.1/30", "fdfe:dcba:9876::1/126"]), ["mtu"] = tunMtu, ["auto_route"] = true, ["strict_route"] = true, ["stack"] = "mixed", ["route_exclude_address"] = settings.BypassLan ? SubscriptionParser.Array(["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "fc00::/7", "fe80::/10"]) : new JsonArray() },
                new JsonObject { ["type"] = "mixed", ["tag"] = "probe", ["listen"] = "127.0.0.1", ["listen_port"] = probePort, ["users"] = new JsonArray(new JsonObject { ["username"] = "health", ["password"] = probePassword }) }
            },
            ["outbounds"] = server.IsEndpoint ? new JsonArray(new JsonObject { ["type"] = "direct", ["tag"] = "direct" }) : new JsonArray(proxy, new JsonObject { ["type"] = "direct", ["tag"] = "direct" }),
            ["endpoints"] = server.IsEndpoint ? new JsonArray(proxy) : new JsonArray(),
            ["route"] = new JsonObject { ["auto_detect_interface"] = true, ["default_domain_resolver"] = "bootstrap", ["rules"] = rules, ["rule_set"] = sets, ["final"] = "proxy" },
            ["experimental"] = new JsonObject { ["cache_file"] = new JsonObject { ["enabled"] = true, ["path"] = System.IO.Path.Combine(dataDir, "cache.db") } }
        };
    }
}

