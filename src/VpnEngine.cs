using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
namespace AsterVPN;
public enum ConnectionState { Disconnected, Connecting, Checking, Connected, Recovering, Degraded, Disconnecting, Error }
public sealed class VpnEngine : IAsyncDisposable
{
    public static string CorePath => Path.Combine(AppContext.BaseDirectory, "core", "sing-box.exe");
    private readonly string corePath;
    private readonly bool diagnostics;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly ProcessJob job = new();
    private readonly KillSwitch killSwitch = new();
    public bool IsTrafficBlocked => killSwitch.IsEnabled;
    private Process? process;
    private Process? awgProcess;
    private string? awgConfigPath;
    private Settings? activeSettings;
    private ServerProfile? activeServer;
    private string? configPath;
    private int port;
    private string password = "";
    private int probeAttempts;
    private string ConnectedStatus(long ms) => $"Подключено · HTTPS {ms} мс · TUN проверен" + (probeAttempts > 1 ? " · резервный адрес" : "");
    private string probeStage = "ядра";
    private bool wanted;
    private int failures, restarts;
    private DateTimeOffset nextRetry;
    private string? uplinkFingerprint;
    private readonly Task watchdog;
    private CancellationTokenSource? connectionAttempt;
    private bool userCancelled;
    public ConnectionState State { get; private set; }
    public bool IsTransitioning => State is ConnectionState.Connecting or ConnectionState.Checking or ConnectionState.Recovering or ConnectionState.Disconnecting;
    public void CancelConnection() { userCancelled = true; connectionAttempt?.Cancel(); }
    private void Report(ConnectionState state, string message) { State = state; Status?.Invoke(message); }
    public event Action<string>? Status;
    public event Action<string>? Log;
    public bool IsWanted => wanted;
    public bool IsRunning => process is { HasExited: false } && ((activeServer?.Protocol != "amneziawg" && activeServer?.UsesXray != true) || awgProcess is { HasExited: false });
    public VpnEngine(string? corePath = null, bool diagnostics = false) { this.corePath = corePath ?? CorePath; this.diagnostics = diagnostics; watchdog = WatchAsync(lifetime.Token); }
    public async Task ConnectAsync(Settings settings, ServerProfile server)
    {
        await gate.WaitAsync();
        userCancelled = false;
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        connectionAttempt = attempt;
        attempt.CancelAfter(TimeSpan.FromSeconds(120));
        try
        {
            if (settings.KillSwitch) killSwitch.Enable(corePath, settings.BypassLan);
            else killSwitch.Disable();
            wanted = false; await StopCoreAsync();
            activeSettings = System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
            activeServer = new() { Id = server.Id, Name = server.Name, Outbound = (System.Text.Json.Nodes.JsonObject)server.Outbound.DeepClone(), IsEndpoint = server.IsEndpoint, UsesXray = server.UsesXray };
            failures = restarts = 0;
            nextRetry = DateTimeOffset.MinValue;
            Report(ConnectionState.Connecting, "Подключение…");
            await StartCoreAsync(attempt.Token);
            wanted = true;
            Report(ConnectionState.Checking, "Проверка интернета…");
            for (var checkAttempt = 1; checkAttempt <= 3; checkAttempt++)
            {
                try { var ms = await ProbeAsync(attempt.Token); Report(ConnectionState.Connected, ConnectedStatus(ms)); break; }
                catch (Exception ex) when (!attempt.IsCancellationRequested)
                {
                    Diagnostic("Начальная проверка не прошла: " + probeStage + "; " + FailureKind(ex));
                    if (checkAttempt == 1 && activeServer!.UsesXray && activeSettings!.AutoUpdate)
                    {
                        // Keep the selected cached profile on healthy connections.
                        // Fetch replacement credentials only after a real failure.
                        var fresh = await SubscriptionService.RefreshForRecoveryAsync(activeSettings, activeServer, attempt.Token,
                            onCachedFallback: () => Diagnostic("Подписка временно недоступна; используется сохранённый VLESS-профиль."));
                        if (!System.Text.Json.Nodes.JsonNode.DeepEquals(fresh.Outbound, activeServer.Outbound))
                        {
                            Report(ConnectionState.Recovering, "Обновление параметров VLESS после ошибки…");
                            await StopCoreAsync(); activeServer = fresh;
                            await StartCoreAsync(attempt.Token); wanted = true;
                            Log?.Invoke("Параметры VLESS обновлены после неудачной проверки сохранённого профиля.");
                        }
                    }
                    if (checkAttempt == 3) Report(ConnectionState.Degraded, "Туннель запущен · интернет не подтверждён");
                    else { Report(ConnectionState.Checking, $"Ожидание готовности сети · проверка {checkAttempt + 1}/3"); await Task.Delay(1000, attempt.Token); }
                }
            }
        }
        catch (OperationCanceledException) when (attempt.IsCancellationRequested)
        { wanted = false; await StopCoreAsync(); if (userCancelled) killSwitch.Disable(); Report(ConnectionState.Disconnected, "Подключение отменено или истекло время ожидания" + BlockedText); }
        catch { wanted = false; await StopCoreAsync(); Report(ConnectionState.Error, "Ошибка подключения · см. журнал" + BlockedText); throw; }
        finally { connectionAttempt = null; gate.Release(); }
    }
    public async Task DisconnectAsync()
    {
        CancelConnection();
        await gate.WaitAsync();
        try { Report(ConnectionState.Disconnecting, "Отключение…"); wanted = false; await StopCoreAsync(); killSwitch.Disable(); Report(ConnectionState.Disconnected, "Не подключено"); }
        finally { gate.Release(); }
    }
    private string BlockedText => killSwitch.IsEnabled ? " · Kill switch блокирует интернет; «Отключить» в трее снимает блокировку" : "";
    public void RestoreProtection(Settings settings)
    {
        if (killSwitch.Restore(corePath, settings.KillSwitch))
            Report(ConnectionState.Error, "Kill switch: блокировка сохранена после сбоя. Подключитесь или нажмите «Отключить» в трее.");
    }
    public async Task ApplyOptionsAsync(Settings settings)
    {
        await gate.WaitAsync();
        try
        {
            if (!settings.KillSwitch) killSwitch.Disable();
            else if (wanted || killSwitch.IsEnabled) killSwitch.Enable(corePath, settings.BypassLan, IsRunning);
            if (activeSettings != null) { activeSettings.KillSwitch = settings.KillSwitch; activeSettings.AutoReconnect = settings.AutoReconnect; }
        }
        finally { gate.Release(); }
    }
    private async Task StartCoreAsync(CancellationToken ct)
    {
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(ct);
        startup.CancelAfter(TimeSpan.FromSeconds(45));
        ct = startup.Token;
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        if (!new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
            throw new InvalidOperationException("Для TUN нужны права администратора. Закройте клиент и запустите AsterVPN.exe от имени администратора.");
        if (!File.Exists(corePath)) throw new FileNotFoundException("Не найден core\\sing-box.exe. Запустите scripts\\build.ps1.");
        var uplink = PhysicalUplink.Find();
        uplinkFingerprint = PhysicalUplink.Fingerprint(uplink);
        Log?.Invoke("Исходящий транспорт: физический Ethernet/Wi-Fi (не интерфейс другого VPN).");
        if (activeSettings!.AppMode != 0 || activeSettings.EffectiveSiteMode != 0 || activeSettings.EffectiveGeoMode != 0)
            Log?.Invoke("Включены пользовательские правила маршрутизации приложений, сайтов или стран.");
        SecureStore.Prepare();
        Diagnostic($"Запуск Aster {typeof(VpnEngine).Assembly.GetName().Version?.ToString(3)}; физический интерфейс выбран.");
        if (ConfigBuilder.RequiresIpv4(activeServer!)) Log?.Invoke("Используется IPv4; IPv6-запросы отклоняются внутри туннеля.");
        using (var listener = new TcpListener(IPAddress.Loopback, 0)) { listener.Start(); port = ((IPEndPoint)listener.LocalEndpoint).Port; }
        password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        configPath = Path.Combine(SecureStore.DirectoryPath, "runtime.json");
        var awgPort = 0; var awgPassword = "";
        string? transportAddress = null;
        var awgPath = Path.Combine(Path.GetDirectoryName(corePath)!, activeServer!.UsesXray ? "xray.exe" : "aster-awg.exe");
        if (activeServer.Protocol == "amneziawg" || activeServer.UsesXray)
        {
            if (!File.Exists(awgPath)) throw new FileNotFoundException("Не найден адаптер протокола: " + Path.GetFileName(awgPath));
            using (var listener = new TcpListener(IPAddress.Loopback, 0)) { listener.Start(); awgPort = ((IPEndPoint)listener.LocalEndpoint).Port; }
            awgPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            var awgConfig = activeServer.UsesXray ? XrayConfig.Build(activeServer, awgPort, awgPassword) : await AmneziaConfig.BuildAsync(activeServer, awgPort, awgPassword, ct, activeSettings!.KillSwitch);
            if (diagnostics && activeServer.UsesXray) awgConfig["log"]!["loglevel"] = "info";
            if (activeServer.UsesXray)
            {
                var addresses = await BootstrapDns.ResolveAsync(activeServer.Host, activeSettings!.KillSwitch, ct);
                var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses.FirstOrDefault() ?? throw new IOException("Не найден адрес Xray-сервера.");
                var source = uplink.GetIPProperties().UnicastAddresses.First(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Address;
                XrayConfig.BindTransport(awgConfig, address.ToString(), uplink.Name, source.ToString());
                Log?.Invoke("Xray: транспорт привязан к исходящему сетевому интерфейсу до запуска TUN.");
            }
            else
            {
                var endpoint = awgConfig["uapi"]!.GetValue<string>().Split('\n').Single(line => line.StartsWith("endpoint="))[9..];
                transportAddress = IPEndPoint.Parse(endpoint).Address.ToString();
            }
            awgConfigPath = Path.Combine(SecureStore.DirectoryPath, "awg-runtime.json");
            await File.WriteAllTextAsync(awgConfigPath, awgConfig.ToJsonString(), ct);
            var start = new ProcessStartInfo(awgPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            if (activeServer.UsesXray) start.ArgumentList.Add("run");
            start.ArgumentList.Add(activeServer.UsesXray ? "-config" : "--config"); start.ArgumentList.Add(awgConfigPath);
            awgProcess = Process.Start(start) ?? throw new IOException("Не удалось запустить AmneziaWG."); job.Add(awgProcess);
            var adapterName = activeServer.UsesXray ? "Xray" : "AmneziaWG";
            // Drain stderr immediately: a failed startup must not deadlock on a full pipe.
            awgProcess.ErrorDataReceived += (_, e) => { if (e.Data != null) Log?.Invoke(adapterName + ": сообщение адаптера (содержимое скрыто для защиты ключей)."); };
            awgProcess.BeginErrorReadLine();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(15));
            if (activeServer.UsesXray)
            {
                awgProcess.OutputDataReceived += (_, e) => { if (diagnostics && e.Data != null) Log?.Invoke(Sanitize(e.Data)); }; awgProcess.BeginOutputReadLine();
                while (true)
                {
                    if (awgProcess.HasExited) throw new IOException("Xray отклонил конфигурацию XHTTP.");
                    using var ready = new TcpClient();
                    try { await ready.ConnectAsync(IPAddress.Loopback, awgPort, timeout.Token); break; }
                    catch (SocketException) { await Task.Delay(100, timeout.Token); }
                }
                if (awgProcess.HasExited) throw new IOException("Xray отклонил конфигурацию XHTTP.");
            }
            else
            {
                var ready = await awgProcess.StandardOutput.ReadLineAsync(timeout.Token);
                if (ready != "READY") throw new IOException("AmneziaWG отклонил конфигурацию. Проверьте ключи и параметры обфускации.");
            }
            Log?.Invoke(activeServer.UsesXray ? "Адаптер Xray XHTTP запущен." : "Адаптер AmneziaWG запущен (официальное ядро v3.1).");
            if (activeServer.UsesXray)
            {
                Diagnostic("Проверка VLESS до включения TUN…");
                try { await ProbeProxyAsync(awgPort, "aster", awgPassword, ct); Diagnostic("VLESS до TUN: HTTPS работает."); }
                catch (Exception ex) when (!ct.IsCancellationRequested) { Diagnostic("VLESS до TUN: ошибка " + ex.GetType().Name); }
            }

        }
        var config = ConfigBuilder.Build(activeSettings!, activeServer!, port, password, SecureStore.DirectoryPath, awgPort, awgPassword, awgPath);
        config["route"]!["auto_detect_interface"] = false;
        config["route"]!["default_interface"] = uplink.Name;
        if (activeServer.UsesXray || activeServer.Protocol == "amneziawg")
            config["outbounds"]![0]!["bind_interface"] = NetworkInterface.GetAllNetworkInterfaces().First(n => n.NetworkInterfaceType == NetworkInterfaceType.Loopback).Name;
        // Keep an already-running proxy core's encrypted transport outside this TUN.
        var otherCores = new List<string>();
        foreach (var name in new[] { "xray", "sing-box" })
            foreach (var other in Process.GetProcessesByName(name))
                using (other)
                    try
                    {
                        if (other.Id != awgProcess?.Id && other.MainModule?.FileName is { } path && !string.Equals(path, corePath, StringComparison.OrdinalIgnoreCase)) otherCores.Add(path);
                    }
                    catch (System.ComponentModel.Win32Exception) { }
                    catch (InvalidOperationException) { }
        if (otherCores.Count > 0)
        {
            config["route"]!["rules"]!.AsArray().Insert(1, new System.Text.Json.Nodes.JsonObject { ["process_path"] = SubscriptionParser.Array(otherCores.Distinct()), ["action"] = "route", ["outbound"] = "direct" });
            Log?.Invoke("Транспорт работающего VPN-клиента исключён из Aster, чтобы избежать петли двух туннелей.");
        }
        if (diagnostics) config["log"]!["level"] = "info";
        // AWG uses a route exclusion; Xray binds sockets with IP_UNICAST_IF instead.
        // Do not globally bypass the Xray server IP for unrelated application traffic.
        if (transportAddress != null)
            config["inbounds"]![0]!["route_exclude_address"]!.AsArray().Add(transportAddress + (transportAddress.Contains(':') ? "/128" : "/32"));
        await File.WriteAllTextAsync(configPath, config.ToJsonString(SecureStore.JsonOptions), ct);
        using (var check = NewProcess("check"))
        {
            check.Start(); job.Add(check);
            var stderr = check.StandardError.ReadToEndAsync(ct); var stdout = check.StandardOutput.ReadToEndAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(20));
            try { await check.WaitForExitAsync(timeout.Token); }
            catch { if (!check.HasExited) check.Kill(true); throw; }
            var error = await stderr; await stdout;
            if (check.ExitCode != 0) throw new InvalidOperationException("Ядро отклонило конфигурацию: " + Sanitize(error));
        }
        process = NewProcess("run");
        process.OutputDataReceived += (_, e) => { if (e.Data != null) Log?.Invoke(Sanitize(e.Data)); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) Log?.Invoke(Sanitize(e.Data)); };
        process.Start(); job.Add(process); process.BeginOutputReadLine(); process.BeginErrorReadLine();
        await Task.Delay(1500, ct);
        if (process.HasExited) throw new InvalidOperationException("Ядро завершилось при запуске. Подробности в журнале. Проверьте права администратора и отсутствие другого VPN.");
        if (activeSettings!.KillSwitch)
        {
            killSwitch.Enable(corePath, activeSettings.BypassLan, allowTunnel: true);
            Log?.Invoke("Kill switch включён: выход вне TUN заблокирован; исключения обрабатываются ядром, LAN — по настройке.");
        }
    }
    private Process NewProcess(string command)
    {
        var info = new ProcessStartInfo(corePath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true, WorkingDirectory = Path.GetDirectoryName(corePath)! };
        info.ArgumentList.Add(command); info.ArgumentList.Add("-c"); info.ArgumentList.Add(configPath!);
        return new Process { StartInfo = info };
    }
    private string Sanitize(string text)
    {
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\x1B\[[0-?]*[ -/]*[@-~]", "");
        void Redact(System.Text.Json.Nodes.JsonNode? node)
        {
            if (node is System.Text.Json.Nodes.JsonObject obj)
                foreach (var (key, value) in obj)
                {
                    if (key is "password" or "uuid" or "private_key" or "public_key" or "short_id" or "username" or "id" or "privateKey" or "publicKey" or "shortId")
                    { var secret = value?.ToString(); if (!string.IsNullOrEmpty(secret)) text = text.Replace(secret, "***", StringComparison.Ordinal); }
                    else Redact(value);
                }
            else if (node is System.Text.Json.Nodes.JsonArray array) foreach (var value in array) Redact(value);
        }
        Redact(activeServer?.Outbound);
        if (password.Length > 0) text = text.Replace(password, "***");
        return text.Length > 1500 ? text[..1500] : text;
    }
    private async Task StopCoreAsync()
    {
        var old = process; process = null;
        if (old != null)
        {
            try { if (!old.HasExited) { old.Kill(true); await old.WaitForExitAsync(); } }
            finally { old.Dispose(); }
        }
        if (configPath != null && File.Exists(configPath)) File.Delete(configPath);
        var awg = awgProcess; awgProcess = null;
        if (awg != null)
        {
            try { if (!awg.HasExited) { awg.Kill(true); await awg.WaitForExitAsync(); } }
            finally { awg.Dispose(); }
        }
        if (awgConfigPath != null && File.Exists(awgConfigPath)) File.Delete(awgConfigPath);
    }
    public async Task<long> ProbeAsync(CancellationToken ct = default)
    {
        if (!IsRunning) throw new InvalidOperationException("Сначала подключите VPN.");
        var url = activeSettings?.ProbeUrl ?? "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https") throw new FormatException("Адрес проверки должен начинаться с HTTPS.");
        probeStage = "SOCKS → VLESS/AWG при включённом TUN";
        // Check actual TUN traffic; SOCKS failure alone must not restart a working TUN.
        probeStage = "системный DNS через TUN";
        // SOCKS alone does not exercise TUN. Bind a second request to its address so
        // a working direct connection cannot masquerade as a working VPN interface.
        using var tunHandler = new SocketsHttpHandler
        {
            UseProxy = false, AllowAutoRedirect = false,
            ConnectCallback = async (context, token) =>
            {
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token);
                Diagnostic("Системный DNS: ответ получен.");
                probeStage = "HTTPS из TUN";
                Exception? last = null;
                foreach (var address in addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork))
                {
                    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        socket.Bind(new IPEndPoint(IPAddress.Parse("172.29.0.1"), 0));
                        await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), token);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (Exception ex) { socket.Dispose(); last = ex; token.ThrowIfCancellationRequested(); }
                }
                throw new IOException("Не удалось выполнить HTTPS через системный TUN.", last);
            }
        };
        using var tunClient = new HttpClient(tunHandler) { Timeout = TimeSpan.FromSeconds(12) };
        var result = await ConnectivityCheck.RunAsync(url, async (target, token) =>
        {
            using var response = await tunClient.GetAsync(target, HttpCompletionOption.ResponseHeadersRead, token);
            // Any authenticated HTTPS response proves transport connectivity, including 403/429.
        }, ct);
        Diagnostic("HTTPS из TUN: работает.");
        probeAttempts = result.Attempts;
        Diagnostic($"HTTPS: {result.ElapsedMilliseconds} мс; адресов проверено: {result.Attempts}.");
        return result.ElapsedMilliseconds;
    }
    private async Task ProbeProxyAsync(int proxyPort, string username, string secret, CancellationToken ct)
    {
        var url = activeSettings?.ProbeUrl ?? "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https") throw new FormatException("Адрес проверки должен начинаться с HTTPS.");
        using var handler = new HttpClientHandler { Proxy = new WebProxy($"socks5://127.0.0.1:{proxyPort}") { Credentials = new NetworkCredential(username, secret) }, UseProxy = true, AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
    }
    private void Diagnostic(string message)
    {
        Log?.Invoke(message);
        // Only our fixed stage labels are persisted: no raw core logs, URLs or credentials.
        try
        {
            var path = Path.Combine(SecureStore.DirectoryPath, "connection-diagnostics.log");
            if (File.Exists(path) && new FileInfo(path).Length > 65536) File.WriteAllText(path, "");
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    private static string FailureKind(Exception error)
    {
        for (Exception? current = error; current != null; current = current.InnerException)
        {
            if (current is SocketException socket) return "socket=" + socket.SocketErrorCode;
            if (current is HttpRequestException { StatusCode: { } code }) return "HTTP=" + (int)code;
        }
        return error is OperationCanceledException ? "timeout/cancel" : error.GetType().Name;
    }
    public static async Task<string> PingAsync(ServerProfile server)
    {
        // ICMP measures host reachability, not the UDP protocol handshake.
        if (server.IsEndpoint || server.Protocol is "amneziawg" or "hysteria" or "hysteria2" or "tuic")
        {
            var host = server.Outbound["peers"]?[0]?["address"]?.GetValue<string>() ?? server.Host;
            if (string.IsNullOrWhiteSpace(host)) return "Нет адреса";
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var addresses = await Dns.GetHostAddressesAsync(host, timeout.Token);
                var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses.FirstOrDefault();
                if (address == null) return "Адрес не найден";
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(address, 3000).WaitAsync(timeout.Token);
                return reply.Status == IPStatus.Success ? $"{reply.RoundtripTime} мс ICMP" : "ICMP не ответил";
            }
            catch { return "ICMP недоступен"; }
        }
        if (server.Port < 1 || server.Host.Length == 0) return "Нет TCP-адреса";
        try
        {
            using var client = new TcpClient(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var watch = Stopwatch.StartNew(); await client.ConnectAsync(server.Host, server.Port, timeout.Token);
            return $"{watch.ElapsedMilliseconds} мс TCP";
        }
        catch { return "TCP не ответил за 5 с"; }
    }
    private async Task WatchAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (!await gate.WaitAsync(0, ct)) continue;
                try
                {
                    if (!wanted || activeSettings == null) continue;
                    string currentUplink;
                    try { currentUplink = PhysicalUplink.Fingerprint(PhysicalUplink.Find()); }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NetworkInformationException)
                    {
                        Report(ConnectionState.Degraded, "Ожидание Ethernet/Wi-Fi · восстановление продолжится при появлении сети");
                        continue;
                    }
                    var networkChanged = uplinkFingerprint != null && currentUplink != uplinkFingerprint;
                    if (networkChanged && activeSettings.AutoReconnect)
                    {
                        nextRetry = DateTimeOffset.MinValue;
                        Diagnostic("Исходящая сеть изменилась; требуется новая привязка транспорта.");
                    }
                    if (IsRunning && !(networkChanged && activeSettings.AutoReconnect))
                    {
                        using var monitor = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        connectionAttempt = monitor;
                        try { var ms = await ProbeAsync(monitor.Token); failures = restarts = 0; Report(ConnectionState.Connected, ConnectedStatus(ms)); continue; }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                        catch (OperationCanceledException) when (monitor.IsCancellationRequested) { continue; }
                        catch (Exception ex) { failures++; Diagnostic("Проверка не прошла: " + probeStage + "; " + FailureKind(ex)); Report(ConnectionState.Degraded, $"Нет ответа проверки · {failures}/3"); }
                        finally { connectionAttempt = null; }
                        if (failures < 3) continue;
                    }
                    if (!activeSettings.AutoReconnect) { Report(ConnectionState.Degraded, "Соединение недоступно · автоподключение выключено"); continue; }
                    if (DateTimeOffset.UtcNow < nextRetry) continue;
                    restarts = Math.Min(restarts + 1, 1000000);
                    Report(ConnectionState.Recovering, $"Восстановление · попытка {restarts}");
                    await StopCoreAsync();
                    using var retry = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    retry.CancelAfter(TimeSpan.FromSeconds(90));
                    connectionAttempt = retry;
                    try
                    {
                        if (activeServer?.UsesXray == true && activeSettings.AutoUpdate)
                        {
                            activeServer = await SubscriptionService.RefreshForRecoveryAsync(activeSettings, activeServer, retry.Token,
                                onCachedFallback: () => Diagnostic("Подписка временно недоступна; восстановление с сохранённым VLESS-профилем."));
                        }
                        await StartCoreAsync(retry.Token); failures = 0;
                        Report(ConnectionState.Checking, "Туннель перезапущен · проверка интернета…");
                        var ms = await ProbeAsync(retry.Token);
                        restarts = 0;
                        Report(ConnectionState.Connected, ConnectedStatus(ms));
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested) { if (!IsRunning) await StopCoreAsync(); else failures = 1; Report(ConnectionState.Degraded, "Ожидание следующей попытки восстановления"); Diagnostic("Восстановление не удалось: " + FailureKind(ex)); }
                    finally { connectionAttempt = null; }
                    nextRetry = DateTimeOffset.UtcNow.AddSeconds(RecoveryPolicy.RetryDelaySeconds(restarts));
                }
                catch (Exception) when (!ct.IsCancellationRequested) { Status?.Invoke("Ошибка контроля соединения"); }
                finally { gate.Release(); }
            }
        }
        catch (OperationCanceledException) { }
    }
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel(); await watchdog;
        try { await DisconnectAsync(); }
        finally { killSwitch.Dispose(); job.Dispose(); lifetime.Dispose(); gate.Dispose(); }
    }
}
