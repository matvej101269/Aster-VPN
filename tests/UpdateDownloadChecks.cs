using AsterVPN;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

internal static class UpdateDownloadChecks
{
    private sealed class Reporter(Action<AppUpdater.DownloadProgress> report) : IProgress<AppUpdater.DownloadProgress>
    { public void Report(AppUpdater.DownloadProgress value) => report(value); }
    internal static async Task RunAsync()
    {
        byte[] body = new byte[262144]; Random.Shared.NextBytes(body);
        var digest = Convert.ToHexString(SHA256.HashData(body));
        for (var scenario = 0; scenario < 3; scenario++)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            var server = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync();
                await using var stream = client.GetStream();
                var request = new byte[8192]; await stream.ReadAsync(request);
                try
                {
                    await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"));
                    await stream.WriteAsync(body);
                }
                catch (System.IO.IOException) { }
            });
            using var cancel = new CancellationTokenSource();
            var reports = new List<AppUpdater.DownloadProgress>();
            var reporter = new Reporter(value => { reports.Add(value); if (scenario == 2 && value.Received > 0) cancel.Cancel(); });
            var update = new AppUpdater.Update("9.0.0", "test.exe", $"http://127.0.0.1:{endpoint.Port}/update", scenario == 1 ? new string('0', 64) : digest, body.Length);
            string? path = null;
            try
            {
                path = await AppUpdater.DownloadAsync(update, reporter, cancel.Token);
                if (scenario != 0) throw new Exception("Invalid or cancelled update was accepted.");
                if (!(await File.ReadAllBytesAsync(path)).SequenceEqual(body) || !reports.Any(x => x.Received == body.Length) || !reports.Any(x => x.Verifying))
                    throw new Exception("Download content or progress is incorrect.");
            }
            catch (System.IO.IOException) when (scenario == 1) { }
            catch (OperationCanceledException) when (scenario == 2) { }
            finally { if (path != null) { File.Delete(path); Directory.Delete(Path.GetDirectoryName(path)!); } }
            await server;
        }
        Console.WriteLine("PASS Update download: byte progress, SHA-256 rejection and cancellation");
    }
}
