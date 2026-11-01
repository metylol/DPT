# Display Preset Tool (DPT)

DPT is a self-contained Windows x64 utility for applying a driver-supported display resolution and requesting full-panel scaling.

## Download

Get **DPT.exe** from the [latest release](https://github.com/metylol/DPT/releases/latest). No .NET installation is required. The executable is unsigned, so Windows may show a security warning.

## Use

1. Select a target display and a resolution exposed by its graphics driver.
2. Select **Apply stretch preset**. DPT saves the original display, monitor-device, and scaling states, applies the selected mode, requests vendor or Windows scaling, and disables identified monitor devices that are currently enabled.
3. Select **Keep changes** within 10 seconds. Otherwise, **Revert safely**, timeout, or closing DPT restores the recorded state. A separate recovery process attempts restoration if DPT stops responding.

The **Revert** button returns the display to its detected native resolution and enables identified monitor devices. After **Keep changes**, it does not restore the previous GPU-scaling setting.

## Limits and recovery

- DPT changes only an active display whose Windows path, device instance, hardware ID, and EDID identity can be matched. It refuses a shared display source path or a missing native resolution.
- NVIDIA, AMD, and Intel driver scaling support varies by GPU, driver, and connection. DPT falls back to Windows display scaling when vendor APIs are unavailable, but cannot guarantee GPU scaling on every PC. AMD, Intel, and hybrid-graphics configurations still need hardware validation.
- The recovery process cannot guarantee restoration after a system crash, power loss, or driver failure. If restoration reports an error, use Windows Display Settings or Device Manager. Recovery errors are written to `%LOCALAPPDATA%\DPT\recovery.log`.

## Build from source

Use the .NET 10 SDK on Windows:

```powershell
dotnet publish ValorantStretch.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o release
```

The generated `DPT.exe` is self-contained. The application icon is embedded in the executable.
