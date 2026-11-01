using System.Runtime.InteropServices;

namespace ValorantStretch;

internal sealed record IntelScalingState(uint TargetId, bool Enabled, uint Type, uint CustomX, uint CustomY);

// IGCL ABI and scaling behavior follow Intel's official Scaling_Samples.
internal sealed class IntelScaling : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct InitArgs
    {
        public uint Size;
        public byte Version;
        public uint AppVersion, Flags, SupportedVersion;
        public Guid ApplicationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FirmwareVersion { public ulong Major, Minor, Build; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DeviceProperties
    {
        public uint Size;
        public byte Version;
        public IntPtr DeviceId;
        public uint DeviceIdSize, Type, SupportedFunctions;
        public ulong DriverVersion;
        public FirmwareVersion Firmware;
        public uint Vendor, Device, Revision, EusPerSubSlice, SubSlicesPerSlice, Slices;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 100)] public string Name;
        public uint GraphicsProperties, Frequency;
        public ushort SubsystemId, SubsystemVendor;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)] public byte[] BusDeviceFunction;
        public uint XeCores;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 108)] public byte[] Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayTiming
    {
        public uint Size;
        public byte Version;
        public ulong PixelClock;
        public uint HActive, VActive, HTotal, VTotal, HBlank, VBlank, HSync, VSync;
        public float RefreshRate;
        public uint SignalStandard;
        public byte VicId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayProperties
    {
        public uint Size;
        public byte Version;
        public uint TargetId, Type, AttachedMuxType, ProtocolConverterOutput;
        public byte SpecMajor, SpecMinor, SpecRevision;
        public uint SupportedBpc, ProtocolConverterType, ConfigFlags, EnabledFeatures, SupportedFeatures,
            AdvancedEnabledFeatures, AdvancedSupportedFeatures;
        public DisplayTiming Timing;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public uint[] Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScalingCaps { public uint Size; public byte Version; public uint Supported; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScalingSettings
    {
        public uint Size;
        public byte Version;
        [MarshalAs(UnmanagedType.I1)] public bool Enabled;
        public uint Type, CustomX, CustomY;
        [MarshalAs(UnmanagedType.I1)] public bool HardwareModeSet;
        public uint PreferredType;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Init(ref InitArgs args, out IntPtr api);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Close(IntPtr api);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Enumerate(IntPtr api, ref uint count, IntPtr handles);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetDeviceProperties(IntPtr device, IntPtr properties);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int EnumerateOutputs(IntPtr device, ref uint count, IntPtr handles);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetDisplayProperties(IntPtr display, IntPtr properties);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetScalingCaps(IntPtr display, ref ScalingCaps caps);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetScaling(IntPtr display, ref ScalingSettings settings);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetScaling(IntPtr display, ref ScalingSettings settings);

    private IntPtr library, api;

    private IntelScaling()
    {
        try
        {
            if (Marshal.SizeOf<InitArgs>() != 36 || Marshal.SizeOf<DeviceProperties>() != 320 ||
                Marshal.SizeOf<DisplayProperties>() != 184 || Marshal.SizeOf<ScalingCaps>() != 12 ||
                Marshal.SizeOf<ScalingSettings>() != 28)
                throw new BadImageFormatException("Intel IGCL structure layout is incompatible with this build.");
            library = NativeLibrary.Load(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ControlLib.dll"));
            var args = new InitArgs { Size = (uint)Marshal.SizeOf<InitArgs>(), AppVersion = 0x00010001 };
            Check(Function<Init>("ctlInit")(ref args, out api), "initialize");
        }
        catch { Dispose(); throw; }
    }

    private T Function<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    private static void Check(int result, string action)
    {
        if (result != 0) throw new IOException($"Intel driver could not {action} (IGCL 0x{result:X8}).");
    }

    private IntPtr FindDisplay(uint targetId)
    {
        uint count = 0;
        Check(Function<Enumerate>("ctlEnumerateDevices")(api, ref count, IntPtr.Zero), "list graphics adapters");
        if (count is < 1 or > 64) throw new IOException("Intel reported an invalid adapter count.");
        var devices = Marshal.AllocHGlobal(checked((int)count * IntPtr.Size));
        var deviceId = Marshal.AllocHGlobal(8);
        var properties = Marshal.AllocHGlobal(Marshal.SizeOf<DeviceProperties>());
        var displays = new List<IntPtr>();
        try
        {
            Marshal.Copy(new byte[8], 0, deviceId, 8);
            Marshal.Copy(new byte[count * IntPtr.Size], 0, devices, checked((int)count * IntPtr.Size));
            Check(Function<Enumerate>("ctlEnumerateDevices")(api, ref count, devices), "read graphics adapters");
            var matches = new List<IntPtr>();
            for (var i = 0; i < count; i++)
            {
                var device = Marshal.ReadIntPtr(devices, checked((int)i * IntPtr.Size));
                Marshal.Copy(new byte[Marshal.SizeOf<DeviceProperties>()], 0, properties, Marshal.SizeOf<DeviceProperties>());
                Marshal.WriteInt32(properties, Marshal.SizeOf<DeviceProperties>());
                Marshal.WriteIntPtr(properties, 8, deviceId);
                Marshal.WriteInt32(properties, 16, 8);
                Marshal.WriteByte(properties, 4, 3);
                var getProperties = Function<GetDeviceProperties>("ctlGetDeviceProperties");
                if (getProperties(device, properties) != 0)
                {
                    Marshal.Copy(new byte[Marshal.SizeOf<DeviceProperties>()], 0, properties, Marshal.SizeOf<DeviceProperties>());
                    Marshal.WriteInt32(properties, Marshal.SizeOf<DeviceProperties>());
                    Marshal.WriteIntPtr(properties, 8, deviceId);
                    Marshal.WriteInt32(properties, 16, 8);
                    if (getProperties(device, properties) != 0) continue;
                }
                if ((uint)Marshal.ReadInt32(properties, 64) != 0x8086) continue;

                uint outputCount = 0;
                Check(Function<EnumerateOutputs>("ctlEnumerateDisplayOutputs")(device, ref outputCount, IntPtr.Zero), "list Intel displays");
                if (outputCount > 64) throw new IOException("Intel reported an invalid display count.");
                if (outputCount == 0) continue;
                var outputs = Marshal.AllocHGlobal(checked((int)outputCount * IntPtr.Size));
                displays.Add(outputs);
                Check(Function<EnumerateOutputs>("ctlEnumerateDisplayOutputs")(device, ref outputCount, outputs), "read Intel displays");
                for (var j = 0; j < outputCount; j++)
                {
                    var output = Marshal.ReadIntPtr(outputs, checked((int)j * IntPtr.Size));
                    var displayProperties = new DisplayProperties
                    {
                        Size = (uint)Marshal.SizeOf<DisplayProperties>(),
                        Reserved = new uint[16]
                    };
                    var displayBuffer = Marshal.AllocHGlobal(Marshal.SizeOf<DisplayProperties>());
                    try
                    {
                        Marshal.StructureToPtr(displayProperties, displayBuffer, false);
                        Check(Function<GetDisplayProperties>("ctlGetDisplayProperties")(output, displayBuffer), "read Intel display identity");
                        displayProperties = Marshal.PtrToStructure<DisplayProperties>(displayBuffer);
                    }
                    finally { Marshal.FreeHGlobal(displayBuffer); }
                    if (displayProperties.TargetId == targetId) matches.Add(output);
                }
            }
            if (matches.Count != 1) throw new IOException("Could not uniquely match the active monitor in Intel's display list.");
            var caps = new ScalingCaps { Size = (uint)Marshal.SizeOf<ScalingCaps>() };
            Check(Function<GetScalingCaps>("ctlGetSupportedScalingCapability")(matches[0], ref caps), "check display scaling support");
            if ((caps.Supported & 4) == 0) throw new NotSupportedException("This Intel display does not support stretched GPU scaling.");
            return matches[0];
        }
        finally
        {
            foreach (var output in displays) Marshal.FreeHGlobal(output);
            Marshal.FreeHGlobal(properties);
            Marshal.FreeHGlobal(deviceId);
            Marshal.FreeHGlobal(devices);
        }
    }

    private ScalingSettings Read(IntPtr display)
    {
        var settings = new ScalingSettings { Size = (uint)Marshal.SizeOf<ScalingSettings>(), Version = 1 };
        Check(Function<GetScaling>("ctlGetCurrentScaling")(display, ref settings), "read current display scaling");
        return settings;
    }

    private void Set(IntPtr display, bool enabled, uint type, uint customX, uint customY)
    {
        var settings = new ScalingSettings
        {
            Size = (uint)Marshal.SizeOf<ScalingSettings>(), Version = 1, Enabled = enabled,
            Type = type, CustomX = customX, CustomY = customY
        };
        Check(Function<SetScaling>("ctlSetCurrentScaling")(display, ref settings), "set display scaling");
    }

    internal static IntelScalingState ReadCurrent(uint targetId)
    {
        using var igcl = new IntelScaling();
        var settings = igcl.Read(igcl.FindDisplay(targetId));
        return new IntelScalingState(targetId, settings.Enabled, settings.Type, settings.CustomX, settings.CustomY);
    }

    internal static void ApplyStretched(IntelScalingState state)
    {
        using var igcl = new IntelScaling();
        var display = igcl.FindDisplay(state.TargetId);
        igcl.Set(display, true, 4, 0, 0);
        var current = igcl.Read(display);
        if (!current.Enabled || current.Type != 4)
            throw new IOException("Intel did not confirm stretched GPU scaling.");
    }

    internal static void Restore(IntelScalingState state)
    {
        using var igcl = new IntelScaling();
        var display = igcl.FindDisplay(state.TargetId);
        igcl.Set(display, state.Enabled, state.Type, state.CustomX, state.CustomY);
        var current = igcl.Read(display);
        if (current.Enabled != state.Enabled || current.Type != state.Type ||
            current.CustomX != state.CustomX || current.CustomY != state.CustomY)
            throw new IOException("Intel did not confirm its original scaling settings.");
    }

    public void Dispose()
    {
        if (api != IntPtr.Zero && library != IntPtr.Zero)
            try { Function<Close>("ctlClose")(api); } catch { }
        api = IntPtr.Zero;
        if (library != IntPtr.Zero) { NativeLibrary.Free(library); library = IntPtr.Zero; }
    }
}
