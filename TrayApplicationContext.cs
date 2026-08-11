using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace CrdDwmWatchdog;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly System.Windows.Forms.Timer _updateTimer;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _updateItem;
    private readonly HealthMonitor _monitor = new();
    private readonly UpdateService _updateService = new();
    private WatchdogSettings _settings;
    private BaselineState? _baseline;
    private HealthSample? _lastSample;
    private HealthAssessment? _lastAssessment;
    private bool _checking;
    private bool _checkingUpdates;
    private bool _firstTick = true;
    private int _dwmBadSamples;
    private int _crdBadSamples;
    private DateTimeOffset _snoozeUntil = DateTimeOffset.MinValue;
    private DateTimeOffset _lastAlert = DateTimeOffset.MinValue;
    private bool _remindWhenIdle;

    public TrayApplicationContext()
    {
        _settings = WatchdogSettings.Load();
        _baseline = BaselineStore.Load();

        _statusItem = new ToolStripMenuItem("Waiting for first health check…") { Enabled = false };
        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open status", null, (_, _) => OpenStatus());
        menu.Items.Add("Check now", null, async (_, _) => await CheckNowAsync(manual: true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Restart DWM…", null, async (_, _) => await RequestRestartAsync(RestartAction.Dwm));
        menu.Items.Add("Restart CRD…", null, async (_, _) => await RequestRestartAsync(RestartAction.Crd));
        menu.Items.Add("Restart both…", null, async (_, _) => await RequestRestartAsync(RestartAction.Both));
        menu.Items.Add(new ToolStripSeparator());
        _updateItem = new ToolStripMenuItem("Check for updates", null,
            async (_, _) => await CheckForUpdatesAsync(manual: true));
        menu.Items.Add(_updateItem);
        menu.Items.Add("Settings…", null, (_, _) => OpenSettings());
        menu.Items.Add("Open logs", null, (_, _) => OpenLogs());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "CRD & DWM Watchdog",
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => OpenStatus();

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += async (_, _) =>
        {
            if (_firstTick)
            {
                _firstTick = false;
                _timer.Interval = _settings.PollIntervalSeconds * 1000;
            }

            await CheckNowAsync(manual: false);
        };
        _timer.Start();

        _updateTimer = new System.Windows.Forms.Timer { Interval = 15_000 };
        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Interval = 6 * 60 * 60 * 1000;
            await CheckForUpdatesAsync(manual: false);
        };
        _updateTimer.Start();

        try
        {
            StartupRegistration.Apply(_settings.StartWithWindows);
        }
        catch (Exception ex)
        {
            FileLog.Write($"Could not update startup registration: {ex.Message}");
        }

        FileLog.Write("Watchdog started.");
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _timer.Dispose();
        _updateTimer.Stop();
        _updateTimer.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        FileLog.Write("Watchdog exited.");
        base.ExitThreadCore();
    }

    private async Task CheckNowAsync(bool manual)
    {
        if (_checking)
        {
            return;
        }

        _checking = true;
        _statusItem.Text = "Checking…";
        try
        {
            _lastSample = await _monitor.SampleAsync();
            _lastAssessment = HealthEvaluator.Evaluate(_lastSample, _settings, _baseline);
            UpdateStatusText();
            UpdateConsecutiveCounts();

            if (manual)
            {
                _notifyIcon.ShowBalloonTip(3000, "Health check complete",
                    BuildShortStatus(), ToolTipIcon.Info);
            }

            await MaybeShowAlertAsync();
        }
        catch (Exception ex)
        {
            FileLog.Write($"Health check failed: {ex}");
            _statusItem.Text = "Health check failed";
        }
        finally
        {
            _checking = false;
        }
    }

    private void UpdateStatusText()
    {
        if (_lastSample is null)
        {
            return;
        }

        var dwmText = _lastSample.Dwm is null ? "DWM unavailable" : $"DWM {_lastSample.Dwm.PrivateMb:N0} MB";
        var crdText = _lastSample.CrdActivity.IsLikelyActive ? "CRD active" : "CRD idle";
        var warning = _lastAssessment?.HasProblem == true ? "⚠ " : string.Empty;
        _statusItem.Text = $"{warning}{dwmText} • {crdText}";
        _notifyIcon.Text = Truncate($"CRD & DWM Watchdog — {dwmText}, {crdText}", 63);
    }

    private string BuildShortStatus()
    {
        if (_lastSample is null)
        {
            return "No sample is available yet.";
        }

        var dwm = _lastSample.Dwm is null ? "DWM unavailable" : $"DWM {_lastSample.Dwm.PrivateMb:N0} MB private";
        var crd = _lastSample.CrdActivity.IsLikelyActive ? "CRD session likely active" : "CRD likely idle";
        var assessment = _lastAssessment?.HasProblem == true ? "Threshold warning detected." : "No sustained warning.";
        return $"{dwm}; {crd}. {assessment}";
    }

    private void UpdateConsecutiveCounts()
    {
        _dwmBadSamples = _lastAssessment?.DwmProblem == true ? _dwmBadSamples + 1 : 0;
        _crdBadSamples = _lastAssessment?.CrdProblem == true ? _crdBadSamples + 1 : 0;
    }

    private async Task MaybeShowAlertAsync()
    {
        if (_lastSample is null || _lastAssessment is null || !_lastAssessment.HasProblem)
        {
            _remindWhenIdle = false;
            return;
        }

        var sustained = _dwmBadSamples >= _settings.ConsecutiveAlertSamples ||
                        _crdBadSamples >= _settings.ConsecutiveAlertSamples;
        if (!sustained)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var becameIdle = _remindWhenIdle && !_lastSample.CrdActivity.IsLikelyActive;
        if (!becameIdle && (now < _snoozeUntil ||
            now - _lastAlert < TimeSpan.FromMinutes(_settings.AlertCooldownMinutes)))
        {
            return;
        }

        _lastAlert = now;
        using var dialog = new AlertDialog(_lastSample, _lastAssessment);
        dialog.ShowDialog();

        if (dialog.RemindWhenIdleRequested)
        {
            _remindWhenIdle = true;
            FileLog.Write("Restart reminder deferred until CRD appears idle.");
        }
        else if (dialog.SnoozeRequested)
        {
            _snoozeUntil = now.AddMinutes(_settings.SnoozeMinutes);
            _remindWhenIdle = false;
            FileLog.Write($"Alerts snoozed until {_snoozeUntil.LocalDateTime:G}.");
        }
        else if (dialog.SelectedAction is { } action)
        {
            _remindWhenIdle = false;
            await RequestRestartAsync(action);
        }
    }

    private void OpenStatus()
    {
        using var form = new StatusForm(_lastSample, _lastAssessment, _baseline);
        form.ShowDialog();

        if (form.CheckNowRequested)
        {
            _ = CheckNowAsync(manual: true);
        }
        else if (form.CaptureBaselineRequested && _lastSample is not null)
        {
            var answer = MessageBox.Show(
                "Use the current readings as the clean baseline? Only do this when the UI feels healthy.",
                "Capture baseline", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer == DialogResult.Yes)
            {
                _baseline = BaselineStore.Capture(_lastSample);
            }
        }
        else if (form.SelectedAction is { } action)
        {
            _ = RequestRestartAsync(action);
        }
    }

    private void OpenSettings()
    {
        using var form = new SettingsForm(_settings);
        if (form.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        form.ApplyTo(_settings);
        _settings.Save();
        _timer.Interval = _settings.PollIntervalSeconds * 1000;

        try
        {
            StartupRegistration.Apply(_settings.StartWithWindows);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Settings were saved, but startup registration failed: {ex.Message}",
                "CRD & DWM Watchdog", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void OpenLogs()
    {
        AppPaths.EnsureDataDirectory();
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{AppPaths.DataDirectory}\"",
            UseShellExecute = true
        });
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_checkingUpdates)
        {
            return;
        }

        if (!_updateService.IsAvailable)
        {
            if (manual)
            {
                MessageBox.Show(
                    "Automatic updates are available after installing the watchdog with its Velopack Setup executable.",
                    "Updates unavailable in portable build",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            return;
        }

        _checkingUpdates = true;
        _updateItem.Text = "Checking for updates…";
        try
        {
            var version = await _updateService.CheckAndDownloadAsync();
            if (version is null)
            {
                if (manual)
                {
                    _notifyIcon.ShowBalloonTip(3000, "No watchdog update",
                        "This computer already has the latest stable version.", ToolTipIcon.Info);
                }
                return;
            }

            _updateItem.Text = $"Restart to install v{version}";
            var answer = MessageBox.Show(
                $"Watchdog version {version} is downloaded and ready. Restart only the watchdog now to install it?\n\n" +
                "Chrome Remote Desktop, DWM, VS Code, Docker, terminals, and your Windows session will not be restarted.",
                "Watchdog update ready",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1);
            if (answer == DialogResult.Yes && _updateService.BeginApplyAndRestart())
            {
                ExitThread();
            }
        }
        catch (Exception ex)
        {
            FileLog.Write($"Update check failed: {ex}");
            if (manual)
            {
                MessageBox.Show(ex.Message, "Update check failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _checkingUpdates = false;
            _updateItem.Text = _updateService.PendingVersion is { } pending
                ? $"Restart to install v{pending}"
                : "Check for updates";
        }
    }

    private async Task RequestRestartAsync(RestartAction action)
    {
        if (_lastSample?.CrdActivity.IsLikelyActive == true)
        {
            var activeAnswer = MessageBox.Show(
                "A Chrome Remote Desktop media session appears active. Restarting CRD will disconnect it, " +
                "and restarting DWM may briefly blank or interrupt the remote display.\n\nContinue now?",
                "Active CRD session", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (activeAnswer != DialogResult.Yes)
            {
                _remindWhenIdle = true;
                return;
            }
        }

        var interruptionNote = action switch
        {
            RestartAction.Crd => "Any active CRD session will disconnect briefly.",
            RestartAction.Dwm => "The display may flicker or go black briefly.",
            _ => "Any active CRD session will disconnect and the display may briefly go black."
        };
        var confirmation = MessageBox.Show(
            $"Restart {ActionLabel(action)}?\n\n{interruptionNote}\n\n" +
            "The helper targets only CRD and/or DWM. VS Code, Docker, terminals, and the Windows login session remain running.",
            "Confirm guarded restart", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (confirmation != DialogResult.Yes)
        {
            return;
        }

        var startedAt = DateTimeOffset.Now;
        try
        {
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("Could not determine the watchdog executable path.");
            using var helper = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = $"--elevated-restart {action.ToString().ToLowerInvariant()}",
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            });

            if (helper is null)
            {
                throw new InvalidOperationException("The elevated restart helper did not start.");
            }

            await helper.WaitForExitAsync();
            var result = ReadFreshRestartResult(startedAt);
            var succeeded = helper.ExitCode == 0 && result?.Success == true;
            var message = result?.Message ?? $"Restart helper exited with code {helper.ExitCode}.";

            MessageBox.Show(message,
                succeeded ? "Restart completed" : "Restart failed",
                MessageBoxButtons.OK,
                succeeded ? MessageBoxIcon.Information : MessageBoxIcon.Error);

            if (succeeded)
            {
                _dwmBadSamples = 0;
                _crdBadSamples = 0;
                _lastAlert = DateTimeOffset.Now;
                await Task.Delay(5000);
                _lastSample = await _monitor.SampleAsync();
                _lastAssessment = HealthEvaluator.Evaluate(_lastSample, _settings, _baseline);
                _baseline = BaselineStore.Capture(_lastSample);
                UpdateStatusText();
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            FileLog.Write("User canceled the UAC prompt.");
        }
        catch (Exception ex)
        {
            FileLog.Write($"Could not launch restart helper: {ex}");
            MessageBox.Show(ex.Message, "Restart failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static RestartResult? ReadFreshRestartResult(DateTimeOffset startedAt)
    {
        try
        {
            if (!File.Exists(AppPaths.RestartResultPath))
            {
                return null;
            }

            var result = JsonSerializer.Deserialize<RestartResult>(
                File.ReadAllText(AppPaths.RestartResultPath), AppPaths.JsonOptions);
            return result is not null && result.FinishedAt >= startedAt.AddSeconds(-2) ? result : null;
        }
        catch (Exception ex)
        {
            FileLog.Write($"Could not read restart result: {ex.Message}");
            return null;
        }
    }

    private static string ActionLabel(RestartAction action) => action switch
    {
        RestartAction.Crd => "Chrome Remote Desktop",
        RestartAction.Dwm => "Desktop Window Manager",
        _ => "Chrome Remote Desktop and Desktop Window Manager"
    };

    private static string Truncate(string text, int length) =>
        text.Length <= length ? text : text[..(length - 1)] + "…";
}
