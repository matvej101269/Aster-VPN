using AsterVPN;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

if (args.Contains("--updates-only")) { await UpdateDownloadChecks.RunAsync(); return; }

if (RecoveryPolicy.RetryDelaySeconds(1) != 15 || RecoveryPolicy.RetryDelaySeconds(2) != 30 ||
    RecoveryPolicy.RetryDelaySeconds(6) != 300 || RecoveryPolicy.RetryDelaySeconds(1000000) != 300)
    throw new Exception("Recovery backoff must stay bounded during a prolonged outage.");

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
var core = Path.Combine(root, "vendor/sing-box-1.14.3-windows-amd64/sing-box.exe");
if (args.Contains("--wfp-only"))
{
    using var protection = new KillSwitch();
    foreach (var bypassLan in new[] { false, true }) protection.Enable(core, bypassLan, validateOnly: true);
    dynamic scheduler = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
    try
    {
        scheduler.Connect();
        dynamic folder = scheduler.GetFolder(@"\");
        try
        {
            var xml = WindowsStartup.BuildXml(Path.Combine(root, "dist/AsterVPN/AsterVPN.exe"), System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value);
            // TASK_VALIDATE_ONLY: Windows validates the XML without creating a task.
            folder.RegisterTask("AsterVPN-Validation", xml, 1, null, null, 3, null);
        }
        finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(folder); }
    }
    finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(scheduler); }
    Console.WriteLine("WFP rules accepted (rolled back); startup task XML accepted (validation only).");
    return;
}
var output = Path.Combine(root, "tests/generated"); Directory.CreateDirectory(output);
var passed = 0;
void Assert(bool value, string label) { if (!value) throw new Exception("FAIL " + label); Console.WriteLine("PASS " + label); passed++; }
void Reject(string value, string label) { try { SubscriptionParser.Parse(value); } catch { Assert(true, label); return; } throw new Exception("FAIL " + label); }
string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
const string uuid = "f1f1408b-959e-4c09-bb5b-981c8ba33101";
var inputs = new[]
{
    $"vless://{uuid}@example.com:443?security=tls&sni=example.com&type=ws&path=%2Fproxy&host=example.com#VLESS",
    "trojan://test-password@example.com:443?sni=example.com#Trojan",
    "ss://" + B64("aes-128-gcm:test-password") + "@example.com:8388#SS",
    "ss://" + B64("aes-128-gcm:test-password@example.com:8388") + "#Legacy",
    "vmess://" + B64("{\"v\":\"2\",\"ps\":\"VMess\",\"add\":\"example.com\",\"port\":\"443\",\"id\":\"" + uuid + "\",\"aid\":\"0\",\"net\":\"ws\",\"tls\":\"tls\"}"),
    "hysteria2://test-password@example.com:443?sni=example.com#HY2",
    $"tuic://{uuid}:test-password@example.com:443?sni=example.com#TUIC",
    "socks5://user:pass@127.0.0.1:1080#SOCKS",
    "https://user:pass@example.com:443#HTTP",
    "anytls://test-password@example.com:443#AnyTLS",
    $"vless://{uuid}@example.com:443?security=reality&pbk=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA&sid=01234567&fp=chrome#Reality",
    "[Interface]\nPrivateKey = AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=\nAddress = 10.8.0.2/32\n[Peer]\nPublicKey = AQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQE=\nEndpoint = example.com:51820\nAllowedIPs = 0.0.0.0/0, ::/0\nPersistentKeepalive = 25"
};
var profiles = inputs.Select(s => SubscriptionParser.Parse(s).Single()).ToList();
var startupConfig = ConfigBuilder.Build(new Settings(), profiles[0], 18991, "test-secret", output);
startupConfig["inbounds"]![0]!["route_exclude_address"]!.AsArray().Add("192.0.2.1/32");
Assert(JsonNode.Parse(startupConfig.ToJsonString(SecureStore.JsonOptions)) != null, "Runtime config serializes with controller options and appended AWG exclusion");
Assert(profiles.Count == 12, "12 protocol/URI variants parsed");
Assert(SubscriptionParser.Parse(B64(string.Join('\n', inputs.Take(3)))).Count == 3, "Base64 subscription");
Assert(profiles[0].Outbound["transport"]!["path"]!.ToString() == "/proxy", "URI escaping");
Assert(profiles[1].Outbound["tls"]!["enabled"]!.GetValue<bool>(), "Trojan TLS default");
Reject("vless://bad@example.com:443?type=unknown", "Unsupported transport fails explicitly");
Reject("trojan://pass@example.com:443?insecure=1", "Insecure TLS rejected");
Reject("{\"outbounds\":[{\"type\":\"ssh\",\"private_key_path\":\"C:/secret\"}]}", "Local key path rejected");
Reject("ss://" + B64("aes-128-gcm:pass") + "@example.com:8388?plugin=evil", "SS plugins rejected");
Reject("[Interface]\nPostUp=evil\n[Peer]\n", "WireGuard hooks rejected");
Reject("{\"outbounds\":[{\"type\":\"direct\"}]}", "Empty subscriptions rejected");
foreach (var (profile, index) in profiles.Select((p, i) => (p, i)))
{
    var settings = new Settings { AppMode = index % 3, Apps = [@"C:\Program Files\Browser\browser.exe"], DirectCountries = "ru, kz" };
    var config = ConfigBuilder.Build(settings, profile, 18991, "test-secret", output);
    var path = Path.Combine(output, $"profile-{index}.json"); File.WriteAllText(path, config.ToJsonString());
    var info = new ProcessStartInfo(core) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
    info.ArgumentList.Add("check"); info.ArgumentList.Add("-c"); info.ArgumentList.Add(path);
    using var process = Process.Start(info)!; var error = await process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync();
    Assert(process.ExitCode == 0, $"sing-box check {profile.Protocol}/{index}: {error}");
}
foreach (var mode in new[] { 0, 1, 2 })
{
    var settings = new Settings { AppMode = mode, Apps = [@"C:\browser.exe"], DirectCountries = "ru" };
    var config = ConfigBuilder.Build(settings, profiles[0], 18000, "secret", output);
    var rules = (JsonArray)config["route"]!["rules"]!;
    var app = rules.OfType<JsonObject>().FirstOrDefault(r => r.ContainsKey("process_path") && !r.ContainsKey("source_ip_cidr"));
    Assert(mode == 0 ? app == null : app != null && (mode == 1 ? app["invert"]!.GetValue<bool>() : app["invert"] == null), $"Application policy mode {mode}");
    Assert(rules[0]!["inbound"]![0]!.ToString() == "probe", $"Health probe always uses VPN {mode}");
    Assert(rules.OfType<JsonObject>().Any(r => r["source_ip_cidr"]?[0]?.ToString() == "172.29.0.1/32" && r["process_path"]?[0]?.ToString() == Environment.ProcessPath && r["outbound"]?.ToString() == "proxy"), $"TUN health probe is scoped to controller {mode}");
}
var empty = ConfigBuilder.Build(new Settings { AppMode = 1 }, profiles[0], 18000, "secret", output);
Assert(ConfigBuilder.Sites("HTTPS://YouTube.com/watch?v=1, *.GoogleVideo.com; ПРИМЕР.РФ\nyoutube.com.")
    .SequenceEqual(new[] { "googlevideo.com", "xn--e1afmkfd.xn--p1ai", "youtube.com" }), "Site rules normalize URLs, wildcards, IDN and duplicates");
