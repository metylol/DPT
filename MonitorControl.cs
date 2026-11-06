using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace ValorantStretch;

internal sealed class MonitorTarget
{
    public required string InstanceId { get; init; }
    public required string[] HardwareIds { get; init; }
    public required string ModelName { get; init; }
    public required string DisplayDeviceName { get; init; }
    public required uint DisplayTargetId { get; init; }
    public required string DisplayPath { get; init; }
    public required int RefreshHz { get; init; }
    public required bool Enabled { get; set; }
    public required int PreferredWidth { get; init; }
    public required int PreferredHeight { get; init; }
    public required bool CanChangeIndependently { get; init; }
    public override string ToString() => $"{ModelName}   ·   {RefreshHz} Hz";
}

internal sealed record MonitorDeviceSnapshot(string InstanceId, bool Enabled);

internal static class MonitorControl
{
    private sealed class PnpDto
    {
        [JsonPropertyName("InstanceId")] public string? InstanceId { get; set; }
        [JsonPropertyName("FriendlyName")] public string? FriendlyName { get; set; }
        [JsonPropertyName("Status")] public string? Status { get; set; }
        [JsonPropertyName("ProblemCode")] public int? ProblemCode { get; set; }
        [JsonPropertyName("HardwareIds")] public string[]? HardwareIds { get; set; }
    }

    private sealed record ActivePath(string DisplayDeviceName, string DevicePath, string MonitorInstanceId,
        string FriendlyName, int RefreshHz, uint TargetId, ushort EdidManufacturerId, ushort EdidProductCodeId);

