using Microsoft.Win32;
using System.Security.Cryptography;
using System.Text;
using System.Net.Http;
namespace AsterVPN;

public static class DeviceIdentity
{
    private static readonly Lazy<string> identity = new(() =>
    {
        using var registry = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = registry.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        var seed = key?.GetValue("MachineGuid") as string;
        if (string.IsNullOrWhiteSpace(seed)) throw new InvalidOperationException("Не удалось прочитать идентификатор Windows для HWID.");
        return Derive(seed);
    });
    public static string Hwid => identity.Value;
    public static string Derive(string seed)
    {
        var hex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("AsterVPN:HWID:v1:" + seed.Trim().ToLowerInvariant())));
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..32]}";
    }
    public static void AddHeaders(HttpRequestMessage request, string hwid)
    {
        request.Headers.Add("x-hwid", hwid);
        request.Headers.Add("x-device-os", "Windows");
        request.Headers.Add("x-ver-os", Environment.OSVersion.Version.ToString());
        request.Headers.Add("x-device-model", "Windows PC");
        request.Headers.Add("x-client", "AsterVPN");
        request.Headers.Add("x-app-version", ApplicationVersion.Current);
    }
}