foreach (var invalidSite in new[] { "127.0.0.1", "localhost", "example.com/path", "example.com:443", "https://user:pass@example.com", "a..com", "-a.com", "ftp://example.com", "*" })
{
    try { ConfigBuilder.Sites(invalidSite); Assert(false, "Invalid site accepted: " + invalidSite); }
    catch (FormatException) { Assert(true, "Invalid site rejected: " + invalidSite); }
}
try { ConfigBuilder.SiteRules("example.com", "https://EXAMPLE.com/path"); Assert(false, "Conflicting site policy accepted"); }
catch (FormatException) { Assert(true, "Conflicting site destinations rejected after normalization"); }
var siteSettings = new Settings { AppMode = 1, DirectCountries = "ru", DirectSites = "example.com", VpnSites = "video.example.com\nyoutube.com" };
var sitesConfig = ConfigBuilder.Build(siteSettings, profiles[0], 18000, "secret", output);
var siteRules = sitesConfig["route"]!["rules"]!.AsArray();
var childIndex = siteRules.ToList().FindIndex(r => r?["domain_suffix"]?[0]?.ToString() == "video.example.com" && r?["action"]?.ToString() == "route");
var parentIndex = siteRules.ToList().FindIndex(r => r?["domain_suffix"]?[0]?.ToString() == "example.com");
var appIndex = siteRules.ToList().FindIndex(r => r?.AsObject().Count == 2 && r?["outbound"]?.ToString() == "direct");
Assert(childIndex >= 0 && childIndex < parentIndex && parentIndex < appIndex, "Specific subdomains override parents and site rules precede app policy");
Assert(sitesConfig["dns"]!["reverse_mapping"]!.GetValue<bool>() && siteRules.Any(r => r?["action"]?.ToString() == "sniff" && r?["timeout"]?.ToString() == "300ms"), "Site routing enables DNS mapping and bounded web sniffing");
var savedSites = System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(siteSettings))!;
Assert(savedSites.DirectSites == siteSettings.DirectSites && savedSites.VpnSites == siteSettings.VpnSites, "Site settings survive save/load");
Assert(System.Text.Json.JsonSerializer.Deserialize<Settings>("{}")!.VpnSites == "", "Older settings load with empty site rules");
foreach (var mode in new[] { 0, 1, 2 })
{
    var modeSettings = new Settings { SiteMode = mode, GeoMode = mode, VpnSites = "youtube.com", DirectSites = "example.com", DirectCountries = "ru, kz" };
    var modeConfig = ConfigBuilder.Build(modeSettings, profiles[0], 18000, "secret", output);
    var modeRules = modeConfig["route"]!["rules"]!.AsArray();
    var domainRoutes = modeRules.Where(r => r?["domain_suffix"] != null && r?["action"]?.ToString() == "route").ToArray();
    Assert(mode switch
    {
        0 => domainRoutes.Length == 0 && !modeConfig["dns"]!["reverse_mapping"]!.GetValue<bool>(),
        1 => domainRoutes.Any(r => r?["outbound"]?.ToString() == "proxy" && r?["domain_suffix"]?[0]?.ToString() == "youtube.com") && domainRoutes.Any(r => r?["invert"]?.ToString() == "true" && r?["outbound"]?.ToString() == "direct"),
        _ => domainRoutes.Length == 1 && domainRoutes[0]?["domain_suffix"]?[0]?.ToString() == "example.com" && domainRoutes[0]?["outbound"]?.ToString() == "direct"
    }, $"Site dropdown mode {mode} applies only its active list");
    var geoRules = modeRules.Where(r => r?["rule_set"] != null).ToArray();
    Assert(mode switch
    {
        0 => geoRules.Length == 0 && modeConfig["route"]!["rule_set"]!.AsArray().Count == 0,
        1 => geoRules.Length == 1 && geoRules[0]?["invert"]?.ToString() == "true" && geoRules[0]?["rule_set"]?.AsArray().Count == 2,
        _ => geoRules.Length == 2 && geoRules.All(r => r?["invert"] == null && r?["outbound"]?.ToString() == "direct")
    }, $"GeoIP dropdown mode {mode} routes the selected countries correctly");
    var modePath = Path.Combine(output, $"routing-mode-{mode}.json"); File.WriteAllText(modePath, modeConfig.ToJsonString());
    var modeCheck = new ProcessStartInfo(core) { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
    modeCheck.ArgumentList.Add("check"); modeCheck.ArgumentList.Add("-c"); modeCheck.ArgumentList.Add(modePath);
    using var checkedMode = Process.Start(modeCheck)!; var modeError = await checkedMode.StandardError.ReadToEndAsync(); await checkedMode.WaitForExitAsync();
    Assert(checkedMode.ExitCode == 0, $"sing-box accepts site/GeoIP mode {mode}: {modeError}");
    var restoredMode = System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(modeSettings))!;
    Assert(restoredMode.SiteMode == mode && restoredMode.GeoMode == mode && restoredMode.DirectSites == "example.com" && restoredMode.VpnSites == "youtube.com", $"Mode {mode} and inactive site list survive save/load");
}
var emptySiteMode = ConfigBuilder.Build(new Settings { SiteMode = 1 }, profiles[0], 18000, "secret", output);
Assert(emptySiteMode["route"]!["rules"]!.AsArray().Any(r => r?.AsObject().Count == 2 && r?["outbound"]?.ToString() == "direct"), "Empty VPN-only site list routes other traffic directly");
var emptyGeoMode = ConfigBuilder.Build(new Settings { GeoMode = 1 }, profiles[0], 18000, "secret", output);
Assert(emptyGeoMode["route"]!["rules"]!.AsArray().Any(r => r?.AsObject().Count == 2 && r?["outbound"]?.ToString() == "direct"), "Empty VPN-only country list routes other traffic directly");
Assert(new Settings { DirectCountries = "ru" }.EffectiveGeoMode == 2 && new Settings().EffectiveGeoMode == 0, "Legacy GeoIP settings retain bypass semantics");
Assert(new Settings { VpnSites = "youtube.com" }.EffectiveSiteMode == 1 && new Settings { DirectSites = "example.com" }.EffectiveSiteMode == 2, "Legacy site lists select the corresponding dropdown mode");
var sitesPath = Path.Combine(output, "site-routing.json"); File.WriteAllText(sitesPath, sitesConfig.ToJsonString());
var sitesCheck = new ProcessStartInfo(core) { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
sitesCheck.ArgumentList.Add("check"); sitesCheck.ArgumentList.Add("-c"); sitesCheck.ArgumentList.Add(sitesPath);
using (var check = Process.Start(sitesCheck)!) { var error = await check.StandardError.ReadToEndAsync(); await check.WaitForExitAsync(); Assert(check.ExitCode == 0, "sing-box accepts site routing and parallel encrypted DNS: " + error); }
Assert(empty["route"]!["rules"]!.AsArray().Any(r => r!.AsObject().Count == 2 && r["outbound"]?.ToString() == "direct"), "Empty include-list routes directly");
Assert(profiles.All(p => p.Outbound["tag"] == null), "Config build does not mutate profiles");
var lanBypass = ConfigBuilder.Build(new Settings { BypassLan = true }, profiles[0], 18000, "secret", output);
Assert(lanBypass["inbounds"]![0]!["route_exclude_address"]!.AsArray().Any(n => n!.ToString() == "192.168.0.0/16"), "LAN bypass uses native Windows routing outside TUN");
var lanCaptured = ConfigBuilder.Build(new Settings { BypassLan = false }, profiles[0], 18000, "secret", output);
Assert(lanCaptured["inbounds"]![0]!["route_exclude_address"]!.AsArray().Count == 0, "Disabling LAN bypass keeps private destinations in TUN");
var awgConf = inputs[11].Replace("[Peer]", "Jc = 2\nJmin = 20\nJmax = 40\nS1 = 16\nS2 = 32\nH1 = 101\nH2 = 102\nH3 = 103\nH4 = 104\n[Peer]");
var namedAwg = SubscriptionParser.Parse("vpn://" + B64(awgConf + "\n# Name=🇩🇪Германия — #2\n")).Single();
Assert(namedAwg.DisplayName == "Германия — #2" && namedAwg.Country == "DE", "AWG subscription comment preserves name and flag");
var mixed = SubscriptionParser.Parse(B64(inputs[0] + "\nvpn://" + B64(awgConf) + "#AWG\nawg://" + B64(awgConf) + "#AWG2"));
Assert(mixed.Count == 3 && mixed[1].Protocol == "amneziawg" && mixed[2].Protocol == "amneziawg", "Mixed Base64 subscription VLESS + vpn:// + awg://");
var awgJson = new JsonObject { ["type"] = "amneziawg", ["servers"] = new JsonArray(new JsonObject { ["name"] = "AWG JSON", ["config"] = B64(awgConf) }) };
Assert(SubscriptionParser.Parse(awgJson.ToJsonString()).Single().Protocol == "amneziawg", "AWG JSON subscription container");
var xhttp = SubscriptionParser.Parse($"vless://{uuid}@example.com:443?type=xhttp&security=tls&path=%2Ftest#XHTTP").Single();
Assert(xhttp.UsesXray && xhttp.Outbound["xray"]!["streamSettings"]!["network"]!.ToString() == "xhttp", "XHTTP selects Xray without losing transport");
var boundXray = XrayConfig.Build(xhttp, 18002, "test-password");
var staleVless = SubscriptionParser.Parse($"vless://{uuid}@example.com:443?type=xhttp&security=reality&sni=old.example&pbk=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA&sid=01234567#Rotating").Single();
var freshVless = SubscriptionParser.Parse($"vless://{uuid}@example.com:443?type=xhttp&security=reality&sni=new.example&pbk=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA&sid=89abcdef#Rotating").Single();
var rotatingSettings = new Settings { Subscriptions = [new() { Url = "https://example.com/sub", Servers = [staleVless, mixed[1]] }] };
var refreshCalls = 0;
Task<SubscriptionProfile> FetchRotated(string address, CancellationToken ct) { refreshCalls++; ct.ThrowIfCancellationRequested(); return Task.FromResult(new SubscriptionProfile { Servers = [freshVless] }); }
var refreshedVless = await SubscriptionService.RefreshConnectionAsync(rotatingSettings, staleVless, fetch: FetchRotated);
Assert(refreshedVless.Id == staleVless.Id && refreshedVless.Outbound["xray"]!["streamSettings"]!["realitySettings"]!["serverName"]!.ToString() == "new.example" && refreshedVless.Outbound["xray"]!["streamSettings"]!["realitySettings"]!["shortId"]!.ToString() == "89abcdef", "VLESS refresh replaces rotated REALITY SNI/shortId and preserves server identity");
Assert(staleVless.Outbound["xray"]!["streamSettings"]!["realitySettings"]!["serverName"]!.ToString() == "old.example", "Connection refresh does not mutate the running profile");
await SubscriptionService.RefreshConnectionAsync(rotatingSettings, mixed[1], fetch: FetchRotated);
Assert(refreshCalls == 1, "AWG connection does not fetch or change subscription parameters");
rotatingSettings.AutoUpdate = false;
Assert(ReferenceEquals(await SubscriptionService.RefreshConnectionAsync(rotatingSettings, staleVless, fetch: FetchRotated), staleVless) && refreshCalls == 1, "Disabled auto-update preserves manual subscription policy");
rotatingSettings.AutoUpdate = true;
try { await SubscriptionService.RefreshConnectionAsync(rotatingSettings, staleVless, fetch: (_, _) => Task.FromResult(new SubscriptionProfile { Servers = [xhttp] })); throw new Exception("Removed VLESS server silently replaced"); }
catch (IOException) { Assert(true, "Removed VLESS server cannot silently switch to another endpoint"); }
using (var refreshCancelled = new CancellationTokenSource())
{
    refreshCancelled.Cancel();
    try { await SubscriptionService.RefreshConnectionAsync(rotatingSettings, staleVless, refreshCancelled.Token, FetchRotated); throw new Exception("Cancelled VLESS refresh completed"); }
    catch (OperationCanceledException) { Assert(true, "VLESS subscription refresh supports connection cancellation"); }
}
Assert(boundXray["outbounds"]![0]!["streamSettings"]!["xhttpSettings"]!["extra"]?["xmux"] == null, "XHTTP uses native core defaults when provider specifies no XMUX");
var cachedFallbacks = 0;
var cachedRecovery = await SubscriptionService.RefreshForRecoveryAsync(rotatingSettings, staleVless,
    fetch: (_, _) => throw new System.Net.Http.HttpRequestException("subscription API offline"), onCachedFallback: () => cachedFallbacks++);
Assert(ReferenceEquals(cachedRecovery, staleVless) && cachedFallbacks == 1, "Subscription outage cannot prevent cached VLESS recovery");
cachedRecovery = await SubscriptionService.RefreshForRecoveryAsync(rotatingSettings, staleVless,
    fetch: async (_, token) => { await Task.Delay(Timeout.Infinite, token); return new(); }, timeout: TimeSpan.FromMilliseconds(30));
Assert(ReferenceEquals(cachedRecovery, staleVless), "Stalled subscription refresh has a bounded recovery budget");
var freshRecovery = await SubscriptionService.RefreshForRecoveryAsync(rotatingSettings, staleVless, fetch: FetchRotated);
Assert(freshRecovery.Outbound["xray"]!["streamSettings"]!["realitySettings"]!["serverName"]!.ToString() == "new.example", "Recovery still applies rotated credentials when API is reachable");
try
{
    await SubscriptionService.RefreshForRecoveryAsync(rotatingSettings, staleVless, fetch: (_, _) => Task.FromResult(new SubscriptionProfile { Servers = [xhttp] }));
    Assert(false, "Removed recovery profile must fail");
}
catch (ConnectionProfileUnavailableException) { Assert(true, "Recovery does not revive an explicitly removed profile"); }
using (var recoveryCanceled = new CancellationTokenSource())
{
    try
    {
        await SubscriptionService.RefreshForRecoveryAsync(rotatingSettings, staleVless, recoveryCanceled.Token,
            fetch: async (_, token) => { recoveryCanceled.Cancel(); await Task.Delay(Timeout.Infinite, token); return new(); });
        Assert(false, "User cancellation must not reconnect a cached profile");
    }
    catch (OperationCanceledException) { Assert(true, "Recovery preserves user cancellation instead of using cached credentials"); }
}
var providerXmux = SubscriptionParser.Parse($"vless://{uuid}@example.com:443?type=xhttp&security=tls&extra=" + Uri.EscapeDataString("{\"xmux\":{\"maxConcurrency\":2}}") + "#Custom").Single();
Assert(XrayConfig.Build(providerXmux, 18002, "test-password")["outbounds"]![0]!["streamSettings"]!["xhttpSettings"]!["extra"]!["xmux"]!["maxConcurrency"]!.GetValue<int>() == 2, "Explicit provider XMUX policy is preserved");
XrayConfig.BindTransport(boundXray, "192.0.2.1", "Ethernet test", "192.0.2.2");
Assert(boundXray["outbounds"]![0]!["streamSettings"]!["sockopt"]!["interface"]!.ToString() == "Ethernet test", "Xray pins transport to pre-TUN interface");
Assert(boundXray["outbounds"]![0]!["settings"]!["vnext"]![0]!["address"]!.ToString() == "192.0.2.1" && boundXray["outbounds"]![0]!["streamSettings"]!["tlsSettings"]!["serverName"]!.ToString() == "example.com", "Xray resolved transport preserves TLS server name");
Assert(xhttp.Outbound["xray"]!["streamSettings"]!["sockopt"] == null && xhttp.Host == "example.com", "Xray runtime binding does not mutate stored profile");
var bridge = ConfigBuilder.Build(new Settings { AppMode = 1 }, mixed[1], 18000, "test", output, 18001, "bridge-password", @"C:\aster-awg.exe");
Assert(bridge["route"]!["rules"]![1]!["process_path"]![0]!.ToString() == @"C:\aster-awg.exe", "AWG transport bypass precedes app filters");
var smallMtuAwg = SubscriptionParser.Parse(awgConf.Replace("Address =", "MTU = 1320\nAddress =")).Single();
var awgIpv4 = ConfigBuilder.Build(new Settings(), smallMtuAwg, 18000, "test", output, 18001, "bridge-password", @"C:\aster-awg.exe");
Assert(awgIpv4["inbounds"]![0]!["mtu"]!.GetValue<int>() == 1320, "TUN MTU follows smaller AWG MTU for QUIC datagrams");
Assert(awgIpv4["dns"]!["strategy"]!.ToString() == "ipv4_only", "AWG DNS returns IPv4 addresses");
Assert(awgIpv4["route"]!["rules"]!.AsArray().Any(r => r?["ip_version"]?.ToString() == "6" && r["action"]?.ToString() == "reject"), "AWG rejects cached IPv6 destinations before SOCKS");
Assert(awgIpv4["inbounds"]![0]!["address"]!.AsArray().Any(a => a!.ToString().Contains(':')), "IPv6 stays captured by TUN, not leaked through removal of IPv6 routes");
var awgCheckPath = Path.Combine(output, "awg-ipv4.json");
File.WriteAllText(awgCheckPath, awgIpv4.ToJsonString());
var awgCheckInfo = new ProcessStartInfo(core) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
awgCheckInfo.ArgumentList.Add("check"); awgCheckInfo.ArgumentList.Add("-c"); awgCheckInfo.ArgumentList.Add(awgCheckPath);
using (var check = Process.Start(awgCheckInfo)!) { var error = await check.StandardError.ReadToEndAsync(); await check.WaitForExitAsync(); Assert(check.ExitCode == 0, "sing-box accepts AWG IPv4 policy: " + error); }
Assert(SubscriptionParser.Parse(B64(inputs[0] + "\ntg://proxy?server=example.com&port=443&secret=test\nvpn://" + B64(awgConf))).Count == 2, "Telegram proxy ignored in mixed VLESS + AWG subscription");
Reject("tg://proxy?server=example.com&port=443&secret=test", "Telegram-only input cannot replace VPN subscription");
Assert(SubscriptionParser.Parse("# Name=AWG comment\n" + awgConf).Single().Name == "AWG comment", "WireGuard comments before Interface supported");
var nested = inputs[0]; for (var n = 0; n < 10; n++) nested = B64(nested);
Reject(nested, "Excessive subscription nesting rejected");
var headers = new Dictionary<string, string> { ["Profile-Title"] = "base64:" + B64("Моя подписка"), ["Subscription-Userinfo"] = "upload=1073741824; download=2147483648; total=10737418240; expire=0", ["Profile-Update-Interval"] = "12" };
var metadata = SubscriptionService.ReadMetadata(k => headers.GetValueOrDefault(k));
Assert(metadata.Name == "Моя подписка" && metadata.UpdateIntervalHours == 12 && metadata.TrafficPercent == 30 && metadata.ExpiryText == "Без срока", "3x-ui title, traffic, expiry and update interval");
var missing = SubscriptionService.ReadMetadata(_ => null);
Assert(missing.Total == null && missing.Expire == null && missing.TrafficText.Contains("не указан"), "Missing metadata is unknown, not unlimited");
var invalid = SubscriptionService.ReadMetadata(k => k == "Subscription-Userinfo" ? "upload=-1; total=0; expire=9223372036854775807" : k == "Profile-Update-Interval" ? "0" : null);
Assert(invalid.Upload == null && invalid.Expire == null && invalid.Total == 0 && invalid.UpdateIntervalHours == 1, "Malformed metadata is bounded safely");
Assert(SubscriptionService.NormalizeAddress("[link](https://example.com/sub)") == "https://example.com/sub", "Markdown subscription URL accepted");
var legacy = new Settings { Subscription = "https://example.com/old", Servers = mixed, Selected = 1 };
legacy.MigrateSubscriptions(); legacy.MigrateSubscriptions();
Assert(legacy.Subscriptions.Count == 1 && legacy.AllServers.Count == 3 && legacy.SelectedServerId == mixed[1].Id && legacy.Servers.Count == 0, "Legacy migration preserves selected server without duplicates");
legacy.Subscriptions[0].IsExpanded = false;
legacy.Subscriptions.Add(new() { Name = "Second", Url = "https://example.com/new", Servers = [profiles[0]] });
var roundtrip = System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(legacy))!;
roundtrip.MigrateSubscriptions();
Assert(roundtrip.Subscriptions.Count == 2 && roundtrip.AllServers.Count == 4 && !roundtrip.Subscriptions[0].IsExpanded && roundtrip.SelectedServerId == mixed[1].Id, "Multiple subscriptions and collapse state survive restart");
Assert(PhysicalUplink.IsPhysical(System.Net.NetworkInformation.NetworkInterfaceType.Ethernet, "Ethernet 2", "Realtek Controller"), "Physical Ethernet accepted");
Assert(PhysicalUplink.IsPhysical(System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211, "Wi-Fi", "Intel Wireless"), "Physical Wi-Fi accepted");
Assert(!PhysicalUplink.IsPhysical(System.Net.NetworkInformation.NetworkInterfaceType.Ethernet, "happ-xray", "Happ Tunnel"), "Happ route is never selected as physical uplink");
Assert(!PhysicalUplink.IsPhysical(System.Net.NetworkInformation.NetworkInterfaceType.Ethernet, "AmneziaVPN", "WireGuard Tunnel"), "WireGuard route is never selected as physical uplink");
Assert(DeviceIdentity.Derive(" TEST ") == DeviceIdentity.Derive("test") && DeviceIdentity.Derive("test") != DeviceIdentity.Derive("other"), "HWID stable and device-specific");
var alpnProfile = SubscriptionParser.Parse($"vless://{uuid}@example.com:443?type=xhttp&security=tls&alpn=h2%2Chttp%2F1.1").Single();
Assert(alpnProfile.Outbound["xray"]!["streamSettings"]!["tlsSettings"]!["alpn"]!.AsArray().Count == 2, "XHTTP preserves subscription ALPN");
var sniffRule = awgIpv4["route"]!["rules"]!.AsArray().Single(r => r?["action"]?.ToString() == "sniff")!;
Assert(sniffRule["port"]!.GetValue<int>() == 53 && sniffRule["sniffer"]![0]!.ToString() == "dns", "Sniff only DNS, no protocol detection delay on media UDP");
Assert(Guid.TryParse(DeviceIdentity.Derive("test"), out _), "HWID UUID format");
using (var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, "https://example.com/sub"))
{
    DeviceIdentity.AddHeaders(request, DeviceIdentity.Derive("test"));
    Assert(request.Headers.GetValues("x-hwid").Single() == DeviceIdentity.Derive("test") && request.Headers.GetValues("x-device-os").Single() == "Windows", "Subscription HWID and platform headers");
}
Assert(boundXray["outbounds"]![0]!["mux"] == null, "XHTTP has no additional outbound mux layer");
var xrayPath = Path.Combine(output, "xudp.json");
File.WriteAllText(xrayPath, boundXray.ToJsonString());
var xrayCheck = new ProcessStartInfo(Path.GetFullPath($"vendor/xray-{File.ReadAllText("scripts/xray-version.txt").Trim()}/xray.exe")) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
xrayCheck.ArgumentList.Add("run"); xrayCheck.ArgumentList.Add("-test"); xrayCheck.ArgumentList.Add("-c"); xrayCheck.ArgumentList.Add(xrayPath);
using (var check = Process.Start(xrayCheck)!) { var error = await check.StandardError.ReadToEndAsync(); await check.WaitForExitAsync(); Assert(check.ExitCode == 0, "Packaged Xray accepts XUDP config: " + error); }
foreach (var mode in new[] { "packet-up", "stream-up", "stream-one", "auto" })
{
    var extra = Uri.EscapeDataString("{\"mode\":\"" + mode + "\",\"xPaddingBytes\":\"100-1000\"}");
    var modeProfile = SubscriptionParser.Parse($"vless://{uuid}@example.com:443?type=xhttp&security=tls&mode={mode}&extra={extra}#Mode").Single();
    var original = modeProfile.Outbound.ToJsonString();
    var modeConfig = XrayConfig.Build(modeProfile, 18002, "test-password");
    var modeTransport = modeConfig["outbounds"]![0]!["streamSettings"]!["xhttpSettings"]!;
    Assert(modeTransport["mode"]!.ToString() == mode && modeTransport["extra"]!["mode"]!.ToString() == mode && modeProfile.Outbound.ToJsonString() == original, $"XHTTP preserves {mode} in URI and extra without mutating provider profile");
    var modePath = Path.Combine(output, $"xhttp-mode-{mode}.json");
    File.WriteAllText(modePath, modeConfig.ToJsonString());
    var modeCheck = new ProcessStartInfo(xrayCheck.FileName) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
    foreach (var argument in new[] { "run", "-test", "-c", modePath }) modeCheck.ArgumentList.Add(argument);
    using var accepted = Process.Start(modeCheck)!;
    var modeError = await accepted.StandardError.ReadToEndAsync(); await accepted.WaitForExitAsync();
    Assert(accepted.ExitCode == 0, $"Packaged Xray accepts {mode}: {modeError}");
}
var nullExtra = $"vless://{uuid}@example.com:443?type=xhttp&security=tls&extra=null";
foreach (var (name, country) in new[] { ("🇷🇺 VK — #1", "RU"), ("🇧🇬 Bulgaria", "BG"), ("🇪🇪 Estonia", "EE"), ("🇺🇸 US", "US") })
    Assert(new ServerProfile { Name = name }.Country == country, "Country decoded from regional indicators: " + country);
