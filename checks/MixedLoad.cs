using System.Diagnostics;
using System.Net;

internal static class MixedLoad
{
    public static async Task<bool> RunAsync(int port, string user, string password, int dnsPort)
    {
        // Separate handlers model independent applications and their connection pools.
        HttpClient Client() => new(new SocketsHttpHandler
        {
            UseProxy = true,
            Proxy = new WebProxy($"socks5://127.0.0.1:{port}") { Credentials = new NetworkCredential(user, password) },
            ConnectTimeout = TimeSpan.FromSeconds(15)
        }) { Timeout = Timeout.InfiniteTimeSpan };
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        var started = Stopwatch.StartNew();
        var allGood = 1;
        Console.WriteLine("  Mixed load: 4 long downloads (32 MB each), 2 independent short-request clients, 100 s budget");
        var downloads = Enumerable.Range(0, 4).Select(async index =>
        {
            using var client = Client();
            var watch = Stopwatch.StartNew(); long bytes = 0; long maxPause = 0;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://dl.google.com/chrome/install/ChromeStandaloneSetup64.exe");
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 31_999_999);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token);
                if (response.StatusCode != HttpStatusCode.PartialContent) throw new IOException("CDN range rejected");
                using var body = await response.Content.ReadAsStreamAsync(budget.Token);
                var buffer = new byte[65536];
                while (bytes < 32_000_000)
                {
                    var before = watch.ElapsedMilliseconds;
                    var count = await body.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, 32_000_000 - bytes)), budget.Token);
                    maxPause = Math.Max(maxPause, watch.ElapsedMilliseconds - before);
                    if (count == 0) break;
                    bytes += count;
                }
                if (bytes != 32_000_000) throw new IOException("incomplete body");
                Console.WriteLine($"  App download {index + 1}: COMPLETE, {bytes} bytes, {watch.Elapsed.TotalSeconds:F1}s, {bytes * 8 / watch.Elapsed.TotalSeconds / 1e6:F1} Mbps, max read pause {maxPause} ms");
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref allGood, 0);
                Console.WriteLine($"  App download {index + 1}: {ex.GetType().Name}, {bytes} bytes, {watch.Elapsed.TotalSeconds:F1}s, max read pause {maxPause} ms");
            }
        }).ToArray();
        var shortClients = Enumerable.Range(0, 2).Select(async index =>
        {
            using var client = Client();
            var latency = new List<long>(); var failed = 0;
            while (started.Elapsed < TimeSpan.FromSeconds(60) && !budget.IsCancellationRequested)
            {
                var watch = Stopwatch.StartNew();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(12));
                try
                {
                    using var response = await client.GetAsync(index == 0 ? "https://www.gstatic.com/generate_204" : "https://cp.cloudflare.com/generate_204", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                    response.EnsureSuccessStatusCode(); latency.Add(watch.ElapsedMilliseconds);
                }
                catch { failed++; }
                try { await Task.Delay(1000, budget.Token); } catch (OperationCanceledException) { break; }
            }
            if (failed > 0) Interlocked.Exchange(ref allGood, 0);
            var ordered = latency.Order().ToArray();
            Console.WriteLine($"  App interactive {index + 1}: {ordered.Length} OK, {failed} failed, p95={(ordered.Length == 0 ? -1 : ordered[(int)Math.Ceiling(ordered.Length * .95) - 1])} ms, max={ordered.LastOrDefault(-1)} ms");
        }).ToArray();
        var dnsChecks = Task.Run(async () =>
        {
            var success = 0; var failed = 0; long max = 0;
            while (started.Elapsed < TimeSpan.FromSeconds(60) && !budget.IsCancellationRequested)
            {
                var watch = Stopwatch.StartNew();
                try
                {
                    using var socket = new System.Net.Sockets.UdpClient();
                    socket.Connect(IPAddress.Loopback, dnsPort);
                    var query = new List<byte> { 0x32, 0x12, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0 };
                    foreach (var label in new[] { "www", success % 2 == 0 ? "google" : "cloudflare", "com" })
                    { query.Add((byte)label.Length); query.AddRange(System.Text.Encoding.ASCII.GetBytes(label)); }
                    query.AddRange(new byte[] { 0, 0, 1, 0, 1 });
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(budget.Token); timeout.CancelAfter(TimeSpan.FromSeconds(8));
                    await socket.SendAsync(query.ToArray(), timeout.Token);
                    var answer = (await socket.ReceiveAsync(timeout.Token)).Buffer;
                    if (answer.Length < 12 || answer[0] != 0x32 || answer[1] != 0x12 || (answer[3] & 15) != 0) throw new IOException("DNS failure");
                    success++; max = Math.Max(max, watch.ElapsedMilliseconds);
                }
                catch { failed++; }
                try { await Task.Delay(1000, budget.Token); } catch (OperationCanceledException) { break; }
            }
            if (failed > 0) Interlocked.Exchange(ref allGood, 0);
            Console.WriteLine($"  Uncached encrypted DNS under load: {success} OK, {failed} failed, max={max} ms");
        });
        await Task.WhenAll(downloads.Concat(shortClients).Append(dnsChecks));
        Console.WriteLine($"  Mixed load finished in {started.Elapsed.TotalSeconds:F1}s");
        return allGood == 1;
    }
}
