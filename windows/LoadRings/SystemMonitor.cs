using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace LoadRings;

public enum MetricKind { Cpu, Gpu, Ram, Ssd }

public static class MetricKindExtensions
{
    public static string Label(this MetricKind kind) => kind.ToString().ToUpperInvariant();
}

/// One gauge on the widget. Value is 0...1, or null when it can't be read on this machine.
public sealed record Metric(MetricKind Kind, double? Value, string Detail);

/// Samples CPU, GPU, memory and disk usage with Win32 APIs and performance counters.
public sealed class SystemMonitor
{
    private (long Idle, long Kernel, long User)? _previousCpu;
    private Dictionary<string, CounterSample> _previousGpu = new();
    private bool _gpuCountersAvailable = true;

    public IReadOnlyList<Metric> Sample()
    {
        var cpu = CpuUsage();
        var gpu = GpuUsage();
        var (memUsed, memTotal) = MemoryUsage();
        var (diskUsed, diskTotal) = DiskUsage();

        return new[]
        {
            new Metric(MetricKind.Cpu, cpu, cpu is double c ? $"CPU {Percent(c)}" : "CPU n/a"),
            new Metric(MetricKind.Gpu, gpu, gpu is double g ? $"GPU {Percent(g)}" : "GPU n/a"),
            new Metric(MetricKind.Ram, Ratio(memUsed, memTotal), $"RAM {Bytes(memUsed)} / {Bytes(memTotal)}"),
            new Metric(MetricKind.Ssd, Ratio(diskUsed, diskTotal), $"SSD {Bytes(diskUsed)} / {Bytes(diskTotal)}"),
        };
    }

    /// Share of non-idle time across all cores since the previous sample. Kernel time includes idle time.
    private double? CpuUsage()
    {
        if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user)) return null;
        var previous = _previousCpu;
        _previousCpu = (idle, kernel, user);
        if (previous is not { } p) return null;

        var total = (kernel - p.Kernel) + (user - p.User);
        var idleDelta = idle - p.Idle;
        return total > 0 ? Math.Clamp((double)(total - idleDelta) / total, 0, 1) : null;
    }

    /// Busiest GPU engine, summed over processes — the number Task Manager shows as "GPU".
    private double? GpuUsage()
    {
        if (!_gpuCountersAvailable) return null;
        InstanceDataCollection? samples;
        try
        {
            samples = new PerformanceCounterCategory("GPU Engine").ReadCategory()["Utilization Percentage"];
        }
        catch (Exception)
        {
            // Old drivers or Windows builds without GPU counters.
            _gpuCountersAvailable = false;
            return null;
        }
        if (samples is null) return null;

        var current = new Dictionary<string, CounterSample>();
        var perEngine = new Dictionary<string, double>();
        foreach (InstanceData instance in samples.Values)
        {
            current[instance.InstanceName] = instance.Sample;
            if (!_previousGpu.TryGetValue(instance.InstanceName, out var previous)) continue;

            // Instance names look like "pid_1234_luid_0x0_0x1A2B_phys_0_eng_3_engtype_3D".
            var name = instance.InstanceName;
            var luid = name.IndexOf("luid_", StringComparison.Ordinal);
            var engine = luid >= 0 ? name[luid..] : name;
            perEngine[engine] = perEngine.GetValueOrDefault(engine) + CounterSample.Calculate(previous, instance.Sample);
        }
        _previousGpu = current;

        return perEngine.Count > 0 ? Math.Clamp(perEngine.Values.Max() / 100, 0, 1) : null;
    }

    private static (ulong Used, ulong Total) MemoryUsage()
    {
        var status = new NativeMethods.MemoryStatusEx();
        if (!NativeMethods.GlobalMemoryStatusEx(status)) return (0, 0);
        return (status.TotalPhys - status.AvailPhys, status.TotalPhys);
    }

    private static (ulong Used, ulong Total) DiskUsage()
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
            return ((ulong)(drive.TotalSize - drive.TotalFreeSpace), (ulong)drive.TotalSize);
        }
        catch (Exception)
        {
            // Drive not ready or access denied.
            return (0, 0);
        }
    }

    private static double? Ratio(ulong used, ulong total) => total > 0 ? (double)used / total : null;

    private static string Percent(double value) => $"{Math.Round(value * 100)}%";

    private static string Bytes(ulong value) => $"{value / 1073741824.0:0.#} GB";
}
