using System.Diagnostics;

namespace CrdDwmWatchdog;

internal sealed class HealthMonitor
{
    private readonly int _interactiveSessionId = Process.GetCurrentProcess().SessionId;
    private readonly Dictionary<int, CpuCheckpoint> _cpuCheckpoints = new();

    public Task<HealthSample> SampleAsync() => Task.Run(Sample);

    private HealthSample Sample()
    {
        try
        {
            var timestamp = DateTimeOffset.Now;
            ProcessMetrics? dwm = null;
            var dwmProcesses = Process.GetProcessesByName("dwm");
            try
            {
                var targetDwm = dwmProcesses.FirstOrDefault(process =>
                    SafeRead(() => process.SessionId, -1) == _interactiveSessionId);
                if (targetDwm is not null)
                {
                    dwm = ReadMetrics(targetDwm, timestamp);
                }
            }
            finally
            {
                foreach (var process in dwmProcesses)
                {
                    process.Dispose();
                }
            }

            var crdMetrics = new List<ProcessMetrics>();
            var crdProcesses = Process.GetProcessesByName("remoting_host");
            try
            {
                foreach (var process in crdProcesses)
                {
                    var metrics = ReadMetrics(process, timestamp);
                    if (metrics is not null)
                    {
                        crdMetrics.Add(metrics);
                    }
                }
            }
            finally
            {
                foreach (var process in crdProcesses)
                {
                    process.Dispose();
                }
            }

            var mainCrd = crdMetrics.OrderByDescending(process => process.PrivateMb).FirstOrDefault();
            var activity = NetstatInspector.Inspect(
                crdMetrics.Select(process => process.Id).ToArray(),
                mainCrd?.WorkingSetMb ?? 0);

            var sample = new HealthSample(timestamp, dwm, crdMetrics, mainCrd, activity);
            FileLog.AppendSample(sample);
            return sample;
        }
        catch (Exception ex)
        {
            FileLog.Write($"Health sample failed: {ex}");
            return new HealthSample(DateTimeOffset.Now, null, Array.Empty<ProcessMetrics>(), null,
                new CrdActivity(false, 0, 0, "Sampling failed."), ex.Message);
        }
    }

    private ProcessMetrics? ReadMetrics(Process process, DateTimeOffset timestamp)
    {
        try
        {
            process.Refresh();
            var id = process.Id;
            var totalCpu = SafeRead(() => process.TotalProcessorTime, TimeSpan.Zero);
            var cpuPercent = 0d;

            if (_cpuCheckpoints.TryGetValue(id, out var previous))
            {
                var elapsed = (timestamp - previous.Timestamp).TotalSeconds;
                var cpuSeconds = (totalCpu - previous.TotalCpu).TotalSeconds;
                if (elapsed > 0 && cpuSeconds >= 0)
                {
                    cpuPercent = Math.Clamp(cpuSeconds / elapsed * 100, 0, 10000);
                }
            }

            _cpuCheckpoints[id] = new CpuCheckpoint(timestamp, totalCpu);

            return new ProcessMetrics(
                id,
                SafeRead(() => process.SessionId, -1),
                Math.Round(cpuPercent, 1),
                SafeRead(() => process.HandleCount, 0),
                SafeRead(() => process.Threads.Count, 0),
                Math.Round(SafeRead(() => process.WorkingSet64, 0L) / 1024d / 1024d, 1),
                Math.Round(SafeRead(() => process.PrivateMemorySize64, 0L) / 1024d / 1024d, 1),
                SafeRead(() => process.Responding, true));
        }
        catch (Exception ex)
        {
            FileLog.Write($"Could not read process {SafeRead(() => process.ProcessName, "unknown")}: {ex.Message}");
            return null;
        }
    }

    private static T SafeRead<T>(Func<T> reader, T fallback)
    {
        try
        {
            return reader();
        }
        catch
        {
            return fallback;
        }
    }

    private sealed record CpuCheckpoint(DateTimeOffset Timestamp, TimeSpan TotalCpu);
}
