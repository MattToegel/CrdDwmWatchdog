namespace CrdDwmWatchdog;

internal enum RestartAction
{
    Crd,
    Dwm,
    Both
}

internal sealed record ProcessMetrics(
    int Id,
    int SessionId,
    double CpuCorePercent,
    int Handles,
    int Threads,
    double WorkingSetMb,
    double PrivateMb,
    bool Responding);

internal sealed record CrdActivity(
    bool IsLikelyActive,
    int EstablishedRelayConnections,
    int UdpSockets,
    string Evidence);

internal sealed record HealthSample(
    DateTimeOffset Timestamp,
    ProcessMetrics? Dwm,
    IReadOnlyList<ProcessMetrics> CrdProcesses,
    ProcessMetrics? MainCrdProcess,
    CrdActivity CrdActivity,
    string? Error = null);

internal sealed record HealthAssessment(
    bool DwmProblem,
    bool CrdProblem,
    IReadOnlyList<string> Reasons)
{
    public bool HasProblem => DwmProblem || CrdProblem;
}

internal sealed record BaselineState(
    DateTimeOffset CapturedAt,
    double? DwmPrivateMb,
    int? DwmHandles,
    double? IdleCrdPrivateMb,
    int? IdleCrdHandles);

internal sealed record RestartResult(
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    RestartAction Action,
    bool Success,
    string Message,
    int? OldDwmPid,
    int? NewDwmPid,
    int VsCodeBefore,
    int VsCodeAfter,
    int DockerBefore,
    int DockerAfter,
    int TerminalBefore,
    int TerminalAfter);
