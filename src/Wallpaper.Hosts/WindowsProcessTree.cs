using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Wallpaper.Hosts;

internal static class WindowsProcessTree
{
    private const uint SnapshotProcess = 0x00000002;
    private static readonly nint InvalidHandleValue = new(-1);

    public static string? TryGetParentProcessName()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            var parentProcessId = TryGetParentProcessId(Environment.ProcessId);
            if (parentProcessId is null)
            {
                return null;
            }

            using var parent = Process.GetProcessById(parentProcessId.Value);
            return parent.ProcessName;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    internal static int? TryGetParentProcessId(int processId)
    {
        var snapshot = CreateToolhelp32Snapshot(SnapshotProcess, 0);
        if (snapshot == InvalidHandleValue)
        {
            return null;
        }

        try
        {
            var entry = new ProcessEntry32
            {
                Size = (uint)Marshal.SizeOf<ProcessEntry32>(),
            };
            if (!Process32First(snapshot, ref entry))
            {
                return null;
            }

            do
            {
                if (entry.ProcessId == (uint)processId)
                {
                    return checked((int)entry.ParentProcessId);
                }
            }
            while (Process32Next(snapshot, ref entry));

            return null;
        }
        finally
        {
            _ = CloseHandle(snapshot);
        }
    }

#pragma warning disable SYSLIB1054
    [DllImport("kernel32.dll", SetLastError = true)]
    [SupportedOSPlatform("windows")]
    private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SupportedOSPlatform("windows")]
    private static extern bool Process32First(nint snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SupportedOSPlatform("windows")]
    private static extern bool Process32Next(nint snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SupportedOSPlatform("windows")]
    private static extern bool CloseHandle(nint handle);
#pragma warning restore SYSLIB1054

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public nuint DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int BasePriority;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExecutableFile;
    }
}
