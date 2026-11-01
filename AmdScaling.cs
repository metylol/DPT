using System.Runtime.InteropServices;

namespace ValorantStretch;

internal sealed record AmdScalingState(string Device, string Model, int Enabled, int Expansion, int PreserveAspect);

// ADL2 signatures and the Full Panel sequence follow AMD's AMDDisplayTool GPUScaling sample.
internal sealed class AmdScaling : IDisposable
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct AdapterInfo
    {
        public int Size, Index;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Udid;
        public int Bus, Device, Function, Vendor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string DisplayName;
        public int Present, Exists;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string DriverPath;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string DriverPathExt;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string PnpString;
        public int OsDisplayIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayId { public int Logical, Physical, LogicalAdapter, PhysicalAdapter; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DisplayInfo
    {
        public DisplayId Id;
        public int Controller;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Manufacturer;
        public int Type, OutputType, Connector, InfoMask, InfoValue;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate IntPtr Allocate(int bytes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Create(Allocate allocator, int connectedOnly, out IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Destroy(IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AdapterCount(IntPtr context, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AdapterList(IntPtr context, IntPtr adapters, int bytes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DisplayList(IntPtr context, int adapter, out int count, out IntPtr displays, int forceDetect);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetValue(IntPtr context, int adapter, int display, out int supported, out int current, out int defaultValue);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetValue(IntPtr context, int adapter, int display, int value);

    private readonly List<IntPtr> allocations = [];
    private readonly Allocate allocator;
    private IntPtr library, context;

    private AmdScaling()
    {
        allocator = bytes =>
        {
            var memory = Marshal.AllocHGlobal(bytes);
            allocations.Add(memory);
            return memory;
        };
        try
        {
            var dll = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "atiadlxx.dll");
            library = NativeLibrary.Load(dll);
            Check(Function<Create>("ADL2_Main_Control_Create")(allocator, 1, out context), "initialize");
        }
        catch { Dispose(); throw; }
    }

    private T Function<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    private static void Check(int code, string action)
    {
        if (code != 0) throw new IOException($"AMD driver could not {action} (ADL {code}).");
    }

    private (int Adapter, int Display) FindDisplay(string device, string model)
    {
        Check(Function<AdapterCount>("ADL2_Adapter_NumberOfAdapters_Get")(context, out var count), "list adapters");
        if (count is < 1 or > 64) throw new IOException("AMD reported an invalid adapter count.");
        var stride = Marshal.SizeOf<AdapterInfo>();
        var buffer = Marshal.AllocHGlobal(checked(count * stride));
        try
        {
            var zeroes = new byte[count * stride];
            Marshal.Copy(zeroes, 0, buffer, zeroes.Length);
            for (var i = 0; i < count; i++)
                Marshal.WriteInt32(buffer + i * stride, stride);
            Check(Function<AdapterList>("ADL2_Adapter_AdapterInfo_Get")(context, buffer, count * stride), "read adapters");
            var matches = new List<(int Adapter, int Display)>();
            for (var i = 0; i < count; i++)
            {
                var adapter = Marshal.PtrToStructure<AdapterInfo>(buffer + i * stride);
                if (adapter.Vendor != 1002 || adapter.Present == 0 ||
                    !string.Equals(adapter.DisplayName, device, StringComparison.OrdinalIgnoreCase)) continue;
                Check(Function<DisplayList>("ADL2_Display_DisplayInfo_Get")(
                    context, adapter.Index, out var displayCount, out var displays, 1), "list displays");
                if (displayCount is < 0 or > 64 || (displayCount > 0 && displays == IntPtr.Zero))
                    throw new IOException("AMD reported an invalid display list.");
                var displayStride = Marshal.SizeOf<DisplayInfo>();
                for (var j = 0; j < displayCount; j++)
                {
                    var display = Marshal.PtrToStructure<DisplayInfo>(displays + j * displayStride);
                    if ((display.InfoValue & 1) != 0 && display.Id.LogicalAdapter >= 0 &&
                        Normalize(display.Name) == Normalize(model))
                        matches.Add((display.Id.LogicalAdapter, display.Id.Logical));
                }
            }
            if (matches.Count != 1) throw new IOException("Could not uniquely match the active monitor in AMD's display list.");
            return matches[0];
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static string Normalize(string? name) =>
        new((name ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private int Read(string name, int adapter, int display)
    {
        Check(Function<GetValue>(name)(context, adapter, display, out var supported, out var current, out _), name);
        if (supported == 0) throw new NotSupportedException("This AMD display does not support GPU scaling.");
        return current;
    }

    private void Set(string name, int adapter, int display, int value) =>
        Check(Function<SetValue>(name)(context, adapter, display, value), name);

    internal static AmdScalingState ReadCurrent(string device, string model)
    {
        using var api = new AmdScaling();
        var (adapter, display) = api.FindDisplay(device, model);
        return new AmdScalingState(device, model,
            api.Read("ADL2_DFP_GPUScalingEnable_Get", adapter, display),
            api.Read("ADL2_Display_ImageExpansion_Get", adapter, display),
            api.Read("ADL2_Display_PreservedAspectRatio_Get", adapter, display));
    }

    internal static void ApplyFullPanel(AmdScalingState state)
    {
        using var api = new AmdScaling();
        var (adapter, display) = api.FindDisplay(state.Device, state.Model);
        api.Set("ADL2_DFP_GPUScalingEnable_Set", adapter, display, 1);
        api.Set("ADL2_Display_ImageExpansion_Set", adapter, display, 1);
        api.Set("ADL2_Display_PreservedAspectRatio_Set", adapter, display, 0);
        if (api.Read("ADL2_DFP_GPUScalingEnable_Get", adapter, display) != 1 ||
            api.Read("ADL2_Display_ImageExpansion_Get", adapter, display) != 1 ||
            api.Read("ADL2_Display_PreservedAspectRatio_Get", adapter, display) != 0)
            throw new IOException("AMD did not confirm Full Panel GPU scaling.");
    }

    internal static void Restore(AmdScalingState state)
    {
        using var api = new AmdScaling();
        var (adapter, display) = api.FindDisplay(state.Device, state.Model);
        api.Set("ADL2_Display_PreservedAspectRatio_Set", adapter, display, state.PreserveAspect);
        api.Set("ADL2_Display_ImageExpansion_Set", adapter, display, state.Expansion);
        api.Set("ADL2_DFP_GPUScalingEnable_Set", adapter, display, state.Enabled);
        if (api.Read("ADL2_DFP_GPUScalingEnable_Get", adapter, display) != state.Enabled ||
            api.Read("ADL2_Display_ImageExpansion_Get", adapter, display) != state.Expansion ||
            api.Read("ADL2_Display_PreservedAspectRatio_Get", adapter, display) != state.PreserveAspect)
            throw new IOException("AMD did not confirm its original scaling settings.");
    }

    public void Dispose()
    {
        if (context != IntPtr.Zero && library != IntPtr.Zero)
            try { Function<Destroy>("ADL2_Main_Control_Destroy")(context); } catch { }
        context = IntPtr.Zero;
        foreach (var memory in allocations) Marshal.FreeHGlobal(memory);
        allocations.Clear();
        if (library != IntPtr.Zero) NativeLibrary.Free(library);
        library = IntPtr.Zero;
    }
}
