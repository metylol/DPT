using System.Diagnostics;
using System.Text.Json;

namespace ValorantStretch;

internal sealed record RecoveryState(
    string DisplayDevice, int Width, int Height, int Frequency,
    MonitorDeviceSnapshot[] Monitors, int? NvidiaScaling, AmdScalingState? AmdScaling, IntelScalingState? IntelScaling,
    WindowsScalingState? WindowsScaling);

internal sealed class RecoveryGuard : IDisposable
{
    private readonly Process child;
    private readonly StreamWriter input;
    private bool finished;

    private RecoveryGuard(Process child)
    {
        this.child = child;
        input = child.StandardInput;
    }

    internal static RecoveryGuard Start(RecoveryState state)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!, "--recovery-watchdog")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        var child = Process.Start(start) ?? throw new IOException("Could not start display recovery process.");
        try
        {
            var guard = new RecoveryGuard(child);
            guard.Send(JsonSerializer.Serialize(state));
            var ready = child.StandardOutput.ReadLineAsync();
            if (!ready.Wait(TimeSpan.FromSeconds(5)) || ready.Result != "READY")
                throw new IOException("Display recovery process did not confirm its snapshot.");
            return guard;
        }
        catch { child.StandardInput.Dispose(); child.Dispose(); throw; }
    }

    internal void Send(string command)
    {
        if (finished || child.HasExited) throw new IOException("Display recovery process stopped unexpectedly.");
        input.WriteLine(command);
        input.Flush();
    }

    internal void Complete()
    {
        if (finished) return;
        Send("DONE");
        var acknowledged = child.StandardOutput.ReadLineAsync();
        if (!acknowledged.Wait(TimeSpan.FromSeconds(5)) || acknowledged.Result != "DONE")
            throw new IOException("Display recovery process did not confirm the preset.");
        finished = true;
        input.Dispose();
        child.Dispose();
    }

    public void Dispose()
    {
        if (!finished) input.Dispose(); // EOF makes the child restore before it exits.
        child.Dispose();
    }

    internal static int RunChild()
    {
        try
        {
            var state = JsonSerializer.Deserialize<RecoveryState>(Console.ReadLine() ?? "")
                ?? throw new IOException("Recovery snapshot missing.");
            Console.WriteLine("READY");
            Console.Out.Flush();
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (true)
            {
                var read = Task.Run(Console.ReadLine);
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero || !read.Wait(remaining)) break;
                var command = read.Result;
                if (command == "DONE")
                {
                    Console.WriteLine("DONE");
                    Console.Out.Flush();
                    return 0;
                }
                if (command is null) break;
                if (command == "ARM") deadline = DateTime.UtcNow.AddSeconds(12);
                if (command is "WORKING" or "RESTORING") deadline = DateTime.UtcNow.AddSeconds(30);
            }
            Restore(state);
            return 0;
        }
        catch (Exception ex)
        {
            Log("Recovery process failed: " + ex);
            return 1;
        }
    }

    private static void Restore(RecoveryState state)
    {
        foreach (var monitor in state.Monitors)
            try
            {
                if (!MonitorControl.SetEnabled(monitor.InstanceId, monitor.Enabled, out var error))
                    Log($"Monitor {monitor.InstanceId}: {error}");
            }
            catch (Exception ex) { Log($"Monitor {monitor.InstanceId}: {ex.Message}"); }

        var mode = Native.CreateMode();
        mode.Width = state.Width;
        mode.Height = state.Height;
        mode.Frequency = state.Frequency;
        mode.Fields = Native.DM_PELSWIDTH | Native.DM_PELSHEIGHT | Native.DM_DISPLAYFREQUENCY;
        try
        {
            if (Native.ChangeDisplaySettings(state.DisplayDevice, ref mode, Native.CDS_UPDATEREGISTRY) != Native.DISP_CHANGE_SUCCESSFUL)
                Log("Could not restore display mode for " + state.DisplayDevice);
        }
        catch (Exception ex) { Log("Could not restore display mode: " + ex.Message); }

        if (state.NvidiaScaling is int scaling)
            try { NvidiaScaling.Apply(state.DisplayDevice, scaling); }
            catch (Exception ex) { Log("Could not restore GPU scaling: " + ex.Message); }
        if (state.AmdScaling is not null)
            try { AmdScaling.Restore(state.AmdScaling); }
            catch (Exception ex) { Log("Could not restore AMD scaling: " + ex.Message); }
        if (state.IntelScaling is not null)
            try { IntelScaling.Restore(state.IntelScaling); }
            catch (Exception ex) { Log("Could not restore Intel scaling: " + ex.Message); }
        if (state.WindowsScaling is not null)
            try { WindowsScaling.Restore(state.WindowsScaling); }
            catch (Exception ex) { Log("Could not restore Windows scaling: " + ex.Message); }

    }

    private static void Log(string message)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DPT");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "recovery.log"), DateTime.Now + " " + message + Environment.NewLine);
        }
        catch { }
    }
}
