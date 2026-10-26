using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace AsterVPN;

/// <summary>Own WFP sublayer only. Persistent filters survive a client/core crash.</summary>
public sealed class KillSwitch : IDisposable
{
    private static readonly Guid SublayerKey = new("379f2f76-78f2-46ab-b052-862977a650ec");
    private static readonly Guid[] Layers = [new("c38d57d1-05a7-4c33-904f-7fbceee60e82"), new("4a72393b-319f-44bc-84c3-ba54dcb3b6b4")];
    private static readonly Guid AppId = new("d78e1e87-8644-4ea5-9437-d809ecefc971");
    private static readonly Guid LocalInterface = new("4cd62a49-59c3-4969-b7f3-bda5d32890a4");
    private static readonly Guid RemoteAddress = new("b235ae9a-1d64-49b8-a44c-5ff3d9095045");
    private static readonly Guid Flags = new("632ce23b-5167-435c-86d7-e903684aa80c");
    private static readonly Guid Protocol = new("3971ef2b-623e-4f9a-8cb1-6e79b806b9a7");
    private static readonly Guid RemotePort = new("c35a604d-d22b-4e1a-91b4-68f674ee674b");
    private IntPtr engine;
    public bool IsEnabled { get; private set; }
    private int nextKey;
    private readonly List<IntPtr> allocations = [];

