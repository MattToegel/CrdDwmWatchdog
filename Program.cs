using System.Text.Json;
using Velopack;

namespace CrdDwmWatchdog;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            VelopackApp.Build()
                .SetAutoApplyOnStartup(false)
                .Run();

            if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
            {
                ConsoleBridge.AttachToParent();
                return SelfTest.Run();
            }

            if (args.Contains("--sample-once", StringComparer.OrdinalIgnoreCase))
            {
                ConsoleBridge.AttachToParent();
                var monitor = new HealthMonitor();
                var sample = monitor.SampleAsync().GetAwaiter().GetResult();
                Console.WriteLine(JsonSerializer.Serialize(sample, AppPaths.JsonOptions));
                return string.IsNullOrWhiteSpace(sample.Error) ? 0 : 1;
            }

            var dryRunIndex = Array.FindIndex(args,
                value => value.Equals("--dry-run-restart", StringComparison.OrdinalIgnoreCase));
            if (dryRunIndex >= 0)
            {
                ConsoleBridge.AttachToParent();
                var action = ParseAction(args.ElementAtOrDefault(dryRunIndex + 1));
                Console.WriteLine(RestartCoordinator.DescribeDryRun(action));
                return 0;
            }

            var elevatedIndex = Array.FindIndex(args,
                value => value.Equals("--elevated-restart", StringComparison.OrdinalIgnoreCase));
            if (elevatedIndex >= 0)
            {
                var action = ParseAction(args.ElementAtOrDefault(elevatedIndex + 1));
                var result = RestartCoordinator.ExecuteElevated(action);
                return result.Success ? 0 : 1;
            }

            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, eventArgs) =>
            {
                FileLog.Write($"Unhandled UI exception: {eventArgs.Exception}");
                MessageBox.Show(eventArgs.Exception.Message, "CRD & DWM Watchdog",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            using var mutex = new Mutex(initiallyOwned: true, @"Local\CrdDwmWatchdog", out var createdNew);
            if (!createdNew)
            {
                MessageBox.Show("CRD & DWM Watchdog is already running in the notification area.",
                    "CRD & DWM Watchdog", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }

            Application.Run(new TrayApplicationContext());
            return 0;
        }
        catch (Exception ex)
        {
            FileLog.Write($"Fatal error: {ex}");
            if (Environment.UserInteractive)
            {
                try
                {
                    MessageBox.Show(ex.Message, "CRD & DWM Watchdog",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch
                {
                }
            }
            return 1;
        }
    }

    private static RestartAction ParseAction(string? value) => value?.ToLowerInvariant() switch
    {
        "crd" => RestartAction.Crd,
        "dwm" => RestartAction.Dwm,
        "both" => RestartAction.Both,
        _ => throw new ArgumentException("Restart action must be crd, dwm, or both.")
    };
}
