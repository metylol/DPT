using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ValorantStretch;

internal static class NativeMonitorDevices
{
    internal sealed record Device(string InstanceId, string Name, string[] HardwareIds, uint Problem);
    private static readonly Guid MonitorClass = new("4d36e96e-e325-11ce-bfc1-08002be10318");
    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfo { public uint Size; public Guid ClassGuid; public uint DevInst; public UIntPtr Reserved; }

    internal static List<Device> Enumerate()
    {
        var guid = MonitorClass;
        var set = SetupDiGetClassDevsW(ref guid, IntPtr.Zero, IntPtr.Zero, 2); // DIGCF_PRESENT
        if (set == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var result = new List<Device>();
            for (uint i = 0; ; i++)
            {
                var info = new DeviceInfo { Size = (uint)Marshal.SizeOf<DeviceInfo>() };
                if (!SetupDiEnumDeviceInfo(set, i, ref info))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == 259) break;
                    throw new Win32Exception(error);
                }
                var id = new StringBuilder(512);
                if (!SetupDiGetDeviceInstanceIdW(set, ref info, id, (uint)id.Capacity, out _))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                var code = CM_Get_DevNode_Status(out _, out var problem, info.DevInst, 0);
                if (code != 0) throw new IOException($"Cannot read monitor state (CM 0x{code:X}).");
                var name = Property(set, ref info, 12).FirstOrDefault() ?? Property(set, ref info, 0).FirstOrDefault() ?? "Monitor";
                result.Add(new Device(id.ToString(), name, Property(set, ref info, 1), problem));
            }
            return result;
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }

    private static string[] Property(IntPtr set, ref DeviceInfo info, uint key)
    {
        SetupDiGetDeviceRegistryPropertyW(set, ref info, key, out _, null, 0, out var size);
        if (size == 0) return [];
        var bytes = new byte[size];
        if (!SetupDiGetDeviceRegistryPropertyW(set, ref info, key, out _, bytes, size, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return Encoding.Unicode.GetString(bytes).Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    internal static bool SetEnabled(string id, bool enabled, out string error)
    {
        try { return SetEnabled(id, enabled, Enumerate(), out error); }
        catch (Exception ex) when (ex is Win32Exception or IOException) { error = ex.Message; return false; }
    }

    internal static bool SetEnabled(string id, bool enabled, IReadOnlyCollection<Device> devices, out string error)
    {
        error = "";
        try
        {
            // Use a fresh monitor-class snapshot, shared for the whole apply/restore batch.
            var device = devices.FirstOrDefault(d => string.Equals(d.InstanceId, id, StringComparison.OrdinalIgnoreCase));
            if (device is null) { error = "Monitor device is no longer present."; return false; }
            if ((device.Problem != 22) == enabled) return true;
            var code = CM_Locate_DevNodeW(out var node, id, 0);
            if (code != 0) { error = $"Cannot locate monitor (CM 0x{code:X})."; return false; }
            // No error dialogs; preserve disabled state across restarts, like Device Manager.
            code = enabled ? CM_Enable_DevNode(node, 0) : CM_Disable_DevNode(node, 0x1 | 0x2);
            if (code != 0) { error = $"Windows rejected the device change (CM 0x{code:X})."; return false; }
            var watch = Stopwatch.StartNew();
            do
            {
                code = CM_Get_DevNode_Status(out _, out var problem, node, 0);
                if (code == 0 && (problem != 22) == enabled) return true;
                Thread.Sleep(20); // Only wait when Windows has not reported the requested state yet.
            } while (watch.ElapsedMilliseconds < 1500);
            error = "Windows has not confirmed the requested monitor state.";
            return false;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException) { error = ex.Message; return false; }
    }

    [DllImport("setupapi.dll", SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr SetupDiGetClassDevsW(ref Guid guid, IntPtr enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref DeviceInfo info);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref DeviceInfo info, StringBuilder id, uint length, out uint required);
    [DllImport("setupapi.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr set, ref DeviceInfo info, uint property, out uint type, byte[]? buffer, uint size, out uint required);
    [DllImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Locate_DevNodeW(out uint node, string id, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint node, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Enable_DevNode(uint node, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Disable_DevNode(uint node, uint flags);
}