Assert(new ServerProfile { Name = "🇷🇺 VK" }.FlagMiddle == "#0039A6", "Russian flag is white blue red");
var ipv4Vless = ConfigBuilder.Build(new Settings(), profiles[0], 18000, "secret", output);
Assert(ipv4Vless["dns"]!["strategy"]!.ToString() == "ipv4_only" && ipv4Vless["route"]!["rules"]!.AsArray().Any(r => r?["ip_version"]?.ToString() == "6" && r["action"]?.ToString() == "reject"), "VLESS IPv4 policy captures and rejects cached IPv6");
XrayConfig.BindTransport(boundXray, "192.0.2.1", "Ethernet test", "192.0.2.2");
Assert(boundXray["outbounds"]![0]!["sendThrough"]!.ToString() == "192.0.2.2", "Xray source address pinned before TUN startup");
Assert(SubscriptionParser.Parse($"vless://{uuid}@example.com:443?type=raw&security=tls").Single().Outbound["transport"] == null, "RAW alias uses TCP transport");
Assert(SubscriptionParser.Parse(nullExtra).Single().Outbound["xray"]!["streamSettings"]!["xhttpSettings"]!["extra"] == null, "XHTTP null extra means absent options");
Assert(SubscriptionParser.Parse(inputs[0] + "\n" + nullExtra).Count == 2, "Null extra does not reject the entire subscription");
Reject(nullExtra.Replace("extra=null", "extra=%5B%5D"), "XHTTP array extra rejected");
Reject(nullExtra.Replace("extra=null", "extra=42"), "XHTTP scalar extra rejected");
if (args.Length == 1 && !args[0].StartsWith("--", StringComparison.Ordinal))
{
    var downloaded = SubscriptionParser.Parse(File.ReadAllText(args[0]));
    Assert(downloaded.Count > 0, $"External subscription imported: {downloaded.Count} profiles");
}
var xhttpRouting = ConfigBuilder.Build(new Settings { AppMode = 2, Apps = ["browser.exe"], DirectCountries = "ru" }, alpnProfile, 1080, "probe-password", output, 1081, "adapter-password", "xray.exe");
var xhttpRules = xhttpRouting["route"]!["rules"]!.AsArray();
var quicReject = xhttpRules.Single(r => r?["network"]?.ToString() == "udp" && r?["port"]?.ToString() == "443");
Assert(quicReject?["action"]?.ToString() == "reject" && xhttpRules.IndexOf(quicReject) > xhttpRules.ToList().FindLastIndex(r => r?["outbound"]?.ToString() == "direct"), "XHTTP QUIC rejection preserves application and country bypasses");
Assert(!awgIpv4["route"]!["rules"]!.AsArray().Any(r => r?["network"]?.ToString() == "udp" && r?["port"]?.ToString() == "443"), "AWG keeps native QUIC available");
var forcedSites = ConfigBuilder.Build(new Settings { AppMode = 1, DirectSites = "direct.example", VpnSites = "video.example" }, xhttp, 18000, "secret", output, 18002, "pw", "xray.exe");
var forcedRules = forcedSites["route"]!["rules"]!.AsArray().ToList();
var forcedRoute = forcedRules.FindIndex(r => r?["domain_suffix"]?[0]?.ToString() == "video.example" && r?["outbound"]?.ToString() == "proxy");
var forcedQuic = forcedRules.FindIndex(r => r?["domain_suffix"]?[0]?.ToString() == "video.example" && r?["network"]?.ToString() == "udp" && r?["action"]?.ToString() == "reject");
var forcedIpv6 = forcedRules.FindIndex(r => r?["domain_suffix"]?[0]?.ToString() == "video.example" && r?["ip_version"]?.ToString() == "6");
Assert(forcedQuic >= 0 && forcedQuic < forcedRoute, "Forced VPN site retains XHTTP QUIC fallback before routing");
Assert(forcedIpv6 >= 0 && forcedIpv6 < forcedRoute, "Forced VPN site retains IPv4 policy before routing");
Assert(!forcedRules.Any(r => r?["domain_suffix"]?[0]?.ToString() == "direct.example" && r?["action"]?.ToString() == "reject"), "Direct sites retain native QUIC and IPv6");
Assert(!ConfigBuilder.Build(new Settings(), xhttp, 18000, "secret", output, 18002, "pw", "xray.exe")["route"]!["rules"]!.AsArray().Any(r => r?["sniffer"]?.AsArray().Any(v => v?.ToString() == "quic") == true), "Empty site lists add no web sniffing overhead");
var xhttpRoutePath = Path.Combine(output, "xhttp-routing.json");
File.WriteAllText(xhttpRoutePath, xhttpRouting.ToJsonString());
var routeCheck = new ProcessStartInfo(core) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
routeCheck.ArgumentList.Add("check"); routeCheck.ArgumentList.Add("-c"); routeCheck.ArgumentList.Add(xhttpRoutePath);
using (var check = Process.Start(routeCheck)!) { var stderr = check.StandardError.ReadToEndAsync(); var stdout = check.StandardOutput.ReadToEndAsync(); await check.WaitForExitAsync(); Assert(check.ExitCode == 0, "sing-box accepts XHTTP QUIC fallback: " + await stderr); await stdout; }
var attempts = 0;
await ConnectivityCheck.RunAsync("https://example.com", (_, _) =>
{
    attempts++;
    if (attempts == 1) throw new System.Net.Http.HttpRequestException("primary blocked");
    return Task.CompletedTask;
}, CancellationToken.None);
Assert(attempts == 2, "Blocked primary health website does not trigger reconnect");
attempts = 0;
try
{
    await ConnectivityCheck.RunAsync("https://example.com", (_, _) => { attempts++; throw new TaskCanceledException(); }, CancellationToken.None);
    Assert(false, "All health targets failed");
}
catch (IOException) { Assert(attempts == 3, "Reconnect only after all independent health targets fail"); }
using (var canceled = new CancellationTokenSource())
{
    canceled.Cancel(); attempts = 0;
    try { await ConnectivityCheck.RunAsync("https://example.com", (_, _) => { attempts++; return Task.CompletedTask; }, canceled.Token); Assert(false, "Cancellation expected"); }
    catch (OperationCanceledException) { Assert(attempts == 0, "Disconnect cancellation skips fallback probes"); }
}
var slowPrimary = true;
var probeResult = await ConnectivityCheck.RunAsync("https://example.com", async (_, _) =>
{
    if (slowPrimary) { slowPrimary = false; await Task.Delay(200); throw new System.Net.Http.HttpRequestException(); }
}, CancellationToken.None);
Assert(probeResult.Attempts == 2 && probeResult.ElapsedMilliseconds < 100, "Displayed HTTPS latency excludes failed primary timeout");
var primaryStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var hedgedResult = await ConnectivityCheck.RunAsync("https://example.com", async (uri, token) =>
{
    if (uri.Host == "example.com")
    {
        try { await Task.Delay(Timeout.Infinite, token); }
        finally { primaryStopped.TrySetResult(); }
    }
}, CancellationToken.None, fallbackDelay: TimeSpan.FromMilliseconds(25));
await primaryStopped.Task.WaitAsync(TimeSpan.FromSeconds(2));
Assert(hedgedResult.Attempts == 2 && hedgedResult.ElapsedMilliseconds < 100, "Healthy fallback wins while primary hangs; losing request is canceled");
var hangingChecks = 0;
try
{
    await ConnectivityCheck.RunAsync("https://example.com", async (_, token) => { Interlocked.Increment(ref hangingChecks); await Task.Delay(Timeout.Infinite, token); },
        CancellationToken.None, fallbackDelay: TimeSpan.FromMilliseconds(10), probeTimeout: TimeSpan.FromMilliseconds(60));
    Assert(false, "All stalled probes must fail");
}
catch (IOException) { Assert(hangingChecks == 3, "All stalled health targets finish within individual timeout budgets"); }
using (var healthCanceled = new CancellationTokenSource())
{
    try
    {
        await ConnectivityCheck.RunAsync("https://example.com", async (_, token) => { healthCanceled.Cancel(); await Task.Delay(Timeout.Infinite, token); }, healthCanceled.Token);
        Assert(false, "In-flight health cancellation must propagate");
    }
    catch (OperationCanceledException) { Assert(true, "In-flight cancellation stops hedged health checks"); }
}
var activationName = "Local\\AsterVPN.Test." + Guid.NewGuid();
using (var delivered = new ManualResetEventSlim())
using (var first = new InstanceActivation(activationName))
using (var second = new InstanceActivation(activationName))
{
    second.Signal(); // launch during startup, before listener registration
    first.Listen(delivered.Set);
    Assert(delivered.Wait(TimeSpan.FromSeconds(3)), "Second launch during startup is not lost");
    delivered.Reset(); second.Signal();
    Assert(delivered.Wait(TimeSpan.FromSeconds(3)), "Further launches activate existing instance");
}
using (var versionRequest = new System.Net.Http.HttpRequestMessage())
{
    DeviceIdentity.AddHeaders(versionRequest, "test");
    Assert(versionRequest.Headers.GetValues("x-app-version").Single() == ApplicationVersion.Current, "Subscription version comes from assembly metadata");
}
var defaultOptions = System.Text.Json.JsonSerializer.Deserialize<Settings>("{}")!;
Assert(!defaultOptions.KillSwitch && !defaultOptions.StartWithWindows && !defaultOptions.AutoConnect, "New protection/startup options are opt-in for existing installations");
var restoredOptions = System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(
    new Settings { KillSwitch = true, StartWithWindows = true, AutoConnect = true, SelectedServerId = "last-server" }))!;
