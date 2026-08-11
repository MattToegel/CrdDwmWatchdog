using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CrdDwmWatchdog;

internal static partial class RestartCoordinator
{
    private const string ServiceName = "chromoting";

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static string DescribeDryRun(RestartAction action)
    {
        var sessionId = Process.GetCurrentProcess().SessionId;
        var dwm = Process.GetProcessesByName("dwm")
            .FirstOrDefault(process => SafeRead(() => process.SessionId, -1) == sessionId);
        var crd = Process.GetProcessesByName("remoting_host");

        try
        {
            return $"Action: {action}{Environment.NewLine}" +
                   $"Interactive session: {sessionId}{Environment.NewLine}" +
                   $"Target DWM PID: {(dwm is null ? "not found" : dwm.Id)}{Environment.NewLine}" +
                   $"CRD process IDs: {string.Join(", ", crd.Select(process => process.Id))}{Environment.NewLine}" +
                   "No processes or services were changed.";
        }
        finally
        {
            dwm?.Dispose();
            foreach (var process in crd)
            {
                process.Dispose();
            }
        }
    }

    public static RestartResult ExecuteElevated(RestartAction action)
    {
        var startedAt = DateTimeOffset.Now;
        var before = GetWorkloadCounts();
        var success = false;
        var message = "Restart did not complete.";
        int? oldDwmPid = null;
        int? newDwmPid = null;

        try
        {
            if (!IsAdministrator())
            {
                throw new InvalidOperationException("The restart helper was not elevated.");
            }

            switch (action)
            {
                case RestartAction.Crd:
                    RestartCrdService();
                    break;
                case RestartAction.Dwm:
                    (oldDwmPid, newDwmPid) = RestartDwm();
                    break;
                case RestartAction.Both:
                    StopCrdService();
                    try
                    {
                        (oldDwmPid, newDwmPid) = RestartDwm();
                    }
                    finally
                    {
                        StartCrdService();
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action));
            }

            if ((action is RestartAction.Crd or RestartAction.Both) &&
                GetServiceState() != ServiceState.Running)
            {
                throw new InvalidOperationException("Chrome Remote Desktop did not return to Running.");
            }

            success = true;
            message = action switch
            {
                RestartAction.Crd => "Chrome Remote Desktop restarted successfully.",
                RestartAction.Dwm => "Desktop Window Manager restarted successfully.",
                _ => "Chrome Remote Desktop and Desktop Window Manager restarted successfully."
            };
        }
        catch (Exception ex)
        {
            message = ex.Message;
            FileLog.Write($"Elevated restart failed: {ex}");

            if (action is RestartAction.Crd or RestartAction.Both)
            {
                try
                {
                    StartCrdService();
                }
                catch (Exception recoveryError)
                {
                    message += $" CRD recovery also failed: {recoveryError.Message}";
                }
            }
        }

        var after = GetWorkloadCounts();
        var result = new RestartResult(
            startedAt,
            DateTimeOffset.Now,
            action,
            success,
            message,
            oldDwmPid,
            newDwmPid,
            before.VsCode,
            after.VsCode,
            before.Docker,
            after.Docker,
            before.Terminal,
            after.Terminal);

        WriteRestartResult(result);
        return result;
    }

    private static void RestartCrdService()
    {
        StopCrdService();
        StartCrdService();
    }

    private static void StopCrdService()
    {
        var state = GetServiceState();
        if (state == ServiceState.Stopped)
        {
            return;
        }

        RunSc($"stop {ServiceName}", allowFailure: state == ServiceState.StopPending);
        WaitForServiceState(ServiceState.Stopped, TimeSpan.FromSeconds(30));
    }

    private static void StartCrdService()
    {
        var state = GetServiceState();
        if (state == ServiceState.Running)
        {
            return;
        }

        RunSc($"start {ServiceName}", allowFailure: state == ServiceState.StartPending);
        WaitForServiceState(ServiceState.Running, TimeSpan.FromSeconds(30));
    }

    private static (int OldPid, int NewPid) RestartDwm()
    {
        var sessionId = Process.GetCurrentProcess().SessionId;
        using var oldDwm = Process.GetProcessesByName("dwm")
            .FirstOrDefault(process => SafeRead(() => process.SessionId, -1) == sessionId)
            ?? throw new InvalidOperationException($"No DWM process was found in session {sessionId}.");

        var oldPid = oldDwm.Id;
        oldDwm.Kill(entireProcessTree: false);
        oldDwm.WaitForExit(15000);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            Thread.Sleep(500);
            var candidates = Process.GetProcessesByName("dwm");
            try
            {
                var replacement = candidates.FirstOrDefault(process =>
                    process.Id != oldPid && SafeRead(() => process.SessionId, -1) == sessionId);
                if (replacement is not null)
                {
                    var newPid = replacement.Id;
                    Thread.Sleep(2500);
                    return (oldPid, newPid);
                }
            }
            finally
            {
                foreach (var candidate in candidates)
                {
                    candidate.Dispose();
                }
            }
        }

        throw new InvalidOperationException("Windows did not recreate Desktop Window Manager within 30 seconds.");
    }

    private static ServiceState GetServiceState()
    {
        var result = RunSc($"query {ServiceName}", allowFailure: false);
        var match = ServiceStateRegex().Match(result.Output);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var state))
        {
            throw new InvalidOperationException("Could not determine Chrome Remote Desktop service state.");
        }

        return Enum.IsDefined(typeof(ServiceState), state)
            ? (ServiceState)state
            : ServiceState.Unknown;
    }

    private static void WaitForServiceState(ServiceState desired, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (GetServiceState() == desired)
            {
                return;
            }

            Thread.Sleep(500);
        }

        throw new TimeoutException($"The {ServiceName} service did not reach {desired} within {timeout.TotalSeconds:N0} seconds.");
    }

    private static ProcessResult RunSc(string arguments, bool allowFailure)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "sc.exe"),
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Could not start sc.exe.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(10000))
        {
            process.Kill();
            throw new TimeoutException($"sc.exe {arguments} timed out.");
        }

        if (process.ExitCode != 0 && !allowFailure)
        {
            throw new InvalidOperationException(
                $"sc.exe {arguments} failed with exit code {process.ExitCode}: {output} {error}".Trim());
        }

        return new ProcessResult(process.ExitCode, output, error);
    }

    private static WorkloadCounts GetWorkloadCounts() => new(
        Process.GetProcessesByName("Code").Length,
        Process.GetProcessesByName("Docker Desktop").Length +
        Process.GetProcessesByName("com.docker.backend").Length,
        Process.GetProcessesByName("WindowsTerminal").Length);

    private static void WriteRestartResult(RestartResult result)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.SharedDirectory);
            File.WriteAllText(AppPaths.RestartResultPath,
                JsonSerializer.Serialize(result, AppPaths.JsonOptions));
        }
        catch (Exception ex)
        {
            FileLog.Write($"Could not write restart result: {ex.Message}");
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

    private sealed record WorkloadCounts(int VsCode, int Docker, int Terminal);
    private sealed record ProcessResult(int ExitCode, string Output, string Error);

    private enum ServiceState
    {
        Unknown = 0,
        Stopped = 1,
        StartPending = 2,
        StopPending = 3,
        Running = 4,
        ContinuePending = 5,
        PausePending = 6,
        Paused = 7
    }

    [GeneratedRegex(@"STATE\s*:\s*(\d+)")]
    private static partial Regex ServiceStateRegex();
}
