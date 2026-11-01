using System.Runtime.InteropServices;

namespace ValorantStretch;

internal sealed record WindowsScalingState(string DisplayDeviceName, uint TargetAdapterLow, int TargetAdapterHigh,
    uint TargetId, uint Scaling);

// Vendor-neutral CCD fallback, based on the MIT-licensed MartinGC94/DisplayConfig project.
internal static class WindowsScaling
{
    private const uint QdcOnlyActivePaths = 2;
    private const uint SdcUseSuppliedDisplayConfig = 0x20, SdcValidate = 0x40, SdcApply = 0x80,
        SdcSaveToDatabase = 0x200;
    private const uint Stretched = 3;

    [DllImport("user32.dll")]
    private static extern int SetDisplayConfig(uint pathCount, [In] NativeDisplay.PathInfo[] paths,
        uint modeCount, [In] NativeDisplay.ModeInfo[] modes, uint flags);

    private static (NativeDisplay.PathInfo[] Paths, NativeDisplay.ModeInfo[] Modes) Query()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var result = NativeDisplay.GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount);
            if (result != 0) throw new IOException($"GetDisplayConfigBufferSizes failed ({result}).");
            var paths = new NativeDisplay.PathInfo[pathCount];
            var modes = new NativeDisplay.ModeInfo[modeCount];
            result = NativeDisplay.QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (result == 122) continue;
            if (result != 0) throw new IOException($"QueryDisplayConfig failed ({result}).");
            return (paths.Take((int)pathCount).ToArray(), modes.Take((int)modeCount).ToArray());
        }
        throw new IOException("Display configuration kept changing while it was being read.");
    }

    private static string SourceName(NativeDisplay.PathInfo path)
    {
        var info = new NativeDisplay.SourceDeviceName
        {
            Header = new NativeDisplay.DeviceInfoHeader
            {
                Type = 1, Size = (uint)Marshal.SizeOf<NativeDisplay.SourceDeviceName>(),
                AdapterId = path.SourceInfo.AdapterId, Id = path.SourceInfo.Id
            },
            ViewGdiDeviceName = new string('\0', 32)
        };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeDisplay.SourceDeviceName>());
        try
        {
            Marshal.StructureToPtr(info, pointer, false);
            var result = NativeDisplay.DisplayConfigGetDeviceInfo(pointer);
            if (result != 0) throw new IOException($"DisplayConfigGetDeviceInfo failed ({result}).");
            info = Marshal.PtrToStructure<NativeDisplay.SourceDeviceName>(pointer);
            return info.ViewGdiDeviceName.TrimEnd('\0');
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    internal static WindowsScalingState ReadCurrent(string displayDeviceName, uint targetId)
    {
        var matches = Query().Paths.Where(path => (path.Flags & 1) != 0 && path.TargetInfo.Id == targetId &&
            string.Equals(SourceName(path), displayDeviceName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) throw new IOException("Could not uniquely match the display path for Windows scaling.");
        var target = matches[0].TargetInfo;
        return new WindowsScalingState(displayDeviceName, target.AdapterId.LowPart, target.AdapterId.HighPart,
            target.Id, target.Scaling);
    }

    internal static void ApplyStretched(WindowsScalingState state) => SetScaling(state, Stretched);
    internal static void Restore(WindowsScalingState state) => SetScaling(state, state.Scaling);

    private static void SetScaling(WindowsScalingState state, uint scaling)
    {
        var (paths, modes) = Query();
        var matches = paths.Select((path, index) => (path, index)).Where(item =>
            item.path.TargetInfo.AdapterId.LowPart == state.TargetAdapterLow &&
            item.path.TargetInfo.AdapterId.HighPart == state.TargetAdapterHigh &&
            item.path.TargetInfo.Id == state.TargetId && (item.path.Flags & 1) != 0 &&
            string.Equals(SourceName(item.path), state.DisplayDeviceName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) throw new IOException("Could not uniquely match the display path to set Windows scaling.");

        var selected = matches[0].path;
        selected.TargetInfo.Scaling = scaling;
        paths[matches[0].index] = selected;
        var supplied = SdcUseSuppliedDisplayConfig;
        var validate = SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes, supplied | SdcValidate);
        if (validate != 0) throw new IOException($"Windows rejected the scaling request ({validate}).");
        var result = SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes,
            supplied | SdcApply | SdcSaveToDatabase);
        if (result != 0) throw new IOException($"Windows could not apply scaling ({result}).");
    }
}