    private static Guid Key(int index) => new($"6d959e0e-ccfb-4cd8-9c92-{index:000000000000}");
    private void Open()
    {
        if (engine == IntPtr.Zero) Check(FwpmEngineOpen0(null, 10, IntPtr.Zero, IntPtr.Zero, out engine));
    }
    public bool Restore(string corePath, bool configured)
    {
        Open();
        var result = FwpmFilterGetByKey0(engine, Key(0), out var filter);
        if (result == 0) FwpmFreeMemory0(ref filter);
        else if (result != 0x80320003) Check(result);
        if (result == 0 && configured) Enable(corePath, bypassLan: false);
        else Disable();
        return IsEnabled;
    }
    public void Enable(string corePath, bool bypassLan, bool allowTunnel = false, bool validateOnly = false)
    {
        Open();
        Check(FwpmTransactionBegin0(engine, 0));
        try
        {
            DeleteFilters();
            var sublayer = new Sublayer { Key = SublayerKey, Display = new() { Name = "Aster VPN Kill switch" }, Flags = 1, Weight = 0x100 };
            var result = FwpmSubLayerAdd0(engine, ref sublayer, IntPtr.Zero);
            if (result != 0x80320009) Check(result); // FWP_E_ALREADY_EXISTS
            nextKey = 0;
            var paths = new[] { Environment.ProcessPath!, corePath, Path.Combine(Path.GetDirectoryName(corePath)!, "xray.exe"), Path.Combine(Path.GetDirectoryName(corePath)!, "aster-awg.exe") };
            ulong? luid = null;
            if (allowTunnel || validateOnly)
            {
                var tun = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => validateOnly
                    ? n.NetworkInterfaceType == NetworkInterfaceType.Loopback
                    : n.Name == "AsterVPN" && n.GetIPProperties().UnicastAddresses.Any(a => a.Address.Equals(IPAddress.Parse("172.29.0.1"))));
                if (tun == null) throw new IOException("Kill switch: интерфейс AsterVPN не найден.");
                Check(ConvertInterfaceIndexToLuid((uint)tun.GetIPProperties().GetIPv4Properties().Index, out var value));
                luid = value;
            }
            for (var family = 0; family < Layers.Length; family++)
            {
                var layer = Layers[family];
                foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists))
                {
                    Check(FwpmGetAppIdFromFileName0(path, out var blob));
                    try { Add(layer, true, new Condition { Field = AppId, Value = new() { Type = 12, Pointer = blob } }); }
                    finally { FwpmFreeMemory0(ref blob); }
                }
                // Loopback is needed for the local authenticated SOCKS adapters.
                Add(layer, true, new Condition { Field = Flags, Match = 6, Value = new() { Type = 3, Pointer = (IntPtr)1 } });
                if (luid.HasValue)
                    Add(layer, true, new Condition { Field = LocalInterface, Value = new() { Type = 4, Pointer = Allocate(luid.Value) } });
                if (bypassLan)
                    foreach (var cidr in family == 0 ? new[] { "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16" } : new[] { "fc00::/7", "fe80::/10" })
                        Add(layer, true, Address(cidr));
                // Address renewal must work even while recovery is blocked.
                Add(layer, true,
                    new() { Field = Protocol, Value = new() { Type = 1, Pointer = (IntPtr)17 } },
                    new() { Field = RemotePort, Value = new() { Type = 2, Pointer = (IntPtr)(family == 0 ? 67 : 547) } });
                Add(layer, false);
            }
            if (validateOnly) Check(FwpmTransactionAbort0(engine));
            else { Check(FwpmTransactionCommit0(engine)); IsEnabled = true; }
        }
        catch { FwpmTransactionAbort0(engine); throw; }
        finally { FreeAllocations(); }
    }
    public void Disable()
    {
        Open();
        Check(FwpmTransactionBegin0(engine, 0));
        try
        {
            DeleteFilters();
            var result = FwpmSubLayerDeleteByKey0(engine, SublayerKey);
            if (result != 0x80320007) Check(result); // FWP_E_SUBLAYER_NOT_FOUND
            Check(FwpmTransactionCommit0(engine));
            IsEnabled = false;
        }
        catch { FwpmTransactionAbort0(engine); throw; }
    }
    private void DeleteFilters()
    {
        for (var i = 0; i < 64; i++)
        {
            var result = FwpmFilterDeleteByKey0(engine, Key(i));
            if (result != 0x80320003) Check(result); // FWP_E_FILTER_NOT_FOUND
        }
    }
    private void Add(Guid layer, bool permit, params Condition[] conditions)
    {
        if (nextKey >= 64) throw new InvalidOperationException("Слишком много правил kill switch.");
        var pointer = IntPtr.Zero;
        if (conditions.Length > 0)
        {
            pointer = Marshal.AllocHGlobal(Marshal.SizeOf<Condition>() * conditions.Length);
            allocations.Add(pointer);
            for (var i = 0; i < conditions.Length; i++) Marshal.StructureToPtr(conditions[i], IntPtr.Add(pointer, i * Marshal.SizeOf<Condition>()), false);
        }
        var filter = new Filter
        {
            Key = Key(nextKey++), Display = new() { Name = permit ? "Aster VPN: разрешение" : "Aster VPN: блокировка вне туннеля" },
            Flags = 1, Layer = layer, Sublayer = SublayerKey,
            Weight = new() { Type = 1, Pointer = (IntPtr)(permit ? 15 : 0) },
            Count = (uint)conditions.Length, Conditions = pointer,
            Action = new() { Type = permit ? 0x1002u : 0x1001u }
        };
        Check(FwpmFilterAdd0(engine, ref filter, IntPtr.Zero, out _));
    }
    private Condition Address(string cidr)
    {
        var parts = cidr.Split('/');
        var address = IPAddress.Parse(parts[0]);
        var prefix = int.Parse(parts[1]);
        IntPtr pointer;
        uint type;
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            pointer = Allocate(new V4Mask { Address = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes), Mask = uint.MaxValue << (32 - prefix) });
            type = 256;
        }
        else { pointer = Allocate(new V6Mask { Address = address.GetAddressBytes(), Prefix = (byte)prefix }); type = 257; }
        return new() { Field = RemoteAddress, Value = new() { Type = type, Pointer = pointer } };
    }
    private IntPtr Allocate<T>(T value) where T : struct
    {
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
        allocations.Add(pointer); Marshal.StructureToPtr(value, pointer, false); return pointer;
    }
    private void FreeAllocations() { foreach (var pointer in allocations) Marshal.FreeHGlobal(pointer); allocations.Clear(); }
    private static void Check(uint error)
    {
        if (error != 0) throw new Win32Exception(unchecked((int)error), $"Kill switch: Windows WFP отклонил операцию (0x{error:X8}).");
    }
    // Dispose only closes the management handle; failures retain the block.
    public void Dispose() { if (engine != IntPtr.Zero) { FwpmEngineClose0(engine); engine = IntPtr.Zero; } }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct Display { [MarshalAs(UnmanagedType.LPWStr)] public string? Name; [MarshalAs(UnmanagedType.LPWStr)] public string? Description; }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public uint Size; public IntPtr Data; }
    [StructLayout(LayoutKind.Sequential)] private struct Value { public uint Type; public IntPtr Pointer; }
    [StructLayout(LayoutKind.Sequential)] private struct Condition { public Guid Field; public uint Match; public Value Value; }
    [StructLayout(LayoutKind.Sequential)] private struct Action { public uint Type; public Guid Key; }
    [StructLayout(LayoutKind.Explicit, Size = 16)] private struct Context { [FieldOffset(0)] public ulong Raw; [FieldOffset(0)] public Guid Key; }
    [StructLayout(LayoutKind.Sequential)] private struct Sublayer { public Guid Key; public Display Display; public uint Flags; public IntPtr Provider; public Blob Data; public ushort Weight; }
    [StructLayout(LayoutKind.Sequential)] private struct Filter
    {
        public Guid Key; public Display Display; public uint Flags; public IntPtr Provider; public Blob Data;
        public Guid Layer; public Guid Sublayer; public Value Weight; public uint Count; public IntPtr Conditions;
        public Action Action; public Context Context; public IntPtr Reserved; public ulong Id; public Value EffectiveWeight;
    }
    [StructLayout(LayoutKind.Sequential)] private struct V4Mask { public uint Address; public uint Mask; }
    [StructLayout(LayoutKind.Sequential)] private struct V6Mask { [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] Address; public byte Prefix; }
    [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode)] private static extern uint FwpmEngineOpen0(string? server, uint auth, IntPtr identity, IntPtr session, out IntPtr handle);
    [DllImport("fwpuclnt.dll")] private static extern uint FwpmEngineClose0(IntPtr handle);
    [DllImport("fwpuclnt.dll")] private static extern uint FwpmTransactionBegin0(IntPtr handle, uint flags);
    [DllImport("fwpuclnt.dll")] private static extern uint FwpmTransactionCommit0(IntPtr handle);
    [DllImport("fwpuclnt.dll")] private static extern uint FwpmTransactionAbort0(IntPtr handle);
    [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode)] private static extern uint FwpmSubLayerAdd0(IntPtr handle, ref Sublayer sublayer, IntPtr security);
    [DllImport("fwpuclnt.dll")] private static extern uint FwpmSubLayerDeleteByKey0(IntPtr handle, in Guid key);
    [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode)] private static extern uint FwpmFilterAdd0(IntPtr handle, ref Filter filter, IntPtr security, out ulong id);
    [DllImport("fwpuclnt.dll")] private static extern uint FwpmFilterDeleteByKey0(IntPtr handle, in Guid key);
    [DllImport("fwpuclnt.dll")] private static extern uint FwpmFilterGetByKey0(IntPtr handle, in Guid key, out IntPtr filter);
    [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode)] private static extern uint FwpmGetAppIdFromFileName0(string file, out IntPtr appId);
    [DllImport("fwpuclnt.dll")] private static extern void FwpmFreeMemory0(ref IntPtr memory);
    [DllImport("iphlpapi.dll")] private static extern uint ConvertInterfaceIndexToLuid(uint index, out ulong luid);
}
