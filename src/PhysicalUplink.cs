using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace AsterVPN;

public static class PhysicalUplink
{
    public static string Fingerprint(NetworkInterface network) => network.Id + "|" +
        string.Join(",", network.GetIPProperties().UnicastAddresses
            .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            .Select(a => a.Address.ToString()).OrderBy(a => a, StringComparer.Ordinal)) + "|" +
        string.Join(",", network.GetIPProperties().GatewayAddresses
            .Select(g => g.Address.ToString()).OrderBy(a => a, StringComparer.Ordinal));

    [DllImport("iphlpapi.dll", SetLastError = false)]
    private static extern uint GetIpForwardTable(IntPtr table, ref uint size, bool order);

    public static bool IsPhysical(NetworkInterfaceType type, string name, string description) =>
        type is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 &&
        !new[] { "tunnel", "wintun", "wireguard", "tap-windows", "amnezia", "happ", "astervpn", "sing-box", "incy" }
            .Any(s => (name + " " + description).Contains(s, StringComparison.OrdinalIgnoreCase));

    public static NetworkInterface Find()
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces().Where(n =>
            n.OperationalStatus == OperationalStatus.Up && IsPhysical(n.NetworkInterfaceType, n.Name, n.Description) &&
            n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
            .ToDictionary(n => (uint)n.GetIPProperties().GetIPv4Properties().Index);
        uint size = 0;
        var result = GetIpForwardTable(IntPtr.Zero, ref size, false);
        if (result != 122) throw new Win32Exception((int)result);
        var buffer = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            result = GetIpForwardTable(buffer, ref size, false);
            if (result != 0) throw new Win32Exception((int)result);
            // MIB_IPFORWARDROW consists of fourteen DWORDs; metric1 includes interface metric.
            var count = Marshal.ReadInt32(buffer);
            uint bestMetric = uint.MaxValue;
            NetworkInterface? best = null;
            for (var i = 0; i < count; i++)
            {
                var row = IntPtr.Add(buffer, 4 + i * 56);
                if (Marshal.ReadInt32(row, 0) != 0 || Marshal.ReadInt32(row, 4) != 0) continue;
                var index = unchecked((uint)Marshal.ReadInt32(row, 16));
                var metric = unchecked((uint)Marshal.ReadInt32(row, 36));
                if (candidates.TryGetValue(index, out var candidate) && metric < bestMetric)
                { best = candidate; bestMetric = metric; }
            }
            return best ?? throw new InvalidOperationException("Не найден физический Ethernet/Wi-Fi с маршрутом в интернет.");
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
}
