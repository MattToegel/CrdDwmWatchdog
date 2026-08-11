namespace CrdDwmWatchdog;

internal static class SelfTest
{
    public static int Run()
    {
        var failures = new List<string>();

        Check("Idle heartbeat is not an active session", failures, () =>
        {
            var activity = NetstatInspector.ParseLines(new[]
            {
                "TCP 192.168.1.10:50000 142.250.1.1:443 ESTABLISHED 100",
                "UDP 0.0.0.0:50001 *:* 100"
            }, new[] { 100 }, 20);
            return !activity.IsLikelyActive;
        });

        Check("Port 3478 identifies an active media session", failures, () =>
        {
            var activity = NetstatInspector.ParseLines(new[]
            {
                "TCP 192.168.1.10:50000 74.125.1.1:3478 ESTABLISHED 100"
            }, new[] { 100 }, 20);
            return activity.IsLikelyActive && activity.EstablishedRelayConnections == 1;
        });

        Check("Media UDP fallback identifies likely activity", failures, () =>
        {
            var lines = Enumerable.Range(1, 5).Select(index => $"UDP 0.0.0.0:{50000 + index} *:* 100");
            return NetstatInspector.ParseLines(lines, new[] { 100 }, 150).IsLikelyActive;
        });

        Check("DWM threshold produces a warning", failures, () =>
        {
            var settings = new WatchdogSettings { DwmPrivateMbThreshold = 2500 };
            var sample = Sample(dwmPrivateMb: 3000, crdPrivateMb: 10, crdActive: false);
            return HealthEvaluator.Evaluate(sample, settings, null).DwmProblem;
        });

        Check("Active CRD load is not treated as idle leakage", failures, () =>
        {
            var settings = new WatchdogSettings { IdleCrdPrivateMbThreshold = 200 };
            var sample = Sample(dwmPrivateMb: 1000, crdPrivateMb: 500, crdActive: true);
            return !HealthEvaluator.Evaluate(sample, settings, null).CrdProblem;
        });

        if (failures.Count == 0)
        {
            Console.WriteLine("All self-tests passed.");
            return 0;
        }

        Console.Error.WriteLine("Self-test failures:");
        foreach (var failure in failures)
        {
            Console.Error.WriteLine("- " + failure);
        }
        return 1;
    }

    private static HealthSample Sample(double dwmPrivateMb, double crdPrivateMb, bool crdActive)
    {
        var dwm = new ProcessMetrics(1, 1, 0, 100, 10, 100, dwmPrivateMb, true);
        var crd = new ProcessMetrics(2, 0, 0, 100, 10, 100, crdPrivateMb, true);
        return new HealthSample(DateTimeOffset.Now, dwm, new[] { crd }, crd,
            new CrdActivity(crdActive, crdActive ? 1 : 0, crdActive ? 4 : 1, "test"));
    }

    private static void Check(string name, ICollection<string> failures, Func<bool> assertion)
    {
        try
        {
            if (assertion())
            {
                Console.WriteLine("PASS: " + name);
            }
            else
            {
                failures.Add(name);
            }
        }
        catch (Exception ex)
        {
            failures.Add($"{name}: {ex.Message}");
        }
    }
}