Assert(restoredOptions.KillSwitch && restoredOptions.StartWithWindows && restoredOptions.AutoConnect && restoredOptions.SelectedServerId == "last-server", "Startup/protection options and selected server survive serialization");
var taskXml = System.Xml.Linq.XDocument.Parse(WindowsStartup.BuildXml(@"C:\VPN & Tools\AsterVPN.exe", "S-1-5-21-123"));
System.Xml.Linq.XNamespace taskNs = "http://schemas.microsoft.com/windows/2004/02/mit/task";
Assert(taskXml.Descendants(taskNs + "Command").Single().Value == @"C:\VPN & Tools\AsterVPN.exe" && taskXml.Descendants(taskNs + "Arguments").Single().Value == "--startup", "Startup task safely preserves paths and requests tray mode");
Assert(taskXml.Descendants(taskNs + "RunLevel").Single().Value == "HighestAvailable" && taskXml.Descendants(taskNs + "LogonType").Single().Value == "InteractiveToken" && taskXml.Descendants(taskNs + "UserId").All(e => e.Value == "S-1-5-21-123"), "Startup task runs elevated in the current interactive user session");
Assert(taskXml.Descendants(taskNs + "ExecutionTimeLimit").Single().Value == "PT0S" && taskXml.Descendants(taskNs + "StopIfGoingOnBatteries").Single().Value == "false", "Windows does not terminate the VPN after a time limit or battery change");
Assert((await BootstrapDns.ResolveAsync("192.0.2.1", true, CancellationToken.None)).Single().Equals(System.Net.IPAddress.Parse("192.0.2.1")), "Literal VPN endpoints require no DNS with Kill switch");
var protectedConfig = ConfigBuilder.Build(new Settings { KillSwitch = true }, profiles[0], 18991, "test-secret", output);
Assert(protectedConfig["dns"]!["servers"]![0]!["type"]!.ToString() == "https" && protectedConfig["dns"]!["servers"]![0]!["detour"]!.ToString() == "direct", "Protected bootstrap uses direct encrypted DNS instead of system DNS");
var protectedPath = Path.Combine(output, "kill-switch.json"); File.WriteAllText(protectedPath, protectedConfig.ToJsonString());
var protectedCheck = new ProcessStartInfo(core) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
protectedCheck.ArgumentList.Add("check"); protectedCheck.ArgumentList.Add("-c"); protectedCheck.ArgumentList.Add(protectedPath);
using (var check = Process.Start(protectedCheck)!)
{
    var error = await check.StandardError.ReadToEndAsync(); await check.WaitForExitAsync();
    Assert(check.ExitCode == 0, "Protected DNS configuration passes sing-box check: " + error);
}
if (args.Contains("--wfp-validate"))
{
    using var protection = new KillSwitch();
    foreach (var bypassLan in new[] { false, true })
    {
        protection.Enable(core, bypassLan, validateOnly: true);
        Assert(!protection.IsEnabled, "Windows accepts IPv4/IPv6 WFP rules; transaction rolled back, LAN=" + bypassLan);
    }
}
Console.WriteLine($"ALL {passed} CHECKS PASSED");
