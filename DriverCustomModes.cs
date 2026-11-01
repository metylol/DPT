using System.Runtime.InteropServices;

namespace ValorantStretch;

// Read-only access to NVIDIA's saved custom modes; ordinary Windows modes are not classified as custom.
internal static class DriverCustomModes
{
    [StructLayout(LayoutKind.Explicit, Size = 144)]
    private struct CustomDisplay
    {
        [FieldOffset(0)] public uint Version;
        [FieldOffset(4)] public uint Width;
        [FieldOffset(8)] public uint Height;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Query(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Initialize();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate int DisplayId([MarshalAs(UnmanagedType.LPStr)] string name, out uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EnumCustom(uint id, uint index, ref CustomDisplay display);

    internal static HashSet<(int Width, int Height)> Read(string displayName)
    {
        var result = new HashSet<(int, int)>();
        IntPtr library = IntPtr.Zero;
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), Environment.Is64BitProcess ? "nvapi64.dll" : "nvapi.dll");
            if (!NativeLibrary.TryLoad(path, out library)) return result;
            var query = Marshal.GetDelegateForFunctionPointer<Query>(NativeLibrary.GetExport(library, "nvapi_QueryInterface"));
            T Function<T>(uint id) where T : Delegate
            {
                var pointer = query(id);
                if (pointer == IntPtr.Zero) throw new NotSupportedException();
                return Marshal.GetDelegateForFunctionPointer<T>(pointer);
            }
            if (Function<Initialize>(0x0150e828)() != 0) return result;
            if (Function<DisplayId>(0xae457190)(displayName, out var displayId) != 0) return result;
            var enumerate = Function<EnumCustom>(0xa2072d59);
            for (uint index = 0; index < 1024; index++)
            {
                var mode = new CustomDisplay { Version = 144u | (1u << 16) };
                if (enumerate(displayId, index, ref mode) != 0) break;
                if (mode.Width > 0 && mode.Height > 0) result.Add(((int)mode.Width, (int)mode.Height));
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or NotSupportedException or BadImageFormatException) { }
        finally { if (library != IntPtr.Zero) NativeLibrary.Free(library); }
        return result;
    }
}