    internal static List<MonitorTarget> FindTargets()
    {
        var activePaths = QueryActivePaths();
        var devices = ReadPnpDevices();
        var pathCounts = activePaths.GroupBy(p => p.DisplayDeviceName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var targets = new List<MonitorTarget>();
        foreach (var path in activePaths)
        {
            var device = ResolveActivePathInstance(path, devices);
            if (device?.InstanceId is null) continue; // Fail closed if the active path cannot be tied to a unique PnP instance.
            var identity = ReadMonitorIdentity(device.InstanceId);
            var pnpHardwareIds = device.HardwareIds ?? [];
            if (identity.HardwareIds.Length == 0 || pnpHardwareIds.Length == 0 ||
                identity.EdidManufacturerId != path.EdidManufacturerId || identity.EdidProductCodeId != path.EdidProductCodeId) continue;
            var instanceParts = device.InstanceId.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (instanceParts.Length < 2 || !pnpHardwareIds.Any(id =>
            {
                var hardwareParts = id.Split('\\', StringSplitOptions.RemoveEmptyEntries);
                return hardwareParts.Length >= 2 && string.Equals(hardwareParts[1], instanceParts[1], StringComparison.OrdinalIgnoreCase);
            })) continue;
            var problemCode = device.ProblemCode;
            var enabled = problemCode.HasValue ? problemCode.Value != 22 :
                string.Equals(device.Status, "OK", StringComparison.OrdinalIgnoreCase);
            var model = string.IsNullOrWhiteSpace(identity.ModelName) ? device.FriendlyName : identity.ModelName;
            if (string.IsNullOrWhiteSpace(model)) model = path.FriendlyName;
            if (string.IsNullOrWhiteSpace(model)) continue;
            targets.Add(new MonitorTarget
            {
                InstanceId = device.InstanceId,
                HardwareIds = pnpHardwareIds,
                ModelName = model.Trim(),
                DisplayDeviceName = path.DisplayDeviceName,
                DisplayTargetId = path.TargetId,
                DisplayPath = string.IsNullOrWhiteSpace(path.DevicePath)
                    ? $"{path.DisplayDeviceName} · QueryDisplayConfig target {path.TargetId} · EDID matched"
                    : path.DevicePath,
                RefreshHz = path.RefreshHz,
                Enabled = enabled,
                PreferredWidth = identity.Width,
                PreferredHeight = identity.Height,
                CanChangeIndependently = pathCounts[path.DisplayDeviceName] == 1
            });
        }
        return targets
            .GroupBy(t => t.InstanceId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(t => t.ModelName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static List<MonitorDeviceSnapshot> SnapshotAll()
    {
        return ReadPnpDevices()
            .Where(d => !string.IsNullOrWhiteSpace(d.InstanceId))
            .Select(d => new MonitorDeviceSnapshot(d.InstanceId!, d.ProblemCode.HasValue
                ? d.ProblemCode.Value != 22
                : string.Equals(d.Status, "OK", StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    internal static bool SetEnabled(string instanceId, bool enabled, out string error)
    {
        error = "";
        if (!IsElevated())
        {
            error = "Device Manager changes require administrator permission. Restart the app as administrator.";
            return false;
        }
        return NativeMonitorDevices.SetEnabled(instanceId, enabled, out error);
    }

    internal static bool SetEnabled(string instanceId, bool enabled, IReadOnlyCollection<NativeMonitorDevices.Device> devices, out string error)
    {
        error = "";
        if (!IsElevated())
        {
            error = "Device Manager changes require administrator permission. Restart the app as administrator.";
            return false;
        }
        return NativeMonitorDevices.SetEnabled(instanceId, enabled, devices, out error);
    }

    internal static bool IsActivePath(MonitorTarget target)
    {
        try
        {
            var devices = ReadPnpDevices();
            return QueryActivePaths().Any(path => string.Equals(
                ResolveActivePathInstance(path, devices)?.InstanceId, target.InstanceId, StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    private static PnpDto? ResolveActivePathInstance(ActivePath path, List<PnpDto> devices)
    {
        if (!string.IsNullOrWhiteSpace(path.MonitorInstanceId))
        {
            var exact = devices.FirstOrDefault(d => string.Equals(d.InstanceId, path.MonitorInstanceId, StringComparison.OrdinalIgnoreCase));
            if (exact is not null && MatchesActivePathIdentity(path, exact)) return exact;
        }
        var candidates = devices.Where(d => TryGetUid(d.InstanceId, out var uid) && uid == path.TargetId)
            .Where(d => MatchesActivePathIdentity(path, d)).ToList();
        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static bool MatchesActivePathIdentity(ActivePath path, PnpDto device)
    {
        if (string.IsNullOrWhiteSpace(device.InstanceId)) return false;
        var id = ReadMonitorIdentity(device.InstanceId);
        var hardwareIds = device.HardwareIds ?? [];
        var instanceParts = device.InstanceId.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return id.EdidManufacturerId == path.EdidManufacturerId && id.EdidProductCodeId == path.EdidProductCodeId &&
            !string.IsNullOrWhiteSpace(id.ModelName) &&
            (string.IsNullOrWhiteSpace(path.FriendlyName) || string.Equals(id.ModelName, path.FriendlyName, StringComparison.OrdinalIgnoreCase)) &&
            instanceParts.Length >= 2 && hardwareIds.Any(hardwareId =>
            {
                var hardwareParts = hardwareId.Split('\\', StringSplitOptions.RemoveEmptyEntries);
                return hardwareParts.Length >= 2 && string.Equals(hardwareParts[1], instanceParts[1], StringComparison.OrdinalIgnoreCase);
            });
    }

    private static List<PnpDto> ReadPnpDevices() => ReadPnpDevices(NativeMonitorDevices.Enumerate());

    private static List<PnpDto> ReadPnpDevices(IEnumerable<NativeMonitorDevices.Device> devices) => devices
        .Select(d => new PnpDto { InstanceId = d.InstanceId, FriendlyName = d.Name,
            HardwareIds = d.HardwareIds, ProblemCode = (int)d.Problem, Status = d.Problem == 0 ? "OK" : "Error" }).ToList();

    internal static bool AreActivePaths(IEnumerable<MonitorTarget> targets, IReadOnlyCollection<NativeMonitorDevices.Device> deviceSnapshot)
    {
        try
        {
            var devices = ReadPnpDevices(deviceSnapshot);
            var activeIds = QueryActivePaths().Select(path => ResolveActivePathInstance(path, devices)?.InstanceId)
                .Where(id => id is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return targets.All(target => activeIds.Contains(target.InstanceId));
        }
        catch { return false; }
    }
    private static bool IsElevated()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    private static (string ModelName, string[] HardwareIds, int Width, int Height, ushort EdidManufacturerId, ushort EdidProductCodeId) ReadMonitorIdentity(string instanceId)
    {
        var parts = instanceId.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return ("", [], 0, 0, 0, 0);
        var keyPath = $@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{parts[1]}\{parts[2]}";
        try
        {
            using var deviceKey = Registry.LocalMachine.OpenSubKey(keyPath, writable: false);
            var hardwareIds = deviceKey?.GetValue("HardwareID") as string[] ?? [];
            using var parameters = deviceKey?.OpenSubKey("Device Parameters", writable: false);
            if (parameters?.GetValue("EDID") is not byte[] edid || edid.Length < 128)
                return ("", hardwareIds, 0, 0, 0, 0);
            var model = ParseEdidModel(edid);
            var (width, height) = ParseEdidPreferredMode(edid);
            return (model, hardwareIds, width, height, BitConverter.ToUInt16(edid, 8), BitConverter.ToUInt16(edid, 10));
        }
        catch { return ("", [], 0, 0, 0, 0); }
    }

    private static string ParseEdidModel(byte[] edid)
    {
        for (var offset = 54; offset <= 108; offset += 18)
        {
            if (edid[offset] != 0 || edid[offset + 1] != 0 || edid[offset + 2] != 0 || edid[offset + 3] != 0xFC) continue;
            var text = Encoding.ASCII.GetString(edid, offset + 5, 13).Trim(' ', '\0', '\n', '\r');
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        return "";
    }

    private static (int Width, int Height) ParseEdidPreferredMode(byte[] edid)
    {
        var preferred = edid[18] == 1 && edid[19] >= 3 && (edid[24] & 0x02) != 0;
        var bestArea = 0;
        var best = (Width: 0, Height: 0);
        for (var offset = 54; offset <= 108; offset += 18)
        {
            if (edid[offset] == 0 && edid[offset + 1] == 0) continue;
            var width = edid[offset + 2] | ((edid[offset + 4] & 0xF0) << 4);
            var height = edid[offset + 5] | ((edid[offset + 7] & 0xF0) << 4);
            if (width <= 0 || height <= 0) continue;
            if (preferred && offset == 54) return (width, height);
            if (width * height > bestArea) { bestArea = width * height; best = (width, height); }
        }
        return best;
    }

    private static string NormalizeDevicePath(string devicePath)
    {
        var value = devicePath.Trim();
        if (value.StartsWith("\\\\?\\", StringComparison.Ordinal)) value = value[4..];
        var interfaceSuffix = value.IndexOf("#{", StringComparison.Ordinal);
        if (interfaceSuffix >= 0) value = value[..interfaceSuffix];
        return value.Replace('#', '\\').TrimEnd('\\');
    }

    private static List<ActivePath> QueryActivePaths()
    {
        const uint qdcOnlyActivePaths = 0x00000002;
        var result = new List<ActivePath>();
        NativeDisplay.PathInfo[] paths = [];
        uint pathCount = 0, modeCount = 0;
        var status = 0;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            status = NativeDisplay.GetDisplayConfigBufferSizes(qdcOnlyActivePaths, out pathCount, out modeCount);
            if (status != 0) throw new IOException($"GetDisplayConfigBufferSizes failed ({status}).");
            paths = new NativeDisplay.PathInfo[pathCount];
            var modes = new NativeDisplay.ModeInfo[modeCount];
            status = NativeDisplay.QueryDisplayConfig(qdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (status != 122) break;
        }
        if (status == 122) throw new IOException("Display configuration kept changing while it was being read.");
        if (status != 0) throw new IOException($"QueryDisplayConfig failed ({status}).");
        for (var i = 0; i < pathCount; i++)
        {
            var path = paths[i];
            var targetRequest = new NativeDisplay.TargetDeviceName
            {
                Header = new NativeDisplay.DeviceInfoHeader { Type = 2, Size = (uint)Marshal.SizeOf<NativeDisplay.TargetDeviceName>(), AdapterId = path.TargetInfo.AdapterId, Id = path.TargetInfo.Id },
                MonitorFriendlyDeviceName = new string('\0', 64),
                MonitorDevicePath = new string('\0', 128)
            };
            var targetPtr = Marshal.AllocHGlobal(Marshal.SizeOf<NativeDisplay.TargetDeviceName>());
            try
            {
                Marshal.StructureToPtr(targetRequest, targetPtr, false);
                status = NativeDisplay.DisplayConfigGetDeviceInfo(targetPtr);
                if (status != 0) continue;
                targetRequest = Marshal.PtrToStructure<NativeDisplay.TargetDeviceName>(targetPtr);
            }
            finally { Marshal.FreeHGlobal(targetPtr); }

            var sourceRequest = new NativeDisplay.SourceDeviceName
            {
                Header = new NativeDisplay.DeviceInfoHeader { Type = 1, Size = (uint)Marshal.SizeOf<NativeDisplay.SourceDeviceName>(), AdapterId = path.SourceInfo.AdapterId, Id = path.SourceInfo.Id },
                ViewGdiDeviceName = new string('\0', 32)
            };
            var sourcePtr = Marshal.AllocHGlobal(Marshal.SizeOf<NativeDisplay.SourceDeviceName>());
            try
            {
                Marshal.StructureToPtr(sourceRequest, sourcePtr, false);
                status = NativeDisplay.DisplayConfigGetDeviceInfo(sourcePtr);
                if (status != 0) continue;
                sourceRequest = Marshal.PtrToStructure<NativeDisplay.SourceDeviceName>(sourcePtr);
            }
            finally { Marshal.FreeHGlobal(sourcePtr); }
            var hz = path.TargetInfo.RefreshRate.Denominator == 0 ? 0 :
                (int)Math.Round((double)path.TargetInfo.RefreshRate.Numerator / path.TargetInfo.RefreshRate.Denominator);
            var sourceName = sourceRequest.ViewGdiDeviceName.TrimEnd('\0');
            var devicePath = targetRequest.MonitorDevicePath.TrimEnd('\0');
            var mappedInstanceId = NormalizeDevicePath(devicePath);
            var friendly = targetRequest.MonitorFriendlyDeviceName.TrimEnd('\0');
            result.Add(new ActivePath(sourceName, devicePath, mappedInstanceId, friendly, hz,
                path.TargetInfo.Id, targetRequest.EdidManufactureId, targetRequest.EdidProductCodeId));
        }
        return result;
    }

    private static bool TryGetUid(string? instanceId, out uint uid)
    {
        uid = 0;
        if (string.IsNullOrWhiteSpace(instanceId)) return false;
        var marker = instanceId.LastIndexOf("UID", StringComparison.OrdinalIgnoreCase);
        return marker >= 0 && uint.TryParse(instanceId[(marker + 3)..], out uid);
    }

}

internal static class NativeDisplay
{
    [StructLayout(LayoutKind.Sequential)] internal struct Luid { public uint LowPart; public int HighPart; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rational { public uint Numerator; public uint Denominator; }
    [StructLayout(LayoutKind.Sequential)] internal struct PathSourceInfo { public Luid AdapterId; public uint Id; public uint ModeInfoIdx; public uint StatusFlags; }
    [StructLayout(LayoutKind.Sequential)] internal struct PathTargetInfo
    {
        public Luid AdapterId; public uint Id; public uint ModeInfoIdx; public uint OutputTechnology; public uint Rotation;
        public uint Scaling; public Rational RefreshRate; public uint ScanLineOrdering; public uint TargetAvailable; public uint StatusFlags;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct PathInfo { public PathSourceInfo SourceInfo; public PathTargetInfo TargetInfo; public uint Flags; }
    [StructLayout(LayoutKind.Explicit, Size = 48)] internal struct ModeUnion { [FieldOffset(0)] public long Data; }
    [StructLayout(LayoutKind.Sequential)] internal struct ModeInfo { public uint InfoType; public uint Id; public Luid AdapterId; public ModeUnion Mode; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct DeviceInfoHeader { public uint Type; public uint Size; public Luid AdapterId; public uint Id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct TargetDeviceName
    {
        public DeviceInfoHeader Header; public uint Flags; public uint OutputTechnology; public ushort EdidManufactureId; public ushort EdidProductCodeId; public uint ConnectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string MonitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string MonitorDevicePath;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct SourceDeviceName
    {
        public DeviceInfoHeader Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string ViewGdiDeviceName;
    }

    [DllImport("user32.dll")] internal static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);
    [DllImport("user32.dll")] internal static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] PathInfo[] pathArray, ref uint numModeInfoArrayElements, [Out] ModeInfo[] modeInfoArray, IntPtr currentTopologyId);
    [DllImport("user32.dll")] internal static extern int DisplayConfigGetDeviceInfo(IntPtr requestPacket);
}
