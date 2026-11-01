# Display Preset Tool (DPT)

DPT is a Windows utility for applying a stretched display mode. It does not find, read, or edit game configuration files. Administrator access is required to change monitor device states.

## Use

The source repository does not include a compiled executable. Build it with the .NET 10 SDK, then copy the two installer scripts beside the published executable:

```powershell
dotnet publish ValorantStretch.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o publish
Copy-Item Install-DPT.ps1 publish
Copy-Item 'Install DPT.cmd' publish
```

Double-click **publish\Install DPT.cmd** for Start menu and Windows Search integration. It installs DPT under `%LOCALAPPDATA%\Programs\DPT` and adds launch and uninstall shortcuts under **Display Preset Tool** in Start. Recovery data is preserved.

1. Select a target monitor and a resolution exposed by its graphics driver. DPT initially selects the active monitor with the highest refresh rate.
2. Select **Apply stretch preset**. DPT records the original display, monitor-device, and GPU-scaling states; changes the selected display mode; requests the vendor's full-screen scaling mode or uses Windows' stretched-scaling fallback; and disables identified monitor devices that are currently enabled. It verifies that the active display paths remain present.
3. Select **Keep changes** within 10 seconds. **Revert safely**, timeout, or closing DPT restores the recorded states. A separate recovery process also attempts restoration if DPT crashes or stops responding.

The lower **Revert** button returns the selected display to its detected native resolution and enables identified monitor devices. After **Keep changes**, it does not restore the previous GPU-scaling setting.

## Limits and recovery

- DPT only changes an active monitor whose Windows display path, device instance, hardware ID, and EDID identity can be matched. It refuses a shared display source path or a missing native resolution.
- Scaling uses NVIDIA NVAPI, AMD ADL, or Intel IGCL when that driver exposes the selected display. If those APIs are unavailable, DPT tries Windows CCD scaling, following the approach used in the MIT-licensed [DisplayConfig project](https://github.com/MartinGC94/DisplayConfig). This cross-vendor API only requests stretched scaling: Windows falls back to the driver's default when a driver does not support it, so DPT cannot guarantee GPU scaling on every PC. Intel drivers may not expose stretched scaling, and Intel documents that custom resolutions do not work with its Stretch mode. AMD, Intel, NVIDIA, and multi-monitor combinations still need validation on hardware before public release.
- The separate recovery process cannot guarantee recovery after a system crash, power loss, or driver failure. If restoration reports an error, use Windows Display Settings or Device Manager. Recovery errors are written to `%LOCALAPPDATA%\DPT\recovery.log`.
- The executable is unsigned. Windows may warn or block it. A trusted publisher signature requires a code-signing certificate or a distribution service that signs the package.

## Build

Use the .NET 10 SDK on Windows:

```powershell
dotnet publish ValorantStretch.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o release
```

Distribute the generated `DPT.exe` only after validating it on the intended hardware. The application icon is embedded in the executable.
