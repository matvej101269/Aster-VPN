using AsterVPN;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Security.Cryptography;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
var privateDir = Path.Combine(root, ".private");
var distribution = Path.Combine(root, args.Contains("--release") ? "dist/AsterVPN-0.4.1/core" : "dist/AsterVPN/core");
if (args.Contains("--uplink")) { Console.WriteLine("Physical uplink: " + PhysicalUplink.Find().Name); return 0; }
if (args.Contains("--settings-summary"))
{
    var settingsPath = Path.Combine(SecureStore.DirectoryPath, "settings.bin");
    var saved = System.Text.Json.JsonSerializer.Deserialize<Settings>(SecureStore.Decrypt(File.ReadAllBytes(settingsPath)))!;
    saved.MigrateSubscriptions();
    Console.WriteLine($"Mode={saved.AppMode}; AppExceptions={saved.Apps.Count}; CountryExceptions={ConfigBuilder.Countries(saved.DirectCountries).Length}; BypassLan={saved.BypassLan}; Profiles={saved.AllServers.Count}");
    return 0;
}
var data = Encoding.UTF8.GetString(SecureStore.Decrypt(File.ReadAllBytes(Path.Combine(privateDir, args.Contains("--new") ? "new-subscription.bin" : "subscription.bin"))));
List<ServerProfile> profiles;
try {
    profiles = SubscriptionParser.Parse(data);
    if (args.Contains("--current")) {
        var saved = System.Text.Json.JsonSerializer.Deserialize<Settings>(SecureStore.Decrypt(File.ReadAllBytes(Path.Combine(SecureStore.DirectoryPath, "settings.bin"))))!;
        saved.MigrateSubscriptions(); profiles = saved.AllServers;
        if (args.Contains("--refresh-readonly"))
        {
            var fresh = new List<ServerProfile>();
            foreach (var sub in saved.Subscriptions)
                if (sub.Url.Length > 0) fresh.AddRange((await SubscriptionService.FetchAsync(sub.Url)).Servers);
                else fresh.AddRange(sub.Servers);
            Console.WriteLine($"Fresh subscription: {fresh.Count} profiles; unchanged configurations={fresh.Count(p => profiles.Any(old => old.Outbound.ToJsonString() == p.Outbound.ToJsonString()))}");
            IEnumerable<string> Changed(JsonNode? a, JsonNode? b, string path)
            {
                if (JsonNode.DeepEquals(a, b)) yield break;
                if (a is JsonObject ao && b is JsonObject bo)
                    foreach (var key in ao.Select(k => k.Key).Union(bo.Select(k => k.Key)))
                        foreach (var changed in Changed(ao[key], bo[key], path + "." + key)) yield return changed;
                else yield return path;
            }
            foreach (var current in fresh)
                if (profiles.FirstOrDefault(old => old.Protocol == current.Protocol && old.Country == current.Country) is { } old)
                    Console.WriteLine($"  {current.Country}/{current.Protocol}: changed fields=" + string.Join(",", Changed(old.Outbound, current.Outbound, "outbound")));
            profiles = fresh;
        }
    }
}
catch (Exception ex) { Console.WriteLine("PARSE FAILED: " + ex.Message); return 1; }
var snapshotPath = Path.Combine(privateDir, "vless-benchmark.json");
if (args.Contains("--use-snapshot")) profiles = System.Text.Json.JsonSerializer.Deserialize<List<ServerProfile>>(File.ReadAllText(snapshotPath))!;
if (args.Contains("--save-snapshot")) File.WriteAllText(snapshotPath, System.Text.Json.JsonSerializer.Serialize(profiles));
Console.WriteLine($"PARSED {profiles.Count} profiles: " + string.Join(", ", profiles.Select(p => p.Protocol + (p.UsesXray ? "/XHTTP" : ""))));
if (args.Contains("--parse-only")) return 0;
if (args.Contains("--shape")) {
    foreach (var p in profiles.Where(p => p.UsesXray)) {
        var stream = p.Outbound["xray"]!["streamSettings"]!;
        var resolved = await Dns.GetHostAddressesAsync(p.Host);
        Console.WriteLine($"  Endpoint IPv4 count={resolved.Count(a => a.AddressFamily == AddressFamily.InterNetwork)}; RealitySniEqualsXhttpHost={stream["realitySettings"]?["serverName"]?.ToString() == stream["xhttpSettings"]?["host"]?.ToString()}; SNI present={!string.IsNullOrWhiteSpace(stream["realitySettings"]?["serverName"]?.ToString())}");
        Console.WriteLine($"{p.Country}: security={stream["security"]}, transport={stream["network"]}, mode={stream["xhttpSettings"]?["mode"]}, extraMode={stream["xhttpSettings"]?["extra"]?["mode"]}, fingerprint={stream["realitySettings"]?["fingerprint"]}, extraKeys={string.Join(',', (stream["xhttpSettings"]?["extra"] as JsonObject)?.Select(k => k.Key) ?? [])}, IP={IPAddress.TryParse(p.Host, out _)}, port={p.Port}");
    }
    return 0;
}
if (args.Contains("--tun"))
{
    await using var engine = new VpnEngine(Path.Combine(distribution, "sing-box.exe"), args.Contains("--trace"));
    engine.Status += message =>
    {
        Console.WriteLine("  " + message);
        if (message == "Проверка интернета…")
            _ = Task.Run(async () =>
            {
                await Task.Delay(2000);
                var sockets = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections().Where(c => c.RemoteEndPoint.Port == profiles[0].Port).ToArray();
                Console.WriteLine("  TRANSPORT: " + string.Join(", ", sockets.GroupBy(c => new { c.State, TunSource = c.LocalEndPoint.Address.ToString() == "172.29.0.1" }).Select(g => $"{g.Key}: {g.Count()}")));
            });
        if (args.Contains("--compare") && message == "Проверка VLESS-сервера…") File.Copy(Path.Combine(SecureStore.DirectoryPath, "awg-runtime.json"), Path.Combine(privateDir, "engine-compare.json"), true);
    };
    engine.Log += message => Console.WriteLine("  " + message);
    var success = true;
    foreach (var profile in profiles)
    {
        if (args.Contains("--first") && profile != profiles[0]) continue;
        if (args.Contains("--spain") && profile != profiles[4]) continue;
        Console.WriteLine("TUN " + profile.Protocol);
        try
        {
            await engine.ConnectAsync(args.Contains("--include") ? new Settings { AppMode = 1, Apps = [Environment.ProcessPath!] } : new Settings(), profile);
            if (engine.State != ConnectionState.Connected) throw new Exception("VPN health failed");
            using var handler = new HttpClientHandler { UseProxy = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            using var response = await client.GetAsync("https://cp.cloudflare.com/generate_204");
            response.EnsureSuccessStatusCode();
            Console.WriteLine("  SYSTEM HTTPS PASS");
        }
        catch (Exception ex) { success = false; Console.WriteLine("  TUN FAILED: " + ex.GetType().Name); }
        finally { await engine.DisconnectAsync(); }
    }
    return success ? 0 : 1;
}
if (args.Contains("--install"))
{
    var settings = SecureStore.Load();
    settings.Subscription = Encoding.UTF8.GetString(SecureStore.Decrypt(File.ReadAllBytes(Path.Combine(privateDir, "subscription-url.bin"))));
    settings.Servers = profiles; settings.Selected = 2; settings.LastUpdated = DateTimeOffset.UtcNow;
    SecureStore.Save(settings);
    var verify = SecureStore.Load();
    if (verify.Servers.Count != 6 || verify.Subscription != settings.Subscription) throw new Exception("Encrypted storage verification failed");
    Console.WriteLine("Subscription installed and DPAPI round-trip verified; credentials not printed.");
    return 0;
}
var results = new List<object>();
var allPassed = true;
var errorKinds = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
var errorSamples = new System.Collections.Concurrent.ConcurrentQueue<string>();
var redactions = new List<string>();
void CollectRedactions(JsonNode? node)
{
    if (node is JsonObject obj) foreach (var item in obj) CollectRedactions(item.Value);
    else if (node is JsonArray array) foreach (var item in array) CollectRedactions(item);
    else if (node is JsonValue value && value.TryGetValue<string>(out var text) && text.Length >= 4) redactions.Add(text);
}
using var testJob = args.Contains("--job") ? new ProcessJob() : null;
int FreePort() { using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); return ((IPEndPoint)listener.LocalEndpoint).Port; }
Process Start(string executable, params string[] arguments)
{
    var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var a in arguments) info.ArgumentList.Add(a);
    var process = Process.Start(info)!;
    testJob?.Add(process);
    // No raw core logs: they may contain profile credentials.
    void Observe(string? line)
    {
        if (!args.Contains("--error-kinds") || line == null) return;
        if (line.Contains("failed to", StringComparison.OrdinalIgnoreCase) && errorSamples.Count < 8)
        {
            foreach (var secret in redactions.OrderByDescending(s => s.Length)) line = line.Replace(secret, "***", StringComparison.Ordinal);
            line = System.Text.RegularExpressions.Regex.Replace(line, @"\b(?:\d{1,3}\.){3}\d{1,3}(?::\d+)?\b", "[IP]");
            errorSamples.Enqueue(line.Length > 800 ? line[..800] : line);
        }
        foreach (var kind in new[] { "REALITY", "handshake", "timeout", "refused", "reset", "certificate", "EOF", "failed to", "invalid", "closed" })
            if (line.Contains(kind, StringComparison.OrdinalIgnoreCase)) errorKinds.AddOrUpdate(kind, 1, (_, count) => count + 1);
    }
    process.OutputDataReceived += (_, e) => Observe(e.Data); process.ErrorDataReceived += (_, e) => Observe(e.Data);
    process.BeginOutputReadLine(); process.BeginErrorReadLine(); return process;
}
var core = args.Contains("--release") ? Path.Combine(distribution, "sing-box.exe") : Path.Combine(root, "vendor/sing-box-1.14.3-windows-amd64/sing-box.exe");
var xrayVersion = args.Contains("--baseline") ? "26.9.9" : File.ReadAllText(Path.Combine(root, "scripts/xray-version.txt")).Trim();
var testedProtocols = new HashSet<string>();
for (var i = 0; i < profiles.Count; i++)
{
    if (args.Contains("--first") && i != 0) continue;
    if (args.Contains("--spain") && i != 4) continue;
    if (args.Contains("--netherlands") && i != 2) continue;
    if (args.Contains("--failed-only") && i is 2 or 5) continue;
    var profile = profiles[i]; var label = $"#{i + 1} {profile.Protocol}" + (profile.UsesXray ? "/XHTTP" : "");
    if (args.Contains("--quick") && !profile.UsesXray) continue;
    if (args.Contains("--vless") && !profile.UsesXray) continue;
    if (args.Contains("--one-per-protocol") && !testedProtocols.Add(profile.Protocol)) continue;
    Console.WriteLine("CHECK " + label);
    errorKinds.Clear();
    errorSamples.Clear(); redactions.Clear(); CollectRedactions(profile.Outbound);
    var helperPath = Path.Combine(root, profile.UsesXray ? $"vendor/xray-{xrayVersion}/xray.exe" : "vendor/aster-awg.exe");
    if (args.Contains("--dist") || args.Contains("--release")) helperPath = Path.Combine(distribution, profile.UsesXray ? "xray.exe" : "aster-awg.exe");
    if (args.Contains("--modern") && profile.UsesXray) helperPath = Path.Combine(root, "vendor/xray-26.9.9/xray.exe");
    var helperPort = FreePort(); var mixedPort = FreePort(); var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    redactions.Add(password);
    var testProxyPort = args.Contains("--helper-only") ? helperPort : mixedPort;
    var testProxyUser = args.Contains("--helper-only") ? "aster" : "health";
    var helperConfig = Path.Combine(privateDir, "helper-test.json"); var coreConfig = Path.Combine(privateDir, "core-test.json");
    Process? helper = null, sing = null;
    var checks = new List<string>();
    try
    {
        var config = profile.UsesXray ? XrayConfig.Build(profile, helperPort, password) : await AmneziaConfig.BuildAsync(profile, helperPort, password);
        if (profile.UsesXray && args.Contains("--legacy-tuning"))
        {
            var outbound = config["outbounds"]![0]!;
            var transport = outbound["streamSettings"]!["xhttpSettings"]!;
            transport["extra"] ??= new JsonObject();
            transport["extra"]!["xmux"] ??= new JsonObject { ["maxConcurrency"] = 4, ["maxConnections"] = 0, ["cMaxReuseTimes"] = 0, ["hMaxRequestTimes"] = "600-900", ["hMaxReusableSecs"] = "1800-3000", ["hKeepAlivePeriod"] = 0 };
            outbound["mux"] = new JsonObject { ["enabled"] = true, ["concurrency"] = -1, ["xudpConcurrency"] = 16, ["xudpProxyUDP443"] = "reject" };
        }
        if (profile.UsesXray && args.Contains("--native-xmux")) config["outbounds"]![0]!["streamSettings"]!["xhttpSettings"]!["extra"]!.AsObject().Remove("xmux");
        if (profile.UsesXray && args.Contains("--no-outbound-mux")) config["outbounds"]![0]!.AsObject().Remove("mux");
        if (profile.UsesXray && (args.Contains("--stream-up") || args.Contains("--stream-one")))
        {
            var candidateMode = args.Contains("--stream-up") ? "stream-up" : "stream-one";
            var transport = config["outbounds"]![0]!["streamSettings"]!["xhttpSettings"]!;
            transport["mode"] = candidateMode; transport["extra"]!["mode"] = candidateMode;
        }
        if (profile.UsesXray && args.Contains("--error-kinds")) config["log"]!["loglevel"] = "debug";
        if (profile.UsesXray && args.Contains("--independent-streams"))
        {
            var xhttp = config["outbounds"]![0]!["streamSettings"]!["xhttpSettings"]!;
            xhttp["extra"] ??= new JsonObject();
            xhttp["extra"]!["xmux"] = new JsonObject { ["maxConcurrency"] = 1, ["maxConnections"] = 0, ["cMaxReuseTimes"] = 0, ["hMaxRequestTimes"] = "600-900", ["hMaxReusableSecs"] = "1800-3000", ["hKeepAlivePeriod"] = 0 };
        }
        if (profile.UsesXray && args.Contains("--pool-four"))
        {
            var transport = config["outbounds"]![0]!["streamSettings"]!["xhttpSettings"]!;
            transport["extra"] ??= new JsonObject();
            transport["extra"]!["xmux"] = new JsonObject { ["maxConnections"] = 4 };
        }
        if (profile.UsesXray && args.Contains("--resolved"))
        {
            var addresses = await Dns.GetHostAddressesAsync(profile.Host);
            config["outbounds"]![0]!["settings"]!["vnext"]![0]!["address"] = addresses.First(a => a.AddressFamily == AddressFamily.InterNetwork).ToString();
        }
        if (profile.UsesXray && (args.Contains("--bound") || args.Contains("--physical")))
        {
            var addresses = await Dns.GetHostAddressesAsync(profile.Host);
            var address = addresses.First(a => a.AddressFamily == AddressFamily.InterNetwork);
            using var routeSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            routeSocket.Connect(new IPEndPoint(address, profile.Port));
            var source = ((IPEndPoint)routeSocket.LocalEndPoint!).Address;
            var network = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces().First(n => n.GetIPProperties().UnicastAddresses.Any(a => a.Address.Equals(source)));
            if (args.Contains("--physical")) network = PhysicalUplink.Find();
            var sourceAddress = network.GetIPProperties().UnicastAddresses.First(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Address;
            XrayConfig.BindTransport(config, address.ToString(), network.Name, sourceAddress.ToString());
        }
        await File.WriteAllTextAsync(helperConfig, config.ToJsonString());
        if (args.Contains("--compare")) File.Copy(helperConfig, Path.Combine(privateDir, "harness-compare.json"), true);
        using (var validate = profile.UsesXray ? Start(helperPath, "run", "-test", "-config", helperConfig) : Start(helperPath, "--check", "--config", helperConfig))
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await validate.WaitForExitAsync(timeout.Token); } catch { validate.Kill(true); throw; }
            if (validate.ExitCode != 0) throw new Exception("Adapter rejected configuration");
        }
        Console.WriteLine("  Adapter configuration VALID");
        var mainConfig = ConfigBuilder.Build(new Settings(), profile, mixedPort, password, privateDir, helperPort, password, helperPath);
        if (args.Contains("--physical"))
        {
            mainConfig["route"]!["auto_detect_interface"] = false;
            mainConfig["route"]!["default_interface"] = PhysicalUplink.Find().Name;
            mainConfig["outbounds"]![0]!["bind_interface"] = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces().First(n => n.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback).Name;
        }
        // Test the actual proxy chain, without creating TUN or touching system routes/DNS.
        mainConfig["inbounds"]!.AsArray().RemoveAt(0); mainConfig.Remove("experimental");
        var dnsTestPort = 0;
        if (args.Contains("--mixed-load"))
        {
            dnsTestPort = FreePort();
            mainConfig["inbounds"]!.AsArray().Add(new JsonObject { ["type"] = "direct", ["tag"] = "dns-load", ["listen"] = "127.0.0.1", ["listen_port"] = dnsTestPort, ["network"] = "udp", ["override_address"] = "8.8.8.8", ["override_port"] = 53 });
            mainConfig["route"]!["rules"]!.AsArray().Insert(0, new JsonObject { ["inbound"] = SubscriptionParser.Array(["dns-load"]), ["action"] = "hijack-dns" });
            mainConfig["dns"]!["disable_cache"] = true; // exercise real DoH during the whole load test
        }
        await File.WriteAllTextAsync(coreConfig, mainConfig.ToJsonString());
        using (var validate = Start(core, "check", "-c", coreConfig)) { await validate.WaitForExitAsync(); if (validate.ExitCode != 0) throw new Exception("Router rejected configuration"); }
        helper = profile.UsesXray ? Start(helperPath, "run", "-config", helperConfig) : Start(helperPath, "--config", helperConfig);
        await Task.Delay(700); if (helper.HasExited) throw new Exception("Adapter exited");
        sing = Start(core, "run", "-c", coreConfig); await Task.Delay(700); if (sing.HasExited) throw new Exception("Router exited");
        if (args.Contains("--mixed-load"))
        {
            if (!await MixedLoad.RunAsync(testProxyPort, testProxyUser, password, dnsTestPort)) allPassed = false;
        }
        if (args.Contains("--load"))
        {
            using var loadHandler = new HttpClientHandler { UseProxy = true, Proxy = new WebProxy($"socks5://127.0.0.1:{testProxyPort}") { Credentials = new NetworkCredential(testProxyUser, password) } };
            using var loadClient = new HttpClient(loadHandler) { Timeout = TimeSpan.FromSeconds(12) };
            if (args.Contains("--warm")) { try { using var warm = await loadClient.GetAsync("https://www.gstatic.com/generate_204"); Console.WriteLine("  Warmup: " + (int)warm.StatusCode); } catch { Console.WriteLine("  Warmup unavailable"); } }
            var loadResults = await Task.WhenAll(Enumerable.Range(0, 16).Select(async _ => { try { using var r = await loadClient.GetAsync("https://www.gstatic.com/generate_204"); return r.IsSuccessStatusCode; } catch { return false; } }));
            Console.WriteLine("  Parallel HTTPS success: " + loadResults.Count(r => r) + "/16");
            if (loadResults.Any(r => !r)) allPassed = false;
        }
        if (args.Contains("--throughput"))
        {
            foreach (var speedPath in args.Contains("--compare-paths") ? new[] { "helper", "router", "helper" } : new[] { args.Contains("--helper-only") ? "helper" : "router" })
            {
            var speedPort = speedPath == "helper" ? helperPort : mixedPort;
            var speedUser = speedPath == "helper" ? "aster" : "health";
            Console.WriteLine("  Throughput path: " + speedPath);

            using var speedHandler = new HttpClientHandler { UseProxy = true, Proxy = new WebProxy($"socks5://127.0.0.1:{speedPort}") { Credentials = new NetworkCredential(speedUser, password) } };
            using var speedClient = new HttpClient(speedHandler) { Timeout = TimeSpan.FromSeconds(60) };
            var watch = Stopwatch.StartNew();
            var downloadTask = Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
            {
                using var downloadRequest = new HttpRequestMessage(HttpMethod.Get, args.Contains("--google-download") ? "https://dl.google.com/chrome/install/ChromeStandaloneSetup64.exe" : "https://speed.cloudflare.com/__down?bytes=4000000");
                if (args.Contains("--google-download")) downloadRequest.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 3999999);
                using var response = await speedClient.SendAsync(downloadRequest, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                if (args.Contains("--google-download") && response.StatusCode != HttpStatusCode.PartialContent) throw new IOException("CDN did not honor bounded range");
                using var body = await response.Content.ReadAsStreamAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var buffer = new byte[65536]; long bytes = 0; int n;
                while ((n = await body.ReadAsync(buffer, timeout.Token)) > 0) bytes += n;
                if (bytes != 4000000) throw new IOException("Incomplete throughput response");
                return bytes;
            })).ContinueWith(completed =>
            {
                watch.Stop(); // Do not include a trailing health probe in download duration.
                return completed.GetAwaiter().GetResult();
            }, TaskContinuationOptions.ExecuteSynchronously);
            var latency = new List<long>();
            while (!downloadTask.IsCompleted)
            {
                var probeWatch = Stopwatch.StartNew();
                try
                {
                    using var probeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                    using var probe = await speedClient.GetAsync("https://www.gstatic.com/generate_204", HttpCompletionOption.ResponseHeadersRead, probeTimeout.Token);
                    if (probe.IsSuccessStatusCode) latency.Add(probeWatch.ElapsedMilliseconds);
                }
                catch { Console.WriteLine("  HTTPS under load: failed"); }
                await Task.WhenAny(downloadTask, Task.Delay(1000));
            }
            var downloads = await downloadTask;
            if (latency.Count > 0) Console.WriteLine($"  HTTPS under load: min={latency.Min()} ms, max={latency.Max()} ms, samples={latency.Count}");
            var mbps = downloads.Sum() * 8 / watch.Elapsed.TotalSeconds / 1000000;
            Console.WriteLine($"  4 parallel downloads: {downloads.Sum()} bytes, {mbps:F1} Mbps, cores alive={!helper.HasExited && !sing.HasExited}");
            checks.Add($"Parallel download: {mbps:F1} Mbps");
            }
        }
        foreach (var (url, name) in new[] { ("https://www.gstatic.com/generate_204", "Google 204"), ("https://cp.cloudflare.com/generate_204", "Cloudflare 204") })
        {
            using var handler = new HttpClientHandler { UseProxy = true, Proxy = new WebProxy($"socks5://127.0.0.1:{testProxyPort}") { Credentials = new NetworkCredential(testProxyUser, password) } };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(args.Contains("--quick") ? 6 : 20) };
            try
            {
                var watch = Stopwatch.StartNew(); using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                var result = $"{name}: HTTP {(int)response.StatusCode}, {watch.ElapsedMilliseconds} ms";
                checks.Add(result); Console.WriteLine("  " + result);
            }
            catch (Exception ex) { allPassed = false; var result = $"{name}: {ex.GetType().Name}"; checks.Add(result); Console.WriteLine("  " + result); }
        }
        results.Add(new { Index = i + 1, Protocol = profile.Protocol, Xhttp = profile.UsesXray, Config = "valid", Probes = checks });
    }
    catch (Exception ex) { allPassed = false; Console.WriteLine("  FAILED: " + ex.GetType().Name + (ex.Message is "Adapter rejected configuration" or "Router rejected configuration" ? " " + ex.Message : "")); results.Add(new { Index = i + 1, Protocol = profile.Protocol, Error = ex.GetType().Name }); }
    finally
    {
        foreach (var p in new[] { sing, helper }) if (p != null) { if (!p.HasExited) { p.Kill(true); await p.WaitForExitAsync(); } p.Dispose(); }
        if (File.Exists(helperConfig)) File.Delete(helperConfig); if (File.Exists(coreConfig)) File.Delete(coreConfig);
        if (args.Contains("--error-kinds")) Console.WriteLine("  Core error categories: " + string.Join(", ", errorKinds.OrderBy(k => k.Key).Select(k => k.Key + "=" + k.Value)));
        if (args.Contains("--error-kinds")) foreach (var sample in errorSamples.Distinct().Take(3)) Console.WriteLine("  Redacted core error: " + sample);
    }
}
await File.WriteAllTextAsync(Path.Combine(root, "tests/subscription-check-results.json"), System.Text.Json.JsonSerializer.Serialize(results, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
return allPassed ? 0 : 1;
