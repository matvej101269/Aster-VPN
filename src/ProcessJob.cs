using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
namespace AsterVPN;
internal sealed class ProcessJob : IDisposable
{
    private IntPtr handle;
    [StructLayout(LayoutKind.Sequential)] private struct Basic { public long ProcessTime, JobTime; public uint Flags; public UIntPtr MinWorking, MaxWorking; public uint ActiveLimit; public UIntPtr Affinity; public uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)] private struct Io { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct Extended { public Basic Basic; public Io Io; public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(IntPtr job, int kind, ref Extended information, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    public ProcessJob()
    {
        handle = CreateJobObject(IntPtr.Zero, null);
        var info = new Extended { Basic = new Basic { Flags = 0x2000 } }; // KILL_ON_JOB_CLOSE
        if (handle == IntPtr.Zero || !SetInformationJobObject(handle, 9, ref info, (uint)Marshal.SizeOf<Extended>())) { Dispose(); throw new Win32Exception(); }
    }
    public void Add(Process process) { if (!AssignProcessToJobObject(handle, process.Handle)) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    public void Dispose() { if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; } }
}
