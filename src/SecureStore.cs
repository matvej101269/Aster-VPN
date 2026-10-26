using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
namespace AsterVPN;
public static class SecureStore
{
    public static string DirectoryPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AsterVPN");
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Default) { WriteIndented = true };
    public static void Prepare()
    {
        Directory.CreateDirectory(DirectoryPath);
        var acl = new DirectorySecurity();
        acl.SetAccessRuleProtection(true, false);
        var user = WindowsIdentity.GetCurrent().User!;
        foreach (var sid in new[] { user, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) })
            acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(DirectoryPath).SetAccessControl(acl);
    }
    public static Settings Load()
    {
        Prepare();
        foreach (var name in new[] { "runtime.json", "awg-runtime.json" })
        {
            var stale = Path.Combine(DirectoryPath, name);
            if (File.Exists(stale)) File.Delete(stale);
        }
        var path = Path.Combine(DirectoryPath, "settings.bin");
        return File.Exists(path) ? JsonSerializer.Deserialize<Settings>(Crypt(File.ReadAllBytes(path), false)) ?? new() : new();
    }
    public static void Save(Settings settings)
    {
        Prepare();
        var path = Path.Combine(DirectoryPath, "settings.bin");
        File.WriteAllBytes(path + ".tmp", Crypt(JsonSerializer.SerializeToUtf8Bytes(settings), true));
        File.Move(path + ".tmp", path, true);
    }
    public static byte[] Decrypt(byte[] bytes) => Crypt(bytes, false);
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool CryptProtectData(ref Blob data, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)] private static extern bool CryptUnprotectData(ref Blob data, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
    private static byte[] Crypt(byte[] bytes, bool protect)
    {
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var ok = protect ? CryptProtectData(ref input, "AsterVPN", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output) : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally { Marshal.FreeHGlobal(input.Data); if (output.Data != IntPtr.Zero) LocalFree(output.Data); }
    }
}
