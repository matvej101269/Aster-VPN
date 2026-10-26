using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AsterVPN;

public static class AppUpdater
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(15) };
    public sealed record Update(string Version, string Name, string Url, string Digest, long Size);
    public static async Task<Update?> CheckAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/matvej101269/Aster-VPN/releases?per_page=100");
        request.Headers.UserAgent.ParseAdd("AsterVPN/" + ApplicationVersion.Current);
        using var response = await Client.SendAsync(request, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        Update? newest = null;
        foreach (var release in json.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) continue;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                var match = Regex.Match(name, @"^AsterVPN-Setup-(\d+\.\d+\.\d+)-x64\.exe$");
                if (!match.Success || !Version.TryParse(match.Groups[1].Value, out var version) || version <= Version.Parse(ApplicationVersion.Current)) continue;
                var url = asset.GetProperty("browser_download_url").GetString() ?? "";
                var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() ?? "" : "";
                long size = asset.GetProperty("size").GetInt64();
                if (!url.StartsWith("https://github.com/matvej101269/Aster-VPN/releases/download/", StringComparison.Ordinal) || !Regex.IsMatch(digest, "^sha256:[a-f0-9]{64}$") || size <= 0 || size > 1_073_741_824) continue;
                if (newest == null || version > Version.Parse(newest.Version)) newest = new(version.ToString(3), name, url, digest[7..], size);
            }
        }
        return newest;
    }
    public sealed record DownloadProgress(long Received, long Total, bool Verifying = false);
    public static async Task<string> DownloadAsync(Update update, IProgress<DownloadProgress>? progress = null, CancellationToken cancellation = default)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AsterVPN", "updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, update.Name);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromMinutes(15));
            progress?.Report(new(0, update.Size));
            using var response = await Client.GetAsync(update.Url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920]; long total = 0; int count;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while ((count = await input.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    total += count;
                    if (total > update.Size) throw new IOException("Размер обновления не совпадает.");
                    await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
                    if (clock.ElapsedMilliseconds >= 100 || total == update.Size) { progress?.Report(new(total, update.Size)); clock.Restart(); }
                }
                if (total != update.Size) throw new IOException("Обновление загружено не полностью.");
            }
            progress?.Report(new(update.Size, update.Size, true));
            await using var file = File.OpenRead(path);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, timeout.Token));
            if (!hash.Equals(update.Digest, StringComparison.OrdinalIgnoreCase)) throw new IOException("SHA-256 обновления не совпадает.");
            return path;
        }
        catch { File.Delete(path); throw; }
    }
}
