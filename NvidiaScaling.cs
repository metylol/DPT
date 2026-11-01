using System.Runtime.InteropServices;
namespace ValorantStretch;

// Layouts and function IDs from NVIDIA's public NVAPI SDK.
internal sealed class NvidiaScaling : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct PathInfo
    {
        public uint Version, SourceId, Count;
        public IntPtr Targets, Source;
        public uint Flags;
        public IntPtr Adapter;
    }
    [StructLayout(LayoutKind.Sequential)] private struct TargetInfo { public uint DisplayId; public IntPtr Details; public uint TargetId; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Query(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Init();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)] private delegate int DisplayId([MarshalAs(UnmanagedType.LPStr)] string name, out uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetConfig(ref uint count, IntPtr paths);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetConfig(uint count, IntPtr paths, uint flags);
    private IntPtr library, paths;
    private uint count;
    private readonly List<IntPtr> allocations = [];
    private Query query = null!;
    private static void Check(int code) { if (code != 0) throw new IOException($"NVIDIA scaling request failed (NVAPI {code})."); }
    private T Function<T>(uint id) where T : Delegate
    {
        var pointer = query(id);
        if (pointer == IntPtr.Zero) throw new NotSupportedException("This NVIDIA driver does not expose scaling control.");
        return Marshal.GetDelegateForFunctionPointer<T>(pointer);
    }
    private IntPtr Allocate(int bytes)
    {
        var pointer = Marshal.AllocHGlobal(bytes);
        allocations.Add(pointer);
        Marshal.Copy(new byte[bytes], 0, pointer, bytes);
        return pointer;
    }
    internal NvidiaScaling()
    {
        try
        {
            var dll = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), Environment.Is64BitProcess ? "nvapi64.dll" : "nvapi.dll");
            library = NativeLibrary.Load(dll);
            query = Marshal.GetDelegateForFunctionPointer<Query>(NativeLibrary.GetExport(library, "nvapi_QueryInterface"));
            Check(Function<Init>(0x0150e828)());
            var get = Function<GetConfig>(0x11abccf8);
            Check(get(ref count, IntPtr.Zero));
            if (count == 0 || count > 64) throw new IOException("NVIDIA reported an invalid display configuration.");
            int stride = Marshal.SizeOf<PathInfo>();
            paths = Allocate(checked((int)count * stride));
            for (int i = 0; i < count; i++)
                Marshal.StructureToPtr(new PathInfo { Version = (uint)stride | (2u << 16), Source = Allocate(32), Adapter = Allocate(8) }, paths + i * stride, false);
            Check(get(ref count, paths));
            for (int i = 0; i < count; i++)
            {
                var path = Marshal.PtrToStructure<PathInfo>(paths + i * stride);
                if (path.Count > 64) throw new IOException("NVIDIA reported too many display targets.");
                path.Targets = Allocate(checked((int)path.Count * Marshal.SizeOf<TargetInfo>()));
                for (int j = 0; j < path.Count; j++)
                {
                    var detail = Allocate(128);
                    Marshal.WriteInt32(detail, 128 | (1 << 16));
                    Marshal.StructureToPtr(new TargetInfo { Details = detail }, path.Targets + j * Marshal.SizeOf<TargetInfo>(), false);
                }
                Marshal.StructureToPtr(path, paths + i * stride, false);
            }
            Check(get(ref count, paths));
        }
        catch { Dispose(); throw; }
    }
    private IntPtr Details(string device)
    {
        Check(Function<DisplayId>(0xae457190)(device, out var id));
        for (int i = 0; i < count; i++)
        {
            var path = Marshal.PtrToStructure<PathInfo>(paths + i * Marshal.SizeOf<PathInfo>());
            if ((path.Flags & 1) != 0) continue;
            for (int j = 0; j < path.Count; j++)
            {
                var target = Marshal.PtrToStructure<TargetInfo>(path.Targets + j * Marshal.SizeOf<TargetInfo>());
                if (target.DisplayId == id && target.Details != IntPtr.Zero) return target.Details;
            }
        }
        throw new IOException("Selected monitor isn't an active NVIDIA display.");
    }
    internal int Read(string device) => Marshal.ReadInt32(Details(device), 8);
    internal void Set(string device, int scaling)
    {
        var details = Details(device);
        Marshal.WriteInt32(details, 8, scaling);
        var set = Function<SetConfig>(0x5d8cf8de);
        Check(set(count, paths, 1)); // Validate before applying the preserved global configuration.
        Check(set(count, paths, 2)); // Save the selected target's scaling; retain all other paths.
    }
    internal static int ReadCurrent(string device) { using var api = new NvidiaScaling(); return api.Read(device); }
    internal static void Apply(string device, int scaling)
    {
        using (var api = new NvidiaScaling()) api.Set(device, scaling);
        if (ReadCurrent(device) != scaling) throw new IOException("NVIDIA did not confirm the requested scaling mode.");
    }
    public void Dispose()
    {
        foreach (var pointer in allocations) Marshal.FreeHGlobal(pointer);
        allocations.Clear();
        if (library != IntPtr.Zero) { NativeLibrary.Free(library); library = IntPtr.Zero; }
    }
}
