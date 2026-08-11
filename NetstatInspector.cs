using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CrdDwmWatchdog;

internal static partial class NetstatInspector
{
    public static CrdActivity Inspect(IReadOnlyCollection<int> processIds, double mainWorkingSetMb)
    {
        if (processIds.Count == 0)
        {
            return new CrdActivity(false, 0, 0, "CRD is not running.");
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "netstat.exe"),
                Arguments = "-ano",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return new CrdActivity(false, 0, 0, "Could not start netstat.");
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return ParseLines(output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries),
                processIds, mainWorkingSetMb);
        }
        catch (Exception ex)
        {
            FileLog.Write($"CRD activity detection failed: {ex.Message}");
            return new CrdActivity(false, 0, 0, "CRD activity could not be determined.");
        }
    }

    internal static CrdActivity ParseLines(
        IEnumerable<string> lines,
        IReadOnlyCollection<int> processIds,
        double mainWorkingSetMb)
    {
        var idSet = processIds.ToHashSet();
        var relayConnections = 0;
        var udpSockets = 0;

        foreach (var line in lines)
        {
            var fields = WhitespaceRegex().Split(line.Trim());
            if (fields.Length < 4)
            {
                continue;
            }

            if (fields[0].Equals("TCP", StringComparison.OrdinalIgnoreCase) && fields.Length >= 5)
            {
                if (!int.TryParse(fields[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var pid) ||
                    !idSet.Contains(pid))
                {
                    continue;
                }

                var remotePort = GetPort(fields[2]);
                var established = fields[3].Equals("ESTABLISHED", StringComparison.OrdinalIgnoreCase);
                if (established && remotePort == 3478)
                {
                    relayConnections++;
                }
            }
            else if (fields[0].Equals("UDP", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(fields[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var pid) &&
                    idSet.Contains(pid))
                {
                    udpSockets++;
                }
            }
        }

        var active = relayConnections > 0 || (udpSockets >= 4 && mainWorkingSetMb >= 100);
        var evidence = active
            ? $"Likely active: {relayConnections} established port-3478 connection(s), {udpSockets} UDP media socket(s)."
            : $"Likely idle: no established port-3478 connection; {udpSockets} CRD UDP socket(s).";

        return new CrdActivity(active, relayConnections, udpSockets, evidence);
    }

    private static int? GetPort(string endpoint)
    {
        var separator = endpoint.LastIndexOf(':');
        if (separator < 0 || separator == endpoint.Length - 1)
        {
            return null;
        }

        return int.TryParse(endpoint[(separator + 1)..], NumberStyles.None,
            CultureInfo.InvariantCulture, out var port)
            ? port
            : null;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
